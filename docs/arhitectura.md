# Arhitectura AR Chess

Această diagramă ilustrează arhitectura de nivel înalt a aplicației AR Chess, arătând modul în care componentele majore interacționează între ele.


```mermaid
flowchart TD
    %% Core Game State Layer
    subgraph Core[Stare Joc & Domeniu]
        GSM[GameStateManager]
        GM[GameModeManager]
        Evts[GameEvents]
    end

    %% Network Layer
    subgraph Network[Stratul de Rețea Mirror]
        LNM[LanNetworkManager]
        LD[LanDiscovery]
        CNP[ChessNetworkProxy]
    end

    %% Presentation & Input Layer
    subgraph Presentation[Prezentare & Intrare 2D/UI]
        R2D[Chess2DRenderer]
        I2D[Chess2DInputHandler]
        HUD[GameplayHUDController]
        Menu[MainMenuController]
    end

    %% Relationships
    Menu -- "Configurează modul & cronometrul" --> GM
    Menu -- "Pornește Host/Client" --> LNM
    LNM -- "Transmite/Descoperă" --> LD
    LNM -- "Generează pentru fiecare jucător" --> CNP
    
    I2D -- "Citește tabla" --> GSM
    I2D -- "Offline: TryApplyMove()" --> GSM
    I2D -- "Online: CmdRequestMove()" --> CNP
    
    CNP -- "Server: TryApplyMove()" --> GSM
    CNP -- "Client: Aplică mutările sincronizate" --> GSM
    
    GSM -- "Declanșează" --> Evts
    Evts -- "OnMoveMade" --> R2D
    Evts -- "OnTurnChanged" --> HUD
    
    R2D -- "Expune Zone de Contact (HitAreas)" --> I2D
```

### Defalcarea componentelor
1. **Domeniul de Bază (Core)**: 
   - `GameStateManager` acționează ca unică sursă a adevărului pentru tabla de șah.
   - `GameEvents` oferă o modalitate decuplată pentru actualizarea elementelor vizuale atunci când starea se schimbă.
2. **Stratul de Rețea (Network)**: 
   - Construit folosind framework-ul Mirror, `LanNetworkManager` inițializează serverul/clientul.
   - `ChessNetworkProxy` rutează datele de intrare ale jucătorului către server și difuzează mutările verificate înapoi către clienți prin apeluri RPC.
3. **Prezentare (Presentation)**: 
   - `Chess2DRenderer` desenează interactiv sprite-urile pe baza `GameEvents`.
   - `Chess2DInputHandler` traduce evenimentele de pe ecran (touch/click) în mutări de șah.
