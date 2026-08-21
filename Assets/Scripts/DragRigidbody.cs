using UnityEngine;
using UnityEngine.InputSystem;

namespace Climb.Core.Interaction
{
    /// <summary>
    /// 通用"点击拖拽刚体"（3D Rigidbody 版）：鼠标点击到自身碰撞体时进入拖拽，
    /// 用 PD 控制器在刚体上施力，使其平滑跟随鼠标移动。
    /// 挂到任意带 Rigidbody + Collider 的对象上即可。
    /// 与 DragRigidbody2D 同款手感，参数一一对应。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class DragRigidbody : MonoBehaviour
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

        private Rigidbody _body;
        private Collider _collider;
        private Camera _cam;
        private bool _dragging;
        private Vector3 _targetWorld;
        private float _dragDepth;                 // 鼠标映射深度（相机 → 物体），拖拽全程固定
        private bool _gravitySaved;
        private RigidbodyConstraints _constraintsSaved;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _collider = GetComponent<Collider>();
            _cam = Camera.main;
            if (_cam == null) _cam = FindFirstObjectByType<Camera>();
            _gravitySaved = _body.useGravity;
            _constraintsSaved = _body.constraints;
        }

        private void Update()
        {
            if (_cam == null) return;

            var pointer = Pointer.current;
            if (pointer == null) return;

            // 按下瞬间：命中自己才进入拖拽
            if (pointer.press.wasPressedThisFrame && !_dragging)
            {
                if (IsHit(pointer.position.ReadValue()))
                    BeginDrag();
            }

            if (_dragging)
            {
                _targetWorld = GetMouseWorld(pointer.position.ReadValue());
                if (pointer.press.wasReleasedThisFrame)
                    EndDrag();
            }
        }

        private void FixedUpdate()
        {
            if (!_dragging) return;

            Vector3 pos = _body.position;
            Vector3 toTarget = _targetWorld - pos;
            if (toTarget.magnitude > maxDragDist)
                toTarget = toTarget.normalized * maxDragDist;

            // ---- 位置 PD：误差拉向目标，速度项反向刹车（跟手 + 停得快）----
            // 质量补偿：AddForce 的加速度 = 力/质量，乘质量后手感与物体质量无关
            Vector3 force = toTarget * dragKp - _body.linearVelocity * dragKd;
            force *= _body.mass;
            force = Vector3.ClampMagnitude(force, maxDragForce);

            _body.AddForce(force, ForceMode.Force);

            // 速度上限（防拉飞），替代旧的逐帧速度衰减
            if (_body.linearVelocity.sqrMagnitude > dragMaxSpeed * dragMaxSpeed)
                _body.linearVelocity = _body.linearVelocity.normalized * dragMaxSpeed;
        }

        /// <summary>鼠标沿相机视线落在物体所在深度平面上。</summary>
        private Vector3 GetMouseWorld(Vector2 screenPos)
        {
            return _cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, _dragDepth));
        }

        /// <summary>点击命中判定：优先射线命中本物体（含子物体）碰撞体，其次距离。</summary>
        private bool IsHit(Vector2 screenPos)
        {
            Ray ray = _cam.ScreenPointToRay(screenPos);
            if (_collider != null)
            {
                if (Physics.Raycast(ray, out RaycastHit hit, 1000f, hitLayers))
                    return hit.collider == _collider || hit.collider.transform.IsChildOf(transform);
                return false;
            }

            // 无碰撞体：物体到鼠标射线的最短距离判定
            Vector3 closest = ray.origin + ray.direction *
                Vector3.Dot(transform.position - ray.origin, ray.direction);
            return Vector3.Distance(closest, transform.position) <= grabRadius;
        }

        private void BeginDrag()
        {
            _dragging = true;
            // 记录物体在当前相机视角下的深度，拖拽全程把鼠标映射到这个深度平面
            _dragDepth = _cam.WorldToScreenPoint(_body.position).z;
            if (!keepGravityWhileDragging) _body.useGravity = false;
            if (lockRotationWhileDragging)
                _body.constraints |= RigidbodyConstraints.FreezeRotation;
        }

        private void EndDrag()
        {
            _dragging = false;
            if (!keepGravityWhileDragging) _body.useGravity = _gravitySaved;
            if (lockRotationWhileDragging)
                _body.constraints = _constraintsSaved;
            // 干净利落：松手立即刹停，消除惯性滑行
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }
    }
}
