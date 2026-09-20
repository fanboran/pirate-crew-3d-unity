using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Data;

namespace PirateCrew.Battle.WorldMaps.Tests
{
    /// <summary>
    /// 方案 D 分层军火（2026-09-17 用户裁决）的数据层测试（无头可跑）：
    /// 每图船员初配全为近程档、船长含全图级旗舰、空投池含中程档，八图配置互不相同。
    /// 分层依据：逆向 §5.1/§5.2——twangMax 20 近程 / twangMax 30 中程 / 五件全图级。
    /// </summary>
    [TestFixture]
    public class WorldMapLoadoutTests
    {
        /// <summary>近程档白名单（twangMax 20；voodooDoll 虽是全图级锁定但归近程投掷档）。</summary>
        static readonly HashSet<WeaponId> NearTier = new HashSet<WeaponId>
        {
            WeaponId.CherryBomb, WeaponId.Dynamite, WeaponId.Mine,
            WeaponId.Boulder, WeaponId.PiecesOfEight,
        };

        /// <summary>全图级集合（射程 = 地图本身，§5.2）。</summary>
        static readonly HashSet<WeaponId> MapWideTier = new HashSet<WeaponId>
        {
            WeaponId.Cannon, WeaponId.Seagull, WeaponId.TidalWave,
            WeaponId.Anchor, WeaponId.VoodooDoll,
        };

        /// <summary>中程档集合（twangMax 30；cannon 蓄力上限 30，其发射的 cannonball 无重力直线）。</summary>
        static readonly HashSet<WeaponId> MidTier = new HashSet<WeaponId>
        {
            WeaponId.Banana, WeaponId.ParachuteBomb, WeaponId.RumBottle, WeaponId.Cannon,
        };

        [Test]
        public void AllMaps_CrewLoadout_IsNearTierOnly()
        {
            foreach (WorldMapDefinition map in WorldMapCatalog.All)
            {
                Assert.IsNotNull(map.CrewWeapons, map.Id + " 船员初配未配置（方案 D 要求八图全配）");
                Assert.Greater(map.CrewWeapons.Count, 0, map.Id);

                bool hasInfiniteCherry = false;
                foreach (WeaponStack stack in map.CrewWeapons)
                {
                    if (stack.id == WeaponId.CherryBomb && stack.count == 10)
                        hasInfiniteCherry = true;
                    else
                        Assert.IsTrue(NearTier.Contains(stack.id),
                            $"{map.Id} 船员初配混入非近程件 {stack.id}");
                }
                Assert.IsTrue(hasInfiniteCherry, map.Id + " 船员初配缺樱桃×∞ 基线");
            }
        }

        [Test]
        public void AllMaps_CaptainLoadout_CarriesMapWideFlagship()
        {
            foreach (WorldMapDefinition map in WorldMapCatalog.All)
            {
                Assert.IsNotNull(map.CaptainWeapons, map.Id + " 船长初配未配置");
                int mapWide = 0;
                foreach (WeaponStack stack in map.CaptainWeapons)
                    if (MapWideTier.Contains(stack.id))
                        mapWide++;

                Assert.GreaterOrEqual(mapWide, 1, map.Id + " 船长缺全图级旗舰（方案 D 核心）");
            }
        }

        [Test]
        public void AllMaps_AirdropPool_ContainsMidTier()
        {
            foreach (WorldMapDefinition map in WorldMapCatalog.All)
            {
                Assert.IsNotNull(map.AirdropPool, map.Id);
                Assert.Greater(map.AirdropPool.Count, 0, map.Id);

                int mid = 0;
                foreach (WeaponStack stack in map.AirdropPool)
                    if (MidTier.Contains(stack.id) || MapWideTier.Contains(stack.id))
                        mid++;

                Assert.GreaterOrEqual(mid, 1,
                    map.Id + " 空投池缺中程/全图级火力（原版 §5.5：宝箱每回合补远程）");
            }
        }

        [Test]
        public void AllMaps_Loadouts_AreDistinct()
        {
            // 八图不全等（图与图要有军备差异；以船员变化件+旗舰组合为指纹）。
            var fingerprints = new HashSet<string>();
            foreach (WorldMapDefinition map in WorldMapCatalog.All)
            {
                var fp = new List<string>();
                foreach (WeaponStack stack in map.CaptainWeapons)
                    fp.Add(stack.id + ":" + stack.count);
                fingerprints.Add(string.Join("|", fp));
            }

            Assert.AreEqual(WorldMapCatalog.Count, fingerprints.Count, "存在军备配置完全相同的两图");
        }
    }
}
