// ============================================================================
// Mindestlohn-Verwaltung (L-GAV) — Walter-Vorgabe 20.05.2026.
// Stil/Muster wie SV-Sätze in admin-settings.js. Nutzt globale Helfer:
//   ah()        – Auth-Header (index.html)
//   showToast() – Toast (payroll.js)
// Styling über die .mw-* Klassen in css/app.css (light + theme-dark).
//
// Datenmodell minimum_wage_rule_new:
//   jobGroupCode, employmentModelCode, educationLevelId, salaryType
//   (hourly/monthly), amount, validFrom, validTo, isActive, ageMax
// Versioniert über validFrom/validTo; eine Version = alle Sätze mit demselben
// Gültig-ab (POST /copy legt sie gemeinsam an).
//
// Ansicht (Walter-Vorgabe 01.10.2026): oben die heute gültigen Sätze (plus die
// nächste geplante Version daneben), unten die Historie aller Versionen —
// Klick zeigt die Sätze jener Version. Bearbeiten nach Zeitlage (Server liefert
// `bearbeitbar`): vergangen nie, aktuell nur ohne Lohnlauf, künftig frei.
// ============================================================================

let mwAllRules = [];
// Gewählte Version aus der Historie (Gültig-ab ISO). null = Standardansicht.
let mwSelVersion = null;
// Frühestes erlaubtes Gültig-ab für eine neue Folge-Version (global über alle
// Filialen): 1. Tag des Monats nach der letzten abgeschlossenen Periode. null = frei.
let mwFirstAllowed = null;

// Ausbildungsstufen = Spalten der Matrix (IDs laut education_level)
const MW_EDU = [
    { id: 2, label: 'Ia',   sub: 'ohne' },
    { id: 3, label: 'Ib',   sub: 'PROGRESSO' },
    { id: 4, label: 'II',   sub: 'EBA' },
    { id: 5, label: 'IIIa', sub: 'EFZ' },
    { id: 6, label: 'IIIb', sub: 'GA6' },
    { id: 7, label: 'IV',   sub: 'BerPrüfung' },
];

// Zeilen-Reihenfolge (Funktion + Modell)
const MW_GROUP_ORDER = ['CREW','HOST_CT','SWING','SHIFT_LEADER_1_6','SHIFT_LEADER_7_PLUS','ASST_2','ASST_1','REST_MANAGER'];
const MW_MODEL_ORDER = ['FLEX','MTP','FIX','FIX-M'];
const MW_GROUP_LABEL = {
    CREW: 'Crew',
    HOST_CT: 'Host (CT)',
    SWING: 'Swing Manager',
    SHIFT_LEADER_1_6: 'Shift Leader 1–6 Mt.',
    SHIFT_LEADER_7_PLUS: 'Shift Leader 7+ Mt.',
    ASST_2: 'Assistant 2',
    ASST_1: 'Assistant 1',
    REST_MANAGER: 'Restaurant Manager',
};

// Vertragsmodell-Farben — identisch zum Rest des Programms (payroll.js,
// contracts-page.js, akonto-workflow.js …): MTP grün, UTP amber, FIX blau,
// FIX-M violett. Text dunkel passend zur Pastell-Fläche.
const MW_MODEL_COLOR = { MTP: '#d1fae5', FLEX: '#fef3c7', FIX: '#ece9e2', 'FIX-M': '#ede9fe' };
const MW_MODEL_TEXT  = { MTP: '#065f46', FLEX: '#92400e', FIX: '#6b6152', 'FIX-M': '#5b21b6' };
function mwBadge(model, extra) {
    const bg = MW_MODEL_COLOR[model] || '#f1f5f9';
    const fg = MW_MODEL_TEXT[model]  || '#475569';
    return `<span class="mw-badge" style="background:${bg};color:${fg};${extra || ''}">${model === 'FLEX' ? 'FLEX' : model}</span>`;
}

function mwGroupIdx(g) { const i = MW_GROUP_ORDER.indexOf(g); return i < 0 ? 999 : i; }
function mwModelIdx(m) { const i = MW_MODEL_ORDER.indexOf(m); return i < 0 ? 999 : i; }
function mwEduLabel(id) { const e = MW_EDU.find(x => x.id === id); return e ? e.label : String(id); }
function mwAmt(v) { return Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }

function mwIso(d) { return d ? String(d).slice(0, 10) : null; }
function mwFmtDate(iso) {
    if (!iso) return '–';
    const s = mwIso(iso);
    return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
}
function mwTodayIso() {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

// Betrag immer im Format 00.00 (zwei Nachkommastellen, Punkt) — akzeptiert beim
// Tippen Komma ODER Punkt, gibt leeren String bei ungültiger Eingabe zurück.
function mwFmtInput(v) {
    if (v == null) return '';
    const n = parseFloat(String(v).replace(',', '.').replace(/[^0-9.\-]/g, ''));
    return isNaN(n) ? '' : n.toFixed(2);
}
function mwParseAmount(v) { return parseFloat(String(v ?? '').replace(',', '.').replace(/[^0-9.\-]/g, '')); }

// Ist die Regel an einem Stichtag gültig? (validFrom ≤ d ≤ validTo|∞)
function mwValidAt(r, dateIso) {
    const vf = mwIso(r.validFrom);
    const vt = mwIso(r.validTo);
    return vf <= dateIso && (vt == null || vt >= dateIso);
}
function mwActive() { return mwAllRules.filter(r => r.isActive); }
function mwSetAt(dateIso) { return mwActive().filter(r => mwValidAt(r, dateIso)); }
function mwCellKey(r) { return [r.salaryType, r.jobGroupCode, r.employmentModelCode, r.educationLevelId, r.ageMax ?? ''].join('|'); }
function mwByKey(rules) { const m = {}; rules.forEach(r => { m[mwCellKey(r)] = r; }); return m; }

// Versionen = je ein Gültig-ab. Zeitlage wie der Server (heute als Grenze).
function mwVersions() {
    const today = mwTodayIso();
    const map = {};
    mwActive().forEach(r => {
        const vf = mwIso(r.validFrom);
        const v = map[vf] || (map[vf] = { from: vf, to: undefined, rules: [] });
        v.rules.push(r);
        const vt = mwIso(r.validTo);
        v.to = (v.to === undefined) ? vt : (v.to === null || vt === null ? null : (vt > v.to ? vt : v.to));
    });
    return Object.values(map).map(v => {
        const lage = v.from > today ? 'geplant' : (v.to && v.to < today ? 'vergangen' : 'aktuell');
        return {
            ...v,
            lage,
            verwendet: v.rules.some(r => r.inLohnVerwendet),
            offen: v.rules.filter(r => !r.confirmed).length,
        };
    }).sort((a, b) => b.from.localeCompare(a.from));
}
function mwNextPlanned() {
    return mwVersions().filter(v => v.lage === 'geplant').sort((a, b) => a.from.localeCompare(b.from))[0] || null;
}

// Beim Öffnen der Page (showPage → mwInit): Standardansicht laden.
function mwInit() {
    mwSelVersion = null;
    mwLoad();
}

// IMMER die komplette Historie laden (all=true) — daraus rendert mwRender()
// die aktuelle Matrix, die geplante Spalte und die Versions-Liste.
async function mwLoad() {
    const cont = document.getElementById('mwContainer');
    if (!cont) return;
    cont.innerHTML = '<div class="mw-muted" style="padding:30px;text-align:center">Wird geladen…</div>';

    try {
        const [res, faRes] = await Promise.all([
            fetch('/api/minimum-wage-rules?all=true', { headers: ah() }),
            fetch('/api/minimum-wage-rules/first-allowed-date', { headers: ah() })
        ]);
        if (!res.ok) {
            cont.innerHTML = `<div style="color:#dc2626;padding:16px">Fehler beim Laden (HTTP ${res.status})</div>`;
            return;
        }
        mwAllRules = await res.json();
        if (faRes.ok) { const fa = await faRes.json().catch(() => ({})); mwFirstAllowed = fa.firstAllowedDate || null; }
        mwRender();
    } catch (e) {
        cont.innerHTML = `<div style="color:#dc2626;padding:16px">Verbindungsfehler: ${e.message}</div>`;
    }
}

function mwSelectVersion(fromIso) {
    mwSelVersion = fromIso || null;
    mwRender();
}

function mwRender() {
    const cont   = document.getElementById('mwContainer');
    const infoEl = document.getElementById('mwInfo');
    if (!cont) return;

    const today = mwTodayIso();
    const cdEl = document.getElementById('mwCreateDate');
    if (cdEl) {
        const morgen = new Date(); morgen.setDate(morgen.getDate() + 1);
        const morgenIso = `${morgen.getFullYear()}-${String(morgen.getMonth() + 1).padStart(2, '0')}-${String(morgen.getDate()).padStart(2, '0')}`;
        cdEl.min = mwFirstAllowed && mwFirstAllowed > morgenIso ? mwFirstAllowed : morgenIso;
    }

    const versions = mwVersions();
    const sel = mwSelVersion ? versions.find(v => v.from === mwSelVersion) : null;
    if (mwSelVersion && !sel) mwSelVersion = null;

    let html = '';
    if (sel) {
        // Einzelne Version aus der Historie
        const left = mwByKey(mwSetAt(sel.from));
        if (infoEl) infoEl.textContent = `Version ab ${mwFmtDate(sel.from)}`;
        html += mwRenderVersionHint(sel);
        html += mwRenderMatrix('Stundenlöhne', 'CHF / Std.',        'hourly',  left, null, null);
        html += mwRenderMatrix('Monatslöhne',  'CHF / Mt. · 100 %', 'monthly', left, null, null);
        html += mwRenderYouth(left, null, null);
    } else {
        const current = mwSetAt(today);
        const planned = mwNextPlanned();
        if (!current.length && !planned) {
            if (infoEl) infoEl.textContent = '0 Sätze';
            cont.innerHTML = '<div class="mw-muted" style="padding:30px;text-align:center;font-style:italic">Keine Sätze erfasst.</div>'
                + mwRenderVersionList(versions);
            return;
        }
        // Gibt es heute nichts Gültiges, steht die geplante Version allein.
        const left  = mwByKey(current.length ? current : mwSetAt(planned.from));
        const right = current.length && planned ? mwByKey(mwSetAt(planned.from)) : null;
        const abDatum = right ? planned.from : null;
        if (infoEl) infoEl.textContent = 'aktuelle Sätze' + (abDatum ? ` · geplant ab ${mwFmtDate(abDatum)}` : '');
        if (right) html += mwRenderPlanHint(abDatum);
        html += mwRenderMatrix('Stundenlöhne', 'CHF / Std.',        'hourly',  left, right, abDatum);
        html += mwRenderMatrix('Monatslöhne',  'CHF / Mt. · 100 %', 'monthly', left, right, abDatum);
        html += mwRenderYouth(left, right, abDatum);
    }
    html += mwRenderVersionList(versions);
    cont.innerHTML = html;
}

// Banner über der Matrix, wenn eine Version aus der Historie gewählt ist.
function mwRenderVersionHint(v) {
    const range = `ab <b>${mwFmtDate(v.from)}</b>${v.to ? ` bis <b>${mwFmtDate(v.to)}</b>` : ''}`;
    const text = v.lage === 'vergangen' ? 'abgelaufen — nur Ansicht, bleibt unverändert'
        : v.lage === 'aktuell' ? (v.verwendet ? 'aktuell — in einem Lohnlauf verwendet, nur über eine neue Version änderbar' : 'aktuell — noch in keinem Lohnlauf, Beträge anklicken zum Ändern')
        : (v.verwendet ? 'geplant — schon in einem Lohnlauf verwendet' : 'geplant — Beträge anklicken zum Ändern');
    const isNewest = mwVersions()[0]?.from === v.from;
    const delBtn = v.lage === 'geplant' && !v.verwendet && isNewest
        ? `<button class="vh-btn vh-btn-danger" onclick="mwDeleteVersion('${v.from}')">Version löschen</button>` : '';
    return `<div class="card mw-section" style="overflow:visible"><div class="mw-planhint" style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
        <span>Version ${range} · ${text}</span>
        <span style="margin-left:auto;display:flex;gap:8px">${delBtn}<button class="vh-btn" onclick="mwSelectVersion(null)">← zurück zu aktuell</button></span>
    </div></div>`;
}

// Erklär-Banner, wenn neben den aktuellen Sätzen eine geplante Version existiert.
function mwRenderPlanHint(abDatum) {
    const body = `Linke Spalte «Aktuell» = heute gültig. Rechte Spalte <b>ab ${mwFmtDate(abDatum)}</b> = geplanter Satz, anklicken zum Bestätigen/Anpassen. <b style="color:#047857">Grün</b> = Betrag geändert, <b style="color:#d97706">Orange</b> = bestätigt &amp; unverändert, <b style="color:#dc2626">Rot</b> = noch nicht bestätigt.`;
    return `<div class="card mw-section" style="overflow:visible"><div class="mw-planhint">${body}</div></div>`;
}

function mwSperrTitel(r) {
    return r.zeitlage === 'vergangen' ? 'Abgelaufene Version — bleibt unverändert'
        : 'In einem Lohnlauf verwendet — nur über eine neue Version änderbar';
}

// Betragszelle der (linken) Hauptspalte.
function mwCurCell(r) {
    if (!r) return `<td class="mw-empty">–</td>`;
    if (!r.bearbeitbar)
        return `<td class="mw-amount mw-cur-ro" onclick="mwLockedInfo(${r.id})" title="${mwSperrTitel(r)}"><span>${mwAmt(r.amount)}</span></td>`;
    return `<td class="mw-amount" onclick="mwEdit(${r.id})" title="Betrag bearbeiten"><span>${mwAmt(r.amount)}</span></td>`;
}

// Geplante Betragszelle NEBEN der aktuellen Spalte.
// Drei-Farben-Logik (Walter-Vorgabe 23.05.2026):
//   GRÜN   = Betrag ≠ aktuell (geändert)
//   ORANGE = bestätigt (gespeichert), aber unverändert
//   ROT    = noch nicht bestätigt (frisch kopiert, noch zu prüfen)
function mwFutCell(edt, ref) {
    if (!edt) return `<td class="mw-empty mw-fut-col">–</td>`;
    let cls, hint;
    if (ref && Number(edt.amount) !== Number(ref.amount)) { cls = 'mw-fut-changed';  hint = 'geänderter Satz'; }
    else if (edt.confirmed)                               { cls = 'mw-fut-reviewed'; hint = 'bestätigt, unverändert'; }
    else                                                  { cls = 'mw-fut-same';     hint = 'noch nicht bestätigt'; }
    const click = edt.bearbeitbar ? `mwEdit(${edt.id})` : `mwLockedInfo(${edt.id})`;
    return `<td class="mw-amount mw-fut-col ${cls}" onclick="${click}" title="Geplanter Satz (${hint})"><span>${mwAmt(edt.amount)}</span></td>`;
}

// left/right = Map Zellenschlüssel → Regel. right = null → einspaltig.
function mwRenderMatrix(title, unit, salaryType, left, right, abDatum) {
    const split = !!right;
    const pool = [...Object.values(left), ...Object.values(right || {})]
        .filter(r => r.salaryType === salaryType && r.ageMax == null);

    const seen = new Set();
    const rowKeys = [];
    pool.forEach(r => {
        const k = r.jobGroupCode + '|' + r.employmentModelCode;
        if (!seen.has(k)) { seen.add(k); rowKeys.push({ g: r.jobGroupCode, m: r.employmentModelCode }); }
    });
    rowKeys.sort((a, b) => (mwGroupIdx(a.g) - mwGroupIdx(b.g)) || (mwModelIdx(a.m) - mwModelIdx(b.m)));

    let thead;
    if (!split) {
        const head = MW_EDU.map(e => `<th>${e.label}<span class="mw-sub">${e.sub}</span></th>`).join('');
        thead = `<tr><th class="mw-th-row">Funktion / Modell</th>${head}</tr>`;
    } else {
        const top = MW_EDU.map(e => `<th colspan="2" class="mw-edu-top">${e.label}<span class="mw-sub">${e.sub}</span></th>`).join('');
        const sub = MW_EDU.map(() => `<th class="mw-sub-cur">Aktuell</th><th class="mw-fut-col mw-sub-fut">ab ${mwFmtDate(abDatum)}</th>`).join('');
        thead = `<tr><th class="mw-th-row" rowspan="2">Funktion / Modell</th>${top}</tr><tr>${sub}</tr>`;
    }

    const colCount = 1 + MW_EDU.length * (split ? 2 : 1);
    let body;
    if (!rowKeys.length) {
        body = `<tr><td colspan="${colCount}" class="mw-muted" style="padding:20px;text-align:center;font-style:italic">Keine Sätze.</td></tr>`;
    } else {
        body = rowKeys.map(rk => {
            const cells = MW_EDU.map(e => {
                const k = [salaryType, rk.g, rk.m, e.id, ''].join('|');
                const l = left[k] || null;
                return split ? mwCurCell(l) + mwFutCell(right[k] || null, l) : mwCurCell(l);
            }).join('');
            return `<tr>
                <td class="mw-row-label">${MW_GROUP_LABEL[rk.g] || rk.g}${mwBadge(rk.m)}</td>
                ${cells}
            </tr>`;
        }).join('');
    }

    return `<div class="card mw-section">
        <div class="mw-section-head">${title}<span class="mw-unit">${unit}</span></div>
        <div class="mw-scroll">
        <table class="mw-table">
            <thead>${thead}</thead>
            <tbody>${body}</tbody>
        </table>
        </div>
    </div>`;
}

function mwRenderYouth(left, right, abDatum) {
    const split = !!right;
    const all = [...Object.values(left), ...Object.values(right || {})].filter(r => r.ageMax != null);
    if (!all.length) return '';
    const keys = [...new Set(all.map(mwCellKey))];
    const rep = k => left[k] || right?.[k];
    keys.sort((a, b) => {
        const ra = rep(a), rb = rep(b);
        return (mwGroupIdx(ra.jobGroupCode) - mwGroupIdx(rb.jobGroupCode))
            || (mwModelIdx(ra.employmentModelCode) - mwModelIdx(rb.employmentModelCode))
            || (ra.educationLevelId - rb.educationLevelId)
            || ((ra.ageMax ?? 0) - (rb.ageMax ?? 0));
    });

    const rows = keys.map(k => {
        const r = rep(k);
        const l = left[k] || null;
        return `<tr>
            <td class="mw-row-label">${MW_GROUP_LABEL[r.jobGroupCode] || r.jobGroupCode}</td>
            <td>${mwBadge(r.employmentModelCode, 'margin-left:0')}</td>
            <td class="mw-muted">${mwEduLabel(r.educationLevelId)}</td>
            <td class="mw-muted" title="Gilt bis zum Vormonat des ${r.ageMax + 1}. Geburtstags — im Geburtstagsmonat gilt schon der Erwachsenen-Mindestlohn.">bis zum ${r.ageMax + 1}. Geburtstag</td>
            <td class="mw-muted">${r.salaryType === 'hourly' ? 'CHF / Std.' : 'CHF / Mt.'}</td>
            ${mwCurCell(l)}${split ? mwFutCell(right[k] || null, l) : ''}
        </tr>`;
    }).join('');

    return `<div class="card mw-section">
        <div class="mw-section-head mw-youth">Jugendliche — Sondersätze nach Alter (L-GAV)</div>
        <div class="mw-scroll">
        <table class="mw-table">
            <thead><tr>
                <th class="mw-th-row">Funktion</th>
                <th class="mw-th-row">Modell</th>
                <th class="mw-th-row">Ausbildung</th>
                <th class="mw-th-row">Alter</th>
                <th class="mw-th-row">Einheit</th>
                <th>${split ? 'Aktuell' : 'Betrag'}</th>
                ${split ? `<th class="mw-fut-col mw-sub-fut">ab ${mwFmtDate(abDatum)}</th>` : ''}
            </tr></thead>
            <tbody>${rows}</tbody>
        </table>
        </div>
    </div>`;
}

// Historie unten: eine Zeile pro Version, Klick zeigt deren Sätze.
function mwRenderVersionList(versions) {
    if (!versions.length) return '';
    const pill = v => {
        if (v.lage === 'geplant')   return `<span class="vh-pill vh-pill-plan">geplant</span>`;
        if (v.lage === 'aktuell')   return `<span class="vh-pill vh-pill-akt">aktuell</span>`;
        return `<span class="vh-pill">vergangen</span>`;
    };
    const rows = versions.map(v => {
        const info = [
            `${v.rules.length} Sätze`,
            v.verwendet ? '🔒 in Lohn verwendet' : '',
            v.lage === 'geplant' && v.offen ? `<span style="color:#dc2626">${v.offen} noch nicht bestätigt</span>` : '',
        ].filter(Boolean).join(' · ');
        const sel = mwSelVersion === v.from ? ' sel' : '';
        return `<div class="vh-row${sel}" onclick="mwSelectVersion('${v.from}')" title="Sätze dieser Version anzeigen">
            <span class="vh-range">${mwFmtDate(v.from)} – ${v.to ? mwFmtDate(v.to) : 'offen'}</span>
            ${pill(v)}
            <span class="vh-info">${info}</span>
        </div>`;
    }).join('');
    return `<div class="card mw-section">
        <div class="mw-section-head">Historie<span class="mw-unit">alle Versionen · anklicken zum Anzeigen</span></div>
        <div class="vh-list">${rows}</div>
    </div>`;
}

// ── Modals ──────────────────────────────────────────────────────────────────
function mwOverlay(innerHtml) {
    mwCloseOverlay();
    const ov = document.createElement('div');
    ov.id = 'mwOverlay';
    ov.style.cssText = 'position:fixed;inset:0;background:rgba(15,23,42,0.45);display:flex;align-items:center;justify-content:center;z-index:3000';
    ov.innerHTML = `<div class="card" style="max-width:430px;width:90%;padding:24px;border-radius:14px">${innerHtml}</div>`;
    ov.addEventListener('click', e => { if (e.target === ov) mwCloseOverlay(); });
    document.body.appendChild(ov);
}
function mwCloseOverlay() { document.getElementById('mwOverlay')?.remove(); }

// Hinweis für gesperrte Sätze (abgelaufen oder in einem Lohnlauf verwendet).
function mwLockedInfo(id) {
    const r = mwAllRules.find(x => x.id === id);
    if (r && r.zeitlage === 'vergangen') {
        showToast('Dieser Mindestlohn gehört zu einer abgelaufenen Version und bleibt unverändert.', 'info');
        return;
    }
    showToast('Dieser Mindestlohn wurde bereits in einem Lohnlauf verwendet und ist gesperrt. Für eine Änderung rechts ein Datum wählen und «+ Folge-Version anlegen».', 'info');
}

function mwEdit(id) {
    const r = mwAllRules.find(x => x.id === id);
    if (!r) return;
    if (!r.bearbeitbar) { mwLockedInfo(id); return; }

    const unit = r.salaryType === 'hourly' ? 'CHF / Std.' : 'CHF / Mt.';
    const isFuture = r.zeitlage === 'geplant';
    mwOverlay(`
        <h3 style="margin:0 0 6px;font-size:16px">${isFuture ? 'Geplanten Mindestlohn bearbeiten' : 'Mindestlohn bearbeiten'}</h3>
        <p class="mw-muted" style="margin:0 0 18px;font-size:13px;line-height:1.5">
            ${MW_GROUP_LABEL[r.jobGroupCode] || r.jobGroupCode} · ${r.employmentModelCode} · ${mwEduLabel(r.educationLevelId)}${r.ageMax != null ? ` · bis zum ${r.ageMax + 1}. Geburtstag` : ''}<br>
            gültig ab <b>${mwFmtDate(r.validFrom)}</b>${isFuture ? ' <span style="color:#4338ca">(geplant)</span>' : ''}
        </p>
        <label class="mw-muted" style="font-size:12px">Betrag (${unit}) — Format 00.00</label>
        <input id="mwEditAmount" type="text" inputmode="decimal" placeholder="0.00" value="${Number(r.amount).toFixed(2)}"
               style="width:100%;padding:10px;border:1px solid #e2e8f0;border-radius:8px;margin:5px 0 20px;font-size:15px;font-weight:600;font-variant-numeric:tabular-nums"
               onblur="this.value=mwFmtInput(this.value)"
               onkeydown="if(event.key==='Enter')mwSaveAmount(${id})">
        <div style="display:flex;gap:8px;justify-content:flex-end">
            <button class="btn btn-secondary" onclick="mwCloseOverlay()">Abbrechen</button>
            <button class="btn btn-primary" onclick="mwSaveAmount(${id})">Speichern</button>
        </div>`);
    setTimeout(() => { const el = document.getElementById('mwEditAmount'); if (el) { el.focus(); el.select(); } }, 50);
}

async function mwSaveAmount(id) {
    const amt = mwParseAmount(document.getElementById('mwEditAmount')?.value);
    if (isNaN(amt) || amt < 0) { showToast('Ungültiger Betrag', 'error'); return; }
    try {
        const res = await fetch(`/api/minimum-wage-rules/${id}`, {
            method: 'PUT', headers: ah(), body: JSON.stringify({ amount: amt })
        });
        if (!res.ok) {
            const data = await res.json().catch(() => ({}));
            showToast(data.message || data.error || ('Speichern fehlgeschlagen (HTTP ' + res.status + ')'), 'error');
            if (res.status === 409) { mwCloseOverlay(); mwLoad(); }
            return;
        }
        mwCloseOverlay();
        showToast('Betrag gespeichert', 'success');
        mwLoad();
    } catch (e) { showToast('Fehler: ' + e.message, 'error'); }
}

// «+ Folge-Version anlegen» — kopiert die jüngste Version auf das gewählte
// Datum (/copy). Nur künftige Daten, nur nach der jüngsten Version.
async function mwCreateGeneration() {
    const d = document.getElementById('mwCreateDate')?.value;
    if (!d) {
        showToast('Bitte zuerst rechts ein «Neue Sätze ab»-Datum wählen.', 'error');
        document.getElementById('mwCreateDate')?.focus();
        return;
    }
    if (d <= mwTodayIso()) {
        showToast('Eine neue Version muss in der Zukunft beginnen.', 'error');
        return;
    }
    if (mwFirstAllowed && d < mwFirstAllowed) {
        showToast(`Das Gültig-ab-Datum muss am oder nach dem ${mwFmtDate(mwFirstAllowed)} liegen — frühester Termin nach der letzten abgeschlossenen Lohnperiode (über alle Filialen).`, 'error');
        return;
    }
    const newest = mwVersions()[0];
    if (newest && d <= newest.from) {
        showToast(`Es gibt bereits eine Version ab ${mwFmtDate(newest.from)}. Eine neue Version muss danach beginnen — oder die bestehende in der Historie anklicken und dort anpassen.`, 'info');
        return;
    }
    if (!(await liquidConfirm(`Alle Sätze der jüngsten Version werden kopiert und gelten ab ${mwFmtDate(d)}. Danach kannst du die geplanten Beträge pro Zelle anpassen.`,
        { title: `Folge-Version ab ${mwFmtDate(d)} anlegen?`, yesLabel: 'Anlegen', noLabel: 'Abbrechen' }))) return;
    try {
        const res  = await fetch('/api/minimum-wage-rules/copy', {
            method: 'POST', headers: ah(), body: JSON.stringify({ effectiveDate: d })
        });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) { showToast(data.message || data.error || ('Anlegen fehlgeschlagen (HTTP ' + res.status + ')'), 'error'); return; }
        showToast(`${data.copied} Sätze ab ${mwFmtDate(d)} angelegt — jetzt pro Zelle anpassbar`, 'success');
        const cd = document.getElementById('mwCreateDate'); if (cd) cd.value = '';
        mwSelVersion = null;
        mwLoad();
    } catch (e) { showToast('Fehler: ' + e.message, 'error'); }
}

// Künftige (jüngste) Version ganz löschen — die Vorversion gilt danach wieder ohne Ende.
async function mwDeleteVersion(fromIso) {
    if (!(await liquidConfirm(`Alle Sätze ab ${mwFmtDate(fromIso)} werden gelöscht. Die vorherige Version gilt danach wieder ohne Enddatum.`,
        { title: 'Geplante Version löschen?', yesLabel: 'Löschen', noLabel: 'Abbrechen' }))) return;
    try {
        const res = await fetch(`/api/minimum-wage-rules/version/${fromIso}`, { method: 'DELETE', headers: ah() });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) { showToast(data.message || data.error || ('Löschen fehlgeschlagen (HTTP ' + res.status + ')'), 'error'); return; }
        showToast(`Version ab ${mwFmtDate(fromIso)} gelöscht`, 'success');
        mwSelVersion = null;
        mwLoad();
    } catch (e) { showToast('Fehler: ' + e.message, 'error'); }
}
