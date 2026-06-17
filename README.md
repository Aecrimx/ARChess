# ARChess

ARChess is a Unity 6 mobile chess project with four play flows:

- Play against the server-backed Stockfish opponent.
- Play local PvP on one device.
- Host or join a two-player LAN match through Mirror discovery.
- Switch an active match from the 2D board into Android AR mode.

The Unity client owns the board state, input, rendering, timers, LAN flow, and HUD. The Python service owns Stockfish-backed AI moves plus Gemini-powered coaching and post-game review.

## Current Feature Set

- Full local rules through `GameStateManager`: legal move generation, check/checkmate, stalemate, castling, en passant, promotion, threefold repetition, the 50-move rule, captured pieces, and timed game results.
- Main menu built in code with Play vs AI, Local PvP, LAN Host/Join, time controls, AI difficulty, player name, and coach personality settings.
- Shared move interaction layer for both 2D and AR boards, including selection highlights, legal-move highlights, last-move highlights, check highlights, and promotion picking.
- Runtime AR mode on supported Android devices using AR Foundation and ARCore. The game creates the AR session/origin at runtime, checks AR support, places the board on horizontal planes, anchors it when possible, and exposes reposition, rotate, and scale controls.
- LAN multiplayer through Mirror with UDP discovery, explicit scene-ready startup, host-as-white assignment, authoritative server validation, client-side prediction with rollback, rematches, timer sync, and disconnect game-over handling.
- AI service integration through `AiCoachClient` for `/ai-move`, `/analyze-move`, and `/review-game`.
- FEN and UCI export through `ChessNotationExporter` for AI move requests and game reviews.

## Repository Layout

```text
Assets/Sources/GameState      Chess rules, snapshots, notation export, mode setup
Assets/Sources/Input          Shared move interaction plus 2D and AR input adapters
Assets/Sources/Rendering      2D renderer, AR renderer, runtime AR view controller
Assets/Sources/Hud            Timers, captured pieces, promotion, AR HUD, game-over UI
Assets/Sources/Menus          Runtime-built main menu and LAN lobby UI
Assets/Sources/Network        Mirror LAN manager, player proxy, LAN discovery
Assets/Sources/Ai             Unity client for AI move, live coach, and game review APIs
Assets/Resources/ARModels     Runtime-loaded 3D board and piece models for AR
Assets/Tests/EditMode         Unity edit-mode tests for FEN/UCI/review/AI move parsing
server                        Current Python AI service, Azure Functions entry point, tests
local_server                  Older local FastAPI prototype kept for reference
docs                          Architecture, class, network, scene, and color documentation
```

The binary report under `docs/Raport 1 MDS-3.pdf` is stored as a Git LFS object. Fetch LFS objects if you need the rendered PDF.

## Unity Setup

Use Unity `6000.4.4f1`, matching `ProjectSettings/ProjectVersion.txt`.

1. Install Git LFS before cloning or fetch LFS after cloning:

   ```bash
   git lfs install
   git lfs pull
   ```

2. Open the repository root in Unity.
3. Let Unity restore packages from `Packages/manifest.json`.
4. Confirm the build scenes are:

   ```text
   Assets/Scenes/MainMenu.unity
   Assets/Scenes/ChessScene.unity
   ```

5. For Android AR builds, use a device supported by Google Play Services for AR and keep ARCore/XR Plug-in Management packages installed.

The client has a default AI endpoint URL in `AiCoachClient`. Function keys must not be committed; configure them locally through `PlayerPrefs` or your build/deployment process:

```text
AiCoachBaseUrl
AiCoachFunctionKey
CoachPersonality
AiDifficulty
```

## Python AI Service

The maintained service is `server/`. It can run as FastAPI locally or as an Azure Functions app.

### Local FastAPI

```bash
cd server
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
export GEMINI_API_KEY="your_api_key_here"
export STOCKFISH_PATH="/usr/bin/stockfish"
python -m uvicorn server:app --host 0.0.0.0 --port 8000
```

On Windows PowerShell:

```powershell
cd server
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
$env:GEMINI_API_KEY="your_api_key_here"
$env:STOCKFISH_PATH="C:\path\to\stockfish.exe"
python -m uvicorn server:app --host 0.0.0.0 --port 8000
```

Open `http://127.0.0.1:8000/docs` for the generated FastAPI documentation.

### Azure Functions

`server/function_app.py` exposes the same service logic as Azure Functions routes. Copy `server/local.settings.sample.json` to `server/local.settings.json`, set local values, then run the Functions host from `server/`.

The `server/Dockerfile` uses the Azure Functions Python 3.11 base image and installs Stockfish at `/usr/games/stockfish`.

### Environment Variables

```text
GEMINI_API_KEY          Required for real Gemini calls
GEMINI_MODEL            Defaults to gemini-3.1-flash-lite
STOCKFISH_PATH          Defaults to /usr/bin/stockfish in code
STOCKFISH_DEPTH         Defaults to 15
COACH_PERSONALITY       cocky or pleasant_coach
MOCK_EXTERNALS          1 skips real Gemini and Stockfish in tests
MOCK_STOCKFISH          1 mocks only Stockfish
MOCK_ANALYZE_RESPONSE   Optional mocked analyze text
MOCK_REVIEW_RESPONSE    Optional mocked review text
```

### API Surface

- `GET /health` returns service status, mock flags, Stockfish path/depth, and Gemini model.
- `POST /ai-move` accepts `{ "fen": "...", "difficulty": "easy|normal|hard" }` and returns a legal UCI move.
- `POST /analyze-move` accepts `fen_before`, `fen_after`, `move_played`, `player_color`, `move_number`, and optional `coach_personality`; it returns short live feedback.
- `POST /review-game` accepts `player_color` plus `moves_uci`, `pgn`, or `final_fen`; it returns a full game review.

## Testing

Server checks:

```bash
cd server
python ci_smoke_test.py
pytest test_ai_service.py test_function_app.py
pytest test_server_local.py -s
```

`ci_smoke_test.py` launches FastAPI in mock mode and verifies `/health`, `/analyze-move`, `/ai-move`, and `/review-game`. `test_server_local.py` is intentionally local-only because it can use real external services.

Unity checks are defined in `.github/workflows/unity-ci.yml`:

- GameCI runs edit-mode and play-mode test jobs for pushes and pull requests targeting `main` or `develop`.
- Android builds run after tests on push events to those branches.
- Current edit-mode coverage focuses on FEN export, UCI normalization, review request creation, and AI move parsing/application.

## Documentation

- `docs/arhitectura.md` - current Unity and AI-service architecture.
- `docs/diagrame_clasa.md` - current class relationships.
- `docs/secventa_network.md` - LAN startup and move sequence.
- `docs/ierarhice_scene.md` - serialized scene hierarchy plus runtime-created objects.
- `docs/tema_cromatica.md` - current UI, HUD, AR, and highlight colors.

## Main Dependencies

- Unity 6, URP, UGUI, Input System, XR Management, AR Foundation, and ARCore.
- Mirror for LAN networking and discovery.
- Python 3.11 service stack with FastAPI, Azure Functions, python-chess, Stockfish, and Google Gemini SDK.
- GameCI for Unity tests/builds and GitHub Actions for server smoke tests.
