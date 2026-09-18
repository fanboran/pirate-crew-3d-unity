namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>场景装饰的种类（决定用哪个几何生成器与哪个材质组）。
    /// 原在 <c>ScenePropLayout.cs</c>（一代道具布局器已退场）；烘焙几何层仍消费本枚举。</summary>
    public enum ScenePropKind
    {
        /// <summary>搁浅断船（主角，场景文档 §4.1）。</summary>
        Wreck,

        /// <summary>木栈桥（§4.2，含桩）。</summary>
        Jetty,

        /// <summary>旗杆 + 旗帜（§4.4）。</summary>
        FlagPole,

        /// <summary>告示牌（§4.4）。</summary>
        SignPost,

        /// <summary>木箱（§4.3）。</summary>
        Crate,

        /// <summary>火药桶（§4.3）。</summary>
        Barrel,

        /// <summary>锚 + 链（§4.5）。</summary>
        Anchor,

        /// <summary>散落杂物（§4.6）。</summary>
        Debris,

        /// <summary>棕榈树（§3.4）。</summary>
        Palm,

        /// <summary>灌木簇（§3.4）。</summary>
        Bush,

        /// <summary>草丛（§3.4）。</summary>
        GrassTuft,

        /// <summary>台地棱线小石块（§3.2）。</summary>
        RidgeRock,

        /// <summary>潮间带石块（§3.2）。</summary>
        IntertidalRock,

        /// <summary>功能掩体中石（§3.2）。</summary>
        CoverRock,

        /// <summary>贝壳碎屑（§3.3）。</summary>
        Shell,

        /// <summary>水下海床坡（加分项 P2）。</summary>
        SeabedMound,

        /// <summary>低模积云（加分项 P1）。</summary>
        CloudPuff,

        /// <summary>远景剪影岛（M8）。</summary>
        FarIsland,

        /// <summary>远景帆船剪影（加分项 P5）。</summary>
        FarShip,
    }
}
