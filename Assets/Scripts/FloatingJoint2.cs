using UnityEngine;
using UnityEngine.InputSystem;

namespace Climb.Core.Interaction
{
   [RequireComponent(typeof(Rigidbody2D))]
    public sealed class FloatingJoint2 : MonoBehaviour
    {
        [SerializeField] private float gravityScaleDragging = 1;
        [SerializeField] private float gravityScaleNotDragging = 1;

        [SerializeField] DragRigidbody2 dragRigidbody2;
        Rigidbody2D _rb;
        bool isDragging = false;

        void Start()
        {
            _rb = GetComponent<Rigidbody2D>();

            isDragging = dragRigidbody2.IsDragging;
        }

        void Update()
        {
            if (isDragging != dragRigidbody2.IsDragging)
            {
                isDragging = dragRigidbody2.IsDragging;
                _rb.gravityScale = isDragging ? gravityScaleDragging : gravityScaleNotDragging;
            }
            
        }
    }
    
}
