using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    ///         想直接进某一关就用启动参数 <c>-bootBattle &lt;关卡号|海图id&gt;</c> 一起给
    ///         （那条参数是运行时读的，编辑器 Play 同样吃）。</item>
    /// </list>
    ///
    /// 【注意】进 Play 前会先保存当前场景（未保存的改动不该被 Play 吃掉）。
    /// </summary>
    public static class EditorPlayBattle
    {
        const string BattleScenePath = "Assets/Scenes/Battle.unity";

        [MenuItem("PirateCrew/评审/进战斗（打开 Battle 并 Play）", priority = 0)]
        public static void EnterBattle()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.Log("[EditorPlayBattle] 已在 Play 模式，先停下来再重进。");
                EditorApplication.isPlaying = false;
                return;
            }

            if (!System.IO.File.Exists(BattleScenePath))
            {
                Debug.LogError("[EditorPlayBattle] 找不到场景：" + BattleScenePath);
                return;
            }

            // 先存场景：Play 会以磁盘上的场景为准，未保存的改动不该被悄悄丢掉或被带进 Play。
            EditorSceneManager.SaveOpenScenes();

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != BattleScenePath)
                EditorSceneManager.OpenScene(BattleScenePath, OpenSceneMode.Single);

            Debug.Log("[EditorPlayBattle] 进入 Play（场景 " + BattleScenePath + "）。"
                + "相机：右键拖 = 方位随便转，俯角锁 30°；滚轮改档，右下提示条显示「镜头 N m」。");
            EditorApplication.isPlaying = true;
        }
    }
}
