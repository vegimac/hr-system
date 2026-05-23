// ============================================================================
// Mindestlohn-Verwaltung (L-GAV) — Walter-Vorgabe 20.05.2026.
// Stil/Muster wie SV-Sätze in admin-settings.js. Nutzt globale Helfer:
//   ah()        – Auth-Header (index.html)
//   showToast() – Toast (payroll.js)
// Styling über die .mw-* Klassen in index.html (light + theme-dark).
//
// Datenmodell minimum_wage_rule_new:
//   jobGroupCode, employmentModelCode, educationLevelId, salaryType
//   (hourly/monthly), amount, validFrom, validTo, isActive, ageMax
// Versioniert über validFrom/validTo → Änderungen können an beliebigem Datum
// greifen. Stichtag-Filter zeigt die an einem Datum gültigen Sätze; „Alle
// Versionen" zeigt die komplette Historie.
// ============================================================================

let mwAllRules = [];
let mwShowAll  = false;
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
const MW_MODEL_ORDER = ['UTP','MTP','FIX','FIX-M'];
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
const MW_MODEL_COLOR = { MTP: '#d1fae5', UTP: '#fef3c7', FIX: '#dbeafe', 'FIX-M': '#ede9fe' };
const MW_MODEL_TEXT  = { MTP: '#065f46', UTP: '#92400e', FIX: '#1e40af', 'FIX-M': '#5b21b6' };
function mwBadge(model, extra) {
    const bg = MW_MODEL_COLOR[model] || '#f1f5f9';
    const fg = MW_MODEL_TEXT[model]  || '#475569';
    return `<span class="mw-badge" style="background:${bg};color:${fg};${extra || ''}">${model}</span>`;
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
// Kurzform für die enge „ab"-Zelle: TT.MM.JJ
function mwShortDate(iso) {
    const s = mwIso(iso);
    if (!s) return '';
    return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(2, 4);
}
function mwTodayIso() { return new Date().toISOString().slice(0, 10); }

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
// Geplante Folge-Version derselben Zelle, deren Gültig-ab GENAU auf abDatum fällt.
function mwFindFuture(cur, abDatum) {
    if (!abDatum) return null;
    return mwAllRules.find(r =>
        r.id !== cur.id
        && r.jobGroupCode === cur.jobGroupCode
        && r.employmentModelCode === cur.employmentModelCode
        && r.educationLevelId === cur.educationLevelId
        && r.salaryType === cur.salaryType
        && (r.ageMax ?? null) === (cur.ageMax ?? null)
        && mwIso(r.validFrom) === abDatum) || null;
}

// Beim Öffnen der Page (showPage → mwInit): einfach laden.
// Kein Stichtag-Filter mehr (Walter-Vorgabe 23.05.2026) — die Matrix zeigt immer
// die heute gültige Version + automatisch die nächste zukünftige (falls vorhanden).
function mwInit() {
    mwLoad();
}

// IMMER die komplette Historie laden (all=true) — daraus rendert mwRender()
// sowohl die Stichtag-Matrix (aktuell gültige Sätze) als auch die geplanten
// „ab"-Sätze und die Versions-Historie. Der inLohnVerwendet-Flag pro Satz
// kommt vom Backend (überlappt eine eingefrorene Lohnperiode → gesperrt).
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

// „alt vs. neu" rein über inLohnVerwendet (NICHT über das heutige Datum,
// Walter-Vorgabe 23.05.2026): „in Verwendung/alt" = der Satz wurde in einer
// eingefrorenen Lohnperiode verwendet; „neu" = noch nicht verwendet, editierbar.
function mwVf(r) { return mwIso(r.validFrom); }

// Gruppiert Regeln pro Zellen-Schlüssel und liefert je { used, unused, newest }:
//   used   = neueste bereits verwendete Version (inLohnVerwendet)
//   unused = neueste noch nicht verwendete Version (editierbar)
//   newest = neueste Version überhaupt (egal ob verwendet)
function mwAggregate(rules, keyFn) {
    const map = {};
    rules.forEach(r => {
        const k = keyFn(r);
        const c = map[k] || (map[k] = { used: null, unused: null, newest: null });
        if (!c.newest || mwVf(r) > mwVf(c.newest)) c.newest = r;
        if (r.inLohnVerwendet) { if (!c.used   || mwVf(r) > mwVf(c.used))   c.used   = r; }
        else                   { if (!c.unused || mwVf(r) > mwVf(c.unused)) c.unused = r; }
    });
    return map;
}

// Ist die neueste Generation noch NICHT verwendet → man editiert sie direkt,
// eine NEUE Folge-Version ist (noch) nicht sinnvoll. Erst wenn die neueste
// Generation in einer Periode verwendet wurde, kann eine neue angelegt werden.
function mwNewestGenerationEditable() {
    if (!mwAllRules.length) return true;
    const globalMax = mwAllRules.map(mwVf).sort().slice(-1)[0];
    return !mwAllRules.some(r => mwVf(r) === globalMax && r.inLohnVerwendet);
}

function mwRender() {
    const cont   = document.getElementById('mwContainer');
    const infoEl = document.getElementById('mwInfo');
    if (!cont) return;

    mwShowAll = document.getElementById('mwShowAll')?.checked ?? false;

    // Zweispaltig (in Verwendung + neu), wenn es eine bereits verwendete Generation
    // gibt UND eine neuere noch-nicht-verwendete (= geplant/editierbar) darüber.
    const usedDates   = mwAllRules.filter(r => r.inLohnVerwendet).map(mwVf).sort();
    const unusedDates = mwAllRules.filter(r => !r.inLohnVerwendet).map(mwVf).sort();
    const usedMax   = usedDates.length   ? usedDates[usedDates.length - 1]     : null;
    const unusedMax = unusedDates.length ? unusedDates[unusedDates.length - 1] : null;
    const split   = !!(usedMax && unusedMax && unusedMax > usedMax);
    const abDatum = split ? unusedMax : '';

    // Date-Picker-Floor (frühestes Datum nach der letzten abgeschlossenen Periode).
    const cdEl = document.getElementById('mwCreateDate');
    if (cdEl && mwFirstAllowed) cdEl.min = mwFirstAllowed;

    // „+ Folge-Version anlegen" nur möglich, wenn die neueste Generation bereits
    // verwendet wurde (sonst editiert man sie direkt).
    const createBtn = document.getElementById('mwCreateBtn');
    if (createBtn) {
        const blocked = mwNewestGenerationEditable();
        createBtn.disabled = blocked;
        createBtn.style.opacity = blocked ? '0.45' : '';
        createBtn.style.cursor  = blocked ? 'not-allowed' : '';
        createBtn.title = blocked
            ? 'Die aktuellen Sätze sind noch nicht in einem Lohnlauf verwendet — bearbeite sie direkt in der Tabelle. Eine neue Folge-Version ist erst möglich, sobald sie in einer abgeschlossenen Periode verwendet wurde.'
            : 'Legt für das gewählte Datum eine vollständige Folge-Version an (Kopie der aktuellen Sätze, danach pro Zelle anpassbar).';
    }

    if (mwShowAll) {
        const rel = mwRelevantVersions(mwAllRules);
        if (infoEl) infoEl.textContent = `${rel.length} Versionen (in Verwendung + neu)`;
        cont.innerHTML = rel.length
            ? mwRenderHistory(rel)
            : '<div class="mw-muted" style="padding:30px;text-align:center;font-style:italic">Keine Sätze erfasst.</div>';
        return;
    }

    if (!mwAllRules.length) {
        if (infoEl) infoEl.textContent = '0 Sätze';
        cont.innerHTML = '<div class="mw-muted" style="padding:30px;text-align:center;font-style:italic">Keine Sätze erfasst.</div>';
        return;
    }
    if (infoEl) infoEl.textContent = (split ? 'in Verwendung + neu' : 'aktuelle Sätze') + (abDatum ? ` · neu ab ${mwFmtDate(abDatum)}` : '');

    const youthRules = mwAllRules.filter(r => r.ageMax != null);

    let html = '';
    if (split) html += mwRenderPlanHint(abDatum);
    html += mwRenderMatrix('Stundenlöhne', 'CHF / Std.',        'hourly',  split, abDatum);
    html += mwRenderMatrix('Monatslöhne',  'CHF / Mt. · 100 %', 'monthly', split, abDatum);
    html += mwRenderYouth(youthRules, split, abDatum);
    cont.innerHTML = html;
}

// Pro Satz die relevanten max-2 Versionen — basierend auf NUTZUNG (nicht Datum):
// die neueste verwendete (in Verwendung) + die neuere noch nicht verwendete (neu).
function mwRelevantVersions(rules) {
    const agg = mwAggregate(rules, r => [r.salaryType, r.jobGroupCode, r.employmentModelCode, r.educationLevelId, r.ageMax ?? ''].join('|'));
    const out = [];
    Object.values(agg).forEach(c => {
        if (c.used) out.push(c.used);
        if (c.unused && (!c.used || mwVf(c.unused) > mwVf(c.used))) out.push(c.unused);
    });
    return out;
}

// Erklär-Banner über der Matrix, wenn eine neue (noch nicht verwendete) Version
// neben der bereits verwendeten existiert.
function mwRenderPlanHint(abDatum) {
    const body = `Linke Spalte „in Verwendung" = bereits in einem Lohnlauf verwendet (grau, Referenz). Rechte Spalte <b>neu ab ${mwFmtDate(abDatum)}</b> = neuer Satz, anklicken zum Bestätigen/Anpassen. <b style="color:#047857">Grün</b> = Betrag geändert, <b style="color:#d97706">Orange</b> = bestätigt &amp; unverändert, <b style="color:#dc2626">Rot</b> = noch nicht bestätigt.`;
    return `<div class="card mw-section" style="overflow:visible"><div class="mw-planhint">${body}</div></div>`;
}

// Eine aktuelle (Stichtag-)Betragszelle. Reine Referenz, daher KEIN Schloss-Icon
// mehr: gesperrte (im Lohnlauf verwendete) Sätze werden hellgrau dargestellt und
// sind nicht direkt editierbar (Klick erklärt warum). Nicht-gesperrte (z.B. neue,
// noch ungenutzte) Sätze bleiben editierbar.
function mwCurCell(cur) {
    if (cur.inLohnVerwendet)
        return `<td class="mw-amount mw-cur-ro" onclick="mwLockedInfo()" title="In einem Lohnlauf verwendet — nur über „Folge-Version anlegen" änderbar"><span>${mwAmt(cur.amount)}</span></td>`;
    return `<td class="mw-amount" onclick="mwEdit(${cur.id})" title="Betrag bearbeiten"><span>${mwAmt(cur.amount)}</span></td>`;
}

// Die neue (noch nicht verwendete) Betragszelle NEBEN der „in Verwendung"-Spalte.
// `edt` = der editierbare neue Satz, `ref` = die verwendete Referenz (für Farbe).
// Drei-Farben-Logik (Walter-Vorgabe 23.05.2026):
//   GRÜN   = Betrag ≠ Referenz (geändert)
//   ORANGE = bestätigt (gespeichert), aber unverändert
//   ROT    = noch nicht bestätigt (frisch kopiert, noch zu prüfen)
function mwFutCell(edt, ref) {
    if (!edt) return `<td class="mw-empty mw-fut-col">–</td>`;
    let cls, hint;
    if (ref && Number(edt.amount) !== Number(ref.amount)) { cls = 'mw-fut-changed';  hint = 'geänderter Satz'; }
    else if (edt.confirmed)                               { cls = 'mw-fut-reviewed'; hint = 'bestätigt, unverändert'; }
    else                                                  { cls = 'mw-fut-same';     hint = 'noch nicht bestätigt'; }
    return `<td class="mw-amount mw-fut-col ${cls}" onclick="mwEdit(${edt.id})" title="Neuer Satz (${hint}) — bearbeiten"><span>${mwAmt(edt.amount)}</span></td>`;
}

function mwRenderMatrix(title, unit, salaryType, split, abDatum) {
    const all = mwAllRules.filter(r => r.salaryType === salaryType && r.ageMax == null);
    const agg = mwAggregate(all, r => r.jobGroupCode + '|' + r.employmentModelCode + '|' + r.educationLevelId);

    // Zeilen = vorhandene (Funktion, Modell)-Kombis.
    const seen = new Set();
    const rowKeys = [];
    all.forEach(r => {
        const k = r.jobGroupCode + '|' + r.employmentModelCode;
        if (!seen.has(k)) { seen.add(k); rowKeys.push({ g: r.jobGroupCode, m: r.employmentModelCode }); }
    });
    rowKeys.sort((a, b) => (mwGroupIdx(a.g) - mwGroupIdx(b.g)) || (mwModelIdx(a.m) - mwModelIdx(b.m)));

    // Kopf: ohne neue Version eine Zeile; mit neuer Version zwei Zeilen —
    // Ausbildungsstufe überspannt je 2 Spalten, Subzeile „in Verwendung | neu ab".
    let thead;
    if (!split) {
        const head = MW_EDU.map(e => `<th>${e.label}<span class="mw-sub">${e.sub}</span></th>`).join('');
        thead = `<tr><th class="mw-th-row">Funktion / Modell</th>${head}</tr>`;
    } else {
        const top = MW_EDU.map(e => `<th colspan="2" class="mw-edu-top">${e.label}<span class="mw-sub">${e.sub}</span></th>`).join('');
        const sub = MW_EDU.map(() => `<th class="mw-sub-cur">in Verwendung</th><th class="mw-fut-col mw-sub-fut">neu ab ${mwShortDate(abDatum)}</th>`).join('');
        thead = `<tr><th class="mw-th-row" rowspan="2">Funktion / Modell</th>${top}</tr><tr>${sub}</tr>`;
    }

    const colCount = 1 + MW_EDU.length * (split ? 2 : 1);
    let body;
    if (!rowKeys.length) {
        body = `<tr><td colspan="${colCount}" class="mw-muted" style="padding:20px;text-align:center;font-style:italic">Keine Sätze.</td></tr>`;
    } else {
        body = rowKeys.map(rk => {
            const cells = MW_EDU.map(e => {
                const c = agg[rk.g + '|' + rk.m + '|' + e.id];
                if (!split) {
                    const eff = c ? c.newest : null;     // neueste Version (editierbar wenn unbenutzt, sonst grau)
                    return eff ? mwCurCell(eff) : `<td class="mw-empty">–</td>`;
                }
                const ref = c ? c.used : null;           // links: verwendete Referenz (grau)
                const edt = c ? c.unused : null;         // rechts: neuer editierbarer Satz
                const leftTd = ref ? mwCurCell(ref) : `<td class="mw-empty">–</td>`;
                return leftTd + mwFutCell(edt, ref);
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

function mwRenderYouth(rules, split, abDatum) {
    if (!rules.length) return '';
    const agg = mwAggregate(rules, r => [r.jobGroupCode, r.employmentModelCode, r.educationLevelId, r.ageMax, r.salaryType].join('|'));
    const cells = Object.values(agg).sort((a, b) => {
        const ra = a.newest, rb = b.newest;
        return (mwGroupIdx(ra.jobGroupCode) - mwGroupIdx(rb.jobGroupCode))
            || (mwModelIdx(ra.employmentModelCode) - mwModelIdx(rb.employmentModelCode))
            || (ra.educationLevelId - rb.educationLevelId)
            || ((ra.ageMax ?? 0) - (rb.ageMax ?? 0));
    });

    const rows = cells.map(c => {
        const r = c.newest;
        const leftRule = split ? c.used : c.newest;
        const leftTd = leftRule ? mwCurCell(leftRule) : `<td class="mw-empty">–</td>`;
        const rightTd = split ? mwFutCell(c.unused, c.used) : '';
        return `<tr>
            <td class="mw-row-label">${MW_GROUP_LABEL[r.jobGroupCode] || r.jobGroupCode}</td>
            <td>${mwBadge(r.employmentModelCode, 'margin-left:0')}</td>
            <td class="mw-muted">${mwEduLabel(r.educationLevelId)}</td>
            <td class="mw-muted">bis ${r.ageMax} J.</td>
            <td class="mw-muted">${r.salaryType === 'hourly' ? 'CHF / Std.' : 'CHF / Mt.'}</td>
            ${leftTd}${rightTd}
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
                <th>${split ? 'in Verwendung' : 'Betrag'}</th>
                ${split ? `<th class="mw-fut-col mw-sub-fut">neu ab ${mwShortDate(abDatum)}</th>` : ''}
            </tr></thead>
            <tbody>${rows}</tbody>
        </table>
        </div>
    </div>`;
}

function mwRenderHistory(rules) {
    // Sortierung (Walter-Vorgabe 23.05.2026): Modell → Ausbildung → Funktion →
    // Alter → gültig ab. „Gültig ab" als innerster Schlüssel hält die zwei
    // Versionen (aktuell + geplant) desselben Satzes direkt untereinander.
    const sorted = [...rules].sort((a, b) =>
        (mwModelIdx(a.employmentModelCode) - mwModelIdx(b.employmentModelCode))
        || (a.educationLevelId - b.educationLevelId)
        || (mwGroupIdx(a.jobGroupCode) - mwGroupIdx(b.jobGroupCode))
        || ((a.ageMax ?? 9999) - (b.ageMax ?? 9999))
        || (String(a.validFrom || '').localeCompare(String(b.validFrom || ''))));

    const rows = sorted.map(r => `
        <tr style="${r.isActive ? '' : 'opacity:0.5'}">
            <td class="mw-row-label">${MW_GROUP_LABEL[r.jobGroupCode] || r.jobGroupCode}</td>
            <td>${mwBadge(r.employmentModelCode, 'margin-left:0')}</td>
            <td class="mw-muted">${mwEduLabel(r.educationLevelId)}</td>
            <td class="mw-muted">${r.salaryType === 'hourly' ? 'CHF / Std.' : 'CHF / Mt.'}${r.ageMax != null ? ` · ≤${r.ageMax} J.` : ''}</td>
            ${r.inLohnVerwendet
                ? `<td class="mw-amount mw-locked" style="cursor:default" onclick="mwLockedInfo()" title="In einem Lohnlauf verwendet — gesperrt"><span><span class="mw-lock">🔒</span>${mwAmt(r.amount)}</span></td>`
                : `<td class="mw-amount" onclick="mwEdit(${r.id})" title="Betrag bearbeiten"><span>${mwAmt(r.amount)}</span></td>`}
            <td class="mw-muted" style="font-size:12px">${mwFmtDate(r.validFrom)}</td>
            <td class="mw-muted" style="font-size:12px">${mwFmtDate(r.validTo)}</td>
        </tr>`).join('');

    return `<div class="card mw-section">
        <div class="mw-section-head">Versionen pro Satz<span class="mw-unit">aktuell + nächste geplante (max. 2)</span></div>
        <table class="mw-table">
            <thead><tr>
                <th class="mw-th-row">Funktion</th>
                <th class="mw-th-row">Modell</th>
                <th class="mw-th-row">Ausbildung</th>
                <th class="mw-th-row">Einheit</th>
                <th>Betrag</th>
                <th class="mw-th-row">Gültig ab</th>
                <th class="mw-th-row">Gültig bis</th>
            </tr></thead>
            <tbody>${rows}</tbody>
        </table>
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

// Hinweis-Toast für gesperrte (in einem Lohnlauf verwendete) Sätze.
function mwLockedInfo() {
    showToast('Dieser Mindestlohn wurde bereits in einem Lohnlauf verwendet und ist gesperrt. Für eine Änderung „+ Folge-Version anlegen" und den geplanten Satz ab dem neuen Datum anpassen.', 'info');
}

function mwEdit(id) {
    const r = mwAllRules.find(x => x.id === id);
    if (!r) return;
    // Sicherheitsnetz: gesperrte Sätze öffnen kein Edit-Modal (Backend gibt sonst 409).
    if (r.inLohnVerwendet) { mwLockedInfo(); return; }

    const unit = r.salaryType === 'hourly' ? 'CHF / Std.' : 'CHF / Mt.';
    const isFuture = mwIso(r.validFrom) > mwTodayIso();
    mwOverlay(`
        <h3 style="margin:0 0 6px;font-size:16px">${isFuture ? 'Geplanten Mindestlohn bearbeiten' : 'Mindestlohn bearbeiten'}</h3>
        <p class="mw-muted" style="margin:0 0 18px;font-size:13px;line-height:1.5">
            ${MW_GROUP_LABEL[r.jobGroupCode] || r.jobGroupCode} · ${r.employmentModelCode} · ${mwEduLabel(r.educationLevelId)}${r.ageMax != null ? ` · ≤${r.ageMax} J.` : ''}<br>
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
            // 409 MINWAGE_LOCKED: Satz wurde inzwischen in einem Lohnlauf verwendet.
            const data = await res.json().catch(() => ({}));
            showToast(data.message || data.error || ('Speichern fehlgeschlagen (HTTP ' + res.status + ')'), 'error');
            if (res.status === 409) { mwCloseOverlay(); mwLoad(); }   // Lock-Flag neu laden
            return;
        }
        mwCloseOverlay();
        showToast('Betrag gespeichert', 'success');
        mwLoad();
    } catch (e) { showToast('Fehler: ' + e.message, 'error'); }
}

// „+ Folge-Version anlegen" — legt für das gewählte „Geplante Sätze ab"-Datum
// eine vollständige Kopie der aktuell offenen Sätze an (/copy). Danach sind die
// geplanten Beträge pro Zelle editierbar. Genau der „nur neue Lohn ab"-Pfad,
// über den auch gesperrte (im Lohnlauf verwendete) Sätze geändert werden.
async function mwCreateGeneration() {
    // Eine neue Folge-Version ist erst sinnvoll, wenn die neueste Generation
    // bereits in einem Lohnlauf verwendet wurde (Walter-Vorgabe 23.05.2026).
    // Solange sie editierbar (unbenutzt) ist, bearbeitet man sie direkt.
    if (mwNewestGenerationEditable()) {
        showToast('Die aktuellen Sätze sind noch nicht in einem Lohnlauf verwendet — bitte direkt in der Tabelle bearbeiten. Eine neue Folge-Version ist erst möglich, sobald sie in einer abgeschlossenen Periode verwendet wurde.', 'info');
        return;
    }
    const d = document.getElementById('mwCreateDate')?.value;
    if (!d) {
        showToast('Bitte zuerst rechts ein „Neue Sätze ab"-Datum wählen.', 'error');
        document.getElementById('mwCreateDate')?.focus();
        return;
    }
    if (mwFirstAllowed && d < mwFirstAllowed) {
        showToast(`Das Gültig-ab-Datum muss am oder nach dem ${mwFmtDate(mwFirstAllowed)} liegen — frühester Termin nach der letzten abgeschlossenen Lohnperiode (über alle Filialen).`, 'error');
        return;
    }
    if (mwAllRules.some(r => mwIso(r.validFrom) === d)) {
        showToast(`Für den ${mwFmtDate(d)} existiert bereits eine Version. Sind das deine aktuellen Sätze, kannst du sie direkt in der Tabelle anklicken und bearbeiten — „+ Folge-Version anlegen" ist nur für ein NEUES, künftiges Datum.`, 'info');
        return;
    }
    if (!confirm(`Folge-Version ab ${mwFmtDate(d)} anlegen?\n\nAlle aktuell offenen Sätze werden kopiert und auf den Vortag begrenzt. Danach kannst du die geplanten Beträge pro Zelle anpassen.`)) return;
    try {
        const res  = await fetch('/api/minimum-wage-rules/copy', {
            method: 'POST', headers: ah(), body: JSON.stringify({ effectiveDate: d })
        });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) { showToast(data.error || ('Anlegen fehlgeschlagen (HTTP ' + res.status + ')'), 'error'); return; }
        showToast(`${data.copied} Sätze ab ${mwFmtDate(d)} angelegt — jetzt pro Zelle anpassbar`, 'success');
        const cd = document.getElementById('mwCreateDate'); if (cd) cd.value = '';
        mwLoad();   // Historie neu laden → geplante Spalte erscheint automatisch
    } catch (e) { showToast('Fehler: ' + e.message, 'error'); }
}

function mwOpenCopy() {
    mwOverlay(`
        <h3 style="margin:0 0 6px;font-size:16px">Neue Sätze ab Datum erstellen</h3>
        <p class="mw-muted" style="margin:0 0 18px;font-size:13px;line-height:1.5">
            Kopiert alle aktuell gültigen Sätze auf ein neues Gültig-ab-Datum. Die
            bisherigen Sätze werden automatisch auf den Vortag begrenzt. Danach kannst
            du die Beträge der neuen Sätze anpassen.
        </p>
        <label class="mw-muted" style="font-size:12px">Gültig ab</label>
        <input id="mwCopyDate" type="date" value="${mwTodayIso()}"
               style="width:100%;padding:10px;border:1px solid #e2e8f0;border-radius:8px;margin:5px 0 20px;font-size:15px">
        <div style="display:flex;gap:8px;justify-content:flex-end">
            <button class="btn btn-secondary" onclick="mwCloseOverlay()">Abbrechen</button>
            <button class="btn btn-primary" onclick="mwDoCopy()">Erstellen</button>
        </div>`);
}

async function mwDoCopy() {
    const d = document.getElementById('mwCopyDate')?.value;
    if (!d) { showToast('Bitte ein Datum wählen', 'error'); return; }
    try {
        const res  = await fetch('/api/minimum-wage-rules/copy', {
            method: 'POST', headers: ah(), body: JSON.stringify({ effectiveDate: d })
        });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) { showToast(data.error || ('Kopieren fehlgeschlagen (HTTP ' + res.status + ')'), 'error'); return; }
        mwCloseOverlay();
        showToast(`${data.copied} Sätze ab ${mwFmtDate(d)} erstellt`, 'success');
        const st = document.getElementById('mwStichtag');
        const sa = document.getElementById('mwShowAll');
        if (sa) sa.checked = false;
        if (st) { st.disabled = false; st.value = d; }
        mwLoad();
    } catch (e) { showToast('Fehler: ' + e.message, 'error'); }
}
