// ══════════════════════════════════════════════════════════════════════
// permit-import.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════
// BEWILLIGUNGSLISTEN-IMPORT (Mirus XLSX)
// ──────────────────────────────────────────────────────────────────────
// XLSX hochladen → Vorschau → Bestätigen.
// Backend: /api/imports/permit/preview + /commit
// ══════════════════════════════════════════════════════════════════════
let _permitImportFile = null;

function permitImportInit() {
    document.getElementById('permitImportAlert').innerHTML = '';
    document.getElementById('permitImportSummary').innerHTML = '';
    document.getElementById('permitImportPreview').innerHTML = '';
    document.getElementById('permitImportCommitBtn').disabled = true;
    const inp = document.getElementById('permitImportFileInput');
    if (inp) inp.value = '';
    _permitImportFile = null;
}

async function permitImportPreview() {
    const inp = document.getElementById('permitImportFileInput');
    const alertEl = document.getElementById('permitImportAlert');
    alertEl.innerHTML = '';
    if (!inp.files || inp.files.length === 0) {
        showPageAlert('permitImportAlert', 'Bitte eine XLSX-Datei wählen.', 'error');
        return;
    }
    const validFrom = document.getElementById('permitImportValidFrom').value;
    if (!validFrom) {
        showPageAlert('permitImportAlert', 'Bitte Beginn-Datum für die Bewilligungs-Verlaufseinträge angeben.', 'error');
        return;
    }
    _permitImportFile = inp.files[0];

    const fd = new FormData();
    fd.append('file', _permitImportFile);
    fd.append('validFrom', validFrom);

    try {
        const r = await fetch('/api/imports/permit/preview', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken },
            body: fd
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            showPageAlert('permitImportAlert', 'Fehler: ' + (j.error || r.status), 'error');
            return;
        }
        const data = await r.json();
        renderPermitImportPreview(data);
        // Commit freischalten wenn entweder neue MA importierbar sind ODER
        // MA mit bestehender Bewilligung existieren (über die Walter via Modus
        // entscheidet).
        const importable = (data.matched || 0) + (data.existingDiff || 0);
        document.getElementById('permitImportCommitBtn').disabled = importable === 0;
    } catch (e) {
        showPageAlert('permitImportAlert', 'Netzwerkfehler: ' + e.message, 'error');
    }
}

function renderPermitImportPreview(data) {
    const summary = document.getElementById('permitImportSummary');
    const preview = document.getElementById('permitImportPreview');
    // Walter-Vorgabe 07.06.2026: zwei neue Status für „bestehende Bewilligung".
    summary.innerHTML = `
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px">
            <div style="background:#dbeafe;border:1px solid #93c5fd;border-radius:8px;padding:12px 14px;color:#1e40af">
                <div style="font-size:24px;font-weight:700">${data.totalRows}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Zeilen total</div>
            </div>
            <div style="background:#dcfce7;border:1px solid #86efac;border-radius:8px;padding:12px 14px;color:#166534">
                <div style="font-size:24px;font-weight:700">${data.matched}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Neu importierbar</div>
            </div>
            <div style="background:#f1f5f9;border:1px solid #cbd5e1;border-radius:8px;padding:12px 14px;color:#475569">
                <div style="font-size:24px;font-weight:700">${data.existingSame || 0}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Identisch — übersprungen</div>
            </div>
            <div style="background:#fef9c3;border:1px solid #fde047;border-radius:8px;padding:12px 14px;color:#854d0e">
                <div style="font-size:24px;font-weight:700">${data.existingDiff || 0}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Bereits Bewilligung — Modus</div>
            </div>
            <div style="background:#fee2e2;border:1px solid #fca5a5;border-radius:8px;padding:12px 14px;color:#991b1b">
                <div style="font-size:24px;font-weight:700">${data.noMatch}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Keine MA-Match</div>
            </div>
            <div style="background:#fef3c7;border:1px solid #fbbf24;border-radius:8px;padding:12px 14px;color:#92400e">
                <div style="font-size:24px;font-weight:700">${data.unknown}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Unklar / fehlend</div>
            </div>
        </div>
        ${(data.existingDiff || 0) > 0 ? `
        <div style="margin-top:14px;padding:12px 14px;background:#fef9c3;border:1px solid #fde047;border-radius:8px">
            <div style="font-weight:700;color:#713f12;margin-bottom:6px">Wie sollen Bewilligungen für MA umgegangen werden, die <strong>bereits</strong> eine Bewilligung haben?</div>
            <div style="display:flex;flex-direction:column;gap:6px;color:#422006;font-size:13px">
                <label style="display:flex;align-items:flex-start;gap:8px;cursor:pointer">
                    <input type="radio" name="permitImportMode" value="STRICT" checked style="margin-top:3px">
                    <span><strong>Überspringen</strong> — bestehende Bewilligungen bleiben unverändert (nur neue MA werden importiert).</span>
                </label>
                <label style="display:flex;align-items:flex-start;gap:8px;cursor:pointer">
                    <input type="radio" name="permitImportMode" value="APPEND" style="margin-top:3px">
                    <span><strong>Beenden + neu anlegen</strong> — bestehender Eintrag wird auf Beginn-Datum −1 geschlossen, neuer Eintrag dahinter angelegt. Verlauf bleibt erhalten.</span>
                </label>
                <label style="display:flex;align-items:flex-start;gap:8px;cursor:pointer">
                    <input type="radio" name="permitImportMode" value="REPLACE" style="margin-top:3px">
                    <span><strong>Ersetzen</strong> — gesamte Bewilligungs-History des MA wird gelöscht und durch den XLSX-Wert ersetzt. Verlauf geht verloren.</span>
                </label>
            </div>
        </div>` : ''}`;

    const statusBadge = (s) => {
        const map = {
            OK:             { bg:'#dcfce7', fg:'#166534', label:'NEU' },
            EXISTING_SAME:  { bg:'#f1f5f9', fg:'#475569', label:'identisch' },
            EXISTING_DIFF:  { bg:'#fef9c3', fg:'#854d0e', label:'bestehend' },
            NO_MATCH:       { bg:'#fee2e2', fg:'#991b1b', label:'MA fehlt' },
            UNKNOWN_PERMIT: { bg:'#fef3c7', fg:'#92400e', label:'Bew. unklar' },
            NO_DATE:        { bg:'#fef3c7', fg:'#92400e', label:'Datum fehlt' }
        };
        const m = map[s] || map.OK;
        return `<span style="background:${m.bg};color:${m.fg};padding:2px 7px;border-radius:7px;font-size:11px;font-weight:600">${m.label}</span>`;
    };

    const fmtDate = d => d ? new Date(d).toLocaleDateString('de-CH', {day:'2-digit',month:'2-digit',year:'numeric'}) : '–';
    const change  = (oldV, newV) => {
        if (!oldV && !newV) return '';
        if (oldV === newV) return ` <span style="color:#94a3b8">unverändert</span>`;
        if (!oldV) return ` <span style="color:#15803d">neu</span>`;
        return ` <span style="color:#92400e">→ ${newV}</span>`;
    };

    preview.innerHTML = `
        <div class="card" style="padding:0;overflow:auto">
            <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                <thead>
                    <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0">
                        <th style="padding:9px 10px;text-align:left">PNr</th>
                        <th style="padding:9px 10px;text-align:left">Name (XLSX)</th>
                        <th style="padding:9px 10px;text-align:left">Name (DB)</th>
                        <th style="padding:9px 10px;text-align:left">Bewilligung XLSX</th>
                        <th style="padding:9px 10px;text-align:left">Bewilligung aktuell</th>
                        <th style="padding:9px 10px;text-align:left">Ablauf XLSX</th>
                        <th style="padding:9px 10px;text-align:left">Ablauf aktuell</th>
                        <th style="padding:9px 10px;text-align:left">Status</th>
                        <th style="padding:9px 10px;text-align:left">Hinweis</th>
                    </tr>
                </thead>
                <tbody>
                    ${data.rows.map(r => `
                        <tr style="border-bottom:1px solid #f1f5f9">
                            <td style="padding:7px 10px;font-family:monospace;font-size:11.5px">${_e(r.employeeNumber)}</td>
                            <td style="padding:7px 10px">${_e(r.csvLastName)} ${_e(r.csvFirstName)}</td>
                            <td style="padding:7px 10px;color:#64748b">${r.dbFirstName ? `${_e(r.dbFirstName)} ${_e(r.dbLastName)}` : '–'}</td>
                            <td style="padding:7px 10px"><b>${_e(r.permitCode || '–')}</b> <span style="color:#94a3b8;font-size:11px">(${_e(r.permitText)})</span></td>
                            <td style="padding:7px 10px;color:#64748b">${_e(r.currentPermitCode || '–')}</td>
                            <td style="padding:7px 10px"><b>${fmtDate(r.permitExpiry)}</b></td>
                            <td style="padding:7px 10px;color:#64748b">${fmtDate(r.currentPermitExpiry)}</td>
                            <td style="padding:7px 10px">${statusBadge(r.status)}</td>
                            <td style="padding:7px 10px;color:#64748b;font-size:11.5px">${_e(r.note || '')}</td>
                        </tr>
                    `).join('')}
                </tbody>
            </table>
        </div>`;
}

async function permitImportCommit() {
    if (!_permitImportFile) {
        showPageAlert('permitImportAlert', 'Erst Datei analysieren.', 'error');
        return;
    }
    const validFrom = document.getElementById('permitImportValidFrom').value;
    if (!validFrom) {
        showPageAlert('permitImportAlert', 'Bitte Beginn-Datum angeben.', 'error');
        return;
    }
    // Walter-Vorgabe 07.06.2026: Modus für MA mit bestehender Bewilligung.
    const modeEl = document.querySelector('input[name="permitImportMode"]:checked');
    const existingMode = modeEl ? modeEl.value : 'STRICT';
    const modeLabel = {
        STRICT:  'Bestehende Bewilligungen werden ÜBERSPRUNGEN.',
        APPEND:  'Bestehende Bewilligungen werden BEENDET (Bis = Beginn-Datum −1), neuer Eintrag dahinter.',
        REPLACE: 'Bestehende Bewilligungs-History wird komplett GELÖSCHT und durch den XLSX-Wert ersetzt.'
    }[existingMode];
    if (!confirm(`Bewilligungen jetzt importieren?\n\n${modeLabel}\n\nMA ohne Bewilligung werden neu angelegt.`)) return;

    const btn = document.getElementById('permitImportCommitBtn');
    btn.disabled = true; btn.textContent = 'Importiere...';

    const fd = new FormData();
    fd.append('file', _permitImportFile);
    fd.append('validFrom', validFrom);
    fd.append('existingMode', existingMode);

    try {
        const r = await fetch('/api/imports/permit/commit', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken },
            body: fd
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) {
            showPageAlert('permitImportAlert', 'Fehler: ' + (j.error || r.status), 'error');
            return;
        }
        // Walter-Vorgabe 07.06.2026: detailliertere Bilanz, damit klar ist
        // was passiert ist (REPLACE/APPEND/STRICT zählen separat).
        const teile = [
            `${j.updated} MA importiert`,
            j.skipped ? `${j.skipped} übersprungen` : null,
            j.replacedExisting ? `${j.replacedExisting} ersetzt` : null,
            j.appendedExisting ? `${j.appendedExisting} verlängert` : null,
            `${j.historyAdded} neue Verlaufseinträge`
        ].filter(Boolean).join(' · ');
        showPageAlert('permitImportAlert',
            `✓ Import erfolgreich (${existingMode}). ${teile}. Fenster wird in 2 Sekunden geschlossen…`,
            'success');
        // Walter-Vorgabe 13.05.2026: nach erfolgreichem Import zurück zur
        // Übersicht — User soll nicht manuell wegnavigieren müssen.
        setTimeout(() => {
            if (typeof showPage === 'function') showPage('admin-hub');
        }, 2000);
    } catch (e) {
        showPageAlert('permitImportAlert', 'Netzwerkfehler: ' + e.message, 'error');
    } finally {
        btn.disabled = false; btn.textContent = 'Import bestätigen';
    }
}


