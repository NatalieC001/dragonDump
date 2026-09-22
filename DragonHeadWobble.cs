using UnityEngine;

/// <summary>
/// Attaches to the visual Dragon Head object (DragonSegment_0).
/// Applies a continuous sinusoidal local offset to simulate a snake/fish swimming motion.
/// </summary>
public class DragonHeadWobble : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("The speed of the swing cycle. How fast the head swings left and right per second.")]
    public float swaySpeed = 3f;

    [Tooltip("PHYSICAL SLIDING: How far (in meters) the head physically slides side-to-side along its local X axis.")]
    public float sideToSideDistance = 0.5f;

    [Tooltip("TWISTING / LOOKING: How much the head twists (in degrees) around its local Y axis when looking left and right.")]
    public float headTurnAngle = 15f;

    [Tooltip("If true, the wobble fades out when the root is moving very slowly (e.g. resting).")]
    public bool scaleWithSpeed = true;

    private Vector3 lastRootPosition;
    private float currentSpeed;
    private Transform rootTransform;

    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;

    private void Start()
    {
        rootTransform = transform.parent;
        if (rootTransform != null)
        {
            lastRootPosition = rootTransform.position;
        }

        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
    }

    private void LateUpdate()
    {
        float speedMultiplier = 1f;

        if (rootTransform != null && scaleWithSpeed)
        {
            float distance = Vector3.Distance(rootTransform.position, lastRootPosition);
            currentSpeed = distance / Time.deltaTime;
            lastRootPosition = rootTransform.position;
            speedMultiplier = Mathf.Clamp01(currentSpeed / 5f);
        }

        float wave = Mathf.Sin(Time.time * swaySpeed);

        Vector3 localOffset = new Vector3(wave * sideToSideDistance * speedMultiplier, 0f, 0f);
        transform.localPosition = initialLocalPosition + localOffset;

        Quaternion rotationOffset = Quaternion.Euler(0f, wave * headTurnAngle * speedMultiplier, 0f);
        transform.localRotation = initialLocalRotation * rotationOffset;
    }
}
