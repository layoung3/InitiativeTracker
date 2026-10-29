import contextlib
import errno
import fcntl
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

    def assert_locks_available(self, names):
        for name in names:
            with (self.root/name).open('a+b') as lock:
                fcntl.flock(lock,fcntl.LOCK_EX | fcntl.LOCK_NB)

    def run_main(self):
        output=io.StringIO();errors=io.StringIO()
        with patch.object(m.sys,'argv',['boot-fallback','tick']), \
             contextlib.redirect_stdout(output),contextlib.redirect_stderr(errors):
            result=m.main()
        return result,output.getvalue(),errors.getvalue()

    def test_each_busy_lock_holds_without_state_access_and_releases_for_retry(self):
        names=('boot-fallback.lock','control.lock','return-transition.lock')
        for index,name in enumerate(names):
            with self.subTest(lock=name):
                state=self.root/'boot-fallback.json'
                state.write_text(json.dumps({'boot':'boot','offline_since':None,'scans':[],'last_action':None}))
                before=state.read_bytes()
                with (self.root/name).open('a+b') as holder:
                    fcntl.flock(holder,fcntl.LOCK_EX | fcntl.LOCK_NB)
                    with patch.object(m,'nm') as commands,patch.object(m,'load') as reads,patch.object(m,'save') as writes:
                        result,output,errors=self.run_main()
                        self.assertEqual(result,0)
                        self.assertEqual(errors,'')
                        self.assertEqual(output.splitlines(),[
                            'RECOMMENDATION=HOLD','REASON=controller_lock_busy',
                            'BUSY_LOCK='+name,'EXECUTED_NETWORK_ACTION=NONE'])
                        commands.assert_not_called();reads.assert_not_called();writes.assert_not_called()
                        self.assertEqual(state.read_bytes(),before)
                    # Earlier acquired locks must already be closed, even while
                    # the independent controller still owns the contended lock.
                    self.assert_locks_available(names[:index])
                self.assert_locks_available(names)
                with patch.object(m,'gate',return_value=False), \
                     patch.object(m,'nm') as commands, \
                     patch.object(m,'load',wraps=m.load) as reads, \
                     patch.object(m,'save',wraps=m.save) as writes:
                    result,output,errors=self.run_main()
                    self.assertEqual(result,0);self.assertEqual(errors,'')
                    self.assertNotIn('controller_lock_busy',output)
                    reads.assert_called_once();writes.assert_called_once();commands.assert_not_called()
                    self.assertEqual(m.load('boot')['offline_since'],60)

    def test_only_contention_errno_values_at_flock_are_holds(self):
        for code in (errno.EAGAIN,errno.EWOULDBLOCK):
            with self.subTest(errno=code), \
                 patch.object(m.fcntl,'flock',side_effect=OSError(code,'fixture contention')), \
                 patch.object(m,'nm') as commands,patch.object(m,'save') as writes:
                result,output,errors=self.run_main()
                self.assertEqual(result,0);self.assertEqual(errors,'')
                self.assertIn('BUSY_LOCK=boot-fallback.lock',output)
                commands.assert_not_called();writes.assert_not_called()

    def test_other_flock_errors_remain_failures_and_release_previous_locks(self):
        original=fcntl.flock
        for code in (errno.EACCES,errno.EBADF,errno.EINTR):
            def acquire(lock,operation):
                if Path(lock.name).name=='return-transition.lock':
                    raise OSError(code,'fixture non-contention lock error')
                return original(lock,operation)
            with self.subTest(errno=code),patch.object(m.fcntl,'flock',side_effect=acquire), \
                 patch.object(m,'nm') as commands,patch.object(m,'save') as writes:
                result,output,errors=self.run_main()
                self.assertEqual(result,1);self.assertIn('N67_ERROR=',errors)
                self.assertNotIn('controller_lock_busy',output)
                commands.assert_not_called();writes.assert_not_called()
            self.assert_locks_available(('boot-fallback.lock','control.lock','return-transition.lock'))

    def test_eagain_outside_lock_acquisition_remains_failure(self):
        original=Path.open
        def open_file(path,*args,**kwargs):
            if path.name=='control.lock':raise OSError(errno.EAGAIN,'fixture open failure')
            return original(path,*args,**kwargs)
        for stage in ('open','load','nm','save'):
            with self.subTest(stage=stage),contextlib.ExitStack() as stack:
                if stage=='open':stack.enter_context(patch.object(Path,'open',open_file))
                else:stack.enter_context(patch.object(m,stage,side_effect=OSError(errno.EAGAIN,'fixture '+stage+' failure')))
                stack.enter_context(patch.object(m,'gate',return_value=stage=='nm'))
                result,output,errors=self.run_main()
                self.assertEqual(result,1);self.assertIn('N67_ERROR=',errors)
                self.assertNotIn('controller_lock_busy',output)
            self.assert_locks_available(('boot-fallback.lock','control.lock','return-transition.lock'))

    def test_both_pending_journals_fail_closed(self):
        for name,terminal in [('transition.json','completed'),('return-transition.json','confirmed')]:
            p=self.root/name;p.write_text('{"state":"pending"}');self.assertTrue(m.pending())
            p.write_text(json.dumps({'state':terminal}));self.assertFalse(m.pending())
            p.write_text('bad');self.assertTrue(m.pending());p.unlink()

    def test_failed_saved_profile_is_attempted_once_then_fallback(self):
        ident='11111111-1111-1111-1111-111111111111'
        state={'boot':'boot','offline_since':10,'scans':[10,25],'last_action':None}
        m.save(state)
        profile=['']
        def nm(*args,**kw):
            if 'uuid' in args:raise RuntimeError('association failed')
            if 'up' in args:profile[0]=m.RECOVERY
            if args==('-g','NAME','connection','show'):return m.RECOVERY
            return ''
        with patch.object(m,'saved_visible',return_value=[ident]), \
             patch.object(m,'active',side_effect=lambda:profile[0]), \
             patch.object(m,'device_state',side_effect=lambda:'100 (connected)' if profile[0] else '30 (disconnected)'), \
             patch.object(m,'nm',side_effect=nm) as calls:
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
        with patch.object(m,'saved_visible',return_value=['id']), \
             patch.object(m,'active',side_effect=lambda:active[0]), \
             patch.object(m,'device_state',side_effect=lambda:'100 (connected)' if active[0] else '30 (disconnected)'), \
             patch.object(m,'nm',side_effect=nm) as calls:
            m.go('tick');m.go('tick');self.assertEqual(sum('up' in c.args for c in calls.call_args_list),1);self.assertTrue(m.load('boot')['settled'])

    def test_manual_ap_or_established_connection_is_never_abandoned(self):
        for name in (m.RECOVERY,'another AP','external'):
            with patch.object(m,'active',return_value=name),patch.object(m,'device_state',return_value='100 (connected)'),patch.object(m,'nm') as calls:
                m.go('tick');calls.assert_not_called()
        with patch.object(m,'nm') as calls:
            m.go('tick');calls.assert_not_called() # later disconnect after settled is recovery controller territory

    def test_delayed_scan_and_transition_prevent_fallback(self):
        with patch.object(m,'nm',side_effect=RuntimeError('scan not ready')):
            with self.assertRaises(RuntimeError):m.go('tick')
        self.assertFalse((self.root/'boot-fallback.json').exists())
        (self.root/'return-transition.json').write_text('{"state":"pending"}')
        with patch.object(m,'nm') as calls:m.go('tick');calls.assert_not_called()

    def test_failed_activation_with_profile_name_still_allows_later_fallback(self):
        profile=['external'];device=['50 (connecting)'];clock=[60]
        def nm(*args,**kwargs):
            if args==('-g','NAME','connection','show'):return m.RECOVERY
            if 'up' in args:
                profile[0]=m.RECOVERY;device[0]='100 (connected)'
            return ''
        with patch.object(m,'active',side_effect=lambda:profile[0]), \
             patch.object(m,'device_state',side_effect=lambda:device[0]), \
             patch.object(m,'uptime',side_effect=lambda:clock[0]), \
             patch.object(m,'saved_visible',return_value=[]), \
             patch.object(m,'nm',side_effect=nm) as calls:
            # A name is visible during both association and authentication.
            for connecting in ('50 (connecting)', '60 (need authentication)'):
                device[0]=connecting;m.go('tick')
                self.assertFalse(m.load('boot').get('settled',False))
                calls.assert_not_called()
            profile[0]='';device[0]='30 (disconnected)'
            for now in (70,80,90):
                clock[0]=now;m.go('tick')
            self.assertTrue(any('up' in c.args and m.RECOVERY in c.args for c in calls.call_args_list))
            self.assertTrue(m.load('boot')['settled'])

    def test_pending_transitions_do_not_settle_an_activating_profile(self):
        for journal in ('transition.json','return-transition.json'):
            with self.subTest(journal=journal):
                path=self.root/journal;path.write_text('{"state":"pending"}')
                with patch.object(m,'active',return_value='external'), \
                     patch.object(m,'device_state',return_value='50 (connecting)'), \
                     patch.object(m,'nm') as calls:
                    m.go('tick');calls.assert_not_called()
                    self.assertFalse(m.load('boot').get('settled',False))
                path.unlink()

    def test_hotspot_command_alone_does_not_settle_unconfirmed_activation(self):
        m.save({'boot':'boot','offline_since':10,'scans':[10,25],'last_action':None})
        profile=[''];device=['30 (disconnected)']
        def nm(*args,**kwargs):
            if args==('-g','NAME','connection','show'):return m.RECOVERY
            if 'up' in args:profile[0]=m.RECOVERY;device[0]='50 (connecting)'
            return ''
        with patch.object(m,'active',side_effect=lambda:profile[0]), \
             patch.object(m,'device_state',side_effect=lambda:device[0]), \
             patch.object(m,'saved_visible',return_value=[]),patch.object(m,'nm',side_effect=nm):
            m.go('tick');self.assertFalse(m.load('boot')['settled'])
            device[0]='100 (connected)';m.go('tick');self.assertTrue(m.load('boot')['settled'])

    def test_profiles_use_uuid_mode_interface_autoconnect_and_priority(self):
        ids=[f'{i:08d}-1111-1111-1111-111111111111' for i in range(1,7)]
        def nm(*args,**kw):
            if 'SSID,SIGNAL' in args:return 'shared:90'
            if 'UUID,TYPE' in args:return '\n'.join(i+':wifi' for i in ids)
            key=args[args.index('-g')+1];i=ids.index(args[-1])
            return {'802-11-wireless.mode':'ap' if i==1 else 'infrastructure',
                    'connection.interface-name':'wlan1' if i==2 else 'wlan0',
                    'connection.autoconnect':'no' if i==3 else 'yes',
                    '802-11-wireless.ssid':'absent' if i==4 else 'shared',
                    'connection.autoconnect-priority':str(i)}[key]
        with patch.object(m,'nm',side_effect=nm):self.assertEqual(m.saved_visible(),[ids[5],ids[0]])

    def test_saved_ssids_match_colons_backslashes_and_edge_spaces_exactly(self):
        ident='11111111-1111-1111-1111-111111111111'
        for ssid in ('Cafe:Guest', r'Room\WiFi', ' Leading', 'Trailing ', ' Both '):
            with self.subTest(ssid=ssid):
                escaped=ssid.replace('\\','\\\\').replace(':','\\:')
                def nm(*args,**kwargs):
                    if 'SSID,SIGNAL' in args:return escaped+':90\n'
                    if 'UUID,TYPE' in args:return ident+':wifi\n'
                    key=args[args.index('-g')+1]
                    if key=='802-11-wireless.ssid':
                        return (ssid if '--escape' in args and args[args.index('--escape')+1]=='no' else escaped)+'\n'
                    return {'802-11-wireless.mode':'infrastructure',
                            'connection.interface-name':'wlan0',
                            'connection.autoconnect':'yes',
                            'connection.autoconnect-priority':'0'}[key]
                with patch.object(m,'nm',side_effect=nm):self.assertEqual(m.saved_visible(),[ident])

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
