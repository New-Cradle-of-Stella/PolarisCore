using System;
using UnityEngine;
using XX;

namespace Polaris.Drawing.Internal
{
    /// <summary>
    /// <c>DrawingAPI.MeasureText</c> 背后唯一的、复用的隐藏 <see cref="TextRenderer"/>。
    /// 排版结果只读它的像素宽高，不产出可见内容，所以整个进程共用一个实例即可。
    /// </summary>
    internal static class TextMeasurementService
    {
        static GameObject host;
        static TextRenderer renderer;
        static bool hidden;

        internal static TextMeasurement Measure(string text, TextStyle style)
        {
            if (!Polaris.API.GameSessionRuntime.IsReady)
            {
                throw new InvalidOperationException(
                    "Game assets are not ready yet; DrawingAPI.MeasureText cannot run before fonts finish loading.");
            }

            EnsureHost();

            TextRendererStyle.ApplyForMeasurement(renderer, style);
            renderer.Txt(text);
            renderer.Redraw(execute: true);

            if (!hidden)
            {
                MeshRenderer meshRenderer = renderer.getMeshRenderer();
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = false;
                    host.SetActive(false);
                    hidden = true;
                }
            }

            return new TextMeasurement(renderer.get_swidth_px(), renderer.get_sheight_px());
        }

        static void EnsureHost()
        {
            if (host != null)
            {
                return;
            }

            host = new GameObject("Polaris.Drawing.TextMeasurement");
            UnityEngine.Object.DontDestroyOnLoad(host);
            renderer = host.AddComponent<TextRenderer>();
            renderer.use_valotile = false;
        }
    }
}
