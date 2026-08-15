// ══════════════════════════════════════════════════════════════════════
//  GRUPPEN-E-MAIL AN MITARBEITENDE (Walter-Vorgabe 14.08.2026)
//  Erste geöffnete Funktion der «Mitarbeiter-Korrespondenz»:
//  Selektion Filiale (eine/alle) × Vertragsmodell (FLEX/MTP/FIX/FIX-M),
//  Empfänger-Vorschau mit Abwahl, Betreff + Text, Versand als Einzelmails
//  über den SMTP-Dienst. Lohnbeleg-Versand bleibt bewusst geschlossen.
//  Anwendungsfall #1: Dienstplan-Handy-Link ans Management-Team (FIX-M).
// ══════════════════════════════════════════════════════════════════════
let _meEmpfaenger = [];

function maEmailInit() {
    // Filial-Select aus allBranches (folgt der Konvention: Sidebar-Filiale
    // als Vorauswahl, «Alle Filialen» verfügbar).
    const sel = document.getElementById('meBranch');
    if (sel && typeof allBranches !== 'undefined') {
        const cur = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? String(fixedCompanyProfileId) : '';
        sel.innerHTML = '<option value="">Alle Filialen</option>' + (allBranches || [])
            .map(b => `<option value="${b.id}" ${String(b.id) === cur ? 'selected' : ''}>${(b.restaurantCode ? b.restaurantCode + ' – ' : '')}${b.city || b.branchName || ''}</option>`)
            .join('');
    }
    const list = document.getElementById('meListe');
    if (list) list.innerHTML = '<div style="color:#8b8b8b;font-size:12.5px">Selektion wählen und «Empfänger laden» klicken.</div>';
    _meEmpfaenger = [];
    const info = document.getElementById('meSendInfo');
    if (info) info.textContent = '';
    _meLadeFunktionen();
}

// Funktions-Checkboxen aus dem JobGroup-Katalog (Walter 15.08.2026).
// Default: alle an = kein Filter. Codes im data-code, Anzeige deutsch.
let _meFunkGeladen = false;
async function _meLadeFunktionen() {
    const row = document.getElementById('meFunkRow');
    if (!row || _meFunkGeladen) return;
    try {
        const r = await fetch('/api/jobgroups', { headers: ah() });
        if (!r.ok) return;
        const j = await r.json();
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        row.innerHTML = j.map(g =>
            `<label><input type="checkbox" class="meFunkCb" data-code="${esc(g.code)}" checked> ${esc(g.displayName || g.code)}</label>`).join('');
        _meFunkGeladen = true;
    } catch (e) { /* Katalog nicht ladbar → Filter bleibt aus */ }
}

function _meFunktionen() {
    const alle = Array.from(document.querySelectorAll('.meFunkCb'));
    if (!alle.length) return '';                 // Katalog nicht geladen → kein Filter
    const gewaehlt = alle.filter(cb => cb.checked);
    if (gewaehlt.length === alle.length) return ''; // alle an = kein Filter
    return gewaehlt.map(cb => cb.dataset.code).join(',');
}

function meAlleFunk(an) {
    document.querySelectorAll('.meFunkCb').forEach(cb => { cb.checked = an; });
}

function _meModelle() {
    return ['FLEX', 'MTP', 'FIX', 'FIX-M']
        .filter(m => document.getElementById('meMod-' + m)?.checked)
        .join(',');
}

function meAlleModelle(an) {
    ['FLEX', 'MTP', 'FIX', 'FIX-M'].forEach(m => {
        const cb = document.getElementById('meMod-' + m);
        if (cb) cb.checked = an;
    });
}

async function meLadeEmpfaenger() {
    const list = document.getElementById('meListe');
    if (!list) return;
    const branch = document.getElementById('meBranch')?.value || '';
    // Modell ODER Funktion reicht (Walter 15.08.2026): kein Modell gewählt
    // = alle Modelle, solange mindestens eine Funktion angehakt ist.
    const modelle = _meModelle();
    const funkGewaehlt = document.querySelectorAll('.meFunkCb:checked').length;
    if (!modelle && !funkGewaehlt) {
        showToast('Mindestens ein Vertragsmodell oder eine Funktion wählen.', 'error'); return;
    }
    const funktionen = _meFunktionen();
    list.innerHTML = '<div style="color:#8b8b8b;font-size:12.5px">Wird geladen…</div>';
    try {
        const q = `/api/ma-email/empfaenger?${modelle ? 'modelle=' + encodeURIComponent(modelle) : ''}${branch ? '&companyProfileId=' + branch : ''}${funktionen ? '&funktionen=' + encodeURIComponent(funktionen) : ''}`;
        const r = await fetch(q, { headers: ah() });
        const j = await r.json();
        if (!r.ok) { list.textContent = 'Fehler: ' + (j?.message || j?.error || ('HTTP ' + r.status)); return; }
        _meEmpfaenger = j;
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        if (!j.length) { list.innerHTML = '<div style="color:#8b8b8b;font-size:12.5px">Keine Mitarbeitenden für diese Selektion.</div>'; return; }
        const mitMail = j.filter(e => e.email).length;
        list.innerHTML = `
            <div style="font-size:12.5px;color:#3f3f3f;margin-bottom:6px">
                <b>${j.length}</b> Mitarbeitende gefunden, <b>${mitMail}</b> mit E-Mail-Adresse.
                <button type="button" onclick="meAlleEmpf(true)" style="margin-left:10px;background:none;border:none;color:#6b7280;cursor:pointer;font-size:12px;text-decoration:underline">alle an</button>
                <button type="button" onclick="meAlleEmpf(false)" style="background:none;border:none;color:#6b7280;cursor:pointer;font-size:12px;text-decoration:underline">alle ab</button>
            </div>
            <div style="max-height:320px;overflow:auto;border:1px solid rgba(60,55,48,0.14);border-radius:12px;background:rgba(255,255,255,0.5)">
            <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                ${j.map(e => `
                <tr style="border-bottom:1px solid rgba(60,55,48,0.08)${e.email ? '' : ';opacity:0.55'}">
                    <td style="padding:3px 8px;width:26px"><input type="checkbox" class="meEmpfCb" data-id="${e.employeeId}" ${e.email ? 'checked' : 'disabled'}></td>
                    <td style="padding:3px 6px;font-weight:600;color:#3f3f3f;white-space:nowrap">${esc(e.name)}</td>
                    <td style="padding:3px 6px;color:#8b8b8b">${esc(e.filiale || '')}</td>
                    <td style="padding:3px 6px;color:#8b8b8b">${esc(e.modell || '')}</td>
                    <td style="padding:3px 6px;color:#8b8b8b">${esc(e.funktion || '')}</td>
                    <td style="padding:3px 6px;color:${e.email ? '#646464' : '#b91c1c'}">${e.email ? esc(e.email) : 'keine E-Mail hinterlegt'}</td>
                </tr>`).join('')}
            </table></div>`;
    } catch (e) { list.textContent = 'Verbindungsfehler: ' + e.message; }
}

function meAlleEmpf(an) {
    document.querySelectorAll('.meEmpfCb:not(:disabled)').forEach(cb => { cb.checked = an; });
}

async function meSenden() {
    const betreff = (document.getElementById('meBetreff')?.value || '').trim();
    const text = (document.getElementById('meText')?.value || '').trim();
    const ids = Array.from(document.querySelectorAll('.meEmpfCb'))
        .filter(cb => cb.checked).map(cb => parseInt(cb.dataset.id, 10));
    const info = document.getElementById('meSendInfo');

    if (!betreff) { showToast('Bitte einen Betreff eingeben.', 'error'); return; }
    if (!text) { showToast('Bitte einen Nachrichtentext eingeben.', 'error'); return; }
    if (!ids.length) { showToast('Bitte zuerst Empfänger laden und auswählen.', 'error'); return; }

    const ok = await liquidConfirm(
        `E-Mail «${betreff}» jetzt an ${ids.length} Mitarbeitende senden? Jede Person erhält eine eigene E-Mail (Adressen bleiben privat).`,
        { title: 'Gruppen-E-Mail', yesLabel: `Ja, an ${ids.length} senden`, noLabel: 'Abbrechen' });
    if (!ok) return;

    if (info) { info.textContent = 'Versand läuft…'; info.style.color = '#64748b'; }
    try {
        const r = await fetch('/api/ma-email/senden', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({ betreff, text, employeeIds: ids }),
        });
        const j = await r.json();
        if (!r.ok) {
            if (info) { info.textContent = 'Fehler: ' + (j?.message || j?.error || ('HTTP ' + r.status)); info.style.color = '#b91c1c'; }
            return;
        }
        const teile = [`${j.gesendet} gesendet`];
        if ((j.fehlgeschlagen || []).length) teile.push(`${j.fehlgeschlagen.length} fehlgeschlagen (${j.fehlgeschlagen.map(f => f.name).join(', ')})`);
        if ((j.ohneEmail || []).length) teile.push(`${j.ohneEmail.length} ohne E-Mail übersprungen`);
        if (info) { info.textContent = '✓ ' + teile.join(' · '); info.style.color = (j.fehlgeschlagen || []).length ? '#9a3412' : '#166534'; }
        showToast(`E-Mail an ${j.gesendet} Mitarbeitende gesendet.`, 'success');
    } catch (e) {
        if (info) { info.textContent = 'Verbindungsfehler: ' + e.message; info.style.color = '#b91c1c'; }
    }
}
