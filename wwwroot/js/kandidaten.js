// ══════════════════════════════════════════════════════════════════════
//  KANDIDATEN-PIPELINE GF → HR (Walter-Vorgabe 10.08.2026, Etappe 1)
//  GF (Restaurant Admin → Kachel «Kandidat an HR»): nach dem Vorstellungs-
//  gespräch Kandidat einreichen — Name, frühester Eintritt, L-GAV-Ausbildung,
//  Onboarding-Wunschtermin (unverbindlich), Anhänge. HR prüft in der
//  ONBOARDING-Kachel (Badge «N unbearb.») und nimmt an / lehnt ab; die Info
//  geht zurück ins Filial-Postfach. Nutzt _ivModalShell aus interview-fenster.js.
// ══════════════════════════════════════════════════════════════════════

const KAND_AUSBILDUNG = [
    ['Ia',   'Ia — ohne gastronomische Berufslehre'],
    ['Ib',   'Ib — ohne Berufslehre, mit PROGRESSO'],
    ['II',   'II — 2-jährige gastronomische Berufslehre EBA'],
    ['IIIa', 'IIIa — 3-jährige gastronomische Berufslehre EFZ'],
    ['IIIb', 'IIIb — 3-jährige Berufslehre GA 6 Tage'],
    ['IV',   'IV — gastronomische Berufsprüfung'],
];

const _kdInp = 'background:#fff;border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:6px 10px;font-size:13px;color:#3f3f3f';
const _kdBtnDark = 'background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 16px;font-size:13px;font-weight:600;cursor:pointer';

function _kdEsc(s) { return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;'); }

// CH-Telefonformat «+41 79 333 44 55» — gleiche Logik wie formatPhone()
// im easy@work-Importer (import.html).
function _kdFormatPhone(raw) {
    if (!raw) return '';
    let digits = String(raw).replace(/\D/g, '');
    if (!digits) return '';
    if (digits.startsWith('0041')) digits = digits.slice(4);
    if (digits.startsWith('41') && digits.length > 9) digits = digits.slice(2);
    if (digits.startsWith('0') && digits.length === 10) digits = digits.slice(1);
    if (digits.length === 9 && /^[2-9]/.test(digits)) {
        return '+41 ' + digits.slice(0, 2) + ' ' + digits.slice(2, 5) + ' ' + digits.slice(5, 7) + ' ' + digits.slice(7, 9);
    }
    return raw;
}
function _kdFmtD(iso) { return iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : ''; }
function _kdFmtTs(ts) { return ts ? `${ts.slice(8, 10)}.${ts.slice(5, 7)}.${ts.slice(2, 4)} ${ts.slice(11, 16)}` : ''; }

function _kdStatusPill(k) {
    const map = {
        NEU:        ['#fef9c3', '#854d0e', 'bei HR in Prüfung'],
        ANGENOMMEN: ['#dcfce7', '#166534', 'angenommen'],
        ABGELEHNT:  ['#fecaca', '#991b1b', 'abgelehnt'],
        ERLEDIGT:   ['#e0e7ff', '#3730a3', 'erledigt'],
    };
    const [bg, fg, label] = map[k.status] || ['#f1efe9', '#8b8b8b', k.status];
    const grund = k.status === 'ABGELEHNT' && k.ablehnungsgrund ? ` title="${_kdEsc(k.ablehnungsgrund)}"` : '';
    return `<span${grund} style="background:${bg};color:${fg};border-radius:8px;padding:1px 8px;font-size:11px;font-weight:700">${label}</span>`;
}

// ── GF: Kandidat einreichen ─────────────────────────────────────────────
let _kdFiles = [];
let _kdEditId = null; // Bearbeiten-Modus (Walter 11.08.2026): Id des Kandidaten
let _kdMeine = [];    // eigene eingereichte Kandidaten (für Bearbeiten)

async function openKandidatModal() {
    _ivModalShell('kdModal', '📨 Kandidat an HR senden', 720);
    document.getElementById('kdModal').style.display = 'flex';
    _kdFiles = [];
    _kdEditId = null;
    const body = document.getElementById('kdModalBody');
    body.innerHTML = '<span style="color:#8b8b8b">Wird geladen…</span>';

    // Filial-Auswahl: admin/superuser alle, GF seine Filialen (Me-Liste).
    let filialen = [];
    if (['admin', 'superuser', 'buchhaltung'].includes(currentUser?.role)) {
        filialen = (typeof allBranches !== 'undefined' && Array.isArray(allBranches)) ? allBranches : [];
    } else {
        filialen = Array.isArray(currentUser?.branches) ? currentUser.branches : [];
        if (!filialen.length && typeof allBranches !== 'undefined') filialen = allBranches;
    }
    const selCp = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) || filialen[0]?.id;
    const filOpts = filialen.map(b =>
        `<option value="${b.id}"${b.id === selCp ? ' selected' : ''}>${_kdEsc((b.restaurantCode ? b.restaurantCode + ' ' : '') + (b.branchName || b.city || b.name || ''))}</option>`).join('');

    // Onboarding-Termine (unverbindlicher Wunsch).
    let termine = [];
    try {
        const r = await fetch('/api/kandidaten/termine', { headers: ah() });
        if (r.ok) termine = await r.json();
    } catch (_) { /* Wunschtermin ist optional */ }
    const terminOpts = ['<option value="">— noch offen —</option>']
        .concat(termine.filter(t => t.frei > 0).map(t =>
            `<option value="${t.id}">${_kdFmtD(t.datum)} · ${t.von}${t.bis ? '–' + t.bis : ''} (${t.frei} frei)</option>`))
        .join('');
    const ausbOpts = KAND_AUSBILDUNG.map(([c, l]) => `<option value="${c}">${l}</option>`).join('');

    body.innerHTML = `
        <p style="margin:0 0 10px;color:#646464">Nach dem Vorstellungsgespräch: Kandidat/in an HR melden.
        HR prüft, entscheidet und meldet sich via Filial-Postfach zurück.</p>
        <div id="kdEditBanner" style="display:none;background:#e0e7ff;border:1px solid #c7d2fe;border-radius:10px;padding:8px 12px;margin-bottom:10px;font-size:13px;color:#3730a3"></div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px 12px">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Vorname
                <input id="kdVorname" style="${_kdInp}"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Name
                <input id="kdName" style="${_kdInp}"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Telefon
                <input id="kdTelefon" placeholder="+41 79 333 44 55" onblur="this.value=_kdFormatPhone(this.value)" style="${_kdInp}"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">E-Mail
                <input id="kdEmail" type="email" placeholder="name@mail.ch" style="${_kdInp}"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Restaurant
                <select id="kdCp" style="${_kdInp}">${filOpts}</select></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Frühest möglicher Eintritt
                <input id="kdEintritt" type="date" style="${_kdInp}"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Gastro-Ausbildung (L-GAV)
                <select id="kdAusbildung" style="${_kdInp}">${ausbOpts}</select></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Onboarding-Wunschtermin
                <select id="kdTermin" style="${_kdInp}">${terminOpts}</select></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Bemerkung
                <input id="kdBemerkung" placeholder="optional" style="${_kdInp}"></label>
        </div>
        <div style="margin-top:10px">
            <button onclick="document.getElementById('kdFiles').click()" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:6px 14px;font-size:12.5px;cursor:pointer;color:#3f3f3f">📎 Dokumente anhängen</button>
            <button onclick="kdPfOpen()" title="Dokument aus dem Filial-Posteingang übernehmen (z.B. gescannte Bewerbungsunterlagen)" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:6px 14px;font-size:12.5px;cursor:pointer;color:#3f3f3f;margin-left:6px">📥 Aus Posteingang</button>
            <input type="file" id="kdFiles" accept="application/pdf,image/*" multiple style="display:none" onchange="kdFilesPicked(this.files)">
            <div id="kdPfPicker" style="display:none;margin-top:8px;background:rgba(255,255,255,0.7);border:1px solid rgba(60,55,48,0.15);border-radius:10px;padding:8px;max-height:220px;overflow:auto;font-size:12.5px"></div>
            <div id="kdFileListVorhanden" style="font-size:12px;color:#646464;margin-top:6px"></div>
            <div id="kdFileList" style="font-size:12px;color:#646464;margin-top:6px"></div>
        </div>
        <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:14px">
            <button id="kdCancelEditBtn" onclick="openKandidatModal()" style="display:none;background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:7px 14px;font-size:13px;cursor:pointer;color:#3f3f3f">Abbrechen</button>
            <button id="kdSubmitBtn" onclick="kdSubmit()" style="${_kdBtnDark}">An HR senden</button>
        </div>
        <div style="font-weight:700;margin:16px 0 4px">Meine eingereichten Kandidaten</div>
        <div id="kdMeineListe" style="font-size:12.5px;color:#3f3f3f">Wird geladen…</div>`;
    kdMeineListe();
}

// Mehrfach anhängen (Walter 10.08.2026): jede Auswahl wird ANGEHÄNGT, nicht
// ersetzt — so kann man nacheinander CV, Bewilligung, Zeugnis … dazuklicken.
function kdFilesPicked(files) {
    for (const f of Array.from(files || [])) {
        if (!_kdFiles.some(x => x.name === f.name && x.size === f.size)) _kdFiles.push(f);
    }
    const inp = document.getElementById('kdFiles');
    if (inp) inp.value = '';   // gleiche Datei erneut wählbar
    kdFilesRender();
}

function kdFilesRender() {
    const el = document.getElementById('kdFileList');
    if (!el) return;
    el.innerHTML = _kdFiles.map((f, i) =>
        `<span style="display:inline-flex;align-items:center;gap:5px;background:#fff;border:1px solid rgba(60,55,48,0.18);border-radius:10px;padding:2px 9px;margin:2px 6px 2px 0">
            📄 ${_kdEsc(f.name)}
            <a onclick="kdFileRemove(${i})" style="cursor:pointer;color:#991b1b;font-weight:700">✕</a>
        </span>`).join('');
}

function kdFileRemove(i) {
    _kdFiles.splice(i, 1);
    kdFilesRender();
}

// ── Dokumente aus dem Posteingang übernehmen (Walter 11.08.2026) ─────────
// ALLE für den Benutzer zugänglichen Postfächer wählbar (eigenes, Filialen,
// HR/Admin/Buchhaltung — via /api/mailbox/postfaecher). Die Datei wird
// heruntergeladen und wie ein lokaler Anhang mitgeschickt — das Original
// bleibt unverändert im Posteingang.
async function kdPfOpen() {
    const el = document.getElementById('kdPfPicker');
    if (!el) return;
    if (el.style.display !== 'none') { el.style.display = 'none'; return; }
    el.style.display = 'block';
    el.innerHTML = '<span style="color:#8b8b8b">Postfächer werden geladen…</span>';
    try {
        const r = await fetch('/api/mailbox/postfaecher', { headers: ah() });
        const pfs = await r.json();
        if (!r.ok || !Array.isArray(pfs) || !pfs.length) { el.innerHTML = 'Keine Postfächer verfügbar.'; return; }
        const val = (p) => p.type === 'USER' ? `USER:${p.targetUserId}`
            : p.type === 'BRANCH' ? `BRANCH:${p.companyProfileId}` : p.type;
        const label = (p) => (p.type === 'BRANCH' && p.code ? p.code + ' ' : '') + (p.name || p.type)
            + (p.isSelf ? ' (mein Postfach)' : '') + (p.count ? ` — ${p.count}` : '');
        // Vorauswahl: Filial-Postfach des im Formular gewählten Restaurants.
        const cpId = document.getElementById('kdCp')?.value;
        const preferred = pfs.find(p => p.type === 'BRANCH' && String(p.companyProfileId) === String(cpId)) || pfs[0];
        const opts = pfs.map(p =>
            `<option value="${val(p)}"${val(p) === val(preferred) ? ' selected' : ''}>${_kdEsc(label(p))}</option>`).join('');
        el.innerHTML = `
            <div style="display:flex;align-items:center;gap:8px;margin-bottom:6px;flex-wrap:wrap">
                <b style="color:#3f3f3f">Postfach:</b>
                <select id="kdPfSel" onchange="kdPfLoadFiles()" style="${_kdInp};padding:4px 8px;font-size:12px;min-width:220px">${opts}</select>
            </div>
            <div id="kdPfFiles" style="font-size:12.5px"></div>`;
        kdPfLoadFiles();
    } catch (_) { el.innerHTML = 'Verbindungsfehler.'; }
}

async function kdPfLoadFiles() {
    const el = document.getElementById('kdPfFiles');
    const v = document.getElementById('kdPfSel')?.value || '';
    if (!el || !v) return;
    el.innerHTML = '<span style="color:#8b8b8b">Wird geladen…</span>';
    let url = '';
    if (v.startsWith('USER:')) url = `/api/mailbox?type=USER&targetUserId=${v.slice(5)}`;
    else if (v.startsWith('BRANCH:')) url = `/api/mailbox?type=BRANCH&companyProfileId=${v.slice(7)}`;
    else url = `/api/mailbox?type=${v}`;
    try {
        const r = await fetch(url, { headers: ah() });
        const list = await r.json();
        if (!r.ok) { el.innerHTML = 'Laden fehlgeschlagen.'; return; }
        const files = (Array.isArray(list) ? list : []).filter(m => m.mimeType && !m.messageBody).slice(0, 40);
        if (!files.length) { el.innerHTML = '<span style="color:#8b8b8b">Keine Dateien in diesem Postfach.</span>'; return; }
        el.innerHTML = `<div style="color:#8b8b8b;font-size:11.5px;margin-bottom:3px">Datei anklicken zum Übernehmen — das Original bleibt im Postfach.</div>` + files.map(m => `
            <div style="display:flex;align-items:center;gap:8px;padding:3px 2px;border-bottom:1px solid rgba(60,55,48,0.07)">
                <a onclick="kdPfAdd(${m.id}, '${_kdEsc(m.originalFilename).replace(/'/g, '&#39;')}')" style="cursor:pointer;color:#1d4ed8;text-decoration:underline">📄 ${_kdEsc(m.originalFilename)}</a>
                <span style="color:#b0aca4;font-size:11px">${_kdFmtTs((m.uploadedAt || '').slice(0, 16).replace('T', ' '))}</span>
                ${m.bemerkung ? `<span style="color:#8b8b8b;font-size:11px">· ${_kdEsc(m.bemerkung)}</span>` : ''}
            </div>`).join('');
    } catch (_) { el.innerHTML = 'Verbindungsfehler.'; }
}

async function kdPfAdd(id, name) {
    try {
        const r = await fetch(`/api/mailbox/${id}/download`, { headers: ah() });
        if (!r.ok) { showToast('Datei konnte nicht geladen werden.', 'error'); return; }
        const blob = await r.blob();
        const f = new File([blob], name || 'dokument', { type: blob.type || 'application/octet-stream' });
        if (_kdFiles.some(x => x.name === f.name && x.size === f.size)) { showToast('Datei ist bereits angehängt.', 'error'); return; }
        _kdFiles.push(f);
        kdFilesRender();
        showToast(`«${name}» aus dem Posteingang übernommen.`, 'success');
    } catch (_) { showToast('Datei konnte nicht geladen werden.', 'error'); }
}

async function kdSubmit() {
    const vorname = (document.getElementById('kdVorname')?.value || '').trim();
    const name = (document.getElementById('kdName')?.value || '').trim();
    if (!vorname || !name) { showToast('Vorname und Name angeben.', 'error'); return; }
    const fd = new FormData();
    fd.append('companyProfileId', document.getElementById('kdCp')?.value || '');
    fd.append('vorname', vorname);
    fd.append('name', name);
    fd.append('telefon', _kdFormatPhone(document.getElementById('kdTelefon')?.value || ''));
    fd.append('email', (document.getElementById('kdEmail')?.value || '').trim());
    fd.append('fruehesterEintritt', document.getElementById('kdEintritt')?.value || '');
    fd.append('lgavAusbildung', document.getElementById('kdAusbildung')?.value || '');
    const terminVal = document.getElementById('kdTermin')?.value;
    if (terminVal) fd.append('wunschTerminId', terminVal);
    fd.append('bemerkung', document.getElementById('kdBemerkung')?.value || '');
    for (const f of _kdFiles) fd.append('files', f);
    // ACHTUNG: bei FormData KEIN ah() — zerstört den Multipart-Boundary.
    const url = _kdEditId ? `/api/kandidaten/${_kdEditId}/update` : '/api/kandidaten';
    const r = await fetch(url, {
        method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd,
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Senden fehlgeschlagen.', 'error'); return; }
    const warEdit = !!_kdEditId;
    showToast(warEdit
        ? 'Änderungen gespeichert — HR sieht sofort den aktuellen Stand.'
        : 'Kandidat an HR gesendet.', 'success');
    _kdFiles = [];
    _kdEditId = null;
    openKandidatModal();
}

async function kdMeineListe() {
    const el = document.getElementById('kdMeineListe');
    if (!el) return;
    try {
        const r = await fetch('/api/kandidaten', { headers: ah() });
        const list = await r.json();
        if (!r.ok) { el.textContent = 'Laden fehlgeschlagen.'; return; }
        _kdMeine = list;
        if (!list.length) { el.innerHTML = '<span style="color:#8b8b8b">Noch keine Kandidaten eingereicht.</span>'; return; }
        el.innerHTML = list.map(k => `
            <div style="display:flex;align-items:center;gap:10px;padding:5px 8px;border-bottom:1px solid rgba(60,55,48,0.08);flex-wrap:wrap">
                <b>${_kdEsc(k.vorname)} ${_kdEsc(k.name)}</b>
                <span style="color:#8b8b8b">${k.fruehesterEintritt ? 'ab ' + _kdFmtD(k.fruehesterEintritt) : ''}</span>
                ${_kdStatusPill(k)}
                <span style="color:#b0aca4;font-size:11px">${_kdFmtTs(k.createdAt)}</span>
                ${k.status === 'NEU'
                    ? `<a onclick="kdEdit(${k.id})" title="Solange HR noch nicht entschieden hat: Daten ändern und weitere Dokumente anhängen"
                          style="cursor:pointer;color:#1d4ed8;font-size:12px;font-weight:700">✎ Bearbeiten</a>`
                    : ''}
            </div>`).join('');
    } catch (_) { el.textContent = 'Verbindungsfehler.'; }
}

// Bearbeiten (Walter 11.08.2026): Formular oben mit den Daten des Kandidaten
// füllen — nur solange HR noch nicht entschieden hat (Status NEU).
function kdEdit(id) {
    const k = _kdMeine.find(x => x.id === id);
    if (!k) return;
    _kdEditId = id;
    _kdFiles = [];
    kdFilesRender();
    const set = (elId, v) => { const e = document.getElementById(elId); if (e) e.value = v ?? ''; };
    set('kdVorname', k.vorname);
    set('kdName', k.name);
    set('kdTelefon', k.telefon);
    set('kdEmail', k.email);
    set('kdCp', k.companyProfileId);
    set('kdEintritt', k.fruehesterEintritt);
    set('kdAusbildung', k.lgavAusbildung);
    set('kdBemerkung', k.bemerkung);
    // Wunschtermin: falls der gewählte Termin nicht (mehr) in der Liste ist
    // (z.B. inzwischen ausgebucht), Option ergänzen — Auswahl bleibt erhalten.
    const sel = document.getElementById('kdTermin');
    if (sel && k.wunschTerminId) {
        if (![...sel.options].some(o => o.value === String(k.wunschTerminId)))
            sel.insertAdjacentHTML('beforeend', `<option value="${k.wunschTerminId}">${_kdEsc(k.wunschTermin || 'gewählter Termin')}</option>`);
        sel.value = String(k.wunschTerminId);
    } else if (sel) sel.value = '';
    // Bereits eingereichte Anhänge (bleiben bestehen, neue kommen dazu).
    const vorhanden = document.getElementById('kdFileListVorhanden');
    if (vorhanden) vorhanden.innerHTML = (k.dokumente || []).length
        ? 'Bereits eingereicht: ' + k.dokumente.map(d =>
            `<span style="display:inline-flex;align-items:center;gap:5px;background:#f1efe9;border:1px solid rgba(60,55,48,0.14);border-radius:10px;padding:2px 9px;margin:2px 6px 2px 0">📄 ${_kdEsc(d.name)}</span>`).join('')
        : '';
    const btn = document.getElementById('kdSubmitBtn');
    if (btn) btn.textContent = '💾 Änderungen an HR speichern';
    const cancel = document.getElementById('kdCancelEditBtn');
    if (cancel) cancel.style.display = '';
    // Unmissverständlicher Bearbeiten-Modus (Walter 11.08.2026): blauer
    // Banner über dem Formular — sonst ist nicht erkennbar, ob man gerade
    // einen NEUEN Kandidaten erfasst oder einen bestehenden ändert.
    const banner = document.getElementById('kdEditBanner');
    if (banner) {
        banner.style.display = 'block';
        banner.innerHTML = `✎ <b>Du bearbeitest: ${_kdEsc(k.vorname)} ${_kdEsc(k.name)}</b> — der Kandidat liegt bereits bei HR.
            Angaben ändern und/oder Dokumente ergänzen, dann unten «💾 Änderungen an HR speichern».
            Zum Erfassen eines NEUEN Kandidaten zuerst «Abbrechen» drücken.`;
    }
    document.getElementById('kdEditBanner')?.scrollIntoView({ behavior: 'smooth', block: 'center' });
}

// ── HR: Kandidaten prüfen (ONBOARDING-Kachel) ───────────────────────────
async function hrKandBadge() {
    const el = document.getElementById('obKandBadge');
    if (!el) return;
    if (!['admin', 'superuser'].includes(currentUser?.role)) { el.textContent = ''; return; }
    try {
        const r = await fetch('/api/kandidaten/count-offen', { headers: ah() });
        const j = await r.json();
        el.innerHTML = (r.ok && j.offen > 0)
            ? `<span style="background:#fecaca;color:#991b1b;border-radius:10px;padding:1px 9px;font-size:11px;font-weight:800">${j.offen} unbearb.</span>`
            : '';
    } catch (_) { /* Badge ist nur Komfort */ }
}

function hrKandOpen() {
    _ivModalShell('hrKandModal', '📨 Kandidaten-Verwaltung', 1560);
    document.getElementById('hrKandModal').style.display = 'flex';
    hrKandReload();
}

function _kdKopf(k) {
    return `
        <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <b style="font-size:14px">${_kdEsc(k.vorname)} ${_kdEsc(k.name)}</b>
            <span class="kd-chip kd-chip-kohle">${_kdEsc(k.filiale)}</span>
            ${k.telefon ? `<span class="kd-dim" style="font-size:12px">📞 ${_kdEsc(k.telefon)}</span>` : ''}
            ${k.email ? `<span class="kd-dim" style="font-size:12px">✉️ ${_kdEsc(k.email)}</span>` : ''}
            <span class="kd-dim" style="font-size:11px">eingereicht ${_kdFmtTs(k.createdAt)} von ${_kdEsc(k.createdBy || '')}</span>
        </div>`;
}

function _kdDetails(k) {
    const ausb = KAND_AUSBILDUNG.find(([c]) => c === k.lgavAusbildung)?.[1] || k.lgavAusbildung || '–';
    const doks = (k.dokumente || []).map(d =>
        `<a style="cursor:pointer;text-decoration:underline;color:#3f3f3f" onclick="kdDokPreview(${d.id}, '${_kdEsc(d.name)}')">📎 ${_kdEsc(d.name)}</a>`).join(' · ');
    // Onboarding-Tag direkt in der Karte änderbar (Walter 11.08.2026):
    // freie Termine + der aktuell gewählte; Änderung speichert sofort.
    const opts = ['<option value="">— kein Termin —</option>']
        .concat((_kdHrTermine || []).filter(t => t.frei > 0 || t.id === k.wunschTerminId).map(t =>
            `<option value="${t.id}"${t.id === k.wunschTerminId ? ' selected' : ''}>${_kdFmtD(t.datum)} · ${t.von}${t.bis ? '–' + t.bis : ''} (${t.frei} frei)</option>`))
        .join('');
    // Markante Anzeige (Walter 11.08.2026): welcher Onboarding-Tag mit dem
    // Kandidaten provisorisch ausgemacht wurde.
    const sel = (_kdHrTermine || []).find(t => t.id === k.wunschTerminId);
    const wtNamen = ['Sonntag', 'Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag'];
    let terminBadge;
    if (sel) {
        const wt = wtNamen[new Date(sel.datum + 'T00:00:00').getDay()];
        terminBadge = `<div style="margin-top:8px;display:inline-flex;align-items:center;gap:8px;background:#e0e7ff;border:1px solid #c7d2fe;border-radius:10px;padding:6px 12px;font-size:13.5px;color:#3730a3">
            📅 <b>Onboarding provisorisch ausgemacht:</b> ${wt}, ${_kdFmtD(sel.datum)} · ${sel.von}${sel.bis ? '–' + sel.bis : ''} Uhr</div>`;
    } else if (k.wunschTermin) {
        // Termin-Detail nicht (mehr) ladbar — Fallback auf den Server-Text.
        terminBadge = `<div style="margin-top:8px;display:inline-flex;align-items:center;gap:8px;background:#e0e7ff;border:1px solid #c7d2fe;border-radius:10px;padding:6px 12px;font-size:13.5px;color:#3730a3">
            📅 <b>Onboarding provisorisch ausgemacht:</b> ${_kdEsc(k.wunschTermin)} Uhr</div>`;
    } else {
        terminBadge = `<div style="margin-top:8px;display:inline-flex;align-items:center;gap:8px;background:#fef9c3;border:1px solid #fde68a;border-radius:10px;padding:6px 12px;font-size:13px;color:#854d0e">
            📅 Noch kein Onboarding-Tag ausgemacht</div>`;
    }
    return `
        <div style="display:flex;gap:14px;flex-wrap:wrap;margin-top:6px;font-size:12.5px;color:#3f3f3f;align-items:center">
            <span><b>Eintritt ab:</b> ${k.fruehesterEintritt ? _kdFmtD(k.fruehesterEintritt) : '–'}</span>
            <span><b>Ausbildung:</b> ${_kdEsc(ausb)}</span>
            <span style="display:flex;align-items:center;gap:6px"><b>Onboarding-Tag ändern:</b>
                <select onchange="hrKandTermin(${k.id}, this.value)" style="${_kdInp};padding:4px 8px;font-size:12px;min-width:210px">${opts}</select></span>
        </div>
        ${terminBadge}
        ${k.bemerkung ? `<div style="margin-top:4px;font-size:12.5px;color:#646464">💬 ${_kdEsc(k.bemerkung)}</div>` : ''}
        ${doks ? `<div style="margin-top:6px;font-size:12.5px">${doks}</div>` : ''}
        ${k.gespraechId ? `<div style="margin-top:6px"><button type="button" onclick="bgsOpenFromHr(${k.gespraechId})" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:5px 12px;font-size:12.5px;cursor:pointer;color:#3f3f3f" title="Alle Angaben aus dem Bewerbungsgespräch ansehen und bearbeiten — beim Verknüpfen mit dem MA werden sie in die Personalakte übernommen">💬 Gesprächsdaten ansehen / bearbeiten</button></div>` : ''}`;
}

async function hrKandTermin(id, val) {
    const terminId = val ? parseInt(val, 10) : null;
    const r = await fetch(`/api/kandidaten/${id}/termin`, {
        method: 'POST', headers: ah(), body: JSON.stringify({ terminId }),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Termin speichern fehlgeschlagen.', 'error'); hrKandReload(); return; }
    const k = _kdHrList.find(x => x.id === id);
    if (k) k.wunschTerminId = terminId;
    showToast(terminId ? 'Onboarding-Tag gespeichert.' : 'Onboarding-Tag entfernt.', 'success');
    hrKandReload(); // Badge «provisorisch ausgemacht» aktualisieren
}

let _kdHrList = [];    // letzte HR-Liste (für die Dokument-Zuordnung beim Verknüpfen)
let _kdHrTermine = []; // Onboarding-Termine (für den Onboarding-Tag-Select in den Karten)
let _kdObTage = [];    // Onboarding-Tage mit Teilnehmenden (Tages-Gruppierung)

async function hrKandReload() {
    const body = document.getElementById('hrKandModalBody');
    if (!body) return;
    body.innerHTML = '<span style="color:#8b8b8b">Wird geladen…</span>';
    try {
        const [r, rt, rob] = await Promise.all([
            fetch('/api/kandidaten', { headers: ah() }),
            fetch('/api/kandidaten/termine', { headers: ah() }),
            fetch('/api/kandidaten/onboarding-tage', { headers: ah() }),
        ]);
        const list = await r.json();
        if (!r.ok) { body.innerHTML = 'Laden fehlgeschlagen.'; return; }
        _kdHrList = list;
        _kdHrTermine = rt.ok ? await rt.json() : [];
        _kdObTage = rob.ok ? await rob.json() : [];
        const neu = list.filter(k => k.status === 'NEU');
        const angenommen = list.filter(k => k.status === 'ANGENOMMEN');
        const abgelehnt = list.filter(k => k.status === 'ABGELEHNT');
        const erledigt = list.filter(k => k.status === 'ERLEDIGT');
        // Karten im Stil des MA-Hauptbildschirms (.kd-day, dark-mode-fähig
        // via app.css); Sektions-Titel als ruhige Uppercase-Zeile mit Pille.
        const card = (inner) => `<div class="kd-day">${inner}</div>`;
        const titel = (t, n, chipCls) => `<div style="display:flex;align-items:center;gap:10px;margin:20px 0 10px">
                <span class="kd-sect">${t}</span>
                <span class="kd-chip ${chipCls || 'kd-chip-grau'}">${n}</span>
                <span class="kd-sect-line"></span>
            </div>`;
        let html = '';

        // ── 1) Zu prüfen ────────────────────────────────────────────────
        html += titel('Zu prüfen', neu.length, neu.length ? 'kd-chip-gelb' : null);
        html += neu.length ? neu.map(k => card(`
            ${_kdKopf(k)}${_kdDetails(k)}
            <div style="display:flex;gap:8px;align-items:flex-end;margin-top:10px;flex-wrap:wrap">
                <button onclick="hrKandEntscheid(${k.id}, true)" style="background:#166534;color:#fff;border:none;border-radius:12px;padding:6px 16px;font-size:12.5px;font-weight:700;cursor:pointer">✓ Annehmen</button>
                <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Ablehnungsgrund
                    <input id="kdGrund${k.id}" style="${_kdInp};min-width:220px"></label>
                <button onclick="hrKandEntscheid(${k.id}, false)" style="background:#fff;border:1.5px solid #991b1b;color:#991b1b;border-radius:12px;padding:6px 14px;font-size:12.5px;font-weight:700;cursor:pointer">✕ Ablehnen</button>
            </div>`)).join('')
            : '<span style="color:#8b8b8b;font-size:12.5px">Keine unbearbeiteten Kandidaten. 🎉</span>';

        // ── 2) Onboarding-Tage (Walter 11.08.2026): Kandidaten/MA pro Tag ──
        // gruppiert — prominente Tages-Header, kompakte Zeilen mit Status
        // rechts. Verknüpfte MA bleiben beim Tag; nach dem Tag bestätigt HR
        // pro Person «Onboarding abgeschlossen» (Meldung an den GF).
        const obTage = _kdObTage || [];
        const obKandIds = new Set();
        const obEmpIds = new Set();
        obTage.forEach(t => (t.rows || []).forEach(rr => {
            if (rr.kandidatId) obKandIds.add(rr.kandidatId);
            if (rr.employeeId) obEmpIds.add(rr.employeeId);
        }));
        const ohneTag = angenommen.filter(k => !k.wunschTerminId);
        html += titel('Onboarding-Tage', obTage.length, obTage.length ? 'kd-chip-gruen' : null);
        if (ohneTag.length) {
            html += card(`
                <div class="kd-day-title" style="font-size:14.5px;margin-bottom:4px">⚠ Angenommen — noch ohne Onboarding-Tag</div>
                ${ohneTag.map(k => `
                <div class="kd-row">
                    <div><span class="kd-chip kd-chip-kohle">${_kdEsc(k.filiale)}</span></div>
                    <div><b>${_kdEsc(k.vorname)} ${_kdEsc(k.name)}</b></div>
                    <div class="kd-dim" style="font-size:11.5px;white-space:nowrap">${k.telefon ? _kdEsc(k.telefon) : ''}</div>
                    <div><span class="kd-chip kd-chip-gelb">Onboarding-Tag wählen → Details</span></div>
                    <a class="kd-link" onclick="_kdObToggle('k${k.id}', ${k.id})" style="font-size:12px;justify-self:end">Details ⌄</a>
                </div>
                <div id="kdObDetk${k.id}" style="display:none"></div>`).join('')}`);
        }
        html += obTage.length
            ? obTage.map(t => card(_kdObTagInner(t))).join('')
            : (ohneTag.length ? '' : '<span style="color:#8b8b8b;font-size:12.5px">Keine Onboarding-Tage mit Teilnehmenden.</span>');

        // ── 3) Abgelehnt: Absage senden (Auto-Löschung nach 30 Tagen) ───
        html += titel('Abgelehnt — Absage senden', abgelehnt.length, abgelehnt.length ? 'kd-chip-rot' : null);
        html += abgelehnt.length ? abgelehnt.map(k => card(`
            ${_kdKopf(k)}
            <div style="margin-top:4px;font-size:12.5px;color:#646464">Grund: ${_kdEsc(k.ablehnungsgrund || '–')}</div>
            <div style="display:flex;gap:8px;align-items:center;margin-top:8px;flex-wrap:wrap">
                ${k.absageGesendetAm
                    ? `<span style="background:#dcfce7;color:#166534;border-radius:8px;padding:2px 10px;font-size:12px;font-weight:700">✓ Absage per ${k.absageKanal === 'EMAIL' ? 'E-Mail' : 'SMS'} gesendet ${_kdFmtTs(k.absageGesendetAm)}</span>`
                    : `${k.email ? `<button onclick="hrKandAbsage(${k.id}, 'EMAIL')" style="${_kdBtnDark};font-size:12.5px;padding:6px 14px">✉️ Absage per E-Mail</button>` : ''}
                       ${k.telefon ? `<button onclick="hrKandAbsage(${k.id}, 'SMS')" style="${_kdBtnDark};font-size:12.5px;padding:6px 14px">📱 Absage per SMS</button>` : ''}
                       ${(!k.email && !k.telefon) ? '<span style="color:#991b1b;font-size:12px">Weder E-Mail noch Telefon erfasst — Absage bitte anders zustellen.</span>' : ''}
                       <button onclick="hrKandZuruecknehmen(${k.id})" title="Ablehnung zurücknehmen — der Kandidat steht wieder unter «Zu prüfen»"
                               style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:6px 12px;font-size:12px;cursor:pointer;color:#3f3f3f">↶ Entscheid zurücknehmen</button>`}
                <span style="color:#b0aca4;font-size:11px">wird 30 Tage nach dem Entscheid automatisch gelöscht</span>
            </div>
            ${_kdNotizHtml(k)}`)).join('')
            : '<span style="color:#8b8b8b;font-size:12.5px">Keine offenen Absagen.</span>';

        // ── 4) Erledigt-Fallback: nur verknüpfte Kandidaten, die in KEINEM
        // Onboarding-Tag auftauchen — auch nicht über den verknüpften MA ──
        const erledigtRest = erledigt.filter(k =>
            !obKandIds.has(k.id) && !(k.verknuepftEmployeeId && obEmpIds.has(k.verknuepftEmployeeId)));
        if (erledigtRest.length) {
            html += titel('Erledigt — ohne Onboarding-Tag', erledigtRest.length, 'kd-chip-indigo');
            html += erledigtRest.map(k => card(`
                ${_kdKopf(k)}
                <div style="display:flex;gap:12px;align-items:center;margin-top:6px;flex-wrap:wrap;font-size:12.5px">
                    <span class="kd-chip kd-chip-indigo">✓ verknüpft ${_kdFmtTs(k.erledigtAm)}</span>
                    <span class="kd-dim" style="font-size:11px">wird 30 Tage später automatisch gelöscht</span>
                    <span style="flex:1"></span>
                    <a class="kd-link" onclick="hrKandLoeschen(${k.id}, '${_kdEsc(k.vorname)} ${_kdEsc(k.name)}')" style="font-size:12px;color:#991b1b">🗑 Jetzt löschen</a>
                </div>
                ${_kdNotizHtml(k)}`)).join('');
        }

        body.innerHTML = html;
    } catch (_) { body.innerHTML = '<span style="color:#991b1b">Verbindungsfehler.</span>'; }
}

// ── Onboarding-Tages-Gruppierung (Walter 11.08.2026) ────────────────────
const _kdWtNamen = ['Sonntag', 'Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag'];

function _kdObTagInner(t) {
    const wt = _kdWtNamen[new Date(t.datum + 'T00:00:00').getDay()];
    const offen = (t.rows || []).filter(r => r.buchungId && !r.abgeschlossenAm).length;
    const header = `
        <div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:6px">
            <div class="kd-day-title">📅 ${wt}, ${_kdFmtD(t.datum)}</div>
            <span class="kd-dim" style="font-size:13.5px">🕘 ${t.von}${t.bis ? '–' + t.bis : ''} Uhr</span>
            <span class="kd-chip kd-chip-grau">${t.belegt}/${t.plaetze} Plätze</span>
            ${t.bemerkung ? `<span class="kd-dim" style="font-size:12px">${_kdEsc(t.bemerkung)}</span>` : ''}
            <span style="flex:1"></span>
            ${t.vergangen && offen
                ? '<span class="kd-chip kd-chip-orange">Tag vorbei — Abschluss bestätigen</span>'
                : ''}
        </div>`;
    // Hauptsortierung Filiale, innerhalb der Filiale nach Vorname (Walter 11.08.2026).
    const sortiert = (t.rows || []).slice().sort((a, b) =>
        (a.filiale || '').localeCompare(b.filiale || '') || (a.name || '').localeCompare(b.name || ''));
    const rows = sortiert.length
        ? sortiert.map(r => _kdObRow(t, r)).join('')
        : '<div style="color:#8b8b8b;font-size:12.5px;padding:4px 2px">Noch niemand eingeladen.</div>';
    return header + rows;
}

function _kdObRow(t, r) {
    const key = r.buchungId != null ? r.buchungId : ('k' + r.kandidatId);
    const k = r.kandidatId ? _kdHrList.find(x => x.id === r.kandidatId) : null;
    let aktionen = '';
    if (k && k.status === 'ANGENOMMEN' && !r.abgeschlossenAm) {
        aktionen += `<button onclick="hrKandWillkommen(${k.id})" style="${_kdBtnDark};font-size:11.5px;padding:4px 10px">📱 ${r.willkommenGesendetAm ? 'SMS erneut' : 'SMS senden'}</button>`;
        if (r.maAntwort === 'ANGENOMMEN' && !r.verknuepft)
            aktionen += `<button onclick="_kdObVerknuepfen('${key}', ${k.id})" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:10px;padding:4px 10px;font-size:11.5px;cursor:pointer;color:#3f3f3f;font-weight:600">🔗 Verknüpfen</button>`;
    }
    if (t.vergangen && r.buchungId && !r.abgeschlossenAm)
        aktionen += `<button onclick="hrKandObAbschliessen(${r.buchungId}, '${_kdEsc(r.name).replace(/'/g, '&#39;')}')" style="background:#166534;color:#fff;border:none;border-radius:10px;padding:4px 12px;font-size:11.5px;font-weight:700;cursor:pointer">✓ Onboarding abschliessen</button>`;
    // Onboarding-Tag verschieben — prominent direkt in der Zeile (Walter 11.08.2026).
    if (!r.abgeschlossenAm && !t.vergangen && (r.kandidatId || r.buchungId))
        aktionen += `<button class="kd-btn-glass" onclick="_kdObTagWechsel('${key}', ${r.kandidatId ?? 'null'}, ${r.buchungId ?? 'null'})" title="Person auf einen anderen Onboarding-Tag verschieben">⇄ Tag ändern</button>`;
    if (k)
        aktionen += `<a class="kd-link" onclick="_kdObToggle('${key}', ${k.id})" style="font-size:12px">Details ⌄</a>`;
    return `
        <div class="kd-row">
            <div>${r.filiale ? `<span class="kd-chip kd-chip-kohle">${_kdEsc(r.filiale)}</span>` : ''}</div>
            <div><b>${_kdEsc(r.name)}</b></div>
            <div class="kd-dim" style="font-size:11.5px;white-space:nowrap">${r.telefon ? _kdEsc(r.telefon) : ''}</div>
            <div style="display:grid;grid-template-columns:135px auto;gap:6px;align-items:center">
                <span class="kd-dim" style="font-size:11.5px;white-space:nowrap">${r.buchungId && r.willkommenGesendetAm ? '📲 ' + _kdFmtTs(r.willkommenGesendetAm) : ''}</span>
                <div style="display:flex;gap:6px;align-items:center;flex-wrap:wrap">${_kdObChips(r)}</div>
            </div>
            <div style="display:flex;gap:8px;align-items:center;justify-self:end;flex-wrap:wrap">${aktionen}</div>
        </div>
        <div id="kdObMv${key}" style="display:none"></div>
        <div id="kdObDet${key}" style="display:none"></div>`;
}

// Inline-Verschieber: Ziel-Tag wählen → Kandidat via Termin-Endpoint (zieht
// Buchung + Wunschtermin mit), reine MA-Buchung via Kalender-Umbuchen.
function _kdObTagWechsel(key, kandidatId, buchungId) {
    const el = document.getElementById(`kdObMv${key}`);
    if (!el) return;
    if (el.style.display !== 'none') { el.style.display = 'none'; el.innerHTML = ''; return; }
    const ziele = (_kdHrTermine || []).filter(t => t.frei > 0);
    if (!ziele.length) { showToast('Kein anderer Termin mit freien Plätzen vorhanden.', 'error'); return; }
    const opts = ziele.map(t =>
        `<option value="${t.id}">${_kdFmtD(t.datum)} · ${t.von}${t.bis ? '–' + t.bis : ''} (${t.frei} frei)</option>`).join('');
    el.style.display = 'block';
    el.innerHTML = `
        <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap;background:rgba(255,255,255,0.6);border:1px solid rgba(60,55,48,0.14);border-radius:10px;padding:8px 10px;margin:4px 0 8px">
            <span style="font-size:12px;color:#646464;font-weight:600">Neuer Onboarding-Tag:</span>
            <select id="kdObMvSel${key}" style="${_kdInp};padding:4px 8px;font-size:12px;min-width:230px">${opts}</select>
            <button onclick="_kdObTagWechselSubmit('${key}', ${kandidatId ?? 'null'}, ${buchungId ?? 'null'})" style="${_kdBtnDark};font-size:12px;padding:5px 12px">Verschieben</button>
            <button onclick="_kdObTagWechsel('${key}')" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:10px;padding:5px 10px;font-size:12px;cursor:pointer;color:#3f3f3f">Abbrechen</button>
            <span style="font-size:11px;color:#8b8b8b">Der Einladungs-Link zeigt danach den neuen Tag — die Person muss neu bestätigen.</span>
        </div>`;
}

async function _kdObTagWechselSubmit(key, kandidatId, buchungId) {
    const neuerTerminId = parseInt(document.getElementById(`kdObMvSel${key}`)?.value, 10);
    if (!neuerTerminId) return;
    if (kandidatId) { await hrKandTermin(kandidatId, String(neuerTerminId)); return; }
    const r = await fetch(`/api/hr-interview/buchungen/${buchungId}/umbuchen`, {
        method: 'POST', headers: ah(), body: JSON.stringify({ neuerTerminId }),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Verschieben fehlgeschlagen.', 'error'); return; }
    showToast('Auf den neuen Onboarding-Tag verschoben.', 'success');
    hrKandReload();
}

function _kdObChips(r) {
    const chip = (cls, text) => `<span class="kd-chip ${cls}">${text}</span>`;
    if (r.abgeschlossenAm)
        return chip('kd-chip-grau', `✓ Onboarding abgeschlossen ${_kdFmtTs(r.abgeschlossenAm)}`);
    let c = '';
    if (!r.buchungId) {
        c += chip('kd-chip-gelb', 'Willkommenstag-SMS offen');
    } else {
        // 📲-Zeitstempel steht in der eigenen Unterspalte (_kdObRow) — hier nur Pillen.
        c += r.maAntwort === 'ANGENOMMEN'
            ? chip('kd-chip-gruen', '✓ Termin bestätigt')
            : chip('kd-chip-gelb', '⏳ unbestätigt');
    }
    if (r.verknuepft) c += chip('kd-chip-indigo', '✓ MA verknüpft');
    return c;
}

function _kdObToggle(key, kandidatId) {
    const el = document.getElementById(`kdObDet${key}`);
    if (!el) return;
    if (el.style.display !== 'none') { el.style.display = 'none'; el.innerHTML = ''; return; }
    const k = _kdHrList.find(x => x.id === kandidatId);
    el.style.display = 'block';
    el.innerHTML = `<div style="background:rgba(255,255,255,0.5);border:1px solid rgba(255,255,255,0.62);border-radius:12px;padding:12px 14px;margin:4px 0 8px">${k
        ? (k.status === 'ANGENOMMEN' ? _kdAngenommenInner(k) : _kdErledigtInner(k))
        : '<span style="color:#8b8b8b;font-size:12px">Keine Kandidaten-Details mehr vorhanden — reguläre/r MA.</span>'}</div>`;
}

function _kdObVerknuepfen(key, kandidatId) {
    const el = document.getElementById(`kdObDet${key}`);
    if (el && el.style.display === 'none') _kdObToggle(key, kandidatId);
    hrKandVorschlaege(kandidatId);
}

async function hrKandObAbschliessen(buchungId, name) {
    if (typeof liquidConfirm === 'function'
        && !await liquidConfirm(`Onboarding für ${name} abschliessen? Der GF erhält die Meldung ins Filial-Postfach — die Person läuft danach als regulärer MA weiter.`, { title: 'Onboarding abschliessen' })) return;
    const r = await fetch(`/api/kandidaten/onboarding-abschliessen/${buchungId}`, { method: 'POST', headers: ah() });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Abschliessen fehlgeschlagen.', 'error'); return; }
    showToast('Onboarding abgeschlossen — Meldung an den GF gesendet.', 'success');
    hrKandReload();
}

// Detail-Inhalt eines ANGENOMMENEN Kandidaten (aufklappbar in der Tages-Zeile).
function _kdAngenommenInner(k) {
    let wkStatus = '';
    if (k.willkommenGesendetAm) {
        const antwort = k.willkommenAntwort === 'ANGENOMMEN'
            ? '<span style="background:#dcfce7;color:#166534;border-radius:8px;padding:2px 10px;font-size:12px;font-weight:700">✓ Termin angenommen — fix gebucht</span>'
            : k.willkommenAntwort === 'ABGELEHNT'
                ? '<span style="background:#fecaca;color:#991b1b;border-radius:8px;padding:2px 10px;font-size:12px;font-weight:700">✕ Termin abgesagt — neu vereinbaren + erneut senden</span>'
                : '<span style="background:#fef9c3;color:#854d0e;border-radius:8px;padding:2px 10px;font-size:12px;font-weight:700">⏳ wartet auf Antwort</span>';
        wkStatus = `<div style="display:flex;gap:10px;align-items:center;margin-top:8px;flex-wrap:wrap;font-size:12.5px">
            <span>📲 Willkommenstag-SMS ${_kdFmtTs(k.willkommenGesendetAm)}</span>${antwort}</div>`;
    }
    const smsOk = !!k.willkommenGesendetAm && k.willkommenAntwort !== 'ABGELEHNT';
    const bestOk = k.willkommenAntwort === 'ANGENOMMEN';
    const step = (nr, text, done, aktiv) => `
        <div style="display:flex;align-items:flex-start;gap:10px;padding:3px 0">
            <span style="flex:0 0 22px;height:22px;border-radius:50%;display:inline-flex;align-items:center;justify-content:center;font-size:11.5px;font-weight:800;
                ${done ? 'background:#dcfce7;color:#166534' : aktiv ? 'background:#3f3f3f;color:#fff' : 'background:#f1efe9;color:#8b8b8b'}">${done ? '✓' : nr}</span>
            <span style="font-size:12.5px;color:${done ? '#8b8b8b' : '#3f3f3f'};padding-top:2px;${aktiv ? 'font-weight:700;' : ''}">${text}</span>
        </div>`;
    const schritte = `
        <div style="margin-top:10px;background:rgba(255,255,255,0.55);border:1px solid rgba(255,255,255,0.62);border-radius:12px;padding:10px 14px">
            <div style="font-size:10.5px;font-weight:800;letter-spacing:1px;text-transform:uppercase;color:#8b8b8b;margin-bottom:5px">Ablauf HR</div>
            ${step(1, 'Onboarding-Tag prüfen und Willkommenstag-SMS senden — der Kandidat bestätigt am Handy', smsOk, !smsOk)}
            ${step(2, 'Termin-Bestätigung des Kandidaten abwarten', bestOk, smsOk && !bestOk)}
            ${step(3, 'MA mit obigen Daten in easy@work erfassen · Sync/Import nach OneCrew', false, bestOk)}
            ${step(4, 'Mit dem importierten MA verknüpfen — Anhänge wandern in die Personalakte, die Termin-Buchung geht an den MA über', false, false)}
            ${step(5, 'Zum Onboarding-Tag AUSDRUCKEN: Vertrag · Erstunterweisung · Sicherheitsblatt · Lebensmittelhygiene — dazu Vertrags-SMS über «2 · Vertrags-SMS senden» · nach dem Tag: Onboarding abschliessen', false, false)}
        </div>`;
    return `
        ${_kdKopf(k)}${_kdDetails(k)}
        ${wkStatus}
        ${schritte}
        <div style="margin-top:12px;display:flex;gap:10px;align-items:center;flex-wrap:wrap">
            <button onclick="hrKandWillkommen(${k.id})" style="${_kdBtnDark};font-size:13px;padding:8px 16px">📱 Willkommenstag-SMS ${k.willkommenGesendetAm ? 'erneut senden' : 'senden'}</button>
            <button onclick="hrKandVorschlaege(${k.id})" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:8px 16px;font-size:13px;cursor:pointer;color:#3f3f3f;font-weight:600">🔗 Mit importiertem MA verknüpfen</button>
            <span style="flex:1"></span>
            <button onclick="hrKandZuruecknehmen(${k.id})" title="Annahme zurücknehmen — der Kandidat steht wieder unter «Zu prüfen»"
                    style="background:transparent;border:none;padding:6px 4px;font-size:12px;cursor:pointer;color:#8b8b8b;text-decoration:underline">↶ Entscheid zurücknehmen</button>
        </div>
        <div id="kdLink${k.id}" style="margin-top:6px"></div>`;
}

// Detail-Inhalt eines ERLEDIGTEN (verknüpften) Kandidaten.
function _kdErledigtInner(k) {
    return `
        ${_kdKopf(k)}
        <div style="display:flex;gap:12px;align-items:center;margin-top:6px;flex-wrap:wrap;font-size:12.5px">
            <span class="kd-chip kd-chip-indigo">✓ verknüpft ${_kdFmtTs(k.erledigtAm)}</span>
            <span class="kd-dim" style="font-size:11px">Kandidaten-Daten werden 30 Tage nach der Verknüpfung automatisch gelöscht</span>
            <span style="flex:1"></span>
            <a class="kd-link" onclick="hrKandLoeschen(${k.id}, '${_kdEsc(k.vorname)} ${_kdEsc(k.name)}')" style="font-size:12px;color:#991b1b">🗑 Jetzt löschen</a>
        </div>
        ${_kdNotizHtml(k)}`;
}

// Kandidaten-Daten sofort löschen (Walter 11.08.2026) — z.B. Test-Einträge.
// Der MA und seine Termin-Buchung bleiben unberührt.
async function hrKandLoeschen(id, name) {
    if (typeof liquidConfirm === 'function'
        && !await liquidConfirm(`Kandidaten-Daten von ${name} jetzt löschen (inkl. Anhänge)? Der verknüpfte MA und seine Termin-Buchung bleiben bestehen.`, { title: 'Kandidat löschen' })) return;
    const r = await fetch(`/api/kandidaten/${id}`, { method: 'DELETE', headers: ah() });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Löschen fehlgeschlagen.', 'error'); return; }
    showToast('Kandidaten-Daten gelöscht.', 'success');
    hrKandReload();
    if (typeof hrKandBadge === 'function') hrKandBadge();
}

// Notiz-Zeile (z.B. «hat sich nach der Absage nochmals gemeldet»).
function _kdNotizHtml(k) {
    return `
        <div style="display:flex;gap:8px;align-items:flex-end;margin-top:8px;flex-wrap:wrap">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px;flex:1;min-width:240px">Notiz
                <input id="kdNotiz${k.id}" value="${_kdEsc(k.notiz || '')}" placeholder="z.B. hat sich am … nochmals gemeldet" style="${_kdInp}"></label>
            <button onclick="hrKandNotiz(${k.id})" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:6px 12px;font-size:12px;cursor:pointer;color:#3f3f3f">💾 Notiz speichern</button>
        </div>`;
}

async function hrKandNotiz(id) {
    const notiz = document.getElementById(`kdNotiz${id}`)?.value || '';
    const r = await fetch(`/api/kandidaten/${id}/notiz`, {
        method: 'POST', headers: ah(), body: JSON.stringify({ notiz }),
    });
    if (!r.ok) { showToast('Notiz speichern fehlgeschlagen.', 'error'); return; }
    showToast('Notiz gespeichert.', 'success');
}

// Willkommenstag-SMS an den Kandidaten (Walter 11.08.2026): Einladung mit
// Bestätigungs-Link — VOR der easy@work-Erfassung.
async function hrKandWillkommen(id) {
    const k = _kdHrList.find(x => x.id === id);
    if (k && !k.wunschTerminId) { showToast('Zuerst oben einen Onboarding-Tag wählen.', 'error'); return; }
    if (k && !(k.telefon || '').trim()) { showToast('Für diesen Kandidaten ist keine Handynummer erfasst.', 'error'); return; }
    if (typeof liquidConfirm === 'function'
        && !await liquidConfirm(`Willkommenstag-SMS an ${k ? k.vorname + ' ' + k.name : 'den Kandidaten'} — ${k?.telefon || ''} — senden? Der Kandidat kann den Termin am Handy annehmen oder absagen.`, { title: 'Willkommenstag-Einladung' })) return;
    const r = await fetch(`/api/kandidaten/${id}/willkommen-sms`, { method: 'POST', headers: ah() });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Versand fehlgeschlagen.', 'error'); return; }
    showToast(`Willkommenstag-SMS an ${j.to} gesendet.` + (j.redirectedTo ? ` (Test-Umleitung: ${j.redirectedTo})` : ''), 'success');
    hrKandReload();
}

// Entscheid zurücknehmen (Walter 11.08.2026): Kandidat zurück zu «Zu prüfen».
async function hrKandZuruecknehmen(id) {
    if (typeof liquidConfirm === 'function'
        && !await liquidConfirm('Entscheid zurücknehmen? Der Kandidat steht danach wieder unter «Zu prüfen».', { title: 'Entscheid zurücknehmen' })) return;
    const r = await fetch(`/api/kandidaten/${id}/entscheid-zuruecknehmen`, { method: 'POST', headers: ah() });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Zurücknehmen fehlgeschlagen.', 'error'); return; }
    showToast('Entscheid zurückgenommen.', 'success');
    hrKandReload();
    if (typeof hrKandBadge === 'function') hrKandBadge();
}

async function hrKandAbsage(id, kanal) {
    if (typeof liquidConfirm === 'function'
        && !await liquidConfirm(`Absage per ${kanal === 'EMAIL' ? 'E-Mail' : 'SMS'} an den Kandidaten senden?`, { title: 'Absage' })) return;
    const r = await fetch(`/api/kandidaten/${id}/absage`, {
        method: 'POST', headers: ah(), body: JSON.stringify({ kanal }),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Versand fehlgeschlagen.', 'error'); return; }
    showToast('Absage gesendet.', 'success');
    hrKandReload();
}

async function hrKandVorschlaege(id) {
    const el = document.getElementById(`kdLink${id}`);
    if (!el) return;
    el.innerHTML = '<span style="color:#8b8b8b;font-size:12px">Suche importierte MA…</span>';
    try {
        const r = await fetch(`/api/kandidaten/${id}/ma-vorschlaege`, { headers: ah() });
        const list = await r.json();
        if (!r.ok) { el.textContent = 'Laden fehlgeschlagen.'; return; }
        if (!list.length) {
            el.innerHTML = '<span style="color:#854d0e;font-size:12.5px;background:#fef9c3;border:1px solid #fde68a;border-radius:8px;padding:4px 8px;display:inline-block">Kein passender MA gefunden — wurde er schon in easy@work erfasst und nach OneCrew importiert?</span>';
            return;
        }
        el.innerHTML = list.map(m => `
            <div style="display:flex;align-items:center;gap:10px;padding:4px 8px;border-bottom:1px solid rgba(60,55,48,0.08);font-size:12.5px">
                <b>${_kdEsc(m.name)}</b>
                <span style="color:#8b8b8b">${_kdEsc(m.employeeNumber || '')}</span>
                <span style="color:#8b8b8b">${m.entryDate ? 'Eintritt ' + _kdFmtD(m.entryDate) : ''}</span>
                <span style="flex:1"></span>
                <button onclick="hrKandZuordnung(${id}, ${m.id}, '${_kdEsc(m.name)}')" style="background:#166534;color:#fff;border:none;border-radius:10px;padding:4px 12px;font-size:12px;font-weight:700;cursor:pointer">Verknüpfen</button>
            </div>`).join('');
    } catch (_) { el.textContent = 'Verbindungsfehler.'; }
}

// ── Dokument-Zuordnung beim Verknüpfen (Walter 10.08.2026): pro Anhang
//    Ziel-Dokumenttyp + Beschreibung wählen — kein Pauschal-Übernehmen. ─────
let _kdTaxonomie = null;

async function _kdLadeTaxonomie() {
    if (_kdTaxonomie) return _kdTaxonomie;
    const r = await fetch('/api/documents/taxonomie', { headers: ah() });
    _kdTaxonomie = r.ok ? await r.json() : [];
    return _kdTaxonomie;
}

async function hrKandZuordnung(kandId, employeeId, maName) {
    const el = document.getElementById(`kdLink${kandId}`);
    if (!el) return;
    const k = _kdHrList.find(x => x.id === kandId);
    const doks = k?.dokumente || [];
    if (!doks.length) {
        // Keine Anhänge → direkt verknüpfen.
        hrKandVerknuepfen(kandId, employeeId, maName, []);
        return;
    }
    el.innerHTML = '<span style="color:#8b8b8b;font-size:12px">Dokumenttypen werden geladen…</span>';
    const tax = await _kdLadeTaxonomie();
    const typOpts = tax.map(kat =>
        `<optgroup label="${_kdEsc(kat.name)}">${(kat.typen || []).map(t =>
            `<option value="${t.id}">${_kdEsc(t.name)}</option>`).join('')}</optgroup>`).join('');
    el.innerHTML = `
        <div style="background:rgba(255,255,255,0.7);border:1px solid rgba(60,55,48,0.15);border-radius:10px;padding:10px;margin-top:6px">
            <div style="font-weight:700;font-size:12.5px;margin-bottom:6px">Dokumente für ${_kdEsc(maName)} zuordnen</div>
            ${doks.map(d => `
                <div style="display:flex;gap:8px;align-items:flex-end;flex-wrap:wrap;padding:5px 0;border-bottom:1px solid rgba(60,55,48,0.08)">
                    <label style="font-size:11px;color:#8b8b8b;display:flex;align-items:center;gap:5px;padding-bottom:7px">
                        <input type="checkbox" id="kdZuNehmen${d.id}" checked> übernehmen</label>
                    <a style="cursor:pointer;text-decoration:underline;color:#3f3f3f;font-size:12.5px;padding-bottom:7px;min-width:150px"
                       onclick="kdDokPreview(${d.id}, '${_kdEsc(d.name)}')">📎 ${_kdEsc(d.name)}</a>
                    <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Dokumenttyp
                        <select id="kdZuTyp${d.id}" style="${_kdInp}">${typOpts}</select></label>
                    <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px;flex:1;min-width:180px">Beschreibung
                        <input id="kdZuBem${d.id}" value="${_kdEsc(String(d.name).replace(/\.[^.]+$/, ''))}" style="${_kdInp}"></label>
                </div>`).join('')}
            <div style="display:flex;gap:8px;margin-top:10px">
                <button onclick='hrKandVerknuepfenSubmit(${kandId}, ${employeeId}, ${JSON.stringify(maName)})' style="background:#166534;color:#fff;border:none;border-radius:12px;padding:6px 16px;font-size:12.5px;font-weight:700;cursor:pointer">✓ Verknüpfen & übernehmen</button>
                <button onclick="document.getElementById('kdLink${kandId}').innerHTML=''" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:6px 12px;font-size:12.5px;cursor:pointer;color:#3f3f3f">Abbrechen</button>
            </div>
        </div>`;
}

function hrKandVerknuepfenSubmit(kandId, employeeId, maName) {
    const k = _kdHrList.find(x => x.id === kandId);
    const dokumente = (k?.dokumente || []).map(d => ({
        dokId: d.id,
        dokumentTypId: parseInt(document.getElementById(`kdZuTyp${d.id}`)?.value, 10) || 0,
        bemerkung: document.getElementById(`kdZuBem${d.id}`)?.value || null,
        uebernehmen: !!document.getElementById(`kdZuNehmen${d.id}`)?.checked,
    }));
    hrKandVerknuepfen(kandId, employeeId, maName, dokumente);
}

async function hrKandVerknuepfen(kandId, employeeId, maName, dokumente) {
    if (typeof liquidConfirm === 'function'
        && !await liquidConfirm(`Kandidat mit «${maName}» verknüpfen? Die zugeordneten Dokumente wandern in seine Personalakte; der Kandidat bleibt 30 Tage als «erledigt» sichtbar.`, { title: 'Verknüpfen' })) return;
    const r = await fetch(`/api/kandidaten/${kandId}/verknuepfen`, {
        method: 'POST', headers: ah(), body: JSON.stringify({ employeeId, dokumente: dokumente || [] }),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Verknüpfen fehlgeschlagen.', 'error'); return; }
    showToast(`${j.dokumente} Dokument(e) übernommen — weiter mit der Einladung im Onboarding-Kalender.`, 'success');
    hrKandReload();
    hrKandBadge();
}

async function hrKandEntscheid(id, angenommen) {
    const grund = document.getElementById(`kdGrund${id}`)?.value || '';
    if (!angenommen && !grund.trim()) { showToast('Bei Ablehnung bitte den Grund angeben.', 'error'); return; }
    if (typeof liquidConfirm === 'function'
        && !await liquidConfirm(angenommen ? 'Kandidat annehmen? Die Filiale wird via Postfach informiert.' : 'Kandidat ablehnen? Die Filiale wird via Postfach informiert.', { title: 'Kandidaten-Entscheid' })) return;
    const r = await fetch(`/api/kandidaten/${id}/entscheid`, {
        method: 'POST', headers: ah(), body: JSON.stringify({ angenommen, grund }),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Entscheid fehlgeschlagen.', 'error'); return; }
    showToast(angenommen ? 'Kandidat angenommen — jetzt MA in easy@work erfassen (HR).' : 'Kandidat abgelehnt.', 'success');
    hrKandReload();
    hrKandBadge();
}

function kdDokPreview(dokId, name) {
    if (typeof previewUrlFetch === 'function')
        previewUrlFetch(`/api/kandidaten/dokumente/${dokId}/preview`, name || 'Dokument', ah());
}
