using UnityEngine;
using PixelCrushers;

/// <summary> 
/// Passive listeners that wait for Quest Machine to dictate state, 
/// then drive the actual animation, movement, and spawning. 
/// NO UPDATE LOOPS HERE! 
/// </summary> 
public class DragonActionListeners : MonoBehaviour, IMessageHandler
{
    private AirborneBossMovement movement;
    private ElementalBreathController breath;
    private MinionRequestBroker spawner;
    private BossCreature brain;
    private SegmentedDragonManager anatomy;

    private void Awake()
    {
        movement = GetComponent<AirborneBossMovement>();
        brain = GetComponent<BossCreature>();
        anatomy = GetComponent<SegmentedDragonManager>();
        spawner = FindFirstObjectByType<MinionRequestBroker>();
    }

    private void OnEnable()
    {
        MessageSystem.AddListener(this, "DragonActions", string.Empty);
        MessageSystem.AddListener(this, "Brain", string.Empty);
        if (anatomy != null) anatomy.OnHeadSpawned += HandleHeadSpawned;
    }

    private void OnDisable()
    {
        MessageSystem.RemoveListener(this, "DragonActions", string.Empty);
        MessageSystem.RemoveListener(this, "Brain", string.Empty);
        if (anatomy != null) anatomy.OnHeadSpawned -= HandleHeadSpawned;
    }

    private void HandleHeadSpawned()
    {
        breath = GetComponentInChildren<ElementalBreathController>(true);
    }

    public void OnMessage(MessageArgs messageArgs)
    {
        // PixelCrushers MessageSystem passes the actual command in the parameter
        // Actually, the Quest Generator sets the Action target to "DragonActions" and the message to the action string
        string action = messageArgs.message;

        // Only process messages targeting "DragonActions" or "Brain"
        // Since we registered for those in OnEnable, we should receive them, but we must check what they are.
        // Wait, if the message string IS the action ("Pursuit"), we must check if we should process it.
        // But we registered explicitly to listen to "DragonActions" and "Brain". So `messageArgs.message` will be "DragonActions" or "Brain"!
        // Let's look at the generator again: targetID = "DragonActions", message = actionMessage, parameter = "".
        // So the PixelCrushers MessageSystem will broadcast a message where `messageArgs.target` = "DragonActions", and `messageArgs.message` = "Pursuit".
        // HOWEVER, `MessageSystem.AddListener(this, "DragonActions", "")` means "listen for messages where the `message` field is 'DragonActions'".
        // This is wrong! The generator sends the action in the `message` field.
        // To fix this without breaking the generator, we can read messageArgs.parameter if the generator set parameter, OR read messageArgs.message if we fixed the generator.

        // I will fix the generator to send targetID = "", message = "DragonActions", parameter = "Pursuit".
        string actionCommand = messageArgs.parameter;
        Vector3 playerPos = GameObject.FindGameObjectWithTag("Player")?.transform.position ?? Vector3.zero;

        // We only want to execute actions if this message is targeted at DragonActions (i.e. the muscles).
        // The Brain (Quest Machine) sends "DragonActions". If it's a message for the "Brain", we don't process it here,
        // otherwise we would infinitely loop by re-broadcasting it.
        if (action != "DragonActions") return;

        switch (actionCommand)
        {
            // ---- DragonActions: visual/behavioral commands ---- 
            case "Pursuit":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Engaged;
                if (movement != null) movement.RequestFreestyleIntent(AirborneBossMovement.FreestyleIntent.Pursue, playerPos);
                break;

            case "Swoop":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Engaged;
                if (movement != null) movement.RequestFreestyleIntent(AirborneBossMovement.FreestyleIntent.Pursue, playerPos);
                if (breath != null) breath.FireBreath(BreathType.Fire, playerPos);

                // Tell DragonTick what desire was executed so it can apply cooldowns
                DragonTick tickSwoop = GetComponent<DragonTick>();
                if (tickSwoop != null) { tickSwoop.lastExecutedDesire = DesireType.Dominance; tickSwoop.timeSinceLastDesire = 0f; }

                Invoke(nameof(SimulateSwoopOvershoot), 2.5f);
                break;

            case "Bank":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Engaged;
                if (movement != null) movement.RequestFreestyleIntent(AirborneBossMovement.FreestyleIntent.Withdraw, transform.position + Vector3.up * 20f);
                break;

            case "Bait":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Engaged;
                if (movement != null) movement.RequestFreestyleIntent(AirborneBossMovement.FreestyleIntent.Pursue, playerPos);
                break;

            case "Herd":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Engaged;
                if (movement != null) movement.RequestFreestyleIntent(AirborneBossMovement.FreestyleIntent.Pursue, playerPos);
                break;

            case "SpawnWave":
                if (spawner != null) spawner.RequestMinions(SpawnIntent.AllIn, playerPos);

                DragonTick tickSpawn = GetComponent<DragonTick>();
                if (tickSpawn != null) { tickSpawn.lastExecutedDesire = DesireType.Attrition; tickSpawn.timeSinceLastDesire = 0f; }

                Invoke(nameof(SimulateActionComplete), 1.0f);
                break;

            case "Flank":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Engaged;
                if (movement != null) movement.RequestFreestyleIntent(AirborneBossMovement.FreestyleIntent.Pursue, playerPos);
                break;

            case "Breath":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Engaged;
                if (breath != null) breath.FireBreath(BreathType.Fire, playerPos);

                DragonTick tickBreath = GetComponent<DragonTick>();
                if (tickBreath != null) { tickBreath.lastExecutedDesire = DesireType.ElementalAdvantage; tickBreath.timeSinceLastDesire = 0f; }

                Invoke(nameof(SimulateActionComplete), 2.5f);
                break;

            case "Topple":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Engaged;
                if (movement != null) movement.RequestFreestyleIntent(AirborneBossMovement.FreestyleIntent.Pursue, playerPos);
                Invoke(nameof(SimulateActionComplete), 2.0f);
                break;

            case "DefendCrystal":
                // Return to the observation spline orbiting the threatened crystal.
                // GetObservationPath queries BossPathManager for any airborne observation path.
                // If none found, fall back to a general evasion (escape spline).
                if (movement != null)
                {
                    GameObject defenceObsPath = movement.GetObservationPath(PathTypeTag.PathType.Airborne);
                    if (defenceObsPath != null)
                        movement.RequestReturnToCoil(defenceObsPath);
                    else
                        movement.ForceImmediateEvasion();
                }
                break;

            case "Evade":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Exhausted;
                if (movement != null) movement.ForceImmediateEvasion();
                break;

            case "Exhausted":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Exhausted;
                break;

            case "Ride Escape Spline":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Exhausted;
                if (movement != null) movement.ForceImmediateEvasion();
                break;

            case "Recharging":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Recharging;
                break;

            case "Observation Spline - Regen Stamina":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Recharging;
                if (movement != null)
                {
                    GameObject obsPath = movement.GetObservationPath(PathTypeTag.PathType.Airborne);
                    movement.RequestReturnToCoil(obsPath);
                }
                break;

            case "Rage":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Engaged;
                break;

            case "Relentless":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Engaged;
                if (movement != null) movement.RequestFreestyleIntent(AirborneBossMovement.FreestyleIntent.Pursue, playerPos);
                break;

            case "Regenerate":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Recharging;
                RegeneratorController regen = GetComponent<RegeneratorController>();
                if (regen != null)
                {
                    HealthCrystal[] crystals = FindObjectsByType<HealthCrystal>(FindObjectsSortMode.None);
                    HealthCrystal crystalToHeal = System.Array.Find(crystals, c => c.gameObject.activeInHierarchy);
                    if (crystalToHeal != null)
                    {
                        regen.BeginRegeneration(crystalToHeal);
                        if (spawner != null) spawner.RefillReserve();
                    }
                }
                break;

            case "Desperate":
                if (brain != null) brain.currentPhase = BossCreature.BossPhase.Engaged;
                if (movement != null) movement.RequestFreestyleIntent(AirborneBossMovement.FreestyleIntent.Pursue, playerPos);
                break;

            case "Weigh Desire vs Threat":
                break;

            case "Circle + Spawn Minions":
                if (spawner != null) spawner.RequestMinions(SpawnIntent.Circle, playerPos);
                MessageSystem.SendMessage(this, "Brain", "ActionComplete");
                break;
        }
    }

    private void SimulateSwoopOvershoot()
    {
        MessageSystem.SendMessage(this, "Brain", "SwoopOvershoot");
    }

    private void SimulateActionComplete()
    {
        MessageSystem.SendMessage(this, "Brain", "ActionComplete");
    }
}
