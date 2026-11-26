using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>
    /// 水下生物的蒙皮缓冲：全局 <c>_CloudSeaJoints</c>（每个关节三行，行 = 关节矩阵的一行）与开关
    /// <c>_CloudSeaSkinOn</c>，CloudSeaAnim.hlsl 的 CloudSeaSkin 读它们。ShaderLab 名字只在这一处登记。
    ///
    /// 【为什么要有「静止姿态」】声明了 StructuredBuffer 的 shader 在缓冲没绑定的时候读到的是未定义内容，
    /// 而编辑器里预览 prefab、脚本重载之后都没人在驱动生物。所以全局槽位上永远绑着一份合法的缓冲：
    /// 没人在驱动时是 <see cref="BindRest"/> 放的一个单位矩阵关节，开关为 0（shader 直接跳过蒙皮）。
    /// </summary>
    public static class CloudSeaSkin
    {
        public static readonly int JointsId = Shader.PropertyToID("_CloudSeaJoints");
        public static readonly int OnId = Shader.PropertyToID("_CloudSeaSkinOn");

        static ComputeBuffer _rest;

        /// <summary>建一个能放 jointCount 个关节的蒙皮缓冲（每帧整块重写）。</summary>
        public static ComputeBuffer CreateBuffer(int jointCount)
        {
            return new ComputeBuffer(Mathf.Max(1, jointCount) * 3, 16, ComputeBufferType.Default,
                ComputeBufferMode.Dynamic);
        }

        /// <summary>把一个单位矩阵关节绑成全局关节缓冲、关掉蒙皮：没人在驱动生物时用。</summary>
        public static void BindRest()
        {
            Shader.SetGlobalFloat(OnId, 0f);
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;                       // 无图形设备的批处理（-nographics）：没有缓冲可绑
            if (_rest == null)
            {
                _rest = CreateBuffer(1);
                _rest.SetData(new[]
                {
                    new Vector4(1f, 0f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f), new Vector4(0f, 0f, 1f, 0f),
                });
            }
            Shader.SetGlobalBuffer(JointsId, _rest);
        }
    }
}
