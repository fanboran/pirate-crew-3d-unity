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

        [Tooltip("面板 panels[tone]（直角对话框外层，Aseprite dark 语法）")]
        public Sprite[] panels = new Sprite[0];

        [Tooltip("带标题栏窗体 windows[tone]（theme window：顶 15u 标题带 + 窗体面）")]
        public Sprite[] windows = new Sprite[0];

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

        [Tooltip("窗控钮三态 [常态/悬停/选中]（theme window_button 直切件 9×11）")]
        public Sprite[] windowButtons = new Sprite[0];

        [Tooltip("窗控图标 [关闭/帮助/播放/停止/居中]（theme window_*_icon 直切件 5×6，可乘色换染）")]
        public Sprite[] windowIcons = new Sprite[0];

        [Tooltip("复选框 [常态/勾选]（theme check 直切件 8×8）")]
        public Sprite[] checks = new Sprite[0];

        [Tooltip("单选钮 [常态/选中]（theme radio 直切件 8×8）")]
        public Sprite[] radios = new Sprite[0];

        [Tooltip("复选/单选焦点框（theme check_focus 直切件 2/6/2）")]
        public Sprite widgetFocus;

        [Tooltip("凹槽 [常态/聚焦]（theme sunken 直切件 4/4/4，textedit/列表底）")]
        public Sprite[] sunken = new Sprite[0];

        [Tooltip("滑条空槽 [常态/聚焦]（theme slider_empty 直切件 5/6/5）")]
        public Sprite[] sliderEmpty = new Sprite[0];

        [Tooltip("滑条充满段 [常态/聚焦]（theme slider_full 直切件，金色）")]
        public Sprite[] sliderFull = new Sprite[0];

        [Tooltip("滑条拇指（theme mini_slider_thumb 直切件 5×4）")]
        public Sprite sliderThumb;

        [Tooltip("滚动条 [底/滑块]（theme scrollbar 直切件 5/6/5）")]
        public Sprite[] scrollbars = new Sprite[0];

        [Tooltip("气泡（theme tooltip 直切件，蓝底 #4069c2）")]
        public Sprite tooltip;

        [Tooltip("组合框下拉箭头 [常态/选中/禁用]（theme combobox_arrow_down 直切件）")]
        public Sprite[] arrowsDown = new Sprite[0];

        [Tooltip("Aseprite dark 直切件全表（与 asePartNames 平行；构建器按 theme.xml <parts> 白名单填）")]
        public Sprite[] aseParts = new Sprite[0];

        [Tooltip("直切件 id 表（与 aseParts 同序同长；PixelSkin.Ase(id) 按它查找）")]
        public string[] asePartNames = new string[0];

        [Tooltip("直切件家族标签表（与 aseParts 同序同长；陈列廊按它分组，数组序即面板序）")]
        public string[] asePartFamilies = new string[0];

        [Tooltip("tone 文字/取色令牌 toneColors[tone×3] = 亮档/中档/暗档（S4/S3/S2）")]
        public Color32[] toneColors = new Color32[0];

        [Tooltip("本仓统一墨色（浅底上的文字/线条用）")]
        public Color32 ink;

        [Tooltip("暖白（深底上的最亮文字/高光用）")]
        public Color32 paperWhite;
    }
}
