using UnityEngine;
using UnityEngine.InputSystem;

namespace Climb.Core.Interaction
{
    /// <summary>
    /// 点击拖拽刚体（TargetJoint2D 版）。
    /// 双模式切换（由是否接触 Stone 决定，与是否拖拽无关）：
    ///  - 动态模式（默认）：TargetJoint2D 弹簧拖拽，用于平常移动；
    ///  - Kinematic 模式：与 Stone 层碰撞后，刚体切为 Kinematic，用 Rigidbody2D.MovePosition 精确定位；
    ///    离开 Stone 后自动恢复动态模式。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(TargetJoint2D))]
    public sealed class DragRigidbody2 : MonoBehaviour
    {
        // ---------------- Inspector 配置 ----------------

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

        [Header("Kinematic 模式（Stone 接触）")]
        [Tooltip("Stone 层名称：与 Stone 碰撞后切为 Kinematic + MovePosition")]
        public string stoneLayerName = "Stone";
        [Tooltip("与 Stone 碰撞时切 Kinematic + MovePosition；离开 Stone 恢复 TargetJoint2D 动态拖拽")]
        public bool kinematicOnStone = true;

        [Header("初始状态")]
        [SerializeField]
        [Tooltip("初始化时若设置了该 collider，刚体自动进入并保持 Kinematic（用于场景初始状态）")]
        private Collider2D _initialKinematicCollider;

        // ---------------- 引用 ----------------

        private Rigidbody2D _body;
        private Collider2D _collider;
        private TargetJoint2D _joint;
        private Camera _cam;

        // ---------------- 运行状态 ----------------

        private bool _dragging;      // 当前是否正在拖拽
        private bool _kinematic;     // 当前是否为 Kinematic（接触 Stone）模式
        private bool _touchingStone; // 是否接触 Stone（由 OnTrigger 维护）

        // 初始值（恢复用）
        private float _gravitySaved;
        private RigidbodyConstraints2D _constraintsSaved;
        private RigidbodyType2D _originalBodyType;
        private int _stoneLayer;

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
            _originalBodyType = _body.bodyType;
            _stoneLayer = LayerMask.NameToLayer(stoneLayerName);

            // 关节默认关闭，只在拖拽期间启用；target 由本脚本每帧驱动
            _joint.enabled = false;
            _joint.autoConfigureTarget = false;

            // 初始化：设置了初始 Kinematic collider → 直接进入 Kinematic 模式
            if (_initialKinematicCollider != null)
                EnterKinematicMode();
        }

        private void Update()
        {
            if (_cam == null) return;

            var pointer = Pointer.current;
            if (pointer == null) return;

            // 刚体类型只取决于是否接触 Stone（与是否拖拽无关）
            UpdateBodyType();

            Vector2 mp = (Vector2)_cam.ScreenToWorldPoint(pointer.position.ReadValue());

            // 按下瞬间：命中自己才进入拖拽
            if (pointer.press.wasPressedThisFrame && !_dragging)
            {
                if (IsHit(mp))
                    BeginDrag(mp);
            }

            if (_dragging)
            {
                if (_kinematic)
                    _body.MovePosition(mp);   // Kinematic：直接精确定位到鼠标
                else
                    _joint.target = mp;       // 动态：TargetJoint2D 弹簧跟随

                if (pointer.press.wasReleasedThisFrame)
                    EndDrag();
            }
        }

        // ---------------- Stone 接触检测（Trigger 标记） ----------------

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.gameObject.layer == _stoneLayer)
                _touchingStone = true;
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other.gameObject.layer == _stoneLayer)
                _touchingStone = false;
        }

        // ---------------- 动态 / Kinematic 模式切换 ----------------

        /// <summary>
        /// 刚体类型切换：
        /// 进入 Kinematic：接触 Stone 且正在拖拽(IsDragging)；
        /// 退出 Kinematic：不再接触 Stone（与拖拽状态无关）。
        /// </summary>
        private void UpdateBodyType()
        {
            if (_touchingStone)
            {
                // 接触 Stone：初始 collider 或拖拽中 → 进入 Kinematic；已进入则保持（松开拖拽也不退出）
                if (!_kinematic && (_initialKinematicCollider != null || _dragging))
                    EnterKinematicMode();
                return;
            }

            // 不再接触 Stone：退出 Kinematic 恢复动态（与初始 collider/拖拽状态无关）
            if (_kinematic) ExitKinematicMode();
        }

        /// <summary>进入 Kinematic：关闭弹簧关节，用 MovePosition 移动。</summary>
        private void EnterKinematicMode()
        {
            _kinematic = true;
            _joint.enabled = false;
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;
        }

        /// <summary>退出 Kinematic：恢复 Dynamic，重新启用 TargetJoint2D 拖拽。</summary>
        private void ExitKinematicMode()
        {
            _kinematic = false;
            _body.bodyType = _originalBodyType;
            _body.linearVelocity = Vector2.zero;
            if (_dragging) _joint.enabled = true;
        }

        // ---------------- 拖拽 ----------------

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
            // 刚体类型由 UpdateBodyType 每帧根据接触状态管理，这里不强制改变
            if (!keepGravityWhileDragging) _body.gravityScale = _gravitySaved;
            if (lockRotationWhileDragging)
                _body.constraints = _constraintsSaved;
            // 干净利落：松手立即刹停，消除惯性滑行
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;
        }
    }
}