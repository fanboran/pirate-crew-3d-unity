using UnityEngine;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// StickTokens 的手写消费端便利层（与 StickTokens.generated.cs 同为一个 partial 类）。
    /// 职责边界：令牌真相源在 stick-world 仓 assets/config/ui_tokens.json，由
    /// tools/ui_sync/sync_to_unity.py 同步生成为 StickTokens.generated.cs（勿手改）；
    /// 本文件只提供 Resources 加载与路径拼接便利，不含任何令牌数值。
    /// </summary>
    public static partial class StickTokens
    {
        /// <summary>图标 Resources 根（对齐同步目标 Assets/Resources/UI/StickWorld/icons）。</summary>
        private const string IconsRoot = "UI/StickWorld/icons";

        /// <summary>sketch 槽位帧图 Resources 根（对齐 Assets/Resources/UI/StickWorld/sketch）。</summary>
        private const string SketchRoot = "UI/StickWorld/sketch";

        /// <summary>按母题名加载图标（如 "金币"）。路径查 IconMotifs 令牌表；母题未知或
        /// Resources 加载失败时告警并返回 null（纪律同 UiKit.RuntimeFont：null 交调用方
        /// 兜底，不抛异常、不静默吞错）。</summary>
        public static Sprite LoadIcon(string motif)
        {
            if (string.IsNullOrEmpty(motif))
            {
                Debug.LogWarning("[StickTokens] LoadIcon：母题名为空");
                return null;
            }
            if (!IconMotifs.TryGetValue(motif, out string path))
            {
                Debug.LogWarning("[StickTokens] 未知图标母题：" + motif + "（可用名单见 IconMotifs）");
                return null;
            }
            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
                Debug.LogWarning("[StickTokens] Resources/" + path + " 加载失败（缺 Sprite 或未同步），返回 null");
            return sprite;
        }

        /// <summary>sketch 槽位某帧的 Resources 路径（如 SketchSlotFrame("panel", 0) →
        /// "UI/StickWorld/sketch/panel_f0"，不带扩展名）；帧循环取模策略由调用方决定，
        /// 帧数上限见 SketchFrames。</summary>
        public static string SketchSlotFrame(string slot, int frame)
        {
            return SketchRoot + "/" + slot + "_f" + frame;
        }
    }
}
