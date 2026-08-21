// ════════════════════════════════════════════════════════════════════════
//  Warnungsverwaltung (Walter-Vorgabe 06.07.2026 / Priorität+Farbe 19.07.2026)
//  Globale Konfiguration der Dashboard-/ToDo-Warnungen. Pro Warnung:
//    • an/aus
//    • Reihenfolge per Drag-and-Drop (Vierpfeil-Griff = ToDo-Reihenfolge)
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
    const tbody = document.getElementById('wcTableBody');
    if (tbody) tbody.innerHTML = '<tr><td colspan="8" style="padding:24px;color:#8b8b8b;font-size:13px;text-align:center">Lade Warnungen…</td></tr>';
    try {
        const r = await fetch('/api/dashboard-warning-config', { headers: ah() });
        if (!r.ok) {
            if (tbody) tbody.innerHTML = `<tr><td colspan="8" style="padding:24px;color:#dc2626;font-size:13px">Konnte Warnungs-Konfiguration nicht laden (HTTP ${r.status}).</td></tr>`;
            return;
        }
        _wcRows = await r.json() || [];
        // Sicherstellen: Anzeige-Reihenfolge = Priorität (Server liefert schon sortiert).
        _wcRows.sort((a, b) => (a.todoPriority ?? 100) - (b.todoPriority ?? 100)
            || (a.sortOrder ?? 0) - (b.sortOrder ?? 0));
        wcSyncPriorities();
        await wcSchulLoad();   // Schulungs-Gültigkeit VOR dem Render (Unterzeile)
        wcRender();
    } catch (e) {
        if (tbody) tbody.innerHTML = `<tr><td colspan="8" style="padding:24px;color:#dc2626;font-size:13px">Verbindungsfehler: ${escapeHtml(e.message)}</td></tr>`;
    }
}

/** Zeilenreihenfolge → todoPriority (10, 20, 30 …) = ToDo-Reihenfolge. */
function wcSyncPriorities() {
    _wcRows.forEach((c, i) => { c.todoPriority = (i + 1) * 10; });
}

let _wcDragFrom = null;

function wcDragStart(ev, idx) {
    _wcDragFrom = idx;
    ev.dataTransfer.effectAllowed = 'move';
    ev.dataTransfer.setData('text/plain', String(idx));
    const tr = ev.target.closest('tr');
    if (tr) {
        // Kurzer Delay, damit der Drag-Ghost die Zeile noch sichtbar hat.
        setTimeout(() => { tr.style.opacity = '0.45'; }, 0);
    }
}
function wcDragEnd(ev) {
    _wcDragFrom = null;
    document.querySelectorAll('#wcContainer tr[data-wc-idx]').forEach(tr => {
        tr.style.opacity = '';
        tr.style.background = '';
        tr.style.outline = '';
    });
}
function wcDragOver(ev, idx) {
    ev.preventDefault();
    ev.dataTransfer.dropEffect = 'move';
    document.querySelectorAll('#wcContainer tr[data-wc-idx]').forEach(tr => {
        tr.style.background = '';
        tr.style.outline = '';
    });
    const tr = ev.currentTarget;
    if (tr) {
        tr.style.background = 'rgba(59,130,246,0.08)';
        tr.style.outline = '1px dashed #93c5fd';
    }
}
function wcDrop(ev, toIdx) {
    ev.preventDefault();
    const from = _wcDragFrom != null ? _wcDragFrom : parseInt(ev.dataTransfer.getData('text/plain'), 10);
    if (!Number.isFinite(from) || from === toIdx || from < 0 || toIdx < 0
        || from >= _wcRows.length || toIdx >= _wcRows.length) {
        wcDragEnd();
        return;
    }
    const [row] = _wcRows.splice(from, 1);
    _wcRows.splice(toIdx, 0, row);
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

/** Vierpfeil-Griff (Move-Symbol) — Zeile ziehen. */
function wcDragHandle(idx) {
    return `<span class="wc-drag-handle" draggable="true"
        ondragstart="wcDragStart(event,${idx})" ondragend="wcDragEnd(event)"
        title="Ziehen zum Sortieren (ToDo-Reihenfolge)"
        style="display:inline-flex;align-items:center;justify-content:center;width:28px;height:28px;border:1px solid #d9d3ca;border-radius:8px;background:#faf8f5;cursor:grab;user-select:none;touch-action:none"
        onmousedown="this.style.cursor='grabbing'" onmouseup="this.style.cursor='grab'">
        <svg width="16" height="16" viewBox="0 0 24 24" aria-hidden="true" fill="#475569">
            <path d="M12 2 L15.5 6.5 H13 V10.5 H17 V8.5 L21.5 12 L17 15.5 V13.5 H13 V17.5 H15.5 L12 22 L8.5 17.5 H11 V13.5 H7 V15.5 L2.5 12 L7 8.5 V10.5 H11 V6.5 H8.5 Z"/>
        </svg>
    </span>`;
}

function wcRender() {
    // Spaltenköpfe sitzen im fixen Kopf (HTML); hier nur Datenzeilen (Walter 22.07.2026).
    const tbody = document.getElementById('wcTableBody');
    if (!tbody) return;
    const info = document.getElementById('wcInfo');
    if (info) info.textContent = `${_wcRows.length} Warnungen · Reihenfolge = ToDo`;

    if (!_wcRows.length) {
        tbody.innerHTML = '<tr><td colspan="8" style="padding:24px;color:#8b8b8b;font-size:13px;text-align:center">Keine Warnungen konfiguriert.</td></tr>';
        return;
    }

    tbody.innerHTML = _wcRows.map((c, i) => {
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
        <tr data-wc-idx="${i}" style="border-bottom:1px solid #ece7df"
            ondragover="wcDragOver(event,${i})" ondrop="wcDrop(event,${i})">
            <td style="${WC_TD};text-align:center">${wcDragHandle(i)}</td>
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
        </tr>${c.category === 'schulung_peak' ? wcSchulSubRow() : ''}`;
    }).join('');
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
        // Schulungs-Gültigkeit mitspeichern (Unterzeile, Walter 21.08.2026).
        await wcSchulSaveIfChanged();
        if (typeof toast === 'function') toast('Warnungen gespeichert');
        else if (typeof showToast === 'function') showToast('Warnungen gespeichert');
        await wcInit();
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'Speichern'; }
    }
}

// ── Schulungs-Gültigkeit (Walter 21.08.2026, integriert in die Liste) ───
// Keine eigene Karte: die Monate erscheinen als Unterzeile direkt an der
// Warnung «Schulung Peak-Verifizierung läuft ab» (wcRender) und werden mit
// dem globalen Speichern-Button mitgespeichert (wcSave).
let _wcSchulMonate = null;

async function wcSchulLoad() {
    try {
        const r = await fetch('/api/manager-schulungen/settings', { headers: ah() });
        if (r.ok) _wcSchulMonate = await r.json();
    } catch (_) { _wcSchulMonate = null; }
}

function wcSchulSet(key, val) {
    if (!_wcSchulMonate) return;
    const n = parseInt(val, 10);
    _wcSchulMonate[key] = Number.isFinite(n) && n >= 1 ? n : _wcSchulMonate[key];
}

/** Unterzeile unter der Schulungs-Warnung: Gültigkeit pro Schulung in Monaten. */
function wcSchulSubRow() {
    if (!_wcSchulMonate) return '';
    const isAdmin = typeof currentUser !== 'undefined' && currentUser?.role === 'admin';
    const dis = isAdmin ? '' : ' disabled';
    const inp = (key, label, val) => `
        <label style="display:inline-flex;align-items:center;gap:5px;font-size:11.5px;color:#8b8b8b">${label}
            <input type="number" min="1" max="240" value="${val}" style="${WC_INP}"${dis}
                   onchange="wcSchulSet('${key}', this.value)"></label>`;
    return `
        <tr style="border-bottom:1px solid #ece7df;background:rgba(255,255,255,0.35)">
            <td style="${WC_TD}"></td>
            <td colspan="7" style="${WC_TD};padding-top:2px">
                <span style="font-size:11.5px;color:#8b8b8b;margin-right:10px"
                      title="Ab dem erfassten Schulungsdatum gilt die Schulung so viele Monate — daraus rechnen sich die «bis»-Daten, die Ampeln in der Manager-Schulungs-Liste und diese Ablauf-Warnung.">
                    ↳ Gültigkeit (Monate):</span>
                ${inp('nothelferMonate', 'Nothelfer', _wcSchulMonate.nothelferMonate)}
                <span style="margin:0 8px;color:#d5d0c6">·</span>
                ${inp('peakMonate', 'Peak-Verif.', _wcSchulMonate.peakMonate)}
                <span style="margin:0 8px;color:#d5d0c6">·</span>
                ${inp('secoMonate', 'Seco', _wcSchulMonate.secoMonate)}
            </td>
        </tr>`;
}

/** Wird von wcSave nach dem Haupt-PUT aufgerufen (admin-only, best-effort). */
async function wcSchulSaveIfChanged() {
    if (!_wcSchulMonate) return;
    const isAdmin = typeof currentUser !== 'undefined' && currentUser?.role === 'admin';
    if (!isAdmin) return;
    try {
        await fetch('/api/manager-schulungen/settings', {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(_wcSchulMonate)
        });
    } catch (_) { /* best-effort — Warnungs-Save ist wichtiger */ }
}
