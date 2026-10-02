using PirateCrew.Core;
using PirateCrew.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 像素比例档的运行时单一真源（UI 画布密度与像素化渲染 rig 同档联动）。
    ///
    /// 【与 PixelSkin.Unit 的分工】PixelSkin.Unit = 2 是烘焙期出厂值（装配器写进场景 CanvasScaler）；
    /// 本类是运行期真值——启动读档覆盖出厂值、设置页改档即时重应用、切场景对半路加载的画布重应用。
    ///
    /// 【应用范围】画布侧直接遍历 <see cref="CanvasScaler"/> 写 scaleFactor（场景里的画布都是
    /// 恒定像素密度栈，逐台覆盖即全量）；渲染侧只写 <see cref="PixelScaleState.Unit"/>，
    /// rig 在自己的 Update 里自适应（避免本类反向依赖渲染程序集的细节）。
    ///
    /// 【无场景对象】静态类 + <see cref="RuntimeInitializeOnLoadMethod"/> 安装（订阅 sceneLoaded），
    /// 不往 Bootstrapper 场景加东西；编辑器域（EditMode 测试/装配）不触发安装，装配链行为不变。
    /// </summary>
    public static class PixelScaleService
    {
        /// <summary>当前档（{自动, 2, 3, 4}，语义见 <see cref="PixelScaleStore"/>）。</summary>
        public static int ScaleIndex { get; private set; } = PixelScaleStore.ScaleDefault;

        /// <summary>当前档解析出的整数倍（已应用到画布与真值的值）。</summary>
        public static int AppliedUnit { get; private set; } = PixelScaleStore.ScaleDefault;

        /// <summary>档被应用后的通知（测试/特殊画布订阅用；常规画布无需订阅，ApplyAll 全量覆盖）。</summary>
        public static event System.Action<int> Applied;

        /// <summary>运行期安装（播放器与 PlayMode 自动触发；重复安装安全）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;   // 域重载/PlayMode 重入防重复订阅
            SceneManager.sceneLoaded += OnSceneLoaded;
            LoadFromSave();
            ApplyAll();
        }

        /// <summary>从设置槽读档（不应用——启动路径由 Install 统一应用）。</summary>
        public static void LoadFromSave()
        {
            SaveManager save = SaveManager.Instance;
            if (save == null || !save.SlotExists(PixelScaleStore.SettingsSlot))
                return;

            SaveData data = save.LoadFromSlot(PixelScaleStore.SettingsSlot);
            int index = ScaleIndex;
            if (PixelScaleStore.TryReadFrom(data, ref index))
                ScaleIndex = index;
        }

        /// <summary>
        /// 改档（立即重应用；**不落盘**——与音量/画质同纪律，关设置面板时统一落盘）。
        /// 未知档忽略并返回 false。
        /// </summary>
        public static bool SetScale(int scaleIndex)
        {
            if (scaleIndex != PixelScaleStore.ScaleAuto
                && (scaleIndex < PixelScaleStore.ScaleMin || scaleIndex > PixelScaleStore.ScaleMax))
            {
                return false;
            }

            if (ScaleIndex == scaleIndex)
                return true;

            ScaleIndex = scaleIndex;
            ApplyAll();
            return true;
        }

        /// <summary>落盘（槽 9 读改写，保留同槽其它键）。返回是否写入成功。</summary>
        public static bool Save()
        {
            SaveManager save = SaveManager.Instance;
            if (save == null)
                return false;

            SaveData data = save.SlotExists(PixelScaleStore.SettingsSlot)
                ? save.LoadFromSlot(PixelScaleStore.SettingsSlot)
                : null;
            if (data == null)
                data = new SaveData();

            PixelScaleStore.WriteTo(data, ScaleIndex);
            return save.SaveToSlot(PixelScaleStore.SettingsSlot, data, PixelScaleStore.SettingsDisplayName);
        }

        /// <summary>
        /// 全量重应用：解析整数倍 → 真值 → 全部画布。切场景后新加载的画布靠 sceneLoaded 重放本方法。
        /// </summary>
        public static void ApplyAll()
        {
            AppliedUnit = PixelScaleStore.ResolveUnit(ScaleIndex, Screen.height);
            PixelScaleState.Unit = AppliedUnit;

            foreach (CanvasScaler scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None))
                scaler.scaleFactor = AppliedUnit;

            Applied?.Invoke(AppliedUnit);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 半路场景（Battle 等）的画布带着烘焙出厂档进来，按当前档重写。
            ApplyAll();
        }
    }
}
