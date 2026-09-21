using System.Collections.Generic;
using PirateCrew.Battle;
using PirateCrew.UI;
using PirateCrew.Water;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 运行期查找清退的**接线补齐器**：把"原来是运行时 <c>FindObjectOfType</c> 兜底"的三条依赖
    /// 改成场景里的显式 <c>[SerializeField]</c> 连线。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/接线/写入 Battle 运行期查找接线
    ///   无头: -batchmode -nographics -quit -projectPath &lt;P&gt; \
    ///         -executeMethod PirateCrew.EditorTools.BattleLookupWiring.Wire -logFile -
    ///   只读校验（CI 用，缺接线或字段名漂移时退出码 1）：
    ///         -executeMethod PirateCrew.EditorTools.BattleLookupWiring.Verify
    ///
    /// 【前置 / 顺序】必须在 <see cref="M2BattleSceneSetup.BuildAll"/> **之后**跑
    /// （BuildAll 会 <c>NewScene(EmptyScene)</c> 全量重建，把这份接线抹掉），与
    /// <see cref="HudMinimapSceneSetup.WireMinimap"/> 同级——两者都可在 BuildAll 之后任意顺序执行。
    ///
    /// 【为什么单独一个脚本】<c>M2BattleSceneSetup.cs</c> / <c>HudMinimapSceneSetup.cs</c> 不在本
    /// 轨道文件域内，不能改；本脚本复用它们的代码模式（<c>SerializedObject</c> 写私有
    /// <c>[SerializeField]</c>、不手写 .unity YAML、不新增对象），只补字段值。
    ///
    /// 【幂等】只写字段：值相同就不改也不标脏；字段名漂移（<c>FindProperty</c> 为 null）会报错并
    /// 计入失败，而不是静默跳过——字段名是本脚本与被接线脚本之间的唯一契约。
    ///
    /// 【接哪三条】三条都是"场景内对象 → 场景内对象"，与 docs/审计/专项/场景接线审计报告.md F-3 的
    /// 修法一致：
    ///   · <c>BattleCameraController.aimThrow</c> ← 根对象 <c>AimThrowController</c>
    ///     （**热路径**：Update 的力度-镜头耦合、LateUpdate 的 Scope 视野混合每帧读它）；
    ///   · <c>BattleHud.cameraController</c> ← 根对象 <c>BattleCameraController</c>
    ///     （观察模式开关要转交给相机）；
    ///   · <c>WaterSimulationDriver.sunLight</c> ← <c>AmbientDirector.sunLight</c>
    ///     （水面太阳光路方向；解析顺序 <c>RenderSettings.sun</c> → 本字段 → 静态正午方向）。
    /// </summary>
    public static class BattleLookupWiring
    {
        const string BattleScenePath = "Assets/Scenes/Battle.unity";

        /// <summary>一条"目标组件字段 ← 源对象"的接线描述。</summary>
        struct WireAssignment
        {
            public string TargetField;
            public Object Value;
        }

        [MenuItem("PirateCrew/接线/写入 Battle 运行期查找接线")]
        public static void Wire()
        {
            Scene scene;
            if (!TryOpenBattleScene(out scene))
            {
                Finish(1);
                return;
            }

            var problems = new List<string>();
            var applied = new List<string>();

            var aim = FindInScene<AimThrowController>(scene);
            var battleCamera = FindInScene<BattleCameraController>(scene);
            var hud = FindInScene<BattleHud>(scene);
            var water = FindInScene<WaterSimulationDriver>(scene);
            var ambient = FindInScene<Ambient.AmbientDirector>(scene);

            if (battleCamera == null)
                problems.Add("场景里没有 BattleCameraController——请先跑 M2BattleSceneSetup.BuildAll。");
            if (hud == null)
                problems.Add("场景里没有 BattleHud——请先跑 M2BattleSceneSetup.BuildAll。");

            if (battleCamera != null)
                ApplyWires(battleCamera, new[]
                {
                    new WireAssignment { TargetField = "aimThrow", Value = aim },
                }, applied, problems);

            if (hud != null)
                ApplyWires(hud, new[]
                {
                    new WireAssignment { TargetField = "cameraController", Value = battleCamera },
                }, applied, problems);

            if (water != null)
            {
                // 太阳优先从 AmbientDirector 抄（它才是场景主光的权威持有者）；它缺失时退到根对象
                // 名字为 Directional Light 的那盏灯（BuildAll 建的那盏）。
                Light sun = ambient != null ? ReadSunLight(ambient) : null;
                if (sun == null)
                    sun = FindLightByName(scene, "Directional Light");
                ApplyWires(water, new[]
                {
                    new WireAssignment { TargetField = "sunLight", Value = sun },
                }, applied, problems);
            }

            bool changed = EditorSceneManager.MarkSceneDirty(scene);
            if (changed && !EditorSceneManager.SaveScene(scene, BattleScenePath))
                problems.Add("保存场景失败: " + BattleScenePath);

            for (int i = 0; i < applied.Count; i++)
                Debug.Log("[BattleLookupWiring] " + applied[i]);
            for (int i = 0; i < problems.Count; i++)
                Debug.LogError("[BattleLookupWiring] " + problems[i]);

            Finish(problems.Count > 0 ? 1 : 0);
        }

        /// <summary>
        /// 只读校验（不改场景、不保存）：三条接线是否都已写入。CI / 主控门禁用。
        /// 有缺失时 <c>Debug.LogError</c> + 批处理退出码 1。
        /// </summary>
        [MenuItem("PirateCrew/接线/校验 Battle 运行期查找接线")]
        public static void Verify()
        {
            Scene scene;
            if (!TryOpenBattleScene(out scene))
            {
                Finish(1);
                return;
            }

            var problems = new List<string>();
            var ok = new List<string>();

            Check<BattleCameraController>(scene, "aimThrow", ok, problems);
            Check<BattleHud>(scene, "cameraController", ok, problems);
            Check<WaterSimulationDriver>(scene, "sunLight", ok, problems);

            for (int i = 0; i < ok.Count; i++)
                Debug.Log("[BattleLookupWiring.Verify] 已接线 " + ok[i]);

            if (problems.Count == 0)
            {
                Debug.Log("[BattleLookupWiring.Verify] 三条运行期查找接线全部就位。");
                Finish(0);
                return;
            }

            for (int i = 0; i < problems.Count; i++)
                Debug.LogError("[BattleLookupWiring.Verify] " + problems[i]);
            Debug.LogError("[BattleLookupWiring.Verify] 修复：跑 "
                           + "PirateCrew.EditorTools.BattleLookupWiring.Wire（会写 " + BattleScenePath + "）。");
            Finish(1);
        }

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        static void ApplyWires(Component target, WireAssignment[] wires, List<string> applied, List<string> problems)
        {
            var so = new SerializedObject(target);
            bool dirty = false;

            for (int i = 0; i < wires.Length; i++)
            {
                SerializedProperty prop = so.FindProperty(wires[i].TargetField);
                if (prop == null)
                {
                    problems.Add(target.GetType().Name + " 找不到序列化字段 '" + wires[i].TargetField
                                 + "'（字段名漂移？被接线脚本改字段名后本脚本必须同步改）。");
                    continue;
                }

                if (prop.propertyType != SerializedPropertyType.ObjectReference)
                {
                    problems.Add(target.GetType().Name + "." + wires[i].TargetField + " 不是对象引用字段。");
                    continue;
                }

                if (wires[i].Value == null)
                {
                    problems.Add(target.GetType().Name + "." + wires[i].TargetField
                                 + " 的源对象在场景里找不到（无法接线）。");
                    continue;
                }

                if (prop.objectReferenceValue == wires[i].Value)
                {
                    applied.Add(target.GetType().Name + "." + wires[i].TargetField + " 已是 "
                                + wires[i].Value.name + "（无变化）");
                    continue;
                }

                prop.objectReferenceValue = wires[i].Value;
                dirty = true;
                applied.Add(target.GetType().Name + "." + wires[i].TargetField + " ← " + wires[i].Value.name);
            }

            if (dirty)
                so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Check<T>(Scene scene, string field, List<string> ok, List<string> problems)
            where T : Component
        {
            T target = FindInScene<T>(scene);
            if (target == null)
            {
                problems.Add("场景里没有 " + typeof(T).Name + "。");
                return;
            }

            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                problems.Add(typeof(T).Name + " 找不到序列化字段 '" + field + "'。");
                return;
            }

            if (prop.objectReferenceValue == null)
            {
                problems.Add(typeof(T).Name + "." + field + " 仍为空（运行时查找清退后该依赖会降级）。");
                return;
            }

            ok.Add(typeof(T).Name + "." + field + " = " + prop.objectReferenceValue.name);
        }

        static bool TryOpenBattleScene(out Scene scene)
        {
            scene = default;

            // 用 AssetDatabase 判存在，避免 batchmode 下依赖当前工作目录。
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BattleScenePath) == null)
            {
                Debug.LogError("[BattleLookupWiring] 找不到 " + BattleScenePath
                               + "，请先运行 PirateCrew.EditorTools.M2BattleSceneSetup.BuildAll。");
                return false;
            }

            scene = EditorSceneManager.OpenScene(BattleScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError("[BattleLookupWiring] 打开场景失败: " + BattleScenePath);
                return false;
            }

            return true;
        }

        /// <summary>按根对象逐层找组件（含失活）：编辑器脚本也不用 Object.FindObjectOfType。</summary>
        static T FindInScene<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T found = roots[i].GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>读 AmbientDirector 的主光（私有 <c>[SerializeField]</c>，只能经 SerializedObject 读）。</summary>
        static Light ReadSunLight(Component ambientDirector)
        {
            var so = new SerializedObject(ambientDirector);
            SerializedProperty prop = so.FindProperty("sunLight");
            return prop != null ? prop.objectReferenceValue as Light : null;
        }

        static Light FindLightByName(Scene scene, string objectName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name != objectName)
                    continue;

                Light light = roots[i].GetComponent<Light>();
                if (light != null)
                    return light;
            }

            return null;
        }

        static void Finish(int exitCode)
        {
            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }
    }
}
