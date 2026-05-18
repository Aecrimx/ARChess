using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  ChessClock
//
//  RESPONSIBILITY: Count down each side's time and end the game on timeout.
//  Writes WhiteTimeRemaining / BlackTimeRemaining on GameStateManager each
//  Update() so GameplayHUDController can render the latest values.
//
//  USAGE:
//  • Local modes — GameModeManager starts the clock directly.
//  • LAN host    — LanNetworkManager starts the authoritative clock.
//  • LAN client  — RpcTimerSync keeps the local display aligned with the host.
// —————————————————————————————————————————————————————————————————————————————
public class ChessClock : MonoBehaviour
{
    public static ChessClock Instance { get; private set; }

    private bool  _running      = false;
    private bool  _isAuthority  = true;   // false on LAN client (server drives the time)

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    void Start()
    {
        GameEvents.OnGameOver += HandleGameOver;
        GameEvents.OnBoardReset += OnBoardReset;
    }

    void OnDestroy()
    {
        GameEvents.OnGameOver -= HandleGameOver;
        GameEvents.OnBoardReset -= OnBoardReset;

        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (!_running) return;

        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.Result != GameResult.Ongoing) return;

        // Count down the active side
        if (gsm.IsWhiteTurn)
        {
            if (gsm.WhiteTimeRemaining == float.MaxValue) return; // unlimited
            gsm.WhiteTimeRemaining -= Time.deltaTime;
            if (_isAuthority && gsm.WhiteTimeRemaining <= 0f)
            {
                gsm.WhiteTimeRemaining = 0f;
                gsm.ForceGameOver(GameResult.BlackWinsOnTime);
            }
        }
        else
        {
            if (gsm.BlackTimeRemaining == float.MaxValue) return; // unlimited
            gsm.BlackTimeRemaining -= Time.deltaTime;
            if (_isAuthority && gsm.BlackTimeRemaining <= 0f)
            {
                gsm.BlackTimeRemaining = 0f;
                gsm.ForceGameOver(GameResult.WhiteWinsOnTime);
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

    public void SetTimesFromServer(float whiteSeconds, float blackSeconds)
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        _isAuthority = false;
        _running = true;
        gsm.WhiteTimeRemaining = whiteSeconds;
        gsm.BlackTimeRemaining = blackSeconds;
    }

    private void HandleGameOver(GameResult _)
    {
        _running = false;
    }

    private void OnBoardReset()
    {
        _running = false;
    }
}
