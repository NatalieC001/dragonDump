using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Attaches to the visual Dragon Head object (alongside DragonHeadWobble).
/// Dynamically finds all body segments and forces them to physically look at the piece in front of them,
/// creating a beautiful cascading snake motion.
/// </summary>
public class DragonBodySegmentRippleAnimator : MonoBehaviour
{
    [Header("Ripple Animation Settings")]
    [Tooltip("How quickly each segment turns to face the segment in front of it. Higher = stiffer snake.")]
    [Range(1f, 30f)]
    public float baseTurnSpeed = 12f;

    [Tooltip("Turn speed dropoff for the tail. If > 0, the tail swings looser and wider than the neck.")]
    [Range(0f, 1f)]
    public float tailLoosenessDropoff = 0.1f;

    [Tooltip("If true, the ripple effect pauses when the dragon stops moving forward.")]
    public bool pauseRippleWhenStationary = true;

    [Tooltip("Minimum movement speed before the ripple pauses.")]
    public float movementThreshold = 0.1f;

    private List<Transform> segmentTransforms = new List<Transform>();
    private Transform parentRoot;
    private Vector3 lastRootPosition;

    private void Start()
    {
        parentRoot = transform.parent;
        if (parentRoot != null)
        {
            lastRootPosition = parentRoot.position;
        }
        StartCoroutine(FindSegmentsRoutine());
    }

    private IEnumerator FindSegmentsRoutine()
    {
        yield return new WaitForSeconds(2f);
        segmentTransforms.Clear();

        if (parentRoot != null)
        {
            int index = 0;
            while (true)
            {
                Transform seg = parentRoot.Find($"DragonSegment_{index}");
                if (seg != null)
                {
                    Transform visual = seg.Find("Mesh_Visual");
                    segmentTransforms.Add(visual != null ? visual : seg);
                    index++;
                }
                else
                {
                    break;
                }
            }
            Debug.Log($"[DragonBodySegmentRippleAnimator] Found {segmentTransforms.Count} segments to animate.");
        }
    }

    private void LateUpdate()
    {
        if (segmentTransforms.Count < 2) return;

        if (pauseRippleWhenStationary && parentRoot != null)
        {
            float speed = Vector3.Distance(parentRoot.position, lastRootPosition) / Time.deltaTime;
            lastRootPosition = parentRoot.position;
            if (speed < movementThreshold) return;
        }

        for (int i = 1; i < segmentTransforms.Count; i++)
        {
            Transform currentSeg = segmentTransforms[i];
            Transform segmentAhead = segmentTransforms[i - 1];

            if (currentSeg != null && segmentAhead != null)
            {
                Vector3 directionToAhead = (segmentAhead.position - currentSeg.position).normalized;
                if (directionToAhead != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(directionToAhead);
                    float currentTurnSpeed = baseTurnSpeed * Mathf.Pow(1f - tailLoosenessDropoff, i);
                    currentTurnSpeed = Mathf.Max(currentTurnSpeed, 1f);
                    currentSeg.rotation = Quaternion.Slerp(currentSeg.rotation, targetRotation, Time.deltaTime * currentTurnSpeed);
                }
            }
        }
    }
}
