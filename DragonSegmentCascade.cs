using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Forces each body segment to physically look at the segment immediately in front of it.
/// Because DragonHeadWobble swings the head left and right, this script causes that
/// swing to beautifully cascade down the entire body like a real snake or fish.
///
/// Attach this script to the Boss Root object.
/// </summary>
public class DragonSegmentCascade : MonoBehaviour
{
    [Header("Cascade Settings")]
    [Tooltip("How quickly each segment turns to face the one in front of it. Higher = stiffer snake.")]
    [Range(1f, 30f)]
    public float baseTurnSpeed = 12f;

    [Tooltip("If > 0, segments further down the tail will swing looser and wider than the neck.")]
    [Range(0f, 1f)]
    public float tailLoosenessDropoff = 0.1f;

    private List<Transform> segmentTransforms = new List<Transform>();

    private void Start()
    {
        // Wait briefly for SegmentedDragonManager to finish spawning the pieces
        StartCoroutine(FindSegmentsRoutine());
    }

    private IEnumerator FindSegmentsRoutine()
    {
        yield return new WaitForSeconds(2f);

        segmentTransforms.Clear();

        // Search for children named "DragonSegment_X" and add them in order
        int index = 0;
        while (true)
        {
            Transform seg = transform.Find($"DragonSegment_{index}");
            if (seg != null)
            {
                // We rotate the actual visual mesh so we don't interfere with root colliders/physics
                Transform visual = seg.Find("Mesh_Visual");
                segmentTransforms.Add(visual != null ? visual : seg);
                index++;
            }
            else
            {
                break;
            }
        }

        Debug.Log($"[DragonSegmentCascade] Locked onto {segmentTransforms.Count} segments. Ready to ripple!");
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
                // Find the direction pointing to the physical position of the segment ahead
                Vector3 directionToAhead = (segmentAhead.position - currentSeg.position).normalized;

                if (directionToAhead != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(directionToAhead);

                    // Calculate dynamic snappiness based on segment index (tail drops off and swings wider)
                    float currentTurnSpeed = baseTurnSpeed * Mathf.Pow(1f - tailLoosenessDropoff, i);
                    currentTurnSpeed = Mathf.Max(currentTurnSpeed, 1f); // Don't let it freeze

                    // Smoothly rotate the segment to look at the leader, completing the snake cascade
                    currentSeg.rotation = Quaternion.Slerp(currentSeg.rotation, targetRotation, Time.deltaTime * currentTurnSpeed);
                }
            }
        }
    }
}
