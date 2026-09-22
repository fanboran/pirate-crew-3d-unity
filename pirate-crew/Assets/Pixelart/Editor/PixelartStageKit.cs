using System.IO;
using PirateCrew.Rendering.Pixelart;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;   // AmbientMode（RenderSettings.ambientMode 的枚举）

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 像素化路径**试点场景装配的共用件**：物体材质配方 / 船员摆放 / Build Settings 登记。
    ///
    /// 【为什么要有这一份】这条路径现在有两个试点场景——`PixelartPilot`（图元几何，验证机制）
    /// 与 `PixelartCloud`（云彩关 L01 真实内容，验证"这一套管线的观感用在真关卡上是什么样"）。
    /// 两者必须**用同一份材质配方与同一套船员摆放口径**，否则"云场画面和试点画面不一样"时
    /// 分不清是内容差异还是装配差异。本仓已经为"两处各写一份常量"付过学费
    /// （`PixelartPilotScene` 的类头：场景写 35.264°、出图脚本写 30°，那张对照图根本不是同一机位），
    /// 故材质/摆放/登记三件事都只留这一个实现。
    ///
    /// 【材质是这条路径的硬约束，不是"建议统一"】物体 pass 用 <c>DrawRenderers</c> 按层拉全部
    /// 不透明物体，**不按 shader 过滤**：场景里只要有一个 renderer 挂着别的 shader（它不会写
    /// 7 张 MRT），那张像素的其余 MRT 通道就是未定义内容——表现是"局部花屏 / 莫名颜色"，
    /// 且不会有任何报错。所以装配器收尾必须跑 <see cref="AssertObjectShaderOnly"/>。
    /// </summary>
    public static class PixelartStageKit
    {
        /// <summary>本路径的材质目录（装配器创建，调色板种子也从这里扫）。</summary>
        public const string MaterialFolder = "Assets/Pixelart/Materials";

        /// <summary>抖动图案目录（v3 的 1-bit 密度图案）。</summary>
        public const string DitherFolder = "Assets/Pixelart/Textures/Dither";

        /// <summary>
        /// 船员预制体（角色几何的唯一来源）。用它是为了**试点的角色就是游戏里的角色**——
        /// 造型早有裁决（两件式 = 圆球 + 圆台柱，见 `CrewVisualPrefabBuilder` 类头），
        /// 自己拼图元必然走样（试过：方块 + 帽檐平板，被连着驳回两次）。
        /// </summary>
        public const string CrewPrefabPath = "Assets/Prefabs/PirateCrew/Crew/Sailor.prefab";

        /// <summary>
        /// 预制体根原点到脚底的距离（米）。**不是猜的**：根缩放 y 0.5 × Visual.localPosition.y −0.5
        /// = −0.25，Body 圆台柱底面正落在世界 −0.25 ⇒ 根原点在脚底上方 0.25。
        /// 于是"脚底落在 surfaceY" = 实例根放在 <c>surfaceY + 0.25</c>。总高 1.85m。
        /// </summary>
        public const float CrewRootToFeetOffset = 0.25f;

        // ------------------------------------------------------------------
        // 共用配色（两个场景同一份）
        // ------------------------------------------------------------------

        /// <summary>
        /// 红队衣色（像素化口径）。**与 <c>CrewVisualCatalog.TeamRed</c> #FF3A29 不同**——
        /// 那是旧链（PirateToon + 描边壳）的原色；本路径经 3 档色带量化，原色会顶到最亮档、
        /// 与高光/描边挤在一起，故取一档更柔的红（实测观感见 r5 归档）。
        /// </summary>
        public static Material CrewRed() => EnsureMaterial("PixelartCrew_Red", Hex("#DE524D"), 3f);

        /// <summary>蓝队衣色（像素化口径；见 <see cref="CrewRed"/> 的说明）。</summary>
        public static Material CrewBlue() => EnsureMaterial("PixelartCrew_Blue", Hex("#598CD9"), 3f);

        /// <summary>球头木色 = 船员自己的木色 <c>#D4A76A</c>（与 `CrewVisualPrefabBuilder` 同值）。</summary>
        public static Material CrewHead() => EnsureMaterial("PixelartCrew_Head", Hex("#D4A76A"), 3f);

        // ------------------------------------------------------------------
        // 材质配方
        // ------------------------------------------------------------------

        /// <summary>
        /// 新建/就地更新物体材质（幂等：重跑覆盖，常量表是唯一调色入口）。
        /// 只设"这个物体该长什么样"的逐物体参数；着色数学全在走 shader 全局的那一趟里。
        ///
        /// 【描边线宽为什么是 1】渲染篇 §5 的起步值是"RT 空间 1px"，旧链实测要 2px 的原因是
        /// **旧架构**"全分辨率渲染 → 3× 点降采"会把 1px 线欠采掉；本路径几何**直接渲进
        /// 低分辨率 RT**，1 低分辨率像素在 1920 宽屏上是 3 屏幕像素，1px 是实打实可见的。
        /// 所以这里**不要**把旧链的 2px 当基准搬过来。
        /// </summary>
        public static Material EnsureMaterial(string name, Color albedo, float bandCount,
            float outlinePixels = 1.0f)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Shader shader = Shader.Find(PixelartPath.ObjectShaderName);
            if (shader == null)
            {
                Debug.LogError("[PixelartStageKit] 找不到 shader \"" + PixelartPath.ObjectShaderName
                    + "\"（编译失败？），材质 " + name + " 未创建。");
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
            // ---- 连通域两项（Shape 缓冲的 b/a 通道）----
            // 法线边加成：连通域判出"单元内法线差超阈值"时给该像素加档（内部转折提亮）。
            // 单位是**色带步长**（着色里 `ndotl += singleLevel * 本值`），0.5 = 加半档。
            mat.SetFloat("_NormalEdgeLevel", 0.5f);
            mat.SetFloat("_NormalEdgeThreshold", 0.5f);
            // AA 缩放：连通域降档的门控乘数，1 = 不缩放（v3 的 _AAScale 同义）。
            mat.SetFloat("_AAScale", 1f);

            // ---- 高光（Specular 那一趟的输入，Physical 缓冲的 r/g）----
            // 数值取得保守（高光一重就压过色带）。
            mat.SetFloat("_Smoothness", 0.25f);
            mat.SetFloat("_Metallic", 0f);

            // ---- 逐物体边缘光色 ----
            // 默认**黑 = 本物体不出边缘光**（不改变已认过的画面）。要验证这条通路，
            // 出图脚本里有一档 `pa-rim` 走 MaterialPropertyBlock 临时拨亮（见 PlayerArtCapture）。
            mat.SetColor("_RimLightColor", Color.black);

            // ---- 描边开关 ----
            // >0 ⇒ 本物体的 applyOutline 置位（艺术画布上那一趟屏幕空间膨胀读它决定给谁出线）。
            // 线宽固定 1 艺术像素，由那一趟保证；这里只有开/关。
            mat.SetFloat("_OutlinePixels", outlinePixels);

            // ---- 物体级像素吸附（v3 CommonPass 的第二层，默认开）----
            mat.SetFloat("_SnapToPixelGrid", 1f);

            // 队列：几何只靠深度测试分前后（描边改屏幕空间后不再与队列有关），一律 Geometry。
            mat.renderQueue = 2000;

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
        /// 从**关卡自己的材质**派生一个本路径材质：取原材质的 <c>_BaseColor</c>（没有就 <c>_Color</c>）
        /// 作 albedo，其余按本路径的配方走。资产落在 `Assets/Pixelart/Materials/PixelartDerived_<原名>.mat`
        /// （幂等：重跑覆盖）。
        ///
        /// 【为什么要有派生档】关卡 2/3 的岛体有十几个材质槽（岩三档 / 草三档 / 土 / 石 / 木 / 水 / 沫 /
        /// 云 / 晶 / 光 / 旗），逐个在手写表里挑色号既慢又会让"岛还是那座岛的颜色"这件事失去保证。
        /// 派生保证**色相与明度关系取自关卡自身**（草比岩亮、岩暗档比亮档暗），观感差异只来自
        /// 本路径的色带量化——这正是要看的东西。
        ///
        /// 【与"不许静默"的边界】派生本身不静默：每个材质打一行"从 X 派生，取色 #RRGGBB"；
        /// 拿不到色值时**报错并返回 null**（而不是拿白色顶上）；装配收尾还有
        /// <see cref="AssertObjectShaderOnly"/> 保证没有漏网的旧 shader。
        /// </summary>
        public static Material EnsureDerivedMaterial(Material source, string logTag)
        {
            if (source == null)
                return null;

            // 取色优先 _BaseColor（URP Lit / PirateSurface 都写它），
            // 其次 _Color（内置着色器），最后 **_BaseColorA**——本仓的 `PirateCrew/PirateSurface`
            // 把三档色写成 `_BaseColorA/B/C`（`LowpolyStageBuilder.ColorOf` 的落点），
            // 空岛的岩/草/土/石/木九个槽位都在这一档上（实测：只看 _BaseColor 时它们全部"取不到色"，
            // 报错 9 行、材质一个都没换——这类"字段名选错⇒整类材质落空"是这一层的典型坑）。
            Color albedo;
            if (source.HasProperty("_BaseColor"))
                albedo = source.GetColor("_BaseColor");
            else if (source.HasProperty("_Color"))
                albedo = source.GetColor("_Color");
            else if (source.HasProperty("_BaseColorA"))
                albedo = source.GetColor("_BaseColorA");
            else
            {
                Debug.LogError(logTag + " 材质 \"" + source.name
                    + "\" 既没有 _BaseColor / _Color 也没有 _BaseColorA，无法派生——"
                    + "请手工在映射表里给它一条。");
                return null;
            }

            string name = "PixelartDerived_" + source.name;
            Material derived = EnsureMaterial(name, albedo, 3f);
            if (derived != null)
            {
                Debug.Log(logTag + " 派生材质：" + name + " ← 原材质 \"" + source.name
                    + "\" 的取色 #" + ColorUtility.ToHtmlStringRGB(albedo) + "。");
            }

            return derived;
        }

        /// <summary>
        /// 把一整棵子树（含预制体实例）上的 renderer 全部换成**按材质名映射**的本路径材质，
        /// 并按 <see cref="AssertObjectShaderOnly"/> 的判据收口。
        ///
        /// 【为什么必须逐 renderer 换而不是"给预制体换一次"】预制体是共享资产：直接改它的
        /// 材质会把主战斗场景（旧链）一起改掉——那正是"读 prefab → 装配 → 换材质"这条路的
        /// 唯一正确粒度。返回换掉的 renderer 数，供装配器打日志与断言。
        /// </summary>
        public static int SwapMaterials(
            GameObject root,
            System.Collections.Generic.Dictionary<string, Material> byOriginalMaterialName,
            string logTag,
            bool deriveMissing = false)
        {
            int swapped = 0;
            int unmapped = 0;

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material original = renderer.sharedMaterial;
                string key = original != null ? original.name : string.Empty;
                Material replacement;
                if (original != null && byOriginalMaterialName.TryGetValue(key, out replacement))
                {
                    renderer.sharedMaterial = replacement;
                    swapped++;
                }
                else if (deriveMissing && original != null)
                {
                    // 【派生档】关卡 2/3 的岛体有十几个材质槽（岩/草/土/木/水/云/晶…），逐个手挑色号
                    // 既慢又容易漂；这里从**关卡自己的材质色**派生（创始人 2026-09-22 对帧级调色板
                    // 的同一条口径："从场景材质色自动生成"）。派生是显式的：每个材质一行日志说清
                    // "从谁派生、取了什么色"，装配完还有 AssertObjectShaderOnly 兜底。
                    Material derived = EnsureDerivedMaterial(original, logTag);
                    if (derived != null)
                    {
                        renderer.sharedMaterial = derived;
                        swapped++;
                    }
                    else
                    {
                        unmapped++;
                    }
                }
                else
                {
                    unmapped++;
                    Debug.LogError(logTag + " 材质未登记：物体 \"" + renderer.name + "\" 挂的是 \""
                        + (original != null ? original.name : "<无>") + "\"（不在映射表里）——"
                        + "它不会写 7 张 G-buffer，那一带像素的 MRT 通道是未定义内容（花屏且无报错）。"
                        + "要么在本装配器的映射表里补一条，要么用 deriveMissing: true 走派生档。");
                }
            }

            Debug.Log(logTag + " 材质换装：" + swapped + " 个 renderer 已换成本路径材质"
                + (unmapped > 0 ? "，" + unmapped + " 个未登记（见上面的报错）" : "，无未登记项") + "。");
            return swapped;
        }

        /// <summary>
        /// 断言场景里**所有启用的 renderer 都挂本路径的物体 shader**。
        ///
        /// 这条是硬门禁：物体 pass 按层拉全部不透明物体、不按 shader 过滤，混进一个旧 shader 的
        /// renderer 就是"那片像素花屏，一行报错都没有"。装在装配期，出图前就红。
        /// </summary>
        public static void AssertObjectShaderOnly(GameObject root, string logTag)
        {
            int total = 0;
            int foreign = 0;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material mat = renderer.sharedMaterial;
                string shaderName = mat != null && mat.shader != null ? mat.shader.name : "<无>";
                total++;
                if (shaderName != PixelartPath.ObjectShaderName)
                {
                    foreign++;
                    Debug.LogError(logTag + " 异物 shader：\"" + renderer.name + "\" 挂的是 \""
                        + shaderName + "\"（应为 " + PixelartPath.ObjectShaderName + "）——"
                        + "它不进本路径的 G-buffer，会留下未定义的 MRT 内容。");
                }
            }

            if (foreign == 0)
                Debug.Log(logTag + " 材质口径通过：" + total + " 个 renderer 全部是本路径物体 shader。");
            else
                Debug.LogError(logTag + " 有 " + foreign + "/" + total + " 个 renderer 不属于本路径。");
        }

        // ------------------------------------------------------------------
        // 图元与光照（两个场景同一份）
        // ------------------------------------------------------------------

        /// <summary>建一个内置图元（**剥掉自带 Collider**：本路径只画几何，物理不参与这个试点）。</summary>
        public static GameObject NewPrimitive(PrimitiveType type, string name, Transform parent,
            Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>图元方块（位置/尺寸写在 transform 上）。</summary>
        public static void AddBox(Transform parent, string name, Vector3 position, Vector3 scale,
            Material material)
        {
            GameObject go = NewPrimitive(PrimitiveType.Cube, name, parent, material);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
        }

        /// <summary>
        /// 建太阳 + 环境光，返回太阳（装配器把它接给 <c>PixelartCameraRig.sun</c>）。
        ///
        /// 【太阳高度 58° 是算出来的，不是随手给的】3 档色带下"顶面 / 朝光立面 / 背光立面"
        /// 必须落进三个不同档，否则**台面会读成一块平面**（实测：仰角 48° 时顶面与朝光立面的
        /// ndotl 只差 0.23、量化后同档 ⇒ 所有台面糊成一整块菱形）。58° 时三者 ≈ 0.85 / 0.41 / 0.0，
        /// 恰好三档。方位角沿用本仓"左上光"惯例 140°。
        ///
        /// 【实时阴影：机制接通、场景暂时关掉】机制侧已经齐（Cast 相机 <c>renderShadows = true</c>、
        /// 着色四趟的 shadow 关键字族与 <c>GetMainLight(shadowCoord)</c>、URP 资产的开关）。
        /// 但出图实测：**铺满画面的大平面整片被判在阴影里**——判据是解析式核对过的（地面 albedo
        /// 实测色 ≈ 纯环境项 albedo × 环境色的 sRGB 值，而台面顶面是受光的）⇒ 不是"没画"而是
        /// "阴影贴图自遮挡"。那是**阴影覆盖范围/偏移量级的调参问题**（160m 大平面 + 正交 cast 相机 +
        /// 阴影距离），不是架构问题；调准之前先关掉，避免整屏发暗掩盖其它机制的验收。
        /// 打开它：把这里改成 <c>LightShadows.Soft</c>，并同步调 URP 资产的阴影距离/级联与光源 bias。
        ///
        /// 【环境光 = 本路径的"暗部色"】口径：渲染篇 §4.6"单平行光 + 单环境光"、蓝本 §7.2 第 5 条
        /// "GI 用扁平环境光"（v3 的暗面来自 SH）。**不能给太小**：旧链取 #1A1E28 是有配套的
        /// （它的暗部是材质上的**替换式暗部色** `_ShadowColor`），本路径暗面 = albedo × 环境色，
        /// 压到 #1A1E28 时暗面与墨线几乎同值、轮廓线和暗面糊成一片。**也不能给太大**：
        /// 加法环境光会抬平明暗跨度。#3A4760 让暗面落在 sRGB 0.2 上下——明显暗于亮面、又明显亮于墨线。
        /// 这一条是**美术旋钮**（改这里即整体调暗部亮度），不是机制。
        /// </summary>
        public static Light CreateSunAndAmbient(Transform parent)
        {
            var sunGo = new GameObject("PixelartSun");
            sunGo.transform.SetParent(parent);
            sunGo.transform.rotation = Quaternion.Euler(58f, 140f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Hex("FFF5E0");
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.None;
            sun.shadowStrength = 0.65f;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Hex("3A4760");
            return sun;
        }

        // ------------------------------------------------------------------
        // 船员摆放
        // ------------------------------------------------------------------

        /// <summary>
        /// 摆一个船员：**直接用游戏里那个船员预制体的几何**（只换材质），脚底落在
        /// <paramref name="feetPosition"/>。
        ///
        /// 【为什么要剥组件】船员预制体的根上带 **Rigidbody + BoxCollider**。
        /// 编辑态不跑物理 ⇒ 编辑器里量到的世界包围盒完全正确（脚底 y=1.0），
        /// 进播放器后 Rigidbody 带重力自由落体，而采集发生在载入后约 1.5 秒，
        /// 下落距离 ½·9.81·1.5² ≈ **11m** —— 与实测"角色比场景低 11.441m、被地面挡住看不见"逐位对上。
        /// 症状是"编辑器里对、播放器里没有角色"，且一行报错都没有。
        /// ⇒ 口径是**白名单**（只留 Transform / MeshFilter / MeshRenderer），并且**多轮清扫 + 残留断言**。
        /// </summary>
        public static GameObject PlaceCrew(Transform parent, string name, Vector3 feetPosition,
            float yawDegrees, Material bodyMaterial, Material headMaterial, string logTag)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrewPrefabPath);
            if (prefab == null)
            {
                Debug.LogError(logTag + " 找不到船员预制体 " + CrewPrefabPath
                    + "（跑过一次 PirateCrew/角色/生成职业视觉预制体 吗？），角色 " + name + " 未摆放。");
                return null;
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
                Debug.LogError(logTag + " 角色 " + name + " 剥组件后仍有残留："
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
                Debug.LogError(logTag + " 船员预制体的 Body/Head renderer 没有认全（只换了 "
                    + swapped + " 个）——本路径的材质没挂上，角色会以旧材质出现在试点里。"
                    + "请核对预制体层级是否仍为 Visual/BodyPivot/TorsoPivot/Body + HeadPivot/Head。");
            }

            LogCrewPlacement(crewRoot, name, feetPosition, logTag);
            return crewRoot;
        }

        /// <summary>
        /// 角色摆放自检（每个角色打一行）。**为什么要打**：预制体实例化 + 剥组件这条路上，
        /// "材质换了但画面上没有"这类问题只能靠世界包围盒判断——是位置错了、缩放到 0 了，
        /// 还是 renderer 被禁用了，一行日志就分得清（光看图分不清）。
        /// </summary>
        static void LogCrewPlacement(GameObject crewRoot, string name, Vector3 feetPosition, string logTag)
        {
            var report = new System.Text.StringBuilder();
            report.Append(logTag).Append(" 角色 ").Append(name)
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

        // ------------------------------------------------------------------
        // Build Settings 登记
        // ------------------------------------------------------------------

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
        public static void RegisterScene(string scenePath, string logTag)
        {
            AssetDatabase.ImportAsset(scenePath);   // 确保 .meta（guid）已生成

            string guid = AssetDatabase.AssetPathToGUID(scenePath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError(logTag + " 场景 guid 解析失败：" + scenePath);
                return;
            }

            // File IO 必须绝对路径（batchmode 下相对路径按进程 CWD 解析）。
            string abs = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "ProjectSettings/EditorBuildSettings.asset")).Replace('\\', '/');

            string text;
            try
            {
                text = File.ReadAllText(abs);
            }
            catch (System.Exception e)
            {
                Debug.LogError(logTag + " 读 EditorBuildSettings.asset 失败：" + e.Message);
                return;
            }

            if (!text.Contains("path: " + scenePath))
            {
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                System.Text.RegularExpressions.MatchCollection guids =
                    System.Text.RegularExpressions.Regex.Matches(text, @"guid: [0-9a-f]{32}");
                if (guids.Count == 0)
                {
                    Debug.LogError(logTag + " EditorBuildSettings 里找不到任何场景条目，未注册。");
                    return;
                }

                System.Text.RegularExpressions.Match last = guids[guids.Count - 1];
                string entry = newline + "  - enabled: 1" + newline + "    path: " + scenePath
                    + newline + "    guid: " + guid;
                text = text.Insert(last.Index + last.Length, entry);
                File.WriteAllText(abs, text);
            }

            // 读回验证。
            string[] verify = File.ReadAllLines(abs);
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
            {
                Debug.LogError(logTag + " 注册回验失败：Build Settings 里没有 " + scenePath
                    + "（现有 " + registered + " 条）。请手工核对 ProjectSettings/EditorBuildSettings.asset。");
            }
            else
            {
                Debug.Log(logTag + " Build Settings 注册回验通过：" + scenePath
                    + "，共 " + registered + " 条场景。");
            }

            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------------
        // 小工具
        // ------------------------------------------------------------------

        /// <summary>
        /// sRGB hex（Gamma 空间直存）→ Color，口径同 SceneArtPalette / ToonPilotSetup。
        /// **带不带 `#` 都收**：文档里写色值几乎都带 `#`（`#DE524D`），装配代码里不带——
        /// 两种写法混用时 `int.Parse(HexNumber)` 会抛 FormatException 并把整条装配打断
        /// （实测踩过：`CrewRed` 在文档里抄了带 `#` 的写法，装配在材质那一步整条挂掉，
        /// 报错是 `FormatException: Input string was not in a correct format`）。
        /// </summary>
        public static Color Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                Debug.LogError("[PixelartStageKit] Hex 收到空串，返回白色占位。");
                return Color.white;
            }

            if (hex[0] == '#')
                hex = hex.Substring(1);

            return new Color(
                int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                1f);
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
