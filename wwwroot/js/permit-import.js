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
        // Commit nur freischalten wenn mind. 1 Match-OK ist
        document.getElementById('permitImportCommitBtn').disabled = data.matched === 0;
    } catch (e) {
        showPageAlert('permitImportAlert', 'Netzwerkfehler: ' + e.message, 'error');
    }
}

function renderPermitImportPreview(data) {
    const summary = document.getElementById('permitImportSummary');
    const preview = document.getElementById('permitImportPreview');
    summary.innerHTML = `
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px">
            <div style="background:#dbeafe;border:1px solid #93c5fd;border-radius:8px;padding:12px 14px;color:#1e40af">
                <div style="font-size:24px;font-weight:700">${data.totalRows}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Zeilen total</div>
            </div>
            <div style="background:#dcfce7;border:1px solid #86efac;border-radius:8px;padding:12px 14px;color:#166534">
                <div style="font-size:24px;font-weight:700">${data.matched}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Übernehmbar</div>
            </div>
            <div style="background:#fee2e2;border:1px solid #fca5a5;border-radius:8px;padding:12px 14px;color:#991b1b">
                <div style="font-size:24px;font-weight:700">${data.noMatch}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Keine MA-Match</div>
            </div>
            <div style="background:#fef3c7;border:1px solid #fbbf24;border-radius:8px;padding:12px 14px;color:#92400e">
                <div style="font-size:24px;font-weight:700">${data.unknown}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Unklar / fehlend</div>
            </div>
        </div>`;

    const statusBadge = (s) => {
        const map = {
            OK:             { bg:'#dcfce7', fg:'#166534', label:'OK' },
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
    if (!confirm('Bewilligungen jetzt importieren?\n\nBestehende Bewilligungs-Daten der MA werden mit den XLSX-Werten überschrieben. Verlaufseinträge werden aktualisiert oder neu angelegt.')) return;

    const btn = document.getElementById('permitImportCommitBtn');
    btn.disabled = true; btn.textContent = 'Importiere...';

    const fd = new FormData();
    fd.append('file', _permitImportFile);
    fd.append('validFrom', validFrom);

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
        showPageAlert('permitImportAlert',
            `✓ Import erfolgreich. ${j.updated} MA aktualisiert, ${j.skipped} übersprungen, ${j.historyAdded} neue Verlaufseinträge, ${j.historyUpdated} Verlaufseinträge aktualisiert. Fenster wird in 2 Sekunden geschlossen…`,
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


