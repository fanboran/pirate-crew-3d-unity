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
    /// 相机默认战斗视角对齐场景出厂值（distance 18、pitch 45°、yaw 0 →
    /// offset ≈ (0, 12.73, 12.73)，见 <c>M2BattleSceneSetup.CameraDistance/CameraPitchDegrees</c>）。
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
                Offset = new Vector3(0f, 12.73f, 12.73f),
                Fov = 60f,
                ShowHud = false,
                Note = "复现出厂默认机位（distance 18 / pitch 45°），评审场景与角色的常规观感。",
            },
            new ArtReviewShot
            {
                Slug = "unit-closeup",
                Label = "单角色特写",
                Pivot = ArtReviewPivot.SelectedUnit,
                Offset = new Vector3(0f, 2.0f, -3.6f),
                LookAtOffset = new Vector3(0f, 0.45f, 0f),
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
                Offset = new Vector3(0f, 12.73f, 12.73f),
                Fov = 60f,
                ShowHud = true,
                Note = "看 HUD 排版/字号/中文字形是否正常（不含方块）、按钮与名册是否溢出。",
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
                Offset = new Vector3(0f, 6f, 2f),
                Fov = 60f,
                ShowHud = false,
                RequiresPlayMode = true,
                Enabled = true,
                Note = "看特效层（FxBootstrap 自举的爆炸/烟/火）在场景里的可读性与亮度。"
                     + "需要 PlayMode（单位与特效运行时才生成）；采集时若场上尚无爆炸，"
                     + "这张图等价于低角度中景，仍可看氛围与地面/水面衔接。",
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
