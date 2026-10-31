#!/usr/bin/env python3
"""Extend the real shared Kestrel fixture with actual N9 media/settings routes.

Trust is confined to the fixture SSLContext and, when enabled, an isolated Chromium
process home/NSS database. Existing user/OS trust is never modified; no certificate
warning bypass, DNS mutation or production data is involved.
"""
import argparse
import hashlib
import http.client
from http.cookies import SimpleCookie
from html import unescape
from pathlib import Path
import re
import os
import ssl
import subprocess
import tempfile
import socket
import struct
import sys
from urllib.parse import urlencode
import zlib


def request(host, context, cookies, path, body=None, content_type=None, origin=True):
    connection = http.client.HTTPSConnection(host, 5445, context=context, timeout=5)
    connection.connect = lambda: setattr(connection, 'sock', context.wrap_socket(
        socket.create_connection(('127.0.0.1', 5445), timeout=5), server_hostname=host))
    headers = {'Host': host + ':5445', 'Cookie': '; '.join(k + '=' + v for k, v in cookies.items())}
    if body is not None:
        headers['Content-Type'] = content_type
        if origin:
            headers['Origin'] = 'https://' + host + ':5445'
    connection.request('GET' if body is None else 'POST', path, body=body, headers=headers)
    response = connection.getresponse()
    for name, value in response.getheaders():
        if name.lower() == 'set-cookie':
            parsed = SimpleCookie(); parsed.load(value)
            cookies.update({key: morsel.value for key, morsel in parsed.items()})
    result = response.status, dict(response.getheaders()), response.read()
    connection.close()
    return result


def verify_n9(host, context, cookies, auth_request, auth_post):
    if os.environ.get('N9_TRUSTED_BROWSER') == '1':
        # Chromium's supported Linux NSS store, scoped to a new disposable child
        # process home. Never call update-ca-certificates or touch existing trust.
        with tempfile.TemporaryDirectory(prefix='n9-browser-trust-') as directory:
            sandbox = Path(directory)
            ca = sandbox / 'fixture-ca.pem'
            ca.write_text(''.join(ssl.DER_cert_to_PEM_cert(cert) for cert in context.get_ca_certs(binary_form=True)))
            nss = sandbox / 'browser-home/.pki/nssdb'
            nss.mkdir(parents=True, mode=0o700)
            subprocess.run(['certutil', '-N', '-d', 'sql:' + str(nss), '--empty-password'], check=True, capture_output=True)
            subprocess.run(['certutil', '-A', '-d', 'sql:' + str(nss), '-n', 'Disposable N9 test root', '-t', 'C,,', '-i', str(ca)], check=True, capture_output=True)
            (sandbox / 'disposable-n9-browser-fixture').write_text('loopback only')
            browser_env = os.environ | {'N9_APP_URL': 'https://' + host + ':5445', 'N9_TLS_SANDBOX': str(sandbox)}
            subprocess.run(['node', '--test', str(Path(__file__).with_name('n9-live-ui.test.cjs'))], env=browser_env, check=True, timeout=240)
        # Browser physical recovery intentionally replaced the earlier Python
        # grant. Obtain fresh physical proof for this separate fixture browser.
        assert auth_post('BeginDm', {'recovery': 'true'})[0] == 302
        status, screen = auth_request('/secure-access/physical'); assert status == 200
        proof = re.search(r'data-physical-code="([^"]+)"', screen)[1]
        assert auth_post('Redeem', {'Code': proof})[0] == 302
    editor = '/appearance/application/00000000-0000-0000-0000-000000000000'
    banner = '/media/application/00000000-0000-0000-0000-000000000000/banner'
    def token():
        status, _, body = request(host, context, cookies, editor)
        assert status == 200
        assert b'HTTPS alone is insufficient' in body
        return unescape(re.search(rb'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body)[1].decode())
    def form(fields, origin=True):
        return request(host, context, cookies, editor + '?handler=Settings', urlencode(fields), 'application/x-www-form-urlencoded', origin)
    csrf = token()
    fields = {'__RequestVerificationToken': csrf, 'ApplicationName': 'Integrated TLS fixture', 'ShowApplicationLogo': 'true'}
    assert form(fields, origin=False)[0] == 403
    assert form({'ApplicationName': 'Rejected without CSRF'})[0] == 400
    assert form(fields)[0] == 302
    assert b'Integrated TLS fixture' in request(host, context, cookies, '/branding/info')[2]
    # Tiny synthetic PNG fixture, independent of the owner's artwork.
    def chunk(name, payload):
        return struct.pack('!I', len(payload)) + name + payload + struct.pack('!I', zlib.crc32(name + payload))
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('!IIBBBBB', 1, 1, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(b'\x00\x30\x70\xa0\xff')) + chunk(b'IEND', b'')
    def upload(payload):
        boundary = 'n9-disposable-test-boundary'
        body = b''
        for name, value in [('__RequestVerificationToken', token()), ('kind', 'banner')]:
            body += f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode()
        body += f'--{boundary}\r\nContent-Disposition: form-data; name="Upload"; filename="fixture.png"\r\nContent-Type: image/png\r\n\r\n'.encode() + payload + f'\r\n--{boundary}--\r\n'.encode()
        return request(host, context, cookies, editor + '?handler=Upload', body, 'multipart/form-data; boundary=' + boundary)
    assert upload(png)[0] == 302
    status, headers, saved = request(host, context, cookies, banner)
    assert status == 200 and headers['Content-Type'] == 'image/png' and saved.startswith(b'\x89PNG')
    assert upload(b'<svg onload="alert(1)"/>')[0] == 400
    assert request(host, context, cookies, banner)[2] == saved
    assert hashlib.sha256(request(host, context, cookies, '/branding/logo')[2]).hexdigest() == '17822fd1b2b7f54f3b084ce19b24d51f50f30e0495529ba8679586145def40b1'
    status, page = auth_request('/secure-access'); assert status == 200
    grant_id = re.search(r'name="id" value="([^"]+)"', page)[1]
    assert auth_post('Revoke', {'id': grant_id})[0] == 302
    assert request(host, context, cookies, banner)[0] == 403
    assert request(host, context, cookies, '/branding/logo')[0] == 200
    # Recover through the real physical-screen flow; existing media is retained.
    assert auth_post('BeginDm', {'recovery': 'true'})[0] == 302
    status, screen = auth_request('/secure-access/physical'); assert status == 200
    code = re.search(r'data-physical-code="([^"]+)"', screen)[1]
    assert auth_post('Redeem', {'Code': code})[0] == 302
    assert request(host, context, cookies, banner)[2] == saved
    return 12, saved


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--fixture', type=Path, required=True)
    parser.add_argument('--publish', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    source = args.fixture / 'https-infra/tests/published_tls_smoke.py'
    text = source.read_text()
    before = "            # Preserve an established TLS stream across cache eviction and >16 rotations."
    assert text.count(before) == 1
    text = text.replace(before, "            n9_count, n9_image = verify_n9(host, context, cookies, auth_request, auth_post)\n            checks += n9_count\n" + before)
    before = "            print(f'PASS: {checks} published Kestrel TLS/rotation/rollback/legacy checks; synthetic process trust only')"
    assert text.count(before) == 1
    text = text.replace(before, "            assert n9_request(host, context, cookies, '/media/application/00000000-0000-0000-0000-000000000000/banner')[2] == n9_image\n            checks += 1\n" + before)
    sys.argv = [str(source), '--publish', str(args.publish), '--dotnet', args.dotnet]
    exec(compile(text, str(source), 'exec'), {'__name__': '__main__', '__file__': str(source), 'verify_n9': verify_n9, 'n9_request': request})


if __name__ == '__main__':
    main()
