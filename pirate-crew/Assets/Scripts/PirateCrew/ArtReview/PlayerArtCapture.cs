using System.Collections;
using System.IO;
using PirateCrew.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateCrew.PirateCrew.ArtReview
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

        /// <summary>
        /// 主动引爆用的爆炸 size：取 160（`WeaponCatalog.cs` banana / parachuteBomb 档），
        /// 比 cannonball 基准 100（`CannonRules.cs:45`）大一档，纯为评审画面里火光可辨——
        /// size→视觉由 <c>FxRules.ExplosionVisualScale</c> 映射（100→scale 1.0、160→1.43，
        /// 上限 2.20 对应 dynamite/mine 的 250）。本处只调 FxApi（纯表现），不触发伤害结算。
        /// </summary>
        const float ExplosionSize = 160f;

        /// <summary>
        /// 引爆后到截屏的等待（闪光峰值期）。按 `FxRules` 的 size→寿命映射，size=160 时
        /// 火球寿命仅 0.16+0.06×1.43≈0.25s、火花≈0.42s、烟≈1.6s；r3 等 0.4s 时火球/火花**已消散**，
        /// 只剩低饱和的烟与木屑（实测 fire_orange=0px）。0.2s 落在火球寿命 80% 处，火光仍在最亮段。
        /// </summary>
        const float ExplosionWarmupSeconds = 0.2f;

        /// <summary>
        /// battle-45/hud-fullscreen 瞄准点的高度（世界 Y）。地面顶面 y=0，单位身高约 1.5–2，
        /// 瞄 0.8 约在胸口；必须与 `ArtReviewShots.cs` 的 LookAtOffset.y 一致。
        /// </summary>
        const float TeamMidAimHeight = 0.8f;

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
                    break;
                }
            }
            if (string.IsNullOrEmpty(outDir))
                return; // 正常启动：零开销，什么都不装。

            var go = new GameObject("[PlayerArtCapture]");
            go.AddComponent<PlayerArtCapture>()._outDir = outDir;
            DontDestroyOnLoad(go);
        }

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
                // 选中态只在特写机位需要（G-1 验收"选中青描边"）；它带的可视化（脚下标记/描边）
                // 会污染其它评审图（r3：单位脚下洋红方块出现在所有机位）——故拍特写前才选中，
                // 拍完立刻清掉，其余机位保持无选中。
                if (shot.Name == UnitCloseupShotName)
                    SelectFocusUnitForCloseup();

                SetupCamera(shot);

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

            Debug.Log("[PlayerArtCapture] 采集完成，退出。目录：" + _outDir);
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
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
            Debug.Log("[PlayerArtCapture] 已引爆 FX：center=" + blast.ToString("F2")
                + " size=" + ExplosionSize + "，等 " + ExplosionWarmupSeconds + "s 取闪光峰值。");
        }

        Shot[] BuildShots()
        {
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
                Vector3 u = _focusUnit.position;
                shots.Add(NewShot(UnitCloseupShotName, false,
                    u + new Vector3(1.1f, 0.9f, 1.4f), 45f, u + new Vector3(0f, 0.25f, 0f)));
            }

            // 爆炸瞬间：斜 45° 中景正视爆心（r3 的近正俯视 (0,6,2) 既框歪又不显火光）。
            // 【必须与 ArtReviewShots.cs 的 explosion-moment 同步】Offset (0,5,-7)、瞄准中心+0.5y。
            shots.Add(NewShot(ExplosionShotName, false,
                c + new Vector3(0f, 5f, -7f), 60f, c + new Vector3(0f, 0.5f, 0f)));

            return shots.ToArray();
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
            _camera.farClipPlane = 300f;
            _camera.enabled = true;

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
