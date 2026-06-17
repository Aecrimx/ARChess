import os

import pytest

os.environ.setdefault("MOCK_EXTERNALS", "1")
os.environ.setdefault("MOCK_STOCKFISH", "1")

import ai_service


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
