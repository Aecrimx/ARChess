# setup pe local laptop
# python -m venv .venv
# source .venv/bin/activate
# pip install -r requirements.txt
# export GEMINI_API_KEY="cheie api aici" in terminal
# # server rulat cu: uvicorn server:app --host 0.0.0.0 --port 8000

# Legat de server, evident deschide port-ul 8000.
# quick test de pe alt device pe aceiasi retea e sa verifici ca /docs endpoint-ul e accesibil
# http://<ip>:8000/docs

# ================Arhitectura==================

# Client Unity -> Python FastAPI server -> Stockfish (local)
#                           ||
#                           \/
#                        Gemini API


#===============Server======================
#Doua endppoint-uri
# POST /analyze-move - poate fi called o adata la cateva miscari / timer intr-un joc live sau nu (1 vs AI coaching)
# POST /review-game - poate fi called la finalul unui meci pentru un full match review.

# Tool flow
# 1. Client trimite game state (notatie FEN adica move history) la server
# 2. Server cheama API gemini cu pozitii si definitii tools
# 3. Gemini decide ca trebuie sa evalueze o pozitie si da call la tool
# 4. Server-ul ruleaza Stockfish, returneaza score/best move la Gemini
# 5. Gemini genereaza feedback
# 6. Server-ul trimite text inapoi la client-ul Unity.


import os
import chess
import chess.engine
from fastapi import FastAPI
from pydantic import BaseModel # pt data validation si parsing
from google import genai
from google.genai import types


app = FastAPI()
STOCKFISH_PATH = "/usr/bin/stockfish" # poate fi diferit dar am folosit yay si aici mi l-a trantit


def _env_flag(name: str) -> bool:
    return os.getenv(name, "").strip().lower() in {"1", "true", "yes", "on"}


MOCK_EXTERNALS = _env_flag("MOCK_EXTERNALS")
MOCK_STOCKFISH = _env_flag("MOCK_STOCKFISH") or MOCK_EXTERNALS
MOCK_ANALYZE_RESPONSE = os.getenv(
    "MOCK_ANALYZE_RESPONSE",
    "[mock] The move changed the position in a predictable way and the server is responding correctly.",
)
MOCK_REVIEW_RESPONSE = os.getenv(
    "MOCK_REVIEW_RESPONSE",
    "[mock] Game review completed successfully and the server returned a deterministic response.",
)


_genai_client = None


def _get_genai_client():
    global _genai_client
    if _genai_client is None:
        _genai_client = genai.Client()
    return _genai_client

# response = client.models.generate_content(
#     model='gemini-2.5-flash',
#     contents='Should I pack an umbrella for my trip to Bucuresti?',
#     config=types.GenerateContentConfig(
#         # functiile se pot pasa direct in tools
#         tools=[test_get_current_weather],
#         # optional dar poti da parametrul lower pt a forta modelul sa foloseasca tool-uri mai deterministic
#         temperature=0.2, 
#     )
# )


# test_response = client.models.generate_content(
#     model='gemini-2.5-flash',
#     contents='What is the evaluation for this FEN: rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1',
#     config=types.GenerateContentConfig(
#         tools=[evaluate_position, get_best_move],
#     )
# )

# print("\nGemini's Response:")
# print(test_response.text)




# TOOL-URI

# Centipawn e un evaluation score
#   +35 -> alb e in fata cu 0.35 pioni /in general inseamna ca ambii jucatori sunt la egal
#   +100-> alb e in fata cu un pion.
#   -50 -> negru e in fata cu 0.5 pioni
#   in general orice e peste 300 inseamna ca e un avantaj mare pentru acel jucator si ce e peste 500 inseamna ca acel jucator are sanse sa castige


# best move foloseste urmatoarea schema pt pozitiile pieselor:
#56 57 58 59 60 61 62 63   (8th rank)
#48 49 50 51 52 53 54 55   (7th rank)
#40 41 42 43 44 45 46 47   (6th rank)
#32 33 34 35 36 37 38 39   (5th rank)
#24 25 26 27 28 29 30 31   (4th rank)
#16 17 18 19 20 21 22 23   (3rd rank)
# 8  9 10 11 12 13 14 15   (2nd rank)
# 0  1  2  3  4  5  6  7   (1st rank)
# a  b  c  d  e  f  g  h

# Deci daca raspunsul tool-ului este ca cea mai buna miscare este de la patratul 52 la patratul 36 
# asta inseamna ca miscarea cea mai buna este e7 la e5


def evaluate_position(fen: str, depth: int = 15) -> dict:
    """Evaluate a chess position using Stockfish. Returns centipawn score and best move."""
    if MOCK_STOCKFISH:
        return {"centipawn_score": 42, "best_move": "e2e4"}

    with chess.engine.SimpleEngine.popen_uci(STOCKFISH_PATH) as engine:
        board = chess.Board(fen)
        info = engine.analyse(board, chess.engine.Limit(depth=depth))
        score = info["score"].white().score(mate_score=10000)
        best_move = info.get("pv", [None])[0]
        return {"centipawn_score": score, "best_move": best_move.uci() if best_move else None}

def get_best_move(fen: str, num_moves: int = 3) -> dict:
    """Get the best move and top 3 candidate moves for a position."""
    if MOCK_STOCKFISH:
        return {"top_moves": [{"move": "e2e4", "score": 42}, {"move": "d2d4", "score": 18}, {"move": "g1f3", "score": 12}]}

    with chess.engine.SimpleEngine.popen_uci(STOCKFISH_PATH) as engine:
        board = chess.Board(fen)
        result = engine.analyse(board, chess.engine.Limit(depth=15), multipv=num_moves)
        moves = []
        for info in result:
            move = info.get("pv", [None])[0]
            score = info["score"].white().score(mate_score=10000)
            if move:
                moves.append({"move": move.uci(), "score": score})
        return {"top_moves": moves}


def classify_score(score: int, turn) -> str:
    """Rough classification of position quality."""
    if score is None: return "unknown"
    adjusted = score if turn == chess.WHITE else -score
    if adjusted > 300: return "winning"
    if adjusted > 100: return "slight advantage"
    if adjusted > -100: return "equal"
    if adjusted > -300: return "slight disadvantage"
    return "losing"


#============ Agentic loop==============

def call_gemini_with_tools(system_prompt: str, user_message: str) -> str:
    """
    Creeaza o instanta cu tool-uri inregistrate.
    Cand un mesaj da trigger la un tool, SDK-ul de la Google
    executa local si apoi da inapoi raspunsul la Gemini
    """
    if MOCK_EXTERNALS:
        normalized_message = user_message.lower()
        if "review this game" in normalized_message or "pgn:" in normalized_message:
            return MOCK_REVIEW_RESPONSE
        return MOCK_ANALYZE_RESPONSE

    config = types.GenerateContentConfig(
        system_instruction=system_prompt,
        tools=[evaluate_position, get_best_move],
        temperature=0.2
    )
    
    # client.chats pt a da handle nativ la invocatii multi-turn fara loop-uri manual scrise
    chat = _get_genai_client().chats.create(
        # model="gemini-2.5-flash",
        model="gemini-3.1-flash-lite",
        config=config
    )
    
    response = chat.send_message(user_message)
    return response.text or "Analysis unavailable."

#==============API Endpoints==============

class MoveRequest(BaseModel):
    fen_before: str
    fen_after: str  
    move_played: str
    player_color: str
    move_number: int

class GameReviewRequest(BaseModel):
    pgn: str
    player_color: str


# personality = "pleasant_coach"
personality = "cocky" # e vina ta Robert


@app.get("/health")
async def health():
    return {
        "status": "ok",
        "mock_mode": MOCK_EXTERNALS,
        "stockfish_mocked": MOCK_STOCKFISH,
    }

@app.post("/analyze-move")
async def analyze_move(req: MoveRequest):
    if personality == "pleasant_coach":
        system = """You are a chess coach giving real-time feedback to a beginner/intermediate player.
        Be concise (2-3 sentences max). Be encouraging but honest. 
        Use the tools to evaluate positions, then explain in plain English what happened."""
    elif personality == "cocky":
        system = """You are a cocky chess coach giving real-time feedback to a beginner/intermediate player.
        Be concise (2-3 sentences max). Be honest and very sarcastic. You can be snarkly critical.
        Use the tools to evaluate positions, then explain in plain English what happened."""
    
    user_msg = f"""The player ({req.player_color}) just played {req.move_played} on move {req.move_number}.
Position before: {req.fen_before}
Position after: {req.fen_after}

Evaluate both positions, compare them, and give brief feedback on whether this was a good or bad move and why."""
    
    feedback = call_gemini_with_tools(system, user_msg)
    return {"feedback": feedback}

@app.post("/review-game")
async def review_game(req: GameReviewRequest):
    system = """You are a chess coach doing a post-game review. 
Identify the 2-3 most critical moments (blunders, missed tactics, good moves).
Be educational and constructive. Use the tools to verify your analysis."""
    
    user_msg = f"""Review this game for the {req.player_color} player.
PGN: {req.pgn}

Find the turning points and key mistakes. Use evaluate_position to check specific positions if needed."""
    
    review = call_gemini_with_tools(system, user_msg)
    return {"review": review}

