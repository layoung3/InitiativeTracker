"""Basic state/validation tests for N6.8; run `python3 -m unittest discover -s pi-network/tests -p 'test_n68_return.py'`."""
import importlib.machinery, importlib.util, pathlib, tempfile, unittest
P=pathlib.Path(__file__).resolve().parents[1]/'initiativetracker-network-return'
loader=importlib.machinery.SourceFileLoader('return_module',str(P))
spec=importlib.util.spec_from_loader('return_module',loader)
mod=importlib.util.module_from_spec(spec);loader.exec_module(mod)
class ReturnTests(unittest.TestCase):
 def test_gate_off_when_missing(self):
  old=mod.GATE
  with tempfile.TemporaryDirectory() as d:
   mod.GATE=pathlib.Path(d)/'absent';self.assertFalse(mod.enabled())
  mod.GATE=old
 def test_gate_rejects_wrong_content(self):
  old=mod.GATE
  with tempfile.TemporaryDirectory() as d:
   p=pathlib.Path(d)/'flag';p.write_text('wrong');p.chmod(0o600);mod.GATE=p
   self.assertFalse(mod.enabled())
  mod.GATE=old
 def test_gate_rejects_open_permissions(self):
  old=mod.GATE
  with tempfile.TemporaryDirectory() as d:
   p=pathlib.Path(d)/'flag';p.write_text('ENABLE_RETURN_SWITCH=yes');p.chmod(0o644);mod.GATE=p
   self.assertFalse(mod.enabled())
  mod.GATE=old
 def test_empty_journal(self):
  old=mod.JOURNAL
  with tempfile.TemporaryDirectory() as d:
   mod.JOURNAL=pathlib.Path(d)/'journal';self.assertIsNone(mod.read())
  mod.JOURNAL=old
 def test_pending_journal_parse(self):
  old=mod.JOURNAL
  with tempfile.TemporaryDirectory() as d:
   p=pathlib.Path(d)/'journal';p.write_text('{"state":"pending"}');mod.JOURNAL=p
   self.assertEqual(mod.read()['state'],'pending')
  mod.JOURNAL=old
 def test_bad_journal_fails_closed(self):
  old=mod.JOURNAL
  with tempfile.TemporaryDirectory() as d:
   p=pathlib.Path(d)/'journal';p.write_text('{"state":"unexpected"}');mod.JOURNAL=p
   with self.assertRaises(RuntimeError):mod.read()
  mod.JOURNAL=old
if __name__=='__main__':unittest.main()
