using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Abstract base class for all Boss movement variants.
/// Holds shared logic: interacting with the BossPathManager, escape route selection, tethering, and phase hooks.
/// Forces leaf classes to implement their own movement ticking.
/// </summary>
public abstract class BaseBossMovement : MonoBehaviour
{
    protected BossPathManager pathManager;
    protected BossCreature bossBrain; // The brain that dictates desires (flee, attack)

    [Header("Base State")]
    public bool isTethered = false;
    protected GameObject currentTargetCrystal = null;

    protected virtual void Awake()
    {
        bossBrain = GetComponent<BossCreature>();
        pathManager = FindFirstObjectByType<BossPathManager>();

        if (pathManager == null)
        {
            Debug.LogError($"[{gameObject.name}] BossPathManager is missing from the scene!");
        }
    }

    protected virtual void Update()
    {
        // Concrete implementations will override this to handle their specific movement ticking
        TickMovement();
    }

    /// <summary>
    /// The core movement loop, to be implemented by Airborne, Ground, or Hybrid classes.
    /// </summary>
    protected abstract void TickMovement();

    /// <summary>
    /// Phase change hook. Called by the BossCreature brain when health thresholds are crossed.
    /// </summary>
    public virtual void OnPhaseChanged(int newPhase)
    {
        Debug.Log($"[{gameObject.name}] Movement system reacting to Phase {newPhase}");
    }

    /// <summary>
    /// Forces the movement system to jump to the nearest escape route immediately.
    /// To be implemented by leaf classes (Airborne, Ground, Hybrid).
    /// </summary>
    public abstract void ForceImmediateEvasion();

    // --- Shared Path & Desire Logic ---

    /// <summary>
    /// Requests the closest appropriate escape route dynamically from the manager.
    /// </summary>
    protected GameObject FindNearestEscapeRoute(PathTypeTag.PathType movementType)
    {
        if (pathManager == null) return null;
        return pathManager.GetNearestEscapePath(transform.position, movementType);
    }

    /// <summary>
    /// Requests an observation path dynamically to circle around a specific crystal.
    /// </summary>
    public GameObject GetObservationPath(PathTypeTag.PathType movementType)
    {
        if (pathManager == null) return null;
        
        List<GameObject> observationPaths = pathManager.GetObservationPaths(movementType);
        if (observationPaths.Count > 0)
        {
            // For now, just grab a random observation path. Can be expanded to filter by proximity to a specific crystal.
            return observationPaths[Random.Range(0, observationPaths.Count)];
        }
        return null;
    }

    // --- Shared Health Crystal Logic ---

    public void SetTargetCrystal(GameObject crystal)
    {
        currentTargetCrystal = crystal;
    }

    // --- Shared Tether Logic ---

    public virtual void HandleTetherAttached()
    {
        isTethered = true;
        // Rubber band logic would limit distance to tether anchor here
    }

    public virtual void HandleTetherDetached()
    {
        isTethered = false;
    }
}
