using UnityEngine;

/// <summary>
/// สถิติของ run ปัจจุบัน ไว้โชว์หน้าสรุปตอนจบเกม
///
/// ทำไมต้องมี: run ที่แพ้แล้วไม่เหลืออะไรเลยทำให้ไม่มีใครอยากกดเล่นใหม่
/// หน้าสรุปที่บอกว่า "ทำได้เท่าไหร่" และ "แพ้เพราะอะไร" คือส่วนที่คุ้มที่สุดของระบบ Headquarters ใน PlateUp
/// ในราคาที่ถูกกว่ามาก (ไม่ต้องมีฉาก hub ไม่ต้องมีระบบเซฟ)
///
/// ของแถมสำคัญ: บรรทัด "แพ้เพราะอะไร" ทำให้เก็บข้อมูล balance จากคนเทสได้ทุกรอบโดยไม่ต้องยืนดูเขาเล่น
///
/// เป็น static ล้วน ไม่ต้องมี GameObject ในฉาก — GameManager.StartNewRun เรียก Reset() ให้เอง
/// </summary>
public static class RunStats
{
    public static int OrdersReturned { get; private set; }
    public static int OrdersComplete { get; private set; }
    public static int FeesEarned { get; private set; }
    public static int TipsEarned { get; private set; }

    public static int GoodsDelivered { get; private set; }
    public static int GoodsEarned { get; private set; }

    /// <summary>ค่าเช่า + ค่าดูแลสถานีที่จ่ายไปทั้ง run</summary>
    public static int CostsPaid { get; private set; }

    public static int Escapes { get; private set; }
    public static int Catches { get; private set; }

    /// <summary>ทำขั้นตอนต่อเนื่องตามลำดับได้กี่ครั้ง (ทิปคอมโบนับรวมใน TipsEarned แล้ว)</summary>
    public static int Combos { get; private set; }

    /// <summary>เหตุผลของการร้องเรียนครั้งล่าสุด = สาเหตุที่แพ้ ถ้า run จบเพราะร้องเรียนเต็ม</summary>
    public static string LastComplaint { get; private set; }

    public static int TotalEarned => FeesEarned + TipsEarned + GoodsEarned;

    public static void Reset()
    {
        OrdersReturned = 0;
        OrdersComplete = 0;
        FeesEarned = 0;
        TipsEarned = 0;
        GoodsDelivered = 0;
        GoodsEarned = 0;
        CostsPaid = 0;
        Escapes = 0;
        Catches = 0;
        Combos = 0;
        LastComplaint = null;
    }

    /// <summary>ส่งสัตว์คืนสำเร็จ (earned รวมทิปมาแล้ว จึงต้องแยกทิปออกไม่ให้นับซ้ำ)</summary>
    public static void NotePetReturned(int earned, int tip, bool complete)
    {
        OrdersReturned++;
        if (complete) OrdersComplete++;

        FeesEarned += Mathf.Max(0, earned - tip);
        TipsEarned += tip;
    }

    public static void NoteGoodsDelivered(int bonus)
    {
        GoodsDelivered++;
        GoodsEarned += bonus;
    }

    public static void NoteCostsPaid(int amount) => CostsPaid += amount;

    public static void NoteEscape() => Escapes++;

    public static void NoteCatch() => Catches++;

    public static void NoteCombo() => Combos++;

    public static void NoteComplaint(string reason)
    {
        if (!string.IsNullOrEmpty(reason)) LastComplaint = reason;
    }
}
