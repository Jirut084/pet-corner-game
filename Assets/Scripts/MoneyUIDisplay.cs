using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// แสดงจำนวนเงินสะสม (ReturnZone.TotalWallet) เป็นข้อความบนหน้าจอ
/// ผูกกับ ReturnZone.OnWalletChanged จึงอัปเดตเฉพาะตอนเงินเปลี่ยนจริง ไม่ต้อง poll ทุกเฟรม
/// วิธีตั้งค่า: สร้าง Canvas > Text (Legacy) แล้วลาก Text object มาใส่ช่อง moneyText ใน Inspector
/// </summary>
public class MoneyUIDisplay : MonoBehaviour
{
    [SerializeField] private Text moneyText;
    [SerializeField] private string format = "Money: {0}";

    private void OnEnable()
    {
        ReturnZone.OnWalletChanged += UpdateDisplay;
        UpdateDisplay(ReturnZone.TotalWallet); // แสดงค่าปัจจุบันทันทีตอนเปิดใช้งาน (กันกรณีเงินสะสมมาจากรอบก่อนแล้ว)
    }

    private void OnDisable()
    {
        ReturnZone.OnWalletChanged -= UpdateDisplay;
    }

    private void UpdateDisplay(int amount)
    {
        if (moneyText == null)
        {
            Debug.LogWarning("[MoneyUIDisplay] ยังไม่ได้ผูก moneyText ใน Inspector");
            return;
        }

        moneyText.text = string.Format(format, amount);
    }
}
