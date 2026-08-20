using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using m2d;
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
            var replacement = AccessTools.Method(typeof(GameMenuPauseRuntime), nameof(GameMenuPauseRuntime.OnMenuResumeMemory),
                new[] { typeof(NelM2DBase), typeof(bool) });

            var codeMatcher = new CodeMatcher(instructions);

            codeMatcher.MatchStartForward(new CodeMatch(ins =>
                    IsMemoryCall(ins, nameof(M2DBase.ResumeMem))))
                .ThrowIfInvalid("Could not find the M2D.ResumeMem(bool) call inside UiGameMenu.deactivate")
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Call, replacement));

            GameMenuPauseRuntime.ReportPatchApplied(GameMenuPauseRuntime.PatchTarget.Deactivate);
            return codeMatcher.Instructions();
        }

        static bool IsMemoryCall(CodeInstruction instruction, string methodName)
        {
            if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
                return false;

            if (!(instruction.operand is MethodInfo method) || method.Name != methodName)
                return false;

            ParameterInfo[] parameters = method.GetParameters();
            return method.DeclaringType != null
                && typeof(M2DBase).IsAssignableFrom(method.DeclaringType)
                && parameters.Length == 1
                && parameters[0].ParameterType == typeof(bool);
        }
    }
}
