using nel;
using nel.gm;

namespace Polaris.API
{
    /// <summary>
    /// <see cref="GameMenuAPI.SetWorldPause"/> 的运行时内核：跟踪 PauseMem/ResumeMem 的归属。
    /// 只在 Unity 主线程读写。
    /// </summary>
    internal static class GameMenuPauseRuntime
    {
        /// <summary>标识 <see cref="ReportPatchApplied"/> 汇报的是哪一个 Harmony 转译补丁。</summary>
        internal enum PatchTarget
        {
            Activate,
            Deactivate
        }

        /// <summary><c>UiGameMenu.activate()</c> 里的转译补丁是否成功找到并替换了它的目标调用。</summary>
        internal static bool ActivatePatchApplied { get; private set; }

        /// <summary><c>UiGameMenu.deactivate()</c> 里的转译补丁是否成功找到并替换了它的目标调用。</summary>
        internal static bool DeactivatePatchApplied { get; private set; }

        /// <summary>由对应的转译补丁在成功替换目标调用后回报，供后续健康检查判断两个补丁是否都生效。</summary>
        internal static void ReportPatchApplied(PatchTarget target)
        {
            switch (target)
            {
                case PatchTarget.Activate:
                    ActivatePatchApplied = true;
                    break;
                case PatchTarget.Deactivate:
                    DeactivatePatchApplied = true;
                    break;
            }
        }

        /// <summary>ESC 菜单打开时是否暂停世界；默认 <c>true</c>，与原版行为一致。</summary>
        internal static bool PauseWorldWhileOpen = true;

        /// <summary>这次菜单打开是否是由 Polaris（经 <see cref="OnMenuPauseMemory"/>）真正执行过 <c>PauseMem</c>。
        /// 只有这个标志为真，关闭/切换时才有资格配对调用 <c>ResumeMem</c>。</summary>
        internal static bool MenuPauseApplied;

        /// <summary>上一次 <see cref="Pump"/> 时，外部暂停（转场/冻结器）是否处于活动状态，用于探测下降沿。</summary>
        internal static bool ExternalStopWasActive;

        internal static void SetPolicy(bool enabled)
        {
            if (PauseWorldWhileOpen == enabled)
            {
                return;
            }

            PauseWorldWhileOpen = enabled;
            Reconcile();
        }

        /// <summary>替换 <c>UiGameMenu.activate()</c> 里原本的 <c>M2D.PauseMem(true)</c> 调用。</summary>
        internal static void OnMenuPauseMemory(NelM2DBase m2d, bool particleSetterStop)
        {
            if (PauseWorldWhileOpen)
            {
                m2d.PauseMem(particleSetterStop);
                MenuPauseApplied = true;
                return;
            }

            MenuPauseApplied = false;
        }

        /// <summary>替换 <c>UiGameMenu.deactivate()</c> 里原本的 <c>M2D.ResumeMem(true)</c> 调用。</summary>
        internal static void OnMenuResumeMemory(NelM2DBase m2d, bool particleSetterResume)
        {
            if (MenuPauseApplied && !IsExternalStopActive())
            {
                m2d.ResumeMem(particleSetterResume);
            }

            MenuPauseApplied = false;
        }

        /// <summary>替换 <c>NelM2DBase.run()</c>/<c>runPostForDraw()</c> 里原本的 <c>GM.isStoppingGame()</c> 调用。</summary>
        internal static bool ShouldStopWorld(UiGameMenu gm)
        {
            bool vanilla = gm.isStoppingGame();
            if (!vanilla)
            {
                return false;
            }

            // 真正的终止/关闭状态一律照原版停；只解除"菜单打开且策略为 false"造成的停止。
            if (gm.isClosingGame() || GAMEOVER.isActive())
            {
                return true;
            }

            return PauseWorldWhileOpen;
        }

        static bool IsExternalStopActive()
        {
            NelM2DBase m2d = GameBinding.NelM2D;
            if (m2d == null)
            {
                return false;
            }

            return m2d.transferring_game_stopping || m2d.Freezer.isPausing();
        }

        /// <summary>由 <see cref="GameMenuAPI.Pump"/> 每帧调用：外部暂停从活动变为不活动时补一次对账，
        /// 让菜单重新拿回本应属于自己的 PauseMem/ResumeMem。不在绘制路径调用，避免产生副作用的时机不可控。</summary>
        internal static void Pump()
        {
            bool externalActive = IsExternalStopActive();
            if (ExternalStopWasActive && !externalActive)
            {
                Reconcile();
            }

            ExternalStopWasActive = externalActive;
        }

        static void Reconcile()
        {
            NelM2DBase m2d = GameBinding.NelM2D;
            UiGameMenu gm = m2d?.GM;
            if (gm == null || !gm.isActive() || IsExternalStopActive())
            {
                return;
            }

            if (PauseWorldWhileOpen && !MenuPauseApplied)
            {
                m2d.PauseMem(true);
                MenuPauseApplied = true;
            }
            else if (!PauseWorldWhileOpen && MenuPauseApplied)
            {
                m2d.ResumeMem(true);
                MenuPauseApplied = false;
            }
        }

        /// <summary>进程退出时调用，只清零本地标志，不触发任何 PauseMem/ResumeMem 副作用。</summary>
        internal static void Reset()
        {
            PauseWorldWhileOpen = true;
            MenuPauseApplied = false;
            ExternalStopWasActive = false;
        }
    }
}
