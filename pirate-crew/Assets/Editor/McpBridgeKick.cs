using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 让 MCP for Unity 的 stdio bridge 在编辑器加载时自动启动，使 AI 工具（ZCode 的 unity-mcp）
    /// 能发现本编辑器实例。
    ///
    /// 【为什么需要这个垫片】MCP for Unity v10 默认走 **stdio** 传输（ZCode 侧的 `mcp-for-unity`
    /// 默认也是 stdio），编辑器侧必须监听本地 socket 才会被 server 发现。负责这件事的是
    /// `StdioBridgeHost.StartAutoConnect()`，但 `StdioBridgeHost` 的静态构造**只在类被首次触达时执行**
    /// ——只打开工程、不打开 MCP 窗口时无人触达它，bridge 就不启动，工具报
    /// `No Unity Editor instances found`。本项目实测吻合：6400 端口无监听、`Assets/UnityMCP/` 日志目录从未生成。
    ///
    /// 【为什么用反射而不是直接引用】`StdioBridgeHost` 是公开类，直接引用也能编译；但本文件是被提交的仓库
    /// 一部分，用反射可在**包被移除/升级改名时优雅降级**（只打一条日志），不会让整个工程编译失败。
    /// 这与玩法代码里 Cinemachine 用强类型是不同取舍——那是硬依赖，这里只是可选的开发工具垫片。
    ///
    /// 【如何停用】删掉本文件即可；也可改在 MCP 窗口（`Window > MCP for Unity`）里手动启动 bridge。
    /// </summary>
    [InitializeOnLoad]
    internal static class McpBridgeKick
    {
        const string HostTypeName = "MCPForUnity.Editor.Services.Transport.Transports.StdioBridgeHost";

        static McpBridgeKick()
        {
            // 等编辑器与包内服务初始化完成再动手（包内 HttpAutoStartHandler 也是这么做的）。
            EditorApplication.delayCall += TryStartBridge;
        }

        [MenuItem("PirateCrew/MCP/启动 stdio Bridge")]
        static void TryStartBridge()
        {
            if (Application.isBatchMode)
                return;   // batchmode 下不监听（与包内行为一致）

            try
            {
                Type hostType = FindType(HostTypeName);
                if (hostType == null)
                {
                    Debug.Log("[McpBridgeKick] 未找到 " + HostTypeName + "（MCP for Unity 包可能未安装），跳过。");
                    return;
                }

                PropertyInfo isRunning = hostType.GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Static);
                if (isRunning != null && isRunning.GetValue(null) is bool running && running)
                {
                    Debug.Log("[McpBridgeKick] stdio Bridge 已在运行，跳过。");
                    return;
                }

                MethodInfo start = hostType.GetMethod("StartAutoConnect", BindingFlags.Public | BindingFlags.Static);
                if (start == null)
                {
                    Debug.LogWarning("[McpBridgeKick] 未找到 StdioBridgeHost.StartAutoConnect，MCP 版本可能已变更，需人工核对。");
                    return;
                }

                start.Invoke(null, null);
                Debug.Log("[McpBridgeKick] 已请求启动 stdio Bridge——AI 工具应可通过 MCP 连接本编辑器。");
            }
            catch (Exception ex)
            {
                // 垫片失败不应影响正常开发，只记录。
                Debug.LogWarning("[McpBridgeKick] 启动 stdio Bridge 失败：" + ex.Message);
            }
        }

        static Type FindType(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = asm.GetType(fullName, false);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
