using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 队伍形状标记的运行时赋图：<see cref="TeamGlyphs"/> 生成的是运行时 Sprite，
    /// 无法序列化进 Prefab（烘进场景会丢成空 sprite → 实心方块）。本件只序列化队伍号，
    /// Awake 时按队伍号重新取形状。
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class TeamMarkIcon : MonoBehaviour
    {
        [SerializeField] int teamIndex;

        /// <summary>设置队伍号并立即赋图（装配期预览用；运行期由 Awake 再赋一次）。</summary>
        public void SetTeam(int index)
        {
            teamIndex = index;
            Apply();
        }

        void Awake()
        {
            Apply();
        }

        void Apply()
        {
            var image = GetComponent<Image>();
            if (image != null)
                image.sprite = TeamGlyphs.Get(teamIndex);
        }
    }
}
