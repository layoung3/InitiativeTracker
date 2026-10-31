"""Actual broker decisions and journals with persistent fake NM; test-only, never installed."""
import importlib.machinery
import importlib.util
import json
from pathlib import Path
import sys
import time

repository, directory = Path(sys.argv[1]), Path(sys.argv[2])
loader = importlib.machinery.SourceFileLoader('actual_broker', str(repository/'pi-satellite/initiativetracker-satellite-network'))
spec = importlib.util.spec_from_loader(loader.name, loader)
broker = importlib.util.module_from_spec(spec)
loader.exec_module(broker)
broker.ROOT, broker.PROFILES = directory/'root', directory/'profiles'
broker.ROOT.mkdir(mode=0o700, exist_ok=True)
broker.PROFILES.mkdir(mode=0o700, exist_ok=True)
network_path = directory/'fake-network.json'
network = json.loads(network_path.read_text()) if network_path.exists() else {'active': None, 'commands': []}


def nm(*args):
    network['commands'].append(args)
    result = ''
    if args[:3] == ('-g', 'GENERAL.CON-UUID', 'device'):
        result = network['active'] or '--'
    elif 'up' in args:
        network['active'] = args[args.index('uuid')+1]
    elif 'down' in args:
        network['active'] = None
    network_path.write_text(json.dumps(network))
    return result


broker.nm = nm
request = json.load(sys.stdin)
try:
    if request['action'] == '__fixture-tick':
        broker.tick()
        response = {'ok': True, 'state': broker.load(), 'network': network}
    elif request['action'] == '__fixture-expire':
        state = broker.load()
        state['transition']['mono_deadline'] = time.monotonic()-1
        broker.save(state)
        broker.tick()
        response = {'ok': True, 'state': broker.load(), 'network': network}
    else:
        response = broker.execute(request)
except (ValueError, RuntimeError, OSError, KeyError, TypeError):
    response = {'ok': False}
print(json.dumps(response))
