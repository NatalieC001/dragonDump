using System.Collections;
using UnityEngine;
using Dreamteck.Splines;
using System.Collections.Generic;

/// <summary>
/// Solely responsible for tracking the breadcrumb history of the boss's root object
/// and physically dragging the pieces along that track.
/// </summary>
public class DragonBreadcrumbManager : MonoBehaviour
{
    private struct PositionData
    {
        public Vector3 position;
        public Quaternion rotation;
        public float distanceTraveled;
    }

    private List<PositionData> positionHistory = new List<PositionData>();
    private float headTotalDistance = 0f;
    private SplineComputer bossSpline;

    // Dynamically tracks the visual head so breadcrumbs record the wobble/sway
    private Transform trackedHead;

    private void Start()
    {
        StartCoroutine(FindHeadRoutine());
    }

    private System.Collections.IEnumerator FindHeadRoutine()
    {
        // Wait 2 seconds to guarantee the SegmentedDragonManager has instantiated the pieces
        yield return new WaitForSeconds(2f);

        Transform foundHead = transform.Find("DragonSegment_0");
        if (foundHead != null)
        {
            trackedHead = foundHead;
            Debug.Log($"[DragonBreadcrumbManager] Successfully locked tracking onto {trackedHead.name}");
        }
        else
        {
            Debug.LogWarning("[DragonBreadcrumbManager] Could not find DragonSegment_0 after 2 seconds!");
        }
    }

    public void InitializeMovement(SplineComputer track, float totalExpectedLength)
    {
        bossSpline = track;
        positionHistory.Clear();
        headTotalDistance = 0f;

        // Populate history backward based on total length
        int samples = Mathf.CeilToInt((totalExpectedLength * 2f) / 0.1f) + 1;

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

    public void SwitchToNewSpline(SplineComputer newTrack)
    {
        bossSpline = newTrack;
    }

    public void UpdateBreadcrumbs(float maxNeededHistoryDistance)
    {
        Vector3 currentHeadPos = trackedHead != null ? trackedHead.position : transform.position;
        Quaternion currentHeadRot = trackedHead != null ? trackedHead.rotation : transform.rotation;

        if (positionHistory.Count == 0) return;
        PositionData lastData = positionHistory[0];

        float distMovedSinceLastFrame = Vector3.Distance(currentHeadPos, lastData.position);

        if (distMovedSinceLastFrame > 0.05f)
        {
            headTotalDistance += distMovedSinceLastFrame;

            positionHistory.Insert(0, new PositionData
            {
                position = currentHeadPos,
                rotation = currentHeadRot,
                distanceTraveled = headTotalDistance
            });

            if (headTotalDistance - positionHistory[positionHistory.Count - 1].distanceTraveled > maxNeededHistoryDistance)
            {
                positionHistory.RemoveAt(positionHistory.Count - 1);
            }
        }
    }

    public void PlaceSegment(DragonSegment segment, float requiredDistanceBehindHead)
    {
        if (segment == null || positionHistory.Count == 0) return;

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
                segment.transform.rotation = Quaternion.Slerp(newer.rotation, older.rotation, t);
                break;
            }
        }
    }
}
