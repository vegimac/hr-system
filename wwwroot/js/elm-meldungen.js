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
    const name = `DeclareMonthlySalary_${jahr}-${String(monat).padStart(2, '0')}.xml`;
    const kb = Math.max(1, Math.round(_elmXml.length / 1024));
    // Walter-Vorgabe 21.05.2026: nie still herunterladen — «Speichern unter…».
    // Walter 27.09.2026: aber auch nie still SCHEITERN — jeder Ausgang wird gesagt.
    try {
        const wie = await saveBlobAsk(new Blob([_elmXml], { type: 'application/xml' }), name);
        if (wie === 'abgebrochen')
            elmHinweis('Speichern abgebrochen — die Datei wurde nicht abgelegt.', true);
        else if (wie === 'fallback')
            elmHinweis(`${name} (${kb} KB) liegt im Download-Ordner. Der «Speichern unter…»-Dialog `
                     + `war nicht verfügbar${window._saveBlobGrund ? ' (' + window._saveBlobGrund + ')' : ''}.`, false);
        else
            elmHinweis(`${name} (${kb} KB) gespeichert.`, false);
    } catch (e) {
        elmHinweis(`Herunterladen fehlgeschlagen: ${e.message}`, true);
    }
}

// ── Durchgang über ALLE Monate mit Referenz (Walter 27.09.2026) ──────────────
// Der Einzelmonat sagt einem, ob ein Monat stimmt. Erst der Durchgang über alle
// zeigt, ob ein Fehler einmalig ist oder sich durch das Jahr zieht.

let _elmAlleBericht = '';

async function elmAllePruefen() {
    const d = await elmAlleHolen(null);
    if (!d) return;
    // Die Referenzen von Swissdec liegen im Quellcode, nicht im veröffentlichten
    // Programm (und 7 MB Übungsdaten gehören auch nicht auf die Lohnanlage).
    // Also fragen wir sie beim Benutzer ab (Walter 27.09.2026).
    if (d.dateienNoetig) { elmReferenzenWaehlen(d.hinweis); return; }
    elmZeigeAlle(d);
}

function elmReferenzenWaehlen(hinweis) {
    const box = document.getElementById('elmAlleErgebnis');
    if (box) {
        box.style.display = 'block';
        box.innerHTML = `<div style="font-size:12.5px;color:#646464;line-height:1.6">${elmEsc(hinweis || '')}<br>
            Der Ordner liegt in deinem Projekt unter <code>SWISSCEC/RefXML</code>. Im Auswahlfenster
            alle <code>RefXML_…_MONTHLY.xml</code> auf einmal markieren (Cmd+A und dann die anderen abwählen,
            oder ins Suchfeld <code>MONTHLY</code> tippen). Die Dateien werden nur für diese Prüfung gelesen
            und nirgends gespeichert.</div>
            <div style="margin-top:12px"><button type="button" class="btn-primary"
                style="font-size:12.5px;padding:7px 15px" onclick="elmReferenzenDateiwahl()">Referenzen auswählen…</button></div>`;
    }
    elmReferenzenDateiwahl();
}

function elmReferenzenDateiwahl() {
    let inp = document.getElementById('elmRefDateien');
    if (!inp) {
        inp = document.createElement('input');
        inp.type = 'file';
        inp.id = 'elmRefDateien';
        inp.multiple = true;
        inp.accept = '.xml,text/xml,application/xml';
        inp.style.display = 'none';
        inp.addEventListener('change', async () => {
            const dateien = Array.from(inp.files || []);
            inp.value = '';
            if (dateien.length === 0) return;
            const d = await elmAlleHolen(dateien);
            if (d) elmZeigeAlle(d);
        });
        document.body.appendChild(inp);
    }
    inp.click();
}

// dateien = null -> Referenzen von der Platte des Servers; sonst hochladen.
async function elmAlleHolen(dateien) {
    const btn = document.getElementById('elmAlleBtn');
    const box = document.getElementById('elmAlleErgebnis');
    if (btn) { btn.disabled = true; btn.textContent = 'prüft alle Monate…'; }
    elmZeigeErgebnis(null);
    if (box && dateien) {
        box.style.display = 'block';
        box.innerHTML = `<div style="font-size:12.5px;color:#646464">${dateien.length} Referenzen gelesen. `
                      + 'Jeder Monat wird erzeugt, gegen das Schema geprüft und Feld für Feld verglichen. '
                      + 'Das dauert einen Moment.</div>';
    }
    try {
        let res;
        if (dateien) {
            const fd = new FormData();
            for (const f of dateien) fd.append('dateien', f, f.name);
            res = await fetch('/api/elm/monthly/alle-pruefen', {
                method: 'POST',
                headers: { 'Authorization': 'Bearer ' + localStorage.hrToken },
                body: fd
            });
        } else {
            res = await fetch('/api/elm/monthly/alle-pruefen', {
                headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
            });
        }
        const d = await res.json();
        if (!res.ok) throw new Error(d.message || d.error || ('HTTP ' + res.status));
        _elmAlleBericht = d.bericht || '';
        return d;
    } catch (e) {
        _elmAlleBericht = '';
        if (box) {
            box.style.display = 'block';
            box.innerHTML = `<div style="font-size:12.5px;color:#b91c1c">Fehler: ${elmEsc(e.message)}</div>`;
        }
        return null;
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'Alle Monate prüfen'; }
    }
}

function elmZeigeAlle(d) {
    const box = document.getElementById('elmAlleErgebnis');
    if (!box) return;
    const z = d.zusammenfassung || {};
    if (d.hinweis) { box.innerHTML = `<div style="font-size:12.5px;color:#646464">${elmEsc(d.hinweis)}</div>`; return; }

    const pille = (text, farbe) =>
        `<span style="display:inline-block;padding:3px 11px;border-radius:999px;font-size:12px;font-weight:600;background:${farbe.bg};color:${farbe.fg}">${text}</span>`;
    const gruen = { bg: '#dcfce7', fg: '#166534' };
    const rot   = { bg: '#fee2e2', fg: '#991b1b' };
    const grau  = { bg: 'rgba(60,55,48,0.10)', fg: '#3f3f3f' };

    let html = `<div style="display:flex;gap:10px;flex-wrap:wrap;align-items:center;margin-bottom:12px">
        ${pille(`${z.fertig} von ${z.gesamt} Monaten erzeugt`, grau)}
        ${pille(z.mitOffenen ? `${z.mitOffenen} mit offenen Unterschieden` : 'alle deckungsgleich',
                z.mitOffenen ? rot : gruen)}
        ${z.nichtBereit ? pille(`${z.nichtBereit} noch nicht meldebereit`, grau) : ''}
      </div>`;

    html += `<div style="border:1px solid rgba(60,55,48,0.14);border-radius:10px;overflow:auto;max-height:420px">
      <table style="width:100%;border-collapse:collapse;font-size:12.5px">
        <thead><tr>
          <th style="text-align:left;padding:7px 10px;position:sticky;top:0;background:#efece5">Monat</th>
          <th style="text-align:right;padding:7px 10px;position:sticky;top:0;background:#efece5">Personen</th>
          <th style="text-align:right;padding:7px 10px;position:sticky;top:0;background:#efece5">Felder</th>
          <th style="text-align:right;padding:7px 10px;position:sticky;top:0;background:#efece5">Offen</th>
          <th style="text-align:right;padding:7px 10px;position:sticky;top:0;background:#efece5">Bewusst</th>
          <th style="text-align:left;padding:7px 10px;position:sticky;top:0;background:#efece5">Schema</th>
        </tr></thead><tbody>`;

    for (const m of (d.monate || [])) {
        const mm = String(m.monat).padStart(2, '0');
        if (!m.bereit || m.grund) {
            html += `<tr style="border-top:1px solid rgba(60,55,48,0.10)">
                <td style="padding:6px 10px">${mm}.${m.jahr}</td>
                <td colspan="5" style="padding:6px 10px;color:#8b8b8b">${elmEsc(m.grund || 'nicht bereit')}</td></tr>`;
            continue;
        }
        const offenFarbe = m.offen > 0 ? '#b91c1c' : '#166534';
        html += `<tr style="border-top:1px solid rgba(60,55,48,0.10)">
            <td style="padding:6px 10px;font-weight:600">${mm}.${m.jahr}</td>
            <td style="padding:6px 10px;text-align:right">${m.personen} von ${m.personenSoll}</td>
            <td style="padding:6px 10px;text-align:right;color:#646464">${m.geprueft}</td>
            <td style="padding:6px 10px;text-align:right;font-weight:600;color:${offenFarbe}">${m.offen === 0 ? '—' : m.offen}</td>
            <td style="padding:6px 10px;text-align:right;color:#646464">${m.bewusst}</td>
            <td style="padding:6px 10px;color:${m.xsdFehler ? '#b91c1c' : '#646464'}">${m.xsdFehler ? m.xsdFehler + ' Fehler' : 'in Ordnung'}</td>
          </tr>`;
        for (const u of (m.unterschiede || [])) {
            html += `<tr style="background:rgba(185,28,28,0.05)">
                <td style="padding:4px 10px"></td>
                <td style="padding:4px 10px;font-size:11.5px">${elmEsc(u.person)}</td>
                <td colspan="2" style="padding:4px 10px;font-size:11.5px;color:#646464">${elmEsc(u.feld)}</td>
                <td style="padding:4px 10px;font-size:11.5px;text-align:right">${elmEsc(u.ist ?? '—')}</td>
                <td style="padding:4px 10px;font-size:11.5px">statt ${elmEsc(u.soll ?? '—')}</td>
              </tr>`;
        }
    }
    html += `</tbody></table></div>`;

    if ((d.uebergangen || []).length > 0) {
        html += `<div style="margin-top:12px"><div style="font-weight:600;font-size:12.5px;margin-bottom:5px">Nicht verwendete Dateien</div>
            <ul style="margin:0;padding-left:18px;font-size:12px;color:#646464">
            ${d.uebergangen.map(u => `<li>${elmEsc(u)}</li>`).join('')}</ul></div>`;
    }

    if (_elmAlleBericht) {
        html += `<div style="display:flex;gap:10px;margin-top:14px;justify-content:flex-end">
            <button type="button" class="kd-btn-glass" style="font-size:12.5px;padding:7px 15px;border-radius:10px"
                    onclick="elmAlleBerichtHerunterladen()">Bericht herunterladen</button>
          </div>
          <details style="margin-top:10px"><summary style="cursor:pointer;font-size:12.5px;color:#646464">Bericht ansehen</summary>
            <pre style="white-space:pre-wrap;font-size:11.5px;background:rgba(60,55,48,0.05);padding:12px;border-radius:10px;margin-top:8px;max-height:420px;overflow:auto">${elmEsc(_elmAlleBericht)}</pre></details>`;
    }
    box.innerHTML = html;
}

function elmAlleBerichtHerunterladen() {
    if (!_elmAlleBericht) return;
    const heute = new Date().toISOString().slice(0, 10);
    // Walter-Vorgabe 21.05.2026: nie still herunterladen — «Speichern unter…».
    saveBlobAsk(new Blob([_elmAlleBericht], { type: 'text/markdown' }), `elm_monatsmeldungen_${heute}.md`);
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
