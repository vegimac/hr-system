// EMA BVG und Quellensteuer (Walter 30.10.2026), seit 05.11.2026 Reiter «EMA» im ELM Cockpit.
// Die Zeilen kommen aus den erfassten Daten. Freigabe merkt «zur Übermittlung».
// Gesendet wird unter Lohnmeldungen (ELM).

function emaInit() { emaRefresh(); emaManInit(); emaFragenInit(); }

async function emaRefresh() {
    const el = document.getElementById('emaPreview');
    if (!el) return;
    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    if (!cid) { el.innerHTML = '<div class="e2-ema-abschnitt e2-ema-still">Bitte oben links eine Filiale wählen.</div>'; return; }
    el.innerHTML = '<div class="e2-ema-abschnitt e2-ema-still">Wird geladen…</div>';
    try {
        const res = await fetch('/api/ema-aufgabe/preview?companyProfileId=' + cid, { headers: ah(), cache: 'no-store' });
        if (!res.ok) { el.innerHTML = '<div style="color:var(--oc-rot-text);padding:14px;font-size:12.5px">Laden fehlgeschlagen.</div>'; return; }
        _emaDaten = await res.json();
        _emaRender(_emaDaten);
    } catch (_) {
        el.innerHTML = '<div style="color:var(--oc-rot-text);padding:14px;font-size:12.5px">Verbindungsfehler.</div>';
    }
}

function _emaRender(d) {
    const el = document.getElementById('emaPreview');
    if (!el) return;
    const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
    const block = (titel, rows, domaene) => {
        const offen = (rows || []).filter(r => !r.gemeldet);
        const erledigt = (rows || []).filter(r => r.gemeldet);
        const zeile = (r) => `<tr>
            <td><a href="#" onclick="emaMaOeffnen(${r.employeeId});return false">${esc(r.vorname)} ${esc(r.name)}</a></td>
            <td>${esc(r.text)}</td>
            <td class="e2-ema-still">${esc(r.zusatz)}</td>
            <td class="e2-ema-still">${esc(r.datum)}</td>
            <td class="e2-ema-aktion">${r.gemeldet
                ? `<span class="e2-ema-ok">✓ freigegeben ${esc(r.gemeldet.am)}</span>
                   <button type="button" class="e2-pille e2-klein" title="Rückgängig" onclick="emaZurueck(${r.gemeldet.id})">↩</button>`
                : `<button type="button" class="e2-pille e2-klein" title="Zur Übermittlung freigeben" onclick="emaFreigeben('${domaene}', ${r.employeeId}, '${esc(r.grund)}', '${esc(r.datumIso)}', this)">Freigeben</button>`
            }</td>
        </tr>`;
        const stand = offen.length ? `<span class="e2-ema-still">${offen.length} offen</span>` : '<span class="e2-ema-ok">✓ nichts offen</span>';
        return `<div class="e2-ema-abschnitt"><div class="e2-ema-titel"><b>${titel}</b>${stand}</div>`
            + (offen.length ? `<table class="e2-ema-tabelle">${offen.map(zeile).join('')}</table>` : '')
            + (erledigt.length ? `<details><summary>${erledigt.length} bereits freigegeben</summary><table class="e2-ema-tabelle">${erledigt.map(zeile).join('')}</table></details>` : '')
            + '</div>';
    };
    el.innerHTML = block('BVG', d.bvg, 'BVG') + block('Quellensteuer', d.qst, 'QST');
}

function emaMaOeffnen(empId) {
    window._ruecksprung = { seite: 'mitarbeiter', ziel: 'elm', vorher: window._prevPageName };
    dashOpenEmployee(empId);
}

async function emaFreigeben(domaene, employeeId, grund, datum, btn) {
    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    if (!cid) { showToast('Bitte oben links eine Filiale wählen.', 'error'); return; }
    const ok = await liquidConfirm('Diese Zeile zur Übermittlung freigeben?',
        { title: 'Zur Übermittlung freigegeben', yesLabel: 'Freigeben', noLabel: 'Abbrechen' });
    if (!ok) return;
    if (btn) btn.disabled = true;
    try {
        const res = await fetch('/api/ema-aufgabe/freigeben', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyProfileId: cid, domaene, eintraege: [{ employeeId, grund, datum }] }),
        });
        if (!res.ok) { showToast('Speichern fehlgeschlagen (HTTP ' + res.status + ').', 'error'); return; }
        const r = await res.json();
        if (r.ungueltig) showToast('Diese Zeile gibt es so nicht mehr.', 'error');
        else showToast('Zur Übermittlung freigegeben.', 'success');
        await emaRefresh();
    } catch (_) {
        showToast('Verbindungsfehler.', 'error');
    } finally {
        if (btn) btn.disabled = false;
    }
}

async function emaZurueck(id) {
    const ok = await liquidConfirm('Freigabe entfernen?',
        { title: 'Rückgängig', yesLabel: 'Entfernen', noLabel: 'Abbrechen' });
    if (!ok) return;
    try {
        const res = await fetch('/api/ema-aufgabe/' + id, { method: 'DELETE', headers: ah() });
        if (!res.ok) { showToast('Entfernen fehlgeschlagen (HTTP ' + res.status + ').', 'error'); return; }
        await emaRefresh();
    } catch (_) {
        showToast('Verbindungsfehler.', 'error');
    }
}

// EMA-Meldung NotifyChanges gegen die Swissdec-Referenz (Walter 04.11.2026). Nur lesen, nichts senden.
let _emaRefBericht = '';

async function emaRefPruefen(input) {
    const el = document.getElementById('emaRefErgebnis');
    const fd = new FormData();
    for (const f of input.files) fd.append('dateien', f);
    input.value = '';
    el.innerHTML = '<div style="color:var(--oc-text-3);font-size:12.5px">Wird geprüft…</div>';
    try {
        const res = await fetch('/api/elm/ema/pruefen', { method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd });
        const d = await res.json().catch(() => ({}));
        if (!res.ok) { el.textContent = d.message || 'Prüfung fehlgeschlagen.'; return; }
        _emaRefBericht = d.bericht || '';
        const zeilen = d.monate.map(m => {
            const hinweise = [...m.xsdFehler.map(x => 'XSD: ' + x), ...m.warnungen];
            return `<tr><td>${m.monat}</td><td style="text-align:right">${m.ereignisse}</td>`
                + `<td style="text-align:right">${m.geprueft ?? '–'}</td><td style="text-align:right">${m.referenz ? (m.offen ?? 'keine Meldung') : 'keine Referenz'}</td>`
                + `<td style="font-size:11.5px">${hinweise.map(emaRefText).join('<br>')}</td></tr>`;
        }).join('');
        el.innerHTML = '<table class="data-table" style="width:100%"><thead><tr><th>Monat</th><th style="text-align:right">Ereignisse</th>'
            + '<th style="text-align:right">Felder</th><th style="text-align:right">Offen</th><th>Hinweise</th></tr></thead>'
            + `<tbody>${zeilen}</tbody></table>`
            + '<button type="button" class="e2-text" onclick="emaRefBerichtLaden()">Bericht herunterladen</button>';
        emaFragenZeigen(d.monate.flatMap(m => m.fragen));
    } catch (_) {
        el.textContent = 'Verbindungsfehler.';
    }
}

function emaRefText(t) {
    const d = document.createElement('div');
    d.textContent = t;
    return d.innerHTML;
}

function emaRefBerichtLaden() {
    saveBlobAsk(new Blob([_emaRefBericht], { type: 'text/markdown' }), 'ema-abgleich.md');
}

// 100-%-Arbeitsfähigkeit bei BVG-Eintritt und -Austritt (RL 8.6.8.2).
let _emaFragen = [];
let _emaFragenLauf = 0;

function emaFragenInit() {
    const m = document.getElementById('emaFrageMonat');
    const j = document.getElementById('emaFrageJahr');
    if (!m || !j) return;
    if (!m.options.length) {
        // Vormonat: der jüngste Monat, der gemeldet werden kann.
        const v = new Date(); v.setDate(1); v.setMonth(v.getMonth() - 1);
        const namen = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
        m.innerHTML = namen.map((n, i) => `<option value="${i + 1}">${n}</option>`).join('');
        m.value = String(v.getMonth() + 1);
        const heute = new Date().getFullYear();
        j.innerHTML = [heute, heute - 1, heute - 2].map(y => `<option value="${y}">${y}</option>`).join('');
        j.value = String(v.getFullYear());
    }
    emaFragenLaden();
}

async function emaFragenLaden() {
    const el = document.getElementById('emaFragen');
    if (!el) return;
    const lauf = ++_emaFragenLauf;
    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    const zeile = (t) => `<div class="e2-ema-abschnitt"><div class="e2-ema-titel"><b>100 % arbeitsfähig?</b><span class="e2-ema-still">${t}</span></div></div>`;
    if (!cid) { _emaFragen = []; el.innerHTML = zeile('Bitte oben links eine Filiale wählen.'); return; }
    el.innerHTML = zeile('Wird geladen…');
    const q = new URLSearchParams({ jahr: document.getElementById('emaFrageJahr').value, monat: document.getElementById('emaFrageMonat').value, companyProfileId: cid });
    try {
        const res = await fetch('/api/elm/ema/fragen?' + q, { headers: ah(), cache: 'no-store' });
        if (lauf !== _emaFragenLauf) return;
        if (!res.ok) { el.innerHTML = zeile('Laden fehlgeschlagen.'); return; }
        emaFragenZeigen(await res.json());
    } catch (_) {
        if (lauf === _emaFragenLauf) el.innerHTML = zeile('Verbindungsfehler.');
    }
}

function emaFragenZeigen(fragen) {
    _emaFragenLauf++;
    _emaFragen = fragen;
    const el = document.getElementById('emaFragen');
    if (!el) return;
    const hilfe = 'Solange eine Frage offen ist, wird die EMA-Meldung nicht gesendet. Auch bei hängigen Arbeitsunfähigkeitsabklärungen mit Nein antworten. Nur bei Ja wird FullyFitForWork gemeldet.';
    const kopf = (rechts) => `<div class="e2-ema-titel"><b>100 % arbeitsfähig? <i class="e2-info" title="${hilfe}">i</i></b>${rechts}</div>`;
    if (!fragen.length) { el.innerHTML = `<div class="e2-ema-abschnitt">${kopf('<span class="e2-ema-ok">✓ keine Frage offen</span>')}</div>`; return; }
    const zeilen = fragen.map((f, i) => `<tr><td>${emaRefText(f.name)}</td>`
        + `<td class="e2-ema-still">${f.art === 'Entry' ? 'Eintritt' : 'Austritt'}</td><td class="e2-ema-still">${f.validAsOf.split('-').reverse().join('.')}</td>`
        + `<td class="e2-ema-aktion"><button type="button" class="e2-pille e2-klein" onclick="emaFrageAntwort(${i},'ja')">Ja</button> `
        + `<button type="button" class="e2-pille e2-klein" onclick="emaFrageAntwort(${i},'nein')">Nein</button></td></tr>`).join('');
    el.innerHTML = '<div class="e2-ema-abschnitt">'
        + kopf(`<span class="e2-ema-rechts"><span class="e2-ema-warn">${fragen.length} offen, sperrt das Senden</span> <button type="button" class="e2-pille e2-klein" onclick="emaFragenAlleJa()">Alle mit Ja</button></span>`)
        + `<table class="e2-ema-tabelle">${zeilen}</table></div>`;
}

async function emaFrageSenden(f, antwort) {
    try {
        const res = await fetch('/api/elm/ema/manuell', { method: 'POST', headers: ah(), body: JSON.stringify({ employeeId: f.employeeId, monat: f.monat, art: 'Fit', grund: antwort, validAsOf: f.validAsOf }) });
        return res.ok;
    } catch (_) { return false; }
}

async function emaFrageAntwort(i, antwort) {
    const f = _emaFragen[i];
    if (!f) return;
    if (!await emaFrageSenden(f, antwort)) { showToast('Speichern fehlgeschlagen.', 'error'); return; }
    emaFragenZeigen(_emaFragen.filter(x => x !== f));
}

async function emaFragenAlleJa() {
    if (!await liquidConfirm(`Alle ${_emaFragen.length} Personen als 100 % arbeitsfähig erfassen?`)) return;
    const offen = [];
    for (const f of _emaFragen) if (!await emaFrageSenden(f, 'ja')) offen.push(f);
    if (offen.length) showToast(`${offen.length} Antworten konnten nicht gespeichert werden.`, 'error');
    emaFragenZeigen(offen);
}

// Ereignisse, die RL 8.6.8 dem Anwender überlässt.
let _emaManAlle = null;

// Personen der gewählten Filiale, auch ausgetretene: Das Ereignis kann zu einem Austritt gehören.
async function emaManInit() {
    const sel = document.getElementById('emaManMa');
    if (!sel) return;
    try {
        if (!_emaManAlle) {
            const res = await fetch('/api/employees', { headers: ah(), cache: 'no-store' });
            if (!res.ok) return;
            _emaManAlle = await res.json();
            _emaManAlle.sort((a, b) => (a.firstName || '').localeCompare(b.firstName || ''));
        }
        const cid = (typeof fixedCompanyProfileId !== 'undefined') ? Number(fixedCompanyProfileId) || 0 : 0;
        const inFiliale = (e) => !cid || !(e.employments || []).length
            || e.employments.some(v => v.companyProfileId === cid || v.companyProfileId == null);
        const vorher = sel.value;
        const liste = _emaManAlle.filter(inFiliale);
        sel.innerHTML = '<option value="">Bitte wählen</option>' + liste.map(e => `<option value="${e.id}">${emaRefText((e.firstName || '') + ' ' + (e.lastName || ''))}</option>`).join('');
        sel.value = liste.some(e => String(e.id) === vorher) ? vorher : '';
        sel.onchange = emaManListe;
        emaManListe();
    } catch (_) { /* Liste bleibt leer, Erfassen meldet es */ }
}

async function emaManListe() {
    const el = document.getElementById('emaManListe');
    const id = document.getElementById('emaManMa').value;
    if (!id) { el.innerHTML = ''; return; }
    const res = await fetch('/api/elm/ema/manuell?employeeId=' + id, { headers: ah(), cache: 'no-store' });
    const liste = (res.ok ? await res.json() : []).filter(m => m.art !== 'Fit');
    el.innerHTML = liste.map(m => `<div style="font-size:12.5px;padding:3px 0">${m.monat.slice(0, 7)} · ${emaRefText(m.grund)} `
        + `<button type="button" class="e2-text" onclick="emaManLoeschen(${m.id})">Entfernen</button></div>`).join('');
}

async function emaManSpeichern() {
    const id = Number(document.getElementById('emaManMa').value);
    const monat = document.getElementById('emaManMonat').value.slice(0, 7);
    const [art, grund] = document.getElementById('emaManGrund').value.split('|');
    const ab = document.getElementById('emaManAb').value || null;
    const el = document.getElementById('emaManListe');
    if (!id || !monat) { el.textContent = 'Bitte Person und Meldemonat wählen.'; return; }
    const res = await fetch('/api/elm/ema/manuell', { method: 'POST', headers: ah(), body: JSON.stringify({ employeeId: id, monat: monat + '-01', art, grund, validAsOf: ab }) });
    if (!res.ok) { const d = await res.json().catch(() => ({})); el.textContent = d.message || 'Erfassen fehlgeschlagen.'; return; }
    await emaManListe();
}

async function emaManLoeschen(id) {
    if (!await liquidConfirm('Dieses Ereignis entfernen?')) return;
    await fetch('/api/elm/ema/manuell/' + id, { method: 'DELETE', headers: ah() });
    await emaManListe();
}
