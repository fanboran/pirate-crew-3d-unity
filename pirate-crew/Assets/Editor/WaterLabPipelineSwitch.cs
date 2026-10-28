using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using PirateCrew.Core;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// WaterLab 场景级渲染管线切换器（B 流管线件）：当前活跃场景是
    /// <see cref="SceneNames.WaterLab"/> 时把现役管线切到
    /// <c>Assets/Settings/URP/WaterLab_URPAsset.asset</c>（HDR on / MSAA off 副本，
    /// 差异登记见该资产文件头），离开/切走时**无条件还原**进入前的原值。
    ///
    /// 【为什么要场景级切换】主战役 QualitySettings 六档全部绑各自管线资产
    /// （当前档 Ultra = PC_Balanced_URPAsset），URP 管线资产是全局单例生效；
    /// WaterLab 的 HDR 定案（E/R 流：色缓冲 B10G11R11_UFloatPack32、运行时无 MSAA）
    /// 不能改 Balanced 本体（主战役绝不泄漏是本件门禁），只能进场景临时换、离开还原。
    ///
    /// 【生效路径】
    /// - 编辑器开/切场景：EditorApplication.sceneOpened（Single/Additive 均覆盖）；
    /// - 进/出 Play：playModeStateChanged 后按当前活跃场景重新裁决（状态跟随场景，
    ///   永远不残留）；
    /// - batchmode 出图（-executeMethod WaterLabSetup.BuildSceneAndCapture）：无
    ///   sceneOpened 也保证生效——BuildScene 在场景保存为 WaterLab.unity 后显式
    ///   <see cref="Apply"/>，且 NewScene 触发的 sceneOpened(Untitled) 会先走
    ///   <see cref="Restore"/>（幂等，无泄漏窗口）。
    ///
    /// 【还原双保险】还原路径有三处独立触发：场景切走 / 退出 Play、
    /// EditorApplication.quitting、AssemblyReloadEvents.beforeAssemblyReload（域重载
    /// 前兜底——QualitySettings.renderPipeline 的修改只存在于会话内，除非有人
    /// 显式保存项目；三处兜底把「切走忘了还原」的窗口压到最小）。
    ///
    /// 【断言】Apply/Restore 后对现役管线（QualitySettings 档位资产优先、
    /// 落 default 的解析序，见 ActivePipeline）断言：
    /// 进 = 副本在位、出 = 副本不在位；还原失败直接 LogError（门禁可断言）。
    /// WaterLab 场景不进 Build Settings（编辑器验证专用），运行时打包场景无需
    /// RuntimeInitializeOnLoad 钩子；将来若进包需另加运行时入口。
    /// </summary>
    [InitializeOnLoad]
    public static class WaterLabPipelineSwitch
    {
        /// <summary>WaterLab 管线副本资产路径（HDR on / MSAA off，见资产文件头差异表）。</summary>
        const string WaterLabAssetPath = "Assets/Settings/URP/WaterLab_URPAsset.asset";

        /// <summary>进入 WaterLab 前的现役管线（Restore 的唯一凭据；null = 不在 WaterLab 态）。</summary>
        static RenderPipelineAsset s_Stored;

        static WaterLabPipelineSwitch()
        {
            // sceneOpened 挂 EditorSceneManager（EditorApplication.sceneOpened 是
            // 2022 拆模块后的 CoreModule 成员，无头编译台 external/harness 绑定的
            // Data/Managed 兼容 shim 里没有——同签名事件在 EditorSceneManager 上
            // 2017.2 起就有，语义一致）。
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += Restore;
            AssemblyReloadEvents.beforeAssemblyReload += Restore;
        }

        static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            // 只按 Single 主场景裁决；Additive 加载的子场景不改变主场景归属。
            if (mode != OpenSceneMode.Single)
                return;
            Reconcile(IsWaterLab(scene));
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // 只在进入态稳定后裁决一次（Entered*）；Exiting* 阶段场景还在卸载中。
            if (state != PlayModeStateChange.EnteredEditMode
                && state != PlayModeStateChange.EnteredPlayMode)
                return;
            Reconcile(IsWaterLab(SceneManager.GetActiveScene()));
        }

        /// <summary>状态机唯一入口：inWaterLab=true 保 Apply、false 保 Restore（均幂等）。</summary>
        static void Reconcile(bool inWaterLab)
        {
            if (inWaterLab) Apply();
            else Restore();
        }

        static bool IsWaterLab(Scene scene)
        {
            var path = SceneNames.PathOf(SceneNames.WaterLab);
            return scene.path == path || scene.name == SceneNames.WaterLab;
        }

        /// <summary>
        /// 切到 WaterLab 副本（幂等）。首次调用记录现役管线为还原凭据；
        /// batchmode 出图链由 <see cref="WaterLabSetup.BuildScene"/> 在场景保存后调用。
        /// </summary>
        public static void Apply()
        {
            var waterLab = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(WaterLabAssetPath);
            if (waterLab == null)
            {
                Debug.LogError($"[WaterLabPipelineSwitch] 找不到管线副本：{WaterLabAssetPath}（不入库无法切换）");
                return;
            }

            // 本工程六档 QualitySettings 全绑管线资产 → 走 QualitySettings 覆盖路径；
            // 纯 default 管线工程（QualitySettings.renderPipeline==null）走 GraphicsSettings。
            if (QualitySettings.renderPipeline == waterLab)
            {
                // 已经是副本（重复 Apply）：幂等返回；若此刻无还原凭据（异常态），
                // Restore 的凭据丢失兜底会把它摘下，此处不臆造凭据。
                LogApplied(waterLab, resumed: true);
                return;
            }

            if (s_Stored == null)
            {
                s_Stored = QualitySettings.renderPipeline != null
                    ? QualitySettings.renderPipeline
                    : GraphicsSettings.defaultRenderPipeline;
            }

            if (QualitySettings.renderPipeline != null)
                QualitySettings.renderPipeline = waterLab;
            else
                GraphicsSettings.defaultRenderPipeline = waterLab;

            AssertActive(waterLab, expect: true);
            LogApplied(waterLab, resumed: false);
        }

        /// <summary>
        /// 还原进入前的管线（幂等；无还原凭据时是 no-op）。三处兜底触发点见类注释。
        /// 还原后断言现役管线不再是 WaterLab 副本——失败即 LogError（门禁项）。
        /// </summary>
        public static void Restore()
        {
            if (s_Stored == null && ActivePipeline == null)
                return;

            var waterLab = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(WaterLabAssetPath);
            if (s_Stored == null)
            {
                // 无凭据但现役是副本（异常态，如域重载丢静态）：至少摘下副本防泄漏。
                if (waterLab != null && ActivePipeline == waterLab)
                {
                    Debug.LogError("[WaterLabPipelineSwitch] 还原凭据丢失但现役仍是 WaterLab 副本，" +
                        "回退 QualitySettings 首个非副本管线（请检查 QualitySettings.asset 是否被保存污染）");
                    FallbackRestore(waterLab);
                }
                return;
            }

            if (QualitySettings.renderPipeline != null || GraphicsSettings.defaultRenderPipeline == waterLab)
            {
                if (QualitySettings.renderPipeline != null)
                    QualitySettings.renderPipeline = s_Stored;
                else
                    GraphicsSettings.defaultRenderPipeline = s_Stored;
            }

            var restored = s_Stored;
            s_Stored = null;
            AssertActive(waterLab, expect: false);
            Debug.Log($"[WaterLabPipelineSwitch] 离开 WaterLab：管线已还原 → {restored.name}（断言通过：现役非副本）");
        }

        /// <summary>凭据丢失时的兜底：default 管线若非副本则顶上（无逐档读回 API）。</summary>
        static void FallbackRestore(RenderPipelineAsset waterLab)
        {
            var def = GraphicsSettings.defaultRenderPipeline;
            if (def != null && def != waterLab && QualitySettings.renderPipeline != null)
                QualitySettings.renderPipeline = def;
        }

        /// <summary>
        /// 现役管线（Unity 解析序：QualitySettings 档位管线优先，null 落
        /// GraphicsSettings.defaultRenderPipeline）。
        /// 【为什么不读 QualitySettings.activeRenderPipeline】同上：该成员是拆模块
        /// 后的 API，无头编译台绑定的兼容 shim 没有；本工程六档全绑管线资产，
        /// 两级写法覆盖 Unity 的真实解析序。
        /// </summary>
        static RenderPipelineAsset ActivePipeline =>
            QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline;

        /// <summary>门禁断言：现役管线必须是（expect=true）/ 不得是（expect=false）WaterLab 副本。</summary>
        static void AssertActive(RenderPipelineAsset waterLab, bool expect)
        {
            var active = ActivePipeline;
            bool ok = expect ? active == waterLab : active != waterLab;
            if (!ok)
                Debug.LogError($"[WaterLabPipelineSwitch] 断言失败：expect={(expect ? "副本在位" : "副本不在位")}，" +
                    $"现役={active?.name ?? "null"}");
            Debug.Assert(ok, "[WaterLabPipelineSwitch] 现役管线断言失败（见 LogError 详情）");
        }

        static void LogApplied(RenderPipelineAsset waterLab, bool resumed)
        {
            Debug.Log($"[WaterLabPipelineSwitch] {(resumed ? "已在" : "进入")} WaterLab：" +
                $"现役管线 = {waterLab.name}（HDR on→B10G11R11 / MSAA off；" +
                $"还原凭据 = {s_Stored?.name ?? "无（重复进入）"}）");
        }
    }
}
