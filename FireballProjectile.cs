using UnityEngine;

/// <summary>
/// Simple fireball projectile that deals damage on impact and plays optional VFX.
/// Expects a Rigidbody on the prefab. Destroys itself after lifetime or on hit.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FireballProjectile : MonoBehaviour
{
    [Tooltip("Damage applied to player or enemy on impact.")]
    public float damage = 25f;

    [Tooltip("Optional lifetime before self-destruct.")]
    public float lifeSeconds = 8f;

    [Tooltip("Layer mask for what the fireball should hit (Player layer typically).")]
    public LayerMask hitMask = ~0;

    private GameObject owner;

    private void Start()
    {
        Destroy(gameObject, lifeSeconds);
    }

    public void SetOwner(GameObject o)
    {
        owner = o;
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Ignore collisions with owner (use transform.IsChildOf)
        if (owner != null && collision.transform.IsChildOf(owner.transform)) return;

        // Try to damage player (best-effort)
        var playerHealth = collision.gameObject.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
        {
            // Call via SendMessage to remain compatible with any PlayerHealth signature variants
            playerHealth.gameObject.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
        }
        else
        {
            // fallback: send message to the collided object
            collision.gameObject.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
        }

        // Optional: spawn hit VFX, play sound...

        Destroy(gameObject);
    }
}