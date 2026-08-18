using UnityEngine;
using XX;

namespace Polaris.Drawing.Internal
{
    /// <summary>Screen/Map 两个后端共用的节点级几何换算，避免两边各写一份同样的矩阵与包围盒逻辑。</summary>
    internal static class DrawNodeGeometry
    {
        /// <summary>节点的本地变换 → Unity 矩阵：按 T·R·S 组合，Z 恒为 0（深度由各后端自己决定）。</summary>
        internal static Matrix4x4 ToMatrix(DrawTransform transform) => Matrix4x4.TRS(
            new Vector3(transform.Translation.X, transform.Translation.Y, 0f),
            Quaternion.Euler(0f, 0f, transform.Rotation * Mathf.Rad2Deg),
            new Vector3(transform.ScaleX, transform.ScaleY, 1f));
    }

    /// <summary>
    /// 逐节点累加出一个 Surface 的包围盒。结构体 + 顶点原地比较，整个过程不分配。
    /// 一个顶点都没并进来时 <see cref="ToRect"/> 给 <c>default</c>。
    /// </summary>
    internal struct DrawBoundsBuilder
    {
        bool any;
        float minX;
        float minY;
        float maxX;
        float maxY;

        /// <summary>把已烘焙好的几何按节点变换映射后并入包围盒；<paramref name="source"/> 为空缓存时跳过。</summary>
        internal void Include(MeshDrawer source, DrawTransform transform)
        {
            if (source == null)
            {
                return;
            }

            Matrix4x4 matrix = DrawNodeGeometry.ToMatrix(transform);
            Vector3[] vertices = source.getVertexArray();
            int vertexMax = source.getVertexMax();
            for (int i = 0; i < vertexMax; i++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(vertices[i]);
                if (!any)
                {
                    any = true;
                    minX = maxX = point.x;
                    minY = maxY = point.y;
                    continue;
                }

                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
        }

        internal DrawRect ToRect() => any ? DrawRect.FromCorner(minX, minY, maxX - minX, maxY - minY) : default;
    }
}
