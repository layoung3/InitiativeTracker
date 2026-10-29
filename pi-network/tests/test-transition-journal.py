import importlib.machinery
import importlib.util
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
SCRIPT=Path(__file__).resolve().parents[1]/'initiativetracker-transition-journal'
loader=importlib.machinery.SourceFileLoader('journal',str(SCRIPT));spec=importlib.util.spec_from_loader(loader.name,loader);mod=importlib.util.module_from_spec(spec);loader.exec_module(mod)
T=datetime(2026,10,9,tzinfo=timezone.utc)
class Tests(unittest.TestCase):
 def make(self):
  self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
  p=Path(self.tmp.name)/'transition.json';return p,mod.create(p,'homewifi','otherwifi',90,T)
 def test_persist(self):
  p,j=self.make();self.assertEqual(mod.load(p)['id'],j['id']);self.assertEqual(p.stat().st_mode & 0o777,0o600)
 def test_no_overwrite(self):
  p,j=self.make();self.assertRaises(ValueError,mod.create,p,'a','b',90,T)
 def test_wait(self):
  _,j=self.make();self.assertEqual(mod.decide(j,'homewifi',now=T+timedelta(seconds=10)),'WAIT')
 def test_validated(self):
  _,j=self.make();self.assertEqual(mod.decide(j,'otherwifi',True,now=T+timedelta(seconds=20)),'VALIDATE_TARGET')
 def test_expired_previous(self):
  _,j=self.make();self.assertEqual(mod.decide(j,'none',now=T+timedelta(seconds=91)),'TRY_PREVIOUS')
 def test_expired_fallback(self):
  _,j=self.make();self.assertEqual(mod.decide(j,'none',previous_available=False,now=T+timedelta(seconds=91)),'TRY_RECOVERY')
 def test_recovered(self):
  _,j=self.make();self.assertEqual(mod.decide(j,'homewifi',now=T+timedelta(seconds=91)),'MARK_RECOVERED')
 def test_recovery_active(self):
  _,j=self.make();self.assertEqual(mod.decide(j,mod.RECOVERY,now=T+timedelta(seconds=91)),'MARK_RECOVERED')
 def test_bad_names(self):
  self.assertRaises(ValueError,mod.valid_profile,'bad name')
 def test_bad_deadline(self):
  with tempfile.TemporaryDirectory() as d:self.assertRaises(ValueError,mod.create,Path(d)/'transition.json','a','b',5,T)
 def test_symlink(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d)/'transition.json';p.symlink_to('/tmp/irrelevant');self.assertRaises(ValueError,mod.load,p)
if __name__=='__main__':unittest.main()
