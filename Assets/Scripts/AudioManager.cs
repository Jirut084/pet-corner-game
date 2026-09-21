using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Sources")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource footstepSource;
    [SerializeField] private AudioSource bgmSource;

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

        PlayBGM();
    }

    private void Start()
    {
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
        bgmSource.volume = musicVolume;
        bgmSource.Play();
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        if (bgmSource != null)
        {
            bgmSource.volume = musicVolume;
        }
    }

    public void SetSfxVolume(float volume)
    {
        if (sfxSource != null)
        {
            sfxSource.volume = Mathf.Clamp01(volume);
        }
    }

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
