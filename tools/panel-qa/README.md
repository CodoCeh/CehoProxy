# Safe panel UI checks

These checks render the **real C# `RenderPage` HTML/CSS and run its real client scripts** with synthetic observations, an in-memory `Job`, a controlled clock and intercepted requests. They never start sing-box, a VPN, a web listener, or a firewall/DNS change. All depicted addresses, operations and app observations are test data. Every screenshot is labeled as a fixture.

## Run

From the repository root:

1. `dotnet build ProxyCage.sln -c Release`
2. `dotnet test tests/ProxyCage.Core.Tests/ProxyCage.Core.Tests.csproj -c Release --no-build --filter "$(cat tools/panel-qa/safe-tests.filter)"`
3. `CEHO_RENDER_DIR=/tmp/cehoproxy-fixtures dotnet test tests/ProxyCage.Core.Tests/ProxyCage.Core.Tests.csproj -c Release --no-build --filter FullyQualifiedName~Fixture_pages_can_be_exported_for_visual_review`
4. `npm ci --prefix tools/panel-qa --ignore-scripts`
5. `npm test --prefix tools/panel-qa`
6. In `tools/panel-qa`: `./node_modules/.bin/playwright install --with-deps chromium`, then `node browser-check.mjs` and `node inspect.mjs`.

The safe filter is an explicit audited class allowlist, not the full suite. New test classes are excluded unless reviewed and added. The original audit excluded real cleanup/repair, listener-based tests and external-network tests; some included tests with cleanup-like names only inspect pure command construction, fixture files or injected collaborators. The existing build workflow has its own broader platform and permission requirements. Adding a live operation to an allowed class requires revisiting the allowlist.

Playwright 1.55.1 and jsdom 26.1.0 are exact dependencies with lockfile integrity hashes. CI invokes the installed Playwright binary, not a transient `npx` package, and pins GitHub actions to verified commit SHAs. The optional `CHROMIUM_PATH` selects a preinstalled browser. `CEHO_RENDER_DIR`, `CEHO_ARTIFACTS` and `CEHO_SCREENSHOTS` select fixture/artifact directories.

## Evidence and limits

- DOM tests: 85 deterministic time/network cases (27 operation/refresh cases and 58 tunnel cases), with no browser-rendering claim. They include filename-only external-drop hints, duplicate/coverage focus, unknown save recovery, pending-versus-applied state, stale replies, failed secondary adds, navigation, reduced motion and explicit Apply consent.
- Browser tests: 54 RU/EN cases cover native form serialization and repeated clicks, dirty dialog Cancel/Escape/Save and delete confirmation, job errors/offline/invalid JSON/wrong IDs, streamed-body timeout/abort/retry, stale replies, reload, Back/Forward, sleep/resume, pagehide cancellation, state refresh, preserved focus and unsaved inputs. Faults never submit to a live server.
- Screenshots: 136 cases across RU/EN, 390px mobile/1280px desktop, light/dark, and 17 screens: state, idle, apps, wizard, subscriptions, settings (expanded), doctor, help, leak, startup progress, delayed startup, startup error, and five tunnel states (ready, confirmation, pending, applied and observed). Job HTML and JSON come from C# fixture jobs with explicit startup steps and monotonic revisions; timing never requires a real startup. App-icon requests use the production letter fallback because no user's installed icons are loaded.
- All HTTP(S) requests are intercepted, service workers are blocked, and unexpected origins/methods/paths fail the case. The hung-body fault is explicitly simulated with a browser `ReadableStream` because route fulfillment always supplies a whole body.
- `browser-results.json` and `screenshot-results.json` include commit identity, fixture SHA-256 hashes and each result. Failed cases retain browser PNGs and traces. Other cases continue after a failure; the process exits unsuccessfully if any case failed.
- The screenshot step runs even if a browser case fails, provided fixture generation/browser installation succeeded. CI uploads original HTML/JSON, screenshots, traces, .NET TRX and client XML evidence for the exact checkout.

Review the actual PNGs and reports for the exact commit before claiming visual/browser acceptance. Missing browser execution, a missing fixture or a launch failure is not a pass. These tests establish panel behavior under synthetic faults; they do not establish successful live VPN routing, platform firewall behavior or real server readiness.

## Hosted platform scope

The same safe allowlist runs on Ubuntu, macOS and Windows after a full solution build. Hosted runners also cover the read-only interface-enumeration cases that could not execute in the restricted local workspace. Windows retains the existing workflow exclusions for `PlatformTests.Tun_options_match_the_running_system` and `AppIconTests.Linux_icon_name_resolves_inside_the_theme_folders`. `DoctorTests` is not allowlisted. No smoke/setup, installation, real cleanup/repair or live VPN test is added. A safe-platform pass is intentionally not a claim of complete platform integration coverage.

New safe test classes include app identity/admission, admitted-configuration snapshots, CLI admission and desktop tunnel rendering. Their engine and apply callbacks are injected; no real tunnel or firewall is started. Automatic recovery and background guard reconciliation must use the admitted runtime snapshot rather than a pending saved draft.

Screenshot evidence is split by language and viewport into four artifacts; reports, fixture HTML/JSON and failure traces remain in a separate evidence artifact. This keeps supported file transfers below the per-file materialization limit without changing which cases run.
