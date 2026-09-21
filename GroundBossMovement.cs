using UnityEngine;

/// <summary>
/// Inherits from BaseBossMovement.
/// Used for pure NavMesh walkers (e.g., Humanoid Boss).
/// Exclusively requests Terrestrial paths from the BossPathManager.
/// </summary>
public class GroundBossMovement : BaseBossMovement
{
    private GameObject currentActivePath;

    protected override void TickMovement()
    {
        // Example integration with NavMesh logic
        bool isEscaping = false;

        if (isEscaping)
        {
            ExecuteGroundEscape();
        }
        else
        {
            ExecuteGroundCombat();
        }
    }

    public override void ForceImmediateEvasion()
    {
        currentActivePath = FindNearestEscapeRoute(PathTypeTag.PathType.Terrestrial);
        if (currentActivePath != null)
        {
            Debug.Log($"[{gameObject.name}] Ground movement immediately pathfinding to escape route: {currentActivePath.name}");
        }
    }

    private void ExecuteGroundEscape()
    {
        if (currentActivePath == null)
        {
            currentActivePath = FindNearestEscapeRoute(PathTypeTag.PathType.Terrestrial);
        }

        if (currentActivePath != null)
        {
            // Use NavMeshAgent to pathfind to the start of the terrestrial escape spline, then ride it
        }
        else
        {
            // Pure NavMesh fleeing
        }
    }

    private void ExecuteGroundCombat()
    {
        // Standard NavMesh combat movement towards player or crystal
    }
}
