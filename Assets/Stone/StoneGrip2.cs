using UnityEngine;
using UnityEngine.InputSystem;

namespace Climb.Core.Interaction
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class StoneGrip2 : MonoBehaviour
    {
        [SerializeField] HingeJoint2D _contactHinge;
        private string _tipLayerName = "Tip";
        private int _tipLayer;
        private LayerMask _tipMask; 
       
        private Collider2D _stoneCollider;   
        private readonly Collider2D[] _results = new Collider2D[8];
        private Camera _cam;

        private void Awake()
        {
            _stoneCollider = GetComponent<Collider2D>();
            _cam = Camera.main;
            if (_cam == null) _cam = FindFirstObjectByType<Camera>();

            _tipLayer = LayerMask.NameToLayer(_tipLayerName);
            _tipMask = 1 << _tipLayer;
        }


        private void FixedUpdate()
        {
            Rigidbody2D tip = GetTouchingTip();
            _contactHinge.connectedBody = tip == null ? null : tip;
            if (tip == null) {
                _contactHinge.transform.localPosition = Vector3.zero;
                return;
            }
            else if(tip.gameObject.GetComponent<DragRigidbody2>().IsDragging)
            {
                _contactHinge.transform.position = GetMouseWorld();
            }
        }

        /// <summary>鼠标世界坐标（InputSystem / 旧 Input 兼容）。</summary>
        private Vector2 GetMouseWorld()
        {
            if (_cam == null) return _contactHinge.transform.position;
            var pointer = Pointer.current;
            Vector2 screen = pointer != null ? pointer.position.ReadValue() : (Vector2)Input.mousePosition;
            return _cam.ScreenToWorldPoint(screen);
        }

        private Rigidbody2D GetTouchingTip()
        {
            if (_stoneCollider == null) return null;
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(_tipMask);
            int count = Physics2D.OverlapCollider(_stoneCollider, filter, _results);
            for (int i = 0; i < count; i++)
            {
                if (_results[i] == null) continue;
                return _results[i].attachedRigidbody;   // 手脚的刚体
            }
            return null;
        }

        // private void OnTriggerEnter2D(Collider2D other)
        // {
        //     print($"OnTrigger2D: {other.gameObject.name}");
        //     if (other.gameObject.layer != _tipLayer) return;
        //     _contactHinge.connectedBody = other.GetComponent<Rigidbody2D>();
        // }

        // private void OnTriggerExit2D(Collider2D other)
        // {
        //     print($"OnTriggerExit2D: {other.gameObject.name}");
        //     if (other.gameObject.layer != _tipLayer) return;
        //     _contactHinge.connectedBody = null;
        // }
    }
}

