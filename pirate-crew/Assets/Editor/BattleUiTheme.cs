using PirateCrew.UI;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 战斗 HUD 换肤钩子（**本波次：多彩卡通 · 图标优先**）。
    ///
    /// M2BattleSceneSetup.BuildHud 在搭完 BattleCanvas 的全部子节点与接线之后调用
    /// <see cref="Apply"/>，把 Canvas 传进来：
    ///   1. 清掉 Canvas 下除 <c>BattleHud</c> 控制器与 <c>MinimapPanel</c>（小地图接线复用）
    ///      之外的旧 HUD 节点；
    ///   2. 交给 <see cref="BattleHudBuilder"/> 按新皮肤与信息架构重建
    ///      （皮肤/字号/颜色全部 <see cref="UiSkin"/> Token，图标走烘焙 PNG）；
    ///   3. 用 <see cref="SerializedObject"/> 把 <see cref="BattleHud"/> 的
    ///      <c>[SerializeField]</c> 引用重新指向新节点。
    ///
    /// 【契约】只改外观与节点归属，不改 Canvas 的 RenderMode / CanvasScaler
    /// （1920×1080 + ScreenMatchMode.Expand，对齐 Godot canvas_items+expand 口径；
    /// 存量已建 .unity 场景仍是旧 match 0.5 口径，待重建批次经构建器统一刷新），
    /// 不新增/变更 EventBus 事件。可重复调用（每次场景重建都会调一次）。
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

            // 顶栏。
            SetTeamBar(so.FindProperty("teamBarRed"), result.teamBarRed);
            SetTeamBar(so.FindProperty("teamBarBlue"), result.teamBarBlue);
            SetRef(so, "badgeRing", result.badgeRing);
            SetRef(so, "badgeText", result.badgeText);
            SetRef(so, "turnHintText", result.turnHintText);

            // 武器面板。
            SetRef(so, "weaponPanelRoot", result.weaponPanelRoot);
            SetRef(so, "unitPortrait", result.unitPortrait);
            SetRef(so, "unitNameText", result.unitNameText);
            SetHpBar(so.FindProperty("unitHpBar"), result.unitHpBar);
            SetRef(so, "weaponNameText", result.weaponNameText);
            SetRef(so, "weaponDescText", result.weaponDescText);
            SetArray(so.FindProperty("weaponButtons"), result.weaponButtons);
            SetArray(so.FindProperty("weaponFrames"), result.weaponFrames);
            SetRef(so, "throwSelfButton", result.throwSelfButton);
            SetRef(so, "endGoButton", result.endGoButton);

            // 模式开关 / 系统钮 / 提示。
            SetArray(so.FindProperty("modeButtons"), result.modeButtons);
            SetArray(so.FindProperty("modeFrames"), result.modeFrames);
            SetRef(so, "backButton", result.backButton);
            SetRef(so, "pauseButton", result.pauseButton);
            SetRef(so, "hintText", result.hintText);

            // 模态：暂停 / 返回确认 / 结算。
            SetRef(so, "pausePanelRoot", result.pausePanelRoot);
            SetRef(so, "pauseCard", result.pauseCard);
            SetRef(so, "resumeButton", result.resumeButton);
            SetRef(so, "pauseRestartButton", result.pauseRestartButton);
            SetRef(so, "pauseBackButton", result.pauseBackButton);
            SetRef(so, "confirmDialogRoot", result.confirmDialogRoot);
            SetRef(so, "confirmCard", result.confirmCard);
            SetRef(so, "confirmMessage", result.confirmMessage);
            SetRef(so, "confirmOkButton", result.confirmOkButton);
            SetRef(so, "confirmCancelButton", result.confirmCancelButton);
            SetRef(so, "settlementPanelRoot", result.settlementPanelRoot);
            SetRef(so, "settlementCard", result.settlementCard);
            SetRef(so, "settlementTitleText", result.settlementTitleText);
            SetRef(so, "settlementLinesText", result.settlementLinesText);
            SetRef(so, "settlementRestartButton", result.settlementRestartButton);
            SetRef(so, "settlementBackButton", result.settlementBackButton);
            if (result.settlementStars != null)
                SetArray(so.FindProperty("settlementStars"), result.settlementStars);

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

            // 构建器漏建节点时 value 为 null，会让 HUD 静默失去该引用；
            // 显式告警，避免「看着有 HUD，其实某块没接上」。
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

        static void SetTeamBar(SerializedProperty property, BattleHud.TeamBarView view)
        {
            if (property == null || view == null)
                return;

            SetRelative(property, "root", view.root);
            SetRelative(property, "segmentRoot", view.segmentRoot);
            SetRelative(property, "pipRoot", view.pipRoot);

            SerializedProperty segments = property.FindPropertyRelative("segments");
            if (segments != null && view.segments != null)
            {
                segments.arraySize = view.segments.Length;
                for (int i = 0; i < view.segments.Length; i++)
                {
                    SerializedProperty element = segments.GetArrayElementAtIndex(i);
                    SetRelative(element, "root", view.segments[i].root);
                    SetRelative(element, "fill", view.segments[i].fill);
                    SetRelative(element, "ghost", view.segments[i].ghost);
                }
            }

            SerializedProperty pips = property.FindPropertyRelative("pips");
            if (pips != null && view.pips != null)
            {
                pips.arraySize = view.pips.Length;
                for (int i = 0; i < view.pips.Length; i++)
                {
                    SerializedProperty element = pips.GetArrayElementAtIndex(i);
                    SetRelative(element, "root", view.pips[i].root);
                    SetRelative(element, "icon", view.pips[i].icon);
                    SetRelative(element, "frame", view.pips[i].frame);
                }
            }
        }

        static void SetHpBar(SerializedProperty property, BattleHud.HpBarView view)
        {
            if (property == null || view == null)
                return;

            SetRelative(property, "track", view.track);
            SetRelative(property, "ghost", view.ghost);
            SetRelative(property, "fill", view.fill);
        }

        static void SetRelative(SerializedProperty element, string childName, Object value)
        {
            SerializedProperty child = element.FindPropertyRelative(childName);
            if (child != null)
                child.objectReferenceValue = value;
        }
    }
}
