# Notes for Claude Code sessions on Jelly Portal

Context that has to survive between sessions. The original brief (a long French prompt) is **not**
available to later sessions; everything binding in it is here. Repository content is written in
**English**; the owner chats in **French**; the plugin's user interface is in **French**.

## Read this part even if you read nothing else

1. **The owner's production Jellyfin is never touched.** It runs natively on Windows on another
   machine. Work only on this dev VM (a Dockerised Jellyfin, see *Dev loop*). Never ask for, store
   or write its address or credentials anywhere, and never put LAN addresses, tokens or API keys
   in this public repository.
2. **The plugin GUID never changes:** `a72a6bca-0bfe-480c-946f-e0a3fcb2c746`. Jellyfin identifies a
   plugin by it; change it and every version becomes a different plugin. It lives in
   `PluginIdentity.Id`, `build.yaml` and this file, and a test keeps all three equal.
3. **You may create and push `-rc.N` tags. You never create a stable `vX.Y.Z` tag.** The owner
   promotes. A published tag is never deleted or moved: if an RC is broken, the fix is the next RC.
   GitHub now enforces this (see *Git workflow and branch protection*), so a burnt RC number stays burnt.
4. **Everything goes through a branch (`feat/...`, `fix/...`) and a pull request.** `main` is
   protected, with no bypass for anyone, admins included. You may merge your own PRs once CI is green.
5. **No version number is written anywhere in the code.** It comes from the git tag and is injected
   at build time (see *Releases*).
6. **Say what you actually verified.** Passing unit tests proved little in the sibling projects;
   running the real thing (Dockerised Jellyfin, real browser) is what finds bugs. State what was and
   was not exercised.
7. **Commits:** Conventional Commits in English (`feat:`, `fix:`, `perf:`, `chore:`, `ci:`, `docs:`,
   `refactor:`, `test:`), small, one logical change each. End each with
   `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. **Use the identity already in git
   config (a GitHub `noreply` address); never pass `-c user.email=...` or the owner's real address**
   - this is a public repo and that mistake was made (and caught before pushing) on day one.

## What this is

A Jellyfin plugin that puts a permanent icon in the header of the Jellyfin web client, next to the
existing header icons. It reflects the overall state of the owner's services as reported by their
**Status Portal** (a separate Flask/SQLite project, `Adam4125-officiel/Status-Portal`, normally cloned
as a sibling directory). Clicking the icon opens a popover with the details.

Only clients built on jellyfin-web see it (browser, Jellyfin Desktop, the Android app). Android TV,
Swiftfin, Findroid and so on do not, and that is accepted. No push notifications, no websocket:
polling is enough.

## Technical target (not negotiable)

- **Jellyfin 12.2**, .NET 10 (`net10.0`). The owner first said 12.0, then on 2026-10-13 said
  "you can do it on 12.2": 12.2 is the target. `Jellyfin.Controller` and `Jellyfin.Model` are
  `12.2.0` (they exist on NuGet: 12.0.0, 12.1.0, 12.2.0), `targetAbi` is `12.2.0.0`, the dev server
  is `jellyfin/jellyfin:12.2`. A test ties `targetAbi` to the package versions.
- Jellyfin plugins are strictly one-version compatible: a 10.11 plugin does not load on 12.x, and a
  plugin with `targetAbi` 12.2.0.0 is not offered to a 12.0 or 12.1 server (the installer hides it
  when the server version is lower than `targetAbi`).
- **Client code is injected with File Transformation (IAmParadox27), 3.0.x.** It publishes a
  `Release-12.2.0.zip` (3.0.2.0, 2026-10-12) in its own manifest
  `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`. Its README says it cannot be
  referenced as a library (plugins load into different load contexts), so registration is by
  **reflection**: find the assembly whose name contains `.FileTransformation`, get type
  `Jellyfin.Plugin.FileTransformation.PluginInterface`, call static `RegisterTransformation` with a
  payload (`id`, `fileNamePattern`, then either `callbackAssembly`/`callbackClass`/`callbackMethod`
  or a `transformationEndpoint`). The callback receives `{ "contents": "..." }` and returns the new
  file contents. `jellyfin-plugin-home-sections` (in `refs/`) is a working example on 12.x. Mind that
  the payload type crosses load contexts: do not assume a `JObject` built in this plugin's context
  is the same type File Transformation expects; check the example and test it for real.
  **If File Transformation is absent the plugin must still start and log a clear warning; it must
  never crash Jellyfin.**
- **Never modify jellyfin-web's files directly.**
- Before using any Jellyfin API, confirm it exists in `refs/jellyfin` at tag `v12.2`. Jellyfin's
  stated policy is that an endpoint absent from its OpenAPI spec must not be used by clients.
- Facts read in Jellyfin's source (`Emby.Server.Implementations/Updates/InstallationManager.cs`,
  `Plugins/PluginManager.cs`): the installer accepts **only `.zip`** from `sourceUrl`, verifies an
  **MD5** of the downloaded bytes (hex, case-insensitive), extracts to the plugin directory, and
  reads an optional `meta.json` from the zip (package info from the manifest wins on `version`).
  A plugin is enabled only if `server version >= targetAbi`.

### References (`./refs/`, gitignored, read-only)

`jellyfin/jellyfin` and `jellyfin/jellyfin-web` at **v12.2**, `jellyfin-plugin-template` (still
10.11.5 / net9.0, so use it only for layout ideas), `jellyfin-plugin-file-transformation` (3.0.2.0),
`jellyfin-plugin-home-sections`. Re-create with `git clone --depth 1 --branch v12.2 ...` if missing.
For the header, read the **real DOM in `refs/jellyfin-web`**, for the **Modern** layout (default in
12.x) **and** the **Legacy** one. Do not guess selectors.

## Client behaviour (specified by the owner)

JavaScript only: vanilla, no framework, no CDN, every log prefixed `[JellyPortal]`, **zero console
errors in normal cases**. UI text is French.

**Icon**: inserted in the header next to the existing icons. jellyfin-web is an SPA that re-renders
the header, so use a `MutationObserver`; never create a duplicate; it must survive navigation, in
Modern and Legacy layouts.

**States, one shown at a time, by decreasing priority (spec revised by the owner 2026-10-13):**

| # | State | Meaning | Look |
|---|---|---|---|
| 1 | Down | at least one service is down | red |
| 2 | Maintenance in progress | a maintenance is under way | **violet**, gentle pulse (it must catch the eye) |
| 3 | Degraded | a service is degraded | orange |
| 4 | Maintenance soon | a maintenance is planned inside the configured window | normal icon + **clock badge** |
| 5 | OK | all is well | discreet, same colour as the other header icons |
| 6 | Unknown | the portal is unreachable | grey, **never red** |

**Orange is reserved for Degraded.** A planned maintenance is not an outage, and it has to look
different from one. The clock badge is shown only if the maintenance touches a priority service or
falls inside the window. The internal state names are `down`, `maintenance_ongoing`, `degraded`,
`maintenance_soon`, `ok`, `unknown`.

**The panel is a bubble (popover), not a page.** Binding, repeated by the owner:

- Anchored under the icon, right-aligned, with a small arrow pointing at the icon. Same principle
  as the "active streams" panel other plugins already put in the header.
- Opens **over the current page**: no navigation, **the URL does not change**. It is not a page,
  not a side drawer, not a new route.
- Width about 360 px, max height about 70 % of the screen, internal scrolling when needed.
- Closes on: click on the icon, click outside, Escape. **Only one bubble open at a time.**
- Mobile: still a bubble, width = screen minus 16 px of margin, **never full screen**.
- No clash with the header's other popovers: opening one closes the other if that can be done
  cleanly; otherwise at the very least no broken overlap.
- **The only real page of this plugin is its configuration in the admin dashboard.**
- Content, in this order: services with a state dot (worst first, as the portal sends them); **open
  incidents ("Incidents en cours") right after the services that are down** (title, status
  investigating / identified / monitoring, since when, services concerned); maintenance in progress
  (time remaining); upcoming maintenance (readable countdown such as "dans 3 h"), priority ones first
  and highlighted; active announcements; an "Ouvrir le status portal" link.
- **Times are shown in the browser's local time zone, in French**: "aujourd'hui à 22:00",
  "demain à 03:00", "dans 3 h", "se termine dans 45 min". The JSON stays UTC with a `Z`; the
  conversion is client-side, after correcting the browser's clock skew with `SummaryDto.now`. A test
  covers it across both daylight-saving changes (the formatter takes the time zone as a parameter so it
  can be tested in any zone).
- Style consistent with Jellyfin's dark theme, using the 12.x theme CSS variables where they exist.

**Polling** uses the configured interval, **pauses while the tab is hidden** (`visibilitychange`)
and refreshes immediately when the tab returns.

## Server behaviour (specified by the owner, refined 2026-10-13)

1. **Admin config page** (dashboard): status portal URL; **the portal's display-device API key
   (required)**, stored server-side and never sent to a browser; client polling interval (default
   60 s); server cache TTL (default 30 s); "maintenance coming up" window (default 24 h, 1 h to 30
   days); priority services (by name, one per line, case-insensitive); visibility (all users / admins
   only). Without a URL or key the state is `unknown` with `reason: not_configured`, and the config
   page says what is wrong in words.
2. `GET /JellyPortal/Summary`, `[Authorize]` (any signed-in Jellyfin user; `403` for a non-admin when
   visibility is "admins only", and the client then does nothing). Queries the portal **server-side**,
   caches the *raw reading* for the TTL (N browsers = one request to the portal; a failure is cached
   for the same time so a dead portal costs one timeout per TTL, not one per browser) and **recomputes
   the state on every request** against the portal's clock, so a window that starts between two
   readings turns "soon" into "in progress" on time. Returns `SummaryDto` (`Summary/SummaryDto.cs`:
   every property has an explicit JSON name, because the host's JSON options are PascalCase and could
   change). It never returns a 5xx for a portal problem.
3. An endpoint that serves the client JS/CSS (`[AllowAnonymous]`: the script has to load before
   sign-in, and it carries no data), registered with File Transformation for injection into
   `index.html`.
4. Portal unreachable, slow, refusing or answering nonsense -> state `unknown` with a `reason` code,
   never an exception and never red. Codes: `not_configured`, `device_api_disabled` (portal answered
   404), `bad_api_key` (401), `unreachable`, `timeout`, `bad_response`, `portal_outdated` (answered
   without `"detail":"full"`: a portal before 1.12.0). Only the code leaves the server; the exception
   text, which can name internal addresses, stays in the log.

### Data source: the portal's display-device API, with `detail=full`

The plugin reads `GET <portal>/api/device/summary` with the key in the **`X-Api-Key` header only**,
asking for **exactly** `?sections=services,incidents,maintenance,announcements&services=all&detail=full`.
**Never** `resources`, `vms` or `jellyfin=1`: that API is not subject to the portal's public-visibility
switches, and this plugin shows its data to every Jellyfin user, so those would expose the inside of
the server. `detail=full` (portal 1.12.0, `device_api.py`) lifts the screen-sized limits and adds the
`service_names` arrays and the maintenance `description`; the contract is the portal's, and
`DevicePayloadParser` reads exactly it. The parser is strict about shape and tolerant of extras.

Decisions made with the owner, which the code and tests encode (change them only on his word):

- **The plugin computes its own state**, with the priority of the table above. It does not use the
  portal's `overall`: the portal ranks degraded above maintenance, this plugin does not.
- **An open incident counts as at least `degraded`**, even with no service marked down (a hand-made
  incident must show). A `down` service still wins, and so does a maintenance in progress, by the table.
- **In progress** = the portal's `state == "active"` **or** the clock is between `starts` and `ends`
  (the portal's flag only flips on its next check cycle). A window past its end but still flagged
  active is still in progress. A service set to `maintenance` by hand with no window counts too, with no
  time left to show.
- **Clock:** the portal's `now` is the reference. The estimate is `now + time elapsed here since the
  reading`; this machine's absolute clock is never compared with a portal time. `SummaryDto.now` carries
  it so the browser can correct its own skew.
- **Services the admin excluded from the portal's overall status** are absent from its services list
  but not from its windows and incidents. A window or incident naming only services missing from a
  *complete* list is shown in the panel but does not change the icon; if the list was cut (`total` above
  the items sent) nothing is inferred.
- **Priority services** (by name) only put a maintenance or incident forward in the panel (first,
  highlighted). The badge follows the configured window for every service: priority never widens it.
- `slow` and unrecognised statuses are OK for the icon (neutral dot in the panel). Announcements never
  change the icon. A manual maintenance, a window and an incident are all shown in the panel.
- Announcement and maintenance text is plain text for the browser (`textContent`), never HTML.

The global state and the panel content come from **pure code with no Jellyfin dependency**
(`Jellyfin.Plugin.JellyPortal/Summary/`), unit-tested in `Jellyfin.Plugin.JellyPortal.Tests`, which
include the 64 combinations of conditions against the table; a convention test keeps that folder free
of Jellyfin/ASP.NET references.

## The status portal side (a different repository)

The portal is `Adam4125-officiel/Status-Portal` (Flask + SQLite; a sibling clone exists on the dev VM).
Work on it happens in **its** repository, on a branch, with a draft PR that **the owner reviews and
merges himself**; nothing is merged there without his word. It has its own strict `CLAUDE.md`
(branch + PR, version bump, convention tests, run the real server): follow it there.

- The owner decided the data source is its machine-to-machine **display-device API**
  (`/api/device/summary`, keyed, versioned, `now`, UTC with `Z`, open incidents only, excluded services
  already left out), not the public `/api/status`.
- Its limits were cut for an ESP8266 display, so the portal got an **opt-in `detail=full`** parameter
  (portal 1.12.0): without it the answer is byte-for-byte what 1.11.1 sent (pinned by a golden test
  there), with it the limits are generous but bounded, services of incidents and windows come as name
  arrays and windows carry their description. The answer says `"detail":"full"` so a plugin can tell a
  portal that honoured it from an older one.
- The fixtures in `Jellyfin.Plugin.JellyPortal.Tests/Fixtures/` are real answers produced by the
  portal's own code; the fake portal in `dev/` must keep honouring exactly that contract.

## Releases

Tags: `vX.Y.Z-rc.N` (pre-release, **N from 1 to 99**) or `vX.Y.Z` (stable). Anything else fails the
workflow. Regex `^v\d+\.\d+\.\d+(-rc\.\d+)?$`, plus: `rc.0` and `rc.100` or more are refused.

| Tag | Jellyfin version |
|---|---|
| `vX.Y.Z-rc.N` | `X.Y.Z.N` |
| `vX.Y.Z` | `X.Y.Z.100` |

**The stable release is the highest number of its series, on purpose** (owner's decision,
2026-10-13; the first mapping, stable = `.0`, had it *below* its own RCs, so a Jellyfin that had
installed `rc.2` was never offered the stable). `.100` is above every possible RC (`.1`..`.99`) and
below the next series' first candidate. Tests pin all of this; `STABLE_BUILD` in
`tools/release_info.py` is the one definition.

Jellyfin versions are four numbers with no suffix. Semver meaning: `Z` fix, `Y` feature, `X`
breaking (a new major Jellyfin, an incompatible config change).

- A **stable** tag is only valid on a commit that already carries a `vX.Y.Z-rc.N` tag of the same
  version (`tools/release_info.py --check-rc-on`).
- `.github/workflows/release.yml` (any `v*` tag, one at a time via `concurrency`): validate the tag
  -> build with `-p:Version=...` -> tests (nothing is published if any fails) -> changelog from
  conventional commits -> zip + MD5 -> GitHub Release (pre-release for an RC) -> `manifest` branch.
  The manifest job downloads the zip from its **public URL** and checksums that, so the manifest
  always matches the bytes Jellyfin will fetch. It is a separate job: "Re-run failed jobs" repairs
  a failed manifest update without rebuilding.
- **Manifest branch** (orphan `manifest`, written only by the workflow): `manifest.json` (stable
  releases) and `manifest-unstable.json` (RCs **and** stables). RC -> unstable only; stable -> both.
  10 highest versions kept per manifest. Each entry: `version`, `changelog`, `targetAbi`,
  `sourceUrl`, `checksum` (MD5), `timestamp`. URLs (in the README):
  `https://raw.githubusercontent.com/Adam4125-officiel/Jelly-portal/manifest/manifest.json` and
  `.../manifest/manifest-unstable.json`.
- `build.yaml` holds the Jellyfin packaging metadata; its `version` is the placeholder `0.0.0.0`.
  `Directory.Build.props` defaults `Version` to `0.0.0.0` for local builds only.
  `tools/package_plugin.py` **refuses a DLL that does not contain the version it is told it has**
  (and a version whose last part is outside 1..100).
- `tools/package_plugin.py` also checks the version string against the **wide, NUL-terminated**
  resource inside the DLL with both ends anchored: a plain substring test would accept `0.1.0.1` in a
  `0.1.0.100` build. Tested against real DLLs, not only fakes.
- GitHub Actions run steps under `bash -e` **without** `pipefail` unless `shell: bash` is set; the
  workflows set it as a default. Keep it, or `cmd | tee` will mask failures. Pass values to scripts
  through `env:`, never inline `${{ }}` inside `run:` (a tag name is attacker-shaped input).

The owner's steps (also in the README): merge PRs on `main`; `git tag vX.Y.Z-rc.1 && git push origin
vX.Y.Z-rc.1`; test on production through the unstable repository; on a bug, fix and tag `rc.2`;
when an RC is validated, the owner tags `vX.Y.Z` on that RC's commit.

## Git workflow and branch protection

Enabled on 2026-10-13 at the owner's request, as two GitHub **rulesets with no bypass actors**
(admins are not exempt; editing a ruleset is the only way around them, and that is the owner's call):

- **`Protect main`**: a pull request is required (**no reviews required**: the owner works alone);
  the checks **`Build and test`**, `Client JS syntax` and `Release tooling` must pass; no force-push
  (non-fast-forward) and no deletion. The branch does not have to be up to date with `main`.
- **`Protect release tags`** (`refs/tags/v*`): no deletion, no update (so no moving), no force. Creating
  a tag is allowed, and that is how a release starts. The `manifest` branch is not covered: the
  release workflow pushes to it.

Working rules that follow:

- One branch at a time per topic (`feat/...`, `fix/...`), small Conventional Commits, a PR into `main`.
  Open the PR as soon as the first push is done so CI runs. **You may merge your own PRs once the
  three checks are green**, with a regular merge commit (`gh pr merge N --merge`), not squash or
  rebase: the changelog is built from individual commits (`git log --no-merges`), so each small
  commit must survive on `main`.
- A docs-only change also takes a PR. The Status Portal repo's "docs straight to main" shortcut does
  not exist here.
- Tags are cut on `main` after the merge. An RC whose release workflow failed is **not** repaired by
  deleting its tag (that is blocked); fix the cause and tag the next RC.
- The release workflow is read from the tagged commit, so a fix to `release.yml` only takes effect
  from the next tag.

## Project phases and gates

1. **Phase 1 - repository and release machinery.** Done and approved by the owner (2026-10-13).
2. **Release test** with an empty plugin: done 2026-10-13 (`v0.0.1-rc.1` = Jellyfin `0.0.1.1`; the
   Release, the unstable manifest entry and its MD5 were checked, and a Dockerised Jellyfin 12.2.0 with
   File Transformation 3.0.2.0 installed and loaded it from the unstable URL with no ABI warning).
3. **Phase 2 - the plugin.** Plan validated by the owner on 2026-10-13. Order of work:
   1. the portal's `detail=full` PR (draft, owner reviews and merges; he then installs a portal RC on
      his production through the portal's updater);
   2. meanwhile, plugin PRs that do not depend on it, built against a **fake portal that honours exactly
      the contract of that PR**: pure logic, client + endpoint, config page, injection, bubble;
   3. once the portal is merged and its RC is running at the owner's, point the real client at the
      real portal;
   4. `v0.1.0-rc.1` (never a stable) and a final report: Playwright screenshots of every state with
      the bubble open, Modern and Legacy layouts, desktop and mobile; what was and was not tested;
      known limits.
   **A real blocker, notably the File Transformation registration by reflection, means stopping and
   asking the owner, not working around it.**

## Dev loop and tests (mandatory in phase 2)

- Jellyfin **12.2** in Docker on this VM (`jellyfin/jellyfin:12.2`), plugins directory mounted as a
  volume, File Transformation 3.x installed in it. Data lives under `dev/data/` (gitignored).
- A **fake status portal** in `dev/` that can simulate: all OK / a service down / degraded /
  maintenance in progress / maintenance in 2 h / maintenance in 3 days / portal timing out.
- `dev/dev.sh`: build -> copy the DLL -> restart the container -> show the plugin's log lines.
- C# unit tests for the global-state computation (all priorities, combined cases), run by `ci.yml`.
- **Playwright headless** screenshots of the header and the open bubble for every scenario, in
  **Modern and Legacy** layouts, **desktop and mobile width**, saved in `./screenshots/`. Look at
  them yourself (Read the PNG; that is also how the owner sees them) before saying it is done.
  Check specifically that it is **a bubble anchored to the icon** and that **the URL does not change
  when it opens**.
- Check the Jellyfin log: the plugin loads with no ABI warning, and a Jellyfin **without** File
  Transformation still starts.
- Node and the .NET SDK are installed per-user: `export PATH=$HOME/.dotnet:$HOME/.local/node/bin:$PATH`.
  Playwright needs its own browser download (`pip install playwright`, then a scratch virtualenv).

## Repository map

```
Jellyfin.Plugin.JellyPortal/         the plugin (Plugin.cs, PluginIdentity.cs, Configuration/, Web/ client JS)
Jellyfin.Plugin.JellyPortal.Tests/   xunit; also guards that build.yaml, the csproj and this file agree
tools/                               release scripts (Python) + tests in tools/tests; check_js.sh
.github/workflows/                   ci.yml, release.yml
build.yaml  Directory.Build.props    packaging metadata; version placeholder
dev/  refs/  screenshots/  artifacts/  local only (dev/data, refs, screenshots, artifacts are gitignored)
```

Run before pushing: `dotnet test Jellyfin.Plugin.JellyPortal.sln`,
`python3 -m unittest discover -s tools/tests`, `tools/check_js.sh`.
