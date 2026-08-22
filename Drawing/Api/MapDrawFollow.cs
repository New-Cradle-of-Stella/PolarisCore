using System;
using Polaris.Drawing.Internal;

namespace Polaris.Drawing
{
    /// <summary>
    /// 可被地图 Drawing Surface 跟随的稳定目标协议。实现不得向调用方暴露
    /// <c>PR</c>、<c>M2Mover</c>、Unity Transform 或其他版本相关类型。
    /// </summary>
    public interface IMapDrawTarget
    {
        /// <summary>
        /// 返回当前地图坐标；目标暂时不可用、已离开地图或已失效时返回 false。
        /// 该方法只会在 Unity 主线程调用。
        /// </summary>
        bool TryGetMapPosition(out DrawPoint position);
    }

    /// <summary>跟随目标不可用时的 Surface 行为。</summary>
    public enum MapTargetLostBehavior
    {
        Hide,
        Freeze,
        Dispose,
    }

    /// <summary>地图 Surface 跟随参数。创建跟随关系时会复制其当前值。</summary>
    public sealed class MapFollowOptions
    {
        float speed = float.PositiveInfinity;

        /// <summary>地图单位/秒。正无穷表示每帧立即贴合，0 表示保持当前位置。</summary>
        public float Speed
        {
            get => speed;
            set
            {
                if (float.IsNaN(value) || value < 0f || float.IsNegativeInfinity(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Follow speed must be non-negative or positive infinity.");
                }
                speed = value;
            }
        }

        /// <summary>相对目标的地图坐标偏移。</summary>
        public DrawPoint Offset { get; set; }

        /// <summary>目标不可用时的处理策略。</summary>
        public MapTargetLostBehavior TargetLostBehavior { get; set; } = MapTargetLostBehavior.Hide;
    }

    /// <summary>
    /// <see cref="DrawingSurface.Follow"/> 返回的可变句柄。修改 <see cref="Speed"/>/<see cref="Offset"/>/
    /// <see cref="TargetLostBehavior"/> 立即生效，不重建 Surface、Binder 或几何。
    /// </summary>
    public sealed class MapFollowHandle : IDisposable
    {
        readonly DrawingSurfaceRuntime owner;
        readonly MapFollowRuntime runtime;
        bool disposed;

        internal MapFollowHandle(DrawingSurfaceRuntime owner, MapFollowRuntime runtime)
        {
            this.owner = owner;
            this.runtime = runtime;
        }

        /// <summary>地图单位/秒；正无穷表示每帧立即贴合，0 表示保持当前位置。</summary>
        public float Speed
        {
            get => runtime.Speed;
            set => runtime.Speed = value;
        }

        public DrawPoint Offset
        {
            get => runtime.Offset;
            set => runtime.Offset = value;
        }

        public MapTargetLostBehavior TargetLostBehavior
        {
            get => runtime.TargetLostBehavior;
            set => runtime.TargetLostBehavior = value;
        }

        public bool IsTargetAvailable => runtime.IsTargetAvailable;

        public bool IsDisposed => disposed || runtime.IsDisposed;

        /// <summary>停止跟随；不会连带释放 Surface 或它的节点，Surface 之后保持在最后一次跟随到的位置。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            runtime.Dispose();
            owner.OnFollowDisposed(runtime);
        }
    }
}
