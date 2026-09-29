using NUnit.Framework;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Data;

namespace PirateCrew.Battle.WorldMaps.Tests
{
    /// <summary>
    /// 礁石场档案门禁（无头可跑）：WorldMapCatalog 的每张海图 id 必须在
    /// <see cref="ReefFieldRules"/> 登记显式档案，不许静默落 default 兜底档——
    /// 新增海图忘写 profile 时该图不报错，只会拿到与图语义无关的通用礁石混比
    /// （加重地图设计审计 §二.6 反对的"俯视同质化"），且无人察觉。
    /// 接缝是 <see cref="ReefFieldRules.HasExplicitProfile"/>：与取档逻辑同一个
    /// switch，单一出处、两边不会漂移。
    ///
    /// 【零海图口径】八张世界海图（101–108）已删除待重做、目录当前为空 ⇒ 门禁真空通过
    /// （不再断言 <c>Count &gt; 0</c>：零图是当前的数据现状，不是通道故障）。
    /// 海图重做后本门禁自动恢复，目录里每张图仍必须显式登记档案。
    /// </summary>
    [TestFixture]
    public class WorldMapReefProfileGateTests
    {
        [Test]
        public void AllMaps_HaveExplicitReefProfile()
        {
            foreach (WorldMapDefinition map in WorldMapCatalog.All)
            {
                Assert.IsTrue(ReefFieldRules.HasExplicitProfile(map.Id),
                    map.Id + " 未在 ReefFieldRules 登记显式礁石场档案（运行时会静默落 default 兜底档）"
                    + "——请按图的语义补档案，并同步目录图注。");
            }
        }
    }
}
