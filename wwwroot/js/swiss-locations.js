// ══════════════════════════════════════════════════════════════════════
// swiss-locations.js — PLZ-/Ortschaften-Pflege
// ──────────────────────────────────────────────────────────────────────
// Ort = Post-Ortschaft (AMTOVZ), Gemeinde = politische Gemeinde (BFS).
// Walter 29.07.2026: Ortschaft getrennt anzeigen (z.B. Bützberg ≠ Thunstetten).
//
// Backend: GET /api/swiss-locations/admin (alle, ohne q)
//          POST/PUT/DELETE /api/swiss-locations/admin[/{id}]
// ══════════════════════════════════════════════════════════════════════
let _locCache = [];
let _locSortState = { key: 'plz4', dir: 'asc' };
let _locSearchTimer = null;
const _LOC_RENDER_LIMIT = 500;  // DOM-Grenze — bei >500 Treffern Hinweis

async function locInit() {
    document.getElementById('locAlert').innerHTML = '';
    document.getElementById('locSearch').value = '';
    document.getElementById('locList').innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const r = await fetch('/api/swiss-locations/admin', { headers: ah() });
        if (!r.ok) {
            document.getElementById('locList').innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
            return;
        }
        const data = await r.json();
        _locCache = data.items || [];
        locRender();
    } catch (e) {
        document.getElementById('locList').innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler: ' + _e(e.message) + '</span></div>';
    }
}

function locSearch() {
    // Debounce 150ms — Filter ist clientseitig, also schnell.
    clearTimeout(_locSearchTimer);
    _locSearchTimer = setTimeout(locRender, 150);
}

function locRender() {
    const search = (document.getElementById('locSearch')?.value || '').trim().toLowerCase();
    const isAdmin = currentUser?.role === 'admin';

    let rows = _locCache.slice();
    if (search) {
        rows = rows.filter(l =>
            (l.plz4 || '').toLowerCase().includes(search)
         || (l.ortschaftsname || '').toLowerCase().includes(search)
         || (l.gemeindename || '').toLowerCase().includes(search)
         || (l.kantonskuerzel || '').toLowerCase() === search
         || String(l.bfsNr || '').includes(search));
    }
    window.sortableApply(rows, _locSortState);

    const total = rows.length;
    const shown = rows.slice(0, _LOC_RENDER_LIMIT);

    const el = document.getElementById('locList');
    if (total === 0) {
        el.innerHTML = '<div class="emp-placeholder"><span>Keine Treffer für diese Suche.</span></div>';
        return;
    }

    const cappedNote = total > _LOC_RENDER_LIMIT
        ? `<div style="padding:8px 12px;background:#fef3c7;border:1px solid #fbbf24;border-radius:6px;color:#92400e;font-size:12px;margin-bottom:10px">
               ${total} Treffer — die ersten ${_LOC_RENDER_LIMIT} werden gezeigt. Suche präzisieren um den Rest zu sehen.
           </div>`
        : `<div style="font-size:11.5px;color:#64748b;margin-bottom:8px">${total} Treffer</div>`;

    el.innerHTML = `
        ${cappedNote}
        <div class="card" style="padding:0;overflow:auto">
            <table style="width:100%;border-collapse:collapse;font-size:13px">
                <thead>
                    <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0">
                        ${window.sortableHeader('PLZ',        'plz4',           _locSortState, '_locSortState', 'locRender', 'width:90px')}
                        ${window.sortableHeader('Ortschaft',  'ortschaftsname', _locSortState, '_locSortState', 'locRender')}
                        ${window.sortableHeader('Gemeinde',   'gemeindename',   _locSortState, '_locSortState', 'locRender')}
                        ${window.sortableHeader('BFS-Nr',     'bfsNr',          _locSortState, '_locSortState', 'locRender', 'width:110px')}
                        ${window.sortableHeader('Kanton',     'kantonskuerzel', _locSortState, '_locSortState', 'locRender', 'width:90px')}
                        ${isAdmin ? '<th style="padding:9px 12px;text-align:right;width:110px;color:#475569">Aktion</th>' : ''}
                    </tr>
                </thead>
                <tbody>
                    ${shown.map(l => `
                        <tr style="border-bottom:1px solid #f1f5f9">
                            <td style="padding:8px 12px;font-family:monospace;font-weight:600;color:#0f172a">${_e(l.plz4)}</td>
                            <td style="padding:8px 12px;color:#0f172a;font-weight:600">${_e(l.ortschaftsname || l.gemeindename)}</td>
                            <td style="padding:8px 12px;color:#64748b">${_e(l.gemeindename)}</td>
                            <td style="padding:8px 12px;font-family:monospace;color:#64748b">${l.bfsNr}</td>
                            <td style="padding:8px 12px;font-family:monospace;font-weight:600">${_e(l.kantonskuerzel)}</td>
                            ${isAdmin
                                ? `<td style="padding:8px 12px;text-align:right;white-space:nowrap">
                                       <button class="btn-emp-add" onclick="locOpenEdit(${l.id})" style="padding:4px 8px;font-size:12px">✎</button>
                                       <button class="btn-emp-add" onclick="locDelete(${l.id})" style="padding:4px 8px;font-size:12px;background:#fee2e2;border-color:#fca5a5;color:#991b1b">🗑</button>
                                   </td>`
                                : ''}
                        </tr>
                    `).join('')}
                </tbody>
            </table>
        </div>`;
}

function locOpenAdd() {
    locOpenModal(null);
}
function locOpenEdit(id) {
    const e = _locCache.find(x => x.id === id);
    if (!e) return;
    locOpenModal(e);
}

function locOpenModal(entry) {
    const isEdit = entry !== null;
    let modal = document.getElementById('locModal');
    if (!modal) {
        modal = document.createElement('div');
        modal.id = 'locModal';
        modal.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,0.45);z-index:300;display:flex;align-items:center;justify-content:center;padding:20px';
        document.body.appendChild(modal);
    }
    modal.innerHTML = `
        <div style="background:#fff;border-radius:14px;max-width:460px;width:100%;padding:22px 24px">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:14px">
                <h3 style="margin:0;font-size:18px;color:#0f172a">${isEdit ? 'PLZ-Eintrag bearbeiten' : 'Neuer PLZ-Eintrag'}</h3>
                <button onclick="locCloseModal()" style="background:none;border:none;font-size:22px;color:#94a3b8;cursor:pointer">×</button>
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px 14px">
                <div>
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">PLZ *</label>
                    <input type="text" id="locF-plz" maxlength="4" value="${entry ? _e(entry.plz4) : ''}"
                           style="width:100%;padding:9px 11px;border:1px solid #cbd5e1;border-radius:7px;font-size:13.5px;font-family:monospace">
                </div>
                <div>
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">BFS-Nr *</label>
                    <input type="number" id="locF-bfs" value="${entry ? entry.bfsNr : ''}"
                           style="width:100%;padding:9px 11px;border:1px solid #cbd5e1;border-radius:7px;font-size:13.5px;font-family:monospace">
                </div>
                <div style="grid-column:1 / -1">
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">Ortschaft (Adress-Ort) *</label>
                    <input type="text" id="locF-ort" value="${entry ? _e(entry.ortschaftsname || entry.gemeindename) : ''}"
                           style="width:100%;padding:9px 11px;border:1px solid #cbd5e1;border-radius:7px;font-size:13.5px">
                </div>
                <div style="grid-column:1 / -1">
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">Politische Gemeinde</label>
                    <input type="text" id="locF-gem" value="${entry ? _e(entry.gemeindename) : ''}"
                           style="width:100%;padding:9px 11px;border:1px solid #cbd5e1;border-radius:7px;font-size:13.5px">
                </div>
                <div>
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">Kanton *</label>
                    <input type="text" id="locF-kt" maxlength="2" value="${entry ? _e(entry.kantonskuerzel) : ''}"
                           style="width:100%;padding:9px 11px;border:1px solid #cbd5e1;border-radius:7px;font-size:13.5px;font-family:monospace;text-transform:uppercase">
                </div>
            </div>
            <div style="display:flex;gap:8px;justify-content:flex-end;margin-top:18px;padding-top:14px;border-top:1px solid #e2e8f0">
                <button onclick="locCloseModal()"
                        style="padding:9px 16px;border:1px solid #cbd5e1;border-radius:7px;background:#fff;color:#475569;cursor:pointer;font-size:13px">Abbrechen</button>
                <button class="btn-primary" onclick="locSave(${entry ? entry.id : 'null'})"
                        style="padding:9px 18px;font-size:13px">Speichern</button>
            </div>
            <div id="locF-error" style="margin-top:10px;color:#b91c1c;font-size:12.5px"></div>
        </div>`;
    modal.style.display = 'flex';
}

function locCloseModal() {
    const m = document.getElementById('locModal');
    if (m) m.style.display = 'none';
}

async function locSave(id) {
    const dto = {
        plz4:           document.getElementById('locF-plz').value.trim(),
        ortschaftsname: document.getElementById('locF-ort').value.trim(),
        gemeindename:   document.getElementById('locF-gem').value.trim(),
        bfsNr:          parseInt(document.getElementById('locF-bfs').value) || 0,
        kantonskuerzel: document.getElementById('locF-kt').value.trim().toUpperCase()
    };
    const errEl = document.getElementById('locF-error');
    errEl.textContent = '';
    if (!dto.plz4 || dto.plz4.length < 4) { errEl.textContent = 'PLZ muss 4 Zeichen haben.'; return; }
    if (!dto.ortschaftsname)               { errEl.textContent = 'Ortschaft ist Pflicht.'; return; }
    if (!dto.gemeindename) dto.gemeindename = dto.ortschaftsname;
    if (!dto.bfsNr)                        { errEl.textContent = 'BFS-Nr muss > 0 sein.'; return; }
    if (dto.kantonskuerzel.length !== 2)   { errEl.textContent = 'Kanton-Kürzel = genau 2 Zeichen (z.B. ZH).'; return; }

    try {
        const url = id ? `/api/swiss-locations/admin/${id}` : '/api/swiss-locations/admin';
        const method = id ? 'PUT' : 'POST';
        const r = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            errEl.textContent = j.error || ('Fehler ' + r.status);
            return;
        }
        const saved = await r.json();
        // Cache aktualisieren — neuer Eintrag anhängen oder bestehenden ersetzen.
        const idx = _locCache.findIndex(x => x.id === saved.id);
        if (idx >= 0) _locCache[idx] = saved;
        else          _locCache.push(saved);
        locCloseModal();
        showPageAlert('locAlert', '✓ ' + saved.plz4 + ' ' + (saved.ortschaftsname || saved.gemeindename) + ' gespeichert.', 'success');
        locRender();
    } catch (e) {
        errEl.textContent = 'Verbindungsfehler: ' + e.message;
    }
}

async function locDelete(id) {
    const e = _locCache.find(x => x.id === id);
    if (!e) return;
    if (!confirm(`Soll der Eintrag "${e.plz4} ${e.ortschaftsname || e.gemeindename}" wirklich gelöscht werden?`)) return;
    try {
        const r = await fetch(`/api/swiss-locations/admin/${id}`, { method: 'DELETE', headers: ah() });
        if (!r.ok && r.status !== 204) {
            const j = await r.json().catch(() => ({}));
            showPageAlert('locAlert', 'Fehler: ' + (j.error || r.status), 'error');
            return;
        }
        _locCache = _locCache.filter(x => x.id !== id);
        showPageAlert('locAlert', '✓ Gelöscht.', 'success');
        locRender();
    } catch (e) {
        showPageAlert('locAlert', 'Verbindungsfehler: ' + e.message, 'error');
    }
}
