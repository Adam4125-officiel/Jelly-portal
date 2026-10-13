# Jelly-portal
A Status Portal integration as a Jellyfin Plugin

Jelly Portal adds a permanent icon to the header of the Jellyfin web client, next to the existing
header icons. The icon reflects the overall state of your self-hosted services as reported by your
[Status Portal](https://github.com/Adam4125-officiel/Status-Portal), and a click opens a small
bubble over the current page with the details: services, maintenance in progress or coming up, and
active announcements.

> **Status: early development.** The repository, CI and release pipeline are in place; the plugin
> itself does not do anything yet. This README describes what it is being built to do.

## Requirements

- **Jellyfin 12.2** (the plugin is built against `Jellyfin.Controller` 12.2.0; its `targetAbi` is
  `12.2.0.0`, so older servers are not offered it).
- The **[File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation)**
  plugin, 3.x. It is what lets Jelly Portal add its icon to the web client without modifying any
  jellyfin-web file. Without it Jelly Portal still loads, logs a warning, and shows no icon.
- A running Status Portal reachable from the Jellyfin server.

It only shows in clients built on jellyfin-web: browsers, Jellyfin Desktop and the Android app.
Android TV, Swiftfin, Findroid and similar clients will not show it.

## Installation

1. In Jellyfin, open **Dashboard > Plugins > Repositories** and add these repositories:
   - File Transformation: `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`
   - Jelly Portal, **stable**:
     `https://raw.githubusercontent.com/Adam4125-officiel/Jelly-portal/manifest/manifest.json`
   - or Jelly Portal, **unstable** (release candidates and stable releases):
     `https://raw.githubusercontent.com/Adam4125-officiel/Jelly-portal/manifest/manifest-unstable.json`
2. In the **Catalog**, install **File Transformation**, then **Jelly Portal**.
3. Restart Jellyfin.

## Configuration

Dashboard > Plugins > Jelly Portal (planned settings):

| Setting | Default |
|---|---|
| Status Portal URL | none |
| API key (optional, kept on the server, never sent to browsers) | none |
| Browser polling interval | 60 s |
| Server cache lifetime | 30 s |
| "Maintenance coming up" window | 24 h |
| Priority services (their maintenance stands out) | none |
| Visible to | all users / admins only |

## How it will look

The icon shows one state at a time:

| State | Meaning | Look |
|---|---|---|
| Down | at least one service is down | red |
| Maintenance in progress | a maintenance window is under way | violet, gently pulsing |
| Degraded | a service is degraded | orange |
| Maintenance soon | one is scheduled inside the window | normal icon with a clock badge |
| OK | everything is fine | discreet, like the other header icons |
| Unknown | the portal cannot be reached | grey (never red) |

Clicking it opens a bubble under the icon: no page change, closes on a click outside, on Escape or
on a second click on the icon. On a phone it is still a bubble, never full screen. Screenshots will
be added here with the first feature release.

## Development

You need the .NET 10 SDK and Node 24 (only for the JavaScript syntax check) and Python 3 with
PyYAML (only for the release tooling).

```sh
dotnet build Jellyfin.Plugin.JellyPortal.sln
dotnet test  Jellyfin.Plugin.JellyPortal.sln
python3 -m unittest discover -s tools/tests
tools/check_js.sh
```

CI runs the same on every pull request and on `main`. Work happens on `feat/...` / `fix/...`
branches merged through pull requests, with [Conventional Commits](https://www.conventionalcommits.org/)
(`feat:`, `fix:`, `perf:`, `chore:`, `ci:`, `docs:`...). `CLAUDE.md` holds the conventions and the
technical decisions for anyone (human or Claude Code) picking the project up.

## Releasing

Versions come from git tags, never from the code.

| Tag | Kind | Jellyfin version |
|---|---|---|
| `vX.Y.Z-rc.N` (N from 1 to 99) | pre-release | `X.Y.Z.N` |
| `vX.Y.Z` | stable | `X.Y.Z.100` |

Any other tag makes the release workflow fail. A stable tag is only accepted on a commit that
already carries a `vX.Y.Z-rc.N` tag. Published tags are never deleted or moved: a broken release
candidate is fixed by the next one.

1. Merge the pull requests on `main`.
2. `git tag vX.Y.Z-rc.1 && git push origin vX.Y.Z-rc.1`, then test on your server through the
   **unstable** repository.
3. A bug? Fix it, then `rc.2`, and so on.
4. A release candidate validated? `git tag vX.Y.Z <commit of the last RC> && git push origin vX.Y.Z`.

The `Release` workflow then builds and tests (nothing is published if a test fails), packages the
plugin zip with its MD5 checksum, writes the changelog from the commits since the previous tag,
creates the GitHub Release (a pre-release for an RC), and updates the `manifest` branch: an RC goes
to `manifest-unstable.json` only, a stable release to `manifest.json` and `manifest-unstable.json`.
Each manifest keeps the 10 latest versions.

Jellyfin compares the four-part number, so the stable release takes `.100`, above every possible
release candidate (`.1` to `.99`): a Jellyfin that installed `rc.2` is always offered the stable
release of the same version. For the same reason `rc.0` and `rc.100` or more are refused.

## License

[GNU AGPL-3.0](LICENSE)
