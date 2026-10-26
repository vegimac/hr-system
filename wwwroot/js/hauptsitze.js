// ══════════════════════════════════════════════════════════════════════
//  HAUPTSITZE / RECHTSEINHEITEN (Walter-Vorgabe 29.08.2026)
//  System → Filialen & Benutzer → «Hauptsitze». Mehrere Hauptsitze möglich
//  (Lizenznehmer mit 2 GmbHs); Filial-Zuordnung im Filial-Stammdaten-Modal.
//  Die Swissdec-Meldung läuft pro Hauptsitz (UID = Meldungskopf).
// ══════════════════════════════════════════════════════════════════════

let _hsList = [];
let _hsEditId = null;

async function loadHauptsitze() {
    const el = document.getElementById('hsListe');
    if (!el) return;
    el.innerHTML = '<div style="color:#8b8b8b;padding:12px">⏳ wird geladen…</div>';
    try {
        const r = await fetch('/api/hauptsitze', { headers: ah() });
        if (!r.ok) { el.innerHTML = '<div style="color:#b91c1c;padding:12px">Laden fehlgeschlagen.</div>'; return; }
        _hsList = await r.json();
        await azmLaden();
        window._hsCache = _hsList; // für die Filial-Stammdaten-Anzeige
        hsRender();
    } catch (e) {
        el.innerHTML = `<div style="color:#b91c1c;padding:12px">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

function hsRender() {
    const el = document.getElementById('hsListe');
    if (!el) return;
    if (!_hsList.length) {
        el.innerHTML = `<div style="color:#8b8b8b;padding:14px;background:rgba(255,255,255,0.45);border:1px solid rgba(255,255,255,0.62);border-radius:12px;max-width:760px">
            Noch kein Hauptsitz erfasst — auf «+ Hauptsitz erfassen» klicken.
            Der Hauptsitz ist die Rechtseinheit (GmbH) mit ihrer UID; die Filialen werden ihm im Filial-Stammdaten-Modal zugeordnet.</div>`;
        return;
    }
    el.innerHTML = _hsList.map(h => `
        <div style="background:rgba(255,255,255,0.55);border:1px solid rgba(255,255,255,0.62);border-radius:14px;padding:14px 16px;margin-bottom:10px;max-width:860px;box-shadow:0 2px 8px rgba(60,55,48,0.08)">
            <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
                <div style="font-weight:800;font-size:15px;color:#3f3f3f">🏛 ${esc(h.name)}</div>
                ${h.uid ? `<span style="background:#fff;border:1px solid rgba(60,55,48,0.18);border-radius:8px;padding:1px 8px;font-size:12px;font-family:ui-monospace,Menlo,monospace">${esc(h.uid)}</span>`
                        : '<span style="color:#b45309;font-size:12px">⚠ UID fehlt</span>'}
                ${h.isActive ? '' : '<span style="background:#fee2e2;color:#991b1b;border-radius:8px;padding:1px 8px;font-size:11.5px">inaktiv</span>'}
                <span style="flex:1"></span>
                <div style="position:relative;display:inline-block">
                    <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'hs-${h.id}')" title="Aktionen">⋮</button>
                    <div class="dok-menu" id="dokMenu-hs-${h.id}">
                        <button class="dok-menu-item" onclick="dokCloseAllMenus();hsOpenModal(${h.id})">Bearbeiten</button>
                        <button class="dok-menu-item" onclick="azVorlageOeffnen(${h.id}, ${JSON.stringify(h.name || '').replace(/"/g, '&quot;')})">Arbeitszeit-Regeln (Vorlage)</button>
                        <button class="dok-menu-item danger" onclick="dokCloseAllMenus();hsDelete(${h.id})">Löschen</button>
                    </div>
                </div>
            </div>
            <div style="font-size:12.5px;color:#646464;margin-top:4px">
                ${[h.strasse, [h.plz, h.ort].filter(Boolean).join(' '), h.kantonCode].filter(Boolean).join(' · ') || '—'}
                ${h.bemerkung ? ' · <span style="color:#8b8b8b">' + esc(h.bemerkung) + '</span>' : ''}
            </div>
            <div style="font-size:12.5px;margin-top:6px">
                <span style="font-weight:600;color:#646464">Filialen:</span>
                ${h.filialen && h.filialen.length
                    ? h.filialen.map(f => `<span style="background:#fff;border:1px solid rgba(60,55,48,0.14);border-radius:8px;padding:1px 8px;margin-right:4px;display:inline-block;margin-top:3px">${esc((f.restaurantCode ? f.restaurantCode + ' ' : '') + f.name)}</span>`).join('')
                    : '<span style="color:#b45309">keine zugeordnet — Zuordnung im Filial-Stammdaten-Modal («Bearbeiten» bei der Filiale)</span>'}
            </div>
            <div style="font-size:12.5px;margin-top:6px">
                <span style="font-weight:600;color:#646464">Kontakt für Lohnmeldungen:</span>
                ${[h.kontaktName, h.kontaktEmail, h.kontaktTelefon].filter(Boolean).join(' · ')
                    || '<span style="color:#b45309">nicht erfasst — in der Meldung erscheint der erste Admin-Benutzer</span>'}
            </div>
            <div id="azmBlock-${h.id}" style="margin-top:10px;padding-top:9px;border-top:1px solid rgba(60,55,48,0.12)"></div>
        </div>`).join('');
    _hsList.forEach(h => azmRender(h.id));
}

// ── Arbeitszeitmodelle (Walter 27.09.2026) ────────────────────────────────
// Swissdec meldet sie als CompanyWorkingTime; jede Person verweist auf eines.
// Die Lohnrechnung liest sie NICHT — sie bleibt bei den Wochenstunden der Filiale.
let _azmAlle = [];

async function azmLaden() {
    try {
        const r = await fetch('/api/arbeitszeitmodelle', { headers: ah() });
        _azmAlle = r.ok ? await r.json() : [];
    } catch { _azmAlle = []; }
}

function azmRender(hauptsitzId) {
    const el = document.getElementById('azmBlock-' + hauptsitzId);
    if (!el) return;
    const liste = _azmAlle.filter(m => m.hauptsitzId === hauptsitzId);
    const zeit = (m) => m.wochenstunden && m.wochenlektionen
        ? `${Number(m.wochenstunden).toFixed(2)} h + ${Number(m.wochenlektionen).toFixed(2)} Lekt./Wo`
        : m.wochenlektionen ? `${Number(m.wochenlektionen).toFixed(2)} Lektionen/Wo`
        : m.wochenstunden ? `${Number(m.wochenstunden).toFixed(2)} h/Wo` : 'keine Zeit erfasst';
    const pill = 'display:inline-flex;align-items:center;gap:6px;background:rgba(255,255,255,0.72);border:1px solid rgba(60,55,48,0.12);border-radius:12px;padding:8px 10px 8px 12px;margin:0';
    el.innerHTML = `
        <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin-bottom:10px">
            <div>
                <div style="font-weight:700;color:#3f3f3f;font-size:13px">Arbeitszeitmodelle</div>
                <div style="font-size:11.5px;color:#8b8b8b;margin-top:1px">Swissdec CompanyWorkingTime · Zuordnung am MA</div>
            </div>
            <span style="flex:1"></span>
            <button type="button" onclick="azmNeu(${hauptsitzId})"
                style="background:#3f3f3f;border:none;border-radius:10px;padding:6px 12px;font-size:12px;font-weight:600;color:#fff;cursor:pointer;font-family:inherit">+ Modell</button>
        </div>
        ${liste.length === 0
            ? `<div style="${pill};width:100%;box-sizing:border-box;font-size:12.5px;color:#8b8b8b;line-height:1.45">
                Noch keines erfasst — die Meldung nimmt ein Standardmodell aus den Wochenstunden der Filiale.
               </div>`
            : `<div style="display:flex;flex-direction:column;gap:6px">${liste.map(m => {
                const belegt = (m.mitarbeiter || 0) > 0;
                const zeitFarbe = m.meldefaehig ? '#646464' : '#b91c1c';
                return `
                <div style="${pill}">
                    <span style="background:#f3f1ec;border:1px solid rgba(60,55,48,0.10);border-radius:8px;padding:2px 8px;font-family:ui-monospace,Menlo,monospace;font-size:11px;color:#646464;flex:0 0 auto">${esc(m.kennungOderId)}</span>
                    <div style="min-width:0;flex:1 1 auto">
                        <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap">
                            <span style="font-weight:700;font-size:13px;color:#3f3f3f">${esc(m.bezeichnung)}</span>
                            ${m.isActive ? '' : '<span style="background:#fee2e2;color:#991b1b;border-radius:8px;padding:0 7px;font-size:11px;font-weight:600">inaktiv</span>'}
                        </div>
                        <div style="font-size:12px;margin-top:2px;color:${zeitFarbe}">
                            ${zeit(m)}${m.ferientageProJahr ? ` · ${Number(m.ferientageProJahr).toFixed(0)} Ferientage` : ''}
                            · <span style="color:#8b8b8b">${m.mitarbeiter} MA</span>
                        </div>
                    </div>
                    <div style="position:relative;display:inline-block;flex:0 0 auto">
                        <button type="button" class="dok-menu-btn" onclick="dokToggleMenu(event, 'azm-${m.id}')" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="dokMenu-azm-${m.id}">
                            <button type="button" class="dok-menu-item" onclick="dokCloseAllMenus();azmBearbeiten(${m.id})">Bearbeiten</button>
                            ${belegt
                                ? `<div class="dok-menu-locked" title="Zuerst MA auf ein anderes Modell umstellen">Löschen · ${m.mitarbeiter} MA verwenden es</div>`
                                : `<button type="button" class="dok-menu-item danger" onclick="dokCloseAllMenus();azmLoeschen(${m.id})">Löschen</button>`}
                        </div>
                    </div>
                </div>`;
            }).join('')}</div>`}`;
}

let _azmEditId = null;
let _azmEditHsId = null;

function _azmEnsureModal() {
    if (document.getElementById('azmModal')) return;
    const inp = 'width:100%;margin-top:3px;padding:7px 10px;border:1px solid rgba(255,255,255,0.95);border-radius:10px;font-size:13px;font-weight:700;background:#fff;box-shadow:0 2px 6px rgba(60,55,48,0.13), inset 0 1px 0 rgba(255,255,255,0.9);box-sizing:border-box;font-family:inherit;color:#3f3f3f';
    const lbl = 'display:block;font-size:11.5px;font-weight:600;color:#8b8b8b';
    const btnSec = 'background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.25);border-radius:12px;padding:8px 16px;font-size:13px;font-weight:600;color:#3f3f3f;cursor:pointer;font-family:inherit';
    const btnPri = 'background:#3f3f3f;border:none;border-radius:12px;padding:8px 18px;font-size:13px;font-weight:600;color:#fff;cursor:pointer;font-family:inherit';
    const div = document.createElement('div');
    div.id = 'azmModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;z-index:330;background:rgba(40,36,30,0.38);backdrop-filter:blur(2px)';
    div.onclick = (e) => { if (e.target === div) azmModalSchliessen(); };
    div.innerHTML = `
    <div class="modal" style="position:absolute;top:8vh;left:50%;transform:translateX(-50%);width:min(480px,94vw);border-radius:16px;box-shadow:0 25px 60px rgba(60,55,48,0.22);padding:22px 24px 18px;box-sizing:border-box" onclick="event.stopPropagation()">
        <div id="azmModalTitle" style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:6px">Arbeitszeitmodell</div>
        <div style="font-size:11.5px;color:#8b8b8b;line-height:1.45;margin-bottom:14px">
            Swissdec CompanyWorkingTime · Bezeichnung eindeutig · Stunden oder Lektionen (max. 50).
            Kein von/bis hier — Gültig-ab steht bei der Zuordnung am Mitarbeiter.
        </div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 12px">
            <label style="${lbl};grid-column:span 2">Bezeichnung<span class="pflicht-stern">*</span>
                <input id="azmBez" placeholder="z.B. Standard, Lager, Lehrpersonen" maxlength="80" style="${inp}">
            </label>
            <label style="${lbl}">Wochenstunden
                <input id="azmStd" type="number" min="0" max="50" step="0.01" placeholder="z.B. 42" style="${inp}">
            </label>
            <label style="${lbl}">Wochenlektionen
                <input id="azmLek" type="number" min="0" max="50" step="0.01" placeholder="optional" style="${inp}">
            </label>
            <label style="${lbl};grid-column:span 2">Ferientage pro Jahr
                <input id="azmFerien" type="number" min="0" max="365" step="0.5" placeholder="optional" style="${inp}">
            </label>
            <label style="grid-column:span 2;display:flex;align-items:center;gap:8px;font-size:12.5px;font-weight:500;color:#3f3f3f;cursor:pointer;margin-top:2px">
                <input type="checkbox" id="azmAktiv" checked style="width:15px;height:15px;margin:0"> aktiv
            </label>
        </div>
        <div id="azmModalFehler" style="display:none;margin-top:10px;font-size:12.5px;color:#b91c1c;line-height:1.4"></div>
        <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:16px">
            <button type="button" onclick="azmModalSchliessen()" style="${btnSec}">Abbrechen</button>
            <button type="button" onclick="azmModalSpeichern()" style="${btnPri}">Speichern</button>
        </div>
    </div>`;
    document.body.appendChild(div);
}

function azmModalSchliessen() {
    const m = document.getElementById('azmModal');
    if (m) m.style.display = 'none';
    _azmEditId = null;
    _azmEditHsId = null;
}

function azmNeu(hauptsitzId) {
    _azmEnsureModal();
    _azmEditId = null;
    _azmEditHsId = hauptsitzId;
    document.getElementById('azmModalTitle').textContent = 'Arbeitszeitmodell erfassen';
    document.getElementById('azmBez').value = '';
    document.getElementById('azmStd').value = '42';
    document.getElementById('azmLek').value = '';
    document.getElementById('azmFerien').value = '';
    document.getElementById('azmAktiv').checked = true;
    document.getElementById('azmModalFehler').style.display = 'none';
    document.getElementById('azmModal').style.display = 'block';
    setTimeout(() => document.getElementById('azmBez')?.focus(), 50);
}

function azmBearbeiten(id) {
    const m = _azmAlle.find(x => x.id === id);
    if (!m) return;
    _azmEnsureModal();
    _azmEditId = id;
    _azmEditHsId = m.hauptsitzId;
    document.getElementById('azmModalTitle').textContent = 'Arbeitszeitmodell bearbeiten';
    document.getElementById('azmBez').value = m.bezeichnung || '';
    document.getElementById('azmStd').value = m.wochenstunden != null ? Number(m.wochenstunden) : '';
    document.getElementById('azmLek').value = m.wochenlektionen != null ? Number(m.wochenlektionen) : '';
    document.getElementById('azmFerien').value = m.ferientageProJahr != null ? Number(m.ferientageProJahr) : '';
    document.getElementById('azmAktiv').checked = m.isActive !== false;
    document.getElementById('azmModalFehler').style.display = 'none';
    document.getElementById('azmModal').style.display = 'block';
    setTimeout(() => document.getElementById('azmBez')?.focus(), 50);
}

function azmModalFehler(text) {
    const el = document.getElementById('azmModalFehler');
    if (!el) return;
    el.textContent = text || '';
    el.style.display = text ? 'block' : 'none';
}

async function azmModalSpeichern() {
    const bez = (document.getElementById('azmBez')?.value || '').trim();
    const stdRaw = (document.getElementById('azmStd')?.value || '').trim();
    const lekRaw = (document.getElementById('azmLek')?.value || '').trim();
    const ferRaw = (document.getElementById('azmFerien')?.value || '').trim();
    const std = stdRaw === '' ? null : Number(stdRaw);
    const lek = lekRaw === '' ? null : Number(lekRaw);
    const fer = ferRaw === '' ? null : Number(ferRaw);

    if (!bez) { azmModalFehler('Bitte eine Bezeichnung angeben.'); return; }
    const doppel = _azmAlle.some(x =>
        x.hauptsitzId === _azmEditHsId
        && x.id !== _azmEditId
        && (x.bezeichnung || '').trim().toLowerCase() === bez.toLowerCase());
    if (doppel) { azmModalFehler(`Die Bezeichnung «${bez}» gibt es an diesem Hauptsitz schon.`); return; }
    if (std != null && (Number.isNaN(std) || std < 0 || std > 50)) {
        azmModalFehler('Wochenstunden: Zahl zwischen 0 und 50.'); return;
    }
    if (lek != null && (Number.isNaN(lek) || lek < 0 || lek > 50)) {
        azmModalFehler('Wochenlektionen: Zahl zwischen 0 und 50.'); return;
    }
    if (!(std > 0) && !(lek > 0)) {
        azmModalFehler('Wochenstunden oder Wochenlektionen angeben (mind. einer der beiden > 0).'); return;
    }

    const bestehend = _azmEditId ? _azmAlle.find(x => x.id === _azmEditId) : null;
    const dto = {
        hauptsitzId: _azmEditHsId,
        kennung: bestehend?.kennung ?? null,
        bezeichnung: bez,
        wochenstunden: std > 0 ? std : null,
        wochenlektionen: lek > 0 ? lek : null,
        ferientageProJahr: fer != null && !Number.isNaN(fer) && fer >= 0 ? fer : null,
        isActive: !!document.getElementById('azmAktiv')?.checked
    };
    await azmSpeichern(_azmEditId, dto);
}

async function azmSpeichern(id, dto) {
    const r = await fetch(id ? `/api/arbeitszeitmodelle/${id}` : '/api/arbeitszeitmodelle', {
        method: id ? 'PUT' : 'POST', headers: ah(), body: JSON.stringify(dto)
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) {
        azmModalFehler(j.message || j.error || 'Speichern fehlgeschlagen');
        showToast(j.message || j.error || 'Speichern fehlgeschlagen', 'error');
        return;
    }
    azmModalSchliessen();
    await azmLaden(); hsRender();
    showToast('Arbeitszeitmodell gespeichert', 'success');
}

async function azmLoeschen(id) {
    if (!await liquidConfirm('Dieses Arbeitszeitmodell löschen?', { title: 'Arbeitszeitmodell' })) return;
    const r = await fetch(`/api/arbeitszeitmodelle/${id}`, { method: 'DELETE', headers: ah() });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Löschen fehlgeschlagen', 'error'); return; }
    await azmLaden(); hsRender();
}

// ── Modal (ov-Standard: class="modal" bekommt den Greige-Verlauf) ──────
function _hsEnsureModal() {
    // Layout v10: Stellvertreter PLZ/Ort-Prüfung wie Hauptdaten
    const existing = document.getElementById('hsModal');
    if (existing && existing.dataset.hsLayout !== '11') existing.remove();
    if (document.getElementById('hsModal')) return;
    const inp = 'width:100%;margin-top:3px;padding:7px 10px;border:1px solid rgba(255,255,255,0.95);border-radius:10px;font-size:13px;font-weight:700;background:#fff;box-shadow:0 2px 6px rgba(60,55,48,0.13), inset 0 1px 0 rgba(255,255,255,0.9);box-sizing:border-box;font-family:inherit;color:#3f3f3f';
    const lbl = 'display:block;font-size:11.5px;font-weight:600;color:#8b8b8b';
    const tabBtn = 'border:1px solid rgba(60,55,48,0.20);background:transparent;border-radius:10px;padding:7px 12px;font-size:12.5px;font-weight:600;color:#646464;cursor:pointer;font-family:inherit';
    const btnSec = 'background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.25);border-radius:12px;padding:8px 16px;font-size:13px;font-weight:600;color:#3f3f3f;cursor:pointer;font-family:inherit';
    const btnPri = 'background:#3f3f3f;border:none;border-radius:12px;padding:8px 18px;font-size:13px;font-weight:600;color:#fff;cursor:pointer;font-family:inherit';
    const landSel = (window.SwissUid && SwissUid.landSelectHtml)
        ? SwissUid.landSelectHtml('hsLand', 'CH', inp)
        : `<select id="hsLand" style="${inp}"><option value="CH" selected>SWITZERLAND</option></select>`;
    const svLandSel = (window.SwissUid && SwissUid.landSelectHtml)
        ? SwissUid.landSelectHtml('hsSvLand', 'CH', inp)
        : `<select id="hsSvLand" style="${inp}"><option value="CH">SWITZERLAND</option></select>`;
    const div = document.createElement('div');
    div.id = 'hsModal';
    div.dataset.hsLayout = '11';
    div.style.cssText = 'display:none;position:fixed;inset:0;z-index:320;background:rgba(40,36,30,0.38);backdrop-filter:blur(2px)';
    div.innerHTML = `
    <div class="modal" style="position:absolute;top:72px;left:50%;transform:translateX(-50%);width:min(820px,96vw);height:min(calc(100vh - 84px),780px);display:flex;flex-direction:column;overflow:hidden;border-radius:16px;box-shadow:0 25px 60px rgba(60,55,48,0.22);padding:0;box-sizing:border-box">
        <div style="flex:0 0 auto;padding:22px 26px 0">
            <div id="hsModalTitle" style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:12px">Hauptsitz erfassen</div>
            <div id="hsTabBar" style="display:flex;flex-wrap:wrap;gap:6px;margin-bottom:14px">
                <button type="button" class="hs-tab-btn" data-hs-tab="hauptdaten" onclick="hsSwitchTab('hauptdaten')" style="${tabBtn}">Hauptdaten</button>
                <button type="button" class="hs-tab-btn" data-hs-tab="kontakt" onclick="hsSwitchTab('kontakt')" style="${tabBtn}">Kontaktperson</button>
                <button type="button" class="hs-tab-btn" data-hs-tab="stellvertreter" onclick="hsSwitchTab('stellvertreter')" style="${tabBtn}">Stellvertreter</button>
                <button type="button" class="hs-tab-btn" data-hs-tab="vertraege" onclick="hsSwitchTab('vertraege')" style="${tabBtn}">Vertragsregeln</button>
            </div>
        </div>
        <div id="hsTabBody" style="flex:1 1 auto;min-height:0;overflow:auto;padding:0 26px">
        <div class="hs-tab-pane" data-hs-pane="hauptdaten">
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 14px">
                <label style="${lbl};grid-column:span 2">Firmenname (Rechtseinheit)<span class="pflicht-stern">*</span><input id="hsName" placeholder="z.B. Muster AG" style="${inp}"></label>
                <label style="${lbl};grid-column:span 2">UID-BFS
                    <div style="display:flex;gap:10px;align-items:center;margin-top:3px">
                        <input id="hsUid" placeholder="CHE-XXX.XXX.XXX" style="${inp};margin-top:0;flex:1;font-family:ui-monospace,Menlo,monospace">
                        <label style="display:flex;align-items:center;gap:6px;font-size:12.5px;font-weight:500;color:#3f3f3f;white-space:nowrap;cursor:pointer">
                            <input type="checkbox" id="hsUidUnbekannt" style="width:15px;height:15px;margin:0" onchange="hsUidUnbekanntToggle()">
                            unbekannt
                        </label>
                    </div>
                    <span class="f-hint" style="display:block;margin-top:3px;font-size:11px;font-weight:400;color:#8b8b8b">Format CHE-123.456.789 — Prüfziffer beim Verlassen. «unbekannt» nur Ausnahme (XML Unknown).</span>
                </label>
                <label style="${lbl};grid-column:span 2">Zusatzzeile (Abteilung / Filialbezeichnung)<input id="hsZusatz1" placeholder="optional" style="${inp}"></label>
                <label style="${lbl};grid-column:span 2">Zusatzzeile 2 (Abteilung / Filiale)<input id="hsZusatz2" placeholder="optional" style="${inp}"></label>
                <label style="${lbl};grid-column:span 2">Zusatzzeile Postadresse<input id="hsAdressZusatz" placeholder="optional" style="${inp}"></label>
                <label style="${lbl}">Strasse / Nr. <span style="font-weight:400;color:#8b8b8b">(optional)</span><input id="hsStrasse" placeholder="Bahnhofstrasse 1" style="${inp}"></label>
                <label style="${lbl}">Postfach <span style="font-weight:400;color:#8b8b8b">(alternativ zur Strasse)</span><input id="hsPostfach" style="${inp}"></label>
                <label style="${lbl};grid-column:span 2">Lokalität (Region / Provinz)<input id="hsLokalitaet" placeholder="optional" style="${inp}"></label>
                <div style="grid-column:span 2">
                    <div style="display:grid;grid-template-columns:100px minmax(0,1fr) 90px 1fr;gap:10px;align-items:start">
                        <label style="${lbl}">PLZ<span class="pflicht-stern">*</span>
                            <input id="hsPlz" inputmode="numeric" maxlength="10" placeholder="6003" style="${inp}"
                                   data-swiss-plz data-swiss-ort="hsOrt" data-swiss-kanton="hsKanton" data-swiss-bfs="hsBfs" data-swiss-hint="hsPlzHint" data-swiss-pflicht="1">
                        </label>
                        <label style="${lbl}">Ort<span class="pflicht-stern">*</span>
                            <input id="hsOrt" placeholder="Luzern" style="${inp}">
                        </label>
                        <label style="${lbl}">Kanton
                            <input id="hsKanton" placeholder="LU" maxlength="2" style="${inp}">
                        </label>
                        <label style="${lbl}">Land<span class="pflicht-stern">*</span>
                            ${landSel}
                        </label>
                    </div>
                    <div id="hsPlzHint" class="f-hint" style="margin-top:6px;min-height:16px;font-size:12px;font-weight:400;line-height:1.4"></div>
                </div>
                <label style="${lbl}">BFS-Gemeindenummer
                    <input id="hsBfs" type="number" min="1" max="9999" readonly placeholder="aus PLZ/Ort" style="${inp};font-family:ui-monospace,Menlo,monospace;background:#f3f1ec;color:#3f3f3f;cursor:default">
                    <span class="f-hint" style="display:block;margin-top:3px;font-size:11px;font-weight:400;color:#8b8b8b">Automatisch aus dem Ortschaftsverzeichnis (PLZ/Ort) — nicht editierbar.</span>
                </label>
                <label style="${lbl};grid-column:span 2">Lohnvereinbarung
                    <select id="hsLohnvereinbarung" style="${inp}">
                        <option value="CLA-Association">Gesamtarbeitsvertrag eines Verbands</option>
                        <option value="CLA-BusinessOrGovernment">Gesamtarbeitsvertrag einer Firma oder einer öffentlichen Verwaltung</option>
                        <option value="collectiveContratOutside-CLA">Kollektive Lohnvereinbarung ausserhalb eines Gesamtarbeitsvertrags</option>
                        <option value="individualContract">Einzelarbeitsvertrag</option>
                    </select>
                    <span class="f-hint" style="display:block;margin-top:3px;font-size:11px;font-weight:400;color:#8b8b8b">Gilt für die ganze Firma. Die Mindestlöhne bleiben die Beträge des L-GAV.</span>
                </label>
                <label style="${lbl}">Buchungskreis
                    <input id="hsBuchungskreis" maxlength="40" placeholder="leer" style="${inp}" title="Ein Kreis für die ganze Firma. Nur nach Absprache mit dem BFS. Nicht pro Filiale.">
                </label>
                <label style="${lbl}">Interne Bemerkung<input id="hsBem" placeholder="optional" style="${inp}"></label>
            </div>
        </div>

        <div class="hs-tab-pane" data-hs-pane="kontakt" style="display:none">
            <div style="font-size:11.5px;color:#8b8b8b;margin:0 0 12px;line-height:1.55">
                Wer eine Rückfrage zur elektronischen Lohnmeldung bekommt. Steht im Kopf jeder
                Meldung an Ausgleichskasse, Steuerverwaltung und Statistik. Leer = es wird der
                erste Admin-Benutzer gemeldet.
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 12px">
                <label style="${lbl};grid-column:span 2">Name<input id="hsKontaktName" placeholder="Hans Muster" style="${inp}"></label>
                <label style="${lbl}">E-Mail<span class="pflicht-stern" title="Pflicht für BFS">*</span><input id="hsKontaktEmail" type="email" placeholder="hans.muster@suva.ch" style="${inp}"></label>
                <label style="${lbl}">Telefon<span class="pflicht-stern">*</span><input id="hsKontaktTel" placeholder="+41 44 444 44 44" style="${inp}"></label>
                <label style="${lbl};grid-column:span 2">Mobil<input id="hsKontaktMobil" placeholder="+41 79 409 43 33" style="${inp}">
                    <span class="f-hint" style="display:block;margin-top:3px;font-size:11px;font-weight:400;color:#8b8b8b">Speichern international (+41 …); im Swissdec-XML national (079 …).</span>
                </label>
            </div>
        </div>

        <div class="hs-tab-pane" data-hs-pane="stellvertreter" style="display:none">
            <div style="font-size:11.5px;color:#8b8b8b;margin:0 0 12px;line-height:1.55">
                Optional — z.B. Treuhandbüro, das Meldungen im Auftrag übermittelt (Swissdec Delegate).
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 12px">
                <label style="${lbl};grid-column:span 2">Name<input id="hsSvName" placeholder="Treuhand Muster AG" style="${inp}"></label>
                <label style="${lbl};grid-column:span 2">Firmeninhaber<input id="hsSvInhaber" placeholder="Vorname Nachname" style="${inp}"></label>
                <label style="${lbl};grid-column:span 2">Zusatzzeile Postadresse<input id="hsSvAdressZusatz" style="${inp}"></label>
                <label style="${lbl}">Strasse / Nr.<input id="hsSvStrasse" style="${inp}"></label>
                <label style="${lbl}">Postfach<input id="hsSvPostfach" style="${inp}"></label>
                <label style="${lbl};grid-column:span 2">Lokalität<input id="hsSvLokalitaet" style="${inp}"></label>
                <div style="grid-column:span 2">
                    <div style="display:grid;grid-template-columns:100px minmax(0,1fr);gap:10px;align-items:start">
                        <label style="${lbl}">PLZ
                            <input id="hsSvPlz" inputmode="numeric" maxlength="10" placeholder="3000" style="${inp}"
                                   data-swiss-plz data-swiss-ort="hsSvOrt" data-swiss-hint="hsSvPlzHint" data-swiss-land="hsSvLand">
                        </label>
                        <label style="${lbl}">Ort
                            <input id="hsSvOrt" placeholder="Bern" style="${inp}">
                        </label>
                    </div>
                    <div id="hsSvPlzHint" class="f-hint" style="margin-top:6px;min-height:16px;font-size:12px;font-weight:400;line-height:1.4"></div>
                </div>
                <label style="${lbl}">Land${svLandSel}</label>
                <label style="${lbl}">UID-BFS<input id="hsSvUid" placeholder="CHE-XXX.XXX.XXX" style="${inp};font-family:ui-monospace,Menlo,monospace"></label>
            </div>
        </div>

        <div class="hs-tab-pane" data-hs-pane="vertraege" style="display:none">
            <div style="font-size:11.5px;color:#8b8b8b;margin:0 0 12px;line-height:1.55">
                Gilt für alle Filialen dieser Rechtseinheit. Verträge aus easy@work, die
                davon abweichen, werden nicht importiert und erscheinen auf der Fehlerliste.
                Felder leer lassen = Standard (FIX 50–100 in Zehnerschritten, FLEX max. 17 h, MTP 17–38 h).
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 12px">
                <label style="${lbl};grid-column:span 2">Erlaubte FIX-Pensen in %
                    <input id="hsFixPensen" placeholder="50, 60, 70, 80, 90, 100" style="${inp}"></label>
                <label style="${lbl}">FLEX max. Std/Woche
                    <input id="hsFlexMax" type="number" step="0.25" min="0" placeholder="17" style="${inp}"></label>
                <label style="${lbl}">MTP Std/Woche von – bis
                    <div style="display:flex;gap:8px;align-items:center">
                        <input id="hsMtpMin" type="number" step="0.25" min="0" placeholder="17" style="${inp}">
                        <span style="color:#8b8b8b">–</span>
                        <input id="hsMtpMax" type="number" step="0.25" min="0" placeholder="38" style="${inp}">
                    </div>
                </label>
            </div>
        </div>
        </div>
        <div style="flex:0 0 auto;display:flex;gap:10px;justify-content:flex-end;padding:14px 26px 20px;border-top:1px solid rgba(60,55,48,0.12)">
            <button type="button" onclick="hsCloseModal()" style="${btnSec}">Verwerfen</button>
            <button type="button" id="hsSaveBtn" onclick="hsSave()" style="${btnPri}">Speichern</button>
        </div>
    </div>`;
    div.addEventListener('click', (e) => { if (e.target === div) hsCloseModal(); });
    document.body.appendChild(div);
    if (window.SwissUid) {
        SwissUid.feldPruefungBinden('hsName', { art: 'pflicht', label: 'Firmenname' });
        SwissUid.feldPruefungBinden('hsUid', { art: 'uid' });
        SwissUid.plzAdresseUeberall(div);
    }
    hsSwitchTab('hauptdaten');
}

function hsSwitchTab(tab) {
    const modal = document.getElementById('hsModal');
    if (!modal) return;
    modal.querySelectorAll('.hs-tab-pane').forEach(p => {
        p.style.display = p.getAttribute('data-hs-pane') === tab ? '' : 'none';
    });
    modal.querySelectorAll('.hs-tab-btn').forEach(b => {
        const aktiv = b.getAttribute('data-hs-tab') === tab;
        b.style.background = aktiv ? '#3f3f3f' : 'transparent';
        b.style.color = aktiv ? '#fff' : '#646464';
        b.style.borderColor = aktiv ? '#3f3f3f' : 'rgba(60,55,48,0.20)';
    });
}

function hsTabFuerFeld(el) {
    const pane = el?.closest?.('.hs-tab-pane');
    return pane?.getAttribute('data-hs-pane') || 'hauptdaten';
}

function hsOpenModal(id) {
    _hsEnsureModal();
    _hsEditId = id || null;
    const h = id ? _hsList.find(x => x.id === id) : null;
    document.getElementById('hsModalTitle').textContent = h ? 'Hauptsitz bearbeiten' : 'Hauptsitz erfassen';
    const set = (fid, v) => { const el = document.getElementById(fid); if (el) { el.value = v ?? ''; if (window.SwissUid) SwissUid.feldFehlerLoeschen(el); } };
    set('hsName', h?.name); set('hsUid', h?.uid);
    const unk = document.getElementById('hsUidUnbekannt');
    if (unk) unk.checked = !!h?.uidUnbekannt;
    hsUidUnbekanntToggle();
    set('hsZusatz1', h?.zusatzAbteilung); set('hsZusatz2', h?.zusatzAbteilung2);
    set('hsAdressZusatz', h?.adressZusatz);
    set('hsStrasse', h?.strasse); set('hsPostfach', h?.postfach); set('hsLokalitaet', h?.lokalitaet);
    set('hsPlz', h?.plz); set('hsOrt', h?.ort); set('hsKanton', h?.kantonCode);
    set('hsBfs', ''); set('hsBem', h?.bemerkung);
    set('hsLohnvereinbarung', h?.lohnvereinbarung || 'individualContract');
    set('hsBuchungskreis', h?.bfsBuchungskreis);
    if (window.SwissUid) SwissUid.landSelectFuellen('hsLand', h?.land || 'CH');
    else set('hsLand', h?.land || 'CH');
    set('hsFixPensen', h?.fixPensenErlaubt); set('hsFlexMax', h?.flexStundenMax);
    set('hsMtpMin', h?.mtpStundenMin);       set('hsMtpMax', h?.mtpStundenMax);
    set('hsKontaktName', h?.kontaktName); set('hsKontaktEmail', h?.kontaktEmail);
    set('hsKontaktTel', h?.kontaktTelefon); set('hsKontaktMobil', h?.kontaktMobil);
    set('hsSvName', h?.stellvertreterName); set('hsSvInhaber', h?.stellvertreterInhaber);
    set('hsSvAdressZusatz', h?.stellvertreterAdressZusatz);
    set('hsSvStrasse', h?.stellvertreterStrasse); set('hsSvPostfach', h?.stellvertreterPostfach);
    set('hsSvLokalitaet', h?.stellvertreterLokalitaet);
    set('hsSvPlz', h?.stellvertreterPlz); set('hsSvOrt', h?.stellvertreterOrt);
    if (window.SwissUid) SwissUid.landSelectFuellen('hsSvLand', h?.stellvertreterLand || 'CH');
    else set('hsSvLand', h?.stellvertreterLand || 'CH');
    set('hsSvUid', h?.stellvertreterUid);
    hsSwitchTab('hauptdaten');
    document.getElementById('hsModal').style.display = 'block';
    const plzEl = document.getElementById('hsPlz');
    if (plzEl && typeof plzEl._swissUidPruefen === 'function') plzEl._swissUidPruefen();
    hsBfsAusVerzeichnis(h?.bfsGemeindeNr);
}

/** BFS-Gemeindenummer nur aus swiss_location (PLZ + Ort) — Feld ist read-only. */
async function hsBfsAusVerzeichnis(fallback) {
    const bfsEl = document.getElementById('hsBfs');
    if (!bfsEl) return;
    const plz = (document.getElementById('hsPlz')?.value || '').trim();
    const ort = (document.getElementById('hsOrt')?.value || '').trim().toLowerCase();
    if (plz.length !== 4) {
        bfsEl.value = fallback > 0 ? fallback : '';
        return;
    }
    try {
        const res = await fetch(`/api/swiss-locations/by-plz?plz=${encodeURIComponent(plz)}`, { headers: ah() });
        if (!res.ok) { bfsEl.value = fallback > 0 ? fallback : ''; return; }
        const locs = await res.json();
        const nrs = [...new Set(locs.map(l => l.bfsNr).filter(Boolean))];
        if (nrs.length === 1) { bfsEl.value = nrs[0]; return; }
        const treffer = locs.find(l =>
            (l.ortschaftsname || '').toLowerCase().startsWith(ort)
            || (l.gemeindename || '').toLowerCase() === ort);
        if (treffer?.bfsNr) { bfsEl.value = treffer.bfsNr; return; }
        bfsEl.value = fallback > 0 ? fallback : '';
    } catch {
        bfsEl.value = fallback > 0 ? fallback : '';
    }
}

function hsUidUnbekanntToggle() {
    const unk = document.getElementById('hsUidUnbekannt')?.checked;
    const uid = document.getElementById('hsUid');
    if (!uid) return;
    uid.disabled = !!unk;
    if (unk) { uid.value = ''; if (window.SwissUid) SwissUid.feldFehlerLoeschen(uid); }
}

function hsCloseModal() {
    const m = document.getElementById('hsModal');
    if (m) m.style.display = 'none';
}

async function hsSave() {
    const val = (fid) => document.getElementById(fid)?.value?.trim() || null;
    const num = (fid) => { const v = val(fid); return v == null ? null : Number(v); };
    const bfsRaw = val('hsBfs');
    const bfsN = bfsRaw ? parseInt(bfsRaw, 10) : null;
    const uidUnbekannt = !!document.getElementById('hsUidUnbekannt')?.checked;
    const dto = {
        name: val('hsName'), uid: uidUnbekannt ? null : val('hsUid'), uidUnbekannt,
        zusatzAbteilung: val('hsZusatz1'), zusatzAbteilung2: val('hsZusatz2'),
        adressZusatz: val('hsAdressZusatz'),
        strasse: val('hsStrasse'), postfach: val('hsPostfach'), lokalitaet: val('hsLokalitaet'),
        plz: val('hsPlz'), ort: val('hsOrt'), kantonCode: val('hsKanton'),
        land: val('hsLand') || 'CH',
        bfsGemeindeNr: Number.isFinite(bfsN) && bfsN > 0 ? bfsN : null,
        bemerkung: val('hsBem'), isActive: true,
        lohnvereinbarung: val('hsLohnvereinbarung'),
        bfsBuchungskreis: val('hsBuchungskreis'),
        fixPensenErlaubt: val('hsFixPensen'),
        flexStundenMax: num('hsFlexMax'),
        mtpStundenMin:  num('hsMtpMin'),
        mtpStundenMax:  num('hsMtpMax'),
        kontaktName:    val('hsKontaktName'),
        kontaktEmail:   val('hsKontaktEmail'),
        kontaktTelefon: val('hsKontaktTel'),
        kontaktMobil:   val('hsKontaktMobil'),
        stellvertreterName: val('hsSvName'),
        stellvertreterInhaber: val('hsSvInhaber'),
        stellvertreterAdressZusatz: val('hsSvAdressZusatz'),
        stellvertreterStrasse: val('hsSvStrasse'),
        stellvertreterPostfach: val('hsSvPostfach'),
        stellvertreterLokalitaet: val('hsSvLokalitaet'),
        stellvertreterPlz: val('hsSvPlz'),
        stellvertreterOrt: val('hsSvOrt'),
        stellvertreterLand: val('hsSvLand') || 'CH',
        stellvertreterUid: val('hsSvUid'),
    };
    if (!dto.name) {
        hsSwitchTab('hauptdaten');
        showToast('Bitte den Firmennamen angeben.', 'error');
        return;
    }
    if (!dto.plz || !dto.ort) {
        hsSwitchTab('hauptdaten');
        showToast('PLZ und Ort sind Pflicht.', 'error');
        return;
    }
    // Untergrenze über Obergrenze waere eine Regel, die nie jemand erfuellen kann.
    if (dto.mtpStundenMin != null && dto.mtpStundenMax != null
        && dto.mtpStundenMin > dto.mtpStundenMax) {
        hsSwitchTab('vertraege');
        showToast('MTP: die Untergrenze darf nicht groesser als die Obergrenze sein.', 'error');
        return;
    }
    const modal = document.getElementById('hsModal');
    if (window.SwissUid && !(await SwissUid.felderPruefen(modal))) {
        const fehler = modal.querySelector('.swiss-uid-fehler, [aria-invalid="true"], .feld-fehler');
        if (fehler) hsSwitchTab(hsTabFuerFeld(fehler));
        else hsSwitchTab('hauptdaten');
        showToast('Bitte die markierten Felder korrigieren.', 'error');
        return;
    }
    const url = _hsEditId ? `/api/hauptsitze/${_hsEditId}` : '/api/hauptsitze';
    const r = await fetch(url, {
        method: _hsEditId ? 'PUT' : 'POST',
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify(dto),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen.', 'error'); return; }
    hsCloseModal();
    showToast('Hauptsitz gespeichert.', 'success');
    window._hsCache = null;
    loadHauptsitze();
}

async function hsDelete(id) {
    const h = _hsList.find(x => x.id === id);
    if (!h) return;
    const anzahl = (h.filialen || []).length;
    const frage = anzahl > 0
        ? `«${h.name}» ist ${anzahl} Filiale(n) zugeordnet. Löschen entfernt die Zuordnungen (die Filialen bleiben bestehen). Wirklich löschen?`
        : `«${h.name}» wirklich löschen?`;
    const ok = await liquidConfirm(frage, { title: 'Hauptsitz löschen', yesLabel: 'Löschen', noLabel: 'Abbrechen' });
    if (!ok) return;
    const r = await fetch(`/api/hauptsitze/${id}?force=true`, { method: 'DELETE', headers: ah() });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Löschen fehlgeschlagen.', 'error'); return; }
    showToast('Hauptsitz gelöscht.', 'success');
    window._hsCache = null;
    loadHauptsitze();
}
