using UnityEngine;

/// <summary>
/// Attaches to the visual Dragon Head object.
/// Applies a continuous sinusoidal local offset to simulate a snake/fish swimming motion.
/// Because DragonMovementManager records the transform of the object this is attached to,
/// this single script causes the entire body to naturally undulate as they follow the breadcrumbs.
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

    // We need to keep track of the root's speed to scale the wobble dynamically.
    private Vector3 lastRootPosition;
    private float currentSpeed;
    private Transform rootTransform;

    // We store the initial local transform so we can oscillate around a clean zero-point
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;

    private void Start()
    {
        // Assuming this is a child of the main boss root
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

        // Calculate root speed to dampen the wobble when standing still
        if (rootTransform != null && scaleWithSpeed)
        {
            float distance = Vector3.Distance(rootTransform.position, lastRootPosition);
            currentSpeed = distance / Time.deltaTime;
            lastRootPosition = rootTransform.position;

            // Normalize speed multiplier (assuming max speed is around 18m/s from escapeSpeed)
            speedMultiplier = Mathf.Clamp01(currentSpeed / 5f);
        }

        // Calculate the swinging motion based on time
        float wave = Mathf.Sin(Time.time * swaySpeed);

        // Move the head side to side
        Vector3 localOffset = new Vector3(wave * sideToSideDistance * speedMultiplier, 0f, 0f);
        transform.localPosition = initialLocalPosition + localOffset;

        // Twist the head to look in the direction it is swinging
        Quaternion rotationOffset = Quaternion.Euler(0f, wave * headTurnAngle * speedMultiplier, 0f);
        transform.localRotation = initialLocalRotation * rotationOffset;
    }
}
