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

function _obEsc(s) { return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;'); }

function _obFmt(ts) {
    if (!ts) return '';
    return `${ts.slice(8, 10)}.${ts.slice(5, 7)}.${ts.slice(2, 4)} ${ts.slice(11, 16)}`;
}

// ── Schritt 2: Kandidaten einladen (Walter 13.09.2026) ──────────────────
// Nur angenommene Kandidaten, die noch kein MA sind. Sobald HR den
// Kandidaten mit einem importierten MA verknüpft, fällt die Zeile weg.
function hrObInvite() {
    _ivModalShell('hrObInvModal', '📲 Vertrags-SMS senden — Vertrag + Dokumente', 1180);
    document.getElementById('hrObInvModal').style.display = 'flex';
    hrObInvReload();
}

async function hrObInvReload() {
    const body = document.getElementById('hrObInvModalBody');
    if (!body) return;
    body.innerHTML = `
        <div style="font-size:11.5px;color:#8b8b8b;margin-bottom:10px">Nur angenommene Kandidaten — keine Mitarbeitenden, keine Neueintritte. Nach der Umstellung zum MA verschwindet die Person hier.</div>
        <div id="hrObInvList" style="font-size:13px;color:#3f3f3f">Wird geladen…</div>`;
    const list = document.getElementById('hrObInvList');
    try {
        const [rk, rt] = await Promise.all([
            fetch('/api/kandidaten?status=ANGENOMMEN', { headers: ah() }),
            fetch('/api/kandidaten/termine', { headers: ah() }),
        ]);
        const kand = rk.ok ? await rk.json() : [];
        if (!rk.ok) { list.textContent = 'Laden fehlgeschlagen.'; return; }
        const termine = rt.ok ? await rt.json() : [];
        const rows = (Array.isArray(kand) ? kand : [])
            .filter(k => !k.verknuepftEmployeeId)
            .sort((a, b) => (a.vorname || '').localeCompare(b.vorname || '', 'de')
                || (a.name || '').localeCompare(b.name || '', 'de'));
        if (!rows.length) {
            list.innerHTML = '<span style="color:#8b8b8b">Keine offenen Kandidaten. Nach der Verknüpfung mit einem MA erscheint niemand mehr hier.</span>';
            return;
        }
        const gridCols = 'grid-template-columns:minmax(200px,1fr) minmax(150px,0.7fr) minmax(320px,360px) 150px';
        const rowsHtml = rows.map((k, i) => {
            const name = `${k.vorname || ''} ${k.name || ''}`.trim();
            const gesendet = k.willkommenGesendetAm;
            let status;
            if (!gesendet) status = '<span style="background:#fef9c3;color:#854d0e;border-radius:8px;padding:2px 9px;font-size:11px;font-weight:700;white-space:nowrap">noch nicht eingeladen</span>';
            else {
                const ant = k.willkommenAntwort === 'ANGENOMMEN'
                    ? '<span style="color:#166534;font-weight:700">✓ bestätigt</span>'
                    : k.willkommenAntwort === 'ABGELEHNT'
                        ? '<span style="color:#991b1b">✕ abgesagt</span>'
                        : '<span style="color:#b45309">⏳ wartet auf Antwort</span>';
                status = `<div style="white-space:nowrap">📲 ${_obFmt(gesendet)}</div>
                          <div style="white-space:nowrap;margin-top:2px">${ant}</div>`;
            }
            const kannSms = !!(k.telefon && String(k.telefon).trim());
            const bestaetigt = k.willkommenAntwort === 'ANGENOMMEN';
            let terminZelle;
            if (bestaetigt && k.wunschTermin) {
                terminZelle = `<div style="font-size:12.5px;display:flex;align-items:center;gap:6px;flex-wrap:wrap">
                    <input type="hidden" id="kdInvTermin${k.id}" value="${k.wunschTerminId || ''}">
                    <span>📅 ${_obEsc(k.wunschTermin)}</span>
                    <span style="background:#dcfce7;color:#166534;border-radius:8px;padding:1px 8px;font-size:11px;font-weight:700">✓ bestätigt</span></div>`;
            } else {
                const terminOpts = ['<option value="">— ohne Termin —</option>']
                    .concat(termine.filter(t => t.frei > 0 || t.id === k.wunschTerminId).map(t =>
                        `<option value="${t.id}"${t.id === k.wunschTerminId ? ' selected' : ''}>${_obFmt(t.datum + ' 00:00').slice(0, 8)} · ${t.von}${t.bis ? '–' + t.bis : ''} (${t.frei} frei)${t.id === k.wunschTerminId ? ' ★ Wunsch' : ''}</option>`))
                    .join('');
                terminZelle = `<select id="kdInvTermin${k.id}" style="background:#fff;border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:5px 8px;font-size:12px;color:#3f3f3f;width:100%">${terminOpts}</select>`;
            }
            const eintritt = k.fruehesterEintritt ? `Eintritt ${_obFmt(k.fruehesterEintritt + ' 00:00').slice(0, 8)} · ` : '';
            return `
            <div style="display:grid;${gridCols};gap:12px;align-items:center;padding:9px 10px;border-bottom:1px solid rgba(60,55,48,0.08);${i % 2 ? 'background:rgba(255,255,255,0.45);' : ''}">
                <div>
                    <div style="font-weight:800">${_obEsc(name)}</div>
                    <div style="color:#8b8b8b;font-size:11.5px;margin-top:2px">${eintritt}${_obEsc(k.filiale || '')}${k.lgavAusbildung ? ' · ' + _obEsc(k.lgavAusbildung) : ''}</div>
                </div>
                <div style="font-size:12px">${status}</div>
                ${kannSms
                    ? `<div>${terminZelle}</div>
                       <button onclick='hrObInvSend(${k.id}, ${JSON.stringify(name)}, ${JSON.stringify(k.telefon || '')})' style="background:${gesendet ? 'rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(60,55,48,0.22)' : '#3f3f3f;color:#fff;border:none'};border-radius:12px;padding:6px 10px;font-size:12px;font-weight:600;cursor:pointer;white-space:nowrap">${gesendet ? '📱 Erneut senden' : '📱 Einladen'}</button>`
                    : '<span style="grid-column:span 2;color:#991b1b;font-size:12px" title="Keine Handynummer hinterlegt">kein Telefon hinterlegt</span>'}
            </div>`;
        }).join('');
        list.innerHTML = `
            <div style="display:grid;${gridCols};gap:12px;padding:4px 10px 6px;font-size:10.5px;font-weight:700;letter-spacing:0.4px;text-transform:uppercase;color:#8b8b8b;border-bottom:2px solid rgba(60,55,48,0.14)">
                <span>Kandidat/in</span><span>Einladung</span><span>Onboarding-Termin</span><span></span>
            </div>${rowsHtml}`;
    } catch (_) { list.textContent = 'Verbindungsfehler.'; }
}

async function hrObInvSend(kandidatId, name, telefon) {
    const terminSel = document.getElementById(`kdInvTermin${kandidatId}`);
    const terminId = terminSel && terminSel.value ? parseInt(terminSel.value, 10) : null;
    if (!terminId) { showToast('Zuerst einen Onboarding-Tag wählen.', 'error'); return; }
    const terminTxt = terminSel.options
        ? ` — ${terminSel.options[terminSel.selectedIndex].text.replace(/ \(\d+ frei\).*/, '')}`
        : '';
    if (typeof liquidConfirm === 'function'
        && !await liquidConfirm(`Einladung an ${name} — ${telefon} — senden?${terminTxt}`, { title: 'Onboarding-Einladung' })) return;
    const tr = await fetch(`/api/kandidaten/${kandidatId}/termin`, {
        method: 'POST', headers: ah(), body: JSON.stringify({ terminId }),
    });
    const tj = await tr.json().catch(() => ({}));
    if (!tr.ok) { showToast(tj.message || tj.error || 'Termin konnte nicht gesetzt werden.', 'error'); return; }
    const r = await fetch(`/api/kandidaten/${kandidatId}/willkommen-sms`, { method: 'POST', headers: ah() });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Versand fehlgeschlagen.', 'error'); return; }
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
