// Lohn-Simulation (Walter 04.10.2026) — OneCrew rechnet die Monate vor dem ersten
// Lohnlauf nach und vergleicht mit dem Mirus-Lohnkonto. Schreibt nur simulation_lohn.
// Backend: /api/lohn-simulation/{status,rechnen,vergleich,detail}. Prefix lsim.

const _LSIM_MONATE = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
let _lsimVergleich = null;
let _lsimAlleZeigen = false;
let _lsimLaeuft = false;

function _lsimCp() {
    return (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
}
function _lsimEsc(s) {
    return s == null ? '' : String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function _lsimChf(v) {
    return Number(v || 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
function _lsimAuth() {
    return { 'Authorization': 'Bearer ' + (localStorage.getItem('hrToken') || '') };
}
function _lsimJahr() {
    return parseInt(document.getElementById('lsimJahr')?.value, 10) || new Date().getFullYear();
}
function _lsimDiff(a, b) {
    return Math.round((Number(a || 0) - Number(b || 0)) * 100) / 100;
}

function lsimInit() {
    const cpId = _lsimCp();
    const branch = (typeof allBranches !== 'undefined' ? allBranches : []).find(b => b.id === cpId);
    const banner = document.getElementById('lsimBranchBanner');
    if (banner) {
        banner.innerHTML = branch
            ? `📍 <b>Filiale: ${_lsimEsc(branch.branchName || branch.companyName || '')}</b> — wird aus dem Hauptmenü übernommen`
            : `⚠️ Bitte zuerst eine Filiale im Hauptmenü wählen.`;
    }
    const jahrInp = document.getElementById('lsimJahr');
    if (jahrInp && !jahrInp.value) jahrInp.value = window._lsimJahr || new Date().getFullYear();
    const bis = document.getElementById('lsimBis');
    if (bis && !bis.options.length) {
        bis.innerHTML = _LSIM_MONATE.map((m, i) => `<option value="${i + 1}">${m}</option>`).join('');
        bis.value = '8';
    }
    document.getElementById('lsimAlert').innerHTML = '';
    document.getElementById('lsimFortschritt').innerHTML = '';
    lsimLoadStatus();
}

function lsimShowAlert(msg, kind) {
    const el = document.getElementById('lsimAlert');
    if (!el) return;
    const bg = kind === 'ok' ? '#dcfce7' : kind === 'warn' ? '#fef3c7' : '#fef2f2';
    const bd = kind === 'ok' ? '#bbf7d0' : kind === 'warn' ? '#fde68a' : '#fecaca';
    const fg = kind === 'ok' ? '#15803d' : kind === 'warn' ? '#854d0e' : '#dc2626';
    el.innerHTML = msg ? `<div style="padding:10px 14px;background:${bg};border:1px solid ${bd};color:${fg};border-radius:8px;font-size:13px">${msg}</div>` : '';
}

async function _lsimFehlertext(r) {
    const txt = await r.text().catch(() => '');
    try { const j = JSON.parse(txt); return j.message || j.error || `HTTP ${r.status}`; } catch (_) { return txt ? txt.slice(0, 240) : `HTTP ${r.status}`; }
}

/** Öffnet einen Saldo-Vortrag-Import mit gesetztem Häkchen «für die Simulation». */
function lsimVortragImport(seite) {
    window._lsimVortragOeffnen = true;
    window._lsimJahr = _lsimJahr();
    showPage(seite);
}

async function lsimLoadStatus() {
    const el = document.getElementById('lsimSchritte');
    const cpId = _lsimCp();
    if (!el || !cpId) { if (el) el.innerHTML = ''; return; }
    const jahr = _lsimJahr();
    let st = { vortragMa: 0, lohnkontoMonate: [], simMonate: [] };
    try {
        const r = await fetch(`/api/lohn-simulation/status?companyProfileId=${cpId}&jahr=${jahr}`, { headers: _lsimAuth() });
        if (r.ok) st = await r.json();
    } catch (_) { /* Anzeige leer lassen */ }

    const ok = '<span style="color:#15803d;font-weight:700">✓</span>';
    const offen = '<span style="color:#b45309;font-weight:700">○</span>';
    const lkChips = st.lohnkontoMonate.map(m => `<span style="display:inline-block;margin:2px 4px 2px 0;padding:2px 8px;border-radius:8px;background:#ecfccb;color:#3f6212;font-size:11.5px">${_LSIM_MONATE[m.monat - 1].slice(0, 3)}</span>`).join('');
    const simChips = st.simMonate.map(m => {
        const rot = m.mitFehler > 0;
        return `<span title="${m.personen} MA${rot ? ' · ' + m.mitFehler + ' nicht rechenbar' : ''}" style="display:inline-block;margin:2px 4px 2px 0;padding:2px 8px;border-radius:8px;background:${rot ? '#fef3c7' : '#e0e7ff'};color:${rot ? '#854d0e' : '#3730a3'};font-size:11.5px">${_LSIM_MONATE[m.monat - 1].slice(0, 3)} · ${m.personen}${rot ? ' ⚠' + m.mitFehler : ''}</span>`;
    }).join('');

    el.innerHTML = `
        <div style="display:grid;gap:10px;font-size:13px;color:#3f3f3f">
            <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
                ${st.vortragMa > 0 ? ok : offen}
                <b>1. Saldi per 31.12.${jahr - 1}</b>
                <span style="color:#64748b">${st.vortragMa > 0 ? st.vortragMa + ' MA mit Saldi' : 'noch keine'}</span>
                <span style="flex:1"></span>
                <button class="btn btn-secondary" style="font-size:12px" onclick="lsimVortragImport('saldo-vortrag-import-stunden')">Stunden/Tage (Monatsblatt Dezember)</button>
                <button class="btn btn-secondary" style="font-size:12px" onclick="lsimVortragImport('saldo-vortrag-import')">CHF (Rückstellungsliste Dezember)</button>
            </div>
            <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
                ${st.lohnkontoMonate.length ? ok : offen}
                <b>2. Lohnkonto ${jahr} aus Mirus</b>
                <span>${lkChips || '<span style="color:#64748b">noch nicht übernommen</span>'}</span>
                <span style="flex:1"></span>
                <button class="btn btn-secondary" style="font-size:12px" onclick="showPage('mirus-lohnkonto-import')">Lohnkonto übernehmen</button>
            </div>
            <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
                ${st.simMonate.length ? ok : offen}
                <b>3. Simulation ${jahr}</b>
                <span>${simChips || '<span style="color:#64748b">noch nicht gerechnet</span>'}</span>
            </div>
        </div>`;

    if (st.simMonate.length) lsimLoadVergleich();
    else document.getElementById('lsimVergleich').innerHTML = '';
}

async function lsimRechnen() {
    if (_lsimLaeuft) return;
    const cpId = _lsimCp();
    if (!cpId) { lsimShowAlert('Bitte zuerst eine Filiale im Hauptmenü wählen.', 'err'); return; }
    const jahr = _lsimJahr();
    const bis = parseInt(document.getElementById('lsimBis').value, 10) || 8;
    const ja = await liquidConfirm(
        `OneCrew rechnet Januar bis ${_LSIM_MONATE[bis - 1]} ${jahr} für diese Filiale neu. Eine frühere Simulation dieser Monate wird ersetzt. Es entstehen keine Lohnläufe, keine Lohnzettel und kein Versand.`,
        { title: 'Simulation rechnen', yesLabel: 'Rechnen', noLabel: 'Abbrechen' });
    if (!ja) return;

    _lsimLaeuft = true;
    const btn = document.getElementById('lsimRechnenBtn');
    if (btn) btn.disabled = true;
    lsimShowAlert('');
    const prog = document.getElementById('lsimFortschritt');
    const zeilen = [];
    const zeichne = (aktuell) => {
        prog.innerHTML = `<div class="card" style="padding:14px 18px;font-size:13px">
            ${zeilen.join('')}
            ${aktuell ? `<div style="color:#64748b;margin-top:6px">⏳ ${aktuell} wird gerechnet…</div>` : ''}
        </div>`;
    };
    try {
        for (let monat = 1; monat <= bis; monat++) {
            zeichne(_LSIM_MONATE[monat - 1]);
            const r = await fetch(`/api/lohn-simulation/rechnen?companyProfileId=${cpId}&jahr=${jahr}&monat=${monat}`,
                { method: 'POST', headers: _lsimAuth() });
            if (!r.ok) {
                zeilen.push(`<div style="color:#dc2626">✗ ${_LSIM_MONATE[monat - 1]}: ${_lsimEsc(await _lsimFehlertext(r))}</div>`);
                zeichne(null);
                lsimShowAlert('Simulation abgebrochen — siehe Meldung unten.', 'err');
                return;
            }
            const e = await r.json();
            const fehler = (e.fehler || []).map(f => `<li>${_lsimEsc(f)}</li>`).join('');
            const ohneLp = (e.codesOhneLohnposition || []).length
                ? `<div style="color:#b45309;margin-left:22px">Nicht übernommen, keine OneCrew-Lohnart zu Mirus-Code ${e.codesOhneLohnposition.map(_lsimEsc).join(', ')} — im «Lohnraster Mirus (Referenz)» verknüpfen</div>` : '';
            zeilen.push(`<div style="margin:3px 0">${e.mitFehler ? '⚠' : '✓'} <b>${_LSIM_MONATE[monat - 1]}</b>: ${e.gerechnet} von ${e.mitarbeiter} MA gerechnet${e.mitFehler ? `, <span style="color:#b45309">${e.mitFehler} nicht rechenbar</span>` : ''}
                ${fehler ? `<details style="margin-left:22px"><summary style="cursor:pointer;color:#64748b">Welche?</summary><ul style="margin:4px 0">${fehler}</ul></details>` : ''}
                ${ohneLp}</div>`);
        }
        zeichne(null);
        lsimShowAlert(`Simulation Januar–${_LSIM_MONATE[bis - 1]} ${jahr} gerechnet.`, 'ok');
    } catch (err) {
        lsimShowAlert('Verbindungsfehler: ' + _lsimEsc(err.message), 'err');
    } finally {
        _lsimLaeuft = false;
        if (btn) btn.disabled = false;
        lsimLoadStatus();
    }
}

async function lsimVerwerfen() {
    const cpId = _lsimCp();
    if (!cpId) return;
    const ja = await liquidConfirm('Alle simulierten Monate dieser Filiale löschen? Echte Lohnläufe sind davon nicht betroffen.',
        { title: 'Simulation verwerfen', yesLabel: 'Verwerfen', noLabel: 'Abbrechen' });
    if (!ja) return;
    const mitVortrag = await liquidConfirm('Auch die Saldi per 31.12. für die Simulation löschen? (Nein = Saldi bleiben, du kannst gleich neu rechnen.)',
        { title: 'Saldi per 31.12.', yesLabel: 'Auch Saldi löschen', noLabel: 'Saldi behalten' });
    const r = await fetch(`/api/lohn-simulation?companyProfileId=${cpId}&mitVortrag=${mitVortrag}`, { method: 'DELETE', headers: _lsimAuth() });
    if (!r.ok) { lsimShowAlert('Fehler: ' + _lsimEsc(await _lsimFehlertext(r)), 'err'); return; }
    const e = await r.json();
    lsimShowAlert(`Verworfen: ${e.loehne} simulierte Lohnzettel${mitVortrag ? `, ${e.vortraege} Saldi` : ''}.`, 'ok');
    document.getElementById('lsimFortschritt').innerHTML = '';
    lsimLoadStatus();
}

async function lsimLoadVergleich() {
    const cpId = _lsimCp();
    const el = document.getElementById('lsimVergleich');
    if (!cpId || !el) return;
    const r = await fetch(`/api/lohn-simulation/vergleich?companyProfileId=${cpId}&jahr=${_lsimJahr()}`, { headers: _lsimAuth() });
    if (!r.ok) { el.innerHTML = ''; return; }
    _lsimVergleich = await r.json();
    lsimRenderVergleich();
}

function _lsimHatDiff(m) {
    return !m.simuliert || !!m.fehler
        || _lsimDiff(m.brutto, m.bruttoMirus) !== 0
        || _lsimDiff(m.netto, m.nettoMirus) !== 0
        || _lsimDiff(m.auszahlung, m.auszahlungMirus) !== 0;
}

function lsimToggleAlle() {
    _lsimAlleZeigen = !_lsimAlleZeigen;
    lsimRenderVergleich();
}

function lsimRenderVergleich() {
    const el = document.getElementById('lsimVergleich');
    const v = _lsimVergleich;
    if (!el || !v) return;

    // Übersicht pro Monat
    const proMonat = v.monate.map(monat => {
        let ma = 0, mitDiff = 0, diffBrutto = 0, diffNetto = 0;
        for (const z of v.zeilen) {
            const m = z.monate.find(x => x.monat === monat);
            if (!m) continue;
            ma++;
            if (_lsimHatDiff(m)) mitDiff++;
            diffBrutto += _lsimDiff(m.brutto, m.bruttoMirus);
            diffNetto += _lsimDiff(m.netto, m.nettoMirus);
        }
        return { monat, ma, mitDiff, diffBrutto, diffNetto };
    });
    const uebersicht = `
        <div class="card" style="padding:16px 18px">
            <div style="font-weight:700;margin-bottom:8px">Vergleich OneCrew ↔ Mirus</div>
            <table style="width:100%;border-collapse:collapse;font-size:13px">
                <thead><tr style="text-align:right;color:#64748b">
                    <th style="text-align:left;padding:4px 6px">Monat</th><th style="padding:4px 6px">MA</th>
                    <th style="padding:4px 6px">mit Abweichung</th><th style="padding:4px 6px">Δ Brutto</th><th style="padding:4px 6px">Δ Netto</th>
                </tr></thead>
                <tbody>${proMonat.map(p => `<tr style="text-align:right;border-top:1px solid #eee">
                    <td style="text-align:left;padding:4px 6px">${_LSIM_MONATE[p.monat - 1]}</td>
                    <td style="padding:4px 6px">${p.ma}</td>
                    <td style="padding:4px 6px;color:${p.mitDiff ? '#b45309' : '#15803d'};font-weight:600">${p.mitDiff}</td>
                    <td style="padding:4px 6px">${_lsimChf(p.diffBrutto)}</td>
                    <td style="padding:4px 6px">${_lsimChf(p.diffNetto)}</td>
                </tr>`).join('')}</tbody>
            </table>
            <div style="margin-top:8px;font-size:12px;color:#64748b">Δ = OneCrew minus Mirus. Mirus: 250.1 Bruttolohn, 1000.1 Nettolohn, 6000.1 Auszahlung. Klick auf einen Monat zeigt beide Lohnzettel Zeile für Zeile.</div>
        </div>`;

    const zellen = (ist, soll) => {
        const d = _lsimDiff(ist, soll);
        return `<td style="padding:3px 6px;text-align:right">${_lsimChf(ist)}</td>
                <td style="padding:3px 6px;text-align:right;color:#64748b">${_lsimChf(soll)}</td>
                <td style="padding:3px 6px;text-align:right;font-weight:600;color:${d === 0 ? '#15803d' : '#dc2626'}">${d === 0 ? '0.00' : _lsimChf(d)}</td>`;
    };

    const personen = v.zeilen
        .map(z => ({ ...z, monate: _lsimAlleZeigen ? z.monate : z.monate.filter(_lsimHatDiff) }))
        .filter(z => z.monate.length);

    const liste = personen.map(z => `
        <div class="card" style="padding:12px 16px;margin-top:10px">
            <div style="font-weight:700;font-size:13.5px">${_lsimEsc(z.name)} <span style="color:#94a3b8;font-weight:400">${_lsimEsc(z.personalnummer || '')}</span></div>
            <table style="width:100%;border-collapse:collapse;font-size:12.5px;margin-top:6px">
                <thead><tr style="color:#64748b;text-align:right">
                    <th style="text-align:left;padding:3px 6px">Monat</th>
                    <th style="padding:3px 6px">Brutto OC</th><th style="padding:3px 6px">Mirus</th><th style="padding:3px 6px">Δ</th>
                    <th style="padding:3px 6px">Netto OC</th><th style="padding:3px 6px">Mirus</th><th style="padding:3px 6px">Δ</th>
                    <th style="padding:3px 6px">Auszahlung OC</th><th style="padding:3px 6px">Mirus</th><th style="padding:3px 6px">Δ</th>
                </tr></thead>
                <tbody>${z.monate.map(m => `
                    <tr style="border-top:1px solid #eee;cursor:pointer" onclick="lsimDetail(${z.employeeId}, ${m.monat}, this)">
                        <td style="padding:3px 6px">${_LSIM_MONATE[m.monat - 1]}${m.fehler ? ` <span style="color:#b45309" title="${_lsimEsc(m.fehler)}">⚠ nicht rechenbar</span>` : ''}${!m.simuliert ? ' <span style="color:#b45309">fehlt in OneCrew</span>' : ''}${m.sonderzahlungen ? ' <span title="' + _lsimEsc(m.sonderzahlungen) + '" style="color:#3730a3">＋Sonderzahlung</span>' : ''}</td>
                        ${zellen(m.brutto, m.bruttoMirus)}
                        ${zellen(m.netto, m.nettoMirus)}
                        ${zellen(m.auszahlung, m.auszahlungMirus)}
                    </tr>`).join('')}</tbody>
            </table>
        </div>`).join('');

    el.innerHTML = uebersicht + `
        <div style="margin-top:12px;display:flex;gap:10px;align-items:center">
            <span style="font-size:13px;color:#3f3f3f"><b>${personen.length}</b> Mitarbeitende ${_lsimAlleZeigen ? '' : 'mit Abweichung'}</span>
            <button class="btn btn-secondary" style="font-size:12px" onclick="lsimToggleAlle()">${_lsimAlleZeigen ? 'Nur Abweichungen zeigen' : 'Auch übereinstimmende zeigen'}</button>
        </div>` + liste;
}

async function lsimDetail(employeeId, monat, tr) {
    const offen = tr.nextElementSibling && tr.nextElementSibling.classList.contains('lsim-detail');
    document.querySelectorAll('.lsim-detail').forEach(x => x.remove());
    if (offen) return;
    const cpId = _lsimCp();
    const r = await fetch(`/api/lohn-simulation/detail?companyProfileId=${cpId}&employeeId=${employeeId}&jahr=${_lsimJahr()}&monat=${monat}`, { headers: _lsimAuth() });
    if (!r.ok) return;
    const d = await r.json();
    const oc = (d.oneCrew || []).map(z => `<tr><td style="padding:2px 6px;color:#64748b">${_lsimEsc(z.code || '')}</td><td style="padding:2px 6px">${_lsimEsc(z.bezeichnung || '')}</td><td style="padding:2px 6px;text-align:right">${_lsimChf(z.betrag)}</td></tr>`).join('');
    const mi = (d.mirus || []).filter(z => z.sektion === 'AN').map(z => `<tr><td style="padding:2px 6px;color:#64748b">${_lsimEsc(z.code)}</td><td style="padding:2px 6px">${_lsimEsc(z.bezeichnung)}</td><td style="padding:2px 6px;text-align:right">${_lsimChf(z.betrag)}</td></tr>`).join('');
    const s = d.saldi;
    const z2 = x => x == null ? '—' : _lsimChf(x);
    const saldi = s ? `Saldi Ende Monat (OneCrew): Stunden ${z2(s.stunden)} · Nacht ${z2(s.nacht)} · Ferien ${z2(s.ferienTage)} Tage / CHF ${z2(s.ferienGeld)} · Feiertage ${z2(s.feiertagTage)} · 13. ML CHF ${z2(s.dreizehnter)}` : '';
    const row = document.createElement('tr');
    row.className = 'lsim-detail';
    row.innerHTML = `<td colspan="10" style="padding:8px 6px;background:#faf8f5">
        ${d.fehler ? `<div style="color:#b45309;margin-bottom:6px">⚠ ${_lsimEsc(d.fehler)}</div>` : ''}
        ${d.sonderzahlungen ? `<div style="color:#3730a3;margin-bottom:6px">Aus dem Mirus-Lohnkonto übernommen: ${_lsimEsc(d.sonderzahlungen)}</div>` : ''}
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px">
            <div><div style="font-weight:700;margin-bottom:4px">OneCrew</div><table style="width:100%;font-size:12px;border-collapse:collapse">${oc || '<tr><td>—</td></tr>'}</table></div>
            <div><div style="font-weight:700;margin-bottom:4px">Mirus</div><table style="width:100%;font-size:12px;border-collapse:collapse">${mi || '<tr><td>—</td></tr>'}</table></div>
        </div>
        ${saldi ? `<div style="margin-top:6px;font-size:12px;color:#64748b">${saldi}</div>` : ''}
    </td>`;
    tr.after(row);
}
