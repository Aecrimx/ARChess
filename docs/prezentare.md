## 1. Arhitectura unui client

More or less MVC, MoveInteractionController e un Controller, GameStateManager e un Model, iar sistemul de rendering este View-ul.

```mermaid
flowchart TD
    subgraph Meniu["Configurare și Meniu"]
        GMM["GameModeManager"]
    end

    subgraph Core["Integrare Stare & Reguli"]
        GSM["GameStateManager"]
    end

    subgraph Interfata["Interacțiune & Randare (2D/AR)"]
        MIC["MoveInteractionController"]
        VMC["ViewModeController"]
    end

    subgraph Retea["Networking LAN"]
        LNM["LanNetworkManager"]
        CNP["ChessNetworkProxy"]
    end

    subgraph AI["AI & Integrare Backend"]
        ACC["AiCoachClient"]
    end

    Meniu --> GMM
    GMM --> GSM
    GMM --> VMC

    MIC --> GSM
    MIC --> CNP
    
    GSM --> VMC
    GSM --> ACC
    
    CNP --> GSM
    LNM --> CNP
```

## 2. Arhitectura Python AI Service


```mermaid
flowchart LR
    Unity["Unity AiCoachClient"] <-->|HTTP JSON| API["FastAPI / Azure Function"]
    API <--> Service["ai_service.py"]
    Service -->|UCI/FEN validation| Chess["python-chess"]
    Service -->|best move and evaluations| Stockfish["Stockfish engine"]
    Service <-->|tool-enabled prompts| Gemini["Google Gemini"]
```

## 3. Schemele de Date

```mermaid
classDiagram
    class Piece {
        <<enum>>
        None, WhitePawn, BlackKing, etc.
    }

    class MoveRecord {
        +Vector2Int From
        +Vector2Int To
        +Piece PieceMoved
        +Piece PieceCaptured
        +bool WasCastle
        +bool WasEnPassant
        +Piece PromotionPiece
        +string Notation
    }

    class GameSnapshot {
        +int[] FlatBoard
        +bool IsWhiteTurn
        +float WhiteTimeRemaining
        +float BlackTimeRemaining
        +List~MoveRecord~ MoveHistory
        +GameResult Result
    }

    class GameResult {
        <<enum>>
        Ongoing
        WhiteWins
        BlackWins
        Stalemate
        DrawByRepetition
        DrawByFiftyMoveRule
        WhiteWinsOnTime
        BlackWinsOnTime
        OpponentDisconnected
    }

    GameStateManager --> Piece
    GameStateManager --> MoveRecord
    GameStateManager --> GameSnapshot
    GameStateManager --> GameResult
```

## 4. Secvențe Networking (Simplificate)

Flow-urile de rețea utilizează o arhitectură server-autoritativă prin Mirror Networking.

### 4.1. Conectare și Start Joc

```mermaid
sequenceDiagram
    actor Host
    actor Client
    participant Meniu
    participant ServerManager

    Host->>Meniu: Deschide Host (Alb)
    Meniu->>ServerManager: StartHost() & LanDiscovery
    
    Client->>Meniu: Caută & Join (Negru)
    Meniu->>ServerManager: StartClient(ip)

    ServerManager->>ServerManager: Asignare culori & Schimbare Scenă
    ServerManager->>Meniu: GameStarted & Timer Sync
```

### 4.2. Flux de Mutare (cu Predicție Locală)

```mermaid
sequenceDiagram
    actor Jucator
    participant ClientProxy
    participant StareLocala
    participant ServerProxy
    participant StareServer

    Jucator->>ClientProxy: Efectuează mutare
    ClientProxy->>StareLocala: Aplică predicție locală (Redare Imediată)
    ClientProxy->>ServerProxy: Cere Mutare (CmdRequestMove)

    ServerProxy->>StareServer: Validează mutarea
    alt Mutare Validă
        ServerProxy->>ClientProxy: RpcApplyMove (Sincronizare clienți)
    else Mutare Invalidă
        ServerProxy->>ClientProxy: TargetMoveRejected
        ClientProxy->>StareLocala: Rollback (RestoreSnapshot)
    end
```