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

    // Aktuellen Eintrag laden und anzeigen
    const today = new Date().toISOString().slice(0, 10);
    const res = await fetch(`/api/employees/${employeeId}/quellensteuer/current?date=${today}`, { headers: ah() });
    const current = res.ok ? await res.json() : null;
    populateQstForm(current);

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
            style="border:1px solid ${active ? '#2563eb' : '#d1d5db'};background:${active ? '#eff6ff' : '#fff'};
            color:${active ? '#1d4ed8' : '#374151'};border-radius:6px;padding:4px 12px;font-size:11px;cursor:pointer;font-weight:${active ? '600' : '400'}">
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
}

async function openQstEntry(id) {
    // Neuen Eintrag vorbereiten (Felder leeren, Datum auf heute)
    qstCurrentEntryId = null;
    populateQstForm(null);

    // Gültig ab: Vortrag = letzter Eintrag.gültigBis + 1 Tag, sonst heute.
    // Robustes Date-Parsing: nur YYYY-MM-DD nehmen und mit 12:00 instanziieren,
    // damit DST/Zeitzone keinen Tag verschiebt.
    const validFromDefault = (() => {
        if (Array.isArray(qstAllEntries) && qstAllEntries.length > 0) {
            const sorted = [...qstAllEntries].sort((a, b) =>
                (b.validFrom ?? '').toString().localeCompare((a.validFrom ?? '').toString()));
            const last = sorted[0];
            const validToStr = (last?.validTo ?? '').toString().slice(0, 10);
            if (validToStr && /^\d{4}-\d{2}-\d{2}$/.test(validToStr)) {
                const d = new Date(validToStr + 'T12:00:00');
                d.setDate(d.getDate() + 1);
                const yyyy = d.getFullYear();
                const mm   = String(d.getMonth() + 1).padStart(2, '0');
                const dd   = String(d.getDate()).padStart(2, '0');
                return `${yyyy}-${mm}-${dd}`;
            }
        }
        const t = new Date();
        return `${t.getFullYear()}-${String(t.getMonth()+1).padStart(2,'0')}-${String(t.getDate()).padStart(2,'0')}`;
    })();
    document.getElementById('qstValidFrom').value = validFromDefault;

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
}

function onQstKantonChange() {
    // Kanton-Kürzel in Kanton-Name umwandeln (für Speichern)
}

function onQstTarifChange() {
    buildQstCode();
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

