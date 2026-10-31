"""Actual kernel Unix peer credentials and activation FD contract; no system account/NM changes."""
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'initiativetracker-satellite-network'
CHILD = r'''
import importlib.machinery, importlib.util, os, pathlib, sys, types
loader = importlib.machinery.SourceFileLoader('actual_socket_broker', sys.argv[1])
spec = importlib.util.spec_from_loader(loader.name, loader)
m = importlib.util.module_from_spec(spec); loader.exec_module(m)
directory = pathlib.Path(sys.argv[2]); m.ROOT = directory
fd = int(sys.argv[3])
if fd != 3: os.dup2(fd, 3)
m.pwd.getpwnam = lambda _: types.SimpleNamespace(pw_uid=int(sys.argv[4]))
def nm(*args):
    (directory/'nm-was-called').write_text('read-only status')
    return '--'
m.nm = nm
os.environ['LISTEN_FDS'] = '1'
os.environ['LISTEN_PID'] = str(os.getpid() + (1 if sys.argv[5] == 'bad-pid' else 0))
print('READY', flush=True)
try:
    m.serve()
except Exception:
    print('BOUNDARY_REFUSED', file=sys.stderr)
    sys.exit(3)
'''


class SocketBoundaryTests(unittest.TestCase):
    def exchange(self, payload=None, uid=None, bad_pid=False):
        with tempfile.TemporaryDirectory() as directory, socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as server:
            location = str(Path(directory)/'activation.sock')
            server.bind(location); os.chmod(location, 0o600); server.listen(1)
            with subprocess.Popen([sys.executable, '-I', '-c', CHILD, str(SCRIPT), directory, str(server.fileno()),
                                   str(os.getuid() if uid is None else uid), 'bad-pid' if bad_pid else 'valid'],
                                  pass_fds=(server.fileno(),), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True) as process:
                self.assertEqual(process.stdout.readline().strip(), 'READY')
                response = b''
                if payload is not None:
                    with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as client:
                        client.settimeout(5); client.connect(location)
                        try:
                            client.sendall(payload); client.shutdown(socket.SHUT_WR)
                            response = client.recv(32768)
                        except (ConnectionResetError, BrokenPipeError):
                            pass
                output, error = process.communicate(timeout=5)
                self.assertNotIn('synthetic-secret', output + error)
                return response, process.returncode, (Path(directory)/'nm-was-called').exists()

    def test_real_peer_uid_can_read_status_through_activated_socket(self):
        response, code, nm = self.exchange(b'{"action":"status"}\n')
        self.assertEqual(code, 0)
        self.assertTrue(json.loads(response)['ok'])
        self.assertTrue(nm)

    def test_claimed_identity_cannot_override_kernel_peer_uid(self):
        expected = os.getuid()+1
        response, code, nm = self.exchange(json.dumps({'action': 'status', 'uid': expected}).encode()+b'\n', uid=expected)
        self.assertFalse(response)
        self.assertEqual(code, 3)
        self.assertFalse(nm)

    def test_wrong_activation_pid_is_rejected_before_accept(self):
        response, code, nm = self.exchange(bad_pid=True)
        self.assertFalse(response); self.assertEqual(code, 3); self.assertFalse(nm)

    def test_extra_data_oversize_and_incomplete_input_never_execute_a_command(self):
        for payload in (b'{"action":"status"}\n{"action":"reset"}\n',
                        b'{"action":"status","padding":"'+b'x'*8200+b'"}\n',
                        b'{"action":"status"}'):
            with self.subTest(size=len(payload)):
                response, _, nm = self.exchange(payload)
                if response:
                    self.assertFalse(json.loads(response)['ok'])
                self.assertFalse(nm)


if __name__ == '__main__':
    unittest.main()
