// ═══════════════════════════════════════════════════════════════════════════
//  Austritts-Feedback (anonym) — Walter 26.07.2026
//  HR-Hub → Auswertungen / Reporting → Austritts-Feedback
//  Endpoint: GET /api/exit-survey
// ═══════════════════════════════════════════════════════════════════════════

const _ES_REASON_LABELS = {
    STARTE_NEUES:       'Ich starte etwas Neues',
    SCHULE_PLAENE:      'Schule, Studium oder persönliche Pläne',
    WENIGER_EINSAETZE:  'Ich wollte weniger Einsätze',
    MEHR_EINSAETZE:     'Ich hätte gerne mehr Einsätze gehabt',
    ARBEIT_PASST_NICHT: 'Etwas bei der Arbeit hat nicht mehr gepasst',
    ETWAS_ANDERES:      'Etwas anderes',
    NEUER_JOB:          'Neuer Job',
    SCHULE_STUDIUM:     'Schule / Studium',
    ZU_VIELE_EINSAETZE: 'Zu viele Einsätze',
    ZU_WENIG_EINSAETZE: 'Zu wenig Einsätze',
    PASST_NICHT_MEHR:   'Es hat für mich nicht mehr gepasst',
    ANDERER_JOB:        'Andere Stelle im Fachgebiet',
    STUDIUM:            'Studium',
    ZU_VIELE_STUNDEN:   'Zu viele Stunden',
    ZU_WENIG_STUNDEN:   'Zu wenig Stunden',
    ARBEITSZEITEN:      'Arbeitszeiten / Verfügbarkeit',
    GASTRONOMIE:        'Gastronomie nicht das Richtige',
    ENTWICKLUNG:        'Keine Entwicklungsmöglichkeiten',
    FAMILIE:            'Familiäre / nicht berufliche Gründe',
    ATMOSPHAERE:        'Atmosphäre / Organisation',
    LOHN:               'Gehalt',
    ANDERES:            'Anderer Grund',
};

const _ES_IMPROVE_LABELS = {
    JA:   'Ja, da gibt es etwas',
    NEIN: 'Nein, für mich war es einfach Zeit für etwas Neues',
};

const _ES_THEME_LABELS = {
    FUEHRUNG:         'Führung',
    TEAMGEFUEHL:      'Teamgefühl',
    PLANUNG_ORG:      'Planung und Organisation',
    ARBEITSZEITEN:    'Arbeitszeiten',
    UNTERSTUETZUNG:   'Unterstützung und Wertschätzung',
    ENTWICKLUNG:      'Entwicklungsmöglichkeiten',
    LOHN_BEDINGUNGEN: 'Lohn und Bedingungen',
    THEMA_ANDERES:    'Etwas anderes',
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

function esReasonsOf(r) {
    if (Array.isArray(r.reasons) && r.reasons.length) {
        return r.reasons.map(x => String(x || '').trim()).filter(Boolean);
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

function esImproveOf(r) {
    const label = r.improveAnswerLabel
        || _ES_IMPROVE_LABELS[r.improveAnswer]
        || _ES_IMPROVE_LABELS[r.ImproveAnswer]
        || null;
    let themes = [];
    if (Array.isArray(r.improveThemes) && r.improveThemes.length) {
        themes = r.improveThemes.map(x => String(x || '').trim()).filter(Boolean);
    } else {
        const json = r.improveThemesJson ?? r.ImproveThemesJson ?? '[]';
        try {
            const arr = typeof json === 'string' ? JSON.parse(json || '[]') : (json || []);
            if (Array.isArray(arr)) {
                themes = arr.map(c => _ES_THEME_LABELS[c] || _ES_THEME_LABELS[String(c).toUpperCase()] || c);
            }
        } catch { themes = []; }
    }
    return { label, themes };
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

    const withImproveJa = rows.filter(r => (r.improveAnswer || r.ImproveAnswer) === 'JA').length;

    const kpi = (label, value, hint) => `
        <div class="es-kpi">
            <div class="es-kpi-label">${label}</div>
            <div class="es-kpi-value">${value}</div>
            ${hint ? `<div class="es-kpi-hint">${hint}</div>` : ''}
        </div>`;

    let html = `<div class="es-kpi-row">
        ${kpi('Antworten', rows.length, filter ? 'gefiltert' : 'gesamt')}
        ${kpi('Mit Themen', withImproveJa, '«Ja, da gibt es etwas»')}
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

    html += `<div class="es-table-card">
        <table class="es-table">
            <thead>
                <tr>
                    <th>Datum</th>
                    <th>Filiale</th>
                    <th>Note</th>
                    <th>Entscheid</th>
                    <th>Besser werden</th>
                    <th>Feedback</th>
                </tr>
            </thead>
            <tbody>`;

    for (const r of rows) {
        const reasons = esReasonsOf(r);
        const improve = esImproveOf(r);
        const bemerkung = esBemerkungText(r);
        const filiale = r.filiale || r.Filiale || '—';
        const created = esFmtDate(r.createdAt || r.CreatedAt);
        const note = r.rating ?? r.Rating;
        const noteHtml = (note != null && note >= 1 && note <= 6)
            ? `<span class="es-note">${esEsc(note)}</span>`
            : '<span class="es-muted">—</span>';

        const reasonsHtml = reasons.length
            ? reasons.map(x => `<div class="es-reason">${esEsc(x)}</div>`).join('')
            : '<span class="es-muted">—</span>';

        let improveHtml = '<span class="es-muted">—</span>';
        if (improve.label) {
            improveHtml = `<div class="es-reason">${esEsc(improve.label)}</div>`;
            if (improve.themes.length) {
                improveHtml += improve.themes.map(x =>
                    `<div class="es-reason" style="font-weight:500;color:#646464">· ${esEsc(x)}</div>`
                ).join('');
            }
        }

        html += `<tr>
            <td class="es-td-date">${esEsc(created)}</td>
            <td class="es-td-filiale">${esEsc(filiale)}</td>
            <td class="es-td-note">${noteHtml}</td>
            <td class="es-td-gruende">${reasonsHtml}</td>
            <td class="es-td-gruende">${improveHtml}</td>
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
