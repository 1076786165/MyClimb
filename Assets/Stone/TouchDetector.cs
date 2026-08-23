using UnityEngine;
using UnityEngine.InputSystem;

class TouchDetector : MonoBehaviourSingleton<TouchDetector>
{
        private string _tipLayerName = "Stone";
        private LayerMask _tipMask; 
       
        private Collider2D _detectorCollider;   
        private readonly Collider2D[] _results = new Collider2D[8];

        private Camera _cam;

       [SerializeField] Collider2D touchingStone;

        public Collider2D TouchingStone => touchingStone;

        protected override void Awake()
        {
            _detectorCollider = GetComponent<Collider2D>();
            _tipMask = LayerMask.GetMask(_tipLayerName);

            _cam = Camera.main;
            if (_cam == null) _cam = FindFirstObjectByType<Camera>();
        }

        void Update()
        {
            touchingStone = GetTouchingStone();
        }

        private Vector2 GetMouseWorld()
        {
            var pointer = Pointer.current;
            Vector2 screen = pointer != null ? pointer.position.ReadValue() : (Vector2)Input.mousePosition;
            return _cam.ScreenToWorldPoint(screen);
        }
        
        private Collider2D GetTouchingStone()
        {
            transform.position = GetMouseWorld();
            Mouse mouse = Mouse.current;
            if (!mouse.leftButton.isPressed) return null;
            if (_detectorCollider == null) return null;

            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(_tipMask);
            int count = Physics2D.OverlapCollider(_detectorCollider, filter, _results);
            for (int i = 0; i < count; i++)
            {
                if (_results[i] == null) continue;
                return _results[i].GetComponent<Collider2D>();   // 手脚的刚体
            }
            return null;
        }
}
