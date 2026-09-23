using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>评审机位的"对焦点"。</summary>
    public enum ArtReviewPivot
    {
        /// <summary>竞技场中心（由场景里的 Ground/Water 包围盒推算，不写死坐标）。</summary>
        ArenaCenter = 0,

        /// <summary>当前选中角色（取 CurrentTeam.SelectedCharacter，回退 FirstAlive，再回退首个角色）。</summary>
        SelectedUnit = 1,

        /// <summary>世界原点。备用。</summary>
        WorldOrigin = 2,
    }

    /// <summary>
    /// 单个美术评审机位的可编辑预设。
    ///
    /// 【坐标系约定】与工程一致：XZ 是水平竞技场、+Y 向上、地面顶面 y=0。
    /// <see cref="Offset"/> 是**相对对焦点**的相机位移；朝向默认 LookAt 对焦点
    /// （<see cref="LookAtPivot"/> = true），需要固定朝向时改为 false 并用 <see cref="Euler"/>。
    /// </summary>
    public sealed class ArtReviewShot
    {
        /// <summary>文件名用短 slug（英文，避免非 ASCII 路径写盘问题）。</summary>
        public string Slug;

        /// <summary>中文含义，写进日志与产物清单。</summary>
        public string Label;

        /// <summary>对焦点。</summary>
        public ArtReviewPivot Pivot = ArtReviewPivot.ArenaCenter;

        /// <summary>相机相对对焦点的位移（世界单位）。</summary>
        public Vector3 Offset;

        /// <summary>看向对焦点时额外抬高/偏移的瞄准点（如角色特写瞄准胸口）。</summary>
        public Vector3 LookAtOffset;

        /// <summary>true = 朝向对焦点；false = 使用 <see cref="Euler"/>。</summary>
        public bool LookAtPivot = true;

        /// <summary><see cref="LookAtPivot"/> = false 时使用的欧拉角。</summary>
        public Vector3 Euler;

        /// <summary>相机视场角（度）。</summary>
        public float Fov = 60f;

        /// <summary>true = 出图时显示 HUD（BattleCanvas）；false = 隐藏，只留场景。</summary>
        public bool ShowHud;

        /// <summary>true = 只在 PlayMode 有效（如需要已生成单位的特写）。</summary>
        public bool RequiresPlayMode;

        /// <summary>false = 本轮跳过（预留给后补/易崩的机位）。</summary>
        public bool Enabled = true;

        /// <summary>这张图该看什么，出图日志与评审时对照用。</summary>
        public string Note;
    }

    /// <summary>
    /// 美术评审机位清单——**改这里就能增删/调整机位**，不改采集器逻辑。
    ///
    /// 【偏移量依据】竞技场由 <c>LevelData.WidthTiles × HeightTiles</c> 决定（level_1 = 50×17，
    /// 中心约 (25, 0, 8.5)）；下列偏移按该尺度取景，换更大的关卡时按需调大 Offset。
    /// 相机默认战斗视角对齐场景出厂值（distance 15、pitch 45°、yaw 0 →
    /// offset ≈ (0, 10.61, 10.61)，见 <c>BattleSceneSetup.CameraDistance/CameraPitchDegrees</c>）。
    ///
    /// 【⚠ 改动必须同步】本清单是 Editor 程序集，运行时（独立播放器）采集器
    /// <c>Assets/Scripts/PirateCrew/ArtReview/PlayerArtCapture.cs</c> 引用不到它，
    /// 那边 <c>BuildShots()</c> 是**逐机位手抄**的镜像。改本文件的 Offset / Fov / 瞄准点 / 顺序，
    /// 必须同步 PlayerArtCapture.BuildShots，否则出厂机位与编辑器评审图会对不上
    /// （教训：r3 出厂 battle-45/hud-fullscreen 的 Offset 停在旧距离 18 的 (0,12.73,12.73)，
    /// 而游戏相机已改距离 15，出厂单位只有 21px）。
    /// </summary>
    public static class ArtReviewShots
    {
        /// <summary>全部机位（顺序即出图顺序，序号按此递增）。</summary>
        public static readonly List<ArtReviewShot> All = new List<ArtReviewShot>
        {
            new ArtReviewShot
            {
                Slug = "arena-overview",
                Label = "竞技场远景俯瞰",
                Pivot = ArtReviewPivot.ArenaCenter,
                Offset = new Vector3(0f, 34f, -14f),
                Fov = 55f,
                ShowHud = false,
                Note = "看整体构图：XZ 竞技场是否水平展开、地平线/天空盒是否正常、单位分布是否可读。",
            },
            new ArtReviewShot
            {
                Slug = "battle-45",
                Label = "45° 默认战斗视角",
                Pivot = ArtReviewPivot.ArenaCenter,
                Offset = new Vector3(0f, 11.5f, 10.61f),
                // 瞄准点从竞技场中心移到"两队出生区中点"（不是 LookAtPivot 的 pivot 本身）：
                // level_1 红队出生中心 ≈(20.3,10.1)、蓝队 ≈(46.17,8.5)，两队中点 ≈(33.23,9.3)，
                // 相对竞技场中心 (25,8.5) 的偏移 =(8.23, 0.8, 0.8)。运行时 PlayerArtCapture
                // 由实际单位动态算同一点（TeamSpawnMidpoint），此处为静态清单写死等价偏移。
                LookAtOffset = new Vector3(8.23f, 1.2f, 0.8f),
                Fov = 60f,
                ShowHud = false,
                Note = "复现出厂默认机位（distance 15 / pitch 45°）并抬高到 11.5 避免下缘裁人；"
                     + "瞄准两队出生区中点，让红/蓝两队都尽量入画（r3 问题：纯中心时蓝队被陈设遮挡、红队出框）。",
            },
            new ArtReviewShot
            {
                Slug = "unit-closeup",
                Label = "单角色特写",
                Pivot = ArtReviewPivot.SelectedUnit,
                Offset = new Vector3(0f, 2.0f, -3.6f),
                LookAtOffset = new Vector3(0f, 1.2f, 0f),
                Fov = 40f,
                ShowHud = false,
                RequiresPlayMode = true,
                Note = "看角色本体与描边质量、材质是否发灰/过曝；需 PlayMode（单位在 Awake 生成）。",
            },
            new ArtReviewShot
            {
                Slug = "sea-shore",
                Label = "海面与岸线",
                Pivot = ArtReviewPivot.ArenaCenter,
                Offset = new Vector3(0f, 4.5f, -28f),
                Fov = 50f,
                ShowHud = false,
                Note = "低角看水面与竞技场边缘/岸线（y=-0.2 水面），检查落水死亡边界是否视觉可读。",
            },
            new ArtReviewShot
            {
                Slug = "terrain-high",
                Label = "地形高台",
                Pivot = ArtReviewPivot.ArenaCenter,
                Offset = new Vector3(-24f, 15f, -9f),
                Fov = 55f,
                ShowHud = false,
                Note = "斜侧看瓦片地形的高低差与材质接缝；level_1 若为平地则以地面/瓦片质感为准。",
            },
            new ArtReviewShot
            {
                Slug = "hud-fullscreen",
                Label = "HUD 全屏",
                Pivot = ArtReviewPivot.ArenaCenter,
                Offset = new Vector3(0f, 11.5f, 10.61f),
                // 与 battle-45 完全同参数（含瞄准两队出生区中点），仅多 HUD。
                LookAtOffset = new Vector3(8.23f, 1.2f, 0.8f),
                Fov = 60f,
                ShowHud = true,
                Note = "与 battle-45 同机位：看 HUD 排版/字号/中文字形是否正常（不含方块）、按钮与名册是否溢出，"
                     + "同时核对 HUD 遮挡下的单位可读性。",
            },
            new ArtReviewShot
            {
                Slug = "side-profile",
                Label = "侧向 45° 剪影",
                Pivot = ArtReviewPivot.ArenaCenter,
                Offset = new Vector3(17f, 9f, 14f),
                Fov = 60f,
                ShowHud = false,
                Note = "侧后方视角，看角色轮廓/描边在明暗交界处的表现。",
            },
            new ArtReviewShot
            {
                Slug = "explosion-moment",
                Label = "爆炸瞬间（PlayMode）",
                Pivot = ArtReviewPivot.ArenaCenter,
                Offset = new Vector3(0f, 5f, -7f),
                LookAtOffset = new Vector3(0f, 1.2f, 0f),
                Fov = 60f,
                ShowHud = false,
                RequiresPlayMode = true,
                Enabled = true,
                Note = "看特效层（FxBootstrap 自举的爆炸/烟/火）在场景里的可读性与亮度。"
                     + "斜 45° 中景正视爆心（r3 的近正俯视 (0,6,2) 既框歪又不显火光）。"
                     + "需要 PlayMode（单位与特效运行时才生成）；采集器（PlayerArtCapture）"
                     + "在这张图前会先在竞技场中心主动引爆一次（size=160，banana/parachuteBomb 档，"
                     + "火光比 cannonball 100 更可辨）并等约 0.2s 取闪光峰值——size=160 的火球寿命仅约 0.25s，"
                     + "r3 等 0.4s 时火球/火花已消散（实测 fire_orange=0px），0.2s 才有火光。",
            },
        };

        /// <summary>本轮启用的机位（按 Enabled 过滤，保持清单顺序）。</summary>
        public static List<ArtReviewShot> Enabled()
        {
            var list = new List<ArtReviewShot>();
            foreach (ArtReviewShot shot in All)
            {
                if (shot != null && shot.Enabled)
                    list.Add(shot);
            }
            return list;
        }
    }
}
