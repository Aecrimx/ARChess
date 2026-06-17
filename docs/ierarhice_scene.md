# Scene Hierarchy

This document lists the current serialized Unity scene objects plus the important runtime-created objects.

## MainMenu.unity

```text
MainMenu
|-- Camera
|-- EventSystem
|-- LanNetworkManager
|   |-- LanNetworkManager
|   |-- LanDiscovery
|   `-- TelepathyTransport
`-- MainMenuCanvas
    `-- MainMenuController
```

`MainMenuController` builds the visible menu panels at runtime:

```text
MainPanel
PvpPanel
TimerSelectPanel
LanPanel
HostLobbyPanel
JoinLobbyPanel
SettingsPanel
```

The menu stores the selected mode, timer preset, AI difficulty, player name, and coach personality in `PlayerPrefs` before loading `ChessScene` or starting LAN networking.

## ChessScene.unity

```text
ChessScene
|-- Camera
|-- EventSystem
|-- GameManager
|   |-- GameStateManager
|   |-- GameModeManager
|   `-- ChessClock
`-- 2dMode
    `-- 2dCanvas
        |-- Board
        |   `-- BoardImage
        |-- Timer_friendly_text
        |-- Timer_enemy_text
        |-- Player_indicator_text
        |-- WhiteCaptureCont
        |-- BlackCaptureCont
        |-- Chess2DInputHandler
        |-- Chess2DRenderer
        |-- PawnPromotionPicker
        |-- GameOverOverlay
        |-- GameplayHUDController
        `-- CapturedPiecesController
```

The `2dCanvas` object owns most of the runtime UI behavior. Several UI trees are generated from scripts at startup:

- `GameplayHUDController` creates the in-game menu, AR toggle, AR HUD, AR controls, exit dialog, and live AI coach bubble.
- `PawnPromotionPicker` creates the promotion overlay.
- `GameOverOverlay` creates the result panel, review button, rematch button, main-menu button, and `AiGameReviewOverlay`.
- `Chess2DRenderer` creates hit areas, highlights, and piece images for all 64 board squares.

## Runtime Objects

`GameModeManager` calls `ChessViewModeController.EnsureInScene()` during `ChessScene` startup. The controller is attached to the persistent game manager object if one is available.

When AR mode is requested, `ChessViewModeController` creates this runtime hierarchy:

```text
ARRoot
|-- ARSession
|-- XROrigin
|   `-- CameraOffset
|       `-- ARCamera
|-- ARPlaneTemplate
|-- ChessARRenderer
`-- ChessARInputHandler
```

`ChessARRenderer` then creates the AR board rig after it needs to render or place the board:

```text
ARChessBoardRig
|-- BoardModel
|-- SquaresRoot
|-- PiecesRoot
`-- HighlightsRoot
```

The AR board and pieces are loaded from `Assets/Resources/ARModels`. Each AR square gets an `ARBoardSquare` marker and collider so `ChessARInputHandler` can translate raycast hits into board coordinates.
