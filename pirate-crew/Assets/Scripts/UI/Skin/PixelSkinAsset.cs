using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// Beveled Pixel 皮肤图集（ScriptableObject）。由 Editor 侧
    /// <c>BeveledPixelSpriteBuilder.BuildAll</c> 烘焙生成并落
    /// <c>Assets/Resources/UI/PixelSkin.asset</c>；运行时统一经 <see cref="PixelSkin"/> 取用，
    /// 装配代码不要直接 LoadAssetAtPath（播放器没有 AssetDatabase）。
    ///
    /// 【为什么是"资产"而不是"运行时生成"】生成器是 Editor 烘焙器岗位（几何/判据/落盘全在
    /// Editor 程序集），运行时只消费成品。图集把全部 Sprite 收进一个可序列化引用的资产：
    /// Editor 装配时 Image 直接持有 Sprite 引用（场景序列化，播放器零加载），
    /// 运行时动态创建的 UI（头顶血条等）再走 Resources.Load 这一份，两条路同源。
    ///
    /// 【数组约定】<see cref="plates"/> 是 tone×state 的直积网格（tone 主序），
    /// <see cref="toneColors"/> 是 tone×3（亮/中/暗）——下标算式只在 <see cref="PixelSkin"/>
    /// 里出现一次，别在装配侧手算。
    /// </summary>
    [CreateAssetMenu(fileName = "PixelSkin", menuName = "PirateCrew/Beveled Pixel 皮肤图集")]
    public sealed class PixelSkinAsset : ScriptableObject
    {
        [Tooltip("凸起块 plates[tone×state]；tone 主序（Frame..Warn），state: 0 常态 / 1 悬停 / 2 按压")]
        public Sprite[] plates = new Sprite[0];

        [Tooltip("凹槽 tracks[tone]（条状件空槽底）")]
        public Sprite[] tracks = new Sprite[0];

        [Tooltip("页签 tabs[tone]（底边无带，与宿主面板顶边贴合）")]
        public Sprite[] tabs = new Sprite[0];

        [Tooltip("填充条 fills[kind]（画在 Track 内容区上）")]
        public Sprite[] fills = new Sprite[0];

        [Tooltip("选人圈（暖金方环，战场/徽章外环）")]
        public Sprite ring;

        [Tooltip("键盘焦点框（蓝白方环）")]
        public Sprite focus;

        [Tooltip("位点·亮（页点/队伍槽指示，菱形）")]
        public Sprite pipOn;

        [Tooltip("位点·暗")]
        public Sprite pipOff;

        [Tooltip("蚀刻分隔线·水平")]
        public Sprite separatorH;

        [Tooltip("蚀刻分隔线·垂直")]
        public Sprite separatorV;

        [Tooltip("面板投影（INK 剪影，垫面板下按 ShadowOffset 右下错开）")]
        public Sprite shadow;

        [Tooltip("tone 文字/取色令牌 toneColors[tone×3] = 亮档/中档/暗档（S4/S3/S2）")]
        public Color32[] toneColors = new Color32[0];

        [Tooltip("本仓统一墨色（浅底上的文字/线条用）")]
        public Color32 ink;

        [Tooltip("暖白（深底上的最亮文字/高光用）")]
        public Color32 paperWhite;
    }
}
