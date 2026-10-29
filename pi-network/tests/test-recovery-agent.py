import importlib.machinery
import importlib.util
from datetime import datetime, timedelta, timezone
from pathlib import Path
import unittest

script=Path(__file__).resolve().parents[1]/'initiativetracker-recovery-agent'
module=importlib.util.module_from_spec(importlib.machinery.ModuleSpec('recovery',importlib.machinery.SourceFileLoader('recovery',str(script))))
module.__spec__.loader.exec_module(module)

class Plans(unittest.TestCase):
    def setUp(self):
        self.now=datetime(2026,10,9,tzinfo=timezone.utc)
        self.j={'state':'pending','previous':'home','target':'other','deadline_utc':(self.now+timedelta(seconds=10)).isoformat()}
    def test_no_journal(self): self.assertEqual(module.recovery_plan(None,'home',True,self.now),'NO_PENDING_TRANSITION')
    def test_no_recovery_profile(self): self.assertEqual(module.recovery_plan(None,'home',False,self.now),'BLOCKED_MISSING_RECOVERY_PROFILE')
    def test_wait(self): self.assertEqual(module.recovery_plan(self.j,'home',True,self.now),'WAIT')
    def test_timeout(self): self.assertEqual(module.recovery_plan(self.j,'other',True,self.now+timedelta(seconds=11)),'WOULD_TRY_PREVIOUS_THEN_RECOVERY')
    def test_already_home(self): self.assertEqual(module.recovery_plan(self.j,'home',True,self.now+timedelta(seconds=11)),'RECOVERY_NETWORK_ALREADY_ACTIVE')
    def test_already_hotspot(self): self.assertEqual(module.recovery_plan(self.j,module.PROFILE,True,self.now+timedelta(seconds=11)),'RECOVERY_NETWORK_ALREADY_ACTIVE')
    def test_complete(self):
        self.j['state']='completed'
        self.assertEqual(module.recovery_plan(self.j,'other',True,self.now),'ALREADY_FINALIZED')
    def test_validated(self):
        self.j['state']='target_validated'
        self.assertEqual(module.recovery_plan(self.j,'other',True,self.now),'AWAIT_FINALIZATION')

if __name__=='__main__': unittest.main()
