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

async function openKandidatModal() {
    _ivModalShell('kdModal', '📨 Kandidat an HR senden', 720);
    document.getElementById('kdModal').style.display = 'flex';
    _kdFiles = [];
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
            <input type="file" id="kdFiles" accept="application/pdf,image/*" multiple style="display:none" onchange="kdFilesPicked(this.files)">
            <div id="kdFileList" style="font-size:12px;color:#646464;margin-top:6px"></div>
        </div>
        <div style="display:flex;justify-content:flex-end;margin-top:14px">
            <button onclick="kdSubmit()" style="${_kdBtnDark}">An HR senden</button>
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
    const r = await fetch('/api/kandidaten', {
        method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd,
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Senden fehlgeschlagen.', 'error'); return; }
    showToast('Kandidat an HR gesendet.', 'success');
    _kdFiles = [];
    openKandidatModal();
}

async function kdMeineListe() {
    const el = document.getElementById('kdMeineListe');
    if (!el) return;
    try {
        const r = await fetch('/api/kandidaten', { headers: ah() });
        const list = await r.json();
        if (!r.ok) { el.textContent = 'Laden fehlgeschlagen.'; return; }
        if (!list.length) { el.innerHTML = '<span style="color:#8b8b8b">Noch keine Kandidaten eingereicht.</span>'; return; }
        el.innerHTML = list.map(k => `
            <div style="display:flex;align-items:center;gap:10px;padding:5px 8px;border-bottom:1px solid rgba(60,55,48,0.08);flex-wrap:wrap">
                <b>${_kdEsc(k.vorname)} ${_kdEsc(k.name)}</b>
                <span style="color:#8b8b8b">${k.fruehesterEintritt ? 'ab ' + _kdFmtD(k.fruehesterEintritt) : ''}</span>
                ${_kdStatusPill(k)}
                <span style="color:#b0aca4;font-size:11px">${_kdFmtTs(k.createdAt)}</span>
            </div>`).join('');
    } catch (_) { el.textContent = 'Verbindungsfehler.'; }
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
    _ivModalShell('hrKandModal', '📨 Kandidaten prüfen', 860);
    document.getElementById('hrKandModal').style.display = 'flex';
    hrKandReload();
}

function _kdKopf(k) {
    return `
        <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <b style="font-size:14px">${_kdEsc(k.vorname)} ${_kdEsc(k.name)}</b>
            <span style="background:#f1efe9;border-radius:8px;padding:1px 8px;font-size:11.5px;color:#646464">${_kdEsc(k.filiale)}</span>
            ${k.telefon ? `<span style="color:#646464;font-size:12px">📞 ${_kdEsc(k.telefon)}</span>` : ''}
            ${k.email ? `<span style="color:#646464;font-size:12px">✉️ ${_kdEsc(k.email)}</span>` : ''}
            <span style="color:#b0aca4;font-size:11px">eingereicht ${_kdFmtTs(k.createdAt)} von ${_kdEsc(k.createdBy || '')}</span>
        </div>`;
}

function _kdDetails(k) {
    const ausb = KAND_AUSBILDUNG.find(([c]) => c === k.lgavAusbildung)?.[1] || k.lgavAusbildung || '–';
    const doks = (k.dokumente || []).map(d =>
        `<a style="cursor:pointer;text-decoration:underline;color:#3f3f3f" onclick="kdDokPreview(${d.id}, '${_kdEsc(d.name)}')">📎 ${_kdEsc(d.name)}</a>`).join(' · ');
    return `
        <div style="display:flex;gap:14px;flex-wrap:wrap;margin-top:6px;font-size:12.5px;color:#3f3f3f">
            <span><b>Eintritt ab:</b> ${k.fruehesterEintritt ? _kdFmtD(k.fruehesterEintritt) : '–'}</span>
            <span><b>Ausbildung:</b> ${_kdEsc(ausb)}</span>
            <span><b>Wunschtermin:</b> ${k.wunschTermin ? _kdEsc(k.wunschTermin) : '–'}</span>
        </div>
        ${k.bemerkung ? `<div style="margin-top:4px;font-size:12.5px;color:#646464">💬 ${_kdEsc(k.bemerkung)}</div>` : ''}
        ${doks ? `<div style="margin-top:6px;font-size:12.5px">${doks}</div>` : ''}`;
}

async function hrKandReload() {
    const body = document.getElementById('hrKandModalBody');
    if (!body) return;
    body.innerHTML = '<span style="color:#8b8b8b">Wird geladen…</span>';
    try {
        const r = await fetch('/api/kandidaten', { headers: ah() });
        const list = await r.json();
        if (!r.ok) { body.innerHTML = 'Laden fehlgeschlagen.'; return; }
        const neu = list.filter(k => k.status === 'NEU');
        const angenommen = list.filter(k => k.status === 'ANGENOMMEN');
        const abgelehnt = list.filter(k => k.status === 'ABGELEHNT');
        const erledigt = list.filter(k => k.status === 'ERLEDIGT');
        const card = (inner) => `<div style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.14);border-radius:12px;padding:10px 12px;margin-bottom:10px">${inner}</div>`;
        const titel = (t) => `<div style="font-weight:800;font-size:13.5px;margin:14px 0 6px;color:#3f3f3f">${t}</div>`;
        let html = '';

        // ── 1) Zu prüfen ────────────────────────────────────────────────
        html += titel(`Zu prüfen (${neu.length})`);
        html += neu.length ? neu.map(k => card(`
            ${_kdKopf(k)}${_kdDetails(k)}
            <div style="display:flex;gap:8px;align-items:flex-end;margin-top:10px;flex-wrap:wrap">
                <button onclick="hrKandEntscheid(${k.id}, true)" style="background:#166534;color:#fff;border:none;border-radius:12px;padding:6px 16px;font-size:12.5px;font-weight:700;cursor:pointer">✓ Annehmen</button>
                <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Ablehnungsgrund
                    <input id="kdGrund${k.id}" style="${_kdInp};min-width:220px"></label>
                <button onclick="hrKandEntscheid(${k.id}, false)" style="background:#fff;border:1.5px solid #991b1b;color:#991b1b;border-radius:12px;padding:6px 14px;font-size:12.5px;font-weight:700;cursor:pointer">✕ Ablehnen</button>
            </div>`)).join('')
            : '<span style="color:#8b8b8b;font-size:12.5px">Keine unbearbeiteten Kandidaten. 🎉</span>';

        // ── 2) Angenommen: in easy erfassen → importieren → verknüpfen ──
        html += titel(`Angenommen — in easy@work erfassen & importieren (${angenommen.length})`);
        html += angenommen.length ? angenommen.map(k => card(`
            ${_kdKopf(k)}${_kdDetails(k)}
            <div style="margin-top:8px;background:#eef2ff;border:1px solid #c7d2fe;border-radius:10px;padding:8px;font-size:12.5px;color:#3730a3">
                <b>Nächste Schritte (HR):</b> 1. MA mit obigen Daten in <b>easy@work</b> erfassen ·
                2. easy@work-Sync/Import nach OneCrew · 3. unten mit dem importierten MA verknüpfen —
                die Anhänge wandern in seine Personalakte, der Kandidat wird gelöscht. Danach: Einladung
                über den Onboarding-Kalender.
            </div>
            <div style="margin-top:8px">
                <button onclick="hrKandVorschlaege(${k.id})" style="${_kdBtnDark};font-size:12.5px;padding:6px 14px">🔗 Mit importiertem MA verknüpfen</button>
                <div id="kdLink${k.id}" style="margin-top:6px"></div>
            </div>`)).join('')
            : '<span style="color:#8b8b8b;font-size:12.5px">Keine offenen Annahmen.</span>';

        // ── 3) Abgelehnt: Absage senden (Auto-Löschung nach 30 Tagen) ───
        html += titel(`Abgelehnt — Absage senden (${abgelehnt.length})`);
        html += abgelehnt.length ? abgelehnt.map(k => card(`
            ${_kdKopf(k)}
            <div style="margin-top:4px;font-size:12.5px;color:#646464">Grund: ${_kdEsc(k.ablehnungsgrund || '–')}</div>
            <div style="display:flex;gap:8px;align-items:center;margin-top:8px;flex-wrap:wrap">
                ${k.absageGesendetAm
                    ? `<span style="background:#dcfce7;color:#166534;border-radius:8px;padding:2px 10px;font-size:12px;font-weight:700">✓ Absage per ${k.absageKanal === 'EMAIL' ? 'E-Mail' : 'SMS'} gesendet ${_kdFmtTs(k.absageGesendetAm)}</span>`
                    : `${k.email ? `<button onclick="hrKandAbsage(${k.id}, 'EMAIL')" style="${_kdBtnDark};font-size:12.5px;padding:6px 14px">✉️ Absage per E-Mail</button>` : ''}
                       ${k.telefon ? `<button onclick="hrKandAbsage(${k.id}, 'SMS')" style="${_kdBtnDark};font-size:12.5px;padding:6px 14px">📱 Absage per SMS</button>` : ''}
                       ${(!k.email && !k.telefon) ? '<span style="color:#991b1b;font-size:12px">Weder E-Mail noch Telefon erfasst — Absage bitte anders zustellen.</span>' : ''}`}
                <span style="color:#b0aca4;font-size:11px">wird 30 Tage nach dem Entscheid automatisch gelöscht</span>
            </div>
            ${_kdNotizHtml(k)}`)).join('')
            : '<span style="color:#8b8b8b;font-size:12.5px">Keine offenen Absagen.</span>';

        // ── 4) Erledigt (verknüpft — Referenz, Auto-Löschung nach 30 Tagen) ─
        html += titel(`Erledigt — mit MA verknüpft (${erledigt.length})`);
        html += erledigt.length ? erledigt.map(k => card(`
            ${_kdKopf(k)}
            <div style="display:flex;gap:12px;align-items:center;margin-top:6px;flex-wrap:wrap;font-size:12.5px">
                <span style="background:#e0e7ff;color:#3730a3;border-radius:8px;padding:2px 10px;font-weight:700">✓ verknüpft ${_kdFmtTs(k.erledigtAm)}</span>
                <span style="color:#b0aca4;font-size:11px">wird 30 Tage später automatisch gelöscht</span>
            </div>
            ${_kdNotizHtml(k)}`)).join('')
            : '<span style="color:#8b8b8b;font-size:12.5px">Keine erledigten Kandidaten.</span>';

        body.innerHTML = html;
    } catch (_) { body.innerHTML = '<span style="color:#991b1b">Verbindungsfehler.</span>'; }
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
                <button onclick="hrKandVerknuepfen(${id}, ${m.id}, '${_kdEsc(m.name)}')" style="background:#166534;color:#fff;border:none;border-radius:10px;padding:4px 12px;font-size:12px;font-weight:700;cursor:pointer">Verknüpfen</button>
            </div>`).join('');
    } catch (_) { el.textContent = 'Verbindungsfehler.'; }
}

async function hrKandVerknuepfen(kandId, employeeId, maName) {
    if (typeof liquidConfirm === 'function'
        && !await liquidConfirm(`Kandidat mit «${maName}» verknüpfen? Die Anhänge wandern in seine Personalakte; der Kandidat bleibt 30 Tage als «erledigt» sichtbar.`, { title: 'Verknüpfen' })) return;
    const r = await fetch(`/api/kandidaten/${kandId}/verknuepfen`, {
        method: 'POST', headers: ah(), body: JSON.stringify({ employeeId }),
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
