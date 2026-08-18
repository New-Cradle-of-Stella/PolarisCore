using System.Collections.Generic;

namespace Polaris.Drawing
{
    /// <summary>
    /// 一次节点构建/重建时用来记录绘制命令的上下文，由 <c>DrawingSurface.Add(...)</c> 的回调接收。
    /// 只在回调执行期间有效：回调返回后继续持有或调用会抛 <see cref="System.InvalidOperationException"/>。
    /// </summary>
    public abstract class DrawContext
    {
        public abstract void FillRect(DrawRect rect, DrawPaint paint);

        public abstract void StrokeRect(DrawRect rect, DrawStroke stroke);

        public abstract void FillRoundedRect(DrawRect rect, float radius, DrawPaint paint);

        public abstract void StrokeRoundedRect(DrawRect rect, float radius, DrawStroke stroke);

        public abstract void FillCircle(DrawPoint center, float radius, DrawPaint paint);

        public abstract void StrokeCircle(DrawPoint center, float radius, DrawStroke stroke);

        public abstract void DrawLine(DrawPoint from, DrawPoint to, DrawStroke stroke);

        public abstract void DrawPolyline(IReadOnlyList<DrawPoint> points, DrawStroke stroke);

        public abstract void FillPolygon(IReadOnlyList<DrawPoint> points, DrawPaint paint);

        /// <summary>阶段 3 功能，当前未接入后端；调用会抛 <see cref="System.NotSupportedException"/>。</summary>
        public abstract void DrawPath(DrawPath path, DrawPathStyle style);

        /// <summary>图片绘制尚未接入后端（未经过阶段 0 动态验证）；调用会抛 <see cref="System.NotSupportedException"/>。</summary>
        public abstract void DrawImage(DrawImage image, DrawRect destination, DrawImageStyle style = null);

        /// <summary><paramref name="text"/> 必须是已求值的字面量文本，不接受本地化 key。</summary>
        public abstract void DrawText(string text, DrawPoint position, TextStyle style);

        public abstract void PushTransform(DrawTransform transform);

        public abstract void PopTransform();

        public abstract void PushOpacity(float opacity);

        public abstract void PopOpacity();

        /// <summary>阶段 3 功能，当前未接入后端；调用会抛 <see cref="System.NotSupportedException"/>。</summary>
        public abstract void PushClipRect(DrawRect rect);

        public abstract void PopClip();
    }
}
