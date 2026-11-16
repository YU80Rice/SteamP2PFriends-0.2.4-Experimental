using BepInEx.Configuration;
using BepInEx.Logging;
using SDG.Unturned;
using SteamP2PFriends.Shared;
using System;
using System.IO;

namespace SteamP2PFriends.Core.Config
{
    /// <summary>
    /// SteamP2PFriends 强类型配置管理器 (PluginConfig)
    /// 统一管理插件的所有 BepInEx 配置项及默认值绑定。
    /// </summary>
    public static class PluginConfig
    {
        public static ConfigEntry<bool> EnableP2PCoop { get; private set; }
        public static ConfigEntry<string> ServerName { get; private set; }
        public static ConfigEntry<byte> MaxPlayers { get; private set; }
        public static ConfigEntry<EGameMode> LastRoomMode { get; private set; }
        public static ConfigEntry<bool> LastRoomCheats { get; private set; }
        public static ConfigEntry<bool> LastRoomPvp { get; private set; }
        public static ConfigEntry<bool> LastRoomKeepInventory { get; private set; }
        public static ConfigEntry<bool> LastRoomKeepSkills { get; private set; }
        public static ConfigEntry<bool> LastRoomKeepExperience { get; private set; }
        public static ConfigEntry<string> GSLT_Login_Token { get; private set; }
        public static ConfigEntry<bool> VerboseLog { get; private set; }
        public static ConfigEntry<bool> RouteDiagnostics { get; private set; }
        public static ConfigEntry<bool> EnableMultiObserverShadow { get; private set; }

        // t145/t146：三个诊断模块（Bug1Probe / Bug1Gate / Bug2HostGunshot）的开关统一并入
        // 插件主配置（BepInEx 自动创建的 com.yu80rice.steamp2pfriends.cfg）。
        // 旧的 SteamP2PFriends.Bug1Probe.cfg / Bug1Gate.cfg / Bug2HostGunshot.cfg 自本版本起
        // 不再读取，旧值不会迁移（兼容性变更见 t145 报告）。
        public static ConfigEntry<bool> Bug1ProbeEnable { get; private set; }
        public static ConfigEntry<string> Bug1ProbeTargetPlayer { get; private set; }
        public static ConfigEntry<string> Bug1ProbeSpeedTier { get; private set; }
        public static ConfigEntry<bool> Bug1GateEnable { get; private set; }
        public static ConfigEntry<string> Bug1GateTargetPlayer { get; private set; }
        public static ConfigEntry<float> Bug1GateOffsetU { get; private set; }
        public static ConfigEntry<float> Bug1GateMaxDeviationU { get; private set; }
        public static ConfigEntry<int> Bug1GateSamples { get; private set; }
        public static ConfigEntry<float> Bug1GateSampleIntervalSeconds { get; private set; }
        public static ConfigEntry<int> Bug1GateMinSamplesPerCell { get; private set; }
        public static ConfigEntry<int> Bug1GateMaxSamples { get; private set; }
        public static ConfigEntry<bool> Bug2HostGunshotEnable { get; private set; }

        /// <summary>主 cfg 的绝对路径（Bind 时记录），用于启动自证日志与模块日志。</summary>
        internal static string BoundConfigFilePath { get; private set; } = string.Empty;

        // t153（R31-03）：启动自证行必须在任何注册/诊断 stage 之前发出；用独立的 BepInEx
        // 日志源而不是插件实例的 Logger，这样在 Bind 内部（Awake 早期、Instance 尚未使用）也能打。
        // 不走 RoleLogger：RoleLogger 会把含 Diagnostic 的消息降级为 Verbose 并改写前缀。
        private static readonly ManualLogSource DiagnosticsLog = Logger.CreateLogSource("SteamP2PFriends");

        public static void Bind(ConfigFile config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            EnableP2PCoop = config.Bind("General", "EnableP2PCoop", true, "是否启用 P2P 好友联机");
            ServerName = config.Bind("General", "ServerName", "P2P Co-op", "房主服务器名称");
            MaxPlayers = config.Bind("General", "MaxPlayers", (byte)4, "最大玩家数（1-32）");
            LastRoomMode = config.Bind("RoomDefaults", "Mode", EGameMode.EASY,
                "上一次成功启动的房间难度");
            LastRoomCheats = config.Bind("RoomDefaults", "AllowCheats", true,
                "上一次成功启动的房间是否允许其他玩家使用作弊指令");
            LastRoomPvp = config.Bind("RoomDefaults", "EnablePvp", false,
                "上一次成功启动的房间是否开启 PVP");
            LastRoomKeepInventory = config.Bind("RoomDefaults", "KeepInventory", true,
                "上一次成功启动的房间是否死亡保留物品与装备");
            LastRoomKeepSkills = config.Bind("RoomDefaults", "KeepSkills", true,
                "上一次成功启动的房间是否死亡保留技能等级");
            LastRoomKeepExperience = config.Bind("RoomDefaults", "KeepExperience", true,
                "上一次成功启动的房间是否死亡保留经验");
            GSLT_Login_Token = config.Bind("General", "GSLT_Login_Token", "",
                "[已弃用 v0.2.2] P2P 模式固定 SteamUser identity 路线，GSLT 不再参与运行。保留仅为向后兼容旧 cfg。");
            VerboseLog = config.Bind("Debug", "VerboseDiagnostics", false, "输出额外的故障诊断日志（修改后重启生效）");
            RouteDiagnostics = config.Bind("Debug", "RouteDiagnostics", false,
                "记录 Steam Networking Sockets 路由诊断；仅与 VerboseDiagnostics 同时开启时生效");
            EnableMultiObserverShadow = config.Bind("Architecture", "EnableMultiObserverShadow", true,
                "多观察者诊断账本开关；M1/M2 物品适配器由启动注册门禁强制启用，不受此诊断开关影响");

            Bug1ProbeEnable = config.Bind("Bug1Probe", "Enable", false,
                "开启 Bug1 只读定向测量仪表（默认 false）。开启后需要 TargetPlayer 指向被观察的客机。");
            Bug1ProbeTargetPlayer = config.Bind("Bug1Probe", "TargetPlayer", string.Empty,
                "被观察玩家的名称或 SteamID64；Host 视角填客机。留空且当前只有 1 名客机时自动选中该客机。");
            Bug1ProbeSpeedTier = config.Bind("Bug1Probe", "SpeedTier", "auto",
                "原生速度上限档位：auto=按开启瞬间目标姿态取 walk/sprint；walk=恒用 SPEED_STAND 档；"
                + "sprint=恒用 SPEED_SPRINT 档。spec §3.4 要求走/跑两档分别冻结，auto 只是选定本次窗口用哪一档。");

            ConfigEntry<bool> gateEnabled;
            ConfigEntry<string> gateTarget;
            ConfigEntry<float> gateOffset;
            ConfigEntry<float> gateMaxDeviation;
            ConfigEntry<int> gateSamples;
            ConfigEntry<float> gateSampleInterval;
            ConfigEntry<int> gateMinSamples;
            ConfigEntry<int> gateMaxSamples;
            BindBug1Gate(config, out gateEnabled, out gateTarget, out gateOffset, out gateSamples,
                out gateMaxDeviation, out gateSampleInterval, out gateMinSamples, out gateMaxSamples);
            Bug1GateEnable = gateEnabled;
            Bug1GateTargetPlayer = gateTarget;
            Bug1GateOffsetU = gateOffset;
            Bug1GateMaxDeviationU = gateMaxDeviation;
            Bug1GateSamples = gateSamples;
            Bug1GateSampleIntervalSeconds = gateSampleInterval;
            Bug1GateMinSamplesPerCell = gateMinSamples;
            Bug1GateMaxSamples = gateMaxSamples;

            Bug2HostGunshotEnable = config.Bind("Bug2HostGunshot", "Enable", false,
                "开启 Host 本机客机枪声补偿（默认 false）。开启后仅在 SPF listen-host 且真实 server fire 锚点补一次原生 playGunshot()。");

            BoundConfigFilePath = config.ConfigFilePath ?? string.Empty;
            if (string.IsNullOrEmpty(BoundConfigFilePath))
            {
                // t153：空路径必须是可判形态（启动行输出 configPath=<unbound>），并在绑定时告警一次。
                RoleLogger.Warn("[Shared]", "PluginConfig: main config path is empty; diagnostics startup line will report configPath=<unbound>");
            }

            // t153（R31-03）：配置刚绑定、任何注册/诊断 stage 尚未执行，无条件自证一行；
            // 前序 stage 失败也不会吞掉它（此前只在诊断 stage 内发，故障现场反而看不到）。
            // t157（R32-02）：读开关与格式化分两层——ReadDiagnosticsSwitches() 只读三个 ConfigEntry，
            // FormatDiagnosticsConfigLine(path, switches) 只做拼接；任何一层被换成常量都有独立用例打红。
            DiagnosticsLog.LogInfo(FormatDiagnosticsConfigLine(BoundConfigFilePath, ReadDiagnosticsSwitches()));
        }

        /// <summary>
        /// t145：Bug1Gate 的键定义只保留这一份。生产由 <see cref="Bind"/> 绑定主 cfg；
        /// 离线用例经 Bug1GateProbe.BindConfig 复用本方法验证默认值与 ConfigEntry 复用语义。
        /// </summary>
        internal static void BindBug1Gate(
            ConfigFile config,
            out ConfigEntry<bool> enabled,
            out ConfigEntry<string> target,
            out ConfigEntry<float> offset,
            out ConfigEntry<int> samples,
            out ConfigEntry<float> maxDeviation,
            out ConfigEntry<float> sampleInterval,
            out ConfigEntry<int> minSamples,
            out ConfigEntry<int> maxSamples)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            enabled = config.Bind("Bug1Gate", "Enable", false,
                "G 门实验开关（默认关闭）。开启后对唯一远端目标做一次性纯渲染偏移测量，只写 GATE.json，不改玩家表现。");
            target = config.Bind("Bug1Gate", "TargetPlayer", string.Empty,
                "目标选择器：玩家名（不区分大小写）或 SteamID64；留空则要求恰好一个远端客机。");
            offset = config.Bind("Bug1Gate", "OffsetU", 0.10f,
                "实验偏移量（Unity 单位，默认 0.10U）。只作用于 Model_0/Model_1 叶节点，本帧内还原。");
            samples = config.Bind("Bug1Gate", "Samples", 64,
                "每个 renderer 每轴的最大采样点数（硬上限，实际点数不会超过它）。");
            maxDeviation = config.Bind("Bug1Gate", "MaxDeviationU", 0f,
                "判定阈值 epsilon（Unity 单位）。0 = 未指定：此时只输出 max_residual_u 并判 UNKNOWN/EPSILON_UNSPECIFIED，"
                + "不会为了让数据好看而挑阈值。由决策者按 t76 §2.2 书面确认后填写。");
            sampleInterval = config.Bind("Bug1Gate", "SampleIntervalSeconds", 1.0f,
                "t95：两次六格采样之间的最小间隔（秒）。操作者正常站/走/跑即可覆盖六格，无需逐格敲命令。");
            minSamples = config.Bind("Bug1Gate", "MinSamplesPerCell", 3,
                "t95：每格（idle/walk/sprint × 两朝向）至少需要的有效样本数；不足判 UNKNOWN，不算通过。");
            maxSamples = config.Bind("Bug1Gate", "MaxSamples", 240,
                "t95：一次启用周期内最多采集多少个轴向样本（上限，避免无限采集）。");
        }

        /// <summary>
        /// t145：启动自证行。主机与客机各打一行，用户可一眼确认「主 cfg 被读到、三个开关是什么」。
        /// 只读已绑定状态，不触发任何 patch。
        /// </summary>
        internal static string FormatDiagnosticsConfigLine()
        {
            string path = BoundConfigFilePath ?? string.Empty;
            try
            {
                if (path.Length > 0) path = Path.GetFullPath(path);
            }
            catch
            {
                // 路径非法时保留原值：日志仍能指出 BepInEx 传入了什么。
            }

            return FormatDiagnosticsConfigLine(path, ReadDiagnosticsSwitches());
        }

        /// <summary>
        /// t157（R32-02）：启动行三个布尔的唯一读取点。
        /// 只读已绑定的 ConfigEntry.Value，不触发 patch、不写盘。
        /// 发射点若改成常量实参，或本函数把某一项写成常量，分别由 T145-02 / T145-07 打红。
        /// </summary>
        internal static (bool bug1Probe, bool bug1Gate, bool bug2HostGunshot) ReadDiagnosticsSwitches()
        {
            return (
                Bug1ProbeEnable != null && Bug1ProbeEnable.Value,
                Bug1GateEnable != null && Bug1GateEnable.Value,
                Bug2HostGunshotEnable != null && Bug2HostGunshotEnable.Value);
        }

        /// <summary>
        /// t153：自证行的纯格式化函数——只做字符串拼接，不读任何静态状态。
        /// 这样八组布尔真值表与接线用例可以分别证明「拼接正确」和「接线正确」，
        /// 把某一项写死成常数时真值表用例必红。
        /// </summary>
        internal static string FormatDiagnosticsConfigLine(
            string absolutePath,
            (bool bug1Probe, bool bug1Gate, bool bug2HostGunshot) switches)
        {
            return FormatDiagnosticsConfigLine(
                absolutePath, switches.bug1Probe, switches.bug1Gate, switches.bug2HostGunshot);
        }

        internal static string FormatDiagnosticsConfigLine(
            string absolutePath,
            bool bug1Probe,
            bool bug1Gate,
            bool bug2HostGunshot)
        {
            string path = string.IsNullOrEmpty(absolutePath) ? "<unbound>" : absolutePath;
            return "[Diagnostics] configPath=" + path
                + " bug1Probe=" + BoolText(bug1Probe)
                + " bug1Gate=" + BoolText(bug1Gate)
                + " bug2HostGunshot=" + BoolText(bug2HostGunshot);
        }

        private static string BoolText(bool value)
        {
            return value ? "True" : "False";
        }
    }
}
