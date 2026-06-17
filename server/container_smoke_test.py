import os
import subprocess
import sys


def main() -> int:
    stockfish_path = os.getenv("STOCKFISH_PATH", "/usr/games/stockfish")

    process = subprocess.Popen(
        [stockfish_path],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    )

    try:
        output, _ = process.communicate("uci\nquit\n", timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()
        print("Stockfish did not respond to UCI within 10 seconds.", file=sys.stderr)
        return 1

    if "uciok" not in output:
        print("Stockfish launched but did not complete UCI handshake.", file=sys.stderr)
        print(output, file=sys.stderr)
        return 1

    print("Container smoke test passed: Stockfish responded to UCI.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
