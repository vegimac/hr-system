// Lohnkonto aus Mirus (Walter 04.10.2026) — Mirus-Lohnkonto (Word) → vorsystem_lohnkonto.
// Backend: /api/mirus-lohnkonto-import/{analyze,commit,status}. Prefix mlk.

let _mlkResult = null;
let _mlkPicks  = {};

const _MLK_MONATE = ['Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez'];

function _mlkCp() {
    return (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
}
function _mlkEsc(s) {
    return s == null ? '' : String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function _mlkChf(v) {
    return Number(v || 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
function _mlkAuth() {
    return { 'Authorization': 'Bearer ' + (localStorage.getItem('hrToken') || '') };
}

function mlkInit() {
    const cpId = _mlkCp();
    const branch = (typeof allBranches !== 'undefined' ? allBranches : []).find(b => b.id === cpId);
    const banner = document.getElementById('mlkBranchBanner');
    if (banner) {
        banner.innerHTML = branch
            ? `📍 <b>Filiale: ${_mlkEsc(branch.branchName || branch.companyName || '')}</b> — wird aus dem Hauptmenü übernommen`
            : `⚠️ Bitte zuerst eine Filiale im Hauptmenü wählen.`;
    }
    _mlkResult = null;
    _mlkPicks = {};
    document.getElementById('mlkSummary').innerHTML = '';
    document.getElementById('mlkPreview').innerHTML = '';
    document.getElementById('mlkAlert').innerHTML = '';
    const btn = document.getElementById('mlkCommitBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Import bestätigen'; }
    mlkLoadStatus();
}

function mlkShowAlert(msg, kind) {
    const el = document.getElementById('mlkAlert');
    if (!el) return;
    const bg = kind === 'ok' ? '#dcfce7' : kind === 'warn' ? '#fef3c7' : '#fef2f2';
    const bd = kind === 'ok' ? '#bbf7d0' : kind === 'warn' ? '#fde68a' : '#fecaca';
    const fg = kind === 'ok' ? '#15803d' : kind === 'warn' ? '#854d0e' : '#dc2626';
    el.innerHTML = `<div style="padding:10px 14px;background:${bg};border:1px solid ${bd};color:${fg};border-radius:8px;font-size:13px">${msg}</div>`;
}

async function _mlkFehlertext(r) {
    const txt = await r.text().catch(() => '');
    try { const j = JSON.parse(txt); return j.message || j.error || `HTTP ${r.status}`; } catch (_) { return txt ? txt.slice(0, 240) : `HTTP ${r.status}`; }
}

async function mlkLoadStatus() {
    const el = document.getElementById('mlkStatus');
    const cpId = _mlkCp();
    if (!el) return;
    if (!cpId) { el.innerHTML = ''; return; }
    try {
        const r = await fetch(`/api/mirus-lohnkonto-import/status?companyProfileId=${cpId}`, { headers: _mlkAuth() });
        if (!r.ok) { el.innerHTML = ''; return; }
        const rows = await r.json();
        if (!rows.length) {
            el.innerHTML = `<div style="padding:10px 14px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;font-size:13px;color:#64748b">Für diese Filiale ist noch kein Mirus-Lohnkonto übernommen.</div>`;
            return;
        }
        const jahre = [...new Set(rows.map(x => x.jahr))];
        el.innerHTML = jahre.map(j => {
            const monate = rows.filter(x => x.jahr === j);
            const zuletzt = monate.map(x => x.importiertAm).sort().pop();
            const chips = monate.map(m => `<span style="display:inline-block;margin:2px 4px 2px 0;padding:3px 8px;border-radius:8px;background:#ecfccb;color:#3f6212;font-size:11.5px" title="${m.personen} Personen · Brutto ${_mlkChf(m.brutto)}">${_MLK_MONATE[m.monat - 1]} · ${m.personen}</span>`).join('');
            return `<div class="card" style="padding:12px 16px;margin-bottom:8px;display:flex;gap:14px;align-items:center;flex-wrap:wrap">
                <div style="font-size:13px"><b>Bereits übernommen ${j}</b>
                    <span style="color:#94a3b8">· zuletzt ${zuletzt ? new Date(zuletzt).toLocaleDateString('de-CH') : ''}</span></div>
                <div style="flex:1">${chips}</div>
                <button class="btn btn-secondary" style="font-size:12px" onclick="mlkDelete(${j})">Jahr ${j} entfernen</button>
            </div>`;
        }).join('');
    } catch (_) { el.innerHTML = ''; }
}

async function mlkAnalyze() {
    const cpId = _mlkCp();
    if (!cpId) { mlkShowAlert('Bitte zuerst eine Filiale im Hauptmenü wählen.', 'err'); return; }
    const input = document.getElementById('mlkFileInput');
    if (!input?.files?.length) { mlkShowAlert('Bitte eine Datei wählen.', 'err'); return; }
    const fd = new FormData();
    fd.append('file', input.files[0]);
    mlkShowAlert('⏳ Datei wird gelesen…', 'warn');
    document.getElementById('mlkCommitBtn').disabled = true;
    try {
        const r = await fetch(`/api/mirus-lohnkonto-import/analyze?companyProfileId=${cpId}`, { method: 'POST', headers: _mlkAuth(), body: fd });
        if (!r.ok) { mlkShowAlert('Fehler beim Lesen: ' + _mlkEsc(await _mlkFehlertext(r)), 'err'); return; }
        _mlkResult = await r.json();
        _mlkPicks = {};
        const ohne = _mlkResult.personen.filter(p => !p.employeeId).length;
        const kontrolleOk = _mlkResult.monate.every(m => m.ok);
        if (_mlkResult.filialWarnung) mlkShowAlert('⚠️ ' + _mlkEsc(_mlkResult.filialWarnung), 'err');
        else if (!kontrolleOk) mlkShowAlert('⚠️ Die Summe der Personen ergibt nicht das Filial-Total von Mirus — Datei bitte prüfen.', 'err');
        else mlkShowAlert(`Gelesen: ${_mlkResult.personen.length} Arbeitsverhältnisse, Kontrolle gegen das Filial-Total stimmt.${ohne ? ' ' + ohne + ' ohne Zuordnung.' : ''}`, ohne ? 'warn' : 'ok');
        mlkRender();
        mlkUpdateCommit();
    } catch (err) {
        mlkShowAlert('Verbindungsfehler: ' + _mlkEsc(err.message), 'err');
    }
}

function mlkRender() {
    const d = _mlkResult;
    if (!d) return;
    const monateHtml = d.monate.map(m => {
        const farbe = m.ok ? '#3f6212' : '#dc2626';
        const leer = m.bruttoPersonen === 0 && m.bruttoTotal === 0;
        return `<td style="padding:6px 8px;text-align:right;font-size:12px;color:${leer ? '#cbd5e1' : farbe}" title="Netto Personen ${_mlkChf(m.nettoPersonen)} / Total ${_mlkChf(m.nettoTotal)}${m.bereitsImportiert ? ' · bereits ' + m.bereitsImportiert + ' Personen übernommen (wird ersetzt)' : ''}">
            ${leer ? '—' : _mlkChf(m.bruttoPersonen)}${m.ok || leer ? '' : '<br><small>Total ' + _mlkChf(m.bruttoTotal) + '</small>'}${m.bereitsImportiert ? ' ↻' : ''}</td>`;
    }).join('');
    document.getElementById('mlkSummary').innerHTML = `
        <div class="card" style="padding:0;overflow-x:auto">
            <table style="width:100%;border-collapse:collapse;min-width:900px">
                <thead><tr style="background:#f8fafc">
                    <th style="padding:8px;text-align:left;font-size:11px;color:#64748b">BRUTTO PRO MONAT</th>
                    ${d.monate.map(m => `<th style="padding:8px;text-align:right;font-size:11px;color:#64748b">${_MLK_MONATE[m.monat - 1]} ${m.jahr}</th>`).join('')}
                </tr></thead>
                <tbody><tr><td style="padding:6px 8px;font-size:12px;color:#475569">Summe Personen ${d.totalGefunden ? '= Filial-Total' : '(keine Total-Seite)'}</td>${monateHtml}</tr></tbody>
            </table>
        </div>`;

    const optionen = ['<option value="">— nicht übernehmen —</option>']
        .concat((d.kandidaten || []).map(k => `<option value="${k.id}">${_mlkEsc(k.firstName)} ${_mlkEsc(k.lastName)}${k.employeeNumber ? ' · ' + _mlkEsc(k.employeeNumber) : ''}</option>`))
        .join('');
    const badge = st => ({
        PNR:      ['#dcfce7', '#166534', '✓ Personalnummer'],
        AHV:      ['#dcfce7', '#166534', '✓ AHV-Nummer'],
        NAME_GEB: ['#dcfce7', '#166534', '✓ Name + Geburtsdatum'],
        NAME:     ['#fef3c7', '#854d0e', '? nur Name'],
    }[st] || ['#fef2f2', '#dc2626', '✗ nicht gefunden']);

    const zeilen = d.personen.map(p => {
        const [bg, fg, txt] = badge(p.status);
        const ziel = p.employeeId
            ? `<b>${_mlkEsc(p.employeeName)}</b> <span style="color:#94a3b8">${_mlkEsc(p.employeeNumber || '')}</span>`
            : `<select onchange="mlkSetPick(${JSON.stringify(p.key).replace(/"/g, '&quot;')}, this.value)" style="font-size:12px;padding:4px 6px;border:1px solid #cbd5e1;border-radius:6px;width:100%">${optionen}</select>`;
        return `<tr style="border-top:1px solid #f1f5f9">
            <td style="padding:6px 8px;font-size:11.5px;color:#94a3b8;font-family:monospace">${_mlkEsc(p.personalnummer)}</td>
            <td style="padding:6px 8px;font-size:12.5px">${_mlkEsc(p.name)}<div style="font-size:11px;color:#94a3b8">Eintritt ${_mlkEsc(p.eintritt || '—')}${p.austritt ? ' · Austritt ' + _mlkEsc(p.austritt) : ''}</div></td>
            <td style="padding:6px 8px;text-align:right;font-size:12px">${p.monateMitLohn}</td>
            <td style="padding:6px 8px;text-align:right;font-size:12px;font-family:monospace">${_mlkChf(p.brutto)}</td>
            <td style="padding:6px 8px;text-align:right;font-size:12px;font-family:monospace">${_mlkChf(p.netto)}</td>
            <td style="padding:6px 8px"><span style="background:${bg};color:${fg};padding:2px 8px;border-radius:6px;font-size:11px;font-weight:600;white-space:nowrap">${txt}</span></td>
            <td style="padding:6px 8px;min-width:240px;font-size:12.5px">${ziel}</td>
        </tr>`;
    }).join('');
    const warn = (d.warnungen || []).length
        ? `<div style="padding:10px 14px;margin-bottom:10px;background:#fef3c7;border:1px solid #fde68a;border-radius:8px;font-size:12.5px;color:#854d0e">${d.warnungen.map(_mlkEsc).join('<br>')}</div>`
        : '';
    document.getElementById('mlkPreview').innerHTML = `${warn}
        <div class="card" style="padding:0;overflow-x:auto">
            <table style="width:100%;border-collapse:collapse;min-width:900px">
                <thead><tr style="background:#f8fafc">
                    <th style="padding:8px;text-align:left;font-size:11px;color:#64748b">PNR</th>
                    <th style="padding:8px;text-align:left;font-size:11px;color:#64748b">NAME (MIRUS)</th>
                    <th style="padding:8px;text-align:right;font-size:11px;color:#64748b">MONATE</th>
                    <th style="padding:8px;text-align:right;font-size:11px;color:#64748b">BRUTTO</th>
                    <th style="padding:8px;text-align:right;font-size:11px;color:#64748b">NETTO</th>
                    <th style="padding:8px;text-align:left;font-size:11px;color:#64748b">GEFUNDEN ÜBER</th>
                    <th style="padding:8px;text-align:left;font-size:11px;color:#64748b">MITARBEITER IN ONECREW</th>
                </tr></thead>
                <tbody>${zeilen}</tbody>
            </table>
        </div>`;
}

function mlkSetPick(key, val) {
    const id = parseInt(val, 10);
    if (Number.isFinite(id) && id > 0) _mlkPicks[key] = id; else delete _mlkPicks[key];
    mlkUpdateCommit();
}

function _mlkAnzahlUebernommen() {
    if (!_mlkResult) return 0;
    return _mlkResult.personen.filter(p => p.employeeId || _mlkPicks[p.key]).length;
}

function mlkUpdateCommit() {
    const btn = document.getElementById('mlkCommitBtn');
    if (!btn) return;
    const d = _mlkResult;
    const n = _mlkAnzahlUebernommen();
    btn.disabled = !d || n === 0 || !!d.filialWarnung || !d.monate.every(m => m.ok);
    btn.textContent = n ? `Import bestätigen (${n})` : 'Import bestätigen';
}

async function mlkCommit() {
    const d = _mlkResult;
    const input = document.getElementById('mlkFileInput');
    if (!d || !input?.files?.length) return;
    const offen = d.personen.length - _mlkAnzahlUebernommen();
    const monate = d.monate.filter(m => m.bruttoPersonen !== 0).map(m => `${_MLK_MONATE[m.monat - 1]} ${m.jahr}`);
    const text = `Lohnkonto für ${_mlkAnzahlUebernommen()} Arbeitsverhältnisse übernehmen (${monate[0] || ''}–${monate[monate.length - 1] || ''})?`
        + (offen ? `\n\n${offen} ohne Zuordnung werden nicht übernommen.` : '')
        + `\n\nBereits übernommene Werte dieser Monate werden ersetzt.`;
    const ok = typeof liquidConfirm === 'function'
        ? await liquidConfirm(text, { title: 'Lohnkonto übernehmen', yesLabel: 'Übernehmen', noLabel: 'Abbrechen' })
        : confirm(text);
    if (!ok) return;

    const fd = new FormData();
    fd.append('file', input.files[0]);
    fd.append('zuordnung', JSON.stringify(_mlkPicks));
    mlkShowAlert('⏳ Wird übernommen…', 'warn');
    document.getElementById('mlkCommitBtn').disabled = true;
    try {
        const r = await fetch(`/api/mirus-lohnkonto-import/commit?companyProfileId=${d.companyProfileId}`, { method: 'POST', headers: _mlkAuth(), body: fd });
        if (!r.ok) { mlkShowAlert('Fehler beim Übernehmen: ' + _mlkEsc(await _mlkFehlertext(r)), 'err'); mlkUpdateCommit(); return; }
        const res = await r.json();
        mlkShowAlert(`Übernommen: ${res.personen} Mitarbeitende, ${res.zeilen.toLocaleString('de-CH')} Werte.`
            + (res.uebersprungen.length ? ` Nicht übernommen: ${res.uebersprungen.map(_mlkEsc).join(', ')}.` : ''), 'ok');
        _mlkResult = null;
        document.getElementById('mlkSummary').innerHTML = '';
        document.getElementById('mlkPreview').innerHTML = '';
        input.value = '';
        mlkLoadStatus();
    } catch (err) {
        mlkShowAlert('Verbindungsfehler: ' + _mlkEsc(err.message), 'err');
        mlkUpdateCommit();
    }
}

async function mlkDelete(jahr) {
    const cpId = _mlkCp();
    if (!cpId) return;
    const text = `Alle übernommenen Mirus-Werte ${jahr} dieser Filiale entfernen? OneCrew-Löhne bleiben unverändert.`;
    const ok = typeof liquidConfirm === 'function'
        ? await liquidConfirm(text, { title: 'Lohnkonto entfernen', yesLabel: 'Entfernen', noLabel: 'Abbrechen' })
        : confirm(text);
    if (!ok) return;
    const r = await fetch(`/api/mirus-lohnkonto-import?companyProfileId=${cpId}&jahr=${jahr}`, { method: 'DELETE', headers: _mlkAuth() });
    if (!r.ok) { mlkShowAlert('Fehler: ' + _mlkEsc(await _mlkFehlertext(r)), 'err'); return; }
    const res = await r.json();
    mlkShowAlert(`Entfernt: ${res.geloescht.toLocaleString('de-CH')} Werte.`, 'ok');
    mlkLoadStatus();
}
