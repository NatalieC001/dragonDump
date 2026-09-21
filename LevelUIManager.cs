using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// Subscribes to LevelProgressionManager and WaveSpawner events to update all player-facing UI.
///
/// VR BEST PRACTICES — why this file is structured the way it is:
///
///   1. World-Space text only.
///      levelFeedbackText and hudText are TMP_Text components placed directly in the 3D scene
///      (not on a Screen-Space Canvas). Never add a Screen-Space overlay to a VR project.
///
///   2. DOTween for all text transitions.
///      Instant text changes are jarring in VR. Every show/hide uses a fade so the player's
///      eye is drawn to the change rather than startled by it.
///
///   3. Countdown fires every frame — guarded with lastCountdownSecond.
///      Only reacts when the integer second changes. Prevents DOTween from being spammed
///      60 times per second while the timer counts down.
///
///   4. Scale punch on countdown ticks.
///      A subtle transform scale punch (DOPunchScale) on each new second makes the countdown
///      legible at VR viewing distances without relying on font size alone.
///
///   5. UpdateLiveWaveHUD fires every frame — text only set when content changes.
///      Avoids unnecessary DOM/rendering work by caching the last HUD string.
///
///   6. DOKill() before every new tween.
///      Prevents overlapping/stacked animations when two events fire in quick succession
///      (e.g. state change + wave announcement arriving in the same frame).
///
/// TEXT ELEMENT RESPONSIBILITIES:
///   levelFeedbackText  — Big world-space announcer. Level intros/outros, wave titles, boss warnings.
///                        Only changes at meaningful moments. Fades in slowly, fades out on wave start.
///   hudText            — Smaller persistent HUD. Countdown integers during transitions,
///                        then "Targets Left: X" / "Survive: Xs" during active waves.
///
/// These two elements NEVER overwrite each other.
/// </summary>
public class LevelUIManager : MonoBehaviour
{
    [Header("Manager References (assign in Inspector — fallback to scene search)")]
    public LevelProgressionManager progressionManager;
    public WaveSpawner waveSpawner;

    [Header("World-Space TMP Text Elements")]
    [Tooltip("Large scene-placed TMP text — level intros, wave announcements, victory screen.")]
    public TMP_Text levelFeedbackText;

    [Tooltip("Smaller scene-placed TMP text — countdown integers and live wave stats.")]
    public TMP_Text hudText;

    [Header("VR Transition Timing")]
    [Tooltip("How long the big announcer takes to fade IN (seconds). 0.6–1.0 feels natural in VR.")]
    public float announcerFadeInDuration = 0.7f;

    [Tooltip("How long the big announcer takes to fade OUT (seconds). Slightly faster than fade-in.")]
    public float announcerFadeOutDuration = 0.4f;

    [Tooltip("How long the HUD text takes to fade in/out (seconds). Keep short for live feedback.")]
    public float hudFadeDuration = 0.25f;

    [Tooltip("Scale punch amount for countdown ticks. Subtle (0.12–0.2) reads well at VR distance.")]
    public float countdownPunchScale = 0.15f;

    // -------------------------------------------------------------------------
    // Private State
    // -------------------------------------------------------------------------

    /// Tracks last shown countdown second so we don't re-tween every frame.
    private int lastCountdownSecond = -1;

    /// Tracks last HUD string to avoid redundant SetText calls every frame.
    private string lastHUDString = "";

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        // Inspector assignment is preferred — these are fallbacks for convenience.
        if (progressionManager == null)
            progressionManager = FindFirstObjectByType<LevelProgressionManager>();

        if (waveSpawner == null)
            waveSpawner = FindFirstObjectByType<WaveSpawner>();

        // Start both text elements invisible so the first fade-in feels intentional.
        if (levelFeedbackText != null) levelFeedbackText.alpha = 0f;
        if (hudText != null)           hudText.alpha = 0f;
    }

    private void OnEnable()
    {
        if (progressionManager != null)
        {
            progressionManager.OnStateChanged     += HandleStateChanged;
            progressionManager.OnLevelIntroReady  += DisplayLevelIntro;
            progressionManager.OnLevelOutroReady  += DisplayLevelOutro;
            progressionManager.OnWaveAnnounced    += DisplayWaveAnnouncement;
            progressionManager.OnCountdownUpdated += UpdateCountdownHUD;
            progressionManager.OnCampaignComplete += DisplayCampaignComplete;
        }

        if (waveSpawner != null)
            waveSpawner.OnWaveProgressUpdated += UpdateLiveWaveHUD;
    }

    private void OnDisable()
    {
        if (progressionManager != null)
        {
            progressionManager.OnStateChanged     -= HandleStateChanged;
            progressionManager.OnLevelIntroReady  -= DisplayLevelIntro;
            progressionManager.OnLevelOutroReady  -= DisplayLevelOutro;
            progressionManager.OnWaveAnnounced    -= DisplayWaveAnnouncement;
            progressionManager.OnCountdownUpdated -= UpdateCountdownHUD;
            progressionManager.OnCampaignComplete -= DisplayCampaignComplete;
        }

        if (waveSpawner != null)
            waveSpawner.OnWaveProgressUpdated -= UpdateLiveWaveHUD;

        // Clean up any running tweens when disabled.
        KillAllTweens();
    }

    private void OnDestroy()
    {
        KillAllTweens();
    }

    // -------------------------------------------------------------------------
    // State Change — clear stale text with fade
    // -------------------------------------------------------------------------

    private void HandleStateChanged(LevelProgressionManager.GameState state)
    {
        bool waveIsActive = state == LevelProgressionManager.GameState.WaveActive
                         || state == LevelProgressionManager.GameState.BossActive;

        if (waveIsActive)
        {
            // Fade out the big announcer — gameplay is now live, HUD takes over.
            FadeOutAnnouncer();
            lastCountdownSecond = -1;
        }

        // Clear HUD when returning to a non-wave, non-countdown state.
        if (!waveIsActive && state != LevelProgressionManager.GameState.Countdown)
        {
            FadeOutHUD();
            lastHUDString = "";
        }
    }

    // -------------------------------------------------------------------------
    // Level intro / outro
    // -------------------------------------------------------------------------

    private void DisplayLevelIntro(LevelConfigSO levelConfig)
    {
        ShowAnnouncer(
            $"<b>{levelConfig.levelName}</b>\n" +
            $"{levelConfig.levelIntroText}\n\n" +
            $"<size=70%>Shoot the Gong to begin!</size>"
        );
        FadeOutHUD();
    }

    /// <summary>
    /// Combines the current level's outro with the next level's intro into one world-space screen,
    /// so the player only needs a SINGLE gong hit — no double-gong confusion.
    /// </summary>
    private void DisplayLevelOutro(LevelConfigSO currentLevel, LevelConfigSO nextLevel)
    {
        if (nextLevel != null)
        {
            ShowAnnouncer(
                $"{currentLevel.levelOutroText}\n" +
                $"<size=70%>✓ Level Cleared!</size>\n\n" +
                $"<size=90%><b>Next:</b> {nextLevel.levelName}\n" +
                $"{nextLevel.levelIntroText}</size>\n\n" +
                $"<size=65%>Shoot the Gong when ready!</size>"
            );
        }
        else
        {
            // Last level — OnCampaignComplete fires separately with the final message.
            ShowAnnouncer(
                $"{currentLevel.levelOutroText}\n" +
                $"<size=70%>✓ Level Cleared!</size>"
            );
        }

        FadeOutHUD();
    }

    // -------------------------------------------------------------------------
    // Wave announcement — shown during the pre-wave countdown
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shown on the big world-space announcer as soon as the pre-wave countdown begins.
    /// The player has the full countdown duration to read the objective before the wave fires.
    /// </summary>
    private void DisplayWaveAnnouncement(WaveData wave, int waveNumber, int totalWaves)
    {
        bool isBossWave = false;
        foreach (var c in wave.characters)
        {
            if (c is BossConfig) { isBossWave = true; break; }
        }

        string objective = wave.progressionType == WaveProgressionType.TimeBased
            ? $"Survive for <b>{wave.waveDuration:F0}s</b>"
            : "Clear <b>all targets</b>";

        string nameLine = !string.IsNullOrEmpty(wave.waveName)
            ? $"<b>{wave.waveName}</b>\n"
            : "";

        string announcementLine = !string.IsNullOrEmpty(wave.waveAnnouncementText)
            ? $"\n<size=75%>{wave.waveAnnouncementText}</size>"
            : "";

        string waveLabel = isBossWave
            ? $"<color=#FF6B35>⚠ BOSS — Wave {waveNumber}/{totalWaves}</color>"
            : $"Wave {waveNumber}/{totalWaves}";

        ShowAnnouncer(
            $"{waveLabel}\n" +
            $"{nameLine}" +
            $"<size=80%>{objective}</size>" +
            $"{announcementLine}"
        );
    }

    // -------------------------------------------------------------------------
    // Countdown HUD — fires every frame, only reacts on integer change
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called every frame while the countdown is running.
    /// Only updates the HUD text when the integer second changes, preventing DOTween spam.
    /// Each new second gets a subtle scale punch for VR legibility at distance.
    /// </summary>
    private void UpdateCountdownHUD(float timeLeft)
    {
        int secondsLeft = Mathf.CeilToInt(timeLeft);
        if (secondsLeft == lastCountdownSecond) return; // no change this frame
        lastCountdownSecond = secondsLeft;

        if (hudText == null) return;

        string newText = secondsLeft > 0
            ? $"Starting in <b>{secondsLeft}</b>..."
            : "<b>GO!</b>";

        // Kill existing tween, snap alpha up, set text, punch scale.
        hudText.DOKill();
        hudText.alpha = 1f;
        hudText.text  = newText;

        // Scale punch: makes each countdown tick 'pop' in the player's peripheral vision.
        hudText.transform.DOKill();
        hudText.transform.DOPunchScale(
            Vector3.one * countdownPunchScale,
            duration:  0.35f,
            vibrato:   2,
            elasticity: 0.5f
        ).SetLink(hudText.gameObject);

        if (secondsLeft <= 0)
        {
            // "GO!" fades out after a brief pause so it doesn't linger.
            hudText.DOFade(0f, hudFadeDuration)
                   .SetDelay(0.6f)
                   .SetLink(hudText.gameObject)
                   .OnComplete(() => { lastHUDString = ""; lastCountdownSecond = -1; });
        }
    }

    // -------------------------------------------------------------------------
    // Live wave HUD — targets remaining / time left
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired by WaveSpawner every frame during an active wave.
    /// Cached against lastHUDString to avoid redundant TMP redraws.
    /// Routes only to the small HUD element — announcer is untouched.
    /// </summary>
    private void UpdateLiveWaveHUD(string text)
    {
        if (text == lastHUDString) return;
        lastHUDString = text;

        if (hudText == null) return;

        // For live game stats, update the text directly (no tween delay — responsiveness matters).
        hudText.DOKill();
        hudText.alpha = 1f;
        hudText.text  = text;
    }

    // -------------------------------------------------------------------------
    // Campaign complete
    // -------------------------------------------------------------------------

    private void DisplayCampaignComplete()
    {
        ShowAnnouncer(
            "<b>Campaign Complete!</b>\n" +
            "<size=80%>You've mastered the Archery Range.\nThanks for playing!</size>"
        );
        FadeOutHUD();
    }

    // -------------------------------------------------------------------------
    // VR-safe TMP helpers — fade-based, DOKill-guarded, SetLink-safe
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows new text on the big world-space announcer with a smooth fade-in.
    /// Kills any running tween first to prevent overlaps.
    /// </summary>
    private void ShowAnnouncer(string text)
    {
        if (levelFeedbackText == null) return;

        levelFeedbackText.DOKill();
        levelFeedbackText.alpha = 0f;
        levelFeedbackText.text  = text;

        levelFeedbackText.DOFade(1f, announcerFadeInDuration)
                         .SetEase(Ease.OutCubic)
                         .SetLink(levelFeedbackText.gameObject);
    }

    /// <summary>
    /// Fades the big announcer out. Does NOT clear the text immediately —
    /// the text stays set so if something fades it back in the content is still there.
    /// </summary>
    private void FadeOutAnnouncer()
    {
        if (levelFeedbackText == null) return;

        levelFeedbackText.DOKill();
        levelFeedbackText.DOFade(0f, announcerFadeOutDuration)
                         .SetEase(Ease.InCubic)
                         .SetLink(levelFeedbackText.gameObject);
    }

    /// <summary>
    /// Fades the HUD element out smoothly.
    /// </summary>
    private void FadeOutHUD()
    {
        if (hudText == null) return;

        hudText.DOKill();
        hudText.transform.DOKill();
        hudText.DOFade(0f, hudFadeDuration)
               .SetLink(hudText.gameObject);

        lastHUDString = "";
        lastCountdownSecond = -1;
    }

    private void KillAllTweens()
    {
        if (levelFeedbackText != null)
        {
            levelFeedbackText.DOKill();
            levelFeedbackText.transform.DOKill();
        }
        if (hudText != null)
        {
            hudText.DOKill();
            hudText.transform.DOKill();
        }
    }
}
