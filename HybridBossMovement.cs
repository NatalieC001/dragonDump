using UnityEngine;

/// <summary>
/// Inherits from BaseBossMovement.
/// For bosses that can both walk and fly. It internally tracks its state and requests
/// the appropriate path type (Airborne when flying, Terrestrial when walking).
/// </summary>
public class HybridBossMovement : BaseBossMovement
{
    public enum HybridState
    {
        Walking,
        Flying
    }

    public HybridState currentHybridState = HybridState.Walking;
    private GameObject currentActivePath;

    protected override void TickMovement()
    {
        if (currentHybridState == HybridState.Walking)
        {
            TickWalkingMovement();
        }
        else
        {
            TickFlyingMovement();
        }
    }

    public override void ForceImmediateEvasion()
    {
        if (currentHybridState == HybridState.Walking)
        {
            currentActivePath = FindNearestEscapeRoute(PathTypeTag.PathType.Terrestrial);
        }
        else
        {
            currentActivePath = FindNearestEscapeRoute(PathTypeTag.PathType.Airborne);
        }

        if (currentActivePath != null)
        {
            Debug.Log($"[{gameObject.name}] Hybrid movement immediately jumping to {currentHybridState} escape route: {currentActivePath.name}");
        }
    }

    private void TickWalkingMovement()
    {
        // Example: If escaping while on the ground, ask for Terrestrial routes
        bool wantsToEscape = false;
        
        if (wantsToEscape)
        {
            if (currentActivePath == null)
            {
                currentActivePath = FindNearestEscapeRoute(PathTypeTag.PathType.Terrestrial);
            }
            // NavMesh to the path...
        }
    }

    private void TickFlyingMovement()
    {
        // Example: If escaping while in the air, ask for Airborne routes
        bool wantsToEscape = false;

        if (wantsToEscape)
        {
            if (currentActivePath == null)
            {
                currentActivePath = FindNearestEscapeRoute(PathTypeTag.PathType.Airborne);
            }
            // SplineFollower logic...
        }
    }

    public void TransitionToFlight()
    {
        currentHybridState = HybridState.Flying;
        currentActivePath = null; // Clear terrestrial path
    }

    public void TransitionToGround()
    {
        currentHybridState = HybridState.Walking;
        currentActivePath = null; // Clear airborne path
    }
}
