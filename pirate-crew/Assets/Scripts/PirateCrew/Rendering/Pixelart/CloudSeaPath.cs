namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// 云端漫步场景重做（2026-11-11）的共享契约：shader 名、对象名、材质名前缀。
    ///
    /// 【为什么集中】FBX / 烘焙器 / 运行时组件三方靠字符串对齐（对象名找鱼群/停栖鸥、
    /// 材质名换装、shader 名换透明趟）。字符串契约拼错是静默失效，只在这一处写字面量。
    /// </summary>
    public static class CloudSeaPath
    {
        /// <summary>云台水洼 / 水壁 / 玻璃的透明 shader（海面不归它管，走 <see cref="ClearWaterShaderName"/>）。</summary>
        public const string WaterShaderName = "PirateCrew/CloudSea/Water";

        /// <summary>清澈海面透明 shader（只挂在 <see cref="SeaSurfaceObjectName"/> 上）。</summary>
        public const string ClearWaterShaderName = "PirateCrew/CloudSea/CloudSeaClearWater";

        /// <summary>海床 shader（程序沙地 + 焦散；只挂在 <see cref="SeaBedObjectName"/> 上）。</summary>
        public const string SeabedShaderName = "PirateCrew/CloudSea/CloudSeaSeabed";

        /// <summary>水下生态 shader（海草 / 珊瑚 / 岩石 / 鱼 / 蟹：卡通色带、投影、不描边）。</summary>
        public const string UnderwaterShaderName = "PirateCrew/CloudSea/CloudSeaUnderwater";

        /// <summary>涟漪同心环透明 shader。</summary>
        public const string RippleShaderName = "PirateCrew/CloudSea/CloudSeaRipple";

        /// <summary>太阳雨/光柱/彩虹的双趟透明 shader。</summary>
        public const string RainShaderName = "PirateCrew/CloudSea/CloudSeaRain";

        /// <summary>水面对象名（FBX 内）。</summary>
        public const string SeaSurfaceObjectName = "Cloud_Sea_Surface";

        /// <summary>海床对象名（FBX 内；整片起伏沙丘是一个网格）。</summary>
        public const string SeaBedObjectName = "Cloud_Sea_Bed";

        /// <summary>海底静物对象名前缀（海草 / 岩石 / 珊瑚 / 沉积物，与海床、水面共用前缀）。</summary>
        public const string SeaBedPrefix = "Cloud_Sea_";

        /// <summary>鱼群对象名前缀（每群一个独立对象）。</summary>
        public const string FishSchoolPrefix = "Cloud_FishSchool_";

        /// <summary>海底螃蟹对象名前缀（每只一个独立对象；CloudSeaLife 做横向爬行）。</summary>
        public const string CrabPrefix = "Cloud_Crab_";

        /// <summary>海底气泡柱对象名前缀。</summary>
        public const string BubblePrefix = "Cloud_Bubbles_";

        /// <summary>停栖海鸥对象名前缀。</summary>
        public const string GullPerchPrefix = "Cloud_GullPerch_";

        /// <summary>云台合并对象名。</summary>
        public const string PlatformsObjectName = "Cloud_Platforms";

        /// <summary>云瀑对象名。</summary>
        public const string WaterfallObjectName = "Cloud_Waterfall";

        /// <summary>水面材质名（烘焙器按此名换成双趟透明材质）。</summary>
        public const string WaterMaterialName = "Cloud_Water";

        /// <summary>深水/水壁材质名。</summary>
        public const string WaterDeepMaterialName = "Cloud_WaterDeep";

        /// <summary>玻璃材质名。</summary>
        public const string GlassMaterialName = "Cloud_Glass";

        /// <summary>云体材质名（只这些开 Palette/Patch；海/木/金属/鱼不算云）。</summary>
        public static bool IsCloudBodyMaterial(string materialName)
        {
            switch (materialName)
            {
                case "Cloud_White":
                case "Cloud_Warm":
                case "Cloud_Shade":
                case "Cloud_Deep":
                case "Cloud_Blue":
                case "Cloud_Mist":
                case "Cloud_Sea":
                case "Cloud_SeaDeep":
                    return true;
                default:
                    return false;
            }
        }
    }
}
