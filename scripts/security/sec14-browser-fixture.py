#!/usr/bin/env python3
"""Test-owned loopback browser controls; never a product authorization adapter."""

from __future__ import annotations

import argparse
import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PAGE = b'''<!doctype html><html lang="en"><head><meta charset="utf-8">
<title>SEC-14 isolated browser fixture</title></head><body><h1>Browser control</h1>
<script>
fetch('/api/protected', {headers:{'X-Fixture-Actor':'alpha'}})
.then(response => { if(response.status !== 200) throw new Error('positive failed');
  return fetch('/api/protected', {headers:{'X-Fixture-Actor':'beta'}}); })
.then(response => { document.body.dataset.negativeStatus = String(response.status);
  const link = document.createElement('a'); link.href = '/ajax-' + 'only';
  link.textContent = 'Browser-created destination'; document.body.appendChild(link);
  return fetch(link.href); });
</script></body></html>'''


def serve(weaken: bool) -> None:
    counters = {'root': 0, 'ajaxDiscovery': 0, 'authorized200': 0,
                'crossScope403': 0, 'unauthorized200': 0, 'other': 0}
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            # No URL, headers, request/response body or browser token logging.
            pass

        def do_GET(self):
            route = self.path.split('?', 1)[0]
            response_type = 'text/html; charset=utf-8'
            if route == '/__receipt':
                with lock:
                    body = json.dumps({'schema': 'sec14-browser-fixture-counters-v1',
                                       'scope': 'TEST_OWNED_LOOPBACK_FIXTURE_ONLY',
                                       'weakened': weaken, 'counters': counters}).encode('utf-8')
                status, counter, response_type = 200, None, 'application/json'
            elif route == '/':
                status, body, counter = 200, PAGE, 'root'
            elif route == '/ajax-only':
                status, body, counter = 200, b'<!doctype html><title>AJAX discovery</title>', 'ajaxDiscovery'
            elif route == '/api/protected':
                actor = self.headers.get('X-Fixture-Actor')
                if actor == 'alpha':
                    status, body, counter = 200, b'{"fixtureAuthorized":true}', 'authorized200'
                elif weaken:
                    status, body, counter = 200, b'{"fixtureUnauthorized":true}', 'unauthorized200'
                else:
                    status, body, counter = 403, b'{"denied":true}', 'crossScope403'
                response_type = 'application/json'
            else:
                status, body, counter = 404, b'fixture route unavailable', 'other'
            if counter:
                with lock:
                    counters[counter] += 1
            self.send_response(status)
            self.send_header('Content-Type', response_type)
            self.send_header('Content-Length', str(len(body)))
            self.send_header('Connection', 'close')
            self.end_headers()
            self.wfile.write(body)

    server = ThreadingHTTPServer(('127.0.0.1', 8123), Handler)
    server.serve_forever(poll_interval=0.2)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--weaken', action='store_true')
    serve(parser.parse_args().weaken)
