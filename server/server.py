from fastapi import FastAPI, HTTPException

try:
    from .ai_service import (
        AiMoveRequest,
        GameReviewRequest,
        MoveRequest,
        ai_move_response,
        analyze_move_response,
        health_payload,
        review_game_response,
    )
except ImportError:
    from ai_service import (
        AiMoveRequest,
        GameReviewRequest,
        MoveRequest,
        ai_move_response,
        analyze_move_response,
        health_payload,
        review_game_response,
    )


app = FastAPI()


@app.get("/health")
async def health():
    return health_payload()


@app.post("/analyze-move")
async def analyze_move(req: MoveRequest):
    try:
        return analyze_move_response(req)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@app.post("/ai-move")
async def ai_move(req: AiMoveRequest):
    try:
        return ai_move_response(req)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@app.post("/review-game")
async def review_game(req: GameReviewRequest):
    try:
        return review_game_response(req)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

