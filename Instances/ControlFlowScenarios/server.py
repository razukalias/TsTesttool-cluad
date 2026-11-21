#!/usr/bin/env python3
"""Tiny local server for the ControlFlowScenarios instance (no internet needed).

  GET /items      -> {"ok": true, "id": "A7", "token": "T123", "count": 3, "items": ["a","b","c"]}
  GET /echo/<x>   -> {"echo": "<x>", "path": "/echo/<x>"}
  GET /reset      -> resets the poll counter
  GET /poll       -> state PENDING twice, then DONE (call /reset first)
  GET /orders/<id>   -> a large nested response (objects, arrays of objects, nested arrays, nulls, odd key names);
                        ids O1, O2, O3 differ in size and values, anything else gives a small one
  GET /customers/<id> -> a second nested shape (profile, addresses, tags, permissions)
Every request is printed, so you can see which steps ran and in which order.
"""
import json, sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

polls = 0

def order(oid):
    n = {"O1": 3, "O2": 1, "O3": 5}.get(oid, 2)
    lines = []
    for i in range(1, n + 1):
        lines.append({
            "lineNo": i, "sku": f"SKU-{oid}-{i}", "qty": i * 2, "price": {"amount": round(9.99 * i, 2), "currency": "EUR"},
            "discount": None if i % 2 else {"code": f"D{i}", "percent": 5 * i},
            "options": [{"name": "color", "value": ["red", "green", "blue"][i % 3]}, {"name": "size", "value": "M"}],
            "warehouses": [{"id": "W1", "stock": 10 * i, "bins": ["A1", "B2"]}, {"id": "W2", "stock": 0, "bins": []}],
        })
    return {
        "id": oid, "status": "CREATED" if oid != "O2" else "PAID", "total": round(sum(l["price"]["amount"] * l["qty"] for l in lines), 2),
        "express": oid == "O3", "note": None, "tags": ["web", "priority"][: 1 + (oid == "O1")],
        "customer": {"id": "C" + oid[1:], "name": "Ünal Müller", "email": f"customer{oid[1:]}@example.com",
                     "address": {"street": "Hauptstr. 1", "city": "Berlin", "geo": {"lat": 52.52, "lon": 13.405}},
                     "phones": [{"type": "mobile", "number": "+49 170 000" + oid[1:]}, {"type": "home", "number": "+49 30 000" + oid[1:]}]},
        "lines": lines,
        "shipping": {"method": "DHL", "tracking": {"codes": ["TR" + oid[1:] + "A", "TR" + oid[1:] + "B"], "events": [[{"at": "2026-11-01", "what": "picked"}], [{"at": "2026-11-02", "what": "sent"}, {"at": "2026-11-03", "what": "delivered"}]]}},
        "meta": {"x-trace-id": "abc-" + oid, "created by": "shop", "2fa": True, "empty": {}, "none": []},
    }

def customer(cid):
    return {"id": cid, "profile": {"first": "Ada", "last": "Lovelace", "age": 36, "vip": cid == "C1"},
            "addresses": [{"kind": "home", "city": "Berlin", "zip": "10115"}, {"kind": "work", "city": "Hamburg", "zip": "20095"}],
            "tags": ["a", "b", "c", "d"], "permissions": {"read": True, "write": False, "scopes": ["orders", "customers"]}, "history": []}

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
        elif path.startswith("/orders/"):
            self._send(order(path[len("/orders/"):]))
        elif path.startswith("/customers/"):
            self._send(customer(path[len("/customers/"):]))
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
