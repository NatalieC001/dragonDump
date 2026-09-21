using UnityEngine;

[RequireComponent(typeof(EnvironmentTag))]
public class ToppleController : MonoBehaviour, IArrowTarget
{
    public GameObject unbrokenMesh;
    public GameObject debrisPrefab;
    public float explosionRadius = 5f;
    public float explosionDamage = 50f;
    
    private EnvironmentTag envTag;
    private bool hasToppled = false;

    private void Awake()
    {
        envTag = GetComponent<EnvironmentTag>();
        if (envTag.type != EnvironmentTag.TagType.ToppleObject)
        {
            Debug.LogWarning($"[ToppleController] {gameObject.name} is not tagged as a ToppleObject!");
        }
    }

    public void OnArrowHit(float damage, Vector3 impactPoint, ElementTypeOB7 elementType)
    {
        // Heavy impacts or fire can trigger topple
        if (damage > 10f || elementType == ElementTypeOB7.Fire)
        {
            TriggerTopple();
        }
    }

    public void TriggerTopple()
    {
        if (hasToppled) return;
        hasToppled = true;

        if (unbrokenMesh != null) unbrokenMesh.SetActive(false);

        if (debrisPrefab != null)
        {
            Instantiate(debrisPrefab, transform.position, transform.rotation);
        }

        // Apply AOE damage
        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius);
        foreach (var hit in hits)
        {
            IArrowTarget target = hit.GetComponent<IArrowTarget>();
            if (target != null && (MonoBehaviour)target != this)
            {
                target.OnArrowHit(explosionDamage, hit.ClosestPoint(transform.position), ElementTypeOB7.Normal);
            }
        }

        // Remove from tag registry so AI knows it's gone
        if (envTag != null)
        {
            envTag.enabled = false; 
        }
    }
}
