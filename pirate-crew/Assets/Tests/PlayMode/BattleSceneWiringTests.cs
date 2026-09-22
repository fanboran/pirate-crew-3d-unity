using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PirateCrew.Core;
using PirateCrew.Battle;
using PirateCrew.Data;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PirateCrew.Tests
{
    /// <summary>
    /// Battle 场景装配完整性 PlayMode 测试（可选交付物，只能由协调者在 Unity 里跑）。
    ///
    /// 【验证闭环】对应 <c>BattleSceneSetup</c> 的装配结果：
    ///   · 关键 <c>[SerializeField]</c> 引用非空（BattleController / TurnManager /
    ///     AimThrowController / TrajectoryPreview / BattleCameraDriver / BattleHud）；
    ///   · 双方船员数量 = 样板第 1 关（云端漫步）数据（红 4 / 蓝 4）；
    ///   · 单位站位落在 XZ 竞技场（<see cref="LevelGeometry.GridToArena"/>，脚底贴地、枢轴抬高）；
    ///   · 水面世界 Y = <see cref="LevelGeometry.WaterSurfaceY"/>（3D 化的全局水位常量）；
    ///   · 相机是真 3D：**烘焙机位**透视 + 45° 俯角 + 距离 15（正交侧视是 2D 时代的遗留），
    ///     **运行时默认**被 BattleCameraDriver 覆盖为角色特写档（用户裁决 2026-09-14：距离 5–7、俯角 25–35°）；
    ///   · TurnManager 已开始回合，且 end go 后能推进到另一队。
    ///
    /// 【说明】私有序列化字段用反射读取，避免为了测试扩大运行时 API；字段名与装配脚本
    ///         <c>SerializedObject.FindProperty(...)</c> 使用的名字一一对应。
    /// </summary>
    public class BattleSceneWiringTests
    {
        static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        static object Field(object target, string name)
        {
            Assert.IsNotNull(target, "读取字段 " + name + " 时目标对象为空");
            FieldInfo field = target.GetType().GetField(name, PrivateInstance);
            Assert.IsNotNull(field, target.GetType().Name + "." + name + " 字段不存在（装配脚本字段名漂移？）");
            return field.GetValue(target);
        }

        /// <summary>
        /// 复核相机是**等距像素卡通口径**（正交 + 斜 45° 方位 + 俯角 30° + 基准 OrthoSize 档），
        /// 且已**去 Cinemachine 化**（2026-09-23 重构）：主相机无 Brain、场景无虚机/中转目标、
        /// Driver 是唯一写入者。
        ///
        /// 【单一写入者守卫】跑几帧后主相机的实况 transform/lens 必须逐位等于
        /// <see cref="BattleCameraDriver.LastFrame"/>——若有别的系统在 Driver 之后改写相机，
        /// 两者会分叉，这条当场红。
        /// </summary>
        static IEnumerator AssertBattleCameraDriverWired(BattleCameraDriver driver)
        {
            Assert.IsNotNull(driver, "BattleCameraDriver 为空");
            Camera mainCamera = (Camera)Field(driver, "mainCamera");
            Assert.IsNotNull(mainCamera, "BattleCameraDriver.mainCamera 未接线");

            // ---- 去 Cinemachine 化（按类型名查，不给 PlayModeTests 引入 Cinemachine 程序集依赖）----
            Component brain = mainCamera.GetComponent("CinemachineBrain");
            Assert.IsNull(brain, "主相机不应再挂 CinemachineBrain（2026-09-23 相机重构已退役它）");
            Assert.IsNull(GameObject.Find("BattleVCam"), "场景不应再有 BattleVCam（虚机已退役）");
            Assert.IsNull(GameObject.Find("CameraTarget"), "场景不应再有 CameraTarget（Cinemachine 中转件已退役）");

            // ---- 正交口径 + 裁剪面（原链 Brain 每帧推送 0.1/200 → 现 Driver Awake 写一次同值）----
            Assert.IsTrue(mainCamera.orthographic, "相机应为正交（等距像素卡通口径）");
            Assert.AreEqual(CameraFraming.OrthoNearClip, mainCamera.nearClipPlane, 1e-3f,
                "近裁剪面应等于 CameraFraming.OrthoNearClip（原虚机 Lens 的实机值）");
            Assert.AreEqual(CameraFraming.OrthoFarClip, mainCamera.farClipPlane, 0.5f,
                "远裁剪面应等于 CameraFraming.OrthoFarClip（主相机烘焙的 400 运行期被覆盖成 200）");

            // ---- 运行时默认 = 基准机位（唯一取景档：size 7 = 可见 14 m，创始人 2026-09-23 裁决）----
            Assert.AreEqual(CameraFraming.CloseUpOrthoSize, driver.RuntimeOrthoSize,
                "运行时取景应恒为基准档（无滚轮缩放）");

            // ---- 单一写入者：让游戏跑几帧，再逐位比对 ----
            yield return null;
            yield return null;
            yield return null;
            Assert.Greater(driver.LastFrame.OrthoSize, 0f, "Driver 应已落过至少一帧取景");
            float positionError = Vector3.Distance(mainCamera.transform.position, driver.LastFrame.Position);
            float rotationError = Quaternion.Angle(mainCamera.transform.rotation, driver.LastFrame.Rotation);
            float sizeError = Mathf.Abs(mainCamera.orthographicSize - driver.LastFrame.OrthoSize);
            Assert.Less(positionError, 0.01f,
                "主相机位置 ≠ Driver.LastFrame —— 除 Driver 外有人在写主相机（误差 " + positionError + "）");
            Assert.Less(rotationError, 0.01f,
                "主相机朝向 ≠ Driver.LastFrame —— 除 Driver 外有人在写主相机（误差 " + rotationError + "°）");
            Assert.Less(sizeError, 0.001f,
                "主相机 OrthoSize ≠ Driver.LastFrame —— 除 Driver 外有人在写主相机");
        }

        [UnityTest]
        public IEnumerator BattleCamera_HasPixelartPathWired()
        {
            yield return SceneManager.LoadSceneAsync(SceneNames.Battle);
            yield return null;
            yield return null;

            // ---- 像素化路径接线（创始人 2026-09-22：「以后走新管线」）----
            // 断的是"装配有没有真的发生"，不是数值：索引由装配器从 URP 资产里查出来，
            // 写死数字会在别人的机器上错位，所以只要求"解出来了"（-1 = 没解出来）。
            var rig = Object.FindObjectOfType<PixelartCameraRig>();
            Assert.IsNotNull(rig, "Battle 场景的主相机应挂 PixelartCameraRig（否则游戏还在旧管线上）");

            Assert.GreaterOrEqual(rig.castRendererIndex, 0,
                "Cast 渲染器索引没解出来（-1）——像素化路径的物体/着色 pass 不会跑");
            Assert.GreaterOrEqual(rig.screenRendererIndex, 0,
                "Screen 渲染器索引没解出来（-1）——上屏 blit 不会跑，画面会停在上屏前");
            Assert.GreaterOrEqual(rig.overlayRendererIndex, 0,
                "透明件叠加渲染器索引没解出来（-1）——FX / 危险虚线 / 接触阴影 / 弹道预览会整类看不见");
            Assert.AreNotEqual(rig.castRendererIndex, rig.screenRendererIndex,
                "Cast 与 Screen 必须是两个不同的渲染器（挂成同一个会让主相机重跑物体 pass 并冲掉结果）");

            Assert.IsFalse(rig.deriveOrthographicSize,
                "deriveOrthographicSize 必须关：取景归 BattleCameraDriver（正交整数档 = 视野档），"
                + "本路径只管像素网格与着色，开着会把玩家的缩放覆盖掉");
            Assert.AreEqual(PixelartPilotScene.PixelScale, rig.pixelScale,
                "像素档位必须等于出图口径 PixelartPilotScene.PixelScale（两边同一个艺术像素网格）");

            var converter = Object.FindObjectOfType<PixelartContentConverter>();
            Assert.IsNotNull(converter,
                "Battle 场景应有 PixelartContentConverter（否则旧链材质在新管线下不画：不是黑，是消失）");
        }

        [UnityTest]
        public IEnumerator BattleScene_IsFullyWired()
        {
            yield return SceneManager.LoadSceneAsync(SceneNames.Battle);
            yield return null;
            yield return null;

            BattleController controller = Object.FindObjectOfType<BattleController>();
            Assert.IsNotNull(controller, "Battle 场景应有 BattleController");

            // ---- 关键序列化引用非空 ----
            Assert.IsNotNull(Field(controller, "piratePrefab"), "BattleController.piratePrefab 未接线");
            Assert.IsNotNull(Field(controller, "team0Root"), "BattleController.team0Root 未接线");
            Assert.IsNotNull(Field(controller, "team1Root"), "BattleController.team1Root 未接线");
            Assert.IsNotNull(Field(controller, "waterPlane"), "BattleController.waterPlane 未接线");
            Assert.IsNotNull(Field(controller, "turnManager"), "BattleController.turnManager 未接线");
            Assert.IsNotNull(Field(controller, "aimController"), "BattleController.aimController 未接线");
            Assert.IsNotNull(Field(controller, "battleCamera"), "BattleController.battleCamera 未接线");

            var turnManager = Object.FindObjectOfType<TurnManager>();
            Assert.IsNotNull(turnManager, "Battle 场景应有 TurnManager");
            Assert.IsNotNull(Field(turnManager, "battle"), "TurnManager.battle 未接线");

            var aim = Object.FindObjectOfType<AimThrowController>();
            Assert.IsNotNull(aim, "Battle 场景应有 AimThrowController");
            Assert.IsNotNull(Field(aim, "battleCamera"), "AimThrowController.battleCamera 未接线");
            Assert.IsNotNull(Field(aim, "battle"), "AimThrowController.battle 未接线");
            Assert.IsNotNull(Field(aim, "trajectory"), "AimThrowController.trajectory 未接线");

            var camDriver = Object.FindObjectOfType<BattleCameraDriver>();
            Assert.IsNotNull(camDriver, "Battle 场景应有 BattleCameraDriver");
            Assert.IsNotNull(Field(camDriver, "mainCamera"), "BattleCameraDriver.mainCamera 未接线");

            // ---- 运行期查找清退后的显式接线（2026-09-21「接线清退」轨道）----
            // 这三条原来是运行时的 FindObjectOfType 兜底（BattleCameraDriver 甚至每帧扫），
            // 现改为 [SerializeField] 显式注入 + Awake 一次性兜底。这里断言"确实来自装配接线"
            // （WiredByAssembly），而不是"兜底也能跑"——兜底是给旧场景的降级通道，不是合格标准。
            // 修复命令：PirateCrew.EditorTools.BattleLookupWiring.Wire（会写 Battle.unity）。
            Assert.IsNotNull(Field(camDriver, "aimThrow"),
                "BattleCameraDriver.aimThrow 未接线（跑 BattleLookupWiring.Wire）");
            Assert.IsTrue(camDriver.AimThrowWiredByAssembly,
                "BattleCameraDriver.aimThrow 未经装配接线，只在跑一次性兜底"
                + "（跑 PirateCrew.EditorTools.BattleLookupWiring.Wire 写入 Battle.unity）");

            Assert.IsNotNull(Object.FindObjectOfType<TrajectoryPreview>(), "Battle 场景应有 TrajectoryPreview");

            // ---- 相机：等距像素卡通 + 去 Cinemachine 化 + Driver 唯一写入者（含跑帧比对）----
            // 【放在单位站位断言之前】相机守卫是本测试的相机域结论，不应被
            // 单位出生物理滑移这类场景内容/物理的环境性失败挡在后面跑不到（2026-09-23）。
            yield return AssertBattleCameraDriverWired(camDriver);

            // ---- 关卡数据驱动的出征数量一致 ----
            BattlePlan plan = controller.Plan;
            Assert.IsNotNull(plan, "BattleController 应生成出战计划");
            Assert.Greater(plan.Entries.Count, 0, "出战计划不应为空");
            Assert.AreEqual(plan.Entries.Count, controller.AllPirates.Count, "实例化角色数应等于出战条目数");
            Assert.AreEqual(plan.CountForTeam(0), controller.GetTeam(0).Characters.Count, "红队人数应与关卡数据一致");
            Assert.AreEqual(plan.CountForTeam(1), controller.GetTeam(1).Characters.Count, "蓝队人数应与关卡数据一致");
            Assert.Greater(plan.CountForTeam(0), 0, "红队应有成员");
            Assert.Greater(plan.CountForTeam(1), 0, "蓝队应有成员");

            // ---- 单位站位：XZ 竞技场（横向 X = gridX+0.5，纵深 Z = gridY+0.5，高度 = 枢轴离地）----
            var pirates = controller.AllPirates;
            for (int i = 0; i < plan.Entries.Count; i++)
            {
                SpawnPlanEntry entry = plan.Entries[i];
                Vector3 expectedPos = LevelGeometry.GridToArena(entry.GridX, entry.GridY);
                Assert.AreEqual(expectedPos.x, entry.WorldPosition.x, 1e-4f, "计划条目的 X 应 = gridX+0.5");
                Assert.AreEqual(expectedPos.z, entry.WorldPosition.z, 1e-4f, "计划条目的 Z 应 = gridY+0.5");

                Vector3 actual = pirates[i].transform.position;
                Assert.AreEqual(expectedPos.x, actual.x, 1e-3f, "单位 " + i + " 的横向 X 应落在 XZ 竞技场上");
                Assert.AreEqual(expectedPos.z, actual.z, 1e-3f, "单位 " + i + " 的纵深 Z 应落在 XZ 竞技场上");
                // 高度只校验"在地面之上"：站位 y = UnitPivotHeight，重力/碰撞在 1~2 帧内可能微调。
                Assert.Greater(actual.y, LevelGeometry.GroundTopY - 0.01f, "单位 " + i + " 不应沉到地面之下");
            }

            // ---- 水位：3D 化后是全局常量 WaterSurfaceY（不再是关卡 waterTileY 推出）----
            var waterPlane = (Transform)Field(controller, "waterPlane");
            Assert.AreEqual(LevelGeometry.WaterSurfaceY, controller.WaterWorldY, 1e-4f, "计划水位应为 WaterSurfaceY");
            Assert.AreEqual(controller.WaterWorldY, waterPlane.position.y, 1e-3f, "水面 y 应等于计划水位");

            // ---- HUD 接线 ----
            var hud = Object.FindObjectOfType<BattleHud>();
            Assert.IsNotNull(hud, "Battle 场景应有 BattleHud");
            Assert.IsTrue(hud.HasCoreReferences, "BattleHud 核心引用未接线");
            Assert.IsNotNull(Field(hud, "cameraController"),
                "BattleHud.cameraController 未接线（跑 BattleLookupWiring.Wire）");
            Assert.IsTrue(hud.CameraControllerWiredByAssembly,
                "BattleHud.cameraController 未经装配接线，只在跑一次性兜底"
                + "（跑 PirateCrew.EditorTools.BattleLookupWiring.Wire 写入 Battle.unity）");

            // 水面太阳方向：第三档"扫全场找最亮平行光"已退役，来源改为
            // RenderSettings.sun（AmbientDirector 运行时登记）→ 装配注入的 sunLight → 静态正午方向。
            var waterDriver = Object.FindObjectOfType<PirateCrew.Water.WaterSimulationDriver>();
            Assert.IsNotNull(waterDriver, "Battle 场景应有 WaterSimulationDriver（水面波动模拟）");
            Assert.IsNotNull(Field(waterDriver, "sunLight"),
                "WaterSimulationDriver.sunLight 未接线（跑 BattleLookupWiring.Wire）");

            // 特效层：FxRoot 由组合根在 BeforeSceneLoad 建立（进程级常驻，无场景可接线），
            // 它唯一的查找白名单条目是"每局一次地拿 BattleController"——这里断言那一发**真的命中了**，
            // 否则 crew_damaged/crew_died 的按 id 定位会静默降级。
            PirateCrew.Fx.FxRoot fx = PirateCrew.Fx.FxRoot.Instance;
            Assert.IsNotNull(fx, "组合根未建立 [FxRoot]（FxBootstrap.Install 未跑？）");
            Assert.IsNotNull(Field(fx, "_battle"),
                "FxRoot 未解析到战场根（BattleController）——单位注册表与弹体拖尾会本局缺席");
            Assert.AreEqual(17, hud.WeaponSlotCount, "HUD 应有 17 个武器槽（§5.2）");
            Assert.IsTrue(hud.HasWeaponWiring, "HUD 武器图标格未全部接线");
            Assert.IsTrue(hud.HasTeamBarWiring, "HUD 双队血条（段+pips）未全部接线");
            Assert.IsTrue(hud.HasModeWiring, "HUD 模式图标钮未全部接线");

            // ---- 小地图（c78fdea 复盘：BuildAll 单独重存曾把 BattleMinimap 组件整颗洗掉，
            //      海图空白静默三轮——组件在 + 接线在必须成为门禁）----
            var minimap = Object.FindObjectOfType<BattleMinimap>();
            Assert.IsNotNull(minimap, "Battle 场景应有 BattleMinimap 组件（WireMinimap 未跑？）");
            Assert.IsTrue(minimap.HasMinimapWiring, "小地图接线不完整（dotLayer/unitRoots）");

            // ---- 回合开始与推进 ----
            Assert.IsTrue(turnManager.Started, "TurnManager 应已开始第一回合");
            Assert.IsNotNull(turnManager.CurrentTeam, "当前队不应为空");

            BattleTeam team = turnManager.CurrentTeam;
            PirateBase actor = team.FirstAlive();
            Assert.IsNotNull(actor, "当前队应有存活角色");
            team.Select(actor);
            actor.MarkEndGo();

            int before = team.Number;
            float deadline = Time.realtimeSinceStartup + 3f;
            while (turnManager.CurrentTeam != null && turnManager.CurrentTeam.Number == before
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreNotEqual(before, turnManager.CurrentTeam.Number, "inactivity > 10 后回合应推进到另一队");
        }
    }
}
