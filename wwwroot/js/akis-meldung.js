// ══════════════════════════════════════════════════════════════════════
//  AKIS AN-/ABMELDUNG (HR-Hub → Behörden-Korrespondenz, Walter 06.08.2026)
//  GastroSocial-Ausgleichskasse: OneCrew erzeugt die AKISnet-Upload-Excel
//  aus den Ein-/Austritten der Filiale (Original-Vorlagen), hochladen
//  bleibt manuell im Portal (kein API/ELM). MA ohne AHV-Nummer werden
//  separat gelistet — für die braucht es zuerst die AHV-Anmeldung 318.260.
// ══════════════════════════════════════════════════════════════════════

const AKIS_PORTAL_URL = 'https://www.akisnet.ch/ak046/'; // GastroSocial (AK 046)

// Keine Datumswahl (Walter 13.10.2026): Anmeldungen inkl. künftiger Eintritte.
// Abmeldungen erst am Tag nach dem Austritt, und nur mit «Kündigung per»
// (Walter 20.10.2026 — befristete Verträge haben oft trotzdem ein Austrittsdatum).
function akisInit() {
    akisRefresh();
}

function _akisParams() {
    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    if (!cid) { showToast('Bitte oben links eine Filiale wählen.', 'error'); return null; }
    return `companyProfileId=${cid}`;
}

async function akisRefresh() {
    const el = document.getElementById('akisPreview');
    if (!el) return;
    const p = _akisParams();
    if (!p) { el.innerHTML = ''; return; }
    el.innerHTML = '<div style="color:#8b8b8b;padding:14px;font-size:12.5px">Wird geladen…</div>';
    try {
        const res = await fetch('/api/akis-export/preview?' + p, { headers: ah(), cache: 'no-store' });
        if (!res.ok) { el.innerHTML = '<div style="color:#991b1b;padding:14px;font-size:12.5px">Laden fehlgeschlagen.</div>'; return; }
        _akisDaten = await res.json();
        _akisRender(_akisDaten);
    } catch (_) {
        el.innerHTML = '<div style="color:#991b1b;padding:14px;font-size:12.5px">Verbindungsfehler.</div>';
    }
}

function _akisRender(d) {
    const el = document.getElementById('akisPreview');
    if (!el) return;
    const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
    const status = (r, art) => r.gemeldet
        ? `<span title="${r.gemeldet.quelle === 'ABGLEICH' ? 'aus dem AHV-Abgleich übernommen' : 'im AKISnet gemeldet'}${r.gemeldet.von ? ' · ' + esc(r.gemeldet.von) : ''}"
                 style="color:#166534;font-weight:600;white-space:nowrap">✓ gemeldet ${esc(r.gemeldet.am)}</span>
           <button class="dok-menu-btn" style="min-width:auto;padding:1px 7px;font-size:11px;margin-left:4px" title="Rückgängig"
                   onclick="akisMeldungZurueck(${r.gemeldet.id})">↩</button>`
        : `<button class="akis-gemeldet-btn" onclick="akisGemeldet('${art}', [${r.employeeId}], ['${r.datumIso}'], this)">✓ gemeldet</button>`;
    // Häkchen nur bei offenen Zeilen mit AHV-Nummer (nur die gehen in die Excel).
    const haken = (r, art) => r.gemeldet ? '' : r.ahv
        ? `<input type="checkbox" class="akis-haken" ${_akisGewaehlt(art, r) ? 'checked' : ''}
                  title="Melden — in die Excel und «Im AKISnet gemeldet» aufnehmen"
                  onchange="akisHaken('${art}', '${_akisKey(r)}', this.checked)">`
        : `<input type="checkbox" class="akis-haken" disabled title="Ohne AHV-Nummer nicht in der Excel">`;
    const tabelle = (rows, art) => `<table style="width:100%;border-collapse:collapse;font-size:12.5px">
            ${rows.map(r => `<tr style="border-bottom:1px solid rgba(60,55,48,0.08)">
                <td style="padding:5px 4px 5px 8px;width:22px">${haken(r, art)}</td>
                <td style="padding:5px 8px;font-variant-numeric:tabular-nums">${esc(r.ahv) || '<span style="color:#b45309">— fehlt —</span>'}</td>
                <td style="padding:5px 8px;font-weight:600"><a href="#" class="akis-ma-link" title="Mitarbeiter öffnen — mit «Zurück» oben rechts wieder hierher"
                    onclick="akisMaOeffnen(${r.employeeId});return false">${esc(r.vorname)} ${esc(r.name)}</a></td>
                <td style="padding:5px 8px">${esc(r.gebDat) || ''}</td>
                <td style="padding:5px 8px">${esc(r.geschlecht) || ''}</td>
                <td style="padding:5px 8px;font-weight:600">${esc(r.datum)}</td>
                ${art === 'ANMELDUNG' ? `<td style="padding:5px 8px">${esc(r.sprache)}</td>` : ''}
                <td style="padding:5px 8px;text-align:right">${status(r, art)}</td>
            </tr>`).join('')}
           </table>`;
    // Offene oben; bereits Gemeldete aufklappbar darunter.
    const tbl = (rows, art) => {
        const offen = rows.filter(r => !r.gemeldet), erledigt = rows.filter(r => r.gemeldet);
        return (offen.length ? tabelle(offen, art)
                : '<div style="color:#166534;padding:8px 0 10px;font-size:12.5px">✓ nichts offen</div>')
            + (erledigt.length ? `<details style="margin:6px 0 4px"><summary style="cursor:pointer;font-size:12px;color:#8b8b8b">✓ ${erledigt.length} bereits gemeldet</summary>${tabelle(erledigt, art)}</details>` : '');
    };
    const alleKnopf = (rows, art) => {
        const offen = rows.filter(r => !r.gemeldet && r.ahv);
        const n = offen.filter(r => _akisGewaehlt(art, r)).length;
        return offen.length
            ? `<button class="akis-gemeldet-btn alle" onclick="akisAlleGemeldet('${art}', this)" ${n ? '' : 'disabled'}>✓ Im AKISnet gemeldet (${n})</button>`
            : (rows.length ? '<span style="font-size:11.5px;color:#166534;font-weight:600">✓ alle gemeldet</span>' : '');
    };
    const alleHaken = (rows, art) => {
        const offen = rows.filter(r => !r.gemeldet && r.ahv);
        if (!offen.length) return '';
        const n = offen.filter(r => _akisGewaehlt(art, r)).length;
        return `<input type="checkbox" class="akis-haken" title="Alle an-/abwählen" ${n === offen.length ? 'checked' : ''}
                       ${n && n < offen.length ? 'data-teil="1"' : ''} onchange="akisAlleHaken('${art}', this.checked)">`;
    };
    const kopf = (titel, rows, art, zusatz) => `
        <div style="display:flex;align-items:center;gap:10px;margin:18px 0 4px;padding-left:8px">
            ${alleHaken(rows, art)}
            <div style="font-size:13px;font-weight:700;color:#3f3f3f">${titel} — offen ${rows.filter(r => !r.gemeldet).length}${zusatz || ''}</div>
            <span style="flex:1"></span>${alleKnopf(rows, art)}
        </div>`;
    const warn = (rows, was) => rows.length
        ? `<div style="background:#fef2f2;border:1px solid #fecaca;border-radius:8px;padding:8px 12px;margin:6px 0 12px;font-size:12px;color:#991b1b">
            ⚠ ${rows.length} ${was} ohne AHV-Nummer — zuerst per «AHV-Anmeldung (Versicherungsausweis)» melden, sie fehlen in der Excel:
            ${rows.map(r => `${esc(r.vorname)} ${esc(r.name)}`).join(', ')}</div>`
        : '';
    el.innerHTML = `
        ${kopf('Anmeldungen', d.anmeldungen.concat(d.anmeldungenOhneAhv || []), 'ANMELDUNG')}
        ${warn((d.anmeldungenOhneAhv || []).filter(r => !r.gemeldet), 'Eintritt(e)')}
        ${tbl(d.anmeldungen.concat(d.anmeldungenOhneAhv || []), 'ANMELDUNG')}
        ${kopf('Abmeldungen', d.abmeldungen.concat(d.abmeldungenOhneAhv || []), 'ABMELDUNG', ' <span style="font-weight:400;color:#8b8b8b;font-size:11.5px">— erst nach dem Austritt, nur mit Kündigung per</span>')}
        ${warn((d.abmeldungenOhneAhv || []).filter(r => !r.gemeldet), 'Austritt(e)')}
        ${tbl(d.abmeldungen.concat(d.abmeldungenOhneAhv || []), 'ABMELDUNG')}
        ${(d.befristetOffen || []).length ? `
        <div style="background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:10px 12px;margin-top:14px;font-size:12px;color:#92400e">
            <b>${d.befristetOffen.length} befristete(r) Vertrag/Verträge enden bald (oder endeten kürzlich) ohne erfassten Austritt</b> — verlängern oder Austritt erfassen (erst dann erscheinen sie als Abmeldung):<br>
            ${d.befristetOffen.map(r => `${esc(r.vorname)} ${esc(r.name)} (per ${esc(r.datum)})`).join(' · ')}
        </div>` : ''}`;
    el.querySelectorAll('input[data-teil]').forEach(cb => { cb.indeterminate = true; });
    _akisKnoepfe();
}

// Gemeldet merken (Walter 13.10.2026): nach dem Hochladen im AKISnet hier
// bestätigen — sonst bleibt das To-do «GastroSocial-Anmeldung/-Abmeldung fehlt».
let _akisDaten = null;

// Auswahl per Häkchen (Walter 14.10.2026): offene Zeilen sind angekreuzt, gemerkt
// werden nur die abgewählten — so sind neu dazukommende Zeilen automatisch dabei.
const _akisAbgewaehlt = new Set();
const _akisKey = (r) => `${r.employeeId}_${r.datumIso}`;
const _akisGewaehlt = (art, r) => !_akisAbgewaehlt.has(art + '|' + _akisKey(r));

/** Offene Zeilen mit AHV-Nummer, die angekreuzt sind (= Inhalt der Excel). */
function _akisAusgewaehlt(art) {
    const rows = (art === 'ANMELDUNG' ? _akisDaten?.anmeldungen : _akisDaten?.abmeldungen) || [];
    return rows.filter(r => !r.gemeldet && r.ahv && _akisGewaehlt(art, r));
}

function akisHaken(art, key, an) {
    if (an) _akisAbgewaehlt.delete(art + '|' + key); else _akisAbgewaehlt.add(art + '|' + key);
    akisRefreshAnsicht();
}

function akisAlleHaken(art, an) {
    const rows = (art === 'ANMELDUNG' ? _akisDaten?.anmeldungen : _akisDaten?.abmeldungen) || [];
    rows.filter(r => !r.gemeldet && r.ahv).forEach(r => {
        if (an) _akisAbgewaehlt.delete(art + '|' + _akisKey(r)); else _akisAbgewaehlt.add(art + '|' + _akisKey(r));
    });
    akisRefreshAnsicht();
}

/** Neu zeichnen ohne Server-Abruf (nach Häkchen-Änderung). */
function akisRefreshAnsicht() {
    if (_akisDaten) _akisRender(_akisDaten);
}

function _akisKnoepfe() {
    const an = _akisAusgewaehlt('ANMELDUNG').length, ab = _akisAusgewaehlt('ABMELDUNG').length;
    const bAn = document.getElementById('akisBtnAn'), bAb = document.getElementById('akisBtnAb');
    if (bAn) bAn.textContent = `⬇ Anmelde-Excel (${an})`;
    if (bAb) bAb.textContent = `⬇ Abmelde-Excel (${ab})`;
}

// Sprung zum MA (Walter 14.10.2026): «Zurück» oben rechts führt wieder hierher.
function akisMaOeffnen(empId) {
    window._ruecksprung = { seite: 'mitarbeiter', ziel: 'akis-meldung', vorher: window._prevPageName };
    dashOpenEmployee(empId);
}

async function akisGemeldet(art, empIds, daten, btn) {
    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    if (!cid) { showToast('Bitte oben links eine Filiale wählen.', 'error'); return; }
    if (btn) btn.disabled = true;
    try {
        const res = await fetch('/api/gastrosocial-meldung/gemeldet', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                companyProfileId: cid, art,
                eintraege: empIds.map((id, i) => ({ employeeId: id, datum: daten[i] })),
            }),
        });
        if (!res.ok) { showToast('Speichern fehlgeschlagen (HTTP ' + res.status + ').', 'error'); return; }
        const r = await res.json();
        showToast(`${r.neu} als gemeldet vermerkt.`, 'success');
        await akisRefresh();
    } catch (_) {
        showToast('Verbindungsfehler.', 'error');
    } finally {
        if (btn) btn.disabled = false;
    }
}

/** Alle angekreuzten offenen Zeilen MIT AHV-Nummer (= Inhalt der Excel) einer Liste. */
async function akisAlleGemeldet(art, btn) {
    const rows = _akisAusgewaehlt(art);
    if (!rows.length) return;
    const was = art === 'ANMELDUNG' ? 'Anmeldungen' : 'Abmeldungen';
    const ok = await liquidConfirm(`${rows.length} ${was} als «im AKISnet gemeldet» vermerken?\n\n${rows.map(r => `${r.vorname} ${r.name} (${r.datum})`).join('\n')}`,
        { title: 'Im AKISnet gemeldet', yesLabel: 'Ja, gemeldet', noLabel: 'Abbrechen' });
    if (!ok) return;
    await akisGemeldet(art, rows.map(r => r.employeeId), rows.map(r => r.datumIso), btn);
}

async function akisMeldungZurueck(id) {
    const alle = [].concat(_akisDaten?.anmeldungen || [], _akisDaten?.anmeldungenOhneAhv || [],
                           _akisDaten?.abmeldungen || [], _akisDaten?.abmeldungenOhneAhv || []);
    const r = alle.find(x => x.gemeldet?.id === id);
    const ok = await liquidConfirm(`Vermerk «gemeldet» bei ${r ? r.vorname + ' ' + r.name : 'diesem MA'} entfernen?`,
        { title: 'Rückgängig', yesLabel: 'Entfernen', noLabel: 'Abbrechen' });
    if (!ok) return;
    try {
        const res = await fetch('/api/gastrosocial-meldung/' + id, { method: 'DELETE', headers: ah() });
        if (!res.ok) { showToast('Entfernen fehlgeschlagen (HTTP ' + res.status + ').', 'error'); return; }
        await akisRefresh();
    } catch (_) {
        showToast('Verbindungsfehler.', 'error');
    }
}

// Erzeugte Dateien im Speicher — als ziehbare Chips angeboten (Walter
// 06.08.2026): direkt aus OneCrew in den AKISnet-Tab bzw. auf den Desktop
// ziehen, ohne Umweg über den Finder. Speichern-Knopf bleibt als Option.
const _akisBlobs = {};   // typ → { blob, name, url }

async function akisDownload(typ, btn) {
    const p = _akisParams();
    if (!p) return;
    const gewaehlt = _akisDaten ? _akisAusgewaehlt(typ === 'anmeldung' ? 'ANMELDUNG' : 'ABMELDUNG') : null;
    if (gewaehlt && !gewaehlt.length) {
        showToast(`Keine ${typ === 'anmeldung' ? 'Anmeldung' : 'Abmeldung'} angekreuzt (mit AHV-Nummer) — nichts zu exportieren.`, 'info');
        return;
    }
    const auswahl = gewaehlt ? '&auswahl=' + encodeURIComponent(gewaehlt.map(_akisKey).join(',')) : '';
    if (btn) btn.disabled = true;
    try {
        const res = await fetch(`/api/akis-export/${typ}?` + p + auswahl, { headers: ah() });
        if (!res.ok) { showToast('Export fehlgeschlagen (HTTP ' + res.status + ').', 'error'); return; }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        const name = cdFilename(cd, `${typ}Mitarbeitende.xlsx`);
        if (_akisBlobs[typ]?.url) URL.revokeObjectURL(_akisBlobs[typ].url);
        // data:-Adresse, nicht blob: — nur die kann Chrome beim Fallenlassen in ein
        // anderes Fenster (AKISnet) als echte Datei nachladen.
        const dataUrl = await _akisAlsDataUrl(blob);
        _akisBlobs[typ] = { blob, name, url: URL.createObjectURL(blob), dataUrl };
        akisRenderFiles();
        showToast('Excel bereit — Chip auf den Schreibtisch ziehen, dann im AKISnet über «…» wählen.', 'success');
    } catch (_) {
        showToast('Verbindungsfehler beim Export.', 'error');
    } finally {
        if (btn) btn.disabled = false;
    }
}

function akisRenderFiles() {
    const el = document.getElementById('akisFiles');
    if (!el) return;
    const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
    el.innerHTML = Object.entries(_akisBlobs).map(([typ, f]) => `
        <div draggable="true" ondragstart="akisDragStart(event, '${typ}')"
             title="Auf den Schreibtisch ziehen. Im AKISnet die Datei über «…» auswählen — der Browser gibt eine hier erzeugte Datei nicht an das andere Fenster weiter."
             style="display:flex;align-items:center;gap:10px;background:rgba(255,255,255,0.55);border:1px dashed rgba(60,55,48,0.35);border-radius:12px;padding:10px 14px;cursor:grab;user-select:none">
            <span style="font-size:20px">📄</span>
            <span style="font-size:12.5px;font-weight:600;color:#3f3f3f">${esc(f.name)}<br>
                <span style="font-weight:400;color:#8b8b8b;font-size:11px">auf den Schreibtisch ziehen, dann im AKISnet über «…»</span></span>
            <button onclick="saveBlobAsk(_akisBlobs['${typ}'].blob, _akisBlobs['${typ}'].name)"
                    title="Speichern unter…"
                    style="background:transparent;border:1px solid rgba(60,55,48,0.18);border-radius:8px;padding:4px 9px;font-size:12px;cursor:pointer;color:#646464">💾</button>
        </div>`).join('');
}

function _akisAlsDataUrl(blob) {
    const mime = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
    const datei = blob.type === mime ? blob : new Blob([blob], { type: mime });
    return new Promise((resolve, reject) => {
        const r = new FileReader();
        r.onload = () => resolve(String(r.result || ''));
        r.onerror = () => reject(r.error);
        r.readAsDataURL(datei);
    });
}

function akisDragStart(e, typ) {
    const f = _akisBlobs[typ];
    if (!f) return;
    const mime = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
    const name = String(f.name || 'Mitarbeitende.xlsx').replace(/[:\\/]/g, '_');
    e.dataTransfer.effectAllowed = 'copy';
    try {
        e.dataTransfer.items.add(new File([f.blob], name, { type: mime }));
    } catch (_) { /* ältere Browser */ }
    // Chrome: DownloadURL macht aus dem Zug eine echte Datei, auch im anderen Fenster.
    // blob:-Adressen gelten nur in OneCrew und kommen im AKISnet leer an — darum data:.
    const url = f.dataUrl || f.url;
    if (url) {
        try { e.dataTransfer.setData('DownloadURL', `${mime}:${name}:${url}`); } catch (_) {}
    }
}

function akisOpenPortal() {
    window.open(AKIS_PORTAL_URL, '_blank', 'noopener');
}
