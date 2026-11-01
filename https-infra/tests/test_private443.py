from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import MagicMock

sys.path.insert(0,str(Path(__file__).parents[1]))
import private443 as ingress
import package_masks
import foundation as f


class Private443Tests(unittest.TestCase):
    def test_rule_scope_has_no_snat_and_preserves_loopback_and_backend_boundaries(self):
        text = ingress.render([{'interface':'eth0','address':'192.168.1.5'},{'interface':'wlan0','address':'fd12::1'}],{'eth0':['192.168.1.5'],'wlan0':['fd12::1']})
        for expected in ['table inet initiativetracker_browser443','iifname "eth0" ip daddr 192.168.1.5 fib daddr type local tcp dport 443 dnat ip to 192.168.1.5:5445',
            'dnat ip6 to [fd12::1]:5445','ip daddr 127.0.0.1 tcp dport 443','ip6 daddr ::1 tcp dport 443',
            'ct original proto-dst 443','tcp dport 5445 drop','tcp dport 443 drop']:
            self.assertIn(expected,text)
        for forbidden in ['flush ruleset','masquerade','snat','forwarded','0.0.0.0/0']:self.assertNotIn(forbidden,text)
    def test_bindings_require_current_private_address_and_unambiguous_interface(self):
        for bindings in [[],[{'interface':'eth0','address':'8.8.8.8'}],[{'interface':'lo','address':'127.0.0.1'}],
            [{'interface':'eth0";drop','address':'192.168.1.5'}],[{'interface':'eth0','address':'192.168.1.6'}],
            [{'interface':'eth0','address':'192.168.1.5'}]*2]:
            with self.subTest(bindings=bindings),self.assertRaises((f.Rejected,KeyError)):ingress.render(bindings,{'eth0':['192.168.1.5']})
    def test_vendor_mask_failure_is_fatal_before_any_followup(self):
        runner = MagicMock(side_effect=f.Rejected('fixture mask failure'))
        with self.assertRaises(f.Rejected):package_masks.mask(_runner=runner)
        self.assertEqual(1,runner.call_count)
    def test_both_units_must_be_masked_and_inactive(self):
        def value(state='masked-runtime',active='inactive',load='masked'):
            return f'LoadState={load}\nUnitFileState={state}\nActiveState={active}\n'.encode()
        runner = MagicMock(return_value=value())
        self.assertTrue(package_masks.verify(_runner=runner)['vendor_units_masked_and_inactive'])
        self.assertEqual(2,runner.call_count)
        with self.assertRaises(f.Rejected):package_masks.verify(persistent=True,_runner=runner)
        self.assertTrue(package_masks.verify(persistent=True,_runner=MagicMock(return_value=value(state='masked')))['vendor_units_masked_and_inactive'])
        for bad in [value(state='enabled'),value(active='active'),value(load='not-found'),b'',b'ActiveState=inactive\n']:
            with self.subTest(bad=bad),self.assertRaises(f.Rejected):package_masks.verify(_runner=MagicMock(side_effect=[value(),bad]))
    def test_documented_mask_failure_stops_pasted_package_block(self):
        guide=(Path(__file__).parents[2]/'docs/https/private443-deployment.md').read_text()
        start=guide.index('sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/package_masks.py mask')
        end=guide.index('\n```',start)
        block=guide[start:end]
        wrapper='sudo() { case "$*" in *package_masks.py*) return 1;; *) echo UNEXPECTED_INSTALL; return 0;; esac; }\n'+block
        result=subprocess.run(['bash'],input=wrapper,text=True,capture_output=True)
        self.assertNotEqual(0,result.returncode);self.assertNotIn('UNEXPECTED_INSTALL',result.stdout)
    def test_probe_separates_backend_socket_from_canonical_host_and_pins_leaf(self):
        from unittest.mock import patch
        connection=MagicMock();connection.getpeercert.return_value=b'leaf'
        connection.recv.side_effect=[b'HTTP/1.0 200 OK\r\n\r\n{"ready":true,"generation":"selected"}',b'']
        context=MagicMock();context.wrap_socket.return_value.__enter__.return_value=connection
        with patch.object(f.socket,'create_connection') as connect:
            f.probe('hub-fixture.colophon.stream',5445,'selected',f.hashlib.sha256(b'leaf').hexdigest(),authority_port=443,_test_context=context)
        connect.assert_called_once_with(('127.0.0.1',5445),timeout=3)
        self.assertIn(b'Host: hub-fixture.colophon.stream\r\n',connection.sendall.call_args.args[0])
