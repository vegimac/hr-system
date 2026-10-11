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

/**
 * Läuft im GastroSocial-Portal — darf nichts aus OneCrew verwenden. Kommt bei jedem
 * «Für GastroSocial kopieren» frisch mit (Feld «kern»); die im Lesezeichen eingebaute
 * Kopie ist nur die Rückfallebene. So wirken Verbesserungen ohne neues Lesezeichen.
 */
function famzGsKern(daten) {
        var VERSION = 11;
        var ID = 'onecrew-famz-panel';
        var norm = function (s) { return String(s == null ? '' : s).replace(/[*?:]/g, ' ').replace(/\s+/g, ' ').trim().toLowerCase(); };
        // Das Portal kann das Formular in einem eingebetteten Rahmen (iframe) zeigen.
        var dokumente = function () {
            var liste = [document], i = 0;
            while (i < liste.length) {
                Array.prototype.forEach.call(liste[i].querySelectorAll('iframe,frame'), function (f) {
                    try { if (f.contentDocument && liste.indexOf(f.contentDocument) < 0) liste.push(f.contentDocument); } catch (e) { /* fremde Herkunft */ }
                });
                i++;
            }
            return liste;
        };
        var alle = function (sel) {
            var out = [];
            dokumente().forEach(function (d) { out = out.concat(Array.prototype.slice.call(d.querySelectorAll(sel))); });
            return out;
        };
        var sichtbar = function (el) {
            var r = el.getBoundingClientRect();
            return r.width > 0 && r.height > 0 && el.ownerDocument.defaultView.getComputedStyle(el).visibility !== 'hidden';
        };
        var imPanel = function (el) { return !!(el.closest && el.closest('#' + ID)); };
        // Auswahlknöpfe sind im Portal oft nur aufgemalt; das echte Feld ist unsichtbar.
        // Dann zählt die Lage des gemalten Kreises bzw. der Beschriftung.
        var ersatz = function (c) {
            if (!/^(radio|checkbox)$/.test(c.type)) return null;
            var kandidaten = [];
            if (c.labels && c.labels.length) kandidaten.push(c.labels[0]);
            if (c.nextElementSibling) kandidaten.push(c.nextElementSibling);
            if (c.closest('label')) kandidaten.push(c.closest('label'));
            if (c.parentElement) kandidaten.push(c.parentElement);
            return kandidaten.filter(function (k) {
                var r = k.getBoundingClientRect();
                return sichtbar(k) && r.height < 40 && r.width < 400;
            })[0] || null;
        };
        var flaeche = function (c) { return (sichtbar(c) ? c : ersatz(c) || c).getBoundingClientRect(); };
        var steuerelemente = function () {
            return alle('input,select,textarea').filter(function (el) {
                return !/^(hidden|button|submit|reset|image|file)$/i.test(el.type) && (sichtbar(el) || ersatz(el)) && !imPanel(el);
            });
        };
        var eigenerText = function (el) {
            var t = '';
            el.childNodes.forEach(function (n) { if (n.nodeType === 3) t += n.textContent; });
            return norm(t);
        };
        var beschriftungen = function () {
            return alle('label,span,div,td,th,p,legend,b,strong,h1,h2,h3,h4').filter(function (el) {
                if (imPanel(el) || !sichtbar(el)) return false;
                var t = eigenerText(el);
                return t.length > 0 && t.length < 90;
            });
        };
        // Lage-Abstand Beschriftung → Feld: darunter (linksbündig) oder rechts daneben.
        var abstand = function (lab, c) {
            if (lab.ownerDocument !== c.ownerDocument) return null;
            var r = lab.getBoundingClientRect(), q = flaeche(c);
            if (lab.contains(c)) return 1;
            var unten = q.top - r.bottom;
            if (unten >= -6 && unten < 48 && Math.abs(q.left - r.left) < 45) return unten + Math.abs(q.left - r.left);
            var mitte = Math.abs((q.top + q.bottom) / 2 - (r.top + r.bottom) / 2);
            var rechts = q.left - r.right;
            if (mitte < 14 && rechts > -6 && rechts < 320) return 60 + rechts;
            return null;
        };
        var radioText = function (c) {
            if (c.labels && c.labels.length && norm(c.labels[0].textContent)) return norm(c.labels[0].textContent);
            var n = c.nextSibling;
            while (n && !n.textContent.trim()) n = n.nextSibling;
            if (n) return norm(n.textContent);
            if (c.closest('label') && norm(c.closest('label').textContent)) return norm(c.closest('label').textContent);
            return norm(c.parentElement ? c.parentElement.textContent : '');
        };
        // Zweites Feld derselben Zeile (z.B. «Arbeitskanton/-Land»: Land | Kanton).
        var rechtsDaneben = function (c) {
            var r = flaeche(c), best = null, bestA = 1e9;
            steuerelemente().forEach(function (x) {
                if (x === c || x.ownerDocument !== c.ownerDocument) return;
                var q = flaeche(x), d = q.left - r.right;
                if (Math.abs((q.top + q.bottom) / 2 - (r.top + r.bottom) / 2) < 14 && d > -6 && d < 200 && d < bestA) { bestA = d; best = x; }
            });
            return best;
        };
        var findeFeld = function (labels, nr) {
            var kandidaten = beschriftungen();
            for (var i = 0; i < labels.length; i++) {
                var muster = norm(labels[i]);
                var treffer = kandidaten.filter(function (el) { return eigenerText(el) === muster; });
                if (!treffer.length) treffer = kandidaten.filter(function (el) { return eigenerText(el).indexOf(muster) === 0; });
                var lab = treffer[nr || 0];
                if (!lab) continue;
                if (lab.htmlFor) {
                    var f = lab.ownerDocument.getElementById(lab.htmlFor);
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
            var fenster = el.ownerDocument.defaultView;
            var proto = el.tagName === 'SELECT' ? fenster.HTMLSelectElement.prototype
                : el.tagName === 'TEXTAREA' ? fenster.HTMLTextAreaElement.prototype : fenster.HTMLInputElement.prototype;
            el.focus();
            // Wie getippt: Datumsfelder mit Eingabemaske nehmen nur echte Eingaben an.
            if (el.tagName !== 'SELECT') {
                var getippt = false;
                try { if (el.select) el.select(); getippt = el.ownerDocument.execCommand('insertText', false, w) && !!el.value; } catch (e) { getippt = false; }
                if (getippt) {
                    el.dispatchEvent(new fenster.Event('change', { bubbles: true }));
                    el.blur();
                    return;
                }
            }
            Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, w);
            ['input', 'change', 'keyup'].forEach(function (t) { el.dispatchEvent(new fenster.Event(t, { bubbles: true })); });
            el.blur();
        };
        // Auswahl, deren Beschriftung die Option selbst ist («Familie im gleichen Haushalt
        // lebend», «Es existieren weitere …»): Knopf links daneben oder in der Beschriftung.
        var findeOption = function (labels) {
            var kandidaten = beschriftungen();
            for (var i = 0; i < labels.length; i++) {
                var muster = norm(labels[i]);
                var lab = kandidaten.filter(function (el) { return eigenerText(el) === muster; })[0]
                    || kandidaten.filter(function (el) { return eigenerText(el).indexOf(muster) === 0; })[0];
                if (!lab) continue;
                if (lab.htmlFor) {
                    var f = lab.ownerDocument.getElementById(lab.htmlFor);
                    if (f && /^(radio|checkbox)$/.test(f.type)) return f;
                }
                var r = lab.getBoundingClientRect(), best = null, bestA = 1e9;
                steuerelemente().forEach(function (c) {
                    if (!/^(radio|checkbox)$/.test(c.type)) return;
                    var q = flaeche(c), a = null;
                    if (lab.contains(c) || (lab.parentElement && lab.parentElement.contains(c) && Math.abs(q.top - r.top) < 14 && q.left <= r.left)) a = 0;
                    else if (Math.abs((q.top + q.bottom) / 2 - (r.top + r.bottom) / 2) < 14 && r.left - q.right > -6 && r.left - q.right < 45) a = r.left - q.right;
                    if (a != null && a < bestA) { bestA = a; best = c; }
                });
                return best || 'ohne';
            }
            return null;
        };
        // Wie ein Mausklick: manche Portal-Knöpfe reagieren nur auf die ganze Ereignisfolge.
        var drueck = function (el) {
            var fenster = el.ownerDocument.defaultView;
            ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'].forEach(function (t) {
                var E = /^pointer/.test(t) && fenster.PointerEvent ? fenster.PointerEvent : fenster.MouseEvent;
                el.dispatchEvent(new E(t, { bubbles: true, cancelable: true, view: fenster }));
            });
        };
        var klickAuf = function (c) {
            if (!sichtbar(c)) {
                var z = [c.labels && c.labels[0], c.nextElementSibling, c.closest('label')].filter(function (k) {
                    return k && sichtbar(k) && k.getBoundingClientRect().width < 400;
                })[0];
                if (z) drueck(z);
            }
            if (!c.checked) c.click();
        };
        // Ja/Nein ohne echtes Auswahlfeld (vom Portal gezeichnet): sichtbaren Text unter der Frage anklicken.
        var nurText = function (t) { return norm(t).replace(/[^a-z0-9äöüàâéèêîôûç ]/g, '').trim(); };
        var textKnopf = function (lab, werte) {
            var r = lab.getBoundingClientRect(), best = null, bestA = 1e9;
            Array.prototype.forEach.call(lab.ownerDocument.querySelectorAll('label,span,div,button,a,[role=radio]'), function (el) {
                if (imPanel(el) || !sichtbar(el) || el.contains(lab)) return;
                var t = nurText(eigenerText(el) || (el.children.length <= 2 ? el.textContent : ''));
                if (!werte.some(function (w) { return t === nurText(w); })) return;
                var q = el.getBoundingClientRect(), unten = q.top - r.bottom;
                var mitte = Math.abs((q.top + q.bottom) / 2 - (r.top + r.bottom) / 2);
                var a = unten >= -6 && unten < 60 && q.left > r.left - 45 && q.left < r.left + 400 ? unten + (q.left - r.left) / 10
                    : mitte < 14 && q.left > r.right - 6 && q.left < r.right + 320 ? 60 + q.left - r.right : null;
                if (a != null && a < bestA) { bestA = a; best = el; }
            });
            if (!best) return null;
            var kreis = best.closest('[role=radio]') || best;
            return { el: best, an: kreis.getAttribute('aria-checked') === 'true' || /\b(checked|selected|active)\b/.test(String(kreis.className)) };
        };
        // Kontrollgang: nichts mehr setzen, nur melden, was das Portal wieder gelöscht hat.
        var nurPruefen = false;
        var verloren = function (name) { return { s: 'fehlt', t: name + ': Portal hat den Wert wieder gelöscht — bitte von Hand eintragen' }; };
        var waehle = function (feld, name) {
            var soll = [].concat(feld.w)[0];
            var an = soll === true || /^(ja|x|true)$/i.test(String(soll));
            var c = findeOption([].concat(feld.l));
            if (!c) return { s: 'fehlt', t: name + ': Beschriftung nicht gefunden' };
            if (c === 'ohne') return { s: 'fehlt', t: name + ': kein Auswahlknopf gefunden' };
            if (c.disabled) return { s: 'info', t: name + ': im Portal gesperrt' };
            if (c.checked === an) return { s: 'gleich', t: name + ': schon ' + (an ? 'gewählt' : 'leer') };
            if (c.type === 'radio') {
                var schon = c.name ? steuerelemente().filter(function (x) { return x.type === 'radio' && x.name === c.name && x.checked; })[0] : null;
                if (schon) return { s: 'anders', t: name + ': im Portal ist schon etwas anderes gewählt' };
                if (!an) return { s: 'gleich', t: name + ': nicht gewählt' };
                if (nurPruefen) return verloren(name);
                klickAuf(c);
                return { s: 'ok', t: name + ': gewählt' };
            }
            if (c.checked && !an) return { s: 'anders', t: name + ': Portal angekreuzt — OneCrew nein' };
            if (nurPruefen) return verloren(name);
            klickAuf(c);
            return { s: 'ok', t: name + ': angekreuzt' };
        };
        var fuelle = function (feld) {
            var werte = [].concat(feld.w).filter(function (x) { return x != null && String(x).trim() !== ''; });
            var name = feld.anzeige || [].concat(feld.l)[0];
            if (!werte.length) return null;
            if (feld.art === 'option') return waehle(feld, name);
            var f = findeFeld([].concat(feld.l), feld.nr);
            if (!f) return { s: 'fehlt', t: name + ': Beschriftung nicht gefunden' };
            var el = f.el;
            if (feld.rechts && el) { el = rechtsDaneben(el); f = { lab: f.lab, el: el }; }
            if (!f.radios && (feld.wahl || /^(ja|nein|männlich|weiblich)$/i.test(String(werte[0])))) {
                var tk = textKnopf(f.lab, werte);
                if (tk) {
                    if (tk.an) return { s: 'gleich', t: name + ': schon ' + werte[0] };
                    if (nurPruefen) return { s: 'info', t: name + ': ' + werte[0] + ' angeklickt — bitte kurz prüfen' };
                    drueck(tk.el);
                    return { s: 'info', klick: true, t: name + ': ' + werte[0] + ' angeklickt — bitte kurz prüfen' };
                }
            }
            if (!el) return { s: 'fehlt', t: name + ': kein Eingabefeld gefunden' };
            if (el.disabled || el.readOnly) return { s: 'info', t: name + ': im Portal gesperrt' };
            if (f.radios) {
                var r = f.radios.filter(function (c) { return werte.some(function (w) { return nurText(radioText(c)) === nurText(w); }); })[0];
                if (!r) return { s: 'fehlt', t: name + ': Auswahl «' + werte[0] + '» nicht gefunden' };
                var schon = f.radios.filter(function (c) { return c.checked; })[0];
                if (schon && schon !== r) return { s: 'anders', t: name + ': Portal «' + radioText(schon) + '» — OneCrew «' + werte[0] + '»' };
                if (r.checked) return { s: 'gleich', t: name + ': schon ' + werte[0] };
                if (nurPruefen) return verloren(name);
                klickAuf(r);
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
                if (nurPruefen) return verloren(name);
                setzeWert(el, o.value);
                return { s: 'ok', t: name + ': ' + o.text };
            }
            var w0 = String(werte[0]);
            var ist = (el.value || '').trim();
            if (ist && norm(ist) === norm(w0)) return { s: 'gleich', t: name + ': schon ' + ist };
            if (ist) return { s: 'anders', t: name + ': Portal «' + ist + '» — OneCrew «' + w0 + '»' };
            if (nurPruefen) return verloren(name);
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
            return 'GastroSocial-Felder (' + location.pathname + location.search + ', Rahmen: ' + (dokumente().length - 1)
                + ', iframes: ' + alle('iframe,frame').length + ')\n' + zeilen.join('\n');
        };

        // ── Fenster: Schritt wählen, Werte mit 📋 (Abschreibhilfe), «Einfüllen» ──
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
        var farbe = { ok: '#2f6b3a', gleich: '#646464', anders: '#9a5b00', fehlt: '#b42318', info: '#646464' };
        var zeichen = { ok: '✓', gleich: '=', anders: '≠', fehlt: '✗', info: 'ⓘ' };
        var abschnitte = daten.abschnitte || [];
        var aktiv = 0, status = {}, laeuft = null;

        var wertText = function (feld) {
            if (feld.zeige != null) return String(feld.zeige);
            var werte = [].concat(feld.w).filter(function (x) { return x != null && String(x).trim() !== ''; });
            if (!werte.length) return '';
            if (feld.art === 'option') return werte[0] === true || /^(ja|x|true)$/i.test(String(werte[0])) ? 'anklicken' : 'nicht ankreuzen';
            return String(werte[0]);
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
                var st = status[aktiv + ':' + k];
                var name = feld.anzeige || [].concat(feld.l)[0] || '';
                return '<div style="display:flex;align-items:center;gap:6px;padding:4px 0;border-bottom:1px solid rgba(60,55,48,.08)">'
                    + '<span style="width:14px;color:' + (st ? farbe[st.s] : '#bbb') + '">' + (st ? zeichen[st.s] : '·') + '</span>'
                    + '<div style="flex:1;min-width:0"><div style="font-size:11px;color:#8b8b8b">' + esc(name) + '</div>'
                    + '<div style="font-weight:700;word-break:break-word">' + esc(w) + '</div>'
                    + (st && st.s !== 'ok' && st.s !== 'gleich' ? '<div style="font-size:11px;color:' + farbe[st.s] + '">' + esc(st.t) + '</div>' : '')
                    + '</div>'
                    + (feld.art === 'option' ? '' : '<button data-kopie="' + k + '" title="kopieren, dann im Portal ⌘V" style="' + hell + 'margin:0;padding:4px 8px">📋</button>')
                    + '</div>';
            }).join('') || '<div style="color:#8b8b8b">Für diesen Schritt hat OneCrew keine Werte.</div>';
            p.innerHTML = '<div style="font-weight:800;font-size:14px">OneCrew → GastroSocial <span style="font-weight:400;font-size:11px;color:#8b8b8b">v' + VERSION + '</span></div>'
                + '<div style="color:#646464;margin:2px 0 6px">' + esc(daten.titel) + '</div>'
                + '<div>' + tabs + '</div>'
                + '<div style="display:flex;align-items:center;gap:8px;margin:8px 0 4px">'
                + '<button id="ocfz-fuellen" style="' + dunkel + 'margin:0"' + (laeuft ? ' disabled' : '') + '>' + (laeuft ? '⏳ ' + esc(laeuft) : 'Einfüllen') + '</button>'
                + '<span style="font-size:11.5px;color:#646464">oder mit 📋 kopieren und im Portal ⌘V</span></div>'
                + zeilen
                + (ab.hinweis ? '<div style="margin-top:6px;font-size:12px;color:#646464">' + esc(ab.hinweis) + '</div>' : '')
                + '<div id="ocfz-meldung" style="margin-top:6px"></div>'
                + '<div style="margin-top:8px;border-top:1px solid rgba(60,55,48,.15);padding-top:6px">'
                + '<button id="ocfz-melden" style="' + hell + '">Felder melden</button>'
                + '<button id="ocfz-zu" style="' + hell + '">Schliessen</button></div>';
            Array.prototype.forEach.call(p.querySelectorAll('[data-tab]'), function (b) {
                b.onclick = function () { if (!laeuft) { aktiv = +b.getAttribute('data-tab'); zeichne(); } };
            });
            Array.prototype.forEach.call(p.querySelectorAll('[data-kopie]'), function (b) {
                b.onclick = function () { kopieren(wertText(ab.felder[+b.getAttribute('data-kopie')]), b); };
            });
            p.querySelector('#ocfz-fuellen').onclick = function () { if (!laeuft) einfuellen(aktiv); };
            p.querySelector('#ocfz-zu').onclick = function () { p.remove(); };
            p.querySelector('#ocfz-melden').onclick = async function () {
                var txt = melden(), ziel = p.querySelector('#ocfz-meldung');
                try {
                    await navigator.clipboard.writeText(txt);
                    ziel.innerHTML = '<div style="color:#2f6b3a">✓ Feldliste kopiert (ohne Personendaten).</div>';
                } catch (e) {
                    ziel.innerHTML = '<textarea style="width:100%;height:160px;font-size:11px">' + esc(txt) + '</textarea>';
                }
            };
        };

        // Das Portal fragt nach jeder Eingabe den Server und baut den Abschnitt neu —
        // was in der Zwischenzeit eingetragen wurde, geht verloren. Darum langsam,
        // danach ein zweiter Durchgang (trägt Verlorenes nach) und ein Kontrollgang.
        var PAUSE = 700, RUHE = 1800;
        var einfuellen = function (idx) {
            var felder = abschnitte[idx].felder || [];
            var gesetzt = [], nochmals = [];
            Object.keys(status).forEach(function (k) { if (k.indexOf(idx + ':') === 0) delete status[k]; });
            var gang = function (liste, modus, fertig) {
                var j = 0;
                var naechstes = function () {
                    if (j >= liste.length) { fertig(); return; }
                    var k = liste[j++];
                    var feld = felder[k];
                    var name = feld.anzeige || [].concat(feld.l)[0] || '';
                    laeuft = (modus === 1 ? '' : modus === 2 ? 'Nachtragen: ' : 'Kontrolle: ') + name;
                    zeichne();
                    setTimeout(function () {
                        var z;
                        nurPruefen = modus === 3;
                        try { z = fuelle(feld); } catch (e) { z = { s: 'fehlt', t: name + ': Fehler ' + (e && e.message || e) }; }
                        nurPruefen = false;
                        var key = idx + ':' + k;
                        if (modus === 1) {
                            if (z) { status[key] = z; if (z.s === 'ok') gesetzt.push(k); if (z.s === 'fehlt') nochmals.push(k); }
                        } else if (z && nochmals.indexOf(k) >= 0) {
                            status[key] = z;
                        } else if (z && z.s !== 'gleich') {
                            status[key] = modus === 2 && z.s === 'ok' ? { s: 'ok', t: z.t + ' (nachgetragen)' } : z;
                        }
                        setTimeout(naechstes, z && (z.s === 'ok' || z.klick) ? PAUSE : 60);
                    }, 60);
                };
                naechstes();
            };
            var schluss = function () { laeuft = null; zeichne(); };
            gang(felder.map(function (f, k) { return k; }), 1, function () {
                if (!gesetzt.length && !nochmals.length) { schluss(); return; }
                laeuft = 'Warte auf das Portal'; zeichne();
                setTimeout(function () {
                    // Felder, die das Portal erst nach einer Antwort zeigt, kommen hier dran.
                    gang(gesetzt.concat(nochmals).sort(function (a, b) { return a - b; }), 2, function () {
                        laeuft = 'Warte auf das Portal'; zeichne();
                        setTimeout(function () { gang(gesetzt, 3, schluss); }, RUHE);
                    });
                }, RUHE);
            });
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
        hinweis: 'Person in der Suche oben aus der Trefferliste wählen.',
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
