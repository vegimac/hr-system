// ═══════════════════════════════════════════════════════════════════════
// Arbeitszeit-Regeln pro Filiale (Walter 07.10.2026)
//   Vorlage am Hauptsitz, jede Filiale kann einzelne Werte abweichend setzen.
//   Leer = übernommen (Filiale ← Vorlage ← Gesetz).
//   Feiertage sind global: Systemeinstellungen → Lohn-Stammdaten → Feiertage (js/feiertage.js).
// ═══════════════════════════════════════════════════════════════════════

const _azInp = 'background:#fff;border:1px solid rgba(255,255,255,0.95);border-radius:10px;box-shadow:0 2px 6px rgba(60,55,48,0.13),inset 0 1px 0 rgba(255,255,255,0.9);padding:6px 10px;font-size:13px;color:#3f3f3f';
const _azBtnDark = 'background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 16px;font-size:13px;font-weight:600;cursor:pointer';
const _azBtnGlas = 'background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(60,55,48,0.2);border-radius:12px;padding:7px 14px;font-size:13px;cursor:pointer';

const _azState = {};   // key → { ebene, daten }

function _azEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function _azRolle() { return (typeof currentUser !== 'undefined' && currentUser?.role) || ''; }
function _azZahl(v) { return v == null ? '' : String(Number(v)); }

// ── Tab im Filial-Detail ───────────────────────────────────────────────
function azTabHtml(cpId) {
    return `
        <div class="emp-section-title">Regeln für Stempel-Verstösse</div>
        <div style="font-size:12.5px;color:#646464;margin:-4px 0 10px">
            Feiertage gelten für alle Filialen gemeinsam und werden in den Systemeinstellungen gepflegt:
            ${_azRolle() === 'admin' ? `<a href="#" onclick="event.preventDefault();showPage('feiertage')" style="color:#3f3f3f;font-weight:600">Lohn-Stammdaten → Feiertage</a>` : 'Lohn-Stammdaten → Feiertage'}.</div>
        <div id="azRg-f${cpId}">Wird geladen…</div>`;
}

function azTabLoad(cpId) {
    azRegelnLoad({ companyProfileId: cpId }, `azRg-f${cpId}`);
}

// ── Regeln (Hauptsitz-Vorlage oder Filiale) ────────────────────────────
async function azRegelnLoad(ebene, containerId) {
    const el = document.getElementById(containerId);
    if (!el) return;
    const q = ebene.companyProfileId ? `companyProfileId=${ebene.companyProfileId}` : `hauptsitzId=${ebene.hauptsitzId}`;
    let d;
    try {
        const r = await fetch(`/api/arbeitszeit-regeln?${q}`, { headers: ah() });
        d = await r.json();
        if (!r.ok) { el.textContent = d.message || 'Laden fehlgeschlagen.'; return; }
    } catch (_) { el.textContent = 'Verbindungsfehler.'; return; }
    _azState[containerId] = { ebene, daten: d };

    const filiale = !!ebene.companyProfileId;
    const kann = filiale ? ['admin', 'superuser'].includes(_azRolle()) : _azRolle() === 'admin';
    const herkunft = filiale ? (d.hauptsitzName ? `Vorlage «${_azEsc(d.hauptsitzName)}»` : 'Gesetz (kein Hauptsitz zugeordnet)') : 'Gesetz';
    const dis = kann ? '' : 'disabled';

    const regeln = d.regeln.map(r => {
        const eigen = filiale ? 'filiale' : 'vorlage';
        const aktivEigen = r.aktiv[eigen];
        const aktivVorher = filiale ? r.aktiv.vorlageGilt : true;
        const abweichend = r.parameter.some(p => p[eigen] != null) || aktivEigen != null || r.text[eigen];
        const params = r.parameter.map(p => {
            const vorher = filiale ? p.vorlageGilt : p.standard;
            const id = `az-${containerId}-${r.code}-${p.schluessel}`;
            const feld = p.einheit === 'ja/nein'
                ? `<select id="${id}" ${dis} style="${_azInp};min-width:150px">
                       <option value="">wie ${filiale ? 'Vorlage' : 'Gesetz'} (${Number(vorher) ? 'ja' : 'nein'})</option>
                       <option value="1" ${p[eigen] === 1 ? 'selected' : ''}>ja</option>
                       <option value="0" ${p[eigen] === 0 ? 'selected' : ''}>nein</option></select>`
                : `<span style="display:flex;align-items:center;gap:6px">
                       <input type="number" id="${id}" ${dis} step="${p.einheit === 'Std.' || p.einheit === 'Uhr' ? '0.25' : '1'}" min="${p.min}" max="${p.max}"
                              value="${_azZahl(p[eigen])}" placeholder="${_azZahl(vorher)}" style="${_azInp};width:90px">
                       <span style="font-size:12px;color:#8b8b8b" ${p.einheit === 'Uhr' ? 'title="Dezimal: 14.5 = 14:30 Uhr"' : ''}>${_azEsc(p.einheit)}${p.einheit === 'Uhr' ? ' (14.5 = 14:30)' : ''}</span></span>`;
            return `<label style="display:flex;flex-direction:column;gap:3px;font-size:11.5px;color:#646464">${_azEsc(p.label)}${feld}</label>`;
        }).join('');
        return `
        <details style="border-bottom:1px solid rgba(60,55,48,0.12);padding:8px 2px" ${abweichend ? 'open' : ''}>
            <summary style="cursor:pointer;display:flex;align-items:center;gap:10px;list-style:none">
                <span style="width:9px;height:9px;border-radius:50%;background:${r.aktiv.gilt ? '#16a34a' : '#b8b2a7'}"></span>
                <b style="color:#3f3f3f;font-size:13.5px">${_azEsc(r.titel)}</b>
                ${abweichend ? `<span style="background:#fef3c7;border-radius:8px;padding:1px 8px;font-size:11px;color:#3f3f3f">${filiale ? 'abweichend' : 'angepasst'}</span>` : ''}
                ${r.aktiv.gilt ? '' : '<span style="font-size:11.5px;color:#8b8b8b">ausgeschaltet</span>'}
                <span style="flex:1"></span>
                <span style="font-size:11px;color:#8b8b8b">${_azEsc(r.recht)}</span>
            </summary>
            <div style="padding:10px 0 4px 19px">
                <div style="font-size:12.5px;color:#646464;margin-bottom:10px">${_azEsc(r.giltText)}</div>
                <div style="display:flex;flex-wrap:wrap;gap:12px 18px;align-items:flex-end">
                    <label style="display:flex;flex-direction:column;gap:3px;font-size:11.5px;color:#646464">Prüfen
                        <select id="az-${containerId}-${r.code}-aktiv" ${dis} style="${_azInp};min-width:150px">
                            <option value="">wie ${filiale ? 'Vorlage' : 'Gesetz'} (${aktivVorher ? 'ein' : 'aus'})</option>
                            <option value="1" ${aktivEigen === 1 ? 'selected' : ''}>ein</option>
                            <option value="0" ${aktivEigen === 0 ? 'selected' : ''}>aus</option></select></label>
                    ${params}
                </div>
                <label style="display:flex;flex-direction:column;gap:3px;font-size:11.5px;color:#646464;margin-top:10px">Eigene Erklärung im Bericht (leer = automatisch)
                    <textarea id="az-${containerId}-${r.code}-text" ${dis} rows="2" placeholder="${_azEsc(filiale ? r.vorlageText : r.standardText)}"
                              style="${_azInp};resize:vertical;font-family:inherit">${_azEsc(r.text[eigen] || '')}</textarea></label>
            </div>
        </details>`;
    }).join('');

    const geaendert = d.geaendert ? `Zuletzt geändert ${new Date(d.geaendert.am).toLocaleString('de-CH', { dateStyle: 'short', timeStyle: 'short' })}${d.geaendert.von ? ' von ' + _azEsc(d.geaendert.von) : ''}` : '';
    el.innerHTML = `
        <div style="font-size:12.5px;color:#646464;margin:-4px 0 8px">
            Leere Felder übernehmen den Wert aus ${herkunft} (grau angezeigt). Nur was hier eingetragen ist, weicht ab.
            Neue Regel-Arten lassen sich nicht selbst erfinden — jede braucht eine eigene Prüfung im Programm.</div>
        ${regeln}
        <div style="display:flex;align-items:center;gap:8px;margin-top:12px">
            <span style="font-size:11.5px;color:#8b8b8b">${geaendert}</span>
            <span style="flex:1"></span>
            ${kann ? `<button style="${_azBtnGlas}" onclick="azRegelnZuruecksetzen('${containerId}')">Alle Abweichungen entfernen</button>
                      <button style="${_azBtnDark}" onclick="azRegelnSpeichern('${containerId}')">Speichern</button>`
                   : `<span style="font-size:12px;color:#8b8b8b">${filiale ? 'Ändern können HR und Admin.' : 'Ändern kann nur ein Admin.'}</span>`}
        </div>`;
}

function _azWerteSammeln(containerId) {
    const st = _azState[containerId];
    const werte = [];
    for (const r of st.daten.regeln) {
        const a = document.getElementById(`az-${containerId}-${r.code}-aktiv`)?.value;
        if (a !== '' && a != null) werte.push({ regel: r.code, schluessel: 'aktiv', wert: Number(a) });
        for (const p of r.parameter) {
            const v = document.getElementById(`az-${containerId}-${r.code}-${p.schluessel}`)?.value;
            if (v !== '' && v != null) werte.push({ regel: r.code, schluessel: p.schluessel, wert: Number(v) });
        }
        const t = (document.getElementById(`az-${containerId}-${r.code}-text`)?.value || '').trim();
        if (t) werte.push({ regel: r.code, schluessel: 'text', text: t });
    }
    return werte;
}

async function _azRegelnSenden(containerId, werte) {
    const { ebene } = _azState[containerId];
    const r = await fetch('/api/arbeitszeit-regeln', {
        method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ hauptsitzId: ebene.hauptsitzId || null, companyProfileId: ebene.companyProfileId || null, werte }),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen.', 'error'); return false; }
    showToast('Regeln gespeichert.', 'success');
    azRegelnLoad(ebene, containerId);
    return true;
}

function azRegelnSpeichern(containerId) { return _azRegelnSenden(containerId, _azWerteSammeln(containerId)); }

async function azRegelnZuruecksetzen(containerId) {
    const filiale = !!_azState[containerId]?.ebene.companyProfileId;
    if (!await liquidConfirm(filiale ? 'Alle Abweichungen dieser Filiale entfernen? Danach gilt wieder die Vorlage des Hauptsitzes.'
                                     : 'Alle Anpassungen der Vorlage entfernen? Danach gelten wieder die gesetzlichen Werte.',
                             { title: 'Regeln zurücksetzen', yesLabel: 'Entfernen' })) return;
    _azRegelnSenden(containerId, []);
}

// ── Hauptsitz-Vorlage (aus dem Hauptsitz-Modul) ────────────────────────
function azVorlageOeffnen(hauptsitzId, name) {
    if (typeof dokCloseAllMenus === 'function') dokCloseAllMenus();
    document.getElementById('azHsModal')?.remove();
    const ov = document.createElement('div');
    ov.id = 'azHsModal';
    ov.style.cssText = 'position:fixed;inset:0;background:rgba(30,28,25,0.45);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px';
    ov.innerHTML = `
        <div class="modal" style="max-width:860px;width:100%;max-height:90vh;overflow:auto;padding:20px 24px;border-radius:16px">
            <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:6px">
                <div style="font-size:15px;font-weight:700;color:#3f3f3f">Arbeitszeit-Regeln — Vorlage ${_azEsc(name)}</div>
                <button style="${_azBtnGlas};padding:4px 12px" onclick="document.getElementById('azHsModal').remove()">✕</button>
            </div>
            <div style="font-size:12.5px;color:#646464;margin-bottom:10px">Gilt für alle Filialen dieses Hauptsitzes. Jede Filiale kann einzelne Werte im Filial-Detail → «Arbeitszeit» abweichend setzen.</div>
            <div id="azRg-h${hauptsitzId}">Wird geladen…</div>
        </div>`;
    ov.onclick = e => { if (e.target === ov) ov.remove(); };
    document.body.appendChild(ov);
    azRegelnLoad({ hauptsitzId }, `azRg-h${hauptsitzId}`);
}
