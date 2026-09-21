using UnityEngine;

/// <summary>
/// Defines the physical space a path occupies. 
/// Placed on the root of path prefabs so the BossPathManager can correctly categorize them.
/// </summary>
public class PathTypeTag : MonoBehaviour
{
    public enum PathType
    {
        Airborne,
        Terrestrial
    }

    [Tooltip("Is this an airborne route for flying creatures, or a terrestrial route for NavMesh walkers?")]
    public PathType pathType = PathType.Airborne;

    [Tooltip("Is this path used for observing the arena, or escaping?")]
    public bool isEscapeRoute = false;
}
