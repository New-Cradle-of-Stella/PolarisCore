using XX;

namespace Polaris.Drawing.Internal
{
    /// <summary>
    /// 把一个 <see cref="TextStyle"/> 铺到 ver029 的 <see cref="TextRenderer"/> 上。
    /// 字形缓存（<see cref="TextMeshCacheEntry"/>）与量文本（<see cref="TextMeasurementService"/>）共用同一套
    /// 映射，只在对齐方式上分叉：前者用样式自己的对齐，后者固定左上角以便直接读排版出的像素宽高。
    /// </summary>
    internal static class TextRendererStyle
    {
        /// <summary>按样式自己的对齐方式应用。</summary>
        internal static void Apply(TextRenderer renderer, TextStyle style)
            => Apply(renderer, style, ToAlign(style.HorizontalAlign), ToAlignY(style.VerticalAlign));

        /// <summary>固定按左上角对齐应用，用于量文本：对齐方式不影响宽高，锚点固定更省事。</summary>
        internal static void ApplyForMeasurement(TextRenderer renderer, TextStyle style)
            => Apply(renderer, style, ALIGN.LEFT, ALIGNY.TOP);

        static void Apply(TextRenderer renderer, TextStyle style, ALIGN align, ALIGNY alignY)
        {
            renderer
                .Size(style.FontSize)
                .Align(align)
                .AlignY(alignY)
                .Col(style.ColorArgb)
                .Bold(style.Bold)
                .Italic(style.Italic)
                .LineSpacing(style.LineSpacing)
                .LetterSpacing(style.LetterSpacing);

            if (style.BorderColorArgb.HasValue)
            {
                renderer.BorderCol(style.BorderColorArgb.Value);
            }
        }

        static ALIGN ToAlign(TextHorizontalAlign align) => align switch
        {
            TextHorizontalAlign.Left => ALIGN.LEFT,
            TextHorizontalAlign.Right => ALIGN.RIGHT,
            _ => ALIGN.CENTER,
        };

        static ALIGNY ToAlignY(TextVerticalAlign align) => align switch
        {
            TextVerticalAlign.Top => ALIGNY.TOP,
            TextVerticalAlign.Bottom => ALIGNY.BOTTOM,
            _ => ALIGNY.MIDDLE,
        };
    }
}
