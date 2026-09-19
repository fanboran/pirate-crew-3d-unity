using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 手绘面板的九砖平铺组装（复刻 Godot StyleBoxTexture 的 TILE 行为——隔壁
    /// 手绘皮肤的"大面板墨线小波浪"就是它给的）。
    ///
    /// 【为什么要它】Unity <c>Image.Type.Sliced</c> 的边带是**拉伸**：1040px 面板的
    /// 顶边把 96px 贴图上 76px 周期的墨线小波浪拉成千像素缓波，手绘密度尽失——
    /// 这就是"观感和隔壁不一样"的硬根因。本组件改用 9 块子砖贴图
    /// （<c>&lt;槽&gt;_tl/_tc/.../&lt;槽&gt;_br_f&lt;i&gt;.png</c>，烘焙管线切出，整平铺周期段）：
    /// 四角 Simple 固定、四边与中心 <c>Image.Type.Tiled</c> 逐砖重复——边带波浪密度
    /// 恒定，与 Godot 逐像素等价。
    ///
    /// 【沸腾】每 <see cref="SketchSkin.BoilSeconds"/> 九砖**同步**换同帧（砖间错帧
    /// 会接缝错乱）；相位逐面板独立。时序铁律同 <see cref="SketchBoil">：延迟一帧
    /// 构建，等调用方给完 <see cref="Slot"/>。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class Sketch9Slice : MonoBehaviour
    {
        /// <summary>面板槽名（子砖 = "<c>&lt;槽&gt;_&lt;部位&gt;</c>"）。</summary>
        public string Slot = "panel";

        Image[] _parts = new Image[9];
        bool _built;
        float _next;
        int _tick;
        int _phase;

        static readonly string[] PartNames = { "tl", "tc", "tr", "ml", "mc", "mr", "bl", "bc", "br" };

        void OnEnable()
        {
            _phase = Mathf.Abs(GetInstanceID()) % SketchSkin.FrameCount;
            _next = Time.unscaledTime + SketchSkin.BoilSeconds;
            StartCoroutine(BuildNextFrame());
        }

        void OnDisable()
        {
            StopAllCoroutines();
            _built = false;
        }

        IEnumerator BuildNextFrame()
        {
            yield return null;   // 时序铁律：AddComponent 同步触发 OnEnable，等调用方赋值完
            if (!gameObject.activeInHierarchy)
                yield break;
            Build();
        }

        void Update()
        {
            if (!_built || Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + SketchSkin.BoilSeconds;
            _tick++;
            Apply();
        }

        /// <summary>换槽（重配九砖 sprite，布局不动）。</summary>
        public void SetSlot(string slot)
        {
            Slot = slot;
            if (_built)
                Apply();
        }

        void Build()
        {
            var root = (RectTransform)transform;
            // 只清自己建的砖（面板根下还有内容子件——DotLayer/模态按钮/血条段，
            // 全量清子件会把它们一起洗掉）；重复 SetSlot/重建走同一入口。
            for (int i = 0; i < 9; i++)
            {
                if (_parts[i] != null)
                    Destroy(_parts[i].gameObject);
                _parts[i] = null;
            }

            for (int i = 0; i < 9; i++)
            {
                _parts[i] = CreatePart(root, i);
                _parts[i].rectTransform.SetAsFirstSibling();   // 砖永远垫底，内容子件在其上
            }
            _built = true;
            Apply();
        }

        Image CreatePart(RectTransform root, int part)
        {
            var rect = new GameObject("Brick_" + PartNames[part], typeof(RectTransform))
                .GetComponent<RectTransform>();
            rect.SetParent(root, false);
            var image = rect.gameObject.AddComponent<Image>();
            image.type = part == 0 || part == 2 || part == 6 || part == 8
                ? Image.Type.Simple          // 四角 10×10
                : Image.Type.Tiled;          // 四边与中心逐砖平铺
            image.raycastTarget = false;

            const float b = SketchSkin.NineSliceBorder;
            switch (part)
            {
                case 0: Set(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(b, b)); break;
                case 1: Set(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(-2f * b, b)); break;
                case 2: Set(rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(b, b)); break;
                case 3: Set(rect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(b, -2f * b)); break;
                case 4: rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                        rect.pivot = new Vector2(0.5f, 0.5f);
                        rect.offsetMin = new Vector2(b, b); rect.offsetMax = new Vector2(-b, -b); break;
                case 5: Set(rect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(b, -2f * b)); break;
                case 6: Set(rect, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(b, b)); break;
                case 7: Set(rect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(-2f * b, b)); break;
                case 8: Set(rect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(b, b)); break;
            }
            return image;
        }

        static void Set(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 sizeDelta, Vector2 anchoredPosition = default)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
        }

        void Apply()
        {
            string slot = Slot;
            for (int i = 0; i < 9; i++)
            {
                if (_parts[i] == null)
                    continue;
                Sprite frame = SketchSkin.Frame(slot + "_" + PartNames[i], _tick + _phase);
                if (frame != null)
                    _parts[i].sprite = frame;
            }
        }
    }
}
