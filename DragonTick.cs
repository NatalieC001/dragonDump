using UnityEngine;
using PixelCrushers;

public class DragonTick : MonoBehaviour, IMessageHandler
{
    [Header("Tick Rate")]
    public float updatesPerSecond = 10f;
    private float tickTimer = 0f;
    private float tickDelta;
    private float tickInterval;

    [Header("Dependencies")]
    public BossCreature brain; // Now serves just as our State Holder 

    // Threshold trackers to only send events when state changes 
    private BossCreature.BossPhase lastPhase;
    private float lastHealthPct;
    private int lastMinionCount;
    private HealthCrystal lastThreatenedCrystal;

    // Anti-spam tracking
    [HideInInspector] public DesireType lastExecutedDesire = DesireType.None;
    [HideInInspector] public float timeSinceLastDesire = 0f;

    private void Start()
    {
        tickInterval = 1f / updatesPerSecond;
        tickDelta = tickInterval;
        if (brain == null) brain = GetComponent<BossCreature>();

        if (brain != null)
        {
            lastPhase = brain.currentPhase;
            lastHealthPct = brain.GetCurrentHealthPct();
            lastMinionCount = brain.lostMinionCount;
        }
    }

    private void OnEnable()
    {
        MessageSystem.AddListener(this, "Brain", string.Empty);
    }

    private void OnDisable()
    {
        MessageSystem.RemoveListener(this, "Brain", string.Empty);
    }

    public void OnMessage(MessageArgs messageArgs)
    {
        if (brain == null) return;

        // PixelCrushers MessageSystem structure:
        // messageArgs.message is the category ("Brain")
        // messageArgs.parameter is the specific event ("CrystalDamaged")
        string eventParameter = messageArgs.parameter;

        if (eventParameter == "CrystalDamaged")
        {
            // The sender is the HealthCrystal
            HealthCrystal crystal = messageArgs.sender as HealthCrystal;
            if (crystal != null)
            {
                brain.LastThreatenedCrystal = crystal;
                brain.crystalThreatTimeout = 5f;
            }
        }
        else if (eventParameter == "CrystalDestroyed")
        {
            HealthCrystal crystal = messageArgs.sender as HealthCrystal;
            if (brain.LastThreatenedCrystal == crystal)
            {
                brain.LastThreatenedCrystal = null;
            }
        }
        else if (eventParameter == "MinionDied")
        {
            brain.lostMinionCount++;
        }
        else if (eventParameter == "StatusEnded")
        {
            // Status effect ended
        }
    }

    private void Update()
    {
        if (brain == null) return;

        // Perform constant time-based math (Stamina/Decays) using real Time.deltaTime 
        UpdatePhysicalState();

        // 10Hz Tick for decision-making and messaging 
        tickTimer += Time.deltaTime;
        timeSinceLastDesire += Time.deltaTime;

        if (tickTimer >= tickInterval)
        {
            tickTimer = 0f;
            EvaluateAndBroadcast();
        }
    }

    private void UpdatePhysicalState()
    {
        // Crystal Threat Timeout 
        if (brain.crystalThreatTimeout > 0f)
        {
            brain.crystalThreatTimeout -= Time.deltaTime;
            if (brain.crystalThreatTimeout <= 0f)
            {
                brain.LastThreatenedCrystal = null;
            }
        }

        // Damage Burst Decay 
        if (brain.recentDamageAccumulator > 0)
        {
            brain.damageDecayTimer += Time.deltaTime;
            if (brain.damageDecayTimer > 3f)
            {
                brain.recentDamageAccumulator = 0f;
                brain.damageDecayTimer = 0f;
            }
        }

        // Handle Stamina Drain & Recharge based on current active Quest Machine Phase 
        // Because Phase is driven by Quest Machine (or internally assigned here depending on the architecture), 
        // we process the float shifts. 
        if (brain.currentPhase == BossCreature.BossPhase.Engaged)
        {
            brain.currentStamina -= brain.staminaDrainRate * Time.deltaTime;
            if (brain.currentStamina <= 0)
            {
                brain.currentStamina = 0;
                brain.maxStamina *= 0.7f;
                if (brain.maxStamina < 20f) brain.maxStamina = 20f;
                brain.currentPhase = BossCreature.BossPhase.Exhausted; // Transition state so tick catches it 
            }
        }
        else if (brain.currentPhase == BossCreature.BossPhase.Recharging)
        {
            brain.currentStamina += brain.staminaRechargeRate * Time.deltaTime;
            if (brain.currentStamina >= brain.maxStamina)
            {
                brain.currentStamina = brain.maxStamina;
                // Transition state back 
                brain.currentPhase = BossCreature.BossPhase.Orchestrator;
            }
        }
    }

    private void EvaluateAndBroadcast()
    {
        // 1. Check if Crystal became safe 
        if (brain.LastThreatenedCrystal == null && brain.crystalThreatTimeout <= 0f)
        {
            if (lastThreatenedCrystal != null) MessageSystem.SendMessage(this, "Brain", "CrystalSafe", string.Empty);
            lastThreatenedCrystal = null;
        }

        if (brain.LastThreatenedCrystal != null && brain.LastThreatenedCrystal.isDestroyed)
        {
            MessageSystem.SendMessage(this, "Brain", "CrystalDestroyed", string.Empty);
        }

        // Segments Intact evaluation 
        SegmentedDragonManager anatomy = GetComponent<SegmentedDragonManager>();
        if (anatomy != null)
        {
            if (anatomy.IsMissingSegments())
            {
                MessageSystem.SendMessage(this, "Brain", "SegmentsMissing", string.Empty);
            }
            else
            {
                MessageSystem.SendMessage(this, "Brain", "SegmentsIntact", string.Empty);
            }
        }

        HealthCrystal[] crystals = FindObjectsByType<HealthCrystal>(FindObjectsSortMode.None);
        if (crystals == null || crystals.Length == 0)
        {
            MessageSystem.SendMessage(this, "Brain", "NoCrystalsLeft", string.Empty);
        }

        // 2. Health & Damage Logic (Burst Damage / Evasion Thresholds) 
        if (brain.recentDamageAccumulator >= brain.evasionDamageThreshold)
        {
            MessageSystem.SendMessage(this, "Brain", "BurstDamageTaken", string.Empty);
            brain.recentDamageAccumulator = 0f; // Consume 
        }

        float currentHealthPct = brain.GetCurrentHealthPct();
        if (lastHealthPct - currentHealthPct > 0.1f) // 10% sudden drop = player shoots boss 
        {
            MessageSystem.SendMessage(this, "Brain", "PlayerShootsBoss", string.Empty);
            lastHealthPct = currentHealthPct;
        }

        if (brain.lostMinionCount > lastMinionCount)
        {
            MessageSystem.SendMessage(this, "Brain", "Minions beaten / Time", string.Empty);
            lastMinionCount = brain.lostMinionCount;
        }

        // 3. Stamina Transitions 
        if (brain.currentPhase == BossCreature.BossPhase.Exhausted && lastPhase != BossCreature.BossPhase.Exhausted)
        {
            MessageSystem.SendMessage(this, "Brain", "StaminaEmpty", string.Empty);
        }
        else if (brain.currentPhase == BossCreature.BossPhase.Orchestrator && lastPhase == BossCreature.BossPhase.Recharging)
        {
            MessageSystem.SendMessage(this, "Brain", "RechargeFull", string.Empty);
        }

        // 4. Decision Evaluator Polling (High level intent mapping) 
        DesireEvaluator evaluator = FindFirstObjectByType<DesireEvaluator>();
        EnvironmentTagRegistry registry = FindFirstObjectByType<EnvironmentTagRegistry>();

        if (evaluator != null && registry != null)
        {
            DesireResult result = evaluator.Evaluate(brain, registry, lastExecutedDesire, timeSinceLastDesire);

            // Map the desire output into the expected flowchart triggers 
            if (result.StrongestDesire == DesireType.CrystalDefense)
            {
                MessageSystem.SendMessage(this, "Brain", "DesireDefend");
            }
            else if (result.StrongestDesire == DesireType.Survival)
            {
                MessageSystem.SendMessage(this, "Brain", "DesireEvade");
            }
            else if (result.StrongestDesire == DesireType.Dominance)
            {
                MessageSystem.SendMessage(this, "Brain", "DesireDominance");
            }
            else if (result.StrongestDesire == DesireType.Territory)
            {
                MessageSystem.SendMessage(this, "Brain", "DesireTerritory");
            }
            else if (result.StrongestDesire == DesireType.ElementalAdvantage)
            {
                MessageSystem.SendMessage(this, "Brain", "DesireElement");
            }
            else if (result.StrongestDesire == DesireType.Attrition)
            {
                MessageSystem.SendMessage(this, "Brain", "DesireSpawn");
            }

            // Map additional situational context tags 
            if (result.TargetTransform != null)
            {
                EnvironmentTag tag = result.TargetTransform.GetComponent<EnvironmentTag>();
                if (tag != null)
                {
                    if (tag.type == EnvironmentTag.TagType.Chokepoint) MessageSystem.SendMessage(this, "Brain", "TagChokepoint", string.Empty);
                    if (tag.type == EnvironmentTag.TagType.HardCover) MessageSystem.SendMessage(this, "Brain", "TagCover", string.Empty);
                    if (tag.type == EnvironmentTag.TagType.ToppleObject) MessageSystem.SendMessage(this, "Brain", "TagTopple", string.Empty);
                }
            }
        }

        lastPhase = brain.currentPhase;
        lastThreatenedCrystal = brain.LastThreatenedCrystal;
    }
}
