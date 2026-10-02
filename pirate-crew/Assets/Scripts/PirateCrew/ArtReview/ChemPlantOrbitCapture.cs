using System.IO;
using PirateCrew.Core;
using UnityEngine;

namespace PirateCrew.ArtReview
{
    /// <summary>
    /// 【临时诊断工具】绕场环绕采集：进战斗后禁用跟随相机，绕场地中心 360°×3 层高度拍照，
    /// 用于复现创始人报告的「地面垂直」——正常出图通道的固定机位看不到的东西，环绕一圈就能看到。
    /// 用完即删，不入库口径。
    ///
    /// 用法：播放器 -orbitOut &lt;绝对目录&gt; -orbitLevel 4|5
    /// </summary>
    public static class ChemPlantOrbitCapture
    {
        [GameBootstrap(GameBootstrapPhase.Initialize, order: 150)]
        internal static void Install()
        {
            string outDir = CommandLineOptions.GetValue(ToolFlags.OrbitOut);
            if (string.IsNullOrEmpty(outDir)
                || !CommandLineOptions.TryGetInt(ToolFlags.OrbitLevel, out int level)
                || level <= 0)
                return;

            outDir = Path.GetFullPath(outDir);
            ArtReview.ArtReviewCaptureOverride.LevelNumber = level;   // 复用官方关卡覆盖通道
            var go = new GameObject("[ChemPlantOrbitCapture]");
            go.AddComponent<OrbitRunner>().Setup(outDir, level);
            Object.DontDestroyOnLoad(go);
        }
    }
}
