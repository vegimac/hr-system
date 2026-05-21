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

function mwFmtDate(iso) {
    if (!iso) return '–';
    const s = String(iso).slice(0, 10);
    return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
}
function mwTodayIso() { return new Date().toISOString().slice(0, 10); }

// Beim Öffnen der Page (showPage → mwInit): Stichtag auf heute, dann laden.
function mwInit() {
    const d = document.getElementById('mwStichtag');
    if (d && !d.value) d.value = mwTodayIso();
    mwLoad();
}

async function mwLoad() {
    const cont = document.getElementById('mwContainer');
    if (!cont) return;
    cont.innerHTML = '<div class="mw-muted" style="padding:30px;text-align:center">Wird geladen…</div>';

    mwShowAll = document.getElementById('mwShowAll')?.checked ?? false;
    const date = document.getElementById('mwStichtag')?.value || mwTodayIso();
    const url  = mwShowAll ? '/api/minimum-wage-rules?all=true'
                           : `/api/minimum-wage-rules?date=${date}`;

    // Stichtag-Feld ausgrauen wenn „Alle Versionen" aktiv (Filter wirkt dann nicht)
    const stEl = document.getElementById('mwStichtag');
    if (stEl) stEl.disabled = mwShowAll;

    try {
        const res = await fetch(url, { headers: ah() });
        if (!res.ok) {
            cont.innerHTML = `<div style="color:#dc2626;padding:16px">Fehler beim Laden (HTTP ${res.status})</div>`;
            return;
        }
        mwAllRules = await res.json();
        mwRender();
    } catch (e) {
        cont.innerHTML = `<div style="color:#dc2626;padding:16px">Verbindungsfehler: ${e.message}</div>`;
    }
}

function mwRender() {
    const cont   = document.getElementById('mwContainer');
    const infoEl = document.getElementById('mwInfo');
    if (!cont) return;

    if (infoEl) infoEl.textContent = `${mwAllRules.length} Satz${mwAllRules.length !== 1 ? 'sätze' : ''}`;

    if (!mwAllRules.length) {
        cont.innerHTML = '<div class="mw-muted" style="padding:30px;text-align:center;font-style:italic">Keine Sätze für diesen Stichtag.</div>';
        return;
    }

    if (mwShowAll) { cont.innerHTML = mwRenderHistory(mwAllRules); return; }

    const main    = mwAllRules.filter(r => r.ageMax == null);
    const youth   = mwAllRules.filter(r => r.ageMax != null);
    const hourly  = main.filter(r => r.salaryType === 'hourly');
    const monthly = main.filter(r => r.salaryType === 'monthly');

    cont.innerHTML =
          mwRenderMatrix('Stundenlöhne', 'CHF / Std.',        hourly,  'hourly')
        + mwRenderMatrix('Monatslöhne',  'CHF / Mt. · 100 %', monthly, 'monthly')
        + mwRenderYouth(youth);
}

function mwRenderMatrix(title, unit, rules, salaryType) {
    // Zeilen = vorhandene (Funktion, Modell)-Kombis
    const seen = new Set();
    const rowKeys = [];
    rules.forEach(r => {
        const k = r.jobGroupCode + '|' + r.employmentModelCode;
        if (!seen.has(k)) { seen.add(k); rowKeys.push({ g: r.jobGroupCode, m: r.employmentModelCode }); }
    });
    rowKeys.sort((a, b) => (mwGroupIdx(a.g) - mwGroupIdx(b.g)) || (mwModelIdx(a.m) - mwModelIdx(b.m)));

    const cellMap = {};
    rules.forEach(r => { cellMap[r.jobGroupCode + '|' + r.employmentModelCode + '|' + r.educationLevelId] = r; });

    const head = MW_EDU.map(e => `<th>${e.label}<span class="mw-sub">${e.sub}</span></th>`).join('');

    let body;
    if (!rowKeys.length) {
        body = `<tr><td colspan="${MW_EDU.length + 1}" class="mw-muted" style="padding:20px;text-align:center;font-style:italic">Keine Sätze für diesen Stichtag.</td></tr>`;
    } else {
        body = rowKeys.map(rk => {
            const cells = MW_EDU.map(e => {
                const r = cellMap[rk.g + '|' + rk.m + '|' + e.id];
                if (!r) return `<td class="mw-empty">–</td>`;
                return `<td class="mw-amount" onclick="mwEdit(${r.id})" title="Betrag bearbeiten"><span>${mwAmt(r.amount)}</span></td>`;
            }).join('');
            return `<tr>
                <td class="mw-row-label">${MW_GROUP_LABEL[rk.g] || rk.g}${mwBadge(rk.m)}</td>
                ${cells}
            </tr>`;
        }).join('');
    }

    return `<div class="card mw-section">
        <div class="mw-section-head">${title}<span class="mw-unit">${unit}</span></div>
        <table class="mw-table">
            <thead><tr><th class="mw-th-row">Funktion / Modell</th>${head}</tr></thead>
            <tbody>${body}</tbody>
        </table>
    </div>`;
}

function mwRenderYouth(rules) {
    if (!rules.length) return '';
    const sorted = [...rules].sort((a, b) =>
        (mwGroupIdx(a.jobGroupCode) - mwGroupIdx(b.jobGroupCode))
        || (mwModelIdx(a.employmentModelCode) - mwModelIdx(b.employmentModelCode))
        || (a.educationLevelId - b.educationLevelId)
        || ((a.ageMax ?? 0) - (b.ageMax ?? 0)));

    const rows = sorted.map(r => `
        <tr>
            <td class="mw-row-label">${MW_GROUP_LABEL[r.jobGroupCode] || r.jobGroupCode}</td>
            <td>${mwBadge(r.employmentModelCode, 'margin-left:0')}</td>
            <td class="mw-muted">${mwEduLabel(r.educationLevelId)}</td>
            <td class="mw-muted">bis ${r.ageMax} J.</td>
            <td class="mw-muted">${r.salaryType === 'hourly' ? 'CHF / Std.' : 'CHF / Mt.'}</td>
            <td class="mw-amount" onclick="mwEdit(${r.id})" title="Betrag bearbeiten"><span>${mwAmt(r.amount)}</span></td>
        </tr>`).join('');

    return `<div class="card mw-section">
        <div class="mw-section-head mw-youth">Jugendliche — Sondersätze nach Alter (L-GAV)</div>
        <table class="mw-table">
            <thead><tr>
                <th class="mw-th-row">Funktion</th>
                <th class="mw-th-row">Modell</th>
                <th class="mw-th-row">Ausbildung</th>
                <th class="mw-th-row">Alter</th>
                <th class="mw-th-row">Einheit</th>
                <th>Betrag</th>
            </tr></thead>
            <tbody>${rows}</tbody>
        </table>
    </div>`;
}

function mwRenderHistory(rules) {
    const sorted = [...rules].sort((a, b) =>
        (a.salaryType.localeCompare(b.salaryType))
        || (mwGroupIdx(a.jobGroupCode) - mwGroupIdx(b.jobGroupCode))
        || (mwModelIdx(a.employmentModelCode) - mwModelIdx(b.employmentModelCode))
        || (a.educationLevelId - b.educationLevelId)
        || (String(a.validFrom || '').localeCompare(String(b.validFrom || ''))));

    const rows = sorted.map(r => `
        <tr style="${r.isActive ? '' : 'opacity:0.5'}">
            <td class="mw-row-label">${MW_GROUP_LABEL[r.jobGroupCode] || r.jobGroupCode}</td>
            <td>${mwBadge(r.employmentModelCode, 'margin-left:0')}</td>
            <td class="mw-muted">${mwEduLabel(r.educationLevelId)}</td>
            <td class="mw-muted">${r.salaryType === 'hourly' ? 'CHF / Std.' : 'CHF / Mt.'}${r.ageMax != null ? ` · ≤${r.ageMax} J.` : ''}</td>
            <td class="mw-amount" onclick="mwEdit(${r.id})" title="Betrag bearbeiten"><span>${mwAmt(r.amount)}</span></td>
            <td class="mw-muted" style="font-size:12px">${mwFmtDate(r.validFrom)}</td>
            <td class="mw-muted" style="font-size:12px">${mwFmtDate(r.validTo)}</td>
        </tr>`).join('');

    return `<div class="card mw-section">
        <div class="mw-section-head">Alle Versionen<span class="mw-unit">Historie</span></div>
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

function mwEdit(id) {
    const r = mwAllRules.find(x => x.id === id);
    if (!r) return;
    const unit = r.salaryType === 'hourly' ? 'CHF / Std.' : 'CHF / Mt.';
    mwOverlay(`
        <h3 style="margin:0 0 6px;font-size:16px">Mindestlohn bearbeiten</h3>
        <p class="mw-muted" style="margin:0 0 18px;font-size:13px;line-height:1.5">
            ${MW_GROUP_LABEL[r.jobGroupCode] || r.jobGroupCode} · ${r.employmentModelCode} · ${mwEduLabel(r.educationLevelId)}${r.ageMax != null ? ` · ≤${r.ageMax} J.` : ''}<br>
            gültig ab ${mwFmtDate(r.validFrom)}
        </p>
        <label class="mw-muted" style="font-size:12px">Betrag (${unit})</label>
        <input id="mwEditAmount" type="number" step="0.01" min="0" value="${Number(r.amount)}"
               style="width:100%;padding:10px;border:1px solid #e2e8f0;border-radius:8px;margin:5px 0 20px;font-size:15px;font-weight:600"
               onkeydown="if(event.key==='Enter')mwSaveAmount(${id})">
        <div style="display:flex;gap:8px;justify-content:flex-end">
            <button class="btn btn-secondary" onclick="mwCloseOverlay()">Abbrechen</button>
            <button class="btn btn-primary" onclick="mwSaveAmount(${id})">Speichern</button>
        </div>`);
    setTimeout(() => { const el = document.getElementById('mwEditAmount'); if (el) { el.focus(); el.select(); } }, 50);
}

async function mwSaveAmount(id) {
    const amt = parseFloat(document.getElementById('mwEditAmount')?.value);
    if (isNaN(amt) || amt < 0) { showToast('Ungültiger Betrag', 'error'); return; }
    try {
        const res = await fetch(`/api/minimum-wage-rules/${id}`, {
            method: 'PUT', headers: ah(), body: JSON.stringify({ amount: amt })
        });
        if (!res.ok) { showToast('Speichern fehlgeschlagen (HTTP ' + res.status + ')', 'error'); return; }
        mwCloseOverlay();
        showToast('Betrag gespeichert', 'success');
        mwLoad();
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
