using UnityEngine;

/// <summary>
/// A tactile, physical object that acts as the "Ready" and "Next Level" button for the Archery Range.
/// Refactored to use Events (LevelProgressionManager), decoupling it from direct state logic.
/// Attach this script to your Gong model and ensure it has a valid Collider and Rigidbody (set to Kinematic).
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(AudioSource))]
public class LevelAdvanceGong : MonoBehaviour, IArrowTarget
{
    [Tooltip("Reference to the new Progression Manager orchestrating the state.")]
    public LevelProgressionManager progressionManager;

    [Tooltip("Sound to play when the gong is successfully hit.")]
    public AudioClip gongSound;

    private AudioSource audioSource;
    private bool isCoolingDown = false;
    private float hitCooldown = 3.0f;
    private float lastHitTime = -10f; // Track time to avoid Coroutines/Invoke

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;

        if (progressionManager == null)
        {
            progressionManager = FindFirstObjectByType<LevelProgressionManager>();
        }
    }

    private void Update()
    {
        if (isCoolingDown && Time.time - lastHitTime >= hitCooldown)
        {
            isCoolingDown = false;
        }
    }

    /// <summary>
    /// Called automatically by the StickingArrow when an arrow physically collides with this GameObject.
    /// </summary>
    public void OnArrowHit(float damage, Vector3 impactPoint, ElementTypeOB7 elementType)
    {
        if (isCoolingDown) return;

        if (progressionManager != null)
        {
            Debug.Log($"<color=cyan>[LevelAdvanceGong] Gong struck! Signaling Progression Manager...</color>");

            if (gongSound != null)
            {
                audioSource.PlayOneShot(gongSound);
            }

            progressionManager.ReceiveGongHit();

            isCoolingDown = true;
            lastHitTime = Time.time;
        }
        else
        {
            Debug.LogError("[LevelAdvanceGong] Cannot signal! LevelProgressionManager is missing!");
        }
    }
}
