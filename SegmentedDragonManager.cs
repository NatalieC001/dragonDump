using DG.Tweening;
using Dreamteck.Splines;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// Controls the multi-part Asian Dragon boss.
/// Strictly manages instantiation, spacing (positions), and gap-closing when segments die.
/// Does NOT handle rotation (managed by DragonBodySegmentRippleAnimator) or navigation.
/// </summary>
public class SegmentedDragonManager : MonoBehaviour
{
    [Header("Dragon Anatomy Prefabs")]
    public GameObject headPrefab;
    public GameObject frontLegsPrefab;
    public GameObject bodyPrefab;
    public GameObject backLegsPrefab;
    public GameObject tailPrefab;

    [Header("Structure")]
    public int numberOfBodySegments = 8;
    public float segmentSpacing = 2f;
    public float gapCloseDuration = 1f;
    public float totalBossPower { get; private set; }

    private List<DragonSegment> activeSegments = new List<DragonSegment>();
    private SplineComputer bossSpline;
    private DragonSpacingManager spacingManager;
    private SplineFollower headFollower;
    private BossCreature bossBrain;

    public event System.Action<int> OnSegmentCountChanged;

    private struct PositionData
    {
        public Vector3 position;
        public Quaternion rotation;
        public float distanceTraveled;
    }
    private List<PositionData> positionHistory = new List<PositionData>();
    private float headTotalDistance = 0f;

    private int originalSegmentCount = 0;
    private Coroutine regenCoroutine;
    public float secondsPerRegrow = 1f;
    private bool regenerationLocked = false;

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

        SpawnSegment(headPrefab, currentIndex, track);
        headFollower = activeSegments[0].Follower;
        headFollower.enabled = false;
        headFollower.follow = false;
        currentIndex++;

        if (frontLegsPrefab != null)
        {
            SpawnSegment(frontLegsPrefab, currentIndex, track);
            currentIndex++;
        }

        for (int i = 0; i < numberOfBodySegments; i++)
        {
            SpawnSegment(bodyPrefab, currentIndex, track);
            currentIndex++;
        }

        if (backLegsPrefab != null)
        {
            SpawnSegment(backLegsPrefab, currentIndex, track);
            currentIndex++;
        }

        SpawnSegment(tailPrefab, currentIndex, track);

        originalSegmentCount = activeSegments.Count;

        for (int i = 1; i < activeSegments.Count; i++)
        {
            if (activeSegments[i].Follower != null)
            {
                activeSegments[i].Follower.enabled = false;
            }
        }

        if (spacingManager != null)
        {
            spacingManager.ClearSegments();
            foreach (var seg in activeSegments)
            {
                spacingManager.RegisterSegment(seg);
            }
        }

        OnSegmentCountChanged?.Invoke(activeSegments.Count);

        positionHistory.Clear();
        float totalLength = spacingManager != null ? spacingManager.GetTotalDragonLength() * 2f : activeSegments.Count * segmentSpacing * 2f;
        int samples = Mathf.CeilToInt(totalLength / 0.1f) + 1;

        if (bossSpline != null)
        {
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
                    percent = System.Math.Clamp(percent, 0.0, 1.0);
                }

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

        segment.isDestructiblePart = (prefab == bodyPrefab);

        segment.Initialize(this, bossBrain, index);
        activeSegments.Add(segment);

        totalBossPower += segment.powerContribution;
    }

    public void StartRegeneration(HealthCrystal crystal)
    {
        if (regenerationLocked) return;
        if (crystal == null || crystal.IsDestroyed) return;

        StopRegeneration();
        regenCoroutine = StartCoroutine(RegrowCoroutine(crystal));
    }

    public void StopRegeneration()
    {
        if (regenCoroutine != null)
        {
            StopCoroutine(regenCoroutine);
            regenCoroutine = null;
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
        int spawnIndex = activeSegments.Count;
        SpawnSegment(bodyPrefab, spawnIndex, bossSpline);

        if (spacingManager != null)
        {
            spacingManager.RegisterSegment(activeSegments[activeSegments.Count - 1]);
            spacingManager.RefreshSegments(activeSegments);
        }

        for (int i = 0; i < activeSegments.Count; i++)
        {
            activeSegments[i].SegmentIndex = i;
        }

        OnSegmentCountChanged?.Invoke(activeSegments.Count);
    }

    public void LockRegenerationPermanently()
    {
        regenerationLocked = true;
        StopRegeneration();
    }

    public void SwitchToNewSpline(SplineComputer newTrack)
    {
        bossSpline = newTrack;
    }

    private bool isClosingGap = false;
    private float gapCloseTimer = 0f;
    private Dictionary<DragonSegment, float> currentSpacings = new Dictionary<DragonSegment, float>();

    private void LateUpdate()
    {
        if (activeSegments.Count == 0) return;

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

        if (isClosingGap)
        {
            gapCloseTimer += Time.deltaTime;
            float t = gapCloseTimer / gapCloseDuration;
            t = Mathf.SmoothStep(0f, 1f, t);

            if (t >= 1f)
            {
                t = 1f;
                isClosingGap = false;
            }

            UpdateSegmentSpacing(true, t);
        }
        else
        {
            UpdateSegmentSpacing(false, 1f);
        }
    }

    private void UpdateSegmentSpacing(bool animateSmoothly, float lerpT)
    {
        for (int i = 0; i < activeSegments.Count; i++)
        {
            DragonSegment segment = activeSegments[i];

            float requiredDistanceBehindHead = spacingManager != null ? spacingManager.GetTargetDistanceForSegment(segment) : segmentSpacing * i;

            if (animateSmoothly && currentSpacings.ContainsKey(segment))
            {
                requiredDistanceBehindHead = Mathf.Lerp(currentSpacings[segment], requiredDistanceBehindHead, lerpT);
            }

            float targetDistanceInHistory = headTotalDistance - requiredDistanceBehindHead;

            for (int j = 0; j < positionHistory.Count - 1; j++)
            {
                PositionData newer = positionHistory[j];
                PositionData older = positionHistory[j + 1];

                if (targetDistanceInHistory <= newer.distanceTraveled && targetDistanceInHistory >= older.distanceTraveled)
                {
                    float range = newer.distanceTraveled - older.distanceTraveled;
                    float t = (newer.distanceTraveled - targetDistanceInHistory) / range;

                    segment.transform.position = Vector3.Lerp(newer.position, older.position, t);
                    // Rotation has been stripped out. Segment rotation is entirely driven by DragonBodySegmentRippleAnimator.
                    break;
                }
            }
        }
    }

    public void OnSegmentDestroyed(DragonSegment destroyedSegment)
    {
        totalBossPower -= destroyedSegment.powerContribution;

        Dictionary<DragonSegment, float> previousSpacings = new Dictionary<DragonSegment, float>(currentSpacings);
        currentSpacings.Clear();

        foreach (var segment in activeSegments)
        {
            if (segment != destroyedSegment)
            {
                float dist = spacingManager != null ? spacingManager.GetTargetDistanceForSegment(segment) : segment.SegmentIndex * segmentSpacing;

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

        if (activeSegments.Count == 0) return;

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
            bossBrain.TakeDamage(99999f, transform.position, ElementTypeOB7.Normal);
        }

        OnSegmentCountChanged?.Invoke(activeSegments.Count);

        isClosingGap = true;
        gapCloseTimer = 0f;
    }

    public void TriggerTotalDeath()
    {
        float longestDissolveDuration = 0f;

        foreach (var segment in activeSegments)
        {
            if (segment != null)
            {
                segment.TriggerTotalDeath();

                DissolveEffect dissolve = segment.GetComponentInChildren<DissolveEffect>();
                if (dissolve != null)
                {
                    float duration = dissolve.dissolveDuration;
                    if (duration > longestDissolveDuration) longestDissolveDuration = duration;
                }
            }
        }
        activeSegments.Clear();
        OnSegmentCountChanged?.Invoke(0);
        Destroy(gameObject, longestDissolveDuration + 0.1f);
    }
}
