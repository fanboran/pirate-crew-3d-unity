using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PirateCrew.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 确认框（模态）—— 确认框族的栈化实现。移植自 stick-world <c>stick_confirm_dialog.gd</c>。
    ///
    /// 入 <see cref="StickModalLayer.Confirm"/> 层（栈顶，经 <see cref="StickKit.Confirm"/> 创建），
    /// ESC = 取消（05 篇：ESC 等效取消，不触发 on_confirm）。全屏遮罩 + 居中紧凑窗口；
    /// **点遮罩不关闭**（确认框白名单，必须显式选择）。关闭即销毁（Destroy，对齐 gd queue_free）。
    ///
    /// gd 侧紧凑自适应靠 resized 信号把窗口按内容居中；Unity 无内容驱动尺寸事件，
    /// 改为 Setup 时用 TMP preferred 测量一次定窗口尺寸（宽度 = 消息单行宽收敛到
    /// [260, MSG_MAX_WIDTH]，高度 = 标题 + 消息 + 按钮行 + 间距/内边距求和）。
    /// </summary>
    public sealed class StickConfirmDialog : MonoBehaviour, IStickScreen
    {
        /// <summary>消息单行宽度上限（超此才折行；紧凑自适应，不摆大面板的架子）。</summary>
        public const float MsgMaxWidth = 480f;

        const float MinWindowWidth = 260f;

        Action _onConfirm;
        RectTransform _window;
        bool _destroyed;

        // ---------------- 构建骨架（gd setup 同签名语义） ----------------

        public void Setup(string title, string message, Action onConfirm,
            string confirmText, StickKit.StickButtonKind kind)
        {
            _onConfirm = onConfirm;

            // 根必须铺满父容器，否则内部遮罩塌缩成 0 尺寸不可见（gd 曾致确认框静默不显示）
            StickUIKit.FullRect((RectTransform)transform);

            // 遮罩：MODAL_DIM 压暗；raycast 消费点击但**不挂关闭**——点遮罩不关（确认框白名单）
            RectTransform dim = UiKit.CreateRect("Dim", transform);
            StickUIKit.FullRect(dim);
            Image dimImage = dim.gameObject.AddComponent<Image>();
            dimImage.color = StickTokens.MODAL_DIM;
            dimImage.raycastTarget = true;

            // 居中紧凑窗口（像素九宫格底 + 投影）
            _window = UiKit.CreateRect("Window", transform);
            _window.anchorMin = new Vector2(0.5f, 0.5f);
            _window.anchorMax = new Vector2(0.5f, 0.5f);
            _window.pivot = new Vector2(0.5f, 0.5f);
            RectTransform backplate = UiKit.CreateRect("Backplate", _window);
            StickUIKit.FullRect(backplate);
            UiKit.EnsurePanel(backplate, PixelTone.Frame);

            // 内容骨架（separation 8 对齐 gd box）
            RectTransform content = UiKit.CreateRect("Content", _window);
            StickLayoutGroup box = content.gameObject.AddComponent<StickLayoutGroup>();
            box.Orientation = StickOrientation.Vertical;
            box.Separation = 8f;
            box.PaddingLeft = StickTokens.SketchPanelPadX;
            box.PaddingRight = StickTokens.SketchPanelPadX;
            box.PaddingTop = StickTokens.SketchPanelPadY;
            box.PaddingBottom = StickTokens.SketchPanelPadY;

            // 标题（粗体居中，对齐 gd SketchFonts.bold）
            TextMeshProUGUI titleL = StickKit.Label(content, title, StickKit.StickLabelKind.Body);
            titleL.fontStyle = FontStyles.Bold;
            titleL.alignment = TextAlignmentOptions.Center;
            titleL.enableWordWrapping = false;

            // 消息（暗字居中；单行展示，超宽才换行——无宽度约束时 autowrap 的 min 宽
            // 会塌缩到单字宽，长消息被折成一行一字把窗口撑出数倍高度，gd 同款注释）
            TextMeshProUGUI msg = StickKit.Label(content, message, StickKit.StickLabelKind.Body,
                StickTokens.TEXT_DIM);
            msg.alignment = TextAlignmentOptions.Center;
            Vector2 oneLine = msg.GetPreferredValues(message);
            bool wrap = oneLine.x > MsgMaxWidth;
            float msgWidth = Mathf.Clamp(oneLine.x, MinWindowWidth, MsgMaxWidth);
            float msgHeight;
            if (wrap)
            {
                msgHeight = msg.GetPreferredValues(message, MsgMaxWidth, float.MaxValue).y;
            }
            else
            {
                msgHeight = oneLine.y;
                msg.enableWordWrapping = false;
            }

            // 按钮行（居中成对：取消 + 确认，高 28）
            StickLayoutGroup btnRow = StickKit.Row(content, 10f);
            StickLayoutElement rowCenter = btnRow.gameObject.AddComponent<StickLayoutElement>();
            rowCenter.CrossAlign = StickCrossAlign.Center;
            StickKit.AutoButton(btnRow.transform, "取消", Close, StickKit.StickButtonKind.Normal, 28f);
            StickKit.AutoButton(btnRow.transform, confirmText, OnConfirmPressed, kind, 28f);

            // 窗口尺寸一次定稿（宽随内容收敛，高 = 累加；gd 靠 resized 自适应的等价展开）。
            // 按钮高 28 对齐 gd 字面量（取消/确认两行小号按钮）。
            float height = StickTokens.SketchPanelPadY * 2f
                + titleL.preferredHeight + 8f + msgHeight + 8f + 28f;
            _window.sizeDelta = new Vector2(msgWidth + StickTokens.SketchPanelPadX * 2f, height);
            StickUIKit.FullRect(content);
        }

        void OnConfirmPressed()
        {
            Close();
            _onConfirm?.Invoke();
        }

        // ---------------- 开关（关闭即销毁） ----------------

        public void Open()
        {
            if (_destroyed)
                return;
            gameObject.SetActive(true);
        }

        /// <summary>关闭并销毁（ESC/取消/确定统一走这里；对齐 gd queue_free）。</summary>
        public void Close()
        {
            if (_destroyed)
                return;
            _destroyed = true;
            Destroy(gameObject);
        }

        public bool IsOpen
        {
            get { return !_destroyed && gameObject.activeSelf; }
        }
    }
}
