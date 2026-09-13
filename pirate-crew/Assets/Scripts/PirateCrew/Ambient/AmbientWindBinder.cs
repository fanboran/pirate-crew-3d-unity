using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.PirateCrew.Ambient
{
    /// <summary>
    /// 把场景美术波次（<c>Assets/Editor/SceneArtBuilder.cs</c>）生成的**合并网格**换成顶点风摆材质，
    /// 让既有棕榈叶 / 灌木 / 草丛 / 旗帜随风摆动。
    ///
    /// 【为什么必须"换材质"而不是"加组件"】
    /// SceneArt 把每一类道具**合并成 1 个网格**（Foliage 一个、FlagRed 一个……），
    /// 网格里所有植株共用一个 Renderer —— 无法用 Transform 逐株摆动。
    /// 唯一能在不拆网格、不改 SceneArt 源码的前提下让它们动起来的办法，
    /// 就是把这一个 Renderer 的材质换成顶点风摆 shader。
    ///
    /// 【代价（已知取舍，必须写进报告）】
    ///   · 换掉的是 <c>PirateCrew/PirateOutline</c> 材质 → 该组**失去 inverted-hull 描边**；
    ///   · 风摆 shader 没有 ShadowCaster Pass → 该组不再投影（本类会把
    ///     <c>shadowCastingMode</c> 显式设为 Off，避免 URP 尝试渲染阴影时报缺失 Pass）。
    /// 两者都是"远景观感细节"，换取"整片植被活起来"，是本模块的主动取舍。
    /// 若协调者判定不可接受，把 <see cref="AmbientDirector"/> 的 <c>enableVegetationWind</c> 关掉即可
    /// （本类会恢复原材质与原阴影设置）。
    ///
    /// 【不做全局搜索】只从传入的 <c>SceneArt</c> 根节点下按**直接子节点名**取
    /// （<see cref="Transform.Find"/>，等价于层级内查找，不是 <c>GameObject.Find</c>）。
    /// 找不到就跳过并记一条警告（容错：SceneArt 波次可能尚未跑、或某组为空没有生成）。
    /// </summary>
    public sealed class AmbientWindBinder
    {
        /// <summary>植被组渲染器名（由 <c>SceneArtBuilder.EmitGroup(root, "Foliage", ...)</c> 生成）。</summary>
        public const string FoliageObjectName = "SceneArt_Foliage";

        /// <summary>红队旗渲染器名。</summary>
        public const string FlagRedObjectName = "SceneArt_FlagRed";

        /// <summary>蓝队旗渲染器名。</summary>
        public const string FlagBlueObjectName = "SceneArt_FlagBlue";

        /// <summary>帆布/布片组渲染器名。</summary>
        public const string ClothObjectName = "SceneArt_Cloth";

        readonly List<Renderer> _bound = new List<Renderer>(4);
        readonly List<Material> _original = new List<Material>(4);
        readonly List<ShadowCastingMode> _originalShadows = new List<ShadowCastingMode>(4);

        /// <summary>已绑定的渲染器数量。</summary>
        public int BoundCount => _bound.Count;

        /// <summary>
        /// 绑定。返回成功替换的渲染器数量。四个材质任一为 null 则跳过对应组。
        /// <paramref name="sceneArtRoot"/> 为 null 时直接返回 0（容错）。
        /// </summary>
        public int Bind(Transform sceneArtRoot, Material foliage, Material flagRed,
            Material flagBlue, Material cloth, bool verbose = true)
        {
            if (sceneArtRoot == null)
            {
                if (verbose)
                    Debug.LogWarning("[Ambient] SceneArt 根节点未接线，植被风摆跳过（场景仍是静态的）。");
                return 0;
            }

            int count = 0;
            count += BindOne(sceneArtRoot, FoliageObjectName, foliage, verbose);
            count += BindOne(sceneArtRoot, FlagRedObjectName, flagRed, verbose);
            count += BindOne(sceneArtRoot, FlagBlueObjectName, flagBlue, verbose);
            count += BindOne(sceneArtRoot, ClothObjectName, cloth, verbose);
            return count;
        }

        int BindOne(Transform root, string objectName, Material windMaterial, bool verbose)
        {
            if (windMaterial == null)
                return 0;

            Transform child = root.Find(objectName);
            if (child == null)
            {
                if (verbose)
                    Debug.LogWarning("[Ambient] SceneArt 下没找到 " + objectName
                        + "，该组不做风摆（可能该组为空未生成，或 SceneArtBuilder 尚未跑）。");
                return 0;
            }

            var renderer = child.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                if (verbose)
                    Debug.LogWarning("[Ambient] " + objectName + " 上没有 MeshRenderer，跳过风摆。");
                return 0;
            }

            _bound.Add(renderer);
            _original.Add(renderer.sharedMaterial);
            _originalShadows.Add(renderer.shadowCastingMode);

            renderer.sharedMaterial = windMaterial;
            // 风摆 shader 无 ShadowCaster Pass：显式关投影，避免 URP 尝试渲染阴影时报缺失 Pass。
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return 1;
        }

        /// <summary>恢复全部原材质与原阴影设置（模块销毁/关闭开关时调用）。</summary>
        public void Restore()
        {
            for (int i = 0; i < _bound.Count; i++)
            {
                Renderer renderer = _bound[i];
                if (renderer == null)
                    continue;

                renderer.sharedMaterial = _original[i];
                renderer.shadowCastingMode = _originalShadows[i];
            }

            _bound.Clear();
            _original.Clear();
            _originalShadows.Clear();
        }
    }
}
