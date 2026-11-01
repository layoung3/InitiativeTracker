"""Execute the exact documented artwork command in disposable home/backup trees."""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import shlex
import signal
import shutil
import stat
import re
import subprocess
import sys
import tarfile
import tempfile
from types import SimpleNamespace
import unittest

ROOT = Path(__file__).resolve().parents[2]
GUIDE = ROOT / 'docs/setup/branding.md'

def procedure():
    block = GUIDE.read_text().split('<!-- branding-procedure:begin -->', 1)[1].split('<!-- branding-procedure:end -->', 1)[0]
    code = block.split("<<'PY'\n", 1)[1].rsplit('\nPY\n}', 1)[0]
    namespace = {'__name__': 'disposable_documentation_fixture'}
    exec(compile(code, str(GUIDE), 'exec'), namespace)
    return namespace

class BrandingDocumentationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='n9-brand-docs-')
        self.base = Path(self.temp.name)
        self.api = procedure()
        fixture_uid = 1000 if os.geteuid() == 0 else os.getuid()
        fixture_gid = 1000 if os.geteuid() == 0 else os.getgid()
        self.desktop = SimpleNamespace(pw_name='table_keeper', pw_uid=fixture_uid, pw_gid=fixture_gid, pw_dir=str(self.base/'a different desktop home'))
        self.home = Path(self.desktop.pw_dir)
        self.home.mkdir(mode=0o700)
        self.backups = self.base/'private backups'
        self.source = self.base/'approved artwork.png'
        shutil.copyfile(ROOT/'InitiativeTracker.Web/wwwroot/images/colophon.png',self.source)
    def tearDown(self): self.temp.cleanup()
    def setup_role(self,role='hub',supervised=False,exists=True):
        local=self.home/f'.local/lib/initiativetracker-{role}'
        local.mkdir(parents=True,mode=0o755 if role=='hub' else 0o700)
        self.role=role; self.local=local; self.target=local/'colophon.png'
        files={'initiativetracker-hub-launch':ROOT/'hub-updater/runtime/initiativetracker-hub-launch'} if role=='hub' else {'kiosk.py':ROOT/'scripts/installation/kiosk.py','waiting.html':ROOT/'scripts/installation/templates/waiting.html'}
        if supervised: files['kiosk.py']=ROOT/'scripts/installation/kiosk.py'
        for name,source in files.items():
            shutil.copyfile(source,local/name); (local/name).chmod(0o644 if name=='waiting.html' else 0o755)
        auto=self.home/'.config/labwc/autostart';auto.parent.mkdir(parents=True,mode=0o700)
        config=self.home/f'.config/initiativetracker-{role}/kiosk.json'
        if role=='satellite' or supervised:
            config.parent.mkdir(mode=0o700);config.write_text(json.dumps({'role':role,'hub_url':'http://fixture.invalid:5000','hub_id':'synthetic-do-not-log'}));config.chmod(0o600)
        line=(shlex.quote(str(local/'initiativetracker-hub-launch'))+(' --supervise '+shlex.quote(str(config)) if supervised else '')) if role=='hub' else '/usr/bin/python3 -I '+shlex.quote(str(local/'kiosk.py'))+' '+shlex.quote(str(config))
        auto.write_text('#!/bin/bash\n# Preserve unrelated startup\nprintf ready\n'+line+' &\n');auto.chmod(0o700)
        if exists:
            self.target.write_bytes(b'previous customer artwork');self.target.chmod(0o600);os.utime(self.target,ns=(1650000000123456789,1650000000987654321))
        profile=self.home/('.config/initiativetracker-hub/chromium' if role=='hub' else '.local/share/initiativetracker-satellite/chromium')
        profile.mkdir(parents=True,exist_ok=True);(profile/'Cookies').write_bytes(b'pairing-fixture-do-not-touch')
        if os.geteuid() == 0:
            for path in [self.home,*self.home.rglob('*')]:
                if path != self.target: os.chown(path,self.desktop.pw_uid,self.desktop.pw_gid)
        self.protected={p.relative_to(self.home):p.read_bytes() for p in self.home.rglob('*') if p.is_file() and p != self.target}
        self.directory_mode=stat.S_IMODE(local.stat().st_mode)
    def run_action(self,action,backup='-'):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            self.api['run'](action,self.desktop,self.role,str(self.source),str(backup),str(self.backups))
        return output.getvalue()
    def backup(self): return next(self.backups.iterdir())
    def assert_protected(self):
        for path,data in self.protected.items(): self.assertEqual(data,(self.home/path).read_bytes(),path)
        self.assertEqual(self.directory_mode,stat.S_IMODE(self.local.stat().st_mode))
    def test_embedded_bash_function_has_valid_syntax(self):
        block=GUIDE.read_text().split('<!-- branding-procedure:begin -->',1)[1].split('<!-- branding-procedure:end -->',1)[0]
        shell=block.split('```bash\n',1)[1].rsplit('```',1)[0]
        subprocess.run(['bash','-n'],input=shell,text=True,check=True,capture_output=True)
    def test_changed_guides_local_links_resolve(self):
        for relative in ['SetupReadme.md','docs/setup/branding.md','docs/setup/fresh-hub.md','docs/setup/satellite.md','docs/setup/automated-installation.md','docs/setup/hub-release.md','hub-updater/EXISTING-HUB-UPGRADE.md']:
            path=ROOT/relative
            prose=re.sub(r'```.*?```','',path.read_text(),flags=re.S)
            for target in re.findall(r'\[[^\]]+\]\(([^)]+)\)',prose):
                target=target.split('#',1)[0]
                if target and not re.match(r'[a-zA-Z]+:',target):self.assertTrue((path.parent/target).exists(),(relative,target))
    def extraction_code(self):
        prefix=GUIDE.read_text().split('<!-- branding-procedure:begin -->',1)[0]
        return prefix.split("<<'PY'\n",1)[1].split('\nPY\n',1)[0]
    def extract(self,archive):
        output=self.base/'extracted original artwork.png'
        result=subprocess.run([sys.executable,'-I','-',str(archive),str(output)],input=self.extraction_code(),text=True,capture_output=True)
        return result,output
    def test_exact_pinned_git_archive_extracts_original_bytes(self):
        archive=self.base/'git source archive.tar'
        subprocess.run(['git','-c','safe.directory='+str(ROOT),'archive','--format=tar','--output='+str(archive),self.api['PIN'],'InitiativeTracker.Web/wwwroot/images/colophon.png'],cwd=ROOT,check=True,capture_output=True)
        result,output=self.extract(archive);self.assertEqual(0,result.returncode,result.stderr)
        self.assertEqual(self.source.read_bytes(),output.read_bytes());self.assertEqual(0o644,stat.S_IMODE(output.stat().st_mode))
    def test_unexpected_duplicate_link_and_wrong_image_archives_never_extract(self):
        name='InitiativeTracker.Web/wwwroot/images/colophon.png'
        for kind in ['unexpected','duplicate','symlink','wrong-image']:
            with self.subTest(kind=kind):
                archive=self.base/(kind+'.tar')
                with tarfile.open(archive,'w') as stream:
                    entry=tarfile.TarInfo(name)
                    if kind=='symlink':entry.type=tarfile.SYMTYPE;entry.linkname='/etc/passwd';stream.addfile(entry)
                    else:
                        data=b'x'*73110 if kind=='wrong-image' else self.source.read_bytes()
                        entry.size=len(data);stream.addfile(entry,io.BytesIO(data))
                        if kind=='duplicate':stream.addfile(entry,io.BytesIO(data))
                        if kind=='unexpected':
                            extra=tarfile.TarInfo('../escape');extra.size=1;stream.addfile(extra,io.BytesIO(b'x'))
                result,output=self.extract(archive);self.assertNotEqual(0,result.returncode);self.assertFalse(output.exists());self.assertFalse((self.base.parent/'escape').exists())
    def test_unsafe_directory_modes_and_wrong_role_are_refused(self):
        self.setup_role('satellite');self.local.chmod(0o777)
        with self.assertRaisesRegex(RuntimeError,'directory ownership'):self.run_action('apply')
        self.local.chmod(0o700)
        config=self.home/'.config/initiativetracker-satellite/kiosk.json';config.write_text('{"role":"hub"}')
        with self.assertRaisesRegex(RuntimeError,'role/config'):self.run_action('apply')
        self.assertFalse(self.backups.exists())
    def test_root_account_and_symlink_home_are_refused(self):
        self.setup_role();uid=self.desktop.pw_uid;self.desktop.pw_uid=0
        with self.assertRaisesRegex(RuntimeError,'ordinary desktop'):self.run_action('apply')
        self.desktop.pw_uid=uid;self.home.rename(self.home.with_name('saved-home'));self.home.symlink_to(self.home.with_name('saved-home'),target_is_directory=True)
        with self.assertRaises(OSError):self.run_action('apply')
        self.assertFalse(self.backups.exists())
    def test_extended_attributes_need_separate_review(self):
        self.setup_role()
        try:os.setxattr(self.target,'user.fixture',b'preserve-me')
        except OSError:self.skipTest('Fixture filesystem lacks user xattrs')
        with self.assertRaisesRegex(RuntimeError,'ACL/xattrs'):self.run_action('apply')
        self.assertFalse(self.backups.exists());self.assertEqual(b'preserve-me',os.getxattr(self.target,'user.fixture'))
    def test_private_backup_modes_are_exact_and_callers_umask_is_restored(self):
        self.setup_role();previous=os.umask(0o777)
        try:
            self.run_action('apply');backup=self.backup()
            self.assertEqual(0o700,stat.S_IMODE(backup.stat().st_mode))
            self.assertEqual(0o600,stat.S_IMODE((backup/'receipt.json').stat().st_mode))
            self.assertEqual(0o644,stat.S_IMODE(self.target.stat().st_mode))
            self.assertEqual(0o777,os.umask(0o777))
        finally:os.umask(previous)
    def test_pin_and_all_asset_component_hashes_match_merged_master(self):
        for key,path in [('PNG','InitiativeTracker.Web/wwwroot/images/colophon.png'),('LAUNCH','hub-updater/runtime/initiativetracker-hub-launch'),('KIOSK','scripts/installation/kiosk.py'),('WAIT','scripts/installation/templates/waiting.html')]:
            self.assertEqual(self.api[key],hashlib.sha256((ROOT/path).read_bytes()).hexdigest())
        self.assertEqual('d9470510eba7af584d5c8f8ba05250d275979a62',self.api['PIN'])
    def test_existing_and_absent_assets_round_trip_for_both_roles_and_supervised_hub(self):
        for role,supervised,exists in [('hub',False,True),('hub',True,False),('satellite',False,True),('satellite',False,False)]:
            with self.subTest(role=role,supervised=supervised,exists=exists):
                if hasattr(self,'local'):
                    shutil.rmtree(self.home);self.home.mkdir(mode=0o700);shutil.rmtree(self.backups,ignore_errors=True)
                self.desktop.pw_name='screen_owner' if role=='satellite' else 'table_keeper'
                self.setup_role(role,supervised,exists)
                old=self.target.stat() if exists else None
                self.assertIn('INSPECT ONLY',self.run_action('inspect'));self.assertFalse(self.backups.exists())
                self.run_action('apply');backup=self.backup()
                self.assertEqual(self.source.read_bytes(),self.target.read_bytes());self.assertEqual(0o644,stat.S_IMODE(self.target.stat().st_mode))
                self.assertEqual((self.desktop.pw_uid,self.desktop.pw_gid),(self.target.stat().st_uid,self.target.stat().st_gid))
                self.assertEqual(0o700,stat.S_IMODE(backup.stat().st_mode));self.assertEqual(0o600,stat.S_IMODE((backup/'receipt.json').stat().st_mode))
                self.assert_protected();self.run_action('rollback',backup)
                if old:
                    now=self.target.stat();self.assertEqual((old.st_uid,old.st_gid,stat.S_IMODE(old.st_mode),old.st_atime_ns,old.st_mtime_ns),(now.st_uid,now.st_gid,stat.S_IMODE(now.st_mode),now.st_atime_ns,now.st_mtime_ns));self.assertEqual(b'previous customer artwork',self.target.read_bytes())
                else:self.assertFalse(self.target.exists())
                self.assertIn('Already at',self.run_action('rollback',backup));self.assert_protected()
    def test_unknown_older_components_stop_without_creating_backup(self):
        self.setup_role();(self.local/'initiativetracker-hub-launch').write_bytes(b'old launcher')
        with self.assertRaisesRegex(RuntimeError,'Unsupported/older'):self.run_action('apply')
        self.assertFalse(self.backups.exists());self.assertEqual(b'previous customer artwork',self.target.read_bytes())
    def test_missing_satellite_supporting_page_stops(self):
        self.setup_role('satellite');(self.local/'waiting.html').unlink()
        with self.assertRaises(FileNotFoundError):self.run_action('apply')
        self.assertFalse(self.backups.exists())
    def test_wrong_source_hash_stops(self):
        self.setup_role();self.source.write_bytes(b'wrong png')
        with self.assertRaisesRegex(RuntimeError,'hash/length'):self.run_action('apply')
        self.assertFalse(self.backups.exists())
    def test_symlink_asset_source_and_parent_are_refused(self):
        for point in ('asset','source','parent'):
            with self.subTest(point=point):
                if hasattr(self,'local'):shutil.rmtree(self.home);self.home.mkdir(mode=0o700)
                self.setup_role();outside=self.base/('outside-'+point);outside.write_bytes(b'untouched')
                if point=='asset':self.target.unlink();self.target.symlink_to(outside)
                if point=='source':self.source.unlink();self.source.symlink_to(outside)
                if point=='parent':self.local.rename(self.local.with_name(self.local.name+'-saved'));self.local.symlink_to(self.local.with_name(self.local.name+'-saved'),target_is_directory=True)
                with self.assertRaises(OSError):self.run_action('apply')
                self.assertEqual(b'untouched',outside.read_bytes());self.assertFalse(self.backups.exists())
                if self.source.is_symlink():self.source.unlink();shutil.copyfile(ROOT/'InitiativeTracker.Web/wwwroot/images/colophon.png',self.source)
    def test_fifo_asset_and_source_are_refused_without_blocking(self):
        self.setup_role()
        for path in [self.target,self.source]:
            with self.subTest(path=path):
                path.unlink();os.mkfifo(path,0o600)
                def timeout(signum,frame):raise TimeoutError('Special-file inspection blocked')
                old_handler=signal.signal(signal.SIGALRM,timeout);signal.alarm(3)
                try:
                    with self.assertRaisesRegex(RuntimeError,'regular file'):self.run_action('apply')
                finally:signal.alarm(0);signal.signal(signal.SIGALRM,old_handler)
                self.assertFalse(self.backups.exists());path.unlink()
                if path==self.target:path.write_bytes(b'previous customer artwork')
                else:shutil.copyfile(ROOT/'InitiativeTracker.Web/wwwroot/images/colophon.png',path)
    def test_hardlinked_asset_is_refused(self):
        self.setup_role();os.link(self.target,self.base/'hardlink')
        with self.assertRaisesRegex(RuntimeError,'hardlinks'):self.run_action('apply')
        self.assertFalse(self.backups.exists())
    def test_wrong_labwc_home_or_duplicate_command_is_refused(self):
        self.setup_role();auto=self.home/'.config/labwc/autostart';original=auto.read_text()
        for text in [original.replace(str(self.home),'/home/someone-else'),original+original.splitlines()[-1]+'\n']:
            auto.write_text(text)
            with self.assertRaises(RuntimeError):self.run_action('apply')
        self.assertFalse(self.backups.exists())
    def test_later_customer_artwork_is_not_overwritten_on_rollback(self):
        self.setup_role();self.run_action('apply');self.target.write_bytes(b'later artwork')
        with self.assertRaisesRegex(RuntimeError,'changed since apply'):self.run_action('rollback',self.backup())
        self.assertEqual(b'later artwork',self.target.read_bytes())
    def test_changed_components_and_wrong_account_are_refused_on_rollback(self):
        self.setup_role();self.run_action('apply');backup=self.backup();self.desktop.pw_name='different-account'
        with self.assertRaisesRegex(RuntimeError,'Wrong device'):self.run_action('rollback',backup)
        self.desktop.pw_name='table_keeper';auto=self.home/'.config/labwc/autostart';auto.write_text(auto.read_text()+'# later edit\n')
        with self.assertRaisesRegex(RuntimeError,'changed components'):self.run_action('rollback',backup)
    def test_corrupt_backup_does_not_replace_current_artwork(self):
        self.setup_role();self.run_action('apply');backup=self.backup();(backup/'previous.png').write_bytes(b'bad backup')
        with self.assertRaisesRegex(RuntimeError,'corrupt/unsafe'):self.run_action('rollback',backup)
        self.assertEqual(self.source.read_bytes(),self.target.read_bytes())
    def test_symlink_backup_base_is_refused(self):
        self.setup_role();other=self.base/'other';other.mkdir(mode=0o700);self.backups.symlink_to(other,target_is_directory=True)
        with self.assertRaises(OSError):self.run_action('apply')
        self.assertEqual([],list(other.iterdir()))
    def test_interruption_after_asset_replace_keeps_a_restorable_receipt(self):
        self.setup_role();original=self.api['atomic_file']
        def interrupted(fd,name,data,meta):
            original(fd,name,data,meta)
            if name=='colophon.png':raise OSError('disposable post-rename interruption')
        self.api['atomic_file']=interrupted
        with self.assertRaises(OSError):self.run_action('apply')
        self.api['atomic_file']=original;self.run_action('rollback',self.backup())
        self.assertEqual(b'previous customer artwork',self.target.read_bytes());self.assert_protected()

if __name__=='__main__':unittest.main()
