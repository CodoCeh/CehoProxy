# Panel and reliability review

This change is a **draft review**, not a release or a claim of complete VPN protection.
It builds on version 1.2.149. Network/firewall/DNS settings were not changed during development.

## User-visible scope

- One ordinary Simple/Pro switch, selected CehoProxy flat-A product identity, separate company brand, and original application-icon extraction, light/dark responsive cards.
- Tunnel connection and observed app routing are separate facts. Idle, unknown, stale, unapplied and direct-exception states cannot silently become verified protection.
- One startup card with confirmed Prepare / Connect / Check milestones, genuine current phase, elapsed time, indeterminate work and explicit failures. No invented progress percentages.
- Repeated identical power commands join one operation; conflicting commands are explicitly rejected. Job identities survive browser history without colliding across service restarts.
- Refresh timeouts include the response body. Lost connection, stale data and interrupted observation are distinct from cancellation. Dirty inputs, focus and open details are retained; deferred sections are labelled stale.
- First-run verification, actionable per-app Doctor checks, configured-vs-observed route details, Windows persistent-guard explanations and route-change consequences.
- Local report preview/download with section selection; links, secrets, paths and free-form logs are not copied into support output.
- Private verified-config checkpoints and explicit guarded recovery, separate from encrypted export and updater rollback. Insecure TLS settings remain advanced and visibly marked.
- Single controller per data directory; finite recovery with delays of 5/15/30/60/120 seconds. Short successful launches do not renew a crash-loop budget; a five-minute stable period can renew it. Stop/new-session generations invalidate old watcher decisions.
- Validated atomic config/runtime writes. Runtime checks do not certify every app/service, and config checkpoint readiness is not an anonymity guarantee.

## Safe fault evidence

| Fault | Expected bounded outcome | Evidence |
| --- | --- | --- |
| Simultaneous starts / conflicting stop | One admitted identical operation; conflicting intent rejected; later retry can run | `JobsTests`, `StartupPipelineTests` |
| Slow/stalled headers or body | Responsive elapsed display; timeout means unknown result, never cancellation or fake success | `panel-client.test.mjs`, `browser-check.mjs` |
| Stale result / wall-clock rollback / old service | Job identity + revision gate; monotonic confirmed milestone | `JobTelemetryTests`, `StartupMilestoneTests`, client/browser checks |
| Repeated process failure | Increasing delays, finite attempt budget, terminal reason/action | `RecoveryPolicyTests` |
| Stop or newer start while observer waits | Old generation cannot restart a newer or stopped session | `RecoveryPolicyTests`, `StartupPipelineTests` |
| Second controller launch | Reuses/refuses existing ownership instead of overlapping controller cleanup | `ControllerLeaseTests`, source integration checks |
| Foreign engine with similar name/path | Exact executable and config ownership required before selecting process | `OwnedEngineProcessTests` (synthetic observations only) |
| Partial settings/checkpoint write | Complete old-or-new file; no truncated published settings | `PrivateFileTests`, `VerifiedConfigStoreTests` |
| Restore failure between file replacements | Earlier replacements undone, bytes and timestamps restored | `VerifiedConfigStoreTests` |
| Mode edit during restore | No concurrent whole-config overwrite; retry uses restored routing | `RestoreAdmissionTests` |
| Missing/expired/idle app evidence | No green completion of wizard from tunnel IP alone | `AppObservationTests`, `VerifiedPanelRenderTests` |
| Uninstall product cache cleanup | New secret-bearing checkpoint removed; unrelated fixture file retained | `FullUninstallCleanupTests` (temporary fixtures only) |

## CI scope and exclusions

`panel-ui.yml` builds the full solution, runs the audited non-mutating allowlist on hosted Linux/Windows/macOS,
and runs actual Chromium fixture/browser checks with RU/EN desktop/mobile light/dark screenshots.
The exact test list is `tools/panel-qa/safe-tests.filter`; `tools/panel-qa/README.md` records exclusions.
The existing broad build job is explicitly skipped **only on the named review branch**, with a visible scope notice.
Main/release workflow behavior is unchanged. A green safe-review job is not a full-suite or merge-readiness claim.

Locally, 53 test cases requiring network-interface enumeration were blocked by the container. The hosted safe
allowlist includes those read-only cases. Live VPN, firewall/routing repair, real sleep/WiFi changes, installers,
and device-specific behavior are not covered by these fixture tests and need separately authorized validation.
The browser fixtures use clearly labelled synthetic data; missing installed application icons use the normal fallback.

## Remaining boundaries

- Safe cancellation of an already-running engine phase is not implemented. Closing the page only stops observation.
- Atomic file replacement and injected exceptions do not prove power-loss durability or crash-atomic replacement of a whole set of files.
- Exact owned-process matching does not establish exclusive ownership of every legacy Linux route-table or Windows adapter identifier. Existing platform cleanup still requires real-device review before a release.
- Source-order tests prove integration structure, not successful privileged runtime transitions.
- The controller lock file is intentionally not unlinked while held; an empty nonsensitive lock file may remain after shutdown.
- Review the actual CI result and screenshots for the exact PR head, not an earlier artifact.

## Selected product identity (local change)

The user selected flat-A (nested tunnel contour plus application window) for CehoProxy and its tray.
Header/login/favicons and platform product resources use the new product identity; `Brand.cs` and the CodoCeh footer company mark remain unchanged.
Windows/Linux state icons share tested pixel composition. macOS uses template resources tinted by AppKit; Swift bundle compilation is prepared for the safe Mac CI job, but local Swift/native-tray runtime checks are unavailable.

Pending UX design, not implemented: tunnel/drop add interaction and existing-card highlight. Current add handlers are serialized and reject matching detected rule paths before saving/applying. Existing case-insensitive path checks can conflate distinct case-sensitive Linux paths; canonical platform-aware identity remains a separately scoped design requirement. No drag/drop or new tunnel animation was implemented here.
