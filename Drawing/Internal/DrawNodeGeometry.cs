using UnityEngine;

namespace Polaris.Drawing.Internal
{
    /// <summary>Screen/Map 两个后端共用的节点级变换换算。</summary>
    internal static class DrawNodeGeometry
    {
        /// <summary>节点的本地变换 → Unity 矩阵：按 T·R·S 组合，Z 恒为 0（深度由各后端自己决定）。</summary>
        internal static Matrix4x4 ToMatrix(DrawTransform transform) => Matrix4x4.TRS(
            new Vector3(transform.Translation.X, transform.Translation.Y, 0f),
            Quaternion.Euler(0f, 0f, transform.Rotation * Mathf.Rad2Deg),
            new Vector3(transform.ScaleX, transform.ScaleY, 1f));
    }
}
