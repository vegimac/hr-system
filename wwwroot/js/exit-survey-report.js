// ═══════════════════════════════════════════════════════════════════════════
//  Austritts-Feedback (anonym) — Walter 26.07.2026
//  HR-Hub → Auswertungen / Reporting → Austritts-Feedback
//  Endpoint: GET /api/exit-survey
// ═══════════════════════════════════════════════════════════════════════════

const _ES_REASON_LABELS = {
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
        const key = r.filialeCode || r.filiale || '';
        if (!key) continue;
        if (!codes.has(key)) codes.set(key, r.filiale || key);
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
    // "2026-07-26T18:00:00" oder Date
    const m = d.match(/^(\d{4})-(\d{2})-(\d{2})/);
    if (m) return `${m[3]}.${m[2]}.${m[1]}`;
    try { return new Date(d).toLocaleDateString('de-CH'); } catch { return d; }
}

function esParseReasons(json) {
    try {
        const arr = typeof json === 'string' ? JSON.parse(json || '[]') : (json || []);
        if (!Array.isArray(arr)) return [];
        return arr.map(c => _ES_REASON_LABELS[c] || c);
    } catch { return []; }
}

function esRender() {
    const box = document.getElementById('esResult');
    if (!box) return;
    const filter = document.getElementById('esFiliale')?.value || '';
    const rows = filter
        ? _esRows.filter(r => (r.filialeCode || r.filiale || '') === filter)
        : _esRows;

    const withRating = rows.filter(r => r.rating != null);
    const avg = withRating.length
        ? (withRating.reduce((s, r) => s + (+r.rating || 0), 0) / withRating.length)
        : null;

    const kpi = (label, value, hint) => `
        <div style="background:#fff;border:1px solid rgba(255,255,255,0.72);border-radius:14px;
                    box-shadow:0 8px 24px rgba(60,55,48,0.06);padding:14px 16px;min-width:0">
            <div style="font-size:10.5px;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#8b8b8b">${label}</div>
            <div style="font-size:26px;font-weight:760;color:#1a1a1a;margin-top:4px;letter-spacing:-0.02em">${value}</div>
            ${hint ? `<div style="font-size:11.5px;color:#8b8b8b;margin-top:4px">${hint}</div>` : ''}
        </div>`;

    let html = `<div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px;margin-bottom:16px">
        ${kpi('Antworten', rows.length, filter ? 'gefiltert' : 'gesamt')}
        ${kpi('Ø Note', avg != null ? avg.toFixed(1) : '—', withRating.length ? `${withRating.length} mit Note` : 'keine Noten')}
        ${kpi('Mit Filiale', rows.filter(r => r.companyProfileId || r.filialeCode).length, 'aus QR / Auswahl')}
    </div>`;

    if (!rows.length) {
        html += `<div style="padding:28px 18px;text-align:center;color:#8b8b8b;background:#fff;border:1px solid #e7e1d8;border-radius:14px">
            Noch keine anonymen Feedbacks${filter ? ' für diese Filiale' : ''}.<br>
            <span style="font-size:12.5px">Sie entstehen, wenn austretende MA den QR auf der Kündigungsbestätigung scannen.</span>
        </div>`;
        box.innerHTML = html;
        return;
    }

    html += `<div style="background:#fff;border:1px solid #e7e1d8;border-radius:14px;overflow:hidden;box-shadow:0 8px 24px rgba(60,55,48,0.06)">
        <table style="width:100%;border-collapse:collapse;font-size:13px">
            <thead>
                <tr style="background:#f6f3ee;text-align:left">
                    <th style="padding:10px 12px;font-weight:700;color:#646464;border-bottom:1px solid #e7e1d8">Datum</th>
                    <th style="padding:10px 12px;font-weight:700;color:#646464;border-bottom:1px solid #e7e1d8">Filiale</th>
                    <th style="padding:10px 12px;font-weight:700;color:#646464;border-bottom:1px solid #e7e1d8;width:56px">Note</th>
                    <th style="padding:10px 12px;font-weight:700;color:#646464;border-bottom:1px solid #e7e1d8">Gründe</th>
                    <th style="padding:10px 12px;font-weight:700;color:#646464;border-bottom:1px solid #e7e1d8">Kommentar</th>
                </tr>
            </thead>
            <tbody>`;

    for (const r of rows) {
        const reasons = esParseReasons(r.reasonsJson);
        if (r.reasonOther) reasons.push(r.reasonOther);
        const detail = [];
        if (r.atmosphereDetail) detail.push('<b>Atmosphäre:</b> ' + esEsc(r.atmosphereDetail));
        if (r.comment) detail.push(esEsc(r.comment));
        html += `<tr style="border-bottom:1px solid #f0ebe3;vertical-align:top">
            <td style="padding:10px 12px;white-space:nowrap;color:#3f3f3f">${esEsc(esFmtDate(r.createdAt))}</td>
            <td style="padding:10px 12px;color:#3f3f3f">${esEsc(r.filiale || '—')}</td>
            <td style="padding:10px 12px;font-weight:700;color:#1a1a1a">${r.rating != null ? esEsc(r.rating) : '—'}</td>
            <td style="padding:10px 12px;color:#3f3f3f;line-height:1.45">${reasons.length ? reasons.map(esEsc).join('<br>') : '—'}</td>
            <td style="padding:10px 12px;color:#646464;line-height:1.45;max-width:360px">${detail.length ? detail.join('<br><br>') : '—'}</td>
        </tr>`;
    }

    html += `</tbody></table></div>
        <div style="font-size:11.5px;color:#8b8b8b;margin-top:10px">
            Anonym — kein Mitarbeitername · Filiale aus dem QR der Kündigungsbestätigung (oder manuelle Wahl).
        </div>`;

    box.innerHTML = html;
    if (typeof fixheadSyncStickyOffset === 'function') fixheadSyncStickyOffset();
}
