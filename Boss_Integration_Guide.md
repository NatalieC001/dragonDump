# Boss AI & Path Registry Integration Guide

> [!info] The Goal
> This guide explains how to transition your existing Bosses (like the Dragon) and their paths to the new **Inheritance-Based Movement** and **Global Path Registry** systems, and how to attach the new manager to your central Level Progression object.

---

### Step 1: Add the Boss Path Manager to the Scene
The Bosses need a single source of truth to ask for their paths. We will add this to the same central manager object we created for the level progression.

1. In your Unity Hierarchy, select the `[MANAGER] _LevelProgression_NEW` object you created previously.
2. Click **Add Component** in the Inspector.
3. Add the `BossPathManager.cs` script (from the `BossAISandbox` folder).
   * *Note: You do not need to configure anything in this script; it automatically manages lists of paths at runtime.*

---

### Step 2: Tag Your Path Prefabs
Your paths (observation splines, escape routes) need to know if they are meant for flying creatures or walking creatures.

1. Open your Project window and find your **Path Prefabs** (e.g., `Assets/Prefabs/Paths/`).
2. Select an Airborne path prefab (e.g., a sky circling route).
3. Click **Add Component** and add `PathTypeTag.cs`.
4. In the Inspector for `PathTypeTag`:
   - Set **Path Type** to `Airborne`.
   - Check **Is Escape Route** ONLY if this is a path meant for fleeing. Leave unchecked if it is an observation path.
5. Repeat this for your Ground/Terrestrial paths, setting the **Path Type** to `Terrestrial`.

> [!warning] Critical Step
> If a path does not have this tag, the `BossPathManager` will default it to an Airborne Observation path and throw a warning in the console!

---

### Step 3: Upgrade the Dragon (or other Bosses)
We need to remove the old prototype wiring and give the Dragon the new movement brain.

1. Open your **Dragon Boss Prefab**.
2. **Remove** any old movement scripts (like a massive God-class controller, or anything hardcoded to the old `BossArenaManager`).
3. Click **Add Component** and add the new `AirborneBossMovement.cs` script.
   * *Because the Dragon is purely a flying creature, it uses `AirborneBossMovement`. If you had a humanoid walker boss, you would use `GroundBossMovement.cs`. If you had a dragon that lands and walks, you would use `HybridBossMovement.cs`.*
4. The movement script will automatically locate the `BossPathManager` in the scene via `Awake()` and handle its own path requests. No manual dragging of paths is required!

---

### Step 4: Verify the Flow
1. Ensure your `LevelConfigSO` has the properly tagged path prefabs assigned in its `observationPathPrefabs` and `escapePathPrefabs` lists.
2. Press Play.
3. When the Boss Wave starts, the `WaveSpawner` will silently instantiate your tagged paths.
4. The paths will register themselves with the `BossPathManager`.
5. The Dragon will spawn, its `AirborneBossMovement` script will ask the manager for an `Airborne` observation path, and the fight will begin seamlessly!

### Step 5: The Dragon Prefab Hierarchy (Blueprint)
To ensure the Dragon functions perfectly with the new movement system and your existing combat logic, its prefab hierarchy and script attachments must look exactly like this:

```text
🐉 [Prefab Root] Dragon_Boss
 ├── 📜 AirborneBossMovement.cs       (The new brain: queries paths & dictates movement)
 ├── 📜 BossCreature.cs               (The core health/combat state machine)
 ├── 📜 SegmentedDragonManager.cs     (Manages the list of child segments)
 ├── 📜 DragonSpacingManager.cs       (Handles the dynamic lerping/gaps)
 ├── 📜 DragonMovementManager.cs      (Handles the breadcrumb trail following)
 ├── 📜 SplineFollower.cs             (Required to traverse observation/escape paths)
 ├── 📜 CreatureStatusEffects.cs      (Required for elemental arrow reactions like Ice)
 │
 ├── 🧩 [Child] Head_Segment
 │    ├── 📜 PermanentDragonSegment.cs (Invulnerable, passes damage to main BossCreature)
 │    ├── 📜 Collider (Layer: Enemy)
 │    ├── 🎨 Mesh_Visual
 │    └── 🎯 MouthTransform            (Empty transform facing forward for breath attacks)
 │
 ├── 🧩 [Child] Body_Segment_1 (Destructible)
 │    ├── 📜 DragonSegment.cs          (Can be destroyed to trigger dissolve)
 │    ├── 📜 DissolveEffect.cs
 │    ├── 📜 Collider (Layer: Enemy)
 │    └── 🎨 Mesh_Visual
 │
 ├── 🧩 [Child] Body_Segment_2 (Destructible)
 │    ├── 📜 DragonSegment.cs
 │    ├── 📜 DissolveEffect.cs
 │    ├── 📜 Collider (Layer: Enemy)
 │    └── 🎨 Mesh_Visual
 │
 └── 🧩 [Child] Tail_Segment
      ├── 📜 PermanentDragonSegment.cs (Invulnerable)
      ├── 📜 Collider (Layer: Enemy)
      └── 🎨 Mesh_Visual
```

> [!tip] Cleanup
> You can now safely delete the old `BossArenaManager.cs` from your scene, as it was only meant for prototype testing.