#!/usr/bin/env python3
import base64, json
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler

class Handler(BaseHTTPRequestHandler):
    def _send(self, payload, status=200):
        data=json.dumps(payload).encode()
        self.send_response(status); self.send_header('Content-Type','application/json'); self.send_header('Content-Length',str(len(data))); self.end_headers(); self.wfile.write(data)
    def do_POST(self):
        raw=self.rfile.read(int(self.headers.get('Content-Length','0')))
        try: body=json.loads(raw)
        except Exception: self._send({'ok':False,'error':'invalid-json'},400); return
        if self.path == '/echo':
            self._send({'ok':True,'value':body.get('value'),'fileName':body.get('fileName')}); return
        if self.path == '/upload':
            encoded=body['handlingar'][0]['filer'][0]['fil']
            try: decoded=base64.b64decode(encoded, validate=True); valid=True
            except Exception: decoded=b''; valid=False
            self._send({'ok':True,'validBase64':valid,'receivedBytes':len(decoded),'fileName':body['handlingar'][0]['filer'][0]['filnamn']}); return
        self._send({'ok':False,'error':'not-found'},404)
    def log_message(self, fmt, *args): print(fmt % args)

if __name__ == '__main__':
    print('Listening on http://127.0.0.1:8787', flush=True)
    ThreadingHTTPServer(('127.0.0.1',8787), Handler).serve_forever()
