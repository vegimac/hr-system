// ══════════════════════════════════════════════════════════════════════
// stammdaten-import.js — Mirus-BVG-Pension-XLSX → AHV + Zivilstand
// ══════════════════════════════════════════════════════════════════════
// Workflow:
//   1) User wählt Filiale + lädt XLSX hoch
//   2) /api/imports/stammdaten/preview parst + matched MAs
//   3) Preview-Tabelle zeigt pro Zeile: was würde gesetzt, was bleibt
//   4) Commit → /api/imports/stammdaten/commit applied selektierte Rows
//
// No-overwrite-Policy: bestehende AHV / Zivilstand werden NIE überschrieben.

let _stImportRows = [];
let _stImportFile = null;
// MA-Liste der Filiale (für den manuellen Picker bei NO_MATCH / AMBIGUOUS)
let _stImportBranchEmployees = [];
// Vom User manuell zugeordnete Zeilen: { rowNum: employeeId }
let _stImportManual = {};

function stImportInit() {
    // Filiale kommt ausschliesslich aus der globalen Sidebar (fixedCompanyProfileId).
    // Banner zeigt welche Filiale gerade aktiv ist; gewechselt wird oben links.
    stImportRefreshBanner();

    // Reset Preview-State (bei Page-Wechsel oder Filialwechsel)
    document.getElementById('stImportSummary').innerHTML = '';
    document.getElementById('stImportPreview').innerHTML = '';
    document.getElementById('stImportAlert').innerHTML = '';
    document.getElementById('stImportCommitBtn').disabled = true;
    _stImportRows = [];
    _stImportFile = null;
    _stImportBranchEmployees = [];
    _stImportManual = {};
    const fileInp = document.getElementById('stImportFileInput');
    if (fileInp) fileInp.value = '';
}

function stImportRefreshBanner() {
    const banner = document.getElementById('stImportBranchBanner');
    if (!banner) return;
    const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                  ? fixedCompanyProfileId : null;
    if (cid && typeof allBranches !== 'undefined' && Array.isArray(allBranches)) {
        const b = allBranches.find(x => x.id === cid);
        if (b) {
            const code = b.restaurantCode ? '#' + b.restaurantCode + ' · ' : '';
            const bn   = b.branchName || b.companyName || '–';
            banner.innerHTML = `<b>Filiale:</b> ${code}${bn} <span style="color:#94a3b8">— wird aus dem Hauptmenü übernommen</span>`;
            return;
        }
    }
    banner.innerHTML = `<span style="color:#92400e">⚠️ Keine Filiale gewählt — bitte oben links in der Sidebar eine Filiale wählen.</span>`;
}

async function stImportPreview() {
    // Filiale aus globalem Sidebar-Selektor (fixedCompanyProfileId).
    const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                       ? String(fixedCompanyProfileId) : '';
    const fileInput = document.getElementById('stImportFileInput');
    const alertBox = document.getElementById('stImportAlert');
    const commitBtn = document.getElementById('stImportCommitBtn');

    if (!branchId) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Bitte zuerst oben links in der Sidebar eine Filiale wählen.</div>`;
        return;
    }
    if (!fileInput.files.length) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Bitte eine BVG-XLSX-Datei wählen.</div>`;
        return;
    }
    _stImportFile = fileInput.files[0];

    commitBtn.disabled = true;
    alertBox.innerHTML = `
        <div style="padding:14px 18px;background:#fef3c7;border:1px solid #fde68a;border-radius:9px;display:flex;gap:14px;align-items:center">
            <div class="import-spinner" style="border-color:#fde68a;border-top-color:#a16207;width:24px;height:24px"></div>
            <div style="font-weight:600;color:#78350f;font-size:14px">XLSX wird analysiert…</div>
        </div>`;

    const fd = new FormData();
    fd.append('file', _stImportFile);
    fd.append('companyProfileId', branchId);

    try {
        const r = await fetch('/api/imports/stammdaten/preview', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd
        });
        if (!r.ok) {
            let errMsg = 'HTTP ' + r.status;
            try { const j = await r.json(); errMsg = j.error || errMsg; }
            catch { try { errMsg = await r.text(); } catch {} }
            alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Fehler: ${errMsg}</div>`;
            return;
        }
        const data = await r.json();
        _stImportRows = data.rows || [];
        _stImportBranchEmployees = data.branchEmployees || [];
        _stImportManual = {};   // bei neuem Preview zurücksetzen
        renderStImportPreview(data);
        alertBox.innerHTML = '';
    } catch (e) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Verbindungsfehler: ${e.message}</div>`;
    }
}

function renderStImportPreview(data) {
    const summary = document.getElementById('stImportSummary');
    const preview = document.getElementById('stImportPreview');

    summary.innerHTML = `
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px">
            ${tileCard('Total', data.totalRows, '#475569')}
            ${tileCard('Gefunden', data.matched, '#15803d')}
            ${tileCard('Importierbar', data.importable, '#1e40af')}
            ${tileCard('Manuell wählen', data.noMatch, '#b91c1c')}
        </div>`;

    if (!_stImportRows.length) {
        preview.innerHTML = '<div style="padding:20px;color:#64748b">Keine Zeilen.</div>';
        document.getElementById('stImportCommitBtn').disabled = true;
        return;
    }

    const rows = _stImportRows.map((r, idx) => {
        // NO_MATCH / AMBIGUOUS → eigene Zeile mit manuellem MA-Picker.
        if (r.status === 'NO_MATCH' || r.status === 'AMBIGUOUS') {
            return stImportManualRow(r, idx);
        }
        const bgRow = stImportRowBg(r.status, r);
        const willSetAddr = r.willSetStreet || r.willSetHouseNumber || r.willSetZipCode || r.willSetCity;
        const checkable = r.status === 'OK' && (r.willSetAhv || r.willSetMaritalStatus || r.willSetLanguage || willSetAddr || r.willSetReligion);
        const cb = checkable
            ? `<input type="checkbox" class="stImportSel" data-row="${r.rowNum}" checked
                       onchange="stImportUpdateCommitBtn()"
                       style="cursor:pointer;width:16px;height:16px">`
            : '';
        const ahvCell = r.willSetAhv
            ? `<span style="color:#15803d;font-weight:600">→ ${r.csvAhv || '–'}</span>`
            : (r.dbAhv ? `<span style="color:#94a3b8">${r.dbAhv} ✓</span>` : '<span style="color:#94a3b8">–</span>');
        const msCell = r.willSetMaritalStatus
            ? `<span style="color:#15803d;font-weight:600">→ ${r.csvMaritalStatusCode}</span>`
            : (r.dbMaritalStatus ? `<span style="color:#94a3b8">${r.dbMaritalStatus} ✓</span>` : '<span style="color:#94a3b8">–</span>');
        const langCell = r.willSetLanguage
            ? `<span style="color:#15803d;font-weight:600">→ ${r.csvLanguageCode}</span>`
            : (r.dbLanguageCode ? `<span style="color:#94a3b8">${r.dbLanguageCode} ✓</span>` : '<span style="color:#94a3b8">–</span>');
        // Adresse zusammengebaut: zeigt was würde gesetzt (grün) bzw. was schon
        // da ist (grau ✓). Pro Teil-Feld separat damit Walter sieht welcher
        // Bestandteil noch fehlt (z.B. nur PLZ leer, Strasse vorhanden).
        const addrParts = [];
        const addPart = (willSet, csvVal, dbVal, label) => {
            if (willSet && csvVal) {
                addrParts.push(`<span style="color:#15803d;font-weight:600">→ ${csvVal}</span>`);
            } else if (dbVal) {
                addrParts.push(`<span style="color:#94a3b8">${dbVal} ✓</span>`);
            }
        };
        addPart(r.willSetStreet,      r.csvStreet,      r.dbStreet,      'Strasse');
        addPart(r.willSetHouseNumber, r.csvHouseNumber, r.dbHouseNumber, 'Hausnr.');
        addPart(r.willSetZipCode,     r.csvZipCode,     r.dbZipCode,     'PLZ');
        addPart(r.willSetCity,        r.csvCity,        r.dbCity,        'Ort');
        const addrCell = addrParts.length > 0
            ? addrParts.join(' · ')
            : '<span style="color:#94a3b8">–</span>';

        // Konfession: BVG-File hat keine; wir setzen Default „keine" wenn DB leer.
        const relCell = r.willSetReligion
            ? '<span style="color:#15803d;font-weight:600">→ keine</span>'
            : (r.dbReligion ? `<span style="color:#94a3b8">${r.dbReligion} ✓</span>` : '<span style="color:#94a3b8">–</span>');
        // Match-Badge: AHV (grün) / NAME_DOB (gelb) / NAME_ONLY (orange — DOB weicht ab)
        const badgeColor = r.matchedBy === 'AHV'        ? ['#dcfce7', '#15803d']
                        : r.matchedBy === 'NAME_DOB'    ? ['#fef3c7', '#854d0e']
                        : r.matchedBy === 'NAME_ONLY'   ? ['#ffedd5', '#9a3412']
                        : ['#f1f5f9', '#475569'];
        const matchedBy = r.matchedBy
            ? `<span style="font-size:10px;background:${badgeColor[0]};color:${badgeColor[1]};padding:1px 6px;border-radius:8px;font-weight:600">${r.matchedBy}</span>`
            : '';
        return `<tr id="stImportRow-${idx}" style="background:${bgRow};border-bottom:1px solid #f1f5f9;font-size:12.5px">
            <td style="padding:8px 10px;text-align:center">${cb}</td>
            <td style="padding:8px 10px">${r.csvFirstName} ${r.csvLastName}</td>
            <td style="padding:8px 10px;color:#64748b">${r.employeeNumber || '–'}</td>
            <td style="padding:8px 10px">${ahvCell}</td>
            <td style="padding:8px 10px">${msCell}</td>
            <td style="padding:8px 10px">${langCell}</td>
            <td style="padding:8px 10px;font-size:11.5px">${addrCell}</td>
            <td style="padding:8px 10px">${relCell}</td>
            <td style="padding:8px 10px">${stImportStatusBadge(r.status)} ${matchedBy}</td>
            <td style="padding:8px 10px;color:#94a3b8;font-size:11px">${r.note || ''}</td>
        </tr>`;
    }).join('');

    preview.innerHTML = `
        <div style="background:white;border:1px solid #e2e8f0;border-radius:9px;overflow:hidden">
            <table style="width:100%;border-collapse:collapse">
                <thead>
                    <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0;font-size:12px;color:#475569;text-align:left">
                        <th style="padding:10px;width:40px;text-align:center">✓</th>
                        <th style="padding:10px">MA (Datei)</th>
                        <th style="padding:10px">Pers.-Nr.</th>
                        <th style="padding:10px">AHV</th>
                        <th style="padding:10px">Zivilstand</th>
                        <th style="padding:10px">Sprache</th>
                        <th style="padding:10px">Adresse</th>
                        <th style="padding:10px">Konfession</th>
                        <th style="padding:10px">Status</th>
                        <th style="padding:10px">Hinweis</th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        </div>`;

    // Commit-Button-Stand aus den tatsächlich angehakten Checkboxen ableiten
    // (auto-Matches sind per Default angehakt, manuell zugeordnete kommen
    // dazu sobald der User im Dropdown einen MA wählt).
    stImportUpdateCommitBtn();
}

// ── Manueller MA-Picker (NO_MATCH / AMBIGUOUS) ───────────────────────
// Walter-Vorgabe 14.05.2026: wenn der Importer keinen sicheren Match
// findet, soll der User den MA aus der Filial-Liste selbst wählen können.

function stFmtDob(iso) {
    if (!iso) return '';
    const p = String(iso).slice(0, 10).split('-');   // "1987-12-24"
    return p.length === 3 ? `${p[2]}.${p[1]}.${p[0]}` : iso;
}

function stImportEmployeeOptions(selectedId) {
    const opts = ['<option value="">— MA wählen —</option>'];
    for (const e of _stImportBranchEmployees) {
        const nr  = e.employeeNumber ? ` (${e.employeeNumber})` : '';
        const dob = e.dateOfBirth ? ' · ' + stFmtDob(e.dateOfBirth) : '';
        // Ausgetretene / inaktive MA markieren — sie sind wählbar (z.B.
        // Personaldossiers ohne Vertrag), aber klar als inaktiv erkennbar.
        const inact = (e.isActive === false) ? ' [inaktiv]' : '';
        const sel = (selectedId && e.id === selectedId) ? ' selected' : '';
        const name = `${e.firstName || ''} ${e.lastName || ''}`.trim();
        opts.push(`<option value="${e.id}"${sel}>${name}${nr}${dob}${inact}</option>`);
    }
    return opts.join('');
}

// Rendert eine Preview-Zeile mit Dropdown statt fixem Match.
function stImportManualRow(r, idx) {
    const csvDob = r.csvDateOfBirth ? ' · geb. ' + stFmtDob(r.csvDateOfBirth) : '';
    const ahvPending = r.csvAhv
        ? `<span style="color:#1e40af;font-weight:600">→ ${r.csvAhv}</span>`
        : '<span style="color:#94a3b8">–</span>';
    const msPending = r.csvMaritalStatusCode
        ? `<span style="color:#1e40af">→ ${r.csvMaritalStatusCode}</span>` : '<span style="color:#94a3b8">–</span>';
    const langPending = r.csvLanguageCode
        ? `<span style="color:#1e40af">→ ${r.csvLanguageCode}</span>` : '<span style="color:#94a3b8">–</span>';
    const addrBits = [r.csvStreet, r.csvHouseNumber, r.csvZipCode, r.csvCity].filter(Boolean);
    const addrPending = addrBits.length
        ? `<span style="color:#1e40af">→ ${addrBits.join(' ')}</span>` : '<span style="color:#94a3b8">–</span>';
    const isPicked = !!_stImportManual[r.rowNum];
    return `<tr id="stImportRow-${idx}" style="background:${isPicked ? '#eff6ff' : '#fef2f2'};border-bottom:1px solid #f1f5f9;font-size:12.5px">
        <td style="padding:8px 10px;text-align:center">
            <input type="checkbox" class="stImportSel" id="stImportCb-${r.rowNum}" data-row="${r.rowNum}"
                   ${isPicked ? 'checked' : 'disabled'} onchange="stImportUpdateCommitBtn()"
                   style="cursor:pointer;width:16px;height:16px">
        </td>
        <td style="padding:8px 10px">${r.csvFirstName} ${r.csvLastName}<span style="color:#94a3b8;font-size:11px">${csvDob}</span></td>
        <td style="padding:8px 10px" colspan="1">
            <select onchange="stImportPickEmployee(${r.rowNum}, this)"
                    style="width:100%;max-width:230px;padding:4px 6px;border:1px solid #cbd5e1;border-radius:6px;font-size:11.5px;font-family:inherit;background:white">
                ${stImportEmployeeOptions(_stImportManual[r.rowNum])}
            </select>
        </td>
        <td style="padding:8px 10px">${ahvPending}</td>
        <td style="padding:8px 10px">${msPending}</td>
        <td style="padding:8px 10px">${langPending}</td>
        <td style="padding:8px 10px;font-size:11.5px">${addrPending}</td>
        <td style="padding:8px 10px"><span style="color:#94a3b8">–</span></td>
        <td style="padding:8px 10px">${stImportStatusBadge(r.status)}</td>
        <td style="padding:8px 10px;color:#94a3b8;font-size:11px">${r.note || ''}</td>
    </tr>`;
}

// User hat im Dropdown einen MA gewählt (oder die Auswahl zurückgesetzt).
function stImportPickEmployee(rowNum, sel) {
    const empId = parseInt(sel.value, 10);
    const cb = document.getElementById('stImportCb-' + rowNum);
    const tr = cb ? cb.closest('tr') : null;
    if (empId) {
        _stImportManual[rowNum] = empId;
        if (cb) { cb.disabled = false; cb.checked = true; }
        if (tr) tr.style.background = '#eff6ff';
    } else {
        delete _stImportManual[rowNum];
        if (cb) { cb.disabled = true; cb.checked = false; }
        if (tr) tr.style.background = '#fef2f2';
    }
    stImportUpdateCommitBtn();
}

// Commit-Button-Zustand: Anzahl aller angehakten Zeilen (auto + manuell).
function stImportUpdateCommitBtn() {
    const n = document.querySelectorAll('.stImportSel:checked').length;
    const btn = document.getElementById('stImportCommitBtn');
    if (!btn) return;
    btn.disabled = n === 0;
    btn.textContent = n > 0 ? `Import bestätigen (${n})` : 'Import bestätigen';
}

function stImportRowBg(status, r) {
    if (status === 'NO_MATCH')   return '#fef2f2';
    if (status === 'AMBIGUOUS')  return '#fffbeb';
    if (status === 'NO_CHANGE')  return '#f8fafc';
    if (r && r.matchedBy === 'NAME_ONLY' && (r.willSetAhv || r.willSetMaritalStatus))
                                  return '#fff7ed';   // orange-leicht: prüfen
    if (r && (r.willSetAhv || r.willSetMaritalStatus)) return '#f0fdf4';
    return 'white';
}

function stImportStatusBadge(s) {
    const map = {
        'OK':         ['ok',           '#dcfce7', '#15803d'],
        'NO_MATCH':   ['MA fehlt',     '#fee2e2', '#b91c1c'],
        'AMBIGUOUS':  ['Mehrdeutig',   '#fef3c7', '#854d0e'],
        'NO_CHANGE':  ['unverändert',  '#f1f5f9', '#475569'],
        'INVALID_AHV':['AHV ungültig', '#fee2e2', '#b91c1c']
    };
    const v = map[s]; if (!v) return s;
    return `<span style="font-size:10px;background:${v[1]};color:${v[2]};padding:1px 7px;border-radius:8px;font-weight:600">${v[0]}</span>`;
}

// Helper für Summary-Kacheln (vereinfacht; reuse-fähig).
function tileCard(label, value, color) {
    return `<div style="background:white;border:1px solid #e2e8f0;border-radius:9px;padding:10px 14px">
        <div style="font-size:11px;color:#64748b;text-transform:uppercase;letter-spacing:.05em">${label}</div>
        <div style="font-size:22px;font-weight:700;color:${color};margin-top:2px">${value}</div>
    </div>`;
}

async function stImportCommit() {
    if (!_stImportFile) return;
    const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                       ? String(fixedCompanyProfileId) : '';
    if (!branchId) return;

    // Selektierte Rows einsammeln (auto-Matches + manuell zugeordnete)
    const selected = Array.from(document.querySelectorAll('.stImportSel:checked'))
                          .map(cb => cb.getAttribute('data-row'))
                          .filter(Boolean);
    if (!selected.length) return;

    // Manuelle MA-Zuordnungen für die selektierten Zeilen — Format
    // "rowNum:employeeId,rowNum:employeeId" (Backend: manualMatches).
    const selectedSet = new Set(selected.map(String));
    const manualMatches = Object.entries(_stImportManual)
        .filter(([rowNum]) => selectedSet.has(String(rowNum)))
        .map(([rowNum, empId]) => `${rowNum}:${empId}`)
        .join(',');

    const fd = new FormData();
    fd.append('file', _stImportFile);
    fd.append('companyProfileId', branchId);
    fd.append('rowNums', selected.join(','));
    if (manualMatches) fd.append('manualMatches', manualMatches);

    const btn = document.getElementById('stImportCommitBtn');
    btn.disabled = true;
    btn.textContent = 'Importieren…';

    try {
        const r = await fetch('/api/imports/stammdaten/commit', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd
        });
        if (!r.ok) {
            let errMsg = 'HTTP ' + r.status;
            try { const j = await r.json(); errMsg = j.error || errMsg; } catch {}
            alert('Fehler: ' + errMsg);
            btn.disabled = false;
            btn.textContent = 'Import bestätigen';
            return;
        }
        const data = await r.json();
        document.getElementById('stImportAlert').innerHTML = `
            <div style="padding:14px 18px;background:#dcfce7;border:1px solid #86efac;color:#15803d;border-radius:9px;font-size:14px">
                <b>Import erfolgreich:</b> ${data.updatedAhv} AHV-Nummern, ${data.updatedMaritalStatus} Zivilstände, ${data.updatedLanguage || 0} Sprachen, ${data.updatedAddress || 0} Adressen, ${data.updatedReligion || 0} Konfessionen gesetzt
                ${data.skipped > 0 ? ` · ${data.skipped} übersprungen` : ''}. Fenster wird in 2 Sekunden geschlossen…
            </div>`;
        document.getElementById('stImportPreview').innerHTML = '';
        document.getElementById('stImportSummary').innerHTML = '';
        btn.textContent = 'Import bestätigen';
        _stImportRows = [];
        // Walter-Vorgabe 13.05.2026: nach erfolgreichem Import zurück zur Übersicht.
        setTimeout(() => { if (typeof showPage === 'function') showPage('admin-hub'); }, 2000);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        btn.disabled = false;
        btn.textContent = 'Import bestätigen';
    }
}
