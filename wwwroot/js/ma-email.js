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
            .map(b => `<option value="${b.id}" ${String(b.id) === cur ? 'selected' : ''}>${(b.restaurantCode ? b.restaurantCode + ' – ' : '')}${(b.workLocation || b.city || b.branchName || '').replace(/\s*\([^)]*\)\s*$/, '')}</option>`)
            .join('');
    }
    const list = document.getElementById('meListe');
    if (list) list.innerHTML = '<div style="color:#8b8b8b;font-size:12.5px">Selektion wählen und «Empfänger laden» klicken.</div>';
    _meEmpfaenger = [];
    const info = document.getElementById('meSendInfo');
    if (info) info.textContent = '';
    _meLadeFunktionen();
    meLadeLog();
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
    const nurBenutzer = document.getElementById('meBenutzer')?.checked || false;

    // «Nur OneCrew-Benutzer» ist eine gültige Auswahl (Walter 01.09.2026).
    // Vorher verlangte die Prüfung stur ein Vertragsmodell oder eine Funktion
    // und kannte den Benutzer-Haken nicht — wer nur an das Backoffice
    // schreiben wollte, kam nicht weiter.
    if (!modelle && !funkGewaehlt && !nurBenutzer) {
        showToast('Mindestens ein Vertragsmodell, eine Funktion oder die OneCrew-Benutzer wählen.', 'error');
        return;
    }
    // Kein Modell UND keine Funktion, aber Benutzer angehakt = ausschliesslich
    // die Benutzer. Ohne dieses Kennzeichen läse der Server «kein Filter» als
    // «alle Mitarbeitenden» — das Gegenteil dessen, was gemeint ist.
    const nurUser = nurBenutzer && !modelle && !funkGewaehlt;

    const funktionen = _meFunktionen();
    list.innerHTML = '<div style="color:#8b8b8b;font-size:12.5px">Wird geladen…</div>';
    try {
        const mitBenutzern = nurBenutzer ? '&benutzer=true' : '';
        const nurFlag = nurUser ? '&nurBenutzer=true' : '';
        const q = `/api/ma-email/empfaenger?${modelle ? 'modelle=' + encodeURIComponent(modelle) : ''}${branch ? '&companyProfileId=' + branch : ''}${funktionen ? '&funktionen=' + encodeURIComponent(funktionen) : ''}${mitBenutzern}${nurFlag}`;
        const r = await fetch(q, { headers: ah() });
        const antwort = await r.json();
        if (!r.ok) { list.textContent = 'Fehler: ' + (antwort?.message || antwort?.error || ('HTTP ' + r.status)); return; }
        const j = antwort.zeilen || [];
        const doppelte = antwort.doppelteEntfernt || 0;
        _meEmpfaenger = j;
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        if (!j.length) {
            list.innerHTML = '<div style="color:#8b8b8b;font-size:12.5px">'
                + (nurUser ? 'Keine aktiven OneCrew-Benutzer mit E-Mail-Adresse gefunden.'
                           : 'Keine Mitarbeitenden für diese Selektion.') + '</div>';
            return;
        }
        const mitMail = j.filter(e => e.email).length;
        const anzMa   = j.filter(e => e.art === 'MA').length;
        const anzUser = j.filter(e => e.art === 'BENUTZER').length;
        const wer = anzUser
            ? `<b>${anzMa}</b> Mitarbeitende und <b>${anzUser}</b> OneCrew-Benutzer`
            : `<b>${anzMa}</b> Mitarbeitende`;
        // Entdoppelung sichtbar machen: Geschäftsführer stehen als MA UND als
        // Benutzer in der Liste. Wer die Zahl nicht erklärt bekommt, zählt nach
        // und meint, es fehle jemand.
        const doppelInfo = doppelte
            ? `<span style="color:#9a3412"> · ${doppelte} doppelte Adresse${doppelte === 1 ? '' : 'n'} entfernt</span>`
            : '';
        list.innerHTML = `
            <div style="font-size:12.5px;color:#3f3f3f;margin-bottom:6px">
                ${wer}, <b>${mitMail}</b> mit E-Mail-Adresse.${doppelInfo}
                <button type="button" onclick="meAlleEmpf(true)" style="margin-left:10px;background:none;border:none;color:#6b7280;cursor:pointer;font-size:12px;text-decoration:underline">alle an</button>
                <button type="button" onclick="meAlleEmpf(false)" style="background:none;border:none;color:#6b7280;cursor:pointer;font-size:12px;text-decoration:underline">alle ab</button>
            </div>
            <div style="max-height:320px;overflow:auto;border:1px solid rgba(60,55,48,0.14);border-radius:12px;background:rgba(255,255,255,0.5)">
            <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                ${j.map(e => `
                <tr style="border-bottom:1px solid rgba(60,55,48,0.08)${e.email ? '' : ';opacity:0.55'}">
                    <td style="padding:3px 8px;width:26px"><input type="checkbox" class="meEmpfCb" data-art="${e.art}" data-id="${e.employeeId ?? ''}" data-uid="${e.userId ?? ''}" ${e.email ? 'checked' : 'disabled'}></td>
                    <td style="padding:3px 6px;font-weight:600;color:#3f3f3f;white-space:nowrap">${esc(e.name)}${e.art === 'BENUTZER' ? ' <span style="font-weight:500;font-size:11px;color:#2563eb">OneCrew</span>' : ''}</td>
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
    const datei = document.getElementById('meAnhang')?.files?.[0] || null;
    const gewaehlt = Array.from(document.querySelectorAll('.meEmpfCb')).filter(cb => cb.checked);
    const maIds   = gewaehlt.filter(cb => cb.dataset.art !== 'BENUTZER').map(cb => cb.dataset.id).filter(Boolean);
    const userIds = gewaehlt.filter(cb => cb.dataset.art === 'BENUTZER').map(cb => cb.dataset.uid).filter(Boolean);
    const info = document.getElementById('meSendInfo');

    if (!betreff) { showToast('Bitte einen Betreff eingeben.', 'error'); return; }
    // Text ODER Anhang genügt (Walter 01.09.2026) — «nur Betreff und ein
    // Dokument» ist ein gültiger Versand.
    if (!text && !datei) {
        showToast('Bitte einen Nachrichtentext eingeben oder ein Dokument anhängen.', 'error'); return;
    }
    if (!maIds.length && !userIds.length) {
        showToast('Bitte zuerst Empfänger laden und auswählen.', 'error'); return;
    }

    const anzahl = maIds.length + userIds.length;
    const teileWer = [];
    if (maIds.length) teileWer.push(`${maIds.length} Mitarbeitende`);
    if (userIds.length) teileWer.push(`${userIds.length} OneCrew-Benutzer`);
    const ok = await liquidConfirm(
        `E-Mail «${betreff}» jetzt an ${teileWer.join(' und ')} senden?`
        + (datei ? `\n\nAnhang: ${datei.name} (${Math.round(datei.size / 1024)} KB)` : '')
        + '\n\nJede Person erhält eine eigene E-Mail (Adressen bleiben privat).',
        { title: 'Gruppen-E-Mail', yesLabel: `Ja, an ${anzahl} senden`, noLabel: 'Abbrechen' });
    if (!ok) return;

    if (info) { info.textContent = 'Versand läuft…'; info.style.color = '#64748b'; }
    try {
        // multipart/form-data wegen des Anhangs. WICHTIG: Content-Type NICHT
        // selbst setzen — der Browser muss die Grenzmarkierung ergänzen.
        const fd = new FormData();
        fd.append('betreff', betreff);
        fd.append('text', text);
        fd.append('employeeIds', maIds.join(','));
        fd.append('userIds', userIds.join(','));
        if (datei) fd.append('anhang', datei, datei.name);
        // Selektion im Klartext fürs Protokoll — aus blossen Zahlen liesse
        // sich später nicht mehr sagen, wer gemeint war.
        const brSel = document.getElementById('meBranch');
        fd.append('filialeText', brSel?.selectedOptions?.[0]?.text || 'Alle Filialen');
        // Ohne MA in der Auswahl wäre «alle» im Protokoll irreführend.
        fd.append('modelleText', maIds.length ? (_meModelle() || 'alle') : '— nur Benutzer');
        fd.append('funktionenText', _meFunktionenText());
        fd.append('mitBenutzern', document.getElementById('meBenutzer')?.checked ? 'true' : 'false');

        const kopf = ah();
        delete kopf['Content-Type'];

        const r = await fetch('/api/ma-email/senden', { method: 'POST', headers: kopf, body: fd });
        const j = await r.json();
        if (!r.ok) {
            if (info) { info.textContent = 'Fehler: ' + (j?.message || j?.error || ('HTTP ' + r.status)); info.style.color = '#b91c1c'; }
            return;
        }
        const teile = [`${j.gesendet} gesendet`];
        if ((j.fehlgeschlagen || []).length) teile.push(`${j.fehlgeschlagen.length} fehlgeschlagen (${j.fehlgeschlagen.map(f => f.name).join(', ')})`);
        if ((j.ohneEmail || []).length) teile.push(`${j.ohneEmail.length} ohne E-Mail übersprungen`);
        if ((j.uebersprungen || []).length) teile.push(`${j.uebersprungen.length} doppelte Adresse(n) nur einmal angeschrieben`);
        if (j.anhang) teile.push(`Anhang: ${j.anhang}`);
        if (info) { info.textContent = '✓ ' + teile.join(' · '); info.style.color = (j.fehlgeschlagen || []).length ? '#9a3412' : '#166534'; }
        showToast(`E-Mail an ${j.gesendet} Empfänger gesendet.`, 'success');
        meLadeLog();
    } catch (e) {
        if (info) { info.textContent = 'Verbindungsfehler: ' + e.message; info.style.color = '#b91c1c'; }
    }
}

// Funktionen als lesbarer Text fürs Protokoll (Codes sagen später niemandem
// etwas). Alle angehakt = «alle».
function _meFunktionenText() {
    const alle = Array.from(document.querySelectorAll('.meFunkCb'));
    if (!alle.length) return 'alle';
    const gewaehlt = alle.filter(cb => cb.checked);
    if (gewaehlt.length === alle.length) return 'alle';
    return gewaehlt.map(cb => cb.parentElement.textContent.trim()).join(', ');
}

// ── Versandprotokoll (Walter-Vorgabe 01.09.2026) ──────────────────────
// Ein Eintrag pro Versand. Zeigt vor allem, ob er SCHARF rausging — ohne
// diese Angabe steht dort «an 200 gesendet», obwohl alles an die
// Test-Adresse ging.
async function meLadeLog() {
    const box = document.getElementById('meLog');
    if (!box) return;
    try {
        const r = await fetch('/api/ma-email/log?limit=25', { headers: ah() });
        if (!r.ok) { box.innerHTML = ''; return; }
        const j = await r.json();
        if (!j.length) {
            box.innerHTML = '<div style="font-size:12.5px;color:#8b8b8b">Noch keine Gruppen-E-Mail versendet.</div>';
            return;
        }
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        box.innerHTML = `
            <div style="overflow-x:auto">
            <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                <thead><tr style="background:rgba(255,255,255,0.55)">
                    <th style="text-align:left;padding:6px 8px;color:#8b8b8b;font-weight:700;white-space:nowrap">Wann</th>
                    <th style="text-align:left;padding:6px 8px;color:#8b8b8b;font-weight:700">Betreff</th>
                    <th style="text-align:left;padding:6px 8px;color:#8b8b8b;font-weight:700">An wen</th>
                    <th style="text-align:right;padding:6px 8px;color:#8b8b8b;font-weight:700;white-space:nowrap">Gesendet</th>
                    <th style="text-align:left;padding:6px 8px;color:#8b8b8b;font-weight:700">Von</th>
                </tr></thead>
                <tbody>${j.map(l => {
                    const wann = new Date(l.gesendetAm).toLocaleString('de-CH',
                        { day:'2-digit', month:'2-digit', year:'numeric', hour:'2-digit', minute:'2-digit' });
                    const gruppe = [l.filiale, l.modelle, l.funktionen]
                        .filter(x => x && x !== 'alle').join(' · ')
                        || 'alle Mitarbeitenden';
                    const zusatz = [];
                    if (l.mitBenutzern) zusatz.push('+ OneCrew-Benutzer');
                    if (l.anhangName) zusatz.push('📎 ' + esc(l.anhangName));
                    if (!l.mitText) zusatz.push('ohne Text');
                    const probleme = [];
                    // «fehlgeschlagen» ist anklickbar: darunter klappt die
                    // Liste der Adressen mit dem Grund auf (Walter 01.09.2026).
                    // Die übrigen Zahlen bleiben stumm — bei «doppelt» und
                    // «ohne E-Mail» gibt es nichts nachzulesen.
                    if (l.anzahlFehlgeschlagen) probleme.push(
                        `<a href="#" onclick="meLogDetails(event, ${l.id})"
                            style="color:#9a3412;text-decoration:underline;cursor:pointer"
                            title="Zeigt, welche Adressen nicht erreicht wurden und warum"
                         >${l.anzahlFehlgeschlagen} fehlgeschlagen</a>`);
                    // Später über die Wiedervorlage doch noch angekommen
                    // (Walter 01.09.2026). «5 fehlgeschlagen» bleibt stehen —
                    // so war es in diesem Lauf. Ob am Ende jeder die Mail hat,
                    // beantwortet erst die Zeile daneben.
                    if (l.anzahlSpaeterZugestellt) probleme.push(
                        `<span style="color:#166534">${l.anzahlSpaeterZugestellt} später zugestellt</span>`);
                    if (l.anzahlDoppelt) probleme.push(`${l.anzahlDoppelt} doppelt`);
                    if (l.anzahlOhneEmail) probleme.push(`${l.anzahlOhneEmail} ohne E-Mail`);
                    return `<tr style="border-top:1px solid rgba(60,55,48,0.08)">
                        <td style="padding:6px 8px;white-space:nowrap;color:#646464">${wann}</td>
                        <td style="padding:6px 8px;font-weight:600;color:#3f3f3f">${esc(l.betreff)}
                            ${zusatz.length ? `<div style="font-weight:400;color:#8b8b8b;font-size:11.5px">${zusatz.join(' · ')}</div>` : ''}</td>
                        <td style="padding:6px 8px;color:#646464">${esc(gruppe)}</td>
                        <td style="padding:6px 8px;text-align:right;white-space:nowrap">
                            <b>${l.anzahlGesendet}</b>
                            ${l.scharf
                                ? '<span style="color:#166534;font-size:11px"> scharf</span>'
                                : '<span style="color:#9a3412;font-size:11px"> → Test-Adresse</span>'}
                            ${probleme.length ? `<div style="color:#9a3412;font-size:11px;font-weight:400">${probleme.join(' · ')}</div>` : ''}</td>
                        <td style="padding:6px 8px;color:#8b8b8b;white-space:nowrap">${esc(l.von || '–')}</td>
                    </tr>
                    <tr id="meLogDet${l.id}" style="display:none">
                        <td colspan="5" style="padding:0 8px 10px"></td>
                    </tr>`;
                }).join('')}</tbody>
            </table></div>`;
    } catch (e) { box.innerHTML = ''; }
}

// ── Fehlgeschlagene Empfänger eines Versands aufklappen ─────────────────
// (Walter-Vorgabe 01.09.2026: «haben wir hier details, was fehlgeschlagen hat»)
// Die Daten stehen im Mail-Protokoll, eine Zeile pro Empfänger. Verknüpft
// wird über gruppen_mail_log_id — nicht über Betreff und Zeitfenster, denn
// zwei Versände mit gleichem Betreff kurz nacheinander wären so nicht
// auseinanderzuhalten.
async function meLogDetails(ev, logId) {
    if (ev) ev.preventDefault();
    const zeile = document.getElementById('meLogDet' + logId);
    if (!zeile) return;
    const zelle = zeile.querySelector('td');

    // Zweiter Klick schliesst wieder.
    if (zeile.style.display !== 'none') { zeile.style.display = 'none'; return; }

    zeile.style.display = '';
    zelle.innerHTML = '<div style="font-size:12px;color:#8b8b8b;padding:6px 0">lädt …</div>';

    try {
        const r = await fetch('/api/ma-email/log/' + logId + '/details', { headers: ah() });
        if (!r.ok) { zelle.innerHTML = _meDetHinweis('Konnte nicht geladen werden.'); return; }
        const j = await r.json();
        const zeilen = j.zeilen || [];

        if (!zeilen.length) {
            zelle.innerHTML = _meDetHinweis(
                'Zu diesem Versand sind im Mail-Protokoll keine fehlgeschlagenen '
              + 'Empfänger auffindbar.');
            return;
        }

        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        zelle.innerHTML = `
          <div style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.12);
                      border-radius:8px;padding:10px 12px">
            <div style="font-size:11.5px;font-weight:700;color:#8b8b8b;text-transform:uppercase;
                        letter-spacing:.05em;margin-bottom:6px">Nicht zugestellt</div>
            ${j.hergeleitet ? `<div style="font-size:11.5px;color:#8b8b8b;margin:-2px 0 8px;line-height:1.45">
                 Versand von vor dem 01.09.2026 — die Zeilen sind aus dem Mail-Protokoll
                 hergeleitet (Zeitfenster zwischen diesem und dem vorherigen Versand).
               </div>` : ''}
            <table style="width:100%;border-collapse:collapse;font-size:12px">
              ${zeilen.map(d => `
                <tr style="border-top:1px solid rgba(60,55,48,0.08)">
                  <td style="padding:5px 8px 5px 0;color:#3f3f3f;white-space:nowrap">
                    ${d.maName ? esc(d.maName) : '<span style="color:#8b8b8b">kein MA</span>'}
                    ${d.maNummer ? `<span style="color:#8b8b8b"> · ${esc(d.maNummer)}</span>` : ''}
                  </td>
                  <td style="padding:5px 8px;color:#646464">${esc(d.toEmail || '–')}</td>
                  <td style="padding:5px 0;color:${d.spaeterZugestellt ? '#166534' : (d.wiederholungAm ? '#92400e' : '#9a3412')}">
                    ${d.spaeterZugestellt
                        ? 'später über die Wiedervorlage zugestellt'
                        : esc(d.error || 'Grund nicht protokolliert')}
                    ${(!d.spaeterZugestellt && d.wiederholungAm)
                        ? `<div style="color:#92400e;font-size:11.5px">⏳ Wiederholung läuft — nächster Versuch um ${
                              new Date(d.wiederholungAm).toLocaleTimeString('de-CH',
                                  { hour:'2-digit', minute:'2-digit' })
                           }. Bitte nicht von Hand nachfassen, sonst kommt die Mail doppelt an.</div>`
                        : ''}
                  </td>
                </tr>`).join('')}
            </table>
          </div>`;
    } catch (e) {
        zelle.innerHTML = _meDetHinweis('Verbindungsfehler.');
    }
}

function _meDetHinweis(text) {
    return '<div style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.12);'
         + 'border-radius:8px;padding:10px 12px;font-size:12px;color:#8b8b8b">'
         + String(text).replace(/&/g, '&amp;').replace(/</g, '&lt;') + '</div>';
}
