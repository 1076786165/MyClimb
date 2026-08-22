using UnityEngine;
using UnityEngine.InputSystem;

namespace Climb.Core.Interaction
{
    /// <summary>
    /// 石头抓握：挂在石头上（需 Rigidbody2D + Collider2D，Layer 设为 Stone）。
    /// 手（Layer = Tip 的 Rigidbody2D + Collider2D）碰到石头 → 启用 FixedJoint2D，
    /// 并把它的 connectedBody（target）设为手的刚体，锚点跟随鼠标；
    /// 手与石头分离 → 禁用关节并清空 target。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public sealed class StoneGrip : MonoBehaviour
    {
        [Header("Layer")]
        [Tooltip("手的 Layer 名称（默认 Tip）")]
        public string tipLayerName = "Tip";

        [Header("绑定")]
        [Tooltip("鼠标移动时绑定点是否跟随（关闭则固定在碰撞瞬间的鼠标位置）")]
        public bool anchorFollowsMouse = true;

        private FixedJoint2D _joint;
        private Rigidbody2D _connectedBody; // 当前绑定的手（Tip）刚体
        private Camera _cam;
        private bool _gripping;

        private void Awake()
        {
            _cam = Camera.main;
            if (_cam == null) _cam = FindFirstObjectByType<Camera>();

            _joint = GetComponent<FixedJoint2D>();
            if (_joint != null)
            {
                _joint.enabled = false;
                _joint.connectedBody = null;
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            // 只响应 自己(Stone) ↔ 手(Tip) 的碰撞
            if (collision.gameObject.layer != LayerMask.NameToLayer(tipLayerName)) return;
            BeginGrip(collision.rigidbody);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            if (collision.gameObject.layer != LayerMask.NameToLayer(tipLayerName)) return;
            EndGrip();
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
            UpdateJointAnchors();
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

        /// <summary>把石头与手各自的"鼠标点本地坐标"设为锚点：两个锚点世界重合于鼠标 → 绑定跟随鼠标。</summary>
        private void UpdateJointAnchors()
        {
            Vector2 mouse = GetMouseWorld();

            // 石头上：鼠标位置转石头本地坐标
            _joint.anchor = (Vector2)transform.InverseTransformPoint(mouse);
            // 手上：鼠标位置转手本地坐标
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

