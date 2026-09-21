using UnityEngine;
using Dreamteck.Splines;

/// <summary>
/// Attached to individual segments (Head, Body, Tail) of the Asian Dragon Boss.
/// Handles segment health and reports destruction to the main SegmentedDragonManager.
/// </summary>
[RequireComponent(typeof(SplineFollower))]
// NEW: Changes 4 of 4:
// 1. Added private bool isDead to prevent double-triggering.
// 2. Updated Awake() to force all nested colliders onto the Enemy layer automatically.
// 3. Updated TakeDamage() to check isDead and Die() to set isDead.
// 4. Updated Die() to pass FinalizeDestruction as a callback to DissolveEffect.
public class DragonSegment : MonoBehaviour, IArrowTarget
{
    [Header("Segment Stats")]
    [Tooltip("Can this individual piece be destroyed mid-fight? (Check True for body segments, False for Head/Legs/Tail).")]
    public bool isDestructiblePart = true;

    public float health = 100f;

    [Tooltip("The amount of power/energy this specific segment contributes to the boss's total power.")]
    public float powerContribution = 10f;

    [Header("Visuals & Physics")]
    [Tooltip("Reference to the child mesh renderer (useful for triggering visual effects).")]
    [SerializeField] private Renderer segmentRenderer;
    [Tooltip("Reference to the child collider (useful for disabling physics upon death).")]
    [SerializeField] public Collider segmentCollider;

    [Tooltip("The physics layer this segment will be forced onto so arrows can detect it. Displayed here as a reminder!")]
    [SerializeField] private string targetLayer = "Enemy";

    private SegmentedDragonManager dragonManager;
    private BossCreature bossBrain;
    private SplineFollower follower;

    // The index of this segment in the manager's list (Head = 0)
    public int SegmentIndex { get; set; }
    public SplineFollower Follower => follower;
    // NEW: 1. Prevent double death triggering
    private bool isDead = false;


    private void Awake()
    {
        follower = GetComponent<SplineFollower>();

        // Force the physics layer so arrows detect this segment, even if the dev forgot to set it!
        int layerIndex = LayerMask.NameToLayer(targetLayer);
        if (layerIndex != -1)
        {
            // NEW: 2. Automatically apply Enemy layer to all child colliders to fix hit detection.
            Collider[] allColliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider col in allColliders)
            {
                col.gameObject.layer = layerIndex;
            }

            gameObject.layer = layerIndex;
            // Also explicitly ensure the collider child is on the layer, as that's what physics actually hits
            if (segmentCollider != null)
            {
                segmentCollider.gameObject.layer = layerIndex;
            }
        }
        else
        {
            Debug.LogWarning($"[DragonSegment] Layer '{targetLayer}' does not exist in your project settings!");
        }
    }

    public void Initialize(SegmentedDragonManager manager, BossCreature brain, int index)
    {
        dragonManager = manager;
        bossBrain = brain;
        SegmentIndex = index;
    }

    public void OnRopeAttached(RopeArrow rope)
    {
        if (dragonManager != null)
        {
            dragonManager.HandleRopeAttached(this, rope);
        }
    }

    // Called by RopeArrow when it is cleaned up / detached from this segment
    public void OnRopeDetached(RopeArrow rope)
    {
        if (dragonManager != null)
        {
            dragonManager.ReleaseTetherFromSegment(this, rope);
        }
    }

    public void OnArrowHit(float damage, Vector3 impactPoint, ElementTypeOB7 elementType)
    {
        TakeDamage(damage, impactPoint, elementType);
    }

    /// <summary>
    /// Called when the player shoots this specific segment.
    /// </summary>
    public virtual void TakeDamage(float amount, Vector3 hitPoint, ElementTypeOB7 arrowType = ElementTypeOB7.Normal)
    {
        // Pass damage up to the brain so the overall boss loses health and can trigger evasions!
        if (isDead) return;

        if (bossBrain != null)
        {
            // The boss brain calculates actual damage using its own elemental modifiers
            bossBrain.TakeDamage(amount, hitPoint, arrowType);
        }

        // Only track local destruction if this is a breakable middle piece
        if (isDestructiblePart)
        {
            // Note: Currently, body segments just take raw base damage to pop off.
            // Elemental logic is managed centrally by the BossBrain above to control the overall health bar.
            health -= amount;
            Debug.Log($"<color=orange>[DragonSegment] Body Segment {SegmentIndex} took {amount} base damage. Local Health: {health}</color>");

            if (health <= 0)
            {
                Die();
            }
        }
        else
        {
            Debug.Log($"<color=yellow>[DragonSegment] Permanent piece {SegmentIndex} hit! Relayed {amount} base damage to Boss Brain.</color>");
        }
    }

    protected virtual void Die()
    {
        isDead = true;

        if (!isDestructiblePart) return;

        Debug.Log($"<color=red>[DragonSegment] Segment {SegmentIndex} health reached 0!</color>");

        // Disable physics immediately so arrows don't keep hitting it
        if (segmentCollider != null)
        {
            segmentCollider.enabled = false;
        }

        // Trigger dissolve on any arrows sticking out of this segment simultaneously.
        StickingArrow[] attachedArrows = GetComponentsInChildren<StickingArrow>(true);
        foreach (StickingArrow arrow in attachedArrows)
        {
            if (arrow != null)
            {
                DissolveEffect arrowDissolve = arrow.GetComponentInChildren<DissolveEffect>();
                if (arrowDissolve != null)
                {
                    arrowDissolve.TriggerDissolve();
                }
            }
        }

        DissolveEffect dissolve = GetComponentInChildren<DissolveEffect>();
        if (dissolve != null)
        {
            // NEW: 4. Passing callback to decoupled visual effect.
            dissolve.TriggerDissolve(FinalizeDestruction);
        }
        else
        {
            FinalizeDestruction();
        }
    }

    /// <summary>
    /// Called EXACTLY when the visual dissolve finishes via callback.
    /// Safely purges the segment from the tracking arrays and obliterates the GameObject hierarchy.
    /// </summary>
    private void FinalizeDestruction()
    {
        // Tell the manager to wipe this piece from the tracking array and close the gap!
        if (dragonManager != null)
        {
            dragonManager.OnSegmentDestroyed(this);
        }

        // Permanently destroy the root object, which automatically takes the Mesh, Collider, and Arrows with it.
        Destroy(gameObject);
    }

    /// <summary>
    /// Helper to grab the size of this segment in case needed externally.
    /// Size is calculated based on bounding box.
    /// </summary>
    public float GetSegmentSize()
    {
        Renderer r = segmentRenderer != null ? segmentRenderer : GetComponentInChildren<Renderer>();
        if (r != null)
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                return mf.sharedMesh.bounds.size.z * mf.transform.lossyScale.z;
            }
            return r.bounds.size.z;
        }
        return 2.0f;
    }

    /// <summary>
    /// Called by the SegmentedDragonManager when the entire boss is defeated.
    /// Forces permanent pieces (Head, Legs, Tail) to finally dissolve.
    /// </summary>
    public virtual void TriggerTotalDeath()  
    {
        if (segmentCollider != null)
        {
            segmentCollider.enabled = false;
        }

        // You can trigger the DissolveEffect.cs directly here if you have a reference to it,
        // otherwise Destroy(gameObject) will clean it up.
        Destroy(gameObject);
    }
}




//using UnityEngine;
//using Dreamteck.Splines;

///// <summary>
///// Attached to individual segments (Head, Body, Tail) of the Asian Dragon Boss.
///// Handles segment health and reports destruction to the main SegmentedDragonManager.
///// </summary>
//[RequireComponent(typeof(SplineFollower))]
//public class DragonSegment : MonoBehaviour, IArrowTarget
//{
//    [Header("Segment Stats")]
//    [Tooltip("Can this individual piece be destroyed mid-fight? (Check True for body segments, False for Head/Legs/Tail).")]
//    public bool isDestructiblePart = true;

//    public float health = 100f;

//    [Tooltip("The amount of power/energy this specific segment contributes to the boss's total power.")]
//    public float powerContribution = 10f;

//    [Header("Visuals & Physics")]
//    [Tooltip("Reference to the child mesh renderer (useful for triggering visual effects).")]
//    [SerializeField] private Renderer segmentRenderer;
//    [Tooltip("Reference to the child collider (useful for disabling physics upon death).")]
//    [SerializeField] public Collider segmentCollider;

//    [Tooltip("The physics layer this segment will be forced onto so arrows can detect it. Displayed here as a reminder!")]
//    [SerializeField] private string targetLayer = "Enemy";

//    private SegmentedDragonManager dragonManager;
//    private BossCreature bossBrain;
//    private SplineFollower follower;

//    // The index of this segment in the manager's list (Head = 0)
//    public int SegmentIndex { get; set; }
//    public SplineFollower Follower => follower;

//    private void Awake()
//    {
//        follower = GetComponent<SplineFollower>();

//        // Force the physics layer so arrows detect this segment, even if the dev forgot to set it!
//        int layerIndex = LayerMask.NameToLayer(targetLayer);
//        if (layerIndex != -1)
//        {
//            gameObject.layer = layerIndex;

//            // Also explicitly ensure the collider child is on the layer, as that's what physics actually hits
//            if (segmentCollider != null)
//            {
//                segmentCollider.gameObject.layer = layerIndex;
//            }
//        }
//        else
//        {
//            Debug.LogWarning($"[DragonSegment] Layer '{targetLayer}' does not exist in your project settings!");
//        }
//    }

//    public void Initialize(SegmentedDragonManager manager, BossCreature brain, int index)
//    {
//        dragonManager = manager;
//        bossBrain = brain;
//        SegmentIndex = index;
//    }

//    public void OnRopeAttached(RopeArrow rope)
//    {
//        if (dragonManager != null)
//        {
//            dragonManager.HandleRopeAttached(this, rope);
//        }
//    }

//    // Called by RopeArrow when it is cleaned up / detached from this segment
//    public void OnRopeDetached(RopeArrow rope)
//    {
//        if (dragonManager != null)
//        {
//            dragonManager.ReleaseTetherFromSegment(this, rope);
//        }
//    }

//    public void OnArrowHit(float damage, Vector3 impactPoint, ElementTypeOB7 elementType)
//    {
//        TakeDamage(damage, impactPoint, elementType);
//    }

//    /// <summary>
//    /// Called when the player shoots this specific segment.
//    /// </summary>
//    public virtual void TakeDamage(float amount, Vector3 hitPoint, ElementTypeOB7 arrowType = ElementTypeOB7.Normal)
//    {
//        // Pass damage up to the brain so the overall boss loses health and can trigger evasions!
//        if (bossBrain != null)
//        {
//            // The boss brain calculates actual damage using its own elemental modifiers
//            bossBrain.TakeDamage(amount, hitPoint, arrowType);
//        }

//        // Only track local destruction if this is a breakable middle piece
//        if (isDestructiblePart)
//        {
//            // Note: Currently, body segments just take raw base damage to pop off.
//            // Elemental logic is managed centrally by the BossBrain above to control the overall health bar.
//            health -= amount;
//            Debug.Log($"<color=orange>[DragonSegment] Body Segment {SegmentIndex} took {amount} base damage. Local Health: {health}</color>");

//            if (health <= 0)
//            {
//                Die();
//            }
//        }
//        else
//        {
//            Debug.Log($"<color=yellow>[DragonSegment] Permanent piece {SegmentIndex} hit! Relayed {amount} base damage to Boss Brain.</color>");
//        }
//    }

//    private void Start()
//    {
//        // For destructible body parts, we intercept the OnDissolveCompleted event right from the start.
//        // The DissolveEffect script naturally handles the arrow hit and plays the animation immediately.
//        // We just sit back and wait for it to finish, then we obliterate the root object and close the gap.
//        if (isDestructiblePart)
//        {
//            DissolveEffect dissolve = GetComponentInChildren<DissolveEffect>();
//            if (dissolve != null)
//            {
//                dissolve.OnDissolveCompleted += FinalizeDestruction;
//            }
//        }
//    }

//    protected virtual void Die()
//    {
//        if (!isDestructiblePart) return;

//        Debug.Log($"<color=red>[DragonSegment] Segment {SegmentIndex} health reached 0!</color>");

//        // Disable physics immediately so arrows don't keep hitting it
//        if (segmentCollider != null)
//        {
//            segmentCollider.enabled = false;
//        }

//        // Trigger dissolve on any arrows sticking out of this segment simultaneously.
//        StickingArrow[] attachedArrows = GetComponentsInChildren<StickingArrow>(true);
//        foreach (StickingArrow arrow in attachedArrows)
//        {
//            if (arrow != null)
//            {
//                DissolveEffect arrowDissolve = arrow.GetComponentInChildren<DissolveEffect>();
//                if (arrowDissolve != null)
//                {
//                    arrowDissolve.TriggerDissolve();
//                }
//            }
//        }

//        // Explicitly command the segment's visual effect to start dissolving!
//        // This will eventually fire the OnDissolveCompleted event we subscribed to in Start, 
//        // which will trigger FinalizeDestruction().
//        DissolveEffect dissolve = GetComponentInChildren<DissolveEffect>();
//        if (dissolve != null)
//        {
//            dissolve.TriggerDissolve();
//        }
//        else
//        {
//            // Fallback: If no DissolveEffect exists to fire the event, we just destroy it now
//            FinalizeDestruction();
//        }
//    }

//    private void OnDestroy()
//    {
//        // Clean up the event listener to avoid memory leaks
//        DissolveEffect dissolve = GetComponentInChildren<DissolveEffect>();
//        if (dissolve != null)
//        {
//            dissolve.OnDissolveCompleted -= FinalizeDestruction;
//        }
//    }

//    /// <summary>
//    /// Called EXACTLY when the visual dissolve finishes via callback.
//    /// Safely purges the segment from the tracking arrays and obliterates the GameObject hierarchy.
//    /// </summary>
//    private void FinalizeDestruction()
//    {
//        // Tell the manager to wipe this piece from the tracking array and close the gap!
//        if (dragonManager != null)
//        {
//            dragonManager.OnSegmentDestroyed(this);
//        }

//        // Permanently destroy the root object, which automatically takes the Mesh, Collider, and Arrows with it.
//        Destroy(gameObject);
//    }

//    /// <summary>
//    /// Helper to grab the size of this segment in case needed externally.
//    /// Size is calculated based on bounding box.
//    /// </summary>
//    public float GetSegmentSize()
//    {
//        Renderer r = segmentRenderer != null ? segmentRenderer : GetComponentInChildren<Renderer>();
//        if (r != null)
//        {
//            MeshFilter mf = r.GetComponent<MeshFilter>();
//            if (mf != null && mf.sharedMesh != null)
//            {
//                return mf.sharedMesh.bounds.size.z * mf.transform.lossyScale.z;
//            }
//            return r.bounds.size.z;
//        }
//        return 2.0f;
//    }

//    /// <summary>
//    /// Called by the SegmentedDragonManager when the entire boss is defeated.
//    /// Forces permanent pieces (Head, Legs, Tail) to finally dissolve.
//    /// </summary>
//    public virtual void TriggerTotalDeath()
//    {
//        if (segmentCollider != null)
//        {
//            segmentCollider.enabled = false;
//        }

//        // You can trigger the DissolveEffect.cs directly here if you have a reference to it,
//        // otherwise Destroy(gameObject) will clean it up.
//        Destroy(gameObject);
//    }
//}
