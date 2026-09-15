using UnityEngine;

/// <summary>
/// สีและชื่อภาษาไทยของแต่ละขั้นตอนการดูแล — ใช้ร่วมกันทุกที่ที่ต้องแสดงผล
/// (ป้ายเหนือหัวสัตว์, หน้าต่างซูมสถานี, gizmo, log) จะได้สีตรงกันทั้งเกม
/// </summary>
public static class CareStepStyle
{
    public static Color ColorOf(CareStep step)
    {
        switch (step)
        {
            case CareStep.Feed: return new Color(1f, 0.84f, 0.25f);
            case CareStep.Bath: return new Color(0.35f, 0.65f, 1f);
            case CareStep.Groom: return new Color(0.9f, 0.45f, 0.9f);
            case CareStep.Play: return new Color(0.45f, 0.85f, 0.45f);
            case CareStep.Rest: return new Color(1f, 0.6f, 0.25f);
            default: return Color.white;
        }
    }

    public static string LabelOf(CareStep step)
    {
        switch (step)
        {
            case CareStep.Feed: return "อาหาร";
            case CareStep.Bath: return "อาบน้ำ";
            case CareStep.Groom: return "แต่งขน";
            case CareStep.Play: return "เล่น";
            case CareStep.Rest: return "พักผ่อน";
            default: return step.ToString();
        }
    }
}
