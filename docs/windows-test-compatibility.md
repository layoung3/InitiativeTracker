# Windows Release test compatibility (0.1.6)

The release target remains Linux ARM64. Windows can build the complete solution
and execute all portable .NET tests. It cannot enable the production browser TLS
configuration, capture Linux socket provenance, or acknowledge durable secure
grants: those production facilities deliberately fail closed outside Linux.
No production source, Pi files, startup settings, certificates or networking are
changed by this correction.

## Reproduction

Baseline source is `e33a3c381aaa9f6ca12080c24a38010fa5ed2806` (PR #11).
The first commit in this PR, `68cf3fb9c486cd690c46cde7bd1aef6c5f8f474d`, adds
only the Windows workflow, leaving the tested application and fixtures unchanged.
[Hosted Windows baseline run](https://github.com/layoung3/InitiativeTracker/actions/runs/41365080659)
and [TRX artifact](https://github.com/layoung3/InitiativeTracker/actions/runs/41365080659/artifacts/13106559029):

- Release solution build: passed.
- Isolated `N7WebTests`: **0 passed, 12 failed, 0 skipped**; every failure is
  `Dispose()` deleting the SQLite database at line 310.
- Full solution: **118 passed, 139 failed, 0 skipped, 257 total**. This reproduces
  the owner's reported 139 errors as 139 failing test cases on hosted Windows.

| Failure category | Cases |
| --- | ---: |
| SQLite cleanup: N7 (12), N9 media (23), branding (3), migration (1) | 39 |
| Production Linux HTTPS: secure web (31), secure commit (13), N9 commit (29), shared integration (9), secure N9 (6), browser TLS (1), ingress profiles (1) | 90 |
| Linux socket provenance | 3 |
| Linux secure-grant durability | 7 |

## Cleanup and persistence

`TestApplicationFactory` uses production SQLite registration, pooling, migrations,
and WAL. Its async disposal awaits `WebApplicationFactory.DisposeAsync()`, which
disposes tracked clients/derived factories, stops hosted services and disposes the
host/service provider. It then clears **only the pool matching that fixture's
exact database connection string**. There is no `ClearAllPools`, forced collection,
cleanup-error suppression, retry loop, WAL disablement or production change.
Caller-owned contexts and sockets stay inside shorter `using` scopes.

Secure integration fixtures request `DeleteOnDispose()` while assertions run;
that request defers actual deletion until their context/client/derived-host scopes
have unwound and the host has stopped. Restart tests keep their storage until the
last restarted host ends. Direct migration-checkpoint connections continue using
their existing nonpooled registration.

`FixtureLifetimeTests` checks real `PRAGMA journal_mode=wal`, persisted encounter
state across three independent host restarts, continued operation of a concurrent
host with a different database, and exclusive file access after each shutdown.
On Windows, an outstanding SQLite handle makes exclusive access fail. A Linux
integration case also verifies deferred deletion with a live caller-owned context.
The existing migration, media and secure restart assertions are retained.

## Platform inventory

Every Linux requirement is attached to the individual fact/theory using
`LinuxFact`/`LinuxTheory`. Unsupported platforms report **Skipped** with a reason
in console output/TRX, rather than passing through an early return. A skipped
theory may be counted as one skipped method on Windows while all its data rows
execute separately on Linux; compare method and data-row inventory, not just
cross-platform totals.

The following methods require real production Linux HTTPS configuration and/or
Linux directory `fsync` for protected secure grants. Running them on Windows would
require bypassing the production checks or substituting fake durability, which
this PR does not do. All execute unfiltered on Linux.

| Test class | Individual Linux-only methods (data rows) |
| --- | --- |
| `BrowserIngressTests` | `InvalidOriginalSocketMetadataCannotConferTrust` (1)<br>`ProvenanceIsCapturedOnceAndMustMatchActualConnectionAndAuthority` (1)<br>`BackendProbeExceptionIsOnlyExactReadOnlyStatusAndNeverGeneralAuthority` (1)<br>`ProfilesRejectAmbiguousPublicPortsAndKeepLegacyDirectMode` (1) |
| `BrowserTlsTests` | `RuntimeCertificatePathsRejectBroadPermissionsAndLinks` (1)<br>`HttpAndSpoofedForwardedHeadersCannotUnlockSensitiveOperations` (1) |
| `FixtureLifetimeTests` | `SecureHostDefersDeletionUntilCallerScopesAndHostHaveStopped` (1) |
| `N9CommitBoundaryTests` | `CompletedRevocationOrExpiryBeforeCommitPreservesPriorState` (9)<br>`CommitAlreadyInProgressCompletesBeforeRevocationCanFinish` (6)<br>`ExpiryAfterCallbackAdmissionDoesNotReorderAnAlreadyAdmittedCommit` (3)<br>`OwnershipChangedAfterPreparationIsRejectedInsideFinalTransaction` (2)<br>`ChangedReferenceAfterPreparationCannotBeOverwrittenOrDeleted` (2)<br>`ContendedSqliteWriteTimesOutWithoutChangingPriorState` (3)<br>`ActualSqliteCommitFailurePreservesPriorReferencesAndFiles` (3)<br>`AmbiguousPostCommitFailureNeverDeletesAReferencedCandidateOrPriorFile` (1) |
| `N9SharedIntegrationTests` | `RevocationDuringActualUploadCannotCommitAfterAwait` (2)<br>`DisabledFoundationRejectsAlternateHttpsEvenWithLegacyOrSecureCookies` (2)<br>`BannersProfilesBrandingAndCsrfKeepTheirSeparatePermissions` (1)<br>`RestartPreservesMediaAndDurableSecureRevocation` (2)<br>`CaptureActualIntegratedPagesForResponsiveChecks` (1)<br>`ActualRollbackFencePreservesN9ImagesButRequiresFreshApprovalAfterRestart` (1) |
| `SecureAccessWebTests` | `LegacyReplayFailsAndFreshPhysicalDmProofProtectsRealEndpoints` (1)<br>`ProofBindsExactBrowserPurposeOriginSessionAndIsSingleUseWithSecureCookies` (1)<br>`CsrfAndOriginFailuresDoNotConsumeCodeAndExpiryAttemptsAreBounded` (1)<br>`PlayerAndDisplayRequireExplicitApprovalAndCannotMixOrEscalateRoles` (1)<br>`ConcurrentRedemptionApprovalCancellationAndRateBoundsFailClosed` (1)<br>`SessionsSurviveRestartAndRevocationDoesNotReviveAfterRestart` (1)<br>`CorruptMissingInterruptedStateAndPhysicalRecoveryNeverUseHttpAuthority` (1)<br>`LegacyIdentityReassociationRecoveryAndRevocationNeverFollowOldGrants` (1)<br>`ActualHttpLoginAndSetupPinNeverBecomeSecureAuthority` (1)<br>`ApprovalCannotMoveToAnotherBrowserOrReassociatedIdentity` (1)<br>`RevokedApproverCannotFinishPendingApprovalAndCancellationWinsRedemptionRace` (1)<br>`FailedPersistenceLeavesDurableFenceAndCannotAcknowledgeRevocationOrIssueAuthority` (1)<br>`ChallengeQueueIsBoundedAndRestartInvalidatesPendingProof` (1)<br>`LegacyRecoveryDeviceDeletionPinAndSessionChangesCannotRestoreSecureGrants` (1)<br>`LegacyPlayerMutationCannotRevokeAnotherIdentityAndLogoutCannotBeUndoneIntoAuthority` (1)<br>`LegacyAdminRoutesCannotUseCapturedCookieAsSensitiveAuthority` (8)<br>`DisplayReplacementAndDeletionInvalidateApprovedIdentityDurably` (1)<br>`SecureExpiryAndFailedIssuanceCannotRenewThroughLegacyAuthority` (1)<br>`SensitiveIdentityChangesRequireBothCsrfAndExactOriginBeforeInvalidation` (1)<br>`ActualCharacterEditorGetRequiresFreshAuthorityAtItsRoutedUrl` (1)<br>`DirectorySyncFailureNeverAcknowledgesRevocationAndRestartRequiresPhysicalRecovery` (3)<br>`ActualUpdaterRollbackFenceRejectsRestoredRevokedTicketAndPreservesGameplayData` (1) |
| `SecureCommitTests` | `RevocationOrCancellationFirstPreventsDatabaseCommit` (2)<br>`CommitFirstExcludesSuccessfulRevokeOrCancelUntilAfterDatabaseCommit` (2)<br>`ExpiryIsCheckedAtCommitEntryAndDoesNotExtendTheGrant` (1)<br>`ContendedGateTimesOutWithoutInvokingOrAbandoningTheSecondCommit` (1)<br>`CommitExceptionPropagatesAndAlwaysReleasesTheGate` (1)<br>`BindingMixedPrincipalAndAlteredExpectedGrantNeverReachCommit` (4)<br>`DisplayGrantAndCorruptStateCannotAuthorizeWrites` (1)<br>`CommitReadsPersistentGrantAndRevocationAcrossRestart` (1) |
| `SecureGrantDurabilityTests` | `SuccessfulCommitSynchronizesDirectoriesInOrderIncludingCreatedParents` (1)<br>`FailedStageReturnsUnavailableAndLeavesRestartFencedUntilRecovery` (5)<br>`UnsupportedDirectoryDurabilityDoesNotReadOrAcknowledgeSecureState` (1) |
| `SecureN9IntegrationTests` | `ActualN9BrandingAndPlayerImagesRequireFreshAuthorizationAndKeepOwnership` (1)<br>`NullAndForeignOriginsRemainDeniedWithBrowserCompatibleReferrerPolicy` (2)<br>`ActualN9DisplayKeepsPublicProjectionAndLegacyVisibilityCannotPublishArtwork` (1)<br>`ActualN9AssetOwnerCannotFollowLegacyCharacterReassociation` (1)<br>`ActualRecoveryCreatesNoSecureAuthorityAndRequiresNewBrowserApproval` (1) |

Additional portable security coverage stays active on Windows:

- Browser certificate trust, hostname/SAN/EKU/time/private-key rejection,
  sensitive-route classification and unsafe-configuration rejection.
- Linux sockaddr byte parsing (pure parsing, with fixture Linux family values),
  malformed/nonprivate destination rejection, and missing-metadata/forwarded-header
  trust rejection. Socket-dependent assertions were split out of the mixed test.
- `UnsupportedPlatformSecurityTests` verifies enabled HTTPS cannot start,
  socket capture cannot create authority, existing protected secure state cannot
  be read, normal/recovery writes cannot succeed, and directory synchronization
  rejects unsupported platforms. These three facts skip visibly **on Linux**,
  where the complete success/failure/recovery durability suite runs instead.
- `N9MediaTests.SymbolicLinkNeverServesAnArbitraryServerFile` no longer
  silently returns on Windows. A discovery-time capability probe runs it when
  symbolic-link creation is available (including hosted CI); only Win32 error
  1314 produces an explicit privilege-related skip. Other errors fail discovery.
  On an unprivileged Windows process without Developer Mode this one capability
  test is visibly skipped; the remaining media tests still execute.

## Owner rerun (Windows)

Install the .NET 10 SDK, open PowerShell at the repository root, then run:

```powershell
dotnet build InitiativeTracker.slnx -c Release
dotnet test InitiativeTracker.Tests/InitiativeTracker.Tests.csproj -c Release --filter "FullyQualifiedName~N7WebTests"
dotnet test InitiativeTracker.slnx -c Release --logger "trx;LogFileName=windows.trx" --results-directory TestResults
./scripts/Write-DotNetTestSummary.ps1 -ResultsDirectory TestResults
```

The Windows CI job runs the full solution without a filter as well as isolated N7,
records the exact source commit/runtime, uploads TRX and a JSON summary including
skipped test names/reasons, and retains `contents: read` permissions. Existing
Linux workflows are unchanged and run their full security suites.

## Corrected verification

Final exact-head Windows/Linux CI links and counters are recorded in the PR.
The first Windows run above remains the independent pre-fix evidence.
