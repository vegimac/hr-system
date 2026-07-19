// ════════════════════════════════════════════════════════════════════════
//  Warnungsverwaltung (Walter-Vorgabe 06.07.2026 / Priorität+Farbe 19.07.2026)
//  Globale Konfiguration der Dashboard-/ToDo-Warnungen. Pro Warnung:
//    • an/aus
//    • Vorlauf in Tagen (nur bei datums-basierten Warnungen)
//    • „Kritisch ab (Tage)" = Eskalations-Schwelle (nur datums-basiert)
//    • Schweregrad (Basis + eskaliert)
//    • Priorität (kleinere Zahl = weiter oben in der ToDo-Liste)
//    • Warnfarbe (Standard / immer rot / rot wenn abgelaufen)
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
        wcRender();
    } catch (e) {
        if (cont) cont.innerHTML = `<div style="padding:24px;color:#dc2626;font-size:13px">Verbindungsfehler: ${escapeHtml(e.message)}</div>`;
    }
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

function wcRender() {
    const cont = document.getElementById('wcContainer');
    if (!cont) return;
    const info = document.getElementById('wcInfo');
    if (info) info.textContent = `${_wcRows.length} Warnungen`;

    if (!_wcRows.length) {
        cont.innerHTML = '<div style="padding:24px;color:#8b8b8b;font-size:13px">Keine Warnungen konfiguriert.</div>';
        return;
    }

    const rowsHtml = _wcRows.map((c, i) => {
        const dateBased = !!c.isDateBased;
        const warnCell = dateBased
            ? `<input type="number" min="0" value="${c.warnDays == null ? '' : c.warnDays}"
                   onchange="wcSet(${i},'warnDays',this.value)"
                   style="width:70px;padding:6px 8px;border:1px solid #d9d3ca;border-radius:8px;background:#faf8f5;font-size:13px;color:#3f3f3f">`
            : '<span style="color:#b8b0a4;font-size:12px">—</span>';
        const escCell = dateBased
            ? `<input type="number" min="0" value="${c.escalateDays == null ? '' : c.escalateDays}"
                   onchange="wcSet(${i},'escalateDays',this.value)"
                   style="width:70px;padding:6px 8px;border:1px solid #d9d3ca;border-radius:8px;background:#faf8f5;font-size:13px;color:#3f3f3f">`
            : '<span style="color:#b8b0a4;font-size:12px">—</span>';
        const escSevSel = `
            <select onchange="wcSet(${i},'severityEscalated',this.value)"
                    style="padding:6px 8px;border:1px solid #d9d3ca;border-radius:8px;background:#faf8f5;font-size:13px;color:#3f3f3f">
                <option value=""${!c.severityEscalated ? ' selected' : ''}>keine</option>
                ${wcSeverityOptions(c.severityEscalated || '')}
            </select>`;
        const prio = c.todoPriority == null ? 100 : c.todoPriority;
        const color = c.warnColor || 'none';

        return `
        <tr style="border-bottom:1px solid #ece7df">
            <td style="padding:10px 12px;font-size:13px;color:#3f3f3f;font-weight:600">${escapeHtml(c.label || c.category)}</td>
            <td style="padding:10px 12px;text-align:center">
                <label style="display:inline-flex;align-items:center;cursor:pointer">
                    <input type="checkbox" ${c.enabled ? 'checked' : ''}
                           onchange="wcSet(${i},'enabled',this.checked)" style="cursor:pointer;width:16px;height:16px">
                </label>
            </td>
            <td style="padding:10px 12px;text-align:center">
                <input type="number" min="0" max="9999" value="${prio}"
                       onchange="wcSet(${i},'todoPriority',this.value)"
                       title="Kleinere Zahl = weiter oben in der ToDo-Liste"
                       style="width:64px;padding:6px 8px;border:1px solid #d9d3ca;border-radius:8px;background:#faf8f5;font-size:13px;color:#3f3f3f">
            </td>
            <td style="padding:10px 12px;text-align:center">
                <select onchange="wcSet(${i},'warnColor',this.value)"
                        title="Titel-Farbe in der ToDo-Liste"
                        style="padding:6px 8px;border:1px solid #d9d3ca;border-radius:8px;background:#faf8f5;font-size:13px;color:#3f3f3f;max-width:160px">
                    ${wcColorOptions(color)}
                </select>
            </td>
            <td style="padding:10px 12px;text-align:center">${warnCell}</td>
            <td style="padding:10px 12px;text-align:center">${escCell}</td>
            <td style="padding:10px 12px;text-align:center">
                <select onchange="wcSet(${i},'severityBase',this.value)"
                        style="padding:6px 8px;border:1px solid #d9d3ca;border-radius:8px;background:#faf8f5;font-size:13px;color:#3f3f3f">
                    ${wcSeverityOptions(c.severityBase)}
                </select>
            </td>
            <td style="padding:10px 12px;text-align:center">${dateBased ? escSevSel : '<span style="color:#b8b0a4;font-size:12px">—</span>'}</td>
        </tr>`;
    }).join('');

    cont.innerHTML = `
    <div style="background:rgba(255,255,255,0.5);border:1px solid rgba(255,255,255,0.62);border-radius:14px;box-shadow:0 6px 20px rgba(60,55,48,0.14);overflow:hidden;overflow-x:auto">
        <table style="width:100%;border-collapse:collapse;min-width:920px">
            <thead>
                <tr style="background:#f6f3ee;text-align:left">
                    <th style="padding:10px 12px;font-size:12px;color:#646464;font-weight:600">Warnung</th>
                    <th style="padding:10px 12px;font-size:12px;color:#646464;font-weight:600;text-align:center">Aktiv</th>
                    <th style="padding:10px 12px;font-size:12px;color:#646464;font-weight:600;text-align:center" title="Kleinere Zahl = weiter oben in der ToDo-Liste">Priorität</th>
                    <th style="padding:10px 12px;font-size:12px;color:#646464;font-weight:600;text-align:center" title="Titel-Farbe: Standard (schwarz), immer rot, oder nur wenn abgelaufen">Warnfarbe</th>
                    <th style="padding:10px 12px;font-size:12px;color:#646464;font-weight:600;text-align:center" title="Vorlauf in Tagen — ab wie vielen Tagen vor dem Ereignis gewarnt wird.">Vorlauf (Tage)</th>
                    <th style="padding:10px 12px;font-size:12px;color:#646464;font-weight:600;text-align:center" title="Ab diesem Rest-Tageswert wird der eskalierte Schweregrad verwendet.">Kritisch ab (Tage)</th>
                    <th style="padding:10px 12px;font-size:12px;color:#646464;font-weight:600;text-align:center">Schweregrad</th>
                    <th style="padding:10px 12px;font-size:12px;color:#646464;font-weight:600;text-align:center">Eskaliert</th>
                </tr>
            </thead>
            <tbody>${rowsHtml}</tbody>
        </table>
    </div>
    <p style="margin-top:12px;font-size:12px;color:#8b8b8b">
        <b>Priorität:</b> kleinere Zahl = weiter oben in der ToDo-Liste.
        <b>Warnfarbe:</b> «Standard» = schwarzer Titel · «Immer rot» · «Rot wenn abgelaufen» (nur bei negativem Rest-Tage-Wert).
        Datums-basierte Warnungen haben zusätzlich Vorlauf und Eskalation.
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
    } else if (field === 'todoPriority') {
        const n = parseInt(String(value).trim(), 10);
        c.todoPriority = Number.isFinite(n) ? Math.max(0, Math.min(9999, n)) : 100;
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
