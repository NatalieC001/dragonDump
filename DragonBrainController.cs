using UnityEngine;
using PixelCrushers.QuestMachine;

/// <summary>
/// DragonBrainController — Combat AI driven by a Quest Machine quest asset.
///
/// CONTEXT / DESIGN NOTES:
/// ---------------------------------------------------------------------------
/// This is NOT a player-facing quest log. We are abusing Quest Machine as a
/// visual combat AI state machine for a boss/character.
///
///   - Each Quest NODE       = an AI "state" (Scan, AttackCrystal, AttackMinion,
///                             ToppleObject, AttackPlayer, Reposition, etc.)
///   - Each node's CONDITIONS = transition rules (when do we leave this state?)
///   - Each node's ACTIONS    = what the dragon does in this state
///                              (send messages, set animator params, move, etc.)
///
/// We use the Message System to bridge quest nodes <-> dragon behavior
/// scripts (e.g. node sends "DragonAttackCrystal"; a behavior script listens
/// and plays the animation, moves the dragon, etc.).
///
/// Repeating behaviors (the AI looping through states) are done INSIDE the
/// quest graph using "Set Quest Node State" actions on a node's True section
/// to flip previous nodes back to Active. We do NOT restart the quest.
///
/// WHY THERE IS NO MANUAL REGISTRATION:
/// ---------------------------------------------------------------------------
/// QuestJournal inherits from IdentifiableQuestListContainer, which registers
/// itself with Quest Machine automatically in OnEnable() and unregisters in
/// OnDisable(). There is NO QuestMachine.RegisterQuestJournal() method — the
/// old code that called it was based on a misunderstanding of the API and
/// threw CS0117. Do NOT add manual registration here.
///
/// WHY WE CLONE THE QUEST:
/// ---------------------------------------------------------------------------
/// The Quest asset on disk is a shared ScriptableObject. If multiple dragons
/// spawn and we don't clone, they all share the same runtime state and would
/// stomp on each other. Clone() gives each dragon its own independent instance.
///
/// WHY NO UI:
/// ---------------------------------------------------------------------------
/// This journal is invisible — no journal UI, no HUD tracker. It exists purely
/// to run the quest logic. Leave journal.questJournalUI / questHUDUI unassigned
/// (or null) if the prefab happens to have them.
/// </summary>
public class DragonBrainController : MonoBehaviour
{
    [Header("Quest Configuration")]
    [Tooltip("The DragonBrain Quest asset. This quest IS the dragon's behavior state machine.")]
    public Quest dragonBrainAsset;

    private QuestJournal journal;
    private Quest runtimeQuestInstance;

    private void Start()
    {
        if (dragonBrainAsset == null)
        {
            Debug.LogError("[DragonBrainController] Missing DragonBrain Quest asset! The boss has no brain.");
            return;
        }

        // Get or add the journal.
        // NOTE: Do NOT try to call QuestMachine.RegisterQuestJournal() —
        // that method does not exist. QuestJournal self-registers in OnEnable().
        journal = GetComponent<QuestJournal>();
        if (journal == null)
        {
            journal = gameObject.AddComponent<QuestJournal>();
        }

        // Clone so each dragon has its own independent runtime state.
        // Never run the shared asset directly on multiple instances.
        runtimeQuestInstance = dragonBrainAsset.Clone();

        // AddQuest kicks off the normal startup flow (Start section -> nodes).
        // We deliberately do NOT call SetState(QuestState.Active) afterwards —
        // AddQuest already drives the startup sequence, and forcing Active here
        // can skip Start-section conditions / actions.
        journal.AddQuest(runtimeQuestInstance);

        Debug.Log($"[DragonBrainController] Brain online for {gameObject.name}");
    }

    private void OnDestroy()
    {
        // Cleanly terminate the state machine when the dragon dies/despawns.
        // We only force Successful if it's still Active — if it already
        // succeeded/failed on its own, leave that state alone.
        if (runtimeQuestInstance != null &&
            runtimeQuestInstance.GetState() == QuestState.Active)
        {
            runtimeQuestInstance.SetState(QuestState.Successful);
        }

        // No manual unregister — QuestJournal handles this in OnDisable().
    }
}