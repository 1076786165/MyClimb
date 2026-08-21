using UnityEngine;
using UnityEngine.InputSystem;

namespace Climb.Core.Interaction
{
    /// <summary>
    /// 用 TargetJoint2D 实现的"点击拖拽刚体"：
    /// 命中后把 TargetJoint2D 锚在点击点，把 target 钉在鼠标上，
    /// 由物理引擎的弹簧关节把刚体拉向鼠标。
    /// 相比 DragRigidbody2D 的 PD 施力方案，更贴近 Unity 物理、更稳定不抖动。
    /// 手感由 frequency（越硬越跟手）/ dampingRatio（越大停得越快）控制。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(TargetJoint2D))]
    public sealed class DragRigidbody2 : MonoBehaviour
    {
        [Header("命中判定")]
        [Tooltip("无碰撞体时用距离判定点击")] [Range(0.05f, 3f)] public float grabRadius = 0.5f;
        [Tooltip("点击命中层（-1=全部）")] public LayerMask hitLayers = -1;

        [Header("TargetJoint2D 弹簧参数")]
        [Tooltip("弹簧频率：越大越硬、跟手越快")] [Range(1f, 100f)] public float jointFrequency = 10f;
        [Tooltip("阻尼比：越大停止越快（0-1）")] [Range(0f, 1f)] public float jointDamping = 0.7f;
        [Tooltip("关节最大施力（物体越重越要大）")] [Range(20f, 5000f)] public float jointMaxForce = 1000f;

        [Header("行为")]
        [Tooltip("拖拽时是否保留重力（默认关闭更跟手）")] public bool keepGravityWhileDragging = false;
        [Tooltip("拖拽时是否锁定旋转")] public bool lockRotationWhileDragging = false;

        private Rigidbody2D _body;
        private Collider2D _collider;
        private TargetJoint2D _joint;
        private Camera _cam;
        private bool _dragging;
        private float _gravitySaved;
        private RigidbodyConstraints2D _constraintsSaved;

        /// <summary>当前是否正在被拖拽（供其他脚本查询，如 FloatingJoint）。</summary>
        public bool IsDragging => _dragging;

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _collider = GetComponent<Collider2D>();
            _joint = GetComponent<TargetJoint2D>();
            _cam = Camera.main;
            if (_cam == null) _cam = FindFirstObjectByType<Camera>();

            _gravitySaved = _body.gravityScale;
            _constraintsSaved = _body.constraints;

            // 关节默认关闭，只在拖拽期间启用；target 由本脚本每帧驱动
            _joint.enabled = false;
            _joint.autoConfigureTarget = false;
        }

        private void Update()
        {
            if (_cam == null) return;

            var pointer = Pointer.current;
            if (pointer == null) return;

            Vector2 mp = (Vector2)_cam.ScreenToWorldPoint(pointer.position.ReadValue());

            // 按下瞬间：命中自己才进入拖拽
            if (pointer.press.wasPressedThisFrame && !_dragging)
            {
                if (IsHit(mp))
                    BeginDrag(mp);
            }

            if (_dragging)
            {
                _joint.target = mp;   // 每帧把关节目标钉在鼠标上
                if (pointer.press.wasReleasedThisFrame)
                    EndDrag();
            }
        }

        /// <summary>点击命中判定：优先碰撞体，其次距离。</summary>
        private bool IsHit(Vector2 worldPoint)
        {
            if (_collider != null)
            {
                var hit = Physics2D.OverlapPoint(worldPoint, hitLayers);
                return hit != null && hit == _collider; // 必须命中自己
            }
            return Vector2.Distance(worldPoint, (Vector2)transform.position) <= grabRadius;
        }

        private void BeginDrag(Vector2 worldPoint)
        {
            _dragging = true;

            // 锚点：把点击点转成刚体本地坐标，拖拽时刚体上的这一点对准鼠标
            _joint.anchor = (Vector2)transform.InverseTransformPoint(worldPoint);
            _joint.frequency = jointFrequency;
            _joint.dampingRatio = jointDamping;
            _joint.maxForce = jointMaxForce;
            _joint.target = worldPoint;
            _joint.enabled = true;

            if (!keepGravityWhileDragging) _body.gravityScale = 0f;
            if (lockRotationWhileDragging)
                _body.constraints |= RigidbodyConstraints2D.FreezeRotation;
        }

        private void EndDrag()
        {
            _dragging = false;
            _joint.enabled = false;
            if (!keepGravityWhileDragging) _body.gravityScale = _gravitySaved;
            if (lockRotationWhileDragging)
                _body.constraints = _constraintsSaved;
            // 干净利落：松手立即刹停，消除惯性滑行
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;
        }
    }
}
