using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>
    /// 远景/植被的**分层附加材质组**（【AI 提案】，可选）：把"一整条同色"的远景拆成可分别配色的层。
    ///
    /// 【为什么要拆】一个材质组只能有一个基色，而验收要求：
    ///   · 远岛 ≥3 层、逐层提亮 + 雾衰减（近 <c>#7E93A8</c> → 远 <c>#AFC2D4</c>）；
    ///   · 云"中心实、边缘虚"（顶点 alpha 在本工程 <see cref="MeshBuffers"/> 里没有通道，只能用高/低 alpha 两层近似）；
    ///   · 草梢两档绿 + 草根渐暗（场景文档 §3.4 的梢 <c>#7BC67E</c> / 根 <c>#2D5A2D</c>）。
    /// 这些都需要"同几何、不同材质"，故由编辑器侧建这些缓冲并各落一个材质组。
    ///
    /// 【不传时（<c>tiers == null</c>）】全部远景剪影并进 <see cref="ScenePropBuffers.Silhouette"/>、
    /// 云并进 <see cref="ScenePropBuffers.Cloud"/>、草并进 <see cref="ScenePropBuffers.Foliage"/>，
    /// 即保持旧的单组行为（无头测试走这条，不产生未登记材质）。
    /// </summary>
    public sealed class ScenePropTierBuffers
    {
        /// <summary>远岛近层（最暗，与天空拉开 ΔL*）。</summary>
        public readonly MeshBuffers SilhouetteNear;

        /// <summary>远岛中层。</summary>
        public readonly MeshBuffers SilhouetteMid;

        /// <summary>远岛远层（最亮最蓝灰，负责抬高地平线亮度）。</summary>
        public readonly MeshBuffers SilhouetteFar;

        /// <summary>云核（高 alpha）。</summary>
        public readonly MeshBuffers CloudCore;

        /// <summary>云缘（低 alpha，近似顶点 alpha 渐变的外圈）。</summary>
        public readonly MeshBuffers CloudFringe;

        /// <summary>远景帆船帆布（<c>#E8E8E0</c>，非过曝）。</summary>
        public readonly MeshBuffers SailFar;

        /// <summary>草根（暗绿 <c>#2D5A2D</c>）。</summary>
        public readonly MeshBuffers GrassRoot;

        /// <summary>草梢亮档（介于 <c>GrassMid</c> 与 <c>GrassLight</c> 之间）。</summary>
        public readonly MeshBuffers GrassLight;

        public ScenePropTierBuffers(MeshBuffers silhouetteNear, MeshBuffers silhouetteMid,
            MeshBuffers silhouetteFar, MeshBuffers cloudCore, MeshBuffers cloudFringe,
            MeshBuffers sailFar, MeshBuffers grassRoot, MeshBuffers grassLight)
        {
            SilhouetteNear = silhouetteNear;
            SilhouetteMid = silhouetteMid;
            SilhouetteFar = silhouetteFar;
            CloudCore = cloudCore;
            CloudFringe = cloudFringe;
            SailFar = sailFar;
            GrassRoot = grassRoot;
            GrassLight = grassLight;
        }
    }

    /// <summary>
    /// 把 <see cref="SceneLayout"/> 的摆位表"翻译成三角面"（纯 C#，无头可测）。
    ///
    /// 【为什么单独一层】编辑器构建器（<c>Assets/Editor/SceneArtBuilder.cs</c>）与性能自证用例
    /// （<c>Assets/Tests/SceneArt/</c>）需要**同一份**调度逻辑：
    ///   · 编辑器侧拿它填 <see cref="ScenePropBuffers"/> 再落成合并网格资产；
    ///   · 测试侧拿它数三角面，把"场景总三角面 ≤ 150k"这条预算（场景文档 §8）变成可回归的断言。
    /// 若把调度写两份，预算数字会在"实现改了、测试没跟上"时悄悄失真。
    ///
    /// 【不含 Unity 对象】只写 <see cref="MeshBuffers"/>，不 new Mesh / 不建 GameObject。
    /// </summary>
    public static class ScenePropComposer
    {
        /// <summary>
        /// 按摆位表生成全部道具几何（单组模式：远景剪影/云/草都并进 <see cref="ScenePropBuffers"/> 既有组）。
        /// </summary>
        /// <param name="buffers">分材质组的三角面缓冲（原地追加）。</param>
        /// <param name="layout">摆位表（<see cref="ScenePropLayout.Build"/> 的产物）。</param>
        /// <param name="seed">随机种子（关卡号派生；只影响形状抖动，不影响摆位）。</param>
        /// <param name="arenaDepth">竞技场纵深（算远景基座高度用）。</param>
        public static void Compose(ScenePropBuffers buffers, SceneLayout layout, int seed, int arenaDepth)
        {
            Compose(buffers, layout, seed, arenaDepth, null);
        }

        /// <summary>
        /// 按摆位表生成全部道具几何；<paramref name="tiers"/> 非空时把远景/草按层写进各自材质组。
        /// 远景剪影的基座高度由 <see cref="HorizonBaseY"/> 决定（见其注释）。
        /// </summary>
        /// <param name="tiers">分层附加材质组（编辑器侧用；无头测试传 null）。</param>
        public static void Compose(ScenePropBuffers buffers, SceneLayout layout, int seed, int arenaDepth,
            ScenePropTierBuffers tiers)
        {
            if (buffers == null || layout == null)
                return;

            float arenaW = layout.ArenaWidth;
            IReadOnlyList<PropPlacement> props = layout.Props;

            for (int i = 0; i < props.Count; i++)
            {
                PropPlacement p = props[i];
                int s = seed + i * 31;

                switch (p.Kind)
                {
                    case ScenePropKind.Wreck:
                        ScenePropGeometry.AddWreck(buffers, p.Position, p.YawDegrees, p.RollDegrees,
                            beam: 4.6f, depth: 2.0f, halfLength: p.Length * 0.5f, seed: s);
                        break;

                    case ScenePropKind.Jetty:
                        ScenePropGeometry.AddJetty(buffers, p.Position.x - p.Length * 0.5f,
                            p.Position.x + p.Length * 0.5f, p.Position.z, p.Position.y);
                        break;

                    case ScenePropKind.FlagPole:
                        ScenePropGeometry.AddFlagPole(buffers, p.Position, p.YawDegrees,
                            redTeam: p.Position.x < arenaW * 0.5f, height: 3.2f, seed: s);
                        break;

                    case ScenePropKind.SignPost:
                        ScenePropGeometry.AddSignPost(buffers, p.Position, p.YawDegrees);
                        break;

                    case ScenePropKind.Crate:
                        {
                            // Scale 存的是堆叠层数（1-2 层，场景文档 §4.3）。
                            int stack = Mathf.Max(1, Mathf.RoundToInt(p.Scale));
                            for (int k = 0; k < stack; k++)
                                ScenePropGeometry.AddCrate(buffers, p.Position + Vector3.up * (0.6f * k), p.YawDegrees);
                        }
                        break;

                    case ScenePropKind.Barrel:
                        ScenePropGeometry.AddBarrel(buffers, p.Position, p.YawDegrees);
                        break;

                    case ScenePropKind.Anchor:
                        ScenePropGeometry.AddAnchor(buffers, p.Position, p.YawDegrees);
                        break;

                    case ScenePropKind.Debris:
                        ScenePropGeometry.AddDebris(buffers, p.Position, p.YawDegrees, s % 4, s);
                        break;

                    case ScenePropKind.Palm:
                        ScenePropGeometry.AddPalm(buffers, p.Position, p.YawDegrees, p.Scale, s);
                        break;

                    case ScenePropKind.Bush:
                        ScenePropGeometry.AddBush(buffers, p.Position, p.Scale, s);
                        break;

                    case ScenePropKind.GrassTuft:
                        // 单组模式：整簇写进植被档（保持旧行为）。
                        // 分层模式：下段写草根暗档、上段按摆位 Tier 写中绿（Foliage）或亮绿（GrassLight）。
                        if (tiers == null)
                        {
                            ScenePropGeometry.AddGrassTuft(buffers.Foliage, null,
                                p.Position, p.Scale, s, p.YawDegrees);
                        }
                        else
                        {
                            ScenePropGeometry.AddGrassTuft(
                                p.Tier == 1 ? tiers.GrassLight : buffers.Foliage, tiers.GrassRoot,
                                p.Position, p.Scale, s, p.YawDegrees);
                        }
                        break;

                    case ScenePropKind.RidgeRock:
                    case ScenePropKind.IntertidalRock:
                    case ScenePropKind.CoverRock:
                        ScenePropGeometry.AddRock(buffers, p.Position, p.Scale, s);
                        break;

                    case ScenePropKind.Shell:
                        ScenePropGeometry.AddShell(buffers, p.Position, p.Scale, s);
                        break;

                    case ScenePropKind.SeabedMound:
                        // 脊顶控制在 -0.38 ~ -0.52（水面下 0.23-0.37）→ 浅滩色带里能看见沙脊轮廓（P2）。
                        ScenePropGeometry.AddShoal(buffers.SandWet, p.Position.x, p.Position.z,
                            -0.38f - 0.14f * ((s % 7) / 7f), p.Scale, s);
                        break;

                    case ScenePropKind.CloudPuff:
                        if (tiers == null)
                            ScenePropGeometry.AddCloudPuff(buffers.Cloud, null, p.Position, p.Scale, s);
                        else
                            ScenePropGeometry.AddCloudPuff(tiers.CloudCore, tiers.CloudFringe,
                                p.Position, p.Scale, s);
                        break;

                    case ScenePropKind.FarIsland:
                        ScenePropGeometry.AddFarIsland(
                            tiers == null ? buffers.Silhouette : FarSilhouetteTarget(tiers, p.Tier),
                            new Vector3(p.Position.x, HorizonBaseY(p.Position.z, arenaDepth) - 0.6f, p.Position.z),
                            p.Scale, p.Height, s, p.Tier);
                        break;

                    case ScenePropKind.FarShip:
                        ScenePropGeometry.AddFarShip(
                            tiers == null ? buffers.Silhouette : tiers.SilhouetteNear,
                            tiers == null ? buffers.Cloud : tiers.SailFar,
                            new Vector3(p.Position.x, HorizonBaseY(p.Position.z, arenaDepth) - 0.4f, p.Position.z),
                            p.Scale, s);
                        break;
                }
            }
        }

        /// <summary>按远岛层号（0 近 / 1 中 / 2 远）取目标材质组。</summary>
        static MeshBuffers FarSilhouetteTarget(ScenePropTierBuffers tiers, int tier)
        {
            if (tier <= 0)
                return tiers.SilhouetteNear;
            if (tier == 1)
                return tiers.SilhouetteMid;
            return tiers.SilhouetteFar;
        }

        /// <summary>
        /// 远景剪影的**基座高度**：让岛/船"从水远缘长出来"而不是浮在天上。
        ///
        /// 【几何依据】相机 pitch 45°、距离 18（<c>docs/M2-3D空间模型对齐.md</c> §2/§3），
        /// 故相机在焦点 +Z/+Y 侧各 12.73 单位。水面立方体只铺到竞技场外扩 40 单位
        /// （<c>M2BattleSceneSetup.CreateWaterPlane</c> 的 margin=40）；比"水远缘视线"更平缓的视线
        /// 越过水缘后打到的是天空。因此更远的剪影必须把基座压到该视线高度**以下**，
        /// 画面上才会正好与水远缘相接。
        /// 该视线在纵深 z 处的高度 ≈ <c>camY − 0.313 × (camZ − z)</c>
        /// （斜率 0.313 = tan(17.4°)，由水远缘（Z=arenaD/2−40）反算）。
        ///
        /// 【AI 提案】斜率与水缘距离是按本工程默认相机（距离 18 / pitch 45°）与默认水面尺寸手算的；
        /// 换相机距离/俯角或换水面 margin 时必须重算，否则远景会浮空或沉进水里。
        /// </summary>
        /// <param name="z">剪影所在世界 Z（应为负数，即场外远侧）。</param>
        /// <param name="arenaDepth">竞技场纵深（瓦片数）。</param>
        public static float HorizonBaseY(float z, int arenaDepth)
        {
            const float cameraDistance = 18f;
            const float cameraPitchDegrees = 45f;
            const float slope = 0.313f;
            const float waterMargin = 40f;

            float camZ = arenaDepth * 0.5f + cameraDistance * Mathf.Cos(cameraPitchDegrees * Mathf.Deg2Rad);
            float camY = cameraDistance * Mathf.Sin(cameraPitchDegrees * Mathf.Deg2Rad);

            // 水远缘（相机侧与远侧对称外扩，故远缘 z = arenaDepth/2 − waterMargin）。
            float waterFarEdgeZ = arenaDepth * 0.5f - waterMargin;
            float horizonDropAtFarEdge = camY - slope * (camZ - waterFarEdgeZ);

            // 以水远缘视线为基准，按距离线性外推到 z 处。
            return horizonDropAtFarEdge - slope * (waterFarEdgeZ - z);
        }
    }
}
