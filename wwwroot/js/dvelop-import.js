// ══════════════════════════════════════════════════════════════════════
// dvelop-import.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════
// ADMIN: D.VELOP-DOKUMENTEN-IMPORT (CSV+ZIP)
// ══════════════════════════════════════════════════════════════════════

let _dvelopEmployees = [];
let _dvelopEmployeesBranchId = null;  // welche Filiale die aktuell geladene Liste betrifft

// Beim Aufruf der Seite UND bei Filialwechsel: MA für die ausgewählte Filiale laden.
// Filter-Regel ist identisch zur Mitarbeiter-Liste (applyEmpFilter): Vertrag in der
// Filiale, Legacy-MA ohne Filial-Zuordnung, oder Personalnummer-Präfix passt zur Filiale.
async function dvelopLoadEmployees() {
    const branchId = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    // Nur neu fetchen wenn nötig (Branch hat gewechselt oder noch leer).
    if (_dvelopEmployees.length > 0 && _dvelopEmployeesBranchId === branchId) {
        renderDvelopEmployeeOptions();
        return;
    }
    try {
        const r = await fetch('/api/employees', { headers: ah() });
        if (!r.ok) return;
        const all = await r.json();
        _dvelopEmployees = filterEmployeesByBranch(all, branchId);
        // Konvention: ALLE MA-Listen nach Vornamen sortieren, Tie-Break Nachname.
        _dvelopEmployees.sort((a, b) =>
            (a.firstName || '').localeCompare(b.firstName || '')
            || (a.lastName || '').localeCompare(b.lastName || ''));
        _dvelopEmployeesBranchId = branchId;
        renderDvelopEmployeeOptions();
    } catch (err) { console.warn('Mitarbeiter-Liste laden fehlgeschlagen:', err); }
}

// Filtert eine MA-Liste nach der aktuell gewählten Filiale. Logik gleich wie
// applyEmpFilter() in employees.js — wenn keine Filiale gesetzt ist, alle zurückgeben.
function filterEmployeesByBranch(employees, branchId) {
    if (!branchId) return employees;
    const cpid = Number(branchId);
    const branch = (typeof allBranches !== 'undefined' ? allBranches : []).find(b => b.id === cpid);
    const restCode = (branch?.restaurantCode || '').replace(/^0+/, '');
    return employees.filter(e => {
        const emps = e.employments || [];
        // Treffer: mindestens ein Vertrag in dieser Filiale
        if (emps.some(v => Number(v.companyProfileId) === cpid)) return true;
        // Legacy: alle Verträge ohne Filial-Zuordnung → in jeder Filiale anzeigen
        if (emps.length && emps.every(v => !v.companyProfileId)) return true;
        // MA OHNE Verträge: Personalnummer-Präfix muss zur Filiale passen
        if (!emps.length && restCode && (e.employeeNumber || '').replace(/alt$/i, '').startsWith(restCode)) return true;
        return false;
    });
}

// Anzeige-Format „Vorname Nachname — MA-Nr." — Konvention im ganzen System
// (siehe CLAUDE.md). Inaktive MA mit Hinweis am Ende.
function dvelopEmpLabel(e) {
    return `${e.firstName} ${e.lastName} — ${e.employeeNumber}${!e.isActive ? ' (inaktiv)' : ''}`;
}

function renderDvelopEmployeeOptions() {
    const list = document.getElementById('dvelopEmployeeList');
    if (!list) return;
    list.innerHTML = _dvelopEmployees.map(e =>
        `<option value="${dvelopEmpLabel(e)}" data-id="${e.id}"></option>`
    ).join('');
}

function dvelopEmpInputChanged(val) {
    const status = document.getElementById('dvelopEmployeeStatus');
    const hiddenId = document.getElementById('dvelopEmployeeId');
    // Suche nach exakter Übereinstimmung mit dem Datalist-Wert
    const matched = _dvelopEmployees.find(e => dvelopEmpLabel(e) === val);
    if (matched) {
        hiddenId.value = matched.id;
        status.innerHTML = `<span style="color:#15803d">✓ Ausgewählt: <b>${matched.firstName} ${matched.lastName}</b> (${matched.employeeNumber})${!matched.isActive ? ' — INAKTIV' : ''}</span>`;
    } else {
        hiddenId.value = '';
        status.innerHTML = val.length > 1 ? `<span style="color:#94a3b8">Bitte aus der Liste wählen…</span>` : '';
    }
}

// ══════════════════════════════════════════════════════════════════════
// AUTO-DETECT: Mandant (Filiale) + MA aus der hochgeladenen d.velop-Datei.
// Walter-Vorgabe 13.05.2026: User soll Filiale + MA nicht mehr manuell wählen —
// die d.velop-Export-Datei enthält diese Info bereits pro Zeile (Mandant,
// Vorname, Nachname, Geburtsdatum, Mitarbeiter Nummer). Wir parsen die erste
// Datenzeile clientseitig (CSV: Semikolon-Split, XLSX: SheetJS) und setzen
// den globalen Filial-Selector + MA-Search-Input entsprechend.
// ══════════════════════════════════════════════════════════════════════
async function dvelopAutoDetect(file) {
    const statusEl = document.getElementById('dvelopAutoDetectStatus');
    if (!file) { if (statusEl) statusEl.innerHTML = ''; return; }
    if (statusEl) statusEl.innerHTML = '<span style="color:#64748b">⏳ Datei wird analysiert…</span>';

    try {
        const rows = await dvelopReadFirstRows(file, 3);   // Header + 2 Datenzeilen
        if (!rows || rows.length < 2) {
            if (statusEl) statusEl.innerHTML = '<span style="color:#b45309">⚠ Datei ist leer oder hat keinen Datenkopf.</span>';
            return;
        }

        // Spalten-Namen aus Header (case-insensitive)
        const header  = rows[0].map(c => String(c || '').toLowerCase().trim());
        const data    = rows[1];
        const colIdx  = (name) => header.findIndex(h => h === name.toLowerCase());

        const idxMandant = colIdx('Mandant');
        const idxVN      = colIdx('Vorname');
        const idxNN      = colIdx('Nachname');
        const idxMaNr    = colIdx('Mitarbeiter Nummer');
        const idxNameGeb = colIdx('Mitarbeiter (Name / Geb.-Datum)');
        const idxGeb     = colIdx('Geburtsdatum');

        const mandant  = idxMandant >= 0 ? String(data[idxMandant] || '').trim() : '';
        const vorname  = idxVN      >= 0 ? String(data[idxVN]      || '').trim() : '';
        const nachname = idxNN      >= 0 ? String(data[idxNN]      || '').trim() : '';
        const maNr     = idxMaNr    >= 0 ? String(data[idxMaNr]    || '').trim() : '';
        let   geb      = idxGeb     >= 0 ? String(data[idxGeb]     || '').trim() : '';
        // Geb.-Datum oft nur in der kombinierten Spalte "Name / YYYY-MM-DD"
        if (!geb && idxNameGeb >= 0) {
            const m = String(data[idxNameGeb] || '').match(/(\d{4}-\d{2}-\d{2})/);
            if (m) geb = m[1];
        }

        // ── 1) Filiale per Mandant-Präfix "104 McDonald's Restaurant Langenthal" ──
        let branchHit = null;
        if (mandant) {
            const m = mandant.match(/^(\d{2,4})\s/);
            const code = m ? m[1] : null;
            if (code && typeof allBranches !== 'undefined') {
                const norm = code.replace(/^0+/, '');
                branchHit = allBranches.find(b =>
                    (b.restaurantCode || '').replace(/^0+/, '') === norm);
            }
        }

        let branchInfo = '';
        if (branchHit) {
            const sel = document.getElementById('branchSelect');
            if (sel && String(sel.value) !== String(branchHit.id)) {
                sel.value = String(branchHit.id);
                if (typeof onBranchChange === 'function') onBranchChange();
            }
            branchInfo = `Filiale <b>${branchHit.restaurantCode} · ${branchHit.branchName || branchHit.companyName}</b>`;
            // MA-Liste für die neu gewählte Filiale frisch holen
            await dvelopLoadEmployees();
        } else if (mandant) {
            branchInfo = `<span style="color:#b45309">Mandant „${mandant}" — keine zugehörige Filiale gefunden</span>`;
        }

        // ── 2) MA-Match: zuerst MA-Nummer, dann Vorname+Nachname (+Geb) ──
        let maHit = null;
        if (maNr) {
            maHit = _dvelopEmployees.find(e =>
                (e.employeeNumber || '').replace(/alt$/i, '') === maNr);
        }
        if (!maHit && vorname && nachname) {
            const vn = vorname.toLowerCase();
            const nn = nachname.toLowerCase();
            const nameMatches = _dvelopEmployees.filter(e =>
                (e.firstName || '').toLowerCase() === vn
                && (e.lastName || '').toLowerCase() === nn);
            if (nameMatches.length === 1) {
                maHit = nameMatches[0];
            } else if (nameMatches.length > 1) {
                // Mehrere gleichnamige → über Geburtsdatum disambiguieren
                maHit = geb
                    ? (nameMatches.find(e => (e.dateOfBirth || '').startsWith(geb)) ?? nameMatches[0])
                    : nameMatches[0];
            }
        }

        let maInfo = '';
        if (maHit) {
            const inp    = document.getElementById('dvelopEmployeeSearch');
            const hidden = document.getElementById('dvelopEmployeeId');
            if (inp)    inp.value    = dvelopEmpLabel(maHit);
            if (hidden) hidden.value = maHit.id;
            if (typeof dvelopEmpInputChanged === 'function') {
                dvelopEmpInputChanged(inp ? inp.value : '');
            }
            maInfo = `MA <b>${maHit.firstName} ${maHit.lastName}</b> (${maHit.employeeNumber})`;
        } else if (vorname || nachname || maNr) {
            const label = `${vorname} ${nachname}`.trim() + (maNr ? ` · MA-Nr ${maNr}` : '');
            maInfo = `<span style="color:#b45309">MA „${label}" nicht in dieser Filiale gefunden — bitte manuell wählen</span>`;
        }

        if (statusEl) {
            if (branchHit && maHit) {
                statusEl.innerHTML = `<span style="color:#15803d">✓ Erkannt: ${branchInfo} · ${maInfo}</span>`;
            } else if (branchInfo || maInfo) {
                statusEl.innerHTML = [branchInfo, maInfo].filter(Boolean).join(' · ');
            } else {
                statusEl.innerHTML = '<span style="color:#94a3b8">Keine Mandant-/MA-Spalten in der Datei gefunden — bitte manuell wählen.</span>';
            }
        }
    } catch (ex) {
        console.warn('Auto-Detect fehlgeschlagen:', ex);
        if (statusEl) statusEl.innerHTML = `<span style="color:#b45309">⚠ Auto-Detect fehlgeschlagen: ${ex.message || ex}</span>`;
    }
}

// Liest die ersten N Zeilen einer CSV oder XLSX-Datei und gibt sie als
// Array<Array<string>> zurück. CSV: semikolon-getrennt (d.velop-Format,
// inkl. BOM). XLSX: über SheetJS (clientseitig via CDN).
async function dvelopReadFirstRows(file, n) {
    const name   = (file.name || '').toLowerCase();
    const isXlsx = name.endsWith('.xlsx') || name.endsWith('.xlsm')
                || file.type === 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

    if (isXlsx) {
        if (typeof XLSX === 'undefined') {
            throw new Error('SheetJS noch nicht geladen — Seite einmal neu laden.');
        }
        const buf = await file.arrayBuffer();
        const wb  = XLSX.read(buf, { type: 'array' });
        const ws  = wb.Sheets[wb.SheetNames[0]];
        const arr = XLSX.utils.sheet_to_json(ws, { header: 1, raw: false, defval: '' });
        return arr.slice(0, n);
    }

    // CSV: nur die ersten ~50KB lesen (genügt für Header + erste Datenzeilen)
    const slice = file.slice(0, 50 * 1024);
    const text  = await slice.text();
    const clean = text.replace(/^﻿/, '');   // BOM weg
    const lines = clean.split(/\r?\n/).slice(0, n);
    return lines.map(l => l.split(';'));
}

async function dvelopImportAnalyze() { await dvelopImportRun(true); }
async function dvelopImportCommit()  { await dvelopImportRun(false); }

// Per-Row MA-Override für d.velop-Import. Walter kann pro Datei in der
// Preview-Tabelle einen anderen Ziel-MA wählen als den global gewählten.
// Format: { "XG00010269": 1379, ... } — wird beim Commit als JSON
// mitgeschickt und vom Backend pro Row angewendet.
let _dvelopRowOverrides = {};
let _dvelopLastResult = null;   // letzter Preview-Response für Re-Render

function dvelopRowOverrideChanged(xgId, val) {
    const matched = _dvelopEmployees.find(e => dvelopEmpLabel(e) === val);
    if (matched) {
        _dvelopRowOverrides[xgId] = matched.id;
        // UI-Hinweis aktualisieren
        const hint = document.getElementById(`dvelopRowHint-${xgId}`);
        if (hint) hint.innerHTML = `<span style="color:#15803d">→ ${matched.firstName} ${matched.lastName}</span>`;
    } else if (!val.trim()) {
        delete _dvelopRowOverrides[xgId];
        const hint = document.getElementById(`dvelopRowHint-${xgId}`);
        if (hint) hint.innerHTML = '';
    }
}

async function dvelopImportRun(dryRun) {
    const csvIn = document.getElementById('dvelopCsvFile');
    const zipIn = document.getElementById('dvelopZipFile');
    const empId = document.getElementById('dvelopEmployeeId').value;
    const alertBox = document.getElementById('dvelopImportAlert');
    const analyzeBtn = document.querySelector('button[onclick="dvelopImportAnalyze()"]');
    const commitBtn  = document.getElementById('dvelopImportCommitBtn');
    if (!empId) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Bitte erst einen Mitarbeiter wählen (oben).</div>`;
        return;
    }
    if (!csvIn.files.length || !zipIn.files.length) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Bitte Metadaten-Datei (CSV oder XLSX) UND ZIP wählen.</div>`;
        return;
    }

    // Buttons sperren während des Laufs
    if (analyzeBtn) analyzeBtn.disabled = true;
    if (commitBtn)  commitBtn.disabled  = true;

    const startTime = Date.now();
    const titleText = dryRun ? 'Analysiere Metadaten + ZIP…' : 'Importiere Dokumente — bitte warten';
    const subText   = dryRun
        ? 'Lese CSV und prüfe Match in ZIP-Datei…'
        : 'Schreibe Dokumente in die DB und auf den Server. Das kann bei vielen Dateien 1-2 Minuten dauern.';
    alertBox.innerHTML = `
        <div style="padding:14px 18px;background:#fef3c7;border:1px solid #fde68a;border-radius:9px;display:flex;gap:14px;align-items:center">
            <div class="import-spinner" style="border-color:#fde68a;border-top-color:#a16207;width:24px;height:24px"></div>
            <div style="flex:1">
                <div style="font-weight:600;color:#78350f;font-size:14px">${titleText}</div>
                <div style="font-size:12px;color:#a16207;margin-top:2px"><span id="dvelopImportTimer">⏳ 0 Sek</span> · ${subText}</div>
            </div>
        </div>`;

    // Live-Timer hochzählen
    const timerEl = document.getElementById('dvelopImportTimer');
    const timerInterval = setInterval(() => {
        const sec = Math.floor((Date.now() - startTime) / 1000);
        if (timerEl) timerEl.textContent = `⏳ ${sec} Sek`;
    }, 500);

    const fd = new FormData();
    fd.append('csvFile', csvIn.files[0]);
    fd.append('zipFile', zipIn.files[0]);
    fd.append('employeeId', empId);
    fd.append('dryRun', dryRun ? 'true' : 'false');
    // Per-Row MA-Overrides aus dem Preview mitschicken — Backend wendet sie an.
    // Beim Erst-Analyze sind sie noch leer, beim Commit enthalten sie evtl. die
    // Korrekturen die Walter in der Vorschau gemacht hat.
    if (Object.keys(_dvelopRowOverrides).length > 0) {
        fd.append('rowOverrides', JSON.stringify(_dvelopRowOverrides));
    }

    try {
        const r = await fetch('/api/documents/import-dvelop', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd
        });
        if (!r.ok) {
            const errTxt = await r.text();
            throw new Error(errTxt || ('HTTP ' + r.status));
        }
        const result = await r.json();
        renderDvelopImportResult(result, dryRun);
        alertBox.innerHTML = '';
        // Walter-Vorgabe 13.05.2026: nach echtem Import (kein Dry-Run) zurück
        // zur Übersicht. Beim Analysieren (dryRun=true) bleibt die Seite offen.
        if (!dryRun) {
            alertBox.innerHTML = `<div style="padding:10px 14px;background:#dcfce7;color:#15803d;border-radius:7px;font-size:13px">✓ Import abgeschlossen — Fenster wird in 2 Sekunden geschlossen…</div>`;
            setTimeout(() => { if (typeof showPage === 'function') showPage('admin-hub'); }, 2000);
        }
    } catch (err) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Fehler: ${err.message}</div>`;
    } finally {
        clearInterval(timerInterval);
        if (analyzeBtn) analyzeBtn.disabled = false;
        // commitBtn wird in renderDvelopImportResult richtig gesetzt
    }
}

function renderDvelopImportResult(r, dryRun) {
    const summary = document.getElementById('dvelopImportSummary');
    const preview = document.getElementById('dvelopImportPreview');
    const commitBtn = document.getElementById('dvelopImportCommitBtn');
    _dvelopLastResult = r;

    const willCreate = r.preview.filter(p => p.action === 'create').length;
    summary.innerHTML = `
    <div style="display:grid;grid-template-columns:repeat(auto-fill, minmax(170px, 1fr));gap:10px">
        ${tileCard('Total CSV', r.totalRows, '#0f172a')}
        ${tileCard(dryRun ? 'Würden importiert' : 'Importiert ✓', dryRun ? willCreate : r.imported, '#15803d')}
        ${tileCard('Schon vorhanden', r.skippedDuplicate, '#a16207')}
        ${tileCard('MA nicht gefunden', r.skippedNoEmployee, '#b91c1c')}
        ${tileCard('Filiale fehlt', r.skippedNoBranch, '#b91c1c')}
        ${tileCard('Kategorie fehlt', r.skippedNoCategory, '#b91c1c')}
        ${tileCard('PDF im ZIP fehlt', r.skippedNoFile, '#b91c1c')}
    </div>`;

    // Datalist (einmal — wird von allen Rows genutzt) für die MA-Override-Inputs
    const datalistHtml = `<datalist id="dvelopOverrideEmpList">${
        _dvelopEmployees.map(e => `<option value="${dvelopEmpLabel(e)}"></option>`).join('')
    }</datalist>`;

    const html = `
    ${datalistHtml}
    <div class="card" style="padding:0;overflow:auto;max-height:60vh;margin-top:12px">
        <table style="width:100%;border-collapse:collapse;font-size:12px">
            <thead style="position:sticky;top:0;background:#f8fafc;z-index:1">
                <tr>
                    <th style="padding:8px 10px;text-align:left">#</th>
                    <th style="padding:8px 10px;text-align:left">XG-ID</th>
                    <th style="padding:8px 10px;text-align:left">Datei</th>
                    <th style="padding:8px 10px;text-align:left">Mitarbeiter (Datei) → Ziel-MA</th>
                    <th style="padding:8px 10px;text-align:left">Filiale</th>
                    <th style="padding:8px 10px;text-align:left">Kategorie / Typ</th>
                    <th style="padding:8px 10px;text-align:left">Bemerkung</th>
                    <th style="padding:8px 10px;text-align:left">Aktion</th>
                </tr>
            </thead>
            <tbody>
                ${r.preview.map(p => `
                <tr style="border-top:1px solid #f1f5f9;background:${dvelopActionBg(p.action)}">
                    <td style="padding:5px 10px">${p.rowNum}</td>
                    <td style="padding:5px 10px;font-family:monospace;font-size:11px">${p.xgId}</td>
                    <td style="padding:5px 10px;max-width:240px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap" title="${(p.filename||'').replace(/"/g,'&quot;')}">${p.filename || '–'}</td>
                    <td style="padding:5px 10px">
                        <div style="font-size:11px;color:#64748b">${p.employeeName}${p.dateOfBirth ? ` · geb. ${new Date(p.dateOfBirth).toLocaleDateString('de-CH', {day:'2-digit',month:'2-digit',year:'numeric'})}` : ''}</div>
                        <input type="text" list="dvelopOverrideEmpList"
                               placeholder="Ziel-MA überschreiben…"
                               oninput="dvelopRowOverrideChanged('${p.xgId}', this.value)"
                               style="width:100%;padding:3px 6px;font-size:11px;border:1px solid #e2e8f0;border-radius:4px;margin-top:3px;background:white"
                               autocomplete="off">
                        <div id="dvelopRowHint-${p.xgId}" style="font-size:10.5px;margin-top:2px"></div>
                    </td>
                    <td style="padding:5px 10px">${p.branchCode ? `<span class="dok-cat-pill">${p.branchCode}</span>` : '–'}</td>
                    <td style="padding:5px 10px">${p.kategorieName || '–'}${p.typName ? `<div style="font-size:10.5px;color:#64748b">${p.typName}</div>` : ''}</td>
                    <td style="padding:5px 10px;font-size:11px;color:#475569;max-width:200px;overflow:hidden;text-overflow:ellipsis">${p.bemerkung || ''}</td>
                    <td style="padding:5px 10px">${dvelopActionBadge(p.action)}${p.reason ? `<div style="font-size:10.5px;color:#64748b">${p.reason}</div>` : ''}</td>
                </tr>`).join('')}
            </tbody>
        </table>
    </div>`;
    preview.innerHTML = html;

    if (dryRun) {
        commitBtn.disabled = willCreate === 0;
        commitBtn.textContent = willCreate > 0 ? `Import bestätigen (${willCreate})` : 'Import bestätigen';
    } else {
        commitBtn.disabled = true;
        commitBtn.textContent = 'Import bestätigen';
    }
}

function dvelopActionBg(action) {
    if (action === 'create')           return '#f0fdf4';
    if (action === 'skip-duplicate')   return '#fffbeb';
    if (action === 'skip-no-employee') return '#fef2f2';
    if (action === 'skip-no-branch')   return '#fef2f2';
    if (action === 'skip-no-category') return '#fef2f2';
    if (action === 'skip-no-file')     return '#fef2f2';
    return 'white';
}
function dvelopActionBadge(action) {
    const map = {
        'create':           ['Importieren', '#dcfce7', '#15803d'],
        'skip-duplicate':   ['Duplikat',    '#fef3c7', '#a16207'],
        'skip-no-employee': ['MA fehlt',    '#fee2e2', '#b91c1c'],
        'skip-no-branch':   ['Filiale fehlt','#fee2e2','#b91c1c'],
        'skip-no-category': ['Kategorie fehlt','#fee2e2','#b91c1c'],
        'skip-no-file':     ['PDF fehlt',   '#fee2e2', '#b91c1c'],
    };
    const v = map[action]; if (!v) return '–';
    return `<span style="display:inline-block;background:${v[1]};color:${v[2]};padding:1px 8px;border-radius:8px;font-size:10.5px;font-weight:600">${v[0]}</span>`;
}

