// ═══════════════════════════════════════════════════════════════════════════
//  Austritts-Feedback (anonym) — Walter 26.07.2026
//  HR-Hub → Auswertungen / Reporting → Austritts-Feedback
//  Endpoint: GET /api/exit-survey?from=&to=&take=
//  Insights: Ø Note · Gründe · Themen · Noten-Trend (Zeitraum Von–Bis)
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

/** Aktuelle Fragebogen-Codes zuerst (Reihenfolge wie im Fragebogen). */
const _ES_REASON_ORDER = [
    'STARTE_NEUES', 'SCHULE_PLAENE', 'WENIGER_EINSAETZE',
    'MEHR_EINSAETZE', 'ARBEIT_PASST_NICHT', 'ETWAS_ANDERES',
];
const _ES_THEME_ORDER = [
    'FUEHRUNG', 'TEAMGEFUEHL', 'PLANUNG_ORG', 'ARBEITSZEITEN',
    'UNTERSTUETZUNG', 'ENTWICKLUNG', 'LOHN_BEDINGUNGEN', 'THEMA_ANDERES',
];
const _ES_MONTH_SHORT = ['Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez'];

let _esRows = [];
let _esDefaultsSet = false;

function esInit() {
    esEnsureDefaultDates();
    esLoad();
}

function esIsoToday() {
    const d = new Date();
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
}

function esIsoYearStart() {
    return `${new Date().getFullYear()}-01-01`;
}

/** Vorschlag: 1.1. dieses Jahres bis heute (einmalig beim ersten Öffnen). */
function esEnsureDefaultDates() {
    const fromEl = document.getElementById('esFrom');
    const toEl = document.getElementById('esTo');
    if (!fromEl || !toEl) return;
    if (_esDefaultsSet && fromEl.value && toEl.value) return;
    if (!fromEl.value) fromEl.value = esIsoYearStart();
    if (!toEl.value) toEl.value = esIsoToday();
    _esDefaultsSet = true;
}

async function esLoad() {
    const box = document.getElementById('esResult');
    if (!box) return;
    esEnsureDefaultDates();
    const from = document.getElementById('esFrom')?.value || '';
    const to = document.getElementById('esTo')?.value || '';
    if (from && to && from > to) {
        box.innerHTML = '<div class="es-empty">«Von» muss vor oder gleich «Bis» liegen.</div>';
        return;
    }
    box.innerHTML = '<div style="padding:20px;color:#8b8b8b">Lade Feedback…</div>';
    try {
        const qs = new URLSearchParams({ take: '2000' });
        if (from) qs.set('from', from);
        if (to) qs.set('to', to);
        const res = await fetch('/api/exit-survey?' + qs.toString(), { headers: ah() });
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

function esFmtDec(n, digits = 1) {
    if (n == null || Number.isNaN(n)) return '—';
    return n.toFixed(digits).replace('.', ',');
}

function esReasonCodesOf(r) {
    if (Array.isArray(r.reasonCodes) && r.reasonCodes.length) {
        return r.reasonCodes.map(c => String(c || '').trim().toUpperCase()).filter(Boolean);
    }
    const json = r.reasonsJson ?? r.ReasonsJson ?? '[]';
    try {
        const arr = typeof json === 'string' ? JSON.parse(json || '[]') : (json || []);
        if (Array.isArray(arr)) {
            return arr.map(c => String(c || '').trim().toUpperCase()).filter(Boolean);
        }
    } catch { /* ignore */ }
    return [];
}

function esReasonsOf(r) {
    if (Array.isArray(r.reasons) && r.reasons.length) {
        return r.reasons.map(x => String(x || '').trim()).filter(Boolean);
    }
    const codes = esReasonCodesOf(r);
    const labels = codes.map(c => _ES_REASON_LABELS[c] || c);
    const other = r.reasonOther || r.ReasonOther;
    if (other && String(other).trim()) labels.push(String(other).trim());
    return labels.filter(Boolean);
}

function esThemeCodesOf(r) {
    if (Array.isArray(r.improveThemeCodes) && r.improveThemeCodes.length) {
        return r.improveThemeCodes.map(c => String(c || '').trim().toUpperCase()).filter(Boolean);
    }
    const json = r.improveThemesJson ?? r.ImproveThemesJson ?? '[]';
    try {
        const arr = typeof json === 'string' ? JSON.parse(json || '[]') : (json || []);
        if (Array.isArray(arr)) {
            return arr.map(c => String(c || '').trim().toUpperCase()).filter(Boolean);
        }
    } catch { /* ignore */ }
    return [];
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
        themes = esThemeCodesOf(r).map(c => _ES_THEME_LABELS[c] || c);
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

function esFilteredRows() {
    const filter = document.getElementById('esFiliale')?.value || '';
    return filter
        ? _esRows.filter(r => (r.filialeCode || r.FilialeCode || '') === filter)
        : _esRows;
}

function esCountByCode(rows, getCodes) {
    const map = new Map();
    for (const r of rows) {
        for (const c of getCodes(r)) {
            if (!c) continue;
            map.set(c, (map.get(c) || 0) + 1);
        }
    }
    return map;
}

function esSortBars(map, preferredOrder, labels) {
    const entries = [...map.entries()].filter(([, n]) => n > 0);
    entries.sort((a, b) => {
        if (b[1] !== a[1]) return b[1] - a[1];
        const ia = preferredOrder.indexOf(a[0]);
        const ib = preferredOrder.indexOf(b[0]);
        if (ia >= 0 && ib >= 0) return ia - ib;
        if (ia >= 0) return -1;
        if (ib >= 0) return 1;
        return String(labels[a[0]] || a[0]).localeCompare(String(labels[b[0]] || b[0]), 'de');
    });
    return entries;
}

function esBarRowsHtml(items, mode) {
    if (!items.length) {
        return '<div class="es-bars-empty">Noch keine Angaben in diesem Zeitraum.</div>';
    }
    const max = Math.max(...items.map(x => x.value), 1);
    return `<div class="es-bars">${items.map(it => {
        const pctW = Math.max(4, Math.round((it.value / max) * 100));
        const right = mode === 'pct'
            ? `${esFmtDec(it.display, 0)} %`
            : String(it.display);
        return `<div class="es-bar-row">
            <div class="es-bar-label">${esEsc(it.label)}</div>
            <div class="es-bar-track"><div class="es-bar-fill" style="width:${pctW}%"></div></div>
            <div class="es-bar-val">${esEsc(right)}</div>
        </div>`;
    }).join('')}</div>`;
}

function esBuildReasonBars(rows) {
    const map = esCountByCode(rows, esReasonCodesOf);
    const sorted = esSortBars(map, _ES_REASON_ORDER, _ES_REASON_LABELS);
    const total = sorted.reduce((s, [, n]) => s + n, 0) || 1;
    return sorted.map(([code, n]) => ({
        label: _ES_REASON_LABELS[code] || code,
        value: n,
        display: Math.round((n / total) * 100),
    }));
}

function esBuildThemeBars(rows) {
    const map = esCountByCode(rows, esThemeCodesOf);
    const sorted = esSortBars(map, _ES_THEME_ORDER, _ES_THEME_LABELS);
    return sorted.map(([code, n]) => ({
        label: _ES_THEME_LABELS[code] || code,
        value: n,
        display: n,
    }));
}

function esRatingStats(rows) {
    const rated = rows
        .map(r => r.rating ?? r.Rating)
        .filter(n => n != null && n >= 1 && n <= 6)
        .map(Number);
    if (!rated.length) {
        return { avg: null, count: 0, hist: [0, 0, 0, 0, 0, 0] };
    }
    const hist = [0, 0, 0, 0, 0, 0];
    let sum = 0;
    for (const n of rated) {
        sum += n;
        hist[n - 1]++;
    }
    return { avg: sum / rated.length, count: rated.length, hist };
}

function esMonthTrend(rows) {
    const buckets = new Map();
    for (const r of rows) {
        const note = r.rating ?? r.Rating;
        if (note == null || note < 1 || note > 6) continue;
        const iso = String(r.createdAt || r.CreatedAt || '');
        const m = iso.match(/^(\d{4})-(\d{2})/);
        if (!m) continue;
        const key = `${m[1]}-${m[2]}`;
        if (!buckets.has(key)) buckets.set(key, { sum: 0, n: 0, y: +m[1], mo: +m[2] });
        const b = buckets.get(key);
        b.sum += Number(note);
        b.n += 1;
    }
    const points = [...buckets.values()]
        .sort((a, b) => a.y - b.y || a.mo - b.mo)
        .map(b => ({
            key: `${b.y}-${String(b.mo).padStart(2, '0')}`,
            label: _ES_MONTH_SHORT[b.mo - 1] || String(b.mo),
            avg: b.sum / b.n,
            n: b.n,
        }));
    return points;
}

function esTrendBadge(points) {
    if (points.length < 2) return null;
    const first = points[0].avg;
    const last = points[points.length - 1].avg;
    const delta = last - first;
    if (delta >= 0.15) return { text: 'steigend', cls: 'is-up' };
    if (delta <= -0.15) return { text: 'rückläufig', cls: 'is-down' };
    if (last >= 4.5) return { text: 'stabil positiv', cls: 'is-ok' };
    return { text: 'stabil', cls: 'is-ok' };
}

function esTrendSvg(points) {
    if (!points.length) {
        return '<div class="es-bars-empty">Noch keine Noten für einen Trend.</div>';
    }
    const W = 640, H = 160, padL = 28, padR = 16, padT = 22, padB = 28;
    const innerW = W - padL - padR;
    const innerH = H - padT - padB;
    let minY = Math.min(...points.map(p => p.avg));
    let maxY = Math.max(...points.map(p => p.avg));
    minY = Math.max(1, Math.floor(minY) - (minY % 1 === 0 ? 1 : 0));
    maxY = Math.min(6, Math.ceil(maxY));
    if (maxY - minY < 1) { minY = Math.max(1, minY - 0.5); maxY = Math.min(6, maxY + 0.5); }
    const xAt = i => padL + (points.length === 1 ? innerW / 2 : (i / (points.length - 1)) * innerW);
    const yAt = v => padT + (1 - (v - minY) / (maxY - minY)) * innerH;
    const path = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${xAt(i).toFixed(1)},${yAt(p.avg).toFixed(1)}`).join(' ');
    const dots = points.map((p, i) => {
        const x = xAt(i), y = yAt(p.avg);
        return `<circle cx="${x.toFixed(1)}" cy="${y.toFixed(1)}" r="4.2" class="es-trend-dot"/>
            <text x="${x.toFixed(1)}" y="${(y - 10).toFixed(1)}" text-anchor="middle" class="es-trend-val">${esEsc(esFmtDec(p.avg, 1))}</text>
            <text x="${x.toFixed(1)}" y="${H - 8}" text-anchor="middle" class="es-trend-lbl">${esEsc(p.label)}</text>`;
    }).join('');
    const grid = [minY, (minY + maxY) / 2, maxY].map(v => {
        const y = yAt(v);
        return `<line x1="${padL}" y1="${y.toFixed(1)}" x2="${W - padR}" y2="${y.toFixed(1)}" class="es-trend-grid"/>
            <text x="${padL - 6}" y="${(y + 3.5).toFixed(1)}" text-anchor="end" class="es-trend-axis">${esEsc(esFmtDec(v, v % 1 ? 1 : 0))}</text>`;
    }).join('');
    return `<svg class="es-trend-svg" viewBox="0 0 ${W} ${H}" role="img" aria-label="Entwicklung der Note">
        ${grid}
        <path d="${path}" class="es-trend-line" fill="none"/>
        ${dots}
    </svg>`;
}

function esInsightsHtml(rows) {
    const rating = esRatingStats(rows);
    const reasons = esBuildReasonBars(rows);
    const themes = esBuildThemeBars(rows);
    const trend = esMonthTrend(rows);
    const badge = esTrendBadge(trend);

    const maxHist = Math.max(...rating.hist, 1);
    const circles = [1, 2, 3, 4, 5, 6].map(n => {
        const c = rating.hist[n - 1];
        const strong = c > 0 && c >= maxHist * 0.55;
        const mid = c > 0;
        const cls = strong ? 'is-strong' : (mid ? 'is-mid' : '');
        return `<span class="es-score-circle ${cls}" title="${c}× Note ${n}">${n}</span>`;
    }).join('');

    const avgHtml = rating.avg == null
        ? `<div class="es-avg"><span class="es-avg-num">—</span></div>
           <div class="es-avg-label">Ø Note · noch keine Bewertung</div>`
        : `<div class="es-avg"><span class="es-avg-num">${esEsc(esFmtDec(rating.avg, 1))}</span>
             <span class="es-avg-of">von 6</span></div>
           <div class="es-avg-label">Ø Note · ${rating.count} Bewertung${rating.count === 1 ? '' : 'en'}</div>`;

    return `<div class="es-insights">
        <div class="es-insight-card es-insight-rating">
            <div class="es-insight-q">Wie blicken unsere Mitarbeitenden zurück?</div>
            <div class="es-insight-rating-row">
                <div class="es-avg-block">${avgHtml}</div>
                <div class="es-score-circles" aria-hidden="true">${circles}</div>
            </div>
            <div class="es-scale-legend">1 = eher schwierig · 6 = richtig gute Zeit</div>
        </div>

        <div class="es-insight-grid">
            <div class="es-insight-card">
                <div class="es-insight-title">Was hat den Entscheid geprägt?</div>
                ${esBarRowsHtml(reasons, 'pct')}
            </div>
            <div class="es-insight-card">
                <div class="es-insight-title">Wo können wir besser werden?</div>
                ${esBarRowsHtml(themes, 'count')}
            </div>
        </div>

        <div class="es-insight-card es-insight-trend">
            <div class="es-insight-title-row">
                <div class="es-insight-title">Entwicklung der Note</div>
                ${badge ? `<span class="es-trend-badge ${badge.cls}">${esEsc(badge.text)}</span>` : ''}
            </div>
            ${esTrendSvg(trend)}
        </div>
    </div>`;
}

function esRender() {
    const box = document.getElementById('esResult');
    if (!box) return;
    const rows = esFilteredRows();
    const filter = document.getElementById('esFiliale')?.value || '';

    let html = esInsightsHtml(rows);

    html += `<div class="es-kpi-row">
        <div class="es-kpi">
            <div class="es-kpi-label">Antworten</div>
            <div class="es-kpi-value">${rows.length}</div>
            <div class="es-kpi-hint">${filter ? 'gefiltert' : 'im Zeitraum'}</div>
        </div>
        <div class="es-kpi">
            <div class="es-kpi-label">Mit Themen</div>
            <div class="es-kpi-value">${rows.filter(r => (r.improveAnswer || r.ImproveAnswer) === 'JA').length}</div>
            <div class="es-kpi-hint">«Ja, da gibt es etwas»</div>
        </div>
        <div class="es-kpi">
            <div class="es-kpi-label">Mit Filiale</div>
            <div class="es-kpi-value">${rows.filter(r => r.companyProfileId || r.CompanyProfileId || r.filialeCode || r.FilialeCode).length}</div>
            <div class="es-kpi-hint">aus QR / Auswahl</div>
        </div>
    </div>`;

    if (!rows.length) {
        html += `<div class="es-empty">
            Noch keine anonymen Feedbacks${filter ? ' für diese Filiale' : ''} in diesem Zeitraum.<br>
            <span>Sie entstehen, wenn austretende MA den QR auf der Kündigungsbestätigung scannen.</span>
        </div>`;
        box.innerHTML = html;
        if (typeof fixheadSyncStickyOffset === 'function') fixheadSyncStickyOffset();
        return;
    }

    html += `<div class="es-table-card">
        <table class="es-table">
            <thead>
                <tr>
                    <th>Datum</th>
                    <th>Filiale</th>
                    <th class="es-th-note">Note</th>
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
