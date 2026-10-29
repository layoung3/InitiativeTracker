import contextlib
import importlib.machinery
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

def load(file):
    loader=importlib.machinery.SourceFileLoader(file,str(Path(__file__).parents[1]/file))
    spec=importlib.util.spec_from_loader(file,loader)
    module=importlib.util.module_from_spec(spec);loader.exec_module(module);return module

m=load('initiativetracker-boot-fallback')
prepare=load('initiativetracker-prepare-startup')

class StartupTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        self.stack=contextlib.ExitStack();self.addCleanup(self.stack.close);self.addCleanup(self.temp.cleanup)
        for target,value in [('ROOT',self.root),('gate',lambda:True),('boot_id',lambda:'boot'),('uptime',lambda:60),('active',lambda:''),('device_state',lambda:'30 (disconnected)')]:
            self.stack.enter_context(patch.object(m,target,value))
        self.stack.enter_context(patch.object(m.os,'geteuid',return_value=0))
        self.stack.enter_context(contextlib.redirect_stdout(io.StringIO()))

    def test_both_pending_journals_fail_closed(self):
        for name,terminal in [('transition.json','completed'),('return-transition.json','confirmed')]:
            p=self.root/name;p.write_text('{"state":"pending"}');self.assertTrue(m.pending())
            p.write_text(json.dumps({'state':terminal}));self.assertFalse(m.pending())
            p.write_text('bad');self.assertTrue(m.pending());p.unlink()

    def test_failed_saved_profile_is_attempted_once_then_fallback(self):
        ident='11111111-1111-1111-1111-111111111111'
        state={'boot':'boot','offline_since':10,'scans':[10,25],'last_action':None}
        m.save(state)
        def nm(*args,**kw):
            if 'uuid' in args:raise RuntimeError('association failed')
            if args==('-g','NAME','connection','show'):return m.RECOVERY
            return ''
        with patch.object(m,'saved_visible',return_value=[ident]),patch.object(m,'nm',side_effect=nm) as calls:
            m.go('tick');self.assertEqual(m.load('boot')['attempted'],[ident])
            m.go('tick');self.assertTrue(m.load('boot')['settled'])
            self.assertEqual(sum('uuid' in c.args for c in calls.call_args_list),1)
            self.assertTrue(any('id' in c.args and m.RECOVERY in c.args for c in calls.call_args_list))

    def test_successful_saved_network_never_activates_hotspot(self):
        state={'boot':'boot','offline_since':10,'scans':[10,25],'last_action':None};m.save(state)
        active=['']
        def nm(*args,**kw):
            if 'up' in args:active[0]='external'
            return ''
        with patch.object(m,'saved_visible',return_value=['id']),patch.object(m,'active',side_effect=lambda:active[0]),patch.object(m,'nm',side_effect=nm) as calls:
            m.go('tick');m.go('tick');self.assertEqual(sum('up' in c.args for c in calls.call_args_list),1);self.assertTrue(m.load('boot')['settled'])

    def test_manual_ap_or_established_connection_is_never_abandoned(self):
        for name in (m.RECOVERY,'another AP','external'):
            with patch.object(m,'active',return_value=name),patch.object(m,'nm') as calls:
                m.go('tick');calls.assert_not_called()
        with patch.object(m,'nm') as calls:
            m.go('tick');calls.assert_not_called() # later disconnect after settled is recovery controller territory

    def test_delayed_scan_and_transition_prevent_fallback(self):
        with patch.object(m,'nm',side_effect=RuntimeError('scan not ready')):
            with self.assertRaises(RuntimeError):m.go('tick')
        self.assertFalse((self.root/'boot-fallback.json').exists())
        (self.root/'return-transition.json').write_text('{"state":"pending"}')
        with patch.object(m,'nm') as calls:m.go('tick');calls.assert_not_called()

    def test_profiles_use_uuid_mode_interface_autoconnect_and_priority(self):
        ids=[f'{i:08d}-1111-1111-1111-111111111111' for i in range(1,7)]
        def nm(*args,**kw):
            if 'SSID,SIGNAL' in args:return 'shared:90'
            if 'UUID,TYPE' in args:return '\n'.join(i+':wifi' for i in ids)
            key=args[1];i=ids.index(args[-1])
            return {'802-11-wireless.mode':'ap' if i==1 else 'infrastructure',
                    'connection.interface-name':'wlan1' if i==2 else 'wlan0',
                    'connection.autoconnect':'no' if i==3 else 'yes',
                    '802-11-wireless.ssid':'absent' if i==4 else 'shared',
                    'connection.autoconnect-priority':str(i)}[key]
        with patch.object(m,'nm',side_effect=nm):self.assertEqual(m.saved_visible(),[ids[5],ids[0]])

    def test_prepare_changes_only_autoconnect_and_preserves_secret(self):
        p=self.root/'recovery';p.write_text('[connection]\nid=InitiativeTracker-Recovery\nuuid=abc\nautoconnect=true\n[wifi]\nmode=ap\n[wifi-security]\npsk=test-only-secret\n')
        p.chmod(0o600)
        original=Path.stat
        def stat(path,*args,**kwargs):
            result=original(path,*args,**kwargs)
            if path==p:
                from types import SimpleNamespace
                return SimpleNamespace(st_uid=0,st_mode=result.st_mode)
            return result
        with patch.object(Path,'stat',stat):
            self.assertTrue(prepare.prepare(p));self.assertFalse(prepare.prepare(p))
        self.assertIn('psk=test-only-secret',p.read_text());self.assertIn('autoconnect=false',p.read_text())

if __name__=='__main__':unittest.main()
