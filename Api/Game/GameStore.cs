using System;
using System.Collections.Generic;
using nel;

namespace Polaris.API
{
    /// <summary>原版商店的稳定包装器。商店目录为进程级对象，不随地图切换失效。</summary>
    public sealed class GameStore
    {
        static readonly Dictionary<string, GameStore> Table =
            new Dictionary<string, GameStore>(StringComparer.Ordinal);

        readonly StoreManager store;

        GameStore(StoreManager store)
        {
            this.store = store;
            Key = store.name;
        }

        /// <summary>商店在原版目录中的稳定键。</summary>
        public string Key { get; }

        /// <summary>该商店是否属于会在地图间移动的流动商店。</summary>
        public bool IsWandering => Safe(static value => value.wandering, false);

        /// <summary>刷新库存；<paramref name="remake"/> 为真时完整重建。</summary>
        public bool Refresh(bool remake = false)
        {
            try
            {
                store.need_summon_flush = remake ? StoreManager.MODE.REMAKE : StoreManager.MODE.FLUSH;
                store.countItems();
                return true;
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "GameStore.Refresh");
                return false;
            }
        }

        internal static GameStore Resolve(string storeKey)
        {
            if (string.IsNullOrEmpty(storeKey))
            {
                return null;
            }

            try
            {
                StoreManager native = StoreManager.Get(storeKey, no_error: true);
                if (native == null)
                {
                    return null;
                }

                lock (Table)
                {
                    if (!Table.TryGetValue(storeKey, out GameStore wrapper))
                    {
                        wrapper = new GameStore(native);
                        Table[storeKey] = wrapper;
                    }

                    return wrapper;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        TValue Safe<TValue>(Func<StoreManager, TValue> read, TValue fallback)
        {
            try
            {
                return read(store);
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
