// ══════════════════════════════════════════════════════════════════════
//  HR-BÜRO-KALENDER FÜR VORSTELLUNGSGESPRÄCHE (Walter-Vorgabe 09.08.2026)
//  Ersetzt den früheren GF-Zeitfenster-Prozess (Code in der Git-Historie;
//  die alten Endpunkte interview-fenster/-termin bleiben serverseitig
//  erhalten, sind aber ohne UI-Zugang).
//
//  HR pflegt hier Termine mit Anzahl verfügbarer Plätze — maximal 2 Monate
//  im Voraus — und bucht beim Einladen eines Kandidaten selbst einen Platz.
//  Einstieg: HR-Hub → Karte «Vorstellungsgespräche» (admin/superuser).
// ══════════════════════════════════════════════════════════════════════

const _ivWd = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];
const _ivMon = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];

function _ivFmtD(iso) { return iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : ''; }
function _ivWdOf(iso) { return _ivWd[new Date(iso + 'T00:00:00').getDay()]; }
function _ivEsc(s) { return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;'); }
function _ivIsoToday() {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
function _ivIsoMax() {
    const d = new Date();
    d.setMonth(d.getMonth() + 2);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

const _ivInp = 'background:#fff;border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:6px 10px;font-size:13px;color:#3f3f3f';
const _ivBtnDark = 'background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 14px;font-size:13px;font-weight:600;cursor:pointer';
const _ivBtnLight = 'background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:7px 12px;font-size:13px;cursor:pointer;color:#3f3f3f';

let _hrIvList = [];        // Termine ab heute (mit Buchungen)
let _hrIvSelDay = null;    // gewählter Tag (iso)
let _hrIvKandidaten = [];  // MA-Eintritte des gewählten Monats (für «Platz buchen» = Einladung)
let _hrIvMonOffset = 0;    // Eintrittsmonat-Offset (−1…+2) im Buchen-Formular

function _ivModalShell(id, titel, maxWidth) {
    if (document.getElementById(id)) return;
    const div = document.createElement('div');
    div.id = id;
    div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center;padding:16px';
    div.innerHTML = `
    <div class="iv-modal-box" style="border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:${maxWidth || 620}px;width:96%;max-height:92vh;overflow-y:auto;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:10px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">${titel}</div>
            <button type="button" onclick="document.getElementById('${id}').style.display='none'" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
        </div>
        <div id="${id}Body" style="font-size:13px;color:#3f3f3f"></div>
    </div>`;
    div.onclick = (e) => { if (e.target === div) div.style.display = 'none'; };
    document.body.appendChild(div);
}

// ── Einstieg (HR-Hub-Karte) ─────────────────────────────────────────────
function hrIvOpen() {
    _ivModalShell('hrIvModal', '🚀 Onboarding — HR-Büro-Kalender', 780);
    document.getElementById('hrIvModal').style.display = 'flex';
    hrIvReload();
}

async function hrIvReload() {
    const body = document.getElementById('hrIvModalBody');
    if (!body) return;
    body.innerHTML = '<span style="color:#8b8b8b">Wird geladen…</span>';
    try {
        const r = await fetch('/api/hr-interview/termine', { headers: ah() });
        _hrIvList = await r.json();
        if (!r.ok) { body.innerHTML = 'Laden fehlgeschlagen.'; return; }
        if (_hrIvSelDay && _hrIvSelDay < _ivIsoToday()) _hrIvSelDay = null;
        _hrIvRender();
    } catch (_) {
        body.innerHTML = '<span style="color:#991b1b">Verbindungsfehler.</span>';
    }
}

// ── Kalender: heute bis +2 Monate (3 Monats-Raster) ─────────────────────
function _hrIvRender() {
    const body = document.getElementById('hrIvModalBody');
    if (!body) return;
    const heute = _ivIsoToday();
    const max = _ivIsoMax();

    // Termine pro Tag: frei-Plätze summieren.
    const proTag = {};
    for (const t of _hrIvList) {
        const frei = t.plaetze - (t.buchungen || []).length;
        const e = (proTag[t.datum] = proTag[t.datum] || { termine: 0, frei: 0 });
        e.termine++;
        e.frei += Math.max(0, frei);
    }

    const now = new Date();
    let months = '';
    for (let m = 0; m < 3; m++) {
        const y = now.getFullYear() + Math.floor((now.getMonth() + m) / 12);
        const mo = (now.getMonth() + m) % 12;
        const tage = new Date(y, mo + 1, 0).getDate();
        const first = new Date(y, mo, 1);
        const lead = (first.getDay() + 6) % 7;   // Mo-basiert
        let cells = '';
        for (let i = 0; i < lead; i++) cells += '<div></div>';
        for (let t = 1; t <= tage; t++) {
            const iso = `${y}-${String(mo + 1).padStart(2, '0')}-${String(t).padStart(2, '0')}`;
            const info = proTag[iso];
            const aktiv = iso >= heute && iso <= max;
            const sel = iso === _hrIvSelDay;
            const bg = sel ? '#3f3f3f' : info ? (info.frei > 0 ? '#dcfce7' : '#fecaca') : (aktiv ? '#fff' : '#f1efe9');
            const fg = sel ? '#fff' : aktiv ? '#3f3f3f' : '#c2beb5';
            cells += `<div ${aktiv ? `onclick="hrIvDay('${iso}')" style="cursor:pointer;` : 'style="'}
                background:${bg};color:${fg};border:1px solid rgba(60,55,48,0.12);border-radius:8px;
                padding:3px 0;text-align:center;font-size:12px;font-weight:600;position:relative">
                ${t}${info ? `<div style="font-size:9px;font-weight:700;color:${sel ? '#fff' : info.frei > 0 ? '#166534' : '#991b1b'}">${info.frei} frei</div>` : ''}
            </div>`;
        }
        months += `
            <div style="min-width:210px;flex:1">
                <div style="font-weight:800;margin-bottom:4px;color:#3f3f3f">${_ivMon[mo]} ${y}</div>
                <div style="display:grid;grid-template-columns:repeat(7,1fr);gap:3px;font-size:10px;color:#8b8b8b;margin-bottom:3px">
                    <div>Mo</div><div>Di</div><div>Mi</div><div>Do</div><div>Fr</div><div>Sa</div><div>So</div></div>
                <div style="display:grid;grid-template-columns:repeat(7,1fr);gap:3px">${cells}</div>
            </div>`;
    }

    body.innerHTML = `
        <p style="margin:0 0 10px;color:#646464">Tag anklicken → Termine mit Plätzen erfassen (max. 2 Monate im Voraus)
        und die gebuchten MA sehen. Eingeladen (mit Termin-Buchung) wird unter
        <b>«2 · MA zum Onboarding einladen»</b>.
        <span style="color:#166534;font-weight:700">grün</span> = freie Plätze,
        <span style="color:#991b1b;font-weight:700">rot</span> = ausgebucht.</p>
        <div style="display:flex;gap:14px;flex-wrap:wrap">${months}</div>
        <div id="hrIvDayDetail" style="margin-top:14px"></div>`;
    if (_hrIvSelDay) _hrIvRenderDay();
}

function hrIvDay(iso) {
    _hrIvSelDay = iso;
    _hrIvRender();
}

// ── Tages-Detail: Termine + Buchungen + Erfassen ────────────────────────
function _hrIvRenderDay() {
    const el = document.getElementById('hrIvDayDetail');
    if (!el || !_hrIvSelDay) return;
    const termine = _hrIvList.filter(t => t.datum === _hrIvSelDay);

    const rows = termine.map(t => {
        const belegt = (t.buchungen || []).length;
        const frei = t.plaetze - belegt;
        const buchungen = (t.buchungen || []).map(b => `
            <div style="display:flex;align-items:center;gap:8px;padding:2px 0 2px 16px;font-size:12.5px;flex-wrap:wrap">
                <span>👤 ${_ivEsc(b.kandidat)}${b.telefon ? ' · ' + _ivEsc(b.telefon) : ''}${b.bemerkung ? ' · ' + _ivEsc(b.bemerkung) : ''}</span>
                ${b.maAntwort === 'ANGENOMMEN'
                    ? '<span style="background:#dcfce7;color:#166534;border-radius:8px;padding:1px 8px;font-size:11px;font-weight:700" title="Der MA hat den Termin über den Vertrags-Link bestätigt">✓ bestätigt</span>'
                    : '<span style="background:#f1efe9;color:#8b8b8b;border-radius:8px;padding:1px 8px;font-size:11px" title="Der MA hat den Termin noch nicht über den Vertrags-Link bestätigt">⏳ unbestätigt</span>'}
                <a onclick="hrIvUmbuchen(${b.id}, ${t.id})" style="cursor:pointer;color:#1d4ed8;font-weight:700;font-size:12px" title="Nach telefonischer Absprache auf einen anderen Termin verschieben — der Vertrags-Link zeigt danach den neuen Termin">⇄ Umbuchen</a>
                <a onclick="hrIvAbsagen(${b.id})" style="cursor:pointer;color:#991b1b;font-weight:700" title="Buchung absagen">✕</a>
                <span id="hrIvUb${b.id}" style="display:none;align-items:center;gap:6px"></span>
            </div>`).join('');
        return `
            <div style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.14);border-radius:12px;padding:8px 10px;margin-bottom:8px">
                <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
                    <b>🕐 ${t.von}${t.bis ? ' – ' + t.bis : ''}</b>
                    <span style="background:${frei > 0 ? '#dcfce7' : '#fecaca'};border-radius:8px;padding:1px 8px;font-size:11.5px;color:${frei > 0 ? '#166534' : '#991b1b'}">
                        ${frei} von ${t.plaetze} Plätzen frei</span>
                    <span style="color:#8b8b8b">${_ivEsc(t.bemerkung || '')}</span>
                    <span style="flex:1"></span>
                    <button onclick="hrIvEditTermin(${t.id})" style="background:#fff;border:1px solid #cbd5e1;border-radius:6px;padding:2px 8px;font-size:12px;cursor:pointer;color:#3f3f3f" title="Termin bearbeiten">✎</button>
                    ${belegt === 0 ? `<button onclick="hrIvDeleteTermin(${t.id})" style="background:#fff;border:1px solid #cbd5e1;border-radius:6px;padding:2px 8px;font-size:12px;cursor:pointer;color:#991b1b" title="Löschen (nur ohne Buchungen)">🗑</button>` : ''}
                </div>
                ${buchungen || '<div style="padding:2px 0 2px 16px;font-size:12px;color:#b0aca4">Noch niemand gebucht.</div>'}
                <div id="hrIvEdit${t.id}"></div>
            </div>`;
    }).join('');

    el.innerHTML = `
        <div style="font-weight:800;margin-bottom:6px;color:#3f3f3f">${_ivWdOf(_hrIvSelDay)}, ${_ivFmtD(_hrIvSelDay)}</div>
        ${rows || '<div style="color:#8b8b8b;margin-bottom:8px">Noch keine Termine an diesem Tag.</div>'}
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;background:rgba(255,255,255,0.45);border:1px solid rgba(255,255,255,0.62);border-radius:12px;padding:10px">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Von
                <input id="hrIvNeuVon" type="time" style="${_ivInp}"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Bis (optional)
                <input id="hrIvNeuBis" type="time" style="${_ivInp}"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Plätze
                <input id="hrIvNeuPlaetze" type="number" min="1" max="50" value="1" style="${_ivInp};width:70px"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Bemerkung
                <input id="hrIvNeuBem" placeholder="optional" style="${_ivInp};min-width:130px"></label>
            <button onclick="hrIvAddTermin()" style="${_ivBtnDark}">+ Termin anlegen</button>
        </div>`;
}

async function hrIvAddTermin() {
    const dto = {
        datum: _hrIvSelDay,
        von: document.getElementById('hrIvNeuVon')?.value,
        bis: document.getElementById('hrIvNeuBis')?.value || null,
        plaetze: parseInt(document.getElementById('hrIvNeuPlaetze')?.value, 10) || 0,
        bemerkung: document.getElementById('hrIvNeuBem')?.value || null,
    };
    if (!dto.datum || !dto.von || dto.plaetze < 1) { showToast('Von-Zeit und Plätze angeben.', 'error'); return; }
    const r = await fetch('/api/hr-interview/termine', { method: 'POST', headers: ah(), body: JSON.stringify(dto) });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen.', 'error'); return; }
    hrIvReload();
}

// Termin bearbeiten — Walter 10.08.2026: Zeit nur solange NIEMAND gebucht ist
// (Eingeladene haben die Zeit erhalten); Plätze runter nur bis Anzahl Gebuchte.
function hrIvEditTermin(id) {
    document.querySelectorAll('[id^="hrIvEdit"]').forEach(e => { e.innerHTML = ''; });
    const t = _hrIvList.find(x => x.id === id);
    const el = document.getElementById(`hrIvEdit${id}`);
    if (!t || !el) return;
    const belegt = (t.buchungen || []).length;
    const zeitLock = belegt > 0 ? ' readonly style="' + _ivInp + ';background:#f1efe9;color:#8b8b8b;pointer-events:none"' : ` style="${_ivInp}"`;
    el.innerHTML = `
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;background:rgba(255,255,255,0.7);border:1px solid rgba(60,55,48,0.15);border-radius:10px;padding:8px;margin-top:6px">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Von${belegt > 0 ? ' 🔒' : ''}
                <input id="hrIvEdVon" type="time" value="${t.von}"${zeitLock}></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Bis (optional)${belegt > 0 ? ' 🔒' : ''}
                <input id="hrIvEdBis" type="time" value="${t.bis || ''}"${zeitLock}></label>
            ${belegt > 0 ? `<span style="font-size:11px;color:#854d0e;background:#fef9c3;border:1px solid #fde68a;border-radius:8px;padding:4px 8px;align-self:center">Zeit gesperrt — ${belegt} MA eingeladen. Verschieben = pro MA «⇄ Umbuchen» (nach Telefonat).</span>` : ''}
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Plätze
                <input id="hrIvEdPlaetze" type="number" min="1" max="50" value="${t.plaetze}" style="${_ivInp};width:70px"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Bemerkung
                <input id="hrIvEdBem" value="${_ivEsc(t.bemerkung || '')}" style="${_ivInp};min-width:130px"></label>
            <button onclick="hrIvSaveTermin(${id})" style="${_ivBtnDark}">Speichern</button>
            <button onclick="document.getElementById('hrIvEdit${id}').innerHTML=''" style="${_ivBtnLight}">Abbrechen</button>
        </div>`;
}

async function hrIvSaveTermin(id) {
    const dto = {
        datum: null,
        von: document.getElementById('hrIvEdVon')?.value,
        bis: document.getElementById('hrIvEdBis')?.value || null,
        plaetze: parseInt(document.getElementById('hrIvEdPlaetze')?.value, 10) || 0,
        bemerkung: document.getElementById('hrIvEdBem')?.value || null,
    };
    if (!dto.von || dto.plaetze < 1) { showToast('Von-Zeit und Plätze angeben.', 'error'); return; }
    const r = await fetch(`/api/hr-interview/termine/${id}`, { method: 'PUT', headers: ah(), body: JSON.stringify(dto) });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen.', 'error'); return; }
    showToast('Termin angepasst.', 'success');
    hrIvReload();
}

async function hrIvDeleteTermin(id) {
    if (typeof liquidConfirm === 'function' && !await liquidConfirm('Diesen Termin löschen?', { title: 'HR-Kalender' })) return;
    const r = await fetch(`/api/hr-interview/termine/${id}`, { method: 'DELETE', headers: ah() });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || 'Löschen fehlgeschlagen.', 'error'); return; }
    hrIvReload();
}

function hrIvPick(terminId) {
    document.querySelectorAll('[id^="hrIvBookForm"]').forEach(e => { e.innerHTML = ''; });
    const el = document.getElementById(`hrIvBookForm${terminId}`);
    if (!el) return;
    // Einladungs-Flow direkt im Buchen (Walter 10.08.2026): Eintrittsmonat
    // wählen (−1…+2) → MA über ALLE Filialen, sortiert nach Eintrittsdatum,
    // Filiale hinter dem Namen. Buchen + Vertrags-SMS in EINEM Schritt.
    // Freitext bleibt für externe Kandidaten.
    const monNamen = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
    const now = new Date();
    const monOpts = [-1, 0, 1, 2].map(off => {
        const d = new Date(now.getFullYear(), now.getMonth() + off, 1);
        return `<option value="${off}"${off === _hrIvMonOffset ? ' selected' : ''}>${monNamen[d.getMonth()]} ${d.getFullYear()}</option>`;
    }).join('');
    el.innerHTML = `
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;background:rgba(255,255,255,0.7);border:1px solid rgba(60,55,48,0.15);border-radius:10px;padding:8px;margin-top:6px">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Eintrittsmonat
                <select id="hrIvMon" onchange="_hrIvMonOffset=parseInt(this.value,10);hrIvLoadKand(${terminId})" style="${_ivInp}">${monOpts}</select></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Mitarbeiter (bucht + lädt per Vertrags-SMS ein)
                <select id="hrIvMa" onchange="document.getElementById('hrIvFreitext').style.display=this.value?'none':'flex'" style="${_ivInp};min-width:300px">
                    <option value="">Wird geladen…</option>
                </select></label>
            <span id="hrIvFreitext" style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end">
                <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Kandidat/in
                    <input id="hrIvKand" style="${_ivInp};min-width:160px"></label>
                <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Telefon
                    <input id="hrIvTel" style="${_ivInp};width:130px"></label>
            </span>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Bemerkung
                <input id="hrIvBem" style="${_ivInp};min-width:130px"></label>
            <button onclick="hrIvBook(${terminId})" style="${_ivBtnDark}">Buchen</button>
            <button onclick="this.closest('div[id^=hrIvBookForm]').innerHTML=''" style="${_ivBtnLight}">Abbrechen</button>
        </div>`;
    hrIvLoadKand(terminId);
}

// MA-Eintritte des gewählten Monats laden (alle Filialen, sortiert nach Eintritt).
async function hrIvLoadKand(terminId) {
    const sel = document.getElementById('hrIvMa');
    if (!sel) return;
    const now = new Date();
    const d = new Date(now.getFullYear(), now.getMonth() + _hrIvMonOffset, 1);
    try {
        const r = await fetch(`/api/contract-share/onboarding-einladungen?year=${d.getFullYear()}&month=${d.getMonth() + 1}`, { headers: ah() });
        _hrIvKandidaten = r.ok ? await r.json() : [];
    } catch (_) { _hrIvKandidaten = []; }
    const maOpts = _hrIvKandidaten.map(k =>
        `<option value="${k.employeeId}">${_ivEsc(k.name)} — Eintritt ${_ivFmtD(k.eintritt)} · ${_ivEsc(k.filiale)}${k.wunschTermin ? ' · ★ Wunsch: ' + _ivEsc(k.wunschTermin) : ''}${k.gesendetAm ? ' · bereits eingeladen' : ''}</option>`).join('');
    sel.innerHTML = `<option value="">— externer Kandidat (Freitext) —</option>${maOpts}`;
    const ft = document.getElementById('hrIvFreitext');
    if (ft) ft.style.display = 'flex';
}

async function hrIvBook(terminId) {
    const maId = parseInt(document.getElementById('hrIvMa')?.value, 10) || null;

    if (maId) {
        // MA gewählt: Platz buchen + Vertrags-SMS mit Termin am Link — in einem
        // Schritt über contract-share/send (bucht serverseitig, Landing-Page
        // zeigt Datum/Zeit + «In Kalender speichern»).
        const k = _hrIvKandidaten.find(x => x.employeeId === maId);
        if (typeof liquidConfirm === 'function'
            && !await liquidConfirm(`${k?.name || 'MA'} auf diesen Termin buchen und per Vertrags-SMS einladen?`, { title: 'Onboarding-Einladung' })) return;
        const r = await fetch('/api/contract-share/send', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({ employeeId: maId, terminId }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { showToast(j.error || j.message || 'Einladung fehlgeschlagen.', 'error'); return; }
        showToast(`Gebucht + Einladung an ${j.to} gesendet.` + (j.redirectedTo ? ` (Test-Umleitung: ${j.redirectedTo})` : ''), 'success');
        hrIvReload();
        return;
    }

    const dto = {
        kandidat: (document.getElementById('hrIvKand')?.value || '').trim(),
        telefon: document.getElementById('hrIvTel')?.value || null,
        bemerkung: document.getElementById('hrIvBem')?.value || null,
    };
    if (!dto.kandidat) { showToast('Kandidatenname angeben (oder oben einen MA wählen).', 'error'); return; }
    const r = await fetch(`/api/hr-interview/termine/${terminId}/buchen`, {
        method: 'POST', headers: ah(), body: JSON.stringify(dto),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Buchen fehlgeschlagen.', 'error'); return; }
    showToast('Platz gebucht.', 'success');
    hrIvReload();
}

// Buchung umbuchen (Walter 10.08.2026): Ziel-Termin wählen — der bestehende
// Vertrags-Link des MA zeigt danach automatisch den neuen Termin.
function hrIvUmbuchen(buchungId, aktuellerTerminId) {
    const el = document.getElementById(`hrIvUb${buchungId}`);
    if (!el) return;
    const ziele = _hrIvList.filter(t =>
        t.id !== aktuellerTerminId && (t.plaetze - (t.buchungen || []).length) > 0);
    if (!ziele.length) { showToast('Kein anderer Termin mit freien Plätzen vorhanden — zuerst einen Termin anlegen.', 'error'); return; }
    const opts = ziele.map(t =>
        `<option value="${t.id}">${_ivFmtD(t.datum)} · ${t.von}${t.bis ? '–' + t.bis : ''} (${t.plaetze - (t.buchungen || []).length} frei)</option>`).join('');
    el.style.display = 'inline-flex';
    el.innerHTML = `
        <select id="hrIvUbSel${buchungId}" style="${_ivInp};padding:3px 8px;font-size:12px">${opts}</select>
        <button onclick="hrIvUmbuchenSubmit(${buchungId})" style="${_ivBtnDark};padding:4px 10px;font-size:12px">Umbuchen</button>
        <button onclick="this.parentElement.style.display='none'" style="${_ivBtnLight};padding:4px 8px;font-size:12px">✕</button>`;
}

async function hrIvUmbuchenSubmit(buchungId) {
    const neuerTerminId = parseInt(document.getElementById(`hrIvUbSel${buchungId}`)?.value, 10);
    if (!neuerTerminId) return;
    if (typeof liquidConfirm === 'function'
        && !await liquidConfirm('Buchung auf den gewählten Termin verschieben? (MA vorher telefonisch informieren — der Vertrags-Link zeigt danach den neuen Termin.)', { title: 'Umbuchen' })) return;
    const r = await fetch(`/api/hr-interview/buchungen/${buchungId}/umbuchen`, {
        method: 'POST', headers: ah(), body: JSON.stringify({ neuerTerminId }),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Umbuchen fehlgeschlagen.', 'error'); return; }
    showToast('Umgebucht — der Vertrags-Link zeigt den neuen Termin.', 'success');
    hrIvReload();
}

async function hrIvAbsagen(buchungId) {
    if (typeof liquidConfirm === 'function' && !await liquidConfirm('Diese Buchung absagen? Der Platz wird wieder frei.', { title: 'HR-Kalender' })) return;
    const r = await fetch(`/api/hr-interview/buchungen/${buchungId}/absagen`, { method: 'POST', headers: ah() });
    if (!r.ok) { showToast('Absagen fehlgeschlagen.', 'error'); return; }
    hrIvReload();
}
