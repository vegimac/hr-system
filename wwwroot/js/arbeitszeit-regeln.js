// ═══════════════════════════════════════════════════════════════════════
// Arbeitszeit-Regeln + Feiertage pro Filiale (Walter 07.10.2026)
//   Regeln: Vorlage am Hauptsitz, jede Filiale kann einzelne Werte abweichend
//           setzen. Leer = übernommen (Filiale ← Vorlage ← Gesetz).
//   Feiertage: Pflege nur hier (Manager-Dienstplan zeigt sie nur an), inkl.
//           «dem Sonntag gleichgestellt» (Jugendliche dürfen nicht arbeiten)
//           und Jahresvorschlag pro Kanton.
// ═══════════════════════════════════════════════════════════════════════

const _azInp = 'background:#fff;border:1px solid rgba(255,255,255,0.95);border-radius:10px;box-shadow:0 2px 6px rgba(60,55,48,0.13),inset 0 1px 0 rgba(255,255,255,0.9);padding:6px 10px;font-size:13px;color:#3f3f3f';
const _azBtnDark = 'background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 16px;font-size:13px;font-weight:600;cursor:pointer';
const _azBtnGlas = 'background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(60,55,48,0.2);border-radius:12px;padding:7px 14px;font-size:13px;cursor:pointer';
const _azWt = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];

const _azState = {};   // key → { ebene, daten }

function _azEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function _azRolle() { return (typeof currentUser !== 'undefined' && currentUser?.role) || ''; }
function _azDatum(iso) { return iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : ''; }
function _azZahl(v) { return v == null ? '' : String(Number(v)); }

// ── Tab im Filial-Detail ───────────────────────────────────────────────
function azTabHtml(cpId) {
    return `
        <div class="emp-section-title">Feiertage</div>
        <div style="font-size:12.5px;color:#646464;margin:-4px 0 10px">
            Gelten für Manager-Dienstplan und Stempel-Berichte. «Wie Sonntag» = dem Sonntag gleichgestellt — Jugendliche dürfen dann nicht arbeiten.</div>
        <div id="azFt-${cpId}" style="margin-bottom:22px">Wird geladen…</div>
        <div class="emp-section-title">Regeln für Stempel-Verstösse</div>
        <div id="azRg-f${cpId}">Wird geladen…</div>`;
}

function azTabLoad(cpId) {
    azFtLoad(cpId);
    azRegelnLoad({ companyProfileId: cpId }, `azRg-f${cpId}`);
}

// ── Feiertage ──────────────────────────────────────────────────────────
async function azFtLoad(cpId, jahr) {
    const el = document.getElementById(`azFt-${cpId}`);
    if (!el) return;
    jahr = jahr || parseInt(document.getElementById(`azFtJahr-${cpId}`)?.value, 10) || new Date().getFullYear();
    let d;
    try {
        const r = await fetch(`/api/feiertage?companyProfileId=${cpId}&year=${jahr}`, { headers: ah() });
        d = await r.json();
        if (!r.ok) { el.textContent = d.message || 'Laden fehlgeschlagen.'; return; }
    } catch (_) { el.textContent = 'Verbindungsfehler.'; return; }

    const kannPflegen = ['admin', 'superuser'].includes(_azRolle());
    const istAdmin = _azRolle() === 'admin';
    const j0 = new Date().getFullYear();
    const jahre = [j0 - 1, j0, j0 + 1, j0 + 2];
    if (!jahre.includes(jahr)) jahre.push(jahr);
    const geltung = f => f.scope === 'NATIONAL' ? 'National (alle Filialen)'
        : f.scope === 'KANTON' ? `Kanton ${_azEsc(f.kantonCode)} (alle Filialen im Kanton)` : 'Nur diese Filiale';
    const farbe = s => s === 'NATIONAL' ? '#e0e7ff' : s === 'KANTON' ? '#fef3c7' : '#dcfce7';

    const zeilen = d.feiertage.map(f => {
        const wt = _azWt[new Date(f.datum + 'T00:00:00').getDay()];
        const darf = kannPflegen && (f.scope !== 'NATIONAL' || istAdmin);
        return `
        <div style="display:flex;align-items:center;gap:10px;padding:7px 4px;border-bottom:1px solid rgba(60,55,48,0.1)">
            <span style="min-width:96px;color:#646464">${wt} ${_azDatum(f.datum)}</span>
            <b style="min-width:150px;color:#3f3f3f">${_azEsc(f.bezeichnung)}</b>
            <span style="background:${farbe(f.scope)};border-radius:8px;padding:1px 8px;font-size:11.5px;color:#3f3f3f">${geltung(f)}</span>
            <span style="flex:1"></span>
            <label style="display:flex;align-items:center;gap:6px;font-size:12.5px;color:#3f3f3f;${darf && !f.sonntagsgleichFest ? 'cursor:pointer' : 'opacity:.7'}"
                   title="${f.sonntagsgleichFest ? '1. August ist von Gesetzes wegen dem Sonntag gleichgestellt.' : 'Dem Sonntag gleichgestellt (kantonales Recht)'}">
                <input type="checkbox" ${f.sonntagsgleich ? 'checked' : ''} ${darf && !f.sonntagsgleichFest ? '' : 'disabled'}
                       onchange="azFtSonntag(${cpId}, ${f.id}, this.checked)"> wie Sonntag</label>
            ${darf ? `<div class="dok-menu-wrap">
                <button type="button" class="dok-menu-btn dok-menu-btn-soft" onclick="dokToggleMenu(event, 'azft${f.id}')" title="Aktionen" aria-label="Aktionen"><span class="dok-menu-dots" aria-hidden="true"></span></button>
                <div class="dok-menu" id="dokMenu-azft${f.id}">
                    <button class="dok-menu-item" onclick="azFtUmbenennen(${cpId}, ${f.id}, '${_azEsc(f.bezeichnung).replace(/'/g, '&#39;')}')">Umbenennen</button>
                    <button class="dok-menu-item danger" onclick="azFtLoeschen(${cpId}, ${f.id}, '${f.scope}')">Löschen</button>
                </div></div>` : '<span style="width:28px"></span>'}
        </div>`;
    }).join('');

    el.innerHTML = `
        <div style="display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap;margin-bottom:8px">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Jahr
                <select id="azFtJahr-${cpId}" onchange="azFtLoad(${cpId})" style="${_azInp}">
                    ${jahre.sort().map(y => `<option value="${y}" ${y === jahr ? 'selected' : ''}>${y}</option>`).join('')}</select></label>
            <span style="font-size:12px;color:#8b8b8b;padding-bottom:8px">Kanton: ${d.kanton ? _azEsc(d.kanton) : '<span style="color:#b45309">nicht gesetzt (Stammdaten)</span>'}</span>
            <span style="flex:1"></span>
            ${kannPflegen ? `<button style="${_azBtnGlas}" onclick="azFtVorschlag(${cpId}, ${jahr})">Vorschlag ${jahr} übernehmen…</button>` : ''}
        </div>
        ${zeilen || `<div style="color:#8b8b8b;font-size:13px;padding:8px 4px">Für ${jahr} sind noch keine Feiertage erfasst.${kannPflegen ? ' Mit «Vorschlag übernehmen» geht es am schnellsten.' : ''}</div>`}
        ${kannPflegen ? `
        <div style="display:flex;gap:8px;align-items:flex-end;flex-wrap:wrap;margin-top:12px">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Datum
                <input type="date" id="azFtDatum-${cpId}" style="${_azInp}"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px;flex:1;min-width:160px">Bezeichnung
                <input type="text" id="azFtName-${cpId}" style="${_azInp}" placeholder="z.B. Berchtoldstag"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Gilt für
                <select id="azFtScope-${cpId}" style="${_azInp}">
                    ${d.kanton ? `<option value="KANTON">Kanton ${_azEsc(d.kanton)}</option>` : ''}
                    <option value="FILIALE">Nur diese Filiale</option>
                    ${istAdmin ? '<option value="NATIONAL">National (alle Filialen)</option>' : ''}
                </select></label>
            <label style="display:flex;align-items:center;gap:6px;font-size:12.5px;color:#3f3f3f;padding-bottom:8px;cursor:pointer">
                <input type="checkbox" id="azFtSo-${cpId}"> wie Sonntag</label>
            <button style="${_azBtnDark}" onclick="azFtNeu(${cpId})">+ Hinzufügen</button>
        </div>` : ''}`;
}

async function _azFtSend(url, method, body) {
    const r = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen.', 'error'); return null; }
    return j;
}

async function azFtNeu(cpId) {
    const datum = document.getElementById(`azFtDatum-${cpId}`)?.value;
    const bezeichnung = (document.getElementById(`azFtName-${cpId}`)?.value || '').trim();
    if (!datum || !bezeichnung) { showToast('Datum und Bezeichnung ausfüllen.', 'error'); return; }
    const ok = await _azFtSend('/api/feiertage', 'POST', {
        companyProfileId: cpId, datum, bezeichnung,
        scope: document.getElementById(`azFtScope-${cpId}`)?.value || 'FILIALE',
        sonntagsgleich: !!document.getElementById(`azFtSo-${cpId}`)?.checked,
    });
    if (ok) { showToast('Feiertag erfasst.', 'success'); azFtLoad(cpId, parseInt(datum.slice(0, 4), 10)); }
}

async function azFtSonntag(cpId, id, wert) {
    const ok = await _azFtSend(`/api/feiertage/${id}`, 'PATCH', { companyProfileId: cpId, sonntagsgleich: wert });
    azFtLoad(cpId);
    if (ok) showToast(wert ? 'Dem Sonntag gleichgestellt.' : 'Nicht mehr dem Sonntag gleichgestellt.', 'success');
}

async function azFtUmbenennen(cpId, id, alt) {
    if (typeof dokCloseAllMenus === 'function') dokCloseAllMenus();
    const neu = ((await liquidPrompt('Neue Bezeichnung:', { title: 'Feiertag umbenennen', value: alt, yesLabel: 'Speichern' })) || '').trim();
    if (!neu || neu === alt) return;
    if (await _azFtSend(`/api/feiertage/${id}`, 'PATCH', { companyProfileId: cpId, bezeichnung: neu })) azFtLoad(cpId);
}

async function azFtLoeschen(cpId, id, scope) {
    if (typeof dokCloseAllMenus === 'function') dokCloseAllMenus();
    const hinweis = scope === 'KANTON' ? ' Er fällt für alle Filialen in diesem Kanton weg.'
        : scope === 'NATIONAL' ? ' Er fällt für alle Filialen weg.' : '';
    if (!await liquidConfirm('Diesen Feiertag löschen?' + hinweis, { title: 'Feiertag löschen', yesLabel: 'Löschen' })) return;
    if (await _azFtSend(`/api/feiertage/${id}?companyProfileId=${cpId}`, 'DELETE')) azFtLoad(cpId);
}

async function azFtVorschlag(cpId, jahr) {
    let d;
    try {
        const r = await fetch(`/api/feiertage/vorschlag?companyProfileId=${cpId}&year=${jahr}`, { headers: ah() });
        d = await r.json();
        if (!r.ok) { showToast(d.message || 'Vorschlag fehlgeschlagen.', 'error'); return; }
    } catch (_) { showToast('Verbindungsfehler.', 'error'); return; }
    window._azVorschlag = d.kandidaten;
    const geltung = s => s === 'NATIONAL' ? 'National' : s === 'KANTON' ? `Kanton ${_azEsc(d.kanton)}` : 'Nur diese Filiale';
    const zeilen = d.kandidaten.map((k, i) => `
        <label style="display:flex;align-items:center;gap:10px;padding:6px 4px;border-bottom:1px solid rgba(60,55,48,0.1);${k.schonErfasst ? 'opacity:.55' : 'cursor:pointer'}">
            <input type="checkbox" id="azVs-${i}" ${k.vorgewaehlt ? 'checked' : ''} ${k.schonErfasst ? 'disabled' : ''}>
            <span style="min-width:96px;color:#646464">${_azWt[new Date(k.datum + 'T00:00:00').getDay()]} ${_azDatum(k.datum)}</span>
            <b style="flex:1;color:#3f3f3f">${_azEsc(k.bezeichnung)}</b>
            <span style="font-size:11.5px;color:#8b8b8b">${k.schonErfasst ? 'schon erfasst' : geltung(k.scope)}</span>
            <span style="display:flex;align-items:center;gap:5px;font-size:12px;color:#3f3f3f" onclick="event.stopPropagation()">
                <input type="checkbox" id="azVsSo-${i}" ${k.sonntagsgleich ? 'checked' : ''} ${k.schonErfasst ? 'disabled' : ''}> wie Sonntag</span>
        </label>`).join('');

    document.getElementById('azVsModal')?.remove();
    const ov = document.createElement('div');
    ov.id = 'azVsModal';
    ov.style.cssText = 'position:fixed;inset:0;background:rgba(30,28,25,0.45);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px';
    ov.innerHTML = `
        <div class="modal" style="max-width:680px;width:100%;max-height:88vh;overflow:auto;padding:20px 22px;border-radius:16px">
            <div style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:4px">Feiertage ${jahr} — Vorschlag${d.kanton ? ` für Kanton ${_azEsc(d.kanton)}` : ''}</div>
            <div style="font-size:12.5px;color:#646464;margin-bottom:10px">
                Bitte prüfen: Welche Tage frei sind und welche dem Sonntag gleichgestellt sind, regelt der Kanton. Vorgewählt sind die üblichen Tage.</div>
            ${zeilen}
            <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:14px">
                <button style="${_azBtnGlas}" onclick="document.getElementById('azVsModal').remove()">Abbrechen</button>
                <button style="${_azBtnDark}" onclick="azFtVorschlagSpeichern(${cpId}, ${jahr})">Übernehmen</button>
            </div>
        </div>`;
    ov.onclick = e => { if (e.target === ov) ov.remove(); };
    document.body.appendChild(ov);
}

async function azFtVorschlagSpeichern(cpId, jahr) {
    const eintraege = (window._azVorschlag || []).map((k, i) => ({ k, i }))
        .filter(({ k, i }) => !k.schonErfasst && document.getElementById(`azVs-${i}`)?.checked)
        .map(({ k, i }) => ({
            companyProfileId: cpId, datum: k.datum, bezeichnung: k.bezeichnung, scope: k.scope,
            sonntagsgleich: !!document.getElementById(`azVsSo-${i}`)?.checked,
        }));
    if (!eintraege.length) { showToast('Nichts ausgewählt.', 'error'); return; }
    const j = await _azFtSend('/api/feiertage/uebernehmen', 'POST', { companyProfileId: cpId, eintraege });
    if (!j) return;
    document.getElementById('azVsModal')?.remove();
    showToast(`${j.neu} Feiertag(e) übernommen${j.uebersprungen ? `, ${j.uebersprungen} übersprungen` : ''}.`, 'success');
    azFtLoad(cpId, jahr);
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
                       <input type="number" id="${id}" ${dis} step="${p.einheit === 'Std.' ? '0.25' : '1'}" min="${p.min}" max="${p.max}"
                              value="${_azZahl(p[eigen])}" placeholder="${_azZahl(vorher)}" style="${_azInp};width:90px">
                       <span style="font-size:12px;color:#8b8b8b">${_azEsc(p.einheit)}</span></span>`;
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
            <div style="font-size:12.5px;color:#646464;margin-bottom:10px">Gilt für alle Filialen dieses Hauptsitzes. Jede Filiale kann einzelne Werte im Filial-Detail → «Arbeitszeit &amp; Feiertage» abweichend setzen.</div>
            <div id="azRg-h${hauptsitzId}">Wird geladen…</div>
        </div>`;
    ov.onclick = e => { if (e.target === ov) ov.remove(); };
    document.body.appendChild(ov);
    azRegelnLoad({ hauptsitzId }, `azRg-h${hauptsitzId}`);
}
