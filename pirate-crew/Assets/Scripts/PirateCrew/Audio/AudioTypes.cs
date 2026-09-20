namespace PirateCrew.Audio
{
    /// <summary>
    /// 音频总线分类（音量分组）。Master 是总闸，其余三类各自独立。
    ///
    /// 【为什么是这四个】直接对应「设置 → 音频」里玩家能理解的四个滑条，
    /// 也是音频资产的三条落地目录（Sfx / Ambient / Music，见 AudioAssetBuilder）：
    /// 分类与目录、与播放上限、与导入压缩策略一一对应，不引入第二套口径。
    /// </summary>
    public enum AudioCategory
    {
        /// <summary>总音量（乘在其余三类之上）。</summary>
        Master = 0,

        /// <summary>音效（战斗 + 反馈 + UI）。</summary>
        Sfx = 1,

        /// <summary>环境音（海浪/风声/海鸥循环与点缀）。</summary>
        Ambient = 2,

        /// <summary>音乐（胜利/失败乐句）。</summary>
        Music = 3,
    }

    /// <summary>空间化模式（映射到 <c>AudioSource.spatialBlend</c>）。</summary>
    public enum SpatialMode
    {
        /// <summary>2D（UI、回合提示、结果乐句）：不随相机位置衰减。</summary>
        TwoD = 0,

        /// <summary>3D（爆炸、命中、弹跳、地雷蜂鸣）：按世界坐标衰减与声道定位。</summary>
        ThreeD = 1,
    }

    /// <summary>分类相关的常量与纯换算。</summary>
    public static class AudioCategories
    {
        /// <summary>分类总数（含 Master）。</summary>
        public const int Count = 4;

        /// <summary>分类 → 数组索引（与枚举值一致，集中一处便于将来扩展）。</summary>
        public static int Index(AudioCategory category)
        {
            return (int)category;
        }

        /// <summary>分类是否受 Master 总闸缩放（Master 自身不重复乘自己）。</summary>
        public static bool IsScaledByMaster(AudioCategory category)
        {
            return category != AudioCategory.Master;
        }

        /// <summary>分类的中文显示名（设置面板用）。</summary>
        public static string DisplayName(AudioCategory category)
        {
            switch (category)
            {
                case AudioCategory.Master:
                    return "总音量";
                case AudioCategory.Ambient:
                    return "环境音";
                case AudioCategory.Music:
                    return "音乐";
                default:
                    return "音效";
            }
        }
    }
}
