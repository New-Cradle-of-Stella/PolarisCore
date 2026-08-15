using System;
using nel;

namespace Polaris.API
{
    /// <summary>
    /// 一种游戏内插件（Enhancer）的定义。取得实例的入口是 <c>PolarisAPI.Game.Enhancers</c>。
    ///
    /// 这是<b>目录定义</b>的包装器，不是"背包里的那一个"：身份跟随原版 <c>ENHA.Enhancer</c> 对象引用，
    /// 一局游戏内稳定；而 <see cref="IsObtained"/>/<see cref="IsActive"/> 这些状态跟随当前存档变化。
    /// 数量仍归 <see cref="GameStorage"/>，这里不复制库存接口。
    /// </summary>
    public sealed class GameEnhancer : GameInstance
    {
        /// <summary>插件对应物品的 key 前缀，见 <c>ENHA.enhancer_item_header</c>。</summary>
        internal const string ItemKeyPrefix = "Enhancer_";

        static readonly InstanceTable<ENHA.Enhancer, GameEnhancer> Table = new();

        readonly ENHA.Enhancer enhancer;

        GameEnhancer(ENHA.Enhancer enhancer)
        {
            this.enhancer = enhancer;
        }

        // ── 观察快照（由 GameEnhancerRuntime 读写）───────────────────────────────

        /// <summary>是否已经建立过基线。为 <c>false</c> 时下一次观察只记录、不发回调。</summary>
        internal bool StateKnown { get; private set; }

        internal bool LastObtained { get; private set; }

        internal bool LastActive { get; private set; }

        internal void CaptureBaseline(bool obtained, bool active)
        {
            LastObtained = obtained;
            LastActive = active;
            StateKnown = true;
        }

        internal void ClearBaseline() => StateKnown = false;

        internal static GameEnhancer Wrap(ENHA.Enhancer native) => Table.Get(native, static n => new GameEnhancer(n));

        internal static GameEnhancer Peek(ENHA.Enhancer native) => Table.Peek(native);

        internal static void SweepEnhancers() => Table.Sweep();

        internal static void InvalidateAllEnhancers() => Table.InvalidateAll();

        /// <summary>遍历已经建过包装器的插件，用于每帧状态差分。</summary>
        internal static void EachLive(Action<GameEnhancer> visit) => Table.Each(visit);

        /// <summary>本层内部拿回底层定义对象。</summary>
        internal ENHA.Enhancer Native => enhancer;

        // 目录定义在一局游戏内不会被销毁；随新游戏/读档重建目录时由 InvalidateAllEnhancers 整体作废。
        private protected override bool IsNativeAlive => enhancer != null;

        private protected override string Describe() => $"GameEnhancer({SafeKey()})";

        /// <summary>获取该插件的稳定键名。</summary>
        public string Key => SafeKey();

        /// <summary>取得该插件关联的物品定义（key 为 <c>Enhancer_&lt;key&gt;</c>）；没有对应物品时为 <c>null</c>。</summary>
        public GameItem Item => GameItem.Resolve(ItemKey);

        /// <summary>获取该插件在当前语言下的显示名称。每次读取都跟随当前语言，不缓存。</summary>
        public string Title => Read(static e => e.title, null);

        /// <summary>获取该插件在当前语言下的说明。每次读取都跟随当前语言，不缓存。</summary>
        public string Description => Read(static e => e.descript, null);

        /// <summary>获取启用该插件占用的槽位数。</summary>
        public int Cost => Read(static e => e.cost, 0);

        /// <summary>判断当前存档是否已经获得该插件（关联物品是否在强化品存储里）。</summary>
        public bool IsObtained => ObtainInfoOrNull() != null;

        /// <summary>
        /// 判断当前存档是否正在启用该插件。启用状态存在 <c>ObtainInfo.top_grade</c> 的 bit 1（值 2）上，
        /// bit 0 是收藏状态，两者互不干扰。
        /// </summary>
        public bool IsActive
        {
            get
            {
                ItemStorage.ObtainInfo info = ObtainInfoOrNull();
                return info != null && IsActiveGrade(SafeTopGrade(info));
            }
        }

        /// <summary>
        /// 查询当前能不能启用该插件以及拒绝原因。<b>纯查询</b>：不改 grade、不改槽位、不触发任何重算。
        /// 已经启用时返回 <see cref="GameEnhancerActivationStatus.Active"/>。
        /// </summary>
        public GameEnhancerActivationStatus ActivationStatus
        {
            get
            {
                try
                {
                    ItemStorage storage = GameBinding.EnhancerStorage;
                    if (storage == null || enhancer == null)
                    {
                        return GameEnhancerActivationStatus.StorageUnavailable;
                    }

                    ItemStorage.ObtainInfo info = ObtainInfoOrNull();
                    if (info == null)
                    {
                        return GameEnhancerActivationStatus.NotObtained;
                    }

                    if (IsActiveGrade(SafeTopGrade(info)))
                    {
                        return GameEnhancerActivationStatus.Active;
                    }

                    // 战斗边界期间原版 UI 禁止切换插件；公开 API 也必须如实报告拒绝而不是假装可用。
                    if (EnemySummoner.isActiveBorder())
                    {
                        return GameEnhancerActivationStatus.RejectedByState;
                    }

                    return enhancer.cost <= PolarisAPI.Game.Enhancers.RemainingSlots
                        ? GameEnhancerActivationStatus.Inactive
                        : GameEnhancerActivationStatus.NotEnoughSlots;
                }
                catch (Exception)
                {
                    return GameEnhancerActivationStatus.Failed;
                }
            }
        }

        /// <summary>
        /// 获得该插件（把关联物品放进强化品存储）。已经拥有时不重复变更并返回 <c>false</c>。
        /// <paramref name="notify"/> 只控制是否显示原版获得通知，不影响数据修改和回调。
        /// </summary>
        public bool Obtain(bool notify = false)
        {
            EnsureUsable();

            if (IsObtained)
            {
                return false;
            }

            NelItem item = NativeItemOrNull();
            NelItemManager manager = GameBinding.ItemManager;
            if (item == null || manager == null)
            {
                return false;
            }

            try
            {
                // 走 getItem 这条高层路径,而不是直接往存储里 Add:那样会绕过 obtain_count、
                // 获得通知和"该放进哪个仓库"的选择。
                manager.getItem(item, 1, 0, notify);
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "GameEnhancer.Obtain");
                return false;
            }

            if (!IsObtained)
            {
                return false;
            }

            GameEnhancerRuntime.PublishFor(this);
            return true;
        }

        /// <summary>
        /// 移除该插件。若正在启用，先原子停用并重算，再从强化品存储移除。
        /// 返回状态是否<b>实际</b>发生了变化。
        /// </summary>
        public bool Revoke(bool notify = false)
        {
            EnsureUsable();

            ItemStorage storage = GameBinding.EnhancerStorage;
            NelItem item = NativeItemOrNull();
            if (storage == null || item == null || !IsObtained)
            {
                return false;
            }

            try
            {
                if (IsActive && !WriteActiveGrade(false))
                {
                    // 停用没成功就不往下走,避免留下"已移除但属性还按启用算"的半状态。
                    return false;
                }

                // 刻意不走 NelItemManager.reduceItem：那个方法会遍历全部仓库,
                // 万一别处存在同 key 的异常数据会被一并删掉。这里只动强化品存储。
                storage.Reduce(item, 1, -1);
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "GameEnhancer.Revoke");
                return false;
            }

            if (IsObtained)
            {
                return false;
            }

            GameEnhancerRuntime.Recalculate();
            GameEnhancerRuntime.PublishFor(this);
            return true;
        }

        /// <summary>
        /// 启用或停用该插件，返回这次操作的确定结果：
        /// 成功启用得到 <see cref="GameEnhancerActivationStatus.Active"/>，成功停用得到
        /// <see cref="GameEnhancerActivationStatus.Inactive"/>；已经是目标状态时同样返回该状态（无变化）；
        /// 被拒绝时返回具体原因。启用时检查所有权、槽位与原版限制，并始终保留收藏 bit。
        /// </summary>
        public GameEnhancerActivationStatus SetActive(bool active)
        {
            EnsureUsable();

            GameEnhancerActivationStatus status = ActivationStatus;

            if (active)
            {
                // Inactive 是唯一"可以启用"的起点；其余（未获得/槽位不足/状态拒绝/存储未就绪）原样返回。
                if (status != GameEnhancerActivationStatus.Inactive)
                {
                    return status;
                }
            }
            else
            {
                if (status == GameEnhancerActivationStatus.Inactive)
                {
                    return GameEnhancerActivationStatus.Inactive;
                }

                if (status != GameEnhancerActivationStatus.Active)
                {
                    return status;
                }

                // 停用同样受原版"战斗边界不可改动"的限制；ActivationStatus 在已启用时会短路返回
                // Active，检查不到这一条，所以这里补一次。
                try
                {
                    if (EnemySummoner.isActiveBorder())
                    {
                        return GameEnhancerActivationStatus.RejectedByState;
                    }
                }
                catch (Exception)
                {
                    return GameEnhancerActivationStatus.Failed;
                }
            }

            if (!WriteActiveGrade(active))
            {
                return GameEnhancerActivationStatus.Failed;
            }

            if (!GameEnhancerRuntime.Recalculate())
            {
                return GameEnhancerActivationStatus.Failed;
            }

            // 重算完成后才发布,保证订阅者读到的属性/连接已经是新状态（计划第 3 节）。
            GameEnhancerRuntime.PublishFor(this);

            return IsActive ? GameEnhancerActivationStatus.Active : GameEnhancerActivationStatus.Inactive;
        }

        /// <summary>
        /// 写入启用位。启用状态是 grade 的 bit 1，收藏是 bit 0；两者共用同一个 grade 值，
        /// 所以改启用时必须把收藏位原样带过去。
        /// </summary>
        bool WriteActiveGrade(bool active)
        {
            ItemStorage.ObtainInfo info = ObtainInfoOrNull();
            if (info == null)
            {
                return false;
            }

            try
            {
                int favouriteBit = SafeTopGrade(info) & 1;
                info.changeGradeForPrecious(active ? favouriteBit | 2 : favouriteBit);
                return true;
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "GameEnhancer.SetActive");
                return false;
            }
        }

        /// <summary>关联物品的 key；定义读不出来时为 <c>null</c>。</summary>
        internal string ItemKey
        {
            get
            {
                string key = SafeKey();
                return string.IsNullOrEmpty(key) ? null : ItemKeyPrefix + key;
            }
        }

        /// <summary>取该插件在强化品存储里的记录；没有获得或存储未就绪时为 <c>null</c>。</summary>
        internal ItemStorage.ObtainInfo ObtainInfoOrNull()
        {
            try
            {
                ItemStorage storage = GameBinding.EnhancerStorage;
                NelItem item = NativeItemOrNull();
                return storage == null || item == null ? null : storage.getInfo(item);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>取关联物品的底层定义对象。</summary>
        internal NelItem NativeItemOrNull()
        {
            string itemKey = ItemKey;
            if (string.IsNullOrEmpty(itemKey))
            {
                return null;
            }

            try
            {
                // no_error: true——插件没有对应物品是正常分支,不应产生日志错误。
                return NelItem.GetById(itemKey, true);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>启用状态是 grade 的 bit 1（值 2）；bit 0 是收藏，读取时必须屏蔽掉。</summary>
        internal static bool IsActiveGrade(int grade) => (grade & 2) == 2;

        internal static int SafeTopGrade(ItemStorage.ObtainInfo info)
        {
            try
            {
                return info?.top_grade ?? 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        string SafeKey()
        {
            try
            {
                return enhancer?.key;
            }
            catch (Exception)
            {
                return null;
            }
        }

        TValue Read<TValue>(Func<ENHA.Enhancer, TValue> read, TValue fallback)
        {
            if (enhancer == null)
            {
                return fallback;
            }

            try
            {
                return read(enhancer);
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
