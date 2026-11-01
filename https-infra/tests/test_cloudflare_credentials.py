import importlib.util
import os
from pathlib import Path
import stat
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import contextlib
import io

ROOT=Path(__file__).parents[1]
spec=importlib.util.spec_from_file_location('cloudflare_credentials',ROOT/'cloudflare_credentials.py')
c=importlib.util.module_from_spec(spec);spec.loader.exec_module(c)

class CredentialTests(unittest.TestCase):
    def setUp(self):
        # The cloud sandbox maps the host root owner to overflow UID 65534.
        # Model root-owned / and sticky /tmp only in this fixture; production
        # ownership checks remain strict and have no namespace bypass option.
        original = Path.lstat
        def fixture_stat(path):
            value = original(path)
            if str(path) in {'/', '/tmp'} and value.st_uid == 65534:
                fields = list(value); fields[4] = 0
                return os.stat_result(fields)
            return value
        mapping = patch.object(Path, 'lstat', fixture_stat)
        mapping.start(); self.addCleanup(mapping.stop)
    def test_private_file_and_explicit_rotation(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'cloudflare.ini'; token='synthetic_test_token_never_a_real_credential'
            c.write_credentials(p, token)
            self.assertEqual(0o600,stat.S_IMODE(p.stat().st_mode))
            self.assertEqual('dns_cloudflare_api_token = '+token+'\n',p.read_text())
            with self.assertRaises(c.Rejected): c.write_credentials(p, token)
            c.write_credentials(p,token+'_rotated',replace=True)
            self.assertTrue(p.read_text().endswith('_rotated\n'))
            self.assertEqual(1,p.stat().st_nlink)
    def test_symlink_hardlink_directory_and_injection_are_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'cloudflare.ini'; target=Path(tmp)/'target';target.write_text('unchanged')
            p.symlink_to(target)
            with self.assertRaises(c.Rejected):c.write_credentials(p,'synthetic_test_token_long_enough',replace=True)
            self.assertEqual('unchanged',target.read_text());p.unlink()
            os.link(target,p)
            with self.assertRaises(c.Rejected):c.write_credentials(p,'synthetic_test_token_long_enough',replace=True)
            p.unlink();p.mkdir()
            with self.assertRaises(c.Rejected):c.write_credentials(p,'synthetic_test_token_long_enough',replace=True)
            p.rmdir()
            with self.assertRaises(c.Rejected):c.write_credentials(p,'synthetic_token\nsecond_key = bad')
            Path(tmp).chmod(0o755)
            with self.assertRaises(c.Rejected):c.write_credentials(p,'synthetic_test_token_long_enough')
    def test_cli_refuses_pipe_and_has_no_token_argument_or_secret_output(self):
        result=subprocess.run(['/usr/bin/python3','-I',str(ROOT/'cloudflare_credentials.py'),'--path','/tmp/never-created-cloudflare.ini'],input='synthetic_test_token_never_a_real_credential\n',capture_output=True,text=True)
        self.assertNotEqual(0,result.returncode)
        self.assertNotIn('synthetic_test_token',result.stdout+result.stderr)
        result=subprocess.run(['/usr/bin/python3','-I',str(ROOT/'cloudflare_credentials.py'),'--help'],capture_output=True,text=True)
        self.assertNotIn('--token',result.stdout)

    def test_terminal_echo_failure_is_rejected_before_reading_or_writing(self):
        output = io.StringIO()
        with patch.object(c.os, 'geteuid', return_value=0), patch.object(c.sys.stdin, 'isatty', return_value=True), patch.object(c.resource, 'setrlimit'), patch.object(c.getpass, 'getpass', side_effect=c.getpass.GetPassWarning('synthetic-secret-must-not-echo')), patch.object(c.sys, 'argv', ['helper','--path','/tmp/not-written.ini']), contextlib.redirect_stderr(output):
            self.assertEqual(1, c.main())
        self.assertNotIn('synthetic-secret', output.getvalue())
