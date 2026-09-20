using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 世界套件（WorldKit）资产表：资产名 → 预制体引用。
    ///
    /// 【为什么是表不是路径】播放器构建里 FBX 不能按路径运行时加载（非 Resources/Addressables）；
    /// 本表由 Editor 生成器（<c>WorldMapAssetSetBuilder</c>，kit FBX 交付后落地）扫
    /// <c>Assets/Art/Models/WorldKit/&lt;Kit&gt;/*.fbx</c> 填充引用，Battle 场景序列化持有，
    /// 打进包里随 <see cref="WorldMapComposer"/> 使用。
    /// 【缺件纪律】<see cref="WorldMapComposer"/> 对 Get 返回 null 的资产静默跳过（灰盒兜底），
    /// 不抛错——捕图与测试不因美术进度中断。
    /// </summary>
    [CreateAssetMenu(fileName = "WorldMapAssetSet", menuName = "PirateCrew/WorldMap Asset Set")]
    public sealed class WorldMapAssetSet : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string assetName;
            public GameObject prefab;
        }

        [SerializeField] List<Entry> entries = new List<Entry>();
        Dictionary<string, GameObject> _index;

        public GameObject Get(string assetName)
        {
            if (_index == null)
            {
                _index = new Dictionary<string, GameObject>(entries.Count);
                for (int i = 0; i < entries.Count; i++)
                {
                    if (!string.IsNullOrEmpty(entries[i].assetName) && entries[i].prefab != null)
                        _index[entries[i].assetName] = entries[i].prefab;
                }
            }
            return _index.TryGetValue(assetName, out GameObject prefab) ? prefab : null;
        }

        /// <summary>Editor 生成器填充入口（覆盖同名项）。</summary>
        public void Set(string assetName, GameObject prefab)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].assetName == assetName)
                {
                    // List<struct> 索引器返回副本，不能直接写字段（CS1612），整体替换。
                    entries[i] = new Entry { assetName = entries[i].assetName, prefab = prefab };
                    _index = null;
                    return;
                }
            }
            entries.Add(new Entry { assetName = assetName, prefab = prefab });
            _index = null;
        }
    }
}
