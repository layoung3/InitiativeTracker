#!/usr/bin/env python3
"""Disposable container only: real client-side DNS with a hub-local hosts override."""
import ipaddress
from pathlib import Path
import random
import socket
import struct
import subprocess
import sys
import tempfile
import time

sys.path.insert(0,str(Path(__file__).parents[1]))
import local_resolution as r


def query(host, kind):
    transaction = random.randrange(65536)
    labels = b''.join(bytes([len(label)]) + label.encode('ascii') for label in host.split('.')) + b'\0'
    question = labels + struct.pack('!HH',kind,1)
    request = struct.pack('!6H',transaction,0x0100,1,0,0,0) + question
    with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as client:
        client.settimeout(.5);client.sendto(request,('127.0.0.1',1053));answer = client.recv(4096)
    received,flags,questions,count,_,_ = struct.unpack_from('!6H',answer)
    assert received == transaction and flags & 0x8000 and not flags & 0x0200 and questions == 1
    assert answer[12:12+len(question)] == question
    offset = 12 + len(question);addresses = []
    for _ in range(count):
        # dnsmasq returns compressed owner names for this simple exact-host fixture.
        assert answer[offset] & 0xc0 == 0xc0
        offset += 2
        record_type,record_class,_,size = struct.unpack_from('!HHIH',answer,offset)
        offset += 10
        assert record_class == 1 and record_type in {1,28}
        addresses.append(str(ipaddress.ip_address(answer[offset:offset+size])));offset += size
    return flags & 15, set(addresses)


host = 'hub-d15c05ab.colophon.stream'  # Synthetic fixture identity, never a real DNS write.
hosts = Path('/etc/hosts');original = hosts.read_bytes()
with tempfile.TemporaryDirectory(prefix='it-hotspot-dns-') as directory:
    folder = Path(directory)
    try:
        hosts.write_bytes(original + ('\n127.0.0.1 ' + host + '\n::1 ' + host + '\n').encode())
        for addresses in [['192.168.4.1'], ['192.168.4.1','fd12::1']]:
            config = folder/'dnsmasq.conf'
            config.write_text(r.hotspot_dns(host,addresses,addresses) +
                # Offline fixture controls only; not part of the NetworkManager snippet.
                'no-resolv\nport=1053\nlisten-address=127.0.0.1\nbind-interfaces\nuser=root\n' +
                'pid-file=' + str(folder/'dnsmasq.pid') + '\n')
            subprocess.run(['/usr/sbin/dnsmasq','--test','--conf-file='+str(config)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            process = subprocess.Popen(['/usr/sbin/dnsmasq','--no-daemon','--conf-file='+str(config)],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            try:
                for _ in range(30):
                    try:
                        a_code,a = query(host,1);break
                    except TimeoutError:time.sleep(.02)
                else:raise AssertionError('Disposable DNS resolver did not start')
                aaaa_code,aaaa = query(host,28)
                assert a_code == aaaa_code == 0
                assert a == {'192.168.4.1'} and aaaa == (set(addresses)-{'192.168.4.1'})
                assert not (a|aaaa)&{'127.0.0.1','::1'}
                assert query('unapproved.'+host,1)[0] == 3
                assert query('unapproved.'+host,28)[0] == 3
            finally:
                process.terminate();process.wait(timeout=3)
    finally:hosts.write_bytes(original)
print('PASS: actual dnsmasq A/AAAA client answers exclude hub-only loopback hosts, exact host only, no internet/DNS mutation')
