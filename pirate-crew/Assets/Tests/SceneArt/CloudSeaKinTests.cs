using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.SceneArt.CloudSea;
using UnityEngine;

namespace PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 云海生物运动学的纯 C# 用例（无头可跑）。
    ///
    /// 【覆盖】
    ///   · 关节矩阵与求解器的数学性质：旋转 / 合成 / 节段映射，两骨 IK 的可达与伸直，FABRIK 与
    ///     Verlet 链的长度保持，弹簧在任何帧长下收敛，踩地的脚踩实后不动；
    ///   · 每个物种的控制器：跑上千帧，矩阵必须有限，链上相邻骨骼不能断开（断开 = 求解没保持住
    ///     节段长度），踩地的脚大多数帧不动但确实迈步，水母不越出巡游带。
    /// 骨架在用例里按 Blender 侧（mod_creatures.py）的角色名手搭——角色名两边对不上时控制器构造
    /// 直接抛，这里先响。
    /// </summary>
    public class CloudSeaKinTests
    {
        const float Epsilon = 1e-3f;

        // ------------------------------------------------------------------
        // 关节矩阵
        // ------------------------------------------------------------------

        [Test]
        public void Rotation_AboutSideAxis_TurnsNoseUpAndKeepsPivot()
        {
            Vector3 pivot = new Vector3(0.3f, -0.2f, 0.5f);
            CloudSeaAffine r = CloudSeaAffine.Rotation(Vector3.forward, Mathf.PI * 0.5f, pivot);
            AssertVector(Vector3.up, r.ApplyDir(Vector3.right), "绕侧轴 +90°：前向转到上");
            AssertVector(pivot, r.Apply(pivot), "支点不动");
        }

        [Test]
        public void Transposed_UndoesPureRotation()
        {
            CloudSeaAffine r = CloudSeaAffine.Rotation(new Vector3(0.3f, 0.8f, -0.5f).normalized, 1.1f, Vector3.zero);
            Vector3 v = new Vector3(0.4f, -1.2f, 2.5f);
            AssertVector(v, r.Transposed.Apply(r.Apply(v)), "转置 × 自身 = 单位");
        }

        [Test]
        public void Multiply_AppliesRightHandSideFirst()
        {
            // 先绕 Z 转 90°（前 → 上），再沿上平移 1：结果前向点落在 (0, 2, 0)。
            CloudSeaAffine combined = CloudSeaAffine.Translation(Vector3.up)
                * CloudSeaAffine.Rotation(Vector3.forward, Mathf.PI * 0.5f, Vector3.zero);
            AssertVector(new Vector3(0f, 2f, 0f), combined.Apply(Vector3.right), "先右后左");
        }

        [Test]
        public void Segment_MapsRestStartAndDirectionOntoPosed()
        {
            CloudSeaAffine s = CloudSeaAffine.Segment(new Vector3(1f, 0f, 0f), Vector3.left, new Vector3(3f, 1f, 0f),
                Vector3.forward * 0.01f);
            AssertVector(new Vector3(3f, 1f, 0f), s.Apply(new Vector3(1f, 0f, 0f)), "节段起点");
            AssertVector(new Vector3(3f, 1f, 1f), s.Apply(Vector3.zero), "节段末端：长度不变、方向已转");
        }

        // ------------------------------------------------------------------
        // IK / 链 / 弹簧 / 脚
        // ------------------------------------------------------------------

        [Test]
        public void TwoBone_ReachesTargetAndKeepsBoneLengths()
        {
            Vector3 a = Vector3.zero;
            Vector3 target = new Vector3(0.5f, 0.2f, -0.3f);
            Vector3 mid, end;
            CloudSeaKin.TwoBone(a, 0.4f, 0.35f, target, Vector3.up, out mid, out end);
            AssertVector(target, end, "末端落在目标上");
            Assert.AreEqual(0.4f, (mid - a).magnitude, Epsilon, "第一骨长度");
            Assert.AreEqual(0.35f, (end - mid).magnitude, Epsilon, "第二骨长度");
            Assert.Greater(mid.y, 0f, "中关节鼓向提示的一侧");
        }

        [Test]
        public void TwoBone_UnreachableTarget_StopsAtFullExtension()
        {
            Vector3 mid, end;
            CloudSeaKin.TwoBone(Vector3.zero, 0.4f, 0.35f, new Vector3(5f, 0f, 0f), Vector3.up, out mid, out end);
            Assert.AreEqual(0.75f, end.magnitude, Epsilon, "伸直能及的位置");
            Assert.AreEqual(0f, mid.y, Epsilon, "伸直时中关节不鼓");
        }

        [Test]
        public void Fabrik_ReachesTargetAndKeepsSegmentLengths()
        {
            Vector3[] p = { Vector3.zero, new Vector3(0f, 0f, 0.3f), new Vector3(0f, 0f, 0.6f),
                new Vector3(0f, 0.04f, 0.9f), new Vector3(0f, 0.1f, 1.2f) };
            float[] len = { 0.3f, 0.3f, 0.3f, 0.3f };
            Vector3 target = new Vector3(0.5f, 0.05f, 0.4f);
            CloudSeaKin.Fabrik(p, p.Length, len, target, 8, null, 0f);
            AssertVector(target, p[p.Length - 1], "末端到达目标");
            AssertVector(Vector3.zero, p[0], "根不动");
            for (int k = 0; k < len.Length; k++)
                Assert.AreEqual(len[k], (p[k + 1] - p[k]).magnitude, Epsilon, "第 " + k + " 节长度");
        }

        [Test]
        public void Strand_KeepsLengthsAndPinsRoot()
        {
            Vector3[] rest = { Vector3.zero, new Vector3(0f, 0f, 0.2f), new Vector3(0f, 0f, 0.4f) };
            var strand = new CloudSeaStrand(rest);
            Vector3[] hint = { new Vector3(1f, 0.2f, 0f), new Vector3(1f, 0.2f, 0.2f), new Vector3(1f, 0.2f, 0.4f) };
            strand.Reset(hint);
            Vector3 root = new Vector3(0f, 5f, 0f);
            strand.Acc[1] = Vector3.down * 9.8f;
            strand.Acc[2] = Vector3.down * 9.8f;
            for (int step = 0; step < 60; step++)
            {
                strand.Integrate(1f / 60f, 1f);
                strand.Constrain(root, 2);
            }
            AssertVector(root, strand.P[0], "根钉在给定位置");
            for (int k = 0; k < strand.Len.Length; k++)
                Assert.AreEqual(0.2f, (strand.P[k + 1] - strand.P[k]).magnitude, Epsilon, "第 " + k + " 节长度");
            Assert.Less(strand.P[2].y, root.y, "受重力的质点链一秒后垂到根以下");
        }

        [Test]
        public void Spring_SettlesRegardlessOfStep()
        {
            float[] steps = { 1f / 144f, 1f / 60f, 1f / 12f, 0.5f };
            foreach (float dt in steps)
            {
                var spring = new CloudSeaSpring { X = -3f, V = 8f };
                float time = 0f;
                while (time < 30f)
                {
                    spring.Step(0.7f, dt, 0.8f, 0.3f);
                    time += dt;
                }
                Assert.IsFalse(float.IsNaN(spring.X), "dt = " + dt);
                Assert.AreEqual(0.7f, spring.X, 1e-2f, "dt = " + dt + " 收敛到目标");
            }
        }

        [Test]
        public void Foot_StaysOnAnchorUntilLifted()
        {
            Vector3 anchor = new Vector3(1f, -2f, 0.5f);
            CloudSeaFoot foot = CloudSeaFoot.Plant(anchor);
            foot.Step(0.1f);
            AssertVector(anchor, foot.Position, "踩实的脚不挪");
            foot.Lift(anchor + Vector3.right, 0.2f, 0.1f);
            Assert.IsFalse(foot.Planted, "抬起来了");
            foot.Step(0.1f);
            Assert.Greater(foot.Position.y, anchor.y + 0.09f, "弧线中点在最高处");
            foot.Step(0.1f);
            Assert.IsTrue(foot.Planted, "踩到新落点");
            AssertVector(anchor + Vector3.right, foot.Position, "落点");
        }

        [Test]
        public void Rig_ComposesChildOntoParent()
        {
            var builder = new Builder();
            builder.Add("root", Vector3.zero).Add("child", Vector3.right, "root");
            CloudSeaRig rig = new CloudSeaRig("Test", builder.Roles.ToArray(), builder.Rest.ToArray(),
                builder.Parent.ToArray(), new[] { 0, 1 });
            rig.Begin();
            rig.SetLocal(rig["root"], CloudSeaAffine.Translation(new Vector3(0f, 5f, 0f)));
            rig.SetLocal(rig["child"], CloudSeaAffine.Rotation(Vector3.up, Mathf.PI * 0.5f, rig.Rest[rig["child"]]));
            var buffer = new Vector4[6];
            rig.Compose(buffer);
            CloudSeaAffine parent = Read(buffer, 0);
            CloudSeaAffine child = Read(buffer, 1);
            AssertVector(new Vector3(1f, 5f, 0f), parent.Apply(rig.Rest[rig["child"]]), "父关节带着子关节走");
            AssertVector(new Vector3(1f, 5f, 0f), child.Apply(rig.Rest[rig["child"]]), "子关节绕自己的静止位置转");
            // 绕 +Y 转 90°（Unity 正向 = 俯视顺时针）：+X 转到 -Z。
            AssertVector(new Vector3(1f, 5f, -1f), child.Apply(rig.Rest[rig["child"]] + Vector3.right),
                "父平移之后才轮到子旋转");
        }

        // ------------------------------------------------------------------
        // 控制器：每个物种跑上千帧
        // ------------------------------------------------------------------

        static readonly string[] Species =
        {
            "Clownfish", "Tang", "YellowTang", "Bait", "Sword", "Puffer", "Manta", "Turtle", "Seahorse", "Jelly",
            "Octopus", "CrabWalk", "CrabWave", "Star", "StarGold", "Anemone",
        };

        [TestCaseSource(nameof(Species))]
        public void Brains_StayFiniteAndKeepChainsConnected(string species)
        {
            Spec spec = MakeRig(species);
            CloudSeaBrain brain = CloudSeaBrain.Create(spec.Rig, 0.37f);
            var buffer = new Vector4[spec.Rig.Count * 3];
            float dt = 1f / 60f;
            for (int frame = 0; frame < 900; frame++)
            {
                brain.Step(dt, MakeFrame(species, frame * dt, spec.Rig));
                spec.Rig.Compose(buffer);
                for (int i = 0; i < buffer.Length; i++)
                {
                    Assert.IsFalse(float.IsNaN(buffer[i].x) || float.IsNaN(buffer[i].y) || float.IsNaN(buffer[i].z)
                        || float.IsNaN(buffer[i].w), species + " 第 " + frame + " 帧：关节矩阵出现 NaN");
                }
                foreach (int[] chain in spec.Chains)
                {
                    for (int k = 0; k < chain.Length - 1; k++)
                    {
                        int a = chain[k], b = chain[k + 1];
                        Vector3 gap = spec.Rig.Pose[a].Apply(spec.Rig.Rest[b])
                            - spec.Rig.Pose[b].Apply(spec.Rig.Rest[b]);
                        Assert.Less(gap.magnitude, 0.02f, species + " 第 " + frame + " 帧：关节 " + spec.Rig.Slot[b]
                            + " 处链断开（节段长度没保持住）");
                    }
                }
            }
        }

        [TestCase("Clownfish")]
        [TestCase("Sword")]
        public void Fish_BodyTrailsBehindTheHead(string species)
        {
            Spec spec = MakeRig(species);
            CloudSeaBrain brain = CloudSeaBrain.Create(spec.Rig, 0.37f);
            var buffer = new Vector4[spec.Rig.Count * 3];
            float dt = 1f / 60f;
            Vector3 lastHead = Vector3.zero;
            for (int frame = 0; frame < 240; frame++)
            {
                CloudSeaFrame frameData = MakeFrame(species, frame * dt, spec.Rig);
                brain.Step(dt, frameData);
                spec.Rig.Compose(buffer);
                int[] chain = spec.Chains[0];
                // 关节矩阵是物体空间的；鱼头的运动要在父空间里量（单体剑鱼相对对象不动）。
                Vector3 head = frameData.ToParent(spec.Rig.Pose[chain[0]].Apply(spec.Rig.Rest[chain[0]]));
                Vector3 tail = frameData.ToParent(
                    spec.Rig.Pose[chain[chain.Length - 1]].Apply(spec.Rig.Rest[chain[chain.Length - 1]]));
                if (frame == 0)
                {
                    lastHead = head;
                    continue;
                }
                // 身体拖在鱼头运动方向的反侧：尾轴和运动方向的夹角大于 50°（尾波让它左右摆、
                // 个体加速时身体滞后，但反过来游 = 夹角 0° 会在这里响）。
                Vector3 motion = head - lastHead;
                lastHead = head;
                Assert.Less(Vector3.Dot(tail - head, motion), 0.64f * (tail - head).magnitude * motion.magnitude,
                    species + " 第 " + frame + " 帧：尾巴甩到了鱼头运动方向的前方");
            }
        }

        [TestCase("CrabWalk")]
        [TestCase("CrabWave")]
        [TestCase("Star")]
        public void PlantedFeet_HoldStillMostOfTheTimeButDoStep(string species)
        {
            Spec spec = MakeRig(species);
            CloudSeaBrain brain = CloudSeaBrain.Create(spec.Rig, 0.61f);
            var buffer = new Vector4[spec.Rig.Count * 3];
            float dt = 1f / 60f;
            Vector3[] last = null;
            int moving = 0, samples = 0;
            for (int frame = 0; frame < 1200; frame++)
            {
                brain.Step(dt, MakeFrame(species, frame * dt, spec.Rig));
                spec.Rig.Compose(buffer);
                Vector3[] tips = new Vector3[spec.Feet.Count];
                for (int i = 0; i < spec.Feet.Count; i++)
                {
                    int[] chain = spec.Feet[i];
                    // 足尖 = 最后一节骨骼把自己的静止末端映过去的位置
                    tips[i] = spec.Rig.Pose[chain[chain.Length - 2]].Apply(spec.Rig.Rest[chain[chain.Length - 1]]);
                }
                if (last != null)
                {
                    for (int i = 0; i < tips.Length; i++)
                    {
                        samples++;
                        if ((tips[i] - last[i]).magnitude > 1e-4f)
                            moving++;
                    }
                }
                last = tips;
            }
            bool walks = species == "CrabWalk" || species.StartsWith("Star");
            if (walks)
            {
                Assert.Greater(moving, 0, "从来不迈步");
                Assert.Less((float)moving / samples, 0.35f, "脚挪动的时间占比过高（在滑，不是迈步）");
            }
            else
            {
                Assert.AreEqual(0, moving, "挥钳的蟹腿不该动");
            }
        }

        [TestCase("Star")]
        [TestCase("StarGold")]
        public void Star_ArmsBendInPlaneInsteadOfStandingUp(string species)
        {
            Spec spec = MakeRig(species);
            CloudSeaBrain brain = CloudSeaBrain.Create(spec.Rig, 0.37f);
            var buffer = new Vector4[spec.Rig.Count * 3];
            float dt = 1f / 60f;
            for (int frame = 0; frame < 1800; frame++)
            {
                CloudSeaFrame frameData = MakeFrame(species, frame * dt, spec.Rig);
                brain.Step(dt, frameData);
                spec.Rig.Compose(buffer);
                foreach (int[] chain in spec.Feet)
                {
                    // 关节矩阵是物体空间的，高度要在竖直统一的父空间里量。
                    Vector3 root = frameData.ToParent(spec.Rig.Pose[chain[0]].Apply(spec.Rig.Rest[chain[0]]));
                    Vector3 mid = frameData.ToParent(spec.Rig.Pose[chain[1]].Apply(spec.Rig.Rest[chain[1]]));
                    Vector3 tip = frameData.ToParent(spec.Rig.Pose[chain[2]].Apply(spec.Rig.Rest[chain[2]]));
                    Assert.Less(mid.y - root.y, 0.10f, species + " 第 " + frame + " 帧：中关节拱得过高，海星撑成了一只手");
                    Assert.Less(tip.y, FlatBed(0f, 0f) + 0.07f, species + " 第 " + frame + " 帧：腕尖迈步抬得过高");
                    float flat = Vector2.Distance(new Vector2(tip.x, tip.z), new Vector2(root.x, root.z));
                    Assert.Greater(flat, 0.15f, species + " 第 " + frame + " 帧：腕尖缩到了中心底下");
                }
            }
        }

        [Test]
        public void Octopus_ExploringArmReachesForwardNotUp()
        {
            Spec spec = MakeRig("Octopus");
            CloudSeaBrain brain = CloudSeaBrain.Create(spec.Rig, 0.37f);
            var buffer = new Vector4[spec.Rig.Count * 3];
            float dt = 1f / 60f;
            for (int frame = 0; frame < 1800; frame++)
            {
                CloudSeaFrame frameData = MakeFrame("Octopus", frame * dt, spec.Rig);
                brain.Step(dt, frameData);
                spec.Rig.Compose(buffer);
                foreach (int[] chain in spec.Chains)
                {
                    if (!spec.Rig.Roles[chain[0]].StartsWith("w", System.StringComparison.Ordinal))
                        continue;
                    // 关节矩阵是物体空间的，高度要在竖直统一的父空间里量。
                    int last = chain.Length - 1;
                    Vector3 root = frameData.ToParent(spec.Rig.Pose[chain[0]].Apply(spec.Rig.Rest[chain[0]]));
                    Vector3 tip = frameData.ToParent(spec.Rig.Pose[chain[last]].Apply(spec.Rig.Rest[chain[last]]));
                    // 迈步的腕尖最多抬 0.17；探水的腕从前把腕尖悬在腕根上方 0.16–0.44——那是举过头顶，不是往前伸。
                    Assert.Less(tip.y - root.y, 0.25f, "第 " + frame + " 帧：有腕尖举过了头顶——探水的腕该贴着沙面往前伸");
                }
            }
        }

        [Test]
        public void Jelly_StaysInsideItsBandAndPulses()
        {
            Spec spec = MakeRig("Jelly");
            var jelly = CloudSeaBrain.Create(spec.Rig, 0.5f);
            var buffer = new Vector4[spec.Rig.Count * 3];
            float dt = 1f / 60f;
            float min = float.MaxValue, max = float.MinValue;
            for (int frame = 0; frame < 3600; frame++)
            {
                CloudSeaFrame frameData = MakeFrame("Jelly", frame * dt, spec.Rig);
                jelly.Step(dt, frameData);
                spec.Rig.Compose(buffer);
                if (frame < 600)
                    continue;
                float y = jelly.AltitudeY;
                Assert.LessOrEqual(y, frameData.CeilingY + Epsilon, "钟顶不出水");
                Assert.GreaterOrEqual(y, frameData.FloorY - Epsilon, "触手尖不插沙");
                min = Mathf.Min(min, y);
                max = Mathf.Max(max, y);
            }
            Assert.Greater(max - min, 0.05f, "没有一窜一沉");
        }

        [Test]
        public void Puffer_SlowsDownWhenPuffed()
        {
            Spec spec = MakeRig("Puffer");
            var puffer = CloudSeaBrain.Create(spec.Rig, 0.2f);
            var buffer = new Vector4[spec.Rig.Count * 3];
            float dt = 1f / 60f;
            float slowest = 1f;
            for (int frame = 0; frame < 60 * 20; frame++)
            {
                puffer.Step(dt, MakeFrame("Puffer", frame * dt, spec.Rig));
                spec.Rig.Compose(buffer);
                slowest = Mathf.Min(slowest, puffer.SpeedScale);
            }
            Assert.Less(slowest, 0.6f, "20 秒里总该鼓一次");
        }

        // ------------------------------------------------------------------
        // 合成骨架
        // ------------------------------------------------------------------

        sealed class Spec
        {
            public CloudSeaRig Rig;
            /// <summary>所有链（关节号序列），用来查相邻骨骼有没有断开。</summary>
            public readonly List<int[]> Chains = new List<int[]>();
            /// <summary>踩地的链（蟹腿 / 海星腕），末节点 = 足尖。</summary>
            public readonly List<int[]> Feet = new List<int[]>();
        }

        sealed class Builder
        {
            readonly List<string> _roles = new List<string>();
            readonly List<Vector3> _rest = new List<Vector3>();
            readonly List<int> _parent = new List<int>();
            readonly Dictionary<string, int> _index = new Dictionary<string, int>();

            public Builder Add(string role, Vector3 rest, string parent = null)
            {
                _index[role] = _roles.Count;
                _roles.Add(role);
                _rest.Add(rest);
                _parent.Add(parent == null ? -1 : _index[parent]);
                return this;
            }

            public int Index(string role)
            {
                return _index[role];
            }

            public List<string> Roles => _roles;
            public List<Vector3> Rest => _rest;
            public List<int> Parent => _parent;
        }

        static Vector3[] Line(float step, int count, Vector3 axis)
        {
            var points = new Vector3[count];
            for (int k = 0; k < count; k++)
                points[k] = axis * (step * k);
            return points;
        }

        static Spec MakeRig(string species)
        {
            var spec = new Spec();
            var b = new Builder();
            // 被求解器驱动的链：每个节点都是根一级的关节（父 = -1），由 SetPose 直接写绝对矩阵。
            int[] Chain(string prefix, Vector3[] points)
            {
                var nodes = new int[points.Length];
                for (int k = 0; k < points.Length; k++)
                {
                    b.Add(prefix + k, points[k]);
                    nodes[k] = b.Index(prefix + k);
                }
                spec.Chains.Add(nodes);
                return nodes;
            }

            // 挂在别的关节上的链（魔鬼鱼的翼、海龟的后鳍）：逐级父子，靠局部形变叠加。
            void Hierarchy(string prefix, Vector3[] points, string parent)
            {
                for (int k = 0; k < points.Length; k++)
                    b.Add(prefix + k, points[k], k == 0 ? parent : prefix + (k - 1));
            }
            switch (species)
            {
                case "Clownfish":
                case "Tang":
                case "YellowTang":
                case "Bait":
                    for (int i = 0; i < 2; i++)
                    {
                        string fish = "f" + i;
                        Vector3[] spine = { new Vector3(0.5f, 0.3f * i, 0.5f * i), new Vector3(0.2f, 0.3f * i, 0.5f * i),
                            new Vector3(-0.1f, 0.3f * i, 0.5f * i), new Vector3(-0.4f, 0.3f * i, 0.5f * i),
                            new Vector3(-0.7f, 0.3f * i, 0.5f * i) };
                        Chain(fish + "s", spine);
                        if (species != "Bait")
                        {
                            b.Add(fish + "finL", new Vector3(0.3f, 0f, 0.3f), fish + "s0");
                            b.Add(fish + "finR", new Vector3(0.3f, 0f, -0.3f), fish + "s0");
                        }
                    }
                    break;
                case "Sword":
                    Chain("s", Line(-0.4f, 5, Vector3.right));
                    b.Add("finL", new Vector3(0.8f, 0f, 0.3f), "s0");
                    b.Add("finR", new Vector3(0.8f, 0f, -0.3f), "s0");
                    break;
                case "Puffer":
                    b.Add("body", Vector3.zero);
                    b.Add("spike", Vector3.zero, "body");
                    b.Add("finL", new Vector3(0.1f, 0f, 0.36f), "body");
                    b.Add("finR", new Vector3(0.1f, 0f, -0.36f), "body");
                    b.Add("tail", new Vector3(-0.4f, 0f, 0f), "body");
                    break;
                case "Manta":
                    b.Add("body", Vector3.zero);
                    Hierarchy("wL", Line(0.5f, 4, Vector3.forward), "body");
                    Hierarchy("wR", Line(-0.5f, 4, Vector3.forward), "body");
                    Chain("tail", Line(-0.4f, 4, Vector3.left));
                    break;
                case "Turtle":
                    b.Add("body", Vector3.zero);
                    Chain("n", new[] { new Vector3(0.5f, 0.1f, 0f), new Vector3(0.72f, 0.14f, 0f), new Vector3(0.95f, 0.16f, 0f) });
                    Chain("fL", new[] { new Vector3(0.3f, 0.14f, 0.5f), new Vector3(0.08f, 0.14f, 0.83f), new Vector3(-0.14f, 0.14f, 1.17f) });
                    Chain("fR", new[] { new Vector3(0.3f, 0.14f, -0.5f), new Vector3(0.08f, 0.14f, -0.83f), new Vector3(-0.14f, 0.14f, -1.17f) });
                    Hierarchy("bL", new[] { new Vector3(-0.55f, 0.08f, 0.38f), new Vector3(-0.69f, 0.08f, 0.76f) }, "body");
                    Hierarchy("bR", new[] { new Vector3(-0.55f, 0.08f, -0.38f), new Vector3(-0.69f, 0.08f, -0.76f) }, "body");
                    break;
                case "Seahorse":
                    Chain("p", Line(0.2f, 6, Vector3.up));
                    b.Add("head", new Vector3(0.1f, 1.0f, 0f), "p4");
                    b.Add("fin", new Vector3(-0.1f, 0.6f, 0f), "p3");
                    break;
                case "Jelly":
                    b.Add("rim", new Vector3(0f, 0f, 0f));
                    b.Add("mid", new Vector3(0f, 0.2f, 0f));
                    b.Add("apex", new Vector3(0f, 0.4f, 0f));
                    Chain("t0_", Line(-0.3f, 4, Vector3.up));
                    Chain("a0_", Line(-0.3f, 3, Vector3.up));
                    break;
                case "Octopus":
                    b.Add("body", new Vector3(0f, 0.16f, 0f));
                    b.Add("mantle", new Vector3(0f, 0.4f, 0f), "body");
                    for (int a = 0; a < 3; a++)
                    {
                        Vector3 dir = new Vector3(Mathf.Cos(a * 2.1f), 0f, Mathf.Sin(a * 2.1f));
                        Chain("w" + a + "_", new[] { new Vector3(0f, 0.16f, 0f), dir * 0.3f + Vector3.up * 0.1f,
                            dir * 0.6f + Vector3.up * 0.05f, dir * 0.9f + Vector3.up * 0.05f });
                    }
                    break;
                case "CrabWalk":
                case "CrabWave":
                    b.Add("body", new Vector3(0f, 0.16f, 0f));
                    b.Add("eyeL", new Vector3(0.24f, 0.22f, 0.09f), "body");
                    b.Add("eyeR", new Vector3(0.24f, 0.22f, -0.09f), "body");
                    Chain("aL_", new[] { new Vector3(0.16f, 0.14f, 0.3f), new Vector3(0.34f, 0.12f, 0.44f), new Vector3(0.5f, 0.1f, 0.4f) });
                    Chain("aR_", new[] { new Vector3(0.16f, 0.14f, -0.3f), new Vector3(0.34f, 0.12f, -0.44f), new Vector3(0.5f, 0.1f, -0.4f) });
                    foreach (string side in new[] { "lL", "lR" })
                    {
                        float sign = side == "lL" ? 1f : -1f;
                        for (int i = 0; i < 2; i++)
                        {
                            // 比例照着真蟹：髋离地 0.13，腿总长 0.44，静止时踝在髋外 0.365（够得着）。
                            Vector3 hip = new Vector3(0.17f - 0.11f * i, 0.13f, sign * 0.3f);
                            spec.Feet.Add(Chain(side + i + "_", new[] { hip, hip + new Vector3(0.10f, 0.08f, sign * 0.15f),
                                hip + new Vector3(0.20f, -0.06f, sign * 0.30f), hip + new Vector3(0.25f, -0.13f, sign * 0.40f) }));
                        }
                    }
                    break;
                case "Star":
                case "StarGold":
                    b.Add("centre", new Vector3(0f, 0.05f, 0f));
                    for (int a = 0; a < 5; a++)
                    {
                        Vector3 dir = new Vector3(Mathf.Cos(a * 1.2566f), 0f, Mathf.Sin(a * 1.2566f));
                        spec.Feet.Add(Chain("w" + a + "_", new[] { new Vector3(0f, 0.02f, 0f), dir * 0.23f + Vector3.up * 0.02f, dir * 0.46f }));
                    }
                    break;
                case "Anemone":
                    Chain("f0_", Line(0.15f, 4, Vector3.up));
                    Chain("f1_", Line(0.15f, 4, new Vector3(0.2f, 0.95f, 0.1f).normalized));
                    break;
                default:
                    Assert.Fail("没有这个物种的合成骨架：" + species);
                    break;
            }
            int count = b.Roles.Count;
            var slots = new int[count];
            for (int i = 0; i < count; i++)
                slots[i] = i;
            spec.Rig = new CloudSeaRig(species, b.Roles.ToArray(), b.Rest.ToArray(), b.Parent.ToArray(), slots);
            return spec;
        }

        /// <summary>
        /// 测试帧：游泳者沿圆巡游（对象在动、在转），贴底生物对象不动；沙面平，在 -2。
        /// </summary>
        static CloudSeaFrame MakeFrame(string species, float time, CloudSeaRig rig)
        {
            bool crawler = species == "Octopus" || species == "CrabWalk" || species == "CrabWave"
                || species == "Star" || species == "StarGold" || species == "Anemone" || species == "Seahorse";
            // 游泳者沿半径 2 的圆顺时针拐：t = 0 时朝 +X，速度方向 = 对象的前向（Unity 偏航 a 的前向 = (cos a, 0, -sin a)）。
            float theta = time * 0.4f;
            float yaw = crawler ? 0.7f : -theta;
            var frame = new CloudSeaFrame
            {
                Time = time,
                Position = crawler ? new Vector3(3f, -2f, 4f)
                    : new Vector3(3f + Mathf.Sin(theta) * 2f, -1.2f, 5f - Mathf.Cos(theta) * 2f),
                Rotation = CloudSeaAffine.Rotation(Vector3.up, yaw, Vector3.zero),
                Velocity = crawler ? Vector3.zero : new Vector3(Mathf.Cos(theta) * 0.8f, 0f, Mathf.Sin(theta) * 0.8f),
                YawRate = crawler ? 0f : -0.4f,
                Bed = FlatBed,
                FloorY = -2f,
                CeilingY = -0.5f,
            };
            return frame;
        }

        static float FlatBed(float x, float z)
        {
            return -2f;
        }

        static CloudSeaAffine Read(Vector4[] buffer, int joint)
        {
            return new CloudSeaAffine { R0 = buffer[joint * 3], R1 = buffer[joint * 3 + 1], R2 = buffer[joint * 3 + 2] };
        }

        static void AssertVector(Vector3 expected, Vector3 actual, string message)
        {
            Assert.Less((expected - actual).magnitude, Epsilon, message + "（期望 " + expected + "，实际 " + actual + "）");
        }
    }
}
