using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PirateCrew.Core;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **编辑器里一键进战斗**（评审用）：打开 Battle 场景并直接进 Play 模式。
    ///
    /// 【为什么要有它】"看一眼、转一转"这类验收**不需要出包**：编辑器里 Play 就是同一套渲染
    /// 与同一份场景，几秒钟的事。出包只在两种情况才必要——① 出图链路（判据要在播放器里跑）、
    /// ② 要一个能双击的独立窗口。以前每次验收都走"烘场景 + 出包"（5–10 分钟），等的就是这几十秒的活。
    ///
    /// 【怎么用】
    /// <list type="bullet">
    ///   <item>菜单：<c>PirateCrew/评审/进战斗（打开 Battle 并 Play）</c>；</item>
    ///   <item>命令行（编辑器会开着、不退出）：<c>-executeMethod PirateCrew.EditorTools.EditorPlayBattle.EnterBattle</c>。
    ///         想直接进某一关就再加 <c>-bootBattle &lt;关卡号|海图id&gt;</c>：此时改走
    ///         <see cref="BootScenePath"/>，与播放器完全同一条路（Bootstrapper 直跳该关，
    ///         <see cref="PirateCrew.Battle.WorldMaps.WorldMapRuntime"/> 读同一个开关），验收画面与 exe 一致。</item>
    /// </list>
    ///
    /// 【注意】进 Play 前会先保存当前场景（未保存的改动不该被 Play 吃掉）。
    /// </summary>
    public static class EditorPlayBattle
    {
        const string BattleScenePath = "Assets/Scenes/Battle.unity";
        const string BootScenePath = "Assets/Scenes/Bootstrapper.unity";

        [MenuItem("PirateCrew/评审/进战斗（打开 Battle 并 Play）", priority = 0)]
        public static void EnterBattle()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.Log("[EditorPlayBattle] 已在 Play 模式，先停下来再重进。");
                EditorApplication.isPlaying = false;
                return;
            }

            // 进程参数带 -bootBattle 时走 Bootstrapper：与播放器同一条启动链
            // （Bootstrapper.StartScene() 直跳 Battle，WorldMapRuntime 再按开关进对应关），
            // 不带开关时（菜单点进来）保持"打开 Battle 直接 Play"的捷径。
            bool viaBoot = CommandLineOptions.Has(ToolFlags.BootBattle);
            string scenePath = viaBoot ? BootScenePath : BattleScenePath;

            if (!System.IO.File.Exists(scenePath))
            {
                Debug.LogError("[EditorPlayBattle] 找不到场景：" + scenePath);
                return;
            }

            // 先存场景：Play 会以磁盘上的场景为准，未保存的改动不该被悄悄丢掉或被带进 Play。
            EditorSceneManager.SaveOpenScenes();

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != scenePath)
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            Debug.Log("[EditorPlayBattle] 进入 Play（场景 " + scenePath
                + (viaBoot ? "，-bootBattle " + (CommandLineOptions.GetValue(ToolFlags.BootBattle) ?? "") : "")
                + "）。相机：右键拖 = 方位随便转，俯角锁 30°；滚轮改档，右下提示条显示「镜头 N m」。");
            EditorApplication.isPlaying = true;
        }
    }
}
