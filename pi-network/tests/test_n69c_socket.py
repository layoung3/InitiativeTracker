import importlib.machinery
import importlib.util
import unittest

p='pi-network/initiativetracker-network-socketd'
loader=importlib.machinery.SourceFileLoader('bridge_socketd',p)
spec=importlib.util.spec_from_loader(loader.name,loader)
module=importlib.util.module_from_spec(spec)
loader.exec_module(module)

class BridgeTest(unittest.TestCase):
    def test_valid(self):
        self.assertEqual(module.parse_request('{"action":"status","uuid":null}')[-1], 'status')
        self.assertEqual(module.parse_request('{"action":"confirm-return","uuid":null}')[-1], 'confirm-return')
        self.assertEqual(module.parse_request('{"action":"begin-return","uuid":"c7ce4ffc-e066-3d85-8917-90322686badf"}')[-2], 'begin-return')
    def test_bad(self):
        for data in ('{"action":"exec","uuid":null}', '{"action":"status","uuid":"x"}', '{"action":"status"}', '[]', 'x'*257, '{"action":"status","uuid":null,"extra":1}'):
            with self.subTest(data=data):
                with self.assertRaises((ValueError, TypeError)):
                    module.parse_request(data)

if __name__=='__main__': unittest.main()
