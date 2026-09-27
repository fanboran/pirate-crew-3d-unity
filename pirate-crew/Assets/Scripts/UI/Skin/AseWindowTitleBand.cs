using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>窗体实际内容顶的载体（挂在窗根上；无此组件 = 原生 17 口径）。
    /// 标题字大时带装不下（创始人裁决：**溢出可以接受**），但内容起点必须随字高
    /// 下移拉开距离（防标题与下方元素穿模太紧）——窗内排版都问
    /// <see cref="UiKit.WindowContentTopOf"/>。
    ///
    /// 【为什么独立成文件】Unity 的 MonoBehaviour 不许嵌套在静态类里（嵌套件存不进
    /// Prefab——ScenePrefabCollapse 折叠 Battle 时 SaveAsPrefabAsset 实锤报错，
    /// 2026-09-28 换装波）；此前嵌套在 UiKit 内，场景文件靠 m_ClassName 兜底能加载，
    /// Prefab 一律静默丢件。类名 = 文件名是序列化前提，与本文件不可分离。</summary>
    public sealed class AseWindowTitleBand : MonoBehaviour
    {
        /// <summary>实际内容顶 = 窗顶到内容区起点的距离（原生 = 17：带 15 + 缝 2）。</summary>
        public float ContentTop = 17f;
    }
}
