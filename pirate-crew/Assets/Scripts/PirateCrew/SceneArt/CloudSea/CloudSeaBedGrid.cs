using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>
    /// 云端漫步海床的高度格（纯逻辑，无头可测）：烘焙时从海床网格采样成一张均匀方格，
    /// 运行时给鱼群 / 螃蟹查「脚下沙丘多高」。
    ///
    /// 【为什么要它】海床是起伏沙丘（脊顶与洼地差近 1 米）。活物若还靠一条固定的深度带夹
    /// 高度，要么扎进沙脊，要么为了不扎进去整群被顶到水面附近。查格定深让鱼群游过沙脊时
    /// 抬、游进洼地时沉——深度分层保得住，穿模从几何上排除。
    ///
    /// 【单源】格里的高度全部来自 Cloud_Sea_Bed 网格本身（烘焙器读顶点），不另抄一份
    /// Blender 的高度公式——两处公式迟早漂移。
    ///
    /// 【坐标】格子存在 CloudWalk 根的本地 XZ 上（根无旋转缩放 ⇒ 本地 Y == 世界 Y）。
    /// </summary>
    public static class CloudSeaBedGrid
    {
        /// <summary>
        /// 从海床顶点采样。方格覆盖 [-halfSize, halfSize]²，共 (cells + 1) × (cells + 1) 个
        /// 格点（行主序：Z 行 X 列）。每个格点取周围半格内**最高**的顶点——对活物而言
        /// 「最坏情况的高度」才是不能穿的那条线，取平均会让格点落在沙脊下面。
        /// </summary>
        public static float[] Build(IList<Vector3> points, int cells, float halfSize)
        {
            int width = cells + 1;
            float cell = 2f * halfSize / cells;
            var heights = new float[width * width];
            for (int i = 0; i < heights.Length; i++)
                heights[i] = float.NegativeInfinity;

            for (int p = 0; p < points.Count; p++)
            {
                Vector3 v = points[p];
                int ix = Mathf.RoundToInt((v.x + halfSize) / cell);
                int iz = Mathf.RoundToInt((v.z + halfSize) / cell);
                if (ix < 0 || ix > cells || iz < 0 || iz > cells)
                    continue;   // 方格覆盖范围之外的海床（远景边缘）不入格
                int index = iz * width + ix;
                if (v.y > heights[index])
                    heights[index] = v.y;
            }

            return FillEmpty(heights, width);
        }

        /// <summary>
        /// 双线性查高。方格之外钳到边缘格点（边缘高度向外延展）——活物游出覆盖范围时
        /// 至少贴着边缘的沙丘走，不会突然掉高度。
        /// </summary>
        public static float Sample(float[] heights, int cells, float halfSize, float x, float z)
        {
            int width = cells + 1;
            float cell = 2f * halfSize / cells;
            float fx = Mathf.Clamp((x + halfSize) / cell, 0f, cells);
            float fz = Mathf.Clamp((z + halfSize) / cell, 0f, cells);
            int ix = Mathf.Min((int)fx, cells - 1);
            int iz = Mathf.Min((int)fz, cells - 1);
            float tx = fx - ix;
            float tz = fz - iz;

            float a = Mathf.Lerp(heights[iz * width + ix], heights[iz * width + ix + 1], tx);
            float b = Mathf.Lerp(heights[(iz + 1) * width + ix], heights[(iz + 1) * width + ix + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        /// <summary>
        /// 没采到顶点的格点（海床网格没铺到的角落）取四邻的最高值，逐圈向内填到没有空格。
        /// </summary>
        static float[] FillEmpty(float[] heights, int width)
        {
            int empty = 0;
            for (int i = 0; i < heights.Length; i++)
            {
                if (float.IsNegativeInfinity(heights[i]))
                    empty++;
            }

            while (empty > 0)
            {
                bool progressed = false;
                for (int iz = 0; iz < width; iz++)
                {
                    for (int ix = 0; ix < width; ix++)
                    {
                        int index = iz * width + ix;
                        if (!float.IsNegativeInfinity(heights[index]))
                            continue;

                        float best = float.NegativeInfinity;
                        if (ix > 0) best = Mathf.Max(best, heights[index - 1]);
                        if (ix < width - 1) best = Mathf.Max(best, heights[index + 1]);
                        if (iz > 0) best = Mathf.Max(best, heights[index - width]);
                        if (iz < width - 1) best = Mathf.Max(best, heights[index + width]);
                        if (float.IsNegativeInfinity(best))
                            continue;   // 四邻也都还空着，等下一圈

                        heights[index] = best;
                        empty--;
                        progressed = true;
                    }
                }

                if (!progressed)
                    break;   // 整张方格一个顶点都没有：不算海床，高度按 0
            }

            for (int i = 0; i < heights.Length; i++)
            {
                if (float.IsNegativeInfinity(heights[i]))
                    heights[i] = 0f;
            }
            return heights;
        }
    }
}
