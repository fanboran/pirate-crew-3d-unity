using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using PirateCrew.Battle;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Campaign;
using PirateCrew.CrewManagement;
using PirateCrew.Core;
using PirateCrew.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 主链路 PlayMode E2E：进战斗 → 打完一局 → 结算 → 进度落盘断言。
    ///
    /// 【实际接通的链（逐段读运行时代码核实，出处见各步注释）】
    ///   Bootstrapper（组合根，Start 自动进主菜单）
    ///   → MainMenu（战斗钮 = 发布 SceneEvents.ChangeScene 进选关页，MainMenuController.OnBattleClicked）
    ///   → LevelSelect（样板行 = WorldMapRuntime.SetPendingShowcase + ChangeScene(Battle)，
    ///     LevelSelectController.OnShowcaseClicked）
    ///   → Battle（BattleController.Awake → LevelSourceResolver.Resolve：
    ///     选关页样板关 ② 优先命中；无待战内容时 ④ 回落样板第 1 关，进战斗不强制要求 pending）
    ///   → 杀光蓝队（SubtractHealth 杀队原语）→ BattleController.Update → CheckMatchOver
    ///   → EventBus.MatchFinished → CampaignApi.OnMatchFinished（TrySettle → 发奖励 → SaveProgress）
    ///   → SaveManager.SaveToSlot(1)（CampaignApi.ProgressSlot）。
    ///
    /// 【断点（不为通测试硬改运行时代码，留协调者裁决补口）】
    ///   海图目录当前为空（Assets/Data/WorldMaps/ 只剩空 _golden 目录，101–108 八张海图已删除
    ///   待重做，见 LevelSelectController 类头），而「结算 → 进度落盘」链有两处以
    ///   「海图 id 在 WorldMapCatalog 里」为前提：
    ///     ① 入口：WorldMapRuntime.SetPending / TryGetPending 都需目录命中——
    ///        BattleStarted 时 CampaignApi.OnBattleStarted 以 TryGetPending 决定结算归属；
    ///     ② 记录：CampaignProgress.CompleteLevel / SetStars 有 WorldMapCatalog.TryGet 门卫——
    ///        目录为空时星记录进不了进度表，存档里 campaign_level_stars 只能是空串。
    ///   因此本类两档覆盖：
    ///     · <see cref="MainMenu_LevelSelect_ShowcaseBattle_MatchFinishes"/> —— 全程真实链，
    ///       打到「样板关 MatchFinished」这个当前可达的最深点，并钉住「样板关不结算、
    ///       不落进度」的既有口径（CampaignApi.OnMatchFinished 无待结算海图即返回）；
    ///     · <see cref="BattleSettlement_WritesProgressSlot"/> —— 结算落盘链，用公共 API
    ///       CampaignApi.Manager.SelectMap 桥接①，真实跑 TrySettle → SaveProgress，
    ///       断言槽位文件出现与结算结果；星记录落盘（断点②）待海图回归后补全链断言。
    ///
    /// 【存档隔离（硬约束：绝不污染真实玩家档）】
    ///   SaveManager.SaveRootPath 是公开可注入的存档根（该属性注释明说"测试或特殊平台可注入
    ///   覆盖"，落法①优于备份/恢复 persistentDataPath 文件），Setup 换进进程临时目录、
    ///   TearDown 还原并删除临时目录——全部读写不触碰 Application.persistentDataPath/saves。
    ///   主菜单 Awake 里的 CampaignApi.LoadProgress 也因此只读临时根（空档），不读真实档。
    /// </summary>
    public class MainChainE2ETests
    {
        /// <summary>桥接用的海图归属 id（只作结算归属键；不要求在目录里，见类头断点②）。</summary>
        const string BridgedMapId = "e2e_bridged_map";

        /// <summary>Setup 换入的临时存档根（进程临时目录，TearDown 删除）。</summary>
        string _tempSaveRoot;

        /// <summary>注入前的原存档根（TearDown 还原，保证后续用例/会话不受污染）。</summary>
        string _originalSaveRoot;

        /// <summary>本测试自己订阅 MatchFinished 记下的载荷（null = 尚未发生）。</summary>
        MatchFinishedPayload? _finishedPayload;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // 每条用例从入口场景重开一局（Single 加载 = 场景复位；服务宿主 DontDestroyOnLoad
            // 跨场景存活，Bootstrapper.EnsureInstalled 幂等不会重复建）。装载走「先清残留实例」
            // 的封装——直接 LoadSceneAsync 会撞上 Bootstrapper 的重复实例自毁守卫，自动转场哑火
            //（同会话内已有别的用例装载过 Bootstrapper 时，见 SceneFlowTests.LoadBootstrapperFresh）。
            yield return SceneFlowTests.LoadBootstrapperFresh();
            yield return SceneFlowTests.WaitForService<SceneLoader>();
            yield return SceneFlowTests.WaitForService<SaveManager>();

            // ---- 存档隔离：换临时存档根（先取原值供 TearDown 还原）----
            _originalSaveRoot = SaveManager.Instance.SaveRootPath;
            _tempSaveRoot = Path.Combine(Path.GetTempPath(), "pc3d-e2e-" + Guid.NewGuid().ToString("N"));
            SaveManager.Instance.SaveRootPath = _tempSaveRoot;

            // ---- 静态域复位（公开 API，各自注释定位均为"测试 / 重开档用"）----
            CampaignApi.Reset();             // 退订结算链 + 清结算结果/归属/红队阵亡计数
            CrewManagementApi.Reset();       // 清真实档读进来的名册/经验内存态
            WorldMapRuntime.ClearPendingShowcase();
            CampaignApi.EnsureBootstrapped(); // Reset 退订了 BattleStarted/CrewDied/MatchFinished，重订
            _finishedPayload = null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            EventBus.Unsubscribe(BattleEvents.MatchFinished, OnMatchFinished);

            // 还原真实存档根 + 删临时目录（全部读写只发生在临时根里）
            if (SaveManager.Instance != null)
                SaveManager.Instance.SaveRootPath = _originalSaveRoot;
            if (!string.IsNullOrEmpty(_tempSaveRoot) && Directory.Exists(_tempSaveRoot))
            {
                try
                {
                    Directory.Delete(_tempSaveRoot, true);
                }
                catch (IOException)
                {
                    // 文件被短暂占用时留给系统临时目录自清；不因清理失败判用例失败
                }
            }

            _tempSaveRoot = null;
            _finishedPayload = null;
            yield return null;
        }

        // ------------------------------------------------------------------
        // 用例 1：全 UI 链到样板关打完（当前可达的最深真实点）
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator MainMenu_LevelSelect_ShowcaseBattle_MatchFinishes()
        {
            EventBus.Subscribe(BattleEvents.MatchFinished, OnMatchFinished);

            // Bootstrapper.Start 自动进主菜单
            yield return SceneFlowTests.WaitForScene(SceneNames.MainMenu);
            yield return SceneFlowTests.WaitForTransitionEnd();

            // 主菜单战斗钮的同一条频道（OnBattleClicked 的域调用，遵循"不模拟点击"纪律）
            EventBus.Publish(SceneEvents.ChangeScene, SceneNames.LevelSelect);
            yield return SceneFlowTests.WaitForScene(SceneNames.LevelSelect);
            yield return SceneFlowTests.WaitForTransitionEnd();

            // 选关页必有可出战内容（数据驱动，不写死张数——现役 4 张手作样板关）
            Assert.Greater(LevelAssetLibrary.Levels.Count, 0, "选关页应至少有一张样板关");

            // 选关页样板行的同一动作链（OnShowcaseClicked 的域调用，不模拟点击）
            int levelNumber = LevelAssetLibrary.Levels[0].levelNumber;
            Assert.IsTrue(WorldMapRuntime.SetPendingShowcase(levelNumber), "样板关应能设定为待战");
            EventBus.Publish(SceneEvents.ChangeScene, SceneNames.Battle);

            yield return SceneFlowTests.WaitForScene(SceneNames.Battle);
            yield return SceneFlowTests.WaitForTransitionEnd();

            BattleController controller = UnityEngine.Object.FindObjectOfType<BattleController>();
            Assert.IsNotNull(controller, "Battle 场景应有 BattleController");
            var turnManager = UnityEngine.Object.FindObjectOfType<TurnManager>();
            Assert.IsNotNull(turnManager, "Battle 场景应有 TurnManager");
            Assert.IsTrue(turnManager.Started, "TurnManager 应已开始第一回合");

            // 本局内容路由：选关页点选的样板关（LevelSourceResolver ② 优先级），不是海图
            Assert.IsFalse(controller.IsWorldMapActive, "样板关入口不应激活海图模式");
            Assert.AreEqual(levelNumber, controller.LevelNumber, "应加载点选的那一关");

            yield return KillTeamOne(controller);

            // BattleController.Update → CheckMatchOver → MatchFinished
            //（CampaignApi 的监听先于本测试执行——订阅顺序即投递顺序）
            yield return WaitForTrue(() => _finishedPayload != null, "等待 MatchFinished 超时");
            Assert.AreEqual(CampaignManager.PlayerWinOutcome, _finishedPayload.Value.Outcome,
                "杀光 AI 队应判玩家胜（TurnRules.ComputeOutcome，Team0Win = 0）");

            // 样板关不结算、不落进度：BattleStarted 时无待战海图（TryGetPending 被样板待战挡住），
            // MatchFinished 时 CampaignApi.OnMatchFinished 直接返回——这是设计口径，钉住它
            Assert.IsNull(CampaignApi.LastSettlement, "样板关没有待结算海图，不应产生结算");
            Assert.IsFalse(SaveManager.Instance.SlotExists(CampaignApi.ProgressSlot), "样板关不应写进度槽");
        }

        // ------------------------------------------------------------------
        // 用例 2：结算 → 进度落盘（桥接说明见方法内注释与类头断点①②）
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator BattleSettlement_WritesProgressSlot()
        {
            EventBus.Subscribe(BattleEvents.MatchFinished, OnMatchFinished);

            yield return SceneFlowTests.WaitForScene(SceneNames.MainMenu);
            yield return SceneFlowTests.WaitForTransitionEnd();

            // 直接进战斗（ChangeScene 频道 = 选关页行点击成功后发出的同一条）。
            // 真实"海图行"在此之前还会 WorldMapRuntime.SetPending(mapId)——海图目录为空走不通
            //（类头断点①），本局因此落 LevelSourceResolver ④ 兜底关（与直接 Play 同路径）。
            EventBus.Publish(SceneEvents.ChangeScene, SceneNames.Battle);
            yield return SceneFlowTests.WaitForScene(SceneNames.Battle);
            yield return SceneFlowTests.WaitForTransitionEnd();

            BattleController controller = UnityEngine.Object.FindObjectOfType<BattleController>();
            Assert.IsNotNull(controller, "Battle 场景应有 BattleController");
            var turnManager = UnityEngine.Object.FindObjectOfType<TurnManager>();
            Assert.IsNotNull(turnManager, "Battle 场景应有 TurnManager");
            Assert.IsTrue(turnManager.Started, "TurnManager 应已开始第一回合");
            Assert.IsFalse(controller.IsWorldMapActive, "无待战海图应回落样板关（LevelSourceResolver ④）");

            // 【桥接】结算归属：真实链路 = 海图行 SetPending → BattleStarted →
            // CampaignApi.OnBattleStarted 走 TryGetPending 命中分支调 Manager.SelectMap(map.Id)。
            // 目录为空时那一支不可达，这里用同一公共 API 在战斗开始后补记归属，
            // 让 MatchFinished 之后的真实结算链（TrySettle → 发奖励 → SaveProgress）原样跑通。
            CampaignApi.Manager.SelectMap(BridgedMapId);

            yield return KillTeamOne(controller);

            // MatchFinished → CampaignApi.OnMatchFinished → TrySettle → SaveProgress（同步完成）
            yield return WaitForTrue(() => _finishedPayload != null, "等待 MatchFinished 超时");
            Assert.AreEqual(CampaignManager.PlayerWinOutcome, _finishedPayload.Value.Outcome,
                "杀光 AI 队应判玩家胜");

            // ---- 结算结果（TrySettle 的产物，CampaignApi.LastSettlement）----
            Assert.IsFalse(CampaignApi.HasPendingMap, "结算后应清空待结算归属");
            Assert.IsTrue(CampaignApi.LastSettlement != null, "结算应已发生");
            CampaignSettlement settlement = CampaignApi.LastSettlement.Value;
            Assert.AreEqual(BridgedMapId, settlement.MapId, "结算归属应为桥接补记的海图 id");
            Assert.IsTrue(settlement.Cleared, "玩家胜应记为通关");
            Assert.IsTrue(settlement.FirstClear, "新档首胜应为首次通关");
            Assert.GreaterOrEqual(settlement.Stars, 2,
                "通关 + 阵亡≤1 至少 2 星（StarRules.Evaluate 提案口径；给出生物理滑移留一档容差）");

            // ---- 进度槽落盘断言（临时存档根内）----
            Assert.IsTrue(SaveManager.Instance.SlotExists(CampaignApi.ProgressSlot),
                "进度槽文件应已写入临时存档根（SaveProgress → SaveToSlot(1)）");
            SaveData saved = SaveManager.Instance.LoadFromSlot(CampaignApi.ProgressSlot);
            Assert.IsNotNull(saved, "进度槽应能读回（版本门卫/迁移管线不拒绝本进程写的档）");
            Assert.IsTrue(saved.HasData(CampaignSaveCodec.StarsKey), "战役进度键应已写入（CampaignApi.WriteTo）");
            Assert.IsTrue(saved.HasData(CrewManagementSaveCodec.UnlockedKey),
                "船员名册键应已写入（CrewManagementApi.WriteTo）");

            // 【断点镜像】Progress.CompleteLevel/SetStars 有 WorldMapCatalog 门卫（类头断点②）：
            // 目录为空 ⇒ 星记录进不了进度表，存档里星键是空串而非 "id:3"。
            // 海图资产回归后，把本用例的桥接换成真实 SetPending(mapId)，此处即可改断
            // "含本次关卡记录"（CampaignSaveCodec.Read 读回 GetStars(mapId) == 结算星数）。
            Assert.AreEqual(string.Empty, saved.GetData(CampaignSaveCodec.StarsKey, "<缺键>"),
                "海图目录为空时星记录无法落盘（CompleteLevel 门卫）——空串即断点在存档里的镜像");
        }

        // ------------------------------------------------------------------
        // 原语
        // ------------------------------------------------------------------

        /// <summary>MatchFinished 记录器（ CampaignApi 的监听先注册，先于本方法执行）。</summary>
        void OnMatchFinished(MatchFinishedPayload payload)
        {
            _finishedPayload = payload;
        }

        /// <summary>
        /// 杀队原语（同 BattleSmokeTests 的 SubtractHealth 路径）：逐个扣掉当前全部血量。
        /// 死亡即广播 CrewDied——红队阵亡才计入星级惩罚（CampaignApi.OnCrewDied），
        /// 本原语只杀蓝队（AI），不产生红队阵亡。
        /// </summary>
        static IEnumerator KillTeamOne(BattleController controller)
        {
            BattleTeam team = controller.GetTeam(1);
            Assert.IsNotNull(team, "蓝队（AI）应存在");
            Assert.Greater(team.Characters.Count, 0, "蓝队应有成员");

            for (int i = 0; i < team.Characters.Count; i++)
            {
                PirateBase pirate = team.Characters[i];
                if (pirate == null || !pirate.Alive)
                    continue;

                Assert.Greater(pirate.Health, 0, "存活角色血量应大于 0");
                pirate.SubtractHealth(pirate.Health);
                Assert.IsFalse(pirate.Alive, "满血扣除后角色应死亡");
            }

            Assert.IsFalse(team.AnyAlive, "杀队后该队应无存活成员");
            yield return null;
        }

        /// <summary>条件轮询（每帧一查，deadline 到点即失败）——同 BattleSceneWiringTests 的超时风格。</summary>
        static IEnumerator WaitForTrue(Func<bool> condition, string timeoutMessage)
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail(timeoutMessage);
                yield return null;
            }
        }
    }
}
