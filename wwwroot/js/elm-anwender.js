// HR-Bereich: Meldung absetzen (Walter 30.10.2026).
// Dieselbe Schnittstelle wie die Entwicklungsseite «Lohnmeldungen (ELM)», ohne Zertifikat,
// Verbindungstest und Quality Tool. Server: ElmMeldungenController (api/elm/meldungen).

const _ELMA_MONATE = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
const _ELMA_ARTEN = [
    { id: 'monthly', name: 'Monatsmeldung', sub: 'Steuer und Statistik', kategorie: 'Meldung senden' },
    { id: 'annual', name: 'Jahresmeldung AHV', sub: 'Ausgleichskasse', kategorie: 'Meldung senden' },
    { id: 'grenz-it', name: 'Grenzgänger Italien', sub: 'Datenaustausch Italien', kategorie: 'Grenzgänger' },
    { id: 'grenz-fr', name: 'Grenzgänger Frankreich', sub: 'Wohnsitz Frankreich', kategorie: 'Grenzgänger' },
    { id: 'grenz-sfn', name: 'Grenzgänger SFN', sub: 'Tarifcode SFN', kategorie: 'Grenzgänger' },
];
const _ELMA_SEND = [
    ['Meldung erstellen', 'Personen, Beträge, Empfänger'],
    ['Leitung öffnen', 'Verbindung zu Swissdec'],
    ['Unterschreiben', 'Programm und Firma'],
    ['Verschlüsseln', 'Für Swissdec'],
    ['Senden', 'Übermittlung und Antwort'],
    ['Antwort prüfen', 'Auftrag ist angekommen'],
];
const _ELMA_QUERY = [
    ['Auftrag da', 'Swissdec hat ihn angenommen'],
    ['Leitung öffnen', 'Neue Verbindung'],
    ['Stand abfragen', 'Wo der Auftrag steht'],
    ['Antwort lesen', 'Noch unterwegs oder fertig'],
    ['Quittung holen', 'Nachrichten der Empfänger'],
    ['Abschluss', 'Nichts mehr offen'],
];
const _ELMA_STUFE = {
    ok:     { punkt: '#16a34a', text: '#166534', grund: 'rgba(22,163,74,0.10)' },
    warn:   { punkt: '#d97706', text: '#92400e', grund: 'rgba(217,119,6,0.12)' },
    rot:    { punkt: '#dc2626', text: '#991b1b', grund: 'rgba(220,38,38,0.10)' },
    laeuft: { punkt: '#6b7280', text: '#3f3f3f', grund: 'rgba(60,55,48,0.08)' },
    grau:   { punkt: '#b8b4ab', text: '#8b8b8b', grund: 'rgba(60,55,48,0.05)' },
};

let _elma = null;
let _elmaVorschau = null;
let _elmaVorschauTimer = 0;
let _elmaVorschauLauf = 0;
let _elmaErsatz = null;
let _elmaBusy = false;
let _elmaHinweis = null;
let _elmaTimer = null;
let _elmaOffen = new Set();
let _elmaArchivOffen = false;
let _elmaFokus = null;
let _elmaFlug = false;
let _elmaAnsicht = 'home';
let _elmaWahl = new Set(['annual', 'qst', 'statistik']);
let _elmaDomaenen = null;
let _elmaBvg = null;
let _elmaKantone = null;
let _elmaQuittung = null;
let _elmaExtra = new Set();

(function () {
    if (document.getElementById('elmaSanduhrStyle')) return;
    const s = document.createElement('style');
    s.id = 'elmaSanduhrStyle';
    s.textContent = '@keyframes elmaSanduhr{0%,40%{transform:rotate(0)}60%,100%{transform:rotate(180deg)}}'
        + '@keyframes elmaPuls{0%,100%{opacity:1}50%{opacity:.35}}'
        + '.elma-sanduhr{display:inline-block;font-size:36px;line-height:1;animation:elmaSanduhr 1.8s ease-in-out infinite}'
        + '.elma-puls{animation:elmaPuls 1.2s ease-in-out infinite}'
        + '@media (prefers-reduced-motion:reduce){.elma-sanduhr,.elma-puls{animation:none}}';
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
    const erlaubt = ['monthly', 'annual', 'grenz-it', 'grenz-fr', 'grenz-sfn'];
    window._elmaArt = erlaubt.includes(art) ? art : null;
    showPage('elm-anwender');
}

let _elmaDavor = 'jahr';
let _elmaLetzter = null;

function elmaFilialeId() {
    const id = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? Number(fixedCompanyProfileId) : 0;
    return id > 0 ? id : null;
}
function elmaFilialeName() {
    const id = elmaFilialeId();
    if (!id) return '';
    const b = (typeof allBranches !== 'undefined' && Array.isArray(allBranches))
        ? allBranches.find(x => Number(x.id) === id) : null;
    if (b && typeof filialKurzname === 'function') {
        const k = filialKurzname(b);
        if (k) return k;
    }
    return (b && (b.branchName || b.companyName)) || '';
}
function elmaFilialeWechsel() {
    _elmaVorschau = null;
    _elmaLetzter = null;
    const el = document.getElementById('elmaVorschau');
    if (el) el.innerHTML = '';
    elmaDomaenenLaden();
    elmaMonatStand(true);
}

function elmaZurueck() {
    if (_elmaAnsicht === 'konfig') { elmaAnsicht(_elmaDavor || 'jahr'); return; }
    showPage('hr-hub');
}

function elmaMehr() {
    const box = document.getElementById('elmaMehr');
    if (box) box.hidden = false;
}

function elmaMehrZu() {
    const box = document.getElementById('elmaMehr');
    if (box) box.hidden = true;
}

let _elmaCert = null;
let _elmaVerb = null;

async function elmaBereit() {
    const server = document.getElementById('elmaServerText');
    const zert = document.getElementById('elmaZertText');
    if (server) server.textContent = 'Verbindung wird geprüft';
    if (zert) zert.textContent = 'Zertifikat wird geprüft';
    const [cert, verb] = await Promise.all([elmaCertHolen(), elmaVerbindungHolen()]);
    _elmaCert = cert;
    _elmaVerb = verb;
    elmaBereitZeichnen();
    elmaKommZeichnen();
}

async function elmaCertHolen() {
    try {
        const r = await fetch('/api/elm/zertifikat', { headers: ah(), cache: 'no-store' });
        return r.ok ? await r.json() : null;
    } catch { return null; }
}

async function elmaVerbindungHolen() {
    try {
        const r = await fetch('/api/elm/meldungen/verbindung/pruefen', { method: 'POST', headers: ah() });
        if (r.ok) return await r.json();
        const g = await fetch('/api/elm/meldungen/verbindung', { headers: ah(), cache: 'no-store' });
        return g.ok ? await g.json() : null;
    } catch { return null; }
}

function _elmaZertAbgelaufen(sua) {
    return !sua?.vorhanden || (sua.restTage != null && sua.restTage < 0);
}

function elmaBereitZeichnen() {
    const sua = _elmaCert?.sua;
    const online = _elmaVerb?.letzte?.erreichbar?.stufe === 'ok';
    const abgelaufen = _elmaZertAbgelaufen(sua);
    const zertText = !sua ? 'Zertifikat nicht geprüft'
        : !sua.vorhanden ? 'Kein Zertifikat'
        : abgelaufen ? `Abgelaufen ${_elmaDatum(sua.gueltigBis)}`
        : `Gültig bis ${_elmaDatum(sua.gueltigBis)}`;
    const serverText = !_elmaVerb ? 'Swissdec nicht geprüft' : online ? 'Swissdec erreichbar' : 'Swissdec nicht erreichbar';
    const server = document.getElementById('elmaServerText');
    const zert = document.getElementById('elmaZertText');
    const dot = document.getElementById('elmaDot');
    const btn = document.getElementById('elmaZertBtn');
    if (server) server.textContent = serverText;
    if (zert) zert.textContent = zertText;
    if (dot) dot.className = 'elma-dot' + (online && sua?.vorhanden && !abgelaufen ? ' ok' : _elmaVerb && !online ? ' rot' : '');
    if (btn) {
        btn.disabled = !abgelaufen;
        btn.title = abgelaufen ? 'Neues Zertifikat bei Swissdec beantragen' : 'Das Zertifikat ist gültig.';
    }
    const sonder = document.getElementById('elmaSonder');
    if (_elmaAnsicht === 'konfig' && sonder && !sonder.contains(document.activeElement)) elmaMitteZeichnen();
}

async function elmaZertifikatErneuern() {
    const sua = _elmaCert?.sua;
    if (!_elmaZertAbgelaufen(sua)) return;
    if (!(await liquidConfirm('Das Zertifikat ist abgelaufen. OneCrew beantragt ein neues bei Swissdec.', {
        title: 'Zertifikat erneuern', yesLabel: 'Beantragen', noLabel: 'Abbrechen' }))) return;
    const v = _elmaCert?.vorlage || {};
    const zert = document.getElementById('elmaZertText');
    if (zert) zert.textContent = 'Wird beantragt …';
    try {
        const r = await fetch('/api/elm/zertifikat/registrieren', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                firma: v.firma, uid: v.uid, kontakt: v.kontakt, plz: v.plz, ort: v.ort,
                domain: v.domain, versicherer: v.versicherer, kundennummer: v.kundennummer,
                vertragsnummer: v.vertragsnummer, addresseeIdentification: v.addresseeIdentification,
            }),
        });
        const j = await r.json().catch(() => ({}));
        if (zert) zert.textContent = j.meldung || j.message || 'Nicht erledigt.';
    } catch {
        if (zert) zert.textContent = 'Server nicht erreichbar.';
    }
    setTimeout(elmaBereit, 1500);
}

function elmaJahrOptionen(jahr) {
    const feld = document.getElementById('elmaJahr');
    if (!feld) return;
    const heute = new Date().getFullYear();
    const von = Math.min(heute - 3, jahr);
    const bis = Math.max(heute + 1, jahr);
    const soll = [];
    for (let y = von; y <= bis; y++) soll.push(String(y));
    const haben = Array.from(feld.options).map(o => o.value);
    if (haben.join(',') === soll.join(',')) return;
    feld.innerHTML = soll.map(y => `<option value="${y}">${y}</option>`).join('');
}

function elmaInit() {
    const monat = document.getElementById('elmaMonat');
    if (monat && !monat.options.length) {
        monat.innerHTML = _ELMA_MONATE.map((n, i) => `<option value="${i + 1}">${n}</option>`).join('');
    }
    const jahr = document.getElementById('elmaJahr');
    if (jahr && !jahr.value) {
        const h = new Date();
        const y = h.getMonth() === 0 ? h.getFullYear() - 1 : h.getFullYear();
        const m = h.getMonth() === 0 ? 12 : h.getMonth();
        elmaJahrOptionen(y);
        jahr.value = String(y);
        if (monat) monat.value = String(m);
    }
    const art = window._elmaArt;
    window._elmaArt = null;
    _elmaVorschau = null;
    _elmaErsatz = null;
    _elmaHinweis = null;
    if (art) {
        const artEl = document.getElementById('elmaArt');
        if (artEl) artEl.value = art;
        if (art === 'monthly') _elmaAnsicht = 'monat';
        else {
            _elmaAnsicht = 'jahr';
            if (String(art).startsWith('grenz')) _elmaWahl.add(art);
        }
    } else _elmaAnsicht = 'monat';
    elmaAnsicht(_elmaAnsicht);
    elmaErsatzZeichnen();
    elmaZeitZeichnen();
    elmaLaden();
    elmaBereit();
    elmaDomaenenLaden();
}

function elmaArtWaehlen(id) {
    const el = document.getElementById('elmaArt');
    if (!el) return;
    el.value = id;
    const ansicht = id === 'monthly' ? 'monat' : 'jahr';
    if (_elmaAnsicht !== ansicht) { elmaAnsicht(ansicht); return; }
    elmaArtGewechselt();
}

function elmaArtSicht(name) {
    if (name !== 'jahr' && name !== 'monat' && name !== 'profil') return;
    elmaAnsicht(name);
}

function elmaAnsicht(name) {
    if (name === 'home' || name === 'aufgaben' || name === 'leistung' || name === 'finanz') name = 'jahr';
    const erlaubt = ['jahr', 'monat', 'konfig', 'profil'];
    if (name === 'konfig' && _elmaAnsicht !== 'konfig') _elmaDavor = _elmaAnsicht || 'jahr';
    _elmaAnsicht = erlaubt.includes(name) ? name : 'jahr';
    const art = document.getElementById('elmaArt');
    if (art) {
        if (_elmaAnsicht === 'monat') art.value = 'monthly';
        else if (_elmaAnsicht === 'jahr' && (art.value === 'monthly' || !art.value)) art.value = 'annual';
    }
    const sicht = document.getElementById('elmaArtSicht');
    if (sicht && (_elmaAnsicht === 'jahr' || _elmaAnsicht === 'monat' || _elmaAnsicht === 'profil')) {
        sicht.value = _elmaAnsicht;
        if (typeof sicht._lqRefresh === 'function') sicht._lqRefresh();
    }
    const home = document.getElementById('elmaHome');
    const arbeit = document.getElementById('elmaArbeit');
    const sonder = document.getElementById('elmaSonder');
    if (home) home.style.display = 'none';
    const inArbeit = _elmaAnsicht === 'jahr' || _elmaAnsicht === 'monat' || _elmaAnsicht === 'profil';
    if (arbeit) arbeit.style.display = inArbeit ? '' : 'none';
    if (sonder) {
        sonder.style.display = _elmaAnsicht === 'konfig' ? '' : 'none';
        sonder.innerHTML = _elmaAnsicht === 'konfig' ? elmaKonfigHtml() : '';
    }
    const titel = document.querySelector('#page-elm-anwender .elma-head h1');
    if (titel) titel.textContent = 'ELM-Cockpit';
    document.querySelectorAll('#page-elm-anwender [data-elma-nav]').forEach(b => {
        b.classList.toggle('an', b.dataset.elmaNav === _elmaAnsicht);
    });
    elmaDomaenenZeichnen();
    elmaHomeAufgaben();
    elmaArtGewechselt();
    elmaMonatStand(true);
}

function elmaHaken(id, an) {
    if (an) _elmaWahl.add(id); else _elmaWahl.delete(id);
    const art = document.getElementById('elmaArt');
    if (art && an && (id === 'annual' || String(id).startsWith('grenz-'))) art.value = id === 'annual' ? 'annual' : id;
    elmaDomaenenZeichnen();
}

function _elmaEmpfaengerNamen(prefix) {
    const namen = [];
    (_elmaDomaenen?.gruppen || []).forEach(g => (g.zeilen || []).forEach(z => {
        const id = String(z.id || '');
        if ((id === prefix || id.startsWith(prefix + '-')) && z.an && z.an !== 'noch keiner zugeordnet') namen.push(z.an);
    }));
    return [...new Set(namen)];
}

function _elmaCheck(id, label, prefix) {
    const an = _elmaEmpfaengerNamen(prefix);
    const anText = an.length ? `<small>an ${an.map(_elmaEsc).join(', ')}</small>` : '';
    return `<label class="elma-check"><input type="checkbox" ${ _elmaWahl.has(id) ? 'checked' : '' } onchange="elmaHaken('${id}', this.checked)"><span><b>${_elmaEsc(label)}</b>${anText}</span></label>`;
}

function elmaDomaenenZeichnen() {
    const box = document.getElementById('elmaDomaenen');
    if (!box || (_elmaAnsicht !== 'jahr' && _elmaAnsicht !== 'monat' && _elmaAnsicht !== 'profil')) return;
    if (_elmaAnsicht === 'profil') {
        box.innerHTML = elmaProfilHtml();
        return;
    }
    const portalAn = !!document.getElementById('elmaPortal')?.checked;
    if (_elmaAnsicht === 'monat') {
        box.innerHTML = elmaFilialeId()
            ? ''
            : '<p class="elma-an">Wähle links die Filiale. Die Monatsmeldung gilt nur für diese, nicht für alle zusammen.</p>';
        return;
    }
    box.innerHTML = `<div class="elma-domcols elma-domcols-4">
        <div><div class="elma-kicker">Ausgleichskassen</div>
            ${_elmaCheck('annual', 'AHV Jahresmeldung', 'ahv')}
            ${_elmaCheck('fak', 'FAK Jahresmeldung', 'fak')}</div>
        <div><div class="elma-kicker">Versicherungen</div>
            ${_elmaCheck('uvg', 'UVG Jahresmeldung', 'uvg')}
            ${_elmaCheck('uvgz', 'UVGZ Jahresmeldung', 'uvgz')}
            ${_elmaCheck('ktg', 'KTG Jahresmeldung', 'ktg')}
            <label class="elma-check" id="elmaPortalWrap" title="In den Übermittlungsprozessen der Versicherungen wird üblicherweise ein Link auf das Portal der Versicherung zurückgegeben. Falls ein Link ins Portal ausdrücklich gewünscht wird, kann das bei der Übermittlung mitgegeben werden.">
                <input type="checkbox" id="elmaPortal" ${portalAn ? 'checked' : ''}><span><b>Freigabe im Portal gewünscht</b></span>
            </label></div>
        <div><div class="elma-kicker">Vorsorgeeinrichtungen</div>
            ${_elmaCheck('bvg', 'BVG Jahresmeldung', 'bvg')}</div>
        <div><div class="elma-kicker">Steuern</div>
            ${_elmaCheck('lohnausweis', 'Lohnausweise', 'lohnausweis')}
            ${_elmaCheck('grenz-sfn', 'Grenzgänger SFN', 'grenz-sfn')}
            ${_elmaCheck('grenz-fr', 'Grenzgänger Frankreich', 'grenz-fr')}
            ${_elmaCheck('grenz-it', 'Grenzgänger Italien', 'grenz-it')}</div>
    </div>`;
    const offen = ['fak', 'uvg', 'uvgz', 'ktg', 'lohnausweis', 'bvg'].filter(id => _elmaWahl.has(id));
    if (offen.length) {
        box.insertAdjacentHTML('beforeend', '<p class="elma-an" style="margin-top:8px">Angekrenzt und adressiert. Die Lohndaten von FAK, UVG, UVGZ, KTG, BVG und den Lohnausweisen stehen noch nicht in der Jahresdatei, deshalb geht dort noch nichts raus. AHV und die Grenzgänger werden übermittelt.</p>');
    }
}

function elmaExtra(id, an) {
    if (an) _elmaExtra.add(id); else _elmaExtra.delete(id);
    elmaMitteZeichnen();
    if (_elmaBvg) elmaBvgZeichnen();
}

function elmaDomWaehlen(art) {
    const el = document.getElementById('elmaArt');
    if (!el || !art) return;
    el.value = art;
    elmaArtGewechselt();
    elmaMitteZeichnen();
}

async function elmaDomaenenLaden() {
    const jahr = parseInt(document.getElementById('elmaJahr')?.value, 10) || new Date().getFullYear();
    const fil = elmaFilialeId() || 0;
    try {
        const r = await fetch('/api/elm/meldungen/domaenen?jahr=' + jahr + '&companyProfileId=' + fil, { headers: ah(), cache: 'no-store' });
        if (r.ok) _elmaDomaenen = await r.json();
    } catch { /* Katalog bleibt leer, die Seite zeigt den Hinweis. */ }
    if (_elmaAnsicht === 'jahr' || _elmaAnsicht === 'monat') elmaDomaenenZeichnen();
    else if (_elmaAnsicht === 'konfig' || _elmaAnsicht === 'profil') elmaMitteZeichnen();
    elmaUeberZeichnen();
}
function elmaUeberZeichnen() {
    const box = document.getElementById('elmaUeber');
    if (!box) return;
    if (_elmaAnsicht !== 'monat' && _elmaAnsicht !== 'jahr') { box.innerHTML = ''; return; }
    const zeilen = [];
    for (const g of _elmaDomaenen?.gruppen || []) {
        for (const z of g.zeilen || []) {
            const id = String(z.id || '');
            const ahv = id === 'annual' || id.startsWith('ahv');
            const fak = id === 'fak' || id.startsWith('fak') || z.domain === 'FAK-CAF';
            if ((ahv || fak) && z.an && z.an !== 'noch keiner zugeordnet') zeilen.push(z);
        }
    }
    if (!zeilen.length) { box.innerHTML = ''; return; }
    const rows = zeilen.map(z => {
        const fak = z.domain === 'FAK-CAF' || String(z.id).startsWith('fak');
        const stand = fak ? 'Mit der AHV' : (_elmaLetzter && _elmaLetzter.monat ? 'Bereit' : '');
        return `<div class="elma-ueber-zeile"><b>${_elmaEsc(fak ? 'FAK' : 'AHV')}</b><span>${_elmaEsc(z.an)}</span><span>${_elmaEsc(stand)}</span></div>`;
    }).join('');
    box.innerHTML = `<div class="elma-kicker">Übermittlungen</div>${rows}`;
}
async function elmaMonatStand(setzen) {
    if (_elmaAnsicht !== 'monat') { elmaUeberZeichnen(); return; }
    const fil = elmaFilialeId();
    const jahr = elmaZeitLesen().jahr;
    _elmaLetzter = null;
    if (fil) {
        try {
            const r = await fetch('/api/elm/meldungen/uebersicht?jahr=' + jahr, { headers: ah(), cache: 'no-store' });
            if (r.ok) {
                const j = await r.json();
                const zeile = (j.filialen || []).find(f => Number(f.id) === fil);
                if (zeile) _elmaLetzter = {
                    monat: zeile.letzterMonat || 0,
                    text: zeile.letzterText || '',
                    monate: Array.isArray(zeile.abgeschlossen) ? zeile.abgeschlossen.map(Number) : null,
                    perioden: Array.isArray(zeile.lohnperioden) ? zeile.lohnperioden : null,
                };
            }
        } catch { /* Monat bleibt, wo er steht. */ }
    }
    const z = elmaZeitLesen();
    if (setzen && _elmaLetzter && _elmaLetzter.monat && _elmaLetzter.monat !== z.monat) {
        elmaZeitSetzen(z.jahr, _elmaLetzter.monat, { still: true });
        elmaVorschauPlanen();
        return;
    }
    elmaZeitZeichnen();
    elmaUeberZeichnen();
    elmaVorschauPlanen();
}

function elmaMitteZeichnen() {
    const sonder = document.getElementById('elmaSonder');
    if (!sonder || sonder.contains(document.activeElement)) return;
    if (_elmaAnsicht === 'konfig') sonder.innerHTML = elmaKonfigHtml();
    else if (_elmaAnsicht === 'profil') sonder.innerHTML = elmaProfilHtml();
}

function elmaHomeAufgaben() {
    const box = document.getElementById('elmaHomeAufgaben');
    if (!box) return;
    const aufgaben = [];
    (_elma?.meldungen || []).forEach(m => {
        if (m.zertifizierung) return;
        (m.empfaenger || []).forEach(e => {
            const dialog = (e.nachrichten || []).some(n => n.art === 'Rückfrage' && n.dialog && !n.beantwortet);
            if (dialog) aufgaben.push({ text: `Rückfrage von ${e.name}`, id: m.id, art: m.art });
            if (e.freigabeUrl) aufgaben.push({ text: `Freigabe bei ${e.name}`, id: m.id, art: m.art });
        });
    });
    const liste = aufgaben.length
        ? aufgaben.map(a => `<button type="button" class="elma-aufgabe" onclick="elmaAufgabe('${_elmaEsc(a.id)}','${_elmaEsc(a.art || '')}')">${_elmaEsc(a.text)}</button>`).join('')
        : '<div style="font-size:13px;color:#8b8b8b">Keine offenen Aufgaben.</div>';
    const fuell = aufgaben.length ? '100%' : '0%';
    box.innerHTML = `<h3>Aufgaben</h3>
        <div class="elma-aufgabe-spur"><i style="width:${fuell}"></i></div>
        <b style="display:block;margin:10px 0 4px;font-size:14px;color:#3f3f3f">${aufgaben.length} offene Aufgaben</b>
        <p style="font-size:12px;color:#646464;margin:0 0 10px">OneCrew holt sie selbst ab, mindestens einmal am Tag.</p>
        ${liste}
        <div style="margin-top:12px"><button type="button" class="btn btn-secondary" onclick="elmaAktualisieren()">Aktualisieren</button></div>`;
}

function elmaAufgabe(id, art) {
    _elmaFokus = id;
    elmaAnsicht(art === 'monthly' ? 'monat' : 'jahr');
}

function elmaJahrHtml() {
    const gruppen = _elmaDomaenen?.gruppen || [];
    if (!gruppen.length) return '<div style="font-size:13px;color:#8b8b8b">Empfänger werden geladen.</div>';
    const art = _elmaAktuelleArt();
    const bloecke = gruppen.map(g => {
        const nachName = new Map();
        (g.zeilen || []).forEach(z => {
            if (!nachName.has(z.name)) nachName.set(z.name, []);
            nachName.get(z.name).push(z);
        });
        const zeilen = [...nachName.entries()].map(([name, liste]) => {
            const namen = [...new Set(liste.map(z => z.an).filter(a => a && a !== 'noch keiner zugeordnet'))];
            const an = namen.length ? namen.join(', ') : 'noch keiner zugeordnet';
            const z = liste.find(x => x.art) || liste[0];
            const summen = liste.some(x => x.lohnsummen)
                ? `<button type="button" class="btn btn-secondary" onclick="elmaBvgLaden()">Lohnsummen aufbereiten</button>`
                : '';
            const marke = z.art
                ? (z.art === art ? '<span class="elma-an">diese Meldung</span>' : '')
                : '';
            return `<div class="elma-zeile"><b>${_elmaEsc(name)}</b><span class="elma-an">an <em>${_elmaEsc(an)}</em></span>${marke}${summen}</div>`;
        }).join('');
        return `<section class="elma-gruppe"><div class="elma-kicker">${_elmaEsc(g.name)}</div>${zeilen}</section>`;
    }).join('');
    return bloecke + '<div id="elmaBvg"></div>';
}

function elmaMonatHtml() {
    return `<section class="elma-gruppe">
        <div class="elma-kicker">Quellensteuer</div>
        <div class="elma-zeile"><b>Quellensteuer</b><span class="elma-an">an die Steuerverwaltung jedes Kantons mit Quellensteuer. Jeder Kanton lässt sich in der Meldung einzeln an- und abwählen.</span></div>
    </section>
    <section class="elma-gruppe">
        <div class="elma-kicker">Statistik</div>
        <div class="elma-zeile"><b>Statistik</b><span class="elma-an">an das <em>Bundesamt für Statistik</em></span></div>
    </section>`;
}

function elmaAufgabenHtml() {
    const aufgaben = [];
    (_elma?.meldungen || []).forEach(m => {
        if (m.zertifizierung) return;
        (m.empfaenger || []).forEach(e => {
            const dialog = (e.nachrichten || []).some(n => n.art === 'Rückfrage' && n.dialog && !n.beantwortet);
            if (dialog) aufgaben.push({ text: `Rückfrage von ${e.name}`, titel: m.titel });
            if (e.freigabeUrl) aufgaben.push({ text: `Freigabe bei ${e.name}`, titel: m.titel });
        });
    });
    const liste = aufgaben.length
        ? aufgaben.map(a => `<div class="elma-zeile"><b>${_elmaEsc(a.text)}</b><span class="elma-an">${_elmaEsc(a.titel)}</span></div>`).join('')
        : '<div style="font-size:13px;color:#8b8b8b">Keine offenen Aufgaben.</div>';
    return `<div class="elma-kicker">Aufgaben</div>
        <p style="font-size:13px;color:#646464;margin:0 0 10px">Rückfragen und Freigaben, die Empfänger an OneCrew schicken. OneCrew holt sie selbst ab, mindestens einmal am Tag.</p>
        ${liste}
        <div style="margin-top:12px"><button type="button" class="btn btn-secondary" onclick="elmaAktualisieren()">Aktualisieren</button></div>`;
}

function elmaKonfigHtml() {
    const v = _elmaCert?.vorlage || {};
    const sua = _elmaCert?.sua;
    const da = !!(sua?.vorhanden && !(sua.restTage != null && sua.restTage < 0));
    const firma = `<div class="elma-panelhead">
        <button type="button" class="btn btn-secondary" onclick="elmaZertifikatAktualisieren()">Aktualisieren</button></div>
    <section class="elma-panel elma-karte">
        <div class="elma-kicker">Swissdec Unternehmens-Authentifizierung (SUA)</div>
        <p style="font-size:13px;color:#3f3f3f;margin:0 0 10px">${da
            ? 'Sie verfügen über ein gültiges SUA-Zertifikat und können die bidirektionalen Prozesse von Swissdec nutzen.'
            : 'Noch kein gültiges Zertifikat. Es wird hier bestellt und danach mit dem Passwort aus dem Brief hinterlegt.'}</p>
        ${da ? '<span class="elma-sua-ok">SUA-Zertifikat gültig</span>' : ''}
        <div class="elma-kicker" style="margin-top:16px">Unternehmen</div>
        <div class="elma-kv2">
            <span>Firma</span><b>${_elmaEsc(v.firma || '—')}</b>
            <span>UID</span><b>${_elmaEsc(v.uid || '—')}</b>
            <span>Kontakt</span><b>${_elmaEsc(v.kontakt || '—')}</b>
            <span>PLZ / Ort</span><b>${_elmaEsc([v.plz, v.ort].filter(Boolean).join(' ') || '—')}</b>
        </div>
        <p style="font-size:13px;color:#92400e;margin:10px 0 0">Diese Angaben werden mit den offiziellen Registern abgeglichen. Abweichungen werden zurückgewiesen. Stimmt etwas nicht, zuerst den Hauptsitz anpassen.</p>
        <div class="elma-aktionen">
            ${da ? '' : '<button type="button" class="btn btn-primary" onclick="elmaZertifikatBestellen()">Zertifikat bestellen</button>'}
        </div>
        <div id="elmaKonfigHinweis" style="font-size:13px;color:#646464;margin-top:8px">${_elmaEsc(_elmaCert?.registrierung?.text || '')}</div>
        ${da ? '' : `<label style="display:flex;flex-direction:column;align-items:flex-start;gap:4px;margin-top:10px;font-size:12px;color:#646464">Registrierungspasswort aus dem Brief
            <input id="elmaBrief" type="text" autocomplete="one-time-code" style="${_ELMA_FELD};width:16rem">
            <button type="button" class="btn btn-primary" style="margin-top:8px" onclick="elmaZertifikatHinterlegen()">Zertifikat hinterlegen</button>
        </label>`}
    </section>`;
    const zeilen = _elmaAnmeldeZeilen().map(r => {
        const z = r.zeile;
        const anm = z && (_elma?.anmeldungen || []).find(m => (m.empfaenger || []).some(e => e.domain === z.domain && (e.name || '').includes(z.an)));
        const empf = anm?.empfaenger?.find(e => e.domain === z.domain);
        let aktion = '<span class="elma-an">Anmeldung, sobald das Zertifikat hinterlegt ist</span>';
        if (da && z) {
            aktion = anm
                ? `<span class="elma-sua-ok">Angemeldet</span> <button type="button" class="btn btn-secondary" onclick="elmaAbmelden('${_elmaEsc(anm.id)}','${_elmaEsc(empf?.addresseeId || '')}')">Abmelden</button>`
                : `<button type="button" class="btn btn-primary" onclick="elmaAnmelden('${_elmaEsc(z.domain)}','${_elmaEsc(z.an)}','${_elmaEsc(z.kundennummer || '')}','${_elmaEsc(z.vertragsnummer || '')}','${_elmaEsc(z.kennung || '')}')">Anmelden</button>`;
            if (anm && r.profil) aktion += ` <button type="button" class="btn btn-primary" onclick="elmaProfilSatz()">Versicherungsprofil bestellen</button>`;
        }
        return `<tr><td>${_elmaEsc(r.name)}</td><td>${_elmaEsc(z ? z.an : 'noch keiner zugeordnet')}</td><td class="elma-aktionen">${aktion}</td></tr>`;
    }).join('');
    return firma + `<section class="elma-panel elma-karte">
        <div class="elma-kicker">Anmeldung für den digitalen Datenaustausch</div>
        <p style="font-size:13px;color:#646464;margin:0 0 8px">Pro Standard und pro Empfänger eine Anmeldung. Im Lohnstandard ist sie für die Versicherungsprofile von UVG, UVGZ und KTG nötig, und für die Beitragsänderung der Domäne BVG.</p>
        <table class="elma-mit"><thead><tr><th>Domäne</th><th>Empfänger</th><th>Aktionen</th></tr></thead><tbody>${zeilen}</tbody></table>
        <div id="elmaProfilHinweis" style="font-size:13px;color:#646464;margin-top:10px"></div>
    </section>`;
}

function _elmaAnmeldeZeilen() {
    const fest = [
        { name: 'KTG', domain: 'KTG-AMC', prefix: 'ktg', profil: true },
        { name: 'UVG', domain: 'UVG-LAA', prefix: 'uvg', profil: true },
        { name: 'UVGZ', domain: 'UVGZ-LAAC', prefix: 'uvgz', profil: true },
        { name: 'BVG', domain: 'BVG-LPP', prefix: 'bvg', profil: false },
    ];
    const rows = [];
    fest.forEach(f => {
        const treffer = [];
        (_elmaDomaenen?.gruppen || []).forEach(g => (g.zeilen || []).forEach(z => {
            const id = String(z.id || '');
            if ((id === f.prefix || id.startsWith(f.prefix + '-')) && z.an && z.an !== 'noch keiner zugeordnet' && z.domain) treffer.push(z);
        }));
        if (!treffer.length) rows.push({ ...f, zeile: null });
        else treffer.forEach(z => rows.push({ ...f, zeile: z }));
    });
    return rows;
}

function elmaStandardHtml(titel, text) {
    return `<section class="elma-panel elma-karte"><h3>${_elmaEsc(titel)}</h3><p style="font-size:13px;color:#3f3f3f;margin:0">${_elmaEsc(text)}</p></section>`;
}

function elmaProfilSatz() {
    const hin = document.getElementById('elmaProfilHinweis');
    if (hin) hin.textContent = 'Eine eigene Bestellung gibt es nicht. Nach der Anmeldung schickt der Versicherer das Versicherungsprofil. Es erscheint unter Aufgaben.';
}

function elmaProfilHtml() {
    const rows = _elmaAnmeldeZeilen().filter(r => r.profil).map(r => {
        const z = r.zeile;
        const anm = z && (_elma?.anmeldungen || []).find(m => (m.empfaenger || []).some(e => e.domain === z.domain && (e.name || '').includes(z.an)));
        const aktion = anm
            ? `<button type="button" class="btn btn-primary" onclick="elmaProfilSatz()">Versicherungsprofil bestellen</button>`
            : `<button type="button" class="btn btn-secondary" onclick="elmaAnsicht('konfig')">Zuerst anmelden</button>`;
        return `<tr><td>${_elmaEsc(r.name)}</td><td>${_elmaEsc(z ? z.an : 'noch keiner zugeordnet')}</td><td>${aktion}</td></tr>`;
    }).join('');
    return `<div>
            <p style="font-size:13px;color:#646464;margin:0 0 8px">Profile von UVG, UVGZ und KTG. Sie treffen nach der Anmeldung als Aufgabe ein. Eine eigene Bestellung gibt es erst nach der Anmeldung.</p>
            <table class="elma-mit"><thead><tr><th>Domäne</th><th>Empfänger</th><th>Aktion</th></tr></thead><tbody>${rows}</tbody></table>
            <div id="elmaProfilHinweis" style="font-size:13px;color:#646464;margin-top:10px"></div>
        </div>`;
}

async function elmaZertifikatBestellen() {
    const v = _elmaCert?.vorlage || {};
    const hin = document.getElementById('elmaKonfigHinweis');
    if (hin) hin.textContent = 'Wird bestellt …';
    try {
        const r = await fetch('/api/elm/zertifikat/registrieren', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                firma: v.firma, uid: v.uid, kontakt: v.kontakt, plz: v.plz, ort: v.ort,
                domain: v.domain, versicherer: v.versicherer, kundennummer: v.kundennummer,
                vertragsnummer: v.vertragsnummer, addresseeIdentification: v.addresseeIdentification,
            }),
        });
        const j = await r.json().catch(() => ({}));
        if (hin) hin.textContent = j.meldung || j.message || 'Nicht erledigt.';
    } catch { if (hin) hin.textContent = 'Server nicht erreichbar.'; }
    setTimeout(elmaBereit, 800);
}

async function elmaZertifikatAktualisieren() {
    const hin = document.getElementById('elmaKonfigHinweis');
    if (hin) hin.textContent = 'Wird aktualisiert …';
    try {
        const r = await fetch('/api/elm/zertifikat/status', { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (j.stand) _elmaCert = j.stand;
        if (hin) hin.textContent = j.meldung || j.message || '';
    } catch { if (hin) hin.textContent = 'Server nicht erreichbar.'; }
    elmaBereitZeichnen();
}

async function elmaZertifikatHinterlegen() {
    const passwort = document.getElementById('elmaBrief')?.value?.trim();
    const hin = document.getElementById('elmaKonfigHinweis');
    if (!passwort) { if (hin) hin.textContent = 'Bitte das Passwort aus dem Brief eingeben.'; return; }
    try {
        const r = await fetch('/api/elm/zertifikat/abschliessen', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ passwort }),
        });
        const j = await r.json().catch(() => ({}));
        if (j.stand) _elmaCert = j.stand;
        if (hin) hin.textContent = j.meldung || j.message || '';
    } catch { if (hin) hin.textContent = 'Server nicht erreichbar.'; }
    elmaBereitZeichnen();
}

async function elmaAnmelden(domain, versicherer, kundennummer, vertragsnummer, kennung) {
    await _elmaPost('anmelden', { domain, versicherer, kundennummer, vertragsnummer, kennung },
        'Anmeldung', 'OneCrew meldet das Unternehmen an.');
    elmaMitteZeichnen();
}

async function elmaAbmelden(id, addresseeId) {
    if (!addresseeId) return;
    await _elmaPost('anmeldungen/' + encodeURIComponent(id) + '/abmelden', { addresseeId, storyId: '' },
        'Abmeldung', 'OneCrew meldet das Unternehmen ab.');
    elmaMitteZeichnen();
}

async function elmaBvgLaden() {
    const jahr = parseInt(document.getElementById('elmaJahr')?.value, 10);
    const box = document.getElementById('elmaBvg');
    if (!box || !jahr) return;
    box.innerHTML = '<div style="font-size:13px;color:#646464">Lohnsummen werden gerechnet.</div>';
    try {
        const r = await fetch('/api/elm/meldungen/bvg-lohnsummen?jahr=' + jahr, { headers: ah(), cache: 'no-store' });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(j.message || 'Fehler');
        _elmaBvg = j;
    } catch (e) {
        box.innerHTML = _elmaKasten(false, _elmaEsc(e.message));
        return;
    }
    elmaBvgZeichnen();
}

function elmaBvgZeichnen() {
    const box = document.getElementById('elmaBvg');
    const b = _elmaBvg;
    if (!box || !b) return;
    const zeilen = (b.zeilen || []).map(z => `<tr>
        <td>${_elmaEsc(z.name)}</td>
        <td>${_elmaGeld(z.vorschlag)}</td>
        <td><input id="elmaBvg-${z.employeeId}" inputmode="decimal" value="${_elmaEsc(String(z.betrag))}" style="${_ELMA_FELD}"></td>
    </tr>`).join('');
    box.innerHTML = `<section class="elma-gruppe">
        <div class="elma-kicker">Voraussichtliche Lohnsummen ${b.jahr}</div>
        <p style="font-size:13px;color:#646464;margin:0">Vorschlag aus den BVG-Basen des Jahres. Die Summe lässt sich anpassen und danach zur Übermittlung freigeben.</p>
        <table class="elma-bvg"><thead><tr><th>Person</th><th>Vorschlag</th><th>Lohnsumme</th></tr></thead><tbody>${zeilen || '<tr><td colspan="3">Keine Lohnabrechnungen in diesem Jahr.</td></tr>'}</tbody></table>
        <div class="elma-aktionen">
            <button type="button" class="btn btn-primary" onclick="elmaBvgFrei(true)">Zur Übermittlung freigeben</button>
            ${b.freigegeben ? '<span style="font-size:13px;color:#166534">Freigegeben</span>' : ''}
        </div>
    </section>`;
}

function _elmaGeld(n) {
    const x = Number(n);
    if (!isFinite(x)) return '';
    return x.toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

async function elmaBvgFrei(frei) {
    const b = _elmaBvg;
    if (!b) return;
    const zeilen = (b.zeilen || []).map(z => {
        const roh = document.getElementById('elmaBvg-' + z.employeeId)?.value || '';
        const betrag = Number(String(roh).replace(/'/g, '').replace(',', '.'));
        return { employeeId: z.employeeId, betrag: isFinite(betrag) ? betrag : z.vorschlag };
    });
    const r = await fetch('/api/elm/meldungen/bvg-lohnsummen', {
        method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ jahr: b.jahr, freigegeben: !!frei, zeilen }),
    });
    if (r.ok) { b.freigegeben = !!frei; elmaBvgZeichnen(); }
}

function elmaAktionenZeichnen() {
    const box = document.getElementById('elmaAktionen');
    if (!box) return;
    const art = _elmaAktuelleArt();
    box.innerHTML = _ELMA_ARTEN.map(a =>
        `<button type="button" class="elma-action${a.id === art ? ' active' : ''}" onclick="elmaArtWaehlen('${a.id}')"><b>${a.name}</b><small>${a.sub}</small></button>`
    ).join('');
    const meta = _ELMA_ARTEN.find(a => a.id === art) || _ELMA_ARTEN[0];
    const kat = document.getElementById('elmaKategorie');
    const op = document.getElementById('elmaOperation');
    if (kat) kat.textContent = meta.kategorie;
    if (op) op.textContent = meta.name;
    const hin = document.getElementById('elmaArtHinweis');
    if (hin) hin.textContent = art === 'monthly'
        ? 'Quellensteuer an die Kantone, Statistik an das Bundesamt für Statistik.'
        : 'Unten nur die Empfänger, die der Firma zugeordnet sind.';
}

function elmaZeitLesen() {
    const jahr = parseInt(document.getElementById('elmaJahr')?.value, 10) || new Date().getFullYear();
    const monat = parseInt(document.getElementById('elmaMonat')?.value, 10) || 1;
    return { jahr, monat };
}

function elmaZeitSetzen(jahr, monat, opt) {
    jahr = Math.min(2035, Math.max(2015, jahr));
    const feldJahr = document.getElementById('elmaJahr');
    const feldMonat = document.getElementById('elmaMonat');
    const alt = parseInt(feldJahr?.value, 10);
    elmaJahrOptionen(jahr);
    if (feldJahr) feldJahr.value = String(jahr);
    if (feldMonat && monat) feldMonat.value = String(monat);
    if (typeof feldJahr?._lqRefresh === 'function') feldJahr._lqRefresh();
    if (typeof feldMonat?._lqRefresh === 'function') feldMonat._lqRefresh();
    elmaZeitZeichnen();
    if (opt && opt.still) { elmaUeberZeichnen(); return; }
    if (jahr !== alt) elmaDomaenenLaden();
    elmaMonatStand(jahr !== alt);
}

function elmaZeitGeaendert() {
    const z = elmaZeitLesen();
    elmaZeitSetzen(z.jahr, z.monat);
}

function elmaZeitZeichnen() {
    const z = elmaZeitLesen();
    const stand = document.getElementById('elmaMonatStand');
    if (stand) {
        const bekannt = _elmaAnsicht === 'monat' && !!elmaFilialeId() && !!_elmaLetzter;
        let text = '';
        let klasse = '';
        if (bekannt) {
            const periode = Array.isArray(_elmaLetzter.perioden)
                ? _elmaLetzter.perioden.find(p => Number(p.monat) === z.monat)
                : null;
            if (Array.isArray(_elmaLetzter.perioden)) {
                if (!periode) { text = 'Keine Periode vorhanden'; klasse = 'ist-offen'; }
                else if (periode.status === 'abgeschlossen') { text = 'Periode abgeschlossen'; klasse = 'ist-zu'; }
                else { text = 'Periode offen'; klasse = 'ist-offen'; }
            } else {
                const zu = Array.isArray(_elmaLetzter.monate)
                    ? _elmaLetzter.monate.indexOf(z.monat) >= 0
                    : _elmaLetzter.monat > 0 && z.monat <= _elmaLetzter.monat;
                text = zu ? 'Periode abgeschlossen' : 'Periode offen';
                klasse = zu ? 'ist-zu' : 'ist-offen';
            }
        }
        stand.textContent = text;
        stand.className = klasse;
    }
}

function elmaArtGewechselt() {
    clearTimeout(_elmaVorschauTimer);
    _elmaVorschauLauf++;
    elmaZeitZeichnen();
    const monatFeld = document.getElementById('elmaZeitMonatFeld');
    if (monatFeld) monatFeld.style.display = _elmaAnsicht === 'monat' ? '' : 'none';
    const profil = _elmaAnsicht === 'profil';
    const prozess = document.getElementById('elmaProzess');
    if (prozess) prozess.disabled = _elmaAnsicht !== 'jahr';
    const pruefen = document.getElementById('elmaPruefenBtn');
    const senden = document.getElementById('elmaSendenBtn');
    if (pruefen) pruefen.disabled = profil;
    if (senden) senden.disabled = profil;
    const controls = document.getElementById('elmaControls');
    if (controls) controls.style.display = (_elmaAnsicht === 'jahr' || _elmaAnsicht === 'monat' || profil) ? '' : 'none';
    _elmaLageKey = '';
    _elmaVorschau = null;
    _elmaKantone = null;
    const vorschau = document.getElementById('elmaVorschau');
    if (vorschau) vorschau.innerHTML = '';
    elmaAktionenZeichnen();
    elmaListeZeichnen();
}

function _elmaPasstAnsicht(m) {
    const art = m.art || 'monthly';
    if (_elmaAnsicht === 'monat') return art === 'monthly';
    return art !== 'monthly';
}

function _elmaGewaehlt(v) {
    return (v?.empfaenger || []).filter(e => _elmaKantone == null || _elmaKantone.has(e.addresseeId));
}

function elmaKantonToggle(id, an) {
    const ids = (_elmaVorschau?.empfaenger || []).map(e => e.addresseeId);
    if (_elmaKantone == null) _elmaKantone = new Set(ids);
    if (an) _elmaKantone.add(id); else _elmaKantone.delete(id);
    elmaVorschauZeichnen();
}

function _elmaAktuelleArt() {
    return document.getElementById('elmaArt')?.value || 'monthly';
}

function _elmaPasstArt(m) {
    return (m.art || 'monthly') === _elmaAktuelleArt();
}

function _elmaAuswahl() {
    const art = document.getElementById('elmaArt')?.value || 'monthly';
    return {
        art,
        jahr: parseInt(document.getElementById('elmaJahr')?.value, 10),
        monat: art === 'monthly' ? parseInt(document.getElementById('elmaMonat')?.value, 10) : null,
        companyProfileId: elmaFilialeId(),
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

function elmaVorschauPlanen() {
    clearTimeout(_elmaVorschauTimer);
    if (_elmaAnsicht !== 'monat' || !elmaFilialeId()) return;
    _elmaKantone = null;
    _elmaVorschau = null;
    const el = document.getElementById('elmaVorschau');
    if (el) el.innerHTML = '<p class="elma-an" style="margin-top:14px">Meldung wird geprüft …</p>';
    _elmaVorschauTimer = setTimeout(function () { elmaPruefen({ auto: true }); }, 200);
}

async function elmaPruefen(opt) {
    const auto = !!(opt && opt.auto);
    const a = _elmaAuswahl();
    const el = document.getElementById('elmaVorschau');
    if (!a.jahr) { if (!auto && el) el.innerHTML = _elmaKasten(false, 'Bitte ein Jahr angeben.'); return; }
    if (!a.companyProfileId) { if (!auto && el) el.innerHTML = _elmaKasten(false, 'Wähle links die Filiale. Eine Meldung gilt nur für diese.'); return; }
    if (!auto && _elmaBusy) return;
    if (!auto) clearTimeout(_elmaVorschauTimer);
    const lauf = ++_elmaVorschauLauf;
    const btn = document.getElementById('elmaPruefenBtn');
    if (!auto) {
        _elmaBusy = true;
        if (btn) btn.disabled = true;
        if (el) el.innerHTML = '';
        _elmaWarte('Meldung wird geprüft', 'OneCrew baut die Meldung und schaut, ob sie so rausgehen kann.');
    }
    try {
        const q = new URLSearchParams({ art: a.art, jahr: String(a.jahr), companyProfileId: String(a.companyProfileId) });
        if (a.monat) q.set('monat', String(a.monat));
        const r = await fetch('/api/elm/meldungen/vorschau?' + q, { headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (lauf !== _elmaVorschauLauf) return;
        if (!r.ok) throw new Error(j.message || 'Fehler ' + r.status);
        _elmaVorschau = Object.assign(j, { auswahl: a });
    } catch (e) {
        if (lauf !== _elmaVorschauLauf) return;
        _elmaVorschau = null;
        if (el) el.innerHTML = _elmaKasten(false, _elmaEsc(e.message));
    } finally {
        if (!auto) {
            _elmaWarte(null);
            _elmaBusy = false;
            if (btn) btn.disabled = false;
        }
        if (lauf !== _elmaVorschauLauf) return;
        elmaLageZeichnen();
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
    const gewaehlt = _elmaGewaehlt(v);
    const leer = !gewaehlt.length;
    const kann = !fehler.length && !(v.technisch || []).length && !leer && !v.sendenGesperrt;
    const warnungen = v.warnungen || [];
    const empf = !(v.empfaenger || []).length
        ? '<span class="elma-send-leer">Niemanden — die Meldung ist leer</span>'
        : v.empfaenger.map(e => {
            const an = _elmaKantone == null || _elmaKantone.has(e.addresseeId);
            return `<label class="elma-send-chip" style="gap:6px;cursor:pointer"><input type="checkbox" ${an ? 'checked' : ''} onchange="elmaKantonToggle('${_elmaEsc(e.addresseeId)}', this.checked)"> ${_elmaEsc(e.name)}</label>`;
        }).join('');
    const hinweise = warnungen.length
        ? `<div class="elma-send-warn"><div class="elma-kicker">Hinweise</div><ul>${warnungen.map(w => `<li>${_elmaEsc(w)}</li>`).join('')}</ul></div>`
        : '';
    el.innerHTML = `
        <section class="elma-send">
            <div class="elma-send-head">
                <div>
                    <div class="elma-kicker">Meldung</div>
                    <h3>${_elmaEsc(v.titel)}</h3>
                </div>
                ${kann ? _elmaPille('ok', 'Bereit zum Senden') : _elmaPille('rot', leer ? 'Keine Empfänger' : 'Bitte zuerst korrigieren')}
            </div>
            <div class="elma-send-an">
                <div class="elma-kicker">Geht an</div>
                <div class="elma-send-chips">${empf}</div>
            </div>
            ${hinweise}
            ${fehler.length ? _elmaKasten(false, `<ul style="margin:0 0 0 18px;padding:0">${fehler.map(f => `<li>${_elmaEsc(f)}</li>`).join('')}</ul>`) : ''}
            ${(v.technisch || []).length ? _elmaKasten(false, 'Die Meldung ist fehlerhaft. Bitte dem Support melden.') : ''}
            ${v.sendenGesperrt ? _elmaKasten(false, _elmaEsc(v.sendenGesperrt)) : ''}
            <div class="elma-send-foot">
                <p>${kann ? 'OneCrew übergibt die Meldung an Swissdec.' : 'Senden bleibt zu, bis die Meldung in Ordnung ist.'}</p>
                <button type="button" class="btn btn-primary" onclick="elmaSenden()" ${kann && !_elmaBusy ? '' : 'disabled'}>${_elmaErsatz ? 'Ersatzmeldung' : 'Übermitteln'}</button>
            </div>
        </section>`;
}

async function elmaMonatSenden() {
    const art = document.getElementById('elmaArt');
    if (art) art.value = 'monthly';
    await elmaPruefen();
    if (!_elmaVorschau || (_elmaVorschau.fehler || []).length || (_elmaVorschau.technisch || []).length || _elmaVorschau.sendenGesperrt) return false;
    if (!_elmaGewaehlt(_elmaVorschau).length) {
        _elmaHinweis = { ok: false, text: 'Keine Empfänger in der Monatsmeldung.' };
        elmaListeZeichnen();
        return false;
    }
    await elmaSenden();
    return true;
}

async function elmaUebermitteln() {
    if (_elmaBusy || _elmaAnsicht === 'profil') return;
    if (_elmaAnsicht === 'monat') {
        await elmaMonatSenden();
        return;
    }
    const arts = [];
    if (_elmaWahl.has('annual')) arts.push('annual');
    ['grenz-sfn', 'grenz-fr', 'grenz-it'].forEach(a => { if (_elmaWahl.has(a)) arts.push(a); });
    if (!arts.length) {
        _elmaHinweis = { ok: false, text: 'AHV oder eine Grenzgängermeldung ankreuzen. FAK, UVG, UVGZ, KTG, BVG und die Lohnausweise sind adressiert, ihre Lohndaten stehen noch nicht in der Datei.' };
        elmaListeZeichnen();
        return;
    }
    for (const id of arts) {
        const art = document.getElementById('elmaArt');
        if (art) art.value = id;
        _elmaKantone = null;
        await elmaPruefen();
        if (!_elmaVorschau || (_elmaVorschau.fehler || []).length || (_elmaVorschau.technisch || []).length || _elmaVorschau.sendenGesperrt) return;
        await elmaSenden();
    }
}

async function elmaSenden() {
    const v = _elmaVorschau;
    if (!v || _elmaBusy) return;
    const adressaten = _elmaKantone == null ? null
        : Object.fromEntries((v.empfaenger || []).map(e => [e.addresseeId, _elmaKantone.has(e.addresseeId)]));
    const body = Object.assign({}, v.auswahl, {
        ersetztId: _elmaErsatz?.id || null,
        ziel: null,
        testmeldung: document.getElementById('elmaTest')?.checked !== false,
        freigabePortal: document.getElementById('elmaArt')?.value !== 'monthly' && !!document.getElementById('elmaPortal')?.checked,
        adressaten,
    });
    _elmaFlug = true;
    elmaKommZeichnen();
    const j = await _elmaPost('senden', body, 'Meldung wird gesendet', 'OneCrew übergibt sie an Swissdec.');
    _elmaFlug = false;
    if (j?.ok) {
        const neu = (_elma?.meldungen || []).find(m => !m.zertifizierung && _elmaPasstArt(m));
        if (neu) _elmaFokus = neu.id;
        _elmaVorschau = null;
        _elmaErsatz = null;
        elmaErsatzZeichnen();
        const el = document.getElementById('elmaVorschau');
        if (el) el.innerHTML = '';
        _elmaHinweis = null;
        elmaListeZeichnen();
        elmaPlanen();
    }
    elmaKommZeichnen();
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
    if (art && m.art) art.value = m.art;
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
        if (!r.ok) { _elmaHinweis = _elmaMensch(j.message || 'Fehler ' + r.status, false); return null; }
        if (j.stand) _elma = j.stand;
        _elmaHinweis = j.meldung ? _elmaMensch(j.meldung, !!j.ok) : null;
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

function _elmaAktionsleiste(fokus) {
    const tot = (label, prim) => `<button type="button" class="btn ${prim ? 'btn-primary' : 'btn-secondary'}" disabled>${label}</button>`;
    if (!fokus) return `<div class="elma-aktionen">${tot('Freigabe starten', true)}${tot('DialogMessage abholen')}${tot('Quittung anzeigen')}${tot('Ersatzmeldung')}</div>`;
    const m = fokus.m, mi = fokus.mi;
    const frei = (m.empfaenger || []).findIndex(e => e.freigabeUrl);
    const quittung = (m.empfaenger || []).some(e => (e.nachrichten || []).some(n => (n.art || '').indexOf('Quittung') >= 0));
    return `<div class="elma-aktionen">
        <button type="button" class="btn btn-primary" ${frei >= 0 ? '' : 'disabled'} onclick="elmaFreigeben(${mi}, ${frei})">Freigabe starten</button>
        <button type="button" class="btn btn-secondary" onclick="elmaDialogHolen('${_elmaEsc(m.id)}')">DialogMessage abholen</button>
        <button type="button" class="btn btn-secondary" ${quittung ? '' : 'disabled'} onclick="elmaQuittung('${_elmaEsc(m.id)}')">Quittung anzeigen</button>
        <button type="button" class="btn btn-secondary" ${m.ersetzbar ? '' : 'disabled'} onclick="elmaErsetzen('${_elmaEsc(m.id)}')">Ersatzmeldung</button>
    </div>`;
}

function _elmaDomKurz(domain) {
    const namen = {
        'AHV-AVS': 'AHV', 'FAK-CAF': 'FAK', 'UVG-LAA': 'UVG', 'UVGZ-LAAC': 'UVGZ', 'KTG-AMC': 'KTG',
        'BVG-LPP': 'BVG', 'TaxAtSource': 'Quellensteuer', 'Statistic': 'Statistik',
        'TaxCrossborder': 'Grenzgänger', 'Tax': 'Lohnausweise',
    };
    return namen[domain] || domain || '';
}
function _elmaStufeZeichen(stufe) {
    if (stufe === 'ok') return '✓';
    if (stufe === 'rot' || stufe === 'warn') return '!';
    return '○';
}
function _elmaEmpfAktion(m, mi, e, ei) {
    if (e.freigabeUrl) return `<button type="button" class="btn btn-primary" onclick="elmaFreigeben(${mi}, ${ei})">Freigabe starten</button>`;
    if (e.freigabeAbgelaufen) return '<span class="elma-an">Link abgelaufen</span>';
    const dialog = (e.nachrichten || []).some(n => n.dialog && n.dialog.beantwortbar && !n.beantwortet);
    if (dialog) return `<button type="button" class="btn btn-secondary" onclick="elmaDialogHolen('${_elmaEsc(m.id)}')">Dialog abholen</button>`;
    const quittung = (e.nachrichten || []).some(n => (n.art || '').indexOf('Quittung') >= 0);
    if (quittung) return `<button type="button" class="btn btn-secondary" onclick="elmaQuittung('${_elmaEsc(m.id)}')">Quittung anzeigen</button>`;
    return '';
}
function _elmaEmpfHinweis(m, e) {
    const n = (e.nachrichten || []).length;
    const hin = e.hinweise || [];
    if (!n && !hin.length) return '';
    const zahl = n || hin.length;
    const tip = hin[0] || (e.nachrichten || [])[0]?.art || '';
    return `<span class="elma-brief" title="${_elmaEsc(tip)}">${zahl}</span>`;
}
function elmaVerlaufWaehlen(id) {
    _elmaFokus = id;
    elmaListeZeichnen();
}
function elmaListeZeichnen() {
    elmaHomeAufgaben();
    elmaAufgabenZeichnen();
    const mitte = document.getElementById('elmaMitteilungen');
    const verlauf = document.getElementById('elmaVerlauf');
    if (!mitte || !verlauf) { elmaKommZeichnen(); return; }
    const alle = (_elma?.meldungen || []).map((m, mi) => ({ m, mi })).filter(x => !x.m.zertifizierung && _elmaPasstAnsicht(x.m));
    const gezeigt = alle.find(x => x.m.id === _elmaFokus) || alle[0] || null;
    const hinweis = _elmaHinweis
        ? `<div style="margin-bottom:10px;padding:10px 12px;border-radius:10px;font-size:13px;background:${_elmaHinweis.ok ? _ELMA_STUFE.ok.grund : _ELMA_STUFE.rot.grund};color:${_elmaHinweis.ok ? _ELMA_STUFE.ok.text : _ELMA_STUFE.rot.text}">${_elmaEsc(_elmaHinweis.text)}</div>`
        : '';
    let kopf = '<p class="elma-an">Noch keine Meldung.</p>';
    let tabelle = '';
    if (gezeigt) {
        const m = gezeigt.m;
        const zeilen = (m.empfaenger || []).map((e, ei) => `<tr>
            <td title="${_elmaEsc(e.text || '')}">${_elmaStufeZeichen(e.stufe)}</td>
            <td>${_elmaEsc(_elmaDomKurz(e.domain))}</td>
            <td>${_elmaEsc(e.addresseeId || e.name || '')}</td>
            <td>${_elmaEmpfHinweis(m, e)}</td>
            <td>${_elmaEmpfAktion(m, gezeigt.mi, e, ei)}</td>
        </tr>`).join('');
        const schein = _elmaQuittung === m.id
            ? (m.empfaenger || []).flatMap(e => (e.nachrichten || []).filter(n => (n.art || '').indexOf('Quittung') >= 0).map(n => `<pre class="elma-pre">${_elmaEsc(n.xml || '')}</pre>`)).join('')
            : '';
        const weg = (m.empfaenger || []).some(e => e.freigabeAbgelaufen) && !(m.empfaenger || []).some(e => e.freigabeUrl);
        kopf = `<p class="elma-id"><span>DeclarationID</span> <b>${_elmaEsc(m.declarationId || 'noch nicht vergeben')}</b><span>Zeit</span> <b>${_elmaEsc(_elmaDatum(m.gesendet) || '—')}</b></p>`;
        tabelle = `<table class="elma-mit"><thead><tr><th>Status</th><th>Domain</th><th>Empfänger</th><th>Benachrichtigung</th><th>Aktion</th></tr></thead>
            <tbody>${zeilen || '<tr><td colspan="5">Keine Empfänger in dieser Meldung.</td></tr>'}</tbody></table>
            ${weg ? '<p class="elma-an">Der Freigabe-Link ist abgelaufen. Neu prüfen und nochmals senden.</p>' : ''}${schein}`;
    }
    mitte.innerHTML = `<div class="elma-panelhead"><div class="elma-kicker" style="margin:0">Mitteilungen</div>
        <button type="button" class="btn btn-secondary" onclick="elmaAktualisieren()">Aktualisieren</button></div>
        ${hinweis}${kopf}${tabelle}`;
    const zeilenVerlauf = alle.map(({ m }) => `<button type="button" class="elma-verlauf-zeile${gezeigt && m.id === gezeigt.m.id ? ' an' : ''}" onclick="elmaVerlaufWaehlen('${_elmaEsc(m.id)}')">
        <span>${_elmaEsc(_elmaDatum(m.gesendet) || '—')}</span><span>${(m.empfaenger || []).length}</span></button>`).join('');
    verlauf.innerHTML = `<div class="elma-panelhead"><div class="elma-kicker" style="margin:0">Mitteilungsverlauf</div></div>
        <div class="elma-verlauf-kopf"><span>Zeit</span><span>Anzahl</span></div>
        ${zeilenVerlauf || '<p class="elma-an">Noch keine Meldung.</p>'}`;
    elmaKommZeichnen();
}

function elmaAufgabenZeichnen() {
    const box = document.getElementById('elmaAufgaben');
    if (!box || (_elmaAnsicht !== 'jahr' && _elmaAnsicht !== 'monat' && _elmaAnsicht !== 'profil')) return;
    const karten = [];
    const sua = _elmaCert?.sua;
    if (sua && sua.restTage != null && sua.restTage <= 30) {
        const weg = sua.restTage < 0 || !sua.vorhanden;
        karten.push({
            art: weg ? 'rot' : 'warn', aufgabe: true,
            titel: weg ? 'SUA-Zertifikat abgelaufen' : `SUA-Zertifikat läuft in ${sua.restTage} Tagen ab`,
            text: 'Erneuern Sie das Unternehmenszertifikat, damit die Übermittlung weiter möglich ist.',
            knopf: 'Zertifikat erneuern', click: 'elmaZertifikatErneuern()',
        });
    }
    const alle = (_elma?.meldungen || []).filter(m => !m.zertifizierung && _elmaPasstAnsicht(m));
    alle.forEach(m => {
        const id = _elmaEsc(m.id);
        const hinweise = (m.empfaenger || []).flatMap(e => e.hinweise || []);
        if (m.status === 'error' || m.stufe === 'rot') {
            karten.push({
                art: 'rot', aufgabe: true,
                titel: `Fehler in der ${m.titel || 'Meldung'}`,
                text: hinweise.length ? (hinweise.length === 1 ? hinweise[0] : `${hinweise.length} Hinweise in der Prüfung.`) : (m.statusText || 'Die Meldung wurde nicht angenommen.'),
                knopf: 'Details anzeigen', click: `elmaAnsehen('${id}')`,
            });
        }
        (m.empfaenger || []).forEach(e => {
            if ((e.nachrichten || []).some(n => n.art === 'Rückfrage' && n.dialog && !n.beantwortet))
                karten.push({ art: 'warn', aufgabe: true, titel: `Rückfrage von ${e.name}`, text: m.titel || '', knopf: 'Öffnen', click: `elmaAnsehen('${id}')` });
            if (e.freigabeUrl)
                karten.push({ art: 'info', aufgabe: true, titel: `Freigabe bei ${e.name}`, text: 'Die Freigabe im Portal steht bereit.', knopf: 'Öffnen', click: `elmaAnsehen('${id}')` });
        });
        const quittung = (m.empfaenger || []).some(e => (e.nachrichten || []).some(n => (n.art || '').indexOf('Quittung') >= 0));
        if (quittung && m.stufe !== 'rot')
            karten.push({ art: 'info', aufgabe: false, titel: 'Quittung der letzten Übermittlung vorhanden', text: 'Die Verarbeitung durch die Empfänger wurde bestätigt.', knopf: 'Quittung anzeigen', click: `elmaQuittung('${id}')` });
    });
    const offen = karten.filter(k => k.aufgabe).length;
    const liste = karten.length
        ? karten.map(k => `<article class="elma-hinweis elma-hinweis-${k.art}"><div><b>${_elmaEsc(k.titel)}</b><p>${_elmaEsc(k.text)}</p></div><button type="button" class="btn btn-secondary" onclick="${k.click}">${_elmaEsc(k.knopf)}</button></article>`).join('')
        : '<div style="font-size:13px;color:#8b8b8b">Keine offenen Aufgaben.</div>';
    box.innerHTML = `<div class="elma-panelhead"><div class="elma-kicker" style="margin:0">Aufgaben & Hinweise</div><span class="elma-an">${offen} ${offen === 1 ? 'offene Aufgabe' : 'offene Aufgaben'}</span></div>
        <div class="elma-aufgabe-spur" style="margin:10px 0 12px"><i style="width:${offen ? '100%' : '0%'}"></i></div>
        ${liste}`;
}

async function elmaAktualisieren() {
    const offen = (_elma?.meldungen || []).filter(m => !m.fertig && !m.zertifizierung);
    if (!offen.length) {
        await elmaLaden();
        if (_elmaAnsicht === 'konfig' || _elmaAnsicht === 'profil') elmaMitteZeichnen();
        return;
    }
    if (_elmaBusy) return;
    _elmaBusy = true;
    _elmaWarte('Aktualisieren', 'OneCrew holt den Stand der offenen Meldungen.');
    try {
        for (const m of offen) {
            const r = await fetch('/api/elm/meldungen/' + encodeURIComponent(m.id) + '/abfragen', { method: 'POST', headers: ah() });
            const j = await r.json().catch(() => ({}));
            if (r.ok && j.stand) _elma = j.stand;
        }
    } finally {
        _elmaWarte(null);
        _elmaBusy = false;
        elmaListeZeichnen();
        if (_elmaAnsicht === 'konfig' || _elmaAnsicht === 'profil') elmaMitteZeichnen();
    }
}

async function elmaDialogHolen(id) {
    _elmaFokus = id;
    await _elmaPost(encodeURIComponent(id) + '/abfragen', {}, 'DialogMessage abholen', 'OneCrew holt die Nachricht des Empfängers.');
}

function elmaQuittung(id) {
    _elmaQuittung = _elmaQuittung === id ? null : id;
    _elmaFokus = id;
    elmaListeZeichnen();
}

function _elmaUebersicht(m, mi) {
    const status = `${m.statusText || 'In Bearbeitung'} (${m.status || 'pending'})`;
    const _ELMA_DOMNAME = {
        'AHV-AVS': 'AHV', 'FAK-CAF': 'FAK', 'UVG-LAA': 'UVG', 'UVGZ-LAAC': 'UVGZ', 'KTG-AMC': 'KTG',
        'BVG-LPP': 'BVG', 'TaxAtSource': 'Quellensteuer', 'Statistic': 'Statistik', 'TaxCrossborder': 'Grenzgänger', 'Tax': 'Lohnausweise',
    };
    const domains = [...new Set((m.empfaenger || []).map(e => _ELMA_DOMNAME[e.domain] || e.domain).filter(Boolean))];
    const empfaenger = (m.empfaenger || []).map(e => e.name).filter(Boolean);
    const hinweise = (m.empfaenger || []).flatMap(e => e.hinweise || []);
    const frei = (m.empfaenger || []).findIndex(e => e.freigabeUrl);
    const quittung = (m.empfaenger || []).some(e => (e.nachrichten || []).some(n => (n.art || '').indexOf('Quittung') >= 0));
    const quittungText = _elmaQuittung === m.id
        ? (m.empfaenger || []).flatMap(e => (e.nachrichten || []).filter(n => (n.art || '').indexOf('Quittung') >= 0).map(n => `<pre style="white-space:pre-wrap;font-size:12px;margin:8px 0 0">${_elmaEsc(n.xml || n.art)}</pre>`)).join('')
        : '';
    return `<div class="elma-kv2">
        <span>Status</span><b>${_elmaEsc(status)}</b>
        <span>Übermittlungszeitpunkt</span><b>${_elmaEsc(_elmaDatum(m.gesendet) || '—')}</b>
        <span>DeclarationID</span><b>${_elmaEsc(m.declarationId || 'noch nicht vergeben')}</b>
        <span>Domäne</span><b>${_elmaEsc(domains.join(', ') || m.titel || '—')}</b>
        <span>Empfänger</span><b>${_elmaEsc(_elmaKurzListe(empfaenger) === '—' ? '—' : _elmaKurzListe(empfaenger))}</b>
        <span>Benachrichtigungen</span><b>${hinweise.length ? _elmaEsc(hinweise.length === 1 ? hinweise[0] : hinweise.length + ' Hinweise') : 'keine'}</b>
    </div>
    ${quittungText}
    ${_elmaDetailsInhalt(m, mi)}`;
}

function elmaAnsehen(id) {
    _elmaFokus = _elmaFokus === id ? null : id;
    elmaListeZeichnen();
}

function _elmaKurzListe(arr) {
    if (!arr.length) return '—';
    if (arr.length <= 2) return arr.join(', ');
    return `${arr[0]} und ${arr.length - 1} weitere`;
}

function _elmaFokusMeldung() {
    const liste = (_elma?.meldungen || []).filter(m => !m.zertifizierung && _elmaPasstArt(m));
    if (_elmaFokus) {
        const m = liste.find(x => x.id === _elmaFokus);
        if (m) return m;
    }
    return liste.find(m => !m.fertig && (m.stufe === 'laeuft' || _elmaBrauchtNutzer(m))) || null;
}

function _elmaKommSatz(schritt) {
    if (schritt === 'Gesendet') return 'Unterschrieben, verschlüsselt und an Swissdec geschickt. Der Auftrag ist angekommen.';
    if (schritt === 'Stand abgefragt') return 'Stand abgefragt. Swissdec sagt, wo der Auftrag steht.';
    if (schritt === 'Nachrichten abgeholt') return 'Quittung und Nachrichten vom Empfänger geholt.';
    if (schritt === 'Angemeldet') return 'Anmeldung an Swissdec geschickt.';
    return schritt || '';
}

function _elmaUhr(iso) {
    if (!iso) return '';
    const d = new Date(iso);
    return isNaN(d) ? '' : d.toLocaleTimeString('de-CH', { hour12: false });
}

function _elmaText(id, text) {
    const el = document.getElementById(id);
    if (el) el.textContent = text;
}

function _elmaSchritteStand(m) {
    const namen = ['Meldung bauen', 'TLS-Handshake', 'Signieren', 'Verschlüsseln', 'Senden', 'Quittung holen'];
    let fertig = -1;
    let jetzt = 0;
    if (_elmaFlug) { fertig = 3; jetzt = 4; }
    else if (m && (((m.verlauf || []).some(p => p.schritt === 'Nachrichten abgeholt')) || (m.fertig && m.stufe !== 'rot'))) { fertig = 5; jetzt = -1; }
    else if (m && ((m.verlauf || []).some(p => p.schritt === 'Gesendet') || m.stufe === 'laeuft')) { fertig = 4; jetzt = 5; }
    else if (m && m.stufe === 'rot') { fertig = 3; jetzt = 4; }
    return namen.map((name, i) => {
        const klasse = i <= fertig ? 'fertig' : (i === jetzt ? 'jetzt' : '');
        const marke = i <= fertig ? '✓' : (i === jetzt ? '●' : '○');
        return `<span class="elma-schritt ${klasse}"><i>${marke}</i>${name}</span>`;
    }).join('');
}

function _elmaPhaseVon(m) {
    if (_elmaFlug)
        return { schritte: _ELMA_SEND, index: 1, laufend: true, titel: 'Leitung, Unterschrift, Verschlüsselung', kern: 'Sendet', phase: 'Sendet', zurueck: false };
    if (!m)
        return { schritte: _ELMA_SEND, index: 0, laufend: false, titel: 'Bereit für die Übermittlung', kern: 'Bereit', phase: 'Bereit', zurueck: false };
    const hat = s => (m.verlauf || []).some(p => p.schritt === s);
    if (m.stufe === 'rot')
        return { schritte: _ELMA_QUERY, index: 4, laufend: false, titel: 'Nicht angenommen', kern: 'Stopp', phase: 'Abgelehnt', zurueck: true };
    if (_elmaBrauchtNutzer(m))
        return { schritte: _ELMA_QUERY, index: 4, laufend: true, titel: 'Empfänger wartet auf eine Ergänzung', kern: 'Wartet', phase: 'Ergänzen', zurueck: true };
    if (m.fertig)
        return { schritte: _ELMA_QUERY, index: 6, laufend: false, titel: 'Auftrag abgeschlossen', kern: 'Fertig', phase: m.stufe === 'ok' ? 'Erledigt' : 'Prüfen', zurueck: true };
    if (hat('Nachrichten abgeholt'))
        return { schritte: _ELMA_QUERY, index: 5, laufend: m.stufe === 'laeuft', titel: 'Quittung ist da', kern: 'Prüft', phase: 'Quittung', zurueck: true };
    if (hat('Gesendet') || hat('Stand abgefragt') || m.stufe === 'laeuft')
        return { schritte: _ELMA_QUERY, index: 3, laufend: true, titel: 'Antwort kommt an', kern: 'Empfängt', phase: 'Unterwegs', zurueck: true };
    return { schritte: _ELMA_SEND, index: 0, laufend: false, titel: 'Bereit für die Übermittlung', kern: 'Bereit', phase: 'Bereit', zurueck: false };
}

function elmaKommZeichnen() {
    const m = _elmaFlug ? null : _elmaFokusMeldung();
    const phase = _elmaPhaseVon(_elmaFlug ? null : m);
    const schritte = document.getElementById('elmaSchritte');
    if (schritte) schritte.innerHTML = _elmaSchritteStand(_elmaFlug ? null : m);
    const bar = document.getElementById('elmaBar');
    if (bar) bar.style.width = (phase.index / phase.schritte.length * 100) + '%';
    _elmaText('elmaProgTitel', phase.titel);
    _elmaText('elmaProgText', `${phase.index} / ${phase.schritte.length}`);
    _elmaText('elmaCore', phase.kern);
    _elmaText('elmaPhase', phase.phase);
    const stage = document.getElementById('elmaStage');
    if (stage) {
        stage.classList.toggle('paused', !phase.laufend);
        stage.classList.toggle('elma-zurueck', !!phase.zurueck);
    }

    const sua = _elmaCert?.sua;
    const online = _elmaVerb?.letzte?.erreichbar?.stufe === 'ok';
    const signiert = !!(m && (m.verlauf || []).some(p => p.schritt === 'Gesendet'));
    _elmaText('elmaTeleLeitung', _elmaFlug ? 'Offen' : online ? 'Erreichbar' : (_elmaVerb ? 'Nicht erreichbar' : '—'));
    _elmaText('elmaTeleZert', !sua ? '—' : !sua.vorhanden ? 'Keines' : _elmaZertAbgelaufen(sua) ? 'Abgelaufen' : 'Gültig');
    _elmaText('elmaTeleSign', _elmaFlug ? 'Gerade' : signiert ? 'Gesetzt' : 'Ausstehend');
    _elmaText('elmaTeleCrypt', _elmaFlug ? 'Gerade' : signiert ? 'Verschlüsselt' : 'Ausstehend');
    _elmaText('elmaTeleStand', phase.phase);
    const namen = (m?.empfaenger || []).map(e => e.name).filter(Boolean);
    _elmaText('elmaTeleEmpf', namen.length ? namen.join(', ') : '—');
    const letzt = (m?.verlauf || [])[0];
    _elmaText('elmaTeleLetzt', letzt ? `${letzt.schritt}${letzt.zeit ? ' · ' + _elmaUhr(letzt.zeit) : ''}` : '—');

    const log = document.getElementById('elmaKomm');
    if (!log) return;
    const text = _elmaFlug ? 'Die Meldung geht an Swissdec.' : (letzt ? _elmaKommSatz(letzt.schritt) : phase.titel);
    const zeit = letzt && !_elmaFlug ? _elmaUhr(letzt.zeit) : '';
    log.innerHTML = `<div class="elma-logline"><time>${_elmaEsc(zeit || '—')}</time><span>${_elmaEsc(text)}</span></div>`;
}

let _elmaLageKey = '';

function _elmaObenMeldung() {
    return (_elma?.meldungen || []).find(m => !m.zertifizierung && !m.fertig && _elmaBrauchtNutzer(m)) || null;
}

function _elmaBrauchtNutzer(m) {
    return (m.empfaenger || []).some(e =>
        (e.nachrichten || []).some(n => n.dialog && n.dialog.beantwortbar && !n.beantwortet) || e.freigabeUrl);
}

function _elmaKarten(innen) {
    return `<div id="elmaLage" style="margin-top:16px;padding:18px 20px;border-radius:16px;background:rgba(255,255,255,0.55);
        border:1px solid rgba(255,255,255,0.75);box-shadow:0 10px 28px rgba(60,55,48,0.12)">${innen}</div>`;
}

function elmaLageZeichnen() {
    if (_elmaBusy) return;
    const el = document.getElementById('elmaStatus');
    if (!el) return;
    const quelle = _elma?.meldungen || [];
    const liste = quelle.filter(m => !m.zertifizierung && _elmaPasstArt(m));
    const aktion = liste.filter(m => !m.fertig && _elmaBrauchtNutzer(m));
    const unterwegs = liste.filter(m => !m.fertig && m.stufe === 'laeuft');
    if (aktion.length) {
        const m = aktion[0];
        const mi = quelle.indexOf(m);
        const key = 'aktion:' + m.id;
        if (key === _elmaLageKey && document.getElementById('elmaLage')) return;
        _elmaLageKey = key;
        const weitere = aktion.length > 1 ? `<div style="font-size:13px;color:#646464;margin-top:8px">Weitere Meldungen warten ebenfalls auf Sie. Sie stehen im Verlauf.</div>` : '';
        const parallel = unterwegs.length
            ? `<div style="display:flex;gap:10px;align-items:center;margin-top:12px;font-size:13px;color:#646464"><span class="elma-sanduhr" style="font-size:18px" aria-hidden="true">⏳</span> OneCrew fragt eine andere Meldung noch bei Swissdec ab.</div>`
            : '';
        el.innerHTML = _elmaKarten(`<div style="font-size:16px;font-weight:700;color:#3f3f3f">Bitte ergänzen</div>
            <div style="font-size:13px;color:#646464;margin-top:4px">${_elmaEsc(m.titel)} — der Empfänger braucht noch Angaben von Ihnen.</div>
            <div id="elmaLageHinweis"></div>
            ${_elmaDetailsInhalt(m, mi)}${weitere}${parallel}`);
        return;
    }
    if (unterwegs.length) {
        _elmaLageKey = '';
        el.innerHTML = '';
        return;
    }
    _elmaLageKey = '';
    el.innerHTML = '';
}

function _elmaMensch(text, ok) {
    const t = text || '';
    if (/andere Übermittlung|Mindestabstand|frühestens in \d+/i.test(t))
        return { ok: true, text: 'OneCrew spricht gerade mit Swissdec. Das dauert einen Moment.' };
    if (/JobKey|soap:|XML|Fault|Schema/i.test(t))
        return { ok: false, text: 'Das hat nicht geklappt. Bitte später noch einmal versuchen.' };
    return { ok, text: t };
}

function _elmaKurzStatus(m) {
    const ergaenzen = (m.empfaenger || []).some(e =>
        (e.nachrichten || []).some(n => n.dialog && n.dialog.beantwortbar && !n.beantwortet) || e.freigabeUrl);
    if (ergaenzen) return _elmaPille('warn', 'Bitte ergänzen');
    if (m.stufe === 'laeuft') return _elmaPille('laeuft', 'Unterwegs');
    if (m.stufe === 'ok') return _elmaPille('ok', 'Erledigt');
    if (m.stufe === 'rot') return _elmaPille('rot', 'Nicht angenommen');
    if (m.stufe === 'warn') return _elmaPille('warn', 'Bitte prüfen');
    return _elmaPille('grau', 'Offen');
}

function elmaArchiv() {
    _elmaArchivOffen = !_elmaArchivOffen;
    elmaListeZeichnen();
}

function elmaDetails(id) {
    const m = (_elma?.meldungen || []).find(x => x.id === id);
    if (m?.fertig) _elmaArchivOffen = true;
    if (_elmaOffen.has(id)) _elmaOffen.delete(id);
    else _elmaOffen.add(id);
    elmaListeZeichnen();
}

function _elmaZeile(m, mi) {
    const offen = _elmaOffen.has(m.id);
    const f = _ELMA_STUFE[m.stufe] || _ELMA_STUFE.grau;
    const id = _elmaEsc(m.id);
    const rand = _elmaFokus === m.id ? '2px solid #3f3f3f' : '1px solid rgba(60,55,48,0.14)';
    return `<div style="margin-bottom:8px;border-radius:12px;border:${rand};background:rgba(255,255,255,0.55)">
        <div style="padding:10px 14px;display:flex;align-items:center;gap:10px">
            <span style="width:10px;height:10px;border-radius:50%;background:${f.punkt};flex:0 0 auto"></span>
            <button type="button" onclick="elmaAnsehen('${id}')" style="background:none;border:none;padding:0;cursor:pointer;font:inherit;font-weight:700;color:#3f3f3f;font-size:14px;white-space:nowrap;text-align:left">${_elmaEsc(m.titel)}</button>
            ${m.ersatz ? '<span style="font-size:11px;color:#646464;white-space:nowrap">Ersatz</span>' : ''}
            <span style="font-size:12px;color:#8b8b8b;white-space:nowrap">${_elmaDatum(m.gesendet)}</span>
            <span style="margin-left:auto;white-space:nowrap">${_elmaKurzStatus(m)}</span>
            <button type="button" class="btn btn-secondary" style="white-space:nowrap" onclick="elmaDetails('${id}')">${offen ? 'Schliessen' : 'Details'}</button>
        </div>
        ${offen ? `<div style="padding:0 14px 14px">
            ${_elmaObenMeldung()?.id === m.id
                ? '<div style="font-size:13px;color:#646464;padding-top:4px">Die Angaben stehen oben.</div>'
                : _elmaDetailsInhalt(m, mi)}
            <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:8px">
                ${m.ersetzbar ? `<button type="button" class="btn btn-secondary" onclick="elmaErsetzen('${id}')">Ersatzmeldung</button>` : ''}
            </div>
        </div>` : ''}
    </div>`;
}

function _elmaDetailsInhalt(m, mi) {
    const teile = (m.empfaenger || []).map((e, ei) => _elmaEmpfaenger(m, mi, e, ei)).filter(Boolean);
    const abgelehnt = (m.empfaenger || []).filter(e => e.stufe === 'rot').length;
    const zeile = abgelehnt ? `<div style="font-size:13px;color:#991b1b;margin-top:8px">${abgelehnt} Empfänger nicht angenommen.</div>` : '';
    if (!teile.length) return zeile;
    return teile.join('') + zeile;
}

function _elmaEmpfaenger(m, mi, e, ei) {
    const runden = [];
    (e.nachrichten || []).forEach((n, ni) => {
        if (!n.dialog || !n.dialog.beantwortbar || n.beantwortet) return;
        const qs = (n.dialog.absaetze || []).filter(p => p.istFrage);
        if (qs.length) runden.push({ ni, qs });
    });
    if (!runden.length && !e.freigabeUrl) return '';
    const felder = runden.map(({ ni, qs }) => qs.map(p => {
        const id = `elmaDlg-${mi}-${ei}-${ni}-${p.id}`;
        return `<label style="display:flex;flex-direction:column;align-items:flex-start;gap:4px;margin-top:12px">
            <span style="font-size:13px;color:#3f3f3f">${_elmaEsc(_elmaFrageTitel(p))}${p.optional ? '' : ' *'}</span>
            ${_elmaFeld(id, p, false)}
        </label>`;
    }).join('') + `<div style="display:flex;justify-content:flex-end;margin-top:14px">
        <button type="button" class="btn btn-primary" onclick="elmaAntworten(${mi}, ${ei}, ${ni})">Antwort senden</button></div>`).join('');
    const freigabe = e.freigabeUrl
        ? `<div style="margin-top:10px"><button type="button" class="btn btn-primary" onclick="elmaFreigeben(${mi}, ${ei})">Beim Empfänger freigeben</button></div>`
        : '';
    return `<div style="padding:10px 0;border-top:1px solid rgba(60,55,48,0.10)">
        <div style="font-size:13px;color:#3f3f3f"><b>${_elmaEsc(e.name)}</b></div>
        ${runden.length ? '<div style="font-size:13px;color:#646464;margin-top:4px">Bitte die Felder ausfüllen.</div>' : ''}
        ${felder}${freigabe}
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
        const hin = document.getElementById('elmaLageHinweis');
        if (hin) hin.innerHTML = _elmaKasten(false, 'Bitte noch ausfüllen: ' + fehlt.map(_elmaEsc).join(', ') + '.');
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
        const m = (_elma?.meldungen || []).find(x => !x.fertig && !x.zertifizierung && x.stufe === 'laeuft');
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
