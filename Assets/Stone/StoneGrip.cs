using UnityEngine;
using UnityEngine.InputSystem;

namespace Climb.Core.Interaction
{
    /// <summary>
    /// 石头抓握：挂在石头上（需 Rigidbody2D + Collider2D，Layer 设为 Stone）。
    /// 手（Layer = Tip 的 Rigidbody2D + Collider2D）碰到石头 → 启用 FixedJoint2D，
    /// 并把它的 connectedBody（target）设为手的刚体，锚点跟随鼠标；
    /// 手与石头分离 → 禁用关节并清空 target。
    /// 使用 Trigger 检测（石头的 Collider2D 需勾选 Is Trigger）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public sealed class StoneGrip : MonoBehaviour
    {
        [Header("Layer")]
        [Tooltip("手的 Layer 名称（默认 Tip）")]
        public string tipLayerName = "Tip";

        [Header("绑定")]
        [Tooltip("碰撞发生即绑定在接触点；勾选后绑定点才跟随鼠标（默认关闭更稳定）")]
        public bool anchorFollowsMouse = false;
        [Tooltip("手离开石头多久才解除绑定（去抖，防止边界抖动导致反复绑定/解除）")]
        [Range(0f, 1f)] public float releaseDelay = 0.15f;

        private FixedJoint2D _joint;
        private Rigidbody2D _connectedBody; // 当前绑定的手（Tip）刚体
        private Camera _cam;
        private int _tipLayer;
        private LayerMask _tipMask;
        private readonly Collider2D[] _overlapResults = new Collider2D[8]; // 复用避免每帧分配
        private float _releaseTimer;
        private bool _gripping;

        private void Awake()
        {
            _cam = Camera.main;
            if (_cam == null) _cam = FindFirstObjectByType<Camera>();

            _tipLayer = LayerMask.NameToLayer(tipLayerName);
            _tipMask = 1 << _tipLayer;

            // 石头是固定(Kinematic)刚体，开启 FullKinematicContacts 保证触发稳定
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null && rb.bodyType == RigidbodyType2D.Kinematic)
                rb.useFullKinematicContacts = true;

            _joint = GetComponent<FixedJoint2D>();
            if (_joint != null)
            {
                _joint.enabled = false;
                _joint.connectedBody = null;
            }
        }

        /// <summary>每帧状态检测：重叠即绑定/保持，离开持续 releaseDelay 才解除（避免 OnTrigger 抖动）。</summary>
        private void FixedUpdate()
        {
            Rigidbody2D tip;
            bool touching = IsTipTouching(out tip);

            if (!_gripping && touching)
            {
                BeginGrip(tip);
            }
            else if (_gripping)
            {
                if (touching)
                {
                    _releaseTimer = 0f;
                }
                else
                {
                    _releaseTimer += Time.fixedDeltaTime;
                    if (_releaseTimer >= releaseDelay)
                        EndGrip();
                }
            }
        }

        /// <summary>石头 Trigger 是否正与 Tip 层 collider 重叠。</summary>
        private bool IsTipTouching(out Rigidbody2D tip)
        {
            tip = null;
            var stoneCol = GetComponent<Collider2D>();
            if (stoneCol == null || _tipLayer < 0) return false;

            var filter = new ContactFilter2D();
            filter.useTriggers = true;
            filter.SetLayerMask(_tipMask);

            int count = Physics2D.OverlapCollider(stoneCol, filter, _overlapResults);
            for (int i = 0; i < count; i++)
            {
                if (_overlapResults[i] == null) continue;
                if (_overlapResults[i].gameObject.layer != _tipLayer) continue;
                tip = _overlapResults[i].attachedRigidbody;
                return true;
            }
            return false;
        }

        private void Update()
        {
            if (!_gripping || _joint == null || !_joint.enabled) return;
            if (anchorFollowsMouse)
                UpdateJointAnchors();
        }

        private void BeginGrip(Rigidbody2D tipBody)
        {
            if (tipBody == null) return;
            _connectedBody = tipBody;

            if (_joint == null)
                _joint = gameObject.AddComponent<FixedJoint2D>();

            _joint.connectedBody = _connectedBody; // target = 手的刚体
            _joint.enabled = true;
            SetJointAnchors(); // 在接触点建立连接，绑定后石头随 FixedJoint 刚性与手锁定
            _gripping = true;
        }

        private void EndGrip()
        {
            _gripping = false;
            _connectedBody = null;
            if (_joint != null)
            {
                _joint.enabled = false;
                _joint.connectedBody = null; // 清空 target
            }
        }

        /// <summary>在绑定点（手当前所在位置）建立锚点：石头与手在接触点刚性锁定。</summary>
        private void SetJointAnchors()
        {
            // 石头上：手的当前位置转石头本地坐标
            _joint.anchor = (Vector2)transform.InverseTransformPoint(_connectedBody.position);
            // 手上：手本地原点（绑定在手中心）
            _joint.connectedAnchor = Vector2.zero;
        }

        /// <summary>锚点跟随鼠标（可选）：石头与手的"鼠标点"世界重合于鼠标。</summary>
        private void UpdateJointAnchors()
        {
            Vector2 mouse = GetMouseWorld();
            _joint.anchor = (Vector2)transform.InverseTransformPoint(mouse);
            if (_connectedBody != null)
                _joint.connectedAnchor = (Vector2)_connectedBody.transform.InverseTransformPoint(mouse);
        }

        private Vector2 GetMouseWorld()
        {
            if (_cam == null) return Vector2.zero;
            var pointer = Pointer.current;
            Vector2 screen = pointer != null ? pointer.position.ReadValue() : (Vector2)Input.mousePosition;
            return _cam.ScreenToWorldPoint(screen);
        }
    }
}

