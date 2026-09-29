using UnityEngine;

namespace PirateCrew.SceneArt.Showcase
{
    /// <summary>
    /// 草坪三档斑块选色（纯 C#，无头可测）——t3ssel8r「Pixel Art Grass Shader V2」口径的几何版：
    /// 草簇颜色不逐簇掷随机，而是按**世界坐标低频值噪声 + 双阈值**选档，
    /// 让亮/暗草皮连成**空间连贯的大斑块**（相邻簇同色），而非彩色纸屑。
    /// 口径出处：external/ref/unity-isometric-pixel-pipeline 的 GrassBlade.shader
    /// （_Noise2/_Noise3 世界噪声 + _Noise2Threshold/_Noise3Threshold；作者自述见
    /// docs/技术/渲染/参照-t3ssel8r像素引擎.md §1.10）。【AI 提案】尺度/阈值待实机出图裁决。
    ///
    /// 【档位即既有材质槽】0=GrassMid（主底色）/ 1=GrassLight（亮斑）/ 2=GrassDark（暗斑），
    /// 复用空岛草皮三档材质（<see cref="FloatingIslandMaterials"/>），不新增 DrawCall。
    /// </summary>
    public static class GrassPatchRules
    {
        /// <summary>亮斑特征尺度（世界单位；对应原版 _Noise2Scale 的低频档）。</summary>
        public const float LightPatchSize = 5.5f;

        /// <summary>暗斑特征尺度（更碎一档；对应 _Noise3Scale 的高频档）。</summary>
        public const float DarkPatchSize = 3.2f;

        /// <summary>亮斑阈值（原版默认 0.604；本仓值噪声双线性分布下按覆盖面实测校准——
        /// 无头测试 <c>GrassPatchRulesTests.SelectBand_IslandFootprint_AllThreeBandsOccur_AndMidDominates</c>
        /// 钉住「亮/暗斑只是点缀、Mid 主导」）。</summary>
        public const float LightThreshold = 0.72f;

        /// <summary>暗斑阈值（原版默认 0.661；校准口径同上）。</summary>
        public const float DarkThreshold = 0.74f;

        // 值噪声格点盐（取冷号段，与几何层其他 SceneArtHash 用途互不串色）。
        const int SaltLight = 9101;
        const int SaltDark = 9107;

        /// <summary>某世界位置的草皮档位：0=Mid / 1=Light / 2=Dark。确定性：同位置恒同档。</summary>
        public static int SelectBand(Vector3 worldPos)
        {
            if (ValueNoise(worldPos.x, worldPos.z, LightPatchSize, SaltLight) > LightThreshold)
                return 1;
            if (ValueNoise(worldPos.x, worldPos.z, DarkPatchSize, SaltDark) > DarkThreshold)
                return 2;
            return 0;
        }

        /// <summary>档位 → 空岛草皮缓冲（岛总装用；档位复用既有三档材质槽）。</summary>
        public static MeshBuffers SelectBuffer(IslandBuffers buffers, Vector3 worldPos)
        {
            switch (SelectBand(worldPos))
            {
                case 1: return buffers.GrassLight;
                case 2: return buffers.GrassDark;
                default: return buffers.GrassMid;
            }
        }

        /// <summary>
        /// 二维值噪声（格点哈希双线性 + smoothstep 渐变）：<see cref="SceneArtHash"/> 是纯整数混哈希，
        /// 直接吃浮点世界坐标需先落在整数格点上——斑块特征尺度由 cellSize 拉开。
        /// </summary>
        static float ValueNoise(float x, float z, float cellSize, int salt)
        {
            float fx = x / cellSize;
            float fz = z / cellSize;
            int ix = Mathf.FloorToInt(fx);
            int iz = Mathf.FloorToInt(fz);
            float tx = Smooth(fx - ix);
            float tz = Smooth(fz - iz);

            float h00 = SceneArtHash.Hash01(salt, ix, iz);
            float h10 = SceneArtHash.Hash01(salt, ix + 1, iz);
            float h01 = SceneArtHash.Hash01(salt, ix, iz + 1);
            float h11 = SceneArtHash.Hash01(salt, ix + 1, iz + 1);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
