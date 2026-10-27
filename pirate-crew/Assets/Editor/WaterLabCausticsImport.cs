using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 焦散体（原作 Texture3D "Caustics"）导入器（I2，沙面复刻）。
    ///
    /// 【真值源】Sandcastle 发行包 <c>sharedassets0.assets</c> path_id 932（名 "Caustics"）：
    /// 256×256×64、R8_UNorm（线性，无 sRGB）、9 mip（mip 数据大→小连续，z 轴按 64→1 收缩）、
    /// Bilinear / Repeat×3 / Aniso 1。体素数据 Σ=4,793,493 B 与 typetree m_StreamData
    /// 对账一致。提取件与导入规格：external/sandcastle-ref/extracted/caustics/（import-spec.txt），
    /// 语义（半相位交叉淡化采样）见 rt/reports/D-sand.md §1/§2.3 段 A/B。
    ///
    /// 【为什么手工逐 mip 构建】9 级 mip 字节都是第一手原作数据，**禁止**让导入器或
    /// 运行时重算（重算即改写烘焙结果）——raw 以 .bytes 入库（工程内拷贝，本目录
    /// <see cref="Folder"/>），SetPixelData 逐级灌进 Texture3D 后存为 <see cref="AssetPath"/>。
    ///
    /// 【数据源优先级】工程内 mip*.bytes（已入库）→ external 工作台 raw 自愈拷入
    /// （模式同 WaterLabSetup.EnsureRefNormalAsset；env <c>PC3D_WATERLAB_CAUSTICS_SRC</c>
    /// 可覆盖源目录）。
    /// 菜单：Tools/PirateCrew/WaterLab/Import Caustics Texture3D。
    /// </summary>
    public static class WaterLabCausticsImport
    {
        /// <summary>raw 入库目录（.bytes = TextAsset，绕开纹理导入器的任何再编码）。</summary>
        public const string Folder = "Assets/Art/Textures/WaterLab/Caustics";

        /// <summary>Texture3D 资产落盘路径（import-spec.txt 建议位置）。</summary>
        public const string AssetPath = "Assets/Art/Textures/WaterLab/Caustics.asset";

        /// <summary>各级 mip 的 xy 尺寸（与 raw 文件名一一对应，Σ=4,793,493 B 已对账）。</summary>
        static readonly int[] MipSize = { 256, 128, 64, 32, 16, 8, 4, 2, 1 };

        /// <summary>各级 mip 的 z 深度（64→1 收缩，尾部补 1）。</summary>
        static readonly int[] MipDepth = { 64, 32, 16, 8, 4, 2, 1, 1, 1 };

        static readonly string[] MipFileNames =
        {
            "mip0_256x256x64_r8", "mip1_128x128x32_r8", "mip2_64x64x16_r8",
            "mip3_32x32x8_r8", "mip4_16x16x4_r8", "mip5_8x8x2_r8",
            "mip6_4x4x1_r8", "mip7_2x2x1_r8", "mip8_1x1x1_r8",
        };

        [MenuItem("Tools/PirateCrew/WaterLab/Import Caustics Texture3D")]
        public static void ImportFromMenu()
        {
            var tex = EnsureCausticsAsset();
            Debug.Log(tex != null
                ? $"[WaterLabCausticsImport] 焦散体就绪：{AssetPath}"
                  + $"（{tex.width}×{tex.height}×{tex.depth}，mips={tex.mipmapCount}）"
                : "[WaterLabCausticsImport] 焦散体构建失败（见上方日志）");
        }

        /// <summary>装配期入口（WaterLabSetup 材质段调用）：资产在即返回；缺失则从 raw
        /// 构建并存盘。返回 null = 数据不可得（材质焦散槽落空，焦散被淹没门控自然置 0，
        /// 场景仍可装配——不阻断重建）。</summary>
        public static Texture3D EnsureCausticsAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(AssetPath);
            if (existing != null)
                return existing;

            var mips = LoadRawMips();
            if (mips == null)
                return null;

            EnsureFolder();
            var tex = new Texture3D(MipSize[0], MipSize[0], MipDepth[0], TextureFormat.R8, true)
            {
                name = "Caustics",
                // 采样设置照抄 typetree m_TextureSettings（import-spec.txt）：
                wrapMode = TextureWrapMode.Repeat, // WrapU/V/W=0(Repeat)，三轴同值
                filterMode = FilterMode.Bilinear,  // FilterMode=1(Bilinear)
                anisoLevel = 1,                    // Aniso=1
            };
            if (tex.mipmapCount != MipFileNames.Length)
            {
                Debug.LogError($"[WaterLabCausticsImport] Texture3D mip 链级数不符："
                    + $"分配得 {tex.mipmapCount}，原作 {MipFileNames.Length}——放弃填充。");
                Object.DestroyImmediate(tex);
                return null;
            }
            for (int m = 0; m < MipFileNames.Length; m++)
                tex.SetPixelData(mips[m], m);
            // updateMipmaps=false：9 级 mip 全是原作字节，禁止 GPU 重算任何一级。
            tex.Apply(false, false);

            AssetDatabase.CreateAsset(tex, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[WaterLabCausticsImport] 已构建焦散体资产：{AssetPath}"
                + $"（256×256×64 R8，9 mip，顶层 {mips[0].Length} B）");
            return tex;
        }

        /// <summary>逐级读 raw：先取工程内 .bytes，缺则从 external 自愈拷入后再读。</summary>
        static List<byte[]> LoadRawMips()
        {
            var mips = new List<byte[]>(MipFileNames.Length);
            for (int m = 0; m < MipFileNames.Length; m++)
            {
                string localPath = $"{Folder}/{MipFileNames[m]}.bytes";
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(localPath);
                byte[] data = asset != null ? asset.bytes : CopyFromExternalAndRead(m, localPath);
                int expect = MipSize[m] * MipSize[m] * MipDepth[m];
                if (data == null)
                {
                    Debug.LogError($"[WaterLabCausticsImport] 缺 mip 数据：{localPath}"
                        + $"（external 源也不可得，env PC3D_WATERLAB_CAUSTICS_SRC 可指源目录）");
                    return null;
                }
                if (data.Length != expect)
                {
                    Debug.LogError($"[WaterLabCausticsImport] mip{m} 字节数不符："
                        + $"{data.Length} ≠ 期望 {expect}（{MipSize[m]}²×{MipDepth[m]}）——数据源可疑，放弃。");
                    return null;
                }
                mips.Add(data);
            }
            return mips;
        }

        /// <summary>从 external 工作台拷 raw 入库（.bytes 便于 TextAsset 直读且无导入器副作用），
        /// 拷完登记 AssetDatabase 后回读字节。</summary>
        static byte[] CopyFromExternalAndRead(int mipIndex, string localPath)
        {
            string fileName = MipFileNames[mipIndex] + ".raw";
            foreach (var dir in ExternalSourceCandidates())
            {
                string src = Path.Combine(dir, fileName);
                if (!File.Exists(src))
                    continue;
                File.Copy(src, localPath);
                AssetDatabase.ImportAsset(localPath); // 先登记，LoadAssetAtPath 才拿得到
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(localPath);
                Debug.Log($"[WaterLabCausticsImport] 自愈拷入 {localPath} ← {src}");
                return asset != null ? asset.bytes : File.ReadAllBytes(localPath);
            }
            return null;
        }

        /// <summary>external 源目录候选：env 优先，其后工程根外层工作台
        ///（dataPath 上两级 = 仓库/worktree 根，主仓与 temp worktree 同构）。</summary>
        static IEnumerable<string> ExternalSourceCandidates()
        {
            var env = System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_CAUSTICS_SRC");
            if (!string.IsNullOrEmpty(env))
                yield return env;
            yield return Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../external/sandcastle-ref/extracted/caustics"));
        }

        static void EnsureFolder()
        {
            string current = "Assets";
            foreach (var part in Folder.Split('/'))
            {
                if (part == "Assets")
                    continue;
                var next = current + "/" + part;
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, part);
                current = next;
            }
            AssetDatabase.Refresh();
        }
    }
}
