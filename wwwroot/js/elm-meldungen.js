// ELM-Meldungen (Walter 27.09.2026) — Etappe E6.
//
// Monatsmeldung (Quellensteuer + Statistik) für einen Monat erzeugen, gegen die
// ELM-6.0-Schemas prüfen und — wenn eine Referenz von Swissdec im Repo liegt —
// Feld für Feld mit ihr vergleichen. Gesendet wird hier NICHTS: das XML wird
// heruntergeladen und im RefApps-Transmitter hochgeladen. Direktes Senden kommt
// erst mit Transmitter-Zertifikat und dann nur an die Adresse, die der
// Super-Admin festgelegt hat.

let _elmMonate = [];
let _elmXml = '';

async function elmMeldungenInit() {
    const sel = document.getElementById('elmMonatSel');
    if (sel) sel.innerHTML = '<option value="">– lade –</option>';
    _elmXml = '';
    elmZeigeErgebnis(null);
    try {
        const res = await fetch('/api/elm/monthly/moegliche-monate', {
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const d = await res.json();
        _elmMonate = d.monate || [];
    } catch (e) {
        _elmMonate = [];
        elmHinweis(`Monate konnten nicht geladen werden: ${e.message}`, true);
    }
    elmFuelleMonate();
}

function elmFuelleMonate() {
    const sel = document.getElementById('elmMonatSel');
    if (!sel) return;
    if (_elmMonate.length === 0) {
        sel.innerHTML = '<option value="">– keine Lohnperioden vorhanden –</option>';
        elmHinweis('Es gibt noch keine Lohnperioden. Eine Meldung entsteht aus abgeschlossenen Lohnläufen.', true);
        return;
    }
    // Walter 27.09.2026: ALLE Monate zeigen. Nicht meldebereite stehen mit dem Grund
    // da und sind gesperrt — eine leere Liste sagt einem nicht, woran es liegt.
    sel.innerHTML = _elmMonate.map(m => {
        const mm = String(m.monat).padStart(2, '0');
        const ref = m.referenz ? ' · Referenz vorhanden' : '';
        if (m.bereit) return `<option value="${m.jahr}-${mm}">${mm}.${m.jahr} · ${m.filialen} Filialen${ref}</option>`;
        const wort = m.offen === 1 ? 'Filiale' : 'Filialen';
        return `<option value="" disabled>${mm}.${m.jahr} · noch offen in ${m.offen} von ${m.filialen} ${wort}</option>`;
    }).join('');
    const bereit = _elmMonate.filter(m => m.bereit);
    const ersteBereit = _elmMonate.findIndex(m => m.bereit);
    if (ersteBereit >= 0) sel.selectedIndex = ersteBereit;
    if (bereit.length === 0) {
        elmHinweis(`Kein Monat ist meldebereit. Eine Meldung entsteht nur aus Lohnläufen, die in ALLEN ${_elmMonate[0].filialen} Filialen definitiv abgeschlossen sind — sonst wäre es eine halbe Meldung.`, true);
    } else {
        const offen = _elmMonate.length - bereit.length;
        elmHinweis(offen > 0
            ? `${bereit.length} Monate sind meldebereit, ${offen} weitere noch nicht in allen Filialen abgeschlossen (grau).`
            : `${bereit.length} Monate sind meldebereit.`, false);
    }
}

function elmHinweis(text, fehler) {
    const el = document.getElementById('elmHinweis');
    if (!el) return;
    el.textContent = text || '';
    el.style.color = fehler ? '#b91c1c' : '#8b8b8b';
}

async function elmMeldungErzeugen() {
    const sel = document.getElementById('elmMonatSel');
    const wert = sel?.value || '';
    if (!wert) { elmHinweis('Bitte einen Monat wählen.', true); return; }
    const [jahr, monat] = wert.split('-').map(Number);
    const btn = document.getElementById('elmErzeugenBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Meldung wird erzeugt…'; }
    elmZeigeErgebnis(null);
    const alle = document.getElementById('elmAlleErgebnis');
    if (alle) { alle.style.display = 'none'; alle.innerHTML = ''; }
    try {
        const res = await fetch(`/api/elm/monthly/${jahr}/${monat}`, {
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        const d = await res.json();
        if (!res.ok) throw new Error(d.message || d.error || ('HTTP ' + res.status));
        _elmXml = d.xml || '';
        _elmJahr = jahr; _elmMonat = monat;
        elmZeigeErgebnis(d, jahr, monat);
    } catch (e) {
        elmHinweis(`Fehler: ${e.message}`, true);
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'Meldung erzeugen'; }
    }
}

function elmZeigeErgebnis(d, jahr, monat) {
    const box = document.getElementById('elmErgebnis');
    if (!box) return;
    if (!d) { box.innerHTML = ''; box.style.display = 'none'; return; }
    box.style.display = 'block';

    const pille = (text, farbe) =>
        `<span style="display:inline-block;padding:3px 11px;border-radius:999px;font-size:12px;font-weight:600;background:${farbe.bg};color:${farbe.fg}">${text}</span>`;
    const gruen = { bg: '#dcfce7', fg: '#166534' };
    const rot   = { bg: '#fee2e2', fg: '#991b1b' };
    const grau  = { bg: 'rgba(60,55,48,0.10)', fg: '#3f3f3f' };

    const xsdOk = (d.xsdFehler || []).length === 0 && (d.xml || '').length > 0;
    let html = `<div style="display:flex;gap:10px;flex-wrap:wrap;align-items:center;margin-bottom:14px">
        ${pille(xsdOk ? 'Schema in Ordnung' : `${(d.xsdFehler || []).length} Schema-Fehler`, xsdOk ? gruen : rot)}
        ${pille(`${d.personen} Personen`, grau)}
        ${pille(`${d.qstZeilen} Quellensteuer-Zeilen`, grau)}
        ${pille(`${d.statistikZeilen} Statistik-Zeilen`, grau)}
      </div>`;

    if ((d.xsdFehler || []).length > 0) {
        html += `<div style="margin-bottom:14px"><div style="font-weight:600;font-size:13px;margin-bottom:6px">Schema-Fehler</div>
            <ul style="margin:0;padding-left:18px;font-size:12.5px;color:#b91c1c">
            ${d.xsdFehler.map(f => `<li>${elmEsc(f)}</li>`).join('')}</ul></div>`;
    }

    if (d.referenzFehlt) {
        html += `<div style="margin-bottom:14px;font-size:12.5px;color:#646464;line-height:1.6">
            Die Referenz von Swissdec liegt nicht auf dem Server — sie gehört auch nicht auf eine Lohnanlage.
            Wähle die Datei <code>RefXML_${jahr}${String(monat).padStart(2,'0')}_MONTHLY.xml</code> (oder
            <code>RefXML_${jahr}-${String(monat).padStart(2,'0')}_MONTHLY.xml</code>) aus deinem Projektordner
            <code>SWISSCEC/RefXML</code>. Sie wird nur für diesen Vergleich gelesen.
            <div style="margin-top:10px"><button type="button" class="btn-primary"
                style="font-size:12.5px;padding:7px 15px" onclick="elmReferenzWaehlen()">Mit Referenz vergleichen…</button></div>
          </div>`;
    }

    const v = d.vergleich;
    if (v && v.fehler) {
        html += `<div style="font-size:12.5px;color:#b91c1c;margin-bottom:14px">${elmEsc(v.fehler)}</div>`;
    } else if (v) {
        const ok = v.offen === 0;
        html += `<div style="margin-bottom:10px;display:flex;gap:10px;flex-wrap:wrap;align-items:center">
            ${pille(ok ? 'Deckt sich mit der Referenz' : `${v.offen} offene Unterschiede`, ok ? gruen : rot)}
            ${pille(`${v.bewusst} bewusste Abweichungen`, grau)}
            ${pille(`${v.geprueft} Felder verglichen`, grau)}
            <span style="font-size:12px;color:#8b8b8b">Referenz: ${elmEsc(v.referenz)} · ${v.personenIst} von ${v.personenSoll} Personen</span>
          </div>`;
        if ((v.unterschiede || []).length > 0) {
            html += `<div style="max-height:360px;overflow:auto;border:1px solid rgba(60,55,48,0.14);border-radius:10px">
              <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                <thead><tr style="background:rgba(60,55,48,0.06)">
                  <th style="text-align:left;padding:7px 10px;position:sticky;top:0;background:#efece5">Person</th>
                  <th style="text-align:left;padding:7px 10px;position:sticky;top:0;background:#efece5">Feld</th>
                  <th style="text-align:right;padding:7px 10px;position:sticky;top:0;background:#efece5">OneCrew</th>
                  <th style="text-align:right;padding:7px 10px;position:sticky;top:0;background:#efece5">Referenz</th>
                  <th style="text-align:left;padding:7px 10px;position:sticky;top:0;background:#efece5"></th>
                </tr></thead><tbody>
                ${v.unterschiede.map(u => `<tr style="border-top:1px solid rgba(60,55,48,0.10)">
                    <td style="padding:6px 10px">${elmEsc(u.person)}</td>
                    <td style="padding:6px 10px;color:#646464">${elmEsc(u.feld)}</td>
                    <td style="padding:6px 10px;text-align:right">${elmEsc(u.ist ?? '—')}</td>
                    <td style="padding:6px 10px;text-align:right">${elmEsc(u.soll ?? '—')}</td>
                    <td style="padding:6px 10px;font-size:11.5px;color:${u.bewusst ? '#646464' : '#b91c1c'}">${u.bewusst ? elmEsc(u.bewusst) : 'offen'}</td>
                  </tr>`).join('')}
              </tbody></table></div>`;
        }
    }

    if ((d.warnungen || []).length > 0) {
        html += `<div style="margin-top:14px"><div style="font-weight:600;font-size:13px;margin-bottom:6px">Hinweise aus dem Aufbau</div>
            <ul style="margin:0;padding-left:18px;font-size:12.5px;color:#646464">
            ${d.warnungen.map(w => `<li>${elmEsc(w)}</li>`).join('')}</ul></div>`;
    }

    if ((d.xml || '').length > 0) {
        const mm = String(monat).padStart(2, '0');
        html += `<div style="display:flex;gap:10px;margin-top:16px;justify-content:flex-end">
            <button type="button" class="kd-btn-glass" style="font-size:12.5px;padding:7px 15px;border-radius:10px"
                    onclick="elmXmlAnsehen()">XML ansehen</button>
            <button type="button" class="kd-btn-glass" style="font-size:12.5px;padding:7px 15px;border-radius:10px"
                    onclick="elmXmlKopieren()">XML kopieren</button>
            <button type="button" class="btn-primary" style="font-size:12.5px;padding:7px 15px"
                    onclick="elmXmlHerunterladen(${jahr}, ${monat})">XML herunterladen</button>
          </div>
          <div style="font-size:11.5px;color:#8b8b8b;margin-top:8px;text-align:right">
            Datei heisst <code>DeclareMonthlySalary_${jahr}-${mm}.xml</code> und wird im RefApps-Transmitter hochgeladen.
          </div>`;
        if (d.bericht) {
            html += `<details style="margin-top:14px"><summary style="cursor:pointer;font-size:12.5px;color:#646464">Bericht als Markdown (für SWISSCEC/Abgleich)</summary>
                <pre style="white-space:pre-wrap;font-size:11.5px;background:rgba(60,55,48,0.05);padding:12px;border-radius:10px;margin-top:8px">${elmEsc(d.bericht)}</pre></details>`;
        }
    }
    box.innerHTML = html;
}

function elmEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function elmXmlAnsehen() {
    if (!_elmXml) return;
    const box = document.getElementById('elmXmlBox');
    if (!box) return;
    box.style.display = box.style.display === 'none' ? 'block' : 'none';
    box.textContent = _elmXml;
}

async function elmXmlHerunterladen(jahr, monat) {
    if (!_elmXml) { elmHinweis('Es liegt noch kein XML vor — bitte zuerst «Meldung erzeugen».', true); return; }
    // Walter 27.09.2026: KEIN Blob, KEIN Link in der Seite, KEIN «Speichern unter…».
    // Der Server legt die Datei unter einer einmaligen Marke bereit, und der Browser
    // holt sie als ganz gewoehnlichen Download ab — daran kann sich keine
    // Erweiterung mehr aufhaengen.
    elmHinweis('Datei wird bereitgestellt…', false);
    const altStack = document.getElementById('elmFehlerStack');
    if (altStack) altStack.textContent = '';
    try {
        const res = await fetch(`/api/elm/monthly/${jahr}/${monat}/datei-marke`, {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        const d = await res.json();
        if (!res.ok) throw new Error(d.message || d.error || ('HTTP ' + res.status));
        window.location.href = `/api/elm/datei/${d.marke}`;
        elmHinweis(`${d.dateiname} wird heruntergeladen — die Datei liegt im Download-Ordner.`, false);
    } catch (e) {
        elmHinweis(`Herunterladen fehlgeschlagen: ${e.message} — als Ausweg «XML kopieren».`, true);
        elmZeigeStack(e);
    }
}

// Walter 27.09.2026: bei einem Fehler auch den Stack zeigen (klein, ausklappbar).
// Steht dort chrome-extension://… oder safari-web-extension://…, kommt der Fehler
// aus einer Browser-Erweiterung und nicht aus OneCrew — das sieht man sonst nie.
// Bewusst ueber DOM-Knoten aufgebaut, nicht ueber innerHTML: der Stack ist fremder
// Text und hat in der Seite nichts als Markup verloren.
function elmZeigeStack(e) {
    const hinweis = document.getElementById('elmHinweis');
    if (!hinweis || !hinweis.parentNode) return;
    let box = document.getElementById('elmFehlerStack');
    if (!box) {
        box = document.createElement('div');
        box.id = 'elmFehlerStack';
        box.style.cssText = 'margin:4px 0 8px';
        hinweis.parentNode.insertBefore(box, hinweis.nextSibling);
    }
    box.textContent = '';
    const stack = (e && (e.stack || e.message)) ? String(e.stack || e.message) : 'kein Stack vorhanden';
    const details = document.createElement('details');
    const summary = document.createElement('summary');
    summary.textContent = 'Woher kommt der Fehler?';
    summary.style.cssText = 'cursor:pointer;font-size:11.5px;color:#8b8b8b';
    const pre = document.createElement('pre');
    pre.textContent = stack;
    pre.style.cssText = 'white-space:pre-wrap;font-size:11px;color:#646464;'
        + 'background:rgba(60,55,48,0.05);padding:10px;border-radius:8px;margin:6px 0 0;max-height:220px;overflow:auto';
    const hinw = document.createElement('div');
    hinw.style.cssText = 'font-size:11.5px;color:#8b8b8b;margin-top:6px';
    hinw.textContent = /(-extension:\/\/)/.test(stack)
        ? 'Der Fehler stammt aus einer Browser-Erweiterung, nicht aus OneCrew.'
        : 'Steht in den Zeilen eine Adresse mit «-extension://», stammt der Fehler aus einer Browser-Erweiterung.';
    details.appendChild(summary); details.appendChild(pre); details.appendChild(hinw);
    box.appendChild(details);
}

// Notausgang, wenn der «Speichern unter…»-Dialog am Rechner klemmt: das XML in
// die Zwischenablage legen. Von dort in einen Editor einfügen und als
// DeclareMonthlySalary_JJJJ-MM.xml ablegen (Walter 27.09.2026).
async function elmXmlKopieren() {
    if (!_elmXml) { elmHinweis('Es liegt noch kein XML vor — bitte zuerst «Meldung erzeugen».', true); return; }
    try {
        await navigator.clipboard.writeText(_elmXml);
        elmHinweis('XML in der Zwischenablage — in einen Editor einfügen und als .xml speichern.', false);
    } catch (e) {
        // Ohne Clipboard-Recht: Text markieren, dann kann der Benutzer selbst kopieren.
        const box = document.getElementById('elmXmlBox');
        if (box) {
            box.style.display = 'block';
            box.textContent = _elmXml;
            const r = document.createRange();
            r.selectNodeContents(box);
            const sel = window.getSelection();
            sel.removeAllRanges(); sel.addRange(r);
            elmHinweis('Kopieren war nicht erlaubt — das XML ist unten markiert, bitte mit Cmd+C kopieren.', true);
        } else {
            elmHinweis(`Kopieren fehlgeschlagen: ${e.message}`, true);
        }
    }
}

// Einen einzelnen Monat gegen eine ausgewaehlte Referenz halten (Walter 27.09.2026).
// Ein Monat, eine Datei — so, wie die Zertifizierung Monat fuer Monat laeuft.
function elmReferenzWaehlen() {
    if (!_elmJahr) { elmHinweis('Bitte zuerst «Meldung erzeugen».', true); return; }
    let inp = document.getElementById('elmRefEinzel');
    if (!inp) {
        inp = document.createElement('input');
        inp.type = 'file';
        inp.id = 'elmRefEinzel';
        inp.accept = '.xml,text/xml,application/xml';
        inp.style.display = 'none';
        inp.addEventListener('change', async () => {
            const datei = (inp.files || [])[0];
            inp.value = '';
            if (datei) await elmMitReferenzVergleichen(datei);
        });
        document.body.appendChild(inp);
    }
    inp.click();
}

async function elmMitReferenzVergleichen(datei) {
    elmHinweis(`${datei.name} wird mit ${String(_elmMonat).padStart(2,'0')}.${_elmJahr} verglichen…`, false);
    try {
        const fd = new FormData();
        fd.append('referenz', datei, datei.name);
        const res = await fetch(`/api/elm/monthly/${_elmJahr}/${_elmMonat}/vergleichen`, {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken },
            body: fd
        });
        const d = await res.json();
        if (!res.ok) throw new Error(d.message || d.error || ('HTTP ' + res.status));
        _elmXml = d.xml || _elmXml;
        elmZeigeErgebnis(d, _elmJahr, _elmMonat);
        const v = d.vergleich;
        if (v && !v.fehler)
            elmHinweis(v.offen === 0
                ? `${String(_elmMonat).padStart(2,'0')}.${_elmJahr} deckt sich mit der Referenz (${v.bewusst} bewusste Abweichungen).`
                : `${v.offen} offene Unterschiede gegenüber ${v.referenz}.`, v.offen > 0);
    } catch (e) {
        elmHinweis(`Vergleich fehlgeschlagen: ${e.message}`, true);
    }
}
