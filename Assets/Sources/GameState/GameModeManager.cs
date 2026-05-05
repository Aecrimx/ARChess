using UnityEngine;
using UnityEngine.SceneManagement;

// ─────────────────────────────────────────────────────────────────────────────
//  GameModeManager
//
//  RESPONSIBILITY: Read the game mode chosen on the main menu and configure
//  the game scene accordingly. Also handles returning to the main menu.
//
//  SCENE SETUP:
//  Attach to GameManager in your game scene alongside GameStateManager.
//
//  Reads PlayerPrefs keys:
//    "GameMode"    → "vsAI" | "local2P" | "lanHost" | "lanClient"
//    "PlayerName"  → player's display name (optional, set in Settings)
//    "TimerPreset" → "unlimited" | "1" | "3" | "5" | "10" | "30" (minutes)
// ─────────────────────────────────────────────────────────────────────────────
public class GameModeManager : MonoBehaviour
{
    public enum GameMode { VsAI, Local2Player, LanHost, LanClient }

    public static GameModeManager Instance { get; private set; }

    public GameMode CurrentMode { get; private set; } = GameMode.Local2Player;

    /// <summary>Display name of the local player (from Settings).</summary>
    public string LocalPlayerName { get; private set; } = "Player";

    /// <summary>Match time per side in seconds. float.MaxValue = unlimited.</summary>
    public float TimerSeconds { get; private set; } = float.MaxValue;

    [Header("Scene Names")]
    public string mainMenuSceneName = "MainMenu";

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        ReadSettings();
    }

    private void ReadSettings()
    {
        // Game mode
        string mode = PlayerPrefs.GetString("GameMode", "local2P");
        CurrentMode = mode switch
        {
            "vsAI"      => GameMode.VsAI,
            "lanHost"   => GameMode.LanHost,
            "lanClient" => GameMode.LanClient,
            _           => GameMode.Local2Player
        };

        // Player name
        LocalPlayerName = PlayerPrefs.GetString("PlayerName", "Player");

        // Timer
        string timerKey = PlayerPrefs.GetString("TimerPreset", "unlimited");
        TimerSeconds = timerKey switch
        {
            "1"  => 60f,
            "3"  => 180f,
            "5"  => 300f,
            "10" => 600f,
            "30" => 1800f,
            _    => float.MaxValue   // "unlimited"
        };

        Debug.Log($"[GameModeManager] Mode: {CurrentMode} | Timer: {TimerSeconds}s | Name: {LocalPlayerName}");
    }

    /// <summary>Called by pause menu or game-over screen to go back to main menu.</summary>
    public void ReturnToMainMenu()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }

    /// <summary>Is this a single-player game against the AI?</summary>
    public bool IsVsAI     => CurrentMode == GameMode.VsAI;

    /// <summary>Is this any kind of LAN game?</summary>
    public bool IsLan      => CurrentMode == GameMode.LanHost || CurrentMode == GameMode.LanClient;

    /// <summary>Is this device the LAN host (server)?</summary>
    public bool IsLanHost  => CurrentMode == GameMode.LanHost;

    /// <summary>Is this device the LAN client?</summary>
    public bool IsLanClient => CurrentMode == GameMode.LanClient;

    /// <summary>
    /// Convert a PlayerPrefs timer preset string to seconds.
    /// Can be called without an instance (e.g. from MainMenuController).
    /// </summary>
    public static float SecondsFromPreset(string preset) => preset switch
    {
        "1"  => 60f,
        "3"  => 180f,
        "5"  => 300f,
        "10" => 600f,
        "30" => 1800f,
        _    => float.MaxValue
    };
}