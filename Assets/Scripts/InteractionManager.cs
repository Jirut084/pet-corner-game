using UnityEngine;

/// <summary>
/// Implement this on any object the player should be able to interact with
/// (pet cages, food bowl, bath point, intake/return point, etc.).
/// </summary>
public interface IInteractable
{
    void Interact(GameObject interactor);
}

/// <summary>
/// Handles the player's interaction input (default: E key).
/// Supports two detection modes so the same prototype logic can be tested
/// with a center-screen raycast (Scene A, third-person) or an overlap
/// check in front of the character (Scene B, top-down).
/// </summary>
public class InteractionManager : MonoBehaviour
{
    public enum InteractionType
    {
        Raycast, // Fires from the camera's center point forward (Minecraft-style)
        Overlap  // Checks a sphere in front of the player's current facing direction
    }

    [Header("Mode")]
    [SerializeField] private InteractionType interactionType = InteractionType.Raycast;

    [Header("Common Settings")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask interactableLayers = ~0; // Default: all layers

    [Header("Raycast Mode")]
    [Tooltip("Camera used as the raycast origin. Defaults to Camera.main if left empty.")]
    [SerializeField] private Camera interactionCamera;

    [Header("Overlap Mode")]
    [SerializeField] private float overlapRadius = 1f;
    [Tooltip("How far in front of the player (along facing direction) the overlap sphere is centered.")]
    [SerializeField] private float overlapForwardOffset = 1f;

    private PlayerController playerController;

    // Last interactable found, exposed for optional UI prompts (e.g. "Press E").
    public IInteractable CurrentTarget { get; private set; }

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();

        if (interactionCamera == null)
        {
            interactionCamera = Camera.main;
        }
    }

    private void Update()
    {
        CurrentTarget = interactionType == InteractionType.Raycast
            ? FindTargetViaRaycast()
            : FindTargetViaOverlap();

        if (Input.GetKeyDown(interactKey))
        {
            CurrentTarget?.Interact(gameObject);
        }
    }

    private IInteractable FindTargetViaRaycast()
    {
        if (interactionCamera == null)
        {
            return null;
        }

        Ray ray = new Ray(interactionCamera.transform.position, interactionCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayers))
        {
            return hit.collider.GetComponentInParent<IInteractable>();
        }

        return null;
    }

    private IInteractable FindTargetViaOverlap()
    {
        Vector3 facing = playerController != null ? playerController.LastMoveDirection : transform.forward;
        Vector3 checkCenter = transform.position + facing.normalized * overlapForwardOffset;

        Collider[] hits = Physics.OverlapSphere(checkCenter, overlapRadius, interactableLayers);

        // Pick the closest valid IInteractable within the overlap.
        IInteractable closest = null;
        float closestDistSqr = float.MaxValue;

        foreach (Collider col in hits)
        {
            IInteractable interactable = col.GetComponentInParent<IInteractable>();
            if (interactable == null)
            {
                continue;
            }

            float distSqr = (col.transform.position - checkCenter).sqrMagnitude;
            if (distSqr < closestDistSqr)
            {
                closestDistSqr = distSqr;
                closest = interactable;
            }
        }

        return closest;
    }

    private void OnDrawGizmosSelected()
    {
        // Visual debug aid in the Scene view.
        if (interactionType == InteractionType.Overlap)
        {
            Vector3 facing = Application.isPlaying && playerController != null
                ? playerController.LastMoveDirection
                : transform.forward;
            Vector3 checkCenter = transform.position + facing.normalized * overlapForwardOffset;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(checkCenter, overlapRadius);
        }
        else if (interactionCamera != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(interactionCamera.transform.position,
                interactionCamera.transform.position + interactionCamera.transform.forward * interactRange);
        }
    }
}
