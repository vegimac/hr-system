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
    if (el) el.innerHTML = '<div style="margin-top:14px;font-size:13px;color:#646464">Meldung wird geprüft …</div>';
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
    const j = await _elmaPost('senden', body);
    if (j?.ok) {
        _elmaVorschau = null;
        _elmaErsatz = null;
        elmaErsatzZeichnen();
        const el = document.getElementById('elmaVorschau');
        if (el) el.innerHTML = _elmaKasten(true, _elmaEsc(j.meldung || 'Gesendet.'));
        if (j.vorgangId) _elmaOffen.add(j.vorgangId);
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

async function _elmaPost(pfad, body) {
    if (_elmaBusy) return null;
    _elmaBusy = true;
    _elmaHinweis = null;
    elmaListeZeichnen();
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

function _elmaZeile(m, mi) {
    const offen = _elmaOffen.has(m.id) || m.aktionNoetig;
    const f = _ELMA_STUFE[m.stufe] || _ELMA_STUFE.grau;
    return `<details ${offen ? 'open' : ''} ontoggle="this.open ? _elmaOffen.add(this.dataset.id) : _elmaOffen.delete(this.dataset.id)" data-id="${_elmaEsc(m.id)}"
        style="margin-bottom:8px;border-radius:12px;border:1px solid rgba(60,55,48,0.14);background:rgba(255,255,255,0.55)">
        <summary style="cursor:pointer;list-style:none;padding:12px 14px;display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <span style="width:10px;height:10px;border-radius:50%;background:${f.punkt}"></span>
            <b style="color:#3f3f3f;font-size:14px">${_elmaEsc(m.titel)}</b>
            ${m.ersatz ? '<span style="font-size:11px;color:#646464">Ersatz</span>' : ''}
            <span style="font-size:12px;color:#8b8b8b">${_elmaDatum(m.gesendet)}</span>
            <span style="margin-left:auto">${_elmaPille(m.stufe, m.text)}</span>
        </summary>
        <div style="padding:0 14px 14px">
            ${(m.empfaenger || []).map((e, ei) => _elmaEmpfaenger(m, mi, e, ei)).join('')}
            <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:8px">
                ${m.ersetzbar ? `<button type="button" class="btn btn-secondary" onclick="elmaErsetzen('${_elmaEsc(m.id)}')">Korrigierte Meldung senden</button>` : ''}
            </div>
        </div>
    </details>`;
}

function _elmaEmpfaenger(m, mi, e, ei) {
    const freigabe = e.freigabeUrl
        ? `<div style="margin-top:8px"><button type="button" class="btn btn-primary" onclick="elmaFreigeben(${mi}, ${ei})">Beim Empfänger freigeben</button></div>` : '';
    const nachrichten = (e.nachrichten || []).map((n, ni) => _elmaNachricht(mi, ei, n, ni)).join('');
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

function _elmaNachricht(mi, ei, n, ni) {
    if (!n.dialog) return '';
    const d = n.dialog;
    const absatz = p => {
        if (!p.istFrage)
            return `<div style="display:flex;gap:10px;font-size:13px;margin-top:4px"><span style="color:#646464;min-width:160px">${_elmaEsc(p.label)}</span><span>${_elmaEsc(typeof _uebDialogWert === 'function' ? _uebDialogWert(p.wertTyp, p.wert) : (p.wert || ''))}</span></div>`;
        const feld = typeof _uebDialogFeld === 'function'
            ? _uebDialogFeld(`elmaDlg-${mi}-${ei}-${ni}-${p.id}`, p, n.beantwortet)
            : `<input id="elmaDlg-${mi}-${ei}-${ni}-${p.id}" type="text" ${n.beantwortet ? 'disabled' : ''}>`;
        return `<label style="display:flex;gap:10px;align-items:center;font-size:13px;margin-top:6px;flex-wrap:wrap">
            <span style="color:#646464;min-width:160px">${_elmaEsc(p.label)}${p.optional ? '' : ' *'}</span>${feld}</label>`;
    };
    const fuss = !d.beantwortbar
        ? ''
        : n.beantwortet
            ? '<div style="font-size:12px;color:#166534;margin-top:6px">Beantwortet.</div>'
            : `<div style="display:flex;justify-content:flex-end;margin-top:10px"><button type="button" class="btn btn-primary" onclick="elmaAntworten(${mi}, ${ei}, ${ni})">Antwort senden</button></div>`;
    return `<div style="margin-top:8px;padding:10px 12px;border-radius:10px;background:rgba(255,255,255,0.7)">
        ${d.titel ? `<div style="font-weight:600;color:#3f3f3f">${_elmaEsc(d.titel)}</div>` : ''}
        ${d.beschreibung ? `<div style="font-size:13px;color:#646464;margin-top:2px">${_elmaEsc(d.beschreibung)}</div>` : ''}
        ${(d.absaetze || []).map(absatz).join('')}${fuss}
    </div>`;
}

async function elmaAntworten(mi, ei, ni) {
    const m = _elma?.meldungen?.[mi], e = m?.empfaenger?.[ei], n = e?.nachrichten?.[ni];
    if (!n?.dialog) return;
    const werte = {};
    n.dialog.absaetze.filter(p => p.istFrage).forEach(p => {
        const f = document.getElementById(`elmaDlg-${mi}-${ei}-${ni}-${p.id}`);
        if (f) werte[p.id] = f.value.trim() || null;
    });
    await _elmaPost(`${encodeURIComponent(m.id)}/antworten`, { addresseeId: e.addresseeId, storyId: n.storyId, werte });
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
