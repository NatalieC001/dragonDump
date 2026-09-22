using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Attaches to the visual Dragon Head object (alongside DragonHeadWobble).
/// A completely self-contained, physics-free script to drive the body segments.
/// It dynamically finds all segments and makes each one physically follow and look at
/// the segment immediately in front of it, creating a perfect snake chain.
/// NO breadcrumbs needed.
/// </summary>
public class DragonBodySegmentRippleAnimator : MonoBehaviour
{
    [Header("Rotation Cascade Settings")]
    [Tooltip("How quickly each segment turns to face the one in front of it. Higher = stiffer snake.")]
    [Range(1f, 30f)]
    public float turnSpeed = 15f;

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
            // Because this script is on DragonSegment_0 (a child of the root),
            // it must look at its parent to find its sibling segments.
            Transform parent = transform.parent;
            if (parent == null) break;

            Transform seg = parent.Find($"DragonSegment_{index}");
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

        Debug.Log($"[DragonBodySegmentRippleAnimator] Locked onto {segmentTransforms.Count} segments. Ready to cascade rotations!");
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
                // Rotate to physically look at the leader segment
                Vector3 directionToAhead = (segmentAhead.position - currentSeg.position).normalized;

                if (directionToAhead != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(directionToAhead);

                    // ONLY affects rotation. Leaves position/spacing completely alone.
                    currentSeg.rotation = Quaternion.Slerp(currentSeg.rotation, targetRotation, Time.deltaTime * turnSpeed);
                }
            }
        }
    }
}
