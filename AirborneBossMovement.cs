using UnityEngine;
using Dreamteck.Splines;

/// <summary>
/// Inherits from BaseBossMovement.
/// Used for purely flying bosses (like the Dragon).
/// Supports Spline mode (observation coil / escape spline) and Freestyle mode
/// (pursue, swoop, bank, withdraw, stillhold), with a smooth BlendingToSpline transition.
///
/// BEHAVIORAL LOOP:
///   Observation Coil  — home base, coils around Power Crystal, regenerates
///   Freestyle         — departs coil to pursue/attack or withdraw
///   BlendingToSpline  — glides back onto observation OR escape spline
///   Escape Spline     — triggered by ForceImmediateEvasion under threat
///
/// UNDULATION DESIGN:
///   currentMode + CurrentIntent are read by DragonMovementManager each frame.
///   Undulation amplitude collapses on Pursue/Swoop (visual attack cue) and
///   rebuilds to full sway during Stillhold/Bank.
/// </summary>
public class AirborneBossMovement : BaseBossMovement
{
    public enum MovementMode
    {
        Spline,
        Freestyle,
        BlendingToSpline
    }

    public enum FreestyleIntent
    {
        Pursue,
        Swoop,
        Bank,
        Stillhold,
        Withdraw
    }

    [Header("Mode State")]
    public MovementMode currentMode = MovementMode.Spline;

    // ─── Speed (VR-scaled, hard cap = 2 m/s) ─────────────────────────────────
    [Header("Movement Speed — VR tuned (max 2 m/s)")]
    [Tooltip("Base speed for Pursuit and most freestyle movement.")]
    public float baseFlightSpeed = 1.5f;

    [Tooltip("Speed used when coiling on the Observation Spline. Slow and stately.")]
    public float observationCoilSpeed = 0.5f;

    [Tooltip("Hard cap for Swoop — the only moment the dragon hits maximum VR speed.")]
    public float swoopMaxSpeed = 2.0f;

    // ─── Freestyle Tuning ─────────────────────────────────────────────────────
    [Header("Freestyle Tuning")]
    [Tooltip("Rate of bodyUndulationRate — feeds into Stillhold hover orbit speed.")]
    public float bodyUndulationRate = 1.2f;
    [Tooltip("How fast the root rotates toward its target. Lower = more deliberate turns.")]
    public float coilTightness = 3f;
    public float minTurnRadius = 3f;

    // ─── Tether ───────────────────────────────────────────────────────────────
    [Header("Tether Struggle Setup")]
    [Tooltip("Local offset direction the dragon pulls toward when tethered.")]
    public Vector3 tetherStruggleDirection = new Vector3(0f, 5f, 5f);
    [Tooltip("How far the dragon tries to push past the anchor to create rope tension.")]
    public float tetherStruggleDistance = 15f;

    // ─── Private ──────────────────────────────────────────────────────────────
    private SplineFollower          splineFollower;
    private CreatureStatusEffects   statusEffects;
    private GameObject              currentActivePath;

    // Blending state
    private float      blendTimer           = 0f;
    private float      dynamicBlendDuration = 2f;
    private Vector3    blendStartPosition;
    private Quaternion blendStartRotation;

    // Freestyle state
    private FreestyleIntent _currentIntent = FreestyleIntent.Stillhold;
    /// <summary>
    /// Read-only access to the current freestyle intent.
    /// DragonMovementManager queries this each frame to scale undulation amplitude.
    /// Collapse on Pursue/Swoop is the visual attack cue; full sway on Stillhold/Bank.
    /// </summary>
    public FreestyleIntent CurrentIntent => _currentIntent;

    private Vector3 freestyleTargetPosition;
    private float   freestyleSpeed;
    private float   currentSpeedMultiplier = 1f;

    // Stillhold hover orbit — captures position when entering Stillhold
    private Vector3 stillholdAnchor;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        splineFollower = GetComponent<SplineFollower>();
        statusEffects  = GetComponent<CreatureStatusEffects>();
    }

    protected override void TickMovement()
    {
        if (statusEffects != null)
            currentSpeedMultiplier = statusEffects.CurrentSpeedMultiplier;

        switch (currentMode)
        {
            case MovementMode.Spline:
                UpdateSplineMode();
                break;
            case MovementMode.Freestyle:
                UpdateFreestyleMode();
                break;
            case MovementMode.BlendingToSpline:
                UpdateBlendingMode();
                break;
        }

        if (isTethered)
        {
            UpdateTetherStruggle();
            ApplyTetherRubberBand();
        }
    }

    // ─── Tether ───────────────────────────────────────────────────────────────

    private void UpdateTetherStruggle()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");

        if (playerObj != null && bossBrain != null)
        {
            Vector3 playerPos = playerObj.transform.position;
            Vector3 toPlayer  = (playerPos - transform.position).normalized;

            if (bossBrain.currentPhase == BossCreature.BossPhase.Exhausted || bossBrain.GetCurrentHealthPct() < 0.25f)
            {
                freestyleTargetPosition = transform.position - toPlayer * 6f + Vector3.up * 4f;
            }
            else if (bossBrain.currentPhase == BossCreature.BossPhase.Engaged)
            {
                freestyleTargetPosition = playerPos + toPlayer * -2f + Vector3.up * 1.5f;
            }
            else
            {
                Vector3 side = Vector3.Cross(toPlayer, Vector3.up);
                freestyleTargetPosition = transform.position + side * 7f + Vector3.up * 6f - toPlayer * 2f;
            }
        }
        else
        {
            SegmentedDragonManager dragonManager = GetComponent<SegmentedDragonManager>();
            if (dragonManager != null && dragonManager.TetherAnchorTransform != null)
            {
                Vector3 worldStruggleDirection = transform.TransformDirection(tetherStruggleDirection.normalized);
                freestyleTargetPosition = dragonManager.TetherAnchorTransform.position + (worldStruggleDirection * tetherStruggleDistance);
            }
        }
    }

    // ─── Public Movement Requests ─────────────────────────────────────────────

    /// <summary>
    /// Switches to Freestyle mode with the given intent and target.
    /// Called by DragonActionListeners in response to Quest Machine actions.
    /// </summary>
    public void RequestFreestyleIntent(FreestyleIntent intent, Vector3 targetPos)
    {
        currentMode            = MovementMode.Freestyle;
        _currentIntent         = intent;
        freestyleTargetPosition = targetPos;

        // Capture anchor when entering Stillhold so the hover orbit has a stable centre
        if (intent == FreestyleIntent.Stillhold)
            stillholdAnchor = transform.position;

        if (splineFollower != null) splineFollower.follow = false;
    }

    /// <summary>
    /// Begins a smooth blend from the current position onto the given observation spline.
    /// Used for both returning to the Observation Coil and landing on an Escape Spline.
    /// As the dragon glides in, DragonMovementManager collapses undulation to near-zero
    /// for a precise, purposeful dock.
    /// </summary>
    public void RequestReturnToCoil(GameObject observationPath)
    {
        if (observationPath == null) return;

        currentActivePath  = observationPath;
        currentMode        = MovementMode.BlendingToSpline;
        blendTimer         = 0f;
        blendStartPosition = transform.position;
        blendStartRotation = transform.rotation;

        if (splineFollower != null)
        {
            SplineComputer targetSpline = observationPath.GetComponentInChildren<SplineComputer>();
            splineFollower.spline = targetSpline;
            splineFollower.follow = false; // Stay manual until blend finishes

            if (targetSpline != null)
            {
                SplineSample targetSample = new SplineSample();
                targetSpline.Project(transform.position, ref targetSample);
                float distanceToSpline = Vector3.Distance(transform.position, targetSample.position);

                // Dynamic duration based on distance — no jarring teleport snaps in VR
                dynamicBlendDuration = distanceToSpline / (baseFlightSpeed > 0 ? baseFlightSpeed : 1f);
                if (dynamicBlendDuration < 0.5f) dynamicBlendDuration = 0.5f;
            }
        }
    }

    /// <summary>
    /// Routes to RequestReturnToCoil — the same smooth blend applies for escape splines.
    /// </summary>
    public void RequestGlideToSpline(GameObject escapePath)
    {
        RequestReturnToCoil(escapePath);
    }

    // ─── Movement Mode Updates ────────────────────────────────────────────────

    private void UpdateSplineMode()
    {
        if (splineFollower != null)
        {
            splineFollower.follow      = true;
            // Observation coil uses its own slow speed; status effects can still slow/freeze
            splineFollower.followSpeed = observationCoilSpeed * currentSpeedMultiplier;
        }
    }

    private void UpdateFreestyleMode()
    {
        // ── VR Speed Table (hard cap: swoopMaxSpeed = 2 m/s) ─────────────────
        switch (_currentIntent)
        {
            case FreestyleIntent.Pursue:
                freestyleSpeed = baseFlightSpeed;                   // 1.5 m/s
                break;
            case FreestyleIntent.Swoop:
                freestyleSpeed = swoopMaxSpeed;                     // 2.0 m/s — hard cap, brief
                break;
            case FreestyleIntent.Withdraw:
                freestyleSpeed = baseFlightSpeed * 0.6f;            // ~0.9 m/s — banking away
                break;
            case FreestyleIntent.Bank:
                freestyleSpeed = baseFlightSpeed * 0.55f;           // ~0.83 m/s — wide arcing turn
                break;
            case FreestyleIntent.Stillhold:
                freestyleSpeed = 0f;                                // handled below (hover orbit)
                break;
        }

        float effectiveSpeed = freestyleSpeed * currentSpeedMultiplier;

        if (_currentIntent == FreestyleIntent.Stillhold)
        {
            // ── Stillhold: lazy elliptical hover orbit ──────────────────────
            // The root traces a slow ellipse so Dragon_Head records a real trail.
            // DragonMovementManager bakes maximum undulation (scale = 1.0) onto
            // this trail — producing the full coiled serpentine look.
            float t        = Time.time * bodyUndulationRate * 0.22f;
            float r        = 1.8f; // orbit radius in metres (VR intimate scale)
            Vector3 hoverTarget = stillholdAnchor + new Vector3(
                Mathf.Cos(t) * r,
                Mathf.Sin(t * 0.6f) * 0.5f,  // subtle vertical bob
                Mathf.Sin(t) * r
            );

            transform.position = Vector3.MoveTowards(
                transform.position, hoverTarget,
                0.3f * Time.deltaTime * currentSpeedMultiplier);

            Vector3 lookDir = hoverTarget - transform.position;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(lookDir),
                    Time.deltaTime * coilTightness * 0.4f);
            }
        }
        else
        {
            // ── All other intents: turn toward target then fly forward ───────
            Vector3 direction = (freestyleTargetPosition - transform.position).normalized;
            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, targetRotation,
                    Time.deltaTime * coilTightness);
            }
            transform.position += transform.forward * (effectiveSpeed * Time.deltaTime);
        }
    }

    private void UpdateBlendingMode()
    {
        if (splineFollower == null || splineFollower.spline == null)
        {
            currentMode = MovementMode.Freestyle;
            return;
        }

        blendTimer += Time.deltaTime * currentSpeedMultiplier;
        float t = Mathf.Clamp01(blendTimer / dynamicBlendDuration);
        t = t * t * (3f - 2f * t); // smoothstep

        SplineSample targetSample = new SplineSample();
        splineFollower.spline.Project(transform.position, ref targetSample);

        transform.position = Vector3.Lerp(blendStartPosition, (Vector3)targetSample.position, t);
        transform.rotation = Quaternion.Slerp(blendStartRotation, targetSample.rotation, t);

        if (t >= 1f)
        {
            currentMode = MovementMode.Spline;
            splineFollower.SetPercent(targetSample.percent);
            splineFollower.follow = true;
        }
    }

    // ─── Overrides ────────────────────────────────────────────────────────────

    /// <summary>
    /// Forces immediate evasion to the nearest airborne escape spline.
    /// Triggered by DragonActionListeners on "Evade" or "Ride Escape Spline".
    /// The escape spline is a closed loop by convention — the dragon rides it until
    /// the threat context resets and it transitions back to the observation coil.
    /// </summary>
    public override void ForceImmediateEvasion()
    {
        currentActivePath = FindNearestEscapeRoute(PathTypeTag.PathType.Airborne);
        if (currentActivePath != null)
        {
            Debug.Log($"[{gameObject.name}] Evading to escape spline: {currentActivePath.name}");
            RequestGlideToSpline(currentActivePath);
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] No Airborne escape spline found — withdrawing freestyle.");

            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                Vector3 awayFromPlayer = (transform.position - playerObj.transform.position).normalized;
                awayFromPlayer.y = 0f;
                RequestFreestyleIntent(FreestyleIntent.Withdraw,
                    transform.position + awayFromPlayer * 20f + Vector3.up * 10f);
            }
            else
            {
                RequestFreestyleIntent(FreestyleIntent.Withdraw,
                    transform.position + transform.forward * 20f + Vector3.up * 10f);
            }
        }
    }

    public override void HandleTetherAttached()
    {
        base.HandleTetherAttached();
        Vector3 worldStruggleDirection = transform.TransformDirection(tetherStruggleDirection.normalized);
        Vector3 struggleTarget = transform.position + (worldStruggleDirection * tetherStruggleDistance);
        RequestFreestyleIntent(FreestyleIntent.Pursue, struggleTarget);
    }

    private void ApplyTetherRubberBand()
    {
        SegmentedDragonManager dragonManager = GetComponent<SegmentedDragonManager>();
        if (dragonManager != null && dragonManager.IsTethered && dragonManager.TetherAnchorTransform != null)
        {
            Vector3 anchorPos        = dragonManager.TetherAnchorTransform.position;
            float   maxRadius        = dragonManager.TetherMaxLength;
            float   distanceToAnchor = Vector3.Distance(transform.position, anchorPos);

            if (distanceToAnchor > maxRadius && maxRadius > 0f)
            {
                Vector3 directionFromAnchor = (transform.position - anchorPos).normalized;
                transform.position = anchorPos + directionFromAnchor * maxRadius;
            }
        }
    }
}

// --- End of unified package ---