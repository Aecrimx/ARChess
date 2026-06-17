# LAN Network Sequences

The current LAN flow uses Mirror with a server-authoritative chess state. The host is always assigned white, the joining client is assigned black, and both clients report scene readiness before the match clock starts.

## Startup Sequence

```mermaid
sequenceDiagram
    actor HostPlayer as Host player
    actor ClientPlayer as Joining player
    participant MenuH as MainMenuController host
    participant LNM as LanNetworkManager host/server
    participant LD as LanDiscovery
    participant MenuC as MainMenuController client
    participant CNP_H as ChessNetworkProxy host
    participant CNP_C as ChessNetworkProxy client
    participant GSM as GameStateManager
    participant Clock as ChessClock
    participant VMC as ChessViewModeController

    HostPlayer->>MenuH: Start Hosting
    MenuH->>LNM: Set HostPlayerName and TimerSeconds
    MenuH->>LNM: StartHost()
    MenuH->>LD: AdvertiseServer()

    ClientPlayer->>MenuC: Join Game / Refresh
    MenuC->>LD: StartDiscovery() and discovery burst
    LD-->>MenuC: OnServerDiscovered(host, timer, uri)
    ClientPlayer->>MenuC: Join discovered host
    MenuC->>LNM: StartClient(uri)

    LNM->>LNM: OnServerAddPlayer for each connection
    LNM->>CNP_H: Assign IsWhite = true
    LNM->>CNP_C: Assign IsWhite = false
    LNM->>LD: StopDiscovery()
    LNM->>LNM: ServerChangeScene("ChessScene")

    CNP_H->>LNM: CmdReportSceneReady()
    CNP_C->>LNM: CmdReportSceneReady()
    LNM->>GSM: InitBoard(TimerSeconds)
    LNM->>Clock: StartClock(TimerSeconds, authority=true)
    LNM->>CNP_H: TargetGameStarted(white, timer)
    LNM->>CNP_C: TargetGameStarted(black, timer)
    CNP_H->>VMC: ConfigureMatchContext(white, proxy)
    CNP_C->>VMC: ConfigureMatchContext(black, proxy)
```

## Move Sequence With Prediction

```mermaid
sequenceDiagram
    actor Player as Local player
    participant Input as 2D or AR input handler
    participant MIC as ChessMoveInteractionController
    participant CNP_C as ChessNetworkProxy owning client
    participant GSM_C as GameStateManager client
    participant CNP_S as ChessNetworkProxy server
    participant GSM_S as GameStateManager server
    participant CNP_All as Remote client proxies
    participant View as Renderers and HUD

    Player->>Input: Select piece and destination
    Input->>MIC: Pointer down/enter/up square
    MIC->>GSM_C: Check turn and legal moves

    alt Host submits
        MIC->>CNP_C: CmdRequestMove(...)
    else Remote client submits
        MIC->>CNP_C: TrySubmitPredictedLocalMove(...)
        CNP_C->>GSM_C: TakeSnapshot()
        CNP_C->>GSM_C: TryApplyMove(...)
        GSM_C-->>View: GameEvents update local view immediately
        CNP_C->>CNP_S: CmdRequestMove(...)
    end

    CNP_S->>GSM_S: Check color turn ownership
    CNP_S->>GSM_S: TryApplyMove(...)

    alt Move accepted
        GSM_S-->>View: Server-side GameEvents
        CNP_S->>CNP_All: RpcApplyMove(...)
        CNP_All->>GSM_C: TryApplyMove(...) if not already predicted
        GSM_C-->>View: GameEvents redraw board/HUD
    else Move rejected
        CNP_S->>CNP_C: TargetMoveRejected(...)
        CNP_C->>GSM_C: RestoreSnapshot(...)
        CNP_C->>View: RefreshAllViews()
    end
```

## Timer And Disconnect Notes

- `LanNetworkManager.Update()` sends `RpcTimerSync` from the authoritative host about every two seconds.
- LAN clients run `ChessClock` as non-authority and use server timer values for display.
- If a remote opponent disconnects during an active LAN match, `GameStateManager.ForceGameOver(GameResult.OpponentDisconnected)` ends the match.
- The host can request a rematch from `GameOverOverlay`; the server resets the board, restarts the authoritative clock, reapplies color assignments, and sends `TargetGameStarted` again.
