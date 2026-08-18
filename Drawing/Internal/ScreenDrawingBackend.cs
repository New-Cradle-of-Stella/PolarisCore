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
    /// 图片命令也各自拥有一个 <c>MeshDrawer</c>（贴图不同不能共享材质），四个角在烘焙时就已经乘过
    /// PushTransform 折出来的矩阵，运行时只需要像形状一样挪子物体的 Unity Transform。
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

        sealed class ImageSlotState
        {
            internal GameObject Gob;
            internal MeshDrawer Drawer;
            internal Material Material;
            internal Texture2D LastTexture;
            internal Color32[] BaseColors = new Color32[4];
        }

        sealed class NodeState
        {
            internal MeshDrawer ShapeDrawer;
            internal GameObject ShapeGob;
            internal Color32[] BaseColors = Array.Empty<Color32>();
            internal bool HasShapeContent;
            internal readonly List<TextSlotState> Texts = new();
            internal readonly List<ImageSlotState> Images = new();
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

            (List<BakedTextOp> texts, List<BakedImageOp> images) = GeometryCache.Bake(state.ShapeDrawer, ops);
            state.ShapeDrawer.updateForMeshRenderer();
            state.HasShapeContent = state.ShapeDrawer.exist_content;

            int vertexMax = state.ShapeDrawer.getVertexMax();
            state.BaseColors = new Color32[vertexMax];
            Array.Copy(state.ShapeDrawer.getColorArray(), state.BaseColors, vertexMax);

            ReconcileTexts(state, texts, nodeId);
            ReconcileImages(state, images, nodeId);
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
                RecolorImages(state);
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
            foreach (ImageSlotState image in state.Images)
            {
                DestroyImageSlot(image);
            }
            nodes.Remove(nodeId);
        }

        public void Relayout(IReadOnlyList<int> nodeIdsInOrder)
        {
            if (!built)
            {
                return;
            }

            // host 自己的 Transform 已经携带了 Plane/SurfaceOrder 的那部分 Z（见 EnsureReady），
            // 这里只需要在节点之间分配一点相对偏移，否则会把 host 的基准 Z 重复叠加一遍。
            DrawBatchBuilder.Order(nodeIdsInOrder, nodeOrderOf, nodeVisibleOf, visibleInDrawOrder);

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
                foreach (ImageSlotState image in state.Images)
                {
                    vertices += image.Drawer.getVertexMax();
                    triangles += image.Drawer.getTriMax() / 3;
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
                foreach (ImageSlotState image in state.Images)
                {
                    DestroyImageSlot(image);
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

        void ReconcileImages(NodeState state, List<BakedImageOp> images, int nodeId)
        {
            while (state.Images.Count > images.Count)
            {
                int last = state.Images.Count - 1;
                DestroyImageSlot(state.Images[last]);
                state.Images.RemoveAt(last);
            }

            for (int i = 0; i < images.Count; i++)
            {
                if (i >= state.Images.Count)
                {
                    state.Images.Add(new ImageSlotState());
                }

                AuthorImageQuad(state.Images[i], images[i], $"Polaris.Drawing.Screen.{debugName}.Node{nodeId}.Image{i}");
            }
        }

        void AuthorImageQuad(ImageSlotState slot, BakedImageOp baked, string slotDebugName)
        {
            Texture2D texture = baked.Image.Texture;
            if (slot.Drawer == null)
            {
                slot.Material = BuildImageMaterial(texture);
                slot.Drawer = meshes.Make(slot.Material);
                slot.Gob = meshes.GetGob(slot.Drawer);
                slot.Gob.name = slotDebugName;
                slot.LastTexture = texture;
            }
            else if (slot.LastTexture != texture)
            {
                slot.Drawer.clear();
                UnityEngine.Object.Destroy(slot.Material);
                slot.Material = BuildImageMaterial(texture);
                slot.Drawer.setMaterial(slot.Material, cloned: true);
                slot.LastTexture = texture;
            }
            else
            {
                slot.Drawer.clear();
            }

            AuthorImageQuadGeometry(slot.Drawer, baked, texture);
            slot.Drawer.updateForMeshRenderer();

            int vertexMax = slot.Drawer.getVertexMax();
            slot.BaseColors = new Color32[vertexMax];
            Array.Copy(slot.Drawer.getColorArray(), slot.BaseColors, vertexMax);
        }

        static Material BuildImageMaterial(Texture2D texture)
        {
            Material material = MTRX.newMtr(MTRX.getMtr(BLEND.NORMAL));
            material.SetTexture("_MainTex", texture);
            return material;
        }

        static void AuthorImageQuadGeometry(MeshDrawer drawer, BakedImageOp baked, Texture2D texture)
        {
            // TintArgb 已经是这条命令自己的颜色；baked.Opacity 是录制时 PushOpacity 栈折出来的值，
            // 两者都要烤进顶点颜色——节点运行期的 Opacity 是另一层，由 RecolorImages 在这之上再乘一次。
            Color32 baseColor = C32.d2c(baked.TintArgb);
            baseColor.a = (byte)(Mathf.Clamp01(baked.Opacity) * baseColor.a);
            drawer.Col = baseColor;

            DrawRect rect = baked.Destination;
            Vector3 p0 = baked.LocalMatrix.MultiplyPoint3x4(new Vector3(rect.Left, rect.Bottom, 0f));
            Vector3 p1 = baked.LocalMatrix.MultiplyPoint3x4(new Vector3(rect.Left, rect.Top, 0f));
            Vector3 p2 = baked.LocalMatrix.MultiplyPoint3x4(new Vector3(rect.Right, rect.Top, 0f));
            Vector3 p3 = baked.LocalMatrix.MultiplyPoint3x4(new Vector3(rect.Right, rect.Bottom, 0f));

            float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;
            if (baked.SourceRect.HasValue && texture != null)
            {
                DrawRect src = baked.SourceRect.Value;
                float texW = Mathf.Max(1, texture.width);
                float texH = Mathf.Max(1, texture.height);
                u0 = src.Left / texW;
                u1 = src.Right / texW;
                v0 = src.Bottom / texH;
                v1 = src.Top / texH;
            }

            drawer.Tri(0, 1, 2).Tri(0, 2, 3);
            drawer.PosUv(p0.x, p0.y, u0, v0);
            drawer.PosUv(p1.x, p1.y, u0, v1);
            drawer.PosUv(p2.x, p2.y, u1, v1);
            drawer.PosUv(p3.x, p3.y, u1, v0);
        }

        static void DestroyImageSlot(ImageSlotState slot)
        {
            if (slot.Gob != null)
            {
                UnityEngine.Object.Destroy(slot.Gob);
            }
            if (slot.Material != null)
            {
                UnityEngine.Object.Destroy(slot.Material);
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

            foreach (ImageSlotState image in state.Images)
            {
                image.Gob.transform.localPosition = new Vector3(
                    state.Transform.Translation.X, state.Transform.Translation.Y, state.ComputedZ);
                image.Gob.transform.localRotation = Quaternion.Euler(0f, 0f, state.Transform.Rotation * Mathf.Rad2Deg);
                image.Gob.transform.localScale = new Vector3(state.Transform.ScaleX, state.Transform.ScaleY, 1f);
            }
            RecolorImages(state);

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
            foreach (ImageSlotState image in state.Images)
            {
                image.Gob.SetActive(visible);
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

        void RecolorImages(NodeState state)
        {
            float opacity = Mathf.Clamp01(state.Opacity);
            foreach (ImageSlotState image in state.Images)
            {
                Color32[] live = image.Drawer.getColorArray();
                int count = Mathf.Min(live.Length, image.BaseColors.Length);
                for (int i = 0; i < count; i++)
                {
                    Color32 c = image.BaseColors[i];
                    c.a = (byte)(opacity * c.a);
                    live[i] = c;
                }
                image.Drawer.updateForMeshRenderer();
            }
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
