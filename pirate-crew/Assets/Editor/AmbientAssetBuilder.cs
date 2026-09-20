using System.Collections.Generic;
using PirateCrew.Ambient;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 【环境与活物 · 可选资产落盘器】把 <c>Assets/Scripts/PirateCrew/Ambient/AmbientMeshFactory.cs</c>
    /// 程序化生成的网格与材质存成 Unity 资产，供美术检查 / 调参 / 将来做预设预制体。
    ///
    /// 【它不是运行时的必需品】<see cref="AmbientDirector"/> 在运行时用同一套工厂自建网格与材质，
    /// 所以**不跑这个脚本，场景里也有活物**。本脚本的价值是：
    ///   ① 让几何/材质成为可视化资产（可在 Inspector 里看三角形、点开材质调风参数）；
    ///   ② 把 4 个风摆材质存成资产，协调者可以填进 <see cref="AmbientDirector"/> 的
    ///      <c>windFoliageMaterial</c> 等覆盖字段，让美术直接改材质而不用改代码；
    ///   ③ 离线打印三角面清单（性能自证）。
    ///
    /// 【执行方式】
    ///   <c>-executeMethod PirateCrew.EditorTools.AmbientAssetBuilder.BuildAll</c>
    ///   或菜单 <c>PirateCrew/环境活物/生成活物资产</c>。
    ///
    /// 【不碰场景】本脚本不修改 <c>Assets/Scenes/**</c>、不挂组件 —— 场景接线由协调者统一做
    /// （接线清单见本模块交付报告）。
    /// </summary>
    public static class AmbientAssetBuilder
    {
        const string MeshFolder = "Assets/Art/Models/Ambient";
        const string MaterialFolder = "Assets/Art/Materials/Ambient";
        const string ShaderFolder = "Assets/Art/Shaders/Ambient";

        [MenuItem("PirateCrew/环境活物/生成活物资产")]
        public static void BuildAll()
        {
            EnsureFolder(MeshFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(ShaderFolder);

            int meshCount = BuildMeshes(out int totalTriangles);
            int materialCount = BuildMaterials(out int missingShaders);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[AmbientAssetBuilder] 环境活物资产生成完成。\n"
                + "  网格: " + meshCount + " 个（" + MeshFolder + "），三角面合计 " + totalTriangles + "\n"
                + "  材质: " + materialCount + " 个（" + MaterialFolder + "）\n"
                + "  缺失 shader: " + missingShaders + " 个"
                + (missingShaders > 0
                    ? " —— 请在有渲染路径的编辑器里 read_console 确认 PirateCrew/Ambient/Wind 与 /Glow 无编译错误。"
                    : string.Empty) + "\n"
                + "  ⚠ 场景接线（把 AmbientDirector 挂到 SceneArt 根节点并填字段）由协调者执行，本脚本不改场景。");
        }

        // ------------------------------------------------------------------
        // 网格
        // ------------------------------------------------------------------

        static int BuildMeshes(out int totalTriangles)
        {
            var meshes = new AmbientMeshSet();
            totalTriangles = 0;

            var entries = new List<KeyValuePair<string, Mesh>>
            {
                new KeyValuePair<string, Mesh>("Ambient_GullBody", meshes.GullBody),
                new KeyValuePair<string, Mesh>("Ambient_GullWing", meshes.GullWing),
                new KeyValuePair<string, Mesh>("Ambient_CrabBody", meshes.CrabBody),
                new KeyValuePair<string, Mesh>("Ambient_CrabClaw", meshes.CrabClaw),
                new KeyValuePair<string, Mesh>("Ambient_Fish", meshes.Fish),
                new KeyValuePair<string, Mesh>("Ambient_LanternFrame", meshes.LanternFrame),
                new KeyValuePair<string, Mesh>("Ambient_LanternCore", meshes.LanternCore),
                new KeyValuePair<string, Mesh>("Ambient_Pennant", meshes.Pennant),
                new KeyValuePair<string, Mesh>("Ambient_Post", meshes.Post),
                new KeyValuePair<string, Mesh>("Ambient_CorkFloat", meshes.CorkFloat),
                new KeyValuePair<string, Mesh>("Ambient_DistantBird", meshes.DistantBird),
                new KeyValuePair<string, Mesh>("Ambient_GlowQuad", meshes.GlowQuad),
            };

            for (int i = 0; i < entries.Count; i++)
            {
                string name = entries[i].Key;
                Mesh mesh = entries[i].Value;
                if (mesh == null)
                    continue;

                totalTriangles += (int)(mesh.GetIndexCount(0) / 3);
                SaveMesh(name, mesh);
            }

            return entries.Count;
        }

        static void SaveMesh(string name, Mesh mesh)
        {
            string path = MeshFolder + "/" + name + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing == null)
            {
                mesh.name = name;
                AssetDatabase.CreateAsset(mesh, path);
                return;
            }

            // 就地覆盖顶点，保持资产 GUID 不变（场景/预制体引用不会断）。
            existing.Clear(false);
            existing.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            existing.SetVertices(new List<Vector3>(mesh.vertices));
            existing.SetNormals(new List<Vector3>(mesh.normals));
            existing.SetTriangles(mesh.triangles, 0, true);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);

            Object.DestroyImmediate(mesh);
        }

        // ------------------------------------------------------------------
        // 材质
        // ------------------------------------------------------------------

        static int BuildMaterials(out int missingShaders)
        {
            var materials = new AmbientMaterialSet();
            missingShaders = materials.HasMissingShaders ? 1 : 0;

            var entries = new List<KeyValuePair<string, Material>>
            {
                new KeyValuePair<string, Material>("Ambient_GullBody", materials.GullBody),
                new KeyValuePair<string, Material>("Ambient_GullWing", materials.GullWing),
                new KeyValuePair<string, Material>("Ambient_CrabShell", materials.CrabShell),
                new KeyValuePair<string, Material>("Ambient_CrabClaw", materials.CrabClaw),
                new KeyValuePair<string, Material>("Ambient_Fish", materials.Fish),
                new KeyValuePair<string, Material>("Ambient_LanternMetal", materials.LanternMetal),
                new KeyValuePair<string, Material>("Ambient_LanternCore", materials.LanternCore),
                new KeyValuePair<string, Material>("Ambient_LanternGlow", materials.LanternGlow),
                new KeyValuePair<string, Material>("Ambient_DistantBird", materials.DistantBird),
                new KeyValuePair<string, Material>("Ambient_Post", materials.Post),
                new KeyValuePair<string, Material>("Ambient_CorkFloat", materials.CorkFloat),
                // 4 个风摆材质：可填进 AmbientDirector 的覆盖字段，让美术直接调风参数。
                new KeyValuePair<string, Material>("Ambient_WindFoliage", materials.WindFoliage),
                new KeyValuePair<string, Material>("Ambient_WindFlagRed", materials.WindFlagRed),
                new KeyValuePair<string, Material>("Ambient_WindFlagBlue", materials.WindFlagBlue),
                new KeyValuePair<string, Material>("Ambient_WindCloth", materials.WindCloth),
            };

            for (int i = 0; i < entries.Count; i++)
            {
                SaveMaterial(entries[i].Key, entries[i].Value);
            }

            return entries.Count;
        }

        static void SaveMaterial(string name, Material material)
        {
            if (material == null)
                return;

            string path = MaterialFolder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing == null)
            {
                material.name = name;
                AssetDatabase.CreateAsset(material, path);
                return;
            }

            // 就地复制 shader 与属性，保持资产 GUID 不变。
            material.name = name;
            EditorUtility.CopySerialized(material, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(material);
        }

        // ------------------------------------------------------------------
        // 文件夹
        // ------------------------------------------------------------------

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
