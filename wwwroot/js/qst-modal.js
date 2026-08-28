// ══════════════════════════════════════════════════════════════════════
// qst-modal.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════
// QUELLENSTEUER MODAL
// ══════════════════════════════════════════════
let qstCurrentEmployeeId = null;
let qstCurrentEntryId    = null;
let qstEmployeeData      = null;
let qstAllEntries        = [];
// Walter-Vorgabe 28.05.2026: Cache der MA-Kinder (mit QstDeductibleFrom/Until)
// für den Auto-Zähler unter „Anzahl Kinder".
let _qstFamilyKinder     = [];
// Walter-Vorgabe 14.06.2026: Cache des Server-Tarifvorschlags. Der Server
// (QstTarifVorschlagService) ist die EINZIGE Quelle der Wahrheit für den
// Vorschlag — die alten Frontend-Heuristiken (qstSuggestTarifBuchstabe,
// qstAutoKinderCount) dienen nur noch als rein lokale Anzeige-Hilfe, falls
// der Server-Endpoint nicht erreichbar ist. Aktualisiert sich bei jeder
// ValidFrom-Änderung (Stichtag wechselt → ggf. anderer Vorschlag).
let _qstServerVorschlag  = null;

// Halbfamilie ist eine Segment-Pille (Radios name="qstHalbfamilie",
// Walter 12.08.2026 — wie LGAV/Zustellart) statt Select.
function qstSetHalbfamilie(val) {
    const want = (val ?? '').toString();
    document.querySelectorAll('input[name="qstHalbfamilie"]').forEach(r => { r.checked = (r.value === want); });
}
function qstGetHalbfamilie() {
    const r = document.querySelector('input[name="qstHalbfamilie"]:checked');
    return r ? r.value : '';
}

async function openQstModal(employeeId, employeeData) {
    qstCurrentEmployeeId = employeeId;
    qstCurrentEntryId    = null;
    qstEmployeeData      = employeeData;

    // Stammdaten anzeigen
    const permitName = employeeData?.permitTypeName ?? employeeData?.permitType ?? '–';
    const wohnortCity = (typeof stripCityCantonSuffix === 'function')
        ? stripCityCantonSuffix(employeeData?.city) : (employeeData?.city || '');
    const wohnort    = [employeeData?.zipCode, wohnortCity].filter(Boolean).join(' ') || '–';
    const nat        = employeeData?.nationalityCode ?? employeeData?.nationality ?? '–';
    const zivil      = employeeData?.zivilstand ?? employeeData?.maritalStatus ?? '–';
    const kantonCode = employeeData?.cantonCode;
    const kantonName = (typeof kantonNameFor === 'function') ? kantonNameFor(kantonCode) : null;
    const kantonDisplay = kantonCode ? (kantonName ? `${kantonCode} — ${kantonName}` : kantonCode) : '–';
    document.getElementById('qstModalSub').textContent    = `${employeeData?.firstName ?? ''} ${employeeData?.lastName ?? ''}`.trim();
    document.getElementById('qstPermitDisplay').textContent  = permitName;
    document.getElementById('qstWohnortDisplay').textContent = wohnort;
    document.getElementById('qstNatDisplay').textContent     = nat;
    document.getElementById('qstKantonDisplay').textContent  = kantonDisplay;
    document.getElementById('qstZivilstandDisplay').textContent = zivil;
    // K4-Vorstufe (Walter 29.08.2026): Inline-Stammzeile (Zivilstand-Anzeige,
    // «seit», Konfession) aus den MA-Daten füllen.
    qstFillStammzeile();

    // Verlauf laden
    await loadQstHistory(employeeId);

    // Ehepartner-Info aus Familie-Tab anzeigen
    loadQstPartnerInfo(employeeId);

    // Walter-Vorgabe 28.05.2026: Familie-Kinder laden für den Auto-Zähler unter
    // „Anzahl Kinder". Sequentiell, damit populateQstForm gleich darauf den Hint
    // zeichnen kann.
    await loadQstFamilyKinder(employeeId);
    // Wohnsituation laden (Wochenaufenthalt-Zusatzadresse, Walter 28.08.2026) —
    // VOR populateQstForm, damit qstApplyWochenaufenthaltLock die Daten hat.
    await loadQstWochenaufenthalt(employeeId);
    // ValidFrom-Trigger einmalig binden, damit beim Datums-Wechsel sowohl der
    // Hint als auch der Server-Vorschlag neu gerechnet werden (anderer
    // Stichtag → ggf. anderer Tarif/anders viele berechtigte Kinder).
    const vfInp = document.getElementById('qstValidFrom');
    if (vfInp && !vfInp.dataset.qstAutoBound) {
        vfInp.addEventListener('change', async () => {
            // Server-Vorschlag NEU holen — der Stichtag fliesst in die
            // Kinderzählung ein. Im Edit-Modus (qstCurrentEntryId gesetzt)
            // nur Banner aktualisieren, NIE Felder überschreiben.
            if (qstCurrentEmployeeId) {
                await qstFetchServerVorschlag(vfInp.value);
                if (!qstCurrentEntryId) {
                    qstApplyServerVorschlagToForm();
                } else {
                    qstRenderVorschlagBanner();
                }
            }
            qstUpdateAutoKinderHint();
        });
        vfInp.dataset.qstAutoBound = '1';
    }

    // Aktuellen Eintrag laden und anzeigen
    const today = new Date().toISOString().slice(0, 10);
    const res = await fetch(`/api/employees/${employeeId}/quellensteuer/current?date=${today}`, { headers: ah() });
    const current = res.ok ? await res.json() : null;
    // Walter-Vorgabe 14.06.2026: wenn kein aktueller Eintrag existiert, NICHT
    // einfach `populateQstForm(null)` aufrufen (das setzt leere Felder), sondern
    // denselben Neu-Eintrag-Pfad wie der „+ Neuer Eintrag"-Button nehmen:
    // openQstEntry(null) befüllt ValidFrom-Default, Auto-Fill Steuerkanton/
    // Gemeinde/BFS aus der Wohnadresse UND holt den Server-Vorschlag.
    if (current) {
        qstCurrentEntryId = current.id ?? null;
        populateQstForm(current);
        // Edit-Modus: Server-Vorschlag NUR als Banner zeigen, NIE auf die Felder
        // schreiben (Walter-Vorgabe: kein Auto-Overwrite beim Bearbeiten).
        await qstFetchServerVorschlag(current.validFrom);
        qstRenderVorschlagBanner();
        qstUpdateAutoKinderHint();
    } else {
        qstCurrentEntryId = null;
        await openQstEntry(null);
    }

    document.getElementById('qstModal').style.display = 'flex';
}

// PLZ → Gemeinde/BFS/Steuerkanton automatisch füllen (Walter 12.08.2026,
// Schweizer Standard-Konvention wie plzLookup bei den Adressen).
async function qstPlzLookup(plz) {
    const p = String(plz || '').trim();
    if (!/^\d{4}$/.test(p)) return;
    try {
        const r = await fetch(`/api/swiss-locations/by-plz?plz=${p}`, { headers: ah() });
        if (!r.ok) return;
        const list = await r.json();
        const hit = Array.isArray(list) ? list[0] : null;
        if (!hit) return;
        const g = document.getElementById('qstGemeinde');
        const b = document.getElementById('qstGemeindeBfs');
        const k = document.getElementById('qstSteuerkanton');
        if (g) g.value = hit.gemeindename || hit.ortschaftsname || '';
        if (b) b.value = hit.bfsNr ?? '';
        if (k && hit.kantonskuerzel) { k.value = hit.kantonskuerzel; if (typeof onQstKantonChange === 'function') onQstKantonChange(); }
    } catch (_) { /* Lookup ist nur Komfort */ }
}

function closeQstModal() {
    document.getElementById('qstModal').style.display = 'none';
    const empId = qstCurrentEmployeeId;
    qstCurrentEmployeeId = null;
    qstCurrentEntryId    = null;
    qstAllEntries        = [];
    // Tab neu laden falls von Mitarbeiter-Tab geöffnet
    if (typeof qstOpenedFromTab !== 'undefined' && qstOpenedFromTab && empId) {
        qstOpenedFromTab = false;
        if (typeof loadQuellensteuerTab === 'function') loadQuellensteuerTab(empId);
    }
}

async function loadQstHistory(employeeId) {
    const res = await fetch(`/api/employees/${employeeId}/quellensteuer`, { headers: ah() });
    qstAllEntries = res.ok ? await res.json() : [];
    renderQstHistoryTabs();
}

// Walter-Vorgabe 28.05.2026: Lädt die Kinder des MA aus dem Familie-Tab und
// cached sie für den Auto-Zähler unter „Anzahl Kinder". Pro Kind wertet das
// Frontend Q-Steuer-Berechtigung (QstDeductibleFrom/Until) am Stichtag des
// QST-Eintrags aus.
async function loadQstFamilyKinder(employeeId) {
    _qstFamilyKinder = [];
    window._qstKPartner = null;
    try {
        const res = await fetch(`/api/employees/${employeeId}/family`, { headers: ah() });
        if (!res.ok) return;
        const members = await res.json();
        _qstFamilyKinder = (members || []).filter(m => m.memberType === 'Kind');
        // Konkubinatspartner (Walter 25.08.2026, docs/konkubinat-qst-konzept.md):
        // Familie-Tab ist die QUELLE für Konkubinat + Einkommensfrage.
        window._qstKPartner = (members || []).find(m =>
            m.memberType === 'Konkubinatspartner' && !m.dateOfDeath) || null;
    } catch { /* leerer Cache */ }
}

// Wochenaufenthalt aus der Wohnsituation (Walter 28.08.2026): QUELLE ist die
// Zusatzadresse Typ «Wochenaufenthalt» beim MA. Existiert eine → Checkbox
// gesetzt + gesperrt (der Server überschreibt beim Speichern ohnehin,
// ApplyWochenaufenthaltAsync). Ohne Adresse bleibt sie editierbar (Alt-Fälle).
// Der QST-Kanton hängt IMMER am Hauptwohnsitz — nie am Aufenthaltsort.
async function loadQstWochenaufenthalt(employeeId) {
    window._qstWaAdresse = null;
    try {
        const res = await fetch(`/api/employees/${employeeId}/addresses`, { headers: ah() });
        if (!res.ok) return;
        const list = await res.json();
        window._qstWaAdresse = (Array.isArray(list) ? list : [])
            .find(a => (a.addressType || a.AddressType) === 'Wochenaufenthalt') || null;
    } catch { /* Komfort — ohne Daten bleibt die Checkbox editierbar */ }
}

function qstApplyWochenaufenthaltLock() {
    const cb = document.getElementById('qstIsWochenaufenthalter');
    if (!cb) return;
    const wa = window._qstWaAdresse;
    if (wa) {
        const ort = [wa.zipCode, wa.city].filter(Boolean).join(' ');
        cb.checked  = true;
        cb.disabled = true;
        cb.parentElement.title = 'Aus der Wohnsituation (Zusatzadresse «Wochenaufenthalt»'
            + (ort ? `: ${ort}` : '') + ') — Pflege beim MA unter Weitere Adressen. QST-Kanton bleibt der Hauptwohnsitz.';
        cb.parentElement.style.opacity = '0.65';
    } else {
        cb.disabled = false;
        cb.parentElement.style.opacity = '';
        cb.parentElement.title = '';
    }
}

// ══════════════════════════════════════════════════════════════════════
// K4-Vorstufe (Walter 29.08.2026): Tarif = RESULTAT, keine Auswahl mehr.
// Die tarifrelevanten Stammdaten (Zivilstand-Anzeige, «seit», Konfession)
// stehen inline unter der Gültigkeit und schreiben DIREKT in den MA-Stamm;
// danach wird der Server-Vorschlag neu geholt und das Resultat unten
// aktualisiert. Tarif-Select + QST-Code sind unsichtbare Datenträger.
// ══════════════════════════════════════════════════════════════════════
const QST_ZIVILSTAND_LABELS = {
    unbekannt: 'Unbekannt', ledig: 'Ledig', verheiratet: 'Verheiratet',
    geschieden: 'Geschieden', verwitwet: 'Verwitwet', getrennt: 'Getrennt',
    eingetragene_partnerschaft: 'Eingetragene Partnerschaft'
};
const QST_TARIF_BEZ = {
    A: 'Alleinstehend', B: 'Verheiratet, Alleinverdiener',
    C: 'Verheiratet, Doppelverdiener', D: 'Nebenerwerb', H: 'Alleinerziehend',
    L: 'Grenzgänger (DE) alleinstehend', M: 'Grenzgänger (DE) verheiratet, Alleinverdiener',
    N: 'Grenzgänger (DE) verheiratet, Doppelverdiener', P: 'Grenzgänger (DE) alleinerziehend',
    Q: 'Grenzgänger (DE)'
};

function qstFillStammzeile() {
    const d = qstEmployeeData || {};
    const ziv = (d.zivilstand ?? d.maritalStatus ?? '').toString();
    const zEl = document.getElementById('qstZivilstandAnzeige');
    if (zEl) zEl.value = QST_ZIVILSTAND_LABELS[ziv] || ziv || '–';
    const sEl = document.getElementById('qstZivilstandSeit');
    if (sEl) sEl.value = (d.maritalStatusSince || '').toString().slice(0, 10);
    const rEl = document.getElementById('qstReligion');
    if (rEl) rEl.value = d.religion || '';
    const hint = document.getElementById('qstStammSaveHint');
    if (hint) hint.innerHTML = '';
}

// Inline-Änderung «Zivilstand seit» / «Konfession» → sofort in den MA-Stamm
// speichern (PUT /api/employees/{id}, null-tolerantes DTO — nur das eine
// Feld wird geschrieben), dann Tarif-Vorschlag + Resultat neu rechnen.
// Hinweis: die Konfessions-Änderung triggert serverseitig den bestehenden
// QstKonfessionSync (Kirchensteuer-Folge) — gleiche Mechanik wie die
// Konfessions-Pflege in der MA-Maske.
async function qstStammChanged(which) {
    if (!qstCurrentEmployeeId) return;
    const hint = document.getElementById('qstStammSaveHint');
    const body = which === 'religion'
        ? { religion: document.getElementById('qstReligion')?.value ?? '' }
        : { maritalStatusSinceSet: true,
            maritalStatusSince: document.getElementById('qstZivilstandSeit')?.value || null };
    try {
        const res = await fetch(`/api/employees/${qstCurrentEmployeeId}`, {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            if (hint) hint.innerHTML = '<span style="color:#dc2626">⚠ Speichern im MA-Stamm fehlgeschlagen.</span>';
            return;
        }
        if (qstEmployeeData) {
            if (which === 'religion') qstEmployeeData.religion = body.religion || null;
            else qstEmployeeData.maritalStatusSince = body.maritalStatusSince;
        }
        if (hint) hint.innerHTML = '<span style="color:#16a34a">✓ Im MA-Stamm gespeichert — Tarif neu hergeleitet.</span>';
        const vf = document.getElementById('qstValidFrom')?.value || '';
        await qstFetchServerVorschlag(vf);
        if (!qstCurrentEntryId) qstApplyServerVorschlagToForm();
        else qstRenderVorschlagBanner();
        qstUpdateAutoKinderHint();
        qstRenderResultat();
    } catch {
        if (hint) hint.innerHTML = '<span style="color:#dc2626">⚠ Verbindungsfehler beim Speichern.</span>';
    }
}

// Resultat-Karte unten: grosser Code + Bezeichnung. Quelle = die (jetzt
// unsichtbaren) Datenträger-Felder; die Begründung liefert der Server-
// Vorschlag-Banner (qstTarifHint sitzt in derselben Karte).
function qstRenderResultat() {
    const codeEl = document.getElementById('qstResultatCode');
    const besEl  = document.getElementById('qstResultatBeschreibung');
    if (!codeEl) return;
    const code  = (document.getElementById('qstCode')?.value || '').toString().trim().toUpperCase();
    const tarif = (document.getElementById('qstTarifCode')?.value || '').toString().trim().toUpperCase();
    const pct   = (document.getElementById('qstProzentsatz')?.value || '').toString().trim();
    codeEl.textContent = code || (pct ? `${pct} %` : '–');
    const v = _qstServerVorschlag;
    let bez = (v && v.tarifCode === tarif && v.tarifBezeichnung) ? v.tarifBezeichnung : (QST_TARIF_BEZ[tarif] || '');
    if (pct) bez = (bez ? bez + ' · ' : '') + 'manueller Prozentsatz';
    if (besEl) besEl.textContent = bez;
}

// Konkubinat-Checkboxen aus dem Familie-Tab befüllen + sperren (Walter
// 25.08.2026): mit K-Partner sind «Konkubinat» und «Höh. Einkommen» nur
// noch Anzeige — der Server überschreibt die Werte beim Speichern ohnehin
// (ApplyKonkubinatAsync). Ohne K-Partner bleiben sie editierbar (Alt-Fälle).
function qstApplyKonkubinatLock() {
    const konk = document.getElementById('qstLivesInKonkubinat');
    const eink = document.getElementById('qstHasHigherIncomeThanPartner');
    if (!konk || !eink) return;
    const kp = window._qstKPartner;
    if (kp) {
        konk.checked = true;
        eink.checked = kp.maHatHoeheresEinkommen === true;
        konk.disabled = true;
        eink.disabled = true;
        konk.parentElement.title = 'Aus dem Familie-Tab (Konkubinatspartner erfasst) — Pflege dort.';
        eink.parentElement.title = 'Aus dem Familie-Tab (Einkommensfrage beim Konkubinatspartner) — Pflege dort.';
        konk.parentElement.style.opacity = '0.65';
        eink.parentElement.style.opacity = '0.65';
    } else {
        konk.disabled = false;
        eink.disabled = false;
        konk.parentElement.style.opacity = '';
        eink.parentElement.style.opacity = '';
        konk.parentElement.title = '';
        eink.parentElement.title = '';
    }
}

// ══════════════════════════════════════════════════════════════════════
// Walter-Vorgabe 14.06.2026: Server-Tarifvorschlag (Quelle der Wahrheit).
// `qstFetchServerVorschlag` holt den Vorschlag vom Endpoint und cached ihn
// in _qstServerVorschlag. `qstApplyServerVorschlagToForm` schreibt die
// Werte ins Formular — wenn `onlyEmptyFields=true`, werden bereits manuell
// gesetzte Felder NICHT überschrieben (Walter's Edit-Modus + manuelle
// Korrektur). `qstRenderVorschlagBanner` zeigt Begründung + Warnungen.
// ══════════════════════════════════════════════════════════════════════
let _qstServerVorschlagError = null;
async function qstFetchServerVorschlag(stichtagIso) {
    _qstServerVorschlag = null;
    _qstServerVorschlagError = null;
    if (!qstCurrentEmployeeId) { _qstServerVorschlagError = 'kein MA-Kontext'; return null; }
    const date = (stichtagIso || '').toString().slice(0, 10) ||
                 new Date().toISOString().slice(0, 10);
    try {
        const res = await fetch(
            `/api/employees/${qstCurrentEmployeeId}/quellensteuer/vorschlag?date=${date}`,
            { headers: ah() });
        if (!res.ok) {
            // Grund sichtbar machen (Walter 13.07.2026): «nicht verfügbar»
            // ohne Ursache war nicht diagnostizierbar.
            let msg = '';
            try { const j = await res.json(); msg = j.message || j.error || ''; } catch (_) {}
            _qstServerVorschlagError = `HTTP ${res.status}${msg ? ' — ' + msg : ''}`;
            return null;
        }
        _qstServerVorschlag = await res.json();
        // Kirchensteuer-Häkchen IMMER aus dem Konfession-abgeleiteten
        // Server-Wert (Walter 12.08.2026) — auch im Edit-Modus. Das Feld ist
        // nur Anzeige; der Server erzwingt beim Speichern denselben Wert.
        // Ohne den Sync zeigte ein Alt-Eintrag (manuell Y) ein Häkchen, das
        // dem Vorschlag (N aus Konfession) widersprach.
        const kirchEl = document.getElementById('qstKirchensteuer');
        if (kirchEl) {
            kirchEl.checked = !!_qstServerVorschlag.kirchensteuer;
            if (typeof buildQstCode === 'function') buildQstCode();
        }
        return _qstServerVorschlag;
    } catch (e) {
        _qstServerVorschlagError = 'Netzwerkfehler: ' + (e?.message || e);
        return null;
    }
}

// Walter-Vorgabe 14.06.2026 (Update): schreibt den Server-Vorschlag in die
// Felder. NUR für NEUE Einträge aufrufen — bestehende Einträge dürfen NIE
// auto-überschrieben werden (im Edit-Modus nur `qstRenderVorschlagBanner`
// aufrufen). Der frühere `onlyEmptyFields`-Mechanismus mit `qstUserTouched`
// war kaputt (das Flag wurde nie gesetzt) und hatte den 0-vs.-leer-Bug:
// populateQstForm(null) setzt qstKinder auf "0", dann hätte ein „nur leere
// Felder überschreiben"-Check das Server-`anzahlKinder=1` übersprungen.
// Jetzt: bei NEU IMMER vollständig befüllen, sonst nichts.
function qstApplyServerVorschlagToForm() {
    const v = _qstServerVorschlag;
    if (!v) { qstRenderVorschlagBanner(); return; }
    const setVal = (id, val) => { const el = document.getElementById(id); if (el) el.value = (val ?? '').toString(); };
    const setChk = (id, val) => { const el = document.getElementById(id); if (el) el.checked = !!val; };
    setVal('qstTarifCode',    v.tarifCode);
    setVal('qstKinder',       v.anzahlKinder);
    setChk('qstKirchensteuer',v.kirchensteuer);
    setVal('qstCode',         v.qstCode);
    if (typeof buildQstCode === 'function') buildQstCode();
    qstRenderVorschlagBanner();
}

function qstRenderVorschlagBanner() {
    const hint = document.getElementById('qstTarifHint');
    if (!hint) return;
    const v   = _qstServerVorschlag;
    const sel = document.getElementById('qstTarifCode');
    if (!v) { hint.innerHTML = ''; return; }
    const manual = sel?.value || '';
    const formCode = (document.getElementById('qstCode')?.value || '').toString().trim().toUpperCase();
    const vorschlagCode = (v.qstCode || '').toString().trim().toUpperCase();
    const codeDiff = !!(vorschlagCode && formCode && formCode !== vorschlagCode);
    const passt  = manual && manual === v.tarifCode && !codeDiff;
    const headerColor = passt ? '#16a34a' : (codeDiff ? '#b45309' : '#6b7280');
    const headerIcon  = passt ? '✓' : (codeDiff ? '⚠' : 'ℹ');
    const begr   = v.begruendung ? `<div style="color:#475569;margin-top:2px">${v.begruendung}</div>` : '';
    const warns  = (v.warnings && v.warnings.length)
        ? `<div style="color:#b45309;margin-top:2px">⚠ ${v.warnings.map(w => w.replace(/"/g,'&quot;')).join(' · ')}</div>`
        : '';
    const choice = (manual && manual !== v.tarifCode && !codeDiff)
        ? `<div style="color:#94a3b8;margin-top:2px">Du hast bewusst <b>${manual}</b> gewählt.</div>`
        : '';
    // Walter 01.08.2026: bei Abweichung (z.B. Konfession → A0Y, Eintrag noch A0N)
    // Ein-Klick-Übernahme — sonst bleibt der falsche Code stehen.
    const applyBtn = codeDiff
        ? `<div style="margin-top:6px"><button type="button" onclick="qstApplyServerVorschlagToForm()"
            style="background:#3f3f3f;color:#fff;border:none;padding:6px 12px;border-radius:10px;font-size:12px;font-weight:600;cursor:pointer">
            Vorschlag ${v.qstCode} übernehmen</button>
            <span style="color:#64748b;font-size:11px;margin-left:8px">Aktuell im Formular: ${formCode || '–'}</span></div>`
        : '';
    hint.innerHTML =
        `<div style="color:${headerColor};font-weight:600">${headerIcon} Server-Vorschlag: <b>${v.qstCode}</b> (Tarif ${v.tarifCode}${v.tarifBezeichnung ? ' — ' + v.tarifBezeichnung : ''})</div>` +
        begr + warns + choice + applyBtn;
    // Resultat-Karte (Code gross) synchron halten (K4-Vorstufe 29.08.2026).
    qstRenderResultat();
}

// Wie viele Kinder sind am gewählten Stichtag QST-abzugsberechtigt?
// Walter-Vorgabe 14.06.2026 (Update): zweistufige Logik:
//   1) Wenn beim Kind QstDeductibleFrom/Until explizit gepflegt sind →
//      dieser Zeitraum gilt (Walter's manuelle Pflege bleibt führend).
//   2) Sonst Fallback: Kind ist abzugsberechtigt zwischen Geburtsdatum und
//      18. Geburtstag (Schweizer QST-Standard, falls keine Verlängerung
//      wegen Ausbildung erfasst wurde). So zählen auch frisch importierte
//      oder neu erfasste Kinder ohne dass Walter erst die QST-Daten
//      hinterlegen muss.
function qstAutoKinderCount(stichtagIso) {
    if (!stichtagIso) return 0;
    const s = stichtagIso.slice(0, 10);
    return _qstFamilyKinder.filter(k => {
        const f = (k.qstDeductibleFrom  || '').toString().slice(0, 10);
        const u = (k.qstDeductibleUntil || '').toString().slice(0, 10);
        // (1) Explizit gepflegt → Zeitraum greift
        if (f || u) {
            if (f && f > s) return false;
            if (u && u < s) return false;
            return true;
        }
        // (2) Fallback aus Geburtsdatum (Geburt … +18 Jahre)
        const dob = (k.dateOfBirth || '').toString().slice(0, 10);
        if (!dob || !/^\d{4}-\d{2}-\d{2}$/.test(dob)) return false;
        if (dob > s) return false;             // noch nicht geboren
        const [y, m, d] = dob.split('-');
        const dob18 = `${parseInt(y, 10) + 18}-${m}-${d}`;
        return dob18 >= s;                     // unter 18 am Stichtag
    }).length;
}

function qstFmtDe(iso) {
    const s = (iso || '').toString().slice(0, 10);
    if (s.length !== 10) return '';
    return s.slice(8,10) + '.' + s.slice(5,7) + '.' + s.slice(0,4);
}

// Ziel-KINDERZIFFER für den Code (Walter 25.08.2026, Fall Konkubinat):
// die Ziffer folgt dem TARIF, nicht der rohen Kinderzahl — der Server-
// Vorschlag rechnet das korrekt (A → 0, auch mit berechtigtem Kind, z.B.
// Konkubinat wenn der Partner mehr verdient; H → Haushalts-Kinder).
// Fallback ohne geladenen Vorschlag: lokale Zählung wie bisher.
function qstZielKinderZiffer(stichtag) {
    const sv = _qstServerVorschlag;
    if (sv && typeof sv.anzahlKinder === 'number' && !sv.abklaerungNoetig)
        return { ziffer: sv.anzahlKinder, tarif: sv.tarifCode || '' };
    return { ziffer: qstAutoKinderCount(stichtag), tarif: '' };
}

// Hint-Zeile unter „Anzahl Kinder": grün wenn manuell == Ziel-Ziffer,
// rot mit „Auto übernehmen"-Button wenn Differenz.
function qstUpdateAutoKinderHint() {
    const inp  = document.getElementById('qstKinder');
    const hint = document.getElementById('qstKinderAutoHint');
    if (!inp || !hint) return;
    const stichtag = document.getElementById('qstValidFrom')?.value || '';
    if (!stichtag) { hint.innerHTML = ''; return; }
    const auto   = qstAutoKinderCount(stichtag);
    const ziel   = qstZielKinderZiffer(stichtag);
    const manual = parseInt(inp.value || '0', 10) || 0;
    const stichtagDe = qstFmtDe(stichtag);

    // Keine MA-Familie geladen / keine Kinder im Familie-Tab gepflegt → dezenter
    // Hinweis statt Auto-Wert, damit Walter nicht denkt es sei falsch berechnet.
    if (!_qstFamilyKinder.length) {
        hint.innerHTML = `<span style="color:#94a3b8">ℹ Keine Kinder im Familie-Tab erfasst.</span>`;
        return;
    }
    // Walter-Vorgabe 14.06.2026: Hinweis ob die Auto-Zahl aus expliziten
    // QST-Daten kommt oder aus dem Geburtsdatum-Fallback — Walter sieht so
    // direkt ob er die QST-Daten der Kinder noch pflegen sollte.
    const allKinderHaveQstData = _qstFamilyKinder.every(k => {
        const f = (k.qstDeductibleFrom  || '').toString().slice(0, 10);
        const u = (k.qstDeductibleUntil || '').toString().slice(0, 10);
        return f || u;
    });
    const quelle = allKinderHaveQstData ? 'aus QST-Daten' : 'aus Geburtsdatum';

    // Walter 12.08.2026: immer zeigen, wie viele Kinder ERFASST sind —
    // zusätzlich zur Zahl der am Stichtag QST-berechtigten.
    const erfasst = `${_qstFamilyKinder.length} Kind${_qstFamilyKinder.length===1?'':'er'} im Familie-Tab`;
    // Tarif-Abweichung sichtbar machen (Walter 25.08.2026): z.B. Konkubinat
    // mit Partner-Mehrverdienst → 1 Kind berechtigt, aber Tarif A → Ziffer 0.
    const tarifNote = (ziel.tarif && ziel.ziffer !== auto)
        ? ` · Tarif ${ziel.tarif} → Ziffer ${ziel.ziffer}` : '';
    if (manual === ziel.ziffer) {
        hint.innerHTML = `<span style="color:#16a34a">✓ ${erfasst} · ${auto} QST-abzugsberechtigt am ${stichtagDe} (${quelle})${tarifNote}</span>`;
    } else {
        hint.innerHTML = `
            <span style="color:#dc2626">⚠ ${erfasst} · Auto: ${ziel.ziffer}${tarifNote ? ' (' + tarifNote.slice(3) + ')' : ' (' + quelle + ')'}, manuell eingetragen: ${manual}</span>
            <button type="button" onclick="qstApplyAutoKinder()"
                    style="margin-left:6px;background:#1a1a1a;color:#fff;border:none;padding:2px 10px;border-radius:4px;font-size:11px;cursor:pointer;font-weight:600">Auto übernehmen</button>`;
    }
}

// Klick auf „Auto übernehmen": schreibt die berechnete Zahl ins Input + baut
// den QST-Code neu auf.
function qstApplyAutoKinder() {
    const inp = document.getElementById('qstKinder');
    const stichtag = document.getElementById('qstValidFrom')?.value || '';
    if (!inp || !stichtag) return;
    // Ziel-Ziffer folgt dem Tarif (Walter 25.08.2026) — nicht der rohen Zahl.
    inp.value = qstZielKinderZiffer(stichtag).ziffer;
    if (typeof buildQstCode === 'function') buildQstCode();
    qstUpdateAutoKinderHint();
    qstSuggestTarif();
}

// ══════════════════════════════════════════════════════════════════════
// Walter-Vorgabe 14.06.2026: QST-Tarif aus Zivilstand + Kinder vorschlagen.
//
// Schweizer QST-Tarif-Logik:
//   • ledig + keine Kinder                              → A (Alleinstehend)
//   • ledig + Kinder                                    → H (Alleinerziehend)
//   • verheiratet / eingetragene Partnerschaft          → B (Alleinverdiener)
//       — Walter wechselt manuell auf C wenn beide arbeiten (Doppelverdiener)
//   • geschieden / verwitwet / getrennt + Kinder        → H
//   • geschieden / verwitwet / getrennt + keine Kinder  → A
//
// C (Doppelverdiener) wird NIE auto-vorgeschlagen — kann das System nicht
// wissen ob beide Partner arbeiten. Walter ergänzt manuell wenn nötig.
// Wenn das Tarif-Feld bereits einen Wert hat (z.B. Eintrag wird bearbeitet),
// wird NICHTS überschrieben. Auch im Hint steht dann „bestehender Eintrag".
// ══════════════════════════════════════════════════════════════════════
function qstSuggestTarifBuchstabe(zivilstand, anzahlKinder) {
    const z = (zivilstand || '').toLowerCase().trim();
    const k = parseInt(anzahlKinder, 10) || 0;
    const verheiratet = z.includes('verheiratet') || z.includes('partnerschaft') && !z.includes('aufgeloest');
    const alleinerziehend_basis =
        z.includes('ledig') || z.includes('geschieden') || z.includes('verwitwet') || z.includes('getrennt');
    // Walter-Vorgabe 14.06.2026: bei verheiratet → C (Doppelverdiener) als
    // Default, weil das in der Schweizer Praxis der Normalfall ist. Bei
    // tatsächlichem Alleinverdiener wechselt der User manuell auf B.
    if (verheiratet) return 'C';                    // Doppelverdiener-Default
    if (alleinerziehend_basis && k > 0) return 'H'; // Alleinerziehend
    if (alleinerziehend_basis) return 'A';          // Alleinstehend
    return null;                                     // Zivilstand unbekannt
}

// Walter-Vorgabe 14.06.2026 (Update): Quelle der Wahrheit ist der Server-
// Endpoint. `qstSuggestTarif` rendert nur noch den Banner (cached in
// _qstServerVorschlag). Wenn kein Server-Vorschlag vorliegt (z.B. Endpoint
// nicht erreichbar), greift als letzter Fallback die Frontend-Heuristik —
// damit der Tab nicht stumm bleibt.
function qstSuggestTarif() {
    const sel = document.getElementById('qstTarifCode');
    if (!sel) return;

    // Hauptpfad: Server-Vorschlag liegt vor → Banner zeichnen.
    if (_qstServerVorschlag) {
        qstRenderVorschlagBanner();
        return;
    }

    // Fallback (Server-Endpoint nicht erreichbar): minimaler Hint aus
    // Zivilstand. Berechnung läuft NICHT mehr automatisch ins Feld — Walter
    // soll sehen dass die Server-Logik gerade nicht greift.
    const hint = document.getElementById('qstTarifHint');
    if (!hint) return;
    const zivil    = qstEmployeeData?.maritalStatus ?? qstEmployeeData?.zivilstand ?? '';
    const kinder   = parseInt(document.getElementById('qstKinder')?.value ?? '0', 10) || 0;
    const suggested = qstSuggestTarifBuchstabe(zivil, kinder);
    if (suggested && sel.value && suggested !== sel.value) {
        hint.innerHTML = `<span style="color:#94a3b8">ℹ Vorschlag aus Zivilstand wäre <b>${suggested}</b> — du hast bewusst <b>${sel.value}</b> gewählt. (Server-Vorschlag nicht verfügbar${_qstServerVorschlagError ? ': ' + _qstServerVorschlagError : ''}.)</span>`;
    } else if (suggested) {
        hint.innerHTML = `<span style="color:#94a3b8">ℹ Vorschlag aus Zivilstand: <b>${suggested}</b> (Server-Vorschlag nicht verfügbar${_qstServerVorschlagError ? ': ' + _qstServerVorschlagError : ''} — bitte manuell prüfen).</span>`;
    } else {
        hint.innerHTML = `<span style="color:#94a3b8">ℹ Kein Vorschlag möglich — Zivilstand „${zivil}" nicht erkannt.</span>`;
    }
}

// Lädt den Ehepartner aus dem Familie-Tab und zeigt Name + Geburtsdatum
// als Read-only-Info im QST-Modal an. Wird beim Öffnen des Modals aufgerufen.
async function loadQstPartnerInfo(employeeId) {
    const el = document.getElementById('qstPartnerName');
    if (!el) return;
    el.textContent = '–';
    try {
        const res = await fetch(`/api/employees/${employeeId}/family`, { headers: ah() });
        if (!res.ok) return;
        const members = await res.json();
        const ehepartner = (members ?? []).find(m => m.memberType === 'Ehepartner');
        if (!ehepartner) {
            el.innerHTML = '<span style="color:#94a3b8;font-style:italic">kein Ehepartner erfasst</span>';
            return;
        }
        const fullname = `${ehepartner.firstName ?? ''} ${ehepartner.lastName ?? ''}`.trim();
        let dob = '';
        if (ehepartner.dateOfBirth) {
            const iso = ehepartner.dateOfBirth.toString().slice(0, 10);
            const [y, m, d] = iso.split('-');
            if (y && m && d) dob = ` · *${d}.${m}.${y}`;
        }
        el.textContent = fullname + dob || '–';
    } catch {}
}

function renderQstHistoryTabs() {
    const container = document.getElementById('qstHistoryTabs');
    if (!qstAllEntries.length) {
        container.innerHTML = '<span style="font-size:12px;color:#94a3b8">Noch kein Eintrag</span>';
        return;
    }
    const fmtDe = (s) => {
        if (!s) return null;
        const iso = s.toString().slice(0, 10);
        const [y, m, d] = iso.split('-');
        return (y && m && d) ? `${d}.${m}.${y}` : iso;
    };
    container.innerHTML = qstAllEntries.map(e => {
        const from = fmtDe(e.validFrom) ?? '–';
        const to   = fmtDe(e.validTo)   ?? 'offen';
        const active = !e.validTo;
        return `<button onclick="loadQstEntry(${e.id})"
            style="border:1px solid ${active ? '#1a1a1a' : '#d1d5db'};background:${active ? '#f6f3ee' : '#fff'};
            color:${active ? '#6b7280' : '#374151'};border-radius:6px;padding:4px 12px;font-size:11px;cursor:pointer;font-weight:${active ? '600' : '400'}">
            ${from} → ${to}
        </button>`;
    }).join('');
}

async function loadQstEntry(id) {
    const res = await fetch(`/api/employees/${qstCurrentEmployeeId}/quellensteuer/${id}`, { headers: ah() });
    if (!res.ok) return;
    const entry = await res.json();
    qstCurrentEntryId = id;
    populateQstForm(entry);
    // Walter-Vorgabe 14.06.2026: bestehende Einträge NIE auto-overwriten —
    // den Server-Vorschlag für den Banner trotzdem holen (Stichtag = ValidFrom
    // des Eintrags), so sieht Walter sofort ob die manuelle Wahl vom heutigen
    // Vorschlag abweicht. Apply wird NICHT aufgerufen.
    await qstFetchServerVorschlag(entry?.validFrom);
    qstRenderVorschlagBanner();
    qstUpdateAutoKinderHint();
}

async function openQstEntry(id) {
    // Neuen Eintrag vorbereiten (Felder leeren, Datum auf heute)
    qstCurrentEntryId = null;
    populateQstForm(null);

    // Walter-Vorgabe 14.06.2026: Familie-Kinder hier IMMER frisch holen.
    // openQstModal lädt die einmalig beim Modal-Open — wenn Walter danach
    // im Familie-Tab Kinder erfasst und dann „+ Neuer Eintrag" klickt, war
    // _qstFamilyKinder leer (Modal wurde nie wieder neu geöffnet). Jetzt
    // holen wir hier nochmal — sicherstellt dass die Auto-Zahl stimmt.
    if (qstCurrentEmployeeId) {
        await loadQstFamilyKinder(qstCurrentEmployeeId);
    }

    // Walter-Vorgabe 14.06.2026: Default „Gültig ab" beim NEUEN Eintrag:
    //   • Wenn der MA noch GAR KEINEN QST-Eintrag hat → Eintrittsdatum.
    //     (Der erste QST-Eintrag soll von Anstellungsbeginn an gelten —
    //      sonst fehlt für den Zeitraum zwischen Eintritt und „heute" der
    //      Tarif, was zu Lücken im Lohnlauf führt.)
    //   • Wenn schon ein QST-Eintrag existiert (Folge-Eintrag) → heute.
    //     (Wechsel im Zivilstand / Kinder / Religion / Wohnsitz wirkt ab
    //      sofort; der Vorgänger wird vom Backend automatisch auf
    //      neu.ValidFrom-1 abgeschlossen — siehe POST-Endpoint.)
    // Robustes ISO-Parsing: nur YYYY-MM-DD, kein new Date() (Zeitzonen-Falle).
    const todayIso = (() => {
        const t = new Date();
        return `${t.getFullYear()}-${String(t.getMonth()+1).padStart(2,'0')}-${String(t.getDate()).padStart(2,'0')}`;
    })();
    const validFromDefault = (() => {
        const hasAnyEntry = Array.isArray(qstAllEntries) && qstAllEntries.length > 0;
        if (!hasAnyEntry) {
            // Eintrittsdatum aus den MA-Stammdaten holen.
            const ed = qstEmployeeData
                ?? (typeof selectedEmployee !== 'undefined' ? selectedEmployee : null);
            const entryIso = (ed?.entryDate || '').toString().slice(0, 10);
            if (/^\d{4}-\d{2}-\d{2}$/.test(entryIso)) return entryIso;
        }
        return todayIso;
    })();
    document.getElementById('qstValidFrom').value = validFromDefault;

    // Walter-Vorgabe 14.06.2026 (Update): Tarif/Kinder/Kirchensteuer/QstCode
    // kommen jetzt vom SERVER (QstTarifVorschlagService) — Quelle der Wahrheit.
    // Die alten Frontend-Heuristiken (qstAutoKinderCount + qstSuggestTarif)
    // bleiben nur noch als lokale Anzeigehilfe für „Auto wäre N Kind(er)".
    // Hier IMMER vollständig überschreiben (neuer Eintrag) — der Bug mit
    // qstKinder=0-aus-populateQstForm würde sonst den Server-Wert blockieren.
    await qstFetchServerVorschlag(validFromDefault);
    qstApplyServerVorschlagToForm();
    qstUpdateAutoKinderHint();

    // Auto-Fill Steuerkanton und Wohngemeinde aus der Wohnadresse des MA.
    // Fallback auf selectedEmployee (wenn aus dem Mitarbeiter-Tab geöffnet
    // und qstEmployeeData nicht gesetzt wurde).
    const ed = qstEmployeeData
        ?? (typeof selectedEmployee !== 'undefined' ? selectedEmployee : null);
    if (!ed) return;

    if (ed.cantonCode) {
        const ksel = document.getElementById('qstSteuerkanton');
        if (ksel && !ksel.value) ksel.value = ed.cantonCode;
    }
    if (ed.city) {
        const cinp = document.getElementById('qstGemeinde');
        if (cinp && !cinp.value) cinp.value = ed.city;
    }
    // BFS-Nr aus dem Ortschaftsverzeichnis holen (Match über PLZ + Gemeinde)
    if (ed.zipCode && ed.city) {
        try {
            const res = await fetch(`/api/swiss-locations/by-plz?plz=${encodeURIComponent(ed.zipCode)}`, { headers: ah() });
            if (!res.ok) return;
            const locs = await res.json();
            const match = locs.find(l => l.gemeindename === ed.city) ?? locs[0];
            if (match) {
                const bfsInp = document.getElementById('qstGemeindeBfs');
                if (bfsInp && !bfsInp.value) bfsInp.value = match.bfsNr;
                // Falls Kanton nicht aus cantonCode kam, vom swiss_location-Match übernehmen
                const ksel = document.getElementById('qstSteuerkanton');
                if (ksel && !ksel.value && match.kantonskuerzel) ksel.value = match.kantonskuerzel;
            }
        } catch {}
    }
}

// Komplett-Sperre (Walter 12.08.2026, gleiche Logik wie Verträge):
// abgeschlossene Versionen (ValidTo gesetzt) und in einem definitiv
// abgeschlossenen Lohnlauf verwendete Einträge sind unveränderbar —
// Änderungen laufen IMMER über einen neuen Eintrag. Der Server blockt
// zusätzlich hart (QST_ABGESCHLOSSEN / LOHN_EDIT_LOCKED).
function qstSetLocked(entry) {
    const wrap   = document.getElementById('qstFormWrap');
    const banner = document.getElementById('qstLockBanner');
    const save   = document.getElementById('qstSaveBtn');
    const locked = !!(entry && (entry.validTo || entry.inLohnVerwendet));
    if (wrap) wrap.classList.toggle('qst-locked', locked);
    if (save) save.style.display = locked ? 'none' : '';
    if (banner) {
        banner.style.display = locked ? 'block' : 'none';
        if (locked) {
            const grund = entry.validTo
                ? `abgeschlossen (${qstFmtDe(entry.validFrom)} – ${qstFmtDe(entry.validTo)})`
                : 'in einem definitiv abgeschlossenen Lohnlauf verwendet';
            banner.innerHTML = `🔒 Diese QST-Version ist ${grund} und kann nicht mehr geändert werden — Änderungen über «+ Neuer Eintrag» in der QST-Liste.`;
        }
    }
}

function populateQstForm(entry) {
    const v = (id, val) => { const el = document.getElementById(id); if (el) el.value = val ?? ''; };
    const c = (id, val) => { const el = document.getElementById(id); if (el) el.checked = !!val; };
    qstSetLocked(entry);

    v('qstValidFrom',      entry?.validFrom?.slice(0, 10)  ?? '');
    v('qstValidTo',        entry?.validTo?.slice(0, 10)    ?? '');
    v('qstSteuerkanton',   entry?.steuerkanton             ?? '');
    v('qstGemeinde',       entry?.qstGemeinde              ?? '');
    v('qstGemeindeBfs',    entry?.qstGemeindeBfsNr         ?? '');
    v('qstTarifCode',      entry?.tarifCode                ?? '');
    v('qstCode',           entry?.qstCode                  ?? '');
    v('qstKinder',         entry?.anzahlKinder             ?? 0);
    v('qstProzentsatz',    entry?.prozentsatz              ?? '');
    v('qstMedianlohn',     entry?.mindestlohnSatzbestimmung ?? '');
    v('qstArbeitsortKanton', entry?.arbeitsortKanton       ?? '');
    v('qstPartnerVon',     entry?.partnerEinkommenVon?.slice(0,10) ?? '');
    v('qstPartnerBis',     entry?.partnerEinkommenBis?.slice(0,10) ?? '');
    v('qstGesamtpensum',   entry?.gesamtpensumWeitereAg   ?? '');
    // Anderer Arbeitgeber des MA (Walter 25.08.2026) — volle Adresse; das
    // Einkommen wird nicht mehr erfasst (Altwert bleibt in der DB erhalten).
    v('qstWagName',    entry?.weitereAgName    ?? '');
    v('qstWagStrasse', entry?.weitereAgStrasse ?? '');
    v('qstWagPlz',     entry?.weitereAgPlz     ?? '');
    v('qstWagOrt',     entry?.weitereAgOrt     ?? '');
    v('qstWagKanton',  entry?.weitereAgKanton  ?? '');
    v('qstWagLand',    entry?.weitereAgLand    ?? '');
    window._qstGesEinkommenAlt = entry?.gesamteinkommenWeitereAg ?? null;
    qstSetHalbfamilie(entry?.halbfamilie ?? '');
    v('qstWohnsitzAusland',entry?.wohnsitzAusland          ?? '');
    v('qstWohnsitzstaat',  entry?.wohnsitzstaat            ?? '');
    v('qstAdresseAusland', entry?.adresseAusland           ?? '');

    c('qstKirchensteuer',   entry?.kirchensteuer);
    c('qstSpezielBewilligt',entry?.spezielBewilligt);
    c('qstTarifvorschlag',  entry?.tarifvorschlagQst ?? true);
    c('qstWeitere',         entry?.weitereBeschaftigungen);

    // Tarif-relevante Stammdaten (versioniert pro QST-Eintrag)
    c('qstLivesInKonkubinat',          entry?.livesInKonkubinat);
    c('qstHasJointParentalCare',       entry?.hasJointParentalCare);
    c('qstPaysAlimonyAdultChildren',   entry?.paysAlimonyAdultChildren);
    c('qstHasHigherIncomeThanPartner', entry?.hasHigherIncomeThanPartner);
    c('qstIsGrenzgaenger',             entry?.isGrenzgaenger);
    c('qstIsWochenaufenthalter',       entry?.isWochenaufenthalter);
    // Konkubinat aus dem Familie-Tab befüllen + sperren (Walter 25.08.2026).
    qstApplyKonkubinatLock();
    // Wochenaufenthalt aus der Wohnsituation befüllen + sperren (Walter 28.08.2026).
    qstApplyWochenaufenthaltLock();
    // Resultat-Karte aus den geladenen Werten zeichnen (K4-Vorstufe 29.08.2026).
    qstRenderResultat();

    toggleQstWeitere();
    document.getElementById('qstSaveResult').textContent = '';
    // Walter-Vorgabe 28.05.2026: Auto-Kinder-Hint immer rendern. Bei NEU
    // (kein entry) zusätzlich das Feld auf den berechneten Wert vorbefüllen,
    // damit Walter nicht selbst zählen muss.
    if (!entry) {
        const stichtag = document.getElementById('qstValidFrom')?.value || '';
        if (stichtag && _qstFamilyKinder.length) {
            const auto = qstAutoKinderCount(stichtag);
            document.getElementById('qstKinder').value = auto;
            if (typeof buildQstCode === 'function') buildQstCode();
        }
    }
    qstUpdateAutoKinderHint();
}

function onQstKantonChange() {
    // Kanton-Kürzel in Kanton-Name umwandeln (für Speichern)
}

function onQstTarifChange() {
    buildQstCode();
    // Walter-Vorgabe 14.06.2026: Hint neu rendern — zeigt jetzt ob Walter's
    // manuelle Wahl mit dem Auto-Vorschlag übereinstimmt oder bewusst abweicht.
    qstSuggestTarif();
}

function buildQstCode() {
    const tarif   = document.getElementById('qstTarifCode')?.value ?? '';
    const kinder  = parseInt(document.getElementById('qstKinder')?.value ?? '0');
    const kirche  = document.getElementById('qstKirchensteuer')?.checked;
    if (!tarif) { qstRenderResultat(); return; }
    // QST-Code: Tarif + Anzahl Kinder + Y/N (Kirchensteuer)
    const code = `${tarif}${kinder}${kirche ? 'Y' : 'N'}`;
    const el = document.getElementById('qstCode');
    if (el && !el.value) el.value = code;
    else if (el) el.value = code;
    // Resultat-Karte unten nachziehen (K4-Vorstufe, Walter 29.08.2026).
    qstRenderResultat();
}

function toggleQstWeitere() {
    const checked = document.getElementById('qstWeitere')?.checked;
    // Walter 25.08.2026: vier Wrapper — Arbeitgeber/Strasse/Pensum + volle
    // Adresszeile (PLZ/Ort/Kanton/Land) für das Anmeldeformular.
    ['qstWeitereField1', 'qstWeitereField2', 'qstWeitereField3', 'qstWeitereField4'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.style.display = checked ? 'block' : 'none';
    });
    // Backwards-compat (falls noch irgendwo ein Element mit der alten ID existiert)
    const legacy = document.getElementById('qstWeitereFields');
    if (legacy) legacy.style.display = checked ? 'flex' : 'none';
}

async function saveQstEntry() {
    const resultEl = document.getElementById('qstSaveResult');
    const kantonSel = document.getElementById('qstSteuerkanton');
    const kantonNames = {
        AG:'Aargau',AI:'Appenzell Innerrhoden',AR:'Appenzell Ausserrhoden',BE:'Bern',
        BL:'Basel-Landschaft',BS:'Basel-Stadt',FR:'Freiburg',GE:'Genf',GL:'Glarus',
        GR:'Graubünden',JU:'Jura',LU:'Luzern',NE:'Neuenburg',NW:'Nidwalden',
        OW:'Obwalden',SG:'St. Gallen',SH:'Schaffhausen',SO:'Solothurn',SZ:'Schwyz',
        TG:'Thurgau',TI:'Tessin',UR:'Uri',VD:'Waadt',VS:'Wallis',ZG:'Zug',ZH:'Zürich'
    };
    const tarifBez = {
        A:'Tarif für alleinstehende Personen',B:'Verheiratet, Alleinverdiener',
        C:'Verheiratet, Doppelverdiener',D:'Nebenerwerb',H:'Alleinerziehend',
        L:'Grenzgänger alleinstehend',M:'Grenzgänger verheiratet',
        N:'Grenzgänger Nebenerwerb',P:'Pauschale',Q:'Grenzgänger alleinerziehend'
    };

    const payload = {
        validFrom:   document.getElementById('qstValidFrom').value || null,
        validTo:     document.getElementById('qstValidTo').value   || null,
        steuerkanton:         document.getElementById('qstSteuerkanton').value    || null,
        steuerkantonName:     kantonNames[document.getElementById('qstSteuerkanton').value] ?? null,
        qstGemeinde:          document.getElementById('qstGemeinde').value         || null,
        qstGemeindeBfsNr:     parseInt(document.getElementById('qstGemeindeBfs').value) || null,
        tarifvorschlagQst:    document.getElementById('qstTarifvorschlag').checked,
        tarifCode:            document.getElementById('qstTarifCode').value        || null,
        tarifBezeichnung:     tarifBez[document.getElementById('qstTarifCode').value] ?? null,
        anzahlKinder:         parseInt(document.getElementById('qstKinder').value) || 0,
        kirchensteuer:        document.getElementById('qstKirchensteuer').checked,
        qstCode:              document.getElementById('qstCode').value             || null,
        spezielBewilligt:     document.getElementById('qstSpezielBewilligt').checked,
        prozentsatz:          parseFloat(document.getElementById('qstProzentsatz').value) || null,
        mindestlohnSatzbestimmung: parseFloat(document.getElementById('qstMedianlohn').value) || null,
        arbeitsortKanton:     document.getElementById('qstArbeitsortKanton').value || null,
        partnerEinkommenVon:  document.getElementById('qstPartnerVon').value       || null,
        partnerEinkommenBis:  document.getElementById('qstPartnerBis').value       || null,
        weitereBeschaftigungen: document.getElementById('qstWeitere').checked,
        gesamtpensumWeitereAg:  parseFloat(document.getElementById('qstGesamtpensum').value)    || null,
        // Einkommen wird nicht mehr erfasst (Walter 25.08.2026) — Altwert
        // unverändert mitschicken, damit bestehende Daten nicht gelöscht werden.
        gesamteinkommenWeitereAg: window._qstGesEinkommenAlt ?? null,
        // Anderer Arbeitgeber des MA — volle Adresse (Anmeldeformular).
        weitereAgName:    document.getElementById('qstWagName')?.value.trim()    || null,
        weitereAgStrasse: document.getElementById('qstWagStrasse')?.value.trim() || null,
        weitereAgPlz:     document.getElementById('qstWagPlz')?.value.trim()     || null,
        weitereAgOrt:     document.getElementById('qstWagOrt')?.value.trim()     || null,
        weitereAgKanton:  (document.getElementById('qstWagKanton')?.value.trim().toUpperCase()) || null,
        weitereAgLand:    document.getElementById('qstWagLand')?.value.trim()    || null,
        halbfamilie:          qstGetHalbfamilie()                                  || null,
        wohnsitzAusland:      document.getElementById('qstWohnsitzAusland').value  || null,
        wohnsitzstaat:        document.getElementById('qstWohnsitzstaat').value    || null,
        adresseAusland:       document.getElementById('qstAdresseAusland').value   || null,

        // Tarif-relevante Stammdaten (versioniert pro QST-Eintrag,
        // fliessen ins Anmeldeformular & in die Tarifbestimmung ein)
        livesInKonkubinat:          document.getElementById('qstLivesInKonkubinat').checked,
        hasJointParentalCare:       document.getElementById('qstHasJointParentalCare').checked,
        paysAlimonyAdultChildren:   document.getElementById('qstPaysAlimonyAdultChildren').checked,
        hasHigherIncomeThanPartner: document.getElementById('qstHasHigherIncomeThanPartner').checked,
        isGrenzgaenger:             document.getElementById('qstIsGrenzgaenger').checked,
        isWochenaufenthalter:       document.getElementById('qstIsWochenaufenthalter').checked,
    };

    if (!payload.validFrom) { resultEl.innerHTML = '<span style="color:#dc2626">Gültig ab ist Pflicht.</span>'; return; }

    let url    = qstCurrentEntryId
        ? `/api/employees/${qstCurrentEmployeeId}/quellensteuer/${qstCurrentEntryId}`
        : `/api/employees/${qstCurrentEmployeeId}/quellensteuer`;
    const method = qstCurrentEntryId ? 'PUT' : 'POST';
    // K1 Korrektur-Weg: bei rückwirkender Erfassung verlangt das Backend einen
    // Grund — Feld erscheint nach dem ersten 409 KORREKTUR_GRUND_NOETIG.
    const korrGrund = document.getElementById('qstKorrGrund')?.value?.trim();
    if (method === 'POST' && korrGrund)
        url += `?korrekturGrund=${encodeURIComponent(korrGrund)}`;

    const res = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
    // Lohnlauf-Sperre: 409 LOHN_EDIT_LOCKED → klare Meldung statt Backend-Text.
    if (res.status === 409) {
        const body = await res.clone().json().catch(() => ({}));
        if (body && body.error === 'LOHN_EDIT_LOCKED') {
            resultEl.innerHTML = `<span style="color:#dc2626">${body.message}</span>`;
            if (window.lohnEditLock) window.lohnEditLock.invalidateCache();
            return;
        }
        if (body && body.error === 'KORREKTUR_GRUND_NOETIG') {
            qstShowKorrGrundRow();
            resultEl.innerHTML = `<span style="color:#b45309">${body.message}</span>`;
            return;
        }
    }
    if (!res.ok) { resultEl.innerHTML = `<span style="color:#dc2626">Fehler: ${await res.text()}</span>`; return; }

    const saved = await res.json();
    const eintrag = saved.eintrag || saved;
    qstCurrentEntryId = eintrag.id;
    const korr = saved.korrekturen;
    if (korr && korr.anzahl > 0) {
        const richtung = korr.totalDifferenz > 0 ? 'Nachbelastung' : 'Erstattung';
        const vorjahrNote = korr.vorjahr > 0
            ? ` · ${korr.vorjahr} Monat(e) aus dem Vorjahr — Abwicklung über die Steuerverwaltung` : '';
        resultEl.innerHTML = `<span style="color:#16a34a">✓ Gespeichert</span>
            <div style="margin-top:6px;background:#fef9c3;border:1px solid #fde68a;border-radius:8px;padding:8px 10px;color:#854d0e;font-size:12.5px">
            🔁 <b>${korr.anzahl} QST-Korrektur-Posten</b> erzeugt — ${richtung}
            <b>CHF ${Math.abs(korr.totalDifferenz).toLocaleString('de-CH', {minimumFractionDigits: 2})}</b>
            (Verrechnung im nächsten Lohnlauf)${vorjahrNote}</div>`;
    } else if (korr) {
        resultEl.innerHTML = '<span style="color:#16a34a">✓ Gespeichert — keine Betragsänderung in den abgeschlossenen Monaten.</span>';
    } else {
        resultEl.innerHTML = '<span style="color:#16a34a">✓ Gespeichert</span>';
    }
    await loadQstHistory(qstCurrentEmployeeId);
    // Tab im Hintergrund aktualisieren
    if (typeof loadQuellensteuerTab === 'function' && qstCurrentEmployeeId)
        loadQuellensteuerTab(qstCurrentEmployeeId);
    // Offenen Lohnzettel neu rechnen (sonst QST-Änderung erst nach Seitenwechsel sichtbar)
    if (typeof reloadLohnAfterQstChange === 'function' && qstCurrentEmployeeId) {
        reloadLohnAfterQstChange(qstCurrentEmployeeId);
    }
    // Modal nach kurzer Erfolgsmeldung automatisch schließen — bei
    // Korrektur-Posten offen lassen, damit HR die Zusammenfassung liest.
    if (!(korr && korr.anzahl > 0)) {
        setTimeout(() => {
            if (typeof closeQstModal === 'function') closeQstModal();
        }, 600);
    }
    const kg = document.getElementById('qstKorrGrundRow');
    if (kg) kg.style.display = 'none';
    const kgi = document.getElementById('qstKorrGrund');
    if (kgi) kgi.value = '';
}

// K1 (Walter 29.08.2026): Grund-Zeile für rückwirkende Erfassung — erscheint
// nach 409 KORREKTUR_GRUND_NOETIG direkt über der Ergebnis-Zeile.
function qstShowKorrGrundRow() {
    let row = document.getElementById('qstKorrGrundRow');
    if (!row) {
        const anchor = document.getElementById('qstSaveResult');
        if (!anchor) return;
        row = document.createElement('div');
        row.id = 'qstKorrGrundRow';
        row.style.cssText = 'margin:8px 0';
        row.innerHTML = `
            <label style="display:block;font-size:11.5px;font-weight:600;color:#8b8b8b;margin-bottom:3px">
                Korrektur-Grund (Pflicht bei rückwirkender Erfassung)</label>
            <input id="qstKorrGrund" type="text" placeholder="z.B. Heirat verspätet gemeldet"
                   style="width:100%;box-sizing:border-box;background:#fff;border:1px solid rgba(255,255,255,0.95);border-radius:10px;padding:7px 10px;font-size:13px;box-shadow:0 2px 6px rgba(60,55,48,0.13), inset 0 1px 0 rgba(255,255,255,0.9)">`;
        anchor.parentNode.insertBefore(row, anchor);
    }
    row.style.display = '';
    document.getElementById('qstKorrGrund')?.focus();
}
