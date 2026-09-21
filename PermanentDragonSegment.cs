using UnityEngine;

/// <summary>
/// A specialized version of DragonSegment specifically for the Head, Legs, and Tail.
/// It overrides standard death logic to ensure it can NEVER be destroyed mid-fight,
/// but still takes damage and relays it to the boss. It only dies when TriggerTotalDeath is called.
/// </summary>
// NEW: Changes 2 of 2:
// 1. Removed reflection hack from Start().
// 2. Updated TriggerTotalDeath() to pass Destroy callback to DissolveEffect.
public class PermanentDragonSegment : DragonSegment
{
    private void Start()
    {
        // Force this to be indestructible just in case the manager didn't catch it
        isDestructiblePart = false;
        // NEW: 1. Removed complex reflection code that used to disable old dissolve logic here.
    }

    protected override void Die()
    {
        // Do absolutely nothing. Permanent segments cannot be destroyed mid-fight!
        Debug.Log($"<color=yellow>[PermanentDragonSegment] Segment {SegmentIndex} tried to die, but it is permanent!</color>");
    }

    public override void TriggerTotalDeath()
    {
        // Now it is allowed to die because the Boss's health hit 0!
        if (segmentCollider != null)
        {
            segmentCollider.enabled = false;
        }

        DissolveEffect dissolve = GetComponentInChildren<DissolveEffect>();
        if (dissolve != null)
        {
            // Trigger visual effect, then destroy the object
            // NEW: 2. Passing lambda callback to destroy object after visuals finish.
            dissolve.TriggerDissolve(() =>
            {
                // Safe null-check: the object may have already been destroyed
                // (e.g. scene unload or another death path) before this callback fires.
                if (this != null)
                {
                    Destroy(gameObject);
                }
            });
        }
        else
        {
            Destroy(gameObject);
        }
    }
}



//using UnityEngine;

///// <summary>
///// A specialized version of DragonSegment specifically for the Head, Legs, and Tail.
///// It overrides standard death logic to ensure it can NEVER be destroyed mid-fight,
///// but still takes damage and relays it to the boss. It only dies when TriggerTotalDeath is called.
///// </summary>
//public class PermanentDragonSegment : DragonSegment
//{
//    private void Start()
//    {
//        // Force this to be indestructible just in case the manager didn't catch it
//        isDestructiblePart = false;

//        // Ensure DissolveEffect waits for TriggerTotalDeath
//        DissolveEffect effect = GetComponentInChildren<DissolveEffect>();
//        if (effect != null)
//        {
//            // Safely toggle the private field so this piece doesn't accidentally bypass health logic and visually explode when shot
//            System.Reflection.FieldInfo field = effect.GetType().GetField("dissolveImmediatelyOnHit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
//            if (field != null)
//            {
//                field.SetValue(effect, false);
//            }
//        }
//    }

//    protected override void Die()
//    {
//        // Do absolutely nothing. Permanent segments cannot be destroyed mid-fight!
//        Debug.Log($"<color=yellow>[PermanentDragonSegment] Segment {SegmentIndex} tried to die, but it is permanent!</color>");
//    }

//    public override void TriggerTotalDeath()
//    {
//        // Now it is allowed to die because the Boss's health hit 0!
//        if (segmentCollider != null)
//        {
//            segmentCollider.enabled = false;
//        }

//        DissolveEffect dissolve = GetComponentInChildren<DissolveEffect>();
//        if (dissolve != null)
//        {
//            // Trigger visual effect, then destroy the object
//            dissolve.TriggerDissolve(() =>
//            {
//                Destroy(gameObject);
//            });
//        }
//        else
//        {
//            Destroy(gameObject);
//        }
//    }
//}
