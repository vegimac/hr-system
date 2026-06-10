// ══════════════════════════════════════════════════════════════════════
// import-stempelzeiten.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════
// IMPORT PAGE
// ══════════════════════════════════════════════
let importSelectedFile = null;

function handleImportFile(file) {
    if (!file) return;
    importSelectedFile = file;
    document.getElementById('importFileInfo').style.display = 'flex';
    document.getElementById('importFileName').textContent = file.name;
    document.getElementById('importBtnRow').style.display = 'flex';
    document.getElementById('stzImportResult').style.display = 'none';
    document.getElementById('importDropZone').style.display = 'none';
}

function handleImportDrop(event) {
    event.preventDefault();
    document.getElementById('importDropZone').classList.remove('drag-over');
    const file = event.dataTransfer.files[0];
    if (file && file.name.endsWith('.pdf')) handleImportFile(file);
    else alert('Bitte eine PDF-Datei ablegen.');
}

function clearImportFile() {
    importSelectedFile = null;
    document.getElementById('importFileInfo').style.display = 'none';
    document.getElementById('importBtnRow').style.display = 'none';
    document.getElementById('stzImportResult').style.display = 'none';
    document.getElementById('importDropZone').style.display = 'block';
    document.getElementById('importFileInput').value = '';
}

async function startStempelzeitenImport() {
    if (!importSelectedFile) return;

    // Vorab Filial-Check via Preview-Endpoint
    if (stzActiveBranch) {
        try {
            const fdPrev = new FormData();
            fdPrev.append('file', importSelectedFile);
            const prevRes = await fetch('/api/import/stempelzeiten/preview', {
                method: 'POST',
                headers: { 'Authorization': `Bearer ${authToken}` },
                body: fdPrev
            });
            if (prevRes.ok) {
                const prev = await prevRes.json();
                const warn = buildStzBranchMismatchWarning(prev);
                if (warn) {
                    if (!confirm('Achtung: Das PDF stammt offenbar aus einer anderen Filiale als der gewählten. Trotzdem importieren?')) {
                        document.getElementById('stzImportResult').style.display = 'block';
                        document.getElementById('stzImportResult').innerHTML = warn;
                        return;
                    }
                }
            }
        } catch {}
    }

    document.getElementById('importBtnRow').style.display = 'none';
    document.getElementById('importProgress').style.display = 'flex';
    document.getElementById('stzImportResult').style.display = 'none';

    const formData = new FormData();
    formData.append('file', importSelectedFile);

    try {
        const res = await fetch('/api/import/stempelzeiten', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: formData
        });

        document.getElementById('importProgress').style.display = 'none';
        const resultEl = document.getElementById('stzImportResult');
        resultEl.style.display = 'block';

        // Erst als Text lesen, dann JSON parsen
        const rawText = await res.text();
        let data;
        try {
            data = JSON.parse(rawText);
        } catch(e) {
            resultEl.innerHTML = `<div class="import-result-err">
                <strong>Server-Antwort konnte nicht gelesen werden (HTTP ${res.status}):</strong><br>
                <code style="font-size:11px;word-break:break-all">${rawText.slice(0, 500) || '(leer)'}</code>
            </div>`;
            document.getElementById('importBtnRow').style.display = 'flex';
            return;
        }

        if (!res.ok) {
            resultEl.innerHTML = `<div class="import-result-err">
                <strong>Fehler (HTTP ${res.status}):</strong> ${data.error ?? JSON.stringify(data)}
            </div>`;
            document.getElementById('importBtnRow').style.display = 'flex';
            return;
        }

        let html = `<div class="import-result-ok">
            <strong>✓ Import abgeschlossen</strong><br>
            <span style="font-size:13px;margin-top:4px;display:block">
                ${data.imported} neue Einträge importiert &nbsp;·&nbsp;
                ${data.skipped} bereits vorhanden (übersprungen)
            </span>
        </div>`;

        if (data.unknownEmployees && data.unknownEmployees.length > 0) {
            html += `<div class="import-result-warn">
                <strong>⚠ Unbekannte Mitarbeitenden-Nummern</strong>
                <div style="font-size:13px;margin-top:6px">
                    Folgende Mitarbeitende wurden nicht gefunden und übersprungen:<br>
                    <span style="font-family:monospace">${data.unknownEmployees.join(', ')}</span>
                </div>
            </div>`;
        }

        resultEl.innerHTML = html;

        // Nur Datei-Auswahl zurücksetzen, Ergebnis sichtbar lassen
        importSelectedFile = null;
        document.getElementById('importFileInfo').style.display = 'none';
        document.getElementById('importBtnRow').style.display = 'none';
        document.getElementById('importDropZone').style.display = 'block';
        document.getElementById('importFileInput').value = '';

        // Walter 07.06.2026: Count-Badge nach erfolgreichem Import aktualisieren
        if (typeof refreshStempelCount === 'function') refreshStempelCount();

    } catch (err) {
        document.getElementById('importProgress').style.display = 'none';
        document.getElementById('stzImportResult').style.display = 'block';
        document.getElementById('stzImportResult').innerHTML =
            `<div class="import-result-err"><strong>Verbindungsfehler:</strong> ${err.message}</div>`;
        document.getElementById('importBtnRow').style.display = 'flex';
    }
}

async function previewStempelzeiten() {
    const resultEl = document.getElementById('stzImportResult');
    if (!importSelectedFile) {
        resultEl.style.display = 'block';
        resultEl.innerHTML = '<div class="import-result-err"><strong>Keine Datei gewählt:</strong> Bitte zuerst ein PDF in die Drop-Zone ziehen oder klicken zum Auswählen.</div>';
        return;
    }
    resultEl.style.display = 'block';
    resultEl.innerHTML = '<div style="color:#3b82f6;font-size:13px">⏳ PDF wird analysiert…</div>';

    const formData = new FormData();
    formData.append('file', importSelectedFile);

    try {
        const res = await fetch('/api/import/stempelzeiten/preview', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: formData
        });
        const data = await res.json();

        // Filial-Match prüfen (anhand Personalnummern-Präfix)
        const branchWarn = buildStzBranchMismatchWarning(data);

        let html = branchWarn + `<div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;padding:14px;font-size:12px">
            <strong style="display:block;margin-bottom:8px">📋 Diagnose-Ergebnis — ${data.totalParsed ?? 0} Einträge erkannt (erste 5 Seiten)</strong>`;

        if (data.parsedSample && data.parsedSample.length > 0) {
            html += `<div style="margin-bottom:10px;color:#16a34a;font-weight:600">✓ Parser hat Einträge gefunden:</div>`;
            data.parsedSample.forEach(e => {
                html += `<div style="font-family:monospace;margin-bottom:3px">Mitarbeiter ${e.emp}: ${e.timeIn} → ${e.timeOut} (${e.duration}h)</div>`;
            });
        } else {
            html += `<div style="color:#dc2626;font-weight:600;margin-bottom:10px">✗ Parser hat KEINE Einträge gefunden</div>`;
        }

        if (data.rawLines && data.rawLines.length > 0) {
            html += `<details style="margin-top:10px"><summary style="cursor:pointer;font-weight:600;color:#475569">Roher PDF-Text (erste 3 Seiten)</summary>
                <pre style="margin-top:8px;white-space:pre-wrap;font-size:11px;color:#334155;max-height:300px;overflow-y:auto">${data.rawLines.join('\n').replace(/</g,'&lt;')}</pre>
            </details>`;
        }

        html += '</div>';
        resultEl.innerHTML = html;
    } catch(err) {
        resultEl.innerHTML = `<div class="import-result-err">Fehler: ${err.message}</div>`;
    }
}

// ══════════════════════════════════════════════
// DUPLIKAT-BEREINIGUNG + STEMPEL-COUNT-BADGE
// (Monats-ZIP-Import wurde am 07.06.2026 entfernt — easy@work liefert
//  EIN PDF mit allen MA via Chrome-Print, der bestehende Stempelzeiten-
//  Importer oben deckt das ab.)
// ══════════════════════════════════════════════

async function stempelDedupe() {
    if (!confirm('Alle Duplikate (gleiche Person + Stempelzeit) entfernen? Der Eintrag mit der niedrigsten ID bleibt erhalten.')) return;
    try {
        const res = await fetch('/api/import/stempelzeiten/dedupe', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` }
        });
        const d = await res.json();
        if (!res.ok) { alert('Fehler: ' + (d.error ?? res.status)); return; }
        alert(`Fertig: ${d.deleted} Duplikate gelöscht.\nVorher: ${d.before}\nNachher: ${d.after}`);
        refreshStempelCount();
    } catch(e) { alert('Verbindungsfehler: ' + e.message); }
}

async function refreshStempelCount() {
    const badge = document.getElementById('importCountBadge');
    if (!badge) return;
    try {
        const res = await fetch('/api/import/stempelzeiten/count', {
            headers: { 'Authorization': `Bearer ${authToken}` }
        });
        if (!res.ok) { badge.textContent = '? in DB'; return; }
        const d = await res.json();
        badge.textContent = `${(d.count ?? 0).toLocaleString('de-CH')} Einträge in DB`;
    } catch { badge.textContent = '? in DB'; }
}


