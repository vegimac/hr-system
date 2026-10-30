// HR-Bereich: Meldung absetzen (Walter 30.10.2026).
// Dieselbe Schnittstelle wie die Entwicklungsseite «Lohnmeldungen (ELM)», ohne Zertifikat,
// Verbindungstest und Quality Tool. Server: ElmMeldungenController (api/elm/meldungen).

const _ELMA_MONATE = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
const _ELMA_STUFE = {
    ok:     { punkt: '#16a34a', text: '#166534', grund: 'rgba(22,163,74,0.10)' },
    warn:   { punkt: '#d97706', text: '#92400e', grund: 'rgba(217,119,6,0.12)' },
    rot:    { punkt: '#dc2626', text: '#991b1b', grund: 'rgba(220,38,38,0.10)' },
    laeuft: { punkt: '#6b7280', text: '#3f3f3f', grund: 'rgba(60,55,48,0.08)' },
    grau:   { punkt: '#b8b4ab', text: '#8b8b8b', grund: 'rgba(60,55,48,0.05)' },
};

let _elma = null;
let _elmaVorschau = null;
let _elmaErsatz = null;
let _elmaBusy = false;
let _elmaHinweis = null;
let _elmaTimer = null;
let _elmaOffen = new Set();

(function () {
    if (document.getElementById('elmaSanduhrStyle')) return;
    const s = document.createElement('style');
    s.id = 'elmaSanduhrStyle';
    s.textContent = '@keyframes elmaSanduhr{0%,40%{transform:rotate(0)}60%,100%{transform:rotate(180deg)}}'
        + '.elma-sanduhr{display:inline-block;font-size:36px;line-height:1;animation:elmaSanduhr 1.8s ease-in-out infinite}'
        + '@media (prefers-reduced-motion:reduce){.elma-sanduhr{animation:none}}';
    document.head.appendChild(s);
})();

const _ELMA_FELD = 'box-sizing:border-box;background:#fff;border:1px solid rgba(255,255,255,0.95);border-radius:10px;'
    + 'padding:8px 10px;font-size:13px;color:#3f3f3f;box-shadow:0 2px 6px rgba(60,55,48,0.13), inset 0 1px 0 rgba(255,255,255,0.9)';

function _elmaWarte(titel, text) {
    const el = document.getElementById('elmaStatus');
    if (!el) return;
    if (!titel) { el.innerHTML = ''; return; }
    el.innerHTML = `<div style="margin-top:16px;padding:18px 20px;border-radius:16px;background:rgba(255,255,255,0.55);
            border:1px solid rgba(255,255,255,0.75);box-shadow:0 10px 28px rgba(60,55,48,0.12);display:flex;gap:16px;align-items:center">
        <div class="elma-sanduhr" aria-hidden="true">⏳</div>
        <div>
            <div style="font-size:16px;font-weight:700;color:#3f3f3f">${_elmaEsc(titel)}</div>
            <div style="font-size:13px;color:#646464;margin-top:4px">${_elmaEsc(text || '')}</div>
        </div>
    </div>`;
    el.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function _elmaEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function _elmaDatum(iso) {
    if (!iso) return '';
    const d = new Date(iso);
    if (isNaN(d)) return '';
    const p = n => String(n).padStart(2, '0');
    return `${p(d.getDate())}.${p(d.getMonth() + 1)}.${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

function _elmaPille(stufe, text) {
    const f = _ELMA_STUFE[stufe] || _ELMA_STUFE.grau;
    return `<span style="display:inline-flex;align-items:center;gap:6px;padding:3px 10px;border-radius:999px;background:${f.grund};color:${f.text};font-weight:600;font-size:12px"><span style="width:8px;height:8px;border-radius:50%;background:${f.punkt}"></span>${_elmaEsc(text)}</span>`;
}

function elmAnwenden(art) {
    window._elmaArt = art === 'annual' ? 'annual' : 'monthly';
    showPage('elm-anwender');
}

function elmaZurueck() {
    showPage('hr-hub');
    if (typeof hrHubShowCat === 'function') hrHubShowCat('elm');
}

function elmaInit() {
    const monat = document.getElementById('elmaMonat');
    if (monat && !monat.options.length) {
        monat.innerHTML = _ELMA_MONATE.map((n, i) => `<option value="${i + 1}">${n}</option>`).join('');
    }
    const jahr = document.getElementById('elmaJahr');
    if (jahr && !jahr.value) {
        const h = new Date();
        jahr.value = h.getMonth() === 0 ? h.getFullYear() - 1 : h.getFullYear();
        if (monat) monat.value = String(h.getMonth() === 0 ? 12 : h.getMonth());
    }
    const art = window._elmaArt;
    window._elmaArt = null;
    if (art) {
        const artEl = document.getElementById('elmaArt');
        if (artEl) artEl.value = art;
    }
    _elmaVorschau = null;
    _elmaErsatz = null;
    _elmaHinweis = null;
    elmaArtGewechselt();
    elmaErsatzZeichnen();
    elmaLaden();
}

function elmaArtGewechselt() {
    const art = document.getElementById('elmaArt')?.value;
    const w = document.getElementById('elmaMonatWrap');
    if (w) w.style.display = art === 'annual' ? 'none' : 'flex';
    const hin = document.getElementById('elmaArtHinweis');
    if (hin) hin.textContent = art === 'annual'
        ? 'Jahresmeldung an die AHV-Ausgleichskasse.'
        : 'Monatsmeldung an die Steuerverwaltungen und ans Bundesamt für Statistik.';
    _elmaVorschau = null;
    const vorschau = document.getElementById('elmaVorschau');
    if (vorschau) vorschau.innerHTML = '';
}

function _elmaAuswahl() {
    const art = document.getElementById('elmaArt')?.value || 'monthly';
    return {
        art,
        jahr: parseInt(document.getElementById('elmaJahr')?.value, 10),
        monat: art === 'monthly' ? parseInt(document.getElementById('elmaMonat')?.value, 10) : null,
    };
}

async function elmaLaden() {
    try {
        const r = await fetch('/api/elm/meldungen', { headers: ah(), cache: 'no-store' });
        if (!r.ok) throw new Error('Fehler ' + r.status);
        _elma = await r.json();
    } catch (e) {
        const el = document.getElementById('elmaListe');
        if (el) el.innerHTML = `<div style="color:#991b1b;font-size:13px">${_elmaEsc(e.message)}</div>`;
        return;
    }
    elmaListeZeichnen();
    elmaPlanen();
}

async function elmaPruefen() {
    const a = _elmaAuswahl();
    const el = document.getElementById('elmaVorschau');
    if (!a.jahr) { if (el) el.innerHTML = _elmaKasten(false, 'Bitte ein Jahr angeben.'); return; }
    if (_elmaBusy) return;
    _elmaBusy = true;
    const btn = document.getElementById('elmaPruefenBtn');
    if (btn) btn.disabled = true;
    if (el) el.innerHTML = '';
    _elmaWarte('Meldung wird geprüft', 'OneCrew baut die Meldung und schaut, ob sie so rausgehen kann.');
    try {
        const q = new URLSearchParams({ art: a.art, jahr: String(a.jahr) });
        if (a.monat) q.set('monat', String(a.monat));
        const r = await fetch('/api/elm/meldungen/vorschau?' + q, { headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(j.message || 'Fehler ' + r.status);
        _elmaVorschau = Object.assign(j, { auswahl: a });
    } catch (e) {
        _elmaVorschau = null;
        if (el) el.innerHTML = _elmaKasten(false, _elmaEsc(e.message));
    } finally {
        _elmaWarte(null);
        _elmaBusy = false;
        if (btn) btn.disabled = false;
        elmaVorschauZeichnen();
    }
}

function _elmaKasten(ok, html) {
    const f = ok ? _ELMA_STUFE.ok : _ELMA_STUFE.rot;
    return `<div style="margin-top:14px;padding:10px 12px;border-radius:10px;background:${f.grund};color:${f.text};font-size:13px">${html}</div>`;
}

function elmaVorschauZeichnen() {
    const el = document.getElementById('elmaVorschau');
    if (!el || !_elmaVorschau) return;
    const v = _elmaVorschau;
    const fehler = v.fehler || [];
    const leer = !(v.empfaenger || []).length;
    const kann = !fehler.length && !(v.technisch || []).length && !leer && !v.sendenGesperrt;
    el.innerHTML = `
        <div style="margin-top:16px;padding-top:14px;border-top:1px solid rgba(60,55,48,0.12)">
            <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
                <b style="color:#3f3f3f;font-size:14px">${_elmaEsc(v.titel)}</b>
                ${kann ? _elmaPille('ok', 'Bereit zum Senden') : _elmaPille('rot', leer ? 'Keine Empfänger' : 'Bitte zuerst korrigieren')}
            </div>
            <div style="margin-top:8px;font-size:13px;color:#646464">Geht an:
                ${leer ? '<b style="color:#991b1b">niemanden — die Meldung ist leer</b>'
                       : v.empfaenger.map(e => `<span style="display:inline-block;margin:3px 6px 0 0;padding:3px 10px;border-radius:999px;border:1px solid rgba(60,55,48,0.2);font-size:12px">${_elmaEsc(e.name)}</span>`).join('')}
            </div>
            ${fehler.length ? _elmaKasten(false, `<ul style="margin:0 0 0 18px;padding:0">${fehler.map(f => `<li>${_elmaEsc(f)}</li>`).join('')}</ul>`) : ''}
            ${(v.technisch || []).length ? _elmaKasten(false, 'Die Meldung ist fehlerhaft. Bitte dem Support melden.') : ''}
            ${v.sendenGesperrt ? _elmaKasten(false, _elmaEsc(v.sendenGesperrt)) : ''}
            <div style="display:flex;justify-content:flex-end;margin-top:14px">
                <button type="button" class="btn btn-primary" onclick="elmaSenden()" ${kann && !_elmaBusy ? '' : 'disabled'}>${_elmaErsatz ? 'Korrigierte Meldung senden' : 'Senden'}</button>
            </div>
        </div>`;
}

async function elmaSenden() {
    const v = _elmaVorschau;
    if (!v || _elmaBusy) return;
    const frage = _elmaErsatz
        ? `${v.titel} als Ersatz für die Meldung vom ${_elmaDatum(_elmaErsatz.gesendet)} senden?`
        : `${v.titel} senden?`;
    if (!(await liquidConfirm(frage, { title: 'Meldung senden', yesLabel: 'Senden', noLabel: 'Abbrechen' }))) return;
    const body = Object.assign({}, v.auswahl, { ersetztId: _elmaErsatz?.id || null, ziel: null });
    const j = await _elmaPost('senden', body, 'Meldung wird gesendet', 'OneCrew übergibt sie an Swissdec.');
    if (j?.ok) {
        _elmaVorschau = null;
        _elmaErsatz = null;
        elmaErsatzZeichnen();
        const el = document.getElementById('elmaVorschau');
        if (el) el.innerHTML = _elmaKasten(true, _elmaEsc(j.meldung || 'Gesendet.'));
        _elmaHinweis = null;
        elmaListeZeichnen();
        elmaPlanen();
    }
}

function elmaErsatzZeichnen() {
    const box = document.getElementById('elmaErsatz');
    if (!box) return;
    const e = _elmaErsatz;
    box.style.display = e ? '' : 'none';
    box.innerHTML = e
        ? `Ersetzt die <b>${_elmaEsc(e.titel)}</b> vom ${_elmaDatum(e.gesendet)}.
           <a href="#" onclick="elmaErsatzAbbrechen();return false" style="margin-left:8px;color:inherit">Abbrechen</a>`
        : '';
}

function elmaErsetzen(id) {
    const m = (_elma?.meldungen || []).find(x => x.id === id);
    if (!m) return;
    _elmaErsatz = { id: m.id, art: m.art, jahr: m.jahr, monat: m.monat, titel: m.titel, gesendet: m.gesendet };
    const art = document.getElementById('elmaArt');
    const jahr = document.getElementById('elmaJahr');
    const monat = document.getElementById('elmaMonat');
    if (art) art.value = m.art === 'annual' ? 'annual' : 'monthly';
    if (jahr && m.jahr) jahr.value = m.jahr;
    if (monat && m.monat) monat.value = String(m.monat);
    _elmaVorschau = null;
    elmaArtGewechselt();
    elmaErsatzZeichnen();
    document.getElementById('page-elm-anwender')?.querySelector('.page-body')?.scrollTo?.({ top: 0, behavior: 'smooth' });
    elmaPruefen();
}

function elmaErsatzAbbrechen() {
    _elmaErsatz = null;
    _elmaVorschau = null;
    elmaErsatzZeichnen();
    const el = document.getElementById('elmaVorschau');
    if (el) el.innerHTML = '';
}

async function _elmaPost(pfad, body, warteTitel, warteText) {
    if (_elmaBusy) return null;
    _elmaBusy = true;
    _elmaHinweis = null;
    _elmaWarte(warteTitel || 'OneCrew arbeitet', warteText || 'Bitte einen Moment.');
    try {
        const r = await fetch('/api/elm/meldungen/' + pfad, { method: 'POST', headers: ah(), body: JSON.stringify(body || {}) });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { _elmaHinweis = { ok: false, text: j.message || 'Fehler ' + r.status }; return null; }
        if (j.stand) _elma = j.stand;
        _elmaHinweis = j.meldung ? { ok: !!j.ok, text: j.meldung } : null;
        return j;
    } catch (e) {
        _elmaHinweis = { ok: false, text: 'Server nicht erreichbar.' };
        return null;
    } finally {
        _elmaWarte(null);
        _elmaBusy = false;
        elmaListeZeichnen();
    }
}

function elmaListeZeichnen() {
    const el = document.getElementById('elmaListe');
    if (!el) return;
    const alle = (_elma?.meldungen || []).map((m, mi) => ({ m, mi })).filter(x => !x.m.zertifizierung);
    const offen = alle.filter(x => !x.m.fertig);
    const fertig = alle.filter(x => x.m.fertig);
    const hinweis = _elmaHinweis
        ? `<div style="margin-bottom:10px;padding:10px 12px;border-radius:10px;font-size:13px;background:${_elmaHinweis.ok ? _ELMA_STUFE.ok.grund : _ELMA_STUFE.rot.grund};color:${_elmaHinweis.ok ? _ELMA_STUFE.ok.text : _ELMA_STUFE.rot.text}">${_elmaEsc(_elmaHinweis.text)}</div>`
        : '';
    const kopf = `<div style="display:flex;align-items:center;gap:10px;margin-bottom:8px">
        <div style="font-size:13px;font-weight:700;color:#3f3f3f">Gesendete Meldungen</div>
        <button type="button" class="btn btn-secondary" style="margin-left:auto" onclick="elmaLaden()">Aktualisieren</button></div>`;
    if (!_elma) { el.innerHTML = ''; return; }
    if (!alle.length) {
        el.innerHTML = kopf + hinweis + '<div style="font-size:13px;color:#8b8b8b">Noch keine Meldung gesendet.</div>';
        return;
    }
    el.innerHTML = kopf + hinweis
        + (offen.length ? offen.map(x => _elmaZeile(x.m, x.mi)).join('') : '<div style="font-size:13px;color:#8b8b8b;margin-bottom:8px">Keine offenen Meldungen.</div>')
        + (fertig.length ? `<details style="margin-top:10px"><summary style="cursor:pointer;font-size:13px;color:#646464">Abgeschlossene Meldungen (${fertig.length})</summary>
            <div style="margin-top:8px">${fertig.map(x => _elmaZeile(x.m, x.mi)).join('')}</div></details>` : '');
}

function elmaDetails(id) {
    if (_elmaOffen.has(id)) _elmaOffen.delete(id);
    else _elmaOffen.add(id);
    elmaListeZeichnen();
}

function _elmaZeile(m, mi) {
    const offen = _elmaOffen.has(m.id);
    const f = _ELMA_STUFE[m.stufe] || _ELMA_STUFE.grau;
    const id = _elmaEsc(m.id);
    return `<div style="margin-bottom:8px;border-radius:12px;border:1px solid rgba(60,55,48,0.14);background:rgba(255,255,255,0.55)">
        <div style="padding:10px 14px;display:flex;align-items:center;gap:10px">
            <span style="width:10px;height:10px;border-radius:50%;background:${f.punkt};flex:0 0 auto"></span>
            <b style="color:#3f3f3f;font-size:14px;white-space:nowrap">${_elmaEsc(m.titel)}</b>
            ${m.ersatz ? '<span style="font-size:11px;color:#646464;white-space:nowrap">Ersatz</span>' : ''}
            <span style="font-size:12px;color:#8b8b8b;white-space:nowrap">${_elmaDatum(m.gesendet)}</span>
            <span style="margin-left:auto;white-space:nowrap">${_elmaPille(m.stufe, m.text)}</span>
            <button type="button" class="btn btn-secondary" style="white-space:nowrap" onclick="elmaDetails('${id}')">${offen ? 'Schliessen' : 'Details'}</button>
        </div>
        ${offen ? `<div style="padding:0 14px 14px">
            ${(m.empfaenger || []).map((e, ei) => _elmaEmpfaenger(m, mi, e, ei)).join('')}
            <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:8px">
                ${m.ersetzbar ? `<button type="button" class="btn btn-secondary" onclick="elmaErsetzen('${id}')">Korrigierte Meldung senden</button>` : ''}
            </div>
        </div>` : ''}
    </div>`;
}

function _elmaEmpfaenger(m, mi, e, ei) {
    const freigabe = e.freigabeUrl
        ? `<div style="margin-top:8px"><button type="button" class="btn btn-primary" onclick="elmaFreigeben(${mi}, ${ei})">Beim Empfänger freigeben</button></div>` : '';
    const nachrichten = (e.nachrichten || []).map((n, ni) => _elmaNachricht(mi, ei, n, ni, e.name)).join('');
    if (!freigabe && !nachrichten && !(e.hinweise || []).length) {
        return `<div style="padding:8px 0;border-top:1px solid rgba(60,55,48,0.10);display:flex;gap:10px;align-items:center">
            <span style="font-size:13px;color:#3f3f3f">${_elmaEsc(e.name)}</span>
            <span style="margin-left:auto">${_elmaPille(e.stufe, e.text)}</span></div>`;
    }
    return `<div style="padding:10px 0;border-top:1px solid rgba(60,55,48,0.10)">
        <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
            <b style="font-size:13px;color:#3f3f3f">${_elmaEsc(e.name)}</b>
            <span style="margin-left:auto">${_elmaPille(e.stufe, e.text)}</span>
        </div>
        ${(e.hinweise || []).map(h => `<div style="font-size:12px;color:#646464;margin-top:4px">${_elmaEsc(h)}</div>`).join('')}
        ${freigabe}${nachrichten}
    </div>`;
}

const _ELMA_TYP = {
    String: 'Text', Integer: 'Ganze Zahl', Double: 'Zahl', Amount: 'Betrag in Franken',
    Boolean: 'Ja oder Nein', YesNoUnknown: 'Ja, Nein oder unbekannt', Date: 'Datum', DateTime: 'Datum und Uhrzeit',
};

function _elmaTypName(typ) { return _ELMA_TYP[typ] || 'Angabe'; }

function _elmaFrageTitel(p) {
    const raw = (p.label || '').trim();
    const m = raw.match(/^(.*?)\s*::\s*(String|Integer|Double|Boolean|Date|Datetime|DateTime|Amount|Yes, No or unknown)\s*$/i);
    const kern = (m ? m[1] : raw).replace(/^Answer\s*/i, '').replace(/^Value\s*/i, '').trim();
    const ohneKlammer = kern.replace(/^\((.*)\)$/, '$1').trim();
    if (!ohneKlammer || /^(value only|default only|both)$/i.test(ohneKlammer)) return _elmaTypName(p.frageTyp || p.wertTyp);
    return ohneKlammer;
}

function _elmaAnzeigeWert(typ, wert) {
    if (wert == null || wert === '') return '';
    if (typeof _uebDialogWert === 'function') return _uebDialogWert(typ, wert);
    return String(wert);
}

function _elmaIsoDatum(s) {
    if (!s) return '';
    const iso = /^(\d{4}-\d{2}-\d{2})/.exec(String(s));
    if (iso) return iso[1];
    const ch = /^(\d{1,2})\.(\d{1,2})\.(\d{4})/.exec(String(s));
    if (!ch) return '';
    return `${ch[3]}-${ch[2].padStart(2, '0')}-${ch[1].padStart(2, '0')}`;
}

function _elmaFeld(id, p, gesperrt) {
    const typ = p.frageTyp || 'String';
    const vor = p.antwort ?? p.default ?? '';
    const dis = gesperrt ? 'disabled' : '';
    const jaNein = (werte, breite) => {
        const aktuell = _elmaAnzeigeWert(typ, vor).toLowerCase();
        return `<div style="width:${breite}"><select id="${id}" ${dis}>${werte.map(w =>
            `<option value="${w}" ${w === aktuell ? 'selected' : ''}>${w}</option>`).join('')}</select></div>`;
    };
    if (typ === 'Boolean') return jaNein(['ja', 'nein'], '7rem');
    if (typ === 'YesNoUnknown') return jaNein(['ja', 'nein', 'unbekannt'], '11rem');
    if (typ === 'Date')
        return `<input id="${id}" type="date" value="${_elmaEsc(_elmaIsoDatum(vor))}" ${dis} style="${_ELMA_FELD};width:11rem">`;
    if (typ === 'DateTime')
        return `<input id="${id}" type="text" value="${_elmaEsc(_elmaAnzeigeWert(typ, vor))}" placeholder="TT.MM.JJJJ HH:MM" ${dis} style="${_ELMA_FELD};width:14rem">`;
    const breite = typ === 'Amount' || typ === 'Double' ? '10rem' : typ === 'Integer' ? '8rem' : '22rem';
    return `<input id="${id}" type="text" value="${_elmaEsc(_elmaAnzeigeWert(typ, vor))}" inputmode="${typ === 'String' ? 'text' : 'decimal'}" ${dis} style="${_ELMA_FELD};width:${breite}">`;
}

function _elmaNachricht(mi, ei, n, ni, empfaengerName) {
    if (!n.dialog) return '';
    const d = n.dialog;
    const abschnitte = d.abschnitte || [];
    const absaetze = d.absaetze || [];
    const gruppen = [];
    const bekannt = new Set(abschnitte.map(s => s.id));
    abschnitte.forEach(s => gruppen.push({ s, zeilen: absaetze.filter(p => p.abschnittRef === s.id) }));
    const lose = absaetze.filter(p => !p.abschnittRef || !bekannt.has(p.abschnittRef));
    if (lose.length) gruppen.push({ s: null, zeilen: lose });

    const block = g => {
        const werte = g.zeilen.filter(p => !p.istFrage && (p.wert || p.wert === 0));
        const fragen = g.zeilen.filter(p => p.istFrage);
        if (!werte.length && !fragen.length && !(g.s?.beschreibung)) return '';
        const kopf = g.s?.titel
            ? `<div style="font-weight:700;color:#3f3f3f;font-size:14px">${_elmaEsc(g.s.titel)}</div>` : '';
        const erklaerung = g.s?.beschreibung && g.s.beschreibung !== d.beschreibung
            ? `<div style="font-size:13px;color:#646464;margin-top:4px;line-height:1.45">${_elmaEsc(g.s.beschreibung)}</div>` : '';
        const kenntnis = werte.length
            ? `<div style="margin-top:8px;font-size:12px;color:#8b8b8b">Zur Kenntnis</div>`
              + werte.map(p => `<div style="display:flex;gap:12px;font-size:13px;margin-top:3px">
                    <span style="color:#646464">${_elmaEsc(_elmaFrageTitel(p))}</span>
                    <span style="color:#3f3f3f">${_elmaEsc(_elmaAnzeigeWert(p.wertTyp, p.wert))}</span></div>`).join('')
            : '';
        const felder = fragen.map(p => {
            const id = `elmaDlg-${mi}-${ei}-${ni}-${p.id}`;
            const hinweis = `${_elmaTypName(p.frageTyp)}${p.optional ? ', freiwillig' : ', Pflicht'}`;
            return `<label style="display:flex;flex-direction:column;align-items:flex-start;gap:4px;margin-top:12px">
                <span style="font-size:12px;color:#646464">${_elmaEsc(_elmaFrageTitel(p))}</span>
                ${_elmaFeld(id, p, n.beantwortet)}
                <span style="font-size:11px;color:#8b8b8b">${hinweis}</span>
                ${p.problem ? `<span style="font-size:12px;color:#991b1b">${_elmaEsc(p.problem)}</span>` : ''}
            </label>`;
        }).join('');
        return `<div style="margin-top:14px">${kopf}${erklaerung}${felder}${kenntnis}</div>`;
    };

    const fuss = !d.beantwortbar
        ? '<div style="font-size:13px;color:#646464;margin-top:10px">Nur zur Kenntnis. Eine Antwort ist nicht nötig.</div>'
        : n.beantwortet
            ? '<div style="font-size:13px;color:#166534;margin-top:12px">Die Antwort ist abgeschickt.</div>'
            : `<div style="display:flex;justify-content:flex-end;margin-top:16px">
                <button type="button" class="btn btn-primary" onclick="elmaAntworten(${mi}, ${ei}, ${ni})">Antwort senden</button></div>`;
    return `<div style="margin-top:12px;padding:16px 18px;border-radius:14px;border:1px solid rgba(60,55,48,0.12);
            background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%)">
        <div style="font-size:15px;font-weight:700;color:#3f3f3f">Rückfrage${empfaengerName ? ' von ' + _elmaEsc(empfaengerName) : ''}</div>
        <div style="font-size:13px;color:#646464;margin-top:4px;line-height:1.45">
            ${d.beantwortbar && !n.beantwortet ? 'Bitte die offenen Felder ausfüllen. Danach geht die Antwort an den Empfänger.' : ''}
        </div>
        ${d.titel ? `<div style="font-weight:600;color:#3f3f3f;margin-top:10px">${_elmaEsc(d.titel)}</div>` : ''}
        ${d.beschreibung ? `<div style="font-size:13px;color:#646464;margin-top:4px;line-height:1.45">${_elmaEsc(d.beschreibung)}</div>` : ''}
        ${gruppen.map(block).join('')}${fuss}
    </div>`;
}

async function elmaAntworten(mi, ei, ni) {
    const m = _elma?.meldungen?.[mi], e = m?.empfaenger?.[ei], n = e?.nachrichten?.[ni];
    if (!n?.dialog) return;
    const werte = {};
    const fehlt = [];
    n.dialog.absaetze.filter(p => p.istFrage).forEach(p => {
        const f = document.getElementById(`elmaDlg-${mi}-${ei}-${ni}-${p.id}`);
        const wert = f ? f.value.trim() : '';
        if (!wert && !p.optional) fehlt.push(_elmaFrageTitel(p));
        werte[p.id] = wert || null;
    });
    if (fehlt.length) {
        const st = document.getElementById('elmaStatus');
        if (st) {
            st.innerHTML = _elmaKasten(false, 'Bitte noch ausfüllen: ' + fehlt.map(_elmaEsc).join(', ') + '.');
            st.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
        return;
    }
    await _elmaPost(`${encodeURIComponent(m.id)}/antworten`, { addresseeId: e.addresseeId, storyId: n.storyId, werte },
        'Antwort wird gesendet', 'OneCrew schickt Ihre Angaben an den Empfänger.');
}

function elmaFreigeben(mi, ei) {
    const m = _elma?.meldungen?.[mi], e = m?.empfaenger?.[ei];
    if (!e?.freigabeUrl) return;
    window.open(e.freigabeUrl, '_blank', 'noopener,noreferrer');
    _elmaHinweis = { ok: true, text: `Freigabe für ${e.name} geöffnet.` };
    elmaListeZeichnen();
}

function elmaPlanen() {
    clearTimeout(_elmaTimer);
    _elmaTimer = setTimeout(async () => {
        if (!document.getElementById('page-elm-anwender')?.classList.contains('active')) return;
        const grenze = Date.now() - 10 * 60 * 1000;
        const m = (_elma?.meldungen || []).find(x => !x.fertig && !x.zertifizierung && x.stufe === 'laeuft' && new Date(x.gesendet).getTime() > grenze);
        if (!m) return;
        if (_elmaBusy) { elmaPlanen(); return; }
        try {
            const r = await fetch(`/api/elm/meldungen/${encodeURIComponent(m.id)}/abfragen`, { method: 'POST', headers: ah() });
            const j = await r.json().catch(() => ({}));
            if (r.ok && j.stand) { _elma = j.stand; elmaListeZeichnen(); }
        } catch (_) { /* nächster Versuch */ }
        elmaPlanen();
    }, 20000);
}
