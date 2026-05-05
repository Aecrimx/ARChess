using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  ChessClock
//
//  RESPONSIBILITY: Count down each side's time and end the game on timeout.
//  Writes WhiteTimeRemaining / BlackTimeRemaining on GameStateManager each
//  Update() so GameplayHUDController picks them up without direct coupling.
//
//  USAGE:
//  • Local modes   — Call StartClock(seconds) from GameModeManager or scene init.
//                    float.MaxValue = unlimited (clock is dormant).
//  • LAN host      — LanNetworkManager calls StartClock(); host is authoritative.
//  • LAN client    — LanNetworkManager calls SetTimesFromServer() each RpcTimerSync.
//
//  SCENE SETUP:
//  Attach to the GameManager GameObject alongside GameStateManager.
// ─────────────────────────────────────────────────────────────────────────────
public class ChessClock : MonoBehaviour
{
    public static ChessClock Instance { get; private set; }

    private bool  _running      = false;
    private bool  _isAuthority  = true;   // false on LAN client (server drives the time)

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        GameEvents.OnGameOver   += _ => _running = false;
        GameEvents.OnBoardReset += OnBoardReset;
    }

    void OnDestroy()
    {
        GameEvents.OnGameOver   -= _ => _running = false;
        GameEvents.OnBoardReset -= OnBoardReset;
    }

    void Update()
    {
        if (!_running || !_isAuthority) return;

        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.Result != GameResult.Ongoing) return;

        // Count down the active side
        if (gsm.IsWhiteTurn)
        {
            if (gsm.WhiteTimeRemaining == float.MaxValue) return; // unlimited
            gsm.WhiteTimeRemaining -= Time.deltaTime;
            if (gsm.WhiteTimeRemaining <= 0f)
            {
                gsm.WhiteTimeRemaining = 0f;
                gsm.ForceGameOver(GameResult.BlackWins);
            }
        }
        else
        {
            if (gsm.BlackTimeRemaining == float.MaxValue) return; // unlimited
            gsm.BlackTimeRemaining -= Time.deltaTime;
            if (gsm.BlackTimeRemaining <= 0f)
            {
                gsm.BlackTimeRemaining = 0f;
                gsm.ForceGameOver(GameResult.WhiteWins);
            }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Start the clock for a new match.
    /// Pass float.MaxValue (or use the parameterless overload) for unlimited.
    /// </summary>
    public void StartClock(float secondsPerSide, bool isAuthority = true)
    {
        _isAuthority = isAuthority;
        _running     = true;

        var gsm = GameStateManager.Instance;
        if (gsm != null)
        {
            gsm.WhiteTimeRemaining = secondsPerSide;
            gsm.BlackTimeRemaining = secondsPerSide;
        }
    }

    /// <summary>Stop and reset the clock (called on rematch / main menu).</summary>
    public void StopClock()
    {
        _running = false;
        var gsm = GameStateManager.Instance;
        if (gsm != null)
        {
            gsm.WhiteTimeRemaining = float.MaxValue;
            gsm.BlackTimeRemaining = float.MaxValue;
        }
    }

    /// <summary>
    /// Called by LanNetworkManager.RpcTimerSync to overwrite times on the client.
    /// The client's clock is not authoritative — it just displays the server's values.
    /// </summary>
    public void SetTimesFromServer(float whiteSeconds, float blackSeconds)
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;
        gsm.WhiteTimeRemaining = whiteSeconds;
        gsm.BlackTimeRemaining = blackSeconds;
    }

    // ── Event handlers ────────────────────────────────────────────────────────
    private void OnBoardReset()
    {
        // Clock is restarted by LanNetworkManager / GameModeManager after a rematch.
        // Simply stop it here so the old time doesn't keep counting.
        _running = false;
    }
}
