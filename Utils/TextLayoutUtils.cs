using System;
using System.Globalization;
using System.Text;

namespace Polaris.Utils
{
    /// <summary>
    /// 与具体 UI 实现无关的文本宽度估算和手动折行工具。
    /// </summary>
    public static class TextLayoutUtils
    {
        private const string ProhibitedLineStartCharacters =
            "，。！？；：、）》】』」〉〕］％…—～·,.!?;:%)]}";

        /// <summary>
        /// 按可见字符的估算像素宽度插入换行。尖括号富文本标签会原样保留且不计宽度。
        /// </summary>
        public static string WrapRichText(
            string text,
            float maxWidthPx,
            float fullWidthCharacterPx = 20f,
            float asciiCharacterPx = 10f,
            float whitespacePx = 7f,
            float horizontalPaddingPx = 8f)
        {
            if (string.IsNullOrEmpty(text) || maxWidthPx <= 0f)
                return text ?? string.Empty;

            float budget = Math.Max(fullWidthCharacterPx, maxWidthPx - horizontalPaddingPx);
            float lineWidth = 0f;
            var result = new StringBuilder(text.Length + 8);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    if (close >= 0)
                    {
                        result.Append(text, i, close - i + 1);
                        i = close;
                        continue;
                    }
                }

                if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    result.Append('\n');
                    lineWidth = 0f;
                    continue;
                }

                int characterLength = char.IsHighSurrogate(c)
                    && i + 1 < text.Length
                    && char.IsLowSurrogate(text[i + 1])
                        ? 2
                        : 1;
                float characterWidth = GetVisibleCharacterWidth(
                    text,
                    i,
                    characterLength,
                    fullWidthCharacterPx,
                    asciiCharacterPx,
                    whitespacePx);

                // 避免句读落在行首；允许其暂时略过预算，下一字符再折行。
                if (lineWidth > 0f
                    && lineWidth + characterWidth > budget
                    && !IsProhibitedAtLineStart(c))
                {
                    result.Append('\n');
                    lineWidth = 0f;
                }

                result.Append(text, i, characterLength);
                lineWidth += characterWidth;
                i += characterLength - 1;
            }

            return result.ToString();
        }

        /// <summary>
        /// 返回指定 UTF-16 字符位置的估算可见像素宽度。
        /// </summary>
        public static float GetVisibleCharacterWidth(
            string text,
            int index,
            int characterLength,
            float fullWidthCharacterPx = 20f,
            float asciiCharacterPx = 10f,
            float whitespacePx = 7f)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            if (index < 0 || index >= text.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (characterLength == 2)
                return fullWidthCharacterPx;

            char c = text[index];
            UnicodeCategory category = char.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.SpacingCombiningMark
                || category == UnicodeCategory.EnclosingMark)
                return 0f;

            if (c <= 0x7f)
                return c == ' ' || c == '\t' ? whitespacePx : asciiCharacterPx;

            return fullWidthCharacterPx;
        }

        /// <summary>判断字符是否属于中文排版中不应出现在行首的句读。</summary>
        public static bool IsProhibitedAtLineStart(char character) =>
            ProhibitedLineStartCharacters.IndexOf(character) >= 0;
    }
}
