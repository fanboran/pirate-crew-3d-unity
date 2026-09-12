namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 描边状态判定的纯逻辑（不引用 UnityEngine，可在无头验证台断言）。
    ///
    /// 【对应章节】§4.5 <c>characterOverlay</c>：<c>corners</c>（选框角）在
    ///             "被选中（且未拖拽）**或**鼠标悬停时"显示——即悬停/选中是同一套描边的两档。
    ///             死亡角色不应再有描边反馈。
    ///
    /// 【与 shader 的契约】返回值直接写进 <c>PirateOutline.shader</c> 的 <c>_OutlineState</c>：
    ///             0 = 无描边（兜底色 alpha 为 0，片元会 discard）、1 = 悬停、2 = 选中。
    ///             取值定义必须与 shader 的 <c>OutlineColorForState()</c> 保持一致。
    ///
    /// 【优先级】存活 &gt; 死亡；选中 &gt; 悬停。原版里 hover 与 selected 用的是不同载体
    ///           （hover = inverted hull，selected = 全屏后处理），所以在原版中二者可以同时存在；
    ///           本实现把两态合并进同一材质，必须二选一，取信息量更大的"选中"。
    /// </summary>
    public static class OutlineStateRules
    {
        /// <summary>无描边（未悬停、未选中，或已死亡）。</summary>
        public const int None = 0;

        /// <summary>鼠标悬停（"可选中"提示）。</summary>
        public const int Hover = 1;

        /// <summary>已选中（"正在操作"）。</summary>
        public const int Selected = 2;

        /// <summary>按状态位解析本帧应写的描边档。</summary>
        public static int Resolve(bool alive, bool selected, bool hovered)
        {
            if (!alive)
                return None;
            if (selected)
                return Selected;
            if (hovered)
                return Hover;
            return None;
        }
    }
}
