// ══════════════════════════════════════════════
// MITARBEITER VERWALTUNG
// ══════════════════════════════════════════════

let allEmployees = [];
let selectedEmployeeId = null;
let selectedEmployee   = null;   // Ganzes Mitarbeiter-Objekt (für Sollstunden etc.)
let activeEmpTab = 'personal';

// ── Liste laden ────────────────────────────────
async function loadMitarbeiterList() {
    try {
        const res = await fetch('/api/employees', { headers: ah() });
        if (!res.ok) return;
        allEmployees = await res.json();
        allEmployees.sort((a, b) => {
            const na = ((a.firstName ?? '') + ' ' + (a.lastName ?? '')).trim().toLowerCase();
            const nb = ((b.firstName ?? '') + ' ' + (b.lastName ?? '')).trim().toLowerCase();
            return na.localeCompare(nb, 'de');
        });
        renderEmployeeList(allEmployees);
    } catch (e) {
        document.getElementById('empList').innerHTML =
            '<div class="emp-no-selection" style="height:200px"><span>Fehler beim Laden</span></div>';
    }
}

// ── Liste rendern ──────────────────────────────
function renderEmployeeList(employees) {
    const el = document.getElementById('empList');
    if (!employees.length) {
        el.innerHTML = '<div class="emp-no-selection" style="height:200px"><span>Keine Mitarbeiter gefunden</span></div>';
        return;
    }
    el.innerHTML = employees.map(e => {
        const name = ((e.firstName ?? '') + ' ' + (e.lastName ?? '')).trim() || '–';
        const initials = getInitials(e.firstName, e.lastName);
        const isFemale = (e.gender ?? '').toLowerCase().startsWith('w') || (e.gender ?? '').toLowerCase() === 'female';
        const active = e.id === selectedEmployeeId ? 'active' : '';
        return `
        <div class="emp-list-item ${active}" onclick="selectEmployee(${e.id})">
            <div class="emp-avatar ${isFemale ? 'female' : ''}">${initials}</div>
            <div>
                <div class="emp-list-name">${name}</div>
                <div class="emp-list-nr">${e.employeeNumber ?? ''}</div>
            </div>
        </div>`;
    }).join('');
}

// ── Suche/Filter ───────────────────────────────
function filterEmployeeList() {
    const q = (document.getElementById('empSearch')?.value ?? '').toLowerCase();
    if (!q) { renderEmployeeList(allEmployees); return; }
    const filtered = allEmployees.filter(e => {
        const name = ((e.firstName ?? '') + ' ' + (e.lastName ?? '')).toLowerCase();
        return name.includes(q) || (e.employeeNumber ?? '').toLowerCase().includes(q);
    });
    renderEmployeeList(filtered);
}

// ── Mitarbeiter auswählen ──────────────────────
async function selectEmployee(id) {
    selectedEmployeeId = id;
    // Aktiven Eintrag in Liste markieren
    document.querySelectorAll('.emp-list-item').forEach(el => {
        el.classList.toggle('active', parseInt(el.onclick?.toString().match(/\d+/)?.[0]) === id);
    });
    // Re-render list to update active state
    const q = document.getElementById('empSearch')?.value ?? '';
    const list = q ? allEmployees.filter(e => {
        const name = ((e.firstName ?? '') + ' ' + (e.lastName ?? '')).toLowerCase();
        return name.includes(q.toLowerCase()) || (e.employeeNumber ?? '').toLowerCase().includes(q.toLowerCase());
    }) : allEmployees;
    renderEmployeeList(list);

    // Detail laden
    try {
        const res = await fetch(`/api/employees/${id}`, { headers: ah() });
        if (!res.ok) return;
        const emp = await res.json();
        selectedEmployee = emp;
        renderEmployeeDetail(emp);
    } catch {}
}

// ── Detail rendern ─────────────────────────────
function renderEmployeeDetail(emp) {
    const panel = document.getElementById('empDetailPanel');
    const name = ((emp.firstName ?? '') + ' ' + (emp.lastName ?? '')).trim() || '–';
    const entry = emp.entryDate ? formatDate(emp.entryDate) : '–';
    const exit  = emp.exitDate  ? formatDate(emp.exitDate)  : 'Aktiv';
    const nr    = emp.employeeNumber ?? '–';

    panel.innerHTML = `
    <div class="emp-detail-header">
        <div style="display:flex;align-items:flex-start;justify-content:space-between;gap:12px">
            <div>
                <div class="emp-detail-name">${name}</div>
                <div class="emp-detail-meta">Personal-Nr. ${nr} &nbsp;·&nbsp; Eintritt: ${entry} &nbsp;·&nbsp; ${emp.exitDate ? 'Austritt: ' + exit : '<span style="color:#22c55e">● Aktiv</span>'}</div>
            </div>
            <button class="btn-emp-edit" style="margin-top:4px;white-space:nowrap;flex-shrink:0" onclick="startEmpEdit()">
                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
                Bearbeiten
            </button>
        </div>
        <div class="emp-detail-tabs">
            <div class="emp-tab active" data-tab="personal"   onclick="switchEmpTab('personal')">Persönliche Angaben</div>
            <div class="emp-tab"        data-tab="adressen"   onclick="switchEmpTab('adressen')">Adressen</div>
            <div class="emp-tab"        data-tab="familie"    onclick="switchEmpTab('familie')">Familie</div>
            <div class="emp-tab"        data-tab="quellensteuer" onclick="switchEmpTab('quellensteuer')">Quellensteuer</div>
            <div class="emp-tab"        data-tab="stempelzeiten" onclick="switchEmpTab('stempelzeiten')">Stempelzeiten</div>
            <div class="emp-tab"        data-tab="absenzen"   onclick="switchEmpTab('absenzen')">Absenzen</div>
            <div class="emp-tab"        data-tab="zulagen"    onclick="switchEmpTab('zulagen')">Zulagen/Abzüge</div>
            <div class="emp-tab"        data-tab="formulare"  onclick="switchEmpTab('formulare')">Formulare</div>
        </div>
    </div>
    <div class="emp-detail-body">
        <!-- TAB: Persönliche Angaben -->
        <div class="emp-tab-content active" id="emp-tab-personal">
            <div class="emp-section-title">Personalien</div>
            <div class="emp-field-grid">
                ${field('Vorname',        emp.firstName)}
                ${field('Nachname',       emp.lastName)}
                ${field('Kurzname',       emp.shortName)}
                ${field('Ledigname',      emp.maidenName)}
                ${field('Geburtsdatum',   emp.dateOfBirth ? formatDate(emp.dateOfBirth) : null)}
                ${field('Geschlecht',     emp.gender)}
                ${field('AHV-Nummer',     emp.socialSecurityNumber)}
                ${field('Zivilstand',     emp.zivilstand)}
                ${field('Sprache',        emp.languageCode)}
                ${field('Anrede',         emp.salutation)}
                ${field('Briefanrede',    emp.letterSalutation)}
                ${field('Heimatort',      emp.placeOfOrigin)}
                ${field('Konfession',     emp.religion)}
                ${field('Nationalität',   emp.nationality)}
            </div>
            <div class="emp-section-title">Aufenthalt</div>
            <div class="emp-field-grid">
                ${field('Bewilligung',       emp.permitTypeId ? 'Typ ' + emp.permitTypeId : null)}
                ${field('Gültig bis',        emp.permitExpiryDate ? formatDate(emp.permitExpiryDate) : null)}
                ${field('ZEMIS-Nr.',         emp.zemisNumber)}
            </div>
            <div class="emp-section-title">Arbeitsverhältnis</div>
            <div class="emp-field-grid">
                ${field('Personal-Nr.',      emp.employeeNumber)}
                ${field('Eintritt',          emp.entryDate ? formatDate(emp.entryDate) : null)}
                ${field('Austritt',          emp.exitDate  ? formatDate(emp.exitDate)  : null)}
                ${field('Modell',            emp.employmentModel)}
                ${field('Pensum',            emp.employmentPercentage != null ? emp.employmentPercentage + ' %' : null)}
                ${field('Wochenstunden',     emp.weeklyHours != null ? emp.weeklyHours + ' h' : null)}
                ${field('Vertragsbeginn',    emp.contractStartDate ? formatDate(emp.contractStartDate) : null)}
                ${field('Vertragsart',       emp.employmentModel === 'MTP' && emp.guaranteedHoursPerWeek != null
                    ? (emp.contractType ? emp.contractType + ' · ' + emp.guaranteedHoursPerWeek + ' Std./Woche' : emp.guaranteedHoursPerWeek + ' Std./Woche')
                    : emp.contractType)}
                ${emp.employmentModel !== 'UTP' && emp.hourlyRate != null
                    ? field('Stundenlohn', 'CHF ' + Number(emp.hourlyRate).toFixed(2))
                    : emp.monthlySalary != null
                    ? field('Monatslohn', 'CHF ' + Number(emp.monthlySalary).toFixed(2))
                    : ''}
            </div>
        </div>

        <!-- TAB: Adressen -->
        <div class="emp-tab-content" id="emp-tab-adressen">
            <div class="emp-section-title">Hauptadresse</div>
            <div class="emp-field-grid">
                ${field('Strasse',      emp.street ? emp.street + (emp.houseNumber ? ' ' + emp.houseNumber : '') : null)}
                ${field('Strasse 2',    emp.street2)}
                ${field('Postfach',     emp.poBox)}
                ${field('PLZ / Ort',    emp.zipCode ? emp.zipCode + ' ' + (emp.city ?? '') : emp.city)}
                ${field('BFS-Nr.',      emp.bfsNumber)}
                ${field('Kanton',       emp.canton)}
                ${field('Land',         emp.country)}
            </div>
            <div class="emp-section-title">Kontakt</div>
            <div class="emp-field-grid">
                ${field('Telefon',      emp.phoneMobile)}
                ${field('Telefon 2',    emp.phone2)}
                ${field('E-Mail',       emp.email)}
                ${field('IncaMail',     emp.incamailDisabled ? 'Deaktiviert' : 'Aktiv')}
            </div>
        </div>

        <!-- TAB: Familie -->
        <div class="emp-tab-content" id="emp-tab-familie">
            <div id="familieContent">
                <div class="emp-placeholder">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
                    <span>Wird geladen...</span>
                </div>
            </div>
        </div>

        <!-- TAB: Quellensteuer -->
        <div class="emp-tab-content" id="emp-tab-quellensteuer">
            <div id="quellensteuerContent">
                <div class="emp-placeholder">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/></svg>
                    <span>Wird geladen...</span>
                </div>
            </div>
        </div>

        <!-- TAB: Stempelzeiten -->
        <div class="emp-tab-content" id="emp-tab-stempelzeiten">
            <div id="stempelzeitenContent">
                <div class="emp-placeholder">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                    <span>Bitte wählen Sie einen Mitarbeiter</span>
                </div>
            </div>
        </div>

        <!-- TAB: Absenzen -->
        <div class="emp-tab-content" id="emp-tab-absenzen">
            <div id="absenzenContent">
                <div class="emp-placeholder">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>
                    <span>Bitte wählen Sie einen Mitarbeiter</span>
                </div>
            </div>
        </div>

        <!-- TAB: Zulagen/Abzüge -->
        <div class="emp-tab-content" id="emp-tab-zulagen">
            <div id="zulagenContent">
                <div class="emp-placeholder">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                    <span>Bitte wählen Sie einen Mitarbeiter</span>
                </div>
            </div>
        </div>

        <!-- TAB: Formulare -->
        <div class="emp-tab-content" id="emp-tab-formulare">

          <!-- Arbeitslosigkeit -->
          <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px">
            <h3 style="margin:0;font-size:14px;font-weight:600">Arbeitslosigkeit (ALV-Meldungen)</h3>
            <button class="btn-primary" onclick="openArbeitslosigkeitForm()">+ Periode erfassen</button>
          </div>
          <div id="arbeitslosigkeitAlert"></div>
          <div id="arbeitslosigkeitInlineForm" style="display:none;background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:16px;margin-bottom:16px">
            <input type="hidden" id="alId">
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-bottom:12px">
              <label style="font-size:12px;font-weight:500">Angemeldet seit *
                <input type="date" id="alAngemeldetSeit" class="form-control" style="margin-top:4px">
              </label>
              <label style="font-size:12px;font-weight:500">Abgemeldet am (leer = noch aktiv)
                <input type="date" id="alAbgemeldetAm" class="form-control" style="margin-top:4px">
              </label>
              <label style="font-size:12px;font-weight:500">RAV-Stelle
                <input type="text" id="alRavStelle" class="form-control" placeholder="z.B. RAV Luzern" style="margin-top:4px">
              </label>
              <label style="font-size:12px;font-weight:500">Kundennummer RAV
                <input type="text" id="alRavKundennummer" class="form-control" style="margin-top:4px">
              </label>
              <label style="font-size:12px;font-weight:500">Arbeitslosenkasse
                <input type="text" id="alArbeitslosenkasse" class="form-control" placeholder="z.B. UNIA" style="margin-top:4px">
              </label>
              <label style="font-size:12px;font-weight:500">Bemerkung
                <input type="text" id="alBemerkung" class="form-control" style="margin-top:4px">
              </label>
            </div>
            <div style="display:flex;gap:8px">
              <button class="btn-primary" onclick="saveArbeitslosigkeit()">Speichern</button>
              <button class="btn-secondary" onclick="closeArbeitslosigkeitForm()">Abbrechen</button>
            </div>
          </div>
          <div id="arbeitslosigkeitList" style="margin-bottom:32px"></div>

          <!-- Zwischenverdienst-Bescheinigung -->
          <h3 style="font-size:14px;font-weight:600;margin-bottom:10px;border-top:1px solid #e2e8f0;padding-top:16px">
            Bescheinigung Zwischenverdienst (ALV 716.105)
          </h3>
          <div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:16px;display:flex;align-items:flex-end;gap:12px;flex-wrap:wrap">
            <label style="font-size:12px;font-weight:500">Monat
              <select id="zvMonat" class="form-control" style="margin-top:4px;width:130px">
                <option value="1">Januar</option><option value="2">Februar</option>
                <option value="3">März</option><option value="4">April</option>
                <option value="5">Mai</option><option value="6">Juni</option>
                <option value="7">Juli</option><option value="8">August</option>
                <option value="9">September</option><option value="10">Oktober</option>
                <option value="11">November</option><option value="12">Dezember</option>
              </select>
            </label>
            <label style="font-size:12px;font-weight:500">Jahr
              <input type="number" id="zvJahr" class="form-control" value="${new Date().getFullYear()}" min="2020" max="2099" style="margin-top:4px;width:90px">
            </label>
            <button class="btn-primary" onclick="generateZwischenverdienst()" style="margin-bottom:1px">
              PDF generieren
            </button>
          </div>
          <p style="font-size:11px;color:#94a3b8;margin-top:8px">
            Das PDF wird mit den erfassten Stempelzeiten und Absenzen des gewählten Monats vorausgefüllt.
            AHV-Nr. und Zivilstand müssen unter «Persönliche Angaben» hinterlegt sein.
          </p>
        </div>
    </div>`;

    activeEmpTab = 'personal';
}

// ── Tab wechseln ───────────────────────────────
function switchEmpTab(tab) {
    activeEmpTab = tab;
    document.querySelectorAll('.emp-tab').forEach(t =>
        t.classList.toggle('active', t.dataset.tab === tab));
    document.querySelectorAll('.emp-tab-content').forEach(c =>
        c.classList.toggle('active', c.id === 'emp-tab-' + tab));
    if (tab === 'familie'        && selectedEmployeeId) loadFamilieTab(selectedEmployeeId);
    if (tab === 'quellensteuer'  && selectedEmployeeId) loadQuellensteuerTab(selectedEmployeeId);
    if (tab === 'stempelzeiten'  && selectedEmployeeId) loadStempelzeitenTab(selectedEmployeeId);
    if (tab === 'absenzen'       && selectedEmployeeId) loadAbsenzenTab(selectedEmployeeId);
    if (tab === 'zulagen'        && selectedEmployeeId) loadZulagenTab(selectedEmployeeId);
    if (tab === 'formulare'      && selectedEmployeeId) loadFormulareTab(selectedEmployeeId);
}

// ══════════════════════════════════════════════
// QUELLENSTEUER TAB
// ══════════════════════════════════════════════

let qstOpenedFromTab = false;

async function loadQuellensteuerTab(employeeId) {
    const el = document.getElementById('quellensteuerContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen...</span></div>';
    try {
        const res = await fetch(`/api/employees/${employeeId}/quellensteuer`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>'; return; }
        const entries = await res.json();
        renderQuellensteuerTab(el, entries);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler</span></div>';
    }
}

function renderQuellensteuerTab(el, entries) {
    const toolbar = `
    <div class="emp-familie-toolbar">
        <button class="btn-emp-add" onclick="openQstFromTab(null)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Neuer Eintrag
        </button>
    </div>`;

    if (!entries.length) {
        el.innerHTML = toolbar + `
        <div class="emp-placeholder">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/></svg>
            <span>Keine Quellensteuer-Einträge erfasst</span>
        </div>`;
        return;
    }

    // Neueste zuerst
    const sorted = [...entries].sort((a, b) => (b.validFrom ?? '').localeCompare(a.validFrom ?? ''));
    let html = toolbar;

    sorted.forEach(e => {
        const isCurrent = !e.validTo;
        const vonStr   = e.validFrom ? formatDate(e.validFrom) : '–';
        const bisStr   = e.validTo   ? formatDate(e.validTo)   : 'offen';
        const kanton   = e.steuerkanton   ?? '–';
        const code     = e.qstCode        ?? (e.tarifCode ? `${e.tarifCode}${e.anzahlKinder ?? 0}${e.kirchensteuer ? 'Y' : 'N'}` : '–');
        const kinder   = e.anzahlKinder   ?? 0;
        const kirche   = e.kirchensteuer  ? 'mit Kirchensteuer' : 'ohne Kirchensteuer';
        const pct      = e.prozentsatz    ? ` · ${Number(e.prozentsatz).toFixed(2)} %` : '';
        const gemeinde = e.qstGemeinde    ? ` · ${e.qstGemeinde}` : '';

        html += `
        <div class="emp-family-card" style="border-left:3px solid ${isCurrent ? '#2563eb' : '#e2e8f0'};margin-bottom:12px">
            <div class="emp-family-card-head">
                <div>
                    <div class="emp-family-name" style="display:flex;align-items:center;gap:8px">
                        ${isCurrent ? '<span style="background:#eff6ff;color:#1d4ed8;font-size:10px;font-weight:700;padding:2px 7px;border-radius:10px;letter-spacing:.04em">AKTUELL</span>' : ''}
                        <span>${vonStr} → ${bisStr}</span>
                    </div>
                    <div style="font-size:12px;color:#64748b;margin-top:3px">
                        Kanton <strong>${kanton}</strong> · Code <strong>${code}</strong> · ${kinder} Kinder · ${kirche}${pct}${gemeinde}
                    </div>
                </div>
                <button class="btn-emp-edit" onclick="openQstFromTab(${e.id})">
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
                    Bearbeiten
                </button>
            </div>
        </div>`;
    });

    el.innerHTML = html;
}

async function openQstFromTab(entryId) {
    if (!selectedEmployeeId || !selectedEmployee) return;
    qstOpenedFromTab    = true;
    qstCurrentEmployeeId = selectedEmployeeId;
    qstCurrentEntryId    = entryId ?? null;

    const emp = selectedEmployee;

    // Stammdaten im Modal setzen
    const setTxt = (id, val) => { const el = document.getElementById(id); if (el) el.textContent = val; };
    setTxt('qstModalSub',         `${emp.firstName ?? ''} ${emp.lastName ?? ''}`.trim());
    setTxt('qstPermitDisplay',    emp.permitTypeId  ? 'Typ ' + emp.permitTypeId : '–');
    setTxt('qstWohnortDisplay',   [emp.zipCode, emp.city].filter(Boolean).join(' ') || '–');
    setTxt('qstNatDisplay',       emp.nationality ?? '–');
    setTxt('qstZivilstandDisplay','–');

    // Verlauf laden
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer`, { headers: ah() });
        qstAllEntries = res.ok ? await res.json() : [];
    } catch { qstAllEntries = []; }
    if (typeof renderQstHistoryTabs === 'function') renderQstHistoryTabs();

    // Formular befüllen
    if (entryId) {
        try {
            const r = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer/${entryId}`, { headers: ah() });
            if (r.ok) populateQstForm(await r.json());
        } catch {}
    } else {
        populateQstForm(null);
        const vf = document.getElementById('qstValidFrom');
        if (vf) vf.value = new Date().toISOString().slice(0, 10);
    }

    document.getElementById('qstSaveResult').textContent = '';
    document.getElementById('qstModal').style.display = 'flex';
}

// ── Familie Tab laden ──────────────────────────
async function loadFamilieTab(employeeId) {
    const el = document.getElementById('familieContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen...</span></div>';
    try {
        const res = await fetch(`/api/employees/${employeeId}/family`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>'; return; }
        const members = await res.json();
        renderFamilieTab(el, members, employeeId);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler</span></div>';
    }
}

function renderFamilieTab(el, members, employeeId) {
    const toolbar = `
    <div class="emp-familie-toolbar">
        <button class="btn-emp-add" onclick="openFamilyModal(null)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Hinzufügen
        </button>
    </div>`;

    if (!members.length) {
        el.innerHTML = toolbar + `
        <div class="emp-placeholder">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
            <span>Keine Familienangehörigen erfasst</span>
        </div>`;
        return;
    }

    // Gruppieren nach Typ
    const groups = {};
    members.forEach(m => {
        if (!groups[m.memberType]) groups[m.memberType] = [];
        groups[m.memberType].push(m);
    });

    const typeOrder = ['Ehepartner', 'Kind', 'Mutter', 'Vater', 'Sonstige'];
    let html = toolbar;

    typeOrder.forEach(type => {
        if (!groups[type]) return;
        html += `<div class="emp-section-title">${type === 'Kind' && groups[type].length > 1 ? 'Kinder' : type}</div>`;
        groups[type].forEach(m => {
            const name = ((m.firstName ?? '') + ' ' + (m.lastName ?? '')).trim() || '–';
            const dob  = m.dateOfBirth ? formatDate(m.dateOfBirth) : null;
            const age  = m.dateOfBirth ? calcAge(m.dateOfBirth) : null;
            html += `
            <div class="emp-family-card">
                <div class="emp-family-card-head">
                    <div class="emp-family-name">${name}</div>
                    <div style="display:flex;align-items:center;gap:8px">
                        ${dob ? `<div class="emp-family-meta">${dob}${age !== null ? ' · ' + age + ' Jahre' : ''}</div>` : ''}
                        <button class="btn-emp-edit" onclick="openFamilyModal(${JSON.stringify(m).replace(/"/g, '&quot;')})">
                            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
                            Bearbeiten
                        </button>
                        <button class="btn-emp-del" onclick="deleteFamilyMember(${m.id})">
                            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/></svg>
                            Löschen
                        </button>
                    </div>
                </div>
                <div class="emp-field-grid" style="margin-top:10px">
                    ${field('AHV-Nummer',        m.socialSecurityNumber)}
                    ${field('In der Schweiz',     m.livesInSwitzerland ? 'Ja' : 'Nein')}
                    ${field('Zulage 1 bis',       m.allowance1Until ? formatMonthYear(m.allowance1Until) : null)}
                    ${field('Zulage 2 bis',       m.allowance2Until ? formatMonthYear(m.allowance2Until) : null)}
                    ${field('Zulage 3 bis',       m.allowance3Until ? formatMonthYear(m.allowance3Until) : null)}
                    ${field('QST ab',             m.qstDeductibleFrom  ? formatDate(m.qstDeductibleFrom)  : null)}
                    ${field('QST bis',            m.qstDeductibleUntil ? formatDate(m.qstDeductibleUntil) : null)}
                </div>
            </div>`;
        });
    });

    el.innerHTML = html;
}

function calcAge(dateStr) {
    const dob = new Date(dateStr);
    if (isNaN(dob)) return null;
    const today = new Date();
    let age = today.getFullYear() - dob.getFullYear();
    if (today < new Date(today.getFullYear(), dob.getMonth(), dob.getDate())) age--;
    return age;
}

function formatMonthYear(dateStr) {
    if (!dateStr) return '–';
    const d = new Date(dateStr);
    if (isNaN(d)) return dateStr;
    return d.toLocaleDateString('de-CH', { month: '2-digit', year: 'numeric' });
}

// ── Hilfsfunktionen ────────────────────────────
function field(label, value) {
    const empty = !value || value === 'null' || value === 'undefined';
    return `<div class="emp-field">
        <div class="emp-field-label">${label}</div>
        <div class="emp-field-value${empty ? ' empty' : ''}">${empty ? '–' : value}</div>
    </div>`;
}

function getInitials(first, last) {
    const f = (first ?? '').trim()[0] ?? '';
    const l = (last  ?? '').trim()[0] ?? '';
    return (f + l).toUpperCase() || '?';
}

function formatDate(dateStr) {
    if (!dateStr) return '–';
    const d = new Date(dateStr);
    if (isNaN(d)) return dateStr;
    return d.toLocaleDateString('de-CH');
}

// ══════════════════════════════════════════════
// MITARBEITER BEARBEITEN
// ══════════════════════════════════════════════

function startEmpEdit() {
    if (!selectedEmployee) return;
    const emp = selectedEmployee;

    // Personal-Tab mit Formularfeldern ersetzen
    const personalTab = document.getElementById('emp-tab-personal');
    if (personalTab) personalTab.innerHTML = buildEmpEditPersonal(emp);

    // Adressen-Tab mit Formularfeldern ersetzen
    const adressenTab = document.getElementById('emp-tab-adressen');
    if (adressenTab) adressenTab.innerHTML = buildEmpEditAdressen(emp);

    // Header-Button durch Speichern/Abbrechen ersetzen
    const btn = document.querySelector('.emp-detail-header .btn-emp-edit');
    if (btn) btn.outerHTML = `
        <div style="display:flex;gap:8px;margin-top:4px;flex-shrink:0">
            <button class="btn-primary" style="padding:6px 16px;font-size:13px" onclick="saveEmpEdit()">Speichern</button>
            <button class="btn-secondary" style="padding:6px 14px;font-size:13px" onclick="cancelEmpEdit()">Abbrechen</button>
        </div>`;
}

function buildEmpEditPersonal(emp) {
    const isMtp = emp.employmentModel === 'MTP';
    const isFix = emp.employmentModel === 'FIX' || emp.employmentModel === 'FIX-M';
    return `
    <div class="emp-section-title">Personalien</div>
    <div class="emp-field-grid">
        ${eField('Anrede', `<select id="ef-salutation" class="ef-input">
            <option value="">–</option>
            <option value="Herr" ${emp.salutation==='Herr'?'selected':''}>Herr</option>
            <option value="Frau" ${emp.salutation==='Frau'?'selected':''}>Frau</option>
            <option value="Divers" ${emp.salutation==='Divers'?'selected':''}>Divers</option>
        </select>`)}
        ${eField('Vorname',    `<input id="ef-firstName" class="ef-input" value="${esc(emp.firstName)}">`)}
        ${eField('Nachname',   `<input id="ef-lastName"  class="ef-input" value="${esc(emp.lastName)}">`)}
        ${eField('Geburtsdatum', `<input id="ef-dob" class="ef-input" type="date" value="${toDateInput(emp.dateOfBirth)}">`)}
        ${eField('Geschlecht', `<select id="ef-gender" class="ef-input">
            <option value="">–</option>
            <option value="male"   ${emp.gender==='male'  ?'selected':''}>Männlich</option>
            <option value="female" ${emp.gender==='female'?'selected':''}>Weiblich</option>
        </select>`)}
        ${eField('AHV-Nummer', `<input id="ef-ahvNummer" class="ef-input" placeholder="756.XXXX.XXXX.XX" value="${esc(emp.socialSecurityNumber)}">`)}
        ${eField('Zivilstand', `<select id="ef-zivilstand" class="ef-input">
            <option value="">–</option>
            <option value="ledig"                    ${emp.zivilstand==='ledig'                   ?'selected':''}>Ledig</option>
            <option value="verheiratet"              ${emp.zivilstand==='verheiratet'             ?'selected':''}>Verheiratet</option>
            <option value="geschieden"               ${emp.zivilstand==='geschieden'              ?'selected':''}>Geschieden</option>
            <option value="verwitwet"                ${emp.zivilstand==='verwitwet'               ?'selected':''}>Verwitwet</option>
            <option value="eingetragene_partnerschaft" ${emp.zivilstand==='eingetragene_partnerschaft'?'selected':''}>Eingetr. Partnerschaft</option>
            <option value="aufgeloeste_partnerschaft"  ${emp.zivilstand==='aufgeloeste_partnerschaft' ?'selected':''}>Aufgel. Partnerschaft</option>
        </select>`)}
        ${eField('Sprache', `<select id="ef-lang" class="ef-input">
            <option value="">–</option>
            <option value="de" ${emp.languageCode==='de'?'selected':''}>Deutsch</option>
            <option value="fr" ${emp.languageCode==='fr'?'selected':''}>Französisch</option>
            <option value="it" ${emp.languageCode==='it'?'selected':''}>Italienisch</option>
            <option value="en" ${emp.languageCode==='en'?'selected':''}>Englisch</option>
        </select>`)}
        ${eField('Telefon',    `<input id="ef-phone"    class="ef-input" type="tel"   value="${esc(emp.phoneMobile)}">`)}
        ${eField('E-Mail',     `<input id="ef-email"    class="ef-input" type="email" value="${esc(emp.email)}">`)}
    </div>

    <div class="emp-section-title">Aufenthalt</div>
    <div class="emp-field-grid">
        ${eField('Bewilligung', `<select id="ef-permitType" class="ef-input">
            <option value="0">–</option>
            <option value="1" ${emp.permitTypeId==1?'selected':''}>Ausweis B</option>
            <option value="2" ${emp.permitTypeId==2?'selected':''}>Ausweis C</option>
            <option value="3" ${emp.permitTypeId==3?'selected':''}>Ausweis G</option>
            <option value="4" ${emp.permitTypeId==4?'selected':''}>Ausweis L</option>
            <option value="5" ${emp.permitTypeId==5?'selected':''}>Ausweis F</option>
            <option value="6" ${emp.permitTypeId==6?'selected':''}>Ausweis N</option>
            <option value="7" ${emp.permitTypeId==7?'selected':''}>Ausweis S</option>
        </select>`)}
        ${eField('Gültig bis', `<input id="ef-permitExpiry" class="ef-input" type="date" value="${toDateInput(emp.permitExpiryDate)}">`)}
    </div>

    <div class="emp-section-title">Arbeitsverhältnis</div>
    <div class="emp-field-grid">
        ${eField('Eintritt',   `<input id="ef-entry" class="ef-input" type="date" value="${toDateInput(emp.entryDate)}">`)}
        ${eField('Austritt',   `<input id="ef-exit"  class="ef-input" type="date" value="${toDateInput(emp.exitDate)}">`)}
        ${eField('Stellenbezeichnung', `<input id="ef-jobTitle" class="ef-input" value="${esc(emp.jobTitle)}">`)}
        ${eField('Pensum (%)', `<input id="ef-pct" class="ef-input" type="number" step="0.01" min="0" max="100" value="${emp.employmentPercentage ?? ''}">`)}
        ${eField('Wochenstunden', `<input id="ef-wh" class="ef-input" type="number" step="0.5" min="0" value="${emp.weeklyHours ?? ''}">`)}
        ${isMtp ? eField('Garantierte Std./Woche', `<input id="ef-gh" class="ef-input" type="number" step="0.5" min="0" value="${emp.guaranteedHoursPerWeek ?? ''}">`) : ''}
        ${!isFix ? eField('Stundenlohn (CHF)', `<input id="ef-hourly" class="ef-input" type="number" step="0.05" min="0" value="${emp.hourlyRate ?? ''}">`) : ''}
        ${isFix  ? eField('Monatslohn (CHF)',  `<input id="ef-monthly" class="ef-input" type="number" step="0.05" min="0" value="${emp.monthlySalary ?? ''}">`) : ''}
        ${eField('Ferien (%)',   `<input id="ef-vac"  class="ef-input" type="number" step="0.01" min="0" value="${emp.vacationPercent ?? ''}">`)}
        ${eField('Feiertag (%)', `<input id="ef-hol"  class="ef-input" type="number" step="0.01" min="0" value="${emp.holidayPercent ?? ''}">`)}
        ${eField('13. ML (%)',   `<input id="ef-13th" class="ef-input" type="number" step="0.01" min="0" value="${emp.thirteenthSalaryPercent ?? ''}">`)}
    </div>`;
}

function buildEmpEditAdressen(emp) {
    return `
    <div class="emp-section-title">Hauptadresse</div>
    <div class="emp-field-grid">
        ${eField('Strasse',      `<input id="ef-street"  class="ef-input" value="${esc(emp.street)}">`)}
        ${eField('Hausnummer',   `<input id="ef-houseNr" class="ef-input" value="${esc(emp.houseNumber)}">`)}
        ${eField('PLZ',          `<input id="ef-zip"     class="ef-input" value="${esc(emp.zipCode)}">`)}
        ${eField('Ort',          `<input id="ef-city"    class="ef-input" value="${esc(emp.city)}">`)}
        ${eField('Land',         `<input id="ef-country" class="ef-input" value="${esc(emp.country ?? 'CH')}">`)}
    </div>
    <div class="emp-section-title">Kontakt</div>
    <div class="emp-field-grid">
        ${eField('Telefon', `<input id="ef-phone2" class="ef-input" type="tel" value="${esc(emp.phoneMobile)}" placeholder="+41 79 …">`)}
        ${eField('E-Mail',  `<input id="ef-email2" class="ef-input" type="email" value="${esc(emp.email)}">`)}
    </div>`;
}

// Hilfshelfer: Edit-Feld
function eField(label, inputHtml) {
    return `<div class="emp-field">
        <div class="emp-field-label">${label}</div>
        <div class="emp-field-value">${inputHtml}</div>
    </div>`;
}

function esc(v) { return (v ?? '').toString().replace(/"/g, '&quot;').replace(/</g, '&lt;'); }

function cancelEmpEdit() {
    if (selectedEmployee) renderEmployeeDetail(selectedEmployee);
}

async function saveEmpEdit() {
    if (!selectedEmployeeId || !selectedEmployee) return;

    const emp = selectedEmployee;

    // ── Employee Stammdaten ──────────────────────────────────────────────
    const exitVal = document.getElementById('ef-exit')?.value;
    const empPayload = {
        firstName:    document.getElementById('ef-firstName')?.value    || null,
        lastName:     document.getElementById('ef-lastName')?.value     || null,
        salutation:   document.getElementById('ef-salutation')?.value   || null,
        gender:       document.getElementById('ef-gender')?.value       || null,
        dateOfBirth:  document.getElementById('ef-dob')?.value          || null,
        languageCode: document.getElementById('ef-lang')?.value         || null,
        phoneMobile:  document.getElementById('ef-phone')?.value
                   || document.getElementById('ef-phone2')?.value       || null,
        email:        document.getElementById('ef-email')?.value
                   || document.getElementById('ef-email2')?.value       || null,
        street:       document.getElementById('ef-street')?.value       || null,
        houseNumber:  document.getElementById('ef-houseNr')?.value      || null,
        zipCode:      document.getElementById('ef-zip')?.value          || null,
        city:         document.getElementById('ef-city')?.value         || null,
        country:      document.getElementById('ef-country')?.value      || null,
        permitTypeId: parseInt(document.getElementById('ef-permitType')?.value) || 0,
        permitExpiryDate: document.getElementById('ef-permitExpiry')?.value || null,
        entryDate:    document.getElementById('ef-entry')?.value        || null,
        exitDateSet:  true,
        exitDate:     exitVal || null,
        socialSecurityNumber: document.getElementById('ef-ahvNummer')?.value || null,
        zivilstand:   document.getElementById('ef-zivilstand')?.value   || null,
    };

    // ── Employment Vertragsdaten ──────────────────────────────────────────
    const empContractPayload = {
        jobTitle:              document.getElementById('ef-jobTitle')?.value || null,
        employmentPercentage:  parseFloatOrNull(document.getElementById('ef-pct')?.value),
        weeklyHours:           parseFloatOrNull(document.getElementById('ef-wh')?.value),
        guaranteedHoursPerWeek: parseFloatOrNull(document.getElementById('ef-gh')?.value),
        hourlyRate:            parseFloatOrNull(document.getElementById('ef-hourly')?.value),
        monthlySalary:         parseFloatOrNull(document.getElementById('ef-monthly')?.value),
        vacationPercent:       parseFloatOrNull(document.getElementById('ef-vac')?.value),
        holidayPercent:        parseFloatOrNull(document.getElementById('ef-hol')?.value),
        thirteenthSalaryPercent: parseFloatOrNull(document.getElementById('ef-13th')?.value),
    };

    try {
        // Parallel speichern
        const requests = [
            fetch(`/api/employees/${selectedEmployeeId}`, {
                method: 'PUT',
                headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify(empPayload)
            })
        ];
        if (emp.activeContractId) {
            requests.push(fetch(`/api/employees/${selectedEmployeeId}/employment/${emp.activeContractId}`, {
                method: 'PUT',
                headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify(empContractPayload)
            }));
        }

        const results = await Promise.all(requests);
        if (results.some(r => !r.ok)) {
            alert('Fehler beim Speichern. Bitte erneut versuchen.');
            return;
        }

        // Neu laden und in Anzeigemodus zurück
        const res = await fetch(`/api/employees/${selectedEmployeeId}`, { headers: ah() });
        if (res.ok) {
            selectedEmployee = await res.json();
            // Liste aktualisieren (Name könnte sich geändert haben)
            const idx = allEmployees.findIndex(e => e.id === selectedEmployeeId);
            if (idx >= 0) {
                allEmployees[idx] = { ...allEmployees[idx], ...selectedEmployee };
                allEmployees.sort((a, b) => {
                    const na = ((a.firstName ?? '') + ' ' + (a.lastName ?? '')).trim().toLowerCase();
                    const nb = ((b.firstName ?? '') + ' ' + (b.lastName ?? '')).trim().toLowerCase();
                    return na.localeCompare(nb, 'de');
                });
                const q = document.getElementById('empSearch')?.value ?? '';
                const list = q ? allEmployees.filter(e => {
                    const n = ((e.firstName??'')+(e.lastName??'')).toLowerCase();
                    return n.includes(q.toLowerCase()) || (e.employeeNumber??'').includes(q);
                }) : allEmployees;
                renderEmployeeList(list);
            }
            renderEmployeeDetail(selectedEmployee);
        }
    } catch {
        alert('Verbindungsfehler beim Speichern.');
    }
}

function parseFloatOrNull(val) {
    if (val === '' || val === null || val === undefined) return null;
    const n = parseFloat(val);
    return isNaN(n) ? null : n;
}

// ══════════════════════════════════════════════
// FAMILIE MODAL
// ══════════════════════════════════════════════

let editingFamilyMemberId = null;

function openFamilyModal(member) {
    editingFamilyMemberId = member ? member.id : null;

    document.getElementById('familyModalTitle').textContent =
        member ? 'Familienangehöriger bearbeiten' : 'Familienangehörigen hinzufügen';

    // Felder befüllen oder leeren
    document.getElementById('fmMemberType').value      = member?.memberType         ?? 'Kind';
    document.getElementById('fmGender').value          = member?.gender             ?? '';
    document.getElementById('fmFirstName').value       = member?.firstName          ?? '';
    document.getElementById('fmLastName').value        = member?.lastName           ?? '';
    document.getElementById('fmMaidenName').value      = member?.maidenName         ?? '';
    document.getElementById('fmDateOfBirth').value     = toDateInput(member?.dateOfBirth);
    document.getElementById('fmSocialSecurity').value  = member?.socialSecurityNumber ?? '';
    document.getElementById('fmLivesInSwitzerland').checked = member?.livesInSwitzerland ?? false;
    document.getElementById('fmAllowance1').value      = toMonthInput(member?.allowance1Until);
    document.getElementById('fmAllowance2').value      = toMonthInput(member?.allowance2Until);
    document.getElementById('fmAllowance3').value      = toMonthInput(member?.allowance3Until);
    document.getElementById('fmQstFrom').value         = toDateInput(member?.qstDeductibleFrom);
    document.getElementById('fmQstUntil').value        = toDateInput(member?.qstDeductibleUntil);

    document.getElementById('familyModal').style.display = 'flex';
}

function closeFamilyModal() {
    document.getElementById('familyModal').style.display = 'none';
    editingFamilyMemberId = null;
}

async function saveFamilyMember() {
    if (!selectedEmployeeId) return;

    const payload = {
        memberType:             document.getElementById('fmMemberType').value         || 'Kind',
        gender:                 document.getElementById('fmGender').value             || null,
        firstName:              document.getElementById('fmFirstName').value          || null,
        lastName:               document.getElementById('fmLastName').value           || null,
        maidenName:             document.getElementById('fmMaidenName').value         || null,
        dateOfBirth:            document.getElementById('fmDateOfBirth').value        || null,
        socialSecurityNumber:   document.getElementById('fmSocialSecurity').value     || null,
        livesInSwitzerland:     document.getElementById('fmLivesInSwitzerland').checked,
        allowance1Until:        monthInputToDate(document.getElementById('fmAllowance1').value),
        allowance2Until:        monthInputToDate(document.getElementById('fmAllowance2').value),
        allowance3Until:        monthInputToDate(document.getElementById('fmAllowance3').value),
        qstDeductibleFrom:      document.getElementById('fmQstFrom').value            || null,
        qstDeductibleUntil:     document.getElementById('fmQstUntil').value           || null,
    };

    const isEdit = editingFamilyMemberId !== null;
    const url    = isEdit
        ? `/api/employees/${selectedEmployeeId}/family/${editingFamilyMemberId}`
        : `/api/employees/${selectedEmployeeId}/family`;
    const method = isEdit ? 'PUT' : 'POST';

    try {
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (!res.ok) {
            alert('Fehler beim Speichern.');
            return;
        }
        closeFamilyModal();
        loadFamilieTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

async function deleteFamilyMember(id) {
    if (!confirm('Diesen Eintrag wirklich löschen?')) return;
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/family/${id}`, {
            method: 'DELETE',
            headers: ah()
        });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadFamilieTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// ── Timezone-sichere ISO-Datumsfunktion ─────────
// toISOString() gibt UTC zurück → in UTC+1/+2 einen Tag zu früh!
// localIso() verwendet lokale Datumskomponenten
function localIso(d) {
    return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
}

// ── Hilfsfunktionen für Datumseingaben ─────────
function toDateInput(dateStr) {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    if (isNaN(d)) return '';
    return localIso(d);
}

function toMonthInput(dateStr) {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    if (isNaN(d)) return '';
    return d.toISOString().slice(0, 7); // YYYY-MM
}

function monthInputToDate(val) {
    if (!val) return null;
    return val + '-01'; // ersten Tag des Monats
}

// ══════════════════════════════════════════════
// STEMPELZEITEN TAB
// ══════════════════════════════════════════════

// Zulagen/Abzüge-Periode
let zulagenPeriodYear  = new Date().getFullYear();
let zulagenPeriodMonth = new Date().getMonth() + 1;

// Absenz-Typen Cache (aus DB geladen, für Modal + hoursPerDay)
let _absenzTypenCache = null;
async function getAbsenzTypen() {
    if (_absenzTypenCache) return _absenzTypenCache;
    try {
        const res = await fetch('/api/absenz-typen', { headers: ah() });
        if (res.ok) _absenzTypenCache = await res.json();
    } catch {}
    // Fallback falls DB noch leer
    if (!_absenzTypenCache || !_absenzTypenCache.length) {
        _absenzTypenCache = [
            { code: 'KRANK',      bezeichnung: 'Krankheit',                   zeitgutschrift: true,  gutschriftModus: '1/5' },
            { code: 'UNFALL',     bezeichnung: 'Unfall',                      zeitgutschrift: true,  gutschriftModus: '1/5' },
            { code: 'SCHULUNG',   bezeichnung: 'Schulung / andere Absenz',    zeitgutschrift: true,  gutschriftModus: '1/5' },
            { code: 'FERIEN',     bezeichnung: 'Ferien',                      zeitgutschrift: true,  gutschriftModus: '1/7' },
            { code: 'MILITAER',   bezeichnung: 'Militär / Zivilschutz',       zeitgutschrift: true,  gutschriftModus: '1/5' },
            { code: 'FEIERTAG',   bezeichnung: 'Feiertag (ausbezahlt)',       zeitgutschrift: false, gutschriftModus: null   },
            { code: 'NACHT_KOMP', bezeichnung: 'Nacht-Kompensation (Ruhetag)',zeitgutschrift: true,  gutschriftModus: '1/5' },
        ];
    }
    return _absenzTypenCache;
}

// stempelPeriodMonth = der Monat, in dem die Lohnperiode ENDET
// z.B. März-Periode (21.02–20.03) → stempelPeriodMonth = 3
let stempelPeriodYear   = new Date().getFullYear();
let stempelPeriodMonth  = new Date().getMonth() + 1;
let stempelStartDay     = 1;   // wird aus company profile geladen
let editingTimeEntryId  = null;

// Berechnet Von/Bis einer Lohnperiode
// startDay=21, periodMonth=3, periodYear=2026 → { from: '2026-02-21', to: '2026-03-20' }
function calcPayrollPeriod(startDay, periodMonth, periodYear) {
    if (startDay <= 1) {
        // normaler Kalendermonat
        const lastDay = new Date(periodYear, periodMonth, 0).getDate();
        return {
            from: `${periodYear}-${String(periodMonth).padStart(2,'0')}-01`,
            to:   `${periodYear}-${String(periodMonth).padStart(2,'0')}-${String(lastDay).padStart(2,'0')}`,
        };
    }
    // Periode endet am (startDay-1) des periodMonth
    const toDay   = startDay - 1;
    const toMonth = periodMonth;
    const toYear  = periodYear;

    // Periode beginnt am startDay des Vormonats
    let fromMonth = periodMonth - 1;
    let fromYear  = periodYear;
    if (fromMonth < 1) { fromMonth = 12; fromYear--; }

    return {
        from: `${fromYear}-${String(fromMonth).padStart(2,'0')}-${String(startDay).padStart(2,'0')}`,
        to:   `${toYear}-${String(toMonth).padStart(2,'0')}-${String(toDay).padStart(2,'0')}`,
    };
}

function formatPeriodLabel(startDay, periodMonth, periodYear) {
    const { from, to } = calcPayrollPeriod(startDay, periodMonth, periodYear);
    if (startDay <= 1) {
        const monthNames = ['Januar','Februar','März','April','Mai','Juni',
                            'Juli','August','September','Oktober','November','Dezember'];
        return `${monthNames[periodMonth - 1]} ${periodYear}`;
    }
    const fmt = s => {
        const [y, m, d] = s.split('-');
        return `${d}.${m}.${y}`;
    };
    return `${fmt(from)} – ${fmt(to)}`;
}

// Sollstunden für eine Lohnperiode berechnen
function calcSollstunden(periodMonth, periodYear) {
    const empModel = selectedEmployee?.employmentModel ?? '';

    // UTP: keine Sollstunden
    if (empModel === 'UTP') return null;

    if (empModel === 'MTP') {
        // MTP: fixer monatlicher Grundlohn → garantierte Stunden × 52 / 12
        // Unabhängig von der Anzahl Tage im Monat
        const guarH = Number(selectedEmployee?.guaranteedHoursPerWeek
                          ?? selectedEmployee?.weeklyHours ?? 0);
        if (!guarH) return null;
        return Math.round(guarH * 52 / 12 * 100) / 100;
    }

    // FIX / FIX-M: normalWeeklyHours × Pensum% ÷ 7 × Tage im Monat
    const normalH = Number(selectedCompanyProfile?.normalWeeklyHours ?? 42);
    if (!normalH) return null;
    const days = new Date(periodYear, periodMonth, 0).getDate();
    const pct   = Number(selectedEmployee?.employmentPercentage ?? 100);
    const weeklyH = normalH * pct / 100;
    return Math.round((weeklyH / 7) * days * 100) / 100;
}

async function loadStempelzeitenTab(employeeId) {
    const el = document.getElementById('stempelzeitenContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        // Einmalig payroll_period_start_day laden
        if (stempelStartDay === 1) {
            const cp = await fetch('/api/companyprofiles', { headers: ah() });
            if (cp.ok) {
                const profiles = await cp.json();
                if (profiles.length > 0 && profiles[0].payrollPeriodStartDay > 1) {
                    stempelStartDay = profiles[0].payrollPeriodStartDay;
                }
            }
        }

        const { from, to } = calcPayrollPeriod(stempelStartDay, stempelPeriodMonth, stempelPeriodYear);
        const [res, absRes] = await Promise.all([
            fetch(`/api/employees/${employeeId}/timeentries?dateFrom=${from}&dateTo=${to}`, { headers: ah() }),
            fetch(`/api/absences/employee/${employeeId}`, { headers: ah() })
        ]);
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>'; return; }
        const entries  = await res.json();
        const absences = absRes.ok ? await absRes.json() : [];

        // Absenztage filtern: nur Tage in dieser Periode
        const absenceDayMap = {}; // date string → { absenceType, hoursPerDay }
        const empModel = selectedEmployee?.employmentModel ?? '';
        const normalH  = Number(selectedCompanyProfile?.normalWeeklyHours ?? 42);
        const mtpH     = Number(selectedEmployee?.guaranteedHoursPerWeek ?? selectedEmployee?.weeklyHours ?? 0);
        // FIX: immer volle Betriebsstunden (42h), unabhängig vom Pensum
        // MTP: vertraglich garantierte Wochenstunden
        const weeklyH  = empModel === 'MTP' ? mtpH : normalH;

        // Absenz-Typen für hoursPerDay-Berechnung
        const absTypenCfg = await getAbsenzTypen();
        const absTypMap   = Object.fromEntries(absTypenCfg.map(t => [t.code, t]));

        absences.forEach(a => {
            const cfg = absTypMap[a.absenceType] ?? { gutschriftModus: '1/5' };
            const hoursPerDay = cfg.gutschriftModus === '1/7' ? weeklyH / 7 : weeklyH / 5;
            // worked_days = Tage mit Gutschrift; Set für schnellen Lookup
            const workedDaysSet = new Set(a.workedDays ? JSON.parse(a.workedDays) : []);

            // Badge für ALLE Tage im Absenz-Zeitraum zeigen (innerhalb Lohnperiode)
            let cur = new Date(a.dateFrom + 'T00:00:00');
            const absEnd = new Date(a.dateTo + 'T00:00:00');
            while (cur <= absEnd) {
                const d = localIso(cur);
                if (d >= from && d <= to) {
                    absenceDayMap[d] = {
                        absenceType:  a.absenceType,
                        hoursPerDay,
                        absenceId:    a.id,
                        // UTP: keine automatische Zeitgutschrift (ausser NACHT_KOMP)
                        // FIX/MTP: Gutschrift gemäss absenz_typ.zeitgutschrift Konfiguration
                        hasCredit:    (empModel !== 'UTP' || a.absenceType === 'NACHT_KOMP')
                                      && (cfg.zeitgutschrift !== false || a.absenceType === 'NACHT_KOMP')
                                      && workedDaysSet.has(d),
                    };
                }
                cur.setDate(cur.getDate() + 1);
            }
        });

        renderStempelzeitenTab(el, entries, employeeId, absenceDayMap);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler</span></div>';
    }
}

function renderStempelzeitenTab(el, entries, employeeId, absenceDayMap = {}) {
    const periodLabel = formatPeriodLabel(stempelStartDay, stempelPeriodMonth, stempelPeriodYear);

    // Group by date for daily totals
    const byDate = {};
    entries.forEach(e => {
        const d = e.entryDate?.slice(0, 10) ?? e.entry_date?.slice(0, 10);
        if (!byDate[d]) byDate[d] = [];
        byDate[d].push(e);
    });

    // Gearbeitete Stunden (nur Stempelzeiten)
    const totalWorked = entries.reduce((s, e) => s + (e.totalHours ?? 0), 0);

    // Stunden aus Absenzen (hasCredit-Tage × hoursPerDay)
    const totalAbsence = Object.values(absenceDayMap)
        .filter(a => a.hasCredit)
        .reduce((s, a) => s + a.hoursPerDay, 0);

    const totalMonth = totalWorked + totalAbsence; // für Saldo

    // Sollstunden
    const empModel   = selectedEmployee?.employmentModel ?? '';
    const soll = calcSollstunden(stempelPeriodMonth, stempelPeriodYear);
    const diff = soll != null ? totalMonth - soll : null;

    // Alle Daten (Stempelzeiten + Absenztage) zusammenführen und sortieren
    const allDates = new Set([...Object.keys(byDate), ...Object.keys(absenceDayMap)]);
    const sortedDates = [...allDates].sort();

    let rows = '';
    sortedDates.forEach(date => {
        const dayEntries = byDate[date] ?? [];
        const absDay     = absenceDayMap[date];
        const dayTotal   = dayEntries.reduce((s, e) => s + (e.totalHours ?? 0), 0);
        const d = new Date(date + 'T00:00:00');
        const dayName = d.toLocaleDateString('de-CH', { weekday: 'short' });
        const dateStr = d.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit' });

        // Anzahl Zeilen für rowspan: Stempelzeiten + evtl. 1 Absenzzeile
        const rowCount = (dayEntries.length || 1) + (absDay ? 1 : 0);

        if (dayEntries.length === 0 && absDay) {
            // Nur Absenzeintrag, keine Stempelzeit
            const absMeta = ABSENCE_LABELS?.[absDay.absenceType] ?? { label: absDay.absenceType, color: '' };
            const creditStr = absDay.hasCredit
                ? `<span class="stamp-abs-hours">+${absDay.hoursPerDay.toFixed(2)} h Gutschrift</span>`
                : '';
            rows += `<tr class="stamp-day-first stamp-abs-row">
                <td class="stamp-date-cell">
                    <div class="stamp-weekday">${dayName}</div>
                    <div class="stamp-date">${dateStr}</div>
                </td>
                <td colspan="3" class="stamp-abs-cell">
                    <span class="abs-type-badge ${absMeta.color}">${absMeta.label}</span>
                    ${creditStr}
                </td>
                <td></td><td></td>
            </tr>`;
        } else {
            dayEntries.forEach((e, idx) => {
                const timeIn  = e.timeIn  ? new Date(e.timeIn).toLocaleTimeString('de-CH', {hour:'2-digit', minute:'2-digit', timeZone:'UTC'}) : '–';
                const timeOut = e.timeOut ? new Date(e.timeOut).toLocaleTimeString('de-CH', {hour:'2-digit', minute:'2-digit', timeZone:'UTC'}) : '–';
                const hrs = e.totalHours != null ? e.totalHours.toFixed(2) : '–';
                const nightHrs = e.nightHours > 0 ? `<span class="stamp-night">${e.nightHours?.toFixed(2)} N</span>` : '';
                const comment = e.comment ? `<span class="stamp-comment">${e.comment}</span>` : '';

                // Audit-Info aufbereiten
                const isEdited = !!e.editedBy;
                const editedClass = isEdited ? ' stamp-row-edited' : '';
                let auditInfo = '';
                if (isEdited) {
                    const origIn  = e.originalTimeIn  ? new Date(e.originalTimeIn).toLocaleTimeString('de-CH', {hour:'2-digit', minute:'2-digit', timeZone:'UTC'}) : null;
                    const origOut = e.originalTimeOut ? new Date(e.originalTimeOut).toLocaleTimeString('de-CH', {hour:'2-digit', minute:'2-digit', timeZone:'UTC'}) : null;
                    const editedAtStr = e.editedAt
                        ? new Date(e.editedAt).toLocaleString('de-CH', {day:'2-digit', month:'2-digit', year:'numeric', hour:'2-digit', minute:'2-digit', timeZone:'UTC'})
                        : '';
                    const origStr = (origIn || origOut) ? `Ursprünglich: ${origIn ?? '–'} – ${origOut ?? '–'} · ` : '';
                    auditInfo = `<div class="stamp-audit-info">✎ ${origStr}Geändert durch ${e.editedBy} am ${editedAtStr}</div>`;
                }

                rows += `<tr class="${idx === 0 ? 'stamp-day-first' : 'stamp-day-cont'}${editedClass}">
                    ${idx === 0 ? `<td rowspan="${rowCount}" class="stamp-date-cell">
                        <div class="stamp-weekday">${dayName}</div>
                        <div class="stamp-date">${dateStr}</div>
                        <div class="stamp-day-total">${dayTotal.toFixed(2)} h</div>
                    </td>` : ''}
                    <td class="stamp-time">${timeIn}</td>
                    <td class="stamp-time">${timeOut}</td>
                    <td class="stamp-hrs">${hrs} h ${nightHrs}</td>
                    <td class="stamp-comment-cell">${comment}${auditInfo}</td>
                    <td class="stamp-actions">
                        <button class="btn-stamp-edit" onclick="openTimeEntryModal(${JSON.stringify(e).replace(/"/g,'&quot;')})">✎</button>
                        <button class="btn-stamp-del"  onclick="deleteTimeEntry(${e.id})">✕</button>
                    </td>
                </tr>`;
            });

            // Absenzzeile anhängen — inkl. Gutschrift wenn hasCredit
            if (absDay) {
                const absMeta = ABSENCE_LABELS?.[absDay.absenceType] ?? { label: absDay.absenceType, color: '' };
                const creditStr = absDay.hasCredit
                    ? `<span class="stamp-abs-hours">+${absDay.hoursPerDay.toFixed(2)} h Gutschrift</span>`
                    : '';
                rows += `<tr class="stamp-abs-row stamp-day-cont">
                    <td colspan="3" class="stamp-abs-cell">
                        <span class="abs-type-badge ${absMeta.color}">${absMeta.label}</span>
                        ${creditStr}
                    </td>
                    <td></td><td></td>
                </tr>`;
            }
        }
    });

    const guarH = selectedEmployee?.guaranteedHoursPerWeek;
    const empModelLabel = empModel
        ? `<span class="stamp-model-badge stamp-model-${empModel.toLowerCase()}">${empModel}${empModel === 'MTP' && guarH != null ? ' · ' + guarH + ' Std.' : ''}</span>`
        : '';

    el.innerHTML = `
    <div class="stamp-toolbar">
        <div class="stamp-nav">
            <button class="btn-stamp-nav" onclick="changeStempelMonth(-1)">‹</button>
            <span class="stamp-month-label">${periodLabel}</span>
            ${empModelLabel}
            <button class="btn-stamp-nav" onclick="changeStempelMonth(1)">›</button>
        </div>
        <div class="stamp-hours-summary">
            <div class="stamp-hours-item">
                <span class="stamp-hours-label">Gearbeitet</span>
                <span class="stamp-hours-value">${totalWorked.toFixed(2)} h</span>
            </div>
            ${totalAbsence > 0 ? `
            <div class="stamp-hours-item stamp-hours-sep">
                <span class="stamp-hours-label">Absenzen</span>
                <span class="stamp-hours-value stamp-diff-pos">+${totalAbsence.toFixed(2)} h</span>
            </div>
            <div class="stamp-hours-item">
                <span class="stamp-hours-label">Total</span>
                <span class="stamp-hours-value">${totalMonth.toFixed(2)} h</span>
            </div>` : ''}
            ${soll != null ? `
            <div class="stamp-hours-item stamp-hours-sep">
                <span class="stamp-hours-label">Soll</span>
                <span class="stamp-hours-value">${soll.toFixed(2)} h</span>
            </div>
            <div class="stamp-hours-item">
                ${empModel === 'MTP' ? (() => {
                    if (diff > 0) {
                        return `<span class="stamp-hours-label">Zusätzlich auszahlen</span>
                                <span class="stamp-hours-value stamp-diff-pos">+${diff.toFixed(2)} h</span>`;
                    } else if (diff < 0) {
                        return `<span class="stamp-hours-label">Fehlstunden</span>
                                <span class="stamp-hours-value stamp-diff-neg">${diff.toFixed(2)} h</span>`;
                    } else {
                        return `<span class="stamp-hours-label">Ausgeglichen</span>
                                <span class="stamp-hours-value">± 0.00 h</span>`;
                    }
                })() : `
                <span class="stamp-hours-label">Saldo</span>
                <span class="stamp-hours-value ${diff >= 0 ? 'stamp-diff-pos' : 'stamp-diff-neg'}">
                    ${diff >= 0 ? '+' : ''}${diff.toFixed(2)} h
                </span>`}
            </div>` : ''}
        </div>
        <button class="btn-emp-add" onclick="openTimeEntryModal(null)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Hinzufügen
        </button>
    </div>
    ${entries.length === 0 ? `
    <div class="emp-placeholder">
        <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
        <span>Keine Stempelzeiten für ${periodLabel}</span>
    </div>` : `
    <table class="stamp-table">
        <thead>
            <tr>
                <th>Datum</th>
                <th>Ein</th>
                <th>Aus</th>
                <th>Stunden</th>
                <th>Kommentar</th>
                <th></th>
            </tr>
        </thead>
        <tbody>${rows}</tbody>
    </table>`}`;

    // Toolbar-Höhe messen und als CSS-Variable setzen, damit <thead> genau darunter klebt
    requestAnimationFrame(() => {
        const toolbar = el.querySelector('.stamp-toolbar');
        if (toolbar) {
            el.style.setProperty('--stamp-thead-top', toolbar.offsetHeight + 'px');
        }
    });
}

function changeStempelMonth(delta) {
    stempelPeriodMonth += delta;
    if (stempelPeriodMonth > 12) { stempelPeriodMonth = 1;  stempelPeriodYear++; }
    if (stempelPeriodMonth < 1)  { stempelPeriodMonth = 12; stempelPeriodYear--; }
    if (selectedEmployeeId) loadStempelzeitenTab(selectedEmployeeId);
}

// ── Nachtstunden-Berechnung ─────────────────────
// nightStart / nightEnd im Format "HH:MM", Übermitternacht wird unterstützt
function calcAutoNightHours(timeInStr, timeOutStr, nightStartStr, nightEndStr) {
    if (!timeInStr || !timeOutStr || !nightStartStr || !nightEndStr) return 0;

    const toMin = t => { const [h, m] = t.split(':').map(Number); return h * 60 + m; };
    const inMin  = toMin(timeInStr);
    const outMin = toMin(timeOutStr) + (toMin(timeOutStr) <= toMin(timeInStr) ? 1440 : 0); // Übermitternacht
    const ns = toMin(nightStartStr);
    const ne = toMin(nightEndStr) + (toMin(nightEndStr) <= ns ? 1440 : 0); // Nachtende nächster Tag

    // Überlappung [inMin, outMin) ∩ [ns, ne)
    const start = Math.max(inMin, ns);
    const end   = Math.min(outMin, ne);
    const nightMin = Math.max(0, end - start);
    return Math.round((nightMin / 60) * 100) / 100;
}

function updateAutoNightHours() {
    const timeIn  = document.getElementById('teTimeIn').value;
    const timeOut = document.getElementById('teTimeOut').value;
    if (!timeIn || !timeOut) return;
    const ns = selectedCompanyProfile?.nightStartTime ?? '00:00';
    const ne = selectedCompanyProfile?.nightEndTime   ?? '07:00';
    const night = calcAutoNightHours(timeIn, timeOut, ns, ne);
    document.getElementById('teNight').value = night;
}

// ── Modal: Eintrag hinzufügen / bearbeiten ─────

function openTimeEntryModal(entry) {
    editingTimeEntryId = entry ? entry.id : null;
    document.getElementById('timeEntryModalTitle').textContent =
        entry ? 'Stempelzeit bearbeiten' : 'Stempelzeit hinzufügen';

    const today = localIso(new Date());
    document.getElementById('teDate').value     = entry ? toDateInput(entry.entryDate ?? entry.entry_date) : today;
    document.getElementById('teTimeIn').value   = entry ? toTimeInput(entry.timeIn  ?? entry.time_in)  : '';
    document.getElementById('teTimeOut').value  = entry ? toTimeInput(entry.timeOut ?? entry.time_out) : '';
    document.getElementById('teNight').value    = entry?.nightHours ?? 0;
    document.getElementById('teComment').value  = entry?.comment ?? '';

    // Nachtstunden neu berechnen, wenn Zeiten vorhanden
    if (document.getElementById('teTimeIn').value && document.getElementById('teTimeOut').value) {
        updateAutoNightHours();
    }

    document.getElementById('timeEntryModal').style.display = 'flex';
}

function closeTimeEntryModal() {
    document.getElementById('timeEntryModal').style.display = 'none';
    editingTimeEntryId = null;
}

async function saveTimeEntry() {
    if (!selectedEmployeeId) return;

    const date    = document.getElementById('teDate').value;
    const timeIn  = document.getElementById('teTimeIn').value;
    const timeOut = document.getElementById('teTimeOut').value;

    if (!date || !timeIn) { alert('Datum und Einzeit sind pflichtfelder.'); return; }

    const payload = {
        entryDate:    date,
        timeIn:       `${date}T${timeIn}:00Z`,
        timeOut:      timeOut ? `${date}T${timeOut}:00Z` : null,
        comment:      document.getElementById('teComment').value || null,
        nightHours:   parseFloat(document.getElementById('teNight').value) || 0,
    };

    const isEdit = editingTimeEntryId !== null;
    const url    = isEdit
        ? `/api/employees/${selectedEmployeeId}/timeentries/${editingTimeEntryId}`
        : `/api/employees/${selectedEmployeeId}/timeentries`;
    const method = isEdit ? 'PUT' : 'POST';

    try {
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (!res.ok) {
            let msg = `Fehler beim Speichern (HTTP ${res.status})`;
            try { const e = await res.json(); msg += '\n' + (e.error ?? '') + (e.inner ? '\n' + e.inner : ''); } catch {}
            alert(msg); return;
        }
        closeTimeEntryModal();
        loadStempelzeitenTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

async function deleteTimeEntry(id) {
    if (!confirm('Diesen Eintrag wirklich löschen?')) return;
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/timeentries/${id}`, {
            method: 'DELETE',
            headers: ah()
        });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadStempelzeitenTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

function toTimeInput(dateTimeStr) {
    if (!dateTimeStr) return '';
    const d = new Date(dateTimeStr);
    if (isNaN(d)) return '';
    return d.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: 'UTC' });
}

// ════════════════════════════════════════════════════════════════
//  ABSENZEN
// ════════════════════════════════════════════════════════════════

const ABSENCE_LABELS = {
    KRANK:      { label: 'Krankheit',         color: 'abs-type-krank'    },
    UNFALL:     { label: 'Unfall',            color: 'abs-type-unfall'   },
    SCHULUNG:   { label: 'Schulung',          color: 'abs-type-schulung' },
    FERIEN:     { label: 'Ferien',            color: 'abs-type-ferien'   },
    NACHT_KOMP: { label: 'Nacht-Kompensation', color: 'abs-type-nacht'  },
    MILITAER:   { label: 'Militär',           color: 'abs-type-schulung' },
    FEIERTAG:   { label: 'Feiertag',          color: 'abs-type-ferien'   },
};

async function loadAbsenzenTab(employeeId) {
    const el = document.getElementById('absenzenContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const res = await fetch(`/api/absences/employee/${employeeId}`, { headers: ah() });
        if (!res.ok) throw new Error();
        const absences = await res.json();
        renderAbsenzenList(el, absences, employeeId);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
    }
}

function renderAbsenzenList(el, absences, employeeId) {
    const empModel = selectedEmployee?.employmentModel ?? '';
    const noHours  = empModel === 'UTP';

    let rows = '';
    if (absences.length === 0) {
        rows = `<tr><td colspan="6" style="text-align:center;color:#94a3b8;padding:24px">Keine Absenzen erfasst</td></tr>`;
    } else {
        absences.forEach(a => {
            const meta   = ABSENCE_LABELS[a.absenceType] ?? { label: a.absenceType, color: '' };
            const days   = a.workedDays ? JSON.parse(a.workedDays) : [];
            const dayStr = days.length
                ? days.map(d => {
                    const dt = new Date(d + 'T00:00:00');
                    return dt.toLocaleDateString('de-CH', { weekday: 'short', day: '2-digit', month: '2-digit' });
                  }).join(', ')
                : '–';

            // Für FERIEN MTP = Abzug, sonst Gutschrift
            let hoursCell = '–';
            if (!noHours && a.hoursCredited != null) {
                const isMtpFerien = (empModel === 'MTP' && a.absenceType === 'FERIEN');
                const sign = isMtpFerien ? '−' : '+';
                const cls  = isMtpFerien ? 'abs-hours-neg' : 'abs-hours-pos';
                const label = isMtpFerien ? 'Abzug' : 'Gutschrift';
                hoursCell = `<span class="${cls}">${sign}${Number(a.hoursCredited).toFixed(2)} h</span>
                             <span class="abs-hours-label">${label}</span>`;
            }

            rows += `<tr>
                <td><span class="abs-type-badge ${meta.color}">${meta.label}</span></td>
                <td>${fmtDate(a.dateFrom)} – ${fmtDate(a.dateTo)}</td>
                <td class="abs-days-cell">${dayStr}</td>
                <td>${hoursCell}</td>
                <td class="abs-notes">${a.notes ?? ''}</td>
                <td class="abs-actions">
                    <button class="btn-stamp-edit" onclick='openAbsenceModal(${JSON.stringify(a).replace(/'/g,"&#39;")})'>✎</button>
                    <button class="btn-stamp-del"  onclick="deleteAbsence(${a.id})">✕</button>
                </td>
            </tr>`;
        });
    }

    el.innerHTML = `
    <div class="abs-toolbar">
        <button class="btn-emp-add" onclick="openAbsenceModal(null)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Absenz erfassen
        </button>
    </div>
    <table class="abs-table">
        <thead><tr>
            <th>Typ</th><th>Zeitraum</th><th>Tage</th><th>Stunden</th><th>Bemerkung</th><th></th>
        </tr></thead>
        <tbody>${rows}</tbody>
    </table>`;
}

function fmtDate(iso) {
    if (!iso) return '–';
    const d = new Date(iso + 'T00:00:00');
    return d.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

// ── Absenz-Modal ───────────────────────────────────────────────
async function openAbsenceModal(existing) {
    const modal = document.getElementById('absenceModal');
    if (!modal) return;
    modal.style.display = 'flex';

    // Typen aus DB laden und Dropdown befüllen
    const typen = await getAbsenzTypen();
    const sel = document.getElementById('absTypeSelect');
    const currentVal = existing?.absenceType ?? 'KRANK';
    sel.innerHTML = typen.map(t =>
        `<option value="${t.code}" ${t.code === currentVal ? 'selected' : ''}>${t.bezeichnung}</option>`
    ).join('');

    // Reset form
    document.getElementById('absTypeSelect').value   = currentVal;
    document.getElementById('absDateFrom').value      = existing?.dateFrom ?? '';
    document.getElementById('absDateTo').value        = existing?.dateTo ?? '';
    document.getElementById('absNotes').value         = existing?.notes ?? '';
    document.getElementById('absModalTitle').textContent = existing ? 'Absenz bearbeiten' : 'Absenz erfassen';
    document.getElementById('absenceModal').dataset.editId = existing?.id ?? '';

    // Pre-select worked days if editing
    window._absEditWorkedDays = existing?.workedDays ? JSON.parse(existing.workedDays) : [];

    renderAbsDayCheckboxes();
    calcAbsHoursPreview();
}

function closeAbsenceModal() {
    const modal = document.getElementById('absenceModal');
    if (modal) { modal.style.display = 'none'; modal.dataset.editId = ''; }
    window._absEditWorkedDays = [];
}

async function renderAbsDayCheckboxes() {
    const from  = document.getElementById('absDateFrom').value;
    const to    = document.getElementById('absDateTo').value;
    const type  = document.getElementById('absTypeSelect').value;
    const box   = document.getElementById('absDayCheckboxes');
    if (!box) return;

    if (!from || !to || from > to) { box.innerHTML = ''; return; }

    const dayNames = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];
    const preselect = window._absEditWorkedDays ?? [];

    // Alle Tage im Zeitraum aufzählen
    const days = [];
    let cur = new Date(from + 'T00:00:00');
    const end = new Date(to + 'T00:00:00');
    while (cur <= end) {
        days.push(new Date(cur));
        cur.setDate(cur.getDate() + 1);
    }

    if (type === 'FERIEN') {
        // Ferien: alle Tage zählen, kein Checkbox-Selection nötig
        box.innerHTML = `<div class="abs-day-info">Alle ${days.length} Tag(e) werden angerechnet.</div>`;
        return;
    }

    // KRANK / UNFALL / SCHULUNG: Tage auswählen
    let html = '<div class="abs-day-label">Welche Tage hätte der/die Mitarbeitende gearbeitet?</div><div class="abs-day-grid">';
    days.forEach(d => {
        const iso     = localIso(d);   // timezone-sicher!
        const weekday = dayNames[d.getDay()];
        const dateStr = d.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit' });
        const checked = preselect.includes(iso) ? 'checked' : '';
        // Alle Tage vorausgewählt (Sa/So sind normale Arbeitstage bei McDonald's)
        const chk = (preselect.length > 0 ? checked : 'checked');
        html += `<label class="abs-day-item">
            <input type="checkbox" value="${iso}" ${chk} onchange="calcAbsHoursPreview()">
            <span class="abs-day-name">${weekday}</span>
            <span class="abs-day-date">${dateStr}</span>
        </label>`;
    });
    html += '</div>';
    box.innerHTML = html;
    calcAbsHoursPreview();
}

function getAbsWorkedDays() {
    const type = document.getElementById('absTypeSelect').value;
    const from = document.getElementById('absDateFrom').value;
    const to   = document.getElementById('absDateTo').value;

    if (type === 'FERIEN') {
        // Alle Tage im Bereich
        const days = [];
        if (from && to && from <= to) {
            let cur = new Date(from + 'T00:00:00');
            const end = new Date(to + 'T00:00:00');
            while (cur <= end) {
                days.push(localIso(cur));
                cur.setDate(cur.getDate() + 1);
            }
        }
        return days;
    }

    // KRANK / UNFALL / SCHULUNG: angekreuzte Tage
    return [...document.querySelectorAll('#absDayCheckboxes input[type=checkbox]:checked')]
        .map(cb => cb.value);
}

async function calcAbsHoursPreview() {
    const type     = document.getElementById('absTypeSelect').value;
    const empModel = selectedEmployee?.employmentModel ?? '';
    const previewEl = document.getElementById('absHoursPreview');
    if (!previewEl) return;

    const workedDays = getAbsWorkedDays();
    const count      = workedDays.length;

    // Wochenstunden für Absenzberechnung:
    // FIX/FIX-M: immer volle Betriebsstunden (42h), unabhängig vom Pensum
    // MTP: vertraglich garantierte Wochenstunden
    let weeklyH = 0;
    if (empModel === 'MTP') {
        weeklyH = Number(selectedEmployee?.guaranteedHoursPerWeek ?? selectedEmployee?.weeklyHours ?? 0);
    } else {
        weeklyH = Number(selectedCompanyProfile?.normalWeeklyHours ?? 42);
    }

    let hours = 0;
    let hint  = '';

    if (empModel === 'UTP' && type !== 'NACHT_KOMP') {
        previewEl.innerHTML = '<span class="abs-hours-label">UTP: keine automatische Stundengutschrift</span>';
        previewEl.dataset.hours = '0';
        return;
    }

    // Konfiguration für diesen Absenz-Typ aus Cache laden
    const typen = await getAbsenzTypen();
    const typCfg = typen.find(t => t.code === type);
    const modus = typCfg?.gutschriftModus ?? '1/5';
    const hatGutschrift = typCfg?.zeitgutschrift ?? true;

    if (type === 'NACHT_KOMP') {
        hours = count * (weeklyH / 5);
        hint  = `<span class="abs-hours-pos">+${hours.toFixed(2)} h</span> <span class="abs-hours-label">Nacht-Ruhetag: ${count} Tag${count>1?'e':''} × ${weeklyH.toFixed(2)} h ÷ 5 → wird zu Ist-Stunden addiert, NachtSaldo sinkt entsprechend</span>`;
    } else if (!hatGutschrift) {
        // Kein Zeitgutschrift → Ausbezahlung
        hours = count * (weeklyH / 5);
        hint  = `<span class="abs-hours-label">${typCfg?.bezeichnung ?? type}: keine Zeitgutschrift, wird ausbezahlt (${count} Tag${count>1?'e':''} × ${weeklyH.toFixed(2)} h ÷ 5)</span>`;
    } else if (modus === '1/7') {
        hours = count * (weeklyH / 7);
        if (empModel === 'MTP') {
            hint = `<span class="abs-hours-neg">−${hours.toFixed(2)} h</span> <span class="abs-hours-label">Abzug vom Monatssoll (${count} Tage × ${weeklyH.toFixed(2)} h ÷ 7)</span>`;
        } else {
            hint = `<span class="abs-hours-pos">+${hours.toFixed(2)} h</span> <span class="abs-hours-label">Gutschrift (${count} Tage × ${weeklyH.toFixed(2)} h ÷ 7)</span>`;
        }
    } else {
        // 1/5 (Standard)
        hours = count * (weeklyH / 5);
        hint  = `<span class="abs-hours-pos">+${hours.toFixed(2)} h</span> <span class="abs-hours-label">Gutschrift (${count} Tag${count>1?'e':''} × ${weeklyH.toFixed(2)} h ÷ 5)</span>`;
    }

    previewEl.innerHTML = hint;
    previewEl.dataset.hours = hours.toFixed(2);
}

async function saveAbsence() {
    const editId   = document.getElementById('absenceModal').dataset.editId;
    const type     = document.getElementById('absTypeSelect').value;
    const dateFrom = document.getElementById('absDateFrom').value;
    const dateTo   = document.getElementById('absDateTo').value;
    const notes    = document.getElementById('absNotes').value.trim();

    if (!dateFrom || !dateTo || dateFrom > dateTo) {
        alert('Bitte gültigen Zeitraum eingeben.');
        return;
    }

    const workedDays   = getAbsWorkedDays();
    const hoursPreview = document.getElementById('absHoursPreview');
    const hours        = parseFloat(hoursPreview?.dataset.hours ?? '0');

    const payload = {
        employeeId:    selectedEmployeeId,
        absenceType:   type,
        dateFrom,
        dateTo,
        workedDays:    JSON.stringify(workedDays),
        hoursCredited: hours,
        notes,
    };

    try {
        const url    = editId ? `/api/absences/${editId}` : '/api/absences';
        const method = editId ? 'PUT' : 'POST';
        const res    = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
        });
        if (!res.ok) { alert('Fehler beim Speichern.'); return; }
        closeAbsenceModal();
        loadAbsenzenTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

async function deleteAbsence(id) {
    if (!confirm('Absenz wirklich löschen?')) return;
    try {
        const res = await fetch(`/api/absences/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadAbsenzenTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// ══════════════════════════════════════════════════════════════════
// ZULAGEN / ABZÜGE TAB
// ══════════════════════════════════════════════════════════════════

const monthNames = ['Januar','Februar','März','April','Mai','Juni',
                    'Juli','August','September','Oktober','November','Dezember'];

function zulagenPeriodeStr() {
    return `${zulagenPeriodYear}-${String(zulagenPeriodMonth).padStart(2,'0')}`;
}

async function loadZulagenTab(employeeId) {
    const el = document.getElementById('zulagenContent');
    if (!el) return;

    const periode = zulagenPeriodeStr();
    const periodeLabel = `${monthNames[zulagenPeriodMonth - 1]} ${zulagenPeriodYear}`;

    try {
        const [listRes, typenRes] = await Promise.all([
            fetch(`/api/lohn-zulagen/${employeeId}/${periode}`, { headers: ah() }),
            fetch('/api/lohn-zulag-typen', { headers: ah() })
        ]);
        const eintraege = listRes.ok ? await listRes.json() : [];
        const typen     = typenRes.ok ? await typenRes.json() : [];

        // Getrennt: Zulagen und Abzüge
        const zulagen = eintraege.filter(e => e.typTyp === 'ZULAGE');
        const abzuege = eintraege.filter(e => e.typTyp === 'ABZUG');
        const totalZ  = zulagen.reduce((s, e) => s + e.betrag, 0);
        const totalA  = abzuege.reduce((s, e) => s + e.betrag, 0);
        const fmtCHF  = v => 'CHF ' + v.toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, "'");

        const buildRows = (list, typ) => list.length === 0
            ? `<tr><td colspan="5" style="color:#94a3b8;font-style:italic;padding:10px 8px">Keine ${typ === 'ZULAGE' ? 'Zulagen' : 'Abzüge'} erfasst</td></tr>`
            : list.map(e => `
                <tr>
                    <td>${e.typBezeichnung}</td>
                    <td>${e.bemerkung ?? '—'}</td>
                    <td style="text-align:center">
                        ${e.svPflichtig ? '<span style="color:#16a34a;font-size:11px">SV</span>' : ''}
                        ${e.qstPflichtig ? '<span style="color:#7c3aed;font-size:11px;margin-left:4px">QST</span>' : ''}
                        ${!e.svPflichtig && !e.qstPflichtig ? '<span style="color:#94a3b8;font-size:11px">—</span>' : ''}
                    </td>
                    <td style="text-align:right;font-variant-numeric:tabular-nums">
                        ${typ === 'ABZUG' ? '<span style="color:#dc2626">−</span>' : ''}${fmtCHF(e.betrag)}
                    </td>
                    <td style="text-align:right">
                        <button class="btn btn-sm btn-secondary" onclick="deleteZulage(${e.id})">Löschen</button>
                    </td>
                </tr>`).join('');

        el.innerHTML = `
        <div class="stamp-toolbar" style="position:sticky;top:0;z-index:20;background:white;margin:0 -24px;padding:14px 24px 12px;border-bottom:1px solid #e2e8f0;display:flex;align-items:center;gap:12px;flex-wrap:wrap">
            <button class="btn btn-secondary btn-sm" onclick="zulagenPrevMonth(${employeeId})">‹</button>
            <span style="font-weight:600;min-width:130px;text-align:center">${periodeLabel}</span>
            <button class="btn btn-secondary btn-sm" onclick="zulagenNextMonth(${employeeId})">›</button>
            <div style="flex:1"></div>
            <button class="btn btn-primary btn-sm" onclick="openZulageForm(${employeeId}, '${periode}')">+ Hinzufügen</button>
        </div>

        <div id="zulagenFormWrap" style="display:none;background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;padding:18px;margin:16px 0">
            <div style="font-weight:600;margin-bottom:12px">Neue Zulage / Abzug erfassen</div>
            <div style="display:grid;grid-template-columns:1fr 160px;gap:12px;margin-bottom:10px">
                <div>
                    <label class="f-label">Typ *</label>
                    <select id="zulagenTypId" class="f-input" onchange="onZulagenTypChange()">
                        <option value="">— Bitte wählen —</option>
                        ${typen.map(t => `<option value="${t.id}" data-typ="${t.typ}" data-sv="${t.svPflichtig}" data-qst="${t.qstPflichtig}">${t.typ === 'ABZUG' ? '− ' : '+ '}${t.bezeichnung}</option>`).join('')}
                    </select>
                </div>
                <div>
                    <label class="f-label">Betrag (CHF) *</label>
                    <input type="number" id="zulagenBetrag" class="f-input" min="0.01" step="0.01" placeholder="0.00">
                </div>
            </div>
            <div style="margin-bottom:10px">
                <label class="f-label">Bemerkung (optional)</label>
                <input type="text" id="zulagenBemerkung" class="f-input" placeholder="z.B. 312 km × CHF 0.70">
            </div>
            <div id="zulagenFlags" style="display:none;background:#eff6ff;border:1px solid #bfdbfe;border-radius:6px;padding:8px 12px;margin-bottom:10px;font-size:12.5px;color:#1e40af"></div>
            <div style="display:flex;gap:8px">
                <button class="btn btn-primary" onclick="saveZulage(${employeeId}, '${periode}')">Speichern</button>
                <button class="btn btn-secondary" onclick="closeZulageForm()">Abbrechen</button>
            </div>
        </div>

        <div style="margin-top:16px">
            <!-- Zulagen -->
            <div style="font-weight:600;color:#166534;margin-bottom:8px;display:flex;align-items:center;gap:8px">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                Zulagen
                ${zulagen.length > 0 ? `<span style="margin-left:auto;font-weight:500;color:#15803d">${fmtCHF(totalZ)}</span>` : ''}
            </div>
            <table class="data-table" style="margin-bottom:20px">
                <thead><tr><th>Bezeichnung</th><th>Bemerkung</th><th style="text-align:center">Basis</th><th style="text-align:right">Betrag</th><th></th></tr></thead>
                <tbody>${buildRows(zulagen, 'ZULAGE')}</tbody>
            </table>

            <!-- Abzüge -->
            <div style="font-weight:600;color:#991b1b;margin-bottom:8px;display:flex;align-items:center;gap:8px">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="5" y1="12" x2="19" y2="12"/></svg>
                Abzüge
                ${abzuege.length > 0 ? `<span style="margin-left:auto;font-weight:500;color:#dc2626">−${fmtCHF(totalA)}</span>` : ''}
            </div>
            <table class="data-table">
                <thead><tr><th>Bezeichnung</th><th>Bemerkung</th><th style="text-align:center">Basis</th><th style="text-align:right">Betrag</th><th></th></tr></thead>
                <tbody>${buildRows(abzuege, 'ABZUG')}</tbody>
            </table>
        </div>`;

    } catch(e) {
        el.innerHTML = `<div class="emp-placeholder"><span>Fehler beim Laden: ${e.message}</span></div>`;
    }
}

function zulagenPrevMonth(employeeId) {
    zulagenPeriodMonth--;
    if (zulagenPeriodMonth < 1) { zulagenPeriodMonth = 12; zulagenPeriodYear--; }
    loadZulagenTab(employeeId);
}

function zulagenNextMonth(employeeId) {
    zulagenPeriodMonth++;
    if (zulagenPeriodMonth > 12) { zulagenPeriodMonth = 1; zulagenPeriodYear++; }
    loadZulagenTab(employeeId);
}

function openZulageForm(employeeId, periode) {
    document.getElementById('zulagenFormWrap').style.display = 'block';
    document.getElementById('zulagenTypId').value    = '';
    document.getElementById('zulagenBetrag').value   = '';
    document.getElementById('zulagenBemerkung').value = '';
    document.getElementById('zulagenFlags').style.display = 'none';
}

function closeZulageForm() {
    const w = document.getElementById('zulagenFormWrap');
    if (w) w.style.display = 'none';
}

function onZulagenTypChange() {
    const sel = document.getElementById('zulagenTypId');
    const opt = sel.selectedOptions[0];
    const flags = document.getElementById('zulagenFlags');
    if (!opt || !opt.value) { flags.style.display = 'none'; return; }
    const sv  = opt.dataset.sv === 'true';
    const qst = opt.dataset.qst === 'true';
    const parts = [];
    if (sv)  parts.push('SV-pflichtig (fliesst in AHV/IV/EO/ALV-Basis ein)');
    if (qst) parts.push('QST-pflichtig (fliesst in Quellensteuer-Basis ein)');
    if (!sv && !qst) parts.push('Nicht SV- und nicht QST-pflichtig (wird nach Nettolohn verrechnet)');
    flags.textContent = '⚡ ' + parts.join(' · ');
    flags.style.display = 'block';
}

async function saveZulage(employeeId, periode) {
    const typId   = parseInt(document.getElementById('zulagenTypId').value);
    const betrag  = parseFloat(document.getElementById('zulagenBetrag').value);
    const bemerkg = document.getElementById('zulagenBemerkung').value.trim() || null;

    if (!typId)         { alert('Bitte einen Typ wählen.'); return; }
    if (!betrag || betrag <= 0) { alert('Bitte einen gültigen Betrag eingeben.'); return; }

    try {
        const res = await fetch('/api/lohn-zulagen', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ employeeId, periode, typId, betrag, bemerkung: bemerkg })
        });
        if (!res.ok) { const e = await res.text(); alert('Fehler: ' + e); return; }
        closeZulageForm();
        loadZulagenTab(employeeId);
    } catch { alert('Verbindungsfehler.'); }
}

async function deleteZulage(id) {
    if (!confirm('Eintrag wirklich löschen?')) return;
    try {
        const res = await fetch(`/api/lohn-zulagen/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadZulagenTab(selectedEmployeeId);
    } catch { alert('Verbindungsfehler.'); }
}

// ══════════════════════════════════════════════
// TAB: Formulare / Arbeitslosigkeit / Zwischenverdienst
// ══════════════════════════════════════════════

async function loadFormulareTab(employeeId) {
    // Monat vorbelegen (aktueller Monat)
    const now = new Date();
    const selMonat = document.getElementById('zvMonat');
    const selJahr  = document.getElementById('zvJahr');
    if (selMonat && !selMonat._initialized) {
        selMonat.value = now.getMonth() + 1;
        selMonat._initialized = true;
    }

    // Arbeitslosigkeits-Einträge laden
    try {
        const res = await fetch(`/api/zwischenverdienist/arbeitslosigkeit/${employeeId}`, { headers: ah() });
        if (!res.ok) throw new Error();
        const list = await res.json();
        renderArbeitslosigkeitList(list, employeeId);
    } catch {
        document.getElementById('arbeitslosigkeitList').innerHTML =
            '<p style="color:#ef4444;font-size:13px">Fehler beim Laden.</p>';
    }
}

function renderArbeitslosigkeitList(list, employeeId) {
    const el = document.getElementById('arbeitslosigkeitList');
    if (!list.length) {
        el.innerHTML = '<p style="color:#94a3b8;font-size:13px">Noch keine Arbeitslosigkeits-Perioden erfasst.</p>';
        return;
    }
    const rows = list.map(a => `
      <tr>
        <td>${fmtDate(a.angemeldetSeit)}</td>
        <td>${a.abgemeldetAm ? fmtDate(a.abgemeldetAm) : '<span style="color:#22c55e;font-weight:600">aktiv</span>'}</td>
        <td>${a.ravStelle || '–'}</td>
        <td>${a.ravKundennummer || '–'}</td>
        <td>${a.arbeitslosenkasse || '–'}</td>
        <td style="white-space:nowrap">
          <button class="btn-icon" onclick="editArbeitslosigkeit(${JSON.stringify(a).replace(/"/g,'&quot;')})">✏️</button>
          <button class="btn-icon" onclick="deleteArbeitslosigkeit(${a.id},${employeeId})">🗑️</button>
        </td>
      </tr>`).join('');
    el.innerHTML = `
      <table class="data-table" style="font-size:12px">
        <thead><tr>
          <th>Angemeldet seit</th><th>Abgemeldet am</th>
          <th>RAV-Stelle</th><th>Kundennr.</th><th>ALK</th><th></th>
        </tr></thead>
        <tbody>${rows}</tbody>
      </table>`;
}

function fmtDate(ds) {
    if (!ds) return '';
    const d = new Date(ds);
    return `${String(d.getUTCDate()).padStart(2,'0')}.${String(d.getUTCMonth()+1).padStart(2,'0')}.${d.getUTCFullYear()}`;
}

function openArbeitslosigkeitForm(entry) {
    document.getElementById('alId').value               = entry?.id || '';
    document.getElementById('alAngemeldetSeit').value   = entry?.angemeldetSeit?.slice(0,10) || '';
    document.getElementById('alAbgemeldetAm').value     = entry?.abgemeldetAm?.slice(0,10)   || '';
    document.getElementById('alRavStelle').value        = entry?.ravStelle        || '';
    document.getElementById('alRavKundennummer').value  = entry?.ravKundennummer  || '';
    document.getElementById('alArbeitslosenkasse').value= entry?.arbeitslosenkasse|| '';
    document.getElementById('alBemerkung').value        = entry?.bemerkung        || '';
    document.getElementById('arbeitslosigkeitInlineForm').style.display = 'block';
}

function editArbeitslosigkeit(entry) { openArbeitslosigkeitForm(entry); }

function closeArbeitslosigkeitForm() {
    document.getElementById('arbeitslosigkeitInlineForm').style.display = 'none';
}

async function saveArbeitslosigkeit() {
    const id    = document.getElementById('alId').value;
    const since = document.getElementById('alAngemeldetSeit').value;
    if (!since) { alert('Anmeldedatum ist erforderlich.'); return; }

    const body = {
        employeeId:       selectedEmployeeId,
        angemeldetSeit:   since,
        abgemeldetAm:     document.getElementById('alAbgemeldetAm').value || null,
        ravStelle:        document.getElementById('alRavStelle').value.trim()         || null,
        ravKundennummer:  document.getElementById('alRavKundennummer').value.trim()   || null,
        arbeitslosenkasse:document.getElementById('alArbeitslosenkasse').value.trim() || null,
        bemerkung:        document.getElementById('alBemerkung').value.trim()         || null,
    };

    const url    = id ? `/api/zwischenverdienist/arbeitslosigkeit/${id}` : '/api/zwischenverdienist/arbeitslosigkeit';
    const method = id ? 'PUT' : 'POST';

    try {
        const res = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!res.ok) { alert('Fehler beim Speichern.'); return; }
        closeArbeitslosigkeitForm();
        loadFormulareTab(selectedEmployeeId);
    } catch { alert('Verbindungsfehler.'); }
}

async function deleteArbeitslosigkeit(id, employeeId) {
    if (!confirm('Eintrag löschen?')) return;
    try {
        const res = await fetch(`/api/zwischenverdienist/arbeitslosigkeit/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadFormulareTab(employeeId);
    } catch { alert('Verbindungsfehler.'); }
}

async function generateZwischenverdienst() {
    const monat = document.getElementById('zvMonat').value;
    const jahr  = document.getElementById('zvJahr').value;

    // companyProfileId aus dem aktuellen Mitarbeiter ermitteln
    const cpId = selectedEmployee?.employments?.[0]?.companyProfileId
              || selectedEmployee?.companyProfileId
              || 1;

    const url = `/api/zwischenverdienist/pdf?employeeId=${selectedEmployeeId}&year=${jahr}&month=${monat}&companyProfileId=${cpId}`;

    // PDF in neuem Tab öffnen (Browser zeigt Download-Dialog)
    window.open(url, '_blank');
}
