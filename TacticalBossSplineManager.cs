using System;
using System.Collections;
using UnityEngine;
using Dreamteck.Splines;
using DG.Tweening;
using Random = UnityEngine.Random;

/// <summary>
/// A manager designed for advanced Boss-level creatures.
/// Desire-driven AI (Patrol, Pursue/Attack, Evade, Recharge).
/// Includes full movement helpers (freestyle & spline attach) used elsewhere in the codebase.
/// </summary>
[RequireComponent(typeof(SplineFollower))]
public class TacticalBossSplineManager : MonoBehaviour
{
    public enum Desire
    {
        Idle,
        Patrol,
        Pursue,
        Attack,
        Evade,
        Recharge
    }

    [Header("Boss Settings")]
    [Tooltip("How long the boss stays on an escape route before attacking.")]
    public float timeOnRoute = 5f;

    [Tooltip("How long it takes to smoothly hop from one route to another.")]
    public float hopDuration = 1.5f;

    [Header("Movement")]
    [Tooltip("Central movement speed value used both on-spline and while freestyle flying.")]
    public float dragonMovementSpeed = 20f;

    [Header("Freestyle Flight Settings")]
    [Tooltip("How fast the dragon turns/steers when flying in the air.")]
    public float freestyleTurnSpeed = 3f;

    [Tooltip("When pursuing the player, the dragon will stop steering directly at them and swoop past if it gets this close.")]
    public float swoopDistance = 15f;

    [Header("Tether (soft constraint)")]
    [Tooltip("How stiff the tether pull is when the dragon reaches the max rope length. Higher = snappier.")]
    public float tetherSpringStrength = 8f;

    [Header("Projectile Attack (Mouth)")]
    [Tooltip("Projectile prefab to spawn from the dragon's mouth when attacking.")]
    public GameObject projectilePrefab;
    [Tooltip("Transform representing the mouth / projectile spawn point. If null, uses the boss root transform.")]
    public Transform mouthTransform;
    [Tooltip("Projectile launch impulse speed.")]
    public float projectileSpeed = 30f;
    [Tooltip("Seconds between projectile shots.")]
    public float projectileFireRate = 2f;

    [Header("AI Tuning")]
    [Tooltip("Preferred splines the dragon will use when evading. Leave empty to use tacticalEscapeRoutes.")]
    public SplineComputer[] preferredEscapeRoutes;
    [Tooltip("How close the player must be to trigger pursuit.")]
    public float pursueTriggerDistance = 30f;
    [Tooltip("Max continuous seconds the dragon will pursue before re-evaluating.")]
    public float maxPursueDuration = 12f;
    [Tooltip("Chance per second the dragon will fire while pursuing.")]
    [Range(0f, 1f)] public float attackChancePerSecond = 0.25f;
    [Tooltip("When patrolling on the observation spline how long (secs) each patrol segment lasts.")]
    public Vector2 patrolSegmentDuration = new Vector2(6f, 14f);

    [Header("Evasion Selection")]
    [Tooltip("Chance [0..1] to prefer designer 'preferredEscapeRoutes' over procedural 'tacticalEscapeRoutes' when both are available.")]
    [Range(0f, 1f)] public float preferredRouteUsageChance = 0.75f;

    // These are injected at runtime by BossArenaManager to avoid Prefab serialization issues
    private SplineComputer observationSpline;
    private SplineComputer[] tacticalEscapeRoutes;

    private SplineFollower bossFollower;
    private SegmentedDragonManager bodyManager;
    private Transform playerTransform;

    private SplineComputer temporaryBridgeSpline;

    // Projectile cooldown
    private float projectileCooldown = 0f;

    // AI
    private Coroutine aiRoutine;
    [SerializeField] private Desire currentDesire = Desire.Idle;
    private BossCreature bossBrain;

    private void Awake()
    {
        bossFollower = GetComponent<SplineFollower>();
        bodyManager = GetComponent<SegmentedDragonManager>();
        bossBrain = GetComponent<BossCreature>();

        if (bossFollower != null) bossFollower.followSpeed = dragonMovementSpeed;
    }

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) playerTransform = playerObj.transform;

        if (mouthTransform == null)
        {
            StartCoroutine(FindMouthTransformRoutine());
        }

        aiRoutine = StartCoroutine(AIRoutine());
    }

    private void OnDestroy()
    {
        if (aiRoutine != null) StopCoroutine(aiRoutine);
        transform.DOKill();
    }

    /// <summary>
    /// Initialize the splines the tactical manager will use.
    /// Must be called by the BossArenaManager at spawn time so the manager always has links to scene roots.
    /// </summary>
    public void InitializeRoutes(SplineComputer observation, SplineComputer[] escapes)
    {
        if (bossFollower == null) bossFollower = GetComponent<SplineFollower>();
        if (bodyManager == null) bodyManager = GetComponent<SegmentedDragonManager>();
        if (bossFollower == null) return;

        bossFollower.followSpeed = dragonMovementSpeed;

        observationSpline = observation;
        tacticalEscapeRoutes = escapes;

        Debug.Log($"[TacticalBossSplineManager] InitializeRoutes: observation={(observationSpline != null)} tacticalEscapeRoutes={(tacticalEscapeRoutes != null ? tacticalEscapeRoutes.Length.ToString() : "null")} preferredEscapeRoutes={(preferredEscapeRoutes != null ? preferredEscapeRoutes.Length.ToString() : "null")}", this);

        if (observationSpline != null)
        {
            bossFollower.follow = false;
            transform.position = observationSpline.Evaluate(0).position;
            bossFollower.spline = observationSpline;
            bossFollower.SetPercent(0);
            bossFollower.follow = true;

            if (bodyManager != null) bodyManager.SwitchToNewSpline(observationSpline);
        }

        if (mouthTransform == null) StartCoroutine(FindMouthTransformRoutine());
    }

    /// <summary>
    /// Runtime helper to inspect assigned route counts from the Console.
    /// </summary>
    public void DebugListRoutes()
    {
        int prefCount = preferredEscapeRoutes != null ? preferredEscapeRoutes.Length : 0;
        int tacCount = tacticalEscapeRoutes != null ? tacticalEscapeRoutes.Length : 0;
        Debug.Log($"[TacticalBossSplineManager] preferredEscapeRoutes={prefCount}, tacticalEscapeRoutes={tacCount}", this);
    }

    private IEnumerator FindMouthTransformRoutine()
    {
        if (mouthTransform != null) yield break;

        const float pollInterval = 0.25f;
        const float timeout = 5f;
        float elapsed = 0f;

        while (mouthTransform == null && elapsed < timeout)
        {
            if (bodyManager != null)
            {
                Transform headInstance = bodyManager.transform.Find("DragonSegment_0");
                if (headInstance != null)
                {
                    Transform found = FindChildByNameRecursive(headInstance, "mouthTransform")
                                      ?? FindChildByNameRecursive(headInstance, "MouthTransform")
                                      ?? FindChildByNameRecursive(headInstance, "mouth")
                                      ?? FindChildByNameRecursive(headInstance, "Mouth");

                    if (found != null)
                    {
                        mouthTransform = found;
                        Debug.Log($"[TacticalBossSplineManager] Found mouth transform at '{GetFullPath(mouthTransform)}'.", this);
                        yield break;
                    }
                }
            }
            elapsed += pollInterval;
            yield return new WaitForSeconds(pollInterval);
        }

        if (mouthTransform == null)
        {
            Debug.LogWarning("[TacticalBossSplineManager] Could not locate a mouthTransform on the runtime head. Falling back to boss root transform for projectile spawn.", this);
        }
    }

    private Transform FindChildByNameRecursive(Transform root, string nameToFind)
    {
        if (root == null) return null;
        var children = root.GetComponentsInChildren<Transform>(true);
        string target = nameToFind.ToLowerInvariant();
        foreach (var t in children) if (t.name.ToLowerInvariant() == target) return t;
        return null;
    }

    private string GetFullPath(Transform t)
    {
        if (t == null) return "<null>";
        string path = t.name;
        Transform p = t.parent;
        while (p != null) { path = p.name + "/" + path; p = p.parent; }
        return path;
    }

    // ---------------- AI ----------------
    private IEnumerator AIRoutine()
    {
        while (true)
        {
            if (bodyManager != null && bodyManager.IsTethered)
            {
                currentDesire = Desire.Evade;
                StartEvasionRoutine();
                yield return new WaitForSeconds(3f);
                continue;
            }

            if (playerTransform == null)
            {
                currentDesire = Desire.Patrol;
                yield return StartCoroutine(PatrolBehavior());
                continue;
            }

            float distToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distToPlayer <= pursueTriggerDistance)
            {
                currentDesire = Desire.Pursue;
                yield return StartCoroutine(PursueAndAttackBehavior(playerTransform));
                continue;
            }

            currentDesire = Desire.Patrol;
            yield return StartCoroutine(PatrolBehavior());
        }
    }

    private IEnumerator PatrolBehavior()
    {
        if (observationSpline != null && bossFollower != null)
        {
            bossFollower.follow = true;
            bossFollower.spline = observationSpline;
            bossFollower.wrapMode = SplineFollower.Wrap.Loop;
            bossFollower.followSpeed = dragonMovementSpeed;

            float patrolTime = UnityEngine.Random.Range(patrolSegmentDuration.x, patrolSegmentDuration.y);
            float elapsed = 0f;
            while (elapsed < patrolTime)
            {
                if (bodyManager != null && bodyManager.IsTethered) yield break;
                if (playerTransform != null && Vector3.Distance(transform.position, playerTransform.position) <= pursueTriggerDistance) yield break;
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            float wanderTime = UnityEngine.Random.Range(patrolSegmentDuration.x * 0.5f, patrolSegmentDuration.y * 0.5f);
            float elapsed = 0f;
            if (bossFollower != null) bossFollower.follow = false;
            if (bodyManager != null) bodyManager.SwitchToNewSpline(null);
            while (elapsed < wanderTime)
            {
                transform.position += transform.forward * dragonMovementSpeed * Time.deltaTime;
                transform.Rotate(0f, (Mathf.PerlinNoise(Time.time * 0.2f, 0f) - 0.5f) * 2f * freestyleTurnSpeed, 0f);
                elapsed += Time.deltaTime;
                if (playerTransform != null && Vector3.Distance(transform.position, playerTransform.position) <= pursueTriggerDistance) break;
                yield return null;
            }
        }
    }

    private IEnumerator PursueAndAttackBehavior(Transform target)
    {
        if (target == null) yield break;

        StartFreestylePursuit(target);

        float elapsed = 0f;
        float pursueDuration = UnityEngine.Random.Range(maxPursueDuration * 0.5f, maxPursueDuration);
        while (elapsed < pursueDuration)
        {
            if (target == null) break;
            if (bodyManager != null && bodyManager.IsTethered) yield break;

            UpdateFreestylePursuit(target);

            if (UnityEngine.Random.value < attackChancePerSecond * Time.deltaTime)
            {
                HandleProjectileAttack(target);
            }

            if (Vector3.Distance(transform.position, target.position) > pursueTriggerDistance * 1.5f) break;

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (UnityEngine.Random.value < 0.5f && target != null && (bodyManager == null || !bodyManager.IsTethered))
        {
            float burst = 2f + UnityEngine.Random.value * 2f;
            float t = 0f;
            while (t < burst && target != null)
            {
                UpdateFreestylePursuit(target);
                if (UnityEngine.Random.value < attackChancePerSecond * Time.deltaTime * 2f)
                {
                    HandleProjectileAttack(target);
                }
                t += Time.deltaTime;
                yield return null;
            }
        }

        if (observationSpline != null && bossFollower != null)
        {
            yield return StartCoroutine(FreestyleToSplineRoutine(observationSpline));
            bossFollower.wrapMode = SplineFollower.Wrap.Loop;
        }
    }

    private void HandleProjectileAttack(Transform target)
    {
        if (projectilePrefab == null || target == null) return;

        projectileCooldown -= Time.deltaTime;
        if (projectileCooldown > 0f) return;

        Transform spawn = mouthTransform != null ? mouthTransform : transform;
        Vector3 spawnPos = spawn.position;
        Vector3 dir = (target.position - spawnPos).normalized;
        Quaternion spawnRot = Quaternion.LookRotation(dir);

        GameObject projGO = Instantiate(projectilePrefab, spawnPos, spawnRot);

        var dragonProj = projGO.GetComponent<DragonProjectile>();
        Rigidbody rb = projGO.GetComponent<Rigidbody>();

        if (dragonProj != null)
        {
            dragonProj.Launch(dir, projectileSpeed, /*damage*/ 10f);
        }
        else if (rb != null)
        {
            rb.isKinematic = false;
            rb.WakeUp();
            rb.linearVelocity = dir * projectileSpeed;
        }
        else
        {
            Debug.LogWarning("[TacticalBossSplineManager] Spawned projectile has no Rigidbody or DragonProjectile component; it will not move.", this);
        }

        projectileCooldown = Mathf.Max(0.01f, projectileFireRate);
    }

    // ---------------- Movement helpers (restored) ----------------

    public void StartEvasionRoutine()
    {
        // StartEvasionRoutine must respect that InitializeRoutes(...) may not yet have been called.
        if ((preferredEscapeRoutes == null || preferredEscapeRoutes.Length == 0) &&
            (tacticalEscapeRoutes == null || tacticalEscapeRoutes.Length == 0))
        {
            Debug.LogWarning("[TacticalBossSplineManager] No escape routes assigned (preferred or tactical). Cannot start evasion.", this);
            return;
        }

        StopAllCoroutines();
        StartCoroutine(EvasionAndRechargeRoutine());
    }

    public void EvadeToEscapeRoute()
    {
        StartEvasionRoutine();
    }

    public void StartFreestylePursuit(Transform target)
    {
        StopAllCoroutines();
        bossFollower.follow = false;
        if (bodyManager != null) bodyManager.SwitchToNewSpline(null);
    }

    public void UpdateFreestylePursuit(Transform target)
    {
        if (target == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, target.position);

        // Natural momentum: Always fly forward
        transform.position += transform.forward * dragonMovementSpeed * Time.deltaTime;

        if (distanceToPlayer > swoopDistance)
        {
            Vector3 direction = (target.position - transform.position).normalized;
            if (direction != Vector3.zero)
            {
                Quaternion lookRot = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * freestyleTurnSpeed * 0.75f);
            }
        }
    }

    /// <summary>
    /// Choose a single escape route combining designer-preferred and scene tactical routes.
    /// Uses `preferredRouteUsageChance` to bias selection toward designer routes when both sets are available.
    /// </summary>
    /// <summary>
    /// Choose the most logical escape route based on proximity to the dragon's current world position.
    /// Combines preferredEscapeRoutes and tacticalEscapeRoutes into a candidate set and returns the spline
    /// whose nearest sample point is the closest to 'fromPosition'.
    /// </summary>
    private SplineComputer ChooseEscapeRoute(Vector3 fromPosition)
    {
        // Build the candidate list: include preferred first (designer choreography) and tactical (scene) after.
        var candidates = new System.Collections.Generic.List<SplineComputer>();
        if (preferredEscapeRoutes != null)
        {
            foreach (var s in preferredEscapeRoutes) if (s != null) candidates.Add(s);
        }
        if (tacticalEscapeRoutes != null)
        {
            foreach (var s in tacticalEscapeRoutes) if (s != null) candidates.Add(s);
        }

        if (candidates.Count == 0) return null;

        // Find the candidate with the minimum nearest-point distance to 'fromPosition'
        float bestDist = float.MaxValue;
        SplineComputer best = null;

        // Number of samples to probe per spline to estimate nearest point.
        // Increase if you need more precision (costlier).
        const int sampleCount = 32;

        foreach (var cand in candidates)
        {
            try
            {
                // Sample the spline at evenly spaced percents and find the closest sample position.
                for (int i = 0; i < sampleCount; i++)
                {
                    double pct = (sampleCount == 1) ? 0.0 : (double)i / (sampleCount - 1);
                    SplineSample sample = cand.Evaluate(pct);
                    float d = Vector3.SqrMagnitude(sample.position - fromPosition);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = cand;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TacticalBossSplineManager] Error sampling candidate spline '{cand?.name}': {ex.Message}", this);
            }
        }

        Debug.Log($"[TacticalBossSplineManager] ChooseEscapeRoute: chosen='{best?.name ?? "<null>"}' distance={Mathf.Sqrt(bestDist):F2}", this);
        return best;
    }

    private IEnumerator FreestyleToSplineRoutine(SplineComputer targetRoute)
    {
        if (bossFollower == null) bossFollower = GetComponent<SplineFollower>();

        bossFollower.follow = false;
        if (bodyManager != null) bodyManager.SwitchToNewSpline(null);

        SplineSample targetStartSample = targetRoute.Evaluate(0);
        bool hasReachedTarget = false;

        while (!hasReachedTarget)
        {
            transform.position += transform.forward * dragonMovementSpeed * Time.deltaTime;

            Vector3 directionToStart = (targetStartSample.position - transform.position).normalized;
            float distance = Vector3.Distance(transform.position, targetStartSample.position);

            if (distance > 10f)
            {
                if (directionToStart != Vector3.zero)
                {
                    Quaternion lookRot = Quaternion.LookRotation(directionToStart);
                    transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * freestyleTurnSpeed);
                }
            }
            else
            {
                if (targetStartSample.forward != Vector3.zero)
                {
                    Quaternion alignRot = Quaternion.LookRotation(targetStartSample.forward);
                    transform.rotation = Quaternion.Slerp(transform.rotation, alignRot, Time.deltaTime * freestyleTurnSpeed * 2f);
                }

                if (distance < 2f)
                {
                    hasReachedTarget = true;
                }
            }

            yield return null;
        }

        bossFollower.follow = false;
        bossFollower.spline = targetRoute;
        bossFollower.wrapMode = SplineFollower.Wrap.Default;
        bossFollower.SetPercent(0);
        bossFollower.follow = true;

        if (bodyManager != null) bodyManager.SwitchToNewSpline(targetRoute);
    }

    private IEnumerator EvasionAndRechargeRoutine()
    {
        // Ensure we have candidate routes (either preferred or tactical)
        if ((preferredEscapeRoutes == null || preferredEscapeRoutes.Length == 0) &&
            (tacticalEscapeRoutes == null || tacticalEscapeRoutes.Length == 0))
        {
            Debug.LogWarning("[TacticalBossSplineManager] No escape routes assigned (preferred or tactical). Aborting evasion.", this);
            yield break;
        }

        // Choose the most logical route based on current position
        SplineComputer targetRoute = ChooseEscapeRoute(transform.position);
        if (targetRoute == null)
        {
            Debug.LogWarning("[TacticalBossSplineManager] No escape route chosen (none available). Aborting evasion.", this);
            yield break;
        }

        // If we are already using that spline, try to pick the next-best alternative (if available)
        // Build same candidate array for proximity re-evaluation
        var sourceArray = new System.Collections.Generic.List<SplineComputer>();
        if (preferredEscapeRoutes != null) foreach (var s in preferredEscapeRoutes) if (s != null) sourceArray.Add(s);
        if (tacticalEscapeRoutes != null) foreach (var s in tacticalEscapeRoutes) if (s != null) sourceArray.Add(s);

        if (sourceArray.Count > 1 && targetRoute == bossFollower.spline)
        {
            // pick the next closest that is not the current spline
            SplineComputer secondBest = null;
            float secondBestDist = float.MaxValue;
            const int sampleCount = 32;
            foreach (var cand in sourceArray)
            {
                if (cand == targetRoute) continue;
                try
                {
                    for (int i = 0; i < sampleCount; i++)
                    {
                        double pct = (sampleCount == 1) ? 0.0 : (double)i / (sampleCount - 1);
                        SplineSample sample = cand.Evaluate(pct);
                        float d = Vector3.SqrMagnitude(sample.position - transform.position);
                        if (d < secondBestDist)
                        {
                            secondBestDist = d;
                            secondBest = cand;
                        }
                    }
                }
                catch { /* ignore sampling errors */ }
            }
            if (secondBest != null) targetRoute = secondBest;
        }

        Debug.Log($"[TacticalBossSplineManager] Evasion starting -> targetRoute: {(targetRoute != null ? targetRoute.name : "<null>")}", this);

        // Fly to and follow the chosen spline
        yield return StartCoroutine(FreestyleToSplineRoutine(targetRoute));

        // Wait until we've traversed most of it
        while (bossFollower.GetPercent() < 0.99f)
        {
            yield return null;
        }

        // Detach follower and fly out of the end
        bossFollower.follow = false;
        if (bodyManager != null) bodyManager.SwitchToNewSpline(null);

        Vector3 currentForward = transform.forward;
        float flyOutTimer = 0f;
        float flyOutDuration = 2f;
        while (flyOutTimer < flyOutDuration)
        {
            flyOutTimer += Time.deltaTime;
            transform.position += currentForward * 20f * Time.deltaTime;
            yield return null;
        }

        // Return to observation spline if available
        if (observationSpline != null)
        {
            yield return StartCoroutine(FreestyleToSplineRoutine(observationSpline));
            bossFollower.wrapMode = SplineFollower.Wrap.Loop;
        }
        else
        {
            bossFollower.follow = false;
        }

        BossCreature brain = GetComponent<BossCreature>();
        PixelCrushers.MessageSystem.SendMessage(this, "Brain", "RechargeFull", string.Empty);
    }
}