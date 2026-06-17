# ARChess Architecture

This document reflects the current repository structure. The application is split into a Unity game client and a Python AI service. The Unity client owns gameplay state and presentation; the service owns Stockfish/Gemini work.

## Unity Client

```mermaid
flowchart TD
    subgraph Menu["Main menu and setup"]
        MMC["MainMenuController"]
        GMM["GameModeManager"]
        Prefs["PlayerPrefs settings"]
    end

    subgraph Core["Game state and rules"]
        GSM["GameStateManager"]
        GE["GameEvents"]
        CNE["ChessNotationExporter"]
        Clock["ChessClock"]
    end

    subgraph Input["Move interaction"]
        MIC["ChessMoveInteractionController"]
        I2D["Chess2DInputHandler"]
        IAR["ChessARInputHandler"]
        Promo["PawnPromotionPicker"]
    end

    subgraph View["Rendering and HUD"]
        VMC["ChessViewModeController"]
        R2D["Chess2DRenderer"]
        RAR["ChessARRenderer"]
        HUD["GameplayHUDController"]
        Captures["CapturedPiecesController"]
        GameOver["GameOverOverlay"]
    end

    subgraph LAN["LAN networking"]
        LNM["LanNetworkManager"]
        LD["LanDiscovery"]
        CNP["ChessNetworkProxy"]
    end

    subgraph AI["AI client controllers"]
        ACC["AiCoachClient"]
        AIO["AiOpponentController"]
        AMC["AiMoveCoachController"]
        Review["AiGameReviewOverlay"]
    end

    MMC --> Prefs
    Prefs --> GMM
    GMM --> GSM
    GMM --> Clock
    GMM --> VMC
    GMM --> AIO

    I2D --> MIC
    IAR --> MIC
    MIC --> GSM
    MIC --> Promo
    MIC --> CNP

    GSM --> GE
    GE --> R2D
    GE --> RAR
    GE --> HUD
    GE --> Captures
    GE --> GameOver
    GE --> AMC
    GE --> AIO

    VMC --> R2D
    VMC --> RAR
    VMC --> I2D
    VMC --> IAR
    HUD --> VMC

    MMC --> LNM
    MMC --> LD
    LNM --> CNP
    LD --> MMC
    CNP --> GSM
    CNP --> VMC
    CNP --> Clock

    GSM --> CNE
    CNE --> ACC
    AIO --> ACC
    AMC --> ACC
    Review --> ACC
```

### Responsibilities

- `GameStateManager` is the single source of truth for chess state. It validates and applies legal moves, tracks timers, captures, move history, castling rights, en passant, repetition, and game-over state.
- `GameEvents` decouples rules from rendering, HUD, AI coach, and game-over UI.
- `ChessMoveInteractionController` centralizes board selection, highlighting, promotion, local move execution, and LAN routing. Both 2D and AR input adapters use it.
- `ChessViewModeController` switches between 2D and AR. It creates the AR session, XROrigin, camera, raycast/plane/anchor managers, renderer, and AR input handler at runtime.
- `ChessARRenderer` loads board and piece models from `Resources/ARModels`, builds square colliders/highlights, fits pieces to squares, and responds to game events.
- `LanNetworkManager` owns Mirror connection lifecycle, two-player admission, scene transition, color assignment, rematch, timer sync, and disconnect results.
- `ChessNetworkProxy` is the per-player Mirror bridge. It sends commands to the server, applies client RPC moves, handles target RPC startup, and rolls back rejected predicted moves.
- `AiCoachClient` posts JSON to the configured AI endpoint for live feedback, AI moves, and post-game reviews.

## Python AI Service

```mermaid
flowchart LR
    Unity["Unity AiCoachClient"] -->|HTTP JSON| API["FastAPI server.py or Azure function_app.py"]
    API --> Service["ai_service.py"]
    Service -->|UCI/FEN validation| Chess["python-chess"]
    Service -->|best move and evaluations| Stockfish["Stockfish engine"]
    Service -->|tool-enabled prompts| Gemini["Google Gemini"]
    Gemini -->|calls tools| Service
    Service --> API
    API --> Unity
```

The canonical service is `server/`. `server.py` exposes a local FastAPI app and `function_app.py` exposes the same handlers as Azure Functions. Both share models and business logic in `ai_service.py`.

Current routes:

- `GET /health`
- `POST /ai-move`
- `POST /analyze-move`
- `POST /review-game`

`local_server/` is an older local prototype kept for reference. It is not the source of truth for the current Unity client.
