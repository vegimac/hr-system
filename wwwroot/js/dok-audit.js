// ══════════════════════════════════════════════════════════════════════
// dok-audit.js — Dokument-Audit (Filial-Mismatch)
// ══════════════════════════════════════════════════════════════════════
// Backend: GET /api/documents/audit/branch-mismatch
// Findet Verdachtsfälle wo der Dateiname eine andere Filiale erwähnt als
// der gespeicherte BranchCode. Walter klickt auf einen MA-Link um das
// Doku zu öffnen und ggf. via Edit-Modal an den richtigen MA zu verschieben.

async function dokAuditRun() {
    const status  = document.getElementById('dokAuditStatus');
    const results = document.getElementById('dokAuditResults');
    const alertBx = document.getElementById('dokAuditAlert');
    if (status)  status.textContent  = 'Wird durchsucht…';
    if (alertBx) alertBx.innerHTML   = '';
    if (results) results.innerHTML   = '';

    try {
        const r = await fetch('/api/documents/audit/branch-mismatch', { headers: ah() });
        if (!r.ok) {
            const txt = await r.text();
            if (alertBx) alertBx.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Fehler: ${txt || ('HTTP ' + r.status)}</div>`;
            if (status) status.textContent = '';
            return;
        }
        const data = await r.json();
        // Stats inline anzeigen damit Walter sieht wieviele Dokus tatsächlich
        // geprüft wurden (gegen welche Branch-Liste, was übersprungen wurde).
        if (status) {
            const skipped = (data.skippedNoBranch || 0) + (data.skippedNoFile || 0);
            status.innerHTML = `
                <span style="color:#15803d;font-weight:600">✓ Fertig</span> —
                ${data.examined ?? 0} von ${data.totalDocs ?? 0} Dokumenten gegen ${data.branchesScanned ?? 0} Filialen geprüft
                ${skipped > 0 ? `· <span style="color:#a16207">${skipped} ohne Branch/Dateiname übersprungen</span>` : ''}
                · <b>${data.total} Verdachtsfälle</b>
            `;
        }
        renderDokAuditResults(data.mismatches || []);
    } catch (e) {
        if (alertBx) alertBx.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Verbindungsfehler: ${e.message}</div>`;
        if (status) status.textContent = '';
    }
}

function renderDokAuditResults(rows) {
    const el = document.getElementById('dokAuditResults');
    if (!el) return;
    if (rows.length === 0) {
        el.innerHTML = `
            <div class="card" style="padding:32px;text-align:center;color:#15803d">
                <div style="font-size:36px">✓</div>
                <div style="font-weight:600;font-size:15px;margin-top:6px">Keine Verdachtsfälle gefunden</div>
                <div style="font-size:12.5px;color:#94a3b8;margin-top:4px">
                    Alle Dateinamen passen zur Filial-Zuordnung des Dokuments.
                </div>
            </div>`;
        return;
    }

    const trs = rows.map(r => {
        const suspectStr = (r.suspectedBranchCodes || []).join(', ');
        return `<tr style="border-top:1px solid #f1f5f9;background:#fffbeb">
            <td style="padding:8px 12px">
                <a href="javascript:void(0)" onclick="dokAuditOpenMa(${r.employeeId})"
                   style="color:#6b7280;text-decoration:none;font-weight:600">
                   ${r.employeeName || '–'}
                </a>
                <div style="font-size:11px;color:#94a3b8">${r.employeeNumber || ''}</div>
            </td>
            <td style="padding:8px 12px">
                <span style="background:#ece9e2;color:#6b6152;padding:2px 8px;border-radius:8px;font-weight:600;font-size:11.5px">
                    ${r.currentBranchCode}
                </span>
                <div style="font-size:11px;color:#64748b;margin-top:2px">${r.currentBranchName || ''}</div>
            </td>
            <td style="padding:8px 12px">
                <span style="background:#fee2e2;color:#b91c1c;padding:2px 8px;border-radius:8px;font-weight:600;font-size:11.5px">
                    ${suspectStr}
                </span>
                <div style="font-size:10.5px;color:#94a3b8;margin-top:2px">aus Dateinamen erkannt</div>
            </td>
            <td style="padding:8px 12px;font-size:12px;color:#475569;max-width:340px;word-break:break-all">
                ${r.filename}
                <div style="font-size:10.5px;color:#94a3b8;margin-top:2px">
                    ${r.kategorie || ''}${r.typ ? ' · ' + r.typ : ''}
                </div>
            </td>
        </tr>`;
    }).join('');

    el.innerHTML = `
    <div style="padding:10px 14px;background:#fef3c7;border:1px solid #fde68a;border-radius:8px;margin-bottom:10px;font-size:12.5px;color:#78350f">
        Achtung: das ist eine Heuristik. Es kann legitime Fälle geben wo der MA wirklich in der Filiale arbeitet
        aber das Dokument einen anderen Filial-Namen erwähnt (z.B. weil der MA dort früher tätig war).
        Falsche Treffer ignorieren — bei echten Fehlern via MA-Link öffnen + im Dokumente-Tab das Doku
        bearbeiten und an den richtigen MA umhängen.
    </div>
    <div class="card" style="padding:0;overflow:auto;max-height:65vh">
        <table style="width:100%;border-collapse:collapse;font-size:12.5px">
            <thead style="position:sticky;top:0;background:#f8fafc;z-index:1">
                <tr>
                    <th style="padding:10px 12px;text-align:left">Mitarbeiter</th>
                    <th style="padding:10px 12px;text-align:left">aktuell zugeordnet</th>
                    <th style="padding:10px 12px;text-align:left">verdächtige Filiale(n)</th>
                    <th style="padding:10px 12px;text-align:left">Datei</th>
                </tr>
            </thead>
            <tbody>${trs}</tbody>
        </table>
    </div>`;
}

function dokAuditOpenMa(employeeId) {
    if (!employeeId) return;
    showPage('mitarbeiter');
    setTimeout(() => {
        if (typeof selectEmployee === 'function') selectEmployee(employeeId);
        // Direkt zum Dokumente-Tab springen damit Walter das Doku findet
        setTimeout(() => {
            if (typeof switchEmpTab === 'function') switchEmpTab('dokumente');
        }, 400);
    }, 250);
}
