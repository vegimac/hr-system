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
                <input id="kdTelefon" placeholder="+41 79 …" style="${_kdInp}"></label>
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
            <span id="kdFileList" style="font-size:12px;color:#646464;margin-left:8px"></span>
        </div>
        <div style="display:flex;justify-content:flex-end;margin-top:14px">
            <button onclick="kdSubmit()" style="${_kdBtnDark}">An HR senden</button>
        </div>
        <div style="font-weight:700;margin:16px 0 4px">Meine eingereichten Kandidaten</div>
        <div id="kdMeineListe" style="font-size:12.5px;color:#3f3f3f">Wird geladen…</div>`;
    kdMeineListe();
}

function kdFilesPicked(files) {
    _kdFiles = Array.from(files || []);
    const el = document.getElementById('kdFileList');
    if (el) el.textContent = _kdFiles.length ? _kdFiles.map(f => f.name).join(' · ') : '';
}

async function kdSubmit() {
    const vorname = (document.getElementById('kdVorname')?.value || '').trim();
    const name = (document.getElementById('kdName')?.value || '').trim();
    if (!vorname || !name) { showToast('Vorname und Name angeben.', 'error'); return; }
    const fd = new FormData();
    fd.append('companyProfileId', document.getElementById('kdCp')?.value || '');
    fd.append('vorname', vorname);
    fd.append('name', name);
    fd.append('telefon', document.getElementById('kdTelefon')?.value || '');
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

async function hrKandReload() {
    const body = document.getElementById('hrKandModalBody');
    if (!body) return;
    body.innerHTML = '<span style="color:#8b8b8b">Wird geladen…</span>';
    try {
        const r = await fetch('/api/kandidaten?status=NEU', { headers: ah() });
        const list = await r.json();
        if (!r.ok) { body.innerHTML = 'Laden fehlgeschlagen.'; return; }
        if (!list.length) {
            body.innerHTML = '<span style="color:#8b8b8b">Keine unbearbeiteten Kandidaten. 🎉</span>';
            hrKandBadge();
            return;
        }
        body.innerHTML = list.map(k => {
            const ausb = KAND_AUSBILDUNG.find(([c]) => c === k.lgavAusbildung)?.[1] || k.lgavAusbildung || '–';
            const doks = (k.dokumente || []).map(d =>
                `<a style="cursor:pointer;text-decoration:underline;color:#3f3f3f" onclick="kdDokPreview(${d.id}, '${_kdEsc(d.name)}')">📎 ${_kdEsc(d.name)}</a>`).join(' · ');
            return `
            <div style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.14);border-radius:12px;padding:10px 12px;margin-bottom:10px">
                <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
                    <b style="font-size:14px">${_kdEsc(k.vorname)} ${_kdEsc(k.name)}</b>
                    <span style="background:#f1efe9;border-radius:8px;padding:1px 8px;font-size:11.5px;color:#646464">${_kdEsc(k.filiale)}</span>
                    ${k.telefon ? `<span style="color:#646464;font-size:12px">📞 ${_kdEsc(k.telefon)}</span>` : ''}
                    <span style="color:#b0aca4;font-size:11px">eingereicht ${_kdFmtTs(k.createdAt)} von ${_kdEsc(k.createdBy || '')}</span>
                </div>
                <div style="display:flex;gap:14px;flex-wrap:wrap;margin-top:6px;font-size:12.5px;color:#3f3f3f">
                    <span><b>Eintritt ab:</b> ${k.fruehesterEintritt ? _kdFmtD(k.fruehesterEintritt) : '–'}</span>
                    <span><b>Ausbildung:</b> ${_kdEsc(ausb)}</span>
                    <span><b>Wunschtermin:</b> ${k.wunschTermin ? _kdEsc(k.wunschTermin) : '–'}</span>
                </div>
                ${k.bemerkung ? `<div style="margin-top:4px;font-size:12.5px;color:#646464">💬 ${_kdEsc(k.bemerkung)}</div>` : ''}
                ${doks ? `<div style="margin-top:6px;font-size:12.5px">${doks}</div>` : ''}
                <div style="display:flex;gap:8px;align-items:flex-end;margin-top:10px;flex-wrap:wrap">
                    <button onclick="hrKandEntscheid(${k.id}, true)" style="background:#166534;color:#fff;border:none;border-radius:12px;padding:6px 16px;font-size:12.5px;font-weight:700;cursor:pointer">✓ Annehmen</button>
                    <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Ablehnungsgrund
                        <input id="kdGrund${k.id}" style="${_kdInp};min-width:220px"></label>
                    <button onclick="hrKandEntscheid(${k.id}, false)" style="background:#fff;border:1.5px solid #991b1b;color:#991b1b;border-radius:12px;padding:6px 14px;font-size:12.5px;font-weight:700;cursor:pointer">✕ Ablehnen</button>
                </div>
            </div>`;
        }).join('');
    } catch (_) { body.innerHTML = '<span style="color:#991b1b">Verbindungsfehler.</span>'; }
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
    showToast(angenommen ? 'Kandidat angenommen — nächster Schritt: MA in easy@work erfassen.' : 'Kandidat abgelehnt.', 'success');
    hrKandReload();
    hrKandBadge();
}

function kdDokPreview(dokId, name) {
    if (typeof previewUrlFetch === 'function')
        previewUrlFetch(`/api/kandidaten/dokumente/${dokId}/preview`, name || 'Dokument', ah());
}
