using System;

namespace Polaris.Drawing
{
    /// <summary>水平对齐，语义与文本锚点在 <see cref="DrawContext.DrawText"/> 传入位置上的对齐方式一致。</summary>
    public enum TextHorizontalAlign
    {
        Left,
        Center,
        Right,
    }

    /// <summary>垂直对齐。</summary>
    public enum TextVerticalAlign
    {
        Top,
        Middle,
        Bottom,
    }

    /// <summary>
    /// 文本样式。Drawing 只接受已求值的字面量文本（见 <see cref="DrawContext.DrawText"/>），不接收本地化键；
    /// 语言切换后由调用方重新取文案并调用 <c>DrawNode.Invalidate()</c>。
    /// </summary>
    public sealed class TextStyle : IEquatable<TextStyle>
    {
        public float FontSize { get; set; } = 22f;

        /// <summary>0xAARRGGBB。</summary>
        public uint ColorArgb { get; set; } = 0xFFFFFFFFu;

        /// <summary>字符描边颜色；<c>null</c> 表示不描边。</summary>
        public uint? BorderColorArgb { get; set; }

        public TextHorizontalAlign HorizontalAlign { get; set; } = TextHorizontalAlign.Left;

        public TextVerticalAlign VerticalAlign { get; set; } = TextVerticalAlign.Top;

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        /// <summary>行距增量（像素）。</summary>
        public float LineSpacing { get; set; }

        /// <summary>字间距增量（像素）。</summary>
        public float LetterSpacing { get; set; }

        public TextStyle Clone() => new TextStyle
        {
            FontSize = FontSize,
            ColorArgb = ColorArgb,
            BorderColorArgb = BorderColorArgb,
            HorizontalAlign = HorizontalAlign,
            VerticalAlign = VerticalAlign,
            Bold = Bold,
            Italic = Italic,
            LineSpacing = LineSpacing,
            LetterSpacing = LetterSpacing,
        };

        public bool Equals(TextStyle other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return FontSize.Equals(other.FontSize) && ColorArgb == other.ColorArgb
                && BorderColorArgb == other.BorderColorArgb && HorizontalAlign == other.HorizontalAlign
                && VerticalAlign == other.VerticalAlign && Bold == other.Bold && Italic == other.Italic
                && LineSpacing.Equals(other.LineSpacing) && LetterSpacing.Equals(other.LetterSpacing);
        }

        public override bool Equals(object obj) => Equals(obj as TextStyle);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = FontSize.GetHashCode();
                hash = (hash * 397) ^ (int)ColorArgb;
                hash = (hash * 397) ^ (BorderColorArgb?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (int)HorizontalAlign;
                hash = (hash * 397) ^ (int)VerticalAlign;
                hash = (hash * 397) ^ Bold.GetHashCode();
                hash = (hash * 397) ^ Italic.GetHashCode();
                hash = (hash * 397) ^ LineSpacing.GetHashCode();
                hash = (hash * 397) ^ LetterSpacing.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>一次 <c>DrawingAPI.MeasureText</c> 的结果，单位与调用时的坐标空间一致。</summary>
    public readonly struct TextMeasurement : IEquatable<TextMeasurement>
    {
        public TextMeasurement(float width, float height)
        {
            Width = width;
            Height = height;
        }

        public float Width { get; }

        public float Height { get; }

        public bool Equals(TextMeasurement other) => Width.Equals(other.Width) && Height.Equals(other.Height);

        public override bool Equals(object obj) => obj is TextMeasurement other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Width.GetHashCode() * 397) ^ Height.GetHashCode();
            }
        }

        public override string ToString() => $"{Width}x{Height}";

        public static bool operator ==(TextMeasurement left, TextMeasurement right) => left.Equals(right);

        public static bool operator !=(TextMeasurement left, TextMeasurement right) => !left.Equals(right);
    }
}
