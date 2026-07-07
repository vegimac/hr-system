// ══════════════════════════════════════════════════════════════════════
// session-timeout.js — benutzerbezogener Session-/Logout-Wächter
// ──────────────────────────────────────────────────────────────────────
// Walter-Vorgabe 21.06.2026. Werte kommen pro Benutzer vom Backend
// (currentUser.idleTimeoutMinutes / maxSessionMinutes / sessionStartedAt;
// leer = Rollen-Default, bereits server-seitig aufgelöst).
//
//   • Inaktivitäts-Logout nach idleTimeoutMinutes (Aktivität setzt ihn zurück)
//   • Harte Max-Session nach maxSessionMinutes ab Login (NICHT verlängerbar)
//   • Warnmodal 60 s vorher mit „Angemeldet bleiben" (setzt NUR den Idle-Timer
//     zurück, verlängert die Max-Session nicht)
//   • Bei Ablauf der Max-Session ist ein Neu-Login zwingend.
//
// Der Server erzwingt die Max-Session zusätzlich hart über die JWT-Ablaufzeit.
// ══════════════════════════════════════════════════════════════════════
(function () {
    const CHECK_MS = 30000;   // Prüf-Intervall (Walter-Vorgabe: alle 30 s)
    const WARN_MS  = 60000;   // Warnmodal 1 Minute vorher
    const ACTIVITY_EVENTS = ['mousemove', 'keydown', 'click', 'touchstart', 'scroll'];

    let idleMs = 0;
    let maxEndMs = 0;
    let lastActivity = 0;
    let checkTimer = null;
    let countdownTimer = null;
    let listening = false;

    const now = () => Date.now();
    const onActivity = () => { lastActivity = now(); };

    function start() {
        const cu = window.currentUser;
        if (!cu) return;
        const idleMin = parseInt(cu.idleTimeoutMinutes, 10);
        const maxMin  = parseInt(cu.maxSessionMinutes, 10);
        if (!idleMin || !maxMin) return;  // ohne Policy kein Wächter

        idleMs = idleMin * 60000;
        const startedAt = cu.sessionStartedAt ? new Date(cu.sessionStartedAt).getTime() : now();
        maxEndMs = (isNaN(startedAt) ? now() : startedAt) + maxMin * 60000;
        lastActivity = now();

        if (!listening) {
            listening = true;
            ACTIVITY_EVENTS.forEach(ev =>
                window.addEventListener(ev, onActivity, { passive: true, capture: true }));
        }
        if (checkTimer) clearInterval(checkTimer);
        checkTimer = setInterval(check, CHECK_MS);
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
    function remaining() {
        const idleLeft = (lastActivity + idleMs) - now();
        const maxLeft  = maxEndMs - now();
        return maxLeft < idleLeft
            ? { left: maxLeft, reason: 'max' }
            : { left: idleLeft, reason: 'idle' };
    }

    function check() {
        const { left } = remaining();
        if (left <= 0) { doExpire(); return; }
        if (left <= WARN_MS) showWarning();
        else hideModal();
    }

    function doExpire() {
        stop();
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
        modalEl.querySelector('#sessionTimeoutLogout').addEventListener('click', doExpire);
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
        if (left <= 0) { doExpire(); return; }
        const secs = Math.max(1, Math.ceil(left / 1000));
        const msgEl   = modalEl.querySelector('#sessionTimeoutMsg');
        const stayBtn = modalEl.querySelector('#sessionTimeoutStay');
        if (reason === 'max') {
            // Max-Session: „Angemeldet bleiben" hilft nicht → ausblenden.
            msgEl.textContent =
                `Die maximale Sitzungsdauer ist erreicht. Du wirst aus Sicherheitsgründen in ${secs} Sekunden abgemeldet. Bitte danach neu anmelden.`;
            stayBtn.style.display = 'none';
        } else {
            msgEl.textContent =
                `Du wirst aus Sicherheitsgründen in ${secs} Sekunden abgemeldet.`;
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
