namespace PirateCrew.ArtReview
{
    /// <summary>
    /// 美术评审的关卡号覆盖（无头出图专用）。由 PlayerArtCapture 解析
    /// 命令行 <c>-artReviewLevel N</c> 写入；&lt;=0 表示未启用，玩法走正常选关链。
    /// 独立成静态类是因为 BattleController 不能反向引用采集协程类型。
    /// </summary>
    public static class ArtReviewCaptureOverride
    {
        /// <summary>
        /// 出图覆盖的关卡号，**随出图会话生死**：写入方 <c>PlayerArtCapture</c> 在会话销毁
        /// （OnDestroy）时归零——静态字段若跨 Play 残留，会劫持选关解析
        /// （消费点 <c>Battle/Levels/LevelSourceResolver</c> 的 ① 级出图覆盖）。
        /// </summary>
        public static int LevelNumber;
    }
}
