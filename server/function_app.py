import json
from typing import Any, Callable, Dict, Type

import azure.functions as func
from pydantic import BaseModel, ValidationError

from ai_service import (
    GameReviewRequest,
    MoveRequest,
    analyze_move_response,
    health_payload,
    parse_model,
    review_game_response,
)


app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)


def _json_response(payload: Dict[str, Any], status_code: int = 200) -> func.HttpResponse:
    return func.HttpResponse(
        json.dumps(payload),
        status_code=status_code,
        mimetype="application/json",
    )


def _read_json(req: func.HttpRequest) -> Dict[str, Any]:
    try:
        payload = req.get_json()
    except ValueError as exc:
        raise ValueError("Request body must be valid JSON.") from exc

    if not isinstance(payload, dict):
        raise ValueError("Request JSON body must be an object.")

    return payload


def _handle_model_request(
    req: func.HttpRequest,
    model_type: Type[BaseModel],
    handler: Callable[[Any], Dict[str, Any]],
) -> func.HttpResponse:
    try:
        model = parse_model(model_type, _read_json(req))
        return _json_response(handler(model))
    except ValidationError as exc:
        return _json_response({"error": exc.errors()}, status_code=422)
    except ValueError as exc:
        return _json_response({"error": str(exc)}, status_code=400)


@app.route(route="health", methods=["GET"])
def health(req: func.HttpRequest) -> func.HttpResponse:
    return _json_response(health_payload())


@app.route(route="analyze-move", methods=["POST"])
def analyze_move(req: func.HttpRequest) -> func.HttpResponse:
    return _handle_model_request(req, MoveRequest, analyze_move_response)


@app.route(route="review-game", methods=["POST"])
def review_game(req: func.HttpRequest) -> func.HttpResponse:
    return _handle_model_request(req, GameReviewRequest, review_game_response)
