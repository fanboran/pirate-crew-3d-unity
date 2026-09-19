using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
/// <summary>
/// 手绘贴图的沸腾驱动（隔壁 SketchTextures"每控件独立 seed 逐帧随机重掷"的移植）。
///
/// 【机制】每 <see cref="SketchSkin.BoilSeconds"/>(0.12s) 给挂着的 <see cref="Image"/>
/// 换同一槽位的下一沸腾帧——边缘扰动逐帧变化，UI 呈现"手画的线在微微沸腾"。
/// 每件一个实例 ID 相位，所有控件不会同步抖动（隔壁"逐个手画"的差异感来源）。
///
/// 【按钮四态】给 <see cref="StateSlots"/> 传四槽组（<see cref="SketchSkin.Btn"/> 等）
/// 时，按指针状态切槽（normal/hover/pressed/disabled 各自沸腾）。UGUI 的按钮内部
/// 状态查询是 protected——这里自己实现指针接口维护 hover/pressed（与 Button 同物体
/// 时事件系统会一并派发）。ColorBlock tint 已被 UiKit 关掉——手绘语言的状态反馈靠
/// 贴图本身（边框变亮/实底变色），不再叠乘色。
/// </summary>
[RequireComponent(typeof(Image))]
public sealed class SketchBoil : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler,
    UnityEngine.EventSystems.IPointerExitHandler, UnityEngine.EventSystems.IPointerDownHandler,
    UnityEngine.EventSystems.IPointerUpHandler
{
    /// <summary>常态槽名（静态件唯一的槽）。</summary>
    public string Slot = "panel";

    /// <summary>按钮状态槽组 {normal, hover, pressed, disabled}；null = 静态件。</summary>
    public string[] StateSlots;

    Image _image;
    Button _button;
    float _next;
    int _tick;
    int _phase;
    bool _hovered;
    bool _pressed;

    void OnEnable()
    {
        _image = GetComponent<Image>();
        _button = GetComponent<Button>();
        // 相位用实例 ID（确定性、跨帧稳定），避免 Random 在同帧批量挂件时同相。
        _phase = Mathf.Abs(_image.GetInstanceID()) % SketchSkin.FrameCount;
        _next = Time.unscaledTime + SketchSkin.BoilSeconds;
        // 【不在这里 Apply】AddComponent 会同步触发 OnEnable，此刻调用方往往还没给
        // Slot 赋值（默认 "panel" 会把按钮/血条的头一帧换成面板图——r12 gallery 全页
        // 错图的根因）。Image.sprite 已由调用方设好正确的 f0，等第一个节拍再接管。
    }

    void Update()
    {
        if (Time.unscaledTime < _next)
            return;
        _next = Time.unscaledTime + SketchSkin.BoilSeconds;
        _tick++;
        Apply();
    }

    void Apply()
    {
        if (_image == null)
            return;
        Sprite frame = SketchSkin.Frame(ResolveSlot(), _tick + _phase);
        if (frame != null)
            _image.sprite = frame;
    }

    string ResolveSlot()
    {
        if (StateSlots == null || StateSlots.Length < 4)
            return Slot;
        if (_button != null && !_button.interactable)
            return StateSlots[3];
        if (_pressed)
            return StateSlots[2];
        if (_hovered)
            return StateSlots[1];
        return StateSlots[0];
    }

    /// <summary>给按钮件配四态槽组（UiKit 工厂内部用）。</summary>
    public void BindStates(string[] stateSlots)
    {
        StateSlots = stateSlots;
    }

    void UnityEngine.EventSystems.IPointerEnterHandler.OnPointerEnter(
        UnityEngine.EventSystems.PointerEventData eventData) => _hovered = true;

    void UnityEngine.EventSystems.IPointerExitHandler.OnPointerExit(
        UnityEngine.EventSystems.PointerEventData eventData)
    {
        _hovered = false;
        _pressed = false;
    }

    void UnityEngine.EventSystems.IPointerDownHandler.OnPointerDown(
        UnityEngine.EventSystems.PointerEventData eventData) => _pressed = true;

    void UnityEngine.EventSystems.IPointerUpHandler.OnPointerUp(
        UnityEngine.EventSystems.PointerEventData eventData) => _pressed = false;
}
}
