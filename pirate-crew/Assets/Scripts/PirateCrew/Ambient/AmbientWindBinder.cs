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
    /// 【代价（已知取舍）】
    ///   · 换掉的是 <c>PirateCrew/PirateOutline</c> 材质 → 该组失去 inverted-hull 描边。
    /// 其余照旧：若协调者判定不可接受，把 <see cref="AmbientDirector"/> 的 <c>enableVegetationWind</c>
    /// 关掉即可（本类会恢复原材质与原阴影设置）。
    ///
    /// 【阴影（写实方向：投影 + 受影都开）】
    ///   风摆 shader（<c>PirateAmbientWind.shader</c>）已补 ShadowCaster Pass，且与本体 Pass 共享
    ///   SubShader 级 HLSLINCLUDE 里的同一份 <c>ApplyWindDisplacement</c>（影子与摆动严格同相），
    ///   故本类**不再**把 <c>shadowCastingMode</c> 设为 Off —— 绑定后显式保留 <c>On</c>，
    ///   植被/旗帜既能向地面投影，也能接收其他物件的投影（本体 ForwardLit 已采样主光阴影）。
    ///   曾经的"风摆 shader 无 ShadowCaster → 关投影"取舍已随该 Pass 落地而作废。
    ///
    /// 【不全局搜索】优先按传入的 <c>SceneArt</c> 根节点**直接子节点名**取
    /// （<see cref="Transform.Find"/>，等价于层级内查找，不是 <c>GameObject.Find</c>）；
    /// 直接子节点没有时再在该根的**后代**里按同名匹配（烘焙 prefab 实例内部的组渲染器，
    /// 如 <c>Ship_Galleon/SceneArt_Cloth</c>——深度只限 SceneArt 子树，不做全场景搜索）。
    /// 找不到就跳过并记一条警告（容错：烘焙件可能未跑、或某组为空没有生成）。
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
                    global::PirateCrew.Core.Log.Warn("[Ambient] SceneArt 根节点未接线，植被风摆跳过（场景仍是静态的）。");
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

            // 【烘焙化后改为深度查找（2026-09-19）】组渲染器可能在烘焙 prefab 实例内部
            //（如 SceneArt/Ship_Galleon_North/SceneArt_Cloth）——Transform.Find 只查直接子节点，
            // 会静默漏绑整船的帆/索具风摆。改为全后代同名匹配，命中几个绑几个（多船各绑各的）。
            Transform top = root.Find(objectName);
            if (top != null)
            {
                int bound = BindRenderer(top, objectName, windMaterial, verbose) ? 1 : 0;
                return bound;
            }

            int count = 0;
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                if (descendants[i].name != objectName)
                    continue;
                if (BindRenderer(descendants[i], objectName, windMaterial, verbose))
                    count++;
            }

            if (count == 0 && verbose)
                global::PirateCrew.Core.Log.Warn("[Ambient] SceneArt 下没找到 " + objectName
                    + "，该组不做风摆（可能该组为空未生成）。");
            return count;
        }

        bool BindRenderer(Transform child, string objectName, Material windMaterial, bool verbose)
        {
            var renderer = child.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                if (verbose)
                    global::PirateCrew.Core.Log.Warn("[Ambient] " + objectName + " 上没有 MeshRenderer，跳过风摆。");
                return false;
            }

            _bound.Add(renderer);
            _original.Add(renderer.sharedMaterial);
            _originalShadows.Add(renderer.shadowCastingMode);

            renderer.sharedMaterial = windMaterial;
            // 写实方向：风摆 shader 已有 ShadowCaster Pass（与本体共享同一份风摆位移），
            // 故显式保留投影 On —— 植被/旗帜投到地面，并由本体 ForwardLit 接收其他物件的投影。
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return true;
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
