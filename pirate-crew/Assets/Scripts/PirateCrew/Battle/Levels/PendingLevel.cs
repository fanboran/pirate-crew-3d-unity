using PirateCrew.Core;
using PirateCrew.Data;

namespace PirateCrew.Battle.Levels
{
    /// <summary>
    /// 待战关卡槽：选关页点哪关、启动即进哪关（<c>-bootBattle N</c>）都写在这里，
    /// 进战斗由 <see cref="LevelSourceResolver"/> 读取。关卡=唯一概念（创始人 2026-10-23 裁决），
    /// 不再有按内容种类分的多个待战槽。
    ///
    /// 【与 BattleController 的契约】进图优先级由 <c>LevelSourceResolver</c> 一处决定：
    /// 出图覆盖（-artReviewLevel）&gt; 待战关卡 &gt; 兜底关 1（直接 Play）。
    /// </summary>
    public static class PendingLevel
    {
        static int _levelNumber;
        static bool _commandLineScanned;

        /// <summary>设定待战关卡（关卡号必须已加载出关卡资产，否则返回 false 且不改动现状）。</summary>
        public static bool SetPending(int levelNumber)
        {
            if (!LevelAssetLibrary.TryGetLevel(levelNumber, out _))
                return false;
            _levelNumber = levelNumber;
            return true;
        }

        /// <summary>取待战关卡号；返回 false 表示本局不是选关页/命令行点进来的。</summary>
        public static bool TryGetPending(out int levelNumber)
        {
            ScanCommandLine();
            levelNumber = _levelNumber;
            return levelNumber > 0;
        }

        /// <summary>清掉待战关卡（静态复位与测试域隔离用）。</summary>
        public static void ClearPending() => _levelNumber = 0;

        /// <summary>
        /// 关闭 Domain Reload 时静态字段不会自动清空，进入播放前强制重置
        /// （由唯一入口 <c>Core/GameEntryPoint</c> 调用）；命令行扫描标志一并复位，
        /// 让每次播放重新取 <c>-bootBattle</c>。关卡资产缓存（<c>LevelAssetLibrary</c>）
        /// 同批清空——同一播放里改了资产要能重读。
        /// </summary>
        [GameBootstrap(GameBootstrapPhase.ResetStatics, order: 20)]
        internal static void ResetStatics()
        {
            ClearPending();
            _commandLineScanned = false;
            LevelAssetLibrary.Reset();
        }

        /// <summary>
        /// 取命令行指定的启动关卡（<c>-bootBattle &lt;关卡号&gt;</c>）。argv 由
        /// <see cref="CommandLineOptions"/> 统一解析（唯一入口解析一次，本类不自扫命令行）；
        /// 只扫描一次并缓存结果（关卡号未登记时告警并忽略）。
        /// </summary>
        static void ScanCommandLine()
        {
            if (_commandLineScanned)
                return;
            _commandLineScanned = true;

            string boot = CommandLineOptions.GetValue(ToolFlags.BootBattle);
            if (string.IsNullOrEmpty(boot) || !int.TryParse(boot, out int bootLevel) || bootLevel <= 0)
                return;

            if (SetPending(bootLevel))
            {
                Log.Info("[PendingLevel] -bootBattle " + bootLevel + "：启动即进关卡 " + bootLevel + "。");
            }
            else
            {
                Log.Warn("[PendingLevel] -bootBattle " + boot
                    + " 不是已登记的关卡号（关卡表里没有它），忽略。");
            }
        }
    }
}
