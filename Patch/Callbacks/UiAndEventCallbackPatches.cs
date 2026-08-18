using HarmonyLib;
using evt;
using nel.gm;
using Polaris.API;

namespace Polaris.Patch
{
    /// <summary>纯通知，不改变行为；与世界暂停的 transpiler 补丁类独立叠加在同一方法上。</summary>
    [HarmonyPatch(typeof(UiGameMenu), nameof(UiGameMenu.activate))]
    internal static class Patch_UiGameMenu_activate_Notify
    {
        [HarmonyPostfix]
        static void Postfix(UiGameMenu __instance) => GameCallbackPublishers.GameMenuOpened(__instance);
    }

    /// <summary>菜单关闭。</summary>
    [HarmonyPatch(typeof(UiGameMenu), nameof(UiGameMenu.deactivate))]
    internal static class Patch_UiGameMenu_deactivate_Notify
    {
        [HarmonyPostfix]
        static void Postfix(UiGameMenu __instance) => GameCallbackPublishers.GameMenuClosed(__instance);
    }

    /// <summary>事件真正成为当前栈顶时记账；仅入栈但尚未启动的事件不提前发布。</summary>
    [HarmonyPatch(typeof(EV), nameof(EV.evStart))]
    internal static class Patch_EV_evStart_Callbacks
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            string key = EV.curEv?.name;
            if (!string.IsNullOrEmpty(key))
            {
                GameEventRuntime.OnOpened(key);
            }
        }
    }

    /// <summary><c>EV.evEnd</c> 是事件结束的唯一出口。</summary>
    [HarmonyPatch(typeof(EV), nameof(EV.evEnd))]
    internal static class Patch_EV_evEnd_Callbacks
    {
        [HarmonyPostfix]
        static void Postfix(bool _all, bool __result)
        {
            if (__result)
            {
                // _all 为真是"整栈强制收掉"，那不是正常演完。
                GameEventRuntime.OnClosed(!_all);
            }
        }
    }
}
