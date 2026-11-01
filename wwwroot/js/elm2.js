// ELM 2 (Walter 01.11.2026): Übermittlungscockpit nach den Swissdec-Testfällen ELMv6.
// Ein Cockpit für Jahr und Monat (Aktualisieren, Testmeldung, Mitteilungsübersicht und
// Mitteilungshistorie sind darum identisch), dazu Aufgaben und Konfiguration (SUA, Anmeldung).
// Dieselben Schnittstellen wie die übrigen ELM-Seiten (api/elm/meldungen, api/elm/zertifikat).
// Die Technik (GetStatus, GetResult, Synchronisieren) bleibt hinter «Aktualisieren».
// Was der Server noch nicht senden kann, steht sichtbar als «noch nicht verfügbar».

// Begriffe an EINER Stelle. Sobald Anhang x (übersetzte Begriffe) da ist, nur hier ersetzen.
const E2_B = {
    aktualisieren: 'Aktualisieren',
    testmeldung: 'Testmeldung',
    portal: 'Freigabe im Portal gewünscht',
    uebermitteln: 'Übermitteln',
    pruefen: 'Prüfen',
    uebersicht: 'Mitteilungsübersicht',
    historie: 'Mitteilungshistorie',
    ersatz: 'Ersatzmeldung',
    status: 'Status',
    zeitpunkt: 'Übermittlungszeitpunkt',
    declarationId: 'DeclarationID',
    domaene: 'Domäne',
    empfaenger: 'Empfänger',
    benachrichtigungen: 'Benachrichtigungen',
    aktionen: 'Aktionen',
    freigabeStarten: 'Freigabe starten',
    dialogAbholen: 'DialogMessage abholen',
    quittungAnzeigen: 'Quittung anzeigen',
    success: 'success',
    pending: 'pending',
    error: 'error',
};
const E2_STATUS_TEXT = { success: 'Erfolgreich', pending: 'In Bearbeitung', error: 'Fehler' };
const E2_TIPP_PORTAL = 'In den Übermittlungsprozessen der Versicherungen wird üblicherweise ein Link auf das jeweilige Portal der Versicherung zurückgegeben (Completion). Je nach Versicherung wird die Übermittlung auch ohne Absprung auf ein Portal verarbeitet. Falls ein Link ins Portal ausdrücklich gewünscht wird, kann dies mit dieser Option bei der Übermittlung mitgegeben werden.';
const E2_TIPP_TEST = 'Als Testmeldung gekennzeichnete Übermittlungen werden von den Empfängern nicht verarbeitet. So lässt sich die Einrichtung jederzeit prüfen, zum Beispiel vor der Lohndeklaration im Januar.';
const E2_SUA_TEXT = [
    'Die Möglichkeiten von Swissdec Standards umfassen Prozesse, die direkt aus Ihrer ERP-Lösung gestartet werden, und Prozesse, die von Versicherungen oder Behörden ausgelöst werden. Für diese von aussen ausgelösten Prozesse muss Ihr Unternehmen eindeutig identifiziert werden können.',
    'Dafür hat Swissdec ein Unternehmenszertifikat geschaffen (SUA, Swissdec Unternehmens-Authentifizierung). Sie bestellen es direkt in OneCrew. Es braucht nur ein Zertifikat, und es gilt für sämtliche Swissdec Standards.',
    'Sobald das Zertifikat hinterlegt ist, melden Sie Ihr Unternehmen in der Konfiguration bei den gewünschten Versicherungen und Behörden für die digitale Kommunikation an.',
];
const E2_MONATE = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
const E2_DOM = {
    'AHV-AVS': 'AHV/ALV', 'FAK-CAF': 'FAK', 'UVG-LAA': 'UVG', 'UVGZ-LAAC': 'UVGZ', 'KTG-AMC': 'KTG',
    'BVG-LPP': 'BVG', 'TaxAtSource': 'Quellensteuer', 'Statistic': 'Statistik',
    'TaxCrossborder': 'Grenzgänger', 'Tax': 'Lohnausweise',
};
// Arten, die der Server heute als Datei bauen und senden kann.
const E2_SENDBAR = new Set(['annual', 'grenz-sfn', 'grenz-fr', 'grenz-it']);
const E2_VERSICHERUNG = new Set(['UVG-LAA', 'UVGZ-LAAC', 'KTG-AMC', 'BVG-LPP']);
const E2_TYP = {
    Boolean: 'ja oder nein', YesNoUnknown: 'ja, nein oder unbekannt',
    Date: 'TT.MM.JJJJ', DateTime: 'TT.MM.JJJJ HH:MM', Amount: 'Betrag',
    Integer: 'ganze Zahl', Double: 'Zahl', String: 'Text',
};

const e2 = {
    gebunden: false,
    reiter: 'monat',         // Monatsmeldung zuerst: sie kommt jeden Monat, die Jahresmeldung einmal
    filiale: 0,
    jahr: 0,
    monat: 1,
    test: true,
    portal: false,
    stand: null,
    cert: null,
    domaenen: null,
    uebersicht: null,        // Lohnperioden je Filiale für e2.jahr (api/elm/meldungen/uebersicht)
    monatVonHand: false,     // Monat selbst gewählt; sonst springt er auf den letzten abgeschlossenen
    wahlJahr: new Set(),
    wahlMonat: new Set(['qst', 'statistik']),
    pruefung: null,          // { art: [ {art, v} ], ok, kantone:Set }
    ersatz: null,            // { id, art, declarationId, gesendet }
    auswahl: { jahr: null, monat: null },   // in der Historie gewählte Übermittlung
    historieOffen: false,
    detail: null,            // { id, art: 'quittung'|'dialog' }
    bvg: null,
    bvgOffen: false,
    suaOffen: false,
    suaVorschau: false,
    hinweis: null,           // { ok, text }
    konfigHinweis: null,
    letzteAktualisierung: null,
    verbindung: null,        // Text, solange gerade eine Verbindung zu Swissdec läuft
    nachlauf: null,          // { bis, timer, naechste, schritt }: nach dem Übermitteln fragt die offene Seite selbst nach
    busy: false,
};
// Abstände (Sekunden) der automatischen Nachfrage nach dem Übermitteln, danach jede Minute bis E2_NACHLAUF_MIN.
const E2_NACHLAUF_TAKT = [5, 10, 20, 30, 45, 60];
const E2_NACHLAUF_MIN = 15;

function e2Esc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function e2Datum(iso, mitZeit = true) {
    if (!iso) return '';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return '';
    const p = n => String(n).padStart(2, '0');
    const t = `${p(d.getDate())}.${p(d.getMonth() + 1)}.${d.getFullYear()}`;
    return mitZeit ? `${t} ${p(d.getHours())}:${p(d.getMinutes())}` : t;
}
async function e2Get(url) {
    try {
        const r = await fetch(url, { headers: ah(), cache: 'no-store' });
        if (!r.ok) return null;
        return await r.json();
    } catch { return null; }
}
async function e2Post(url, body) {
    try {
        const r = await fetch(url, { method: 'POST', headers: ah(), body: JSON.stringify(body ?? {}) });
        const j = await r.json().catch(() => ({}));
        return { ok: r.ok, j };
    } catch { return { ok: false, j: { message: 'Server nicht erreichbar.' } }; }
}

// ── Filiale ─────────────────────────────────────────────────────────────────

function e2FilialeId() { return e2.filiale > 0 ? e2.filiale : null; }
function e2FilialeName(id = e2.filiale) {
    const b = (typeof allBranches !== 'undefined' && Array.isArray(allBranches))
        ? allBranches.find(x => Number(x.id) === Number(id)) : null;
    if (!b) return '';
    if (typeof filialKurzname === 'function') { const k = filialKurzname(b); if (k) return k; }
    return b.branchName || b.companyName || '';
}
function e2SelektorFiliale() {
    return (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? Number(fixedCompanyProfileId) : 0;
}

// ── Daten ───────────────────────────────────────────────────────────────────

function e2Meldungen() {
    return (e2.stand?.meldungen || [])
        .filter(m => !m.zertifizierung)
        .filter(m => !e2.filiale || m.companyProfileId === e2.filiale)
        .sort((a, b) => new Date(b.gesendet || 0) - new Date(a.gesendet || 0));
}
function e2MeldungenDesReiters(reiter = e2.reiter) {
    return e2Meldungen().filter(m => reiter === 'monat' ? m.art === 'monthly' : m.art !== 'monthly');
}
function e2Zeilen() {
    const out = [];
    for (const g of e2.domaenen?.gruppen || []) for (const z of g.zeilen || []) out.push(Object.assign({ gruppeId: g.id, gruppe: g.name }, z));
    return out;
}
function e2Zugeordnet(z) { return !!(z.an && z.an !== 'noch keiner zugeordnet'); }
function e2Sendbar(z) { return !!(z.art && E2_SENDBAR.has(z.art) && e2Zugeordnet(z)); }
function e2SuaGueltig() {
    const s = e2.cert?.sua;
    return !!(s?.vorhanden && !(s.restTage != null && s.restTage < 0));
}
function e2EmpfStatus(e) {
    if (e.stufe === 'ok') return 'success';
    if (e.stufe === 'rot') return 'error';
    return 'pending';
}

function e2Init() {
    const app = document.getElementById('e2App');
    if (!app) return;
    if (!e2.gebunden) {
        e2.gebunden = true;
        app.addEventListener('click', e2Klick);
        app.addEventListener('change', e2Change);
        const h = new Date();
        e2.jahr = h.getMonth() === 0 ? h.getFullYear() - 1 : h.getFullYear();
        e2.monat = h.getMonth() === 0 ? 12 : h.getMonth();
    }
    e2.filiale = e2SelektorFiliale();
    e2Laden();
}
function e2FilialeWechsel() {
    e2.filiale = e2SelektorFiliale();
    e2.monatVonHand = false;
    e2.wahlJahr = new Set();
    e2.pruefung = null;
    e2.ersatz = null;
    e2.auswahl = { jahr: null, monat: null };
    e2.domaenen = null;
    e2.bvg = null; e2.bvgOffen = false;
    e2.hinweis = null;
    e2Laden();
}
async function e2Laden() {
    e2Zeichnen();
    const f = e2FilialeId();
    const [stand, cert, dom, ueb] = await Promise.all([
        e2Get('/api/elm/meldungen'),
        e2Get('/api/elm/zertifikat'),
        f ? e2Get(`/api/elm/meldungen/domaenen?jahr=${e2.jahr}&companyProfileId=${f}`) : Promise.resolve(null),
        e2Get('/api/elm/meldungen/uebersicht?jahr=' + e2.jahr),
    ]);
    if (stand) e2.stand = stand;
    if (cert) e2.cert = cert;
    e2.domaenen = f ? dom : null;
    e2.uebersicht = ueb;
    const letzter = e2FilialeUebersicht()?.letzterMonat;
    if (!e2.monatVonHand && letzter) e2.monat = letzter;
    if (!e2.nachlauf && e2LaufendeMeldungen().length) e2NachlaufStarten();
    if (f && !e2.wahlJahr.size) for (const z of e2Zeilen()) if (z.art === 'annual' && e2Sendbar(z)) e2.wahlJahr.add(z.id);
    e2Zeichnen();
    e2MonatAutoPruefen();
}

// ── Zeichnen ────────────────────────────────────────────────────────────────

function e2Zeichnen() {
    const app = document.getElementById('e2App');
    if (!app) return;
    let inhalt = '';
    if (e2.reiter === 'jahr' || e2.reiter === 'monat') inhalt = e2CockpitHtml();
    else if (e2.reiter === 'aufgaben') inhalt = e2AufgabenHtml();
    else inhalt = e2KonfigHtml();
    app.innerHTML = `${e2KopfHtml()}${e2SuaHtml()}${e2ReiterHtml()}<div class="e2-inhalt">${inhalt}</div>`;
}

function e2KopfHtml() {
    const ziel = e2.stand?.ziel;
    const gesperrt = e2.stand?.sendenGesperrt;
    const zeit = e2.letzteAktualisierung ? 'Zuletzt aktualisiert ' + e2Datum(e2.letzteAktualisierung) : '';
    return `<header class="e2-kopf">
        <div>
            <div class="e2-eyebrow">Swissdec · Lohnstandard-CH (ELM)</div>
            <h1>ELM 2</h1>
            <p class="e2-unter">${e2FilialeId() ? e2Esc(e2FilialeName()) : 'Alle Filialen'}${ziel ? ' · Ziel: ' + e2Esc(ziel.name) : ''}</p>
        </div>
    </header>
    <div class="e2-leiste">
        <button type="button" class="e2-pille e2-primaer" data-e2="aktualisieren" ${e2.busy ? 'disabled' : ''}>${e2.busy ? 'Läuft …' : '↻ ' + E2_B.aktualisieren}</button>
        <label class="e2-schalter" title="${e2Esc(E2_TIPP_TEST)}">
            <input type="checkbox" data-e2="test" ${e2.test ? 'checked' : ''}>
            <span>${E2_B.testmeldung}</span>
        </label>
        <span class="e2-tipp">${e2.test ? 'Empfänger verarbeiten Testmeldungen nicht. Ideal, um die Einrichtung zu prüfen.' : 'Echte Übermittlung: die Empfänger verarbeiten die Daten.'}</span>
        <span class="e2-zeit">${e2.verbindung ? `<span class="e2-dreher"></span>${e2Esc(e2.verbindung)}` : e2Esc(zeit)}</span>
    </div>
    ${gesperrt ? `<p class="e2-hinweis e2-warn">${e2Esc(gesperrt)}</p>` : ''}
    ${e2.hinweis ? `<p class="e2-hinweis ${e2.hinweis.ok ? 'e2-gut' : 'e2-warn'}">${e2Esc(e2.hinweis.text)}</p>` : ''}`;
}

function e2SuaHtml() {
    const da = e2SuaGueltig();
    const s = e2.cert?.sua;
    const stand = da
        ? `Zertifikat gültig bis ${e2Esc(e2Datum(s.gueltigBis, false))}.`
        : 'Noch kein gültiges Zertifikat hinterlegt.';
    return `<section class="e2-sua">
        <div class="e2-sua-kopf">
            <span class="e2-punkt ${da ? 'e2-p-ok' : 'e2-p-warn'}"></span>
            <b>Unternehmenszertifikat (SUA)</b>
            <span class="e2-still">${stand}</span>
            <span class="e2-rechts">
                <button type="button" class="e2-text" data-e2="sua-mehr">${e2.suaOffen ? 'Weniger' : 'Was ist das?'}</button>
                <button type="button" class="e2-pille" data-e2="reiter" data-wert="konfig">Zur Konfiguration</button>
            </span>
        </div>
        ${e2.suaOffen ? `<div class="e2-sua-text">${E2_SUA_TEXT.map(t => `<p>${e2Esc(t)}</p>`).join('')}</div>` : ''}
    </section>`;
}

function e2ReiterHtml() {
    const offen = e2AufgabenListe().length;
    const r = (id, text, zahl) => `<button type="button" class="e2-reiter${e2.reiter === id ? ' an' : ''}" data-e2="reiter" data-wert="${id}">${text}${zahl ? ` <span class="e2-zahl">${zahl}</span>` : ''}</button>`;
    return `<nav class="e2-reiterleiste">
        ${r('monat', 'Monatsmeldung')}${r('jahr', 'Jahresmeldung')}${r('aufgaben', 'Aufgaben', offen)}${r('konfig', 'Konfiguration')}
    </nav>`;
}

// ── Cockpit (Jahr und Monat) ────────────────────────────────────────────────

function e2CockpitHtml() {
    if (!e2FilialeId()) return '<p class="e2-leer">Bitte oben links eine Filiale wählen. Jede Filiale meldet für sich.</p>';
    const jahr = e2.reiter === 'jahr';
    const ersatz = e2.ersatz && (jahr ? e2.ersatz.art !== 'monthly' : e2.ersatz.art === 'monthly')
        ? `<p class="e2-hinweis e2-warn">${E2_B.ersatz} für ${E2_B.declarationId} <b>${e2Esc(e2.ersatz.declarationId || '')}</b> vom ${e2Esc(e2Datum(e2.ersatz.gesendet))}. Die neue Übermittlung ersetzt diese vollständig.
            <button type="button" class="e2-text" data-e2="ersatz-weg">Abbrechen</button></p>` : '';
    const auswahl = jahr ? e2JahrAuswahlHtml() : e2MonatAuswahlHtml();
    const kannPruefen = jahr ? e2ZeilenGewaehlt().length > 0 : e2.wahlMonat.size > 0;
    const kannSenden = !!e2.pruefung?.ok && !e2.busy && !e2.stand?.sendenGesperrt;
    return `<section class="e2-karte">
        <div class="e2-karte-kopf"><h2>${jahr ? 'Jahresmeldung' : 'Monatsmeldung'} ${jahr ? e2.jahr : E2_MONATE[e2.monat - 1] + ' ' + e2.jahr} übermitteln</h2></div>
        ${e2PeriodeHtml(jahr)}
        ${ersatz}
        ${auswahl}
        ${e2PruefungHtml()}
        <div class="e2-aktionen">
            <button type="button" class="e2-pille" data-e2="pruefen" ${kannPruefen && !e2.busy ? '' : 'disabled'}>${E2_B.pruefen}</button>
            <button type="button" class="e2-pille e2-primaer" data-e2="senden" ${kannSenden ? '' : 'disabled'}>${E2_B.uebermitteln}${e2.test ? ' (' + E2_B.testmeldung + ')' : ''}</button>
        </div>
    </section>
    ${e2UebersichtHtml()}
    ${e2HistorieHtml()}`;
}

// Periode: Jahr als Pillen, Monat als Raster mit dem Stand je Monat (Lohnlauf, Übermittlung).
function e2FilialeUebersicht() {
    return (e2.uebersicht?.filialen || []).find(x => x.id === e2.filiale) || null;
}
// Farbe je Monat: grün gefüllt = erfolgreich übermittelt, grün = Lohnlauf abgeschlossen,
// orange = Lohnperiode offen. Monate ohne Lohnperiode und ohne Übermittlung erscheinen nicht.
function e2MonatStand(m) {
    const meldung = e2Meldungen().find(x => x.art === 'monthly' && x.jahr === e2.jahr && x.monat === m);
    const lp = (e2FilialeUebersicht()?.lohnperioden || []).find(x => x.monat === m);
    if (meldung) return { sichtbar: true, klasse: 'e2-m-' + meldung.status, text: meldung.status === 'success' ? 'übermittelt' : (E2_STATUS_TEXT[meldung.status] || '') };
    if (!lp) return { sichtbar: false };
    if (lp.status === 'abgeschlossen') return { sichtbar: true, klasse: 'e2-m-bereit', text: 'abgeschlossen' };
    if (lp.status === 'provisorisch_abgeschlossen') return { sichtbar: true, klasse: 'e2-m-offen', text: 'provisorisch' };
    return { sichtbar: true, klasse: 'e2-m-offen', text: 'Lohnlauf offen' };
}
function e2JahrStand(y) {
    const m = e2Meldungen().find(x => x.art !== 'monthly' && x.jahr === y);
    return m ? { klasse: 'e2-m-' + m.status, text: E2_STATUS_TEXT[m.status] || '' } : null;
}
function e2PeriodeHtml(jahr) {
    const heute = new Date().getFullYear();
    const jahre = [];
    for (let y = Math.min(heute - 2, e2.jahr); y <= Math.max(heute, e2.jahr); y++) jahre.push(y);
    const jahrPillen = jahre.map(y => {
        const st = jahr ? e2JahrStand(y) : null;
        return `<button type="button" class="e2-jahrpille${y === e2.jahr ? ' an' : ''}" data-e2="jahr-wahl" data-wert="${y}">${y}${st ? ` <i class="e2-mpunkt ${st.klasse}" title="${e2Esc(st.text)}"></i>` : ''}</button>`;
    }).join('');
    const kacheln = jahr ? '' : E2_MONATE.map((name, i) => {
        const m = i + 1;
        const st = e2MonatStand(m);
        if (!st.sichtbar) return '';
        return `<button type="button" class="e2-monat${m === e2.monat ? ' an' : ''} ${st.klasse}" data-e2="monat-wahl" data-wert="${m}" title="${e2Esc(name + ' ' + e2.jahr + ': ' + st.text)}">
            <b>${name.slice(0, 3)}</b><span>${e2Esc(st.text)}</span></button>`;
    }).join('');
    const monate = jahr ? '' : (kacheln
        ? `<div class="e2-monate">${kacheln}</div>`
        : `<p class="e2-leer">${e2.uebersicht ? 'In ' + e2.jahr + ' gibt es für diese Filiale keine Lohnperiode.' : 'Lohnperioden werden geladen …'}</p>`);
    return `<div class="e2-periodenwahl"><div class="e2-jahre"><span class="e2-still">${jahr ? 'Lohnjahr' : 'Jahr'}</span>${jahrPillen}</div>${monate}</div>`;
}

function e2ZeilenGewaehlt() { return e2Zeilen().filter(z => e2.wahlJahr.has(z.id) && e2Sendbar(z)); }

function e2JahrAuswahlHtml() {
    if (!e2.domaenen) return '<p class="e2-leer">Empfänger werden geladen …</p>';
    const ahvGewaehlt = e2Zeilen().some(z => z.art === 'annual' && e2.wahlJahr.has(z.id));
    const gruppen = (e2.domaenen.gruppen || []).map(g => {
        const zeilen = (g.zeilen || []).map(z => e2JahrZeileHtml(z, ahvGewaehlt)).join('')
            || '<div class="e2-zeile e2-gedimmt"><span>Dieser Filiale ist kein Empfänger zugeordnet.</span></div>';
        const portal = g.portal
            ? `<label class="e2-schalter e2-klein" title="${e2Esc(E2_TIPP_PORTAL)}"><input type="checkbox" data-e2="portal" ${e2.portal ? 'checked' : ''}><span>${E2_B.portal}</span><i class="e2-info" title="${e2Esc(E2_TIPP_PORTAL)}">i</i></label>` : '';
        const bvg = g.id === 'vorsorge' ? e2BvgHtml() : '';
        return `<div class="e2-gruppe"><div class="e2-gruppe-kopf"><h3>${e2Esc(g.name)}</h3>${portal}</div>${zeilen}${bvg}</div>`;
    }).join('');
    return `<div class="e2-gruppen">${gruppen}</div>`;
}

function e2JahrZeileHtml(z, ahvGewaehlt) {
    const nr = [z.kennung ? 'Nr. ' + z.kennung : '', z.kundennummer ? 'Kunde ' + z.kundennummer : '', z.vertragsnummer ? 'Vertrag ' + z.vertragsnummer : ''].filter(Boolean).join(' · ');
    const an = `<span class="e2-an"><b>${e2Esc(z.name)}</b><i>an ${e2Esc(z.an)}${nr ? ' · ' + e2Esc(nr) : ''}</i></span>`;
    if (!e2Zugeordnet(z))
        return `<div class="e2-zeile e2-gedimmt"><input type="checkbox" disabled>${an}<span class="e2-chip e2-grau">Kein Empfänger zugeordnet</span></div>`;
    if (z.domain === 'FAK-CAF')
        return `<div class="e2-zeile${ahvGewaehlt ? '' : ' e2-gedimmt'}"><input type="checkbox" disabled ${ahvGewaehlt ? 'checked' : ''}>${an}<span class="e2-chip e2-grau">geht mit der AHV-Meldung</span></div>`;
    if (!e2Sendbar(z))
        return `<div class="e2-zeile e2-gedimmt"><input type="checkbox" disabled>${an}<span class="e2-chip e2-grau">noch nicht verfügbar</span></div>`;
    return `<label class="e2-zeile"><input type="checkbox" data-e2="wahl-jahr" data-id="${e2Esc(z.id)}" ${e2.wahlJahr.has(z.id) ? 'checked' : ''}>${an}<span class="e2-chip e2-ok">bereit</span></label>`;
}

function e2BvgHtml() {
    const kopf = `<div class="e2-zeile e2-bvg-kopf"><span class="e2-an"><b>Voraussichtliche BVG-Jahresmeldung</b><i>Lohnsummen über den ganzen Bestand aufbereiten, prüfen und zur Übermittlung freigeben.</i></span>
        <button type="button" class="e2-pille" data-e2="bvg">${e2.bvgOffen ? 'Schliessen' : 'Aufbereitung starten'}</button></div>`;
    if (!e2.bvgOffen) return kopf;
    if (!e2.bvg) return kopf + '<p class="e2-leer">Lohnsummen werden aufbereitet …</p>';
    const zeilen = (e2.bvg.zeilen || []).map(z => `<tr>
        <td>${e2Esc(z.name)}</td>
        <td class="e2-zahlspalte">${Number(z.vorschlag || 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</td>
        <td class="e2-zahlspalte"><input class="e2-betrag" data-bvg="${e2Esc(z.employeeId)}" value="${e2Esc(Number(z.betrag || 0).toFixed(2))}" ${e2.bvg.freigegeben ? 'disabled' : ''}></td>
    </tr>`).join('');
    const summe = (e2.bvg.zeilen || []).reduce((s, z) => s + Number(z.betrag || 0), 0);
    const stand = e2.bvg.freigegeben
        ? `<span class="e2-chip e2-ok">freigegeben ${e2Esc(e2Datum(e2.bvg.freigegebenAm))}</span>`
        : '<span class="e2-chip e2-grau">Entwurf</span>';
    const knoepfe = e2.bvg.freigegeben
        ? '<button type="button" class="e2-pille" data-e2="bvg-frei" data-wert="0">Freigabe zurücknehmen</button>'
        : '<button type="button" class="e2-pille" data-e2="bvg-speichern">Speichern</button><button type="button" class="e2-pille e2-primaer" data-e2="bvg-frei" data-wert="1">Zur Übermittlung freigeben</button>';
    return `${kopf}<div class="e2-bvg">
        <div class="e2-bvg-stand">${stand}<span class="e2-still">Die effektive Übermittlung der BVG-Jahresmeldung ist noch nicht verfügbar.</span></div>
        <div class="e2-tabelle-rahmen"><table class="e2-tabelle"><thead><tr><th>Mitarbeiter</th><th class="e2-zahlspalte">Vorschlag</th><th class="e2-zahlspalte">Lohnsumme</th></tr></thead>
        <tbody>${zeilen || '<tr><td colspan="3">Keine Lohnsummen in diesem Jahr.</td></tr>'}</tbody>
        <tfoot><tr><td>Total</td><td></td><td class="e2-zahlspalte"><b>${summe.toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</b></td></tr></tfoot></table></div>
        <div class="e2-aktionen">${knoepfe}</div>
    </div>`;
}

function e2MonatAuswahlHtml() {
    const p = e2.pruefung;
    const laeuft = !p;   // ohne Ergebnis läuft die automatische Prüfung gleich bzw. schon
    const liste = p?.kantoneListe || [];
    const qstKantone = liste.filter(e => e.domain === 'TaxAtSource');
    const bfs = liste.find(e => e.domain === 'Statistic');
    const lauf = e2MonatStand(e2.monat);
    const chip = (text, art) => `<span class="e2-chip ${art}">${e2Esc(text)}</span>`;
    const pruefChip = laeuft ? chip('wird geprüft …', 'e2-grau') : null;

    // Quellensteuer: Kantone dieser Filiale im gewählten Monat
    let qstChip, qstAus = false, kantonHtml = '';
    if (pruefChip) qstChip = pruefChip;
    else if (!qstKantone.length) { qstChip = chip('keine quellensteuerpflichtigen Personen', 'e2-grau'); qstAus = true; }
    else {
        qstChip = chip(qstKantone.length === 1 ? '1 Kanton' : qstKantone.length + ' Kantone', 'e2-ok');
        kantonHtml = `<div class="e2-kantone">${qstKantone.map(e => `<label class="e2-kanton"><input type="checkbox" data-e2="kanton" data-id="${e2Esc(e.addresseeId)}" ${p.kantone.has(e.addresseeId) ? 'checked' : ''}><span>${e2Esc(e.name)}</span></label>`).join('')}</div>
           ${qstKantone.length > 1 ? '<p class="e2-still">Normalerweise gehen alle Kantone zusammen. Einen Kanton abwählen, wenn nur einzelne übermittelt werden sollen.</p>' : ''}`;
    }
    // Statistik: BFS nur, wenn es in der Meldung dieser Filiale vorkommt
    let statChip, statAus = false;
    if (pruefChip) statChip = pruefChip;
    else if (!bfs) { statChip = chip('nicht in dieser Meldung', 'e2-grau'); statAus = true; }
    else statChip = chip('bereit', 'e2-ok');

    const zeile = (id, titel, unter, chipHtml, aus) => aus
        ? `<div class="e2-zeile e2-gedimmt"><input type="checkbox" disabled><span class="e2-an"><b>${titel}</b><i>${unter}</i></span>${chipHtml}</div>`
        : `<label class="e2-zeile"><input type="checkbox" data-e2="wahl-monat" data-id="${id}" ${e2.wahlMonat.has(id) ? 'checked' : ''}><span class="e2-an"><b>${titel}</b><i>${unter}</i></span>${chipHtml}</label>`;
    const offen = lauf.sichtbar && lauf.klasse === 'e2-m-offen'
        ? `<p class="e2-hinweis e2-warn">Der Lohnlauf ${E2_MONATE[e2.monat - 1]} ${e2.jahr} ist ${e2Esc(lauf.text === 'provisorisch' ? 'erst provisorisch abgeschlossen' : 'noch offen')}. Eine Übermittlung wäre unvollständig. Für eine Testmeldung ist das in Ordnung.</p>` : '';
    return `${offen}<div class="e2-gruppen">
        <div class="e2-gruppe"><div class="e2-gruppe-kopf"><h3>Quellensteuer</h3></div>
            ${zeile('qst', 'Quellensteuer', 'an die Steuerverwaltung jedes Kantons mit quellensteuerpflichtigen Personen', qstChip, qstAus)}
            ${!qstAus && e2.wahlMonat.has('qst') ? kantonHtml : ''}
        </div>
        <div class="e2-gruppe"><div class="e2-gruppe-kopf"><h3>Statistik</h3></div>
            ${zeile('statistik', 'Statistik', 'an das Bundesamt für Statistik', statChip, statAus)}
        </div>
        <div class="e2-gruppe"><div class="e2-gruppe-kopf"><h3>Versicherungen</h3></div>
            <div class="e2-zeile e2-gedimmt"><input type="checkbox" disabled><span class="e2-an"><b>UVG TariTemp</b><i>an die Suva · nur für die Personalverleihbranche</i></span><span class="e2-chip e2-grau">nicht relevant</span></div>
        </div>
    </div>`;
}

function e2PruefungHtml() {
    const p = e2.pruefung;
    if (!p) return '';
    const fehler = p.liste.flatMap(c => [...(c.v.fehler || []), ...(c.v.technisch || [])]);
    const warn = p.liste.flatMap(c => c.v.warnungen || []).filter(w => !String(w).startsWith('FAK Nullmeldung'));
    const empf = [];
    for (const c of p.liste) for (const e of c.v.empfaenger || []) {
        if (c.art === 'monthly' && !p.kantone.has(e.addresseeId)) continue;
        if (!empf.some(x => x.addresseeId === e.addresseeId && x.domain === e.domain)) empf.push(e);
    }
    return `<div class="e2-pruefung ${p.ok ? 'e2-gut' : 'e2-warn'}">
        <b>${p.ok ? 'Geprüft.' : 'Die Prüfung hat etwas gefunden.'}</b>
        ${empf.length ? `<p>${E2_B.uebermitteln} schickt die Daten an: ${empf.map(e => `<span class="e2-chip e2-grau">${e2Esc((E2_DOM[e.domain] || e.domain) + ' · ' + e.name)}</span>`).join(' ')}</p>` : ''}
        ${fehler.length ? `<ul>${fehler.map(f => `<li>${e2Esc(f)}</li>`).join('')}</ul>` : ''}
        ${warn.length ? `<details><summary>${warn.length} Hinweis${warn.length === 1 ? '' : 'e'}</summary><ul>${warn.map(w => `<li>${e2Esc(w)}</li>`).join('')}</ul></details>` : ''}
    </div>`;
}

// ── Mitteilungsübersicht + Historie ─────────────────────────────────────────

function e2AktuelleMeldung() {
    const liste = e2MeldungenDesReiters();
    const id = e2.auswahl[e2.reiter];
    return (id && liste.find(m => m.id === id)) || liste[0] || null;
}

function e2UebersichtHtml() {
    const m = e2AktuelleMeldung();
    const titel = `<h2>${E2_B.uebersicht}</h2>`;
    if (!m) return `<section class="e2-karte">${titel}<p class="e2-leer">Noch keine Übermittlung. Nach dem Klick auf «${E2_B.uebermitteln}» erscheinen die Mitteilungen hier.</p></section>`;
    const ausHistorie = !!e2.auswahl[e2.reiter] && m.id !== e2MeldungenDesReiters()[0]?.id;
    const marken = [
        m.testCase ? `<span class="e2-chip e2-grau">${E2_B.testmeldung}</span>` : '',
        m.ersatz ? `<span class="e2-chip e2-grau">${E2_B.ersatz}</span>` : '',
        m.freigabePortal ? `<span class="e2-chip e2-grau">${E2_B.portal}</span>` : '',
    ].join('');
    const zeilen = (m.empfaenger || []).map(e => {
        const st = e2EmpfStatus(e);
        return `<tr>
            <td><span class="e2-status e2-s-${st}" title="${e2Esc(e.text || '')}">${E2_B[st]}</span><i class="e2-status-text">${e2Esc(E2_STATUS_TEXT[st])}</i></td>
            <td>${e2Esc(e2Datum(m.gesendet))}</td>
            <td class="e2-mono">${e2Esc(e.declarationId || m.declarationId || '—')}</td>
            <td>${e2Esc(E2_DOM[e.domain] || e.domain || '')}</td>
            <td>${e2Esc(e.name || e.addresseeId || '')}</td>
            <td>${e2BenachrichtigungenHtml(e)}</td>
            <td class="e2-aktionsspalte">${e2AktionenHtml(m, e)}</td>
        </tr>`;
    }).join('');
    return `<section class="e2-karte e2-uebersicht" id="e2Uebersicht">
        <div class="e2-karte-kopf">${titel}<span class="e2-rechts">${marken}${m.ersetzbar ? `<button type="button" class="e2-pille" data-e2="ersatz" data-id="${e2Esc(m.id)}">${E2_B.ersatz}</button>` : ''}</span></div>
        <p class="e2-still">${e2Esc(m.titel || '')}${ausHistorie ? ' · aus der Historie gewählt <button type="button" class="e2-text" data-e2="auswahl-weg">zur letzten Übermittlung</button>' : ' · letzte Übermittlung'}${m.letzteAbfrage ? ' · Stand abgefragt ' + e2Esc(e2Datum(m.letzteAbfrage)) : ' · Stand noch nicht abgefragt'}</p>
        ${e2SchritteHtml(m)}
        ${e2NachlaufHtml(m)}
        <div class="e2-tabelle-rahmen"><table class="e2-tabelle">
            <thead><tr><th>${E2_B.status}</th><th>${E2_B.zeitpunkt}</th><th>${E2_B.declarationId}</th><th>${E2_B.domaene}</th><th>${E2_B.empfaenger}</th><th>${E2_B.benachrichtigungen}</th><th>${E2_B.aktionen}</th></tr></thead>
            <tbody>${zeilen || '<tr><td colspan="7">Keine Empfänger in dieser Übermittlung.</td></tr>'}</tbody>
        </table></div>
        ${e2DetailHtml(m)}
        ${e2VerlaufHtml(m)}
    </section>`;
}

// Kommunikationsschritte sichtbar, aber ohne Knöpfe (Swissdec: Technik nur hinter «Aktualisieren»).
// 1 Gesendet → 2 Swissdec verteilt → 3 Empfänger verarbeiten → 4 Quittungen abgeholt
function e2SchritteHtml(m) {
    const empf = (m.empfaenger || []).filter(e => e.stufe !== 'grau');
    const letzte = art => (m.verlauf || []).find(v => v.schritt === art)?.zeit;
    const verteilt = !!m.jobFinished;
    const verarbeitet = verteilt && empf.length > 0 && empf.every(e => e.stufe !== 'laeuft');
    const wartet = empf.some(e => e.stufe === 'warn');
    const fehler = empf.some(e => e.stufe === 'rot');
    const quittiert = !!m.fertig;
    const anzahlFertig = empf.filter(e => e.stufe === 'ok' || e.stufe === 'rot').length;
    const schritte = [
        { titel: 'Gesendet', text: e2Datum(m.gesendet), stand: 'fertig' },
        { titel: 'Swissdec verteilt', text: verteilt ? 'an ' + empf.length + ' Empfänger' : (letzte('Stand abgefragt') ? 'zuletzt gefragt ' + e2Datum(letzte('Stand abgefragt')) : 'wartet auf Swissdec'), stand: verteilt ? 'fertig' : 'jetzt' },
        { titel: 'Empfänger verarbeiten', text: !verteilt ? '' : wartet ? 'wartet auf Sie' : `${anzahlFertig} von ${empf.length} fertig`, stand: !verteilt ? 'offen' : wartet ? 'warten' : verarbeitet ? 'fertig' : 'jetzt' },
        { titel: 'Quittungen abgeholt', text: quittiert ? (letzte('Nachrichten abgeholt') ? e2Datum(letzte('Nachrichten abgeholt')) : '') : '', stand: quittiert ? (fehler ? 'fehler' : 'fertig') : verarbeitet ? 'jetzt' : 'offen' },
    ];
    const zeichen = { fertig: '✓', jetzt: '…', warten: '!', fehler: '!', offen: '' };
    return `<ol class="e2-schritte">${schritte.map((x, i) => `<li class="e2-schritt e2-st-${x.stand}">
        <span class="e2-schritt-punkt">${zeichen[x.stand] || i + 1}</span>
        <span class="e2-schritt-text"><b>${x.titel}</b><i>${e2Esc(x.text)}</i></span></li>`).join('')}</ol>`;
}

function e2VerlaufHtml(m) {
    const v = m.verlauf || [];
    if (!v.length) return '';
    const zeilen = v.slice(0, 50).map(x => `<tr><td class="e2-nowrap">${e2Esc(e2Datum(x.zeit))}</td><td class="e2-nowrap">${e2Esc(x.schritt)}</td><td>${e2Esc(x.text || '')}</td></tr>`).join('');
    return `<details class="e2-verlauf"><summary>Kommunikation mit Swissdec · ${m.verlaufAnzahl || v.length} Schritt${(m.verlaufAnzahl || v.length) === 1 ? '' : 'e'}</summary>
        <p class="e2-still">Jede Zeile ist eine Verbindung zu Swissdec: OneCrew schickt eine signierte und verschlüsselte Nachricht und erhält die Antwort. Die neusten oben.</p>
        <div class="e2-tabelle-rahmen"><table class="e2-tabelle"><thead><tr><th>Zeit</th><th>Schritt</th><th>Antwort</th></tr></thead><tbody>${zeilen}</tbody></table></div>
    </details>`;
}

function e2NachlaufHtml(m) {
    const n = e2.nachlauf;
    if (!n || !e2LaufendeMeldungen().some(x => x.id === m.id)) return '';
    const sek = n.naechste ? Math.max(0, Math.round((n.naechste - Date.now()) / 1000)) : 0;
    return `<p class="e2-hinweis e2-gut e2-nachlauf"><span class="e2-dreher"></span>Swissdec verteilt die Meldung. OneCrew fragt selbst nach${e2.busy ? ' …' : ` · nächste Abfrage in ${sek} s`}. Verlassen Sie die Seite, holt OneCrew den Stand später im Hintergrund ab.</p>`;
}

function e2BenachrichtigungenHtml(e) {
    const teile = [];
    for (const h of e.hinweise || []) teile.push(`<li>${e2Esc(h)}</li>`);
    for (const n of e.nachrichten || []) {
        const offen = n.dialog && n.dialog.beantwortbar && !n.beantwortet;
        teile.push(`<li>${e2Esc(n.art)}${n.empfangen ? ' · ' + e2Esc(e2Datum(n.empfangen)) : ''}${offen ? ' · <b>wartet auf Antwort</b>' : ''}</li>`);
    }
    if (e.freigabeAbgelaufen) teile.push('<li>Freigabe-Link abgelaufen. Erneut übermitteln.</li>');
    if (!teile.length) return `<span class="e2-still">${e2Esc(e.text || '—')}</span>`;
    return `<ul class="e2-notiz">${teile.join('')}</ul>`;
}

function e2AktionenHtml(m, e) {
    const k = [];
    if (e.freigabeUrl)
        k.push(`<button type="button" class="e2-pille e2-klein e2-primaer" data-e2="freigabe" data-url="${e2Esc(e.freigabeUrl)}" title="Gültig bis ${e2Esc(e2Datum(e.freigabeBis))}">${E2_B.freigabeStarten}</button>`);
    if ((e.nachrichten || []).some(n => n.dialog && n.dialog.beantwortbar && !n.beantwortet))
        k.push(`<button type="button" class="e2-pille e2-klein e2-primaer" data-e2="dialog" data-id="${e2Esc(m.id)}" data-emp="${e2Esc(e.addresseeId)}">${E2_B.dialogAbholen}</button>`);
    if ((e.nachrichten || []).some(n => /Quittung|Ergebnis/.test(n.art || '')))
        k.push(`<button type="button" class="e2-pille e2-klein" data-e2="quittung" data-id="${e2Esc(m.id)}" data-emp="${e2Esc(e.addresseeId)}">${E2_B.quittungAnzeigen}</button>`);
    return k.join('') || '<span class="e2-still">—</span>';
}

function e2DetailHtml(m) {
    const d = e2.detail;
    if (!d || d.id !== m.id) return '';
    const e = (m.empfaenger || []).find(x => x.addresseeId === d.emp);
    if (!e) return '';
    if (d.art === 'quittung') {
        const n = (e.nachrichten || []).filter(x => /Quittung|Ergebnis/.test(x.art || ''));
        return `<div class="e2-detail"><div class="e2-karte-kopf"><h3>${E2_B.quittungAnzeigen} · ${e2Esc(e.name)}</h3><button type="button" class="e2-text" data-e2="detail-weg">Schliessen</button></div>
            ${n.map(x => `<p class="e2-still">${e2Esc(x.art)} · ${e2Esc(e2Datum(x.empfangen))}</p><pre class="e2-pre">${e2Esc(x.xml || '')}</pre>`).join('') || '<p class="e2-leer">Keine Quittung.</p>'}</div>`;
    }
    let form = '';
    for (const n of e.nachrichten || []) {
        if (!n.dialog || !n.dialog.beantwortbar || n.beantwortet) continue;
        form += `<div class="e2-frage">`;
        if (n.dialog.titel) form += `<p><b>${e2Esc(n.dialog.titel)}</b></p>`;
        if (n.dialog.beschreibung) form += `<p>${e2Esc(n.dialog.beschreibung)}</p>`;
        for (const p of (n.dialog.absaetze || []).filter(x => !x.istFrage && x.label))
            form += `<p class="e2-still">${e2Esc(p.label)}${p.wert ? ': <b>' + e2Esc(p.wert) + '</b>' : ''}</p>`;
        for (const p of (n.dialog.absaetze || []).filter(x => x.istFrage)) {
            form += `<label class="e2-feld"><span>${e2Esc(p.label || 'Antwort')}${p.optional ? '' : ' *'}</span>
                <input data-frage="${e2Esc(p.id)}" value="${e2Esc(p.default || p.antwort || '')}" placeholder="${e2Esc(E2_TYP[p.frageTyp] || '')}"></label>`;
        }
        form += `<div class="e2-aktionen"><button type="button" class="e2-pille e2-primaer" data-e2="antwort" data-id="${e2Esc(m.id)}" data-emp="${e2Esc(e.addresseeId)}" data-story="${e2Esc(n.storyId)}">Antwort senden</button></div></div>`;
    }
    return `<div class="e2-detail"><div class="e2-karte-kopf"><h3>Rückfrage · ${e2Esc(e.name)}</h3><button type="button" class="e2-text" data-e2="detail-weg">Schliessen</button></div>
        <p class="e2-still">Den Text schreibt der Empfänger, oft auf Englisch. Vorgeschlagene Werte sind schon eingetragen.</p>
        ${form || '<p class="e2-leer">Keine offene Rückfrage. «Aktualisieren» holt sie, falls sie noch unterwegs ist.</p>'}</div>`;
}

function e2HistorieHtml() {
    const liste = e2MeldungenDesReiters();
    const gewaehlt = e2AktuelleMeldung()?.id;
    const zeilen = liste.map(m => `<tr class="e2-klickbar${m.id === gewaehlt ? ' an' : ''}" data-e2="auswahl" data-id="${e2Esc(m.id)}">
        <td><span class="e2-status e2-s-${e2Esc(m.status)}">${E2_B[m.status] || e2Esc(m.status)}</span></td>
        <td>${e2Esc(e2Datum(m.gesendet))}</td>
        <td>${e2Esc(m.titel || '')}${m.testCase ? ` <span class="e2-chip e2-grau">${E2_B.testmeldung}</span>` : ''}${m.ersatz ? ` <span class="e2-chip e2-grau">${E2_B.ersatz}</span>` : ''}</td>
        <td class="e2-mono">${e2Esc(m.declarationId || '—')}</td>
        <td>${(m.empfaenger || []).length}</td>
        <td class="e2-aktionsspalte">${m.ersetzbar ? `<button type="button" class="e2-pille e2-klein" data-e2="ersatz" data-id="${e2Esc(m.id)}">${E2_B.ersatz}</button>` : ''}</td>
    </tr>`).join('');
    return `<section class="e2-karte">
        <div class="e2-karte-kopf"><h2>${E2_B.historie}</h2><button type="button" class="e2-text" data-e2="historie">${e2.historieOffen ? 'Ausblenden' : `Anzeigen (${liste.length})`}</button></div>
        ${e2.historieOffen ? `<p class="e2-still">Eine Zeile wählen: die ${E2_B.uebersicht} oben zeigt dieselben Angaben dazu. Eine ${E2_B.ersatz} geht nur von einer bestehenden Übermittlung aus.</p>
        <div class="e2-tabelle-rahmen"><table class="e2-tabelle"><thead><tr><th>${E2_B.status}</th><th>${E2_B.zeitpunkt}</th><th>Meldung</th><th>${E2_B.declarationId}</th><th>${E2_B.empfaenger}</th><th></th></tr></thead>
        <tbody>${zeilen || '<tr><td colspan="6">Noch keine Übermittlung.</td></tr>'}</tbody></table></div>` : ''}
    </section>`;
}

// ── Aufgaben ────────────────────────────────────────────────────────────────

function e2AufgabenListe() {
    const out = [];
    if (e2.cert && !e2SuaGueltig()) out.push({ titel: 'Unternehmenszertifikat fehlt', text: 'Ohne Zertifikat keine Anmeldung bei Versicherungen.', e2: 'reiter', wert: 'konfig' });
    for (const m of e2Meldungen()) {
        const reiter = m.art === 'monthly' ? 'monat' : 'jahr';
        for (const e of m.empfaenger || []) {
            if (e.freigabeUrl) out.push({ titel: E2_B.freigabeStarten, text: `${e.name} · ${m.titel}`, e2: 'zeigen', id: m.id, reiter });
            if ((e.nachrichten || []).some(n => n.dialog && n.dialog.beantwortbar && !n.beantwortet))
                out.push({ titel: 'Rückfrage beantworten', text: `${e.name} · ${m.titel}`, e2: 'zeigen', id: m.id, reiter });
            else if (e.stufe === 'rot')
                out.push({ titel: 'Nicht angenommen', text: `${e.name} · ${m.titel}`, e2: 'zeigen', id: m.id, reiter });
        }
    }
    return out;
}

function e2AufgabenHtml() {
    const liste = e2AufgabenListe();
    const karten = liste.map(a => `<button type="button" class="e2-aufgabe" data-e2="${a.e2}" data-wert="${e2Esc(a.wert || '')}" data-id="${e2Esc(a.id || '')}" data-reiter="${e2Esc(a.reiter || '')}">
        <b>${e2Esc(a.titel)}</b><span>${e2Esc(a.text || '')}</span></button>`).join('');
    return `<section class="e2-karte">
        <h2>Aufgaben</h2>
        <p class="e2-still">Was bei Übermittlungen auf Sie wartet. «${E2_B.aktualisieren}» holt den neusten Stand; zusätzlich holt OneCrew ihn selbst im Hintergrund.</p>
        <div class="e2-aufgaben">${karten || '<p class="e2-leer">Nichts liegt an.</p>'}</div>
    </section>
    <section class="e2-karte e2-gedimmt">
        <h2>Aufgaben von Versicherungen und Behörden</h2>
        <p class="e2-still">Versicherungsprofile (UVG, UVGZ, KTG) und Beitragsänderungen (BVG) kommen von aussen und erscheinen hier, sobald die Anmeldung steht. <span class="e2-chip e2-grau">noch nicht verfügbar</span></p>
    </section>`;
}

// ── Konfiguration ───────────────────────────────────────────────────────────

function e2KonfigHtml() {
    const s = e2.cert?.sua;
    const v = e2.cert?.vorlage || {};
    const reg = e2.cert?.registrierung || {};
    const da = e2SuaGueltig();
    let sua;
    if (da) {
        sua = `<p><span class="e2-chip e2-ok">gültig</span> bis ${e2Esc(e2Datum(s.gueltigBis, false))}${s.text ? ' · ' + e2Esc(s.text) : ''}</p>`;
    } else if (reg.laufend) {
        sua = `<p><span class="e2-chip e2-grau">bestellt</span> ${e2Esc(reg.text || '')}</p>
            <p class="e2-still">Swissdec schickt ein Passwort per Brief. Damit wird das Zertifikat hinterlegt.</p>
            <label class="e2-feld"><span>Passwort aus dem Brief</span><input id="e2Brief" autocomplete="one-time-code"></label>
            <div class="e2-aktionen"><button type="button" class="e2-pille e2-primaer" data-e2="zert-hinterlegen">Zertifikat hinterlegen</button></div>`;
    } else if (e2.suaVorschau) {
        const feld = (l, w) => `<div class="e2-angabe"><span>${l}</span><b>${e2Esc(w || '—')}</b></div>`;
        sua = `<p>Diese Angaben gehen mit der Bestellung an Swissdec. Bitte prüfen.</p>
            <div class="e2-angaben">${feld('Unternehmen', v.firma)}${feld('UID', v.uid)}${feld('Kontaktperson', v.kontakt)}${feld('PLZ / Ort', [v.plz, v.ort].filter(Boolean).join(' '))}</div>
            <p class="e2-hinweis e2-warn">Swissdec gleicht die Angaben mit den offiziellen Registern (UID-Register) ab. Weichen sie ab, wird die Bestellung zurückgewiesen. Falsche Angaben unter System → Hauptsitze korrigieren.</p>
            <div class="e2-aktionen"><button type="button" class="e2-pille" data-e2="sua-vorschau" data-wert="0">Abbrechen</button><button type="button" class="e2-pille e2-primaer" data-e2="zert-bestellen">Zertifikat bestellen</button></div>`;
    } else {
        sua = `<p><span class="e2-chip e2-grau">fehlt</span> Noch kein Zertifikat hinterlegt.</p>
            <div class="e2-aktionen"><button type="button" class="e2-pille e2-primaer" data-e2="sua-vorschau" data-wert="1">Zertifikat bestellen …</button></div>`;
    }
    return `<section class="e2-karte">
        <h2>Unternehmenszertifikat (SUA)</h2>
        ${E2_SUA_TEXT.map(t => `<p class="e2-still">${e2Esc(t)}</p>`).join('')}
        ${sua}
        ${e2.konfigHinweis ? `<p class="e2-hinweis ${e2.konfigHinweis.ok ? 'e2-gut' : 'e2-warn'}">${e2Esc(e2.konfigHinweis.text)}</p>` : ''}
    </section>
    <section class="e2-karte">
        <h2>Anmeldung bei Versicherungen</h2>
        <p class="e2-still">Mit der Anmeldung erlauben Sie einer Versicherung, Prozesse bei Ihnen auszulösen, zum Beispiel ein Versicherungsprofil (UVG, UVGZ, KTG) oder eine Beitragsänderung (BVG) zu schicken. Die Anmeldung gilt pro Standard und pro Empfänger und lässt sich jederzeit aufheben. Sie ist erst möglich, wenn das Zertifikat hinterlegt ist.</p>
        ${e2AnmeldungenHtml(da)}
        <p class="e2-still">Welche Versicherungen eine Filiale hat, steht in den Stammdaten der Filiale unter «Versicherungen». Diese Liste folgt ihnen.</p>
    </section>`;
}

function e2AnmeldungenHtml(suaDa) {
    if (!e2FilialeId()) return '<p class="e2-leer">Bitte oben links eine Filiale wählen.</p>';
    if (!e2.domaenen) return '<p class="e2-leer">Wird geladen …</p>';
    const zeilen = e2Zeilen().filter(z => E2_VERSICHERUNG.has(z.domain) && e2Zugeordnet(z));
    if (!zeilen.length) return '<p class="e2-leer">Dieser Filiale ist noch keine Versicherung zugeordnet.</p>';
    const tr = zeilen.map(z => {
        const anm = (e2.stand?.anmeldungen || []).find(m => (m.empfaenger || []).some(e => e.domain === z.domain && (e.name || '').includes(z.an)));
        const empf = anm?.empfaenger?.find(e => e.domain === z.domain);
        const nr = [z.kundennummer, z.vertragsnummer].filter(Boolean).join(' · ');
        let stand = '<span class="e2-chip e2-grau">nicht angemeldet</span>';
        let knopf = suaDa
            ? `<button type="button" class="e2-pille e2-klein e2-primaer" data-e2="anmelden" data-domain="${e2Esc(z.domain)}" data-versicherer="${e2Esc(z.an)}" data-kunde="${e2Esc(z.kundennummer || '')}" data-vertrag="${e2Esc(z.vertragsnummer || '')}" data-kennung="${e2Esc(z.kennung || '')}">Anmelden</button>`
            : '<span class="e2-still">Zuerst das Zertifikat</span>';
        if (anm) {
            const st = empf ? e2EmpfStatus(empf) : anm.status;
            stand = `<span class="e2-status e2-s-${e2Esc(st)}">${E2_B[st] || e2Esc(st)}</span> <i class="e2-status-text">angemeldet ${e2Esc(e2Datum(anm.gesendet, false))}</i>`;
            knopf = `<button type="button" class="e2-pille e2-klein" data-e2="abmelden" data-id="${e2Esc(anm.id)}" data-emp="${e2Esc(empf?.addresseeId || '')}">Abmelden</button>`;
            if (z.domain !== 'BVG-LPP') knopf += ' <span class="e2-chip e2-grau" title="Versicherungsprofil bestellen">Profil: noch nicht verfügbar</span>';
        }
        return `<tr><td>${e2Esc(E2_DOM[z.domain] || z.domain)}</td><td>${e2Esc(z.an)}${nr ? `<br><i class="e2-status-text">${e2Esc(nr)}</i>` : ''}</td><td>${stand}</td><td class="e2-aktionsspalte">${knopf}</td></tr>`;
    }).join('');
    return `<div class="e2-tabelle-rahmen"><table class="e2-tabelle"><thead><tr><th>Standard / ${E2_B.domaene}</th><th>${E2_B.empfaenger}</th><th>Stand</th><th>${E2_B.aktionen}</th></tr></thead><tbody>${tr}</tbody></table></div>`;
}

// ── Ereignisse ──────────────────────────────────────────────────────────────

function e2Klick(ev) {
    const el = ev.target.closest('[data-e2]');
    if (!el) return;
    const a = el.dataset.e2;
    if (['test', 'portal', 'wahl-jahr', 'wahl-monat', 'kanton'].includes(a)) return;
    if (e2.busy && a !== 'reiter' && a !== 'historie' && a !== 'sua-mehr') return;
    if (a === 'reiter') {
        const neu = el.dataset.wert;
        if (neu !== e2.reiter && (neu === 'monat' || neu === 'jahr')) e2.pruefung = null;
        e2.reiter = neu; e2.hinweis = null; e2.detail = null; e2Zeichnen(); e2MonatAutoPruefen();
    }
    else if (a === 'aktualisieren') e2Aktualisieren();
    else if (a === 'sua-mehr') { e2.suaOffen = !e2.suaOffen; e2Zeichnen(); }
    else if (a === 'jahr-wahl') e2JahrWahl(parseInt(el.dataset.wert, 10));
    else if (a === 'monat-wahl') e2MonatWahl(parseInt(el.dataset.wert, 10));
    else if (a === 'pruefen') e2Pruefen();
    else if (a === 'senden') e2Senden();
    else if (a === 'historie') { e2.historieOffen = !e2.historieOffen; e2Zeichnen(); }
    else if (a === 'auswahl') { e2.auswahl[e2.reiter] = el.dataset.id; e2.detail = null; e2Zeichnen(); e2ZurUebersicht(); }
    else if (a === 'auswahl-weg') { e2.auswahl[e2.reiter] = null; e2.detail = null; e2Zeichnen(); }
    else if (a === 'zeigen') { e2.reiter = el.dataset.reiter || e2.reiter; e2.auswahl[e2.reiter] = el.dataset.id; e2Zeichnen(); e2ZurUebersicht(); }
    else if (a === 'ersatz') e2Ersatz(el.dataset.id);
    else if (a === 'ersatz-weg') { e2.ersatz = null; e2.pruefung = null; e2Zeichnen(); }
    else if (a === 'freigabe' && el.dataset.url) window.open(el.dataset.url, '_blank', 'noopener');
    else if (a === 'dialog') e2DialogAbholen(el.dataset.id, el.dataset.emp);
    else if (a === 'quittung') { e2.detail = { id: el.dataset.id, emp: el.dataset.emp, art: 'quittung' }; e2Zeichnen(); }
    else if (a === 'detail-weg') { e2.detail = null; e2Zeichnen(); }
    else if (a === 'antwort') e2Antworten(el);
    else if (a === 'bvg') e2BvgUmschalten();
    else if (a === 'bvg-speichern') e2BvgSpeichern(false);
    else if (a === 'bvg-frei') e2BvgSpeichern(el.dataset.wert === '1');
    else if (a === 'sua-vorschau') { e2.suaVorschau = el.dataset.wert === '1'; e2Zeichnen(); }
    else if (a === 'zert-bestellen') e2ZertBestellen();
    else if (a === 'zert-hinterlegen') e2ZertHinterlegen();
    else if (a === 'anmelden') e2Anmelden(el);
    else if (a === 'abmelden') e2Abmelden(el);
}

function e2Change(ev) {
    const el = ev.target.closest('[data-e2]');
    if (!el) return;
    const a = el.dataset.e2;
    if (a === 'test') { e2.test = el.checked; }
    else if (a === 'portal') { e2.portal = el.checked; }
    else if (a === 'wahl-jahr') { if (el.checked) e2.wahlJahr.add(el.dataset.id); else e2.wahlJahr.delete(el.dataset.id); e2.pruefung = null; }
    else if (a === 'wahl-monat') {
        if (el.checked) e2.wahlMonat.add(el.dataset.id); else e2.wahlMonat.delete(el.dataset.id);
        // Kein neuer Abruf nötig: die Empfänger dieser Filiale sind bekannt, nur die Auswahl ändert.
        const p = e2.pruefung;
        if (p) {
            for (const e of p.kantoneListe || []) {
                const zuQst = e.domain !== 'Statistic';
                if ((zuQst && el.dataset.id === 'qst') || (!zuQst && el.dataset.id === 'statistik')) {
                    if (el.checked) p.kantone.add(e.addresseeId); else p.kantone.delete(e.addresseeId);
                }
            }
            p.ok = p.okOhneKantone && e2MonatHatEmpfaenger();
        }
    }
    else if (a === 'kanton' && e2.pruefung) {
        if (el.checked) e2.pruefung.kantone.add(el.dataset.id); else e2.pruefung.kantone.delete(el.dataset.id);
        e2.pruefung.ok = e2.pruefung.okOhneKantone && e2MonatHatEmpfaenger();
    } else return;
    e2Zeichnen();
}

function e2ZurUebersicht() {
    requestAnimationFrame(() => document.getElementById('e2Uebersicht')?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
}

// ── Aktionen ────────────────────────────────────────────────────────────────

async function e2Aktualisieren(still = false) {
    if (e2.busy) return;
    e2.busy = true; if (!still) e2.hinweis = null;
    e2.verbindung = 'Verbindung zu Swissdec: Stand und Quittungen werden abgeholt …'; e2Zeichnen();
    const r = await e2Post('/api/elm/meldungen/aktualisieren', {});
    e2.verbindung = null;
    if (r.j.stand) e2.stand = r.j.stand;
    const cert = await e2Get('/api/elm/zertifikat');
    if (cert) e2.cert = cert;
    e2.letzteAktualisierung = new Date().toISOString();
    if (!still && (!r.ok || r.j.ok === false)) e2.hinweis = { ok: false, text: r.j.meldung || r.j.message || 'Nicht alles konnte aktualisiert werden.' };
    e2.busy = false; e2Zeichnen();
}

// Meldungen, bei denen Swissdec bzw. ein Empfänger noch arbeitet (nicht: wartet auf den Benutzer),
// gesendet in den letzten E2_NACHLAUF_MIN Minuten.
function e2LaufendeMeldungen() {
    const grenze = Date.now() - E2_NACHLAUF_MIN * 60000;
    return e2Meldungen().filter(m => new Date(m.gesendet || 0).getTime() >= grenze
        && (m.empfaenger || []).some(e => e.stufe === 'laeuft'));
}
function e2NachlaufStarten() {
    if (e2.nachlauf?.timer) clearTimeout(e2.nachlauf.timer);
    e2.nachlauf = { bis: Date.now() + E2_NACHLAUF_MIN * 60000, schritt: 0, timer: null, naechste: null };
    e2NachlaufPlanen();
}
function e2NachlaufPlanen() {
    const n = e2.nachlauf;
    if (!n) return;
    const sek = E2_NACHLAUF_TAKT[Math.min(n.schritt, E2_NACHLAUF_TAKT.length - 1)];
    n.naechste = Date.now() + sek * 1000;
    n.timer = setTimeout(e2NachlaufSchritt, sek * 1000);
    e2NachlaufUhr();
}
function e2NachlaufUhr() {
    clearInterval(e2NachlaufUhr.iv);
    e2NachlaufUhr.iv = setInterval(() => {
        const el = document.querySelector('#e2App .e2-nachlauf');
        if (!e2.nachlauf || !el) { clearInterval(e2NachlaufUhr.iv); return; }
        const sek = Math.max(0, Math.round((e2.nachlauf.naechste - Date.now()) / 1000));
        if (!e2.busy) el.innerHTML = el.innerHTML.replace(/nächste Abfrage in \d+ s/, `nächste Abfrage in ${sek} s`);
    }, 1000);
}
function e2NachlaufStopp() {
    if (e2.nachlauf?.timer) clearTimeout(e2.nachlauf.timer);
    clearInterval(e2NachlaufUhr.iv);
    e2.nachlauf = null;
}
async function e2NachlaufSchritt() {
    const n = e2.nachlauf;
    if (!n) return;
    const sichtbar = document.getElementById('page-elm2')?.classList.contains('active');
    if (!sichtbar || Date.now() > n.bis) { e2NachlaufStopp(); e2Zeichnen(); return; }
    // Wer gerade etwas eintippt (Rückfrage, Lohnsummen), wird nicht überzeichnet: diese Runde auslassen.
    const tippt = document.activeElement?.closest?.('#e2App') && document.activeElement.matches('input, textarea');
    if (!e2.busy && !tippt) await e2Aktualisieren(true);
    if (!e2.nachlauf) return;
    if (!e2LaufendeMeldungen().length) { e2NachlaufStopp(); e2Zeichnen(); return; }
    n.schritt++;
    e2NachlaufPlanen();
}

function e2JahrWahl(y) {
    if (!y || y === e2.jahr) return;
    e2.jahr = y;
    e2.monatVonHand = false;
    e2.wahlJahr = new Set(); e2.bvg = null; e2.bvgOffen = false;
    e2.pruefung = null; e2.hinweis = null;
    e2Laden();
}

function e2MonatWahl(m) {
    if (!m || m === e2.monat) return;
    e2.monat = m;
    e2.monatVonHand = true;
    e2.pruefung = null; e2.hinweis = null;
    e2Zeichnen();
    e2MonatAutoPruefen();
}

function e2MonatHatEmpfaenger() {
    const p = e2.pruefung;
    if (!p) return false;
    return (p.kantoneListe || []).some(e => p.kantone.has(e.addresseeId));
}

// Schlüssel der Prüfung: wechselt Filiale, Periode oder Reiter, gilt ein altes Ergebnis nicht mehr.
function e2PruefSchluessel() { return [e2.filiale, e2.jahr, e2.monat, e2.reiter].join('|'); }

// Die Monatsmeldung prüft sich selbst, sobald Filiale oder Monat feststehen: so zeigen
// Quellensteuer (Kantone) und Statistik den Stand dieser Filiale, nicht einen festen Text.
function e2MonatAutoPruefen() {
    if (e2.reiter !== 'monat' || !e2FilialeId() || e2.pruefung) return;
    if (e2.busy) { setTimeout(e2MonatAutoPruefen, 800); return; }
    e2Pruefen(true);
}

async function e2Pruefen(auto = false) {
    if (e2.busy || !e2FilialeId()) return;
    const monat = e2.reiter === 'monat';
    const arts = monat ? ['monthly'] : [...new Set(e2ZeilenGewaehlt().map(z => z.art))];
    if (!arts.length) return;
    const schluessel = e2PruefSchluessel();
    e2.busy = true; e2.pruefung = null; if (!auto) e2.hinweis = null; e2Zeichnen();
    const liste = [];
    let ok = true;
    for (const art of arts) {
        const q = new URLSearchParams({ art, jahr: String(e2.jahr), companyProfileId: String(e2.filiale) });
        if (art === 'monthly') q.set('monat', String(e2.monat));
        let v;
        try {
            const r = await fetch('/api/elm/meldungen/vorschau?' + q, { headers: ah() });
            v = await r.json().catch(() => ({}));
            if (!r.ok) v = { fehler: [v.message || 'Prüfung fehlgeschlagen.'], empfaenger: [] };
        } catch { v = { fehler: ['Server nicht erreichbar.'], empfaenger: [] }; }
        liste.push({ art, v });
        if ((v.fehler || []).length || (v.technisch || []).length || v.sendenGesperrt || !(v.empfaenger || []).length) ok = false;
    }
    if (schluessel !== e2PruefSchluessel()) {
        // Inzwischen andere Filiale oder anderer Monat gewählt: Ergebnis verwerfen, neu prüfen.
        e2.busy = false; e2Zeichnen(); e2MonatAutoPruefen(); return;
    }
    const p = { liste, ok, okOhneKantone: ok, kantone: new Set(), kantoneListe: [] };
    if (monat) {
        p.kantoneListe = liste[0].v.empfaenger || [];
        for (const e of p.kantoneListe) {
            const statistik = e.domain === 'Statistic';
            if (statistik ? e2.wahlMonat.has('statistik') : e2.wahlMonat.has('qst')) p.kantone.add(e.addresseeId);
        }
        e2.pruefung = p;
        p.ok = ok && e2MonatHatEmpfaenger();
    } else e2.pruefung = p;
    e2.busy = false; e2Zeichnen();
}

async function e2Senden() {
    const p = e2.pruefung;
    if (!p?.ok || e2.busy) return;
    const wem = [];
    for (const c of p.liste) for (const e of c.v.empfaenger || []) {
        if (c.art === 'monthly' && !p.kantone.has(e.addresseeId)) continue;
        wem.push(e.name);
    }
    const frage = `${e2.test ? 'Als Testmeldung (wird nicht verarbeitet) an: ' : 'Echte Übermittlung an: '}${[...new Set(wem)].join(', ')}.`;
    if (!(await liquidConfirm(frage, { title: E2_B.uebermitteln, yesLabel: E2_B.uebermitteln, noLabel: 'Abbrechen' }))) return;
    e2.busy = true; e2.hinweis = null; e2Zeichnen();
    let fehler = '', letzte = null;
    for (const c of p.liste) {
        const adressaten = c.art === 'monthly'
            ? Object.fromEntries((c.v.empfaenger || []).map(e => [e.addresseeId, p.kantone.has(e.addresseeId)]))
            : null;
        const versicherung = (c.v.empfaenger || []).some(e => E2_VERSICHERUNG.has(e.domain));
        e2.verbindung = 'Verbindung zu Swissdec: Meldung wird signiert, verschlüsselt und gesendet …'; e2Zeichnen();
        const r = await e2Post('/api/elm/meldungen/senden', {
            art: c.art, jahr: e2.jahr, monat: c.art === 'monthly' ? e2.monat : null,
            ersetztId: e2.ersatz && e2.ersatz.art === c.art ? e2.ersatz.id : null,
            ziel: null, testmeldung: e2.test, freigabePortal: e2.portal && versicherung,
            adressaten, companyProfileId: e2.filiale,
        });
        if (r.j.stand) e2.stand = r.j.stand;
        if (!r.ok || !r.j.ok) { fehler = r.j.meldung || r.j.message || 'Nicht übermittelt.'; break; }
        letzte = r.j.vorgangId || letzte;
    }
    e2.busy = false; e2.verbindung = null; e2.pruefung = null; e2.ersatz = null;
    e2.auswahl[e2.reiter] = null;
    e2.hinweis = fehler ? { ok: false, text: fehler } : { ok: true, text: 'Übermittelt. Die Mitteilungen der Empfänger erscheinen unten; «Aktualisieren» holt neue.' };
    e2Zeichnen();
    if (letzte) { e2ZurUebersicht(); e2NachlaufStarten(); }
}

function e2Ersatz(id) {
    const m = e2Meldungen().find(x => x.id === id);
    if (!m || !m.ersetzbar) return;
    e2.ersatz = { id: m.id, art: m.art, declarationId: m.declarationId, gesendet: m.gesendet };
    e2.reiter = m.art === 'monthly' ? 'monat' : 'jahr';
    if (m.jahr) e2.jahr = m.jahr;
    if (m.monat) { e2.monat = m.monat; e2.monatVonHand = true; }
    if (m.art !== 'monthly') {
        e2.wahlJahr = new Set();
        e2.pruefung = null;
        e2Laden().then(() => {
            e2.wahlJahr = new Set(e2Zeilen().filter(z => z.art === m.art && e2Sendbar(z)).map(z => z.id));
            e2Zeichnen();
            document.getElementById('e2App')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        });
        return;
    }
    e2.pruefung = null;
    e2Laden();
}

async function e2DialogAbholen(id, emp) {
    e2.detail = { id, emp, art: 'dialog' };
    e2.busy = true; e2Zeichnen();
    const r = await e2Post('/api/elm/meldungen/' + encodeURIComponent(id) + '/abfragen', {});
    if (r.j.stand) e2.stand = r.j.stand;
    if (!r.ok) e2.hinweis = { ok: false, text: r.j.message || 'Die Rückfrage konnte nicht geholt werden.' };
    e2.busy = false; e2Zeichnen();
}

async function e2Antworten(el) {
    const box = el.closest('.e2-frage');
    const werte = {};
    box?.querySelectorAll('[data-frage]').forEach(f => { werte[f.dataset.frage] = f.value.trim() || null; });
    e2.busy = true; e2Zeichnen();
    const r = await e2Post('/api/elm/meldungen/' + encodeURIComponent(el.dataset.id) + '/antworten', {
        addresseeId: el.dataset.emp, storyId: el.dataset.story, werte,
    });
    if (r.j.stand) e2.stand = r.j.stand;
    e2.hinweis = { ok: r.ok && r.j.ok !== false, text: r.j.meldung || r.j.message || (r.ok ? 'Antwort gesendet.' : 'Antwort nicht angekommen.') };
    if (r.ok) e2.detail = null;
    e2.busy = false; e2Zeichnen();
}

async function e2BvgUmschalten() {
    e2.bvgOffen = !e2.bvgOffen;
    if (!e2.bvgOffen) { e2Zeichnen(); return; }
    e2.bvg = null; e2Zeichnen();
    e2.bvg = await e2Get('/api/elm/meldungen/bvg-lohnsummen?jahr=' + e2.jahr) || { zeilen: [] };
    e2Zeichnen();
}

async function e2BvgSpeichern(frei) {
    const zeilen = [...document.querySelectorAll('#e2App [data-bvg]')].map(el => ({
        employeeId: parseInt(el.dataset.bvg, 10),
        betrag: Number(String(el.value).replace(/['\s]/g, '').replace(',', '.')) || 0,
    }));
    const behalten = zeilen.length ? zeilen : (e2.bvg?.zeilen || []).map(z => ({ employeeId: z.employeeId, betrag: z.betrag }));
    e2.busy = true; e2Zeichnen();
    const r = await e2Post('/api/elm/meldungen/bvg-lohnsummen', { jahr: e2.jahr, freigegeben: frei, zeilen: behalten });
    e2.hinweis = { ok: r.ok, text: r.j.message || (r.ok ? (frei ? 'Lohnsummen zur Übermittlung freigegeben.' : 'Lohnsummen gespeichert.') : 'Nicht gespeichert.') };
    e2.bvg = await e2Get('/api/elm/meldungen/bvg-lohnsummen?jahr=' + e2.jahr) || e2.bvg;
    e2.busy = false; e2Zeichnen();
}

async function e2ZertBestellen() {
    const v = e2.cert?.vorlage || {};
    e2.busy = true; e2Zeichnen();
    const r = await e2Post('/api/elm/zertifikat/registrieren', {
        firma: v.firma, uid: v.uid, kontakt: v.kontakt, plz: v.plz, ort: v.ort,
        domain: v.domain, versicherer: v.versicherer, kundennummer: v.kundennummer,
        vertragsnummer: v.vertragsnummer, addresseeIdentification: v.addresseeIdentification,
    });
    if (r.j.stand) e2.cert = r.j.stand;
    e2.konfigHinweis = { ok: r.ok && r.j.ok !== false, text: r.j.meldung || r.j.message || (r.ok ? 'Bestellt.' : 'Nicht bestellt.') };
    e2.suaVorschau = false;
    e2.busy = false; e2Zeichnen();
}

async function e2ZertHinterlegen() {
    const passwort = document.getElementById('e2Brief')?.value?.trim();
    if (!passwort) { e2.konfigHinweis = { ok: false, text: 'Bitte das Passwort aus dem Brief eingeben.' }; e2Zeichnen(); return; }
    e2.busy = true; e2Zeichnen();
    const r = await e2Post('/api/elm/zertifikat/abschliessen', { passwort });
    if (r.j.stand) e2.cert = r.j.stand;
    e2.konfigHinweis = { ok: r.ok && r.j.ok !== false, text: r.j.meldung || r.j.message || (r.ok ? 'Hinterlegt.' : 'Nicht hinterlegt.') };
    e2.busy = false; e2Zeichnen();
}

async function e2Anmelden(el) {
    const d = el.dataset;
    if (!d.versicherer || !d.kunde || !d.vertrag) {
        e2.konfigHinweis = { ok: false, text: 'Bei dieser Versicherung fehlen Kundennummer oder Vertragsnummer in den Stammdaten der Filiale.' };
        e2Zeichnen(); return;
    }
    e2.busy = true; e2Zeichnen();
    const r = await e2Post('/api/elm/meldungen/anmelden', {
        domain: d.domain, versicherer: d.versicherer, kundennummer: d.kunde, vertragsnummer: d.vertrag, kennung: d.kennung,
    });
    if (r.j.stand) e2.stand = r.j.stand;
    e2.konfigHinweis = { ok: r.ok && r.j.ok !== false, text: r.j.meldung || r.j.message || (r.ok ? 'Anmeldung gesendet.' : 'Nicht angemeldet.') };
    e2.busy = false; e2Zeichnen();
}

async function e2Abmelden(el) {
    if (!el.dataset.emp) return;
    if (!(await liquidConfirm('Die Anmeldung bei dieser Versicherung wird aufgehoben.', { title: 'Abmelden', yesLabel: 'Abmelden', noLabel: 'Abbrechen' }))) return;
    e2.busy = true; e2Zeichnen();
    const r = await e2Post('/api/elm/meldungen/anmeldungen/' + encodeURIComponent(el.dataset.id) + '/abmelden', { addresseeId: el.dataset.emp, storyId: '' });
    if (r.j.stand) e2.stand = r.j.stand;
    e2.konfigHinweis = { ok: r.ok && r.j.ok !== false, text: r.j.meldung || r.j.message || (r.ok ? 'Abgemeldet.' : 'Nicht abgemeldet.') };
    e2.busy = false; e2Zeichnen();
}
