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

    // --- Blending State ---
    private bool isBlending = false;
    private Vector3 blendStartPosition;
    private Quaternion blendStartRotation;
    private SplineSample blendTargetSample;
    private float blendTimer = 0f;
    private float blendDuration = 0f;
    private float blendSpeed = 0f;
    private SplineComputer pendingSpline;
    private bool isPendingEscape = false;

    // --- Fallback State ---
    private bool isFreestylingFallback = false;
    private float nextEvadeCheckTime = 0f;

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
            AssignSplineAndFollow(currentActivePath, observationSpeed);
            Debug.Log($"<color=green>[{gameObject.name}] AirborneBossMovement: Dragon placed on observation path '{currentActivePath.name}'.</color>");
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] AirborneBossMovement: No Airborne observation paths found. Dragon will stay at spawn position.");
        }
    }

    /// <summary>
    /// Smoothly transitions the dragon from its current position to the given path instead of teleporting.
    /// </summary>
    private void SmoothlyTransitionToPath(GameObject pathObj, float speed, bool isEscape)
    {
        if (rootFollower == null || pathObj == null) return;

        SplineComputer splineComputer = pathObj.GetComponentInChildren<SplineComputer>();
        if (splineComputer == null)
        {
            Debug.LogWarning($"[{gameObject.name}] Path '{pathObj.name}' has no SplineComputer. Cannot transition.");
            return;
        }

        isBlending = true;
        blendStartPosition = transform.position;
        blendStartRotation = transform.rotation;
        blendTimer = 0f;
        blendSpeed = speed;
        pendingSpline = splineComputer;
        isPendingEscape = isEscape;

        blendTargetSample = new SplineSample();
        if (isEscape)
        {
            // Escape splines are open: fly to the start (0.0)
            blendTargetSample = splineComputer.Evaluate(0.0);
        }
        else
        {
            // Observation splines are closed: fly to the nearest point on the track
            splineComputer.Project(blendStartPosition, ref blendTargetSample);
        }

        float distance = Vector3.Distance(blendStartPosition, blendTargetSample.position);
        blendDuration = distance / (speed > 0f ? speed : 1f);

        // Disable regular spline following while we freestyle fly towards it
        rootFollower.follow = false;
    }

    /// <summary>
    /// Assigns a SplineComputer from the given path GameObject to the root SplineFollower and starts following immediately.
    /// </summary>
    private void AssignSplineAndFollow(GameObject pathObj, float speed)
    {
        if (rootFollower == null || pathObj == null) return;

        // The path prefab can hold the SplineComputer directly on the root or on a child.
        SplineComputer splineComputer = pathObj.GetComponentInChildren<SplineComputer>();
        if (splineComputer == null)
        {
            Debug.LogWarning($"[{gameObject.name}] Path '{pathObj.name}' has no SplineComputer. Cannot follow.");
            return;
        }

        rootFollower.spline = splineComputer;
        rootFollower.followSpeed = speed;
        rootFollower.wrapMode = SplineFollower.Wrap.Loop;
        rootFollower.follow = true;

        // Also update the SegmentedDragonManager's internal spline reference so the breadcrumb
        // history stays in sync with whichever track the dragon is currently following.
        SegmentedDragonManager dragonBody = GetComponent<SegmentedDragonManager>();
        if (dragonBody != null)
        {
            dragonBody.SwitchToNewSpline(splineComputer);
        }
    }

    // --- Core Movement Tick ---

    protected override void TickMovement()
    {
        if (isBlending)
        {
            ExecuteBlendTick();
            return;
        }

        if (isTethered)
        {
            ApplyTetherRubberBand();
            return;
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

    private void ExecuteBlendTick()
    {
        blendTimer += Time.deltaTime;
        float t = Mathf.Clamp01(blendTimer / blendDuration);

        // Smoothstep
        t = t * t * (3f - 2f * t);

        transform.position = Vector3.Lerp(blendStartPosition, (Vector3)blendTargetSample.position, t);
        transform.rotation = Quaternion.Slerp(blendStartRotation, blendTargetSample.rotation, t);

        if (t >= 1f)
        {
            // Reached destination, snap to spline and follow
            isBlending = false;
            rootFollower.spline = pendingSpline;
            rootFollower.followSpeed = blendSpeed;
            rootFollower.wrapMode = SplineFollower.Wrap.Loop;
            rootFollower.SetPercent(blendTargetSample.percent);
            rootFollower.follow = true;

            SegmentedDragonManager dragonBody = GetComponent<SegmentedDragonManager>();
            if (dragonBody != null)
            {
                dragonBody.SwitchToNewSpline(pendingSpline);
            }
        }
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
            SmoothlyTransitionToPath(currentActivePath, observationSpeed, false);
            Debug.Log($"[{gameObject.name}] Smoothly returning to observation spline: {currentActivePath.name}");
        }
    }

    // --- Escape choreography ---

    private void ExecuteEscapeChoreography()
    {
        // If we are already freestyling as a fallback, don't spam path checks every frame.
        if (currentActivePath == null)
        {
            if (Time.time > nextEvadeCheckTime)
            {
                // Try finding an evasion path every 2 seconds if we failed previously
                ForceImmediateEvasion();
                nextEvadeCheckTime = Time.time + 2f;
            }

            // If it's STILL null after trying to find an evasion path, execute fallback movement
            if (currentActivePath == null)
            {
                isFreestylingFallback = true;
                transform.Translate(Vector3.up * escapeSpeed * Time.deltaTime, Space.World);
                return;
            }
        }

        if (currentActivePath != null)
        {
            isFreestylingFallback = false;
            if (rootFollower != null && rootFollower.follow && rootFollower.spline != currentActivePath.GetComponentInChildren<SplineComputer>())
            {
                PathTypeTag tag = currentActivePath.GetComponent<PathTypeTag>();
                bool isEscape = tag != null && tag.isEscapeRoute;
                SmoothlyTransitionToPath(currentActivePath, escapeSpeed, isEscape);
            }
            else if (!isBlending && (rootFollower == null || !rootFollower.follow))
            {
                PathTypeTag tag = currentActivePath.GetComponent<PathTypeTag>();
                bool isEscape = tag != null && tag.isEscapeRoute;
                SmoothlyTransitionToPath(currentActivePath, escapeSpeed, isEscape);
            }
        }
    }

    // --- Forced immediate evasion (called by BossCreature when burst damage threshold hit) ---

    public override void ForceImmediateEvasion()
    {
        // 1. Check if already on an escape route
        bool isOnEscapeSpline = false;
        if (currentActivePath != null)
        {
            PathTypeTag pathTag = currentActivePath.GetComponent<PathTypeTag>();
            if (pathTag != null && pathTag.isEscapeRoute)
            {
                isOnEscapeSpline = true;
            }
        }

        GameObject targetPath = null;
        bool targetIsEscape = false;

        if (isOnEscapeSpline)
        {
            // Escape to observation/heal if already escaping
            targetPath = GetObservationPath(PathTypeTag.PathType.Airborne);
            targetIsEscape = false;
        }
        else
        {
            // Find closest escape
            GameObject escapePath = FindNearestEscapeRoute(PathTypeTag.PathType.Airborne);

            // Logic check: avoid flying towards player
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            bool isEscapeLogical = true;

            if (escapePath != null && playerObj != null)
            {
                float distToEscape = Vector3.Distance(transform.position, escapePath.transform.position);
                float playerDistToEscape = Vector3.Distance(playerObj.transform.position, escapePath.transform.position);

                if (playerDistToEscape < distToEscape)
                {
                    isEscapeLogical = false;
                    Debug.Log($"[{gameObject.name}] Nearest escape spline is too close to player. Rejecting it.");
                }
            }

            if (escapePath != null && isEscapeLogical)
            {
                targetPath = escapePath;
                targetIsEscape = true;
            }
        }

        if (targetPath != null)
        {
            currentActivePath = targetPath;
            SmoothlyTransitionToPath(currentActivePath, escapeSpeed, targetIsEscape);
            Debug.Log($"[{gameObject.name}] Airborne movement smoothly evading to route: {currentActivePath.name}");
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] Tried to evade, but no logical routes found by BossPathManager!");

            // Do not override currentActivePath here so we don't trap it in a null loop,
            // but we do want to disable follower and let freestyle fallback handle it.
            if (rootFollower != null) rootFollower.follow = false;
            isBlending = false;
            currentActivePath = null; // Clear it so it stops trying to blend to something invalid
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
                isFreestylingFallback = false;
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

        SegmentedDragonManager dragonBody = GetComponent<SegmentedDragonManager>();
        if (dragonBody == null || !dragonBody.IsTethered) return;

        Transform anchor = dragonBody.TetherAnchorTransform;
        float maxLength = dragonBody.TetherMaxLength;
        if (anchor == null || maxLength <= 0f) return;

        Vector3 toAnchor = anchor.position - transform.position;
        if (toAnchor.magnitude > maxLength)
        {
            // Rubber-band: push root back toward anchor boundary.
            transform.position = anchor.position - toAnchor.normalized * maxLength;
            if (rootFollower != null) rootFollower.follow = false;
            isBlending = false;
        }
    }
}