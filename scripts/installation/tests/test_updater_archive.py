"""Real Git exports in tiny disposable repositories; no installed hub required."""
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
NAMES = ('initiativetracker-install-update', 'initiativetracker-check-update',
         'initiativetracker-hub-launch')


class ArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='it-archive-')
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.repo = self.base / 'repository'
        self.repo.mkdir()
        shutil.copyfile(ROOT / '.gitattributes', self.repo / '.gitattributes')
        shutil.copytree(ROOT / 'hub-updater', self.repo / 'hub-updater',
                        ignore=shutil.ignore_patterns('__pycache__'))
        self.git('init', '-q')
        self.git('-c', 'core.autocrlf=false', 'add', '.')
        self.commit()

    def git(self, *args):
        # Ignore workstation/system Git configuration, including filters and
        # signing hooks; the explicit conversion settings under test remain real.
        env = dict(os.environ, GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL=os.devnull)
        return subprocess.check_output(['git', '-C', str(self.repo), *args], env=env)

    def commit(self):
        self.git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid',
                 '-c', 'commit.gpgsign=false', 'commit', '-qm', 'disposable snapshot')

    def export(self, autocrlf='true', eol='crlf'):
        archive = self.base / 'bundle.tar'
        self.git('-c', 'core.autocrlf=' + autocrlf, '-c', 'core.eol=' + eol,
                 'archive', '--format=tar', '--output=' + str(archive), 'HEAD', 'hub-updater')
        return archive

    def verify(self, archive):
        return subprocess.run([sys.executable, str(ROOT / 'hub-updater/verify-archive.py'), str(archive)],
                              text=True, capture_output=True)

    def test_archived_bytes_and_clean_checkouts_match_both_manifests_for_all_git_eol_settings(self):
        for autocrlf in ('false', 'true', 'input'):
            for eol in ('lf', 'crlf'):
                with self.subTest(autocrlf=autocrlf, eol=eol):
                    archive = self.export(autocrlf, eol)
                    result = self.verify(archive)
                    self.assertEqual(result.returncode, 0, result.stderr)
                    self.assertEqual(result.stdout.count('sha256='), 6)
                    for variant in ('original', 'runtime'):
                        for name in NAMES:
                            path = f'hub-updater/{variant}/{name}'
                            blob = self.git('show', 'HEAD:' + path)
                            with tarfile.open(archive) as tar:
                                self.assertEqual(tar.extractfile(path).read(), blob)
                            # Force a fresh checkout rather than trusting copied files.
                            (self.repo / path).unlink()
                            self.git('-c', 'core.autocrlf=' + autocrlf, '-c', 'core.eol=' + eol,
                                     'checkout-index', '-f', '--', path)
                            self.assertEqual((self.repo / path).read_bytes(), blob)
                            self.assertNotIn(b'\r', blob)
                            self.assertFalse(blob.startswith(b'\xef\xbb\xbf'))

    def test_old_attributes_reproduce_six_crlf_mismatches_without_changing_blobs(self):
        # Model the exact relevant attribute state at 7f940bbb: only text=auto.
        (self.repo / '.gitattributes').write_bytes(b'* text=auto\n')
        self.git('add', '.gitattributes')
        self.commit()
        with tarfile.open(self.export()) as tar:
            for variant in ('original', 'runtime'):
                for name in NAMES:
                    path = f'hub-updater/{variant}/{name}'
                    blob = self.git('show', 'HEAD:' + path)
                    exported = tar.extractfile(path).read()
                    self.assertEqual(exported, blob.replace(b'\n', b'\r\n'))
                    self.assertNotEqual(hashlib.sha256(exported).digest(), hashlib.sha256(blob).digest())
        self.assertNotEqual(self.verify(self.base / 'bundle.tar').returncode, 0)

    @unittest.skipIf(os.name == 'nt', 'documented Linux workstation commands require Bash')
    def test_exact_documented_bash_export_and_verification(self):
        guide = (ROOT / 'hub-updater/EXISTING-HUB-UPGRADE.md').read_text()
        section = guide.split('**Linux workstation — Bash:**', 1)[1]
        snippet = re.search(r'```bash\n(.*?)```', section, re.S).group(1)
        sha = self.git('rev-parse', 'HEAD').decode().strip()
        snippet = snippet.replace('APPROVED_40_CHARACTER_COMMIT_SHA', sha)
        result = subprocess.run(['/bin/bash', '-euo', 'pipefail', '-c', snippet], cwd=self.repo,
                                text=True, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('PASS: all six', result.stdout)

    def rewrite(self, archive, target, transform):
        modified = self.base / 'modified.tar'
        with tarfile.open(archive) as source, tarfile.open(modified, 'w') as dest:
            for entry in source:
                data = source.extractfile(entry).read() if entry.isfile() else None
                if entry.name == target:
                    data = transform(data)
                    entry.size = len(data)
                dest.addfile(entry, io.BytesIO(data) if data is not None else None)
        return modified

    def test_each_tampered_script_and_each_manifest_still_fail(self):
        archive = self.export()
        for variant in ('original', 'runtime'):
            for name in NAMES:
                with self.subTest(variant=variant, name=name):
                    modified = self.rewrite(archive, f'hub-updater/{variant}/{name}', lambda b: b + b'\n# tampered\n')
                    result = self.verify(modified)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn('manifest mismatch', result.stderr)
        for filename, field in (('provenance.json', 'files'), ('runtime-manifest.json', 'sha256')):
            def bad_manifest(data):
                document = json.loads(data)
                if field == 'files':
                    document[field][0]['sha256'] = '0' * 64
                else:
                    document[field][NAMES[0]] = '0' * 64
                return json.dumps(document).encode()
            result = self.verify(self.rewrite(archive, 'hub-updater/' + filename, bad_manifest))
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('manifest mismatch', result.stderr)

    def test_duplicate_link_and_traversing_archive_members_fail(self):
        archive = self.export()
        for name, kind in (('hub-updater/provenance.json', tarfile.REGTYPE),
                           ('hub-updater/link', tarfile.SYMTYPE),
                           ('hub-updater/../outside', tarfile.REGTYPE)):
            modified = self.base / 'unsafe.tar'
            shutil.copyfile(archive, modified)
            with tarfile.open(modified, 'a') as tar:
                entry = tarfile.TarInfo(name)
                entry.type = kind
                if kind == tarfile.SYMTYPE:
                    entry.linkname = '/outside'
                tar.addfile(entry)
            result = self.verify(modified)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('archive member', result.stderr)

    @unittest.skipIf(os.name == 'nt', 'Linux inspector imports fcntl/pwd; exercised in Linux CI')
    def test_extracted_candidates_load_and_six_shell_scripts_parse(self):
        extracted = self.base / 'extracted'
        with tarfile.open(self.export()) as tar:
            tar.extractall(extracted, filter='data')
        bundle = extracted / 'hub-updater'
        spec = importlib.util.spec_from_file_location('archived_components', bundle / 'upgrade-existing-components.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        # candidates() reads only this disposable bundle; no installed paths.
        from types import SimpleNamespace
        manager = module.Components(SimpleNamespace(pw_uid=1000, pw_dir='/home/fixture'), bundle=bundle)
        self.assertEqual(set(manager.candidates()), set(NAMES))
        for variant in ('original', 'runtime'):
            for name in NAMES:
                subprocess.run(['/bin/bash', '-p', '-n', str(bundle / variant / name)], check=True)
        for variant, message in (('original', 'Original source provenance mismatch'),
                                 ('runtime', 'Candidate runtime hash mismatch')):
            path = bundle / variant / NAMES[0]
            data = path.read_bytes()
            path.write_bytes(data.replace(b'\n', b'\r\n'))
            with self.assertRaisesRegex(module.UpgradeError, message):
                manager.candidates()
            path.write_bytes(data)


if __name__ == '__main__':
    unittest.main()
