using System;
using System.IO;
using PirateCrew.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 菜单/管理屏的**烘焙装配助手**：三张屏与 Battle HUD（<c>BattleHudBuilder</c>）同款形态——
    /// <see cref="UiScreenBuilder"/> 的构建代码在编辑器期跑，整树落成
    /// <c>Assets/Prefabs/UI/*Screen.prefab</c>，场景只留实例（相机 + EventSystem + 1 个 Prefab 实例），
    /// 打开场景即见 UI（Scene/Game 窗口直接预览）。
    ///
    /// 【为什么是烘焙（创始人 2026-10-26 裁决）】10-03 的 UI 运行时化（e001e132）为高频改版期
    /// 省"每轮重烘"，代价是编辑态全空——WYSIWYG 断裂。现菜单 UI 已稳定，编辑期可见性优先。
    /// 构建代码仍是唯一真相源（<see cref="UiScreenBuilder"/> 不动），prefab 是派生产物：
    /// 改 UI 后重跑生成器（ArtGate ⑨⑩ / 场景菜单）即重烘，不会出现"改了代码忘重烘"之外的第二真相。
    ///
    /// 【缩放档】画布出厂档 = <see cref="PixelSkin.Unit"/>（烘焙落盘值）；运行期由
    /// <see cref="PixelScaleService"/> 的 sceneLoaded 钩子按当前档重写，不依赖本烘焙值。
    /// </summary>
    static class MenuScreenPrefab
    {
        internal const string PrefabFolder = "Assets/Prefabs/UI";

        /// <summary>画布工厂（原 UiScreenBoot.CreateCanvas 原值）：恒定像素密度出厂档。</summary>
        internal static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            // 【恒定像素密度（红警2 式）】出厂档 = PixelSkin.Unit；运行期由 PixelScaleService 重写。
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = PixelSkin.Unit;

            return canvas;
        }

        /// <summary>
        /// 控制器挂画布下（原 UiScreenBoot.AttachController 原值）：非激活注入 refs 后激活。
        /// 编辑态 Awake 不触发，序列化值经 <see cref="SaveAsScreenPrefab"/> 落进 prefab，
        /// 运行期场景加载时字段已齐——与原装配器按字段名回写的时序等价。
        /// </summary>
        internal static T AttachController<T>(Transform canvas, string name, Action<T> bind)
            where T : MonoBehaviour
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            go.SetActive(false);
            T controller = go.AddComponent<T>();
            bind?.Invoke(controller);
            go.SetActive(true);
            return controller;
        }

        /// <summary>画布整树存为 Prefab 并把场景对象连为实例（菜单屏的折叠终态）。</summary>
        internal static void SaveAsScreenPrefab(GameObject canvasRoot, string prefabName)
        {
            EnsureFolder(PrefabFolder);
            PrefabUtility.SaveAsPrefabAssetAndConnect(
                canvasRoot, PrefabFolder + "/" + prefabName + ".prefab", InteractionMode.AutomatedAction);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
