using UnityEngine;

namespace Climb.Core
{
    /// <summary>
    /// 角色肢体物理参数配置（ScriptableObject）：
    /// 为每个肢体独立配置 Mass / LinearDamping / AngularDamping / GravityScale。
    /// 创建：Project 右键 → Create → Climb → Body Config，然后填写参数并拖给 BodyController。
    /// </summary>
    [CreateAssetMenu(fileName = "BodyConfig", menuName = "Climb/Body Config")]
    public class BodyConfig : ScriptableObject
    {
        [Header("肩部")] public LimbParams shoulder = new LimbParams();
        [Header("肘部")] public LimbParams elbow = new LimbParams();
        [Header("手腕")] public LimbParams wrist = new LimbParams();
        [Header("手部")] public LimbParams hand = new LimbParams();
        [Header("髋关节")] public LimbParams hip = new LimbParams();
        [Header("膝盖")] public LimbParams knee = new LimbParams();
        [Header("脚踝")] public LimbParams ankle = new LimbParams();
        [Header("脚部")] public LimbParams foot = new LimbParams();
        [Header("身体躯干")] public LimbParams torso = new LimbParams();
    }

    /// <summary>单个肢体的物理参数。</summary>
    [System.Serializable]
    public class LimbParams
    {
        [Header("刚体参数")]
        [Tooltip("质量（kg）")] public float mass = 1f;
        [Tooltip("线性阻尼：越大平动衰减越快")] public float linearDamping = 0f;
        [Tooltip("角阻尼：越大旋转衰减越快")] public float angularDamping = 0.05f;
        [Tooltip("重力倍率：0=无重力，1=全重力")] public float gravityScale = 1f;
    }
}