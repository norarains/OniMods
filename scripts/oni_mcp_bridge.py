#!/usr/bin/env python3
"""Local stdio MCP transport for ONI, including Windows-hosted ONI from WSL.

No third-party packages, public listener, automatic replay, or credentials on disk.
Windows uses one persistent PowerShell HTTP worker, bypassing WSL localhost routing.
"""
from __future__ import annotations

import argparse
import base64
import json
import os
from pathlib import Path
import platform
import queue
import shutil
import subprocess
import sys
import threading
import urllib.error
import urllib.parse
import urllib.request

PROTOCOL = "2025-11-25"
MAX_MESSAGE = 16 * 1024 * 1024


def validate_url(url):
    parsed = urllib.parse.urlsplit(url)
    if (parsed.scheme != "http" or parsed.hostname not in {"localhost", "127.0.0.1", "::1"}
            or parsed.username or parsed.password or parsed.query or parsed.fragment):
        raise ValueError("ONI MCP requires an HTTP loopback URL without credentials or query parameters")
    if parsed.port is not None and not 1 <= parsed.port <= 65535:
        raise ValueError("Invalid MCP port")
    return url


def is_wsl():
    return "microsoft" in platform.release().lower()


def parse_messages(body):
    if not body.strip():
        return []
    try:
        return [json.loads(body)]
    except json.JSONDecodeError:
        messages = []
        data = []
        for line in body.replace("\r\n", "\n").split("\n") + [""]:
            if line.startswith("data:"):
                data.append(line[5:].lstrip(" "))
            elif not line and data:
                messages.append(json.loads("\n".join(data)))
                data = []
        if not messages:
            raise ValueError("ONI returned a non-JSON MCP response; check the endpoint and host")
        return messages


class DirectTransport:
    def __init__(self):
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *args, **kwargs):
                return None
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    def post(self, url, data, headers, timeout):
        headers = dict(headers, **{"Content-Type": "application/json; charset=utf-8",
                                   "Accept": "application/json, text/event-stream"})
        request = urllib.request.Request(url, data=data, headers=headers)
        try:
            response = self.opener.open(request, timeout=timeout)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            body = response.read(MAX_MESSAGE + 1)
            if len(body) > MAX_MESSAGE:
                raise ValueError("MCP response exceeds the bridge message limit")
            return {"status": response.status, "session": response.headers.get("Mcp-Session-Id"),
                    "body": body.decode("utf-8-sig")}

    def close(self):
        pass


class WindowsTransport:
    def __init__(self):
        executable = shutil.which("powershell.exe")
        fallback = Path("/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe")
        if executable is None and fallback.is_file():
            executable = str(fallback)
        if executable is None:
            raise RuntimeError("Windows PowerShell interop is unavailable; enable WSL interop or run the client on Windows")
        script = Path(__file__).with_name("oni_mcp_windows_worker.ps1").read_text(encoding="utf-8")
        encoded = base64.b64encode(script.encode("utf-16le")).decode("ascii")
        self.process = subprocess.Popen(
            [executable, "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
            encoding="utf-8", bufsize=1)
        self.responses = queue.Queue()
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        for line in self.process.stdout:
            self.responses.put(line)
        self.responses.put(None)

    def post(self, url, data, headers, timeout):
        message = {"url": url, "body": base64.b64encode(data).decode("ascii"),
                   "headers": headers, "timeoutMs": int(timeout * 1000)}
        self.process.stdin.write(json.dumps(message) + "\n")
        self.process.stdin.flush()
        try:
            line = self.responses.get(timeout=timeout + 2)
        except queue.Empty:
            self.close()
            raise TimeoutError("Windows MCP transport timed out; the request was not replayed") from None
        if line is None:
            raise RuntimeError("Windows interop worker exited; verify WSL interop and the client execution environment")
        result = json.loads(line.lstrip("\ufeff"))
        if result.get("error"):
            raise RuntimeError(result["error"])
        return result

    def close(self):
        if self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=2)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=2)


class Bridge:
    def __init__(self, url, backend="auto", timeout=25):
        self.url = validate_url(url)
        self.timeout = timeout
        self.session = None
        self.protocol = PROTOCOL
        windows = backend == "windows" or (backend == "auto" and is_wsl())
        self.transport = WindowsTransport() if windows else DirectTransport()
        self.backend = "windows" if windows else "direct"

    def request(self, message):
        if message.get("method") == "initialize":
            # No independent receive channel for server-initiated requests.
            message = dict(message, params=dict(message.get("params", {}), capabilities={}))
        headers = {"Mcp-Protocol-Version": self.protocol}
        if self.session:
            headers["Mcp-Session-Id"] = self.session
        token = os.environ.get("ONI_MCP_TOKEN")
        if token:
            headers["Authorization"] = "Bearer " + token
        # Forward exactly once. A failed POST may already have applied game actions.
        response = self.transport.post(self.url, json.dumps(message, ensure_ascii=False).encode("utf-8"),
                                       headers, self.timeout)
        if not 200 <= response["status"] < 300:
            raise RuntimeError("ONI MCP HTTP " + str(response["status"])
                               + "; check the port, enabled mod, and ONI_MCP_TOKEN. Request was not replayed.")
        if response.get("session"):
            self.session = response["session"]
        messages = parse_messages(response.get("body", ""))
        if message.get("method") == "initialize":
            for item in messages:
                result = item.get("result", {})
                if result.get("protocolVersion"):
                    self.protocol = result["protocolVersion"]
                capabilities = result.get("capabilities", {})
                # This sequential bridge has no independent SSE receive channel.
                capabilities.pop("experimental", None)
                for name in ("tools", "resources", "prompts"):
                    if isinstance(capabilities.get(name), dict):
                        capabilities[name].pop("listChanged", None)
                        capabilities[name].pop("subscribe", None)
        return messages

    def close(self):
        self.transport.close()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://localhost:8788/mcp/")
    parser.add_argument("--backend", choices=["auto", "direct", "windows"], default="auto")
    parser.add_argument("--check", action="store_true", help="Initialize, discover tools, print a compact health check, and exit")
    args = parser.parse_args(argv)
    bridge = Bridge(args.url, args.backend)
    try:
        if args.check:
            initialized = bridge.request({"jsonrpc":"2.0", "id":1, "method":"initialize", "params":{
                "protocolVersion":PROTOCOL, "capabilities":{}, "clientInfo":{"name":"oni-bridge-check","version":"1"}}})
            if not initialized or initialized[0].get("error"):
                raise RuntimeError("ONI MCP initialization failed")
            bridge.request({"jsonrpc":"2.0", "method":"notifications/initialized"})
            tools = bridge.request({"jsonrpc":"2.0", "id":2, "method":"tools/list"})
            print(json.dumps({"ok":True,"backend":bridge.backend,"endpoint":bridge.url,
                              "server":initialized[0]["result"]["serverInfo"],
                              "tools":[t["name"] for t in tools[0]["result"]["tools"]]}, ensure_ascii=False))
            return 0
        while True:
            line = sys.stdin.buffer.readline(MAX_MESSAGE + 1)
            if not line:
                return 0
            if len(line) > MAX_MESSAGE:
                raise ValueError("MCP request exceeds the bridge message limit")
            message = json.loads(line)
            try:
                replies = bridge.request(message)
            except Exception as error:
                # Errors retain the request ID, never retry mutations, and never print credentials.
                if "id" in message:
                    replies = [{"jsonrpc":"2.0", "id":message["id"],
                                "error":{"code":-32000, "message":str(error)}}]
                else:
                    print("ONI MCP notification delivery failed; reconnect the client.", file=sys.stderr)
                    return 1
            for reply in replies:
                sys.stdout.write(json.dumps(reply, ensure_ascii=False) + "\n")
                sys.stdout.flush()
    finally:
        bridge.close()


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print("ONI MCP bridge: " + str(error), file=sys.stderr)
        sys.exit(1)
