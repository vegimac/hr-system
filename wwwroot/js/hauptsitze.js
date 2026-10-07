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
        : m.wochenstunden ? `${Number(m.wochenstunden).toFixed(2)} h/Wo` : '⚠ keine Zeit erfasst';
    el.innerHTML = `
        <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin-bottom:6px">
            <span style="font-weight:600;color:#646464;font-size:12.5px">Arbeitszeitmodelle (Swissdec)</span>
            <span style="flex:1"></span>
            <button onclick="azmNeu(${hauptsitzId})" style="background:rgba(255,255,255,0.6);border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:3px 11px;font-size:12px;font-weight:600;color:#3f3f3f;cursor:pointer">+ Modell</button>
        </div>
        ${liste.length === 0
            ? '<div style="font-size:12px;color:#8b8b8b">Keines erfasst — die Meldung nimmt dann ein Standardmodell aus den Wochenstunden der Filiale.</div>'
            : liste.map(m => `
                <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;font-size:12.5px;padding:3px 0">
                    <span style="background:#fff;border:1px solid rgba(60,55,48,0.14);border-radius:8px;padding:1px 8px;font-family:ui-monospace,Menlo,monospace;font-size:11.5px">${esc(m.kennungOderId)}</span>
                    <b>${esc(m.bezeichnung)}</b>
                    <span style="color:${m.meldefaehig ? '#646464' : '#b91c1c'}">${zeit(m)}</span>
                    ${m.ferientageProJahr ? `<span style="color:#8b8b8b">· ${Number(m.ferientageProJahr).toFixed(0)} Ferientage</span>` : ''}
                    <span style="color:#8b8b8b">· ${m.mitarbeiter} MA</span>
                    ${m.isActive ? '' : '<span style="background:#fee2e2;color:#991b1b;border-radius:8px;padding:0 7px;font-size:11px">inaktiv</span>'}
                    <span style="flex:1"></span>
                    <button onclick="azmBearbeiten(${m.id})" style="background:none;border:none;color:#646464;cursor:pointer;font-size:12px;text-decoration:underline">bearbeiten</button>
                    ${m.mitarbeiter === 0 ? `<button onclick="azmLoeschen(${m.id})" style="background:none;border:none;color:#b91c1c;cursor:pointer;font-size:12px;text-decoration:underline">löschen</button>` : ''}
                </div>`).join('')}`;
}

async function azmNeu(hauptsitzId) {
    const bez = prompt('Bezeichnung des Arbeitszeitmodells (z.B. «Standard», «Lager»):');
    if (!bez) return;
    const std = prompt('Wochenstunden (leer lassen, wenn nur Lektionen):', '42');
    const lek = prompt('Wochenlektionen (leer lassen, wenn nur Stunden):', '');
    await azmSpeichern(null, {
        hauptsitzId, bezeichnung: bez,
        wochenstunden: std ? Number(std) : null,
        wochenlektionen: lek ? Number(lek) : null,
        isActive: true
    });
}

async function azmBearbeiten(id) {
    const m = _azmAlle.find(x => x.id === id);
    if (!m) return;
    const bez = prompt('Bezeichnung:', m.bezeichnung);
    if (bez === null) return;
    const std = prompt('Wochenstunden:', m.wochenstunden ?? '');
    const lek = prompt('Wochenlektionen:', m.wochenlektionen ?? '');
    await azmSpeichern(id, {
        hauptsitzId: m.hauptsitzId, kennung: m.kennung, bezeichnung: bez,
        wochenstunden: std ? Number(std) : null,
        wochenlektionen: lek ? Number(lek) : null,
        ferientageProJahr: m.ferientageProJahr, isActive: m.isActive
    });
}

async function azmSpeichern(id, dto) {
    const r = await fetch(id ? `/api/arbeitszeitmodelle/${id}` : '/api/arbeitszeitmodelle', {
        method: id ? 'PUT' : 'POST', headers: ah(), body: JSON.stringify(dto)
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Speichern fehlgeschlagen', 'error'); return; }
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
    if (document.getElementById('hsModal')) return;
    const inp = 'width:100%;margin-top:3px;padding:7px 10px;border:1px solid rgba(255,255,255,0.95);border-radius:10px;font-size:13px;background:#fff;box-shadow:0 2px 6px rgba(60,55,48,0.13), inset 0 1px 0 rgba(255,255,255,0.9);box-sizing:border-box;font-family:inherit;color:#3f3f3f';
    const lbl = 'display:block;font-size:11.5px;font-weight:600;color:#8b8b8b';
    const div = document.createElement('div');
    div.id = 'hsModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;z-index:320;background:rgba(40,36,30,0.38);backdrop-filter:blur(2px)';
    div.innerHTML = `
    <div class="modal" style="position:absolute;top:50%;left:50%;transform:translate(-50%,-50%);width:min(560px,94vw);max-height:92vh;overflow:auto;border-radius:16px;box-shadow:0 25px 60px rgba(60,55,48,0.22);padding:22px 24px">
        <div id="hsModalTitle" style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:14px">Hauptsitz erfassen</div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 12px">
            <label style="${lbl};grid-column:span 2">Firmenname (Rechtseinheit)<input id="hsName" placeholder="z.B. Schaub Restaurants GmbH" style="${inp}"></label>
            <label style="${lbl};grid-column:span 2">UID<input id="hsUid" placeholder="CHE-XXX.XXX.XXX" style="${inp};font-family:ui-monospace,Menlo,monospace"></label>
            <label style="${lbl};grid-column:span 2">Strasse<input id="hsStrasse" style="${inp}"></label>
            <label style="${lbl}">PLZ / Ort
                <div style="display:flex;gap:8px">
                    <input id="hsPlz" style="${inp};width:80px;flex:none">
                    <input id="hsOrt" style="${inp}">
                </div>
            </label>
            <label style="${lbl}">Kanton<input id="hsKanton" placeholder="LU" maxlength="2" style="${inp};width:80px"></label>
            <label style="${lbl};grid-column:span 2">Bemerkung<input id="hsBem" placeholder="optional" style="${inp}"></label>
        </div>

        <!-- Vertragsregeln der Rechtseinheit (Walter 01.09.2026). Der
             easy@work-Sync prüft jeden Vertrag dagegen; was abweicht, wird
             nicht importiert. Leer = Standardwerte, nie «ungeprüft». -->
        <div style="margin-top:18px;padding-top:14px;border-top:1px solid rgba(60,55,48,0.15)">
            <div style="font-size:13px;font-weight:700;color:#3f3f3f">Vertragsregeln</div>
            <div style="font-size:11.5px;color:#8b8b8b;margin:4px 0 12px;line-height:1.55">
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
        <!-- Kontaktperson fuer elektronische Lohnmeldungen (Walter 27.09.2026).
             Sie gehoert der Rechtseinheit, nicht dem, der die Meldung erzeugt. -->
        <div style="margin-top:18px;padding-top:14px;border-top:1px solid rgba(60,55,48,0.15)">
            <div style="font-size:13px;font-weight:700;color:#3f3f3f">Kontaktperson für Lohnmeldungen</div>
            <div style="font-size:11.5px;color:#8b8b8b;margin:4px 0 12px;line-height:1.55">
                Wer eine Rückfrage zur elektronischen Lohnmeldung bekommt. Steht im Kopf jeder
                Meldung an Ausgleichskasse, Steuerverwaltung und Statistik. Leer = es wird der
                erste Admin-Benutzer gemeldet.
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 12px">
                <label style="${lbl};grid-column:span 2">Name<input id="hsKontaktName" placeholder="Vorname Nachname" style="${inp}"></label>
                <label style="${lbl}">E-Mail<input id="hsKontaktEmail" type="email" style="${inp}"></label>
                <label style="${lbl}">Telefon<input id="hsKontaktTel" placeholder="041 218 65 32" style="${inp}"></label>
            </div>
        </div>
        <div style="display:flex;gap:10px;justify-content:flex-end;margin-top:16px">
            <button onclick="hsCloseModal()" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.25);border-radius:12px;padding:8px 16px;font-size:13px;font-weight:600;color:#3f3f3f;cursor:pointer">Abbrechen</button>
            <button id="hsSaveBtn" onclick="hsSave()" style="background:#3f3f3f;border:none;border-radius:12px;padding:8px 18px;font-size:13px;font-weight:600;color:#fff;cursor:pointer">Speichern</button>
        </div>
    </div>`;
    div.addEventListener('click', (e) => { if (e.target === div) hsCloseModal(); });
    document.body.appendChild(div);
}

function hsOpenModal(id) {
    _hsEnsureModal();
    _hsEditId = id || null;
    const h = id ? _hsList.find(x => x.id === id) : null;
    document.getElementById('hsModalTitle').textContent = h ? 'Hauptsitz bearbeiten' : 'Hauptsitz erfassen';
    const set = (fid, v) => { const el = document.getElementById(fid); if (el) el.value = v ?? ''; };
    set('hsName', h?.name); set('hsUid', h?.uid); set('hsStrasse', h?.strasse);
    set('hsPlz', h?.plz); set('hsOrt', h?.ort); set('hsKanton', h?.kantonCode); set('hsBem', h?.bemerkung);
    set('hsFixPensen', h?.fixPensenErlaubt); set('hsFlexMax', h?.flexStundenMax);
    set('hsMtpMin', h?.mtpStundenMin);       set('hsMtpMax', h?.mtpStundenMax);
    set('hsKontaktName', h?.kontaktName); set('hsKontaktEmail', h?.kontaktEmail); set('hsKontaktTel', h?.kontaktTelefon);
    document.getElementById('hsModal').style.display = 'block';
}

function hsCloseModal() {
    const m = document.getElementById('hsModal');
    if (m) m.style.display = 'none';
}

async function hsSave() {
    const val = (fid) => document.getElementById(fid)?.value?.trim() || null;
    const num = (fid) => { const v = val(fid); return v == null ? null : Number(v); };
    const dto = {
        name: val('hsName'), uid: val('hsUid'), strasse: val('hsStrasse'),
        plz: val('hsPlz'), ort: val('hsOrt'), kantonCode: val('hsKanton'),
        bemerkung: val('hsBem'), isActive: true,
        fixPensenErlaubt: val('hsFixPensen'),
        flexStundenMax: num('hsFlexMax'),
        mtpStundenMin:  num('hsMtpMin'),
        mtpStundenMax:  num('hsMtpMax'),
        kontaktName:    val('hsKontaktName'),
        kontaktEmail:   val('hsKontaktEmail'),
        kontaktTelefon: val('hsKontaktTel'),
    };
    // Untergrenze über Obergrenze waere eine Regel, die nie jemand erfuellen kann.
    if (dto.mtpStundenMin != null && dto.mtpStundenMax != null
        && dto.mtpStundenMin > dto.mtpStundenMax) {
        showToast('MTP: die Untergrenze darf nicht groesser als die Obergrenze sein.', 'error');
        return;
    }
    if (!dto.name) { showToast('Bitte den Firmennamen angeben.', 'error'); return; }
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
