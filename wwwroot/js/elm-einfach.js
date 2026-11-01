// Lohnmeldung, einfacher Ablauf (Walter 01.11.2026).
// Dieselben Schnittstellen wie bisher. Auf dem Bildschirm nur die nächste Frage.

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

const ee = {
    gebunden: false,
    schritt: 'start',
    jahr: 0,
    monat: 1,
    wahl: new Set(['qst', 'statistik']),
    jahrGesetzt: false,
    test: true,
    domaenen: null,
    stand: null,
    cert: null,
    checks: [],
    arm: null,
    kantone: null,
    ersatz: null,
    fokus: null,
    hinweis: '',
    zertHinweis: '',
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
    }
    ee.schritt = 'start';
    ee.hinweis = '';
    eeLaden();
}

function eeFilialeWechsel() {
    ee.jahrGesetzt = false;
    ee.wahl = new Set(['qst', 'statistik']);
    ee.arm = null;
    ee.checks = [];
    ee.kantone = null;
    ee.ersatz = null;
    ee.hinweis = '';
    eeLaden();
}

function eeFilialeId() {
    return (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
}

function eeFilialeName() {
    const id = eeFilialeId();
    if (!id || typeof allBranches === 'undefined') return '';
    const b = allBranches.find(x => x.id === id);
    return b ? (b.branchName || b.companyName || '') : '';
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
    const [stand, cert, dom] = await Promise.all([
        eeGet('/api/elm/meldungen'),
        eeGet('/api/elm/zertifikat'),
        filiale ? eeGet(domUrl) : Promise.resolve({ gruppen: [] }),
    ]);
    if (stand) ee.stand = stand;
    if (cert) ee.cert = cert;
    if (dom) ee.domaenen = dom;
    eeJahrVorwahl();
    ee.force = true;
    eeZeichnen();
}

function eeZeilen() {
    const out = [];
    for (const g of ee.domaenen?.gruppen || []) {
        for (const z of g.zeilen || []) out.push(z);
    }
    return out;
}

function eeKann(z) {
    return !!(z.art && EE_SEND.has(z.art) && !z.lohnsummen && z.an && z.an !== 'noch keiner zugeordnet');
}

function eeJahrVorwahl() {
    if (ee.jahrGesetzt) return;
    ee.jahrGesetzt = true;
    for (const z of eeZeilen()) {
        if (eeKann(z) && String(z.id).startsWith('ahv')) ee.wahl.add(z.id);
    }
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
    if (eeHatFrage(m))
        return 'Ein Empfänger hat eine Frage. Den Text schreibt Swissdec, oft auf Englisch.';
    if (m.status === 'error')
        return hin[0] || 'Die Meldung wurde nicht angenommen.';
    if (m.status === 'success')
        return 'Die Empfänger haben die Meldung angenommen.';
    return 'Die Meldung ist unterwegs.';
}

function eeZertWeg() {
    const sua = ee.cert?.sua;
    return !sua?.vorhanden || (sua.restTage != null && sua.restTage < 0);
}

function eeZeichnen() {
    const app = document.getElementById('eeApp');
    if (!app) return;
    const fokus = document.activeElement;
    if (!ee.force && fokus && fokus.closest && fokus.closest('#eeApp') && fokus.matches('input, textarea')) return;
    ee.force = false;
    app.innerHTML = eeKopf() + '<div class="ee-blatt">' + eeInhalt() + '</div>';
}

function eeKopf() {
    const zurueck = ee.schritt === 'start' ? '' : '<button type="button" class="ee-text" data-ee="start">Zurück</button>';
    return `<header class="ee-kopf">
        ${zurueck}
        <button type="button" class="ee-text" data-ee="neu" ${ee.busy ? 'disabled' : ''}>Aktualisieren</button>
    </header>`;
}

function eeInhalt() {
    if (!eeFilialeId())
        return '<h1>Zuerst eine Filiale wählen</h1><p class="ee-lead">Links in der Leiste. Danach siehst du nur die Empfänger dieser Filiale.</p>';
    if (ee.schritt === 'jahr') return eeJahrHtml();
    if (ee.schritt === 'monat') return eeMonatHtml();
    if (ee.schritt === 'versicherung') return eeVersicherungHtml();
    if (ee.schritt === 'zertifikat') return eeZertHtml();
    if (ee.schritt === 'stand') return eeStandHtml();
    if (ee.schritt === 'frage') return eeFrageHtml();
    return eeStartHtml();
}

function eeStartHtml() {
    const name = eeFilialeName() || 'dieser Filiale';
    const letzte = eeMeldungen().find(m => eeHatFrage(m) || m.status === 'error' || m.status === 'pending');
    const karte = letzte
        ? `<button type="button" class="ee-karte ee-hinweis" data-ee="stand" data-id="${eeEsc(letzte.id)}">
            <b>Es liegt etwas an</b><span>${eeEsc(eeLage(letzte))}</span></button>`
        : '';
    const zert = eeZertWeg()
        ? '<button type="button" class="ee-karte ee-hinweis" data-ee="zertifikat"><b>Zertifikat fehlt</b><span>Ohne Zertifikat nimmt Swissdec keine Meldung an.</span></button>'
        : '';
    return `<h1>Was möchtest du tun?</h1>
        <p class="ee-lead">Für ${eeEsc(name)}.</p>
        <div class="ee-tueren">
            <button type="button" class="ee-tuer" data-ee="jahr"><b>Jahresmeldung</b><span>Die Löhne des Jahres an die Kassen dieser Filiale.</span></button>
            <button type="button" class="ee-tuer" data-ee="monat"><b>Monatsmeldung</b><span>Quellensteuer an den Wohnkanton. Statistik an den Bund. Nur die Mitarbeiter dieser Filiale.</span></button>
            <button type="button" class="ee-tuer" data-ee="versicherung"><b>Versicherung</b><span>Bei den Versicherungen dieser Filiale anmelden. Das Profil kommt danach von selbst.</span></button>
        </div>
        ${karte}${zert}
        <p class="ee-still"><button type="button" class="ee-text" data-ee="zertifikat">Zertifikat</button></p>`;
}

function eeJahrHtml() {
    const kann = [];
    const spaeter = [];
    for (const z of eeZeilen()) {
        if (!z.an || z.an === 'noch keiner zugeordnet') continue;
        (eeKann(z) ? kann : spaeter).push(z);
    }
    const reihe = z => {
        const nr = [z.kundennummer, z.vertragsnummer].filter(Boolean).join(' · ');
        const an = nr ? `${z.an} · ${nr}` : z.an;
        return `<label class="ee-zeile">
            <span><b>${eeEsc(z.name)}</b><i>${eeEsc(an)}</i></span>
            <input type="checkbox" data-ee="wahl" data-id="${eeEsc(z.id)}" ${ee.wahl.has(z.id) ? 'checked' : ''}>
        </label>`;
    };
    const spaeterHtml = spaeter.length
        ? `<h2>Das geht noch nicht</h2><p class="ee-lead">Die Stelle ist zugeordnet. Die Lohndaten stehen noch nicht in der Datei.</p>
            <ul class="ee-liste">${spaeter.map(z => `<li>${eeEsc(z.name)} · ${eeEsc(z.an)}</li>`).join('')}</ul>`
        : '';
    return `<h1>Jahresmeldung ${ee.jahr}</h1>
        <p class="ee-lead">${eeEsc(eeFilialeName())}. Wer soll sie bekommen?</p>
        <div class="ee-step">
            <button type="button" data-ee="jahr-schritt" data-wert="-1" aria-label="Jahr zurück">‹</button>
            <b>${ee.jahr}</b>
            <button type="button" data-ee="jahr-schritt" data-wert="1" aria-label="Jahr vor">›</button>
        </div>
        ${ee.ersatz ? `<p class="ee-merk">Das ersetzt die Meldung vom ${eeEsc(eeDatum(ee.ersatz.gesendet))}.</p>` : ''}
        <div class="ee-liste">${kann.map(reihe).join('') || '<p class="ee-lead">Für diese Filiale ist noch keine Kasse zugeordnet, an die wir schicken können.</p>'}</div>
        ${spaeterHtml}
        ${eePruefBlock('jahr')}
        ${ee.fokus ? eeStandKurz() : ''}`;
}

function eeMonatHtml() {
    const name = `${EE_MONATE[ee.monat - 1] || ''} ${ee.jahr}`;
    const reihe = (id, titel, text) => `<label class="ee-zeile">
        <span><b>${titel}</b><i>${text}</i></span>
        <input type="checkbox" data-ee="wahl" data-id="${id}" ${ee.wahl.has(id) ? 'checked' : ''}>
    </label>`;
    let wohn = '';
    const v = (ee.checks || []).find(c => c.art === 'monthly');
    if (v && ee.wahl.has('qst') && ee.kantone) {
        wohn = (v.v.empfaenger || []).filter(e => e.domain !== 'Statistic' && e.addresseeId !== '#BFS').map(e => `<label class="ee-zeile">
            <span><b>Wohnkanton</b><i>${eeEsc(e.name || '')}</i></span>
            <input type="checkbox" data-ee="kanton" data-id="${eeEsc(e.addresseeId)}" ${ee.kantone.has(e.addresseeId) ? 'checked' : ''}>
        </label>`).join('');
    }
    return `<h1>Monatsmeldung</h1>
        <p class="ee-lead">${eeEsc(name)}. Nur die Mitarbeiter von ${eeEsc(eeFilialeName())}.</p>
        <div class="ee-step">
            <button type="button" data-ee="monat-schritt" data-wert="-1" aria-label="Monat zurück">‹</button>
            <b>${eeEsc(name)}</b>
            <button type="button" data-ee="monat-schritt" data-wert="1" aria-label="Monat vor">›</button>
        </div>
        <div class="ee-liste">
            ${reihe('qst', 'Quellensteuer', 'An den Kanton, in dem der Mitarbeiter wohnt.')}
            ${reihe('statistik', 'Statistik', 'An den Bund.')}
            ${wohn}
        </div>
        ${eePruefBlock('monat')}
        ${ee.fokus ? eeStandKurz() : ''}`;
}

function eePruefBlock(art) {
    const bereit = ee.arm && !ee.busy;
    return `<p class="ee-merk">${eeEsc(ee.hinweis || (art === 'monat'
        ? 'Danach Prüfen. Eine Testmeldung verarbeiten die Empfänger nicht.'
        : 'Hake an, danach Prüfen. Eine Testmeldung verarbeiten die Empfänger nicht.'))}</p>
        <div class="ee-aktionen">
            <label class="ee-test"><input type="checkbox" data-ee="test" ${ee.test ? 'checked' : ''}> Testmeldung</label>
            <button type="button" class="ee-ghost" data-ee="pruefen" ${ee.busy ? 'disabled' : ''}>Prüfen</button>
            <button type="button" class="ee-go" data-ee="senden" ${bereit ? '' : 'disabled'}>${ee.ersatz ? 'Ersatzmeldung' : 'Übermitteln'}</button>
        </div>`;
}

function eeVersicherungHtml() {
    const zeilen = eeZeilen().filter(z => ['UVG-LAA', 'UVGZ-LAAC', 'KTG-AMC', 'BVG-LPP'].includes(z.domain) && z.an && z.an !== 'noch keiner zugeordnet');
    const sua = ee.cert?.sua;
    const da = !!(sua?.vorhanden && !(sua.restTage != null && sua.restTage < 0));
    const rows = zeilen.map(z => {
        const anm = (ee.stand?.anmeldungen || []).find(m => (m.empfaenger || []).some(e => e.domain === z.domain && (e.name || '').includes(z.an)));
        const empf = anm?.empfaenger?.find(e => e.domain === z.domain);
        const nr = [z.kundennummer, z.vertragsnummer].filter(Boolean).join(' · ');
        let knopf = '<span class="ee-still">Zuerst das Zertifikat.</span>';
        if (da) {
            knopf = anm
                ? `<span class="ee-ok">Angemeldet</span> <button type="button" class="ee-text" data-ee="abmelden" data-id="${eeEsc(anm.id)}" data-emp="${eeEsc(empf?.addresseeId || '')}">Abmelden</button>`
                : `<button type="button" class="ee-go" data-ee="anmelden" data-domain="${eeEsc(z.domain)}" data-versicherer="${eeEsc(z.an)}" data-kunde="${eeEsc(z.kundennummer || '')}" data-vertrag="${eeEsc(z.vertragsnummer || '')}" data-kennung="${eeEsc(z.kennung || '')}">Anmelden</button>`;
        }
        return `<div class="ee-zeile"><span><b>${eeEsc(z.name)}</b><i>${eeEsc(nr ? z.an + ' · ' + nr : z.an)}</i></span><span class="ee-rechts">${knopf}</span></div>`;
    }).join('');
    return `<h1>Versicherung</h1>
        <p class="ee-lead">${eeEsc(eeFilialeName())}. Eine Anmeldung pro Versicherung. Das Profil kommt danach von selbst, als Aufgabe.</p>
        <div class="ee-liste">${rows || '<p class="ee-lead">Dieser Filiale ist noch keine Versicherung zugeordnet.</p>'}</div>
        ${ee.zertHinweis ? `<p class="ee-merk">${eeEsc(ee.zertHinweis)}</p>` : ''}
        <p class="ee-still"><button type="button" class="ee-text" data-ee="zertifikat">Zertifikat</button></p>`;
}

function eeZertHtml() {
    const sua = ee.cert?.sua;
    const da = !!(sua?.vorhanden && !(sua.restTage != null && sua.restTage < 0));
    const text = da
        ? 'Das Zertifikat ist gültig. Es gilt für alle Meldungen an Swissdec.'
        : 'Das Zertifikat fehlt noch. Swissdec schickt es per Brief. Danach trägst du das Passwort aus dem Brief ein.';
    const knoepfe = da ? '' : `<div class="ee-aktionen">
        <button type="button" class="ee-go" data-ee="zert-bestellen">Zertifikat bestellen</button>
    </div>
    <label class="ee-feld">Passwort aus dem Brief
        <input id="eeBrief" autocomplete="one-time-code">
    </label>
    <button type="button" class="ee-ghost" data-ee="zert-hinterlegen">Zertifikat hinterlegen</button>`;
    return `<h1>Zertifikat</h1><p class="ee-lead">${text}</p>${knoepfe}
        ${ee.zertHinweis ? `<p class="ee-merk">${eeEsc(ee.zertHinweis)}</p>` : ''}`;
}

function eeStandHtml() {
    const m = eeMeldungen().find(x => x.id === ee.fokus) || eeMeldungen()[0];
    if (!m) return '<h1>Noch keine Meldung</h1><p class="ee-lead">Schick zuerst eine Jahres- oder Monatsmeldung.</p>';
    return eeStandKarte(m, true);
}

function eeStandKurz() {
    const m = eeMeldungen().find(x => x.id === ee.fokus);
    return m ? eeStandKarte(m, false) : '';
}

function eeStandKarte(m, gross) {
    const domains = [...new Set((m.empfaenger || []).map(e => EE_DOM[e.domain] || e.domain).filter(Boolean))];
    const empf = (m.empfaenger || []).map(e => e.name).filter(Boolean);
    const hin = (m.empfaenger || []).flatMap(e => e.hinweise || []);
    const frei = (m.empfaenger || []).find(e => e.freigabeUrl);
    const quittung = (m.empfaenger || []).some(e => (e.nachrichten || []).some(n => (n.art || '').indexOf('Quittung') >= 0));
    const frage = eeHatFrage(m);
    const knoepfe = [
        frei ? `<button type="button" class="ee-go" data-ee="freigabe" data-url="${eeEsc(frei.freigabeUrl)}">Freigabe starten</button>` : '',
        (frage || m.status === 'pending') ? `<button type="button" class="ee-ghost" data-ee="frage" data-id="${eeEsc(m.id)}">DialogMessage abholen</button>` : '',
        quittung ? `<button type="button" class="ee-ghost" data-ee="quittung" data-id="${eeEsc(m.id)}">Quittung anzeigen</button>` : '',
        m.ersetzbar ? `<button type="button" class="ee-ghost" data-ee="ersatz" data-id="${eeEsc(m.id)}">Ersatzmeldung</button>` : '',
    ].filter(Boolean).join('');
    const schein = ee.quittung === m.id
        ? (m.empfaenger || []).flatMap(e => (e.nachrichten || []).filter(n => (n.art || '').indexOf('Quittung') >= 0).map(n => `<pre class="ee-pre">${eeEsc(n.xml || n.art || '')}</pre>`)).join('')
        : '';
    return `<section class="ee-stand">
        ${gross ? `<h1>${eeEsc(m.statusText || 'In Bearbeitung')}</h1>` : `<h2>${eeEsc(m.statusText || 'In Bearbeitung')}</h2>`}
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

function eeFrageHtml() {
    const m = eeMeldungen().find(x => x.id === ee.fokus);
    if (!m) return '<h1>Keine Frage</h1>';
    let form = '';
    for (const e of m.empfaenger || []) {
        for (const n of e.nachrichten || []) {
            if (!n.dialog || !n.dialog.beantwortbar || n.beantwortet) continue;
            const fragen = (n.dialog.absaetze || []).filter(p => p.istFrage);
            form += `<section class="ee-frage"><h2>${eeEsc(e.name)}</h2>`;
            if (n.dialog.titel) form += `<p class="ee-original"><b>${eeEsc(n.dialog.titel)}</b></p>`;
            if (n.dialog.beschreibung) form += `<p class="ee-original">${eeEsc(n.dialog.beschreibung)}</p>`;
            for (const p of fragen) {
                const breit = p.frageTyp === 'String' || p.frageTyp === 'DateTime' ? 'ee-breit' : 'ee-kurz';
                form += `<label class="ee-feld">${eeEsc(EE_TYP[p.frageTyp] || 'Antwort')}${p.optional ? '' : ' *'}
                    <input class="${breit}" data-frage="${eeEsc(p.id)}" value="${eeEsc(p.default || p.antwort || '')}">
                    <i>${eeEsc(p.label || '')}</i>
                </label>`;
            }
            form += `<button type="button" class="ee-go" data-ee="antwort" data-id="${eeEsc(m.id)}" data-emp="${eeEsc(e.addresseeId)}" data-story="${eeEsc(n.storyId)}">Antwort senden</button></section>`;
        }
    }
    return `<h1>Frage</h1>
        <p class="ee-lead">Den Text schreibt Swissdec, oft auf Englisch. Die Felder sind schon ausgefüllt. Antwort senden schickt sie zurück. Ändere nur, wenn du einen anderen Wert schicken willst.</p>
        ${form || '<p class="ee-lead">Keine offene Frage.</p>'}
        ${ee.hinweis ? `<p class="ee-merk">${eeEsc(ee.hinweis)}</p>` : ''}`;
}

function eeKlick(ev) {
    const el = ev.target.closest('[data-ee]');
    if (!el || el.dataset.ee === 'wahl' || el.dataset.ee === 'test' || el.dataset.ee === 'kanton') return;
    if (ee.busy && el.dataset.ee !== 'start') return;
    const a = el.dataset.ee;
    if (a === 'start') { ee.schritt = 'start'; ee.hinweis = ''; ee.force = true; eeZeichnen(); }
    else if (a === 'jahr' || a === 'monat' || a === 'versicherung' || a === 'zertifikat') {
        ee.schritt = a; ee.hinweis = ''; ee.zertHinweis = ''; ee.arm = null; ee.checks = []; ee.kantone = null; ee.fokus = null; ee.force = true; eeZeichnen();
    }
    else if (a === 'stand') { ee.fokus = el.dataset.id || ee.fokus; ee.schritt = 'stand'; ee.force = true; eeZeichnen(); }
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
}

function eeChange(ev) {
    const el = ev.target.closest('[data-ee]');
    if (!el) return;
    const a = el.dataset.ee;
    if (a === 'wahl') {
        if (el.checked) ee.wahl.add(el.dataset.id); else ee.wahl.delete(el.dataset.id);
        ee.arm = null;
    } else if (a === 'test') {
        ee.test = el.checked;
        ee.arm = null;
    } else if (a === 'kanton') {
        if (!ee.kantone) ee.kantone = new Set();
        if (el.checked) ee.kantone.add(el.dataset.id); else ee.kantone.delete(el.dataset.id);
    } else return;
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
    const dom = await eeGet('/api/elm/meldungen/domaenen?jahr=' + ee.jahr + '&companyProfileId=' + (eeFilialeId() || 0));
    if (dom) ee.domaenen = dom;
    eeJahrVorwahl();
    ee.force = true;
    eeZeichnen();
}

function eeMonatSchritt(delta) {
    let m = ee.monat + delta;
    if (m < 1) { m = 12; ee.jahr -= 1; }
    if (m > 12) { m = 1; ee.jahr += 1; }
    ee.monat = m;
    ee.arm = null;
    ee.checks = [];
    ee.kantone = null;
    ee.force = true;
    eeZeichnen();
}

function eeKlar(text) {
    const t = String(text || '');
    if (t.startsWith('FAK Nullmeldung')) return '';
    if (t.includes('Nicht definitiv abgeschlossene Monate'))
        return 'Einige Monate sind noch nicht abgeschlossen. Für eine Übung ist das in Ordnung.';
    if (t.includes('ohne AHV-pflichtigen Lohn')) return '';
    return t;
}

async function eePruefen() {
    if (ee.busy || !eeFilialeId()) return;
    ee.checks = [];
    ee.kantone = null;
    ee.hinweis = '';
    const arts = ee.schritt === 'monat'
        ? (ee.wahl.has('qst') || ee.wahl.has('statistik') ? ['monthly'] : [])
        : [...new Set(eeZeilen().filter(z => ee.wahl.has(z.id) && eeKann(z)).map(z => z.art))];
    if (!arts.length) {
        ee.hinweis = ee.schritt === 'monat' ? 'Quellensteuer oder Statistik wählen.' : 'Wähle mindestens eine Kasse, an die wir schicken können.';
        ee.arm = null;
        ee.force = true;
        eeZeichnen();
        return;
    }
    ee.busy = true;
    ee.hinweis = 'Wird geprüft.';
    ee.force = true;
    eeZeichnen();
    const checks = [];
    let ok = true;
    const saetze = [];
    for (const art of arts) {
        const q = new URLSearchParams({ art, jahr: String(ee.jahr), companyProfileId: String(eeFilialeId()) });
        if (art === 'monthly') q.set('monat', String(ee.monat));
        let j = {};
        let gut = false;
        try {
            const r = await fetch('/api/elm/meldungen/vorschau?' + q, { headers: ah() });
            j = await r.json().catch(() => ({}));
            gut = r.ok;
            if (!gut) j = { fehler: [j.message || 'Prüfung fehlgeschlagen'], empfaenger: [], warnungen: [] };
        } catch {
            j = { fehler: ['Server nicht erreichbar.'], empfaenger: [], warnungen: [] };
        }
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
    ee.busy = false;
    ee.force = true;
    eeZeichnen();
}

async function eeSenden() {
    if (!ee.arm || ee.busy || !eeFilialeId()) return;
    ee.busy = true;
    ee.hinweis = 'Wird geschickt.';
    ee.force = true;
    eeZeichnen();
    let hinweis = '';
    let gerade = null;
    for (const c of ee.checks) {
        const art = c.art;
        const adressaten = art === 'monthly' && ee.kantone
            ? Object.fromEntries((c.v.empfaenger || []).map(e => [e.addresseeId, ee.kantone.has(e.addresseeId)]))
            : null;
        const body = {
            art, jahr: ee.jahr, monat: art === 'monthly' ? ee.monat : null,
            ersetztId: ee.ersatz && ee.ersatz.art === art ? ee.ersatz.id : null,
            ziel: null, testmeldung: ee.test, freigabePortal: false, adressaten,
            companyProfileId: eeFilialeId(),
        };
        try {
            const r = await fetch('/api/elm/meldungen/senden', { method: 'POST', headers: ah(), body: JSON.stringify(body) });
            const j = await r.json().catch(() => ({}));
            if (j.stand) ee.stand = j.stand;
            if (!r.ok || !j.ok) { hinweis = j.meldung || j.message || 'Nicht gesendet.'; break; }
            gerade = j.vorgangId || gerade;
        } catch { hinweis = 'Server nicht erreichbar.'; break; }
    }
    ee.busy = false;
    ee.arm = null;
    ee.checks = [];
    ee.ersatz = null;
    ee.hinweis = hinweis;
    if (gerade) { ee.fokus = gerade; ee.schritt = 'stand'; }
    ee.force = true;
    eeZeichnen();
}

async function eeAktualisieren() {
    if (ee.busy) return;
    const offen = eeMeldungen().filter(m => !m.fertig);
    ee.busy = true;
    ee.force = true;
    eeZeichnen();
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
    } finally {
        ee.busy = false;
        ee.force = true;
        eeZeichnen();
    }
}

async function eeFrageOeffnen(id) {
    ee.fokus = id;
    ee.schritt = 'frage';
    ee.busy = true;
    ee.hinweis = '';
    ee.force = true;
    eeZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/' + encodeURIComponent(id) + '/abfragen', { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (j.stand) ee.stand = j.stand;
        if (!r.ok) ee.hinweis = j.message || 'Die Frage konnte nicht geholt werden.';
    } catch { ee.hinweis = 'Server nicht erreichbar.'; }
    ee.busy = false;
    ee.force = true;
    eeZeichnen();
}

function eeErsatz(id) {
    const m = eeMeldungen().find(x => x.id === id);
    if (!m) return;
    ee.ersatz = { id: m.id, art: m.art, gesendet: m.gesendet };
    ee.schritt = m.art === 'monthly' ? 'monat' : 'jahr';
    if (m.jahr) ee.jahr = m.jahr;
    if (m.monat) ee.monat = m.monat;
    ee.arm = null;
    ee.checks = [];
    ee.force = true;
    eeZeichnen();
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
    ee.busy = true;
    ee.force = true;
    eeZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/' + encodeURIComponent(m.id) + '/antworten', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({ addresseeId: e.addresseeId, storyId: n.storyId, werte }),
        });
        const j = await r.json().catch(() => ({}));
        if (j.stand) ee.stand = j.stand;
        ee.hinweis = j.meldung || j.message || (r.ok ? 'Antwort ist weg.' : 'Antwort nicht angekommen.');
        if (r.ok) ee.schritt = 'stand';
    } catch { ee.hinweis = 'Server nicht erreichbar.'; }
    ee.busy = false;
    ee.force = true;
    eeZeichnen();
}

async function eeAnmelden(el) {
    const d = el.dataset;
    if (!d.versicherer || !d.kunde || !d.vertrag) {
        ee.zertHinweis = 'Bei dieser Versicherung fehlen Name, Kundennummer oder Vertragsnummer.';
        ee.force = true;
        eeZeichnen();
        return;
    }
    ee.busy = true;
    ee.force = true;
    eeZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/anmelden', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({
                domain: d.domain, versicherer: d.versicherer, kundennummer: d.kunde,
                vertragsnummer: d.vertrag, kennung: d.kennung,
            }),
        });
        const j = await r.json().catch(() => ({}));
        if (j.stand) ee.stand = j.stand;
        ee.zertHinweis = j.meldung || j.message || '';
    } catch { ee.zertHinweis = 'Server nicht erreichbar.'; }
    ee.busy = false;
    ee.force = true;
    eeZeichnen();
}

async function eeAbmelden(el) {
    if (!el.dataset.emp) return;
    if (!(await liquidConfirm('Die Anmeldung bei dieser Versicherung wird aufgehoben.', {
        title: 'Abmelden', yesLabel: 'Abmelden', noLabel: 'Abbrechen' }))) return;
    ee.busy = true;
    ee.force = true;
    eeZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/anmeldungen/' + encodeURIComponent(el.dataset.id) + '/abmelden', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({ addresseeId: el.dataset.emp, storyId: '' }),
        });
        const j = await r.json().catch(() => ({}));
        if (j.stand) ee.stand = j.stand;
        ee.zertHinweis = j.meldung || j.message || '';
    } catch { ee.zertHinweis = 'Server nicht erreichbar.'; }
    ee.busy = false;
    ee.force = true;
    eeZeichnen();
}

async function eeZertBestellen() {
    if (!(await liquidConfirm('OneCrew bestellt das Zertifikat bei Swissdec. Es gilt für alle Meldungen.', {
        title: 'Zertifikat bestellen', yesLabel: 'Bestellen', noLabel: 'Abbrechen' }))) return;
    const v = ee.cert?.vorlage || {};
    ee.busy = true;
    ee.force = true;
    eeZeichnen();
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
    ee.busy = false;
    ee.force = true;
    eeZeichnen();
}

async function eeZertHinterlegen() {
    const passwort = document.getElementById('eeBrief')?.value?.trim();
    if (!passwort) { ee.zertHinweis = 'Bitte das Passwort aus dem Brief eingeben.'; ee.force = true; eeZeichnen(); return; }
    try {
        const r = await fetch('/api/elm/zertifikat/abschliessen', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({ passwort }),
        });
        const j = await r.json().catch(() => ({}));
        if (j.stand) ee.cert = j.stand;
        ee.zertHinweis = j.meldung || j.message || '';
    } catch { ee.zertHinweis = 'Server nicht erreichbar.'; }
    ee.busy = false;
    ee.force = true;
    eeZeichnen();
}
