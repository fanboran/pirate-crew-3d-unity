using UnityEngine;

namespace PirateCrew.SceneArt
{
    /// <summary>
    /// 空岛尺寸/构图参数（纯 C#，无头可测）。全部字段都有默认值，<see cref="Default"/> 即"交付构图"。
    ///
    /// 【坐标口径】空岛自带一套局部坐标：**草皮穹顶顶面 ≈ y=0，岛尖 ≈ y=-RockDepth**，
    /// 水平中心在原点。摆进场景时由编辑器装配层（FloatingIslandComposer.PlacementHeight）
    /// 把整座岛抬到竞技场上方——这样"岛体尺寸"与"摆多高"是两件互不干扰的事。
    ///
    /// 【为什么在运行时程序集】穹顶高度场（<see cref="FloatingIslandDome"/>）不只服务烘焙几何：
    /// 第 2 关的出生站位逐点贴穹顶（「可见表面 = 可站立表面」），运行时出生链与
    /// 编辑器烘焙（SceneArtBake 程序集，Editor-only）必须引用**同一份**参数与公式，
    /// 放在本程序集两侧才都够得着，且不可能各自漂移。
    ///
    /// 【构图意图（AI 自有设计，本工程无既有空岛资产）】
    ///   一座**有生活痕迹的浮空岛**，而不是一块浮石：岩层分 6 层读得出地质史；3 条悬瀑（前两条带
    ///   水潭与溪流）落向云海；老树 + 树群 + 垂藤 + 发光花草 + 潭边芦苇睡莲；东侧一座半塌的遗迹
    ///   （石柱/断拱/台上悬浮的主晶 + 符文光环）；西侧崖沿一座木制瞭望台（海盗旗 + 灯 + 营地篝火）；
    ///   一条双色石板小径把两处连起来、老树下藏一口金光照人的宝箱；
    ///   岛外 9 颗悬浮晶（三颗带金环）、7 块浮石、80 点萤光尘、12 团云、6 只飞鸟成编队
    ///   ——把"它在天上、有人住过"一起讲完整。
    /// </summary>
    public sealed class FloatingIslandSpec
    {
        /// <summary>确定性种子：同种子必得同一座岛（无头测试据此断言）。</summary>
        public int Seed = 20260914;

        /// <summary>草皮顶面椭圆的 X 半径（世界单位）。</summary>
        public float RadiusX = 14.5f;

        /// <summary>草皮顶面椭圆的 Z 半径。略小于 X → 岛形是椭圆，不是圆盘。</summary>
        public float RadiusZ = 12.6f;

        /// <summary>岛尖相对草皮顶面的深度（正值；岛尖在 y = -RockDepth）。</summary>
        public float RockDepth = 19.5f;

        /// <summary>草皮穹顶中心高度（不是纯平顶：低模浮岛的顶面必须有一点隆起才读得出体积）。</summary>
        public float PlateauDome = 1.35f;

        /// <summary>岛形轮廓的环段数（不规则环的分辨率；16 段在 45° 俯视下已读不出多边形）。</summary>
        public int RimSegments = 16;

        /// <summary>阔叶树数量（不含老树）。</summary>
        public int TreeCount = 8;

        /// <summary>灌木数量。</summary>
        public int BushCount = 13;

        /// <summary>草簇数量。</summary>
        public int GrassTuftCount = 76;

        /// <summary>发光花簇数量。</summary>
        public int FlowerCount = 26;

        /// <summary>崖沿垂藤数量。</summary>
        public int VineCount = 9;

        /// <summary>悬瀑数量（前两条自带水潭 + 溪流；全部带末端雾团）。</summary>
        public int WaterfallCount = 3;

        /// <summary>悬瀑长度（从崖沿往下落多少米）。</summary>
        public float WaterfallLength = 23f;

        /// <summary>浮尘光点数量。</summary>
        public int MoteCount = 80;

        /// <summary>悬浮晶簇数量。</summary>
        public int SatelliteCrystalCount = 9;

        /// <summary>岛外浮石（带草帽/晶簇的小块）数量。</summary>
        public int FloatingRockCount = 7;

        /// <summary>云团数量（不含悬瀑末端雾团与岛底云裙）。</summary>
        public int CloudPuffCount = 12;

        /// <summary>飞鸟数量。</summary>
        public int BirdCount = 6;

        /// <summary>遗迹所在方位（弧度，0=+X 方向；默认东偏北）。</summary>
        public float RuinAngle = 0.62f;

        /// <summary>瞭望台所在方位（弧度；默认西侧，与遗迹相对）。</summary>
        public float WatchAngle = 2.92f;

        /// <summary>交付构图。</summary>
        public static FloatingIslandSpec Default => new FloatingIslandSpec();

        /// <summary>同构图、换种子（用于"一岛一景"或测试里造对照件）。</summary>
        public static FloatingIslandSpec FromSeed(int seed)
        {
            var spec = new FloatingIslandSpec { Seed = seed };
            return spec;
        }
    }

    /// <summary>
    /// 空岛**草皮穹顶高度场**（纯 C#，无头可测；唯一真源）。
    /// 烘焙侧的一切"站在顶面上"的几何（顶面网格 / 树 / 石 / 台 / 径 / 碰撞薄壳 / 出生点表）
    /// 与运行时的出生贴地都调这里，因此不存在"树浮在草皮上 10cm""角色埋进草皮半米"
    /// 这种只能靠出图发现的错位。
    /// </summary>
    public static class FloatingIslandDome
    {
        /// <summary>
        /// 草皮穹顶高度场：主穹顶（越靠中心越高）+ 2 处缓丘 + 1 处浅洼。
        /// 【唯一真源】顶面网格与所有"站在顶面上"的零件（树/石/台/径）都调它，
        /// 因此不存在"树浮在草皮上 10cm"这种只能靠出图发现的错位。
        /// </summary>
        public static float PlateauY(FloatingIslandSpec spec, float x, float z)
        {
            if (spec == null)
                spec = FloatingIslandSpec.Default;

            float nx = x / Mathf.Max(0.001f, spec.RadiusX);
            float nz = z / Mathf.Max(0.001f, spec.RadiusZ);
            float r = Mathf.Sqrt(nx * nx + nz * nz);

            float y = spec.PlateauDome * Mathf.Pow(Mathf.Clamp01(1f - r), 0.85f);

            // 缓丘 / 浅洼：给顶面起伏，避免"一口平底锅"。
            y += Mound(x, z, 0.34f * spec.RadiusX, -0.12f * spec.RadiusZ, 0.30f * spec.RadiusX,
                0.46f * spec.PlateauDome);
            y += Mound(x, z, -0.40f * spec.RadiusX, 0.28f * spec.RadiusZ, 0.27f * spec.RadiusX,
                0.34f * spec.PlateauDome);
            y -= Mound(x, z, 0.06f * spec.RadiusX, 0.50f * spec.RadiusZ, 0.23f * spec.RadiusX,
                0.26f * spec.PlateauDome);
            return y;
        }

        /// <summary>圆润丘：中心 (cx,cz)、半径 <paramref name="radius"/>、峰高 <paramref name="height"/>。</summary>
        static float Mound(float x, float z, float cx, float cz, float radius, float height)
        {
            if (radius <= 0f)
                return 0f;

            float dx = x - cx, dz = z - cz;
            float d = Mathf.Sqrt(dx * dx + dz * dz) / radius;
            if (d >= 1f)
                return 0f;

            float t = 1f - d;
            return height * t * t;
        }

        /// <summary>顶面上某个方位/半径比例处的落点（y 已贴合草皮面）。公开给出生点表/测试复用。</summary>
        public static Vector3 OnPlateau(FloatingIslandSpec spec, float angle, float scaleXZ)
        {
            float x = Mathf.Cos(angle) * spec.RadiusX * scaleXZ;
            float z = Mathf.Sin(angle) * spec.RadiusZ * scaleXZ;
            return new Vector3(x, PlateauY(spec, x, z), z);
        }

        /// <summary>
        /// 空岛上某**世界坐标**点的出生站面 Y（脚底草皮面）：根节点世界位 + 局部穹顶高度。
        /// 【世界↔局部约定】装配契约（PlaceIntoBattleCenter）只改根节点 position、不旋转不缩放，
        /// 因此世界↔局部就是平移，与装配器日志的 TransformPoint 同值；根若被旋转/缩放，
        /// 本公式与可见几何即分叉——装配侧不允许。
        /// 【谁在调】BattleController 出生链（空岛关逐点贴穹顶）；
        /// 站位朝向与 UnitPivotHeight 由调用方叠加，本方法只给站面。
        /// </summary>
        public static float SpawnGroundY(FloatingIslandSpec spec, Vector3 islandRootWorld,
            float worldX, float worldZ)
        {
            return islandRootWorld.y
                + PlateauY(spec, worldX - islandRootWorld.x, worldZ - islandRootWorld.z);
        }
    }
}
