// ══════════════════════════════════════════════════════════════════════
// roster-absence-import.js — Dienstplan-XLS → Absenzen erfassen
// ══════════════════════════════════════════════════════════════════════
// Workflow:
//   1) User wählt Filiale (globale Sidebar) + lädt Dienstplan-XLS hoch
//   2) /api/imports/roster-absences/preview parst + konsolidiert + matcht MA
//   3) Vorschau-Tabelle: pro Absenz-Span eine Zeile (MA, Typ, Zeitraum, Tage,
//      Stunden, Status). NO_MATCH/AMBIGUOUS → manueller MA-Picker.
//   4) Commit → /api/imports/roster-absences/commit legt die Absence-Records an
//
// Codes im Plan: FE = Ferien, KR = Krankheit, UN = Unfall. Konsekutive gleiche
// Tage werden zu einer Absenz zusammengefasst. Dubletten (gleiche Absenz schon
// erfasst) werden erkannt und nicht erneut importiert.

let _raRows = [];
let _raFile = null;
let _raBranchEmployees = [];           // MA-Liste der Filiale für den manuellen Picker
let _raManual = {};                    // { rowNum: employeeId }

const _RA_TYPE_BADGE = {
    FERIEN:     ['#dcfce7', '#15803d', 'Ferien'],
    FEIERTAG:   ['#fef3c7', '#854d0e', 'Feiertag'],
    KRANK:      ['#fee2e2', '#b91c1c', 'Krankheit'],
    UNFALL:     ['#ffedd5', '#9a3412', 'Unfall'],
    MUTT_VATER: ['#ede9fe', '#5b21b6', 'Mutter-/Vaterschaft'],
    FREI_KOMP:  ['#e0e7ff', '#5a5348', 'Frei-Kompensation'],
    BEZ_ABSENZ: ['#cffafe', '#155e75', 'Bezahlte Absenz'],
};

function rosterImportInit() {
    rosterImportRefreshBanner();
    document.getElementById('rosterImportSummary').innerHTML = '';
    document.getElementById('rosterImportPreview').innerHTML = '';
    document.getElementById('rosterImportAlert').innerHTML = '';
    document.getElementById('rosterImportPeriodInfo').innerHTML = '';
    document.getElementById('rosterImportCommitBtn').disabled = true;
    _raRows = [];
    _raFile = null;
    _raBranchEmployees = [];
    _raManual = {};
    const fileInp = document.getElementById('rosterImportFileInput');
    if (fileInp) fileInp.value = '';
}

function rosterImportRefreshBanner() {
    const banner = document.getElementById('rosterImportBranchBanner');
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

function _raFmtDate(iso) {
    if (!iso) return '';
    const p = String(iso).slice(0, 10).split('-');
    return p.length === 3 ? `${p[2]}.${p[1]}.${p[0]}` : iso;
}

async function rosterImportPreview() {
    const branchId  = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                        ? String(fixedCompanyProfileId) : '';
    const fileInput = document.getElementById('rosterImportFileInput');
    const alertBox  = document.getElementById('rosterImportAlert');
    const commitBtn = document.getElementById('rosterImportCommitBtn');

    if (!branchId) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Bitte zuerst oben links in der Sidebar eine Filiale wählen.</div>`;
        return;
    }
    if (!fileInput.files.length) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Bitte eine Dienstplan-Datei (XLS/XLSX) wählen.</div>`;
        return;
    }
    _raFile = fileInput.files[0];

    commitBtn.disabled = true;
    alertBox.innerHTML = `
        <div style="padding:14px 18px;background:#fef3c7;border:1px solid #fde68a;border-radius:9px;display:flex;gap:14px;align-items:center">
            <div class="import-spinner" style="border-color:#fde68a;border-top-color:#a16207;width:24px;height:24px"></div>
            <div style="font-weight:600;color:#78350f;font-size:14px">Dienstplan wird analysiert…</div>
        </div>`;

    const fd = new FormData();
    fd.append('file', _raFile);
    fd.append('companyProfileId', branchId);

    try {
        const r = await fetch('/api/imports/roster-absences/preview', {
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
        _raRows = data.rows || [];
        _raBranchEmployees = data.branchEmployees || [];
        _raManual = {};
        renderRosterImportPreview(data);
        alertBox.innerHTML = '';
    } catch (e) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Verbindungsfehler: ${e.message}</div>`;
    }
}

function renderRosterImportPreview(data) {
    const summary    = document.getElementById('rosterImportSummary');
    const preview    = document.getElementById('rosterImportPreview');
    const periodInfo = document.getElementById('rosterImportPeriodInfo');

    if (periodInfo) {
        periodInfo.innerHTML = (data.periodFrom && data.periodTo)
            ? `<span style="color:#475569">Dienstplan-Zeitraum: <b>${_raFmtDate(data.periodFrom)} – ${_raFmtDate(data.periodTo)}</b></span>`
            : '';
    }

    summary.innerHTML = `
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px">
            ${_raTile('Absenzen gefunden', data.totalRows, '#475569')}
            ${_raTile('MA zugeordnet', data.matched, '#15803d')}
            ${_raTile('Importierbar', data.importable, '#6b6152')}
            ${_raTile('Manuell wählen', data.noMatch, '#b91c1c')}
        </div>`;

    if (!_raRows.length) {
        preview.innerHTML = '<div style="padding:20px;color:#64748b">Keine Absenzen im Dienstplan gefunden.</div>';
        document.getElementById('rosterImportCommitBtn').disabled = true;
        return;
    }

    const rows = _raRows.map((r, idx) => {
        if (r.status === 'NO_MATCH' || r.status === 'AMBIGUOUS') {
            return _raManualRow(r, idx);
        }
        const bg = _raRowBg(r.status);
        const checkable = r.status === 'OK' && r.employeeId != null;
        const cb = checkable
            ? `<input type="checkbox" class="raSel" data-row="${r.rowNum}" checked
                       onchange="rosterImportUpdateCommitBtn()"
                       style="cursor:pointer;width:16px;height:16px">`
            : '';
        const maCell = r.employeeId
            ? `${r.dbFirstName || ''} ${r.dbLastName || ''}`.trim()
              + (r.employeeNumber ? ` <span style="color:#94a3b8">(${r.employeeNumber})</span>` : '')
              + (r.employmentModel ? ` <span style="font-size:10px;background:#f1f5f9;color:#475569;padding:1px 6px;border-radius:8px">${r.employmentModel}</span>` : '')
            : '<span style="color:#94a3b8">–</span>';
        const hoursCell = (r.hoursCredited && r.hoursCredited > 0)
            ? `<span style="color:#15803d;font-weight:600">+${Number(r.hoursCredited).toFixed(2)} h</span>`
            : '<span style="color:#94a3b8">–</span>';
        return `<tr style="background:${bg};border-bottom:1px solid #f1f5f9;font-size:12.5px">
            <td style="padding:8px 10px;text-align:center">${cb}</td>
            <td style="padding:8px 10px">${r.rawName}</td>
            <td style="padding:8px 10px">${maCell}</td>
            <td style="padding:8px 10px">${_raTypeBadge(r.absenceType)}</td>
            <td style="padding:8px 10px;white-space:nowrap">${_raFmtDate(r.dateFrom)} – ${_raFmtDate(r.dateTo)}</td>
            <td style="padding:8px 10px;text-align:center;color:#475569">${r.dayCount}</td>
            <td style="padding:8px 10px">${hoursCell}</td>
            <td style="padding:8px 10px">${_raStatusBadge(r.status)}</td>
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
                        <th style="padding:10px">MA (System)</th>
                        <th style="padding:10px">Typ</th>
                        <th style="padding:10px">Zeitraum</th>
                        <th style="padding:10px;text-align:center">Tage</th>
                        <th style="padding:10px">Stunden</th>
                        <th style="padding:10px">Status</th>
                        <th style="padding:10px">Hinweis</th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        </div>`;

    rosterImportUpdateCommitBtn();
}

// ── Manueller MA-Picker (NO_MATCH / AMBIGUOUS) ───────────────────────
function _raEmployeeOptions(selectedId) {
    const opts = ['<option value="">— MA wählen —</option>'];
    for (const e of _raBranchEmployees) {
        const nr    = e.employeeNumber ? ` (${e.employeeNumber})` : '';
        const inact = (e.isActive === false) ? ' [inaktiv]' : '';
        const sel   = (selectedId && e.id === selectedId) ? ' selected' : '';
        const name  = `${e.firstName || ''} ${e.lastName || ''}`.trim();
        opts.push(`<option value="${e.id}"${sel}>${name}${nr}${inact}</option>`);
    }
    return opts.join('');
}

function _raManualRow(r, idx) {
    const isPicked = !!_raManual[r.rowNum];
    return `<tr style="background:${isPicked ? '#f6f3ee' : '#fef2f2'};border-bottom:1px solid #f1f5f9;font-size:12.5px">
        <td style="padding:8px 10px;text-align:center">
            <input type="checkbox" class="raSel" id="raCb-${r.rowNum}" data-row="${r.rowNum}"
                   ${isPicked ? 'checked' : 'disabled'} onchange="rosterImportUpdateCommitBtn()"
                   style="cursor:pointer;width:16px;height:16px">
        </td>
        <td style="padding:8px 10px">${r.rawName}</td>
        <td style="padding:8px 10px">
            <select onchange="rosterImportPickEmployee(${r.rowNum}, this)"
                    style="width:100%;max-width:230px;padding:4px 6px;border:1px solid #cbd5e1;border-radius:6px;font-size:11.5px;font-family:inherit;background:white">
                ${_raEmployeeOptions(_raManual[r.rowNum])}
            </select>
        </td>
        <td style="padding:8px 10px">${_raTypeBadge(r.absenceType)}</td>
        <td style="padding:8px 10px;white-space:nowrap">${_raFmtDate(r.dateFrom)} – ${_raFmtDate(r.dateTo)}</td>
        <td style="padding:8px 10px;text-align:center;color:#475569">${r.dayCount}</td>
        <td style="padding:8px 10px"><span style="color:#94a3b8">–</span></td>
        <td style="padding:8px 10px">${_raStatusBadge(r.status)}</td>
        <td style="padding:8px 10px;color:#94a3b8;font-size:11px">${r.note || ''}</td>
    </tr>`;
}

function rosterImportPickEmployee(rowNum, sel) {
    const empId = parseInt(sel.value, 10);
    const cb = document.getElementById('raCb-' + rowNum);
    const tr = cb ? cb.closest('tr') : null;
    if (empId) {
        _raManual[rowNum] = empId;
        if (cb) { cb.disabled = false; cb.checked = true; }
        if (tr) tr.style.background = '#f6f3ee';
    } else {
        delete _raManual[rowNum];
        if (cb) { cb.disabled = true; cb.checked = false; }
        if (tr) tr.style.background = '#fef2f2';
    }
    rosterImportUpdateCommitBtn();
}

function rosterImportUpdateCommitBtn() {
    const n = document.querySelectorAll('.raSel:checked').length;
    const btn = document.getElementById('rosterImportCommitBtn');
    if (!btn) return;
    btn.disabled = n === 0;
    btn.textContent = n > 0 ? `Absenzen erfassen (${n})` : 'Absenzen erfassen';
}

function _raRowBg(status) {
    if (status === 'DUPLICATE')    return '#f8fafc';
    if (status === 'UNKNOWN_CODE') return '#fffbeb';
    if (status === 'OK')           return '#f0fdf4';
    return 'white';
}

function _raTypeBadge(type) {
    const v = _RA_TYPE_BADGE[type];
    if (!v) return `<span style="color:#94a3b8">${type || '–'}</span>`;
    return `<span style="font-size:11px;background:${v[0]};color:${v[1]};padding:2px 8px;border-radius:8px;font-weight:600">${v[2]}</span>`;
}

function _raStatusBadge(s) {
    const map = {
        'OK':           ['bereit',       '#dcfce7', '#15803d'],
        'NO_MATCH':     ['MA fehlt',     '#fee2e2', '#b91c1c'],
        'AMBIGUOUS':    ['Mehrdeutig',   '#fef3c7', '#854d0e'],
        'DUPLICATE':    ['schon erfasst','#f1f5f9', '#475569'],
        'UNKNOWN_CODE': ['Code unbekannt','#fef3c7', '#854d0e'],
    };
    const v = map[s]; if (!v) return s;
    return `<span style="font-size:10px;background:${v[1]};color:${v[2]};padding:1px 7px;border-radius:8px;font-weight:600">${v[0]}</span>`;
}

function _raTile(label, value, color) {
    return `<div style="background:white;border:1px solid #e2e8f0;border-radius:9px;padding:10px 14px">
        <div style="font-size:11px;color:#64748b;text-transform:uppercase;letter-spacing:.05em">${label}</div>
        <div style="font-size:22px;font-weight:700;color:${color};margin-top:2px">${value}</div>
    </div>`;
}

async function rosterImportCommit() {
    if (!_raFile) return;
    const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                       ? String(fixedCompanyProfileId) : '';
    if (!branchId) return;

    const selected = Array.from(document.querySelectorAll('.raSel:checked'))
                          .map(cb => cb.getAttribute('data-row'))
                          .filter(Boolean);
    if (!selected.length) return;

    const selectedSet = new Set(selected.map(String));
    const manualMatches = Object.entries(_raManual)
        .filter(([rowNum]) => selectedSet.has(String(rowNum)))
        .map(([rowNum, empId]) => `${rowNum}:${empId}`)
        .join(',');

    const fd = new FormData();
    fd.append('file', _raFile);
    fd.append('companyProfileId', branchId);
    fd.append('rowNums', selected.join(','));
    if (manualMatches) fd.append('manualMatches', manualMatches);

    const btn = document.getElementById('rosterImportCommitBtn');
    btn.disabled = true;
    btn.textContent = 'Erfassen…';

    try {
        const r = await fetch('/api/imports/roster-absences/commit', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd
        });
        if (!r.ok) {
            let errMsg = 'HTTP ' + r.status;
            try { const j = await r.json(); errMsg = j.error || errMsg; } catch {}
            alert('Fehler: ' + errMsg);
            btn.disabled = false;
            btn.textContent = 'Absenzen erfassen';
            return;
        }
        const data = await r.json();
        const locked = data.lockedSkipped || 0;
        const lockedNote = locked > 0
            ? `<div style="margin-top:8px;padding:10px 14px;background:#fef3c7;border:1px solid #fcd34d;color:#854d0e;border-radius:9px;font-size:13px">
                   <b>${locked} Absenz(en) nicht importiert</b> — betreffen eine bereits abgeschlossene oder in Verarbeitung befindliche Lohnperiode:
                   <ul style="margin:6px 0 0;padding-left:18px">${(data.lockedMessages || []).map(m => `<li>${m}</li>`).join('')}</ul>
               </div>`
            : '';
        document.getElementById('rosterImportAlert').innerHTML = `
            <div style="padding:14px 18px;background:#dcfce7;border:1px solid #86efac;color:#15803d;border-radius:9px;font-size:14px">
                <b>Import:</b> ${data.created} Absenzen erfasst${data.duplicates > 0 ? `, ${data.duplicates} Dubletten übersprungen` : ''}${data.skipped > 0 ? `, ${data.skipped} ohne MA-Zuordnung übersprungen` : ''}.${locked > 0 ? '' : ' Fenster wird in 2 Sekunden geschlossen…'}
            </div>${lockedNote}`;
        document.getElementById('rosterImportPreview').innerHTML = '';
        document.getElementById('rosterImportSummary').innerHTML = '';
        document.getElementById('rosterImportPeriodInfo').innerHTML = '';
        btn.textContent = 'Absenzen erfassen';
        _raRows = [];
        // Bei gesperrten Perioden NICHT automatisch schliessen — der User soll
        // die Meldung lesen können.
        if (locked === 0) setTimeout(() => { if (typeof showPage === 'function') showPage('admin-hub'); }, 2000);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        btn.disabled = false;
        btn.textContent = 'Absenzen erfassen';
    }
}
