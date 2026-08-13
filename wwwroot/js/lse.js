// ════════════════════════════════════════════════════════════════════════
//  lse.js — BFS Lohnstrukturerhebung (Walter-Vorgabe 13.08.2026)
//
//  Phase 1: Startmaske (Kennzahlen) + Prüfmaske (🟢🟠🔴, Zeilen-Klick =
//  nur fehlende/problematische Angaben bearbeiten) + Lohnarten-/Code-Mapping.
//  XLS-Export + BFS-Vorschau folgen als Phase 2 (Server liefert 501-Hinweis).
//
//  Grundprinzip: Daten laden → Fehler korrigieren → prüfen → BFS-Datei.
//  Bestehendes OneCrew-Design (kd-day-Karten, Kohle-Pillen), keine neue
//  Designwelt. Filiale folgt dem globalen Sidebar-Selektor.
// ════════════════════════════════════════════════════════════════════════

let _lseYear = null;
let _lseScope = 'alle';          // 'alle' (Unternehmen) | 'filiale' (Sidebar-Filiale)
let _lseData = null;             // letzte Prüfungs-Antwort
let _lseConfig = null;           // Versions-Konfig (Code-Labels)
let _lseVersions = [];

const _lseBtnDark = 'background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 16px;font-size:13px;font-weight:600;cursor:pointer';
const _lseBtnLight = 'background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:7px 16px;font-size:13px;font-weight:600;cursor:pointer;color:#3f3f3f';
const _lseEsc = s => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

function _lseCpId() {
    return _lseScope === 'filiale' && typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId
        ? Number(fixedCompanyProfileId) : null;
}

async function lseInit() {
    const el = document.getElementById('lseYearSel');
    if (!el) return;
    try {
        const r = await fetch('/api/lse/versions', { headers: ah() });
        _lseVersions = r.ok ? await r.json() : [];
    } catch { _lseVersions = []; }
    if (!_lseVersions.length) {
        document.getElementById('lseStatusCards').innerHTML =
            `<div style="padding:14px;background:#fef2f2;border:1px solid #fca5a5;border-radius:12px;color:#991b1b">Keine LSE-Version konfiguriert (lse_version).</div>`;
        return;
    }
    if (_lseYear == null) _lseYear = _lseVersions.find(v => v.isActive)?.surveyYear ?? _lseVersions[0].surveyYear;
    el.innerHTML = _lseVersions.map(v =>
        `<option value="${v.surveyYear}" ${v.surveyYear === _lseYear ? 'selected' : ''}>${v.surveyYear} (Spez. ${_lseEsc(v.specVersion || '–')})</option>`).join('');
    el._lqRefresh?.();
    await lseLoadConfig();
    await lseLoadStatus();
}

async function lseLoadConfig() {
    try {
        const r = await fetch(`/api/lse/config?year=${_lseYear}`, { headers: ah() });
        _lseConfig = r.ok ? await r.json() : null;
    } catch { _lseConfig = null; }
}

function lseYearChanged() {
    _lseYear = parseInt(document.getElementById('lseYearSel')?.value, 10) || _lseYear;
    _lseData = null;
    document.getElementById('lseBody').innerHTML = '';
    lseLoadConfig().then(lseLoadStatus);
}

function lseSetScope(s) {
    _lseScope = s;
    document.getElementById('lseScopeAlle')?.style.setProperty('background', s === 'alle' ? '#3f3f3f' : 'rgba(255,255,255,0.55)');
    document.getElementById('lseScopeAlle')?.style.setProperty('color', s === 'alle' ? '#fff' : '#3f3f3f');
    document.getElementById('lseScopeFil')?.style.setProperty('background', s === 'filiale' ? '#3f3f3f' : 'rgba(255,255,255,0.55)');
    document.getElementById('lseScopeFil')?.style.setProperty('color', s === 'filiale' ? '#fff' : '#3f3f3f');
    lseLoadStatus();
}

// ── Startmaske: Kennzahlen ────────────────────────────────────────────────
async function lseLoadStatus() {
    const box = document.getElementById('lseStatusCards');
    if (!box) return;
    box.innerHTML = '<div style="color:#8b8b8b;padding:10px">Lade Kennzahlen…</div>';
    try {
        const cp = _lseCpId();
        const r = await fetch(`/api/lse/status?year=${_lseYear}${cp ? '&companyProfileId=' + cp : ''}`, { headers: ah() });
        const j = await r.json();
        if (!r.ok) { box.innerHTML = `<div style="padding:12px;background:#fef2f2;border-radius:10px;color:#991b1b">${_lseEsc(j.message || j.error)}</div>`; return; }
        const card = (label, val, color) => `
            <div class="kd-day" style="padding:12px 16px;min-width:150px">
                <div style="font-size:11px;color:#8b8b8b;font-weight:700;text-transform:uppercase;letter-spacing:.5px">${label}</div>
                <div style="font-size:24px;font-weight:800;color:${color || '#3f3f3f'}">${val}</div>
            </div>`;
        box.innerHTML = `
            <div style="display:flex;gap:12px;flex-wrap:wrap">
                ${card('Referenzmonat', 'Oktober ' + j.surveyYear)}
                ${card('Zu melden', j.total)}
                ${card('Vollständig', j.vollstaendig, '#166534')}
                ${card('Zu prüfen', j.zuPruefen, '#b45309')}
                ${card('Pflicht fehlt', j.fehlend, j.fehlend > 0 ? '#b91c1c' : '#166534')}
            </div>
            ${(j.unmappedLohnarten || j.unmappedStellung || j.unmappedVertrag) ? `
            <div style="margin-top:10px;padding:10px 14px;background:#fef9c3;border:1px solid #fde68a;border-radius:12px;font-size:12.5px;color:#854d0e">
                🧩 BFS-Zuordnung fehlt: ${j.unmappedLohnarten} Lohnart(en) · ${j.unmappedStellung} Funktion(en) · ${j.unmappedVertrag} Vertragsart(en)
                — <a style="cursor:pointer;text-decoration:underline;font-weight:700" onclick="lseShowMapping()">jetzt zuordnen</a>
            </div>` : ''}`;
    } catch (e) {
        box.innerHTML = `<div style="padding:12px;color:#991b1b">Verbindungsfehler: ${_lseEsc(e.message)}</div>`;
    }
}

// ── Prüfmaske ─────────────────────────────────────────────────────────────
async function lsePruefen() {
    const body = document.getElementById('lseBody');
    body.innerHTML = '<div style="color:#8b8b8b;padding:14px">Prüfe Daten…</div>';
    try {
        const cp = _lseCpId();
        const r = await fetch(`/api/lse/pruefung?year=${_lseYear}${cp ? '&companyProfileId=' + cp : ''}`, { headers: ah() });
        const j = await r.json();
        if (!r.ok) { body.innerHTML = `<div style="padding:12px;color:#991b1b">${_lseEsc(j.message || j.error)}</div>`; return; }
        _lseData = j;
        lseRenderPruefung();
        lseLoadStatus();
    } catch (e) {
        body.innerHTML = `<div style="padding:12px;color:#991b1b">Verbindungsfehler: ${_lseEsc(e.message)}</div>`;
    }
}

function _lseAmpel(status) {
    return status === 'GRUEN' ? '🟢' : status === 'ORANGE' ? '🟠' : '🔴';
}

function _lseNum(v) { return (v === null || v === undefined) ? '–' : Number(v).toFixed(2); }

function lseRenderPruefung() {
    const body = document.getElementById('lseBody');
    const rows = _lseData?.rows || [];
    if (!rows.length) { body.innerHTML = '<div style="padding:14px;color:#8b8b8b">Keine zu meldenden Mitarbeitenden gefunden.</div>'; return; }
    const posLbl = c => c ?? '–';
    const tr = rows.map(x => {
        const w = x.werte || {};
        return `
        <tr onclick="lseOpenRow(${x.employeeId})" style="cursor:pointer">
            <td style="text-align:center">${_lseAmpel(x.status)}</td>
            <td style="text-align:left;font-weight:600">${_lseEsc(x.name)}<div style="font-size:10.5px;color:#94a3b8">${_lseEsc(x.employeeNumber)} · ${_lseEsc(x.filiale)}</div></td>
            <td>${w.vn ? _lseEsc(w.vn) : '<span style="color:#b91c1c">fehlt</span>'}</td>
            <td>${w.education ?? '<span style="color:#b91c1c">–</span>'}</td>
            <td>${posLbl(w.position) ?? '–'}</td>
            <td>${w.contract ?? '<span style="color:#b91c1c">–</span>'}</td>
            <td>${_lseNum(w.contractualWorkingTime)}</td>
            <td style="max-width:170px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${_lseEsc(w.practicedProfessionOct || '–')}</td>
            <td>${_lseNum(w.salaryOct)}</td>
            <td>${_lseNum(w.earnings13th)}</td>
        </tr>
        <tr id="lseRow${x.employeeId}" style="display:none"><td colspan="10" style="text-align:left;background:#faf8f5"></td></tr>`;
    }).join('');
    body.innerHTML = `
        <div class="kd-day" style="padding:12px">
        <div style="overflow:auto">
        <table style="border-collapse:collapse;font-size:12px;width:100%">
            <thead><tr style="color:#8b8b8b;font-size:10.5px;text-transform:uppercase;letter-spacing:.4px">
                <th style="padding:6px 8px">Status</th><th style="text-align:left;padding:6px 8px">Mitarbeiter</th>
                <th>AHV</th><th>Ausbildung</th><th>Stellung</th><th>Vertrag</th>
                <th>Arbeitszeit</th><th style="text-align:left">Beruf</th><th>Oktoberlohn</th><th>13. ML Jahr</th>
            </tr></thead>
            <tbody style="text-align:center">${tr}</tbody>
        </table></div></div>`;
    body.querySelectorAll('tbody td').forEach(td => { td.style.padding = '6px 8px'; td.style.borderTop = '1px solid rgba(60,55,48,0.08)'; });
}

// Zeilen-Klick: NUR die fehlenden/problematischen Angaben bearbeiten.
async function lseOpenRow(empId) {
    const row = _lseData?.rows?.find(x => x.employeeId === empId);
    const tr = document.getElementById(`lseRow${empId}`);
    if (!row || !tr) return;
    if (tr.style.display !== 'none') { tr.style.display = 'none'; return; }
    document.querySelectorAll('[id^="lseRow"]').forEach(x => { x.style.display = 'none'; });
    tr.style.display = '';
    const cell = tr.firstElementChild;
    cell.innerHTML = '<div style="padding:10px;color:#8b8b8b">Lade…</div>';
    let lse = {};
    try {
        const r = await fetch(`/api/lse/employee/${empId}`, { headers: ah() });
        if (r.ok) lse = await r.json();
    } catch { }
    const codes = _lseConfig?.codes || {};
    const opt = (list, val) => '<option value="">–</option>' + (list || []).map(c =>
        `<option value="${c.code}" ${val === c.code ? 'selected' : ''}>${c.code} — ${_lseEsc(c.label)}</option>`).join('');
    const probleme = [...(row.fehler || []).map(f => `<li style="color:#b91c1c">${_lseEsc(f)}</li>`),
                      ...(row.hinweise || []).map(h => `<li style="color:#b45309">${_lseEsc(h)}</li>`)];
    cell.innerHTML = `
        <div style="padding:10px 12px">
            ${probleme.length ? `<ul style="margin:0 0 10px 18px;font-size:12.5px">${probleme.join('')}</ul>`
                              : '<div style="color:#166534;font-size:12.5px;margin-bottom:8px">✓ Keine offenen Punkte.</div>'}
            <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end">
                <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Ausbildung (BFS 1–8)
                    <select id="lseE${empId}" class="no-liquid" style="padding:5px 8px;border:1px solid #cbd5e1;border-radius:8px;min-width:250px">${opt(codes.education, lse.education)}</select></label>
                <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Hochschultitel (1–3)
                    <select id="lseU${empId}" class="no-liquid" style="padding:5px 8px;border:1px solid #cbd5e1;border-radius:8px;min-width:160px">${opt(codes.universityDegree, lse.universityDegree)}</select></label>
                <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Stellung-Override (1–5, leer = aus Funktion)
                    <select id="lseP${empId}" class="no-liquid" style="padding:5px 8px;border:1px solid #cbd5e1;border-radius:8px;min-width:210px">${opt(codes.position, lse.positionOverride)}</select></label>
                <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Ausgeübter Beruf (Klartext)
                    <input id="lseB${empId}" maxlength="255" value="${_lseEsc(lse.practicedProfession || '')}" placeholder="${_lseEsc(row.werte?.practicedProfessionOct || 'z.B. Restaurantmitarbeiter/in')}" style="padding:5px 8px;border:1px solid #cbd5e1;border-radius:8px;min-width:230px"></label>
                <button onclick="lseSaveRow(${empId})" style="${_lseBtnDark}">Speichern</button>
            </div>
        </div>`;
}

async function lseSaveRow(empId) {
    const g = id => document.getElementById(id + empId);
    const dto = {
        education: parseInt(g('lseE')?.value, 10) || null,
        universityDegree: parseInt(g('lseU')?.value, 10) || null,
        positionOverride: parseInt(g('lseP')?.value, 10) || null,
        practicedProfession: g('lseB')?.value || null,
    };
    const r = await fetch(`/api/lse/employee/${empId}`, { method: 'PUT', headers: ah(), body: JSON.stringify(dto) });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen', 'error'); return; }
    showToast('BFS-Angaben gespeichert', 'success');
    lsePruefen();
}

// ── Mapping (Lohnarten + Stellung/Vertrag) ────────────────────────────────
async function lseShowMapping() {
    const body = document.getElementById('lseBody');
    body.innerHTML = '<div style="color:#8b8b8b;padding:14px">Lade Zuordnungen…</div>';
    const cp = _lseCpId();
    let lm = null, cm = [];
    try {
        const r1 = await fetch(`/api/lse/mapping/lohnarten?year=${_lseYear}${cp ? '&companyProfileId=' + cp : ''}`, { headers: ah() });
        lm = r1.ok ? await r1.json() : null;
        const r2 = await fetch('/api/lse/mapping/codes', { headers: ah() });
        cm = r2.ok ? await r2.json() : [];
    } catch { }
    if (!lm) { body.innerHTML = '<div style="padding:12px;color:#991b1b">Zuordnungen nicht ladbar.</div>'; return; }

    const katOpt = sel => '<option value="">— BFS-Zuordnung fehlt —</option>'
        + lm.kategorien.map(k => `<option value="${k}" ${k === sel ? 'selected' : ''}>${k}</option>`).join('');
    const keyId = code => encodeURIComponent(code).replace(/%/g, '_');
    const zeile = (code, bez, kat, confirmed) => `
        <tr>
            <td style="font-family:monospace">${_lseEsc(code)}</td>
            <td style="text-align:left">${_lseEsc(bez || '')}</td>
            <td><select class="no-liquid" id="lseKat_${keyId(code)}" style="padding:4px 8px;border:1px solid #cbd5e1;border-radius:7px;min-width:210px">${katOpt(kat)}</select></td>
            <td>${confirmed ? '<span style="color:#166534;font-weight:700">✓</span>' : '<span style="color:#b91c1c;font-weight:700">offen</span>'}</td>
            <td><button onclick="lseSaveLohnart('${_lseEsc(code).replace(/'/g, '')}')" style="${_lseBtnLight};padding:3px 10px;font-size:12px">Zuordnen</button></td>
        </tr>`;
    const bestandKeys = new Set((lm.mappings || []).map(m => m.lohnartCode));
    const lohnRows = (lm.mappings || []).map(m => zeile(m.lohnartCode, m.bezeichnung, m.bfsKategorie, m.confirmed)).join('')
        + (lm.offen || []).filter(o => !bestandKeys.has(o.key)).map(o => zeile(o.key, o.bezeichnung + ` · ${o.vorkommen}×`, null, false)).join('');

    const codes = _lseConfig?.codes || {};
    const codeOpt = (list, val) => '<option value="">— fehlt —</option>' + (list || []).map(c =>
        `<option value="${c.code}" ${val === c.code ? 'selected' : ''}>${c.code} — ${_lseEsc(c.label)}</option>`).join('');
    const cmByTyp = typ => cm.filter(m => m.mappingTyp === typ);
    const stellungSrc = new Set([...cmByTyp('STELLUNG').map(m => m.sourceCode), ...(_lseData?.unmappedStellung || [])]);
    const vertragSrc = new Set([...cmByTyp('VERTRAG').map(m => m.sourceCode), ...(_lseData?.unmappedVertrag || [])]);
    const codeZeile = (typ, src) => {
        const m = cm.find(x => x.mappingTyp === typ && x.sourceCode === src);
        const list = typ === 'STELLUNG' ? codes.position : codes.contract;
        return `<tr>
            <td style="font-family:monospace;text-align:left">${_lseEsc(src)}</td>
            <td><select class="no-liquid" id="lseCM_${typ}_${_lseEsc(src)}" style="padding:4px 8px;border:1px solid #cbd5e1;border-radius:7px;min-width:250px">${codeOpt(list, m?.bfsCode)}</select></td>
            <td>${m?.confirmed ? '<span style="color:#166534;font-weight:700">✓</span>' : '<span style="color:#b91c1c;font-weight:700">offen</span>'}</td>
            <td><button onclick="lseSaveCode('${typ}', '${_lseEsc(src)}')" style="${_lseBtnLight};padding:3px 10px;font-size:12px">Zuordnen</button></td>
        </tr>`;
    };

    body.innerHTML = `
        <div class="kd-day" style="padding:14px;margin-bottom:12px">
            <div style="font-weight:800;margin-bottom:4px">🧩 BFS → Lohnarten-Zuordnung</div>
            <div style="font-size:12px;color:#8b8b8b;margin-bottom:10px">Jede Lohnart einmal kontrollieren und zuordnen — OneCrew verwendet die Zuordnung danach wieder. Nicht zugeordnete Lohnarten werden NIE automatisch einer Kategorie zugerechnet.</div>
            <div style="overflow:auto"><table style="border-collapse:collapse;font-size:12px;width:100%;text-align:center">
                <thead><tr style="color:#8b8b8b;font-size:10.5px;text-transform:uppercase"><th style="padding:5px 8px">Lohnart</th><th style="text-align:left">Bezeichnung</th><th>BFS-Kategorie</th><th>Bestätigt</th><th></th></tr></thead>
                <tbody>${lohnRows || '<tr><td colspan="5" style="padding:12px;color:#8b8b8b">Keine Lohnarten gefunden — zuerst «Daten prüfen» ausführen.</td></tr>'}</tbody>
            </table></div>
        </div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px">
            <div class="kd-day" style="padding:14px">
                <div style="font-weight:800;margin-bottom:8px">Berufliche Stellung (X, Funktion → BFS 1–5)</div>
                <table style="border-collapse:collapse;font-size:12px;width:100%;text-align:center"><tbody>
                    ${[...stellungSrc].sort().map(s => codeZeile('STELLUNG', s)).join('') || '<tr><td style="color:#8b8b8b;padding:10px">Keine offenen Funktionen.</td></tr>'}
                </tbody></table>
            </div>
            <div class="kd-day" style="padding:14px">
                <div style="font-weight:800;margin-bottom:8px">Vertragsart (Y, Modell → BFS 1–7)</div>
                <table style="border-collapse:collapse;font-size:12px;width:100%;text-align:center"><tbody>
                    ${[...vertragSrc].sort().map(s => codeZeile('VERTRAG', s)).join('') || '<tr><td style="color:#8b8b8b;padding:10px">Keine offenen Vertragsarten.</td></tr>'}
                </tbody></table>
            </div>
        </div>`;
    body.querySelectorAll('table td, table th').forEach(td => { td.style.padding = '5px 8px'; td.style.borderTop = '1px solid rgba(60,55,48,0.08)'; });
}

async function lseSaveLohnart(code) {
    const sel = document.getElementById('lseKat_' + encodeURIComponent(code).replace(/%/g, '_'));
    const dto = { lohnartCode: code, bfsKategorie: sel?.value || null };
    const r = await fetch('/api/lse/mapping/lohnarten', { method: 'PUT', headers: ah(), body: JSON.stringify(dto) });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Zuordnung fehlgeschlagen', 'error'); return; }
    showToast(`Lohnart «${code}» zugeordnet`, 'success');
    lseShowMapping();
}

async function lseSaveCode(typ, src) {
    const sel = document.getElementById(`lseCM_${typ}_${src}`);
    const dto = { mappingTyp: typ, sourceCode: src, bfsCode: parseInt(sel?.value, 10) || null };
    const r = await fetch('/api/lse/mapping/codes', { method: 'PUT', headers: ah(), body: JSON.stringify(dto) });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Zuordnung fehlgeschlagen', 'error'); return; }
    showToast('Zuordnung gespeichert', 'success');
    lseShowMapping();
}

// ── Phase-2-Knöpfe (Export/Vorschau) — zeigen den Server-Hinweis ─────────
async function lseVorschau() {
    const r = await fetch('/api/lse/vorschau', { headers: ah() });
    const j = await r.json().catch(() => ({}));
    showToast(j.message || 'BFS-Vorschau folgt in Phase 2.', 'info');
}
async function lseExport() {
    const r = await fetch('/api/lse/export', { headers: ah() });
    const j = await r.json().catch(() => ({}));
    showToast(j.message || 'XLS-Export folgt in Phase 2.', 'info');
}

// ── Bereich «BFS / Statistik» im MA-Detail (Restaurant Admin) ────────────
async function lseEmpBlockAppend(container, employeeId) {
    if (!container) return;
    let lse = {};
    try {
        const r = await fetch(`/api/lse/employee/${employeeId}`, { headers: ah() });
        if (r.ok) lse = await r.json();
    } catch { }
    if (!_lseConfig) {
        try {
            const rv = await fetch('/api/lse/versions', { headers: ah() });
            const vs = rv.ok ? await rv.json() : [];
            const y = vs.find(v => v.isActive)?.surveyYear;
            if (y) { const rc = await fetch(`/api/lse/config?year=${y}`, { headers: ah() }); _lseConfig = rc.ok ? await rc.json() : null; }
        } catch { }
    }
    const codes = _lseConfig?.codes || {};
    const opt = (list, val) => '<option value="">–</option>' + (list || []).map(c =>
        `<option value="${c.code}" ${val === c.code ? 'selected' : ''}>${c.code} — ${_lseEsc(c.label)}</option>`).join('');
    const div = document.createElement('div');
    div.id = 'lseEmpBlock';
    div.innerHTML = `
        <div class="emp-section-title" style="margin-top:22px">BFS / Statistik</div>
        <div style="background:#f6f3ee;border:1px solid #e7e1d8;border-radius:14px;padding:11px 14px;display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Höchste abgeschlossene Ausbildung (BFS)
                <select id="lseEmpEdu" class="no-liquid" style="padding:5px 8px;border:1px solid #cbd5e1;border-radius:8px;min-width:250px">${opt(codes.education, lse.education)}</select></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Hochschultitel
                <select id="lseEmpDeg" class="no-liquid" style="padding:5px 8px;border:1px solid #cbd5e1;border-radius:8px;min-width:150px">${opt(codes.universityDegree, lse.universityDegree)}</select></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Berufliche Stellung (Override)
                <select id="lseEmpPos" class="no-liquid" style="padding:5px 8px;border:1px solid #cbd5e1;border-radius:8px;min-width:200px">${opt(codes.position, lse.positionOverride)}</select></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Ausgeübter Beruf (Klartext, LSE)
                <input id="lseEmpBeruf" maxlength="255" value="${_lseEsc(lse.practicedProfession || '')}" placeholder="z.B. Restaurantmitarbeiter/in" style="padding:5px 8px;border:1px solid #cbd5e1;border-radius:8px;min-width:220px"></label>
            <button onclick="lseEmpBlockSave(${employeeId})" style="${_lseBtnDark};padding:6px 14px">Speichern</button>
        </div>`;
    container.appendChild(div);
}

async function lseEmpBlockSave(employeeId) {
    const dto = {
        education: parseInt(document.getElementById('lseEmpEdu')?.value, 10) || null,
        universityDegree: parseInt(document.getElementById('lseEmpDeg')?.value, 10) || null,
        positionOverride: parseInt(document.getElementById('lseEmpPos')?.value, 10) || null,
        practicedProfession: document.getElementById('lseEmpBeruf')?.value || null,
    };
    const r = await fetch(`/api/lse/employee/${employeeId}`, { method: 'PUT', headers: ah(), body: JSON.stringify(dto) });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen', 'error'); return; }
    showToast('BFS-Angaben gespeichert', 'success');
}
