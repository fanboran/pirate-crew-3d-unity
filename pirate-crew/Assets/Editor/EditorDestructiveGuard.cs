using System;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 破坏性 Editor 工具的人工确认闸（审计 T2/P1）：把「重建会顶掉场景」「删除资产无法撤销」
    /// 这两类有损动作的确认集中到一处，交互模式下弹对话框拦一道，程序化流程零打扰。
    ///
    /// 【样板出处】BattleSceneSetup.ConfirmSceneReplacement（本类由此抽共）：
    ///   · 活动场景有未保存修改 → 直接拒绝（提示先保存，不做任何动作）；
    ///   · 场景干净 → 弹一次「继续 / 取消」。
    ///
    /// 【放行口径：程序化链路必须零打扰】这些工具的无头用法（run.sh / chain 链 / 出图命令行）
    /// 依赖「无人应答对话框也不会卡住」：
    ///   · batchmode —— 对话框根本不显示；
    ///   · <see cref="HeadlessChain.IsRunning"/> —— 链式器在**同步**执行装配步骤（batchmode
    ///     入口与常驻编辑器命令桥两个入口），弹框会把整条链（进而命令桥的 Poll）卡死在模态等待上；
    ///   · 命令行 -executeMethod 拉起的进程 —— 出图链（TextSampleBuilder.CaptureSample、
    ///     WaterLabSetup.BuildSceneAndCapture、UiShowcaseSceneSetup.CaptureFromCommandLine 等）
    ///     必须带图形跑、不带 -batchmode，同样无人值守。启动参数在进程整个生命周期保留，
    ///     据此识别；该进程本就是自动化用途，后续在里面手工点菜单也不再弹框，是接受的代价。
    /// </summary>
    public static class EditorDestructiveGuard
    {
        /// <summary>
        /// 场景替换确认：重建类装配器在 <c>NewScene(Single)</c> / <c>OpenScene(Single)</c>
        /// 把当前打开场景直接顶掉之前调用。用户拒绝时返回 false，调用方应直接放弃（什么都没动）。
        /// </summary>
        /// <param name="what">动作名，用菜单文案口径（如「重建 Battle 战斗场景」），作对话框标题。</param>
        public static bool ConfirmSceneReplacement(string what)
        {
            if (IsProgrammaticRun())
                return true;

            Scene active = EditorSceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty)
            {
                EditorUtility.DisplayDialog(
                    what,
                    "当前场景 \"" + (string.IsNullOrEmpty(active.path) ? active.name : active.path)
                        + "\" 有未保存的修改，" + what + "会直接丢弃它。\n请先保存当前场景，再执行。",
                    "知道了");
                return false;
            }

            return EditorUtility.DisplayDialog(
                what,
                what + "会新建场景并覆盖保存产物，当前打开的场景将被直接关闭（未保存内容会丢失）。\n确定继续吗？",
                "继续",
                "取消");
        }

        /// <summary>
        /// 资产删除确认：<c>AssetDatabase.DeleteAsset</c> 前调用。删除无法从编辑器撤销。
        /// 调用方先把同批删除收集齐、数出 <paramref name="count"/> 再问一次（不要逐个弹框）；
        /// count ≤ 0（本次实际没有东西可删）直接放行，不弹框。
        /// </summary>
        public static bool ConfirmAssetDeletion(string what, int count)
        {
            if (count <= 0 || IsProgrammaticRun())
                return true;

            return EditorUtility.DisplayDialog(
                what,
                what + "将删除 " + count + " 个现有资产，删除后无法从编辑器撤销。\n确定继续吗？",
                "继续删除",
                "取消");
        }

        /// <summary>目录下资产计数（供调用方统计删除量；目录不存在或为空返回 0）。</summary>
        public static int CountAssetsInFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
                return 0;
            return AssetDatabase.FindAssets("", new[] { folder }).Length;
        }

        /// <summary>程序化运行判定，见类注释「放行口径」。</summary>
        static bool IsProgrammaticRun()
        {
            if (Application.isBatchMode || HeadlessChain.IsRunning)
                return true;

            string[] args = Environment.GetCommandLineArgs();
            foreach (string arg in args)
                if (arg == "-executeMethod")
                    return true;
            return false;
        }
    }
}
