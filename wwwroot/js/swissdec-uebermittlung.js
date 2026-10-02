// ═══════════════════════════════════════════════════════════════════════════
//  SWISSDEC ÜBERMITTLUNG — Foundation F05 / F06 / F08 (30.09.2026)
//  Declare (Monat / Jahr AHV) → Status mit JobKey → Synchronize je Adressat,
//  dazu SubscribeOrganization. Wird vom Kommunikations-Test (swissdec-komm.js)
//  in Schritt 2 eines Prüfpunkts gezeichnet (Katalog-Eintrag mit «ueb»).
//  Server: /api/elm/uebermittlung/* (nur Super-Admin), Ergebnis in #kommResult.
// ═══════════════════════════════════════════════════════════════════════════

let _uebCfg = null;
let _uebPunkt = null;
let _uebVorgaenge = [];
let _uebVorschau = null;
let _uebTimer = null;
let _uebBeschaeftigt = false;

const _uebDomains = ['UVG-LAA', 'UVGZ-LAAC', 'KTG-AMC', 'BVG-LPP'];

function _uebForm() {
    try { return JSON.parse(localStorage.getItem('uebForm') || '{}'); } catch (_) { return {}; }
}
function _uebFormMerken(f) {
    try { localStorage.setItem('uebForm', JSON.stringify(Object.assign(_uebForm(), f))); } catch (_) { /* egal */ }
}

function _uebDatum(iso, mitZeit) {
    if (!iso) return '—';
    const d = new Date(iso);
    if (isNaN(d)) return esc(iso);
    return mitZeit ? d.toLocaleString('de-CH') : d.toLocaleDateString('de-CH');
}

/** Einstieg aus kommDetailZeichnen: Formular + Liste für den gewählten Prüfpunkt. */
function uebInit(punkt) {
    _uebPunkt = punkt;
    _uebCfg = punkt.ueb || {};
    _uebVorschau = null;
    const el = document.getElementById('uebPanel');
    if (!el) return;
    el.innerHTML = (_uebCfg.modus === 'subscribe' ? _uebSubscribeHtml() : _uebDeclareHtml())
        + `<div class="ueb-liste-kopf"><div class="komm-schritt-titel">Übermittlungen</div>
             <button type="button" class="komm-btn-sekundaer ueb-klein" onclick="uebListeLaden()">↻ neu laden</button></div>
           <div id="uebListe" class="ueb-liste"><span class="komm-klein">⏳ …</span></div>`;
    uebListeLaden();
}

// ── Formulare ─────────────────────────────────────────────────────────────

function _uebDeclareHtml() {
    const f = _uebForm();
    const art = _uebCfg.art || f.art || 'monthly';
    const heute = new Date();
    const jahr = f.jahr || (art === 'annual' ? heute.getFullYear() - 1 : heute.getFullYear());
    const monat = f.monat || (heute.getMonth() || 12);
    return `
    <div class="ueb-form">
        <label>Meldung
            <select id="uebArt" onchange="uebArtGewechselt()">
                <option value="monthly" ${art === 'monthly' ? 'selected' : ''}>Monatsmeldung (QST + Statistik)</option>
                <option value="annual" ${art === 'annual' ? 'selected' : ''}>Jahresmeldung AHV</option>
            </select></label>
        <label>Jahr <input type="number" id="uebJahr" value="${esc(String(jahr))}" min="2020" max="2100"></label>
        <label id="uebMonatWrap" style="${art === 'annual' ? 'display:none' : ''}">Monat
            <input type="number" id="uebMonat" value="${esc(String(monat))}" min="1" max="12"></label>
        <label class="ueb-check"><input type="checkbox" id="uebTestCase" checked> TestCase</label>
        <label class="ueb-check"><input type="checkbox" id="uebDoppelt" checked> doppelt signieren (ERP + SUA)</label>
        <label class="ueb-breit">Ersatzmeldung für DeclarationID ${_uebCfg.substitution ? '' : '<span class="komm-klein">(leer = normale Meldung)</span>'}
            <input type="text" id="uebSubstitution" placeholder="DeclarationID der ersetzten Meldung"></label>
        ${_uebCfg.requestIdProbe ? `<label class="ueb-check ueb-breit ueb-probe"><input type="checkbox" id="uebRequestIdAlt">
            RequestID der letzten Anfrage nochmals verwenden (Vorführung: OneCrew muss das Senden verweigern)</label>` : ''}
    </div>
    <div id="uebAdressaten" class="ueb-adressaten"></div>
    <div class="komm-knoepfe">
        <button type="button" class="komm-btn-sekundaer" onclick="uebAdressatenLaden()">Adressaten anzeigen</button>
        <button type="button" class="komm-btn-primaer" id="uebSendenBtn" onclick="uebDeclare()">Meldung senden</button>
    </div>`;
}

function uebArtGewechselt() {
    const art = document.getElementById('uebArt')?.value;
    const m = document.getElementById('uebMonatWrap');
    if (m) m.style.display = art === 'annual' ? 'none' : '';
    _uebVorschau = null;
    const a = document.getElementById('uebAdressaten');
    if (a) a.innerHTML = '';
}

function _uebWert(id) { return (document.getElementById(id)?.value || '').trim(); }

function _uebSubscribeHtml() {
    const v = id => esc(_uebWert(id));
    const dom = _uebWert('suaDomain') || 'UVG-LAA';
    return `
    <div class="ueb-form">
        <label>UID <input type="text" id="uebSubUid" value="${v('suaUid')}" placeholder="CHE-999.999.996"></label>
        <label>Firma <input type="text" id="uebSubFirma" value="${v('suaFirma')}"></label>
        <label>Kontakt <input type="text" id="uebSubKontakt" value="${v('suaKontakt')}"></label>
        <label>PLZ <input type="text" id="uebSubPlz" value="${v('suaPlz')}"></label>
        <label>Ort <input type="text" id="uebSubOrt" value="${v('suaOrt')}"></label>
        <label>Adressat (AddresseeIdentification) <input type="text" id="uebSubAdr" value="${v('suaAddressee') || '1234'}"></label>
        <label>Versicherungszweig
            <select id="uebSubDomain">${_uebDomains.map(d => `<option ${d === dom ? 'selected' : ''}>${d}</option>`).join('')}</select></label>
        <label class="ueb-check"><input type="checkbox" id="uebSubTestCase" checked> TestCase</label>
        <label class="ueb-check"><input type="checkbox" id="uebSubDoppelt" checked> doppelt signieren (ERP + SUA)</label>
    </div>
    <div class="komm-knoepfe">
        <button type="button" class="komm-btn-primaer" onclick="uebSubscribe()">Anmelden (SubscribeOrganization)</button>
    </div>`;
}

async function uebAdressatenLaden() {
    const el = document.getElementById('uebAdressaten');
    if (!el) return null;
    const art = _uebWert('uebArt') || 'monthly';
    const jahr = parseInt(_uebWert('uebJahr'), 10);
    const monat = parseInt(_uebWert('uebMonat'), 10);
    _uebFormMerken({ art, jahr, monat });
    el.innerHTML = '<span class="komm-klein">⏳ Meldung wird aufgebaut…</span>';
    try {
        const q = new URLSearchParams({ art, jahr: String(jahr) });
        if (art === 'monthly') q.set('monat', String(monat));
        const r = await fetch('/api/elm/uebermittlung/vorschau?' + q, { headers: ah() });
        const j = await r.json().catch(() => null);
        if (!r.ok) throw new Error(j?.message || 'HTTP ' + r.status);
        _uebVorschau = j;
        const probleme = [...(j.xsdFehler || []).map(x => '✗ Schema: ' + x), ...(j.warnungen || []).map(x => '⚠ ' + x)];
        el.innerHTML = `<div class="komm-schritt-titel">${esc(j.titel)} · ${esc(j.firmenname || '')} ${esc(j.uid || '')}</div>`
            + (j.adressaten.length ? j.adressaten.map((a, i) => `
                <label class="ueb-check ueb-adressat"><input type="checkbox" data-adr="${i}" ${a.verarbeiten ? 'checked' : ''}>
                    <b>${esc(a.identification)}</b> <span class="komm-klein">${esc(a.domain || '')} · ${esc(a.addresseeId)}</span></label>`).join('')
                : '<span class="komm-rot">Keine Adressaten — die Meldung ist leer.</span>')
            + (j.adressaten.length > 1 ? '<div class="komm-klein">Abgewählte Adressaten gehen mit ProcessByDistributor=false mit.</div>' : '')
            + ((j.plausibilitaet || []).length
                ? `<div class="ueb-meldung ueb-rot"><b>✗ Plausibilitätsprüfung: Diese Meldung würde OneCrew nicht senden.</b>${_uebHinweisZeilen(j.plausibilitaet)}</div>` : '')
            + (probleme.length ? `<div class="komm-hinweis">${probleme.slice(0, 8).map(esc).join('<br>')}</div>` : '');
        return j;
    } catch (e) {
        el.innerHTML = `<span class="komm-rot">${esc(e.message)}</span>`;
        return null;
    }
}

function _uebAuswahl() {
    if (!_uebVorschau) return null;
    const aus = {};
    document.querySelectorAll('#uebAdressaten input[data-adr]').forEach(cb => {
        const a = _uebVorschau.adressaten[+cb.dataset.adr];
        if (a) aus[a.addresseeId] = cb.checked;
    });
    return aus;
}

// ── Aufrufe ───────────────────────────────────────────────────────────────

/** Während einer Anfrage sind alle Knöpfe der Übermittlung gesperrt (kein Doppelklick). */
function _uebSperren(an) {
    _uebBeschaeftigt = an;
    document.getElementById('uebPanel')?.classList.toggle('ueb-beschaeftigt', an);
}

async function _uebPost(pfad, body, warteText) {
    if (_uebBeschaeftigt) return { fehler: 'Es läuft noch eine Anfrage.' };
    _uebSperren(true);
    const out = document.getElementById('kommResult');
    if (out) {
        out.innerHTML = `<div class="ueb-meldung ueb-warte"><b>⏳ ${esc(warteText)}</b> <span class="komm-klein">Bitte warten — nicht nochmals klicken.</span></div>`;
        out.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
    try {
        const r = await fetch('/api/elm/uebermittlung/' + pfad, {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(_suaZielBody(body))
        });
        const j = await r.json().catch(() => null);
        if (!r.ok) {
            const f = j?.message || j?.error || 'HTTP ' + r.status;
            if (out) out.innerHTML = `<div class="ueb-meldung ueb-rot"><b>✗ ${esc(f)}</b></div>`;
            return { fehler: f };
        }
        _uebZeigeErgebnis(j);
        return j;
    } catch (e) {
        if (out) out.innerHTML = `<div class="ueb-meldung ueb-rot"><b>Verbindungsfehler: ${esc(e.message)}</b></div>`;
        return { fehler: e.message };
    } finally {
        _uebSperren(false);
    }
}

async function _uebVersuchMerken(label, j) {
    if (!_uebPunkt || !j) return;
    const kurz = j.fehler ? '✗ ' + j.fehler : (j.ok ? '' : '✗ ') + (j.meldung || '');
    await kommSpeichern(_uebPunkt.id, { letzterVersuch: `${label}: ${kurz}` });
}

async function uebDeclare() {
    if (_uebBeschaeftigt) return;
    if (!_uebVorschau && !(await uebAdressatenLaden())) return;
    const art = _uebWert('uebArt') || 'monthly';
    const alt = !!document.getElementById('uebRequestIdAlt')?.checked;
    const sub = _uebWert('uebSubstitution');
    const frage = alt ? 'Meldung mit der RequestID der letzten Anfrage senden? OneCrew muss das verweigern — es geht nichts an den Distributor.'
        : `${_uebVorschau.titel} an den Swissdec-Testdistributor senden${sub ? ' (Ersatzmeldung für ' + sub + ')' : ''}?`;
    if (!(await liquidConfirm(frage, { title: 'Meldung senden', yesLabel: 'Senden', noLabel: 'Abbrechen' }))) return;
    const btn = document.getElementById('uebSendenBtn');
    if (btn) btn.disabled = true;
    try {
        const j = await _uebPost('declare', {
            art, jahr: parseInt(_uebWert('uebJahr'), 10),
            monat: art === 'monthly' ? parseInt(_uebWert('uebMonat'), 10) : null,
            testCase: !!document.getElementById('uebTestCase')?.checked,
            substitution: sub || null,
            adressaten: _uebAuswahl(),
            doppeltSignieren: !!document.getElementById('uebDoppelt')?.checked,
            requestIdWiederverwenden: alt,
        }, 'Meldung wird gesendet…');
        await _uebVersuchMerken(alt ? 'Declare (RequestID wiederverwendet)' : 'Declare', j);
        uebListeLaden();
    } finally { if (btn) btn.disabled = false; }
}

async function uebSubscribe() {
    if (_uebBeschaeftigt) return;
    if (!(await liquidConfirm('Anmeldung (SubscribeOrganization) an den Swissdec-Testdistributor senden?',
        { title: 'Anmelden', yesLabel: 'Senden', noLabel: 'Abbrechen' }))) return;
    const j = await _uebPost('subscribe', {
        uid: _uebWert('uebSubUid'), firmenname: _uebWert('uebSubFirma'), kontakt: _uebWert('uebSubKontakt'),
        plz: _uebWert('uebSubPlz'), ort: _uebWert('uebSubOrt'),
        addresseeIdentification: _uebWert('uebSubAdr'), domain: _uebWert('uebSubDomain'),
        testCase: !!document.getElementById('uebSubTestCase')?.checked,
        doppeltSignieren: !!document.getElementById('uebSubDoppelt')?.checked,
    }, 'Anmeldung wird gesendet…');
    await _uebVersuchMerken('SubscribeOrganization', j);
    uebListeLaden();
}

async function uebStatus(vi) {
    const v = _uebVorgaenge[vi];
    if (!v || _uebBeschaeftigt) return;
    const j = await _uebPost('status', { vorgangId: v.id }, 'Status wird abgefragt…');
    await _uebVersuchMerken('GetStatus', j);
    uebListeLaden();
}

async function uebSync(vi, ai, extra) {
    const v = _uebVorgaenge[vi];
    const a = v?.adressaten?.[ai];
    if (!a || _uebBeschaeftigt) return;
    if (extra?.abmelden && !(await liquidConfirm(`Anmeldung ${a.identification} beenden (Unsubscribe)?`,
        { title: 'Abmelden', yesLabel: 'Abmelden', noLabel: 'Abbrechen' }))) return;
    const j = await _uebPost('synchronize', Object.assign({ vorgangId: v.id, addresseeId: a.addresseeId }, extra || {}),
        extra?.antworten ? 'Antwort wird gesendet…' : 'Synchronisieren…');
    await _uebVersuchMerken(extra?.antworten ? 'Dialog-Antwort' : extra?.abmelden ? 'Unsubscribe' : 'Synchronize', j);
    uebListeLaden();
}

async function uebDialogSenden(vi, ai, si) {
    const s = _uebVorgaenge[vi]?.adressaten?.[ai]?.stories?.[si];
    if (!s?.dialog) return;
    const werte = {};
    s.dialog.absaetze.filter(p => p.istFrage).forEach(p => {
        const el = document.getElementById(`uebDlg-${vi}-${ai}-${si}-${p.id}`);
        if (el) werte[p.id] = el.value.trim() || null;
    });
    await uebSync(vi, ai, { antworten: [{ storyId: s.storyId, werte }] });
}

async function uebLoeschen(vi) {
    const v = _uebVorgaenge[vi];
    if (!v || _uebBeschaeftigt) return;
    if (!(await liquidConfirm(`«${v.titel}» aus der Liste entfernen? Beim Distributor ändert sich nichts, die Archivdateien bleiben.`,
        { title: 'Entfernen', yesLabel: 'Entfernen', noLabel: 'Abbrechen' }))) return;
    const r = await fetch('/api/elm/uebermittlung/' + encodeURIComponent(v.id), { method: 'DELETE', headers: ah() });
    if (!r.ok) {
        const j = await r.json().catch(() => null);
        const out = document.getElementById('kommResult');
        if (out) out.innerHTML = `<div class="ueb-meldung ueb-rot"><b>✗ ${esc(j?.message || 'HTTP ' + r.status)}</b></div>`;
    }
    uebListeLaden();
}

// ── Ergebnis ──────────────────────────────────────────────────────────────

function _uebHinweisZeilen(liste) {
    return (liste || []).map(h => `<div class="ueb-hinweis-zeile ueb-h-${esc((h.art || 'Info').toLowerCase())}">
        <b>${esc(h.art || 'Info')}${h.code ? ' ' + esc(h.code) : ''}</b>${h.stufe ? ` <span class="komm-klein">(${esc(h.stufe)})</span>` : ''}
        ${h.text ? ' — ' + esc(h.text) : ''}${h.storyId ? ` <span class="komm-klein">Story ${esc(h.storyId)}</span>` : ''}</div>`).join('');
}

function _uebZeigeErgebnis(j) {
    const out = document.getElementById('kommResult');
    if (!out) return;
    const kopf = `<div class="ueb-meldung ${j.ok ? 'ueb-gruen' : 'ueb-rot'}"><b>${j.ok ? '✓' : '✗'} ${esc(j.meldung || '')}</b>
        ${j.faultText ? `<div style="margin-top:4px">${esc(j.faultText)}</div>` : ''}
        ${_uebHinweisZeilen(j.hinweise)}</div>`;
    const warn = (j.warnungen || []).length
        ? `<div class="komm-hinweis">${j.warnungen.map(w => '⚠ ' + esc(w)).join('<br>')}</div>` : '';
    out.innerHTML = kopf + warn + '<div id="uebTechnik"></div>';
    if (j.ergebnis) _suaZeigeErgebnis({ ergebnis: j.ergebnis }, 'uebTechnik');
}

// ── Liste der Übermittlungen ──────────────────────────────────────────────

async function uebListeLaden() {
    try {
        const r = await fetch('/api/elm/uebermittlung', { headers: ah(), cache: 'no-store' });
        _uebVorgaenge = r.ok ? await r.json() : [];
    } catch (_) { _uebVorgaenge = []; }
    uebListeZeichnen();
}

function _uebWarteSek(v) {
    if (!v.letzteStatusAbfrage) return 0;
    const vergangen = (Date.now() - new Date(v.letzteStatusAbfrage).getTime()) / 1000;
    return Math.max(0, Math.ceil(10 - vergangen));
}

const _uebZustand = {
    Success: ['ueb-z-ok', 'erfolgreich'],
    Error: ['ueb-z-rot', 'Fehler'],
    Ignored: ['ueb-z-grau', 'nicht verarbeitet (abgewählt)'],
    Processing: ['ueb-z-warn', 'in Bearbeitung'],
    offen: ['ueb-z-grau', 'noch kein Status'],
};

function uebListeZeichnen() {
    const el = document.getElementById('uebListe');
    if (!el) return;
    clearTimeout(_uebTimer);
    const modus = _uebCfg?.modus === 'subscribe' ? 'subscribe' : 'declare';
    const liste = _uebVorgaenge.map((v, vi) => ({ v, vi }))
        .filter(x => modus === 'subscribe' ? x.v.art === 'subscribe' : x.v.art !== 'subscribe');
    if (!liste.length) {
        el.innerHTML = `<span class="komm-klein">Noch keine ${modus === 'subscribe' ? 'Anmeldung' : 'Meldung'} gesendet.</span>`;
        return;
    }
    let warten = 0;
    const offen = _uebOffen();
    el.innerHTML = liste.map(({ v, vi }, i) => {
        const w = _uebWarteSek(v);
        if (w > 0 && !v.jobFinished) warten = Math.max(warten, w);
        const statusKnopf = v.art === 'subscribe' ? '' : v.jobFinished
            ? '<span class="komm-klein">JobFinished — keine Statusabfrage mehr</span>'
            : `<button type="button" class="komm-btn-primaer ueb-klein" ${w > 0 ? 'disabled' : ''} onclick="uebStatus(${vi})">
                   Status abfragen${w > 0 ? ` (in ${w} s)` : ''}</button>`;
        const istOffen = v.id in offen ? offen[v.id] : i === 0;
        const chips = v.adressaten.map(a => {
            const [klasse, text] = a.state ? [_uebStateKlasse(a.state), a.state] : (_uebZustand[a.zustand] || ['ueb-z-grau', a.zustand]);
            return `<span class="ueb-z ${klasse}">${esc(a.identification)} · ${esc(text)}</span>`;
        }).join(' ');
        return `<details class="ueb-vorgang" data-id="${esc(v.id)}" ${istOffen ? 'open' : ''} ontoggle="_uebOffenMerken(this)">
            <summary class="ueb-vorgang-kopf">
                <div><b>${esc(v.titel)}</b> ${chips}
                    <div class="komm-klein">${_uebDatum(v.gesendet, true)}${v.jobKey ? ` · JobKey <code>${esc(v.jobKey)}</code>` : ''}</div></div>
            </summary>
            <div class="ueb-vorgang-body">
                <div class="ueb-vorgang-leiste">
                    <div class="komm-knoepfe">${statusKnopf}
                        <button type="button" class="komm-btn-sekundaer ueb-klein" onclick="uebLoeschen(${vi})">Entfernen</button></div>
                    <div class="komm-klein">RequestID <code>${esc(v.requestId)}</code>${v.substitution ? ` · ersetzt <code>${esc(v.substitution)}</code>` : ''}
                        ${v.doppeltSigniert ? ' · doppelt signiert' : ''}${v.statusAbfragen ? ` · ${v.statusAbfragen}× Status` : ''}</div>
                </div>
                ${v.adressaten.map((a, ai) => _uebAdressatHtml(v, vi, a, ai)).join('')}
                <details class="ueb-protokoll"><summary>Protokoll (${v.protokoll.length}) · Anfrage und Antwort je Schritt</summary>
                    ${v.protokoll.map(_uebProtokollZeile).join('')}
                </details>
            </div>
        </details>`;
    }).join('');
    if (warten > 0) _uebTimer = setTimeout(uebListeZeichnen, warten * 1000 + 200);
}

function _uebOffen() {
    try { return JSON.parse(localStorage.getItem('uebOffen') || '{}'); } catch (_) { return {}; }
}
function _uebOffenMerken(el) {
    const o = _uebOffen();
    o[el.dataset.id] = el.open;
    try { localStorage.setItem('uebOffen', JSON.stringify(o)); } catch (_) { /* egal */ }
}

function _uebStateKlasse(state) {
    if (state === 'Finished' || state === 'subscribed') return 'ueb-z-ok';
    if (state === 'Rejected') return 'ueb-z-rot';
    if (state === 'closed') return 'ueb-z-grau';
    return 'ueb-z-warn';
}

// ── Protokoll mit Archiv (F04) ────────────────────────────────────────────

function _uebProtokollZeile(p) {
    const link = (name, label) => name
        ? `<button type="button" class="ueb-archiv-link" onclick="uebArchivZeigen(this, '${esc(name)}')">${label}</button>` : '';
    return `<div class="ueb-prot-zeile">
        <div><span class="komm-klein">${_uebDatum(p.zeit, true)} · ${esc(p.schritt)}</span> ${esc(p.text)}
            ${link(p.archivAnfrage, 'Anfrage')}${link(p.archivAntwort, 'Antwort')}</div>
        <div class="ueb-archiv-inhalt"></div>
    </div>`;
}

/** Signierte Klartext-XML ohne Leerraum → eingerückt, nur für die Anzeige. */
function _uebXmlHuebsch(xml) {
    const teile = xml.replace(/>\s*</g, '>\n<').split('\n');
    let tiefe = 0;
    return teile.map(t => {
        if (/^<\//.test(t)) tiefe = Math.max(0, tiefe - 1);
        const zeile = '  '.repeat(tiefe) + t;
        if (/^<[^!?\/][^>]*>$/.test(t) && !/\/>$/.test(t)) tiefe++;
        return zeile;
    }).join('\n');
}

async function uebArchivZeigen(btn, name) {
    const box = btn.closest('.ueb-prot-zeile')?.querySelector('.ueb-archiv-inhalt');
    if (!box) return;
    if (box.dataset.name === name) { box.innerHTML = ''; box.dataset.name = ''; return; }
    box.dataset.name = name;
    box.innerHTML = '<span class="komm-klein">⏳ …</span>';
    try {
        const r = await fetch('/api/elm/archiv/' + encodeURIComponent(name), { headers: ah(), cache: 'no-store' });
        const text = await r.text();
        if (!r.ok) throw new Error(r.status === 404 ? 'Archivdatei nicht gefunden.' : 'HTTP ' + r.status);
        box.innerHTML = `<div class="ueb-archiv-kopf"><code>${esc(name)}</code>
                <button type="button" class="komm-btn-sekundaer ueb-klein" onclick="uebArchivSpeichern('${esc(name)}')">Herunterladen</button></div>
            <pre class="komm-xml">${esc(_uebXmlHuebsch(text))}</pre>`;
    } catch (e) {
        box.innerHTML = `<span class="komm-rot">${esc(e.message)}</span>`;
    }
}

async function uebArchivSpeichern(name) {
    const r = await fetch('/api/elm/archiv/' + encodeURIComponent(name), { headers: ah(), cache: 'no-store' });
    if (!r.ok) return;
    await saveBlobAsk(await r.blob(), name);
}

function _uebAdressatHtml(v, vi, a, ai) {
    const [klasse, text] = _uebZustand[a.zustand] || ['ueb-z-grau', a.zustand];
    const syncBar = a.zustand === 'Success';
    const knoepfe = !syncBar ? '' : `
        <button type="button" class="komm-btn-sekundaer ueb-klein" onclick="uebSync(${vi}, ${ai})">Synchronisieren${a.zuQuittieren ? ` (${a.zuQuittieren} quittieren)` : ''}</button>
        ${v.art === 'subscribe' && a.state !== 'closed' ? `<button type="button" class="komm-btn-sekundaer ueb-klein" onclick="uebSync(${vi}, ${ai}, { abmelden: true })">Abmelden</button>` : ''}`;
    const zeilen = [];
    if (a.fallId) zeilen.push(`${v.art === 'subscribe' ? 'SubscriptionID' : 'DeclarationID'} <code>${esc(a.fallId)}</code>${a.testCaseBestaetigt ? ' · TestCase bestätigt' : ''}`);
    if (a.key || a.password) zeilen.push(`Key <code class="ueb-roh">${esc(a.key || '')}</code> · Passwort <code class="ueb-roh">${esc(a.password || '')}</code>`);
    if (a.state) zeilen.push(`State: <b>${esc(a.stateText || a.state)}</b>`);
    if (a.fehler) zeilen.push(`<span class="komm-rot">${esc(a.fehler)}</span>${a.fehlerCode ? ` <span class="komm-klein">(${esc(a.fehlerCode)})</span>` : ''}${a.fehlerDetail ? `<div class="komm-klein">${esc(a.fehlerDetail)}</div>` : ''}`);
    if (a.wartung) zeilen.push(`Geplante Wartung: ${esc(a.wartung)}`);
    if (a.verfuegbar?.length) zeilen.push(`Verfügbar: ${a.verfuegbar.map(esc).join(', ')}`);
    if (a.unterdrueckteInstitutionStories?.length || a.unterdrueckteSenderStories?.length)
        zeilen.push(`<span class="komm-klein">Unterdrückt: Empfänger ${a.unterdrueckteInstitutionStories.map(esc).join(', ') || '—'} · eigene ${a.unterdrueckteSenderStories.map(esc).join(', ') || '—'}</span>`);
    if (a.ausstehend?.length)
        zeilen.push(`<span class="komm-klein">Eigene Antwort(en) noch nicht quittiert: ${a.ausstehend.map(g => esc(g.storyId) + ` (${g.gesendet}× gesendet)`).join(', ')} — gehen beim nächsten Synchronisieren nochmals mit.</span>`);
    return `<div class="ueb-adr">
        <div class="ueb-adr-kopf">
            <div><b>${esc(a.identification)}</b> <span class="komm-klein">${esc(a.domain || '')}${a.institutionName ? ' · ' + esc(a.institutionName) : ''}</span>
                <span class="ueb-z ${klasse}">${esc(text)}</span></div>
            <div class="komm-knoepfe">${knoepfe}</div>
        </div>
        ${zeilen.map(z => `<div class="ueb-adr-zeile">${z}</div>`).join('')}
        ${_uebHinweisZeilen(a.hinweise)}
        ${!a.completion ? '' : (a.state === 'Finished' || a.state === 'closed')
            ? '<div class="ueb-adr-zeile komm-klein">✓ Completion freigegeben — Fall abgeschlossen.</div>'
            : _uebCompletionHtml(a.completion)}
        ${a.stories.map((s, si) => _uebStoryHtml(vi, ai, s, si)).join('')}
    </div>`;
}

function _uebCompletionHtml(c) {
    return `<div class="ueb-completion">
        <div class="komm-schritt-titel">Completion — Freigabe beim Empfänger</div>
        <a href="${esc(c.aufrufUrl)}" target="_blank" rel="noopener noreferrer">🔗 Link öffnen</a>
        <span class="komm-klein">gültig bis ${_uebDatum(c.ablauf, true)}</span>
        <div class="ueb-adr-zeile">Key <code class="ueb-roh">${esc(c.key || '')}</code> · Passwort <code class="ueb-roh">${esc(c.password || '')}</code>
            ${c.keyAusFall ? '<span class="komm-klein">(aus den Credentials des Falls)</span>' : ''}</div>
        <div class="komm-klein ueb-url">${esc(c.aufrufUrl)}</div>
    </div>`;
}

function _uebStoryHtml(vi, ai, s, si) {
    const quitt = s.quittiert ? '<span class="ueb-z ueb-z-ok">quittiert</span>' : '<span class="ueb-z ueb-z-warn">wird quittiert</span>';
    const doppelt = s.empfangszaehler > 1 ? ` <span class="komm-klein">${s.empfangszaehler}× empfangen</span>` : '';
    const kopf = `<div class="ueb-story-kopf"><b>${esc(s.art)}</b> <span class="komm-klein">${esc(s.storyId)} · ${_uebDatum(s.empfangen, true)}</span> ${quitt}${doppelt}</div>`;
    if (!s.dialog) {
        return `<div class="ueb-story">${kopf}
            <details><summary class="komm-klein">XML</summary><pre class="komm-xml">${esc(s.xml)}</pre></details></div>`;
    }
    return `<div class="ueb-story ueb-dialog">${kopf}${_uebDialogHtml(vi, ai, s, si)}</div>`;
}

function _uebDialogWert(typ, wert) {
    if (wert == null) return '';
    if (typ === 'Boolean') return wert === 'true' ? 'ja' : wert === 'false' ? 'nein' : wert;
    if (typ === 'YesNoUnknown') return { yes: 'ja', no: 'nein', unknown: 'unbekannt' }[wert] || wert;
    // xs:date / xs:dateTime dürfen eine Zeitzone tragen — angezeigt wird die Wanduhrzeit wie geliefert.
    const d = typ === 'Date' && /^(\d{4})-(\d{2})-(\d{2})(Z|[+-]\d{2}:\d{2})?$/.exec(wert);
    if (d) return `${d[3]}.${d[2]}.${d[1]}`;
    const dt = typ === 'DateTime' && /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?(?:\.\d+)?(Z|[+-]\d{2}:\d{2})?$/.exec(wert);
    if (dt) return `${dt[3]}.${dt[2]}.${dt[1]} ${dt[4]}:${dt[5]}${dt[6] ? ':' + dt[6] : ''}`;
    return wert;
}

function _uebDialogFeld(id, p, gesperrt) {
    const vor = _uebDialogWert(p.frageTyp, p.antwort ?? p.default);
    const dis = gesperrt ? 'disabled' : '';
    const opt = (werte) => `<select id="${id}" ${dis}><option value=""></option>${werte.map(w =>
        `<option ${w === vor ? 'selected' : ''}>${w}</option>`).join('')}</select>`;
    switch (p.frageTyp) {
        case 'Boolean': return opt(['ja', 'nein']);
        case 'YesNoUnknown': return opt(['ja', 'nein', 'unbekannt']);
        case 'Date': return `<input type="text" id="${id}" value="${esc(vor)}" placeholder="TT.MM.JJJJ" ${dis}>`;
        case 'DateTime': return `<input type="text" id="${id}" value="${esc(vor)}" placeholder="TT.MM.JJJJ HH:MM" ${dis}>`;
        case 'Amount': return `<input type="text" id="${id}" value="${esc(vor)}" placeholder="0.00" inputmode="decimal" ${dis}>`;
        case 'Integer': case 'Double': return `<input type="text" id="${id}" value="${esc(vor)}" inputmode="decimal" ${dis}>`;
        default: return `<input type="text" id="${id}" value="${esc(vor)}" ${dis}>`;
    }
}

function _uebDialogHtml(vi, ai, s, si) {
    const n = s.dialog;
    const gesperrt = s.beantwortet;
    const absatz = p => {
        const id = `uebDlg-${vi}-${ai}-${si}-${p.id}`;
        if (!p.istFrage)
            return `<div class="ueb-absatz"><span class="ueb-label">${esc(p.label)}</span><span>${esc(_uebDialogWert(p.wertTyp, p.wert))}</span></div>`;
        return `<label class="ueb-absatz"><span class="ueb-label">${esc(p.label)}${p.optional ? ' <span class="komm-klein">(optional)</span>' : ' *'}</span>
            ${_uebDialogFeld(id, p, gesperrt)}
            ${p.problem ? `<span class="komm-rot">${esc(p.problem)}</span>` : ''}</label>`;
    };
    const ohneAbschnitt = n.absaetze.filter(p => !p.abschnittRef || !n.abschnitte.some(a => a.id === p.abschnittRef));
    const abschnitte = n.abschnitte.map(a => {
        const teile = n.absaetze.filter(p => p.abschnittRef === a.id);
        return `<div class="ueb-abschnitt">${a.titel ? `<div class="ueb-abschnitt-titel">${esc(a.titel)}</div>` : ''}
            ${a.beschreibung ? `<div class="komm-klein">${esc(a.beschreibung)}</div>` : ''}${teile.map(absatz).join('')}</div>`;
    }).join('');
    const vorher = n.vorherAnfrage || n.vorherAntwort;
    let fuss;
    if (!n.beantwortbar) fuss = `<div class="komm-klein">Nur zur Kenntnis — wird mit dem nächsten Synchronisieren quittiert.</div>`;
    else if (gesperrt) fuss = `<div class="komm-klein">Beantwortet (eigene Story ${esc(s.antwortStoryId || '')}).</div>`;
    else fuss = `<div class="komm-knoepfe"><button type="button" class="komm-btn-primaer ueb-klein" onclick="uebDialogSenden(${vi}, ${ai}, ${si})">Antwort senden</button>
        <span class="komm-klein">* Pflichtfeld · leer = Vorgabewert</span></div>`;
    return `<div class="ueb-dialog-kopf">${n.titel ? `<b>${esc(n.titel)}</b>` : '<b>Nachricht</b>'}
            <span class="komm-klein">${esc(n.art)}${n.erstellt ? ' · ' + _uebDatum(n.erstellt, true) : ''}${vorher ? ' · bezieht sich auf ' + esc(vorher) : ''}</span></div>
        ${n.beschreibung ? `<div>${esc(n.beschreibung)}</div>` : ''}
        ${ohneAbschnitt.map(absatz).join('')}${abschnitte}${fuss}`;
}
