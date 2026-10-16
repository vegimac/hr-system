// Lohnmeldungen (ELM) — Meldung senden + Meldungen verfolgen (Walter 15.10.2026, Swissdec-Zertifizierung:
// «eine fertige Kommunikations-Schnittstelle, die sendet, empfängt, Fehler meldet und Korrekturen zurücksendet»).
// Server: ElmMeldungenController (api/elm/meldungen). Adresse, TestCase und Doppelsignatur setzt der Server.
// OneCrew holt den Stand selbst ab (Hintergrunddienst); in den ersten 10 Minuten nach dem Senden fragt
// die offene Seite zusätzlich alle 20 s nach.

let _elmm = null;              // { ziel, sendenGesperrt, meldungen }
let _elmmVorschau = null;
let _elmmErsatz = null;        // Meldung, die ersetzt wird
let _elmmBusy = false;
let _elmmMeldung = null;       // { ok, text } über der Liste
let _elmmOffen = new Set();
let _elmmVerlaufOffen = new Set();
let _elmmAeltere = false;
let _elmmZert = false;
let _elmmTimer = null;
let _elmmZielWahl = null;      // Testinstanz: Refapps Receiver oder Quality Tool

const _ELMM_MONATE = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];

const _ELMM_STUFE = {
    ok:     { punkt: '#16a34a', text: '#166534', grund: 'rgba(22,163,74,0.10)' },
    warn:   { punkt: '#d97706', text: '#92400e', grund: 'rgba(217,119,6,0.12)' },
    rot:    { punkt: '#dc2626', text: '#991b1b', grund: 'rgba(220,38,38,0.10)' },
    laeuft: { punkt: '#6b7280', text: '#3f3f3f', grund: 'rgba(60,55,48,0.08)' },
    grau:   { punkt: '#b8b4ab', text: '#8b8b8b', grund: 'rgba(60,55,48,0.05)' },
};

function _elmmDatum(iso, mitZeit = true) {
    if (!iso) return '–';
    const d = new Date(iso);
    if (isNaN(d)) return '–';
    const p = n => String(n).padStart(2, '0');
    const t = `${p(d.getDate())}.${p(d.getMonth() + 1)}.${d.getFullYear()}`;
    return mitZeit ? `${t} ${p(d.getHours())}:${p(d.getMinutes())}` : t;
}

function _elmmPille(stufe, text) {
    const f = _ELMM_STUFE[stufe] || _ELMM_STUFE.grau;
    return `<span style="display:inline-flex;align-items:center;gap:6px;padding:3px 10px;border-radius:999px;background:${f.grund};
        color:${f.text};font-weight:600;font-size:12px"><span style="width:8px;height:8px;border-radius:50%;background:${f.punkt};flex:0 0 8px"></span>${_elmzEsc(text)}</span>`;
}

function _elmmBox(ok, html) {
    const f = ok ? _ELMM_STUFE.ok : _ELMM_STUFE.rot;
    return `<div style="margin-top:12px;padding:10px 12px;border-radius:10px;background:${f.grund};color:${f.text};font-size:13px">${html}</div>`;
}

function elmmInit() {
    _elmmVorschau = null;
    _elmmErsatz = null;
    _elmmMeldung = null;
    _elmvFehler = null;
    elmmSendenZeichnen();
    elmmLaden();
    elmvLaden();
    elmqLaden();
}

// ── Verbindung (drei Ampeln, nur auf Knopfdruck) ──────────────────────────

let _elmv = null;              // { ziel, letzte }
let _elmvBusy = false;
let _elmvFehler = null;

async function elmvLaden() {
    try {
        const r = await fetch('/api/elm/meldungen/verbindung', { headers: ah(), cache: 'no-store' });
        if (!r.ok) throw new Error('Fehler ' + r.status);
        _elmv = await r.json();
    } catch (e) {
        _elmvFehler = e.message;
    }
    elmvZeichnen();
}

async function elmvPruefen() {
    if (_elmvBusy) return;
    _elmvBusy = true;
    _elmvFehler = null;
    elmvZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/verbindung/pruefen', { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(j.message || 'Fehler ' + r.status);
        _elmv = j;
    } catch (e) {
        _elmvFehler = e.message === 'Failed to fetch' ? 'Server nicht erreichbar.' : e.message;
    } finally {
        _elmvBusy = false;
        elmvZeichnen();
    }
}

function _elmvKachel(titel, a) {
    const f = _ELMM_STUFE[a?.stufe] || _ELMM_STUFE.grau;
    return `<div style="flex:1 1 240px;padding:12px 14px;border-radius:12px;border:1px solid rgba(60,55,48,0.14);background:rgba(255,255,255,0.35)">
        <div style="display:flex;align-items:center;gap:8px">
            <span style="width:12px;height:12px;border-radius:50%;background:${f.punkt};flex:0 0 12px"></span>
            <b style="color:#3f3f3f;font-size:13px">${titel}</b></div>
        <div style="margin-top:6px;font-size:13px;color:${a ? f.text : '#8b8b8b'}">${_elmzEsc(a?.text || 'Noch nicht geprüft.')}</div>
        ${a?.tipp ? `<div style="margin-top:4px;font-size:12px;color:#646464">${_elmzEsc(a.tipp)}</div>` : ''}
        ${a?.detail ? `<details style="margin-top:4px;font-size:11px;color:#8b8b8b"><summary style="cursor:pointer">Technisch</summary>${_elmzEsc(a.detail)}</details>` : ''}
    </div>`;
}

function elmvZeichnen() {
    const box = document.getElementById('elmvVerbindung');
    if (!box) return;
    const l = _elmv?.letzte;
    box.innerHTML = `
        <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <div style="font-size:12px;color:#8b8b8b;text-transform:uppercase;letter-spacing:.04em">Verbindung zu Swissdec</div>
            <span style="font-size:12px;color:#8b8b8b">${_elmzEsc(_elmv?.ziel || '')}</span>
            <span style="font-size:12px;color:#8b8b8b;margin-left:auto">${l ? 'Zuletzt geprüft ' + _elmmDatum(l.geprueftAm) + (l.geprueftVon ? ' von ' + _elmzEsc(l.geprueftVon) : '') : 'Noch nie geprüft'}</span>
            <button type="button" class="btn btn-secondary" onclick="elmvPruefen()" ${_elmvBusy ? 'disabled' : ''}>${_elmvBusy ? 'Prüfe …' : 'Verbindung prüfen'}</button>
        </div>
        <div style="display:flex;gap:12px;flex-wrap:wrap;margin-top:12px">
            ${_elmvKachel('Swissdec erreichbar', l?.erreichbar)}
            ${_elmvKachel('Uhrzeit', l?.uhrzeit)}
            ${_elmvKachel('Sichere Übermittlung', l?.sicher)}
        </div>
        ${_elmvFehler ? _elmmBox(false, _elmzEsc(_elmvFehler)) : ''}
        <div style="margin-top:8px;font-size:12px;color:#8b8b8b">Swissdec erlaubt diesen Test nur von Hand — OneCrew prüft die Verbindung nie selbständig.</div>`;
}

async function elmmLaden() {
    try {
        const r = await fetch('/api/elm/meldungen', { headers: ah(), cache: 'no-store' });
        if (!r.ok) throw new Error('Fehler ' + r.status);
        _elmm = await r.json();
    } catch (e) {
        const el = document.getElementById('elmmListe');
        if (el) el.innerHTML = `<div style="color:#991b1b">${_elmzEsc(e.message)}</div>`;
        return;
    }
    if ((_elmm.ziele || []).length > 1 && !document.getElementById('elmmZiel')) elmmSendenZeichnen();
    elmmListeZeichnen();
    _elmmPlanen();
}

// ── Meldung senden ────────────────────────────────────────────────────────

function elmmSendenZeichnen() {
    const box = document.getElementById('elmmSenden');
    if (!box) return;
    const heute = new Date();
    const vorMonat = heute.getMonth() === 0 ? 12 : heute.getMonth();
    const e = _elmmErsatz;
    const art = e ? e.art : (document.getElementById('elmmArt')?.value || 'monthly');
    const jahr = e ? e.jahr : (document.getElementById('elmmJahr')?.value || (heute.getMonth() === 0 ? heute.getFullYear() - 1 : heute.getFullYear()));
    const monat = e ? e.monat : (document.getElementById('elmmMonat')?.value || vorMonat);
    const gesperrt = e ? 'disabled' : '';
    const ziele = _elmm?.ziele || [];
    const zielWahl = ziele.some(z => z.schluessel === _elmmZielWahl) ? _elmmZielWahl : ziele[0]?.schluessel;
    const feld = 'padding:9px 12px;font-size:13px;color:#3f3f3f;background:#fff;border:1px solid rgba(255,255,255,0.95);border-radius:10px;'
        + 'box-shadow:0 2px 6px rgba(60,55,48,0.13), inset 0 1px 0 rgba(255,255,255,0.9)';

    box.innerHTML = `
        <div style="font-size:12px;color:#8b8b8b;text-transform:uppercase;letter-spacing:.04em">${e ? 'Korrigierte Meldung senden' : 'Meldung senden'}</div>
        ${e ? `<div style="margin-top:8px;padding:10px 12px;border-radius:10px;background:${_ELMM_STUFE.warn.grund};color:${_ELMM_STUFE.warn.text};font-size:13px">
            Ersetzt die <b>${_elmzEsc(e.titel)}</b> vom ${_elmmDatum(e.gesendet)}. OneCrew baut die Meldung mit den heutigen Daten neu auf
            und schickt sie als Ersatz — die Empfänger tauschen die alte Meldung aus.
            <a href="#" onclick="elmmErsatzAbbrechen();return false" style="margin-left:8px;color:inherit">Abbrechen</a></div>` : ''}
        <div style="display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap;margin-top:10px">
            <label style="display:flex;flex-direction:column;gap:4px;font-size:12px;color:#646464">Meldung
                <select id="elmmArt" ${gesperrt} onchange="elmmArtGewechselt()">
                    <option value="monthly" ${art === 'monthly' ? 'selected' : ''}>Monatsmeldung (Quellensteuer + Statistik)</option>
                    <option value="annual" ${art === 'annual' ? 'selected' : ''}>Jahresmeldung (AHV)</option>
                </select></label>
            <label id="elmmMonatWrap" style="display:${art === 'annual' ? 'none' : 'flex'};flex-direction:column;gap:4px;font-size:12px;color:#646464">Monat
                <select id="elmmMonat" ${gesperrt} onchange="_elmmVorschau=null;elmmVorschauZeichnen()">
                    ${_ELMM_MONATE.map((n, i) => `<option value="${i + 1}" ${+monat === i + 1 ? 'selected' : ''}>${n}</option>`).join('')}
                </select></label>
            <label style="display:flex;flex-direction:column;gap:4px;font-size:12px;color:#646464">Jahr
                <input id="elmmJahr" type="number" min="2020" max="2100" value="${_elmzEsc(String(jahr))}" ${gesperrt}
                       onchange="_elmmVorschau=null;elmmVorschauZeichnen()" style="${feld};width:110px"></label>
            ${!e && ziele.length > 1 ? `<label style="display:flex;flex-direction:column;gap:4px;font-size:12px;color:#646464">Senden an
                <select id="elmmZiel" onchange="_elmmZielWahl=this.value;elmmVorschauZeichnen()">
                    ${ziele.map(z => `<option value="${_elmzEsc(z.schluessel)}" ${z.schluessel === zielWahl ? 'selected' : ''}>${_elmzEsc(z.name)}</option>`).join('')}
                </select></label>` : ''}
            <button type="button" class="btn btn-secondary" onclick="elmmPruefen()" ${_elmmBusy ? 'disabled' : ''}>Prüfen</button>
        </div>
        <div id="elmmVorschau"></div>`;
    elmmVorschauZeichnen();
}

function elmmArtGewechselt() {
    const art = document.getElementById('elmmArt')?.value;
    const w = document.getElementById('elmmMonatWrap');
    if (w) w.style.display = art === 'annual' ? 'none' : 'flex';
    _elmmVorschau = null;
    elmmVorschauZeichnen();
}

function _elmmAuswahl() {
    const art = document.getElementById('elmmArt')?.value || 'monthly';
    return {
        art,
        jahr: parseInt(document.getElementById('elmmJahr')?.value, 10),
        monat: art === 'monthly' ? parseInt(document.getElementById('elmmMonat')?.value, 10) : null,
    };
}

async function elmmPruefen() {
    const a = _elmmAuswahl();
    const el = document.getElementById('elmmVorschau');
    if (!a.jahr) { if (el) el.innerHTML = _elmmBox(false, 'Bitte ein Jahr angeben.'); return; }
    if (el) el.innerHTML = '<div style="margin-top:12px;font-size:13px;color:#646464">Meldung wird aufgebaut und geprüft …</div>';
    try {
        const q = new URLSearchParams({ art: a.art, jahr: String(a.jahr) });
        if (a.monat) q.set('monat', String(a.monat));
        const r = await fetch('/api/elm/meldungen/vorschau?' + q, { headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(j.message || 'Fehler ' + r.status);
        _elmmVorschau = Object.assign(j, { auswahl: a });
    } catch (e) {
        _elmmVorschau = null;
        if (el) el.innerHTML = _elmmBox(false, _elmzEsc(e.message));
        return;
    }
    elmmVorschauZeichnen();
}

function elmmVorschauZeichnen() {
    const el = document.getElementById('elmmVorschau');
    if (!el) return;
    const v = _elmmVorschau;
    if (!v) { el.innerHTML = ''; return; }
    const fehler = v.fehler || [], technisch = v.technisch || [], warn = v.warnungen || [];
    const leer = !(v.empfaenger || []).length;
    const kannSenden = !fehler.length && !technisch.length && !leer && !v.sendenGesperrt;
    el.innerHTML = `
        <div style="margin-top:16px;padding:14px 16px;border-radius:12px;border:1px solid rgba(60,55,48,0.16)">
            <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
                <b style="color:#3f3f3f;font-size:14px">${_elmzEsc(v.titel)}</b>
                <span style="font-size:12px;color:#8b8b8b">${_elmzEsc(v.firmenname || '')} ${_elmzEsc(v.uid || '')}</span>
                ${kannSenden ? _elmmPille('ok', 'Prüfung bestanden') : _elmmPille('rot', leer ? 'Leer' : fehler.length || technisch.length ? 'Fehler gefunden' : 'Senden gesperrt')}
            </div>
            <div style="margin-top:10px;font-size:13px;color:#646464">Geht an:
                ${leer ? '<b style="color:#991b1b">keine Empfänger — die Meldung ist leer</b>'
                       : v.empfaenger.map(e => `<span style="display:inline-block;margin:3px 4px 0 0;padding:3px 10px;border-radius:999px;border:1px solid rgba(60,55,48,0.2);font-size:12px">${_elmzEsc(e.name)}</span>`).join('')}
            </div>
            ${fehler.length ? _elmmBox(false, `<b>Diese Meldung sendet OneCrew nicht — bitte zuerst die Daten korrigieren:</b>
                <ul style="margin:6px 0 0 18px;padding:0">${fehler.map(f => `<li>${_elmzEsc(f)}</li>`).join('')}</ul>`) : ''}
            ${technisch.length ? _elmmBox(false, `<b>Die Meldung ist technisch fehlerhaft (${technisch.length} Punkt${technisch.length === 1 ? '' : 'e'}) — bitte dem Support melden.</b>
                <details style="margin-top:4px"><summary style="cursor:pointer">Details</summary>${technisch.map(t => `<div style="font-size:12px">${_elmzEsc(t)}</div>`).join('')}</details>`) : ''}
            ${warn.length ? `<div style="margin-top:12px;font-size:12px;color:#92400e">${warn.slice(0, 6).map(w => '⚠ ' + _elmzEsc(w)).join('<br>')}</div>` : ''}
            ${v.sendenGesperrt ? _elmmBox(false, _elmzEsc(v.sendenGesperrt)) : ''}
            <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:14px">
                <button type="button" class="btn btn-primary" onclick="elmmSenden()" ${kannSenden && !_elmmBusy ? '' : 'disabled'}>
                    ${_elmmErsatz ? 'Korrigierte Meldung senden' : 'An Swissdec senden'}</button>
            </div>
        </div>`;
}

async function elmmSenden() {
    const v = _elmmVorschau;
    if (!v || _elmmBusy) return;
    const ziel = _elmmErsatz ? null : _elmmZielAktuell();
    const frage = _elmmErsatz
        ? `${v.titel} als Ersatz für die Meldung vom ${_elmmDatum(_elmmErsatz.gesendet)} senden?`
        : `${v.titel} an ${v.empfaenger.length} Empfänger senden?` + (ziel ? `\n\nZiel: ${ziel.name}` : '');
    if (!(await liquidConfirm(frage, { title: 'Meldung senden', yesLabel: 'Senden', noLabel: 'Abbrechen' }))) return;
    const body = Object.assign({}, v.auswahl, { ersetztId: _elmmErsatz?.id || null, ziel: ziel?.schluessel || null });
    const j = await _elmmPost('senden', body, 'Meldung wird gesendet …');
    if (j?.ok) {
        _elmmVorschau = null;
        _elmmErsatz = null;
        if (j.vorgangId) _elmmOffen.add(j.vorgangId);
        elmmSendenZeichnen();
        _elmmPlanen(12000);
    } else if (j?.hinweise?.length) {
        _elmmMeldung = { ok: false, text: (j.meldung || '') + ' — ' + j.hinweise.join(' · ') };
        elmmListeZeichnen();
    }
}

/** Gewähltes Ziel, wenn es mehr als eines gibt (nur Testinstanz) — sonst null, der Server nimmt den Standard. */
function _elmmZielAktuell() {
    const ziele = _elmm?.ziele || [];
    if (ziele.length < 2) return null;
    const wahl = document.getElementById('elmmZiel')?.value || _elmmZielWahl;
    return ziele.find(z => z.schluessel === wahl) || ziele[0];
}

function elmmErsetzen(id) {
    const m = (_elmm?.meldungen || []).find(x => x.id === id);
    if (!m) return;
    _elmmErsatz = { id: m.id, art: m.art, jahr: m.jahr, monat: m.monat, titel: m.titel, gesendet: m.gesendet };
    _elmmVorschau = null;
    elmmSendenZeichnen();
    document.getElementById('elmmSenden')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    elmmPruefen();
}

function elmmErsatzAbbrechen() {
    _elmmErsatz = null;
    _elmmVorschau = null;
    elmmSendenZeichnen();
}

// ── Aufrufe ───────────────────────────────────────────────────────────────

async function _elmmPost(pfad, body, warteText) {
    if (_elmmBusy) return null;
    _elmmBusy = true;
    _elmmMeldung = { ok: true, text: warteText };
    elmmListeZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/' + pfad, { method: 'POST', headers: ah(), body: JSON.stringify(body || {}) });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { _elmmMeldung = { ok: false, text: j.message || 'Fehler ' + r.status }; return null; }
        if (j.stand) _elmm = j.stand;
        _elmmMeldung = j.meldung ? { ok: !!j.ok, text: j.meldung } : null;
        return j;
    } catch (e) {
        _elmmMeldung = { ok: false, text: 'Server nicht erreichbar.' };
        return null;
    } finally {
        _elmmBusy = false;
        elmmListeZeichnen();
        elmmVorschauZeichnen();
    }
}

async function elmmAbfragen(id, still = false) {
    if (_elmmBusy) return;
    if (still) {
        try {
            const r = await fetch(`/api/elm/meldungen/${encodeURIComponent(id)}/abfragen`, { method: 'POST', headers: ah() });
            const j = await r.json().catch(() => ({}));
            if (r.ok && j.stand) { _elmm = j.stand; elmmListeZeichnen(); }
        } catch (_) { /* nächster Versuch */ }
        return;
    }
    await _elmmPost(`${encodeURIComponent(id)}/abfragen`, {}, 'Stand wird bei Swissdec abgefragt …');
}

async function elmmAntworten(mi, ei, ni) {
    const m = _elmm?.meldungen?.[mi], e = m?.empfaenger?.[ei], n = e?.nachrichten?.[ni];
    if (!n?.dialog) return;
    const werte = {};
    n.dialog.absaetze.filter(p => p.istFrage).forEach(p => {
        const f = document.getElementById(`elmmDlg-${mi}-${ei}-${ni}-${p.id}`);
        if (f) werte[p.id] = f.value.trim() || null;
    });
    await _elmmPost(`${encodeURIComponent(m.id)}/antworten`, { addresseeId: e.addresseeId, storyId: n.storyId, werte }, 'Antwort wird gesendet …');
}

function elmmFreigeben(mi, ei) {
    const m = _elmm?.meldungen?.[mi], e = m?.empfaenger?.[ei];
    if (!e?.freigabeUrl) return;
    window.open(e.freigabeUrl, '_blank', 'noopener,noreferrer');
    _elmmMeldung = { ok: true, text: `Freigabe für ${e.name} im neuen Fenster geöffnet. OneCrew fragt den Stand in 20 Sekunden ab.` };
    elmmListeZeichnen();
    setTimeout(() => elmmAbfragen(m.id, true), 20000);
}

/** In den ersten 10 Minuten nach dem Senden, solange die Seite offen ist: alle 20 s still nachfragen. */
function _elmmPlanen(ms = 20000) {
    clearTimeout(_elmmTimer);
    _elmmTimer = setTimeout(async () => {
        if (!document.getElementById('page-elm-schnittstelle')?.classList.contains('active')) return;
        const grenze = Date.now() - 10 * 60 * 1000;
        const m = (_elmm?.meldungen || []).find(x => !x.fertig && x.stufe === 'laeuft' && new Date(x.gesendet).getTime() > grenze);
        if (!m) return;
        await elmmAbfragen(m.id, true);
        _elmmPlanen();
    }, ms);
}

// ── Liste ─────────────────────────────────────────────────────────────────

function elmmListeZeichnen() {
    const el = document.getElementById('elmmListe');
    if (!el || !_elmm) return;
    const alle = (_elmm.meldungen || []).map((m, mi) => ({ m, mi }));
    const offen = alle.filter(x => !x.m.fertig && !x.m.zertifizierung);
    const fertig = alle.filter(x => x.m.fertig && !x.m.zertifizierung);
    const zert = alle.filter(x => x.m.zertifizierung);
    const meldung = _elmmMeldung
        ? `<div style="margin:4px 0 10px;padding:10px 12px;border-radius:10px;font-size:13px;
              background:${_elmmMeldung.ok ? _ELMM_STUFE.laeuft.grund : _ELMM_STUFE.rot.grund};
              color:${_elmmMeldung.ok ? _ELMM_STUFE.laeuft.text : _ELMM_STUFE.rot.text}">${_elmzEsc(_elmmMeldung.text)}</div>` : '';
    const kopf = `<div style="display:flex;align-items:center;gap:10px;margin-bottom:8px">
        <div style="font-size:12px;color:#8b8b8b;text-transform:uppercase;letter-spacing:.04em">Meldungen</div>
        <span style="font-size:12px;color:#8b8b8b">OneCrew holt Quittungen und Rückfragen selbst ab.</span>
        <button type="button" class="btn btn-secondary" style="margin-left:auto" onclick="elmmLaden()">Aktualisieren</button></div>`;
    if (!alle.length) {
        el.innerHTML = kopf + meldung + '<div style="font-size:13px;color:#8b8b8b">Noch keine Meldung gesendet.</div>';
        return;
    }
    const zeilen = x => _elmmMeldungHtml(x.m, x.mi);
    el.innerHTML = kopf + meldung
        + (offen.length ? offen.map(zeilen).join('') : '<div style="font-size:13px;color:#8b8b8b;margin-bottom:8px">Keine offenen Meldungen.</div>')
        + (fertig.length ? `<details ${_elmmAeltere ? 'open' : ''} ontoggle="_elmmAeltere=this.open" style="margin-top:10px">
              <summary style="cursor:pointer;font-size:13px;color:#646464">Abgeschlossene Meldungen (${fertig.length})</summary>
              <div style="margin-top:8px">${fertig.map(zeilen).join('')}</div></details>` : '')
        + (zert.length ? `<details ${_elmmZert ? 'open' : ''} ontoggle="_elmmZert=this.open" style="margin-top:10px">
              <summary style="cursor:pointer;font-size:13px;color:#646464">Tests aus der Zertifizierung (${zert.length})
                <span style="color:#8b8b8b">— werden nicht mehr automatisch abgefragt</span></summary>
              <div style="margin-top:8px">${zert.map(zeilen).join('')}</div></details>` : '');
}

function _elmmMeldungHtml(m, mi) {
    const offen = _elmmOffen.has(m.id);
    const f = _ELMM_STUFE[m.stufe] || _ELMM_STUFE.grau;
    return `<details data-id="${_elmzEsc(m.id)}" ${offen ? 'open' : ''} ontoggle="_elmmOffenMerken(this)"
        style="margin-bottom:8px;border-radius:12px;border:1px solid rgba(60,55,48,0.14);background:rgba(255,255,255,0.35)">
        <summary style="cursor:pointer;list-style:none;padding:12px 14px;display:flex;align-items:center;gap:12px;flex-wrap:wrap">
            <span style="width:10px;height:10px;border-radius:50%;background:${f.punkt};flex:0 0 10px"></span>
            <b style="color:#3f3f3f;font-size:14px">${_elmzEsc(m.titel)}</b>
            ${m.ersatz ? '<span style="font-size:11px;padding:2px 8px;border-radius:999px;border:1px solid rgba(60,55,48,0.25);color:#646464">Ersatzmeldung</span>' : ''}
            ${m.qualityTool ? '<span style="font-size:11px;padding:2px 8px;border-radius:999px;border:1px solid rgba(60,55,48,0.25);color:#646464">Quality Tool</span>'
                : m.testCase ? '<span style="font-size:11px;color:#8b8b8b">Test</span>' : ''}
            <span style="font-size:12px;color:#8b8b8b">gesendet ${_elmmDatum(m.gesendet)}</span>
            <span style="margin-left:auto">${_elmmPille(m.stufe, m.text)}</span>
        </summary>
        <div style="padding:0 14px 14px">
            ${m.empfaenger.map((e, ei) => _elmmEmpfaengerHtml(m, mi, e, ei)).join('')}
            ${_elmmVerlaufHtml(m)}
            <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-top:12px">
                <span style="font-size:12px;color:#8b8b8b">${m.letzteAbfrage ? 'Zuletzt abgefragt ' + _elmmDatum(m.letzteAbfrage) : ''}</span>
                <span style="margin-left:auto"></span>
                ${m.gesendeteMeldung ? `<button type="button" class="btn btn-secondary" onclick="elmmArchivSpeichern('${_elmzEsc(m.gesendeteMeldung)}')">Gesendete Meldung herunterladen</button>` : ''}
                ${m.ersetzbar ? `<button type="button" class="btn btn-secondary" onclick="elmmErsetzen('${_elmzEsc(m.id)}')">Korrigierte Meldung senden</button>` : ''}
                ${m.fertig ? '' : `<button type="button" class="btn btn-secondary" onclick="elmmAbfragen('${_elmzEsc(m.id)}')" ${_elmmBusy ? 'disabled' : ''}>Stand jetzt abfragen</button>`}
            </div>
        </div>
    </details>`;
}

/** Archiv der Meldung: jeder Schritt mit der gesendeten Anfrage und der Antwort von Swissdec. */
function _elmmVerlaufHtml(m) {
    const v = m.verlauf || [];
    if (!v.length) return '';
    const knopf = (name, text) => name
        ? `<a href="#" onclick="elmmArchivSpeichern('${_elmzEsc(name)}');return false" style="color:#3f3f3f;white-space:nowrap">${text}</a>` : '';
    const zeilen = v.map(p => `<tr>
        <td style="padding:4px 10px 4px 0;white-space:nowrap;color:#646464">${_elmmDatum(p.zeit)}</td>
        <td style="padding:4px 10px 4px 0;white-space:nowrap;color:#3f3f3f;font-weight:600">${_elmzEsc(p.schritt)}</td>
        <td style="padding:4px 10px 4px 0;color:#646464">${_elmzEsc(p.text)}</td>
        <td style="padding:4px 0;white-space:nowrap">${knopf(p.anfrage, 'Gesendet')}${p.anfrage && p.antwort ? ' · ' : ''}${knopf(p.antwort, 'Antwort')}</td></tr>`).join('');
    const mehr = m.verlaufAnzahl > v.length ? `<div style="font-size:11px;color:#8b8b8b;margin-top:4px">Die neuesten ${v.length} von ${m.verlaufAnzahl} Schritten.</div>` : '';
    return `<details data-id="${_elmzEsc(m.id)}" ${_elmmVerlaufOffen.has(m.id) ? 'open' : ''}
        ontoggle="this.open ? _elmmVerlaufOffen.add(this.dataset.id) : _elmmVerlaufOffen.delete(this.dataset.id)"
        style="margin-top:10px;padding-top:8px;border-top:1px solid rgba(60,55,48,0.10)">
        <summary style="cursor:pointer;font-size:13px;color:#646464">Verlauf und Archiv (${m.verlaufAnzahl})
            <span style="color:#8b8b8b">— jede gesendete Nachricht und jede Antwort von Swissdec, so wie sie übermittelt wurde</span></summary>
        <div style="overflow-x:auto;margin-top:6px"><table style="border-collapse:collapse;font-size:12px;width:100%">${zeilen}</table></div>${mehr}
    </details>`;
}

async function elmmArchivSpeichern(name) {
    try {
        const r = await fetch(`/api/elm/meldungen/archiv/${encodeURIComponent(name)}`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            throw new Error(j.message || 'Fehler ' + r.status);
        }
        await saveBlobAsk(await r.blob(), name);
    } catch (e) {
        _elmmMeldung = { ok: false, text: e.message };
        elmmListeZeichnen();
    }
}

function _elmmOffenMerken(el) {
    if (el.open) _elmmOffen.add(el.dataset.id); else _elmmOffen.delete(el.dataset.id);
}

function _elmmEmpfaengerHtml(m, mi, e, ei) {
    const hinweise = (e.hinweise || []).length
        ? `<div style="font-size:12px;color:#646464;margin-top:4px">${e.hinweise.map(h => '• ' + _elmzEsc(h)).join('<br>')}</div>` : '';
    const freigabe = e.freigabeUrl
        ? `<div style="margin-top:8px;display:flex;gap:10px;align-items:center;flex-wrap:wrap">
              <button type="button" class="btn btn-primary" onclick="elmmFreigeben(${mi}, ${ei})">Beim Empfänger freigeben</button>
              <span style="font-size:12px;color:#8b8b8b">Link gültig bis ${_elmmDatum(e.freigabeBis)}</span></div>` : '';
    const nachrichten = (e.nachrichten || []).map((n, ni) => _elmmNachrichtHtml(m, mi, e, ei, n, ni)).join('');
    return `<div style="padding:10px 0;border-top:1px solid rgba(60,55,48,0.10)">
        <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
            <b style="color:#3f3f3f;font-size:13px">${_elmzEsc(e.name)}</b>
            <span style="margin-left:auto">${_elmmPille(e.stufe, e.text)}</span>
        </div>
        ${hinweise}${freigabe}${nachrichten}
    </div>`;
}

function _elmmNachrichtHtml(m, mi, e, ei, n, ni) {
    const kopf = `<span style="font-weight:600;color:#3f3f3f">${_elmzEsc(n.art)}</span>
        <span style="color:#8b8b8b">· erhalten ${_elmmDatum(n.empfangen)}</span>`;
    if (!n.dialog) {
        return `<details style="margin-top:6px;font-size:12px"><summary style="cursor:pointer">${kopf}</summary>
            <pre style="white-space:pre-wrap;word-break:break-all;font-size:11px;background:rgba(255,255,255,0.6);padding:8px;border-radius:8px;max-height:260px;overflow:auto">${_elmzEsc(typeof _uebXmlHuebsch === 'function' ? _uebXmlHuebsch(n.xml) : n.xml)}</pre></details>`;
    }
    const d = n.dialog;
    const absatz = p => {
        if (!p.istFrage)
            return `<div style="display:flex;gap:10px;font-size:13px;margin-top:4px"><span style="color:#646464;min-width:180px">${_elmzEsc(p.label)}</span>
                <span>${_elmzEsc(_uebDialogWert(p.wertTyp, p.wert))}</span></div>`;
        return `<label style="display:flex;gap:10px;align-items:center;font-size:13px;margin-top:6px">
            <span style="color:#646464;min-width:180px">${_elmzEsc(p.label)}${p.optional ? '' : ' *'}</span>
            ${_uebDialogFeld(`elmmDlg-${mi}-${ei}-${ni}-${p.id}`, p, n.beantwortet)}
            ${p.problem ? `<span style="color:#991b1b">${_elmzEsc(p.problem)}</span>` : ''}</label>`;
    };
    const fuss = !d.beantwortbar
        ? (n.quittiert
            ? '<div style="font-size:12px;color:#166534;margin-top:6px">✓ Nur zur Kenntnis — Empfang bestätigt.</div>'
            : '<div style="font-size:12px;color:#8b8b8b;margin-top:6px">Nur zur Kenntnis, keine Antwort nötig — OneCrew bestätigt den Empfang beim nächsten Abfragen selbst.</div>')
        : n.beantwortet ? '<div style="font-size:12px;color:#166534;margin-top:6px">✓ Beantwortet.</div>'
        : `<div style="display:flex;justify-content:flex-end;margin-top:10px">
              <button type="button" class="btn btn-primary" onclick="elmmAntworten(${mi}, ${ei}, ${ni})" ${_elmmBusy ? 'disabled' : ''}>Antwort senden</button></div>`;
    return `<div style="margin-top:8px;padding:10px 12px;border-radius:10px;border:1px solid rgba(60,55,48,0.16);background:rgba(255,255,255,0.45)">
        <div style="font-size:12px">${kopf}</div>
        ${d.titel ? `<div style="font-weight:600;color:#3f3f3f;margin-top:4px">${_elmzEsc(d.titel)}</div>` : ''}
        ${d.beschreibung ? `<div style="font-size:13px;color:#646464;margin-top:2px">${_elmzEsc(d.beschreibung)}</div>` : ''}
        ${d.absaetze.map(absatz).join('')}${fuss}
    </div>`;
}

// ── Lohntest im Swissdec Quality Tool (nur Testinstanz, Walter 15.10.2026) ─────
// Bericht über die Quality-Tool-Schnittstelle (ElmQualityToolController). Das Quality Tool listet nur
// Abweichungen: Soll = Referenz der Muster AG, OneCrew = unsere gesendete Meldung. Der API-Schlüssel
// bleibt auf dem Server.

let _elmq = null;              // Bericht
let _elmqStatus = null;        // { verfuegbar, nichtBereit, monitoringId }
let _elmqBusy = false;
let _elmqFehler = null;
let _elmqOffen = new Set();

function _elmqMonat(seq) {
    const m = /^(\d{4})-(\d{2})$/.exec(seq || '');
    return m ? `${_ELMM_MONATE[+m[2] - 1]} ${m[1]}` : (seq || '');
}

function _elmqTeil(t) {
    return { MonthlySalary: 'Monatsmeldung', YearlyRetrospective: 'Jahresmeldung', YearlyProspective: 'Vorausmeldung' }[t] || t || '';
}

async function elmqLaden() {
    const box = document.getElementById('elmqBericht');
    if (!box) return;
    try {
        const r = await fetch('/api/elm/qualitytool/status', { headers: ah(), cache: 'no-store' });
        _elmqStatus = r.ok ? await r.json() : null;
    } catch (_) { _elmqStatus = null; }
    box.style.display = _elmqStatus?.verfuegbar ? '' : 'none';
    if (!_elmqStatus?.verfuegbar) return;
    if (_elmqStatus.nichtBereit) { _elmqFehler = _elmqStatus.nichtBereit; elmqZeichnen(); return; }
    await _elmqAufruf('GET', 'bericht');
}

async function _elmqAufruf(methode, pfad) {
    if (_elmqBusy) return;
    _elmqBusy = true;
    _elmqFehler = null;
    elmqZeichnen();
    try {
        const r = await fetch('/api/elm/qualitytool/' + pfad, { method: methode, headers: ah(), cache: 'no-store' });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(j.message || 'Fehler ' + r.status);
        _elmq = j;
    } catch (e) {
        _elmqFehler = e.message === 'Failed to fetch' ? 'Server nicht erreichbar.' : e.message;
    } finally {
        _elmqBusy = false;
        elmqZeichnen();
    }
}

async function elmqZuruecksetzen() {
    if (!(await liquidConfirm('Alle bisher gesendeten Meldungen im laufenden Quality-Tool-Lauf löschen?\n\nDie Referenz der Muster AG bleibt geladen — danach die Monate neu senden.',
        { title: 'Sendungen zurücksetzen', yesLabel: 'Zurücksetzen', noLabel: 'Abbrechen' }))) return;
    await _elmqAufruf('POST', 'sendungen-zuruecksetzen');
}

async function elmqNeuerLauf() {
    if (!(await liquidConfirm('Den laufenden Lauf archivieren und einen neuen mit der aktuellen Referenz der Muster AG starten?\n\nDer alte Lauf bleibt im Quality Tool unter den archivierten Läufen einsehbar. Rundungstoleranz 0.00.',
        { title: 'Neuer Lauf', yesLabel: 'Neuen Lauf starten', noLabel: 'Abbrechen' }))) return;
    await _elmqAufruf('POST', 'neuer-lauf');
}

function _elmqOffenMerken(el) {
    if (el.open) _elmqOffen.add(el.dataset.seq); else _elmqOffen.delete(el.dataset.seq);
}

function elmqZeichnen() {
    const box = document.getElementById('elmqBericht');
    if (!box || !_elmqStatus?.verfuegbar) return;
    const b = _elmq;
    const knopf = (text, fn, primaer) => `<button type="button" class="btn ${primaer ? 'btn-primary' : 'btn-secondary'}" onclick="${fn}()"
        ${_elmqBusy || _elmqStatus.nichtBereit ? 'disabled' : ''}>${text}</button>`;
    const kopf = `
        <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <div style="font-size:12px;color:#8b8b8b;text-transform:uppercase;letter-spacing:.04em">Lohntest — Swissdec Quality Tool</div>
            ${_elmqStatus.monitoringId ? `<span style="font-size:12px;color:#8b8b8b">MonitoringID ${_elmzEsc(_elmqStatus.monitoringId)}</span>` : ''}
            <span style="margin-left:auto"></span>
            ${knopf('Sendungen zurücksetzen', 'elmqZuruecksetzen')}
            ${knopf('Neuer Lauf', 'elmqNeuerLauf')}
            ${knopf(_elmqBusy ? 'Lade …' : 'Bericht holen', 'elmqLaden', true)}
        </div>
        <div style="margin-top:6px;font-size:12px;color:#8b8b8b">Monatsmeldungen oben an «Swissdec Quality Tool» senden, dann «Bericht holen».
            Unten siehst du <b>jeden gesendeten Monat einzeln</b> — Swissdec listet nur Differenzen; «Stimmt» = gesendet und keine Abweichung.</div>`;
    if (!b) {
        box.innerHTML = kopf + (_elmqFehler ? _elmmBox(false, _elmzEsc(_elmqFehler)) : `<div style="margin-top:10px;font-size:13px;color:#646464">${_elmqBusy ? 'Bericht wird geholt …' : ''}</div>`);
        return;
    }
    const stand = `<div style="display:flex;gap:16px;align-items:baseline;flex-wrap:wrap;margin-top:12px">
        <div><span style="font-size:26px;font-weight:700;color:#3f3f3f">${Number(b.punkte || 0).toLocaleString('de-CH', { maximumFractionDigits: 1 })} %</span>
            <span style="font-size:12px;color:#8b8b8b;margin-left:4px">Punktzahl des Laufs (Swissdec)</span></div>
        <div>${b.anzahl ? _elmmPille('rot', `${b.anzahl} Abweichung${b.anzahl === 1 ? '' : 'en'}`) : _elmmPille('ok', 'Keine Abweichung')}</div>
        ${b.geholtAm ? `<div style="font-size:12px;color:#8b8b8b;margin-left:auto">Bericht geholt ${_elmmDatum(b.geholtAm)}</div>` : ''}
    </div>`;
    const zeile = a => {
        const artHinweis = a.art === 'Element fehlt' ? 'fehlt bei uns'
            : a.art === 'Element zu viel' ? 'haben wir zu viel'
            : a.art === 'Anzahl Elemente' ? 'Anzahl anders'
            : a.art === 'Element anders' ? 'Element anders'
            : null;
        return `<tr title="${_elmzEsc(a.pfad)}">
        <td style="padding:6px 10px 6px 0;color:#3f3f3f;vertical-align:top">
            <div style="font-weight:600">${_elmzEsc(a.feld)}${artHinweis ? ` <span style="font-weight:500;color:#92400e">(${_elmzEsc(artHinweis)})</span>` : ''}</div>
            ${a.klartext ? `<div style="font-size:11.5px;color:#646464;margin-top:2px;line-height:1.35">${_elmzEsc(a.klartext)}</div>` : ''}
        </td>
        <td style="padding:6px 10px 6px 0;text-align:right;white-space:nowrap;color:#166534;vertical-align:top">${_elmzEsc(a.soll ?? '–')}</td>
        <td style="padding:6px 0;text-align:right;white-space:nowrap;color:#991b1b;font-weight:600;vertical-align:top">${_elmzEsc(a.ist ?? '–')}</td></tr>`;
    };
    const fall = f => `<div style="margin-top:10px">
        <div style="font-weight:600;color:#3f3f3f;font-size:13px">${_elmzEsc(f.name)}</div>
        <table style="border-collapse:collapse;font-size:12px;width:100%;margin-top:4px;table-layout:fixed">
            <colgroup><col><col style="width:150px"><col style="width:150px"></colgroup>
            <tr style="color:#8b8b8b"><td style="padding:2px 10px 2px 0">Was stimmt nicht</td><td style="padding:2px 10px 2px 0;text-align:right">Swissdec erwartet</td><td style="padding:2px 0;text-align:right">OneCrew hat</td></tr>
            ${(f.abweichungen || []).map(zeile).join('')}</table></div>`;
    const pille = u => u.status === 'rot' ? _elmmPille('rot', u.text)
        : u.status === 'ok' ? _elmmPille('ok', u.text)
        : u.status === 'warn' ? _elmmPille('warn', u.text)
        : _elmmPille('grau', u.text);
    // Monat für Monat (gesendet + QT) — nicht nur die Monate mit Fehlern
    const liste = (b.uebersicht && b.uebersicht.length) ? b.uebersicht : (b.monate || []).map(m => ({
        sequenz: m.sequenz, teilprozess: m.teilprozess, status: m.anzahl ? 'rot' : 'ok',
        text: m.anzahl ? `${m.anzahl} Abweichung${m.anzahl === 1 ? '' : 'en'}` : 'Stimmt',
        anzahl: m.anzahl, faelle: m.faelle,
    }));
    const monate = liste.map(u => {
        const key = (u.sequenz || '') + '|' + (u.teilprozess || '');
        const wann = u.gesendetAm
            ? `<span style="font-size:12px;color:#8b8b8b">gesendet ${_elmmDatum(u.gesendetAm)}</span>`
            : '';
        const hatDetails = u.status === 'rot' && (u.faelle || []).length;
        if (!hatDetails) {
            return `<div data-seq="${_elmzEsc(key)}" style="margin-top:8px;border-radius:12px;border:1px solid rgba(60,55,48,0.14);background:rgba(255,255,255,0.35);padding:10px 14px;display:flex;align-items:center;gap:12px;flex-wrap:wrap">
                <b style="color:#3f3f3f;font-size:14px">${_elmzEsc(_elmqMonat(u.sequenz))}</b>
                <span style="font-size:12px;color:#8b8b8b">${_elmzEsc(_elmqTeil(u.teilprozess))}</span>
                ${wann}
                <span style="margin-left:auto">${pille(u)}</span>
            </div>`;
        }
        return `<details data-seq="${_elmzEsc(key)}" ${_elmqOffen.has(key) ? 'open' : ''}
            ontoggle="_elmqOffenMerken(this)" style="margin-top:8px;border-radius:12px;border:1px solid rgba(60,55,48,0.14);background:rgba(255,255,255,0.35)">
        <summary style="cursor:pointer;list-style:none;padding:10px 14px;display:flex;align-items:center;gap:12px;flex-wrap:wrap">
            <b style="color:#3f3f3f;font-size:14px">${_elmzEsc(_elmqMonat(u.sequenz))}</b>
            <span style="font-size:12px;color:#8b8b8b">${_elmzEsc(_elmqTeil(u.teilprozess))}</span>
            ${wann}
            <span style="margin-left:auto">${pille(u)}</span>
        </summary>
        <div style="padding:0 14px 12px">${(u.faelle || []).map(fall).join('')}</div>
    </details>`;
    }).join('');
    box.innerHTML = kopf + stand + (_elmqFehler ? _elmmBox(false, _elmzEsc(_elmqFehler)) : '')
        + (monate || `<div style="margin-top:10px;font-size:13px;color:#646464">${b.punkte > 0
            ? 'Noch kein Monat an das Quality Tool gesendet — oben Monat wählen und senden, dann «Bericht holen».'
            : 'Noch keine Meldung im laufenden Lauf.'}</div>`);
}
