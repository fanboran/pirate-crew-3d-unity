using NUnit.Framework;
using PirateCrew.PirateCrew.SceneArt;
using PirateCrew.PirateCrew.SceneArt.Showcase;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 空岛展示件管线的纯 C# 用例（无头可跑，不 new 任何 Unity 对象）。
    ///
    /// 【覆盖】
    ///   · <see cref="FloatingIslandComposer.Compose"/> 端到端可跑（全 15 槽都有几何、无静默空槽）；
    ///   · 确定性（同 seed 两次合成三角面逐槽一致——"同一座岛"的无头判据）；
    ///   · 预算（≤ 4 万面；J2 设计注记约 2 万面）与包围盒合理域；
    ///   · <see cref="FloatingIslandComposer.PlacementHeight"/> 的净空契约：
    ///     按"抖动后最深的悬瀑末端 + 雾团"验证岛体最低件仍高于竞技场净空 9 单位
    ///     （回归用例：标称值口径曾把净空 9 漏成约 4）；
    ///   · <see cref="FloatingIslandScenePlan"/> 的装配契约（路径 / 组名 / 展示位 / 投影口径）。
    /// </summary>
    public class FloatingIslandShowcaseTests
    {
        static FloatingIslandStats ComposeDefault(out IslandBuffers buffers)
        {
            buffers = new IslandBuffers();
            return FloatingIslandComposer.Compose(buffers, FloatingIslandSpec.Default);
        }

        // ------------------------------------------------------------------
        // 合成
        // ------------------------------------------------------------------

        [Test]
        public void Compose_FillsAllFifteenSlots()
        {
            FloatingIslandStats stats = ComposeDefault(out IslandBuffers buffers);

            Assert.AreEqual(15, stats.Slots, "交付构图应点亮全部 15 个材质槽（有空槽 = 某族零件静默消失）");
            for (int i = 0; i < IslandMaterialCatalog.All.Length; i++)
            {
                IslandMaterial slot = IslandMaterialCatalog.All[i];
                Assert.False(buffers.Get(slot).IsEmpty, "槽位 " + slot + " 为空：该材质族的零件没画出来");
            }
        }

        [Test]
        public void Compose_IsDeterministic_ForSameSeed()
        {
            var a = new IslandBuffers();
            var b = new IslandBuffers();
            FloatingIslandStats statsA = FloatingIslandComposer.Compose(a, FloatingIslandSpec.Default);
            FloatingIslandStats statsB = FloatingIslandComposer.Compose(b, FloatingIslandSpec.Default);

            Assert.AreEqual(statsA.Triangles, statsB.Triangles, "同 seed 必得同一座岛（总面数一致）");
            for (int i = 0; i < IslandMaterialCatalog.All.Length; i++)
            {
                IslandMaterial slot = IslandMaterialCatalog.All[i];
                Assert.AreEqual(a.Get(slot).TriangleCount, b.Get(slot).TriangleCount,
                    "同 seed 下槽位 " + slot + " 的面数应逐槽一致");
            }
        }

        [Test]
        public void Compose_TriangleBudget_WithinFortyThousand()
        {
            FloatingIslandStats stats = ComposeDefault(out _);
            Assert.LessOrEqual(stats.Triangles, 40000,
                "空岛三角面超预算（设计注记约 2 万面；上限给到 2 倍余量）");
            Assert.Greater(stats.Triangles, 5000, "面数过少 = 构图没画全");
        }

        [Test]
        public void Compose_BoundsWithinSaneRange()
        {
            FloatingIslandStats stats = ComposeDefault(out IslandBuffers buffers);
            buffers.TryGetBounds(out Vector3 min, out Vector3 max);

            // 顶面（含树冠/高层云/飞鸟）在局部 y ≈ +5..+20；岛尖与岛底云裙在 -RockDepth 更深一侧。
            Assert.LessOrEqual(max.y, 25f, "顶面高出设计域（顶件不该飞到 25 以上）");
            Assert.GreaterOrEqual(max.y, 5f, "顶面过低（树冠/高层云没画出来）");
            Assert.LessOrEqual(min.y, -DefaultSpec().RockDepth, "最深件应越过岛尖（岛底云裙/石笋在尖之下）");
            Assert.GreaterOrEqual(min.y, -DefaultSpec().RockDepth - 45f, "岛底坠得过深（悬瀑/云裙失控）");

            // 水平：岛体椭圆 + 外围卫星，跨度在 2-4 倍半径之间。
            float horizontalSpan = stats.HorizontalSpan;
            Assert.GreaterOrEqual(horizontalSpan, DefaultSpec().RadiusX * 2f, "水平跨度小于岛体本身");
            Assert.LessOrEqual(horizontalSpan, DefaultSpec().RadiusX * 6.5f, "水平跨度过大（卫星晶/云散布失控）");
        }

        [Test]
        public void PlateauY_IsHighestAtCenter_AndMatchesRimHeight()
        {
            FloatingIslandSpec spec = DefaultSpec();
            Assert.Greater(FloatingIslandComposer.PlateauY(spec, 0f, 0f), 0f, "穹顶中心应高于局部 y=0 基准");

            // 顶面零件的落点必须贴在高度场上：取一个已知会贴地调用的方位（OnPlateau 的等价公式）。
            float rimY = FloatingIslandComposer.PlateauY(spec, spec.RadiusX, 0f);
            Assert.LessOrEqual(rimY, 0.01f, "轮廓边缘处穹顶高度应归零（草皮只在岛内隆起）");
        }

        // ------------------------------------------------------------------
        // 摆放高度（净空契约）
        // ------------------------------------------------------------------

        [Test]
        public void PlacementHeight_KeepsClearance_ForWorstCaseWaterfall()
        {
            FloatingIslandSpec spec = DefaultSpec();
            float rootY = FloatingIslandComposer.PlacementHeight(spec, 0f);

            // 最坏零件：帘长抖动到 1.14×，末端雾团再垂 1.9（与 AddWaterfalls 的抖动上界同口径）。
            float deepestPart = spec.WaterfallLength * 1.14f + 1.9f;
            Assert.GreaterOrEqual(rootY - deepestPart, FloatingIslandComposer.ArenaClearance,
                "最坏悬瀑末端穿透净空线（净空 " + FloatingIslandComposer.ArenaClearance + "）");

            // 岛尖（局部 -RockDepth）同样不得低于净空线。
            Assert.GreaterOrEqual(rootY - spec.RockDepth, FloatingIslandComposer.ArenaClearance,
                "岛尖穿透净空线");
        }

        [Test]
        public void PlacementHeight_RespectsArenaY()
        {
            FloatingIslandSpec spec = DefaultSpec();
            Assert.AreEqual(
                FloatingIslandComposer.PlacementHeight(spec, 0f) + 3.5f,
                FloatingIslandComposer.PlacementHeight(spec, 3.5f), 1e-4f,
                "摆放高度应随参考地面整体平移");
        }

        // ------------------------------------------------------------------
        // 落场景计划（路径 / 命名 / 展示位 / 投影口径）
        // ------------------------------------------------------------------

        [Test]
        public void ScenePlan_PathsAndNames_FollowConvention()
        {
            Assert.AreEqual("FloatingIslandShowcase", FloatingIslandScenePlan.RootName);
            Assert.AreEqual("Island_RockMid", FloatingIslandScenePlan.GroupName(IslandMaterial.RockMid));
            Assert.AreEqual("Assets/Art/Models/Scene/FloatingIsland_Stone.asset",
                FloatingIslandScenePlan.MeshAssetPath(IslandMaterial.Stone));
            Assert.AreEqual("Assets/Art/Materials/Scene/FloatingIsland_Wood.mat",
                FloatingIslandScenePlan.MaterialAssetPath(IslandMaterial.Wood));
        }

        [Test]
        public void ScenePlan_BackdropPosition_HoversBeyondFarEdge_WithClearance()
        {
            FloatingIslandSpec spec = DefaultSpec();

            // 样板第 1 关：20×15 格 × 2 单位/格 → 中心 X=20、远缘 Z=0。
            Vector3 pos = FloatingIslandScenePlan.DefaultBackdropRootPosition(spec);

            Assert.AreEqual(20f, pos.x, 1e-3f, "展示位应水平对准竞技场中心");
            Assert.AreEqual(FloatingIslandComposer.PlacementHeight(spec, 0f), pos.y, 1e-4f,
                "展示位高度必须走 PlacementHeight（净空契约的唯一入口）");
            Assert.LessOrEqual(pos.z, -FloatingIslandScenePlan.FarEdgeGap - spec.RadiusZ,
                "展示位应在远缘外侧足够远（岛体不压危险虚线）");
        }

        [Test]
        public void ScenePlan_ShadowPolicy_SolidReceives_TransparentAndGlowNot()
        {
            Assert.True(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.RockMid));
            Assert.True(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.GrassLight));
            Assert.True(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Wood));
            Assert.True(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Stone));
            Assert.True(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Banner));
            Assert.False(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Water));
            Assert.False(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Foam));
            Assert.False(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Cloud));
            Assert.False(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Crystal));
            Assert.False(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Glow));
        }

        [Test]
        public void MaterialCatalog_RecipesAreComplete()
        {
            for (int i = 0; i < IslandMaterialCatalog.All.Length; i++)
            {
                IslandMaterial slot = IslandMaterialCatalog.All[i];
                IslandMaterialRecipe recipe = IslandMaterialCatalog.For(slot);
                Assert.NotNull(recipe, "槽位 " + slot + " 缺材质配方（目录表漏登记）");

                if (recipe.Kind == IslandShaderKind.SurfaceSolid)
                {
                    Assert.False(string.IsNullOrEmpty(recipe.HexDark), slot + " 缺暗档色");
                    Assert.False(string.IsNullOrEmpty(recipe.HexMid), slot + " 缺中档色");
                    Assert.False(string.IsNullOrEmpty(recipe.HexLight), slot + " 缺亮档色");
                }
                else
                {
                    Assert.False(string.IsNullOrEmpty(recipe.Hex), slot + " 缺单基色");
                    // 云/沫禁用纯白 255（SceneArtBuilder 的 r2 教训）：单基色每个通道 ≤ 250。
                    if (recipe.Kind == IslandShaderKind.UnlitTransparent)
                    {
                        Color c = SceneArtPalette.Hex(recipe.Hex);
                        Assert.LessOrEqual(Mathf.Max(c.r, Mathf.Max(c.g, c.b)) * 255f, 250f,
                            slot + " 的基色含接近纯白的通道（亮天空前会烧白洞）");
                    }
                }

                // 实体档不投影发光/半透明件：投影口径由配方唯一决定（CastShadows），
                // 与 ScenePlan.ReceivesShadows 的分档在上方用例单独断言。
            }
        }

        // ------------------------------------------------------------------
        // 可玩地面：碰撞代理 + 出生点表（第 3 关验收口径）
        // ------------------------------------------------------------------

        [Test]
        public void CollisionSurface_IsClosedShell_AtDomeTop()
        {
            FloatingIslandSpec spec = DefaultSpec();
            var shell = new MeshBuffers();
            FloatingIslandComposer.BuildCollisionSurface(shell, spec);

            Vector3[] verts = shell.ToVertices();
            Vector3 min = verts[0], max = verts[0];
            for (int i = 1; i < verts.Length; i++)
            {
                min = Vector3.Min(min, verts[i]);
                max = Vector3.Max(max, verts[i]);
            }

            // 顶盖最高点 = 草皮穹顶中心；侧裙/底盖略低于垂帘末端（-0.8 - 抖动）。
            Assert.AreEqual(FloatingIslandComposer.PlateauY(spec, 0f, 0f), max.y, 0.05f,
                "碰撞面顶盖必须与穹顶同高（'看着站的'与'物理上站的'不分叉）");
            Assert.GreaterOrEqual(min.y, -1.8f, "碰撞壳不应深入岛体（它是顶面薄壳不是全岛）");
            Assert.LessOrEqual(min.y, -0.6f, "侧裙缺失（弹体会从上沿以下直接穿进岛体）");
            // 确定性面数：3 环带×16×2 + 顶毂 16 + 侧裙 16×2 + 底盖 16 = 160。
            Assert.AreEqual(160, shell.TriangleCount, "碰撞薄壳面数漂移（环公式被改动？）");
        }

        [Test]
        public void SpawnTable_GivesTwoSquads_WithRequiredSpacing()
        {
            IslandSpawnPoint[] spawns = FloatingIslandSpawnTable.Build(DefaultSpec());

            Assert.GreaterOrEqual(spawns.Length, 6, "出生点不足两支小队（红蓝各 3-4）");
            Assert.LessOrEqual(spawns.Length, 8, "出生点超额");

            int red = 0, blue = 0;
            foreach (IslandSpawnPoint p in spawns)
            {
                if (p.TeamIndex == 0) red++;
                else blue++;
            }
            Assert.GreaterOrEqual(red, 3, "红队不足 3 位");
            Assert.GreaterOrEqual(blue, 3, "蓝队不足 3 位");

            // 两两间距 ≥3（验收口径；同队/跨队一并检查）。
            for (int i = 0; i < spawns.Length; i++)
            {
                for (int j = i + 1; j < spawns.Length; j++)
                {
                    float d = Vector3.Distance(spawns[i].Position, spawns[j].Position);
                    Assert.GreaterOrEqual(d, 3f,
                        "出生位 " + i + " 与 " + j + " 间距 " + d.ToString("0.00") + " < 3");
                }
            }
        }

        [Test]
        public void SpawnTable_PointsStandOnDome_ClearOfKeepOuts()
        {
            FloatingIslandSpec spec = DefaultSpec();
            IslandSpawnPoint[] spawns = FloatingIslandSpawnTable.Build(spec);
            IslandKeepOutZone[] zones = FloatingIslandComposer.BuildKeepOutZones(spec);

            foreach (IslandSpawnPoint p in spawns)
            {
                // 脚底高度必须就是草皮高度场（装配器只补 UnitPivotHeight，不该再做地面修正）。
                Assert.AreEqual(FloatingIslandComposer.PlateauY(spec, p.Position.x, p.Position.z),
                    p.Position.y, 1e-4f, "出生位未贴合草皮面");

                // 都在穹顶平缓区内（不出顶面、不站到崖沿外）。
                float nx = p.Position.x / spec.RadiusX;
                float nz = p.Position.z / spec.RadiusZ;
                Assert.LessOrEqual(Mathf.Sqrt(nx * nx + nz * nz), 0.8f, "出生位出顶面（r>0.8）");

                // 不压任何构图预留区（遗迹/瞭望台/老树/水潭）。
                foreach (IslandKeepOutZone zone in zones)
                {
                    float d = Vector2.Distance(
                        new Vector2(p.Position.x, p.Position.z),
                        new Vector2(zone.Center.x, zone.Center.z));
                    Assert.GreaterOrEqual(d, zone.Radius,
                        "出生位 (" + p.Position.x.ToString("0.0") + "," + p.Position.z.ToString("0.0")
                        + ") 压进预留区（半径 " + zone.Radius.ToString("0.0") + "）");
                }
            }
        }

        [Test]
        public void SpawnTable_IsDeterministic()
        {
            IslandSpawnPoint[] a = FloatingIslandSpawnTable.Build(DefaultSpec());
            IslandSpawnPoint[] b = FloatingIslandSpawnTable.Build(DefaultSpec());
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].TeamIndex, b[i].TeamIndex);
                Assert.AreEqual(a[i].Position, b[i].Position);
            }
        }

        [Test]
        public void KeepOutZones_SnapToComposedFeatures()
        {
            FloatingIslandSpec spec = DefaultSpec();
            IslandKeepOutZone[] zones = FloatingIslandComposer.BuildKeepOutZones(spec);

            Assert.GreaterOrEqual(zones.Length, 5, "预留区缺失（遗迹/瞭望台/老树/水潭至少 5 处）");
            foreach (IslandKeepOutZone zone in zones)
            {
                // 区域中心必须真在草皮面上（公式漂移会浮空或入地）。
                Assert.AreEqual(
                    FloatingIslandComposer.PlateauY(spec, zone.Center.x, zone.Center.z),
                    zone.Center.y, 1e-4f, "预留区中心脱离草皮高度场");
                Assert.Greater(zone.Radius, 0f);
            }
        }

        static FloatingIslandSpec DefaultSpec()
        {
            return FloatingIslandSpec.Default;
        }
    }
}
