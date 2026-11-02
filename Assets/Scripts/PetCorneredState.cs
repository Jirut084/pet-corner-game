using UnityEngine;

/// <summary>
/// จนมุม — ผู้เล่นเข้ามาใกล้พอแล้ว สัตว์หยุดยืนรอให้กด E จับ
/// ถ้าผู้เล่นถอยห่างออกไปอีก กลับไปหนีต่อ (ไม่ใช่ยืนให้จับฟรี)
/// </summary>
[CreateAssetMenu(menuName = "Pet Corner/Pet State/Cornered", fileName = "PetState_Cornered")]
public class PetCorneredState : PetState
{
    [Tooltip("ผู้เล่นต้องอยู่ในระยะนี้ถึงจะยืนรอต่อ ไกลกว่านี้กลับไปหนี")]
    [SerializeField] private float holdRadius = 2.2f;

    [SerializeField] private PetState fleeState;

    public override void Enter(PetEscape brain)
    {
        brain.StopMoving();
        brain.FacePlayer();
    }

    /// <summary>ผูกสถานะปลายทางตอนสร้างชุดมาตรฐานใน runtime (ตอนใช้ไฟล์ asset ให้ลากใส่ใน Inspector แทน)</summary>
    public void LinkRuntime(PetState flee) => fleeState = flee;

    public override PetState Tick(PetEscape brain, float deltaTime)
    {
        if (brain.Pet.IsCarried) return brain.CalmState;

        if (brain.DistanceToPlayer() > brain.ScaledCatchRadius(holdRadius) && fleeState != null) return fleeState;

        brain.FacePlayer();
        return this;
    }
}
