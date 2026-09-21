using UnityEngine;

public class RegeneratorController : MonoBehaviour
{
    [Header("Regeneration Limits")]
    public float maxRegenTime = 15f; // Hard cap
    public float secondsPerRegrow = 1.5f;

    private float currentRegenTimer = 0f;
    private float segmentRegenTimer = 0f;
    private bool isRegenerating = false;

    private AirborneBossMovement movementManager;
    private MinionRequestBroker requestBroker;
    private SegmentedDragonManager segmentManager;
    private HealthCrystal activeCrystal;

    private void Awake()
    {
        movementManager = GetComponent<AirborneBossMovement>();
        segmentManager = GetComponent<SegmentedDragonManager>();
        requestBroker = FindFirstObjectByType<MinionRequestBroker>();
    }

    public void BeginRegeneration(HealthCrystal targetCrystal)
    {
        if (targetCrystal == null || targetCrystal.IsDestroyed) return;

        isRegenerating = true;
        currentRegenTimer = 0f;
        segmentRegenTimer = 0f;
        activeCrystal = targetCrystal;

        // Refill minion reserve immediately
        if (requestBroker != null)
        {
            requestBroker.RefillReserve();
        }

        Debug.Log("[RegeneratorController] Dragon has begun regeneration loop.");
    }

    private void Update()
    {
        if (!isRegenerating) return;

        if (activeCrystal == null || activeCrystal.IsDestroyed)
        {
            FinishRegeneration();
            return;
        }

        currentRegenTimer += Time.deltaTime;
        segmentRegenTimer += Time.deltaTime;

        if (segmentManager != null)
        {
            if (segmentManager.IsMissingSegments())
            {
                if (segmentRegenTimer >= secondsPerRegrow)
                {
                    segmentManager.RegrowOneSegment();
                    segmentRegenTimer = 0f;
                }
            }
            else
            {
                // Reset timer when full so we don't instantly regrow a lost segment
                segmentRegenTimer = 0f;
            }
        }

        if (currentRegenTimer >= maxRegenTime)
        {
            FinishRegeneration();
        }
    }

    private void FinishRegeneration()
    {
        isRegenerating = false;
        currentRegenTimer = 0f;
        segmentRegenTimer = 0f;
        activeCrystal = null;

        Debug.Log("[RegeneratorController] Regeneration finished. Forcing Dragon off crystal.");

        if (movementManager != null)
        {
            movementManager.RequestFreestyleIntent(AirborneBossMovement.FreestyleIntent.Withdraw, transform.position + Vector3.up * 20f);
        }

        PixelCrushers.MessageSystem.SendMessage(this, "Brain", "RechargeFull", string.Empty);
    }
}
