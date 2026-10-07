// ═══════════════════════════════════════════════════════════════════════
// Feiertage global (Walter 07.10.2026) — Systemeinstellungen → Lohn-Stammdaten.
//   Ein Eintrag gilt national, für einen Kanton oder für eine einzelne Filiale.
//   «Wie Sonntag» = dem Sonntag gleichgestellt (Art. 20a ArG) — Jugendliche
//   dürfen dann nicht arbeiten. Jahresvorschlag pro Kanton. Nur admin pflegt;
//   Filial-Detail und Manager-Dienstplan zeigen die Feiertage nur an.
// ═══════════════════════════════════════════════════════════════════════

const _ftgInp = 'background:#fff;border:1px solid rgba(255,255,255,0.95);border-radius:10px;box-shadow:0 2px 6px rgba(60,55,48,0.13),inset 0 1px 0 rgba(255,255,255,0.9);padding:6px 10px;font-size:13px;color:#3f3f3f';
const _ftgBtnDark = 'background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 16px;font-size:13px;font-weight:600;cursor:pointer';
const _ftgBtnGlas = 'background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(60,55,48,0.2);border-radius:12px;padding:7px 14px;font-size:13px;cursor:pointer';
const _ftgWt = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];

const _ftg = { jahr: new Date().getFullYear(), kanton: '', daten: null, vorschlag: null };

function _ftgEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function _ftgDatum(iso) { return iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : ''; }
function _ftgAdmin() { return (typeof currentUser !== 'undefined' && currentUser?.role) === 'admin'; }

function feiertageInit() { ftgLoad(); }

async function ftgLoad() {
    const el = document.getElementById('ftgInhalt');
    if (!el) return;
    let d;
    try {
        const r = await fetch(`/api/feiertage/alle?year=${_ftg.jahr}&kanton=${encodeURIComponent(_ftg.kanton)}`, { headers: ah() });
        d = await r.json();
        if (!r.ok) { el.textContent = d.message || 'Laden fehlgeschlagen.'; return; }
    } catch (_) { el.textContent = 'Verbindungsfehler.'; return; }
    _ftg.daten = d;

    const admin = _ftgAdmin();
    const j0 = new Date().getFullYear();
    const jahre = [j0 - 1, j0, j0 + 1, j0 + 2];
    if (!jahre.includes(_ftg.jahr)) jahre.push(_ftg.jahr);
    const geltung = f => f.scope === 'NATIONAL' ? 'National'
        : f.scope === 'KANTON' ? `Kanton ${_ftgEsc(f.kantonCode)}` : `Filiale ${_ftgEsc(f.filiale || '?')}`;
    const farbe = s => s === 'NATIONAL' ? '#e0e7ff' : s === 'KANTON' ? '#fef3c7' : '#dcfce7';

    const chip = (code, label, title) => `
        <button type="button" onclick="_ftg.kanton='${code}';ftgLoad()" title="${_ftgEsc(title || '')}"
            style="border-radius:12px;padding:5px 12px;font-size:12.5px;cursor:pointer;color:#3f3f3f;
                   background:${_ftg.kanton === code ? 'rgba(255,255,255,0.85)' : 'transparent'};
                   border:1px solid ${_ftg.kanton === code ? '#3f3f3f' : 'rgba(60,55,48,0.20)'}">${label}</button>`;

    const zeilen = d.feiertage.map(f => {
        const wt = _ftgWt[new Date(f.datum + 'T00:00:00').getDay()];
        return `
        <div style="display:flex;align-items:center;gap:10px;padding:7px 4px;border-bottom:1px solid rgba(60,55,48,0.1)">
            <span style="min-width:96px;color:#646464">${wt} ${_ftgDatum(f.datum)}</span>
            <b style="min-width:170px;color:#3f3f3f">${_ftgEsc(f.bezeichnung)}</b>
            <span style="background:${farbe(f.scope)};border-radius:8px;padding:1px 8px;font-size:11.5px;color:#3f3f3f">${geltung(f)}</span>
            <span style="flex:1"></span>
            <label style="display:flex;align-items:center;gap:6px;font-size:12.5px;color:#3f3f3f;${admin && !f.sonntagsgleichFest ? 'cursor:pointer' : 'opacity:.7'}"
                   title="${f.sonntagsgleichFest ? '1. August ist von Gesetzes wegen dem Sonntag gleichgestellt.' : 'Dem Sonntag gleichgestellt (Art. 20a ArG, kantonal)'}">
                <input type="checkbox" ${f.sonntagsgleich ? 'checked' : ''} ${admin && !f.sonntagsgleichFest ? '' : 'disabled'}
                       onchange="ftgSonntag(${f.id}, this.checked)"> wie Sonntag</label>
            ${admin ? `<div class="dok-menu-wrap">
                <button type="button" class="dok-menu-btn dok-menu-btn-soft" onclick="dokToggleMenu(event, 'ftg${f.id}')" title="Aktionen" aria-label="Aktionen"><span class="dok-menu-dots" aria-hidden="true"></span></button>
                <div class="dok-menu" id="dokMenu-ftg${f.id}">
                    <button class="dok-menu-item" onclick="ftgUmbenennen(${f.id}, '${_ftgEsc(f.bezeichnung).replace(/'/g, '&#39;')}')">Umbenennen</button>
                    <button class="dok-menu-item danger" onclick="ftgLoeschen(${f.id}, '${f.scope}', '${_ftgEsc(f.kantonCode || '')}')">Löschen</button>
                </div></div>` : '<span style="width:28px"></span>'}
        </div>`;
    }).join('');

    const geltungOptionen = `
        <option value="NATIONAL">National (alle Filialen)</option>
        ${d.kantone.map(k => `<option value="KANTON:${k.code}" ${_ftg.kanton === k.code ? 'selected' : ''}>Kanton ${_ftgEsc(k.code)}</option>`).join('')}
        ${d.filialen.map(f => `<option value="FILIALE:${f.id}">Nur Filiale ${_ftgEsc(f.name)}</option>`).join('')}`;

    el.innerHTML = `
        <div class="sticky-section-head" style="display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap;margin-bottom:10px">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Jahr
                <select onchange="_ftg.jahr=parseInt(this.value,10);ftgLoad()" style="${_ftgInp}">
                    ${jahre.sort().map(y => `<option value="${y}" ${y === _ftg.jahr ? 'selected' : ''}>${y}</option>`).join('')}</select></label>
            <div style="display:flex;gap:6px;flex-wrap:wrap;padding-bottom:2px">
                ${chip('', 'Alle')}
                ${d.kantone.map(k => chip(k.code, k.code, k.filialen.join(', '))).join('')}
            </div>
            <span style="flex:1"></span>
            ${admin ? `<button style="${_ftgBtnGlas}" onclick="ftgVorschlag()">Vorschlag ${_ftg.jahr} übernehmen…</button>` : ''}
        </div>
        ${_ftg.kanton ? `<div style="font-size:12.5px;color:#646464;margin-bottom:8px">Gezeigt: was in Kanton ${_ftgEsc(_ftg.kanton)} gilt
            (${_ftgEsc(d.kantone.find(k => k.code === _ftg.kanton)?.filialen.join(', ') || '')}).</div>` : ''}
        ${d.ohneKanton.length ? `<div style="font-size:12.5px;color:#b45309;margin-bottom:8px">Ohne Kanton in den Stammdaten: ${_ftgEsc(d.ohneKanton.join(', '))} — dort gelten nur nationale und Filial-Einträge.</div>` : ''}
        <div class="card" style="padding:8px 14px">
            ${zeilen || `<div style="color:#8b8b8b;font-size:13px;padding:8px 4px">Für ${_ftg.jahr} sind noch keine Feiertage erfasst.${admin ? ' Mit «Vorschlag übernehmen» geht es am schnellsten.' : ''}</div>`}
        </div>
        ${admin ? `
        <div style="display:flex;gap:8px;align-items:flex-end;flex-wrap:wrap;margin-top:14px">
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Datum
                <input type="date" id="ftgDatum" style="${_ftgInp}"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px;flex:1;min-width:180px">Bezeichnung
                <input type="text" id="ftgName" style="${_ftgInp}" placeholder="z.B. Josefstag"></label>
            <label style="font-size:11px;color:#8b8b8b;display:flex;flex-direction:column;gap:3px">Gilt für
                <select id="ftgGeltung" style="${_ftgInp}">${geltungOptionen}</select></label>
            <label style="display:flex;align-items:center;gap:6px;font-size:12.5px;color:#3f3f3f;padding-bottom:8px;cursor:pointer">
                <input type="checkbox" id="ftgSo"> wie Sonntag</label>
            <button style="${_ftgBtnDark}" onclick="ftgNeu()">+ Hinzufügen</button>
        </div>` : `<div style="font-size:12px;color:#8b8b8b;margin-top:10px">Ändern kann nur ein Admin.</div>`}`;
}

async function _ftgSend(url, method, body) {
    const r = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen.', 'error'); return null; }
    return j;
}

async function ftgNeu() {
    const datum = document.getElementById('ftgDatum')?.value;
    const bezeichnung = (document.getElementById('ftgName')?.value || '').trim();
    if (!datum || !bezeichnung) { showToast('Datum und Bezeichnung ausfüllen.', 'error'); return; }
    const [scope, wert] = (document.getElementById('ftgGeltung')?.value || 'NATIONAL').split(':');
    const ok = await _ftgSend('/api/feiertage', 'POST', {
        datum, bezeichnung, scope,
        kantonCode: scope === 'KANTON' ? wert : null,
        companyProfileId: scope === 'FILIALE' ? parseInt(wert, 10) : null,
        sonntagsgleich: !!document.getElementById('ftgSo')?.checked,
    });
    if (!ok) return;
    showToast('Feiertag erfasst.', 'success');
    _ftg.jahr = parseInt(datum.slice(0, 4), 10);
    ftgLoad();
}

async function ftgSonntag(id, wert) {
    const ok = await _ftgSend(`/api/feiertage/${id}`, 'PATCH', { sonntagsgleich: wert });
    ftgLoad();
    if (ok) showToast(wert ? 'Dem Sonntag gleichgestellt.' : 'Nicht mehr dem Sonntag gleichgestellt.', 'success');
}

async function ftgUmbenennen(id, alt) {
    if (typeof dokCloseAllMenus === 'function') dokCloseAllMenus();
    const neu = ((await liquidPrompt('Neue Bezeichnung:', { title: 'Feiertag umbenennen', value: alt, yesLabel: 'Speichern' })) || '').trim();
    if (!neu || neu === alt) return;
    if (await _ftgSend(`/api/feiertage/${id}`, 'PATCH', { bezeichnung: neu })) ftgLoad();
}

async function ftgLoeschen(id, scope, kanton) {
    if (typeof dokCloseAllMenus === 'function') dokCloseAllMenus();
    const hinweis = scope === 'KANTON' ? ` Er fällt für alle Filialen im Kanton ${kanton} weg.`
        : scope === 'NATIONAL' ? ' Er fällt für alle Filialen weg.' : '';
    if (!await liquidConfirm('Diesen Feiertag löschen?' + hinweis, { title: 'Feiertag löschen', yesLabel: 'Löschen' })) return;
    if (await _ftgSend(`/api/feiertage/${id}`, 'DELETE')) ftgLoad();
}

// ── Jahresvorschlag pro Kanton ─────────────────────────────────────────
async function ftgVorschlag(kanton) {
    const kantone = _ftg.daten?.kantone || [];
    kanton = kanton || _ftg.kanton || kantone[0]?.code;
    if (!kanton) { showToast('Keine Filiale mit Kanton in den Stammdaten.', 'error'); return; }
    let d;
    try {
        const r = await fetch(`/api/feiertage/vorschlag?kanton=${encodeURIComponent(kanton)}&year=${_ftg.jahr}`, { headers: ah() });
        d = await r.json();
        if (!r.ok) { showToast(d.message || 'Vorschlag fehlgeschlagen.', 'error'); return; }
    } catch (_) { showToast('Verbindungsfehler.', 'error'); return; }
    _ftg.vorschlag = d;

    const zeilen = d.kandidaten.map((k, i) => `
        <label style="display:flex;align-items:center;gap:10px;padding:6px 4px;border-bottom:1px solid rgba(60,55,48,0.1);${k.schonErfasst ? 'opacity:.55' : 'cursor:pointer'}">
            <input type="checkbox" id="ftgVs-${i}" ${k.vorgewaehlt ? 'checked' : ''} ${k.schonErfasst ? 'disabled' : ''}>
            <span style="min-width:96px;color:#646464">${_ftgWt[new Date(k.datum + 'T00:00:00').getDay()]} ${_ftgDatum(k.datum)}</span>
            <b style="flex:1;color:#3f3f3f">${_ftgEsc(k.bezeichnung)}</b>
            <span style="font-size:11.5px;color:#8b8b8b">${k.schonErfasst ? 'schon erfasst' : k.scope === 'NATIONAL' ? 'National' : `Kanton ${_ftgEsc(d.kanton)}`}</span>
            <span style="display:flex;align-items:center;gap:5px;font-size:12px;color:#3f3f3f" onclick="event.stopPropagation()">
                <input type="checkbox" id="ftgVsSo-${i}" ${k.sonntagsgleich ? 'checked' : ''} ${k.schonErfasst ? 'disabled' : ''}> wie Sonntag</span>
        </label>`).join('');

    document.getElementById('ftgVsModal')?.remove();
    const ov = document.createElement('div');
    ov.id = 'ftgVsModal';
    ov.style.cssText = 'position:fixed;inset:0;background:rgba(30,28,25,0.45);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px';
    ov.innerHTML = `
        <div class="modal" style="max-width:700px;width:100%;max-height:88vh;overflow:auto;padding:20px 22px;border-radius:16px">
            <div style="display:flex;align-items:center;gap:10px;margin-bottom:4px">
                <div style="font-size:15px;font-weight:700;color:#3f3f3f;flex:1">Feiertage ${_ftg.jahr} — Vorschlag für Kanton</div>
                <select onchange="ftgVorschlag(this.value)" style="${_ftgInp}">
                    ${kantone.map(k => `<option value="${k.code}" ${k.code === d.kanton ? 'selected' : ''}>${_ftgEsc(k.code)}</option>`).join('')}</select>
            </div>
            <div style="font-size:12.5px;color:#646464;margin-bottom:10px">
                ${_ftgEsc(d.hinweis || '')}${d.kantonsliste ? '' : ' Vorgewählt sind die üblichen Tage.'}</div>
            ${zeilen}
            <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:14px">
                <button style="${_ftgBtnGlas}" onclick="document.getElementById('ftgVsModal').remove()">Abbrechen</button>
                <button style="${_ftgBtnDark}" onclick="ftgVorschlagSpeichern()">Übernehmen</button>
            </div>
        </div>`;
    ov.onclick = e => { if (e.target === ov) ov.remove(); };
    document.body.appendChild(ov);
}

async function ftgVorschlagSpeichern() {
    const d = _ftg.vorschlag;
    if (!d) return;
    const eintraege = d.kandidaten.map((k, i) => ({ k, i }))
        .filter(({ k, i }) => !k.schonErfasst && document.getElementById(`ftgVs-${i}`)?.checked)
        .map(({ k, i }) => ({
            datum: k.datum, bezeichnung: k.bezeichnung, scope: k.scope,
            sonntagsgleich: !!document.getElementById(`ftgVsSo-${i}`)?.checked,
        }));
    if (!eintraege.length) { showToast('Nichts ausgewählt.', 'error'); return; }
    const j = await _ftgSend('/api/feiertage/uebernehmen', 'POST', { kantonCode: d.kanton, eintraege });
    if (!j) return;
    document.getElementById('ftgVsModal')?.remove();
    showToast(`${j.neu} Feiertag(e) für Kanton ${d.kanton} übernommen${j.uebersprungen ? `, ${j.uebersprungen} übersprungen` : ''}.`, 'success');
    ftgLoad();
}
