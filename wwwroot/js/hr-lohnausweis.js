// ══════════════════════════════════════════════════════════════════════
// hr-lohnausweis.js — Jahres-Lohnausweis (ESTV Form 11 dfe)
// Phase 1: MA + Jahr wählen → Vorschau-Modal mit editierbaren Werten
//          → PDF aus dem Backend generieren.
// ══════════════════════════════════════════════════════════════════════
let _laAllEmployees = [];
let _laCurrentData  = null;     // letzter Preview-Stand (LohnausweisData)
let _laCurrentEmp   = null;     // { id, name, employeeNumber }
let _laCurrentYear  = null;

async function laInit() {
    // Filial-Info anzeigen
    const infoEl = document.getElementById('laBranchInfo');
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
    if (infoEl) {
        const branch = (typeof allBranches !== 'undefined' && Array.isArray(allBranches) && fixedCompanyProfileId)
            ? allBranches.find(b => b.id === fixedCompanyProfileId)
            : null;
        let html = '';
        if (branch) {
            const bn = branch.branchName || branch.companyName || '–';
            const code = branch.restaurantCode ? '#' + branch.restaurantCode + ' · ' : '';
            html = `<b>Filiale:</b> ${code}${bn}`;
        } else {
            html = `<span style="color:#92400e">Keine Filiale gewählt — bitte oben links wählen.</span>`;
        }
        // Hinweis falls eingeloggter User noch keine Unterschrift hinterlegt hat
        try {
            if (currentUser?.id) {
                const sigRes = await fetch(`/api/users/${currentUser.id}/signature?_=${Date.now()}`,
                                            { headers: ah(), cache: 'no-store' });
                if (!sigRes.ok) {
                    html += `<div style="margin-top:8px;padding:8px 10px;background:#fffbeb;border:1px solid #fde68a;color:#92400e;font-size:12px;border-radius:6px">
                        Hinweis: Du hast noch keine Unterschrift hinterlegt — die Unterschriften-Stelle im PDF bleibt leer. Im Benutzerprofil kannst du eine Unterschrift hochladen.
                    </div>`;
                }
            }
        } catch {}
        infoEl.innerHTML = html;
    }

    // Default-Jahr = letztes Jahr (Lohnausweis wird typisch im Januar/Februar für Vorjahr generiert)
    const yrInput = document.getElementById('laJahr');
    if (yrInput && !yrInput.value) {
        const today = new Date();
        yrInput.value = (today.getMonth() < 6) ? (today.getFullYear() - 1) : today.getFullYear();
    }

    // Mitarbeiter laden — leichter Lookup-Cache (Walter 14.06.2026).
    try { _laAllEmployees = await loadEmployeeLookup(); }
    catch { _laAllEmployees = []; }

    laRenderEmpList();
}

function laRenderEmpList() {
    const sel    = document.getElementById('laEmpSelect');
    const filter = document.getElementById('laEmpFilter')?.value || 'active';
    const search = (document.getElementById('laEmpSearch')?.value || '').toLowerCase().trim();
    if (!sel) return;

    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;

    // Walter-Bug 16.08.2026: massgebend ist der LAUFENDE Vertrag — ein MA,
    // der frueher hier war (alter beendeter Vertrag), gehoert nicht mehr in
    // die Liste dieser Filiale. Nur wenn KEIN laufender Vertrag existiert
    // (Ausgetretene), zaehlt der letzte Vertrag ueberhaupt.
    const inThisBranch = (e) => {
        if (!cid) return true;
        const emps = e.employments || [];
        if (emps.length === 0) return true;
        const heute = new Date().toISOString().slice(0, 10);
        const laufend = emps.filter(v => v.isActive
            && (!v.contractEndDate || String(v.contractEndDate).slice(0, 10) >= heute));
        const relevant = laufend.length ? laufend : emps;
        return relevant.some(v => v.companyProfileId === cid || v.companyProfileId == null);
    };

    let list = _laAllEmployees.filter(inThisBranch);
    if (filter === 'active')   list = list.filter(e => e.isActive);
    if (filter === 'inactive') list = list.filter(e => !e.isActive);

    if (search) {
        list = list.filter(e =>
            (`${e.firstName||''} ${e.lastName||''}`.toLowerCase().includes(search)) ||
            (e.employeeNumber || '').toLowerCase().includes(search)
        );
    }

    // Sortierung NACH VORNAME (Projekt-Konvention)
    list.sort((a, b) => {
        const f = (a.firstName||'').localeCompare(b.firstName||'');
        if (f !== 0) return f;
        return (a.lastName||'').localeCompare(b.lastName||'');
    });

    const opts = list.map(e => {
        const inactiveTag = e.isActive ? '' : ' · (inaktiv)';
        const nr = e.employeeNumber ? ` · ${e.employeeNumber}` : '';
        const name = `${e.firstName||''} ${e.lastName||''}`.trim();
        return `<option value="${e.id}">${name}${nr}${inactiveTag}</option>`;
    }).join('');

    sel.innerHTML = opts || `<option disabled>Keine Mitarbeiter gefunden</option>`;
    laUpdateGenerateState();
}

function laUpdateGenerateState() {
    const sel  = document.getElementById('laEmpSelect');
    const btn  = document.getElementById('laGenerateBtn');
    const hint = document.getElementById('laSelectedHint');
    if (!sel || !btn) return;
    const empId = parseInt(sel.value, 10);
    const yr    = parseInt(document.getElementById('laJahr')?.value, 10);
    const ok = Number.isFinite(empId) && empId > 0 && Number.isFinite(yr) && yr >= 2020;
    btn.disabled = !ok;
    if (hint) {
        if (ok) {
            const opt = sel.options[sel.selectedIndex];
            hint.textContent = `Gewählt: ${opt?.textContent || ''} — Jahr ${yr}`;
        } else {
            hint.textContent = 'Mitarbeiter und Jahr wählen';
        }
    }
}

// ──────────────────────────────────────────────────────────────────────
// VORSCHAU-MODAL
// ──────────────────────────────────────────────────────────────────────
async function laOpenPreview() {
    const sel = document.getElementById('laEmpSelect');
    const empId = parseInt(sel?.value, 10);
    const year = parseInt(document.getElementById('laJahr')?.value, 10);
    if (!Number.isFinite(empId) || !Number.isFinite(year)) return;

    const btn = document.getElementById('laGenerateBtn');
    if (btn) btn.disabled = true;

    try {
        const r = await fetch(`/api/lohnausweis/${empId}/${year}/preview`, { headers: ah() });
        if (!r.ok) {
            const errJson = await r.json().catch(() => ({}));
            alert('Vorschau fehlgeschlagen: ' + (errJson.error || r.statusText));
            return;
        }
        const payload = await r.json();
        _laCurrentData = payload.data || {};
        _laCurrentYear = year;
        const opt = sel.options[sel.selectedIndex];
        _laCurrentEmp = { id: empId, name: opt?.textContent || '' };

        const sub = document.getElementById('laPreviewSub');
        if (sub) {
            let txt = `Jahr ${year} — ${payload.anzahlMonate} Lohnabrechnungen aggregiert`;
            if (payload.data?.anzahlLohnausweise > 1)
                txt += ` — ${payload.data.anzahlLohnausweise} Lohnausweise in diesem Jahr`;
            sub.textContent = txt;
        }

        laRenderForm();
        document.getElementById('laPreviewModal').style.display = 'block';
    } catch (ex) {
        alert('Fehler beim Laden der Vorschau: ' + ex.message);
    } finally {
        if (btn) btn.disabled = false;
    }
}

function laPreviewClose() {
    document.getElementById('laPreviewModal').style.display = 'none';
    _laCurrentData = null;
}

// Render alle Felder als bearbeitbare Inputs in #laFormGrid
function laRenderForm() {
    const d = _laCurrentData || {};
    const grid = document.getElementById('laFormGrid');
    if (!grid) return;

    // Sektionen: Header / Beträge / Bemerkungen / Bestätigung
    grid.innerHTML = `
      <div class="la-abschnitt">EMPFÄNGER &amp; PERIODE</div>

      ${laTextarea('empfaengerAdresse', 'Empfänger-Adresse (Mitarbeiter)', d.empfaengerAdresse, 2)}
      ${laTextarea('mitarbeiterNameAdresse', 'Ziffer D — Name + Adresse', d.mitarbeiterNameAdresse, 2)}
      ${laInput('ahvNummer', 'Ziffer C — AHV-Nummer (756.XXXX.XXXX.XX)', d.ahvNummer, 'la-ahv')}
      ${laInput('geburtsdatum', 'Ziffer C — Geburtsdatum (dd.mm.yyyy)', d.geburtsdatum, 'la-kurz')}
      ${laInput('periodeVon', 'Ziffer E — Periode von', d.periodeVon, 'la-kurz')}
      ${laInput('periodeBis', 'Ziffer E — Periode bis', d.periodeBis, 'la-kurz')}
      ${laInput('heimatort', 'Ziffer I — Heimatort', d.heimatort)}

      <div class="la-abschnitt">CHECKBOXEN</div>
      ${laCheckbox('istGanzesJahr', 'Ganzes Jahr beschäftigt', d.istGanzesJahr)}
      ${laCheckbox('istLohnausweis', 'Buchstabe A — Lohnausweis (aus = Rentenbescheinigung, Buchstabe B)', d.istLohnausweis)}
      ${laCheckbox('boxFFreierTransport', 'Box F — Unentgeltliche Beförderung Wohn-/Arbeitsort', d.boxFFreierTransport)}
      ${laCheckbox('boxGKantineGratis', 'Box G — Kantinenverpflegung gratis', d.boxGKantineGratis)}

      <div class="la-abschnitt">LOHN (Ziffer 1–8)</div>
      ${laMoney('ziffer1Lohn', 'Ziffer 1 — Lohn', d.ziffer1Lohn)}
      ${laMoney('ziffer21VerpflegungUnterkunft', 'Ziffer 2.1 — Verpflegung/Unterkunft', d.ziffer21VerpflegungUnterkunft)}
      ${laMoney('ziffer22PrivatanteilFahrzeug', 'Ziffer 2.2 — Privatanteil Fahrzeug', d.ziffer22PrivatanteilFahrzeug)}
      ${laMoney('ziffer23AndereGehaltsnebenleistungen', 'Ziffer 2.3 — Andere Nebenleistungen', d.ziffer23AndereGehaltsnebenleistungen)}
      ${laInput('ziffer23Art', 'Ziffer 2.3 — Art', d.ziffer23Art)}
      ${laMoney('ziffer3Unregelmaessige', 'Ziffer 3 — Unregelmässige Leistungen', d.ziffer3Unregelmaessige)}
      ${laInput('ziffer3Art', 'Ziffer 3 — Art', d.ziffer3Art)}
      ${laMoney('ziffer4Kapitalleistungen', 'Ziffer 4 — Kapitalleistungen', d.ziffer4Kapitalleistungen)}
      ${laInput('ziffer4Art', 'Ziffer 4 — Art', d.ziffer4Art)}
      ${laMoney('ziffer5Beteiligungsrechte', 'Ziffer 5 — Beteiligungsrechte', d.ziffer5Beteiligungsrechte)}
      ${laMoney('ziffer6VrEntschaedigung', 'Ziffer 6 — VR-Entschädigung', d.ziffer6VrEntschaedigung)}
      ${laMoney('ziffer7AndereLeistungen', 'Ziffer 7 — Andere Leistungen', d.ziffer7AndereLeistungen)}
      ${laInput('ziffer7Art', 'Ziffer 7 — Art', d.ziffer7Art)}
      ${laMoney('ziffer8BruttoTotal', 'Ziffer 8 — Bruttoeinkommen Total', d.ziffer8BruttoTotal)}

      <div class="la-abschnitt">ABZÜGE (Ziffer 9–12)</div>
      ${laMoney('ziffer9AhvIvEoAlvNbu', 'Ziffer 9 — AHV/IV/EO/ALV/NBU', d.ziffer9AhvIvEoAlvNbu)}
      ${laMoney('ziffer101BvgOrdentlich', 'Ziffer 10.1 — BVG ordentlich', d.ziffer101BvgOrdentlich)}
      ${laMoney('ziffer102BvgEinkauf', 'Ziffer 10.2 — BVG Einkauf', d.ziffer102BvgEinkauf)}
      ${laMoney('ziffer11Nettolohn', 'Ziffer 11 — Nettolohn', d.ziffer11Nettolohn)}
      ${laMoney('ziffer12Quellensteuer', 'Ziffer 12 — Quellensteuer-Abzug', d.ziffer12Quellensteuer)}

      <div class="la-abschnitt">SPESENVERGÜTUNGEN (Ziffer 13)</div>
      ${laCheckbox('ziffer1311EffektivOhneBeleg', 'Ziffer 13.1.1 — Spesen effektiv ohne Beleg', d.ziffer1311EffektivOhneBeleg)}
      ${laMoney('ziffer1311SpesenEffektivBetrag', 'Ziffer 13.1.1 — Betrag', d.ziffer1311SpesenEffektivBetrag)}
      ${laMoney('ziffer1312SpesenPauschal', 'Ziffer 13.1.2 — Spesen pauschal', d.ziffer1312SpesenPauschal)}
      ${laInput('ziffer1312Art', 'Ziffer 13.1.2 — Art', d.ziffer1312Art)}
      ${laMoney('ziffer1321Repraesentation', 'Ziffer 13.2.1 — Repräsentation', d.ziffer1321Repraesentation)}
      ${laMoney('ziffer1322Autopauschale', 'Ziffer 13.2.2 — Auto-Pauschale', d.ziffer1322Autopauschale)}
      ${laMoney('ziffer1323AnderePauschalen', 'Ziffer 13.2.3 — Andere Pauschalen', d.ziffer1323AnderePauschalen)}
      ${laInput('ziffer1323Art', 'Ziffer 13.2.3 — Art', d.ziffer1323Art)}
      ${laMoney('ziffer133AusWeiterbildung', 'Ziffer 13.3 — Aus-/Weiterbildung', d.ziffer133AusWeiterbildung)}

      <div class="la-abschnitt">BEMERKUNGEN (Ziffer 14 + 15 — 4 Zeilen)</div>
      ${laInput('ziffer141Bemerkungen', 'Bemerkung Zeile 1', d.ziffer141Bemerkungen, 'la-breit')}
      ${laInput('ziffer142Bemerkungen', 'Bemerkung Zeile 2', d.ziffer142Bemerkungen, 'la-breit')}
      ${laInput('ziffer151Bemerkungen', 'Bemerkung Zeile 3', d.ziffer151Bemerkungen, 'la-breit')}
      ${laInput('ziffer152Bemerkungen', 'Bemerkung Zeile 4', d.ziffer152Bemerkungen, 'la-breit')}

      <div class="la-abschnitt">ORT, DATUM &amp; UNTERSCHRIFT</div>
      ${laInput('ziffer151Ort', 'Ort und Datum — Ort', d.ziffer151Ort)}
      ${laInput('ziffer152Datum', 'Ort und Datum — Datum (dd.mm.yyyy)', d.ziffer152Datum, 'la-kurz')}
      ${laTextarea('bestaetigungAgBlock', 'Bestätigungs-Block AG (UID, Firma, Adresse)', d.bestaetigungAgBlock, 2, true)}
    `;
}

// ── Form-Helper ───────────────────────────────────────────────────────
function laInput(name, label, value, breite) {
    const v = (value ?? '');
    return `<span class="la-lbl">${label}</span>
        <input type="text" id="la_${name}" value="${laEscape(String(v))}"
               class="la-inp ${breite || 'la-text'}">`;
}
function laGeldText(value) {
    if (value === null || value === undefined || value === '') return '';
    const n = typeof value === 'number'
        ? value
        : parseFloat(String(value).replace(/'/g, '').replace(',', '.'));
    return Number.isFinite(n) ? n.toFixed(2) : '';
}
function laGeldNorm(el) {
    el.value = laGeldText(String(el.value || '').trim());
}
function laMoney(name, label, value) {
    return `<span class="la-lbl">${label}</span>
        <input type="text" inputmode="decimal" autocomplete="off" id="la_${name}" value="${laEscape(laGeldText(value))}"
               class="la-inp la-geld" onblur="laGeldNorm(this)">`;
}
function laTextarea(name, label, value, rows, ganz) {
    const v = (value ?? '');
    return `<label class="la-adresse${ganz ? ' la-ganz' : ''}">
        <span class="la-lbl">${label}</span>
        <textarea id="la_${name}" rows="${rows||2}" class="la-area">${laEscape(String(v))}</textarea>
    </label>`;
}
function laCheckbox(name, label, value) {
    return `<label class="la-check">
        <input type="checkbox" id="la_${name}" ${value ? 'checked' : ''}>
        <span>${label}</span>
    </label>`;
}
function laEscape(s) {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

// Liest die Werte aus dem Form zurück in ein LohnausweisData-Objekt (camelCase Keys)
function laCollectFormData() {
    const grab = (name) => document.getElementById('la_' + name);
    const str   = (name) => grab(name)?.value || null;
    const money = (name) => {
        const el = grab(name);
        const v = el?.value;
        if (v === undefined || v === null || v === '') return null;
        const n = parseFloat(String(v).replace(/'/g, '').replace(',', '.'));
        return Number.isFinite(n) ? n : null;
    };
    const bool  = (name) => !!grab(name)?.checked;

    // C# DTO erwartet PascalCase Property-Namen — System.Text.Json mit Default-
    // PropertyNameCaseInsensitive=true akzeptiert beide, aber wir senden camelCase
    // (matched dem Default JsonSerializerOptions in ASP.NET Core 8).
    return {
        // Ziffer D (Steuerjahr) aus der Jahr-Auswahl — fehlte im Payload,
        // darum blieb das D-Feld im PDF leer (Walter-Bug 16.08.2026).
        jahr:                     String(parseInt(document.getElementById('laJahr')?.value, 10) || ''),
        empfaengerAdresse:        str('empfaengerAdresse'),
        istGanzesJahr:            bool('istGanzesJahr'),
        istLohnausweis:           bool('istLohnausweis'),
        ahvNummer:                str('ahvNummer'),
        geburtsdatum:             str('geburtsdatum'),
        mitarbeiterNameAdresse:   str('mitarbeiterNameAdresse'),
        periodeVon:               str('periodeVon'),
        periodeBis:               str('periodeBis'),
        boxFFreierTransport:      bool('boxFFreierTransport'),
        boxGKantineGratis:        bool('boxGKantineGratis'),
        heimatort:                str('heimatort'),

        ziffer1Lohn:                              money('ziffer1Lohn'),
        ziffer21VerpflegungUnterkunft:            money('ziffer21VerpflegungUnterkunft'),
        ziffer22PrivatanteilFahrzeug:             money('ziffer22PrivatanteilFahrzeug'),
        ziffer23AndereGehaltsnebenleistungen:     money('ziffer23AndereGehaltsnebenleistungen'),
        ziffer23Art:                              str('ziffer23Art'),
        ziffer3Unregelmaessige:                   money('ziffer3Unregelmaessige'),
        ziffer3Art:                               str('ziffer3Art'),
        ziffer4Kapitalleistungen:                 money('ziffer4Kapitalleistungen'),
        ziffer4Art:                               str('ziffer4Art'),
        ziffer5Beteiligungsrechte:                money('ziffer5Beteiligungsrechte'),
        ziffer6VrEntschaedigung:                  money('ziffer6VrEntschaedigung'),
        ziffer7AndereLeistungen:                  money('ziffer7AndereLeistungen'),
        ziffer7Art:                               str('ziffer7Art'),
        ziffer8BruttoTotal:                       money('ziffer8BruttoTotal'),

        ziffer9AhvIvEoAlvNbu:                     money('ziffer9AhvIvEoAlvNbu'),
        ziffer101BvgOrdentlich:                   money('ziffer101BvgOrdentlich'),
        ziffer102BvgEinkauf:                      money('ziffer102BvgEinkauf'),
        ziffer11Nettolohn:                        money('ziffer11Nettolohn'),
        ziffer12Quellensteuer:                    money('ziffer12Quellensteuer'),

        ziffer1311EffektivOhneBeleg:              bool('ziffer1311EffektivOhneBeleg'),
        ziffer1311SpesenEffektivBetrag:           money('ziffer1311SpesenEffektivBetrag'),
        ziffer1312SpesenPauschal:                 money('ziffer1312SpesenPauschal'),
        ziffer1312Art:                            str('ziffer1312Art'),
        ziffer1321Repraesentation:                money('ziffer1321Repraesentation'),
        ziffer1322Autopauschale:                  money('ziffer1322Autopauschale'),
        ziffer1323AnderePauschalen:               money('ziffer1323AnderePauschalen'),
        ziffer1323Art:                            str('ziffer1323Art'),
        ziffer133AusWeiterbildung:                money('ziffer133AusWeiterbildung'),

        ziffer141Bemerkungen:                     str('ziffer141Bemerkungen'),
        ziffer142Bemerkungen:                     str('ziffer142Bemerkungen'),
        ziffer151Bemerkungen:                     str('ziffer151Bemerkungen'),
        ziffer152Bemerkungen:                     str('ziffer152Bemerkungen'),
        ziffer151Ort:                             str('ziffer151Ort'),
        ziffer152Datum:                           str('ziffer152Datum'),
        bestaetigungAgBlock:                      str('bestaetigungAgBlock'),
    };
}

// ──────────────────────────────────────────────────────────────────────
// PDF-GENERIERUNG: POST an Backend mit aktuellem Form-Stand
// ──────────────────────────────────────────────────────────────────────
async function laGeneratePdf(final = false, ohneFrage = false) {
    if (!_laCurrentEmp?.id || !_laCurrentYear) return;
    // Finalisierung (Walter 16.08.2026): erster Final-Druck vergibt die
    // definitive DocID (UUID) + CreationDate — Wiederdrucke sind identisch.
    if (final && !ohneFrage) {
        const ok = await liquidConfirm(
            `Finalen Lohnausweis ${_laCurrentYear} für ${_laCurrentEmp.name} erzeugen? `
            + `Beim ersten Mal werden DocID und Erstellungsdatum definitiv vergeben; `
            + `spätere Ausdrucke tragen dieselbe Identifikation.`,
            { title: 'Lohnausweis finalisieren', yesLabel: 'Final erzeugen', noLabel: 'Abbrechen' });
        if (!ok) return;
    }
    const payload = laCollectFormData();
    try {
        const r = await fetch(`/api/lohnausweis/${_laCurrentEmp.id}/${_laCurrentYear}/pdf${final ? '?final=true' : ''}`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
        });
        if (!r.ok) {
            const errJson = await r.json().catch(() => ({}));
            alert((final ? 'Finaler Lohnausweis blockiert:\n' : 'PDF-Erstellung fehlgeschlagen: ')
                + (errJson.message || errJson.error || r.statusText));
            return;
        }
        const blob = await r.blob();
        const name = (_laCurrentEmp.name || 'Lohnausweis').replace(/[^a-zA-Z0-9_-]/g, '_');
        await previewFileModal(blob, `Lohnausweis_${_laCurrentYear}_${name}${final ? '' : '_ENTWURF'}.pdf`);
    } catch (ex) {
        alert('Fehler beim PDF-Generieren: ' + ex.message);
    }
}

async function laRektifikat() {
    if (!_laCurrentEmp?.id || !_laCurrentYear) return;
    const ok = await liquidConfirm(
        `Den finalen Lohnausweis ${_laCurrentYear} von ${_laCurrentEmp.name} als Rektifikat ersetzen? `
        + 'Es gibt eine neue DocID. In Ziffer 15 steht die DocID des bisherigen Ausweises.',
        { title: 'Rektifikat', yesLabel: 'Rektifikat erstellen', noLabel: 'Abbrechen' });
    if (!ok) return;
    const r = await fetch(`/api/lohnausweis/${_laCurrentEmp.id}/${_laCurrentYear}/rektifikat`, {
        method: 'POST', headers: ah(),
    });
    const body = await r.json().catch(() => ({}));
    if (!r.ok) { alert(body.message || 'Rektifikat fehlgeschlagen.'); return; }
    await laGeneratePdf(true, true);
}

async function laErsatz() {
    const year = parseInt(document.getElementById('laJahr')?.value, 10);
    const filiale = typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null;
    if (!year || !filiale) { alert('Zuerst Jahr und Filiale wählen.'); return; }
    const ok = await liquidConfirm(
        `Alle finalen Lohnausweise ${year} dieser Filiale als Ersatzmeldung ersetzen? `
        + 'Jede Person bekommt eine neue DocID. In Ziffer 15 steht die bisherige.',
        { title: 'Ersatzmeldung', yesLabel: 'Alle ersetzen', noLabel: 'Abbrechen' });
    if (!ok) return;
    const r = await fetch(`/api/lohnausweis/ersatz?year=${year}&companyProfileId=${filiale}`, {
        method: 'POST', headers: ah(),
    });
    const body = await r.json().catch(() => ({}));
    if (!r.ok) { alert(body.message || 'Ersatzmeldung fehlgeschlagen.'); return; }
    alert(body.anzahl + (body.anzahl === 1 ? ' Lohnausweis wurde ersetzt.' : ' Lohnausweise wurden ersetzt.'));
}
