using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// สร้าง/ซ่อม GameObject "AudioManager" ในฉากให้อัตโนมัติ แล้วเสียบคลิปเสียงจาก Assets/Audio ให้ครบ
///
/// ใช้เมื่อ: เปิดฉาก Topdown แล้วกดเมนู Tools/Pet Corner/Setup Audio Manager
/// ถ้ามี AudioManager ในฉากอยู่แล้ว มันจะไม่สร้างซ้ำ แค่เติมช่องที่ยังว่างให้
/// (ช่องที่เคยตั้งค่าเองไว้แล้วจะไม่ถูกทับ)
/// </summary>
public static class AudioSetupTool
{
    // ชื่อ field ใน AudioManager → ชื่อไฟล์ (ไม่ต้องใส่นามสกุล) ใน Assets/Audio
    private static readonly (string field, string clipName)[] ClipBindings =
    {
        ("bgMusic", "BG_music"),
        ("walkSfx", "walk"),
        ("dayFinishSfx", "dayFinish"),
        ("getMoneySfx", "getmoney"),
        ("taskDoneSfx", "taskdone"),
        ("pickupPetSfx", "pickupPet"),
        ("pickupFurnitureSfx", "pickupFurniture"),
        ("eatSfx", "eat"),
        ("cuttingHairSfx", "CuttingHair"),
        ("sleepSfx", "sleep"),
        ("playToySfx", "playToy"),
        ("showerSfx", "shower"),
    };

    [MenuItem("Tools/Pet Corner/Setup Audio Manager")]
    public static void SetupAudioManager()
    {
        AudioManager manager = Object.FindAnyObjectByType<AudioManager>();

        if (manager == null)
        {
            GameObject go = new GameObject("AudioManager");
            manager = go.AddComponent<AudioManager>();
            Undo.RegisterCreatedObjectUndo(go, "Setup Audio Manager");
            Debug.Log("[AudioSetupTool] สร้าง GameObject \"AudioManager\" ใหม่ในฉาก");
        }
        else
        {
            Undo.RecordObject(manager, "Setup Audio Manager");
            Debug.Log($"[AudioSetupTool] เจอ AudioManager อยู่แล้วที่ \"{manager.name}\" — เติมเฉพาะช่องที่ยังว่าง");
        }

        SerializedObject so = new SerializedObject(manager);

        EnsureSources(manager, so);

        int filled = 0;
        int missing = 0;

        foreach ((string field, string clipName) in ClipBindings)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning($"[AudioSetupTool] ไม่พบ field \"{field}\" ใน AudioManager");
                continue;
            }

            if (prop.objectReferenceValue != null) continue; // ตั้งไว้เองแล้ว ไม่ทับ

            AudioClip clip = FindClip(clipName);
            if (clip == null)
            {
                Debug.LogWarning($"[AudioSetupTool] หาไฟล์เสียง \"{clipName}\" ใน Assets/Audio ไม่เจอ — ช่อง {field} ยังว่างอยู่");
                missing++;
                continue;
            }

            prop.objectReferenceValue = clip;
            filled++;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

        Selection.activeGameObject = manager.gameObject;

        Debug.Log($"[AudioSetupTool] เสร็จแล้ว — เสียบคลิปให้ {filled} ช่อง, หาไม่เจอ {missing} ช่อง " +
                  "(อย่าลืมกด Ctrl+S เซฟฉาก)");
    }

    /// <summary>
    /// AudioManager สร้าง AudioSource เองได้ตอน runtime อยู่แล้ว
    /// แต่สร้างไว้ตั้งแต่ตอน edit จะได้ปรับ volume/pitch จาก Inspector ก่อนกด Play
    /// </summary>
    private static void EnsureSources(AudioManager manager, SerializedObject so)
    {
        AudioSource[] existing = manager.GetComponents<AudioSource>();
        int index = 0;

        AudioSource Take()
        {
            if (index < existing.Length) return existing[index++];

            index++;
            return Undo.AddComponent<AudioSource>(manager.gameObject);
        }

        AssignSource(so, "sfxSource", Take(), loop: false);
        AssignSource(so, "footstepSource", Take(), loop: true);
        AssignSource(so, "bgmSource", Take(), loop: true);
    }

    private static void AssignSource(SerializedObject so, string field, AudioSource source, bool loop)
    {
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f; // เสียงระบบ ได้ยินเท่ากันทั้งแผนที่

        SerializedProperty prop = so.FindProperty(field);
        if (prop != null && prop.objectReferenceValue == null)
        {
            prop.objectReferenceValue = source;
        }
    }

    private static AudioClip FindClip(string clipName)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{clipName} t:AudioClip", new[] { "Assets/Audio" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // FindAssets จับชื่อแบบ "มีคำนี้อยู่" ต้องกรองให้ตรงเป๊ะเอง ไม่งั้น "eat" จะไปโดน "repeat"
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), clipName,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        return null;
    }
}
