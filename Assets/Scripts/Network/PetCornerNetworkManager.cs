using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Mirror;
using UnityEngine;

/// <summary>
/// เล่นผ่าน LAN ด้วย Mirror (ตาม DG333 Lab 02: Basic Connection, NetworkManager, Player Spawn)
///
/// Prefab อยู่ที่ Resources/Net/PetCornerNetwork (KcpTransport + ตัวนี้) — เมนูหลักสร้างให้ตอนกด Host / Join
/// Host = เปิดเซิร์ฟเวอร์ + เป็นผู้เล่นไปด้วย แล้ว Mirror โหลดฉาก Shop ให้ทุกเครื่องเอง (Online Scene)
/// หลุดจากเซิร์ฟเวอร์ = กลับเมนู (Offline Scene)
///
/// ขอบเขตตาม Lab 02: ทุกเครื่องเห็นกันเดินไปมา (NetworkTransform) ส่วนระบบร้าน (ลูกค้า สัตว์ สถานี เงิน)
/// ยังรันแยกกันในแต่ละเครื่อง ยังไม่ซิงก์ — เป็นงานของ lab ถัดไป
/// </summary>
public class PetCornerNetworkManager : NetworkManager
{
    private const string PrefabPath = "Net/PetCornerNetwork";

    public static PetCornerNetworkManager Instance => singleton as PetCornerNetworkManager;

    /// <summary>สถานะการเชื่อมต่อเป็นข้อความ ให้หน้าเมนู LAN โชว์</summary>
    public static event Action<string> StatusChanged;

    /// <summary>สร้างจาก prefab ถ้ายังไม่มี (ตัวเดิมอยู่ข้ามฉากด้วย DontDestroyOnLoad)</summary>
    public static PetCornerNetworkManager Ensure()
    {
        if (Instance != null) return Instance;

        GameObject prefab = Resources.Load<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[Network] ไม่พบ prefab Resources/{PrefabPath} — สร้างใหม่ได้จากเมนู Tools > Pet Corner > Build Network Prefabs");
            return null;
        }

        return Instantiate(prefab).GetComponent<PetCornerNetworkManager>();
    }

    public static void Host()
    {
        PetCornerNetworkManager manager = Ensure();
        if (manager == null || NetworkServer.active) return;

        Notify("กำลังเปิดห้อง...");
        manager.StartHost();
    }

    public static void Join(string address)
    {
        PetCornerNetworkManager manager = Ensure();
        if (manager == null || NetworkClient.active) return;

        manager.networkAddress = string.IsNullOrWhiteSpace(address) ? "localhost" : address.Trim();
        Notify($"กำลังเชื่อมต่อ {manager.networkAddress}...");
        manager.StartClient();
    }

    /// <summary>ออกจากเกมออนไลน์ไม่ว่าเป็น host หรือ client</summary>
    public static void Leave()
    {
        if (singleton == null) return;

        if (NetworkServer.active && NetworkClient.isConnected) singleton.StopHost();
        else if (NetworkClient.active) singleton.StopClient();
        else if (NetworkServer.active) singleton.StopServer();
    }

    public static ushort Port => Transport.active is PortTransport port ? port.Port : (ushort)7777;

    /// <summary>IPv4 ของเครื่องนี้ในวง LAN ให้ host บอกเพื่อน</summary>
    public static List<string> LocalAddresses()
    {
        var list = new List<string>();
        try
        {
            foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) list.Add(ip.ToString());
            }
        }
        catch (SocketException)
        {
            // ไม่มีเครือข่าย — ปล่อยรายการว่าง
        }
        return list;
    }

    private static void Notify(string message)
    {
        Debug.Log($"[Network] {message}");
        StatusChanged?.Invoke(message);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        Notify($"เปิดห้องแล้ว พอร์ต {Port}");
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Notify("เชื่อมต่อสำเร็จ");
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        Notify("หลุดการเชื่อมต่อ");
    }

    public override void OnClientError(TransportError error, string reason)
    {
        Notify($"เชื่อมต่อไม่ได้: {reason}");
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        int index = FindFreePlayerNumber();
        if (index < 0)
        {
            Debug.LogWarning($"[Network] ห้องเต็ม ({PlayerControls.MaxPlayers} คน) ปฏิเสธ connection {conn.connectionId}");
            conn.Disconnect();
            return;
        }

        // จุดเกิดเดียวกับเล่น local: ผู้เล่น 1 ที่ตำแหน่ง Player ในฉาก คนต่อไปหาที่ว่างข้างๆ
        Vector3 position = index == 0
            ? PlayerSpawner.SpawnAnchor
            : PlayerSpawner.FindFreeSpot(PlayerSpawner.SpawnAnchor, playerPrefab.GetComponent<CharacterController>(), index);

        GameObject player = Instantiate(playerPrefab, position, PlayerSpawner.SpawnRotation);
        player.name = $"Player {index + 1} [conn {conn.connectionId}]";
        player.GetComponent<NetPlayer>().PlayerNumber = index;

        NetworkServer.AddPlayerForConnection(conn, player);
        Debug.Log($"[Network] ผู้เล่น {index + 1} เข้าห้อง (conn {conn.connectionId})");
    }

    private static int FindFreePlayerNumber()
    {
        for (int i = 0; i < PlayerControls.MaxPlayers; i++)
        {
            bool used = false;
            foreach (NetworkConnectionToClient c in NetworkServer.connections.Values)
            {
                if (c.identity != null && c.identity.TryGetComponent(out NetPlayer p) && p.PlayerNumber == i)
                {
                    used = true;
                    break;
                }
            }

            if (!used) return i;
        }

        return -1;
    }
}
