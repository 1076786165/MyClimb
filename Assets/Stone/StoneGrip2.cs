using UnityEngine;
using UnityEngine.InputSystem;

namespace Climb.Core.Interaction
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class StoneGrip2 : MonoBehaviour
    {
        [SerializeField] private Rigidbody2D _defaultTip;
        [SerializeField] HingeJoint2D _contactHinge;
        [SerializeField] private MeshRenderer _outlineMesh;
        

        private string _tipLayerName = "Tip";
        private int _tipLayer;
        private LayerMask _tipMask; 
       
        private Collider2D _stoneCollider;   
        private readonly Collider2D[] _results = new Collider2D[8];
        private Camera _cam;

        [Tooltip("防抖：失去接触后持续多久仍无接触，才真正断开连接")]
        [Range(0f, 0.5f)] public float releaseDelay = 0.15f;
        [Tooltip("stone 跟随鼠标的平滑速度（越大越跟手）")]
        [Range(1f, 30f)] public float followSpeed = 10f;
        private Rigidbody2D _connectedTip;   // 当前已连接的手脚（带防抖滞回）
        private float _releaseTimer;          // 分离倒计时

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
            GameObject detectstone = TouchDetector.Instance.TouchingStone == null ? null : TouchDetector.Instance.TouchingStone.gameObject;

            Rigidbody2D tip = GetTouchingTip();

            if(_defaultTip != null){
                _contactHinge.transform.position = _defaultTip.transform.position;
                _contactHinge.connectedBody = _defaultTip;
                _defaultTip = null;
            }

            if(_contactHinge.connectedBody == null){
                if(detectstone != null && tip != null){
                   _contactHinge.connectedBody = tip;
                   _contactHinge.transform.position = tip.transform.position;
                }
            }
            else{
                if(tip== null && detectstone == null){
                    _contactHinge.connectedBody = null;
                }
            }

            if(_contactHinge.connectedBody != null && tip != null){
                if (tip.gameObject.TryGetComponent<DragRigidbody2>(out var drag) && drag.IsDragging){
                    Vector3 target = GetMouseWorld();
                    float t = 1f - Mathf.Exp(-followSpeed * Time.fixedDeltaTime);
                    _contactHinge.transform.position = Vector3.Lerp(_contactHinge.transform.position, target, t);
                }
            }

            if(detectstone != null && tip != null){
                _outlineMesh.gameObject.SetActive(detectstone == gameObject);
            }else{
                _outlineMesh.gameObject.SetActive(false);
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

