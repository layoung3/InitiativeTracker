import json
from pathlib import Path
import ssl
import sys
import unittest
from unittest.mock import MagicMock

sys.path.insert(0, str(Path(__file__).parents[1]))
import local_resolution as r


class LocalResolutionTests(unittest.TestCase):
    host = 'hub-84eaee7c.colophon.stream'
    identity = '84eaee7c-0000-4000-8000-000000000001'  # Synthetic, never the owner's full identity.

    def test_hotspot_ignores_hub_hosts_and_serves_exact_private_records(self):
        text = r.hotspot_dns(self.host, ['192.168.4.1','fd12::1'], ['192.168.4.1','fd12::1'])
        self.assertEqual('no-hosts\nlocal=/' + self.host + '/\nhost-record=' + self.host + ',192.168.4.1,fd12::1\n', text)
        self.assertNotIn('127.0.0.1', text)
        self.assertNotIn('::1\n', r.hotspot_dns(self.host, ['192.168.4.1'], ['192.168.4.1']))

    def test_hotspot_and_clients_reject_loopback_public_or_stale_addresses(self):
        for address in ['127.0.0.1','::1','8.8.8.8','fe80::1','::ffff:192.168.4.1']:
            with self.subTest(address=address), self.assertRaises(r.f.Rejected):
                r.hotspot_dns(self.host, [address], [address])
            with self.assertRaises(r.f.Rejected):r.client_hosts(self.host, [address])
        with self.assertRaises(r.f.Rejected):r.hotspot_dns(self.host, ['192.168.4.1'], ['192.168.4.2'])

    def test_distinct_client_and_physical_mappings(self):
        self.assertEqual('192.168.1.7\t' + self.host + '\n', r.client_hosts(self.host, ['192.168.1.7']))
        self.assertEqual('127.0.0.1\t' + self.host + '\n::1\t' + self.host + '\n', r.physical_hosts(self.host))
        with self.assertRaises(r.f.Rejected):r.client_hosts(self.host, [])

    def test_hotspot_dns_can_be_prepared_inactive_only_from_existing_ap_profile(self):
        identity='ddae7300-1111-4111-8111-111111111111'
        profile={'id':identity,'interface':'wlan0','mode':'ap','method':'shared','addresses':'10.84.0.1/24','ipv6':'disabled'}
        reader=MagicMock(return_value=profile)
        text=r.hotspot_dns(self.host,['10.84.0.1'],['192.168.1.5'],profile=identity,interface='wlan0',_hotspot_reader=reader)
        self.assertIn('no-hosts\n',text);self.assertIn('host-record='+self.host+',10.84.0.1',text)
        self.assertNotIn('127.0.0.1',text)
        with self.assertRaises(ValueError):r.hotspot_dns(self.host,['10.84.0.2'],[],profile=identity,interface='wlan0',_hotspot_reader=reader)

    def test_stale_a_aaaa_and_unconfigured_dns_fail_before_tls(self):
        identity = MagicMock()
        for addresses in [[], ['127.0.0.1'], ['192.168.1.8'], ['192.168.1.7','fd12::4']]:
            resolver = lambda *a, **kw: [(None,None,None,None,(address,0)) for address in addresses]
            self.assertFalse(r.client_preflight(self.host,self.identity,['192.168.1.7'],_resolver=resolver,_identity=identity)['ready'])
        identity.assert_not_called()

    def test_client_success_still_requires_browser_and_internet_acceptance(self):
        resolver = lambda *a, **kw: [(None,None,None,None,('192.168.1.7',0))]
        identity = MagicMock()
        result = r.client_preflight(self.host,self.identity,['192.168.1.7'],_resolver=resolver,_identity=identity)
        self.assertTrue(result['ready']);self.assertTrue(result['browser_verification_required'])
        identity.assert_called_once_with(self.host,self.identity,{'192.168.1.7'})
        identity.side_effect = r.f.Rejected('wrong full identity')
        self.assertFalse(r.client_preflight(self.host,self.identity,['192.168.1.7'],_resolver=resolver,_identity=identity)['ready'])

    def test_canonical_public_tls_full_identity_no_redirect_or_certificate_bypass(self):
        connection = MagicMock();factory = MagicMock(return_value=connection)
        connection.sock.getpeername.return_value = ('192.168.1.7',443)
        response = connection.getresponse.return_value
        response.status = 200;response.getheader.return_value = 'application/json; charset=utf-8'
        response.read.return_value = json.dumps({'hubId':self.identity,'protocolVersion':1}).encode()
        r.https_identity(self.host,self.identity,['192.168.1.7'],_connection=factory)
        args,kwargs = factory.call_args
        self.assertEqual((self.host,443),args)
        self.assertEqual(ssl.CERT_REQUIRED,kwargs['context'].verify_mode)
        self.assertTrue(kwargs['context'].check_hostname)
        for payload in [b'{}',b'[]',b'null',b'1',b'{"hubId":123,"protocolVersion":1}',b'{"hubId":[],"protocolVersion":1}', b'x' * 8193, json.dumps({'hubId':'84eaee7c-0000-4000-8000-000000000002','protocolVersion':1}).encode()]:
            response.read.return_value = payload
            with self.assertRaises(r.f.Rejected):r.https_identity(self.host,self.identity,['192.168.1.7'],_connection=factory)
        response.status = 302
        with self.assertRaises(r.f.Rejected):r.https_identity(self.host,self.identity,['192.168.1.7'],_connection=factory)
        self.assertTrue(connection.close.called)

    def test_dns_change_before_connect_rejects_peer_before_http(self):
        resolver = lambda *a,**kw: [(None,None,None,None,('192.168.1.7',0))]
        connection = MagicMock();connection.sock.getpeername.return_value = ('192.168.1.8',443)
        def identity(host,expected,addresses):
            r.https_identity(host,expected,addresses,_connection=lambda *a,**kw:connection)
        self.assertFalse(r.client_preflight(self.host,self.identity,['192.168.1.7'],_resolver=resolver,_identity=identity)['ready'])
        connection.request.assert_not_called()
        connection.close.assert_called_once()
