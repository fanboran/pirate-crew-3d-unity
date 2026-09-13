using UnityEngine;

namespace PirateCrew.PirateCrew.Visual
{
    /// <summary>
    /// 战斗单位的**职业外观档**（7 档：六职业 + 船长）。
    ///
    /// 【为什么自建枚举】战斗数据层（<c>CrewCatalog.ExportSymbols</c>）只有原版导出符号字符串，
    /// <c>CrewRosterCatalog</c> 只有 6 个招募 id（sailor/gunner/sniper/hooker/arsonist/skeleton），
    /// 二者都没有"职业外观"枚举。外观档只服务造型/装配，故定义在 Visual 域，
    /// 由 <see cref="CrewVisualCatalog"/> 从符号字符串映射而来。
    ///
    /// 【映射出处】docs/角色造型规范.md §3.1 的"母题来源"列（该列本身在 §8.4 标【待定】：
    /// 职业与原版外观母题的映射尚未被用户确认）。
    /// </summary>
    public enum CrewProfession
    {
        /// <summary>水手（母题 redPirate）：阵营色头巾 + 短刀。</summary>
        Sailor = 0,

        /// <summary>炮手（母题 soldier）：最宽肩 + 皮革围裙 + 抱炸弹。</summary>
        Bombardier = 1,

        /// <summary>狙击手（母题 femalePirate/cabinBoy）：最瘦长 + 眼罩 + 长管火枪。</summary>
        Sniper = 2,

        /// <summary>钩子手（母题 blindPirate）：左手铁钩 + 左腿木腿。</summary>
        Hook = 3,

        /// <summary>纵火狂（母题 oldPirate）：大胡子 + 自发光火把。</summary>
        Arsonist = 4,

        /// <summary>骷髅海盗（母题 skeletonPirate）：骨色 + 肋骨 + 黑洞眼窝。</summary>
        Skeleton = 5,

        /// <summary>船长（母题 redPirateCaptain/bluePirateCaptain）：三角帽 + 长大衣 + 白胡。</summary>
        Captain = 6,
    }

    /// <summary>
    /// 部件材质角色。每个角色对应一份**共享材质资产**（`Assets/Art/Materials/Crew/`），
    /// 颜色取自 docs/美术风格指南.md §3.1 材质表 + docs/角色造型规范.md §2.1 配色分工。
    ///
    /// 【阵营色纪律】只有 <see cref="TeamCloth"/> 走阵营色（红 #FF3A29 / 蓝 #3366FF，静态文档:721），
    /// 且运行时由 <c>UnitOutlineBinder</c> 用 MaterialPropertyBlock 写 <c>_BaseColor</c> 覆盖；
    /// 肤色/铁/木/皮革/骨色**不得**被阵营色污染（造型规范 §2.1 纪律）。
    /// </summary>
    public enum CrewMaterialRole
    {
        /// <summary>阵营色布料（头巾/上衣/腰带）——运行时按队伍改 _BaseColor。</summary>
        TeamCloth = 0,

        /// <summary>皮肤 #E8B98A（脸/手/前臂）。</summary>
        Skin = 1,

        /// <summary>骨骼 #E6DFC8（骷髅职业替代皮肤）。</summary>
        Bone = 2,

        /// <summary>铁 #6E6A63（钩/刀身/罩/炮管）。</summary>
        Iron = 3,

        /// <summary>黄铜/金 #C9A227（肩章/护手）。</summary>
        Brass = 4,

        /// <summary>木 #D4A76A（木腿/木柄/枪托）。</summary>
        Wood = 5,

        /// <summary>皮革 #8B5E3C（围裙/皮带/靴/裤）。</summary>
        Leather = 6,

        /// <summary>毛/白 #F0EDE4（胡须/帆布）。</summary>
        Fur = 7,

        /// <summary>深色 #2A2A2A（眼罩/眼窝/炸弹/描边兜底）。</summary>
        Dark = 8,

        /// <summary>火焰 #FF7A1A（火把顶球；用高亮 base color 借 HDR Bloom 出光，见实现说明）。</summary>
        Flame = 9,

        /// <summary>玻璃 #4DA6D9（燃烧瓶瓶身）。</summary>
        Glass = 10,
    }

    /// <summary>
    /// 职业外观目录：符号 ↔ 外观档映射、中文显示名、调色板（hex → Color）、材质角色取色。
    /// 纯 C# 静态类，无头验证台可断言。
    /// </summary>
    public static class CrewVisualCatalog
    {
        /// <summary>外观档数量（六职业 + 船长）。</summary>
        public const int ProfessionCount = 7;

        // ------------------------------------------------------------------
        // 调色板（docs/美术风格指南.md §2.2 / §3.1；造型规范 §2.1）
        // 注：直接取 hex 的 0-1 归一化值，不做 sRGB→Linear 转换——与本工程既有材质
        //     （M2BattleSceneSetup 里的 new Color(...)）保持同一口径；若日后统一色彩空间，
        //     应整体迁移而不是只改角色。
        // ------------------------------------------------------------------

        /// <summary>红队阵营色 #FF3A29（静态文档:721）。</summary>
        public static readonly Color TeamRed = new Color(1.000f, 0.228f, 0.161f, 1f);

        /// <summary>蓝队阵营色 #3366FF（静态文档:721）。</summary>
        public static readonly Color TeamBlue = new Color(0.200f, 0.400f, 1.000f, 1f);

        /// <summary>皮肤 #E8B98A。</summary>
        public static readonly Color Skin = new Color(0.910f, 0.725f, 0.541f, 1f);

        /// <summary>骨骼 #E6DFC8（gdd.md:412 骷髅海盗）。</summary>
        public static readonly Color Bone = new Color(0.902f, 0.875f, 0.784f, 1f);

        /// <summary>铁 #6E6A63。</summary>
        public static readonly Color Iron = new Color(0.431f, 0.416f, 0.388f, 1f);

        /// <summary>黄铜 #C9A227。</summary>
        public static readonly Color Brass = new Color(0.788f, 0.635f, 0.153f, 1f);

        /// <summary>木 #D4A76A（gdd.md:820 木材暗档到中间调之间取中间调）。</summary>
        public static readonly Color Wood = new Color(0.831f, 0.655f, 0.416f, 1f);

        /// <summary>皮革 #8B5E3C。</summary>
        public static readonly Color Leather = new Color(0.545f, 0.369f, 0.235f, 1f);

        /// <summary>毛/白 #F0EDE4。</summary>
        public static readonly Color Fur = new Color(0.941f, 0.929f, 0.894f, 1f);

        /// <summary>深色 #2A2A2A（gdd.md:831 描边兜底色）。</summary>
        public static readonly Color Dark = new Color(0.165f, 0.165f, 0.165f, 1f);

        /// <summary>火焰 #FF7A1A（gdd.md:819 岩浆中间调）。</summary>
        public static readonly Color Flame = new Color(1.000f, 0.478f, 0.102f, 1f);

        /// <summary>玻璃 #4DA6D9（gdd.md:819 海水亮面）。</summary>
        public static readonly Color Glass = new Color(0.302f, 0.651f, 0.851f, 1f);

        // ------------------------------------------------------------------
        // 材质角色取色
        // ------------------------------------------------------------------

        /// <summary>取材质角色的基础色；<see cref="CrewMaterialRole.TeamCloth"/> 默认给红队色（运行时被 MPB 覆盖）。</summary>
        public static Color RoleColor(CrewMaterialRole role)
        {
            switch (role)
            {
                case CrewMaterialRole.TeamCloth: return TeamRed;
                case CrewMaterialRole.Skin: return Skin;
                case CrewMaterialRole.Bone: return Bone;
                case CrewMaterialRole.Iron: return Iron;
                case CrewMaterialRole.Brass: return Brass;
                case CrewMaterialRole.Wood: return Wood;
                case CrewMaterialRole.Leather: return Leather;
                case CrewMaterialRole.Fur: return Fur;
                case CrewMaterialRole.Dark: return Dark;
                case CrewMaterialRole.Flame: return Flame;
                case CrewMaterialRole.Glass: return Glass;
                default: return Color.white;
            }
        }

        /// <summary>
        /// 粗糙度（docs/美术风格指南.md §3.1 材质表；相邻部件至少差 0.15，见 §3.2 纪律 1）。
        /// 描边 shader（PirateOutline）没有 Smoothness 通道，此值仅供记录与后续 PBR 材质迁移。
        /// </summary>
        public static float RoleSmoothness(CrewMaterialRole role)
        {
            switch (role)
            {
                case CrewMaterialRole.TeamCloth: return 0.12f;   // 布料极哑
                case CrewMaterialRole.Skin: return 0.35f;
                case CrewMaterialRole.Bone: return 0.25f;
                case CrewMaterialRole.Iron: return 0.42f;
                case CrewMaterialRole.Brass: return 0.50f;
                case CrewMaterialRole.Wood: return 0.28f;
                case CrewMaterialRole.Leather: return 0.30f;
                case CrewMaterialRole.Fur: return 0.15f;
                case CrewMaterialRole.Dark: return 0.35f;
                case CrewMaterialRole.Flame: return 0.10f;
                case CrewMaterialRole.Glass: return 0.60f;
                default: return 0.3f;
            }
        }

        /// <summary>材质角色名 → 资产文件名（不含扩展名）。</summary>
        public static string MaterialFileName(CrewMaterialRole role)
        {
            return "Crew" + role;
        }

        // ------------------------------------------------------------------
        // 符号 ↔ 外观档映射
        // ------------------------------------------------------------------

        /// <summary>
        /// 战斗导出符号 → 外观档。映射按 docs/角色造型规范.md §3.1 母题来源（【待定】，见 §8.4）；
        /// 先判 Captain（符号含 "Captain"），再精确匹配母题，未知符号回落 <see cref="CrewProfession.Sailor"/>。
        /// </summary>
        public static CrewProfession ProfessionFromBattleSymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return CrewProfession.Sailor;

            // 船长变体优先（redPirateCaptain / bluePirateCaptain / cabinBoyCaptain ...）。
            if (symbol.IndexOf("Captain", System.StringComparison.Ordinal) >= 0)
                return CrewProfession.Captain;

            switch (symbol)
            {
                case "redPirate":
                case "bluePirate":
                    return CrewProfession.Sailor;
                case "soldier":
                    return CrewProfession.Bombardier;
                case "femalePirate":
                case "cabinBoy":
                    return CrewProfession.Sniper;
                case "blindPirate":
                    return CrewProfession.Hook;
                case "oldPirate":
                case "rainbowBeard":
                    return CrewProfession.Arsonist;
                case "skeletonPirate":
                    return CrewProfession.Skeleton;
                case "bossGuy":
                case "bossGuyZombie":
                    return CrewProfession.Captain;
                default:
                    return CrewProfession.Sailor;
            }
        }

        /// <summary>
        /// 招募名册 id → 外观档（<c>CrewRosterCatalog</c> 的 6 个 id，含中文名对照）。
        /// id 无法识别时回落 <see cref="CrewProfession.Sailor"/>。
        /// </summary>
        public static CrewProfession ProfessionFromRosterId(string rosterId)
        {
            switch (rosterId)
            {
                case "sailor": return CrewProfession.Sailor;
                case "gunner": return CrewProfession.Bombardier;
                case "sniper": return CrewProfession.Sniper;
                case "hooker": return CrewProfession.Hook;
                case "arsonist": return CrewProfession.Arsonist;
                case "skeleton": return CrewProfession.Skeleton;
                default: return CrewProfession.Sailor;
            }
        }

        /// <summary>外观档中文显示名（docs/美术风格指南.md §7.1 术语表口径）。</summary>
        public static string DisplayName(CrewProfession profession)
        {
            switch (profession)
            {
                case CrewProfession.Sailor: return "水手";
                case CrewProfession.Bombardier: return "炮手";
                case CrewProfession.Sniper: return "狙击手";
                case CrewProfession.Hook: return "钩子手";
                case CrewProfession.Arsonist: return "纵火狂";
                case CrewProfession.Skeleton: return "骷髅海盗";
                case CrewProfession.Captain: return "船长";
                default: return "水手";
            }
        }

        /// <summary>
        /// 外观档 → 预制体资产文件名（不含扩展名）。用英文 PascalCase 避免中文路径编码风险，
        /// 中文名对照见 <see cref="DisplayName"/>。
        /// </summary>
        public static string PrefabFileName(CrewProfession profession)
        {
            return profession.ToString();
        }

        /// <summary>全部外观档（顺序即枚举顺序）。</summary>
        public static readonly CrewProfession[] AllProfessions =
        {
            CrewProfession.Sailor,
            CrewProfession.Bombardier,
            CrewProfession.Sniper,
            CrewProfession.Hook,
            CrewProfession.Arsonist,
            CrewProfession.Skeleton,
            CrewProfession.Captain,
        };
    }
}
