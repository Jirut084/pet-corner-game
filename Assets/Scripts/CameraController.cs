using UnityEngine;

/// <summary>
/// Versatile follow camera for the Pet Corner prototype.
/// Switch CameraMode to reuse the same script for both test scenes:
/// Scene A (ThirdPerson, mouse look) and Scene B (TopDown2_5D, fixed angle).
/// </summary>
public class CameraController : MonoBehaviour
{
    public enum CameraMode
    {
        ThirdPerson,   // Follows behind the target, supports mouse look.
        TopDown2_5D    // Fixed downward angle, no rotation input.
    }

    [Header("Mode")]
    [SerializeField] private CameraMode cameraMode = CameraMode.ThirdPerson;

    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Third Person Settings")]
    [SerializeField] private Vector3 thirdPersonOffset = new Vector3(0f, 2.5f, -4f);
    [SerializeField] private float mouseSensitivity = 3f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 60f;

    [Header("Top-Down 2.5D Settings")]
    [SerializeField] private Vector3 topDownOffset = new Vector3(0f, 8f, -6f);
    [SerializeField] private float topDownPitch = 45f;

    [Header("Follow Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.15f;

    private Vector3 velocityRef; // Used internally by SmoothDamp.
    private float yaw;
    private float pitch;

    private void Start()
    {
        if (cameraMode == CameraMode.ThirdPerson)
        {
            Cursor.lockState = CursorLockMode.Locked;
            yaw = target != null ? target.eulerAngles.y : 0f;
            pitch = 15f;
        }
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        switch (cameraMode)
        {
            case CameraMode.ThirdPerson:
                UpdateThirdPerson();
                break;

            case CameraMode.TopDown2_5D:
                UpdateTopDown();
                break;
        }
    }

    private void UpdateThirdPerson()
    {
        // Mouse look input controls yaw/pitch around the target.
        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredPosition = target.position + rotation * thirdPersonOffset;

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocityRef, positionSmoothTime);
        transform.LookAt(target.position + Vector3.up * 1.5f);
    }

    private void UpdateTopDown()
    {
        // Fixed rotation looking down at the target, offset stays constant relative to world axes.
        Vector3 desiredPosition = target.position + topDownOffset;

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocityRef, positionSmoothTime);
        transform.rotation = Quaternion.Euler(topDownPitch, 0f, 0f);
    }
}
