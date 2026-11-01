#!/usr/bin/env python3
"""Disposable Docker namespace only: actual Linux NAT, Kestrel TLS and authorization."""
import base64
import functools
import http.client
from http.cookies import SimpleCookie
from html import unescape
import json
import os
from pathlib import Path
import re
import signal
import socket
import ssl
import struct
import subprocess
import sys
import time
from urllib.parse import urlencode

sys.path.insert(0,str(Path(__file__).parents[1]))
import foundation as f
import private443

HOST = 'hub-fixture.colophon.stream'
ROOT = Path('/fixture')
PUBLIC = Path('/public')
V4 = '192.168.253.2'
V6 = 'fd25:443:5445::2'


def context():return ssl.create_default_context(cafile=str(PUBLIC/'ca.pem'))


class Session:
    def __init__(self,address='127.0.0.1',port=443):
        self.address,self.port,self.cookies = address,port,{}
        self.authority = HOST if port == 443 else HOST+':'+str(port)
    def request(self,path,fields=None,*,method=None,authority=None,origin=None):
        connection = http.client.HTTPSConnection(HOST,self.port,timeout=5,context=context())
        connection.connect = lambda:setattr(connection,'sock',context().wrap_socket(socket.create_connection((self.address,self.port),timeout=5),server_hostname=HOST))
        headers = {'Host':authority or self.authority,'Cookie':'; '.join(k+'='+v for k,v in self.cookies.items())}
        if fields is not None:headers |= {'Content-Type':'application/x-www-form-urlencoded','Origin':origin or 'https://'+self.authority}
        connection.request(method or ('POST' if fields is not None else 'GET'),path,body=urlencode(fields) if fields is not None else None,headers=headers)
        response = connection.getresponse();body = response.read().decode();values = response.getheaders()
        for name,value in values:
            if name.lower() == 'set-cookie':
                parsed = SimpleCookie();parsed.load(value)
                for key,item in parsed.items():self.cookies[key] = item.value
        result = response.status,body,values;connection.close();return result
    def post(self,handler,fields=None,*,origin=None):
        status,body,_ = self.request('/secure-access');assert status == 200
        token = unescape(re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',body)[1])
        return self.request('/secure-access?handler='+handler,(fields or {})|{'__RequestVerificationToken':token},origin=origin)
    def enroll(self):
        assert self.post('BeginDm',{'recovery':'true'})[0] == 302
        status,screen,_ = self.request('/secure-access/physical');assert status == 200
        code = re.search(r'data-physical-code="([^"]+)"',screen)[1]
        assert code not in self.request('/secure-access')[1]
        assert self.post('Redeem',{'Code':code},origin='https://'+HOST+':5999')[0] == 403
        status,_,headers = self.post('Redeem',{'Code':code});assert status == 302
        secure = [value.lower() for name,value in headers if name.lower() == 'set-cookie' and value.startswith('__Host-InitiativeTracker.SecureAccess=')]
        assert len(secure) == 1 and all(flag in secure[0] for flag in ['secure','httponly','samesite=strict','path=/']) and 'domain=' not in secure[0]
        assert 'Authorized as DM' in self.request('/secure-access')[1]
        assert self.post('Redeem',{'Code':code})[0] == 403


def probe(generation,leaf,authority_port=443):
    f.probe(HOST,5445,generation,leaf,authority_port=authority_port,_test_context=context())


def wait_app(process):
    for _ in range(100):
        try:
            connection = http.client.HTTPConnection('127.0.0.1',15491,timeout=.5)
            connection.request('GET','/system/health');response = connection.getresponse();response.read();connection.close()
            if response.status == 200:return
        except OSError:pass
        if process.poll() is not None:raise AssertionError('Disposable app exited')
        time.sleep(.1)
    raise AssertionError('Disposable app did not start')


def start_app(ingress='kernel443'):
    def unprivileged():os.setgroups([]);os.setgid(65000);os.setuid(65000)
    env = os.environ|{'BrowserTls__Enabled':'true','BrowserTls__Host':HOST,'BrowserTls__Port':'5445',
        'BrowserTls__PublicPort':'443' if ingress == 'kernel443' else '5445','BrowserTls__Ingress':ingress,
        'BrowserTls__Store':str(ROOT/'tls'),'Tracker__DataDirectory':str(ROOT/'data'),
        'Kestrel__Endpoints__LanHttp__Url':'http://127.0.0.1:15491','Tracker__UseHttpsRedirection':'false',
        'ASPNETCORE_ENVIRONMENT':'Production','Logging__LogLevel__Default':'Warning',
        'SSL_CERT_FILE':str(PUBLIC/'ca.pem'),'SSL_CERT_DIR':str(ROOT/'empty-ca-dir')}
    log = (ROOT/'app.log').open('ab')
    process = subprocess.Popen(['/app/InitiativeTracker.Web'],cwd='/app',env=env,stdout=log,stderr=log,preexec_fn=unprivileged)
    log.close();wait_app(process);return process


def stop_app(process):
    process.terminate()
    try:process.wait(timeout=10)
    except subprocess.TimeoutExpired:process.kill();process.wait(timeout=3)


def websocket(session):
    with context().wrap_socket(socket.create_connection(('127.0.0.1',443),timeout=5),server_hostname=HOST) as stream:
        key = base64.b64encode(os.urandom(16)).decode()
        cookie = '; '.join(k+'='+v for k,v in session.cookies.items())
        stream.sendall(('GET /hubs/encounter HTTP/1.1\r\nHost: '+HOST+'\r\nOrigin: https://'+HOST+
            '\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: '+key+'\r\nCookie: '+cookie+'\r\n\r\n').encode())
        response = b''
        while b'\r\n\r\n' not in response:response += stream.recv(4096)
        assert response.startswith(b'HTTP/1.1 101')
        payload = b'{"protocol":"json","version":1}\x1e';mask = os.urandom(4)
        stream.sendall(bytes([0x81,0x80|len(payload)])+mask+bytes(x^mask[i%4] for i,x in enumerate(payload)))
        response = stream.recv(4096)
        assert b'{}\x1e' in response


def hub():
    os.chmod(ROOT,0o2750);os.chown(ROOT,0,65000)
    for name in ['tls','empty-ca-dir']:
        (ROOT/name).mkdir(mode=0o2750);os.chown(ROOT/name,0,65000);os.chmod(ROOT/name,0o2750)
    (ROOT/'data').mkdir(mode=0o700);os.chown(ROOT/'data',65000,65000)
    def openssl(*args):subprocess.run(['/usr/bin/openssl',*args],cwd=ROOT,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    openssl('req','-x509','-newkey','rsa:2048','-nodes','-keyout','ca.key','-out','ca.pem','-subj','/CN=Disposable Kernel Test Root','-days','3','-addext','basicConstraints=critical,CA:TRUE')
    (PUBLIC/'ca.pem').write_bytes((ROOT/'ca.pem').read_bytes());(PUBLIC/'ca.pem').chmod(0o644)
    (ROOT/'leaf.ext').write_text(f'subjectAltName=DNS:{HOST}\nextendedKeyUsage=serverAuth\nbasicConstraints=critical,CA:FALSE\n')
    for name in ['first','second']:
        openssl('req','-new','-newkey','rsa:2048','-nodes','-keyout',name+'.key','-out',name+'.csr','-subj','/CN='+HOST)
        openssl('x509','-req','-in',name+'.csr','-CA','ca.pem','-CAkey','ca.key','-CAcreateserial','-out',name+'.pem','-days','2','-extfile','leaf.ext')
        (ROOT/(name+'.pem')).chmod(0o600);(ROOT/(name+'.key')).chmod(0o600)
    # Only this disposable container's network namespace is changed. No host mode.
    rules = private443.render([{'interface':'eth0','address':V4},{'interface':'eth0','address':V6}],private443.assigned_interfaces())
    (ROOT/'ingress.nft').write_text(rules)
    subprocess.run(['/usr/sbin/nft','--check','-f',str(ROOT/'ingress.nft')],check=True)
    subprocess.run(['/usr/sbin/nft','-f',str(ROOT/'ingress.nft')],check=True)
    settings = {'hostname':HOST,'port':5445,'store':str(ROOT/'tls')}
    process = start_app('direct')
    try:
        first = f.activate(settings,ROOT/'first.pem',ROOT/'first.key',dry_run=False,
            _probe=lambda h,p,g,leaf:probe(g,leaf,5445),_test_ca=ROOT/'ca.pem')['generation']
        old = Session(port=5445);old.enroll();stop_app(process)
        process = start_app()
        expected = f.leaf_fingerprint((ROOT/'first.pem').read_bytes())
        f.wait_for_probe(lambda h,p,g,leaf:probe(g,leaf),HOST,5445,first,expected)
        replay = Session();replay.cookies = dict(old.cookies)
        assert 'Authorized as DM' not in replay.request('/secure-access')[1]
        assert replay.request('/appearance')[0] == 403
        session = Session();session.enroll()
        for address in ['127.0.0.1','::1']:
            local = Session(address)
            assert local.request('/system/health')[0] == 200
            assert local.request('/secure-access/physical')[0] == 200
        assert session.request('/appearance')[0] == 200
        assert session.request('/appearance?handler=Settings',{})[0] == 400  # CSRF still mandatory.
        backend = Session(port=5445)
        assert backend.request('/system/https-status',authority=HOST)[0] == 200
        assert backend.request('/system/https-status',authority=HOST,method='HEAD')[0] == 200
        for path in ['/system/https-status/','/system/https-status?x=1','/media','/appearance','/secure-access/physical']:
            assert backend.request(path,authority=HOST)[0] in {421,426}
        assert backend.request('/system/https-status',{},authority=HOST)[0] == 421
        assert session.request('/system/health',authority=HOST+':5445')[0] == 421
        websocket(session)
        settings['public_port'] = 443
        second = f.activate(settings,ROOT/'second.pem',ROOT/'second.key',dry_run=False,
            _probe=lambda h,p,g,leaf:probe(g,leaf),_test_ca=ROOT/'ca.pem')['generation']
        try:
            f.activate(settings,ROOT/'first.pem',ROOT/'first.key',dry_run=False,
                _probe=lambda h,p,g,leaf:probe(g,leaf) if g == second else (_ for _ in ()).throw(f.Rejected('Fixture acknowledgement failure')),
                _test_ca=ROOT/'ca.pem')
        except f.Rejected as error:assert 'previous manifest restored' in str(error)
        else:raise AssertionError('Unacknowledged candidate activated')
        stop_app(process);process = start_app()
        f.wait_for_probe(lambda h,p,g,leaf:probe(g,leaf),HOST,5445,second,f.leaf_fingerprint((ROOT/'second.pem').read_bytes()))
        assert 'Authorized as DM' in session.request('/secure-access')[1]
        # Existing HTTP recovery still responds; spoofing cannot grant secure access.
        connection = http.client.HTTPConnection('127.0.0.1',15491,timeout=3)
        connection.request('GET','/secure-access/physical',headers={'X-Forwarded-Proto':'https','X-Forwarded-For':'127.0.0.1','X-Forwarded-Host':HOST})
        response = connection.getresponse();assert response.status == 426;response.read();connection.close()
        (PUBLIC/'ready').write_text('ready\n')
        print('PASS: kernel IPv4/IPv6 OUTPUT, physical proof, strict backend probe, old-origin replay, cookies/CSRF, WebSocket, rotation/rollback/restart; app uid 65000',flush=True)
        signal.signal(signal.SIGTERM,lambda *_:sys.exit(0))
        while True:time.sleep(1)
    finally:stop_app(process)


def remote():
    for address in [V4,V6]:
        session = Session(address)
        assert session.request('/system/health')[0] == 200
        assert session.request('/secure-access')[0] == 200
        for method in ['GET','POST']:
            assert session.request('/secure-access/physical',method=method)[0] == 403
        assert session.request('/system/health',authority=HOST+':5445')[0] == 421
        try:socket.create_connection((address,5445),timeout=.75)
        except OSError:pass
        else:raise AssertionError('Remote direct backend was reachable')
    print('PASS: separate client actual IPv4/IPv6 PREROUTING, trusted TLS, remote physical GET/POST denial and direct 5445 firewall rejection')


if __name__ == '__main__':
    if not Path('/.dockerenv').exists() or os.getpid() != 1 or os.environ.get('IT_DISPOSABLE_KERNEL_TEST') != '1':
        raise SystemExit('Refuse outside an explicit disposable Docker PID/network namespace')
    if sys.argv[1:] == ['hub']:hub()
    elif sys.argv[1:] == ['remote']:remote()
    else:raise SystemExit('Expected hub or remote fixture role')
