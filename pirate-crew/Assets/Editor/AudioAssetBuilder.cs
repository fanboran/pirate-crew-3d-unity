using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PirateCrew.PirateCrew.Audio;
using PirateCrew.PirateCrew.Audio.Synth;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 把程序化合成的音效离线渲染成 wav 资产（全部原创合成，零第三方素材，符合版权纪律）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/音频/生成程序化音频资产（幂等）
    ///   菜单: PirateCrew/音频/强制重建程序化音频资产
    ///   无头: -executeMethod PirateCrew.EditorTools.AudioAssetBuilder.BuildAll
    ///         -executeMethod PirateCrew.EditorTools.AudioAssetBuilder.ForceRebuildAll
    ///
    /// 【产物】
    ///   Assets/Resources/PirateCrewAudio/&lt;SfxXxx.wav&gt;      战斗 + 反馈 + UI（Sfx 总线）
    ///   Assets/Resources/PirateCrewAudio/&lt;AmbientXxx.wav&gt;   海浪/风声循环与海鸥（Ambient 总线）
    ///   Assets/Resources/PirateCrewAudio/&lt;MusicXxx.wav&gt;     胜利/失败乐句（Music 总线）
    ///   Assets/Art/Audio/README.md                           资产清单 + 导入参数理由（本脚本生成）
    ///   文件名 = <see cref="SfxCatalog.AssetFileName"/>，与运行时回退合成共用同一命名。
    ///
    /// 【为什么全部平铺在 Resources/PirateCrewAudio/】<c>AudioService</c> 用
    /// <c>Resources.Load("PirateCrewAudio/" + 文件名)</c> 解析，Resources 路径不含子目录；
    /// 分类前缀（Sfx/Ambient/Music）已由文件名承载，不再需要 Sfx/Ambient/Music 子目录。
    ///
    /// ====================================================================
    /// 【两条路（离线资产 vs 运行时合成）的取舍 —— 必读】
    /// ====================================================================
    ///   ① 离线 wav 资产（本脚本，默认路径）：
    ///      · 优点：运行时零合成 CPU；导入后可被调音/替换/压缩；包体与加载策略可控；
    ///        非程序员（音效师）可直接替换同名 wav 而无需改代码。
    ///      · 代价：资产需入库（体积）；改配方后必须重跑本脚本。
    ///   ② 运行时合成回退（<see cref="AudioService"/> 的 AudioClip.Create 路径）：
    ///      · 优点：完全自包含，不依赖任何资产引用就能出声；改配方立即生效，便于迭代。
    ///      · 代价：首次播放某音效时要现场生成 PCM（海浪 6.5 s 约几十到上百毫秒，可能卡一帧）；
    ///        全部常驻内存。
    ///   ③ 本工程的落地策略：**资产为主、回退兜底**。AudioService 的剪辑解析优先级是
    ///      ClipProvider 委托 → Resources 约定路径（"PirateCrewAudio/&lt;文件名&gt;"）→ 运行时合成。
    ///      本脚本已把 wav 落在 Resources 约定路径下，因此**无需任何场景接线、无需改
    ///      Bootstrapper，进入播放即走资产**；ClipProvider 只是留给特殊场合的覆盖钩子。
    ///
    /// ====================================================================
    /// 【导入设置：每类的选择与理由】
    /// ====================================================================
    ///   · Sfx（战斗/反馈/UI）→ DecompressOnLoad + Vorbis 0.9：
    ///     音效是「随时可能被触发」的短音（0.09–1.6 s），Decompress On Load 让播放时
    ///     零解码延迟、不会因解码抖动漏掉瞬态；单条很短，解压后常驻内存的代价可忽略。
    ///     压缩格式用 Vorbis 而非 PCM：项目里数十条音效若全用 PCM 会白占包体，
    ///     Vorbis 0.9 对这种噪声音效的听感损失几乎不可闻。
    ///     注意「同帧去抖 + 每类 12 路并发上限」在 AudioService 里另有控制，与导入设置无关。
    ///   · Ambient（海浪/风声循环、海鸥）→ Streaming + Vorbis 0.75：
    ///     海浪 6 s / 风声 8 s 且会长时间循环播放，常驻内存没有意义；Streaming 按需
    ///     从磁盘流式解码，内存占用最小。代价是循环点附近对磁盘的连续读取需求，
    ///     桌面平台可接受；移动端若发现卡顿可改为 Compressed In Memory。
    ///   · Music（胜利/失败乐句）→ Compressed In Memory + Vorbis 0.8：
    ///     乐句只有 3.6–4 s，且必须在结算瞬间立即响起（不能因 Streaming 建流而延迟），
    ///     所以解码后整段常驻内存；4 s 单声道 44100 的体积很小，代价可接受。
    ///   · 三类统一：forceToMono = true（3D 空间音必须单声道，Unity 对立体声片段
    ///     忽略 spatialBlend）；sampleRateSetting = PreserveSampleRate（合成已是
    ///     44100 Hz，重采样只会引入无谓失真）。
    ///
    /// 【循环点】Unity 的 AudioClip 在导入器里没有循环开关，循环由播放侧
    /// <c>AudioSource.loop</c> 决定（本工程见 <see cref="SfxCatalog.IsLoop"/> /
    /// AudioService.StartLoop）。循环音在**合成阶段**就用
    /// <see cref="AudioBuffer.FoldSeamlessLoop"/> 折叠成首尾无缝的循环体
    /// （调制频率取循环长度整数分频 + 尾部交叉淡化），因此不存在需要手工对齐的循环点。
    ///
    /// 【幂等性】渲染是确定性的（固定 seed 的 <see cref="SynthRandom"/>，不用 UnityEngine.Random），
    /// 所以同样配方每次产出**逐字节相同**的 wav：
    ///   · 文件字节与现有资产相同 → 不写盘、不重新导入；
    ///   · 导入参数与期望不同 → 只修正参数（SaveAndReimport）；
    ///   · 目录里存在清单外的旧 wav → 删除（避免改名后残留孤儿资产）。
    /// 因此重复执行不会产生 Unity 资产 diff、不会生成 "... 1.wav" 之类重复文件。
    /// </summary>
    public static class AudioAssetBuilder
    {
        /// <summary>wav 资产输出根目录。
        /// 必须落在 <c>Assets/Resources/</c> 下且平铺：AudioService 用
        /// <c>Resources.Load("PirateCrewAudio/&lt;文件名&gt;")</c> 解析，Resources 路径不含子目录。</summary>
        public const string AudioRoot = "Assets/Resources/PirateCrewAudio";

        /// <summary>README 资产清单所在目录（人类可读文档；刻意不放 Resources 下，避免进构建）。</summary>
        public const string ReadmeFolder = "Assets/Art/Audio";

        /// <summary>README 资产清单文件名。</summary>
        public const string ReadmeFileName = "README.md";

        const string MenuIdempotent = "PirateCrew/音频/生成程序化音频资产（幂等）";
        const string MenuForce = "PirateCrew/音频/强制重建程序化音频资产";

        // 各类导入期望值（集中一处，README 与设置修正共用）
        const float SfxQuality = 0.90f;
        const float AmbientQuality = 0.75f;
        const float MusicQuality = 0.80f;

        /// <summary>幂等生成（-executeMethod 入口，可直接跑）。</summary>
        [MenuItem(MenuIdempotent, false, 40)]
        public static void BuildAll()
        {
            Build(force: false);
        }

        /// <summary>强制重建（忽略现有字节差异，全部重写）。</summary>
        [MenuItem(MenuForce, false, 41)]
        public static void ForceRebuildAll()
        {
            Build(force: true);
        }

        /// <summary>只做检查不写盘（CI/评审用；有差异时抛异常）。</summary>
        public static void VerifyOnly()
        {
            int missing = 0;

            // 程序化合成资产
            foreach (SfxRecipe recipe in SfxCatalog.All)
            {
                if (!SynthRenderer.CanRender(recipe.Id))
                    continue; // 纯外部搬运素材不在合成清单内（见 ②）
                if (!File.Exists(PathFor(recipe)))
                    missing++;
            }

            // ② 搬运素材的各变奏目标资产（源目录在不在无所谓：wav 已随工程提交）
            Game2Port[] ports = Game2AudioAssets.All;
            for (int p = 0; p < ports.Length; p++)
            {
                string[] names = Game2AudioAssets.TargetFileNames(ports[p].Id);
                for (int v = 0; v < names.Length; v++)
                {
                    if (!File.Exists(AudioRoot + "/" + names[v] + ".wav"))
                        missing++;
                }
            }

            if (missing > 0)
                throw new Exception("[AudioAssetBuilder] 缺少 " + missing + " 个音频资产，请先执行 BuildAll。");

            Debug.Log("[AudioAssetBuilder] 校验通过：合成 " + SynthRenderer.RenderableIds().Length
                      + " 条 + 搬运 " + Game2AudioAssets.Count + " 条（共 "
                      + Game2AudioAssets.AllTargetFileNames().Count + " 个变奏资产）齐全。");
        }

        static void Build(bool force)
        {
            EnsureFolders();

            int written = 0;
            int unchanged = 0;
            int failed = 0;

            // ① 程序化合成（只处理有合成实现的 id；纯外部素材见 ②）
            foreach (SfxRecipe recipe in SfxCatalog.All)
            {
                if (!SynthRenderer.CanRender(recipe.Id))
                    continue;

                try
                {
                    if (!BuildOne(recipe, force, out bool wrote, out _))
                    {
                        failed++;
                        continue;
                    }

                    if (wrote)
                        written++;
                    else
                        unchanged++;
                }
                catch (Exception e)
                {
                    failed++;
                    Debug.LogError("[AudioAssetBuilder] 生成失败 " + recipe.Id + ": " + e);
                }
            }

            // ② 外部搬运素材（隔壁 Game-2 自产 WAV）：源目录在就按字节幂等同步；
            //    源目录不在也不报错——wav 已随工程提交，构建不依赖别的仓库 checkout。
            int portedMissing = SyncPortedAssets(force, out int portedCopied, out int portedUnchanged, out int portedSettingsFixed);

            int removed = RemoveStaleAssets();
            bool readmeChanged = WriteReadme();
            AssetDatabase.Refresh();

            Debug.Log("[AudioAssetBuilder] 完成：合成写盘 " + written + "，未变 " + unchanged
                      + "；搬运拷贝 " + portedCopied + "，未变 " + portedUnchanged
                      + "，修正导入设置 " + portedSettingsFixed
                      + "，源缺失 " + portedMissing
                      + "，删除孤儿 " + removed
                      + "，README " + (readmeChanged ? "更新" : "未变")
                      + "，失败 " + failed + "。资产根目录 " + AudioRoot);

            if (failed > 0)
                throw new Exception("[AudioAssetBuilder] 有 " + failed + " 个音频资产生成失败，详见 Console。");

            if (portedMissing > 0)
            {
                Debug.LogWarning("[AudioAssetBuilder] 有 " + portedMissing
                                 + " 个搬运变奏既没有源文件、目标资产也不存在，运行时该变奏缺失（其余变奏不受影响）。");
            }
        }

        /// <summary>
        /// 把 <see cref="Game2AudioAssets"/> 登记的搬运素材同步到 <see cref="AudioRoot"/>。
        ///
        /// 【幂等与 GUID】判据与程序化合成完全一致——**字节相同就不写盘、不重新导入**
        /// （主变奏的文件名与搬运前同名，所以覆盖写也不会改 GUID；
        /// 新变奏在资产首次导入时由 Unity 落 .meta 并保持，之后每次构建都跳过写盘）。
        /// 传入 <paramref name="force"/> 时强制重写（对应「强制重建」菜单）。
        ///
        /// 【返回】既没有源文件、目标资产也不存在的变奏数（真正的交付缺口）。
        /// </summary>
        static int SyncPortedAssets(bool force, out int copied, out int unchanged, out int settingsFixed)
        {
            copied = 0;
            unchanged = 0;
            settingsFixed = 0;
            int missing = 0;

            Game2Port[] ports = Game2AudioAssets.All;
            for (int p = 0; p < ports.Length; p++)
            {
                Game2Port port = ports[p];
                SfxRecipe recipe = SfxCatalog.Get(port.Id);

                for (int v = 0; v < port.VariantCount; v++)
                {
                    string targetPath = AudioRoot + "/" + Game2AudioAssets.TargetFileName(port.Id, v) + ".wav";
                    string sourcePath = Game2AudioAssets.SourcePath(port, v);

                    if (sourcePath == null || !File.Exists(sourcePath))
                    {
                        if (!File.Exists(targetPath))
                        {
                            missing++;
                            Debug.LogWarning("[AudioAssetBuilder] 搬运源缺失且目标不存在：" + port.Id
                                             + " 变奏 " + (v + 1) + "（源 " + sourcePath + "）");
                        }

                        continue;
                    }

                    byte[] bytes = File.ReadAllBytes(sourcePath);
                    bool wrote = force || !BytesEqual(targetPath, bytes);
                    if (wrote)
                    {
                        File.WriteAllBytes(targetPath, bytes);
                        AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
                        copied++;
                    }
                    else
                    {
                        unchanged++;
                    }

                    if (ApplyImportSettings(recipe, targetPath))
                        settingsFixed++;
                }
            }

            return missing;
        }

        static bool BuildOne(SfxRecipe recipe, bool force, out bool wrote, out bool settingsFixed)
        {
            wrote = false;
            settingsFixed = false;

            string path = PathFor(recipe);

            AudioBuffer buffer = SynthRenderer.Render(recipe.Id);
            byte[] wav = WavCodec.EncodePcm16(buffer);

            if (force || !BytesEqual(path, wav))
            {
                File.WriteAllBytes(path, wav);
                wrote = true;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            else if (!File.Exists(path))
            {
                // 理论上不会到这里（BytesEqual 对不存在文件返回 false），防御性处理
                File.WriteAllBytes(path, wav);
                wrote = true;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            settingsFixed = ApplyImportSettings(recipe, path);
            return true;
        }

        // ------------------------------------------------------------------
        // 导入设置
        // ------------------------------------------------------------------

        /// <summary>按分类写入导入设置；返回是否发生了修改（幂等判据）。</summary>
        static bool ApplyImportSettings(SfxRecipe recipe, string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
            {
                Debug.LogWarning("[AudioAssetBuilder] 取不到 AudioImporter（资产未导入？）: " + path);
                return false;
            }

            AudioClipLoadType loadType;
            float quality;
            bool preload;

            switch (recipe.Category)
            {
                case AudioCategory.Ambient:
                    loadType = AudioClipLoadType.Streaming;
                    quality = AmbientQuality;
                    preload = false;
                    break;
                case AudioCategory.Music:
                    loadType = AudioClipLoadType.CompressedInMemory;
                    quality = MusicQuality;
                    preload = true;
                    break;
                default:
                    loadType = AudioClipLoadType.DecompressOnLoad;
                    quality = SfxQuality;
                    preload = true;
                    break;
            }

            bool loadInBackground = recipe.Category == AudioCategory.Ambient;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            bool differs =
                importer.forceToMono != true
                || settings.preloadAudioData != preload
                || importer.loadInBackground != loadInBackground
                || settings.loadType != loadType
                || settings.compressionFormat != AudioCompressionFormat.Vorbis
                || Math.Abs(settings.quality - quality) > 1e-4f
                || settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate;

            if (!differs)
                return false;

            importer.forceToMono = true;
            importer.loadInBackground = loadInBackground;

            // 2022.3 起 preloadAudioData 已从 AudioImporter 移到 AudioImporterSampleSettings
            // （按平台各自保存），故写在这里而不是 importer 上。
            settings.preloadAudioData = preload;
            settings.loadType = loadType;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = quality;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;

            importer.SaveAndReimport();
            return true;
        }

        // ------------------------------------------------------------------
        // 目录 / 孤儿清理 / README
        // ------------------------------------------------------------------

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art"))
                AssetDatabase.CreateFolder("Assets", "Art");

            CreateFolderIfMissing("Assets/Art", "Audio");
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            // wav 全部平铺在本目录（分类由文件名前缀 Sfx/Ambient/Music 承载）。
            CreateFolderIfMissing("Assets/Resources", "PirateCrewAudio");
        }

        static void CreateFolderIfMissing(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
        }

        /// <summary>删除清单外的旧 wav（改名/下线音效后的孤儿资产），返回删除数量。</summary>
        static int RemoveStaleAssets()
        {
            if (!AssetDatabase.IsValidFolder(AudioRoot))
                return 0;

            var expected = new HashSet<string>();
            foreach (SfxRecipe recipe in SfxCatalog.All)
                expected.Add(Path.GetFileName(PathFor(recipe)));

            // 搬运素材的变奏（SfxExplosion_2.wav 等）也是合法资产，不能被当成孤儿删掉
            foreach (string portedName in Game2AudioAssets.AllTargetFileNames())
                expected.Add(portedName);

            int removed = 0;
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (Path.GetDirectoryName(path)?.Replace('\\', '/') != AudioRoot)
                    continue; // 只清理本层，不动子目录

                if (expected.Contains(Path.GetFileName(path)))
                    continue;

                if (AssetDatabase.DeleteAsset(path))
                {
                    removed++;
                    Debug.Log("[AudioAssetBuilder] 删除清单外的音频资产: " + path);
                }
            }

            return removed;
        }

        static bool WriteReadme()
        {
            string path = ReadmeFolder + "/" + ReadmeFileName;
            string content = BuildReadmeContent();
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);

            if (BytesEqual(path, bytes))
                return false;

            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return true;
        }

        static string BuildReadmeContent()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 程序化音频资产（Sfx / Ambient / Music）");
            sb.AppendLine();
            sb.AppendLine("> wav 落在构建资产目录 `Assets/Resources/PirateCrewAudio/`（平铺，分类只体现在文件名前缀），");
            sb.AppendLine("> 由两类来源组成：");
            sb.AppendLine("> ① **程序化合成**：波形 100% 来自 `Assets/Scripts/PirateCrew/Audio/Synth/` 的");
            sb.AppendLine(">    纯函数（正弦/三角/噪声 + ADSR + 滤波 + Schroeder 混响 + 和弦工具），零版权风险；");
            sb.AppendLine("> ② **搬运**：隔壁同一作者的 Game-2（stick-world）自产 WAV，映射表见");
            sb.AppendLine(">    `Assets/Scripts/PirateCrew/Audio/Game2AudioAssets.cs`（每条都登记了源文件相对路径，可逐条核对）；");
            sb.AppendLine(">    这类资产的文件名与搬运前一致或加 `_2`/`_3` 变奏后缀，由本脚本按「字节不变则不写」幂等同步。");
            sb.AppendLine("> 本 README 留在 `Assets/Art/Audio/`（文档不进构建）。");
            sb.AppendLine("> 重新生成：菜单 `PirateCrew/音频/生成程序化音频资产（幂等）`，或");
            sb.AppendLine("> `-executeMethod PirateCrew.EditorTools.AudioAssetBuilder.BuildAll`。");
            sb.AppendLine();
            sb.AppendLine("## 导入设置（脚本自动写入，可复核）");
            sb.AppendLine();
            sb.AppendLine("| 分类 | Load Type | Compression | Quality | Preload | 理由 |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
            sb.AppendLine("| Sfx | Decompress On Load | Vorbis | " + SfxQuality.ToString("0.00")
                          + " | 是 | 短音效随时触发，需零解码延迟；单条体积小，常驻内存代价可忽略 |");
            sb.AppendLine("| Ambient | Streaming | Vorbis | " + AmbientQuality.ToString("0.00")
                          + " | 否 | 6–8 秒循环长期播放，流式解码使内存占用最小 |");
            sb.AppendLine("| Music | Compressed In Memory | Vorbis | " + MusicQuality.ToString("0.00")
                          + " | 是 | 3.6–4 秒，结算瞬间必须立即响，不能因建流延迟 |");
            sb.AppendLine();
            sb.AppendLine("统一：`forceToMono = true`（3D 空间音必须单声道）、"
                          + "`PreserveSampleRate`（合成本就是 44100 Hz，避免重采样失真）。");
            sb.AppendLine();
            sb.AppendLine("**循环点**：Unity 导入器没有循环开关，循环由播放侧 `AudioSource.loop` 控制（见 `AudioService.StartLoop`）。"
                          + "循环音在合成阶段已用交叉淡化折叠成首尾无缝体，无需手工对齐循环点。");
            sb.AppendLine();
            sb.AppendLine("## 资产清单");
            sb.AppendLine();
            sb.AppendLine("| 音效 | 文件 | 分类 | 时长(s) | 循环 | 空间 | 默认音量 | 触发来源 | 合成配方要点 |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");

            foreach (SfxRecipe recipe in SfxCatalog.All)
            {
                sb.Append("| ").Append(recipe.Id)
                  .Append(" | ").Append(SfxCatalog.AssetFileName(recipe.Id)).Append(".wav")
                  .Append(" | ").Append(recipe.Category)
                  .Append(" | ").Append(recipe.DurationSeconds.ToString("0.00"))
                  .Append(" | ").Append(recipe.Loop ? "是" : "否")
                  .Append(" | ").Append(recipe.Spatial == SpatialMode.ThreeD ? "3D" : "2D")
                  .Append(" | ").Append(recipe.DefaultVolume.ToString("0.00"))
                  .Append(" | ").Append(recipe.Trigger)
                  .Append(" | ").Append(recipe.Recipe)
                  .AppendLine(" |");
            }

            sb.AppendLine();
            sb.AppendLine("## 搬运素材清单（源：隔壁 Game-2 自产 WAV）");
            sb.AppendLine();
            sb.AppendLine("映射的唯一定义在 `Assets/Scripts/PirateCrew/Audio/Game2AudioAssets.cs`，本表由它生成；");
            sb.AppendLine("「目标事件」为空表示没有对应 EventBus 事件、需要手动调 `AudioService` 的公开 API。");
            sb.AppendLine();
            sb.AppendLine("| 本项目音效 | 目标资产（第 1 个是主变奏） | 源文件（相对 Game-2 assets/audio） | 目标事件 | 说明 |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");

            foreach (Game2Port port in Game2AudioAssets.All)
            {
                string[] targets = Game2AudioAssets.TargetFileNames(port.Id);
                sb.Append("| ").Append(port.Id)
                  .Append(" | ").Append(string.Join(", ", targets)).Append(".wav")
                  .Append(" | ").Append(string.Join(", ", port.Sources))
                  .Append(" | ").Append(port.WiredToEventBus ? port.EventName : "（无事件）")
                  .Append(" | ").Append(port.Note)
                  .AppendLine(" |");
            }

            sb.AppendLine();
            sb.AppendLine("> 变奏（`_2`/`_3`…）由播放侧随机抽取，配合 `AudioVariation` 的 ±8% 音高 / ±10% 音量抖动消解重复感；");
            sb.AppendLine("> 主变奏的文件名与搬运前完全一致，因此覆盖写不会改变 Unity 资产 GUID。");
            sb.AppendLine();
            sb.AppendLine("## 剪辑解析（资产优先，回退兜底）");
            sb.AppendLine();
            sb.AppendLine("`AudioService` 的剪辑解析优先级：`ClipProvider` 委托 → "
                          + "`Resources.Load(\"PirateCrewAudio/<文件名>\")` → 运行时 `AudioClip.Create` 合成回退。"
                          + "本目录（`Assets/Resources/PirateCrewAudio/`）正落在 Resources 约定路径上，"
                          + "因此**无需任何场景接线、无需改 Bootstrapper，进入播放即走 wav 资产**；"
                          + "`ClipProvider` 只是留给特殊场合（如换素材源）的覆盖钩子，回退合成仅在资产缺失时兜底。");
            sb.AppendLine();
            sb.AppendLine("> 数值说明：全部合成参数（时长/音量/衰减距离/配方细节）为 **AI 提案/待定**，"
                          + "原版 Flash 逆向只给出机制（如 mine 的 beepTimes），没有音频素材与混音参数，需人耳验收。");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        static string PathFor(SfxRecipe recipe)
        {
            // 平铺在 Resources 约定目录：子目录会改变 Resources.Load 的路径，故分类只体现在文件名前缀。
            return AudioRoot + "/" + SfxCatalog.AssetFileName(recipe.Id) + ".wav";
        }

        static bool BytesEqual(string path, byte[] expected)
        {
            if (!File.Exists(path))
                return false;

            var info = new FileInfo(path);
            if (info.Length != expected.Length)
                return false;

            byte[] existing = File.ReadAllBytes(path);
            if (existing.Length != expected.Length)
                return false;

            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != expected[i])
                    return false;
            }

            return true;
        }
    }
}
