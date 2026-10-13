'use strict';

// Tests of the client's pure helpers: durations, local times, clock skew, the bubble's placement, the wording.
// Run: node --test client-tests/*.test.js      (Node's own runner, no dependency)
const test = require('node:test');
const assert = require('node:assert/strict');
const c = require('../Jellyfin.Plugin.JellyPortal/Web/jellyportal.js');

const MIN = 60 * 1000;
const HOUR = 60 * MIN;
const at = (iso) => Date.parse(iso);

// ---- durations ---------------------------------------------------------------------------------------------

test('a duration is written in the unit that reads best', () => {
    const rows = [
        [0, null], [30 * 1000, null], [59999, null], [-5 * MIN, null],
        [60 * 1000, '1 min'], [45 * MIN, '45 min'], [59 * MIN + 59000, '59 min'],
        [60 * MIN, '1 h'], [2 * HOUR + 5 * MIN, '2 h 05'], [2 * HOUR + 30 * MIN, '2 h 30'], [5 * HOUR + 59 * MIN, '5 h 59'],
        [6 * HOUR, '6 h'], [6 * HOUR + 30 * MIN, '6 h'], [23 * HOUR + 59 * MIN, '23 h'],
        [24 * HOUR, '1 j'], [80 * HOUR, '3 j'], [10 * 24 * HOUR, '10 j']
    ];
    for (const [ms, expected] of rows) { assert.equal(c.duration(ms), expected, `${ms} ms`); }
});

test('countdown wording', () => {
    assert.equal(c.inFuture(3 * HOUR), 'dans 3 h');
    assert.equal(c.inFuture(45 * MIN), 'dans 45 min');
    assert.equal(c.inFuture(20 * 1000), 'dans moins d\u2019une minute');
    assert.equal(c.endsIn(45 * MIN), 'se termine dans 45 min');
    assert.equal(c.endsIn(0), 'fin pr\u00e9vue d\u00e9pass\u00e9e');
    assert.equal(c.endsIn(-HOUR), 'fin pr\u00e9vue d\u00e9pass\u00e9e');
    assert.equal(c.endsIn(10 * 1000), 'se termine dans moins d\u2019une minute');
    assert.equal(c.sinceAgo(25 * MIN), 'depuis 25 min');
    assert.equal(c.sinceAgo(3 * HOUR), 'depuis 3 h');
    assert.equal(c.sinceAgo(10 * 1000), 'depuis un instant');
});

// ---- local time, in French, across both daylight-saving changes -----------------------------------------------

const PARIS = 'Europe/Paris';

test('today and tomorrow, in summer time (CEST, UTC+2)', () => {
    const now = at('2026-10-13T18:00:00Z');                       // 20:00 in Paris
    assert.equal(c.formatWhen('2026-10-13T20:00:00Z', now, PARIS), 'aujourd\u2019hui \u00e0 22:00');
    assert.equal(c.formatWhen('2026-10-14T01:00:00Z', now, PARIS), 'demain \u00e0 03:00');
    assert.equal(c.formatWhen('2026-10-12T20:00:00Z', now, PARIS), 'hier \u00e0 22:00');
});

test('today, in winter time (CET, UTC+1)', () => {
    const now = at('2026-11-10T12:00:00Z');                       // 13:00 in Paris
    assert.equal(c.formatWhen('2026-11-10T21:00:00Z', now, PARIS), 'aujourd\u2019hui \u00e0 22:00');
    assert.equal(c.formatWhen('2026-11-10T22:30:00Z', now, PARIS), 'aujourd\u2019hui \u00e0 23:30');
});

test('the same UTC time reads one hour later after the autumn change', () => {
    assert.equal(c.formatWhen('2026-10-24T20:00:00Z', at('2026-10-24T08:00:00Z'), PARIS), 'aujourd\u2019hui \u00e0 22:00');   // CEST
    assert.equal(c.formatWhen('2026-10-26T21:00:00Z', at('2026-10-26T08:00:00Z'), PARIS), 'aujourd\u2019hui \u00e0 22:00');   // CET
});

test('the night of the autumn change (clocks go back 03:00 -> 02:00 on 2026-10-25)', () => {
    const now = at('2026-10-24T23:30:00Z');                       // 01:30 CEST on the 25th
    const later = '2026-10-25T01:30:00Z';                         // 02:30 CET: the second 02:xx of that night
    assert.equal(c.formatWhen(later, now, PARIS), 'aujourd\u2019hui \u00e0 02:30');
    // Two real hours apart although the wall clock only moved from 01:30 to 02:30.
    assert.equal(c.inFuture(at(later) - now), 'dans 2 h');
});

test('the night of the spring change (clocks go forward 02:00 -> 03:00 on 2027-03-28)', () => {
    const now = at('2027-03-27T23:30:00Z');                       // 00:30 CET on the 28th
    const later = '2027-03-28T01:30:00Z';                         // 03:30 CEST
    assert.equal(c.formatWhen(later, now, PARIS), 'aujourd\u2019hui \u00e0 03:30');
    // Two real hours apart although the wall clock moved from 00:30 to 03:30.
    assert.equal(c.inFuture(at(later) - now), 'dans 2 h');
});

test('a maintenance that crosses midnight is "tomorrow", by the calendar of the viewer', () => {
    const now = at('2026-10-13T21:30:00Z');                       // 23:30 in Paris
    assert.equal(c.formatWhen('2026-10-13T22:30:00Z', now, PARIS), 'demain \u00e0 00:30');
    assert.equal(c.inFuture(at('2026-10-13T22:30:00Z') - now), 'dans 1 h');
});

test('the day follows the time zone, not UTC', () => {
    const now = at('2026-10-13T23:30:00Z');                       // 19:30 in New York, 01:30 on the 14th in Paris
    assert.equal(c.formatWhen('2026-10-14T01:00:00Z', now, 'America/New_York'), 'aujourd\u2019hui \u00e0 21:00');
    assert.equal(c.formatWhen('2026-10-14T01:00:00Z', now, PARIS), 'aujourd\u2019hui \u00e0 03:00');
    assert.equal(c.formatWhen('2026-10-14T01:00:00Z', now, 'Asia/Tokyo'), 'aujourd\u2019hui \u00e0 10:00');
});

test('a farther day is named, and the year is added when it is not this one', () => {
    const now = at('2026-10-13T10:00:00Z');
    assert.match(c.formatWhen('2026-10-19T20:00:00Z', now, PARIS), /^lundi 19 oct\.? \u00e0 22:00$/);
    assert.match(c.formatWhen('2027-01-05T20:00:00Z', now, PARIS), /^mardi 5 janv\.? 2027 \u00e0 21:00$/);
});

test('an unreadable instant is an empty string, never "Invalid Date"', () => {
    assert.equal(c.formatWhen('', Date.now(), PARIS), '');
    assert.equal(c.formatWhen(null, Date.now(), PARIS), '');
    assert.equal(c.formatWhen('garbage', Date.now(), PARIS), '');
});

test('without a time zone the browser\u2019s own is used', () => {
    const out = c.formatWhen('2026-10-13T20:00:00Z', at('2026-10-13T18:00:00Z'));
    assert.match(out, /^(aujourd\u2019hui|demain|hier|\w+ \d+ \w+\.?) \u00e0 \d\d:\d\d$/);
});

// ---- clock skew ----------------------------------------------------------------------------------------------------

test('the skew is the portal\u2019s clock minus the browser\u2019s', () => {
    const portalNow = '2026-10-13T20:00:00Z';
    assert.equal(c.clockSkew(portalNow, at('2026-10-13T20:05:00Z')), -5 * MIN);   // the browser runs 5 min ahead
    assert.equal(c.clockSkew(portalNow, at('2026-10-13T19:58:00Z')), 2 * MIN);    // 2 min behind
    assert.equal(c.clockSkew(portalNow, at(portalNow)), 0);
    assert.equal(c.clockSkew('garbage', 123), 0);
});

test('a browser clock that is wrong does not move a countdown', () => {
    // The portal says it is 20:00 and a maintenance starts at 22:00. The browser believes it is 20:07.
    const browserNow = at('2026-10-13T20:07:00Z');
    const skew = c.clockSkew('2026-10-13T20:00:00Z', browserNow);
    const corrected = browserNow + skew;
    assert.equal(c.inFuture(at('2026-10-13T22:00:00Z') - corrected), 'dans 2 h');
    // 22:00 UTC is midnight in Paris, so "tomorrow" by the corrected clock (20:00 UTC = 22:00 in Paris).
    assert.equal(c.formatWhen('2026-10-13T22:00:00Z', corrected, PARIS), 'demain \u00e0 00:00');
});

// ---- polling --------------------------------------------------------------------------------------------------------

test('the polling interval comes from the server, never below ten seconds', () => {
    assert.equal(c.pollDelayMs(45), 45000);
    assert.equal(c.pollDelayMs(10), 10000);
    assert.equal(c.pollDelayMs(1), 10000);
    for (const bad of [undefined, null, 0, -3, NaN, 'abc']) { assert.equal(c.pollDelayMs(bad), 60000, String(bad)); }
});

// ---- wording --------------------------------------------------------------------------------------------------------

test('every state has a title, and an unknown one falls back to "unavailable"', () => {
    for (const state of c.STATES) { assert.ok(c.stateTitle(state).length > 5, state); }
    assert.equal(c.stateTitle('something new'), c.stateTitle('unknown'));
    assert.equal(c.stateTitle('maintenance_ongoing'), 'Maintenance en cours');
});

test('every failure reason has its own sentence', () => {
    const sentences = c.REASONS.map((r) => c.reasonText(r));
    assert.equal(new Set(sentences).size, sentences.length);
    assert.ok(c.reasonText('nonsense').length > 10);
});

test('status and incident labels', () => {
    assert.equal(c.statusLabel('down'), 'Hors service');
    assert.equal(c.statusLabel('maintenance'), 'En maintenance');
    assert.equal(c.statusLabel('whatever'), 'Inconnu');
    assert.equal(c.incidentLabel('identified'), 'Cause identifi\u00e9e');
    assert.equal(c.incidentLabel('???'), '');
});

// ---- the link -------------------------------------------------------------------------------------------------------

test('only a plain http(s) address is ever linked', () => {
    assert.equal(c.safePortalUrl('http://192.168.1.10:5000'), 'http://192.168.1.10:5000/');
    assert.equal(c.safePortalUrl('https://status.example/portal'), 'https://status.example/portal');
    for (const bad of ['javascript:alert(1)', 'data:text/html,<script>', 'ftp://x', 'portal.example', '', null, undefined, 'file:///etc/passwd']) {
        assert.equal(c.safePortalUrl(bad), null, String(bad));
    }
});

// ---- where the bubble goes ------------------------------------------------------------------------------------------

const icon = (left, top = 1, size = 46) => ({ left, top, right: left + size, bottom: top + size });

test('on a desktop the bubble hangs under the icon, 360 px wide, right-aligned to it', () => {
    const place = c.placeBubble(icon(1013), { width: 1280, height: 800 });
    assert.equal(place.width, 360);
    assert.equal(place.top, 47 + 10);
    assert.equal(place.left + place.width, 1059 + 12);               // right edge a little past the icon's
    assert.ok(place.left >= 8 && place.left + place.width <= 1280 - 8);
    assert.equal(place.left + place.arrowLeft, (1013 + 1059) / 2);   // the arrow is on the icon's centre
    assert.equal(place.maxHeight, 560);                              // 70 % of 800
});

test('on a phone it stays a bubble: the screen minus 16 px, never the full screen', () => {
    for (const width of [320, 360, 390, 430, 599]) {
        const place = c.placeBubble(icon(width - 140), { width, height: 800 });
        assert.equal(place.width, width - 16, `${width} px wide`);
        assert.equal(place.left, 8);
        assert.equal(place.left + place.width, width - 8);
        assert.ok(place.arrowLeft >= 18 && place.arrowLeft <= place.width - 18);
        assert.ok(place.width < width, 'never full screen');
    }
    assert.ok(c.placeBubble(icon(250), { width: 390, height: 800 }).maxHeight <= 560);
});

test('a tablet or a narrow window of 600 px and more gets the desktop bubble', () => {
    assert.equal(c.placeBubble(icon(500), { width: 600, height: 800 }).width, 360);
    assert.equal(c.placeBubble(icon(500), { width: 768, height: 800 }).width, 360);
});

test('an icon near the left edge never pushes the bubble off screen', () => {
    const place = c.placeBubble(icon(2), { width: 1280, height: 800 });
    assert.equal(place.left, 8);
    assert.ok(place.arrowLeft >= 18);
});

test('an icon at the very right edge keeps the bubble inside the margin', () => {
    const place = c.placeBubble(icon(1230), { width: 1280, height: 800 });
    assert.ok(place.left + place.width <= 1280 - 8);
});

test('the height is 70 % of the screen, and never more than the room under the icon', () => {
    assert.equal(c.placeBubble(icon(900), { width: 1280, height: 1000 }).maxHeight, 700);
    const short = c.placeBubble(icon(900, 1), { width: 1280, height: 300 });
    assert.ok(short.maxHeight <= 300 - 57 - 8 || short.maxHeight === 120);
});

test('an end is just a time the same day as the start, and the full wording when the day differs', () => {
    const now = at('2026-10-13T18:00:00Z');
    assert.equal(c.formatUntil('2026-10-13T21:00:00Z', '2026-10-13T20:00:00Z', now, PARIS), '23:00');
    assert.equal(c.formatUntil('2026-10-14T00:30:00Z', '2026-10-13T20:00:00Z', now, PARIS), 'demain \u00e0 02:30');
    // The same day in the viewer's zone, not in UTC: 22:30 UTC and 21:30 UTC are the 14th in Tokyo.
    assert.equal(c.formatUntil('2026-10-13T23:30:00Z', '2026-10-13T21:30:00Z', now, 'Asia/Tokyo'), '08:30');
    assert.equal(c.formatUntil('', '2026-10-13T20:00:00Z', now, PARIS), '');
    assert.equal(c.formatUntil('2026-10-13T21:00:00Z', 'garbage', now, PARIS), 'aujourd\u2019hui \u00e0 23:00');
});
