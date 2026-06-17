import os

import pytest

os.environ.setdefault("MOCK_EXTERNALS", "1")
os.environ.setdefault("MOCK_STOCKFISH", "1")

import ai_service


def test_move_request_normalizes_coach_personality():
    payload = ai_service.MoveRequest(
        fen_before=ai_service.chess.STARTING_FEN,
        fen_after=ai_service.chess.STARTING_FEN,
        move_played="e2e4",
        player_color="white",
        move_number=1,
        coach_personality=" Pleasant_Coach ",
    )

    assert payload.coach_personality == "pleasant_coach"


def test_move_request_rejects_invalid_coach_personality():
    with pytest.raises(ValueError, match="coach_personality must be one of"):
        ai_service.MoveRequest(
            fen_before=ai_service.chess.STARTING_FEN,
            fen_after=ai_service.chess.STARTING_FEN,
            move_played="e2e4",
            player_color="white",
            move_number=1,
            coach_personality="mean",
        )


def test_ai_move_response_returns_legal_mock_move():
    payload = ai_service.AiMoveRequest(
        fen="rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
        difficulty="normal",
    )

    response = ai_service.ai_move_response(payload)
    board = ai_service.chess.Board(payload.fen)
    move = ai_service.chess.Move.from_uci(response["best_move"])

    assert response["difficulty"] == "normal"
    assert move in board.legal_moves


def test_ai_move_response_normalizes_difficulty():
    payload = ai_service.AiMoveRequest(
        fen=ai_service.chess.STARTING_FEN,
        difficulty=" HARD ",
    )

    response = ai_service.ai_move_response(payload)

    assert response["difficulty"] == "hard"


def test_ai_move_response_rejects_invalid_fen():
    payload = ai_service.AiMoveRequest(fen="not a fen", difficulty="easy")

    with pytest.raises(ValueError, match="fen is not a valid FEN"):
        ai_service.ai_move_response(payload)


def test_ai_move_request_rejects_invalid_difficulty():
    with pytest.raises(ValueError, match="difficulty must be one of"):
        ai_service.AiMoveRequest(
            fen=ai_service.chess.STARTING_FEN,
            difficulty="impossible",
        )


def test_ai_move_response_rejects_game_over_position():
    payload = ai_service.AiMoveRequest(
        fen="7k/5Q2/6K1/8/8/8/8/8 b - - 0 1",
        difficulty="normal",
    )

    with pytest.raises(ValueError, match="game-over position"):
        ai_service.ai_move_response(payload)


def test_replay_uci_moves_builds_final_fen_and_san():
    replay = ai_service.replay_uci_moves(["e2e4", "e7e5", "g1f3"])

    assert replay["moves_uci"] == ["e2e4", "e7e5", "g1f3"]
    assert replay["moves_san"] == "1. e4 e5 2. Nf3"
    assert replay["final_fen"] == "rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2"


def test_replay_uci_moves_rejects_illegal_move():
    with pytest.raises(ValueError, match="Illegal move at ply 2"):
        ai_service.replay_uci_moves(["e2e4", "e2e4"])


def test_replay_uci_moves_normalizes_promotion_notation():
    replay = ai_service.replay_uci_moves([
        "a2a4",
        "h7h5",
        "a4a5",
        "h5h4",
        "a5a6",
        "h4h3",
        "a6b7",
        "h3g2",
        "b7a8=Q",
    ])

    assert replay["moves_uci"][-1] == "b7a8q"


def test_review_game_accepts_uci_payload_in_mock_mode():
    payload = ai_service.GameReviewRequest(
        player_color="white",
        moves_uci=["f2f3", "e7e5", "g2g4", "d8h4"],
    )

    response = ai_service.review_game_response(payload)

    assert "review" in response
    assert "[mock]" in response["review"]


def test_review_game_rejects_invalid_final_fen():
    payload = ai_service.GameReviewRequest(
        player_color="white",
        final_fen="not a fen",
    )

    with pytest.raises(ValueError, match="final_fen is not a valid FEN"):
        ai_service.review_game_response(payload)
