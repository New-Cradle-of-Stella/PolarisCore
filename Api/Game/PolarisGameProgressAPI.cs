using System;
using nel;
using XX;

namespace Polaris
{
    public static partial class PolarisAPI
    {
        public static partial class Game
        {
            /// <summary>
            /// 剧情进度状态：具名全局旗标、具名全局计数器、剧情标记与主进度，这四类都是**持久状态**——写进去就进存档，
            /// 不随场景、事件或地图切换回滚。所以每个写入入口都返回"是否实际写入"而非静默成功，调用方需要能区分"写了"和"这个键不存在"。
            /// </summary>
            /// <remarks>
            /// 键名校验是这一节存在的主要理由：原版 GFB/GFC 底层是位数组，键名靠名字表映射到下标，未登记的键会落到不确定的下标上、
            /// 踩到别的剧情旗标，因此所有写入都先过 <see cref="HasFlag"/> / <see cref="HasCounter"/>。
            /// 剧情标记（SF）本身是字符串键字典没有这个问题；变更统一由 Core 自己的补丁发布 <c>GameStaticCallbackKind.StoryFlagChanged</c> 通知，不论改动来源。
            /// </remarks>
            public static class Progress
            {
                /// <summary>判断全局旗标系统是否已经初始化；读档之前它是空的。</summary>
                public static bool IsReady => Safe(static () => GF.initted, false);

                // ---- 具名全局旗标（原版 GFB）----

                /// <summary>判断当前游戏版本是否定义了这个旗标键名。写入前必须先问它。</summary>
                public static bool HasFlag(string key)
                    => !string.IsNullOrEmpty(key) && Safe(() => GF.Onamed_b != null && GF.Onamed_b.ContainsKey(key), false);

                /// <summary>读取具名全局旗标；键名未定义时返回 <c>false</c>。</summary>
                public static bool GetFlag(string key)
                    => HasFlag(key) && Safe(() => GF.getB(key), false);

                /// <summary>
                /// 写入具名全局旗标并返回是否实际写入。键名未定义时一位都不改并返回 <c>false</c>——
                /// 底层是位数组，未定义的键会落到不确定的下标上。
                /// </summary>
                public static bool SetFlag(string key, bool value)
                {
                    if (!HasFlag(key))
                    {
                        return false;
                    }

                    try
                    {
                        GF.setB(key, value);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Errors.Report(ex, "Game.Progress.SetFlag");
                        return false;
                    }
                }

                // ---- 具名全局计数器（原版 GFC）----

                /// <summary>判断当前游戏版本是否定义了这个计数器键名。写入前必须先问它。</summary>
                public static bool HasCounter(string key)
                    => !string.IsNullOrEmpty(key) && Safe(() => GF.Onamed_c != null && GF.Onamed_c.ContainsKey(key), false);

                /// <summary>读取具名全局计数器；键名未定义时返回 <c>0</c>。计数器在原版是无符号的。</summary>
                public static uint GetCounter(string key)
                    => HasCounter(key) ? Safe(() => GF.getC(key), 0u) : 0u;

                /// <summary>写入具名全局计数器并返回是否实际写入；键名未定义时不做任何事。</summary>
                public static bool SetCounter(string key, uint value)
                {
                    if (!HasCounter(key))
                    {
                        return false;
                    }

                    try
                    {
                        GF.setC(key, value);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Errors.Report(ex, "Game.Progress.SetCounter");
                        return false;
                    }
                }

                /// <summary>
                /// 把具名全局计数器抬高到至少 <paramref name="minimum"/> 并返回最终值；
                /// 当前值已经不小于它时不写入。对应原版的"取较大值"语义。
                /// </summary>
                public static uint RaiseCounter(string key, uint minimum)
                {
                    uint current = GetCounter(key);
                    if (current >= minimum)
                    {
                        return current;
                    }

                    return SetCounter(key, minimum) ? minimum : current;
                }

                // ---- 剧情标记（原版 SF）----

                /// <summary>
                /// 读取剧情标记的整数值；键不存在时返回 <c>0</c>。
                /// 剧情标记是字符串键字典，没有"键名必须预先定义"的限制。
                /// </summary>
                public static int GetStoryFlag(string key)
                    => string.IsNullOrEmpty(key) ? 0 : Safe(() => COOK.getSF(key), 0);

                /// <summary>
                /// 写入剧情标记并返回是否实际写入。
                /// 变化会通过 <c>GameStaticCallbackKind.StoryFlagChanged</c> 通知。
                /// </summary>
                public static bool SetStoryFlag(string key, int value)
                {
                    if (string.IsNullOrEmpty(key))
                    {
                        return false;
                    }

                    try
                    {
                        COOK.setSF(key, value);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Errors.Report(ex, "Game.Progress.SetStoryFlag");
                        return false;
                    }
                }

                /// <summary>判断剧情标记是否为非零值。</summary>
                public static bool HasStoryFlag(string key) => GetStoryFlag(key) != 0;

                // ---- 主进度（原版 PVV）----

                /// <summary>读取主进度的原版显示文本；取不到时为 <c>null</c>。</summary>
                public static string MainProgressText => Safe(static () => GF.getPVV(), null);

                /// <summary>
                /// 推进主进度并返回是否实际写入。
                /// </summary>
                /// <remarks>
                /// 只暴露原版 <c>PVV</c> 的语义。原版另有一个 <c>PVV_ABSOLUTE</c> 变体走同一个方法的
                /// 第二个参数，但那个参数的确切含义还没有动态验证过，所以这里固定按非绝对方式调用，
                /// 不提供一个含义不明的开关。
                /// </remarks>
                public static bool SetMainProgress(int value)
                {
                    if (value < 0)
                    {
                        return false;
                    }

                    try
                    {
                        GF.setPVV(value, false);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Errors.Report(ex, "Game.Progress.SetMainProgress");
                        return false;
                    }
                }
            }
        }
    }
}
