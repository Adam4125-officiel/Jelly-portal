/*
 * Jelly Portal - the status icon in the Jellyfin web client header, and its bubble.
 *
 * Vanilla JavaScript, no framework, no external resource. Injected into index.html by the plugin (through
 * File Transformation). Every console message carries the [JellyPortal] prefix. Text from the server is only ever
 * written with textContent, never as HTML.
 *
 * The pure helpers (durations, local times, clock skew, French wording) are exported for the node tests in
 * client-tests/ when this file is loaded as a CommonJS module; in a browser the file just starts.
 */
(function (root) {
    'use strict';

    var API_PATH = 'JellyPortal/Summary';
    var MIN_POLL_MS = 10000;
    var DEFAULT_POLL_MS = 60000;
    var TICK_MS = 20000;
    var BUBBLE_WIDTH = 360;
    var PHONE_BELOW = 600;    // narrower than this the bubble takes the screen width minus its margins
    var MARGIN = 8;
    var COLLAPSE_ABOVE = 6;   // more healthy services than this are folded behind one line

    // ------------------------------------------------------------------------------------------------------
    // Pure helpers
    // ------------------------------------------------------------------------------------------------------

    function parseUtc(iso) {
        if (typeof iso !== 'string' || iso === '') { return NaN; }
        return Date.parse(iso);
    }

    /** How far the browser's clock is from the portal's, in ms (portal minus browser). */
    function clockSkew(portalNowIso, browserNowMs) {
        var portal = parseUtc(portalNowIso);
        return isNaN(portal) ? 0 : portal - browserNowMs;
    }

    function pollDelayMs(pollSeconds) {
        var seconds = Number(pollSeconds);
        if (!isFinite(seconds) || seconds <= 0) { return DEFAULT_POLL_MS; }
        return Math.max(MIN_POLL_MS, Math.round(seconds * 1000));
    }

    // ------------------------------------------------------------------------------------------------------
    // Wording, in English and in French (the administrator's setting, or each viewer's own Jellyfin language)
    // ------------------------------------------------------------------------------------------------------

    // One table per language, with exactly the same keys (a test checks it, and that every state and every
    // failure reason the server can send has its entry). {0} is replaced by the first argument of text().
    var TEXT_EN = {
        state_down: 'At least one service is down',
        state_maintenance_ongoing: 'Maintenance in progress',
        state_degraded: 'Degraded performance',
        state_maintenance_soon: 'Maintenance planned soon',
        state_ok: 'All services are operational',
        state_unknown: 'Status unavailable',
        reason_not_configured: 'The plugin is not configured yet: the status portal’s address and key are missing.',
        reason_device_api_disabled: 'The status portal’s API is switched off.',
        reason_bad_api_key: 'The status portal refuses the configured API key.',
        reason_unreachable: 'The status portal cannot be reached.',
        reason_timeout: 'The status portal is not answering.',
        reason_bad_response: 'The status portal answered something unexpected.',
        reason_portal_outdated: 'The status portal is too old: version 1.12.0 or newer is required.',
        reason_jellyfin_unreachable: 'The Jellyfin server cannot be reached.',
        status_operational: 'Operational',
        status_slow: 'Slow',
        status_degraded: 'Degraded',
        status_maintenance: 'Under maintenance',
        status_down: 'Down',
        status_unknown: 'Unknown',
        incident_investigating: 'Investigating',
        incident_identified: 'Cause identified',
        incident_monitoring: 'Monitoring',
        h_services: 'Services',
        h_other_services: 'Other services',
        h_incidents: 'Ongoing incidents',
        h_maintenance: 'Maintenance in progress',
        h_upcoming: 'Upcoming maintenance',
        h_announcements: 'Announcements',
        tag_priority: 'Priority',
        tag_pinned: 'Pinned',
        meta_in_progress: 'in progress',
        meta_until: 'until {0}',
        fold_show: 'Show the {0} operational services',
        fold_hide: 'Hide the {0} operational services',
        link_open: 'Open the status portal',
        foot_updated: 'Updated at {0}',
        aria_bubble: 'Service status',
        aria_icon: 'Service status: {0}',
        when_today: 'today',
        when_tomorrow: 'tomorrow',
        when_yesterday: 'yesterday',
        when_at: 'at',
        unit_min: 'min',
        unit_h: 'h',
        unit_d: 'd',
        rel_in: 'in {0}',
        rel_in_less: 'in less than a minute',
        rel_since: 'for {0}',
        rel_since_less: 'just now',
        rel_ends: 'ends in {0}',
        rel_ends_less: 'ends in less than a minute',
        rel_ends_over: 'planned end passed'
    };

    var TEXT_FR = {
        state_down: 'Au moins un service est hors service',
        state_maintenance_ongoing: 'Maintenance en cours',
        state_degraded: 'Fonctionnement dégradé',
        state_maintenance_soon: 'Maintenance prévue prochainement',
        state_ok: 'Tous les services fonctionnent',
        state_unknown: 'État indisponible',
        reason_not_configured: 'Le plugin n’est pas encore configuré : l’adresse et la clé du status portal manquent.',
        reason_device_api_disabled: 'L’API du status portal est désactivée.',
        reason_bad_api_key: 'Le status portal refuse la clé API configurée.',
        reason_unreachable: 'Impossible de joindre le status portal.',
        reason_timeout: 'Le status portal ne répond pas.',
        reason_bad_response: 'Le status portal a répondu quelque chose d’inattendu.',
        reason_portal_outdated: 'Le status portal est trop ancien : la version 1.12.0 ou plus récente est nécessaire.',
        reason_jellyfin_unreachable: 'Impossible de contacter le serveur Jellyfin.',
        status_operational: 'Opérationnel',
        status_slow: 'Lent',
        status_degraded: 'Dégradé',
        status_maintenance: 'En maintenance',
        status_down: 'Hors service',
        status_unknown: 'Inconnu',
        incident_investigating: 'Investigation en cours',
        incident_identified: 'Cause identifiée',
        incident_monitoring: 'Sous surveillance',
        h_services: 'Services',
        h_other_services: 'Autres services',
        h_incidents: 'Incidents en cours',
        h_maintenance: 'Maintenance en cours',
        h_upcoming: 'Maintenances à venir',
        h_announcements: 'Annonces',
        tag_priority: 'Prioritaire',
        tag_pinned: 'Épinglée',
        meta_in_progress: 'en cours',
        meta_until: 'jusqu’à {0}',
        fold_show: 'Afficher les {0} services opérationnels',
        fold_hide: 'Masquer les {0} services opérationnels',
        link_open: 'Ouvrir le status portal',
        foot_updated: 'Mis à jour à {0}',
        aria_bubble: 'État des services',
        aria_icon: 'État des services : {0}',
        when_today: 'aujourd’hui',
        when_tomorrow: 'demain',
        when_yesterday: 'hier',
        when_at: 'à',
        unit_min: 'min',
        unit_h: 'h',
        unit_d: 'j',
        rel_in: 'dans {0}',
        rel_in_less: 'dans moins d’une minute',
        rel_since: 'depuis {0}',
        rel_since_less: 'depuis un instant',
        rel_ends: 'se termine dans {0}',
        rel_ends_less: 'se termine dans moins d’une minute',
        rel_ends_over: 'fin prévue dépassée'
    };

    var TEXTS = { en: TEXT_EN, fr: TEXT_FR };

    /**
     * 'fr' or 'en'. The setting is 'en', 'fr' or 'auto'; 'auto' follows what this viewer's Jellyfin is displayed in
     * (jellyfin-web writes it to <html lang>), and anything that is not French is English.
     */
    function resolveLanguage(setting, htmlLang) {
        if (setting === 'fr' || setting === 'en') { return setting; }
        return /^fr(?:[-_]|$)/i.test(String(htmlLang || '')) ? 'fr' : 'en';
    }

    /** The locale dates and times are written in: a 24-hour clock in both, whatever the browser's own habits. */
    function localeOf(lang) { return lang === 'fr' ? 'fr-FR' : 'en-GB'; }

    /** A sentence of the given language, with {0}, {1}... replaced. A key missing from a language falls back to English. */
    function text(lang, key, args) {
        var table = TEXTS[lang] || TEXT_EN;
        var value = table[key] !== undefined ? table[key] : TEXT_EN[key];
        if (value === undefined) { return ''; }
        return value.replace(/\{(\d+)\}/g, function (whole, index) { return args && args[Number(index)] !== undefined ? args[Number(index)] : whole; });
    }

    /** "3 h", "45 min", "2 d": the amount, in the unit that reads best. Real elapsed time, never wall-clock. */
    function duration(ms, lang) {
        var minutes = Math.floor(Math.max(0, ms) / 60000);
        if (minutes < 1) { return null; }
        if (minutes < 60) { return minutes + ' ' + text(lang, 'unit_min'); }
        var hours = Math.floor(minutes / 60);
        var h = text(lang, 'unit_h');
        if (hours < 24) {
            var rest = minutes % 60;
            return hours < 6 && rest !== 0 ? hours + ' ' + h + ' ' + (rest < 10 ? '0' : '') + rest : hours + ' ' + h;
        }
        return Math.floor(hours / 24) + ' ' + text(lang, 'unit_d');
    }

    function inFuture(ms, lang) {
        var d = duration(ms, lang);
        return d === null ? text(lang, 'rel_in_less') : text(lang, 'rel_in', [d]);
    }

    function sinceAgo(ms, lang) {
        var d = duration(ms, lang);
        return d === null ? text(lang, 'rel_since_less') : text(lang, 'rel_since', [d]);
    }

    function endsIn(ms, lang) {
        if (ms <= 0) { return text(lang, 'rel_ends_over'); }
        var d = duration(ms, lang);
        return d === null ? text(lang, 'rel_ends_less') : text(lang, 'rel_ends', [d]);
    }

    function calendarDay(ms, timeZone) {
        var parts = new Intl.DateTimeFormat('en-CA', { timeZone: timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date(ms));
        var get = function (type) { return Number(parts.filter(function (p) { return p.type === type; })[0].value); };
        return { year: get('year'), day: Math.round(Date.UTC(get('year'), get('month') - 1, get('day')) / 86400000) };
    }

    function timeOfDay(ms, timeZone, lang) {
        return new Intl.DateTimeFormat(localeOf(lang), { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: timeZone }).format(new Date(ms));
    }

    /**
     * An instant as a person reads it, in the given time zone (the browser's own when none is given):
     * "today at 22:00", "tomorrow at 03:00", "Monday 19 Oct at 22:00" (and the French equivalents). The day is the
     * calendar day in that zone, so it stays right across a daylight-saving change.
     */
    function formatWhen(iso, nowMs, timeZone, lang) {
        var ms = parseUtc(iso);
        if (isNaN(ms)) { return ''; }
        var then = calendarDay(ms, timeZone);
        var today = calendarDay(nowMs, timeZone);
        var diff = then.day - today.day;
        var label;
        if (diff === 0) { label = text(lang, 'when_today'); }
        else if (diff === 1) { label = text(lang, 'when_tomorrow'); }
        else if (diff === -1) { label = text(lang, 'when_yesterday'); }
        else {
            var options = { weekday: 'long', day: 'numeric', month: 'short', timeZone: timeZone };
            if (then.year !== today.year) { options.year = 'numeric'; }
            label = new Intl.DateTimeFormat(localeOf(lang), options).format(new Date(ms));
        }
        return label + ' ' + text(lang, 'when_at') + ' ' + timeOfDay(ms, timeZone, lang);
    }

    /** When something ends, relative to when it starts: just "03:57" the same day, the full wording otherwise. */
    function formatUntil(endIso, startIso, nowMs, timeZone, lang) {
        var end = parseUtc(endIso);
        var start = parseUtc(startIso);
        if (isNaN(end)) { return ''; }
        if (!isNaN(start) && calendarDay(end, timeZone).day === calendarDay(start, timeZone).day) {
            return timeOfDay(end, timeZone, lang);
        }
        return formatWhen(endIso, nowMs, timeZone, lang);
    }

    function stateTitle(state, lang) { return text(lang, TEXT_EN['state_' + state] !== undefined ? 'state_' + state : 'state_unknown'); }
    function reasonText(reason, lang) { return text(lang, TEXT_EN['reason_' + reason] !== undefined ? 'reason_' + reason : 'reason_bad_response'); }
    function statusLabel(status, lang) { return text(lang, TEXT_EN['status_' + status] !== undefined ? 'status_' + status : 'status_unknown'); }
    function incidentLabel(status, lang) { return TEXT_EN['incident_' + status] !== undefined ? text(lang, 'incident_' + status) : ''; }

    /** The portal link is only ever followed if it is a plain http(s) address (never javascript: or data:). */
    function safePortalUrl(url) {
        try {
            var parsed = new URL(url);
            return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? parsed.href : null;
        } catch (e) {
            return null;
        }
    }

    /**
     * Where the bubble goes: under the icon, right-aligned to it, never wider than the screen minus a margin,
     * with the arrow on the icon's centre. All numbers are viewport pixels.
     */
    function placeBubble(icon, viewport) {
        // About 360 px on a desktop; on a phone, the screen minus 16 px of margin (8 on each side): a bubble, never full screen.
        var width = viewport.width < PHONE_BELOW ? viewport.width - 2 * MARGIN : BUBBLE_WIDTH;
        var right = Math.min(viewport.width - MARGIN, icon.right + 12);
        var left = Math.max(MARGIN, right - width);
        right = left + width;
        var top = icon.bottom + 10;
        var arrow = Math.min(Math.max((icon.left + icon.right) / 2 - left, 18), width - 18);
        return {
            left: left,
            top: top,
            width: width,
            arrowLeft: arrow,
            maxHeight: Math.max(120, Math.min(Math.round(viewport.height * 0.7), viewport.height - top - MARGIN))
        };
    }

    /** True on jellyfin-web's playback page (`#/video`), where the header is the player's own and fades out by itself. */
    function isPlayerRoute(hash) { return /^#\/?video(?:[?\/]|$)/.test(String(hash || '')); }

    // ------------------------------------------------------------------------------------------------------
    // Exports for the node tests (and nothing else happens when loaded as a module)
    // ------------------------------------------------------------------------------------------------------

    var pure = {
        parseUtc: parseUtc, clockSkew: clockSkew, pollDelayMs: pollDelayMs, duration: duration, inFuture: inFuture,
        sinceAgo: sinceAgo, endsIn: endsIn, formatWhen: formatWhen, formatUntil: formatUntil, stateTitle: stateTitle, reasonText: reasonText,
        statusLabel: statusLabel, incidentLabel: incidentLabel, safePortalUrl: safePortalUrl, placeBubble: placeBubble, isPlayerRoute: isPlayerRoute,
        resolveLanguage: resolveLanguage, localeOf: localeOf, text: text, TEXT_EN: TEXT_EN, TEXT_FR: TEXT_FR,
        REASONS: Object.keys(TEXT_EN).filter(function (k) { return k.indexOf('reason_') === 0; }).map(function (k) { return k.slice(7); }),
        STATES: Object.keys(TEXT_EN).filter(function (k) { return k.indexOf('state_') === 0; }).map(function (k) { return k.slice(6); })
    };
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = pure;
        return;
    }
    if (typeof document === 'undefined' || root.__jellyPortalLoaded) { return; }
    root.__jellyPortalLoaded = true;

    // ------------------------------------------------------------------------------------------------------
    // DOM helpers
    // ------------------------------------------------------------------------------------------------------

    var SVG_NS = 'http://www.w3.org/2000/svg';

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) { node.className = className; }
        if (text !== undefined && text !== null) { node.textContent = text; }
        return node;
    }

    function svg(markup, size) {
        var wrapper = document.createElementNS(SVG_NS, 'svg');
        wrapper.setAttribute('viewBox', '0 0 24 24');
        wrapper.setAttribute('width', String(size));
        wrapper.setAttribute('height', String(size));
        wrapper.setAttribute('fill', 'none');
        wrapper.setAttribute('stroke', 'currentColor');
        wrapper.setAttribute('stroke-width', '2');
        wrapper.setAttribute('stroke-linecap', 'round');
        wrapper.setAttribute('stroke-linejoin', 'round');
        wrapper.setAttribute('aria-hidden', 'true');
        wrapper.setAttribute('focusable', 'false');
        markup.forEach(function (shape) {
            var node = document.createElementNS(SVG_NS, shape[0]);
            Object.keys(shape[1]).forEach(function (name) { node.setAttribute(name, shape[1][name]); });
            wrapper.appendChild(node);
        });
        return wrapper;
    }

    function glyph(size) {
        return svg([['circle', { cx: 12, cy: 12, r: 9.5 }], ['polyline', { points: '6.5,12.5 9.5,12.5 11,8.5 13.5,15.5 15,12.5 17.5,12.5' }]], size);
    }

    function clock(size) {
        return svg([['circle', { cx: 12, cy: 12, r: 9 }], ['polyline', { points: '12,7 12,12 15.5,14' }]], size);
    }

    // ------------------------------------------------------------------------------------------------------
    // State of the page
    // ------------------------------------------------------------------------------------------------------

    var model = {
        summary: null,        // the last SummaryDto
        skew: 0,              // portal clock minus browser clock, ms
        fetchedAt: 0,         // browser time of the last answer
        forbidden: false,     // the server said this user may not see it
        inflight: false,
        timer: null,
        tick: null,
        open: false,
        showHealthy: false,   // the folded healthy services are expanded
        language: 'auto'      // the administrator's setting, from the last summary: 'en', 'fr' or 'auto'
    };

    /** 'en' or 'fr', for this viewer, now. */
    function lang() { return resolveLanguage(model.language, document.documentElement.getAttribute('lang')); }
    var icon = null;
    var bubble = null;
    var raf = 0;

    function nowMs() { return Date.now() + model.skew; }

    function api() { return root.ApiClient; }

    function signedIn() {
        var client = api();
        return !!(client && typeof client.getCurrentUserId === 'function' && client.getCurrentUserId()
            && typeof client.accessToken === 'function' && client.accessToken());
    }

    // ------------------------------------------------------------------------------------------------------
    // Finding where the icon goes (Modern and Legacy headers)
    // ------------------------------------------------------------------------------------------------------

    function visible(node) { return !!node && node.getClientRects().length > 0; }

    /**
     * The legacy playback header, while the player has faded it out: jellyfin-web gives it the class `hide`
     * (display: none) a moment after the fade, and takes it away when the controls come back. That is the player's
     * doing and not the end of the header, so the icon must stay where it is (and an open bubble with it). Only
     * ever true without a modern app bar: the legacy header also exists, hidden for good, in the Modern layout.
     */
    function hiddenByThePlayer(headerRight) {
        return !document.querySelector('.MuiAppBar-root') && !!headerRight.closest('.skinHeader.osdHeader');
    }

    /** Something the viewer presses on purpose (a button, a link, a slider), as opposed to the picture or the background. */
    function isControl(node) {
        return !!(node && node.closest && node.closest('button, a, input, select, textarea, [role="button"], [role="slider"], [role="menuitem"], .headerButton, .paper-icon-button-light, .sliderContainer'));
    }

    function firstOther(parent) {
        var child = parent.firstElementChild;
        while (child && child.hasAttribute('data-jellyportal-icon')) { child = child.nextElementSibling; }
        return child;
    }

    /**
     * Modern layout: the app bar's toolbar is [left stack][box of buttons][box with the user menu]; the icon is the
     * first of the buttons, found from the user menu's avatar (labels are translated, the structure is not).
     * Modern layout on the playback page: the buttons box of the player's own toolbar (see below).
     * Legacy layout: .skinHeader .headerRight holds the header buttons (on the playback page too).
     * `before` is the element the icon must sit right before (null: last in its parent).
     */
    function findAnchor() {
        var avatar = document.querySelector('.MuiAppBar-root .MuiToolbar-root .MuiAvatar-root');
        if (avatar && visible(avatar)) {
            var userBox = avatar.closest('.MuiBox-root');
            if (userBox && userBox.parentElement) {
                var buttons = userBox.previousElementSibling;
                if (buttons && buttons.classList.contains('MuiBox-root') && !buttons.hasAttribute('data-jellyportal-icon')) {
                    return { layout: 'modern', parent: buttons, before: firstOther(buttons) };
                }
                return { layout: 'modern', parent: userBox.parentElement, before: userBox };
            }
        }
        // Modern layout, playback page: jellyfin-web draws a header of its own there (a toolbar with the back button, the
        // title and the cast and SyncPlay buttons) and no user menu, so there is no avatar to find the buttons from.
        var player = document.querySelector('.osdHeader .videoOsd-appBar');
        if (player && visible(player)) {
            var playerButtons = player.lastElementChild;
            if (playerButtons && playerButtons.classList.contains('MuiBox-root')) {
                return { layout: 'modern', parent: playerButtons, before: firstOther(playerButtons) };
            }
        }
        var right = document.querySelector('.skinHeader .headerRight');
        if (right && (visible(right) || hiddenByThePlayer(right))) {
            return { layout: 'legacy', parent: right, before: firstOther(right) };
        }
        return null;
    }

    // ------------------------------------------------------------------------------------------------------
    // The icon
    // ------------------------------------------------------------------------------------------------------

    function buildIcon(layout) {
        var button = el('button', 'jellyportal-btn' + (layout === 'legacy' ? ' headerButton headerButtonRight paper-icon-button-light' : ''));
        button.type = 'button';
        button.setAttribute('aria-haspopup', 'dialog');
        button.setAttribute('aria-expanded', 'false');
        button.setAttribute('data-jellyportal-icon', layout);
        button.appendChild(glyph(layout === 'legacy' ? 25 : 22));
        var badge = el('span', 'jellyportal-badge');
        badge.setAttribute('aria-hidden', 'true');
        badge.hidden = true;
        badge.appendChild(clock(10));
        button.appendChild(badge);
        button.addEventListener('click', function (event) {
            event.preventDefault();
            event.stopPropagation();
            toggle();
        });
        return button;
    }

    function paintIcon() {
        if (!icon) { return; }
        // Until the first answer there is no state to show: the icon keeps its place but stays invisible, so the
        // header does not jump when it appears and a grey "unknown" is never flashed at someone whose portal is fine.
        var summary = model.summary;
        var state = summary ? summary.state : 'loading';
        icon.setAttribute('data-state', state);
        if (!summary) { return; }
        var label = text(lang(), 'aria_icon', [stateTitle(state, lang())]);
        icon.setAttribute('aria-label', label);
        icon.title = label;
        icon.querySelector('.jellyportal-badge').hidden = state !== 'maintenance_soon';
    }

    function removeIcon() {
        closeBubble();
        if (icon && icon.parentNode) { icon.parentNode.removeChild(icon); }
        icon = null;
    }

    /** Idempotent: makes sure exactly one icon sits where the header currently wants it. */
    function ensureIcon() {
        raf = 0;
        if (model.forbidden || !signedIn()) { removeIcon(); return; }
        var anchor = findAnchor();
        if (!anchor) { removeIcon(); return; }
        if (icon && icon.getAttribute('data-jellyportal-icon') !== anchor.layout) { removeIcon(); }
        var created = false;
        if (!icon) { icon = buildIcon(anchor.layout); paintIcon(); created = true; }
        if (icon.parentNode !== anchor.parent || icon.nextElementSibling !== anchor.before) {
            anchor.parent.insertBefore(icon, anchor.before);
        }
        if (created && !model.summary) { refresh(true); }
        if (model.open) { positionBubble(); }
    }

    function scheduleEnsure() {
        if (!raf) { raf = root.requestAnimationFrame(ensureIcon); }
    }

    // ------------------------------------------------------------------------------------------------------
    // The bubble
    // ------------------------------------------------------------------------------------------------------

    function dot(status) {
        var node = el('span', 'jellyportal-dot');
        node.setAttribute('data-status', status);
        node.setAttribute('aria-hidden', 'true');
        return node;
    }

    function section(title, count) {
        var heading = el('div', 'jellyportal-heading');
        heading.appendChild(el('span', null, title));
        if (count !== undefined) { heading.appendChild(el('span', 'jellyportal-count', String(count))); }
        return heading;
    }

    function serviceRow(service) {
        var L = lang();
        var row = el('div', 'jellyportal-row jellyportal-service');
        row.appendChild(dot(service.status));
        row.appendChild(el('span', 'jellyportal-name', service.name));
        if (service.priority) { row.appendChild(el('span', 'jellyportal-tag', text(L, 'tag_priority'))); }
        if (service.status !== 'operational') { row.appendChild(el('span', 'jellyportal-status', statusLabel(service.status, L))); }
        return row;
    }

    function listOfServices(names) { return names.join(', '); }

    function incidentRow(incident, now) {
        var L = lang();
        var row = el('div', 'jellyportal-item' + (incident.priority ? ' jellyportal-priority' : ''));
        row.appendChild(el('div', 'jellyportal-title', incident.title));
        var meta = [incidentLabel(incident.status, L)];
        var since = parseUtc(incident.since);
        if (!isNaN(since)) { meta.push(sinceAgo(now - since, L)); }
        row.appendChild(el('div', 'jellyportal-meta', meta.filter(Boolean).join(' · ')));
        if (incident.services.length) { row.appendChild(el('div', 'jellyportal-meta', listOfServices(incident.services))); }
        return row;
    }

    function maintenanceRow(item, ongoing, now) {
        var L = lang();
        var row = el('div', 'jellyportal-item' + (item.priority ? ' jellyportal-priority' : ''));
        var title = el('div', 'jellyportal-title');
        title.appendChild(document.createTextNode(item.title));
        if (item.priority) { title.appendChild(el('span', 'jellyportal-tag', text(L, 'tag_priority'))); }
        row.appendChild(title);
        var start = parseUtc(item.startsAt);
        var end = parseUtc(item.endsAt);
        if (ongoing) {
            row.appendChild(el('div', 'jellyportal-meta', isNaN(end) ? text(L, 'meta_in_progress') : endsIn(end - now, L) + ' (' + formatWhen(item.endsAt, now, undefined, L) + ')'));
        } else {
            var when = isNaN(start) ? '' : formatWhen(item.startsAt, now, undefined, L) + ' · ' + inFuture(start - now, L);
            row.appendChild(el('div', 'jellyportal-meta', when));
            if (!isNaN(end)) { row.appendChild(el('div', 'jellyportal-meta', text(L, 'meta_until', [formatUntil(item.endsAt, item.startsAt, now, undefined, L)]))); }
        }
        if (item.services.length) { row.appendChild(el('div', 'jellyportal-meta', listOfServices(item.services))); }
        if (item.description) { row.appendChild(el('div', 'jellyportal-text', item.description)); }
        return row;
    }

    function announcementRow(announcement) {
        var L = lang();
        var row = el('div', 'jellyportal-item jellyportal-announcement');
        row.setAttribute('data-type', announcement.type);
        var title = el('div', 'jellyportal-title');
        title.appendChild(document.createTextNode(announcement.title));
        if (announcement.pinned) { title.appendChild(el('span', 'jellyportal-tag', text(L, 'tag_pinned'))); }
        row.appendChild(title);
        if (announcement.message) { row.appendChild(el('div', 'jellyportal-text', announcement.message)); }
        return row;
    }

    function buildContent() {
        var L = lang();
        var summary = model.summary;
        var now = nowMs();
        var box = el('div', 'jellyportal-content');

        var head = el('div', 'jellyportal-head');
        var state = summary ? summary.state : 'unknown';
        var pill = el('span', 'jellyportal-pill');
        pill.setAttribute('data-state', state);
        pill.appendChild(glyph(16));
        head.appendChild(pill);
        head.appendChild(el('span', 'jellyportal-headline', stateTitle(state, L)));
        box.appendChild(head);

        if (!summary || summary.state === 'unknown') {
            box.appendChild(el('div', 'jellyportal-notice', reasonText(summary && summary.reason ? summary.reason : 'jellyfin_unreachable', L)));
        } else {
            var down = summary.services.filter(function (s) { return s.status === 'down'; });
            var others = summary.services.filter(function (s) { return s.status !== 'down'; });
            if (down.length) {
                box.appendChild(section(text(L, 'h_services'), down.length));
                down.forEach(function (s) { box.appendChild(serviceRow(s)); });
            }
            if (summary.incidents.length) {
                box.appendChild(section(text(L, 'h_incidents'), summary.incidents.length));
                summary.incidents.forEach(function (i) { box.appendChild(incidentRow(i, now)); });
            }
            if (others.length) {
                box.appendChild(section(text(L, down.length ? 'h_other_services' : 'h_services'), others.length));
                var healthy = others.filter(function (s) { return s.status === 'operational'; });
                // Folded only when other sections compete for the room: with nothing else to show, the whole list is the answer.
                var competing = summary.incidents.length + summary.maintenance.ongoing.length + summary.maintenance.upcoming.length + summary.announcements.length > 0;
                var foldable = healthy.length > COLLAPSE_ABOVE && competing;
                var folded = foldable && !model.showHealthy;
                others.forEach(function (s) { if (!(folded && s.status === 'operational')) { box.appendChild(serviceRow(s)); } });
                if (foldable) {
                    var toggleRow = el('button', 'jellyportal-fold', text(L, model.showHealthy ? 'fold_hide' : 'fold_show', [healthy.length]));
                    toggleRow.type = 'button';
                    toggleRow.setAttribute('aria-expanded', String(model.showHealthy));
                    toggleRow.addEventListener('click', function () { model.showHealthy = !model.showHealthy; renderBubble(); });
                    box.appendChild(toggleRow);
                }
            }
            if (summary.maintenance.ongoing.length) {
                box.appendChild(section(text(L, 'h_maintenance'), summary.maintenance.ongoing.length));
                summary.maintenance.ongoing.forEach(function (m) { box.appendChild(maintenanceRow(m, true, now)); });
            }
            if (summary.maintenance.upcoming.length) {
                box.appendChild(section(text(L, 'h_upcoming'), summary.maintenance.upcoming.length));
                summary.maintenance.upcoming.forEach(function (m) { box.appendChild(maintenanceRow(m, false, now)); });
            }
            if (summary.announcements.length) {
                box.appendChild(section(text(L, 'h_announcements'), summary.announcements.length));
                summary.announcements.forEach(function (a) { box.appendChild(announcementRow(a)); });
            }
        }

        var foot = el('div', 'jellyportal-foot');
        var url = summary && summary.portal ? safePortalUrl(summary.portal.url) : null;
        if (url) {
            var link = el('a', 'jellyportal-link', text(L, 'link_open'));
            link.href = url;
            link.target = '_blank';
            link.rel = 'noopener noreferrer';
            foot.appendChild(link);
        }
        if (model.fetchedAt) {
            foot.appendChild(el('span', 'jellyportal-updated', text(L, 'foot_updated', [timeOfDay(model.fetchedAt, undefined, L)])));
        }
        box.appendChild(foot);
        return box;
    }

    function renderBubble() {
        if (!bubble) { return; }
        var scroller = bubble.querySelector('.jellyportal-scroll');
        var keep = scroller.scrollTop;
        while (scroller.firstChild) { scroller.removeChild(scroller.firstChild); }
        scroller.appendChild(buildContent());
        scroller.scrollTop = keep;
        positionBubble();
    }

    function positionBubble() {
        // An icon in a header the player has hidden (display: none) has no box: keep the bubble where it was.
        if (!bubble || !icon || !icon.getClientRects().length) { return; }
        var rect = icon.getBoundingClientRect();
        var place = placeBubble(rect, { width: root.innerWidth, height: root.innerHeight });
        bubble.style.left = place.left + 'px';
        bubble.style.top = place.top + 'px';
        bubble.style.width = place.width + 'px';
        bubble.style.maxHeight = place.maxHeight + 'px';
        bubble.querySelector('.jellyportal-arrow').style.left = place.arrowLeft + 'px';
    }

    function openBubble() {
        if (!icon || model.open) { return; }
        bubble = el('div', 'jellyportal-bubble');
        bubble.setAttribute('role', 'dialog');
        bubble.setAttribute('aria-label', text(lang(), 'aria_bubble'));
        bubble.appendChild(el('div', 'jellyportal-arrow'));
        bubble.appendChild(el('div', 'jellyportal-scroll'));
        document.body.appendChild(bubble);
        model.open = true;
        icon.setAttribute('aria-expanded', 'true');
        renderBubble();
        model.tick = root.setInterval(renderBubble, TICK_MS);
        refresh(false);
    }

    function closeBubble() {
        if (!model.open) { return; }
        model.open = false;
        root.clearInterval(model.tick);
        if (bubble && bubble.parentNode) { bubble.parentNode.removeChild(bubble); }
        bubble = null;
        if (icon) { icon.setAttribute('aria-expanded', 'false'); }
    }

    function toggle() { if (model.open) { closeBubble(); } else { openBubble(); } }

    // ------------------------------------------------------------------------------------------------------
    // Polling
    // ------------------------------------------------------------------------------------------------------

    function applySummary(summary) {
        model.summary = summary;
        if (summary.language) { model.language = summary.language; }
        model.fetchedAt = Date.now();
        model.skew = clockSkew(summary.now, model.fetchedAt);
        paintIcon();
        if (model.open) { renderBubble(); }
    }

    function applyFailure() {
        model.summary = { state: 'unknown', reason: 'jellyfin_unreachable', services: [], incidents: [], maintenance: { ongoing: [], upcoming: [] }, announcements: [], pollSeconds: 60, portal: { url: '' } };
        model.fetchedAt = Date.now();
        paintIcon();
        if (model.open) { renderBubble(); }
    }

    function schedule() {
        root.clearTimeout(model.timer);
        if (document.hidden || model.forbidden || !signedIn()) { return; }
        model.timer = root.setTimeout(function () { refresh(false); }, pollDelayMs(model.summary && model.summary.pollSeconds));
    }

    /** One request, never two at once. `force` is for coming back to the tab: do not wait for the next tick. */
    function refresh(force) {
        if (model.inflight || model.forbidden || !signedIn()) { return; }
        if (!force && model.fetchedAt && Date.now() - model.fetchedAt < 2000) { schedule(); return; }
        var client = api();
        model.inflight = true;
        client.ajax({ type: 'GET', url: client.getUrl(API_PATH), dataType: 'json' }).then(function (summary) {
            model.inflight = false;
            applySummary(summary);
            ensureIcon();
            schedule();
        }, function (response) {
            model.inflight = false;
            var status = response && response.status;
            if (status === 403) {
                model.forbidden = true;
                removeIcon();
                return;
            }
            if (status === 401) {
                removeIcon();
                return;
            }
            console.warn('[JellyPortal] the summary could not be loaded (HTTP ' + (status || 'network') + ')');
            applyFailure();
            ensureIcon();
            schedule();
        });
    }

    // ------------------------------------------------------------------------------------------------------
    // Wiring
    // ------------------------------------------------------------------------------------------------------

    function start() {
        // Any click or touch outside the bubble and the icon closes it, which is also how another popover of the
        // header (user menu, cast, ...) takes over: opening one is a click outside the other.
        // On the playback page jellyfin-web reacts to the *pointerdown* on the picture (a click pauses, a touch shows
        // or hides the controls), so a press that only closes the bubble must not reach it: it is stopped here, on the
        // window and in the capture phase, unless it lands on a control (which should still work).
        root.addEventListener('pointerdown', function (event) {
            if (!model.open) { return; }
            var target = event.target;
            if ((bubble && bubble.contains(target)) || (icon && icon.contains(target))) { return; }
            closeBubble();
            if (isPlayerRoute(root.location.hash) && !isControl(target)) { event.stopImmediatePropagation(); }
        }, true);
        // Closing the bubble is all Escape does: on the playback page jellyfin-web's own handler would also hide the
        // player's controls, and it cannot know the bubble was what the press was for. This listener is on the window,
        // in the capture phase, so it runs before jellyfin-web's.
        root.addEventListener('keydown', function (event) {
            if (event.key === 'Escape' && model.open) {
                event.stopImmediatePropagation();
                event.preventDefault();
                var button = icon;
                closeBubble();
                if (button) { button.focus(); }
            }
        }, true);
        root.addEventListener('resize', function () { if (model.open) { positionBubble(); } });
        root.addEventListener('scroll', function () { if (model.open) { positionBubble(); } }, true);
        // Opening the bubble never navigates; a navigation made by something else closes it.
        root.addEventListener('hashchange', function () { closeBubble(); refresh(false); scheduleEnsure(); });
        root.addEventListener('popstate', function () { closeBubble(); scheduleEnsure(); });
        document.addEventListener('visibilitychange', function () {
            if (document.hidden) { root.clearTimeout(model.timer); } else { refresh(true); schedule(); }
        });

        // jellyfin-web is a single-page app that renders its header again and again: watch for it.
        new MutationObserver(scheduleEnsure).observe(document.body, { childList: true, subtree: true });
        scheduleEnsure();
        refresh(true);
        console.info('[JellyPortal] client script loaded');
    }

    if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', start); } else { start(); }
}(typeof window !== 'undefined' ? window : globalThis));
