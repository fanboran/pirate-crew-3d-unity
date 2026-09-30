namespace PirateCrew.Core
{
    /// <summary>
    /// 场景名常量与场景资产路径解析（集中登记的唯一真源）。
    ///
    /// 【约定】
    ///   字符串必须与 Build Settings 中注册的场景资产名一致（不含路径与扩展名），
    ///   供 <see cref="SceneLoader.ChangeScene"/> 与 EventBus "change_scene" 载荷使用。
    ///   新增场景时在此登记，禁止在业务代码里散落魔法字符串。
    ///   场景**资产路径**一律经 <see cref="PathOf"/> 推导，禁止手拼
    ///   "Assets/Scenes/…"（目录布局只在此处知晓）。
    /// </summary>
    public static class SceneNames
    {
        /// <summary>游戏场景目录（Bootstrapper/MainMenu/Battle/CrewManagement/LevelSelect/UIShowcase）。</summary>
        public const string GameFolder = "Assets/Scenes/Game";

        /// <summary>像素化试点/验收场景目录（Pixelart* 前缀的 7 张，开发/测试专用不进发行包）。</summary>
        public const string PixelartFolder = "Assets/Scenes/Pixelart";

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

        /// <summary>组件展示场景（运行时部件陈列廊 + 旧演示页；主菜单「组件展示」钮进入）。</summary>
        public const string UIShowcase = "UIShowcase";

        /// <summary>
        /// 场景名 → 场景资产路径（<c>Battle</c> → <c>Assets/Scenes/Game/Battle.unity</c>；
        /// <c>Pixelart*</c> 前缀 → <see cref="PixelartFolder"/>）。目录布局的唯一出处，
        /// 编辑器装配/构建链与契约测试共用本方法。
        /// </summary>
        public static string PathOf(string sceneName)
        {
            var folder = sceneName != null && sceneName.StartsWith("Pixelart")
                ? PixelartFolder
                : GameFolder;
            return folder + "/" + sceneName + ".unity";
        }
    }
}
