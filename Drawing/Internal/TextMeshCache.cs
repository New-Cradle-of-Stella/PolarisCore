using UnityEngine;
using XX;

namespace Polaris.Drawing.Internal
{
    /// <summary>
    /// 一条文本命令对应的 <see cref="TextRenderer"/> 缓存：Screen 用它直接可见渲染（<c>visible: true</c>，打开 Valotile）；
    /// Map 用它做隐藏的字形缓存（<c>visible: false</c>，关掉自身 MeshRenderer，只把生成的网格复制到 Effect Mesh，见 <see cref="MapDrawingBackend"/>）。
    /// 只有文本内容或样式真的变化时才会重新排版，位置/透明度变化都走各自后端的廉价路径。
    /// </summary>
    internal sealed class TextMeshCacheEntry
    {
        readonly bool visible;
        string lastText;
        TextStyle lastStyle;

        internal TextMeshCacheEntry(GameObject parent, int layer, string debugName, bool visible)
        {
            this.visible = visible;
            Host = new GameObject(debugName);
            Host.transform.SetParent(parent.transform, worldPositionStays: false);
            Host.layer = layer;
            Renderer = Host.AddComponent<TextRenderer>();
            Renderer.use_valotile = visible;
        }

        internal GameObject Host { get; private set; }

        internal TextRenderer Renderer { get; private set; }

        internal bool IsDisposed => Host == null;

        /// <summary>按需重新排版：文本与样式都没变时直接返回，不重做字形网格。</summary>
        internal void UpdateContent(string text, TextStyle style)
        {
            if (text == lastText && style.Equals(lastStyle))
            {
                return;
            }

            TextRendererStyle.Apply(Renderer, style);
            Renderer.Txt(text);
            Renderer.Redraw(execute: true);
            lastText = text;
            lastStyle = style.Clone();

            if (!visible)
            {
                MeshRenderer meshRenderer = Renderer.getMeshRenderer();
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = false;
                }
                Host.SetActive(false);
            }
        }

        internal void Dispose()
        {
            if (Host != null)
            {
                UnityEngine.Object.Destroy(Host);
            }
            Host = null;
            Renderer = null;
        }
    }
}
