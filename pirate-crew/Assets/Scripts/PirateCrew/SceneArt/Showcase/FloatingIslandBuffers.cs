using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Showcase
{
    /// <summary>
    /// 空岛的**分材质三角面缓冲组**（纯 C#，无头可测）。
    ///
    /// 【与 <see cref="ScenePropBuffers"/> 的关系】同构、但不共用：场景道具的 14 个槽位是
    /// "竞技场陈设"的语义（旗帜红蓝、危险虚线、远景剪影…），空岛是独立展示件，
    /// 强行复用会让两个不相干的组件互相绑架（改动其一必动其二）。
    /// 两者的公共下层（<see cref="MeshBuffers"/>）已经是共享的，重复的只是十几个字段声明。
    ///
    /// 【合并口径】每个槽位最终落成 1 个网格 + 1 个材质 = 1 个 DrawCall；
    /// 一座约 2 万面的浮空岛（岩层 + 悬瀑 + 植被 + 遗迹 + 浮岛群）只有 15 个 DrawCall。
    /// 顶点不复用（<see cref="MeshBuffers"/> 每面独立输出 3 顶点）是块面硬边平面着色的前提，
    /// 顶点量对 2022.3 的静态网格完全不是瓶颈（预算详见 docs/场景设计-战斗竞技场.md §8）。
    /// </summary>
    public sealed class IslandBuffers
    {
        /// <summary>岩层亮档面。</summary>
        public readonly MeshBuffers RockLight = new MeshBuffers();
        /// <summary>岩层中档面。</summary>
        public readonly MeshBuffers RockMid = new MeshBuffers();
        /// <summary>岩层暗档面。</summary>
        public readonly MeshBuffers RockDark = new MeshBuffers();
        /// <summary>草皮亮档面。</summary>
        public readonly MeshBuffers GrassLight = new MeshBuffers();
        /// <summary>草皮中档面（含树冠/灌木）。</summary>
        public readonly MeshBuffers GrassMid = new MeshBuffers();
        /// <summary>草皮暗档面（含垂藤）。</summary>
        public readonly MeshBuffers GrassDark = new MeshBuffers();
        /// <summary>泥土/根须面。</summary>
        public readonly MeshBuffers Dirt = new MeshBuffers();
        /// <summary>石工面（遗迹）。</summary>
        public readonly MeshBuffers Stone = new MeshBuffers();
        /// <summary>木料面（瞭望台/木箱/踏板）。</summary>
        public readonly MeshBuffers Wood = new MeshBuffers();
        /// <summary>水帘面（半透明）。</summary>
        public readonly MeshBuffers Water = new MeshBuffers();
        /// <summary>浪花/水沫面（半透明）。</summary>
        public readonly MeshBuffers Foam = new MeshBuffers();
        /// <summary>云雾面（半透明）。</summary>
        public readonly MeshBuffers Cloud = new MeshBuffers();
        /// <summary>晶体面（加法发光）。</summary>
        public readonly MeshBuffers Crystal = new MeshBuffers();
        /// <summary>暖光点面（加法发光）。</summary>
        public readonly MeshBuffers Glow = new MeshBuffers();
        /// <summary>旗帜面。</summary>
        public readonly MeshBuffers Banner = new MeshBuffers();

        /// <summary>按槽位取缓冲。未在枚举内的槽位回落 <see cref="RockMid"/>（不返回 null）。</summary>
        public MeshBuffers Get(IslandMaterial material)
        {
            switch (material)
            {
                case IslandMaterial.RockLight: return RockLight;
                case IslandMaterial.RockMid: return RockMid;
                case IslandMaterial.RockDark: return RockDark;
                case IslandMaterial.GrassLight: return GrassLight;
                case IslandMaterial.GrassMid: return GrassMid;
                case IslandMaterial.GrassDark: return GrassDark;
                case IslandMaterial.Dirt: return Dirt;
                case IslandMaterial.Stone: return Stone;
                case IslandMaterial.Wood: return Wood;
                case IslandMaterial.Water: return Water;
                case IslandMaterial.Foam: return Foam;
                case IslandMaterial.Cloud: return Cloud;
                case IslandMaterial.Crystal: return Crystal;
                case IslandMaterial.Glow: return Glow;
                default: return Banner;
            }
        }

        /// <summary>全部槽位的三角面总和（性能预算断言用）。</summary>
        public int TotalTriangles
        {
            get
            {
                int sum = 0;
                var all = IslandMaterialCatalog.All;
                for (int i = 0; i < all.Length; i++)
                    sum += Get(all[i]).TriangleCount;
                return sum;
            }
        }

        /// <summary>非空槽位数量（= 预计 DrawCall 数）。</summary>
        public int NonEmptySlots
        {
            get
            {
                int count = 0;
                var all = IslandMaterialCatalog.All;
                for (int i = 0; i < all.Length; i++)
                {
                    if (!Get(all[i]).IsEmpty)
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// 全部槽位的顶点包围盒（空岛自身局部坐标：顶面约 y=0、岛尖约 y=-RockDepth）。
        /// 返回 false = 全部槽位为空。用于把岛摆到场景时**按真实几何**求高度，不靠手写常量。
        /// </summary>
        public bool TryGetBounds(out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;

            var all = IslandMaterialCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                Vector3[] vertices = Get(all[i]).ToVertices();
                for (int v = 0; v < vertices.Length; v++)
                {
                    any = true;
                    Vector3 p = vertices[v];
                    if (p.x < min.x) min.x = p.x;
                    if (p.y < min.y) min.y = p.y;
                    if (p.z < min.z) min.z = p.z;
                    if (p.x > max.x) max.x = p.x;
                    if (p.y > max.y) max.y = p.y;
                    if (p.z > max.z) max.z = p.z;
                }
            }

            if (!any)
                min = max = Vector3.zero;
            return any;
        }
    }
}
