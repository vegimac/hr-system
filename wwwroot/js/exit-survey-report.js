// ═══════════════════════════════════════════════════════════════════════════
//  Austritts-Feedback (anonym) — Walter 26.07.2026
//  HR-Hub → Auswertungen / Reporting → Austritts-Feedback
//  Endpoint: GET /api/exit-survey
//  Anonym = kein MA-Name; Gründe + Bemerkung sichtbar.
//  Layout: eine Zeile pro Feedback (Walter 26.07.2026).
// ═══════════════════════════════════════════════════════════════════════════

const _ES_REASON_LABELS = {
    // OneCrew-Kurzliste (ab 26.07.2026)
    NEUER_JOB:          'Neuer Job',
    SCHULE_STUDIUM:     'Schule / Studium',
    ZU_VIELE_EINSAETZE: 'Zu viele Einsätze',
    ZU_WENIG_EINSAETZE: 'Zu wenig Einsätze',
    PASST_NICHT_MEHR:   'Es hat für mich nicht mehr gepasst',
    ETWAS_ANDERES:      'Etwas anderes',
    // Historische Codes
    ANDERER_JOB:      'Andere Stelle im Fachgebiet',
    STUDIUM:          'Studium',
    ZU_VIELE_STUNDEN: 'Zu viele Stunden',
    ZU_WENIG_STUNDEN: 'Zu wenig Stunden',
    ARBEITSZEITEN:    'Arbeitszeiten / Verfügbarkeit',
    GASTRONOMIE:      'Gastronomie nicht das Richtige',
    ENTWICKLUNG:      'Keine Entwicklungsmöglichkeiten',
    FAMILIE:          'Familiäre / nicht berufliche Gründe',
    ATMOSPHAERE:      'Atmosphäre / Organisation',
    LOHN:             'Gehalt',
    ANDERES:          'Anderer Grund',
};

let _esRows = [];

function esInit() {
    esLoad();
}

async function esLoad() {
    const box = document.getElementById('esResult');
    if (!box) return;
    box.innerHTML = '<div style="padding:20px;color:#8b8b8b">Lade Feedback…</div>';
    try {
        const res = await fetch('/api/exit-survey?take=500', { headers: ah() });
        if (!res.ok) {
            box.innerHTML = `<div style="padding:20px;color:#b91c1c">Fehler beim Laden (${res.status}).</div>`;
            return;
        }
        _esRows = await res.json();
        if (!Array.isArray(_esRows)) _esRows = [];
        esFillFilialeFilter();
        esRender();
    } catch {
        box.innerHTML = '<div style="padding:20px;color:#b91c1c">Netzwerkfehler beim Laden.</div>';
    }
}

function esFillFilialeFilter() {
    const sel = document.getElementById('esFiliale');
    if (!sel) return;
    const cur = sel.value || '';
    const codes = new Map();
    for (const r of _esRows) {
        const key = r.filialeCode || r.FilialeCode || '';
        if (!key) continue;
        if (!codes.has(key)) codes.set(key, r.filiale || r.Filiale || key);
    }
    const sorted = [...codes.entries()].sort((a, b) =>
        String(a[1]).localeCompare(String(b[1]), 'de'));
    sel.innerHTML = '<option value="">— alle Filialen —</option>'
        + sorted.map(([c, lbl]) =>
            `<option value="${esEsc(c)}">${esEsc(lbl)}</option>`).join('');
    if (cur && [...sel.options].some(o => o.value === cur)) sel.value = cur;
}

function esEsc(s) {
    return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;')
        .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function esFmtDate(iso) {
    if (!iso) return '—';
    const d = String(iso);
    const m = d.match(/^(\d{4})-(\d{2})-(\d{2})/);
    if (m) return `${m[3]}.${m[2]}.${m[1]}`;
    try { return new Date(d).toLocaleDateString('de-CH'); } catch { return d; }
}

/** Gründe als Klartext-Liste — API liefert `reasons[]`, Fallback über reasonsJson. */
function esReasonsOf(r) {
    if (Array.isArray(r.reasons) && r.reasons.length) {
        return r.reasons.map(x => String(x || '').trim()).filter(Boolean);
    }
    if (Array.isArray(r.Reasons) && r.Reasons.length) {
        return r.Reasons.map(x => String(x || '').trim()).filter(Boolean);
    }
    const json = r.reasonsJson ?? r.ReasonsJson ?? '[]';
    let codes = [];
    try {
        const arr = typeof json === 'string' ? JSON.parse(json || '[]') : (json || []);
        if (Array.isArray(arr)) codes = arr;
    } catch { codes = []; }
    const labels = codes.map(c => _ES_REASON_LABELS[c] || _ES_REASON_LABELS[String(c).toUpperCase()] || c);
    const other = r.reasonOther || r.ReasonOther;
    if (other && String(other).trim()) labels.push(String(other).trim());
    return labels.filter(Boolean);
}

function esBemerkungText(r) {
    const atm = String(r.atmosphereDetail || r.AtmosphereDetail || '').trim();
    const comment = String(r.comment || r.Comment || '').trim();
    const parts = [];
    if (atm) parts.push(atm);
    if (comment) parts.push(comment);
    return parts.join('\n\n');
}

function esRender() {
    const box = document.getElementById('esResult');
    if (!box) return;
    const filter = document.getElementById('esFiliale')?.value || '';
    const rows = filter
        ? _esRows.filter(r => (r.filialeCode || r.FilialeCode || '') === filter)
        : _esRows;

    const withRating = rows.filter(r => (r.rating ?? r.Rating) != null);
    const avg = withRating.length
        ? (withRating.reduce((s, r) => s + (+(r.rating ?? r.Rating) || 0), 0) / withRating.length)
        : null;

    const kpi = (label, value, hint) => `
        <div class="es-kpi">
            <div class="es-kpi-label">${label}</div>
            <div class="es-kpi-value">${value}</div>
            ${hint ? `<div class="es-kpi-hint">${hint}</div>` : ''}
        </div>`;

    let html = `<div class="es-kpi-row">
        ${kpi('Antworten', rows.length, filter ? 'gefiltert' : 'gesamt')}
        ${kpi('Ø Note', avg != null ? avg.toFixed(1) : '—', withRating.length ? `${withRating.length} mit Note` : 'keine Noten')}
        ${kpi('Mit Filiale', rows.filter(r => r.companyProfileId || r.CompanyProfileId || r.filialeCode || r.FilialeCode).length, 'aus QR / Auswahl')}
    </div>`;

    if (!rows.length) {
        html += `<div class="es-empty">
            Noch keine anonymen Feedbacks${filter ? ' für diese Filiale' : ''}.<br>
            <span>Sie entstehen, wenn austretende MA den QR auf der Kündigungsbestätigung scannen.</span>
        </div>`;
        box.innerHTML = html;
        return;
    }

    // Eine Zeile pro Feedback. Gründe-Spalte: width:1% + nowrap → so breit wie
    // der längste Grund über alle Zeilen; Bemerkung direkt rechts daneben.
    // KEIN overflow-Wrapper (bricht sticky / verdeckt Zeilen).
    html += `<div class="es-table-card">
        <table class="es-table">
            <thead>
                <tr>
                    <th>Datum</th>
                    <th>Filiale</th>
                    <th class="es-th-note">Note</th>
                    <th>Gründe</th>
                    <th>Bemerkung</th>
                </tr>
            </thead>
            <tbody>`;

    for (const r of rows) {
        const reasons = esReasonsOf(r);
        const bemerkung = esBemerkungText(r);
        const filiale = r.filiale || r.Filiale || '—';
        const rating = r.rating ?? r.Rating;
        const created = esFmtDate(r.createdAt || r.CreatedAt);

        const reasonsHtml = reasons.length
            ? reasons.map(x => `<div class="es-reason">${esEsc(x)}</div>`).join('')
            : '<span class="es-muted">—</span>';

        const noteHtml = rating != null
            ? `<span class="es-note">${esEsc(rating)}</span>`
            : '<span class="es-muted">—</span>';

        html += `<tr>
            <td class="es-td-date">${esEsc(created)}</td>
            <td class="es-td-filiale">${esEsc(filiale)}</td>
            <td class="es-td-note">${noteHtml}</td>
            <td class="es-td-gruende">${reasonsHtml}</td>
            <td class="es-td-bemerkung">${bemerkung ? esEsc(bemerkung) : '<span class="es-muted">—</span>'}</td>
        </tr>`;
    }

    html += `</tbody></table></div>
        <div class="es-foot">
            Anonym — kein Mitarbeitername · Filiale aus dem QR der Kündigungsbestätigung (oder manuelle Wahl).
        </div>`;

    box.innerHTML = html;
    if (typeof fixheadSyncStickyOffset === 'function') fixheadSyncStickyOffset();
}
