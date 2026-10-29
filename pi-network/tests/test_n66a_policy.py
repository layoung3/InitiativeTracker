import importlib.machinery
import importlib.util
from pathlib import Path
import unittest

FILE=Path(__file__).resolve().parents[1]/'initiativetracker-network-policy'
loader=importlib.machinery.SourceFileLoader('policy', str(FILE))
spec=importlib.util.spec_from_loader('policy',loader)
p=importlib.util.module_from_spec(spec)
import sys
sys.modules['policy']=p
loader.exec_module(p)

class PolicyTests(unittest.TestCase):
    def s(self, **changes):
        d=dict(active='',saved=('Office','Home',p.RECOVERY), available=('Home',), recovery_present=True)
        d.update(changes)
        return p.Snapshot(**d)
    def test_current_saved_stays(self): self.assertEqual(p.decide(self.s(active='Home'))[0],'HOLD')
    def test_hotspot_stays_even_if_home_available(self): self.assertEqual(p.decide(self.s(active=p.RECOVERY))[0],'HOLD')
    def test_pending_defers(self): self.assertEqual(p.decide(self.s(transition_pending=True))[0],'DEFER_TO_RECOVERY_CONTROLLER')
    def test_operator_override(self): self.assertEqual(p.decide(self.s(manual_override=True))[0],'HOLD')
    def test_session_lock(self): self.assertEqual(p.decide(self.s(session_locked=True))[0],'HOLD')
    def test_grace(self): self.assertEqual(p.decide(self.s(startup_grace_elapsed=False))[0],'WAIT')
    def test_saved_wifi(self): self.assertEqual(p.decide(self.s())[0],'PREFER_SAVED_WIFI')
    def test_hotspot_fallback(self): self.assertEqual(p.decide(self.s(available=()))[0],'PREFER_PRIVATE_HOTSPOT')
    def test_missing_recovery_blocks(self): self.assertEqual(p.decide(self.s(available=(),recovery_present=False))[0],'BLOCKED')
    def test_never_choose_recovery_as_saved(self): self.assertEqual(p.decide(self.s(available=(p.RECOVERY,)))[0],'PREFER_PRIVATE_HOTSPOT')
    def test_candidate_deterministic(self): self.assertEqual(p.decide(self.s(available=('Office','Home')))[1],'candidate=Home')
    def test_invalid_scenario_key(self):
        with self.assertRaises(ValueError): p.scenario({'bogus':True})
    def test_invalid_types(self):
        with self.assertRaises(ValueError): p.scenario({'session_locked':'false'})
    def test_scenario_defaults(self): self.assertEqual(p.scenario({}).active,'')

if __name__=='__main__': unittest.main()
