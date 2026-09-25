using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// TMP 文字顶点像素对齐（BaseMeshEffect）：TMP 排版是浮点的（行高/基线/居中除法），
    /// 12px 位图字落在半画布格上时，字形位图的行列就压不到画布像素边界（实拍：笔画落在
    /// 奇数屏幕行，整块字比它的盒子偏 1 屏幕像素）——「文字没对齐显示像素」的根因。
    /// 本组件把每个网格顶点 round 到**画布像素**网格（1 格 = 1 贴图像素 = 1 画布像素，
    /// ×1 终局），2× 屏幕放大后每笔画正好占整屏行对。
    ///
    /// 【必须按画布空间取整，不能只 round 局部坐标】局部坐标是相对 rect 原点的：父布局把
    /// rect 摆到半格上时（按钮内居中、奇宽模态卡居中、27 高行里居中的 15 高盒子），局部整数
    /// 仍是半格——旧实现只 round 局部，实测主菜单四个按钮字、设置页选项块字全落奇数屏幕列。
    /// 这里把本地原点换算到画布像素空间取相位，再按相位取整：等价于"先在画布空间 round，
    /// 再换回局部"，与 rect 停在哪儿无关。
    ///
    /// UiKit.CreateText / RuntimeUiBuilder.CreateText / SketchButton 统一挂载，勿裸加 TMP。
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class PixelSnapText : BaseMeshEffect
    {
        private float _appliedPhaseX, _appliedPhaseY;
        private bool _hasApplied;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!isActiveAndEnabled || vh.currentVertCount == 0)
                return;

            // 本地原点在画布像素空间的相位（分数部分）：画布根 RectTransform 的局部单位
            // 就是画布像素（960×540 设计分辨率），故 worldToLocal 后的分数部分即相位偏移。
            float phaseX = 0f, phaseY = 0f;
            TryGetPhase(out phaseX, out phaseY);
            _appliedPhaseX = phaseX;
            _appliedPhaseY = phaseY;
            _hasApplied = true;

            // 【按 quad 一致取整，禁逐顶点独立 round】逐顶点 round 会把 2.2 格的 quad
            // 撑成 3 格（左缘 100.4→100、右缘 102.6→103）——"2 模成 3"的笔画粗细抖动
            // 正是这么来的。每 4 顶点一组（TMP 字符 quad）：min/max 各自 round，
            // 尺寸 = round(max) - round(min)，四角按原象限归位（UV/顶点色不动）。
            int count = vh.currentVertCount;
            for (int i = 0; i + 3 < count; i += 4)
            {
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                var corners = new UIVertex[4];
                for (int k = 0; k < 4; k++)
                {
                    vh.PopulateUIVertex(ref corners[k], i + k);
                    Vector3 p = corners[k].position;
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                }
                float nx = Mathf.Round(minX + phaseX) - phaseX;
                float ny = Mathf.Round(minY + phaseY) - phaseY;
                float sx = Mathf.Max(nx + 1f, Mathf.Round(maxX + phaseX) - phaseX - nx);   // 至少 1 格
                float sy = Mathf.Max(ny + 1f, Mathf.Round(maxY + phaseY) - phaseY - ny);
                float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = corners[k].position;
                    p.x = p.x < cx ? nx : nx + sx;
                    p.y = p.y < cy ? ny : ny + sy;
                    corners[k].position = p;
                    vh.SetUIVertex(corners[k], i + k);
                }
            }
        }

        /// <summary>
        /// **相位变化即重算网格**。相位是在 <see cref="ModifyMesh"/> 里烘焙进顶点的，
        /// 而 TMP 只在文本"变脏"时重建网格——面板开合动效把 rect 又挪了半格时，
        /// 烘进去的仍是旧相位，字就停在半格上（实拍：设置面板滑条内的百分比文字
        /// 在开启动效后整列落在奇数屏幕像素上，392 条半格边缘）。这里每帧比一次相位，
        /// 变了就把顶点标脏，动画停稳后自动回到整格。
        /// </summary>
        private void LateUpdate()
        {
            float phaseX, phaseY;
            if (!TryGetPhase(out phaseX, out phaseY))
                return;
            if (_hasApplied
                && Mathf.Abs(phaseX - _appliedPhaseX) < 0.001f
                && Mathf.Abs(phaseY - _appliedPhaseY) < 0.001f)
                return;

            Graphic owner = graphic;
            if (owner != null)
                owner.SetVerticesDirty();
        }

        /// <summary>本地原点在画布像素空间的相位（分数部分）；取不到画布时返回 (0,0)。</summary>
        private bool TryGetPhase(out float phaseX, out float phaseY)
        {
            phaseX = 0f;
            phaseY = 0f;
            Graphic owner = graphic;
            Canvas canvas = owner != null ? owner.canvas : null;
            if (canvas == null)
                return false;

            Matrix4x4 toCanvas = canvas.rootCanvas.transform.worldToLocalMatrix
                * owner.transform.localToWorldMatrix;
            Vector3 origin = toCanvas.MultiplyPoint3x4(Vector3.zero);
            phaseX = origin.x - Mathf.Round(origin.x);
            phaseY = origin.y - Mathf.Round(origin.y);
            return true;
        }
    }
}
