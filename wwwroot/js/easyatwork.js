// ════════════════════════════════════════════════════════════════════════
// easy@work API — Frontend (Phase 1 Foundation)
// Walter-Vorgabe 17.06.2026
//
// Page-ID: page-easyatwork
// Status-Box / Test-Button / Mapping-Tabelle (CompanyProfile ↔ Customer-ID)
// ════════════════════════════════════════════════════════════════════════

let _eawCustomers = [];   // Liste aus dem letzten test-connection-Aufruf
let _eawMappings  = [];   // bestehende DB-Mappings

async function eawInit() {
    eawClearResults();   // frische Seite bei jedem Öffnen (Walter-Vorgabe 19.06.2026)
    await eawLoadStatus();
    await eawLoadMappings();
    eawSyncInit();
    _eawEmpSyncInit();
    eawLogLoad();
    // Selects werden global von liquid-select.js umgebaut — kein lightSelect
    // mehr hier (sonst doppelte Filial-Dropdowns, Walter-Bug 18.07.2026).
}

// Leert die Vorschau-/Ergebnis-Bereiche + setzt den Zustand zurück. Wird beim
// Öffnen der easy@work-Seite UND nach einem Import aufgerufen, damit keine
// veraltete (oft riesige) Vorschau-Tabelle stehen bleibt.
function eawClearResults() {
    const s = document.getElementById('eawSyncResult');     if (s) s.innerHTML = '';
    const e = document.getElementById('eawEmpSyncResult');  if (e) e.innerHTML = '';
    _eawSyncLastPreview = null;
    _eawEmpSyncLast     = null;
    // Stempel-Import ist seit 09.07.2026 ein Direkt-Import (kein Vorschau-
    // Pflichtschritt) — der Button bleibt aktiv, nur der MA-Sync-Commit gated.
    const c1 = document.getElementById('eawSyncCommitBtn');     if (c1) c1.disabled = false;
    const c2 = document.getElementById('eawEmpSyncCommitBtn');  if (c2) c2.disabled = true;
}

// ═══════════════════════ Stempelzeit-Sync (Phase 2) ══════════════════════

let _eawSyncLastPreview = null;

// Schnellwahl Von/Bis (Walter-Vorgabe 21.06.2026). Setzt die bestehenden
// nativen Datumsfelder eawSyncFrom/eawSyncTo — der Browser-Datepicker bleibt
// unverändert. Direkte .value-Zuweisung (kein change-Event), damit der
// Auto-„Bis = Monatsende"-Handler von Von NICHT das gesetzte Bis überschreibt.
function eawSyncQuickRange(kind) {
    const fromEl = document.getElementById('eawSyncFrom');
    const toEl   = document.getElementById('eawSyncTo');
    if (!fromEl || !toEl) return;
    const iso = (d) => `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
    const today = new Date();
    const y = today.getFullYear(), m = today.getMonth();   // m: 0–11
    let from, to;
    switch (kind) {
        case 'current':                                     // 1. des Monats … heute
            from = new Date(y, m, 1);     to = today;                  break;
        case 'prevmonth':                                   // kompletter Vormonat
            from = new Date(y, m - 1, 1); to = new Date(y, m, 0);      break;
        case 'prevyear':                                    // gleicher Monat im Vorjahr, voll
            from = new Date(y - 1, m, 1); to = new Date(y - 1, m + 1, 0); break;
        case 'last40':                                      // heute − 40 … heute
            from = new Date(today); from.setDate(today.getDate() - 40); to = today; break;
        default: return;
    }
    fromEl.value = iso(from);
    toEl.value   = iso(to);
}

// Import-Meldungen (Stammdaten-Sync-Ergebnis inkl. Notes/⚠-Hinweise) als PDF
// sichern/drucken. Öffnet ein Druckfenster mit den Seiten-Styles + Inhalt und
// startet den Browser-Druck-/„Als PDF sichern"-Dialog. (Walter 04.07.2026)
function eawEmpPrintResult() {
    const out = document.getElementById('eawEmpSyncResult');
    if (!out || !out.textContent.trim()) {
        alert('Keine Import-Meldung vorhanden — bitte zuerst „Vorschau" oder „Importieren" ausführen.');
        return;
    }
    const sel = document.getElementById('eawEmpSyncBranchSel');
    const branch = (sel && sel.value) ? sel.options[sel.selectedIndex].textContent : 'Filiale';
    const scopeSel = document.getElementById('eawEmpSyncScope');
    const scope = scopeSel ? scopeSel.options[scopeSel.selectedIndex].textContent : '';
    const when = new Date().toLocaleString('de-CH');
    const styles = Array.from(document.querySelectorAll('style')).map(s => s.outerHTML).join('');
    const w = window.open('', '_blank');
    if (!w) { alert('Bitte Popups für diese Seite erlauben, damit das PDF erstellt werden kann.'); return; }
    w.document.write(`<!doctype html><html lang="de"><head><meta charset="utf-8">
        <title>easy@work-Import — Meldungen</title>
        ${styles}
        <style>
            /* Muss NACH den kopierten Seiten-Styles stehen, um deren
               @media print { body * { visibility:hidden } } (ToDo-Druck) zu
               neutralisieren — sonst bleiben die Druckseiten leer. */
            @page { size: portrait; margin: 14mm; }
            @media print {
                body * { visibility: visible !important; }
                #todosPrintArea, #page-todos { display: none !important; }
                @page { size: portrait; margin: 14mm; }
            }
            html, body { height: auto !important; overflow: visible !important; }
            body { padding: 22px; background: #fff !important; color:#111;
                   -webkit-print-color-adjust: exact; print-color-adjust: exact; }
            .eaw-ph1 { font-size: 20px; font-weight: 800; margin: 0 0 3px; color:#111; }
            .eaw-pmeta { color: #555; font-size: 12px; margin-bottom: 16px; border-bottom:1px solid #ccc; padding-bottom:10px; }
            .eaw-sync-table { font-size: 11px; }
        </style></head><body>
        <div class="eaw-ph1">easy@work-Import — Meldungen</div>
        <div class="eaw-pmeta">${escapeHtml(branch)}${scope ? ' · Umfang: ' + escapeHtml(scope) : ''} · ${escapeHtml(when)}</div>
        ${out.innerHTML}
        </body></html>`);
    w.document.close();
    setTimeout(() => { try { w.focus(); w.print(); } catch (e) {} }, 350);
}

function eawSyncInit() {
    // Filial-Picker mit den GEMAPPTEN Filialen befüllen (nur die haben einen Customer)
    const sel = document.getElementById('eawSyncBranchSel');
    if (!sel) return;
    const mapped = _eawMappings || [];
    sel.innerHTML = mapped.length
        ? '<option value="">— wählen —</option>' + mapped.map(m =>
            `<option value="${m.companyProfileId}">${escapeHtml(m.companyProfileName||'')} (${escapeHtml(m.restaurantCode||'')}) → ${m.easyAtWorkCustomerId}</option>`
          ).join('')
        : '<option value="">— keine Filiale gemappt —</option>';

    // Global gewählte Filiale (Sidebar-Selektor) vorauswählen, sofern sie
    // gemappt ist (Walter-Vorgabe 19.06.2026 — folgt der Sub-Page-Konvention).
    if (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId
        && mapped.some(m => Number(m.companyProfileId) === Number(fixedCompanyProfileId))) {
        sel.value = String(fixedCompanyProfileId);
    }

    // Datumsbereich-Default: letzte 7 Tage
    const fromEl = document.getElementById('eawSyncFrom');
    const toEl   = document.getElementById('eawSyncTo');
    if (fromEl && !fromEl.value) {
        const today = new Date();
        const past  = new Date(today); past.setDate(today.getDate() - 7);
        fromEl.value = past.toISOString().slice(0, 10);
        toEl.value   = today.toISOString().slice(0, 10);
    }

    // Beim Ändern von „Von" das „Bis" automatisch ans Monatsende des Von-Datums
    // setzen (Walter-Vorgabe 19.06.2026). Praktisch für Monats-Läufe: Von = 1.2.
    // → Bis = 28./29.2. Bis bleibt danach frei änderbar.
    if (fromEl && toEl) {
        fromEl.onchange = () => {
            if (fromEl.value) toEl.value = _eawEndOfMonth(fromEl.value);
        };
    }
}

/// Letzter Tag des Monats eines ISO-Datums "YYYY-MM-DD" → "YYYY-MM-DD".
function _eawEndOfMonth(iso) {
    const [y, m] = iso.split('-').map(Number);
    if (!y || !m) return iso;
    const last = new Date(y, m, 0).getDate();   // Tag 0 des Folgemonats = letzter Tag
    return `${y}-${String(m).padStart(2,'0')}-${String(last).padStart(2,'0')}`;
}

// Bewusst übersprungene easy@work-MA (Walter-Vorgabe 20.06.2026): ihre Stempel
// blockieren den Import nicht mehr und werden nicht geschrieben.
let _eawSkipIds = [];

async function eawSyncPreview() {
    _eawSkipIds = [];          // neue Vorschau → Skip-Liste zurücksetzen
    await _eawSyncRun(false);
}

// „Überspringen": MA zur Skip-Liste — und die letzte Vorschau LOKAL anpassen
// (kein neuer API-Abruf). Beim Commit wird _eawSkipIds ohnehin mitgeschickt.
function eawSkipEmployee(eawId) {
    if (!_eawSkipIds.includes(eawId)) _eawSkipIds.push(eawId);
    const p = _eawSyncLastPreview;
    if (!p) { _eawSyncRun(false); return; }   // Fallback: doch neu laden

    if (Array.isArray(p.missingEmployees))
        p.missingEmployees = p.missingEmployees.filter(m => m.eawEmployeeId !== eawId);
    let moved = 0;
    if (Array.isArray(p.rows)) {
        p.rows.forEach(r => {
            if (r.eawEmployeeId === eawId && r.status !== 'SKIPPED') {
                r.status = 'SKIPPED';
                r.reason = 'Übersprungen — nicht zugeordneter MA.';
                moved++;
            }
        });
    }
    p.countSkipped   = (p.countSkipped || 0) + moved;
    p.countUnmatched = Math.max(0, (p.countUnmatched || 0) - moved);

    _eawSyncRenderResult(p, false);
    const blocked = (p.missingEmployees && p.missingEmployees.length > 0)
                 || (p.ambiguousEmployees && p.ambiguousEmployees.length > 0);
    const commitBtn = document.getElementById('eawSyncCommitBtn');
    if (commitBtn) commitBtn.disabled = false; // Direkt-Import: immer klickbar
}
// Direkt-Import (Walter-Vorgabe 09.07.2026): keine Vorschau-Pflicht mehr —
// der Import holt selbst alles Neue, blockiert serverseitig bei fehlenden/
// mehrdeutigen MA und überspringt geschlossene Perioden. Läuft als
// Hintergrund-Job mit Fortschrittsbalken (Polling, kein Proxy-Timeout).
async function eawSyncCommit() {
    const sel    = document.getElementById('eawSyncBranchSel');
    const fromEl = document.getElementById('eawSyncFrom');
    const toEl   = document.getElementById('eawSyncTo');
    const out    = document.getElementById('eawSyncResult');
    const btn    = document.getElementById('eawSyncCommitBtn');
    if (!sel.value) { alert('Bitte zuerst Filiale wählen.'); return; }
    if (!fromEl.value || !toEl.value) { alert('Bitte Datumsbereich angeben.'); return; }
    // Bewusst KEINE Rückfrage (Walter-Vorgabe 09.07.2026): der Import ist
    // idempotent (nur NEUE Stempel) und respektiert geschlossene Perioden —
    // Klick startet direkt, der Fortschrittsbalken zeigt den Lauf.

    const preMirusEl = document.getElementById('eawSyncPreMirus');
    const dto = {
        companyProfileId: parseInt(sel.value, 10),
        from: fromEl.value,
        to:   toEl.value,
        skipEawEmployeeIds: _eawSkipIds,
        employeeCutoffOverride: (preMirusEl && preMirusEl.checked) ? '2021-01-01' : null
    };
    btn.disabled = true;
    try {
        const r = await fetch('/api/easywork/sync/timepunches/commit-async', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        const j = await r.json();
        if (!r.ok || !j.jobId) {
            out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">✗ Fehler ${r.status}</div><div class="eaw-result-msg">${escapeHtml(j.message || j.error || 'Job konnte nicht gestartet werden.')}</div></div>`;
            return;
        }
        await _eawPollTimepunchJob(j.jobId, out);
    } catch (e) {
        out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">Netzwerkfehler</div><div class="eaw-result-msg">${escapeHtml(String(e))}</div></div>`;
    } finally {
        btn.disabled = false;
    }
}

// Pollt den Import-Job und rendert den Fortschrittsbalken.
async function _eawPollTimepunchJob(jobId, out) {
    const t0 = Date.now();
    while (true) {
        await new Promise(res => setTimeout(res, 700));
        let job;
        try {
            const r = await fetch(`/api/easywork/sync/employees/job/${jobId}`, { headers: ah() });
            job = await r.json();
            if (!r.ok) throw new Error(job.error || ('HTTP ' + r.status));
        } catch (e) {
            out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">Job-Status nicht abrufbar</div><div class="eaw-result-msg">${escapeHtml(String(e))}</div></div>`;
            return;
        }
        if (job.status === 'running') { out.innerHTML = _eawJobProgressHtml(job, t0); continue; }
        if (job.status === 'error') {
            out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">✗ Import fehlgeschlagen</div><div class="eaw-result-msg">${escapeHtml(job.error || 'Unbekannter Fehler')}</div></div>`;
            return;
        }
        const body = job.result || {};
        const blocked = (body.missingEmployees && body.missingEmployees.length > 0)
                     || (body.ambiguousEmployees && body.ambiguousEmployees.length > 0)
                     || body.isBlocked === true;
        if (blocked) {
            _eawSyncLastPreview = body;
            _eawSyncRenderResult(body, true);
        } else {
            const notes = (body.notes && body.notes.length)
                ? `<div style="color:#64748b;font-size:12px;margin-top:6px">${body.notes.map(n => '• ' + escapeHtml(n)).join('<br>')}</div>` : '';
            out.innerHTML = `<div style="color:#166534;font-size:13px;padding:10px;background:#dcfce7;border:1px solid #bbf7d0;border-radius:8px">✓ Import abgeschlossen — <b>${body.inserted||0} neu</b>, ${body.updated||0} geändert, ${body.deleted||0} gelöscht${body.unchanged ? ', ' + body.unchanged + ' bereits vorhanden (unverändert)' : ''}${body.lockedSkipped ? ', ' + body.lockedSkipped + ' gesperrt übersprungen' : ''}.${notes}</div>`;
            _eawSyncLastPreview = null;
        }
        return;
    }
}

// Fortschrittsbalken: Seiten-Fortschritt wenn total bekannt, sonst Puls-Phase.
function _eawJobProgressHtml(job, t0) {
    const s = Math.round((Date.now() - t0) / 1000);
    const pct = job.total > 0 ? Math.max(2, Math.round(job.done / job.total * 100)) : null;
    const bar = pct !== null
        ? `<div style="background:#e7e1d8;border-radius:99px;height:10px;overflow:hidden;margin-top:6px"><div style="background:#3f3f3f;height:100%;width:${pct}%;border-radius:99px;transition:width .4s"></div></div>`
        : `<div style="background:#e7e1d8;border-radius:99px;height:10px;overflow:hidden;margin-top:6px"><div style="background:#b8ab93;height:100%;width:100%;opacity:.45"></div></div>`;
    return `<div style="font-size:13px;color:#475569;padding:10px;background:#f6f3ee;border:1px solid #e7e1d8;border-radius:10px;max-width:520px">
        <div style="display:flex;align-items:center;gap:8px">
            <span class="import-spinner" style="width:14px;height:14px"></span>
            <span>${escapeHtml(job.phase || 'Importiere…')}${pct !== null ? ` — <strong>${pct}%</strong>` : ''} <span style="color:#94a3b8">(${s}s)</span></span>
        </div>${bar}</div>`;
}

async function _eawSyncRun(commit) {
    const sel    = document.getElementById('eawSyncBranchSel');
    const fromEl = document.getElementById('eawSyncFrom');
    const toEl   = document.getElementById('eawSyncTo');
    const out    = document.getElementById('eawSyncResult');
    const commitBtn = document.getElementById('eawSyncCommitBtn');
    if (!sel.value) { alert('Bitte zuerst Filiale wählen.'); return; }
    if (!fromEl.value || !toEl.value) { alert('Bitte Datumsbereich angeben.'); return; }

    const _total = (commit && _eawSyncLastPreview) ? (_eawSyncLastPreview.countNew || 0) : 0;
    const _label = commit
        ? `Importiere${_total ? ' ' + _total + ' Stempelzeit(en)' : ''}`
        : 'Lese Stempelzeiten aus easy@work';
    const stopProgress = _eawStartProgress(out, _label);
    commitBtn.disabled = true;

    const preMirusEl = document.getElementById('eawSyncPreMirus');
    const dto = {
        companyProfileId: parseInt(sel.value, 10),
        from: fromEl.value,
        to:   toEl.value,
        skipEawEmployeeIds: _eawSkipIds,
        // Einmaliger Tief-Import: Stichtag auf 1.1.2021 absenken (Walter 21.06.2026).
        employeeCutoffOverride: (preMirusEl && preMirusEl.checked) ? '2021-01-01' : null
    };
    try {
        const url = commit ? '/api/easywork/sync/timepunches/commit'
                           : '/api/easywork/sync/timepunches/preview';
        const r = await fetch(url, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        const body = await r.json();
        stopProgress();
        if (!r.ok) {
            out.innerHTML = `<div class="eaw-result eaw-result-err">
                <div class="eaw-result-title">✗ Fehler ${r.status}</div>
                <div class="eaw-result-msg">${escapeHtml(body.message || JSON.stringify(body))}</div>
            </div>`;
            return;
        }
        _eawSyncLastPreview = body;
        const blocked = (body.missingEmployees && body.missingEmployees.length > 0)
                     || (body.ambiguousEmployees && body.ambiguousEmployees.length > 0);
        if (commit && !blocked) {
            // Nach erfolgreichem Import: Vorschau leeren + knappe Bestätigung.
            out.innerHTML = `<div style="color:#166534;font-size:13px;padding:10px;background:#dcfce7;border:1px solid #bbf7d0;border-radius:8px">✓ Import abgeschlossen — <b>${body.inserted||0} neu</b>, ${body.updated||0} geändert, ${body.deleted||0} gelöscht${body.unchanged ? ', ' + body.unchanged + ' bereits vorhanden (unverändert)' : ''}${body.lockedSkipped ? ', ' + body.lockedSkipped + ' gesperrt übersprungen' : ''}.</div>`;
            _eawSyncLastPreview = null;
        } else {
            _eawSyncRenderResult(body, commit);
        }
        // Nach Commit nicht erneut committen können; ebenso gesperrt, solange
        // es nicht-zuordenbare MA gibt (Preflight-Block) oder keine NEW-Zeilen.
        commitBtn.disabled = commit || blocked || !(body.countNew > 0);
    } catch (e) {
        stopProgress();
        out.innerHTML = `<div class="eaw-result eaw-result-err">
            <div class="eaw-result-title">Netzwerkfehler</div>
            <div class="eaw-result-msg">${escapeHtml(String(e))}</div>
        </div>`;
    }
}

// Live-Lauf-Anzeige mit Sekundenzähler. Gibt eine Stop-Funktion zurück.
function _eawStartProgress(el, label) {
    if (!el) return () => {};
    const t0 = Date.now();
    const render = () => {
        const s = Math.round((Date.now() - t0) / 1000);
        el.innerHTML = `<div style="color:#64748b;font-size:13px;padding:8px;display:flex;align-items:center;gap:8px">
            <span class="import-spinner" style="width:14px;height:14px"></span>
            <span>${escapeHtml(label)} … <strong>${s}s</strong></span></div>`;
    };
    render();
    const iv = setInterval(render, 1000);
    return () => clearInterval(iv);
}

function _eawSyncRenderResult(res, wasCommit) {
    const out = document.getElementById('eawSyncResult');
    const notes = (res.notes||[]).map(n => `<div style="color:#b45309;font-size:12px;padding:4px 0">⚠ ${escapeHtml(n)}</div>`).join('');

    // Blockierende Missing-Employee-Liste (Walter-Vorgabe 18.06.2026): easy@work-MA
    // mit Stempeln, die sich keiner Cowork-Personalnummer zuordnen lassen. Solange
    // diese Liste nicht leer ist, ist der Import gesperrt.
    const missing = res.missingEmployees || [];
    const missingHtml = missing.length ? `
        <div style="border:1px solid #fecaca;background:#fef2f2;border-radius:8px;padding:12px;margin-bottom:12px">
            <div style="font-weight:700;color:#991b1b;margin-bottom:4px">⛔ Import blockiert — ${missing.length} Mitarbeiter ohne Zuordnung</div>
            <div style="font-size:12px;color:#7f1d1d;margin-bottom:10px">Diese easy@work-MA haben Stempel im Zeitraum, lassen sich aber keiner Cowork-Personalnummer zuordnen. Bitte <strong>zuordnen</strong>, in Cowork anlegen — oder <strong>überspringen</strong> (alter/gelöschter Datensatz), dann wird der Rest importiert.</div>
            <table class="eaw-sync-table" style="margin:0">
                <thead><tr><th>easy@work-MA</th><th style="text-align:right">Stempel</th><th>Problem</th><th></th></tr></thead>
                <tbody>${missing.map(m => `
                    <tr>
                        <td>${escapeHtml(m.eawEmployeeName || ('easy@work-MA #' + m.eawEmployeeId))} <span style="color:#94a3b8">(#${m.eawEmployeeId}${m.eawEmployeeNumber ? ' · Nr. ' + escapeHtml(m.eawEmployeeNumber) : ''})</span></td>
                        <td style="text-align:right">${m.timepunchCount}</td>
                        <td style="color:#7f1d1d;font-size:12px">${escapeHtml(m.reason || '')}</td>
                        <td style="white-space:nowrap">
                            <button onclick="eawLookupEmployee(${m.eawEmployeeId})" title="In easy@work per ID nachschlagen — auch gelöschte/archivierte MA" style="background:#ede9fe;border:1px solid #c4b5fd;color:#6d28d9;border-radius:6px;padding:3px 8px;font-size:12px;cursor:pointer;white-space:nowrap">🔍 Nachschlagen</button>
                            <button onclick="eawAssignAlias(${m.eawEmployeeId})" style="margin-left:6px;background:#ece9e2;border:1px solid #d0c8b8;color:#6b7280;border-radius:6px;padding:3px 8px;font-size:12px;cursor:pointer;white-space:nowrap">→ MA zuordnen</button>
                            <button onclick="eawSkipEmployee(${m.eawEmployeeId})" title="Diesen MA überspringen — seine Stempel werden nicht importiert, der Rest schon" style="margin-left:6px;background:#fff;border:1px solid #cbd5e1;color:#475569;border-radius:6px;padding:3px 8px;font-size:12px;cursor:pointer;white-space:nowrap">⏭ Überspringen</button>
                        </td>
                    </tr>`).join('')}</tbody>
            </table>
        </div>` : '';

    // Ambiguous (Walter 21.06.2026): Person mit MEHREREN Lohn-MA (IsPayrollExcluded=
    // false) — Lohn-MA nicht eindeutig. BLOCKIERT immer (auch Tief-Import).
    const ambig = res.ambiguousEmployees || [];
    const ambigHtml = ambig.length ? `
        <div style="border:1px solid #fecaca;background:#fef2f2;border-radius:8px;padding:12px;margin-bottom:12px">
            <div style="font-weight:700;color:#991b1b;margin-bottom:4px">⛔ Import blockiert — ${ambig.length} Person(en) mit mehreren Lohn-MA</div>
            <div style="font-size:12px;color:#7f1d1d;margin-bottom:10px">Für diese Personen gibt es <strong>mehr als einen</strong> Cowork-MA mit Lohn (IsPayrollExcluded=false). Der Lohn-MA ist nicht eindeutig. Bitte je Person <strong>genau einen</strong> als Lohn-MA führen, die übrigen auf „kein Lohn" (IsPayrollExcluded) setzen.</div>
            <table class="eaw-sync-table" style="margin:0">
                <thead><tr><th>easy@work-MA</th><th style="text-align:right">Stempel</th><th>Problem</th></tr></thead>
                <tbody>${ambig.map(m => `
                    <tr>
                        <td>${escapeHtml(m.eawEmployeeName || ('easy@work-MA #' + m.eawEmployeeId))} <span style="color:#94a3b8">(#${m.eawEmployeeId}${m.eawEmployeeNumber ? ' · Nr. ' + escapeHtml(m.eawEmployeeNumber) : ''})</span></td>
                        <td style="text-align:right">${m.timepunchCount}</td>
                        <td style="color:#7f1d1d;font-size:12px">${escapeHtml(m.reason || '')}</td>
                    </tr>`).join('')}</tbody>
            </table>
        </div>` : '';

    const summary = wasCommit ? `
        <div class="eaw-sync-summary">
            <span style="color:#166534">Importiert: <strong>${res.inserted||0}</strong></span>
            <span style="color:#6b6152">Geändert: <strong>${res.updated||0}</strong></span>
            <span style="color:#991b1b">Gelöscht: <strong>${res.deleted||0}</strong></span>
            <span style="color:#b45309">🔒 Gesperrt übersprungen: <strong>${res.lockedSkipped||0}</strong></span>
            <span style="color:#64748b">Übersprungen: <strong>${res.skipped||0}</strong></span>
        </div>` : `
        <div class="eaw-sync-summary">
            <span>Total: <strong>${res.countTotal}</strong></span>
            <span style="color:#166534">NEW: <strong>${res.countNew}</strong></span>
            <span style="color:#854d0e">Duplikate: <strong>${res.countDuplicate}</strong></span>
            <span style="color:#991b1b">Unmatched: <strong>${res.countUnmatched}</strong></span>
            <span style="color:#b45309">🔒 Gesperrt: <strong>${res.countLocked||0}</strong></span>
            <span style="color:#64748b">⏭ Übersprungen: <strong>${res.countSkipped||0}</strong></span>
            <span style="color:#64748b">Gelöscht/Invalid: <strong>${res.countSoftDeleted + res.countInvalid}</strong></span>
        </div>`;
    const rows = (res.rows||[]).map(r => {
        const pill = {
            NEW:        '<span class="eaw-pill eaw-pill-new">NEW</span>',
            DUPLICATE:  '<span class="eaw-pill eaw-pill-duplicate">DUPLIKAT</span>',
            UNMATCHED:  '<span class="eaw-pill eaw-pill-unmatched">UNMATCHED</span>',
            SOFT_DELETED:'<span class="eaw-pill eaw-pill-soft">GELÖSCHT</span>',
            INVALID:    '<span class="eaw-pill eaw-pill-invalid">INVALID</span>',
            LOCKED:     '<span class="eaw-pill" style="background:#fef3c7;color:#b45309">🔒 GESPERRT</span>',
            SKIPPED:    '<span class="eaw-pill" style="background:#f1f5f9;color:#475569">⏭ ÜBERSPRUNGEN</span>',
        }[r.status] || r.status;
        const comment = escapeHtml(r.comment || r.reason || '');
        // Bei UNMATCHED mit bekannter easy@work-ID: Ein-Klick-Zuordnung anbieten.
        const assignBtn = (r.status === 'UNMATCHED' && r.eawEmployeeId)
            ? `<button onclick="eawAssignAlias(${r.eawEmployeeId})" style="margin-top:4px;display:inline-block;background:#ece9e2;border:1px solid #d0c8b8;color:#6b7280;border-radius:6px;padding:3px 8px;font-size:12px;cursor:pointer">→ MA zuordnen</button>`
            : '';
        let editFlag = '';
        if (r.isEdited) {
            const origIn  = _eawTime(r.originalTimeIn);
            const origOut = _eawTime(r.originalTimeOut);
            const hasOrig = (origIn && origIn !== '—') || (origOut && origOut !== '—');
            const tip = hasOrig ? `title="Original-Zeit: ${origIn} → ${origOut}"` : '';
            const label = hasOrig
                ? `✎ ${origIn} → ${origOut}`
                : '✎ bearbeitet';
            editFlag = `<span class="eaw-pill" style="background:#fef3c7;color:#854d0e" ${tip}>${escapeHtml(label)}</span>`;
        }
        return `<tr>
            <td>${pill}</td>
            <td>${escapeHtml(r.eawEmployeeName||'?')} <span style="color:#94a3b8">(${escapeHtml(r.eawEmployeeNumber||'-')})</span></td>
            <td>${_eawDate(r.businessDate)}</td>
            <td>${_eawTime(r.timeIn)} → ${_eawTime(r.timeOut)}</td>
            <td style="text-align:right">${r.hours != null ? Number(r.hours).toFixed(2) : ''}</td>
            <td style="text-align:right;color:${r.nightHours > 0 ? '#6b6152' : '#94a3b8'}">${r.nightHours != null ? Number(r.nightHours).toFixed(2) : ''}</td>
            <td>${editFlag}</td>
            <td style="color:#475569">${comment}${assignBtn ? '<br>' + assignBtn : ''}</td>
        </tr>`;
    }).join('');
    out.innerHTML = `
        ${ambigHtml}
        ${missingHtml}
        ${notes}
        ${summary}
        ${wasCommit ? '' : `<table class="eaw-sync-table">
            <thead><tr>
                <th>Status</th><th>MA (Personalnr.)</th><th>Datum</th><th>Von → Bis</th><th style="text-align:right">Std</th><th style="text-align:right">Nacht</th><th>Bearbeitet</th><th>Bemerkung</th>
            </tr></thead>
            <tbody>${rows || '<tr><td colspan="8" style="padding:10px;color:#94a3b8">— keine Einträge —</td></tr>'}</tbody>
        </table>`}`;
}

function _eawDate(iso) {
    if (!iso) return '';
    const s = String(iso).slice(0, 10);
    return s.length === 10 ? `${s.slice(8,10)}.${s.slice(5,7)}.${s.slice(0,4)}` : s;
}

// ─────────────────── Auto-Sync-Protokoll (Admin-Ansicht) ─────────────────
async function eawLogLoad() {
    const el = document.getElementById('eawLogContainer');
    if (!el) return;
    el.innerHTML = `<div style="color:#64748b;font-size:13px;padding:8px">⏳ Lade…</div>`;
    try {
        const r = await fetch('/api/easywork/sync-log?limit=100', { headers: ah(), cache: 'no-store' });
        if (!r.ok) { el.innerHTML = `<div style="color:#b91c1c;font-size:13px">Fehler beim Laden des Protokolls.</div>`; return; }
        eawLogRender(await r.json());
    } catch (e) {
        el.innerHTML = `<div style="color:#b91c1c;font-size:13px">Netzwerkfehler beim Laden.</div>`;
    }
}

function eawLogRender(rows) {
    const el = document.getElementById('eawLogContainer');
    if (!el) return;
    if (!rows || !rows.length) {
        el.innerHTML = `<div style="color:#94a3b8;font-size:13px;padding:8px">— noch keine Läufe protokolliert —</div>`;
        return;
    }
    const badge = (s) => {
        const m = {
            OK:      ['#166534', '#dcfce7', 'OK'],
            BLOCKED: ['#991b1b', '#fee2e2', 'BLOCKIERT'],
            ERROR:   ['#7f1d1d', '#fecaca', 'FEHLER'],
            SKIPPED: ['#64748b', '#f1f5f9', 'ÜBERSPRUNGEN'],
        }[s] || ['#334155', '#e2e8f0', s];
        return `<span style="background:${m[1]};color:${m[0]};font-weight:600;font-size:11px;padding:2px 8px;border-radius:9px;white-space:nowrap">${m[2]}</span>`;
    };
    const fmtDt = (iso) => { try { return new Date(iso).toLocaleString('de-CH', { day:'2-digit', month:'2-digit', year:'numeric', hour:'2-digit', minute:'2-digit' }); } catch(e){ return iso; } };
    const body = rows.map(r => `
        <tr>
            <td style="white-space:nowrap">${fmtDt(r.runAt)}</td>
            <td>${escapeHtml(r.companyProfileName || ('Filiale ' + r.companyProfileId))}</td>
            <td>${badge(r.status)}</td>
            <td style="white-space:nowrap;color:#64748b">${r.periodFrom ? _eawDate(r.periodFrom) + '–' + _eawDate(r.periodTo) : ''} ${r.usedUpdatesFeed ? '<span style="color:#94a3b8" title="Delta-Feed (timepunch_updates)">Δ</span>' : ''}</td>
            <td style="text-align:right;color:#166534">${r.inserted || 0}</td>
            <td style="text-align:right;color:#6b6152">${r.updated || 0}</td>
            <td style="text-align:right;color:#991b1b">${r.deleted || 0}</td>
            <td style="text-align:right;color:#854d0e" title="in gesperrter Periode übersprungen">${r.lockedSkipped || 0}</td>
            <td style="color:#475569;font-size:12px">${escapeHtml(r.message || '')}${r.hasDetail ? ` <button onclick="eawLogDetail(${r.id})" style="margin-left:6px;background:#f1efe9;border:1px solid #c7d2fe;color:#5a5348;font-size:11px;font-weight:600;padding:2px 9px;border-radius:6px;cursor:pointer;white-space:nowrap">🔍 Detail</button>` : ''}</td>
        </tr>`).join('');
    el.innerHTML = `
        <table class="eaw-sync-table">
            <thead><tr>
                <th>Zeitpunkt</th><th>Filiale</th><th>Status</th><th>Fenster</th>
                <th style="text-align:right">+Neu</th><th style="text-align:right">~Änd</th><th style="text-align:right">−Del</th><th style="text-align:right">🔒</th><th>Meldung</th>
            </tr></thead>
            <tbody>${body}</tbody>
        </table>`;
}
// Detail-Drill-Down zu einem Sync-Lauf (Variante A): echte Änderungen (neu /
// Wert-geändert) mit Mitarbeiter, Datum und alt→neu Stunden.
async function eawLogDetail(id) {
    let modal = document.getElementById('eawLogDetailModal');
    if (!modal) {
        modal = document.createElement('div');
        modal.id = 'eawLogDetailModal';
        modal.style.cssText = 'position:fixed;inset:0;background:rgba(15,23,42,.55);z-index:9999;display:flex;align-items:center;justify-content:center;padding:24px';
        modal.innerHTML = `<div style="background:var(--card-bg,#fff);border-radius:12px;max-width:820px;width:100%;max-height:85vh;display:flex;flex-direction:column;box-shadow:0 20px 60px rgba(0,0,0,.3)">
            <div style="display:flex;align-items:center;justify-content:space-between;padding:14px 18px;border-bottom:1px solid #e2e8f0">
                <strong id="eawLogDetailTitle" style="font-size:15px">Sync-Detail</strong>
                <button onclick="document.getElementById('eawLogDetailModal').remove()" style="background:none;border:none;font-size:20px;cursor:pointer;color:#64748b">✕</button>
            </div>
            <div id="eawLogDetailBody" style="overflow:auto;padding:14px 18px"></div>
        </div>`;
        document.body.appendChild(modal);
        modal.addEventListener('click', e => { if (e.target === modal) modal.remove(); });
    }
    const body = modal.querySelector('#eawLogDetailBody');
    body.innerHTML = '<div style="color:#64748b;padding:10px">⏳ Lade…</div>';
    try {
        const r = await fetch(`/api/easywork/sync-log/${id}/detail`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) { body.innerHTML = '<div style="color:#b91c1c;padding:10px">Fehler beim Laden des Details.</div>'; return; }
        const d = await r.json();
        const changes = d.changes || [];
        if (!changes.length) { body.innerHTML = '<div style="color:#94a3b8;padding:10px">Keine echten Änderungen in diesem Lauf (nur identische Neuschreibungen).</div>'; return; }
        const neu = changes.filter(c => c.action === 'neu').length;
        const geae = changes.length - neu;
        const fmt = (v) => (v == null ? '–' : Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 }));
        const actBadge = (a) => a === 'neu'
            ? '<span style="background:#dcfce7;color:#166534;font-size:11px;font-weight:600;padding:1px 7px;border-radius:8px">neu</span>'
            : '<span style="background:#ece9e2;color:#6b6152;font-size:11px;font-weight:600;padding:1px 7px;border-radius:8px">geändert</span>';
        const rows = changes.map(c => `
            <tr>
                <td style="white-space:nowrap">${escapeHtml(c.name)} <span style="color:#94a3b8">(${escapeHtml(c.number || '-')})</span></td>
                <td style="white-space:nowrap">${c.date ? _eawDate(c.date) : ''}</td>
                <td>${actBadge(c.action)}</td>
                <td style="text-align:right;white-space:nowrap">${c.action === 'neu' ? fmt(c.newTotal) : `${fmt(c.oldTotal)} → <strong>${fmt(c.newTotal)}</strong>`}</td>
                <td style="text-align:right;white-space:nowrap;color:#7c3aed">${c.action === 'neu' ? fmt(c.newNight) : `${fmt(c.oldNight)} → ${fmt(c.newNight)}`}</td>
            </tr>`).join('');
        modal.querySelector('#eawLogDetailTitle').textContent =
            `Sync-Detail — ${neu} neu, ${geae} geändert${d.capped ? ' (gekappt auf 1000)' : ''}`;
        body.innerHTML = `
            <div style="font-size:12px;color:#64748b;margin-bottom:8px">Nur echte Änderungen (identische Neuschreibungen ausgeblendet). Stunden: Total bzw. <span style="color:#7c3aed">Nacht</span>, alt → neu.</div>
            <table class="eaw-sync-table" style="font-size:12.5px">
                <thead><tr><th>Mitarbeiter</th><th>Datum</th><th>Aktion</th><th style="text-align:right">Total Std</th><th style="text-align:right">Nacht Std</th></tr></thead>
                <tbody>${rows}</tbody>
            </table>`;
    } catch (e) {
        body.innerHTML = '<div style="color:#b91c1c;padding:10px">Netzwerkfehler.</div>';
    }
}

function _eawTime(iso) {
    if (!iso) return '—';
    const s = String(iso);
    // "2026-06-17T08:30:00"  → "08:30"
    const m = s.match(/T(\d{2}:\d{2})/);
    return m ? m[1] : s;
}

// ───────── Alias-Zuordnung: alte easy@work-ID einem MA zuweisen ──────────
// Wird aus der UNMATCHED-Zeile heraus aufgerufen (Walter 18.06.2026). Speichert
// die alte easy@work-ID am gewählten MA — danach matchen seine Stempel automatisch.
let _eawAllEmps   = null;
let _eawAliasEawId = 0;

// Nachschlagen eines (evtl. gelöschten) easy@work-MA per ID über die API
// (Walter-Vorgabe 20.06.2026). Findet die API ihn + passt die Nummer zu einem
// Cowork-MA → direkt vorausgewählt zuordnen; sonst Hinweis.
async function eawLookupEmployee(eawId) {
    const sel = document.getElementById('eawSyncBranchSel');
    const cp = sel ? parseInt(sel.value, 10) : 0;
    if (!cp) { alert('Bitte zuerst Filiale wählen.'); return; }
    try {
        const r = await fetch(`/api/easywork/employee-lookup?companyProfileId=${cp}&eawId=${eawId}`, { headers: ah(), cache: 'no-store' });
        const d = await r.json().catch(() => null);
        if (!r.ok || !d) { alert('Nachschlagen fehlgeschlagen.'); return; }
        if (!d.found) {
            alert(`easy@work gibt zur ID #${eawId} keinen Datensatz zurück (endgültig gelöscht/archiviert).\n\nBitte „⏭ Überspringen" oder den MA in Cowork manuell zuordnen.`);
            return;
        }
        const info = `easy@work #${eawId}: ${d.name || '(ohne Name)'}${d.number ? ' · Nr. ' + d.number : ''}`;
        if (d.coworkEmployeeId) {
            if (confirm(`${info}\n\nGefundener Cowork-Mitarbeiter: ${d.coworkName}\n\nJetzt zuordnen?`)) {
                eawAssignAlias(eawId, d.coworkEmployeeId);
            }
        } else {
            alert(`${info}\n\nKein Cowork-Mitarbeiter mit dieser Nummer gefunden.\nBitte den MA in Cowork anlegen und dann zuordnen — oder überspringen.`);
        }
    } catch (e) { alert('Netzwerkfehler beim Nachschlagen.'); }
}

async function eawAssignAlias(eawEmployeeId, preselectCoworkId) {
    if (!_eawAllEmps) {
        try {
            const res = await fetch('/api/employees', { headers: ah(), cache: 'no-store' });
            if (!res.ok) { alert('Mitarbeiterliste konnte nicht geladen werden.'); return; }
            _eawAllEmps = await res.json();
        } catch (e) { alert('Netzwerkfehler beim Laden der Mitarbeiterliste.'); return; }
    }
    // Nach Vorname sortieren (Walter-Konvention), Tie-Break Nachname.
    const emps = (_eawAllEmps || []).slice().sort((a, b) =>
        (a.firstName||'').localeCompare(b.firstName||'', 'de') ||
        (a.lastName||'').localeCompare(b.lastName||'', 'de'));

    _eawAliasEawId = eawEmployeeId;
    const opts = emps.map(e =>
        `<option value="${e.id}">${escapeHtml(((e.firstName||'')+' '+(e.lastName||'')).trim())} (${escapeHtml(e.employeeNumber||'-')})</option>`
    ).join('');

    let overlay = document.getElementById('eawAliasOverlay');
    if (!overlay) {
        overlay = document.createElement('div');
        overlay.id = 'eawAliasOverlay';
        overlay.style.cssText = 'position:fixed;inset:0;background:rgba(15,23,42,0.45);z-index:9999;display:flex;align-items:center;justify-content:center';
        document.body.appendChild(overlay);
    }
    overlay.innerHTML = `
        <div style="background:#fff;border-radius:10px;padding:20px;max-width:460px;width:90%;box-shadow:0 10px 40px rgba(0,0,0,0.25)">
            <h3 style="margin:0 0 6px;font-size:16px">easy@work-ID #${eawEmployeeId} zuordnen</h3>
            <p style="color:#64748b;font-size:13px;margin:0 0 12px">Wähle den Mitarbeiter, zu dem diese alte easy@work-ID gehört. Ab dann werden seine Stempel mit dieser ID automatisch erkannt.</p>
            <input id="eawAliasSearch" placeholder="Suchen (Name oder Nummer)…" oninput="eawAliasFilter()" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:6px;margin-bottom:8px;font-size:14px;box-sizing:border-box">
            <select id="eawAliasSelect" size="8" style="width:100%;border:1px solid #cbd5e1;border-radius:6px;font-size:14px;padding:4px;box-sizing:border-box">${opts}</select>
            <div style="display:flex;gap:8px;justify-content:flex-end;margin-top:14px">
                <button onclick="eawAliasClose()" style="background:#f1f5f9;border:1px solid #cbd5e1;border-radius:6px;padding:7px 14px;cursor:pointer">Abbrechen</button>
                <button onclick="eawAliasSave()" style="background:#6b7280;border:1px solid #6b7280;color:#fff;border-radius:6px;padding:7px 14px;cursor:pointer">Zuordnen &amp; speichern</button>
            </div>
        </div>`;
    overlay.style.display = 'flex';
    // Vorauswahl (aus „Nachschlagen", wenn die API-Nummer zu einem Cowork-MA passt).
    if (preselectCoworkId) {
        const selEl = document.getElementById('eawAliasSelect');
        if (selEl) { selEl.value = String(preselectCoworkId); selEl.focus(); }
    } else {
        const search = document.getElementById('eawAliasSearch');
        if (search) search.focus();
    }
}

function eawAliasFilter() {
    const q = (document.getElementById('eawAliasSearch').value || '').toLowerCase();
    const sel = document.getElementById('eawAliasSelect');
    if (!sel) return;
    Array.from(sel.options).forEach(o => {
        o.style.display = o.text.toLowerCase().includes(q) ? '' : 'none';
    });
}

function eawAliasClose() {
    const o = document.getElementById('eawAliasOverlay');
    if (o) o.style.display = 'none';
}

async function eawAliasSave() {
    const sel = document.getElementById('eawAliasSelect');
    const empId = sel && sel.value ? parseInt(sel.value, 10) : 0;
    if (!empId) { alert('Bitte zuerst einen Mitarbeiter auswählen.'); return; }
    try {
        const r = await fetch('/api/easywork/aliases', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ easyAtWorkId: _eawAliasEawId, coworkEmployeeId: empId })
        });
        if (!r.ok) {
            let msg = 'Fehler beim Speichern.';
            try { const b = await r.json(); msg = b.message || msg; } catch (e) {}
            alert(msg); return;
        }
    } catch (e) { alert('Netzwerkfehler beim Speichern.'); return; }
    eawAliasClose();
    // Vorschau neu laden → die zugeordnete Zeile sollte jetzt matchen.
    eawSyncPreview();
}

// ═════════════════════ Mitarbeiter-Sync (Phase 3.1) ══════════════════════

let _eawEmpSyncLast = null;

function _eawEmpSyncInit() {
    const sel = document.getElementById('eawEmpSyncBranchSel');
    if (!sel) return;
    const mapped = _eawMappings || [];
    sel.innerHTML = mapped.length
        ? '<option value="">— wählen —</option>' + mapped.map(m =>
            `<option value="${m.companyProfileId}">${escapeHtml(m.companyProfileName||'')} (${escapeHtml(m.restaurantCode||'')}) → ${m.easyAtWorkCustomerId}</option>`
          ).join('')
        : '<option value="">— keine Filiale gemappt —</option>';

    // Global gewählte Filiale (Sidebar-Selektor) vorauswählen, sofern gemappt —
    // gleiche Sub-Page-Konvention wie eawSyncInit (Walter-Vorgabe 13.05.2026).
    if (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId
        && mapped.some(m => Number(m.companyProfileId) === Number(fixedCompanyProfileId))) {
        sel.value = String(fixedCompanyProfileId);
    }
}

// Hinweis (Walter-Vorgabe 08.07.2026): der Einstieg „＋ Neuer MA aus easy@work"
// aus der Mitarbeiter-Verwaltung lebt als eigenes Modal in employees.js
// (empImportFromEasy) und nutzt den eingeschränkten Endpoint
// /api/easywork/neuzugang/* — NICHT diese Admin-Seite. Der Massen-Sync hier
// bleibt unverändert (admin/superuser).

async function eawEmpChooseEmployees() {
    const sel = document.getElementById('eawEmpSyncBranchSel');
    const out = document.getElementById('eawEmpChooseResult');
    if (!out) return;
    const cpId = parseInt(sel?.value || '0', 10);
    if (!cpId) {
        out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">Bitte zuerst Filiale wählen</div></div>`;
        return;
    }
    out.innerHTML = `<div style="color:#64748b;font-size:13px;padding:8px;display:flex;align-items:center;gap:8px">
        <span class="import-spinner" style="width:14px;height:14px"></span>
        <span>Lade aktive easy@work-Mitarbeiter…</span></div>`;
    try {
        const r = await fetch(`/api/easywork/employees/active?companyProfileId=${cpId}`, { headers: ah(), cache: 'no-store' });
        const body = await r.json().catch(() => null);
        if (!r.ok || !body) {
            out.innerHTML = `<div class="eaw-result eaw-result-err">
                <div class="eaw-result-title">Fehler ${r.status}</div>
                <div class="eaw-result-msg">${escapeHtml(body?.message || body?.error || 'Aktive MA konnten nicht geladen werden.')}</div>
            </div>`;
            return;
        }
        const rows = (body.employees || []).slice().sort((a, b) =>
            (a.firstName || '').localeCompare(b.firstName || '', 'de') ||
            (a.lastName || '').localeCompare(b.lastName || '', 'de'));
        const tableRows = rows.map(e => `
            <tr>
                <td style="padding:7px 10px;font-weight:600;color:#0f172a">${escapeHtml(((e.firstName||'') + ' ' + (e.lastName||'')).trim() || e.name || '–')}</td>
                <td style="padding:7px 10px;color:#64748b;font-family:monospace">${escapeHtml(e.number || '-')}</td>
                <td style="padding:7px 10px;color:#64748b">${escapeHtml(e.email || '–')}</td>
                <td style="padding:7px 10px;color:#64748b">${_eawDate(e.from)}</td>
                <td style="padding:7px 10px;text-align:right">
                    <button onclick="document.getElementById('eawDumpNumber').value='${escapeHtml(e.number || '')}';eawEmpDump()" style="background:#f1efe9;border:1px solid #c7d2fe;color:#5a5348;border-radius:6px;padding:3px 8px;font-size:12px;cursor:pointer">🔬 Felder</button>
                </td>
            </tr>`).join('');
        out.innerHTML = `
            <div style="border:1px solid #ece9e2;background:#f6f3ee;border-radius:10px;padding:12px 14px;margin-bottom:10px">
                <div style="font-weight:700;color:#6b7280;margin-bottom:3px">Aktive easy@work-Mitarbeiter (${rows.length})</div>
                <div style="font-size:12px;color:#64748b">Sortiert nach Vorname. Diese Liste schreibt noch nichts.</div>
            </div>
            <div style="max-height:420px;overflow:auto;border:1px solid #e2e8f0;border-radius:10px;background:#fff">
                <table class="eaw-sync-table" style="margin:0">
                    <thead><tr><th>Name</th><th>Nr.</th><th>E-Mail</th><th>Eintritt</th><th></th></tr></thead>
                    <tbody>${tableRows || '<tr><td colspan="5" style="padding:12px;color:#94a3b8">— keine aktiven MA —</td></tr>'}</tbody>
                </table>
            </div>`;
    } catch (e) {
        out.innerHTML = `<div class="eaw-result eaw-result-err">
            <div class="eaw-result-title">Netzwerkfehler</div>
            <div class="eaw-result-msg">${escapeHtml(String(e))}</div>
        </div>`;
    }
}

async function eawEmpSyncPreview() {
    await _eawEmpSyncRun(false, null);
}

// API-Dump: alle erreichbaren easy@work-Roh-Felder eines MA anzeigen (Diagnose).
async function eawEmpDump() {
    const out = document.getElementById('eawDumpResult');
    const number = (document.getElementById('eawDumpNumber')?.value || '').trim();
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : '';
    if (!number) { if (out) out.textContent = 'Bitte eine Personalnummer eingeben.'; return; }
    if (!cpId)   { if (out) out.textContent = 'Bitte zuerst oben eine Filiale wählen.'; return; }
    if (out) out.textContent = 'Lade…';
    try {
        const r = await fetch(`/api/easywork/debug/employee-dump?companyProfileId=${cpId}&number=${encodeURIComponent(number)}`, { headers: ah() });
        const j = await r.json();
        if (!r.ok) { out.textContent = 'Fehler: ' + (j?.message || j?.error || ('HTTP ' + r.status)); return; }
        out.textContent = JSON.stringify(j, null, 2);
    } catch (e) {
        if (out) out.textContent = 'Verbindungsfehler: ' + e.message;
    }
}

// Verfügbarkeits-Dump (Walter 09.07.2026): availabilities-Liste + pro
// Verfügbarkeit die /days — Endpunkte von easy@work-Support bestätigt.
// Nutzt dasselbe Personalnummer-Feld + Ausgabefenster wie der Felder-Dump.
async function eawAvailabilityDump() {
    const out = document.getElementById('eawDumpResult');
    const number = (document.getElementById('eawDumpNumber')?.value || '').trim();
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : '';
    if (!number) { if (out) out.textContent = 'Bitte eine Personalnummer eingeben.'; return; }
    if (!cpId)   { if (out) out.textContent = 'Bitte zuerst oben eine Filiale wählen.'; return; }
    if (out) out.textContent = 'Lade Verfügbarkeiten…';
    try {
        const r = await fetch(`/api/easywork/debug/availability-dump?companyProfileId=${cpId}&number=${encodeURIComponent(number)}`, { headers: ah() });
        const j = await r.json();
        if (!r.ok) { out.textContent = 'Fehler: ' + (j?.message || j?.error || ('HTTP ' + r.status)); return; }
        out.textContent = JSON.stringify(j, null, 2);
    } catch (e) {
        if (out) out.textContent = 'Verbindungsfehler: ' + e.message;
    }
}

// Absenzen-Probe (Walter 09.08.2026): easy@work hat keine öffentliche API-Doku —
// testet read-only die plausiblen Absenz-Endpunkte (absences / absence_types /
// vacations / leaves …) auf Customer- und MA-Ebene durch. Personalnummer ist
// optional (ohne Nummer nur Customer-Ebene). Status 200 = Endpunkt existiert.
async function eawAbsenceProbe() {
    const out = document.getElementById('eawDumpResult');
    const number = (document.getElementById('eawDumpNumber')?.value || '').trim();
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : '';
    if (!cpId) { if (out) out.textContent = 'Bitte zuerst oben eine Filiale wählen.'; return; }
    if (out) out.textContent = 'Absenz-Endpunkte werden durchgetestet…';
    try {
        const r = await fetch(`/api/easywork/debug/absence-probe?companyProfileId=${cpId}${number ? '&number=' + encodeURIComponent(number) : ''}`, { headers: ah() });
        const j = await r.json();
        if (!r.ok) { out.textContent = 'Fehler: ' + (j?.message || j?.error || ('HTTP ' + r.status)); return; }
        out.textContent = JSON.stringify(j, null, 2);
    } catch (e) {
        if (out) out.textContent = 'Verbindungsfehler: ' + e.message;
    }
}

// API-Dump nach easy@work-ID: holt die Roh-Felder direkt für eine ID. Der
// passende Customer wird serverseitig über alle gemappten Filialen gesucht —
// so sieht Walter, welche Personalnummer easy@work für diese ID liefert.
async function eawEmpDumpById() {
    const out = document.getElementById('eawDumpByIdResult');
    const id  = (document.getElementById('eawDumpId')?.value || '').trim();
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : '';
    if (!id) { if (out) out.textContent = 'Bitte eine easy@work-ID eingeben.'; return; }
    if (out) out.textContent = 'Lade…';
    try {
        const q = `/api/easywork/debug/employee-dump-by-id?easyAtWorkId=${encodeURIComponent(id)}${cpId ? '&companyProfileId=' + cpId : ''}`;
        const r = await fetch(q, { headers: ah() });
        const j = await r.json();
        if (!r.ok) { out.textContent = 'Fehler: ' + (j?.message || j?.error || ('HTTP ' + r.status)); return; }
        out.textContent = JSON.stringify(j, null, 2);
    } catch (e) {
        if (out) out.textContent = 'Verbindungsfehler: ' + e.message;
    }
}

// Probezeiten nachführen (Walter 29.06.2026 / 02.08.2026): nur MA mit Eintritt
// in den letzten 4 Monaten — fehlende anlegen + an 1. Stempelzeit ≥ Eintritt.
async function eawAnchorProbation() {
    const out = document.getElementById('eawSyncResult');
    if (out) out.innerHTML = `<div style="color:#64748b;font-size:13px;padding:8px">⏳ Führe Probezeiten nach (Eintritt ≤ 4 Monate)…</div>`;
    try {
        const r = await fetch('/api/easywork/probation/anchor', { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) {
            if (out) out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">Fehler</div><div class="eaw-result-msg">${escapeHtml(j.message || j.error || ('HTTP ' + r.status))}</div></div>`;
            return;
        }
        const notes = j.notes || [];
        const n = j.processed ?? j.anchored ?? notes.length;
        if (out) out.innerHTML = `
            <div style="color:#166534;font-size:13px;padding:10px;background:#dcfce7;border:1px solid #bbf7d0;border-radius:8px">
                ⚓ ${n} Probezeit(en) nachgeführt — nur MA mit Eintritt in den letzten 4 Monaten.
            </div>
            ${notes.length ? `<div style="margin-top:8px;color:#475569;font-size:12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:10px;max-height:280px;overflow:auto">${notes.map(n => '• ' + escapeHtml(n)).join('<br>')}</div>` : '<div style="margin-top:8px;color:#64748b;font-size:12px">Nichts zu tun — im 4-Monats-Fenster keine fehlenden/provisorischen Probezeiten (oder Stempel/Filial-Dauer fehlen).</div>'}`;
    } catch (e) {
        if (out) out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">Netzwerkfehler</div><div class="eaw-result-msg">${escapeHtml(String(e))}</div></div>`;
    }
}

async function eawEmpSyncCommit() {
    const checked = Array.from(document.querySelectorAll('.eaw-emp-pick:checked'))
        .map(cb => cb.getAttribute('data-number'))
        .filter(Boolean);
    // Auch wenn keine Checkbox angehakt ist (= alle UNCHANGED), läuft der
    // Commit weiter — der Backfill für `easyatwork_employee_id` läuft im
    // Commit-Pfad unabhängig von der Auswahl.
    const last = _eawEmpSyncLast || {};
    const unchangedCount = Math.max(0, (last.countTotal || 0) - (last.countNew || 0) - (last.countUpdate || 0) - (last.countConflict || 0));
    const msg = checked.length
        ? `${checked.length} MA jetzt importieren/aktualisieren?\nZusätzlich werden für ${unchangedCount} UNCHANGED-MA die easy@work-IDs nachgetragen.`
        : `Keine MA ausgewählt → kein Insert/Update.\nFür ${unchangedCount} UNCHANGED-MA werden trotzdem die easy@work-IDs nachgetragen. Fortfahren?`;
    if (!confirm(msg)) return;
    await _eawEmpSyncRun(true, checked.length ? checked : null);
}

async function eawEmpRepairIds() {
    const out = document.getElementById('eawEmpSyncResult');
    if (!confirm('easy@work-IDs für alle bestehenden Mitarbeiter korrigieren?\n\nDieser Lauf ersetzt alte gespeicherte user_id-Werte durch die echte easy@work employee.id.')) return;
    if (out) out.innerHTML = `<div style="color:#64748b;font-size:13px;padding:8px;display:flex;align-items:center;gap:8px">
        <span class="import-spinner" style="width:14px;height:14px"></span>
        <span>Korrigiere easy@work-IDs…</span></div>`;
    try {
        const r = await fetch('/api/easywork/sync/employees/repair-ids', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' }
        });
        const body = await r.json().catch(() => ({}));
        if (!r.ok) {
            if (out) out.innerHTML = `<div class="eaw-result eaw-result-err">
                <div class="eaw-result-title">Korrektur fehlgeschlagen</div>
                <div class="eaw-result-msg">${escapeHtml(body.message || body.error || ('HTTP ' + r.status))}</div>
            </div>`;
            return;
        }
        const rows = (body.rows || []).slice(0, 80).map(x => `<tr>
            <td>${escapeHtml(x.name || '')}</td>
            <td>${escapeHtml(x.employeeNumber || '')}</td>
            <td style="font-family:monospace">${escapeHtml(String(x.oldEasyAtWorkId ?? ''))}</td>
            <td style="font-family:monospace">${escapeHtml(String(x.newEasyAtWorkId ?? ''))}</td>
            <td style="font-family:monospace">${escapeHtml(String(x.eawUserId ?? ''))}</td>
        </tr>`).join('');
        const cRows = body.conflictRows || [];
        const conflictTxt = body.conflicts ? (() => {
            const items = cRows.map(c => {
                if (c.matches) {
                    // easy@work-Zeile passt auf MEHRERE Cowork-MA
                    const cands = (c.matches || []).map(m =>
                        `<div style="padding-left:14px;color:#78350f">– ${escapeHtml(((m.firstName||'')+' '+(m.lastName||'')).trim()||'(ohne Name)')} · Nr. ${escapeHtml(m.employeeNumber||'–')} · Cowork-#${m.id} · gespeicherte eaw-ID ${m.easyAtWorkEmployeeId ?? '–'}</div>`
                    ).join('');
                    return `<div style="padding:6px 0;border-top:1px solid #fde68a">
                        <strong>easy@work #${c.eawEmployeeId}</strong> (user_id ${c.eawUserId ?? '–'}, Nr. ${escapeHtml(c.number||'–')}) passt auf <strong>mehrere</strong> Cowork-MA:${cands}
                        <div style="padding-left:14px;color:#92400e;font-size:11px;margin-top:2px">→ In „🧹 Duplikate bereinigen" zusammenführen, dann wird die ID eindeutig.</div></div>`;
                }
                if (c.error) {
                    return `<div style="padding:6px 0;border-top:1px solid #fde68a">Filiale/Customer ${c.customerId}: ${escapeHtml(c.error)}</div>`;
                }
                // Ein Cowork-MA soll in EINEM Lauf zwei verschiedene easy@work-IDs bekommen
                return `<div style="padding:6px 0;border-top:1px solid #fde68a">
                    <strong>Cowork-MA #${c.employeeId}</strong> (Nr. ${escapeHtml(c.employeeNumber||'–')}) — hat bereits eaw-ID ${c.current ?? '–'}, easy@work #${c.proposed} beansprucht denselben MA.
                    <div style="padding-left:14px;color:#92400e;font-size:11px;margin-top:2px">→ Zwei easy@work-Datensätze zeigen auf dieselbe Person. In „🧹 Duplikate bereinigen" prüfen/zusammenführen.</div></div>`;
            }).join('');
            return `<div style="font-size:12px;color:#92400e;margin-top:6px">
                ${body.conflicts} Konflikt(e) übersprungen:
                <div style="margin-top:4px;background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:8px 10px">${items}</div>
            </div>`;
        })() : '';
        if (out) out.innerHTML = `<div class="eaw-result eaw-result-ok">
            <div class="eaw-result-title">easy@work-IDs korrigiert</div>
            <div class="eaw-result-msg">
                ${body.repaired || 0} MA korrigiert · ${body.scannedCowork || 0} Cowork-MA · ${body.scannedEasyAtWork || 0} easy@work-Zeilen geprüft.
                ${conflictTxt}
            </div>
            ${rows ? `<table class="eaw-result-table" style="margin-top:10px">
                <thead><tr><th>Name</th><th>Nr.</th><th>alt</th><th>neu employee.id</th><th>user_id</th></tr></thead>
                <tbody>${rows}</tbody>
            </table>` : '<div class="eaw-empty">Keine ID-Korrekturen nötig.</div>'}
        </div>`;
    } catch (e) {
        if (out) out.innerHTML = `<div class="eaw-result eaw-result-err">
            <div class="eaw-result-title">Netzwerkfehler</div>
            <div class="eaw-result-msg">${escapeHtml(String(e))}</div>
        </div>`;
    }
}

// ═══════════════ Einmaliger Tief-Import alle Filialen (ab 2021) ═══════════════
// Walter-Vorgabe 21.06.2026: holt für ALLE Filialen die inaktiven MA zurück bis
// 1.1.2021, Pre-Mirus-Austritte (< 1.1.2025) bekommen `alt`-Suffix. Läuft ohne
// Vorschau direkt durch (Upsert). Ruft /api/easywork/sync/employees/initial-import.
let _eawInitRunning = false;

async function eawInitialImport() {
    if (_eawInitRunning) { alert('Der Tief-Import läuft bereits.'); return; }
    const out   = document.getElementById('eawInitImportResult');
    const since = document.getElementById('eawInitImportSince')?.value || '2021-01-01';
    const skipDetails = document.getElementById('eawInitSkipDetails')?.checked !== false;
    const fmt   = s => `${s.slice(8,10)}.${s.slice(5,7)}.${s.slice(0,4)}`;
    if (!confirm(`Tief-Import für ALLE Filialen ab ${fmt(since)} starten?\n\n` +
                 `• Inaktive MA werden mitgeholt.\n` +
                 `• Austritte vor dem 1.1.2025 bekommen Suffix "alt" an der Personalnummer.\n` +
                 `• Bestehende MA werden nicht doppelt angelegt (Upsert).\n\n` +
                 `Das kann je nach Datenmenge ein paar Minuten dauern.`)) return;

    // 1) Gemappte Filialen holen.
    let branches = [];
    try {
        const br = await fetch('/api/easywork/mapped-branches', { headers: ah() });
        branches = await br.json();
        if (!br.ok || !Array.isArray(branches) || !branches.length) {
            out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">Keine gemappten Filialen gefunden</div></div>`;
            return;
        }
    } catch (e) {
        out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">Netzwerkfehler</div><div class="eaw-result-msg">${escapeHtml(String(e))}</div></div>`;
        return;
    }

    _eawInitRunning = true;
    const agg = {};   // id → {name, inserted, updated, status, error}
    branches.forEach(b => agg[b.id] = { name: b.name, inserted:0, updated:0, status:'wartet', error:null });
    let done = 0, grandInserted = 0, grandUpdated = 0;

    const render = (curLabel, finished) => {
        const rows = branches.map(b => {
            const a = agg[b.id];
            const badge = a.status === 'läuft'  ? '<span style="color:#b45309">⏳ läuft…</span>'
                        : a.status === 'fertig' ? '<span style="color:#166534">✓ fertig</span>'
                        : a.status === 'fehler' ? `<span style="color:#991b1b">✗ ${escapeHtml(a.error||'Fehler')}</span>`
                        : '<span style="color:#94a3b8">– wartet</span>';
            return `<tr>
                <td style="padding:3px 10px">${escapeHtml(a.name)}</td>
                <td style="padding:3px 10px;text-align:right;color:#166534">${a.inserted}</td>
                <td style="padding:3px 10px;text-align:right;color:#6b6152">${a.updated}</td>
                <td style="padding:3px 10px">${badge}</td>
            </tr>`;
        }).join('');
        const banner = finished
            ? `<div style="color:#166534;font-size:13px;padding:10px;background:#dcfce7;border:1px solid #bbf7d0;border-radius:8px;margin-bottom:10px">
                   ✓ Tief-Import abgeschlossen — <strong>${grandInserted}</strong> angelegt, <strong>${grandUpdated}</strong> aktualisiert über ${branches.length} Filiale(n).
               </div>`
            : `<div style="font-size:13px;padding:10px;background:#fef9c3;border:1px solid #fde68a;border-radius:8px;margin-bottom:10px">
                   ⏳ ${escapeHtml(curLabel)} — Filiale ${done}/${branches.length} · bisher ${grandInserted} angelegt
               </div>`;
        out.innerHTML = banner + `<table style="font-size:12px;border-collapse:collapse;width:100%">
            <thead><tr style="color:#64748b;text-align:left">
                <th style="padding:3px 10px">Filiale</th>
                <th style="padding:3px 10px;text-align:right">Angelegt</th>
                <th style="padding:3px 10px;text-align:right">Aktualisiert</th>
                <th style="padding:3px 10px">Status</th>
            </tr></thead><tbody>${rows}</tbody></table>`;
    };
    render('Start', false);

    // 2) Sequenziell pro Filiale, Live-Status.
    try {
        for (const b of branches) {
            agg[b.id].status = 'läuft';
            render(`Importiere ${b.name}`, false);
            try {
                const r = await fetch('/api/easywork/sync/employees/initial-import-branch', {
                    method: 'POST',
                    headers: { ...ah(), 'Content-Type': 'application/json' },
                    body: JSON.stringify({ companyProfileId: b.id, since, skipDetailCalls: skipDetails })
                });
                const body = await r.json();
                if (!r.ok) {
                    agg[b.id].status = 'fehler';
                    agg[b.id].error  = body.message || ('HTTP ' + r.status);
                } else {
                    agg[b.id].inserted = body.inserted || 0;
                    agg[b.id].updated  = body.updated  || 0;
                    agg[b.id].status   = 'fertig';
                    grandInserted += body.inserted || 0;
                    grandUpdated  += body.updated  || 0;
                }
            } catch (e) {
                agg[b.id].status = 'fehler';
                agg[b.id].error  = String(e);
            }
            done++;
            render(`Importiere ${b.name}`, false);
        }
        render('Fertig', true);
    } finally {
        _eawInitRunning = false;
    }
}

// ═══════ Einmaliger historischer Stempelzeiten-Batch (2021–2024) ═══════
// Walter-Vorgabe 21.06.2026: importiert pro Filiale alle Stempel in 90-Tage-
// Fenstern, ohne Vorschau, fehlerhafte fallen lassen. Der Loop läuft im
// FRONTEND (Filiale × Fenster), je ein kurzer Commit-Request — so gibt es kein
// Gateway-Timeout und Live-Fortschritt. Idempotent: mehrfaches Laufen
// verdoppelt nichts (Dedupe über die easy@work-Timepunch-ID).
let _eawBatchRunning = false;

function _eawIso(d) {                      // Date → 'YYYY-MM-DD' in LOKALzeit (kein UTC-Shift)
    const y = d.getFullYear(), m = String(d.getMonth()+1).padStart(2,'0'), da = String(d.getDate()).padStart(2,'0');
    return `${y}-${m}-${da}`;
}
function _eawAddDays(iso, n) {             // ISO 'YYYY-MM-DD' + n Tage → ISO
    const d = new Date(iso + 'T00:00:00');
    d.setDate(d.getDate() + n);
    return _eawIso(d);
}
// Fenster-Ende = Monatsende des Monats (Startmonat + 2) → 3 Kalendermonate,
// an Monatsgrenze ausgerichtet und IMMER ≤ 92 Tage (easy@work/Backend-Limit).
// Bei start=1.1. → 31.3.; 1.4. → 30.6.; usw. Walter-Vorgabe 21.06.2026.
// (Die frühere „Start + 90 Tage → Monatsende" erzeugte bis zu 120-Tage-Fenster,
//  die der 92-Tage-Check im Backend ablehnte → 0 Stempel.)
function _eawWindowEnd(iso) {
    const d = new Date(iso + 'T00:00:00');
    return _eawIso(new Date(d.getFullYear(), d.getMonth() + 3, 0));   // Tag 0 = letzter Tag von (Monat+2)
}

// Beim Ändern von „Von" das „Bis"-Feld automatisch auf das Ende des dritten
// Monats ab Startmonat setzen (≤ 92 Tage; Walter-Vorgabe 21.06.2026). Leer
// lassen löscht nichts.
function eawBatchFromChanged(val) {
    if (!val) return;
    const toEl = document.getElementById('eawBatchTo');
    if (toEl) toEl.value = _eawWindowEnd(val);
}

async function eawBatchHistorical() {
    if (_eawBatchRunning) { alert('Der Tief-Import läuft bereits.'); return; }
    const out  = document.getElementById('eawBatchResult');
    const from = document.getElementById('eawBatchFrom')?.value || '2021-01-01';
    const to   = document.getElementById('eawBatchTo')?.value   || '2024-12-31';
    const fmt  = s => `${s.slice(8,10)}.${s.slice(5,7)}.${s.slice(0,4)}`;
    if (from > to) { alert('„Von" muss vor „Bis" liegen.'); return; }
    if (!confirm(`Stempelzeiten-Tief-Import für ALLE Filialen von ${fmt(from)} bis ${fmt(to)} starten?\n\n` +
                 `• In 3-Monats-Schritten bis Monatsende (max. 92 Tage), ohne Vorschau.\n` +
                 `• Fehlerhafte Stempel werden fallen gelassen, nicht zuordenbare MA übersprungen.\n` +
                 `• Abgeschlossene Lohnperioden bleiben gesperrt.\n` +
                 `• Mehrfaches Laufen verdoppelt nichts.\n\n` +
                 `Das kann einige Minuten dauern — Fenster bitte offen lassen.`)) return;

    // 1) Gemappte Filialen holen.
    let branches = [];
    try {
        const br = await fetch('/api/easywork/mapped-branches', { headers: ah() });
        branches = await br.json();
        if (!br.ok || !Array.isArray(branches) || !branches.length) {
            out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">Keine gemappten Filialen gefunden</div></div>`;
            return;
        }
    } catch (e) {
        out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">Netzwerkfehler</div><div class="eaw-result-msg">${escapeHtml(String(e))}</div></div>`;
        return;
    }

    // 2) Fenster bilden: Bis = Ende des dritten Monats ab Startmonat (max. 92 Tage). Walter 21.06.2026.
    const windows = [];
    let ws = from;
    while (ws <= to) {
        let we = _eawWindowEnd(ws);
        if (we > to) we = to;
        windows.push([ws, we]);
        ws = _eawAddDays(we, 1);
    }
    const totalSteps = branches.length * windows.length;

    _eawBatchRunning = true;
    const agg = {};   // companyProfileId → {name, inserted, invalid, locked, missing, errors:[]}
    branches.forEach(b => agg[b.id] = { name: b.name, inserted:0, invalid:0, locked:0, missing:0, errors:[] });
    let step = 0, grandInserted = 0, grandInvalid = 0, grandLocked = 0, grandMissing = 0;

    const renderProgress = (curLabel) => {
        const rows = branches.map(b => {
            const a = agg[b.id];
            const err = a.errors.length ? `<div style="color:#991b1b;font-size:11px">${a.errors.map(escapeHtml).join('<br>')}</div>` : '';
            return `<tr>
                <td style="padding:3px 10px">${escapeHtml(a.name)}</td>
                <td style="padding:3px 10px;text-align:right;color:#166534">${a.inserted}</td>
                <td style="padding:3px 10px;text-align:right;color:#b45309">${a.invalid}</td>
                <td style="padding:3px 10px;text-align:right;color:#64748b">${a.locked}</td>
                <td style="padding:3px 10px;text-align:right;color:#64748b">${a.missing}</td>
                <td style="padding:3px 10px">${err}</td>
            </tr>`;
        }).join('');
        out.innerHTML = `
            <div style="font-size:13px;padding:10px;background:#fef9c3;border:1px solid #fde68a;border-radius:8px;margin-bottom:10px">
                ⏳ ${curLabel} — Schritt ${step}/${totalSteps} · bisher ${grandInserted} Stempel importiert
            </div>
            <table style="font-size:12px;border-collapse:collapse;width:100%">
                <thead><tr style="color:#64748b;text-align:left">
                    <th style="padding:3px 10px">Filiale</th>
                    <th style="padding:3px 10px;text-align:right">Importiert</th>
                    <th style="padding:3px 10px;text-align:right">Übersprungen</th>
                    <th style="padding:3px 10px;text-align:right">Gesperrt</th>
                    <th style="padding:3px 10px;text-align:right">Nicht&nbsp;zugeordnet</th>
                    <th></th>
                </tr></thead><tbody>${rows}</tbody></table>`;
    };
    renderProgress('Start');

    // 3) EINE Filiale verarbeitet ihre Fensterliste WEITERHIN SEQUENZIELL (nie
    //    zwei Fenster derselben Filiale parallel — sonst Dedup-/Reihenfolge-
    //    Probleme). Request-Body unverändert. Walter-Vorgabe 22.06.2026.
    const runBranch = async (b) => {
        for (const [wf, wt] of windows) {
            step++;
            renderProgress(`${b.name}: ${fmt(wf)}–${fmt(wt)}`);
            try {
                const r = await fetch('/api/easywork/sync/timepunches/commit', {
                    method: 'POST',
                    headers: { ...ah(), 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        companyProfileId: b.id,
                        from: wf, to: wt,
                        employeeCutoffOverride: from,   // MA-Stichtag bis Import-Beginn
                        ignoreMissing: true             // nicht zuordenbare MA fallen lassen
                    })
                });
                const body = await r.json();
                if (!r.ok) {
                    agg[b.id].errors.push(`${fmt(wf)}–${fmt(wt)}: ${body.message || ('HTTP ' + r.status)}`);
                    continue;
                }
                // Der Commit-Endpoint liefert ein AutoSyncResult: inserted /
                // lockedSkipped / skipped / missingEmployees (NICHT die count*-
                // Felder der Vorschau!). Walter-Bug 21.06.2026: das UI las
                // body.countInserted → immer 0, obwohl Stempel geschrieben wurden.
                const a = agg[b.id];
                const ins  = body.inserted      || 0;
                const skip = body.skipped       || 0;
                const lock = body.lockedSkipped || 0;
                // „Nicht zugeordnet" = blockierend fehlende + (Tief-Import) bewusst
                // übersprungene MA. Beim ignoreMissing-Lauf liegen sie in
                // skippedMissingEmployees (sonst bliebe die Spalte fälschlich 0).
                const miss = ((body.missingEmployees && body.missingEmployees.length) || 0)
                           + ((body.skippedMissingEmployees && body.skippedMissingEmployees.length) || 0);
                a.inserted += ins;
                a.invalid  += skip;   // Spalte „Übersprungen" (Dubletten/ungültig/ausgeschlossen)
                a.locked   += lock;
                a.missing  += miss;
                grandInserted += ins;
                grandInvalid  += skip;
                grandLocked   += lock;
                grandMissing  += miss;
                // BLOCK (z.B. mehrere Lohn-MA für eine Person): Chunk wurde NICHT
                // geschrieben → isBlocked. Klartext aus den Notes sichtbar machen.
                if (body.isBlocked) {
                    const note = (body.notes && body.notes.length) ? body.notes.join(' ') : 'Import blockiert (mehrere Lohn-MA für eine Person).';
                    agg[b.id].errors.push(`${fmt(wf)}–${fmt(wt)}: ⚠ ${note}`);
                }
                // Sonst: wurde NICHTS importiert UND gibt es Backend-Notizen
                // (z.B. „max. 92 Tage", API-Fehler, „N MA übersprungen") →
                // anzeigen, damit ein stiller 0-Lauf nie unbemerkt bleibt.
                else if (ins === 0 && body.notes && body.notes.length) {
                    agg[b.id].errors.push(`${fmt(wf)}–${fmt(wt)}: ℹ ${body.notes.join(' ')}`);
                }
            } catch (e) {
                agg[b.id].errors.push(`${fmt(wf)}–${fmt(wt)}: ${String(e)}`);
            }
        }
    };

    // Worker-Pool: maximal 2 Filialen GLEICHZEITIG (jede für sich sequenziell).
    try {
        let nextBranchIdx = 0;
        const concurrency = Math.min(2, branches.length);
        const worker = async () => {
            while (nextBranchIdx < branches.length) {
                const b = branches[nextBranchIdx++];
                await runBranch(b);
            }
        };
        await Promise.all(Array.from({ length: concurrency }, worker));

        // 4) Abschluss-Anzeige (grünes Banner statt gelbem Fortschritt + Tabelle).
        const rows = branches.map(b => {
            const a = agg[b.id];
            const err = a.errors.length ? `<div style="color:#991b1b;font-size:11px">${a.errors.map(escapeHtml).join('<br>')}</div>` : '';
            return `<tr>
                <td style="padding:3px 10px">${escapeHtml(a.name)}</td>
                <td style="padding:3px 10px;text-align:right;color:#166534">${a.inserted}</td>
                <td style="padding:3px 10px;text-align:right;color:#b45309">${a.invalid}</td>
                <td style="padding:3px 10px;text-align:right;color:#64748b">${a.locked}</td>
                <td style="padding:3px 10px;text-align:right;color:#64748b">${a.missing}</td>
                <td style="padding:3px 10px">${err}</td>
            </tr>`;
        }).join('');
        out.innerHTML = `
            <div style="color:#166534;font-size:13px;padding:10px;background:#dcfce7;border:1px solid #bbf7d0;border-radius:8px;margin-bottom:10px">
                ✓ Tief-Import abgeschlossen — <strong>${grandInserted}</strong> Stempel importiert über ${branches.length} Filiale(n).
                Fallen gelassen: ${grandInvalid} fehlerhaft, ${grandLocked} in gesperrter Periode, ${grandMissing} nicht zuordenbar.
            </div>
            <table style="font-size:12px;border-collapse:collapse;width:100%">
                <thead><tr style="color:#64748b;text-align:left">
                    <th style="padding:3px 10px">Filiale</th>
                    <th style="padding:3px 10px;text-align:right">Importiert</th>
                    <th style="padding:3px 10px;text-align:right">Übersprungen</th>
                    <th style="padding:3px 10px;text-align:right">Gesperrt</th>
                    <th style="padding:3px 10px;text-align:right">Nicht&nbsp;zugeordnet</th>
                    <th></th>
                </tr></thead><tbody>${rows}</tbody></table>`;
    } finally {
        _eawBatchRunning = false;
    }
}

async function _eawEmpSyncRun(commit, selected) {
    const sel        = document.getElementById('eawEmpSyncBranchSel');
    const scopeEl    = document.getElementById('eawEmpSyncScope');
    const out        = document.getElementById('eawEmpSyncResult');
    const commitBtn  = document.getElementById('eawEmpSyncCommitBtn');
    if (!sel.value) { alert('Bitte zuerst Filiale wählen.'); return; }

    const _empCount = commit && Array.isArray(selected) ? selected.length : 0;
    const _empLabel = commit
        ? `Importiere${_empCount ? ' ' + _empCount + ' MA' : ' MA-Daten'}`
        : 'Lese MA-Stammdaten aus easy@work';
    const stopProgress = _eawStartProgress(out, _empLabel);
    commitBtn.disabled = true;

    const dto = {
        companyProfileId: parseInt(sel.value, 10),
        onlyActive:       (scopeEl?.value === 'active'),
        selectedNumbers:  selected
    };
    try {
        // Commit läuft als Hintergrund-Job (Walter 29.06.2026): sofort starten,
        // dann Fortschritt pollen — kein Request-Timeout mehr.
        if (commit) {
            stopProgress();
            await _eawEmpImportAsync(dto, out, commitBtn);
            return;
        }
        const url = '/api/easywork/sync/employees/preview';
        const r = await fetch(url, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        // Robust gegen leere/nicht-JSON-Antworten (z.B. 500 ohne Body): zuerst Text
        // lesen, dann parsen — sonst wirft res.json() „Unexpected end of JSON input"
        // und der echte Serverfehler bleibt verborgen.
        const raw = await r.text();
        let body = null;
        try { body = raw ? JSON.parse(raw) : null; } catch (_) { /* nicht-JSON */ }
        stopProgress();
        if (!r.ok) {
            const msg = (body && (body.message || JSON.stringify(body))) || raw || `HTTP ${r.status}`;
            out.innerHTML = `<div class="eaw-result eaw-result-err">
                <div class="eaw-result-title">✗ Fehler ${r.status}</div>
                <div class="eaw-result-msg">${escapeHtml(msg)}</div>
            </div>`;
            return;
        }
        if (!body) {
            out.innerHTML = `<div class="eaw-result eaw-result-err">
                <div class="eaw-result-title">Leere Antwort</div>
                <div class="eaw-result-msg">Server lieferte HTTP ${r.status} ohne Inhalt.</div>
            </div>`;
            return;
        }
        _eawEmpSyncLast = body;
        if (commit) {
            // Nach erfolgreichem Import: Vorschau leeren + knappe Bestätigung.
            const skip = body.skippedContracts || [];
            const skipHtml = skip.length
                ? `<div style="margin-top:8px;color:#b45309;font-size:12.5px;background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:10px">
                       <strong>⚠ ${skip.length} Vertrag/Verträge wegen geschlossener Lohnperiode NICHT importiert:</strong><br>
                       ${skip.map(s => '• ' + escapeHtml(s)).join('<br>')}
                   </div>`
                : '';
            out.innerHTML = `<div style="color:#166534;font-size:13px;padding:10px;background:#dcfce7;border:1px solid #bbf7d0;border-radius:8px">✓ Import abgeschlossen — ${body.countInserted||0} angelegt, ${body.countUpdated||0} aktualisiert.</div>${skipHtml}`;
            _eawEmpSyncLast = null;
        } else {
            _eawEmpSyncRender(body, commit);
        }
        // Auch enablen wenn nur UNCHANGED-MA da sind — Backfill braucht den Commit.
        // Bei doppelter Personalnummer bleibt „Importieren" gesperrt, bis Walter
        // die Dublette in beiden Systemen geklärt hat (Walter 29.06.2026).
        const hasConflicts = (body.numberConflicts || []).length > 0;
        const hasAny = (body.countNew + body.countUpdate + (body.countTotal - (body.countConflict||0))) > 0;
        commitBtn.disabled = commit || !hasAny || hasConflicts;
    } catch (e) {
        stopProgress();
        out.innerHTML = `<div class="eaw-result eaw-result-err">
            <div class="eaw-result-title">Netzwerkfehler</div>
            <div class="eaw-result-msg">${escapeHtml(String(e))}</div>
        </div>`;
    }
}

// Asynchroner Filial-Import (Walter 29.06.2026): stösst den Hintergrund-Job an
// und pollt Fortschritt + Ergebnis. So gibt es kein Request-Timeout mehr, auch
// wenn easy@work mehrere Minuten braucht.
async function _eawEmpImportAsync(dto, out, commitBtn) {
    const renderBar = (phase, done, total) => {
        const pct = total > 0 ? Math.round(done / total * 100) : 0;
        out.innerHTML = `
            <div style="background:#f6f3ee;border:1px solid #e5e0d6;border-radius:10px;padding:14px 16px">
                <div style="display:flex;justify-content:space-between;align-items:center;font-size:13px;color:#6b7280;font-weight:600;margin-bottom:8px">
                    <span>${escapeHtml(phase || 'Import läuft…')}</span>
                    <span>${total > 0 ? done + ' / ' + total : ''}</span>
                </div>
                <div style="height:10px;background:#ece9e2;border-radius:6px;overflow:hidden">
                    <div style="height:100%;width:${pct}%;background:#1a1a1a;transition:width .3s"></div>
                </div>
                <div style="font-size:11.5px;color:#64748b;margin-top:8px">Du kannst das Fenster offen lassen — der Import läuft auf dem Server weiter, auch wenn es etwas dauert.</div>
            </div>`;
    };
    renderBar('Starte Import…', 0, 0);

    let jobId;
    try {
        const r = await fetch('/api/easywork/sync/employees/commit-async', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(dto)
        });
        const raw = await r.text();
        const body = raw ? JSON.parse(raw) : null;
        if (!r.ok || !body?.jobId) throw new Error(body?.message || `HTTP ${r.status}`);
        jobId = body.jobId;
    } catch (e) {
        out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">Start fehlgeschlagen</div><div class="eaw-result-msg">${escapeHtml(String(e))}</div></div>`;
        commitBtn.disabled = false;
        return;
    }

    let fails = 0;
    const poll = async () => {
        try {
            const r = await fetch(`/api/easywork/sync/employees/job/${jobId}`, { headers: ah(), cache: 'no-store' });
            if (!r.ok) throw new Error(`HTTP ${r.status}`);
            const j = await r.json();
            fails = 0;
            if (j.status === 'running') { renderBar(j.phase, j.done, j.total); setTimeout(poll, 1500); return; }
            if (j.status === 'error') {
                out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">✗ Import fehlgeschlagen</div><div class="eaw-result-msg">${escapeHtml(j.error || 'Unbekannter Fehler')}</div></div>`;
                commitBtn.disabled = false; return;
            }
            // status === 'done'
            const res = j.result || {};

            // Personalnummer-Kollision → Import wurde BLOCKIERT, nichts geschrieben.
            // Genaue Nummern + beide Seiten anzeigen, damit Walter es in easy@work
            // UND Cowork prüfen kann (Walter 29.06.2026).
            const conflicts = res.numberConflicts || [];
            if (res.blocked || conflicts.length) {
                out.innerHTML = `
                    <div style="color:#991b1b;font-size:13px;padding:14px 16px;background:#fef2f2;border:1px solid #fecaca;border-radius:10px">
                        <div style="font-weight:700;margin-bottom:6px">✗ Import blockiert — doppelte Personalnummer (nichts gespeichert)</div>
                        <div style="color:#7f1d1d;font-size:12.5px;margin-bottom:8px">Folgende Personalnummer(n) wären doppelt vergeben. Bitte in easy@work und in Cowork prüfen und korrigieren, danach erneut „Vorschau" → „Importieren".</div>
                        ${conflicts.map(c => '<div style="padding:6px 0;border-top:1px solid #fecaca">• ' + escapeHtml(c) + '</div>').join('')}
                    </div>`;
                commitBtn.disabled = true;   // erst neue Vorschau nach Korrektur
                return;
            }

            const skip = res.skippedContracts || [];
            const notes = res.notes || [];
            const skipHtml = skip.length
                ? `<div style="margin-top:8px;color:#b45309;font-size:12.5px;background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:10px"><strong>⚠ ${skip.length} Vertrag/Verträge wegen geschlossener Lohnperiode NICHT importiert:</strong><br>${skip.map(s => '• ' + escapeHtml(s)).join('<br>')}</div>`
                : '';
            const noteColor = n => n.startsWith('✓') ? '#166534' : n.startsWith('⚠') ? '#b91c1c' : '#475569';
            const notesHtml = notes.length
                ? `<div style="margin-top:8px;font-size:12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:10px"><strong style="color:#334155">Hinweise (${notes.length}):</strong><br>${notes.map(n => `<div style="color:${noteColor(n)};padding:2px 0">${escapeHtml(n)}</div>`).join('')}</div>`
                : '';
            out.innerHTML = `<div style="color:#166534;font-size:13px;padding:10px;background:#dcfce7;border:1px solid #bbf7d0;border-radius:8px">✓ Import abgeschlossen — ${res.countInserted || 0} angelegt, ${res.countUpdated || 0} aktualisiert${res.countConflict ? `, ${res.countConflict} übersprungen` : ''}.</div>${skipHtml}${notesHtml}`;
            _eawEmpSyncLast = null;
            commitBtn.disabled = true;   // fertig → für erneuten Import zuerst neue Vorschau
        } catch (e) {
            if (++fails > 5) {
                out.innerHTML = `<div class="eaw-result eaw-result-err"><div class="eaw-result-title">Verbindung zum Import-Job verloren</div><div class="eaw-result-msg">${escapeHtml(String(e))} — der Import läuft serverseitig evtl. weiter. Bitte später die Liste prüfen.</div></div>`;
                commitBtn.disabled = false; return;
            }
            setTimeout(poll, 2500);
        }
    };
    setTimeout(poll, 1200);
}

function _eawEmpSyncRender(res, wasCommit) {
    const out = document.getElementById('eawEmpSyncResult');
    // Personalnummer-Kollision ganz oben, rot — Import bleibt gesperrt bis geklärt.
    const conflicts = res.numberConflicts || [];
    const conflictHtml = conflicts.length
        ? `<div style="color:#991b1b;font-size:13px;padding:14px 16px;margin-bottom:10px;background:#fef2f2;border:1px solid #fecaca;border-radius:10px">
               <div style="font-weight:700;margin-bottom:6px">⛔ Doppelte Personalnummer — Import gesperrt</div>
               <div style="color:#7f1d1d;font-size:12.5px;margin-bottom:8px">Bitte in easy@work UND in Cowork prüfen und korrigieren, danach erneut „Vorschau".</div>
               ${conflicts.map(c => '<div style="padding:6px 0;border-top:1px solid #fecaca">• ' + escapeHtml(c) + '</div>').join('')}
           </div>`
        : '';
    const _noteColor = n => n.startsWith('✓') ? '#166534' : n.startsWith('⚠') ? '#b91c1c' : '#b45309';
    const notes = (res.notes||[]).map(n => `<div style="color:${_noteColor(n)};font-size:12px;padding:4px 0">${escapeHtml(n)}</div>`).join('')
        + ((res.skippedContracts||[]).length
            ? `<div style="margin-top:6px;color:#b45309;font-size:12px;background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:8px">
                   <strong>⚠ Verträge wegen geschlossener Lohnperiode NICHT importiert:</strong><br>
                   ${res.skippedContracts.map(s => '• ' + escapeHtml(s)).join('<br>')}
               </div>`
            : '');
    const summary = `
        <div class="eaw-sync-summary">
            <span>Total: <strong>${res.countTotal}</strong></span>
            <span style="color:#166534">NEW: <strong>${res.countNew}</strong></span>
            <span style="color:#6b6152">UPDATE: <strong>${res.countUpdate}</strong></span>
            <span style="color:#64748b">UNCHANGED: <strong>${res.countUnchanged}</strong></span>
            <span style="color:#991b1b">CONFLICT: <strong>${res.countConflict}</strong></span>
            ${res.countExisting ? `<span style="color:#92400e">EXISTIERT: <strong>${res.countExisting}</strong></span>` : ''}
            ${wasCommit ? `<span style="color:#166534">Eingefügt: <strong>${res.countInserted}</strong></span><span style="color:#6b6152">Aktualisiert: <strong>${res.countUpdated}</strong></span>` : ''}
        </div>`;

    // Tabelle: pro MA eine Hauptzeile + Detail (Diffs) auf-/zuklappbar
    const rows = (res.rows||[]).map((r, idx) => {
        const pill = {
            NEW:        '<span class="eaw-pill eaw-pill-new">NEW</span>',
            UPDATE:     '<span class="eaw-pill" style="background:#ece9e2;color:#6b6152">UPDATE</span>',
            UNCHANGED:  '<span class="eaw-pill eaw-pill-soft">UNCHANGED</span>',
            EXISTING:   '<span class="eaw-pill" style="background:#fef9c3;color:#92400e">EXISTIERT</span>',
            CONFLICT:   '<span class="eaw-pill eaw-pill-unmatched">CONFLICT</span>',
        }[r.status] || r.status;
        const willWrite = (r.status === 'NEW' || r.status === 'UPDATE');
        const cb = willWrite
            ? `<input type="checkbox" class="eaw-emp-pick" data-number="${escapeHtml(r.number||'')}" checked>`
            : '';
        const changes = (r.diffs||[]).filter(d => d.willSet);
        const changesSummary = changes.length
            ? changes.map(d => escapeHtml(d.field)).join(', ')
            : '<span style="color:#94a3b8">—</span>';
        const detailId = `eawEmpDet_${idx}`;
        const detailRow = `<tr id="${detailId}" style="display:none"><td colspan="6" style="background:#f8fafc;padding:10px">
            ${_eawEmpRenderDiff(r.diffs||[])}
        </td></tr>`;
        // Zusätzliche, on-demand geladene easy@work-Detailzeile (fiscal_info + Custom Fields).
        const eawDetail = r.eawEmployeeId
            ? ` · <a href="#" onclick="event.preventDefault();eawEmpLoadDetail(${r.eawEmployeeId}, '${detailId}eaw')" style="color:#6b7280;font-size:12px">🔎 easy@work-Felder</a>`
            : '';
        const eawDetailRow = r.eawEmployeeId
            ? `<tr id="${detailId}eaw" style="display:none"><td colspan="6" style="background:#f6f3ee;padding:10px"></td></tr>`
            : '';
        return `<tr>
            <td>${cb}</td>
            <td>${pill}</td>
            <td>${escapeHtml((r.firstName||'') + ' ' + (r.lastName||''))} <span style="color:#94a3b8">(${escapeHtml(r.number||'-')})</span>
                ${r.numberChangeFrom ? `<div style="font-size:11px;margin-top:2px;background:#fef9c3;border:1px solid #fde68a;color:#92400e;border-radius:5px;padding:2px 6px;display:inline-block">🔁 Personalnummer: <strong>${escapeHtml(r.numberChangeFrom)}</strong> → <strong>${escapeHtml(r.numberChangeTo||'')}</strong> <span style="color:#a16207">(alte Nr. → Alias)</span></div>` : ''}
                ${r.matchedViaAltNumber ? `<div style="font-size:11px;color:#7c3aed">↪ gematcht über alte Nr. ${escapeHtml(r.matchedViaAltNumber)}</div>` : ''}
                ${r.possibleReentry ? `<div style="font-size:11px;margin-top:3px;background:#fef9c3;border:1px solid #fbbf24;color:#92400e;border-radius:5px;padding:3px 7px">⚠ Möglicher Wiedereintritt: gleicher Name + Geburtsdatum wie bestehender MA <strong>#${r.reentryEmployeeId}${r.reentryEmployeeNumber ? ' ('+escapeHtml(r.reentryEmployeeNumber)+')' : ''}</strong> (neue eaw-ID ${r.reentryNewEawId}). Beim Import wird die alte eaw-ID als Alias gespeichert. <strong>Falls eine andere Person → abwählen.</strong></div>` : ''}
                ${r.employmentInfo ? `<div style="font-size:11px;color:#475569">Employment: <strong>${escapeHtml(r.employmentInfo)}</strong>${r.assignedBranchName ? ' · Filiale: ' + escapeHtml(r.assignedBranchName) : ''}</div>` : ''}
            </td>
            <td style="font-size:11px;color:#475569">${changesSummary}</td>
            <td><a href="#" onclick="event.preventDefault();_eawEmpToggle('${detailId}')" style="color:#6b6152;font-size:12px">Diffs</a>${eawDetail}</td>
            <td style="color:#94a3b8;font-size:11px">${escapeHtml(r.reason||'')}</td>
        </tr>${detailRow}${eawDetailRow}`;
    }).join('');

    out.innerHTML = `
        ${conflictHtml}
        ${notes}
        ${summary}
        <table class="eaw-sync-table">
            <thead><tr>
                <th style="width:30px"><input type="checkbox" id="eawEmpAllChk" onchange="_eawEmpToggleAll(this.checked)"></th>
                <th>Status</th><th>MA</th><th>Änderungen</th><th></th><th>Bemerkung</th>
            </tr></thead>
            <tbody>${rows || '<tr><td colspan="6" style="padding:10px;color:#94a3b8">— keine Einträge —</td></tr>'}</tbody>
        </table>`;
    // Default: alle NEW+UPDATE selektiert (Walter kann einzeln abwählen)
    const allChk = document.getElementById('eawEmpAllChk');
    if (allChk) allChk.checked = true;
}

function _eawEmpToggle(id) {
    const el = document.getElementById(id);
    if (!el) return;
    el.style.display = (el.style.display === 'none') ? '' : 'none';
}

function _eawEmpToggleAll(on) {
    document.querySelectorAll('.eaw-emp-pick').forEach(cb => { cb.checked = !!on; });
}

// On-Demand: easy@work fiscal_info + Custom Fields eines MA laden + anzeigen
// (Walter-Vorgabe 19.06.2026, read-only). Zeigt die echten Property-`key`s.
async function eawEmpLoadDetail(eawEmployeeId, rowId) {
    const row = document.getElementById(rowId);
    if (!row) return;
    if (row.style.display !== 'none') { row.style.display = 'none'; return; }  // toggle zu
    row.style.display = '';
    const cell = row.querySelector('td');
    cell.innerHTML = '<span style="color:#64748b;font-size:12px">⏳ Lade easy@work-Felder…</span>';
    const branchId = document.getElementById('eawEmpSyncBranchSel')?.value;
    if (!branchId) { cell.innerHTML = '<span style="color:#b91c1c">Keine Filiale gewählt.</span>'; return; }
    try {
        const r = await fetch(`/api/easywork/employees/${branchId}/${eawEmployeeId}/detail`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) { cell.innerHTML = `<span style="color:#b91c1c">Fehler ${r.status}</span>`; return; }
        cell.innerHTML = _eawEmpRenderEawDetail(await r.json());
    } catch (e) { cell.innerHTML = '<span style="color:#b91c1c">Netzwerkfehler.</span>'; }
}

function _eawEmpRenderEawDetail(d) {
    const f = d.fiscal;
    const fRows = f ? [
        ['Bewilligung', f.visaPermitType],
        ['Bewilligung gültig', (f.emission || f.expiration) ? `${f.emission || '?'} – ${f.expiration || '?'}` : null],
        ['IBAN', f.iban],
        ['Bank (Clearing)', f.bankId],
        ['Konto-Inhaber', f.accountName],
        ['Ehepartner arbeitet CH', (f.spouseWorksSwitzerland != null) ? f.spouseWorksSwitzerland : null],
        ['Ehepartner Bewilligung', f.spouseVisaPermitType],
    ].filter(x => x[1] != null && x[1] !== '') : [];
    const fiscalHtml = fRows.length
        ? `<div style="font-weight:600;margin-bottom:4px">fiscal_info</div>` +
          fRows.map(x => `<div><span style="color:#64748b">${escapeHtml(x[0])}:</span> <strong>${escapeHtml(String(x[1]))}</strong></div>`).join('')
        : '<div style="color:#94a3b8">Keine fiscal_info.</div>';
    const props = d.properties || [];
    const propsHtml = props.length
        ? `<div style="font-weight:600;margin:10px 0 4px">Custom Fields <span style="color:#94a3b8;font-weight:400">(key → value — diese Keys nutze ich fürs Mapping)</span></div>` +
          `<table style="font-size:12px;border-collapse:collapse">${props.map(p =>
              `<tr><td style="padding:1px 14px 1px 0;color:#6b7280;font-family:monospace">${escapeHtml(p.key || '')}</td><td style="padding:1px 0"><strong>${escapeHtml(p.value || '')}</strong></td></tr>`
          ).join('')}</table>`
        : '<div style="color:#94a3b8;margin-top:8px">Keine Custom Fields.</div>';
    const notes = (d.notes || []).map(n => `<div style="color:#b45309;font-size:11px;margin-top:4px">⚠ ${escapeHtml(n)}</div>`).join('');
    return `<div style="font-size:12px;line-height:1.5">${fiscalHtml}${propsHtml}${notes}</div>`;
}

function _eawEmpRenderDiff(diffs) {
    if (!diffs || !diffs.length) return '<span style="color:#94a3b8">— keine Felder —</span>';
    const rows = diffs.map(d => {
        const bg = d.willSet ? 'background:#dcfce7' : '';
        const w  = d.willSet ? '✓ Übernehmen' : '·';
        return `<tr style="${bg}">
            <td style="padding:3px 8px;font-weight:600;width:120px">${escapeHtml(d.field)}</td>
            <td style="padding:3px 8px;color:#64748b;width:240px">${escapeHtml(d.cowork||'—')}</td>
            <td style="padding:3px 8px;width:30px;color:#94a3b8">→</td>
            <td style="padding:3px 8px;font-weight:600;width:240px">${escapeHtml(d.easy||'—')}</td>
            <td style="padding:3px 8px;font-size:11px;color:#166534">${w}</td>
        </tr>`;
    }).join('');
    return `<table style="font-size:12px;width:100%;border-collapse:collapse">
        <thead><tr style="color:#94a3b8;font-size:11px">
            <th style="text-align:left;padding:3px 8px">Feld</th>
            <th style="text-align:left;padding:3px 8px">Cowork</th>
            <th></th>
            <th style="text-align:left;padding:3px 8px">easy@work</th>
            <th></th>
        </tr></thead>
        <tbody>${rows}</tbody>
    </table>`;
}

async function eawLoadStatus() {
    const box = document.getElementById('eawStatusBox');
    if (!box) return;
    try {
        const r = await fetch('/api/easywork/status', { headers: ah() });
        if (!r.ok) {
            box.innerHTML = `<div style="color:#991b1b">Status konnte nicht geladen werden (HTTP ${r.status}).</div>`;
            return;
        }
        const s = await r.json();
        if (!s.configured) {
            box.innerHTML = `
                <div style="font-weight:600;color:#b45309;margin-bottom:6px">⚠ Nicht konfiguriert</div>
                <div style="font-size:13px;color:#64748b">
                    Setze auf dem Server die ENV-Variablen
                    <code>EASYATWORK_CLIENT_ID</code>,
                    <code>EASYATWORK_CLIENT_SECRET</code> und
                    <code>EASYATWORK_BASE_URL</code> (oder die Section
                    <code>EasyAtWork</code> in <code>appsettings.json</code>).
                    Danach Service neu starten.
                </div>`;
        } else {
            box.innerHTML = `
                <div style="font-weight:600;color:#065f46;margin-bottom:6px">✓ Konfiguriert</div>
                <div style="font-size:13px;color:#475569">
                    Base-URL: <code>${escapeHtml(s.baseUrl||'')}</code><br/>
                    Client-ID: <code>${escapeHtml(s.clientId||'')}</code>
                </div>`;
        }
    } catch (e) {
        box.innerHTML = `<div style="color:#991b1b">Fehler: ${escapeHtml(String(e))}</div>`;
    }
}

async function eawTestConnection() {
    const out = document.getElementById('eawTestResultBox');
    if (!out) return;
    out.style.display = 'block';
    out.innerHTML = '⏳ Token holen + GET /customers …';
    try {
        const r = await fetch('/api/easywork/test-connection', { headers: ah() });
        const body = await r.json();
        if (!r.ok) {
            out.className = 'eaw-result eaw-result-err';
            out.innerHTML = `<div class="eaw-result-title">✗ Fehler ${r.status}</div>
                             <div class="eaw-result-msg">${escapeHtml(body.message || JSON.stringify(body))}</div>`;
            _eawCustomers = [];
            eawRenderMappings();
            return;
        }
        _eawCustomers = body.customers || [];
        out.className = 'eaw-result eaw-result-ok';
        const rows = _eawCustomers.map(c =>
            `<tr><td>${c.id}</td>
                 <td>${escapeHtml(c.number||'')}</td>
                 <td>${escapeHtml(c.name||'')}</td></tr>`).join('');
        out.innerHTML = `
            <div class="eaw-result-title">✓ Verbindung OK — ${_eawCustomers.length} Customer(s)</div>
            <table class="eaw-result-table">
                <thead><tr><th>Customer-ID</th><th>Number</th><th>Name</th></tr></thead>
                <tbody>${rows || '<tr><td colspan="3" class="eaw-empty">— keine Customers sichtbar —</td></tr>'}</tbody>
            </table>`;
        eawRenderMappings();
    } catch (e) {
        out.className = 'eaw-result eaw-result-err';
        out.innerHTML = `<div class="eaw-result-title">Netzwerkfehler</div>
                         <div class="eaw-result-msg">${escapeHtml(String(e))}</div>`;
    }
}

async function eawLoadMappings() {
    try {
        const r = await fetch('/api/easywork/mappings', { headers: ah() });
        if (!r.ok) {
            _eawMappings = [];
            eawRenderMappings();
            return;
        }
        _eawMappings = await r.json();
        // Sortierung wie in der Hauptauswahl (Sidebar): nach Filial-Code
        // aufsteigend, Tie-Break nach Name (Walter-Vorgabe 05.07.2026).
        _eawMappings.sort((a, b) =>
            (parseInt(a.restaurantCode || '9999', 10) - parseInt(b.restaurantCode || '9999', 10))
            || String(a.companyProfileName || '').localeCompare(String(b.companyProfileName || '')));
    } catch {
        _eawMappings = [];
    }
    eawRenderMappings();
}

function eawRenderMappings() {
    const container = document.getElementById('eawMappingsContainer');
    if (!container) return;
    // allBranches kommt aus index.html (let im globalen Scope, NICHT window.allBranches).
    const branches = (typeof allBranches !== 'undefined' && Array.isArray(allBranches)) ? allBranches : [];
    if (!branches.length) {
        container.innerHTML = '<div style="color:#64748b;font-size:13px">— keine Filialen geladen —</div>';
        return;
    }
    const mappingByCp = new Map(_eawMappings.map(m => [m.companyProfileId, m]));
    const renderOpts = (selectedId, mappingFallback) => {
        // Auch wenn die _eawCustomers-Liste leer ist (kein test-connection
        // gelaufen), zeigen wir das bereits gespeicherte Mapping als Option
        // an, damit der User die aktuelle Zuordnung sieht.
        const opts = _eawCustomers.slice();
        if (mappingFallback && !opts.some(c => c.id === mappingFallback.easyAtWorkCustomerId)) {
            opts.push({
                id:     mappingFallback.easyAtWorkCustomerId,
                number: mappingFallback.easyAtWorkCustomerNumber,
                name:   mappingFallback.easyAtWorkCustomerName
            });
        }
        if (!opts.length) {
            return '<option value="">(zuerst „Verbindung testen" klicken)</option>';
        }
        return opts.map(c => {
            const isSel = (c.id === selectedId) ? ' selected' : '';
            return `<option value="${c.id}" data-number="${escapeHtml(c.number||'')}" data-name="${escapeHtml(c.name||'')}"${isSel}>${c.id} — ${escapeHtml(c.number||'')} ${escapeHtml(c.name||'')}</option>`;
        }).join('');
    };
    const rows = branches.map(b => {
        const m = mappingByCp.get(b.id);
        const opts = renderOpts(m ? m.easyAtWorkCustomerId : null, m);
        const sel = `<select id="eawSel_${b.id}">
                <option value="">— nicht gemappt —</option>
                ${opts}
            </select>`;
        const actions = m
            ? `<button class="btn-secondary" onclick="eawSaveMapping(${b.id})" style="margin-right:6px">Speichern</button>
               <button class="btn-secondary" onclick="eawDeleteMapping(${b.id})">Mapping entfernen</button>`
            : `<button class="btn-secondary" onclick="eawSaveMapping(${b.id})">Speichern</button>`;
        const branchName = b.branchName || b.companyName || b.name || '';
        const code = b.restaurantCode || '';
        return `<tr>
            <td>${escapeHtml(branchName)}</td>
            <td>${escapeHtml(code)}</td>
            <td>${sel}</td>
            <td class="eaw-actions">${actions}</td>
        </tr>`;
    }).join('');
    container.innerHTML = `
        <table class="eaw-map-table">
            <thead>
                <tr>
                    <th>Filiale</th>
                    <th>Code</th>
                    <th>easy@work-Customer</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>`;
}

async function eawSaveMapping(companyProfileId) {
    const sel = document.getElementById(`eawSel_${companyProfileId}`);
    if (!sel || !sel.value) {
        alert('Bitte erst eine Customer-ID auswählen (vorher „Verbindung testen" klicken).');
        return;
    }
    const opt = sel.options[sel.selectedIndex];
    const dto = {
        companyProfileId: companyProfileId,
        easyAtWorkCustomerId: parseInt(sel.value, 10),
        easyAtWorkCustomerNumber: opt?.dataset?.number || null,
        easyAtWorkCustomerName: opt?.dataset?.name || null,
    };
    try {
        const r = await fetch('/api/easywork/mappings', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        if (!r.ok) {
            const b = await r.json().catch(() => ({}));
            alert('Fehler: ' + (b.message || r.status));
            return;
        }
        await eawLoadMappings();
    } catch (e) {
        alert('Netzwerkfehler: ' + e);
    }
}

// Auto-Map per Code: easy@work-Number ↔ Cowork-RestaurantCode (führende Nullen werden getrimmt).
async function eawAutoMap() {
    if (!_eawCustomers.length) {
        alert('Bitte zuerst „Verbindung testen" klicken — Customer-Liste fehlt.');
        return;
    }
    const branches = (typeof allBranches !== 'undefined' && Array.isArray(allBranches)) ? allBranches : [];
    if (!branches.length) { alert('Keine Filialen geladen.'); return; }
    // Normalisierungs-Helper: "058" → "58", " 225 " → "225"
    const norm = s => String(s||'').trim().replace(/^0+/, '') || '0';
    const custByNum = new Map(_eawCustomers.map(c => [norm(c.number), c]));
    const plan = [];
    for (const b of branches) {
        const code = norm(b.restaurantCode);
        const cust = custByNum.get(code);
        if (cust) {
            const existing = _eawMappings.find(m => m.companyProfileId === b.id);
            if (existing && existing.easyAtWorkCustomerId === cust.id) continue; // schon korrekt
            plan.push({ branch: b, cust });
        }
    }
    if (!plan.length) { alert('Alle möglichen Mappings sind bereits korrekt — nichts zu tun.'); return; }
    if (!confirm(`${plan.length} Filiale(n) automatisch mappen?\n\n` +
        plan.map(p => `${p.branch.branchName||p.branch.companyName} (${p.branch.restaurantCode}) → ${p.cust.id} ${p.cust.name||''}`).join('\n'))) return;
    let ok = 0, errs = [];
    for (const p of plan) {
        const dto = {
            companyProfileId: p.branch.id,
            easyAtWorkCustomerId: p.cust.id,
            easyAtWorkCustomerNumber: p.cust.number,
            easyAtWorkCustomerName: p.cust.name,
        };
        try {
            const r = await fetch('/api/easywork/mappings', {
                method: 'POST',
                headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify(dto)
            });
            if (r.ok) ok++;
            else {
                const b = await r.json().catch(()=>({}));
                errs.push(`${p.branch.branchName||p.branch.companyName}: ${b.message||r.status}`);
            }
        } catch (e) {
            errs.push(`${p.branch.branchName||p.branch.companyName}: ${e}`);
        }
    }
    await eawLoadMappings();
    eawSyncInit();   // Sync-Dropdown mit den neuen Mappings neu befüllen
    let msg = `${ok} Mapping(s) gespeichert.`;
    if (errs.length) msg += `\n\nFehler:\n` + errs.join('\n');
    alert(msg);
}

async function eawDeleteMapping(companyProfileId) {
    if (!confirm('Mapping entfernen?')) return;
    try {
        const r = await fetch(`/api/easywork/mappings/${companyProfileId}`, {
            method: 'DELETE',
            headers: ah()
        });
        if (!r.ok && r.status !== 204) {
            alert('Fehler beim Löschen (HTTP ' + r.status + ')');
            return;
        }
        await eawLoadMappings();
    } catch (e) {
        alert('Netzwerkfehler: ' + e);
    }
}

// kleines escapeHtml-Fallback, falls die globale Funktion nicht da ist
if (typeof escapeHtml !== 'function') {
    window.escapeHtml = function (s) {
        if (s == null) return '';
        return String(s).replace(/[&<>"']/g, c =>
            ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    };
}

// Verschollen-Check manuell (Walter 06.08.2026): gleicher Lauf wie im
// Nacht-Sync — setzt und HEBT Markierungen sofort auf.
async function eawVerschollenCheck(btn) {
    if (btn) { btn.disabled = true; btn.textContent = '🕵️ Prüfe…'; }
    try {
        const res = await fetch('/api/easywork/verschollen-check', { method: 'POST', headers: ah() });
        if (!res.ok) { showToast('Verschollen-Check fehlgeschlagen (HTTP ' + res.status + ').', 'error'); return; }
        const d = await res.json();
        const notes = d.notes || [];
        if (!notes.length) showToast('Verschollen-Check: alles unverändert — keine Auffälligkeiten.', 'success');
        else alert('Verschollen-Check:\n\n' + notes.join('\n'));
    } catch (_) {
        showToast('Verbindungsfehler beim Verschollen-Check.', 'error');
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = '🕵️ Verschollen-Check jetzt'; }
    }
}


// ── Absenz-Sync easy@work → OneCrew (Walter 14.08.2026) ─────────────────
// Vorschau (dryRun) zeigt NEU/UPDATE/DELETE/KONFLIKT/SKIP pro Zeile;
// «Übertragen» erscheint erst nach der Vorschau und fragt nochmals nach.
async function eawAbsenceSync(dryRun) {
    const out = document.getElementById('eawAbsSyncResult');
    const btn = document.getElementById('eawAbsCommitBtn');
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : '';
    if (!cpId) { if (out) out.textContent = 'Bitte zuerst oben eine Filiale wählen.'; return; }
    const von = document.getElementById('eawAbsVon')?.value || '2026-01-01';
    if (!dryRun) {
        const ok = await liquidConfirm(
            'Absenzen jetzt aus easy@work übernehmen? Neue werden angelegt, geänderte angepasst, in easy gelöschte entfernt (gesperrte Lohnperioden bleiben unangetastet).',
            { title: 'Absenzen-Sync', yesLabel: 'Ja, übertragen', noLabel: 'Abbrechen' });
        if (!ok) return;
    }
    if (out) out.textContent = dryRun ? 'Vorschau wird geladen…' : 'Absenzen werden übertragen…';
    try {
        const r = await fetch(`/api/easywork/absence-sync?companyProfileId=${cpId}&von=${von}&dryRun=${dryRun}`, {
            method: 'POST', headers: ah() });
        const j = await r.json();
        if (!r.ok) { out.textContent = 'Fehler: ' + (j?.message || j?.error || ('HTTP ' + r.status)); return; }
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        const farbe = { NEU: '#166534', UPDATE: '#1e40af', DELETE: '#991b1b', KONFLIKT: '#9a3412', SKIP: '#8b8b8b' };
        const fmtD = (iso) => iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : '';
        const rows = (j.zeilen || []).map(z => `
            <tr style="border-bottom:1px solid #e2e8f0">
                <td style="padding:2px 6px;font-weight:700;color:${farbe[z.aktion] || '#0f172a'}">${esc(z.aktion)}</td>
                <td style="padding:2px 6px">${esc(z.maName)}</td>
                <td style="padding:2px 6px">${esc(z.code || '')}<span style="color:#94a3b8"> (${esc(z.easyTyp || '')})</span></td>
                <td style="padding:2px 6px;white-space:nowrap">${fmtD(z.von)} – ${fmtD(z.bis)}</td>
                <td style="padding:2px 6px">${z.prozent && z.prozent !== 100 ? z.prozent + '%' : ''}</td>
                <td style="padding:2px 6px;color:#64748b">${esc(z.hinweis || '')}</td>
            </tr>`).join('');
        out.innerHTML = `
            <div style="margin-bottom:6px"><b>${j.dryRun ? 'Vorschau' : 'Übertragen'}:</b>
                ${j.neu} neu · ${j.geaendert} geändert · ${j.geloescht} gelöscht · ${j.uebersprungen} übersprungen/Konflikt
                <span style="color:#94a3b8">(ab ${fmtD(j.von)})</span></div>
            ${rows ? `<table style="border-collapse:collapse;width:100%">${rows}</table>`
                   : '<span style="color:#64748b">Keine Änderungen — alles aktuell.</span>'}`;
        if (btn) btn.style.display = j.dryRun && (j.neu + j.geaendert + j.geloescht) > 0 ? '' : 'none';
        if (!j.dryRun) showToast('Absenzen-Sync abgeschlossen.', 'success');
    } catch (e) {
        if (out) out.textContent = 'Verbindungsfehler: ' + e.message;
    }
}
