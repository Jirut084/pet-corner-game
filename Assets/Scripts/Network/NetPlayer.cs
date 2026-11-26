using Mirror;
using UnityEngine;

/// <summary>
/// ติดอยู่บน prefab ผู้เล่นออนไลน์ (Resources/Net/NetPlayer) คู่กับ NetworkIdentity + NetworkTransformReliable
///
/// เครื่องของเจ้าของตัว (isLocalPlayer) — ใช้ปุ่มของผู้เล่น 1 ในเครื่องนั้น เดินเองแล้ว NetworkTransform ส่งตำแหน่งไปเซิร์ฟเวอร์
/// เครื่องอื่น — ปิดการรับปุ่ม/โต้ตอบ/ฟิสิกส์ของตัวนี้ ปล่อยให้ NetworkTransform ขยับตามที่เจ้าของส่งมา
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public class NetPlayer : NetworkBehaviour
{
    /// <summary>ผู้เล่นคนที่เท่าไหร่ในห้อง (0-3) เซิร์ฟเวอร์กำหนด ใช้เลือกสีวงใต้เท้า</summary>
    [SyncVar(hook = nameof(OnPlayerNumberChanged))]
    public int PlayerNumber = -1;

    private GameObject marker;

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!isLocalPlayer) SetLocalControl(false);
        RefreshMarker();
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // แต่ละเครื่องเล่นได้คนเดียว ใช้ชุดปุ่มของผู้เล่น 1 ในเครื่องนั้น
        GetComponent<PlayerController>().PlayerSlot = 0;
        SetLocalControl(true);
    }

    /// <summary>เปิด/ปิดทุกอย่างที่อ่านปุ่มหรือขยับตัวเอง — ตัวของเครื่องอื่นต้องปิด ไม่งั้นจะเดินแย่งกับตำแหน่งที่ซิงก์มา</summary>
    private void SetLocalControl(bool local)
    {
        foreach (Behaviour behaviour in new Behaviour[]
                 {
                     GetComponent<PlayerController>(),
                     GetComponent<InteractionManager>(),
                     GetComponent<TilePlacer>(),
                     GetComponent<PlacementPreview>()
                 })
        {
            if (behaviour != null) behaviour.enabled = local;
        }

        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = local;
    }

    private void OnPlayerNumberChanged(int oldValue, int newValue) => RefreshMarker();

    private void RefreshMarker()
    {
        if (PlayerNumber < 0) return;

        if (marker != null) Destroy(marker);
        marker = PlayerSpawner.AddMarker(gameObject, PlayerNumber);
    }
}
