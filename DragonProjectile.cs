using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DragonProjectile : MonoBehaviour
{
    [Tooltip("Seconds before the projectile auto-despawns.")]
    public float lifetime = 6f;

    [Tooltip("Damage applied to a hit target.")]
    public float damage = 10f;

    private Rigidbody rb;
    private float spawnTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        spawnTime = Time.time;
    }

    /// <summary>
    /// Launch the projectile in direction (normalized) with speed (units/s) and assign damage.
    /// This sets velocity directly and wakes the rigidbody to avoid pooled-sleep issues.
    /// </summary>
    public void Launch(Vector3 direction, float speed, float damageValue = 10f)
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
        }

        rb.isKinematic = false;
        rb.WakeUp();

        // Use direct velocity for deterministic flight on spawn
        rb.linearVelocity = direction.normalized * speed;
        spawnTime = Time.time;

        damage = damageValue;
    }

    private void Update()
    {
        if (Time.time - spawnTime >= lifetime)
        {
            Destroy(gameObject);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Attempt to notify an IArrowTarget on the object hit (or its parents)
        Vector3 impactPoint = collision.contacts.Length > 0 ? collision.contacts[0].point : collision.transform.position;

        var target = collision.collider.GetComponentInParent<IArrowTarget>();
        if (target != null)
        {
            try
            {
                target.OnArrowHit(damage, impactPoint, ElementTypeOB7.Normal);
            }
            catch
            {
                // Fallback to SendMessage if interface doesn't match
                collision.collider.SendMessageUpwards("OnArrowHit", new object[] { damage, impactPoint, ElementTypeOB7.Normal }, SendMessageOptions.DontRequireReceiver);
                collision.collider.SendMessageUpwards("TakeDamage", new object[] { damage, impactPoint, ElementTypeOB7.Normal }, SendMessageOptions.DontRequireReceiver);
            }
        }
        else
        {
            // Fallback: attempt common method names
            collision.collider.SendMessageUpwards("OnArrowHit", new object[] { damage, impactPoint, ElementTypeOB7.Normal }, SendMessageOptions.DontRequireReceiver);
            collision.collider.SendMessageUpwards("TakeDamage", new object[] { damage, impactPoint, ElementTypeOB7.Normal }, SendMessageOptions.DontRequireReceiver);
        }

        // Default behaviour: destroy projectile on first impact
        Destroy(gameObject);
    }
}