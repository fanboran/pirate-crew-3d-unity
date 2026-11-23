using PirateCrew.Core;
using PirateCrew.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateCrew.UI
{
    /// <summary>
    /// 渲染风格的运行时单一真源（设置页写入；渲染 rig 在 Update 里检测真值变化并热切链条）。
    ///
    /// 【为什么轮询而不是事件】真值放 Core（渲染与 UI 程序集共用），但 rig 不能反向引用
    /// UI 程序集的事件类型；rig 每帧比对一个枚举（零分配）即完成跨程序集通知，避免为一条
    /// 低频切换加事件总线。
    ///
    /// 【生效形态】热切换：rig 切换主相机 Renderer 索引 + 启停 Cast 相机，不重载场景
    /// （战斗中切换不丢对局；两套 Renderer 的 feature 天然隔离）。
    ///
    /// 【持久化】与音频/视频/像素比例同纪律：改档即时生效、关设置面板时统一落盘（槽 9 读改写）。
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

        /// <summary>从设置槽读档并写入真值（不通知——rig 初始化时自读真值）。</summary>
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
        /// 改风格（立即生效；**不落盘**——关设置面板时统一落盘）。风格未变返回 false。
        /// </summary>
        public static bool SetStyle(RenderStyle style)
        {
            if (RenderStyleState.Current == style)
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
            // 半路加载的场景里的 rig 自己读真值初始化；此处无需动作（保留钩子给将来需要的场景级重置）。
        }
    }
}
