using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Polaris.Drawing.Internal
{
    /// <summary>
    /// 全部 Surface 的注册表：每帧推进 Follow，并在 Unity 场景切换/地图代数变化时按
    /// <see cref="DrawLifetime"/> 自动释放到期的 Surface。由 <c>Plugin</c> 驱动，见
    /// <see cref="Update"/>/<see cref="Shutdown"/>。
    /// </summary>
    internal static class DrawingRuntime
    {
        static readonly List<DrawingSurfaceRuntime> surfaces = new();
        static int trackedSceneHandle = int.MinValue;
        static int trackedMapGeneration = int.MinValue;

        internal static void Register(DrawingSurfaceRuntime surface) => surfaces.Add(surface);

        internal static void Update()
        {
            TrackSceneLifetime();
            TrackMapLifetime();

            float deltaSeconds = Time.deltaTime;
            for (int i = surfaces.Count - 1; i >= 0; i--)
            {
                DrawingSurfaceRuntime surface = surfaces[i];
                if (surface.IsDisposed)
                {
                    surfaces.RemoveAt(i);
                    continue;
                }
                surface.Pump(deltaSeconds);
            }
        }

        internal static void Shutdown()
        {
            for (int i = surfaces.Count - 1; i >= 0; i--)
            {
                surfaces[i].Dispose();
            }
            surfaces.Clear();
        }

        internal static DrawingDebugStats GetStats()
        {
            int nodeCount = 0, vertexCount = 0, triangleCount = 0, textCount = 0, mapCallbacks = 0, rebuildCount = 0, activeFollow = 0;
            int liveSurfaces = 0;
            foreach (DrawingSurfaceRuntime surface in surfaces)
            {
                if (surface.IsDisposed)
                {
                    continue;
                }
                liveSurfaces++;
                DrawingDebugStats.BackendStats stats = surface.Stats;
                nodeCount += stats.NodeCount;
                vertexCount += stats.VertexCount;
                triangleCount += stats.TriangleCount;
                textCount += stats.TextCount;
                mapCallbacks += stats.MapCallbacks;
                rebuildCount += surface.RebuildCount;
                if (surface.HasActiveFollow)
                {
                    activeFollow++;
                }
            }

            return new DrawingDebugStats(liveSurfaces, nodeCount, vertexCount, triangleCount, textCount, mapCallbacks, rebuildCount, activeFollow);
        }

        static void TrackSceneLifetime()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (trackedSceneHandle == int.MinValue)
            {
                trackedSceneHandle = scene.handle;
                return;
            }

            if (scene.handle != trackedSceneHandle)
            {
                trackedSceneHandle = scene.handle;
                DisposeWhere(DrawLifetime.Scene);
            }
        }

        static void TrackMapLifetime()
        {
            int generation = Polaris.API.GameBinding.MapGeneration;
            if (trackedMapGeneration == int.MinValue)
            {
                trackedMapGeneration = generation;
                return;
            }

            if (generation != trackedMapGeneration)
            {
                trackedMapGeneration = generation;
                DisposeWhere(DrawLifetime.Map);
            }
        }

        static void DisposeWhere(DrawLifetime lifetime)
        {
            for (int i = surfaces.Count - 1; i >= 0; i--)
            {
                if (surfaces[i].Lifetime == lifetime)
                {
                    surfaces[i].Dispose();
                    surfaces.RemoveAt(i);
                }
            }
        }
    }
}
