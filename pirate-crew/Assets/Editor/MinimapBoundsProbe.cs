using PirateCrew.Battle;
using PirateCrew.SceneArt;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Text;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 顶视图取景的**包围盒取证**：打开 Battle 场景、按关卡摆出陈设（RuntimeSceneArt），
    /// 把「换平色材质会命中」的 renderer 按世界包围盒对角线排序转储（前 12 名 + 全体统计），
    /// 用于定位把取景撑爆的离群件（远景环带/特效 mesh 一类）。
    /// 无头：-executeMethod PirateCrew.EditorTools.MinimapBoundsProbe.Dump
    /// </summary>
    public static class MinimapBoundsProbe
    {
        static readonly string BattleScenePath = "Assets/Scenes/Game/Battle.unity";

        public static void Dump()
        {
            var sb = new StringBuilder();
            for (int level = 1; level <= 4; level++)
            {
                var scene = EditorSceneManager.OpenScene(BattleScenePath, OpenSceneMode.Single);
                var art = Object.FindObjectOfType<RuntimeSceneArt>(true);
                art.RebuildFor(level);

                var renderers = Object.FindObjectsOfType<Renderer>(false)
                    .Where(r => !(r is ParticleSystemRenderer))
                    .ToArray();
                var diags = renderers
                    .Select(r => (renderer: r, diag: r.bounds.extents.magnitude * 2f))
                    .OrderByDescending(x => x.diag)
                    .ToArray();

                sb.AppendLine($"==== 关{level}：renderer {renderers.Length} 个 ====");
                foreach (var (r, diag) in diags.Take(12))
                {
                    var c = r.bounds.center;
                    sb.AppendLine($"  diag={diag:F1}m  center=({c.x:F1},{c.y:F1},{c.z:F1})  {r.name}  [{r.gameObject.scene.name}]");
                }

                var median = diags[diags.Length / 2].diag;
                sb.AppendLine($"  —— 对角线中位数 {median:F2}m；>中位数×10 的离群 {diags.Count(x => x.diag > median * 10f)} 个");
            }

            string path = "export/minimap-bounds.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[MinimapBoundsProbe] 转储 → " + Path.GetFullPath(path));
        }
    }
}
