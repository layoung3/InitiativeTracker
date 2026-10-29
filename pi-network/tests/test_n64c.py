import importlib.machinery
import importlib.util
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path

FILE = Path(__file__).resolve().parents[1] / 'initiativetracker-network-control'
spec = importlib.util.spec_from_loader('controller', importlib.machinery.SourceFileLoader('controller', str(FILE)))
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

class Tests(unittest.TestCase):
    def journal(self, state='pending', seconds=60):
        now=datetime.now(timezone.utc)
        return dict(format=1,id='test',previous='netplan-wlan0-Logan',target=mod.RECOVERY,
                    created_utc=mod.stamp(now-timedelta(seconds=30)),deadline_utc=mod.stamp(now+timedelta(seconds=seconds)),state=state,events=[])
    def test_wait(self): self.assertEqual('WAIT',mod.determine(self.journal(), 'netplan-wlan0-Logan',datetime.now(timezone.utc)))
    def test_expired_recovery(self): self.assertEqual('RECOVER',mod.determine(self.journal(seconds=-1),'different',datetime.now(timezone.utc)))
    def test_active_previous(self): self.assertEqual('RECOVERED_ALREADY',mod.determine(self.journal(seconds=-1),'netplan-wlan0-Logan',datetime.now(timezone.utc)))
    def test_active_hotspot(self): self.assertEqual('RECOVER',mod.determine(self.journal(seconds=-1),mod.RECOVERY,datetime.now(timezone.utc)))
    def test_final(self): self.assertEqual('NONE',mod.determine(self.journal('completed'),'unknown',datetime.now(timezone.utc)))
    def test_no_journal(self): self.assertEqual('NONE',mod.determine(None,'unknown',datetime.now(timezone.utc)))
    def test_symlink_refused(self):
        with tempfile.TemporaryDirectory() as d:
            a=Path(d)/'a'; b=Path(d)/'b'; b.write_text('x'); a.symlink_to(b)
            with self.assertRaises(ValueError): mod.load(a)
    def test_roundtrip(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'transition.json'; mod.save(p,self.journal())
            self.assertEqual(mod.load(p)['state'],'pending')
            self.assertEqual(p.stat().st_mode & 0o777,0o600)
    def test_gate_absent(self):
        old=mod.GATE
        with tempfile.TemporaryDirectory() as d:
            mod.GATE=Path(d)/'missing'
            try: self.assertFalse(mod.enabled())
            finally: mod.GATE=old
    def test_invalid_name(self):
        with self.assertRaises(ValueError): mod.valid_name('evil/filename')
    def test_strict_schema(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'transition.json'; item=self.journal(); item['extra']='fail'; mod.save(p,item)
            with self.assertRaises(ValueError): mod.load(p)

if __name__=='__main__': unittest.main()
