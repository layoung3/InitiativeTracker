#!/usr/bin/env python3
"""Actual Debian packages/parser, disposable container, network disabled at runtime."""
import copy
import hashlib
import importlib.metadata
from pathlib import Path
import subprocess
import sys
import tempfile
sys.path.insert(0,str(Path(__file__).parents[1]))
import certbot_adapter as a
from certbot._internal import cli
from certbot._internal.plugins import disco

with tempfile.TemporaryDirectory(prefix='debian4-qualification-') as directory:
    root=Path(directory);root.chmod(0o700)
    (root/'adapter.ini').write_text('')
    value={'hostname':'hub-fixture.colophon.stream','store':str(root/'browser-store'),'port':5445,
        'acme':{'backend_profile':a.DEBIAN_PROFILE,'executable':'/usr/bin/certbot','expected_version':'4.0.0',
        'state_directory':str(root),'dns_plugin':'cloudflare','credentials_file':str(root/'cloudflare.ini'),
        'propagation_seconds':60,'directory_url':a.LE_PRODUCTION,'email':'fixture@example.org',
        'scope_reviewed':True,'terms_approved':True}}
    a.qualify_debian_packages(value)
    assert subprocess.check_output(['/usr/bin/certbot','--version'],text=True).strip()=='certbot 4.0.0'
    assert importlib.metadata.version('certbot-dns-cloudflare')=='4.0.0'
    assert importlib.metadata.version('cloudflare')=='2.20.0'
    a.reject_ambient_config(value)
    plugins=disco.PluginsRegistry.find_all()
    for action in ['issue','renew','stage']:
        current=copy.deepcopy(value)
        if action=='stage':current['acme']['directory_url']=a.LE_STAGING
        parsed=cli.prepare_and_parse_args(plugins,a.arguments(current,action)[1:])
        assert parsed.authenticator=='dns-cloudflare'
        assert parsed.server==current['acme']['directory_url']
        assert parsed.max_log_backups==5
        if action=='stage':assert parsed.dry_run
    # Prove additive default-file parsing with the real installed parser.
    ambient=a.DEBIAN_VENDOR_CONFIG;before=ambient.read_bytes()
    try:
        ambient.write_text('max-log-backups = 23\n')
        args=a.arguments(value,'issue')[1:];index=args.index('--max-log-backups');del args[index:index+2]
        assert cli.prepare_and_parse_args(plugins,args).max_log_backups==23
        try:a.reject_ambient_config(value)
        except a.f.Rejected:pass
        else:raise AssertionError('Modified vendor configuration accepted')
    finally:ambient.write_bytes(before)
    a.reject_ambient_config(value)
    print('PASS: actual Debian Certbot 4 / Cloudflare 4 package, parser, staging flags and ambient-config qualification; no CA/DNS access')
