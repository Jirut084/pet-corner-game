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
            case CareStep.Feed: return Loc.T("อาหาร", "Feed");
            case CareStep.Bath: return Loc.T("อาบน้ำ", "Bath");
            case CareStep.Groom: return Loc.T("แต่งขน", "Groom");
            case CareStep.Play: return Loc.T("เล่น", "Play");
            case CareStep.Rest: return Loc.T("พักผ่อน", "Rest");
            default: return step.ToString();
        }
    }
}
