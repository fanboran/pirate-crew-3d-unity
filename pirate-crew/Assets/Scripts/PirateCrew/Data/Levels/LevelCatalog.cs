using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Data
{
    /// <summary>
    /// 关卡资产的**唯一加载清单**：Unity 侧靠它把全部关卡资产带进构建包。
    ///
    /// 【为什么需要清单而不是路径扫描】播放器构建里 <c>Assets/</c> 下的资产不能按路径加载
    /// （非 Resources / 非 Addressables）。清单持有直接引用，Unity 会把被引用的资产一并打进包；
    /// 自身放 <c>Assets/Data/Levels/Resources/LevelCatalog.asset</c>，由
    /// <see cref="LevelAssetLibrary"/> 用 <see cref="Resources.Load{T}(string)"/> 取到。
    ///
    /// 【新增一张关卡】把新资产拖进本清单（或跑迁移器，它会重写清单）。
    /// 漏登记 = 运行时加载不到该关，由 <c>LevelAssetValidator</c> 的内容门禁报红。
    /// </summary>
    [CreateAssetMenu(menuName = "PirateCrew/Data/关卡资源清单", fileName = "LevelCatalog")]
    public sealed class LevelCatalog : ScriptableObject
    {
        [SerializeField, Tooltip("关卡资产（按关卡号升序）。")]
        List<LevelDefinition> levels = new List<LevelDefinition>();

        /// <summary>关卡资产（可能含 null 空洞——由校验器报）。</summary>
        public IReadOnlyList<LevelDefinition> Levels => levels;

        /// <summary>迁移器重写清单入口（覆盖，不追加——避免重复登记同一关）。</summary>
        public void Apply(List<LevelDefinition> levelAssets)
        {
            levels = levelAssets ?? new List<LevelDefinition>();
        }
    }
}
