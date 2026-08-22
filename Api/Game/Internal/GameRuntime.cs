using System;
using m2d;
using nel;
using UnityEngine;
using XX;

namespace Polaris.API
{
    /// <summary>
    /// v2 游戏层的每帧泵：推进实例生命周期，并对可轮询状态做差分以发布事件（这些量写入路径多，逐条打补丁不划算）；
    /// 差分只入队，实际派发由 <see cref="Infra.CallbackRuntime.Drain"/> 负责。
    /// </summary>
    internal static class GameRuntime
    {
        static Map2d lastMap;
        static string lastMapKey;

        static bool known;
        static bool lastNight;
        static float lastNightLevel;
        static float lastDangerLevel;
        static int lastWeatherBits;

        static string lastLocale;
        static bool localeKnown;

        static readonly uint[] lastMoney = new uint[3];
        static bool moneyKnown;

        static readonly int[] lastVolume = new int[4];
        static bool volumeKnown;

        static string lastBgmTiming;
        static string lastBgmCue;
        static bool lastBgmPlaying;
        static bool bgmKnown;

        static string lastFocusedQuest;
        static bool focusedQuestKnown;

        /// <summary>由 <see cref="Plugin.Update"/> 每帧调用，在回调派发之前。</summary>
        internal static void Pump()
        {
            // 地图代数必须先于其他回调推进，确保订阅者取实例时失效状态已生效。
            PumpMapLifetime();

            PumpWorldState();
            PumpLocale();
            PumpEconomy();
            PumpAudioState();
            PumpQuests();

            InputBinding.Pump();
            GameAudioRuntime.Pump();

            // 插件/技能的外部修改（原版菜单直接改 top_grade 或 manip_bits、事件指令
            // GET/REM/ENABLE/DISABLESKILL）没有稳定的补丁点，统一靠每帧差分捕获。
            // 只观察已经建过包装器的定义，不为此重建整个目录快照。
            GameEnhancerRuntime.Pump();
            GameSkillRuntime.Pump();

            PumpInstances();
        }

        /// <summary>低频清理：把已经失效的包装器从各张表里丢掉。</summary>
        internal static void Sweep()
        {
            GameCharacter.SweepTable();
            GamePlayer.SweepPlayers();
            GameEnemy.SweepEnemies();
            GameMap.SweepMaps();
            GameMenu.SweepMenus();
            GameStorage.SweepStorages();
            GameEvent.SweepEvents();
            GameEnhancer.SweepEnhancers();
            GameSkill.SweepSkills();
        }

        /// <summary>世界卸载/回到标题时的整体作废。</summary>
        internal static void ResetWorld()
        {
            GameCharacter.InvalidateAll();
            GamePlayer.InvalidateAllPlayers();
            GameEnemy.InvalidateAllEnemies();
            GameStorage.InvalidateAllStorages();
            GameQuest.InvalidateAllQuests();

            // 插件/技能的目录对象在新游戏与读档时并不重建，所以这里刻意不作废包装器（避免丢掉调用方持有的实例身份），
            // 只清空观察基线：下一帧把存档值当成新基线记录，不发回调，避免读档产生初值风暴。真正换了目录对象时，InstanceTable 会自然给出新身份。
            GameEnhancerRuntime.Reset();
            GameSkillRuntime.Reset();
            GameEventRuntime.Reset();
            GameAudioRuntime.ReleaseAll();

            known = false;
            moneyKnown = false;
            bgmKnown = false;
            focusedQuestKnown = false;
        }

        /// <summary>某一代包装器是不是还属于当前这一代地图。角色包装器的失效判据。</summary>
        internal static bool IsCurrentGeneration(GameCharacter character)
            => character != null && character.MapGeneration == GameBinding.MapGeneration;

        // ── 地图生命周期 ───────────────────────────────────────────────────────

        static void PumpMapLifetime()
        {
            Map2d map = GameBinding.CurrentMap;
            if (ReferenceEquals(map, lastMap))
            {
                return;
            }

            Map2d previous = lastMap;
            string previousKey = lastMapKey;
            lastMap = map;
            lastMapKey = SafeMapKey(map);

            // 整体作废旧地图的角色包装器：mover 对象池复用,仅比较引用会把复用对象误认成同一目标。
            GameCharacter.InvalidateAll();
            GamePlayer.InvalidateAllPlayers();
            GameEnemy.InvalidateAllEnemies();

            if (previous != null)
            {
                GameMap closed = GameMap.Peek(previous);
                if (closed != null)
                {
                    GameCallbackHub.PublishInstance(
                        GameInstanceCallbackKind.MapActionClosed, closed, () => new MapActionClosedCallbackData(closed));
                    GameCallbackHub.PublishInstance(
                        GameInstanceCallbackKind.MapClosed, closed, () => new MapClosedCallbackData(closed));
                }
            }

            GameMap opened = GameMap.Wrap(map);

            GameCallbackHub.PublishStatic(
                GameStaticCallbackKind.MapChanged, () => new MapChangedCallbackData(previousKey, opened));

            if (opened != null)
            {
                GameCallbackHub.PublishStatic(
                    GameStaticCallbackKind.MapOpened, () => new MapOpenedCallbackData(opened));

                // 先让 MapOpened 订阅者拿到实例并注册实例回调，再发布动作初始化。
                Infra.CallbackRuntime.Enqueue(() => GameCallbackHub.PublishInstance(
                    GameInstanceCallbackKind.MapActionInitialized,
                    opened,
                    () => new MapActionInitializedCallbackData(opened)));
            }
        }

        // ── 世界状态差分 ───────────────────────────────────────────────────────

        static void PumpWorldState()
        {
            NightController night = GameBinding.Night;
            if (night == null)
            {
                known = false;
                return;
            }

            bool isNight;
            float nightLevel;
            float dangerLevel;
            int weatherBits;

            try
            {
                isNight = night.isNight();
                nightLevel = night.night_level;
                dangerLevel = night.getDangerLevel();
                weatherBits = night.current_weather_bit;
            }
            catch (Exception)
            {
                return;
            }

            if (!known)
            {
                // 首次探测只记录不发事件,避免把启动初值当成一次变化广播出去。
                known = true;
                lastNight = isNight;
                lastNightLevel = nightLevel;
                lastDangerLevel = dangerLevel;
                lastWeatherBits = weatherBits;
                return;
            }

            if (isNight != lastNight)
            {
                lastNight = isNight;
                GameCallbackHub.PublishStatic(
                    GameStaticCallbackKind.DayNightChanged, () => new DayNightChangedCallbackData(isNight));
            }

            if (!Mathf.Approximately(nightLevel, lastNightLevel))
            {
                float previous = lastNightLevel;
                lastNightLevel = nightLevel;
                GameCallbackHub.PublishStatic(
                    GameStaticCallbackKind.NightLevelChanged, () => new NightLevelChangedCallbackData(previous, nightLevel));
            }

            if (!Mathf.Approximately(dangerLevel, lastDangerLevel))
            {
                float previous = lastDangerLevel;
                lastDangerLevel = dangerLevel;
                GameCallbackHub.PublishStatic(
                    GameStaticCallbackKind.DangerLevelChanged, () => new DangerLevelChangedCallbackData(previous, dangerLevel));
            }

            if (weatherBits != lastWeatherBits)
            {
                int previous = lastWeatherBits;
                lastWeatherBits = weatherBits;
                GameCallbackHub.PublishStatic(
                    GameStaticCallbackKind.WeatherChanged, () => new WeatherChangedCallbackData(previous, weatherBits));
            }
        }

        static void PumpLocale()
        {
            string locale;
            try
            {
                locale = TX.getCurrentFamilyName();
            }
            catch (Exception)
            {
                return;
            }

            if (string.IsNullOrEmpty(locale))
            {
                return;
            }

            if (!localeKnown)
            {
                localeKnown = true;
                lastLocale = locale;
                return;
            }

            if (locale == lastLocale)
            {
                return;
            }

            string previous = lastLocale;
            lastLocale = locale;
            Plugin.Logger.LogMessage($"[Polaris] Game language changed: {previous} -> {locale}.");

            GameCallbackHub.PublishStatic(
                GameStaticCallbackKind.LocaleChanged, () => new LocaleChangedCallbackData(previous, locale));

            GameSessionRuntime.NotifyLocaleChanged(previous, locale);
        }

        static void PumpEconomy()
        {
            for (int i = 0; i < lastMoney.Length; i++)
            {
                var currency = (GameCurrency)i;
                uint amount = PolarisAPI.Game.Economy.GetAmount(currency);

                if (!moneyKnown)
                {
                    lastMoney[i] = amount;
                    continue;
                }

                if (amount == lastMoney[i])
                {
                    continue;
                }

                uint previous = lastMoney[i];
                lastMoney[i] = amount;
                GameCallbackHub.PublishStatic(
                    GameStaticCallbackKind.MoneyChanged, () => new MoneyChangedCallbackData(currency, previous, amount));
            }

            moneyKnown = true;
        }

        static void PumpAudioState()
        {
            int[] volumes;
            try
            {
                volumes = new[] { SND.master_volume, SND.volume, SND.voice_volume, SND.bgm_volume };
            }
            catch (Exception)
            {
                return;
            }

            for (int i = 0; i < volumes.Length; i++)
            {
                if (!volumeKnown)
                {
                    lastVolume[i] = volumes[i];
                    continue;
                }

                if (volumes[i] == lastVolume[i])
                {
                    continue;
                }

                var channel = (GameVolumeChannel)i;
                int previous = lastVolume[i];
                int current = volumes[i];
                lastVolume[i] = current;
                GameCallbackHub.PublishStatic(
                    GameStaticCallbackKind.VolumeChanged, () => new VolumeChangedCallbackData(channel, previous, current));
            }

            volumeKnown = true;

            string timing;
            string cue;
            bool playing;
            try
            {
                BGM.getFrontBgm(out timing, out cue);
                playing = BGM.isFrontPlaying();
            }
            catch (Exception)
            {
                return;
            }

            if (!bgmKnown)
            {
                bgmKnown = true;
                lastBgmTiming = timing;
                lastBgmCue = cue;
                lastBgmPlaying = playing;
                return;
            }

            if (cue != lastBgmCue || timing != lastBgmTiming)
            {
                GameBgmTrack previous = string.IsNullOrEmpty(lastBgmCue) ? null : new GameBgmTrack(lastBgmTiming, lastBgmCue);
                GameBgmTrack current = string.IsNullOrEmpty(cue) ? null : new GameBgmTrack(timing, cue);
                lastBgmTiming = timing;
                lastBgmCue = cue;
                GameCallbackHub.PublishStatic(
                    GameStaticCallbackKind.MusicChanged, () => new MusicChangedCallbackData(previous, current));
            }

            if (playing != lastBgmPlaying)
            {
                lastBgmPlaying = playing;
                GameCallbackHub.PublishStatic(
                    GameStaticCallbackKind.MusicPlaybackChanged, () => new MusicPlaybackChangedCallbackData(playing));
            }
        }

        static void PumpQuests()
        {
            QuestTracker tracker = GameBinding.Quests;
            if (tracker == null)
            {
                focusedQuestKnown = false;
                return;
            }

            string key;
            try
            {
                key = tracker.getHeadQuest()?.Q?.key;
            }
            catch (Exception)
            {
                return;
            }

            if (!focusedQuestKnown)
            {
                focusedQuestKnown = true;
                lastFocusedQuest = key;
                return;
            }

            if (key == lastFocusedQuest)
            {
                return;
            }

            lastFocusedQuest = key;
            GameQuest quest = GameQuest.Wrap(key);
            GameCallbackHub.PublishStatic(
                GameStaticCallbackKind.FocusedQuestChanged, () => new FocusedQuestChangedCallbackData(quest));
        }

        static void PumpInstances()
        {
            GamePlayer player = GamePlayer.Wrap(GameBinding.Player);
            player?.PumpState();

            GameEnemy.EachLive(static enemy => enemy.PumpState());
        }

        static string SafeMapKey(Map2d map)
        {
            try
            {
                return map?.key;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
