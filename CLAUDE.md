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
  `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`. It cannot be referenced as a library (plugins
  load into different load contexts), so `Injection/FileTransformationBridge` registers by **reflection**, and
  this was run against the real 3.0.2.0 on Jellyfin 12.2, not only against a stand-in. What was learned reading
  its source and confirmed live:
  - The bridge finds the assembly that has `Jellyfin.Plugin.FileTransformation.PluginInterface` (by the type,
    in every load context), then builds the payload **with that method's own parameter type**
    (`JObject.Parse(json)` through reflection): no compile-time Newtonsoft reference and no assumption about
    which copy of it File Transformation loaded. The callback is `IndexHtmlTransformation.Apply(
    TransformationPayload)`; File Transformation builds the parameter with `JObject{"contents":...}.ToObject(type)`
    (so the property must be called `Contents`) and takes the returned string as the new file.
  - **The registration key must be the literal `index.html`, the same as the other plugins that patch that
    file (Home Sections, ...).** File Transformation keeps one list of transformations per exact key and, for a
    file whose name equals a key, runs *only that list*; with any other pattern string (an "anchored regex"
    looks tidier) it picks the first matching key, so either this plugin or the others would silently not run.
    `FilePattern` is pinned by a test. Registering the same id twice is ignored by File Transformation.
  - The injected addresses are **relative** (`../JellyPortal/client.js?v=<version>`), so a Jellyfin served under
    a base URL still works. The assets (`client.js`, `client.css`) are `[AllowAnonymous]` (the page loads
    before sign-in; they carry no data), looked up by exact name from embedded resources (nothing is joined to a
    path), with an `ETag` and `no-cache` so a browser revalidates with a 304.
  - **If File Transformation is absent the plugin starts, logs one clear `[JellyPortal]` warning and does
    nothing else; the same for an incompatible or failing File Transformation. Verified live: Jellyfin
    `Healthy`, plugin `Active`, no error, API still answering.**
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

### How the client is built (and why)

One script and one stylesheet, embedded in the plugin: `Web/jellyportal.js` (IIFE, no framework, no external
load: a test forbids `@import`, `url(` and any other host) and `Web/jellyportal.css`. Its pure helpers
(durations, local times, clock skew, bubble placement, French wording) are exported when it is loaded as a
CommonJS module, and tested with Node's own runner: `node --test client-tests/*.test.js` (CI runs it in the
`Client JS syntax` job). The DOM part is covered by the end-to-end scripts in `dev/e2e/`.

- **Where the icon goes, read from the real DOM** of `refs/jellyfin-web` v12.2 and confirmed live. *Modern*
  (the default, `layout` unset or `modern` in `localStorage`): `header.MuiAppBar-root > .MuiToolbar-root` holds
  `[left stack][Box of buttons][Box with the user menu]`; the icon is the **first** button of the middle Box,
  found from the user menu's `.MuiAvatar-root` (aria labels are translated, the structure is not). *Legacy*
  (`desktop-legacy`, `mobile-legacy`): `.skinHeader .headerRight`, first. The legacy `.skinHeader` also exists,
  hidden, in Modern, which is why Modern is looked for first. `localStorage.layout` is how a test selects one.
- **A `MutationObserver` on the body re-runs one idempotent `ensureIcon()`** (throttled with
  `requestAnimationFrame`): one icon, in the right place, rebuilt if the header was re-rendered or the layout
  changed, removed when there is no header (login page, video player) or the user is not signed in.
- **Authentication and errors**: `window.ApiClient.ajax` (the page's own token). 401 removes the icon; **403
  (administrators only) stops everything for that page and logs nothing**; any other failure shows the grey
  state with "Impossible de contacter le serveur Jellyfin" and keeps polling.
- **Polling**: the interval is the summary's `pollSeconds` (at least 10 s), one request at a time, none while
  the tab is hidden, an immediate refresh when it comes back.
- **The bubble**: `position: fixed`, `z-index: var(--jf-zIndex-drawer)` (1200: above the app bar at 1100, below
  menus and dialogs at 1300), 360 px wide on a desktop and the screen minus 16 px below 600 px, at most 70 % of
  the screen high with its own scroll, an arrow on the icon's centre, right-aligned to the icon. It closes on a
  `pointerdown` outside it and the icon (capture phase: this is also how it gives way to Jellyfin's own header
  menus, whose opening is a click outside it), on Escape (focus returns to the icon), on a click on the icon, and
  on `hashchange`/`popstate`. It never navigates and the URL never changes. Healthy services beyond six are
  folded behind one line so that incidents and maintenance are not buried under a long list of green dots.
- **Colours** come from Jellyfin's `--jf-palette-*` variables with a fallback each; violet (not in the palette)
  is `--jellyportal-violet` (`#b388ff`). Orange is only ever "degraded". The pulse of "maintenance in progress"
  stops under `prefers-reduced-motion`.
- **Logging**: every `console` call starts with the literal `'[JellyPortal]'` (checked by a test, which is why
  there is no `LOG` constant). No `innerHTML` and nothing built from server text but `textContent` (tested).
  The client and the server are tied by tests: the states and failure reasons the client words are exactly the
  ones the server can send.
- **Dev builds are not cached by the browser between rebuilds** (`no-cache` + ETag), but the `?v=` is the plugin
  version, so every dev build is `0.0.0.1`: hard-reload if a stale script is suspected.

## Server behaviour (specified by the owner, refined 2026-10-13)

1. **Admin config page** (dashboard): status portal URL; **the portal's display-device API key
   (required)**, stored server-side and never sent to a browser; client polling interval (default
   60 s); server cache TTL (default 30 s); "maintenance coming up" window (default 24 h, 1 h to 30
   days); priority services (by name, one per line, case-insensitive); visibility (all users / admins
   only). Without a URL or key the state is `unknown` with `reason: not_configured`, and the config
   page says what is wrong in words.
   **The settings page (`Configuration/configPage.html`, in French, reachable from the dashboard sidebar and
   from Plugins) talks only to this plugin's own endpoints**, all admin-only (`RequiresElevation`):
   `GET/POST /JellyPortal/Config` and `POST /JellyPortal/Config/Test`. It must never use Jellyfin's generic
   `getPluginConfiguration`/`updatePluginConfiguration`: that API would hand the portal key to the browser, and
   posting the whole object back would erase it (a test pins this). The view carries `hasApiKey`, never the
   key; **a blank key field means "keep the saved one"** (the page never has it), `clearApiKey` erases it.
   Numbers are clamped by the server and echoed back (polling 10 s-1 h, cache 5 s-10 min, window 1 h-30 days;
   the page takes its `min`/`max` from the server, so the browser's own validation refuses an out-of-range
   value before it is sent); an address that is not plain `http(s)://host[:port][/prefix]` is refused with a
   sentence. "Tester la connexion" asks the portal now with the address and key *as typed*, saved or not (an
   explicit admin action, so the one sanctioned wait on the network) and answers in words per failure reason.
   The page writes text with `textContent` only, never HTML. A refused value (400) is a normal answer, not a
   console error.
2. `GET /JellyPortal/Summary`, `[Authorize]` (any signed-in Jellyfin user; `403` for a non-admin when
   visibility is "admins only", and the client then does nothing). Queries the portal **server-side**,
   caches the *raw reading* for the TTL (N browsers = one request to the portal; a failure is cached
   for the same time so a dead portal costs one timeout per TTL, not one per browser) and **recomputes
   the state on every request** against the portal's clock, so a window that starts between two
   readings turns "soon" into "in progress" on time. The summary carries `pollSeconds` (the configured polling
   interval), so a running page follows a changed setting at its next poll. Returns `SummaryDto` (`Summary/SummaryDto.cs`:
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

## Handoff: where the work stands and where to pick it up (written 2026-10-14, ~03:00 CEST)

Read the rest of this file first; this section is only the state of play. The owner talks French; this repo is English.

**On `main` (merged, CI green):** PR #1 (stable = `X.Y.Z.100`, violet maintenance), #2 (pure `Summary/` logic), #3 (portal
client, cache, `GET /JellyPortal/Summary`, strict fake portal, dev loop), #4 (File Transformation injection), #5 (settings page).
`v0.0.1-rc.1` (= Jellyfin `0.0.1.1`) was released and installed from the unstable URL on the dev Jellyfin as the release test.

**This PR (#6, branch `feat/client-icon-bubble`): the client itself** (icon + bubble + polling), its Node tests
(`client-tests/`), the browser end-to-end scripts (`dev/e2e/`) and the README images (`docs/images/`). It was verified live:
`dev/e2e/bubble.py` **1644 checks, 0 failures** (16 scenarios x Modern/Legacy x desktop/phone: icon state and colour, pulse only
for maintenance in progress, clock badge only for "soon", bubble geometry, URL unchanged, close by outside click / Escape / icon,
text per scenario, incident order, no console error of ours, no grey flash before the first answer, colour kept while pressed),
`dev/e2e/behaviour.py` **55 of 56** (the one failure was the Tokyo time-zone check, a flaw in the *test*: the fake portal moves a
"two hours from now" start by a minute between two requests; fixed with a +-1 min tolerance and **not re-run**), 26 Node tests
(daylight saving included), 330 C# tests, 22+13 Python tests. Seven+ mutations of each layer were shown to fail the suite.
**Next action: `gh pr view 6`, wait for CI, then `gh pr ready 6 && gh pr merge 6 --merge --delete-branch`.**

**Then, in order:**
1. **The portal side is the owner's to merge**: `Adam4125-officiel/Status-Portal` **PR #29** (draft, branch `1.12.0`, `VERSION`
   `1.12.0-rc.1`, adds the opt-in `detail=full` to `/api/device/summary`; default answer byte-identical, pinned by a golden test).
   It was written by a Sonnet subagent and re-verified by me (full portal suite re-run: 1589 passed). **Do not merge it and do not
   tag it**: the owner reviews, merges, tags the portal RC and updates his production through the portal's updater (unstable
   channel). A local worktree `/home/adrrrr/Status-Portal-1.12.0` holds that branch (remove with `git worktree remove` once merged).
2. **Only when the owner says the portal RC runs at his place**: point the plugin at the **real** portal and check it for real
   (never done: everything so far ran against captured real answers and the fake). In the plugin's settings enter the portal address
   and its Display device key, press "Tester la connexion", then compare the bubble with the portal's own page. Capture anything
   that differs as a new fixture in `Jellyfin.Plugin.JellyPortal.Tests/Fixtures/` (re-capture from the real route, never by hand).
3. **Cut `v0.1.0-rc.1`** (Jellyfin `0.1.0.1`) from `main` once #6 is merged: `git tag v0.1.0-rc.1 && git push origin v0.1.0-rc.1`
   (Claude may push RC tags, never a stable one, and tags cannot be deleted or moved). Check the GitHub pre-release, the
   `manifest-unstable.json` entry (MD5 = the zip served at `sourceUrl`), and an install from the catalog on the dev Jellyfin
   (add the unstable repository on purpose and remove the local dev build: see the auto-update trap in *Dev loop*).
4. **Final report to the owner**: what works, what was tested and what was not, known limits, and the Playwright screenshots
   (`./screenshots/` is gitignored; regenerate with `dev/e2e/bubble.py` and `dev/e2e/contact_sheet.py`, Read the PNGs to show them).

**Known limits / not verified (say so in the report):** the real portal 1.12.0 and the owner's production Jellyfin were never
contacted; Jellyfin Desktop and the Android app were not tried (only a desktop Chromium, at 1280 and 390 px); a hidden tab is
simulated by overriding `document.hidden`; the Legacy layout was driven as `desktop-legacy`/`mobile-legacy` through
`localStorage.layout`; `v0.0.1-rc.1` predates all of the plugin's real features.

**To resume a session on the dev VM** (state lives in `dev/data/`, gitignored; Docker container `jellyportal-dev`, stopped when idle):
```
export PATH=$HOME/.dotnet:$HOME/.local/node/bin:$PATH
dev/dev.sh --no-logs                                   # build, package like a release, install, restart Jellyfin 12.2
python3 dev/jf.py configure --cache 5 --poll 10        # then restart: the e2e scripts expect a 5 s cache and a 10 s poll
(nohup python3 dev/fake_portal.py --host 172.17.0.1 --port 8099 > dev/data/fake-portal.log 2>&1 &)
dev/data/venv/bin/python -u dev/e2e/bubble.py          # ~12 min; or name scenarios; never run two e2e at once (shared server)
dev/data/venv/bin/python -u dev/e2e/behaviour.py       # ~6 min
dotnet test Jellyfin.Plugin.JellyPortal.sln; node --test client-tests/*.test.js; python3 -m unittest discover -s tools/tests; python3 -m unittest discover -s dev/tests
```
Do not run `dev/dev.sh` while an e2e script is running (it stops the container). The Python e2e output is buffered unless `-u`.
The dev admin and a `viewer` user are in `dev/data/dev-*.json` (random passwords, gitignored); the dev Jellyfin listens on
127.0.0.1 only and its Jelly Portal repository must stay unconfigured (auto-update).

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

- **Jellyfin 12.2 in Docker** on this VM (`jellyfin/jellyfin:12.2`, container `jellyportal-dev`), listening on
  `127.0.0.1:8096` only, File Transformation 3.x installed in it. Everything lives under `dev/data/`
  (gitignored). `dev/jellyfin.sh up|down|restart|logs|reset`; `dev/jf.py` is a stdlib Jellyfin admin client
  (`bootstrap`, `repos`, `packages`, `install`, `plugins`, `configure`, `create-user`, `summary`, `get`).
- **`dev/dev.sh`**: build -> package with `tools/package_plugin.py` (the release's own path) -> install as
  `Jelly Portal_0.0.0.1` -> restart -> show the plugin's log lines. **The dev Jellyfin must not have the Jelly
  Portal repository configured** (only File Transformation's): plugins auto-update by default, and at startup
  Jellyfin silently replaced the 0.0.0.1 dev build with the newest release from that repository. To test a
  real release, add the unstable repository on purpose and install from the catalog.
- **The plugin's configuration** is written with `dev/jf.py configure` (an XML file, read at start, so restart
  after). It cannot be posted through Jellyfin's configuration API: the key is `[JsonIgnore]`d on purpose.
- **Fake status portal** `dev/fake_portal.py`, bound to the Docker bridge gateway (`172.17.0.1:8099`, reachable
  from the container, not from the LAN). It answers exactly the plugin's request and **refuses anything else
  with a loud 400** (a query naming `resources`, `vms` or `jellyfin` above all). Scenarios, switchable while
  it runs (`curl -X POST http://172.17.0.1:8099/__scenario/NAME`): `ok`, `slow`, `down`, `degraded`,
  `incident_only`, `maintenance_ongoing`, `manual_maintenance`, `maintenance_soon` (in 2 h), `maintenance_far`
  (in 3 days), `muted_window`, `announcements`, `busy` (everything at once), and the failures `timeout`,
  `disabled` (404), `bad_key` (401), `outdated` (no `detail` marker), `bad_json`, `bad_gateway`, `slow_answer`.
- **The fake must keep the real portal's shape.** `Jellyfin.Plugin.JellyPortal.Tests/Fixtures/portal-1.12.0/`
  holds real answers captured from the portal's own route; `dev/tests/test_fake_portal.py` compares the fake's
  keys, key order and timestamp format with them, and `PortalContractTests` parses them with the real parser.
  If the portal's contract changes, re-capture the fixtures from its real route, never by hand.
- **End-to-end checks in a real Chromium** live in `dev/e2e/` (run by hand against the Docker Jellyfin, not in CI;
  `dev/data/venv` holds Playwright): `common.py` (launch, real login, a `Recorder` of console/page errors/failed
  requests/response bodies) and `config_page.py` (the settings page against every fake-portal failure, key
  never received, save/clear/refuse). Jellyfin's own client logs a console error before sign-in (a refused
  WebSocket); "no console error" therefore means none *of ours*: a `[JellyPortal]` error/warning, a page error
  naming the plugin, or a failed request under `/JellyPortal/`.
- C# unit tests for the state computation (every priority, every combination), the client, the cache and
  the configuration, run by `ci.yml`; so do the Python tests of `tools/` and `dev/`.
- **Playwright headless** screenshots of the header and the open bubble for every scenario, in **Modern and
  Legacy** layouts, **desktop and mobile width**, saved in `./screenshots/`. Look at them yourself (Read the
  PNG; that is also how the owner sees them) before saying it is done. Check specifically that it is **a
  bubble anchored to the icon** and that **the URL does not change when it opens**.
- Check the Jellyfin log: the plugin loads with no ABI warning, and a Jellyfin **without** File
  Transformation still starts.
- Node and the .NET SDK are installed per-user: `export PATH=$HOME/.dotnet:$HOME/.local/node/bin:$PATH`.
  Playwright needs its own browser download (`pip install playwright`, then a scratch virtualenv).
- **Verified live (2026-10-14), not just unit-tested:** the Summary endpoint through a real Jellyfin 12.2 for all
  12 state scenarios and all 7 failure scenarios (always HTTP 200, one clear `[JellyPortal]` warning line per
  failure, no stack trace, 5.1 s on a portal that never answers); 60 browser polls inside one cache lifetime
  produce 1 request to the portal; 401 without a token, 403 for a non-admin when set to administrators only;
  the key appears in no JSON answer, including Jellyfin's own `GET /Plugins/{id}/Configuration`, and stays in
  the XML on disk.

## Repository map

```
Jellyfin.Plugin.JellyPortal/         the plugin: Summary/ (pure logic), Portal/ (client, cache, service), Api/ (controller),
                                     Configuration/, Web/ (client JS)
Jellyfin.Plugin.JellyPortal.Tests/   xunit; also guards that build.yaml, the csproj and this file agree
tools/                               release scripts (Python) + tests in tools/tests; check_js.sh
.github/workflows/                   ci.yml, release.yml
build.yaml  Directory.Build.props    packaging metadata; version placeholder
dev/                                 fake portal, dev.sh, jellyfin.sh, jf.py and their tests (dev/data is gitignored)
refs/  screenshots/  artifacts/      local only, gitignored
```

Run before pushing: `dotnet test Jellyfin.Plugin.JellyPortal.sln`,
`python3 -m unittest discover -s tools/tests`, `tools/check_js.sh`.
