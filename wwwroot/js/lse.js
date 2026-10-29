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
let _statKatalog = null;         // Ausbildung + Stellung aus der Tabelle

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

// ── Katalog (System → Verzeichnisse) ─────────────────────────────────────
async function statKatalogLaden(frisch) {
    if (_statKatalog && !frisch) return _statKatalog;
    const [a, s] = await Promise.all([
        fetch('/api/statistik-katalog/ausbildung', { headers: ah() }),
        fetch('/api/statistik-katalog/stellung', { headers: ah() }),
    ]);
    _statKatalog = {
        ausbildung: a.ok ? await a.json() : [],
        stellung: s.ok ? await s.json() : [],
    };
    return _statKatalog;
}

function statOpt(list, val, leer) {
    const rows = (list || []).filter(x => x.aktiv !== false || x.id === val);
    return `<option value="">${_lseEsc(leer)}</option>` + rows.map(x =>
        `<option value="${x.id}" ${Number(val) === x.id ? 'selected' : ''}>${_lseEsc(x.bezeichnung)}</option>`).join('');
}

async function statistikKatalogInit() {
    const el = document.getElementById('statistikKatalogBody');
    if (!el) return;
    el.innerHTML = '<div style="padding:16px;color:#8b8b8b">Lade…</div>';
    const k = await statKatalogLaden(true);
    const admin = typeof currentUser !== 'undefined' && currentUser?.role === 'admin';
    const feld = (id, val, breit) => admin
        ? `<input id="${id}" value="${_lseEsc(val ?? '')}" style="width:${breit};padding:6px 8px;border:1px solid rgba(60,55,48,0.16);border-radius:8px;background:#fff">`
        : _lseEsc(val ?? '');
    const zeileA = t => `
        <tr style="border-top:1px solid rgba(60,55,48,0.1);${t.aktiv ? '' : 'opacity:0.55'}">
            <td style="padding:8px 6px">${feld('skaBez' + t.id, t.bezeichnung, '100%')}</td>
            <td style="padding:8px 6px">${feld('skaBesch' + t.id, t.beschreibung, '100%')}</td>
            <td style="padding:8px 6px;font-family:monospace;font-size:12px">${feld('skaCode' + t.id, t.code, '180px')}</td>
            <td style="padding:8px 6px">${feld('skaLse' + t.id, t.lseAusbildung ?? '', '52px')}</td>
            <td style="padding:8px 6px">${feld('skaTitel' + t.id, t.lseTitel ?? '', '42px')}</td>
            <td style="padding:8px 6px">${feld('skaSort' + t.id, t.sortierung, '52px')}</td>
            <td style="padding:8px 6px;text-align:center"><input id="skaAktiv${t.id}" type="checkbox" ${t.aktiv ? 'checked' : ''} ${admin ? '' : 'disabled'}></td>
            <td style="padding:8px 6px">${admin ? `<button type="button" onclick="statistikKatalogSave('ausbildung', ${t.id})" style="${_lseBtnDark};padding:5px 12px">Speichern</button>` : ''}</td>
        </tr>`;
    const zeileS = t => `
        <tr style="border-top:1px solid rgba(60,55,48,0.1);${t.aktiv ? '' : 'opacity:0.55'}">
            <td style="padding:8px 6px">${feld('sksBez' + t.id, t.bezeichnung, '100%')}</td>
            <td style="padding:8px 6px">${feld('sksBesch' + t.id, t.beschreibung, '100%')}</td>
            <td style="padding:8px 6px;font-family:monospace;font-size:12px">${feld('sksCode' + t.id, t.code, '160px')}</td>
            <td style="padding:8px 6px">${feld('sksLse' + t.id, t.lseCode ?? '', '52px')}</td>
            <td style="padding:8px 6px">${feld('sksSort' + t.id, t.sortierung, '52px')}</td>
            <td style="padding:8px 6px;text-align:center"><input id="sksAktiv${t.id}" type="checkbox" ${t.aktiv ? 'checked' : ''} ${admin ? '' : 'disabled'}></td>
            <td style="padding:8px 6px">${admin ? `<button type="button" onclick="statistikKatalogSave('stellung', ${t.id})" style="${_lseBtnDark};padding:5px 12px">Speichern</button>` : ''}</td>
        </tr>`;
    const th = 'text-align:left;padding:8px 6px;color:#8b8b8b;font-size:11px;text-transform:uppercase';
    el.innerHTML = `
        <div class="card" style="padding:14px 16px;margin-bottom:16px">
            <div style="font-weight:700;margin-bottom:8px">Höchste abgeschlossene Ausbildung</div>
            <div style="font-size:12.5px;color:#646464;margin-bottom:10px">Diese Liste erscheint am Mitarbeiter. Der XML-Wert geht in die Meldung und muss zum Swissdec-Schema passen.</div>
            <table style="width:100%;border-collapse:collapse;font-size:13px">
                <thead><tr>
                    <th style="${th}">Bezeichnung</th><th style="${th}">Hilfetext</th><th style="${th}">XML-Wert</th>
                    <th style="${th}">LSE</th><th style="${th}">Titel</th><th style="${th}">Reihe</th><th style="${th}">Aktiv</th><th></th>
                </tr></thead>
                <tbody>${(k.ausbildung || []).map(zeileA).join('')}</tbody>
            </table>
        </div>
        <div class="card" style="padding:14px 16px">
            <div style="font-weight:700;margin-bottom:8px">Berufliche Stellung</div>
            <table style="width:100%;border-collapse:collapse;font-size:13px">
                <thead><tr>
                    <th style="${th}">Bezeichnung</th><th style="${th}">Hilfetext</th><th style="${th}">XML-Wert</th>
                    <th style="${th}">LSE</th><th style="${th}">Reihe</th><th style="${th}">Aktiv</th><th></th>
                </tr></thead>
                <tbody>${(k.stellung || []).map(zeileS).join('')}</tbody>
            </table>
        </div>`;
}

function _statZahl(id) {
    const v = document.getElementById(id)?.value;
    if (v == null || String(v).trim() === '') return null;
    const n = parseInt(v, 10);
    return Number.isFinite(n) ? n : null;
}

async function statistikKatalogSave(art, id) {
    const a = art === 'ausbildung';
    const dto = a ? {
        code: document.getElementById('skaCode' + id)?.value,
        bezeichnung: document.getElementById('skaBez' + id)?.value,
        beschreibung: document.getElementById('skaBesch' + id)?.value,
        sortierung: _statZahl('skaSort' + id) || 0,
        lseAusbildung: _statZahl('skaLse' + id),
        lseTitel: _statZahl('skaTitel' + id),
        aktiv: !!document.getElementById('skaAktiv' + id)?.checked,
    } : {
        code: document.getElementById('sksCode' + id)?.value,
        bezeichnung: document.getElementById('sksBez' + id)?.value,
        beschreibung: document.getElementById('sksBesch' + id)?.value,
        sortierung: _statZahl('sksSort' + id) || 0,
        lseCode: _statZahl('sksLse' + id),
        aktiv: !!document.getElementById('sksAktiv' + id)?.checked,
    };
    const r = await fetch(`/api/statistik-katalog/${art}/${id}`, { method: 'PUT', headers: ah(), body: JSON.stringify(dto) });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen', 'error'); return; }
    showToast('Gespeichert', 'success');
    statistikKatalogInit();
}

// Fester Bereich unten im Tab «Training, Verfügbar, Statistik».
// Die Lohnrechnung liest diese Felder nicht.
let _statMaLauf = 0;
async function loadStatistikMa(employeeId) {
    const box = document.getElementById('statistikMaContent');
    if (!box) return;
    const lauf = ++_statMaLauf;
    const darf = ['admin', 'superuser', 'buchhaltung'].includes(currentUser?.role)
        && !selectedEmployee?.isPayrollExcluded;
    if (!employeeId || !darf) { box.innerHTML = ''; return; }
    box.innerHTML = '<div class="card" style="padding:14px 16px;color:#8b8b8b;font-size:13px">Statistik wird geladen…</div>';
    let lse = {};
    let k = { ausbildung: [], stellung: [] };
    try {
        const [rk, rl] = await Promise.all([
            statKatalogLaden(true),
            fetch(`/api/lse/employee/${employeeId}`, { headers: ah() }),
        ]);
        k = rk || k;
        if (!rl.ok) {
            if (lauf === _statMaLauf) box.innerHTML = '<div class="card" style="padding:14px 16px;color:#b91c1c">Statistik konnte nicht geladen werden.</div>';
            return;
        }
        lse = await rl.json();
    } catch {
        if (lauf === _statMaLauf) box.innerHTML = '<div class="card" style="padding:14px 16px;color:#b91c1c">Statistik konnte nicht geladen werden.</div>';
        return;
    }
    if (lauf !== _statMaLauf) return;
    const stellungLeer = lse.stellungVorschlagText
        ? `aus der Funktion (${lse.stellungVorschlagText})`
        : 'aus der Funktion';
    const feld = (cls, label, inhalt) =>
        `<label class="lse-stat-feld ${cls}"><span>${label}</span>${inhalt}</label>`;
    box.innerHTML = `
        <div class="card" style="padding:14px 16px">
            <div style="display:flex;align-items:baseline;justify-content:space-between;gap:12px;flex-wrap:wrap;margin-bottom:12px">
                <div>
                    <div style="font-size:16px;font-weight:800;color:#3f3f3f">Statistik</div>
                    <div style="font-size:12.5px;color:#8b8b8b">Angaben für das BFS. Die Lohnrechnung benutzt sie nicht.</div>
                </div>
                <button type="button" class="ov-hbtn ov-hbtn-primary" onclick="lseEmpBlockSave(${employeeId})">Speichern</button>
            </div>
            <div class="lse-stat-felder">
                ${feld('lse-stat-edu', 'Höchste abgeschlossene Ausbildung',
                    `<select id="lseEmpEdu" class="ov-softin">${statOpt(k.ausbildung, lse.statistikAusbildungId, '–')}</select>`)}
                ${feld('lse-stat-pos', 'Berufliche Stellung',
                    `<select id="lseEmpPos" class="ov-softin">${statOpt(k.stellung, lse.statistikStellungId, stellungLeer)}</select>`)}
                ${feld('lse-stat-beruf', 'Ausgeübter Beruf',
                    `<input id="lseEmpBeruf" class="ov-softin" maxlength="255" value="${_lseEsc(lse.practicedProfession || '')}" placeholder="Restaurantmitarbeiter/in">`)}
                ${feld('lse-stat-ferien', 'Ferientage pro Jahr',
                    `<input id="lseEmpFerien" class="ov-softin" type="number" min="0" max="99" step="1" value="${lse.leaveEntitlementDays ?? ''}" placeholder="${lse.ferienTageRegel ?? ''}">`)}
                ${feld('lse-stat-flex', 'Profiling',
                    `<input id="lseEmpFlex" class="ov-softin" maxlength="255" value="${_lseEsc(lse.flexProfiling || '')}" placeholder="nur mit BFS">`)}
            </div>
            <div id="lseEmpEduHilfe" style="font-size:12px;color:#646464;margin-top:8px"></div>
            <div id="lseEmpHinweis" style="font-size:12px;color:#646464;margin-top:4px">${_lseEsc(lse.ferienHinweis || '')}</div>
            <div style="font-size:11.5px;color:#8b8b8b;margin-top:4px">Leer bei den Ferientagen lässt die Regel gelten. Profiling nur ausfüllen, wenn das BFS es so vereinbart hat.</div>
            <div style="display:flex;flex-wrap:wrap;gap:16px;margin-top:12px;font-size:13px;color:#3f3f3f">
                <label style="display:flex;gap:8px;align-items:center"><input id="lseEmpVerliehen" type="checkbox" ${lse.verliehenesPersonal ? 'checked' : ''}> Verliehenes Personal</label>
                <label style="display:flex;gap:8px;align-items:center"><input id="lseEmpStamm" type="checkbox" ${lse.stammpersonalOeffentlich ? 'checked' : ''}> Stammpersonal in öffentlichen Verwaltungen</label>
            </div>
            <div id="lseEmpAzm" style="font-size:12px;color:#646464;margin-top:14px"></div>
        </div>`;
    const hilfe = () => {
        const id = Number(document.getElementById('lseEmpEdu')?.value);
        const zeile = (k.ausbildung || []).find(x => x.id === id);
        const ziel = document.getElementById('lseEmpEduHilfe');
        if (ziel) ziel.textContent = zeile?.beschreibung || '';
    };
    document.getElementById('lseEmpEdu')?.addEventListener('change', hilfe);
    hilfe();
    lseEmpArbeitszeitmodell(employeeId);
}

// Arbeitszeitmodell der Person (Walter 27.09.2026) — reine Meldeangabe fuer die
// Swissdec-Statistik; die Lohnrechnung benutzt sie NICHT. Darum hier bei den
// Statistik-Angaben und nicht im Vertrag.
async function lseEmpArbeitszeitmodell(employeeId) {
    const el = document.getElementById('lseEmpAzm');
    if (!el) return;
    try {
        const [rz, rm] = await Promise.all([
            fetch(`/api/arbeitszeitmodelle/mitarbeiter/${employeeId}`, { headers: ah() }),
            fetch('/api/arbeitszeitmodelle', { headers: ah() }),
        ]);
        if (!rz.ok || !rm.ok) { el.style.display = 'none'; return; }
        const zuord = await rz.json();
        const modelle = (await rm.json()).filter(m => m.isActive);
        if (modelle.length === 0) { el.style.display = 'none'; return; }
        const heute = new Date().toISOString().slice(0, 10);
        const aktuell = zuord.find(z => z.gueltigAb <= heute) || zuord[0];
        const dat = (iso) => iso ? iso.slice(8, 10) + '.' + iso.slice(5, 7) + '.' + iso.slice(0, 4) : '';
        el.innerHTML = `
            <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap">
                <span><b>Arbeitszeitmodell (Swissdec):</b> ${aktuell ? _lseEsc(aktuell.bezeichnung || '–') + ' seit ' + dat(aktuell.gueltigAb) : 'keines zugeordnet'}</span>
                <select id="lseEmpAzmSel" style="padding:4px 8px;border:1px solid #cbd5e1;border-radius:8px">
                    ${modelle.map(m => `<option value="${m.id}" ${aktuell && aktuell.arbeitszeitmodellId === m.id ? 'selected' : ''}>${_lseEsc(m.bezeichnung)}</option>`).join('')}
                </select>
                <input id="lseEmpAzmAb" type="date" value="${heute}" style="padding:4px 8px;border:1px solid #cbd5e1;border-radius:8px">
                <button onclick="lseEmpAzmSave(${employeeId})" style="${_lseBtnDark};padding:5px 12px">Zuordnen</button>
            </div>
            ${zuord.length > 1 ? `<div style="margin-top:5px">Verlauf: ${zuord.map(z => `${dat(z.gueltigAb)} ${_lseEsc(z.bezeichnung || '–')}`).join(' · ')}</div>` : ''}`;
        el.style.display = '';
    } catch { el.style.display = 'none'; }
}

async function lseEmpAzmSave(employeeId) {
    const id = parseInt(document.getElementById('lseEmpAzmSel')?.value, 10);
    const ab = document.getElementById('lseEmpAzmAb')?.value;
    if (!id || !ab) return;
    const r = await fetch(`/api/arbeitszeitmodelle/mitarbeiter/${employeeId}`, {
        method: 'POST', headers: ah(),
        body: JSON.stringify({ arbeitszeitmodellId: id, gueltigAb: ab })
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen', 'error'); return; }
    showToast('Arbeitszeitmodell zugeordnet', 'success');
    lseEmpArbeitszeitmodell(employeeId);
}

async function lseEmpBlockSave(employeeId) {
    const ferienRoh = document.getElementById('lseEmpFerien')?.value;
    const dto = {
        vollstaendig: true,
        statistikAusbildungId: parseInt(document.getElementById('lseEmpEdu')?.value, 10) || null,
        statistikStellungId: parseInt(document.getElementById('lseEmpPos')?.value, 10) || null,
        practicedProfession: document.getElementById('lseEmpBeruf')?.value || null,
        leaveEntitlementDays: ferienRoh === '' || ferienRoh == null ? null : Number(ferienRoh),
        verliehenesPersonal: !!document.getElementById('lseEmpVerliehen')?.checked,
        stammpersonalOeffentlich: !!document.getElementById('lseEmpStamm')?.checked,
        flexProfiling: document.getElementById('lseEmpFlex')?.value || null,
    };
    const r = await fetch(`/api/lse/employee/${employeeId}`, { method: 'PUT', headers: ah(), body: JSON.stringify(dto) });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen', 'error'); return; }
    showToast('Gespeichert', 'success');
}
