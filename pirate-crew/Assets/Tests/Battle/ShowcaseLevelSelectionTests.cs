using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PirateCrew.ArtReview;
using PirateCrew.Battle.Levels;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Data;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 「选关页点样板关进图」这条通道的门禁（无头可跑）：待战样板关
    /// （<see cref="WorldMapRuntime.SetPendingShowcase"/>）与待战海图互斥，解析优先级里它夹在
    /// 出图覆盖与待战海图之间。
    ///
    /// 【为什么单独钉它】两个待战槽位都是静态字段，写侧漏清一个就会出现"点了样板关却进了上一局的海图"
    /// 这类跨局串台——静态残留是本仓最难查的故障。所以这里既断言注入式纯函数的优先级，
    /// 也断言静态槽位的互斥、坏关号的拒绝与复位链。
    /// </summary>
    public class ShowcaseLevelSelectionTests
    {
        /// <summary>现存样板关的关卡号：1 云端漫步 / 3 天空之岛（关卡 2「碎岛雨」已删除，号段有意不连续）。</summary>
        const int CloudWalk = 1;
        const int SkyIsland = 3;

        /// <summary>已删除的关卡号——用来证明"不存在的样板关号会被拒绝"。</summary>
        const int DeletedLevel = 2;

        [SetUp]
        public void SetUp()
        {
            ClearStaticSlots();
        }

        [TearDown]
        public void TearDown()
        {
            ClearStaticSlots();
        }

        /// <summary>两个待战槽位与出图覆盖都是静态字段——测试之间必须逐个清干净。</summary>
        static void ClearStaticSlots()
        {
            WorldMapRuntime.ClearPending();
            WorldMapRuntime.ClearPendingShowcase();
            ArtReviewCaptureOverride.LevelNumber = 0;
        }

        // ------------------------------------------------------------------
        // 优先级
        // ------------------------------------------------------------------

        [Test]
        public void PendingShowcase_BeatsPendingWorldMap()
        {
            // Arrange：刻意让两级同时有待战内容（真实流程里写入口互斥；这里直接考优先级本身）。
            WorldMapDefinition map = WorldMapCatalog.All[0];

            // Act
            LevelSource source = LevelSourceResolver.Resolve(0, map, SkyIsland);

            // Assert：走样板关，且海图那一档的字段一律为空（免得上层按海图语义取用）。
            Assert.That(source, Is.Not.Null);
            Assert.That(source.Kind, Is.EqualTo(LevelSourceKind.Showcase));
            Assert.That(source.LevelNumber, Is.EqualTo(SkyIsland));
            Assert.That(source.IsWorldMapActive, Is.False);
            Assert.That(source.WorldMap, Is.Null, "样板关局不该带海图定义");
            Assert.That(source.Notice, Is.Null, "选关页点选是正常路径，不该留回落告警");
        }

        [Test]
        public void ArtReviewOverride_BeatsPendingShowcase()
        {
            // Arrange：出图覆盖是工具专用通道，出现即最高优先。两级样板关号取不同的值
            // （覆盖 = 关卡 3 / 待战 = 关卡 1），否则分不清是谁赢的。
            WorldMapDefinition map = WorldMapCatalog.All[0];

            // Act
            LevelSource source = LevelSourceResolver.Resolve(SkyIsland, map, CloudWalk);

            // Assert
            Assert.That(source, Is.Not.Null);
            Assert.That(source.Kind, Is.EqualTo(LevelSourceKind.Showcase));
            Assert.That(source.LevelNumber, Is.EqualTo(SkyIsland), "出图覆盖必须压过选关页点选的样板关");
            Assert.That(source.Notice, Is.Null, "覆盖命中时不该有回落告警");
        }

        /// <summary>
        /// 静态状态 → <see cref="LevelSourceResolver.Resolve()"/> 的接线（注入式重载测不到这一段）：
        /// 出图覆盖在场时压过待战样板关，覆盖撤掉后轮到待战样板关。
        /// </summary>
        [Test]
        public void Resolve_ReadsPendingShowcaseFromStaticState()
        {
            // Arrange
            Assert.That(WorldMapRuntime.SetPendingShowcase(SkyIsland), Is.True);
            ArtReviewCaptureOverride.LevelNumber = CloudWalk;

            // Act / Assert：① 覆盖赢
            LevelSource overridden = LevelSourceResolver.Resolve();
            Assert.That(overridden, Is.Not.Null);
            Assert.That(overridden.LevelNumber, Is.EqualTo(CloudWalk));

            // ② 撤掉覆盖 → 待战样板关（这同时证明 Resolve() 把样板关通道真读了出来）
            ArtReviewCaptureOverride.LevelNumber = 0;
            LevelSource selected = LevelSourceResolver.Resolve();
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected.Kind, Is.EqualTo(LevelSourceKind.Showcase));
            Assert.That(selected.LevelNumber, Is.EqualTo(SkyIsland));
        }

        // ------------------------------------------------------------------
        // 静态槽位的互斥与复位
        // ------------------------------------------------------------------

        [Test]
        public void SetPending_AndSetPendingShowcase_ClearEachOther()
        {
            // Arrange
            string mapId = WorldMapCatalog.All[0].Id;

            // Act / Assert：先点样板关
            Assert.That(WorldMapRuntime.SetPendingShowcase(CloudWalk), Is.True);
            Assert.That(WorldMapRuntime.TryGetPendingShowcase(out int selected), Is.True);
            Assert.That(selected, Is.EqualTo(CloudWalk));

            // 再点海图：样板关槽位必须立刻清空
            Assert.That(WorldMapRuntime.SetPending(mapId), Is.True);
            Assert.That(WorldMapRuntime.TryGetPendingShowcase(out _), Is.False, "写海图没清掉待战样板关");
            Assert.That(WorldMapRuntime.TryGetPending(out WorldMapDefinition map), Is.True);
            Assert.That(map.Id, Is.EqualTo(mapId));

            // 再点样板关：海图槽位必须真的被清空——读侧（TryGetPending 在样板待战期间让位）
            // 会掩盖没清这件事，所以先撤掉样板关再看海图会不会"复活"。
            Assert.That(WorldMapRuntime.SetPendingShowcase(SkyIsland), Is.True);
            Assert.That(WorldMapRuntime.TryGetPending(out _), Is.False);
            WorldMapRuntime.ClearPendingShowcase();
            Assert.That(WorldMapRuntime.TryGetPending(out _), Is.False,
                "写样板关没清掉海图槽位：撤掉样板关后上一局的海图又活了");
        }

        [Test]
        public void SetPendingShowcase_RejectsLevelNumberWithoutAsset()
        {
            // Arrange：先放一个有效的待战样板关，用来验证"拒绝时也不改动现状"。
            Assert.That(WorldMapRuntime.SetPendingShowcase(CloudWalk), Is.True);

            // Act / Assert：已删除的关卡号、以及 0 / 负数（表示"无"）都不得被接受。
            Assert.That(WorldMapRuntime.SetPendingShowcase(DeletedLevel), Is.False,
                "关卡 " + DeletedLevel + " 已删除，不该被接受");
            Assert.That(WorldMapRuntime.SetPendingShowcase(0), Is.False);
            Assert.That(WorldMapRuntime.SetPendingShowcase(-1), Is.False);

            // 现状不变（接受坏关号会顺手清空待战内容，把这一局变成兜底关 1）
            Assert.That(WorldMapRuntime.TryGetPendingShowcase(out int levelNumber), Is.True);
            Assert.That(levelNumber, Is.EqualTo(CloudWalk));

            // 解析侧同样忽略坏关号：即使有人绕过写侧校验塞进来，也让位给待战海图而不是兜底关。
            LevelSource source = LevelSourceResolver.Resolve(0, WorldMapCatalog.All[0], DeletedLevel);
            Assert.That(source.Kind, Is.EqualTo(LevelSourceKind.WorldMap));
        }

        /// <summary>
        /// 复位链：<c>WorldMapRuntime.ResetStatics</c>（进入播放前唯一入口调用）必须把样板关槽位也清掉。
        /// 它是 <c>internal</c>（测试程序集没有 InternalsVisibleTo），所以用反射调——
        /// 它只清缓存、不碰原生对象，无头安全（与 GameBootstrapTests 刻意不碰 Initialize/ResetStatics
        /// 那两个真正碰 ECall 的阶段不同）。
        /// </summary>
        [Test]
        public void ResetStatics_ClearsPendingShowcase()
        {
            // Arrange
            MethodInfo reset = typeof(WorldMapRuntime).GetMethod(
                "ResetStatics", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(reset, Is.Not.Null, "WorldMapRuntime.ResetStatics 被改名/删除了（复位链断了）");
            Assert.That(WorldMapRuntime.SetPendingShowcase(SkyIsland), Is.True);

            // Act
            reset.Invoke(null, null);

            // Assert
            Assert.That(WorldMapRuntime.TryGetPendingShowcase(out _), Is.False,
                "复位漏了样板关槽位 = 下一局点了海图也会进样板关");
            Assert.That(WorldMapRuntime.TryGetPending(out _), Is.False);
        }

        // ------------------------------------------------------------------
        // 选关列表的内容契约（列表由这两份清单拼成）
        // ------------------------------------------------------------------

        /// <summary>
        /// 选关页必须列出全部 10 关、且按关卡号升序（页面把两份清单拼起来后排序，
        /// 号段不重叠 → 样板关 1/3 在前、海图 101–108 在后）。冻结期望值：改号段等于改界面，
        /// 属于设计裁决，必须同时改这里。
        /// </summary>
        [Test]
        public void SelectionPage_ShowsAllTenLevelsInLevelNumberOrder()
        {
            var numbers = new List<int>();

            IReadOnlyList<LevelAssetPayload> showcases = LevelAssetLibrary.Levels;
            for (int i = 0; i < showcases.Count; i++)
                numbers.Add(showcases[i].levelNumber);

            IReadOnlyList<WorldMapDefinition> maps = WorldMapCatalog.All;
            for (int i = 0; i < maps.Count; i++)
                numbers.Add(maps[i].LevelNumber);

            numbers.Sort();

            Assert.That(numbers, Is.EqualTo(new[] { 1, 3, 101, 102, 103, 104, 105, 106, 107, 108 }),
                "选关页应列出 2 张手作样板关 + 8 张海图，按关卡号升序");
        }
    }
}
