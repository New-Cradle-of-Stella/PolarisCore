using System.Collections.Generic;
using UnityEngine;
using XX;

namespace Polaris.Drawing.Internal
{
    /// <summary>
    /// <see cref="DrawContext.DrawPath"/> 的离散化与三角化：把 Move/Line/Quadratic/Cubic/Close 命令流
    /// 拍平成折线子路径，再交给填充（带孔）/描边两条路径。曲线用自适应细分（de Casteljau + 平坦度判定），
    /// 不是固定分段数。
    /// </summary>
    /// <remarks>
    /// 填充按包含深度的奇偶性区分外轮廓和洞；每个洞桥接进直接外轮廓后再做耳切三角化。
    /// </remarks>
    internal static class PathGeometry
    {
        const float FlattenToleranceUnits = 0.5f;
        const int MaxSubdivisionDepth = 10;

        // ── 拍平 ──────────────────────────────────────────

        internal static List<List<DrawPoint>> Flatten(IReadOnlyList<DrawPathCommand> commands)
        {
            var subpaths = new List<List<DrawPoint>>();
            List<DrawPoint> current = null;
            DrawPoint cursor = default;
            DrawPoint subpathStart = default;

            foreach (DrawPathCommand cmd in commands)
            {
                switch (cmd.Verb)
                {
                    case DrawPathVerb.Move:
                        current = new List<DrawPoint> { cmd.P0 };
                        subpaths.Add(current);
                        cursor = cmd.P0;
                        subpathStart = cmd.P0;
                        break;

                    case DrawPathVerb.Line:
                        EnsureCurrent(ref current, subpaths, cursor, ref subpathStart);
                        current.Add(cmd.P0);
                        cursor = cmd.P0;
                        break;

                    case DrawPathVerb.Quadratic:
                        EnsureCurrent(ref current, subpaths, cursor, ref subpathStart);
                        FlattenQuadratic(current, cursor, cmd.P0, cmd.P1, 0);
                        cursor = cmd.P1;
                        break;

                    case DrawPathVerb.Cubic:
                        EnsureCurrent(ref current, subpaths, cursor, ref subpathStart);
                        FlattenCubic(current, cursor, cmd.P0, cmd.P1, cmd.P2, 0);
                        cursor = cmd.P2;
                        break;

                    case DrawPathVerb.Close:
                        if (current != null && current.Count > 0)
                        {
                            current.Add(subpathStart);
                            cursor = subpathStart;
                        }
                        break;
                }
            }

            return subpaths;
        }

        /// <summary>没有先 <see cref="DrawPath.MoveTo"/> 就画线/曲线时，从当前游标位置起一条新子路径，不抛异常。</summary>
        static void EnsureCurrent(ref List<DrawPoint> current, List<List<DrawPoint>> subpaths, DrawPoint cursor, ref DrawPoint subpathStart)
        {
            if (current != null)
            {
                return;
            }

            current = new List<DrawPoint> { cursor };
            subpaths.Add(current);
            subpathStart = cursor;
        }

        static void FlattenQuadratic(List<DrawPoint> outPoints, DrawPoint p0, DrawPoint control, DrawPoint p1, int depth)
        {
            if (depth >= MaxSubdivisionDepth || DistancePointToSegment(control, p0, p1) <= FlattenToleranceUnits)
            {
                outPoints.Add(p1);
                return;
            }

            DrawPoint p01 = Mid(p0, control);
            DrawPoint p12 = Mid(control, p1);
            DrawPoint p012 = Mid(p01, p12);
            FlattenQuadratic(outPoints, p0, p01, p012, depth + 1);
            FlattenQuadratic(outPoints, p012, p12, p1, depth + 1);
        }

        static void FlattenCubic(List<DrawPoint> outPoints, DrawPoint p0, DrawPoint c0, DrawPoint c1, DrawPoint p1, int depth)
        {
            if (depth >= MaxSubdivisionDepth
                || (DistancePointToSegment(c0, p0, p1) <= FlattenToleranceUnits
                    && DistancePointToSegment(c1, p0, p1) <= FlattenToleranceUnits))
            {
                outPoints.Add(p1);
                return;
            }

            DrawPoint p01 = Mid(p0, c0);
            DrawPoint p12 = Mid(c0, c1);
            DrawPoint p23 = Mid(c1, p1);
            DrawPoint p012 = Mid(p01, p12);
            DrawPoint p123 = Mid(p12, p23);
            DrawPoint p0123 = Mid(p012, p123);
            FlattenCubic(outPoints, p0, p01, p012, p0123, depth + 1);
            FlattenCubic(outPoints, p0123, p123, p23, p1, depth + 1);
        }

        static DrawPoint Mid(DrawPoint a, DrawPoint b) => new((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);

        static float DistancePointToSegment(DrawPoint p, DrawPoint a, DrawPoint b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float lengthSq = dx * dx + dy * dy;
            if (lengthSq <= 1e-9f)
            {
                return Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(a.X, a.Y));
            }

            float t = Mathf.Clamp01(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSq);
            float projX = a.X + t * dx;
            float projY = a.Y + t * dy;
            return Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(projX, projY));
        }

        // ── 填充：按包含深度的奇偶性分出外轮廓/洞，洞用钥匙孔桥接进直接外轮廓 ──

        internal static void Fill(MeshDrawer target, List<List<DrawPoint>> subpaths)
        {
            var polygons = new List<List<DrawPoint>>();
            foreach (List<DrawPoint> subpath in subpaths)
            {
                List<DrawPoint> polygon = CleanContour(subpath);
                if (polygon.Count >= 3)
                {
                    polygons.Add(polygon);
                }
            }
            if (polygons.Count == 0)
            {
                return;
            }

            var samples = new DrawPoint[polygons.Count];
            for (int i = 0; i < polygons.Count; i++)
            {
                samples[i] = polygons[i][0];
            }

            var containerCount = new int[polygons.Count];
            for (int i = 0; i < polygons.Count; i++)
            {
                for (int k = 0; k < polygons.Count; k++)
                {
                    if (k != i && PointInPolygon(samples[i], polygons[k]))
                    {
                        containerCount[i]++;
                    }
                }
            }

            var directParent = new int[polygons.Count];
            for (int i = 0; i < polygons.Count; i++)
            {
                directParent[i] = -1;
                if (containerCount[i] % 2 == 0)
                {
                    continue; // 只有洞（奇数层）需要认领一个直接外轮廓
                }

                for (int k = 0; k < polygons.Count; k++)
                {
                    if (k != i && containerCount[k] == containerCount[i] - 1 && PointInPolygon(samples[i], polygons[k]))
                    {
                        directParent[i] = k;
                        break;
                    }
                }
            }

            for (int i = 0; i < polygons.Count; i++)
            {
                if (containerCount[i] % 2 != 0)
                {
                    continue; // 偶数层（含 0）才是外轮廓，本身走独立一次填充
                }

                List<DrawPoint> merged = NormalizeWinding(polygons[i], ccw: true);
                for (int j = 0; j < polygons.Count; j++)
                {
                    if (directParent[j] == i)
                    {
                        merged = BridgeHole(merged, NormalizeWinding(polygons[j], ccw: false));
                    }
                }
                GeometryCache.FillPolygon(target, merged);
            }
        }

        static List<DrawPoint> NormalizeWinding(List<DrawPoint> polygon, bool ccw)
        {
            bool isCcw = GeometryCache.SignedArea(polygon) >= 0f;
            if (isCcw == ccw)
            {
                return new List<DrawPoint>(polygon);
            }

            var reversed = new List<DrawPoint>(polygon);
            reversed.Reverse();
            return reversed;
        }

        /// <summary>经典钥匙孔拼接：把洞的顶点环缝进外轮廓，产出一条耳切三角化能直接吃的简单多边形。</summary>
        static List<DrawPoint> BridgeHole(List<DrawPoint> outer, List<DrawPoint> hole)
        {
            int holeStart = IndexOfRightmost(hole);
            DrawPoint bridgeFromHole = hole[holeStart];
            int outerIndex = FindBridgeOuterIndex(outer, bridgeFromHole);

            var result = new List<DrawPoint>(outer.Count + hole.Count + 2);
            for (int k = 0; k <= outerIndex; k++)
            {
                result.Add(outer[k]);
            }
            for (int k = 0; k < hole.Count; k++)
            {
                result.Add(hole[(holeStart + k) % hole.Count]);
            }
            result.Add(bridgeFromHole);
            for (int k = outerIndex; k < outer.Count; k++)
            {
                result.Add(outer[k]);
            }
            return result;
        }

        static int IndexOfRightmost(List<DrawPoint> polygon)
        {
            int best = 0;
            for (int i = 1; i < polygon.Count; i++)
            {
                DrawPoint c = polygon[i];
                DrawPoint b = polygon[best];
                if (c.X > b.X || (c.X == b.X && c.Y > b.Y))
                {
                    best = i;
                }
            }
            return best;
        }

        /// <summary>挑一个洞能直连、且桥接线不穿过外轮廓自身边界的顶点；找不到就退而求其次挑最近的。</summary>
        static int FindBridgeOuterIndex(List<DrawPoint> outer, DrawPoint from)
        {
            int fallback = 0;
            float fallbackDistSq = float.MaxValue;
            int best = -1;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i < outer.Count; i++)
            {
                DrawPoint candidate = outer[i];
                float distSq = DistanceSq(from, candidate);
                if (distSq < fallbackDistSq)
                {
                    fallbackDistSq = distSq;
                    fallback = i;
                }

                if (distSq < bestDistSq && !BridgeCrossesOuter(outer, i, from, candidate))
                {
                    bestDistSq = distSq;
                    best = i;
                }
            }

            return best >= 0 ? best : fallback;
        }

        static bool BridgeCrossesOuter(List<DrawPoint> outer, int candidateIndex, DrawPoint from, DrawPoint to)
        {
            int n = outer.Count;
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                if (i == candidateIndex || next == candidateIndex)
                {
                    continue; // 桥接线本来就该碰到它自己的端点，不算穿过
                }
                if (SegmentsProperlyIntersect(from, to, outer[i], outer[next]))
                {
                    return true;
                }
            }
            return false;
        }

        static bool SegmentsProperlyIntersect(DrawPoint a1, DrawPoint a2, DrawPoint b1, DrawPoint b2)
        {
            float d1 = GeometryCache.Cross(b1, b2, a1);
            float d2 = GeometryCache.Cross(b1, b2, a2);
            float d3 = GeometryCache.Cross(a1, a2, b1);
            float d4 = GeometryCache.Cross(a1, a2, b2);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        static float DistanceSq(DrawPoint a, DrawPoint b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        static List<DrawPoint> CleanContour(List<DrawPoint> source)
        {
            var result = new List<DrawPoint>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (result.Count == 0 || result[result.Count - 1] != source[i])
                {
                    result.Add(source[i]);
                }
            }

            if (result.Count > 1 && result[0] == result[result.Count - 1])
            {
                result.RemoveAt(result.Count - 1);
            }
            return result;
        }

        static bool PointInPolygon(DrawPoint p, List<DrawPoint> polygon)
        {
            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                DrawPoint a = polygon[j];
                DrawPoint b = polygon[i];
                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    float t = (p.Y - a.Y) / (b.Y - a.Y);
                    float x = a.X + t * (b.X - a.X);
                    if (x > p.X)
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

        // ── 描边：逐子路径按相邻点连线，Close() 时 Flatten 已经把首点补在末尾，闭合线段自然就有了 ──

        internal static void Stroke(MeshDrawer target, List<List<DrawPoint>> subpaths, float thickness)
        {
            foreach (List<DrawPoint> subpath in subpaths)
            {
                for (int i = 0; i < subpath.Count - 1; i++)
                {
                    DrawPoint a = subpath[i];
                    DrawPoint b = subpath[i + 1];
                    target.Line(a.X, a.Y, b.X, b.Y, thickness);
                }
            }
        }
    }
}
