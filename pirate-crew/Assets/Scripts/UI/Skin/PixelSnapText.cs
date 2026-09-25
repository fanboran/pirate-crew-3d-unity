using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// TMP 文字顶点像素对齐（BaseMeshEffect）：TMP 排版是浮点的（行高/基线/居中除法），
    /// 12px 位图字落在半画布格上会被 2× 屏幕放大摊成两行各半亮度——「笔画粗细不一、
    /// 横线发浅」的文字侧元凶。本组件把每个网格顶点 round 到画布像素网格（1 格 = 1 贴图
    /// 像素 = 1 画布像素，×1 终局），字形笔画回到整数格上，2× 放大后每笔画占整屏行。
    /// UiKit.CreateText / RuntimeUiBuilder.CreateText / SketchButton 统一挂载，勿裸加 TMP。
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class PixelSnapText : BaseMeshEffect
    {
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!isActiveAndEnabled || vh.currentVertCount == 0)
                return;

            // 【按 quad 一致取整，禁逐顶点独立 round】逐顶点 round 会把 2.2 格的 quad
            // 撑成 3 格（左缘 100.4→100、右缘 102.6→103）——"2 模成 3"的笔画粗细抖动
            // 正是这么来的。每 4 顶点一组（TMP 字符 quad）：min/max 各自 round，
            // 尺寸 = round(max) - round(min)，四角按原象限归位（UV/顶点色不动）。
            var v = new UIVertex();
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
                float nx = Mathf.Round(minX), ny = Mathf.Round(minY);
                float sx = Mathf.Max(nx + 1f, Mathf.Round(maxX) - nx);   // 至少 1 格
                float sy = Mathf.Max(ny + 1f, Mathf.Round(maxY) - ny);
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
    }
}
