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
// Walter-Vorgabe 10.06.2026: nach dem Import beim erneuten Öffnen der
// Page das UI komplett zurücksetzen — sonst sieht Walter immer noch die
// Stats + Tabelle vom letzten Lauf.
function dvelopResetUi() {
    const ids = [
        'dvelopImportAlert',     // grüner Erfolgs-Banner
        'dvelopImportSummary',   // Statistik-Cards
        'dvelopImportPreview',   // Tabelle
        'dvelopAutoDetectStatus',// „Erkannt: Filiale …"
        'dvelopBackfillAlert',   // Backfill-Block
        'dvelopBackfillResult',
    ];
    ids.forEach(id => { const el = document.getElementById(id); if (el) el.innerHTML = ''; });
    // Datei-Inputs leeren
    ['dvelopCsvFile','dvelopZipFile','dvelopBackfillFile'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.value = '';
    });
    // Hidden-Inputs leeren
    ['dvelopEmployeeId','dvelopEmployeeSearch'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.value = '';
    });
    // Buttons wieder deaktivieren
    ['dvelopImportCommitBtn','dvelopBackfillCommitBtn'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.disabled = true;
    });
}

async function dvelopLoadEmployees() {
    // KEIN Reset hier — diese Funktion wird auch von dvelopAutoDetect()
    // aufgerufen, nachdem die CSV ausgewählt UND die Filiale erkannt wurde.
    // Ein Reset hier würde die gerade ausgewählte Datei und den
    // „Erkannt: …"-Text wieder wegblasen. Der Reset läuft separat beim
    // Öffnen der Page (showPage-Hook in index.html).
    //
    // Walter 14.06.2026: leichter Lookup-Endpoint mit 60-s-Cache (siehe
    // employee-lookup-cache.js). Wird per `invalidateEmployeeLookupCache()`
    // an allen Mutationspfaden geleert — der frühere Konsistenz-Bug („MA
    // gerade angelegt, aber Cache zeigt ihn nicht") tritt nicht mehr auf,
    // weil saveEmployee/vtSave den Cache aktiv leeren.
    const branchId = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    try {
        const all = await loadEmployeeLookup();
        _dvelopEmployees = filterEmployeesByBranch(all, branchId);
        // Backend sortiert schon (firstName, lastName) — kein Resort nötig.
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

        // ── 2) MA-Match: zuerst MA-Nummer (inkl. ALTER Nummern/Aliase), dann
        //    Vorname+Nachname (+Geb). Walter 10.07.2026: Restaurant-Wechsler
        //    (z.B. Dossier unter 104374, heute 2300022 mit Alias 104374alt)
        //    werden über die Alias-Nummern und notfalls FILIALÜBERGREIFEND gefunden.
        const stripAlt = n => (n || '').replace(/alt$/i, '');
        const numMatch = (e, nr) => stripAlt(e.employeeNumber) === nr
            || (e.numberAliases || []).some(a => stripAlt(a) === nr);
        const nameMatchIn = list => {
            const vn = (vorname || '').toLowerCase();
            const nn = (nachname || '').toLowerCase();
            if (!vn || !nn) return null;
            const hits = list.filter(e =>
                (e.firstName || '').toLowerCase() === vn
                && (e.lastName || '').toLowerCase() === nn);
            if (hits.length === 1) return hits[0];
            if (hits.length > 1)
                return geb ? (hits.find(e => (e.dateOfBirth || '').startsWith(geb)) ?? hits[0]) : hits[0];
            return null;
        };

        let maHit = null;
        let crossBranch = false;
        if (maNr) maHit = _dvelopEmployees.find(e => numMatch(e, maNr));
        if (!maHit) maHit = nameMatchIn(_dvelopEmployees);
        if (!maHit) {
            // Fallback über ALLE Filialen (MA hat das Restaurant gewechselt)
            try {
                const all = await loadEmployeeLookup();
                if (maNr) maHit = all.find(e => numMatch(e, maNr));
                if (!maHit) maHit = nameMatchIn(all);
                if (maHit) {
                    crossBranch = true;
                    // In die (filial-gefilterte) Picker-Liste aufnehmen, sonst
                    // würde dvelopEmpInputChanged die Auswahl gleich wieder leeren.
                    if (!_dvelopEmployees.some(e => e.id === maHit.id)) {
                        _dvelopEmployees.push(maHit);
                        renderDvelopEmployeeOptions();
                    }
                }
            } catch (_) { /* Lookup nicht verfügbar → manuell wählen */ }
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
            maInfo = `MA <b>${maHit.firstName} ${maHit.lastName}</b> (${maHit.employeeNumber})`
                + (crossBranch ? ` <span style="color:#b45309">— heute in anderer Filiale, über alte Nummer/Name gefunden</span>` : '');
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
        // Walter-Vorgabe 10.07.2026: wurde der Import aus dem MA-Dokumente-Tab
        // geöffnet, zurück ZUM MA und die Dokumentenliste frisch laden.
        if (!dryRun) {
            alertBox.innerHTML = `<div style="padding:10px 14px;background:#dcfce7;color:#15803d;border-radius:7px;font-size:13px">✓ Import abgeschlossen — Fenster wird in 2 Sekunden geschlossen…</div>`;
            setTimeout(() => {
                const empId = window._dvelopReturnEmpId || null;
                window._dvelopReturnEmpId = null;
                if (typeof showPage !== 'function') return;
                if (empId) {
                    window.activeEmpId = empId;   // loadMitarbeiterList selektiert ihn mit höchster Priorität
                    showPage('mitarbeiter');
                    // Doku-Tab (re)aktivieren, sobald das Detail gerendert ist —
                    // switchEmpTab('dokumente') lädt die Liste immer frisch.
                    setTimeout(() => {
                        if (typeof switchEmpTab === 'function') switchEmpTab('dokumente');
                    }, 900);
                } else {
                    showPage('admin-hub');
                }
            }, 2000);
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

// ══════════════════════════════════════════════════════════════════════
// METADATEN-BACKFILL (Walter-Vorgabe 24.05.2026)
// Nur die Excel/CSV hochladen → trägt zu BEREITS importierten Dokumenten die
// d.velop-Datumsfelder + „Im Besitz von" nach. Mitarbeiter + Dokument werden
// pro Zeile selbst aufgelöst (Mitarbeiter-Nr + Dateiname). Keine MA-Auswahl.
// ══════════════════════════════════════════════════════════════════════
async function dvelopBackfillAnalyze() { await dvelopBackfillRun(true); }
async function dvelopBackfillCommit()  { await dvelopBackfillRun(false); }

async function dvelopBackfillRun(dryRun) {
    const fileIn   = document.getElementById('dvelopBackfillFile');
    const alertBox = document.getElementById('dvelopBackfillAlert');
    const resultBox = document.getElementById('dvelopBackfillResult');
    const analyzeBtn = document.querySelector('button[onclick="dvelopBackfillAnalyze()"]');
    const commitBtn  = document.getElementById('dvelopBackfillCommitBtn');

    if (!fileIn.files.length) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Bitte die d.velop-Export-Datei (CSV oder XLSX) wählen.</div>`;
        return;
    }

    if (analyzeBtn) analyzeBtn.disabled = true;
    if (commitBtn)  commitBtn.disabled  = true;
    alertBox.innerHTML = `<div style="padding:12px 16px;background:#fef3c7;border:1px solid #fde68a;border-radius:9px;font-size:13px;color:#78350f">${dryRun ? '⏳ Datei wird analysiert…' : '⏳ Metadaten werden nachgetragen…'}</div>`;

    const fd = new FormData();
    fd.append('csvFile', fileIn.files[0]);
    fd.append('dryRun', dryRun ? 'true' : 'false');

    try {
        const r = await fetch('/api/documents/import-dvelop/backfill-metadata', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd
        });
        if (!r.ok) throw new Error((await r.text()) || ('HTTP ' + r.status));
        const res = await r.json();
        alertBox.innerHTML = '';
        renderDvelopBackfillResult(res, dryRun);
        if (!dryRun) {
            alertBox.innerHTML = `<div style="padding:10px 14px;background:#dcfce7;color:#15803d;border-radius:7px;font-size:13px">✓ ${res.updated} Dokumente aktualisiert.</div>`;
        }
    } catch (err) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Fehler: ${err.message}</div>`;
    } finally {
        if (analyzeBtn) analyzeBtn.disabled = false;
    }
}

function renderDvelopBackfillResult(r, dryRun) {
    const box = document.getElementById('dvelopBackfillResult');
    const commitBtn = document.getElementById('dvelopBackfillCommitBtn');

    box.innerHTML = `
    <div style="display:grid;grid-template-columns:repeat(auto-fill, minmax(160px, 1fr));gap:10px">
        ${tileCard('Zeilen total', r.totalRows, '#0f172a')}
        ${tileCard(dryRun ? 'Würden aktualisiert' : 'Aktualisiert ✓', r.updated, '#15803d')}
        ${tileCard('Schon aktuell', r.unchanged || 0, '#94a3b8')}
        ${tileCard('MA nicht gefunden', r.noEmployee, r.noEmployee ? '#b91c1c' : '#94a3b8')}
        ${tileCard('Dokument nicht gefunden', r.noDocument, r.noDocument ? '#b91c1c' : '#94a3b8')}
        ${tileCard('Ohne Daten', r.noData, '#a16207')}
    </div>`;

    // Walter-Vorgabe 06.06.2026: Liste der aktualisierten Zeilen — pro Zeile
    // MA-Nr + Name + Dateiname + welche Felder sich ändern. Standard offen,
    // weil die Information hilfreich ist; bei vielen Zeilen ist's scrollbar.
    if (r.updatedItems && r.updatedItems.length) {
        const items = r.updatedItems;
        const rowsHtml = items.map(u => `
            <tr>
                <td style="padding:5px 8px;border-bottom:1px solid #efece5;font-size:12px;white-space:nowrap">
                    <b>${esc(u.maName || '–')}</b>
                    ${u.maNr ? `<div style="font-size:11px;color:#94a3b8">Nr. ${esc(u.maNr)}</div>` : ''}
                </td>
                <td style="padding:5px 8px;border-bottom:1px solid #efece5;font-size:12px;color:#0f172a">${esc(u.filename || '–')}</td>
                <td style="padding:5px 8px;border-bottom:1px solid #efece5;font-size:11.5px;color:#475569">${esc(u.beschreibung || '')}</td>
                <td style="padding:5px 8px;border-bottom:1px solid #efece5;font-size:11px">
                    ${(u.changedFields || []).map(f =>
                        `<span style="display:inline-block;background:#ece9e2;color:#6b7280;padding:1px 6px;border-radius:4px;margin:0 3px 2px 0;font-size:10.5px">${esc(f)}</span>`
                    ).join('')}
                </td>
            </tr>`).join('');
        box.innerHTML += `
        <details open style="margin-top:14px;padding:12px 14px;background:#f6f3ee;border:1px solid #e5e0d6;border-radius:9px">
            <summary style="cursor:pointer;font-size:13.5px;font-weight:700;color:#6b6152;list-style:none;display:flex;align-items:center;justify-content:space-between">
                <span>📋 ${dryRun ? 'Würden aktualisiert' : 'Aktualisiert'}: ${items.length} Zeile${items.length !== 1 ? 'n' : ''}</span>
                <span style="font-size:11px;color:#6b7280;font-weight:400">▼ einklappen</span>
            </summary>
            <div style="margin-top:8px;max-height:380px;overflow:auto;border:1px solid #e5e0d6;border-radius:6px;background:#fff">
                <table style="width:100%;border-collapse:collapse">
                    <thead style="position:sticky;top:0;background:#ece9e2;z-index:1">
                        <tr>
                            <th style="padding:6px 8px;text-align:left;font-size:10.5px;color:#6b6152;text-transform:uppercase;letter-spacing:.04em">Mitarbeiter</th>
                            <th style="padding:6px 8px;text-align:left;font-size:10.5px;color:#6b6152;text-transform:uppercase;letter-spacing:.04em">Dateiname</th>
                            <th style="padding:6px 8px;text-align:left;font-size:10.5px;color:#6b6152;text-transform:uppercase;letter-spacing:.04em">Beschreibung</th>
                            <th style="padding:6px 8px;text-align:left;font-size:10.5px;color:#6b6152;text-transform:uppercase;letter-spacing:.04em">Geänderte Felder</th>
                        </tr>
                    </thead>
                    <tbody>${rowsHtml}</tbody>
                </table>
            </div>
        </details>`;
    }

    // Walter-Vorgabe 06.06.2026: fehlende Mitarbeiter als Karte mit „Anlegen"-
    // Button. Nutzt MA-Nr 1:1 oder mit alt-Suffix bei Kollision (analog Archiv-
    // Import). Anschliessend kann Walter „Analysieren" erneut klicken → MA
    // ist gefunden → Dokumente erscheinen als „fehlt bei uns" → Schnell-Upload.
    if (r.missingEmployees && r.missingEmployees.length) {
        const me = r.missingEmployees;
        window._dvelopMissingEmployees = me;
        const meRows = me.map((m, i) => {
            const dob = m.geburtsIso ? new Date(m.geburtsIso).toLocaleDateString('de-CH') : '';
            const sub = [dob ? 'geb. ' + dob : '', m.mandant, m.dvelopStatus].filter(Boolean).join(' · ');
            return `
            <tr id="dvelopMissingEmpRow-${i}">
                <td style="padding:6px 8px;border-bottom:1px solid #fde2e2;font-size:12.5px">
                    <b>${esc(m.vorname || '–')} ${esc(m.nachname || '–')}</b>
                    ${m.maNr ? `<span style="color:#94a3b8;font-size:11px;margin-left:6px">Nr. ${esc(m.maNr)}</span>` : ''}
                    ${sub ? `<div style="font-size:11px;color:#94a3b8;margin-top:2px">${esc(sub)}</div>` : ''}
                </td>
                <td style="padding:6px 8px;border-bottom:1px solid #fde2e2;font-size:12px;color:#475569;white-space:nowrap;text-align:center">
                    ${m.dokumentCount} Dokus
                </td>
                <td style="padding:6px 8px;border-bottom:1px solid #fde2e2;font-size:11px;text-align:right;white-space:nowrap">
                    <button class="btn" style="font-size:11px;padding:4px 10px;background:#ece9e2;color:#6b7280;border:1px solid #d0c8b8;border-radius:5px;font-weight:600;cursor:pointer" onclick="dvelopCreateMissingEmployee(${i})" title="MA als Personaldossier (inaktiv, kein Vertrag) anlegen — alt-Suffix bei MA-Nr-Kollision">👤 Anlegen</button>
                </td>
            </tr>`;
        }).join('');
        box.innerHTML += `
        <div style="margin-top:14px;padding:12px 14px;background:#fef2f2;border:1px solid #fca5a5;border-radius:9px">
            <div style="font-size:13.5px;font-weight:700;color:#7f1d1d;margin-bottom:4px">⚠ ${me.length} Mitarbeiter fehl${me.length === 1 ? 't' : 'en'} bei uns</div>
            <div style="font-size:11.5px;color:#991b1b;margin-bottom:8px">Steht im d.velop, aber nicht in unserer DB — wird als Personaldossier (inaktiv, kein Vertrag) angelegt. Nach Anlegen erneut „Analysieren" klicken → Dokumente erscheinen als „fehlt bei uns" zum Hochladen.</div>
            <div style="max-height:300px;overflow:auto;border:1px solid #fecaca;border-radius:6px;background:#fff">
                <table style="width:100%;border-collapse:collapse">
                    <thead style="position:sticky;top:0;background:#fee2e2;z-index:1">
                        <tr>
                            <th style="padding:6px 8px;text-align:left;font-size:10.5px;color:#7f1d1d;text-transform:uppercase;letter-spacing:.04em">Mitarbeiter</th>
                            <th style="padding:6px 8px;text-align:center;font-size:10.5px;color:#7f1d1d;text-transform:uppercase;letter-spacing:.04em">Dokus</th>
                            <th style="padding:6px 8px;text-align:right;font-size:10.5px;color:#7f1d1d;text-transform:uppercase;letter-spacing:.04em">Aktion</th>
                        </tr>
                    </thead>
                    <tbody>${meRows}</tbody>
                </table>
            </div>
        </div>`;
    }

    // Walter-Vorgabe 06.06.2026: strukturierte Liste der fehlenden Dokumente —
    // damit Walter sie gezielt nachimportieren kann. Eigene Karte mit Tabelle
    // (MA, Dateiname, Kategorie, Typ, Beschreibung, d.velop-Link).
    if (r.missingDocuments && r.missingDocuments.length) {
        const missing = r.missingDocuments;
        // Index in window für Direkt-Upload-Klicks
        window._dvelopMissingDocs = missing;
        const rows = missing.map((m, i) => {
            // Schnell-Upload nur aktiv, wenn Backend Employee + DokumentTyp aufgelöst hat
            const canQuickUpload = m.employeeId > 0 && m.dokumentTypId > 0 && m.branchCode;
            let uploadBtn;
            if (canQuickUpload) {
                uploadBtn = `<div style="display:flex;flex-direction:column;gap:3px;align-items:flex-end">
                    <button class="btn" style="font-size:11px;padding:3px 8px;background:#dcfce7;color:#15803d;border:1px solid #86efac;border-radius:5px;font-weight:600;cursor:pointer" onclick="dvelopMissingQuickUpload(${i})" title="Lokale Datei picken — Metadaten kommen aus dem Excel">📥 Hier hochladen</button>
                    <button class="btn" style="font-size:10.5px;padding:2px 7px;background:#f6f3ee;color:#6b7280;border:1px solid #d0c8b8;border-radius:5px;font-weight:600;cursor:pointer" onclick="dvelopOpenAssignModal(${i})" title="Diese d.velop-Zeile einem bereits existierenden DB-Dokument zuordnen (XG-ID + d.velop-Daten nachtragen)">🔗 Bestehendem zuordnen</button>
                </div>`;
            } else if (!m.dokumentTypId && m.kategorie && m.typ) {
                // Walter-Vorgabe 06.06.2026 (final): Taxonomie wird AUSSCHLIESSLICH
                // vom Admin in den Systemeinstellungen → Dokument-Struktur gepflegt.
                // Hier nur Hinweis was fehlt — kein Inline-Anlegen.
                uploadBtn = `<div style="font-size:10.5px;color:#7f1d1d;text-align:right;line-height:1.4">
                    <div>⚠ Typ <b>„${esc(m.typ)}"</b> fehlt</div>
                    <div style="color:#94a3b8;font-size:10px">Admin → Systemeinstellungen →<br>Dokument-Struktur → „${esc(m.kategorie)}"</div>
                </div>`;
            } else {
                uploadBtn = `<span style="font-size:10.5px;color:#94a3b8" title="${m.branchCode ? '' : 'MA hat keinen aktiven Vertrag — keine Filiale.'}">⚠ Auto-Upload nicht möglich</span>`;
            }
            return `
            <tr id="dvelopMissingRow-${i}">
                <td style="padding:5px 8px;border-bottom:1px solid #fde2e2;font-size:12px;white-space:nowrap">
                    <b>${esc(m.maName || '–')}</b>
                    ${m.maNr ? `<div style="font-size:11px;color:#94a3b8">Nr. ${esc(m.maNr)}</div>` : ''}
                </td>
                <td style="padding:5px 8px;border-bottom:1px solid #fde2e2;font-size:12px;color:#0f172a">${esc(m.filename || '–')}</td>
                <td style="padding:5px 8px;border-bottom:1px solid #fde2e2;font-size:11.5px;color:#475569">${esc(m.kategorie || '–')}<div style="font-size:11px;color:#94a3b8">${esc(m.typ || '')}</div></td>
                <td style="padding:5px 8px;border-bottom:1px solid #fde2e2;font-size:11.5px;color:#475569">${esc(m.beschreibung || '')}</td>
                <td style="padding:5px 8px;border-bottom:1px solid #fde2e2;font-size:11px;text-align:right;white-space:nowrap">
                    <div style="display:flex;flex-direction:column;align-items:flex-end;gap:3px">
                        ${m.url ? `<a href="${esc(m.url)}" target="_blank" rel="noopener" style="color:#6b7280;text-decoration:underline">→ d.velop</a>` : ''}
                        ${uploadBtn}
                        ${m.dokumentId ? `<div style="color:#94a3b8;font-family:monospace;font-size:10px">${esc(m.dokumentId)}</div>` : ''}
                    </div>
                </td>
            </tr>`;
        }).join('');
        box.innerHTML += `
        <div style="margin-top:14px;padding:12px 14px;background:#fef2f2;border:1px solid #fca5a5;border-radius:9px">
            <div style="display:flex;align-items:center;justify-content:space-between;gap:10px;margin-bottom:8px">
                <div>
                    <div style="font-size:13.5px;font-weight:700;color:#7f1d1d">⚠ <span class="dvelop-missing-count">${missing.length}</span> Dokument${missing.length !== 1 ? 'e' : ''} fehlt${missing.length !== 1 ? 'en' : ''} bei uns</div>
                    <div style="font-size:11.5px;color:#991b1b;margin-top:2px">Steht im d.velop, aber nicht in unserer DB — bitte als ZIP oder einzeln nachimportieren.</div>
                </div>
                <button class="btn btn-outline" style="font-size:12px;padding:4px 10px" onclick="dvelopExportMissingCsv()">📋 als CSV</button>
            </div>
            <div style="max-height:340px;overflow:auto;border:1px solid #fecaca;border-radius:6px;background:#fff">
                <table style="width:100%;border-collapse:collapse">
                    <thead style="position:sticky;top:0;background:#fee2e2;z-index:1">
                        <tr>
                            <th style="padding:6px 8px;text-align:left;font-size:10.5px;color:#7f1d1d;text-transform:uppercase;letter-spacing:.04em">Mitarbeiter</th>
                            <th style="padding:6px 8px;text-align:left;font-size:10.5px;color:#7f1d1d;text-transform:uppercase;letter-spacing:.04em">Dateiname</th>
                            <th style="padding:6px 8px;text-align:left;font-size:10.5px;color:#7f1d1d;text-transform:uppercase;letter-spacing:.04em">Kategorie / Typ</th>
                            <th style="padding:6px 8px;text-align:left;font-size:10.5px;color:#7f1d1d;text-transform:uppercase;letter-spacing:.04em">Beschreibung</th>
                            <th style="padding:6px 8px;text-align:right;font-size:10.5px;color:#7f1d1d;text-transform:uppercase;letter-spacing:.04em">d.velop</th>
                        </tr>
                    </thead>
                    <tbody>${rows}</tbody>
                </table>
            </div>
        </div>`;
        // Liste für CSV-Export merken
        window._dvelopMissingDocs = missing;
    }
    // Klassische Unmatched-Liste (z.B. „MA nicht gefunden") bleibt als kleines
    // Details-Element darunter — weniger prominent, weil seltener relevant.
    if (r.unmatched && r.unmatched.length) {
        box.innerHTML += `
        <details style="margin-top:12px">
            <summary style="cursor:pointer;font-size:12px;color:#64748b">${r.unmatched.length} weitere Detail-Meldungen</summary>
            <div style="max-height:200px;overflow:auto;margin-top:6px;font-size:11.5px;color:#475569;background:#f8fafc;border-radius:6px;padding:8px;line-height:1.6">
                ${r.unmatched.map(u => esc(u)).join('<br>')}
            </div>
        </details>`;
    }

    if (commitBtn) {
        if (dryRun) {
            commitBtn.disabled = (r.updated === 0);
            commitBtn.textContent = r.updated > 0 ? `Nachtragen bestätigen (${r.updated})` : 'Nachtragen bestätigen';
        } else {
            commitBtn.disabled = true;
            commitBtn.textContent = 'Nachtragen bestätigen';
        }
    }
}

// Walter-Vorgabe 06.06.2026: bestehendes DB-Dokument einer d.velop-Zeile zuordnen.
// Setzt die XG-ID + d.velop-Datumsfelder am bereits existierenden Datensatz,
// ohne Bemerkung/Kategorie/Typ zu überschreiben (Walter hat die manuell angepasst).
async function dvelopOpenAssignModal(idx) {
    const list = window._dvelopMissingDocs || [];
    const m = list[idx];
    if (!m) return;
    if (!m.employeeId) { alert('Kein Mitarbeiter aufgelöst — Zuordnen nicht möglich.'); return; }
    const overlay = _dvelopBuildAssignModal();
    overlay.style.display = 'flex';
    const body = overlay.querySelector('.dvelop-assign-body');
    body.innerHTML = '<div style="padding:18px;color:#64748b;font-size:13px">⏳ Lade Dokumente des MA…</div>';
    try {
        const r = await fetch(`/api/documents/by-employee/${m.employeeId}`, { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const docs = await r.json();
        _dvelopRenderAssignModal(overlay, m, idx, docs);
    } catch (err) {
        body.innerHTML = `<div style="padding:18px;color:#b91c1c;font-size:13px">Fehler: ${esc(err.message)}</div>`;
    }
}

function _dvelopBuildAssignModal() {
    let overlay = document.getElementById('dvelopAssignOverlay');
    if (overlay) return overlay;
    overlay = document.createElement('div');
    overlay.id = 'dvelopAssignOverlay';
    overlay.style.cssText = 'position:fixed;inset:0;background:rgba(15,23,42,.5);z-index:9999;display:none;align-items:center;justify-content:center;padding:30px';
    overlay.innerHTML = `
        <div style="background:white;border-radius:12px;width:min(900px,95vw);max-height:85vh;display:flex;flex-direction:column;overflow:hidden;box-shadow:0 30px 60px rgba(0,0,0,.3)">
            <div style="padding:14px 18px;border-bottom:1px solid #e2e8f0;display:flex;align-items:center;justify-content:space-between">
                <div>
                    <div style="font-size:15px;font-weight:700;color:#0f172a">🔗 Zuordnen: d.velop-Zeile → existierendes Dokument</div>
                    <div class="dvelop-assign-subtitle" style="font-size:11.5px;color:#94a3b8;margin-top:2px"></div>
                </div>
                <button onclick="document.getElementById('dvelopAssignOverlay').style.display='none'" style="background:none;border:none;font-size:22px;color:#94a3b8;cursor:pointer">×</button>
            </div>
            <div class="dvelop-assign-body" style="flex:1;overflow:auto"></div>
        </div>`;
    overlay.addEventListener('click', (ev) => {
        if (ev.target === overlay) overlay.style.display = 'none';
    });
    document.body.appendChild(overlay);
    return overlay;
}

function _dvelopRenderAssignModal(overlay, m, idx, docs) {
    overlay.querySelector('.dvelop-assign-subtitle').textContent =
        `d.velop ${m.dokumentId || '–'} · ${m.kategorie || '–'} / ${m.typ || '–'} · ${m.beschreibung || ''} · ${m.filename || ''}`;
    if (!docs.length) {
        overlay.querySelector('.dvelop-assign-body').innerHTML =
            `<div style="padding:24px;text-align:center;color:#94a3b8;font-size:13px">Keine bestehenden Dokumente für ${esc(m.maName)} in unserer DB.</div>`;
        return;
    }
    const rows = docs.map(d => {
        const erstellt = d.erstelltAm || d.gueltigVon || d.hochgeladenAm;
        const erstelltFmt = erstellt ? new Date(erstellt).toLocaleDateString('de-CH') : '–';
        const alreadyMapped = !!d.dvelopDokumentId;
        const sameXg = alreadyMapped && d.dvelopDokumentId === m.dokumentId;
        const stateChip = sameXg
            ? `<span style="font-size:10px;color:#15803d;background:#dcfce7;padding:2px 6px;border-radius:4px">bereits dieser XG-ID</span>`
            : alreadyMapped
                ? `<span style="font-size:10px;color:#b91c1c;background:#fee2e2;padding:2px 6px;border-radius:4px" title="${esc(d.dvelopDokumentId)}">andere XG-ID</span>`
                : '';
        const disabled = alreadyMapped && !sameXg;
        return `
        <tr style="${disabled ? 'opacity:.5' : ''}">
            <td style="padding:6px 8px;border-bottom:1px solid #f1f5f9;font-size:11.5px;color:#475569">${esc(d.kategorieName || '–')}<div style="font-size:11px;color:#94a3b8">${esc(d.dokumentTypName || '')}</div></td>
            <td style="padding:6px 8px;border-bottom:1px solid #f1f5f9;font-size:12px;color:#0f172a"><b>${esc(d.bemerkung || d.filenameOriginal || '–')}</b>${stateChip ? ' ' + stateChip : ''}<div style="font-size:11px;color:#94a3b8">${esc(d.filenameOriginal || '')}</div></td>
            <td style="padding:6px 8px;border-bottom:1px solid #f1f5f9;font-size:11.5px;color:#64748b;white-space:nowrap;text-align:right">${erstelltFmt}</td>
            <td style="padding:6px 8px;border-bottom:1px solid #f1f5f9;text-align:right;white-space:nowrap">
                ${disabled
                    ? '<span style="font-size:10.5px;color:#94a3b8">—</span>'
                    : `<button onclick="dvelopAssignDoc(${idx}, ${d.id})" style="font-size:11px;padding:3px 9px;background:#ece9e2;color:#6b7280;border:1px solid #d0c8b8;border-radius:5px;font-weight:600;cursor:pointer">${sameXg ? 'erneut zuordnen' : '✓ Dieses wählen'}</button>`}
            </td>
        </tr>`;
    }).join('');
    overlay.querySelector('.dvelop-assign-body').innerHTML = `
        <div style="padding:8px 16px;font-size:11.5px;color:#475569;background:#f8fafc;border-bottom:1px solid #e2e8f0">
            Picke das existierende DB-Dokument, das diesem d.velop-Eintrag entspricht. Wir setzen dann die <b>d.velop-XG-ID</b> + die <b>d.velop-Datumsfelder</b> auf den existierenden Datensatz. Bemerkung, Kategorie und Typ bleiben so wie du sie angepasst hast.
        </div>
        <table style="width:100%;border-collapse:collapse">
            <thead style="position:sticky;top:0;background:#f1f5f9;z-index:1">
                <tr>
                    <th style="padding:6px 8px;text-align:left;font-size:10px;color:#64748b;text-transform:uppercase">Kategorie/Typ</th>
                    <th style="padding:6px 8px;text-align:left;font-size:10px;color:#64748b;text-transform:uppercase">Beschreibung / Dateiname</th>
                    <th style="padding:6px 8px;text-align:right;font-size:10px;color:#64748b;text-transform:uppercase">Datum</th>
                    <th style="padding:6px 8px;text-align:right;font-size:10px;color:#64748b;text-transform:uppercase">Aktion</th>
                </tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>`;
}

async function dvelopAssignDoc(idx, existingDocId) {
    const list = window._dvelopMissingDocs || [];
    const m = list[idx];
    if (!m) return;
    const overlay = document.getElementById('dvelopAssignOverlay');
    if (overlay) {
        overlay.querySelector('.dvelop-assign-body').innerHTML =
            '<div style="padding:20px;text-align:center;color:#64748b;font-size:13px">⏳ Zuordnung wird gespeichert…</div>';
    }
    try {
        const r = await fetch('/api/documents/import-dvelop/assign-to-existing', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                existingDocId,
                dvelopDokumentId: m.dokumentId,
                erstelltAm:       m.erstelltAm,
                geaendertAm:      m.geaendertAm,
                dateiGeaendertAm: m.dateiGeaendertAm,
                zugriffAm:        m.zugriffAm,
                geaendertVon:     m.geaendertVon
            })
        });
        if (r.status === 409) {
            const body = await r.clone().json().catch(() => ({}));
            alert(body.message || 'Konflikt — Dokument bereits anders verknüpft.');
            if (overlay) overlay.style.display = 'none';
            return;
        }
        if (!r.ok) throw new Error((await r.text()) || ('HTTP ' + r.status));
        // Erfolg → Missing-Zeile entfernen + Counter dekrementieren
        if (overlay) overlay.style.display = 'none';
        const rowEl = document.getElementById('dvelopMissingRow-' + idx);
        if (rowEl) {
            rowEl.style.transition = 'background .2s, opacity .4s';
            rowEl.style.background = '#ece9e2';
            setTimeout(() => { rowEl.style.opacity = '0'; setTimeout(() => rowEl.remove(), 400); }, 250);
        }
        const cntEl = document.querySelector('.dvelop-missing-count');
        if (cntEl) {
            const n = (parseInt(cntEl.textContent, 10) || 0) - 1;
            cntEl.textContent = n;
        }
    } catch (err) {
        alert('Fehler: ' + err.message);
        if (overlay) overlay.style.display = 'none';
    }
}

// Walter-Vorgabe 06.06.2026: fehlenden MA als Personaldossier anlegen.
async function dvelopCreateMissingEmployee(idx, forceCreate = false) {
    const list = window._dvelopMissingEmployees || [];
    const m = list[idx];
    if (!m) return;
    const rowEl = document.getElementById('dvelopMissingEmpRow-' + idx);
    const actionCell = rowEl?.querySelector('td:last-child');
    if (actionCell) actionCell.innerHTML = '<span style="font-size:11px;color:#64748b">⏳ Lege an…</span>';

    try {
        const r = await fetch('/api/documents/import-dvelop/create-missing-employee', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                maNr:       m.maNr || '',
                vorname:    m.vorname || '',
                nachname:   m.nachname || '',
                geburtsIso: m.geburtsIso || null,
                branchCode: m.branchCode || null,
                inactive:   true,
                forceCreate: !!forceCreate
            })
        });
        // Walter-Vorgabe 06.06.2026: 409 POSSIBLE_DUPLICATE → Confirm zeigen
        if (r.status === 409) {
            const body = await r.clone().json().catch(() => ({}));
            if (body?.error === 'POSSIBLE_DUPLICATE') {
                const ok = confirm(`⚠ Möglicher Duplikat-Mitarbeiter!\n\n` +
                    `Bei uns existiert bereits:\n` +
                    `  ${body.existingName} (Nr. ${body.existingNr || '–'})\n` +
                    `  geb. ${body.existingGeb ? new Date(body.existingGeb).toLocaleDateString('de-CH') : '–'}\n\n` +
                    `Aus d.velop kommt:\n` +
                    `  ${m.vorname} ${m.nachname} (Nr. ${m.maNr || '–'})\n` +
                    `  geb. ${m.geburtsIso ? new Date(m.geburtsIso).toLocaleDateString('de-CH') : '–'}\n\n` +
                    `Es ist vermutlich derselbe MA mit einer typografischen Abweichung (z.B. „I" vs „l").\n\n` +
                    `OK = trotzdem als neuer MA anlegen\nABBRECHEN = bestehenden MA verwenden (kein neuer Eintrag)`);
                if (!ok) {
                    if (actionCell) actionCell.innerHTML = `<span style="font-size:11px;color:#0f172a">✓ Verwende ${esc(body.existingName)}</span>`;
                    if (rowEl) rowEl.style.background = '#ece9e2';
                    return;
                }
                // User bestätigt: trotzdem anlegen
                return dvelopCreateMissingEmployee(idx, true);
            }
        }
        if (!r.ok) throw new Error((await r.text()) || ('HTTP ' + r.status));
        const res = await r.json();
        const note = res.usedAltSuffix
            ? `Nr. <b>${esc(res.finalNr)}</b> (Original ${esc(res.originalNr)} war besetzt → alt-Suffix)`
            : `Nr. <b>${esc(res.finalNr)}</b>`;
        if (rowEl) {
            rowEl.style.background = '#dcfce7';
            if (actionCell) actionCell.innerHTML = `<span style="font-size:11px;color:#15803d">✓ angelegt · ${note}</span>`;
        }
    } catch (err) {
        if (actionCell) actionCell.innerHTML = `<span style="font-size:11px;color:#b91c1c">✗ ${esc(err.message)}</span>`;
    }
}

// Walter-Vorgabe 06.06.2026: Schnell-Upload aus der „fehlende Dokumente"-Liste.
// Walter lädt die Datei vom d.velop in Downloads → klickt hier „📥 Hier hochladen"
// → File-Picker öffnet → Datei gewählt → POST an /api/documents/upload mit allen
// Metadaten aus dem Excel (Employee, Typ, Branch, Beschreibung). Zeile verschwindet
// bei Erfolg, Fehler-Toast bei Misserfolg (z.B. Duplikat).
function dvelopMissingQuickUpload(idx) {
    const list = window._dvelopMissingDocs || [];
    const m = list[idx];
    if (!m) return;
    // Versteckten File-Picker bauen + auslösen
    const input = document.createElement('input');
    input.type = 'file';
    // d.velop liefert Dateinamen — wir setzen ihn als Hint, der Browser kann aber
    // jeden Namen akzeptieren (User-Datei hat oft den gleichen Namen)
    input.style.display = 'none';
    input.onchange = async () => {
        if (!input.files.length) return;
        const file = input.files[0];
        await dvelopUploadMissingFile(idx, m, file);
    };
    document.body.appendChild(input);
    input.click();
    setTimeout(() => input.remove(), 60000);  // Cleanup nach 1 Min
}

async function dvelopUploadMissingFile(idx, m, file) {
    const rowEl = document.getElementById('dvelopMissingRow-' + idx);
    const lastTd = rowEl?.querySelector('td:last-child');
    if (lastTd) lastTd.innerHTML = '<span style="font-size:11px;color:#64748b">⏳ Lade hoch…</span>';

    const fd = new FormData();
    fd.append('file', file, file.name);
    fd.append('employeeId', m.employeeId);
    fd.append('dokumentTypId', m.dokumentTypId);
    fd.append('branchCode', m.branchCode);
    if (m.beschreibung) fd.append('bemerkung', m.beschreibung);
    // Walter-Vorgabe 06.06.2026: d.velop-Datumsfelder mit hochladen, damit
    // das neue Dokument dieselben Erstellt/Geändert/Geöffnet-Werte hat wie
    // das d.velop-Original (sonst sind die Spalten in der Doku-Liste leer).
    if (m.erstelltAm)       fd.append('erstelltAm',       m.erstelltAm);
    if (m.geaendertAm)      fd.append('geaendertAm',      m.geaendertAm);
    if (m.dateiGeaendertAm) fd.append('dateiGeaendertAm', m.dateiGeaendertAm);
    if (m.zugriffAm)        fd.append('zugriffAm',        m.zugriffAm);
    if (m.geaendertVon)     fd.append('geaendertVon',     m.geaendertVon);
    if (m.dokumentId)       fd.append('dvelopDokumentId', m.dokumentId);

    try {
        const r = await fetch('/api/documents/upload', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd
        });
        if (r.status === 409) {
            const body = await r.clone().json().catch(() => ({}));
            if (lastTd) lastTd.innerHTML = `<span style="font-size:11px;color:#b45309" title="${esc(body.message || '')}">⚠ Schon vorhanden</span>`;
            return;
        }
        if (!r.ok) throw new Error((await r.text()) || ('HTTP ' + r.status));
        // Erfolg → Zeile aus der Tabelle entfernen, Counter dekrementieren
        if (rowEl) {
            rowEl.style.transition = 'background .2s, opacity .4s';
            rowEl.style.background = '#dcfce7';
            setTimeout(() => {
                rowEl.style.opacity = '0';
                setTimeout(() => rowEl.remove(), 400);
            }, 250);
        }
        // Counter im Karten-Titel updaten
        const cntEl = document.querySelector('.dvelop-missing-count');
        if (cntEl) {
            const n = (parseInt(cntEl.textContent, 10) || 0) - 1;
            cntEl.textContent = n;
        }
    } catch (err) {
        if (lastTd) lastTd.innerHTML = `<span style="font-size:11px;color:#b91c1c">✗ ${esc(err.message)}</span>`;
    }
}

// Walter-Vorgabe 06.06.2026: CSV-Export der fehlenden Dokumente, damit Walter
// die Liste als Arbeitsblatt mitnehmen oder weitergeben kann.
function dvelopExportMissingCsv() {
    const list = window._dvelopMissingDocs || [];
    if (!list.length) { alert('Keine fehlenden Dokumente zum Export.'); return; }
    const esc = v => {
        const s = String(v ?? '');
        return /[;"\n\r]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
    };
    const headers = ['MA-Nr', 'Mitarbeiter', 'Dateiname', 'Kategorie', 'Typ', 'Beschreibung', 'd.velop-ID', 'd.velop-URL'];
    const rows = list.map(m => [m.maNr, m.maName, m.filename, m.kategorie, m.typ, m.beschreibung, m.dokumentId, m.url].map(esc).join(';'));
    const csv = '﻿' + headers.join(';') + '\n' + rows.join('\n');  // BOM für Excel/UTF-8
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
    if (typeof saveBlobAsk === 'function') {
        saveBlobAsk(blob, `fehlende-dokumente-${new Date().toISOString().slice(0,10)}.csv`);
    } else {
        // Fallback (sollte nie greifen — save-blob.js wird global geladen)
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url; a.download = 'fehlende-dokumente.csv'; a.click();
        URL.revokeObjectURL(url);
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

