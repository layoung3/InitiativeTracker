import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0,str(Path(__file__).parents[1]))
import certbot_adapter as a
import test_certbot_adapter as fixtures

class DebianBackendTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls): fixtures.CertbotAdapterTests.setUpClass.__func__(cls)
    @classmethod
    def tearDownClass(cls): fixtures.CertbotAdapterTests.tearDownClass.__func__(cls)
    setUp = fixtures.CertbotAdapterTests.setUp
    tearDown = fixtures.CertbotAdapterTests.tearDown
    execute = fixtures.CertbotAdapterTests.execute
    def native(self):
        value=copy.deepcopy(self.value)
        value['acme'].update(backend_profile=a.DEBIAN_PROFILE,expected_version='4.0.0',dns_plugin='cloudflare',executable='/usr/bin/certbot')
        return value
    def test_explicit_profile_prevents_version_or_plugin_family_mixing(self):
        self.assertEqual(a.DEBIAN_PROFILE,a.backend_profile(self.native()['acme']))
        for field,value in [('expected_version','5.9.0'),('dns_plugin','ovh'),('executable','/usr/local/bin/certbot')]:
            data=self.native();data['acme'][field]=value
            with self.subTest(field=field),self.assertRaises(a.f.Rejected):a.settings(data)
        legacy=copy.deepcopy(self.value);legacy['acme']['expected_version']='4.0.0'
        with self.assertRaises(a.f.Rejected):a.settings(legacy)
    def test_package_receipt_accepts_reviewed_revision_updates_not_mixed_upstreams(self):
        versions=dict(a.DEBIAN_PACKAGES);versions['certbot']='4.0.0-2+deb13u2'
        def runner(args,**kwargs):
            if args[0].endswith('dpkg-query'):
                return ''.join(f'{name}\t{version}\tall\n' for name,version in versions.items()).encode()
            return a.f.run(args,**kwargs)
        a.qualify_debian_packages(self.native(),_runner=runner)
        versions['python3-cloudflare']='3.0.0-1'
        with self.assertRaises(a.f.Rejected):a.qualify_debian_packages(self.native(),_runner=runner)
        versions['python3-cloudflare']='2.20.0-1';versions['certbot']='4.0.0-1'
        with self.assertRaises(a.f.Rejected):a.qualify_debian_packages(self.native(),_runner=runner)
    def test_qualification_never_invokes_the_group_resetting_dpkg_command(self):
        calls=[]
        def runner(args,**kwargs):
            calls.append(args)
            self.assertEqual('/usr/bin/dpkg-query',args[0])
            return ''.join(f'{name}\t{version}\tall\n' for name,version in a.DEBIAN_PACKAGES.items()).encode()
        a.qualify_debian_packages(self.native(),_runner=runner)
        self.assertEqual(1,len(calls))
    def test_reviewed_revision_order_matches_dpkg_without_relaxing_families(self):
        minimum='4.0.0-2+deb13u1'
        for version in ['4.0.0-1','4.0.0-2','4.0.0-2+deb13u1','4.0.0-2+deb13u2',
                        '4.0.0-2+deb13u10','4.0.0-3','4.0.0-10+deb13u1']:
            with self.subTest(version=version):
                expected=a.debian_revision(version,'4.0.0') >= a.debian_revision(minimum,'4.0.0')
                try:a.f.run(['/usr/bin/dpkg','--compare-versions',version,'ge',minimum]);actual=True
                except a.f.Rejected:actual=False
                self.assertEqual(expected,actual)
    def test_unreviewed_revision_syntax_and_architecture_are_rejected(self):
        for version in ['4.0.0-2~deb13u1','4.0.0-2+b1','4.0.0-2+deb12u99','4.0.0-2+deb14u1',
                        '1:4.0.0-2+deb13u1','4.0.0-02+deb13u1','4.0.0-2+deb13u01','4.0.0-2+deb13u0',
                        '4.0.0-2+deb13u1-junk','4.0.0-9999999999','5.0.0-3']:
            with self.subTest(version=version),self.assertRaises(a.f.Rejected):
                a.debian_revision(version,'4.0.0')
        def runner(args,**kwargs):
            return ''.join(f'{name}\t{version}\tarm64\n' for name,version in a.DEBIAN_PACKAGES.items()).encode()
        with self.assertRaises(a.f.Rejected):a.qualify_debian_packages(self.native(),_runner=runner)
    def test_unknown_ambient_configuration_is_rejected_in_native_profile(self):
        path=self.root/'ambient.ini';path.write_text('pre-hook = never-execute\n')
        with self.assertRaises(a.f.Rejected):a.reject_ambient_config(self.native())
    def test_stage_is_isolated_dry_run_without_export_and_cannot_become_production(self):
        value=copy.deepcopy(self.value);value['acme']['directory_url']=a.LE_STAGING
        calls=[]
        def runner(args,**kwargs):
            if args[-1]=='--version':return b'certbot 5.9.0\n'
            calls.append(args);return b''
        result=a.execute(value,'stage',apply=True,_runner=runner)
        self.assertTrue(result['staging_validated']);self.assertFalse(result['certificate_activated'])
        self.assertIn('--dry-run',calls[0]);self.assertFalse((self.root/'exports').exists());self.assertFalse((self.root/'active.json').exists())
        value['acme']['directory_url']=a.LE_PRODUCTION
        with self.assertRaises(a.f.Rejected):a.execute(value,'issue',apply=True,_runner=runner)
    def test_stage_rejects_production_endpoint_and_existing_issuer_material(self):
        with self.assertRaises(a.f.Rejected):a.arguments(self.value,'stage')
        self.execute('issue',apply=True)
        before=(self.root/'active.json').read_bytes()
        value=copy.deepcopy(self.value);value['acme']['directory_url']=a.LE_STAGING
        with self.assertRaises(a.f.Rejected):a.execute(value,'stage',apply=True)
        self.assertEqual(before,(self.root/'active.json').read_bytes())
