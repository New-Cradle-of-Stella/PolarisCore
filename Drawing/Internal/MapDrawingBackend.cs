using System;
using System.Collections.Generic;
using m2d;
using UnityEngine;
using XX;

namespace Polaris.Drawing.Internal
{
    /// <summary>
    /// Map 后端：注册一个 <c>M2DrawBinder</c>（<see cref="DrawPlane"/> 决定 setEDC/setED/setEDT 哪一个），
    /// 每帧回调里只读固定/跟随锚点、检查相机范围、取当帧 Effect Mesh，并把节点已缓存的几何/字形/图片通过
    /// <c>RotaTempMeshDrawer</c> 复制过去——这是阶段 0 验证过的路径，回调本身不做路径离散或三角化。
    /// 地图切换时自动在新地图上重新绑定同一批缓存好的节点，不重新跑用户的构建回调。
    /// </summary>
    internal sealed class MapDrawingBackend : IDrawingBackend
    {
        const float CullMarginX = 10f;
        const float CullMarginY = 8f;

        sealed class TextSlotState
        {
            internal TextMeshCacheEntry Entry;
            internal DrawPoint LocalPosition;
            internal Matrix4x4 LocalMatrix;
            internal float BakedOpacity;

            /// <summary>预先算好的 <c>EffectItem.GetMesh</c> 标题；Binder 回调每帧路径不能拼字符串。</summary>
            internal string Title;
        }

        sealed class ImageSlotState
        {
            internal MeshDrawer SourceQuad;
            internal Material Material;
            internal Texture2D LastTexture;

            /// <summary>预先算好的 <c>EffectItem.GetMesh</c> 标题；Binder 回调每帧路径不能拼字符串。</summary>
            internal string Title;
        }

        sealed class NodeState
        {
            internal MeshDrawer SourceShape;
            internal readonly List<TextSlotState> Texts = new();
            internal readonly List<ImageSlotState> Images = new();
            internal DrawTransform Transform = DrawTransform.Identity;
            internal float Opacity = 1f;
            internal bool Visible = true;
            internal int Order;
            internal string ShapeTitle;
        }

        readonly DrawPlane plane;
        readonly string debugName;
        readonly Dictionary<int, NodeState> nodes = new();
        readonly List<int> drawOrder = new();
        readonly Func<int, int> nodeOrderOf;
        readonly GameObject textCacheHost;

        Map2d boundMap;
        M2DrawBinder binder;
        DrawPoint surfacePosition;
        bool surfaceVisible = true;
        bool disposed;

        internal MapDrawingBackend(DrawPlane plane, string debugName)
        {
            this.plane = plane;
            this.debugName = debugName;
            nodeOrderOf = id => nodes[id].Order;
            // 这个容器本身必须保持 active：它没有自己的渲染内容，但如果它是 inactive，挂在它下面的
            // TextRenderer 子物体在 activeInHierarchy=false 期间永远不会跑 Awake，Size/Align/Redraw
            // 那一套调用会因为内部状态没初始化而出问题。真正的“隐藏”由每个 TextMeshCacheEntry
            // 自己在首次 Redraw 完成后关闭自身 GameObject 完成（见 TextMeshCacheEntry.UpdateContent）。
            textCacheHost = new GameObject("Polaris.Drawing.Map." + debugName + ".TextCache");
            UnityEngine.Object.DontDestroyOnLoad(textCacheHost);
        }

        public void CreateNode(int nodeId) => nodes[nodeId] = new NodeState();

        public void RebuildNode(int nodeId, IReadOnlyList<DrawOp> ops)
        {
            if (disposed || !nodes.TryGetValue(nodeId, out NodeState state))
            {
                return;
            }

            if (state.SourceShape == null)
            {
                state.SourceShape = GeometryCache.CreateSourceBuffer();
                state.ShapeTitle = $"{debugName}.N{nodeId}";
            }
            else
            {
                state.SourceShape.clear();
            }

            (List<BakedTextOp> texts, List<BakedImageOp> images) = GeometryCache.Bake(state.SourceShape, ops);
            ReconcileTexts(state, texts, nodeId);
            ReconcileImages(state, images, nodeId);
        }

        public void SetTransform(int nodeId, DrawTransform transform) => nodes[nodeId].Transform = transform;

        public void SetOpacity(int nodeId, float opacity) => nodes[nodeId].Opacity = opacity;

        public void SetVisible(int nodeId, bool visible) => nodes[nodeId].Visible = visible;

        public void SetOrder(int nodeId, int order) => nodes[nodeId].Order = order;

        public void DestroyNode(int nodeId)
        {
            if (!nodes.TryGetValue(nodeId, out NodeState state))
            {
                return;
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

        /// <summary>Map 侧不在这里过滤可见性：Binder 回调每帧自己读 <c>state.Visible</c>，隐藏节点也要留在序列里。</summary>
        public void Relayout(IReadOnlyList<int> nodeIdsInOrder)
            => DrawBatchBuilder.Order(nodeIdsInOrder, nodeOrderOf, visibleOf: null, drawOrder);

        public void Pump(float deltaSeconds)
        {
            if (disposed)
            {
                return;
            }

            Map2d current = Polaris.API.GameBinding.CurrentMap;
            if (current == boundMap)
            {
                return;
            }

            Unbind();
            boundMap = current;
            if (boundMap != null)
            {
                Bind();
            }
        }

        public void SetSurfacePosition(DrawPoint position) => surfacePosition = position;

        public void SetSurfaceVisible(bool visible) => surfaceVisible = visible;

        public DrawingDebugStats.BackendStats GetStats()
        {
            int vertices = 0, triangles = 0, textCount = 0;
            foreach (NodeState state in nodes.Values)
            {
                if (state.SourceShape != null)
                {
                    vertices += state.SourceShape.getVertexMax();
                    triangles += state.SourceShape.getTriMax() / 3;
                }
                foreach (ImageSlotState image in state.Images)
                {
                    vertices += image.SourceQuad.getVertexMax();
                    triangles += image.SourceQuad.getTriMax() / 3;
                }
                textCount += state.Texts.Count;
            }
            return new DrawingDebugStats.BackendStats(nodes.Count, vertices, triangles, textCount, binder != null ? 1 : 0);
        }

        public void Dispose()
        {
            disposed = true;
            Unbind();
            foreach (NodeState state in nodes.Values)
            {
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
            drawOrder.Clear();
            UnityEngine.Object.Destroy(textCacheHost);
        }

        void Bind()
        {
            switch (plane)
            {
                case DrawPlane.WorldBehindActors:
                    binder = boundMap.setEDC(debugName, DrawCallback);
                    break;
                case DrawPlane.WorldActors:
                    binder = boundMap.setED(debugName, DrawCallback);
                    break;
                case DrawPlane.WorldForeground:
                    binder = boundMap.setEDT(debugName, DrawCallback);
                    break;
                default:
                    throw new NotSupportedException($"{plane} is not a valid Map plane.");
            }
        }

        void Unbind()
        {
            if (binder == null)
            {
                return;
            }
            boundMap?.remED(binder);
            binder = null;
        }

        bool DrawCallback(EffectItem effect, M2DrawBinder callingBinder)
        {
            if (callingBinder != binder || boundMap == null)
            {
                return false;
            }

            if (!surfaceVisible)
            {
                return true;
            }

            for (int i = 0; i < drawOrder.Count; i++)
            {
                if (!nodes.TryGetValue(drawOrder[i], out NodeState state) || !state.Visible)
                {
                    continue;
                }

                float nodeX = surfacePosition.X + state.Transform.Translation.X;
                float nodeY = surfacePosition.Y + state.Transform.Translation.Y;
                effect.x = nodeX;
                effect.y = nodeY;
                if (!callingBinder.isinCamera(effect, CullMarginX, CullMarginY))
                {
                    continue;
                }

                DrawShape(effect, state);
                DrawTexts(effect, state);
                DrawImages(effect, state);
            }

            return true;
        }

        void DrawShape(EffectItem effect, NodeState state)
        {
            if (state.SourceShape == null || !state.SourceShape.exist_content)
            {
                return;
            }

            MeshDrawer target = effect.GetMesh(
                state.ShapeTitle, 0xFFFFFFFFu, BLEND.NORMAL, bottom_flag: plane == DrawPlane.WorldBehindActors);
            target.Col = TintWhite(state.Opacity);
            target.RotaTempMeshDrawer(
                0f, 0f, state.Transform.ScaleX, state.Transform.ScaleY, state.Transform.Rotation,
                state.SourceShape, flip: false, get_color: true);
        }

        void DrawTexts(EffectItem effect, NodeState state)
        {
            Matrix4x4 nodeMatrix = DrawNodeGeometry.ToMatrix(state.Transform);
            for (int i = 0; i < state.Texts.Count; i++)
            {
                TextSlotState text = state.Texts[i];
                MeshDrawer textMesh = text.Entry.Renderer.getMeshDrawer();
                if (textMesh == null || textMesh.getTriMax() == 0)
                {
                    continue;
                }

                MeshDrawer target = effect.GetMesh(
                    text.Title, textMesh.getMaterial(), bottom_flag: plane == DrawPlane.WorldBehindActors);
                target.Col = TintWhite(state.Opacity * text.BakedOpacity);

                Matrix4x4 full = nodeMatrix * text.LocalMatrix;
                Vector3 localPoint = full.MultiplyPoint3x4(new Vector3(text.LocalPosition.X, text.LocalPosition.Y, 0f));
                Vector3 scale = full.lossyScale;
                float rotationRadians = full.rotation.eulerAngles.z * Mathf.Deg2Rad;
                target.RotaTempMeshDrawer(localPoint.x, localPoint.y, scale.x, scale.y, rotationRadians, textMesh, flip: false, get_color: true);
            }
        }

        void DrawImages(EffectItem effect, NodeState state)
        {
            for (int i = 0; i < state.Images.Count; i++)
            {
                ImageSlotState image = state.Images[i];
                if (image.SourceQuad == null || !image.SourceQuad.exist_content)
                {
                    continue;
                }

                MeshDrawer target = effect.GetMesh(
                    image.Title, image.Material, bottom_flag: plane == DrawPlane.WorldBehindActors);
                target.Col = TintWhite(state.Opacity);
                target.RotaTempMeshDrawer(
                    0f, 0f, state.Transform.ScaleX, state.Transform.ScaleY, state.Transform.Rotation,
                    image.SourceQuad, flip: false, get_color: true);
            }
        }

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
                            textCacheHost, textCacheHost.layer, $"Polaris.Drawing.Map.{debugName}.Node{nodeId}.Text{i}", visible: false),
                        Title = $"{debugName}.N{nodeId}.T{i}",
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
                    state.Images.Add(new ImageSlotState { Title = $"{debugName}.N{nodeId}.I{i}" });
                }

                AuthorImageQuad(state.Images[i], images[i]);
            }
        }

        static void AuthorImageQuad(ImageSlotState slot, BakedImageOp baked)
        {
            Texture2D texture = baked.Image.Texture;
            if (slot.SourceQuad == null)
            {
                slot.SourceQuad = GeometryCache.CreateSourceBuffer();
            }
            else
            {
                slot.SourceQuad.clear();
            }

            if (slot.Material == null || slot.LastTexture != texture)
            {
                if (slot.Material != null)
                {
                    UnityEngine.Object.Destroy(slot.Material);
                }
                slot.Material = MTRX.newMtr(MTRX.getMtr(BLEND.NORMAL));
                slot.Material.SetTexture("_MainTex", texture);
                slot.LastTexture = texture;
            }

            // TintArgb 是这条命令自己的颜色，baked.Opacity 是录制时 PushOpacity 栈折出来的值，两者都要
            // 烤进顶点颜色；节点运行期的 Opacity 是另一层，由 DrawImages 在复制到 Effect Mesh 时再乘一次。
            Color32 baseColor = C32.d2c(baked.TintArgb);
            baseColor.a = (byte)(Mathf.Clamp01(baked.Opacity) * baseColor.a);
            slot.SourceQuad.Col = baseColor;

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

            slot.SourceQuad.Tri(0, 1, 2).Tri(0, 2, 3);
            slot.SourceQuad.PosUv(p0.x, p0.y, u0, v0);
            slot.SourceQuad.PosUv(p1.x, p1.y, u0, v1);
            slot.SourceQuad.PosUv(p2.x, p2.y, u1, v1);
            slot.SourceQuad.PosUv(p3.x, p3.y, u1, v0);
        }

        static void DestroyImageSlot(ImageSlotState slot)
        {
            if (slot.Material != null)
            {
                UnityEngine.Object.Destroy(slot.Material);
            }
        }

        static Color32 TintWhite(float opacity)
        {
            byte a = (byte)(Mathf.Clamp01(opacity) * 255f);
            return new Color32(255, 255, 255, a);
        }
    }
}
