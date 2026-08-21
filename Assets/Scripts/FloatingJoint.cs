using UnityEngine;

namespace Climb.Core.Interaction
{
    /// <summary>
    /// 膝盖"浮力"关节：挂在膝盖上，引用被拖拽的脚（DragRigidbody2）。
    /// 当脚正在被拖拽时（IsDragging），对本物体（膝盖）的 Rigidbody2D 施加持续力，
    /// 方向垂直于"脚 → 膝盖"连线（向上/向下可切换），模拟膝盖被托起/压下的浮力。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class FloatingJoint : MonoBehaviour
    {
        [Header("外部拖拽引用")]
        [Tooltip("被拖拽的脚/末端物体（DragRigidbody2）")]
        public DragRigidbody2 dragRigidbody2;

        [Header("力参数")]
        [Tooltip("拖拽期间施加到膝盖的持续力大小（越大越猛）")]
        [Min(0f)] public float force = 5f;

        [Tooltip("力的垂直方向开关：勾选=一个方向，不勾=另一个方向（垂直于脚↔膝盖连线，试一下哪个是你要的上/下效果）")]
        public bool pushUp = true;

        private Rigidbody2D _body;

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
        }

        private void FixedUpdate()
        {
            if (dragRigidbody2 == null || _body == null) return;
            if (!dragRigidbody2.IsDragging) return;

            // 连线方向：脚 → 膝盖
            Vector2 fromFoot = (Vector2)transform.position - (Vector2)dragRigidbody2.transform.position;
            if (fromFoot.sqrMagnitude < 0.0001f) return;

            // 2D 中垂直于连线的方向（旋转 90°）× 方向开关
            float sign = pushUp ? 1f : -1f;
            Vector2 perp = new Vector2(-fromFoot.y, fromFoot.x).normalized * sign;

            _body.AddForce(perp * force, ForceMode2D.Force);
        }
    }
}
