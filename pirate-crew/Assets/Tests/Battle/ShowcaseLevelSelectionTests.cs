using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PirateCrew.ArtReview;
using PirateCrew.Battle.Levels;
using PirateCrew.Data;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 「选关页点关卡进图」这条通道的门禁（无头可跑）：待战关卡
    /// （<see cref="PendingLevel.SetPending"/>）在解析优先级里排在出图覆盖之后、兜底关 1 之前。
    ///
    /// 【为什么单独钉它】待战槽是静态字段，写侧漏清就会出现"点了这关却进了上一局那关"
    /// 这类跨局串台——静态残留是本仓最难查的故障。所以这里既断言注入式纯函数的优先级，
    /// 也断言静态槽位的替换、坏关号的拒绝与复位链。
    /// </summary>
    public class ShowcaseLevelSelectionTests
    {
        /// <summary>现役关卡号：1 云端漫步 / 2 天空之岛 / 3·4 废弃化工厂。</summary>
        const int CloudWalk = 1;
        const int SkyIsland = 2;

        /// <summary>当前无对应资产的关卡号——用来证明"不存在的关卡号会被拒绝"。</summary>
        const int MissingLevel = 9;

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

        /// <summary>待战槽与出图覆盖都是静态字段——测试之间必须逐个清干净。</summary>
        static void ClearStaticSlots()
        {
            PendingLevel.ClearPending();
            ArtReviewCaptureOverride.LevelNumber = 0;
        }

        // ------------------------------------------------------------------
        // 优先级
        // ------------------------------------------------------------------

        [Test]
        public void PendingLevel_ResolvesToThatLevel()
        {
            // Act
            LevelSource source = LevelSourceResolver.Resolve(0, SkyIsland);

            // Assert
            Assert.That(source, Is.Not.Null);
            Assert.That(source.LevelNumber, Is.EqualTo(SkyIsland));
            Assert.That(source.Notice, Is.Null, "选关页点选是正常路径，不该留回落告警");
        }

        [Test]
        public void ArtReviewOverride_BeatsPendingLevel()
        {
            // Arrange：出图覆盖是工具专用通道，出现即最高优先。两级关卡号取不同的值
            // （覆盖 = 关卡 2 / 待战 = 关卡 1），否则分不清是谁赢的。
            // Act
            LevelSource source = LevelSourceResolver.Resolve(SkyIsland, CloudWalk);

            // Assert
            Assert.That(source, Is.Not.Null);
            Assert.That(source.LevelNumber, Is.EqualTo(SkyIsland), "出图覆盖必须压过选关页点选的关卡");
            Assert.That(source.Notice, Is.Null, "覆盖命中时不该有回落告警");
        }

        /// <summary>
        /// 静态状态 → <see cref="LevelSourceResolver.Resolve()"/> 的接线（注入式重载测不到这一段）：
        /// 出图覆盖在场时压过待战关卡，覆盖撤掉后轮到待战关卡。
        /// </summary>
        [Test]
        public void Resolve_ReadsPendingLevelFromStaticState()
        {
            // Arrange
            Assert.That(PendingLevel.SetPending(SkyIsland), Is.True);
            ArtReviewCaptureOverride.LevelNumber = CloudWalk;

            // Act / Assert：① 覆盖赢
            LevelSource overridden = LevelSourceResolver.Resolve();
            Assert.That(overridden, Is.Not.Null);
            Assert.That(overridden.LevelNumber, Is.EqualTo(CloudWalk));

            // ② 撤掉覆盖 → 待战关卡（这同时证明 Resolve() 把待战通道真读了出来）
            ArtReviewCaptureOverride.LevelNumber = 0;
            LevelSource selected = LevelSourceResolver.Resolve();
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected.LevelNumber, Is.EqualTo(SkyIsland));
        }

        // ------------------------------------------------------------------
        // 静态槽位的替换与复位
        // ------------------------------------------------------------------

        [Test]
        public void SetPending_ReplacesSlot()
        {
            Assert.That(PendingLevel.SetPending(CloudWalk), Is.True);
            Assert.That(PendingLevel.TryGetPending(out int selected), Is.True);
            Assert.That(selected, Is.EqualTo(CloudWalk));

            // 再点另一关：槽位被替换（不是叠加）。
            Assert.That(PendingLevel.SetPending(SkyIsland), Is.True);
            Assert.That(PendingLevel.TryGetPending(out int next), Is.True);
            Assert.That(next, Is.EqualTo(SkyIsland));
        }

        [Test]
        public void SetPending_RejectsLevelNumberWithoutAsset()
        {
            // Arrange：先放一个有效的待战关卡，用来验证"拒绝时也不改动现状"。
            Assert.That(PendingLevel.SetPending(CloudWalk), Is.True);

            // Act / Assert：无资产的关卡号、以及 0 / 负数（表示"无"）都不得被接受。
            Assert.That(PendingLevel.SetPending(MissingLevel), Is.False,
                "关卡 " + MissingLevel + " 没有对应资产，不该被接受");
            Assert.That(PendingLevel.SetPending(0), Is.False);
            Assert.That(PendingLevel.SetPending(-1), Is.False);

            // 现状不变（接受坏关号会顺手清空待战内容，把这一局变成兜底关 1）
            Assert.That(PendingLevel.TryGetPending(out int levelNumber), Is.True);
            Assert.That(levelNumber, Is.EqualTo(CloudWalk));

            // 解析侧同样忽略坏关号：即使有人绕过写侧校验塞进来，也不得把它当成有效关卡
            // （落到兜底关 1 并留提示）。
            LevelSource source = LevelSourceResolver.Resolve(0, MissingLevel);
            Assert.That(source.LevelNumber, Is.EqualTo(LevelSourceResolver.FallbackLevelNumber));
            Assert.That(source.Notice, Is.Not.Null, "坏关卡号被忽略后必须留回落提示");
        }

        /// <summary>
        /// 复位链：<c>PendingLevel.ResetStatics</c>（进入播放前唯一入口调用）必须把待战槽清掉。
        /// 它是 <c>internal</c>（测试程序集没有 InternalsVisibleTo），所以用反射调——
        /// 它只清缓存、不碰原生对象，无头安全（与 GameBootstrapTests 刻意不碰 Initialize/ResetStatics
        /// 那两个真正碰 ECall 的阶段不同）。
        /// </summary>
        [Test]
        public void ResetStatics_ClearsPendingLevel()
        {
            // Arrange
            MethodInfo reset = typeof(PendingLevel).GetMethod(
                "ResetStatics", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(reset, Is.Not.Null, "PendingLevel.ResetStatics 被改名/删除了（复位链断了）");
            Assert.That(PendingLevel.SetPending(SkyIsland), Is.True);

            // Act
            reset.Invoke(null, null);

            // Assert
            Assert.That(PendingLevel.TryGetPending(out _), Is.False,
                "复位漏了待战槽 = 下一局的进图内容会串台");
        }

        // ------------------------------------------------------------------
        // 选关列表的内容契约（列表 = 关卡资产清单）
        // ------------------------------------------------------------------

        /// <summary>
        /// 选关页必须列出全部 4 个现役关卡、且按关卡号升序。冻结期望值：改号段等于改界面，
        /// 属于设计裁决，必须同时改这里。
        /// </summary>
        [Test]
        public void SelectionPage_ShowsAllLevelsInLevelNumberOrder()
        {
            var numbers = new List<int>();

            IReadOnlyList<LevelAssetPayload> levels = LevelAssetLibrary.Levels;
            for (int i = 0; i < levels.Count; i++)
                numbers.Add(levels[i].levelNumber);

            numbers.Sort();

            Assert.That(numbers, Is.EqualTo(new[] { 1, 2, 3, 4 }),
                "选关页应列出 4 张现役关卡，按关卡号升序");
        }
    }
}
