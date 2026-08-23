using Climb.Core.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Climb.Core
{
    /// <summary>
    /// 角色身体管理：接收 BodyConfig（ScriptableObject）配置，
    /// 初始化时把每个肢体的物理参数（Mass / LinearDamping / AngularDamping / GravityScale）
    /// 应用到对应的 Rigidbody2D。
    /// </summary>
    public class BodyController : MonoBehaviour
    {
        [Header("躯干拖拽辅助力")]
        [SerializeField] float bodyDragAssistanceForce;

        [Header("物理参数配置")]
        [Tooltip("肢体物理参数配置（ScriptableObject，在 Project 里创建并填写）")]
        [SerializeField] BodyConfig config;

        [Header("肢体引用（Rigidbody2D，除躯干外左右各一）")]
        [Header("左：肩/肘/腕/手")]
        [SerializeField] Rigidbody2D shoulderLeft;
        [SerializeField] Rigidbody2D elbowLeft;
        [SerializeField] Rigidbody2D wristLeft;
        [SerializeField] Rigidbody2D handLeft;
        [Header("右：肩/肘/腕/手")]
        [SerializeField] Rigidbody2D shoulderRight;
        [SerializeField] Rigidbody2D elbowRight;
        [SerializeField] Rigidbody2D wristRight;
        [SerializeField] Rigidbody2D handRight;
        [Header("左：髋/膝/踝/脚")]
        [SerializeField] Rigidbody2D hipLeft;
        [SerializeField] Rigidbody2D kneeLeft;
        [SerializeField] Rigidbody2D ankleLeft;
        [SerializeField] Rigidbody2D footLeft;
        [Header("右：髋/膝/踝/脚")]
        [SerializeField] Rigidbody2D hipRight;
        [SerializeField] Rigidbody2D kneeRight;
        [SerializeField] Rigidbody2D ankleRight;
        [SerializeField] Rigidbody2D footRight;
        [Header("躯干")]
        [SerializeField] Rigidbody2D torso;

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogWarning("[BodyController] 未指定 BodyConfig，跳过参数应用", this);
                return;
            }

            // 上肢：左右各一，共用该肢体的一组配置参数
            Apply(shoulderLeft, config.shoulder);
            Apply(shoulderRight, config.shoulder);
            Apply(elbowLeft, config.elbow);
            Apply(elbowRight, config.elbow);
            Apply(wristLeft, config.wrist);
            Apply(wristRight, config.wrist);
            Apply(handLeft, config.hand);
            Apply(handRight, config.hand);
            // 下肢：左右各一
            Apply(hipLeft, config.hip);
            Apply(hipRight, config.hip);
            Apply(kneeLeft, config.knee);
            Apply(kneeRight, config.knee);
            Apply(ankleLeft, config.ankle);
            Apply(ankleRight, config.ankle);
            Apply(footLeft, config.foot);
            Apply(footRight, config.foot);
            // 躯干（单个）
            Apply(torso, config.torso);
        }

        /// <summary>把一个肢体的参数应用到对应刚体。</summary>
        private static void Apply(Rigidbody2D rb, LimbParams p)
        {
            if (rb == null || p == null) return;
            rb.mass = p.mass;
            rb.linearDamping = p.linearDamping;
            rb.angularDamping = p.angularDamping;
            rb.gravityScale = p.gravityScale;
        }

        void Update(){
            int SupportCount = 0;
            if(handLeft != null && handLeft.GetComponent<DragRigidbody2>().HasBeenSupported) SupportCount++;
            if(handRight != null && handRight.GetComponent<DragRigidbody2>().HasBeenSupported) SupportCount++;
            if(footLeft != null && footLeft.GetComponent<DragRigidbody2>().HasBeenSupported) SupportCount++;    
            if(footRight != null && footRight.GetComponent<DragRigidbody2>().HasBeenSupported) SupportCount++;

            bool isAnyDragging = false;
            if(Pointer.current.press.isPressed){
                if(handLeft != null && handLeft.GetComponent<DragRigidbody2>().IsDragging) isAnyDragging = true;
                if(handRight != null && handRight.GetComponent<DragRigidbody2>().IsDragging) isAnyDragging = true;
                if(footLeft != null && footLeft.GetComponent<DragRigidbody2>().IsDragging) isAnyDragging = true;
                if(footRight != null && footRight.GetComponent<DragRigidbody2>().IsDragging) isAnyDragging = true; 
            }

            // 给自己施加力：大小 bodyDragAssistanceForce，方向朝向鼠标
            var pointer = Pointer.current;
            Vector2 screen = pointer != null ? pointer.position.ReadValue() : Vector2.zero;
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(screen);
            Vector2 toMouse = ((Vector2)mousePos - (Vector2)transform.position).normalized;
            // 给自己（躯干刚体）施加力
            if(SupportCount > 0 && isAnyDragging){
                // Debug.Log($"开始拖拽 躯干助力施加 : {SupportCount}");
                GetComponent<Rigidbody2D>()?.AddForce(bodyDragAssistanceForce * toMouse);
            }
        }
    }
}