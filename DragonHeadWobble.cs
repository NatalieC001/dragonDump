using UnityEngine;

/// <summary>
/// Attaches to the visual Dragon Head object.
/// Applies a continuous sinusoidal local offset to simulate a snake/fish swimming motion.
/// Because DragonMovementManager records the transform of the object this is attached to,
/// this single script causes the entire body to naturally undulate as they follow the breadcrumbs.
/// </summary>
public class DragonHeadWobble : MonoBehaviour
{
    [Header("Wobble Settings")]
    [Tooltip("How fast the head snakes side-to-side.")]
    public float wobbleFrequency = 3f;

    [Tooltip("How wide the head swings on the local X axis.")]
    public float positionAmplitude = 0.5f;

    [Tooltip("How much the head rotates (yaw) during the swing.")]
    public float rotationAmplitude = 15f;

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

        // Calculate the sine wave based on time
        float wave = Mathf.Sin(Time.time * wobbleFrequency);

        // Apply local position sway (side to side)
        Vector3 localOffset = new Vector3(wave * positionAmplitude * speedMultiplier, 0f, 0f);
        transform.localPosition = initialLocalPosition + localOffset;

        // Apply local rotation sway (yaw)
        Quaternion rotationOffset = Quaternion.Euler(0f, wave * rotationAmplitude * speedMultiplier, 0f);
        transform.localRotation = initialLocalRotation * rotationOffset;
    }
}
