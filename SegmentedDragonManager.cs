using DG.Tweening;
using Dreamteck.Splines;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// Controls the multi-part Asian Dragon boss.
/// Added: segment regrowth support driven by a HealthCrystal. When a crystal is active
/// and the dragon is on its observation/recharge behavior, it will regrow missing body segments
/// up to the original spawn count at a configurable rate.
/// </summary>
public class SegmentedDragonManager : MonoBehaviour
{
    [Header("Dragon Anatomy Prefabs")]
    public GameObject headPrefab;
    [Tooltip("Optional: Inserted between Head and Body.")]
    public GameObject frontLegsPrefab;
    public GameObject bodyPrefab;
    [Tooltip("Optional: Inserted between Body and Tail.")]
    public GameObject backLegsPrefab;
    public GameObject tailPrefab;

    [Header("Structure")]
    [Tooltip("How many plain body segments to insert between the legs.")]
    public int numberOfBodySegments = 8;

    [Tooltip("Legacy spacing, now handled by DragonSpacingManager. Kept for initialization fallback.")]
    public float segmentSpacing = 2f;

    public float gapCloseDuration = 1f;

    [Tooltip("The total combined power of the boss based on remaining segments.")]
    public float totalBossPower { get; private set; }

    // List tracking all live segments. Head is index 0.
    private List<DragonSegment> activeSegments = new List<DragonSegment>();
    private SplineComputer bossSpline;

    // The new dynamic spacing manager
    private DragonSpacingManager spacingManager;

    // The Dreamteck follower component for the Head. The rest of the body follows this.
    private SplineFollower headFollower;
    private BossCreature bossBrain;

    // --- TETHER STATE ---
    private RopeArrow currentTether = null;
    public bool IsTethered { get; private set; } = false;
    public Transform TetherAnchorTransform { get; private set; } = null;
    public float TetherMaxLength { get; private set; } = 0f;

    /// <summary>
    /// Event invoked whenever the number of active segments changes (passes new remaining count).
    /// </summary>
    public event System.Action<int> OnSegmentCountChanged;

    // --- BREADCRUMB SLITHERING SYSTEM ---
    private struct PositionData
    {
        public Vector3 position;
        public Quaternion rotation;
        public float distanceTraveled; // Total distance head had traveled when recording this
    }
    private List<PositionData> positionHistory = new List<PositionData>();
    private float headTotalDistance = 0f;

    // --- Regrowth state ---
    private int originalSegmentCount = 0;
    private Coroutine regenCoroutine;
    [Tooltip("Seconds between regrowing one segment from the crystal.")]
    public float secondsPerRegrow = 1f;
    private bool regenerationLocked = false; // becomes true when crystal destroyed

    public void InitializeDragon(SplineComputer track)
    {
        bossSpline = track;
        totalBossPower = 0f;
        activeSegments.Clear();
        bossBrain = GetComponent<BossCreature>();

        spacingManager = GetComponent<DragonSpacingManager>();
        if (spacingManager == null)
        {
            spacingManager = gameObject.AddComponent<DragonSpacingManager>();
        }
        spacingManager.ClearSegments();

        int currentIndex = 0;

        // 1. Spawn Head
        SpawnSegment(headPrefab, currentIndex, track);
        headFollower = activeSegments[0].Follower;

        // Disable the head's follower component entirely. The body will follow the ROOT object instead!
        headFollower.enabled = false;
        headFollower.follow = false;

        currentIndex++;

        // 2. Spawn Front Legs (if assigned)
        if (frontLegsPrefab != null)
        {
            SpawnSegment(frontLegsPrefab, currentIndex, track);
            currentIndex++;
        }

        // 3. Spawn Body
        for (int i = 0; i < numberOfBodySegments; i++)
        {
            SpawnSegment(bodyPrefab, currentIndex, track);
            currentIndex++;
        }

        // 4. Spawn Back Legs (if assigned)
        if (backLegsPrefab != null)
        {
            SpawnSegment(backLegsPrefab, currentIndex, track);
            currentIndex++;
        }

        // 5. Spawn Tail
        SpawnSegment(tailPrefab, currentIndex, track);

        // Remember original count for regeneration
        originalSegmentCount = activeSegments.Count;

        // Clear follower components from body segments - they will be driven entirely by the History buffer!
        for (int i = 1; i < activeSegments.Count; i++)
        {
            if (activeSegments[i].Follower != null)
            {
                activeSegments[i].Follower.enabled = false;
            }
        }

        // Register all segments strictly after they are completely spawned
        if (spacingManager != null)
        {
            spacingManager.ClearSegments();
            foreach (var seg in activeSegments)
            {
                spacingManager.RegisterSegment(seg);
            }
        }

        RopeArrowManagerObi7.OnRopeBroken += OnRopeBroken;

        // Notify listeners of initial count
        OnSegmentCountChanged?.Invoke(activeSegments.Count);

        // Initialize history with pre-filled positions backward from ROOT object
        positionHistory.Clear();
        float totalLength = spacingManager != null ? spacingManager.GetTotalDragonLength() * 2f : activeSegments.Count * segmentSpacing * 2f;
        int samples = Mathf.CeilToInt(totalLength / 0.1f) + 1;

        if (bossSpline != null)
        {
            // We use the ROOT object's percent, as that is the true brain that moves!
            SplineFollower rootFollower = GetComponent<SplineFollower>();
            double startPercent = rootFollower != null ? rootFollower.GetPercent() : 0.0;
            float splineLength = bossSpline.CalculateLength();

            for (int i = 0; i < samples; i++)
            {
                float distBack = i * 0.1f;
                double percent = startPercent - (distBack / splineLength);
                if (bossSpline.isClosed)
                {
                    while (percent < 0.0) percent += 1.0;
                    while (percent > 1.0) percent -= 1.0;
                }
                else
                {
                    if (percent < 0.0) percent = 0.0;
                    if (percent > 1.0) percent = 1.0;
                }

                percent = System.Math.Clamp(percent, 0.0, 1.0);
                SplineSample sample = bossSpline.Evaluate(percent);

                positionHistory.Add(new PositionData
                {
                    position = sample.position,
                    rotation = sample.rotation,
                    distanceTraveled = -distBack
                });
            }

            UpdateSegmentSpacing(false, 1f);
        }
        else
        {
            positionHistory.Add(new PositionData
            {
                position = transform.position,
                rotation = transform.rotation,
                distanceTraveled = 0f
            });
        }
    }

    private void SpawnSegment(GameObject prefab, int index, SplineComputer track)
    {
        if (prefab == null) return;

        GameObject segmentObj = Instantiate(prefab, transform);
        segmentObj.name = $"DragonSegment_{index}";

        SplineFollower follower = segmentObj.GetComponent<SplineFollower>();
        if (follower == null) follower = segmentObj.AddComponent<SplineFollower>();

        DragonSegment segment = segmentObj.GetComponent<DragonSegment>();
        if (segment == null) segment = segmentObj.AddComponent<DragonSegment>();

        // Force indestructible flag based on prefab type
        segment.isDestructiblePart = (prefab == bodyPrefab);

        segment.Initialize(this, bossBrain, index);
        activeSegments.Add(segment);

        totalBossPower += segment.powerContribution;
    }

    /// <summary>
    /// Start a regeneration loop while the crystal exists and is not destroyed.
    /// Segments will be regrown at a rate of one per secondsPerRegrow until originalSegmentCount or crystal destroyed.
    /// </summary>
    public void StartRegeneration(HealthCrystal crystal)
    {
        if (regenerationLocked) return;
        if (crystal == null || crystal.IsDestroyed) return;

        StopRegeneration();
        regenCoroutine = StartCoroutine(RegrowCoroutine(crystal));
        Debug.Log("[SegmentedDragonManager] Starting regeneration from crystal.");
    }

    public void StopRegeneration()
    {
        if (regenCoroutine != null)
        {
            StopCoroutine(regenCoroutine);
            regenCoroutine = null;
            Debug.Log("[SegmentedDragonManager] Stopped regeneration.");
        }
    }

    private IEnumerator RegrowCoroutine(HealthCrystal crystal)
    {
        while (crystal != null && !crystal.IsDestroyed && activeSegments.Count < originalSegmentCount && !regenerationLocked)
        {
            RegrowOneSegment();
            yield return new WaitForSeconds(secondsPerRegrow);
        }
        regenCoroutine = null;
    }

    private void RegrowOneSegment()
    {
        // Regrow at the index before tail: place just before the final tail segment if exists.
        int insertIndex = Mathf.Max(1, activeSegments.Count - 1); // don't insert before head
        int spawnIndex = activeSegments.Count; // name index for naming only

        SpawnSegment(bodyPrefab, spawnIndex, bossSpline);

        // Re-register segments with spacing manager
        if (spacingManager != null)
        {
            spacingManager.RegisterSegment(activeSegments[activeSegments.Count - 1]);
            spacingManager.RefreshSegments(activeSegments);
        }

        // Recalculate indices and notify
        for (int i = 0; i < activeSegments.Count; i++)
        {
            activeSegments[i].SegmentIndex = i;
        }

        OnSegmentCountChanged?.Invoke(activeSegments.Count);
        Debug.Log($"[SegmentedDragonManager] Regrew a body segment. New count: {activeSegments.Count}/{originalSegmentCount}");
    }

    /// <summary>
    /// Call to permanently lock regeneration (e.g. when crystal destroyed).
    /// </summary>
    public void LockRegenerationPermanently()
    {
        regenerationLocked = true;
        StopRegeneration();
        Debug.Log("[SegmentedDragonManager] Regeneration permanently locked (crystal destroyed).");
    }

    /// <summary>
    /// Updates the target spline for all active segments (e.g. when the boss evades to a new track).
    /// </summary>
    public void SwitchToNewSpline(SplineComputer newTrack)
    {
        bossSpline = newTrack;
    }

    public void HandleRopeAttached(DragonSegment segment, RopeArrow rope)
    {
        if (rope == null || segment == null) return;

        currentTether = rope;
        IsTethered = true;

        TetherAnchorTransform = rope.GetTailTransform();

        if (TetherAnchorTransform != null)
        {
            TetherMaxLength = Vector3.Distance(transform.position, TetherAnchorTransform.position);
        }
        else
        {
            TetherMaxLength = 0f;
        }

        Debug.Log($"<color=cyan>[SegmentedDragonManager] Tether attached to segment {segment.SegmentIndex}. MaxLength: {TetherMaxLength:F2}</color>");
    }

    public void ReleaseTetherFromSegment(DragonSegment segment, RopeArrow rope)
    {
        if (rope == null) return;
        if (currentTether == rope)
        {
            Debug.Log("<color=cyan>[SegmentedDragonManager] Tether released.</color>");
            currentTether = null;
            IsTethered = false;
            TetherAnchorTransform = null;
            TetherMaxLength = 0f;
        }
    }

    private void OnRopeBroken(RopeArrow rope)
    {
        if (rope == null) return;
        if (currentTether == rope)
        {
            ReleaseTetherFromSegment(null, rope);
        }
    }

    private void OnDestroy()
    {
        RopeArrowManagerObi7.OnRopeBroken -= OnRopeBroken;
    }

    // Controls whether the Update loop forces rigid spacing. Disabled briefly when closing a gap.
    private bool isClosingGap = false;
    private float gapCloseTimer = 0f;


    // When gap closing, segments blend from an inflated spacing value down to the normal spacing value
    private Dictionary<DragonSegment, float> currentSpacings = new Dictionary<DragonSegment, float>();

    private void LateUpdate()
    {
        if (activeSegments.Count == 0) return;

        // 1. Update the Breadcrumb History using the ROOT BOSS object, not the spawned head
        Vector3 currentHeadPos = transform.position;
        if (positionHistory.Count == 0) return;
        PositionData lastData = positionHistory[0];

        float distMovedSinceLastFrame = Vector3.Distance(currentHeadPos, lastData.position);

        if (distMovedSinceLastFrame > 0.05f)
        {
            headTotalDistance += distMovedSinceLastFrame;

            positionHistory.Insert(0, new PositionData
            {
                position = currentHeadPos,
                rotation = transform.rotation,
                distanceTraveled = headTotalDistance
            });

            float maxNeededHistoryDistance = spacingManager != null ? spacingManager.GetTotalDragonLength() * 2f : segmentSpacing * activeSegments.Count * 2f;
            if (headTotalDistance - positionHistory[positionHistory.Count - 1].distanceTraveled > maxNeededHistoryDistance)
            {
                positionHistory.RemoveAt(positionHistory.Count - 1);
            }
        }

        // 2. Handle Gap Closing Animation Timers
        if (isClosingGap)
        {
            gapCloseTimer += Time.deltaTime;
            float t = gapCloseTimer / gapCloseDuration;

            // Smoothly ease the interpolation
            t = Mathf.SmoothStep(0f, 1f, t);

            if (t >= 1f)
            {
                t = 1f;
                isClosingGap = false; // Done closing gap, resume rigid follow
            }

            UpdateSegmentSpacing(true, t);
        }
        else
        {
            UpdateSegmentSpacing(false, 1f); // Follow rigidly
        }
    }

    private void UpdateSegmentSpacing(bool animateSmoothly, float lerpT)
    {
        // 3. Move all segments (including the Head at index 0) along the history buffer.
        // Because they follow the root object, the head just trails at 0 distance!
        for (int i = 0; i < activeSegments.Count; i++)
        {
            DragonSegment segment = activeSegments[i];

            float requiredDistanceBehindHead = spacingManager != null ? spacingManager.GetTargetDistanceForSegment(segment) : segmentSpacing * i;

            if (animateSmoothly && currentSpacings.ContainsKey(segment))
            {
                // Smoothly close the gap
                requiredDistanceBehindHead = Mathf.Lerp(currentSpacings[segment], requiredDistanceBehindHead, lerpT);
            }

            float targetDistanceInHistory = headTotalDistance - requiredDistanceBehindHead;

            // Find the two breadcrumbs this distance falls between
            for (int j = 0; j < positionHistory.Count - 1; j++)
            {
                PositionData newer = positionHistory[j];
                PositionData older = positionHistory[j + 1];

                if (targetDistanceInHistory <= newer.distanceTraveled && targetDistanceInHistory >= older.distanceTraveled)
                {
                    // Interpolate between these two breadcrumbs
                    float range = newer.distanceTraveled - older.distanceTraveled;
                    float t = (newer.distanceTraveled - targetDistanceInHistory) / range; // 0 = at newer, 1 = at older

                    segment.transform.position = Vector3.Lerp(newer.position, older.position, t);

                    // --- SEGMENT ALIGNMENT FIX ---
                    // Instead of strictly copying the head's historical rotation, each segment looks
                    // at the physical position of the segment immediately in front of it.
                    // This creates a natural cascading sinusoidal curve through the air, without relying on the Spline rotation.
                    if (i > 0 && activeSegments[i - 1] != null)
                    {
                        Vector3 directionToAhead = (activeSegments[i - 1].transform.position - segment.transform.position).normalized;
                        if (directionToAhead != Vector3.zero)
                        {
                            segment.transform.rotation = Quaternion.LookRotation(directionToAhead);
                        }
                        else
                        {
                            segment.transform.rotation = Quaternion.Slerp(newer.rotation, older.rotation, t);
                        }
                    }
                    else
                    {
                        // Head segment (i == 0) still copies history rotation directly to follow the actual flight path steering.
                        segment.transform.rotation = Quaternion.Slerp(newer.rotation, older.rotation, t);
                    }

                    break;
                }
            }
        }
    }

    public void PauseSplineFollow()
    {
        // No longer needed, root object handles its own follow state
    }

    public void ResumeSplineFollow()
    {
        // No longer needed
    }

    /// <summary>
    /// Called by a DragonSegment when it is destroyed.
    /// </summary>
    public void OnSegmentDestroyed(DragonSegment destroyedSegment)
    {
        totalBossPower -= destroyedSegment.powerContribution;

        // Before removing the piece, store EVERY segment's exact current physical target distance
        // so we can smooth lerp from exactly where they are right now to their new tighter positions.

        // Temporarily store current lerp distances in case we are interrupting a gap close
        Dictionary<DragonSegment, float> previousSpacings = new Dictionary<DragonSegment, float>(currentSpacings);
        currentSpacings.Clear();

        foreach (var segment in activeSegments)
        {
            if (segment != destroyedSegment)
            {
                float dist = spacingManager != null ? spacingManager.GetTargetDistanceForSegment(segment) : segment.SegmentIndex * segmentSpacing;

                // If we were already closing a gap, we want to lerp from our CURRENT interpolated distance
                if (isClosingGap && previousSpacings.ContainsKey(segment))
                {
                    float lerpT = Mathf.SmoothStep(0f, 1f, gapCloseTimer / gapCloseDuration);
                    float actualInterpolatedDist = Mathf.Lerp(previousSpacings[segment], dist, lerpT);
                    currentSpacings[segment] = actualInterpolatedDist;
                }
                else
                {
                    currentSpacings[segment] = dist;
                }
            }
        }

        activeSegments.Remove(destroyedSegment);
        if (spacingManager != null)
        {
            spacingManager.RefreshSegments(activeSegments);
        }

        Debug.Log($"<color=magenta>[SegmentedDragonManager] A segment fell! Boss power reduced to {totalBossPower}. Closing gap!</color>");

        if (activeSegments.Count == 0)
        {
            // The whole dragon is dead!
            return;
        }

        // Update the indices of remaining active segments
        int destructibleCount = 0;
        for (int i = 0; i < activeSegments.Count; i++)
        {
            activeSegments[i].SegmentIndex = i;
            if (activeSegments[i].isDestructiblePart)
            {
                destructibleCount++;
            }
        }

        if (destructibleCount == 0 && bossBrain != null)
        {
            Debug.Log("<color=red>[SegmentedDragonManager] No destructible segments remain. Executing Boss Death!</color>");
            bossBrain.TakeDamage(99999f, transform.position, ElementTypeOB7.Normal);
        }

        // Notify listeners of segment loss
        OnSegmentCountChanged?.Invoke(activeSegments.Count);

        // Handle tether detachment if the piece that dissolved was the tether anchor
        if (IsTethered && currentTether != null)
        {
            Transform tail = currentTether.GetTailTransform();
            if (tail != null && (tail.IsChildOf(destroyedSegment.transform) || tail == destroyedSegment.transform))
            {
                ReleaseTetherFromSegment(destroyedSegment, currentTether);
            }
        }

        // Pause standard rigidly-spaced updates and begin the smooth gap close
        isClosingGap = true;
        gapCloseTimer = 0f;
    }



    /// <summary>
    /// Called by BossCreature when the overall health reaches 0.
    /// Commands all remaining permanent pieces (Head, Legs, Tail) to die.
    /// </summary>
    public void TriggerTotalDeath()
    {
        Debug.Log("<color=red>[SegmentedDragonManager] The entire dragon is collapsing!</color>");

        float longestDissolveDuration = 0f;

        foreach (var segment in activeSegments)
        {
            if (segment != null)
            {
                segment.TriggerTotalDeath();

                // Find the longest dissolve time to synchronize the root destruction
                DissolveEffect dissolve = segment.GetComponentInChildren<DissolveEffect>();
                if (dissolve != null)
                {
                    // NEW: 2. Directly accessing public property without Reflection.
                    float duration = dissolve.dissolveDuration;
                    if (duration > longestDissolveDuration) longestDissolveDuration = duration;
                }
            }
        }
        activeSegments.Clear();

        currentTether = null;
        IsTethered = false;
        TetherAnchorTransform = null;
        TetherMaxLength = 0f;

        OnSegmentCountChanged?.Invoke(0);

        // Schedule the root boss object (which holds BossCreature) to be destroyed exactly
        // after the longest child dissolve finishes. This guarantees the visual completes gracefully
        // AND the TrainingLevelManager detects the null object to advance the wave!
        Destroy(gameObject, longestDissolveDuration + 0.1f);
    }
}
