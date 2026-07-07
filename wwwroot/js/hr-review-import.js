// ══════════════════════════════════════════════════════════════════════
// hr-review-import.js — Mirus HR-Review (.xls) Import
// ──────────────────────────────────────────────────────────────────────
// XLS hochladen → Preview (MA-Match, Picker für NO_MATCH/AMBIGUOUS,
// 3-Modi-Wahl für bestehende Bewilligungen) → Commit.
// Backend: /api/imports/hr-review/preview + /commit
// ══════════════════════════════════════════════════════════════════════
let _hrrImportFile = null;
let _hrrImportData = null;
// Manuelle MA-Zuordnungen pro Zeile (rowNum → empId).
let _hrrManualMatches = {};

function hrrImportInit() {
    document.getElementById('hrrImportAlert').innerHTML = '';
    document.getElementById('hrrImportSummary').innerHTML = '';
    document.getElementById('hrrImportPreview').innerHTML = '';
    document.getElementById('hrrImportCommitBtn').disabled = true;
    const inp = document.getElementById('hrrImportFileInput');
    if (inp) inp.value = '';
    // Beginn-Datum mit heute vorbelegen.
    const vf = document.getElementById('hrrImportValidFrom');
    if (vf && !vf.value) vf.value = new Date().toISOString().slice(0,10);
    _hrrImportFile = null;
    _hrrImportData = null;
    _hrrManualMatches = {};
}

async function hrrImportPreview() {
    const inp = document.getElementById('hrrImportFileInput');
    document.getElementById('hrrImportAlert').innerHTML = '';
    if (!inp.files || inp.files.length === 0) {
        showPageAlert('hrrImportAlert', 'Bitte eine HR-Review-Datei wählen.', 'error');
        return;
    }
    const validFrom = document.getElementById('hrrImportValidFrom').value;
    if (!validFrom) {
        showPageAlert('hrrImportAlert', 'Bitte Beginn-Datum für die Bewilligungs-Verlaufseinträge angeben.', 'error');
        return;
    }
    _hrrImportFile = inp.files[0];
    _hrrManualMatches = {};

    const fd = new FormData();
    fd.append('file', _hrrImportFile);
    // Walter-Vorgabe 07.06.2026: Filial-Filter — Picker und Auto-Match nur
    // gegen MA der aktuell gewählten Filiale.
    const cpId = typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0;
    fd.append('companyProfileId', String(cpId));

    try {
        const r = await fetch('/api/imports/hr-review/preview', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken },
            body: fd
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            showPageAlert('hrrImportAlert', 'Fehler: ' + (j.error || r.status), 'error');
            return;
        }
        _hrrImportData = await r.json();
        renderHrrImportPreview(_hrrImportData);
        hrrUpdateCommitButton();
    } catch (e) {
        showPageAlert('hrrImportAlert', 'Netzwerkfehler: ' + e.message, 'error');
    }
}

function hrrUpdateCommitButton() {
    if (!_hrrImportData) return;
    const d = _hrrImportData;
    // Importierbar = OK + NO_PERMIT + EXISTING_SAME + EXISTING_DIFF + (NO_MATCH/AMBIGUOUS mit manueller Auswahl)
    const manualCount = Object.keys(_hrrManualMatches).length;
    const autoMatched = (d.rows || []).filter(r => r.employeeId !== null && r.employeeId !== undefined).length;
    const total = autoMatched + manualCount;
    document.getElementById('hrrImportCommitBtn').disabled = total === 0;
}

function renderHrrImportPreview(data) {
    const summary = document.getElementById('hrrImportSummary');
    const preview = document.getElementById('hrrImportPreview');
    summary.innerHTML = `
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px">
            <div style="background:#ece9e2;border:1px solid #d0c8b8;border-radius:8px;padding:12px 14px;color:#6b6152">
                <div style="font-size:24px;font-weight:700">${data.totalRows}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Zeilen total</div>
            </div>
            <div style="background:#dcfce7;border:1px solid #86efac;border-radius:8px;padding:12px 14px;color:#166534">
                <div style="font-size:24px;font-weight:700">${data.matched}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Auto-Match (neu)</div>
            </div>
            <div style="background:#f1f5f9;border:1px solid #cbd5e1;border-radius:8px;padding:12px 14px;color:#475569">
                <div style="font-size:24px;font-weight:700">${data.existingSame || 0}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Bewilligung identisch</div>
            </div>
            <div style="background:#fef9c3;border:1px solid #fde047;border-radius:8px;padding:12px 14px;color:#854d0e">
                <div style="font-size:24px;font-weight:700">${data.existingDiff || 0}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Bewilligung anders</div>
            </div>
            <div style="background:#fee2e2;border:1px solid #fca5a5;border-radius:8px;padding:12px 14px;color:#991b1b">
                <div style="font-size:24px;font-weight:700">${data.noMatch}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Kein MA-Treffer</div>
            </div>
            <div style="background:#ffedd5;border:1px solid #fdba74;border-radius:8px;padding:12px 14px;color:#9a3412">
                <div style="font-size:24px;font-weight:700">${data.ambiguous || 0}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Mehrere MA-Treffer</div>
            </div>
            <div style="background:#fef3c7;border:1px solid #fbbf24;border-radius:8px;padding:12px 14px;color:#92400e">
                <div style="font-size:24px;font-weight:700">${data.unknown || 0}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Bewilligung unklar</div>
            </div>
        </div>
        ${(data.existingDiff || 0) > 0 ? `
        <div style="margin-top:14px;padding:12px 14px;background:#fef9c3;border:1px solid #fde047;border-radius:8px">
            <div style="font-weight:700;color:#713f12;margin-bottom:6px">Wie mit MA umgehen, die <strong>bereits eine andere</strong> Bewilligung haben?</div>
            <div style="display:flex;flex-direction:column;gap:6px;color:#422006;font-size:13px">
                <label style="display:flex;align-items:flex-start;gap:8px;cursor:pointer">
                    <input type="radio" name="hrrImportMode" value="STRICT" checked style="margin-top:3px">
                    <span><strong>Überspringen</strong> — bestehende Bewilligungen bleiben unverändert. Andere Felder (Geburtsdatum, Nationalität, Eintritt, Austritt) werden trotzdem übernommen.</span>
                </label>
                <label style="display:flex;align-items:flex-start;gap:8px;cursor:pointer">
                    <input type="radio" name="hrrImportMode" value="APPEND" style="margin-top:3px">
                    <span><strong>Beenden + neu anlegen</strong> — bestehender Bewilligungseintrag wird auf Beginn-Datum −1 geschlossen, neuer dahinter angelegt. Verlauf bleibt erhalten.</span>
                </label>
                <label style="display:flex;align-items:flex-start;gap:8px;cursor:pointer">
                    <input type="radio" name="hrrImportMode" value="REPLACE" style="margin-top:3px">
                    <span><strong>Ersetzen</strong> — gesamte Bewilligungs-History des MA wird gelöscht und durch HR-Review-Wert ersetzt. Verlauf geht verloren.</span>
                </label>
            </div>
        </div>` : ''}`;

    const statusBadge = (s) => {
        const map = {
            OK:             { bg:'#dcfce7', fg:'#166534', label:'NEU' },
            NO_PERMIT:      { bg:'#ece9e2', fg:'#6b6152', label:'CH/keine' },
            EXISTING_SAME:  { bg:'#f1f5f9', fg:'#475569', label:'identisch' },
            EXISTING_DIFF:  { bg:'#fef9c3', fg:'#854d0e', label:'andere Bew.' },
            NO_MATCH:       { bg:'#fee2e2', fg:'#991b1b', label:'kein MA' },
            AMBIGUOUS:      { bg:'#ffedd5', fg:'#9a3412', label:'mehrere' },
            UNKNOWN_PERMIT: { bg:'#fef3c7', fg:'#92400e', label:'Bew. unklar' }
        };
        const m = map[s] || map.OK;
        return `<span style="background:${m.bg};color:${m.fg};padding:2px 7px;border-radius:7px;font-size:11px;font-weight:600">${m.label}</span>`;
    };

    const fmtDate = d => d ? new Date(d).toLocaleDateString('de-CH', {day:'2-digit',month:'2-digit',year:'numeric'}) : '–';

    preview.innerHTML = `
        <div class="card" style="padding:0;overflow:auto">
            <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                <thead>
                    <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0">
                        <th style="padding:9px 10px;text-align:left">Name (XLS)</th>
                        <th style="padding:9px 10px;text-align:left">Geb.</th>
                        <th style="padding:9px 10px;text-align:left">Nat</th>
                        <th style="padding:9px 10px;text-align:left">MA in System</th>
                        <th style="padding:9px 10px;text-align:left">Bewilligung</th>
                        <th style="padding:9px 10px;text-align:left">Ablauf</th>
                        <th style="padding:9px 10px;text-align:left">Eintritt</th>
                        <th style="padding:9px 10px;text-align:left">Austritt</th>
                        <th style="padding:9px 10px;text-align:left">Status</th>
                    </tr>
                </thead>
                <tbody>
                    ${data.rows.map(r => renderHrrRow(r, statusBadge, fmtDate)).join('')}
                </tbody>
            </table>
        </div>`;
}

function renderHrrRow(r, statusBadge, fmtDate) {
    const needsPicker = r.status === 'NO_MATCH' || r.status === 'AMBIGUOUS';
    let maCell;
    if (needsPicker) {
        const opts = (r.candidates || []).map(c =>
            `<option value="${c.employeeId}">${_e(c.firstName)} ${_e(c.lastName)} (Nr ${_e(c.employeeNumber)}${c.dateOfBirth ? ', *' + new Date(c.dateOfBirth).toLocaleDateString('de-CH') : ''}${c.isActive ? '' : ' [inaktiv]'})</option>`
        ).join('');
        const selected = _hrrManualMatches[r.rowNum] || '';
        maCell = `<select onchange="hrrSetManual(${r.rowNum}, this.value)"
                          style="width:100%;padding:5px;border:1px solid #cbd5e1;border-radius:5px;font-size:12px">
                      <option value="">— MA auswählen —</option>
                      ${(r.candidates || []).map(c => {
                          const sel = String(c.employeeId) === String(selected) ? 'selected' : '';
                          return `<option value="${c.employeeId}" ${sel}>${_e(c.firstName)} ${_e(c.lastName)} (Nr ${_e(c.employeeNumber)}${c.dateOfBirth ? ', *' + new Date(c.dateOfBirth).toLocaleDateString('de-CH') : ''}${c.isActive ? '' : ' [inaktiv]'})</option>`;
                      }).join('')}
                  </select>`;
    } else if (r.dbFirstName) {
        maCell = `<span style="color:#475569">${_e(r.dbFirstName)} ${_e(r.dbLastName)} <span style="color:#94a3b8;font-size:11px">(${_e(r.dbEmployeeNumber || '')})</span></span>`;
    } else {
        maCell = '<span style="color:#94a3b8">–</span>';
    }
    return `
        <tr style="border-bottom:1px solid #f1f5f9">
            <td style="padding:7px 10px"><b>${_e(r.csvFirstName)} ${_e(r.csvLastName)}</b></td>
            <td style="padding:7px 10px;color:#64748b;font-size:11.5px">${fmtDate(r.csvDateOfBirth)}</td>
            <td style="padding:7px 10px;font-family:monospace;font-size:11.5px">${_e(r.csvNationalityCode || '–')}</td>
            <td style="padding:7px 10px;min-width:200px">${maCell}</td>
            <td style="padding:7px 10px"><b>${_e(r.permitCode || '–')}</b> <span style="color:#94a3b8;font-size:11px">${_e(r.permitText || '')}</span></td>
            <td style="padding:7px 10px;font-size:11.5px">${fmtDate(r.permitExpiry)}</td>
            <td style="padding:7px 10px;color:#64748b;font-size:11.5px">${fmtDate(r.entryDate)}</td>
            <td style="padding:7px 10px;color:#64748b;font-size:11.5px">${fmtDate(r.exitDate)}</td>
            <td style="padding:7px 10px">${statusBadge(r.status)}<br><span style="color:#64748b;font-size:11px">${_e(r.note || '')}</span></td>
        </tr>`;
}

function hrrSetManual(rowNum, empId) {
    if (empId) _hrrManualMatches[rowNum] = parseInt(empId);
    else       delete _hrrManualMatches[rowNum];
    hrrUpdateCommitButton();
}

async function hrrImportCommit() {
    if (!_hrrImportFile) {
        showPageAlert('hrrImportAlert', 'Erst Datei analysieren.', 'error');
        return;
    }
    const validFrom = document.getElementById('hrrImportValidFrom').value;
    if (!validFrom) {
        showPageAlert('hrrImportAlert', 'Bitte Beginn-Datum angeben.', 'error');
        return;
    }
    const modeEl = document.querySelector('input[name="hrrImportMode"]:checked');
    const existingMode = modeEl ? modeEl.value : 'STRICT';
    const modeLabel = {
        STRICT:  'Bestehende Bewilligungen werden ÜBERSPRUNGEN. Übrige Felder werden trotzdem übernommen.',
        APPEND:  'Bestehende Bewilligungen werden BEENDET (Bis = Beginn-Datum −1), neue Einträge dahinter.',
        REPLACE: 'Bestehende Bewilligungs-History wird komplett GELÖSCHT und durch HR-Review ersetzt.'
    }[existingMode];

    if (!confirm(`HR-Review-Import jetzt durchführen?\n\n${modeLabel}\n\nGeburtsdatum, Nationalität, Eintritt und Austritt werden für ALLE gematchten MA übernommen. Der Aktiv-Status wird NICHT geändert.`)) return;

    const btn = document.getElementById('hrrImportCommitBtn');
    btn.disabled = true; btn.textContent = 'Importiere…';

    const fd = new FormData();
    fd.append('file', _hrrImportFile);
    fd.append('validFrom', validFrom);
    fd.append('existingMode', existingMode);
    const cpId = typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0;
    fd.append('companyProfileId', String(cpId));
    // Manuelle Matches als "rowNum:empId,rowNum:empId" packen.
    const manual = Object.entries(_hrrManualMatches).map(([k,v]) => `${k}:${v}`).join(',');
    if (manual) fd.append('manualMatches', manual);

    try {
        const r = await fetch('/api/imports/hr-review/commit', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken },
            body: fd
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) {
            showPageAlert('hrrImportAlert', 'Fehler: ' + (j.error || r.status), 'error');
            return;
        }
        const teile = [
            j.birthUpdated      ? `${j.birthUpdated} Geburtsdaten`         : null,
            j.nationalityUpdated? `${j.nationalityUpdated} Nationalitäten` : null,
            j.entryUpdated      ? `${j.entryUpdated} Eintritte`            : null,
            j.exitUpdated       ? `${j.exitUpdated} Austritte`             : null,
            j.permitAdded       ? `${j.permitAdded} neue Bewilligungen`    : null,
            j.permitReplaced    ? `${j.permitReplaced} Bewilligungen ersetzt` : null,
            j.permitAppended    ? `${j.permitAppended} Bewilligungen verlängert` : null,
            j.permitSkippedExisting ? `${j.permitSkippedExisting} Bewilligungen übersprungen` : null,
            j.skipped           ? `${j.skipped} Zeilen übersprungen`       : null
        ].filter(Boolean).join(' · ');
        let warn = '';
        if (j.warnings && j.warnings.length > 0) {
            warn = '<br><span style="color:#92400e">⚠ ' + j.warnings.slice(0,5).map(w => _e(w)).join('<br>⚠ ') +
                   (j.warnings.length > 5 ? `<br>… und ${j.warnings.length - 5} weitere Hinweise.` : '') + '</span>';
        }
        showPageAlert('hrrImportAlert',
            `✓ Import abgeschlossen (${existingMode}). ${teile || 'Keine Änderungen.'}.${warn}<br>Fenster wird in 4 Sekunden geschlossen…`,
            'success');
        setTimeout(() => {
            if (typeof showPage === 'function') showPage('admin-hub');
        }, 4000);
    } catch (e) {
        showPageAlert('hrrImportAlert', 'Netzwerkfehler: ' + e.message, 'error');
    } finally {
        btn.disabled = false; btn.textContent = 'Import bestätigen';
    }
}
