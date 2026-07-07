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

async function openQstModal(employeeId, employeeData) {
    qstCurrentEmployeeId = employeeId;
    qstCurrentEntryId    = null;
    qstEmployeeData      = employeeData;

    // Stammdaten anzeigen
    const permitName = employeeData?.permitTypeName ?? employeeData?.permitType ?? '–';
    const wohnort    = [employeeData?.zipCode, employeeData?.city].filter(Boolean).join(' ') || '–';
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

    // Verlauf laden
    await loadQstHistory(employeeId);

    // Ehepartner-Info aus Familie-Tab anzeigen
    loadQstPartnerInfo(employeeId);

    // Walter-Vorgabe 28.05.2026: Familie-Kinder laden für den Auto-Zähler unter
    // „Anzahl Kinder". Sequentiell, damit populateQstForm gleich darauf den Hint
    // zeichnen kann.
    await loadQstFamilyKinder(employeeId);
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
    try {
        const res = await fetch(`/api/employees/${employeeId}/family`, { headers: ah() });
        if (!res.ok) return;
        const members = await res.json();
        _qstFamilyKinder = (members || []).filter(m => m.memberType === 'Kind');
    } catch { /* leerer Cache */ }
}

// ══════════════════════════════════════════════════════════════════════
// Walter-Vorgabe 14.06.2026: Server-Tarifvorschlag (Quelle der Wahrheit).
// `qstFetchServerVorschlag` holt den Vorschlag vom Endpoint und cached ihn
// in _qstServerVorschlag. `qstApplyServerVorschlagToForm` schreibt die
// Werte ins Formular — wenn `onlyEmptyFields=true`, werden bereits manuell
// gesetzte Felder NICHT überschrieben (Walter's Edit-Modus + manuelle
// Korrektur). `qstRenderVorschlagBanner` zeigt Begründung + Warnungen.
// ══════════════════════════════════════════════════════════════════════
async function qstFetchServerVorschlag(stichtagIso) {
    _qstServerVorschlag = null;
    if (!qstCurrentEmployeeId) return null;
    const date = (stichtagIso || '').toString().slice(0, 10) ||
                 new Date().toISOString().slice(0, 10);
    try {
        const res = await fetch(
            `/api/employees/${qstCurrentEmployeeId}/quellensteuer/vorschlag?date=${date}`,
            { headers: ah() });
        if (!res.ok) return null;
        _qstServerVorschlag = await res.json();
        return _qstServerVorschlag;
    } catch {
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
    const passt  = manual && manual === v.tarifCode;
    const headerColor = passt ? '#16a34a' : '#6b7280';
    const headerIcon  = passt ? '✓' : 'ℹ';
    const begr   = v.begruendung ? `<div style="color:#475569;margin-top:2px">${v.begruendung}</div>` : '';
    const warns  = (v.warnings && v.warnings.length)
        ? `<div style="color:#b45309;margin-top:2px">⚠ ${v.warnings.map(w => w.replace(/"/g,'&quot;')).join(' · ')}</div>`
        : '';
    const choice = (manual && manual !== v.tarifCode)
        ? `<div style="color:#94a3b8;margin-top:2px">Du hast bewusst <b>${manual}</b> gewählt.</div>`
        : '';
    hint.innerHTML =
        `<div style="color:${headerColor};font-weight:600">${headerIcon} Server-Vorschlag: <b>${v.qstCode}</b> (Tarif ${v.tarifCode}${v.tarifBezeichnung ? ' — ' + v.tarifBezeichnung : ''})</div>` +
        begr + warns + choice;
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

// Hint-Zeile unter „Anzahl Kinder": grün wenn manuell == auto,
// rot mit „Auto übernehmen"-Button wenn Differenz.
function qstUpdateAutoKinderHint() {
    const inp  = document.getElementById('qstKinder');
    const hint = document.getElementById('qstKinderAutoHint');
    if (!inp || !hint) return;
    const stichtag = document.getElementById('qstValidFrom')?.value || '';
    if (!stichtag) { hint.innerHTML = ''; return; }
    const auto   = qstAutoKinderCount(stichtag);
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

    if (manual === auto) {
        hint.innerHTML = `<span style="color:#16a34a">✓ ${auto} Kind${auto===1?'':'er'} QST-abzugsberechtigt am ${stichtagDe} (${quelle})</span>`;
    } else {
        hint.innerHTML = `
            <span style="color:#dc2626">⚠ Auto: ${auto} (${quelle}), manuell eingetragen: ${manual}</span>
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
    inp.value = qstAutoKinderCount(stichtag);
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
        hint.innerHTML = `<span style="color:#94a3b8">ℹ Vorschlag aus Zivilstand wäre <b>${suggested}</b> — du hast bewusst <b>${sel.value}</b> gewählt. (Server-Vorschlag nicht verfügbar.)</span>`;
    } else if (suggested) {
        hint.innerHTML = `<span style="color:#94a3b8">ℹ Vorschlag aus Zivilstand: <b>${suggested}</b> (Server-Vorschlag nicht verfügbar — bitte manuell prüfen).</span>`;
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

function populateQstForm(entry) {
    const v = (id, val) => { const el = document.getElementById(id); if (el) el.value = val ?? ''; };
    const c = (id, val) => { const el = document.getElementById(id); if (el) el.checked = !!val; };

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
    v('qstGesamteinkommen',entry?.gesamteinkommenWeitereAg ?? '');
    v('qstHalbfamilie',    entry?.halbfamilie              ?? '');
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
    if (!tarif) return;
    // QST-Code: Tarif + Anzahl Kinder + Y/N (Kirchensteuer)
    const code = `${tarif}${kinder}${kirche ? 'Y' : 'N'}`;
    const el = document.getElementById('qstCode');
    if (el && !el.value) el.value = code;
    else if (el) el.value = code;
}

function toggleQstWeitere() {
    const checked = document.getElementById('qstWeitere')?.checked;
    // Im neuen kompakten Layout: zwei separate Wrapper im 3-Spalten-Grid
    const f1 = document.getElementById('qstWeitereField1');
    const f2 = document.getElementById('qstWeitereField2');
    if (f1) f1.style.display = checked ? 'block' : 'none';
    if (f2) f2.style.display = checked ? 'block' : 'none';
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
        gesamteinkommenWeitereAg: parseFloat(document.getElementById('qstGesamteinkommen').value) || null,
        halbfamilie:          document.getElementById('qstHalbfamilie').value      || null,
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

    const url    = qstCurrentEntryId
        ? `/api/employees/${qstCurrentEmployeeId}/quellensteuer/${qstCurrentEntryId}`
        : `/api/employees/${qstCurrentEmployeeId}/quellensteuer`;
    const method = qstCurrentEntryId ? 'PUT' : 'POST';

    const res = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
    // Lohnlauf-Sperre: 409 LOHN_EDIT_LOCKED → klare Meldung statt Backend-Text.
    if (res.status === 409) {
        const body = await res.clone().json().catch(() => ({}));
        if (body && body.error === 'LOHN_EDIT_LOCKED') {
            resultEl.innerHTML = `<span style="color:#dc2626">${body.message}</span>`;
            if (window.lohnEditLock) window.lohnEditLock.invalidateCache();
            return;
        }
    }
    if (!res.ok) { resultEl.innerHTML = `<span style="color:#dc2626">Fehler: ${await res.text()}</span>`; return; }

    const saved = await res.json();
    qstCurrentEntryId = saved.id;
    resultEl.innerHTML = '<span style="color:#16a34a">✓ Gespeichert</span>';
    await loadQstHistory(qstCurrentEmployeeId);
    // Tab im Hintergrund aktualisieren
    if (typeof loadQuellensteuerTab === 'function' && qstCurrentEmployeeId)
        loadQuellensteuerTab(qstCurrentEmployeeId);
    // Modal nach kurzer Erfolgsmeldung automatisch schließen
    setTimeout(() => {
        if (typeof closeQstModal === 'function') closeQstModal();
    }, 600);
}

