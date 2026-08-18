using System;

namespace Polaris.Drawing
{
    /// <summary>填充颜色。<see cref="ColorArgb"/> 是 0xAARRGGBB 顺序的 32 位颜色，与 ver029 的颜色码一致。</summary>
    public readonly struct DrawPaint : IEquatable<DrawPaint>
    {
        public DrawPaint(uint colorArgb)
        {
            ColorArgb = colorArgb;
        }

        public uint ColorArgb { get; }

        public static DrawPaint FromArgb(uint colorArgb) => new DrawPaint(colorArgb);

        public bool Equals(DrawPaint other) => ColorArgb == other.ColorArgb;

        public override bool Equals(object obj) => obj is DrawPaint other && Equals(other);

        public override int GetHashCode() => (int)ColorArgb;

        public override string ToString() => $"#{ColorArgb:X8}";

        public static bool operator ==(DrawPaint left, DrawPaint right) => left.Equals(right);

        public static bool operator !=(DrawPaint left, DrawPaint right) => !left.Equals(right);
    }

    /// <summary>描边样式：颜色 + 粗细（像素/地图单位，取决于所在 <see cref="DrawSpace"/>）。</summary>
    public readonly struct DrawStroke : IEquatable<DrawStroke>
    {
        public DrawStroke(uint colorArgb, float thickness)
        {
            if (thickness <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "Stroke thickness must be positive.");
            }

            ColorArgb = colorArgb;
            Thickness = thickness;
        }

        public uint ColorArgb { get; }

        public float Thickness { get; }

        public bool Equals(DrawStroke other) => ColorArgb == other.ColorArgb && Thickness.Equals(other.Thickness);

        public override bool Equals(object obj) => obj is DrawStroke other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)ColorArgb * 397) ^ Thickness.GetHashCode();
            }
        }

        public override string ToString() => $"#{ColorArgb:X8} x{Thickness}";

        public static bool operator ==(DrawStroke left, DrawStroke right) => left.Equals(right);

        public static bool operator !=(DrawStroke left, DrawStroke right) => !left.Equals(right);
    }

    /// <summary>
    /// 一张可绘制的位图。只包装调用方自备的 <see cref="UnityEngine.Texture2D"/>，不接触游戏内部的图集/资源系统；
    /// Drawing 不负责加载或缓存贴图本体，只在绘制时读取其像素尺寸与内容。
    /// </summary>
    /// <remarks>
    /// Screen 节点每张图片各自一个 <c>MeshDrawer</c>/材质；Map 节点每张图片各自一份缓存网格，
    /// 每帧通过 <c>RotaTempMeshDrawer</c> 复制到 Effect Mesh，和文本走同一套已验证的路径。
    /// </remarks>
    public sealed class DrawImage
    {
        public DrawImage(UnityEngine.Texture2D texture)
        {
            Texture = texture ?? throw new ArgumentNullException(nameof(texture));
        }

        internal UnityEngine.Texture2D Texture { get; }

        public int PixelWidth => Texture.width;

        public int PixelHeight => Texture.height;
    }

    /// <summary>图片绘制的可选样式：着色与源矩形裁切（像素坐标，<c>null</c> 表示整张贴图）。</summary>
    public sealed class DrawImageStyle
    {
        public uint TintArgb { get; set; } = 0xFFFFFFFFu;

        public DrawRect? SourcePixelRect { get; set; }
    }
}
