namespace PirateCrew.PirateCrew.ArtReview
{
    /// <summary>
    /// 美术评审的关卡号覆盖（无头出图专用）。由 PlayerArtCapture 解析
    /// 命令行 <c>-artReviewLevel N</c> 写入；&lt;=0 表示未启用，玩法走正常选关链。
    /// 独立成静态类是因为 BattleController 不能反向引用采集协程类型。
    /// </summary>
    public static class ArtReviewCaptureOverride
    {
        public static int LevelNumber;
    }
}
