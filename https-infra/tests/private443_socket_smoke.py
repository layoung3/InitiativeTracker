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
import zlib
from urllib.parse import urlencode

sys.path.insert(0,str(Path(__file__).parents[1]))
import foundation as f
import private443

HOST = 'hub-fixture.colophon.stream'
ROOT = Path('/fixture')
PUBLIC = Path('/public')
V4 = '192.168.253.2'
V6 = 'fd25:443:5445::2'
HOTSPOT = '192.168.253.4'
EDITOR = '/appearance/application/00000000-0000-0000-0000-000000000000'
BANNER = '/media/application/00000000-0000-0000-0000-000000000000/banner'


def context():return ssl.create_default_context(cafile=str(PUBLIC/'ca.pem'))


class Session:
    def __init__(self,address='127.0.0.1',port=443):
        self.address,self.port,self.cookies = address,port,{}
        self.authority = HOST if port == 443 else HOST+':'+str(port)
    def request(self,path,fields=None,*,method=None,authority=None,origin=None,body=None,content_type=None,binary=False):
        connection = http.client.HTTPSConnection(HOST,self.port,timeout=5,context=context())
        connection.connect = lambda:setattr(connection,'sock',context().wrap_socket(socket.create_connection((self.address,self.port),timeout=5),server_hostname=HOST))
        headers = {'Host':authority or self.authority,'Cookie':'; '.join(k+'='+v for k,v in self.cookies.items())}
        if fields is not None or body is not None:headers |= {'Content-Type':content_type or 'application/x-www-form-urlencoded','Origin':origin or 'https://'+self.authority}
        connection.request(method or ('POST' if fields is not None or body is not None else 'GET'),path,body=urlencode(fields) if fields is not None else body,headers=headers)
        response = connection.getresponse();body = response.read();values = response.getheaders()
        for name,value in values:
            if name.lower() == 'set-cookie':
                parsed = SimpleCookie();parsed.load(value)
                for key,item in parsed.items():self.cookies[key] = item.value
        result = response.status,body if binary else body.decode(),values;connection.close();return result
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
    try:f.probe(HOST,5445,generation,leaf,authority_port=authority_port,_test_context=context())
    except (OSError,ValueError) as error:
        # Class only, never a TLS/provider message, code, cookie, key or body.
        print('Fixture probe failed: '+type(error).__name__+' verify-code='+str(getattr(error,'verify_code',None)),flush=True);raise


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


def websocket(session,origin=None):
    with context().wrap_socket(socket.create_connection(('127.0.0.1',443),timeout=5),server_hostname=HOST) as stream:
        key = base64.b64encode(os.urandom(16)).decode()
        cookie = '; '.join(k+'='+v for k,v in session.cookies.items())
        stream.sendall(('GET /hubs/encounter HTTP/1.1\r\nHost: '+HOST+'\r\nOrigin: '+(origin or 'https://'+HOST)+
            '\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: '+key+'\r\nCookie: '+cookie+'\r\n\r\n').encode())
        response = b''
        while b'\r\n\r\n' not in response:response += stream.recv(4096)
        if origin is not None:
            assert response.startswith(b'HTTP/1.1 403');return
        assert response.startswith(b'HTTP/1.1 101')
        payload = b'{"protocol":"json","version":1}\x1e';mask = os.urandom(4)
        stream.sendall(bytes([0x81,0x80|len(payload)])+mask+bytes(x^mask[i%4] for i,x in enumerate(payload)))
        response = b''
        while b'{}\x1e' not in response and len(response) < 16384:
            chunk=stream.recv(4096)
            if not chunk:break
            response += chunk
        assert b'{}\x1e' in response


def media(session):
    def token():
        status,page,_ = session.request(EDITOR);assert status == 200
        return unescape(re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',page)[1])
    assert session.request(EDITOR+'?handler=Settings',{})[0] == 400
    fields = {'__RequestVerificationToken':token(),'ApplicationName':'Private 443 fixture','ShowApplicationLogo':'true'}
    assert session.request(EDITOR+'?handler=Settings',fields,origin='https://other.colophon.stream')[0] == 403
    assert session.request(EDITOR+'?handler=Settings',fields)[0] == 302
    assert 'Private 443 fixture' in session.request('/branding/info')[1]
    def chunk(name,payload):return struct.pack('!I',len(payload))+name+payload+struct.pack('!I',zlib.crc32(name+payload))
    png = b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('!IIBBBBB',1,1,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(b'\x00\x20\x60\xa0\xff'))+chunk(b'IEND',b'')
    def upload(image):
        boundary = 'disposable-443-fixture'
        body = b''
        for key,value in [('__RequestVerificationToken',token()),('kind','banner')]:
            body += f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode()
        body += f'--{boundary}\r\nContent-Disposition: form-data; name="Upload"; filename="synthetic.png"\r\nContent-Type: image/png\r\n\r\n'.encode()+image+f'\r\n--{boundary}--\r\n'.encode()
        return session.request(EDITOR+'?handler=Upload',body=body,content_type='multipart/form-data; boundary='+boundary)[0]
    assert upload(png) == 302
    status,saved,headers = session.request(BANNER,binary=True)
    assert status == 200 and saved.startswith(b'\x89PNG') and ('Content-Type','image/png') in headers
    assert upload(b'<svg onload="bad()"/>') == 400
    assert session.request(BANNER,binary=True)[1] == saved
    assert session.request('/api/coordination/enroll',{})[0] == 403
    _,page,_ = session.request('/secure-access')
    grant = re.search(r'name="id" value="([^"]+)"',page)[1]
    assert session.post('Revoke',{'id':grant})[0] == 302
    assert session.request(BANNER)[0] == 403
    session.enroll()
    assert session.request(BANNER,binary=True)[1] == saved
    return saved


def hub():
    os.chmod(ROOT,0o2750);os.chown(ROOT,0,65000)
    for name in ['tls','empty-ca-dir']:
        (ROOT/name).mkdir(mode=0o2750);os.chown(ROOT/name,0,65000);os.chmod(ROOT/name,0o2750)
    (ROOT/'data').mkdir(mode=0o700);os.chown(ROOT/'data',65000,65000)
    def openssl(*args):subprocess.run(['/usr/bin/openssl',*args],cwd=ROOT,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    openssl('req','-x509','-newkey','rsa:2048','-nodes','-keyout','ca.key','-out','ca.pem','-subj','/CN=Disposable Kernel Test Root','-days','3',
        '-addext','basicConstraints=critical,CA:TRUE','-addext','keyUsage=critical,keyCertSign,cRLSign',
        '-addext','subjectKeyIdentifier=hash','-addext','authorityKeyIdentifier=keyid:always')
    (PUBLIC/'ca.pem').write_bytes((ROOT/'ca.pem').read_bytes());(PUBLIC/'ca.pem').chmod(0o644)
    (ROOT/'leaf.ext').write_text(f'subjectAltName=DNS:{HOST}\nextendedKeyUsage=serverAuth\nbasicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\nsubjectKeyIdentifier=hash\nauthorityKeyIdentifier=keyid,issuer\n')
    for name in ['first','second']:
        openssl('req','-new','-newkey','rsa:2048','-nodes','-keyout',name+'.key','-out',name+'.csr','-subj','/CN='+HOST)
        openssl('x509','-req','-in',name+'.csr','-CA','ca.pem','-CAkey','ca.key','-CAcreateserial','-out',name+'.pem','-days','2','-extfile','leaf.ext')
        (ROOT/(name+'.pem')).chmod(0o600);(ROOT/(name+'.key')).chmod(0o600)
    # Only this disposable container's network namespace is changed. No host mode.
    profile_id = 'ddae7300-1111-4111-8111-111111111111'
    # NM profile provenance is a contract fixture; real kernel transitions below
    # do not pretend a NetworkManager daemon or Wi-Fi hardware exists here.
    profile = {'id':profile_id,'interface':'eth0','mode':'ap','method':'shared','addresses':HOTSPOT+'/24','ipv6':'disabled'}
    rules = private443.render([{'interface':'eth0','address':V4},{'interface':'eth0','address':V6},
        {'interface':'eth0','address':HOTSPOT,'hotspot_profile':profile_id}],private443.assigned_interfaces(),_hotspot_reader=lambda _:profile)
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
        assert replay.request(EDITOR)[0] == 403
        session = Session();session.enroll()
        for address in ['127.0.0.1','::1']:
            local = Session(address)
            assert local.request('/system/health')[0] == 200
            assert local.request('/secure-access/physical')[0] == 200
        saved_banner = media(session)
        backend = Session(port=5445)
        assert backend.request('/system/https-status',authority=HOST)[0] == 200
        assert backend.request('/system/https-status',authority=HOST,method='HEAD')[0] == 200
        for path in ['/system/https-status/','/system/https-status?x=1','/media','/appearance','/secure-access/physical']:
            assert backend.request(path,authority=HOST)[0] in {421,426}
        assert backend.request('/system/https-status',{},authority=HOST)[0] == 421
        assert session.request('/system/health',authority=HOST+':5445')[0] == 421
        websocket(session)
        websocket(session,origin='https://other.colophon.stream')
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
        assert session.request(BANNER,binary=True)[1] == saved_banner
        # Existing HTTP recovery still responds; spoofing cannot grant secure access.
        connection = http.client.HTTPConnection('127.0.0.1',15491,timeout=3)
        connection.request('GET','/secure-access/physical',headers={'X-Forwarded-Proto':'https','X-Forwarded-For':'127.0.0.1','X-Forwarded-Host':HOST})
        response = connection.getresponse();assert response.status == 426;response.read();connection.close()
        (PUBLIC/'ready').write_text('ready\n')
        print('PASS: kernel IPv4/IPv6 OUTPUT, physical proof, strict backend probe, old-origin replay, cookies/CSRF, private N9 upload/settings/revocation, WebSocket, rotation/rollback/restart; app uid 65000',flush=True)
        signal.signal(signal.SIGTERM,lambda *_:sys.exit(0))
        for requested,finished,commands in [
            ('switch-hotspot','hotspot-ready',[("del",V4+'/24'),("del",V6+'/64'),("add",HOTSPOT+'/24')]),
            ('switch-home','home-ready',[("del",HOTSPOT+'/24'),("add",V4+'/24'),("add",V6+'/64')])]:
            while not (PUBLIC/requested).exists():time.sleep(.1)
            for action,address in commands:
                # Deterministic fixture-only IPv6 alias restoration, no radio/DAD claim.
                subprocess.run(['/usr/sbin/ip','address',action,address,'dev','eth0']+(['nodad'] if action == 'add' and ':' in address else []),check=True)
            assert session.request(BANNER,binary=True)[1] == saved_banner
            (PUBLIC/finished).write_text('ready\n')
        print('PASS: actual home → fixed hotspot → home address transitions, unchanged nftables/app, preserved secure grant/media; NetworkManager metadata is a contract fixture',flush=True)
        while True:time.sleep(1)
    except Exception:
        for path in [ROOT,ROOT/'tls',*(ROOT/'tls').glob('*/bundle.pfx'),ROOT/'tls'/'active.json',PUBLIC,PUBLIC/'ca.pem']:
            if path.exists():
                info=path.stat();print('Fixture permission receipt:',path.name,oct(info.st_mode&0o7777),info.st_uid,info.st_gid,flush=True)
        for line in (ROOT/'app.log').read_text().splitlines():
            if 'Browser certificate reload rejected' in line:print(line,flush=True)
        raise
    finally:stop_app(process)


def remote(hotspot=False):
    for address in ([HOTSPOT] if hotspot else [V4,V6]):
        session = Session(address)
        assert session.request('/system/health')[0] == 200
        assert session.request('/secure-access')[0] == 200
        for method in ['GET','POST']:
            assert session.request('/secure-access/physical',method=method)[0] == 403
        assert session.request('/system/health',authority=HOST+':5445')[0] == 421
        try:socket.create_connection((address,5445),timeout=.75)
        except OSError:pass
        else:raise AssertionError('Remote direct backend was reachable')
    absent = V4 if hotspot else HOTSPOT
    try:socket.create_connection((absent,443),timeout=.75)
    except OSError:pass
    else:raise AssertionError('Unassigned private origin remained reachable')
    print('PASS: separate client actual IPv4/IPv6 PREROUTING, trusted TLS, remote physical GET/POST denial and direct 5445 firewall rejection')


if __name__ == '__main__':
    if not Path('/.dockerenv').exists() or os.getpid() != 1 or os.environ.get('IT_DISPOSABLE_KERNEL_TEST') != '1':
        raise SystemExit('Refuse outside an explicit disposable Docker PID/network namespace')
    if sys.argv[1:] == ['hub']:hub()
    elif sys.argv[1:] == ['remote']:remote()
    elif sys.argv[1:] == ['remote-hotspot']:remote(hotspot=True)
    else:raise SystemExit('Expected hub or remote fixture role')
