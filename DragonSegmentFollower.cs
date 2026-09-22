using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// A completely self-contained, physics-free script to drive the body segments.
/// It dynamically finds all segments and makes each one physically follow and look at
/// the segment immediately in front of it, creating a perfect snake chain.
/// NO breadcrumbs needed.
/// </summary>
public class DragonSegmentFollower : MonoBehaviour
{
    [Header("Chain Settings")]
    [Tooltip("The physical distance maintained between each segment.")]
    public float segmentSpacing = 2.0f;

    [Tooltip("How quickly each segment turns to face the one in front of it. Higher = stiffer snake.")]
    [Range(1f, 30f)]
    public float turnSpeed = 15f;

    [Tooltip("How quickly each segment moves to catch up to its required distance. Higher = tighter chain.")]
    [Range(1f, 50f)]
    public float followSpeed = 20f;

    private List<Transform> segmentTransforms = new List<Transform>();

    private void Start()
    {
        // Wait briefly for the spawner to finish instantiating the pieces
        StartCoroutine(FindSegmentsRoutine());
    }

    private IEnumerator FindSegmentsRoutine()
    {
        yield return new WaitForSeconds(2f);

        segmentTransforms.Clear();

        int index = 0;
        while (true)
        {
            Transform seg = transform.Find($"DragonSegment_{index}");
            if (seg != null)
            {
                segmentTransforms.Add(seg);
                index++;
            }
            else
            {
                break;
            }
        }

        Debug.Log($"[DragonSegmentFollower] Locked onto {segmentTransforms.Count} segments. Ready to slither!");
    }

    private void LateUpdate()
    {
        if (segmentTransforms.Count < 2) return;

        // Loop through all segments behind the head
        for (int i = 1; i < segmentTransforms.Count; i++)
        {
            Transform currentSeg = segmentTransforms[i];
            Transform segmentAhead = segmentTransforms[i - 1];

            if (currentSeg != null && segmentAhead != null)
            {
                // 1. Calculate the target position (exactly 'segmentSpacing' behind the leader)
                Vector3 directionToAhead = (segmentAhead.position - currentSeg.position).normalized;

                if (directionToAhead != Vector3.zero)
                {
                    // The ideal spot this segment wants to be
                    Vector3 targetPosition = segmentAhead.position - (directionToAhead * segmentSpacing);

                    // Smoothly drag the segment to that spot
                    currentSeg.position = Vector3.Lerp(currentSeg.position, targetPosition, Time.deltaTime * followSpeed);

                    // 2. Rotate to look at the leader
                    Quaternion targetRotation = Quaternion.LookRotation(directionToAhead);
                    currentSeg.rotation = Quaternion.Slerp(currentSeg.rotation, targetRotation, Time.deltaTime * turnSpeed);
                }
            }
        }
    }
}
