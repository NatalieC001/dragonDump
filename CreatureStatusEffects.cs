using UnityEngine;
using Dreamteck.Splines;
using System.Collections;

/// <summary>
/// Handles elemental status effects (Slow, Brittle, Stasis) applied by special arrows.
/// Works seamlessly on both Minions and Bosses by modifying their SplineFollower speed
/// and exposing CurrentSpeedMultiplier for freestyle movement scripts.
/// </summary>
public class CreatureStatusEffects : MonoBehaviour
{
    private SplineFollower follower;
    private float originalSpeed;

    // State Tracking
    public bool IsBrittle { get; private set; } = false;
    public float CurrentSpeedMultiplier { get; private set; } = 1f;

    private Coroutine activeSpeedModifier;

    private void Awake()
    {
        follower = GetComponent<SplineFollower>();
    }

    private void Start()
    {
        if (follower != null)
        {
            originalSpeed = follower.followSpeed;
        }
    }

    /// <summary>
    /// Evaluates the incoming arrow type and applies temporary status effects.
    /// </summary>
    public void ApplyElementalEffect(ElementTypeOB7 arrowType)
    {
        // Always reset to base speed before applying a new effect to prevent infinite slow stacking
        if (activeSpeedModifier != null)
        {
            StopCoroutine(activeSpeedModifier);
            ResetEffects();
        }

        switch (arrowType)
        {
            case ElementTypeOB7.Ice:
                Debug.Log($"<color=cyan>[StatusEffect] {gameObject.name} is Frozen! Slowed by 50% and Brittle.</color>");
                activeSpeedModifier = StartCoroutine(TemporarySpeedModifier(0.5f, 3f, false));
                IsBrittle = true;
                // Note: Visual Ice/Frost particle effects would be triggered here
                break;

            case ElementTypeOB7.Sticky:
                Debug.Log($"<color=yellow>[StatusEffect] {gameObject.name} is Stuck! Slowed by 90%.</color>");
                activeSpeedModifier = StartCoroutine(TemporarySpeedModifier(0.1f, 5f, false));
                // Note: Visual Goo/Slime particle effects would be triggered here
                break;

            case ElementTypeOB7.Stasis:
                Debug.Log($"<color=magenta>[StatusEffect] {gameObject.name} is frozen in Time! Speed is 0.</color>");
                activeSpeedModifier = StartCoroutine(TemporarySpeedModifier(0f, 4f, true));
                // Note: Visual Stasis particle effects would be triggered here
                break;
        }
    }

    private IEnumerator TemporarySpeedModifier(float speedMultiplier, float duration, bool isStasis)
    {
        CurrentSpeedMultiplier = speedMultiplier;
        if (follower != null)
        {
            follower.followSpeed = originalSpeed * speedMultiplier;
        }

        yield return new WaitForSeconds(duration);

        // Effect ends
        ResetEffects();

        if (isStasis)
        {
            PixelCrushers.MessageSystem.SendMessage(this, "Brain", "StatusEnded", string.Empty);
        }
    }

    private void ResetEffects()
    {
        CurrentSpeedMultiplier = 1f;
        if (follower != null)
        {
            follower.followSpeed = originalSpeed;
        }
        IsBrittle = false;
    }
}





//using UnityEngine;
//using Dreamteck.Splines;
//using System.Collections;

///// <summary>
///// Handles elemental status effects (Slow, Brittle, Stasis) applied by special arrows.
///// Works seamlessly on both Minions and Bosses by modifying their SplineFollower speed
///// and exposing CurrentSpeedMultiplier for freestyle movement scripts.
///// </summary>
//public class CreatureStatusEffects : MonoBehaviour
//{
//    private SplineFollower follower;
//    private float originalSpeed;

//    // State Tracking
//    public bool IsBrittle { get; private set; } = false;
//    public float CurrentSpeedMultiplier { get; private set; } = 1f;

//    private Coroutine activeSpeedModifier;
//    private BossEventBus eventBus;

//    private void Awake()
//    {
//        follower = GetComponent<SplineFollower>();
//        eventBus = FindFirstObjectByType<BossEventBus>();
//    }

//    private void Start()
//    {
//        if (follower != null)
//        {
//            originalSpeed = follower.followSpeed;
//        }
//    }

//    /// <summary>
//    /// Evaluates the incoming arrow type and applies temporary status effects.
//    /// </summary>
//    public void ApplyElementalEffect(ElementTypeOB7 arrowType)
//    {
//        // Always reset to base speed before applying a new effect to prevent infinite slow stacking
//        if (activeSpeedModifier != null)
//        {
//            StopCoroutine(activeSpeedModifier);
//            ResetEffects();
//        }

//        switch (arrowType)
//        {
//            case ElementTypeOB7.Ice:
//                Debug.Log($"<color=cyan>[StatusEffect] {gameObject.name} is Frozen! Slowed by 50% and Brittle.</color>");
//                activeSpeedModifier = StartCoroutine(TemporarySpeedModifier(0.5f, 3f, false));
//                IsBrittle = true;
//                // Note: Visual Ice/Frost particle effects would be triggered here
//                break;

//            case ElementTypeOB7.Sticky:
//                Debug.Log($"<color=yellow>[StatusEffect] {gameObject.name} is Stuck! Slowed by 90%.</color>");
//                activeSpeedModifier = StartCoroutine(TemporarySpeedModifier(0.1f, 5f, false));
//                // Note: Visual Goo/Slime particle effects would be triggered here
//                break;

//            case ElementTypeOB7.Stasis:
//                Debug.Log($"<color=magenta>[StatusEffect] {gameObject.name} is frozen in Time! Speed is 0.</color>");
//                activeSpeedModifier = StartCoroutine(TemporarySpeedModifier(0f, 4f, true));
//                // Note: Visual Stasis particle effects would be triggered here
//                break;
//        }
//    }

//    private IEnumerator TemporarySpeedModifier(float speedMultiplier, float duration, bool isStasis)
//    {
//        CurrentSpeedMultiplier = speedMultiplier;
//        if (follower != null)
//        {
//            follower.followSpeed = originalSpeed * speedMultiplier;
//        }

//        yield return new WaitForSeconds(duration);

//        // Effect ends
//        ResetEffects();

//        if (isStasis && eventBus != null)
//        {
//            eventBus.TriggerBossStatusEnded();
//        }
//    }

//    private void ResetEffects()
//    {
//        CurrentSpeedMultiplier = 1f;
//        if (follower != null)
//        {
//            follower.followSpeed = originalSpeed;
//        }
//        IsBrittle = false;
//    }
//}



//using UnityEngine;
//using Dreamteck.Splines;
//using System.Collections;

///// <summary>
///// Handles elemental status effects (Slow, Brittle, Stasis) applied by special arrows.
///// Works seamlessly on both Minions and Bosses by modifying their SplineFollower speed.
///// </summary>
//[RequireComponent(typeof(SplineFollower))]
//public class CreatureStatusEffects : MonoBehaviour
//{
//    private SplineFollower follower;
//    private float originalSpeed;

//    // State Tracking
//    public bool IsBrittle { get; private set; } = false;

//    private Coroutine activeSpeedModifier;

//    private void Awake()
//    {
//        follower = GetComponent<SplineFollower>();
//    }

//    private void Start()
//    {
//        originalSpeed = follower.followSpeed;
//    }

//    public float CurrentSpeedMultiplier { get; private set; } = 1f;

//    /// <summary>
//    /// Evaluates the incoming arrow type and applies temporary status effects.
//    /// </summary>
//    public void ApplyElementalEffect(ElementTypeOB7 arrowType)
//    {
//        // Always reset to base speed before applying a new effect to prevent infinite slow stacking
//        if (activeSpeedModifier != null)
//        {
//            StopCoroutine(activeSpeedModifier);
//            ResetEffects();
//        }

//        switch (arrowType)
//        {
//            case ElementTypeOB7.Ice:
//                Debug.Log($"<color=cyan>[StatusEffect] {gameObject.name} is Frozen! Slowed by 50% and Brittle.</color>");
//                activeSpeedModifier = StartCoroutine(TemporarySpeedModifier(0.5f, 3f));
//                IsBrittle = true;
//                // Note: Visual Ice/Frost particle effects would be triggered here
//                break;

//            case ElementTypeOB7.Sticky:
//                Debug.Log($"<color=yellow>[StatusEffect] {gameObject.name} is Stuck! Slowed by 90%.</color>");
//                activeSpeedModifier = StartCoroutine(TemporarySpeedModifier(0.1f, 5f));
//                // Note: Visual Goo/Slime particle effects would be triggered here
//                break;

//            case ElementTypeOB7.Stasis:
//                Debug.Log($"<color=magenta>[StatusEffect] {gameObject.name} is frozen in Time! Speed is 0.</color>");
//                activeSpeedModifier = StartCoroutine(TemporarySpeedModifier(0f, 4f));
//                // Note: Visual Stasis particle effects would be triggered here
//                break;
//        }
//    }

//    private IEnumerator TemporarySpeedModifier(float speedMultiplier, float duration)
//    {
//        // Apply the slow
//        follower.followSpeed = originalSpeed * speedMultiplier;

//        yield return new WaitForSeconds(duration);

//        // Effect ends
//        ResetEffects();
//    }

//    private void ResetEffects()
//    {
//        follower.followSpeed = originalSpeed;
//        IsBrittle = false;
//    }
//}
