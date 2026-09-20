namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 全游戏 UI 层号统一常量 —— 一处定义、处处引用，杜绝魔法数字。
    /// 移植自 stick-world <c>modules/ui_global/scripts/theme/layer_order.gd</c>，数值 1:1。
    ///
    /// 跨引擎映射约定：
    ///  - Godot「CanvasLayer.layer」（不同 CanvasLayer 之间的层序）→ Unity 侧对应
    ///    <c>Canvas.sortingOrder</c>（各宿主自建 Canvas 时取用）；
    ///  - Godot「UIRoot 内 z_index」（同一 CanvasLayer 内 Control 之间的层序）→
    ///    Unity 同一 Canvas 内的兄弟顺序（sibling index），<see cref="StickUIRoot"/>
    ///    按本表 Z* 值对槽容器做 <c>SetSiblingIndex</c> 稳定排序，值越大越靠上。
    /// </summary>
    public static class StickLayerOrder
    {
        // ---------------- CanvasLayer 层号 ----------------

        /// <summary>UIRoot（全局 UI：HUD / 模式 / 上下文 / 模态 / 系统 / 调试 都在其内，用 Z* 细分）。</summary>
        public const int Hud = 1;

        /// <summary>世界加载覆盖层（game_root 下独立 CanvasLayer，须高于 UIRoot）。</summary>
        public const int WorldLoading = 10;

        /// <summary>调试覆盖层（debug_gui，F3）。</summary>
        public const int DebugOverlay = 20;

        /// <summary>战略图 L1（Tab）。</summary>
        public const int StrategicL1 = 100;

        /// <summary>战略图 L3（M）。</summary>
        public const int StrategicL3 = 101;

        /// <summary>战略图 L2（L3 下钻）。</summary>
        public const int StrategicL2 = 102;

        // ---------------- UIRoot 内 z_index（同 Canvas 内兄弟顺序排序键） ----------------

        /// <summary>HUD 槽（GlobalHUD / ModePanel / ContextPanel / HudOverlay / ResourceBar）。</summary>
        public const int ZHud = 0;

        /// <summary>zone 保留区 debug 画框（随 F3 显隐；压过 HUD 部件、低于模态）。</summary>
        public const int ZHudZoneDebug = 10;

        /// <summary>模态层（ModalOverlay：排他模态盖住 HUD 槽）。</summary>
        public const int ZModal = 50;

        /// <summary>系统层（SystemOverlay：toast / 确认框，在模态之上）。</summary>
        public const int ZSystem = 90;

        /// <summary>F3 调试 inspect（最高，纯绘制）。</summary>
        public const int ZInspector = 100;
    }
}
