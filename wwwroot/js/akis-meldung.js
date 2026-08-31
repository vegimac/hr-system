// ══════════════════════════════════════════════════════════════════════
//  AKIS AN-/ABMELDUNG (HR-Hub → Behörden-Korrespondenz, Walter 06.08.2026)
//  GastroSocial-Ausgleichskasse: OneCrew erzeugt die AKISnet-Upload-Excel
//  aus den Ein-/Austritten der Filiale (Original-Vorlagen), hochladen
//  bleibt manuell im Portal (kein API/ELM). MA ohne AHV-Nummer werden
//  separat gelistet — für die braucht es zuerst die AHV-Anmeldung 318.260.
// ══════════════════════════════════════════════════════════════════════

const AKIS_PORTAL_URL = 'https://www.akisnet.ch/ak046/'; // GastroSocial (AK 046)

function akisInit() {
    // Default-Zeitraum: aktueller Monat.
    const now = new Date();
    const from = new Date(now.getFullYear(), now.getMonth(), 1);
    const to   = new Date(now.getFullYear(), now.getMonth() + 1, 0);
    const iso = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    const f = document.getElementById('akisFrom');
    const t = document.getElementById('akisTo');
    if (f && !f.value) f.value = iso(from);
    if (t && !t.value) t.value = iso(to);
    akisRefresh();
}

function _akisParams() {
    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    const from = document.getElementById('akisFrom')?.value;
    const to   = document.getElementById('akisTo')?.value;
    if (!cid) { showToast('Bitte oben links eine Filiale wählen.', 'error'); return null; }
    if (!from || !to) { showToast('Bitte Zeitraum wählen.', 'error'); return null; }
    return `companyProfileId=${cid}&from=${from}&to=${to}`;
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
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        const tbl = (rows, withEintritt) => rows.length
            ? `<table style="width:100%;border-collapse:collapse;font-size:12.5px">
                ${rows.map(r => `<tr style="border-bottom:1px solid rgba(60,55,48,0.08)">
                    <td style="padding:5px 8px;font-variant-numeric:tabular-nums">${esc(r.ahv) || '<span style="color:#b45309">— fehlt —</span>'}</td>
                    <td style="padding:5px 8px;font-weight:600">${esc(r.vorname)} ${esc(r.name)}</td>
                    <td style="padding:5px 8px">${esc(r.gebDat) || ''}</td>
                    <td style="padding:5px 8px">${esc(r.geschlecht) || ''}</td>
                    <td style="padding:5px 8px;font-weight:600">${esc(r.datum)}</td>
                    ${withEintritt ? `<td style="padding:5px 8px">${esc(r.sprache)}</td>` : ''}
                </tr>`).join('')}
               </table>`
            : '<div style="color:#8b8b8b;padding:8px 0 14px;font-size:12px;font-style:italic">keine</div>';
        const warn = (rows, was) => rows.length
            ? `<div style="background:#fef2f2;border:1px solid #fecaca;border-radius:8px;padding:8px 12px;margin:6px 0 12px;font-size:12px;color:#991b1b">
                ⚠ ${rows.length} ${was} ohne AHV-Nummer — zuerst per «AHV-Anmeldung (Versicherungsausweis)» melden, sie fehlen in der Excel:
                ${rows.map(r => `${esc(r.vorname)} ${esc(r.name)}`).join(', ')}</div>`
            : '';
        el.innerHTML = `
            <div style="font-size:13px;font-weight:700;color:#3f3f3f;margin:14px 0 4px">Anmeldungen (${d.anmeldungen.length})</div>
            ${warn(d.anmeldungenOhneAhv, 'Eintritt(e)')}
            ${tbl(d.anmeldungen, true)}
            <div style="font-size:13px;font-weight:700;color:#3f3f3f;margin:18px 0 4px">Abmeldungen (${d.abmeldungen.length}) <span style="font-weight:400;color:#8b8b8b;font-size:11.5px">— nur MA mit erfasstem Austritt</span></div>
            ${warn(d.abmeldungenOhneAhv, 'Austritt(e)')}
            ${tbl(d.abmeldungen, false)}
            ${(d.befristetOffen || []).length ? `
            <div style="background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:10px 12px;margin-top:14px;font-size:12px;color:#92400e">
                <b>${d.befristetOffen.length} befristete(r) Vertrag/Verträge enden im Zeitraum ohne erfassten Austritt</b> — verlängern oder Austritt erfassen (erst dann erscheinen sie als Abmeldung):<br>
                ${d.befristetOffen.map(r => `${esc(r.vorname)} ${esc(r.name)} (per ${esc(r.datum)})`).join(' · ')}
            </div>` : ''}`;
    } catch (_) {
        el.innerHTML = '<div style="color:#991b1b;padding:14px;font-size:12.5px">Verbindungsfehler.</div>';
    }
}

// Erzeugte Dateien im Speicher — als ziehbare Chips angeboten (Walter
// 06.08.2026): direkt aus OneCrew in den AKISnet-Tab bzw. auf den Desktop
// ziehen, ohne Umweg über den Finder. Speichern-Knopf bleibt als Option.
const _akisBlobs = {};   // typ → { blob, name, url }

async function akisDownload(typ, btn) {
    const p = _akisParams();
    if (!p) return;
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
