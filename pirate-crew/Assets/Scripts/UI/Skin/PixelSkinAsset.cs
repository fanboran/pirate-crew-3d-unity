using System.Collections.Generic;
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
    /// 【数据纪律】字段全部 <c>[SerializeField] private</c>（写法对照 BalanceConfig）：
    /// Editor 程序集看不见 private，所以烘焙器的唯一写口是 <see cref="ApplyBake"/>
    /// 批量灌表（全参数必填，少传一个编译不过——新增字段时强制烘焙器同步），外部读
    /// 只走下方只读访问器。**字段名一个不许改**：序列化按名字对位，改名 = 丢 .asset
    /// 已烘焙数据。没开只读访问器的字段（windowButtons/checks 等语义族）当前零 C# 读者
    /// ——语义取用器已改走 <c>PixelSkin.Ase(id)</c>，这些字段仅存档；要用时再补访问器。
    ///
    /// 【数组约定】<see cref="toneColors"/> 是 tone×3（亮/中/暗）——tone 取色令牌；
    /// <see cref="windows"/> 是 tone 族的存件窗体皮（<c>PixelSkin.Window</c>）；
    /// 其余控件件全部收在 aseParts/asePartNames/asePartFamilies 三平行数组里。
    /// 下标算式只在 <see cref="PixelSkin"/> 里出现一次，别在装配侧手算。
    /// </summary>
    [CreateAssetMenu(fileName = "PixelSkin", menuName = "PirateCrew/Beveled Pixel 皮肤图集")]
    public sealed class PixelSkinAsset : ScriptableObject
    {
        // ---- 序列化字段（名字即序列化键，禁止改名；写只经 ApplyBake，读只经下方访问器）----

        [SerializeField, Tooltip("带标题栏窗体 windows[tone]（theme window：顶 15u 标题带 + 窗体面）")]
        private Sprite[] windows = new Sprite[0];

        [SerializeField, Tooltip("键盘焦点框（蓝白方环）")]
        private Sprite focus;

        [SerializeField, Tooltip("窗控钮三态 [常态/悬停/选中]（theme window_button 直切件 9×11）")]
        private Sprite[] windowButtons = new Sprite[0];

        [SerializeField, Tooltip("窗控图标 [关闭/帮助/播放/停止/居中]（theme window_*_icon 直切件 5×6，可乘色换染）")]
        private Sprite[] windowIcons = new Sprite[0];

        [SerializeField, Tooltip("复选框 [常态/勾选]（theme check 直切件 8×8）")]
        private Sprite[] checks = new Sprite[0];

        [SerializeField, Tooltip("单选钮 [常态/选中]（theme radio 直切件 8×8）")]
        private Sprite[] radios = new Sprite[0];

        [SerializeField, Tooltip("复选/单选焦点框（theme check_focus 直切件 2/6/2）")]
        private Sprite widgetFocus;

        [SerializeField, Tooltip("凹槽 [常态/聚焦]（theme sunken 直切件 4/4/4，textedit/列表底）")]
        private Sprite[] sunken = new Sprite[0];

        [SerializeField, Tooltip("滑条空槽 [常态/聚焦]（theme slider_empty 直切件 5/6/5）")]
        private Sprite[] sliderEmpty = new Sprite[0];

        [SerializeField, Tooltip("滑条充满段 [常态/聚焦]（theme slider_full 直切件，金色）")]
        private Sprite[] sliderFull = new Sprite[0];

        [SerializeField, Tooltip("滑条拇指（theme mini_slider_thumb 直切件 5×4）")]
        private Sprite sliderThumb;

        [SerializeField, Tooltip("滚动条 [底/滑块]（theme scrollbar 直切件 5/6/5）")]
        private Sprite[] scrollbars = new Sprite[0];

        [SerializeField, Tooltip("气泡（theme tooltip 直切件，蓝底 #4069c2）")]
        private Sprite tooltip;

        [SerializeField, Tooltip("组合框下拉箭头 [常态/选中/禁用]（theme combobox_arrow_down 直切件）")]
        private Sprite[] arrowsDown = new Sprite[0];

        [SerializeField, Tooltip("Aseprite dark 直切件全表（与 asePartNames 平行；构建器按 theme.xml <parts> 白名单填）")]
        private Sprite[] aseParts = new Sprite[0];

        [SerializeField, Tooltip("直切件 id 表（与 aseParts 同序同长；PixelSkin.Ase(id) 按它查找）")]
        private string[] asePartNames = new string[0];

        [SerializeField, Tooltip("直切件家族标签表（与 aseParts 同序同长；陈列廊按它分组，数组序即面板序）")]
        private string[] asePartFamilies = new string[0];

        [SerializeField, Tooltip("tone 文字/取色令牌 toneColors[tone×3] = 亮档/中档/暗档（S4/S3/S2）")]
        private Color32[] toneColors = new Color32[0];

        [SerializeField, Tooltip("本仓统一墨色（浅底上的文字/线条用）")]
        private Color32 ink;

        [SerializeField, Tooltip("暖白（深底上的最亮文字/高光用）")]
        private Color32 paperWhite;

        // ---- 只读访问器（只暴露现有读者用到的字段；数组回 IReadOnlyList 只读视图，零拷贝——
        //      消费方只做判空/取长/下标读，IReadOnlyList 正好堵死外部改元素/换数组这条路）----

        /// <summary>带标题栏窗体 windows[tone]（存件，现役窗体走 Ase("window")）。</summary>
        public IReadOnlyList<Sprite> Windows => windows;

        /// <summary>键盘焦点框（存件，现役焦点框走 Ase("check_focus")）。</summary>
        public Sprite Focus => focus;

        /// <summary>Aseprite dark 直切件全表（与 <see cref="AsePartNames"/> 同序同长）。</summary>
        public IReadOnlyList<Sprite> AseParts => aseParts;

        /// <summary>直切件 id 表（与 <see cref="AseParts"/> 同序同长；PixelSkin.Ase(id) 按它查找）。</summary>
        public IReadOnlyList<string> AsePartNames => asePartNames;

        /// <summary>直切件家族标签表（与 <see cref="AseParts"/> 同序同长；陈列廊按它分组）。</summary>
        public IReadOnlyList<string> AsePartFamilies => asePartFamilies;

        /// <summary>tone 取色令牌 toneColors[tone×3] = 亮/中/暗（S4/S3/S2）。</summary>
        public IReadOnlyList<Color32> ToneColors => toneColors;

        /// <summary>本仓统一墨色（浅底上的文字/线条用）。</summary>
        public Color32 Ink => ink;

        /// <summary>暖白（深底上的最亮文字/高光用）。</summary>
        public Color32 PaperWhite => paperWhite;

        /// <summary>
        /// 烘焙器专用写口（<c>BeveledPixelSpriteBuilder.GenerateAtlas</c> 一次灌全表），
        /// 运行时/装配代码只读。开成批量入口而不是逐字段 setter：烘焙器本就是"一次烘焙
        /// 全量覆盖"，批口逼着调用点具名列全 20 个参数，少写一个编译不过，不会留半新半旧
        /// 的混表。不做长度校验——对齐判据在烘焙器 <c>VerifyAtlas</c>，这里哑赋值，
        /// 保持落盘结果与旧直写逐位一致。
        /// </summary>
        public void ApplyBake(
            Sprite[] windows, Sprite focus,
            Sprite[] windowButtons, Sprite[] windowIcons,
            Sprite[] checks, Sprite[] radios, Sprite widgetFocus,
            Sprite[] sunken, Sprite[] sliderEmpty, Sprite[] sliderFull, Sprite sliderThumb,
            Sprite[] scrollbars, Sprite tooltip, Sprite[] arrowsDown,
            Sprite[] aseParts, string[] asePartNames, string[] asePartFamilies,
            Color32[] toneColors, Color32 ink, Color32 paperWhite)
        {
            this.windows = windows;
            this.focus = focus;
            this.windowButtons = windowButtons;
            this.windowIcons = windowIcons;
            this.checks = checks;
            this.radios = radios;
            this.widgetFocus = widgetFocus;
            this.sunken = sunken;
            this.sliderEmpty = sliderEmpty;
            this.sliderFull = sliderFull;
            this.sliderThumb = sliderThumb;
            this.scrollbars = scrollbars;
            this.tooltip = tooltip;
            this.arrowsDown = arrowsDown;
            this.aseParts = aseParts;
            this.asePartNames = asePartNames;
            this.asePartFamilies = asePartFamilies;
            this.toneColors = toneColors;
            this.ink = ink;
            this.paperWhite = paperWhite;
        }
    }
}
