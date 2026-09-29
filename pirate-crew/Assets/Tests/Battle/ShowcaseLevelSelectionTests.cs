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
    ///
    /// 【零海图口径】八张世界海图已删除待重做、目录为空 ⇒ <c>SetPending</c> 恒失败
    /// （"写海图"这一侧无对象可测）；需要"非空海图"的优先级断言改注入**合成海图**
    /// （<see cref="SyntheticMap"/>，只供解析器纯函数重载用，不碰目录）。
    /// </summary>
    public class ShowcaseLevelSelectionTests
    {
        /// <summary>现存样板关的关卡号：1 云端漫步 / 3 天空之岛 / 4·5 废弃化工厂（关卡 2「碎岛雨」已删除，号段有意不连续）。</summary>
        const int CloudWalk = 1;
        const int SkyIsland = 3;

        /// <summary>已删除的关卡号——用来证明"不存在的样板关号会被拒绝"。</summary>
        const int DeletedLevel = 2;

        /// <summary>合成海图（零海图下的注入对象；内容全空，只给 span 与氛围档）。</summary>
        static WorldMapDefinition SyntheticMap()
        {
            return new WorldMapDefinition(
                id: "synthetic_map", displayName: "合成图", levelNumber: WorldMapCatalog.FirstLevelNumber,
                spanX: 100f, spanZ: 100f, ambientTier: "Noon",
                crewWeapons: null, captainWeapons: null,
                terrain: new List<WorldKitPlacement>(),
                horizon: new List<WorldKitPlacement>(),
                props: new List<WorldPropPlacement>(),
                spawns: new List<WorldMapSpawn>(),
                airdropPool: new List<WeaponStack>(),
                horizonSeed: 0,
                horizonFeatures: new List<string>());
        }

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
            WorldMapDefinition map = SyntheticMap();

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
            WorldMapDefinition map = SyntheticMap();

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
        public void WriteShowcase_ClearsPendingWorldMapSlot()
        {
            // 零海图口径：目录为空 ⇒ SetPending（写海图）恒失败、无对象可测；
            // 这里钉写侧互斥的**另一半**——写样板关必须清掉待战海图槽位，
            // 且读侧在样板待战期间让位（TryGetPending 恒 false）。
            Assert.That(WorldMapRuntime.SetPending("wreck_hymn"), Is.False,
                "零海图下 SetPending 一律失败（目录为空）——这条同时证明「没有待战海图」这一前置");

            Assert.That(WorldMapRuntime.SetPendingShowcase(CloudWalk), Is.True);
            Assert.That(WorldMapRuntime.TryGetPendingShowcase(out int selected), Is.True);
            Assert.That(selected, Is.EqualTo(CloudWalk));
            Assert.That(WorldMapRuntime.TryGetPending(out _), Is.False, "样板待战期间海图槽位必须让位");

            // 再点另一关：槽位被替换（不是叠加），海图槽位仍是空的。
            Assert.That(WorldMapRuntime.SetPendingShowcase(SkyIsland), Is.True);
            Assert.That(WorldMapRuntime.TryGetPendingShowcase(out int next), Is.True);
            Assert.That(next, Is.EqualTo(SkyIsland));
            Assert.That(WorldMapRuntime.TryGetPending(out _), Is.False,
                "写样板关没清掉海图槽位：撤掉样板关后上一局的海图又会活");
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

            // 解析侧同样忽略坏关号：即使有人绕过写侧校验塞进来，也不得把它当成有效样板关
            // （零海图下没有待战海图可让位 ⇒ 落到兜底关 1 并留提示）。
            LevelSource source = LevelSourceResolver.Resolve(0, null, DeletedLevel);
            Assert.That(source.Kind, Is.EqualTo(LevelSourceKind.Showcase));
            Assert.That(source.LevelNumber, Is.EqualTo(LevelSourceResolver.FallbackLevelNumber));
            Assert.That(source.Notice, Is.Not.Null, "坏样板关号被忽略后必须留回落提示");
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
        /// 选关页必须列出全部 4 个现役内容、且按关卡号升序（页面把两份清单拼起来后排序，
        /// 海图目录当前为空 ⇒ 只剩手作样板关 1/3/4/5）。冻结期望值：改号段等于改界面，
        /// 属于设计裁决，必须同时改这里。
        /// </summary>
        [Test]
        public void SelectionPage_ShowsAllShowcaseLevelsInLevelNumberOrder()
        {
            var numbers = new List<int>();

            IReadOnlyList<LevelAssetPayload> showcases = LevelAssetLibrary.Levels;
            for (int i = 0; i < showcases.Count; i++)
                numbers.Add(showcases[i].levelNumber);

            IReadOnlyList<WorldMapDefinition> maps = WorldMapCatalog.All;
            for (int i = 0; i < maps.Count; i++)
                numbers.Add(maps[i].LevelNumber);

            numbers.Sort();

            Assert.That(numbers, Is.EqualTo(new[] { 1, 3, 4, 5 }),
                "选关页应列出 4 张手作样板关（海图目录当前为空），按关卡号升序");
        }
    }
}
