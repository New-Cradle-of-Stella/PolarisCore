using System;
using System.Collections.Generic;

namespace Polaris.Drawing.Internal
{
    internal enum ShapeKind
    {
        FillRect,
        StrokeRect,
        FillRoundedRect,
        StrokeRoundedRect,
        FillCircle,
        StrokeCircle,
        Line,
        Polyline,
        FillPolygon,
    }

    internal enum DrawOpKind
    {
        PushTransform,
        PopTransform,
        PushOpacity,
        PopOpacity,
        Shape,
        Text,
    }

    /// <summary>
    /// 一条已录制的绘制命令。字段按 <see cref="Kind"/> 取用哪些有效，由 <see cref="GeometryCache"/>/
    /// <see cref="TextMeshCacheEntry"/> 的回放逻辑决定，不在这里做子类型拆分以减少分配。
    /// </summary>
    internal sealed class DrawOp
    {
        internal DrawOpKind Kind;

        // PushTransform
        internal DrawTransform Transform;

        // PushOpacity
        internal float Opacity;

        // Shape：矩形系用 Rect(+Radius)，圆用 PointA 当圆心加 Radius，线段用 PointA/PointB，
        // 折线与多边形用 Points；Thickness 只对描边类形状有意义。
        internal ShapeKind Shape;
        internal DrawRect Rect;
        internal float Radius;
        internal DrawPoint PointA;
        internal DrawPoint PointB;
        internal IReadOnlyList<DrawPoint> Points;
        internal uint ColorArgb;
        internal float Thickness;

        // Text：位置同样存在 PointA 里。
        internal string Text;
        internal TextStyle TextStyle;
    }

    /// <summary>
    /// <see cref="DrawContext"/> 的录制实现：节点构建回调期间把调用原样记成 <see cref="DrawOp"/> 序列，
    /// 供 <see cref="GeometryCache"/>/<see cref="TextMeshCacheEntry"/> 在几何/字形缓存阶段回放。
    /// 回调结束后 <see cref="Seal"/> 会让上下文失效，防止调用方偷偷保留引用继续画。
    /// </summary>
    internal sealed class DrawCommandBuffer : DrawContext
    {
        readonly List<DrawOp> ops = new();
        int transformDepth;
        int opacityDepth;
        bool sealedForUse;

        internal IReadOnlyList<DrawOp> Ops => ops;

        internal void Seal()
        {
            if (transformDepth != 0)
            {
                throw new InvalidOperationException("PushTransform/PopTransform calls were not balanced.");
            }

            if (opacityDepth != 0)
            {
                throw new InvalidOperationException("PushOpacity/PopOpacity calls were not balanced.");
            }

            sealedForUse = true;
        }

        void EnsureUsable()
        {
            if (sealedForUse)
            {
                throw new InvalidOperationException(
                    "This DrawContext is only valid during the node build callback; it cannot be used or retained afterward.");
            }
        }

        public override void FillRect(DrawRect rect, DrawPaint paint)
            => AddShape(new DrawOp { Shape = ShapeKind.FillRect, Rect = rect, ColorArgb = paint.ColorArgb });

        public override void StrokeRect(DrawRect rect, DrawStroke stroke)
            => AddShape(new DrawOp
            {
                Shape = ShapeKind.StrokeRect,
                Rect = rect,
                ColorArgb = stroke.ColorArgb,
                Thickness = stroke.Thickness,
            });

        public override void FillRoundedRect(DrawRect rect, float radius, DrawPaint paint)
            => AddShape(new DrawOp
            {
                Shape = ShapeKind.FillRoundedRect,
                Rect = rect,
                Radius = radius,
                ColorArgb = paint.ColorArgb,
            });

        public override void StrokeRoundedRect(DrawRect rect, float radius, DrawStroke stroke)
            => AddShape(new DrawOp
            {
                Shape = ShapeKind.StrokeRoundedRect,
                Rect = rect,
                Radius = radius,
                ColorArgb = stroke.ColorArgb,
                Thickness = stroke.Thickness,
            });

        public override void FillCircle(DrawPoint center, float radius, DrawPaint paint)
            => AddShape(new DrawOp
            {
                Shape = ShapeKind.FillCircle,
                PointA = center,
                Radius = radius,
                ColorArgb = paint.ColorArgb,
            });

        public override void StrokeCircle(DrawPoint center, float radius, DrawStroke stroke)
            => AddShape(new DrawOp
            {
                Shape = ShapeKind.StrokeCircle,
                PointA = center,
                Radius = radius,
                ColorArgb = stroke.ColorArgb,
                Thickness = stroke.Thickness,
            });

        public override void DrawLine(DrawPoint from, DrawPoint to, DrawStroke stroke)
            => AddShape(new DrawOp
            {
                Shape = ShapeKind.Line,
                PointA = from,
                PointB = to,
                ColorArgb = stroke.ColorArgb,
                Thickness = stroke.Thickness,
            });

        public override void DrawPolyline(IReadOnlyList<DrawPoint> points, DrawStroke stroke)
        {
            if (points == null || points.Count < 2)
            {
                throw new ArgumentException("A polyline needs at least two points.", nameof(points));
            }

            AddShape(new DrawOp
            {
                Shape = ShapeKind.Polyline,
                Points = points,
                ColorArgb = stroke.ColorArgb,
                Thickness = stroke.Thickness,
            });
        }

        public override void FillPolygon(IReadOnlyList<DrawPoint> points, DrawPaint paint)
        {
            if (points == null || points.Count < 3)
            {
                throw new ArgumentException("A polygon needs at least three points.", nameof(points));
            }

            AddShape(new DrawOp { Shape = ShapeKind.FillPolygon, Points = points, ColorArgb = paint.ColorArgb });
        }

        public override void DrawPath(DrawPath path, DrawPathStyle style)
        {
            EnsureUsable();
            throw new NotSupportedException(
                "DrawContext.DrawPath is reserved for a later phase (adaptive curve tessellation and holed outlines " +
                "have not gone through phase-0 dynamic validation yet).");
        }

        public override void DrawImage(DrawImage image, DrawRect destination, DrawImageStyle style = null)
        {
            EnsureUsable();
            throw new NotSupportedException(
                "DrawContext.DrawImage is not backed by a renderer yet (image drawing has not gone through phase-0 dynamic validation).");
        }

        public override void DrawText(string text, DrawPoint position, TextStyle style)
        {
            EnsureUsable();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (style == null)
            {
                throw new ArgumentNullException(nameof(style));
            }

            ops.Add(new DrawOp
            {
                Kind = DrawOpKind.Text,
                Text = text,
                PointA = position,
                TextStyle = style.Clone(),
            });
        }

        public override void PushTransform(DrawTransform transform)
        {
            EnsureUsable();
            transformDepth++;
            ops.Add(new DrawOp { Kind = DrawOpKind.PushTransform, Transform = transform });
        }

        public override void PopTransform()
        {
            EnsureUsable();
            if (transformDepth <= 0)
            {
                throw new InvalidOperationException("PopTransform called without a matching PushTransform.");
            }

            transformDepth--;
            ops.Add(new DrawOp { Kind = DrawOpKind.PopTransform });
        }

        public override void PushOpacity(float opacity)
        {
            EnsureUsable();
            if (float.IsNaN(opacity) || opacity < 0f || opacity > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(opacity), opacity, "Opacity must be within [0, 1].");
            }

            opacityDepth++;
            ops.Add(new DrawOp { Kind = DrawOpKind.PushOpacity, Opacity = opacity });
        }

        public override void PopOpacity()
        {
            EnsureUsable();
            if (opacityDepth <= 0)
            {
                throw new InvalidOperationException("PopOpacity called without a matching PushOpacity.");
            }

            opacityDepth--;
            ops.Add(new DrawOp { Kind = DrawOpKind.PopOpacity });
        }

        public override void PushClipRect(DrawRect rect)
        {
            EnsureUsable();
            throw new NotSupportedException(
                "DrawContext.PushClipRect is reserved for a later phase (CPU clip rects have not gone through phase-0 dynamic validation yet).");
        }

        public override void PopClip()
        {
            EnsureUsable();
            throw new NotSupportedException(
                "DrawContext.PopClip is reserved for a later phase (CPU clip rects have not gone through phase-0 dynamic validation yet).");
        }

        /// <summary>补上 <see cref="DrawOpKind.Shape"/> 再入列，调用点只填自己这一种形状真正用到的字段。</summary>
        void AddShape(DrawOp op)
        {
            EnsureUsable();
            op.Kind = DrawOpKind.Shape;
            ops.Add(op);
        }
    }
}
