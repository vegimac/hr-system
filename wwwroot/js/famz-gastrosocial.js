// Anmeldung Familienzulagen → GastroSocial-Portal (akisnet.ch) per Lesezeichen
// (Walter 11.10.2026, «Variante A»).
//
// Der Browser lässt OneCrew nicht in eine fremde Seite schreiben. Deshalb:
//   1. OneCrew legt die Formulardaten als JSON in die Zwischenablage
//      («Für GastroSocial kopieren»). Darin steht auch, welche Beschriftung
//      im Portal zu welchem Wert gehört — Anpassungen an der Zuordnung
//      brauchen also nur ein OneCrew-Deploy, kein neues Lesezeichen.
//   2. Das Lesezeichen «OneCrew einfüllen» (famzGsEngine) läuft im Portal,
//      liest die Zwischenablage und füllt die Felder des gerade offenen
//      Schritts. Felder werden über ihre Beschriftung gefunden (Lage: direkt
//      darunter oder rechts daneben). Ausgefüllte Portal-Felder werden nie
//      überschrieben, Abweichungen nur gemeldet.
//   3. «Felder melden» kopiert die Beschriftungen der aktuellen Portal-Seite
//      (ohne Werte) — damit wird die Zuordnung für weitere Schritte ergänzt.

const FAMZ_GS_KANTONE = {
    AG: 'Aargau', AI: 'Appenzell Innerrhoden', AR: 'Appenzell Ausserrhoden', BE: 'Bern', BL: 'Basel-Landschaft',
    BS: 'Basel-Stadt', FR: 'Freiburg', GE: 'Genf', GL: 'Glarus', GR: 'Graubünden', JU: 'Jura', LU: 'Luzern',
    NE: 'Neuenburg', NW: 'Nidwalden', OW: 'Obwalden', SG: 'St. Gallen', SH: 'Schaffhausen', SO: 'Solothurn',
    SZ: 'Schwyz', TG: 'Thurgau', TI: 'Tessin', UR: 'Uri', VD: 'Waadt', VS: 'Wallis', ZG: 'Zug', ZH: 'Zürich',
};

/** Läuft im GastroSocial-Portal (als Lesezeichen) — darf nichts aus OneCrew verwenden. */
function famzGsEngine() {
    (async function () {
        var ID = 'onecrew-famz-panel';
        var norm = function (s) { return String(s == null ? '' : s).replace(/[*?:]/g, ' ').replace(/\s+/g, ' ').trim().toLowerCase(); };
        var sichtbar = function (el) {
            var r = el.getBoundingClientRect();
            return r.width > 0 && r.height > 0 && getComputedStyle(el).visibility !== 'hidden';
        };
        var imPanel = function (el) { return !!(el.closest && el.closest('#' + ID)); };
        var steuerelemente = function () {
            return Array.prototype.filter.call(document.querySelectorAll('input,select,textarea'), function (el) {
                return !/^(hidden|button|submit|reset|image|file)$/i.test(el.type) && sichtbar(el) && !imPanel(el);
            });
        };
        var eigenerText = function (el) {
            var t = '';
            el.childNodes.forEach(function (n) { if (n.nodeType === 3) t += n.textContent; });
            return norm(t);
        };
        var beschriftungen = function () {
            return Array.prototype.filter.call(document.querySelectorAll('label,span,div,td,th,p,legend,b,strong'), function (el) {
                if (imPanel(el) || !sichtbar(el)) return false;
                var t = eigenerText(el);
                return t.length > 0 && t.length < 90;
            });
        };
        // Lage-Abstand Beschriftung → Feld: darunter (linksbündig) oder rechts daneben.
        var abstand = function (lab, c) {
            var r = lab.getBoundingClientRect(), q = c.getBoundingClientRect();
            if (lab.contains(c)) return 1;
            var unten = q.top - r.bottom;
            if (unten >= -6 && unten < 48 && Math.abs(q.left - r.left) < 45) return unten + Math.abs(q.left - r.left);
            var mitte = Math.abs((q.top + q.bottom) / 2 - (r.top + r.bottom) / 2);
            var rechts = q.left - r.right;
            if (mitte < 14 && rechts > -6 && rechts < 320) return 60 + rechts;
            return null;
        };
        var radioText = function (c) {
            if (c.labels && c.labels.length) return norm(c.labels[0].textContent);
            var n = c.nextSibling;
            while (n && n.nodeType === 3 && !n.textContent.trim()) n = n.nextSibling;
            if (n) return norm(n.textContent);
            return norm(c.parentElement ? c.parentElement.textContent : '');
        };
        var findeFeld = function (labels, nr) {
            var alle = beschriftungen();
            for (var i = 0; i < labels.length; i++) {
                var muster = norm(labels[i]);
                var treffer = alle.filter(function (el) { return eigenerText(el) === muster; });
                if (!treffer.length) treffer = alle.filter(function (el) { return eigenerText(el).indexOf(muster) === 0; });
                var lab = treffer[nr || 0];
                if (!lab) continue;
                if (lab.htmlFor) {
                    var f = document.getElementById(lab.htmlFor);
                    if (f && /^(INPUT|SELECT|TEXTAREA)$/.test(f.tagName) && f.type !== 'radio') return { lab: lab, el: f };
                }
                var best = null, bestA = 1e9;
                steuerelemente().forEach(function (c) {
                    var a = abstand(lab, c);
                    if (a != null && a < bestA) { bestA = a; best = c; }
                });
                if (!best) return { lab: lab, el: null };
                if (best.type === 'radio') {
                    var gruppe = steuerelemente().filter(function (c) {
                        return c.type === 'radio' && (best.name ? c.name === best.name : abstand(lab, c) != null);
                    });
                    return { lab: lab, el: best, radios: gruppe };
                }
                return { lab: lab, el: best };
            }
            return null;
        };
        var setzeWert = function (el, w) {
            var proto = el.tagName === 'SELECT' ? HTMLSelectElement.prototype
                : el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
            el.focus();
            Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, w);
            ['input', 'change', 'keyup'].forEach(function (t) { el.dispatchEvent(new Event(t, { bubbles: true })); });
            el.blur();
        };
        var fuelle = function (feld) {
            var werte = [].concat(feld.w).filter(function (x) { return x != null && String(x).trim() !== ''; });
            var name = feld.anzeige || [].concat(feld.l)[0];
            if (!werte.length) return null;
            var f = findeFeld([].concat(feld.l), feld.nr);
            if (!f) return { s: 'fehlt', t: name + ': Beschriftung nicht gefunden' };
            var el = f.el;
            if (!el) return { s: 'fehlt', t: name + ': kein Eingabefeld gefunden' };
            if (el.disabled || el.readOnly) return { s: 'info', t: name + ': im Portal gesperrt' };
            if (f.radios) {
                var r = f.radios.filter(function (c) { return werte.some(function (w) { return radioText(c) === norm(w); }); })[0];
                if (!r) return { s: 'fehlt', t: name + ': Auswahl «' + werte[0] + '» nicht gefunden' };
                var schon = f.radios.filter(function (c) { return c.checked; })[0];
                if (schon && schon !== r) return { s: 'anders', t: name + ': Portal «' + radioText(schon) + '» — OneCrew «' + werte[0] + '»' };
                if (!r.checked) r.click();
                return { s: 'ok', t: name + ': ' + werte[0] };
            }
            if (el.tagName === 'SELECT') {
                var opts = Array.prototype.slice.call(el.options);
                var passt = function (o) { return werte.some(function (w) { return norm(o.text) === norm(w) || norm(o.value) === norm(w); }); };
                var o = opts.filter(passt)[0] || opts.filter(function (o) {
                    return werte.some(function (w) { return norm(w).length > 2 && norm(o.text).indexOf(norm(w)) === 0; });
                })[0];
                if (!o) return { s: 'fehlt', t: name + ': Eintrag «' + werte[0] + '» nicht in der Liste' };
                var akt = el.options[el.selectedIndex];
                var leer = !akt || el.selectedIndex <= 0 && /^(-|–|)$|unbekannt|bitte/i.test(akt.text.trim());
                if (akt === o) return { s: 'gleich', t: name + ': schon ' + o.text };
                if (!leer) return { s: 'anders', t: name + ': Portal «' + akt.text + '» — OneCrew «' + o.text + '»' };
                setzeWert(el, o.value);
                return { s: 'ok', t: name + ': ' + o.text };
            }
            var w0 = String(werte[0]);
            var ist = (el.value || '').trim();
            if (ist && norm(ist) === norm(w0)) return { s: 'gleich', t: name + ': schon ' + ist };
            if (ist) return { s: 'anders', t: name + ': Portal «' + ist + '» — OneCrew «' + w0 + '»' };
            setzeWert(el, w0);
            if (feld.hinweis) return { s: 'info', t: name + ': ' + w0 + ' — ' + feld.hinweis };
            if (norm(el.value) !== norm(w0)) return { s: 'anders', t: name + ': eingetragen, Portal zeigt «' + el.value + '» — bitte prüfen' };
            return { s: 'ok', t: name + ': ' + w0 };
        };
        var melden = function () {
            var zeilen = [];
            steuerelemente().forEach(function (c) {
                var best = null, bestA = 1e9;
                beschriftungen().forEach(function (lab) {
                    if (c.type === 'radio' && lab.contains(c)) return;
                    var a = abstand(lab, c);
                    if (a != null && a < bestA) { bestA = a; best = lab; }
                });
                var art = c.tagName === 'SELECT' ? 'Liste' : c.type === 'radio' ? 'Auswahl ' + radioText(c) : c.type === 'checkbox' ? 'Häkchen' : 'Text';
                var opt = c.tagName === 'SELECT'
                    ? ' [' + Array.prototype.slice.call(c.options, 0, 12).map(function (o) { return o.text.trim(); }).join(' | ') + ']' : '';
                zeilen.push((best ? eigenerText(best) : '(ohne Beschriftung)') + ' → ' + art + opt + (c.disabled ? ' (gesperrt)' : ''));
            });
            return 'GastroSocial-Felder (' + location.pathname + location.search + ')\n' + zeilen.join('\n');
        };

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

        var alt = document.getElementById(ID);
        if (alt) alt.remove();
        var p = document.createElement('div');
        p.id = ID;
        p.style.cssText = 'position:fixed;right:18px;bottom:18px;z-index:2147483647;width:340px;max-height:72vh;overflow:auto;'
            + 'background:linear-gradient(165deg,#eeece4,#dfdcd1);border:1px solid rgba(255,255,255,.7);border-radius:16px;'
            + 'box-shadow:0 18px 50px rgba(60,55,48,.28);padding:14px 16px;font:13px/1.45 -apple-system,Helvetica,Arial,sans-serif;color:#3f3f3f';
        var knopf = 'border:none;border-radius:10px;padding:7px 11px;margin:3px 4px 3px 0;cursor:pointer;font:600 12.5px -apple-system,Helvetica,Arial,sans-serif;';
        var html = '<div style="font-weight:800;font-size:14px">OneCrew → GastroSocial</div>'
            + '<div style="color:#646464;margin:2px 0 8px">' + String(daten.titel || '').replace(/</g, '&lt;') + '</div>'
            + '<div style="font-size:12px;color:#646464;margin-bottom:4px">Welchen Schritt ausfüllen?</div><div id="ocfz-knoepfe"></div>'
            + '<div id="ocfz-ergebnis" style="margin-top:8px"></div>'
            + '<div style="margin-top:10px;border-top:1px solid rgba(60,55,48,.15);padding-top:8px">'
            + '<button id="ocfz-melden" style="' + knopf + 'background:rgba(255,255,255,.6);color:#3f3f3f;border:1px solid rgba(60,55,48,.2)">Felder melden</button>'
            + '<button id="ocfz-zu" style="' + knopf + 'background:rgba(255,255,255,.6);color:#3f3f3f;border:1px solid rgba(60,55,48,.2)">Schliessen</button></div>';
        p.innerHTML = html;
        document.body.appendChild(p);
        var farbe = { ok: '#2f6b3a', gleich: '#646464', anders: '#9a5b00', fehlt: '#b42318', info: '#646464' };
        var zeichen = { ok: '✓', gleich: '=', anders: '≠', fehlt: '✗', info: 'ⓘ' };
        var ergebnis = p.querySelector('#ocfz-ergebnis');
        (daten.abschnitte || []).forEach(function (ab) {
            var b = document.createElement('button');
            b.textContent = ab.titel;
            b.style.cssText = knopf + 'background:#3f3f3f;color:#fff';
            b.onclick = function () {
                var zeilen = (ab.felder || []).map(fuelle).filter(Boolean);
                ergebnis.innerHTML = '<div style="font-weight:700;margin-bottom:3px">' + ab.titel.replace(/</g, '&lt;') + '</div>'
                    + zeilen.map(function (z) {
                        return '<div style="color:' + farbe[z.s] + '">' + zeichen[z.s] + ' ' + z.t.replace(/</g, '&lt;') + '</div>';
                    }).join('')
                    + (ab.hinweis ? '<div style="margin-top:6px;color:#646464">' + ab.hinweis.replace(/</g, '&lt;') + '</div>' : '');
            };
            p.querySelector('#ocfz-knoepfe').appendChild(b);
        });
        p.querySelector('#ocfz-zu').onclick = function () { p.remove(); };
        p.querySelector('#ocfz-melden').onclick = async function () {
            var txt = melden();
            try {
                await navigator.clipboard.writeText(txt);
                ergebnis.innerHTML = '<div style="color:#2f6b3a">✓ Feldliste kopiert (ohne Personendaten) — bitte an Walters Agent schicken.</div>';
            } catch (e) {
                ergebnis.innerHTML = '<textarea style="width:100%;height:160px;font-size:11px">' + txt.replace(/</g, '&lt;') + '</textarea>';
            }
        };
    })();
}

/** javascript:-Adresse für das Lesezeichen. */
function famzGsLesezeichenUrl() {
    const code = famzGsEngine.toString()
        .split('\n').map(z => z.trim()).filter(z => z && !z.startsWith('//')).join('\n');
    return 'javascript:' + encodeURIComponent('(' + code + ')()');
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

/** Ein Personen-Block (Partner / anderer Elternteil). Beschriftungen sind Annahmen — «Felder melden» korrigiert sie. */
function famzGsPerson(p) {
    if (!p) return [];
    const n = famzGsNamen(p);
    const po = famzGsPlzOrt(p.plzOrt);
    return [
        { l: ['Name', 'Nachname', 'Familienname'], w: n.name, anzeige: 'Name' },
        { l: ['Vorname'], w: n.vorname },
        { l: ['Geburtsdatum'], w: p.geburtsdatum },
        { l: ['AHV-Nummer', 'Sozialversicherungsnummer', 'AHV-Nr', 'Versichertennummer'], w: p.ahvNummer, anzeige: 'AHV-Nummer' },
        { l: ['Geschlecht'], w: famzGsGeschlecht(p.geschlecht) },
        { l: ['Zivilstand'], w: p.zivilstand ? (FAMZ_ZIVIL[p.zivilstand] || p.zivilstand) : null },
        { l: ['Nationalität', 'Staatsangehörigkeit'], w: p.nationalitaet, anzeige: 'Nationalität' },
        { l: ['Strasse'], w: p.strasseNr },
        { l: ['PLZ'], w: po.plz },
        { l: ['Ort'], w: po.ort },
        { l: ['Land'], w: p.kanton ? ['Schweiz', 'CH'] : null },
        { l: ['Erwerbstätig', 'Angestellt', 'Arbeitnehmend'], w: famzGsJn(p.angestellt), anzeige: 'Erwerbstätig' },
        { l: ['Arbeitgeber', 'Name Arbeitgeber'], w: p.arbeitgeberZeile1, anzeige: 'Arbeitgeber' },
        { l: ['Arbeitskanton', 'Arbeitsort'], w: famzGsKanton(p.arbeitsortKanton), anzeige: 'Arbeitskanton' },
        { l: ['Arbeitspensum', 'Pensum', 'Beschäftigungsgrad'], w: p.arbeitspensum, anzeige: 'Pensum' },
    ];
}

function famzGsDaten() {
    const f = famzSammeln();
    const a = f.antragsteller || {};
    const n = famzGsNamen(a);
    const po = famzGsPlzOrt(a.plzOrt);
    const ma = (_famzAlleMa || []).find(e => e.id === _famzEmpId) || {};
    const verheiratet = ['verheiratet', 'getrennt', 'eingetragene_partnerschaft'].includes(a.zivilstand);
    const ae = f.andererElternteil;
    const partner = f.partner || (verheiratet ? ae : null);

    const abschnitte = [{
        titel: '1 · Antrag',
        hinweis: 'Person in der Suche oben aus der Trefferliste wählen. «Meldeverfahren» bitte selbst wählen.',
        felder: [
            { l: ['Name, Geburtsdatum, AHV-Nummer'], w: a.ahvNummer, anzeige: 'Suche', hinweis: 'Person aus der Trefferliste wählen' },
            { l: ['Personalnummer des Bezügers'], w: ma.employeeNumber, anzeige: 'Personalnummer' },
            { l: ['Zulagen beantragt ab'], w: f.abWann },
            { l: ['Beschäftigt seit'], w: f.beschaeftigtVon },
            { l: ['Arbeitskanton'], w: famzGsKanton(f.arbeitsortKanton) },
            { l: ['Minimallohn erreicht'], w: famzGsJn(f.einkommenUeberMinimum) },
            { l: ['Strasse'], w: a.strasseNr },
            { l: ['PLZ'], w: po.plz },
            { l: ['Ort'], w: po.ort },
            { l: ['Land'], w: ['Schweiz', 'CH'] },
        ],
    }];
    if (partner) abschnitte.push({
        titel: '2 · Ehe-/Lebenspartner' + (partner === ae ? ' (= anderer Elternteil)' : ''),
        felder: famzGsPerson(partner),
    });
    if (ae) abschnitte.push({ titel: '3 · Anderer Elternteil', felder: famzGsPerson(ae) });
    (f.kinder || []).forEach((k, i) => abschnitte.push({
        titel: `4 · Kind ${i + 1}: ${k.vorname || ''}`.trim(),
        felder: [
            { l: ['Name', 'Nachname', 'Familienname'], w: k.name, anzeige: 'Name' },
            { l: ['Vorname'], w: k.vorname },
            { l: ['Geburtsdatum'], w: k.geburtsdatum },
            { l: ['Geschlecht'], w: famzGsGeschlecht(k.geschlecht) },
            { l: ['Kindesverhältnis', 'Beziehung', 'Verhältnis'], w: k.beziehung ? [FAMZ_BEZ[k.beziehung] || k.beziehung] : null, anzeige: 'Kindesverhältnis' },
        ],
    }));
    return {
        onecrew: 'famz-gs',
        v: 1,
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
