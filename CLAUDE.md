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
4. **No force-push on `main`.** After the initial setup, every change is a branch (`feat/...`,
   `fix/...`) and a PR into `main`, so CI runs.
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

**States, one shown at a time, by decreasing priority:**

| # | State | Look |
|---|---|---|
| 1 | `down` (at least one service down) | red |
| 2 | `maintenance_ongoing` | orange with a gentle pulse, to draw the eye |
| 3 | `degraded` | solid orange |
| 4 | `maintenance_soon` (inside the configured window) | normal icon + **clock badge**; badge only if the maintenance touches a priority service or falls in the window |
| 5 | `ok` | white / same colour as the other header icons (discreet) |
| 6 | `unknown` (portal unreachable) | grey, **never red** |

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
- Content: services with a state dot, ongoing maintenance (time remaining), upcoming maintenance
  (readable countdown such as "dans 3 h"), active announcements, an "Ouvrir le status portal" link.
- Style consistent with Jellyfin's dark theme, using the 12.x theme CSS variables where they exist.

**Polling** uses the configured interval, **pauses while the tab is hidden** (`visibilitychange`)
and refreshes immediately when the tab returns.

## Server behaviour (specified by the owner)

1. **Admin config page** (dashboard): status portal URL; optional API key (**stored server-side,
   never sent to the browser**); client polling interval (default 60 s); server cache TTL (default
   30 s); "upcoming maintenance" window (default 24 h); list of "priority" services (for example
   Jellyfin) whose maintenance must stand out; visibility (all users / admins only).
2. `GET /JellyPortal/Summary`, `[Authorize]` (any signed-in Jellyfin user). Queries the portal
   **server-side**, caches for the TTL (N browsers = one request to the portal) and returns
   normalised JSON: computed global state, services, ongoing and upcoming maintenance, active
   announcements, timestamp. The JSON format is agreed with the owner before coding (phase 2 plan).
3. An endpoint that serves the client JS/CSS, registered with File Transformation for injection
   into `index.html`.
4. Portal unreachable or timing out -> state `unknown` in the JSON. No exception, no red state.

The global state is computed by **pure code with no Jellyfin dependency**, so it is unit-tested
(every priority above, including combined cases) in `Jellyfin.Plugin.JellyPortal.Tests`.

## The status portal side (a different repository)

Work on the portal is done in **its** repository, not here, and shown to the owner as a **separate
diff**. First check what it already exposes: it has a public `/api/status` (its key set is pinned by
a test and external dashboards read it) and a keyed `/api/device/summary` for the Status-ESP display.
Add or extend a **read-only** `/api/summary` only if those are insufficient, **without breaking
anything that already uses them**. The portal has its own strict `CLAUDE.md` (branch + PR, version
bump, conventions tests, run the real server): follow it there.

## Releases

Tags: `vX.Y.Z-rc.N` (pre-release, N >= 1) or `vX.Y.Z` (stable). Anything else fails the workflow.
Regex `^v\d+\.\d+\.\d+(-rc\.\d+)?$`; `rc.0` is also refused (it would equal the stable version).

| Tag | Jellyfin version |
|---|---|
| `vX.Y.Z-rc.N` | `X.Y.Z.N` |
| `vX.Y.Z` | `X.Y.Z.0` |

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
  `tools/package_plugin.py` **refuses a DLL that does not contain the version it is told it has**.
- **Known quirk, by the owner's design:** since a stable is `X.Y.Z.0` and RCs are `X.Y.Z.N`, the
  stable of a series sorts *below* its own RCs numerically. Same code, so harmless, but a Jellyfin
  that installed `rc.2` (`X.Y.Z.2`) will not be offered the stable `X.Y.Z.0` as an update.
- GitHub Actions run steps under `bash -e` **without** `pipefail` unless `shell: bash` is set; the
  workflows set it as a default. Keep it, or `cmd | tee` will mask failures. Pass values to scripts
  through `env:`, never inline `${{ }}` inside `run:` (a tag name is attacker-shaped input).

The owner's steps (also in the README): merge PRs on `main`; `git tag vX.Y.Z-rc.1 && git push origin
vX.Y.Z-rc.1`; test on production through the unstable repository; on a bug, fix and tag `rc.2`;
when an RC is validated, the owner tags `vX.Y.Z` on that RC's commit.

## Project phases and gates

1. **Phase 1 - repository and release machinery.** Done once the owner has approved the tree and the
   workflows. Branch protection for `main` (required status checks: `Build and test`,
   `Client JS syntax`, `Release tooling`) is *proposed*, never enabled without the owner's say.
2. **Release test** with an empty plugin: `v0.0.1-rc.1`; check the Release, the unstable manifest,
   and that the dev Jellyfin installs it from the unstable URL.
3. **Phase 2 - the plugin.** Clone the refs; then **propose a short plan (code structure and the
   exact `/JellyPortal/Summary` JSON) and wait for the owner's validation before coding**. After that,
   work autonomously with the test loop. Deliver: features merged via PR, README (installation,
   configuration, screenshots), the portal diff apart, a `v0.1.0-rc.1` released by the workflow
   (never a stable), and a final summary of what works, what was tested and the known limits.

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
