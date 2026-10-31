// EMA BVG und Quellensteuer (Walter 30.10.2026).
// Die Zeilen kommen aus den erfassten Daten. Freigabe merkt «zur Übermittlung».
// Die ELM-Datei selbst ist Etappe E7.

function emaInit() { emaRefresh(); }

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
