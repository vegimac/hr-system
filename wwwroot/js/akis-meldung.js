// ══════════════════════════════════════════════════════════════════════
//  AKIS AN-/ABMELDUNG (HR-Hub → Behörden-Korrespondenz, Walter 06.08.2026)
//  GastroSocial-Ausgleichskasse: OneCrew erzeugt die AKISnet-Upload-Excel
//  aus den Ein-/Austritten der Filiale (Original-Vorlagen), hochladen
//  bleibt manuell im Portal (kein API/ELM). MA ohne AHV-Nummer werden
//  separat gelistet — für die braucht es zuerst die AHV-Anmeldung 318.260.
// ══════════════════════════════════════════════════════════════════════

const AKIS_PORTAL_URL = 'https://www.akisnet.ch/ak046/'; // GastroSocial (AK 046)

// Keine Datumswahl (Walter 13.10.2026): immer alle noch nicht gemeldeten
// Ein-/Austritte seit Start der Meldungs-Kontrolle, auch künftige.
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
        const d = await res.json();
        _akisDaten = d;
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        const status = (r, art) => r.gemeldet
            ? `<span title="${r.gemeldet.quelle === 'ABGLEICH' ? 'aus dem AHV-Abgleich übernommen' : 'im AKISnet gemeldet'}${r.gemeldet.von ? ' · ' + esc(r.gemeldet.von) : ''}"
                     style="color:#166534;font-weight:600;white-space:nowrap">✓ gemeldet ${esc(r.gemeldet.am)}</span>
               <button class="dok-menu-btn" style="min-width:auto;padding:1px 7px;font-size:11px;margin-left:4px" title="Rückgängig"
                       onclick="akisMeldungZurueck(${r.gemeldet.id})">↩</button>`
            : `<button class="akis-gemeldet-btn" onclick="akisGemeldet('${art}', [${r.employeeId}], ['${r.datumIso}'], this)">✓ gemeldet</button>`;
        const tabelle = (rows, art) => `<table style="width:100%;border-collapse:collapse;font-size:12.5px">
                ${rows.map(r => `<tr style="border-bottom:1px solid rgba(60,55,48,0.08)">
                    <td style="padding:5px 8px;font-variant-numeric:tabular-nums">${esc(r.ahv) || '<span style="color:#b45309">— fehlt —</span>'}</td>
                    <td style="padding:5px 8px;font-weight:600">${esc(r.vorname)} ${esc(r.name)}</td>
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
            return offen.length
                ? `<button class="akis-gemeldet-btn alle" onclick="akisAlleGemeldet('${art}', this)">✓ Im AKISnet gemeldet (${offen.length})</button>`
                : (rows.length ? '<span style="font-size:11.5px;color:#166534;font-weight:600">✓ alle gemeldet</span>' : '');
        };
        const kopf = (titel, rows, art, zusatz) => `
            <div style="display:flex;align-items:center;gap:10px;margin:18px 0 4px">
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
            ${kopf('Abmeldungen', d.abmeldungen.concat(d.abmeldungenOhneAhv || []), 'ABMELDUNG', ' <span style="font-weight:400;color:#8b8b8b;font-size:11.5px">— nur MA mit erfasstem Austritt</span>')}
            ${warn((d.abmeldungenOhneAhv || []).filter(r => !r.gemeldet), 'Austritt(e)')}
            ${tbl(d.abmeldungen.concat(d.abmeldungenOhneAhv || []), 'ABMELDUNG')}
            ${(d.befristetOffen || []).length ? `
            <div style="background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:10px 12px;margin-top:14px;font-size:12px;color:#92400e">
                <b>${d.befristetOffen.length} befristete(r) Vertrag/Verträge enden bald (oder endeten kürzlich) ohne erfassten Austritt</b> — verlängern oder Austritt erfassen (erst dann erscheinen sie als Abmeldung):<br>
                ${d.befristetOffen.map(r => `${esc(r.vorname)} ${esc(r.name)} (per ${esc(r.datum)})`).join(' · ')}
            </div>` : ''}`;
    } catch (_) {
        el.innerHTML = '<div style="color:#991b1b;padding:14px;font-size:12.5px">Verbindungsfehler.</div>';
    }
}

// Gemeldet merken (Walter 13.10.2026): nach dem Hochladen im AKISnet hier
// bestätigen — sonst bleibt das To-do «GastroSocial-Anmeldung/-Abmeldung fehlt».
let _akisDaten = null;

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

/** Alle noch offenen Zeilen MIT AHV-Nummer (= Inhalt der Excel) einer Liste. */
async function akisAlleGemeldet(art, btn) {
    const rows = ((art === 'ANMELDUNG' ? _akisDaten?.anmeldungen : _akisDaten?.abmeldungen) || []).filter(r => !r.gemeldet);
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
    const liste = typ === 'anmeldung' ? _akisDaten?.anmeldungen : _akisDaten?.abmeldungen;
    if (liste && !liste.some(r => !r.gemeldet)) {
        showToast(`Keine offenen ${typ === 'anmeldung' ? 'Anmeldungen' : 'Abmeldungen'} mit AHV-Nummer — nichts zu exportieren.`, 'info');
        return;
    }
    if (btn) btn.disabled = true;
    try {
        const res = await fetch(`/api/akis-export/${typ}?` + p, { headers: ah() });
        if (!res.ok) { showToast('Export fehlgeschlagen (HTTP ' + res.status + ').', 'error'); return; }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        const name = cdFilename(cd, `${typ}Mitarbeitende.xlsx`);
        if (_akisBlobs[typ]?.url) URL.revokeObjectURL(_akisBlobs[typ].url);
        _akisBlobs[typ] = { blob, name, url: URL.createObjectURL(blob) };
        akisRenderFiles();
        showToast('Excel bereit — Chip in den AKISnet-Tab ziehen oder speichern.', 'success');
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
             title="Auf den Schreibtisch ziehen (Chrome legt die Datei dort ab), dann vom Schreibtisch in die AKISnet-Upload-Zone — Browser erlauben keinen Datei-Drag direkt zwischen zwei Fenstern"
             style="display:flex;align-items:center;gap:10px;background:rgba(255,255,255,0.55);border:1px dashed rgba(60,55,48,0.35);border-radius:12px;padding:10px 14px;cursor:grab;user-select:none">
            <span style="font-size:20px">📄</span>
            <span style="font-size:12.5px;font-weight:600;color:#3f3f3f">${esc(f.name)}<br>
                <span style="font-weight:400;color:#8b8b8b;font-size:11px">auf den Schreibtisch ziehen → von dort ins AKISnet</span></span>
            <button onclick="saveBlobAsk(_akisBlobs['${typ}'].blob, _akisBlobs['${typ}'].name)"
                    title="Speichern unter…"
                    style="background:transparent;border:1px solid rgba(60,55,48,0.18);border-radius:8px;padding:4px 9px;font-size:12px;cursor:pointer;color:#646464">💾</button>
        </div>`).join('');
}

function akisDragStart(e, typ) {
    const f = _akisBlobs[typ];
    if (!f) return;
    const mime = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
    try {
        // Echte Datei in den Drag legen — Drop-Zonen (AKISnet) sehen sie als File.
        e.dataTransfer.items.add(new File([f.blob], f.name, { type: mime }));
    } catch (_) { /* ältere Browser */ }
    // Fallback: Drag auf Desktop/Finder (Chrome-Konvention).
    try { e.dataTransfer.setData('DownloadURL', `${mime}:${f.name}:${f.url}`); } catch (_) {}
    e.dataTransfer.effectAllowed = 'copy';
}

function akisOpenPortal() {
    window.open(AKIS_PORTAL_URL, '_blank', 'noopener');
}
