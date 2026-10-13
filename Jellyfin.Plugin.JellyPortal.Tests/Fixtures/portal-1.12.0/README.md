# Real answers of the Status Portal's display-device API

Raw bytes served by the portal's own code (Status-Portal 1.12.0, `GET /api/device/summary`, key in the
`X-Api-Key` header), saved by `curl -o`, never reformatted. They are the contract the plugin reads; the
tests in `PortalContractTests.cs` parse them, and `dev/tests/test_fake_portal.py` checks that the fake
portal has exactly their shape.

| File | What it is |
|---|---|
| `full_scenario.json` | the plugin's exact request, `?sections=services,incidents,maintenance,announcements&services=all&detail=full`, against a seeded scenario |
| `default_scenario.json` | the same data and the same query answered by an **older** portal (1.11.1), which ignores `detail=full`: no `detail` key, short names, no `service_names` arrays |
| `full_worstcase.json` | the adversarial worst case for that request (112,785 bytes): 110 services, 12 incidents and 25 windows each covering 20 services, 12 announcements, every string made of quotes, backslashes, emoji and control characters |

The seed behind `full_scenario.json` (captured 2026-10-13 about 21:23 UTC, `now` = `2026-10-13T21:23:48Z`):

- 12 services, one of them muted (`ignore_in_overall_status`), which is why `total` is 11 and it is absent.
  `Nextcloud family photos, documents and archive` is **down** and its name contains commas; `Sonarr` is
  degraded; `Router admin` is under maintenance (its window is in progress); `Prowlarr` is slow.
- Two open incidents: one covers two services, one covers none (`"services":""`, `"service_names":[]`).
- Maintenance: "Router firmware upgrade" 20:53-22:23 in progress; "Media disk replacement" 23:23 covering
  Jellyfin and the comma-named service; "Quarterly UPS battery test" three days later.
- Announcements: "Movie night" (pinned) and "Backups run on Sunday"; a scheduled one is **not** sent.

The portal's full notes are in its PR (Status-Portal #29). If the portal's contract changes, re-capture
these from its real route; do not edit them by hand.
