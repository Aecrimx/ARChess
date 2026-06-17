import logging
import os
from typing import Any, Dict, List, Optional, Type, TypeVar

import chess
import chess.engine
from pydantic import BaseModel, validator

try:
    from google import genai
    from google.genai import types
except ImportError:  # Allows mocked tests to run without the external SDK installed.
    genai = None
    types = None


DEFAULT_ANALYZE_RESPONSE = (
    "[mock] The move changed the position in a predictable way and the server is responding correctly."
)
DEFAULT_REVIEW_RESPONSE = (
    "[mock] Game review completed successfully and the server returned a deterministic response."
)

logger = logging.getLogger(__name__)


def _env_flag(name: str) -> bool:
    return os.getenv(name, "").strip().lower() in {"1", "true", "yes", "on"}


def _env_flag_default(name: str, default_value: bool) -> bool:
    raw = os.getenv(name)
    if raw is None:
        return default_value

    return raw.strip().lower() in {"1", "true", "yes", "on"}


def _env_int(name: str, default_value: int) -> int:
    raw = os.getenv(name)
    if raw is None:
        return default_value

    try:
        return int(raw)
    except ValueError:
        return default_value


COACH_PERSONALITIES = {"pleasant_coach", "cocky"}


def normalize_coach_personality(value: Optional[str]) -> str:
    normalized = str(value or "").strip().lower()
    return normalized if normalized in COACH_PERSONALITIES else "cocky"


MOCK_EXTERNALS = _env_flag("MOCK_EXTERNALS")
MOCK_STOCKFISH = _env_flag("MOCK_STOCKFISH") or MOCK_EXTERNALS
MOCK_ANALYZE_RESPONSE = os.getenv("MOCK_ANALYZE_RESPONSE", DEFAULT_ANALYZE_RESPONSE)
MOCK_REVIEW_RESPONSE = os.getenv("MOCK_REVIEW_RESPONSE", DEFAULT_REVIEW_RESPONSE)

STOCKFISH_PATH = os.getenv("STOCKFISH_PATH", "/usr/bin/stockfish")
STOCKFISH_DEPTH = _env_int("STOCKFISH_DEPTH", 15)
GEMINI_MODEL = os.getenv("GEMINI_MODEL", "gemini-3.1-flash-lite")
GEMINI_AFC_MAX_REMOTE_CALLS = max(1, _env_int("GEMINI_AFC_MAX_REMOTE_CALLS", 10))
COACH_PERSONALITY = normalize_coach_personality(os.getenv("COACH_PERSONALITY", "cocky"))
REVIEW_USE_TOOLS = _env_flag_default("REVIEW_USE_TOOLS", True)

AI_DIFFICULTY_PRESETS = {
    "easy": {"depth": 3, "skill_level": 3},
    "normal": {"depth": 8, "skill_level": 10},
    "hard": {"depth": STOCKFISH_DEPTH, "skill_level": 20},
}

_genai_client = None


class MoveRequest(BaseModel):
    fen_before: str
    fen_after: str
    move_played: str
    player_color: str
    move_number: int
    coach_personality: Optional[str] = None

    @validator("coach_personality", pre=True, always=True)
    def validate_coach_personality(cls, value: Any) -> Optional[str]:
        if value is None:
            return None

        normalized = str(value).strip().lower()
        if not normalized:
            return None

        if normalized not in COACH_PERSONALITIES:
            allowed = ", ".join(sorted(COACH_PERSONALITIES))
            raise ValueError(f"coach_personality must be one of: {allowed}")

        return normalized


class GameReviewRequest(BaseModel):
    player_color: str
    pgn: Optional[str] = None
    moves_uci: Optional[List[str]] = None
    final_fen: Optional[str] = None


class AiMoveRequest(BaseModel):
    fen: str
    difficulty: str = "normal"

    @validator("difficulty", pre=True, always=True)
    def normalize_difficulty(cls, value: Any) -> str:
        if value is None:
            return "normal"

        normalized = str(value).strip().lower()
        if not normalized:
            return "normal"

        if normalized not in AI_DIFFICULTY_PRESETS:
            allowed = ", ".join(AI_DIFFICULTY_PRESETS.keys())
            raise ValueError(f"difficulty must be one of: {allowed}")

        return normalized


ModelT = TypeVar("ModelT", bound=BaseModel)


def parse_model(model_type: Type[ModelT], payload: Dict[str, Any]) -> ModelT:
    if hasattr(model_type, "model_validate"):
        return model_type.model_validate(payload)  # pydantic v2
    return model_type.parse_obj(payload)  # pydantic v1 fallback


def health_payload() -> Dict[str, Any]:
    return {
        "status": "ok",
        "mock_mode": MOCK_EXTERNALS,
        "stockfish_mocked": MOCK_STOCKFISH,
        "stockfish_path": STOCKFISH_PATH,
        "stockfish_depth": STOCKFISH_DEPTH,
        "model": GEMINI_MODEL,
        "afc_max_remote_calls": GEMINI_AFC_MAX_REMOTE_CALLS,
        "review_use_tools": REVIEW_USE_TOOLS,
    }


def _get_genai_client():
    global _genai_client
    if genai is None:
        raise RuntimeError("google-genai is not installed. Install requirements.txt or enable MOCK_EXTERNALS.")

    if _genai_client is None:
        _genai_client = genai.Client()
    return _genai_client


def _board_from_fen(fen: str, label: str) -> chess.Board:
    try:
        return chess.Board(fen)
    except ValueError as exc:
        raise ValueError(f"{label} is not a valid FEN: {exc}") from exc


def _validate_fen(fen: str, label: str) -> None:
    _board_from_fen(fen, label)


def evaluate_position(fen: str, depth: int = STOCKFISH_DEPTH) -> Dict[str, Any]:
    """Evaluate a chess position using Stockfish. Returns centipawn score and best move."""
    _validate_fen(fen, "fen")

    if MOCK_STOCKFISH:
        return {"centipawn_score": 42, "best_move": "e2e4"}

    with chess.engine.SimpleEngine.popen_uci(STOCKFISH_PATH) as engine:
        board = chess.Board(fen)
        info = engine.analyse(board, chess.engine.Limit(depth=depth))
        score = info["score"].white().score(mate_score=10000)
        best_move = info.get("pv", [None])[0]
        return {"centipawn_score": score, "best_move": best_move.uci() if best_move else None}


def get_best_move(fen: str, num_moves: int = 3) -> Dict[str, Any]:
    """Get the best move and candidate moves for a position."""
    _validate_fen(fen, "fen")

    if MOCK_STOCKFISH:
        return {
            "top_moves": [
                {"move": "e2e4", "score": 42},
                {"move": "d2d4", "score": 18},
                {"move": "g1f3", "score": 12},
            ]
        }

    with chess.engine.SimpleEngine.popen_uci(STOCKFISH_PATH) as engine:
        board = chess.Board(fen)
        result = engine.analyse(board, chess.engine.Limit(depth=STOCKFISH_DEPTH), multipv=num_moves)
        moves = []
        for info in result:
            move = info.get("pv", [None])[0]
            score = info["score"].white().score(mate_score=10000)
            if move:
                moves.append({"move": move.uci(), "score": score})
        return {"top_moves": moves}


def _first_legal_move(board: chess.Board) -> chess.Move:
    for move in board.legal_moves:
        return move

    raise ValueError("fen has no legal moves.")


def select_ai_move(fen: str, difficulty: str) -> str:
    board = _board_from_fen(fen, "fen")
    if board.is_game_over(claim_draw=True):
        raise ValueError("fen is a game-over position.")

    if not any(board.legal_moves):
        raise ValueError("fen has no legal moves.")

    preset = AI_DIFFICULTY_PRESETS[difficulty]

    if MOCK_STOCKFISH:
        return _first_legal_move(board).uci()

    with chess.engine.SimpleEngine.popen_uci(STOCKFISH_PATH) as engine:
        try:
            engine.configure({"Skill Level": preset["skill_level"]})
        except chess.engine.EngineError:
            pass

        result = engine.play(board, chess.engine.Limit(depth=preset["depth"]))
        if result.move is None:
            raise ValueError("Stockfish did not return a legal move.")

        return result.move.uci()


def ai_move_response(req: AiMoveRequest) -> Dict[str, str]:
    return {
        "best_move": select_ai_move(req.fen, req.difficulty),
        "difficulty": req.difficulty,
    }


def classify_score(score: Optional[int], turn: chess.Color) -> str:
    """Rough classification of position quality."""
    if score is None:
        return "unknown"

    adjusted = score if turn == chess.WHITE else -score
    if adjusted > 300:
        return "winning"
    if adjusted > 100:
        return "slight advantage"
    if adjusted > -100:
        return "equal"
    if adjusted > -300:
        return "slight disadvantage"
    return "losing"


def _normalize_uci_move(move_text: str) -> str:
    return move_text.strip().replace("=", "").lower()


def _format_san_moves(san_moves: List[str]) -> str:
    if not san_moves:
        return ""

    turns = []
    for index in range(0, len(san_moves), 2):
        turn_number = index // 2 + 1
        white_move = san_moves[index]
        black_move = san_moves[index + 1] if index + 1 < len(san_moves) else ""
        turns.append(f"{turn_number}. {white_move} {black_move}".strip())
    return " ".join(turns)


def replay_uci_moves(moves_uci: List[str]) -> Dict[str, Any]:
    board = chess.Board()
    san_moves: List[str] = []
    fens_after_moves: List[str] = []
    normalized_moves: List[str] = []

    for index, raw_move in enumerate(moves_uci):
        normalized = _normalize_uci_move(raw_move)
        try:
            move = chess.Move.from_uci(normalized)
        except ValueError as exc:
            raise ValueError(f"Invalid UCI move at ply {index + 1}: {raw_move}") from exc

        if move not in board.legal_moves:
            raise ValueError(f"Illegal move at ply {index + 1}: {raw_move}")

        san_moves.append(board.san(move))
        board.push(move)
        normalized_moves.append(normalized)
        fens_after_moves.append(board.fen())

    return {
        "moves_uci": normalized_moves,
        "moves_san": _format_san_moves(san_moves),
        "final_fen": board.fen(),
        "fens_after_moves": fens_after_moves,
    }


def call_gemini_with_tools(system_prompt: str, user_message: str) -> str:
    """
    Register Stockfish helper tools with Gemini and let the model call them
    through the SDK's native function-calling support.
    """
    if MOCK_EXTERNALS:
        normalized_message = user_message.lower()
        if "review this game" in normalized_message or "pgn:" in normalized_message:
            return MOCK_REVIEW_RESPONSE
        return MOCK_ANALYZE_RESPONSE

    if types is None:
        return _fallback_model_response(
            "google-genai is not installed. Install requirements.txt or enable MOCK_EXTERNALS."
        )

    config = types.GenerateContentConfig(
        system_instruction=system_prompt,
        tools=[evaluate_position, get_best_move],
        automatic_function_calling=types.AutomaticFunctionCallingConfig(
            maximum_remote_calls=GEMINI_AFC_MAX_REMOTE_CALLS,
        ),
        temperature=0.2,
    )

    try:
        chat = _get_genai_client().chats.create(
            model=GEMINI_MODEL,
            config=config,
        )

        response = chat.send_message(user_message)
        text = _extract_response_text(response)
        if text:
            return text

        logger.warning("Gemini returned no text for a tool-enabled request; retrying without tools.")
        retry_message = (
            f"{user_message}\n\n"
            "The previous tool-enabled attempt returned no text. "
            "Give the player a plain text coaching response without calling tools."
        )
    except Exception as exc:
        logger.warning("Gemini tool-enabled request failed; retrying without tools: %s", exc)
        retry_message = (
            f"{user_message}\n\n"
            "The engine/tool-assisted attempt failed on the server. "
            "Give the best plain text coaching response you can from the supplied chess notation."
        )

    return call_gemini_text_with_fallback(system_prompt, retry_message)


def call_gemini_text_with_fallback(system_prompt: str, user_message: str) -> str:
    try:
        return call_gemini_text(system_prompt, user_message)
    except Exception as exc:
        logger.exception("Gemini plain-text request failed.")
        return _fallback_model_response(str(exc))


def call_gemini_text(system_prompt: str, user_message: str) -> str:
    if MOCK_EXTERNALS:
        normalized_message = user_message.lower()
        if "review this game" in normalized_message or "pgn:" in normalized_message:
            return MOCK_REVIEW_RESPONSE
        return MOCK_ANALYZE_RESPONSE

    if types is None:
        raise RuntimeError("google-genai is not installed. Install requirements.txt or enable MOCK_EXTERNALS.")

    config = types.GenerateContentConfig(
        system_instruction=system_prompt,
        temperature=0.2,
    )
    response = _get_genai_client().models.generate_content(
        model=GEMINI_MODEL,
        contents=user_message,
        config=config,
    )
    text = _extract_response_text(response)
    if not text:
        raise RuntimeError("Gemini returned an empty response.")

    return text


def _extract_response_text(response: Any) -> str:
    try:
        text = getattr(response, "text", None)
    except Exception:
        return ""

    return text.strip() if isinstance(text, str) else ""


def _fallback_model_response(reason: str) -> str:
    logger.warning("Returning fallback AI coach response: %s", reason)
    return (
        "The AI coach received the game data, but the model-backed analysis could not be completed right now. "
        "Please retry in a moment."
    )


def analyze_move_response(req: MoveRequest) -> Dict[str, str]:
    _validate_fen(req.fen_before, "fen_before")
    _validate_fen(req.fen_after, "fen_after")

    coach_personality = req.coach_personality or COACH_PERSONALITY
    if coach_personality == "pleasant_coach":
        system = """You are a chess coach giving real-time feedback to a beginner/intermediate player.
Be concise (2-3 sentences max). Be encouraging but honest.
Use the tools to evaluate positions, then explain in plain English what happened."""
    else:
        system = """You are a cocky chess coach giving real-time feedback to a beginner/intermediate player.
Be concise (2-3 sentences max). Be honest and sarcastic without being cruel.
Use the tools to evaluate positions, then explain in plain English what happened."""

    user_msg = f"""The player ({req.player_color}) just played {req.move_played} on move {req.move_number}.
Position before: {req.fen_before}
Position after: {req.fen_after}

Evaluate both positions, compare them, and give brief feedback on whether this was a good or bad move and why."""

    feedback = call_gemini_with_tools(system, user_msg)
    return {"feedback": feedback}


def _build_review_context(req: GameReviewRequest) -> str:
    sections: List[str] = []

    if req.pgn and req.pgn.strip():
        sections.append(f"PGN:\n{req.pgn.strip()}")

    if req.moves_uci:
        replay = replay_uci_moves(req.moves_uci)
        sections.append("Move list (UCI):\n" + " ".join(replay["moves_uci"]))
        sections.append("Move list (SAN):\n" + replay["moves_san"])
        sections.append("Final FEN from replay:\n" + replay["final_fen"])

    if req.final_fen and req.final_fen.strip():
        _validate_fen(req.final_fen, "final_fen")
        sections.append("Final FEN reported by client:\n" + req.final_fen.strip())

    if not sections:
        raise ValueError("review-game requires pgn, moves_uci, or final_fen.")

    return "\n\n".join(sections)


def review_game_response(req: GameReviewRequest) -> Dict[str, str]:
    if REVIEW_USE_TOOLS:
        logger.info("Review game is using tool-enabled Gemini review.")
        system = """You are a chess coach doing a post-game review.
Identify the 2-3 most critical moments (blunders, missed tactics, good moves).
Be educational and constructive. Use the tools to verify your analysis."""
    else:
        logger.info("Review game is using plain text Gemini review.")
        system = """You are a chess coach doing a post-game review.
Identify the 2-3 most critical moments from the supplied move list and final position.
Be educational and constructive. Do not claim exact engine evaluations or call tools."""

    review_context = _build_review_context(req)
    user_msg = f"""Review this game for the {req.player_color} player.
{review_context}

Find the turning points and key mistakes."""

    review = (
        call_gemini_with_tools(system, user_msg)
        if REVIEW_USE_TOOLS
        else call_gemini_text_with_fallback(system, user_msg)
    )
    return {"review": review}
