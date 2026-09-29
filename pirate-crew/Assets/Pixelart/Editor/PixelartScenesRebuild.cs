using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **像素口径全仓收敛**：把渲染配方默认值（色带档数 / 抖动 / 内线阈值 / 角色造型常量——
    /// 真源在 <see cref="PirateCrew.Rendering.Pixelart.PixelartMaterialFactory"/>、
    /// <see cref="PirateCrew.Rendering.Pixelart.PixelartCameraRig"/> 与 <c>CrewVisualPrefabBuilder</c>）
    /// 推平到全部资产与场景。配方改档后跑一次本类即全仓收敛，幂等可重跑。
    ///
    /// 【三段做什么】
    /// ① 角色网格 / 材质 / 预制体重建（<c>CrewVisualPrefabBuilder.BuildAll</c>）：
    ///    网格资产**就地刷新**（ApplyToMesh 不换 GUID）——所有已烘场景里剥好的角色
    ///    引用跟着自动变新几何，不需要重烘那些场景的角色；
    /// ② 材质配方推平（<see cref="PixelartMaterialRecipeSync.Run"/>）：全仓像素 .mat
    ///    按 Configure 覆写（色带 4 档 / Bayer 0.5），逐物体的色/档数/描边保留；
    /// ③ 像素场景装配器重跑：rig 的序列化值（aaScaler 等）回到当前常量。
    ///
    /// 【化工厂 L04/L05 有意不在 ③】它们有另一会话的**在途未提交修改**（场景文件 dirty），
    /// 重跑装配器会洗掉在途工作——其材质已由 ② 推平、角色网格由 ① 自动跟随，
    /// 只剩 rig 序列化值待该批修改收尾后单独跑各自装配器。
    ///
    /// 入口：菜单 <c>PirateCrew/Pixelart/重烘角色与全部像素场景</c>；
    /// 无头 <c>-executeMethod PirateCrew.EditorTools.PixelartScenesRebuild.FromCommandLine</c>。
    /// （Battle / M1 / M3 不在本类范围——走 ArtGate.BuildAll 整链。）
    /// </summary>
    public static class PixelartScenesRebuild
    {
        const string LogTag = "[PixelartScenesRebuild]";

        [MenuItem("PirateCrew/Pixelart/重烘角色与全部像素场景")]
        public static void BuildAll()
        {
            int failed = 0;
            Failed("角色网格 / 材质 / 7 职业预制体", () => CrewVisualPrefabBuilder.BuildAll(), ref failed);
            Failed("材质配方推平（全仓像素 .mat）", () => PixelartMaterialRecipeSync.Run(), ref failed);
            Failed("PixelartPilotSetup（试点）", () => PixelartPilotSetup.BuildAll(), ref failed);
            Failed("PixelartLevelPilotSetup（云/空岛）", () => PixelartLevelPilotSetup.BuildAll(), ref failed);
            Failed("PixelartGrassFieldSetup（草场）", () => PixelartGrassFieldSetup.BuildAll(), ref failed);
            Failed("PixelartWorldMapPilotSetup（海图 101–108）", () => PixelartWorldMapPilotSetup.BuildAll(), ref failed);
            Failed("PixelartCharCamDebugSetup（调试场）", () => PixelartCharCamDebugSetup.BuildAll(), ref failed);

            if (failed > 0)
                Debug.LogError(LogTag + " 完成，" + failed + " 步报错（见上方逐条 Error）。");
            else
                Debug.Log(LogTag + " 全部收敛完成。化工厂 L04/L05 有在途修改未重跑装配器"
                    + "（材质/角色已推平跟随，rig 序列化值待该批收尾后单独重跑）。");
        }

        /// <summary>命令行入口（batchmode 一次只能一个 executeMethod，用本方法串全部）。</summary>
        public static void FromCommandLine()
        {
            BuildAll();
        }

        static void Failed(string name, System.Action action, ref int failed)
        {
            try
            {
                Debug.Log(LogTag + " → " + name);
                action();
            }
            catch (System.Exception e)
            {
                failed++;
                Debug.LogError(LogTag + " " + name + " 抛异常：" + e);
            }
        }
    }
}
