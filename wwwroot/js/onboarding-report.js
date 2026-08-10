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
let _obInvCp = null;        // Filiale im Einladungs-Modal
let _obInvOffset = 0;       // Eintrittsmonat: Offset zum aktuellen Monat (−1…+2)

function _obEsc(s) { return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;'); }

function _obFmt(ts) {
    if (!ts) return '';
    return `${ts.slice(8, 10)}.${ts.slice(5, 7)}.${ts.slice(2, 4)} ${ts.slice(11, 16)}`;
}

// ── Schritt 2: MA zum Onboarding einladen (Walter 10.08.2026) ───────────
// Restaurant wählen → alle MA mit Eintritt in der Zukunft → Vertrags-SMS
// (inkl. Onboarding-Dokumente am Link) direkt auslösen.
function hrObInvite() {
    _ivModalShell('hrObInvModal', '🚀 Onboarding — MA einladen', 960);
    document.getElementById('hrObInvModal').style.display = 'flex';
    hrObInvReload();
}

async function hrObInvReload() {
    const body = document.getElementById('hrObInvModalBody');
    if (!body) return;
    // KEINE Filial-Auswahl (Walter 10.08.2026): immer ALLE Eintritte des
    // gewählten Monats (−1…+2), sortiert nach Eintrittsdatum, Filiale pro Zeile.
    const monNamen = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
    const now = new Date();
    const monOpts = [-1, 0, 1, 2].map(off => {
        const d = new Date(now.getFullYear(), now.getMonth() + off, 1);
        return `<option value="${off}"${off === _obInvOffset ? ' selected' : ''}>${monNamen[d.getMonth()]} ${d.getFullYear()}</option>`;
    }).join('');
    const selDate = new Date(now.getFullYear(), now.getMonth() + _obInvOffset, 1);

    body.innerHTML = `
        <div style="display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap;margin-bottom:10px">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Eintrittsmonat
                <select onchange="_obInvOffset=parseInt(this.value,10);hrObInvReload()" style="background:#fff;border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:6px 10px;font-size:13px;color:#3f3f3f">${monOpts}</select></label>
            <span style="font-size:11.5px;color:#8b8b8b;padding-bottom:8px">Alle MA mit Eintritt im gewählten Monat, über alle Restaurants.</span>
        </div>
        <div id="hrObInvList" style="font-size:13px;color:#3f3f3f">Wird geladen…</div>`;
    const list = document.getElementById('hrObInvList');
    try {
        // MA-Liste + Onboarding-Termine (für die Termin-Auswahl) parallel laden.
        const [r, rt] = await Promise.all([
            fetch(`/api/contract-share/onboarding-einladungen?year=${selDate.getFullYear()}&month=${selDate.getMonth() + 1}`, { headers: ah() }),
            fetch('/api/kandidaten/termine', { headers: ah() }),
        ]);
        const rows = await r.json();
        if (!r.ok) { list.textContent = 'Laden fehlgeschlagen.'; return; }
        const termine = rt.ok ? await rt.json() : [];
        if (!rows.length) { list.innerHTML = '<span style="color:#8b8b8b">Keine Mitarbeitenden mit Eintritt in diesem Monat.</span>'; return; }
        // Tabellen-Grid mit festen Spalten (Walter 10.08.2026 «schöner anordnen»):
        // MA (Name + Eintritt·Filiale·Modell) | Einladung (Status) | Termin | Aktion.
        const gridCols = 'grid-template-columns:minmax(190px,1.1fr) minmax(160px,0.9fr) minmax(220px,240px) 150px';
        const rowsHtml = rows.map((m, i) => {
            let status;
            if (!m.gesendetAm) status = '<span style="background:#fef9c3;color:#854d0e;border-radius:8px;padding:2px 9px;font-size:11px;font-weight:700;white-space:nowrap">noch nicht eingeladen</span>';
            else {
                status = `<div style="white-space:nowrap">📲 ${_obFmt(m.gesendetAm)}</div>
                          <div style="white-space:nowrap;margin-top:2px">${m.geoeffnetAm
                    ? `👁 ${_obFmt(m.geoeffnetAm)}${m.pdfAm ? ' <span style="color:#166534;font-weight:700">✓</span>' : ''}`
                    : '<span style="color:#b45309">👁 noch nicht geöffnet</span>'}</div>`;
            }
            const kannSms = !!(m.telefon && m.telefon.trim());
            // Termin-Auswahl: freie Termine; Wunschtermin des GF vorausgewählt.
            const terminOpts = ['<option value="">— ohne Termin —</option>']
                .concat(termine.filter(t => t.frei > 0 || t.id === m.wunschTerminId).map(t =>
                    `<option value="${t.id}"${t.id === m.wunschTerminId ? ' selected' : ''}>${_obFmt(t.datum + ' 00:00').slice(0, 8)} · ${t.von}${t.bis ? '–' + t.bis : ''} (${t.frei} frei)${t.id === m.wunschTerminId ? ' ★ Wunsch' : ''}</option>`))
                .join('');
            return `
            <div style="display:grid;${gridCols};gap:12px;align-items:center;padding:9px 10px;border-bottom:1px solid rgba(60,55,48,0.08);${i % 2 ? 'background:rgba(255,255,255,0.45);' : ''}">
                <div>
                    <div style="font-weight:800">${_obEsc(m.name)}</div>
                    <div style="color:#8b8b8b;font-size:11.5px;margin-top:2px">Eintritt ${_obFmt(m.eintritt + ' 00:00').slice(0, 8)} · ${_obEsc(m.filiale || '')}${m.modell ? ' · ' + _obEsc(m.modell) : ''}</div>
                </div>
                <div style="font-size:12px">${status}</div>
                ${kannSms
                    ? `<select id="kdInvTermin${m.employeeId}" style="background:#fff;border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:5px 8px;font-size:12px;color:#3f3f3f;width:100%">${terminOpts}</select>
                       <button onclick="hrObInvSend(${m.employeeId}, '${_obEsc(m.name)}', '${_obEsc(m.telefon)}')" style="background:${m.gesendetAm ? 'rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(60,55,48,0.22)' : '#3f3f3f;color:#fff;border:none'};border-radius:12px;padding:6px 10px;font-size:12px;font-weight:600;cursor:pointer;white-space:nowrap">${m.gesendetAm ? '📱 Erneut senden' : '📱 Einladen'}</button>`
                    : '<span style="grid-column:span 2;color:#991b1b;font-size:12px" title="Keine Handynummer hinterlegt — im MA-Detail erfassen">kein Telefon hinterlegt</span>'}
            </div>`;
        }).join('');
        list.innerHTML = `
            <div style="display:grid;${gridCols};gap:12px;padding:4px 10px 6px;font-size:10.5px;font-weight:700;letter-spacing:0.4px;text-transform:uppercase;color:#8b8b8b;border-bottom:2px solid rgba(60,55,48,0.14)">
                <span>Mitarbeiter/in</span><span>Einladung</span><span>Onboarding-Termin</span><span></span>
            </div>${rowsHtml}`;
    } catch (_) { list.textContent = 'Verbindungsfehler.'; }
}

async function hrObInvSend(employeeId, name, telefon) {
    const terminSel = document.getElementById(`kdInvTermin${employeeId}`);
    const terminId = terminSel && terminSel.value ? parseInt(terminSel.value, 10) : null;
    const terminTxt = terminId ? ` — inkl. Onboarding-Termin ${terminSel.options[terminSel.selectedIndex].text.replace(/ \(\d+ frei\).*/, '')}` : '';
    if (typeof liquidConfirm === 'function'
        && !await liquidConfirm(`Vertrags-SMS (inkl. Onboarding-Dokumente am Link) an ${name} — ${telefon} — senden?${terminTxt}`, { title: 'Onboarding-Einladung' })) return;
    const r = await fetch('/api/contract-share/send', {
        method: 'POST', headers: ah(), body: JSON.stringify({ employeeId, terminId }),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.error || j.message || 'Versand fehlgeschlagen.', 'error'); return; }
    showToast(`Einladung an ${j.to} gesendet.` + (j.redirectedTo ? ` (Test-Umleitung: ${j.redirectedTo})` : ''), 'success');
    hrObInvReload();
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
