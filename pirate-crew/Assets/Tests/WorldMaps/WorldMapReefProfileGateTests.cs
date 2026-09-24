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
    /// </summary>
    [TestFixture]
    public class WorldMapReefProfileGateTests
    {
        [Test]
        public void AllMaps_HaveExplicitReefProfile()
        {
            // 目录空 = 数据通道双缺（资产清单读不到且 golden JSON 不可读），
            // foreach 会空转成假绿——先把通道故障钉出来再谈门禁。
            Assert.Greater(WorldMapCatalog.Count, 0,
                "一张海图都没加载到，门禁空转（诊断：" + LevelAssetLibrary.Diagnostic + "）");

            foreach (WorldMapDefinition map in WorldMapCatalog.All)
            {
                Assert.IsTrue(ReefFieldRules.HasExplicitProfile(map.Id),
                    map.Id + " 未在 ReefFieldRules 登记显式礁石场档案（运行时会静默落 default 兜底档）"
                    + "——请按图的语义补档案，并同步目录图注。");
            }
        }
    }
}
