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
                        Hinweis: Sie haben noch keine Unterschrift hinterlegt — die Unterschriften-Stelle im PDF bleibt leer. Im Benutzerprofil kann eine Unterschrift hochgeladen werden.
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

    // Mitarbeiter laden
    try {
        const r = await fetch('/api/employees', { headers: ah() });
        _laAllEmployees = r.ok ? await r.json() : [];
    } catch { _laAllEmployees = []; }

    laRenderEmpList();
}

function laRenderEmpList() {
    const sel    = document.getElementById('laEmpSelect');
    const filter = document.getElementById('laEmpFilter')?.value || 'active';
    const search = (document.getElementById('laEmpSearch')?.value || '').toLowerCase().trim();
    if (!sel) return;

    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;

    const inThisBranch = (e) => {
        if (!cid) return true;
        const emps = e.employments || [];
        if (emps.length === 0) return true;
        return emps.some(v => v.companyProfileId === cid || v.companyProfileId == null);
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
        if (sub) sub.textContent = `${_laCurrentEmp.name} — Jahr ${year} — ${payload.anzahlMonate} Lohnabrechnungen aggregiert`;

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
      <div style="grid-column:1 / -1">
        <div style="font-size:11px;font-weight:700;color:#64748b;letter-spacing:.06em;margin-bottom:8px;padding-bottom:4px;border-bottom:1px solid #f1f5f9">EMPFÄNGER &amp; PERIODE</div>
      </div>

      ${laTextarea('empfaengerAdresse', 'Empfänger-Adresse (Mitarbeiter)', d.empfaengerAdresse, 3)}
      ${laTextarea('mitarbeiterNameAdresse', 'Ziffer D — Name + Adresse', d.mitarbeiterNameAdresse, 3)}
      ${laInput('ahvNummer', 'Ziffer C — AHV-Nummer (756.XXXX.XXXX.XX)', d.ahvNummer)}
      ${laInput('geburtsdatum', 'Ziffer C — Geburtsdatum (dd.mm.yyyy)', d.geburtsdatum)}
      ${laInput('periodeVon', 'Ziffer E — Periode von', d.periodeVon)}
      ${laInput('periodeBis', 'Ziffer E — Periode bis', d.periodeBis)}
      ${laInput('heimatort', 'Ziffer I — Heimatort', d.heimatort)}

      <div style="grid-column:1 / -1">
        <div style="font-size:11px;font-weight:700;color:#64748b;letter-spacing:.06em;margin:12px 0 8px;padding-bottom:4px;border-bottom:1px solid #f1f5f9">CHECKBOXEN</div>
      </div>
      ${laCheckbox('istGanzesJahr', 'Ziffer A — Ganzes Jahr', d.istGanzesJahr)}
      ${laCheckbox('istLohnausweis', 'Ziffer B — Lohnausweis', d.istLohnausweis)}
      ${laCheckbox('boxFFreierTransport', 'Box F — Unentgeltliche Beförderung Wohn-/Arbeitsort', d.boxFFreierTransport)}
      ${laCheckbox('boxGKantineGratis', 'Box G — Kantinenverpflegung gratis', d.boxGKantineGratis)}

      <div style="grid-column:1 / -1">
        <div style="font-size:11px;font-weight:700;color:#64748b;letter-spacing:.06em;margin:12px 0 8px;padding-bottom:4px;border-bottom:1px solid #f1f5f9">LOHN (Ziffer 1–8)</div>
      </div>
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

      <div style="grid-column:1 / -1">
        <div style="font-size:11px;font-weight:700;color:#64748b;letter-spacing:.06em;margin:12px 0 8px;padding-bottom:4px;border-bottom:1px solid #f1f5f9">ABZÜGE (Ziffer 9–12)</div>
      </div>
      ${laMoney('ziffer9AhvIvEoAlvNbu', 'Ziffer 9 — AHV/IV/EO/ALV/NBU', d.ziffer9AhvIvEoAlvNbu)}
      ${laMoney('ziffer101BvgOrdentlich', 'Ziffer 10.1 — BVG ordentlich', d.ziffer101BvgOrdentlich)}
      ${laMoney('ziffer102BvgEinkauf', 'Ziffer 10.2 — BVG Einkauf', d.ziffer102BvgEinkauf)}
      ${laMoney('ziffer11Nettolohn', 'Ziffer 11 — Nettolohn', d.ziffer11Nettolohn)}
      ${laMoney('ziffer12Quellensteuer', 'Ziffer 12 — Quellensteuer-Abzug', d.ziffer12Quellensteuer)}

      <div style="grid-column:1 / -1">
        <div style="font-size:11px;font-weight:700;color:#64748b;letter-spacing:.06em;margin:12px 0 8px;padding-bottom:4px;border-bottom:1px solid #f1f5f9">SPESENVERGÜTUNGEN (Ziffer 13)</div>
      </div>
      ${laCheckbox('ziffer1311EffektivOhneBeleg', 'Ziffer 13.1.1 — Spesen effektiv ohne Beleg', d.ziffer1311EffektivOhneBeleg)}
      ${laMoney('ziffer1311SpesenEffektivBetrag', 'Ziffer 13.1.1 — Betrag', d.ziffer1311SpesenEffektivBetrag)}
      ${laMoney('ziffer1312SpesenPauschal', 'Ziffer 13.1.2 — Spesen pauschal', d.ziffer1312SpesenPauschal)}
      ${laInput('ziffer1312Art', 'Ziffer 13.1.2 — Art', d.ziffer1312Art)}
      ${laMoney('ziffer1321Repraesentation', 'Ziffer 13.2.1 — Repräsentation', d.ziffer1321Repraesentation)}
      ${laMoney('ziffer1322Autopauschale', 'Ziffer 13.2.2 — Auto-Pauschale', d.ziffer1322Autopauschale)}
      ${laMoney('ziffer1323AnderePauschalen', 'Ziffer 13.2.3 — Andere Pauschalen', d.ziffer1323AnderePauschalen)}
      ${laInput('ziffer1323Art', 'Ziffer 13.2.3 — Art', d.ziffer1323Art)}
      ${laMoney('ziffer133AusWeiterbildung', 'Ziffer 13.3 — Aus-/Weiterbildung', d.ziffer133AusWeiterbildung)}

      <div style="grid-column:1 / -1">
        <div style="font-size:11px;font-weight:700;color:#64748b;letter-spacing:.06em;margin:12px 0 8px;padding-bottom:4px;border-bottom:1px solid #f1f5f9">BEMERKUNGEN (Ziffer 14 + 15 — 4 Zeilen)</div>
      </div>
      ${laInput('ziffer141Bemerkungen', 'Bemerkung Zeile 1', d.ziffer141Bemerkungen)}
      ${laInput('ziffer142Bemerkungen', 'Bemerkung Zeile 2', d.ziffer142Bemerkungen)}
      ${laInput('ziffer151Bemerkungen', 'Bemerkung Zeile 3', d.ziffer151Bemerkungen)}
      ${laInput('ziffer152Bemerkungen', 'Bemerkung Zeile 4', d.ziffer152Bemerkungen)}

      <div style="grid-column:1 / -1">
        <div style="font-size:11px;font-weight:700;color:#64748b;letter-spacing:.06em;margin:12px 0 8px;padding-bottom:4px;border-bottom:1px solid #f1f5f9">ORT, DATUM &amp; UNTERSCHRIFT</div>
      </div>
      ${laInput('ziffer151Ort', 'Ort und Datum — Ort', d.ziffer151Ort)}
      ${laInput('ziffer152Datum', 'Ort und Datum — Datum (dd.mm.yyyy)', d.ziffer152Datum)}
      ${laTextarea('bestaetigungAgBlock', 'Bestätigungs-Block AG (UID, Firma, Adresse)', d.bestaetigungAgBlock, 4)}
    `;
}

// ── Form-Helper ───────────────────────────────────────────────────────
function laInput(name, label, value) {
    const v = (value ?? '');
    return `<label style="font-size:12px;color:#475569;display:block">
        <span style="display:block;margin-bottom:3px">${label}</span>
        <input type="text" id="la_${name}" value="${laEscape(String(v))}"
               style="width:100%;padding:7px 10px;border:1px solid #e2e8f0;border-radius:7px;font-size:13px;background:white">
    </label>`;
}
function laMoney(name, label, value) {
    const v = (value ?? '');
    return `<label style="font-size:12px;color:#475569;display:block">
        <span style="display:block;margin-bottom:3px">${label}</span>
        <input type="number" step="0.01" id="la_${name}" value="${v === null || v === undefined ? '' : v}"
               style="width:100%;padding:7px 10px;border:1px solid #e2e8f0;border-radius:7px;font-size:13px;background:white">
    </label>`;
}
function laTextarea(name, label, value, rows) {
    const v = (value ?? '');
    return `<label style="font-size:12px;color:#475569;display:block;grid-column:1 / -1">
        <span style="display:block;margin-bottom:3px">${label}</span>
        <textarea id="la_${name}" rows="${rows||2}"
               style="width:100%;padding:7px 10px;border:1px solid #e2e8f0;border-radius:7px;font-size:13px;background:white;font-family:inherit;resize:vertical">${laEscape(String(v))}</textarea>
    </label>`;
}
function laCheckbox(name, label, value) {
    return `<label style="font-size:12px;color:#475569;display:flex;align-items:center;gap:8px;cursor:pointer">
        <input type="checkbox" id="la_${name}" ${value ? 'checked' : ''}
               style="width:16px;height:16px;cursor:pointer">
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
        const n = parseFloat(v);
        return Number.isFinite(n) ? n : null;
    };
    const bool  = (name) => !!grab(name)?.checked;

    // C# DTO erwartet PascalCase Property-Namen — System.Text.Json mit Default-
    // PropertyNameCaseInsensitive=true akzeptiert beide, aber wir senden camelCase
    // (matched dem Default JsonSerializerOptions in ASP.NET Core 8).
    return {
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
async function laGeneratePdf() {
    if (!_laCurrentEmp?.id || !_laCurrentYear) return;
    const payload = laCollectFormData();
    try {
        const r = await fetch(`/api/lohnausweis/${_laCurrentEmp.id}/${_laCurrentYear}/pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
        });
        if (!r.ok) {
            const errJson = await r.json().catch(() => ({}));
            alert('PDF-Erstellung fehlgeschlagen: ' + (errJson.error || r.statusText));
            return;
        }
        const blob = await r.blob();
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        const name = (_laCurrentEmp.name || 'Lohnausweis').replace(/[^a-zA-Z0-9_-]/g, '_');
        a.download = `Lohnausweis_${_laCurrentYear}_${name}.pdf`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(() => URL.revokeObjectURL(url), 5000);
    } catch (ex) {
        alert('Fehler beim PDF-Generieren: ' + ex.message);
    }
}
