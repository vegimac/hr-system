// ══════════════════════════════════════════════════════════════════════
// session-lock.js — Sperrbildschirm (Walter 04.09.2026)
// ──────────────────────────────────────────────────────────────────────
// Der GF erfasst etwas, wird weggerufen, nach X Minuten ohne Aktivität
// legt sich ein blickdichter Sperrbildschirm über OneCrew. Das Token wird
// im Browser gelöscht (kein Server-Aufruf mehr möglich), die Seite dahinter
// bleibt aber samt offenem Formular erhalten. Passwort oder Passkey der
// GLEICHEN Person → neues Token → Sperre weg → weiter, wo man war.
// Andere Person → kompletter Neustart (Reload), nichts vom Vorgänger sichtbar.
// Testmodus (impersoniert) → kein Sperren, sondern wie bisher Abmelden.
// ══════════════════════════════════════════════════════════════════════
(function () {
    let el = null;
    let locked = false;

    function esc(t) { return String(t ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }

    function ensure() {
        if (el) return el;
        el = document.createElement('div');
        el.id = 'sessionLock';
        el.style.cssText = 'display:none;position:fixed;inset:0;z-index:100000;background:#e9e6df;align-items:center;justify-content:center;font-family:-apple-system,system-ui,sans-serif';
        el.innerHTML = `
            <div style="width:min(420px,calc(100% - 40px));background:#faf8f5;border:1px solid rgba(255,255,255,0.7);border-radius:18px;box-shadow:0 25px 60px rgba(60,55,48,0.22);padding:28px 30px;text-align:center">
                <div style="font-size:36px;margin-bottom:6px">🔒</div>
                <div style="font-size:18px;font-weight:700;color:#1a1a1a">OneCrew ist gesperrt</div>
                <div id="sessionLockWho" style="font-size:13px;color:#646464;margin:6px 0 18px"></div>
                <form id="sessionLockForm" autocomplete="on" style="display:flex;flex-direction:column;gap:10px">
                    <input type="text" id="sessionLockEmail" name="username" autocomplete="username" style="position:absolute;left:-9999px;width:1px;height:1px;opacity:0" tabindex="-1" aria-hidden="true">
                    <input type="password" id="sessionLockPw" name="password" autocomplete="current-password" placeholder="Passwort"
                           style="width:100%;box-sizing:border-box;padding:12px 14px;border:1px solid rgba(60,55,48,0.2);border-radius:10px;font-size:15px;background:#fff">
                    <button type="submit" style="background:#1a1a1a;color:#fff;border:none;border-radius:12px;padding:12px 18px;font-size:14px;font-weight:600;cursor:pointer">Entsperren</button>
                    <button type="button" id="sessionLockPasskey" style="display:none;background:transparent;border:1px solid #bfbfbf;border-radius:12px;padding:11px 18px;font-size:14px;color:#1a1a1a;cursor:pointer">Mit Face ID / Touch ID entsperren</button>
                    <div id="sessionLockErr" style="display:none;color:#b91c1c;font-size:12.5px"></div>
                </form>
                <div style="margin-top:16px;font-size:12px;color:#8b8b8b">Ungespeicherte Eingaben bleiben erhalten, solange du dich als dieselbe Person wieder anmeldest.</div>
                <button type="button" id="sessionLockLogout" style="margin-top:10px;background:none;border:none;color:#8b8b8b;font-size:12.5px;text-decoration:underline;cursor:pointer">Abmelden und neu starten</button>
            </div>`;
        document.body.appendChild(el);
        el.querySelector('#sessionLockForm').addEventListener('submit', (e) => { e.preventDefault(); unlockWithPassword(); });
        el.querySelector('#sessionLockLogout').addEventListener('click', () => { if (typeof doLogout === 'function') doLogout(); else location.reload(); });
        const pk = el.querySelector('#sessionLockPasskey');
        try { if (typeof webauthnSupported === 'function' && webauthnSupported()) pk.style.display = ''; } catch (_) {}
        pk.addEventListener('click', unlockWithPasskey);
        return el;
    }

    function showErr(msg) {
        const e = el.querySelector('#sessionLockErr');
        e.textContent = msg; e.style.display = 'block';
    }

    // Sperren: Token weg (Server unerreichbar), Wächter aus, Overlay drüber.
    function lock() {
        if (locked) return;
        const cu = window.currentUser;
        // Testmodus → wie bisher komplett abmelden (Token gehört dem Admin).
        if (!cu || cu.impersonating === true || localStorage.getItem('hrTokenAdmin')) {
            if (typeof doLogout === 'function') doLogout(); else location.reload();
            return;
        }
        locked = true;
        try { authToken = null; } catch (_) {}
        try { localStorage.removeItem('hrToken'); } catch (_) {}
        if (window.SessionGuard) window.SessionGuard.stop();
        ensure();
        el.querySelector('#sessionLockWho').textContent = `${cu.firstName || cu.username || ''} — gesperrt nach Inaktivität`;
        el.querySelector('#sessionLockEmail').value = cu.email || '';
        el.querySelector('#sessionLockPw').value = '';
        el.querySelector('#sessionLockErr').style.display = 'none';
        el.style.display = 'flex';
        document.body.classList.add('session-locked');
        setTimeout(() => el.querySelector('#sessionLockPw').focus(), 50);
    }

    async function unlockWithPassword() {
        const cu = window.currentUser;
        const pw = el.querySelector('#sessionLockPw').value;
        if (!pw) return;
        try {
            const res = await fetch('/api/auth/login', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email: cu.email, password: pw }),
            });
            const data = await res.json().catch(() => ({}));
            if (!res.ok) { showErr(data.message || 'Anmeldung fehlgeschlagen.'); return; }
            applyUnlock(data);
        } catch (e) { showErr('Verbindungsfehler: ' + e.message); }
    }

    async function unlockWithPasskey() {
        try {
            const data = await webauthnLoginRaw();
            applyUnlock(data);
        } catch (e) {
            if (e && (e.name === 'NotAllowedError' || e.name === 'AbortError')) return;
            showErr(e.message || 'Face-ID-Anmeldung fehlgeschlagen.');
        }
    }

    // Neues Token übernehmen — nur wenn es dieselbe Person ist.
    function applyUnlock(data) {
        const cu = window.currentUser;
        if (!data || !data.token) { showErr('Keine Antwort vom Server.'); return; }
        if (!data.user || data.user.id !== cu.id) {
            // Andere Person: nichts vom Vorgänger zeigen → Neustart mit dem neuen Token.
            try { localStorage.setItem('hrToken', data.token); } catch (_) {}
            location.reload();
            return;
        }
        authToken = data.token;
        try { localStorage.setItem('hrToken', data.token); } catch (_) {}
        cu.sessionStartedAt = data.sessionStartedAt;
        cu.loginAt = data.loginAt;
        cu.hardEndAt = data.hardEndAt;
        if (data.idleTimeoutMinutes != null) cu.idleTimeoutMinutes = data.idleTimeoutMinutes;
        if (data.maxSessionMinutes != null) cu.maxSessionMinutes = data.maxSessionMinutes;
        locked = false;
        el.style.display = 'none';
        el.querySelector('#sessionLockPw').value = '';
        document.body.classList.remove('session-locked');
        if (window.SessionGuard) window.SessionGuard.start();
    }

    window.SessionLock = { lock, isLocked: () => locked };
})();
