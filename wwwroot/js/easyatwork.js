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
    await eawLoadStatus();
    await eawLoadMappings();
    eawSyncInit();
    _eawEmpSyncInit();
}

// ═══════════════════════ Stempelzeit-Sync (Phase 2) ══════════════════════

let _eawSyncLastPreview = null;

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

    // Datumsbereich-Default: letzte 7 Tage
    const fromEl = document.getElementById('eawSyncFrom');
    const toEl   = document.getElementById('eawSyncTo');
    if (fromEl && !fromEl.value) {
        const today = new Date();
        const past  = new Date(today); past.setDate(today.getDate() - 7);
        fromEl.value = past.toISOString().slice(0, 10);
        toEl.value   = today.toISOString().slice(0, 10);
    }
}

async function eawSyncPreview() {
    await _eawSyncRun(false);
}
async function eawSyncCommit() {
    if (!_eawSyncLastPreview || !_eawSyncLastPreview.countNew) {
        alert('Keine NEW-Zeilen zum Importieren.');
        return;
    }
    if (_eawSyncLastPreview.missingEmployees && _eawSyncLastPreview.missingEmployees.length) {
        alert('Import blockiert: Es gibt easy@work-MA ohne Cowork-Zuordnung. Bitte diese zuerst zuordnen („→ MA zuordnen") oder in Cowork anlegen.');
        return;
    }
    if (!confirm(`${_eawSyncLastPreview.countNew} Stempelzeit(en) jetzt importieren?`)) return;
    await _eawSyncRun(true);
}

async function _eawSyncRun(commit) {
    const sel    = document.getElementById('eawSyncBranchSel');
    const fromEl = document.getElementById('eawSyncFrom');
    const toEl   = document.getElementById('eawSyncTo');
    const out    = document.getElementById('eawSyncResult');
    const commitBtn = document.getElementById('eawSyncCommitBtn');
    if (!sel.value) { alert('Bitte zuerst Filiale wählen.'); return; }
    if (!fromEl.value || !toEl.value) { alert('Bitte Datumsbereich angeben.'); return; }

    out.innerHTML = `<div style="color:#64748b;font-size:13px;padding:8px">⏳ ${commit ? 'Importiere' : 'Hole Vorschau'} …</div>`;
    commitBtn.disabled = true;

    const dto = {
        companyProfileId: parseInt(sel.value, 10),
        from: fromEl.value,
        to:   toEl.value
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
        if (!r.ok) {
            out.innerHTML = `<div class="eaw-result eaw-result-err">
                <div class="eaw-result-title">✗ Fehler ${r.status}</div>
                <div class="eaw-result-msg">${escapeHtml(body.message || JSON.stringify(body))}</div>
            </div>`;
            return;
        }
        _eawSyncLastPreview = body;
        _eawSyncRenderResult(body, commit);
        // Nach Commit nicht erneut committen können; ebenso gesperrt, solange
        // es nicht-zuordenbare MA gibt (Preflight-Block) oder keine NEW-Zeilen.
        const blocked = (body.missingEmployees && body.missingEmployees.length > 0);
        commitBtn.disabled = commit || blocked || !(body.countNew > 0);
    } catch (e) {
        out.innerHTML = `<div class="eaw-result eaw-result-err">
            <div class="eaw-result-title">Netzwerkfehler</div>
            <div class="eaw-result-msg">${escapeHtml(String(e))}</div>
        </div>`;
    }
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
            <div style="font-size:12px;color:#7f1d1d;margin-bottom:10px">Diese easy@work-MA haben Stempel im Zeitraum, lassen sich aber keiner Cowork-Personalnummer zuordnen. Bitte zuordnen oder in Cowork anlegen — erst dann kann importiert werden.</div>
            <table class="eaw-sync-table" style="margin:0">
                <thead><tr><th>easy@work-MA</th><th style="text-align:right">Stempel</th><th>Problem</th><th></th></tr></thead>
                <tbody>${missing.map(m => `
                    <tr>
                        <td>${escapeHtml(m.eawEmployeeName || ('easy@work-MA #' + m.eawEmployeeId))} <span style="color:#94a3b8">(#${m.eawEmployeeId}${m.eawEmployeeNumber ? ' · Nr. ' + escapeHtml(m.eawEmployeeNumber) : ''})</span></td>
                        <td style="text-align:right">${m.timepunchCount}</td>
                        <td style="color:#7f1d1d;font-size:12px">${escapeHtml(m.reason || '')}</td>
                        <td><button onclick="eawAssignAlias(${m.eawEmployeeId})" style="background:#dbeafe;border:1px solid #93c5fd;color:#1d4ed8;border-radius:6px;padding:3px 8px;font-size:12px;cursor:pointer;white-space:nowrap">→ MA zuordnen</button></td>
                    </tr>`).join('')}</tbody>
            </table>
        </div>` : '';

    const summary = `
        <div class="eaw-sync-summary">
            <span>Total: <strong>${res.countTotal}</strong></span>
            <span style="color:#166534">${wasCommit ? 'Importiert' : 'NEW'}: <strong>${wasCommit ? res.countInserted : res.countNew}</strong></span>
            <span style="color:#854d0e">Duplikate: <strong>${res.countDuplicate}</strong></span>
            <span style="color:#991b1b">Unmatched: <strong>${res.countUnmatched}</strong></span>
            <span style="color:#64748b">Gelöscht/Invalid: <strong>${res.countSoftDeleted + res.countInvalid}</strong></span>
        </div>`;
    const rows = (res.rows||[]).map(r => {
        const pill = {
            NEW:        '<span class="eaw-pill eaw-pill-new">NEW</span>',
            DUPLICATE:  '<span class="eaw-pill eaw-pill-duplicate">DUPLIKAT</span>',
            UNMATCHED:  '<span class="eaw-pill eaw-pill-unmatched">UNMATCHED</span>',
            SOFT_DELETED:'<span class="eaw-pill eaw-pill-soft">GELÖSCHT</span>',
            INVALID:    '<span class="eaw-pill eaw-pill-invalid">INVALID</span>',
        }[r.status] || r.status;
        const comment = escapeHtml(r.comment || r.reason || '');
        // Bei UNMATCHED mit bekannter easy@work-ID: Ein-Klick-Zuordnung anbieten.
        const assignBtn = (r.status === 'UNMATCHED' && r.eawEmployeeId)
            ? `<button onclick="eawAssignAlias(${r.eawEmployeeId})" style="margin-top:4px;display:inline-block;background:#dbeafe;border:1px solid #93c5fd;color:#1d4ed8;border-radius:6px;padding:3px 8px;font-size:12px;cursor:pointer">→ MA zuordnen</button>`
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
            <td style="text-align:right;color:${r.nightHours > 0 ? '#1e40af' : '#94a3b8'}">${r.nightHours != null ? Number(r.nightHours).toFixed(2) : ''}</td>
            <td>${editFlag}</td>
            <td style="color:#475569">${comment}${assignBtn ? '<br>' + assignBtn : ''}</td>
        </tr>`;
    }).join('');
    out.innerHTML = `
        ${missingHtml}
        ${notes}
        ${summary}
        <table class="eaw-sync-table">
            <thead><tr>
                <th>Status</th><th>MA (Personalnr.)</th><th>Datum</th><th>Von → Bis</th><th style="text-align:right">Std</th><th style="text-align:right">Nacht</th><th>Bearbeitet</th><th>Bemerkung</th>
            </tr></thead>
            <tbody>${rows || '<tr><td colspan="8" style="padding:10px;color:#94a3b8">— keine Einträge —</td></tr>'}</tbody>
        </table>`;
}

function _eawDate(iso) {
    if (!iso) return '';
    const s = String(iso).slice(0, 10);
    return s.length === 10 ? `${s.slice(8,10)}.${s.slice(5,7)}.${s.slice(0,4)}` : s;
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

async function eawAssignAlias(eawEmployeeId) {
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
                <button onclick="eawAliasSave()" style="background:#1d4ed8;border:1px solid #1d4ed8;color:#fff;border-radius:6px;padding:7px 14px;cursor:pointer">Zuordnen &amp; speichern</button>
            </div>
        </div>`;
    overlay.style.display = 'flex';
    const search = document.getElementById('eawAliasSearch');
    if (search) search.focus();
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
}

async function eawEmpSyncPreview() {
    await _eawEmpSyncRun(false, null);
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
    await _eawEmpSyncRun(true, checked);
}

async function _eawEmpSyncRun(commit, selected) {
    const sel        = document.getElementById('eawEmpSyncBranchSel');
    const exitedEl   = document.getElementById('eawEmpSyncExitedAfter');
    const out        = document.getElementById('eawEmpSyncResult');
    const commitBtn  = document.getElementById('eawEmpSyncCommitBtn');
    if (!sel.value) { alert('Bitte zuerst Filiale wählen.'); return; }

    out.innerHTML = `<div style="color:#64748b;font-size:13px;padding:8px">⏳ ${commit ? 'Importiere' : 'Hole Vorschau'} …</div>`;
    commitBtn.disabled = true;

    // Logik: Stichtag = heute (immer). Aktive MA werden immer geladen.
    // ExitedAfter optional: zusätzlich die Ausgetretenen mit Austritt > Cutoff.
    const dto = {
        companyProfileId: parseInt(sel.value, 10),
        activeAt:           null,
        exitedAfter:        exitedEl?.value || null,
        includeAllInactive: false,
        selectedNumbers:    selected
    };
    try {
        const url = commit ? '/api/easywork/sync/employees/commit'
                           : '/api/easywork/sync/employees/preview';
        const r = await fetch(url, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        const body = await r.json();
        if (!r.ok) {
            out.innerHTML = `<div class="eaw-result eaw-result-err">
                <div class="eaw-result-title">✗ Fehler ${r.status}</div>
                <div class="eaw-result-msg">${escapeHtml(body.message || JSON.stringify(body))}</div>
            </div>`;
            return;
        }
        _eawEmpSyncLast = body;
        _eawEmpSyncRender(body, commit);
        // Auch enablen wenn nur UNCHANGED-MA da sind — Backfill braucht den Commit.
        const hasAny = (body.countNew + body.countUpdate + (body.countTotal - (body.countConflict||0))) > 0;
        commitBtn.disabled = commit || !hasAny;
    } catch (e) {
        out.innerHTML = `<div class="eaw-result eaw-result-err">
            <div class="eaw-result-title">Netzwerkfehler</div>
            <div class="eaw-result-msg">${escapeHtml(String(e))}</div>
        </div>`;
    }
}

function _eawEmpSyncRender(res, wasCommit) {
    const out = document.getElementById('eawEmpSyncResult');
    const notes = (res.notes||[]).map(n => `<div style="color:#b45309;font-size:12px;padding:4px 0">⚠ ${escapeHtml(n)}</div>`).join('');
    const summary = `
        <div class="eaw-sync-summary">
            <span>Total: <strong>${res.countTotal}</strong></span>
            <span style="color:#166534">NEW: <strong>${res.countNew}</strong></span>
            <span style="color:#1e40af">UPDATE: <strong>${res.countUpdate}</strong></span>
            <span style="color:#64748b">UNCHANGED: <strong>${res.countUnchanged}</strong></span>
            <span style="color:#991b1b">CONFLICT: <strong>${res.countConflict}</strong></span>
            ${wasCommit ? `<span style="color:#166534">Eingefügt: <strong>${res.countInserted}</strong></span><span style="color:#1e40af">Aktualisiert: <strong>${res.countUpdated}</strong></span>` : ''}
        </div>`;

    // Tabelle: pro MA eine Hauptzeile + Detail (Diffs) auf-/zuklappbar
    const rows = (res.rows||[]).map((r, idx) => {
        const pill = {
            NEW:        '<span class="eaw-pill eaw-pill-new">NEW</span>',
            UPDATE:     '<span class="eaw-pill" style="background:#dbeafe;color:#1e40af">UPDATE</span>',
            UNCHANGED:  '<span class="eaw-pill eaw-pill-soft">UNCHANGED</span>',
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
        return `<tr>
            <td>${cb}</td>
            <td>${pill}</td>
            <td>${escapeHtml((r.firstName||'') + ' ' + (r.lastName||''))} <span style="color:#94a3b8">(${escapeHtml(r.number||'-')})</span></td>
            <td style="font-size:11px;color:#475569">${changesSummary}</td>
            <td><a href="#" onclick="event.preventDefault();_eawEmpToggle('${detailId}')" style="color:#1e40af;font-size:12px">Details</a></td>
            <td style="color:#94a3b8;font-size:11px">${escapeHtml(r.reason||'')}</td>
        </tr>${detailRow}`;
    }).join('');

    out.innerHTML = `
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
