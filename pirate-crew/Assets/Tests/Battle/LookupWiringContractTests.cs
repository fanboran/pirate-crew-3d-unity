using System.Reflection;
using NUnit.Framework;
using PirateCrew.Battle;
using PirateCrew.UI;
using PirateCrew.Water;
using UnityEngine;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 「接线清退」的**无头可跑**契约测试：断言三个新的显式注入字段**存在、类型对、且真的可序列化**
    /// （带 <see cref="SerializeFieldAttribute"/> 且非 public）。
    ///
    /// 【为什么必须是无头测试也跑这条】这四处依赖原来是运行时的 <c>FindObjectOfType</c> 兜底，
    /// 改成 <c>[SerializeField]</c> 之后，字段名与"可序列化"就成了一份**跨文件契约**：
    ///   · <c>Assets/Editor/BattleLookupWiring.cs</c> 按字段名写值（<c>SerializedObject.FindProperty</c>）；
    ///   · 被测类按同一个字段名读它。
    /// 任一侧漂移（改字段名、把 <c>[SerializeField]</c> 删掉、把字段改成属性/静态）都不会编译报错，
    /// 只会在 Unity 里**静默不接线**——正是本轨道要消灭的那类故障。反射读类型不实例化任何对象，
    /// 所以这条契约能在无头验证台（不启动 Unity）里守住。
    ///
    /// 【Unity 侧的门禁在别处】"场景里真的连上了"由 PlayMode 的
    /// <c>BattleSceneWiringTests</c>（断言 <c>*WiredByAssembly</c>）收口——那需要真实场景与实例，
    /// 无头环境跑不了（GameObject 实例化会抛 SecurityException）。
    /// </summary>
    [TestFixture]
    public class LookupWiringContractTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>断言字段存在、非 public、类型正确、且带 [SerializeField]（Unity 才会序列化它）。</summary>
        static void AssertSerializedField<TTarget>(string fieldName, string expectedTypeName, string fixHint)
        {
            FieldInfo field = typeof(TTarget).GetField(fieldName, PrivateInstance);
            Assert.IsNotNull(field,
                typeof(TTarget).Name + "." + fieldName + " 字段不存在（字段名漂移？接线工具按名字写值）。" + fixHint);
            Assert.IsFalse(field.IsPublic, typeof(TTarget).Name + "." + fieldName + " 应为私有字段。");
            Assert.IsFalse(field.IsStatic, typeof(TTarget).Name + "." + fieldName + " 不应是静态字段。");
            Assert.AreEqual(expectedTypeName, field.FieldType.Name,
                typeof(TTarget).Name + "." + fieldName + " 类型不符（声明类型变了要同步改接线工具）。");
            Assert.IsTrue(field.IsDefined(typeof(SerializeField), inherit: false),
                typeof(TTarget).Name + "." + fieldName + " 缺 [SerializeField] —— Unity 不会序列化它，"
                + "接线工具写了也存不进场景（静默失效）。" + fixHint);
        }

        static void AssertAssemblyFlag<TTarget>(string propertyName, string fixHint)
        {
            PropertyInfo property = typeof(TTarget).GetProperty(propertyName,
                BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(property,
                typeof(TTarget).Name + "." + propertyName + " 公开属性不存在（PlayMode 装配测试靠它区分"
                + "「装配接线」与「一次性兜底」）。" + fixHint);
            Assert.AreEqual(typeof(bool), property.PropertyType,
                typeof(TTarget).Name + "." + propertyName + " 应为 bool。");
        }

        // ------------------------------------------------------------------
        // BattleCameraDriver（原热路径每帧 FindObjectOfType<AimThrowController>）
        // ------------------------------------------------------------------

        [Test]
        public void BattleCameraDriver_AimThrow_IsSerializedInjectionPoint()
        {
            AssertSerializedField<BattleCameraDriver>("aimThrow", "AimThrowController",
                "修复：PirateCrew.EditorTools.BattleLookupWiring.Wire。");
        }

        [Test]
        public void BattleCameraDriver_ExposesAssemblyWiringFlag()
        {
            AssertAssemblyFlag<BattleCameraDriver>("AimThrowWiredByAssembly",
                "修复：PirateCrew.EditorTools.BattleLookupWiring.Wire。");
        }

        // ------------------------------------------------------------------
        // BattleHud（原 SetHudMode 里按需 FindObjectOfType<BattleCameraDriver>）
        // ------------------------------------------------------------------

        [Test]
        public void BattleHud_CameraController_IsSerializedInjectionPoint()
        {
            AssertSerializedField<BattleHud>("cameraController", "BattleCameraDriver",
                "修复：PirateCrew.EditorTools.BattleLookupWiring.Wire。");
        }

        [Test]
        public void BattleHud_ExposesAssemblyWiringFlag()
        {
            AssertAssemblyFlag<BattleHud>("CameraControllerWiredByAssembly",
                "修复：PirateCrew.EditorTools.BattleLookupWiring.Wire。");
        }

        // ------------------------------------------------------------------
        // WaterSimulationDriver（原"扫全场最亮平行光"）
        // ------------------------------------------------------------------

        [Test]
        public void WaterSimulationDriver_SunLight_IsSerializedInjectionPoint()
        {
            AssertSerializedField<WaterSimulationDriver>("sunLight", "Light",
                "修复：PirateCrew.EditorTools.BattleLookupWiring.Wire。");
        }

        [Test]
        public void WaterSimulationDriver_DroppedFullSceneLightScan()
        {
            // 清退前的实现细节：私有字段 _sunLight + 负缓存 _sunSearchAllowedTime + 扫全场的方法。
            // 它们必须整体消失——留着就意味着"扫全场找平行光"随时可能被再启用。
            Assert.IsNull(typeof(WaterSimulationDriver).GetField("_sunLight", PrivateInstance),
                "WaterSimulationDriver._sunLight 应已随清退删除（改用 [SerializeField] sunLight）。");
            Assert.IsNull(typeof(WaterSimulationDriver).GetField("_sunSearchAllowedTime", PrivateInstance),
                "WaterSimulationDriver._sunSearchAllowedTime（负缓存）应已随清退删除。");
            Assert.IsNull(typeof(WaterSimulationDriver).GetMethod("FindBrightestDirectionalLight",
                    BindingFlags.Static | BindingFlags.NonPublic),
                "WaterSimulationDriver.FindBrightestDirectionalLight（扫全场找最亮平行光）应已删除。");
        }

    }
}
