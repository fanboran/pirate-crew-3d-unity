using System.Collections;
using System.IO;
using PirateCrew.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateCrew.ArtReview
{
    /// <summary>
    /// 独立播放器的自动评审出图：协调者在命令行传 <c>-artReviewOut &lt;绝对目录&gt;</c> 启动播放器，
    /// 本组件直接加载 Battle 场景、等战斗就绪后按机位预设逐张截图落盘，完毕自动退出。
    ///
    /// 【为什么要有它】美术调参必须"看着画面调"。编辑器出图依赖人工点菜单，
    /// 而协调者可以无头构建播放器并自己启动它——由此获得不依赖任何人的视觉迭代闭环：
    /// 改参数 → 重建 → 跑播放器出图 → 读图评审 → 再改。
    ///
    /// 【零侵入】不带参数启动（正常游玩/测试）时本组件在 Awake 即自毁，无任何开销。
    /// 查找场景节点用的是根对象逐层按名匹配（仅本调试组件允许，运行时代码仍禁 GameObject.Find）。
    ///
    /// 【机位镜像铁律】<see cref="BuildShots"/> 是编辑器侧 <c>PlayerArtCapture</c> 的机位清单
    /// （Editor 程序集不能被运行时引用），**逐机位手抄**自 <c>Assets/Editor/ArtReviewShots.cs</c>。
    /// 改任一侧的 Offset / Fov / 瞄准点 / 顺序，都必须同步另一侧，否则出厂机位与编辑器评审图会对不上
    /// （教训：r3 出厂 battle-45/hud-fullscreen 的 Offset 仍停在旧距离 18 的 (0,12.73,12.73)，
    /// 而游戏相机已改距离 15，导致出厂机位单位只有 21px）。
    /// </summary>
    public sealed class PlayerArtCapture : MonoBehaviour
    {
        struct Shot
        {
            public string Name;
            public bool HudVisible;
            public Vector3 Position;
            public Quaternion Rotation;
            public float Fov;
        }

        const int Width = 1920;
        const int Height = 1080;
        const float ReadyTimeoutSeconds = 60f;

        /// <summary>爆炸机位名（与 <c>ArtReviewShots</c> 的 slug 保持一致）。</summary>
        const string ExplosionShotName = "explosion-moment";

        /// <summary>单角色特写机位名（与 <c>ArtReviewShots</c> 的 slug 保持一致）。</summary>
        const string UnitCloseupShotName = "unit-closeup";

        /// <summary>武器面板机位（HUD 样板验收）：选中单位让面板滑入后截图，拍完不清（特写接着用同一选中）。</summary>
        const string WeaponPanelShotName = "hud-weaponpanel";

        /// <summary>
        /// 纯 HUD 演示机位（空白背景）：相机 cullingMask=0 + 纯色清屏，画面只剩 HUD 本体——
        /// 复杂 3D 场景会把 HUD 淹没，观感验收要在"空白窗口"里看（用户 2026-09-19 裁决）。
        /// armed 版带武器面板（复用特写选中）。
        /// </summary>
        const string ShowcaseShotName = "hud-showcase";

        const string ShowcaseArmedShotName = "hud-showcase-armed";

        /// <summary>演示底色（中亮蓝灰）：深底 HUD 与红蓝队色在其上对比最清晰。</summary>
        static readonly Color ShowcaseBackdrop = new Color(0x8E / 255f, 0x97 / 255f, 0xA6 / 255f, 1f);

        /// <summary>
        /// 主动引爆用的爆炸 size：取 160（`WeaponCatalog.cs` banana / parachuteBomb 档），
        /// 比 cannonball 基准 100（`CannonRules.cs:45`）大一档，纯为评审画面里火光可辨——
        /// size→视觉由 <c>FxRules.ExplosionVisualScale</c> 映射（100→scale 1.0、160→1.43，
        /// 上限 2.20 对应 dynamite/mine 的 250）。本处只调 FxApi（纯表现），不触发伤害结算。
        /// </summary>
        const float ExplosionSize = 160f;

        /// <summary>
        /// 引爆后到截屏的等待（火球峰值期）。按 `FxRules` 的 size→寿命映射，size=160 时
        /// 火球寿命 = 0.16+0.06×1.43 ≈ **0.246s**（r6 起三层结构下火球仍是这一寿命）。
        /// 【r6 由 0.2 → 0.12】0.2s 落在火球寿命的 **81%**——此时火球已几乎淡出、只剩低透的烟，
        /// 这正是 r5 复验"拍不到火球、只有白雾"的采集侧原因。0.12s 落在 **49%**，
        /// 在火球最亮的中前段（粒子 LifeSpan 刚过一半、尺寸接近峰值），火光可辨。
        /// </summary>
        const float ExplosionWarmupSeconds = 0.12f;

        /// <summary>
        /// battle-45/hud-fullscreen 瞄准点的高度（世界 Y）。地面顶面 y=0，单位身高约 1.5–2，
        /// 瞄 0.8 约在胸口；必须与 `ArtReviewShots.cs` 的 LookAtOffset.y 一致。
        /// </summary>
        const float TeamMidAimHeight = 1.2f;

        string _outDir;
        Camera _camera;
        Canvas _hud;
        Transform _focusUnit;
        Battle.PirateBase _focusPirate;
        Battle.BattleTeam _focusTeam;
        Battle.BattleController _controller;
        Vector3 _arenaCenter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            string outDir = null;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-artReviewOut")
                {
                    outDir = args[i + 1];
                }
                else if (args[i] == "-toonPilotOut")
                {
                    // 等距像素卡通步骤 2 试点出图：进 ToonPilot 场景（见 RunToonPilotCapture）。
                    outDir = args[i + 1];
                    ToonPilotMode = true;
                }
                else if (args[i] == "-artReviewLevel"
                    && int.TryParse(args[i + 1], out int levelArg)
                    && levelArg >= 1 && levelArg <= SceneArt.ShowcaseLevels.LastLevel)
                {
                    // 多关卡出图验收：覆盖 BattleController 的关卡解析（见 ArtReviewCaptureOverride）。
                    ArtReviewCaptureOverride.LevelNumber = levelArg;
                }
            }
            if (string.IsNullOrEmpty(outDir))
                return; // 正常启动：零开销，什么都不装。

            var go = new GameObject("[PlayerArtCapture]");
            go.AddComponent<PlayerArtCapture>()._outDir = outDir;
            DontDestroyOnLoad(go);
        }

        /// <summary>-toonPilotOut 模式标记：进 ToonPilot 试点场景出图（正交机位 + Toon 调试档）。</summary>
        public static bool ToonPilotMode { get; private set; }

        IEnumerator Start()
        {
            Screen.SetResolution(Width, Height, false);
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            // 等 Bootstrapper 的引导流程走完（它会异步加载主菜单并覆盖任何抢先加载的场景），
            // 再切战斗场景——否则 LoadScene(Battle) 会被随后的 MainMenu 加载顶掉，拍到的全是菜单。
            float bootDeadline = Time.unscaledTime + 15f;
            while (SceneManager.GetActiveScene().name != SceneNames.MainMenu
                && Time.unscaledTime < bootDeadline)
                yield return null;
            yield return new WaitForSeconds(0.5f);

            // Toon 试点模式：独立小场景（岛台+船员+海面），与 Battle 出图链完全解耦。
            if (ToonPilotMode)
            {
                yield return RunToonPilotCapture();
                yield break;
            }

            SceneManager.LoadScene(SceneNames.Battle, LoadSceneMode.Single);
            yield return null;
            if (SceneManager.GetActiveScene().name != SceneNames.Battle)
            {
                Debug.LogError("[PlayerArtCapture] Battle 场景加载失败，当前场景："
                    + SceneManager.GetActiveScene().name + "——中止采集");
                Application.Quit(1);
                yield break;
            }

            // 等战斗就绪：单位已生成。超时也要出图（至少能看场景本身）。
            float deadline = Time.unscaledTime + ReadyTimeoutSeconds;
            var controller = FindInRoots<Battle.BattleController>();
            while (Time.unscaledTime < deadline)
            {
                if (controller != null && controller.AllPirates != null && controller.AllPirates.Count > 0)
                    break;
                yield return null;
                if (controller == null)
                    controller = FindInRoots<Battle.BattleController>();
            }
            _controller = controller;

            // 等水体/活物/后处理热身几秒（水模拟需要若干步才有涟漪层次）。
            yield return new WaitForSeconds(3f);

            CollectSceneRefs();
            Directory.CreateDirectory(_outDir);

            foreach (Shot shot in BuildShots())
            {
                // 选中态只在特写/武器面板/HUD 演示机位需要（G-1 验收"选中青描边"；HUD 样板要武器面板滑入）；
                // 它带的可视化（脚下标记/描边）会污染其它评审图（r3：单位脚下洋红方块出现在所有机位）
                // ——故拍特写前才选中，拍完立刻清掉，其余机位保持无选中。
                if (shot.Name == UnitCloseupShotName || shot.Name == WeaponPanelShotName
                    || shot.Name == ShowcaseArmedShotName)
                    SelectFocusUnitForCloseup();

                SetupCamera(shot);

                // 武器面板滑入是 0.16s 动效 + 事件刷新，等它收尾再截（否则拍到半途）。
                if (shot.Name == WeaponPanelShotName || shot.Name == ShowcaseArmedShotName)
                    yield return new WaitForSeconds(0.6f);

                // 爆炸瞬间：先真的引爆再等火光/烟起来，否则拍到的只是空场中景。
                if (shot.Name == ExplosionShotName)
                {
                    FireTestExplosion(_arenaCenter);
                    yield return new WaitForSeconds(ExplosionWarmupSeconds);
                }

                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();

                string path = Path.Combine(_outDir, shot.Name + ".png");
                ScreenCapture.CaptureScreenshot(path);
                // CaptureScreenshot 异步落盘：等文件出现再切下一机位。
                float waitDeadline = Time.unscaledTime + 10f;
                while (!File.Exists(path) && Time.unscaledTime < waitDeadline)
                    yield return null;
                yield return new WaitForSeconds(0.2f);

                // 特写拍完立刻熄选中态，避免污染其后的 explosion-moment。
                if (shot.Name == UnitCloseupShotName)
                    ClearFocusSelection();
            }

            global::PirateCrew.Core.Log.Info("[PlayerArtCapture] 采集完成，退出。目录：" + _outDir);
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        // ================================================================
        // Toon 试点出图（-toonPilotOut）：等距像素卡通步骤 2 的验收出口
        // ================================================================

        /// <summary>
        /// ToonPilot 场景采集：5 张 1920×1080 无损 PNG（判据脚本要求原始 PNG——JPEG 会抹掉
        /// 2px 宽的描边线，judge_toon_pilot 的校准结论）。机位沿装配好的 45° 视线推进
        /// （zoom 系数），后两张切 <c>_DebugMode</c>（档位灰阶 / 只描边）供分层诊断。
        /// </summary>
        IEnumerator RunToonPilotCapture()
        {
            SceneManager.LoadScene("ToonPilot", LoadSceneMode.Single);
            yield return null;
            if (SceneManager.GetActiveScene().name != "ToonPilot")
            {
                Debug.LogError("[PlayerArtCapture] ToonPilot 场景加载失败（Build Settings 未注册？），当前："
                    + SceneManager.GetActiveScene().name);
                Application.Quit(1);
                yield break;
            }

            // 等 ToonLightDriver 首帧下发 + 像素化 Feature 首帧完成。
            yield return new WaitForSeconds(1.5f);

            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[PlayerArtCapture] ToonPilot 场景里找不到 MainCamera。");
                Application.Quit(1);
                yield break;
            }

            Vector3 basePos = cam.transform.position;
            Quaternion baseRot = cam.transform.rotation;
            Vector3 forward = baseRot * Vector3.forward;
            Vector3 target = basePos + forward * 30f; // 装配常量：基准机位到目标 30u

            Directory.CreateDirectory(_outDir);
            (string name, float size, float zoom, float debug)[] shots =
            {
                ("toon-wide", 7f, 1f, 0f),
                ("toon-mid", 3.2f, 0.5f, 0f),
                ("toon-close", 1.6f, 0.3f, 0f),
                ("toon-bands", 3.2f, 0.5f, 1f),   // 档位灰阶：色带切分位置 + dither 撕边形态
                ("toon-ink", 3.2f, 0.5f, 3f),     // 只描边：本体 discard，只剩反壳墨线
            };

            foreach (var shot in shots)
            {
                cam.orthographicSize = shot.size;
                // 沿视线推进（保持 45° 俯角与旋转锁死——等距口径不动，只改距离与视场）。
                cam.transform.position = target - forward * (30f * shot.zoom);
                cam.transform.rotation = baseRot;

                SetToonDebugMode(shot.debug);

                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();

                string path = Path.Combine(_outDir, shot.name + ".png");
                ScreenCapture.CaptureScreenshot(path);
                float waitDeadline = Time.unscaledTime + 10f;
                while (!File.Exists(path) && Time.unscaledTime < waitDeadline)
                    yield return null;
                yield return new WaitForSeconds(0.2f);
            }

            SetToonDebugMode(0f);
            global::PirateCrew.Core.Log.Info("[PlayerArtCapture] Toon 试点采集完成，退出。目录：" + _outDir);
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        /// <summary>
        /// 给场景里所有 PirateToon 材质的 renderer 设 <c>_DebugMode</c>（MPB 覆盖，不动材质资产）；
        /// 传 0 清 MPB 还原。采集场景无批次压力，MPB 打断 SRP Batcher 无所谓。
        /// </summary>
        static void SetToonDebugMode(float mode)
        {
            foreach (MeshRenderer renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                Material mat = renderer.sharedMaterial;
                if (mat == null || mat.shader == null || mat.shader.name != "PirateCrew/PirateToon")
                    continue;

                if (mode > 0f)
                {
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    block.SetFloat("_DebugMode", mode);
                    renderer.SetPropertyBlock(block);
                }
                else
                {
                    renderer.SetPropertyBlock(null);
                }
            }
        }

        void CollectSceneRefs()
        {
            _hud = FindInRoots<Canvas>();

            // 竞技场中心（level_1 ≈ 50×17 单位，中心约 (25,0,8.5)）——取景与引爆共用，不写死坐标。
            var ground = FindRootByName("Ground");
            Bounds bounds = ground != null && ground.GetComponent<Renderer>() != null
                ? ground.GetComponent<Renderer>().bounds
                : new Bounds(new Vector3(25f, 0f, 8.5f), new Vector3(50f, 1f, 17f));
            _arenaCenter = bounds.center;

            ResolveFocusUnit();
        }

        /// <summary>
        /// 解析特写主体并记下它的 <see cref="Battle.PirateBase"/>——**不选中**。
        /// 选中推迟到拍 unit-closeup 前（见 <see cref="SelectFocusUnitForCloseup"/>），
        /// 因为选中态可视化会污染其他机位。取人与选中保持同一确定性口径
        /// （<c>SelectedCharacter ?? FirstAlive</c>），特写取景与选中对象才一致。
        /// </summary>
        void ResolveFocusUnit()
        {
            if (_controller != null)
            {
                Battle.BattleTeam team = _controller.CurrentTeam;
                if (team != null)
                {
                    Battle.PirateBase pick = team.SelectedCharacter ?? team.FirstAlive();
                    if (pick == null && team.Characters.Count > 0)
                        pick = team.Characters[0];

                    if (pick != null)
                    {
                        _focusPirate = pick;
                        _focusUnit = pick.transform;
                        return;
                    }
                }
            }

            // 回退：拿不到 BattleController 时至少按名字找一个主体，保证特写不是空镜。
            Transform teamRoot = FindRootByName("Team0_Red");
            if (teamRoot != null && teamRoot.childCount > 0)
                _focusUnit = teamRoot.GetChild(0);
        }

        /// <summary>
        /// 只在拍特写前选中主体。
        ///
        /// 【为什么必须先选中】G-1 验收的是"选中态青色描边 #49D9D6"（规程:285、:295 的描边口径）；
        /// 未选中的画面里没有描边是**预期行为**，unit-closeup 不选中就等于从未检验过这条判据。
        /// 走 <c>BattleController.SelectCharacter</c>（点选命中的公共入口）——它内部
        /// <c>BattleTeam.Select</c> 会同步 <c>PirateBase.SetSelected</c> 状态位，
        /// <c>UnitOutlineBinder</c> 每帧据此写 <c>_OutlineState</c>，选中描边才会入画。
        /// </summary>
        void SelectFocusUnitForCloseup()
        {
            if (_controller == null || _focusPirate == null)
                return;

            Battle.BattleTeam team = _controller.CurrentTeam;
            if (team == null)
                return;

            _controller.SelectCharacter(_focusPirate);
            _focusTeam = team;
        }

        /// <summary>
        /// 清除特写用的选中态，让后续机位（explosion-moment）画面干净。
        ///
        /// 【为什么用 FinishTurn】<c>BattleTeam.ClearSelection</c> 是私有方法，模块内没有公开的
        /// "取消选中"入口；<c>FinishTurn()</c>（BattleTeam.cs:180-196）是唯一会同时清
        /// <c>SelectedCharacter</c> 与 <c>SelectedCharacter.SetSelected(false)</c> 状态位的公开方法。
        /// 它本身不会推进回合（<c>TurnManager.EndTeamTurn</c> 才是推进方），且人类队未选角色时
        /// TurnManager 只等待、不累计 inactivity，故借用它对采集过程无副作用。
        /// </summary>
        void ClearFocusSelection()
        {
            if (_focusTeam == null)
                return;

            _focusTeam.FinishTurn();
            _focusTeam = null;
        }

        /// <summary>
        /// 在竞技场中心附近主动引爆一次，让火光/烟/冲击波真的出现在 explosion-moment 画面里。
        ///
        /// 【为什么直接调 FxApi】<c>FxApi</c> 是 FX 模块的公开出口（架构原则 3）；
        /// 造一条假弹体走 <c>battle_projectile_detonated</c> 只为出图，会多一层耦合、
        /// 且弹体可能真的砸到单位（改变这一轮要评审的画面）。
        ///
        /// 【为什么靠时序而非确认播放】<c>FxApi.PlayExplosion</c> 返回 void，
        /// <c>FxPool</c>/<c>FxRoot</c> 也没有"当前是否有存活爆炸"的公开查询，
        /// 故只能按寿命曲线取峰值期截屏（见 <see cref="ExplosionWarmupSeconds"/>），
        /// 并打日志记录引爆参数便于事后核对。
        /// </summary>
        static void FireTestExplosion(Vector3 center)
        {
            Vector3 blast = center + new Vector3(0f, 0.5f, 0f);
            Fx.FxApi.PlayExplosion(blast, ExplosionSize);
            global::PirateCrew.Core.Log.Info("[PlayerArtCapture] 已引爆 FX：center=" + blast.ToString("F2")
                + " size=" + ExplosionSize + "，等 " + ExplosionWarmupSeconds + "s 取闪光峰值。");
        }

        Shot[] BuildShots()
        {
            // 【M4 世界地图】经典六机位按 50×17 老竞技场写死，在 150–280u 的大海域图上全部失焦；
            // world 模式改走按地图跨度动态计算的机位组。该组只在播放器 -worldMap 模式使用，
            // 不属于与 ArtReviewShots.cs 互为镜像的经典清单（镜像铁律不覆盖）。
            if (Battle.WorldMaps.WorldMapRuntime.TryGetPending(out var worldMap))
                return BuildWorldShots(worldMap);

            Vector3 c = _arenaCenter;

            // battle-45/hud-fullscreen 瞄准"两队出生区中点"而非纯竞技场中心，让两队都尽量入画
            // （纯中心会让远端一队贴边/被陈设遮挡；见 TeamSpawnMidpoint）。
            Vector3 aim = TeamSpawnMidpoint();

            var shots = new System.Collections.Generic.List<Shot>
            {
                // 【必须与 ArtReviewShots.cs 的 battle-45 同步】Offset (0,11.5,10.61)、瞄准两队中点。
                NewShot("battle-45", true, c + new Vector3(0f, 11.5f, 10.61f), 60f, aim),
                NewShot("arena-overview", false, c + new Vector3(0f, 34f, 22f), 55f, c),
                NewShot("side-profile", false, c + new Vector3(-30f, 8f, 10f), 55f, c),
                NewShot("sea-shore", false, new Vector3(6f, 2.2f, 20f), 60f, new Vector3(2f, -0.2f, 8f)),
                NewShot("terrain-high", false, c + new Vector3(-14f, 10f, 16f), 55f, new Vector3(30f, 1.5f, 4f)),
                // 【必须与 ArtReviewShots.cs 的 hud-fullscreen 同步】与 battle-45 同参数，仅多 HUD。
                NewShot("hud-fullscreen", true, c + new Vector3(0f, 11.5f, 10.61f), 60f, aim),
            };

            if (_focusUnit != null)
            {
                // 武器面板机位（带 HUD、同 battle-45 参数）：选中单位 → 面板滑入 → 截图。
                // 拍完不清选中，紧随其后的纯 HUD 武装演示与特写直接复用。
                shots.Add(NewShot(WeaponPanelShotName, true,
                    c + new Vector3(0f, 11.5f, 10.61f), 60f, aim));
                // 纯 HUD 武装演示（空白背景 + 武器面板开）。
                shots.Add(NewShot(ShowcaseArmedShotName, true, c, 60f, c));

                Vector3 u = _focusUnit.position;
                shots.Add(NewShot(UnitCloseupShotName, false,
                    u + new Vector3(1.1f, 0.9f, 1.4f), 45f, u + new Vector3(0f, 1.2f, 0f)));
            }

            // 爆炸瞬间：斜 45° 中景正视爆心（r3 的近正俯视 (0,6,2) 既框歪又不显火光）。
            // 【必须与 ArtReviewShots.cs 的 explosion-moment 同步】Offset (0,5,-7)、瞄准中心+0.5y。
            shots.Add(NewShot(ExplosionShotName, false,
                c + new Vector3(0f, 5f, -7f), 60f, c + new Vector3(0f, 0.5f, 0f)));

            // 纯 HUD 常态演示（空白背景）放最末：不依赖选中，3D/粒子全被裁掉。
            shots.Add(NewShot(ShowcaseShotName, true, c, 60f, c));

            return shots.ToArray();
        }

        /// <summary>
        /// 世界地图机位组（-worldMap 模式专用）：全景斜 45° / 正俯瞰 / 海平线远眺 / 双方出生群近景。
        /// 距离全部按跨度 span 比例计算，far clip 在 <see cref="SetupCamera"/> 里随 world 模式放宽到 4500。
        /// 不做特写/爆炸机位——经典清单里那两张依赖老竞技场的聚焦与爆心约定。
        /// </summary>
        Shot[] BuildWorldShots(Battle.WorldMaps.WorldMapDefinition map)
        {
            var c = new Vector3(map.SpanX * 0.5f, 0f, map.SpanZ * 0.5f);
            float span = Mathf.Max(map.SpanX, map.SpanZ);

            var shots = new System.Collections.Generic.List<Shot>
            {
                NewShot("world-pano", true,
                    c + new Vector3(span * 0.42f, span * 0.62f, span * 0.6f), 55f, c),
                NewShot("world-overhead", false,
                    c + new Vector3(0f, span * 0.92f, 0.01f), 50f, c),
                NewShot("world-horizon", false,
                    new Vector3(c.x, 2.5f, -span * 0.12f), 60f,
                    c + new Vector3(0f, 6f, span * 0.18f)),
            };

            Vector3 team0 = TeamSpawnCentroid(0);
            Vector3 team1 = TeamSpawnCentroid(1);
            if (team0.sqrMagnitude > 0f)
                shots.Add(SpawnGroupShot("world-team0-spawn", team0, team1));
            if (team1.sqrMagnitude > 0f)
                shots.Add(SpawnGroupShot("world-team1-spawn", team1, team0));

            return shots.ToArray();
        }

        /// <summary>某队全部出生点的地面质心（取不到单位时返回 Vector3.zero，调用方跳过）。</summary>
        Vector3 TeamSpawnCentroid(int teamIndex)
        {
            if (_controller == null || _controller.AllPirates == null)
                return Vector3.zero;
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var pirate in _controller.AllPirates)
            {
                if (pirate == null || !pirate.gameObject.activeInHierarchy)
                    continue;
                if (pirate.TeamIndex != teamIndex)
                    continue;
                sum += pirate.transform.position;
                count++;
            }
            return count > 0 ? sum / count : Vector3.zero;
        }

        /// <summary>出生群近景：从南侧 16u、高 7u 斜视质心（FOV 55 下群像入画、局部地形可辨）。</summary>
        Shot SpawnGroupShot(string name, Vector3 centroid, Vector3 enemyCentroid)
        {
            // 视线轴 = 本队质心 → 敌方质心（只取水平分量）。
            Vector3 toEnemy = enemyCentroid - centroid;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude < 1e-4f)
                toEnemy = Vector3.forward;   // 两队重合（数据异常）时退回固定朝向，不产生 NaN
            toEnemy.Normalize();

            // 【自适应抬升】机位在出生群**背后**（远离敌方那一侧）——而出生群多半落在岛上，
            // "背后"往往正好是同一座岛的中部，kit 视觉件（梯田台地、龟背穹顶、断船船体）
            // 比站面 box 高得多，固定 7u 高度会让画面被近处巨物糊满（实测 spiral_throne 蓝队
            // 机位遮挡 77.9%、角色只剩几个像素）。故先量一次"出生群 30u 内的最高视觉件顶面"，
            // 机位高度与后退距离随它上抬——这条就是"出生机位画面 ≥1/3 是可读战场"的落实。
            float skylineTop = MaxVisualTopNear(centroid, SpawnShotClearRadius);
            float lift = Mathf.Max(0f, skylineTop + SpawnShotClearance - SpawnShotHeight);
            float eyeY = SpawnShotHeight + lift;
            float distance = SpawnShotDistance + lift * SpawnShotLiftToDistance;

            Vector3 eye = centroid - toEnemy * distance + Vector3.up * eyeY;
            Vector3 aim = centroid + Vector3.up * SpawnShotAimHeight + toEnemy * SpawnShotAimPush;
            return NewShot(name, false, eye, 55f, aim);
        }

        /// <summary>出生群机位：沿「本队→敌方」轴反推机位（距离/高度/瞄准前推，单位 u）。</summary>
        const float SpawnShotDistance = 16f;
        const float SpawnShotHeight = 7f;
        const float SpawnShotAimHeight = 1.4f;
        const float SpawnShotAimPush = 6f;
        /// <summary>量天际线的半径（u）：出生群周边这么大范围里的最高视觉件决定机位抬升。</summary>
        const float SpawnShotClearRadius = 30f;
        /// <summary>机位要高出天际线的余量（u）。</summary>
        const float SpawnShotClearance = 5f;
        /// <summary>每抬升 1u 额外后退多少 u（保持俯角不失控、群像不贴脸）。</summary>
        const float SpawnShotLiftToDistance = 0.7f;

        /// <summary>
        /// 出生群周边 <paramref name="radius"/> 内最高视觉件的顶面高度（世界 y）。
        /// 只统计**尺寸合理**的 MeshRenderer：海面裙边那类 4200u 的巨网格不算天际线
        /// （它的包围盒顶面是波峰，会把机位抬到天上）。取不到任何件时返回 0。
        /// </summary>
        static float MaxVisualTopNear(Vector3 center, float radius)
        {
            var renderers = Object.FindObjectsOfType<MeshRenderer>();
            float top = 0f;
            float r2 = radius * radius;
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer mr = renderers[i];
                if (mr == null)
                    continue;
                Bounds b = mr.bounds;
                // 巨网格（海面裙边/远景幕布）不计入天际线
                if (b.size.x > radius * 2f || b.size.z > radius * 2f)
                    continue;
                var dxz = new Vector2(b.center.x - center.x, b.center.z - center.z);
                if (dxz.sqrMagnitude > r2)
                    continue;
                if (b.max.y > top)
                    top = b.max.y;
            }
            return top;
        }

        /// <summary>
        /// 两队出生区的中点：每队各取出生中心，再取两队中点（口径与 `ArtReviewShots.cs`
        /// battle-45/hud-fullscreen 的 LookAtOffset 一致，那边因静态清单只能写死 level_1 的等价偏移）。
        ///
        /// 用**实际已生成单位**的站位近似出生区中心：采集在战斗就绪后 3s 进行，回合制下
        /// 单位基本未移动；这样也天然排除了被出战名册过滤掉的计划条目。
        /// Y 统一取 <see cref="TeamMidAimHeight"/>（地形高低差在瞄准口径里忽略）。
        /// 任一队无单位时回退竞技场中心（保证机位不至于瞄空）。
        /// </summary>
        Vector3 TeamSpawnMidpoint()
        {
            if (_controller == null || _controller.AllPirates == null || _controller.AllPirates.Count == 0)
                return _arenaCenter;

            Vector3 teamCenters = Vector3.zero;
            int teamsWithUnits = 0;

            for (int teamIndex = 0; teamIndex < _controller.TeamCount; teamIndex++)
            {
                Vector3 teamSum = Vector3.zero;
                int count = 0;
                foreach (Battle.PirateBase pirate in _controller.AllPirates)
                {
                    if (pirate != null && pirate.TeamIndex == teamIndex)
                    {
                        teamSum += pirate.transform.position;
                        count++;
                    }
                }

                if (count > 0)
                {
                    teamCenters += teamSum / count;
                    teamsWithUnits++;
                }
            }

            if (teamsWithUnits == 0)
                return _arenaCenter;

            Vector3 mid = teamCenters / teamsWithUnits;
            mid.y = TeamMidAimHeight;
            return mid;
        }

        static Shot NewShot(string name, bool hud, Vector3 pos, float fov, Vector3 lookTarget)
        {
            return new Shot
            {
                Name = name,
                HudVisible = hud,
                Position = pos,
                Rotation = Quaternion.LookRotation(lookTarget - pos, Vector3.up),
                Fov = fov,
            };
        }

        void SetupCamera(Shot shot)
        {
            if (_camera == null)
            {
                var go = new GameObject("[ArtReviewCamera]");
                go.AddComponent<AudioListener>(); // 独立场景里可能没有监听器；多余监听器仅影响音频，不影响画面
                _camera = go.AddComponent<Camera>();
            }
            // 让评审相机接管渲染：禁用主相机（含 Cinemachine Brain）。
            Camera main = Camera.main;
            if (main != null && main != _camera)
                main.enabled = false;
            var brain = FindInRoots<Cinemachine.CinemachineBrain>();
            if (brain != null)
                brain.enabled = false;

            _camera.transform.position = shot.Position;
            _camera.transform.rotation = shot.Rotation;
            _camera.fieldOfView = shot.Fov;
            _camera.nearClipPlane = 0.05f;
            // M4 世界地图：4200u 远场裙边与远景装饰要入画，far 放宽；经典图维持 300 防远景糊近景排序。
            _camera.farClipPlane = Battle.WorldMaps.WorldMapRuntime.TryGetPending(out _)
                ? 4500f
                : 300f;
            _camera.enabled = true;

            // 纯 HUD 演示：裁掉全部 3D 层 + 纯色清屏（"空白窗口里看 HUD"）；其余机位恢复正常渲染。
            bool showcase = shot.Name == ShowcaseShotName || shot.Name == ShowcaseArmedShotName;
            if (showcase)
            {
                _camera.cullingMask = 0;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = ShowcaseBackdrop;
            }
            else
            {
                _camera.cullingMask = ~0;
                _camera.clearFlags = CameraClearFlags.Skybox;
            }

            if (_hud != null)
                _hud.enabled = shot.HudVisible;
        }

        static T FindInRoots<T>() where T : Component
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }
            return null;
        }

        static Transform FindRootByName(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == name)
                    return root.transform;
                foreach (Transform child in root.transform)
                {
                    if (child.name == name)
                        return child;
                }
            }
            return null;
        }
    }
}
