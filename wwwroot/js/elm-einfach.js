// Lohnmeldung (Walter 01.11.2026). Übersicht über die Filialen, dann Monat oder Jahr.
// Dieselben Schnittstellen. «Alle Filialen» filtert nur, es entsteht keine gemeinsame Datei.

const EE_SEND = new Set(['annual', 'grenz-sfn', 'grenz-fr', 'grenz-it']);
const EE_MONATE = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
const EE_DOM = {
    'AHV-AVS': 'AHV', 'FAK-CAF': 'FAK', 'UVG-LAA': 'UVG', 'UVGZ-LAAC': 'UVGZ', 'KTG-AMC': 'KTG',
    'BVG-LPP': 'BVG', 'TaxAtSource': 'Quellensteuer', 'Statistic': 'Statistik',
    'TaxCrossborder': 'Grenzgänger', 'Tax': 'Lohnausweise',
};
const EE_TYP = {
    Boolean: 'ja oder nein', YesNoUnknown: 'ja, nein oder unbekannt',
    Date: 'TT.MM.JJJJ', DateTime: 'TT.MM.JJJJ HH:MM', Amount: 'Betrag',
    Integer: 'ganze Zahl', Double: 'Zahl', String: 'Text',
};
const EE_PORTAL = 'In den Übermittlungsprozessen der Versicherungen wird üblicherweise ein Link auf das Portal zurückgegeben. Falls der Link ausdrücklich gewünscht wird, geht diese Option mit.';
const EE_NAV = [
    ['uebersicht', 'Übersicht'],
    ['monat', 'Monat'],
    ['jahr', 'Jahr'],
    ['aufgaben', 'Aufgaben'],
    ['historie', 'Historie'],
    ['konfiguration', 'Konfiguration'],
];

const ee = {
    gebunden: false,
    schritt: 'uebersicht',
    filiale: 0,
    jahr: 0,
    monat: 1,
    wahl: new Set(['qst', 'statistik']),
    jahrGesetzt: false,
    portal: false,
    test: true,
    uebersicht: null,
    domaenen: null,
    stand: null,
    cert: null,
    checks: [],
    arm: null,
    kantone: null,
    ersatz: null,
    fokus: null,
    quittung: null,
    hinweis: '',
    zertHinweis: '',
    bvg: null,
    bvgOffen: false,
    busy: false,
    force: false,
};

function eeEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function eeDatum(iso) {
    if (!iso) return '';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return String(iso).slice(0, 16).replace('T', ' ');
    const p = n => String(n).padStart(2, '0');
    return `${p(d.getDate())}.${p(d.getMonth() + 1)}.${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}`;
}
function eeFilialeId() { return ee.filiale > 0 ? ee.filiale : null; }
function eeFilialeLabel(id, fallback) {
    const b = (typeof allBranches !== 'undefined' && Array.isArray(allBranches))
        ? allBranches.find(x => Number(x.id) === Number(id))
        : null;
    if (b && typeof filialKurzname === 'function') {
        const k = filialKurzname(b);
        if (k) return k;
    }
    return fallback || '';
}
function eeFilialenListe() {
    return (ee.uebersicht?.filialen || []).slice().sort((a, b) =>
        eeFilialeLabel(a.id, a.name).localeCompare(eeFilialeLabel(b.id, b.name), 'de'));
}
function eeFilialeName() {
    const z = (ee.uebersicht?.filialen || []).find(f => f.id === ee.filiale);
    return eeFilialeLabel(ee.filiale, z ? z.name : '');
}
function eeMeldungen() {
    return (ee.stand?.meldungen || [])
        .filter(m => !m.zertifizierung)
        .sort((a, b) => new Date(b.gesendet || 0) - new Date(a.gesendet || 0));
}
function eeHatFrage(m) {
    return (m.empfaenger || []).some(e => (e.nachrichten || []).some(n => n.dialog && n.dialog.beantwortbar && !n.beantwortet));
}
function eeLage(m) {
    if (!m) return '';
    const hin = (m.empfaenger || []).flatMap(e => e.hinweise || []);
    if (hin.some(h => String(h).includes('Keine Person übrig')))
        return 'Die Kasse hat die Meldung nicht angenommen. In der Datei ist keine Person für sie.';
    if (eeHatFrage(m)) return 'Ein Empfänger hat eine Frage.';
    if (m.status === 'error') return hin[0] || 'Die Meldung wurde nicht angenommen.';
    if (m.status === 'success') return 'Die Empfänger haben die Meldung angenommen.';
    return 'Übertragen. Der Empfänger ist noch nicht fertig.';
}
function eeZertWeg() {
    const sua = ee.cert?.sua;
    return !sua?.vorhanden || (sua.restTage != null && sua.restTage < 0);
}
function eeZeilen() {
    const out = [];
    for (const g of ee.domaenen?.gruppen || []) for (const z of g.zeilen || []) out.push(Object.assign({ gruppe: g.name, gruppeId: g.id }, z));
    return out;
}
function eeKann(z) {
    return !!(z.art && EE_SEND.has(z.art) && !z.lohnsummen && z.an && z.an !== 'noch keiner zugeordnet');
}

function eeInit() {
    const app = document.getElementById('eeApp');
    if (!app) return;
    if (!ee.gebunden) {
        ee.gebunden = true;
        app.addEventListener('click', eeKlick);
        app.addEventListener('change', eeChange);
        const h = new Date();
        ee.jahr = h.getMonth() === 0 ? h.getFullYear() - 1 : h.getFullYear();
        ee.monat = h.getMonth() === 0 ? 12 : h.getMonth();
        const vor = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : 0;
        ee.filiale = vor || 0;
    }
    ee.schritt = 'uebersicht';
    eeLaden();
}
function eeFilialeWechsel() {
    const vor = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : 0;
    ee.filiale = vor || 0;
    ee.jahrGesetzt = false;
    ee.arm = null;
    ee.checks = [];
    ee.kantone = null;
    eeLaden();
}

async function eeGet(url) {
    try {
        const r = await fetch(url, { headers: ah(), cache: 'no-store' });
        if (!r.ok) return null;
        return await r.json();
    } catch { return null; }
}
async function eeLaden() {
    ee.force = true;
    eeZeichnen();
    const filiale = eeFilialeId();
    const domUrl = '/api/elm/meldungen/domaenen?jahr=' + ee.jahr + '&companyProfileId=' + (filiale || 0);
    const [stand, cert, ueb, dom] = await Promise.all([
        eeGet('/api/elm/meldungen'),
        eeGet('/api/elm/zertifikat'),
        eeGet('/api/elm/meldungen/uebersicht?jahr=' + ee.jahr),
        filiale ? eeGet(domUrl) : Promise.resolve(null),
    ]);
    if (stand) ee.stand = stand;
    if (cert) ee.cert = cert;
    if (ueb) ee.uebersicht = ueb;
    if (filiale) { if (dom) ee.domaenen = dom; }
    else ee.domaenen = null;
    eeJahrVorwahl();
    ee.force = true;
    eeZeichnen();
}
function eeJahrVorwahl() {
    if (ee.jahrGesetzt || !ee.domaenen) return;
    ee.jahrGesetzt = true;
    for (const z of eeZeilen()) if (eeKann(z) && String(z.id).startsWith('ahv')) ee.wahl.add(z.id);
}

function eeZeichnen() {
    const app = document.getElementById('eeApp');
    if (!app) return;
    const fokus = document.activeElement;
    if (!ee.force && fokus && fokus.closest && fokus.closest('#eeApp') && (fokus.matches('input, textarea') || fokus.matches('select'))) return;
    ee.force = false;
    const inhalt = ee.schritt === 'uebersicht' ? eeUebersichtHtml()
        : ee.schritt === 'monat' ? eeMonatHtml()
        : ee.schritt === 'jahr' ? eeJahrHtml()
        : ee.schritt === 'aufgaben' ? eeAufgabenHtml()
        : ee.schritt === 'historie' ? eeHistorieHtml()
        : ee.schritt === 'konfiguration' ? eeKonfigHtml()
        : ee.schritt === 'frage' ? eeFrageHtml()
        : eeUebersichtHtml();
    app.innerHTML = eeKopf() + inhalt;
}

function eeKopf() {
    const nav = EE_NAV.map(([id, name]) =>
        `<button type="button" class="ee-nav${ee.schritt === id ? ' an' : ''}" data-ee="nav" data-wert="${id}">${name}</button>`).join('');
    return `<header class="ee-kopf">
        <nav class="ee-navs">${nav}</nav>
        <button type="button" class="ee-text" data-ee="neu" ${ee.busy ? 'disabled' : ''}>Aktualisieren</button>
    </header>`;
}

function eeUebersichtHtml() {
    const alle = eeFilialenListe();
    const liste = ee.filiale > 0 ? alle.filter(f => f.id === ee.filiale) : alle;
    const zeilen = liste.map(f => `<tr>
        <td>${eeEsc(eeFilialeLabel(f.id, f.name))}</td>
        <td>${eeEsc(f.letzterText)}</td>
        <td><button type="button" class="ee-text" data-ee="oeffnen" data-ziel="monat" data-id="${f.id}" data-monat="${f.letzterMonat || ''}">${eeEsc(f.monatText)}</button></td>
        <td><button type="button" class="ee-text" data-ee="oeffnen" data-ziel="jahr" data-id="${f.id}">${eeEsc(f.jahrText)}</button></td>
        <td>${f.aufgabe ? `<button type="button" class="ee-text" data-ee="stand" data-id="${eeEsc(f.aufgabeId)}">${eeEsc(f.aufgabe)}</button>` : ''}</td>
    </tr>`).join('');
    return `<div class="ee-split">
        <div>
            <h1>Lohnmeldung ${ee.jahr}</h1>
            <p class="ee-lead">Eine Zeile pro Filiale. Alle Filialen zeigt nur diese Liste, es wird nichts zusammengefasst.</p>
            <div class="ee-step">
                <button type="button" data-ee="jahr-schritt" data-wert="-1" aria-label="Jahr zurück">‹</button>
                <b>${ee.jahr}</b>
                <button type="button" data-ee="jahr-schritt" data-wert="1" aria-label="Jahr vor">›</button>
            </div>
            <table class="ee-tabelle">
                <thead><tr><th>Filiale</th><th>Lohnlauf</th><th>Monatsmeldung</th><th>Jahresmeldung</th><th>Aufgabe</th></tr></thead>
                <tbody>${zeilen || '<tr><td colspan="5">Keine Filiale.</td></tr>'}</tbody>
            </table>
        </div>
        <aside class="ee-seite">${eeAufgabenKurz()}</aside>
    </div>`;
}

function eeAufgabenKurz() {
    const punkte = [];
    if (eeZertWeg()) punkte.push('<button type="button" class="ee-karte" data-ee="nav" data-wert="konfiguration"><b>Zertifikat</b><span>Ohne Zertifikat nimmt Swissdec keine Meldung an.</span></button>');
    const seen = new Set();
    for (const m of eeMeldungen()) {
        if (ee.filiale > 0 && m.companyProfileId !== ee.filiale) continue;
        if (eeHatFrage(m) && !seen.has(m.id)) {
            seen.add(m.id);
            const name = (m.empfaenger || []).find(e => (e.nachrichten || []).some(n => n.dialog && !n.beantwortet))?.name || 'Empfänger';
            punkte.push(`<button type="button" class="ee-karte" data-ee="frage" data-id="${eeEsc(m.id)}"><b>Rückfrage</b><span>${eeEsc(name)}</span></button>`);
        } else if ((m.empfaenger || []).some(e => e.freigabeUrl) && !seen.has(m.id)) {
            seen.add(m.id);
            punkte.push(`<button type="button" class="ee-karte" data-ee="stand" data-id="${eeEsc(m.id)}"><b>Freigabe</b><span>${eeEsc(m.titel || '')}</span></button>`);
        }
        if (punkte.length >= 4) break;
    }
    return `<h2>Aufgaben</h2>${punkte.join('') || '<p class="ee-lead">Nichts liegt an.</p>'}`;
}

function eeMonatHtml() {
    if (!eeFilialeId()) return '<h1>Monat</h1><p class="ee-lead">Wähle links die Filiale. Alle Filialen ist nur die Übersicht.</p>';
    const name = `${EE_MONATE[ee.monat - 1] || ''} ${ee.jahr}`;
    const reihe = (id, titel, text) => `<label class="ee-zeile"><span><b>${titel}</b><i>${text}</i></span>
        <input type="checkbox" data-ee="wahl" data-id="${id}" ${ee.wahl.has(id) ? 'checked' : ''}></label>`;
    let wohn = '';
    const v = (ee.checks || []).find(c => c.art === 'monthly');
    if (v && ee.wahl.has('qst') && ee.kantone) {
        wohn = (v.v.empfaenger || []).filter(e => e.domain !== 'Statistic' && e.addresseeId !== '#BFS').map(e => `<label class="ee-zeile">
            <span><b>Wohnkanton</b><i>${eeEsc(e.name || '')}</i></span>
            <input type="checkbox" data-ee="kanton" data-id="${eeEsc(e.addresseeId)}" ${ee.kantone.has(e.addresseeId) ? 'checked' : ''}>
        </label>`).join('');
    }
    return `<h1>Monatsmeldung</h1>
        <p class="ee-lead">${eeEsc(eeFilialeName())}. Quellensteuer an den Wohnkanton, Statistik an den Bund.</p>
        <div class="ee-step">
            <button type="button" data-ee="monat-schritt" data-wert="-1" aria-label="Monat zurück">‹</button>
            <b>${eeEsc(name)}</b>
            <button type="button" data-ee="monat-schritt" data-wert="1" aria-label="Monat vor">›</button>
        </div>
        <div class="ee-liste">${reihe('qst', 'Quellensteuer', 'Kantone nach dem Prüfen, einzeln.')}${reihe('statistik', 'Statistik', 'Bundesamt für Statistik.')}${wohn}</div>
        ${eeLeiste()}
        ${eeFokusKarte()}`;
}

function eeJahrHtml() {
    if (!eeFilialeId()) return '<h1>Jahr</h1><p class="ee-lead">Wähle links die Filiale. Alle Filialen ist nur die Übersicht.</p>';
    const gruppen = ee.domaenen?.gruppen || [];
    const bloecke = gruppen.map(g => {
        const rows = (g.zeilen || []).map(z => eeJahrZeile(z)).join('');
        const portal = g.id === 'versicherung'
            ? `<label class="ee-zeile" title="${eeEsc(EE_PORTAL)}"><span><b>Portal</b><i>Freigabe im Portal gewünscht</i></span>
                <input type="checkbox" data-ee="portal" ${ee.portal ? 'checked' : ''}></label>` : '';
        return `<section><h2>${eeEsc(g.name)}</h2><div class="ee-liste">${rows}${portal}</div></section>`;
    }).join('');
    const bvg = ee.bvgOffen ? eeBvgHtml() : '';
    return `<h1>Jahresmeldung ${ee.jahr}</h1>
        <p class="ee-lead">${eeEsc(eeFilialeName())}. Was noch keine Lohndaten hat, bleibt sichtbar und geht nicht mit.</p>
        <div class="ee-step">
            <button type="button" data-ee="jahr-schritt" data-wert="-1" aria-label="Jahr zurück">‹</button>
            <b>${ee.jahr}</b>
            <button type="button" data-ee="jahr-schritt" data-wert="1" aria-label="Jahr vor">›</button>
        </div>
        ${ee.ersatz ? `<p class="ee-merk">Das ersetzt die Meldung vom ${eeEsc(eeDatum(ee.ersatz.gesendet))}.</p>` : ''}
        ${bloecke || '<p class="ee-lead">Empfänger werden geladen.</p>'}
        ${bvg}
        ${eeLeiste()}
        ${eeFokusKarte()}`;
}
function eeJahrZeile(z) {
    const nr = [z.kundennummer, z.vertragsnummer].filter(Boolean).join(' · ');
    const an = !z.an || z.an === 'noch keiner zugeordnet' ? 'noch keiner zugeordnet' : (nr ? `${z.an} · ${nr}` : z.an);
    if (z.lohnsummen) {
        return `<div class="ee-zeile"><span><b>${eeEsc(z.name)}</b><i>${eeEsc(an)} · Lohndaten fehlen in der Meldung</i></span>
            <button type="button" class="ee-text" data-ee="bvg">Lohnsummen</button></div>`;
    }
    if (!eeKann(z)) {
        const hin = z.domain === 'FAK-CAF' ? 'Geht mit der AHV-Meldung mit' : 'Geht noch nicht';
        return `<div class="ee-zeile"><span><b>${eeEsc(z.name)}</b><i>${eeEsc(an)}</i></span><span class="ee-still">${hin}</span></div>`;
    }
    return `<label class="ee-zeile"><span><b>${eeEsc(z.name)}</b><i>${eeEsc(an)}</i></span>
        <input type="checkbox" data-ee="wahl" data-id="${eeEsc(z.id)}" ${ee.wahl.has(z.id) ? 'checked' : ''}></label>`;
}
function eeBvgHtml() {
    const j = ee.bvg;
    if (!j) return '<p class="ee-lead">Lohnsummen werden gerechnet.</p>';
    const zeilen = (j.zeilen || []).map(z => `<div class="ee-zeile"><span><b>${eeEsc(z.name)}</b><i>Vorschlag ${Number(z.vorschlag).toFixed(2)}</i></span>
        <input class="ee-geld" data-bvg="${z.employeeId}" value="${eeEsc(Number(z.betrag).toFixed(2))}" inputmode="decimal" aria-label="Lohnsumme"></div>`).join('');
    return `<section class="ee-bvg"><h2>Lohnsummen ${ee.jahr}</h2>
        <p class="ee-lead">Für die ganze Firma. Eine Meldung an die Pensionskasse gibt es noch nicht. ${j.freigegeben ? 'Zur Übermittlung freigegeben.' : ''}</p>
        ${zeilen || '<p class="ee-lead">Keine Lohnsummen.</p>'}
        <div class="ee-aktionen">
            <button type="button" class="ee-ghost" data-ee="bvg-frei" data-wert="0">Speichern</button>
            <button type="button" class="ee-go" data-ee="bvg-frei" data-wert="1">Zur Übermittlung freigeben</button>
        </div></section>`;
}

function eeLeiste() {
    const bereit = ee.arm && !ee.busy;
    return `<p class="ee-merk">${eeEsc(ee.hinweis || 'Testmeldung ankreuzen, dann Prüfen. Eine Testmeldung verarbeiten die Empfänger nicht.')}</p>
        <div class="ee-aktionen">
            <label class="ee-test"><input type="checkbox" data-ee="test" ${ee.test ? 'checked' : ''}> Testmeldung</label>
            <button type="button" class="ee-ghost" data-ee="pruefen" ${ee.busy ? 'disabled' : ''}>Prüfen</button>
            <button type="button" class="ee-go" data-ee="senden" ${bereit ? '' : 'disabled'}>${ee.ersatz ? 'Ersatzmeldung' : 'Übermitteln'}</button>
        </div>`;
}

function eeFokusKarte() {
    const m = eeMeldungen().find(x => x.id === ee.fokus);
    return m ? eeKarte(m) : '';
}
function eeKarte(m) {
    const domains = [...new Set((m.empfaenger || []).map(e => EE_DOM[e.domain] || e.domain).filter(Boolean))];
    const empf = (m.empfaenger || []).map(e => e.name).filter(Boolean);
    const hin = (m.empfaenger || []).flatMap(e => e.hinweise || []);
    const frei = (m.empfaenger || []).find(e => e.freigabeUrl);
    const quittung = (m.empfaenger || []).some(e => (e.nachrichten || []).some(n => (n.art || '').indexOf('Quittung') >= 0));
    const knoepfe = [
        frei ? `<button type="button" class="ee-go" data-ee="freigabe" data-url="${eeEsc(frei.freigabeUrl)}">Freigabe starten</button>` : '',
        (eeHatFrage(m) || m.status === 'pending') ? `<button type="button" class="ee-ghost" data-ee="frage" data-id="${eeEsc(m.id)}">DialogMessage abholen</button>` : '',
        quittung ? `<button type="button" class="ee-ghost" data-ee="quittung" data-id="${eeEsc(m.id)}">Quittung anzeigen</button>` : '',
        m.ersetzbar ? `<button type="button" class="ee-ghost" data-ee="ersatz" data-id="${eeEsc(m.id)}">Ersatzmeldung</button>` : '',
    ].filter(Boolean).join('');
    const schein = ee.quittung === m.id
        ? (m.empfaenger || []).flatMap(e => (e.nachrichten || []).filter(n => (n.art || '').indexOf('Quittung') >= 0).map(n => `<pre class="ee-pre">${eeEsc(n.xml || '')}</pre>`)).join('')
        : '';
    return `<section class="ee-stand">
        <h2>${eeEsc(m.statusText || 'In Bearbeitung')}</h2>
        <p class="ee-lead">${eeEsc(eeLage(m))}</p>
        <dl class="ee-fakt">
            <dt>Zeit</dt><dd>${eeEsc(eeDatum(m.gesendet) || '—')}</dd>
            <dt>DeclarationID</dt><dd>${eeEsc(m.declarationId || 'noch nicht vergeben')}</dd>
            <dt>Domäne</dt><dd>${eeEsc(domains.join(', ') || '—')}</dd>
            <dt>Empfänger</dt><dd>${eeEsc(empf.join(', ') || '—')}</dd>
            <dt>Benachrichtigungen</dt><dd>${eeEsc(hin[0] || 'keine')}</dd>
            <dt>Status</dt><dd>${eeEsc(m.statusText || 'In Bearbeitung')}</dd>
        </dl>
        ${schein}
        ${knoepfe ? `<div class="ee-aktionen">${knoepfe}</div>` : ''}
    </section>`;
}

function eeAufgabenHtml() {
    const liste = [];
    if (eeZertWeg()) liste.push('<button type="button" class="ee-karte" data-ee="nav" data-wert="konfiguration"><b>Zertifikat</b><span>Fehlt oder ist abgelaufen.</span></button>');
    for (const m of eeMeldungen()) {
        if (ee.filiale > 0 && m.companyProfileId !== ee.filiale) continue;
        if (eeHatFrage(m)) {
            const name = (m.empfaenger || []).find(e => (e.nachrichten || []).some(n => n.dialog && !n.beantwortet))?.name || 'Empfänger';
            liste.push(`<button type="button" class="ee-karte" data-ee="frage" data-id="${eeEsc(m.id)}"><b>Rückfrage von ${eeEsc(name)}</b><span>${eeEsc(eeDatum(m.gesendet))}</span></button>`);
        }
        for (const e of m.empfaenger || []) {
            if (e.freigabeUrl) liste.push(`<button type="button" class="ee-karte" data-ee="freigabe" data-url="${eeEsc(e.freigabeUrl)}"><b>Freigabe starten</b><span>${eeEsc(e.name || '')}</span></button>`);
        }
        if (m.status === 'error' && !eeHatFrage(m))
            liste.push(`<button type="button" class="ee-karte" data-ee="stand" data-id="${eeEsc(m.id)}"><b>Nicht angenommen</b><span>${eeEsc(eeLage(m))}</span></button>`);
    }
    return `<h1>Aufgaben</h1><p class="ee-lead">${ee.filiale ? eeEsc(eeFilialeName()) : 'Alle Filialen'}.</p>
        <div class="ee-tueren">${liste.join('') || '<p class="ee-lead">Nichts liegt an.</p>'}</div>
        ${eeFokusKarte()}`;
}

function eeHistorieHtml() {
    const liste = eeMeldungen().filter(m => !ee.filiale || m.companyProfileId === ee.filiale);
    const zeilen = liste.map(m => `<button type="button" class="ee-hist${m.id === ee.fokus ? ' an' : ''}" data-ee="stand" data-id="${eeEsc(m.id)}">
        <b>${eeEsc(m.statusText || '')}</b><span>${eeEsc(eeDatum(m.gesendet))}</span><span>${eeEsc(m.declarationId || 'ohne DeclarationID')}</span><span>${eeEsc(m.titel || '')}</span>
    </button>`).join('');
    return `<h1>Historie</h1><p class="ee-lead">Jede Sendung. Eine Zeile öffnet dieselben Angaben wie nach dem Übermitteln. Ersatzmeldung startet von dort.</p>
        <div class="ee-liste">${zeilen || '<p class="ee-lead">Noch keine Meldung.</p>'}</div>
        ${eeFokusKarte()}`;
}

function eeKonfigHtml() {
    const sua = ee.cert?.sua;
    const da = !!(sua?.vorhanden && !(sua.restTage != null && sua.restTage < 0));
    const text = da
        ? 'Das Zertifikat ist gültig. Es gilt für alle Meldungen.'
        : 'Das Zertifikat fehlt. Swissdec schickt das Passwort per Brief.';
    const knoepfe = da ? '' : `<div class="ee-aktionen"><button type="button" class="ee-go" data-ee="zert-bestellen">Zertifikat bestellen</button></div>
        <label class="ee-feld">Passwort aus dem Brief<input id="eeBrief" autocomplete="one-time-code" class="ee-kurz"></label>
        <button type="button" class="ee-ghost" data-ee="zert-hinterlegen">Zertifikat hinterlegen</button>`;
    const vers = eeVersicherungHtml();
    return `<h1>Konfiguration</h1><p class="ee-lead">${text}</p>${knoepfe}
        ${ee.zertHinweis ? `<p class="ee-merk">${eeEsc(ee.zertHinweis)}</p>` : ''}
        <h2>Anmeldung</h2>
        <p class="ee-lead">${eeFilialeId() ? eeEsc(eeFilialeName()) + '. ' : ''}Eine Anmeldung pro Versicherung. Das Profil kommt danach als Aufgabe.</p>
        ${vers}`;
}
function eeVersicherungHtml() {
    if (!eeFilialeId()) return '<p class="ee-lead">Wähle links die Filiale.</p>';
    const zeilen = eeZeilen().filter(z => ['UVG-LAA', 'UVGZ-LAAC', 'KTG-AMC', 'BVG-LPP'].includes(z.domain) && z.an && z.an !== 'noch keiner zugeordnet');
    const sua = ee.cert?.sua;
    const da = !!(sua?.vorhanden && !(sua.restTage != null && sua.restTage < 0));
    return zeilen.map(z => {
        const anm = (ee.stand?.anmeldungen || []).find(m => (m.empfaenger || []).some(e => e.domain === z.domain && (e.name || '').includes(z.an)));
        const empf = anm?.empfaenger?.find(e => e.domain === z.domain);
        const nr = [z.kundennummer, z.vertragsnummer].filter(Boolean).join(' · ');
        let knopf = '<span class="ee-still">Zuerst das Zertifikat.</span>';
        if (da) knopf = anm
            ? `<span>Angemeldet</span> <button type="button" class="ee-text" data-ee="abmelden" data-id="${eeEsc(anm.id)}" data-emp="${eeEsc(empf?.addresseeId || '')}">Abmelden</button>`
            : `<button type="button" class="ee-go" data-ee="anmelden" data-domain="${eeEsc(z.domain)}" data-versicherer="${eeEsc(z.an)}" data-kunde="${eeEsc(z.kundennummer || '')}" data-vertrag="${eeEsc(z.vertragsnummer || '')}" data-kennung="${eeEsc(z.kennung || '')}">Anmelden</button>`;
        return `<div class="ee-zeile"><span><b>${eeEsc(z.name)}</b><i>${eeEsc(nr ? z.an + ' · ' + nr : z.an)}</i></span><span class="ee-rechts">${knopf}</span></div>`;
    }).join('') || '<p class="ee-lead">Dieser Filiale ist noch keine Versicherung zugeordnet.</p>';
}

function eeFrageHtml() {
    const m = eeMeldungen().find(x => x.id === ee.fokus);
    if (!m) return '<h1>Frage</h1><p class="ee-lead">Keine offene Frage.</p>';
    let form = '';
    for (const e of m.empfaenger || []) {
        for (const n of e.nachrichten || []) {
            if (!n.dialog || !n.dialog.beantwortbar || n.beantwortet) continue;
            form += `<section class="ee-frage"><h2>${eeEsc(e.name)}</h2>`;
            if (n.dialog.titel) form += `<p class="ee-original"><b>${eeEsc(n.dialog.titel)}</b></p>`;
            if (n.dialog.beschreibung) form += `<p class="ee-original">${eeEsc(n.dialog.beschreibung)}</p>`;
            for (const p of (n.dialog.absaetze || []).filter(x => x.istFrage)) {
                const breit = p.frageTyp === 'String' || p.frageTyp === 'DateTime' ? 'ee-breit' : 'ee-kurz';
                form += `<label class="ee-feld">${eeEsc(EE_TYP[p.frageTyp] || 'Antwort')}${p.optional ? '' : ' *'}
                    <input class="${breit}" data-frage="${eeEsc(p.id)}" value="${eeEsc(p.default || p.antwort || '')}"><i>${eeEsc(p.label || '')}</i></label>`;
            }
            form += `<button type="button" class="ee-go" data-ee="antwort" data-id="${eeEsc(m.id)}" data-emp="${eeEsc(e.addresseeId)}" data-story="${eeEsc(n.storyId)}">Antwort senden</button></section>`;
        }
    }
    return `<h1>Frage</h1>
        <p class="ee-lead">Den Text schreibt Swissdec, oft auf Englisch. Die Felder sind schon ausgefüllt. Antwort senden schickt sie zurück.</p>
        ${form || '<p class="ee-lead">Keine offene Frage. Aktualisieren holt sie, falls sie noch kommt.</p>'}
        ${ee.hinweis ? `<p class="ee-merk">${eeEsc(ee.hinweis)}</p>` : ''}`;
}

function eeKlick(ev) {
    const el = ev.target.closest('[data-ee]');
    if (!el) return;
    const a = el.dataset.ee;
    if (a === 'wahl' || a === 'test' || a === 'kanton' || a === 'portal') return;
    if (ee.busy && a !== 'nav') return;
    if (a === 'nav') { ee.schritt = el.dataset.wert; ee.hinweis = ''; ee.force = true; eeZeichnen(); if (el.dataset.wert === 'jahr' || el.dataset.wert === 'konfiguration') eeDomaenenNach(); }
    else if (a === 'oeffnen') eeOeffnen(el);
    else if (a === 'stand') { ee.fokus = el.dataset.id; ee.schritt = ee.schritt === 'uebersicht' || ee.schritt === 'aufgaben' ? 'historie' : ee.schritt; ee.force = true; eeZeichnen(); }
    else if (a === 'jahr-schritt') eeJahrSchritt(parseInt(el.dataset.wert, 10));
    else if (a === 'monat-schritt') eeMonatSchritt(parseInt(el.dataset.wert, 10));
    else if (a === 'pruefen') eePruefen();
    else if (a === 'senden') eeSenden();
    else if (a === 'neu') eeAktualisieren();
    else if (a === 'frage') eeFrageOeffnen(el.dataset.id);
    else if (a === 'freigabe' && el.dataset.url) window.open(el.dataset.url, '_blank', 'noopener');
    else if (a === 'quittung') { ee.quittung = ee.quittung === el.dataset.id ? null : el.dataset.id; ee.fokus = el.dataset.id; ee.force = true; eeZeichnen(); }
    else if (a === 'ersatz') eeErsatz(el.dataset.id);
    else if (a === 'antwort') eeAntworten(el);
    else if (a === 'anmelden') eeAnmelden(el);
    else if (a === 'abmelden') eeAbmelden(el);
    else if (a === 'zert-bestellen') eeZertBestellen();
    else if (a === 'zert-hinterlegen') eeZertHinterlegen();
    else if (a === 'bvg') eeBvgLaden();
    else if (a === 'bvg-frei') eeBvgFrei(el.dataset.wert === '1');
}
function eeChange(ev) {
    const el = ev.target.closest('[data-ee]');
    if (!el) return;
    const a = el.dataset.ee;
    if (a === 'wahl') { if (el.checked) ee.wahl.add(el.dataset.id); else ee.wahl.delete(el.dataset.id); ee.arm = null; }
    else if (a === 'test') { ee.test = el.checked; ee.arm = null; }
    else if (a === 'portal') { ee.portal = el.checked; ee.arm = null; }
    else if (a === 'kanton') {
        if (!ee.kantone) ee.kantone = new Set();
        if (el.checked) ee.kantone.add(el.dataset.id); else ee.kantone.delete(el.dataset.id);
        ee.arm = null;
    } else return;
    ee.force = true;
    eeZeichnen();
}
function eeOeffnen(el) {
    const id = parseInt(el.dataset.id, 10) || 0;
    ee.schritt = el.dataset.ziel === 'jahr' ? 'jahr' : 'monat';
    if (el.dataset.monat) ee.monat = parseInt(el.dataset.monat, 10) || ee.monat;
    ee.arm = null;
    ee.checks = [];
    ee.hinweis = '';
    ee.jahrGesetzt = false;
    ee.domaenen = null;
    if (id && id !== ee.filiale && typeof selectSidebarBranch === 'function') {
        selectSidebarBranch(String(id));
        return;
    }
    eeLaden();
}
async function eeDomaenenNach() {
    if (!eeFilialeId() || ee.domaenen) return;
    const dom = await eeGet('/api/elm/meldungen/domaenen?jahr=' + ee.jahr + '&companyProfileId=' + ee.filiale);
    if (dom) ee.domaenen = dom;
    eeJahrVorwahl();
    ee.force = true;
    eeZeichnen();
}
async function eeJahrSchritt(delta) {
    const n = ee.jahr + delta;
    if (n < 2015 || n > 2035) return;
    ee.jahr = n;
    ee.arm = null;
    ee.checks = [];
    ee.jahrGesetzt = false;
    ee.bvg = null;
    eeLaden();
}
function eeMonatSchritt(delta) {
    let m = ee.monat + delta;
    if (m < 1) { m = 12; ee.jahr -= 1; }
    if (m > 12) { m = 1; ee.jahr += 1; }
    ee.monat = m;
    ee.arm = null;
    ee.checks = [];
    ee.kantone = null;
    ee.hinweis = '';
    ee.force = true;
    eeZeichnen();
}

function eeKlar(text) {
    const t = String(text || '');
    if (t.startsWith('FAK Nullmeldung')) return '';
    if (t.includes('Nicht definitiv abgeschlossene Monate')) return 'Einige Monate sind noch nicht abgeschlossen. Für eine Übung ist das in Ordnung.';
    if (t.includes('ohne AHV-pflichtigen Lohn')) return '';
    return t;
}
async function eePruefen() {
    if (ee.busy || !eeFilialeId()) return;
    const arts = ee.schritt === 'monat'
        ? (ee.wahl.has('qst') || ee.wahl.has('statistik') ? ['monthly'] : [])
        : [...new Set(eeZeilen().filter(z => ee.wahl.has(z.id) && eeKann(z)).map(z => z.art))];
    if (!arts.length) {
        ee.hinweis = ee.schritt === 'monat' ? 'Quellensteuer oder Statistik wählen.' : 'Wähle mindestens eine Stelle, an die wir schicken können.';
        ee.arm = null; ee.force = true; eeZeichnen(); return;
    }
    ee.busy = true; ee.hinweis = 'Wird geprüft.'; ee.checks = []; ee.kantone = null; ee.force = true; eeZeichnen();
    const checks = [];
    let ok = true;
    const saetze = [];
    for (const art of arts) {
        const q = new URLSearchParams({ art, jahr: String(ee.jahr), companyProfileId: String(ee.filiale) });
        if (art === 'monthly') q.set('monat', String(ee.monat));
        let j = {}, gut = false;
        try {
            const r = await fetch('/api/elm/meldungen/vorschau?' + q, { headers: ah() });
            j = await r.json().catch(() => ({}));
            gut = r.ok;
            if (!gut) j = { fehler: [j.message || 'Prüfung fehlgeschlagen'], empfaenger: [], warnungen: [] };
        } catch { j = { fehler: ['Server nicht erreichbar.'], empfaenger: [], warnungen: [] }; }
        checks.push({ art, v: j });
        for (const f of j.fehler || []) saetze.push(f);
        for (const w of j.warnungen || []) { const k = eeKlar(w); if (k) saetze.push(k); }
        if (!gut || (j.fehler || []).length || (j.technisch || []).length || j.sendenGesperrt || !(j.empfaenger || []).length) ok = false;
    }
    ee.checks = checks;
    if (ee.schritt === 'monat' && checks[0]) {
        ee.kantone = new Set();
        for (const e of checks[0].v.empfaenger || []) {
            const statistik = e.domain === 'Statistic' || e.addresseeId === '#BFS';
            if (statistik ? ee.wahl.has('statistik') : ee.wahl.has('qst')) ee.kantone.add(e.addresseeId);
        }
        if (!ee.kantone.size) ok = false;
    }
    const namen = [];
    for (const c of checks) for (const e of c.v.empfaenger || []) if (e.name && !namen.includes(e.name)) namen.push(e.name);
    ee.arm = ok ? 'ja' : null;
    ee.hinweis = ok
        ? 'Geprüft. Übermitteln schickt die Meldung' + (namen.length ? ' an ' + namen.join(', ') : '') + '.'
        : (saetze[0] || 'Die Prüfung hat etwas gefunden. Übermitteln bleibt zu.');
    ee.busy = false; ee.force = true; eeZeichnen();
}
async function eeSenden() {
    if (!ee.arm || ee.busy || !eeFilialeId()) return;
    ee.busy = true; ee.hinweis = 'Wird geschickt.'; ee.force = true; eeZeichnen();
    let hinweis = '', gerade = null;
    for (const c of ee.checks) {
        const art = c.art;
        const versicherung = art !== 'monthly' && art !== 'annual' && !String(art).startsWith('grenz');
        const adressaten = art === 'monthly' && ee.kantone
            ? Object.fromEntries((c.v.empfaenger || []).map(e => [e.addresseeId, ee.kantone.has(e.addresseeId)]))
            : null;
        try {
            const r = await fetch('/api/elm/meldungen/senden', {
                method: 'POST', headers: ah(),
                body: JSON.stringify({
                    art, jahr: ee.jahr, monat: art === 'monthly' ? ee.monat : null,
                    ersetztId: ee.ersatz && ee.ersatz.art === art ? ee.ersatz.id : null,
                    ziel: null, testmeldung: ee.test,
                    freigabePortal: ee.portal && versicherung,
                    adressaten, companyProfileId: ee.filiale,
                }),
            });
            const j = await r.json().catch(() => ({}));
            if (j.stand) ee.stand = j.stand;
            if (!r.ok || !j.ok) { hinweis = j.meldung || j.message || 'Nicht gesendet.'; break; }
            gerade = j.vorgangId || gerade;
        } catch { hinweis = 'Server nicht erreichbar.'; break; }
    }
    ee.busy = false; ee.arm = null; ee.checks = []; ee.ersatz = null; ee.hinweis = hinweis;
    if (gerade) ee.fokus = gerade;
    ee.force = true; eeZeichnen();
    eeGet('/api/elm/meldungen/uebersicht?jahr=' + ee.jahr).then(ueb => { if (ueb) { ee.uebersicht = ueb; ee.force = true; eeZeichnen(); } });
}

async function eeAktualisieren() {
    if (ee.busy) return;
    const offen = eeMeldungen().filter(m => !m.fertig);
    ee.busy = true; ee.force = true; eeZeichnen();
    try {
        if (!offen.length) {
            const s = await eeGet('/api/elm/meldungen');
            if (s) ee.stand = s;
        } else {
            for (const m of offen.slice(0, 8)) {
                const r = await fetch('/api/elm/meldungen/' + encodeURIComponent(m.id) + '/abfragen', { method: 'POST', headers: ah() });
                const j = await r.json().catch(() => ({}));
                if (r.ok && j.stand) ee.stand = j.stand;
            }
        }
        const ueb = await eeGet('/api/elm/meldungen/uebersicht?jahr=' + ee.jahr);
        if (ueb) ee.uebersicht = ueb;
    } finally { ee.busy = false; ee.force = true; eeZeichnen(); }
}
async function eeFrageOeffnen(id) {
    ee.fokus = id; ee.schritt = 'frage'; ee.busy = true; ee.hinweis = ''; ee.force = true; eeZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/' + encodeURIComponent(id) + '/abfragen', { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (j.stand) ee.stand = j.stand;
        if (!r.ok) ee.hinweis = j.message || 'Die Frage konnte nicht geholt werden.';
    } catch { ee.hinweis = 'Server nicht erreichbar.'; }
    ee.busy = false; ee.force = true; eeZeichnen();
}
function eeErsatz(id) {
    const m = eeMeldungen().find(x => x.id === id);
    if (!m) return;
    ee.ersatz = { id: m.id, art: m.art, gesendet: m.gesendet };
    ee.schritt = m.art === 'monthly' ? 'monat' : 'jahr';
    if (m.jahr) ee.jahr = m.jahr;
    if (m.monat) ee.monat = m.monat;
    ee.arm = null; ee.checks = []; ee.jahrGesetzt = false; ee.domaenen = null;
    if (m.companyProfileId && m.companyProfileId !== ee.filiale && typeof selectSidebarBranch === 'function') {
        selectSidebarBranch(String(m.companyProfileId));
        return;
    }
    eeLaden();
}
async function eeAntworten(el) {
    const m = eeMeldungen().find(x => x.id === el.dataset.id);
    const e = m?.empfaenger?.find(x => x.addresseeId === el.dataset.emp);
    const n = e?.nachrichten?.find(x => x.storyId === el.dataset.story);
    if (!n?.dialog) return;
    const box = el.closest('.ee-frage');
    const werte = {};
    for (const p of n.dialog.absaetze.filter(x => x.istFrage)) {
        const f = box?.querySelector(`[data-frage="${CSS.escape(String(p.id))}"]`);
        werte[p.id] = f ? f.value.trim() : (p.default || null);
    }
    ee.busy = true; ee.force = true; eeZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/' + encodeURIComponent(m.id) + '/antworten', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({ addresseeId: e.addresseeId, storyId: n.storyId, werte }),
        });
        const j = await r.json().catch(() => ({}));
        if (j.stand) ee.stand = j.stand;
        ee.hinweis = j.meldung || j.message || (r.ok ? 'Antwort ist weg.' : 'Antwort nicht angekommen.');
        if (r.ok) ee.schritt = 'historie';
    } catch { ee.hinweis = 'Server nicht erreichbar.'; }
    ee.busy = false; ee.force = true; eeZeichnen();
}
async function eeAnmelden(el) {
    const d = el.dataset;
    if (!d.versicherer || !d.kunde || !d.vertrag) {
        ee.zertHinweis = 'Bei dieser Versicherung fehlen Name, Kundennummer oder Vertragsnummer.';
        ee.force = true; eeZeichnen(); return;
    }
    ee.busy = true; ee.force = true; eeZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/anmelden', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({ domain: d.domain, versicherer: d.versicherer, kundennummer: d.kunde, vertragsnummer: d.vertrag, kennung: d.kennung }),
        });
        const j = await r.json().catch(() => ({}));
        if (j.stand) ee.stand = j.stand;
        ee.zertHinweis = j.meldung || j.message || '';
    } catch { ee.zertHinweis = 'Server nicht erreichbar.'; }
    ee.busy = false; ee.force = true; eeZeichnen();
}
async function eeAbmelden(el) {
    if (!el.dataset.emp) return;
    if (!(await liquidConfirm('Die Anmeldung bei dieser Versicherung wird aufgehoben.', { title: 'Abmelden', yesLabel: 'Abmelden', noLabel: 'Abbrechen' }))) return;
    ee.busy = true; ee.force = true; eeZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/anmeldungen/' + encodeURIComponent(el.dataset.id) + '/abmelden', {
            method: 'POST', headers: ah(), body: JSON.stringify({ addresseeId: el.dataset.emp, storyId: '' }),
        });
        const j = await r.json().catch(() => ({}));
        if (j.stand) ee.stand = j.stand;
        ee.zertHinweis = j.meldung || j.message || '';
    } catch { ee.zertHinweis = 'Server nicht erreichbar.'; }
    ee.busy = false; ee.force = true; eeZeichnen();
}
async function eeZertBestellen() {
    if (!(await liquidConfirm('OneCrew bestellt das Zertifikat bei Swissdec. Es gilt für alle Meldungen.', { title: 'Zertifikat bestellen', yesLabel: 'Bestellen', noLabel: 'Abbrechen' }))) return;
    const v = ee.cert?.vorlage || {};
    ee.busy = true; ee.force = true; eeZeichnen();
    try {
        const r = await fetch('/api/elm/zertifikat/registrieren', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({
                firma: v.firma, uid: v.uid, kontakt: v.kontakt, plz: v.plz, ort: v.ort,
                domain: v.domain, versicherer: v.versicherer, kundennummer: v.kundennummer,
                vertragsnummer: v.vertragsnummer, addresseeIdentification: v.addresseeIdentification,
            }),
        });
        const j = await r.json().catch(() => ({}));
        if (j.stand) ee.cert = j.stand;
        ee.zertHinweis = j.meldung || j.message || 'Nicht erledigt.';
    } catch { ee.zertHinweis = 'Server nicht erreichbar.'; }
    ee.busy = false; ee.force = true; eeZeichnen();
}
async function eeZertHinterlegen() {
    const passwort = document.getElementById('eeBrief')?.value?.trim();
    if (!passwort) { ee.zertHinweis = 'Bitte das Passwort aus dem Brief eingeben.'; ee.force = true; eeZeichnen(); return; }
    try {
        const r = await fetch('/api/elm/zertifikat/abschliessen', { method: 'POST', headers: ah(), body: JSON.stringify({ passwort }) });
        const j = await r.json().catch(() => ({}));
        if (j.stand) ee.cert = j.stand;
        ee.zertHinweis = j.meldung || j.message || '';
    } catch { ee.zertHinweis = 'Server nicht erreichbar.'; }
    ee.force = true; eeZeichnen();
}
async function eeBvgLaden() {
    ee.bvgOffen = !ee.bvgOffen;
    if (!ee.bvgOffen) { ee.force = true; eeZeichnen(); return; }
    ee.bvg = null; ee.force = true; eeZeichnen();
    ee.bvg = await eeGet('/api/elm/meldungen/bvg-lohnsummen?jahr=' + ee.jahr) || { zeilen: [] };
    ee.force = true; eeZeichnen();
}
async function eeBvgFrei(frei) {
    const zeilen = [...document.querySelectorAll('#eeApp [data-bvg]')].map(el => ({
        employeeId: parseInt(el.dataset.bvg, 10), betrag: Number(String(el.value).replace(',', '.')) || 0,
    }));
    ee.busy = true; ee.force = true; eeZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/bvg-lohnsummen', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({ jahr: ee.jahr, freigegeben: frei, zeilen }),
        });
        const j = await r.json().catch(() => ({}));
        ee.hinweis = j.message || (r.ok ? (frei ? 'Lohnsummen zur Übermittlung freigegeben.' : 'Lohnsummen gespeichert.') : 'Nicht gespeichert.');
        ee.bvg = await eeGet('/api/elm/meldungen/bvg-lohnsummen?jahr=' + ee.jahr);
    } catch { ee.hinweis = 'Server nicht erreichbar.'; }
    ee.busy = false; ee.force = true; eeZeichnen();
}
