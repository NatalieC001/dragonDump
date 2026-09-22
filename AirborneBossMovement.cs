using UnityEngine;
using Dreamteck.Splines;

/// <summary>
/// Inherits from BaseBossMovement.
/// Used for purely flying bosses (like the Dragon or Butterfly).
/// Exclusively requests Airborne paths from the BossPathManager.
///
/// Movement is driven by the ROOT object's SplineFollower (assigned here),
/// and SegmentedDragonManager's breadcrumb system drags all body segments behind it.
/// </summary>
public class AirborneBossMovement : BaseBossMovement
{
    [Header("Airborne Movement Settings")]
    [Tooltip("Speed at which the dragon follows its observation spline (units/sec).")]
    public float observationSpeed = 8f;

    [Tooltip("Speed at which the dragon flies along an escape route.")]
    public float escapeSpeed = 18f;

    private GameObject currentActivePath;
    private SplineFollower rootFollower;

    // --- Blending State (Fish Steering) ---
    private bool isBlending = false;
    private SplineComputer targetSplineForBlend;
    private float targetFollowSpeedForBlend;
    private bool isCurrentPathLooping = true;

    // Steering parameters
    [Header("Transition Steering")]
    [Tooltip("How sharply the dragon turns toward a new flight path.")]
    public float steeringTurnSpeed = 2f;
    [Tooltip("Distance threshold to snap onto the target spline.")]
    public float splineDockingRadius = 2f;

    // --- Initialise once the scene is ready ---
    protected override void Awake()
    {
        base.Awake(); // finds pathManager and bossBrain

        // The root object IS the SplineFollower that drives the whole dragon.
        rootFollower = GetComponent<SplineFollower>();
        if (rootFollower == null)
        {
            rootFollower = gameObject.AddComponent<SplineFollower>();
            Debug.Log($"[{gameObject.name}] AirborneBossMovement: Added SplineFollower to root.");
        }

        // Start with following disabled — Start() will assign the first path.
        rootFollower.follow = false;
    }

    protected virtual void Start()
    {
        InitialiseOnObservationPath();
    }

    /// <summary>
    /// Picks the first available Airborne observation path and snaps the root onto it.
    /// Called once on Start so the dragon is immediately visible and moving.
    /// </summary>
    private void InitialiseOnObservationPath()
    {
        if (pathManager == null)
        {
            Debug.LogWarning($"[{gameObject.name}] AirborneBossMovement: No BossPathManager in scene — cannot initialise path.");
            return;
        }

        currentActivePath = GetObservationPath(PathTypeTag.PathType.Airborne);

        if (currentActivePath != null)
        {
            // Initialisation is the ONLY time we snap instantly so we don't blend from 0,0,0
            SplineComputer splineComputer = currentActivePath.GetComponentInChildren<SplineComputer>();
            if (splineComputer != null && rootFollower != null)
            {
                rootFollower.spline = splineComputer;
                rootFollower.followSpeed = observationSpeed;
                rootFollower.wrapMode = SplineFollower.Wrap.Loop;

                SplineSample startSample = new SplineSample();
                splineComputer.Evaluate(0.0, ref startSample);
                transform.position = startSample.position;
                transform.rotation = startSample.rotation;
                rootFollower.SetPercent(0.0);

                rootFollower.follow = true;
            }
            Debug.Log($"<color=green>[{gameObject.name}] AirborneBossMovement: Dragon placed instantly on observation path '{currentActivePath.name}'.</color>");
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] AirborneBossMovement: No Airborne observation paths found. Dragon will stay at spawn position.");
        }
    }

    /// <summary>
    /// Assigns a SplineComputer from the given path GameObject to the root SplineFollower and starts following.
    /// </summary>
    private void AssignSplineAndFollow(GameObject pathObj, float speed, bool looping = true)
    {
        if (rootFollower == null || pathObj == null) return;
        isCurrentPathLooping = looping;

        SplineComputer splineComputer = pathObj.GetComponentInChildren<SplineComputer>();
        if (splineComputer == null)
        {
            Debug.LogWarning($"[{gameObject.name}] Path '{pathObj.name}' has no SplineComputer. Cannot follow.");
            return;
        }

        // Initiate fish-like steering toward the new track
        isBlending = true;
        targetSplineForBlend = splineComputer;
        targetFollowSpeedForBlend = speed;

        rootFollower.follow = false; // Disable rigid track-following until we get there
    }

    private void FinalizeSplineAttachment()
    {
        if (rootFollower == null || targetSplineForBlend == null) return;

        rootFollower.spline = targetSplineForBlend;
        rootFollower.followSpeed = targetFollowSpeedForBlend;
        rootFollower.wrapMode = isCurrentPathLooping ? SplineFollower.Wrap.Loop : SplineFollower.Wrap.Default;

        // Project to get exact percent and set it before enabling follow
        SplineSample sample = new SplineSample();
        targetSplineForBlend.Project(transform.position, ref sample);
        rootFollower.SetPercent(sample.percent);

        rootFollower.follow = true;

        isBlending = false;
        targetSplineForBlend = null;
    }

    // --- Core Movement Tick ---

    protected override void TickMovement()
    {
        if (isTethered)
        {
            ApplyTetherRubberBand();
            return;
        }

        // If we are currently transitioning to a new spline, handle that and block other movement
        if (isBlending)
        {
            UpdateBlending();
            return;
        }

        // --- End of Linear Path Check ---
        // If we reached the end of a non-looping path (like an escape route), detach so we don't repeat it
        if (!isCurrentPathLooping && rootFollower != null && rootFollower.follow)
        {
            double currentPercent = rootFollower.result.percent;
            if (currentPercent >= 0.999 || currentPercent <= 0.001 && rootFollower.direction == Spline.Direction.Backward)
            {
                rootFollower.follow = false;
                currentActivePath = null;

                // Randomly pick either a new Escape Spline or the Observation Spline to keep it flying beautifully.
                if (Random.value > 0.5f)
                {
                    Debug.Log($"[{gameObject.name}] Reached end of linear path. Chaining to new Escape Spline.");
                    ExecuteEscapeChoreography();
                }
                else
                {
                    Debug.Log($"[{gameObject.name}] Reached end of linear path. Returning to Observation Spline.");
                    ExecuteObservationSpline();
                }
                return;
            }
        }

        // Read phase from the BossCreature brain to drive movement decisions.
        bool desiresToEscape = bossBrain != null &&
            (bossBrain.currentPhase == BossCreature.BossPhase.Exhausted);

        if (desiresToEscape)
        {
            ExecuteEscapeChoreography();
        }
        else
        {
            ExecuteObservationSpline();
        }
    }

    private void UpdateBlending()
    {
        if (targetSplineForBlend == null)
        {
            isBlending = false;
            return;
        }

        // 1. Find our waypoint (the nearest point on the target track)
        SplineSample targetSample = new SplineSample();
        targetSplineForBlend.Project(transform.position, ref targetSample);
        Vector3 targetPos = (Vector3)targetSample.position;

        // 2. Are we close enough to dock?
        float distanceToTarget = Vector3.Distance(transform.position, targetPos);
        if (distanceToTarget <= splineDockingRadius)
        {
            // We have arrived. Snap precisely and engage track-following.
            transform.position = targetPos;
            transform.rotation = targetSample.rotation;
            FinalizeSplineAttachment();
            return;
        }

        // 3. Fish Steering Logic (Continuous forward movement)
        // Steer toward the waypoint
        Vector3 directionToTarget = (targetPos - transform.position).normalized;
        if (directionToTarget != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * steeringTurnSpeed);
        }

        // Always push the head forward at the target flight speed (no stalling!)
        float currentSpeed = targetFollowSpeedForBlend > 0f ? targetFollowSpeedForBlend : 10f;
        transform.position += transform.forward * currentSpeed * Time.deltaTime;
    }

    // --- Observation (looping patrol path) ---

    private void ExecuteObservationSpline()
    {
        // If we already have a path assigned and the follower is running, nothing to do.
        if (currentActivePath != null && rootFollower != null && rootFollower.follow)
        {
            return;
        }

        // Otherwise pick a path and start following.
        currentActivePath = GetObservationPath(PathTypeTag.PathType.Airborne);
        if (currentActivePath != null)
        {
            AssignSplineAndFollow(currentActivePath, observationSpeed, true);
            Debug.Log($"[{gameObject.name}] Resuming observation spline: {currentActivePath.name}");
        }
    }

    // --- Escape choreography ---

    private void ExecuteEscapeChoreography()
    {
        if (currentActivePath == null)
        {
            currentActivePath = FindNearestEscapeRoute(PathTypeTag.PathType.Airborne);
        }

        if (currentActivePath != null)
        {
            // Only assign if we aren't already following or blending to this exact path
            if (isBlending || (rootFollower != null && rootFollower.follow)) return;

            AssignSplineAndFollow(currentActivePath, escapeSpeed, false);
        }
        else
        {
            ExecuteFreestyleFallback();
        }
    }

    // --- Forced immediate evasion (called by BossCreature when burst damage threshold hit) ---

    public override void ForceImmediateEvasion()
    {
        GameObject escapePath = FindNearestEscapeRoute(PathTypeTag.PathType.Airborne);
        if (escapePath != null)
        {
            currentActivePath = escapePath;
            AssignSplineAndFollow(currentActivePath, escapeSpeed, false);
            Debug.Log($"[{gameObject.name}] Airborne movement jumping to escape route: {currentActivePath.name}");
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] Tried to evade, but no Airborne escape routes found by BossPathManager!");
            // Freestyle fallback
            if (rootFollower != null) rootFollower.follow = false;
        }
    }

    // --- Phase change hook (called by BossCreature.ChangePhase) ---

    public override void OnPhaseChanged(int newPhase)
    {
        base.OnPhaseChanged(newPhase);

        BossCreature.BossPhase phase = (BossCreature.BossPhase)newPhase;

        switch (phase)
        {
            case BossCreature.BossPhase.Orchestrator:
            case BossCreature.BossPhase.Recharging:
                // Return to observation loop — pick a fresh path.
                currentActivePath = null;
                ExecuteObservationSpline();
                break;

            case BossCreature.BossPhase.Exhausted:
                // ForceImmediateEvasion will be called separately by BossCreature.EnterExhaustedPhase.
                break;
        }
    }

    // --- Tether rubber-band ---

    private void ApplyTetherRubberBand()
    {
        // Keep the dragon within the tether radius of the anchor.
        if (bossBrain == null) return;

        // Note: Assumes Tethering is managed elsewhere or through BaseBossMovement
    }

    private void ExecuteFreestyleFallback()
    {
        // Freestyle fallback: maintain forward momentum but pitch upward smoothly for a swooping flight path
        Vector3 targetForward = (transform.forward + Vector3.up * 0.5f).normalized;
        if (targetForward != Vector3.zero)
        {
            Quaternion upwardRotation = Quaternion.LookRotation(targetForward);
            transform.rotation = Quaternion.Slerp(transform.rotation, upwardRotation, Time.deltaTime * 2f);
        }
        transform.position += transform.forward * escapeSpeed * Time.deltaTime;
    }
}
