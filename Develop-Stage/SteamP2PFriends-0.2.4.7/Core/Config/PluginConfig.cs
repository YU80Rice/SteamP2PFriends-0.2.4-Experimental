using BepInEx.Configuration;
using SDG.Unturned;
using System;

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
        }
    }
}
