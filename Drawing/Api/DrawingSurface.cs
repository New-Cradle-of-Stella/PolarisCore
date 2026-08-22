using System;
using Polaris.Drawing.Internal;

namespace Polaris.Drawing
{
    /// <summary>创建 <see cref="DrawingSurface"/> 时的固定参数；创建后不能再改 Space/Plane/Lifetime。</summary>
    public sealed class DrawingSurfaceOptions
    {
        public DrawSpace Space { get; set; } = DrawSpace.Screen;

        public DrawPlane Plane { get; set; } = DrawPlane.Hud;

        public DrawLifetime Lifetime { get; set; } = DrawLifetime.Scene;

        /// <summary>Screen 同一 Plane 上多个 Surface 之间的粗粒度顺序；Surface 内部节点顺序另见 <see cref="DrawNode.Order"/>。</summary>
        public int ScreenOrder { get; set; }

        /// <summary>用于内部对象命名与调试；不填时使用固定名称。</summary>
        public string DebugName { get; set; }
    }

    /// <summary>
    /// Drawing 的保留模式画布，持有若干 <see cref="DrawNode"/>；节点只在首次构建或 <c>Invalidate()</c> 时重新生成几何/字形缓存。
    /// <see cref="Position"/>/<see cref="Visible"/> 是整个 Surface 的运行状态。
    /// </summary>
    public sealed class DrawingSurface : IDisposable
    {
        readonly DrawingSurfaceRuntime runtime;

        internal DrawingSurface(DrawingSurfaceOptions options)
        {
            runtime = new DrawingSurfaceRuntime(options ?? throw new ArgumentNullException(nameof(options)));
            DrawingRuntime.Register(runtime);
        }

        /// <summary>录制一个节点：<paramref name="draw"/> 在这里以及每次 <c>Invalidate()</c> 时都会被调用一次。</summary>
        public DrawNode Add(Action<DrawContext> draw) => runtime.Add(draw);

        /// <summary>
        /// 让这个 Surface 跟随一个地图目标；只对 <see cref="DrawSpace.Map"/> 有意义。
        /// 重复调用会先释放上一次的跟随关系（同一时刻只有一个跟随目标生效）。
        /// </summary>
        public MapFollowHandle Follow(IMapDrawTarget target, MapFollowOptions options = null) => runtime.Follow(target, options);

        /// <summary>重新跑一遍所有节点的构建回调。只想更新单个节点时优先用 <see cref="DrawNode.Invalidate"/>。</summary>
        public void Invalidate() => runtime.InvalidateAll();

        public bool Visible
        {
            get => runtime.Visible;
            set => runtime.Visible = value;
        }

        /// <summary>整个 Surface 的锚点：Screen 下是像素偏移，Map 下是地图坐标；<see cref="Follow"/> 会持续驱动它。</summary>
        public DrawPoint Position
        {
            get => runtime.Position;
            set => runtime.Position = value;
        }

        public void Dispose() => runtime.Dispose();
    }
}
