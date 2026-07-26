// ═══════════════════════════════════════════════════════════════════════════
//  Austritts-Feedback (anonym) — Walter 26.07.2026
//  HR-Hub → Auswertungen / Reporting → Austritts-Feedback
//  Endpoint: GET /api/exit-survey
//  Anonym = kein MA-Name; Gründe + Bemerkung sind sichtbar (Walter 26.07.2026).
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
        <div style="background:#fff;border:1px solid rgba(255,255,255,0.72);border-radius:14px;
                    box-shadow:0 8px 24px rgba(60,55,48,0.06);padding:14px 16px;min-width:0">
            <div style="font-size:10.5px;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#8b8b8b">${label}</div>
            <div style="font-size:26px;font-weight:760;color:#1a1a1a;margin-top:4px;letter-spacing:-0.02em">${value}</div>
            ${hint ? `<div style="font-size:11.5px;color:#8b8b8b;margin-top:4px">${hint}</div>` : ''}
        </div>`;

    let html = `<div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px;margin-bottom:16px">
        ${kpi('Antworten', rows.length, filter ? 'gefiltert' : 'gesamt')}
        ${kpi('Ø Note', avg != null ? avg.toFixed(1) : '—', withRating.length ? `${withRating.length} mit Note` : 'keine Noten')}
        ${kpi('Mit Filiale', rows.filter(r => r.companyProfileId || r.CompanyProfileId || r.filialeCode || r.FilialeCode).length, 'aus QR / Auswahl')}
    </div>`;

    if (!rows.length) {
        html += `<div style="padding:28px 18px;text-align:center;color:#8b8b8b;background:#fff;border:1px solid #e7e1d8;border-radius:14px">
            Noch keine anonymen Feedbacks${filter ? ' für diese Filiale' : ''}.<br>
            <span style="font-size:12.5px">Sie entstehen, wenn austretende MA den QR auf der Kündigungsbestätigung scannen.</span>
        </div>`;
        box.innerHTML = html;
        return;
    }

    // Karten statt Tabelle: sticky-thead + overflow:hidden hat die einzige Zeile
    // unter dem Kopf versteckt (Walter-Bug 26.07.2026). Gründe/Bemerkung prominent.
    html += `<div style="display:flex;flex-direction:column;gap:12px">`;
    for (const r of rows) {
        const reasons = esReasonsOf(r);
        const atm = (r.atmosphereDetail || r.AtmosphereDetail || '').trim();
        const comment = (r.comment || r.Comment || '').trim();
        const filiale = r.filiale || r.Filiale || '—';
        const rating = r.rating ?? r.Rating;
        const created = esFmtDate(r.createdAt || r.CreatedAt);

        const reasonHtml = reasons.length
            ? `<ul style="margin:0;padding:0 0 0 18px;color:#1a1a1a;line-height:1.55">
                ${reasons.map(x => `<li style="margin:0 0 4px">${esEsc(x)}</li>`).join('')}
               </ul>`
            : `<span style="color:#8b8b8b">kein Grund angegeben</span>`;

        let bemerkungHtml = '';
        if (atm || comment) {
            const parts = [];
            if (atm) {
                parts.push(`<div style="margin-bottom:${comment ? '10px' : '0'}">
                    <div style="font-size:11px;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#8b8b8b;margin-bottom:4px">Atmosphäre / Organisation</div>
                    <div style="color:#3f3f3f;line-height:1.55;white-space:pre-wrap">${esEsc(atm)}</div>
                </div>`);
            }
            if (comment) {
                parts.push(`<div>
                    <div style="font-size:11px;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#8b8b8b;margin-bottom:4px">Bemerkung</div>
                    <div style="color:#3f3f3f;line-height:1.55;white-space:pre-wrap">${esEsc(comment)}</div>
                </div>`);
            }
            bemerkungHtml = parts.join('');
        } else {
            bemerkungHtml = `<span style="color:#8b8b8b">keine Bemerkung</span>`;
        }

        html += `<div style="background:#fff;border:1px solid #e7e1d8;border-radius:14px;padding:16px 18px;
                            box-shadow:0 8px 24px rgba(60,55,48,0.06)">
            <div style="display:flex;flex-wrap:wrap;gap:10px 18px;align-items:baseline;margin-bottom:14px">
                <div style="font-size:13.5px;font-weight:760;color:#1a1a1a">${esEsc(filiale)}</div>
                <div style="font-size:12.5px;color:#8b8b8b">${esEsc(created)}</div>
                <div style="margin-left:auto;font-size:13px;font-weight:760;color:#1a1a1a">
                    Note ${rating != null ? esEsc(rating) : '—'}
                </div>
            </div>
            <div style="display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1.2fr);gap:16px 22px">
                <div>
                    <div style="font-size:11px;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#8b8b8b;margin-bottom:8px">Gründe</div>
                    ${reasonHtml}
                </div>
                <div>
                    <div style="font-size:11px;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#8b8b8b;margin-bottom:8px">Bemerkung</div>
                    ${bemerkungHtml}
                </div>
            </div>
        </div>`;
    }
    html += `</div>
        <div style="font-size:11.5px;color:#8b8b8b;margin-top:12px">
            Anonym — kein Mitarbeitername · Gründe und Bemerkung sind sichtbar ·
            Filiale aus dem QR der Kündigungsbestätigung (oder manuelle Wahl).
        </div>`;

    box.innerHTML = html;
    if (typeof fixheadSyncStickyOffset === 'function') fixheadSyncStickyOffset();
}
