using NUnit.Framework;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 最小冒烟测试：验证测试程序集能编译、被 UTF 发现并执行。
    /// PirateCrew.Runtime 目前无代码，故不做类型探测。
    /// </summary>
    public class SmokeTests
    {
        [Test]
        public void RuntimeAssemblyLoads()
        {
            Assert.That(typeof(object), Is.Not.Null);
        }
    }
}
