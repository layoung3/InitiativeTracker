"""Existing hub return controller handles external A -> B with its independent rollback timer."""
import importlib.machinery
import importlib.util
import json
from pathlib import Path
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import uuid


def module(name):
    path=Path(__file__).resolve().parents[2]/'pi-network'/name
    loader=importlib.machinery.SourceFileLoader(name.replace('-','_'),str(path))
    spec=importlib.util.spec_from_loader(loader.name,loader)
    result=importlib.util.module_from_spec(spec)
    loader.exec_module(result)
    return result


controller=module('initiativetracker-network-return')
handoff=module('initiativetracker-return-handoff')


class ExternalWifiTests(unittest.TestCase):
    def test_real_controller_persists_previous_uuid_and_restores_it_on_timeout(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);previous,target=str(uuid.uuid4()),str(uuid.uuid4())
            current={'uuid':previous,'name':'IT-External-A'};calls=[]
            profiles=[{'uuid':previous,'name':'IT-External-A'},{'uuid':target,'name':'IT-External-B'}]
            def nm(*args,**kwargs):
                calls.append(args)
                if args[:2]==('-g','GENERAL.CON-UUID'):return current['uuid']
                if 'up' in args:
                    value=args[args.index('uuid')+1];current.update(uuid=value,name=next(p['name'] for p in profiles if p['uuid']==value))
                return ''
            with patch.multiple(controller,ROOT=root,JOURNAL=root/'return.json',LOCK=root/'return.lock'),patch.object(controller.os,'geteuid',return_value=0),patch.object(controller,'enabled',return_value=True),patch.object(controller,'pending_recovery',return_value=False),patch.object(controller,'saved',return_value=profiles),patch.object(controller,'active',side_effect=lambda:current['name']),patch.object(controller,'nm',side_effect=nm):
                controller.run(SimpleNamespace(command='begin',uuid=target,seconds=180))
                state=controller.read();self.assertEqual(state['previous_uuid'],previous);self.assertEqual(current['uuid'],target)
                state['deadline']=time.time()-1;controller.write(state)
                controller.run(SimpleNamespace(command='tick'))
                self.assertEqual(current['uuid'],previous);self.assertEqual(controller.read()['state'],'rolled_back')
                self.assertTrue(any('uuid' in c and previous in c for c in calls))

    def test_failed_previous_wifi_restoration_falls_back_to_private_hotspot_durably(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);previous=str(uuid.uuid4());calls=[]
            def nm(*args,**kwargs):
                calls.append(args)
                if previous in args:raise RuntimeError('synthetic previous router unavailable')
                return ''
            with patch.multiple(controller,ROOT=root,JOURNAL=root/'return.json'),patch.object(controller,'nm',side_effect=nm):
                state={'state':'pending','previous_uuid':previous}
                controller.rollback(state)
                persisted=controller.read()
                self.assertEqual(persisted['state'],'rolled_back')
                self.assertTrue(persisted['fallback_private_hotspot'])
                self.assertTrue(any('id' in c and controller.HOTSPOT in c for c in calls))

    def test_handoff_dispatches_external_wifi_without_confirming_a_nonexistent_hotspot_switch(self):
        target=str(uuid.uuid4());calls=[]
        def command(args,**kwargs):
            calls.append(args)
            if args==[handoff.RECOVERY,'status']:return 'ACTIVE_PROFILE=IT-External-A\nJOURNAL_STATE=completed'
            if args==[handoff.RETURN,'status']:return 'ACTIVE_PROFILE=IT-External-A\nRETURN_SWITCH_ENABLED=true\nRETURN_JOURNAL_STATE=confirmed'
            if args[0]=='/usr/bin/systemctl':return 'active'
            if args==[handoff.RETURN,'list-saved']:return 'SAVED_WIFI='+json.dumps([{'uuid':target,'name':'IT-External-B'}])
            return ''
        with patch.object(handoff.os,'geteuid',return_value=0),patch.object(handoff,'run_command',side_effect=command):
            handoff.execute(target,180)
        self.assertIn([handoff.RETURN,'begin','--uuid',target,'--seconds','180'],calls)
        self.assertNotIn([handoff.RECOVERY,'confirm'],calls)


if __name__=='__main__':unittest.main()
