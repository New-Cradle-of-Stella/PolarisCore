using System;
using System.Collections.Generic;

namespace Polaris.Drawing.Internal
{
    /// <summary>
    /// 把一个 Surface 里的节点排成稳定的提交顺序：每个可见节点各自拥有一个 MeshRenderer（Screen 后端
    /// 一个节点一个 <c>MeshDrawer</c>），节点之间的前后关系完全靠 Z/渲染队列决定，因此这里就是这套后端
    /// 事实上的“合批”策略——相同 <see cref="DrawNode.Order"/> 内部再按插入顺序稳定排序，避免同 Order
    /// 节点之间的顺序抖动。排完的顺序怎么变成深度由各后端自己决定（Screen 按名次分配 Z，Map 直接照序提交）。
    /// </summary>
    internal static class DrawBatchBuilder
    {
        // 只在主线程的 Relayout 里用，且不重入（orderOf/visibleOf 都只是查表），所以复用同一份暂存表与
        // 同一个比较委托，节点可见性每帧翻转时也不会持续分配。
        static readonly List<(int Id, int Order, int Index)> scratch = new();

        static readonly Comparison<(int Id, int Order, int Index)> byOrderThenIndex =
            static (a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Index.CompareTo(b.Index);

        /// <summary>
        /// 把 <paramref name="nodeIds"/> 按 Order（相同则按插入顺序）排好写进 <paramref name="destination"/>，
        /// 写之前先清空它。<paramref name="visibleOf"/> 为 <c>null</c> 表示不做可见性过滤、全部保留。
        /// </summary>
        internal static void Order(
            IReadOnlyList<int> nodeIds, Func<int, int> orderOf, Func<int, bool> visibleOf, List<int> destination)
        {
            scratch.Clear();
            destination.Clear();

            for (int i = 0; i < nodeIds.Count; i++)
            {
                int id = nodeIds[i];
                if (visibleOf == null || visibleOf(id))
                {
                    scratch.Add((id, orderOf(id), i));
                }
            }

            scratch.Sort(byOrderThenIndex);

            for (int i = 0; i < scratch.Count; i++)
            {
                destination.Add(scratch[i].Id);
            }
        }
    }
}
