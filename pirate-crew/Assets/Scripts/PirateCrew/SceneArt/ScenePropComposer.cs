using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
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
        /// 按摆位表生成全部道具几何。远景剪影的基座高度由 <see cref="HorizonBaseY"/> 决定（见其注释）。
        /// </summary>
        /// <param name="buffers">分材质组的三角面缓冲（原地追加）。</param>
        /// <param name="layout">摆位表（<see cref="ScenePropLayout.Build"/> 的产物）。</param>
        /// <param name="seed">随机种子（关卡号派生；只影响形状抖动，不影响摆位）。</param>
        /// <param name="arenaDepth">竞技场纵深（算远景基座高度用）。</param>
        public static void Compose(ScenePropBuffers buffers, SceneLayout layout, int seed, int arenaDepth)
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
                        ScenePropGeometry.AddGrassTuft(buffers, p.Position, p.Scale, s);
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
                        ScenePropGeometry.AddCloudPuff(buffers, p.Position, p.Scale, s);
                        break;

                    case ScenePropKind.FarIsland:
                        ScenePropGeometry.AddFarIsland(buffers,
                            new Vector3(p.Position.x, HorizonBaseY(p.Position.z, arenaDepth) - 0.6f, p.Position.z),
                            p.Scale, p.Height, s);
                        break;

                    case ScenePropKind.FarShip:
                        ScenePropGeometry.AddFarShip(buffers,
                            new Vector3(p.Position.x, HorizonBaseY(p.Position.z, arenaDepth) - 0.4f, p.Position.z),
                            p.Scale, s);
                        break;
                }
            }
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
