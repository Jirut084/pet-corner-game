using UnityEngine;

/// <summary>
/// ส่งค่าให้ Animator ของตัวละคร (ผู้เล่น / ลูกค้า) โดยไม่ต้องแก้ PlayerController หรือ Customer
/// วัดความเร็วจากการเคลื่อนที่จริงของตัวละครทุกเฟรม จึงใช้ได้ทั้งแบบ CharacterController และ MoveTowards
///
/// ใส่ไว้ที่ root ของตัวละคร (GameObject เดียวกับ PlayerController หรือ Customer)
/// Animator อยู่ที่โมเดลลูก — ปล่อยช่อง animator ว่างไว้ จะหาในลูกให้เอง
///
/// พารามิเตอร์ที่อ่านจาก Animator Controller (ไม่มีตัวไหนก็ข้ามไป ไม่ขึ้น error):
///   Speed      (Float) — ความเร็วแนวราบ หน่วยต่อวินาที ใช้สลับ Idle / Walk / Run
///   IsCarrying (Bool)  — กำลังอุ้มของอยู่ (เฉพาะตัวที่มี PlayerCarrier)
///   IsSitting  (Bool)  — ลูกค้ากำลังนั่งรอ (เฉพาะตัวที่มี Customer)
/// </summary>
public class CharacterAnimationDriver : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Animator ของโมเดล ปล่อยว่างจะหาในลูกให้อัตโนมัติ")]
    [SerializeField] private Animator animator;

    [Header("Parameters")]
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string carryingParameter = "IsCarrying";
    [SerializeField] private string sittingParameter = "IsSitting";

    [Tooltip("หน่วงค่าความเร็วกี่วินาที กันท่าเดินกระตุกตอนเริ่ม/หยุดเดิน")]
    [SerializeField] private float speedDampTime = 0.1f;

    private PlayerCarrier carrier;
    private Customer customer;
    private Vector3 lastPosition;

    private int speedHash;
    private int carryingHash;
    private int sittingHash;
    private bool hasSpeed;
    private bool hasCarrying;
    private bool hasSitting;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();

        carrier = GetComponent<PlayerCarrier>();
        customer = GetComponent<Customer>();
        lastPosition = transform.position;

        if (animator == null)
        {
            Debug.LogWarning($"[CharacterAnimationDriver] {name} ไม่มี Animator ในลูก ปิดตัวเองไว้ก่อน");
            enabled = false;
            return;
        }

        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"[CharacterAnimationDriver] {name}: Animator ยังไม่ได้ใส่ Controller");
        }

        // ตำแหน่งตัวละครคุมด้วยโค้ดอยู่แล้ว ถ้าเปิด Root Motion อนิเมชันจะดันตัวละครซ้อนอีกชั้น
        animator.applyRootMotion = false;

        CacheParameters();
    }

    /// <summary>จำเฉพาะพารามิเตอร์ที่มีอยู่จริงใน controller จะได้ไม่ขึ้น warning ทุกเฟรม</summary>
    private void CacheParameters()
    {
        speedHash = Animator.StringToHash(speedParameter);
        carryingHash = Animator.StringToHash(carryingParameter);
        sittingHash = Animator.StringToHash(sittingParameter);

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == speedHash && parameter.type == AnimatorControllerParameterType.Float) hasSpeed = true;
            if (parameter.nameHash == carryingHash && parameter.type == AnimatorControllerParameterType.Bool) hasCarrying = true;
            if (parameter.nameHash == sittingHash && parameter.type == AnimatorControllerParameterType.Bool) hasSitting = true;
        }
    }

    // ใช้ LateUpdate เพื่อให้ตัวละครขยับใน Update เสร็จก่อนค่อยวัดระยะที่เดินไปจริง
    private void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f) return;

        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f; // ไม่นับการตกตามแรงโน้มถ่วง ไม่งั้นยืนนิ่งก็กลายเป็นท่าเดิน
        lastPosition = transform.position;

        if (hasSpeed)
        {
            animator.SetFloat(speedHash, delta.magnitude / deltaTime, speedDampTime, deltaTime);
        }

        if (hasCarrying && carrier != null)
        {
            animator.SetBool(carryingHash, carrier.IsCarrying);
        }

        if (hasSitting && customer != null)
        {
            animator.SetBool(sittingHash, customer.State == Customer.CustomerState.Waiting);
        }
    }
}
