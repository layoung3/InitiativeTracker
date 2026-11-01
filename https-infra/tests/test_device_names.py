import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('device_names', Path(__file__).parents[1] / 'device_names.py')
n = importlib.util.module_from_spec(spec); spec.loader.exec_module(n)

class DeviceNameTests(unittest.TestCase):
    def value(self):
        return {'format': 1, 'domain': 'colophon.stream', 'devices': [
            {'role': 'hub', 'id': '84eaee7c-1111-4111-8111-111111111111', 'short_id': '84eaee7c'}]}
    def test_existing_alias_is_preserved_from_full_guid(self):
        value = self.value(); item = n.lookup(value, 'hub', value['devices'][0]['id'])
        self.assertEqual('hub-84eaee7c.colophon.stream', item['hostname'])
        self.assertEqual('84eaee7c', item['short_id'])
    def test_collision_requires_explicit_resolution_without_reseeding(self):
        value = self.value(); other = {'role': 'hub', 'id': '84eaee7c-2222-4222-8222-222222222222', 'short_id': '84eaee7c'}
        value['devices'].append(other)
        with self.assertRaises(n.Rejected): n.inventory(value)
        other['short_id'] = '84eaee7c2222'
        self.assertEqual('hub-84eaee7c2222.colophon.stream', n.lookup(value, 'hub', other['id'])['hostname'])
        self.assertEqual('hub-84eaee7c.colophon.stream', n.inventory(value)[0]['hostname'])
    def test_duplicate_identity_mac_and_unrelated_alias_are_rejected(self):
        value = self.value(); value['devices'].append(dict(value['devices'][0]))
        with self.assertRaises(n.Rejected): n.inventory(value)
        for identity, alias in [('aa:bb:cc:dd:ee:ff','aabbccdd'), ('00000000-0000-0000-0000-000000000000','00000000'), ('84eaee7c-1111-4111-8111-111111111111','12345678')]:
            value = self.value(); value['devices'][0].update(id=identity, short_id=alias)
            with self.subTest(identity=identity), self.assertRaises(n.Rejected): n.inventory(value)
    def test_unknown_device_is_never_automatically_assigned(self):
        with self.assertRaises(n.Rejected): n.lookup(self.value(), 'hub', '84eaee7c-2222-4222-8222-222222222222')
    def test_satellite_convention_does_not_confer_agent_or_certificate_authority(self):
        value = self.value(); value['devices'].append({'role':'satellite','id':'84eaee7c-2222-4222-8222-222222222222','short_id':'84eaee7c2222'})
        self.assertEqual('satellite-84eaee7c2222.colophon.stream', n.inventory(value)[1]['hostname'])
        self.assertEqual({'role','id','short_id','hostname'}, set(n.inventory(value)[1]))

    def test_duplicate_guid_or_short_id_across_roles_is_also_detected(self):
        for identity in ['84eaee7c-1111-4111-8111-111111111111', '84eaee7c-2222-4222-8222-222222222222']:
            value = self.value(); value['devices'].append({'role':'satellite','id':identity,'short_id':'84eaee7c'})
            with self.assertRaises(n.Rejected): n.inventory(value)
    def test_nonpublic_domain_and_oversized_inventory_are_rejected(self):
        for domain in ['hub.local', 'customer.123', '*.colophon.stream', 'COLOPHON.stream', 'colophon.stream.']:
            value=self.value(); value['domain']=domain
            with self.subTest(domain=domain), self.assertRaises(n.Rejected): n.inventory(value)
