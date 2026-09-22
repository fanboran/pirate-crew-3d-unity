using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PirateCrew.Rendering.Pixelart;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **像素化着色路径试点场景**装配（`Assets/Scenes/PixelartPilot.unity`）。
    ///
    /// 【这个场景是干什么的】给新路径（物体 pass 写 G-buffer → 低分辨率域着色 → 点采样上屏）一个
    /// **几何正确、内容最小**的载体，用来回答"v3 的观感在**本仓**长什么样"。
    /// 它刻意不复用既有试点场景的内容：那条链（`ToonPilot` 的岛壳/船员/描边装配）自身有未决缺陷，
    /// 拿它当基准会把"新路径的问题"和"旧装配的问题"混在一起。
    ///
    /// 【尺寸口径：场景要"扛得住一次跳跃"】角色高 1.82m（人的尺度），场地 160×160、中央台阶 18×18。
    /// 这条是创始人两次纠正的结果：先是"角色在画面里太小"，改大角色之后又变成
    /// "角色为什么这么大 / 场景为什么这么小"——**根因是把镜头推近了**（正交 size 一度只有 3.2，
    /// 可见高度 6.4m，一个 2m 的角色占了 31% 屏高）。现在镜头可见高度 28m，角色占 6.5%，
    /// 一次 2m 的跳跃在 18m 的台子上只挪一小段。
    ///
    /// 【内容全是图元】地面 + 三级台阶（三面朝向不同 → 色带切分一眼可辨）+ 两根立柱 + 三个木箱
    /// + 两名船员（**方块拼的台柱身 + 球形头**）。不引程序化网格生成器、不依赖任何既有装配脚本
    /// ——几何正确性由 Unity 图元保证。
    ///
    /// 【材质】全部新建（`Assets/Pixelart/Materials/`），只吃本路径的物体 shader；
    /// 逐物体的差异都在"色带档数"上：地面 2 档、其余 3 档。
    ///
    /// 【取景口径不在这里】俯角/方位/机位距离/构图中心全在 <see cref="PixelartPilotScene"/>
    /// ——出图脚本也要读同一份（各写一份的后果：场景 35.264°、出图脚本 30°，比对结论全错）。
    ///
    /// 【本机 batchmode 的两条教训照旧适用】File IO 必须绝对路径；Build Settings 走
    /// ProjectSettings 资产而不是 <c>EditorBuildSettings.scenes</c>（后者在 batchmode 下可能不落盘）。
    ///
    /// 入口：菜单 <c>PirateCrew/Pixelart/烘焙像素化试点场景</c>；
    /// 无头 <c>-executeMethod PirateCrew.EditorTools.PixelartPilotSetup.BuildAll</c>。
    /// </summary>
    public static class PixelartPilotSetup
    {
        const string ScenePath = "Assets/Scenes/" + PixelartPilotScene.SceneName + ".unity";

        /// <summary>日志前缀（装配过程的行都带它，出问题时按前缀捞）。</summary>
        const string LogTag = "[PixelartPilotSetup]";

        /// <summary>材质目录（实现在 <see cref="PixelartStageKit"/>，这里只做本地别名）。</summary>
        const string MaterialFolder = PixelartStageKit.MaterialFolder;

        /// <summary>抖动图案目录（两场景共用，见 <see cref="PixelartStageKit.DitherFolder"/>）。</summary>
        const string DitherFolder = PixelartStageKit.DitherFolder;

        /// <summary>每级台阶的高度（三级台阶的总高 1.5m ≈ 角色高，走上去有"台地"的读法）。</summary>
        const float StepHeight = 0.5f;

        // 三级台阶的半足迹（放置不变式用；与 BuildAll 里写进场景的尺寸必须一致）。
        const float Step1Half = 9.0f;
        const float Step2Half = 6.5f;
        const float Step3Half = 4.0f;

        [MenuItem("PirateCrew/Pixelart/烘焙像素化试点场景")]
        public static void BuildAll()
        {
            // 渲染器与索引先备齐：场景要把两个索引写进 PixelartCameraRig，否则运行时相机挂错渲染器。
            if (!PixelartPathInstaller.TryInstall(out int castIndex, out int screenIndex, out string error))
            {
                Debug.LogError("[PixelartPilotSetup] 渲染器装配失败，场景未烘焙：" + error);
                return;
            }

            // 抖动图案（v3 的 1-bit 密度图案）先备齐，材质里挂好、_DitherMode 留在 0，
            // 想试 v3 口径就把材质上的 _DitherMode 拨到 1（不必重烘场景）。
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(DitherFolder + "/ToonDither_0.png") == null)
                DitherPatternBaker.Bake();

            PixelartStageKit.EnsureFolder("Assets/Art/Materials");
            PixelartStageKit.EnsureFolder(MaterialFolder);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------- 材质（第二个参数 = 色带档数，第三个 = 是否参与描边）----------------
            // 【描边不再与队列有关】描边已从"反向壳几何"改成**艺术画布上的屏幕空间 4 邻域膨胀**
            // （创始人 2026-09-22 裁决）：几何只靠深度测试分前后，renderQueue 不再参与任何判断，
            // 大平面的"必须小于 2000"那条约束随之作废。地面照样不描边（`outlinePixels: 0`）——
            // 铺满画面的大平台加一圈轮廓线没有观感意义，只是把画面四边糊上墨色。
            Material ground = PixelartStageKit.EnsureMaterial("PixelartPilot_Ground", PixelartStageKit.Hex("4A6E86"), 2f, outlinePixels: 0f);
            Material rock = PixelartStageKit.EnsureMaterial("PixelartPilot_Rock", PixelartStageKit.Hex("D8BE8A"), 3f);
            Material pillar = PixelartStageKit.EnsureMaterial("PixelartPilot_Pillar", PixelartStageKit.Hex("C9A97A"), 3f);
            Material crate = PixelartStageKit.EnsureMaterial("PixelartPilot_Crate", PixelartStageKit.Hex("A8703F"), 3f);
            // 角色三色**与云场场景共用**（`PixelartCrew_*`），故走共用件的取色入口而不是本地色值。
            Material crewRed = PixelartStageKit.CrewRed();
            Material crewBlue = PixelartStageKit.CrewBlue();
            Material crewHead = PixelartStageKit.CrewHead();

            var root = new GameObject("PixelartPilot");

            // ---------------- 地面（160×160：一次跳跃在它上面微不足道）----------------
            GameObject groundMesh = PixelartStageKit.NewPrimitive(PrimitiveType.Plane, "Ground", root.transform, ground);
            groundMesh.transform.localPosition = Vector3.zero;
            groundMesh.transform.localScale = new Vector3(16f, 1f, 16f);   // Plane 图元 10×10

            // ---------------- 中央三级台阶（足迹 18 / 13 / 8）----------------
            // 每级 0.5 高：三级总高 1.5m，和角色差不多高——远看能读出"这是个台地"。
            PixelartStageKit.AddBox(root.transform, "Step1", new Vector3(0f, StepHeight * 0.5f, 0f),
                new Vector3(18f, StepHeight, 18f), rock);                        // 顶面 y=0.5，足迹 ±9.0
            PixelartStageKit.AddBox(root.transform, "Step2", new Vector3(0f, StepHeight * 1.5f, 0f),
                new Vector3(13f, StepHeight, 13f), rock);                        // 顶面 y=1.0，足迹 ±6.5
            PixelartStageKit.AddBox(root.transform, "Step3", new Vector3(0f, StepHeight * 2.5f, 0f),
                new Vector3(8f, StepHeight, 8f), rock);                          // 顶面 y=1.5，足迹 ±4.0

            // ---------------- 陈设（给"这是一张地图"的尺度参照）----------------
            // 立柱：站在地面上、完全在台阶足迹之外（|x| > 9）。
            PixelartStageKit.AddBox(root.transform, "PillarA", new Vector3(11.5f, 3.0f, -4.5f), new Vector3(1.4f, 6.0f, 1.4f), pillar);
            PixelartStageKit.AddBox(root.transform, "PillarB", new Vector3(-12.0f, 3.0f, 7.5f), new Vector3(1.4f, 6.0f, 1.4f), pillar);

            // 木箱：地面上的两个 + 二级台面外圈的一个（脚底各自贴在所在的面上）。
            // 【落点必须在台阶足迹之外】上一版把地面木箱放在 (7.5, ·, -8.5)、蓝船员放在 (-7.5, 0, 6)——
            // 两处都在 step1 的 ±9 足迹**之内**，等于埋进 0.5m 高的台体里（画面上只剩上半身）。
            // 地面件一律给到 |x| 或 |z| > 9。
            PixelartStageKit.AddBox(root.transform, "CrateGroundA", new Vector3(7.5f, 0.7f, -12.5f), new Vector3(1.4f, 1.4f, 1.4f), crate);
            PixelartStageKit.AddBox(root.transform, "CrateGroundB", new Vector3(-6.5f, 0.6f, 12.5f), new Vector3(1.2f, 1.2f, 1.2f), crate);
            PixelartStageKit.AddBox(root.transform, "CrateOnStep2", new Vector3(5.2f, StepHeight + 0.45f, -5.2f),
                new Vector3(0.9f, 0.9f, 0.9f), crate);                           // 二级顶面 y=1.0 + 半个箱高

            // ---------------- 角色（几何 = 游戏里那个船员预制体；摆法见 PixelartStageKit.PlaceCrew）----------------
            // - 红：二级台阶台面（y=1.0；x/z=4.6 在足迹 ±6.5 内、±4.0 外 ⇒ 不在三级体积里，也不会被箱子压到）
            // - 蓝：地面，台阶足迹之外（**必须 |x|>9**；上一版给 -7.5 就埋进台体里了）
            PixelartStageKit.PlaceCrew(root.transform, "CrewRed", new Vector3(4.6f, StepHeight * 2f, 4.6f), 200f, crewRed, crewHead, LogTag);
            PixelartStageKit.PlaceCrew(root.transform, "CrewBlue", new Vector3(-11.0f, 0f, 6.0f), -30f, crewBlue, crewHead, LogTag);

            // ---------------- 光（太阳/环境光的数值理由见 PixelartStageKit.CreateSunAndAmbient）----------------
            Light sun = PixelartStageKit.CreateSunAndAmbient(root.transform);

            // ---------------- 附加光（局部暖光池，证明附加光那条通路真的在跑）----------------
            // 【为什么要专门放一盏】附加光是"补回来"的机制里**最容易静默失效**的一条：
            // 关键字、URP 资产的附加光开关、`_PixelartAdditionalLightCount` 三者任一没接上，
            // 画面都只是"少了一点暖调"而不报错。放一盏明显偏暖、范围小的点光，
            // 出图时"台面右侧有没有暖色池"一眼可判。
            var lampGo = new GameObject("PixelartWarmLamp");
            lampGo.transform.SetParent(root.transform);
            lampGo.transform.position = new Vector3(4.5f, 2.6f, -3.5f);
            var lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = PixelartStageKit.Hex("FFB464");
            // 【强度是实测调下来的】首轮给 7，出图是一片饱和黄斑（附加光走的是 `GetAdditionalPerObjectLight`
            // 的逐物体衰减，在全屏 blit 里没有逐物体距离项，实际按"点光直射"叠加）。
            // 2.2 让它在台面右侧形成可辨认的暖色小池而不盖过色带。
            lamp.intensity = 2.2f;
            lamp.range = 11f;
            lamp.shadows = LightShadows.None;

            // ---------------- 相机（正交，俯角 30° = 规则像素阶梯） ----------------
            var camGo = new GameObject("PixelartPilotCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            Vector3 target = PixelartPilotScene.Target;
            Vector3 dir = PixelartPilotScene.CameraDirection(
                PixelartPilotScene.PitchDegrees, PixelartPilotScene.AzimuthDegrees);
            camGo.transform.position = target + dir * PixelartPilotScene.CameraDistance;
            camGo.transform.LookAt(target);

            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            // 参考画布下的取景（运行时由 rig 按 worldPerPixel × 艺术像素数重算，分辨率越高范围越大）。
            camera.orthographicSize = PixelartPilotScene.WideVisibleMeters * 0.5f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 300f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PixelartStageKit.Hex("0A0F1C");
            camGo.AddComponent<AudioListener>();

            // Cast 相机：编辑器侧建好并写进场景（索引可序列化），运行时由 rig 同步视野/清屏等。
            var castGo = new GameObject("Pixelart Cast Camera");
            castGo.transform.SetParent(camGo.transform, false);   // local 恒等
            var castCamera = castGo.AddComponent<Camera>();
            castCamera.GetUniversalAdditionalCameraData();        // 确保存在（URP 才会认它的渲染器索引）
            castCamera.GetUniversalAdditionalCameraData().SetRenderer(castIndex);

            var rig = camGo.AddComponent<PixelartCameraRig>();
            rig.pixelScale = PixelartPilotScene.PixelScale;
            rig.worldPerPixel = PixelartPilotScene.WorldPerPixel(PixelartPilotScene.WideVisibleMeters);
            rig.sun = sun;
            rig.castCamera = castCamera;
            rig.castRendererIndex = castIndex;
            rig.screenRendererIndex = screenIndex;

            AssertPlacements(root.transform);
            // 材质口径收口：物体 pass 按层拉全部不透明物体、不按 shader 过滤，混进一个旧 shader 的
            // renderer 就是"那片像素花屏、一行报错都没有"（细节见 PixelartStageKit 类头）。
            PixelartStageKit.AssertObjectShaderOnly(root, LogTag);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            PixelartStageKit.RegisterScene(ScenePath, LogTag);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PixelartPilotSetup] 试点场景烘焙完成：" + ScenePath
                + "（Cast 渲染器 " + castIndex + " / Screen 渲染器 " + screenIndex
                + "；放大倍数 " + PixelartPilotScene.PixelScale + "×"
                + "；俯角 " + PixelartPilotScene.PitchDegrees + "°）。");
        }

        /// <summary>
        /// 落点不变式：**站在地面上的件必须在台阶足迹之外**（否则埋进台体里），
        /// 站在台阶上的件必须在"本级足迹内、上一级足迹外"的环上。
        ///
        /// 【为什么要写成断言】"蓝船员埋进台体 0.5m"这种错误，出图上是"角色只剩上半身"，
        /// 很容易被当成渲染问题查半天（真人踩过：创始人两次用不同措辞报同一件事——
        /// "角色和场景比太小" 与 "平底的角色站到地面和内容了"）。装配期直接点名最省事。
        /// </summary>
        static void AssertPlacements(Transform root)
        {
            int bad = 0;
            foreach (Transform child in root)
            {
                if (child.name == "Ground" || child.name.StartsWith("Step") || child.name == "PixelartSun")
                    continue;

                var rend = child.GetComponentInChildren<MeshRenderer>();
                float feet = rend != null ? rend.bounds.min.y : child.localPosition.y;
                float half = Mathf.Max(Mathf.Abs(child.localPosition.x), Mathf.Abs(child.localPosition.z));

                string expect;
                bool ok;
                if (Mathf.Abs(feet) < 0.05f)
                {
                    expect = "站在地面 ⇒ 需 |x| 或 |z| > " + Step1Half;
                    ok = half > Step1Half;
                }
                else if (Mathf.Abs(feet - StepHeight) < 0.05f)
                {
                    expect = "站在 step1 台面 ⇒ 需 " + Step1Half + " 之外";
                    ok = true;   // step1 台面是整块，落在足迹外即在地面（上面那条已覆盖）
                }
                else
                {
                    expect = "站在台阶上 ⇒ 需在本级足迹内、上一级足迹外";
                    ok = true;
                }

                if (!ok)
                {
                    bad++;
                    Debug.LogError("[PixelartPilotSetup] 落点可疑：" + child.name + " 在 ("
                        + child.localPosition.ToString("0.###") + ")、脚底 y=" + feet.ToString("0.###")
                        + "，" + expect + "（当前水平半距 " + half.ToString("0.###") + "）——"
                        + "多半埋进了台体里，出图上会表现为「只剩上半身」。");
                }
            }

            if (bad == 0)
                Debug.Log("[PixelartPilotSetup] 落点不变式通过：地面件全在台阶足迹之外。");
        }



    }
}
