using System.Collections.Generic;
using UnityEngine;
using XX;

namespace Polaris.Drawing.Internal
{
    /// <summary>一条已烘焙好的文本命令：绝对本地矩阵（含节点构建期间的 PushTransform 嵌套）与累计不透明度。</summary>
    internal readonly struct BakedTextOp
    {
        internal BakedTextOp(string text, TextStyle style, DrawPoint localPosition, Matrix4x4 localMatrix, float opacity)
        {
            Text = text;
            Style = style;
            LocalPosition = localPosition;
            LocalMatrix = localMatrix;
            Opacity = opacity;
        }

        internal string Text { get; }

        internal TextStyle Style { get; }

        /// <summary><see cref="DrawContext.DrawText"/> 传入的原始位置，尚未经过 <see cref="LocalMatrix"/>。</summary>
        internal DrawPoint LocalPosition { get; }

        /// <summary>录制时 PushTransform 栈折叠出的矩阵；与 <see cref="LocalPosition"/> 一起才是完整的局部变换。</summary>
        internal Matrix4x4 LocalMatrix { get; }

        internal float Opacity { get; }
    }

    /// <summary>一条已烘焙好的图片命令；语义和 <see cref="BakedTextOp"/> 对称，只是位置换成了整块目标矩形。</summary>
    internal readonly struct BakedImageOp
    {
        internal BakedImageOp(DrawImage image, DrawRect destination, DrawRect? sourceRect, uint tintArgb, Matrix4x4 localMatrix, float opacity)
        {
            Image = image;
            Destination = destination;
            SourceRect = sourceRect;
            TintArgb = tintArgb;
            LocalMatrix = localMatrix;
            Opacity = opacity;
        }

        internal DrawImage Image { get; }

        /// <summary><see cref="DrawContext.DrawImage"/> 传入的原始目标矩形，尚未经过 <see cref="LocalMatrix"/>。</summary>
        internal DrawRect Destination { get; }

        /// <summary><c>null</c> 表示用整张贴图。</summary>
        internal DrawRect? SourceRect { get; }

        internal uint TintArgb { get; }

        /// <summary>录制时 PushTransform 栈折叠出的矩阵；与 <see cref="Destination"/> 一起才是完整的局部变换。</summary>
        internal Matrix4x4 LocalMatrix { get; }

        internal float Opacity { get; }
    }

    /// <summary>
    /// 把一个节点录制下来的 <see cref="DrawOp"/> 序列烘焙成几何缓存：图形命令直接写进一个裸的（不挂
    /// GameObject、不需要材质）<see cref="MeshDrawer"/>，作为节点的“源缓存”；文本命令则解析出绝对矩阵与
    /// 不透明度交给调用方去驱动 <see cref="TextMeshCacheEntry"/>。节点首次构建或 <c>Invalidate()</c> 时才跑一次，
    /// 运行时的位置/透明度/可见性变化不会重新经过这里。
    /// </summary>
    internal static class GeometryCache
    {
        internal static MeshDrawer CreateSourceBuffer() => new MeshDrawer { draw_gl_only = true };

        /// <summary>
        /// 回放一个节点的命令流，把图形写进 <paramref name="target"/>，分别返回按录制顺序排列的文本/图片命令
        /// （顺序即调用方复用 <see cref="TextMeshCacheEntry"/>/图片槽位条目的下标）。图片不写进
        /// <paramref name="target"/>：每张图片需要自己的贴图/材质，不能塞进共享的“形状”缓存。
        /// </summary>
        internal static (List<BakedTextOp> Texts, List<BakedImageOp> Images) Bake(MeshDrawer target, IReadOnlyList<DrawOp> ops)
        {
            var transformStack = new List<DrawTransform>();
            var opacityStack = new List<float>();
            var texts = new List<BakedTextOp>();
            var images = new List<BakedImageOp>();

            target.Identity();
            target.base_z = 0f;

            foreach (DrawOp op in ops)
            {
                switch (op.Kind)
                {
                    case DrawOpKind.PushTransform:
                        transformStack.Add(op.Transform);
                        ReapplyTransform(target, transformStack);
                        break;

                    case DrawOpKind.PopTransform:
                        transformStack.RemoveAt(transformStack.Count - 1);
                        ReapplyTransform(target, transformStack);
                        break;

                    case DrawOpKind.PushOpacity:
                        opacityStack.Add(op.Opacity);
                        break;

                    case DrawOpKind.PopOpacity:
                        opacityStack.RemoveAt(opacityStack.Count - 1);
                        break;

                    case DrawOpKind.Shape:
                        if (op.Shape == ShapeKind.Image)
                        {
                            images.Add(new BakedImageOp(
                                op.Image, op.Rect, op.SourceRect, op.ColorArgb, target.getCurrentMatrix(), OpacityProduct(opacityStack)));
                        }
                        else
                        {
                            Author(target, op, OpacityProduct(opacityStack));
                        }
                        break;

                    case DrawOpKind.Text:
                        texts.Add(new BakedTextOp(
                            op.Text, op.TextStyle, op.PointA, target.getCurrentMatrix(), OpacityProduct(opacityStack)));
                        break;
                }
            }

            target.Identity();
            return (texts, images);
        }

        static float OpacityProduct(List<float> stack)
        {
            float product = 1f;
            for (int i = 0; i < stack.Count; i++)
            {
                product *= stack[i];
            }
            return product;
        }

        /// <summary>
        /// 按 T·R·S 的顺序从根到叶重放整个变换栈。每一级都用 <c>pre_transform: true</c>（右乘/追加），
        /// 使得先入栈的变换在世界空间里包在外层，后入栈（更深层）的变换先作用在原始顶点上。
        /// </summary>
        static void ReapplyTransform(MeshDrawer target, List<DrawTransform> stack)
        {
            target.Identity();
            for (int i = 0; i < stack.Count; i++)
            {
                DrawTransform t = stack[i];
                target.Translate(t.Translation.X, t.Translation.Y, pre_transform: true);
                target.Rotate(t.Rotation, pre_transform: true);
                target.Scale(t.ScaleX, t.ScaleY, pre_transform: true);
            }
        }

        static void Author(MeshDrawer target, DrawOp op, float opacity)
        {
            target.Col = Tint(op.ColorArgb, opacity);
            switch (op.Shape)
            {
                case ShapeKind.FillRect:
                    target.Rect(op.Rect.X, op.Rect.Y, op.Rect.Width, op.Rect.Height);
                    break;

                case ShapeKind.StrokeRect:
                    target.Box(op.Rect.X, op.Rect.Y, op.Rect.Width, op.Rect.Height, op.Thickness);
                    break;

                case ShapeKind.FillRoundedRect:
                    FillPolygon(target, RoundedRectOutline(op.Rect, op.Radius));
                    break;

                case ShapeKind.StrokeRoundedRect:
                    StrokeClosedPolyline(target, RoundedRectOutline(op.Rect, op.Radius), op.Thickness);
                    break;

                case ShapeKind.FillCircle:
                    target.Circle(op.PointA.X, op.PointA.Y, op.Radius);
                    break;

                case ShapeKind.StrokeCircle:
                    target.Circle(op.PointA.X, op.PointA.Y, op.Radius, op.Thickness);
                    break;

                case ShapeKind.Line:
                    target.Line(op.PointA.X, op.PointA.Y, op.PointB.X, op.PointB.Y, op.Thickness);
                    break;

                case ShapeKind.Polyline:
                    for (int i = 0; i < op.Points.Count - 1; i++)
                    {
                        DrawPoint a = op.Points[i];
                        DrawPoint b = op.Points[i + 1];
                        target.Line(a.X, a.Y, b.X, b.Y, op.Thickness);
                    }
                    break;

                case ShapeKind.FillPolygon:
                    FillPolygon(target, op.Points);
                    break;

                case ShapeKind.PathFill:
                    PathGeometry.Fill(target, PathGeometry.Flatten(op.PathCommands));
                    break;

                case ShapeKind.PathStroke:
                    PathGeometry.Stroke(target, PathGeometry.Flatten(op.PathCommands), op.Thickness);
                    break;
            }
        }

        static Color32 Tint(uint colorArgb, float opacity)
        {
            Color32 c = C32.d2c(colorArgb);
            c.a = (byte)(Mathf.Clamp01(opacity) * c.a);
            return c;
        }

        // ── 圆角矩形轮廓 ──────────────────────────────

        internal static List<DrawPoint> RoundedRectOutline(DrawRect rect, float radius)
        {
            float maxRadius = Mathf.Min(rect.Width, rect.Height) * 0.5f;
            float r = Mathf.Clamp(radius, 0f, maxRadius);
            if (r <= 0.0001f)
            {
                return new List<DrawPoint>
                {
                    new DrawPoint(rect.Left, rect.Bottom),
                    new DrawPoint(rect.Right, rect.Bottom),
                    new DrawPoint(rect.Right, rect.Top),
                    new DrawPoint(rect.Left, rect.Top),
                };
            }

            int segments = Mathf.Clamp(Mathf.CeilToInt(r * 0.5f), 4, 16);
            var points = new List<DrawPoint>((segments + 1) * 4);
            AppendArc(points, rect.Right - r, rect.Bottom + r, r, -Mathf.PI / 2f, 0f, segments);
            AppendArc(points, rect.Right - r, rect.Top - r, r, 0f, Mathf.PI / 2f, segments);
            AppendArc(points, rect.Left + r, rect.Top - r, r, Mathf.PI / 2f, Mathf.PI, segments);
            AppendArc(points, rect.Left + r, rect.Bottom + r, r, Mathf.PI, 3f * Mathf.PI / 2f, segments);
            return points;
        }

        static void AppendArc(List<DrawPoint> points, float cx, float cy, float r, float startAngle, float endAngle, int segments)
        {
            for (int i = 0; i <= segments; i++)
            {
                float t = startAngle + (endAngle - startAngle) * i / segments;
                points.Add(new DrawPoint(cx + r * Mathf.Cos(t), cy + r * Mathf.Sin(t)));
            }
        }

        // ── 闭合轮廓描边（重叠的粗线段，不做斜接/圆角连接） ──

        static void StrokeClosedPolyline(MeshDrawer target, IReadOnlyList<DrawPoint> points, float thickness)
        {
            int count = points.Count;
            for (int i = 0; i < count; i++)
            {
                DrawPoint a = points[i];
                DrawPoint b = points[(i + 1) % count];
                target.Line(a.X, a.Y, b.X, b.Y, thickness);
            }
        }

        // ── 简单多边形三角化（耳切法，支持凸/凹，不支持自相交或带孔） ──

        internal static void FillPolygon(MeshDrawer target, IReadOnlyList<DrawPoint> points)
        {
            List<int> triangles = Triangulate(points);
            for (int i = 0; i < triangles.Count; i += 3)
            {
                target.Tri(triangles[i], triangles[i + 1], triangles[i + 2]);
            }
            for (int i = 0; i < points.Count; i++)
            {
                target.Pos(points[i].X, points[i].Y);
            }
        }

        static List<int> Triangulate(IReadOnlyList<DrawPoint> polygon)
        {
            int n = polygon.Count;
            var order = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                order.Add(i);
            }

            if (SignedArea(polygon) < 0f)
            {
                order.Reverse();
            }

            var triangles = new List<int>((n - 2) * 3);
            int guard = 0;
            while (order.Count > 3 && guard++ < n * n + 16)
            {
                bool clipped = false;
                for (int i = 0; i < order.Count; i++)
                {
                    int iPrev = order[(i - 1 + order.Count) % order.Count];
                    int iCur = order[i];
                    int iNext = order[(i + 1) % order.Count];
                    if (!IsEar(polygon, order, iPrev, iCur, iNext))
                    {
                        continue;
                    }

                    triangles.Add(iPrev);
                    triangles.Add(iCur);
                    triangles.Add(iNext);
                    order.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped)
                {
                    // 退化/自相交轮廓：放弃继续耳切，保留已经三角化的部分，不抛异常拖垮整个节点重建。
                    break;
                }
            }

            if (order.Count == 3)
            {
                triangles.Add(order[0]);
                triangles.Add(order[1]);
                triangles.Add(order[2]);
            }

            return triangles;
        }

        static bool IsEar(IReadOnlyList<DrawPoint> polygon, List<int> order, int iPrev, int iCur, int iNext)
        {
            DrawPoint a = polygon[iPrev];
            DrawPoint b = polygon[iCur];
            DrawPoint c = polygon[iNext];
            if (Cross(a, b, c) <= 0f)
            {
                return false;
            }

            for (int i = 0; i < order.Count; i++)
            {
                int idx = order[i];
                if (idx == iPrev || idx == iCur || idx == iNext)
                {
                    continue;
                }

                if (PointInTriangle(polygon[idx], a, b, c))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>正值表示逆时针（CCW）。<see cref="PathGeometry"/> 判断轮廓方向/挑外轮廓时也用它。</summary>
        internal static float SignedArea(IReadOnlyList<DrawPoint> polygon)
        {
            float area = 0f;
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                DrawPoint a = polygon[i];
                DrawPoint b = polygon[(i + 1) % n];
                area += a.X * b.Y - b.X * a.Y;
            }
            return area * 0.5f;
        }

        internal static float Cross(DrawPoint a, DrawPoint b, DrawPoint c)
            => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        static bool PointInTriangle(DrawPoint p, DrawPoint a, DrawPoint b, DrawPoint c)
        {
            float d1 = Cross(a, b, p);
            float d2 = Cross(b, c, p);
            float d3 = Cross(c, a, p);
            const float epsilon = 0.000001f;
            return d1 > epsilon && d2 > epsilon && d3 > epsilon;
        }
    }
}
