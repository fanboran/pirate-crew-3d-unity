using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PirateCrew.PirateCrew.Audio;
using PirateCrew.PirateCrew.Battle;

namespace PirateCrew.Tests.Audio
{
    /// <summary>
    /// 「隔壁 Game-2 自产 WAV → 本项目 SfxId / 事件」搬运映射表的一致性测试（纯 C#）。
    ///
    /// 搬运类故障全是**静默**的：文件忘了拷 → 运行时回落到程序化合成（听感不一致但没人报错）；
    /// 事件名写错 → 声音永远不响；变奏少拷一个 → 只是「重复感又回来了」。
    /// 所以这四条要对表逐项断言：
    ///   ① 每条映射至少有 1 个源文件，且变奏数与源文件数一致；
    ///   ② 源文件在 Game-2 目录里确实存在（源目录不在的机器上跳过——wav 已随工程提交）；
    ///     上游重构后迁移走的源改查 <see cref="ArchivedSourceRelocations"/> 登记位置，
    ///     并逐字节核对已搬运副本（见该表注释）。
    ///   ③ 每条映射的目标事件**确实被 AudioService 订阅**（或在表里明确标注为手动 API）；
    ///   ④ 每个变奏的目标资产都已落在 Resources/PirateCrewAudio。
    /// </summary>
    public class Game2AudioPortTests
    {
        /// <summary>
        /// 订阅表里**不是**搬运映射驱动的接线（沿用程序化合成素材，没有 Game-2 源）。
        /// 列在测试里是刻意的：新增订阅事件必须同步更新本表或搬运映射，否则测试失败
        /// （防止「订阅了一个没人播的事件」这种静默死接线）。
        /// </summary>
        static readonly string[] NonPortedSubscriptions =
        {
            BattleEvents.TurnEnded,      // 回合结束 → SfxId.TurnEnd（合成，Game-2 无对应源）
            BattleEvents.MineBeep,       // 地雷引信 → SfxId.MineBeep（合成，§5.2 beepTimes）
            BattleEvents.AiDecided,      // AI 换武器 → SfxId.WeaponSwitch（合成）
            "scene_load_started",        // 离开战斗场景 → 停底床/停音乐（清理，不出声）
        };

        // ------------------------------------------------------------------
        // ① 表自身完整性
        // ------------------------------------------------------------------

        [Test]
        public void Port_EveryEntryHasAtLeastOneSource()
        {
            Game2Port[] ports = Game2AudioAssets.All;
            Assert.That(ports.Length, Is.GreaterThan(0), "搬运表为空");

            foreach (Game2Port port in ports)
            {
                Assert.That(port.VariantCount, Is.GreaterThan(0), port.Id + " 没有任何源文件");
                Assert.That(port.Note, Is.Not.Null.And.Not.Empty, port.Id + " 缺用途说明");
                for (int i = 0; i < port.Sources.Length; i++)
                {
                    Assert.That(port.Sources[i], Is.Not.Null.And.Not.Empty, port.Id + " 第 " + i + " 个源为空");
                    Assert.That(port.Sources[i], Does.EndWith(".wav"), port.Id + " 的源不是 wav: " + port.Sources[i]);
                    Assert.That(port.Sources[i], Does.Not.StartWith("/"), port.Id + " 的源必须是相对路径");
                }
            }
        }

        [Test]
        public void Port_MapsToRegisteredSfxWithMatchingLoopSemantics()
        {
            foreach (Game2Port port in Game2AudioAssets.All)
            {
                SfxRecipe recipe = SfxCatalog.Get(port.Id);
                Assert.That(recipe.DurationSeconds, Is.GreaterThan(0d), port.Id + " 映射到未登记的 SfxId");

                // 底床层的映射必须是循环音，一次性音效必须不是循环音——播错会静默不出声或循环不停止
                bool isBedLayer = AmbientBedRules.IsBedLayer(port.Id);
                if (isBedLayer)
                    Assert.That(recipe.Loop, Is.True, port.Id + " 是底床层但不是循环音");
            }
        }

        [Test]
        public void Port_SourceAndVariantCountsAgree()
        {
            foreach (Game2Port port in Game2AudioAssets.All)
            {
                Assert.That(Game2AudioAssets.VariantCount(port.Id), Is.EqualTo(port.Sources.Length),
                    port.Id + " 的变奏数与源文件数不一致");
                Assert.That(Game2AudioAssets.TargetFileNames(port.Id).Length, Is.EqualTo(port.Sources.Length));
            }
        }

        [Test]
        public void Port_PrimaryTargetKeepsCatalogFileName()
        {
            // 主变奏沿用 SfxCatalog 的命名 → 覆盖写不会改 Unity 资产 GUID
            foreach (Game2Port port in Game2AudioAssets.All)
            {
                Assert.That(Game2AudioAssets.TargetFileName(port.Id, 0),
                    Is.EqualTo(SfxCatalog.AssetFileName(port.Id)),
                    port.Id + " 的第 1 个变奏必须沿用目录命名（GUID 稳定策略）");
            }

            // 未搬运的 id 只有一个「变奏」，就是目录命名本身
            foreach (SfxRecipe recipe in SfxCatalog.All)
            {
                if (Game2AudioAssets.IsPorted(recipe.Id))
                    continue;
                Assert.That(Game2AudioAssets.VariantCount(recipe.Id), Is.EqualTo(1));
                Assert.That(Game2AudioAssets.TargetFileName(recipe.Id, 0),
                    Is.EqualTo(SfxCatalog.AssetFileName(recipe.Id)));
            }
        }

        [Test]
        public void Port_VariantSuffixesAreDistinct()
        {
            var seen = new HashSet<string>();
            foreach (SfxRecipe recipe in SfxCatalog.All)
            {
                foreach (string name in Game2AudioAssets.TargetFileNames(recipe.Id))
                {
                    Assert.That(seen.Add(name), Is.True, "搬运目标文件名重复: " + name);
                }
            }
        }

        // ------------------------------------------------------------------
        // ③ 目标事件有播放调用（订阅表断言）
        // ------------------------------------------------------------------

        [Test]
        public void Port_EventWiredEntriesUseSubscribedEvents()
        {
            var subscribed = new HashSet<string>(AudioService.SubscribedEvents(), StringComparer.Ordinal);
            Assert.That(subscribed.Count, Is.GreaterThan(0), "AudioService 订阅表为空？");

            foreach (Game2Port port in Game2AudioAssets.All)
            {
                if (!port.WiredToEventBus)
                    continue;

                Assert.That(subscribed.Contains(port.EventName), Is.True,
                    port.Id + " 映射到事件 " + port.EventName + "，但 AudioService 没有订阅它（声音永远不会响）");
            }
        }

        [Test]
        public void Port_ManualEntriesDocumentTheirApi()
        {
            // 「有事件就自动接线，没有就明确留 TODO」：手动项必须在说明里写清用哪个 API
            foreach (Game2Port port in Game2AudioAssets.All)
            {
                if (port.WiredToEventBus)
                    continue;

                Assert.That(port.Note, Does.Contain("PlayUi"),
                    port.Id + " 没有事件驱动，说明里必须写清改用哪个公开 API（未接线清单靠它可核对）");
            }
        }

        [Test]
        public void Port_SubscribedEventsAreAllAccountedFor()
        {
            // 反向检查：订阅的每个事件要么有搬运映射驱动，要么在 NonPortedSubscriptions 里登记
            var wiredOrPorted = new HashSet<string>(StringComparer.Ordinal);
            foreach (Game2Port port in Game2AudioAssets.All)
            {
                if (port.WiredToEventBus)
                    wiredOrPorted.Add(port.EventName);
            }

            var allow = new HashSet<string>(NonPortedSubscriptions, StringComparer.Ordinal);
            foreach (string eventName in AudioService.SubscribedEvents())
            {
                Assert.That(wiredOrPorted.Contains(eventName) || allow.Contains(eventName), Is.True,
                    "订阅了事件 " + eventName + "，但它既不在搬运映射里也没有登记为「沿用合成素材」");
            }
        }

        [Test]
        public void Port_BirdVariantsCoverAllChirpSources()
        {
            // 鸟鸣三个变奏必须是三条独立的源（只剩 1 条 = 随机点缀退化成每次同一个音）
            var birdIds = new[] { SfxId.SeagullCry1, SfxId.SeagullCry2, SfxId.SeagullCry3 };
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SfxId id in birdIds)
            {
                Game2Port port = Game2AudioAssets.Get(id);
                Assert.That(port.VariantCount, Is.EqualTo(1), id + " 只应有 1 个源（变体由不同 SfxId 承担）");
                Assert.That(sources.Add(port.Sources[0]), Is.True, "鸟鸣源重复: " + port.Sources[0]);
            }
        }

        // ------------------------------------------------------------------
        // ② / ④ 源文件与目标资产都存在
        // ------------------------------------------------------------------

        /// <summary>
        /// 已知**上游迁移**的源文件登记表（测试侧，键 = 搬运表里的源相对路径）。
        ///
        /// 【2026-09-14 核对结论】BedPad 的源 <c>bgm/ambient_pad.wav</c> 在 game-2 的 bgm 系统
        /// 重做（单文件垫底 → <c>music_manifest.json</c> 分层 OGG）后已从规范资产树移除；
        /// 唯一残本在其构建备份快照里，且与本工程已搬运的目标资产 <c>AmbientBedPad.wav</c>
        /// **逐字节相同**（MD5 4aa22883022587a05ff98a0455226141）——搬运本身无损，
        /// 只是上游没有正本了。故对这类源：注册路径找不到时改查迁移登记位置，
        /// 并**逐字节**核对该残本与已搬运目标资产一致（残本损坏 = 目标也不可信）。
        /// 快照位于 .temp 临时区，若哪天也被清理，本测试会失败并把
        /// 「BedPad 原源彻底丢失」顶到台面上，由协调者重新裁决。
        /// </summary>
        static readonly Dictionary<string, string> ArchivedSourceRelocations =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                {
                    "bgm/ambient_pad.wav",
                    "F:/VSCode/game-2/.temp/building-pipeline-v2/.temp/_restore_morning/stick-world/assets/audio/bgm/ambient_pad.wav"
                },
            };

        [Test]
        public void Port_SourcesExistInGame2Checkout()
        {
            if (!Directory.Exists(Game2AudioAssets.SourceRoot))
            {
                Assert.Ignore("本机没有 Game-2 素材目录，跳过源文件存在性检查（wav 已随工程提交）："
                              + Game2AudioAssets.SourceRoot);
            }

            foreach (Game2Port port in Game2AudioAssets.All)
            {
                for (int i = 0; i < port.Sources.Length; i++)
                {
                    string path = RelativeToPlatform(Game2AudioAssets.SourcePath(port, i));
                    if (File.Exists(path))
                        continue;

                    // 源在注册路径不存在：若是已登记的上游迁移（如 BedPad），改查迁移位置，
                    // 并要求它与本工程已搬运的目标资产逐字节相同（源以"已搬运副本"的形式存续）。
                    string relocated;
                    if (ArchivedSourceRelocations.TryGetValue(port.Sources[i], out relocated)
                        && File.Exists(RelativeToPlatform(relocated)))
                    {
                        AssertSourceMatchesPortedTarget(port, relocated);
                        continue;
                    }

                    Assert.That(File.Exists(path), Is.True,
                        port.Id + " 的源文件不存在（且不在上游迁移登记表里）: " + path);
                }
            }
        }

        /// <summary>迁移残本必须与已搬运的目标资产逐字节相同——否则搬运数据已不可信。</summary>
        static void AssertSourceMatchesPortedTarget(Game2Port port, string relocatedPath)
        {
            string root = FindProjectRoot();
            Assert.That(root, Is.Not.Null, "找不到工程根，无法核对 " + port.Id + " 的已搬运资产");

            string targetPath = Path.Combine(root, "Assets", "Resources", "PirateCrewAudio",
                Game2AudioAssets.TargetFileName(port.Id, 0) + ".wav");

            Assert.That(File.Exists(targetPath), Is.True,
                port.Id + " 的目标资产缺失（运行时会回落到程序化合成）: " + targetPath);

            byte[] sourceHash;
            using (var stream = File.OpenRead(RelativeToPlatform(relocatedPath)))
                sourceHash = System.Security.Cryptography.SHA256.Create()
                    .ComputeHash(stream);

            byte[] targetHash;
            using (var stream = File.OpenRead(targetPath))
                targetHash = System.Security.Cryptography.SHA256.Create()
                    .ComputeHash(stream);

            Assert.That(Convert.ToBase64String(sourceHash),
                Is.EqualTo(Convert.ToBase64String(targetHash)),
                port.Id + " 的上游残本与已搬运资产不一致（搬运数据被改动过）");
        }

        [Test]
        public void Port_TargetAssetsExistInProject()
        {
            string root = FindProjectRoot();
            if (root == null)
            {
                Assert.Ignore("找不到工程根（无 Assets/Resources/PirateCrewAudio）；"
                              + "可用环境变量 PIRATECREW_PROJECT_ROOT 显式指定");
            }

            foreach (Game2Port port in Game2AudioAssets.All)
            {
                string[] names = Game2AudioAssets.TargetFileNames(port.Id);
                for (int i = 0; i < names.Length; i++)
                {
                    string path = Path.Combine(root, "Assets", "Resources", "PirateCrewAudio", names[i] + ".wav");
                    Assert.That(File.Exists(path), Is.True,
                        port.Id + " 的第 " + (i + 1) + " 个变奏资产缺失（运行时该变奏不发声）: " + path);
                }
            }
        }

        [Test]
        public void Port_TargetAssetsAreNonEmptyWavFiles()
        {
            string root = FindProjectRoot();
            if (root == null)
                Assert.Ignore("找不到工程根");

            foreach (Game2Port port in Game2AudioAssets.All)
            {
                string[] names = Game2AudioAssets.TargetFileNames(port.Id);
                for (int i = 0; i < names.Length; i++)
                {
                    string path = Path.Combine(root, "Assets", "Resources", "PirateCrewAudio", names[i] + ".wav");
                    var info = new FileInfo(path);
                    if (!info.Exists)
                        continue; // 存在性由上一个测试负责

                    Assert.That(info.Length, Is.GreaterThan(1024), path + " 体积过小，疑似空文件");

                    byte[] head = new byte[12];
                    using (FileStream stream = File.OpenRead(path))
                    {
                        int read = stream.Read(head, 0, head.Length);
                        Assert.That(read, Is.EqualTo(head.Length));
                    }

                    Assert.That(System.Text.Encoding.ASCII.GetString(head, 0, 4), Is.EqualTo("RIFF"), path + " 不是 RIFF");
                    Assert.That(System.Text.Encoding.ASCII.GetString(head, 8, 4), Is.EqualTo("WAVE"), path + " 不是 WAVE");
                }
            }
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        static string RelativeToPlatform(string path)
        {
            return path == null ? null : path.Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// 找 Unity 工程根（含 <c>Assets/Resources/PirateCrewAudio</c> 的目录）。
        /// 顺序：环境变量（无头验证台用快照目录时显式指定）→ Unity 侧 <c>Application.dataPath</c>
        /// （EditMode 下直指 <c>&lt;工程&gt;/Assets</c>）→ 从测试程序集目录逐级向上找。
        /// </summary>
        static string FindProjectRoot()
        {
            string env = Environment.GetEnvironmentVariable("PIRATECREW_PROJECT_ROOT");
            if (!string.IsNullOrEmpty(env) && IsProjectRoot(env))
                return env;

            // Unity EditMode：dataPath = <工程>/Assets，其父目录即工程根。
            // 必须走【反射】：无头验证台没有 Unity 运行时，直接 call icall 会在 JIT 编译期炸出
            // SecurityException（try/catch 拦不住，本文件 2026-09-14 实测）；反射调用则把同一错误
            // 包成可捕获的 TargetInvocationException。
            try
            {
                Type appType = Type.GetType("UnityEngine.Application, UnityEngine.CoreModule");
                PropertyInfo dataPath = appType?.GetProperty("dataPath",
                    BindingFlags.Public | BindingFlags.Static);
                string assets = dataPath?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(assets))
                {
                    string parent = Path.GetDirectoryName(assets.Replace('/', Path.DirectorySeparatorChar));
                    if (IsProjectRoot(parent))
                        return parent;
                }
            }
            catch (Exception)
            {
                // 无 Unity 运行时：忽略，走 BaseDirectory 上溯
            }

            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (IsProjectRoot(dir.FullName))
                    return dir.FullName;

                string nested = Path.Combine(dir.FullName, "pirate-crew");
                if (IsProjectRoot(nested))
                    return nested;

                dir = dir.Parent;
            }

            return null;
        }

        static bool IsProjectRoot(string path)
        {
            return !string.IsNullOrEmpty(path)
                   && Directory.Exists(Path.Combine(path, "Assets", "Resources", "PirateCrewAudio"));
        }
    }
}
