using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>
    /// 一只生物的运动学控制器：每帧读对象位姿与速度（<see cref="CloudSeaFrame"/>），用求解器算出骨架上
    /// 每个关节的矩阵。只动骨架——对象往哪走由 <c>CloudSeaLife</c>> 决定；蟹 / 海星 / 章鱼 / 海葵 / 海马
    /// 这类贴底生物对象不动，整体位移在骨架里做（脚要踩在世界里的锚点上）。
    /// 纯 C#：只用 Vector3 / Mathf 的托管数学，验证台可跑。
    /// </summary>
    public abstract class CloudSeaBrain
    {
        public readonly CloudSeaRig Rig;
        public readonly float Seed;

        protected CloudSeaBrain(CloudSeaRig rig, float seed)
        {
            Rig = rig;
            Seed = seed;
        }

        public abstract void Step(float dt, in CloudSeaFrame frame);

        /// <summary>巡游速度倍率：有的生物在某些状态下推不动水（河豚鼓起来以后）。</summary>
        public virtual float SpeedScale => 1f;

        /// <summary>自己管升沉的生物（水母）为 true：<see cref="AltitudeY"/> 给出对象原点该在的高度。</summary>
        public virtual bool DrivesAltitude => false;

        /// <summary>对象原点该在的高度（父空间），只在 <see cref="DrivesAltitude"/> 为 true 时有意义。</summary>
        public virtual float AltitudeY => 0f;

        public static CloudSeaBrain Create(CloudSeaRig rig, float seed)
        {
            switch (rig.Species)
            {
                case "Clownfish":
                case "Tang":
                case "YellowTang":
                case "Bait":
                    return new FishBrain(rig, seed, true);
                case "Sword":
                    return new FishBrain(rig, seed, false);
                case "Puffer":
                    return new PufferBrain(rig, seed);
                case "Manta":
                    return new MantaBrain(rig, seed);
                case "Turtle":
                    return new TurtleBrain(rig, seed);
                case "Seahorse":
                    return new SeahorseBrain(rig, seed);
                case "Jelly":
                    return new JellyBrain(rig, seed);
                case "Octopus":
                    return new OctopusBrain(rig, seed);
                case "CrabWalk":
                    return new CrabBrain(rig, seed, false);
                case "CrabWave":
                    return new CrabBrain(rig, seed, true);
                case "Star":
                case "StarGold":
                    return new StarBrain(rig, seed);
                case "Anemone":
                    return new AnemoneBrain(rig, seed);
                default:
                    return new StillBrain(rig, seed);
            }
        }

        /// <summary>按「前缀 + 0, 1, 2…」取链；一条都没有时报错（骨架与控制器对不上，不能悄悄不动画）。</summary>
        protected int[] NeedChain(string prefix)
        {
            int[] nodes = Rig.ChainRoles(prefix);
            if (nodes.Length < 2)
                throw new ArgumentException("[" + Rig.Species + "] 骨架里没有链 " + prefix + "0、…" + "。");
            return nodes;
        }
    }

    // ----------------------------------------------------------------------
    // 鱼：个体有惯性，身体拖在头的后面
    // ----------------------------------------------------------------------

    /// <summary>
    /// 鱼（鱼群里的个体 / 单体的剑鱼）。
    /// 鱼群里的每条鱼是一个有质量的点：被弹簧拉向自己在群里的位置、和同伴互相避让、各自游移，所以群不是
    /// 一块刚体——拐弯时外侧的鱼会被甩开一点，再追回来。身体是追随链（<see cref="CloudSeaFollowChain"/>）：
    /// 头真的在空间里走，身后的节段被拖着走、只允许有限弯角，于是拐弯时身体弯进弯里；摆头带出一道尾波，
    /// 游得越快摆得越快越大。胸鳍跟着划水。
    /// 单体的剑鱼头钉在对象上，身体同理。
    /// </summary>
    sealed class FishBrain : CloudSeaBrain
    {
        const float SeekStiffness = 25f;       // ω = 5 rad/s
        const float SeekDamping = 6f;          // ζ ≈ 0.6
        const float Separate = 9f;
        const float MaxRelative = 0.8f;        // m/s：相对群的速度上限
        const float TurnRate = 7f;             // rad/s

        sealed class Fish
        {
            public CloudSeaFollowChain Chain;
            public int FinL = -1;
            public int FinR = -1;
            public Vector3 Slot;
            public Vector3 Position;
            public Vector3 Relative;
            public Vector3 Forward;
            public float Seed;
            public float Rate;
            public float HeadAmplitude;
        }

        readonly Fish[] _fish;
        readonly bool _school;
        readonly float _gap;
        readonly float _referenceSpeed;
        bool _initialised;

        public FishBrain(CloudSeaRig rig, float seed, bool school) : base(rig, seed)
        {
            _school = school;
            var fish = new List<Fish>();
            for (int i = 0; ; i++)
            {
                string prefix = school ? "f" + i : string.Empty;
                if (!rig.Has(prefix + "s0"))
                    break;
                int[] nodes = NeedChain(prefix + "s");
                var f = new Fish
                {
                    Chain = new CloudSeaFollowChain(rig, nodes),
                    Slot = rig.Rest[nodes[0]],
                    Seed = CloudSeaKin.Frac(seed + i * 0.618034f),
                    HeadAmplitude = 0.10f,
                };
                int fin;
                if (rig.TryGet(prefix + "finL", out fin))
                    f.FinL = fin;
                if (rig.TryGet(prefix + "finR", out fin))
                    f.FinR = fin;
                int last = nodes.Length - 1;
                float length = 0f;
                for (int k = 0; k < last; k++)
                    length += f.Chain.Len[k];
                f.Rate = 2.6f / Mathf.Sqrt(Mathf.Max(0.2f, length));
                float bend = 0.5f / Mathf.Sqrt(Mathf.Max(0.2f, length));
                for (int k = 1; k < last; k++)
                {
                    f.Chain.Wave[k] = 0.17f * (0.25f + 0.75f * k / (float)(last - 1));
                    f.Chain.MaxBend[k] = bend;
                }
                fish.Add(f);
                if (!school)
                    break;
            }
            if (fish.Count == 0)
                throw new ArgumentException("[" + rig.Species + "] 骨架里没有鱼链。");
            _fish = fish.ToArray();
            float gap = 0f;
            for (int i = 0; i < _fish.Length; i++)
                gap += 0.30f * _fish[i].Chain.Len[0] * 4f;
            _gap = gap / _fish.Length;
            _referenceSpeed = school ? 0.55f : 0.85f;
        }

        public override void Step(float dt, in CloudSeaFrame frame)
        {
            Rig.Begin();
            float t = frame.Time;
            bool first = !_initialised;
            for (int i = 0; i < _fish.Length; i++)
            {
                Fish f = _fish[i];
                Vector3 velocity = frame.Velocity;
                if (_school)
                {
                    Vector3 wander = new Vector3(
                        0.22f * CloudSeaKin.Wobble(t * 0.37f, f.Seed),
                        0.09f * CloudSeaKin.Wobble(t * 0.51f, f.Seed + 7.3f),
                        0.22f * CloudSeaKin.Wobble(t * 0.29f, f.Seed + 13.7f));
                    Vector3 acc = (frame.ToParent(f.Slot + wander) - f.Position) * SeekStiffness
                        - f.Relative * SeekDamping;
                    for (int j = 0; j < _fish.Length; j++)
                    {
                        if (j == i)
                            continue;
                        Vector3 away = f.Position - _fish[j].Position;
                        float dist = away.magnitude;
                        if (dist > 1e-4f && dist < _gap)
                            acc += away * ((1f - dist / _gap) * Separate / dist);
                    }
                    f.Relative += acc * dt;
                    float rel = f.Relative.magnitude;
                    if (rel > MaxRelative)
                        f.Relative *= MaxRelative / rel;
                    f.Position += (velocity + f.Relative) * dt;
                    velocity += f.Relative;
                }
                else
                {
                    f.Position = frame.ToParent(f.Slot);
                }

                // 鱼群里的个体按自己的速度定朝向；单体的剑鱼头钉在对象上（对象已经跟住路径、带着俯仰侧倾）。
                Vector3 desired = _school
                    ? CloudSeaKin.Unit(velocity, frame.DirToParent(Vector3.right))
                    : frame.DirToParent(Vector3.right);
                if (first)
                {
                    f.Position = frame.ToParent(f.Slot);
                    f.Relative = Vector3.zero;
                    f.Forward = desired;
                    f.Chain.Reset(Rig, frame);
                }
                f.Forward = CloudSeaKin.TurnToward(f.Forward, desired, TurnRate * dt);
                float effort = Mathf.Clamp01(velocity.magnitude / _referenceSpeed);
                f.Chain.Phase += CloudSeaKin.Tau * f.Rate * (0.75f + 0.45f * effort) * dt;
                f.Chain.WaveScale = 0.75f + 0.5f * effort;
                f.Chain.Solve(frame, f.Position, f.Forward, f.HeadAmplitude);
                f.Chain.Write(Rig, frame);

                if (f.FinL >= 0)
                {
                    float a = 0.32f * Mathf.Sin(f.Chain.Phase + 1.1f) + 0.10f;
                    Rig.SetLocal(f.FinL, CloudSeaAffine.Rotation(Vector3.right, a, Rig.Rest[f.FinL]));
                    Rig.SetLocal(f.FinR, CloudSeaAffine.Rotation(Vector3.right, -a, Rig.Rest[f.FinR]));
                }
            }
            _initialised = true;
        }
    }

    // ----------------------------------------------------------------------
    // 河豚：划水带出升沉，鼓胀是弹簧
    // ----------------------------------------------------------------------

    sealed class PufferBrain : CloudSeaBrain
    {
        int _body, _spike, _finL, _finR, _tail;
        CloudSeaSpring _puff;
        CloudSeaSpring _bob;
        float _phase;
        float _clock;
        float _lastSin;

        public PufferBrain(CloudSeaRig rig, float seed) : base(rig, seed)
        {
            _body = rig["body"];
            _spike = rig["spike"];
            _finL = rig["finL"];
            _finR = rig["finR"];
            _tail = rig["tail"];
            _puff = new CloudSeaSpring();
            _bob = new CloudSeaSpring();
        }

        public override float SpeedScale => Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(_puff.X));

        public override void Step(float dt, in CloudSeaFrame frame)
        {
            Rig.Begin();
            _clock += dt;
            // 鼓胀日程：隔一阵鼓一次——0.35 秒鼓起、保持 2.5 秒、1.6 秒放掉。
            float cycle = 9f + 6f * Seed;
            float tc = Mathf.Repeat(_clock + cycle * 0.55f, cycle);
            float target = tc < 0.35f ? tc / 0.35f
                : tc < 2.85f ? 1f
                : tc < 4.45f ? 1f - (tc - 2.85f) / 1.6f
                : 0f;
            _puff.Step(target, dt, 1.6f, 0.5f);
            float puff = Mathf.Clamp01(_puff.X);

            _phase += CloudSeaKin.Tau * Mathf.Lerp(2.4f, 1.1f, puff) * dt;
            float s = Mathf.Sin(_phase);
            if (s < -0.98f && _lastSin >= -0.98f)
                _bob.Impulse(0.28f * (1f - 0.7f * puff));        // 划到最低点：身体被托一下
            _lastSin = s;
            _bob.Step(0f, dt, 0.55f, 0.22f);

            float pitch = Mathf.Clamp(_bob.V * 0.5f, -0.22f, 0.22f);
            float scale = 1f + 0.45f * puff;
            Vector3 pivot = Rig.Rest[_body];
            Rig.SetLocal(_body, CloudSeaAffine.Translation(Vector3.up * _bob.X)
                * CloudSeaAffine.Rotation(Vector3.forward, pitch, pivot)
                * CloudSeaAffine.Scale(new Vector3(scale, scale, scale), pivot));
            float spike = (1f + 1.15f * puff) / scale;
            Rig.SetLocal(_spike, CloudSeaAffine.Scale(new Vector3(spike, spike, spike), Rig.Rest[_spike]));

            float fin = (0.5f * s + 0.12f) * (1f - 0.8f * puff) + 0.08f;
            Rig.SetLocal(_finL, CloudSeaAffine.Rotation(Vector3.right, fin, Rig.Rest[_finL]));
            Rig.SetLocal(_finR, CloudSeaAffine.Rotation(Vector3.right, -fin, Rig.Rest[_finR]));
            Rig.SetLocal(_tail,
                CloudSeaAffine.Rotation(Vector3.up, 0.4f * Mathf.Sin(_phase * 0.5f), Rig.Rest[_tail]));
        }
    }

    // ----------------------------------------------------------------------
    // 魔鬼鱼：翼沿翼展的行波，细尾拖在身后
    // ----------------------------------------------------------------------

    sealed class MantaBrain : CloudSeaBrain
    {
        readonly int _body;
        readonly int[][] _wings = new int[2][];
        readonly int[] _tailNodes;
        readonly CloudSeaStrand _tail;
        readonly float _phase0;
        CloudSeaSpring _bob;
        float _phase;
        float _lastSin;
        bool _initialised;
        readonly Vector3[] _tailRest;

        public MantaBrain(CloudSeaRig rig, float seed) : base(rig, seed)
        {
            _body = rig["body"];
            _wings[0] = NeedChain("wL");
            _wings[1] = NeedChain("wR");
            _tailNodes = NeedChain("tail");
            _tailRest = new Vector3[_tailNodes.Length];
            rig.RestChain(_tailNodes, _tailRest);
            _tail = new CloudSeaStrand(_tailRest);
            _bob = new CloudSeaSpring();
            _phase0 = seed * CloudSeaKin.Tau;
        }

        public override void Step(float dt, in CloudSeaFrame frame)
        {
            Rig.Begin();
            _phase += CloudSeaKin.Tau * 0.55f * dt;
            float s = Mathf.Sin(_phase + _phase0);
            if (s < -0.98f && _lastSin >= -0.98f)
                _bob.Impulse(0.22f);
            _lastSin = s;
            _bob.Step(0f, dt, 0.5f, 0.3f);
            float pitch = Mathf.Clamp(_bob.V * 0.4f, -0.18f, 0.18f);
            Vector3 pivot = Rig.Rest[_body];
            CloudSeaAffine body = CloudSeaAffine.Translation(Vector3.up * _bob.X)
                * CloudSeaAffine.Rotation(Vector3.forward, pitch, pivot);
            Rig.SetLocal(_body, body);

            // 扇翼：每节相对前一节绕前向轴摆，越靠翼尖幅度越大、相位越滞后。左右同号镜像 = 同上同下。
            // 转弯时两翼一起偏一点（内侧翼压低），配合对象自身的侧倾。
            float turn = 0.35f * Mathf.Clamp(frame.YawRate, -1f, 1f);
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? 1f : -1f;
                int[] wing = _wings[side];
                for (int k = 0; k < 3; k++)
                {
                    float a = sign * 0.30f * (0.35f + 0.65f * k * 0.5f) * Mathf.Sin(_phase + _phase0 - k * 0.85f)
                        - turn * (k * 0.5f);
                    Rig.SetLocal(wing[k], CloudSeaAffine.Rotation(Vector3.right, a, Rig.Rest[wing[k]]));
                }
            }

            // 细尾：Verlet 链，根钉在身体后端，水阻力把它甩在路径上。
            Vector3 root = frame.ToParent(body.Apply(Rig.Rest[_tailNodes[0]]));
            if (!_initialised)
            {
                for (int k = 0; k < _tailNodes.Length; k++)
                    _tailRest[k] = frame.ToParent(Rig.Rest[_tailNodes[k]]);
                _tail.Reset(_tailRest);
                _initialised = true;
            }
            Vector3[] acc = _tail.Acc;
            for (int i = 1; i < _tail.Count; i++)
                acc[i] = Vector3.down * 0.8f - _tail.Velocity(i, dt) * 2.5f;
            _tail.Integrate(dt, 1f);
            _tail.Constrain(root, 2);
            Rig.WriteChainParent(_tailNodes, _tail.P, frame);
        }
    }

    // ----------------------------------------------------------------------
    // 海龟：前后鳍两骨 IK 划水，脖子 IK 张望
    // ----------------------------------------------------------------------

    sealed class TurtleBrain : CloudSeaBrain
    {
        sealed class Flipper
        {
            public int[] Nodes;
            public float L1, L2;
        }

        readonly int _body;
        readonly int[] _neck;
        readonly Flipper[] _front = new Flipper[2];
        readonly int[][] _rear = new int[2][];
        readonly float[] _len = new float[2];
        readonly Vector3[] _q = new Vector3[3];
        float _phase;

        public TurtleBrain(CloudSeaRig rig, float seed) : base(rig, seed)
        {
            _body = rig["body"];
            _neck = NeedChain("n");
            _front[0] = MakeFlipper(rig, "fL");
            _front[1] = MakeFlipper(rig, "fR");
            _rear[0] = NeedChain("bL");
            _rear[1] = NeedChain("bR");
            _len[0] = (rig.Rest[_neck[1]] - rig.Rest[_neck[0]]).magnitude;
            _len[1] = (rig.Rest[_neck[2]] - rig.Rest[_neck[1]]).magnitude;
        }

        static Flipper MakeFlipper(CloudSeaRig rig, string prefix)
        {
            int[] nodes = NeedChainStatic(rig, prefix);
            return new Flipper
            {
                Nodes = nodes,
                L1 = (rig.Rest[nodes[1]] - rig.Rest[nodes[0]]).magnitude,
                L2 = (rig.Rest[nodes[2]] - rig.Rest[nodes[1]]).magnitude,
            };
        }

        static int[] NeedChainStatic(CloudSeaRig rig, string prefix)
        {
            int[] nodes = rig.ChainRoles(prefix);
            if (nodes.Length < 3)
                throw new ArgumentException("[" + rig.Species + "] 鳍肢链 " + prefix + "0… 不足三节。");
            return nodes;
        }

        public override void Step(float dt, in CloudSeaFrame frame)
        {
            Rig.Begin();
            float t = frame.Time;
            _phase += CloudSeaKin.Tau * 0.45f * dt;

            Rig.SetLocal(_body,
                CloudSeaAffine.Translation(Vector3.up * (0.03f * Mathf.Sin(_phase))));

            // 前鳍：绕肩一圈斜着划（下划时往后扫、上抬时往前送）。左右鳍伸向相反方向，同一个转角
            // 正好给出左右交替的划水。肘朝下、往后弯。
            for (int side = 0; side < 2; side++)
            {
                Flipper f = _front[side];
                Vector3 root = Rig.Rest[f.Nodes[0]];
                Vector3 offset = Rig.Rest[f.Nodes[2]] - root;
                CloudSeaAffine stroke = CloudSeaAffine.Rotation(Vector3.right, 0.55f * Mathf.Sin(_phase), Vector3.zero)
                    * CloudSeaAffine.Rotation(Vector3.up, 0.35f * Mathf.Cos(_phase), Vector3.zero);
                Vector3 target = root + stroke.ApplyDir(offset);
                Vector3 hint = Vector3.down - Vector3.right * 0.6f;
                Vector3 mid, end;
                CloudSeaKin.TwoBone(root, f.L1, f.L2, target, hint, out mid, out end);
                Rig.SetPose(f.Nodes[0], CloudSeaAffine.Segment(root, Rig.Rest[f.Nodes[1]] - root, root, mid - root));
                Rig.SetPose(f.Nodes[1],
                    CloudSeaAffine.Segment(Rig.Rest[f.Nodes[1]], Rig.Rest[f.Nodes[2]] - Rig.Rest[f.Nodes[1]], mid,
                        end - mid));
                Rig.SetPose(f.Nodes[2], Rig.Pose[f.Nodes[1]]);
            }

            // 后鳍：小幅打舵。
            float rudder = 0.30f * Mathf.Clamp(frame.YawRate, -1f, 1f);
            for (int side = 0; side < 2; side++)
            {
                int[] rear = _rear[side];
                Rig.SetLocal(rear[0], CloudSeaAffine.Rotation(Vector3.right, 0.22f * Mathf.Sin(_phase * 0.5f + 1f),
                    Rig.Rest[rear[0]]) * CloudSeaAffine.Rotation(Vector3.up, rudder, Rig.Rest[rear[0]]));
            }

            // 脖子：张望的目标慢慢游移；隔一阵缩一次脖子（目标往壳里收），脖子往下弯。
            float retractCycle = 11f;
            float rt = Mathf.Repeat(t + Seed * retractCycle, retractCycle);
            float retract = rt < 8f ? 0f : rt < 9.2f ? (rt - 8f) / 1.2f : rt < 9.8f ? 1f : 1f - (rt - 9.8f) / 1.2f;
            Vector3 gaze = new Vector3(
                0.14f * CloudSeaKin.Wobble(t * 0.6f, Seed) - 0.26f * retract,
                0.07f * CloudSeaKin.Wobble(t * 0.8f, Seed + 2f) - 0.10f * retract,
                0.16f * CloudSeaKin.Wobble(t * 0.45f, Seed + 4f));
            Vector3 neckRoot = Rig.Rest[_neck[0]];
            Vector3 target2 = Rig.Rest[_neck[2]] + gaze;
            Vector3 mid2, end2;
            CloudSeaKin.TwoBone(neckRoot, _len[0], _len[1], target2, Vector3.down, out mid2, out end2);
            Rig.SetPose(_neck[0], CloudSeaAffine.Segment(neckRoot, Rig.Rest[_neck[1]] - neckRoot, neckRoot,
                mid2 - neckRoot));
            Rig.SetPose(_neck[1],
                CloudSeaAffine.Segment(Rig.Rest[_neck[1]], Rig.Rest[_neck[2]] - Rig.Rest[_neck[1]], mid2, end2 - mid2));
            Rig.SetPose(_neck[2], Rig.Pose[_neck[1]]);
        }
    }

    // ----------------------------------------------------------------------
    // 海马：尾尖锚定，身体弯向张望的方向
    // ----------------------------------------------------------------------

    sealed class SeahorseBrain : CloudSeaBrain
    {
        readonly int[] _spine;
        readonly int _head, _fin;
        readonly Vector3[] _q, _hint;
        readonly float[] _len;

        public SeahorseBrain(CloudSeaRig rig, float seed) : base(rig, seed)
        {
            _spine = NeedChain("p");
            _head = rig["head"];
            _fin = rig["fin"];
            int n = _spine.Length;
            _q = new Vector3[n];
            _hint = new Vector3[n];
            _len = new float[n - 1];
            for (int k = 0; k < _len.Length; k++)
                _len[k] = (rig.Rest[_spine[k + 1]] - rig.Rest[_spine[k]]).magnitude;
        }

        public override void Step(float dt, in CloudSeaFrame frame)
        {
            Rig.Begin();
            float t = frame.Time;
            float bob = 0.035f * Mathf.Sin(t * 1.6f + Seed * CloudSeaKin.Tau);
            Vector3 target = Rig.Rest[_spine[_spine.Length - 1]]
                + new Vector3(
                    0.09f * CloudSeaKin.Wobble(t * 0.5f, Seed),
                    0.05f * CloudSeaKin.Wobble(t * 0.75f, Seed + 2f) + bob,
                    0.09f * CloudSeaKin.Wobble(t * 0.4f, Seed + 4f));
            // 尾尖钉住（p0），从静止体形出发求解：每一轮都被静止体形往回拉一点，所以身体保持卷曲的轮廓。
            Rig.RestChain(_spine, _q);
            Rig.RestChain(_spine, _hint);
            CloudSeaKin.Fabrik(_q, _spine.Length, _len, target, 6, _hint, 0.35f);
            Rig.WriteChainObject(_spine, _q);

            // 头：管吻慢慢扫过一片，偶尔抬头低头。
            Vector3 gaze = new Vector3(1f,
                0.35f * CloudSeaKin.Wobble(t * 0.9f, Seed + 9f),
                0.5f * CloudSeaKin.Wobble(t * 0.6f, Seed + 11f));
            float yaw = Mathf.Atan2(gaze.z, gaze.x);
            float pitch = Mathf.Atan2(gaze.y, new Vector2(gaze.x, gaze.z).magnitude);
            Vector3 pivot = Rig.Rest[_head];
            Rig.SetLocal(_head, CloudSeaAffine.Rotation(Vector3.up, -yaw * 0.8f, pivot)
                * CloudSeaAffine.Rotation(Vector3.forward, pitch * 0.8f, pivot));
            Rig.SetLocal(_fin, CloudSeaAffine.Rotation(Vector3.forward, 0.45f * Mathf.Sin(t * 9.5f), Rig.Rest[_fin]));
        }
    }
}
