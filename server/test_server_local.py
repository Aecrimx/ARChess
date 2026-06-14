# pytest test_server_local.py -s
# de rulat doar local, nu in ci/cd pipeline
from fastapi.testclient import TestClient
from server import app

client = TestClient(app)

def test_analyze_move():
    print("\n--- Testing /analyze-move ---")
    
    # Opening standard: 1. e4 e5
    payload = {
        "fen_before": "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
        "fen_after": "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
        "move_played": "e7e5",
        "player_color": "black",
        "move_number": 1
    }
    
    response = client.post("/analyze-move", json=payload)
    
    assert response.status_code == 200
    data = response.json()
    
    assert "feedback" in data
    print("Feedback received:")
    print(data["feedback"])
    assert len(data["feedback"]) > 0


def test_review_game():
    print("\n--- Testing /review-game ---")
    
    #"Fool's Mate"
    payload = {
        "pgn": "1. f3 e5 2. g4 Qh4#",
        "player_color": "white"
    }
    
    response = client.post("/review-game", json=payload)
    
    assert response.status_code == 200
    data = response.json()
    
    assert "review" in data
    print("Game Review received:")
    print(data["review"])
    assert len(data["review"]) > 0