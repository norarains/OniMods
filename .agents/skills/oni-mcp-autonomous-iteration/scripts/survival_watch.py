#!/usr/bin/env python3
"""Advance an already reviewed work batch; return to the agent on any replan trigger."""
import argparse
import json
from pathlib import Path
import sys
import time
import urllib.request

from loopback_http import open_url

URL = "http://localhost:8788/mcp/"
PROTOCOL = "2025-11-25"


def post(payload, session_id=None, parse=True, timeout=20):
    """Small loopback transport fixture retained for host transport regressions."""
    headers = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream",
               "Mcp-Protocol-Version": PROTOCOL}
    if session_id:
        headers["Mcp-Session-Id"] = session_id
    req = urllib.request.Request(URL, data=json.dumps(payload).encode(), headers=headers)
    with open_url(req, timeout=timeout) as response:
        body = response.read()
        return response, json.loads(body) if parse and body else None


sys.path.insert(0, str(Path(__file__).resolve().parents[4] / "scripts"))
from oni_mcp_bridge import Bridge  # noqa: E402


def call(bridge, request_id, action, **arguments):
    result = bridge.request({"jsonrpc": "2.0", "id": request_id, "method": "tools/call", "params": {
        "name": "game_control", "arguments": {"domain": "speed", "action": action,
        "task": "Advance reviewed work and check safety" if action == "continue" else "Pause after bounded monitoring",
        **arguments}}})[0]
    if "error" in result:
        raise RuntimeError(str(result["error"]))
    result = result["result"]
    content = result.get("content", [])
    text = next((part["text"] for part in content if part.get("type") == "text"), "")
    if result.get("isError"):
        raise RuntimeError(text)
    return json.loads(text) if action == "continue" else text


def run_windows(bridge, target_cycles, max_seconds, window_seconds, speed, emit=print):
    deadline = time.monotonic() + max_seconds
    advanced = 0.0
    request_id = 10
    first = True
    while time.monotonic() + 1 <= deadline and advanced < target_cycles * 600:
        seconds = min(window_seconds, deadline - time.monotonic())
        result = call(bridge, request_id, "continue", seconds=seconds, speed=speed, resetMonitor=first)
        request_id += 1
        first = False
        emit(json.dumps(result, ensure_ascii=False))
        if result.get("isPaused") is not True:
            raise RuntimeError("continue did not confirm pause")
        decision = result.get("decision")
        if decision != "continue":
            return 2 if decision in {"replan", "urgent"} else 1
        advanced += result.get("gameSecondsAdvanced", 0)
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target-cycles", type=float, default=1)
    parser.add_argument("--max-seconds", type=float, default=60)
    parser.add_argument("--poll-seconds", type=float, default=15, help="bounded window duration, 1-20 real seconds")
    parser.add_argument("--speed", type=int, choices=[1, 2, 3], default=3)
    args = parser.parse_args(argv)
    if not (0 < args.target_cycles < float("inf") and 1 <= args.max_seconds < float("inf")
            and 1 <= args.poll_seconds <= 20):
        parser.error("positive finite cycle target, max-seconds >= 1, and poll-seconds 1-20 required")
    bridge = Bridge(URL)
    try:
        init = bridge.request({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
            "protocolVersion": PROTOCOL, "capabilities": {}, "clientInfo": {"name": "oni-bounded-watch", "version": "2"}}})
        if not init or init[0].get("error"):
            raise RuntimeError("MCP initialize failed")
        bridge.request({"jsonrpc": "2.0", "method": "notifications/initialized"})
        call(bridge, 2, "pause")
        return run_windows(bridge, args.target_cycles, args.max_seconds, args.poll_seconds, args.speed)
    finally:
        # A safety pause is not a replay of an uncertain advance. The server also
        # owns a deadline, so transport failure cannot leave an indefinite run.
        try:
            call(bridge, 999999, "pause")
        except Exception:
            print("Pause confirmation unavailable; reconnect after the server's bounded window.", file=sys.stderr)
        bridge.close()


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
