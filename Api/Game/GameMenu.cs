using System;
using nel.gm;

namespace Polaris.API
{
    /// <summary>
    /// 游戏内 ESC 菜单的一次打开。入口是 <c>PolarisAPI.Game.Menu</c> 与 <see cref="GameStaticCallbackKind.GameMenuOpened"/> 回调。
    /// 注意区分 <c>PolarisAPI.GameMenu</c>（菜单分类扩展注册表）：名字接近但不是一回事。
    ///
    /// 同一个包装器覆盖"请求已接受但菜单尚未激活"（<see cref="requestPending"/>）到"已激活"的整个生命周期：
    /// 底层 <c>UiGameMenu</c> 对象在一局游戏内是复用的单例，激活前 <c>state</c> 仍是 <c>OFFLINE</c>，
    /// 不能靠它判断"这个包装器还有没有意义"，所以待处理阶段单独用一个标志位追踪。
    /// </summary>
    public sealed class GameMenu : GameInstance
    {
        static readonly InstanceTable<UiGameMenu, GameMenu> Table = new();

        readonly UiGameMenu menu;

        /// <summary>请求已被 <c>menu_open = OPEN</c> 接受，但原版还没有真正调用 <c>activate()</c>。</summary>
        bool requestPending;

        GameMenu(UiGameMenu menu)
        {
            this.menu = menu;
        }

        internal static GameMenu Wrap(UiGameMenu native) => Table.Get(native, static n => new GameMenu(n));

        /// <summary>为一次刚被接受的打开请求取得（或复用）包装器，标记为"待激活"。</summary>
        internal static GameMenu WrapForPendingRequest(UiGameMenu native)
        {
            GameMenu menu = Table.Get(native, static n => new GameMenu(n));
            if (menu != null)
            {
                menu.requestPending = true;
            }

            return menu;
        }

        internal static GameMenu Peek(UiGameMenu native) => Table.Peek(native);

        internal static void Invalidate(UiGameMenu native) => Table.Invalidate(native);

        internal static void SweepMenus() => Table.Sweep();

        UiGameMenu Native => IsValid ? menu : null;

        /// <summary>由 <c>UiGameMenu.activate()</c> 的 Harmony postfix 调用：请求真正兑现，转入"已激活"阶段。</summary>
        internal void ConfirmActivated() => requestPending = false;

        private protected override bool IsNativeAlive
        {
            get
            {
                if (menu == null)
                {
                    return false;
                }

                if (requestPending)
                {
                    return true;
                }

                try
                {
                    // 用状态而不是 Unity 销毁判定：菜单对象会被复用，销毁判定要等很久才为真。
                    return menu.state != UiGameMenu.STATE.OFFLINE;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private protected override string Describe() => "GameMenu";

        /// <summary>判断该菜单当前是否可以处理输入。</summary>
        public bool CanHandleInput => Read(static m => m.general_button_handleable, false);

        /// <summary>获取或设置该菜单是否应退出当前分类。</summary>
        public bool ShouldQuitCategory
        {
            get => Read(static m => m.category_to_quit, false);
            set
            {
                EnsureUsable();
                Act("ShouldQuitCategory", m => m.category_to_quit = value);
            }
        }

        /// <summary>
        /// 获取或设置该菜单的输入处理开关。实际是游戏里的全局静态开关，关掉后记得开回来，
        /// 否则下一次打开的菜单也不响应输入。
        /// </summary>
        public bool IsInputHandlingEnabled
        {
            get
            {
                try
                {
                    return UiGameMenu.handle;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            set
            {
                EnsureUsable();

                try
                {
                    UiGameMenu.handle = value;
                }
                catch (Exception ex)
                {
                    PolarisAPI.Errors.Report(ex, "GameMenu.IsInputHandlingEnabled");
                }
            }
        }

        /// <summary>
        /// 关闭该菜单实例。<paramref name="immediate"/> 为真时跳过关闭动画。
        /// 对仍处于"待激活"阶段的包装器，这会取消尚未兑现的打开请求而不是关闭一个不存在的菜单；
        /// 对已激活的包装器，这会走原版 <c>deactivate()</c> 流程。两种情况下包装器随后都失效。
        /// </summary>
        public void Close(bool immediate = false)
        {
            EnsureUsable();

            if (requestPending)
            {
                CancelPendingRequest();
                return;
            }

            Act("Close", m => m.deactivate(immediate));
        }

        /// <summary>
        /// 取消一次尚未激活的打开请求：只有在请求仍然待处理（原版还没抢先接管/激活）时才撤回
        /// <c>menu_open</c>，随后无条件让包装器失效——不管撤回是否发生，这次"请求→关闭"的生命周期都结束了。
        /// </summary>
        void CancelPendingRequest()
        {
            requestPending = false;

            nel.NelM2DBase m2d = GameBinding.NelM2D;
            if (m2d != null && m2d.menu_open_ == nel.NelM2DBase.MENU_OPEN.OPEN && !IsReallyActive(menu))
            {
                m2d.menu_open = nel.NelM2DBase.MENU_OPEN.NONE;
            }

            Invalidate(menu);
        }

        static bool IsReallyActive(UiGameMenu native)
        {
            try
            {
                return native != null && native.isActive();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>判断该菜单是否正在关闭。</summary>
        public bool IsClosing() => Read(static m => m.isClosingGame(), false);

        /// <summary>判断该菜单是否正在暂停世界运行。</summary>
        public bool IsStoppingWorld() => Read(static m => GameMenuPauseRuntime.ShouldStopWorld(m), false);

        /// <summary>判断该菜单是否处于长椅菜单状态。</summary>
        public bool IsBenchMenuActive() => Read(static m => m.isBenchMenuActive(false), false);

        /// <summary>判断该菜单是否正在编辑指定分类；<paramref name="categoryKey"/> 为游戏分类名，不分大小写，不认识时返回 <c>false</c>。</summary>
        public bool IsEditingCategory(string categoryKey)
        {
            UiGameMenu m = Native;
            if (m == null || string.IsNullOrEmpty(categoryKey))
            {
                return false;
            }

            if (!Enum.TryParse(categoryKey, true, out CATEG category))
            {
                return false;
            }

            try
            {
                return m.isEditState() && m.edit_categ == category;
            }
            catch (Exception)
            {
                return false;
            }
        }

        TValue Read<TValue>(Func<UiGameMenu, TValue> read, TValue fallback)
        {
            UiGameMenu m = Native;
            if (m == null)
            {
                return fallback;
            }

            try
            {
                return read(m);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        void Act(string what, Action<UiGameMenu> action)
        {
            UiGameMenu m = Native;
            if (m == null)
            {
                return;
            }

            try
            {
                action(m);
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, $"GameMenu.{what}");
            }
        }
    }
}
