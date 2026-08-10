// ══════════════════════════════════════════════════════════════════════
//  ONBOARDING-AUSWERTUNG «Dokumente gelesen» (Walter-Vorgabe 10.08.2026)
//  HR-Hub → Kachel ONBOARDING. Pro MA der global gewählten Filiale:
//  Status aktiv/inaktiv · Vertrag (gesendet/geöffnet/PDF) · pro Onboarding-
//  Dokument (Filial-Dokumente, Kategorie ONBOARDING) der Erst-Abruf über
//  den Vertrags-Link. Dokument-Spalten sind automatisch nummeriert
//  (alphabetisch), Legende unter der Tabelle.
// ══════════════════════════════════════════════════════════════════════

let _obRep = null;          // letzter Report
let _obRepInaktive = false; // Filter «inaktive anzeigen»

function _obEsc(s) { return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;'); }

function _obFmt(ts) {
    if (!ts) return '';
    return `${ts.slice(8, 10)}.${ts.slice(5, 7)}.${ts.slice(2, 4)} ${ts.slice(11, 16)}`;
}

function hrObOpen() {
    _ivModalShell('hrObModal', '🚀 Onboarding — Dokumente gelesen', 980);
    document.getElementById('hrObModal').style.display = 'flex';
    hrObReload();
}

async function hrObReload() {
    const body = document.getElementById('hrObModalBody');
    if (!body) return;
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
    if (!cpId) {
        body.innerHTML = '<div style="background:#fef9c3;border:1px solid #fde68a;border-radius:10px;padding:10px;color:#854d0e">Bitte links oben zuerst eine <b>Filiale</b> wählen — die Auswertung gilt pro Filiale.</div>';
        return;
    }
    body.innerHTML = '<span style="color:#8b8b8b">Wird geladen…</span>';
    try {
        const r = await fetch(`/api/contract-share/onboarding-report?companyProfileId=${cpId}`, { headers: ah() });
        _obRep = await r.json();
        if (!r.ok) { body.innerHTML = 'Laden fehlgeschlagen.'; return; }
        _obRender();
    } catch (_) {
        body.innerHTML = '<span style="color:#991b1b">Verbindungsfehler.</span>';
    }
}

function hrObToggleInaktive(chk) {
    _obRepInaktive = !!chk.checked;
    _obRender();
}

function _obRender() {
    const body = document.getElementById('hrObModalBody');
    if (!body || !_obRep) return;
    const doks = _obRep.doks || [];
    const alle = _obRep.rows || [];
    const aktive = alle.filter(x => x.aktiv);
    const inaktive = alle.filter(x => !x.aktiv);
    const rows = _obRepInaktive ? alle : aktive;

    const dokHead = doks.map(d =>
        `<th style="min-width:34px;text-align:center" title="${_obEsc(d.name)}">${d.nr}</th>`).join('');

    const tr = rows.map(m => {
        // Vertrag: 📲 gesendet · 👁 geöffnet (✓ = PDF abgerufen)
        let vertrag;
        if (!m.gesendetAm) vertrag = '<span style="color:#b0aca4">–</span>';
        else {
            vertrag = `📲 ${_obFmt(m.gesendetAm)}`;
            vertrag += m.geoeffnetAm
                ? ` · 👁 ${_obFmt(m.geoeffnetAm)}${m.pdfAm ? ' <span style="color:#166534">✓</span>' : ''}`
                : ' · <span style="color:#b45309">👁 –</span>';
        }
        const dokCells = doks.map(d => {
            const ts = (m.gelesen || {})[String(d.id)];
            return ts
                ? `<td style="text-align:center" title="${_obEsc(d.name)} — gelesen ${_obFmt(ts)}"><span style="color:#166534;font-weight:800">✓</span></td>`
                : `<td style="text-align:center;color:#c8c3ba">–</td>`;
        }).join('');
        return `<tr${m.aktiv ? '' : ' style="opacity:0.55"'}>
            <td style="white-space:nowrap"><b>${_obEsc(m.name)}</b></td>
            <td>${m.aktiv
                ? '<span style="background:#dcfce7;color:#166534;border-radius:8px;padding:1px 8px;font-size:11px;font-weight:700">aktiv</span>'
                : '<span style="background:#f1efe9;color:#8b8b8b;border-radius:8px;padding:1px 8px;font-size:11px;font-weight:700">inaktiv</span>'}</td>
            <td style="white-space:nowrap;font-size:12px">${vertrag}</td>
            ${dokCells}
        </tr>`;
    }).join('');

    const legende = doks.length
        ? `<div style="margin-top:10px;font-size:12px;color:#646464;display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:2px 16px">
            ${doks.map(d => `<div><b>${d.nr}</b> — ${_obEsc(d.name.replace(/\.pdf$/i, ''))}</div>`).join('')}</div>`
        : '<div style="margin-top:10px;color:#854d0e;background:#fef9c3;border:1px solid #fde68a;border-radius:10px;padding:8px">Diese Filiale hat noch keine Dokumente in der Kategorie «Onboarding (Vertrags-Link)» (Filiale → Dokumente).</div>';

    body.innerHTML = `
        <div style="display:flex;align-items:center;gap:14px;flex-wrap:wrap;margin-bottom:10px">
            <span style="font-size:13px"><b>${aktive.length}</b> aktive · <b>${inaktive.length}</b> inaktive MA</span>
            <label style="display:flex;align-items:center;gap:6px;font-size:12.5px;color:#646464;cursor:pointer">
                <input type="checkbox" ${_obRepInaktive ? 'checked' : ''} onchange="hrObToggleInaktive(this)"> inaktive anzeigen</label>
            <span style="flex:1"></span>
            <span style="font-size:11.5px;color:#8b8b8b">📲 gesendet · 👁 Link geöffnet · ✓ Vertrag-PDF/Dokument abgerufen</span>
        </div>
        <div style="max-height:56vh;overflow:auto;border:1px solid rgba(60,55,48,0.14);border-radius:12px;background:#fff">
            <table style="border-collapse:collapse;width:100%;font-size:12.5px">
                <thead><tr style="position:sticky;top:0;background:#f6f3ee">
                    <th style="text-align:left;padding:6px 8px">Mitarbeiter</th>
                    <th style="text-align:left;padding:6px 8px">Status</th>
                    <th style="text-align:left;padding:6px 8px">Vertrag</th>
                    ${dokHead}
                </tr></thead>
                <tbody>${tr || '<tr><td colspan="99" style="padding:10px;color:#8b8b8b">Keine Mitarbeitenden gefunden.</td></tr>'}</tbody>
            </table>
        </div>
        ${legende}
        <style>#hrObModal td, #hrObModal th { padding:5px 8px; border-bottom:1px solid rgba(60,55,48,0.08); }</style>`;
}
