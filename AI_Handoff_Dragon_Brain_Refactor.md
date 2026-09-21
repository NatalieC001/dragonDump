# AI Handoff: Dragon Boss Architecture Refactor

## 1. Project Context & Current State (The Back View)

We are developing a VR boss fight against a Dragon. The core combat dynamic is a war of attrition over supply lines: the player must destroy Power Crystals and minions to force the Dragon into a vulnerable state, while the Dragon defends these assets and exerts territorial control.

**The Current Codebase State (Phase 1):**
Currently, the Dragon's AI is driven by **Quest Machine** (PixelCrushers), which is used as a visual node-based state machine. However, the implementation is highly coupled and suffers from mixed responsibilities:
- **`BossCreature.cs`**: Confusingly named. It tracks vitals (Health, Stamina) but also overrides combat logic (e.g., forcing evasions when stamina drops), bypassing Quest Machine.
- **`AirborneBossMovement.cs`**: Executes movement (splines, freestyle) but contains internal logic querying `BossCreature` state to decide *where* to fly.
- **`DragonActionListeners.cs`**: A massive `switch` statement that listens for string-based macro-commands (like "Swoop") from Quest Machine and manually triggers animations, movement, and phase changes across all systems.
- **Level Progression (Waves):** Currently, advancing the level requires destroying *all* enemies. This conflicts with the Dragon's tactical desire to hide special minions for a final ambush wave.

*(Note: See `Quest_Machine_Dragon_Brain_Docs.md` for a deep-dive architectural analysis of the current flaws, the logic evaluations, and the benefits of the decoupled approach.)*

---

## 2. The Target Architecture (The Goal)

The objective is to refactor the Dragon into a modular **Triad Architecture**:
1. **The Body Statistics (`BossVitals`)**: Pure data container/sensor. Takes damage, tracks stamina/status, and fires C# events (`OnStaminaDepleted`). Makes ZERO decisions.
2. **The Movement Logic (`BossMotor`)**: Pure actuator/spatial sensor. Knows *how* to move (math for splines/freestyle) and detects collisions (`OnTetherAttached`), but relies entirely on external commands to know *where* to go.
3. **The Brain System (`IDragonBrain` & `QuestMachineDragonBrain`)**: The central orchestrator. The C# Adapter listens to events from the Body and Motor, updates variables inside the Quest Machine API, and translates Quest Machine outputs into method calls.

**The Golden Rule:** The Quest Machine Node Graph must be the *exclusive* location where combat decisions and strategy are evaluated.

---

## 3. Actionable Task Roadmap (Upgrades Needed)

This roadmap outlines the exact steps required to upgrade the codebase to the new architecture.

### Task 1: Refactor Vitals and Movement
- Rename/Refactor `BossCreature.cs` into `BossVitals.cs`. Strip out all `ForceImmediateEvasion()` logic. Replace it with standard C# events (e.g., `public event Action OnStaminaDepleted;`).
- Rename/Refactor `AirborneBossMovement.cs` into `BossMotor.cs`. Remove all references to `BossCreature` health/phases. Expose clean public methods (e.g., `RequestFreestyleIntent(Intent, TargetVector)`).

### Task 2: Implement the Brain Interface & Adapter
- Create `IDragonBrain.cs` interface to guarantee methods for receiving events (`NotifyDamageTaken`, `NotifyStaminaDepleted`).
- Create `QuestMachineDragonBrain.cs` (Monobehaviour implementing `IDragonBrain`).
- Wire the Adapter: Subscribe it to `BossVitals` and `BossMotor` events. Write the code that pushes these event updates into Quest Machine's internal variables (Counters/Conditions).

### Task 3: Replace `DragonActionListeners` with Atomic Actions
- Delete `DragonActionListeners.cs`.
- Create new C# scripts extending Quest Machine's `QuestAction` class to build **Atomic Custom Actions**.
  - Examples needed: `SetMotorIntentAction`, `RequestSplineAction`, `FireWeaponAction`, `SetPhaseAction`, `SpawnSpecialMinionAction`.
- The node graph will composite these atomic actions (e.g., a "Swoop" node triggers `SetPhase` + `SetMotorIntent` + `FireWeapon` sequentially). This superior architectural isolation ensures the C# classes do not become tangled.

### Task 4: Fix the Wave Logic Conflict
- Decouple **Automated Game Waves** (which the player must clear to advance the level) from **Dragon Special Minions**.
- Create a distinct spawn pool/prefab for the Dragon's tactical minions. Ensure the Level Progression manager ignores these special minions so the Dragon can hide them without stalling the game.

### Task 5: Implement Environmental Sensors & Territorial Control
- Create a **Platform Grid Sensor** script (e.g., a 5x5 grid applied to the arena floor) that tracks `PlayerWalkableCells` and broadcasts when cells are occupied by obstacles.
- Feed this grid data into `QuestMachineDragonBrain`.
- Implement the **Cursed Pot Hazard**: A projectile the Dragon fires. If the player shoots it mid-air, they are safe. If it hits the grid, it spawns a "Dark Spirit Cloud" restricting a grid cell. If the player enters the cloud, apply an incremental weapon strength debuff.

### Task 6: Rebuild the Quest Machine Node Graph
- Using the new variables and atomic actions, construct the 4-tier Master Logic loop inside the Unity Editor:
  1. **Survival (Reactive):** Defend crystals / Evade if critical.
  2. **Territorial Dominance (Proactive):** Evaluate the Grid Sensor. Topple columns or cast Cursed Pots to box the player in.
  3. **Ambush Preparation:** Spawn Special Minions to hide and accrue numbers. (Implement a `PatienceTimer` fallback to prevent stalling if the player dodges perfectly).
  4. **Coordinated Execution:** Launch the final charge (Swoop + Swarm).
