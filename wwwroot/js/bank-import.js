// ══════════════════════════════════════════════════════════════════════
// bank-import.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════
// ADMIN: BANKVERBINDUNGS-IMPORT (Mirus-Lohnabrechnungs-XLS → IBAN pro MA)
// ══════════════════════════════════════════════════════════════════════
// Backend: /api/imports/bank/preview + /api/imports/bank/commit
// Strategie: XLS parsen → Vor-/Nachname + IBAN extrahieren → Bank via
// /api/banks/lookup ermitteln → Vorschau → User wählt → commit.

let _bankImportRows = [];   // Letzter Preview-Stand für Commit
let _bankImportEmps = [];   // Aktive MA der Filiale für manuelle Zuweisung bei NO_EMPLOYEE

function bankImportInit() {
    // Walter-Vorgabe 13.05.2026: kein eigenes Filial-Dropdown mehr — die
    // Filiale kommt IMMER vom globalen Sidebar-Selector. Wir setzen das
    // Hidden-Feld auf fixedCompanyProfileId und zeigen sie als Info-Banner.
    const hidden  = document.getElementById('bankImportBranch');
    const banner  = document.getElementById('bankImportBranchBanner');
    const cid     = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    const branches = (typeof allBranches !== 'undefined' && Array.isArray(allBranches)) ? allBranches : [];
    const branch  = cid ? branches.find(b => b.id === Number(cid)) : null;

    if (hidden) hidden.value = branch ? String(branch.id) : '';
    if (banner) {
        if (branch) {
            banner.style.background = '#eff6ff';
            banner.style.border     = '1px solid #bfdbfe';
            banner.style.color      = '#1e40af';
            banner.innerHTML = `<b>Filiale:</b> ${branch.restaurantCode || '?'} — ${branch.branchName || branch.companyName || ''}
                                <span style="color:#64748b;font-size:11.5px;margin-left:6px">(über Sidebar oben links wechseln)</span>`;
        } else {
            banner.style.background = '#fffbeb';
            banner.style.border     = '1px solid #fde68a';
            banner.style.color      = '#92400e';
            banner.innerHTML = `⚠️ Keine Filiale gewählt — bitte oben links in der Sidebar eine Filiale wählen.`;
        }
    }

    // Reset
    document.getElementById('bankImportSummary').innerHTML = '';
    document.getElementById('bankImportPreview').innerHTML = '';
    document.getElementById('bankImportAlert').innerHTML = '';
    document.getElementById('bankImportCommitBtn').disabled = true;
    _bankImportRows = [];
}

async function bankImportPreview() {
    const branchId = document.getElementById('bankImportBranch').value;
    const fileInput = document.getElementById('bankImportFileInput');
    const alertBox = document.getElementById('bankImportAlert');
    const commitBtn = document.getElementById('bankImportCommitBtn');

    if (!branchId) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Bitte zuerst eine Filiale wählen.</div>`;
        return;
    }
    if (!fileInput.files.length) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Bitte eine Mirus-XLS-Datei wählen.</div>`;
        return;
    }

    commitBtn.disabled = true;
    alertBox.innerHTML = `
        <div style="padding:14px 18px;background:#fef3c7;border:1px solid #fde68a;border-radius:9px;display:flex;gap:14px;align-items:center">
            <div class="import-spinner" style="border-color:#fde68a;border-top-color:#a16207;width:24px;height:24px"></div>
            <div style="font-weight:600;color:#78350f;font-size:14px">XLS wird analysiert…</div>
        </div>`;

    const fd = new FormData();
    fd.append('file', fileInput.files[0]);
    fd.append('companyProfileId', branchId);

    try {
        const r = await fetch('/api/imports/bank/preview', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd
        });
        if (!r.ok) {
            let errMsg = 'HTTP ' + r.status;
            try { const j = await r.json(); errMsg = j.message || errMsg; }
            catch { try { errMsg = await r.text(); } catch {} }
            throw new Error(errMsg);
        }
        const data = await r.json();
        // MA-Liste der gewählten Filiale für manuelle Zuweisung bei NO_EMPLOYEE.
        // Sortiert nach Vorname (Walter-Konvention).
        try {
            const empsRes = await fetch('/api/employees', { headers: { 'Authorization': `Bearer ${authToken}` }});
            if (empsRes.ok) {
                const all = await empsRes.json();
                const cpid = parseInt(branchId, 10);
                _bankImportEmps = all
                    .filter(e => e.isActive && !e.isPayrollExcluded
                              && (e.employments || []).some(em => em.companyProfileId === cpid))
                    .sort((a,b) => (a.firstName||'').localeCompare(b.firstName||'')
                                || (a.lastName||'').localeCompare(b.lastName||''));
            } else {
                _bankImportEmps = [];
            }
        } catch { _bankImportEmps = []; }

        alertBox.innerHTML = '';
        bankImportRenderPreview(data);
    } catch (err) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Fehler: ${err.message}</div>`;
        document.getElementById('bankImportSummary').innerHTML = '';
        document.getElementById('bankImportPreview').innerHTML = '';
    }
}

function bankImportRenderPreview(data) {
    _bankImportRows = data.rows || [];

    const summary = document.getElementById('bankImportSummary');
    const preview = document.getElementById('bankImportPreview');
    const commitBtn = document.getElementById('bankImportCommitBtn');

    const counts = {
        MATCH:           _bankImportRows.filter(r => r.status === 'MATCH').length,
        DUPLICATE:       _bankImportRows.filter(r => r.status === 'DUPLICATE').length,
        NO_EMPLOYEE:     _bankImportRows.filter(r => r.status === 'NO_EMPLOYEE').length,
        UNKNOWN_BANK:    _bankImportRows.filter(r => r.status === 'UNKNOWN_BANK').length,
        LOHNABTRETUNG:   _bankImportRows.filter(r => r.status === 'LOHNABTRETUNG').length,
        INVALID_IBAN:    _bankImportRows.filter(r => r.status === 'INVALID_IBAN').length,
    };

    summary.innerHTML = `
    <div style="display:grid;grid-template-columns:repeat(auto-fill, minmax(170px, 1fr));gap:10px">
        ${tileCard('Gefunden in XLS', data.totalEntries, '#0f172a')}
        ${tileCard('Importierbar', counts.MATCH, '#15803d')}
        ${tileCard('Bereits hinterlegt', counts.DUPLICATE, '#a16207')}
        ${tileCard('MA fehlt', counts.NO_EMPLOYEE, '#b91c1c')}
        ${tileCard('Bank unbekannt', counts.UNKNOWN_BANK, '#a16207')}
        ${tileCard('Lohnabtretung', counts.LOHNABTRETUNG, '#7c3aed')}
        ${tileCard('IBAN ungültig', counts.INVALID_IBAN, '#b91c1c')}
    </div>
    <div style="margin-top:8px;font-size:12.5px;color:#475569">
        Filiale: <b>${data.companyProfileName}</b>
    </div>`;

    const html = `
    <div class="card" style="padding:0;overflow:auto;max-height:62vh;margin-top:12px">
        <table style="width:100%;border-collapse:collapse;font-size:12.5px">
            <thead style="position:sticky;top:0;background:#f8fafc;z-index:1">
                <tr>
                    <th style="padding:8px 10px;text-align:left"><input type="checkbox" id="bankImportSelAll" onclick="bankImportToggleAll(this.checked)"></th>
                    <th style="padding:8px 10px;text-align:left">Status</th>
                    <th style="padding:8px 10px;text-align:left">Name (Mirus)</th>
                    <th style="padding:8px 10px;text-align:left">MA-Nr.</th>
                    <th style="padding:8px 10px;text-align:left">IBAN</th>
                    <th style="padding:8px 10px;text-align:left">Bank</th>
                    <th style="padding:8px 10px;text-align:left">Hinweis</th>
                </tr>
            </thead>
            <tbody>
                ${_bankImportRows.map((r, idx) => `
                <tr id="bankImportRow-${idx}" style="border-top:1px solid #f1f5f9;background:${bankImportStatusBg(r.status)}">
                    <td style="padding:5px 10px">
                        <input type="checkbox" class="bankImportSel" data-idx="${idx}"
                               ${r.status === 'MATCH' ? 'checked' : ''}
                               ${r.status === 'MATCH' ? '' : 'style="display:none"'}>
                    </td>
                    <td style="padding:5px 10px" id="bankImportStatusCell-${idx}">${bankImportStatusBadge(r.status)}</td>
                    <td style="padding:5px 10px">${r.name || '–'}</td>
                    <td style="padding:5px 10px;font-family:monospace;font-size:11.5px" id="bankImportEmpCell-${idx}">
                        ${r.status === 'NO_EMPLOYEE'
                          ? bankImportEmpDropdown(idx)
                          : (r.employeeNumber || '–')}
                    </td>
                    <td style="padding:5px 10px;font-family:monospace;font-size:11.5px">${bankImportFormatIban(r.iban)}</td>
                    <td style="padding:5px 10px">${r.bankName || '–'}${r.bic ? `<div style="font-size:10.5px;color:#94a3b8">${r.bic}</div>` : ''}</td>
                    <td style="padding:5px 10px;font-size:11.5px;color:#64748b" id="bankImportHintCell-${idx}">${r.hint || ''}</td>
                </tr>`).join('')}
            </tbody>
        </table>
    </div>`;
    preview.innerHTML = html;

    commitBtn.disabled = counts.MATCH === 0;
    commitBtn.textContent = counts.MATCH > 0
        ? `Import bestätigen (${counts.MATCH})`
        : 'Import bestätigen';
}

function bankImportToggleAll(checked) {
    document.querySelectorAll('.bankImportSel').forEach(cb => cb.checked = checked);
}

// Dropdown für manuelle MA-Zuweisung bei NO_EMPLOYEE-Zeilen.
// Listet alle aktiven MA der gewählten Filiale, sortiert nach Vorname.
function bankImportEmpDropdown(idx) {
    const opts = _bankImportEmps.map(e =>
        `<option value="${e.id}">${e.firstName || ''} ${e.lastName || ''} · ${e.employeeNumber || '?'}</option>`
    ).join('');
    return `<select onchange="bankImportAssignEmp(${idx}, this.value)"
                    style="font-size:12px;padding:3px 6px;border:1px solid #cbd5e1;border-radius:5px;background:white;max-width:240px">
        <option value="">— MA wählen —</option>
        ${opts}
    </select>`;
}

// Wird aufgerufen wenn der User im Dropdown einen MA wählt. Setzt Status
// auf MATCH (damit Checkbox erscheint), schreibt employeeId/Number ins Row-
// Objekt, und re-rendert die betroffene Zeile.
function bankImportAssignEmp(idx, empIdStr) {
    const r = _bankImportRows[idx];
    if (!r) return;
    if (!empIdStr) {
        // Auswahl zurückgesetzt → wieder NO_EMPLOYEE
        r.status         = 'NO_EMPLOYEE';
        r.employeeId     = null;
        r.employeeNumber = null;
    } else {
        const emp = _bankImportEmps.find(e => String(e.id) === String(empIdStr));
        if (!emp) return;
        r.status         = 'MATCH';
        r.employeeId     = emp.id;
        r.employeeNumber = emp.employeeNumber || null;
        r.hint           = `Manuell zugewiesen: ${emp.firstName} ${emp.lastName}`;
    }
    // Zeile aktualisieren ohne komplettes Re-Render
    const tr      = document.getElementById(`bankImportRow-${idx}`);
    const cb      = tr?.querySelector('.bankImportSel');
    const stCell  = document.getElementById(`bankImportStatusCell-${idx}`);
    const hintCell= document.getElementById(`bankImportHintCell-${idx}`);
    if (tr)      tr.style.background = bankImportStatusBg(r.status);
    if (cb) {
        cb.checked = r.status === 'MATCH';
        cb.style.display = r.status === 'MATCH' ? '' : 'none';
    }
    if (stCell)  stCell.innerHTML = bankImportStatusBadge(r.status);
    if (hintCell) hintCell.innerHTML = r.hint || '';

    // Commit-Button neu auswerten
    const matched = _bankImportRows.filter(x => x.status === 'MATCH').length;
    const cBtn = document.getElementById('bankImportCommitBtn');
    if (cBtn) {
        cBtn.disabled = matched === 0;
        cBtn.textContent = matched > 0 ? `Import bestätigen (${matched})` : 'Import bestätigen';
    }
}

function bankImportStatusBg(s) {
    if (s === 'MATCH')          return '#f0fdf4';
    if (s === 'DUPLICATE')      return '#fffbeb';
    if (s === 'NO_EMPLOYEE')    return '#fef2f2';
    if (s === 'UNKNOWN_BANK')   return '#fffbeb';
    if (s === 'LOHNABTRETUNG')  return '#f5f3ff';
    if (s === 'INVALID_IBAN')   return '#fef2f2';
    return 'white';
}
function bankImportStatusBadge(s) {
    const map = {
        'MATCH':         ['Importieren', '#dcfce7', '#15803d'],
        'DUPLICATE':     ['Duplikat',    '#fef3c7', '#a16207'],
        'NO_EMPLOYEE':   ['MA fehlt',    '#fee2e2', '#b91c1c'],
        'UNKNOWN_BANK':  ['Bank ?',      '#fef3c7', '#a16207'],
        'LOHNABTRETUNG': ['Lohnabtretung','#ede9fe','#6d28d9'],
        'INVALID_IBAN':  ['IBAN ungültig','#fee2e2','#b91c1c'],
    };
    const v = map[s]; if (!v) return s;
    return `<span style="display:inline-block;background:${v[1]};color:${v[2]};padding:1px 8px;border-radius:8px;font-size:10.5px;font-weight:600">${v[0]}</span>`;
}
function bankImportFormatIban(iban) {
    if (!iban) return '–';
    const clean = iban.replace(/\s+/g, '');
    return clean.replace(/(.{4})/g, '$1 ').trim();
}

async function bankImportCommit() {
    const branchId = parseInt(document.getElementById('bankImportBranch').value, 10);
    if (!branchId) return;

    const selectedIdxs = Array.from(document.querySelectorAll('.bankImportSel'))
        .filter(cb => cb.checked)
        .map(cb => parseInt(cb.dataset.idx, 10));

    const rowsToCommit = selectedIdxs
        .map(i => _bankImportRows[i])
        .filter(r => r && r.status === 'MATCH' && r.employeeId);

    if (rowsToCommit.length === 0) {
        alert('Keine Zeilen ausgewählt zum Importieren.');
        return;
    }

    if (!confirm(`Sollen ${rowsToCommit.length} Bankverbindung(en) importiert werden?`)) return;

    const commitBtn = document.getElementById('bankImportCommitBtn');
    commitBtn.disabled = true;
    const alertBox = document.getElementById('bankImportAlert');
    alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef3c7;color:#78350f;border-radius:7px;font-size:13px">Importiere ${rowsToCommit.length} Bankverbindung(en)…</div>`;

    try {
        const body = {
            companyProfileId: branchId,
            rows: rowsToCommit.map(r => ({
                employeeId: r.employeeId,
                iban:       r.iban,
                bankName:   r.bankName,
                bic:        r.bic,
                validFrom:  null
            }))
        };
        const r = await fetch('/api/imports/bank/commit', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}`, 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!r.ok) {
            let errMsg = 'HTTP ' + r.status;
            try { const j = await r.json(); errMsg = j.message || errMsg; }
            catch { try { errMsg = await r.text(); } catch {} }
            throw new Error(errMsg);
        }
        const result = await r.json();
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#dcfce7;color:#15803d;border-radius:7px;font-size:13px">
            ✓ ${result.created} Bankverbindung(en) gespeichert${result.skipped ? `, ${result.skipped} übersprungen` : ''}. Fenster wird in 2 Sekunden geschlossen…
        </div>`;
        commitBtn.disabled = true;
        commitBtn.textContent = 'Import bestätigen';
        // Walter-Vorgabe 13.05.2026: nach erfolgreichem Import zurück zur Übersicht.
        setTimeout(() => { if (typeof showPage === 'function') showPage('admin-hub'); }, 2000);
    } catch (err) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Fehler: ${err.message}</div>`;
        commitBtn.disabled = false;
    }
}


