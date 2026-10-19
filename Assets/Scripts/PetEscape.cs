using UnityEngine;

/// <summary>
/// สัตว์หนีเมื่อถูกทิ้งไว้นานเกินไป แล้วผู้เล่นต้องวิ่งไล่จับคืน
///
/// นับว่า "ถูกทิ้ง" เมื่อสัตว์ไม่ได้อยู่ในมือผู้เล่น และไม่มีสถานีไหนกำลังดูแลอยู่
/// (วางไว้บนโต๊ะพัก, วางคาสถานีที่ทำงานเสร็จแล้ว, หรือหลุดลงพื้น) ครบ neglectSeconds แล้วหนี
///
/// หนีด้วยการเดินถอยห่างจากผู้เล่นในรัศมีจำกัด เข้าใกล้ถึง catchRadius แล้วมันจะหยุดให้จับ
/// จับด้วยปุ่ม interact เดิม (E) — สคริปต์นี้เป็น IInteractable เอง
/// บัฟ CatchRadius จากการ์ดรางวัลขยายรัศมีที่สัตว์ยอมหยุด
///
/// Pet.Awake ใส่ component นี้ให้อัตโนมัติ ไม่ต้องแก้ prefab
/// อยากปรับค่า ให้ Add Component "PetEscape" ลง prefab สัตว์เองแล้วตั้งใน Inspector
/// ponytail: เดินตรงเข้าหาทิศที่เลือก ไม่มี pathfinding — ถ้าสัตว์เริ่มมุดผ่านสถานีบ่อยค่อยใส่ NavMesh
/// </summary>
[RequireComponent(typeof(Pet))]
public class PetEscape : MonoBehaviour, IHoldInteractable
{
    [Header("Trigger")]
    [Tooltip("ถูกทิ้งไว้โดยไม่มีใครดูแลนานกี่วินาทีแล้วหนี")]
    [SerializeField] private float neglectSeconds = 12f;

    [Header("Chase")]
    [Tooltip("ความเร็วตอนหนี ต้องน้อยกว่า moveSpeed ของผู้เล่น (5) ไม่งั้นไล่ไม่ทัน")]
    [SerializeField] private float fleeSpeed = 2.6f;

    [Tooltip("หนีไกลจากจุดที่เริ่มหนีได้มากสุดกี่หน่วย")]
    [SerializeField] private float wanderRadius = 7f;

    [Tooltip("ผู้เล่นเข้ามาใกล้กว่านี้ สัตว์จะเริ่มหนีออกห่าง ไกลกว่านี้จะเดินเปะปะไปเรื่อย")]
    [SerializeField] private float awarenessRadius = 4.5f;

    [Tooltip("ผู้เล่นเข้ามาใกล้เท่านี้ สัตว์ยอมหยุดให้จับ (ขยายได้ด้วยบัฟ CatchRadius)")]
    [SerializeField] private float catchRadius = 1.6f;

    [Tooltip("ตอนเดินเปะปะ เปลี่ยนทิศทุกกี่วินาที")]
    [SerializeField] private float wanderInterval = 1.5f;

    /// <summary>กำลังหนีอยู่หรือไม่ — HUD/ระบบอื่นอ่านได้</summary>
    public bool IsEscaping { get; private set; }

    /// <summary>หยุดรอให้จับอยู่ (ผู้เล่นเข้ามาในรัศมีจับแล้ว)</summary>
    public bool IsCornered { get; private set; }

    private Pet pet;
    private Rigidbody body;
    private Transform player;
    private TileGrid grid;

    private float neglectTimer;
    private Vector3 escapeOrigin;
    private Vector3 wanderDirection;
    private float nextWanderTime;
    private bool cachedIsKinematic;
    private RigidbodyConstraints cachedConstraints;

    private void Awake()
    {
        pet = GetComponent<Pet>();
        body = GetComponent<Rigidbody>();
        grid = FindAnyObjectByType<TileGrid>(); // ไม่มีกริดในฉาก (ฉากเทสเปล่า) ก็หนีได้ แค่ไม่มีขอบกั้น
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f) return; // เกมหยุดอยู่ (timeScale 0)

        if (IsEscaping)
        {
            UpdateEscape(deltaTime);
            return;
        }

        if (!IsNeglected())
        {
            neglectTimer = 0f;
            return;
        }

        neglectTimer += deltaTime;
        if (neglectTimer >= neglectSeconds) BeginEscape();
    }

    /// <summary>ถูกทิ้งอยู่ไหม — ไม่ได้ถูกอุ้ม ออเดอร์ยังเดินอยู่ และไม่มีสถานีไหนกำลังดูแล</summary>
    private bool IsNeglected()
    {
        if (pet.IsCarried) return false;
        if (!pet.IsPatienceRunning) return false; // ยังไม่รับออเดอร์ / จบออเดอร์ไปแล้ว

        CareStation station = GetComponentInParent<CareStation>();
        return station == null || station.State != CareStation.StationState.Working;
    }

    private void BeginEscape()
    {
        CareStation station = GetComponentInParent<CareStation>();
        if (station != null) station.ReleaseForEscape();

        IsEscaping = true;
        IsCornered = false;
        neglectTimer = 0f;
        escapeOrigin = transform.position;
        nextWanderTime = 0f;

        // สัตว์ที่เคยถูกอุ้มจะถูกปิด collider ไว้ (PlayerCarrier ปิด แล้ว CareStation.Consume ไม่ได้เปิดคืน)
        // ถ้าไม่เปิดคืน จะชนอะไรไม่ได้และเรย์ของผู้เล่นก็ยิงไม่โดน = ไล่จับไม่ได้
        foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = true;

        // เปิด physics จริงระหว่างหนี: แรงโน้มถ่วงดึงลงพื้นเอง และชนกำแพง/สถานีแทนที่จะมุดทะลุ
        if (body != null)
        {
            cachedIsKinematic = body.isKinematic;
            cachedConstraints = body.constraints;

            body.isKinematic = false;
            body.useGravity = true;
            body.constraints = RigidbodyConstraints.FreezeRotation; // กันตัวสัตว์ล้มกลิ้งตอนชนของ
        }

        GameLog.Push($"{pet.DisplayName} หนีแล้ว! วิ่งไปไล่จับแล้วกด E", LogTone.Warn);
    }

    private void EndEscape()
    {
        IsEscaping = false;
        IsCornered = false;
        neglectTimer = 0f;

        if (body == null) return;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = cachedIsKinematic;
        body.constraints = cachedConstraints;
    }

    private void UpdateEscape(float deltaTime)
    {
        if (pet.IsCarried) // ถูกจับได้ด้วยทางอื่น (เช่นสถานีส่งต่อ)
        {
            EndEscape();
            return;
        }

        Transform target = Player();
        Vector3 toPlayer = target != null ? Flatten(target.position - transform.position) : Vector3.zero;
        float playerDistance = target != null ? toPlayer.magnitude : float.MaxValue;

        float stopRadius = catchRadius * (PlayerProgress.Instance != null
            ? Mathf.Max(0.1f, PlayerProgress.Instance.CatchRadiusMultiplier)
            : 1f);

        IsCornered = playerDistance <= stopRadius;
        if (IsCornered)
        {
            Stop(); // จนมุมแล้ว ยืนรอให้กด E จับ
            return;
        }

        Vector3 direction = ChooseDirection(toPlayer, playerDistance);
        if (direction.sqrMagnitude < 0.0001f)
        {
            Stop();
            return;
        }

        Move(direction, deltaTime);
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    /// <summary>
    /// เดินด้วยความเร็วของ Rigidbody เฉพาะแนวราบ ปล่อยแกน Y ให้แรงโน้มถ่วงคุม
    /// สัตว์จึงตกลงพื้นเองหลังกระโดดลงจากสถานี และชนกำแพง/สถานีแทนที่จะมุดทะลุ
    /// </summary>
    private void Move(Vector3 direction, float deltaTime)
    {
        if (body == null)
        {
            // ไม่มี Rigidbody ในโมเดลสัตว์ตัวนี้ — ถอยไปใช้การเลื่อน transform แบบเดิมพร้อมขอบกริดกัน
            transform.position = ClampToShopFloor(transform.position + direction * fleeSpeed * deltaTime);
            return;
        }

        Vector3 velocity = direction * fleeSpeed;
        velocity.y = body.linearVelocity.y;
        body.linearVelocity = velocity;
    }

    private void Stop()
    {
        if (body == null) return;

        Vector3 velocity = body.linearVelocity;
        body.linearVelocity = new Vector3(0f, velocity.y, 0f);
    }

    private Vector3 ChooseDirection(Vector3 toPlayer, float playerDistance)
    {
        // ออกนอกรัศมีที่ยอมให้หนีแล้ว — เลี้ยวกลับเข้าหาจุดเริ่มหนี กันสัตว์หายออกนอกแผนที่
        Vector3 fromOrigin = Flatten(transform.position - escapeOrigin);
        if (fromOrigin.magnitude > wanderRadius) return -fromOrigin.normalized;

        if (playerDistance <= awarenessRadius && playerDistance > 0.01f)
        {
            return -toPlayer.normalized; // ผู้เล่นใกล้ = หนีออกห่าง
        }

        if (Time.time >= nextWanderTime)
        {
            Vector2 random = Random.insideUnitCircle.normalized;
            wanderDirection = new Vector3(random.x, 0f, random.y);
            nextWanderTime = Time.time + wanderInterval;
        }

        return wanderDirection;
    }

    /// <summary>
    /// ดึงตำแหน่งกลับเข้าในพื้นที่กริดร้าน ใช้เฉพาะกรณีสัตว์ไม่มี Rigidbody (เดินด้วย transform จึงทะลุกำแพงได้)
    /// ponytail: กันด้วยขอบกริด ไม่ได้ชนกำแพงจริง ถ้าต่อไปมีห้องหลายห้องค่อยเปลี่ยนไปใช้ NavMesh
    /// </summary>
    private Vector3 ClampToShopFloor(Vector3 position)
    {
        if (grid == null) return position;

        Vector3 min = grid.CellToWorld(Vector2Int.zero);
        Vector3 max = grid.CellToWorld(new Vector2Int(grid.Width - 1, grid.Height - 1));

        return new Vector3(
            Mathf.Clamp(position.x, Mathf.Min(min.x, max.x), Mathf.Max(min.x, max.x)),
            position.y,
            Mathf.Clamp(position.z, Mathf.Min(min.z, max.z), Mathf.Max(min.z, max.z)));
    }

    // ---------- การจับ (ปุ่ม interact เดิม) ----------

    /// <summary>
    /// สัตว์ที่อยู่บนสถานีเป็นลูกของสถานี เรย์/overlap ของผู้เล่นจึงโดนตัวสัตว์ก่อนโดนสถานีได้
    /// ตอนไม่ได้หนี จึงส่งทุกการกดต่อให้สถานีจัดการเหมือนเดิม (รวมการกดค้างของอ่างอาบน้ำ/แต่งขน)
    /// </summary>
    private CareStation ParentStation => GetComponentInParent<CareStation>();

    public bool UseHold
    {
        get
        {
            if (IsEscaping) return false; // ไล่จับใช้กดครั้งเดียว
            CareStation station = ParentStation;
            return station != null && station.UseHold;
        }
    }

    public void OnHoldStart(GameObject interactor) => ParentStation?.OnHoldStart(interactor);
    public void OnHoldTick(GameObject interactor, float heldSeconds) => ParentStation?.OnHoldTick(interactor, heldSeconds);
    public void OnHoldRelease(GameObject interactor, float heldSeconds) => ParentStation?.OnHoldRelease(interactor, heldSeconds);
    public void OnHoldCancel(GameObject interactor) => ParentStation?.OnHoldCancel(interactor);

    public void Interact(GameObject interactor)
    {
        if (!IsEscaping)
        {
            ParentStation?.Interact(interactor);
            return;
        }

        PlayerCarrier carrier = interactor != null ? interactor.GetComponent<PlayerCarrier>() : null;
        if (carrier == null) return;

        if (carrier.IsCarrying)
        {
            GameLog.Push($"มือไม่ว่าง — วางของที่ถืออยู่ก่อนถึงจะจับ {pet.DisplayName} ได้", LogTone.Warn);
            return;
        }

        EndEscape();

        if (carrier.Carry(gameObject))
        {
            GameLog.Push($"จับ {pet.DisplayName} กลับมาได้แล้ว", LogTone.Good);
        }
    }

    private Transform Player()
    {
        if (player != null) return player;

        PlayerCarrier carrier = FindAnyObjectByType<PlayerCarrier>();
        player = carrier != null ? carrier.transform : null;
        return player;
    }

    private static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, catchRadius);
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, awarenessRadius);
    }
}
