using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// 调试树重构建分帧器：首开「部件陈列廊」（345 格 ≈ 1400 物件 + 690 TMP）这类一次性
    /// 吞掉数帧的构建，按压反馈会卡在按下与响应之间（实机走查「点下去一会才有反应」）。
    /// 统一走本件：①整闭包延一帧执行（先落按压态/音效）；②步进式构建（IEnumerable 步源，
    /// 每帧走 N 步）。调试树是静态构建的——本件以隐藏 MonoBehaviour 宿主挂在 _root 下。
    /// </summary>
    public sealed class DebugBuildQueue : MonoBehaviour
    {
        /// <summary>每帧走的构建步数（一步 ≈ 一格/一个家族面板）。</summary>
        const int StepsPerFrame = 24;

        static DebugBuildQueue _instance;

        public static DebugBuildQueue Ensure(Transform parent)
        {
            if (_instance == null)
            {
                var go = new GameObject("DebugBuildQueue", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                _instance = go.AddComponent<DebugBuildQueue>();
            }
            return _instance;
        }

        void OnDestroy()
        {
            // 宿主随场景卸载时反向置空静态槽：Unity fake-null 判空虽能兜住重建，
            // 真置空让「旧实例已死」在销毁当下就成立，不给跨场景悬挂留窗口
            if (_instance == this)
                _instance = null;
        }

        /// <summary>延迟一帧执行整闭包（首开窗：按压反馈先落地）。</summary>
        public void RunNextFrame(System.Action build)
        {
            StartCoroutine(RunNextFrameRoutine(build));
        }

        IEnumerator RunNextFrameRoutine(System.Action build)
        {
            yield return null;
            build();
        }

        /// <summary>步进式跑完构建步源（重页用；<paramref name="onDone"/> 收尾）。</summary>
        public void RunSteps(IEnumerable<object> steps, System.Action onDone = null)
        {
            StartCoroutine(RunStepsRoutine(steps, onDone));
        }

        IEnumerator RunStepsRoutine(IEnumerable<object> steps, System.Action onDone)
        {
            int n = 0;
            foreach (object _ in steps)
            {
                if (++n >= StepsPerFrame)
                {
                    n = 0;
                    yield return null;
                }
            }
            onDone?.Invoke();
        }
    }
}
