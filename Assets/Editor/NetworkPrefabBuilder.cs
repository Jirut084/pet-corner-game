using kcp2k;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// สร้าง prefab สำหรับเล่น LAN จาก Player ในฉาก Shop — กดใหม่ได้ทุกครั้งที่แก้ตัวละครในฉาก
///   Assets/Resources/Net/NetPlayer.prefab          — สำเนา Player + NetworkIdentity + NetworkTransformReliable + NetPlayer
///   Assets/Resources/Net/PetCornerNetwork.prefab   — KcpTransport + PetCornerNetworkManager (Online = Shop, Offline = MainMenu)
/// ไม่แก้ไฟล์ฉาก Shop (สำเนาถูกสร้างใน preview scene แล้วทิ้ง)
/// </summary>
public static class NetworkPrefabBuilder
{
    private const string Folder = "Assets/Resources/Net";
    private const string ShopScene = "Assets/Scenes/Shop.unity";
    private const string MenuScene = "Assets/Scenes/MainMenu.unity";

    [MenuItem("Tools/Pet Corner/Build Network Prefabs")]
    public static string Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "Net");

        Scene shop = SceneManager.GetSceneByPath(ShopScene);
        bool openedHere = !shop.isLoaded;
        if (openedHere) shop = EditorSceneManager.OpenScene(ShopScene, OpenSceneMode.Additive);

        try
        {
            PlayerController source = FindPlayer(shop);
            if (source == null) return $"ไม่พบ PlayerController ใน {ShopScene}";

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject player = BuildPlayer(source, preview);
                GameObject manager = BuildManager(player, preview);
                return $"สร้างแล้ว: {AssetDatabase.GetAssetPath(player)}, {AssetDatabase.GetAssetPath(manager)}";
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(shop, true);
        }
    }

    private static PlayerController FindPlayer(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            PlayerController found = root.GetComponentInChildren<PlayerController>(true);
            if (found != null) return found;
        }
        return null;
    }

    private static GameObject BuildPlayer(PlayerController source, Scene preview)
    {
        GameObject copy = Object.Instantiate(source.gameObject);
        SceneManager.MoveGameObjectToScene(copy, preview);
        copy.name = "NetPlayer";
        copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        copy.AddComponent<NetworkIdentity>();

        // เจ้าของตัวเดินเอง แล้วส่งตำแหน่งให้เซิร์ฟเวอร์กระจายต่อ (เหมือน Lab 02)
        NetworkTransformReliable netTransform = copy.AddComponent<NetworkTransformReliable>();
        netTransform.syncDirection = SyncDirection.ClientToServer;
        netTransform.syncScale = false;

        copy.AddComponent<NetPlayer>();

        return PrefabUtility.SaveAsPrefabAsset(copy, $"{Folder}/NetPlayer.prefab");
    }

    private static GameObject BuildManager(GameObject playerPrefab, Scene preview)
    {
        GameObject go = new GameObject("PetCornerNetwork");
        SceneManager.MoveGameObjectToScene(go, preview);

        KcpTransport transport = go.AddComponent<KcpTransport>();
        transport.Port = 7777;

        PetCornerNetworkManager manager = go.AddComponent<PetCornerNetworkManager>();
        manager.transport = transport;
        manager.playerPrefab = playerPrefab;
        manager.onlineScene = ShopScene;
        manager.offlineScene = MenuScene;
        manager.maxConnections = PlayerControls.MaxPlayers;
        manager.dontDestroyOnLoad = true;

        return PrefabUtility.SaveAsPrefabAsset(go, $"{Folder}/PetCornerNetwork.prefab");
    }
}
