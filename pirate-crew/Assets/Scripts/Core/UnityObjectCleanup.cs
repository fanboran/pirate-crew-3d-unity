using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 运行期对象销毁的小工具：按播放态选 Destroy / DestroyImmediate，null 直放过。
    ///
    /// 【为什么收拢】播放态用 DestroyImmediate 会绕开 Unity 的帧末销毁排队（同帧内
    /// 顺序敏感的引用、OnDestroy 回调次序都会被搅乱），而编辑器非播放态（装配脚本 /
    /// 菜单工具链）里 Destroy 要到帧末才生效，同帧后续的重建步骤会撞上还没死的旧对象
    /// ——所以编辑态必须立即销、播放态必须排队销。这套判别原来在 UI 菜单会话与
    /// 像素化相机链各有一份同构拷贝，收拢一处免得长出第三份。
    /// </summary>
    public static class UnityObjectCleanup
    {
        /// <summary>
        /// 播放态用 <see cref="Object.Destroy"/>（帧末生效）、编辑器非播放态用
        /// <see cref="Object.DestroyImmediate"/>（立即生效）；null（含已销毁的
        /// fake-null）直接放过。
        /// </summary>
        public static void DestroySafe(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }
    }
}
