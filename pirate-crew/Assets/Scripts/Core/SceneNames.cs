namespace PirateCrew.Core
{
    /// <summary>
    /// 场景名常量（对应 Godot 版各 tscn 路径的集中登记）。
    ///
    /// 【约定】
    ///   字符串必须与 Build Settings 中注册的场景资产名一致（不含路径与扩展名），
    ///   供 <see cref="SceneLoader.ChangeScene"/> 与 EventBus "change_scene" 载荷使用。
    ///   新增场景时在此登记，禁止在业务代码里散落魔法字符串。
    /// </summary>
    public static class SceneNames
    {
        /// <summary>入口/引导场景（Build Settings index 0）：只负责创建全局服务后跳主菜单。</summary>
        public const string Bootstrapper = "Bootstrapper";

        /// <summary>主菜单场景（index 1）。</summary>
        public const string MainMenu = "MainMenu";

        /// <summary>战斗场景（index 2，完整战斗与 HUD）。</summary>
        public const string Battle = "Battle";

        /// <summary>船员管理场景（index 3；招募 / 编成，M3）。</summary>
        public const string CrewManagement = "CrewManagement";

        /// <summary>关卡选择场景（index 4；选关 → 进 Battle，M3）。</summary>
        public const string LevelSelect = "LevelSelect";
    }
}
