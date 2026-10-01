using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PirateCrew.Data
{
    /// <summary>
    /// 关卡资产的 **golden JSON** 读写器（纯 C#，无 Unity API；编辑器与无头验证台同一份实现）。
    ///
    /// 【为什么自己写而不是 JsonUtility】① <c>JsonUtility</c> 是原生 ECall，无头验证台调用必崩；
    /// ② 字段顺序 / 数字格式必须**确定性**（重复跑逐字节一致），Unity 的实现不承诺这两点。
    ///
    /// 【格式约定】字段顺序 = 载荷类声明顺序；缩进 2 空格；整数型浮点不写小数点（<c>150</c> 而非 <c>150.0</c>）；
    /// 非 ASCII 原样输出（UTF-8，读起来就是「搁浅圣母号」而不是转义串）；空数组写 <c>[]</c>。
    /// 这套约定让 golden JSON 既能被机器逐字节对拍，也能被人 diff。
    /// </summary>
    public static class LevelAssetJson
    {
        // ------------------------------------------------------------------
        // 数字
        // ------------------------------------------------------------------

        /// <summary>
        /// 浮点的规范文本：整数型不带小数点，其余最短往返形式（InvariantCulture）。
        ///
        /// 【NaN/Infinity 兜底】JSON 规范没有这两个字面量，静默写 "0" 是唯一的合法出路，
        /// 但必须吵闹：golden JSON 由本写入器生成、被门禁逐字节对拍——不告警的话，
        /// 上游算出的坏值会被无声漂白成 0 并固化进 golden 文件，门禁从此守护一个错误基准。
        /// 用 <see cref="PirateCrew.Core.Log.Error"/> 直通播放器日志（真错误必须留痕）；
        /// 写侧只在编辑器/迁移器里跑，读侧（无头对拍）不经过本方法。
        /// </summary>
        public static string Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                global::PirateCrew.Core.Log.Error(
                    "[LevelAssetJson] 浮点字段为 " + value + "，非法值将被写成 0——请先修上游数据源，"
                    + "否则 golden JSON 会被固化进错误基准");
                return "0";
            }
            if (value == Math.Floor(value) && Math.Abs(value) < 1e7f)
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        // 写
        // ------------------------------------------------------------------

        /// <summary>样板关快照 → golden JSON 文本（行尾 \n，末行带换行）。</summary>
        public static string Write(LevelAssetPayload payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            var w = new JsonWriter();
            w.BeginObject();
            w.Field("schema", LevelAssetSchema.Version);
            w.Field("kind", LevelAssetSchema.KindLevel);
            w.Field("levelNumber", payload.levelNumber);
            w.Field("assetName", payload.assetName);
            w.Field("displayName", payload.displayName);
            w.Field("sizeX", payload.sizeX);
            w.Field("sizeZ", payload.sizeZ);
            w.Field("waterWorldY", payload.waterWorldY);

            w.FieldName("airdropPool");
            WriteWeaponStacks(w, payload.airdropPool);

            w.FieldName("units");
            w.BeginArray();
            for (int i = 0; i < payload.units.Count; i++)
            {
                LevelUnit u = payload.units[i];
                w.Item();
                w.BeginObject();
                w.Field("typeName", u.typeName);
                w.Field("teamIndex", u.teamIndex);
                w.Field("x", u.x);
                w.Field("z", u.z);
                w.Field("luck", u.luck);
                w.FieldName("initialWeapons");
                WriteWeaponStacks(w, u.initialWeapons);
                w.EndObject();
            }
            w.EndArray();

            w.FieldName("terrain");
            w.BeginObject();
            TerrainRaster raster = payload.terrain;
            w.Field("sizeX", raster.sizeX);
            w.Field("sizeZ", raster.sizeZ);
            w.Field("blockWorldHeight", raster.blockWorldHeight);
            w.FieldName("heights");
            WriteFloatGrid(w, raster.heights, LevelAssetSchema.RasterCells(raster.sizeX));
            w.EndObject();

            w.FieldName("bakedPieces");
            w.BeginArray();
            for (int i = 0; i < payload.bakedPieces.Count; i++)
            {
                BakedPieceEntry p = payload.bakedPieces[i];
                w.Item();
                w.BeginObject();
                w.Field("pieceId", p.pieceId);
                w.Field("instanceName", p.instanceName);
                w.Field("x", p.x);
                w.Field("y", p.y);
                w.Field("z", p.z);
                w.Field("yawDeg", p.yawDeg);
                w.EndObject();
            }
            w.EndArray();

            w.EndObject();
            return w.ToText();
        }

        /// <summary>海图载荷 → golden JSON 文本。</summary>
        public static string Write(WorldMapAssetPayload payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            var w = new JsonWriter();
            w.BeginObject();
            w.Field("schema", LevelAssetSchema.Version);
            w.Field("kind", LevelAssetSchema.KindWorldMap);
            w.Field("id", payload.id);
            w.Field("displayName", payload.displayName);
            w.Field("levelNumber", payload.levelNumber);
            w.Field("spanX", payload.spanX);
            w.Field("spanZ", payload.spanZ);
            w.Field("ambientTier", payload.ambientTier);
            w.Field("horizonSeed", payload.horizonSeed);

            w.FieldName("crewWeapons");
            WriteWeaponStacks(w, payload.crewWeapons);
            w.FieldName("captainWeapons");
            WriteWeaponStacks(w, payload.captainWeapons);
            w.FieldName("airdropPool");
            WriteWeaponStacks(w, payload.airdropPool);

            w.FieldName("terrain");
            w.BeginArray();
            for (int i = 0; i < payload.terrain.Count; i++)
                WriteKitEntry(w, payload.terrain[i]);
            w.EndArray();

            w.FieldName("horizon");
            w.BeginArray();
            for (int i = 0; i < payload.horizon.Count; i++)
                WriteKitEntry(w, payload.horizon[i]);
            w.EndArray();

            w.FieldName("props");
            w.BeginArray();
            for (int i = 0; i < payload.props.Count; i++)
            {
                PropPlacementEntry p = payload.props[i];
                w.Item();
                w.BeginObject();
                w.Field("asset", p.asset);
                w.Field("x", p.x);
                w.Field("y", p.y);
                w.Field("z", p.z);
                w.Field("yawDeg", p.yawDeg);
                w.EndObject();
            }
            w.EndArray();

            w.FieldName("spawns");
            w.BeginArray();
            for (int i = 0; i < payload.spawns.Count; i++)
            {
                SpawnEntry s = payload.spawns[i];
                w.Item();
                w.BeginObject();
                w.Field("teamIndex", s.teamIndex);
                w.Field("archetype", s.archetype);
                w.Field("x", s.x);
                w.Field("z", s.z);
                w.Field("luck", s.luck);
                w.EndObject();
            }
            w.EndArray();

            w.FieldName("horizonFeatures");
            w.BeginArray();
            for (int i = 0; i < payload.horizonFeatures.Count; i++)
                w.StringItem(payload.horizonFeatures[i]);
            w.EndArray();

            w.EndObject();
            return w.ToText();
        }

        static void WriteKitEntry(JsonWriter w, in KitPlacementEntry e)
        {
            w.Item();
            w.BeginObject();
            w.Field("kit", e.kit);
            w.Field("asset", e.asset);
            w.Field("x", e.x);
            w.Field("y", e.y);
            w.Field("z", e.z);
            w.Field("yawDeg", e.yawDeg);
            w.EndObject();
        }

        static void WriteWeaponStacks(JsonWriter w, List<WeaponStack> stacks)
        {
            w.BeginArray();
            if (stacks != null)
            {
                for (int i = 0; i < stacks.Count; i++)
                {
                    w.Item();
                    w.BeginObject();
                    w.Field("id", (int)stacks[i].id);
                    w.Field("count", stacks[i].count);
                    w.EndObject();
                }
            }
            w.EndArray();
        }

        /// <summary>
        /// 高度阵列（米）：**每行 = 栅格一行**（宽度 <paramref name="cellCountX"/> 个采样格）。
        /// 这样改动一处地形 = diff 里改一行里的一个数，而不是在 300 行单列里数位置。
        /// </summary>
        static void WriteFloatGrid(JsonWriter w, List<float> values, int cellCountX)
        {
            w.BeginArray();
            if (values != null && values.Count > 0)
            {
                int width = cellCountX > 0 ? cellCountX : values.Count;
                for (int start = 0; start < values.Count; start += width)
                {
                    int end = Math.Min(start + width, values.Count);
                    var sb = new StringBuilder(end - start);
                    for (int i = start; i < end; i++)
                    {
                        if (i > start)
                            sb.Append(", ");
                        sb.Append(Number(values[i]));
                    }
                    w.RawItem("[" + sb + "]");
                }
            }
            w.EndArray();
        }

        // ------------------------------------------------------------------
        // 读
        // ------------------------------------------------------------------

        /// <summary>
        /// 读侧 schema 校验：schema 不等于当前 <see cref="LevelAssetSchema.Version"/> 的资产按坏数据拒绝
        /// （返回 null + <see cref="PirateCrew.Core.Log.Error"/> 留痕）。
        ///
        /// 【口径】跟本文件读侧既有坏数据风格一致——**返回 null、不抛异常**：调用方
        /// （<c>LevelAssetLibrary</c> / 门禁测试）对 null 已有统一的失败路径，抛异常等于
        /// 悄悄改契约。kind 不符保持静默 null（那可能只是"读错了文件"）；schema 不符必须吵闹：
        /// 走到这说明文件**确实是本类载荷、但字段语义已经代差**——继续读就是拿旧语义当新语义
        /// 静默错读，比解析失败更危险。留痕指向两条出路：用对应版本的迁移器重写资产，
        /// 或把工程对齐到资产所属版本。
        ///
        /// 【无头验证台】Log.Error 直通 <see cref="UnityEngine.Debug.LogError"/>，脱离 Unity
        /// 运行时会抛——与写侧 <see cref="Number"/> 的先例同款边界：正常对拍只喂当前 schema
        /// 的 golden JSON，不会走进本路径；schema 用例只在 Unity 侧（EditMode）跑。
        /// </summary>
        static bool SchemaMatches(JsonValue root)
        {
            int schema = root.Int("schema");
            if (schema == LevelAssetSchema.Version)
                return true;

            global::PirateCrew.Core.Log.Error(
                "[LevelAssetJson] 资产 schema=" + schema
                + (schema == 0 ? "（缺失或非数字）" : "")
                + "，当前版本=" + LevelAssetSchema.Version + "，字段语义已代差，拒绝读取——"
                + "请用对应版本的迁移器重写资产，或将工程对齐到资产所属版本");
            return false;
        }

        /// <summary>golden JSON → 样板关载荷；不是 level 载荷、schema 代差或字段缺失时返回 null。</summary>
        public static LevelAssetPayload ReadLevel(string json)
        {
            JsonValue root = JsonValue.Parse(json);
            if (root == null || root.Kind != JsonKind.Object)
                return null;
            if (root.String("kind") != LevelAssetSchema.KindLevel)
                return null;
            if (!SchemaMatches(root))
                return null;

            var payload = new LevelAssetPayload
            {
                levelNumber = root.Int("levelNumber"),
                assetName = root.String("assetName"),
                displayName = root.String("displayName"),
                sizeX = root.Float("sizeX"),
                sizeZ = root.Float("sizeZ"),
                waterWorldY = root.Float("waterWorldY"),
                airdropPool = ReadWeaponStacks(root["airdropPool"]),
                units = new List<LevelUnit>(),
                terrain = ReadRaster(root["terrain"]),
                bakedPieces = new List<BakedPieceEntry>(),
            };

            foreach (JsonValue u in root.ArrayItems("units"))
            {
                payload.units.Add(new LevelUnit(
                    u.String("typeName"), u.Int("teamIndex"), u.Float("x"), u.Float("z"),
                    u.Int("luck"), ReadWeaponStacks(u["initialWeapons"])));
            }

            foreach (JsonValue p in root.ArrayItems("bakedPieces"))
            {
                payload.bakedPieces.Add(new BakedPieceEntry
                {
                    pieceId = p.Int("pieceId"),
                    instanceName = p.String("instanceName"),
                    x = p.Float("x"),
                    y = p.Float("y"),
                    z = p.Float("z"),
                    yawDeg = p.Float("yawDeg"),
                });
            }

            return payload;
        }

        /// <summary>golden JSON → 海图载荷；不是海图载荷、schema 代差时返回 null。</summary>
        public static WorldMapAssetPayload ReadWorldMap(string json)
        {
            JsonValue root = JsonValue.Parse(json);
            if (root == null || root.Kind != JsonKind.Object)
                return null;
            if (root.String("kind") != LevelAssetSchema.KindWorldMap)
                return null;
            if (!SchemaMatches(root))
                return null;

            var payload = new WorldMapAssetPayload
            {
                id = root.String("id"),
                displayName = root.String("displayName"),
                levelNumber = root.Int("levelNumber"),
                spanX = root.Float("spanX"),
                spanZ = root.Float("spanZ"),
                ambientTier = root.String("ambientTier"),
                horizonSeed = root.Int("horizonSeed"),
                crewWeapons = ReadWeaponStacks(root["crewWeapons"]),
                captainWeapons = ReadWeaponStacks(root["captainWeapons"]),
                airdropPool = ReadWeaponStacks(root["airdropPool"]),
                terrain = new List<KitPlacementEntry>(),
                horizon = new List<KitPlacementEntry>(),
                props = new List<PropPlacementEntry>(),
                spawns = new List<SpawnEntry>(),
                horizonFeatures = new List<string>(),
            };

            foreach (JsonValue e in root.ArrayItems("terrain"))
                payload.terrain.Add(ReadKitEntry(e));
            foreach (JsonValue e in root.ArrayItems("horizon"))
                payload.horizon.Add(ReadKitEntry(e));

            foreach (JsonValue p in root.ArrayItems("props"))
            {
                payload.props.Add(new PropPlacementEntry
                {
                    asset = p.String("asset"),
                    x = p.Float("x"),
                    y = p.Float("y"),
                    z = p.Float("z"),
                    yawDeg = p.Float("yawDeg"),
                });
            }

            foreach (JsonValue s in root.ArrayItems("spawns"))
            {
                payload.spawns.Add(new SpawnEntry
                {
                    teamIndex = s.Int("teamIndex"),
                    archetype = s.String("archetype"),
                    x = s.Float("x"),
                    z = s.Float("z"),
                    luck = s.Int("luck"),
                });
            }

            foreach (JsonValue f in root.ArrayItems("horizonFeatures"))
                payload.horizonFeatures.Add(f.AsString());

            return payload;
        }

        static KitPlacementEntry ReadKitEntry(JsonValue e)
        {
            return new KitPlacementEntry
            {
                kit = e.String("kit"),
                asset = e.String("asset"),
                x = e.Float("x"),
                y = e.Float("y"),
                z = e.Float("z"),
                yawDeg = e.Float("yawDeg"),
            };
        }

        static TerrainRaster ReadRaster(JsonValue node)
        {
            var raster = new TerrainRaster
            {
                heights = new List<float>(),
            };
            if (node == null)
                return raster;

            raster.sizeX = node.Float("sizeX");
            raster.sizeZ = node.Float("sizeZ");
            raster.blockWorldHeight = node.Float("blockWorldHeight");
            // 高度写作「每行一个数组」；同时容忍扁平写法（一个数一行），
            // 免得手改过的 JSON 读出静默的半截数据。
            foreach (JsonValue row in node.ArrayItems("heights"))
            {
                if (row.Kind == JsonKind.Array)
                {
                    foreach (JsonValue b in row.Items())
                        raster.heights.Add(b.AsFloat());
                }
                else
                {
                    raster.heights.Add(row.AsFloat());
                }
            }
            return raster;
        }

        static List<WeaponStack> ReadWeaponStacks(JsonValue node)
        {
            var list = new List<WeaponStack>();
            if (node == null)
                return list;
            foreach (JsonValue s in node.Items())
                list.Add(new WeaponStack((WeaponId)s.Int("id"), s.Int("count")));
            return list;
        }

        // ==================================================================
        // 写侧的最小 JSON 生成器（顺序固定，缩进固定）
        // ==================================================================

        sealed class JsonWriter
        {
            readonly StringBuilder _sb = new StringBuilder(4096);
            readonly List<bool> _first = new List<bool>();

            public void BeginObject()
            {
                _sb.Append('{');
                _first.Add(true);
            }

            public void BeginArray()
            {
                _sb.Append('[');
                _first.Add(true);
            }

            public void EndObject() => End('}');
            public void EndArray() => End(']');

            void End(char close)
            {
                int depth = _first.Count - 1;
                bool wasEmpty = _first[depth];
                _first.RemoveAt(depth);
                if (wasEmpty)
                {
                    _sb.Append(close);
                    return;
                }
                _sb.Append('\n');
                Indent(depth);
                _sb.Append(close);
            }

            public void FieldName(string name)
            {
                NewEntry();
                _sb.Append('"').Append(name).Append("\": ");
            }

            /// <summary>整行原样元素（栅格按行输出用）。</summary>
            public void RawItem(string text)
            {
                Item();
                _sb.Append(text);
            }

            /// <summary>元素前缀（数组项）：换行 + 缩进。</summary>
            public void Item()
            {
                int depth = _first.Count;
                bool first = _first[depth - 1];
                if (!first)
                    _sb.Append(',');
                _first[depth - 1] = false;
                _sb.Append('\n');
                Indent(depth);
            }

            void NewEntry()
            {
                int depth = _first.Count;
                if (depth == 0)
                    return;
                if (!_first[depth - 1])
                    _sb.Append(',');
                _first[depth - 1] = false;
                _sb.Append('\n');
                Indent(depth);
            }

            void Indent(int depth)
            {
                for (int i = 0; i < depth; i++)
                    _sb.Append("  ");
            }

            public void Field(string name, int value)
            {
                FieldName(name);
                _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            }

            public void Field(string name, float value)
            {
                FieldName(name);
                _sb.Append(Number(value));
            }

            public void Field(string name, string value)
            {
                FieldName(name);
                AppendEscaped(value);
            }

            public void IntItem(int value)
            {
                Item();
                _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            }

            public void StringItem(string value)
            {
                Item();
                AppendEscaped(value);
            }

            void AppendEscaped(string value)
            {
                _sb.Append('"');
                if (value != null)
                {
                    for (int i = 0; i < value.Length; i++)
                    {
                        char c = value[i];
                        switch (c)
                        {
                            case '"': _sb.Append("\\\""); break;
                            case '\\': _sb.Append("\\\\"); break;
                            case '\n': _sb.Append("\\n"); break;
                            case '\r': _sb.Append("\\r"); break;
                            case '\t': _sb.Append("\\t"); break;
                            default:
                                if (c < 0x20)
                                    _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                                else
                                    _sb.Append(c);
                                break;
                        }
                    }
                }
                _sb.Append('"');
            }

            public string ToText() => _sb.Append('\n').ToString();
        }

        // ==================================================================
        // 读侧的最小 JSON 解析器
        // ==================================================================

        enum JsonKind { Object, Array, String, Number, Bool, Null }

        sealed class JsonValue
        {
            JsonKind _kind;
            Dictionary<string, JsonValue> _members;
            List<JsonValue> _items;
            string _text;
            bool _boolValue;

            public JsonKind Kind => _kind;

            public IEnumerable<JsonValue> Items() => _items ?? (IEnumerable<JsonValue>)System.Array.Empty<JsonValue>();

            public IEnumerable<JsonValue> ArrayItems(string name)
            {
                JsonValue node = this[name];
                if (node == null || node._kind != JsonKind.Array)
                    return System.Array.Empty<JsonValue>();
                return node._items;
            }

            public JsonValue this[string name]
            {
                get
                {
                    if (_kind != JsonKind.Object || _members == null)
                        return null;
                    return _members.TryGetValue(name, out JsonValue value) ? value : null;
                }
            }

            public string AsString() => _text ?? string.Empty;

            public string String(string name)
            {
                JsonValue node = this[name];
                return node == null ? null : node._text;
            }

            public float AsFloat() => _text == null ? 0f : (float)ParseNumber(_text);

            public int Int(string name)
            {
                JsonValue node = this[name];
                return node == null || node._kind != JsonKind.Number ? 0 : (int)ParseNumber(node._text);
            }

            public float Float(string name)
            {
                JsonValue node = this[name];
                return node == null || node._kind != JsonKind.Number ? 0f : (float)ParseNumber(node._text);
            }

            public bool Bool(string name)
            {
                JsonValue node = this[name];
                return node != null && node._kind == JsonKind.Bool && node._boolValue;
            }

            static double ParseNumber(string text)
            {
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;
            }

            /// <summary>解析 JSON 文本；语法错误返回 null（不做容错修复——坏数据要吵闹）。</summary>
            public static JsonValue Parse(string json)
            {
                if (string.IsNullOrEmpty(json))
                    return null;
                int pos = 0;
                try
                {
                    JsonValue value = ParseValue(json, ref pos);
                    SkipWhitespace(json, ref pos);
                    return pos == json.Length ? value : null;
                }
                catch (FormatException)
                {
                    return null;
                }
            }

            static JsonValue ParseValue(string s, ref int i)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                    throw new FormatException("eof");
                char c = s[i];
                switch (c)
                {
                    case '{': return ParseObject(s, ref i);
                    case '[': return ParseArray(s, ref i);
                    case '"': return new JsonValue { _kind = JsonKind.String, _text = ParseString(s, ref i) };
                    case 't': Expect(s, ref i, "true"); return new JsonValue { _kind = JsonKind.Bool, _boolValue = true };
                    case 'f': Expect(s, ref i, "false"); return new JsonValue { _kind = JsonKind.Bool, _boolValue = false };
                    case 'n': Expect(s, ref i, "null"); return new JsonValue { _kind = JsonKind.Null };
                    default: return new JsonValue { _kind = JsonKind.Number, _text = ParseNumberToken(s, ref i) };
                }
            }

            static JsonValue ParseObject(string s, ref int i)
            {
                var value = new JsonValue { _kind = JsonKind.Object, _members = new Dictionary<string, JsonValue>() };
                i++;   // '{'
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return value;
                }

                while (true)
                {
                    SkipWhitespace(s, ref i);
                    string key = ParseString(s, ref i);
                    SkipWhitespace(s, ref i);
                    if (i >= s.Length || s[i] != ':')
                        throw new FormatException("expected ':'");
                    i++;
                    value._members[key] = ParseValue(s, ref i);
                    SkipWhitespace(s, ref i);
                    if (i >= s.Length)
                        throw new FormatException("eof in object");
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (s[i] == '}')
                    {
                        i++;
                        return value;
                    }
                    throw new FormatException("expected ',' or '}'");
                }
            }

            static JsonValue ParseArray(string s, ref int i)
            {
                var value = new JsonValue { _kind = JsonKind.Array, _items = new List<JsonValue>() };
                i++;   // '['
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return value;
                }

                while (true)
                {
                    value._items.Add(ParseValue(s, ref i));
                    SkipWhitespace(s, ref i);
                    if (i >= s.Length)
                        throw new FormatException("eof in array");
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (s[i] == ']')
                    {
                        i++;
                        return value;
                    }
                    throw new FormatException("expected ',' or ']'");
                }
            }

            static string ParseString(string s, ref int i)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new FormatException("expected string");
                i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length)
                        throw new FormatException("eof in string");
                    char c = s[i++];
                    if (c == '"')
                        return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (i >= s.Length)
                        throw new FormatException("eof in escape");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length)
                                throw new FormatException("bad \\u");
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default: throw new FormatException("bad escape");
                    }
                }
            }

            static string ParseNumberToken(string s, ref int i)
            {
                int start = i;
                while (i < s.Length && "-+.eE0123456789".IndexOf(s[i]) >= 0)
                    i++;
                if (i == start)
                    throw new FormatException("expected value");
                return s.Substring(start, i - start);
            }

            static void Expect(string s, ref int i, string token)
            {
                if (i + token.Length > s.Length || string.CompareOrdinal(s, i, token, 0, token.Length) != 0)
                    throw new FormatException("expected " + token);
                i += token.Length;
            }

            static void SkipWhitespace(string s, ref int i)
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r'))
                    i++;
            }
        }
    }
}
