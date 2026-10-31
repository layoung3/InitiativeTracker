"""Run real NM parsing tests only inside a disposable container without network devices."""
import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest


def main():
    if (os.environ.get('IT_N8_REAL_NM_CONTAINER') != '1' or
            not Path('/.dockerenv').exists() or os.geteuid() != 0 or
            {p.name for p in Path('/sys/class/net').iterdir()} != {'lo'}):
        raise RuntimeError('Requires isolated network-none disposable container')
    processes = []
    try:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            Path('/run/dbus').mkdir(parents=True, exist_ok=True)
            subprocess.run(['dbus-uuidgen', '--ensure'], check=True)
            with (root / 'daemons.log').open('wb') as log:
                processes.append(subprocess.Popen(['dbus-daemon', '--system', '--nofork'], stdout=log, stderr=log))
                for _ in range(80):
                    if Path('/run/dbus/system_bus_socket').exists():
                        break
                    if processes[0].poll() is not None:
                        raise RuntimeError('Disposable DBus failed to start')
                    time.sleep(.1)
                else:
                    raise RuntimeError('Disposable DBus startup timed out')
                config = root / 'NetworkManager.conf'
                config.write_text('[main]\nplugins=keyfile\ndns=none\n[connectivity]\nenabled=false\n')
                processes.append(subprocess.Popen(['/usr/sbin/NetworkManager', '--no-daemon', '--config', str(config),
                    '--pid-file', str(root / 'nm.pid'), '--state-file', str(root / 'nm.state')], stdout=log, stderr=log))
                for _ in range(80):
                    ready = subprocess.run(['nmcli', 'general', 'status'], capture_output=True)
                    if ready.returncode == 0:
                        break
                    if processes[-1].poll() is not None:
                        raise RuntimeError('Disposable NetworkManager failed to start')
                    time.sleep(.1)
                else:
                    raise RuntimeError('Disposable NetworkManager startup timed out')
                suite = unittest.defaultTestLoader.discover(str(Path(__file__).parent), pattern='test_real_nm.py')
                if not unittest.TextTestRunner(verbosity=2).run(suite).wasSuccessful():
                    raise RuntimeError('Real NetworkManager validation failed')
    finally:
        for process in reversed(processes):
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()


if __name__ == '__main__':
    main()
