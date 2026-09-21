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

    [Header("Freestyle Undulation")]
    [Tooltip("How fast the dragon snakes side-to-side in freestyle/blending air.")]
    public float undulationFrequency = 3f;
    [Tooltip("How wide the side-to-side snaking is.")]
    public float undulationAmplitude = 2f;

    private GameObject currentActivePath;
    private SplineFollower rootFollower;

    // --- Blending State ---
    private bool isBlending = false;
    private float blendTimer = 0f;
    private float blendDuration = 1f;
    private Vector3 blendStartPos;
    private Quaternion blendStartRot;
    private SplineComputer targetSplineForBlend;
    private float targetFollowSpeedForBlend;

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

                SegmentedDragonManager dragonBody = GetComponent<SegmentedDragonManager>();
                if (dragonBody != null)
                {
                    dragonBody.SwitchToNewSpline(splineComputer);
                }
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
    private bool isCurrentPathLooping = true;

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

        // Instead of instantly snapping, we initiate a blend
        isBlending = true;
        blendTimer = 0f;
        blendStartPos = transform.position;
        blendStartRot = transform.rotation;

        targetSplineForBlend = splineComputer;
        targetFollowSpeedForBlend = speed;

        rootFollower.follow = false; // Disable direct follow while blending

        // Calculate dynamic duration based on distance and current speed
        SplineSample sample = new SplineSample();
        splineComputer.Project(transform.position, ref sample);

        float distance = Vector3.Distance(transform.position, (Vector3)sample.position);

        // Dynamic speed calculation. If it's standing still, use a base speed.
        float currentSpeed = speed;
        if (currentSpeed <= 0f) currentSpeed = 10f;

        blendDuration = distance / currentSpeed;
        if (blendDuration < 0.5f) blendDuration = 0.5f; // Hard floor so it doesn't instantly snap on tiny distances
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

        // ONLY switch the body segments after the head has physically arrived at the spline
        SegmentedDragonManager dragonBody = GetComponent<SegmentedDragonManager>();
        if (dragonBody != null)
        {
            dragonBody.SwitchToNewSpline(targetSplineForBlend);
        }

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

        // If we reached the end of a non-looping path (like an escape route), detach so we don't repeat it
        if (!isCurrentPathLooping && rootFollower != null && rootFollower.follow)
        {
            double currentPercent = rootFollower.result.percent;
            if (currentPercent >= 0.999 || currentPercent <= 0.001 && rootFollower.direction == Spline.Direction.Backward)
            {
                rootFollower.follow = false;
                currentActivePath = null;
                Debug.Log($"[{gameObject.name}] Reached end of linear path. Detaching for next action.");
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

        blendTimer += Time.deltaTime;
        float t = Mathf.Clamp01(blendTimer / blendDuration);

        // Smoothstep curve for natural ease-in ease-out, no linear jarring
        float smoothT = t * t * (3f - 2f * t);

        // Project dynamically so we hit a moving target if the spline is moving, or hit it accurately
        SplineSample targetSample = new SplineSample();
        targetSplineForBlend.Project(transform.position, ref targetSample);

        Vector3 basePosition = Vector3.Lerp(blendStartPos, (Vector3)targetSample.position, smoothT);

        // Add graceful undulation (snaking) so the body trails beautifully
        float sway = Mathf.Sin(Time.time * undulationFrequency) * undulationAmplitude;

        // Fade out the sway as it approaches the spline to dock perfectly
        float swayFade = 1f - smoothT;

        transform.position = basePosition + (transform.right * sway * swayFade);
        transform.rotation = Quaternion.Slerp(blendStartRot, targetSample.rotation, smoothT);

        if (t >= 1f)
        {
            FinalizeSplineAttachment();
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
            // Freestyle fallback: maintain forward momentum but pitch upward smoothly for a swooping flight path
            Vector3 targetForward = (transform.forward + Vector3.up * 0.5f).normalized;
            if (targetForward != Vector3.zero)
            {
                // Apply undulation to the rotation to create a snaking forward flight path
                float sway = Mathf.Sin(Time.time * undulationFrequency) * undulationAmplitude;
                Vector3 snakingForward = targetForward + (transform.right * sway * 0.1f);

                Quaternion upwardRotation = Quaternion.LookRotation(snakingForward.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, upwardRotation, Time.deltaTime * 2f);
            }
            transform.position += transform.forward * escapeSpeed * Time.deltaTime;
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
        }
    }
}
