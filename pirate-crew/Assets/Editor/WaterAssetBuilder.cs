using System.IO;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using PirateCrew.PirateCrew.Water;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 水面模拟的构建期资产烘焙入口：把"地形高于水面的格"烘成一张障碍图，
    /// 供 <c>PirateWater.shader</c> 采样（障碍接触带泡沫加亮）与
    /// <see cref="WaterSimulationDriver"/> 读取（诺伊曼反射墙）。
    ///
    /// 【产物】<c>Assets/Art/Textures/Water/WaterObstacleMap.png</c>
    ///   · 128×128（= 模拟域格数），R 通道 255 = 障碍（岛/礁，反射墙），0 = 开阔水；
    ///   · 线性（sRGB off）、无 mipmap、Clamp、Bilinear、Is Readable（驱动要 GetPixels32）；
    ///   · 覆盖 64×64 世界单位、以竞技场中心为中心 —— 与 <see cref="WaterSimulationDriver"/>
    ///     的默认域参数一致，改一边必须改另一边。
    ///
    /// 【数据来源】<see cref="TerrainCatalog.Build"/>（只读，不改地形模块）：
    ///   <c>grid.SurfaceWorldYAtWorld(worldX, worldZ) &gt;= LevelGeometry.WaterSurfaceY</c> 视为障碍。
    ///   竞技场内部（沙岛本体）整片是障碍 —— 这正是"浪拍岸反射"要的边界。
    ///   岛外海床台阶在水面之下 → 不是障碍（只写深度，供浅深水色/焦散）。
    ///
    /// 【缺口，留给协调者】SceneArt 的船体/礁石没有公开几何查询接口，本烘焙**只含静态地形**。
    ///   需要把礁石也算障碍时，请给 SceneArt 加一个"返回占位 AABB/格列表"的公开 API，再在这里并集进来。
    ///
    /// 【无头入口】<c>-executeMethod PirateCrew.EditorTools.WaterAssetBuilder.BakeObstacleMap</c>
    /// </summary>
    public static class WaterAssetBuilder
    {
        public const string TextureFolder = "Assets/Art/Textures/Water";
        public const string ObstacleMapPath = TextureFolder + "/WaterObstacleMap.png";

        /// <summary>Battle 场景未打开时的兜底关卡号（与 Assets/Scenes/Battle.unity 的 fallbackLevelNumber 一致）。</summary>
        public const int FallbackLevelNumber = 1;

        [MenuItem("Tools/PirateCrew/Water/Bake Obstacle Map")]
        public static void BakeObstacleMap()
        {
            int levelNumber = ResolveLevelNumber();
            LevelData level = LevelCatalog.Get(levelNumber);
            if (level.WidthTiles <= 0 || level.HeightTiles <= 0)
            {
                Debug.LogError("[WaterAssetBuilder] 关卡 " + levelNumber + " 尺寸无效，烘焙中止。");
                return;
            }

            int width = level.WidthTiles;
            int depth = level.HeightTiles;
            TileTerrainGrid grid = TerrainCatalog.Build(levelNumber, width, depth);
            bool transcribed = grid != null;
            if (grid == null)
                grid = TileTerrainGrid.Flat(width, depth);

            int cells = WaterSimRules.DefaultCellsPerAxis;
            float domainSize = WaterSimRules.DefaultDomainSize;
            var center = new Vector2(width * 0.5f, depth * 0.5f);
            float waterY = LevelGeometry.WaterSurfaceY;

            // 判据在纯 C# 的 ObstacleMapRules 里（无头可测），这里只做编码。
            bool[] mask = ObstacleMapRules.Bake(grid, center, domainSize, cells, waterY, width, depth);
            int obstacleCount = ObstacleMapRules.CountObstacles(mask);

            var pixels = new Color32[cells * cells];
            for (int i = 0; i < mask.Length; i++)
            {
                bool isObstacle = mask[i];
                byte r = isObstacle ? (byte)255 : (byte)0;
                // 障碍用暖红、水用深蓝，便于直接人眼核对（shader 只读 R 通道）。
                byte g = isObstacle ? (byte)40 : (byte)20;
                byte b = isObstacle ? (byte)40 : (byte)120;
                pixels[i] = new Color32(r, g, b, 255);
            }

            EnsureFolder();
            var tex = new Texture2D(cells, cells, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            File.WriteAllBytes(ObstacleMapPath, png);
            AssetDatabase.ImportAsset(ObstacleMapPath, ImportAssetOptions.ForceUpdate);
            ApplyImporterSettings(cells);

            Debug.Log("[WaterAssetBuilder] 障碍图烘焙完成：" + ObstacleMapPath
                      + " | 关卡 " + levelNumber + (transcribed ? "（已转写地形）" : "（未转写→平坦地面）")
                      + " | 域中心 (" + center.x + ", " + center.y + ") 边长 " + domainSize
                      + " | 障碍格 " + obstacleCount + " / " + (cells * cells));
        }

        static int ResolveLevelNumber()
        {
            BattleController battle = Object.FindObjectOfType<BattleController>();
            if (battle != null)
                return battle.LevelNumber;
            return FallbackLevelNumber;
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(TextureFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art/Textures"))
                    AssetDatabase.CreateFolder("Assets/Art", "Textures");
                AssetDatabase.CreateFolder("Assets/Art/Textures", "Water");
            }
        }

        static void ApplyImporterSettings(int size)
        {
            var importer = AssetImporter.GetAtPath(ObstacleMapPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("[WaterAssetBuilder] 找不到 TextureImporter: " + ObstacleMapPath);
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;          // 数据图，不是颜色
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.isReadable = true;            // 驱动要 GetPixels32
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = Mathf.Max(64, size);
            importer.SaveAndReimport();
        }
    }
}
