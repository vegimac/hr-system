// ══════════════════════════════════════════════════════════════════════
// archive-import.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════
// ADMIN: MITARBEITER-ARCHIV-IMPORT (CSV → ausgetretene MAs)
// ══════════════════════════════════════════════════════════════════════

// ── Archiv-MA aufräumen (Walter 05.08.2026): alle «…alt»-MA löschen ────
// Zweistufig: Vorschau (dryRun) → erst danach ist der Lösch-Knopf aktiv.
// Server überspringt MA mit Lohn-Daten oder Dokumenten immer.

async function archivCleanupPreview() {
    const box = document.getElementById('archivCleanupResult');
    const delBtn = document.getElementById('archivCleanupBtn');
    if (delBtn) delBtn.disabled = true;
    if (box) box.innerHTML = '<div style="font-size:13px;color:#64748b">⏳ Prüfe Archiv-MA…</div>';
    try {
        const r = await fetch('/api/employees/cleanup-archiv?dryRun=true', {
            method: 'POST', headers: ah()
        });
        if (!r.ok) {
            const t = await r.text();
            if (box) box.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Fehler: ${t || r.status}</div>`;
            return;
        }
        const d = await r.json();
        const esc = s => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;');
        let html = `<div style="font-size:13px;color:#0f172a"><b>${d.total}</b> inaktive MA geprüft · <b style="color:#15803d">${d.loeschbar.length} löschbar (Austritt vor 1.1.2025)</b>`;
        if ((d.uebersprungenVerknuepft || []).length) html += ` · <b style="color:#1d4ed8">${d.uebersprungenVerknuepft.length} bleiben (Mirus-Ära/kein Datum)</b>`;
        if (d.uebersprungenLohn.length) html += ` · <b style="color:#b45309">${d.uebersprungenLohn.length} mit Lohn-Daten (bleiben)</b>`;
        if (d.uebersprungenDokumente.length) html += ` · <b style="color:#b91c1c">${d.uebersprungenDokumente.length} mit Dokumenten (bleiben)</b>`;
        html += '</div>';
        if (d.loeschbar.length)
            html += `<details style="margin-top:8px;font-size:12px;color:#64748b"><summary style="cursor:pointer">Löschbare anzeigen (${d.loeschbar.length})</summary><div style="margin-top:6px;max-height:220px;overflow:auto">${d.loeschbar.map(esc).join('<br>')}</div></details>`;
        if ((d.uebersprungenVerknuepft || []).length)
            html += `<details style="margin-top:8px;font-size:12px;color:#1d4ed8"><summary style="cursor:pointer">Bleiben stehen — mit Grund (${d.uebersprungenVerknuepft.length})</summary><div style="margin-top:6px;max-height:220px;overflow:auto">${d.uebersprungenVerknuepft.map(x => esc(x.label) + ' — ' + esc(x.grund)).join('<br>')}</div></details>`;
        if (d.uebersprungenDokumente.length)
            html += `<details style="margin-top:8px;font-size:12px;color:#b91c1c" open><summary style="cursor:pointer">Mit Dokumenten — bitte prüfen</summary><div style="margin-top:6px;max-height:220px;overflow:auto">${d.uebersprungenDokumente.map(x => esc(x.label) + ' (' + x.dokCount + ' Dok.)').join('<br>')}</div></details>`;
        if (box) box.innerHTML = html;
        if (delBtn) delBtn.disabled = d.loeschbar.length === 0;
    } catch (e) {
        if (box) box.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Verbindungsfehler: ${e.message}</div>`;
    }
}

async function archivCleanupRun() {
    const box = document.getElementById('archivCleanupResult');
    const delBtn = document.getElementById('archivCleanupBtn');
    const ok = await (typeof liquidConfirm === 'function'
        ? liquidConfirm('Alle löschbaren Prä-Mirus-Karteileichen (Austritt vor 1.1.2025) endgültig löschen? Das kann nicht rückgängig gemacht werden.',
            { title: 'Karteileichen aufräumen', yesLabel: 'Ja, löschen', noLabel: 'Abbrechen' })
        : Promise.resolve(confirm('Alle löschbaren Prä-Mirus-Karteileichen endgültig löschen?')));
    if (!ok) return;
    if (delBtn) delBtn.disabled = true;
    if (box) box.innerHTML = '<div style="font-size:13px;color:#64748b">⏳ Lösche Archiv-MA… (kann bei vielen MA eine Minute dauern)</div>';
    try {
        const r = await fetch('/api/employees/cleanup-archiv?dryRun=false', {
            method: 'POST', headers: ah()
        });
        const d = await r.json().catch(() => ({}));
        if (!r.ok) {
            if (box) box.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Fehler: ${d.message || d.error || r.status}</div>`;
            return;
        }
        let html = `<div style="padding:10px 14px;background:#ecfdf5;color:#047857;border-radius:7px;font-size:13px">✓ <b>${d.geloescht}</b> Archiv-MA gelöscht.`;
        if (d.uebersprungenLohn.length || d.uebersprungenDokumente.length)
            html += ` Übersprungen: ${d.uebersprungenLohn.length} mit Lohn-Daten, ${d.uebersprungenDokumente.length} mit Dokumenten.`;
        html += '</div>';
        if ((d.fehler || []).length)
            html += `<div style="margin-top:8px;padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:12.5px">${d.fehler.map(f => '✗ ' + f).join('<br>')}</div>`;
        if (box) box.innerHTML = html;
    } catch (e) {
        if (box) box.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Verbindungsfehler: ${e.message}</div>`;
    }
}

async function archivImportAnalyze() { await archivImportRun(true); }
async function archivImportCommit()  { await archivImportRun(false); }

async function archivImportRun(dryRun) {
    const fileInput = document.getElementById('archivCsvFile');
    const alertBox  = document.getElementById('archivImportAlert');
    const analyzeBtn = document.querySelector('button[onclick="archivImportAnalyze()"]');
    const commitBtn  = document.getElementById('archivImportCommitBtn');
    if (!fileInput.files.length) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Bitte zuerst eine CSV-Datei wählen.</div>`;
        return;
    }

    if (analyzeBtn) analyzeBtn.disabled = true;
    if (commitBtn)  commitBtn.disabled  = true;

    const startTime = Date.now();
    const titleText = dryRun ? 'Analysiere CSV…' : 'Importiere Mitarbeiter — bitte warten';
    const subText   = dryRun
        ? 'Lese CSV-Datei und prüfe Match mit existierenden MA…'
        : 'Schreibe Mitarbeiter + Verträge in die DB. Bei vielen Zeilen kann das einen Moment dauern.';
    alertBox.innerHTML = `
        <div style="padding:14px 18px;background:#fef3c7;border:1px solid #fde68a;border-radius:9px;display:flex;gap:14px;align-items:center">
            <div class="import-spinner" style="border-color:#fde68a;border-top-color:#a16207;width:24px;height:24px"></div>
            <div style="flex:1">
                <div style="font-weight:600;color:#78350f;font-size:14px">${titleText}</div>
                <div style="font-size:12px;color:#a16207;margin-top:2px"><span id="archivImportTimer">⏳ 0 Sek</span> · ${subText}</div>
            </div>
        </div>`;
    const timerEl = document.getElementById('archivImportTimer');
    const timerInterval = setInterval(() => {
        const sec = Math.floor((Date.now() - startTime) / 1000);
        if (timerEl) timerEl.textContent = `⏳ ${sec} Sek`;
    }, 500);

    const updateExisting = document.getElementById('archivUpdateExisting')?.checked === true;
    const fullMigration  = document.getElementById('archivFullMigration')?.checked === true;

    const fd = new FormData();
    fd.append('file', fileInput.files[0]);
    fd.append('dryRun', dryRun ? 'true' : 'false');
    fd.append('updateExisting', updateExisting ? 'true' : 'false');
    fd.append('fullMigration', fullMigration ? 'true' : 'false');

    try {
        const r = await fetch('/api/employees/import-archived', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd
        });
        if (!r.ok) {
            const errTxt = await r.text();
            throw new Error(errTxt || ('HTTP ' + r.status));
        }
        const result = await r.json();
        renderArchivImportResult(result, dryRun);
        alertBox.innerHTML = '';
    } catch (err) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Fehler: ${err.message}</div>`;
    } finally {
        clearInterval(timerInterval);
        if (analyzeBtn) analyzeBtn.disabled = false;
        // commitBtn wird in renderArchivImportResult richtig gesetzt
    }
}

function renderArchivImportResult(r, dryRun) {
    const summary = document.getElementById('archivImportSummary');
    const preview = document.getElementById('archivImportPreview');
    const commitBtn = document.getElementById('archivImportCommitBtn');

    const willCreate = r.preview.filter(p => p.action==='create').length;
    const willUpdate = r.preview.filter(p => p.action==='update').length;

    // Tile-Cards
    summary.innerHTML = `
    <div style="display:grid;grid-template-columns:repeat(auto-fill, minmax(170px, 1fr));gap:10px">
        ${tileCard('Gesamt Zeilen', r.totalRows, '#0f172a')}
        ${tileCard('Mit Bis-Datum', r.withExitDate, '#8b8b8b')}
        ${tileCard('Ohne Bis-Datum', r.withoutExitDate, '#94a3b8')}
        ${tileCard(dryRun ? 'Würden neu' : 'Neu angelegt ✓', dryRun ? willCreate : r.imported, '#15803d')}
        ${tileCard(dryRun ? 'Würden aktualisiert' : 'Aktualisiert ✓', dryRun ? willUpdate : (r.updated ?? 0), '#8b8b8b')}
        ${tileCard('Schon vorhanden', r.skippedAlreadyExists, '#a16207')}
        ${tileCard('Filiale fehlt', r.skippedNoBranch, '#b91c1c')}
        ${tileCard('Ungültig', r.skippedInvalid, '#b91c1c')}
    </div>`;

    // Preview-Tabelle (nur Aktionen, keine "skip-no-bis"-Zeilen)
    const rows = r.preview.filter(p => p.action !== 'skip-no-bis');
    const html = `
    <div class="card" style="padding:0;overflow:auto;max-height:60vh;margin-top:12px">
        <table style="width:100%;border-collapse:collapse;font-size:12.5px">
            <thead style="position:sticky;top:0;background:#f8fafc;z-index:1">
                <tr>
                    <th style="padding:8px 10px;text-align:left">#</th>
                    <th style="padding:8px 10px;text-align:left">Name</th>
                    <th style="padding:8px 10px;text-align:left">Geb.-Datum</th>
                    <th style="padding:8px 10px;text-align:left">Nr.</th>
                    <th style="padding:8px 10px;text-align:left">Eintritt → Austritt</th>
                    <th style="padding:8px 10px;text-align:left">Filiale</th>
                    <th style="padding:8px 10px;text-align:left">Funktion → Vertrag</th>
                    <th style="padding:8px 10px;text-align:left">Aktion</th>
                </tr>
            </thead>
            <tbody>
                ${rows.map(p => `
                <tr style="border-top:1px solid #f1f5f9;background:${actionBg(p.action)}">
                    <td style="padding:6px 10px">${p.rowNum}</td>
                    <td style="padding:6px 10px">${p.firstName} ${p.lastName}${p.resolvedActive ? '' : ' <span style="color:#94a3b8;font-size:10px">(inaktiv)</span>'}</td>
                    <td style="padding:6px 10px">${p.dateOfBirth ? new Date(p.dateOfBirth).toLocaleDateString('de-CH', {day:'2-digit',month:'2-digit',year:'numeric'}) : '–'}</td>
                    <td style="padding:6px 10px;font-family:monospace">${p.employeeNumber || '–'}</td>
                    <td style="padding:6px 10px">${p.entryDate ? new Date(p.entryDate).toLocaleDateString('de-CH', {day:'2-digit',month:'2-digit',year:'numeric'}) : '?'} → <b>${p.exitDate ? new Date(p.exitDate).toLocaleDateString('de-CH', {day:'2-digit',month:'2-digit',year:'numeric'}) : '?'}</b></td>
                    <td style="padding:6px 10px">${p.branchCode ? `<span class="dok-cat-pill">${p.branchCode}</span> ${p.branchName ?? ''}` : '–'}</td>
                    <td style="padding:6px 10px">${renderContractCell(p)}</td>
                    <td style="padding:6px 10px">${actionBadge(p.action)}${p.reason ? `<div style="font-size:11px;color:#64748b">${p.reason}</div>` : ''}</td>
                </tr>`).join('')}
            </tbody>
        </table>
    </div>`;
    preview.innerHTML = html;

    // Commit-Button nur aktivieren, wenn Dry-Run > 0 importable/aktualisierbare Zeilen
    if (dryRun) {
        const willDo = willCreate + willUpdate;
        commitBtn.disabled = willDo === 0;
        if (willDo > 0) {
            const parts = [];
            if (willCreate > 0) parts.push(`${willCreate} neu`);
            if (willUpdate > 0) parts.push(`${willUpdate} aktualisieren`);
            commitBtn.textContent = `Import bestätigen (${parts.join(' + ')})`;
        } else {
            commitBtn.textContent = 'Import bestätigen';
        }
    } else {
        commitBtn.disabled = true;
        commitBtn.textContent = 'Import bestätigen';
    }
}

function tileCard(label, val, color) {
    return `<div style="background:white;border:1px solid #e2e8f0;border-radius:10px;padding:12px 14px">
        <div style="font-size:11px;color:#64748b;text-transform:uppercase;letter-spacing:0.04em;font-weight:600">${label}</div>
        <div style="font-size:22px;font-weight:700;color:${color};margin-top:2px">${val}</div>
    </div>`;
}
function actionBg(action) {
    if (action === 'create')          return '#f0fdf4';
    if (action === 'update')          return '#f6f3ee';
    if (action === 'skip-exists')     return '#fffbeb';
    if (action === 'skip-no-branch')  return '#fef2f2';
    if (action === 'skip-invalid')    return '#fef2f2';
    return 'white';
}
function renderContractCell(p) {
    if (!p.employmentModel && !p.csvFunktion) return '<span style="color:#cbd5e1">–</span>';
    const modelClass = ({ MTP:'model-badge-mtp', FLEX:'model-badge-utp', FIX:'model-badge-fix', 'FIX-M':'model-badge-fix-m' })[p.employmentModel] || '';
    let detail = '';
    if (p.employmentPercentage != null) detail = `${p.employmentPercentage}%`;
    else if (p.guaranteedHoursPerWeek != null) detail = `${p.guaranteedHoursPerWeek} Std/W`;
    return `
        <div style="font-size:11px;color:#64748b">${p.csvFunktion ?? '–'}${p.csvContractType ? ' · ' + p.csvContractType : ''}</div>
        <div style="display:flex;gap:6px;align-items:center;margin-top:2px">
            <span class="${modelClass}" style="display:inline-block;padding:1px 8px;border-radius:8px;font-size:10.5px;font-weight:600">${p.employmentModel ?? '–'}</span>
            ${detail ? `<span style="font-size:11px;color:#475569">${detail}</span>` : ''}
            ${p.jobGroupCode ? `<span style="font-size:10px;color:#94a3b8">[${p.jobGroupCode}${p.isKader ? ' · Kader' : ''}]</span>` : ''}
        </div>
    `;
}
function actionBadge(action) {
    if (action === 'create')          return '<span style="display:inline-block;background:#dcfce7;color:#15803d;padding:1px 8px;border-radius:8px;font-size:10.5px;font-weight:600">Anlegen</span>';
    if (action === 'update')          return '<span style="display:inline-block;background:#ece9e2;color:#6b7280;padding:1px 8px;border-radius:8px;font-size:10.5px;font-weight:600">Aktualisieren</span>';
    if (action === 'skip-exists')     return '<span style="display:inline-block;background:#fef3c7;color:#a16207;padding:1px 8px;border-radius:8px;font-size:10.5px;font-weight:600">Bereits da</span>';
    if (action === 'skip-no-branch')  return '<span style="display:inline-block;background:#fee2e2;color:#b91c1c;padding:1px 8px;border-radius:8px;font-size:10.5px;font-weight:600">Filiale fehlt</span>';
    if (action === 'skip-invalid')    return '<span style="display:inline-block;background:#fee2e2;color:#b91c1c;padding:1px 8px;border-radius:8px;font-size:10.5px;font-weight:600">Ungültig</span>';
    return '<span style="color:#94a3b8">–</span>';
}

