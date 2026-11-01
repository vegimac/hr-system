// ELM Cockpit, Crew-Anzeige (Walter 01.11.2026).
// Dieselben Schnittstellen wie das Cockpit im HR-Hub. Die Technik bleibt hinter
// Aktualisieren. Sichtbar ist die Entscheidung, der Empfänger und der Status.

const XC_SEND = new Set(['annual', 'grenz-sfn', 'grenz-fr', 'grenz-it']);
const XC_MONATE = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];
const XC_DOM = {
    'AHV-AVS': 'AHV', 'FAK-CAF': 'FAK', 'UVG-LAA': 'UVG', 'UVGZ-LAAC': 'UVGZ', 'KTG-AMC': 'KTG',
    'BVG-LPP': 'BVG', 'TaxAtSource': 'Quellensteuer', 'Statistic': 'Statistik',
    'TaxCrossborder': 'Grenzgänger', 'Tax': 'Lohnausweise',
};
const XC_PORTAL = 'In den Übermittlungsprozessen der Versicherungen wird üblicherweise ein Link auf das Portal zurückgegeben. Falls der Link ausdrücklich gewünscht wird, geht diese Option mit.';
const XC_LAUF = {
    'PRÜFUNG': 'Wird geprüft',
    'ÜBERMITTLUNG': 'Wird übermittelt',
    'STAND': 'Stand wird geholt',
    'NACHRICHT': 'Mitteilung wird geholt',
    'ANTWORT': 'Antwort wird gesendet',
    'ANMELDUNG': 'Anmeldung läuft',
    'ABMELDUNG': 'Abmeldung läuft',
};

const xc = {
    gebunden: false,
    jahr: 0,
    monat: 1,
    art: 'jahr',
    sicht: 'senden',
    wahl: new Set(),
    ahvGesetzt: false,
    portal: false,
    test: true,
    domaenen: null,
    stand: null,
    cert: null,
    verb: null,
    checks: [],
    arm: null,
    kantone: null,
    ersatz: null,
    ruhe: false,
    fokus: null,
    gerade: null,
    historie: false,
    sheet: null,
    bvg: null,
    bvgStand: null,
    quittung: null,
    hinweis: '',
    konfigHinweis: '',
    busy: false,
    lauf: '',
    force: false,
};

function xcEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function xcDatum(iso) {
    if (!iso) return '';
    const d = new Date(iso);
    if (isNaN(d)) return '';
    const p = n => String(n).padStart(2, '0');
    return `${p(d.getDate())}.${p(d.getMonth() + 1)}.${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

function xcGeld(n) {
    const x = Number(n);
    if (!isFinite(x)) return '';
    return x.toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function xcZahl(s) {
    const n = Number(String(s ?? '').replace(/['\s]/g, '').replace(',', '.'));
    return isFinite(n) ? Math.round(n * 100) / 100 : null;
}

function xcInit() {
    const app = document.getElementById('xcApp');
    if (!app) return;
    if (!xc.gebunden) {
        xc.gebunden = true;
        app.addEventListener('click', xcKlick);
        app.addEventListener('change', xcChange);
        const h = new Date();
        xc.jahr = h.getMonth() === 0 ? h.getFullYear() - 1 : h.getFullYear();
        xc.monat = h.getMonth() === 0 ? 12 : h.getMonth();
        xc.wahl.add('qst');
        xc.wahl.add('statistik');
    }
    xc.sicht = 'senden';
    xc.sheet = null;
    xcLaden();
}

async function xcGet(url) {
    try {
        const r = await fetch(url, { headers: ah(), cache: 'no-store' });
        if (!r.ok) return null;
        return await r.json();
    } catch { return null; }
}

async function xcLaden() {
    xc.force = true;
    xcZeichnen();
    const [stand, cert, verb, dom] = await Promise.all([
        xcGet('/api/elm/meldungen'),
        xcGet('/api/elm/zertifikat'),
        xcGet('/api/elm/meldungen/verbindung').then(async (alt) => {
            try {
                const r = await fetch('/api/elm/meldungen/verbindung/pruefen', { method: 'POST', headers: ah() });
                if (r.ok) return await r.json();
            } catch { /* letzter bekannter Stand */ }
            return alt;
        }),
        xcGet(xcDomaenenUrl()),
    ]);
    if (stand) xc.stand = stand;
    xc.standFehler = !stand && !xc.stand;
    if (cert) xc.cert = cert;
    if (verb) xc.verb = verb;
    if (!xcFilialeId()) { xc.ohneFiliale = true; xc.domaenen = { gruppen: [] }; xc.domFehler = false; }
    else if (dom) { xc.ohneFiliale = false; xc.domaenen = dom; xc.domFehler = false; xcWahlAhv(); }
    else xc.domFehler = !xc.domaenen;
    xc.force = true;
    xcZeichnen();
}

function xcWahlAhv() {
    if (xc.ahvGesetzt) return;
    xc.ahvGesetzt = true;
    for (const z of xcJahrZeilen()) {
        if (String(z.id).startsWith('ahv') && z.an !== 'noch keiner zugeordnet' && xcSendbar(z)) xc.wahl.add(z.id);
    }
}

function xcFilialeId() {
    return (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
}

function xcFiliale() {
    const id = xcFilialeId();
    if (!id || typeof allBranches === 'undefined') return null;
    return allBranches.find(x => x.id === id) || null;
}

function xcFilialeName() {
    const b = xcFiliale();
    return b ? (b.branchName || b.companyName || '') : '';
}

function xcKanton() {
    const k = (xcFiliale()?.kantonCode || '').trim().toUpperCase();
    return k;
}

function xcDomaenenUrl() {
    const id = xcFilialeId();
    if (!id) return '/api/elm/meldungen/domaenen?jahr=' + xc.jahr + '&companyProfileId=0';
    return '/api/elm/meldungen/domaenen?jahr=' + xc.jahr + '&companyProfileId=' + id;
}

function xcFilialeWechsel() {
    xc.ahvGesetzt = false;
    for (const id of [...xc.wahl]) {
        if (id !== 'qst' && id !== 'statistik') xc.wahl.delete(id);
    }
    xcEntschaerfen();
    xcLaden();
}

function xcAnText(z) {
    const nr = [z.kundennummer, z.vertragsnummer].filter(Boolean).join(' · ');
    return nr ? `${z.an} · ${nr}` : (z.an || '');
}

function xcJahrZeilen() {
    const out = [];
    for (const g of xc.domaenen?.gruppen || []) {
        for (const z of g.zeilen || []) out.push(Object.assign({}, z, { gruppe: g.id, gruppeName: g.name }));
    }
    return out;
}

function xcSendbar(z) { return !!z.art && XC_SEND.has(z.art); }

function xcArts() {
    const arts = [];
    for (const z of xcJahrZeilen()) {
        if (xc.wahl.has(z.id) && xcSendbar(z) && !arts.includes(z.art)) arts.push(z.art);
    }
    return arts;
}

function xcOffenOhneDatei() {
    return xcJahrZeilen().filter(z => xc.wahl.has(z.id) && !xcSendbar(z));
}

function xcKern() {
    const ids = [...xc.wahl].sort().join(',');
    return [xc.art, xc.jahr, xc.monat, ids, xc.portal ? 1 : 0, xc.ersatz?.id || ''].join('|');
}

function xcBereit() {
    if (xc.arm !== xcKern()) return false;
    if (xc.stand?.sendenGesperrt) return false;
    if (xc.art === 'monat' && (!xc.kantone || !xc.kantone.size)) return false;
    return true;
}

function xcEntschaerfen() {
    xc.arm = null;
    xc.checks = [];
    xc.kantone = null;
    xc.hinweis = '';
    if (!xc.ruhe) xc.ersatz = null;
}

function xcListe() {
    const monat = xc.art === 'monat';
    return (xc.stand?.meldungen || [])
        .filter(m => !m.zertifizierung && (monat ? m.art === 'monthly' : m.art !== 'monthly'))
        .sort((a, b) => new Date(b.gesendet || 0) - new Date(a.gesendet || 0));
}

function xcAufgaben() {
    const list = [];
    const sua = xc.cert?.sua;
    if (sua && sua.restTage != null && sua.restTage <= 30) {
        const weg = sua.restTage < 0 || !sua.vorhanden;
        list.push({
            ton: weg ? 'rot' : 'gelb',
            titel: weg ? 'Zertifikat abgelaufen' : `Zertifikat läuft in ${sua.restTage} Tagen ab`,
            text: 'Ein Zertifikat, gültig für alle Swissdec-Standards.',
            aktion: 'konfig', label: 'Konfiguration',
        });
    }
    for (const m of xc.stand?.meldungen || []) {
        if (m.zertifizierung) continue;
        const hin = (m.empfaenger || []).flatMap(e => e.hinweise || []);
        if (m.status === 'error') {
            list.push({ ton: 'rot', titel: m.titel || 'Meldung', text: hin[0] || m.statusText || '', aktion: 'fokus', id: m.id, label: 'Öffnen' });
        }
        for (const e of m.empfaenger || []) {
            if ((e.nachrichten || []).some(n => n.dialog && n.dialog.beantwortbar && !n.beantwortet))
                list.push({ ton: 'gelb', titel: `Rückfrage von ${e.name}`, text: m.titel || '', aktion: 'fokus', id: m.id, label: 'Öffnen' });
            if (e.freigabeUrl)
                list.push({ ton: 'gelb', titel: `Freigabe bei ${e.name}`, text: m.titel || '', aktion: 'freigabe', url: e.freigabeUrl, label: 'Freigabe starten' });
        }
    }
    return list;
}

function xcLink() {
    const sua = xc.cert?.sua;
    const online = xc.verb?.letzte?.erreichbar?.stufe === 'ok';
    const weg = !sua?.vorhanden || (sua?.restTage != null && sua.restTage < 0);
    if (xc.lauf) return { wort: XC_LAUF[xc.lauf] || xc.lauf, klasse: '' };
    if (!xc.verb) return { wort: 'Verbindung wird geprüft', klasse: '' };
    if (!online) return { wort: 'Keine Verbindung', klasse: 'xc-rot' };
    if (weg) return { wort: 'Zertifikat fehlt', klasse: 'xc-rot' };
    if (sua.restTage != null && sua.restTage <= 30) return { wort: 'Zertifikat läuft ab', klasse: 'xc-gelb' };
    return { wort: '', klasse: '' };
}

function xcSatz() {
    if (!xcFilialeId())
        return 'Zuerst eine Filiale in der Seitenleiste wählen. Die Empfänger sind die Lohndatenempfänger dieser Filiale.';
    if (xc.sicht === 'konfig')
        return 'Hier liegen das Zertifikat und die Anmeldungen bei den Versicherungen. Die Lohnmeldung schickst du über Zurück.';
    const sua = xc.cert?.sua;
    const weg = sua && (!sua.vorhanden || (sua.restTage != null && sua.restTage < 0));
    if (weg)
        return 'Ohne gültiges Zertifikat nimmt Swissdec keine Meldung an. Das Zertifikat liegt unter Konfiguration.';
    if (xc.art === 'monat') {
        const name = `${XC_MONATE[xc.monat - 1] || ''} ${xc.jahr}`.trim();
        const kt = xcKanton();
        if (xc.arm === xcKern() && xcBereit())
            return `Geprüft. Übermitteln schickt die Monatsmeldung ${name}${kt ? ' an Kanton ' + kt : ''}. Eine Testmeldung verarbeiten die Empfänger nicht.`;
        return `Monatsmeldung ${name}. Quellensteuer geht an ${kt ? 'Kanton ' + kt : 'den Kanton der Filiale'}. Danach Prüfen.`;
    }
    if (xc.arm === xcKern() && xcBereit())
        return `Geprüft. Übermitteln schickt die Jahresmeldung ${xc.jahr}. Eine Testmeldung verarbeiten die Empfänger nicht.`;
    const gewaehlt = xcJahrZeilen().filter(z => xc.wahl.has(z.id));
    if (!gewaehlt.length)
        return `Jahresmeldung ${xc.jahr}. Hake an, wer sie erhalten soll.`;
    if (!xcArts().length)
        return 'Die angehakten Stellen haben noch keine Lohndaten. Übermitteln bleibt zu.';
    return `Jahresmeldung ${xc.jahr}. Angehakt erhält die Meldung. Danach Prüfen.`;
}

function xcZeichnen() {
    const app = document.getElementById('xcApp');
    if (!app) return;
    const fokus = document.activeElement;
    if (!xc.force && fokus && fokus.closest && fokus.closest('#page-elm-cockpit') && fokus.matches('input, textarea')) return;
    xc.force = false;
    const link = xcLink();
    const kopf = `<div class="xc-bar"></div>
        <header class="xc-top">
            <div>
                <div class="xc-kicker">ELM${xcFilialeName() ? ' · ' + xcEsc(xcFilialeName()) : ''}</div>
                <h1>${xc.sicht === 'konfig' ? 'Zertifikat' : 'Lohnmeldung'}</h1>
            </div>
            ${link.wort ? `<div class="xc-link ${link.klasse}" aria-live="polite">${xcEsc(link.wort)}</div>` : '<div class="xc-link"></div>'}
            <button type="button" class="xc-text" data-xc="${xc.sicht === 'konfig' ? 'zurueck' : 'konfig'}" title="${xc.sicht === 'konfig' ? 'Zurück zur Lohnmeldung' : 'Zertifikat und Anmeldung bei den Versicherungen'}">${xc.sicht === 'konfig' ? 'Zurück' : 'Konfiguration'}</button>
            <button type="button" class="xc-ghost xc-klein" data-xc="aktualisieren" title="Holt den aktuellen Stand und neue Mitteilungen." ${xc.busy ? 'disabled' : ''}>Aktualisieren</button>
        </header>
        <p class="xc-satz">${xcEsc(xcSatz())}</p>`;
    app.className = 'xc' + (xc.busy ? ' ist-busy' : '');
    app.innerHTML = kopf + (xc.sicht === 'konfig' ? xcKonfigHtml() : xcSendHtml()) + xcSheetHtml();
}

function xcSendHtml() {
    const monat = xc.art === 'monat';
    const seg = `<div class="xc-seg" role="tablist">
        <button type="button" data-xc="art" data-wert="jahr" class="${monat ? '' : 'an'}">Jahresmeldung</button>
        <button type="button" data-xc="art" data-wert="monat" class="${monat ? 'an' : ''}">Monatsmeldung</button>
    </div>`;
    const jahr = `<div class="xc-step"><span>Jahr</span>
        <button type="button" data-xc="jahr" data-wert="-1" aria-label="Jahr zurück">‹</button>
        <b>${xc.jahr}</b>
        <button type="button" data-xc="jahr" data-wert="1" aria-label="Jahr vor">›</button></div>`;
    const monatStep = monat ? `<div class="xc-step"><span>Monat</span>
        <button type="button" data-xc="monat" data-wert="-1" aria-label="Monat zurück">‹</button>
        <b>${XC_MONATE[xc.monat - 1] || ''}</b>
        <button type="button" data-xc="monat" data-wert="1" aria-label="Monat vor">›</button></div>` : '';
    const koerper = monat ? xcMonatHtml() : xcJahrHtml();
    const geht = xcGehtHtml();
    const fehler = xcFehlerHtml();
    const sperre = xc.stand?.sendenGesperrt
        ? `<p class="xc-fault">${xcEsc(xc.stand.sendenGesperrt)}</p>` : '';
    const ersatz = xc.ersatz
        ? `<p class="xc-ersatz">Ersatz für ${xcEsc(xc.ersatz.titel || 'die Meldung')} vom ${xcEsc(xcDatum(xc.ersatz.gesendet))}.
            <button type="button" class="xc-text" data-xc="ersatz-aus">Abbrechen</button></p>` : '';
    const bereit = xcBereit() && !xc.busy;
    return `<div class="xc-flaeche">
        <div>
            <div class="xc-wahl">${seg}${jahr}${monatStep}</div>
            ${ersatz}
            ${koerper}
            ${geht}
            ${xc.hinweis ? `<p class="xc-hin">${xcEsc(xc.hinweis)}</p>` : ''}
            ${fehler}
            ${sperre}
            <div class="xc-commit">
                <label class="xc-test" title="Als Testmeldung gekennzeichnete Übermittlungen werden von den Empfängern nicht verarbeitet.">
                    <input class="xc-check" type="checkbox" data-xc="test" ${xc.test ? 'checked' : ''}> Testmeldung
                </label>
                <button type="button" class="xc-ghost" data-xc="pruefen" ${xc.busy ? 'disabled' : ''}>Prüfen</button>
                <button type="button" class="xc-go" data-xc="senden" ${bereit ? '' : 'disabled'}>${xc.ersatz ? 'Ersatzmeldung' : 'Übermitteln'}</button>
            </div>
            ${xcStatusHtml()}
        </div>
        <aside class="xc-seite">${xcAufgabenHtml()}</aside>
    </div>`;
}

function xcJahrHtml() {
    if (xc.ohneFiliale) return '<p class="xc-an">Zuerst eine Filiale in der Seitenleiste wählen.</p>';
    if (xc.domFehler) return '<p class="xc-fault">Empfänger nicht geladen.</p>';
    if (!xc.domaenen) return '<p class="xc-an">Empfänger werden geladen.</p>';
    const gruppen = xc.domaenen.gruppen || [];
    if (!gruppen.length) return '<p class="xc-an">Keine Empfänger zugeordnet.</p>';
    const html = gruppen.map(g => {
        const rows = (g.zeilen || []).map(z => xcZeile(z)).join('');
        const portal = g.portal && g.id === 'versicherung'
            ? `<label class="xc-row xc-portal" title="${xcEsc(XC_PORTAL)}">
                <span class="xc-name">Portal</span>
                <span class="xc-an">Freigabe im Portal gewünscht</span>
                <span></span>
                <input class="xc-check" type="checkbox" data-xc="portal" ${xc.portal ? 'checked' : ''}>
               </label>` : '';
        return `<section class="xc-spalte"><div class="xc-gruppe">${xcEsc(g.name)}</div>${rows}${portal}</section>`;
    }).join('');
    return `<div class="xc-gruppen">${html}</div>`;
}

function xcZeile(z) {
    const an = xc.wahl.has(z.id);
    let tag = '';
    if (z.lohnsummen) tag = xc.bvgStand?.jahr === xc.jahr && xc.bvgStand.freigegeben ? 'Freigegeben' : 'Lohndaten fehlen';
    else if (!xcSendbar(z)) tag = 'Lohndaten fehlen';
    const knopf = z.lohnsummen ? ' <button type="button" class="xc-text" data-xc="bvg">Lohnsummen</button>' : '';
    const tagTitle = tag === 'Lohndaten fehlen'
        ? 'Die Stelle ist zugeordnet. Die Beträge stehen noch nicht in der Meldung, sie geht nicht mit.'
        : (tag === 'Freigegeben' ? 'Die Lohnsummen sind für dieses Jahr freigegeben.' : '');
    return `<div class="xc-row">
        <span class="xc-name">${xcEsc(z.name)}</span>
        <span class="xc-an">${xcEsc(xcAnText(z))}${knopf}</span>
        <span class="xc-tag"${tagTitle ? ` title="${xcEsc(tagTitle)}"` : ''}>${xcEsc(tag)}</span>
        <input class="xc-check" type="checkbox" data-xc="wahl" data-id="${xcEsc(z.id)}" ${an ? 'checked' : ''} aria-label="${xcEsc(z.name)}">
    </div>`;
}

function xcIstStatistik(e) {
    return e.domain === 'Statistic' || e.addresseeId === '#BFS';
}

function xcIstKantonDerFiliale(e) {
    const kt = xcKanton();
    if (!kt || xcIstStatistik(e)) return false;
    const id = String(e.addresseeId || '');
    const kennung = String(e.identification || '').trim().toUpperCase();
    return id === '#QST-' + kt || kennung === kt;
}

function xcMonatHtml() {
    const kt = xcKanton();
    const reihe = (id, name, an) => `<div class="xc-row">
        <span class="xc-name">${name}</span>
        <span class="xc-an">${an}</span>
        <span></span>
        <input class="xc-check" type="checkbox" data-xc="wahl" data-id="${id}" ${xc.wahl.has(id) ? 'checked' : ''} aria-label="${name}">
    </div>`;
    return `<div class="xc-gruppe">Monatsmeldung</div>
        ${reihe('qst', 'Quellensteuer', kt ? 'Kanton ' + kt : 'Kanton fehlt an der Filiale')}
        ${reihe('statistik', 'Statistik', 'Bundesamt für Statistik')}`;
}

function xcGehtHtml() {
    const namen = [];
    for (const c of xc.checks) {
        for (const e of c.v.empfaenger || []) {
            if (c.art === 'monthly' && xc.kantone && !xc.kantone.has(e.addresseeId)) continue;
            if (e.name && !namen.includes(e.name)) namen.push(e.name);
        }
    }
    if (!namen.length || xc.arm !== xcKern()) return '';
    return `<p class="xc-geht">Geht an ${xcEsc(namen.join(', '))}</p>`;
}

function xcFehlerHtml() {
    const zeilen = [];
    for (const c of xc.checks) {
        for (const f of c.v.fehler || []) zeilen.push(f);
        if ((c.v.technisch || []).length) zeilen.push('Die Meldung ist fehlerhaft.');
        if (c.v.sendenGesperrt) zeilen.push(c.v.sendenGesperrt);
        if (c.art === 'monthly' && xc.kantone && !xc.kantone.size && xc.wahl.has('qst') && !xcKanton())
            zeilen.push('An der Filiale ist kein Kanton hinterlegt.');
        if (!(c.v.fehler || []).length && !(c.v.empfaenger || []).length) zeilen.push('Die Meldung hat keinen Empfänger.');
    }
    const warn = xc.checks.flatMap(c => c.v.warnungen || []);
    return (zeilen.length ? `<ul class="xc-fault">${zeilen.map(z => `<li>${xcEsc(z)}</li>`).join('')}</ul>` : '')
        + (warn.length ? `<ul class="xc-warn">${warn.map(z => `<li>${xcEsc(z)}</li>`).join('')}</ul>` : '');
}

function xcStatusHtml() {
    const liste = xcListe();
    const m = (xc.fokus && liste.find(x => x.id === xc.fokus)) || liste[0];
    if (!m) {
        if (xc.standFehler) return '<p class="xc-fault xc-leer">Meldungen nicht geladen.</p>';
        return xc.historie ? '' : '<p class="xc-an xc-leer">Noch keine Meldung in diesem Bereich.</p>';
    }
    const gross = xc.gerade === m.id || xc.fokus === m.id || m.status === 'error' || m.status === 'pending'
        || (m.empfaenger || []).some(e => e.freigabeUrl || (e.nachrichten || []).some(n => n.dialog && n.dialog.beantwortbar && !n.beantwortet));
    const kopf = gross ? xcMeldungHtml(m) : `<button type="button" class="xc-leis" data-xc="fokus" data-id="${xcEsc(m.id)}">Letzte Meldung · ${xcEsc(m.statusText || 'In Bearbeitung')} · ${xcEsc(xcDatum(m.gesendet))} · ${xcEsc(m.declarationId || 'ohne DeclarationID')}</button>`;
    const n = liste.length;
    const hist = `<button type="button" class="xc-leis" data-xc="historie">Frühere Meldungen · ${n}</button>`
        + (xc.historie ? `<div class="xc-hist">${liste.map(x => `<button type="button" class="xc-hist-row${x.id === m.id ? ' an' : ''}" data-xc="fokus" data-id="${xcEsc(x.id)}">
            <b>${xcEsc(x.statusText || '')}</b><span>${xcEsc(xcDatum(x.gesendet))}</span><span>${xcEsc(x.titel || '')}</span></button>`).join('')}</div>` : '');
    return kopf + hist;
}

function xcMeldungHtml(m) {
    const domains = [...new Set((m.empfaenger || []).map(e => XC_DOM[e.domain] || e.domain).filter(Boolean))];
    const empf = (m.empfaenger || []).map(e => e.name).filter(Boolean);
    const hin = (m.empfaenger || []).flatMap(e => e.hinweise || []);
    const frei = (m.empfaenger || []).find(e => e.freigabeUrl);
    const quittung = (m.empfaenger || []).some(e => (e.nachrichten || []).some(n => (n.art || '').indexOf('Quittung') >= 0));
    const dialog = (m.empfaenger || []).some(e => (e.nachrichten || []).some(n => n.dialog && !n.beantwortet));
    const ton = m.status === 'error' ? 'xc-rot' : m.status === 'success' ? 'xc-gruen' : 'xc-gelb';
    const aktionen = [
        frei ? `<button type="button" class="xc-go" data-xc="freigabe" data-url="${xcEsc(frei.freigabeUrl)}">Freigabe starten</button>` : '',
        (dialog || m.status === 'pending') ? `<button type="button" class="xc-ghost" data-xc="dialog" data-id="${xcEsc(m.id)}">DialogMessage abholen</button>` : '',
        quittung ? `<button type="button" class="xc-ghost" data-xc="quittung" data-id="${xcEsc(m.id)}">Quittung anzeigen</button>` : '',
        m.ersetzbar ? `<button type="button" class="xc-ghost" data-xc="ersatz" data-id="${xcEsc(m.id)}">Ersatzmeldung</button>` : '',
    ].filter(Boolean).join('');
    const schein = xc.quittung === m.id
        ? (m.empfaenger || []).flatMap(e => (e.nachrichten || []).filter(n => (n.art || '').indexOf('Quittung') >= 0).map(n => `<pre class="xc-pre">${xcEsc(n.xml || n.art || '')}</pre>`)).join('')
        : '';
    return `<article class="xc-status">
        <div class="xc-wort ${ton}">${xcEsc((m.statusText || 'In Bearbeitung').toUpperCase())}</div>
        <div class="xc-meta">${xcEsc(xcDatum(m.gesendet) || '—')} · ${xcEsc(m.declarationId || 'DeclarationID noch nicht vergeben')}</div>
        <div class="xc-kv">
            <span>Domäne</span><b>${xcEsc(domains.join(', ') || m.titel || '—')}</b>
            <span>Empfänger</span><b>${xcEsc(empf.join(', ') || '—')}</b>
            <span>Benachrichtigungen</span><b>${xcEsc(hin.length ? (hin.length === 1 ? hin[0] : hin.length + ' Hinweise') : 'keine')}</b>
        </div>
        ${xcDialogHtml(m)}
        ${schein}
        ${aktionen ? `<div class="xc-aktionen">${aktionen}</div>` : ''}
    </article>`;
}

function xcDialogHtml(m) {
    let html = '';
    for (const e of m.empfaenger || []) {
        for (const n of e.nachrichten || []) {
            if (!n.dialog || !n.dialog.beantwortbar || n.beantwortet) continue;
            const fragen = (n.dialog.absaetze || []).filter(p => p.istFrage);
            html += `<div class="xc-dialog"><div class="xc-an">${xcEsc(e.name)}</div><b>${xcEsc(n.dialog.titel || 'Rückfrage')}</b>`;
            if (n.dialog.beschreibung) html += `<p>${xcEsc(n.dialog.beschreibung)}</p>`;
            for (const p of fragen) {
                const hinweis = { Boolean: 'ja oder nein', YesNoUnknown: 'ja, nein oder unbekannt', Date: 'TT.MM.JJJJ', DateTime: 'TT.MM.JJJJ HH:MM', Amount: 'Betrag', Integer: 'ganze Zahl', Double: 'Zahl' }[p.frageTyp] || '';
                html += `<label class="xc-frage">${xcEsc(p.label)}${p.optional ? '' : ' *'}${hinweis ? ` <i>${xcEsc(hinweis)}</i>` : ''}
                    <input data-frage="${xcEsc(p.id)}" value="${xcEsc(p.default || p.antwort || '')}"></label>`;
            }
            html += `<button type="button" class="xc-go" data-xc="antwort" data-id="${xcEsc(m.id)}" data-emp="${xcEsc(e.addresseeId)}" data-story="${xcEsc(n.storyId)}">Antwort senden</button></div>`;
        }
    }
    return html;
}

function xcKonfigHtml() {
    const v = xc.cert?.vorlage || {};
    const sua = xc.cert?.sua;
    const da = !!(sua?.vorhanden && !(sua.restTage != null && sua.restTage < 0));
    const firma = `<div class="xc-kv">
        <span>Firma</span><b>${xcEsc(v.firma || '—')}</b>
        <span>UID</span><b>${xcEsc(v.uid || '—')}</b>
        <span>Kontakt</span><b>${xcEsc(v.kontakt || '—')}</b>
        <span>PLZ / Ort</span><b>${xcEsc([v.plz, v.ort].filter(Boolean).join(' ') || '—')}</b>
    </div>
    <p class="xc-hin">Diese Angaben werden mit den offiziellen Registern abgeglichen. Abweichungen werden zurückgewiesen. Stimmt etwas nicht, zuerst den Hauptsitz anpassen.</p>`;
    const bestellen = da ? '<p class="xc-gruen">Zertifikat gültig.</p>' : `<div class="xc-aktionen">
        <button type="button" class="xc-go" data-xc="zert-bestellen">Zertifikat bestellen</button>
    </div>
    <label class="xc-frage">Registrierungspasswort aus dem Brief
        <input id="xcBrief" autocomplete="one-time-code" style="width:16rem">
    </label>
    <button type="button" class="xc-ghost" data-xc="zert-hinterlegen">Zertifikat hinterlegen</button>`;
    const zeilen = xcAnmeldeZeilen().map(r => {
        const z = r.zeile;
        const anm = z && (xc.stand?.anmeldungen || []).find(m => (m.empfaenger || []).some(e => e.domain === z.domain && (e.name || '').includes(z.an)));
        const empf = anm?.empfaenger?.find(e => e.domain === z.domain);
        let aktion = '<span class="xc-an">Anmeldung, sobald das Zertifikat hinterlegt ist</span>';
        if (da && z) {
            aktion = anm
                ? `<span class="xc-gruen">Angemeldet</span> <button type="button" class="xc-ghost xc-klein" data-xc="abmelden" data-id="${xcEsc(anm.id)}" data-emp="${xcEsc(empf?.addresseeId || '')}">Abmelden</button>`
                : `<button type="button" class="xc-go" data-xc="anmelden" data-domain="${xcEsc(z.domain)}" data-versicherer="${xcEsc(z.an)}" data-kunde="${xcEsc(z.kundennummer || '')}" data-vertrag="${xcEsc(z.vertragsnummer || '')}" data-kennung="${xcEsc(z.kennung || '')}">Anmelden</button>`;
            if (anm && r.profil) aktion += ' <button type="button" class="xc-ghost xc-klein" data-xc="profil">Versicherungsprofil bestellen</button>';
        }
        return `<div class="xc-row xc-anm"><span class="xc-name">${xcEsc(r.name)}</span><span class="xc-an">${xcEsc(z ? xcAnText(z) : 'noch keiner zugeordnet')}</span><span class="xc-aktionen">${aktion}</span></div>`;
    }).join('');
    return `<div class="xc-flaeche">
        <div>
            <div class="xc-gruppe">Zertifikat</div>
            <p class="xc-an">Für Prozesse, die Versicherungen oder Behörden auslösen, braucht das Unternehmen ein Zertifikat. Es wird einmal bestellt und gilt für sämtliche Swissdec-Standards.</p>
            ${firma}
            ${bestellen}
            <p class="xc-hin">${xcEsc(xc.konfigHinweis || xc.cert?.registrierung?.text || '')}</p>
        </div>
        <div>
            <div class="xc-gruppe">Anmeldung</div>
            <p class="xc-an">Pro Empfänger eine Anmeldung. Das Versicherungsprofil von UVG, UVGZ und KTG kommt danach als Aufgabe.</p>
            ${zeilen}
            ${xcAufgabenHtml()}
        </div>
    </div>`;
}

function xcAnmeldeZeilen() {
    const fest = [
        { name: 'KTG', domain: 'KTG-AMC', prefix: 'ktg', profil: true },
        { name: 'UVG', domain: 'UVG-LAA', prefix: 'uvg', profil: true },
        { name: 'UVGZ', domain: 'UVGZ-LAAC', prefix: 'uvgz', profil: true },
        { name: 'BVG', domain: 'BVG-LPP', prefix: 'bvg', profil: false },
    ];
    const rows = [];
    for (const f of fest) {
        const treffer = xcJahrZeilen().filter(z => {
            const id = String(z.id || '');
            return (id === f.prefix || id.startsWith(f.prefix + '-')) && z.an && z.an !== 'noch keiner zugeordnet' && z.domain;
        });
        if (!treffer.length) rows.push(Object.assign({}, f, { zeile: null }));
        else treffer.forEach(z => rows.push(Object.assign({}, f, { zeile: z })));
    }
    return rows;
}

function xcAufgabenHtml() {
    const list = xcAufgaben();
    const leer = xc.sicht === 'konfig' ? 'Nichts liegt an.' : 'Nichts liegt an.';
    const seen = [];
    for (const a of list) {
        const g = seen.find(x => x.titel === a.titel);
        if (g) g.n += 1;
        else seen.push(Object.assign({ n: 1 }, a));
    }
    const rows = seen.map(a => {
        const mehr = a.n > 1;
        const text = mehr ? `${a.n} Meldungen` : (a.text || '');
        const aktion = mehr ? 'aufgaben' : a.aktion;
        const label = mehr ? 'Anzeigen' : a.label;
        return `<div class="xc-auf ${a.ton}">
        <div><b>${xcEsc(a.titel)}</b><p>${xcEsc(text)}</p></div>
        <button type="button" class="xc-ghost xc-klein" data-xc="${xcEsc(aktion)}" ${!mehr && a.id ? `data-id="${xcEsc(a.id)}"` : ''} ${!mehr && a.url ? `data-url="${xcEsc(a.url)}"` : ''}>${xcEsc(label)}</button>
    </div>`;
    }).join('');
    return `<div class="xc-gruppe">Offen</div>${rows || `<p class="xc-an">${leer}</p>`}`;
}

function xcSheetHtml() {
    if (xc.sheet === 'aufgaben') {
        const list = xcAufgaben();
        const rows = list.length ? list.map(a => `<div class="xc-auf ${a.ton}">
            <div><b>${xcEsc(a.titel)}</b><p>${xcEsc(a.text || '')}</p></div>
            <button type="button" class="xc-ghost xc-klein" data-xc="${xcEsc(a.aktion)}" ${a.id ? `data-id="${xcEsc(a.id)}"` : ''} ${a.url ? `data-url="${xcEsc(a.url)}"` : ''}>${xcEsc(a.label)}</button>
        </div>`).join('') : '<p class="xc-an">Keine offenen Aufgaben.</p>';
        return `<div class="xc-sheet" data-xc="schliessen"><div class="xc-panel" data-xc="halt">
            <div class="xc-kicker">Aufgaben</div><h2>Offen</h2>${rows}
            <button type="button" class="xc-text" data-xc="schliessen">Schliessen</button>
        </div></div>`;
    }
    if (xc.sheet === 'bvg') return xcBvgHtml();
    return '';
}

function xcBvgHtml() {
    const j = xc.bvg;
    const stand = !j ? '<p class="xc-an">Lohnsummen werden gerechnet.</p>'
        : (j.zeilen || []).map(z => `<div class="xc-row xc-bvg">
            <span class="xc-name">${xcEsc(z.name)}</span>
            <span class="xc-an">${xcEsc(xcGeld(z.vorschlag))}</span>
            <input class="xc-geld" data-bvg="${z.employeeId}" value="${xcEsc(Number(z.betrag).toFixed(2))}" inputmode="decimal" aria-label="Lohnsumme ${xcEsc(z.name)}">
        </div>`).join('') || '<p class="xc-an">Keine Lohnsummen in diesem Jahr.</p>';
    const frei = j?.freigegeben ? `<p class="xc-gruen">Freigegeben ${xcEsc(xcDatum(j.freigegebenAm))}</p>` : '';
    return `<div class="xc-sheet" data-xc="schliessen"><div class="xc-panel" data-xc="halt">
        <div class="xc-kicker">Vorsorge ${xc.jahr}</div>
        <h2>Lohnsummen</h2>
        <p class="xc-an">Vorschlag aus den Lohnläufen. Der Betrag lässt sich ändern. Die Jahresmeldung an die Vorsorge geht erst, wenn die Lohndaten in der Datei stehen.</p>
        ${frei}${stand}
        <div class="xc-aktionen">
            <button type="button" class="xc-ghost" data-xc="bvg-frei" data-wert="0" ${j ? '' : 'disabled'}>Speichern</button>
            <button type="button" class="xc-go" data-xc="bvg-frei" data-wert="1" ${j ? '' : 'disabled'}>Zur Übermittlung freigeben</button>
        </div>
        <button type="button" class="xc-text" data-xc="schliessen">Schliessen</button>
    </div></div>`;
}

function xcKlick(ev) {
    const el = ev.target.closest('[data-xc]');
    if (!el || el.dataset.xc === 'halt') return;
    const a = el.dataset.xc;
    if (xc.busy && a !== 'schliessen') return;
    if (a === 'art') xcArt(el.dataset.wert);
    else if (a === 'jahr') xcSchrittJahr(parseInt(el.dataset.wert, 10));
    else if (a === 'monat') xcSchrittMonat(parseInt(el.dataset.wert, 10));
    else if (a === 'pruefen') xcPruefen();
    else if (a === 'senden') xcSenden();
    else if (a === 'aktualisieren') xcAktualisieren();
    else if (a === 'konfig') { xc.sicht = 'konfig'; xc.sheet = null; xc.force = true; xcZeichnen(); }
    else if (a === 'zurueck') { xc.sicht = 'senden'; xc.force = true; xcZeichnen(); }
    else if (a === 'aufgaben') { xc.sheet = 'aufgaben'; xc.force = true; xcZeichnen(); }
    else if (a === 'schliessen') { xc.sheet = null; xc.force = true; xcZeichnen(); }
    else if (a === 'historie') { xc.historie = !xc.historie; xc.force = true; xcZeichnen(); }
    else if (a === 'fokus') { xc.fokus = el.dataset.id; xcArtPassend(el.dataset.id); xc.sheet = null; xc.force = true; xcZeichnen(); }
    else if (a === 'ersatz') xcErsatz(el.dataset.id);
    else if (a === 'ersatz-aus') { xc.ersatz = null; xcEntschaerfen(); xc.force = true; xcZeichnen(); }
    else if (a === 'freigabe' && el.dataset.url) window.open(el.dataset.url, '_blank', 'noopener');
    else if (a === 'dialog') xcDialogHolen(el.dataset.id);
    else if (a === 'quittung') { xc.quittung = xc.quittung === el.dataset.id ? null : el.dataset.id; xc.fokus = el.dataset.id; xc.force = true; xcZeichnen(); }
    else if (a === 'antwort') xcAntworten(el);
    else if (a === 'bvg') xcBvgOeffnen();
    else if (a === 'bvg-frei') xcBvgFrei(el.dataset.wert === '1');
    else if (a === 'zert-bestellen') xcZertBestellen();
    else if (a === 'zert-hinterlegen') xcZertHinterlegen();
    else if (a === 'anmelden') xcAnmelden(el);
    else if (a === 'abmelden') xcAbmelden(el);
    else if (a === 'profil') { xc.konfigHinweis = 'Eine eigene Bestellung gibt es nicht. Nach der Anmeldung schickt der Versicherer das Profil. Es erscheint unter Aufgaben.'; xc.force = true; xcZeichnen(); }
}

function xcChange(ev) {
    const el = ev.target.closest('[data-xc]');
    if (!el || xc.busy) return;
    const a = el.dataset.xc;
    if (a === 'wahl') {
        if (el.checked) xc.wahl.add(el.dataset.id); else xc.wahl.delete(el.dataset.id);
        xcEntschaerfen();
    } else if (a === 'portal') {
        xc.portal = el.checked;
        xcEntschaerfen();
    } else if (a === 'test') {
        xc.test = el.checked;
    } else if (a === 'kanton') {
        if (!xc.kantone) xc.kantone = new Set();
        if (el.checked) xc.kantone.add(el.dataset.id); else xc.kantone.delete(el.dataset.id);
        xc.force = true;
        xcZeichnen();
        return;
    } else return;
    xc.force = true;
    xcZeichnen();
}

function xcArt(name) {
    if (name !== 'jahr' && name !== 'monat') return;
    if (xc.art === name) return;
    xc.art = name;
    xc.fokus = null;
    xc.gerade = null;
    xc.sicht = 'senden';
    xcEntschaerfen();
    xc.force = true;
    xcZeichnen();
}

async function xcSchrittJahr(delta) {
    const n = xc.jahr + delta;
    if (n < 2015 || n > 2035) return;
    xc.jahr = n;
    xcEntschaerfen();
    const dom = await xcGet(xcDomaenenUrl());
    if (dom) xc.domaenen = dom;
    xc.force = true;
    xcZeichnen();
}

function xcSchrittMonat(delta) {
    const vorher = xc.jahr;
    let m = xc.monat + delta;
    if (m < 1) { m = 12; xc.jahr -= 1; }
    if (m > 12) { m = 1; xc.jahr += 1; }
    xc.monat = m;
    xcEntschaerfen();
    if (xc.jahr !== vorher) xcSchrittJahr(0);
    else { xc.force = true; xcZeichnen(); }
}

function xcArtPassend(id) {
    const m = (xc.stand?.meldungen || []).find(x => x.id === id);
    if (!m) return;
    const neu = m.art === 'monthly' ? 'monat' : 'jahr';
    if (neu !== xc.art) { xc.art = neu; xcEntschaerfen(); xc.fokus = id; }
}

async function xcPruefen() {
    if (xc.busy) return;
    xc.checks = [];
    xc.kantone = null;
    xc.hinweis = '';
    const arts = xc.art === 'monat' ? (xc.wahl.has('qst') || xc.wahl.has('statistik') ? ['monthly'] : []) : xcArts();
    const offen = xc.art === 'jahr' ? xcOffenOhneDatei() : [];
    if (!arts.length) {
        xc.hinweis = offen.length
            ? 'Diese Lohndaten stehen noch nicht in der Datei.'
            : (xc.art === 'monat' ? 'Quellensteuer oder Statistik wählen.' : 'Eine Meldung wählen.');
        xc.arm = null;
        xc.force = true;
        xcZeichnen();
        return;
    }
    xc.busy = true;
    xc.lauf = 'PRÜFUNG';
    xc.force = true;
    xcZeichnen();
    const checks = [];
    let ok = true;
    for (const art of arts) {
        const q = new URLSearchParams({ art, jahr: String(xc.jahr) });
        if (art === 'monthly') q.set('monat', String(xc.monat));
        let j = {};
        let gut = false;
        try {
            const r = await fetch('/api/elm/meldungen/vorschau?' + q, { headers: ah() });
            j = await r.json().catch(() => ({}));
            gut = r.ok;
            if (!gut) j = { fehler: [j.message || 'Prüfung fehlgeschlagen'], technisch: [], empfaenger: [] };
        } catch {
            j = { fehler: ['Server nicht erreichbar.'], technisch: [], empfaenger: [] };
        }
        checks.push({ art, v: j });
        if (!gut || (j.fehler || []).length || (j.technisch || []).length || j.sendenGesperrt || !(j.empfaenger || []).length) ok = false;
        if (!gut) break;
    }
    xc.checks = checks;
    if (xc.art === 'monat' && checks[0]) {
        xc.kantone = new Set();
        for (const e of checks[0].v.empfaenger || []) {
            if (xcIstStatistik(e) ? xc.wahl.has('statistik') : (xc.wahl.has('qst') && xcIstKantonDerFiliale(e)))
                xc.kantone.add(e.addresseeId);
        }
        if (!xc.kantone.size) ok = false;
    }
    xc.arm = ok ? xcKern() : null;
    if (offen.length && ok) xc.hinweis = offen.map(z => z.name).filter((n, i, a) => a.indexOf(n) === i).join(', ') + ' geht nicht mit. Die Lohndaten fehlen in der Datei.';
    xc.busy = false;
    xc.lauf = '';
    xc.force = true;
    xcZeichnen();
}

async function xcSenden() {
    if (!xcBereit() || xc.busy) return;
    xc.busy = true;
    xc.lauf = 'ÜBERMITTLUNG';
    xc.force = true;
    xcZeichnen();
    let hinweis = '';
    let gerade = null;
    for (const c of xc.checks) {
        const art = c.art;
        const adressaten = art === 'monthly' && xc.kantone
            ? Object.fromEntries((c.v.empfaenger || []).map(e => [e.addresseeId, xc.kantone.has(e.addresseeId)]))
            : null;
        const body = {
            art, jahr: xc.jahr, monat: art === 'monthly' ? xc.monat : null,
            ersetztId: xc.ersatz && xc.ersatz.art === art ? xc.ersatz.id : null,
            ziel: null,
            testmeldung: xc.test,
            freigabePortal: art !== 'monthly' && xc.portal,
            adressaten,
        };
        try {
            const r = await fetch('/api/elm/meldungen/senden', { method: 'POST', headers: ah(), body: JSON.stringify(body) });
            const j = await r.json().catch(() => ({}));
            if (j.stand) xc.stand = j.stand;
            if (!r.ok || !j.ok) { hinweis = j.meldung || j.message || 'Nicht gesendet.'; break; }
            gerade = j.vorgangId || gerade;
        } catch { hinweis = 'Server nicht erreichbar.'; break; }
    }
    xc.busy = false;
    xc.lauf = '';
    xc.hinweis = hinweis;
    if (gerade) {
        xc.gerade = gerade;
        xc.fokus = gerade;
        xc.ersatz = null;
        xc.arm = null;
        xc.checks = [];
        xc.kantone = null;
    }
    xc.force = true;
    xcZeichnen();
}

async function xcAktualisieren() {
    if (xc.busy) return;
    const offen = (xc.stand?.meldungen || []).filter(m => !m.fertig && !m.zertifizierung);
    xc.busy = true;
    xc.lauf = 'STAND';
    xc.force = true;
    xcZeichnen();
    try {
        if (!offen.length) {
            const s = await xcGet('/api/elm/meldungen');
            if (s) xc.stand = s;
        } else {
            for (const m of offen) {
                const r = await fetch('/api/elm/meldungen/' + encodeURIComponent(m.id) + '/abfragen', { method: 'POST', headers: ah() });
                const j = await r.json().catch(() => ({}));
                if (r.ok && j.stand) xc.stand = j.stand;
            }
        }
        try {
            const r = await fetch('/api/elm/zertifikat/status', { method: 'POST', headers: ah() });
            const j = await r.json().catch(() => ({}));
            if (j.stand) xc.cert = j.stand;
        } catch { /* Zertifikat bleibt beim letzten Stand */ }
    } finally {
        xc.busy = false;
        xc.lauf = '';
        xc.force = true;
        xcZeichnen();
    }
}

async function xcDialogHolen(id) {
    xc.fokus = id;
    xc.busy = true;
    xc.lauf = 'NACHRICHT';
    xc.force = true;
    xcZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/' + encodeURIComponent(id) + '/abfragen', { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (j.stand) xc.stand = j.stand;
        if (!r.ok) xc.hinweis = j.message || 'Nachricht nicht geholt.';
    } catch { xc.hinweis = 'Server nicht erreichbar.'; }
    xc.busy = false;
    xc.lauf = '';
    xc.force = true;
    xcZeichnen();
}

function xcErsatz(id) {
    const m = (xc.stand?.meldungen || []).find(x => x.id === id);
    if (!m) return;
    xc.ruhe = true;
    xc.ersatz = { id: m.id, art: m.art, titel: m.titel, gesendet: m.gesendet };
    xc.art = m.art === 'monthly' ? 'monat' : 'jahr';
    if (m.jahr) xc.jahr = m.jahr;
    if (m.monat) xc.monat = m.monat;
    xc.wahl.clear();
    if (m.art === 'monthly') {
        const hatStat = (m.empfaenger || []).some(e => e.domain === 'Statistic');
        const hatQst = (m.empfaenger || []).some(e => e.domain !== 'Statistic');
        if (hatQst || !hatStat) xc.wahl.add('qst');
        if (hatStat) xc.wahl.add('statistik');
    } else {
        for (const z of xcJahrZeilen()) if (z.art === m.art) xc.wahl.add(z.id);
    }
    xc.fokus = m.id;
    xc.sicht = 'senden';
    xc.sheet = null;
    xcEntschaerfen();
    xc.ruhe = false;
    xc.ersatz = { id: m.id, art: m.art, titel: m.titel, gesendet: m.gesendet };
    xc.force = true;
    xcZeichnen();
}

async function xcAntworten(el) {
    const m = (xc.stand?.meldungen || []).find(x => x.id === el.dataset.id);
    const e = m?.empfaenger?.find(x => x.addresseeId === el.dataset.emp);
    const n = e?.nachrichten?.find(x => x.storyId === el.dataset.story);
    if (!n?.dialog) return;
    const box = el.closest('.xc-dialog');
    const werte = {};
    const fehlt = [];
    for (const p of n.dialog.absaetze.filter(x => x.istFrage)) {
        const f = box?.querySelector(`[data-frage="${CSS.escape(String(p.id))}"]`);
        const wert = f ? f.value.trim() : '';
        if (!wert && !p.optional) fehlt.push(p.label || 'Feld');
        werte[p.id] = wert || null;
    }
    if (fehlt.length) { xc.hinweis = 'Bitte ausfüllen: ' + fehlt.join(', '); xc.force = true; xcZeichnen(); return; }
    xc.busy = true;
    xc.lauf = 'ANTWORT';
    xc.force = true;
    xcZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/' + encodeURIComponent(m.id) + '/antworten', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({ addresseeId: e.addresseeId, storyId: n.storyId, werte }),
        });
        const j = await r.json().catch(() => ({}));
        if (j.stand) xc.stand = j.stand;
        xc.hinweis = j.meldung || j.message || '';
    } catch { xc.hinweis = 'Server nicht erreichbar.'; }
    xc.busy = false;
    xc.lauf = '';
    xc.force = true;
    xcZeichnen();
}

async function xcBvgOeffnen() {
    xc.sheet = 'bvg';
    xc.bvg = null;
    xc.force = true;
    xcZeichnen();
    const j = await xcGet('/api/elm/meldungen/bvg-lohnsummen?jahr=' + xc.jahr);
    xc.bvg = j || { zeilen: [], freigegeben: false };
    if (j) xc.bvgStand = { jahr: xc.jahr, freigegeben: !!j.freigegeben };
    xc.force = true;
    xcZeichnen();
}

async function xcBvgFrei(frei) {
    const zeilen = [...document.querySelectorAll('#xcApp [data-bvg]')].map(el => ({
        employeeId: parseInt(el.dataset.bvg, 10),
        betrag: xcZahl(el.value),
    }));
    if (zeilen.some(z => z.betrag == null)) { xc.konfigHinweis = ''; xc.hinweis = 'Jede Lohnsumme braucht einen Betrag.'; xc.force = true; xcZeichnen(); return; }
    try {
        const r = await fetch('/api/elm/meldungen/bvg-lohnsummen', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({ jahr: xc.jahr, freigegeben: frei, zeilen }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { xc.hinweis = j.message || 'Nicht gespeichert.'; }
        else {
            xc.bvgStand = { jahr: xc.jahr, freigegeben: !!j.freigegeben };
            if (xc.bvg) {
                xc.bvg.freigegeben = !!j.freigegeben;
                for (const z of xc.bvg.zeilen || []) {
                    const neu = zeilen.find(x => x.employeeId === z.employeeId);
                    if (neu) z.betrag = neu.betrag;
                }
            }
            xc.hinweis = frei ? 'Lohnsummen zur Übermittlung freigegeben.' : 'Lohnsummen gespeichert.';
            if (frei) xc.sheet = null;
        }
    } catch { xc.hinweis = 'Server nicht erreichbar.'; }
    xc.force = true;
    xcZeichnen();
}

async function xcZertBestellen() {
    if (!(await liquidConfirm('OneCrew bestellt das Unternehmenszertifikat bei Swissdec. Es gilt für alle Standards.', {
        title: 'Zertifikat bestellen', yesLabel: 'Bestellen', noLabel: 'Abbrechen' }))) return;
    const v = xc.cert?.vorlage || {};
    xc.konfigHinweis = 'Wird bestellt …';
    xc.force = true;
    xcZeichnen();
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
        xc.konfigHinweis = j.meldung || j.message || 'Nicht erledigt.';
        if (j.stand) xc.cert = j.stand;
    } catch { xc.konfigHinweis = 'Server nicht erreichbar.'; }
    xc.force = true;
    xcZeichnen();
}

async function xcZertHinterlegen() {
    const passwort = document.getElementById('xcBrief')?.value?.trim();
    if (!passwort) { xc.konfigHinweis = 'Bitte das Passwort aus dem Brief eingeben.'; xc.force = true; xcZeichnen(); return; }
    try {
        const r = await fetch('/api/elm/zertifikat/abschliessen', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({ passwort }),
        });
        const j = await r.json().catch(() => ({}));
        if (j.stand) xc.cert = j.stand;
        xc.konfigHinweis = j.meldung || j.message || '';
    } catch { xc.konfigHinweis = 'Server nicht erreichbar.'; }
    xc.force = true;
    xcZeichnen();
}

async function xcAnmelden(el) {
    const d = el.dataset;
    if (!d.versicherer || !d.kunde || !d.vertrag) {
        xc.konfigHinweis = 'Name, Kundennummer und Vertragsnummer des Empfängers müssen im Katalog stehen.';
        xc.force = true;
        xcZeichnen();
        return;
    }
    xc.busy = true;
    xc.lauf = 'ANMELDUNG';
    xc.force = true;
    xcZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/anmelden', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({
                domain: d.domain, versicherer: d.versicherer, kundennummer: d.kunde,
                vertragsnummer: d.vertrag, kennung: d.kennung,
            }),
        });
        const j = await r.json().catch(() => ({}));
        if (j.stand) xc.stand = j.stand;
        xc.konfigHinweis = j.meldung || j.message || '';
    } catch { xc.konfigHinweis = 'Server nicht erreichbar.'; }
    xc.busy = false;
    xc.lauf = '';
    xc.force = true;
    xcZeichnen();
}

async function xcAbmelden(el) {
    if (!el.dataset.emp) return;
    if (!(await liquidConfirm('Die Anmeldung bei diesem Empfänger wird aufgehoben.', {
        title: 'Abmelden', yesLabel: 'Abmelden', noLabel: 'Abbrechen' }))) return;
    xc.busy = true;
    xc.lauf = 'ABMELDUNG';
    xc.force = true;
    xcZeichnen();
    try {
        const r = await fetch('/api/elm/meldungen/anmeldungen/' + encodeURIComponent(el.dataset.id) + '/abmelden', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({ addresseeId: el.dataset.emp, storyId: '' }),
        });
        const j = await r.json().catch(() => ({}));
        if (j.stand) xc.stand = j.stand;
        xc.konfigHinweis = j.meldung || j.message || '';
    } catch { xc.konfigHinweis = 'Server nicht erreichbar.'; }
    xc.busy = false;
    xc.lauf = '';
    xc.force = true;
    xcZeichnen();
}
