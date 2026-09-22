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
    /// 【材质】全部新建（`Assets/Art/Materials/Pixelart/`），只吃本路径的物体 shader；
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
        const string MaterialFolder = "Assets/Art/Materials/Pixelart";
        const string DitherFolder = "Assets/Art/Textures/Fx/Dither";

        /// <summary>
        /// 船员预制体（角色几何的唯一来源）。用它是为了**试点的角色就是游戏里的角色**——
        /// 造型早有裁决（见 <see cref="AddCrew"/> 的注释），自己拼图元必然走样。
        /// </summary>
        const string CrewPrefabPath = "Assets/Prefabs/PirateCrew/Crew/Sailor.prefab";

        /// <summary>
        /// 预制体根原点到脚底的距离（米）。**不是猜的**：根缩放 y 0.5 × Visual.localPosition.y −0.5
        /// = −0.25，Body 圆台柱底面正落在世界 −0.25 ⇒ 根原点在脚底上方 0.25。
        /// 于是"脚底落在 surfaceY" = 实例根放在 <c>surfaceY + 0.25</c>。总高 1.85m。
        /// </summary>
        const float CrewRootToFeetOffset = 0.25f;

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

            EnsureFolder("Assets/Art/Materials");
            EnsureFolder(MaterialFolder);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------- 材质（第二个参数 = 色带档数，第三个 = 描边线宽/低分辨率像素）----------------
            // 地面不描边（铺满画面的大平面的壳环会顶到边缘、无观感意义），且队列压到 1999 ——
            // 墨线壳是"外扩 + 沿视线拉近"，轮廓外侧那圈像素落在地面上；地面**必须先画完**，
            // 否则它 ZWrite On 的本体会把壳环整圈盖掉（渲染篇 §5 + 旧链"海面回 2000"的同一结论）。
            Material ground = EnsureMaterial("PixelartPilot_Ground", HexGamma("4A6E86"), 2f,
                outlinePixels: 0f, renderQueue: PixelartPath.BackgroundPlaneRenderQueue);
            Material rock = EnsureMaterial("PixelartPilot_Rock", HexGamma("D8BE8A"), 3f);
            Material pillar = EnsureMaterial("PixelartPilot_Pillar", HexGamma("C9A97A"), 3f);
            Material crate = EnsureMaterial("PixelartPilot_Crate", HexGamma("A8703F"), 3f);
            Material crewRed = EnsureMaterial("PixelartPilot_CrewRed", HexGamma("DE524D"), 3f);
            Material crewBlue = EnsureMaterial("PixelartPilot_CrewBlue", HexGamma("598CD9"), 3f);
            // 球头用船员自己的木色（`CrewVisualPrefabBuilder` 的 `CrewMaterialRole.Wood` = #D4A76A）。
            Material crewHead = EnsureMaterial("PixelartPilot_CrewHead", HexGamma("D4A76A"), 3f);

            var root = new GameObject("PixelartPilot");

            // ---------------- 地面（160×160：一次跳跃在它上面微不足道）----------------
            GameObject groundMesh = NewPrimitive(PrimitiveType.Plane, "Ground", root.transform, ground);
            groundMesh.transform.localPosition = Vector3.zero;
            groundMesh.transform.localScale = new Vector3(16f, 1f, 16f);   // Plane 图元 10×10

            // ---------------- 中央三级台阶（足迹 18 / 13 / 8）----------------
            // 每级 0.5 高：三级总高 1.5m，和角色差不多高——远看能读出"这是个台地"。
            AddBox(root.transform, "Step1", new Vector3(0f, StepHeight * 0.5f, 0f),
                new Vector3(18f, StepHeight, 18f), rock);                        // 顶面 y=0.5，足迹 ±9.0
            AddBox(root.transform, "Step2", new Vector3(0f, StepHeight * 1.5f, 0f),
                new Vector3(13f, StepHeight, 13f), rock);                        // 顶面 y=1.0，足迹 ±6.5
            AddBox(root.transform, "Step3", new Vector3(0f, StepHeight * 2.5f, 0f),
                new Vector3(8f, StepHeight, 8f), rock);                          // 顶面 y=1.5，足迹 ±4.0

            // ---------------- 陈设（给"这是一张地图"的尺度参照）----------------
            // 立柱：站在地面上、完全在台阶足迹之外（|x| > 9）。
            AddBox(root.transform, "PillarA", new Vector3(11.5f, 3.0f, -4.5f), new Vector3(1.4f, 6.0f, 1.4f), pillar);
            AddBox(root.transform, "PillarB", new Vector3(-12.0f, 3.0f, 7.5f), new Vector3(1.4f, 6.0f, 1.4f), pillar);

            // 木箱：地面上的两个 + 二级台面外圈的一个（脚底各自贴在所在的面上）。
            // 【落点必须在台阶足迹之外】上一版把地面木箱放在 (7.5, ·, -8.5)、蓝船员放在 (-7.5, 0, 6)——
            // 两处都在 step1 的 ±9 足迹**之内**，等于埋进 0.5m 高的台体里（画面上只剩上半身）。
            // 地面件一律给到 |x| 或 |z| > 9。
            AddBox(root.transform, "CrateGroundA", new Vector3(7.5f, 0.7f, -12.5f), new Vector3(1.4f, 1.4f, 1.4f), crate);
            AddBox(root.transform, "CrateGroundB", new Vector3(-6.5f, 0.6f, 12.5f), new Vector3(1.2f, 1.2f, 1.2f), crate);
            AddBox(root.transform, "CrateOnStep2", new Vector3(5.2f, StepHeight + 0.45f, -5.2f),
                new Vector3(0.9f, 0.9f, 0.9f), crate);                           // 二级顶面 y=1.0 + 半个箱高

            // ---------------- 角色（几何 = 游戏里那个船员预制体；见 AddCrew 的注释）----------------
            // - 红：二级台阶台面（y=1.0；x/z=4.6 在足迹 ±6.5 内、±4.0 外 ⇒ 不在三级体积里，也不会被箱子压到）
            // - 蓝：地面，台阶足迹之外（**必须 |x|>9**；上一版给 -7.5 就埋进台体里了）
            AddCrew(root.transform, "CrewRed", new Vector3(4.6f, StepHeight * 2f, 4.6f), 200f, crewRed, crewHead);
            AddCrew(root.transform, "CrewBlue", new Vector3(-11.0f, 0f, 6.0f), -30f, crewBlue, crewHead);

            // ---------------- 光 ----------------
            // 【太阳高度 58° 是算出来的，不是随手给的】3 档色带下"顶面 / 朝光立面 / 背光立面"
            // 必须落进三个不同档，否则**台阶会读成一块平面**（实测：仰角 48° 时顶面与朝光立面
            // 的 ndotl 只差 0.23、量化后同档 ⇒ 所有台面糊成一整块菱形）。
            // 58° 时顶面/朝光立面/背光面 ≈ 0.85 / 0.41 / 0.0 三档。
            // 方位角沿用本仓"左上光"惯例 140°。
            var sunGo = new GameObject("PixelartSun");
            sunGo.transform.SetParent(root.transform);
            sunGo.transform.rotation = Quaternion.Euler(58f, 140f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = HexGamma("FFF5E0");
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.None;    // 色带表面接实时投影必脏（渲染篇 §4.5）
            RenderSettings.sun = sun;

            // 环境光 = 本路径的"暗部色"（暗面 = albedo × 环境色）。来源与取舍：
            //   · 口径：渲染篇 §4.6"单平行光 + 单环境光"，蓝本 §7.2 第 5 条"GI 用扁平环境光"
            //     ——v3 的暗面来自 SH（`_GlobalIlluminationBuffer` 的 lightmap 通道实际是空的，
            //     等于只吃 SH，蓝本 §2 注意 2），本路径按裁决简化为这一项。
            //   · **不能给太小**：旧链取 #1A1E28 是有配套的——它的暗部是材质上的
            //     **替换式暗部色**（`_ShadowColor`，渲染篇 §4.2），本路径还没有那条通道，
            //     暗面就是 albedo × 环境色；环境色压到 #1A1E28 时暗面落在 sRGB 0.08 上下，
            //     与墨线（#120C14）几乎同值，轮廓线和暗面糊成一片，等于没有描边。
            //   · **也不能给太大**：加法环境光会抬平明暗跨度（旧链实测结论）。
            //   本档 #3A4760 让暗面落在 sRGB 0.2 上下：明显暗于亮面、又明显亮于墨线。
            //   这一条是**美术旋钮**（改这里即整体调暗部亮度），不是机制。
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = HexGamma("3A4760");

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
            camera.backgroundColor = HexGamma("0A0F1C");
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

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            RegisterInBuildSettings(ScenePath);

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

        static GameObject NewPrimitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static GameObject AddBoxObj(Transform parent, string name, Material material)
        {
            return NewPrimitive(PrimitiveType.Cube, name, parent, material);
        }

        static void AddBox(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = AddBoxObj(parent, name, material);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
        }

        /// <summary>
        /// 角色：**直接用游戏里那个船员预制体的几何**（只换材质），不再自己拼图元。
        ///
        /// 【为什么不再手搓】上一版我用两个方块 + 一块帽檐平板拼了个"人"，创始人两次驳回
        /// （"为什么身体不是台柱" / "球头上为什么还有平板"）。本仓对船员造型**早有裁决**
        /// （`CrewVisualPrefabBuilder`：用户裁决 2026-09-14，两件式 = **圆球 + 圆台柱**，
        /// 三角帽/头巾/腿/靴/臂/掌全部删除，"要和 Godot 里面一模一样那种圆球+圆台柱"）：
        ///
        ///   · Body 圆台柱：顶 r 0.35 / 底 r 0.40 / 高 1.20 / 16 段，网格 `CrewBodyFrustum.asset`
        ///   · Head 圆球：r 0.35，球心在柱顶上方（与柱顶微叠 0.05）
        ///   · **没有帽子**——那是一块我自己加的平板
        ///
        /// 所以本方法改为实例化 `Sailor.prefab` 并只做两件事：换成本路径的材质、剥掉玩法脚本。
        /// 这样尺寸/枢轴/网格与游戏里逐值一致，也**不会**出现"试点的角色和游戏里的不是同一个形状"。
        ///
        /// 【根原点的偏移是读出来的，不是猜的】预制体根局部缩放 (0.375, 0.5, 0.375)、
        /// Visual 用 (2.6667, 2, 2.6667) 抵消 ⇒ Visual 局部 1 单位 = 世界 1 单位；
        /// Visual 自身 y=−0.5 ⇒ 世界 y = Visual 局部 y − 0.25。Body 在 Visual 局部 y=0.6、高 1.20
        /// ⇒ 世界底 −0.25、头顶 1.85−0.25=1.60 ⇒ **总高 1.85m，根原点在脚底上方 0.25m**。
        /// 于是"脚底落在 surfaceY"= 实例根放在 <c>surfaceY + 0.25</c>。
        /// </summary>
        static void AddCrew(Transform parent, string name, Vector3 feetPosition, float yawDegrees,
            Material bodyMaterial, Material headMaterial)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrewPrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[PixelartPilotSetup] 找不到船员预制体 " + CrewPrefabPath
                    + "（跑过一次 PirateCrew/角色/生成职业视觉预制体 吗？），角色未摆放。");
                return;
            }

            var crewRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            crewRoot.name = name;
            crewRoot.transform.SetParent(parent);
            crewRoot.transform.localPosition = feetPosition + new Vector3(0f, CrewRootToFeetOffset, 0f);
            crewRoot.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            crewRoot.transform.localScale = prefab.transform.localScale;   // 预制体的根缩放口径照搬

            // 先**完全解包**成普通场景对象：对预制体实例直接 DestroyImmediate 组件，
            // 实测**删不掉 Rigidbody 与 PirateBase**（场景里 m_RemovedComponents 只记下了
            // BoxCollider 与三个 MonoBehaviour，那两个悄悄留了下来——`[RequireComponent]`
            // 关系下的组件会被 Unity 重新补回来）。解包成普通对象后，删组件就是普通的场景操作。
            if (PrefabUtility.IsPartOfPrefabInstance(crewRoot))
                PrefabUtility.UnpackPrefabInstance(crewRoot, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);

            // 只留几何：Transform / MeshFilter / MeshRenderer 以外的组件一律剥掉。
            //
            // 【为什么必须剥干净——这一条踩了大坑】船员预制体的根上带 **Rigidbody + BoxCollider**。
            // 编辑态不跑物理 ⇒ 编辑器里量到的世界包围盒完全正确（脚底 y=1.0），
            // 进播放器后 Rigidbody 带重力自由落体，而采集发生在载入后约 1.5 秒，
            // 下落距离 ½·9.81·1.5² ≈ **11m** —— 与实测"角色比场景低 11.441m、被地面挡住看不见"逐位对上。
            // 症状是"编辑器里对、播放器里没有角色"，且一行报错都没有。
            // ⇒ 口径是**白名单**（只留几何三件套），并且**多轮清扫 + 残留断言**（见下）。
            for (int pass = 0; pass < 3; pass++)
            {
                int removed = 0;
                foreach (Component component in crewRoot.GetComponentsInChildren<Component>(true))
                {
                    if (component is Transform || component is MeshFilter || component is MeshRenderer)
                        continue;
                    Object.DestroyImmediate(component);
                    removed++;
                }
                if (removed == 0)
                    break;
            }

            // 残留断言：这条路的失效方式是"场景在编辑器里看着对、进播放器才露馅"，
            // 所以剥完当场核一遍——有残留就报错点名，别等到出图看不见角色再回头查。
            var leftovers = new System.Collections.Generic.List<string>();
            foreach (Component component in crewRoot.GetComponentsInChildren<Component>(true))
            {
                if (component is Transform || component is MeshFilter || component is MeshRenderer)
                    continue;
                leftovers.Add(component.GetType().Name + "@" + component.gameObject.name);
            }
            if (leftovers.Count > 0)
            {
                Debug.LogError("[PixelartPilotSetup] 角色 " + name + " 剥组件后仍有残留："
                    + string.Join("、", leftovers) + " —— 这些组件会在播放器里动这个物体"
                    + "（Rigidbody 会自由落体、PirateBase 会按战斗网格重摆设），必须清掉。");
            }

            // 接触阴影面片是半透明的，本路径的 G-buffer 没有混合，索性删掉。
            Transform contactShadow = crewRoot.transform.Find("Visual/ContactShadow");
            if (contactShadow == null)
                contactShadow = crewRoot.transform.Find("ContactShadow");
            if (contactShadow != null)
                Object.DestroyImmediate(contactShadow.gameObject);

            // 换材质：Body（圆台柱）→ 阵营色，Head（圆球）→ 木色。
            int swapped = 0;
            foreach (MeshRenderer renderer in crewRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name == "Body")
                {
                    renderer.sharedMaterial = bodyMaterial;
                    swapped++;
                }
                else if (renderer.name == "Head")
                {
                    renderer.sharedMaterial = headMaterial;
                    swapped++;
                }
            }

            if (swapped < 2)
            {
                Debug.LogError("[PixelartPilotSetup] 船员预制体的 Body/Head renderer 没有认全（只换了 "
                    + swapped + " 个）——本路径的材质没挂上，角色会以旧材质出现在试点里。"
                    + "请核对预制体层级是否仍为 Visual/BodyPivot/TorsoPivot/Body + HeadPivot/Head。");
            }

            LogCrewPlacement(crewRoot, name, feetPosition);
        }

        /// <summary>
        /// 角色摆放自检（每个角色打一行）。**为什么要打**：预制体实例化 + 剥组件这条路上，
        /// "材质换了但画面上没有"这类问题只能靠世界包围盒判断——是位置错了、缩放到 0 了，
        /// 还是 renderer 被禁用了，一行日志就分得清（光看图分不清）。
        /// </summary>
        static void LogCrewPlacement(GameObject crewRoot, string name, Vector3 feetPosition)
        {
            var report = new System.Text.StringBuilder();
            report.Append("[PixelartPilotSetup] 角色 ").Append(name)
                .Append("：意图脚底 y=").Append(feetPosition.y.ToString("0.###"));

            foreach (MeshRenderer renderer in crewRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                Bounds b = renderer.bounds;
                report.Append("\n    ").Append(renderer.name)
                    .Append(" 启用=").Append(renderer.enabled)
                    .Append(" 活动=").Append(renderer.gameObject.activeInHierarchy)
                    .Append(" 材质=").Append(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "<无>")
                    .Append(" 网格=").Append(renderer.GetComponent<MeshFilter>() != null
                        && renderer.GetComponent<MeshFilter>().sharedMesh != null
                        ? renderer.GetComponent<MeshFilter>().sharedMesh.name : "<无>")
                    .Append(" 世界包围盒 中心=").Append(b.center.ToString("0.###"))
                    .Append(" 尺寸=").Append(b.size.ToString("0.###"));
            }

            Debug.Log(report.ToString());
        }

        /// <summary>
        /// 新建/就地更新物体材质（幂等：重跑覆盖，常量表是唯一调色入口）。
        /// 只设"这个物体该长什么样"的逐物体参数；着色数学全在走 shader 全局的那一趟里。
        ///
        /// 【描边线宽为什么是 1】渲染篇 §5 的起步值是"RT 空间 1px"，旧链实测要 2px 的原因是
        /// **旧架构**"全分辨率渲染 → 3× 点降采"会把 1px 线欠采掉；本路径几何**直接渲进
        /// 低分辨率 RT**，1 低分辨率像素 = 1920 宽屏上 5 屏幕像素，1px 是实打实可见的。
        /// 所以这里**不要**把旧链的 2px 当基准搬过来。
        /// </summary>
        static Material EnsureMaterial(string name, Color albedo, float bandCount,
            float outlinePixels = 1.0f, int renderQueue = -1)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Shader shader = Shader.Find(PixelartPath.ObjectShaderName);
            if (shader == null)
            {
                Debug.LogError("[PixelartPilotSetup] 找不到 shader \"" + PixelartPath.ObjectShaderName
                    + "\"（编译失败？），材质未创建。");
                return null;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = shader;
            mat.SetColor("_BaseColor", albedo);
            mat.SetFloat("_MainLightLevel", bandCount);
            mat.SetFloat("_DitherMode", 0f);       // 0 = Bayer 4×4 矩阵；1 = v3 的 1-bit 密度图案
            mat.SetFloat("_DitherStrength", 0f);   // 默认关（v3 自己的材质默认也是 0）；出图时用 MPB 拨开对照
            mat.SetFloat("_NormalEdgeLevel", 0f);  // P4 接连通域后才有效

            // 反向壳描边（渲染篇 §5）：线宽单位 = 低分辨率像素；0 = 本物体不描边。
            // 墨色与 UI 令牌 INK 同色（3D/2D 描边同色统一）。
            mat.SetColor("_InkColor", HexGamma("120C14"));   // 比纯黑带一点紫（阴影里不发死）
            mat.SetFloat("_OutlinePixels", outlinePixels);

            // -1 = 用 shader 里的 Queue（Geometry = 2000）；大平面由调用方压到 1999。
            mat.renderQueue = renderQueue >= 0 ? renderQueue : 2000;

            // 挂上 v3 口径的密度图案（_DitherMode 拨到 1 即生效，不必重烘场景）。
            var pattern = AssetDatabase.LoadAssetAtPath<Texture2D>(DitherFolder + "/ToonDither_0.png");
            if (pattern != null)
            {
                mat.SetTexture("_DitherPattern", pattern);
                mat.SetFloat("_DitherPatternSize", pattern.width);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// 把场景写进 Build Settings（编辑器里 Play / 按名 LoadScene 用）。
        ///
        /// 【为什么走文本直改而不是 SerializedObject】`EditorBuildSettingsScene.guid` 在 2022.3 是
        /// <c>GUID</c> **结构体**而不是字符串——用 <c>SerializedProperty.stringValue</c> 读它直接抛
        /// "type is not a supported string value"，而异常发生在写完 path 之前，于是**插进去了半条脏记录**
        /// （实测：本脚本第一版把 ToonPilot 变成两条、新场景反而没进去，且只在日志里留一行异常）。
        /// 本仓既有装配脚本（`ToonPilotSetup`）对同一问题也是走 YAML 直改，这里沿用同一条路。
        ///
        /// 【实现口径】先找**最后一条 guid 行**（每个场景条目的末行），在它之后插入完整的三行条目；
        /// 幂等靠"文本里已有该 path 就跳过"；写完**读文件回验**并把条数打进日志——
        /// 注册这类"默默不生效"的活，不读回等于没做。
        /// 播放器出包不受本文件影响：`BuildScript` 是显式把场景集传给 <c>BuildPlayerOptions</c> 的。
        /// </summary>
        static void RegisterInBuildSettings(string scenePath)
        {
            AssetDatabase.ImportAsset(scenePath);   // 确保 .meta（guid）已生成

            string guid = AssetDatabase.AssetPathToGUID(scenePath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError("[PixelartPilotSetup] 场景 guid 解析失败：" + scenePath);
                return;
            }

            // File IO 必须绝对路径（batchmode 下相对路径按进程 CWD 解析）。
            string abs = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                Application.dataPath, "..", "ProjectSettings/EditorBuildSettings.asset")).Replace('\\', '/');

            string text;
            try
            {
                text = System.IO.File.ReadAllText(abs);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[PixelartPilotSetup] 读 EditorBuildSettings.asset 失败：" + e.Message);
                return;
            }

            if (!text.Contains("path: " + scenePath))
            {
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                System.Text.RegularExpressions.MatchCollection guids =
                    System.Text.RegularExpressions.Regex.Matches(text, @"guid: [0-9a-f]{32}");
                if (guids.Count == 0)
                {
                    Debug.LogError("[PixelartPilotSetup] EditorBuildSettings 里找不到任何场景条目，未注册。");
                    return;
                }

                System.Text.RegularExpressions.Match last = guids[guids.Count - 1];
                string entry = newline + "  - enabled: 1" + newline + "    path: " + scenePath
                    + newline + "    guid: " + guid;
                text = text.Insert(last.Index + last.Length, entry);
                System.IO.File.WriteAllText(abs, text);
            }

            // 读回验证。
            string[] verify = System.IO.File.ReadAllLines(abs);
            int registered = 0;
            bool found = false;
            foreach (string line in verify)
            {
                if (!line.TrimStart().StartsWith("path:", System.StringComparison.Ordinal))
                    continue;
                registered++;
                if (line.Contains(scenePath))
                    found = true;
            }

            if (!found)
                Debug.LogError("[PixelartPilotSetup] 注册回验失败：Build Settings 里没有 " + scenePath
                    + "（现有 " + registered + " 条）。请手工核对 ProjectSettings/EditorBuildSettings.asset。");
            else
                Debug.Log("[PixelartPilotSetup] Build Settings 注册回验通过：" + scenePath
                    + "，共 " + registered + " 条场景。");

            AssetDatabase.Refresh();
        }

        /// <summary>sRGB hex（Gamma 空间直存）→ Color，口径同 SceneArtPalette / ToonPilotSetup。</summary>
        static Color HexGamma(string hex)
        {
            return new Color(
                int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                1f);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
