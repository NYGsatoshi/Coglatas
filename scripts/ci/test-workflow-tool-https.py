"""Exercise the workflow's real curl arguments against isolated loopback TLS servers.

Only transport behavior is tested: synthetic bytes do not replace or qualify the
production release archive hashes. No release server or real credential is used.
"""
from contextlib import ExitStack
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import shlex
import shutil
import ssl
import subprocess
import tempfile
from threading import Thread
import unittest


WORKFLOW = Path(__file__).resolve().parents[2] / '.github/workflows/workflow-static-analysis.yml'
PAYLOAD = b'Synthetic workflow installer transport control\n'


class InstallerTransportTests(unittest.TestCase):
    def setUp(self):
        self.resources = ExitStack()
        self.addCleanup(self.resources.close)
        self.directory = Path(self.resources.enter_context(tempfile.TemporaryDirectory()))
        self.curl = self.tool('curl')
        certificate = self.directory / 'certificate.pem'
        key = self.directory / 'key.pem'
        subprocess.run([
            self.tool('openssl'), 'req', '-x509', '-newkey', 'rsa:2048', '-nodes',
            '-keyout', str(key), '-out', str(certificate), '-days', '1',
            '-subj', '/CN=localhost', '-addext', 'subjectAltName=DNS:localhost',
        ], check=True, capture_output=True, timeout=10)
        self.certificate = certificate
        tls = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        tls.minimum_version = ssl.TLSVersion.TLSv1_2
        tls.load_cert_chain(certificate, key)
        self.requests = []
        servers = {}
        requests = self.requests

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                scheme = 'https' if isinstance(self.connection, ssl.SSLSocket) else 'http'
                requests.append((scheme, self.path))
                redirects = {
                    '/secure': ('https', servers['target'].server_port, '/payload'),
                    '/downgrade': ('http', servers['plain'].server_port, '/payload'),
                    '/multi-hop': ('https', servers['target'].server_port, '/downgrade'),
                }
                if self.path in redirects:
                    protocol, port, path = redirects[self.path]
                    self.send_response(302)
                    self.send_header('Location', f'{protocol}://localhost:{port}{path}')
                    self.send_header('Content-Length', '0')
                    self.end_headers()
                elif self.path == '/payload':
                    self.send_response(200)
                    self.send_header('Content-Length', str(len(PAYLOAD)))
                    self.end_headers()
                    self.wfile.write(PAYLOAD)
                else:
                    self.send_error(404)

            def log_message(self, _format, *args):
                pass

        for name in ('plain', 'origin', 'target'):
            server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
            server.daemon_threads = True
            if name != 'plain':
                server.socket = tls.wrap_socket(server.socket, server_side=True)
            servers[name] = server
            thread = Thread(target=server.serve_forever, kwargs={'poll_interval': 0.01}, daemon=True)
            thread.start()
            self.resources.callback(self.stop_server, server, thread)
        self.servers = servers

    @staticmethod
    def tool(name):
        path = shutil.which(name)
        if path is None or not Path(path).is_absolute():
            raise RuntimeError('An installed absolute native tool is required: ' + name)
        return path

    @staticmethod
    def stop_server(server, thread):
        server.shutdown()
        server.server_close()
        thread.join(timeout=5)
        if thread.is_alive():
            raise RuntimeError('Synthetic transport server did not stop.')

    def commands(self):
        commands = [shlex.split(line.strip()) for line in WORKFLOW.read_text(encoding='utf-8').splitlines()
                    if line.lstrip().startswith('curl ')]
        self.assertEqual(len(commands), 2, 'Both original installer download commands must be exercised.')
        self.assertTrue(any('rhysd/actionlint/' in token for token in commands[0]))
        self.assertTrue(any('zizmorcore/zizmor/' in token for token in commands[1]))
        return commands

    def transfer(self, command, path, *, trust=True, initial_scheme='https'):
        arguments = list(command)
        self.assertEqual(arguments[0], 'curl')
        arguments[0] = self.curl
        urls = [index for index, value in enumerate(arguments) if value.startswith('https://')]
        self.assertEqual(len(urls), 1)
        server = self.servers['origin' if initial_scheme == 'https' else 'plain']
        arguments[urls[0]] = f'{initial_scheme}://localhost:{server.server_port}{path}'
        output = self.directory / 'download.bin'
        output.unlink(missing_ok=True)
        arguments[arguments.index('--output') + 1] = str(output)
        if trust:
            arguments.extend(['--cacert', str(self.certificate)])
        environment = {key: value for key, value in os.environ.items()
                       if not key.upper().endswith('_PROXY') and key.upper() not in
                       {'CURL_CA_BUNDLE', 'SSL_CERT_FILE', 'SSL_CERT_DIR'}}
        environment['NO_PROXY'] = '*'
        before = len(self.requests)
        result = subprocess.run(arguments, env=environment, capture_output=True, timeout=10)
        requests = self.requests[before:]
        content = output.read_bytes() if output.exists() else b''
        # Closed synthetic diagnostics; do not print URLs, certificate keys, or child stderr.
        print(json.dumps({'scenario': path, 'trustedSyntheticCa': trust, 'exitCode': result.returncode,
                          'httpsRequests': sum(scheme == 'https' for scheme, _ in requests),
                          'httpRequests': sum(scheme == 'http' for scheme, _ in requests),
                          'syntheticPayloadMatches': content == PAYLOAD}), flush=True)
        return result, requests, content

    def test_https_redirects_preserve_exact_synthetic_bytes(self):
        for command in self.commands():
            with self.subTest(command=command[1:]):
                result, requests, content = self.transfer(command, '/secure')
                self.assertEqual(result.returncode, 0)
                self.assertEqual(requests, [('https', '/secure'), ('https', '/payload')])
                self.assertEqual(content, PAYLOAD)
                self.assertEqual(hashlib.sha256(content).digest(), hashlib.sha256(PAYLOAD).digest())

    def test_http_redirect_is_rejected_before_plaintext_request(self):
        for command in self.commands():
            with self.subTest(command=command[1:]):
                result, requests, content = self.transfer(command, '/downgrade')
                self.assertEqual(requests, [('https', '/downgrade')])
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(content, b'')

    def test_later_http_hop_is_also_rejected(self):
        for command in self.commands():
            with self.subTest(command=command[1:]):
                result, requests, content = self.transfer(command, '/multi-hop')
                self.assertEqual(requests, [('https', '/multi-hop'), ('https', '/downgrade')])
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(content, b'')

    def test_initial_http_is_rejected_before_any_request(self):
        for command in self.commands():
            with self.subTest(command=command[1:]):
                result, requests, content = self.transfer(command, '/payload', initial_scheme='http')
                self.assertEqual(requests, [])
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(content, b'')

    def test_untrusted_certificate_remains_rejected(self):
        for command in self.commands():
            with self.subTest(command=command[1:]):
                result, requests, content = self.transfer(command, '/secure', trust=False)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(requests, [])
                self.assertEqual(content, b'')


if __name__ == '__main__':
    unittest.main()
