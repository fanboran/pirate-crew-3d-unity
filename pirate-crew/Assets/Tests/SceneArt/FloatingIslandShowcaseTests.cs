using NUnit.Framework;
using PirateCrew.SceneArt;
using PirateCrew.SceneArtBake.Showcase;
using UnityEngine;

namespace PirateCrew.SceneArt.Tests
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
        public void ScenePlan_ShadowPolicy_SolidReceives_TransparentNot()
        {
            Assert.True(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.RockMid));
            Assert.True(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.GrassLight));
            Assert.True(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Wood));
            Assert.True(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Stone));
            Assert.True(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Banner));
            // 晶/光自 2026-10-07 改受光像素材质（加色在亮天空前必截顶成白片），随实体档收影；
            // 像素材质不采样 Unity 阴影贴图，这一位只作场景存档口径。
            Assert.True(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Crystal));
            Assert.True(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Glow));
            Assert.False(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Water));
            Assert.False(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Foam));
            Assert.False(FloatingIslandScenePlan.ReceivesShadows(IslandMaterial.Cloud));
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

        [Test]
        public void MaterialCatalog_CrystalAndGlow_AreLitPixelSurfaces()
        {
            // 加色档在亮天空前必把 G/B 推过 1 截顶成"没有明暗的惨白片"（创始人实拍：
            // "为什么我们的水晶变成这样了"）——晶/光两槽锁在实体像素档 = 与试点场景的派生观感同源。
            Assert.AreEqual(IslandShaderKind.SurfaceSolid, IslandMaterialCatalog.For(IslandMaterial.Crystal).Kind);
            Assert.AreEqual(IslandShaderKind.SurfaceSolid, IslandMaterialCatalog.For(IslandMaterial.Glow).Kind);
            Assert.AreEqual("#7CF0E4", IslandMaterialCatalog.For(IslandMaterial.Crystal).HexDark,
                "晶体的像素反照率（试点派生也读这个色，改了会出现两场景分叉）");
            Assert.AreEqual("#FFCB6B", IslandMaterialCatalog.For(IslandMaterial.Glow).HexDark);
        }

        // ------------------------------------------------------------------
        // 可玩地面：碰撞代理 + 出生点表（第 2 关验收口径）
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

        // ------------------------------------------------------------------
        // 浮件拆件（天外件 → 独立物体，运行期缓慢浮沉）
        // ------------------------------------------------------------------

        [Test]
        public void Compose_WithFloaterTable_SeparatesSkyPieces_WithoutChangingGeometry()
        {
            FloatingIslandSpec spec = DefaultSpec();
            var mergedBuffers = new IslandBuffers();
            FloatingIslandStats mergedStats = FloatingIslandComposer.Compose(mergedBuffers, spec);

            var splitBuffers = new IslandBuffers();
            var floaters = new System.Collections.Generic.List<FloatingIslandFloater>();
            FloatingIslandStats splitStats = FloatingIslandComposer.Compose(splitBuffers, spec, floaters);

            // 件数 = 悬浮晶 + 浮石 + 云团 + 岛底云裙(3) + 飞鸟；萤光尘留在共享槽（太小，动也读不出）。
            Assert.AreEqual(
                spec.SatelliteCrystalCount + spec.FloatingRockCount + spec.CloudPuffCount + 3 + spec.BirdCount,
                floaters.Count, "天外浮件件数不符（应各成一件）");

            int floaterTriangles = 0;
            for (int i = 0; i < floaters.Count; i++)
            {
                FloatingIslandFloater floater = floaters[i];
                Assert.Greater(floater.Parts.Count, 0, "浮件 " + i + " 没有任何分件网格");
                Assert.Greater(floater.BobAmplitude, 0f, "浮件 " + i + " 缺浮沉幅度");
                Assert.Greater(floater.Speed, 0f, "浮件 " + i + " 缺浮沉速度");
                for (int k = 0; k < floater.Parts.Count; k++)
                {
                    Assert.False(floater.Parts[k].Mesh.IsEmpty,
                        "浮件 " + i + " 的 " + floater.Parts[k].Slot + " 分件为空");
                    floaterTriangles += floater.Parts[k].Mesh.TriangleCount;
                }
            }

            // 拆件只是"把天外件搬进自己的网格"：总面数必须与不拆时逐面相等。
            Assert.AreEqual(mergedStats.Triangles, splitStats.Triangles,
                "拆件前后的总面数必须一致（拆件不该增删几何）");
            Assert.Greater(floaterTriangles, 0, "浮件表里一片几何都没有");
        }

        // ------------------------------------------------------------------
        // 出生站位单源化：穹顶高度场下沉运行时程序集，出生链逐点贴穹顶
        // ------------------------------------------------------------------

        [Test]
        public void Dome_PlateauY_SingleSource_BakeForwardMatchesRuntime()
        {
            // 烘焙侧转发口（FloatingIslandComposer.PlateauY）与运行时唯一真源
            // （FloatingIslandDome.PlateauY）逐点一致：烘焙几何与出生贴地共用同一高度场，
            // 「可见表面 = 可站立表面」才不会因两份公式各自漂移而破。
            FloatingIslandSpec spec = DefaultSpec();
            for (int i = 0; i < 24; i++)
            {
                float x = Mathf.Cos(i * 0.71f) * spec.RadiusX * 0.75f;
                float z = Mathf.Sin(i * 0.71f) * spec.RadiusZ * 0.75f;
                Assert.AreEqual(FloatingIslandDome.PlateauY(spec, x, z),
                    FloatingIslandComposer.PlateauY(spec, x, z), 1e-6f,
                    "烘焙转发口与运行时穹顶高度场在 (" + x.ToString("0.00") + "," + z.ToString("0.00") + ") 分叉");
            }
        }

        [Test]
        public void Dome_SpawnGroundY_IsWorldOffsetOfLocalDome()
        {
            // 装配契约：PlaceIntoBattleCenter 只改根节点 position（不旋转不缩放），
            // 世界↔局部即平移——SpawnGroundY 必须严格等于「根 Y + 局部穹顶高度」，
            // 与装配器日志的 TransformPoint 同值。
            FloatingIslandSpec spec = DefaultSpec();
            Vector3 root = new Vector3(20f, 13.3f, 15f);   // 场心居中位（装配器写死的摆放）
            for (int i = 0; i < 12; i++)
            {
                float localX = Mathf.Cos(i * 1.3f) * spec.RadiusX * 0.6f;
                float localZ = Mathf.Sin(i * 1.3f) * spec.RadiusZ * 0.6f;
                Assert.AreEqual(
                    root.y + FloatingIslandDome.PlateauY(spec, localX, localZ),
                    FloatingIslandDome.SpawnGroundY(spec, root, root.x + localX, root.z + localZ),
                    1e-5f, "世界站面与「根 Y + 局部穹顶」不一致");
            }
        }

        [Test]
        public void SpawnGroundY_GroundsEverySkyIslandUnit_OnDome()
        {
            // 第 2 关（sky_island）golden 出生点逐点贴穹顶：出生站面 Y = 根 Y + PlateauY(局部)。
            // 与逻辑高度场平顶（topY=14）的逐点落差即悬空/埋脚量（穹顶 13.3→14.65），
            // 贴穹顶后归零；此测试钉住「落差窗口存在且有限」，单位挪位/构图改参时强制重看。
            Assert.IsTrue(PirateCrew.Data.LevelAssetLibrary.TryGetLevel(2,
                out PirateCrew.Data.LevelAssetPayload payload),
                "取不到第 2 关关卡数据（无头侧应回落 golden JSON）");
            Assert.Greater(payload.units.Count, 0, "第 2 关没有出战单位");

            FloatingIslandSpec spec = DefaultSpec();
            Vector3 root = new Vector3(20f, 13.3f, 15f);
            float flatTopY = payload.terrain.islands[0].topY;
            float worstFlatGap = 0f;

            foreach (PirateCrew.Data.LevelUnit unit in payload.units)
            {
                float grounded = FloatingIslandDome.SpawnGroundY(spec, root, unit.x, unit.z);

                Assert.AreEqual(
                    root.y + FloatingIslandDome.PlateauY(spec, unit.x - root.x, unit.z - root.z),
                    grounded, 1e-5f, "单位 " + unit.typeName + " 的出生站面未贴合穹顶");
                Assert.GreaterOrEqual(grounded, 13.29f,
                    "单位 " + unit.typeName + " 站面低于崖沿（出生点出了穹顶？）");
                Assert.LessOrEqual(grounded, 14.75f,
                    "单位 " + unit.typeName + " 站面高于穹顶中心");

                worstFlatGap = Mathf.Max(worstFlatGap, Mathf.Abs(grounded - flatTopY));
            }

            Assert.GreaterOrEqual(worstFlatGap, 0.05f,
                "出生点与平顶的落差消失了？——要么单位数据被挪到穹顶恰好 =14 的环上，"
                + "要么贴地链路没生效，重看本测试与 golden 数据");
            Assert.LessOrEqual(worstFlatGap, 0.7f,
                "出生点与平顶落差超 0.7——构图参数（PlateauDome/半径）改动后穹顶起伏已超原验收窗口，重看");
        }

        // ------------------------------------------------------------------
        // 类体素逐格高度（v4）：烘焙采样 ↔ 穹顶公式 ↔ 光栅化消费 的一致性
        // ------------------------------------------------------------------

        /// <summary>第 2 关空岛根位（Battle 场景装配器写死的场心居中位；与上方穹顶用例同源）。</summary>
        static readonly Vector3 SkyIslandRoot = new Vector3(20f, 13.3f, 15f);

        [Test]
        public void HeightsBake_SamplesDomeAtCellCenters_WithGridConvention()
        {
            // 采样口径与 TerrainShapeRasterizer 同一套：格中心 ((col+0.5)·cell, (row+0.5)·cell)、
            // 行主序、轮廓外包围盒角落 = 0（那里不是岛）。逐格与穹顶公式在格中心严格同值。
            FloatingIslandSpec spec = DefaultSpec();
            var island = new PirateCrew.Data.TerrainIsland
            {
                topY = 14f,
                outline = new System.Collections.Generic.List<float>
                { 8f, 4f, 32f, 4f, 32f, 26f, 8f, 26f },   // L2 岛包围盒量级的矩形岛
            };

            PirateCrew.Data.TerrainIsland baked = FloatingIslandHeightsBake.Bake(
                island, spec, SkyIslandRoot, 40f, 30f);

            Assert.IsTrue(baked.HasHeights, "烘焙后岛必须带逐格高度");
            // 包围盒 8..32 × 4..26 → 格 col 4..16、row 2..13（外扩到整格）。
            Assert.AreEqual(4, baked.heightCol0, "col0 = floor(minX/cell)");
            Assert.AreEqual(16 - 4, baked.heightCols, "cols = ceil(maxX/cell) - col0");
            Assert.AreEqual(2, baked.heightRow0, "row0 = floor(minZ/cell)");
            Assert.AreEqual(13 - 2, baked.heightRows, "rows = ceil(maxZ/cell) - row0");
            Assert.AreEqual(baked.heightCols * baked.heightRows, baked.heights.Count);

            const float cell = PirateCrew.Data.LevelAssetSchema.RasterCellSize;
            for (int row = 0; row < baked.heightRows; row++)
            {
                float cz = (baked.heightRow0 + row + 0.5f) * cell;
                for (int col = 0; col < baked.heightCols; col++)
                {
                    float cx = (baked.heightCol0 + col + 0.5f) * cell;
                    float value = baked.heights[col + row * baked.heightCols];
                    if (PirateCrew.Data.TerrainShapeRasterizer.PointInIsland(cx, cz, baked))
                    {
                        Assert.AreEqual(
                            SkyIslandRoot.y + FloatingIslandDome.PlateauY(
                                spec, cx - SkyIslandRoot.x, cz - SkyIslandRoot.z),
                            value, 1e-4f,
                            "岛内格 (" + cx + "," + cz + ") 必须 = 根 Y + PlateauY(局部)（格中心采样）");
                        Assert.Greater(value, 13.29f, "岛内格不低于崖沿");
                    }
                    else
                    {
                        Assert.AreEqual(0f, value, 1e-6f,
                            "轮廓外包围盒角落 (" + cx + "," + cz + ") 不是岛，写 0");
                    }
                }
            }
        }

        [Test]
        public void HeightsBake_IsDeterministic()
        {
            FloatingIslandSpec spec = DefaultSpec();
            var outline = new System.Collections.Generic.List<float>
            { 8f, 4f, 32f, 4f, 32f, 26f, 8f, 26f };

            var a = new PirateCrew.Data.TerrainIsland { topY = 14f, outline = outline };
            var b = new PirateCrew.Data.TerrainIsland { topY = 14f, outline = outline };
            FloatingIslandHeightsBake.Bake(a, spec, SkyIslandRoot, 40f, 30f);
            FloatingIslandHeightsBake.Bake(b, spec, SkyIslandRoot, 40f, 30f);

            CollectionAssert.AreEqual(a.heights, b.heights, "同岛同根必得同一数组（golden 重生成不漂移）");
            Assert.AreEqual(a.heightCol0, b.heightCol0);
            Assert.AreEqual(a.heightCols, b.heightCols);
        }

        [Test]
        public void SkyIslandLevel_DataHeights_MatchDome_WithinCellDropPlusQuantization()
        {
            // 出生站位单源化的收口判据：第 2 关 golden 带逐格高度后，「光栅化数据高度」与
            // 「穹顶公式逐点值」的差 ≤ 该单位所在采样格内的穹顶落差 + 半块量化（0.25）。
            // 两条路径都活、数据赢——这条钉住它们没有分叉（采样口径漂移会立刻爆掉）。
            Assert.IsTrue(PirateCrew.Data.LevelAssetLibrary.TryGetLevel(2,
                out PirateCrew.Data.LevelAssetPayload payload),
                "取不到第 2 关关卡数据（无头侧应回落 golden JSON）");

            PirateCrew.Data.TerrainIsland island = payload.terrain.islands[0];
            Assert.IsTrue(island.HasHeights,
                "第 2 关 golden 未带逐格高度——先跑 PirateCrew/关卡/烘焙逐格高度入 golden（schema 3→4）");

            FloatingIslandSpec spec = DefaultSpec();
            const float cell = PirateCrew.Data.LevelAssetSchema.RasterCellSize;

            // 抽 5 点对拍（网格四角 + 中心）：golden 里的值必须与穹顶公式在这些格中心严格同值。
            (int col, int row)[] probes =
            {
                (island.heightCol0, island.heightRow0),
                (island.heightCol0 + island.heightCols - 1, island.heightRow0),
                (island.heightCol0, island.heightRow0 + island.heightRows - 1),
                (island.heightCol0 + island.heightCols - 1, island.heightRow0 + island.heightRows - 1),
                (island.heightCol0 + island.heightCols / 2, island.heightRow0 + island.heightRows / 2),
            };
            foreach ((int col, int row) in probes)
            {
                float cx = (col + 0.5f) * cell;
                float cz = (row + 0.5f) * cell;
                float golden = island.heights[col - island.heightCol0 + (row - island.heightRow0) * island.heightCols];
                float expected = PirateCrew.Data.TerrainShapeRasterizer.PointInIsland(cx, cz, island)
                    ? SkyIslandRoot.y + FloatingIslandDome.PlateauY(spec, cx - SkyIslandRoot.x, cz - SkyIslandRoot.z)
                    : 0f;
                Assert.AreEqual(expected, golden, 1e-4f,
                    "对拍格 (col " + col + ", row " + row + ") 与穹顶公式分叉——采样口径漂移");
            }

            PirateCrew.Battle.HeightfieldGrid grid =
                PirateCrew.Battle.Levels.LevelRasterFromAsset.Build(payload);
            float halfBlock = payload.terrain.blockWorldHeight * 0.5f;

            foreach (PirateCrew.Data.LevelUnit unit in payload.units)
            {
                float domeY = FloatingIslandDome.SpawnGroundY(spec, SkyIslandRoot, unit.x, unit.z);
                int cellX = Mathf.FloorToInt(unit.x / cell);
                int cellY = Mathf.FloorToInt(unit.z / cell);
                float dataY = grid.SurfaceWorldY(cellX, cellY);

                // 格内穹顶落差：格中心的穹顶值 vs 单位点（同一条公式，只是位置不同）。
                float domeAtCenter = FloatingIslandDome.SpawnGroundY(
                    spec, SkyIslandRoot, (cellX + 0.5f) * cell, (cellY + 0.5f) * cell);
                float inCellDrop = Mathf.Abs(domeAtCenter - domeY);
                float tolerance = inCellDrop + halfBlock + 1e-3f;

                Assert.AreEqual(domeAtCenter,
                    SkyIslandRoot.y + FloatingIslandDome.PlateauY(
                        spec, (cellX + 0.5f) * cell - SkyIslandRoot.x, (cellY + 0.5f) * cell - SkyIslandRoot.z),
                    1e-4f, "前置：格中心穹顶值与采样公式一致");

                Assert.LessOrEqual(Mathf.Abs(dataY - domeY), tolerance,
                    "单位 " + unit.typeName + " (" + unit.x + "," + unit.z + ") 的数据高度 "
                    + dataY + " 与穹顶 " + domeY + " 的差超过「格内落差 " + inCellDrop.ToString("0.000")
                    + " + 半块 " + halfBlock + "」——采样口径或格子对齐漂移了");
                Assert.GreaterOrEqual(dataY, 13.5f, "台阶不得低于 27 块档（穹顶最低 13.3 量化后 13.5）");
                Assert.LessOrEqual(dataY, 14.5f, "台阶不得高于 29 块档（穹顶最高 14.65 量化后 14.5）");
            }
        }
    }
}
