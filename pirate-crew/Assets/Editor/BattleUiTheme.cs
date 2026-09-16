using PirateCrew.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 战斗 HUD 视觉主题钩子（**本波次换皮：半透明亚克力 / 液态玻璃**）。
    ///
    /// M2BattleSceneSetup.BuildHud 在搭完 BattleCanvas 的全部子节点与接线之后调用
    /// <see cref="Apply"/>，把 Canvas 传进来。
    ///
    /// 【本波次实现】按用户裁决换掉早先的「木板 / 羊皮纸 / 黄铜 = 纯色面板」：
    ///   1. 清掉 Canvas 下除 <c>BattleHud</c> 控制器与 <c>MinimapPanel</c>（小地图接线复用）之外的旧 HUD 节点；
    ///   2. 交给 <see cref="BattleHudBuilder"/> 按**半透明亚克力语言**重建
    ///      （面板 = <see cref="GlassPanelSpriteBuilder.Tone.Frame"/> 玻璃框架 + 内容片，
    ///      按钮 = <see cref="GlassPanelSpriteBuilder.Tone.Button"/> 玻璃三态，
    ///      文字 = 浅米 / 金（色板见下 <see cref="Tok"/>）、字号 = <see cref="MenuUiBuilder.FontScale"/>）；
    ///   3. 用 <see cref="SerializedObject"/> 把 <see cref="BattleHud"/> 的
    ///      <c>[SerializeField]</c> 引用重新指向新节点（**接线口径不变**：17 武器槽 / 12 名册行 / 3 个命令按钮）。
    ///
    /// 【契约】只改外观与节点归属，不改 Canvas 的 RenderMode / CanvasScaler（1920×1080 match 0.5），
    /// 不新增/变更 EventBus 事件。可重复调用（每次场景重建都会调一次）。
    /// 【保留节点】清旧节点时保留 Canvas 上的 <c>BattleHud</c> 与 <c>MinimapPanel</c>；
    /// 小地图的 <c>DotLayer/TileLayer/IslandLayer</c> 是 MinimapPanel 的子物体，随面板一并保留
    /// （IslandLayer 的沙/草烘焙由 <c>HudMinimapSceneSetup</c> 负责，本类不触碰）。
    /// </summary>
    public static class BattleUiTheme
    {
        /// <summary>HUD 控制器对象名（M2BattleSceneSetup 创建）。</summary>
        const string HudControllerName = "BattleHud";

        /// <summary>
        /// 战斗 HUD 的文字/状态色板（**本波次唯一真相源**；色值本身复用 <see cref="UiTheme"/> 的既有 Token，
        /// 这里只登记"谁压在什么玻璃上"，避免散落魔法色值 —— 同 AGENTS.md 的事件契约纪律）。
        ///
        /// 【为什么单独一层】玻璃底的对比度取决于"玻璃叠在场景上"的合成色，且**分层**决定可用色：
        /// 详见 <see cref="GlassPanelSpriteBuilder"/> 类注释的推导 —— 结论是
        /// **金标题只许放内容片（alpha 0.88）、浅米字可放框架（0.70）、队伍色名只许放内容片**。
        /// </summary>
        public static class Tok
        {
            /// <summary>深色玻璃上的正文（浅米）：叠纯白最坏底，框架 4.86:1 / 内容片 9.56:1。</summary>
            public static Color TextOnGlass => UiTheme.TextLight;

            /// <summary>金标题/关键数值（B 站强调色那套）：压内容片 7.76:1；框架 0.74 上 4.52:1（达标但余量薄，故仍优先内容片）。</summary>
            public static Color TitleOnGlass => UiTheme.BrassLight;

            /// <summary>金底上的深字（模式开关的当前段）。</summary>
            public static Color InkOnGold => new Color(0x2A / 255f, 0x1D / 255f, 0x0E / 255f, 1f);

            /// <summary>选中态描边（UI 与 3D 同源，同屏只出现一处，§6.3）。</summary>
            public static Color Select => UiTheme.Select;

            /// <summary>
            /// 模式开关的"当前/非当前"区分口径（本成员只是"当前段外描边色"的出口，底片由
            /// <see cref="BattleHudBuilder"/> 选 tone 实现）：
            /// **当前段 = 金玻璃（<see cref="GlassPanelSpriteBuilder.Tone.Primary"/>）+ 深墨字**；
            /// **非当前段 = 中性深玻璃（<see cref="GlassPanelSpriteBuilder.Tone.Button"/>）+ 浅米字**。
            /// 【为什么不沿用"同底色压暗一档"】旧版两段都金底、靠乘色压暗区分；但深墨字压"压暗后的金"
            /// 在纯黑最坏底上：乘色 0.84 只剩 3.43:1、0.95 也才 4.24:1 —— **任何 &lt;1 的乘色都不达标**。
            /// 改**色相区分**后两段各自达标（4.65 / 4.64），且与参照物 game-2 的
            /// tab_selected（琥珀底）/ tab_normal（中性底）同构。当前段另加 1px 亮金外描边，
            /// 给色觉障碍玩家第二重信号（不依赖色相）。
            /// </summary>
            public static Color ModeActiveStroke => UiTheme.BrassLight;

            /// <summary>名册血条槽底（凹槽感：比面板更暗的一层薄片）。</summary>
            public static Color BarGroove => new Color(0f, 0f, 0f, 0.38f);
        }

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

            // 模态层（发布收口）：暂停 / 结算 / 返回确认。
            SetRef(so, "pauseButton", result.pauseButton);
            SetRef(so, "pausePanelRoot", result.pausePanelRoot);
            SetRef(so, "resumeButton", result.resumeButton);
            SetRef(so, "pauseRestartButton", result.pauseRestartButton);
            SetRef(so, "pauseBackButton", result.pauseBackButton);
            SetRef(so, "confirmDialogRoot", result.confirmDialogRoot);
            SetRef(so, "confirmMessage", result.confirmMessage);
            SetRef(so, "confirmOkButton", result.confirmOkButton);
            SetRef(so, "confirmCancelButton", result.confirmCancelButton);
            SetRef(so, "settlementPanelRoot", result.settlementPanelRoot);
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
