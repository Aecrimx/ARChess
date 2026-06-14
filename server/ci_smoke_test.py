import os
import signal
import socket
import subprocess
import sys
import time

import requests


DEFAULT_ANALYZE_RESPONSE = "[mock] The move changed the position in a predictable way and the server is responding correctly."
DEFAULT_REVIEW_RESPONSE = "[mock] Game review completed successfully and the server returned a deterministic response."


def _find_free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def _wait_for_server(base_url: str, timeout_seconds: int = 30) -> None:
    deadline = time.time() + timeout_seconds
    last_error = None

    while time.time() < deadline:
        try:
            response = requests.get(f"{base_url}/health", timeout=2)
            if response.status_code == 200:
                return
            last_error = f"unexpected status {response.status_code}"
        except requests.RequestException as exc:
            last_error = str(exc)
        time.sleep(1)

    raise RuntimeError(f"Server did not become ready within {timeout_seconds}s: {last_error}")


def _start_server(port: int) -> subprocess.Popen:
    env = os.environ.copy()
    env.update(
        {
            "MOCK_EXTERNALS": "1",
            "MOCK_STOCKFISH": "1",
            "MOCK_ANALYZE_RESPONSE": DEFAULT_ANALYZE_RESPONSE,
            "MOCK_REVIEW_RESPONSE": DEFAULT_REVIEW_RESPONSE,
        }
    )

    command = [
        sys.executable,
        "-m",
        "uvicorn",
        "server:app",
        "--host",
        "127.0.0.1",
        "--port",
        str(port),
    ]

    return subprocess.Popen(command, env=env)


def _post_json(base_url: str, path: str, payload: dict) -> dict:
    response = requests.post(f"{base_url}{path}", json=payload, timeout=5)
    response.raise_for_status()
    return response.json()


def main() -> int:
    port = int(os.getenv("SERVER_TEST_PORT", "0")) or _find_free_port()
    base_url = f"http://127.0.0.1:{port}"
    process = _start_server(port)

    try:
        _wait_for_server(base_url)

        health = requests.get(f"{base_url}/health", timeout=5)
        health.raise_for_status()
        health_data = health.json()
        assert health_data["status"] == "ok"
        assert health_data["mock_mode"] is True

        analyze_payload = {
            "fen_before": "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
            "fen_after": "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
            "move_played": "e7e5",
            "player_color": "black",
            "move_number": 1,
        }
        analyze_data = _post_json(base_url, "/analyze-move", analyze_payload)
        assert "feedback" in analyze_data
        assert "[mock]" in analyze_data["feedback"]

        review_payload = {
            "pgn": "1. f3 e5 2. g4 Qh4#",
            "player_color": "white",
        }
        review_data = _post_json(base_url, "/review-game", review_payload)
        assert "review" in review_data
        assert "[mock]" in review_data["review"]

        print("Smoke test passed: /health, /analyze-move, and /review-game responded correctly.")
        return 0
    finally:
        if process.poll() is None:
            if os.name == "nt":
                process.send_signal(signal.CTRL_BREAK_EVENT)
            else:
                process.terminate()

            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()


if __name__ == "__main__":
    raise SystemExit(main())