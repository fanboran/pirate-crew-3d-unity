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
