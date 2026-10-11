// Anmeldung Familienzulagen → GastroSocial-Portal (akisnet.ch) per Lesezeichen
// (Walter 11.10.2026, «Variante A»).
//
// Der Browser lässt OneCrew nicht in eine fremde Seite schreiben. Deshalb:
//   1. OneCrew legt die Formulardaten als JSON in die Zwischenablage
//      («Für GastroSocial kopieren»), inkl. Anzeige-Kern («kern»).
//   2. Das Lesezeichen «OneCrew einfüllen» (famzGsLader) läuft im Portal, liest die
//      Zwischenablage und zeigt ein Fenster mit allen Werten pro Schritt in
//      Portal-Reihenfolge — 📋 kopiert einen Wert, im Portal mit ⌘V einfügen.
//   Bewusst KEIN automatisches Einfüllen mehr (Walter 11.10.2026): das Portal baut
//   die Seite nach PLZ, Personensuche usw. neu auf und verwirft gesetzte Werte.

const FAMZ_GS_KANTONE = {
    AG: 'Aargau', AI: 'Appenzell Innerrhoden', AR: 'Appenzell Ausserrhoden', BE: 'Bern', BL: 'Basel-Landschaft',
    BS: 'Basel-Stadt', FR: 'Freiburg', GE: 'Genf', GL: 'Glarus', GR: 'Graubünden', JU: 'Jura', LU: 'Luzern',
    NE: 'Neuenburg', NW: 'Nidwalden', OW: 'Obwalden', SG: 'St. Gallen', SH: 'Schaffhausen', SO: 'Solothurn',
    SZ: 'Schwyz', TG: 'Thurgau', TI: 'Tessin', UR: 'Uri', VD: 'Waadt', VS: 'Wallis', ZG: 'Zug', ZH: 'Zürich',
};

/**
 * Läuft im GastroSocial-Portal — darf nichts aus OneCrew verwenden. Kommt bei jedem
 * «Für GastroSocial kopieren» frisch mit (Feld «kern»); die im Lesezeichen eingebaute
 * Kopie ist nur die Rückfallebene. So wirken Verbesserungen ohne neues Lesezeichen.
 */
function famzGsKern(daten) {
        var VERSION = 14;
        var ID = 'onecrew-famz-panel';
        var alt = document.getElementById(ID);
        if (alt) alt.remove();
        var esc = function (t) { return String(t == null ? '' : t).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;'); };
        var p = document.createElement('div');
        p.id = ID;
        p.style.cssText = 'position:fixed;right:18px;bottom:18px;z-index:2147483647;width:360px;max-height:82vh;overflow:auto;'
            + 'background:linear-gradient(165deg,#eeece4,#dfdcd1);border:1px solid rgba(255,255,255,.7);border-radius:16px;'
            + 'box-shadow:0 18px 50px rgba(60,55,48,.28);padding:14px 16px;font:13px/1.45 -apple-system,Helvetica,Arial,sans-serif;color:#3f3f3f';
        var knopf = 'border:none;border-radius:10px;padding:6px 10px;margin:3px 4px 3px 0;cursor:pointer;font:600 12px -apple-system,Helvetica,Arial,sans-serif;';
        var hell = knopf + 'background:rgba(255,255,255,.6);color:#3f3f3f;border:1px solid rgba(60,55,48,.2);';
        var dunkel = knopf + 'background:#3f3f3f;color:#fff;';
        var abschnitte = daten.abschnitte || [];
        var aktiv = 0;

        var werteVon = function (feld) { return [].concat(feld.w).filter(function (x) { return x != null && String(x).trim() !== ''; }); };
        var wertText = function (feld) {
            if (feld.zeige != null) return String(feld.zeige);
            var werte = werteVon(feld);
            if (!werte.length) return '';
            if (feld.art === 'option') return werte[0] === true || /^(ja|x|true)$/i.test(String(werte[0])) ? 'anklicken' : 'nicht ankreuzen';
            return String(werte[0]);
        };
        // Auswahlknöpfe klickt man an — kopieren bringt dort nichts.
        var nurKlicken = function (feld) {
            return feld.art === 'option' || feld.wahl || /^(ja|nein|männlich|weiblich)$/i.test(wertText(feld));
        };
        var kopieren = function (text, knopfEl) {
            var gut = function () { knopfEl.textContent = '✓'; setTimeout(function () { knopfEl.textContent = '📋'; }, 1200); };
            var notweg = function () {
                var t = document.createElement('textarea');
                t.value = text; t.style.cssText = 'position:fixed;left:-9999px';
                document.body.appendChild(t); t.select();
                try { document.execCommand('copy'); gut(); } catch (e) { /* nichts */ }
                t.remove();
            };
            if (navigator.clipboard) navigator.clipboard.writeText(text).then(gut, notweg); else notweg();
        };
        var zeichne = function () {
            var ab = abschnitte[aktiv] || { felder: [] };
            var tabs = abschnitte.map(function (x, k) {
                return '<button data-tab="' + k + '" style="' + (k === aktiv ? dunkel : hell) + '">' + esc(x.titel) + '</button>';
            }).join('');
            var zeilen = (ab.felder || []).map(function (feld, k) {
                var w = wertText(feld);
                if (!w) return '';
                var name = feld.anzeige || [].concat(feld.l)[0] || '';
                return '<div style="display:flex;align-items:center;gap:6px;padding:4px 0;border-bottom:1px solid rgba(60,55,48,.08)">'
                    + '<div style="flex:1;min-width:0"><div style="font-size:11px;color:#8b8b8b">' + esc(name) + '</div>'
                    + '<div style="font-weight:700;word-break:break-word">' + esc(w) + '</div></div>'
                    + (feld.art === 'option' ? '' : nurKlicken(feld) ? '<span style="font-size:11px;color:#8b8b8b">anklicken</span>'
                        : '<button data-kopie="' + k + '" title="kopieren, dann im Portal ⌘V" style="' + hell + 'margin:0;padding:4px 8px">📋</button>')
                    + '</div>';
            }).join('') || '<div style="color:#8b8b8b">Für diesen Schritt hat OneCrew keine Werte.</div>';
            p.innerHTML = '<div style="font-weight:800;font-size:14px">OneCrew → GastroSocial <span style="font-weight:400;font-size:11px;color:#8b8b8b">v' + VERSION + '</span></div>'
                + '<div style="color:#646464;margin:2px 0 6px">' + esc(daten.titel) + '</div>'
                + '<div>' + tabs + '</div>'
                + '<div style="font-size:11.5px;color:#646464;margin:8px 0 4px">📋 klicken, im Portal ins Feld klicken, ⌘V</div>'
                + zeilen
                + (ab.hinweis ? '<div style="margin-top:6px;font-size:12px;color:#646464">' + esc(ab.hinweis) + '</div>' : '')
                + '<div style="margin-top:8px;border-top:1px solid rgba(60,55,48,.15);padding-top:6px">'
                + '<button id="ocfz-zu" style="' + hell + '">Schliessen</button></div>';
            Array.prototype.forEach.call(p.querySelectorAll('[data-tab]'), function (b) {
                b.onclick = function () { aktiv = +b.getAttribute('data-tab'); zeichne(); };
            });
            Array.prototype.forEach.call(p.querySelectorAll('[data-kopie]'), function (b) {
                b.onclick = function () { kopieren(wertText(ab.felder[+b.getAttribute('data-kopie')]), b); };
            });
            p.querySelector('#ocfz-zu').onclick = function () { p.remove(); };
        };
        document.body.appendChild(p);
        zeichne();
}

/** Lesezeichen: holt die Daten und startet den mitgelieferten Kern (sonst den eingebauten). */
function famzGsLader(KERN) {
    (async function () {
        var lesen = function (txt) {
            try { var d = JSON.parse(txt); if (d && d.onecrew === 'famz-gs') return d; } catch (e) { /* kein JSON */ }
            return null;
        };
        var daten = null;
        try { if (navigator.clipboard) daten = lesen(await navigator.clipboard.readText()); } catch (e) { /* keine Erlaubnis */ }
        if (!daten) daten = window.__onecrewFamz || null;
        if (!daten) daten = lesen(prompt('OneCrew-Daten hier einfügen (Cmd+V).\n\nIn OneCrew vorher «Für GastroSocial kopieren» klicken.') || '');
        if (!daten) { alert('Keine OneCrew-Daten gefunden. In OneCrew zuerst «Für GastroSocial kopieren» klicken.'); return; }
        window.__onecrewFamz = daten;
        if (daten.kern) {
            try { new Function('daten', daten.kern)(daten); return; } catch (e) { /* Seite verbietet das — eingebauter Kern */ }
        }
        KERN(daten);
    })();
}

const famzGsKompakt = fn => fn.toString()
    .split('\n').map(z => z.trim()).filter(z => z && !z.startsWith('//')).join('\n');

/** javascript:-Adresse für das Lesezeichen. */
function famzGsLesezeichenUrl() {
    return 'javascript:' + encodeURIComponent(`(${famzGsKompakt(famzGsLader)})(${famzGsKompakt(famzGsKern)})`);
}

// ── Daten aus der Maske ────────────────────────────────────────────────────

function famzGsNamen(p) {
    if (!p) return { name: '', vorname: '' };
    const nv = (p.nameVorname || '').trim();
    if (p.name && `${p.name} ${p.vorname || ''}`.trim() === nv) return { name: p.name, vorname: p.vorname || '' };
    const i = nv.indexOf(' ');
    return i < 0 ? { name: nv, vorname: '' } : { name: nv.slice(0, i), vorname: nv.slice(i + 1) };
}

function famzGsPlzOrt(s) {
    const m = String(s || '').trim().match(/^(\d{4,5})\s+(.+)$/);
    return m ? { plz: m[1], ort: m[2] } : { plz: '', ort: String(s || '').trim() };
}

const famzGsJn = b => b === true ? 'Ja' : b === false ? 'Nein' : null;
const famzGsGeschlecht = g => g === 'M' ? ['männlich', 'Mann', 'M'] : g === 'F' ? ['weiblich', 'Frau', 'F', 'W'] : null;
const famzGsKanton = k => k ? [FAMZ_GS_KANTONE[k] || k, k] : null;
/** «LU», «lu», «Luzern» → «LU»; Ausland/unbekannt → null. */
function famzGsKantonCode(k) {
    const t = String(k || '').trim();
    if (!t) return null;
    if (FAMZ_GS_KANTONE[t.toUpperCase()]) return t.toUpperCase();
    return Object.keys(FAMZ_GS_KANTONE).find(c => FAMZ_GS_KANTONE[c].toLowerCase() === t.toLowerCase()) || null;
}

/**
 * Ein Personen-Block. Portal-Beschriftungen (Seite «Zusätzliche Familienzulage», 11.10.2026):
 * «Name Ehepartner», «Vorname Ehepartner», «AHV-Nummer», «Geburtsdatum», «Geschlecht»
 * (männlich/weiblich), «Ist der Ehepartner erwerbstätig?». Die spezifische Beschriftung
 * steht vorne — sonst träfe «Name» die Kinder-Tabelle.
 */
function famzGsPerson(p, rolle) {
    if (!p) return [];
    const n = famzGsNamen(p);
    const po = famzGsPlzOrt(p.plzOrt);
    const wer = rolle === 'partner' ? ['Ehepartner', 'Lebenspartner', 'Partner'] : ['anderer Elternteil', 'Elternteil'];
    const mit = (vor, rest = []) => [...wer.map(w => `${vor} ${w}`), ...rest];
    return [
        { l: mit('Name', ['Nachname', 'Familienname']), w: n.name, anzeige: 'Name' },
        { l: mit('Vorname'), w: n.vorname, anzeige: 'Vorname' },
        { l: ['AHV-Nummer', 'Sozialversicherungsnummer', 'AHV-Nr', 'Versichertennummer'], w: p.ahvNummer, anzeige: 'AHV-Nummer' },
        { l: ['Geburtsdatum'], w: p.geburtsdatum },
        { l: ['Geschlecht'], w: famzGsGeschlecht(p.geschlecht) },
        { l: [...wer.map(w => `Ist der ${w} erwerbstätig`), ...wer.map(w => `Ist die ${w}in erwerbstätig`), 'Erwerbstätig'],
          w: famzGsJn(p.angestellt), anzeige: 'Erwerbstätig?' },
        { l: [...wer.map(w => `Verdient der ${w} mehr als das Minimaleinkommen`), ...wer.map(w => `Verdient die ${w}in mehr als das Minimaleinkommen`), 'Verdient'],
          w: famzGsJn(p.einkommenUeberMinimum), anzeige: 'Mehr als Minimaleinkommen?' },
        { l: ['Wer erzielt das höhere Einkommen'], wahl: true, anzeige: 'Höheres Einkommen',
          w: p.einkommenHoeher === true ? wer : p.einkommenHoeher === false ? ['Antragsteller', 'Antragstellerin'] : null },
        { l: ['Selbständig', 'Selbstständig'], w: p.seNe ? (String(p.seNe).toUpperCase() === 'SE' ? 'Ja' : 'Nein') : null,
          anzeige: 'Selbständig?' },
        ...(p.angestellt && famzGsKantonCode(p.arbeitsortKanton) ? [
            { l: ['Arbeitskanton/-Land', 'Arbeitskanton / -Land', 'Arbeitskanton/Land'], w: ['CH', 'Schweiz'], anzeige: 'Arbeitsland' },
            { l: ['Arbeitskanton/-Land', 'Arbeitskanton / -Land', 'Arbeitskanton/Land'], rechts: true,
              w: famzGsKanton(famzGsKantonCode(p.arbeitsortKanton)), anzeige: 'Arbeitskanton' },
        ] : p.angestellt && p.arbeitsortKanton ? [
            { l: ['Arbeitskanton/-Land'], w: null, anzeige: 'Arbeitskanton/-Land (von Hand wählen)', zeige: p.arbeitsortKanton },
        ] : []),
        { l: ['Arbeitgeber', 'Name Arbeitgeber'], w: p.arbeitgeberZeile1, anzeige: 'Arbeitgeber' },
        { l: ['Arbeitspensum', 'Pensum', 'Beschäftigungsgrad'], w: p.arbeitspensum, anzeige: 'Pensum' },
        { l: ['Zivilstand'], w: p.zivilstand ? (FAMZ_ZIVIL[p.zivilstand] || p.zivilstand) : null },
        { l: ['Nationalität', 'Staatsangehörigkeit'], w: p.nationalitaet, anzeige: 'Nationalität' },
    ];
}

/** Kinder-Tabelle im Portal: Name | Vorname | GebDat | m/w | Sorgerecht | Überwiegender Wohnsitz | Beziehung. */
const FAMZ_GS_ELTERN = {
    ELTERN: ['Beide Eltern', 'Beide', 'gemeinsam', 'Eltern'],
    MUTTER: ['Mutter'],
    VATER: ['Vater'],
};
const FAMZ_GS_BEZ = {
    L: ['Leibl. Kind', 'leiblich'], A: ['Adoptivkind', 'Adopt'], S: ['Stiefkind', 'Stief'],
    P: ['Pflegekind', 'Pflege'], G: ['Geschwister'], E: ['Enkel', 'Grosskind'],
};

function famzGsDaten() {
    const f = famzSammeln();
    const a = f.antragsteller || {};
    const n = famzGsNamen(a);
    const po = famzGsPlzOrt(a.plzOrt);
    const ma = (_famzAlleMa || []).find(e => e.id === _famzEmpId) || {};
    const verheiratet = ['verheiratet', 'getrennt', 'eingetragene_partnerschaft'].includes(a.zivilstand);
    const ae = f.andererElternteil;
    const partner = f.partner || (verheiratet ? ae : null);
    // Portal: «Familie im gleichen Haushalt lebend» = MA, anderer Elternteil und Kinder unter einem Dach.
    const gleicheAdresse = ae && (ae.strasseNr || '').trim() === (a.strasseNr || '').trim()
        && (ae.plzOrt || '').trim() === (a.plzOrt || '').trim();
    const situation = !f.lebenBei ? null
        : f.lebenBei === 'ELTERN' && gleicheAdresse ? 'Familie im gleichen Haushalt lebend' : 'Andere Familiensituation';

    const abschnitte = [{
        titel: '1 · Antrag',
        hinweis: 'Person in der Suche oben aus der Trefferliste wählen. Erscheint nach der PLZ «Auswahl Ortschaft»: Ort anklicken, «Ort übernehmen».',
        felder: [
            { l: ['Name, Geburtsdatum, AHV-Nummer'], w: a.ahvNummer, anzeige: 'Suche (AHV-Nummer)', hinweis: 'Person aus der Trefferliste wählen' },
            { l: ['Personalnummer des Bezügers'], w: ma.employeeNumber, anzeige: 'Personalnummer' },
            { l: ['Zulagen beantragt ab'], w: f.abWann },
            { l: ['Strasse'], w: a.strasseNr },
            { l: ['Arbeitskanton'], w: famzGsKanton(f.arbeitsortKanton) },
            { l: ['Beschäftigt seit'], w: f.beschaeftigtVon },
            { l: ['PLZ'], w: po.plz },
            { l: ['Ort'], w: po.ort },
            { l: ['Minimallohn erreicht'], w: famzGsJn(f.einkommenUeberMinimum), anzeige: 'Minimallohn erreicht?' },
            { l: ['Land'], w: ['Schweiz', 'CH'] },
            { l: ['Meldeverfahren'], w: ['Abwicklung durch Arbeitgeber'] },
            { l: ['Es existieren weitere Arbeitsverhältnisse'], w: f.weitereArbeitgeber, art: 'option', anzeige: 'Weitere Arbeitsverhältnisse' },
            { l: ['Zivilstand'], w: a.zivilstand ? (FAMZ_ZIVIL[a.zivilstand] || a.zivilstand) : null },
            { l: situation ? [situation] : [], w: situation ? true : null, art: 'option', anzeige: situation || 'Familiensituation' },
        ],
    }];
    if (partner) abschnitte.push({
        titel: '2 · Ehe-/Lebenspartner' + (partner === ae ? ' (= anderer Elternteil)' : ''),
        felder: famzGsPerson(partner, 'partner'),
    });
    if (ae && partner !== ae) abschnitte.push({ titel: '3 · Anderer Elternteil', felder: famzGsPerson(ae, 'ae') });
    (f.kinder || []).forEach((k, i) => abschnitte.push({
        titel: `4 · Kind ${i + 1}: ${k.vorname || ''}`.trim(),
        hinweis: 'Nur für ein Kind, das noch nicht in der Liste steht. Danach «Kind zur Liste hinzufügen» klicken.',
        felder: [
            { l: ['Name'], w: k.name },
            { l: ['Vorname'], w: k.vorname },
            { l: ['GebDat', 'Geburtsdatum'], w: k.geburtsdatum, anzeige: 'Geburtsdatum' },
            { l: ['m/w', 'Geschlecht'], w: k.geschlecht === 'M' ? ['m', 'männlich'] : k.geschlecht === 'F' ? ['w', 'weiblich'] : null, anzeige: 'm/w' },
            { l: ['Sorgerecht', 'Elterliche Sorge'], w: FAMZ_GS_ELTERN[f.sorge] || null, anzeige: 'Sorgerecht' },
            { l: ['Überwiegender Wohnsitz', 'Wohnsitz'], w: FAMZ_GS_ELTERN[f.lebenBei] || null, anzeige: 'Wohnsitz' },
            { l: ['Beziehung', 'Kindesverhältnis'], w: FAMZ_GS_BEZ[k.beziehung] || null, anzeige: 'Beziehung' },
        ],
    }));
    return {
        onecrew: 'famz-gs',
        v: 2,
        kern: `(${famzGsKompakt(famzGsKern)})(daten);`,
        titel: `${n.vorname} ${n.name}`.trim(),
        abschnitte,
    };
}

async function famzGsKopieren() {
    if (!_famzDaten) return;
    try {
        await navigator.clipboard.writeText(JSON.stringify(famzGsDaten()));
        showToast('Kopiert — jetzt im GastroSocial-Portal das Lesezeichen «OneCrew einfüllen» klicken.', 'success');
    } catch {
        showToast('Zwischenablage nicht erreichbar.', 'error');
    }
}

/** Anleitung + Lesezeichen zum Hineinziehen in die Lesezeichenleiste. */
function famzGsLesezeichenZeigen() {
    document.getElementById('famzGsModal')?.remove();
    const wrap = document.createElement('div');
    wrap.id = 'famzGsModal';
    wrap.style.cssText = 'position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9800;display:flex;align-items:center;justify-content:center';
    wrap.innerHTML = `
        <div class="modal" style="max-width:520px;width:92%;padding:22px 24px;position:relative">
            <div style="font-size:15px;font-weight:800;color:#3f3f3f;margin-bottom:10px">Lesezeichen «OneCrew einfüllen» einrichten</div>
            <ol style="font-size:13.5px;color:#3f3f3f;line-height:1.6;padding-left:18px;margin:0">
                <li>In Chrome die Lesezeichenleiste einblenden (Cmd+Shift+B).</li>
                <li>Diesen Knopf mit der Maus in die Lesezeichenleiste <b>ziehen</b> (nicht klicken):<br>
                    <a id="famzGsLink" href="#" onclick="return false"
                       style="display:inline-block;margin:8px 0;background:#3f3f3f;color:#fff;border-radius:12px;padding:8px 16px;font-weight:700;text-decoration:none;cursor:grab">OneCrew einfüllen</a></li>
                <li>Hier in OneCrew «Für GastroSocial kopieren» klicken.</li>
                <li>Im GastroSocial-Portal auf «Familienzulagen anmelden» gehen und das Lesezeichen klicken.
                    Rechts unten erscheint ein Fenster — den Schritt wählen, der gerade offen ist.</li>
            </ol>
            <div style="font-size:12.5px;color:#646464;margin-top:10px;line-height:1.5">
                Chrome fragt beim ersten Mal, ob die Seite die Zwischenablage lesen darf → «Zulassen».
                Was im Portal schon ausgefüllt ist, wird nie überschrieben. «Weiter» und «Absenden» klickst du selbst.
            </div>
            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:16px">
                <button class="famz-btn-sek" onclick="famzGsCodeKopieren()">Code kopieren (falls Ziehen nicht geht)</button>
                <button class="famz-btn-primaer" onclick="document.getElementById('famzGsModal').remove()">Fertig</button>
            </div>
        </div>`;
    document.body.appendChild(wrap);
    wrap.querySelector('#famzGsLink').setAttribute('href', famzGsLesezeichenUrl());
    wrap.addEventListener('click', e => { if (e.target === wrap) wrap.remove(); });
}

async function famzGsCodeKopieren() {
    try {
        await navigator.clipboard.writeText(famzGsLesezeichenUrl());
        showToast('Code kopiert — neues Lesezeichen anlegen und den Code als Adresse (URL) einfügen.', 'success');
    } catch {
        showToast('Zwischenablage nicht erreichbar.', 'error');
    }
}
