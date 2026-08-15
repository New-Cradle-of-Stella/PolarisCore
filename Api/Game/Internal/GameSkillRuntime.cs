using System;
using nel;

namespace Polaris.API
{
    /// <summary>
    /// 技能状态的观察与回调发布，结构与 <see cref="GameEnhancerRuntime"/> 对称：
    /// API 自己写完立即 <see cref="PublishFor"/>（比较、发布、同步快照），原版菜单/事件命令
    /// 直接改 <c>manip_bits</c> 时由 <see cref="Pump"/> 每帧差分补发，两条来源各自只发一次。
    /// </summary>
    internal static class GameSkillRuntime
    {
        internal static void Pump() => GameSkill.EachLive(static skill => PublishFor(skill));

        internal static void PublishFor(GameSkill skill)
        {
            if (skill == null || !skill.IsValid)
            {
                return;
            }

            bool obtained;
            bool enabled;
            byte manipBits;
            try
            {
                obtained = skill.IsObtained;
                enabled = skill.IsEnabled;
                manipBits = skill.Native?.manip_bits ?? 0;
            }
            catch (Exception)
            {
                return;
            }

            if (!skill.StateKnown)
            {
                skill.CaptureBaseline(obtained, enabled, manipBits);
                return;
            }

            bool previousObtained = skill.LastObtained;
            bool previousEnabled = skill.LastEnabled;
            byte previousManip = skill.LastManipBits;

            if (obtained == previousObtained && enabled == previousEnabled && manipBits == previousManip)
            {
                return;
            }

            skill.CaptureBaseline(obtained, enabled, manipBits);

            // 顺序固定（计划 5.5）：获得并启用＝先 Obtained 后 Enabled；
            // 启用中被移除＝先 Enabled 后 Obtained。
            bool obtainedGained = obtained && !previousObtained;

            if (obtainedGained && obtained != previousObtained)
            {
                PublishObtained(skill, previousObtained, obtained);
            }

            if (enabled != previousEnabled)
            {
                PublishEnabled(skill, previousEnabled, enabled);
            }

            if (!obtainedGained && obtained != previousObtained)
            {
                PublishObtained(skill, previousObtained, obtained);
            }

            PublishManipulationDiff(skill, previousManip, manipBits);
        }

        /// <summary>操作方式逐位比较；每个实际变化的选项各发一次，载荷带 option 与 previous/current。</summary>
        static void PublishManipulationDiff(GameSkill skill, byte previous, byte current)
        {
            // bit 0 是 enabled，已经由 SkillEnabledChanged 覆盖，这里只看选项位 1..7。
            int changed = (previous ^ current) & 0xFE;
            if (changed == 0)
            {
                return;
            }

            int count = skill.ManipulationCount;
            for (int option = 0; option < count; option++)
            {
                int mask = 2 << option;
                if ((changed & mask) == 0)
                {
                    continue;
                }

                bool before = (previous & mask) != 0;
                bool after = (current & mask) != 0;
                GameCallbackHub.PublishInstance(
                    GameInstanceCallbackKind.SkillManipulationChanged,
                    skill,
                    () => new SkillManipulationChangedCallbackData(skill, option, before, after));
            }
        }

        static void PublishObtained(GameSkill skill, bool previous, bool current)
            => GameCallbackHub.PublishInstance(
                GameInstanceCallbackKind.SkillObtainedChanged,
                skill,
                () => new SkillObtainedChangedCallbackData(skill, previous, current));

        static void PublishEnabled(GameSkill skill, bool previous, bool current)
            => GameCallbackHub.PublishInstance(
                GameInstanceCallbackKind.SkillEnabledChanged,
                skill,
                () => new SkillEnabledChangedCallbackData(skill, previous, current));

        /// <summary>技能连接重算。必须在回调入队之前跑完（计划第 3 节）。</summary>
        internal static bool Recalculate()
        {
            try
            {
                M2PrSkill.resetSkillConnectionWhole();
                return true;
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "GameSkill.Recalculate");
                return false;
            }
        }

        /// <summary>原版菜单/事件当前是否禁止改动技能，与 <c>UiSkillManageBox.fnClickCheckboxEnable</c> 的第一道闸同源。</summary>
        internal static bool IsEditingBlocked()
        {
            try
            {
                return evt.EV.isStoppingGameHandle();
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void Reset() => GameSkill.EachLive(static skill => skill.ClearBaseline());
    }
}
