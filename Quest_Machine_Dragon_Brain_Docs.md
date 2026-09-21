# Dragon Boss AI & Quest Machine Integration

## High-Level Concept

In this project, we are creatively using **Quest Machine** (a popular asset by PixelCrushers) not for a traditional player-facing quest log, but as a visual, node-based **Combat AI State Machine** for our bosses (e.g., the Dragon).

Instead of writing complex, hard-to-maintain state machine logic in C#, we use Quest Machine's node editor to visually design the boss's behavior.

- **Quest Nodes** represent the **AI States** (e.g., Scan, AttackCrystal, Swoop, ToppleObject, Reposition).
- **Node Conditions** act as **Transition Rules** (determining when the boss leaves a state and enters another).
- **Node Actions** define what the boss **executes** when entering or during that state (such as playing an animation, moving, or triggering an attack).

The Quest Machine UI is disabled. The `DragonBrainController` loads this "quest" in the background, and the boss executes it to fight the player. Below is a breakdown of our first iteration, its technical flaws, and the refactored modular architecture we are adopting.

---

## Combat Dynamics & Objectives: Player vs. Dragon

Before delving into the technical architecture, it is critical to understand the overarching design of the fight. The entire game hinges on two opposing sets of priorities. The Player and the Dragon are engaged in a war of attrition over supply lines.

**The Player wants to:**
- Destroy the Power Crystals — the main objective. Each crystal is a lifeline to the Dragon.
- Pick off minions — the secondary objective. Every minion killed steals a small amount of power from the Dragon.
- Force the Dragon into a state where it can't recharge — no crystals, no minions, no recovery.
- Survive long enough to do all three.

**The Dragon wants to:**
- Protect the Power Crystals — they're the reason it can keep regenerating segments and health.
- Keep its minion count up — the pack feeds it.
- Corral the player with Dark Spirit Clouds — shrink the arena, obscure itself and its minions, weaken the player's attacks.
- Kill the player before the player cuts the supply lines.
- When the player threatens a crystal, pivot *everything* to defending it — minions, breath, body.

The two sides are in direct opposition. The player's priorities are the Dragon's priorities, inverted.

To visualize how these mechanics play out systemically, here are two flowcharts representing the fight from each perspective.

### The Player's Point of View

```mermaid
graph TD
    StartPlayer((Player Spawns)) --> AssessArena[Assess Arena Threats]
    AssessArena --> ThreatenCrystal[Target Power Crystals]
    AssessArena --> PickOffMinions[Hunt Minion Pack]

    PickOffMinions -->|Minions Die| StarveDragon[Starve Dragon of Pack Power]
    ThreatenCrystal -->|Crystal Damaged| TriggerDragonDefenses[Trigger Massive Dragon Retaliation]

    TriggerDragonDefenses --> Survive[Survive Swoops & Breath Attacks]
    Survive --> DestroyCrystal[Destroy Power Crystal]

    DestroyCrystal -->|Crystals Gone| ForceVulnerability[Force Dragon into Vulnerable State]
    StarveDragon -->|Minions Gone| ForceVulnerability

    ForceVulnerability -->|No Recovery Left| DefeatDragon((Defeat Dragon))

    %% Obstacles
    AssessArena -.->|Avoid| Clouds[Dark Spirit Clouds]
    Clouds -.->|Debuffs| ShrinkArena(Shrinks Arena & Weakens Attacks)
```

### The Dragon's Point of View (Orchestrated by the Brain)

```mermaid
graph TD
    StartDragon((Dragon Brain Evaluates)) --> CheckSupplyLines[Assess Crystals & Minions]

    CheckSupplyLines --> IsCrystalSafe{Is Crystal Threatened?}
    IsCrystalSafe -- Yes (Taking Damage) --> PivotToDefense[Pivot ALL Resources to Defense]
    IsCrystalSafe -- No --> ManagePack{Is Pack Healthy?}

    PivotToDefense --> CommandBody[Body: Bodyblock Crystal]
    PivotToDefense --> CommandBreath[Breath: Target Player at Crystal]
    PivotToDefense --> CommandMinions[Minions: Swarm Crystal Area]

    ManagePack -- Low Minions --> SpawnMinions[Action: Spawn Wave]
    ManagePack -- Healthy --> CorralPlayer[Action: Corral Player]

    CorralPlayer --> CastClouds[Cast Dark Spirit Clouds]
    CastClouds -->|Limits Player Space| ExecuteAttack[Action: Attack / Swoop]

    CommandBody --> ProtectRecovery[Ensure Capability to Regenerate]
    SpawnMinions --> ProtectRecovery
    ProtectRecovery --> KillPlayer((Kill Player))
```

---

## Phase 1: The First Attempt (Current Implementation)

Our initial approach proved that Quest Machine could drive AI, but the implementation tightly coupled systems and mixed class responsibilities. Every flaw in this design has been addressed by the new architecture.

### The Components

1. **`DragonBrainController`**
   - **Role:** Instantiates the `Quest` asset, adding it to a `QuestJournal` component to start the node sequence.

2. **`DragonActionListeners`**
   - **Role:** Listens for `"DragonActions"` strings broadcast by the Quest Machine Message System.
   - **The Flaw:** It is heavily coupled. It receives a macro-string like `"Swoop"`, then manually alters phase states in `BossCreature`, triggers attacks on `ElementalBreathController`, and dictates movement on `AirborneBossMovement`.
   - **The Solution:** The `DragonActionListeners` script is deleted entirely. In the new system, we use Atomic Custom Actions inside the Quest Machine UI. A node now simply fires independent actions (`SetPhase`, `SetMotorIntent`, `FireWeapon`), allowing the behaviors to execute simultaneously without the C# scripts ever referencing or knowing about each other.

3. **`BossCreature`**
   - **Role:** Tracks numeric values (Health, Stamina) and an enum state (`BossPhase`).
   - **The Flaw:** It mixes variable tracking with combat logic overrides. For example, in its `Update()` loop, if stamina hits zero, it triggers a function to force an evasion. This bypasses the Quest Machine entirely, splitting the AI logic into two separate locations.
   - **The Solution:** We replace this with `BossVitals`. When stamina hits zero in the new system, it merely fires an `OnStaminaDepleted()` event. The Quest Machine receives this event, updates its variables, and the Node Graph evaluates the condition to decide what to do. This ensures that 100% of the combat logic is consolidated visibly within the node editor, making the AI predictable and easy to adjust.

4. **`AirborneBossMovement`**
   - **Role:** Moves the transform using flight intents (`Pursue`, `Bank`, `Stillhold`) and spline followers.
   - **The Flaw:** Instead of purely receiving movement coordinates, it contains its own logic checks. It queries `BossCreature.currentPhase` and `BossCreature.GetCurrentHealthPct()` internally to calculate where it should fly.
   - **The Solution:** We replace this with `BossMotor`. The motor is completely stripped of AI queries. It waits for the Brain to provide an intent and a target (e.g., `RequestFreestyleIntent(Pursue, Player)`). This benefits the architecture by making the movement logic entirely reusable for any boss or creature, as it no longer relies on specific boss state variables.

---

## Phase 2: The Optimal Solution (Architectural Evolution)

To fix the coupling and scattered logic, the architecture is being rebuilt into strictly separated components.

```mermaid
graph TD
    subgraph The Body Statistics
        BV[BossVitals]
    end

    subgraph The Brain System
        IDB((IDragonBrain Interface))
        QMB[QuestMachineDragonBrain Adapter] -.->|Implements| IDB
        QMN[QuestMachine Node Graph Asset] -.->|Evaluated by| QMB
    end

    subgraph The Movement Logic
        ABM[BossMotor]
    end

    subgraph Other Executors
        EBC[ElementalBreathController]
        SPW[WaveSpawner]
    end

    %% Sensor Reporting
    BV -- Invokes event: OnHealthThreshold(10) --> IDB
    ABM -- Invokes event: OnTetherAttached(AnchorData) --> IDB

    %% Brain Orchestration Commands
    QMN -- Dispatches Atomic Actions --> QMB
    QMB -- Calls method: RequestSplineEvasion() --> ABM
    QMB -- Calls method: FireBreath(Player) --> EBC
```

### Technical Responsibilities & Data Flow

#### 1. The Body Statistics (`BossVitals`)
- **Role:** A data container for floats (Health, Stamina) and enums (Status Effects).
- **What it does:** It runs the math when damage is taken. When a value crosses a specific threshold (e.g., Stamina drops to 0), it invokes a C# event. It contains zero logic for deciding how the boss should react to that damage.

#### 2. The Movement Logic (`BossMotor`)
- **Role:** A script that manipulates `transform.position` and `transform.rotation`.
- **What it does:** It contains the mathematical formulas required to move the object (freestyle math, spline math). It relies entirely on public methods being called to provide its target destination. It acts as a spatial sensor, firing events (e.g., `OnTetherAttached()`) when collisions occur.

#### 3. The Brain System (De-tangled)
In Phase 1, the "Brain" was a confusing mix of concepts. In Phase 2, it is strictly separated into three technical parts:

1. **`IDragonBrain` (The Interface):** This is just a C# contract. It guarantees that any class implementing it has methods like `OnDamageTaken(float amount)` and `OnStaminaDepleted()`. `BossVitals` and `BossMotor` only talk to this interface. They do not know Quest Machine exists.
2. **`QuestMachineDragonBrain` (The Adapter Script):** This is a C# script attached to the boss. It implements `IDragonBrain`.
   - **Input:** When `BossVitals` fires an event, this Adapter catches it, and mathematically updates the corresponding "Counter" or "Condition" variables *inside* the Quest Machine API.
   - **Output:** When Quest Machine executes an action, this Adapter translates that action into public method calls on the Motor and Body scripts.
3. **The Quest Machine Node Graph (The Logic Data):** This is the visual asset created in the Unity Editor. **This is where the actual combat strategy and decision-making exist.** It continuously evaluates the variables updated by the Adapter. When conditions are met, it changes nodes and outputs Actions back to the Adapter.

---

## External Signals & The Priority Logic Loop

To truly decouple the system, the Brain (Quest Machine) must rely heavily on **Outside Information** imported from the scene environment. The dragon does not magically know everything; it relies on signals sent from environmental sensors to calculate its priorities.

These external signals create a **Logic Loop**:
1. An outside scene element sends a signal.
2. The Adapter imports it into Quest Machine.
3. The Dragon evaluates its priority based on its internal stats (Health/Minions) vs. the external environment.
4. The Dragon acts to manipulate the environment.

Below is a diagram explicitly showing how outside information flows into the Brain to drive these priority decisions.

```mermaid
graph TD
    subgraph The Outside World (External Scene Sensors)
        Crystal[Power Crystal]
        VRArena[VR Walking Space Sensor]
        ToppleNode[Topple-able Environment Object]
        MinionSpots[Hidden Ambush Nodes]
    end

    subgraph The Adapter
        QMB[QuestMachineDragonBrain]
    end

    subgraph Quest Machine Priority Logic Loop
        PriorityEval{Evaluate Combined State}

        PriorityEval -->|Crystal Damaged| LogicDefend[Logic: Pivot to Defense]
        PriorityEval -->|Player Space High| LogicCorral[Logic: Shrink Arena]
        PriorityEval -->|Objects Available| LogicObscure[Logic: Topple to Block View]
        PriorityEval -->|Ambush Ready| LogicSpawn[Logic: Create Wave]
    end

    subgraph Action Executors
        Motor[BossMotor]
        Spawner[WaveSpawner]
        Breath[ElementalBreathController]
    end

    %% External Signals Flowing IN
    Crystal -- Signal: "I am taking damage!" --> QMB
    VRArena -- Signal: "Player has 15sqm of safe space" --> QMB
    ToppleNode -- Signal: "I can be struck to block movement" --> QMB
    MinionSpots -- Signal: "Ambush locations are unoccupied" --> QMB

    %% Adapter Imports to Logic
    QMB -- Updates Variables --> PriorityEval

    %% Logic drives Execution (Output)
    LogicDefend -- Action: Defend Crystal --> Motor
    LogicCorral -- Action: Cast Clouds --> Breath
    LogicObscure -- Action: Strike Pillar --> Motor
    LogicSpawn -- Action: Spawn Minions --> Spawner
```

### Explaining the External Signal Loops

#### 1. The Power Crystal Loop
- **The External Signal:** The crystal object in the scene detects collision from a player's arrow. It broadcasts a decoupled event: `OnCrystalDamaged`.
- **The Brain's Priority:** The Adapter hears this and updates `IsCrystalThreatened = True`. The Quest Machine logic evaluates: "My health is fine, but my lifeline is dying." It drops all other priorities, transitioning the state machine to defense.

#### 2. The VR Walking Space Loop
- **The External Signal:** A spatial sensor grid in the arena calculates how much physical walking space the VR player currently has. It broadcasts: `AvailablePlayerSpace = 15`.
- **The Brain's Priority:** The Adapter feeds this integer into Quest Machine. The logic evaluates: "The player has too much room to dodge." The dragon prioritizes shrinking the arena, transitioning to a state that casts Dark Spirit Clouds to close up those safe spaces.

#### 3. The Topple Object Loop
- **The External Signal:** Static pillars or debris in the scene broadcast their state: `CanBeToppled = True`.
- **The Brain's Priority:** If the Dragon determines the player has clear line-of-sight to the crystals, it reads this signal and transitions to a state where it attacks the pillar instead of the player, dynamically altering the platform geometry to make the player's life harder.

#### 4. The Minion Ambush Loop
- **The External Signal:** Hidden nodes in the scene (e.g., caves or dark corners) broadcast their status: `ReadyForAmbush = True`.
- **The Brain's Priority:** The Dragon checks its internal stats (`CurrentMinionCount < 3`). Because it needs power, it prioritizes a wave spawn, but uses the external signal to direct the `WaveSpawner` to use those specific hidden scene nodes, maximizing the tactical advantage.

---

### Understanding the Sequence: How Events Drive the Node Graph

To fully grasp how this decoupled architecture works with Quest Machine, it is crucial to understand the chronological sequence of events. A static flowchart shows the *states*, but a **sequence diagram** shows *time*.

The C# classes (`BossVitals`, `BossMotor`) are completely ignorant of Quest Machine. They only shout into the void (fire C# events). The `QuestMachineDragonBrain` (Adapter) listens to those shouts and updates variables in Quest Machine. Quest Machine then evaluates those variables under the hood, transitions nodes, and spits actions back out.

Here is the exact timeline of a single decision (e.g., Evading due to low stamina):

```mermaid
sequenceDiagram
    participant Player
    participant Body as BossVitals (C#)
    participant Adapter as QuestMachineDragonBrain (C#)
    participant QuestMachine as Node Graph Logic (PixelCrushers API)
    participant Motor as BossMotor (C#)

    Player->>Body: Attacks Boss (Stamina Drops to 0)

    note over Body: Body contains no logic to evade.
    Body->>Adapter: Fires Event: OnStaminaDepleted()

    note over Adapter: Adapter acts as translator.
    Adapter->>QuestMachine: Updates Variable: "StaminaCounter = 0"

    note over QuestMachine: Quest Machine runs its internal Update loop.
    QuestMachine->>QuestMachine: Evaluates Conditions on Active Node
    QuestMachine->>QuestMachine: Condition Met: "If StaminaCounter == 0"
    QuestMachine->>QuestMachine: Transitions to "Evade" Node

    note over QuestMachine: Evade Node becomes Active.
    QuestMachine-->>Adapter: Outputs Atomic Action: RequestSplineAction(Escape)

    note over Adapter: Adapter translates action back to C#.
    Adapter->>Motor: Calls Method: RequestSplineEvasion()

    note over Motor: Motor executes the math to fly to spline.
    Motor-->>Player: Boss flies away from Player
```

#### Detailed Breakdown of Node Evaluations (The Full Life Cycle)
The sequence above shows the flow of time. Below is the comprehensive, static map of the nodes themselves, showing how the variables (updated by the Adapter in step 3 above) dictate the transitions. This covers the *entire* life cycle of the boss.

```mermaid
graph TD
    Idle[Idle Orchestrating] -->|Player Enters Arena| Eval[Evaluate Combat Priorities]

    %% Priority 1: Survival & Regeneration
    Eval --> CheckRegen{Needs Health/Stamina <br> AND <br> Crystal Available?}
    CheckRegen -- True --> Regen[Regenerate]
    CheckRegen -- False --> CheckCrystal{Is Crystal Threatened?}

    %% Priority 2: Defense
    CheckCrystal -- True --> Defend[Defend Crystal]
    CheckCrystal -- False --> CheckThreat{Threat >= 100 <br> OR <br> Stamina == 0?}

    %% Priority 3: Evasion
    CheckThreat -- True --> Evade[Evade]
    CheckThreat -- False --> CheckMinions{Minion Count < 3 <br> AND <br> Cooldown Ready?}

    %% Priority 4: Pack Management
    CheckMinions -- True --> Spawn[Spawn Minions]
    CheckMinions -- False --> CheckBreath{Player Dist < 20m <br> AND <br> Breath Ready?}

    %% Priority 5: Ranged Attack
    CheckBreath -- True --> Breath[Breath Attack]
    CheckBreath -- False --> CheckBait{Threat < 50 <br> AND <br> Time Passivity > 5s?}

    %% Priority 6: Positioning
    CheckBait -- True --> Bait[Bait and Herd]
    CheckBait -- False --> CheckSwoop{Threat < 50 <br> AND <br> Player Dist > 30m?}

    %% Priority 7: Melee Aggression
    CheckSwoop -- True --> Swoop[Swoop]
    CheckSwoop -- False --> Attack[Attack]

    %% Return Paths (Action Completed triggers evaluation again)
    Regen -->|Health/Stamina Replenished| Eval
    Defend -->|Action Completed| Eval
    Evade -->|Stamina Replenished| Eval
    Spawn -->|Action Completed| Eval
    Breath -->|Action Completed| Eval
    Bait -->|Action Completed| Eval
    Swoop -->|Action Completed| Eval
    Attack -->|Action Completed| Eval
```

---

### Proof of Concept: Uncoupling the Legacy Behaviors

To completely remove `DragonActionListeners` without losing functionality, we move the composition of behavior directly into the **Quest Machine Node UI** using **Atomic Custom Actions**.

In Phase 1, there was a lot of redundancy in the behavior names (e.g., "Bank" and "Swoop" were nearly identical, "Recharging" and "Regenerate" were duplicates). In the new architecture, we have tidied up these behaviors, giving them their real, intuitive names, and ensuring the entire life cycle of the creature is covered.

Below is the technical proof of concept demonstrating how the cleaned-up list of behaviors maps to uncoupled atomic actions inside the Quest Machine UI.

| The Cleaned-Up Behavior Name | What the Quest Machine Node UI Executes (The Atomic Actions List) |
| :--- | :--- |
| **Attack** *(formerly Pursuit)* | 1. `SetPhaseAction(Phase: Engaged)` <br> 2. `SetMotorIntentAction(Intent: Pursue, Target: Player)` |
| **Swoop** *(merged with Bank)* | 1. `SetPhaseAction(Phase: Engaged)` <br> 2. `SetMotorIntentAction(Intent: Pursue, Target: Player)` <br> 3. `FireWeaponAction(Type: Fire, Target: Player)` <br> 4. `StartCooldownAction(DesireType: Dominance)` |
| **Bait & Herd** *(merged Flank/Bait/Herd)* | 1. `SetPhaseAction(Phase: Engaged)` <br> 2. `SetMotorIntentAction(Intent: Pursue, Target: Player)` |
| **Spawn Minions** *(merged SpawnWave/Circle)* | 1. `SpawnMinionAction(Intent: AllIn, Target: Player)` <br> 2. `StartCooldownAction(DesireType: Attrition)` |
| **Breath Attack** | 1. `SetPhaseAction(Phase: Engaged)` <br> 2. `FireWeaponAction(Type: Fire, Target: Player)` <br> 3. `StartCooldownAction(DesireType: ElementalAdvantage)` |
| **Defend Crystal** | 1. `SetPhaseAction(Phase: Engaged)` <br> 2. `RequestSplineAction(PathType: AirborneObservation)` |
| **Evade** | 1. `SetPhaseAction(Phase: Exhausted)` <br> 2. `RequestSplineAction(PathType: AirborneEscape)` |
| **Regenerate** *(merged Recharging/Regenerate)* | 1. `SetPhaseAction(Phase: Recharging)` <br> 2. `RequestSplineAction(PathType: AirborneObservation)` <br> 3. `TriggerHealthRegenAction(Target: NearestCrystal)` <br> 4. `TriggerSegmentRegrowthAction()` <br> 5. `RefillMinionReserveAction()` |

**The Result:** The Boss successfully executes every complex behavior required for its life cycle—including regrowing body segments and recovering health. However, the `BossMotor` script has absolutely no idea that the `ElementalBreathController` fired or that segments grew back.

All logic and macro-behavior composition reside 100% inside the Quest Machine node graph, and the C# classes remain fully uncoupled and ignorant of each other.
