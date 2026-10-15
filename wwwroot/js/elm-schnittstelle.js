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
let _elmmAeltere = false;
let _elmmTimer = null;

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
    elmmSendenZeichnen();
    elmmLaden();
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
    const frage = _elmmErsatz
        ? `${v.titel} als Ersatz für die Meldung vom ${_elmmDatum(_elmmErsatz.gesendet)} senden?`
        : `${v.titel} an ${v.empfaenger.length} Empfänger senden?`;
    if (!(await liquidConfirm(frage, { title: 'Meldung senden', yesLabel: 'Senden', noLabel: 'Abbrechen' }))) return;
    const body = Object.assign({}, v.auswahl, { ersetztId: _elmmErsatz?.id || null });
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
    const offen = alle.filter(x => !x.m.fertig);
    const fertig = alle.filter(x => x.m.fertig);
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
              <div style="margin-top:8px">${fertig.map(zeilen).join('')}</div></details>` : '');
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
            ${m.testCase ? '<span style="font-size:11px;color:#8b8b8b">Test</span>' : ''}
            <span style="font-size:12px;color:#8b8b8b">gesendet ${_elmmDatum(m.gesendet)}</span>
            <span style="margin-left:auto">${_elmmPille(m.stufe, m.text)}</span>
        </summary>
        <div style="padding:0 14px 14px">
            ${m.empfaenger.map((e, ei) => _elmmEmpfaengerHtml(m, mi, e, ei)).join('')}
            <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-top:12px">
                <span style="font-size:12px;color:#8b8b8b">${m.letzteAbfrage ? 'Zuletzt abgefragt ' + _elmmDatum(m.letzteAbfrage) : ''}</span>
                <span style="margin-left:auto"></span>
                ${m.ersetzbar ? `<button type="button" class="btn btn-secondary" onclick="elmmErsetzen('${_elmzEsc(m.id)}')">Korrigierte Meldung senden</button>` : ''}
                ${m.fertig ? '' : `<button type="button" class="btn btn-secondary" onclick="elmmAbfragen('${_elmzEsc(m.id)}')" ${_elmmBusy ? 'disabled' : ''}>Stand jetzt abfragen</button>`}
            </div>
        </div>
    </details>`;
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
    const fuss = !d.beantwortbar ? '<div style="font-size:12px;color:#8b8b8b;margin-top:6px">Zur Kenntnis.</div>'
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
