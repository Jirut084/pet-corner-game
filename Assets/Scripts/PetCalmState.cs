using UnityEngine;

/// <summary>
/// สงบ — สัตว์อยู่ในมือผู้เล่น อยู่ในห้องรอ หรืออยู่บนสถานีที่กำลังทำงาน
/// จะนับเวลาเฉพาะตอน "ถูกทิ้ง" เท่านั้น ครบเวลาแล้วเปลี่ยนไปสถานะหนี
/// </summary>
[CreateAssetMenu(menuName = "Pet Corner/Pet State/Calm", fileName = "PetState_Calm")]
public class PetCalmState : PetState
{
    [Tooltip("ถูกทิ้งไว้โดยไม่มีใครดูแลนานกี่วินาทีแล้วหนี")]
    [SerializeField] private float neglectSeconds = 12f;

    [Tooltip("สัตว์บุคลิกขี้ตกใจหนีเร็วกว่าปกติกี่เท่า (0.6 = ไวขึ้น 40%)")]
    [Range(0.1f, 1f)]
    [SerializeField] private float skittishMultiplier = 0.6f;

    [SerializeField] private PetState fleeState;

    public override void Enter(PetEscape brain)
    {
        brain.NeglectTimer = 0f;
    }

    /// <summary>ผูกสถานะปลายทางตอนสร้างชุดมาตรฐานใน runtime (ตอนใช้ไฟล์ asset ให้ลากใส่ใน Inspector แทน)</summary>
    public void LinkRuntime(PetState flee) => fleeState = flee;

    public override PetState Tick(PetEscape brain, float deltaTime)
    {
        if (!brain.IsNeglected())
        {
            brain.NeglectTimer = 0f;
            return this;
        }

        brain.NeglectTimer += deltaTime;

        float limit = brain.Pet.Trait == PetTrait.Skittish ? neglectSeconds * skittishMultiplier : neglectSeconds;
        if (brain.NeglectTimer < limit) return this;

        return fleeState != null ? fleeState : this;
    }
}
