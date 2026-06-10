// Walter-Vorgabe 27.05.2026: Admin-Sicht aufs zentrale Audit-Log.
// Filterbar nach Datum, User, Entity-Typ, Aktion + Volltext + CSV-Export.

let _alState = {
    rows: [],
    entityTypes: [],
    users: [],
};

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
    // Default: letzte 7 Tage
    const today = new Date();
    const weekAgo = new Date(today.getTime() - 7 * 86400000);
    const iso = d => d.toISOString().slice(0, 10);
    document.getElementById('alFrom').value = iso(weekAgo);
    document.getElementById('alTo').value   = iso(today);
    alLoad();
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
    if (mount) mount.innerHTML = '<div style="padding:30px;text-align:center;color:#94a3b8">Lade…</div>';
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

function alActionBadge(action) {
    const styles = {
        CREATE: { bg: '#dcfce7', color: '#166534', label: '+ NEU' },
        UPDATE: { bg: '#dbeafe', color: '#1e40af', label: '✎ ÄND.' },
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
    const keys = Object.keys(obj);
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
    if (!mount) return;
    if (!_alState.rows.length) {
        mount.innerHTML = '<div style="padding:30px;text-align:center;color:#94a3b8">Keine Einträge gefunden.</div>';
        return;
    }
    let html = `
    <div style="padding:6px 12px;font-size:12px;color:#64748b">${_alState.rows.length} Einträge — neueste zuerst.</div>
    <div style="overflow-x:auto">
    <table style="width:100%;border-collapse:collapse;font-size:12px">
        <thead>
            <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0">
                <th style="text-align:left;padding:6px 8px;font-size:10.5px;font-weight:700;color:#475569;text-transform:uppercase;letter-spacing:.06em;white-space:nowrap">Zeit</th>
                <th style="text-align:left;padding:6px 8px;font-size:10.5px;font-weight:700;color:#475569;text-transform:uppercase;letter-spacing:.06em">User</th>
                <th style="text-align:left;padding:6px 8px;font-size:10.5px;font-weight:700;color:#475569;text-transform:uppercase;letter-spacing:.06em">Aktion</th>
                <th style="text-align:left;padding:6px 8px;font-size:10.5px;font-weight:700;color:#475569;text-transform:uppercase;letter-spacing:.06em">Entität</th>
                <th style="text-align:left;padding:6px 8px;font-size:10.5px;font-weight:700;color:#475569;text-transform:uppercase;letter-spacing:.06em">Änderungen</th>
                <th style="text-align:right;padding:6px 8px"></th>
            </tr>
        </thead>
        <tbody>`;
    _alState.rows.forEach(r => {
        html += `
        <tr style="border-bottom:1px solid #f1f5f9;vertical-align:top">
            <td style="padding:6px 8px;white-space:nowrap;color:#0f172a">${alFmtTime(r.createdAt)}</td>
            <td style="padding:6px 8px">
                <div style="font-weight:600;color:#0f172a">${esc(r.userName || ('#' + (r.userId ?? '?')))}</div>
                <div style="font-size:11px;color:#94a3b8">${esc(r.userRole || '')}${r.route ? ' · ' + esc(r.route) : ''}</div>
            </td>
            <td style="padding:6px 8px;white-space:nowrap">${alActionBadge(r.action)}</td>
            <td style="padding:6px 8px;white-space:nowrap">
                <div style="font-weight:600;color:#0f172a">${esc(r.entityType)}</div>
                <div style="font-size:11px;color:#64748b;font-family:ui-monospace,Menlo,Consolas,monospace">${esc(r.entityId || '–')}</div>
            </td>
            <td style="padding:6px 8px;line-height:1.5;color:#334155">${alChangesSummary(r.changesJson, r.action)}</td>
            <td style="padding:6px 8px;text-align:right">
                <button onclick="alShowDetail(${r.id})" title="Vollen JSON-Diff zeigen" style="background:#fff;border:1px solid #cbd5e1;border-radius:4px;padding:3px 8px;font-size:11px;cursor:pointer">Detail</button>
            </td>
        </tr>`;
    });
    html += '</tbody></table></div>';
    mount.innerHTML = html;
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
        const filename = `audit-log_${new Date().toISOString().slice(0,10)}.csv`;
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
    document.getElementById('alFrom').value = '';
    document.getElementById('alTo').value   = '';
    document.getElementById('alUserSel').value   = '';
    document.getElementById('alEntitySel').value = '';
    document.getElementById('alActionSel').value = '';
    document.getElementById('alSearch').value    = '';
    document.getElementById('alLimit').value     = '200';
    alLoad();
}
