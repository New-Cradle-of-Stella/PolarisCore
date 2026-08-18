using System;

namespace Polaris.Drawing.Internal
{
    /// <summary>IMapDrawTarget 的无分配主线程跟随器；后续由 Map Surface 持有。</summary>
    internal sealed class MapFollowRuntime : IDisposable
    {
        readonly IMapDrawTarget target;
        float speed;
        DrawPoint offset;
        MapTargetLostBehavior targetLostBehavior;
        bool initialized;

        internal MapFollowRuntime(IMapDrawTarget target, MapFollowOptions options)
        {
            this.target = target ?? throw new ArgumentNullException(nameof(target));
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            Speed = options.Speed;
            offset = options.Offset;
            targetLostBehavior = options.TargetLostBehavior;
        }

        internal DrawPoint Position { get; private set; }

        internal bool Visible { get; private set; }

        internal bool IsDisposed { get; private set; }

        internal bool IsTargetAvailable { get; private set; }

        internal float Speed
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

        internal DrawPoint Offset
        {
            get => offset;
            set => offset = value;
        }

        internal MapTargetLostBehavior TargetLostBehavior
        {
            get => targetLostBehavior;
            set => targetLostBehavior = value;
        }

        internal void Update(float deltaSeconds)
        {
            if (IsDisposed)
            {
                return;
            }

            IsTargetAvailable = target.TryGetMapPosition(out DrawPoint targetPosition);
            if (!IsTargetAvailable)
            {
                switch (targetLostBehavior)
                {
                    case MapTargetLostBehavior.Hide:
                        Visible = false;
                        break;
                    case MapTargetLostBehavior.Freeze:
                        Visible = initialized;
                        break;
                    case MapTargetLostBehavior.Dispose:
                        Dispose();
                        break;
                }
                return;
            }

            targetPosition = new DrawPoint(targetPosition.X + offset.X, targetPosition.Y + offset.Y);
            if (!initialized || float.IsPositiveInfinity(speed))
            {
                Position = targetPosition;
                initialized = true;
                Visible = true;
                return;
            }

            float dx = targetPosition.X - Position.X;
            float dy = targetPosition.Y - Position.Y;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            float maxDistance = speed * Math.Max(0f, deltaSeconds);
            if (distance <= maxDistance || distance <= 0.000001f)
            {
                Position = targetPosition;
            }
            else if (maxDistance > 0f)
            {
                float ratio = maxDistance / distance;
                Position = new DrawPoint(Position.X + dx * ratio, Position.Y + dy * ratio);
            }
            Visible = true;
        }

        public void Dispose()
        {
            IsDisposed = true;
            IsTargetAvailable = false;
            Visible = false;
        }
    }
}
