using NUnit.Framework;
using PirateCrew.Core;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 服务注册表（<see cref="Services"/>）测试：登记/取用/缺失语义/覆盖/注销/清空。
    ///
    /// 【注意】<see cref="Services.Get{T}"/> 的"未登记"分支会经 <c>Log.Error</c> 报错，
    /// 而无头验证台上 <c>UnityEngine.Debug.LogError</c> 是原生调用（<c>SecurityException</c>），
    /// 所以本文件对"缺失"只断言 <see cref="Services.TryGet{T}"/> 路径——
    /// 那是**可选依赖**的正规取法（"必须存在"的 <c>Get</c> 由 Unity batchmode 侧收口）。
    /// </summary>
    public class ServicesTests
    {
        class FakeSceneLoader
        {
        }

        sealed class FakeSaveManager
        {
        }

        [SetUp]
        public void SetUp()
        {
            Services.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Services.Clear();
        }

        [Test]
        public void RegisterThenTryGet_ReturnsSameInstance()
        {
            var loader = new FakeSceneLoader();

            Services.Register(loader);

            Assert.That(Services.TryGet<FakeSceneLoader>(out FakeSceneLoader found), Is.True);
            Assert.That(found, Is.SameAs(loader));
        }

        [Test]
        public void Get_AfterRegister_ReturnsSameInstance()
        {
            var save = new FakeSaveManager();
            Services.Register(save);

            Assert.That(Services.Get<FakeSaveManager>(), Is.SameAs(save));
        }

        [Test]
        public void TryGet_WhenMissing_ReturnsFalseAndNull()
        {
            Assert.That(Services.TryGet<FakeSceneLoader>(out FakeSceneLoader found), Is.False);
            Assert.That(found, Is.Null);
            Assert.That(Services.Count, Is.EqualTo(0));
        }

        [Test]
        public void KeysAreDistinctPerConcreteType()
        {
            var loader = new FakeSceneLoader();
            var save = new FakeSaveManager();

            Services.Register(loader);
            Services.Register(save);

            Assert.That(Services.Count, Is.EqualTo(2));
            Assert.That(Services.Get<FakeSceneLoader>(), Is.SameAs(loader));
            Assert.That(Services.Get<FakeSaveManager>(), Is.SameAs(save));
        }

        [Test]
        public void Register_SameTypeTwice_KeepsLatest()
        {
            // 组合根幂等：第二次装配（例如场景里的 Bootstrapper 与启动装配都跑过）覆盖旧实例，
            // 不抛异常（覆盖不同实例会打告警，见 Services.Register 注释）。
            var first = new FakeSceneLoader();
            var second = new FakeSceneLoader();

            Services.Register(first);
            Services.Register(second);

            Assert.That(Services.Count, Is.EqualTo(1));
            Assert.That(Services.Get<FakeSceneLoader>(), Is.SameAs(second));
        }

        [Test]
        public void Register_Null_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => Services.Register<FakeSceneLoader>(null));
        }

        [Test]
        public void Unregister_RemovesOnlyThatType()
        {
            Services.Register(new FakeSceneLoader());
            Services.Register(new FakeSaveManager());

            Services.Unregister<FakeSceneLoader>();

            Assert.That(Services.TryGet<FakeSceneLoader>(out _), Is.False);
            Assert.That(Services.TryGet<FakeSaveManager>(out _), Is.True);
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            Services.Register(new FakeSceneLoader());
            Services.Register(new FakeSaveManager());

            Services.Clear();

            Assert.That(Services.Count, Is.EqualTo(0));
            Assert.That(Services.TryGet<FakeSceneLoader>(out _), Is.False);
            Assert.That(Services.RegisteredTypes, Is.Empty);
        }
    }
}
