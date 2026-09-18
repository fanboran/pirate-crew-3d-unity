using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>
    /// 构件调度（纯 C#，无头可测）：把 <see cref="SceneKitLayout"/> 的摆放表翻译成三角面，
    /// 按材质组写进 <see cref="ScenePropBuffers"/>（构建期同材质合并 → 1 组 = 1 DrawCall）。
    ///
    /// 【为什么单独一层】编辑器构建（<c>Assets/Editor/SceneArtBuilder.cs</c>）与性能预算用例
    /// 必须共用同一份调度：若两处各写一遍，"实现改了、预算测试没跟上"会让三角面数字悄悄失真。
    ///
    /// 【无碰撞（表现层）】本类**只往 <see cref="MeshBuffers"/> 里写顶点/三角面**，没有任何
    /// Collider 概念；运行时 <c>RuntimeSceneArt</c> 把这些缓冲挂成 MeshFilter + MeshRenderer。
    /// 故 kit 是纯表现层：船体 / 桅 / 岛顶都不参与物理，不挡单位、不弹投掷物
    /// （场景文档 §9.4："预览 = 实弹"）。玩法锚点（可站面 / 水距 / 高度 / 单位落位）全部来自
    /// <c>PlatformMap</c> / <c>TileTerrainGrid</c>，与本层无关。
    ///
    /// 【加新构件种类时必须在这里加 case】<see cref="SceneKitPiece"/> 是枚举，缺 case 的构件会
    /// 静默无几何（"名字对、画面上什么都没有"），比漏个材质更难查 —— 新增枚举值务必同步本 switch。
    /// </summary>
    public static class SceneKitComposer
    {
        /// <summary>按材质组取目标缓冲。</summary>
        public static MeshBuffers BufferOf(ScenePropBuffers buffers, SceneKitMaterial material)
        {
            switch (material)
            {
                case SceneKitMaterial.WoodDark: return buffers.WoodDark;
                case SceneKitMaterial.Rock: return buffers.Rock;
                case SceneKitMaterial.Metal: return buffers.Metal;
                case SceneKitMaterial.Cloth: return buffers.Cloth;
                case SceneKitMaterial.Foliage: return buffers.Foliage;
                default: return buffers.Wood;
            }
        }

        /// <summary>展开整套 kit 摆放表。确定性：同 layout + 同 seed 必得同一网格。</summary>
        public static void Compose(ScenePropBuffers buffers, SceneKitLayout layout, int seed)
        {
            if (buffers == null || layout == null)
                return;

            var parts = layout.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                KitPart p = parts[i];
                int s = seed + i * 17;
                MeshBuffers target = BufferOf(buffers, p.Material);

                switch (p.Piece)
                {
                    case SceneKitPiece.ShipHullLoft:
                        // 整艘放样船体（真船建模，用户裁决 2026-09-14）：
                        // Scale=船宽、Length=总长、Height=甲板到龙骨吃深；亮/暗木两缓冲直接写入。
                        ShipHullGeometry.AddLoftedHull(buffers.Wood, buffers.WoodDark,
                            p.Position, p.YawDegrees, p.Length, p.Scale, p.Height);
                        break;

                    case SceneKitPiece.HullBow:
                        SceneKitGeometry.AddHullSegment(target, p.Position, p.YawDegrees,
                            p.Length, p.Height, p.Scale, 0.6f, s);
                        break;

                    case SceneKitPiece.HullMid:
                        SceneKitGeometry.AddHullSegment(target, p.Position, p.YawDegrees,
                            p.Length, p.Height, p.Scale, 1.0f, s);
                        break;

                    case SceneKitPiece.HullStern:
                        SceneKitGeometry.AddHullSegment(target, p.Position, p.YawDegrees,
                            p.Length, p.Height, p.Scale, 0.82f, s);
                        break;

                    case SceneKitPiece.DeckPlank:
                        SceneKitGeometry.AddDeckPlank(target, p.Position, p.YawDegrees,
                            p.Length, Mathf.Max(0.04f, p.Height), 0.5f, s);
                        break;

                    case SceneKitPiece.Bulwark:
                        SceneKitGeometry.AddBulwark(target, p.Position, p.YawDegrees, p.Length, p.Height, s);
                        break;

                    case SceneKitPiece.Mast:
                        SceneKitGeometry.AddMast(buffers.Wood, buffers.Metal, p.Position, p.YawDegrees,
                            p.Length, p.Height, s);
                        break;

                    case SceneKitPiece.Rigging:
                        SceneKitGeometry.AddRigging(target, p.Position, p.YawDegrees, p.Length, p.Height, s);
                        break;

                    case SceneKitPiece.Sail:
                        SceneKitGeometry.AddSail(target, p.Position, p.YawDegrees, p.Length, p.Scale, s);
                        break;

                    case SceneKitPiece.Yard:
                        // 横桁：一根沿船宽方向的扁木杆（帆挂在它下面）。舷墙那段薄板几何正好就是
                        // "一根扁木杆"，直接复用（位置 / 朝向由摆位表给）。
                        SceneKitGeometry.AddBulwark(target, p.Position, p.YawDegrees,
                            p.Length, Mathf.Max(0.08f, p.Height), s);
                        break;

                    case SceneKitPiece.CrowNest:
                        // 桅顶瞭望巢：木底板（甲板薄板几何）+ 一圈矮栏（岛顶岩唇几何，换木材质即木斗）。
                        SceneKitGeometry.AddDeckPlank(target, p.Position, p.YawDegrees,
                            p.Length * 2f, 0.09f, p.Scale * 2f, s);
                        SceneKitGeometry.AddIslandTop(target, p.Position + Vector3.up * 0.12f,
                            p.Length, p.Height, s);
                        break;

                    case SceneKitPiece.BowDeco:
                    {
                        // 船艏装饰：艏楼横栏（沿船宽的一道矮栏）+ 艏斜桁（从艏端甲板斜向前上方伸出的短杆）。
                        // 艏斜桁直接用杆图元：几何层没有"斜桁"这个函数，而调度层取一次图元比扩几何层更小。
                        SceneKitGeometry.AddBulwark(target, p.Position, p.YawDegrees + 90f,
                            p.Scale, Mathf.Max(0.14f, p.Height), s);
                        float rad = p.YawDegrees * Mathf.Deg2Rad;
                        Vector3 forward = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
                        target.AddRod(p.Position + Vector3.up * 0.06f,
                            p.Position + forward * p.Length + Vector3.up * (p.Height * 1.6f), 0.05f, 5);
                        break;
                    }

                    case SceneKitPiece.IslandTop:
                        SceneKitGeometry.AddIslandTop(target, p.Position, p.Length, p.Height, s);
                        break;

                    case SceneKitPiece.RockChunk:
                        SceneKitGeometry.AddRockChunk(target, p.Position, p.Scale, p.YawDegrees, s);
                        break;

                    case SceneKitPiece.Prop:
                        ComposeProp(buffers, p, s);
                        break;
                }
            }
        }

        static void ComposeProp(ScenePropBuffers buffers, in KitPart p, int seed)
        {
            switch (p.PropKind)
            {
                case ScenePropKind.Palm:
                    ScenePropGeometry.AddPalm(buffers, p.Position, p.YawDegrees, p.Scale, seed);
                    break;

                case ScenePropKind.Barrel:
                    ScenePropGeometry.AddBarrel(buffers, p.Position, p.YawDegrees);
                    break;

                case ScenePropKind.Anchor:
                    ScenePropGeometry.AddAnchor(buffers, p.Position, p.YawDegrees, p.Scale);
                    break;

                default:
                    ScenePropGeometry.AddCrate(buffers, p.Position, p.YawDegrees);
                    break;
            }
        }
    }
}
