import importlib.machinery, importlib.util
from pathlib import Path
p=Path(__file__).parents[1]/'initiativetracker-boot-fallback'
loader=importlib.machinery.SourceFileLoader('n67',str(p));spec=importlib.util.spec_from_loader('n67',loader)
m=importlib.util.module_from_spec(spec);loader.exec_module(m)

def check():
    assert m.decision('external','100 (connected)',False,900,None,[])[0]=='HOLD'
    assert m.decision('InitiativeTracker-Recovery','100 (connected)',False,900,None,[])[0]=='HOLD'
    assert m.decision('other AP','100 (connected)',False,900,None,[])[0]=='HOLD'
    assert m.decision('','30 (disconnected)',True,900,200,[300,500])[0]=='HOLD'
    assert m.decision('','20 (unavailable)',False,900,100,[200,300])[0]=='HOLD'
    assert m.decision('','30 (disconnected)',False,10,10,[])[0]=='SCAN'
    assert m.decision('','30 (disconnected)',False,25,10,[10])[0]=='SCAN'
    assert m.decision('','30 (disconnected)',False,59,10,[10,25])[0]=='WAIT'
    assert m.decision('','30 (disconnected)',False,60,10,[10,25])[0]=='HOTSPOT'
    assert m.decision('','30 (disconnected)',False,90,80,[])[0]=='SCAN'
    print('10 startup timing and active connection policy tests passed')
if __name__=='__main__':check()
