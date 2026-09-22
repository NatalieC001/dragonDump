using System.Collections;
using UnityEngine;
using Dreamteck.Splines;
using System.Collections.Generic;

/// <summary>
/// Solely responsible for the breadcrumb trail of the Dragon's visual head (Dragon_Head child),
/// baking contextual serpentine undulation into recorded rotations, and physically placing
/// each body segment along that trail.
///
/// SEPARATION OF CONCERNS:
///   SegmentedDragonManager  — segment lifecycle (spawn, regrowth, gap-close on death)
///   DragonBreadcrumbManager   — movement (breadcrumbs, undulation, placement)
///   DragonSpacingManager    — spacing (cumulative segment offsets)
///   AirborneBossMovement    — intent (spline/freestyle modes, speed)
/// </summary>
public class DragonBreadcrumbManager : MonoBehaviour
{
    private struct PositionData
    {
        public Vector3    position;
        public Quaternion rotation;
        public float      distanceTraveled;
    }

    private List<PositionData> positionHistory  = new List<PositionData>();
    private float              headTotalDistance = 0f;
    private SplineComputer     bossSpline;

    // ─── Head Reference ───────────────────────────────────────────────────────
    [Header("Head Reference")]
    [Tooltip("Drag the Dragon_Head child transform here directly. " +
             "If left empty, SetHeadTransform() must be called at runtime by SegmentedDragonManager.")]
    public Transform headTransformOverride;

    private Transform              headTransform;
    private AirborneBossMovement   movementBrain;

    // ─── Serpentine Undulation ────────────────────────────────────────────────
    [Header("Serpentine Undulation")]
    [Tooltip("Full sine-wave cycles per second. 0.5 = one lazy S-curve every 2 seconds (VR-safe).")]
    public float undulationFrequency = 0.5f;

    [Tooltip("Max lateral yaw sway in degrees at full amplitude (Stillhold / Bank).")]
    public float maxUndulationYaw  = 20f;

    [Tooltip("Max roll tilt in degrees — gives the body 3D banking grace on turns.")]
    public float maxUndulationRoll = 7f;

    // Smoothly lerped each frame; read by EvaluateUndulationScale()
    private float currentUndulationScale = 1f;

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Total distance the head has traveled. Used by SegmentedDragonManager for gap-close animation.</summary>
    public float HeadTotalDistance => headTotalDistance;

    /// <summary>
    /// Called by SegmentedDragonManager immediately after the head segment is spawned.
    /// Provides the actual Dragon_Head child transform as the breadcrumb source.
    /// </summary>
    public void SetHeadTransform(Transform head)
    {
        headTransform = head;
    }

    /// <summary>
    /// Called by SegmentedDragonManager to provide intent queries for contextual undulation.
    /// </summary>
    public void SetMovementBrain(AirborneBossMovement brain)
    {
        movementBrain = brain;
    }

    // ─── Initialization ───────────────────────────────────────────────────────

    public void InitializeMovement(SplineComputer track, float totalExpectedLength)
    {
        bossSpline        = track;
        positionHistory.Clear();
        headTotalDistance = 0f;

        // Honour the Inspector override if the runtime call hasn't set it yet
        if (headTransformOverride != null && headTransform == null)
            headTransform = headTransformOverride;

        // Pre-fill the breadcrumb history backwards along the spline so segments
        // start at valid positions rather than collapsing to the head on frame 1.
        int samples = Mathf.CeilToInt((totalExpectedLength * 2f) / 0.1f) + 1;

        if (bossSpline != null)
        {
            SplineFollower rootFollower = GetComponent<SplineFollower>();
            double startPercent  = rootFollower != null ? rootFollower.GetPercent() : 0.0;
            float  splineLength  = bossSpline.CalculateLength();

            for (int i = 0; i < samples; i++)
            {
                float  distBack = i * 0.1f;
                double percent  = startPercent - (distBack / splineLength);

                if (bossSpline.isClosed)
                {
                    while (percent < 0.0) percent += 1.0;
                    while (percent > 1.0) percent -= 1.0;
                }
                else
                {
                    percent = System.Math.Clamp(percent, 0.0, 1.0);
                }

                SplineSample sample = bossSpline.Evaluate(percent);

                positionHistory.Add(new PositionData
                {
                    position         = sample.position,
                    rotation         = sample.rotation,
                    distanceTraveled = -distBack
                });
            }
        }
        else
        {
            // No spline: seed with current head position
            Vector3    startPos = headTransform != null ? headTransform.position : transform.position;
            Quaternion startRot = headTransform != null ? headTransform.rotation : transform.rotation;

            positionHistory.Add(new PositionData
            {
                position         = startPos,
                rotation         = startRot,
                distanceTraveled = 0f
            });
        }
    }

    public void SwitchToNewSpline(SplineComputer newTrack)
    {
        bossSpline = newTrack;
    }

    // ─── Per-Frame Update (called from SegmentedDragonManager.LateUpdate) ────

    /// <summary>
    /// Records the Dragon_Head child transform into the breadcrumb trail, with
    /// serpentine undulation baked into the stored rotation.
    ///
    /// UNDULATION DESIGN: The spline is the centreline of the sine wave.
    /// Amplitude (how far the dragon deviates from the centreline) is encoded
    /// in rotation only — not in position — so the body oscillates around the
    /// path without drifting off it. The wave travels naturally down the body
    /// because every segment replays the rotation the head had at that crumb.
    /// </summary>
    public void UpdateBreadcrumbs(float maxNeededHistoryDistance)
    {
        // Honour Inspector override late-assign
        if (headTransformOverride != null && headTransform == null)
            headTransform = headTransformOverride;

        // ── Source: Dragon_Head child, NOT the invisible root ──────────────
        Vector3    currentHeadPos = headTransform != null ? headTransform.position : transform.position;
        Quaternion currentHeadRot = headTransform != null ? headTransform.rotation : transform.rotation;

        if (positionHistory.Count == 0) return;

        float distMovedSinceLastFrame = Vector3.Distance(currentHeadPos, positionHistory[0].position);

        // Always run the undulation lerp every frame so the amplitude transition stays smooth,
        // even when the dragon is barely moving (e.g. slow coil orbit, Stillhold hover).
        EvaluateUndulationScale();

        // Record a breadcrumb virtually every frame.
        // Old threshold was 0.02m — at observation coil speed (0.5 m/s, 90 Hz VR) the dragon
        // moves only 0.0055m per frame, so crumbs were recording every ~4 frames → stiff body.
        // 0.001m lets the trail update every single frame at any speed.
        if (distMovedSinceLastFrame > 0.001f)
        {
            headTotalDistance += distMovedSinceLastFrame;

            // ── Contextual undulation ─────────────────────────────────────
            // Evaluate the current amplitude scale based on dragon intent:
            //   Stillhold / Bank  → full sway (1.0)     visual: dragon is watching
            //   Withdraw          → partial sway (0.6)  visual: banking away
            //   Spline coil       → gentle sway (0.35)  visual: settled
            //   Blending to spline → collapses to 0     visual: precise landing
            //   Pursue / Swoop    → near zero (0.05)    visual: ATTACK CUE — body becomes an arrow
            float wavePhase = Time.time * undulationFrequency * Mathf.PI * 2f;
            float yawDeg    = Mathf.Sin(wavePhase) * maxUndulationYaw  * currentUndulationScale;
            float rollDeg   = Mathf.Cos(wavePhase) * maxUndulationRoll * currentUndulationScale;

            Quaternion undulationOffset = Quaternion.Euler(0f, yawDeg, rollDeg);
            Quaternion recordedRot      = currentHeadRot * undulationOffset;

            positionHistory.Insert(0, new PositionData
            {
                position         = currentHeadPos,
                rotation         = recordedRot,
                distanceTraveled = headTotalDistance
            });

            // Trim history older than needed
            if (headTotalDistance - positionHistory[positionHistory.Count - 1].distanceTraveled > maxNeededHistoryDistance)
                positionHistory.RemoveAt(positionHistory.Count - 1);
        }
    }


    /// <summary>
    /// Positions a single DragonSegment at its target distance behind the head
    /// by interpolating between two breadcrumbs.
    /// Uses Rigidbody.MovePosition when available to prevent arrow tunnelling.
    /// </summary>
    public void PlaceSegment(DragonSegment segment, float requiredDistanceBehindHead)
    {
        if (segment == null || positionHistory.Count < 2) return;

        float targetDistInHistory = headTotalDistance - requiredDistanceBehindHead;

        for (int j = 0; j < positionHistory.Count - 1; j++)
        {
            PositionData newer = positionHistory[j];
            PositionData older = positionHistory[j + 1];

            if (targetDistInHistory <= newer.distanceTraveled &&
                targetDistInHistory >= older.distanceTraveled)
            {
                float range = newer.distanceTraveled - older.distanceTraveled;
                float t     = range > 0f
                    ? (newer.distanceTraveled - targetDistInHistory) / range
                    : 0f;

                Vector3    newPos = Vector3.Lerp(   newer.position, older.position, t);
                Quaternion newRot = Quaternion.Slerp(newer.rotation, older.rotation, t);

                Rigidbody rb = segment.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.MovePosition(newPos);
                    rb.MoveRotation(newRot);
                }
                else
                {
                    segment.transform.position = newPos;
                    segment.transform.rotation = newRot;
                }
                break;
            }
        }
    }

    // ─── Internal ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Smoothly lerps currentUndulationScale toward the target dictated by the
    /// dragon's current movement mode and freestyle intent.
    /// Collapse is faster than build so the attack cue is sharp and readable.
    /// </summary>
    private void EvaluateUndulationScale()
    {
        if (movementBrain == null)
        {
            currentUndulationScale = 1f;
            return;
        }

        float target;

        switch (movementBrain.currentMode)
        {
            case AirborneBossMovement.MovementMode.Spline:
                // Settled on observation coil — gentle, composed sway
                target = 0.35f;
                break;

            case AirborneBossMovement.MovementMode.BlendingToSpline:
                // Gliding to dock on a spline (return to coil OR escape spline) — straighten out
                target = 0f;
                break;

            case AirborneBossMovement.MovementMode.Freestyle:
                switch (movementBrain.CurrentIntent)
                {
                    case AirborneBossMovement.FreestyleIntent.Pursue:
                        // ← ATTACK CUE: body collapses to an arrow, telegraphing the strike
                        target = 0.05f;
                        break;
                    case AirborneBossMovement.FreestyleIntent.Swoop:
                        // Maximum-speed dive — nearly straight, tiny fishtail
                        target = 0.08f;
                        break;
                    case AirborneBossMovement.FreestyleIntent.Withdraw:
                        // Banking away after an attack overshoot — partial sway
                        target = 0.6f;
                        break;
                    case AirborneBossMovement.FreestyleIntent.Bank:
                        // Wide banking turn — full serpentine, graceful arcs
                        target = 0.85f;
                        break;
                    case AirborneBossMovement.FreestyleIntent.Stillhold:
                        // Hovering and watching — maximum coiled sway, full presence
                        target = 1.0f;
                        break;
                    default:
                        target = 0.5f;
                        break;
                }
                break;

            default:
                target = 1f;
                break;
        }

        // Collapse fast (attack cue must be sharp), build slow (sway resumes gracefully)
        float lerpRate = (target < currentUndulationScale) ? 4f : 1.5f;
        currentUndulationScale = Mathf.Lerp(currentUndulationScale, target, Time.deltaTime * lerpRate);
    }
}
