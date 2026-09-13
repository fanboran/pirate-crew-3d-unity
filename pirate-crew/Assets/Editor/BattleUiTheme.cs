using PirateCrew.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 战斗 HUD 视觉主题钩子（波次 I4 填充）。
    ///
    /// M2BattleSceneSetup.BuildHud 在搭完 BattleCanvas 的全部子节点与接线之后调用
    /// <see cref="Apply"/>，把 Canvas 传进来。
    ///
    /// 【本波次实现】规范 §3.5 要求 HUD 大改（顶部信息条拆双方存活、武器面板改滚动列表、
    /// 名册行中文并提字号、全中文）。做法：
    ///   1. 清掉 Canvas 下除 <c>BattleHud</c> 控制器与 <c>MinimapPanel</c>（小地图接线复用）之外的旧 HUD 节点；
    ///   2. 交给 <see cref="BattleHudBuilder"/> 按 §3.5 线框图重建（木板 / 羊皮纸 / 黄铜材质语言）；
    ///   3. 用 <see cref="SerializedObject"/> 把 <see cref="BattleHud"/> 的
    ///      <c>[SerializeField]</c> 引用重新指向新节点（**接线口径不变**：17 武器槽 / 12 名册行 / 3 个命令按钮）。
    ///
    /// 【契约】只改外观与节点归属，不改 Canvas 的 RenderMode / CanvasScaler（1920×1080 match 0.5），
    /// 不新增/变更 EventBus 事件。可重复调用（每次场景重建都会调一次）。
    /// 【布局口径】外安全边距 24px、面板内边距 24px、底部提示条底距 16px（§1.7 近贴边例外）
    /// 且武器面板底缘与提示条顶边净间距 24px；回合/计时各带 120×36 木底板；名册面板高度随实际行数收缩
    /// （VerticalLayoutGroup + ContentSizeFitter）——具体坐标/控件常量集中在 <see cref="BattleHudBuilder"/>；
    /// 本类只负责清旧节点、重建、按字段名回写引用。
    /// 【保留节点】清旧节点时保留 Canvas 上的 <c>BattleHud</c> 与 <c>MinimapPanel</c>；
    /// 小地图的 <c>DotLayer/TileLayer/IslandLayer</c> 是 MinimapPanel 的子物体，随面板一并保留
    /// （IslandLayer 的沙/草烘焙由 <c>HudMinimapSceneSetup</c> 负责，本类不触碰）。
    /// </summary>
    public static class BattleUiTheme
    {
        /// <summary>HUD 控制器对象名（M2BattleSceneSetup 创建）。</summary>
        const string HudControllerName = "BattleHud";

        /// <summary>给 <paramref name="canvas"/>（BattleCanvas）换皮并重建 HUD。</summary>
        public static void Apply(GameObject canvas)
        {
            if (canvas == null)
            {
                Debug.LogWarning("[BattleUiTheme] canvas 为 null，跳过 HUD 主题应用。");
                return;
            }

            var hud = canvas.GetComponentInChildren<BattleHud>(true);
            ClearOldHudNodes(canvas.transform);

            BattleHudBuilder.Result result = BattleHudBuilder.Build(canvas);

            if (hud == null)
            {
                Debug.LogWarning("[BattleUiTheme] 未在 Canvas 下找到 BattleHud 组件，"
                    + "已重建 HUD 外观但未回写引用（HUD 逻辑可能不工作）。");
                return;
            }

            WireHud(hud, result);
        }

        /// <summary>清掉旧 HUD 节点；保留 BattleHud 控制器与小地图面板（后者由小地图接线脚本复用）。</summary>
        static void ClearOldHudNodes(Transform canvas)
        {
            for (int i = canvas.childCount - 1; i >= 0; i--)
            {
                Transform child = canvas.GetChild(i);
                if (child.name == HudControllerName || child.name == BattleHudBuilder.MinimapPanelName)
                    continue;

                Object.DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>把新节点回写给 BattleHud 的私有序列化字段（字段名与脚本一一对应）。</summary>
        static void WireHud(BattleHud hud, BattleHudBuilder.Result result)
        {
            var so = new SerializedObject(hud);

            SetRef(so, "turnHintText", result.turnHintText);
            SetRef(so, "teamStatusText", result.teamStatusText);
            SetRef(so, "weaponPanelRoot", result.weaponPanelRoot);
            SetRef(so, "weaponPanelTitle", result.weaponPanelTitle);
            SetRef(so, "rosterTitle", result.rosterTitle);
            SetRef(so, "throwSelfButton", result.throwSelfButton);
            SetRef(so, "endGoButton", result.endGoButton);
            SetRef(so, "backButton", result.backButton);

            SetArray(so.FindProperty("weaponButtons"), result.weaponButtons);
            SetArray(so.FindProperty("weaponLabels"), result.weaponLabels);
            SetRosterRows(so.FindProperty("rosterRows"), result.rosterRows);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetRef(SerializedObject so, string name, Object value)
        {
            SerializedProperty property = so.FindProperty(name);
            if (property == null)
            {
                Debug.LogWarning("[BattleUiTheme] BattleHud 找不到序列化字段: " + name + "（字段名漂移？）");
                return;
            }

            // 构建器漏建节点时 value 为 null，会让 HUD 静默失去该引用（返回按钮/回合提示等）；
            // 这里显式告警，避免「看着有 HUD，其实某块没接上」。
            if (value == null)
                Debug.LogWarning("[BattleUiTheme] BattleHud." + name + " 未接上（BattleHudBuilder 未产出该节点？）");

            property.objectReferenceValue = value;
        }

        static void SetArray(SerializedProperty arrayProperty, Object[] values)
        {
            if (arrayProperty == null || values == null)
                return;

            arrayProperty.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                arrayProperty.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static void SetRosterRows(SerializedProperty arrayProperty, BattleHud.RosterRowView[] rows)
        {
            if (arrayProperty == null || rows == null)
                return;

            arrayProperty.arraySize = rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                SerializedProperty element = arrayProperty.GetArrayElementAtIndex(i);
                SetRelative(element, "root", rows[i].root);
                SetRelative(element, "teamSwatch", rows[i].teamSwatch);
                SetRelative(element, "nameLabel", rows[i].nameLabel);
                SetRelative(element, "healthFill", rows[i].healthFill);
                SetRelative(element, "healthLabel", rows[i].healthLabel);
            }
        }

        static void SetRelative(SerializedProperty element, string childName, Object value)
        {
            SerializedProperty child = element.FindPropertyRelative(childName);
            if (child != null)
                child.objectReferenceValue = value;
        }
    }
}
