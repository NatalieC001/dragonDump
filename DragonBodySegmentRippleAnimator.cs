using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Attaches to the visual Dragon Head object (alongside DragonHeadWobble).
/// It dynamically finds all body segments and visually forces them to follow the
/// physical snaking/wobbling motion of the piece in front of them, creating a customizable ripple effect.
/// This runs in LateUpdate, neatly overriding the rigid rotations set by the breadcrumb physics system.
/// </summary>
public class DragonBodySegmentRippleAnimator : MonoBehaviour
{
    [Header("Ripple Animation Settings")]

    [Tooltip("How quickly each segment turns to face the segment in front of it. Higher values mean a tighter, stiffer snake; lower values mean a looser, more delayed ripple.")]
    [Range(1f, 30f)]
    public float baseTurnSpeed = 12f;

    [Tooltip("How much the turn speed drops off as it goes down the tail. If > 0, the tail will swing looser and wider than the neck.")]
    [Range(0f, 1f)]
    public float tailLoosenessDropoff = 0.1f;

    [Tooltip("If true, the ripple effect pauses when the dragon stops moving forward.")]
    public bool pauseRippleWhenStationary = true;

    [Tooltip("Minimum movement speed before the ripple pauses (if pauseRippleWhenStationary is true).")]
    public float movementThreshold = 0.1f;

    // Ordered list of segments: index 0 is the head (this object), index 1 is the first body part, etc.
    private List<Transform> segmentTransforms = new List<Transform>();

    // Tracking root movement to pause animation if stationary
    private Transform parentRoot;
    private Vector3 lastRootPosition;

    private void Start()
    {
        parentRoot = transform.parent;
        if (parentRoot != null)
        {
            lastRootPosition = parentRoot.position;
        }

        // Give the SegmentedDragonManager time to instantiate the prefabs
        StartCoroutine(FindSegmentsRoutine());
    }

    private IEnumerator FindSegmentsRoutine()
    {
        yield return new WaitForSeconds(2f);

        segmentTransforms.Clear();

        if (parentRoot != null)
        {
            // We search for children named "DragonSegment_X" and add them in order
            int index = 0;
            while (true)
            {
                Transform seg = parentRoot.Find($"DragonSegment_{index}");
                if (seg != null)
                {
                    // If the segment has a Mesh_Visual, we prefer tracking/rotating that directly
                    // so we don't mess with root colliders, but falling back to the segment root is fine.
                    Transform visual = seg.Find("Mesh_Visual");
                    segmentTransforms.Add(visual != null ? visual : seg);
                    index++;
                }
                else
                {
                    break;
                }
            }

            Debug.Log($"[DragonBodySegmentRippleAnimator] Found {segmentTransforms.Count} segments to animate with the ripple effect.");
        }
    }

    private void LateUpdate()
    {
        if (segmentTransforms.Count < 2) return;

        // Check if the dragon is actually moving to avoid jitter when stationary
        if (pauseRippleWhenStationary && parentRoot != null)
        {
            float speed = Vector3.Distance(parentRoot.position, lastRootPosition) / Time.deltaTime;
            lastRootPosition = parentRoot.position;

            if (speed < movementThreshold)
            {
                return; // Skip rippling if stationary
            }
        }

        // Loop through all segments behind the head
        for (int i = 1; i < segmentTransforms.Count; i++)
        {
            Transform currentSeg = segmentTransforms[i];
            Transform segmentAhead = segmentTransforms[i - 1];

            if (currentSeg != null && segmentAhead != null)
            {
                // Find the direction pointing from this segment to the physical position of the segment ahead
                Vector3 directionToAhead = (segmentAhead.position - currentSeg.position).normalized;

                if (directionToAhead != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(directionToAhead);

                    // Calculate dynamic snappiness based on segment index (tail drops off and swings wider)
                    float currentTurnSpeed = baseTurnSpeed * Mathf.Pow(1f - tailLoosenessDropoff, i);

                    // Ensure it doesn't drop to 0
                    currentTurnSpeed = Mathf.Max(currentTurnSpeed, 1f);

                    // Smoothly rotate the segment to look at the leader, completing the snake cascade
                    currentSeg.rotation = Quaternion.Slerp(currentSeg.rotation, targetRotation, Time.deltaTime * currentTurnSpeed);
                }
            }
        }
    }
}
