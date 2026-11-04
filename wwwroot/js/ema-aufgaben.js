// EMA BVG und Quellensteuer (Walter 30.10.2026).
// Die Zeilen kommen aus den erfassten Daten. Freigabe merkt «zur Übermittlung».
// Die ELM-Datei selbst ist Etappe E7.

function emaInit() { emaRefresh(); emaManInit(); }

async function emaRefresh() {
    const el = document.getElementById('emaPreview');
    if (!el) return;
    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    if (!cid) { el.innerHTML = '<div style="color:#8b8b8b;padding:8px;font-size:12.5px">Bitte oben links eine Filiale wählen.</div>'; return; }
    el.innerHTML = '<div style="color:#8b8b8b;padding:14px;font-size:12.5px">Wird geladen…</div>';
    try {
        const res = await fetch('/api/ema-aufgabe/preview?companyProfileId=' + cid, { headers: ah(), cache: 'no-store' });
        if (!res.ok) { el.innerHTML = '<div style="color:#991b1b;padding:14px;font-size:12.5px">Laden fehlgeschlagen.</div>'; return; }
        _emaDaten = await res.json();
        _emaRender(_emaDaten);
    } catch (_) {
        el.innerHTML = '<div style="color:#991b1b;padding:14px;font-size:12.5px">Verbindungsfehler.</div>';
    }
}

function _emaRender(d) {
    const el = document.getElementById('emaPreview');
    if (!el) return;
    const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
    const block = (titel, rows, domaene) => {
        const offen = (rows || []).filter(r => !r.gemeldet);
        const erledigt = (rows || []).filter(r => r.gemeldet);
        const zeile = (r) => `<tr style="border-bottom:1px solid rgba(60,55,48,0.08)">
            <td style="padding:6px 8px;font-weight:600"><a href="#" style="color:inherit;text-decoration:none"
                onclick="emaMaOeffnen(${r.employeeId});return false">${esc(r.vorname)} ${esc(r.name)}</a></td>
            <td style="padding:6px 8px">${esc(r.text)}</td>
            <td style="padding:6px 8px;color:#646464">${esc(r.zusatz)}</td>
            <td style="padding:6px 8px;font-weight:600;white-space:nowrap">${esc(r.datum)}</td>
            <td style="padding:6px 8px;text-align:right;white-space:nowrap">${r.gemeldet
                ? `<span style="color:#166534;font-weight:600">✓ freigegeben ${esc(r.gemeldet.am)}</span>
                   <button class="dok-menu-btn" style="min-width:auto;padding:1px 7px;font-size:11px;margin-left:4px" title="Rückgängig"
                           onclick="emaZurueck(${r.gemeldet.id})">↩</button>`
                : `<button class="akis-gemeldet-btn" onclick="emaFreigeben('${domaene}', ${r.employeeId}, '${esc(r.grund)}', '${esc(r.datumIso)}', this)">Zur Übermittlung freigegeben</button>`
            }</td>
        </tr>`;
        const kopf = `<div style="font-size:13px;font-weight:700;margin:16px 0 6px">${titel}</div>`;
        if (!offen.length && !erledigt.length)
            return kopf + '<div style="color:#8b8b8b;font-size:12.5px;padding:4px 0 8px">Keine Zeile in dieser Filiale.</div>';
        const tabelle = (list) => `<table style="width:100%;border-collapse:collapse;font-size:12.5px">${list.map(zeile).join('')}</table>`;
        return kopf
            + (offen.length ? tabelle(offen) : '<div style="color:#166534;padding:4px 0 8px;font-size:12.5px">✓ nichts offen</div>')
            + (erledigt.length ? `<details style="margin:6px 0 4px"><summary style="cursor:pointer;font-size:12px;color:#8b8b8b">✓ ${erledigt.length} bereits freigegeben</summary>${tabelle(erledigt)}</details>` : '');
    };
    el.innerHTML = block('BVG', d.bvg, 'BVG') + block('Quellensteuer', d.qst, 'QST');
}

function emaMaOeffnen(empId) {
    window._ruecksprung = { seite: 'mitarbeiter', ziel: 'ema-aufgaben', vorher: window._prevPageName };
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
            + '<button type="button" class="btn btn-outline" style="margin-top:10px" onclick="emaRefBerichtLaden()">Bericht herunterladen</button>'
            + emaFragenHtml(d.monate.flatMap(m => m.fragen));
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

function emaFragenHtml(fragen) {
    _emaFragen = fragen;
    if (!fragen.length) return '';
    const zeilen = fragen.map((f, i) => `<tr id="emaFrage${i}"><td>${emaRefText(f.name)}</td>`
        + `<td>${f.art === 'Entry' ? 'Eintritt' : 'Austritt'}</td><td>${f.validAsOf.split('-').reverse().join('.')}</td>`
        + `<td style="text-align:right;white-space:nowrap"><button type="button" class="btn btn-outline" onclick="emaFrageAntwort(${i},'ja')">Ja</button> `
        + `<button type="button" class="btn btn-outline" onclick="emaFrageAntwort(${i},'nein')">Nein</button></td></tr>`).join('');
    return `<h4 style="margin:18px 0 6px">100 % arbeitsfähig? (${fragen.length} offen)</h4>`
        + '<p class="page-sub" style="margin:0 0 8px">Auch bei hängigen Arbeitsunfähigkeitsabklärungen mit Nein antworten. Nur bei Ja wird FullyFitForWork gemeldet.</p>'
        + '<button type="button" class="btn btn-outline" style="margin-bottom:8px" onclick="emaFragenAlleJa()">Alle mit Ja beantworten</button>'
        + `<table class="data-table" style="width:100%"><tbody>${zeilen}</tbody></table>`;
}

async function emaFrageSenden(f, antwort) {
    const res = await fetch('/api/elm/ema/manuell', { method: 'POST', headers: ah(), body: JSON.stringify({ employeeId: f.employeeId, monat: f.monat, art: 'Fit', grund: antwort, validAsOf: f.validAsOf }) });
    return res.ok;
}

async function emaFrageAntwort(i, antwort) {
    if (await emaFrageSenden(_emaFragen[i], antwort)) document.getElementById('emaFrage' + i)?.remove();
}

async function emaFragenAlleJa() {
    if (!await liquidConfirm(`Alle ${_emaFragen.length} Personen als 100 % arbeitsfähig erfassen?`)) return;
    for (let i = 0; i < _emaFragen.length; i++) await emaFrageAntwort(i, 'ja');
}

// Ereignisse, die RL 8.6.8 dem Anwender überlässt.
async function emaManInit() {
    const sel = document.getElementById('emaManMa');
    if (!sel || sel.options.length) return;
    try {
        const res = await fetch('/api/employees', { headers: ah(), cache: 'no-store' });
        const liste = res.ok ? await res.json() : [];
        liste.sort((a, b) => (a.firstName || '').localeCompare(b.firstName || ''));
        sel.innerHTML = '<option value="">Bitte wählen</option>' + liste.map(e => `<option value="${e.id}">${emaRefText((e.firstName || '') + ' ' + (e.lastName || ''))}</option>`).join('');
        sel.onchange = emaManListe;
    } catch (_) { /* Liste bleibt leer, Erfassen meldet es */ }
}

async function emaManListe() {
    const el = document.getElementById('emaManListe');
    const id = document.getElementById('emaManMa').value;
    if (!id) { el.innerHTML = ''; return; }
    const res = await fetch('/api/elm/ema/manuell?employeeId=' + id, { headers: ah(), cache: 'no-store' });
    const liste = (res.ok ? await res.json() : []).filter(m => m.art !== 'Fit');
    el.innerHTML = liste.map(m => `<div style="font-size:12.5px;padding:3px 0">${m.monat.slice(0, 7)} · ${emaRefText(m.grund)} `
        + `<button type="button" class="btn btn-outline" style="font-size:11px" onclick="emaManLoeschen(${m.id})">Entfernen</button></div>`).join('');
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

// EMA NotifyChanges und voraussichtliche Jahresmeldung senden. Nur auf ausdrückliche Bestätigung.
async function emaSenden(art) {
    const el = document.getElementById('emaSendErgebnis');
    const tag = document.getElementById('emaSendTag').value;
    if (!tag) { el.textContent = 'Bitte zuerst ein Datum wählen.'; return; }
    const jahr = Number(tag.slice(0, 4));
    const monat = art === 'ema' ? Number(tag.slice(5, 7)) : null;
    el.textContent = 'Vorschau wird erstellt…';
    try {
        const q = new URLSearchParams({ art, jahr });
        if (monat) q.set('monat', monat);
        const res = await fetch('/api/elm/meldungen/vorschau?' + q, { headers: ah(), cache: 'no-store' });
        const v = await res.json().catch(() => ({}));
        if (!res.ok) { el.textContent = v.message || 'Vorschau fehlgeschlagen.'; return; }
        const hinweise = [...(v.technisch || []), ...(v.fehler || []), ...(v.warnungen || [])];
        if (!v.empfaenger?.length) { el.textContent = hinweise.join(' · ') || 'Keine Meldung.'; return; }
        el.innerHTML = hinweise.map(emaRefText).join('<br>');
        if (v.sendenGesperrt) { el.textContent = v.sendenGesperrt; return; }
        const an = v.empfaenger.map(e => e.name || e.identification).join(', ');
        if (!await liquidConfirm(`${v.titel}\nAn: ${an}`, { title: 'Meldung an Swissdec senden?', yesLabel: 'Ja, senden', noLabel: 'Abbrechen' })) {
            el.textContent = 'Nicht gesendet.';
            return;
        }
        el.textContent = 'Wird gesendet…';
        const s = await fetch('/api/elm/meldungen/senden', { method: 'POST', headers: ah(), body: JSON.stringify({ art, jahr, monat }) });
        const d = await s.json().catch(() => ({}));
        el.textContent = d.meldung || d.message || 'Senden fehlgeschlagen.';
    } catch (_) {
        el.textContent = 'Verbindungsfehler.';
    }
}
