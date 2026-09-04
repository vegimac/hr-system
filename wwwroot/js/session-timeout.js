// ══════════════════════════════════════════════════════════════════════
// session-timeout.js — benutzerbezogener Session-/Logout-Wächter
// ──────────────────────────────────────────────────────────────────────
// Walter-Vorgabe 21.06.2026. Werte kommen pro Benutzer vom Backend
// (currentUser.idleTimeoutMinutes / maxSessionMinutes / sessionStartedAt;
// leer = Rollen-Default, bereits server-seitig aufgelöst).
//
//   • Inaktivitäts-Logout nach idleTimeoutMinutes (Aktivität setzt ihn zurück)
//   • GLEITENDE Verlängerung (Walter 04.09.2026): solange gearbeitet wird,
//     holt der Wächter ca. alle 30 Minuten still ein frisches Token
//     (POST /api/auth/refresh) — der 8-Stunden-Rauswurf mitten in der
//     Arbeit ist damit weg. maxSessionMinutes ist nur noch die Lebensdauer
//     EINES Tokens (Fallback, wenn kein Refresh mehr gelingt).
//   • Harte Obergrenze ab dem ersten Login (Server: 14 h, hardEndAt) —
//     danach ist Neu-Login Pflicht; Warnmodal 60 s vorher.
//   • Warnmodal 60 s vorher mit „Angemeldet bleiben" (Idle-Fall).
// ══════════════════════════════════════════════════════════════════════
(function () {
    const CHECK_MS = 60000;   // Prüf-Intervall (Walter 04.09.2026: jede Minute reicht)
    const WARN_MS  = 120000;  // Warnmodal 2 Minuten vorher — damit bei 60-s-Takt
                              // sicher eine Prüfung ins Warnfenster fällt
    const REFRESH_AFTER_MS = 30 * 60000;   // Token-Alter, ab dem bei Aktivität verlängert wird
    const ACTIVITY_EVENTS = ['mousemove', 'keydown', 'click', 'touchstart', 'scroll'];

    let idleMs = 0;
    let maxEndMs = 0;        // Ablauf des aktuellen Tokens
    let hardEndMs = 0;       // harte Obergrenze ab erstem Login
    let tokenIssuedMs = 0;   // Ausstellung des aktuellen Tokens
    let lastActivity = 0;
    let checkTimer = null;
    let countdownTimer = null;
    let listening = false;
    let refreshing = false;

    const now = () => Date.now();
    // Lebenszeichen im Browser-Speicher (Walter 04.09.2026): jede Minute,
    // bei Aktivität (max. alle 10 s) und beim Verlassen der Seite. app-core.js
    // prüft beim Start: liegt das letzte Lebenszeichen mehr als 2 Minuten
    // zurück, war der Browser zu → Token weg, Anmeldeseite. Ein Reload oder
    // ein zweiter Tab bleibt angemeldet (das Lebenszeichen ist dann frisch).
    let lastAliveWrite = 0;
    function aliveMark(force) {
        const t = now();
        if (!force && t - lastAliveWrite < 10000) return;
        lastAliveWrite = t;
        try { localStorage.setItem('hrLastAlive', String(t)); } catch (_) {}
    }
    const onActivity = () => { lastActivity = now(); aliveMark(false); };

    // Benutzer-Objekt: `currentUser` ist ein let in app-core.js (KEINE
    // window-Eigenschaft) — Walter-Bug 04.09.2026: window.currentUser war
    // immer undefined, der Wächter startete nie.
    function cuGet() {
        try { if (typeof currentUser !== 'undefined' && currentUser) return currentUser; } catch (_) {}
        return window.currentUser || null;
    }

    function start() {
        const cu = cuGet();
        if (!cu) return;
        const idleMin = parseInt(cu.idleTimeoutMinutes, 10);
        const maxMin  = parseInt(cu.maxSessionMinutes, 10);
        if (isNaN(idleMin) || !maxMin) return;  // ohne Policy kein Wächter

        // 0 = keine Inaktivitäts-Sperre (Walter 04.09.2026) — nur harte Obergrenze.
        idleMs = idleMin > 0 ? idleMin * 60000 : Number.POSITIVE_INFINITY;
        const startedAt = cu.sessionStartedAt ? new Date(cu.sessionStartedAt).getTime() : now();
        tokenIssuedMs = isNaN(startedAt) ? now() : startedAt;
        maxEndMs = tokenIssuedMs + maxMin * 60000;
        const hard = cu.hardEndAt ? new Date(cu.hardEndAt).getTime() : NaN;
        hardEndMs = isNaN(hard) ? tokenIssuedMs + 14 * 3600000 : hard;
        lastActivity = now();

        if (!listening) {
            listening = true;
            ACTIVITY_EVENTS.forEach(ev =>
                window.addEventListener(ev, onActivity, { passive: true, capture: true }));
        }
        if (checkTimer) clearInterval(checkTimer);
        checkTimer = setInterval(check, CHECK_MS);
        aliveMark(true);
        if (!window._sessionAliveBound) {
            window._sessionAliveBound = true;
            window.addEventListener('pagehide', () => { if (checkTimer) aliveMark(true); });
            window.addEventListener('beforeunload', () => { if (checkTimer) aliveMark(true); });
        }
        if (!window._sessionGuardVisBound) {
            window._sessionGuardVisBound = true;
            // Nach Ruhezustand / Tab-Wechsel sofort prüfen — nicht erst beim
            // nächsten Minuten-Takt (der letzte Bildschirm wäre sonst kurz sichtbar).
            document.addEventListener('visibilitychange', () => { if (!document.hidden && checkTimer) check(); });
            window.addEventListener('focus', () => { if (checkTimer) check(); });
        }
        check();
    }

    function stop() {
        if (checkTimer) { clearInterval(checkTimer); checkTimer = null; }
        if (countdownTimer) { clearInterval(countdownTimer); countdownTimer = null; }
        if (listening) {
            ACTIVITY_EVENTS.forEach(ev =>
                window.removeEventListener(ev, onActivity, { capture: true }));
            listening = false;
        }
        hideModal();
    }

    // Verbleibende Zeit bis zum Logout + Grund (idle vs. max).
    // «max» = Token-Ablauf ODER harte Obergrenze, was früher kommt.
    function remaining() {
        const idleLeft = (lastActivity + idleMs) - now();
        const maxLeft  = Math.min(maxEndMs, hardEndMs) - now();
        return maxLeft < idleLeft
            ? { left: maxLeft, reason: 'max' }
            : { left: idleLeft, reason: 'idle' };
    }

    // Gleitende Verlängerung: Token älter als 30 Min. UND in den letzten
    // 30 Min. Aktivität UND harte Obergrenze noch nicht erreicht → still
    // ein frisches Token holen. Scheitert es (Netz, 401, 409), bleibt das
    // alte Token bis zu seinem Ablauf gültig — der Wächter warnt wie bisher.
    async function maybeRefresh() {
        if (refreshing) return;
        const t = now();
        if (t - tokenIssuedMs < REFRESH_AFTER_MS) return;
        if (t - lastActivity > REFRESH_AFTER_MS) return;
        if (t >= hardEndMs - WARN_MS) return;
        if (typeof authToken === 'undefined' || !authToken) return;
        refreshing = true;
        try {
            const r = await fetch('/api/auth/refresh', { method: 'POST', headers: { 'Authorization': 'Bearer ' + authToken }, cache: 'no-store' });
            if (!r.ok) return;
            const j = await r.json();
            if (!j || !j.token) return;
            authToken = j.token;
            try { localStorage.setItem('hrToken', j.token); } catch (_) {}
            const issued = j.sessionStartedAt ? new Date(j.sessionStartedAt).getTime() : t;
            tokenIssuedMs = isNaN(issued) ? t : issued;
            const maxMin = parseInt(j.maxSessionMinutes, 10) || Math.round((maxEndMs - tokenIssuedMs) / 60000);
            maxEndMs = tokenIssuedMs + maxMin * 60000;
            // Inaktivitäts-Wert live übernehmen (Admin hat ihn evtl. geändert).
            const idleMin = parseInt(j.idleTimeoutMinutes, 10);
            if (!isNaN(idleMin)) {
                idleMs = idleMin > 0 ? idleMin * 60000 : Number.POSITIVE_INFINITY;
                const cuI = cuGet(); if (cuI) cuI.idleTimeoutMinutes = idleMin;
            }
            const hard = j.hardEndAt ? new Date(j.hardEndAt).getTime() : NaN;
            if (!isNaN(hard)) hardEndMs = hard;
            const cuR = cuGet();
            if (cuR) {
                cuR.sessionStartedAt = j.sessionStartedAt;
                cuR.hardEndAt = j.hardEndAt;
            }
        } catch (_) { /* nächster Versuch beim nächsten Check */ }
        finally { refreshing = false; }
    }

    // Heartbeat (Walter 04.09.2026, Aktive Sitzungen): jede Minute ein
    // Lebenszeichen an den Server, solange ein Token da ist (Sperrbildschirm
    // = kein Token = kein Heartbeat). aktiv=1, wenn in der letzten Minute
    // Tastatur/Maus bewegt wurde. Antwortet der Server 401 (Admin hat
    // abgemeldet), greift der globale 401-Interceptor.
    function heartbeat() {
        if (typeof authToken === 'undefined' || !authToken) return;
        const aktiv = (now() - lastActivity) <= CHECK_MS ? '1' : '0';
        try {
            fetch('/api/auth/heartbeat?aktiv=' + aktiv, { method: 'POST', headers: { 'Authorization': 'Bearer ' + authToken }, cache: 'no-store' }).catch(() => {});
        } catch (_) {}
    }

    function check() {
        aliveMark(true);
        maybeRefresh();
        heartbeat();
        const { left, reason } = remaining();
        if (left <= 0) { doExpire(reason); return; }
        if (left <= WARN_MS) showWarning();
        else hideModal();
    }

    // Ablauf: Inaktivität → Sperrbildschirm (Eingaben bleiben erhalten,
    // Walter 04.09.2026); harte Obergrenze → Abmelden + Neustart.
    function doExpire(reason) {
        stop();
        if (reason !== 'max' && window.SessionLock) { window.SessionLock.lock(); return; }
        if (typeof doLogout === 'function') doLogout();
    }

    // ── Warnmodal ─────────────────────────────────────────────────────
    let modalEl = null;
    function ensureModal() {
        if (modalEl) return modalEl;
        modalEl = document.createElement('div');
        modalEl.id = 'sessionTimeoutModal';
        modalEl.style.cssText =
            'display:none;position:fixed;inset:0;z-index:99999;background:rgba(15,23,42,0.55);' +
            'align-items:center;justify-content:center;font-family:-apple-system,system-ui,sans-serif';
        modalEl.innerHTML = `
            <div style="background:#fff;border-radius:14px;max-width:420px;width:calc(100% - 40px);
                        box-shadow:0 20px 60px rgba(0,0,0,0.3);padding:24px 26px;text-align:center">
                <div style="font-size:34px;margin-bottom:8px">🔒</div>
                <div style="font-size:17px;font-weight:700;color:#0f172a;margin-bottom:8px"
                     id="sessionTimeoutTitle">Automatische Abmeldung</div>
                <div style="font-size:14px;color:#475569;line-height:1.5;margin-bottom:18px"
                     id="sessionTimeoutMsg"></div>
                <div style="display:flex;gap:10px;justify-content:center;flex-wrap:wrap">
                    <button id="sessionTimeoutStay"
                            style="background:#1a1a1a;color:#fff;border:none;border-radius:8px;
                                   padding:10px 18px;font-size:14px;font-weight:600;cursor:pointer">
                        Angemeldet bleiben
                    </button>
                    <button id="sessionTimeoutLogout"
                            style="background:#fff;color:#475569;border:1px solid #cbd5e1;border-radius:8px;
                                   padding:10px 18px;font-size:14px;font-weight:600;cursor:pointer">
                        Jetzt abmelden
                    </button>
                </div>
            </div>`;
        document.body.appendChild(modalEl);
        modalEl.querySelector('#sessionTimeoutStay').addEventListener('click', stay);
        modalEl.querySelector('#sessionTimeoutLogout').addEventListener('click', () => doExpire('max'));
        return modalEl;
    }

    function showWarning() {
        ensureModal();
        modalEl.style.display = 'flex';
        if (!countdownTimer) {
            countdownTimer = setInterval(renderCountdown, 1000);
        }
        renderCountdown();
    }

    function renderCountdown() {
        const { left, reason } = remaining();
        if (left <= 0) { doExpire(reason); return; }
        const secs = Math.max(1, Math.ceil(left / 1000));
        // Zähler als feste Zweistellen-Anzeige in einem Feld mit fester Breite
        // (Walter 04.09.2026: unter 10 sprang der Text bei jeder Sekunde).
        const zahl = `<span style="display:inline-block;min-width:2.2ch;text-align:right;font-variant-numeric:tabular-nums;font-weight:700;color:#0f172a">${String(secs).padStart(2, '0')}</span>`;
        const msgEl   = modalEl.querySelector('#sessionTimeoutMsg');
        const stayBtn = modalEl.querySelector('#sessionTimeoutStay');
        if (reason === 'max') {
            // Max-Session: „Angemeldet bleiben" hilft nicht → ausblenden.
            msgEl.innerHTML =
                `Die maximale Sitzungsdauer (14 Stunden seit der Anmeldung) ist erreicht. Du wirst aus Sicherheitsgründen in ${zahl} Sekunden abgemeldet. Bitte danach neu anmelden.`;
            stayBtn.style.display = 'none';
        } else {
            msgEl.innerHTML =
                `OneCrew wird in ${zahl} Sekunden gesperrt (Inaktivität). Deine Eingaben bleiben erhalten — zum Entsperren Passwort oder Face ID.`;
            stayBtn.style.display = '';
        }
    }

    // „Angemeldet bleiben": NUR den Idle-Timer zurücksetzen — die Max-Session
    // bleibt unangetastet (Walter-Vorgabe).
    function stay() {
        lastActivity = now();
        // Wenn nach dem Reset noch genug Zeit ist (Idle war der Grund) → Modal zu.
        // Wenn die Max-Session der nahende Grund ist, bleibt das Modal und der
        // Countdown läuft weiter bis zum erzwungenen Logout.
        if (remaining().left > WARN_MS) hideModal();
        else renderCountdown();
    }

    function hideModal() {
        if (countdownTimer) { clearInterval(countdownTimer); countdownTimer = null; }
        if (modalEl) modalEl.style.display = 'none';
    }

    window.SessionGuard = { start, stop };
})();
