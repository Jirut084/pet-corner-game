using UnityEngine;

/// <summary>
/// หนี — เดินด้วย NavMeshAgent จึงอ้อมสถานีและกำแพงแทนที่จะมุดทะลุ และติดพื้นเสมอ
/// ผู้เล่นใกล้ = วิ่งออกห่าง ผู้เล่นไกล = เดินเปะปะในรัศมีที่กำหนด
/// </summary>
[CreateAssetMenu(menuName = "Pet Corner/Pet State/Flee", fileName = "PetState_Flee")]
public class PetFleeState : PetState
{
    [Tooltip("ความเร็วตอนหนี ต้องน้อยกว่า moveSpeed ของผู้เล่น (5) ไม่งั้นไล่ไม่ทัน")]
    [SerializeField] private float fleeSpeed = 2.6f;

    [Tooltip("ผู้เล่นเข้ามาใกล้กว่านี้ = หนีออกห่าง ไกลกว่านี้ = เดินเปะปะ")]
    [SerializeField] private float awarenessRadius = 4.5f;

    [Tooltip("หนีไกลจากจุดที่เริ่มหนีได้มากสุดกี่หน่วย")]
    [SerializeField] private float wanderRadius = 7f;

    [Tooltip("ตอนเดินเปะปะ เลือกจุดหมายใหม่ทุกกี่วินาที")]
    [SerializeField] private float repathInterval = 1.5f;

    [Tooltip("ผู้เล่นเข้ามาใกล้เท่านี้ สัตว์ยอมหยุดให้จับ (ขยายได้ด้วยบัฟ CatchRadius)")]
    [SerializeField] private float catchRadius = 1.6f;

    [SerializeField] private PetState corneredState;

    public override void Enter(PetEscape brain)
    {
        brain.BeginRunaway(fleeSpeed);
        brain.NextRepathTime = 0f;
    }

    /// <summary>ผูกสถานะปลายทางตอนสร้างชุดมาตรฐานใน runtime (ตอนใช้ไฟล์ asset ให้ลากใส่ใน Inspector แทน)</summary>
    public void LinkRuntime(PetState cornered) => corneredState = cornered;

    public override PetState Tick(PetEscape brain, float deltaTime)
    {
        if (brain.Pet.IsCarried) return brain.CalmState; // ถูกจับได้ด้วยทางอื่น

        float distance = brain.DistanceToPlayer();
        if (distance <= brain.ScaledCatchRadius(catchRadius) && corneredState != null) return corneredState;

        if (Time.time >= brain.NextRepathTime)
        {
            brain.MoveTo(brain.PickFleeDestination(distance <= awarenessRadius, wanderRadius));
            brain.NextRepathTime = Time.time + repathInterval;
        }

        return this;
    }
}
