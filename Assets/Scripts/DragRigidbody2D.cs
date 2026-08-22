using UnityEngine;
using UnityEngine.InputSystem;

namespace Climb.Core.Interaction
{
    /// <summary>
    /// 通用"点击拖拽刚体"：鼠标点击到自身碰撞体时进入拖拽，
    /// 用 PD 控制器在刚体上施力，使其平滑跟随鼠标移动。
    /// 挂到任意带 Rigidbody2D + Collider2D 的对象上即可。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class DragRigidbody2D : MonoBehaviour
    {
        [Header("命中判定")]
        [Tooltip("无碰撞体时用距离判定点击")] [Range(0.05f, 3f)] public float grabRadius = 0.5f;
        [Tooltip("点击命中层（-1=全部）")] public LayerMask hitLayers = -1;

        [Header("拖拽 (PD 控制)")]
        [Tooltip("末端离鼠标最大距离（防拉飞）")] [Range(0.5f, 6f)] public float maxDragDist = 4f;
        [Tooltip("拖拽最大速度上限（防拉飞）")] [Range(0.5f, 12f)] public float dragMaxSpeed = 6f;
        [Tooltip("位置增益：越大越跟手")] [Range(1f, 600f)] public float dragKp = 300f;
        [Tooltip("速度阻尼：越大刹车越利落（防振荡）")] [Range(0f, 40f)] public float dragKd = 20f;
        [Tooltip("施力上限（已做质量补偿，按需调大）")] [Range(20f, 2000f)] public float maxDragForce = 800f;
        [Tooltip("已弃用：新实现用 PD 速度项刹车，无需额外衰减（保留兼容）")] [Range(0.7f, 1f)] public float dragDamping = 1f;

        [Header("行为")]
        [Tooltip("拖拽时是否保留重力（默认关闭更跟手）")] public bool keepGravityWhileDragging = false;
        [Tooltip("拖拽时是否锁定旋转")] public bool lockRotationWhileDragging = false;

        private Rigidbody2D _body;
        private Collider2D _collider;
        private Camera _cam;
        private bool _dragging;
        private Vector3 _targetWorld;
        private float _gravitySaved;

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _collider = GetComponent<Collider2D>();
            _cam = Camera.main;
            if (_cam == null) _cam = FindFirstObjectByType<Camera>();
            _gravitySaved = _body.gravityScale;
        }

        private void Update()
        {
            if (_cam == null) return;

            var pointer = Pointer.current;
            if (pointer == null) return;

            Vector3 mp = _cam.ScreenToWorldPoint(pointer.position.ReadValue());
            mp.z = 0f;

            if (pointer.press.wasPressedThisFrame && !_dragging)
            {
                if (IsHit(mp))
                    BeginDrag();
            }

            if (_dragging)
            {
                _targetWorld = mp;
                if (pointer.press.wasReleasedThisFrame)
                    EndDrag();
            }
        }

        private void FixedUpdate()
        {
            if (!_dragging) return;

            Vector2 pos = _body.position;
            Vector2 toTarget = (Vector2)_targetWorld - pos;
            if (toTarget.magnitude > maxDragDist)
                toTarget = toTarget.normalized * maxDragDist;

            Vector2 force = toTarget * dragKp - _body.linearVelocity * dragKd;
            force *= _body.mass;
            force = Vector2.ClampMagnitude(force, maxDragForce);

            _body.AddForce(force, ForceMode2D.Force);

            if (_body.linearVelocity.sqrMagnitude > dragMaxSpeed * dragMaxSpeed)
                _body.linearVelocity = _body.linearVelocity.normalized * dragMaxSpeed;
        }

        private bool IsHit(Vector2 worldPoint)
        {
            if (_collider != null)
            {
                var hit = Physics2D.OverlapPoint(worldPoint, hitLayers);
                return hit != null && hit == _collider;
            }
            return Vector2.Distance(worldPoint, (Vector2)transform.position) <= grabRadius;
        }

        private void BeginDrag()
        {
            _dragging = true;
            if (!keepGravityWhileDragging) _body.gravityScale = 0f;
            if (lockRotationWhileDragging)
                _body.constraints |= RigidbodyConstraints2D.FreezeRotation;
        }

        private void EndDrag()
        {
            _dragging = false;
            if (!keepGravityWhileDragging) _body.gravityScale = _gravitySaved;
            if (lockRotationWhileDragging)
                _body.constraints &= ~RigidbodyConstraints2D.FreezeRotation;
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;
        }
    }
}