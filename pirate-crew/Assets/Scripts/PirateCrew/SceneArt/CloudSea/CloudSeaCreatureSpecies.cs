using System.Collections.Generic;
using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>水下生物的行为族：<see cref="CloudSeaLife"/> 按它决定每帧怎么挪这只生物的**对象**。</summary>
    public enum CloudSeaCreatureKind
    {
        /// <summary>不是生物，或物种名不在表里。</summary>
        Unknown,

        /// <summary>鱼群：一群一个对象，绕锚点巡游，群头对准速度方向。</summary>
        School,

        /// <summary>大型游泳者：绕锚点巡游，转弯侧倾、升降俯仰。</summary>
        Swimmer,

        /// <summary>漂浮：水平慢漂、缓缓自转；升沉由运动学控制器推出来。</summary>
        Jelly,

        /// <summary>
        /// 贴底：蟹、海星、章鱼、海马、海葵。对象不动，整只的位移在骨架里做（脚要踩在世界里的锚点上）。
        /// </summary>
        Bottom,
    }

    /// <summary>
    /// 云端漫步水下生物的**物种表**（纯逻辑，无头可测）：对象名 → 物种 → 行为族与巡游参数。
    ///
    /// 【对象名契约】<c>Cloud_Creature_&lt;物种&gt;_&lt;序号&gt;</c>（前缀在 <see cref="CloudSeaPath"/>），
    /// 由 Blender 侧 mod_creatures.py 写出。物种名是两边对齐的关键：Blender 出了表里没有的物种，
    /// 运行时那只生物停在静止姿态（并报错），不静默当成别的东西。
    ///
    /// 【分工】本表只管「整只的对象往哪挪」（鱼群 / 游泳者 / 水母的水平路径）；肢体的动画在运动学
    /// 控制器里（<see cref="CloudSeaBrain"/>，按物种名分派），贴底生物连整只的位移也在那里。
    ///
    /// 【朝向】生物建模面朝本地 +X。<see cref="HeadingOf"/> 给出让本地 +X 指向某个水平方向所需的 Y 轴旋转角。
    /// </summary>
    public static class CloudSeaCreatureSpecies
    {
        /// <summary>
        /// 一个物种的巡游参数。长度单位都是米；伸出量相对对象原点，用来把整只生物夹在
        /// 「脚下沙丘 + 净空」与「水面 − 余量」之间。
        /// </summary>
        public struct Profile
        {
            public readonly CloudSeaCreatureKind Kind;

            /// <summary>巡游线速度（m/s）：鱼群 / 游泳者沿巡游圈。贴底生物不用（它们的步态自己出速度）。</summary>
            public readonly float Speed;

            /// <summary>巡游圈半径（m）；贴底生物 = 它在骨架里能挪出去的范围，用来放大渲染器包围盒。</summary>
            public readonly float Radius;

            /// <summary>升沉幅度（m）。</summary>
            public readonly float Bob;

            /// <summary>原点向上的最大伸出（m）。鱼群为 0 = 用渲染器包围盒现算。</summary>
            public readonly float Top;

            /// <summary>原点向下的最大伸出（m，正数）。鱼群为 0 = 用渲染器包围盒现算。</summary>
            public readonly float Bottom;

            /// <summary>转弯侧倾系数：侧倾角 = 系数 × 线速度 × 角速度。0 = 不侧倾。</summary>
            public readonly float Bank;

            public Profile(CloudSeaCreatureKind kind, float speed, float radius, float bob, float top, float bottom,
                float bank)
            {
                Kind = kind;
                Speed = speed;
                Radius = radius;
                Bob = bob;
                Top = top;
                Bottom = bottom;
                Bank = bank;
            }
        }

        static readonly Dictionary<string, Profile> Table = new Dictionary<string, Profile>
        {
            { "Clownfish", new Profile(CloudSeaCreatureKind.School, 0.34f, 1.5f, 0.07f, 0f, 0f, 0f) },
            { "Tang", new Profile(CloudSeaCreatureKind.School, 0.42f, 1.7f, 0.07f, 0f, 0f, 0f) },
            { "YellowTang", new Profile(CloudSeaCreatureKind.School, 0.38f, 1.6f, 0.07f, 0f, 0f, 0f) },
            { "Bait", new Profile(CloudSeaCreatureKind.School, 0.55f, 1.3f, 0.09f, 0f, 0f, 0f) },
            { "Sword", new Profile(CloudSeaCreatureKind.Swimmer, 0.85f, 2.8f, 0.12f, 0.84f, 0.54f, 1.0f) },
            { "Manta", new Profile(CloudSeaCreatureKind.Swimmer, 0.55f, 2.3f, 0.14f, 0.60f, 0.60f, 1.2f) },
            { "Turtle", new Profile(CloudSeaCreatureKind.Swimmer, 0.40f, 1.9f, 0.10f, 0.44f, 0.46f, 0f) },
            { "Puffer", new Profile(CloudSeaCreatureKind.Swimmer, 0.14f, 0.8f, 0.05f, 0.62f, 0.62f, 0f) },
            { "Jelly", new Profile(CloudSeaCreatureKind.Jelly, 0f, 0.25f, 0.30f, 0.42f, 1.08f, 0f) },
            // 贴底：半径 = 骨架里的活动范围（蟹的巡逻线半长、章鱼 / 海星的慢逛半径……），要盖过控制器的常数。
            { "CrabWalk", new Profile(CloudSeaCreatureKind.Bottom, 0f, 0.6f, 0f, 0.225f, 0.01f, 0f) },
            { "CrabWave", new Profile(CloudSeaCreatureKind.Bottom, 0f, 0.25f, 0f, 0.225f, 0.01f, 0f) },
            { "Star", new Profile(CloudSeaCreatureKind.Bottom, 0f, 0.9f, 0f, 0.15f, 0.02f, 0f) },
            { "StarGold", new Profile(CloudSeaCreatureKind.Bottom, 0f, 0.9f, 0f, 0.15f, 0.02f, 0f) },
            { "Octopus", new Profile(CloudSeaCreatureKind.Bottom, 0f, 1.6f, 0f, 0.75f, 0.02f, 0f) },
            { "Seahorse", new Profile(CloudSeaCreatureKind.Bottom, 0f, 0.3f, 0f, 1.05f, 0.04f, 0f) },
            { "Anemone", new Profile(CloudSeaCreatureKind.Bottom, 0f, 0.3f, 0f, 0.6f, 0f, 0f) },
        };

        /// <summary>对象名 → 物种名。只认 <c>前缀 + 物种 + 下划线 + 纯数字序号</c>。</summary>
        public static bool TryParse(string objectName, out string species)
        {
            species = null;
            if (objectName == null || !objectName.StartsWith(CloudSeaPath.CreaturePrefix, System.StringComparison.Ordinal))
                return false;

            string rest = objectName.Substring(CloudSeaPath.CreaturePrefix.Length);
            int split = rest.LastIndexOf('_');
            if (split <= 0 || split == rest.Length - 1)
                return false;
            for (int i = split + 1; i < rest.Length; i++)
            {
                if (rest[i] < '0' || rest[i] > '9')
                    return false;
            }
            species = rest.Substring(0, split);
            return true;
        }

        /// <summary>物种 → 行为族；表里没有的物种是 <see cref="CloudSeaCreatureKind.Unknown"/>。</summary>
        public static CloudSeaCreatureKind KindOf(string species)
        {
            Profile profile;
            return TryGet(species, out profile) ? profile.Kind : CloudSeaCreatureKind.Unknown;
        }

        /// <summary>物种 → 巡游参数。表里没有的物种返回 false（调用方要报错，不兜底成别的物种）。</summary>
        public static bool TryGet(string species, out Profile profile)
        {
            if (species != null && Table.TryGetValue(species, out profile))
                return true;
            profile = default(Profile);
            return false;
        }

        /// <summary>让物体的本地 +X 指向水平方向 (x, z) 的 Y 轴旋转角（度）。零向量按 +X 处理。</summary>
        public static float HeadingOf(float x, float z)
        {
            return Mathf.Atan2(-z, x) * Mathf.Rad2Deg;
        }
    }
}
