// ════════════════════════════════════════════════════════════════════════
//  Warnungsverwaltung (Walter-Vorgabe 06.07.2026 / Priorität+Farbe 19.07.2026)
//  Globale Konfiguration der Dashboard-/ToDo-Warnungen. Pro Warnung:
//    • an/aus
//    • Reihenfolge per ▲/▼ (= ToDo-Reihenfolge)
//    • Warnfarbe (Standard / immer rot / rot wenn abgelaufen)
//    • Vorlauf / Eskalation / Schweregrad
//  Liquid-Glass-Optik, ein globaler Speichern-Button (Bulk-PUT).
// ════════════════════════════════════════════════════════════════════════

let _wcRows = [];

const WC_SEVERITIES = [
    { v: 'info',     l: 'Info' },
    { v: 'warning',  l: 'Wichtig' },
    { v: 'critical', l: 'Kritisch' }
];

const WC_WARN_COLORS = [
    { v: 'none',         l: 'Standard' },
    { v: 'red',          l: 'Immer rot' },
    { v: 'red_overdue',  l: 'Rot wenn abgelaufen' }
];

const WC_INP = 'width:58px;padding:3px 6px;border:1px solid #d9d3ca;border-radius:6px;background:#faf8f5;font-size:12px;color:#3f3f3f';
const WC_SEL = 'padding:3px 6px;border:1px solid #d9d3ca;border-radius:6px;background:#faf8f5;font-size:12px;color:#3f3f3f';
const WC_TD  = 'padding:4px 8px;font-size:12.5px;vertical-align:middle';
const WC_TH  = 'padding:6px 8px;font-size:11px;color:#646464;font-weight:600';

async function wcInit() {
    const cont = document.getElementById('wcContainer');
    if (cont) cont.innerHTML = '<div style="padding:24px;color:#8b8b8b;font-size:13px">Lade Warnungen…</div>';
    try {
        const r = await fetch('/api/dashboard-warning-config', { headers: ah() });
        if (!r.ok) {
            cont.innerHTML = `<div style="padding:24px;color:#dc2626;font-size:13px">Konnte Warnungs-Konfiguration nicht laden (HTTP ${r.status}). Wurde die Migration <code>add_dashboard_warning_priority_color.sql</code> ausgeführt?</div>`;
            return;
        }
        _wcRows = await r.json() || [];
        // Sicherstellen: Anzeige-Reihenfolge = Priorität (Server liefert schon sortiert).
        _wcRows.sort((a, b) => (a.todoPriority ?? 100) - (b.todoPriority ?? 100)
            || (a.sortOrder ?? 0) - (b.sortOrder ?? 0));
        wcSyncPriorities();
        wcRender();
    } catch (e) {
        if (cont) cont.innerHTML = `<div style="padding:24px;color:#dc2626;font-size:13px">Verbindungsfehler: ${escapeHtml(e.message)}</div>`;
    }
}

/** Zeilenreihenfolge → todoPriority (10, 20, 30 …) = ToDo-Reihenfolge. */
function wcSyncPriorities() {
    _wcRows.forEach((c, i) => { c.todoPriority = (i + 1) * 10; });
}

function wcMove(idx, delta) {
    const j = idx + delta;
    if (j < 0 || j >= _wcRows.length) return;
    const tmp = _wcRows[idx];
    _wcRows[idx] = _wcRows[j];
    _wcRows[j] = tmp;
    wcSyncPriorities();
    wcRender();
}

function wcSeverityOptions(selected) {
    return WC_SEVERITIES.map(s =>
        `<option value="${s.v}"${s.v === selected ? ' selected' : ''}>${s.l}</option>`
    ).join('');
}

function wcColorOptions(selected) {
    return WC_WARN_COLORS.map(s =>
        `<option value="${s.v}"${s.v === selected ? ' selected' : ''}>${s.l}</option>`
    ).join('');
}

function wcMoveBtn(idx, delta, label, title) {
    const disabled = (delta < 0 && idx === 0) || (delta > 0 && idx === _wcRows.length - 1);
    return `<button type="button" onclick="wcMove(${idx},${delta})" title="${title}"
        ${disabled ? 'disabled' : ''}
        style="width:26px;height:22px;padding:0;border:1px solid #d9d3ca;border-radius:6px;background:${disabled ? '#f0ebe4' : '#faf8f5'};color:${disabled ? '#c4bdb3' : '#3f3f3f'};font-size:11px;font-weight:700;cursor:${disabled ? 'default' : 'pointer'};line-height:1">${label}</button>`;
}

function wcRender() {
    const cont = document.getElementById('wcContainer');
    if (!cont) return;
    const info = document.getElementById('wcInfo');
    if (info) info.textContent = `${_wcRows.length} Warnungen · Reihenfolge = ToDo`;

    if (!_wcRows.length) {
        cont.innerHTML = '<div style="padding:24px;color:#8b8b8b;font-size:13px">Keine Warnungen konfiguriert.</div>';
        return;
    }

    const rowsHtml = _wcRows.map((c, i) => {
        const dateBased = !!c.isDateBased;
        const warnCell = dateBased
            ? `<input type="number" min="0" value="${c.warnDays == null ? '' : c.warnDays}"
                   onchange="wcSet(${i},'warnDays',this.value)" style="${WC_INP}">`
            : '<span style="color:#b8b0a4;font-size:11px">—</span>';
        const escCell = dateBased
            ? `<input type="number" min="0" value="${c.escalateDays == null ? '' : c.escalateDays}"
                   onchange="wcSet(${i},'escalateDays',this.value)" style="${WC_INP}">`
            : '<span style="color:#b8b0a4;font-size:11px">—</span>';
        const escSevSel = `
            <select onchange="wcSet(${i},'severityEscalated',this.value)" style="${WC_SEL}">
                <option value=""${!c.severityEscalated ? ' selected' : ''}>keine</option>
                ${wcSeverityOptions(c.severityEscalated || '')}
            </select>`;
        const color = c.warnColor || 'none';

        return `
        <tr style="border-bottom:1px solid #ece7df">
            <td style="${WC_TD};width:56px;text-align:center">
                <div style="display:inline-flex;flex-direction:column;gap:2px">
                    ${wcMoveBtn(i, -1, '▲', 'Nach oben (weiter vorne in ToDo)')}
                    ${wcMoveBtn(i, 1, '▼', 'Nach unten (weiter hinten in ToDo)')}
                </div>
            </td>
            <td style="${WC_TD};color:#3f3f3f;font-weight:600;white-space:nowrap">
                <span style="color:#b8b0a4;font-weight:500;font-size:11px;margin-right:6px">${i + 1}.</span>${escapeHtml(c.label || c.category)}
            </td>
            <td style="${WC_TD};text-align:center">
                <label style="display:inline-flex;align-items:center;cursor:pointer">
                    <input type="checkbox" ${c.enabled ? 'checked' : ''}
                           onchange="wcSet(${i},'enabled',this.checked)" style="cursor:pointer;width:15px;height:15px">
                </label>
            </td>
            <td style="${WC_TD};text-align:center">
                <select onchange="wcSet(${i},'warnColor',this.value)"
                        title="Titel-Farbe in der ToDo-Liste" style="${WC_SEL};max-width:148px">
                    ${wcColorOptions(color)}
                </select>
            </td>
            <td style="${WC_TD};text-align:center">${warnCell}</td>
            <td style="${WC_TD};text-align:center">${escCell}</td>
            <td style="${WC_TD};text-align:center">
                <select onchange="wcSet(${i},'severityBase',this.value)" style="${WC_SEL}">
                    ${wcSeverityOptions(c.severityBase)}
                </select>
            </td>
            <td style="${WC_TD};text-align:center">${dateBased ? escSevSel : '<span style="color:#b8b0a4;font-size:11px">—</span>'}</td>
        </tr>`;
    }).join('');

    cont.innerHTML = `
    <div style="background:rgba(255,255,255,0.5);border:1px solid rgba(255,255,255,0.62);border-radius:14px;box-shadow:0 6px 20px rgba(60,55,48,0.14);overflow:hidden;overflow-x:auto">
        <table style="width:100%;border-collapse:collapse;min-width:880px">
            <thead>
                <tr style="background:#f6f3ee;text-align:left">
                    <th style="${WC_TH};text-align:center" title="Reihenfolge = ToDo-Liste">↕</th>
                    <th style="${WC_TH}">Warnung</th>
                    <th style="${WC_TH};text-align:center">Aktiv</th>
                    <th style="${WC_TH};text-align:center" title="Titel-Farbe in der ToDo-Liste">Warnfarbe</th>
                    <th style="${WC_TH};text-align:center" title="Vorlauf in Tagen">Vorlauf</th>
                    <th style="${WC_TH};text-align:center" title="Ab diesem Rest-Tageswert eskaliert">Kritisch ab</th>
                    <th style="${WC_TH};text-align:center">Schweregrad</th>
                    <th style="${WC_TH};text-align:center">Eskaliert</th>
                </tr>
            </thead>
            <tbody>${rowsHtml}</tbody>
        </table>
    </div>
    <p style="margin-top:10px;font-size:12px;color:#8b8b8b;line-height:1.45">
        Mit ▲/▼ verschieben — <b>diese Reihenfolge ist die ToDo-Reihenfolge</b> (nach Speichern).
        Warnfarbe: Standard (schwarz) · Immer rot · Rot wenn abgelaufen.
    </p>`;
}

function wcSet(idx, field, value) {
    const c = _wcRows[idx];
    if (!c) return;
    if (field === 'enabled') {
        c.enabled = !!value;
    } else if (field === 'warnDays' || field === 'escalateDays') {
        const s = String(value).trim();
        c[field] = (s === '') ? null : Math.max(0, parseInt(s, 10) || 0);
    } else if (field === 'warnColor') {
        c.warnColor = value || 'none';
    } else if (field === 'severityEscalated') {
        c.severityEscalated = value ? value : null;
    } else if (field === 'severityBase') {
        c.severityBase = value;
    }
}

async function wcSave() {
    const btn = document.getElementById('wcSaveBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Speichere…'; }
    try {
        wcSyncPriorities();
        const payload = _wcRows.map(c => ({
            id: c.id,
            enabled: !!c.enabled,
            warnDays: c.warnDays,
            escalateDays: c.escalateDays,
            severityBase: c.severityBase,
            severityEscalated: c.severityEscalated || null,
            todoPriority: c.todoPriority == null ? 100 : c.todoPriority,
            warnColor: c.warnColor || 'none'
        }));
        const r = await fetch('/api/dashboard-warning-config', {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (!r.ok) {
            let msg = `HTTP ${r.status}`;
            try { const j = await r.json(); if (j && j.message) msg = j.message; } catch (e) {}
            alert('Speichern fehlgeschlagen: ' + msg);
            return;
        }
        if (typeof toast === 'function') toast('Warnungen gespeichert');
        else if (typeof showToast === 'function') showToast('Warnungen gespeichert');
        await wcInit();
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'Speichern'; }
    }
}
