using System;
using System.Collections.Generic;
using nel;
using Polaris.API;

namespace Polaris
{
    public static partial class PolarisAPI
    {
        public static partial class Game
        {
            /// <summary>
            /// 游戏内插件（Enhancer）的目录与槽位查询入口。单个插件的读写在
            /// <see cref="API.GameEnhancer"/> 实例上。
            /// </summary>
            public static class Enhancers
            {
                /// <summary>贵重品存储里代表"插件槽位"的物品 key；总槽位就是它的持有数量。</summary>
                const string SlotItemKey = "enhancer_slot";

                /// <summary>按稳定键名取得插件实例；当前游戏版本没有该插件时返回 <c>null</c>。</summary>
                public static API.GameEnhancer Resolve(string enhancerKey)
                {
                    if (string.IsNullOrEmpty(enhancerKey))
                    {
                        return null;
                    }

                    try
                    {
                        return API.GameEnhancer.Wrap(ENHA.Get(enhancerKey));
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }

                /// <summary>
                /// 取得当前游戏版本定义的全部插件，保持原版 <c>ENHA.AEh</c> 的定义顺序。
                /// 返回只读快照；目录还没初始化时返回空列表而不是 <c>null</c>。
                /// </summary>
                public static IReadOnlyList<API.GameEnhancer> GetAll()
                {
                    List<ENHA.Enhancer> definitions;
                    try
                    {
                        definitions = ENHA.AEh;
                    }
                    catch (Exception)
                    {
                        return Array.Empty<API.GameEnhancer>();
                    }

                    if (definitions == null)
                    {
                        return Array.Empty<API.GameEnhancer>();
                    }

                    var result = new List<API.GameEnhancer>(definitions.Count);
                    foreach (ENHA.Enhancer definition in definitions)
                    {
                        API.GameEnhancer wrapper = API.GameEnhancer.Wrap(definition);
                        if (wrapper != null)
                        {
                            result.Add(wrapper);
                        }
                    }

                    return result;
                }

                /// <summary>
                /// 读取当前存档可用于启用插件的总槽位，也就是贵重品存储里 <c>enhancer_slot</c> 的持有数量。
                /// 刻意重算而不是读原版的 <c>ENHA.max_slot</c> 缓存：那个值只在
                /// <c>fineEnhancerStorage</c> 跑过之后才准。存档未加载时为 0。
                /// </summary>
                public static int SlotCapacity => Safe(
                    static () =>
                    {
                        ItemStorage precious = GameBinding.PreciousStorage;
                        NelItem slotItem = NelItem.GetById(SlotItemKey, true);
                        return precious == null || slotItem == null ? 0 : precious.getCount(slotItem);
                    },
                    0);

                /// <summary>
                /// 读取当前已启用插件占用的槽位，即所有<b>有效</b>启用项的 <c>Cost</c> 之和。
                /// 同样按存储现算，不解析 UI 上的 <c>"used/max"</c> 文本。
                /// </summary>
                public static int UsedSlots => Safe(
                    static () =>
                    {
                        ItemStorage storage = GameBinding.EnhancerStorage;
                        if (storage == null)
                        {
                            return 0;
                        }

                        int used = 0;
                        foreach (KeyValuePair<NelItem, ItemStorage.ObtainInfo> entry in storage.getWholeInfoDictionary())
                        {
                            if (!API.GameEnhancer.IsActiveGrade(API.GameEnhancer.SafeTopGrade(entry.Value)))
                            {
                                continue;
                            }

                            ENHA.Enhancer definition = ENHA.Get(entry.Key);
                            if (definition != null)
                            {
                                used += definition.cost;
                            }
                        }

                        return used;
                    },
                    0);

                /// <summary>读取当前剩余槽位；等于总槽位减已用槽位，且不小于零（旧存档可能残留超额启用项）。</summary>
                public static int RemainingSlots
                {
                    get
                    {
                        int remaining = SlotCapacity - UsedSlots;
                        return remaining < 0 ? 0 : remaining;
                    }
                }
            }
        }
    }
}
