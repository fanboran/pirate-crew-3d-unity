namespace PirateCrew.UI
{
    /// <summary>
    /// 全界面中文文案表（**唯一来源**）。
    ///
    /// 【出处】docs/UI-UX与中文本地化规范.md §4.2–§4.9 逐条落地；每条文案的出处/标注见规范表。
    /// 【纪律】
    ///   · 任何用户可见文本都必须从这里取，禁止在控制器 / 构建器里散落字符串字面量；
    ///   · 含 {0} 的是 <c>string.Format</c> 模板，配套格式化函数在 <see cref="UiTextRules"/>；
    ///   · 全表为纯 C# 常量，可在无头验证台做「无英文残留」断言（见 Tests/UI/UiStringsTests.cs）。
    /// 【例外】品牌名「3D」、按键字母（Esc / WASD / E / W / S）与版本号数字按规范保留半角，
    ///         它们是文案规范明确允许的例外（§4.1、§5.4）。
    /// </summary>
    public static class UiStrings
    {
        // ==================================================================
        // 通用（§4.8）
        // ==================================================================

        /// <summary>通用：确定。</summary>
        public const string Confirm = "确定";

        /// <summary>通用：取消。</summary>
        public const string Cancel = "取消";

        /// <summary>通用：是。</summary>
        public const string Yes = "是";

        /// <summary>通用：否。</summary>
        public const string No = "否";

        /// <summary>通用：关闭。</summary>
        public const string Close = "关闭";

        /// <summary>通用：返回。</summary>
        public const string Back = "返回";

        /// <summary>通用：返回主菜单。</summary>
        public const string BackToMainMenu = "返回主菜单";

        /// <summary>通用：载入中……。</summary>
        public const string Loading = "载入中……";

        /// <summary>通用：返回确认弹窗正文。</summary>
        public const string BackConfirm = "确定要返回主菜单吗？本局进度不会保留。";

        // ==================================================================
        // 队伍与术语（§4.1 术语统一）
        // ==================================================================

        /// <summary>红队。</summary>
        public const string TeamRed = "红队";

        /// <summary>蓝队。</summary>
        public const string TeamBlue = "蓝队";

        // ==================================================================
        // 主菜单（§4.2）
        // ==================================================================

        /// <summary>主菜单标题（品牌名 3D 保留半角，规范允许）。</summary>
        public const string MainTitle = "海盗军团夺宝 3D";

        /// <summary>主菜单：进入战斗（非战役快捷入口）。</summary>
        public const string MainBattle = "进入战斗";

        /// <summary>主菜单：单人战役。</summary>
        public const string MainCampaign = "单人战役";

        /// <summary>主菜单：船员管理。</summary>
        public const string MainCrew = "船员管理";

        /// <summary>主菜单：设置。</summary>
        public const string MainSettings = "设置";

        /// <summary>主菜单：退出游戏（危险态）。</summary>
        public const string MainQuit = "退出游戏";

        /// <summary>主菜单状态：已读取存档。</summary>
        public const string MainStatusLoaded = "已读取存档";

        /// <summary>主菜单状态：无存档。</summary>
        public const string MainStatusNoSave = "暂无存档";

        /// <summary>主菜单左下版本号（版本号数字保留半角）。</summary>
        public const string MainVersion = "版本 1.0";

        /// <summary>主菜单：退出确认弹窗正文。</summary>
        public const string MainQuitConfirm = "确定要退出游戏吗？";

        /// <summary>主菜单状态：设置已保存。</summary>
        public const string MainStatusSettingsSaved = "设置已保存。";

        // ==================================================================
        // 船员管理（§4.3）
        // ==================================================================

        /// <summary>船员管理标题。</summary>
        public const string CrewTitle = "船员管理";

        /// <summary>船员管理顶部概况模板。</summary>
        public const string CrewSummaryFormat = "编成 {0}/{1}：{2}　已拥有 {3}/{4}　总星数 {5}";

        /// <summary>船员管理：未编成占位。</summary>
        public const string CrewSummaryEmpty = "（未编成）";

        /// <summary>船员管理行文本模板（等级 / 经验，去掉既有 Lv./XP 混排）。</summary>
        public const string CrewRowFormat = "{0}　等级 {1}　经验 {2}";

        /// <summary>船员管理行文本模板（未解锁）。</summary>
        public const string CrewRowLockedFormat = "{0}　（累计 {1} 星后招募）";

        /// <summary>船员管理动作：上阵。</summary>
        public const string CrewEnlist = "上阵";

        /// <summary>船员管理动作：取消上阵。</summary>
        public const string CrewRemove = "取消上阵";

        /// <summary>船员管理动作：未解锁。</summary>
        public const string CrewLocked = "未解锁";

        /// <summary>船员管理按钮：选择关卡。</summary>
        public const string CrewLevelSelect = "选择关卡";

        /// <summary>船员管理按钮：保存进度。</summary>
        public const string CrewSave = "保存进度";

        /// <summary>船员管理状态：换下。</summary>
        public const string CrewStatusRemovedFormat = "已把 {0} 换下。";

        /// <summary>船员管理状态：编入。</summary>
        public const string CrewStatusEnlistedFormat = "已把 {0} 编入阵容。";

        /// <summary>船员管理状态：满编。</summary>
        public const string CrewStatusFullFormat = "编成上限 {0} 人，先换下一名再编入。";

        /// <summary>船员管理状态：空名册。</summary>
        public const string CrewStatusEmptyRoster = "至少要编入 1 名船员才能出战。";

        /// <summary>船员管理状态：存档成功。</summary>
        public const string CrewStatusSavedFormat = "已保存进度到槽位 {0}。";

        /// <summary>船员管理状态：存档失败（去 Bootstrapper 术语）。</summary>
        public const string CrewStatusSaveFailed = "存档不可用，请从主菜单重新进入。";

        /// <summary>船员管理状态：新船员加入。</summary>
        public const string CrewStatusNewCrewFormat = "新船员加入：{0}";

        /// <summary>船员管理状态：通关。</summary>
        public const string CrewStatusClearedFormat = "{0} 通关（{1} 星），经验已发放给编成阵容。";

        /// <summary>船员管理状态：挑战失败。</summary>
        public const string CrewStatusFailedFormat = "{0} 挑战失败，编成保留，可再战。";

        // ==================================================================
        // 六职业名 / 描述 / 短标语（§4.3；描述取自 GDD §5.2）
        // ==================================================================

        /// <summary>职业名：水手。</summary>
        public const string CrewSailor = "水手";

        /// <summary>职业名：炮手。</summary>
        public const string CrewGunner = "炮手";

        /// <summary>职业名：狙击手。</summary>
        public const string CrewSniper = "狙击手";

        /// <summary>职业名：钩子手。</summary>
        public const string CrewHooker = "钩子手";

        /// <summary>职业名：纵火狂。</summary>
        public const string CrewArsonist = "纵火狂";

        /// <summary>职业名：骷髅海盗。</summary>
        public const string CrewSkeleton = "骷髅海盗";

        /// <summary>职业描述：水手。</summary>
        public const string CrewSailorDesc = "均衡基准，没有特技，什么都能干。";

        /// <summary>职业描述：炮手。</summary>
        public const string CrewGunnerDesc = "双重爆破：同时扔出 2 个炸弹，第二颗偏移 ±15%。";

        /// <summary>职业描述：狙击手。</summary>
        public const string CrewSniperDesc = "精准射击：消耗 2 行动点，换一次无弹道下坠的直线射击。";

        /// <summary>职业描述：钩子手。</summary>
        public const string CrewHookerDesc = "抓钩位移：沿直线快速移动到目标点（无弧线）。";

        /// <summary>职业描述：纵火狂。</summary>
        public const string CrewArsonistDesc = "火焰路径：移动后原地留下持续 2 回合的火焰区域。";

        /// <summary>职业描述：骷髅海盗。</summary>
        public const string CrewSkeletonDesc = "不死之身：被击杀后 1 回合原地复活一次（50% 生命）。";

        /// <summary>职业短标语：水手【AI 提案】。</summary>
        public const string CrewSailorTag = "稳";

        /// <summary>职业短标语：炮手【AI 提案】。</summary>
        public const string CrewGunnerTag = "双响";

        /// <summary>职业短标语：狙击手【AI 提案】。</summary>
        public const string CrewSniperTag = "直线";

        /// <summary>职业短标语：钩子手【AI 提案】。</summary>
        public const string CrewHookerTag = "飞索";

        /// <summary>职业短标语：纵火狂【AI 提案】。</summary>
        public const string CrewArsonistTag = "火路";

        /// <summary>职业短标语：骷髅海盗【AI 提案】。</summary>
        public const string CrewSkeletonTag = "复生";

        // ==================================================================
        // 选关（§4.4）
        // ==================================================================

        /// <summary>选关标题。</summary>
        public const string LevelTitle = "单人战役";

        /// <summary>选关顶部信息模板。</summary>
        public const string LevelHeaderFormat = "{0}　第 {1}/{2} 章　总星数 {3}/{4}";

        /// <summary>选关顶部：建议下一关模板。</summary>
        public const string LevelHeaderNextFormat = "　建议下一关：{0}";

        /// <summary>选关顶部：全部通关。</summary>
        public const string LevelHeaderAllClear = "　全部关卡已通关";

        /// <summary>选关章节页签模板。</summary>
        public const string LevelChapterFormat = "第 {0} 章";

        /// <summary>选关行状态：已通关（星级改图标后不再拼文本）。</summary>
        public const string LevelRowCleared = "已通关";

        /// <summary>选关行状态：可挑战。</summary>
        public const string LevelRowAvailable = "可挑战";

        /// <summary>选关行状态：未解锁。</summary>
        public const string LevelRowLocked = "未解锁";

        /// <summary>选关行：占位竞技场。</summary>
        public const string LevelRowPlaceholder = "　（占位竞技场）";

        /// <summary>选关动作：再战。</summary>
        public const string LevelReplay = "再战";

        /// <summary>选关动作：出战。</summary>
        public const string LevelFight = "出战";

        /// <summary>选关状态：空编成。</summary>
        public const string LevelStatusEmptyRoster = "编成阵容为空：先去船员管理编入至少 1 名船员。";

        /// <summary>选关状态：未解锁。</summary>
        public const string LevelStatusLocked = "该关卡尚未解锁。";

        /// <summary>选关状态：新船员加入。</summary>
        public const string LevelStatusNewCrewFormat = "新船员已加入名册：{0}";

        /// <summary>选关状态：出战加载说明（与实际行为一致：选哪关加载哪关）【AI 提案/待定】。</summary>
        public const string LevelStatusFixedArena = "提示：出战会加载所选关卡的竞技场，结算记到该关。";

        // —— 大海域页签（M4 八图 UI 入口，UI 审计 P0-1）——

        /// <summary>第 4 个页签名（M4 世界地图入口）。</summary>
        public const string LevelTabWorldSeas = "大海域";

        /// <summary>选关页页头（列表按关卡号升序，样板关在前；{0} = 总关数，{1} = 手作样板关数，
        /// {2} = 海图张数，{3}/{4} = 累计/满分星数。星级只记在海图上，故满分 = 海图数 × 3）【AI 提案/待定】。</summary>
        public const string LevelSelectHeaderFormat = "共 {0} 关　手作样板 {1}　海域图 {2}　累计 {3}/{4} 星";

        /// <summary>海图行状态后缀（未通关）。</summary>
        public const string WorldRowAvailable = "可出战";

        /// <summary>海图行状态后缀（已通关，行内另附星级图标）。</summary>
        public const string WorldRowCleared = "已通关";

        /// <summary>海图行出战按钮。</summary>
        public const string WorldSetSail = "出海";

        /// <summary>选关页样板关行的标识后缀（样板关不是"海域图"，行里必须一眼看得出来）。</summary>
        public const string LevelRowShowcaseTag = "手作样板关";

        /// <summary>选关状态：海图 id 不在目录（数据异常兜底，正常流程不可达）。</summary>
        public const string WorldStatusMapMissing = "海域图数据缺失，请重启游戏。";

        /// <summary>选关状态：样板关号取不到关卡资产（数据异常兜底，正常流程不可达）。</summary>
        public const string LevelStatusShowcaseMissing = "样板关卡数据缺失，请重启游戏。";

        /// <summary>关卡名模板（15 关统一用「第 N 关」，花名待定）。</summary>
        public const string LevelNameFormat = "第 {0} 关";

        /// <summary>章节名 1（GDD §关卡章节）。</summary>
        public const string Chapter1 = "第一章 · 加勒比新手海域";

        /// <summary>章节名 2。</summary>
        public const string Chapter2 = "第二章 · 海盗黄金航线";

        /// <summary>章节名 3。</summary>
        public const string Chapter3 = "第三章 · 传奇宝藏";

        // ==================================================================
        // 战斗 HUD（§4.5）
        // ==================================================================

        /// <summary>玩家回合提示模板。</summary>
        public const string BattleTurnYouFormat = "玩家 {0}，该你了";

        /// <summary>电脑回合提示。</summary>
        public const string BattleTurnAi = "电脑回合，行动中……";

        /// <summary>模式开关：移动。</summary>
        public const string BattleModeMove = "移动";

        /// <summary>模式开关：操作。</summary>
        public const string BattleModeAction = "操作";

        /// <summary>模式开关：观察（r12 用户裁决；我的世界同款鼠标转视角）。</summary>
        public const string BattleModeObserve = "观察";

        /// <summary>抛自己按钮（r12 用户裁决：对外文案叫"跳跃"，机制仍是抛出自己）。</summary>
        public const string BattleThrowSelf = "跳跃";

        /// <summary>结束回合按钮。</summary>
        public const string BattleEndGo = "结束回合";

        /// <summary>力度数字模板（瞄准态，不标「力度」二字）。</summary>
        public const string BattleStrengthFormat = "{0}%";

        /// <summary>力度完整写法模板（非瞄准态）。</summary>
        public const string BattleStrengthLabelFormat = "力度 {0}%";

        /// <summary>武器列表标题。</summary>
        public const string BattleWeaponListTitle = "选择武器";

        /// <summary>武器面板未装备时的提示（图标格已表意，这里只留一行兜底）。</summary>
        public const string BattleWeaponPickHint = "点图标选择武器";

        /// <summary>操作提示：角色模式通用。</summary>
        public const string BattleHintGeneral = "空格 瞄准　E 聚焦　Esc 取消";

        /// <summary>操作提示：瞄准态。</summary>
        public const string BattleHintAiming = "AD 转向　WS 力度　滚轮微调　回车 开炮";

        /// <summary>操作提示：移动模式。</summary>
        public const string BattleHintMove = "左键拖空白环绕　滚轮缩放　中键解除跟随　左键选中　双击聚焦";

        /// <summary>操作提示：武器菜单。</summary>
        public const string BattleHintWeaponMenu = "滚轮选择　回车确认";

        /// <summary>操作提示：聚焦态。</summary>
        public const string BattleHintFocus = "拖动环视　滚轮调距　E 退出";

        /// <summary>小地图面板标题。</summary>
        public const string BattleMinimapTitle = "海图";

        /// <summary>暂停按钮 / 暂停面板触发（含快捷键提示，按键字母按规范保留半角）。</summary>
        public const string BattlePauseButton = "暂停 (Esc)";

        /// <summary>暂停面板标题。</summary>
        public const string BattlePauseTitle = "已暂停";

        /// <summary>暂停面板动作：继续游戏。</summary>
        public const string BattleResume = "继续游戏";

        /// <summary>暂停 / 结算动作：再来一局（重载本关）。</summary>
        public const string BattleRestart = "再来一局";

        // ==================================================================
        // 结算（§4.7）
        // ==================================================================

        /// <summary>胜利（1P 模式玩家胜）。</summary>
        public const string SettlementWin = "胜 利";

        /// <summary>玩家 1 获胜（2P）。</summary>
        public const string SettlementWinP1 = "玩家 1 获胜";

        /// <summary>玩家 2 获胜（2P）。</summary>
        public const string SettlementWinP2 = "玩家 2 获胜";

        /// <summary>平局。</summary>
        public const string SettlementDraw = "平 局";

        /// <summary>挑战失败。</summary>
        public const string SettlementFail = "挑战失败";

        /// <summary>结算行：关卡。</summary>
        public const string SettlementRowLevelFormat = "关卡　{0}";

        /// <summary>结算行：评分。</summary>
        public const string SettlementRowScoreFormat = "评分　{0}";

        /// <summary>结算行：星级。</summary>
        public const string SettlementRowStarsFormat = "星级　{0}/{1}";

        /// <summary>结算行：每人经验。</summary>
        public const string SettlementRowXpFormat = "每人经验　+{0}";

        /// <summary>结算行：新招募。</summary>
        public const string SettlementRowUnlockFormat = "新招募　{0}";

        /// <summary>结算行：首次通关。</summary>
        public const string SettlementRowFirstClear = "首次通关　是";

        /// <summary>结算行：失败提示。</summary>
        public const string SettlementFailTipFormat = "{0} 挑战失败：编成与武器保留，回管理界面可再战。";

        /// <summary>结算动作：返回选关。</summary>
        public const string SettlementBackToSelect = "返回选关";

        /// <summary>星级规则提示。</summary>
        public const string SettlementStarRuleHint =
            "通关得 1 星；阵亡不超过 1 人得 2 星；全员存活且拾取所有宝箱得 3 星。";

        // ==================================================================
        // 设置（§4.8；音量/画质/窗口模式均已真接线，存储走设置槽位 9）
        // ==================================================================

        /// <summary>设置标题。</summary>
        public const string SettingsTitle = "设置";

        /// <summary>设置项：总音量（乘在全部分类之上）。</summary>
        public const string SettingsFieldVolumeMaster = "总音量";

        /// <summary>设置项：音效音量。</summary>
        public const string SettingsFieldVolumeSfx = "音效";

        /// <summary>设置项：音乐音量。</summary>
        public const string SettingsFieldVolumeMusic = "音乐";

        /// <summary>设置项：环境音音量。</summary>
        public const string SettingsFieldVolumeAmbient = "环境";

        /// <summary>设置项：画质档（高画质=PC_Balanced / 流畅=PC_Performant）。</summary>
        public const string SettingsFieldQuality = "画质";

        /// <summary>设置项：高画质（默认）。</summary>
        public const string SettingsOptionQualityHigh = "高画质";

        /// <summary>设置项：流畅（低配档）。</summary>
        public const string SettingsOptionQualitySmooth = "流畅";

        /// <summary>设置项：窗口模式。</summary>
        public const string SettingsFieldWindowMode = "窗口模式";

        /// <summary>设置项：全屏。</summary>
        public const string SettingsOptionFullscreen = "全屏";

        /// <summary>设置项：窗口。</summary>
        public const string SettingsOptionWindowed = "窗口";

        /// <summary>设置动作：恢复默认。</summary>
        public const string SettingsRestore = "恢复默认";

        /// <summary>设置提示：音量改动在关闭面板时保存（避免拖动滑条写盘）。</summary>
        public const string SettingsSaveHint = "改动将在关闭设置时自动保存。";

        // ==================================================================
        // 错误与边界（§4.9）
        // ==================================================================

        /// <summary>无法投掷。</summary>
        public const string ErrorNoThrow = "该船员本回合不能再跳跃了。";

        /// <summary>落水警告。</summary>
        public const string ErrorWater = "危险：这里落水会直接出局！";

        /// <summary>无武器可用。</summary>
        public const string ErrorNoWeapon = "这件武器已经用完了。";

        /// <summary>未选中角色。</summary>
        public const string ErrorNoSelection = "先选一名船员。";
    }
}
