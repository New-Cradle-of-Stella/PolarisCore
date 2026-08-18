using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using nel;
using nel.gm;
using Polaris.API;

namespace Polaris.Patch
{
    /// <summary>
    /// 把 <c>UiGameMenu.activate()</c> 里原本的 <c>M2D.PauseMem(bool)</c> 调用替换成
    /// <see cref="GameMenuPauseRuntime.OnMenuPauseMemory"/>。
    /// </summary>
    [HarmonyPatch(typeof(UiGameMenu), nameof(UiGameMenu.activate))]
    internal static class Patch_UiGameMenu_activate_GameMenuWorldPause
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var pauseMem = AccessTools.Method(typeof(NelM2DBase), nameof(NelM2DBase.PauseMem));
            var replacement = AccessTools.Method(typeof(GameMenuPauseRuntime), nameof(GameMenuPauseRuntime.OnMenuPauseMemory));

            var codeMatcher = new CodeMatcher(instructions);

            codeMatcher.MatchStartForward(new CodeMatch(ins =>
                    (ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt) && ins.OperandIs(pauseMem)))
                .ThrowIfInvalid("Could not find the M2D.PauseMem(bool) call inside UiGameMenu.activate")
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Call, replacement));

            return codeMatcher.Instructions();
        }
    }
}
