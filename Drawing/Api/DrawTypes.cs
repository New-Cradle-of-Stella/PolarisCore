using System;

namespace Polaris.Drawing
{
    /// <summary>Drawing 使用的坐标空间。Screen 是 GUI 逻辑像素，Map 是原版地图坐标。</summary>
    public enum DrawSpace
    {
        Screen,
        Map,
    }

    /// <summary>
    /// 绘制所在的平面，合法组合由 <see cref="DrawSpace"/> 决定：Screen 只能用 <see cref="Background"/>/<see cref="Hud"/>/<see cref="Overlay"/>，
    /// Map 只能用 <see cref="WorldBehindActors"/>/<see cref="WorldActors"/>/<see cref="WorldForeground"/>。
    /// 非法组合在 <c>DrawingAPI.CreateSurface</c> 时立即抛 <see cref="NotSupportedException"/>。
    /// </summary>
    public enum DrawPlane
    {
        Background,
        WorldBehindActors,
        WorldActors,
        WorldForeground,
        Hud,
        Overlay,
    }

    /// <summary>Surface 的自动释放策略。</summary>
    public enum DrawLifetime
    {
        /// <summary>只由调用方 <c>Dispose()</c> 释放。</summary>
        Manual,

        /// <summary>Unity 场景切换时自动释放（标题、地图之间的场景边界）。</summary>
        Scene,

        /// <summary>当前地图实例/地图代数变化时自动释放；仅对 <see cref="DrawSpace.Map"/> 有意义。</summary>
        Map,
    }

    /// <summary>Drawing 公共坐标值。Screen 空间表示 GUI 逻辑像素，Map 空间表示原版地图坐标。</summary>
    public readonly struct DrawPoint : IEquatable<DrawPoint>
    {
        public DrawPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }

        public float Y { get; }

        public bool Equals(DrawPoint other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object obj) => obj is DrawPoint other && Equals(other);

        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();

        public override string ToString() => $"({X}, {Y})";

        public static bool operator ==(DrawPoint left, DrawPoint right) => left.Equals(right);

        public static bool operator !=(DrawPoint left, DrawPoint right) => !left.Equals(right);
    }

    /// <summary>轴对齐矩形，左下角为原点方向与 ver029 一致（Y 向上）。</summary>
    public readonly struct DrawRect : IEquatable<DrawRect>
    {
        public DrawRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>矩形中心的横坐标。</summary>
        public float X { get; }

        /// <summary>矩形中心的纵坐标。</summary>
        public float Y { get; }

        public float Width { get; }

        public float Height { get; }

        public float Left => X - Width * 0.5f;

        public float Right => X + Width * 0.5f;

        public float Bottom => Y - Height * 0.5f;

        public float Top => Y + Height * 0.5f;

        public DrawPoint Center => new DrawPoint(X, Y);

        /// <summary>按左下角+宽高构造，避免调用方手动换算中心点。</summary>
        public static DrawRect FromCorner(float left, float bottom, float width, float height)
            => new DrawRect(left + width * 0.5f, bottom + height * 0.5f, width, height);

        public bool Equals(DrawRect other)
            => X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);

        public override bool Equals(object obj) => obj is DrawRect other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Width.GetHashCode();
                hash = (hash * 397) ^ Height.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => $"[{X}, {Y}, {Width}x{Height}]";

        public static bool operator ==(DrawRect left, DrawRect right) => left.Equals(right);

        public static bool operator !=(DrawRect left, DrawRect right) => !left.Equals(right);
    }

    /// <summary>
    /// 节点的本地 2D 仿射变换：先缩放、再旋转、再平移，<see cref="Rotation"/> 单位是弧度。
    /// 不暴露 Unity Matrix/Transform，调用方只能通过这三个分量组合。
    /// </summary>
    public readonly struct DrawTransform : IEquatable<DrawTransform>
    {
        public static readonly DrawTransform Identity = new DrawTransform(default, 0f, 1f, 1f);

        public DrawTransform(DrawPoint translation, float rotation = 0f, float scaleX = 1f, float scaleY = 1f)
        {
            Translation = translation;
            Rotation = rotation;
            ScaleX = scaleX;
            ScaleY = scaleY;
        }

        public DrawPoint Translation { get; }

        /// <summary>旋转角度，弧度制，逆时针为正。</summary>
        public float Rotation { get; }

        public float ScaleX { get; }

        public float ScaleY { get; }

        public static DrawTransform FromTranslation(DrawPoint translation) => new DrawTransform(translation);

        public bool Equals(DrawTransform other)
            => Translation.Equals(other.Translation) && Rotation.Equals(other.Rotation)
               && ScaleX.Equals(other.ScaleX) && ScaleY.Equals(other.ScaleY);

        public override bool Equals(object obj) => obj is DrawTransform other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Translation.GetHashCode();
                hash = (hash * 397) ^ Rotation.GetHashCode();
                hash = (hash * 397) ^ ScaleX.GetHashCode();
                hash = (hash * 397) ^ ScaleY.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => $"[t={Translation}, r={Rotation}, s=({ScaleX}, {ScaleY})]";

        public static bool operator ==(DrawTransform left, DrawTransform right) => left.Equals(right);

        public static bool operator !=(DrawTransform left, DrawTransform right) => !left.Equals(right);
    }
}
