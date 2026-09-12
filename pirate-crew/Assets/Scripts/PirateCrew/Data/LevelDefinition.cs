using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.Data
{
    /// <summary>
    /// 关卡内的单个出战单位（对应原版关卡 XML 的一个 <c>&lt;obj type="redPirate" ...&gt;</c>）。
    ///
    /// 【出处】静态逆向文档 §4.3（坐标换算）与 §5.5（初始武器解析）。
    /// 【文档覆盖度】§7.2 只收录了代表关的双方人数与武器池，<b>不含每单位的坐标 / luck / 初始武器</b>；
    ///                 这些字段只存在于原版 XML（文档 §7.2 指明的数据源 levels_all.json）。
    ///                 本结构把 XML 字段落全，便于生成器和玩法层使用。
    /// </summary>
    [Serializable]
    public struct LevelUnit
    {
        /// <summary>原版导出符号名（§4.2，如 redPirate / bossGuy）。</summary>
        public string typeName;

        /// <summary>队伍索引：0=红队(team1)，1=蓝队(team2)，按 §4.3 规则求得。</summary>
        public int teamIndex;

        /// <summary>原版 XML 瓦片格 x（运行时 px = (gridX+0.5)*32，§4.3）。</summary>
        public int gridX;

        /// <summary>原版 XML 瓦片格 y（运行时 py = (gridY+0.5)*32 + 16 - bottomExtent，§4.3）。</summary>
        public int gridY;

        /// <summary>该单位的 luck（§4.1 / §7.2；覆盖默认 5，AI 随机投掷次数基数）。</summary>
        public int luck;

        /// <summary>该单位的初始武器栈（§5.5；count=10 表示无限）。</summary>
        public List<WeaponStack> initialWeapons;

        public LevelUnit(string typeName, int teamIndex, int gridX, int gridY, int luck, List<WeaponStack> initialWeapons)
        {
            this.typeName = typeName;
            this.teamIndex = teamIndex;
            this.gridX = gridX;
            this.gridY = gridY;
            this.luck = luck;
            this.initialWeapons = initialWeapons ?? new List<WeaponStack>();
        }

        /// <summary>§4.3：运行时像素 X。</summary>
        public float PixelX => LevelCatalog.ToPixelX(gridX);

        /// <summary>§4.3：运行时像素 Y（bottomExtent 用 §4.1 的 8）。</summary>
        public float PixelY => LevelCatalog.ToPixelY(gridY, CrewCatalog.BottomExtent);
    }

    /// <summary>
    /// 关卡的 ScriptableObject 定义。
    ///
    /// 【出处】静态逆向文档 §7.2（关卡配置）、§4.3（坐标换算）、§5.5（水面与空投）。
    ///
    /// 【坐标系】units 存原版 XML 瓦片格坐标；运行时按 §4.3 换算：
    ///   <c>px = (xmlX + 0.5) * 32</c>
    ///   <c>py = (xmlY + 0.5) * 32 + 16 - bottomExtent</c>（bottomExtent = 8）
    ///   <c>waterY = waterTileY * 32</c>（§5.5）
    ///
    /// 【XML-only 字段】originalXmlPlayers、sourceXmlMaxChests、每单位的 luck/初始武器/坐标
    ///   都属于原版 XML 才有、§7.2 表格未收录的信息；已在字段 Tooltip 中注明。
    /// </summary>
    [CreateAssetMenu(menuName = "PirateCrew/Data/关卡定义", fileName = "LevelDefinition")]
    public class LevelDefinition : ScriptableObject
    {
        [Header("标识")]
        [SerializeField, Tooltip("关卡序号（1–33，§7.2：1P 战役 1–15、2P 面板 16–33）。")]
        int levelNumber;

        [SerializeField, Tooltip("关卡名（原版 XML name 属性，本作多为 undefined）。")]
        string levelName;

        [Header("尺寸与模式")]
        [SerializeField, Tooltip("宽度（瓦片）。§7.2 尺寸表；原版瓦片 32px。")]
        int widthTiles;

        [SerializeField, Tooltip("高度（瓦片）。§7.2 尺寸表。")]
        int heightTiles;

        [SerializeField, Tooltip("原版 XML players 属性。XML-only：本作 raw 恒为 undefined，实际 1P/2P 由菜单按钮决定（§7.2）。")]
        int originalXmlPlayers;

        [Header("水面（§5.5）")]
        [SerializeField, Tooltip("原版 water 对象的瓦片 y。XML-only。")]
        float waterTileY;

        [SerializeField, Tooltip("运行时水面 Y（px）= waterTileY * 32（§5.5）；角色 y 超过此值即落水死亡（§4.4）。")]
        float waterY;

        [Header("空投武器与宝箱（§5.5）")]
        [SerializeField, Tooltip("空投武器池 potentialWeapons（§5.5）。")]
        List<WeaponStack> potentialWeapons = new List<WeaponStack>();

        [SerializeField, Tooltip("宝箱同时存在上限。§5.5 硬编码 3。")]
        int maxChests = LevelCatalog.DefaultMaxChests;

        [SerializeField, Tooltip("原版 XML 的 maxChests 属性原值。XML-only，运行时用途文档未收录，仅存档备查。")]
        int sourceXmlMaxChests;

        [Header("出战单位")]
        [SerializeField, Tooltip("双方出战单位列表（类型名 / 队伍 / 瓦片坐标 / luck / 初始武器）。§4.3 + §5.5。")]
        List<LevelUnit> units = new List<LevelUnit>();

        public int LevelNumber => levelNumber;
        public string LevelName => levelName;
        public int WidthTiles => widthTiles;
        public int HeightTiles => heightTiles;
        public int OriginalXmlPlayers => originalXmlPlayers;
        public float WaterTileY => waterTileY;
        public float WaterY => waterY;
        public IReadOnlyList<WeaponStack> PotentialWeapons => potentialWeapons;
        public int MaxChests => maxChests;
        public int SourceXmlMaxChests => sourceXmlMaxChests;
        public IReadOnlyList<LevelUnit> Units => units;

        /// <summary>
        /// 用 <see cref="LevelCatalog"/> 的关卡数据覆盖本资产（生成器幂等覆盖用）。
        /// </summary>
        public void Apply(LevelData data)
        {
            levelNumber = data.LevelNumber;
            levelName = data.Name;
            widthTiles = data.WidthTiles;
            heightTiles = data.HeightTiles;
            originalXmlPlayers = data.OriginalXmlPlayers;
            waterTileY = data.WaterTileY;
            waterY = data.WaterY;
            maxChests = data.MaxChests;
            sourceXmlMaxChests = data.SourceXmlMaxChests;

            potentialWeapons = new List<WeaponStack>(data.PotentialWeapons);
            units = new List<LevelUnit>(data.Units);
        }
    }
}
