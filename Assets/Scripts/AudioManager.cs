using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// เสียงกลางของเกม — BGM, เสียงเดิน และ SFX
///
/// Audio Mixer (ไม่บังคับ): ใส่กลุ่ม BGM / SFX แล้วทุกเสียงจะวิ่งผ่านกลุ่มนั้น ปรับสมดุลได้ในหน้าต่าง Audio Mixer ตอนกด Play
/// รวมถึงเสียงวนของสถานี (CareStation อ่าน SfxGroup ไปใช้) — ไม่ใส่ก็เล่นได้เหมือนเดิม
/// มีระบบบันทึกค่าลง PlayerPrefs อัตโนมัติ ปรับระดับเสียงจากหน้าจอเมนูใดก็ได้
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    public const string PrefKeyMusic = "PetCorner_MusicVolume";
    public const string PrefKeySfx = "PetCorner_SfxVolume";

    [Header("Audio Sources")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource footstepSource;
    [SerializeField] private AudioSource bgmSource;

    [Header("Mixer (ไม่บังคับ)")]
    [Tooltip("Audio Mixer ของเกม — ต้อง Expose พารามิเตอร์ Volume ของกลุ่ม BGM/SFX ตามชื่อด้านล่าง ถึงจะปรับเสียงจากโค้ดได้")]
    [SerializeField] private AudioMixer mixer;

    [Tooltip("กลุ่มเพลงพื้นหลัง")]
    [SerializeField] private AudioMixerGroup musicGroup;

    [Tooltip("กลุ่มเสียงเอฟเฟกต์ (รวมเสียงเดินและเสียงวนของสถานี)")]
    [SerializeField] private AudioMixerGroup sfxGroup;

    [SerializeField] private string musicVolumeParameter = "MusicVolume";
    [SerializeField] private string sfxVolumeParameter = "SfxVolume";

    /// <summary>กลุ่ม SFX ให้ AudioSource ของระบบอื่น (เช่นสถานี) ส่งเสียงเข้า mixer เดียวกัน (null = ไม่ได้ใช้ mixer)</summary>
    public AudioMixerGroup SfxGroup => sfxGroup;

    [Header("Music Settings")]
    [SerializeField] private AudioClip bgMusic;
    [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.5f;

    [Header("SFX Settings")]
    [Range(0f, 1f)] [SerializeField] private float sfxVolume = 0.8f;

    [Header("Footstep Settings")]
    [SerializeField] private AudioClip walkSfx;

    [Header("Game Flow SFX")]
    [SerializeField] private AudioClip dayFinishSfx;
    [SerializeField] private AudioClip getMoneySfx;
    [SerializeField] private AudioClip taskDoneSfx;
    [Range(0f, 1f)] [SerializeField] private float taskDoneVolume = 0.4f;

    [Header("Gameplay SFX")]
    [SerializeField] private AudioClip pickupPetSfx;
    [SerializeField] private AudioClip pickupFurnitureSfx;
    [SerializeField] private AudioClip eatSfx;
    [SerializeField] private AudioClip cuttingHairSfx;
    [SerializeField] private AudioClip sleepSfx;
    [SerializeField] private AudioClip playToySfx; // เพิ่มเสียงเล่นของเล่น
    [SerializeField] private AudioClip showerSfx;  // เพิ่มเสียงอาบน้ำ

    [Header("UI SFX")]
    [SerializeField] private AudioClip uiClickSfx;

    // ส่งต่อคลิปเสียงให้ CareStation ไปเปิดเล่นลูป
    public AudioClip EatClip => eatSfx;
    public AudioClip CuttingHairClip => cuttingHairSfx;
    public AudioClip SleepClip => sleepSfx;
    public AudioClip PlayToyClip => playToySfx;
    public AudioClip UiClickClip => uiClickSfx;

    /// <summary>ระดับเสียงดนตรีปัจจุบัน (0–1)</summary>
    public float MusicVolume => musicVolume;

    /// <summary>ระดับเสียงเอฟเฟกต์ปัจจุบัน (0–1)</summary>
    public float SfxVolume => sfxVolume;

    /// <summary>แจ้งเตือนเมื่อระดับ SFX มีการเปลี่ยนแปลง (สำหรับระบบที่ไม่ได้ใช้ mixer ปรับตาม)</summary>
    public event System.Action<float> OnSfxVolumeChanged;

    // ---------- ตัวช่วยเรียกใช้งานจากหน้าจอเมนู (แม้ไม่มี Instance ในฉาก) ----------

    public static float GetMusicVolumeGlobal()
    {
        if (Instance != null) return Instance.musicVolume;
        return PlayerPrefs.GetFloat(PrefKeyMusic, 0.5f);
    }

    public static float GetSfxVolumeGlobal()
    {
        if (Instance != null) return Instance.sfxVolume;
        return PlayerPrefs.GetFloat(PrefKeySfx, 0.8f);
    }

    public static void SetMusicVolumeGlobal(float volume)
    {
        volume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKeyMusic, volume);
        if (Instance != null) Instance.SetMusicVolume(volume);
    }

    public static void SetSfxVolumeGlobal(float volume)
    {
        volume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKeySfx, volume);
        if (Instance != null) Instance.SetSfxVolume(volume);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // โหลดค่าระดับเสียงที่เซฟไว้ใน PlayerPrefs
        if (PlayerPrefs.HasKey(PrefKeyMusic)) musicVolume = PlayerPrefs.GetFloat(PrefKeyMusic, musicVolume);
        if (PlayerPrefs.HasKey(PrefKeySfx)) sfxVolume = PlayerPrefs.GetFloat(PrefKeySfx, sfxVolume);

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }
        sfxSource.spatialBlend = 0f;

        if (footstepSource == null)
        {
            footstepSource = gameObject.AddComponent<AudioSource>();
            footstepSource.loop = true;
            footstepSource.playOnAwake = false;
        }

        if (bgmSource == null)
        {
            bgmSource = gameObject.AddComponent<AudioSource>();
        }
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;
        bgmSource.volume = musicVolume;

        // ใส่เฉพาะกลุ่มที่ตั้งไว้ ไม่ทับค่าที่ตั้งไว้ใน AudioSource เองด้วย null
        if (musicGroup != null) bgmSource.outputAudioMixerGroup = musicGroup;
        if (sfxGroup != null)
        {
            sfxSource.outputAudioMixerGroup = sfxGroup;
            footstepSource.outputAudioMixerGroup = sfxGroup;
        }

        PlayBGM();
    }

    private void Start()
    {
        // ตั้งค่า mixer และความดังของ AudioSource ตอน Start (ตั้งใน Awake ค่า mixer อาจยังไม่ติด)
        SetMusicVolume(musicVolume);
        SetSfxVolume(sfxVolume);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnDayEnded += HandleDayEnded;
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnDayEnded -= HandleDayEnded;
        }
    }

    private void HandleDayEnded(int day) => PlaySfx(dayFinishSfx);

    public void PlayBGM()
    {
        if (bgmSource == null || bgMusic == null) return;
        bgmSource.clip = bgMusic;
        bgmSource.Play();
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKeyMusic, musicVolume);

        // มี mixer = ปรับที่กลุ่ม แล้วเปิด bgmSource เต็ม ไม่งั้นเสียงโดนลดซ้ำสองชั้น
        bool viaMixer = mixer != null && mixer.SetFloat(musicVolumeParameter, ToDecibels(musicVolume));

        if (bgmSource != null)
        {
            bgmSource.volume = viaMixer ? 1f : musicVolume;
        }
    }

    public void SetSfxVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKeySfx, sfxVolume);

        if (mixer != null && mixer.SetFloat(sfxVolumeParameter, ToDecibels(sfxVolume)))
        {
            OnSfxVolumeChanged?.Invoke(sfxVolume);
            return;
        }

        if (sfxSource != null)
        {
            sfxSource.volume = sfxVolume;
        }

        if (footstepSource != null)
        {
            footstepSource.volume = sfxVolume;
        }

        OnSfxVolumeChanged?.Invoke(sfxVolume);
    }

    /// <summary>mixer ใช้หน่วย dB (0 = ดังเต็ม, −80 = เงียบ) แปลงจากค่า 0–1 แบบที่หูได้ยินลดลงเป็นเส้นตรง</summary>
    private static float ToDecibels(float linear) => linear > 0.0001f ? Mathf.Log10(linear) * 20f : -80f;

    public void PlaySfx(AudioClip clip, float volume = 1f)
    {
        if (clip != null && sfxSource != null)
        {
            sfxSource.PlayOneShot(clip, volume);
        }
    }

    public void PlayTaskDone() => PlaySfx(taskDoneSfx, taskDoneVolume);
    public void PlayGetMoney() => PlaySfx(getMoneySfx);
    public void PlayPickupPet() => PlaySfx(pickupPetSfx);
    public void PlayPickupFurniture() => PlaySfx(pickupFurnitureSfx);
    public void PlayEat() => PlaySfx(eatSfx);
    public void PlayCuttingHair() => PlaySfx(cuttingHairSfx);
    public void PlaySleep() => PlaySfx(sleepSfx);
    public void PlayShower() => PlaySfx(showerSfx); // เรียกเล่นเสียงอาบน้ำ (ครั้งเดียว)

    public void PlayClick()
    {
        if (uiClickSfx == null) uiClickSfx = Resources.Load<AudioClip>("UI/ui_click");
        if (uiClickSfx != null) PlaySfx(uiClickSfx);
    }

    public void SetWalking(bool isMoving)
    {
        if (footstepSource == null || walkSfx == null) return;

        if (isMoving && !footstepSource.isPlaying)
        {
            footstepSource.clip = walkSfx;
            footstepSource.Play();
        }
        else if (!isMoving && footstepSource.isPlaying)
        {
            footstepSource.Stop();
        }
    }
}
