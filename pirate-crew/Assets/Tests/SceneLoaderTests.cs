using NUnit.Framework;
using PirateCrew.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace PirateCrew.Tests
{
    /// <summary>
    /// SceneLoader 纯逻辑部分的轻量测试（栈深度、过渡时长夹紧、空栈容错）。
    /// 协程/UI 过渡部分不在此测试（需要 PlayMode 与真实场景）。
    /// </summary>
    public class SceneLoaderTests
    {
        GameObject _go;
        SceneLoader _loader;

        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
            _go = new GameObject("SceneLoaderUnderTest");
            _loader = _go.AddComponent<SceneLoader>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);

            EventBus.ClearAll();
        }

        [Test]
        public void StackDepth_InitiallyZero()
        {
            Assert.That(_loader.StackDepth, Is.EqualTo(0));
        }

        [Test]
        public void DefaultTransitionDuration_IsPointFourSeconds()
        {
            Assert.That(_loader.TransitionDuration, Is.EqualTo(0.4f).Within(0.0001f));
        }

        [Test]
        public void SetTransitionDuration_ClampsNegativeToZero()
        {
            _loader.SetTransitionDuration(-3f);

            Assert.That(_loader.TransitionDuration, Is.EqualTo(0f));
        }

        [Test]
        public void SetTransitionDuration_AcceptsPositiveValue()
        {
            _loader.SetTransitionDuration(1.25f);

            Assert.That(_loader.TransitionDuration, Is.EqualTo(1.25f).Within(0.0001f));
        }

        [Test]
        public void GoBack_OnEmptyStack_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _loader.GoBack());
        }

        [Test]
        public void ClearStack_OnEmptyStack_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _loader.ClearStack());
            Assert.That(_loader.StackDepth, Is.EqualTo(0));
        }

        [Test]
        public void ChangeScene_WithEmptyName_DoesNotThrow()
        {
            // 空场景名会打一条 error 日志用于诊断，加载流程不应启动
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("场景名为空"));

            Assert.DoesNotThrow(() => _loader.ChangeScene(string.Empty));
            Assert.That(_loader.IsLoading, Is.False);
        }
    }
}
