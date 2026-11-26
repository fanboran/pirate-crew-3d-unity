using PirateCrew.Core;
using PirateCrew.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateCrew.UI
{
    /// <summary>
    /// 渲染风格的运行时单一真源。本仓只保留 PostLine：<see cref="RenderStyleState.Current"/> 恒为
    /// PostLine，本类只剩「把风格键写进设置槽」一件事，不再提供切换入口。
    ///
    /// 【为什么还留着】风格键与音频/视频/像素比例共用设置槽 9（读改写，互不覆盖）。设置页已删掉
    /// 「渲染风格」行；这里只负责关面板落盘时把同槽的其余键原样带过去。随 CellShade 线的代码
    /// 退役，本类与 <see cref="RenderStyleStore"/> 一并删除。
    ///
    /// 【持久化】与音频/视频/像素比例同纪律：关设置面板时统一落盘（槽 9 读改写）。
    /// </summary>
    public static class RenderStyleService
    {
        /// <summary>当前风格（真源在 <see cref="RenderStyleState"/>，此处只做读取镜像）。</summary>
        public static RenderStyle Style { get { return RenderStyleState.Current; } }

        /// <summary>运行期安装（播放器与 PlayMode 自动触发；重复安装安全）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;   // 域重载/PlayMode 重入防重复订阅
            SceneManager.sceneLoaded += OnSceneLoaded;
            LoadFromSave();
        }

        /// <summary>从设置槽读档并写入真值（旧档里的退役风格值读出来也是 PostLine）。</summary>
        public static void LoadFromSave()
        {
            SaveManager save = SaveManager.Instance;
            if (save == null || !save.SlotExists(RenderStyleStore.SettingsSlot))
                return;

            SaveData data = save.LoadFromSlot(RenderStyleStore.SettingsSlot);
            RenderStyle style = RenderStyleState.Current;
            if (RenderStyleStore.TryReadFrom(data, ref style))
                RenderStyleState.Current = style;
        }

        /// <summary>
        /// 改风格。本仓只保留 PostLine：传入退役的 CellShade 不生效并返回 false；
        /// 传入 PostLine 而当前已是 PostLine 时同样返回 false。
        /// </summary>
        public static bool SetStyle(RenderStyle style)
        {
            if (style != RenderStyle.PostLine || RenderStyleState.Current == style)
                return false;

            RenderStyleState.Current = style;
            return true;
        }

        /// <summary>落盘（槽 9 读改写，保留同槽其它键）。返回是否写入成功。</summary>
        public static bool Save()
        {
            SaveManager save = SaveManager.Instance;
            if (save == null)
                return false;

            SaveData data = save.SlotExists(RenderStyleStore.SettingsSlot)
                ? save.LoadFromSlot(RenderStyleStore.SettingsSlot)
                : null;
            if (data == null)
                data = new SaveData();

            RenderStyleStore.WriteTo(data, RenderStyleState.Current);
            return save.SaveToSlot(RenderStyleStore.SettingsSlot, data, RenderStyleStore.SettingsDisplayName);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 没有场景级的风格动作；保留钩子给将来需要的场景级设置重置。
        }
    }
}
