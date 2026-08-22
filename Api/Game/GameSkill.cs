using System;
using nel;

namespace Polaris.API
{
    /// <summary>
    /// 一个技能的定义，取得实例的入口是 <c>PolarisAPI.Game.Skills</c>。
    /// 与 <see cref="GameEnhancer"/> 同理，这是<b>目录定义</b>的包装器（身份跟随原版 <c>PrSkill</c> 对象引用，一局游戏内稳定），
    /// 获得/启用/操作方式等状态跟随当前存档变化。
    /// </summary>
    public sealed class GameSkill : GameInstance
    {
        /// <summary>技能书物品的 key 前缀。</summary>
        internal const string BookItemKeyPrefix = "skillbook_";

        /// <summary>
        /// 原版存档把 <c>manip_bits</c> 左移一位后写进<b>单个字节</b>
        /// （<c>SkillManager.writeBinaryTo</c>：<c>(new_icon?1:0) | (manip_bits &lt;&lt; 1)</c>），
        /// 所以公共 API 只暴露能稳定保存的操作选项 0..5。
        /// </summary>
        internal const int LastPersistedManipulationOption = 5;

        static readonly InstanceTable<PrSkill, GameSkill> Table = new();

        readonly PrSkill skill;

        GameSkill(PrSkill skill)
        {
            this.skill = skill;
        }

        // ── 观察快照（由 GameSkillRuntime 读写）─────────────────────────────────

        /// <summary>是否已经建立过基线。为 <c>false</c> 时下一次观察只记录、不发回调。</summary>
        internal bool StateKnown { get; private set; }

        internal bool LastObtained { get; private set; }

        internal bool LastEnabled { get; private set; }

        internal byte LastManipBits { get; private set; }

        internal void CaptureBaseline(bool obtained, bool enabled, byte manipBits)
        {
            LastObtained = obtained;
            LastEnabled = enabled;
            LastManipBits = manipBits;
            StateKnown = true;
        }

        internal void ClearBaseline() => StateKnown = false;

        internal static GameSkill Wrap(PrSkill native) => Table.Get(native, static n => new GameSkill(n));

        internal static GameSkill Peek(PrSkill native) => Table.Peek(native);

        internal static void SweepSkills() => Table.Sweep();

        internal static void InvalidateAllSkills() => Table.InvalidateAll();

        /// <summary>遍历已经建过包装器的技能，用于每帧状态差分。</summary>
        internal static void EachLive(Action<GameSkill> visit) => Table.Each(visit);

        /// <summary>本层内部拿回底层定义对象。</summary>
        internal PrSkill Native => skill;

        private protected override bool IsNativeAlive => skill != null;

        private protected override string Describe() => $"GameSkill({SafeKey()})";

        /// <summary>获取该技能的稳定键名。</summary>
        public string Key => SafeKey();

        /// <summary>取得关联技能书的物品定义（key 为 <c>skillbook_&lt;key&gt;</c>）；技能没有技能书时为 <c>null</c>。</summary>
        public GameItem BookItem
        {
            get
            {
                string key = SafeKey();
                return string.IsNullOrEmpty(key) ? null : GameItem.Resolve(BookItemKeyPrefix + key);
            }
        }

        /// <summary>获取该技能在当前语言下的显示名称。每次读取都跟随当前语言，不缓存。</summary>
        public string Title => Read(static s => s.title, null);

        /// <summary>获取该技能在当前语言下的说明。每次读取都跟随当前语言，不缓存。</summary>
        public string Description => Read(static s => s.descript, null);

        /// <summary>获取该技能的公共分类（位标志）。与原版 <c>SKILL_CTG</c> 的映射在本类内部显式维护。</summary>
        public GameSkillCategory Category => Read(
            static s => MapCategory(s.category), GameSkillCategory.None);

        /// <summary>判断该技能当前是否应在原版技能界面显示。</summary>
        public bool IsVisible => Read(static s => s.visible, false);

        /// <summary>判断该技能是否属于获得后不能经公开 API 禁用的常驻技能。</summary>
        public bool IsAlwaysEnabled => Read(static s => s.always_enable, false);

        /// <summary>
        /// 判断当前存档是否已经获得该技能。用定义层的 <c>visible</c>/<c>first_visible</c>，
        /// 不用 <c>M2PrSkill.isObtained(SKILL_TYPE)</c>——那个回答的是"能不能用这类动作",不是"这条定义有没有拿到"。
        /// </summary>
        public bool IsObtained => Read(static s => s.visible || s.first_visible, false);

        /// <summary>判断当前存档是否启用该技能（<c>manip_bits</c> 的 bit 0）。</summary>
        public bool IsEnabled => Read(static s => s.enabled, false);

        /// <summary>获取该技能可配置的操作方式数量；没有可配置项时为 0。</summary>
        public int ManipulationCount => Read(
            static s => s.manip_max <= 1 ? 0 : Math.Min((int)s.manip_max, LastPersistedManipulationOption + 1), 0);

        /// <summary>
        /// 读取指定操作方式在当前语言下的说明。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">序号超出 <see cref="ManipulationCount"/> 范围。</exception>
        public string GetManipulationText(int option)
        {
            int count = ManipulationCount;
            if (option < 0 || option >= count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(option), option, $"This skill has {count} configurable manipulation option(s).");
            }

            try
            {
                return skill?.getManipulateLocalized(option);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>查询指定操作方式是否启用；序号非法时返回 <c>false</c> 而非像 <see cref="GetManipulationText"/> 那样抛异常，方便按序号遍历。</summary>
        public bool IsManipulationEnabled(int option)
        {
            if (option < 0 || option >= ManipulationCount)
            {
                return false;
            }

            return Read(s => s.isManipEnable(option), false);
        }

        /// <summary>
        /// 获得该技能：首次获得走原版 <c>PrSkill.Obtain(!enable)</c>，已获得时不重复获得，
        /// 但 <paramref name="enable"/> 为 <c>true</c> 时仍会按契约确保它处于启用状态。
        /// 返回状态是否<b>实际</b>发生了变化。
        /// </summary>
        /// <param name="enable">是否同时启用。</param>
        public bool Obtain(bool enable = true)
        {
            EnsureUsable();

            if (skill == null)
            {
                return false;
            }

            bool wasObtained = IsObtained;
            bool wasEnabled = IsEnabled;

            try
            {
                if (!wasObtained)
                {
                    // do_not_enable 与 enable 语义相反。首次获得时原版自己会把
                    // manip_bits 设成 1|manip_default_bits 并重算连接。
                    skill.Obtain(!enable);
                }
                else if (enable && !wasEnabled)
                {
                    // 已经 visible 的技能 PrSkill.Obtain 不会再动它，但契约要求 enable=true 时确保启用。
                    if (!ApplyEnabled(true))
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "GameSkill.Obtain");
                return false;
            }

            bool changed = IsObtained != wasObtained || IsEnabled != wasEnabled;
            if (!changed)
            {
                return false;
            }

            GameSkillRuntime.Recalculate();
            GameSkillRuntime.PublishFor(this);
            return true;
        }

        /// <summary>
        /// 移除该技能并原子清除启用状态。<c>first_visible</c> 的技能不允许移除，返回 <c>false</c>。
        /// </summary>
        public bool Revoke()
        {
            EnsureUsable();

            if (skill == null || !IsObtained)
            {
                return false;
            }

            try
            {
                // 原版 ReleaseObtain 自己就拒绝 first_visible，这里先判一次是为了给出确定的返回值。
                if (skill.first_visible)
                {
                    return false;
                }

                skill.ReleaseObtain();
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "GameSkill.Revoke");
                return false;
            }

            if (IsObtained)
            {
                return false;
            }

            GameSkillRuntime.Recalculate();
            GameSkillRuntime.PublishFor(this);
            return true;
        }

        /// <summary>
        /// 启用或禁用该技能。未获得、<see cref="IsAlwaysEnabled"/> 常驻技能要求禁用、
        /// 或原版事件/编辑状态拒绝时返回 <c>false</c>。
        /// </summary>
        public bool SetEnabled(bool enabled)
        {
            EnsureUsable();

            if (skill == null || !IsObtained)
            {
                return false;
            }

            if (IsEnabled == enabled)
            {
                return false;
            }

            // always_enable 只限制"关闭"：这类技能获得后不能通过公开 API 禁用。
            if (!enabled && IsAlwaysEnabled)
            {
                return false;
            }

            if (GameSkillRuntime.IsEditingBlocked())
            {
                return false;
            }

            if (!ApplyEnabled(enabled))
            {
                return false;
            }

            GameSkillRuntime.Recalculate();
            GameSkillRuntime.PublishFor(this);
            return true;
        }

        /// <summary>
        /// 修改指定操作方式；未获得、技能未启用、序号非法或原版状态拒绝时返回 <c>false</c>。
        /// <c>manip_multi</c> 为 <c>false</c> 时启用一个选项会原子关闭其它选项，已启用技能的最后一个选项不允许关闭。
        /// </summary>
        public bool SetManipulationEnabled(int option, bool enabled)
        {
            EnsureUsable();

            if (skill == null || option < 0 || option >= ManipulationCount)
            {
                return false;
            }

            if (!IsObtained || !IsEnabled || GameSkillRuntime.IsEditingBlocked())
            {
                return false;
            }

            try
            {
                int mask = 2 << option;
                byte before = skill.manip_bits;

                if (enabled)
                {
                    // 单选技能：先清掉全部选项位（保留 bit 0 的 enabled），再点亮这一个。
                    byte next = skill.manip_multi ? before : (byte)(before & 1);
                    next |= (byte)mask;
                    if (next == before)
                    {
                        return false;
                    }

                    skill.manip_bits = next;
                }
                else
                {
                    if ((before & mask) == 0)
                    {
                        return false;
                    }

                    // 启用中的技能不能落到零个操作方式。
                    if ((before & 0xFE & ~mask) == 0)
                    {
                        return false;
                    }

                    skill.manip_bits = (byte)(before & ~mask);
                }
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "GameSkill.SetManipulationEnabled");
                return false;
            }

            GameSkillRuntime.Recalculate();
            GameSkillRuntime.PublishFor(this);
            return true;
        }

        /// <summary>
        /// 写 <c>enabled</c> 位。启用时若一个操作方式都没点亮，补上默认位——原版读档
        /// （<c>SkillManager.readBinaryFrom</c>）也做同样的修补，不能留下"已启用但零个操作方式"。
        /// </summary>
        bool ApplyEnabled(bool enabled)
        {
            try
            {
                skill.enabled = enabled;

                if (enabled && skill.manip_max > 1 && (skill.manip_bits & 0xFE) == 0)
                {
                    byte defaults = (byte)(skill.manip_default_bits & 0xFE);
                    skill.manip_bits |= defaults != 0 ? defaults : (byte)2;
                }

                return true;
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "GameSkill.SetEnabled");
                return false;
            }
        }

        /// <summary>
        /// 原版 <c>SKILL_CTG</c> 到公共分类的显式映射。刻意逐位列出而不是整体数值强转：
        /// 那个枚举的数值不保证跨版本稳定，而且 <c>HPMP</c> 本身就是 <c>HP|MP</c> 的组合值
        /// （24 = 8|16），强转会让模组在下一次游戏更新后静默错位。
        /// </summary>
        static GameSkillCategory MapCategory(SkillManager.SKILL_CTG category)
        {
            GameSkillCategory result = GameSkillCategory.None;

            if ((category & SkillManager.SKILL_CTG.PUNCH) != 0) result |= GameSkillCategory.Melee;
            if ((category & SkillManager.SKILL_CTG.MAGIC) != 0) result |= GameSkillCategory.Magic;
            if ((category & SkillManager.SKILL_CTG.GUARD) != 0) result |= GameSkillCategory.Guard;
            if ((category & SkillManager.SKILL_CTG.HP) != 0) result |= GameSkillCategory.HpGrowth;
            if ((category & SkillManager.SKILL_CTG.MP) != 0) result |= GameSkillCategory.MpGrowth;
            if ((category & SkillManager.SKILL_CTG.SPECIAL) != 0) result |= GameSkillCategory.Special;
            if ((category & SkillManager.SKILL_CTG.ONLY_ALICE) != 0) result |= GameSkillCategory.AliceOnly;
            if ((category & SkillManager.SKILL_CTG.ONLY_NOEL) != 0) result |= GameSkillCategory.NoelOnly;

            return result;
        }

        string SafeKey()
        {
            try
            {
                return skill?.key;
            }
            catch (Exception)
            {
                return null;
            }
        }

        TValue Read<TValue>(Func<PrSkill, TValue> read, TValue fallback)
        {
            if (skill == null)
            {
                return fallback;
            }

            try
            {
                return read(skill);
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
