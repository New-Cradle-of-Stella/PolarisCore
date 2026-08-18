using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using nel;
using nel.gm;
using Polaris.API;

namespace Polaris.Patch
{
    /// <summary>
    /// 把 <c>UiGameMenu.deactivate()</c> 里原本的 <c>M2D.ResumeMem(bool)</c> 调用替换成
    /// <see cref="GameMenuPauseRuntime.OnMenuResumeMemory"/>。
    /// </summary>
    [HarmonyPatch(typeof(UiGameMenu), nameof(UiGameMenu.deactivate))]
    internal static class Patch_UiGameMenu_deactivate_GameMenuWorldPause
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var resumeMem = AccessTools.Method(typeof(NelM2DBase), nameof(NelM2DBase.ResumeMem));
            var replacement = AccessTools.Method(typeof(GameMenuPauseRuntime), nameof(GameMenuPauseRuntime.OnMenuResumeMemory));

            var codeMatcher = new CodeMatcher(instructions);

            codeMatcher.MatchStartForward(new CodeMatch(ins =>
                    (ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt) && ins.OperandIs(resumeMem)))
                .ThrowIfInvalid("Could not find the M2D.ResumeMem(bool) call inside UiGameMenu.deactivate")
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Call, replacement));

            return codeMatcher.Instructions();
        }
    }
}
