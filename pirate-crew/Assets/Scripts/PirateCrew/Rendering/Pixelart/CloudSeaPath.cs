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
        /// <summary>海水/水洼双趟透明 shader：CellShade 走 PixelartTransparent 趟，PostLine 走标准前向透明趟。</summary>
        public const string WaterShaderName = "PirateCrew/CloudSea/Water";

        /// <summary>太阳雨/光柱/彩虹/涟漪的双趟透明 shader（与水体同一套双链口径）。</summary>
        public const string RainShaderName = "PirateCrew/CloudSea/CloudSeaRain";

        /// <summary>水面对象名（FBX 内）。</summary>
        public const string SeaSurfaceObjectName = "Cloud_Sea_Surface";

        /// <summary>海床对象名前缀。</summary>
        public const string SeaBedPrefix = "Cloud_Sea_";

        /// <summary>鱼群对象名前缀（每群一个独立对象）。</summary>
        public const string FishSchoolPrefix = "Cloud_FishSchool_";

        /// <summary>海底螃蟹对象名前缀（每只一个独立对象；CloudSeaLife 做横向爬行）。</summary>
        public const string CrabPrefix = "Cloud_Crab_";

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
