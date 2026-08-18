using System;
using Polaris.Drawing.Internal;

namespace Polaris.Drawing
{
    /// <summary>
    /// 一次 <c>DrawingSurface.Add(...)</c> 得到的节点句柄。节点首次构建或 <see cref="Invalidate"/> 时才
    /// 重新跑一次构建回调、重做几何/字形缓存；<see cref="Transform"/>/<see cref="Opacity"/>/
    /// <see cref="Visible"/>/<see cref="Order"/> 只更新运行状态，不触发重建。
    /// </summary>
    public sealed class DrawNode : IDisposable
    {
        readonly DrawingSurfaceRuntime owner;
        readonly int id;
        bool disposed;

        internal DrawNode(DrawingSurfaceRuntime owner, int id)
        {
            this.owner = owner;
            this.id = id;
        }

        public DrawTransform Transform
        {
            get
            {
                EnsureUsable();
                return owner.GetNodeTransform(id);
            }
            set
            {
                EnsureUsable();
                owner.SetNodeTransform(id, value);
            }
        }

        /// <summary>
        /// [0, 1] 的不透明度。对 Screen 图形内容，这会用统一透明度重新覆盖已烘焙的顶点颜色
        /// （不会把它和命令自带的透明度逐个相乘）；对文本节点，会和文本自身颜色的透明度正确相乘。
        /// </summary>
        public float Opacity
        {
            get
            {
                EnsureUsable();
                return owner.GetNodeOpacity(id);
            }
            set
            {
                EnsureUsable();
                if (float.IsNaN(value) || value < 0f || value > 1f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Opacity must be within [0, 1].");
                }
                owner.SetNodeOpacity(id, value);
            }
        }

        public bool Visible
        {
            get
            {
                EnsureUsable();
                return owner.GetNodeVisible(id);
            }
            set
            {
                EnsureUsable();
                owner.SetNodeVisible(id, value);
            }
        }

        /// <summary>同一 Surface 内的绘制顺序，越大越晚画（越靠上）。</summary>
        public int Order
        {
            get
            {
                EnsureUsable();
                return owner.GetNodeOrder(id);
            }
            set
            {
                EnsureUsable();
                owner.SetNodeOrder(id, value);
            }
        }

        /// <summary>重新跑一次构建回调，重做这个节点的几何/字形缓存。</summary>
        public void Invalidate()
        {
            EnsureUsable();
            owner.InvalidateNode(id);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            owner.RemoveNode(id);
        }

        void EnsureUsable()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(DrawNode));
            }
        }
    }
}
