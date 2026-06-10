// ══════════════════════════════════════════════════════════════════════
// nationen.js — Nationalitäten-Stammdaten-Pflege
// ──────────────────────────────────────────────────────────────────────
// ISO-3166-Codes plus optionaler Alternativ-Code (Code2) für abweichende
// Drittsystem-Codes (z.B. Mirus „XZ" für Kosovo). Aktiv-Flag kann hier
// auch umgeschaltet werden — gesperrte Nationen verschwinden aus der
// MA-Dropdown-Liste.
//
// Backend: GET /api/nationalities/admin (admin/superuser)
//          PATCH /api/nationalities/{id} (admin)
// ══════════════════════════════════════════════════════════════════════
let _natCache = [];
let _natSortState = { key: 'code', dir: 'asc' };

async function natInit() {
    document.getElementById('natAlert').innerHTML = '';
    document.getElementById('natSearch').value = '';
    document.getElementById('natShowInactive').checked = false;
    document.getElementById('natList').innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const r = await fetch('/api/nationalities/admin', { headers: ah() });
        if (!r.ok) {
            document.getElementById('natList').innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>';
            return;
        }
        _natCache = await r.json();
        natRender();
    } catch (e) {
        document.getElementById('natList').innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler: ' + _e(e.message) + '</span></div>';
    }
}

function natRender() {
    const search = (document.getElementById('natSearch')?.value || '').trim().toLowerCase();
    const showInactive = document.getElementById('natShowInactive')?.checked === true;
    const isAdmin = currentUser?.role === 'admin';

    let rows = (_natCache || []).filter(n => showInactive || n.isActive);
    if (search) {
        rows = rows.filter(n =>
            (n.code || '').toLowerCase().includes(search)
         || (n.code2 || '').toLowerCase().includes(search)
         || (n.name || '').toLowerCase().includes(search));
    }
    window.sortableApply(rows, _natSortState);

    const el = document.getElementById('natList');
    if (rows.length === 0) {
        el.innerHTML = '<div class="emp-placeholder"><span>Keine Nationen für diese Suche.</span></div>';
        return;
    }

    el.innerHTML = `
        <div class="card" style="padding:0;overflow:auto">
            <table style="width:100%;border-collapse:collapse;font-size:13px">
                <thead>
                    <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0">
                        ${window.sortableHeader('Code',     'code',     _natSortState, '_natSortState', 'natRender', 'width:90px')}
                        ${window.sortableHeader('Code 2',   'code2',    _natSortState, '_natSortState', 'natRender', 'width:140px')}
                        ${window.sortableHeader('Name',     'name',     _natSortState, '_natSortState', 'natRender')}
                        ${window.sortableHeader('Aktiv',    'isActive', _natSortState, '_natSortState', 'natRender', 'width:120px')}
                        ${isAdmin ? '<th style="padding:9px 12px;text-align:right;width:100px;color:#475569">Aktion</th>' : ''}
                    </tr>
                </thead>
                <tbody>
                    ${rows.map(n => `
                        <tr style="border-bottom:1px solid #f1f5f9" data-natid="${n.id}">
                            <td style="padding:8px 12px;font-family:monospace;font-weight:600;color:#0f172a">${_e(n.code)}</td>
                            <td style="padding:8px 12px">
                                ${isAdmin
                                    ? `<input type="text" id="nat-code2-${n.id}" value="${_e(n.code2 || '')}" maxlength="4"
                                              style="width:90px;padding:5px 8px;border:1px solid #cbd5e1;border-radius:5px;font-family:monospace;font-size:12.5px;text-transform:uppercase">`
                                    : `<span style="font-family:monospace;color:#64748b">${_e(n.code2 || '–')}</span>`}
                            </td>
                            <td style="padding:8px 12px;color:#475569">${_e(n.name)}</td>
                            <td style="padding:8px 12px">
                                ${isAdmin
                                    ? `<label style="display:inline-flex;align-items:center;gap:6px;cursor:pointer">
                                           <input type="checkbox" id="nat-active-${n.id}" ${n.isActive ? 'checked' : ''}>
                                           <span style="font-size:11.5px;color:#64748b">${n.isActive ? 'aktiv' : 'inaktiv'}</span>
                                       </label>`
                                    : (n.isActive
                                        ? '<span style="background:#dcfce7;color:#166534;padding:2px 8px;border-radius:9px;font-size:11px;font-weight:600">aktiv</span>'
                                        : '<span style="background:#fee2e2;color:#991b1b;padding:2px 8px;border-radius:9px;font-size:11px;font-weight:600">inaktiv</span>')}
                            </td>
                            ${isAdmin
                                ? `<td style="padding:8px 12px;text-align:right">
                                       <button class="btn-emp-add" onclick="natSave(${n.id})" style="padding:5px 10px;font-size:12px">Speichern</button>
                                   </td>`
                                : ''}
                        </tr>
                    `).join('')}
                </tbody>
            </table>
        </div>`;
}

async function natSave(id) {
    const code2El  = document.getElementById('nat-code2-' + id);
    const activeEl = document.getElementById('nat-active-' + id);
    if (!code2El) return;
    const dto = {
        code2:    code2El.value.trim().toUpperCase(),
        isActive: activeEl ? activeEl.checked : null
    };
    try {
        const r = await fetch('/api/nationalities/' + id, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            showPageAlert('natAlert', 'Fehler: ' + (j.message || j.error || r.status), 'error');
            return;
        }
        const saved = await r.json();
        // Cache aktualisieren
        const idx = _natCache.findIndex(n => n.id === id);
        if (idx >= 0) {
            _natCache[idx].code2    = saved.code2;
            _natCache[idx].isActive = saved.isActive;
        }
        showPageAlert('natAlert', `✓ ${saved.code} gespeichert.`, 'success');
        // Subtile Anzeige-Aktualisierung — kompletter Re-Render wäre overkill.
        natRender();
    } catch (e) {
        showPageAlert('natAlert', 'Verbindungsfehler: ' + e.message, 'error');
    }
}
