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
//  Reads PlayerPrefs key "GameMode":
//    "vsAI"    → single player (AI opponent active)
//    "local2P" → local two player (AI inactive)
// ─────────────────────────────────────────────────────────────────────────────
public class GameModeManager : MonoBehaviour
{
    public enum GameMode { VsAI, Local2Player }

    public static GameModeManager Instance { get; private set; }

    public GameMode CurrentMode { get; private set; } = GameMode.Local2Player;

    [Header("Scene Names")]
    public string mainMenuSceneName = "MainMenu";

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        ReadGameMode();
    }

    private void ReadGameMode()
    {
        string mode = PlayerPrefs.GetString("GameMode", "local2P");
        CurrentMode = mode == "vsAI" ? GameMode.VsAI : GameMode.Local2Player;
        Debug.Log($"[GameModeManager] Mode: {CurrentMode}");
    }

    /// <summary>Called by pause menu or game-over screen to go back to main menu.</summary>
    public void ReturnToMainMenu()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }

    /// <summary>Is this a single-player game against the AI?</summary>
    public bool IsVsAI => CurrentMode == GameMode.VsAI;
}