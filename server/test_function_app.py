import importlib
import json
import os

import pytest

func = pytest.importorskip("azure.functions")

os.environ["MOCK_EXTERNALS"] = "1"
os.environ["MOCK_STOCKFISH"] = "1"

import ai_service
import function_app

ai_service = importlib.reload(ai_service)
function_app = importlib.reload(function_app)


def _request(method, url, payload=None):
    body = b"" if payload is None else json.dumps(payload).encode("utf-8")
    return func.HttpRequest(
        method=method,
        url=url,
        headers={"content-type": "application/json"},
        params={},
        route_params={},
        body=body,
    )


def _json_body(response):
    return json.loads(response.get_body().decode("utf-8"))


def test_health_function_route_returns_json():
    response = function_app.health(_request("GET", "/health"))

    assert response.status_code == 200
    assert _json_body(response)["status"] == "ok"


def test_analyze_move_function_route_returns_feedback():
    payload = {
        "fen_before": "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
        "fen_after": "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
        "move_played": "e7e5",
        "player_color": "black",
        "move_number": 1,
    }

    response = function_app.analyze_move(_request("POST", "/analyze-move", payload))

    assert response.status_code == 200
    assert "[mock]" in _json_body(response)["feedback"]


def test_review_game_function_route_returns_review_from_uci():
    payload = {
        "player_color": "white",
        "moves_uci": ["f2f3", "e7e5", "g2g4", "d8h4"],
    }

    response = function_app.review_game(_request("POST", "/review-game", payload))

    assert response.status_code == 200
    assert "[mock]" in _json_body(response)["review"]
