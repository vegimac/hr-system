// ══════════════════════════════════════════════════════════════════════
// qst-import.js — Mirus „QST Auswertung" (.xls) Import
// ──────────────────────────────────────────────────────────────────────
// XLS hochladen → Preview (MA-Match per AHV, Tarif-Phasen-Vorschau,
// 3-Modi-Wahl für bestehende QST-Einträge) → Commit.
// Backend: /api/imports/qst/preview + /commit
// ══════════════════════════════════════════════════════════════════════
let _qstImportFile = null;
let _qstImportData = null;
let _qstManualMatches = {};   // ahvNr → empId

function qstImportInit() {
    document.getElementById('qstImportAlert').innerHTML = '';
    document.getElementById('qstImportSummary').innerHTML = '';
    document.getElementById('qstImportPreview').innerHTML = '';
    document.getElementById('qstImportCommitBtn').disabled = true;
    const inp = document.getElementById('qstImportFileInput');
    if (inp) inp.value = '';
    const yr = document.getElementById('qstImportYear');
    if (yr && !yr.value) yr.value = new Date().getFullYear();
    _qstImportFile = null;
    _qstImportData = null;
    _qstManualMatches = {};
}

async function qstImportPreview() {
    const inp = document.getElementById('qstImportFileInput');
    document.getElementById('qstImportAlert').innerHTML = '';
    if (!inp.files || inp.files.length === 0) {
        showPageAlert('qstImportAlert', 'Bitte eine QST-Auswertung wählen.', 'error');
        return;
    }
    const year = parseInt(document.getElementById('qstImportYear').value) || 0;
    if (year < 2000 || year > 2100) {
        showPageAlert('qstImportAlert', 'Bitte gültiges Jahr eingeben.', 'error');
        return;
    }
    _qstImportFile = inp.files[0];
    _qstManualMatches = {};

    const fd = new FormData();
    fd.append('file', _qstImportFile);
    fd.append('year', String(year));
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : 0;
    fd.append('companyProfileId', String(cpId));

    try {
        const r = await fetch('/api/imports/qst/preview', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken },
            body: fd
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            showPageAlert('qstImportAlert', 'Fehler: ' + (j.error || r.status), 'error');
            return;
        }
        _qstImportData = await r.json();
        renderQstImportPreview(_qstImportData);
        qstUpdateCommitButton();
    } catch (e) {
        showPageAlert('qstImportAlert', 'Netzwerkfehler: ' + e.message, 'error');
    }
}

function qstUpdateCommitButton() {
    if (!_qstImportData) return;
    const auto    = (_qstImportData.rows || []).filter(r => r.employeeId).length;
    const manual  = Object.keys(_qstManualMatches).length;
    document.getElementById('qstImportCommitBtn').disabled = (auto + manual) === 0;
}

function renderQstImportPreview(data) {
    const summary = document.getElementById('qstImportSummary');
    const preview = document.getElementById('qstImportPreview');
    summary.innerHTML = `
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px">
            <div style="background:#dbeafe;border:1px solid #93c5fd;border-radius:8px;padding:12px 14px;color:#1e40af">
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Format</div>
                <div style="font-size:22px;font-weight:700">${_e(data.formatErkannt || '?')}</div>
                <div style="font-size:11px;color:#1e40af">Jahr ${data.year}</div>
            </div>
            <div style="background:#dbeafe;border:1px solid #93c5fd;border-radius:8px;padding:12px 14px;color:#1e40af">
                <div style="font-size:24px;font-weight:700">${data.totalRows}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">MA total</div>
            </div>
            <div style="background:#dcfce7;border:1px solid #86efac;border-radius:8px;padding:12px 14px;color:#166534">
                <div style="font-size:24px;font-weight:700">${data.matched}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Neu importierbar</div>
            </div>
            <div style="background:#f1f5f9;border:1px solid #cbd5e1;border-radius:8px;padding:12px 14px;color:#475569">
                <div style="font-size:24px;font-weight:700">${data.existingSame || 0}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Bereits identisch</div>
            </div>
            <div style="background:#fef9c3;border:1px solid #fde047;border-radius:8px;padding:12px 14px;color:#854d0e">
                <div style="font-size:24px;font-weight:700">${data.existingDiff || 0}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Bestehend anders</div>
            </div>
            <div style="background:#fee2e2;border:1px solid #fca5a5;border-radius:8px;padding:12px 14px;color:#991b1b">
                <div style="font-size:24px;font-weight:700">${data.noMatch}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Kein MA-Treffer</div>
            </div>
            <div style="background:#ffedd5;border:1px solid #fdba74;border-radius:8px;padding:12px 14px;color:#9a3412">
                <div style="font-size:24px;font-weight:700">${data.ambiguous || 0}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Mehrere Treffer</div>
            </div>
        </div>
        ${(data.existingDiff || 0) > 0 ? `
        <div style="margin-top:14px;padding:12px 14px;background:#fef9c3;border:1px solid #fde047;border-radius:8px">
            <div style="font-weight:700;color:#713f12;margin-bottom:6px">Wie mit MA umgehen, die <strong>bereits einen QST-Eintrag</strong> haben?</div>
            <div style="display:flex;flex-direction:column;gap:6px;color:#422006;font-size:13px">
                <label style="display:flex;align-items:flex-start;gap:8px;cursor:pointer">
                    <input type="radio" name="qstImportMode" value="STRICT" checked style="margin-top:3px">
                    <span><strong>Überspringen</strong> — bestehende QST-Einträge bleiben unverändert.</span>
                </label>
                <label style="display:flex;align-items:flex-start;gap:8px;cursor:pointer">
                    <input type="radio" name="qstImportMode" value="APPEND" style="margin-top:3px">
                    <span><strong>Beenden + neu anlegen</strong> — bestehender Eintrag wird auf Beginn-Datum −1 geschlossen, neue Tarif-Phasen dahinter angelegt. Verlauf bleibt erhalten.</span>
                </label>
                <label style="display:flex;align-items:flex-start;gap:8px;cursor:pointer">
                    <input type="radio" name="qstImportMode" value="REPLACE" style="margin-top:3px">
                    <span><strong>Ersetzen</strong> — gesamte QST-History des MA wird gelöscht und durch die neuen Tarif-Phasen ersetzt.</span>
                </label>
            </div>
        </div>` : ''}`;

    const statusBadge = (s) => {
        const map = {
            OK:             { bg:'#dcfce7', fg:'#166534', label:'NEU' },
            EXISTING_SAME:  { bg:'#f1f5f9', fg:'#475569', label:'identisch' },
            EXISTING_DIFF:  { bg:'#fef9c3', fg:'#854d0e', label:'andere QST' },
            NO_MATCH:       { bg:'#fee2e2', fg:'#991b1b', label:'kein MA' },
            AMBIGUOUS:      { bg:'#ffedd5', fg:'#9a3412', label:'mehrere' },
            NO_DATA:        { bg:'#fef3c7', fg:'#92400e', label:'keine Daten' }
        };
        const m = map[s] || map.OK;
        return `<span style="background:${m.bg};color:${m.fg};padding:2px 7px;border-radius:7px;font-size:11px;font-weight:600">${m.label}</span>`;
    };

    const fmtDate = d => d ? new Date(d).toLocaleDateString('de-CH',{day:'2-digit',month:'2-digit',year:'numeric'}) : '–';

    preview.innerHTML = `
        <div class="card" style="padding:0;overflow:auto">
            <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                <thead>
                    <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0">
                        <th style="padding:9px 10px;text-align:left">AHV-Nr</th>
                        <th style="padding:9px 10px;text-align:left">Name (XLS)</th>
                        <th style="padding:9px 10px;text-align:left">Wohnort</th>
                        <th style="padding:9px 10px;text-align:left">Kt</th>
                        <th style="padding:9px 10px;text-align:left">MA im System</th>
                        <th style="padding:9px 10px;text-align:left">Geplante QST-Phasen</th>
                        <th style="padding:9px 10px;text-align:left">Status</th>
                    </tr>
                </thead>
                <tbody>
                    ${data.rows.map(r => renderQstRow(r, statusBadge, fmtDate)).join('')}
                </tbody>
            </table>
        </div>`;
}

function renderQstRow(r, statusBadge, fmtDate) {
    const needsPicker = r.status === 'NO_MATCH' || r.status === 'AMBIGUOUS';
    let maCell;
    if (needsPicker) {
        const selected = _qstManualMatches[r.ahvNumber] || '';
        const opts = (r.candidates || []).map(c => {
            const sel = String(c.employeeId) === String(selected) ? 'selected' : '';
            return `<option value="${c.employeeId}" ${sel}>${_e(c.firstName)} ${_e(c.lastName)} (Nr ${_e(c.employeeNumber)}${c.dateOfBirth ? ', *' + new Date(c.dateOfBirth).toLocaleDateString('de-CH') : ''}${c.isActive ? '' : ' [inaktiv]'})</option>`;
        }).join('');
        maCell = `<select onchange="qstSetManual('${r.ahvNumber}', this.value)"
                          style="width:100%;padding:5px;border:1px solid #cbd5e1;border-radius:5px;font-size:12px">
                      <option value="">— MA auswählen —</option>
                      ${opts}
                  </select>`;
    } else if (r.dbFirstName) {
        maCell = `<span style="color:#475569">${_e(r.dbFirstName)} ${_e(r.dbLastName)} <span style="color:#94a3b8;font-size:11px">(${_e(r.dbEmployeeNumber || '')})</span></span>`;
    } else {
        maCell = '<span style="color:#94a3b8">–</span>';
    }

    const phasenHtml = (r.phasen || []).length === 0
        ? '<span style="color:#94a3b8">–</span>'
        : (r.phasen || []).map((p, i, arr) => {
            const fromStr = fmtDate(p.validFrom);
            const toStr   = p.validTo ? fmtDate(p.validTo) : '<span style="color:#15803d;font-weight:600">offen</span>';
            return `<div style="margin-bottom:3px"><span style="background:#ede9fe;color:#5b21b6;padding:1px 8px;border-radius:9px;font-size:11px;font-weight:700">${_e(p.qstCode)}</span> ${fromStr} – ${toStr} <span style="color:#94a3b8;font-size:11px">(${p.monateImBlock} Mt.)</span></div>`;
          }).join('');

    return `
        <tr style="border-bottom:1px solid #f1f5f9">
            <td style="padding:7px 10px;font-family:monospace;font-size:11.5px">${_e(r.ahvNumber)}</td>
            <td style="padding:7px 10px"><b>${_e(r.xlsLastName)}</b> ${_e(r.xlsFirstName)}</td>
            <td style="padding:7px 10px;color:#64748b">${_e(r.wohnort || '')}</td>
            <td style="padding:7px 10px;font-family:monospace;font-weight:600">${_e(r.kanton || '')}</td>
            <td style="padding:7px 10px;min-width:220px">${maCell}</td>
            <td style="padding:7px 10px">${phasenHtml}</td>
            <td style="padding:7px 10px">${statusBadge(r.status)}<br><span style="color:#64748b;font-size:11px">${_e(r.note || '')}</span></td>
        </tr>`;
}

function qstSetManual(ahvNr, empId) {
    if (empId) _qstManualMatches[ahvNr] = parseInt(empId);
    else       delete _qstManualMatches[ahvNr];
    qstUpdateCommitButton();
}

async function qstImportCommit() {
    if (!_qstImportFile) {
        showPageAlert('qstImportAlert', 'Erst Datei analysieren.', 'error');
        return;
    }
    const year = parseInt(document.getElementById('qstImportYear').value) || 0;
    if (year < 2000 || year > 2100) {
        showPageAlert('qstImportAlert', 'Bitte gültiges Jahr eingeben.', 'error');
        return;
    }
    const modeEl = document.querySelector('input[name="qstImportMode"]:checked');
    const existingMode = modeEl ? modeEl.value : 'STRICT';
    const modeLabel = {
        STRICT:  'Bestehende QST-Einträge werden ÜBERSPRUNGEN.',
        APPEND:  'Bestehende QST-Einträge werden auf Beginn-Datum −1 GESCHLOSSEN, neue Tarif-Phasen dahinter.',
        REPLACE: 'Gesamte QST-History pro MA wird komplett GELÖSCHT und ersetzt.'
    }[existingMode];

    if (!confirm(`QST-Import jetzt durchführen?\n\n${modeLabel}\n\nIdentische Einträge werden in jedem Modus automatisch übersprungen.`)) return;

    const btn = document.getElementById('qstImportCommitBtn');
    btn.disabled = true; btn.textContent = 'Importiere…';

    const fd = new FormData();
    fd.append('file', _qstImportFile);
    fd.append('year', String(year));
    fd.append('existingMode', existingMode);
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : 0;
    fd.append('companyProfileId', String(cpId));
    const manual = Object.entries(_qstManualMatches).map(([k,v]) => `${k}:${v}`).join(',');
    if (manual) fd.append('manualMatches', manual);

    try {
        const r = await fetch('/api/imports/qst/commit', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken },
            body: fd
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) {
            showPageAlert('qstImportAlert', 'Fehler: ' + (j.error || r.status), 'error');
            return;
        }
        const teile = [
            j.added       ? `${j.added} QST-Phasen angelegt`           : null,
            j.replaced    ? `${j.replaced} MA ersetzt`                 : null,
            j.appended    ? `${j.appended} MA verlängert`              : null,
            j.skippedSame ? `${j.skippedSame} identisch übersprungen`  : null,
            j.skipped     ? `${j.skipped} sonst übersprungen`          : null
        ].filter(Boolean).join(' · ');
        let warn = '';
        if (j.warnings && j.warnings.length > 0) {
            warn = '<br><span style="color:#92400e">⚠ ' + j.warnings.slice(0,5).map(w => _e(w)).join('<br>⚠ ') +
                   (j.warnings.length > 5 ? `<br>… und ${j.warnings.length - 5} weitere Hinweise.` : '') + '</span>';
        }
        showPageAlert('qstImportAlert',
            `✓ Import abgeschlossen (${j.format}/${existingMode}). ${teile || 'Keine Änderungen.'}${warn}<br>Fenster wird in 4 Sekunden geschlossen…`,
            'success');
        setTimeout(() => {
            if (typeof showPage === 'function') showPage('admin-hub');
        }, 4000);
    } catch (e) {
        showPageAlert('qstImportAlert', 'Netzwerkfehler: ' + e.message, 'error');
    } finally {
        btn.disabled = false; btn.textContent = 'Import bestätigen';
    }
}
