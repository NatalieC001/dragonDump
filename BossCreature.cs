using UnityEngine;

/// <summary>
/// The core Health and Phase 'Brain' for an advanced Boss creature.
/// It monitors health and battle state, and commands the BaseBossMovement
/// to make intelligent evasion choices when threatened.
/// </summary>
[RequireComponent(typeof(BaseBossMovement))]
public class BossCreature : MonoBehaviour
{
    [System.Serializable]
    public struct ElementalModifier
    {
        public ElementTypeOB7 arrowType;
        [Tooltip("1.0 = normal damage. 2.0 = double damage (weakness). 0.0 = immune. -1.0 = heals the boss!")]
        public float damageMultiplier;
    }

    [Header("Boss Stats")]
    public float maxHealth = 500f;
    private float currentHealth;

    [Header("Elemental Resistances")]
    [Tooltip("If an arrow type isn't listed here, it deals standard 1.0x damage.")]
    public ElementalModifier[] elementalModifiers;

    [Tooltip("The boss will attempt to jump to an escape route if taking rapid damage.")]
    public float evasionDamageThreshold = 50f;

    public enum BossPhase
    {
        Orchestrator, // Watching minions, charging on Crystal
        Engaged,      // Actively fighting the player
        Exhausted,    // Fleeing through environment to recharge
        Recharging    // Back on Observation spline, regaining stamina
    }

    [Header("Battle State")]
    public BossPhase currentPhase = BossPhase.Orchestrator;

    [Header("Energy System")]
    [Tooltip("How much stamina the boss has for attacking before it must retreat.")]
    public float maxStamina = 100f;
    [Tooltip("How fast stamina drains while attacking.")]
    public float staminaDrainRate = 10f;
    [Tooltip("How fast stamina recovers while on the observation spline.")]
    public float staminaRechargeRate = 15f;
    private float currentStamina;

    [Header("Anatomy Tracking")]
    [Tooltip("Dynamically found on Awake. Used as the origin point for breath attacks or projectiles.")]
    public Transform mouthTransform { get; private set; }

    private BaseBossMovement movementSystem;
    private CreatureStatusEffects statusEffects;
    private float recentDamageAccumulator = 0f;
    private float damageDecayTimer = 0f;

    // Injected by WaveSpawner to explicitly report Boss death progression
    private WaveSpawner waveSpawner;

    public void Initialize(WaveSpawner spawner)
    {
        waveSpawner = spawner;
    }

    private void Awake()
    {
        movementSystem = GetComponent<BaseBossMovement>();
        statusEffects = GetComponent<CreatureStatusEffects>();
        currentHealth = maxHealth;
        currentStamina = maxStamina;

      
    }

    private void Start()
    {
        // Boss anatomy spawns cleanly at root. It doesn't need to be forced onto a track immediately!
        SegmentedDragonManager dragonBody = GetComponent<SegmentedDragonManager>();
        if (dragonBody != null)
        {
            // Try to get the SplineComputer already assigned to this root's SplineFollower.
            // AirborneBossMovement.Start() will assign it in the same frame or next frame;
            // passing null here is safe — SegmentedDragonManager handles null gracefully by
            // placing all segments at the root spawn position until the spline is provided via SwitchToNewSpline().
            Dreamteck.Splines.SplineFollower rootFollower = GetComponent<Dreamteck.Splines.SplineFollower>();
            Dreamteck.Splines.SplineComputer initialSpline = rootFollower != null ? rootFollower.spline : null;

            Debug.Log($"[BossCreature] Instructing SegmentedDragonManager to spawn anatomy. Initial spline: {(initialSpline != null ? initialSpline.name : "none — segments will cluster at spawn position until first path is assigned")}");
            dragonBody.InitializeDragon(initialSpline);
        }

        ChangePhase(BossPhase.Orchestrator);

        FindMouthTransform();
    }

    /// <summary>
    /// Dynamically searches all children (even deeply nested ones) for an object exactly named 'MouthTransform'.
    /// </summary>
    /// <summary>
    private void FindMouthTransform()
    {
        if (mouthTransform != null) return; // Already assigned in Inspector

        Transform[] allChildren = GetComponentsInChildren<Transform>(true);
        foreach (Transform t in allChildren)
        {
            if (t.name == "mouthTransform")
            {
                mouthTransform = t;
                Debug.Log($"<color=green>[BossCreature] Successfully located MouthTransform on {t.parent.name}</color>");
                return;
            }
        }

        Debug.LogWarning("[BossCreature] Could not find a child object named 'MouthTransform'. Breath attacks may fail!");
    }



    private void ChangePhase(BossPhase newPhase)
    {
        currentPhase = newPhase;
        if (movementSystem != null)
        {
            movementSystem.OnPhaseChanged((int)newPhase);
        }
    }

    public void EngagePlayer()
    {
        ChangePhase(BossPhase.Engaged);
        Debug.Log("<color=magenta>[BossCreature] Phase 2: Dragon attacking player!</color>");
        
        // Movement system now handles freestyle logic via OnPhaseChanged hooks
    }

    private void Update()
    {
        // Handle Phase 2 Stamina Drain
        if (currentPhase == BossPhase.Engaged)
        {
            currentStamina -= staminaDrainRate * Time.deltaTime;
            if (currentStamina <= 0)
            {
                currentStamina = 0;
                EnterExhaustedPhase();
            }
        }
        else if (currentPhase == BossPhase.Recharging)
        {
            // Regain stamina. Note: Health never heals to prevent infinite fights!
            currentStamina += staminaRechargeRate * Time.deltaTime;
            if (currentStamina >= maxStamina)
            {
                currentStamina = maxStamina;
                Debug.Log("<color=green>[BossCreature] Stamina full! Diving back in to attack!</color>");
                EngagePlayer();
            }
        }

        // Decay the damage accumulator over time so the boss only evades
        // burst damage, not slow, consistent pokes.
        if (recentDamageAccumulator > 0)
        {
            damageDecayTimer += Time.deltaTime;
            if (damageDecayTimer > 3f) // Reset accumulator after 3 seconds of no damage
            {
                recentDamageAccumulator = 0f;
                damageDecayTimer = 0f;
            }
        }
    }

    private void EnterExhaustedPhase()
    {
        ChangePhase(BossPhase.Exhausted);
        Debug.Log("<color=cyan>[BossCreature] Phase 3: Dragon is exhausted! Fleeing to recharge!</color>");

        // Permanently decay stamina so fights don't last forever
        maxStamina *= 0.7f;
        if (maxStamina < 20f) maxStamina = 20f; // Minimum stamina floor so it can still fight briefly

        // Command the movement system to flee
        if (movementSystem != null)
        {
            movementSystem.ForceImmediateEvasion();
        }
    }

    /// <summary>
    /// Called by the Movement System when the boss finishes an escape route
    /// and returns to the observation deck.
    /// </summary>
    public void BeginRecharging()
    {
        ChangePhase(BossPhase.Recharging);
        Debug.Log("<color=cyan>[BossCreature] Phase 4: Recharging stamina on the observation deck!</color>");
    }

    /// <summary>
    /// Force the boss to begin evasive maneuvers immediately.
    /// Called by other systems (e.g. DragonSegment) when the boss is hit and should react instantly.
    /// </summary>
    public void ForceImmediateEvasion()
    {
        Debug.Log("<color=red>[BossCreature] ForceImmediateEvasion: ordering immediate tactical evasion.</color>");

        if (currentPhase == BossPhase.Orchestrator)
        {
            EngagePlayer();
        }

        if (movementSystem != null)
        {
            movementSystem.ForceImmediateEvasion();
        }

        // Reset accumulators so we don't re-trigger immediately
        recentDamageAccumulator = 0f;
        damageDecayTimer = 0f;

        ChangePhase(BossPhase.Exhausted);
    }

    /// <summary>
    /// Called when the player shoots the boss.
    /// </summary>
    public void TakeDamage(float baseAmount, Vector3 hitPoint, ElementTypeOB7 arrowType = ElementTypeOB7.Normal)
    {
        // 0. Trigger Status Effects (Slows, Freezes)
        if (statusEffects != null)
        {
            statusEffects.ApplyElementalEffect(arrowType);
        }

        // 1. Calculate actual damage based on elemental weaknesses/resistances
        float actualDamage = baseAmount;
        if (elementalModifiers != null)
        {
            foreach (var mod in elementalModifiers)
            {
                if (mod.arrowType == arrowType)
                {
                    actualDamage *= mod.damageMultiplier;
                    break;
                }
            }
        }

        // 1.5 Apply Brittle modifier if frozen by previous ice arrows
        if (statusEffects != null && statusEffects.IsBrittle && actualDamage > 0)
        {
            Debug.Log($"<color=cyan>[BossCreature] Boss Brittle shattered! Damage doubled.</color>");
            actualDamage *= 2.0f;
        }

        // 2. Apply damage or healing
        if (actualDamage < 0)
        {
            currentHealth -= actualDamage;
            if (currentHealth > maxHealth) currentHealth = maxHealth;
            Debug.Log($"<color=green>[BossCreature] Absorbed {arrowType} magic! HEALED for {-actualDamage}. Current Health: {currentHealth}</color>");
            return;
        }

        currentHealth -= actualDamage;
        Debug.Log($"<color=orange>[BossCreature] Hit by {arrowType} arrow! Took {actualDamage} damage. Remaining Health: {currentHealth}</color>");

        if (currentHealth <= 0)
        {
            Die();
            return;
        }

        // If the boss is hit while orchestrating, maybe it engages early!
        if (currentPhase == BossPhase.Orchestrator)
        {
            Debug.Log("<color=red>[BossCreature] You dared to shoot the boss while it was watching? It attacks early!</color>");
            EngagePlayer();
        }

        // Tactical Decision Logic: Evaluate if we need to evade (only if engaged or exhausted)
        if (currentPhase != BossPhase.Orchestrator)
        {
            EvaluateThreat(actualDamage);
        }
    }

    private void EvaluateThreat(float damageTaken)
    {
        recentDamageAccumulator += damageTaken;
        damageDecayTimer = 0f; // Reset decay

        if (recentDamageAccumulator >= evasionDamageThreshold)
        {
            Debug.Log("<color=red>[BossCreature] Threat level high! Ordering tactical evasion.</color>");

            ForceImmediateEvasion();
        }
    }

    private void Die()
    {
        Debug.Log("<color=red>[BossCreature] The Boss has been defeated!</color>");

        // Ping the injected WaveSpawner so the level progression can cleanly move to Victory.
        // Fall back to a scene search if the boss was manually placed (not spawned via WaveSpawner).
        WaveSpawner spawnerToNotify = waveSpawner;
        if (spawnerToNotify == null)
        {
            spawnerToNotify = FindFirstObjectByType<WaveSpawner>();
            if (spawnerToNotify != null)
            {
                Debug.Log("[BossCreature] WaveSpawner not injected — found it via scene search.");
            }
        }

        if (spawnerToNotify != null)
        {
            spawnerToNotify.NotifyTargetDestroyed();
        }
        else
        {
            Debug.LogWarning("[BossCreature] No WaveSpawner found — level progression cannot advance after boss death!");
        }

        // Stop movement
        if (movementSystem != null)
        {
            movementSystem.enabled = false;
        }

        // Command all indestructible pieces (Head, Legs, Tail) to dissolve
        SegmentedDragonManager dragonBody = GetComponent<SegmentedDragonManager>();
        if (dragonBody != null)
        {
            dragonBody.TriggerTotalDeath();
        }

        // Clean up the main boss entity
        Destroy(gameObject, 1f);
    }
}