#!/usr/bin/env python3
"""Tiny local server for the ControlFlowScenarios instance (no internet needed).

  GET /items      -> {"ok": true, "id": "A7", "token": "T123", "count": 3, "items": ["a","b","c"]}
  GET /echo/<x>   -> {"echo": "<x>", "path": "/echo/<x>"}
  GET /reset      -> resets the poll counter
  GET /poll       -> state PENDING twice, then DONE (call /reset first)
Every request is printed, so you can see which steps ran and in which order.
"""
import json, sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

polls = 0
SEEN = None   # verify.py sets this to a list to record the requests

class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    def _send(self, body):
        data = json.dumps(body).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        global polls
        length = int(self.headers.get("Content-Length") or 0)
        if length:
            self.rfile.read(length)   # the runner sends the template ({}) as a body even for GET
        path = self.path.split("?")[0]
        if path == "/items":
            self._send({"ok": True, "id": "A7", "token": "T123", "count": 3, "items": ["a", "b", "c"]})
        elif path.startswith("/echo/"):
            self._send({"echo": path[len("/echo/"):], "path": path})
        elif path == "/reset":
            polls = 0
            self._send({"reset": True})
        elif path == "/poll":
            polls += 1
            self._send({"state": "DONE" if polls > 2 else "PENDING", "polls": polls})
        else:
            self._send({"path": path})

    do_POST = do_GET

    def log_message(self, fmt, *args):
        print("  server:", self.command, self.path, flush=True)
        if SEEN is not None:
            SEEN.append(self.path)

if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8099
    print(f"ControlFlowScenarios server on http://127.0.0.1:{port}  (Ctrl+C to stop)", flush=True)
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
