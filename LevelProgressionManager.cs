using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orchestrates the high-level progression of the game loop using an event-driven state machine.
/// Handles the core flow: Gong -> Countdown -> Waves -> Boss -> Victory -> Next Level.
/// Completely avoids Coroutines in favor of simple Update timers.
/// </summary>
public class LevelProgressionManager : MonoBehaviour
{
    public enum GameState
    {
        Initialize,
        WaitingForGong,
        Countdown,
        WaveActive,
        BossActive,
        LevelVictory,
        CampaignComplete
    }

    [Header("Campaign Configuration")]
    [Tooltip("The ordered list of levels to play through.")]
    public List<LevelConfigSO> levelPlaylist = new List<LevelConfigSO>();

    [Header("Settings")]
    public float countdownDuration = 3.0f;
    public float victoryDelay = 5.0f;

    // --- State & Tracking ---
    private GameState currentState = GameState.Initialize;
    private int currentLevelIndex = 0;
    private int currentWaveIndex = 0;
    private float timer = 0f;

    // -------------------------------------------------------------------------
    // Public Events
    // -------------------------------------------------------------------------

    /// Fired whenever the state machine transitions.
    public event Action<GameState> OnStateChanged;

    /// Fired when a level is ready to begin — player must hit the Gong.
    /// Passes the level about to start so UI can show its intro.
    public event Action<LevelConfigSO> OnLevelIntroReady;

    /// Fired when a level ends.
    /// currentLevel = the level just finished.
    /// nextLevel    = the level coming next (null if this was the last level).
    /// UI combines both into a single screen so the player only needs ONE gong hit.
    public event Action<LevelConfigSO, LevelConfigSO> OnLevelOutroReady;

    /// Fired when a wave is about to start (during the pre-wave countdown).
    /// UI should display the wave name, objective type, and any announcement text.
    /// waveData    = the upcoming wave's data.
    /// waveNumber  = 1-based index (e.g. 2 of 4).
    /// totalWaves  = total waves in the current level.
    public event Action<WaveData, int, int> OnWaveAnnounced;

    /// Fired every frame during Countdown state — value counts DOWN to 0.
    /// Routes to the HUD (small text), not the big announcer.
    public event Action<float> OnCountdownUpdated;

    /// Fired when the campaign is fully complete.
    public event Action OnCampaignComplete;

    /// Fired to WaveSpawner when it should start spawning.
    public event Action<LevelConfigSO, int> OnWaveStartRequested;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        if (levelPlaylist.Count == 0)
        {
            Debug.LogError("[LevelProgressionManager] No levels in playlist!");
            ChangeState(GameState.CampaignComplete);
            return;
        }

        PrepareLevel(0);
    }

    private void Update()
    {
        switch (currentState)
        {
            case GameState.Countdown:
                HandleCountdownState();
                break;
            case GameState.LevelVictory:
                HandleVictoryState();
                break;
        }
    }

    // -------------------------------------------------------------------------
    // State Machine
    // -------------------------------------------------------------------------

    private void ChangeState(GameState newState)
    {
        currentState = newState;
        Debug.Log($"[LevelProgressionManager] State → {newState}");
        OnStateChanged?.Invoke(currentState);
    }

    /// <summary>
    /// Sets up a level and waits for the player to hit the gong.
    /// </summary>
    private void PrepareLevel(int levelIndex)
    {
        if (levelIndex >= levelPlaylist.Count)
        {
            OnCampaignComplete?.Invoke();
            ChangeState(GameState.CampaignComplete);
            return;
        }

        currentLevelIndex = levelIndex;
        currentWaveIndex = 0;

        LevelConfigSO currentLevel = levelPlaylist[currentLevelIndex];
        OnLevelIntroReady?.Invoke(currentLevel);

        ChangeState(GameState.WaitingForGong);
    }

    // -------------------------------------------------------------------------
    // External Inputs
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by LevelAdvanceGong when the gong is struck.
    /// Announces wave 1 immediately so the player can read the objective during the countdown.
    /// </summary>
    public void ReceiveGongHit()
    {
        if (currentState != GameState.WaitingForGong) return;

        Debug.Log("[LevelProgressionManager] Gong hit. Starting countdown.");

        // Announce the first wave NOW so the player can read it during the 3s countdown.
        AnnounceWave(currentWaveIndex);

        timer = countdownDuration;
        ChangeState(GameState.Countdown);
    }

    /// <summary>
    /// Called by WaveSpawner if it detects a Boss Config in the wave data.
    /// </summary>
    public void NotifyBossWaveStarted()
    {
        if (currentState == GameState.WaveActive)
        {
            ChangeState(GameState.BossActive);
        }
    }

    /// <summary>
    /// Called by WaveSpawner when all targets for a standard wave are destroyed.
    /// </summary>
    public void ReceiveWaveCompleted()
    {
        if (currentState != GameState.WaveActive && currentState != GameState.BossActive) return;

        currentWaveIndex++;
        Debug.Log($"[LevelProgressionManager] Wave completed. Moving to wave index {currentWaveIndex}.");

        LevelConfigSO currentLevel = levelPlaylist[currentLevelIndex];

        if (currentWaveIndex < currentLevel.waves.Count)
        {
            // Announce the NEXT wave at the very start of the inter-wave pause,
            // so the player has the full 2.5 seconds to read the upcoming objective.
            AnnounceWave(currentWaveIndex);

            timer = 2.5f;
            ChangeState(GameState.Countdown);
        }
        else
        {
            TriggerLevelVictory();
        }
    }

    /// <summary>
    /// Called by BossCreature (via WaveSpawner) when the boss is defeated.
    /// </summary>
    public void ReceiveBossDefeated()
    {
        if (currentState == GameState.BossActive || currentState == GameState.WaveActive)
        {
            TriggerLevelVictory();
        }
    }

    // -------------------------------------------------------------------------
    // Internal Helpers
    // -------------------------------------------------------------------------

    private void HandleCountdownState()
    {
        timer -= Time.deltaTime;
        OnCountdownUpdated?.Invoke(Mathf.Max(0, timer));

        if (timer <= 0)
        {
            StartNextWave();
        }
    }

    private void StartNextWave()
    {
        LevelConfigSO currentLevel = levelPlaylist[currentLevelIndex];

        if (currentWaveIndex < currentLevel.waves.Count)
        {
            bool isBossWave = false;
            foreach (var character in currentLevel.waves[currentWaveIndex].characters)
            {
                if (character is BossConfig) { isBossWave = true; break; }
            }

            ChangeState(GameState.WaveActive);
            OnWaveStartRequested?.Invoke(currentLevel, currentWaveIndex);

            if (isBossWave)
            {
                Debug.Log($"[LevelProgressionManager] Boss wave {currentWaveIndex + 1} starting.");
                NotifyBossWaveStarted();
            }
            else
            {
                Debug.Log($"[LevelProgressionManager] Standard wave {currentWaveIndex + 1}/{currentLevel.waves.Count} starting.");
            }
        }
        else
        {
            TriggerLevelVictory();
        }
    }

    private void TriggerLevelVictory()
    {
        LevelConfigSO currentLevel = levelPlaylist[currentLevelIndex];
        LevelConfigSO nextLevel = (currentLevelIndex + 1 < levelPlaylist.Count)
            ? levelPlaylist[currentLevelIndex + 1]
            : null;

        // Pass both levels so UI can compose a single screen:
        // "Level cleared! | Next: [name] [intro] | Shoot the Gong"
        OnLevelOutroReady?.Invoke(currentLevel, nextLevel);

        timer = victoryDelay;
        ChangeState(GameState.LevelVictory);
    }

    private void HandleVictoryState()
    {
        timer -= Time.deltaTime;

        if (timer <= 0)
        {
            PrepareLevel(currentLevelIndex + 1);
        }
    }

    /// <summary>
    /// Fires OnWaveAnnounced for the wave at the given index.
    /// Safe to call even if the index is out of range (no-op).
    /// </summary>
    private void AnnounceWave(int waveIndex)
    {
        LevelConfigSO currentLevel = levelPlaylist[currentLevelIndex];
        if (waveIndex < 0 || waveIndex >= currentLevel.waves.Count) return;

        WaveData wave = currentLevel.waves[waveIndex];
        int totalWaves = currentLevel.waves.Count;
        OnWaveAnnounced?.Invoke(wave, waveIndex + 1, totalWaves);

        Debug.Log($"[LevelProgressionManager] Announced wave {waveIndex + 1}/{totalWaves}" +
                  $"{(string.IsNullOrEmpty(wave.waveName) ? "" : $" — {wave.waveName}")}");
    }
}
