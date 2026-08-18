using System;
using System.Collections.Generic;
using UnityEngine;
using XX;

namespace Polaris.Drawing.Internal
{
    /// <summary>
    /// Screen 后端：GUI Layer 上的一个 <c>MultiMeshRenderer</c> 宿主，每个可见节点各自拥有一个
    /// <c>MeshDrawer</c>（通过 <c>Make(...)</c> 得到，同时也是它自己的子 GameObject）。
    /// 节点的位置/旋转/缩放/深度都是那个子 GameObject 的 Unity Transform，不重新三角化；
    /// 不透明度通过重新推送已烘焙好的顶点颜色数组实现，同样不重新三角化。
    /// 文本命令是各自独立、可见的 <c>TextRenderer</c> 子物体，直接摆放，不经过网格复制。
    /// </summary>
    internal sealed class ScreenDrawingBackend : IDrawingBackend
    {
        const float SurfaceZStep = -0.001f;
        const float NodeZStep = -0.00002f;

        sealed class TextSlotState
        {
            internal TextMeshCacheEntry Entry;
            internal DrawPoint LocalPosition;
            internal Matrix4x4 LocalMatrix;
            internal float BakedOpacity;
        }

        sealed class NodeState
        {
            internal MeshDrawer ShapeDrawer;
            internal GameObject ShapeGob;
            internal Color32[] BaseColors = Array.Empty<Color32>();
            internal bool HasShapeContent;
            internal readonly List<TextSlotState> Texts = new();
            internal DrawTransform Transform = DrawTransform.Identity;
            internal float Opacity = 1f;
            internal bool Visible = true;
            internal int Order;
            internal float ComputedZ;
        }

        readonly DrawPlane plane;
        readonly int surfaceOrder;
        readonly string debugName;
        readonly Dictionary<int, NodeState> nodes = new();
        readonly List<int> visibleInDrawOrder = new();
        readonly Func<int, int> nodeOrderOf;
        readonly Func<int, bool> nodeVisibleOf;

        GameObject host;
        MultiMeshRenderer meshes;
        bool built;
        bool disposed;
        bool surfaceVisible = true;

        internal ScreenDrawingBackend(DrawPlane plane, int surfaceOrder, string debugName)
        {
            this.plane = plane;
            this.surfaceOrder = surfaceOrder;
            this.debugName = debugName;
            // 缓存这两个查表委托：Relayout 可能每帧被节点可见性/顺序变化触发，不要每次都新建闭包。
            nodeOrderOf = id => nodes[id].Order;
            nodeVisibleOf = id => nodes[id].Visible;
        }

        public void SetSurfacePosition(DrawPoint position)
        {
            if (host != null)
            {
                host.transform.localPosition = new Vector3(position.X, position.Y, host.transform.localPosition.z);
            }
        }

        public void SetSurfaceVisible(bool visible)
        {
            // host 本身永远保持 active：如果连它一起关掉，之后再往它下面挂新的 MeshDrawer/TextRenderer
            // 子物体时会因为祖先 inactive 而不跑 Awake，Size/Align/Redraw 那一套调用会因为内部状态
            // 没初始化而出问题。真正的隐藏落在每个节点自己的 GameObject 上（见 ApplyActive）。
            surfaceVisible = visible;
            foreach (NodeState state in nodes.Values)
            {
                ApplyActive(state);
            }
        }

        public void CreateNode(int nodeId) => nodes[nodeId] = new NodeState();

        public void RebuildNode(int nodeId, IReadOnlyList<DrawOp> ops)
        {
            if (disposed || !nodes.TryGetValue(nodeId, out NodeState state))
            {
                return;
            }

            EnsureReady();
            if (state.ShapeDrawer == null)
            {
                state.ShapeDrawer = meshes.Make(MTRX.MtrMeshNormal);
                state.ShapeGob = meshes.GetGob(state.ShapeDrawer);
            }
            else
            {
                state.ShapeDrawer.clear();
            }

            List<BakedTextOp> texts = GeometryCache.Bake(state.ShapeDrawer, ops);
            state.ShapeDrawer.updateForMeshRenderer();
            state.HasShapeContent = state.ShapeDrawer.exist_content;

            int vertexMax = state.ShapeDrawer.getVertexMax();
            state.BaseColors = new Color32[vertexMax];
            Array.Copy(state.ShapeDrawer.getColorArray(), state.BaseColors, vertexMax);

            ReconcileTexts(state, texts, nodeId);
            ApplyVisualState(state);
        }

        public void SetTransform(int nodeId, DrawTransform transform)
        {
            NodeState state = nodes[nodeId];
            state.Transform = transform;
            if (built)
            {
                ApplyVisualState(state);
            }
        }

        public void SetOpacity(int nodeId, float opacity)
        {
            NodeState state = nodes[nodeId];
            state.Opacity = opacity;
            if (built)
            {
                RecolorShape(state);
                ApplyTextOpacity(state);
            }
        }

        public void SetVisible(int nodeId, bool visible)
        {
            NodeState state = nodes[nodeId];
            state.Visible = visible;
            if (built)
            {
                ApplyActive(state);
            }
        }

        public void SetOrder(int nodeId, int order)
        {
            nodes[nodeId].Order = order;
        }

        public void DestroyNode(int nodeId)
        {
            if (!nodes.TryGetValue(nodeId, out NodeState state))
            {
                return;
            }

            if (state.ShapeGob != null)
            {
                UnityEngine.Object.Destroy(state.ShapeGob);
            }
            foreach (TextSlotState text in state.Texts)
            {
                text.Entry.Dispose();
            }
            nodes.Remove(nodeId);
        }

        public void Relayout(IReadOnlyList<int> nodeIdsInOrder)
        {
            if (!built)
            {
                return;
            }

            DrawBatchBuilder.Order(nodeIdsInOrder, nodeOrderOf, nodeVisibleOf, visibleInDrawOrder);

            // host 自己的 Transform 已经携带了 Plane/SurfaceOrder 的那部分 Z（见 EnsureReady），
            // 这里只需要在节点之间分配一点相对偏移，否则会把 host 的基准 Z 重复叠加一遍。
            for (int i = 0; i < visibleInDrawOrder.Count; i++)
            {
                NodeState state = nodes[visibleInDrawOrder[i]];
                state.ComputedZ = NodeZStep * i;
                ApplyVisualState(state);
            }
        }

        public void Pump(float deltaSeconds)
        {
            // Screen 节点全部是事件驱动（属性 setter 直接生效），不需要每帧轮询。
        }

        public DrawRect ComputeBounds(IReadOnlyList<int> nodeIds)
        {
            var bounds = new DrawBoundsBuilder();
            foreach (int id in nodeIds)
            {
                if (nodes.TryGetValue(id, out NodeState state))
                {
                    bounds.Include(state.ShapeDrawer, state.Transform);
                }
            }
            return bounds.ToRect();
        }

        public DrawingDebugStats.BackendStats GetStats()
        {
            int vertices = 0, triangles = 0, textCount = 0;
            foreach (NodeState state in nodes.Values)
            {
                if (state.ShapeDrawer != null)
                {
                    vertices += state.ShapeDrawer.getVertexMax();
                    triangles += state.ShapeDrawer.getTriMax() / 3;
                }
                textCount += state.Texts.Count;
            }
            return new DrawingDebugStats.BackendStats(nodes.Count, vertices, triangles, textCount, mapCallbacks: 0);
        }

        public void Dispose()
        {
            disposed = true;
            foreach (NodeState state in nodes.Values)
            {
                if (state.ShapeGob != null)
                {
                    UnityEngine.Object.Destroy(state.ShapeGob);
                }
                foreach (TextSlotState text in state.Texts)
                {
                    text.Entry.Dispose();
                }
            }
            nodes.Clear();

            if (host != null)
            {
                UnityEngine.Object.Destroy(host);
            }
            host = null;
            meshes = null;
            built = false;
        }

        /// <summary>
        /// 懒建 GUI Layer 上的宿主。调用方（<see cref="DrawingSurfaceRuntime.RunBuild"/>）已经通过
        /// <c>GameSessionRuntime.WhenReady</c> 保证了此时 MTRX/字体已经就绪，这里不用再判 <c>IsReady</c>。
        /// </summary>
        void EnsureReady()
        {
            if (built)
            {
                return;
            }

            int guiLayer = LayerMask.NameToLayer(IN.gui_layer_name);
            if (guiLayer < 0)
            {
                throw new InvalidOperationException($"GUI layer was not found: {IN.gui_layer_name}");
            }

            host = new GameObject("Polaris.Drawing.Screen." + debugName)
            {
                layer = guiLayer,
            };
            UnityEngine.Object.DontDestroyOnLoad(host);
            IN.setZ(host.transform, ComputePlaneBaseZ(plane) + surfaceOrder * SurfaceZStep);

            meshes = host.AddComponent<MultiMeshRenderer>();
            meshes.BaseZ(0f);
            meshes.use_valotile = true;
            meshes.valotile_enabled = true;

            built = true;
        }

        static float ComputePlaneBaseZ(DrawPlane plane) => plane switch
        {
            DrawPlane.Background => -0.9f,
            DrawPlane.Hud => -0.5f,
            DrawPlane.Overlay => -0.1f,
            _ => throw new NotSupportedException($"{plane} is not a valid Screen plane."),
        };

        void ReconcileTexts(NodeState state, List<BakedTextOp> texts, int nodeId)
        {
            while (state.Texts.Count > texts.Count)
            {
                int last = state.Texts.Count - 1;
                state.Texts[last].Entry.Dispose();
                state.Texts.RemoveAt(last);
            }

            for (int i = 0; i < texts.Count; i++)
            {
                BakedTextOp baked = texts[i];
                if (i >= state.Texts.Count)
                {
                    var slot = new TextSlotState
                    {
                        Entry = new TextMeshCacheEntry(
                            host, host.layer, $"Polaris.Drawing.Screen.{debugName}.Node{nodeId}.Text{i}", visible: true),
                    };
                    state.Texts.Add(slot);
                }

                TextSlotState text = state.Texts[i];
                text.Entry.UpdateContent(baked.Text, baked.Style);
                text.LocalPosition = baked.LocalPosition;
                text.LocalMatrix = baked.LocalMatrix;
                text.BakedOpacity = baked.Opacity;
            }
        }

        void ApplyVisualState(NodeState state)
        {
            ApplyActive(state);

            if (state.ShapeGob != null)
            {
                state.ShapeGob.transform.localPosition = new Vector3(
                    state.Transform.Translation.X, state.Transform.Translation.Y, state.ComputedZ);
                state.ShapeGob.transform.localRotation = Quaternion.Euler(0f, 0f, state.Transform.Rotation * Mathf.Rad2Deg);
                state.ShapeGob.transform.localScale = new Vector3(state.Transform.ScaleX, state.Transform.ScaleY, 1f);
                RecolorShape(state);
            }

            Matrix4x4 nodeMatrix = DrawNodeGeometry.ToMatrix(state.Transform);
            foreach (TextSlotState text in state.Texts)
            {
                Matrix4x4 full = nodeMatrix * text.LocalMatrix;
                Vector3 pos = full.MultiplyPoint3x4(new Vector3(text.LocalPosition.X, text.LocalPosition.Y, 0f));
                Transform t = text.Entry.Host.transform;
                t.localPosition = new Vector3(pos.x, pos.y, state.ComputedZ);
                t.localRotation = full.rotation;
                Vector3 scale = full.lossyScale;
                t.localScale = new Vector3(scale.x, scale.y, 1f);
            }
            ApplyTextOpacity(state);
        }

        void ApplyActive(NodeState state)
        {
            bool visible = state.Visible && surfaceVisible;
            if (state.ShapeGob != null)
            {
                state.ShapeGob.SetActive(visible && state.HasShapeContent);
            }
            foreach (TextSlotState text in state.Texts)
            {
                if (!text.Entry.IsDisposed)
                {
                    text.Entry.Host.SetActive(visible);
                }
            }
        }

        void RecolorShape(NodeState state)
        {
            if (state.ShapeDrawer == null || state.BaseColors.Length == 0)
            {
                return;
            }

            Color32[] live = state.ShapeDrawer.getColorArray();
            int count = Mathf.Min(live.Length, state.BaseColors.Length);
            float opacity = Mathf.Clamp01(state.Opacity);
            for (int i = 0; i < count; i++)
            {
                Color32 c = state.BaseColors[i];
                c.a = (byte)(opacity * c.a);
                live[i] = c;
            }
            state.ShapeDrawer.updateForMeshRenderer();
        }

        void ApplyTextOpacity(NodeState state)
        {
            float opacity = Mathf.Clamp01(state.Opacity);
            foreach (TextSlotState text in state.Texts)
            {
                text.Entry.Renderer.Alpha(Mathf.Clamp01(opacity * text.BakedOpacity));
            }
        }
    }
}
