using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// 运行时 shader 的**单一取用口**（全仓裸 <c>Shader.Find</c> 只许存在于本类内部）。
    ///
    /// 【为什么要有这个口】裸 <c>Shader.Find(名字)</c> 是静默失效点：播放器构建只收
    /// "被资产引用链摸到"的 shader，只在 C# 里按名查找的 shader 会被剥离——运行时返回
    /// null，画面缺一块却不报任何错（实现口径.md §6 静默失效点 1）。
    /// 处置分两层：
    ///   · **本工程 shader 由 Resources 持有**——shader 文件搬进
    ///     <c>Assets/Resources/PixelartShaders/</c>（文件名 = shader 内部名末段），
    ///     Resources 资产必然入构建包，引用链摸得到；运行时走 <see cref="Load"/> 按路径取。
    ///   · **取用失败必须响**——Resources 摸不到时先按内部名 <c>Shader.Find</c> 回退
    ///     （双保险，尽力不让画面断），两条路都落空则 <see cref="global::PirateCrew.Core.Log.Error"/>
    ///     响亮报错，绝不让"shader 丢了"退化成无声的黑块/洋红。
    ///
    /// 【两个入口怎么选】
    ///   · <see cref="Load"/>：持有于 Resources 的本工程 shader（Pixelart 四件套等）。
    ///   · <see cref="Find"/>：**不**持有于 Resources 的 shader——引擎/URP 内置
    ///     （<c>Hidden/Universal/CoreBlit</c>、<c>Sprites/Default</c>、URP Unlit 等，
    ///     是包资产或引擎常驻，塞不进也塞不该进 Resources），以及由 ArtGate ⓪
    ///     登记进 Always Included Shaders 保障入包的本工程 shader
    ///     （Fx/Additive、Fx/Alpha、PirateGradientSky——文件在 Assets/Art/Shaders/，
    ///     走 Load 会对"不在 Resources"误报）。
    ///
    /// 【报错去重】同一 shader 名只报一次（静态去重表），避免每帧/每材质创建刷屏；
    /// 调用方对返回 null 的业务后果仍应各自报错（如"材质 X 未创建"）。
    /// </summary>
    public static class PixelartShaders
    {
        /// <summary>Resources 下的 shader 持有目录（相对 Resources 根；目录里文件名 = 内部名末段）。</summary>
        public const string ResourceFolder = "PixelartShaders";

        /// <summary>已响亮报过错的 shader 名（每名只报一次，不刷屏）。</summary>
        static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>
        /// 取持有于 Resources 的本工程 shader：主路径 <c>Resources.Load</c>，
        /// 未命中时按内部名 <see cref="Find"/> 回退（双保险），两路都落空则响亮报错返回 null。
        /// </summary>
        public static Shader Load(string shaderName)
        {
            // 约定：Resources/PixelartShaders/ 下文件名 = shader 内部名末段
            //（"PirateCrew/Pixelart/PixelartObject" → "PixelartShaders/PixelartObject"）。
            string resourcePath = ResourceFolder + "/" + FileNameOf(shaderName);
            Shader shader = Resources.Load<Shader>(resourcePath);
            if (shader != null)
                return shader;

            Shader fallback = Find(shaderName);
            if (fallback != null)
            {
                ErrorOnce(shaderName,
                    "Resources/" + resourcePath + " 摸不到（资产摆放或打包异常？），"
                    + "已按内部名 Shader.Find 回退命中——请核查 shader 文件是否真的在"
                    + " Assets/Resources/" + ResourceFolder + "/ 下。");
            }
            return fallback;
        }

        /// <summary>
        /// 按内部名取**不**持有于 Resources 的 shader（引擎/URP 内置、或由 ArtGate
        /// Always Included 保障入包的本工程 shader）。找不到时响亮报错并返回 null。
        /// </summary>
        public static Shader Find(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                ErrorOnce(shaderName,
                    "Shader.Find 摸不到（被构建剥离/未编译/名字拼错？）。"
                    + "内置 shader 请核对 URP 包版本；本工程 shader 请核对 ArtGate ⓪"
                    + " 的 Always Included 登记。");
            }
            return shader;
        }

        /// <summary>shader 内部名末段（"PirateCrew/Pixelart/PixelartObject" → "PixelartObject"）。</summary>
        static string FileNameOf(string shaderName)
        {
            int lastSlash = shaderName.LastIndexOf('/');
            return lastSlash >= 0 ? shaderName.Substring(lastSlash + 1) : shaderName;
        }

        /// <summary>同一 shader 只响一次；Error 直通播放器日志（Core/Log 的发布收口约定）。</summary>
        static void ErrorOnce(string shaderName, string reason)
        {
            if (!Reported.Add(shaderName))
                return;
            global::PirateCrew.Core.Log.Error("[PixelartShaders] shader \"" + shaderName + "\"：" + reason);
        }
    }
}
