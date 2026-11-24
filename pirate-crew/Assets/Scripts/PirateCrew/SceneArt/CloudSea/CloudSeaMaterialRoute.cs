using PirateCrew.Rendering.Pixelart;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>云端漫步场景件的着色路线：决定一个对象上的材质烘焙成哪支 shader。</summary>
    public enum CloudSeaRoute
    {
        /// <summary>水上件：物体 shader（运行时换装成 PostLineToonLit），或云台水洼 / 玻璃的透明 shader。</summary>
        Default,

        /// <summary>海床：程序沙地 + 焦散，写深度法线。</summary>
        Seabed,

        /// <summary>海面：清澈透明水，不写深度。</summary>
        ClearWater,

        /// <summary>水下生态：卡通色带、投影、不描边。</summary>
        Underwater,
    }

    /// <summary>
    /// 云端漫步场景件「对象 → 着色路线」与「路线 → 烘焙材质资产名」的**纯逻辑**（无头可测）。
    /// <see cref="CloudSeaAssetBaker"/> 只做遍历 / 建材质 / 落盘的胶水，判据全在本类。
    ///
    /// 【为什么按对象分流，而不是按材质名】同一个颜色水上水下都有：珊瑚色既是海底的珊瑚
    /// 也是海鸥的喙，骨色既是鱼腹也是鸟巢里的蛋。材质资产按源名共享，只有对象才知道
    /// 「这是在水里还是水面上」。
    ///
    /// 【字面量只在 <see cref="CloudSeaPath"/>】对象名 / shader 名不在本类重复。
    /// </summary>
    public static class CloudSeaMaterialRoute
    {
        /// <summary>
        /// 对象的着色路线。精确名优先于前缀：海床与水面都带 <see cref="CloudSeaPath.SeaBedPrefix"/>，
        /// 但它们各走各的 shader，其余同前缀对象（海草 / 岩石 / 珊瑚 / 沉积物）才是水下生态。
        /// </summary>
        public static CloudSeaRoute Of(string objectName)
        {
            if (objectName == CloudSeaPath.SeaSurfaceObjectName)
                return CloudSeaRoute.ClearWater;
            if (objectName == CloudSeaPath.SeaBedObjectName)
                return CloudSeaRoute.Seabed;

            if (StartsWith(objectName, CloudSeaPath.SeaBedPrefix)
                || StartsWith(objectName, CloudSeaPath.FishSchoolPrefix)
                || StartsWith(objectName, CloudSeaPath.CrabPrefix)
                || StartsWith(objectName, CloudSeaPath.BubblePrefix))
            {
                return CloudSeaRoute.Underwater;
            }

            return CloudSeaRoute.Default;
        }

        /// <summary>
        /// 烘焙材质的资产名。源材质只出现在一个路线上 → 直接占原名；被多个路线共用
        /// （<paramref name="routeCount"/> ≥ 2）→ 默认路线保留原名，其余路线加路线后缀：
        /// 同一个颜色在两支 shader 下是两套参数，不能共用一份资产。
        /// 例：珊瑚色 = Cloud_Coral（水上）+ Cloud_Coral_Underwater（水下）。
        /// </summary>
        /// <param name="routeCount">该源材质名在整件 prefab 里出现在几个不同路线上。</param>
        public static string AssetName(string sourceMaterialName, CloudSeaRoute route, int routeCount)
        {
            if (route == CloudSeaRoute.Default || routeCount <= 1)
                return sourceMaterialName;
            return sourceMaterialName + "_" + route;
        }

        static bool StartsWith(string value, string prefix)
        {
            return value != null && value.StartsWith(prefix, System.StringComparison.Ordinal);
        }
    }
}
