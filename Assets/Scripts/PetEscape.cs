using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// สมองของสัตว์เลี้ยง — ตัวรันเครื่องจักรสถานะ (FSM) และเป็น blackboard ให้สถานะใช้ร่วมกัน
///
/// FSM 3 สถานะ (ไฟล์สถานะเป็น ScriptableObject ดู PetState.cs):
///
///     [สงบ] --ถูกทิ้งครบเวลา--> [หนี] --ผู้เล่นเข้าใกล้--> [จนมุม]
///       ^                        ^                         |
///       |                        +---ผู้เล่นถอยห่าง--------+
///       +------------------ถูกอุ้ม / ถูกจับ-----------------+
///
/// การเคลื่อนที่ตอนหนีใช้ NavMeshAgent:
///   - อ้อมสถานีกับกำแพงแทนที่จะมุดทะลุ (สถานีทุกชิ้นมี NavMeshObstacle แบบ carve จาก GridOccupant)
///   - ตัวสัตว์ติดพื้น NavMesh เสมอ ไม่ลอยและไม่ตกทะลุพื้น
///   - ไม่มี NavMesh ในฉาก (เช่นฉากเทสเปล่า) จะถอยไปเลื่อน transform ตรงๆ แทน ยังเล่นได้
///
/// จับด้วยปุ่ม interact เดิม (E) — สคริปต์นี้เป็น IHoldInteractable เอง
/// Pet.Awake ใส่ component นี้ให้อัตโนมัติ ไม่ต้องแก้ prefab สัตว์ทุกตัว
/// </summary>
[RequireComponent(typeof(Pet))]
public class PetEscape : MonoBehaviour, IHoldInteractable
{
    [Header("States (ScriptableObject)")]
    [Tooltip("ชุดสถานะของสัตว์ตัวนี้ ปล่อยว่างจะสร้างชุดมาตรฐานให้ตอนเริ่มเกม")]
    [SerializeField] private PetState calmState;
    [SerializeField] private PetState fleeState;
    [SerializeField] private PetState corneredState;

    [Header("Agent")]
    [Tooltip("รัศมีตัวสัตว์สำหรับการหาทาง — เล็กกว่าช่องกริด (1) เพื่อให้ลอดช่องระหว่างสถานีได้")]
    [SerializeField] private float agentRadius = 0.28f;

    [Tooltip("ความเร็วหมุนตัวตอนเปลี่ยนทิศ")]
    [SerializeField] private float angularSpeed = 720f;

    /// <summary>กำลังหนีอยู่หรือไม่ — HUD กับ InteractionManager อ่านค่านี้</summary>
    public bool IsEscaping => current != null && current != calmState;

    /// <summary>หยุดรอให้จับอยู่ (ผู้เล่นเข้ามาในรัศมีจับแล้ว)</summary>
    public bool IsCornered => current == corneredState;

    /// <summary>ชื่อสถานะปัจจุบัน ใช้ดีบัก/โชว์ตอนนำเสนอ</summary>
    public string StateName => current != null ? current.displayName : "-";

    // ---------- blackboard ที่สถานะใช้ร่วมกัน ----------
    public Pet Pet { get; private set; }
    public PetState CalmState => calmState;
    public float NeglectTimer { get; set; }
    public float NextRepathTime { get; set; }

    private PetState current;
    private NavMeshAgent agent;
    private Rigidbody body;
    private Transform player;
    private TileGrid grid;

    private Vector3 escapeOrigin;
    private bool cachedIsKinematic;
    private RigidbodyConstraints cachedConstraints;
    private float moveSpeed = 2.6f;

    private void Awake()
    {
        Pet = GetComponent<Pet>();
        body = GetComponent<Rigidbody>();
        grid = FindAnyObjectByType<TileGrid>();

        EnsureStates();
        SetupAgent();

        current = calmState;
        current.Enter(this);
    }

    /// <summary>
    /// ไม่ได้ผูกไฟล์สถานะใน Inspector ก็เล่นได้ — สร้างชุดมาตรฐานขึ้นมาใช้ในหน่วยความจำ
    /// (อยากปรับค่า: สร้าง asset จากเมนู Create → Pet Corner → Pet State แล้วลากใส่ช่องด้านบน)
    /// </summary>
    private void EnsureStates()
    {
        if (calmState != null && fleeState != null && corneredState != null) return;

        PetCalmState calm = ScriptableObject.CreateInstance<PetCalmState>();
        PetFleeState flee = ScriptableObject.CreateInstance<PetFleeState>();
        PetCorneredState cornered = ScriptableObject.CreateInstance<PetCorneredState>();

        calm.displayName = "สงบ";
        flee.displayName = "หนี";
        cornered.displayName = "จนมุม";

        // ผูกปลายทางของแต่ละสถานะเข้าหากัน (ช่องพวกนี้เป็น private ใน asset จึงตั้งผ่าน SerializedObject ไม่ได้ตอน runtime)
        calm.LinkRuntime(flee);
        flee.LinkRuntime(cornered);
        cornered.LinkRuntime(flee);

        calmState = calm;
        fleeState = flee;
        corneredState = cornered;
    }

    private void SetupAgent()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();

        agent.radius = agentRadius;
        agent.height = Mathf.Max(0.3f, Pet.VisualBounds.size.y);
        agent.angularSpeed = angularSpeed;
        agent.acceleration = 20f;
        agent.stoppingDistance = 0.1f;
        agent.autoBraking = true;
        agent.enabled = false; // เปิดเฉพาะตอนหนี เวลาถูกอุ้ม/อยู่บนสถานีให้ตำแหน่งถูกคุมโดยพ่อแม่
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f || current == null) return; // เกมหยุดอยู่

        PetState next = current.Tick(this, deltaTime);
        if (next == current || next == null) return;

        current.Exit(this);

        // ออกจากกลุ่มสถานะหนีกลับมาสงบ = คืนค่า physics และปิด agent
        if (next == calmState) EndRunaway();

        current = next;
        current.Enter(this);
    }

    // ---------- เงื่อนไขที่สถานะเรียกใช้ ----------

    /// <summary>ถูกทิ้งอยู่ไหม — ไม่ได้ถูกอุ้ม ออเดอร์ยังเดินอยู่ และไม่มีใครดูแล</summary>
    public bool IsNeglected()
    {
        if (Pet.IsCarried) return false;
        if (!Pet.IsPatienceRunning) return false; // ยังไม่รับออเดอร์ / จบออเดอร์ไปแล้ว

        // ห้องรอข้างเคาน์เตอร์มีคนดูแล สัตว์ในนั้นจึงไม่หนี (โต๊ะพักกลางร้านไม่นับ)
        if (GetComponentInParent<PetWaitingRoom>() != null) return false;

        CareStation station = GetComponentInParent<CareStation>();
        return station == null || station.State != CareStation.StationState.Working;
    }

    public float DistanceToPlayer()
    {
        Transform target = Player();
        if (target == null) return float.MaxValue;

        Vector3 delta = target.position - transform.position;
        delta.y = 0f;
        return delta.magnitude;
    }

    /// <summary>รัศมีที่ปรับด้วยบัฟ CatchRadius จากการ์ดรางวัลแล้ว</summary>
    public float ScaledCatchRadius(float baseRadius)
    {
        float multiplier = PlayerProgress.Instance != null
            ? Mathf.Max(0.1f, PlayerProgress.Instance.CatchRadiusMultiplier)
            : 1f;

        return baseRadius * multiplier;
    }

    // ---------- การเคลื่อนที่ ----------

    /// <summary>เริ่มหนี: หลุดจากสถานี เปิด collider คืน ปิด physics แล้วส่งตัวขึ้น NavMesh</summary>
    public void BeginRunaway(float speed)
    {
        moveSpeed = speed;

        CareStation station = GetComponentInParent<CareStation>();
        if (station != null) station.ReleaseForEscape();
        transform.SetParent(null);

        // สัตว์ที่เคยถูกอุ้มจะถูกปิด collider ไว้ ถ้าไม่เปิดคืน เรย์ของผู้เล่นจะยิงไม่โดน = ไล่จับไม่ได้
        foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = true;

        if (body != null)
        {
            cachedIsKinematic = body.isKinematic;
            cachedConstraints = body.constraints;

            // NavMeshAgent เป็นคนขยับตัวสัตว์ จึงปิด physics ไว้ ไม่ให้สองระบบแย่งกันคุมตำแหน่ง
            body.isKinematic = true;
            body.constraints = RigidbodyConstraints.FreezeRotation;
        }

        escapeOrigin = transform.position;

        if (agent != null && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
        {
            agent.enabled = true;
            agent.Warp(hit.position); // วางตัวลงบนพื้น NavMesh พอดี แก้อาการลอยหลังกระโดดลงจากสถานี
            agent.speed = speed;
            agent.isStopped = false;
            escapeOrigin = hit.position;
        }

        RunStats.NoteEscape();
        GameLog.Push($"{Pet.DisplayName} หนีแล้ว! วิ่งไปไล่จับแล้วกด E", LogTone.Warn);
    }

    /// <summary>กลับสู่สถานะสงบ — ปิด agent และคืนค่า physics เดิม</summary>
    public void EndRunaway()
    {
        if (agent != null && agent.enabled)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.enabled = false;
        }

        if (body == null) return;

        // ระหว่างหนี body เป็น kinematic ตลอด (agent เป็นคนขยับ) จึงไม่มีความเร็วค้างให้ล้าง
        body.isKinematic = cachedIsKinematic;
        body.constraints = cachedConstraints;
    }

    public void MoveTo(Vector3 destination)
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(destination);
            return;
        }

        // ไม่มี NavMesh ในฉาก — เลื่อนตรงๆ พร้อมกันไม่ให้หลุดขอบกริด
        Vector3 direction = destination - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;

        transform.position = ClampToShopFloor(transform.position + direction.normalized * moveSpeed * Time.deltaTime);
        transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    public void StopMoving()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    public void FacePlayer()
    {
        Transform target = Player();
        if (target == null) return;

        Vector3 look = target.position - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
    }

    /// <summary>
    /// เลือกจุดหมายของการหนี — หนีออกจากผู้เล่นถ้าใกล้ ไม่งั้นสุ่มจุดรอบๆ จุดที่เริ่มหนี
    /// แล้วดึงจุดนั้นให้มาอยู่บน NavMesh เสมอ สัตว์จะได้ไม่พยายามวิ่งเข้ากำแพง
    /// </summary>
    public Vector3 PickFleeDestination(bool playerIsNear, float wanderRadius)
    {
        Vector3 candidate;

        if (playerIsNear && Player() != null)
        {
            Vector3 away = transform.position - Player().position;
            away.y = 0f;
            candidate = transform.position + away.normalized * 3.5f;
        }
        else
        {
            Vector2 random = Random.insideUnitCircle * wanderRadius;
            candidate = escapeOrigin + new Vector3(random.x, 0f, random.y);
        }

        // ไม่ให้หนีไกลเกินรัศมีจากจุดเริ่ม กันสัตว์หายไปมุมร้าน
        Vector3 fromOrigin = candidate - escapeOrigin;
        fromOrigin.y = 0f;
        if (fromOrigin.magnitude > wanderRadius) candidate = escapeOrigin + fromOrigin.normalized * wanderRadius;

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) return hit.position;

        return ClampToShopFloor(candidate);
    }

    /// <summary>กันตำแหน่งให้อยู่ในกริดร้าน ใช้เฉพาะตอนไม่มี NavMesh ให้เดิน</summary>
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

    /// <summary>ผู้เล่นที่อยู่ใกล้ที่สุด — เล่นหลายคน สัตว์จะหนีคนที่เข้ามาใกล้สุดและหยุดให้คนนั้นจับ</summary>
    private Transform Player()
    {
        player = null;
        float best = float.MaxValue;

        foreach (PlayerController candidate in PlayerController.All)
        {
            if (candidate == null) continue;

            float dist = (candidate.transform.position - transform.position).sqrMagnitude;
            if (dist < best)
            {
                best = dist;
                player = candidate.transform;
            }
        }

        return player;
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
            if (ParentStation != null) ParentStation.Interact(interactor);
            else GetComponentInParent<PetWaitingRoom>()?.Interact(interactor);
            return;
        }

        PlayerCarrier carrier = interactor != null ? interactor.GetComponent<PlayerCarrier>() : null;
        if (carrier == null) return;

        if (carrier.IsCarrying)
        {
            GameLog.Push($"มือไม่ว่าง — วางของที่ถืออยู่ก่อนถึงจะจับ {Pet.DisplayName} ได้", LogTone.Warn);
            return;
        }

        // ออกจากสถานะหนีก่อน ไม่งั้น agent จะแย่งคุมตำแหน่งกับมือผู้เล่น
        current.Exit(this);
        EndRunaway();
        current = calmState;
        current.Enter(this);

        if (carrier.Carry(gameObject))
        {
            RunStats.NoteCatch();
            GameLog.Push($"จับ {Pet.DisplayName} กลับมาได้แล้ว", LogTone.Good);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 1.6f);

        if (Application.isPlaying && IsEscaping)
        {
            Gizmos.color = new Color(1f, 0.5f, 0.2f);
            Gizmos.DrawLine(transform.position, escapeOrigin);
        }
    }
}
