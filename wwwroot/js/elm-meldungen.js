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
    const bereit = _elmMonate.filter(m => m.bereit);
    if (bereit.length === 0) {
        sel.innerHTML = '<option value="">– kein Monat definitiv abgeschlossen –</option>';
        return;
    }
    sel.innerHTML = bereit.map(m => {
        const mm = String(m.monat).padStart(2, '0');
        const ref = m.referenz ? ' · Referenz vorhanden' : '';
        return `<option value="${m.jahr}-${mm}">${mm}.${m.jahr} · ${m.filialen} Filialen${ref}</option>`;
    }).join('');
    const offen = _elmMonate.filter(m => !m.bereit).length;
    elmHinweis(offen > 0
        ? `${bereit.length} Monate sind meldebereit. ${offen} weitere sind noch nicht in allen Filialen definitiv abgeschlossen und darum nicht wählbar.`
        : `${bereit.length} Monate sind meldebereit.`, false);
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

function elmXmlHerunterladen(jahr, monat) {
    if (!_elmXml) return;
    const name = `DeclareMonthlySalary_${jahr}-${String(monat).padStart(2, '0')}.xml`;
    // Walter-Vorgabe 21.05.2026: nie still herunterladen — «Speichern unter…».
    saveBlobAsk(new Blob([_elmXml], { type: 'application/xml' }), name);
}
