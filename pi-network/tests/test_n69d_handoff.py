import importlib.machinery
import importlib.util
import pathlib
import unittest
from unittest.mock import patch

PATH = pathlib.Path(__file__).resolve().parents[1] / 'initiativetracker-return-handoff'
loader = importlib.machinery.SourceFileLoader('handoff', str(PATH))
spec = importlib.util.spec_from_loader(loader.name, loader)
mod = importlib.util.module_from_spec(spec)
loader.exec_module(mod)
UUID = 'c7ce4ffc-e066-3d85-8917-90322686badf'

class HandoffTests(unittest.TestCase):
    def call(self, state='pending', gate='true', active='InitiativeTracker-Recovery', timer='active'):
        calls=[]
        def command(args, timeout=15):
            calls.append(args)
            if args[0] == mod.RECOVERY and args[1] == 'status':
                return f'ACTIVE_PROFILE={active}\nJOURNAL_STATE={state}\nN64C_LIVE_ENABLED={gate}'
            if args[0] == mod.RETURN and args[1] == 'status':
                return f'ACTIVE_PROFILE={active}\nRETURN_SWITCH_ENABLED=true\nRETURN_JOURNAL_STATE=none'
            if args[0] == '/usr/bin/systemctl': return timer
            if args[0] == mod.RETURN and args[1] == 'list-saved':
                return 'SAVED_WIFI=[{"uuid":"'+UUID+'","name":"netplan-wlan0-Logan"}]'
            return ''
        with patch.object(mod.os, 'geteuid', return_value=0), patch.object(mod, 'run_command', side_effect=command):
            mod.execute(UUID, 180)
        return calls

    def test_pending_handoff_confirms_before_return(self):
        calls=self.call()
        self.assertLess(calls.index([mod.RECOVERY,'confirm']),next(i for i,x in enumerate(calls) if x[:2]==[mod.RETURN,'begin']))
    def test_recovered_journal_skips_confirm(self):
        calls=self.call(state='recovered')
        self.assertNotIn([mod.RECOVERY,'confirm'], calls)
    def test_refuses_disabled_recovery_gate_for_pending(self):
        with self.assertRaises(RuntimeError):self.call(gate='false')
    def test_refuses_inactive_timer(self):
        with self.assertRaises(RuntimeError):self.call(timer='inactive')
    def test_refuses_not_on_hotspot(self):
        with self.assertRaises(RuntimeError):self.call(active='netplan-wlan0-Logan')
    def test_refuses_bad_uuid(self):
        with patch.object(mod.os,'geteuid',return_value=0):
            with self.assertRaises(ValueError):mod.execute('bad',180)

if __name__=='__main__':unittest.main()
