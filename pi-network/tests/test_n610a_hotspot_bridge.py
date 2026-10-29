import importlib.machinery
import importlib.util
from pathlib import Path
from unittest import TestCase, main
from unittest.mock import patch
ROOT = Path(__file__).resolve().parents[1]
def load(name, filename):
    loader = importlib.machinery.SourceFileLoader(name, str(ROOT / filename))
    spec = importlib.util.spec_from_loader(name, loader)
    mod = importlib.util.module_from_spec(spec)
    loader.exec_module(mod)
    return mod
socketd = load('socketd', 'initiativetracker-network-socketd')
helper = load('helper', 'initiativetracker-web-network-bridge')
class Tests(TestCase):
    def test_socket_allows_bounded_hotspot_actions(self):
        for a in ('begin-hotspot','confirm-hotspot'):
            self.assertEqual(socketd.parse_request(('{"action":"%s","uuid":null}'%a)),[socketd.HELPER,a])
    def test_socket_rejects_extra_fields_and_uuid(self):
        for payload in ('{"action":"begin-hotspot","uuid":"x"}', '{"action":"begin-hotspot","uuid":null,"shell":"id"}'):
            with self.assertRaises(ValueError): socketd.parse_request(payload)
    def test_begin_uses_detached_supervised_controller(self):
        with patch.object(helper.os,'geteuid',return_value=0),patch.object(helper.subprocess,'run') as run:
            run.return_value.returncode=0
            run.return_value.stdout='started'
            helper.execute('begin-hotspot')
            args=run.call_args.args[0]
            self.assertIn('systemd-run',args[0]);self.assertIn(helper.RECOVERY,args)
            self.assertEqual(args[args.index('--seconds')+1],'180')
    def test_confirm_uses_existing_controller(self):
        with patch.object(helper.os,'geteuid',return_value=0),patch.object(helper.subprocess,'run') as run:
            run.return_value.returncode=0;run.return_value.stdout='ok'
            helper.execute('confirm-hotspot')
            self.assertEqual(run.call_args.args[0],[helper.RECOVERY,'confirm'])
if __name__=='__main__': main()
