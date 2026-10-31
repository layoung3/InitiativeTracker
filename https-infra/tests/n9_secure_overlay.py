#!/usr/bin/env python3
"""Archive the exact frozen N9 tree and overlay candidate modules only in disposable storage."""
import argparse
import io
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile

N9 = '1e86dc520f9aa5e228b5639f1e31e9f7f19e0caa'
ROOT = Path(__file__).resolve().parents[2]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--evidence', type=Path)
    args = parser.parse_args()
    remote = subprocess.check_output(['git', 'remote', 'get-url', 'origin'], cwd=ROOT, text=True).strip()
    if remote not in {'https://github.com/layoung3/InitiativeTracker.git', 'https://github.com/layoung3/InitiativeTracker'}:
        raise SystemExit('Unexpected repository; no fixture created')
    if subprocess.run(['git', 'cat-file', '-e', N9 + '^{commit}'], cwd=ROOT, capture_output=True).returncode:
        subprocess.run(['git', 'fetch', '--no-tags', 'origin', N9], cwd=ROOT, check=True)
    archive = subprocess.check_output(['git', 'archive', N9], cwd=ROOT)
    with tempfile.TemporaryDirectory(prefix='it-n9-secure-overlay-') as directory:
        fixture = Path(directory)
        with tarfile.open(fileobj=io.BytesIO(archive), mode='r:') as source:
            source.extractall(fixture, filter='data')
        for relative in ['InitiativeTracker.Web/Infrastructure', 'InitiativeTracker.Web/Pages/SecureAccess']:
            shutil.copytree(ROOT / relative, fixture / relative)
        for name in ['SecureAccessWebTests.cs', 'SecureGrantDurabilityTests.cs', 'SecureCommitTests.cs']:
            shutil.copy2(ROOT / 'InitiativeTracker.Tests' / name, fixture / 'InitiativeTracker.Tests' / name)
        shutil.copy2(ROOT / 'https-infra/tests/overlay/SecureN9OverlayTests.cs.template', fixture / 'InitiativeTracker.Tests/SecureN9OverlayTests.cs')
        shutil.copy2(ROOT / 'hub-updater/runtime/initiativetracker-install-update', fixture / 'hub-updater/runtime/initiativetracker-install-update')
        program = fixture / 'InitiativeTracker.Web/Program.cs'
        text = program.read_text(encoding='utf-8-sig')
        replacements = {
            'var builder = WebApplication.CreateBuilder(args);': 'var builder = WebApplication.CreateBuilder(args);\nBrowserTls.Configure(builder);',
            'builder.Services.AddAuthorization();': 'SecureAccess.Configure(builder, dataDirectory);\nbuilder.Services.AddAuthorization();',
            '    app.UseHsts();': '    if (!app.Services.GetRequiredService<BrowserTlsSettings>().Enabled) app.UseHsts();',
            'app.UseRouting();': 'app.Use(BrowserTls.Guard);\napp.UseRouting();',
            'app.UseAuthentication();': 'app.UseAuthentication();\napp.Use(SecureAccess.Guard);'
        }
        for before, after in replacements.items():
            if text.count(before) != 1:
                raise SystemExit('Frozen fixture contract changed')
            text = text.replace(before, after)
        program.write_text('using InitiativeTracker.Web.Infrastructure;\n' + text)
        evidence = args.evidence.resolve() if args.evidence else fixture / 'evidence'
        evidence.mkdir(parents=True, exist_ok=True)
        subprocess.run([args.dotnet, 'test', '-c', 'Release', '--logger', 'trx;LogFileName=n9-secure-overlay.trx',
                        '--results-directory', str(evidence)], cwd=fixture, check=True)
        print('PASS: actual frozen N9 Razor/media/ownership handlers with candidate secure auth; disposable overlay only; source ' + N9)

if __name__ == '__main__':
    main()
