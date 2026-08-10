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

let _hrIvList = [];      // Termine ab heute (mit Buchungen)
let _hrIvSelDay = null;  // gewählter Tag (iso)

function _ivModalShell(id, titel, maxWidth) {
    if (document.getElementById(id)) return;
    const div = document.createElement('div');
    div.id = id;
    div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center;padding:16px';
    div.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:${maxWidth || 620}px;width:96%;max-height:92vh;overflow-y:auto;padding:22px 24px">
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
    _ivModalShell('hrIvModal', '🗣 Vorstellungsgespräche — HR-Büro-Kalender', 780);
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
        und beim Einladen eines Kandidaten direkt einen Platz buchen.
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
            <div style="display:flex;align-items:center;gap:8px;padding:2px 0 2px 16px;font-size:12.5px">
                <span>👤 ${_ivEsc(b.kandidat)}${b.telefon ? ' · ' + _ivEsc(b.telefon) : ''}${b.bemerkung ? ' · ' + _ivEsc(b.bemerkung) : ''}</span>
                <a onclick="hrIvAbsagen(${b.id})" style="cursor:pointer;color:#991b1b;font-weight:700" title="Buchung absagen">✕</a>
            </div>`).join('');
        return `
            <div style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.14);border-radius:12px;padding:8px 10px;margin-bottom:8px">
                <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
                    <b>🕐 ${t.von}${t.bis ? ' – ' + t.bis : ''}</b>
                    <span style="background:${frei > 0 ? '#dcfce7' : '#fecaca'};border-radius:8px;padding:1px 8px;font-size:11.5px;color:${frei > 0 ? '#166534' : '#991b1b'}">
                        ${frei} von ${t.plaetze} Plätzen frei</span>
                    <span style="color:#8b8b8b">${_ivEsc(t.bemerkung || '')}</span>
                    <span style="flex:1"></span>
                    ${frei > 0 ? `<button onclick="hrIvPick(${t.id})" style="${_ivBtnDark};padding:4px 12px;font-size:12px">Platz buchen</button>` : ''}
                    ${belegt === 0 ? `<button onclick="hrIvDeleteTermin(${t.id})" style="background:#fff;border:1px solid #cbd5e1;border-radius:6px;padding:2px 8px;font-size:12px;cursor:pointer;color:#991b1b">🗑</button>` : ''}
                </div>
                ${buchungen}
                <div id="hrIvBookForm${t.id}"></div>
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
    el.innerHTML = `
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;background:rgba(255,255,255,0.7);border:1px solid rgba(60,55,48,0.15);border-radius:10px;padding:8px;margin-top:6px">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Kandidat/in
                <input id="hrIvKand" style="${_ivInp};min-width:160px"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Telefon
                <input id="hrIvTel" style="${_ivInp};width:130px"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Bemerkung
                <input id="hrIvBem" style="${_ivInp};min-width:130px"></label>
            <button onclick="hrIvBook(${terminId})" style="${_ivBtnDark}">Buchen</button>
            <button onclick="this.closest('div[id^=hrIvBookForm]').innerHTML=''" style="${_ivBtnLight}">Abbrechen</button>
        </div>`;
    document.getElementById('hrIvKand')?.focus();
}

async function hrIvBook(terminId) {
    const dto = {
        kandidat: (document.getElementById('hrIvKand')?.value || '').trim(),
        telefon: document.getElementById('hrIvTel')?.value || null,
        bemerkung: document.getElementById('hrIvBem')?.value || null,
    };
    if (!dto.kandidat) { showToast('Kandidatenname angeben.', 'error'); return; }
    const r = await fetch(`/api/hr-interview/termine/${terminId}/buchen`, {
        method: 'POST', headers: ah(), body: JSON.stringify(dto),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Buchen fehlgeschlagen.', 'error'); return; }
    showToast('Platz gebucht.', 'success');
    hrIvReload();
}

async function hrIvAbsagen(buchungId) {
    if (typeof liquidConfirm === 'function' && !await liquidConfirm('Diese Buchung absagen? Der Platz wird wieder frei.', { title: 'HR-Kalender' })) return;
    const r = await fetch(`/api/hr-interview/buchungen/${buchungId}/absagen`, { method: 'POST', headers: ah() });
    if (!r.ok) { showToast('Absagen fehlgeschlagen.', 'error'); return; }
    hrIvReload();
}
