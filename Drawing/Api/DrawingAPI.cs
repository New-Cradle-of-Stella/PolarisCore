using System;

namespace Polaris.Drawing
{
    /// <summary>Drawing 的公共入口：创建 Surface、量文本，以及只读的调试统计。</summary>
    public static class DrawingAPI
    {
        public static DrawingSurface CreateSurface(DrawingSurfaceOptions options)
            => new DrawingSurface(options ?? throw new ArgumentNullException(nameof(options)));

        /// <summary><paramref name="text"/> 必须是已求值的字面量文本。游戏资源尚未就绪时抛 <see cref="InvalidOperationException"/>。</summary>
        public static TextMeasurement MeasureText(string text, TextStyle style)
        {
            if (style == null)
            {
                throw new ArgumentNullException(nameof(style));
            }

            if (string.IsNullOrEmpty(text))
            {
                return default;
            }

            return Internal.TextMeasurementService.Measure(text, style);
        }

        public static DrawingDebugStats DebugStats => Internal.DrawingRuntime.GetStats();
    }

    /// <summary>
    /// 全部 Surface 的聚合调试统计快照。<see cref="RebuildCount"/> 是进程启动以来的累计节点重建次数，
    /// 其余字段是取快照那一刻的即时值。
    /// </summary>
    public readonly struct DrawingDebugStats
    {
        internal DrawingDebugStats(
            int surfaceCount, int nodeCount, int vertexCount, int triangleCount,
            int textCount, int mapCallbackCount, int rebuildCount, int activeFollowCount)
        {
            SurfaceCount = surfaceCount;
            NodeCount = nodeCount;
            VertexCount = vertexCount;
            TriangleCount = triangleCount;
            TextCount = textCount;
            MapCallbackCount = mapCallbackCount;
            RebuildCount = rebuildCount;
            ActiveFollowCount = activeFollowCount;
        }

        public int SurfaceCount { get; }

        public int NodeCount { get; }

        public int VertexCount { get; }

        public int TriangleCount { get; }

        public int TextCount { get; }

        /// <summary>当前存活的 Map <c>M2DrawBinder</c> 回调数量（setEDC/setED/setEDT 合计）。</summary>
        public int MapCallbackCount { get; }

        /// <summary>进程启动以来 <c>DrawNode.Invalidate</c>/<c>DrawingSurface.Invalidate</c> 触发的节点重建累计次数。</summary>
        public int RebuildCount { get; }

        /// <summary>当前生效的 <see cref="MapFollowHandle"/> 数量。</summary>
        public int ActiveFollowCount { get; }

        /// <summary>各后端上报聚合前的分片统计，由 <see cref="Internal.IDrawingBackend.GetStats"/> 产出。</summary>
        internal readonly struct BackendStats
        {
            internal BackendStats(int nodeCount, int vertexCount, int triangleCount, int textCount, int mapCallbacks)
            {
                NodeCount = nodeCount;
                VertexCount = vertexCount;
                TriangleCount = triangleCount;
                TextCount = textCount;
                MapCallbacks = mapCallbacks;
            }

            internal int NodeCount { get; }

            internal int VertexCount { get; }

            internal int TriangleCount { get; }

            internal int TextCount { get; }

            internal int MapCallbacks { get; }
        }
    }
}
