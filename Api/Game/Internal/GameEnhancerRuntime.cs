using System;
using nel;

namespace Polaris.API
{
    /// <summary>
    /// 插件状态的观察与回调发布。两条变化来源共用这里的同一套差分逻辑，保证"恰好发一次"：
    /// <list type="bullet">
    /// <item>Polaris 自己写完后立即调 <see cref="PublishFor"/> 比较并发布，同时同步快照，避免下一帧重复发。</item>
    /// <item>原版菜单/事件命令直接改 grade 时，由 <see cref="Pump"/> 每帧差分补发。</item>
    /// </list>
    /// 首次观察、进入新游戏或读档后由 <see cref="Reset"/> 重建基线且不发回调，避免把存档初值误报成玩家操作。
    /// </summary>
    internal static class GameEnhancerRuntime
    {
        /// <summary>每帧观察已经建过包装器的插件；没人取过的定义没有订阅者，不值得为它每帧重建整个目录快照。</summary>
        internal static void Pump() => GameEnhancer.EachLive(static enhancer => PublishFor(enhancer));

        /// <summary>把当前状态与快照比较，有变化就按固定顺序入队回调，并同步快照。</summary>
        internal static void PublishFor(GameEnhancer enhancer)
        {
            if (enhancer == null || !enhancer.IsValid)
            {
                return;
            }

            bool obtained;
            bool active;
            try
            {
                obtained = enhancer.IsObtained;
                active = enhancer.IsActive;
            }
            catch (Exception)
            {
                return;
            }

            if (!enhancer.StateKnown)
            {
                // 首次观察只记录不发事件：这是存档初值,不是一次玩家操作。
                enhancer.CaptureBaseline(obtained, active);
                return;
            }

            bool previousObtained = enhancer.LastObtained;
            bool previousActive = enhancer.LastActive;
            if (obtained == previousObtained && active == previousActive)
            {
                return;
            }

            // 先更新快照再发布：订阅者可能同步回调进来再读一次状态,不能让它看到旧快照。
            enhancer.CaptureBaseline(obtained, active);

            // 顺序固定（计划 5.5）：获得并启用是"先获得后启用"；启用中被移除是"先停用后失去"。
            bool obtainedGained = obtained && !previousObtained;

            if (obtainedGained && obtained != previousObtained)
            {
                PublishObtained(enhancer, previousObtained, obtained);
            }

            if (active != previousActive)
            {
                PublishActive(enhancer, previousActive, active);
            }

            if (!obtainedGained && obtained != previousObtained)
            {
                PublishObtained(enhancer, previousObtained, obtained);
            }
        }

        static void PublishObtained(GameEnhancer enhancer, bool previous, bool current)
            => GameCallbackHub.PublishInstance(
                GameInstanceCallbackKind.EnhancerObtainedChanged,
                enhancer,
                () => new EnhancerObtainedChangedCallbackData(enhancer, previous, current));

        static void PublishActive(GameEnhancer enhancer, bool previous, bool current)
            => GameCallbackHub.PublishInstance(
                GameInstanceCallbackKind.EnhancerActiveChanged,
                enhancer,
                () => new EnhancerActiveChangedCallbackData(enhancer, previous, current));

        /// <summary>
        /// 状态写入后的原版重算。<c>fineEnhancerStorage</c> 会修正超出槽位的非法启用项并重建
        /// <c>enhancer_bits</c>；<c>resetSkillConnectionWhole</c> 让属性/技能连接跟上。
        /// 两者都要跑完，回调才允许入队（计划第 3 节）。
        /// </summary>
        internal static bool Recalculate()
        {
            try
            {
                ItemStorage precious = GameBinding.PreciousStorage;
                ItemStorage enhancerStorage = GameBinding.EnhancerStorage;
                if (precious == null || enhancerStorage == null)
                {
                    return false;
                }

                ENHA.fineEnhancerStorage(precious, enhancerStorage);
                M2PrSkill.resetSkillConnectionWhole();
                return true;
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "GameEnhancer.Recalculate");
                return false;
            }
        }

        /// <summary>新游戏、读档或目录重建后重置全部观察基线；下一次 <see cref="Pump"/> 只记录不发回调。</summary>
        internal static void Reset() => GameEnhancer.EachLive(static enhancer => enhancer.ClearBaseline());
    }
}
