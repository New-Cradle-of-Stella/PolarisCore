using System;
using System.Collections.Generic;

namespace Polaris.Drawing
{
    enum DrawPathVerb
    {
        Move,
        Line,
        Quadratic,
        Cubic,
        Close,
    }

    readonly struct DrawPathCommand
    {
        internal DrawPathCommand(DrawPathVerb verb, DrawPoint p0, DrawPoint p1, DrawPoint p2)
        {
            Verb = verb;
            P0 = p0;
            P1 = p1;
            P2 = p2;
        }

        internal DrawPathVerb Verb { get; }

        internal DrawPoint P0 { get; }

        internal DrawPoint P1 { get; }

        internal DrawPoint P2 { get; }
    }

    /// <summary>
    /// 任意路径：直线、二次/三次贝塞尔曲线与子路径闭合。可以有多个子路径（多次 <see cref="MoveTo"/>），
    /// 从而支持带孔轮廓。路径本身只是数据；离散、三角化与缓存在渲染时（见 <see cref="Internal.PathGeometry"/>）
    /// 才发生，只在节点构建/<c>Invalidate()</c> 时跑一次。
    /// </summary>
    /// <remarks>曲线用自适应细分离散；填充按轮廓包含深度处理嵌套。</remarks>
    public sealed class DrawPath
    {
        readonly List<DrawPathCommand> commands = new();

        public DrawPath MoveTo(DrawPoint point)
        {
            commands.Add(new DrawPathCommand(DrawPathVerb.Move, point, default, default));
            return this;
        }

        public DrawPath LineTo(DrawPoint point)
        {
            commands.Add(new DrawPathCommand(DrawPathVerb.Line, point, default, default));
            return this;
        }

        public DrawPath QuadraticTo(DrawPoint control, DrawPoint end)
        {
            commands.Add(new DrawPathCommand(DrawPathVerb.Quadratic, control, end, default));
            return this;
        }

        public DrawPath CubicTo(DrawPoint control0, DrawPoint control1, DrawPoint end)
        {
            commands.Add(new DrawPathCommand(DrawPathVerb.Cubic, control0, control1, end));
            return this;
        }

        /// <summary>闭合当前子路径（连回最近一次 <see cref="MoveTo"/> 的点）。</summary>
        public DrawPath Close()
        {
            commands.Add(new DrawPathCommand(DrawPathVerb.Close, default, default, default));
            return this;
        }

        internal IReadOnlyList<DrawPathCommand> Commands => commands;
    }

    /// <summary>路径的渲染方式：可以只填充、只描边或两者都要。</summary>
    public sealed class DrawPathStyle
    {
        public DrawPaint? Fill { get; set; }

        public DrawStroke? Stroke { get; set; }

    }
}
