using System;
using nel;
using Polaris.API;

namespace Polaris
{
    public static partial class PolarisAPI
    {
        public static partial class Game
        {
            /// <summary>原版自动存档入口。写入在调用返回前完成。</summary>
            public static class Save
            {
                /// <summary>判断当前玩家与地图状态是否允许普通自动存档。</summary>
                public static bool CanAutosave => Safe(static () => COOK.canSave(), false);

                /// <summary>请求自动存档；Force 模式要求调用方先确认游戏状态安全。</summary>
                public static bool RequestAutosave(GameAutosaveMode mode = GameAutosaveMode.Normal)
                {
                    if (!Enum.IsDefined(typeof(GameAutosaveMode), mode))
                    {
                        return false;
                    }

                    NelM2DBase game = GameBinding.NelM2D;
                    if (game == null)
                    {
                        return false;
                    }

                    try
                    {
                        return COOK.autoSave(
                            game,
                            is_bench: mode == GameAutosaveMode.Bench,
                            force: mode == GameAutosaveMode.Force) != null;
                    }
                    catch (Exception ex)
                    {
                        Errors.Report(ex, "Game.Save.RequestAutosave");
                        return false;
                    }
                }
            }

            /// <summary>原版商店目录的查询入口；具体刷新操作由 <see cref="GameStore"/> 实例完成。</summary>
            public static class Stores
            {
                /// <summary>按稳定商店键取得实例；本版本没有该商店时返回 <c>null</c>。</summary>
                public static GameStore Resolve(string storeKey) => GameStore.Resolve(storeKey);
            }
        }
    }
}
