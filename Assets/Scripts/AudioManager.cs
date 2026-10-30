using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// เสียงกลางของเกม — BGM, เสียงเดิน และ SFX
///
/// Audio Mixer (ไม่บังคับ): ใส่กลุ่ม BGM / SFX แล้วทุกเสียงจะวิ่งผ่านกลุ่มนั้น ปรับสมดุลได้ในหน้าต่าง Audio Mixer ตอนกด Play
/// รวมถึงเสียงวนของสถานี (CareStation อ่าน SfxGroup ไปใช้) — ไม่ใส่ก็เล่นได้เหมือนเดิม
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

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

    // ส่งต่อคลิปเสียงให้ CareStation ไปเปิดเล่นลูป
    public AudioClip EatClip => eatSfx;
    public AudioClip CuttingHairClip => cuttingHairSfx;
    public AudioClip SleepClip => sleepSfx;
    public AudioClip PlayToyClip => playToySfx;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }

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
        // ตั้งค่า mixer ใน Awake ไม่ติด (ข้อจำกัดของ Unity) จึงตั้งระดับเพลงตอน Start
        if (mixer != null) SetMusicVolume(musicVolume);

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

        // มี mixer = ปรับที่กลุ่ม แล้วเปิด bgmSource เต็ม ไม่งั้นเสียงโดนลดซ้ำสองชั้น
        bool viaMixer = mixer != null && mixer.SetFloat(musicVolumeParameter, ToDecibels(musicVolume));

        if (bgmSource != null)
        {
            bgmSource.volume = viaMixer ? 1f : musicVolume;
        }
    }

    public void SetSfxVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);
        if (mixer != null && mixer.SetFloat(sfxVolumeParameter, ToDecibels(volume))) return;

        if (sfxSource != null)
        {
            sfxSource.volume = volume;
        }
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
