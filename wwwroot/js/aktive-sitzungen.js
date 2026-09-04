// Aktive Sitzungen (Walter 04.09.2026): System › Aktive Sitzungen — wer ist
// gerade angemeldet, seit wann, letzte Aktivität, Gerät — und «Abmelden»
// pro Benutzer (setzt einen Sperrvermerk: alle bisherigen Tokens dieses
// Benutzers sind ungültig, sein Browser meldet «vom Administrator abgemeldet»).
// Quelle: SessionRegistry (Speicher) — gefüttert vom Minuten-Heartbeat des
// Session-Wächters und jedem API-Zugriff. Auto-Refresh alle 30 s, solange
// die Seite offen ist.

let _asTimer = null;
let _asRows = [];

function asInit() {
    asLoad();
    if (_asTimer) clearInterval(_asTimer);
    _asTimer = setInterval(() => {
        const pg = document.getElementById('page-aktive-sitzungen');
        if (!pg || !pg.classList.contains('active')) {
            clearInterval(_asTimer); _asTimer = null; return;
        }
        asLoad();
    }, 30000);
}

function asChDateTime(iso) {
    if (!iso) return '–';
    const d = new Date(iso);
    if (isNaN(d)) return '–';
    const p = n => String(n).padStart(2, '0');
    return `${p(d.getDate())}.${p(d.getMonth() + 1)}.${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

function asVor(iso, serverIso) {
    if (!iso) return '–';
    const t = new Date(iso).getTime();
    const s = serverIso ? new Date(serverIso).getTime() : Date.now();
    const min = Math.max(0, Math.round((s - t) / 60000));
    if (min < 1) return 'gerade eben';
    if (min < 60) return `vor ${min} Min.`;
    const h = Math.floor(min / 60), m = min % 60;
    return `vor ${h} Std. ${String(m).padStart(2, '0')} Min.`;
}

function asRolle(r) {
    return ({ admin: 'Admin', superuser: 'Superuser', user: 'GF', buchhaltung: 'Buchhaltung', employee: 'MA-Postfach' })[r] || (r || '–');
}

function asGeraet(ua) {
    if (!ua) return '–';
    let os = 'Gerät';
    if (/iPhone/i.test(ua)) os = 'iPhone';
    else if (/iPad/i.test(ua)) os = 'iPad';
    else if (/Android/i.test(ua)) os = 'Android';
    else if (/Mac OS X/i.test(ua)) os = 'Mac';
    else if (/Windows/i.test(ua)) os = 'Windows';
    else if (/Linux/i.test(ua)) os = 'Linux';
    let br = '';
    if (/Edg\//i.test(ua)) br = 'Edge';
    else if (/OPR\//i.test(ua)) br = 'Opera';
    else if (/Chrome\//i.test(ua)) br = 'Chrome';
    else if (/Firefox\//i.test(ua)) br = 'Firefox';
    else if (/Safari\//i.test(ua)) br = 'Safari';
    return br ? `${os} · ${br}` : os;
}

async function asLoad() {
    const body = document.getElementById('asBody');
    if (!body) return;
    try {
        const r = await fetch('/api/admin/sessions', { headers: ah() });
        if (!r.ok) { body.innerHTML = `<tr><td colspan="8" style="color:#991b1b;padding:14px">Fehler ${r.status} beim Laden.</td></tr>`; return; }
        const j = await r.json();
        _asRows = j.sitzungen || [];
        asRender(j.serverZeit);
    } catch (e) {
        body.innerHTML = `<tr><td colspan="8" style="color:#991b1b;padding:14px">${esc(e.message || String(e))}</td></tr>`;
    }
}

function asRender(serverZeit) {
    const body = document.getElementById('asBody');
    const info = document.getElementById('asInfo');
    if (!body) return;
    const online = _asRows.filter(x => x.online).length;
    if (info) info.textContent = `${_asRows.length} Sitzung${_asRows.length === 1 ? '' : 'en'} · ${online} online · Stand ${asChDateTime(serverZeit)}`;
    if (!_asRows.length) {
        body.innerHTML = '<tr><td colspan="8" style="color:#6b7280;padding:18px 14px">Keine aktiven Sitzungen bekannt. Die Liste füllt sich mit dem nächsten Zugriff jedes Benutzers (nach einem Neustart des Servers ist sie leer).</td></tr>';
        return;
    }
    body.innerHTML = _asRows.map(s => {
        const status = s.online
            ? '<span class="as-status as-online">● online</span>'
            : '<span class="as-status as-offline">○ inaktiv / gesperrt</span>';
        const test = s.impersonatedBy
            ? `<div style="font-size:11px;color:#b45309;font-weight:600;margin-top:2px">Testmodus · von ${esc(s.impersonatedByName || ('#' + s.impersonatedBy))}</div>` : '';
        const eigene = s.istEigene ? '<div style="font-size:11px;color:#6b7280;margin-top:2px">deine Sitzung</div>' : '';
        return `<tr>
            <td><div style="font-weight:700;color:#0f172a">${esc(s.name || s.username)}</div>
                <div style="font-size:11px;color:#6b7280">${esc(s.username || '')}</div>${test}${eigene}</td>
            <td>${esc(asRolle(s.role))}</td>
            <td>${status}</td>
            <td>${asChDateTime(s.loginAt)}</td>
            <td title="${esc(s.lastPath || '')}">${asVor(s.lastActivity, serverZeit)}<div style="font-size:11px;color:#6b7280">${asChDateTime(s.lastActivity)}</div></td>
            <td title="${esc(s.userAgent || '')}">${esc(asGeraet(s.userAgent))}<div style="font-size:11px;color:#6b7280">${esc(s.ip || '')}</div></td>
            <td>${s.idleTimeoutMinutes == null ? '15 (Std.)' : (s.idleTimeoutMinutes === 0 ? 'aus' : s.idleTimeoutMinutes)}</td>
            <td style="text-align:right">
                <button class="qst-warum-btn" onclick="asAbmelden(${s.userId}, '${esc(s.name || s.username)}', ${s.istEigene ? 'true' : 'false'})">Abmelden</button>
            </td>
        </tr>`;
    }).join('');
}

async function asAbmelden(userId, name, eigene) {
    const msg = eigene
        ? `Das ist deine eigene Sitzung. Du wirst sofort abgemeldet und musst dich neu anmelden. Fortfahren?`
        : `${name} jetzt abmelden?\n\nAlle laufenden Sitzungen dieses Benutzers werden beendet — er landet beim nächsten Klick auf dem Anmeldebildschirm. Nicht gespeicherte Eingaben gehen verloren.`;
    const ok = (typeof liquidConfirm === 'function')
        ? await liquidConfirm(msg, { yesLabel: 'Abmelden' })
        : confirm(msg);
    if (!ok) return;
    try {
        const r = await fetch(`/api/admin/sessions/${userId}/logout`, { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { showToast(j.message || `Fehler ${r.status}`, 'error'); return; }
        showToast(j.message || 'Abgemeldet.', 'success');
        if (j.selbst) { setTimeout(() => { if (typeof doLogout === 'function') doLogout(); }, 800); return; }
        asLoad();
    } catch (e) {
        showToast(e.message || String(e), 'error');
    }
}
