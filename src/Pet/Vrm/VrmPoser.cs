using Godot;
using JustNeko.Pet.Rig;

namespace JustNeko.Pet.Vrm;

/// <summary>
/// 每帧直接改骨骼姿态：待机时手臂自然垂下、呼吸起伏；通用层的看向、倾斜叠加在身体、头颈和眼睛上。
/// 用 Godot 的人形骨骼名（VRM 导入时已经重定向好），换别的 VRM 角色也通用。
/// </summary>
/// <remarks>
/// 不用 SkeletonModifier3D：它的结果只用于渲染、不写回姿态，VRM 的约束（袖子的扭转骨骼）和弹簧骨骼读不到，
/// 袖子会留在 T 字姿势。这里在角色节点的 _Process 里写，Godot 先处理父节点，约束和弹簧骨骼随后就能读到。
/// 旋转都在骨架空间里做：VRM 角色面朝 +Z（朝向相机），角色的左手在 +X。
/// </remarks>
internal sealed class VrmPoser
{
    private const float ArmDown = 74f;
    private const float BreathPeriod = 4.2f;
    // 父骨骼在前，保证先改父再改子。
    private static readonly string[] Order = ["Spine", "Chest", "UpperChest", "Neck", "Head", "LeftEye", "RightEye", "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm"];
    private readonly Skeleton3D _skeleton;
    private readonly List<(string Name, int Index)> _bones = [];
    private readonly Dictionary<string, List<int>> _followers = [];
    private double _time;

    public VrmPoser(Skeleton3D skeleton)
    {
        _skeleton = skeleton;
        foreach (var name in Order)
        {
            var index = skeleton.FindBone(name);
            if (index >= 0)
                _bones.Add((name, index));
        }
        // 有的模型（比如带约束的 VRoid 模型）把袖子、肩部扭转骨骼和上臂并排挂在肩膀下，本来靠约束跟着上臂转。
        // godot-vrm 对这类约束的导入和计算不可靠（会把袖子算回水平），VrmAvatar 会停用约束节点，
        // 这里直接让它们和上臂一起转，袖子就会贴着手臂放下来。
        foreach (var arm in new[] { "LeftUpperArm", "RightUpperArm" })
        {
            var index = skeleton.FindBone(arm);
            if (index < 0)
                continue;
            var parent = skeleton.GetBoneParent(index);
            var armDirection = RestDirection(skeleton, index);
            // 只带走和上臂朝向一致的骨骼（袖子、扭转骨骼），挂在同一个肩膀下的其他东西（比如背包上的机械臂）不动。
            _followers[arm] = Enumerable.Range(0, skeleton.GetBoneCount())
                .Where(bone => bone != index && parent >= 0 && skeleton.GetBoneParent(bone) == parent && !_bones.Any(known => known.Index == bone)
                               && RestDirection(skeleton, bone).Dot(armDirection) > 0.8f)
                .ToList();
        }
    }

    public AvatarPose Pose { get; set; } = AvatarPose.Rest;

    public void Update(double delta)
    {
        _time += delta;
        foreach (var index in _bones.Select(bone => bone.Index).Concat(_followers.Values.SelectMany(list => list)))
            _skeleton.SetBonePoseRotation(index, _skeleton.GetBoneRest(index).Basis.GetRotationQuaternion());

        var look = Pose.Look;
        var lean = Pose.Lean;
        var breath = Mathf.Sin((float)_time * Mathf.Tau / BreathPeriod);
        foreach (var (name, index) in _bones)
        {
            var rotation = name switch
            {
                // 身体：看向时跟着转一点；拖动时侧倾、前后仰；呼吸时胸口轻微起伏。
                "Spine" => Yaw(look.X * 6) * Roll(-lean.X * 6) * Pitch(lean.Y * 5),
                "Chest" => Pitch(breath * 1.2f) * Roll(-lean.X * 5),
                // 头颈：看向光标，拖动时头也甩一下。
                "Neck" => Yaw(look.X * 12) * Pitch(-look.Y * 7) * Roll(-lean.X * 4),
                "Head" => Yaw(look.X * 16) * Pitch(-look.Y * 10) * Roll(-lean.X * 6),
                "LeftEye" or "RightEye" => Yaw(look.X * 10) * Pitch(-look.Y * 8),
                // 手臂：从 T 字姿势放下来、稍微往前收，随呼吸轻轻动；小臂绕左右轴往前弯一点（手臂垂下后再绕竖直轴转会拧成手心朝外）。
                "LeftUpperArm" => Roll(-ArmDown + breath * 1.5f) * Yaw(-10),
                "RightUpperArm" => Roll(ArmDown - breath * 1.5f) * Yaw(10),
                "LeftLowerArm" or "RightLowerArm" => Pitch(-18),
                _ => Basis.Identity,
            };
            Rotate(index, rotation);
            foreach (var follower in _followers.GetValueOrDefault(name) ?? [])
                Rotate(follower, rotation);
        }
    }

    private void Rotate(int index, Basis rotation)
    {
        var pose = _skeleton.GetBoneGlobalPose(index);
        _skeleton.SetBoneGlobalPose(index, new Transform3D(rotation * pose.Basis, pose.Origin));
    }

    // 骨骼在静止姿态下的朝向：从自己指向第一个子骨骼；没有子骨骼时返回零向量（不会被当成跟随骨骼）。
    private static Vector3 RestDirection(Skeleton3D skeleton, int bone)
    {
        var children = skeleton.GetBoneChildren(bone);
        if (children.Length == 0)
            return Vector3.Zero;
        var direction = skeleton.GetBoneGlobalRest(children[0]).Origin - skeleton.GetBoneGlobalRest(bone).Origin;
        return direction.LengthSquared() > 1e-8f ? direction.Normalized() : Vector3.Zero;
    }

    // 绕 Y：正值把脸（+Z）转向 +X，也就是画面右边。
    private static Basis Yaw(float degrees) => new(Vector3.Up, Mathf.DegToRad(degrees));

    // 绕 X：正值让 +Y 倒向 +Z，也就是往前（朝相机）低头。
    private static Basis Pitch(float degrees) => new(Vector3.Right, Mathf.DegToRad(degrees));

    // 绕 Z：正值让 +X 转向 +Y。
    private static Basis Roll(float degrees) => new(new Vector3(0, 0, 1), Mathf.DegToRad(degrees));
}
