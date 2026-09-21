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

## Discussion Point: Level Progression Waves vs. Dragon Tactical Minions

A critical architectural distinction must be made regarding how the Dragon spawns minions, and how this affects the core game loop.

Currently, the game's Level Progression relies on a "Clear the Wave" architecture: the level only advances when *all* enemies on the board are destroyed. 

If the Dragon spawns standard enemies and hides them intentionally (as part of its tactical strategy), the player will be unable to find them, and the level progression will completely stall. We cannot conflate automated level waves with the Dragon's tactical reserves.

**The Solution:**
1. **Automated Game Waves:** These are standard, un-strategic enemies designed to keep the player busy. When these are cleared, the game advances to the next wave phase.
2. **Dragon's Special Minions:** The Dragon must control a distinct, specialized type of minion (its own private spawn pool). These special minions are strategically placed by the Dragon, actively hide, and accrue numbers. **Crucially, the survival of these special minions must NOT block the core Level Progression.** The player can choose to hunt them down to weaken the Dragon's final charge, but failing to find them won't break the game.

This separation ensures the Dragon can execute complex staging tactics without ruining the global game flow.

---

## The Complete Master Logic: Unifying Reactive and Proactive Tactics

To truly understand how this architecture pulls everything together, we must look at the **Master Priority Logic** inside the Quest Machine node graph. 

The dragon does not just react to being hit; it proactively "thinks" about its environment. It evaluates Line of Sight (LOS), VR walking space, environmental hazards, and minion positioning to formulate a strategy.

The Quest Machine evaluates variables imported from the environment in a strict hierarchical order: **Survival -> Tactical Control -> Preparation -> Execution**.

Below is the unified Master Logic diagram showing how the dragon makes priority decisions.

```mermaid
graph TD
    Idle[Evaluate AI State] --> Priority1
    
    %% Priority 1: Survival (Reactive)
    subgraph Priority1_Survival
        Priority1{Is Crystal Threatened?}
        Priority1 -- Yes --> Defend[Action: Defend Crystal]
        Priority1 -- No --> ThreatCheck{Health/Stamina Critical?}
        ThreatCheck -- Yes --> EvadeRegen[Action: Evade & Regenerate]
    end
    
    %% Priority 2: Tactical Environmental Control (Proactive)
    subgraph Priority2_Tactical
        ThreatCheck -- No --> CheckSpace{Player has clear LOS <br> OR large walking area?}
        CheckSpace -- Yes --> ToppleCheck{Are pillars/columns <br> available near player?}
        ToppleCheck -- Yes --> Topple[Action: Topple Column <br> to restrict space/vision]
        ToppleCheck -- No --> Clouds[Action: Cast Dark Spirit Clouds]
    end
    
    %% Priority 3: Ambush Preparation (Proactive)
    subgraph Priority3_Preparation
        CheckSpace -- No (Player is Obscured) --> SpawnCheck{Do I have stored <br> waves in hiding?}
        SpawnCheck -- No --> SpawnHidden[Action: Spawn Special Minions near Dragon]
    end
    
    %% Priority 4: Execution
    subgraph Priority4_Execution
        SpawnCheck -- Yes --> FinalCharge[Action: Coordinated Final Charge <br> Swoop + Swarm + Breath]
    end
    
    Defend -->|Action Complete| Idle
    EvadeRegen -->|Action Complete| Idle
    Topple -->|Action Complete| Idle
    Clouds -->|Action Complete| Idle
    SpawnHidden -->|Minions Travel to Hiding Spots| Idle
    FinalCharge -->|Action Complete| Idle
```

### Explaining the Thought Process

To make this logic work, the C# Adapter translates environmental feedback into Quest Machine variables. The Dragon then steps through its priorities.

#### 1. Survival (Reactive)
Before doing anything else, the Dragon must secure its supply lines. 
- **The Evaluation:** The Brain checks `IsCrystalThreatened` and `IsHealthCritical`.
- **The Decision:** If its life or its crystal is in danger, it drops all tactical planning to aggressively **Defend the Crystal** or retreat to **Regenerate**.

#### 2. Tactical Control (Proactive Thinking)
If the Dragon is safe, it begins evaluating the arena geometry to put the player at a disadvantage.
- **The Evaluation:** The Brain checks the spatial variables: `PlayerWalkableArea` and `PlayerHasLineOfSight`.
- **The Decision:** If the player can move freely and see everything, the Dragon decides to strip those advantages. It checks `ColumnsAvailable`. If true, it executes an action to **Topple a Column**, cutting off player movement and creating a visual obstruction. If no columns are left, it casts **Dark Spirit Clouds** to achieve the same result.

#### 3. Ambush Preparation
Once the player's movement and vision are crippled, the Dragon uses that opportunity to prepare an overwhelming assault.
- **The Evaluation:** The Brain confirms the player is restricted (`PlayerWalkableArea < Threshold`). It then checks if enough of its Special Minions have accrued in hiding.
- **The Decision:** If numbers are low, the Dragon executes an action to **Spawn Special Minions**. These spawn near the Dragon, forcing the player to try and shoot them mid-air. If the minions survive the journey, they tuck themselves into inaccessible, hidden locations within the arena geometry, waiting for the command.

#### 4. Coordinated Execution
- **The Evaluation:** The Dragon is healthy, the player is boxed in and blinded, and the Special Minion ambush wave is fully staged and accrued.
- **The Decision:** The conditions are perfect for the climax of the battle. The node transitions to the **Coordinated Final Charge**. The Dragon commands the hidden minion waves to emerge and swarm while simultaneously executing a heavy **Swoop** and firing **Elemental Breath**, catching the restricted player in a devastating, multi-directional crossfire. Surviving this brutal wave is the key to advancing the phase.

### Conclusion

By using this unified priority hierarchy, the Quest Machine node graph pulls all of the disjointed systems together. The C# scripts simply provide the raw data (Sensor: "Pillar is here", "Player is here"). The Node Editor processes that data to form a highly intelligent, proactive tactical strategy, achieving complete decoupling without sacrificing AI depth.
