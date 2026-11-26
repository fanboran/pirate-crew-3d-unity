using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>方向角（弧度）：按 Unity 偏航的约定，角 a 的方向 = (cos a, 0, -sin a)。</summary>
    static class CloudSeaAngle
    {
        public static float Delta(float from, float to)
        {
            return Mathf.Repeat(to - from + Mathf.PI, CloudSeaKin.Tau) - Mathf.PI;
        }

        public static float Turn(float from, float to, float maxStep)
        {
            return from + Mathf.Clamp(Delta(from, to), -maxStep, maxStep);
        }

        public static Vector3 Direction(float angle)
        {
            return new Vector3(Mathf.Cos(angle), 0f, -Mathf.Sin(angle));
        }
    }

    // ----------------------------------------------------------------------
    // 水母：铃体「发放—松弛」振荡器，触手是 Verlet 链
    // ----------------------------------------------------------------------

    /// <summary>
    /// 铃体是一块被周期性发放带动的肌肉：发放 0.2 秒、松弛一个周期，收缩量走弹簧（带一点过冲 = 回弹）。
    /// 收缩得越快推力越大，身体本身略重于水、又受水阻——所以每收缩一下往上窜一截、再慢慢沉，
    /// 整体停在巡游带里（离水面越近推得越轻）。升沉量交给 <c>CloudSeaLife</c> 挪对象。
    /// 触手 / 口腕是 Verlet 链：根钉在钟口上，其余质点靠惯性拖在后面——钟体上窜时触手张开，缓沉时收拢。
    /// </summary>
    sealed class JellyBrain : CloudSeaBrain
    {
        const float Sink = 0.12f;       // m/s²：净下沉加速度
        const float Drag = 1.2f;        // 1/s：水阻力
        const float Thrust = 0.42f;     // 每收缩一下给的速度增量（巡游带中部）

        sealed class Strand
        {
            public int[] Nodes;
            public CloudSeaStrand Chain;
            public float Seed;
        }

        readonly int _rim, _mid, _apex;
        readonly Strand[] _strands;
        CloudSeaSpring _bell;
        readonly float _period;
        readonly Vector3[] _start;
        float _drive;
        float _fireTimer;
        float _lastBell;
        float _altY;
        float _altV;
        bool _initialised;

        public JellyBrain(CloudSeaRig rig, float seed) : base(rig, seed)
        {
            _rim = rig["rim"];
            _mid = rig["mid"];
            _apex = rig["apex"];
            _bell = new CloudSeaSpring();
            _period = 2.4f + 1.2f * seed;
            _drive = CloudSeaKin.Frac(seed * 0.37f);
            var strands = new List<Strand>();
            AddStrands(rig, "t", strands);
            AddStrands(rig, "a", strands);
            _strands = strands.ToArray();
            _start = new Vector3[8];
        }

        void AddStrands(CloudSeaRig rig, string prefix, List<Strand> strands)
        {
            for (int i = 0; ; i++)
            {
                if (!rig.Has(prefix + i + "_0"))
                    break;
                int[] nodes = NeedChain(prefix + i + "_");
                var rest = new Vector3[nodes.Length];
                rig.RestChain(nodes, rest);
                strands.Add(new Strand
                {
                    Nodes = nodes,
                    Chain = new CloudSeaStrand(rest),
                    Seed = CloudSeaKin.Frac(Seed + i * 0.37f),
                });
            }
        }

        public override bool DrivesAltitude => true;

        /// <summary>对象原点该在的高度（父空间）：由发放与下沉推出来，<c>CloudSeaLife</c> 用它挪对象。</summary>
        public override float AltitudeY => _altY;

        float BandFraction(in CloudSeaFrame frame)
        {
            if (!frame.HasBounds)
                return 0.5f;
            return Mathf.Clamp01((_altY - frame.FloorY) / (frame.CeilingY - frame.FloorY));
        }

        public override void Step(float dt, in CloudSeaFrame frame)
        {
            Rig.Begin();
            if (!_initialised)
            {
                _altY = frame.Position.y;
                _initialised = true;
            }

            // 发放—松弛：慢变量攒满就发放一下。
            _drive += dt / _period;
            if (_fireTimer > 0f)
                _fireTimer -= dt;
            if (_drive >= 1f)
            {
                _drive = 0f;
                _fireTimer = 0.22f;
            }
            _bell.Step(_fireTimer > 0f ? 1f : 0f, dt, 1.4f, 0.35f);
            float c = Mathf.Clamp01(_bell.X);
            float rate = (c - _lastBell) / Mathf.Max(dt, 1e-4f);
            _lastBell = c;

            // 升沉
            float band = BandFraction(frame);
            _altV += (Thrust * (0.35f + 0.9f * (1f - band)) * Mathf.Max(0f, rate) - Sink) * dt
                - Drag * _altV * dt;
            _altY += _altV * dt;
            if (frame.HasBounds)
            {
                if (_altY < frame.FloorY)
                {
                    _altY = frame.FloorY;
                    _altV = Mathf.Max(0f, _altV);
                }
                else if (_altY > frame.CeilingY)
                {
                    _altY = frame.CeilingY;
                    _altV = Mathf.Min(0f, _altV);
                }
            }

            // 铃体：三层关节（钟口 / 中段 / 钟顶）各自缩放 + 上抬，顶点按高度在相邻两层间插值。
            float ratio = Rig.Rest[_apex].y / 0.52f;
            Vector3 axis = new Vector3(0f, Rig.Rest[_rim].y, 0f);
            Rig.SetLocal(_rim, CloudSeaAffine.Translation(Vector3.up * (0.17f * c * ratio))
                * CloudSeaAffine.Scale(new Vector3(1f - 0.28f * c, 1f, 1f - 0.28f * c), axis));
            Rig.SetLocal(_mid, CloudSeaAffine.Translation(Vector3.up * (0.09f * c * ratio))
                * CloudSeaAffine.Scale(new Vector3(1f - 0.16f * c, 1f, 1f - 0.16f * c), axis));
            Rig.SetLocal(_apex, CloudSeaAffine.Translation(Vector3.up * (0.05f * c * ratio))
                * CloudSeaAffine.Scale(new Vector3(1f - 0.06f * c, 1f, 1f - 0.06f * c), axis));

            // 触手 / 口腕
            float t = frame.Time;
            for (int s = 0; s < _strands.Length; s++)
            {
                Strand strand = _strands[s];
                int[] nodes = strand.Nodes;
                int n = nodes.Length;
                Vector3 root = frame.ToParent(Rig.Local[_rim].Apply(Rig.Rest[nodes[0]]));
                if (!_strandsReady)
                {
                    for (int k = 0; k < n; k++)
                        _start[k] = frame.ToParent(Rig.Rest[nodes[k]]);
                    strand.Chain.Reset(_start);
                }
                Vector3[] p = strand.Chain.P;
                Vector3[] acc = strand.Chain.Acc;
                for (int i = 1; i < n; i++)
                {
                    Vector3 restDir = CloudSeaKin.Unit(Rig.Rest[nodes[i]] - Rig.Rest[nodes[i - 1]], Vector3.down);
                    Vector3 ahead = i >= 2
                        ? CloudSeaKin.Unit(p[i - 1] - p[i - 2], frame.DirToParent(restDir))
                        : frame.DirToParent(restDir);
                    Vector3 flow = new Vector3(
                        0.12f * CloudSeaKin.Wobble(t * 0.4f + p[i].y * 1.5f, strand.Seed), 0f,
                        0.12f * CloudSeaKin.Wobble(t * 0.37f + 3f, strand.Seed + 5f));
                    Vector3 straight = p[i - 1] + ahead * strand.Chain.Len[i - 1];
                    acc[i] = Vector3.down * 0.9f + (flow - strand.Chain.Velocity(i, dt)) * 2.2f
                        + (straight - p[i]) * 6f;
                }
                strand.Chain.Integrate(dt, 1f);
                strand.Chain.Constrain(root, 2);
                Rig.WriteChainParent(nodes, p, frame);
            }
            _strandsReady = true;
        }

        bool _strandsReady;
    }

    // ----------------------------------------------------------------------
    // 章鱼：八条腕 FABRIK，腕尖踩地迈步，留一条在水里探
    // ----------------------------------------------------------------------

    /// <summary>
    /// 身体慢逛。八条腕是 FABRIK 链：腕尖踩在沙上（锚点），身体走远了才抬起来迈到前方的落点，相邻的腕不同时
    /// 抬；每轮迭代后把中间节点往「腕根—腕尖」连线上方的一条拱线拉，所以腕是拱着的、不是贴地拖一条直线，
    /// 节点不进沙。同一时间有一条腕不踩地，腕尖贴着沙面往前游移——伸出去探。
    /// </summary>
    sealed class OctopusBrain : CloudSeaBrain
    {
        const float Speed = 0.07f;
        const float Leash = 0.5f;
        const float StepDistance = 0.26f;

        sealed class Arm
        {
            public int[] Nodes;
            public float[] Len;
            public Vector3[] P, Hint;
            public Vector3 Out;
            public float Reach;
            public CloudSeaFoot Foot;
            public float Seed;
            public bool Exploring;
            public Vector3 Tip;
        }

        readonly int _body, _mantle;
        readonly Arm[] _arms;
        Vector3 _centre;
        float _heading;
        int _explorer = -1;
        float _explorerLeft;
        float _restLeft;
        int _picks;
        bool _initialised;

        public OctopusBrain(CloudSeaRig rig, float seed) : base(rig, seed)
        {
            _body = rig["body"];
            _mantle = rig["mantle"];
            var arms = new List<Arm>();
            for (int a = 0; ; a++)
            {
                if (!rig.Has("w" + a + "_0"))
                    break;
                int[] nodes = NeedChain("w" + a + "_");
                var arm = new Arm
                {
                    Nodes = nodes,
                    Len = new float[nodes.Length - 1],
                    P = new Vector3[nodes.Length],
                    Hint = new Vector3[nodes.Length],
                    Seed = CloudSeaKin.Frac(seed + a * 0.618034f),
                };
                for (int k = 0; k < arm.Len.Length; k++)
                    arm.Len[k] = (rig.Rest[nodes[k + 1]] - rig.Rest[nodes[k]]).magnitude;
                Vector3 span = rig.Rest[nodes[nodes.Length - 1]] - rig.Rest[nodes[0]];
                span.y = 0f;
                arm.Reach = span.magnitude;
                arm.Out = CloudSeaKin.Unit(span, new Vector3(1f, 0f, 0f));
                arms.Add(arm);
            }
            _arms = arms.ToArray();
            _heading = seed * CloudSeaKin.Tau;
        }

        bool NeighboursPlanted(int a)
        {
            int n = _arms.Length;
            if (n < 2)
                return true;
            Arm left = _arms[(a + 1) % n];
            Arm right = _arms[(a + n - 1) % n];
            return !left.Exploring && left.Foot.Planted && !right.Exploring && right.Foot.Planted;
        }

        public override void Step(float dt, in CloudSeaFrame frame)
        {
            Rig.Begin();
            float t = frame.Time;
            if (!_initialised)
            {
                _centre = frame.Position;
                for (int a = 0; a < _arms.Length; a++)
                {
                    Arm arm = _arms[a];
                    Vector3 home = Home(arm, frame, Vector3.zero);
                    arm.Foot = CloudSeaFoot.Plant(home);
                    arm.Tip = home;
                    for (int k = 0; k < arm.Nodes.Length; k++)
                        arm.P[k] = frame.ToParent(Rig.Rest[arm.Nodes[k]]);
                }
                _restLeft = 3f + 4f * Seed;
                _initialised = true;
            }

            // 身体慢逛：朝向慢慢漂，离锚点太远就往回拐。
            Vector3 fromHome = _centre - frame.Position;
            fromHome.y = 0f;
            if (fromHome.magnitude > Leash)
                _heading = CloudSeaAngle.Turn(_heading, Mathf.Atan2(-fromHome.z, -fromHome.x), 1.5f * dt);
            _heading += 0.6f * CloudSeaKin.Wobble(t * 0.23f, Seed) * dt;
            Vector3 velocity = CloudSeaAngle.Direction(_heading)
                * (Speed * (0.6f + 0.4f * CloudSeaKin.Wobble(t * 0.5f, Seed + 3f)));
            _centre += velocity * dt;
            _centre.y = frame.BedHeight(_centre.x, _centre.z);

            Rig.SetLocal(_body, CloudSeaAffine.Translation(frame.ToObject(_centre)));
            Rig.SetLocal(_mantle, CloudSeaAffine.Scale(
                new Vector3(1f, 1f, 1f) * (1f + 0.05f * Mathf.Sin(t * 2.8f + Seed * CloudSeaKin.Tau)),
                Rig.Rest[_mantle]));

            // 轮换探水的腕
            if (_explorerLeft > 0f)
            {
                _explorerLeft -= dt;
                if (_explorerLeft <= 0f)
                {
                    Arm done = _arms[_explorer];
                    done.Exploring = false;
                    done.Foot = CloudSeaFoot.Plant(done.Tip);
                    _explorer = -1;
                    _restLeft = 3f + 4f * CloudSeaKin.Frac(Seed * 7.3f + _picks * 0.381966f);
                }
            }
            else
            {
                _restLeft -= dt;
                if (_restLeft <= 0f && _arms.Length > 0)
                {
                    _picks++;
                    _explorer = (int)(CloudSeaKin.Frac(Seed * 5.1f + _picks * 0.618034f) * _arms.Length) % _arms.Length;
                    Arm arm = _arms[_explorer];
                    arm.Exploring = true;
                    arm.Tip = arm.Foot.Position;
                    _explorerLeft = 4f + 3f * arm.Seed;
                }
            }

            for (int a = 0; a < _arms.Length; a++)
                StepArm(_arms[a], a, dt, frame, velocity);
        }

        Vector3 Home(Arm arm, in CloudSeaFrame frame, Vector3 velocity)
        {
            Vector3 home = ArmBase(arm, frame) + frame.DirToParent(arm.Out) * arm.Reach + velocity * 1.5f;
            home.y = frame.BedHeight(home.x, home.z);
            return home;
        }

        Vector3 ArmBase(Arm arm, in CloudSeaFrame frame)
        {
            return _centre + frame.DirToParent(Rig.Rest[arm.Nodes[0]] - Rig.Rest[_body]);
        }

        void StepArm(Arm arm, int index, float dt, in CloudSeaFrame frame, Vector3 velocity)
        {
            float t = frame.Time;
            Vector3 baseP = ArmBase(arm, frame);
            if (arm.Exploring)
            {
                Vector3 sway = new Vector3(CloudSeaKin.Wobble(t * 0.9f, arm.Seed), 0f,
                    CloudSeaKin.Wobble(t * 0.7f, arm.Seed + 2f));
                // 探水的腕尖贴着沙面伸到几乎整条臂长，只抬离沙面一点——是伸出去摸，不是举过头顶。
                Vector3 want = baseP + frame.DirToParent(arm.Out) * (arm.Reach * 0.88f)
                    + Vector3.up * (0.08f + 0.04f * Mathf.Sin(t * 1.3f + arm.Seed * 6f)) + sway * 0.30f;
                want.y = Mathf.Max(want.y, frame.BedHeight(want.x, want.z) + 0.05f);
                arm.Tip += (want - arm.Tip) * Mathf.Min(1f, 3f * dt);
            }
            else
            {
                Vector3 home = Home(arm, frame, velocity);
                if (arm.Foot.Planted && (arm.Foot.Anchor - home).magnitude > StepDistance && NeighboursPlanted(index))
                    arm.Foot.Lift(home + velocity * 2f, 0.42f, 0.17f);      // 0.42 秒迈一步，腕尖最高抬 0.17
                arm.Foot.Step(dt);
                arm.Tip = arm.Foot.Position;
            }

            Vector3[] p = arm.P;
            p[0] = baseP;
            float arch = arm.Exploring ? 0.10f : arm.Foot.Planted ? 0.13f : 0.22f;
            int n = arm.Nodes.Length;
            for (int k = 1; k < n - 1; k++)
                arm.Hint[k] = Vector3.Lerp(baseP, arm.Tip, k / (float)n)
                    + Vector3.up * (arch * Mathf.Sin(Mathf.PI * k / (float)n));
            CloudSeaKin.Fabrik(p, n, arm.Len, arm.Tip, 4, arm.Hint, 0.30f);
            for (int k = 1; k < n; k++)
            {
                float floor = frame.BedHeight(p[k].x, p[k].z) + 0.03f;
                if (p[k].y < floor)
                    p[k].y = floor;
            }
            // 托出沙面之后节段长度变了：从根到梢再满足一遍。
            for (int k = 0; k < n - 1; k++)
                p[k + 1] = p[k] + CloudSeaKin.Unit(p[k + 1] - p[k], Vector3.up) * arm.Len[k];
            Rig.WriteChainParent(arm.Nodes, p, frame);
        }
    }

    // ----------------------------------------------------------------------
    // 蟹：八条腿两骨 IK，脚踩在锚点上，迈步由「锚点离落点多远」触发
    // ----------------------------------------------------------------------

    /// <summary>
    /// 每条腿的脚踩在沙上的一个锚点（世界里的点，身体怎么走都不动）。身体走动后锚点离「该在的位置」
    /// 越过阈值，这条腿才抬起来沿弧线迈到前方的新落点——同侧相邻的两条腿和另一侧的同序号腿都踩着才许抬，
    /// 所以是交替的步态，快慢跟着巡游速度自己变。腿用两骨解析 IK 解（脚踝的位置由落点加上静止时
    /// 「踝相对足尖」的偏移给出），膝盖朝外上鼓。
    /// 车身挂在弹簧上：高度跟脚下沙面，每次落脚被压一下；左右 / 前后脚下的沙面高度差让它侧倾 / 俯仰。
    /// 眼柄顺着速度往后倒一点（弹簧，有滞后）。挥钳的蟹腿不巡游，两只螯按节奏举起来挥（两骨 IK）。
    /// 对象不动：整只蟹的位移都在骨架里。
    /// </summary>
    sealed class CrabBrain : CloudSeaBrain
    {
        // 下面的长度、速度常数是按「身体离地 0.16 米的蟹」手调的；别的尺寸按 _size 等比换算，步频不变。
        const float ReferenceBodyHeight = 0.16f;
        const float DesiredSpeed = 0.22f;
        const float WeaveRatio = 0.16f;
        const float WeaveFreq = 2.3f;
        const float Leash = 0.9f;

        sealed class Leg
        {
            public int[] Nodes;
            public float L1, L2;
            public int Index, Side;
            public Vector3 FootOffset;
            public Vector3 AnkleDrop;
            public Vector3 KneeHint;
            public float Threshold;
            public CloudSeaFoot Foot;
        }

        readonly int _body, _eyeL, _eyeR;
        readonly Leg[] _legs;
        readonly int[][] _arms = new int[2][];
        readonly float[,] _armLen = new float[2, 2];
        readonly Vector3[] _chain = new Vector3[4];
        readonly Vector3 _along = new Vector3(0f, 0f, -1f);
        readonly Vector3 _across = new Vector3(1f, 0f, 0f);
        readonly float _phase2;
        CloudSeaSpring _suspension;
        CloudSeaSpring _leanX;
        CloudSeaSpring _leanZ;
        readonly bool _wave;
        readonly float _size;
        readonly int _frontLegs;
        float _u;
        float _roll, _pitch, _ground;
        bool _initialised;

        public CrabBrain(CloudSeaRig rig, float seed, bool wave) : base(rig, seed)
        {
            _wave = wave;
            _body = rig["body"];
            _size = Rig.Rest[_body].y / ReferenceBodyHeight;
            _eyeL = rig["eyeL"];
            _eyeR = rig["eyeR"];
            _arms[0] = NeedChain("aL_");
            _arms[1] = NeedChain("aR_");
            for (int s = 0; s < 2; s++)
                for (int k = 0; k < 2; k++)
                    _armLen[s, k] = (rig.Rest[_arms[s][k + 1]] - rig.Rest[_arms[s][k]]).magnitude;

            var legs = new List<Leg>();
            for (int side = 0; side < 2; side++)
            {
                string prefix = side == 0 ? "lL" : "lR";
                for (int i = 0; ; i++)
                {
                    if (!rig.Has(prefix + i + "_0"))
                        break;
                    int[] nodes = NeedChain(prefix + i + "_");
                    if (nodes.Length != 4)
                        throw new ArgumentException("[" + rig.Species + "] 腿链 " + prefix + i + " 必须是髋 / 膝 / 踝 / 尖四节。");
                    Vector3 footOffset = rig.Rest[nodes[3]] - rig.Rest[nodes[0]];
                    var leg = new Leg
                    {
                        Nodes = nodes,
                        Index = i,
                        Side = side == 0 ? 1 : -1,
                        L1 = (rig.Rest[nodes[1]] - rig.Rest[nodes[0]]).magnitude,
                        L2 = (rig.Rest[nodes[2]] - rig.Rest[nodes[1]]).magnitude,
                        FootOffset = footOffset,
                        AnkleDrop = rig.Rest[nodes[2]] - rig.Rest[nodes[3]],
                        KneeHint = CloudSeaKin.Unit(
                            (rig.Rest[nodes[1]] - rig.Rest[nodes[0]]) + footOffset * 0.5f, Vector3.up),
                        Threshold = _size * (0.12f + 0.12f * new Vector3(footOffset.x, 0f, footOffset.z).magnitude),
                    };
                    legs.Add(leg);
                }
            }
            if (legs.Count == 0)
                throw new ArgumentException("[" + rig.Species + "] 骨架里没有腿链 lL0_0、lR0_0…。");
            int perSide = 0;
            foreach (Leg leg in legs)
                perSide = Mathf.Max(perSide, leg.Index + 1);
            _frontLegs = (perSide + 1) / 2;
            _legs = legs.ToArray();
            _phase2 = seed * CloudSeaKin.Tau;
            _suspension = new CloudSeaSpring();
            _leanX = new CloudSeaSpring();
            _leanZ = new CloudSeaSpring();
            _u = seed * CloudSeaKin.Tau;
        }

        bool NeighboursPlanted(Leg leg)
        {
            foreach (Leg other in _legs)
            {
                if (other == leg || !other.Foot.Planted)
                    continue;
                bool sameSide = other.Side == leg.Side;
                if ((sameSide && Mathf.Abs(other.Index - leg.Index) == 1)
                    || (!sameSide && other.Index == leg.Index))
                    return false;
            }
            return true;
        }

        public override void Step(float dt, in CloudSeaFrame frame)
        {
            Rig.Begin();
            float t = frame.Time;
            int swinging = 0;
            foreach (Leg leg in _legs)
            {
                if (!leg.Foot.Planted)
                    swinging++;
            }

            // 脚该在的位置：髋的外侧（静止偏移收一成半）加上朝行进方向的提前量，落在沙面上。
            Vector3 LegHome(Leg leg, CloudSeaAffine bodyNow, in CloudSeaFrame frameNow, Vector3 velocityParentNow)
            {
                Vector3 hip = frameNow.ToParent(bodyNow.Apply(Rig.Rest[leg.Nodes[0]]));
                Vector3 home = hip + frameNow.DirToParent(bodyNow.ApplyDir(leg.FootOffset * 0.85f))
                    + velocityParentNow * 0.25f;
                home.y = frameNow.BedHeight(home.x, home.z);
                return home;
            }

            // 巡游：沿身体的侧向往复（横爬），带一点前后的蛇行。同时抬的腿太多就放慢，等腿跟上。
            Vector3 centreObj = Vector3.zero;
            Vector3 velocityObj = Vector3.zero;
            float wave = 0f;
            if (_wave)
            {
                wave = t * CloudSeaKin.Tau * 1.5f + Seed * CloudSeaKin.Tau;
                _roll = 0.05f * Mathf.Sin(wave);
            }
            else
            {
                // 长度与速度按体型换算：蟹整只等比缩放后，巡游半径、步幅、迈步高度跟着缩，步频不变。
                float leash = Leash * _size;
                float speed = DesiredSpeed * _size;
                float governor = swinging >= 4 ? 0.35f : 1f;
                _u += dt * speed * governor / leash;
                float along = Mathf.Sin(_u) * leash;
                centreObj = _along * along
                    + _across * (leash * WeaveRatio * Mathf.Sin(_u * WeaveFreq + _phase2));
                velocityObj = _along * (Mathf.Cos(_u) * speed * governor)
                    + _across * (WeaveRatio * WeaveFreq * Mathf.Cos(_u * WeaveFreq + _phase2) * speed
                        * governor);
            }

            // 车身：高度跟脚下的沙面（弹簧），左右 / 前后的沙面高差给侧倾 / 俯仰。
            Vector3 centreParent = frame.ToParent(centreObj);
            float ground = frame.BedHeight(centreParent.x, centreParent.z);
            if (!_initialised)
                _ground = ground;
            _ground += (ground - _ground) * Mathf.Min(1f, 8f * dt);
            _suspension.Step(0f, dt, 1.6f, 0.35f);
            centreParent.y = _ground + Rig.Rest[_body].y + _suspension.X;

            float left = 0f, right = 0f, front = 0f, back = 0f;
            int leftCount = 0, rightCount = 0, frontCount = 0, backCount = 0;
            foreach (Leg leg in _legs)
            {
                if (leg.Side > 0)
                {
                    left += leg.Foot.Position.y;
                    leftCount++;
                }
                else
                {
                    right += leg.Foot.Position.y;
                    rightCount++;
                }
                if (leg.Index < _frontLegs)
                {
                    front += leg.Foot.Position.y;
                    frontCount++;
                }
                else
                {
                    back += leg.Foot.Position.y;
                    backCount++;
                }
            }
            float width = 2f * _legs[0].Threshold;
            float leftMean = leftCount > 0 ? left / leftCount : right / Mathf.Max(1, rightCount);
            float rightMean = rightCount > 0 ? right / rightCount : left / Mathf.Max(1, leftCount);
            float frontMean = frontCount > 0 ? front / frontCount : back / Mathf.Max(1, backCount);
            float backMean = backCount > 0 ? back / backCount : front / Mathf.Max(1, frontCount);
            float rollTarget = -Mathf.Atan((leftMean - rightMean) / Mathf.Max(0.2f * _size, width));
            float pitchTarget = Mathf.Atan((frontMean - backMean) / Mathf.Max(0.2f * _size, width));
            if (!_wave)
                _roll = rollTarget * 0.7f;
            _pitch += (pitchTarget * 0.7f - _pitch) * Mathf.Min(1f, 6f * dt);

            // 车身矩阵把静止的支点送到现在的位置，再绕它侧倾 / 俯仰（先转后平移：平移量要扣掉支点）。
            Vector3 pivot = Rig.Rest[_body];
            CloudSeaAffine body = CloudSeaAffine.Translation(frame.ToObject(centreParent) - pivot)
                * CloudSeaAffine.Rotation(Vector3.up, 0.06f * Mathf.Sin(t * 0.7f + Seed * 6f), pivot)
                * CloudSeaAffine.Rotation(Vector3.right, _roll, pivot)
                * CloudSeaAffine.Rotation(Vector3.forward, _pitch, pivot);
            Rig.SetLocal(_body, body);

            Vector3 velocityParent = frame.DirToParent(velocityObj);
            if (!_initialised)
            {
                // 开局踩在收拢一成半的落点上（静止姿势的足尖离髋几乎是腿长的极限，一晃就够不着）。
                foreach (Leg leg in _legs)
                    leg.Foot = CloudSeaFoot.Plant(LegHome(leg, body, frame, Vector3.zero));
            }

            // 腿
            foreach (Leg leg in _legs)
            {
                Vector3 hip = frame.ToParent(body.Apply(Rig.Rest[leg.Nodes[0]]));
                Vector3 home = LegHome(leg, body, frame, velocityParent);
                // 踝的目标 = 脚的位置 + 静止时「踝相对足尖」的偏移（跗节保持朝向）。
                Vector3 ankleDrop = frame.DirToParent(body.ApplyDir(leg.AnkleDrop));
                // 够不着了（腿伸直也到不了锚点）就必须迈，不等邻居；否则只是离落点远了才迈。
                bool outOfReach = (leg.Foot.Position + ankleDrop - hip).magnitude
                    > (leg.L1 + leg.L2) * 0.97f;
                if (leg.Foot.Planted && (outOfReach
                    || ((leg.Foot.Anchor - home).magnitude > leg.Threshold && swinging < 4
                        && NeighboursPlanted(leg))))
                {
                    // 0.10 秒迈一步。够不着的腿贴着沙面赶紧拽回来，平时抬 0.08——螃蟹迈的是小碎步。
                    leg.Foot.Lift(home + velocityParent * 0.35f, 0.10f, (outOfReach ? 0.05f : 0.08f) * _size);
                    swinging++;
                }
                bool wasPlanted = leg.Foot.Planted;
                leg.Foot.Step(dt);
                if (!wasPlanted && leg.Foot.Planted)
                    _suspension.Impulse(-0.10f * _size);   // 落脚：车身被压一下

                Vector3 ankleTarget = leg.Foot.Position + ankleDrop;
                // 膝盖朝外上：向上的分量小一点，膝才不会冒出壳顶成一排「手指」。
                Vector3 hint = frame.DirToParent(body.ApplyDir(leg.KneeHint)) + Vector3.up * 0.5f;
                Vector3 knee, ankle;
                CloudSeaKin.TwoBone(hip, leg.L1, leg.L2, ankleTarget, hint, out knee, out ankle);
                _chain[0] = hip;
                _chain[1] = knee;
                _chain[2] = ankle;
                _chain[3] = leg.Foot.Position;
                Rig.WriteChainParent(leg.Nodes, _chain, frame);
            }

            // 螯：两骨 IK。横爬的蟹把螯端在身前随车身轻晃；挥钳的蟹两只螯一上一下按节奏举。
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? 1f : -1f;
                int[] arm = _arms[s];
                Vector3 shoulder = frame.ToParent(body.Apply(Rig.Rest[arm[0]]));
                Vector3 carry;
                if (_wave)
                {
                    float phase = wave + (s == 0 ? 0f : Mathf.PI);
                    carry = _size * new Vector3(0.05f * Mathf.Sin(phase), 0.24f + 0.20f * Mathf.Sin(phase),
                        side * 0.06f * Mathf.Cos(phase));
                }
                else
                {
                    carry = _size * new Vector3(0.02f * CloudSeaKin.Wobble(t * 1.3f, Seed + s), 0.02f,
                        0.02f * CloudSeaKin.Wobble(t * 0.9f, Seed + s + 2f));
                }
                Vector3 target = frame.ToParent(body.Apply(Rig.Rest[arm[2]] + carry));
                Vector3 hint = frame.DirToParent(body.ApplyDir(new Vector3(0f, -0.6f, 0.9f * side)));
                Vector3 elbow, claw;
                CloudSeaKin.TwoBone(shoulder, _armLen[s, 0], _armLen[s, 1], target, hint, out elbow, out claw);
                _chain[0] = shoulder;
                _chain[1] = elbow;
                _chain[2] = claw;
                Rig.WriteChainParent(arm, _chain, frame);
            }

            // 眼柄：顺着速度往后倒（弹簧，有滞后）。速度随体型缩，除回去让倒的角度不随大小变。
            _leanX.Step(-velocityObj.z * 0.8f / _size, dt, 1.2f, 0.5f);
            _leanZ.Step(velocityObj.x * 0.8f / _size, dt, 1.2f, 0.5f);
            Rig.SetLocal(_eyeL, CloudSeaAffine.Rotation(Vector3.right, _leanX.X, Rig.Rest[_eyeL])
                * CloudSeaAffine.Rotation(Vector3.forward, _leanZ.X, Rig.Rest[_eyeL]));
            Rig.SetLocal(_eyeR, CloudSeaAffine.Rotation(Vector3.right, _leanX.X, Rig.Rest[_eyeR])
                * CloudSeaAffine.Rotation(Vector3.forward, _leanZ.X, Rig.Rest[_eyeR]));
            _initialised = true;
        }
    }

    // ----------------------------------------------------------------------
    // 海星：五条腕两骨 IK，腕尖依次踩地，腕在沙面平面里拖后弯
    // ----------------------------------------------------------------------

    /// <summary>
    /// 中心慢逛。五条腕的尖各踩在一个锚点上；锚点离「该在的位置」过了阈值就抬起来（又慢又低的一小步）
    /// 迈到前方。阈值按一条慢转的步态时钟绕着五条腕轮一圈——腕一条接一条地迈，不是同时。
    /// 腕用两骨 IK 解，关节弯向**行进方向的反侧**、只略微离沙：踩着的腕尖不动、中心往前走，腕就在沙面
    /// 平面里被拖弯；迈一步伸出去又拉直——读作一条弯沿着五条腕轮着走，海星翻着往前挪。
    /// 不让它往上拱：两骨链只要有余量，「向上」的提示经取单位向量就是一整截，俯视下读成一只撑在地上的手。
    /// </summary>
    sealed class StarBrain : CloudSeaBrain
    {
        const float Speed = 0.05f;
        const float Leash = 0.35f;
        const float SwingDuration = 0.30f;   // 迈一步的时长
        const float SwingLift = 0.05f;       // 迈步时腕尖最多抬多高

        sealed class Arm
        {
            public int[] Nodes;
            public float L1, L2;
            public Vector3 Out;
            public float Reach;
            public CloudSeaFoot Foot;
        }

        readonly int _centre;
        readonly Arm[] _arms;
        readonly Vector3[] _q = new Vector3[3];
        Vector3 _centrePos;
        float _heading;
        float _gait;
        bool _initialised;

        public StarBrain(CloudSeaRig rig, float seed) : base(rig, seed)
        {
            _centre = rig["centre"];
            var arms = new List<Arm>();
            for (int a = 0; ; a++)
            {
                if (!rig.Has("w" + a + "_0"))
                    break;
                int[] nodes = NeedChain("w" + a + "_");
                if (nodes.Length != 3)
                    throw new ArgumentException("[" + rig.Species + "] 腕链 w" + a + "_ 必须是根 / 中 / 尖三节。");
                Vector3 span = rig.Rest[nodes[2]] - rig.Rest[nodes[0]];
                span.y = 0f;
                arms.Add(new Arm
                {
                    Nodes = nodes,
                    L1 = (rig.Rest[nodes[1]] - rig.Rest[nodes[0]]).magnitude,
                    L2 = (rig.Rest[nodes[2]] - rig.Rest[nodes[1]]).magnitude,
                    Out = CloudSeaKin.Unit(span, Vector3.right),
                    Reach = span.magnitude,
                });
            }
            _arms = arms.ToArray();
            _heading = seed * CloudSeaKin.Tau;
        }

        public override void Step(float dt, in CloudSeaFrame frame)
        {
            Rig.Begin();
            float t = frame.Time;
            if (!_initialised)
            {
                _centrePos = frame.Position;
                _centrePos.y = frame.BedHeight(_centrePos.x, _centrePos.z);
                foreach (Arm arm in _arms)
                {
                    Vector3 home = _centrePos + frame.DirToParent(arm.Out) * arm.Reach;
                    home.y = frame.BedHeight(home.x, home.z);
                    arm.Foot = CloudSeaFoot.Plant(home);
                }
                _initialised = true;
            }

            Vector3 fromHome = _centrePos - frame.Position;
            fromHome.y = 0f;
            if (fromHome.magnitude > Leash)
                _heading = CloudSeaAngle.Turn(_heading, Mathf.Atan2(-fromHome.z, -fromHome.x), 1.2f * dt);
            _heading += 0.8f * CloudSeaKin.Wobble(t * 0.21f, Seed) * dt;
            Vector3 velocity = CloudSeaAngle.Direction(_heading) * Speed;
            _centrePos += velocity * dt;
            _centrePos.y = frame.BedHeight(_centrePos.x, _centrePos.z);
            _gait += dt * 1.6f;

            Vector3 back = velocity.sqrMagnitude > 1e-8f ? -velocity.normalized : Vector3.zero;
            int n = _arms.Length;
            for (int a = 0; a < n; a++)
            {
                Arm arm = _arms[a];
                Vector3 outP = frame.DirToParent(arm.Out);
                // 落点收在臂长的八成：腕尖离中心近了腕才弯得起来，也不会因为够不着而拖着走。
                Vector3 home = _centrePos + outP * (arm.Reach * 0.80f) + velocity * 0.6f;
                home.y = frame.BedHeight(home.x, home.z);
                float threshold = 0.15f * (1f + 0.5f * Mathf.Cos(_gait - a * CloudSeaKin.Tau / n));
                bool neighbours = n < 2 || (_arms[(a + 1) % n].Foot.Planted && _arms[(a + n - 1) % n].Foot.Planted);
                bool outOfReach = (arm.Foot.Anchor - _centrePos).magnitude > (arm.L1 + arm.L2) * 0.97f;
                if (arm.Foot.Planted && (outOfReach
                    || ((arm.Foot.Anchor - home).magnitude > threshold && neighbours)))
                    arm.Foot.Lift(home, SwingDuration, SwingLift);
                arm.Foot.Step(dt);

                // 在沙面平面里弯：侧向取「垂直于这条腕」的水平方向，符号让它倒向行进方向的反侧（被拖着走），
                // 只带很小一点向上。侧向必须逐腕取——拿固定方向做提示，和它平行的那条腕取完垂直分量只剩向上。
                Vector3 along = arm.Foot.Position - _centrePos;
                along.y = 0f;
                Vector3 side = new Vector3(-along.z, 0f, along.x);
                side = side.sqrMagnitude > 1e-8f ? side.normalized : outP;
                if (Vector3.Dot(side, back) < 0f)
                    side = -side;
                Vector3 hint = side + Vector3.up * 0.22f;
                Vector3 mid, end;
                CloudSeaKin.TwoBone(_centrePos, arm.L1, arm.L2, arm.Foot.Position, hint, out mid, out end);
                _q[0] = frame.ToObject(_centrePos);
                _q[1] = frame.ToObject(mid);
                _q[2] = frame.ToObject(arm.Foot.Position);
                Rig.WriteChainObject(arm.Nodes, _q);
            }
            Rig.SetLocal(_centre, CloudSeaAffine.Translation(frame.ToObject(_centrePos)));
        }
    }

    // ----------------------------------------------------------------------
    // 海葵：触手是 Verlet 链，随水流摆
    // ----------------------------------------------------------------------

    /// <summary>
    /// 每根触手是一条 Verlet 链：根钉在基座上，受浮力（向上站）、被水流的阻力推着走、又被「顺着上一节
    /// 直着长」的回复力拉回。水流是慢慢游移的场，所以整丛触手各摆各的。
    /// </summary>
    sealed class AnemoneBrain : CloudSeaBrain
    {
        sealed class Frond
        {
            public int[] Nodes;
            public CloudSeaStrand Chain;
            public float Seed;
        }

        readonly Frond[] _fronds;
        bool _initialised;

        public AnemoneBrain(CloudSeaRig rig, float seed) : base(rig, seed)
        {
            var fronds = new List<Frond>();
            for (int i = 0; ; i++)
            {
                if (!rig.Has("f" + i + "_0"))
                    break;
                int[] nodes = NeedChain("f" + i + "_");
                var rest = new Vector3[nodes.Length];
                rig.RestChain(nodes, rest);
                fronds.Add(new Frond
                {
                    Nodes = nodes,
                    Chain = new CloudSeaStrand(rest),
                    Seed = CloudSeaKin.Frac(seed + i * 0.381966f),
                });
            }
            _fronds = fronds.ToArray();
        }

        public override void Step(float dt, in CloudSeaFrame frame)
        {
            Rig.Begin();
            float t = frame.Time;
            for (int i = 0; i < _fronds.Length; i++)
            {
                Frond frond = _fronds[i];
                int[] nodes = frond.Nodes;
                int n = nodes.Length;
                Vector3 root = frame.ToParent(Rig.Rest[nodes[0]]);
                if (!_initialised)
                {
                    for (int k = 0; k < n; k++)
                        frond.Chain.P[k] = frame.ToParent(Rig.Rest[nodes[k]]);
                    frond.Chain.Reset(frond.Chain.P);
                }
                Vector3[] p = frond.Chain.P;
                Vector3[] acc = frond.Chain.Acc;
                for (int k = 1; k < n; k++)
                {
                    Vector3 restDir = CloudSeaKin.Unit(Rig.Rest[nodes[k]] - Rig.Rest[nodes[k - 1]], Vector3.up);
                    Vector3 ahead = k >= 2
                        ? CloudSeaKin.Unit(p[k - 1] - p[k - 2], frame.DirToParent(restDir))
                        : frame.DirToParent(restDir);
                    Vector3 flow = new Vector3(
                        0.22f * CloudSeaKin.Wobble(t * 0.5f + p[k].y * 2f, frond.Seed), 0f,
                        0.22f * CloudSeaKin.Wobble(t * 0.43f + 7f, frond.Seed + 5f));
                    Vector3 straight = p[k - 1] + ahead * frond.Chain.Len[k - 1];
                    acc[k] = Vector3.up * 0.5f + (flow - frond.Chain.Velocity(k, dt)) * 2.5f
                        + (straight - p[k]) * 18f;
                }
                frond.Chain.Integrate(dt, 1f);
                frond.Chain.Constrain(root, 2);
                Rig.WriteChainParent(nodes, p, frame);
            }
            _initialised = true;
        }
    }

    /// <summary>没有控制器的生物：骨架停在静止姿态。</summary>
    sealed class StillBrain : CloudSeaBrain
    {
        public StillBrain(CloudSeaRig rig, float seed) : base(rig, seed)
        {
        }

        public override void Step(float dt, in CloudSeaFrame frame)
        {
            Rig.Begin();
        }
    }
}
