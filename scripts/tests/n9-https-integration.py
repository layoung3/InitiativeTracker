#!/usr/bin/env python3
"""Test pinned shared HTTPS and N9 source together without merging either branch."""
import argparse
import io
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile

ROOT = Path(__file__).resolve().parents[2]
SHARED = '5b3e0f66b3e9a7ea6a6b2e2bdea6f0a222bb287e'
N9 = '1e86dc520f9aa5e228b5639f1e31e9f7f19e0caa'


def archive(ref, destination):
    data = subprocess.check_output(['git', 'archive', ref], cwd=ROOT)
    with tarfile.open(fileobj=io.BytesIO(data)) as source:
        source.extractall(destination, filter='data')


def prepare(destination, entry_only=False):
    archive(N9, destination)
    with tempfile.TemporaryDirectory(prefix='n9-shared-source-') as directory:
        shared = Path(directory)
        archive(SHARED, shared)
        for relative in ['InitiativeTracker.Web/Infrastructure', 'InitiativeTracker.Web/Pages/SecureAccess', 'https-infra']:
            shutil.copytree(shared / relative, destination / relative)
        # Fixture only: shared installer bytes, N9 launcher bytes, per-component hash composition.
        installer = 'hub-updater/runtime/initiativetracker-install-update'
        shutil.copy2(shared / installer, destination / installer)
        manifest_path = destination / 'hub-updater/runtime-manifest.json'
        manifest = json.loads(manifest_path.read_text())
        shared_manifest = json.loads((shared / 'hub-updater/runtime-manifest.json').read_text())
        manifest['componentSet'] = shared_manifest['componentSet']
        manifest['sha256']['initiativetracker-install-update'] = shared_manifest['sha256']['initiativetracker-install-update']
        for name, digest in manifest['sha256'].items():
            if hashlib.sha256((destination / 'hub-updater/runtime' / name).read_bytes()).hexdigest() != digest:
                raise SystemExit('Composite runtime manifest mismatch: ' + name)
        manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
        for name in ['BrowserTlsTests.cs', 'SecureAccessWebTests.cs', 'SecureGrantDurabilityTests.cs']:
            shutil.copy2(shared / 'InitiativeTracker.Tests' / name, destination / 'InitiativeTracker.Tests' / name)
        shutil.copy2(shared / 'https-infra/tests/overlay/SecureN9OverlayTests.cs.template', destination / 'InitiativeTracker.Tests/SecureN9OverlayTests.cs')
    # The entry-only mode reproduces the existing PR #6 frozen overlay boundary.
    program = destination / 'InitiativeTracker.Web/Program.cs'
    source = program.read_text(encoding='utf-8-sig')
    replacements = {
        'var builder = WebApplication.CreateBuilder(args);': 'var builder = WebApplication.CreateBuilder(args);\nBrowserTls.Configure(builder);',
        'builder.Services.AddAuthorization();': 'SecureAccess.Configure(builder, dataDirectory);\nbuilder.Services.AddAuthorization();',
        '    app.UseHsts();': '    if (!app.Services.GetRequiredService<BrowserTlsSettings>().Enabled) app.UseHsts();',
        'app.UseRouting();': 'app.Use(BrowserTls.Guard);\napp.UseRouting();',
        'app.UseAuthentication();': 'app.UseAuthentication();\napp.Use(SecureAccess.Guard);'
    }
    for before, after in replacements.items():
        if source.count(before) != 1:
            raise SystemExit('Program wiring contract changed: ' + before)
        source = source.replace(before, after)
    source = 'using InitiativeTracker.Web.Infrastructure;\n' + source
    source = source.replace('app.Run();', '''app.MapGet("/system/https-status", (HttpContext context, BrowserCertificateStore certificates) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Json(new { ready = certificates.TrustedRequest(context), generation = certificates.Generation, expiresAtUtc = certificates.ExpiresAtUtc });
}).AllowAnonymous();
app.Run();''')
    program.write_text(source)
    setup = destination / 'InitiativeTracker.Web/Pages/Setup/Index.cshtml'
    setup.write_text(setup.read_text(encoding='utf-8-sig') + '''
@inject InitiativeTracker.Web.Infrastructure.ITrustedBrowserOrigin SecureOrigin
@if (SecureOrigin.Enabled) {
    <section class="card p-3 my-3"><h2>Secure authorization</h2>
    <p>Open the trusted HTTPS authorization screen on this hub. Codes are displayed only there, after local loopback resolution is configured.</p>
    <a class="btn btn-primary" href="@(SecureOrigin.Origin + "/secure-access/physical")">Open secure physical screen</a></section>
}
''')
    if not entry_only:
        for name in ['n9-shared-https.patch', 'n9-shared-updater-fixtures.patch', 'n9-shared-installer-ci.patch']:
            patch = ROOT / 'integration' / name
            subprocess.run(['git', 'apply', '--check', str(patch)], cwd=destination, check=True)
            subprocess.run(['git', 'apply', str(patch)], cwd=destination, check=True)
            print(name + ' SHA256: ' + hashlib.sha256(patch.read_bytes()).hexdigest(), flush=True)
    shutil.copy2(ROOT / 'scripts/tests/N9SharedIntegrationTests.cs.template', destination / 'InitiativeTracker.Tests/N9SharedIntegrationTests.cs')
    print(f'Composite sources: N9 {N9}; shared HTTPS {SHARED}; reviewed adapter applied: {not entry_only}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--fixture', type=Path, help='Keep an empty disposable source fixture for inspection/browser tests')
    parser.add_argument('--prepare-only', action='store_true')
    parser.add_argument('--entry-only', action='store_true', help='Negative control: original route-entry-only overlay')
    parser.add_argument('--evidence', type=Path)
    args = parser.parse_args()
    remote = subprocess.check_output(['git', 'remote', 'get-url', 'origin'], cwd=ROOT, text=True).strip()
    if remote not in {'https://github.com/layoung3/InitiativeTracker.git', 'https://github.com/layoung3/InitiativeTracker'}:
        raise SystemExit('Unexpected repository')
    for ref in (SHARED, N9):
        if subprocess.run(['git', 'cat-file', '-e', ref + '^{commit}'], cwd=ROOT, capture_output=True).returncode:
            subprocess.run(['git', 'fetch', '--no-tags', 'origin', ref], cwd=ROOT, check=True)
    with tempfile.TemporaryDirectory(prefix='n9-https-composite-') as temporary:
        fixture = args.fixture.resolve() if args.fixture else Path(temporary)
        if fixture == ROOT or ROOT in fixture.parents or (fixture.exists() and any(fixture.iterdir())):
            raise SystemExit('Fixture must be empty and outside the source checkout')
        fixture.mkdir(parents=True, exist_ok=True)
        prepare(fixture, args.entry_only)
        if not args.prepare_only:
            evidence = args.evidence.resolve() if args.evidence else fixture / 'TestResults'
            command = [args.dotnet, 'test', '-c', 'Release', '--logger', 'trx;LogFileName=n9-shared-integration.trx', '--results-directory', str(evidence)]
            if args.entry_only:
                command += ['--filter', 'FullyQualifiedName~N9SharedIntegrationTests.RevocationDuringActualUpload']
            subprocess.run(command, cwd=fixture, check=True, timeout=600)


if __name__ == '__main__':
    main()
