#!/usr/bin/env python3
"""Real published Kestrel + disposable, PROCESS-ONLY trust; no OS trust or DNS writes."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import socket
import ssl
import subprocess
import tempfile
import time
import urllib.request

spec = importlib.util.spec_from_file_location('foundation', Path(__file__).parents[1] / 'foundation.py')
f = importlib.util.module_from_spec(spec)
spec.loader.exec_module(f)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--publish', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='it-kestrel-tls-') as tmp:
        root = Path(tmp)
        host = 'hub.owner-domain.org'
        store = root / 'tls'
        store.mkdir(mode=0o750)
        def openssl(*argv):
            subprocess.run(['/usr/bin/openssl', *argv], cwd=root, check=True, capture_output=True)
        openssl('req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-keyout', 'ca.key', '-out', 'ca.pem',
                '-subj', '/CN=Disposable Process Only Root', '-days', '3', '-addext', 'basicConstraints=critical,CA:TRUE')
        (root / 'leaf.ext').write_text(f'subjectAltName=DNS:{host}\nextendedKeyUsage=serverAuth\nbasicConstraints=critical,CA:FALSE\n')
        for name in ['first', 'second', 'third']:
            openssl('req', '-new', '-newkey', 'rsa:2048', '-nodes', '-keyout', name + '.key', '-out', name + '.csr', '-subj', '/CN=' + host)
            openssl('x509', '-req', '-in', name + '.csr', '-CA', 'ca.pem', '-CAkey', 'ca.key', '-CAcreateserial', '-out', name + '.pem', '-days', '2', '-extfile', 'leaf.ext')
            (root / (name + '.pem')).chmod(0o600)
            (root / (name + '.key')).chmod(0o600)
        context = ssl.create_default_context(cafile=str(root / 'ca.pem'))
        def probe(h, p, g):
            return f.probe(h, p, g, _test_context=context)
        settings = {'hostname': host, 'store': str(store), 'port': 5445}
        env = os.environ | {'BrowserTls__Enabled': 'true', 'BrowserTls__Host': host, 'BrowserTls__Store': str(store),
                            'Tracker__DataDirectory': str(root / 'data'), 'Tracker__LanPort': '15491',
                            'Kestrel__Endpoints__LanHttp__Url': 'http://127.0.0.1:15491',
                            'Logging__LogLevel__Default': 'Warning',
                            'Tracker__UseHttpsRedirection': 'false', 'ASPNETCORE_ENVIRONMENT': 'Production',
                            'SSL_CERT_FILE': str(root / 'ca.pem'), 'SSL_CERT_DIR': str(root / 'empty-ca-dir')}
        (root / 'empty-ca-dir').mkdir()
        # No network listener outside this disposable app; fixed browser port mirrors checkpoint contract.
        log = (root / 'application.log').open('wb')
        process = subprocess.Popen([args.dotnet, str(args.publish.resolve() / 'InitiativeTracker.Web.dll')], cwd=args.publish.resolve(), env=env, stdout=log, stderr=log)
        checks = 0
        try:
            for _ in range(80):
                try:
                    with urllib.request.urlopen('http://127.0.0.1:15491/system/health', timeout=1) as response:
                        assert response.status == 200
                    break
                except OSError:
                    if process.poll() is not None:
                        raise AssertionError('Published app exited')
                    time.sleep(.25)
            else:
                raise AssertionError('Published app did not start')
            checks += 1
            try:
                probe(host, 5445, 'no-certificate')
            except (OSError, ValueError):
                pass
            else:
                raise AssertionError('TLS without a valid certificate must fail')
            checks += 1
            # Failed initial acknowledgement publishes explicit disable; no false rollback claim.
            try:
                f.activate(settings, root / 'first.pem', root / 'first.key', dry_run=False,
                           _probe=lambda *a: (_ for _ in ()).throw(f.Rejected('fixture outage')), _test_ca=root / 'ca.pem')
            except f.Rejected as error:
                assert 'recovery is unconfirmed' in str(error)
            else:
                raise AssertionError('Unconfirmed initial activation reported success')
            time.sleep(2.5)
            try:
                probe(host, 5445, 'disabled')
            except (OSError, ValueError):
                pass
            else:
                raise AssertionError('Explicit disabled manifest continued accepting TLS')
            checks += 1
            first = f.activate(settings, root / 'first.pem', root / 'first.key', dry_run=False, _probe=probe, _test_ca=root / 'ca.pem')['generation']
            probe(host, 5445, first)
            checks += 1
            unchanged = f.activate(settings, root / 'first.pem', root / 'first.key', dry_run=False, _probe=probe, _test_ca=root / 'ca.pem')
            assert not unchanged['changed'] and unchanged['generation'] == first
            checks += 1
            second = f.activate(settings, root / 'second.pem', root / 'second.key', dry_run=False, _probe=probe, _test_ca=root / 'ca.pem')['generation']
            assert second != first
            probe(host, 5445, second)
            checks += 1
            # Ordinary unmodified public roots reject our synthetic chain.
            try:
                f.probe(host, 5445, second)
            except ssl.SSLError:
                pass
            else:
                raise AssertionError('Public trust accepted synthetic root')
            checks += 1
            # Actual TLS cannot use another hostname, even though the local endpoint is reachable.
            try:
                probe('other.owner-domain.org', 5445, second)
            except (OSError, ValueError):
                pass
            else:
                raise AssertionError('Wrong SNI accepted')
            checks += 1
            # A valid TLS handshake cannot authorize an unrelated Host header or port.
            for bad_host in ['other.owner-domain.org:5445', host + ':443']:
                with socket.create_connection(('127.0.0.1', 5445), timeout=3) as raw:
                    with context.wrap_socket(raw, server_hostname=host) as connection:
                        connection.sendall(f'GET /system/health HTTP/1.0\r\nHost: {bad_host}\r\n\r\n'.encode())
                        response = connection.recv(4096)
                        assert response.startswith((b'HTTP/1.1 421', b'HTTP/1.0 421'))
                checks += 1
            # Invalid on-disk generation cannot displace the valid serving generation.
            corrupt = 'a' * 32
            (store / corrupt).mkdir(mode=0o750)
            (store / corrupt / 'bundle.pfx').write_bytes(b'not a PKCS12')
            (store / corrupt / 'bundle.pfx').chmod(0o640)
            f.write_active(store, {'generation': corrupt})
            time.sleep(2.5)
            probe(host, 5445, second)
            checks += 1
            f.write_active(store, {'generation': second})
            def reject_candidate(h, p, g):
                if g != second:
                    raise f.Rejected('simulated acknowledgement outage')
                return probe(h, p, g)
            try:
                f.activate(settings, root / 'third.pem', root / 'third.key', dry_run=False, _probe=reject_candidate, _test_ca=root / 'ca.pem')
            except f.Rejected as error:
                assert 'previous manifest restored' in str(error)
            else:
                raise AssertionError('Failed acknowledgement reported success')
            probe(host, 5445, second)
            checks += 1
            # Sensitive requests reject HTTP and spoofed forwarding headers before app authorization.
            for path in ['/media?id=fixture', '/appearance?handler=Settings']:
                request = urllib.request.Request('http://127.0.0.1:15491' + path,
                    data=b'sensitive' if path.startswith('/appearance') else None,
                    headers={'X-Forwarded-Proto': 'https', 'X-Forwarded-Host': host + ':5445'})
                try:
                    urllib.request.urlopen(request, timeout=2)
                except urllib.error.HTTPError as error:
                    assert error.code == 426
                else:
                    raise AssertionError('Sensitive HTTP accepted')
                checks += 1
            with urllib.request.urlopen('http://127.0.0.1:15491/system/health', timeout=2) as response:
                assert response.status == 200
            checks += 1
            print(f'PASS: {checks} published Kestrel TLS/rotation/rollback/legacy checks; synthetic process trust only')
        except Exception:
            log.flush()
            # Logs belong solely to disposable fixtures. Never print production or provider logs.
            print((root / 'application.log').read_text()[-16000:])
            raise
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
            log.close()

if __name__ == '__main__':
    main()
