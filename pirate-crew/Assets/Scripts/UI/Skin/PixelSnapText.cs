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

            var uiVertex = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref uiVertex, i);
                uiVertex.position = new Vector3(
                    Mathf.Round(uiVertex.position.x),
                    Mathf.Round(uiVertex.position.y),
                    uiVertex.position.z);
                vh.SetUIVertex(uiVertex, i);
            }
        }
    }
}
