// Walter-Vorgabe 27.05.2026: Admin-Sicht aufs zentrale Audit-Log.
// Filterbar nach Datum, User, Entity-Typ, Aktion + Volltext + CSV-Export.
// Spaltentitel sitzen im sticky Filter-Kopf (Walter 26.07.2026) — kein sticky-thead.

let _alState = {
    rows: [],
    entityTypes: [],
    users: [],
};

/** Lokales Kalenderdatum yyyy-MM-dd — NICHT toISOString (UTC-Verschiebung). */
function alLocalIsoDate(d) {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
}

async function alInit() {
    // User-Liste fuer Filter-Dropdown vorladen
    try {
        const r = await fetch('/api/users', { headers: ah() });
        if (r.ok) _alState.users = await r.json();
    } catch (_) {}
    try {
        const r = await fetch('/api/audit-log/entity-types', { headers: ah() });
        if (r.ok) _alState.entityTypes = await r.json();
    } catch (_) {}
    alRenderFilters();
    // Default: letzte 7 Tage (Schweizer Lokaldatum)
    const today = new Date();
    const weekAgo = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 7);
    document.getElementById('alFrom').value = alLocalIsoDate(weekAgo);
    document.getElementById('alTo').value   = alLocalIsoDate(today);
    alLoadHealth();
    alLoad();
}

/** Roter Banner, wenn audit_log länger als die Schwelle nichts schreibt. */
async function alLoadHealth() {
    const el = document.getElementById('alHealthBanner');
    if (!el) return;
    try {
        const r = await fetch('/api/audit-log/health', { headers: ah() });
        if (!r.ok) { el.style.display = 'none'; return; }
        const h = await r.json();
        if (h && h.ok === false) {
            el.style.display = '';
            el.textContent = '⚠ ' + (h.message || 'Aktivitäts-Log schreibt nicht mehr.');
        } else {
            el.style.display = 'none';
            el.textContent = '';
        }
    } catch (_) {
        el.style.display = 'none';
    }
}

function alRenderFilters() {
    const userSel = document.getElementById('alUserSel');
    if (userSel) {
        userSel.innerHTML = '<option value="">– alle User –</option>'
            + _alState.users
                .sort((a, b) => (a.username || '').localeCompare(b.username || ''))
                .map(u => `<option value="${u.id}">${esc(u.username)} (${esc(u.role)})</option>`)
                .join('');
    }
    const etSel = document.getElementById('alEntitySel');
    if (etSel) {
        etSel.innerHTML = '<option value="">– alle Entitaeten –</option>'
            + _alState.entityTypes.map(t => `<option value="${esc(t)}">${esc(t)}</option>`).join('');
    }
}

async function alLoad() {
    const params = alBuildParams();
    const mount = document.getElementById('alResults');
    const countEl = document.getElementById('alCount');
    if (mount) mount.innerHTML = '<div style="padding:30px;text-align:center;color:#94a3b8">Lade…</div>';
    if (countEl) countEl.textContent = '';
    alLoadHealth();
    try {
        const r = await fetch('/api/audit-log?' + params.toString(), { headers: ah() });
        if (!r.ok) {
            mount.innerHTML = '<div style="padding:30px;text-align:center;color:#dc2626">Fehler beim Laden (HTTP ' + r.status + ')</div>';
            return;
        }
        _alState.rows = await r.json();
        alRenderResults();
    } catch (err) {
        mount.innerHTML = '<div style="padding:30px;text-align:center;color:#dc2626">Verbindungsfehler: ' + err.message + '</div>';
    }
}

function alBuildParams() {
    const p = new URLSearchParams();
    const from = document.getElementById('alFrom')?.value;
    const to   = document.getElementById('alTo')?.value;
    const user = document.getElementById('alUserSel')?.value;
    const et   = document.getElementById('alEntitySel')?.value;
    const act  = document.getElementById('alActionSel')?.value;
    const q    = document.getElementById('alSearch')?.value;
    const lim  = document.getElementById('alLimit')?.value || '200';
    if (from) p.set('from', from);
    if (to)   p.set('to',   to);
    if (user) p.set('userId',     user);
    if (et)   p.set('entityType', et);
    if (act)  p.set('action',     act);
    if (q)    p.set('search',     q);
    p.set('limit', lim);
    return p;
}

function alFmtTime(iso) {
    if (!iso) return '–';
    const d = new Date(iso);
    if (isNaN(d)) return iso;
    return d.toLocaleString('de-CH', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

function alFmtDay(iso) {
    if (!iso) return '–';
    const d = new Date(iso);
    if (isNaN(d)) return String(iso).slice(0, 10);
    return d.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

function alActionBadge(action) {
    const styles = {
        CREATE: { bg: '#dcfce7', color: '#166534', label: '+ NEU' },
        UPDATE: { bg: '#ece9e2', color: '#6b6152', label: '✎ ÄND.' },
        DELETE: { bg: '#fee2e2', color: '#991b1b', label: '✕ LÖSCH' },
    };
    const s = styles[action] || { bg: '#f1f5f9', color: '#475569', label: action };
    return `<span style="display:inline-block;padding:2px 8px;border-radius:3px;background:${s.bg};color:${s.color};font-size:10.5px;font-weight:700;white-space:nowrap">${s.label}</span>`;
}

function alChangesSummary(changesJson, action) {
    if (!changesJson) return '<span style="color:#94a3b8">–</span>';
    let obj;
    try { obj = JSON.parse(changesJson); } catch { return '<span style="color:#94a3b8">(unleserlich)</span>'; }
    if (!obj || typeof obj !== 'object') return '<span style="color:#94a3b8">–</span>';
    // Adress-Felder zuerst — sonst verschwinden sie hinter «+ N weitere».
    const prefer = ['Street', 'HouseNumber', 'Zip', 'ZipCode', 'City', 'CantonCode'];
    const keys = Object.keys(obj).sort((a, b) => {
        const ia = prefer.indexOf(a), ib = prefer.indexOf(b);
        if (ia >= 0 && ib >= 0) return ia - ib;
        if (ia >= 0) return -1;
        if (ib >= 0) return 1;
        return 0;
    });
    if (action === 'UPDATE') {
        // Nur die geaenderten Felder anzeigen (jeder Wert ist { old, new })
        const parts = keys.slice(0, 4).map(k => {
            const v = obj[k];
            if (v && typeof v === 'object' && 'new' in v) {
                const oldV = v.old === null || v.old === undefined ? '<i style="color:#94a3b8">leer</i>' : esc(String(v.old).slice(0, 60));
                const newV = v.new === null || v.new === undefined ? '<i style="color:#94a3b8">leer</i>' : esc(String(v.new).slice(0, 60));
                return `<b>${esc(k)}</b>: ${oldV} → ${newV}`;
            }
            return `<b>${esc(k)}</b>: ${esc(String(v).slice(0, 60))}`;
        });
        const more = keys.length > 4 ? ` <span style="color:#94a3b8">+ ${keys.length - 4} weitere</span>` : '';
        return parts.join('<br>') + more;
    }
    // CREATE / DELETE — top-Level Felder kurz
    const parts = keys.slice(0, 6).map(k => `<b>${esc(k)}</b>: ${esc(String(obj[k]).slice(0, 40))}`);
    const more = keys.length > 6 ? ` <span style="color:#94a3b8">+ ${keys.length - 6} weitere</span>` : '';
    return parts.join(' · ') + more;
}

function alRenderResults() {
    const mount = document.getElementById('alResults');
    const countEl = document.getElementById('alCount');
    if (!mount) return;

    if (!_alState.rows.length) {
        if (countEl) countEl.textContent = 'Keine Einträge für diesen Filter.';
        mount.innerHTML = '<div style="padding:30px;text-align:center;color:#94a3b8">Keine Einträge gefunden.</div>';
        if (typeof fixheadSyncStickyOffset === 'function') fixheadSyncStickyOffset();
        return;
    }

    const newest = _alState.rows[0]?.createdAt;
    const oldest = _alState.rows[_alState.rows.length - 1]?.createdAt;
    const lim = document.getElementById('alLimit')?.value || '200';
    const toFilter = document.getElementById('alTo')?.value || '';
    const newestDay = newest ? String(newest).slice(0, 10) : '';
    // Sortierung = neueste zuerst: die erste Zeile IST das neueste Audit in der DB
    // (für diesen Filter). Fehlt heute/gestern, wurde nichts geschrieben — das Limit
    // versteckt keine neueren Tage (Walter-Bug-Warnung 26.07.2026 korrigiert).
    let countTxt = `${_alState.rows.length} Einträge — neueste zuerst`
        + ` · in Liste: ${alFmtDay(newest)} → ${alFmtDay(oldest)}`;
    if (toFilter && newestDay && newestDay < toFilter) {
        countTxt += ` · ⚠ Neuestes Audit erst ${alFmtDay(newest)} — danach wurde nichts mehr protokolliert (nicht das Limit)`;
    } else if (String(_alState.rows.length) === String(lim)) {
        countTxt += ` · Limit ${lim} — ältere Einträge ausgeblendet (Filter/Limit erhöhen)`;
    }
    if (countEl) countEl.textContent = countTxt;

    // Kein overflow-x Wrapper — bricht sticky/fixhead (Walter 26.07.2026).
    let html = `
    <table class="al-data">
        <colgroup>
            <col class="al-col-zeit"><col class="al-col-user"><col class="al-col-aktion">
            <col class="al-col-entity"><col class="al-col-changes"><col class="al-col-detail">
        </colgroup>
        <thead><tr>
            <th>Zeit</th><th>User</th><th>Aktion</th><th>Entität</th><th>Änderungen</th><th></th>
        </tr></thead>
        <tbody>`;
    _alState.rows.forEach(r => {
        html += `
        <tr style="border-bottom:1px solid #f1f5f9;vertical-align:top">
            <td style="padding:6px 8px;white-space:nowrap;color:#0f172a">${alFmtTime(r.createdAt)}</td>
            <td style="padding:6px 8px">
                <div style="font-weight:600;color:#0f172a">${esc(r.userName || ('#' + (r.userId ?? '?')))}</div>
                <div style="font-size:11px;color:#94a3b8;word-break:break-all">${esc(r.userRole || '')}${r.route ? ' · ' + esc(r.route) : ''}</div>
            </td>
            <td style="padding:6px 8px;white-space:nowrap">${alActionBadge(r.action)}</td>
            <td style="padding:6px 8px">
                <div style="font-weight:600;color:#0f172a">${esc(r.entityType)}</div>
                ${r.employeeNumber || r.employeeName
                    ? `<div style="font-size:12px;font-weight:700;color:#0f172a">${esc(r.employeeNumber || '')}${r.employeeName ? ' · ' + esc(r.employeeName) : ''}</div>
                       <div style="font-size:10.5px;color:#94a3b8;font-family:ui-monospace,Menlo,Consolas,monospace">id ${esc(r.entityId || '–')}</div>`
                    : `<div style="font-size:11px;color:#64748b;font-family:ui-monospace,Menlo,Consolas,monospace">${esc(r.entityId || '–')}</div>`}
            </td>
            <td style="padding:6px 8px;line-height:1.5;color:#334155">${alChangesSummary(r.changesJson, r.action)}</td>
            <td style="padding:6px 8px;text-align:right">
                <button onclick="alShowDetail(${r.id})" title="Vollen JSON-Diff zeigen" style="background:#fff;border:1px solid #cbd5e1;border-radius:4px;padding:3px 8px;font-size:11px;cursor:pointer">Detail</button>
            </td>
        </tr>`;
    });
    html += '</tbody></table>';
    mount.innerHTML = html;
    if (typeof fixheadSyncStickyOffset === 'function') fixheadSyncStickyOffset();
}

function alShowDetail(id) {
    const r = _alState.rows.find(x => x.id === id);
    if (!r) return;
    let pretty;
    try { pretty = JSON.stringify(JSON.parse(r.changesJson || '{}'), null, 2); }
    catch { pretty = r.changesJson || ''; }
    const html = `
    <div id="alDetailModal" style="position:fixed;inset:0;background:rgba(15,23,42,.45);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px"
         onclick="if(event.target===this)document.getElementById('alDetailModal').remove()">
      <div class="ma-modal-box" style="max-width:880px">
        <div class="ma-modal-head">
            <div>
                <div class="ma-modal-title">Audit-Eintrag #${r.id}</div>
                <div class="ma-modal-sub">${alFmtTime(r.createdAt)} · ${esc(r.userName || '')} · ${alActionBadge(r.action)} ${esc(r.entityType)} ${esc(r.entityId || '')}</div>
            </div>
            <button class="ma-modal-close" onclick="document.getElementById('alDetailModal').remove()">✕</button>
        </div>
        <div class="ma-modal-body">
            <div class="emp-section-title">Kontext</div>
            <div class="ma-grid cols-2" style="margin-bottom:6px">
                <div class="ma-field">
                    <div class="ma-field-label">Route</div>
                    <div class="emp-field-value" style="font-family:ui-monospace,Menlo,Consolas,monospace">${esc(r.route || '–')}</div>
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">IP</div>
                    <div class="emp-field-value">${esc(r.ipAddress || '–')}</div>
                </div>
            </div>
            <div class="emp-section-title">Änderungen (JSON)</div>
            <pre style="background:#0f172a;color:#e2e8f0;padding:14px;border-radius:4px;font-size:11.5px;line-height:1.5;white-space:pre-wrap;overflow:auto;max-height:50vh">${esc(pretty)}</pre>
        </div>
        <div class="ma-modal-foot">
            <button class="btn btn-primary" onclick="document.getElementById('alDetailModal').remove()">Schliessen</button>
        </div>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

async function alExportCsv() {
    const params = alBuildParams();
    params.delete('limit'); // Export hat eigenes 50k-Cap server-seitig
    try {
        const r = await fetch('/api/audit-log/export?' + params.toString(), { headers: ah() });
        if (!r.ok) { alert('Export fehlgeschlagen (HTTP ' + r.status + ')'); return; }
        const blob = await r.blob();
        const filename = `audit-log_${alLocalIsoDate(new Date())}.csv`;
        if (typeof saveBlobAsk === 'function') saveBlobAsk(blob, filename);
        else {
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url; a.download = filename; a.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        }
    } catch (err) { alert('Export fehlgeschlagen: ' + err.message); }
}

function alReset() {
    const today = new Date();
    const weekAgo = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 7);
    document.getElementById('alFrom').value = alLocalIsoDate(weekAgo);
    document.getElementById('alTo').value   = alLocalIsoDate(today);
    document.getElementById('alUserSel').value   = '';
    document.getElementById('alEntitySel').value = '';
    document.getElementById('alActionSel').value = '';
    document.getElementById('alSearch').value    = '';
    document.getElementById('alLimit').value     = '200';
    alLoad();
}

/** Schnellfilter: nur Strassen-Änderungen an MA (Walter 26.07.2026). */
function alFilterStreet() {
    const today = new Date();
    const weekAgo = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 7);
    document.getElementById('alFrom').value = alLocalIsoDate(weekAgo);
    document.getElementById('alTo').value   = alLocalIsoDate(today);
    document.getElementById('alEntitySel').value = 'Employee';
    document.getElementById('alActionSel').value = 'UPDATE';
    document.getElementById('alSearch').value    = 'Strasse';
    document.getElementById('alLimit').value     = '1000';
    alLoad();
}
