using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 世界地图运行时组装：站面 box → BoxCollider + 分色灰盒视觉。
    ///
    /// 【站面平直机制（§4.2）】碰撞不读网格：每个 <see cref="WorldStandBox"/> 直接生成一个
    /// 顶面严格水平的 BoxCollider——「视觉自由、碰撞平直」两解耦。kit FBX 的网格视觉由
    /// <see cref="WorldMapAssetSet"/>（Editor 生成、Battle 场景持有引用）接入后叠加在灰盒之上；
    /// 资产表缺件或未配置时仅保留灰盒（测试与捕图不中断）。
    ///
    /// 【灰盒配色】按顶面高度带取 <c>SceneArtPalette</c> 同源中间调：+0.5~+1.5 沙、+2~+3 草、
    /// +3.5 以上岩（三档明度纪律：只给中间调，亮/暗由光照产生）。
    /// </summary>
    public static class WorldMapComposer
    {
        /// <summary>碰撞体向下延伸深度（锚进海床，防穿底/隧穿）。</summary>
        const float ColliderDepth = 4f;

        /// <summary>构建整图（碰撞 + 灰盒 + 已配置的 kit 视觉）。返回根 Transform。</summary>
        public static Transform Build(Transform parent, WorldMapDefinition map, WorldMapAssetSet assetSet)
        {
            var root = new GameObject("WorldMap_" + map.Id);
            if (parent != null)
                root.transform.SetParent(parent, false);

            var standBoxes = WorldMapRules.AllStandBoxes(map);
            var collisionRoot = new GameObject("Stands");
            collisionRoot.transform.SetParent(root.transform, false);
            for (int i = 0; i < standBoxes.Count; i++)
                BuildStandBox(collisionRoot.transform, standBoxes[i]);

            var kitRoot = new GameObject("Kit");
            kitRoot.transform.SetParent(root.transform, false);
            for (int i = 0; i < map.Terrain.Count; i++)
                BuildKitPlacement(kitRoot.transform, "Terrain", map.Terrain[i], assetSet);
            for (int i = 0; i < map.Horizon.Count; i++)
                BuildKitPlacement(kitRoot.transform, "Horizon", map.Horizon[i], assetSet);
            for (int i = 0; i < map.Props.Count; i++)
            {
                WorldPropPlacement prop = map.Props[i];
                GameObject prefab = assetSet != null ? assetSet.Get(prop.Asset) : null;
                if (prefab == null)
                    continue;
                var go = Object.Instantiate(prefab, kitRoot.transform);
                go.name = "Prop_" + prop.Asset;
                // 落地吸附：道具 y 一律吸附到所在 (x,z) 的地表顶高（目录里的 y 只是提示），
                // 水面件（浮标等）吸附到水面 —— 消掉「摆错高度」这一整类手工错误。
                go.transform.position = new Vector3(
                    prop.Position.x,
                    WorldMapRules.HeightAtWorld(standBoxes, new Vector2(prop.Position.x, prop.Position.z)),
                    prop.Position.z);
                go.transform.rotation = Quaternion.Euler(0f, prop.YawDeg, 0f);
                go.isStatic = true;
            }
            return root.transform;
        }

        static void BuildStandBox(Transform root, in WorldMapRules.WorldBox box)
        {
            var go = new GameObject(string.Format("Stand_{0:F0}_{1:F0}_{2:F1}",
                box.Center.x, box.Center.y, box.TopY));
            go.transform.SetParent(root, false);
            // box.Center 为平面 (X, Z)；摆放根在海平面 y=0，顶面 = TopY。
            go.transform.position = new Vector3(box.Center.x, 0f, box.Center.y);
            go.transform.rotation = Quaternion.Euler(0f, box.YawDeg, 0f);

            float height = box.TopY + ColliderDepth;
            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(box.Size.x, height, box.Size.y);
            collider.center = new Vector3(0f, box.TopY - height * 0.5f, 0f);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = CubeMesh();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BandMaterial(box.TopY);
        }

        static void BuildKitPlacement(Transform root, string group, in WorldKitPlacement placement,
            WorldMapAssetSet assetSet)
        {
            GameObject prefab = assetSet != null ? assetSet.Get(placement.Asset) : null;
            if (prefab == null)
                return; // 站面灰盒即地形本体；kit 件缺位时静默跳过

            var go = Object.Instantiate(prefab, root);
            go.name = group + "_" + placement.Asset;
            go.transform.position = placement.Position;
            go.transform.rotation = Quaternion.Euler(0f, placement.YawDeg, 0f);
            go.isStatic = true;
        }

        // ------------------------------------------------------------------
        // 共享网格与材质
        // ------------------------------------------------------------------

        static Mesh _cube;

        static Mesh CubeMesh()
        {
            if (_cube == null)
            {
                var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(temp.GetComponent<Collider>());
                temp.hideFlags = HideFlags.HideAndDontSave;
                _cube = temp.GetComponent<MeshFilter>().sharedMesh;
            }
            return _cube;
        }

        static readonly Dictionary<int, Material> _bandMaterials = new Dictionary<int, Material>();

        static Material BandMaterial(float topY)
        {
            int band = topY <= 1.5f ? 0 : (topY <= 3.0f ? 1 : 2);
            if (_bandMaterials.TryGetValue(band, out Material mat))
                return mat;

            // SceneArtPalette 中间调：沙 #C4A76A / 草 #4A8C4A / 岩 #8C7B6A（docs/美术风格指南.md §2.1）
            var color = band == 0
                ? new Color(0.769f, 0.655f, 0.416f)
                : band == 1
                    ? new Color(0.290f, 0.549f, 0.290f)
                    : new Color(0.549f, 0.482f, 0.416f);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(shader);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else
                mat.color = color;
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", band == 0 ? 0.18f : 0.22f);
            _bandMaterials[band] = mat;
            return mat;
        }
    }
}
