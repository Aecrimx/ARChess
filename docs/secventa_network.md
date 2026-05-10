# Diagrama de Secvență a Rețelei AR Chess

Această diagramă de secvență detaliază fluxul efectuării unei mutări tipice în timpul unei sesiuni active de multiplayer prin LAN.

```mermaid
sequenceDiagram
    actor Jucător as Player
    participant Input as Chess2DInputHandler (Client)
    participant CNP_C as ChessNetworkProxy (Client)
    participant CNP_S as ChessNetworkProxy (Server)
    participant GSM as GameStateManager (Server)
    participant CNP_Remote as ChessNetworkProxy (Client Distant)
    participant Render as Chess2DRenderer (Toți Clienții)

    Jucător->>Input: Dă click pe piesă și pe pătrățelul destinație
    Input->>Input: Validează rândul local și mutările pseudo-legale
    Input->>CNP_C: ExecuteMove(CmdRequestMove)
    
    Note over CNP_C, CNP_S: Este trimisă o [Command] Mirror prin rețea
    CNP_C->>CNP_S: CmdRequestMove(de_la, la, promovare)
    
    CNP_S->>GSM: Verifică a cui este rândul
    CNP_S->>GSM: TryApplyMove(de_la, la, promovare)
    GSM-->>CNP_S: returnează true (Mutare Validă)
    
    Note right of GSM: Serverul își actualizează Starea internă a Tablei și declanșează OnMoveMade
    
    Note over CNP_S, CNP_Remote: Transmisie broadcast Mirror [ClientRpc]
    CNP_S->>CNP_Remote: RpcApplyMove(de_la, la, promovare)
    
    CNP_Remote->>GameStateManager (Client): TryApplyMove(de_la, la)
    Note right of GameStateManager (Client): Clientul își actualizează propria replică a tablei
    
    GameStateManager (Client)->>GameEvents: RaiseMoveMade(MoveRecord)
    GameEvents->>Render: HandleMoveMade()
    Render->>Render: Redesenare piese / Actualizare Evidențieri
```

### Note privind Fluxul:
- **Server cu Autoritate**: Proxy-ul clientului trimite o intenție (`CmdRequestMove`) și se bazează pe server pentru a valida de fapt regulile prin `GameStateManager.TryApplyMove()`. 
- **Releu Rpc**: Odată verificat, serverul declanșează `RpcApplyMove` pe ceilalți clienți.
- **Interfață condusă de evenimente**: Odată ce o mutare este înregistrată fizic în `GameStateManager`-ul oricărei instanțe, aceasta lansează `GameEvents.OnMoveMade()`, determinând interfața vizuală locală (și procesatorii de date de intrare) să elimine logicile temporare și să se redeseneze.
