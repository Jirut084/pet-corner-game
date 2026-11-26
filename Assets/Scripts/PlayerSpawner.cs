using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// จัดผู้เล่นทุกครั้งที่โหลดฉากที่มี Player อยู่ (เช่น Shop) — ไม่ต้องใส่ในฉาก ทำงานเองผ่าน sceneLoaded
///
///   เล่นในเครื่องเดียว — ใช้ Player ในฉากเป็นผู้เล่น 1 แล้วโคลนเพิ่มตามช่องที่เปิดไว้ในเมนูปุ่มควบคุม
///                     โคลนจากของในฉากจึงได้การผูกของในฉาก (กริด กล้อง ฯลฯ) ครบ ไม่ต้องแก้ไฟล์ฉาก
///   เล่นผ่าน LAN     — ปิด Player ในฉากทิ้ง จำตำแหน่งไว้ให้ PetCornerNetworkManager ใช้เป็นจุดเกิด
///                     แล้วให้ Mirror spawn ผู้เล่นจาก prefab แทน (หนึ่งคนต่อหนึ่งเครื่อง)
/// </summary>
public static class PlayerSpawner
{
    private const float Spacing = 1.2f;

    /// <summary>ตำแหน่ง/ทิศของ Player ในฉากล่าสุด — จุดเกิดของผู้เล่นออนไลน์</summary>
    public static Vector3 SpawnAnchor { get; private set; }
    public static Quaternion SpawnRotation { get; private set; } = Quaternion.identity;

    private static readonly Vector3[] Offsets =
    {
        new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f),
        new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f),
        new Vector3(2f, 0f, 0f), new Vector3(-2f, 0f, 0f)
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PlayerController scenePlayer = FindScenePlayer(scene);
        if (scenePlayer == null) return;

        SpawnAnchor = scenePlayer.transform.position;
        SpawnRotation = scenePlayer.transform.rotation;

        if (NetworkServer.active || NetworkClient.active)
        {
            // ผู้เล่นออนไลน์มาจาก prefab ของ Mirror — ตัวในฉากไม่ใช้
            Object.Destroy(scenePlayer.gameObject);
            return;
        }

        SpawnLocalPlayers(scenePlayer);
    }

    private static PlayerController FindScenePlayer(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            PlayerController found = root.GetComponentInChildren<PlayerController>();
            if (found != null && found.GetComponent<NetworkIdentity>() == null) return found;
        }

        return null;
    }

    private static void SpawnLocalPlayers(PlayerController first)
    {
        List<int> slots = PlayerControls.ActiveSlots();
        first.PlayerSlot = slots[0];
        if (slots.Count < 2) return; // เล่นคนเดียว ไม่ต้องมีวงสีบอกตัว

        AddMarker(first.gameObject, slots[0]);

        for (int i = 1; i < slots.Count; i++)
        {
            Vector3 position = FindFreeSpot(first.transform.position, first.GetComponent<CharacterController>(), i);
            GameObject clone = Object.Instantiate(first.gameObject, position, first.transform.rotation);
            clone.name = $"Player {slots[i] + 1}";

            clone.GetComponent<PlayerController>().PlayerSlot = slots[i];
            AddMarker(clone, slots[i]);
        }

        Debug.Log($"[PlayerSpawner] เล่นในเครื่องเดียว {slots.Count} คน");
    }

    /// <summary>หาที่ว่างข้างผู้เล่น 1 ที่ไม่ชนสถานี/ผนัง — ไม่เจอเลยก็วางทับตำแหน่งเดิม (CharacterController จะดันออกเอง)</summary>
    public static Vector3 FindFreeSpot(Vector3 origin, CharacterController cc, int index)
    {
        int tried = 0;

        foreach (Vector3 offset in Offsets)
        {
            Vector3 candidate = origin + offset * Spacing;
            if (cc == null || IsFree(candidate, cc))
            {
                if (++tried >= index) return candidate;
            }
        }

        return origin;
    }

    private static bool IsFree(Vector3 position, CharacterController cc)
    {
        Vector3 center = position + cc.center;
        float half = Mathf.Max(0f, cc.height * 0.5f - cc.radius);

        // ยกปลายล่างขึ้นนิดหน่อยไม่ให้ชนพื้น
        Vector3 bottom = center + Vector3.down * half + Vector3.up * 0.1f;
        Vector3 top = center + Vector3.up * half;
        return !Physics.CheckCapsule(bottom, top, cc.radius, ~0, QueryTriggerInteraction.Ignore);
    }

    /// <summary>วงสีใต้เท้าบอกว่าใครคือผู้เล่นคนไหน</summary>
    public static GameObject AddMarker(GameObject player, int slot)
    {
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Player Marker";
        Object.Destroy(ring.GetComponent<Collider>());

        CharacterController cc = player.GetComponent<CharacterController>();
        float feet = cc != null ? cc.center.y - cc.height * 0.5f : -1f;

        ring.transform.SetParent(player.transform, false);
        ring.transform.localPosition = new Vector3(0f, feet + 0.03f, 0f);
        ring.transform.localScale = new Vector3(0.9f, 0.01f, 0.9f);

        Renderer renderer = ring.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.material.color = PlayerControls.PlayerColors[Mathf.Clamp(slot, 0, PlayerControls.PlayerColors.Length - 1)];
        return ring;
    }
}
