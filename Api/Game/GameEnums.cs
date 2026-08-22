namespace Polaris.API
{
    /// <summary>原版自动存档的提示与安全检查模式。</summary>
    public enum GameAutosaveMode
    {
        /// <summary>普通自动存档；沿用原版的玩家存活与地图就绪检查。</summary>
        Normal = 0,

        /// <summary>长椅自动存档；安全检查与普通模式相同，但使用长椅提示样式。</summary>
        Bench = 1,

        /// <summary>强制自动存档；跳过原版的 <c>canSave</c> 检查。</summary>
        Force = 2,
    }

    /// <summary>角色朝向。游戏内部用布尔 <c>is_right</c> 表示，这里改用具名类型以避免调用点含义不明。</summary>
    public enum GameFacing
    {
        Left = 0,
        Right = 1,
    }

    /// <summary>天气种类，与游戏的 <c>WeatherItem.WEATHER</c> 一一对应但独立定义，避免跟随游戏枚举变动漂移。</summary>
    public enum GameWeather
    {
        Normal = 0,
        Wind = 1,
        Thunder = 2,
        Mist = 3,
        Drought = 4,
        MistDense = 5,
        Plague = 6,
    }

    /// <summary>
    /// 游戏动作，对应内部按键映射的动作槽而非虚拟键码，天然跟随玩家改键设置。
    /// <c>ButtonZ</c>–<c>ButtonD</c> 保留游戏内部字母命名，因为其含义随场景变化，不适合起具体名字。
    /// </summary>
    public enum GameInputAction
    {
        Left,
        Right,
        Up,
        Down,
        Jump,
        Run,
        Check,
        Menu,
        Submit,
        Cancel,
        TabLeft,
        TabRight,
        Add,
        Remove,
        ButtonZ,
        ButtonX,
        ButtonC,
        ButtonA,
        ButtonS,
        ButtonD,
        Shift,
    }

    /// <summary>游戏里的几种货币。与 <c>CoinStorage.CTYPE</c> 一一对应。</summary>
    public enum GameCurrency
    {
        Gold = 0,
        Crafts = 1,
        Juice = 2,
    }

    /// <summary>
    /// 玩家状态机的状态，数值与游戏的 <c>PR.STATE</c> 一致（不可重新编号）。
    /// 本枚举<b>不是</b>穷尽的，<see cref="GamePlayer.State"/> 遇到未知值会原样返回该数值。
    /// </summary>
    public enum GamePlayerState
    {
        Offline = -1,
        Normal = 0,
        MagicExplodePrepare = 1,
        MagicExploded = 2,
        Evade = 10,
        Ukemi = 11,
        EvadeShotgun = 12,
        UkemiShotgun = 13,
        EvadeJump = 14,
        Punch = 20,
        Burst = 22,
        Sliding = 23,
        Wheel = 24,
        WheelShotgun = 25,
        Comet = 26,
        CometShotgun = 27,
        DashPunch = 28,
        DashPunchShotgun = 29,
        AirPunch = 30,
        AirPunchShotgun = 31,
        ShieldBush = 40,
        ShieldLariat = 41,
        EvadeCounter = 42,
        EvadeCounterShotgun = 43,
        Smash = 44,
        SmashShotgun = 45,
        BurstScapecat = 200,
        SpecialRun = 250,
        UseBomb = 390,
        EnemySink = 430,
        ShieldBreakStun = 431,
        LayingEgg = 432,
        Orgasm = 433,
        GameOverRecovery = 434,
        WaterChokedRelease = 436,
        Sleep = 437,
        Frozen = 438,
        Onnie = 500,
        EventGacha = 501,
        Damage = 4000,
        DamageLarge = 4010,
        DamageLargeHitWall = 4011,
        DamageLargeLand = 4015,
        DamageLargeDownAbsorbAfter = 4016,
        DamageLaunch = 4020,
        DamageLaunchSpin = 4022,
        DamageLaunchLand = 4025,
        DamagePressLeftRight = 4030,
        DamagePressTopBottom = 4031,
        DamageOtherStun = 4050,
        DownStun = 4100,
        DamageBurned = 4150,
        DamageWebTrapped = 4200,
        DamageWebTrappedLand = 4201,
        Absorb = 4600,
        WormTrapped = 4980,
        WaterChoked = 4981,
        WaterChokedDown = 4982,
        Bench = 10000,
        BenchLoadAfter = 10001,
        BenchOnnie = 10002,
        BenchSitDownWait = 100003,
    }

    /// <summary>
    /// 敌人状态机的状态。数值与游戏的 <c>NelEnemy.STATE</c> 一致，理由同
    /// <see cref="GamePlayerState"/>；同样<b>不是</b>穷尽枚举。
    /// </summary>
    public enum GameEnemyState
    {
        Stand = 0,
        Special = 500,
        Stun = 900,
        Absorb = 1000,
        OverdriveActivate = 2000,
        Damage = 4000,
        DamageLaunch = 4010,
        Die = 5000,
        Summoned = 10000,
        RingOutResume = 10001,
    }

    /// <summary>
    /// 敌人种类编号，数值与游戏的 <c>nel.ENEMYID</c> 一致。高位 <c>0x80000000</c> 是狂暴形态附加位
    /// （非独立种类），<see cref="GameEnemy.EnemyId"/> 会剥掉它，狂暴状态请看 <see cref="GameEnemy.State"/>。
    /// </summary>
    public enum GameEnemyId
    {
        Slime = 256,
        SlimeTutorial = 496,
        SlimeFollower = 497,
        SlimeTutorialGarage = 498,
        Mushroom = 512,
        MushroomFollower = 753,
        Puppy = 768,
        PuppyEvent = 1008,
        Golem = 1024,
        GolemNightmare = 1152,
        Snake = 1280,
        SnakeTutorial = 1520,
        Sponge = 1536,
        Sponge1 = 1537,
        Uni = 1792,
        Mage = 4096,
        Fox = 4352,
        GolemToyMkb = 4608,
        GolemToyRm = 4609,
        GolemToyPod = 4610,
        GolemToyBow = 4611,
        GolemToyCatapult = 4816,
        GolemToyCatapult0 = 4817,
        Gecko = 4864,
        GeckoFollower = 5105,
        Frog = 5120,
        Pentapod = 5376,
        PentapodNightmare = 5504,
        PentapodHead = 5632,
        Pig = 5888,
        Roaper = 6144,
        Ramda = 6400,
        EHome = 8192,
        Honeycomb = 8448,
        Syabon = 8704,
        SyabonGimmick = 8944,
        Leech = 8960,
        LeechWcnc = 9216,
        Empress = 9472,
        LeechQueen = 9728,
        LeechQueenWcnc = 9984,
        SgWall = 10240,
        SgWallCnc = 10496,
        Mimic = 12288,
        MechGolem = 393216,
        MechGolem1 = 393217,
        WanderPuppetNpc = 458752,
        MgmFarmCowNpc = 458753,
        MgmFarmChickenNpc = 458754,
        BossNusi = 524288,
        BossNusiCage = 524289,
        BossNusiTentacle = 524290,
        BossSpider = 524544,
    }

    /// <summary>
    /// 技能分类（位标志），<b>独立编号</b>，不跟随游戏内部的 <c>SKILL_CTG</c>——那个枚举的数值随版本调整，
    /// 直接暴露会让模组在下一次游戏更新后静默错位。映射表在 <c>GameSkill</c> 内部显式维护。
    /// </summary>
    [System.Flags]
    public enum GameSkillCategory
    {
        /// <summary>不属于任何已知分类，或分类信息读不出来。</summary>
        None = 0,

        /// <summary>近身攻击类。</summary>
        Melee = 1 << 0,

        /// <summary>魔法类。</summary>
        Magic = 1 << 1,

        /// <summary>防御与回避类。</summary>
        Guard = 1 << 2,

        /// <summary>体力上限成长类。</summary>
        HpGrowth = 1 << 3,

        /// <summary>魔力上限成长类。</summary>
        MpGrowth = 1 << 4,

        /// <summary>剧情或系统授予的特殊技能。</summary>
        Special = 1 << 5,

        /// <summary>仅 Alice 可用。</summary>
        AliceOnly = 1 << 6,

        /// <summary>仅 Noel 可用。</summary>
        NoelOnly = 1 << 7,
    }

    /// <summary>
    /// 查询"这个游戏内插件现在能不能启用"，以及一次 <c>SetActive</c> 的确定结果。
    /// 每个值只表示一种原因，不把"查询状态"和"操作失败"混在同一个值里：
    /// <see cref="Active"/>/<see cref="Inactive"/> 描述当前状态，其余描述一次拒绝的具体原因。
    /// </summary>
    public enum GameEnhancerActivationStatus
    {
        /// <summary>已获得、槽位足够、原版规则允许，当前处于<b>未启用</b>状态，可以启用。</summary>
        Inactive = 0,

        /// <summary>当前已经启用。</summary>
        Active = 1,

        /// <summary>当前存档还没有获得这个插件。</summary>
        NotObtained = 2,

        /// <summary>存档或物品存储尚未就绪（还在标题画面、读档中）。</summary>
        StorageUnavailable = 3,

        /// <summary>剩余槽位不足以容纳这个插件的 <c>Cost</c>。</summary>
        NotEnoughSlots = 4,

        /// <summary>原版规则当前禁止改动插件（例如 <c>EnemySummoner.isActiveBorder()</c> 期间）。</summary>
        RejectedByState = 5,

        /// <summary>底层调用抛异常或写入未生效。</summary>
        Failed = 6,
    }

    /// <summary>物品分类（位标志，数值与 <c>NelItem.CATEG</c> 一致）；判断单项属性优先用 <see cref="GameItem"/> 上的 <c>IsFood</c>/<c>IsTool</c> 等属性。</summary>
    [System.Flags]
    public enum GameItemCategory
    {
        Other = 0,
        IndividualGrade = 1,
        CureHp = 16,
        CureMp = 32,
        CureEp = 64,
        Material = 128,
        ForFishing = 256,
        Water = 4096,
        CureMpCrack = 8192,
        StatusApply = 16384,
        StatusCure = 32768,
        Fruit = 65536,
        Food = 131072,
        Dust = 262144,
        Ancient = 524288,
        Tool = 1048576,
        Special = 2097152,
        SpecialUse = 4194304,
        Enhancer = 8388608,
        Bomb = 16777216,
        Bottle = 268435456,
    }
}
