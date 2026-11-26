using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>海床高度采样：(x, z) → 沙面高度，父空间（CloudWalk 根的本地系）。</summary>
    public delegate float CloudSeaBed(float x, float z);

    /// <summary>
    /// 一步模拟的输入：对象在父空间里的位姿与速度。父空间 = CloudWalk 根的本地系，竖直是 +Y。
    /// 物体空间的约定见 <see cref="CloudSeaRig"/>。
    /// </summary>
    public struct CloudSeaFrame
    {
        public float Time;
        /// <summary>对象原点（父空间）。</summary>
        public Vector3 Position;
        /// <summary>物体 → 父 的旋转。</summary>
        public CloudSeaAffine Rotation;
        /// <summary>对象速度（父空间）。</summary>
        public Vector3 Velocity;
        /// <summary>偏航角速度（弧度/秒；Unity 正向 = 俯视顺时针，即右转）。</summary>
        public float YawRate;
        /// <summary>海床高度采样；null = 没有高度格。</summary>
        public CloudSeaBed Bed;
        /// <summary>对象原点允许的高度范围（父空间）；CeilingY 低于 FloorY 表示不设限。</summary>
        public float FloorY;
        public float CeilingY;

        public Vector3 ToParent(Vector3 p)
        {
            return Rotation.Apply(p) + Position;
        }

        public Vector3 ToObject(Vector3 p)
        {
            return Rotation.Transposed.Apply(p - Position);
        }

        public Vector3 DirToParent(Vector3 d)
        {
            return Rotation.ApplyDir(d);
        }

        public Vector3 DirToObject(Vector3 d)
        {
            return Rotation.Transposed.ApplyDir(d);
        }

        public float BedHeight(float x, float z)
        {
            return Bed != null ? Bed(x, z) : 0f;
        }

        public bool HasBounds => CeilingY > FloorY;
    }

    /// <summary>
    /// 一只生物的骨架：Blender 侧建关节（creature_kit.py），随 FBX 以空物体 <c>J&lt;编号&gt;_&lt;角色&gt;</c>
    /// 进来，这里记下每个关节的静止位置、父关节与它在蒙皮缓冲里的槽位。
    ///
    /// 【物体空间】x = 前、y = 上、z = 侧（左），单位 = 米。导出时轴向转换烘进了顶点，对象变换只剩偏航。
    ///
    /// 【两种驱动方式】每帧先 <see cref="Begin"/>，然后：
    ///   · 求解器直接给关节写绝对矩阵（<see cref="SetPose"/>）——链上的节点都是这样，父关节记 -1；
    ///   · 挂在别的关节上的小件（鳍、眼柄、刺）只写自己的局部形变（<see cref="SetLocal"/>），
    ///     <see cref="Compose"/> 时乘上父关节的矩阵。
    /// 关节按「父在子前」的顺序登记，所以一遍顺序遍历就能合成。
    /// </summary>
    public sealed class CloudSeaRig
    {
        public readonly string Species;
        public readonly int Count;
        /// <summary>关节 → 蒙皮缓冲里的关节号。</summary>
        public readonly int[] Slot;
        /// <summary>关节的角色名（与 <see cref="Rest"/> 同序）。</summary>
        public readonly string[] Roles;
        /// <summary>关节静止位置（物体空间）。</summary>
        public readonly Vector3[] Rest;
        /// <summary>父关节；-1 = 没有父关节（由求解器直接驱动）。</summary>
        public readonly int[] Parent;
        public readonly CloudSeaAffine[] Local;
        public readonly CloudSeaAffine[] Pose;
        readonly bool[] _driven;
        readonly Dictionary<string, int> _byRole;

        public CloudSeaRig(string species, string[] roles, Vector3[] rest, int[] parent, int[] slot)
        {
            if (roles.Length != rest.Length || roles.Length != parent.Length || roles.Length != slot.Length)
                throw new ArgumentException("骨架的角色 / 位置 / 父关节 / 槽位长度不一致");

            Species = species;
            Count = roles.Length;
            Roles = (string[])roles.Clone();
            Rest = rest;
            Parent = parent;
            Slot = slot;
            Local = new CloudSeaAffine[Count];
            Pose = new CloudSeaAffine[Count];
            _driven = new bool[Count];
            _byRole = new Dictionary<string, int>(Count, StringComparer.Ordinal);
            for (int j = 0; j < Count; j++)
            {
                _byRole.Add(roles[j], j);
                if (parent[j] >= j)
                    throw new ArgumentException("关节 " + roles[j] + " 的父关节必须登记在它之前");
            }
            Q = new Vector3[Count];
        }

        /// <summary>物体空间的节点位置暂存（写链时用）。</summary>
        public readonly Vector3[] Q;


        public int this[string role]
        {
            get { return _byRole[role]; }
        }

        public bool Has(string role)
        {
            return _byRole.ContainsKey(role);
        }

        public bool TryGet(string role, out int joint)
        {
            return _byRole.TryGetValue(role, out joint);
        }

        /// <summary>按 <c>前缀 + 0, 1, 2…</c> 取一条链的关节号，取到第一个缺失的序号为止。</summary>
        public int[] ChainRoles(string prefix)
        {
            var list = new List<int>();
            for (int k = 0; ; k++)
            {
                int joint;
                if (!_byRole.TryGetValue(prefix + k, out joint))
                    break;
                list.Add(joint);
            }
            return list.ToArray();
        }

        /// <summary>清掉本帧的形变：局部与绝对矩阵回到静止。</summary>
        public void Begin()
        {
            for (int j = 0; j < Count; j++)
            {
                Local[j] = CloudSeaAffine.Identity;
                Pose[j] = CloudSeaAffine.Identity;
                _driven[j] = false;
            }
        }

        /// <summary>直接给关节写绝对矩阵（物体空间）。</summary>
        public void SetPose(int joint, CloudSeaAffine pose)
        {
            Pose[joint] = pose;
            _driven[joint] = true;
        }

        /// <summary>写关节自己的局部形变（绕自己的静止位置转 / 缩放，再平移）。</summary>
        public void SetLocal(int joint, CloudSeaAffine local)
        {
            Local[joint] = local;
            _driven[joint] = false;
        }

        /// <summary>合成全部关节并写进蒙皮缓冲：每个关节占三行。</summary>
        public void Compose(Vector4[] buffer)
        {
            for (int j = 0; j < Count; j++)
            {
                int parent = Parent[j];
                if (parent >= 0)
                    Pose[j] = Pose[parent] * Local[j];
                else if (!_driven[j])
                    Pose[j] = Local[j];
                int row = Slot[j] * 3;
                buffer[row] = Pose[j].R0;
                buffer[row + 1] = Pose[j].R1;
                buffer[row + 2] = Pose[j].R2;
            }
        }

        /// <summary>把一条链在父空间里的节点位置写成分段的骨骼矩阵。</summary>
        public void WriteChainParent(int[] nodes, Vector3[] p, in CloudSeaFrame frame)
        {
            for (int k = 0; k < nodes.Length; k++)
                Q[k] = frame.ToObject(p[k]);
            WriteChainObject(nodes, Q);
        }

        /// <summary>把一条链在物体空间里的节点位置写成分段的骨骼矩阵；末节点跟着最后一节。</summary>
        public void WriteChainObject(int[] nodes, Vector3[] q)
        {
            int last = nodes.Length - 1;
            for (int k = 0; k < last; k++)
            {
                int a = nodes[k], b = nodes[k + 1];
                SetPose(a, CloudSeaAffine.Segment(Rest[a], Rest[b] - Rest[a], q[k], q[k + 1] - q[k]));
            }
            SetPose(nodes[last], Pose[nodes[last - 1]]);
        }

        /// <summary>静止状态下这条链的节点位置（物体空间），写进 q。</summary>
        public void RestChain(int[] nodes, Vector3[] q)
        {
            for (int k = 0; k < nodes.Length; k++)
                q[k] = Rest[nodes[k]];
        }
    }

    /// <summary>
    /// 追随链：头两节钉在头架上（头架带一点摆头），其余每个节点被自己上一帧的位置拖着走——
    /// 身体只允许相对前一节弯 <see cref="MaxBend"/>，再叠一道从头往尾走的尾波（<see cref="Wave"/>）。
    /// 头拐弯，身后就弯进弯里；头摆动，波就顺着身体传下去。鱼身、鱼群里的个体用它。
    /// 位置在父空间里记（头真的在世界里走），写出时换回物体空间。
    /// </summary>
    public sealed class CloudSeaFollowChain
    {
        public readonly int[] Nodes;
        public readonly float[] Len;
        public readonly Vector3[] P;
        public readonly float[] MaxBend;
        public readonly float[] Wave;
        public float Phase;
        public float PrevPhase;
        /// <summary>尾波每往后一节滞后的相位（弧度）。</summary>
        public float Lag = 0.9f;
        public float WaveScale = 1f;

        public CloudSeaFollowChain(CloudSeaRig rig, int[] nodes)
        {
            Nodes = nodes;
            int n = nodes.Length;
            Len = new float[n - 1];
            MaxBend = new float[n - 1];
            Wave = new float[n - 1];
            for (int k = 0; k < Len.Length; k++)
            {
                Len[k] = (rig.Rest[nodes[k + 1]] - rig.Rest[nodes[k]]).magnitude;
                MaxBend[k] = 0.5f;
                Wave[k] = 0.15f;
            }
            P = new Vector3[n];
        }

        public int Count => Nodes.Length;

        /// <summary>把节点摆回静止位置（父空间）。</summary>
        public void Reset(CloudSeaRig rig, in CloudSeaFrame frame)
        {
            for (int k = 0; k < Nodes.Length; k++)
                P[k] = frame.ToParent(rig.Rest[Nodes[k]]);
            Phase = 0f;
            PrevPhase = 0f;
        }

        /// <param name="head0">头节点的位置（父空间）。</param>
        /// <param name="forward">头架朝向（父空间单位向量）。</param>
        /// <param name="headAmplitude">摆头幅度（弧度）。</param>
        public void Solve(in CloudSeaFrame frame, Vector3 head0, Vector3 forward, float headAmplitude)
        {
            Vector3 up = frame.DirToParent(Vector3.up);
            Vector3 side = frame.DirToParent(Vector3.forward);
            P[0] = head0;
            // 节点从头排到尾：第 0 节指向身后，摆头是绕上轴在前后方向上小幅左右拧。
            Vector3 dir = CloudSeaKin.Rotate(-forward, up, headAmplitude * Mathf.Sin(Phase));
            P[1] = P[0] + dir * Len[0];
            Vector3 parentDir = dir;
            int last = Nodes.Length - 1;
            for (int k = 1; k < last; k++)
            {
                Vector3 d = CloudSeaKin.Unit(P[k + 1] - P[k], parentDir);
                // 上一帧的尾波先撤掉（节点位置里带着它），夹完弯角再叠这一帧的。
                float oldPhase = PrevPhase - k * Lag;
                float nowPhase = Phase - k * Lag;
                float amp = Wave[k] * WaveScale;
                d = CloudSeaKin.Rotate(d, up, -amp * Mathf.Sin(oldPhase));
                d = CloudSeaKin.Rotate(d, side, -amp * 0.3f * Mathf.Cos(oldPhase));
                d = CloudSeaKin.ClampBend(parentDir, d, MaxBend[k]);
                d = CloudSeaKin.Rotate(d, up, amp * Mathf.Sin(nowPhase));
                d = CloudSeaKin.Rotate(d, side, amp * 0.3f * Mathf.Cos(nowPhase));
                d = CloudSeaKin.Unit(d, parentDir);
                P[k + 1] = P[k] + d * Len[k];
                parentDir = d;
            }
            PrevPhase = Phase;
        }

        public void Write(CloudSeaRig rig, in CloudSeaFrame frame)
        {
            rig.WriteChainParent(Nodes, P, frame);
        }
    }
}
