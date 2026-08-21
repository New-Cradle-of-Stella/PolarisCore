using System;
using System.Collections.Generic;

namespace Polaris.Content
{
    /// <summary>内容注册撞了同一个 Key 时的处置方式。</summary>
    public enum ContentConflictPolicy
    {
        /// <summary>发现冲突立刻抛 <see cref="InvalidOperationException"/>，调用方自行处理（通常是单条报告并跳过这一条）。</summary>
        ThrowImmediately,

        /// <summary>发现冲突先记下来，全部注册结束后由 <see cref="ContentCatalog{TKey, TValue}.Seal"/> 一次性处置。</summary>
        Aggregate,
    }

    /// <summary>
    /// 通用的按 Key 注册的内容目录：处理"两个来源注册了同一个 Key"的冲突，取代各模块各自维护的
    /// <c>Dictionary&lt;TKey, TValue&gt;</c> + 手写冲突判定（PlangRuntime、PnpcRegistry、BehaviorRepository、
    /// AddonCatalogBuilder 都曾各自实现一遍，规则相同、写法各异）。同一来源（<c>source</c> 相同）重复注册同一个
    /// Key 视为覆盖而非冲突，只走 <paramref name="onSameSourceOverwrite"/>提示；不同来源才是真正的冲突。
    /// </summary>
    public sealed class ContentCatalog<TKey, TValue>
    {
        readonly Dictionary<TKey, (TValue Value, string Source)> entries;
        readonly ContentConflictPolicy policy;
        readonly List<ContentDiagnostic> conflicts = new();

        public ContentCatalog(IEqualityComparer<TKey> comparer = null, ContentConflictPolicy policy = ContentConflictPolicy.ThrowImmediately)
        {
            entries = new Dictionary<TKey, (TValue, string)>(comparer);
            this.policy = policy;
        }

        /// <summary>已记录但尚未经 <see cref="Seal"/> 处置的冲突；只有 <see cref="ContentConflictPolicy.Aggregate"/> 下才会累积。</summary>
        public IReadOnlyList<ContentDiagnostic> Conflicts => conflicts;

        /// <summary>当前目录内容的一份快照拷贝。</summary>
        public IReadOnlyDictionary<TKey, TValue> Snapshot
        {
            get
            {
                var snapshot = new Dictionary<TKey, TValue>(entries.Count, entries.Comparer);
                foreach (KeyValuePair<TKey, (TValue Value, string Source)> entry in entries)
                {
                    snapshot[entry.Key] = entry.Value.Value;
                }

                return snapshot;
            }
        }

        /// <summary>
        /// 注册一个 Key。同一来源重复注册视为覆盖（返回 <c>true</c>，覆盖发生时回调 <paramref name="onSameSourceOverwrite"/>）；
        /// 不同来源注册已存在的 Key 是冲突，按构造时选定的策略处置——<see cref="ContentConflictPolicy.ThrowImmediately"/> 立刻抛出，
        /// <see cref="ContentConflictPolicy.Aggregate"/> 记入 <see cref="Conflicts"/> 并返回 <c>false</c>（先注册的一方保留）。
        /// </summary>
        public bool TryRegister(TKey key, TValue value, string source, Action<string> onSameSourceOverwrite = null)
        {
            if (entries.TryGetValue(key, out (TValue Value, string Source) existing))
            {
                if (string.Equals(existing.Source, source, StringComparison.Ordinal))
                {
                    onSameSourceOverwrite?.Invoke(
                        $"'{key}' was registered more than once by '{source}'; the later registration overrode the earlier one.");
                    entries[key] = (value, source);
                    return true;
                }

                var conflict = new ContentDiagnostic(
                    "duplicate-key",
                    ContentDiagnosticSeverity.Error,
                    $"'{key}' from '{source}' conflicts with an existing registration from '{existing.Source}'.",
                    source);

                if (policy == ContentConflictPolicy.ThrowImmediately)
                {
                    throw new InvalidOperationException(conflict.Message);
                }

                conflicts.Add(conflict);
                return false;
            }

            entries.Add(key, (value, source));
            return true;
        }

        /// <summary>移除一个 Key；用于文件热重载后旧 Key 被新内容取代的场景。</summary>
        public bool Remove(TKey key) => entries.Remove(key);

        public bool TryGet(TKey key, out TValue value)
        {
            if (entries.TryGetValue(key, out (TValue Value, string Source) existing))
            {
                value = existing.Value;
                return true;
            }

            value = default;
            return false;
        }

        public bool ContainsKey(TKey key) => entries.ContainsKey(key);

        /// <summary>清空目录与已记录的冲突；用于组件 Shutdown 时重置静态状态。</summary>
        public void Clear()
        {
            entries.Clear();
            conflicts.Clear();
        }

        /// <summary>
        /// <see cref="ContentConflictPolicy.Aggregate"/> 模式下，全部注册结束后调用一次：有冲突就交给
        /// <paramref name="onConflicts"/> 统一处置（通常是汇总成一条致命错误），随后清空已记录的冲突。
        /// </summary>
        public void Seal(Action<IReadOnlyList<ContentDiagnostic>> onConflicts)
        {
            if (conflicts.Count == 0)
            {
                return;
            }

            ContentDiagnostic[] batch = conflicts.ToArray();
            conflicts.Clear();
            onConflicts?.Invoke(batch);
        }
    }
}
