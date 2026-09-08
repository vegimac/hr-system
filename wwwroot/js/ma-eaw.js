// ══════════════════════════════════════════════════════════════════════
//  EASY@WORK-MITTEILUNG AN MITARBEITENDE (Walter-Vorgabe 08.09.2026)
//  Dritter Kanal neben Gruppen-E-Mail und SMS. Gleiche Empfängerwahl wie
//  die Gruppen-E-Mail (Filiale × Vertragsmodell × Funktion, Vorschau) —
//  die Vorschau kommt vom selben Endpoint /api/ma-email/empfaenger, dazu
//  /api/ma-eaw/check, ob der MA eine easy@work-ID hat.
//  Betreff + Text → PDF im Haus-Stil mit wählbarer Unterzeichnung, als
//  HR-Datei ins Dossier jedes MA; easy@work benachrichtigt den MA in der App.
//  Freigabe: Systemsteuerung → Freigabe-Matrix, Spalte «easy@work scharf»;
//  ohne Haken geht die Mitteilung nur an die Test-Personalnummer.
// ══════════════════════════════════════════════════════════════════════
let _mwEmpfaenger = [];
let _mwStatus = null;

function maEawInit() {
    const sel = document.getElementById('mwBranch');
    if (sel && typeof allBranches !== 'undefined') {
        const cur = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? String(fixedCompanyProfileId) : '';
        sel.innerHTML = '<option value="">Alle Filialen</option>' + (allBranches || [])
            .map(b => `<option value="${b.id}" ${String(b.id) === cur ? 'selected' : ''}>${(b.restaurantCode ? b.restaurantCode + ' – ' : '')}${(b.workLocation || b.city || b.branchName || '').replace(/\s*\([^)]*\)\s*$/, '')}</option>`)
            .join('');
        sel.onchange = () => mwLadeUnterzeichner();
    }
    const list = document.getElementById('mwListe');
    if (list) list.innerHTML = '<div style="color:#8b8b8b;font-size:12.5px">Selektion wählen und «Empfänger laden» klicken.</div>';
    _mwEmpfaenger = [];
    const info = document.getElementById('mwSendInfo');
    if (info) info.textContent = '';
    _mwLadeFunktionen();
    mwLadeStatus();
    mwLadeUnterzeichner();
    mwLadeLog();
}

// Freigabe-Stand oben auf der Seite: scharf oder Umleitung an die Test-Personalnummer.
async function mwLadeStatus() {
    const box = document.getElementById('mwStatus');
    if (!box) return;
    try {
        const r = await fetch('/api/ma-eaw/status', { headers: ah() });
        const j = await r.json();
        _mwStatus = j;
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        if (!j.konfiguriert) {
            box.innerHTML = '<div style="background:#fef2f2;border:1px solid #fca5a5;border-radius:10px;padding:9px 12px;color:#991b1b;font-size:12.5px">easy@work ist nicht konfiguriert.</div>';
        } else if (j.scharf) {
            box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fca5a5;border-radius:10px;padding:9px 12px;color:#991b1b;font-size:12.5px"><b>⚠️ Scharf:</b> Mitteilungen gehen an die echten Mitarbeitenden (Freigabe-Matrix, Spalte easy@work). Dateityp in easy@work: «${esc(j.dateityp)}».</div>`;
        } else if (j.testNummer) {
            box.innerHTML = `<div style="background:#f0fdf4;border:1px solid #86efac;border-radius:10px;padding:9px 12px;color:#166534;font-size:12.5px">✓ <b>Testmodus:</b> jede Mitteilung geht nur an die Test-Personalnummer <b>${esc(j.testNummer)}</b> (einmal), alle anderen Empfänger werden übersprungen. Scharf schalten: Systemsteuerung → Freigabe-Matrix → easy@work. Dateityp in easy@work: «${esc(j.dateityp)}».</div>`;
        } else {
            box.innerHTML = '<div style="background:#eff6ff;border:1px solid #bfdbfe;border-radius:10px;padding:9px 12px;color:#1e40af;font-size:12.5px">Nicht scharf und keine Test-Personalnummer hinterlegt — der Versand ist blockiert. Systemsteuerung → Freigabe-Matrix → easy@work.</div>';
        }
    } catch (e) { box.innerHTML = ''; }
}

// Unterzeichnung wählbar (Walter 08.09.2026): Benutzer der gewählten Filiale
// (mit Funktion aus dem Filial-Zugang), sonst alle Benutzer; plus «ohne».
async function mwLadeUnterzeichner() {
    const sel = document.getElementById('mwUnterzeichner');
    if (!sel) return;
    const branch = document.getElementById('mwBranch')?.value || '';
    const vorher = sel.value;
    try {
        const r = await fetch('/api/ma-eaw/unterzeichner' + (branch ? '?companyProfileId=' + branch : ''), { headers: ah() });
        const j = await r.json();
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        sel.innerHTML = (j || []).map(u =>
            `<option value="${u.userId}" ${(vorher ? String(u.userId) === vorher : u.istIch) ? 'selected' : ''}>${esc(u.name)}${u.funktion ? ' — ' + esc(u.funktion) : ''}${u.hatUnterschrift ? ' ✍︎' : ''}</option>`).join('')
            + '<option value="ohne" ' + (vorher === 'ohne' ? 'selected' : '') + '>— ohne Unterschrift —</option>';
    } catch (e) { /* Liste bleibt */ }
}

let _mwFunkGeladen = false;
async function _mwLadeFunktionen() {
    const row = document.getElementById('mwFunkRow');
    if (!row || _mwFunkGeladen) return;
    try {
        const r = await fetch('/api/jobgroups', { headers: ah() });
        if (!r.ok) return;
        const j = await r.json();
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        row.innerHTML = j.map(g =>
            `<label><input type="checkbox" class="mwFunkCb" data-code="${esc(g.code)}" checked> ${esc(g.displayName || g.code)}</label>`).join('');
        _mwFunkGeladen = true;
    } catch (e) { /* Katalog nicht ladbar → Filter bleibt aus */ }
}

function _mwFunktionen() {
    const alle = Array.from(document.querySelectorAll('.mwFunkCb'));
    if (!alle.length) return '';
    const gewaehlt = alle.filter(cb => cb.checked);
    if (gewaehlt.length === alle.length) return '';
    return gewaehlt.map(cb => cb.dataset.code).join(',');
}
function _mwFunktionenText() {
    const alle = Array.from(document.querySelectorAll('.mwFunkCb'));
    if (!alle.length) return 'alle';
    const gewaehlt = alle.filter(cb => cb.checked);
    if (gewaehlt.length === alle.length) return 'alle';
    return gewaehlt.map(cb => cb.parentElement.textContent.trim()).join(', ');
}
function mwAlleFunk(an) { document.querySelectorAll('.mwFunkCb').forEach(cb => { cb.checked = an; }); }
function _mwModelle() {
    return ['FLEX', 'MTP', 'FIX', 'FIX-M'].filter(m => document.getElementById('mwMod-' + m)?.checked).join(',');
}
function mwAlleModelle(an) {
    ['FLEX', 'MTP', 'FIX', 'FIX-M'].forEach(m => { const cb = document.getElementById('mwMod-' + m); if (cb) cb.checked = an; });
}

async function mwLadeEmpfaenger() {
    const list = document.getElementById('mwListe');
    if (!list) return;
    const branch = document.getElementById('mwBranch')?.value || '';
    const modelle = _mwModelle();
    const funkGewaehlt = document.querySelectorAll('.mwFunkCb:checked').length;
    if (!modelle && !funkGewaehlt) {
        showToast('Mindestens ein Vertragsmodell oder eine Funktion wählen.', 'error');
        return;
    }
    const funktionen = _mwFunktionen();
    list.innerHTML = '<div style="color:#8b8b8b;font-size:12.5px">Wird geladen…</div>';
    try {
        const q = `/api/ma-email/empfaenger?${modelle ? 'modelle=' + encodeURIComponent(modelle) : ''}${branch ? '&companyProfileId=' + branch : ''}${funktionen ? '&funktionen=' + encodeURIComponent(funktionen) : ''}`;
        const r = await fetch(q, { headers: ah() });
        const antwort = await r.json();
        if (!r.ok) { list.textContent = 'Fehler: ' + (antwort?.message || antwort?.error || ('HTTP ' + r.status)); return; }
        const j = (antwort.zeilen || []).filter(e => e.art !== 'BENUTZER');
        // easy@work-ID prüfen: nur MA mit ID sind erreichbar.
        let eaw = {};
        try {
            const r2 = await fetch('/api/ma-eaw/check', { method: 'POST', headers: ah(),
                body: JSON.stringify({ employeeIds: j.map(e => e.employeeId).filter(Boolean) }) });
            if (r2.ok) (await r2.json()).forEach(x => { eaw[x.employeeId] = x; });
        } catch (_) { /* dann gilt: unbekannt */ }
        _mwEmpfaenger = j;
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        if (!j.length) { list.innerHTML = '<div style="color:#8b8b8b;font-size:12.5px">Keine Mitarbeitenden für diese Selektion.</div>'; return; }
        const erreichbar = j.filter(e => eaw[e.employeeId]?.hatEawId).length;
        list.innerHTML = `
            <div style="font-size:12.5px;color:#3f3f3f;margin-bottom:6px">
                <b>${j.length}</b> Mitarbeitende, <b>${erreichbar}</b> mit easy@work-ID (erreichbar).
                <button type="button" onclick="mwAlleEmpf(true)" style="margin-left:10px;background:none;border:none;color:#6b7280;cursor:pointer;font-size:12px;text-decoration:underline">alle an</button>
                <button type="button" onclick="mwAlleEmpf(false)" style="background:none;border:none;color:#6b7280;cursor:pointer;font-size:12px;text-decoration:underline">alle ab</button>
            </div>
            <div style="max-height:320px;overflow:auto;border:1px solid rgba(60,55,48,0.14);border-radius:12px;background:rgba(255,255,255,0.5)">
            <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                ${j.map(e => {
                    const ok = !!eaw[e.employeeId]?.hatEawId;
                    return `
                <tr style="border-bottom:1px solid rgba(60,55,48,0.08)${ok ? '' : ';opacity:0.55'}">
                    <td style="padding:3px 8px;width:26px"><input type="checkbox" class="mwEmpfCb" data-id="${e.employeeId ?? ''}" ${ok ? 'checked' : 'disabled'}></td>
                    <td style="padding:3px 6px;font-weight:600;color:#3f3f3f;white-space:nowrap">${esc(e.name)}</td>
                    <td style="padding:3px 6px;color:#8b8b8b;font-family:monospace">${esc(eaw[e.employeeId]?.number || '')}</td>
                    <td style="padding:3px 6px;color:#8b8b8b">${esc(e.filiale || '')}</td>
                    <td style="padding:3px 6px;color:#8b8b8b">${esc(e.modell || '')}</td>
                    <td style="padding:3px 6px;color:#8b8b8b">${esc(e.funktion || '')}</td>
                    <td style="padding:3px 6px;color:${ok ? '#166534' : '#b91c1c'}">${ok ? 'easy@work' : 'keine easy@work-ID (MA-Sync)'}</td>
                </tr>`; }).join('')}
            </table></div>`;
    } catch (e) { list.textContent = 'Verbindungsfehler: ' + e.message; }
}

function mwAlleEmpf(an) { document.querySelectorAll('.mwEmpfCb:not(:disabled)').forEach(cb => { cb.checked = an; }); }

async function mwSenden() {
    const betreff = (document.getElementById('mwBetreff')?.value || '').trim();
    const text = (document.getElementById('mwText')?.value || '').trim();
    const datei = document.getElementById('mwAnhang')?.files?.[0] || null;
    const maIds = Array.from(document.querySelectorAll('.mwEmpfCb')).filter(cb => cb.checked).map(cb => cb.dataset.id).filter(Boolean);
    const info = document.getElementById('mwSendInfo');
    const unterz = document.getElementById('mwUnterzeichner')?.value || '';

    if (!betreff) { showToast('Bitte einen Betreff eingeben — den sieht der MA in der App.', 'error'); return; }
    if (!text && !datei) { showToast('Bitte einen Mitteilungstext eingeben oder ein Dokument anhängen.', 'error'); return; }
    if (!maIds.length) { showToast('Bitte zuerst Empfänger laden und auswählen.', 'error'); return; }

    const scharf = !!_mwStatus?.scharf;
    const zielText = scharf
        ? `an ${maIds.length} Mitarbeitende SCHARF senden?`
        : `senden? Testmodus: geht nur an die Test-Personalnummer ${_mwStatus?.testNummer || '?'} (die ${maIds.length} gewählten Empfänger werden übersprungen).`;
    const ok = await liquidConfirm(
        `easy@work-Mitteilung «${betreff}» ${zielText}`
        + (datei ? `\n\nDatei: ${datei.name} (${Math.round(datei.size / 1024)} KB) — statt Text-PDF` : '\n\nAus Betreff + Text entsteht ein PDF im Haus-Stil, das im easy@work-Dossier abgelegt wird; easy@work benachrichtigt den MA in der App.'),
        { title: 'easy@work-Mitteilung', yesLabel: scharf ? `Ja, an ${maIds.length} senden` : 'Ja, Test senden', noLabel: 'Abbrechen' });
    if (!ok) return;

    if (info) { info.textContent = 'Versand läuft… (easy@work erlaubt max. 240 Anfragen/Minute, bei vielen Empfängern dauert es)'; info.style.color = '#64748b'; }
    try {
        const fd = new FormData();
        fd.append('betreff', betreff);
        fd.append('text', text);
        fd.append('employeeIds', maIds.join(','));
        if (datei) fd.append('anhang', datei, datei.name);
        if (unterz === 'ohne') fd.append('ohneUnterschrift', 'true');
        else if (unterz) fd.append('unterzeichnerUserId', unterz);
        const brSel = document.getElementById('mwBranch');
        if (brSel?.value) fd.append('companyProfileId', brSel.value);
        fd.append('filialeText', brSel?.selectedOptions?.[0]?.text || 'Alle Filialen');
        fd.append('modelleText', _mwModelle() || 'alle');
        fd.append('funktionenText', _mwFunktionenText());
        const kopf = ah(); delete kopf['Content-Type'];
        const r = await fetch('/api/ma-eaw/senden', { method: 'POST', headers: kopf, body: fd });
        const j = await r.json();
        if (!r.ok) {
            if (info) { info.textContent = 'Fehler: ' + (j?.message || j?.error || ('HTTP ' + r.status)); info.style.color = '#b91c1c'; }
            return;
        }
        const teile = [`${j.gesendet} gesendet${j.scharf ? '' : ' (Test-MA ' + (j.testNummer || '') + ')'}`];
        if ((j.umgeleitet || []).length) teile.push(`${j.umgeleitet.length} übersprungen (Testmodus)`);
        if ((j.fehlgeschlagen || []).length) teile.push(`${j.fehlgeschlagen.length} fehlgeschlagen (${j.fehlgeschlagen.map(f => f.name + ': ' + f.fehler).join('; ')})`);
        if ((j.ohneEawId || []).length) teile.push(`${j.ohneEawId.length} ohne easy@work-ID übersprungen`);
        if (j.anhang) teile.push(`Datei: ${j.anhang}`);
        if (info) { info.textContent = '✓ ' + teile.join(' · '); info.style.color = (j.fehlgeschlagen || []).length ? '#9a3412' : '#166534'; }
        showToast(`easy@work-Mitteilung an ${j.gesendet} Empfänger gesendet.`, 'success');
        mwLadeLog();
    } catch (e) {
        if (info) { info.textContent = 'Verbindungsfehler: ' + e.message; info.style.color = '#b91c1c'; }
    }
}

async function mwLadeLog() {
    const box = document.getElementById('mwLog');
    if (!box) return;
    try {
        const r = await fetch('/api/ma-eaw/log?limit=25', { headers: ah() });
        if (!r.ok) { box.innerHTML = ''; return; }
        const j = await r.json();
        if (!j.length) { box.innerHTML = '<div style="font-size:12.5px;color:#8b8b8b">Noch keine easy@work-Mitteilung versendet.</div>'; return; }
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        box.innerHTML = `
            <div style="overflow-x:auto">
            <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                <thead><tr style="background:rgba(255,255,255,0.55)">
                    <th style="text-align:left;padding:6px 8px;color:#8b8b8b;font-weight:700;white-space:nowrap">Wann</th>
                    <th style="text-align:left;padding:6px 8px;color:#8b8b8b;font-weight:700">Betreff</th>
                    <th style="text-align:left;padding:6px 8px;color:#8b8b8b;font-weight:700">An wen</th>
                    <th style="text-align:left;padding:6px 8px;color:#8b8b8b;font-weight:700">Unterzeichnet</th>
                    <th style="text-align:right;padding:6px 8px;color:#8b8b8b;font-weight:700;white-space:nowrap">Gesendet</th>
                    <th style="text-align:left;padding:6px 8px;color:#8b8b8b;font-weight:700">Von</th>
                </tr></thead>
                <tbody>${j.map(l => {
                    const wann = new Date(l.gesendetAm).toLocaleString('de-CH', { day:'2-digit', month:'2-digit', year:'numeric', hour:'2-digit', minute:'2-digit' });
                    const gruppe = [l.filiale, l.modelle, l.funktionen].filter(x => x && x !== 'alle').join(' · ') || 'alle Mitarbeitenden';
                    const zusatz = [];
                    if (l.anhangName) zusatz.push('📎 ' + esc(l.anhangName));
                    if (!l.mitText) zusatz.push('ohne Text');
                    const probleme = [];
                    if (l.anzahlFehlgeschlagen) probleme.push(`<a href="#" onclick="mwLogDetails(event, ${l.id})" style="color:#9a3412;text-decoration:underline">${l.anzahlFehlgeschlagen} fehlgeschlagen</a>`);
                    if (l.anzahlOhneEawId) probleme.push(`${l.anzahlOhneEawId} ohne easy@work-ID`);
                    if (l.anzahlUmgeleitet) probleme.push(`${l.anzahlUmgeleitet} übersprungen (Test)`);
                    return `<tr style="border-top:1px solid rgba(60,55,48,0.08)">
                        <td style="padding:6px 8px;white-space:nowrap;color:#646464">${wann}</td>
                        <td style="padding:6px 8px;font-weight:600;color:#3f3f3f">${esc(l.betreff)}
                            ${zusatz.length ? `<div style="font-weight:400;color:#8b8b8b;font-size:11.5px">${zusatz.join(' · ')}</div>` : ''}</td>
                        <td style="padding:6px 8px;color:#646464">${esc(gruppe)}</td>
                        <td style="padding:6px 8px;color:#646464">${esc(l.unterzeichner || '—')}</td>
                        <td style="padding:6px 8px;text-align:right;white-space:nowrap">
                            <b>${l.anzahlGesendet}</b>
                            ${l.scharf ? '<span style="color:#166534;font-size:11px"> scharf</span>' : '<span style="color:#9a3412;font-size:11px"> → Test-MA</span>'}
                            ${probleme.length ? `<div style="color:#9a3412;font-size:11px;font-weight:400">${probleme.join(' · ')}</div>` : ''}</td>
                        <td style="padding:6px 8px;color:#8b8b8b;white-space:nowrap">${esc(l.von || '–')}</td>
                    </tr>
                    <tr id="mwLogDet${l.id}" style="display:none"><td colspan="6" style="padding:0 8px 10px"></td></tr>`;
                }).join('')}</tbody>
            </table></div>`;
    } catch (e) { box.innerHTML = ''; }
}

async function mwLogDetails(ev, logId) {
    if (ev) ev.preventDefault();
    const zeile = document.getElementById('mwLogDet' + logId);
    if (!zeile) return;
    const zelle = zeile.querySelector('td');
    if (zeile.style.display !== 'none') { zeile.style.display = 'none'; return; }
    zeile.style.display = '';
    zelle.innerHTML = '<div style="font-size:12px;color:#8b8b8b;padding:6px 0">lädt …</div>';
    try {
        const r = await fetch('/api/ma-eaw/log/' + logId + '/details', { headers: ah() });
        const j = await r.json();
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        const f = (j.details && j.details.fehlgeschlagen) || [];
        zelle.innerHTML = `<div style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.12);border-radius:8px;padding:10px 12px;font-size:12px">
            <div style="font-weight:700;color:#8b8b8b;text-transform:uppercase;letter-spacing:.05em;margin-bottom:6px;font-size:11.5px">Nicht zugestellt</div>
            ${f.length ? f.map(x => `<div style="padding:3px 0;color:#3f3f3f">${esc(x.name)} <span style="color:#8b8b8b">· ${esc(x.number || '')}</span> — <span style="color:#9a3412">${esc(x.fehler || '')}</span></div>`).join('') : '<div style="color:#8b8b8b">keine Details</div>'}
        </div>`;
    } catch (e) { zelle.innerHTML = '<div style="font-size:12px;color:#8b8b8b">Verbindungsfehler.</div>'; }
}
