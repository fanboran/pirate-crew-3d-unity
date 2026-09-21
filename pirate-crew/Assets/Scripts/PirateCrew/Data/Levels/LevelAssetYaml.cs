using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PirateCrew.Data
{
    /// <summary>
    /// 关卡资产 → **Unity `.asset` YAML** 的确定性写出器（纯字符串拼装，不用 Unity API）。
    ///
    /// 【为什么它留在运行时程序集】两个消费者都要它，而且必须**逐字节同一份**：
    ///   ① 编辑器迁移器（<c>Assets/Editor/Levels/LevelDataMigrator.cs</c>）写盘；
    ///   ② 无头验证台的对拍用例（`Assets/Tests/Battle/LevelAssetTests.cs`）算「提交进仓库的
    ///      `.asset` 内容 == golden JSON 内容」——它在无头环境跑，够不着编辑器程序集。
    ///   Unity 的 managed stripping 会把播放器里没人调的方法剥掉，故不构成运行时负担。
    ///
    /// 【格式来源】逐条对齐 Unity 2022.3 自己的序列化风格（对照仓库既有资产
    /// `Assets/Data/Balance/BalanceConfig.asset` 与已退役的 `Assets/Data/Levels/level_1.asset`）：
    ///   · 头 11 行固定（<c>m_Script</c> 用本资产类型的脚本 guid）；
    ///   · 序列值的破折号与键**同缩进**，序列项映射的其余字段缩进 +2；
    ///   · 嵌套 [Serializable] 类型写成一个子映射（本工程的 <c>data</c> 字段）；
    ///   · 空列表写 <c>[]</c>，空字符串写「冒号 + 空格」；
    ///   · 整数型浮点不写小数点（与 <see cref="LevelAssetJson.Number"/> 同一规则）。
    ///
    /// 【纪律】资产以文本方式提交，内容只能由 golden JSON 经本写出器产生。在 Inspector 里手改会
    /// 破坏「资产 == golden JSON」门禁——改数据请改 golden JSON 再跑迁移器（见
    /// <c>docs/技术/架构/关卡数据资产.md</c> §更新流程）。
    /// </summary>
    public static class LevelAssetYaml
    {
        /// <summary>MonoBehaviour 资产的本体 fileID（Unity 新建 SO 恒为 11400000）。</summary>
        const long AssetFileId = 11400000L;

        /// <summary>脚本引用用的 fileID（MonoScript 恒为 11500000）。</summary>
        const long ScriptFileId = 11500000L;

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        /// <summary>样板关快照资产文本（<paramref name="scriptGuid"/> = LevelDefinition.cs.meta 的 guid）。</summary>
        public static string WriteLevel(LevelAssetPayload payload, string assetName, string scriptGuid)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            var data = new Node(NodeKind.Map);
            data.Add("levelNumber", payload.levelNumber);
            data.Add("assetName", payload.assetName);
            data.Add("displayName", payload.displayName);
            data.Add("widthTiles", payload.widthTiles);
            data.Add("depthTiles", payload.depthTiles);
            data.Add("originalXmlPlayers", payload.originalXmlPlayers);
            data.Add("waterTileY", payload.waterTileY);
            data.Add("maxChests", payload.maxChests);
            data.Add("sourceXmlMaxChests", payload.sourceXmlMaxChests);
            data.Add("airdropPool", WeaponStackNodes(payload.airdropPool));

            var units = new Node(NodeKind.Seq);
            for (int i = 0; i < payload.units.Count; i++)
            {
                LevelUnit u = payload.units[i];
                var item = new Node(NodeKind.Map);
                item.Add("typeName", u.typeName);
                item.Add("teamIndex", u.teamIndex);
                item.Add("gridX", u.gridX);
                item.Add("gridY", u.gridY);
                item.Add("luck", u.luck);
                item.Add("initialWeapons", WeaponStackNodes(u.initialWeapons));
                units.Add(item);
            }
            data.Add("units", units);

            var terrain = new Node(NodeKind.Map);
            terrain.Add("widthTiles", payload.terrain.widthTiles);
            terrain.Add("depthTiles", payload.terrain.depthTiles);
            terrain.Add("blockWorldHeight", payload.terrain.blockWorldHeight);
            var blocks = new Node(NodeKind.Seq);
            if (payload.terrain.blocks != null)
            {
                for (int i = 0; i < payload.terrain.blocks.Count; i++)
                    blocks.Add(payload.terrain.blocks[i]);
            }
            terrain.Add("blocks", blocks);
            data.Add("terrain", terrain);

            var pieces = new Node(NodeKind.Seq);
            for (int i = 0; i < payload.bakedPieces.Count; i++)
            {
                BakedPieceEntry p = payload.bakedPieces[i];
                var item = new Node(NodeKind.Map);
                item.Add("pieceId", p.pieceId);
                item.Add("instanceName", p.instanceName);
                item.Add("x", p.x);
                item.Add("y", p.y);
                item.Add("z", p.z);
                item.Add("yawDeg", p.yawDeg);
                pieces.Add(item);
            }
            data.Add("bakedPieces", pieces);

            return WriteMonoBehaviour(assetName, scriptGuid, "data", data);
        }

        /// <summary>海图资产文本（<paramref name="scriptGuid"/> = WorldMapDefinitionAsset.cs.meta 的 guid）。</summary>
        public static string WriteWorldMap(WorldMapAssetPayload payload, string assetName, string scriptGuid)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            var data = new Node(NodeKind.Map);
            data.Add("id", payload.id);
            data.Add("displayName", payload.displayName);
            data.Add("levelNumber", payload.levelNumber);
            data.Add("spanX", payload.spanX);
            data.Add("spanZ", payload.spanZ);
            data.Add("ambientTier", payload.ambientTier);
            data.Add("crewWeapons", WeaponStackNodes(payload.crewWeapons));
            data.Add("captainWeapons", WeaponStackNodes(payload.captainWeapons));
            data.Add("airdropPool", WeaponStackNodes(payload.airdropPool));
            data.Add("terrain", KitPlacementNodes(payload.terrain));
            data.Add("horizon", KitPlacementNodes(payload.horizon));
            data.Add("props", PropPlacementNodes(payload.props));
            data.Add("spawns", SpawnNodes(payload.spawns));
            data.Add("horizonSeed", payload.horizonSeed);
            data.Add("horizonFeatures", StringNodes(payload.horizonFeatures));

            return WriteMonoBehaviour(assetName, scriptGuid, "data", data);
        }

        /// <summary>资源清单资产文本（<paramref name="scriptGuid"/> = LevelCatalog.cs.meta 的 guid）。
        /// 清单持有的引用用「脚本 guid + fileID 11400000」写，见 <see cref="AssetRef"/>。
        ///
        /// 【注意清单没有 data 外壳】<see cref="LevelCatalog"/> 的两个字段直接挂在 MonoBehaviour 上
        /// （与 <see cref="LevelDefinition"/> 那种「单个 data 载荷」的宿主不同）。写成 data 嵌套会让
        /// Unity 把未知字段静默丢弃——清单加载成空表，关卡数据一条都取不到。</summary>
        public static string WriteCatalog(
            string assetName, string scriptGuid, IEnumerable<string> levelGuids, IEnumerable<string> worldMapGuids)
        {
            var levels = new Node(NodeKind.Seq);
            foreach (string guid in levelGuids)
                levels.AddRaw(AssetRef(guid));
            var maps = new Node(NodeKind.Seq);
            foreach (string guid in worldMapGuids)
                maps.AddRaw(AssetRef(guid));

            var fields = new List<KeyValuePair<string, Node>>(2)
            {
                new KeyValuePair<string, Node>("levels", levels),
                new KeyValuePair<string, Node>("worldMaps", maps),
            };

            return WriteMonoBehaviour(assetName, scriptGuid, fields);
        }

        /// <summary>一条资产引用（Unity 的 <c>{fileID: 11400000, guid: &lt;资产 guid&gt;, type: 2}</c>）。</summary>
        public static string AssetRef(string assetGuid)
        {
            return "{fileID: " + AssetFileId.ToString(CultureInfo.InvariantCulture)
                + ", guid: " + assetGuid + ", type: 2}";
        }

        /// <summary>
        /// 资产 guid 的**确定性派生**：同一工程内相对路径恒得同一个 32 位小写十六进制串。
        ///
        /// 【为什么自己算】`.asset` 里的 <c>m_Script</c> 必须引用目标 <c>.cs.meta</c> 的 guid，
        /// 而 `.cs.meta` 要连同资产一起入库。让生成器、迁移器、对拍测试用同一个派生式，
        /// 三处才可能对得上；靠"手工抄 guid"必然漂移。派生式本身不是密码学用途，
        /// 只要求"同路径同值、异路径异值"（md5 在这个用途上足够）。
        /// </summary>
        public static string GuidFor(string assetPath)
        {
            if (assetPath == null)
                throw new ArgumentNullException(nameof(assetPath));

            string seed = "piratecrew.assetguid:" + assetPath.Replace('\\', '/');
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
                var sb = new StringBuilder(32);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>`.asset` / `.json` / `.cs` / 目录的 `.meta` 文本（Unity 2022.3 格式）。</summary>
        /// <param name="guid">32 位小写 guid（用 <see cref="GuidFor"/> 取）。</param>
        /// <param name="kind">meta 的导入器种类。</param>
        public static string MetaText(string guid, AssetMetaKind kind)
        {
            var sb = new StringBuilder(256);
            sb.Append("fileFormatVersion: 2\n");
            sb.Append("guid: ").Append(guid).Append('\n');
            switch (kind)
            {
                case AssetMetaKind.Folder:
                    sb.Append("folderAsset: yes\n");
                    sb.Append("DefaultImporter:\n");
                    sb.Append("  externalObjects: {}\n");
                    sb.Append("  userData: \n");
                    sb.Append("  assetBundleName: \n");
                    sb.Append("  assetBundleVariant: \n");
                    break;
                case AssetMetaKind.ScriptableObject:
                    sb.Append("NativeFormatImporter:\n");
                    sb.Append("  externalObjects: {}\n");
                    sb.Append("  mainObjectFileID: 11400000\n");
                    sb.Append("  userData: \n");
                    sb.Append("  assetBundleName: \n");
                    sb.Append("  assetBundleVariant: \n");
                    break;
                case AssetMetaKind.Text:
                    sb.Append("TextScriptImporter:\n");
                    sb.Append("  externalObjects: {}\n");
                    sb.Append("  userData: \n");
                    sb.Append("  assetBundleName: \n");
                    sb.Append("  assetBundleVariant: \n");
                    break;
                default:
                    sb.Append("MonoImporter:\n");
                    sb.Append("  externalObjects: {}\n");
                    sb.Append("  serializedVersion: 2\n");
                    sb.Append("  defaultReferences: []\n");
                    sb.Append("  executionOrder: 0\n");
                    sb.Append("  icon: {instanceID: 0}\n");
                    sb.Append("  userData: \n");
                    sb.Append("  assetBundleName: \n");
                    sb.Append("  assetBundleVariant: \n");
                    break;
            }
            return sb.ToString();
        }

        /// <summary>`.meta` 的导入器种类。</summary>
        public enum AssetMetaKind
        {
            /// <summary>目录（folderAsset）。</summary>
            Folder = 0,

            /// <summary>ScriptableObject 资产（NativeFormatImporter）。</summary>
            ScriptableObject = 1,

            /// <summary>文本资产（TextScriptImporter；golden JSON 用它）。</summary>
            Text = 2,

            /// <summary>C# 脚本（MonoImporter）。</summary>
            Script = 3,
        }

        // ------------------------------------------------------------------
        // 载荷片段 → Node
        // ------------------------------------------------------------------

        static Node WeaponStackNodes(List<WeaponStack> stacks)
        {
            var seq = new Node(NodeKind.Seq);
            if (stacks == null)
                return seq;
            for (int i = 0; i < stacks.Count; i++)
            {
                var item = new Node(NodeKind.Map);
                item.Add("id", (int)stacks[i].id);
                item.Add("count", stacks[i].count);
                seq.Add(item);
            }
            return seq;
        }

        static Node KitPlacementNodes(List<KitPlacementEntry> entries)
        {
            var seq = new Node(NodeKind.Seq);
            if (entries == null)
                return seq;
            for (int i = 0; i < entries.Count; i++)
            {
                KitPlacementEntry e = entries[i];
                var item = new Node(NodeKind.Map);
                item.Add("kit", e.kit);
                item.Add("asset", e.asset);
                item.Add("x", e.x);
                item.Add("y", e.y);
                item.Add("z", e.z);
                item.Add("yawDeg", e.yawDeg);
                seq.Add(item);
            }
            return seq;
        }

        static Node PropPlacementNodes(List<PropPlacementEntry> entries)
        {
            var seq = new Node(NodeKind.Seq);
            if (entries == null)
                return seq;
            for (int i = 0; i < entries.Count; i++)
            {
                PropPlacementEntry e = entries[i];
                var item = new Node(NodeKind.Map);
                item.Add("asset", e.asset);
                item.Add("x", e.x);
                item.Add("y", e.y);
                item.Add("z", e.z);
                item.Add("yawDeg", e.yawDeg);
                seq.Add(item);
            }
            return seq;
        }

        static Node SpawnNodes(List<SpawnEntry> entries)
        {
            var seq = new Node(NodeKind.Seq);
            if (entries == null)
                return seq;
            for (int i = 0; i < entries.Count; i++)
            {
                SpawnEntry e = entries[i];
                var item = new Node(NodeKind.Map);
                item.Add("teamIndex", e.teamIndex);
                item.Add("archetype", e.archetype);
                item.Add("x", e.x);
                item.Add("z", e.z);
                item.Add("luck", e.luck);
                seq.Add(item);
            }
            return seq;
        }

        static Node StringNodes(List<string> values)
        {
            var seq = new Node(NodeKind.Seq);
            if (values == null)
                return seq;
            for (int i = 0; i < values.Count; i++)
                seq.Add(values[i]);
            return seq;
        }

        // ------------------------------------------------------------------
        // 头 + 发射
        // ------------------------------------------------------------------

        static string WriteMonoBehaviour(string assetName, string scriptGuid, string dataField, Node data)
        {
            return WriteMonoBehaviour(assetName, scriptGuid,
                new List<KeyValuePair<string, Node>>(1) { new KeyValuePair<string, Node>(dataField, data) });
        }

        /// <summary>
        /// 同上，但字段挂在 MonoBehaviour 顶层（顺序即序列化顺序）。
        /// <see cref="LevelCatalog"/> 这类「字段直接挂在类上」的资产用这个重载，
        /// 不要为它造 data 外壳——见 <see cref="WriteCatalog"/> 的说明。
        /// </summary>
        static string WriteMonoBehaviour(
            string assetName, string scriptGuid, IReadOnlyList<KeyValuePair<string, Node>> fields)
        {
            var sb = new StringBuilder(4096);
            sb.Append("%YAML 1.1\n");
            sb.Append("%TAG !u! tag:unity3d.com,2011:\n");
            sb.Append("--- !u!114 &").Append(AssetFileId.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("MonoBehaviour:\n");
            sb.Append("  m_ObjectHideFlags: 0\n");
            sb.Append("  m_CorrespondingSourceObject: {fileID: 0}\n");
            sb.Append("  m_PrefabInstance: {fileID: 0}\n");
            sb.Append("  m_PrefabAsset: {fileID: 0}\n");
            sb.Append("  m_GameObject: {fileID: 0}\n");
            sb.Append("  m_Enabled: 1\n");
            sb.Append("  m_EditorHideFlags: 0\n");
            sb.Append("  m_Script: {fileID: ").Append(ScriptFileId.ToString(CultureInfo.InvariantCulture))
              .Append(", guid: ").Append(scriptGuid).Append(", type: 3}\n");
            sb.Append("  m_Name: ").Append(assetName).Append('\n');
            sb.Append("  m_EditorClassIdentifier: \n");
            for (int i = 0; i < fields.Count; i++)
            {
                sb.Append("  ").Append(fields[i].Key).Append(":\n");
                Emit(sb, fields[i].Value, 4);
            }
            return sb.ToString();
        }

        /// <summary>递归发射：映射的每个字段一行；序列的每项以「- 」起头。</summary>
        static void Emit(StringBuilder sb, Node node, int indent)
        {
            if (node.Kind == NodeKind.Map)
            {
                for (int i = 0; i < node.Children.Count; i++)
                {
                    Node child = node.Children[i];
                    AppendIndent(sb, indent);
                    sb.Append(child.Name).Append(':');
                    EmitValue(sb, child, indent);
                }
                return;
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                Node child = node.Children[i];
                AppendIndent(sb, indent);
                sb.Append("- ");
                if (child.Kind == NodeKind.Map)
                {
                    // 第一行贴在破折号后面，其余字段缩进 +2（对齐 Unity 自己的写法）。
                    EmitMapItem(sb, child, indent + 2);
                    continue;
                }
                // 标量项直接跟在 "- " 后面（Unity 写 List<int> 就是 "  - 5"）。
                sb.Append(child.Scalar).Append('\n');
            }
        }

        static void EmitMapItem(StringBuilder sb, Node map, int fieldIndent)
        {
            for (int i = 0; i < map.Children.Count; i++)
            {
                Node child = map.Children[i];
                if (i > 0)
                    AppendIndent(sb, fieldIndent);
                sb.Append(child.Name).Append(':');
                EmitValue(sb, child, fieldIndent);
            }
        }

        /// <summary>写「冒号之后」的内容：标量就地写，容器换行递归。</summary>
        static void EmitValue(StringBuilder sb, Node child, int indent)
        {
            switch (child.Kind)
            {
                case NodeKind.Map:
                    sb.Append('\n');
                    Emit(sb, child, indent + 2);
                    return;
                case NodeKind.Seq:
                    if (child.Children.Count == 0)
                    {
                        sb.Append(" []\n");
                        return;
                    }
                    sb.Append('\n');
                    // 序列的破折号与键同缩进（Unity 的写法）。
                    Emit(sb, child, indent);
                    return;
                default:
                    sb.Append(' ').Append(child.Scalar).Append('\n');
                    return;
            }
        }

        static void AppendIndent(StringBuilder sb, int indent)
        {
            for (int i = 0; i < indent; i++)
                sb.Append(' ');
        }

        // ------------------------------------------------------------------
        // Node
        // ------------------------------------------------------------------

        enum NodeKind { Scalar, Map, Seq }

        sealed class Node
        {
            public readonly NodeKind Kind;
            public readonly List<Node> Children = new List<Node>();
            public string Name;
            public string Scalar;

            public Node(NodeKind kind)
            {
                Kind = kind;
            }

            /// <summary>映射字段。</summary>
            public void Add(string name, Node value)
            {
                value.Name = name;
                Children.Add(value);
            }

            public void Add(string name, string value)
            {
                Children.Add(new Node(NodeKind.Scalar) { Name = name, Scalar = FormatString(value) });
            }

            public void Add(string name, int value)
            {
                Children.Add(new Node(NodeKind.Scalar) { Name = name, Scalar = value.ToString(CultureInfo.InvariantCulture) });
            }

            public void Add(string name, float value)
            {
                Children.Add(new Node(NodeKind.Scalar) { Name = name, Scalar = LevelAssetJson.Number(value) });
            }

            /// <summary>序列项（标量）。</summary>
            public void Add(int value)
            {
                Children.Add(new Node(NodeKind.Scalar) { Scalar = value.ToString(CultureInfo.InvariantCulture) });
            }

            /// <summary>序列项（标量字符串）。</summary>
            public void Add(string value)
            {
                Children.Add(new Node(NodeKind.Scalar) { Scalar = FormatString(value) });
            }

            /// <summary>序列项（已建好的子节点）。</summary>
            public void Add(Node value)
            {
                Children.Add(value);
            }

            /// <summary>序列项（已成型的内联标量，如资产引用）。</summary>
            public void AddRaw(string raw)
            {
                Children.Add(new Node(NodeKind.Scalar) { Scalar = raw });
            }
        }

        /// <summary>
        /// 标量文本：`null` → 空（Unity 的空字符串写法是「冒号后一个空格」）；
        /// 含 YAML 敏感字符或看起来像数字的串加单引号（与 Unity 的引号规则同向）。
        /// </summary>
        static string FormatString(string value)
        {
            if (value == null)
                return string.Empty;
            if (value.Length == 0)
                return string.Empty;

            bool needsQuote = false;
            char first = value[0];
            if (first == ' ' || first == '\t' || first == '-' || first == '?' || first == ':'
                || first == '[' || first == ']' || first == '{' || first == '}' || first == '#'
                || first == '&' || first == '*' || first == '!' || first == '|' || first == '>'
                || first == '\'' || first == '"' || first == '%' || first == '@' || first == '`')
            {
                needsQuote = true;
            }

            for (int i = 0; i < value.Length && !needsQuote; i++)
            {
                char c = value[i];
                if (c == ':' || c == '#' || c == '\n' || c == '\r' || c == '\t' || c == '\'')
                    needsQuote = true;
                else if (c < 0x20)
                    needsQuote = true;
            }

            if (!needsQuote && LooksNumeric(value))
                needsQuote = true;

            if (!needsQuote)
                return value;
            return "'" + value.Replace("'", "''") + "'";
        }

        static bool LooksNumeric(string value)
        {
            bool sawDigit = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= '0' && c <= '9')
                {
                    sawDigit = true;
                    continue;
                }
                if (c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                    continue;
                return false;
            }
            return sawDigit;
        }
    }
}
