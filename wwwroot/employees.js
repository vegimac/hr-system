// ══════════════════════════════════════════════
// MITARBEITER VERWALTUNG
// ══════════════════════════════════════════════

let allEmployees = [];
let selectedEmployeeId = null;
let selectedEmployee   = null;   // Ganzes Mitarbeiter-Objekt (für Sollstunden etc.)
let activeEmpTab = 'personal';

// Aktiv-Filter & Cache aller MA (für "Aktive" / "Inaktive" / "Alle"-Toggle)
let _empAllRaw = [];
let _empFilter = 'aktiv';

// ── Liste laden ────────────────────────────────
async function loadMitarbeiterList() {
    // Falls vorher der Dokumente-Tab aktiv war: Layout zurücksetzen,
    // damit die Mitarbeiter-Liste links wieder sichtbar wird.
    const empLayout = document.querySelector('.emp-layout');
    if (empLayout) empLayout.classList.remove('emp-layout-dokumente');

    try {
        const res = await fetch('/api/employees', { headers: ah(), cache: 'no-store' });
        if (!res.ok) return;
        _empAllRaw = await res.json();
        applyEmpFilter();
    } catch (e) {
        document.getElementById('empList').innerHTML =
            '<div class="emp-no-selection" style="height:200px"><span>Fehler beim Laden</span></div>';
    }
}

/// Wendet den Aktiv-Filter + Filial-Filter an und rendert die Liste neu.
function applyEmpFilter() {
    let filtered = _empAllRaw;
    if (_empFilter === 'aktiv')   filtered = filtered.filter(e => e.isActive);
    if (_empFilter === 'inaktiv') filtered = filtered.filter(e => !e.isActive);

    if (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) {
        filtered = filtered.filter(e =>
            e.employments?.some(v => Number(v.companyProfileId) === Number(fixedCompanyProfileId))
        );
    }

    allEmployees = filtered.sort((a, b) => {
        const na = ((a.firstName ?? '') + ' ' + (a.lastName ?? '')).trim().toLowerCase();
        const nb = ((b.firstName ?? '') + ' ' + (b.lastName ?? '')).trim().toLowerCase();
        return na.localeCompare(nb, 'de');
    });
    // Aktuelle Suche erneut anwenden
    filterEmployeeList();
}

/// Schaltet zwischen Aktive / Inaktive / Alle um (von den Buttons aufgerufen).
function setEmpFilter(mode) {
    _empFilter = mode;
    const styleActive   = 'flex:1;padding:6px 8px;font-size:11.5px;font-weight:600;background:#3b82f6;color:white;border:none;cursor:pointer';
    const styleInactive = 'flex:1;padding:6px 8px;font-size:11.5px;font-weight:600;background:#f1f5f9;color:#475569;border:none;cursor:pointer';
    const a = document.getElementById('empFilterAktiv');
    const i = document.getElementById('empFilterInaktiv');
    const all = document.getElementById('empFilterAlle');
    if (a)   a.style.cssText  = (mode === 'aktiv'   ? styleActive : styleInactive) + ';border-radius:6px 0 0 6px';
    if (i)   i.style.cssText  =  mode === 'inaktiv' ? styleActive : styleInactive;
    if (all) all.style.cssText = (mode === 'alle'    ? styleActive : styleInactive) + ';border-radius:0 6px 6px 0';
    applyEmpFilter();
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
        const isInactive = !e.isActive;
        return `
        <div class="emp-list-item ${active}" onclick="selectEmployee(${e.id})"${isInactive ? ' style="opacity:0.65"' : ''}>
            <div class="emp-avatar ${isFemale ? 'female' : ''}">${initials}</div>
            <div>
                <div class="emp-list-name">${name}${isInactive ? ' <span style="color:#94a3b8;font-weight:400;font-size:11px">(inaktiv)</span>' : ''}</div>
                <div class="emp-list-nr">${e.employeeNumber ?? ''}</div>
            </div>
        </div>`;
    }).join('');
}

// ── Suche/Filter ───────────────────────────────
function filterEmployeeList() {
    const q = (document.getElementById('empSearch')?.value ?? '').toLowerCase().trim();
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
            <div class="emp-tab"        data-tab="absenzen"   onclick="switchEmpTab('absenzen')" style="line-height:1.2;text-align:center">Absenzen<br>Zulagen<br>Abzüge</div>
            <div class="emp-tab"        data-tab="formulare"  onclick="switchEmpTab('formulare')">Formulare</div>
            <div class="emp-tab"        data-tab="ktg"        onclick="switchEmpTab('ktg')">KTG/UVG</div>
            <div class="emp-tab"        data-tab="dokumente"  onclick="switchEmpTab('dokumente')">Dokumente</div>
        </div>
    </div>
    <div class="emp-detail-body">
        <!-- TAB: Persönliche Angaben -->
        <div class="emp-tab-content active" id="emp-tab-personal">
            <div class="emp-section-title">Personalien</div>
            <div class="emp-field-grid-3">
                ${field('Nachname',       emp.lastName)}
                ${field('Vorname',        emp.firstName)}
                ${field('Kurzname',       emp.shortName)}
                ${field('Ledigname',      emp.maidenName)}
                ${field('Geburtsdatum',   emp.dateOfBirth ? formatDate(emp.dateOfBirth) : null)}
                ${field('Geschlecht',     emp.gender)}
                ${field('AHV-Nummer',     emp.socialSecurityNumber)}
                ${field('Zivilstand',     emp.zivilstand ?? emp.maritalStatus)}
                ${field('Sprache',        emp.languageCode)}
                ${field('Anrede',         emp.salutation)}
                ${field('Briefanrede',    emp.letterSalutation)}
                ${field('Heimatort',      emp.placeOfOrigin)}
                ${field('Konfession',     emp.religion)}
                ${field('Nationalität',   emp.nationality)}
            </div>
            <div class="emp-section-title">Aufenthalt</div>
            <div class="emp-field-grid-3">
                ${field('Bewilligung',       emp.permitType
                    ? `${emp.permitType.code}${emp.permitType.description ? ' — ' + emp.permitType.description : ''}`
                    : (emp.permitTypeId ? 'Typ ' + emp.permitTypeId : null))}
                ${field('Gültig bis',        emp.permitExpiryDate ? formatDate(emp.permitExpiryDate) : null)}
                ${field('ZEMIS-Nr.',         emp.zemisNumber)}
            </div>
            <div class="emp-section-title">Arbeitsverhältnis</div>
            <div class="emp-field-grid-3">
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
            <div class="emp-field-grid-3">
                ${field('Strasse',      emp.street ? emp.street + (emp.houseNumber ? ' ' + emp.houseNumber : '') : null)}
                ${field('Strasse 2',    emp.street2)}
                ${field('Postfach',     emp.poBox)}
                ${field('PLZ / Ort',    emp.zipCode ? emp.zipCode + ' ' + (emp.city ?? '') : emp.city)}
                ${field('BFS-Nr.',      emp.bfsNumber)}
                ${field('Kanton',       emp.cantonCode ? (kantonNameFor(emp.cantonCode) ? `${emp.cantonCode} — ${kantonNameFor(emp.cantonCode)}` : emp.cantonCode) : null)}
                ${field('Land',         emp.country)}
            </div>
            <div class="emp-section-title">Kontakt</div>
            <div class="emp-field-grid-3">
                ${field('Telefon',      emp.phoneMobile)}
                ${field('Telefon 2',    emp.phone2)}
                ${field('E-Mail',       emp.email)}
                ${field('IncaMail',     emp.incamailDisabled ? 'Deaktiviert' : 'Aktiv')}
            </div>

            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                <span>Bankverbindung</span>
                <button class="btn-emp-add" onclick="openBankAccountModal(null)">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                    Neue Bankverbindung
                </button>
            </div>
            <div id="bankAccountsContent">
                <div class="emp-placeholder"><span>Wird geladen…</span></div>
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

        <!-- TAB: Absenzen / Zulagen / Abzüge -->
        <div class="emp-tab-content" id="emp-tab-absenzen">
            <!-- Bereich 1: Absenzen -->
            <div class="emp-section-title">Absenzen</div>
            <div id="absenzenContent">
                <div class="emp-placeholder">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>
                    <span>Bitte wählen Sie einen Mitarbeiter</span>
                </div>
            </div>

            <!-- Trennung -->
            <div style="height:1px;background:#e2e8f0;margin:28px 0"></div>

            <!-- Bereich 2: Wiederkehrende Zulagen &amp; Abzüge -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                <span>Wiederkehrende Zulagen &amp; Abzüge</span>
                <span style="font-size:11px;font-weight:400;color:#94a3b8">Werden bei jedem Lohnlauf im Gültigkeitszeitraum automatisch verrechnet</span>
            </div>
            <div id="recurringWagesContent">
                <div class="emp-placeholder"><span>Bitte wählen Sie einen Mitarbeiter</span></div>
            </div>

            <!-- Trennung -->
            <div style="height:1px;background:#e2e8f0;margin:28px 0"></div>

            <!-- Bereich 3: Lohnabtretungen (Pfändung / Sozialamt) -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                <span>Lohnabtretungen</span>
                <span style="font-size:11px;font-weight:400;color:#94a3b8">Lohnpfändung oder Abtretung an Sozialamt — nach Netto berechnet</span>
            </div>
            <div id="lohnAssignmentsContent">
                <div class="emp-placeholder"><span>Bitte wählen Sie einen Mitarbeiter</span></div>
            </div>
        </div>

<!-- TAB: KTG/UVG Durchschnitt -->
        <div class="emp-tab-content" id="emp-tab-ktg">
            <div id="ktgDurchschnittContent">
                <div style="padding:40px;text-align:center;color:#94a3b8">Wird geladen...</div>
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

<!-- TAB: Dokumente -->
        <div class="emp-tab-content" id="emp-tab-dokumente">
            <div id="empTabDokumente">
                <div class="emp-placeholder" style="height:200px">Dokumente werden geladen…</div>
            </div>
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
    if (tab === 'adressen'       && selectedEmployeeId) loadBankAccountsTab(selectedEmployeeId);
    if (tab === 'familie'        && selectedEmployeeId) loadFamilieTab(selectedEmployeeId);
    if (tab === 'quellensteuer'  && selectedEmployeeId) loadQuellensteuerTab(selectedEmployeeId);
    if (tab === 'stempelzeiten'  && selectedEmployeeId) loadStempelzeitenTab(selectedEmployeeId);
    if (tab === 'absenzen'       && selectedEmployeeId) { loadAbsenzenTab(selectedEmployeeId); loadRecurringWagesTab(selectedEmployeeId); loadLohnAssignmentsTab(selectedEmployeeId); }
    if (tab === 'formulare'      && selectedEmployeeId) loadFormulareTab(selectedEmployeeId);
    if (tab === 'ktg'            && selectedEmployeeId) loadKtgTab(selectedEmployeeId);
    if (tab === 'dokumente'      && selectedEmployeeId) loadEmpDokumente(selectedEmployeeId);

    // Volle Breite für Dokumente-Tab: Mitarbeiterliste ausblenden
    const empLayout = document.querySelector('.emp-layout');
    if (empLayout) empLayout.classList.toggle('emp-layout-dokumente', tab === 'dokumente');
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
    // Damit openQstEntry() Steuerkanton/Gemeinde/BFS aus den MA-Stammdaten
    // automatisch vorschlagen kann, qstEmployeeData hier setzen.
    qstEmployeeData     = selectedEmployee;

    const emp = selectedEmployee;

    // Stammdaten im Modal setzen
    const setTxt = (id, val) => { const el = document.getElementById(id); if (el) el.textContent = val; };
    setTxt('qstModalSub',         `${emp.firstName ?? ''} ${emp.lastName ?? ''}`.trim());
    setTxt('qstPermitDisplay',
        emp.permitType
            ? `${emp.permitType.code}${emp.permitType.description ? ' — ' + emp.permitType.description : ''}`
            : (emp.permitTypeId ? 'Typ ' + emp.permitTypeId : '–'));
    setTxt('qstWohnortDisplay',   [emp.zipCode, emp.city].filter(Boolean).join(' ') || '–');
    setTxt('qstNatDisplay',       emp.nationality ?? '–');
    const _ktName = (typeof kantonNameFor === 'function') ? kantonNameFor(emp.cantonCode) : null;
    setTxt('qstKantonDisplay',    emp.cantonCode ? (_ktName ? `${emp.cantonCode} — ${_ktName}` : emp.cantonCode) : '–');
    setTxt('qstZivilstandDisplay', emp.maritalStatus ?? '–');

    // Verlauf laden
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer`, { headers: ah() });
        qstAllEntries = res.ok ? await res.json() : [];
    } catch { qstAllEntries = []; }
    if (typeof renderQstHistoryTabs === 'function') renderQstHistoryTabs();

    // Ehepartner-Info aus Familie-Tab anzeigen
    if (typeof loadQstPartnerInfo === 'function') {
        loadQstPartnerInfo(selectedEmployeeId);
    }

    // Formular befüllen
    if (entryId) {
        try {
            const r = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer/${entryId}`, { headers: ah() });
            if (r.ok) populateQstForm(await r.json());
        } catch {}
    } else {
        // Neuer Eintrag → openQstEntry(null) übernimmt:
        // - Felder leeren
        // - Gültig-ab = letzter Eintrag.gültigBis + 1 Tag (oder heute)
        // - Auto-Fill Steuerkanton, Gemeinde, BFS-Nr aus Wohnadresse
        if (typeof openQstEntry === 'function') {
            await openQstEntry(null);
        } else {
            populateQstForm(null);
            const vf = document.getElementById('qstValidFrom');
            if (vf) vf.value = new Date().toISOString().slice(0, 10);
        }
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
    // Cache für Detail-Popup-Lookup
    window._familyMembersCache = members;

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
    const typeBadge = {
        Ehepartner: { color:'#9d174d', bg:'#fce7f3' },
        Kind:       { color:'#1e40af', bg:'#dbeafe' },
        Mutter:     { color:'#92400e', bg:'#fef3c7' },
        Vater:      { color:'#92400e', bg:'#fef3c7' },
        Sonstige:   { color:'#475569', bg:'#f1f5f9' }
    };
    let html = toolbar;

    typeOrder.forEach(type => {
        if (!groups[type]) return;
        const sectionTitle = type === 'Kind' && groups[type].length > 1 ? 'Kinder' : type;
        html += `<div class="emp-section-title" style="margin-top:14px">${sectionTitle}</div>`;
        html += `<div style="display:flex;flex-direction:column;gap:4px">`;
        groups[type].forEach(m => {
            const name = ((m.firstName ?? '') + ' ' + (m.lastName ?? '')).trim() || '–';
            const dob  = m.dateOfBirth ? formatDate(m.dateOfBirth) : '';
            const age  = m.dateOfBirth ? calcAge(m.dateOfBirth) : null;
            const meta = dob ? `${dob}${age !== null ? ' · ' + age + ' Jahre' : ''}` : '';
            const b    = typeBadge[type] ?? typeBadge.Sonstige;
            html += `
            <div onclick="showFamilyDetailPopup(${m.id})"
                 style="display:flex;align-items:center;gap:12px;padding:8px 12px;border:1px solid #e2e8f0;border-radius:8px;background:white;cursor:pointer;transition:background .15s"
                 onmouseover="this.style.background='#f8fafc'" onmouseout="this.style.background='white'">
                <span style="font-size:10.5px;font-weight:700;padding:2px 8px;border-radius:10px;background:${b.bg};color:${b.color};white-space:nowrap">${type}</span>
                <span style="font-weight:600;color:#0f172a;flex:1">${esc(name)}</span>
                <span style="font-size:12.5px;color:#64748b;white-space:nowrap">${meta}</span>
                <button onclick="event.stopPropagation();openFamilyModal(${JSON.stringify(m).replace(/"/g, '&quot;')})"
                        style="background:none;border:none;cursor:pointer;color:#64748b;padding:4px 8px;border-radius:6px;font-size:13px" title="Bearbeiten">✎</button>
                <button onclick="event.stopPropagation();deleteFamilyMember(${m.id})"
                        style="background:none;border:none;cursor:pointer;color:#dc2626;padding:4px 8px;border-radius:6px;font-size:13px" title="Löschen">🗑</button>
            </div>`;
        });
        html += `</div>`;
    });

    el.innerHTML = html;
}

function showFamilyDetailPopup(memberId) {
    const m = (window._familyMembersCache ?? []).find(x => x.id === memberId);
    if (!m) return;
    const name = ((m.firstName ?? '') + ' ' + (m.lastName ?? '')).trim() || '–';
    const dob  = m.dateOfBirth ? formatDate(m.dateOfBirth) : '–';
    const age  = m.dateOfBirth ? calcAge(m.dateOfBirth) : null;
    const subtitle = `${m.memberType}${age !== null ? ' · ' + dob + ' (' + age + ' Jahre)' : ''}`;

    const row = (label, value) => `
        <div style="display:flex;justify-content:space-between;padding:6px 0;border-bottom:1px solid #f1f5f9">
            <span style="color:#64748b;font-size:12px">${label}</span>
            <span style="color:#1e293b;font-weight:500">${value ?? '–'}</span>
        </div>`;

    const html = `
        <div style="position:fixed;inset:0;background:rgba(0,0,0,0.4);z-index:2000;display:flex;align-items:center;justify-content:center" onclick="if(event.target===this)closeFamilyDetailPopup()">
            <div style="background:white;border-radius:14px;width:480px;max-width:92vw;box-shadow:0 12px 48px rgba(0,0,0,0.2);overflow:hidden">
                <div style="padding:18px 22px;border-bottom:1px solid #e2e8f0;display:flex;align-items:flex-start;justify-content:space-between;gap:8px">
                    <div>
                        <div style="font-size:16px;font-weight:700;color:#0f172a">${esc(name)}</div>
                        <div style="font-size:12px;color:#64748b;margin-top:2px">${esc(subtitle)}</div>
                    </div>
                    <button onclick="closeFamilyDetailPopup()" style="background:none;border:none;cursor:pointer;font-size:18px;color:#94a3b8;padding:4px 8px">✕</button>
                </div>
                <div style="padding:14px 22px">
                    ${row('AHV-Nummer',     m.socialSecurityNumber || '–')}
                    ${row('In der Schweiz', m.livesInSwitzerland ? 'Ja' : 'Nein')}
                    ${row('Zulage 1 bis',   m.allowance1Until ? formatMonthYear(m.allowance1Until) : '–')}
                    ${row('Zulage 2 bis',   m.allowance2Until ? formatMonthYear(m.allowance2Until) : '–')}
                    ${row('Zulage 3 bis',   m.allowance3Until ? formatMonthYear(m.allowance3Until) : '–')}
                    ${row('QST ab',         m.qstDeductibleFrom  ? formatDate(m.qstDeductibleFrom)  : '–')}
                    ${row('QST bis',        m.qstDeductibleUntil ? formatDate(m.qstDeductibleUntil) : '–')}
                </div>
                <div style="padding:14px 22px;border-top:1px solid #e2e8f0;display:flex;justify-content:flex-end;gap:8px">
                    <button onclick="closeFamilyDetailPopup();openFamilyModal(${JSON.stringify(m).replace(/"/g, '&quot;')})" style="background:#2563eb;color:white;border:none;border-radius:8px;padding:8px 16px;font-size:13px;cursor:pointer;font-weight:600">✎ Bearbeiten</button>
                    <button onclick="closeFamilyDetailPopup()" style="background:white;border:1px solid #e2e8f0;border-radius:8px;padding:8px 16px;font-size:13px;cursor:pointer">Schliessen</button>
                </div>
            </div>
        </div>`;

    let pop = document.getElementById('familyDetailPopup');
    if (!pop) {
        pop = document.createElement('div');
        pop.id = 'familyDetailPopup';
        document.body.appendChild(pop);
    }
    pop.innerHTML = html;
}

function closeFamilyDetailPopup() {
    const pop = document.getElementById('familyDetailPopup');
    if (pop) pop.innerHTML = '';
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

// Permit-Type Cache – ausschliesslich aus der Datenbank (permit_type Tabelle)
let _permitTypeCache = null;
async function getPermitTypes() {
    if (_permitTypeCache) return _permitTypeCache;
    try {
        const res = await fetch('/api/permittypes', { headers: ah() });
        if (res.ok) {
            _permitTypeCache = await res.json();
        } else {
            console.error('Permit-Types konnten nicht geladen werden:', res.status);
            _permitTypeCache = [];
        }
    } catch (e) {
        console.error('Fehler beim Laden der Permit-Types:', e);
        _permitTypeCache = [];
    }
    return _permitTypeCache;
}

async function startEmpEdit() {
    if (!selectedEmployee) return;
    const emp = selectedEmployee;

    // Permit-Types aus DB laden
    const permitTypes = await getPermitTypes();

    // Personal-Tab mit Formularfeldern ersetzen
    const personalTab = document.getElementById('emp-tab-personal');
    if (personalTab) personalTab.innerHTML = buildEmpEditPersonal(emp, permitTypes);

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

function buildEmpEditPersonal(emp, permitTypes = []) {
    const permitOptions = permitTypes
        .filter(p => p.isActive !== false)
        .map(p => `<option value="${p.id}" ${emp.permitTypeId == p.id ? 'selected' : ""}>${p.description ?? p.code}</option>`)
        .join("");
    const isMtp = emp.employmentModel === 'MTP';
    const isFix = emp.employmentModel === 'FIX' || emp.employmentModel === 'FIX-M';
    return `
    <div class="emp-section-title">Personalien</div>
    <div class="emp-field-grid">
        ${eField('Nachname',   `<input id="ef-lastName"   class="ef-input" value="${esc(emp.lastName)}">`)}
        ${eField('Vorname',    `<input id="ef-firstName"  class="ef-input" value="${esc(emp.firstName)}">`)}
        ${eField('Kurzname',   `<input id="ef-shortName"  class="ef-input" value="${esc(emp.shortName)}">`)}
        ${eField('Ledigname',  `<input id="ef-maidenName" class="ef-input" value="${esc(emp.maidenName)}">`)}
        ${eField('Geburtsdatum', `<input id="ef-dob" class="ef-input" type="date" value="${toDateInput(emp.dateOfBirth)}">`)}
        ${eField('Geschlecht', `<select id="ef-gender" class="ef-input">
            <option value="">–</option>
            <option value="female" ${emp.gender==='female'?'selected':''}>Weiblich</option>
            <option value="male"   ${emp.gender==='male'  ?'selected':''}>Männlich</option>
        </select>`)}
        ${eField('AHV-Nummer', `<input id="ef-ahvNummer" class="ef-input" placeholder="756.XXXX.XXXX.XX" value="${esc(emp.socialSecurityNumber)}">`)}
        ${eField('Zivilstand', `<select id="ef-zivilstand" class="ef-input">
            <option value="">–</option>
            <option value="ledig"                      ${emp.zivilstand==='ledig'                      ?'selected':''}>Ledig</option>
            <option value="verheiratet"                ${emp.zivilstand==='verheiratet'                ?'selected':''}>Verheiratet</option>
            <option value="geschieden"                 ${emp.zivilstand==='geschieden'                 ?'selected':''}>Geschieden</option>
            <option value="verwitwet"                  ${emp.zivilstand==='verwitwet'                  ?'selected':''}>Verwitwet</option>
            <option value="eingetragene_partnerschaft" ${emp.zivilstand==='eingetragene_partnerschaft' ?'selected':''}>Eingetr. Partnerschaft</option>
            <option value="aufgeloeste_partnerschaft"  ${emp.zivilstand==='aufgeloeste_partnerschaft'  ?'selected':''}>Aufgel. Partnerschaft</option>
        </select>`)}
        ${eField('Sprache', `<select id="ef-lang" class="ef-input">
            <option value="">–</option>
            <option value="de" ${emp.languageCode==='de'?'selected':''}>Deutsch</option>
            <option value="fr" ${emp.languageCode==='fr'?'selected':''}>Französisch</option>
            <option value="it" ${emp.languageCode==='it'?'selected':''}>Italienisch</option>
            <option value="en" ${emp.languageCode==='en'?'selected':''}>Englisch</option>
        </select>`)}
        ${eField('Anrede', `<select id="ef-salutation" class="ef-input">
            <option value="">–</option>
            <option value="Herr"   ${emp.salutation==='Herr'  ?'selected':''}>Herr</option>
            <option value="Frau"   ${emp.salutation==='Frau'  ?'selected':''}>Frau</option>
            <option value="Divers" ${emp.salutation==='Divers'?'selected':''}>Divers</option>
        </select>`)}
        ${eField('Telefon', `<input id="ef-phone" class="ef-input" type="tel"   value="${esc(emp.phoneMobile)}">`)}
        ${eField('E-Mail',  `<input id="ef-email" class="ef-input" type="email" value="${esc(emp.email)}">`)}
    </div>

    <div class="emp-section-title">Aufenthalt</div>
    <div class="emp-field-grid">
        ${eField('Bewilligung', `<select id="ef-permitType" class="ef-input">
            <option value="0">–</option>
            ${permitOptions}
        </select>`)}
        ${eField('Gültig bis', `<input id="ef-permitExpiry" class="ef-input" type="date" value="${toDateInput(emp.permitExpiryDate)}">`)}
    </div>

    <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
        Arbeitsverhältnis
        <span style="font-size:11px;font-weight:400;color:#94a3b8">Wird im Modul Vertrag bearbeitet</span>
    </div>
    <div class="emp-field-grid">
        ${field('Eintritt',        emp.entryDate ? formatDate(emp.entryDate) : null)}
        ${field('Austritt',        emp.exitDate  ? formatDate(emp.exitDate)  : null)}
        ${field('Modell',          emp.employmentModel)}
        ${field('Stellenbezeichnung', emp.jobTitle)}
        ${field('Pensum (%)',      emp.employmentPercentage != null ? emp.employmentPercentage + ' %' : null)}
        ${field('Wochenstunden',   emp.weeklyHours != null ? emp.weeklyHours + ' h' : null)}
        ${emp.hourlyRate  != null ? field('Stundenlohn', 'CHF ' + Number(emp.hourlyRate).toFixed(2))  : ''}
        ${emp.monthlySalary != null ? field('Monatslohn', 'CHF ' + Number(emp.monthlySalary).toFixed(2)) : ''}
        ${field('Vertragsbeginn',  emp.contractStartDate ? formatDate(emp.contractStartDate) : null)}
    </div>`;
}

function buildEmpEditAdressen(emp) {
    return `
    <div class="emp-section-title">Hauptadresse</div>
    <div class="emp-field-grid">
        ${eField('Strasse',      `<input id="ef-street"  class="ef-input" value="${esc(emp.street)}">`)}
        ${eField('Hausnummer',   `<input id="ef-houseNr" class="ef-input" value="${esc(emp.houseNumber)}">`)}
        ${eField('PLZ',          `<input id="ef-zip" class="ef-input" value="${esc(emp.zipCode)}" inputmode="numeric" maxlength="4" onblur="plzLookup(this.value)" onkeyup="if(this.value.length===4)plzLookup(this.value)">`)}
        ${eField('Ort',          `<input id="ef-city" class="ef-input" value="${esc(emp.city)}">`)}
        ${eField('Kanton',       renderKantonSelect('ef-canton', emp.cantonCode))}
        ${eField('Land',         `<input id="ef-country" class="ef-input" value="${esc(emp.country ?? 'CH')}">`)}
    </div>
    <div id="ef-plz-hint" style="font-size:12px;margin-top:-6px;margin-bottom:10px"></div>
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

// Schweizer Kantone — 2-Zeichen-Codes mit deutschem Namen, alphabetisch.
const SWISS_KANTONE = [
    ['AG', 'Aargau'],           ['AI', 'Appenzell Innerrhoden'],
    ['AR', 'Appenzell Ausserrhoden'], ['BE', 'Bern'],
    ['BL', 'Basel-Landschaft'], ['BS', 'Basel-Stadt'],
    ['FR', 'Freiburg'],         ['GE', 'Genf'],
    ['GL', 'Glarus'],           ['GR', 'Graubünden'],
    ['JU', 'Jura'],             ['LU', 'Luzern'],
    ['NE', 'Neuenburg'],        ['NW', 'Nidwalden'],
    ['OW', 'Obwalden'],         ['SG', 'St. Gallen'],
    ['SH', 'Schaffhausen'],     ['SO', 'Solothurn'],
    ['SZ', 'Schwyz'],           ['TG', 'Thurgau'],
    ['TI', 'Tessin'],           ['UR', 'Uri'],
    ['VD', 'Waadt'],            ['VS', 'Wallis'],
    ['ZG', 'Zug'],              ['ZH', 'Zürich']
];

function renderKantonSelect(id, current) {
    const cur = (current ?? '').toString().toUpperCase();
    const opts = SWISS_KANTONE
        .map(([code, name]) => `<option value="${code}" ${code === cur ? 'selected' : ''}>${code} — ${name}</option>`)
        .join('');
    return `<select id="${id}" class="ef-input">
        <option value="" ${!cur ? 'selected' : ''}>— nicht gepflegt —</option>
        ${opts}
    </select>`;
}

function kantonNameFor(code) {
    if (!code) return null;
    const found = SWISS_KANTONE.find(([c]) => c === code.toUpperCase());
    return found ? found[1] : null;
}

// Lookup für PLZ → Gemeinde(n) + Kanton.
// Wird aus dem Edit-Form heraus aufgerufen, wenn der User die PLZ
// tippt oder das Feld verlässt. Befüllt die Ort- und Kanton-Felder
// automatisch. Bei mehreren Gemeinden pro PLZ erscheint eine Auswahl
// im Ort-Dropdown, der Kanton wird an die erste Gemeinde angepasst
// und aktualisiert sich live, wenn der User eine andere Gemeinde wählt.
let _plzLookupAbort = null;
let _plzLookupCache = new Map();

async function plzLookup(rawPlz) {
    const plz = (rawPlz ?? '').toString().trim();
    const cityInput   = document.getElementById('ef-city');
    const kantonSelect = document.getElementById('ef-canton');
    const hint = document.getElementById('ef-plz-hint');
    if (!cityInput || !kantonSelect || !hint) return;

    if (!/^\d{4}$/.test(plz)) {
        hint.innerHTML = '';
        return;
    }

    // Vorherigen Request abbrechen (z.B. wenn User schnell tippt)
    if (_plzLookupAbort) _plzLookupAbort.abort();
    _plzLookupAbort = new AbortController();

    let locs = _plzLookupCache.get(plz);
    if (!locs) {
        try {
            const res = await fetch(`/api/swiss-locations/by-plz?plz=${encodeURIComponent(plz)}`, {
                headers: ah(),
                signal: _plzLookupAbort.signal
            });
            if (!res.ok) return;
            locs = await res.json();
            _plzLookupCache.set(plz, locs);
        } catch (e) {
            if (e.name === 'AbortError') return;
            return;
        }
    }

    if (!locs.length) {
        hint.innerHTML = `<span style="color:#b45309">⚠ PLZ ${plz} nicht im Ortschaftsverzeichnis gefunden — Ort und Kanton bitte manuell eintragen.</span>`;
        return;
    }

    // Eindeutiger Treffer → automatisch setzen
    if (locs.length === 1) {
        const l = locs[0];
        cityInput.value   = l.gemeindename;
        kantonSelect.value = l.kantonskuerzel;
        hint.innerHTML = `<span style="color:#16a34a">✓ ${l.gemeindename} (${l.kantonskuerzel})</span>`;
        return;
    }

    // Mehrere Gemeinden → Auswahl-Dropdown im Hint-Bereich
    // (nicht am Ort-Feld selbst, damit manuelles Eintragen weiter möglich ist)
    const opts = locs.map(l => `<option value="${esc(l.gemeindename)}" data-kanton="${l.kantonskuerzel}">${esc(l.gemeindename)} (${l.kantonskuerzel})</option>`).join('');
    hint.innerHTML = `
        <span style="color:#475569">Mehrere Gemeinden für PLZ ${plz} — bitte auswählen:</span>
        <select id="ef-plz-choice" class="ef-input" style="margin-left:8px;padding:4px 8px;font-size:12.5px">
            <option value="" disabled selected>— wählen —</option>
            ${opts}
        </select>`;

    const sel = document.getElementById('ef-plz-choice');
    // Falls der aktuelle Ort in der Liste ist, gleich vorwählen
    const preMatch = locs.find(l => l.gemeindename === cityInput.value);
    if (preMatch) {
        sel.value = preMatch.gemeindename;
        kantonSelect.value = preMatch.kantonskuerzel;
    }
    sel.onchange = () => {
        const opt = sel.options[sel.selectedIndex];
        const kt  = opt?.dataset?.kanton;
        cityInput.value = sel.value;
        if (kt) kantonSelect.value = kt;
    };
}

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
        cantonCode:   document.getElementById('ef-canton')?.value       || null,
        permitTypeId: parseInt(document.getElementById('ef-permitType')?.value) || 0,
        permitExpiryDate: document.getElementById('ef-permitExpiry')?.value || null,
        entryDate:    document.getElementById('ef-entry')?.value        || null,
        exitDateSet:  true,
        exitDate:     exitVal || null,
        socialSecurityNumber: document.getElementById('ef-ahvNummer')?.value || null,
        shortName:    document.getElementById('ef-shortName')?.value    || null,
        zivilstand:   document.getElementById('ef-zivilstand')?.value   || null,
    };

    try {
        // Nur Stammdaten speichern – Vertragsdaten werden im Modul Vertrag bearbeitet
        const requests = [
            fetch(`/api/employees/${selectedEmployeeId}`, {
                method: 'PUT',
                headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify(empPayload)
            })
        ];

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
        const activeEmp = selectedEmployee?.employments?.find(e => e.isActive)
                       ?? selectedEmployee?.employments?.[0];
        const cpId      = activeEmp?.companyProfileId;

        const [absRes, karenzRes, sperrRes] = await Promise.all([
            fetch(`/api/absences/employee/${employeeId}`, { headers: ah() }),
            cpId
                ? fetch(`/api/absences/employee/${employeeId}/karenz-history?companyProfileId=${cpId}`, { headers: ah() })
                : Promise.resolve(null),
            fetch(`/api/absences/employee/${employeeId}/sperrfrist`, { headers: ah() }),
        ]);
        if (!absRes.ok) throw new Error();
        const absences      = await absRes.json();
        const karenzHistory = karenzRes && karenzRes.ok ? await karenzRes.json() : [];
        const sperrfrist    = sperrRes && sperrRes.ok ? await sperrRes.json() : null;
        renderAbsenzenList(el, absences, employeeId, karenzHistory, sperrfrist);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
    }
}

function renderAbsenzenList(el, absences, employeeId, karenzHistory = [], sperrfrist = null) {
    const empModel = selectedEmployee?.employmentModel ?? '';
    const noHours  = empModel === 'UTP';
    const sperrHtml  = renderSperrfristPanel(sperrfrist);
    const karenzHtml = renderKarenzPanel(karenzHistory);

    let rows = '';
    if (absences.length === 0) {
        rows = `<tr><td colspan="6" style="text-align:center;color:#94a3b8;padding:24px">Keine Absenzen erfasst</td></tr>`;
    } else {
        absences.forEach(a => {
            const meta   = ABSENCE_LABELS[a.absenceType] ?? { label: a.absenceType, color: '' };
            const prozent = Number(a.prozent ?? 100);
            const typBadge = prozent < 100
                ? `${meta.label} <span style="font-size:10px;opacity:0.85;font-weight:600">${prozent}%</span>`
                : meta.label;
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
                <td><span class="abs-type-badge ${meta.color}">${typBadge}</span></td>
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
    ${sperrHtml}
    ${karenzHtml}
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

// ══════════════════════════════════════════════════════════════════
// SPERRFRIST-PANEL (Kündigungsschutz nach Art. 336c OR)
// ══════════════════════════════════════════════════════════════════
// Zeigt ob/bis wann dem MA bei durchgehender Krankheit oder Unfall
// NICHT gekündigt werden darf. Sperrfrist-Dauer je Dienstjahr:
// 1. DJ = 30 Tage, 2.-5. DJ = 90 Tage, ab 6. DJ = 180 Tage.
function renderSperrfristPanel(info) {
    if (!info) return '';

    const wrap = (color, bg, border, title, body, extra = '') => `
        <div style="background:${bg};border:1px solid ${border};border-radius:12px;padding:14px 18px;margin-bottom:14px">
            <div style="display:flex;justify-content:space-between;align-items:flex-start;gap:12px;flex-wrap:wrap">
                <div style="flex:1;min-width:220px">
                    <div style="font-size:11px;font-weight:700;color:${color};text-transform:uppercase;letter-spacing:0.08em">Kündigungsschutz · Art. 336c OR</div>
                    <div style="font-weight:600;font-size:14px;color:#0f172a;margin-top:3px">${title}</div>
                    <div style="font-size:12px;color:#475569;margin-top:4px;line-height:1.5">${body}</div>
                </div>
                ${extra}
            </div>
        </div>`;

    const status = info.status;
    const djText = info.dienstjahrAmStichtag ? `${info.dienstjahrAmStichtag}. Dienstjahr` : '–';

    if (status === 'KEIN_EINTRITT') {
        return wrap('#64748b', '#f8fafc', '#e2e8f0',
            'Kein Eintrittsdatum hinterlegt',
            'Ohne Eintrittsdatum kann die Sperrfrist nicht berechnet werden. Bitte Vertragsdaten ergänzen.');
    }
    if (status === 'IN_PROBEZEIT') {
        return wrap('#7c3aed', '#faf5ff', '#e9d5ff',
            info.statusText,
            `${djText} · Eintritt ${fmtDate(info.entryDate)}${info.probezeitEndDate ? ` · Probezeit bis ${fmtDate(info.probezeitEndDate)}` : ''}. ${info.hinweis ?? ''}`);
    }
    if (status === 'KEINE_AU') {
        return wrap('#16a34a', '#f0fdf4', '#bbf7d0',
            'Kein Kündigungsschutz aktiv',
            `${djText} · Ordentliche Kündigung ist möglich. Sperrfristen nach Art. 336c OR greifen erst bei durchgehender Krankheit oder Unfall.`);
    }

    // GESCHUETZT / SPERRFRIST_ABGELAUFEN — volles Datenbild anzeigen
    const isGeschuetzt = status === 'GESCHUETZT';
    const color    = isGeschuetzt ? '#b91c1c' : '#16a34a';
    const bg       = isGeschuetzt ? '#fef2f2' : '#f0fdf4';
    const border   = isGeschuetzt ? '#fecaca' : '#bbf7d0';

    const grundLabel = info.auGrund === 'UNFALL' ? 'Unfall'
                    : info.auGrund === 'KRANK+UNFALL' ? 'Krankheit + Unfall (gemischt)'
                    : 'Krankheit';

    const sperrTagesText = info.sperrfristTageHoechstenfalls
        ? `${info.sperrfristTage} Tage <span style="color:#94a3b8">(erhöht wegen Dienstjahr-Übergang)</span>`
        : `${info.sperrfristTage} Tage`;

    const title = isGeschuetzt
        ? `Kündigung gesperrt bis ${fmtDate(info.sperrfristEnde)} — frühestens kündbar am <b style="color:${color}">${fmtDate(info.kuendigungAbDatum)}</b>`
        : `Sperrfrist am ${fmtDate(info.sperrfristEnde)} abgelaufen — Kündigung jetzt möglich`;

    const body = `
        Arbeitsunfähig seit <b>${fmtDate(info.auBeginn)}</b> (${grundLabel}, ${info.auDauerTage} Tag${info.auDauerTage === 1 ? '' : 'e'} am Stück) ·
        ${djText} · Sperrfrist ${sperrTagesText}.
        ${info.hinweis ? `<div style="margin-top:6px;padding:8px 10px;background:#fffbeb;border:1px solid #fde68a;border-radius:6px;color:#78350f;font-size:11px">⚠︎ ${info.hinweis}</div>` : ''}`;

    const badge = isGeschuetzt
        ? `<div style="text-align:right;min-width:130px">
               <div style="font-size:24px;font-weight:700;color:${color}">${info.verbleibendeTage}</div>
               <div style="font-size:11px;color:#64748b">Tag${info.verbleibendeTage === 1 ? '' : 'e'} bis Kündigung möglich</div>
           </div>`
        : `<div style="text-align:right;min-width:130px">
               <div style="font-size:13px;font-weight:700;color:${color}">✓ Schutz abgelaufen</div>
           </div>`;

    return wrap(color, bg, border, title, body, badge);
}

// ══════════════════════════════════════════════════════════════════
// KARENZ-PANEL
// ══════════════════════════════════════════════════════════════════
// Rendert eine Übersicht der Karenzjahre: aktuelles Jahr prominent mit
// Progress-Balken + verbrauchte Tage, ältere Jahre zusammenklappbar.
function renderKarenzPanel(history) {
    if (!Array.isArray(history) || history.length === 0) return '';

    const [current, ...older] = history;  // GetHistoryAsync liefert absteigend
    const curInfo = current.info;
    const max     = Number(curInfo.tageMax) || 14;
    const used    = Number(curInfo.tageVerbraucht) || 0;
    const pct     = Math.min(100, Math.round((used / max) * 100));
    const remaining = Math.max(0, max - used);
    const grenzTxt  = curInfo.grenzErreichtAm
        ? `<span style="color:#b91c1c;font-weight:600">Grenze am ${fmtDate(curInfo.grenzErreichtAm)} erreicht → reduzierte Lohnfortzahlung</span>`
        : `<span style="color:#16a34a">Innerhalb der Karenz — volle Lohnfortzahlung</span>`;

    const barColor = curInfo.grenzErreichtAm ? '#dc2626' : (pct > 75 ? '#f59e0b' : '#3b82f6');

    const olderBlocks = older.map(j => {
        const jInfo = j.info;
        const jUsed = Number(jInfo.tageVerbraucht) || 0;
        const jMax  = Number(jInfo.tageMax) || max;
        const reached = jInfo.grenzErreichtAm
            ? `<span style="color:#b91c1c">Grenze am ${fmtDate(jInfo.grenzErreichtAm)}</span>`
            : `<span style="color:#64748b">nicht erreicht</span>`;
        const rows = j.krankheiten.map(k => `
            <tr>
                <td>${fmtDate(k.dateFrom)} – ${fmtDate(k.dateTo)}</td>
                <td style="text-align:center">${k.tageImJahr}</td>
                <td style="text-align:center">${Number(k.prozent).toFixed(0)}%</td>
                <td style="text-align:right">${Number(k.karenztageInDiesemJahr).toFixed(2)}</td>
                <td style="text-align:right;color:#64748b">${Number(k.kumuliertNach).toFixed(2)}</td>
                <td style="color:#94a3b8;font-size:11px">${k.notes ?? ''}</td>
            </tr>`).join('');
        return `
        <details style="margin-top:8px;border:1px solid #e2e8f0;border-radius:8px;background:#f8fafc">
            <summary style="padding:10px 14px;cursor:pointer;font-size:13px;display:flex;justify-content:space-between;align-items:center">
                <span><b>${fmtDate(jInfo.von)} – ${fmtDate(jInfo.bis)}</b></span>
                <span style="color:#475569">${jUsed.toFixed(2)} / ${jMax} Tage · ${reached}</span>
            </summary>
            <div style="padding:0 14px 12px">
                <table style="width:100%;font-size:12px;border-collapse:collapse;margin-top:4px">
                    <thead><tr style="color:#64748b;text-align:left">
                        <th style="padding:6px 4px">Zeitraum</th>
                        <th style="padding:6px 4px;text-align:center">Tage</th>
                        <th style="padding:6px 4px;text-align:center">%</th>
                        <th style="padding:6px 4px;text-align:right">Karenztage</th>
                        <th style="padding:6px 4px;text-align:right">Kumuliert</th>
                        <th style="padding:6px 4px">Bemerkung</th>
                    </tr></thead>
                    <tbody>${rows}</tbody>
                </table>
            </div>
        </details>`;
    }).join('');

    const currentRows = current.krankheiten.map(k => `
        <tr>
            <td>${fmtDate(k.dateFrom)} – ${fmtDate(k.dateTo)}</td>
            <td style="text-align:center">${k.tageImJahr}</td>
            <td style="text-align:center">${Number(k.prozent).toFixed(0)}%</td>
            <td style="text-align:right">${Number(k.karenztageInDiesemJahr).toFixed(2)}</td>
            <td style="text-align:right;color:#64748b">${Number(k.kumuliertNach).toFixed(2)}</td>
        </tr>`).join('');

    const currentTable = current.krankheiten.length > 0 ? `
        <details style="margin-top:12px" open>
            <summary style="cursor:pointer;font-size:12px;color:#475569">Krankheiten in diesem Karenzjahr (${current.krankheiten.length})</summary>
            <table style="width:100%;font-size:12px;border-collapse:collapse;margin-top:6px">
                <thead><tr style="color:#64748b;text-align:left">
                    <th style="padding:6px 4px">Zeitraum</th>
                    <th style="padding:6px 4px;text-align:center">Tage</th>
                    <th style="padding:6px 4px;text-align:center">%</th>
                    <th style="padding:6px 4px;text-align:right">Karenztage</th>
                    <th style="padding:6px 4px;text-align:right">Kumuliert</th>
                </tr></thead>
                <tbody>${currentRows}</tbody>
            </table>
        </details>` : '';

    return `
    <div style="background:#ffffff;border:1px solid #e2e8f0;border-radius:12px;padding:16px 20px;margin-bottom:14px">
        <div style="display:flex;justify-content:space-between;align-items:flex-start;gap:12px;flex-wrap:wrap">
            <div>
                <div style="font-size:11px;font-weight:700;color:#64748b;text-transform:uppercase;letter-spacing:0.08em">Krankheits-Karenz</div>
                <div style="font-weight:600;font-size:14px;color:#0f172a;margin-top:3px">${fmtDate(curInfo.von)} – ${fmtDate(curInfo.bis)}</div>
                <div style="font-size:12px;margin-top:4px">${grenzTxt}</div>
            </div>
            <div style="text-align:right">
                <div style="font-size:22px;font-weight:700;color:${barColor}">${used.toFixed(2)}<span style="font-size:13px;color:#94a3b8;font-weight:500"> / ${max} Tage</span></div>
                <div style="font-size:11px;color:#64748b;margin-top:2px">Noch ${remaining.toFixed(2)} Tage mit erhöhter Fortzahlung</div>
            </div>
        </div>
        <div style="margin-top:10px;height:8px;background:#f1f5f9;border-radius:4px;overflow:hidden">
            <div style="width:${pct}%;height:100%;background:${barColor};transition:width 0.3s"></div>
        </div>
        ${currentTable}
        ${older.length ? `<div style="margin-top:12px;font-size:11px;font-weight:700;color:#64748b;text-transform:uppercase;letter-spacing:0.08em">Frühere Karenzjahre</div>${olderBlocks}` : ''}
    </div>`;
}

function fmtDate(iso) {
    if (!iso) return '–';
    const d = new Date(iso + 'T00:00:00');
    return d.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

// ── Absenz-Typen-Cache ─────────────────────────────────────────
// Laden einmal pro Modal-Öffnung; Cache wird bei jedem openAbsenceModal
// zurückgesetzt, damit Admin-Änderungen sichtbar werden.
let _absenzTypenCache = null;
async function getAbsenzTypen() {
    if (_absenzTypenCache) return _absenzTypenCache;
    try {
        const res = await fetch('/api/absenz-typen', { headers: ah() });
        if (!res.ok) return [];
        _absenzTypenCache = await res.json();
        return _absenzTypenCache;
    } catch {
        return [];
    }
}

// ── Absenz-Modal ───────────────────────────────────────────────
async function openAbsenceModal(existing) {
    _absenzTypenCache = null;  // Cache invalidieren → frische Konfig holen
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

    // Reset form — bei neuer Absenz: heutiges Datum in Von/Bis vorbelegen
    const today = new Date().toISOString().slice(0, 10);
    document.getElementById('absTypeSelect').value   = currentVal;
    document.getElementById('absDateFrom').value      = existing?.dateFrom ?? today;
    document.getElementById('absDateTo').value        = existing?.dateTo   ?? today;
    document.getElementById('absProzent').value       = existing?.prozent ?? 100;
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

// Wenn Von-Datum geändert wird: Bis-Datum automatisch auf dasselbe Datum
// setzen (typischer 1-Tages-Fall). Für Mehrtages-Absenzen passt der User
// Bis danach manuell an.
function syncAbsDateTo() {
    const fromEl = document.getElementById('absDateFrom');
    const toEl   = document.getElementById('absDateTo');
    if (!fromEl || !toEl) return;
    if (!fromEl.value) return;
    toEl.value = fromEl.value;
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

    if (type === 'FERIEN' || type === 'FEIERTAG') {
        // Ferien und Feiertag: alle Tage im Zeitraum zählen automatisch —
        // kein Checkbox-Ankreuzen nötig (das Backend nimmt den DateFrom..DateTo-
        // Range als Grundlage). Wir zeigen nur einen Info-Text.
        const typLabel = type === 'FEIERTAG' ? 'Feiertag' : 'Ferien';
        box.innerHTML = `<div class="abs-day-info">Alle ${days.length} Tag(e) im Zeitraum werden automatisch als ${typLabel} verbucht.</div>`;
        calcAbsHoursPreview();
        return;
    }

    // KRANK / UNFALL / SCHULUNG: Tage auswählen.
    // Default-Vorauswahl: alle Tage im Bereich angekreuzt — ausser die
    // Absenz umfasst eine komplette Mo–So-Woche, dann sind Sa + So dieser
    // Woche NICHT vorausgewählt (typischer Fall bei Langzeit-Erfassung:
    // User will meist nur Werktage als Arbeitstage zählen).
    const daySet = new Set(days.map(d => localIso(d)));
    const isFullWeekInRange = (d) => {
        // Montag der Woche finden (getDay: 0=So, 1=Mo, ..., 6=Sa)
        const dow = d.getDay();
        const daysFromMonday = dow === 0 ? 6 : dow - 1;
        const monday = new Date(d);
        monday.setDate(d.getDate() - daysFromMonday);
        for (let i = 0; i < 7; i++) {
            const check = new Date(monday);
            check.setDate(monday.getDate() + i);
            if (!daySet.has(localIso(check))) return false;
        }
        return true;
    };

    let html = '<div class="abs-day-label">Welche Tage hätte der/die Mitarbeitende gearbeitet?</div><div class="abs-day-grid">';
    days.forEach(d => {
        const iso     = localIso(d);   // timezone-sicher!
        const dow     = d.getDay();
        const weekday = dayNames[dow];
        const dateStr = d.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit' });
        const isSaSo  = dow === 0 || dow === 6;
        const defaultOff = isSaSo && isFullWeekInRange(d);
        const chk = preselect.length > 0
            ? (preselect.includes(iso) ? 'checked' : '')
            : (defaultOff ? '' : 'checked');
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

    // FERIEN und FEIERTAG: alle Tage im Bereich zählen (keine Tages-Auswahl
    // durch den User — der Eintrag deckt grundsätzlich alle Tage im Zeitraum ab).
    if (type === 'FERIEN' || type === 'FEIERTAG') {
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

    // Ausfall-Prozent (Default 100). Wird multiplikativ auf die Stunden
    // angewendet — 50 = halb krank/abwesend, etc.
    let prozent = Number(document.getElementById('absProzent')?.value ?? 100);
    if (!Number.isFinite(prozent) || prozent <= 0) prozent = 100;
    if (prozent > 100) prozent = 100;
    const pFactor   = prozent / 100;
    const pSuffix   = prozent < 100 ? ` × ${prozent}%` : '';

    // Konfiguration für diesen Absenz-Typ aus Cache laden
    const typen = await getAbsenzTypen();
    const typCfg = typen.find(t => t.code === type);
    let modus            = typCfg?.gutschriftModus ?? '1/5';
    let hatGutschrift    = typCfg?.zeitgutschrift  ?? true;
    const utpAuszahlung  = typCfg?.utpAuszahlung   ?? false;
    const basisStunden   = typCfg?.basisStunden    ?? 'BETRIEB';
    const reduziertSaldo = typCfg?.reduziertSaldo  ?? null;

    // FIX/FIX-M bei FEIERTAG: Walter-Regel — Zeitgutschrift 1/7 vom Wochensoll
    // (unabhängig von der AbsenzTyp-Config, weil FEIERTAG global auf "wird
    // ausbezahlt" steht, damit MTP-Logik funktioniert; MTP wurde oben schon
    // per early-return abgefangen).
    if ((empModel === 'FIX' || empModel === 'FIX-M') && type === 'FEIERTAG') {
        modus = '1/7';
        hatGutschrift = true;
    }

    // MTP bei FEIERTAG: keine Zeitgutschrift — Feiertage werden monatlich
    // über die Festlohn-Position ausbezahlt (Lohnposition 10.3), daher kein
    // Eintrag in den Arbeitsstunden-Saldo.
    if (empModel === 'MTP' && type === 'FEIERTAG') {
        previewEl.innerHTML = '<span class="abs-hours-label">MTP: Feiertag ist im Monatslohn enthalten (kein Saldo-Eintrag)</span>';
        previewEl.dataset.hours = '0';
        return;
    }

    // MTP und UTP bei FERIEN: KEINE Zeitgutschrift. Stattdessen:
    //   - MTP: Garantie-Festlohn (10.5) wird um die Ferientage gekürzt;
    //          Ferien-Auszahlung anteilig aus Ferien-Geld-Saldo (CHF).
    //   - UTP: Auszahlung anteilig aus Ferien-Geld-Saldo (CHF) —
    //          sofern der Firmenparameter "Feriengeld auf Konto"
    //          aktiv ist (sonst wird Ferien mit Stundenlohn ausbezahlt).
    //   Beide: Ferien-Tage-Saldo wird um die bezogenen Ferientage reduziert.
    if ((empModel === 'MTP' || empModel === 'UTP') && type === 'FERIEN') {
        const modellInfo = empModel === 'MTP'
            ? 'Festlohn wird um diese Tage gekürzt, Auszahlung aus Ferien-Geld-Saldo (CHF)'
            : 'Auszahlung anteilig aus Ferien-Geld-Saldo (CHF)';
        previewEl.innerHTML = `<span class="abs-hours-label">${empModel}: ${count} Ferientag${count > 1 ? 'e' : ''} — ${modellInfo}. Ferien-Tage-Saldo -${count}. (Keine Stunden-Gutschrift)</span>`;
        previewEl.dataset.hours = '0';
        return;
    }

    // Wochenstunden-Basis pro AbsenzTyp (CH-Payroll-Regel):
    //   BETRIEB = Filial-NormalWeeklyHours (42 h Default)
    //   VERTRAG = Modell + Typ-abhängig:
    //     MTP       → GuaranteedHoursPerWeek (z.B. 33 h/Woche)
    //                  → alle Typen: Krank/Unfall/Ferien-Gutschrift basieren
    //                    auf der Garantie (nicht auf Betriebs-Wochen).
    //     FIX/FIX-M → Spezialregel:
    //                  FERIEN und FEIERTAG: pensum-adjustiertes Wochensoll
    //                    (1/7 × WeeklyHours bzw. betriebWeekly × Pensum/100)
    //                  KRANK und UNFALL: volle Betriebs-Wochen (1/5 × 42h)
    //                    — entspricht Walter's Vorgabe:
    //                    "bei FIX und FIX-M immer 1/5 der Betriebszeit"
    //     UTP       → Betrieb (Fallback; Gutschrift bei UTP ist selten)
    const betriebWeekly = Number(selectedCompanyProfile?.normalWeeklyHours ?? 42);
    let weeklyH = betriebWeekly;
    if (basisStunden === 'VERTRAG') {
        if (empModel === 'MTP') {
            weeklyH = Number(selectedEmployee?.guaranteedHoursPerWeek
                          ?? selectedEmployee?.weeklyHours
                          ?? betriebWeekly);
        } else if (empModel === 'FIX' || empModel === 'FIX-M') {
            // NUR bei FERIEN/FEIERTAG pensum-adjustiert, sonst Betrieb.
            if (type === 'FERIEN' || type === 'FEIERTAG') {
                const pct = Number(selectedEmployee?.employmentPercentage ?? 100);
                weeklyH = Number(selectedEmployee?.weeklyHours
                              ?? (betriebWeekly * pct / 100));
            }
            // KRANK, UNFALL, SCHULUNG, MILITAER: weeklyH bleibt auf betriebWeekly
        }
        // UTP: bleibt auf betriebWeekly
    }

    let hours = 0;
    let hint  = '';

    // UTP: nur Typen mit UtpAuszahlung-Flag bekommen etwas
    if (empModel === 'UTP' && !utpAuszahlung) {
        previewEl.innerHTML = '<span class="abs-hours-label">UTP: keine automatische Stundengutschrift für diesen Typ</span>';
        previewEl.dataset.hours = '0';
        return;
    }

    if (reduziertSaldo === 'NACHT_STUNDEN') {
        hours = count * (weeklyH / 5) * pFactor;
        const saldoHint = empModel === 'UTP'
            ? 'als Stundenlohn ausbezahlt, Nacht-Saldo sinkt entsprechend'
            : 'wird zu Ist-Stunden addiert, Nacht-Saldo sinkt entsprechend';
        hint  = `<span class="abs-hours-pos">+${hours.toFixed(2)} h</span> <span class="abs-hours-label">${typCfg?.bezeichnung ?? type}: ${count} Tag${count>1?'e':''} × ${weeklyH.toFixed(2)} h ÷ 5${pSuffix} → ${saldoHint}</span>`;
    } else if (!hatGutschrift) {
        // Kein Zeitgutschrift → Ausbezahlung
        hours = count * (weeklyH / 5) * pFactor;
        hint  = `<span class="abs-hours-label">${typCfg?.bezeichnung ?? type}: keine Zeitgutschrift, wird ausbezahlt (${count} Tag${count>1?'e':''} × ${weeklyH.toFixed(2)} h ÷ 5${pSuffix})</span>`;
    } else if (modus === '1/7') {
        hours = count * (weeklyH / 7) * pFactor;
        // Immer Gutschrift — die Stunden werden dem Arbeitszeit-Saldo
        // hinzugerechnet, damit Ferien/ähnliche Tage nicht als Minusstunden
        // erscheinen. Bei MTP basiert die Berechnung auf den garantierten
        // Vertragsstunden (33h), bei FIX/FIX-M/UTP auf der betrieblichen
        // Wochenarbeitszeit (42h) — geregelt über absenz_typ.basis_stunden.
        hint = `<span class="abs-hours-pos">+${hours.toFixed(2)} h</span> <span class="abs-hours-label">Gutschrift (${count} Tage × ${weeklyH.toFixed(2)} h ÷ 7${pSuffix})</span>`;
    } else {
        // 1/5 (Standard)
        hours = count * (weeklyH / 5) * pFactor;
        hint  = `<span class="abs-hours-pos">+${hours.toFixed(2)} h</span> <span class="abs-hours-label">Gutschrift (${count} Tag${count>1?'e':''} × ${weeklyH.toFixed(2)} h ÷ 5${pSuffix})</span>`;
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

    let prozent = Number(document.getElementById('absProzent')?.value ?? 100);
    if (!Number.isFinite(prozent) || prozent <= 0) prozent = 100;
    if (prozent > 100) prozent = 100;

    const payload = {
        employeeId:    selectedEmployeeId,
        absenceType:   type,
        dateFrom,
        dateTo,
        workedDays:    JSON.stringify(workedDays),
        hoursCredited: hours,
        prozent,
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
        if (!res.ok) {
            let msg = 'Fehler beim Speichern.';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch {}
            alert(msg);
            return;
        }
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
        if (!res.ok) {
            let msg = 'Fehler beim Löschen.';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch {}
            alert(msg);
            return;
        }
        loadAbsenzenTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// ══════════════════════════════════════════════════════════════════
// WIEDERKEHRENDE ZULAGEN / ABZÜGE (pro Mitarbeiter, mit Gültig-ab/bis)
// ══════════════════════════════════════════════════════════════════

let _rwLohnpositionen = [];   // Lohnposition-Cache (ZULAGE + ABZUG)

async function loadRecurringWagesTab(employeeId) {
    const el = document.getElementById('recurringWagesContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';

    // Lohnposition-Katalog einmalig laden
    if (_rwLohnpositionen.length === 0) {
        try {
            const resLp = await fetch('/api/lohn-zulag-typen', { headers: ah() });
            _rwLohnpositionen = resLp.ok ? await resLp.json() : [];
        } catch { _rwLohnpositionen = []; }
    }

    try {
        const res = await fetch(`/api/employee-recurring-wages/${employeeId}`, { headers: ah() });
        if (!res.ok) throw new Error();
        const list = await res.json();
        renderRecurringWagesList(el, list, employeeId);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
    }
}

function renderRecurringWagesList(el, list, employeeId) {
    const fmtAmount = v => Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const today = new Date().toISOString().slice(0, 10);

    let rows = '';
    if (!list.length) {
        rows = `<tr><td colspan="6" style="text-align:center;color:#94a3b8;padding:20px">Keine wiederkehrenden Einträge</td></tr>`;
    } else {
        list.forEach(r => {
            const isAbzug = r.typ === 'ABZUG';
            const typeBadge = isAbzug
                ? `<span style="font-size:11px;font-weight:600;padding:2px 8px;border-radius:10px;background:#fee2e2;color:#991b1b">− Abzug</span>`
                : `<span style="font-size:11px;font-weight:600;padding:2px 8px;border-radius:10px;background:#dcfce7;color:#166534">+ Zulage</span>`;
            const activeNow = r.validFrom <= today && (!r.validTo || r.validTo >= today);
            const activeIcon = activeNow
                ? '<span title="Zurzeit aktiv" style="color:#16a34a">●</span>'
                : '<span title="Ausserhalb Gültigkeitszeitraum" style="color:#cbd5e1">○</span>';
            rows += `<tr>
                <td>${activeIcon} ${typeBadge} <span style="font-weight:500">${r.lohnpositionBezeichnung}</span>
                    <span style="color:#94a3b8;font-size:11px;margin-left:4px">[${r.lohnpositionCode}]</span></td>
                <td style="font-family:monospace;text-align:right;color:${isAbzug ? '#dc2626' : '#059669'};font-weight:600">
                    ${isAbzug ? '−' : '+'} CHF ${fmtAmount(r.betrag)}</td>
                <td style="white-space:nowrap">${fmtDate(r.validFrom)}</td>
                <td style="white-space:nowrap;color:${r.validTo ? '#334155' : '#94a3b8'}">${r.validTo ? fmtDate(r.validTo) : 'offen'}</td>
                <td style="color:#64748b">${r.bemerkung ?? ''}</td>
                <td style="text-align:right;white-space:nowrap">
                    <button class="btn-stamp-edit" onclick='openRecurringWageModal(${JSON.stringify(r).replace(/'/g,"&#39;")})'>✎</button>
                    <button class="btn-stamp-del"  onclick="deleteRecurringWage(${r.id})">✕</button>
                </td>
            </tr>`;
        });
    }

    el.innerHTML = `
    <div class="abs-toolbar">
        <button class="btn-emp-add" onclick="openRecurringWageModal(null)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Zulage / Abzug erfassen
        </button>
    </div>
    <table class="abs-table">
        <thead><tr>
            <th>Lohnposition</th>
            <th style="text-align:right">Betrag</th>
            <th>Gültig ab</th>
            <th>Gültig bis</th>
            <th>Bemerkung</th>
            <th></th>
        </tr></thead>
        <tbody>${rows}</tbody>
    </table>`;
}

function openRecurringWageModal(existing) {
    const modal = document.getElementById('recurringWageModal');
    if (!modal) return;
    modal.style.display = 'flex';
    modal.dataset.editId = existing?.id ?? '';
    document.getElementById('rwModalTitle').textContent = existing ? 'Wiederkehrende Vergütung bearbeiten' : 'Wiederkehrende Vergütung erfassen';

    // Dropdown befüllen
    const sel = document.getElementById('rwLpSel');
    sel.innerHTML = '<option value="">— Lohnposition wählen —</option>' +
        _rwLohnpositionen.map(l =>
            `<option value="${l.id}">[${l.code}] ${l.bezeichnung} ${l.typ === 'ABZUG' ? '(−)' : '(+)'}</option>`
        ).join('');
    sel.value = existing?.lohnpositionId ?? '';

    const today = new Date().toISOString().slice(0, 10);
    document.getElementById('rwBetrag').value    = existing?.betrag ?? '';
    document.getElementById('rwValidFrom').value = existing?.validFrom ?? today;
    document.getElementById('rwValidTo').value   = existing?.validTo   ?? '';
    document.getElementById('rwBemerkung').value = existing?.bemerkung ?? '';
}

function closeRecurringWageModal() {
    const modal = document.getElementById('recurringWageModal');
    if (modal) { modal.style.display = 'none'; modal.dataset.editId = ''; }
}

async function saveRecurringWage() {
    const modal = document.getElementById('recurringWageModal');
    const editId = modal?.dataset.editId;
    const lpId   = parseInt(document.getElementById('rwLpSel').value);
    const betrag = parseFloat(document.getElementById('rwBetrag').value);
    const from   = document.getElementById('rwValidFrom').value;
    const to     = document.getElementById('rwValidTo').value;
    const bem    = document.getElementById('rwBemerkung').value.trim() || null;

    if (!lpId)   { alert('Bitte eine Lohnposition wählen.'); return; }
    if (!betrag || betrag <= 0) { alert('Bitte einen gültigen Betrag eingeben.'); return; }
    if (!from)   { alert('Bitte "Gültig ab"-Datum angeben.'); return; }
    if (to && to < from) { alert('"Gültig bis" muss grösser oder gleich "Gültig ab" sein.'); return; }

    const body = {
        employeeId:     selectedEmployeeId,
        lohnpositionId: lpId,
        betrag,
        validFrom:      from,
        validTo:        to || null,
        bemerkung:      bem
    };

    try {
        const url = editId ? `/api/employee-recurring-wages/${editId}` : '/api/employee-recurring-wages';
        const method = editId ? 'PUT' : 'POST';
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            const err = await res.text();
            alert('Fehler beim Speichern: ' + err);
            return;
        }
        closeRecurringWageModal();
        loadRecurringWagesTab(selectedEmployeeId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function deleteRecurringWage(id) {
    if (!confirm('Eintrag wirklich löschen?')) return;
    try {
        const res = await fetch(`/api/employee-recurring-wages/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadRecurringWagesTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// ══════════════════════════════════════════════════════════════════
// LOHNABTRETUNGEN (Pfändung / Sozialamt) pro Mitarbeiter
// ══════════════════════════════════════════════════════════════════

let _laBehoerden = [];   // Cache aktive Behörden

async function loadLohnAssignmentsTab(employeeId) {
    const el = document.getElementById('lohnAssignmentsContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';

    // Behörden-Katalog einmalig laden
    if (_laBehoerden.length === 0) {
        try {
            const rB = await fetch('/api/behoerden', { headers: ah() });
            _laBehoerden = rB.ok ? await rB.json() : [];
        } catch { _laBehoerden = []; }
    }

    try {
        const res = await fetch(`/api/employee-lohn-assignments/${employeeId}`, { headers: ah() });
        if (!res.ok) throw new Error();
        const list = await res.json();
        renderLohnAssignmentsList(el, list);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
    }
}

function renderLohnAssignmentsList(el, list) {
    const fmt = v => Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const today = new Date().toISOString().slice(0, 10);

    let rows = '';
    if (!list.length) {
        rows = `<tr><td colspan="7" style="text-align:center;color:#94a3b8;padding:20px">Keine Lohnabtretungen erfasst</td></tr>`;
    } else {
        list.forEach(a => {
            const activeNow = a.validFrom <= today && (!a.validTo || a.validTo >= today);
            const fertig    = a.zielbetrag > 0 && a.bereitsAbgezogen >= a.zielbetrag;
            let statusIcon;
            if (fertig)         statusIcon = '<span title="Zielbetrag erreicht" style="color:#0891b2">✓</span>';
            else if (activeNow) statusIcon = '<span title="Aktiv" style="color:#16a34a">●</span>';
            else                statusIcon = '<span title="Nicht aktiv" style="color:#cbd5e1">○</span>';

            const fortschritt = a.zielbetrag > 0
                ? `<div style="font-size:11px;color:#64748b">Bisher ${fmt(a.bereitsAbgezogen)} von ${fmt(a.zielbetrag)} CHF</div>`
                : `<div style="font-size:11px;color:#64748b">Bisher ${fmt(a.bereitsAbgezogen)} CHF · unbegrenzt</div>`;

            rows += `<tr>
                <td>${statusIcon} <span style="font-weight:500">${a.bezeichnung}</span>
                    <div style="font-size:11px;color:#64748b">${a.behoerdeName ?? '—'}</div></td>
                <td style="text-align:right;font-family:monospace">${fmt(a.freigrenze)}</td>
                <td style="text-align:right;font-family:monospace">${a.zielbetrag > 0 ? fmt(a.zielbetrag) : '<span style="color:#94a3b8">offen</span>'}
                    ${fortschritt}</td>
                <td style="white-space:nowrap">${fmtDate(a.validFrom)}</td>
                <td style="white-space:nowrap;color:${a.validTo ? '#334155' : '#94a3b8'}">${a.validTo ? fmtDate(a.validTo) : 'Widerruf'}</td>
                <td style="color:#64748b;font-size:11px">${a.referenzAmt ? `<div>${a.referenzAmt}</div>` : ''}${a.zahlungsReferenz ? `<div style="font-family:monospace">${a.zahlungsReferenz}</div>` : ''}${a.bemerkung ?? ''}</td>
                <td style="text-align:right;white-space:nowrap">
                    <button class="btn-stamp-edit" onclick='openLohnAssignmentModal(${JSON.stringify(a).replace(/'/g,"&#39;")})'>✎</button>
                    <button class="btn-stamp-del"  onclick="deleteLohnAssignment(${a.id})">✕</button>
                </td>
            </tr>`;
        });
    }

    el.innerHTML = `
    <div class="abs-toolbar">
        <button class="btn-emp-add" onclick="openLohnAssignmentModal(null)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Lohnabtretung erfassen
        </button>
    </div>
    <table class="abs-table">
        <thead><tr>
            <th>Bezeichnung / Behörde</th>
            <th style="text-align:right">Freigrenze CHF</th>
            <th style="text-align:right">Zielbetrag / Fortschritt</th>
            <th>Ab</th>
            <th>Bis</th>
            <th>Referenz / Bemerkung</th>
            <th></th>
        </tr></thead>
        <tbody>${rows}</tbody>
    </table>`;
}

function openLohnAssignmentModal(existing) {
    const modal = document.getElementById('lohnAssignmentModal');
    if (!modal) return;
    modal.style.display = 'flex';
    modal.dataset.editId = existing?.id ?? '';
    document.getElementById('laModalTitle').textContent = existing ? 'Lohnabtretung bearbeiten' : 'Lohnabtretung erfassen';

    // Behörden-Dropdown
    const sel = document.getElementById('laBehoerdeSel');
    sel.innerHTML = '<option value="">— Behörde wählen —</option>' +
        _laBehoerden.map(b => `<option value="${b.id}">${b.name}</option>`).join('');
    sel.value = existing?.behoerdeId ?? '';

    const today = new Date().toISOString().slice(0, 10);
    document.getElementById('laBezeichnung').value      = existing?.bezeichnung ?? 'Lohnpfändung';
    document.getElementById('laFreigrenze').value       = existing?.freigrenze ?? '';
    document.getElementById('laZielbetrag').value       = (existing?.zielbetrag && existing.zielbetrag > 0) ? existing.zielbetrag : '';
    document.getElementById('laValidFrom').value        = existing?.validFrom ?? today;
    document.getElementById('laValidTo').value          = existing?.validTo   ?? '';
    document.getElementById('laReferenzAmt').value      = existing?.referenzAmt ?? '';
    const zrEl = document.getElementById('laZahlungsReferenz');
    zrEl.value = existing?.zahlungsReferenz ?? '';
    validateZahlungsReferenz(zrEl);   // initiales Live-Feedback (falls Wert vorhanden)
    document.getElementById('laBemerkung').value        = existing?.bemerkung ?? '';
}

function closeLohnAssignmentModal() {
    const modal = document.getElementById('lohnAssignmentModal');
    if (modal) { modal.style.display = 'none'; modal.dataset.editId = ''; }
}

async function saveLohnAssignment() {
    const modal = document.getElementById('lohnAssignmentModal');
    const editId = modal?.dataset.editId;
    const behoerdeId  = parseInt(document.getElementById('laBehoerdeSel').value);
    const bezeichnung = document.getElementById('laBezeichnung').value.trim() || 'Lohnpfändung';
    const freigrenze  = parseFloat(document.getElementById('laFreigrenze').value) || 0;
    const zielbetragStr = document.getElementById('laZielbetrag').value;
    const zielbetrag  = zielbetragStr ? parseFloat(zielbetragStr) : 0;
    const from        = document.getElementById('laValidFrom').value;
    const to          = document.getElementById('laValidTo').value;
    const refAmt      = document.getElementById('laReferenzAmt').value.trim() || null;
    const refZahlung  = document.getElementById('laZahlungsReferenz').value.trim() || null;
    const bem         = document.getElementById('laBemerkung').value.trim() || null;

    if (!behoerdeId) { alert('Bitte eine Behörde wählen.'); return; }
    if (freigrenze < 0)     { alert('Freigrenze muss ≥ 0 sein.'); return; }
    if (zielbetrag < 0)     { alert('Zielbetrag muss ≥ 0 sein.'); return; }
    if (!from)              { alert('Bitte "Gültig ab"-Datum angeben.'); return; }
    if (to && to < from)    { alert('"Gültig bis" muss grösser oder gleich "Gültig ab" sein.'); return; }
    if (refZahlung) {
        const check = validateReferenz(refZahlung);
        if (!check.valid && !confirm(`Die Zahlungsreferenz scheint ungültig zu sein:\n${check.error}\n\nTrotzdem speichern?`)) return;
    }

    const body = {
        employeeId: selectedEmployeeId,
        behoerdeId,
        bezeichnung,
        freigrenze,
        zielbetrag,
        validFrom: from,
        validTo:   to || null,
        referenzAmt:      refAmt,
        zahlungsReferenz: refZahlung,
        bemerkung: bem
    };

    try {
        const url = editId ? `/api/employee-lohn-assignments/${editId}` : '/api/employee-lohn-assignments';
        const method = editId ? 'PUT' : 'POST';
        const res = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!res.ok) { const err = await res.text(); alert('Fehler beim Speichern: ' + err); return; }
        closeLohnAssignmentModal();
        loadLohnAssignmentsTab(selectedEmployeeId);
    } catch(e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function deleteLohnAssignment(id) {
    if (!confirm('Lohnabtretung wirklich löschen?')) return;
    try {
        const res = await fetch(`/api/employee-lohn-assignments/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadLohnAssignmentsTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// ══════════════════════════════════════════════════════════════════
// ZAHLUNGSREFERENZ-VALIDIERUNG (QR-Referenz + SCOR/RF-Referenz)
// ══════════════════════════════════════════════════════════════════
//
// Zwei gültige Formate in der Schweiz:
//   1) QR-Referenz (früher ESR/BVR): 27 Ziffern, Modulo-10 rekursiv.
//      Die 27. Ziffer ist die Prüfziffer.
//   2) SCOR / RF-Creditor Reference (ISO 11649): "RF" + 2 Prüfziffern
//      + bis 21 alphanumerische Zeichen. Prüfung via Modulo-97.
//
// Liefert { valid: bool, type: 'QR'|'SCOR'|'UNKNOWN', error?: string }.

function validateReferenz(raw) {
    if (!raw) return { valid: true, type: 'UNKNOWN' };   // leer = ok (optional)
    const clean = raw.replace(/\s+/g, '').toUpperCase();

    // SCOR beginnt mit "RF"
    if (clean.startsWith('RF')) return validateScor(clean);

    // Sonst: rein numerisch → QR-Referenz
    if (/^\d+$/.test(clean)) return validateQrReferenz(clean);

    return { valid: false, type: 'UNKNOWN',
             error: 'Weder QR-Referenz (nur Ziffern) noch SCOR/RF-Referenz (mit "RF" am Anfang).' };
}

// QR-Referenz Modulo-10 rekursiv (27 Ziffern, letzte = Prüfziffer)
function validateQrReferenz(digits) {
    if (digits.length !== 27) {
        return { valid: false, type: 'QR',
                 error: `QR-Referenz muss exakt 27 Ziffern haben (aktuell ${digits.length}).` };
    }
    const table = [0, 9, 4, 6, 8, 2, 7, 1, 3, 5];
    let carry = 0;
    for (let i = 0; i < 26; i++) {
        carry = table[(carry + parseInt(digits[i], 10)) % 10];
    }
    const expected = (10 - carry) % 10;
    const actual   = parseInt(digits[26], 10);
    if (expected !== actual) {
        return { valid: false, type: 'QR',
                 error: `Prüfziffer falsch. Erwartet ${expected}, gefunden ${actual}.` };
    }
    return { valid: true, type: 'QR' };
}

// SCOR / RF Modulo-97 (ISO 11649 / ISO 13616)
// Gesamtlänge max. 25 Zeichen (RF + 2 + bis 21).
function validateScor(ref) {
    if (ref.length < 5 || ref.length > 25) {
        return { valid: false, type: 'SCOR',
                 error: `SCOR-Referenz muss 5–25 Zeichen haben (aktuell ${ref.length}).` };
    }
    if (!/^RF\d{2}[A-Z0-9]+$/.test(ref)) {
        return { valid: false, type: 'SCOR',
                 error: 'SCOR-Format: "RF" + 2 Prüfziffern + alphanumerisch.' };
    }
    // "RF" + Prüfziffern an den Schluss rotieren, Buchstaben zu Zahlen.
    const rearranged = ref.slice(4) + ref.slice(0, 4);
    let numeric = '';
    for (const ch of rearranged) {
        if (ch >= '0' && ch <= '9') numeric += ch;
        else                         numeric += (ch.charCodeAt(0) - 55).toString(); // A=10…Z=35
    }
    // Mod-97 in Chunks (BigInt wäre Alternative, aber so bleibt der Code simpel)
    let remainder = 0;
    for (const ch of numeric) {
        remainder = (remainder * 10 + parseInt(ch, 10)) % 97;
    }
    if (remainder !== 1) {
        return { valid: false, type: 'SCOR',
                 error: 'Prüfziffer der SCOR-Referenz stimmt nicht (MOD-97 ≠ 1).' };
    }
    return { valid: true, type: 'SCOR' };
}

// Live-Feedback im Modal (direkt unter dem Eingabefeld)
function validateZahlungsReferenz(inputEl) {
    const hint = document.getElementById('laReferenzHint');
    if (!hint) return;
    const val = inputEl.value.trim();
    if (!val) { hint.textContent = ''; hint.style.color = ''; inputEl.style.borderColor = ''; return; }
    const r = validateReferenz(val);
    if (r.valid) {
        hint.textContent = r.type === 'QR' ? '✓ Gültige QR-Referenz (27-stellig)'
                          : r.type === 'SCOR' ? '✓ Gültige SCOR/RF-Referenz'
                          : '';
        hint.style.color = '#16a34a';
        inputEl.style.borderColor = '#86efac';
    } else {
        hint.textContent = '✗ ' + r.error;
        hint.style.color = '#dc2626';
        inputEl.style.borderColor = '#fca5a5';
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
// TAB: Stempelzeiten – Einträge pro Mitarbeiter und Monat
// ══════════════════════════════════════════════
const MONATSNAMEN_DE = ['Januar','Februar','März','April','Mai','Juni',
                        'Juli','August','September','Oktober','November','Dezember'];

async function loadStempelzeitenTab(employeeId) {
    const el = document.getElementById('stempelzeitenContent');
    if (!el) return;
    if (!employeeId) {
        el.innerHTML = `<div class="emp-placeholder">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
            <span>Bitte wählen Sie einen Mitarbeiter</span>
        </div>`;
        return;
    }

    // Lohnperioden der aktuellen Filiale laden
    const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
    let perioden = [];
    if (cid) {
        try {
            const res = await fetch(`/api/payroll-perioden?companyProfileId=${cid}`,
                { headers: { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` } });
            perioden = res.ok ? await res.json() : [];
        } catch { perioden = []; }
    }
    el._stempelPerioden = perioden;

    // Default: neueste Periode
    if (!el._stempelPeriodeId && perioden.length > 0) {
        el._stempelPeriodeId = perioden[0].id;
    }

    // Fallback: wenn keine Perioden existieren, nutze Kalendermonat
    const useKalender = perioden.length === 0;
    if (useKalender && (!el._stempelYear || !el._stempelMonth)) {
        const now = new Date();
        el._stempelYear  = now.getFullYear();
        el._stempelMonth = now.getMonth() + 1;
    }

    let filterHtml;
    if (useKalender) {
        const jahre = [];
        const curY  = new Date().getFullYear();
        for (let y = curY - 3; y <= curY + 1; y++) jahre.push(y);
        const monthOpts = MONATSNAMEN_DE.map((n, i) => `
            <option value="${i+1}" ${i+1 === el._stempelMonth ? 'selected' : ''}>${n}</option>`).join('');
        const yearOpts = jahre.map(y => `
            <option value="${y}" ${y === el._stempelYear ? 'selected' : ''}>${y}</option>`).join('');
        filterHtml = `
            <select id="stempelMonthSel" class="f-input" style="width:140px;font-size:13px" onchange="stempelChangePeriod()">${monthOpts}</select>
            <select id="stempelYearSel" class="f-input" style="width:90px;font-size:13px" onchange="stempelChangePeriod()">${yearOpts}</select>
            <span style="font-size:11px;color:#94a3b8">(keine Lohnperioden definiert)</span>`;
    } else {
        const opts = perioden.map(p => `
            <option value="${p.id}" ${p.id === el._stempelPeriodeId ? 'selected' : ''}>
                ${p.label || MONATSNAMEN_DE[p.month-1] + ' ' + p.year} · ${stempelFmtDateShort(p.periodFrom)} – ${stempelFmtDateShort(p.periodTo)}
            </option>`).join('');
        filterHtml = `
            <select id="stempelPeriodeSel" class="f-input" style="min-width:320px;font-size:13px" onchange="stempelChangePeriod()">${opts}</select>`;
    }

    el.innerHTML = `
        <div style="display:flex;flex-direction:column;height:calc(100vh - 260px);min-height:400px;padding:16px;gap:10px">
            <!-- Fix-Bereich: Filter -->
            <div style="display:flex;align-items:center;gap:10px;flex-shrink:0;flex-wrap:wrap">
                ${filterHtml}
                <button class="btn btn-outline" style="font-size:12px;padding:6px 12px" onclick="stempelChangePeriod()">&#8635; Aktualisieren</button>
                <div id="stempelCount" style="margin-left:auto;font-size:12px;color:#64748b"></div>
            </div>
            <!-- Fix-Bereich: Neu-Button -->
            <div style="flex-shrink:0">
                <button class="btn btn-primary" style="font-size:12px;padding:6px 12px" onclick="stempelStartNew()">+ Neuer Eintrag</button>
            </div>
            <!-- Fix-Bereich: Edit-Form -->
            <div id="stempelEditRow" style="flex-shrink:0"></div>
            <!-- Scroll-Bereich: Liste -->
            <div id="stempelListe" style="flex:1;overflow-y:auto;min-height:100px;border-top:1px solid #e2e8f0;padding-top:6px">
                <div style="padding:20px;text-align:center;color:#94a3b8;font-size:13px">Lade…</div>
            </div>
        </div>`;

    await stempelLadeEintraege(employeeId);
}

function stempelFmtDateShort(iso) {
    if (!iso) return '';
    const m = /(\d{4})-(\d{2})-(\d{2})/.exec(iso);
    return m ? `${m[3]}.${m[2]}.${m[1].slice(2)}` : '';
}

function stempelChangePeriod() {
    const el = document.getElementById('stempelzeitenContent');
    if (!el || !selectedEmployeeId) return;
    const perSel = document.getElementById('stempelPeriodeSel');
    if (perSel) {
        el._stempelPeriodeId = parseInt(perSel.value, 10);
    } else {
        // Kalendermonat-Fallback
        el._stempelYear  = parseInt(document.getElementById('stempelYearSel').value, 10);
        el._stempelMonth = parseInt(document.getElementById('stempelMonthSel').value, 10);
    }
    stempelLadeEintraege(selectedEmployeeId);
}

// Helfer: DB speichert Stempelzeiten als `timestamp without time zone` (Lokalzeit,
// keine TZ-Konvertierung). Wir parsen die Strings direkt per Regex, nicht via
// Date-Objekt — das vermeidet jede Browser-TZ-Interpretation.
const stempelPad2    = (n) => String(n).padStart(2, '0');
const stempelFmtTime = (iso) => {
    if (!iso) return '';
    const m = /T(\d{2}):(\d{2})/.exec(iso);
    return m ? `${m[1]}:${m[2]}` : '';
};
const stempelFmtDate = (iso) => {
    if (!iso) return '';
    const m = /(\d{4})-(\d{2})-(\d{2})/.exec(iso);
    if (!m) return '';
    // Wochentag via UTC-Date (reines Datum, keine Zeit-Komponente)
    const d = new Date(Date.UTC(+m[1], +m[2]-1, +m[3]));
    const wd = ['So.','Mo.','Di.','Mi.','Do.','Fr.','Sa.'][d.getUTCDay()];
    return `${wd}, ${m[3]}.${m[2]}.${m[1].slice(2)}`;
};
const stempelFmtHours = (h) => Number(h || 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// Datum + HH:MM → ISO ohne Z (Backend speichert 1:1 als Lokalzeit)
function stempelBuildIso(dateStr, timeStr) {
    if (!dateStr || !timeStr) return null;
    return `${dateStr}T${timeStr}:00`;
}

let _stempelRowsCache = []; // Cache für Edit-Modus

async function stempelLadeEintraege(employeeId) {
    const el = document.getElementById('stempelzeitenContent');
    const listEl  = document.getElementById('stempelListe');
    const countEl = document.getElementById('stempelCount');
    if (!listEl || !el) return;
    listEl.innerHTML = `<div style="padding:20px;text-align:center;color:#94a3b8;font-size:13px">Lade…</div>`;

    // URL bauen: Perioden-Modus (dateFrom/dateTo) oder Kalendermonat (year/month)
    let url, labelHint;
    if (el._stempelPeriodeId) {
        const periode = (el._stempelPerioden || []).find(p => p.id === el._stempelPeriodeId);
        if (!periode) {
            listEl.innerHTML = `<div style="padding:20px;color:#dc2626;font-size:13px">Periode nicht gefunden.</div>`;
            return;
        }
        url = `/api/employees/${employeeId}/timeentries?dateFrom=${periode.periodFrom}&dateTo=${periode.periodTo}`;
        labelHint = `Lohnperiode ${periode.label || ''} (${stempelFmtDateShort(periode.periodFrom)}–${stempelFmtDateShort(periode.periodTo)})`;
    } else {
        url = `/api/employees/${employeeId}/timeentries?year=${el._stempelYear}&month=${el._stempelMonth}`;
        labelHint = `${MONATSNAMEN_DE[el._stempelMonth-1]} ${el._stempelYear}`;
    }

    try {
        const res = await fetch(url,
            { headers: { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` } });
        if (!res.ok) {
            listEl.innerHTML = `<div style="padding:20px;color:#dc2626;font-size:13px">Fehler ${res.status}</div>`;
            return;
        }
        const rows = await res.json();
        _stempelRowsCache = rows;

        if (countEl) {
            countEl.textContent = rows.length === 0
                ? `Keine Einträge in ${labelHint}`
                : `${rows.length} Eintrag${rows.length === 1 ? '' : 'e'} · ${labelHint}`;
        }

        stempelRenderTable(rows, employeeId);

        // Wenn leer: Shortcut-Buttons zu Monaten mit Einträgen nachladen
        if (rows.length === 0) stempelLadeQuickNav(employeeId);
    } catch (err) {
        listEl.innerHTML = `<div style="padding:20px;color:#dc2626;font-size:13px">Fehler: ${err.message}</div>`;
    }
}

async function stempelLadeQuickNav(employeeId) {
    const navEl = document.getElementById('stempelQuickNav');
    if (!navEl) return;
    try {
        const res = await fetch(`/api/employees/${employeeId}/timeentries/periods`,
            { headers: { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` } });
        if (!res.ok) return;
        const periods = await res.json();
        if (!Array.isArray(periods) || periods.length === 0) {
            navEl.innerHTML = '<div style="font-size:12px;color:#94a3b8">Noch gar keine Einträge für diesen Mitarbeiter.</div>';
            return;
        }
        const btns = periods.slice(0, 12).map(p => `
            <button class="btn btn-outline" style="font-size:11px;padding:4px 10px"
                    onclick="stempelJumpTo(${p.year}, ${p.month})">
                ${MONATSNAMEN_DE[p.month - 1].substring(0,3)} ${p.year}
                <span style="color:#94a3b8;margin-left:4px">(${p.count})</span>
            </button>`).join(' ');
        navEl.innerHTML = `
            <div style="font-size:12px;color:#64748b;margin-bottom:6px">Einträge vorhanden in:</div>
            <div style="display:flex;flex-wrap:wrap;gap:6px;justify-content:center">${btns}</div>`;
    } catch { /* silent */ }
}

function stempelJumpTo(year, month) {
    const el = document.getElementById('stempelzeitenContent');
    if (!el) return;
    // Perioden-Modus: suche Periode die Year/Month abdeckt
    const perSel = document.getElementById('stempelPeriodeSel');
    if (perSel && Array.isArray(el._stempelPerioden)) {
        const match = el._stempelPerioden.find(p => p.year === year && p.month === month);
        if (match) {
            perSel.value = match.id;
            el._stempelPeriodeId = match.id;
            stempelChangePeriod();
            return;
        }
    }
    // Kalendermonat-Fallback
    const yEl = document.getElementById('stempelYearSel');
    const mEl = document.getElementById('stempelMonthSel');
    if (!yEl || !mEl) return;
    if (!Array.from(yEl.options).some(o => parseInt(o.value, 10) === year)) {
        const opt = document.createElement('option');
        opt.value = year; opt.textContent = year;
        yEl.appendChild(opt);
    }
    yEl.value = year;
    mEl.value = month;
    stempelChangePeriod();
}

function stempelRenderTable(rows, employeeId) {
    const listEl = document.getElementById('stempelListe');
    if (!listEl) return;

    // Summen
    let sumH = 0, sumN = 0;
    rows.forEach(r => {
        sumH += Number(r.totalHours ?? r.durationHours ?? 0);
        sumN += Number(r.nightHours ?? 0);
    });

    const esc = (s) => s == null ? '' : String(s).replace(/</g,'&lt;');

    const trs = rows.map(r => {
        const wasEdited = !!r.editedBy;

        // Korrekturzeile (oben): geänderte Werte in Rot, Kommentar ergänzt um "geändert am X von Y"
        const timeColor = wasEdited ? 'color:#dc2626;font-weight:600' : '';
        const korrekturKommentar = wasEdited
            ? `${esc(r.comment || '')}${r.comment ? ' · ' : ''}<span style="color:#64748b">geändert ${new Date(r.editedAt).toLocaleDateString('de-CH')} von ${esc(r.editedBy)}</span>`
            : esc(r.comment);

        const mainRow = `
            <tr style="border-top:1px solid #f1f5f9" data-row-id="${r.id}">
                <td style="padding:8px 10px;font-size:12px;color:#475569;vertical-align:top">${stempelFmtDate(r.entryDate)}</td>
                <td style="padding:8px 10px;font-size:12px;font-family:monospace;vertical-align:top;${timeColor}">${stempelFmtTime(r.timeIn)}</td>
                <td style="padding:8px 10px;font-size:12px;font-family:monospace;vertical-align:top;${timeColor}">${stempelFmtTime(r.timeOut)}</td>
                <td style="padding:8px 10px;font-size:12px;text-align:right;font-family:monospace;vertical-align:top">${stempelFmtHours(r.durationHours)}</td>
                <td style="padding:8px 10px;font-size:12px;text-align:right;font-family:monospace;vertical-align:top;color:${Number(r.nightHours||0)>0?'#1d4ed8':'#94a3b8'}">${stempelFmtHours(r.nightHours)}</td>
                <td style="padding:8px 10px;font-size:12px;color:#64748b;vertical-align:top">${korrekturKommentar}</td>
                <td style="padding:8px 10px;font-size:11px;color:#94a3b8;vertical-align:top">${r.source || ''}</td>
                <td style="padding:6px 8px;text-align:right;white-space:nowrap;vertical-align:top">
                    <button onclick="stempelStartEdit(${r.id})" style="background:none;border:none;cursor:pointer;padding:4px;color:#3b82f6" title="Bearbeiten">&#9998;</button>
                    <button onclick="stempelDelete(${r.id})" style="background:none;border:none;cursor:pointer;padding:4px;color:#dc2626" title="Löschen">&#10005;</button>
                </td>
            </tr>`;

        if (!wasEdited) return mainRow;

        // Original-Zeile direkt darunter, braun hinterlegt (Pfeil ↲ zur Korrektur zeigt)
        const origRow = `
            <tr style="background:#fef3c7;border-top:1px dashed #fde68a">
                <td style="padding:4px 10px;font-size:11px;color:#92400e;vertical-align:top;padding-left:24px">↳ Original</td>
                <td style="padding:4px 10px;font-size:11px;font-family:monospace;color:#92400e;vertical-align:top">${stempelFmtTime(r.originalTimeIn)}</td>
                <td style="padding:4px 10px;font-size:11px;font-family:monospace;color:#92400e;vertical-align:top">${stempelFmtTime(r.originalTimeOut)}</td>
                <td colspan="2"></td>
                <td style="padding:4px 10px;font-size:11px;color:#92400e;vertical-align:top;font-style:italic">${esc(r.originalComment || '')}</td>
                <td style="padding:4px 10px;font-size:10px;color:#b45309;vertical-align:top">import</td>
                <td></td>
            </tr>`;

        return mainRow + origRow;
    }).join('');

    const empty = rows.length === 0
        ? `<tr><td colspan="8" style="padding:30px;text-align:center;color:#94a3b8;font-size:13px">
            Keine Einträge in dieser Periode.
            <div id="stempelQuickNav" style="margin-top:12px"></div>
            <div style="margin-top:10px;font-size:12px">Oder klick oben auf „+ Neuer Eintrag" um einen Eintrag zu erfassen.</div>
        </td></tr>`
        : '';

    // Hinweis: Neuer-Eintrag-Button und Edit-Form leben jetzt außerhalb
    // von #stempelListe (in loadStempelzeitenTab), damit sie fix bleiben
    // und nur die Tabelle scrollt.
    listEl.innerHTML = `
        <table style="width:100%;border-collapse:collapse;background:#fff">
            <thead>
                <tr style="background:#f8fafc">
                    <th style="padding:8px 10px;text-align:left;font-size:11px;color:#64748b;font-weight:600;background:#f8fafc;position:sticky;top:0;z-index:3">DATUM</th>
                    <th style="padding:8px 10px;text-align:left;font-size:11px;color:#64748b;font-weight:600;background:#f8fafc;position:sticky;top:0;z-index:3">IN</th>
                    <th style="padding:8px 10px;text-align:left;font-size:11px;color:#64748b;font-weight:600;background:#f8fafc;position:sticky;top:0;z-index:3">OUT</th>
                    <th style="padding:8px 10px;text-align:right;font-size:11px;color:#64748b;font-weight:600;background:#f8fafc;position:sticky;top:0;z-index:3">DAUER (h)</th>
                    <th style="padding:8px 10px;text-align:right;font-size:11px;color:#64748b;font-weight:600;background:#f8fafc;position:sticky;top:0;z-index:3">NACHT (h)</th>
                    <th style="padding:8px 10px;text-align:left;font-size:11px;color:#64748b;font-weight:600;background:#f8fafc;position:sticky;top:0;z-index:3">KOMMENTAR</th>
                    <th style="padding:8px 10px;text-align:left;font-size:11px;color:#64748b;font-weight:600;background:#f8fafc;position:sticky;top:0;z-index:3">QUELLE</th>
                    <th style="padding:8px 10px;text-align:right;font-size:11px;color:#64748b;font-weight:600;background:#f8fafc;position:sticky;top:0;z-index:3">AKTION</th>
                </tr>
            </thead>
            <tbody>${trs}${empty}</tbody>
            ${rows.length > 0 ? `<tfoot>
                <tr>
                    <td colspan="3" style="padding:10px;font-weight:700;font-size:12px;color:#1d4ed8;background:#eff6ff;border-top:2px solid #bfdbfe;position:sticky;bottom:0;z-index:3">Summe</td>
                    <td style="padding:10px;text-align:right;font-family:monospace;font-weight:700;font-size:13px;color:#1d4ed8;background:#eff6ff;border-top:2px solid #bfdbfe;position:sticky;bottom:0;z-index:3">${stempelFmtHours(sumH)}</td>
                    <td style="padding:10px;text-align:right;font-family:monospace;font-weight:700;font-size:13px;color:#1d4ed8;background:#eff6ff;border-top:2px solid #bfdbfe;position:sticky;bottom:0;z-index:3">${stempelFmtHours(sumN)}</td>
                    <td colspan="3" style="background:#eff6ff;border-top:2px solid #bfdbfe;position:sticky;bottom:0;z-index:3"></td>
                </tr>
            </tfoot>` : ''}
        </table>`;
}

// ── Edit-Form ──────────────────────────────────────────────────────────────
function stempelRenderForm(row) {
    const isNew  = !row.id;
    const dateVal = row.entryDate ? row.entryDate.substring(0, 10) : new Date().toISOString().substring(0, 10);
    const inVal   = row.timeIn    ? stempelFmtTime(row.timeIn)  : '';
    const outVal  = row.timeOut   ? stempelFmtTime(row.timeOut) : '';
    const nightVal = row.nightHours != null ? row.nightHours : '';

    // Import-Kommentar = originalComment falls schon einmal bearbeitet, sonst comment (wenn Source=import)
    const importKommentar = row.originalComment
                           ?? (row.source === 'import' ? (row.comment || '') : '');
    const letzterAenderungsgrund = (row.editedBy && row.originalComment != null) ? row.comment : null;
    const esc = (s) => s == null ? '' : String(s).replace(/</g,'&lt;').replace(/"/g, '&quot;');

    // Bei Neu: Kommentar-Feld = optional, leer. Bei Edit: Kommentar = Grund der Änderung (PFLICHT), leer.
    const kommentarLabel       = isNew ? 'Kommentar (optional)' : 'Grund der Änderung';
    const kommentarPlaceholder = isNew ? 'optional' : 'z.B. "zu früh ausgestempelt"';
    const kommentarValue       = isNew ? (row.comment || '') : ''; // bei Edit immer leer

    // Info-Block (Original-Kommentar und letzte Änderung) — nur bei Edit wenn vorhanden
    const infoBlocks = [];
    if (!isNew && importKommentar) {
        infoBlocks.push(`
            <div style="background:#fef3c7;border-left:3px solid #92400e;padding:8px 12px;border-radius:4px">
                <div style="font-size:10px;color:#92400e;font-weight:700;text-transform:uppercase;letter-spacing:.3px">Original (Import) — unveränderlich</div>
                <div style="font-size:12px;color:#78350f;margin-top:3px">${esc(importKommentar)}</div>
            </div>`);
    }
    if (letzterAenderungsgrund) {
        infoBlocks.push(`
            <div style="background:#f1f5f9;border-left:3px solid #475569;padding:8px 12px;border-radius:4px">
                <div style="font-size:10px;color:#475569;font-weight:700;text-transform:uppercase;letter-spacing:.3px">Letzte Änderung</div>
                <div style="font-size:12px;color:#334155;margin-top:3px">${esc(letzterAenderungsgrund)} — ${esc(row.editedBy)}, ${new Date(row.editedAt).toLocaleString('de-CH')}</div>
            </div>`);
    }
    const infoHtml = infoBlocks.length
        ? `<div style="display:flex;flex-direction:column;gap:6px;margin-bottom:10px">${infoBlocks.join('')}</div>`
        : '';

    return `
        <div style="background:#eff6ff;border:1px solid #bfdbfe;border-radius:10px;padding:14px;box-shadow:0 2px 4px rgba(30,64,175,.08)">
            <div style="font-weight:700;font-size:13px;color:#1d4ed8;margin-bottom:10px">${isNew ? '+ Neuer Eintrag' : '✎ Eintrag bearbeiten'}</div>
            ${infoHtml}
            <div style="display:grid;grid-template-columns:1fr 110px 110px 110px 1.5fr;gap:10px">
                <label style="font-size:11px;color:#64748b">Datum
                    <input type="date" id="stempelFormDate" value="${dateVal}" style="width:100%;padding:6px 8px;border:1px solid #cbd5e1;border-radius:6px;font-size:13px;margin-top:2px">
                </label>
                <label style="font-size:11px;color:#64748b">In (HH:MM)
                    <input type="time" id="stempelFormIn" value="${inVal}" style="width:100%;padding:6px 8px;border:1px solid #cbd5e1;border-radius:6px;font-size:13px;margin-top:2px">
                </label>
                <label style="font-size:11px;color:#64748b">Out (HH:MM)
                    <input type="time" id="stempelFormOut" value="${outVal}" style="width:100%;padding:6px 8px;border:1px solid #cbd5e1;border-radius:6px;font-size:13px;margin-top:2px">
                </label>
                <label style="font-size:11px;color:#64748b">Nachtstunden
                    <input type="number" step="0.01" min="0" id="stempelFormNight" value="${nightVal}" style="width:100%;padding:6px 8px;border:1px solid #cbd5e1;border-radius:6px;font-size:13px;margin-top:2px">
                </label>
                <label style="font-size:11px;color:#64748b">${kommentarLabel}${!isNew ? ' <span style=\"color:#dc2626\">*</span>' : ''}
                    <input type="text" id="stempelFormComment" value="${esc(kommentarValue)}" style="width:100%;padding:6px 8px;border:1px solid #cbd5e1;border-radius:6px;font-size:13px;margin-top:2px" placeholder="${kommentarPlaceholder}">
                </label>
            </div>
            <div style="margin-top:12px;display:flex;gap:8px;align-items:center">
                <button class="btn btn-primary" style="font-size:12px;padding:6px 14px" onclick="stempelSaveForm(${row.id || 'null'})">💾 Speichern</button>
                <button class="btn btn-outline" style="font-size:12px;padding:6px 14px" onclick="stempelCancelForm()">Abbrechen</button>
            </div>
        </div>`;
}

function stempelStartNew() {
    const el = document.getElementById('stempelEditRow');
    if (!el) return;
    const y = document.getElementById('stempelYearSel')?.value;
    const m = document.getElementById('stempelMonthSel')?.value;
    const defaultDate = `${y}-${String(m).padStart(2,'0')}-${String(new Date().getUTCDate()).padStart(2,'0')}`;
    el.innerHTML = stempelRenderForm({ entryDate: defaultDate });
    el.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

function stempelStartEdit(id) {
    const row = _stempelRowsCache.find(r => r.id === id);
    if (!row) return;
    const el = document.getElementById('stempelEditRow');
    if (!el) return;
    el.innerHTML = stempelRenderForm(row);
    el.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

function stempelCancelForm() {
    const el = document.getElementById('stempelEditRow');
    if (el) el.innerHTML = '';
}

async function stempelSaveForm(id) {
    const dateVal  = document.getElementById('stempelFormDate').value;
    const inVal    = document.getElementById('stempelFormIn').value;
    const outVal   = document.getElementById('stempelFormOut').value;
    const nightVal = document.getElementById('stempelFormNight').value;
    const comVal   = document.getElementById('stempelFormComment').value;

    if (!dateVal || !inVal) {
        alert('Datum und In-Zeit sind Pflicht.');
        return;
    }
    // Bei Bearbeitung ist ein Grund der Änderung Pflicht
    if (id && !comVal.trim()) {
        alert('Bitte den Grund der Änderung angeben.');
        document.getElementById('stempelFormComment')?.focus();
        return;
    }

    const body = {
        employeeId: selectedEmployeeId,
        entryDate:  dateVal,
        timeIn:     stempelBuildIso(dateVal, inVal),
        timeOut:    outVal ? stempelBuildIso(dateVal, outVal) : null,
        comment:    comVal || null,
        nightHours: nightVal === '' ? 0 : parseFloat(nightVal),
        source:     id ? undefined : 'manual'
    };

    // OUT vor IN → OUT auf Folgetag
    if (body.timeOut && body.timeOut < body.timeIn) {
        const d = new Date(body.timeOut);
        d.setUTCDate(d.getUTCDate() + 1);
        body.timeOut = d.toISOString();
    }

    const url = id
        ? `/api/employees/${selectedEmployeeId}/timeentries/${id}`
        : `/api/employees/${selectedEmployeeId}/timeentries`;
    const method = id ? 'PUT' : 'POST';

    try {
        const res = await fetch(url, {
            method,
            headers: {
                'Content-Type':  'application/json',
                'Authorization': `Bearer ${localStorage.getItem('hrToken')}`
            },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            const err = await res.text();
            alert('Fehler: ' + err);
            return;
        }
        stempelCancelForm();
        const contentEl = document.getElementById('stempelzeitenContent');
        await stempelLadeEintraege(selectedEmployeeId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function stempelDelete(id) {
    if (!confirm('Eintrag wirklich löschen?')) return;
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/timeentries/${id}`, {
            method: 'DELETE',
            headers: { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` }
        });
        if (!res.ok && res.status !== 204) {
            alert('Fehler beim Löschen.');
            return;
        }
        const contentEl = document.getElementById('stempelzeitenContent');
        await stempelLadeEintraege(selectedEmployeeId);
    } catch(e) { alert('Verbindungsfehler: ' + e.message); }
}

// ══════════════════════════════════════════════
// TAB: KTG/UVG – Tagessatz nach Spezialistenvorgabe
// Regel A (≤ 4 Perioden seit Vertragsstart): Hochrechnung aus Vertrag
// Regel B (≥ 4 Perioden):                    Durchschnitt aus AHV-Brutto (+ Mehrstunden bei MTP)
async function loadKtgTab(employeeId) {
    const el = document.getElementById('ktgDurchschnittContent');
    if (!el) return;
    el.innerHTML = '<div style="padding:20px;text-align:center;color:#94a3b8">Lade…</div>';

    try {
        const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
            ? fixedCompanyProfileId
            : (typeof selectedCompanyProfile !== 'undefined' && selectedCompanyProfile?.id)
            ? selectedCompanyProfile.id
            : null;
        if (!cid) {
            el.innerHTML = '<div style="padding:20px;color:#94a3b8">Bitte Filiale wählen.</div>';
            return;
        }

        const res = await fetch(`/api/payroll/ktg-tagessatz?employeeId=${employeeId}&companyProfileId=${cid}`,
            { headers: { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` } });

        if (res.status === 404) {
            el.innerHTML = `<div style="padding:28px;text-align:center;color:#94a3b8">
                <div style="font-size:28px;margin-bottom:8px">📊</div>
                <div style="font-size:13px">Kein aktives Anstellungsverhältnis gefunden.</div>
            </div>`;
            return;
        }
        if (!res.ok) {
            el.innerHTML = `<div style="padding:20px;color:#dc2626">Fehler ${res.status}</div>`;
            return;
        }

        const d  = await res.json();
        const bd = d.breakdown || {};
        const fmt = (n, dec = 2) => Number(n || 0).toLocaleString('de-CH', {
            minimumFractionDigits: dec, maximumFractionDigits: dec
        });

        // Vertragsstart-Datum aus ISO-String
        const vs   = d.vertragsStart ? new Date(d.vertragsStart).toLocaleDateString('de-CH') : '—';
        const badge = d.regel === 'A'
            ? `<span style="background:#fef3c7;color:#92400e;padding:3px 9px;border-radius:999px;font-size:11px;font-weight:600">REGEL A · Hochrechnung</span>`
            : `<span style="background:#dbeafe;color:#1e40af;padding:3px 9px;border-radius:999px;font-size:11px;font-weight:600">REGEL B · Durchschnitt</span>`;

        // Hilfs-Renderer für Monatsliste (bei Regel A nur Info, bei Regel B Berechnungsgrundlage)
        const renderMonate = (titel, istBerechnungsbasis) => {
            const monate = bd.monate || [];
            if (monate.length === 0) return '';
            const rows = monate.map(m => `
                <tr style="border-top:1px solid #f1f5f9">
                    <td style="padding:6px 10px;font-size:12px;color:#475569">${m.monatName} ${m.jahr}</td>
                    <td style="padding:6px 10px;font-size:12px;text-align:right;font-family:monospace">CHF ${fmt(m.brutto)}</td>
                </tr>`).join('');
            const avg = monate.reduce((s, m) => s + Number(m.brutto), 0) / monate.length;
            const footer = istBerechnungsbasis
                ? `<tr style="border-top:2px solid #e2e8f0;background:#eff6ff">
                       <td style="padding:8px 10px;font-size:12px;font-weight:700;color:#1d4ed8">Ø pro Monat</td>
                       <td style="padding:8px 10px;font-size:12px;text-align:right;font-family:monospace;font-weight:700;color:#1d4ed8">CHF ${fmt(avg)}</td>
                   </tr>`
                : '';
            return `
                <div style="margin-top:12px;padding:12px 16px;background:#f8fafc;border-radius:8px;font-size:12px;color:#334155">
                    <div style="font-weight:600;margin-bottom:6px">${titel}</div>
                    <table style="width:100%;border-collapse:collapse">
                        <tbody>${rows}</tbody>
                        ${footer ? `<tfoot>${footer}</tfoot>` : ''}
                    </table>
                </div>`;
        };

        // Breakdown-Block je nach Regel
        let breakdownHtml = '';
        if (d.regel === 'A') {
            // Hochrechnung
            if (d.vertragsModell === 'FIX' || d.vertragsModell === 'FIX-M') {
                breakdownHtml = `
                    <div style="margin-top:12px;padding:12px 16px;background:#f8fafc;border-radius:8px;font-size:12px;color:#334155">
                        <div><b>Monatslohn:</b> CHF ${fmt(bd.monatsLohn)}</div>
                        <div style="margin-top:4px;color:#64748b">Formel: Monatslohn × 12 ÷ 365 = Tagessatz 100 %</div>
                    </div>`;
            } else {
                // UTP / MTP
                breakdownHtml = `
                    <div style="margin-top:12px;padding:12px 16px;background:#f8fafc;border-radius:8px;font-size:12px;color:#334155;line-height:1.7">
                        <div><b>Stundenlohn (Basis):</b> CHF ${fmt(bd.stundenlohnBasis, 2)}</div>
                        <div>+ Ferien ${fmt(bd.ferienPct, 2)} %, + Feiertag ${fmt(bd.feiertagPct, 2)} %, + 13. ML ${fmt(bd.zehnterMLPct, 2)} %</div>
                        <div><b>= Brutto-Stundenlohn:</b> CHF ${fmt(bd.stundenlohnBrutto, 4)}</div>
                        <div style="margin-top:6px"><b>Wochenstunden:</b> ${fmt(bd.wochenStunden, 2)} h (${d.vertragsModell === 'MTP' ? 'garantiert' : 'FLEX/UTP aus Filiale'})</div>
                        <div style="margin-top:4px;color:#64748b">Formel: Wochenstunden × Std-Lohn brutto × 52 ÷ 365 = Tagessatz 100 %</div>
                    </div>`;
            }
            // Zusätzlich: bisherige Lohnperioden als Info (nicht in Berechnung)
            breakdownHtml += renderMonate(
                `ℹ️ Bisherige Lohnperioden <span style="font-weight:400;color:#64748b">(zur Information — flie\u00dft bei Regel A nicht in die Berechnung ein)</span>`,
                false
            );
        } else {
            // Durchschnitt: Monatsliste ist Berechnungsbasis
            const monate = bd.monate || [];
            breakdownHtml = renderMonate(
                `AHV-Brutto der letzten ${monate.length} Perioden`,
                true
            );
            if (d.vertragsModell === 'MTP') {
                breakdownHtml += `
                    <div style="margin-top:8px;padding:12px 16px;background:#f8fafc;border-radius:8px;font-size:12px;color:#334155;line-height:1.7">
                        <div style="font-weight:600;margin-bottom:4px">MTP-Aufteilung:</div>
                        <div>Garantie-Basis/Monat: CHF ${fmt(bd.garantieBasisMonat)} &rarr; Tagessatz CHF ${fmt(bd.garantieTagessatz)}</div>
                        <div>Ø Mehrstunden/Monat (brutto): CHF ${fmt(bd.mehrstundenAnteilMonat)} &rarr; Tagessatz CHF ${fmt(bd.mehrstundenTagessatz)}</div>
                    </div>`;
            }
            breakdownHtml += `
                <div style="margin-top:4px;font-size:12px;color:#64748b;padding:0 16px">
                    Formel: Ø × 12 ÷ 365 = Tagessatz 100 %${d.vertragsModell === 'MTP' ? ' (Garantie- und Mehrstunden-Anteil summiert)' : ''}
                </div>`;
        }

        el.innerHTML = `
            <div style="padding:20px">
                <div style="display:flex;align-items:center;gap:10px;margin-bottom:4px">
                    <div style="font-weight:700;font-size:14px;color:#0f172a">📊 KTG/UVG-Tagessatz</div>
                    ${badge}
                </div>
                <div style="font-size:12px;color:#64748b;margin-bottom:16px">
                    Vertrag <b>${d.vertragsModell || '?'}</b> seit ${vs} · ${d.anzahlPerioden} abgeschlossene Periode${d.anzahlPerioden === 1 ? '' : 'n'}
                </div>

                <table style="width:100%;border-collapse:collapse;background:#f8fafc;border-radius:10px;overflow:hidden">
                    <thead>
                        <tr style="background:#f1f5f9">
                            <th style="padding:8px 12px;text-align:left;font-size:11px;color:#64748b;font-weight:600">TAGESSATZ</th>
                            <th style="padding:8px 12px;text-align:right;font-size:11px;color:#64748b;font-weight:600">CHF / TAG</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr style="border-top:1px solid #e2e8f0">
                            <td style="padding:10px 12px;font-size:13px;color:#334155">100 %</td>
                            <td style="padding:10px 12px;font-size:13px;text-align:right;font-family:monospace">CHF ${fmt(d.tagessatz100)}</td>
                        </tr>
                        <tr style="border-top:1px solid #f1f5f9;background:#fef3c7">
                            <td style="padding:10px 12px;font-size:13px;font-weight:600;color:#92400e">88 % — Karenzfrist</td>
                            <td style="padding:10px 12px;font-size:14px;text-align:right;font-family:monospace;font-weight:700;color:#92400e">CHF ${fmt(d.tagessatz88)}</td>
                        </tr>
                        <tr style="border-top:1px solid #f1f5f9;background:#dcfce7">
                            <td style="padding:10px 12px;font-size:13px;font-weight:600;color:#15803d">80 % — Meldebetrag Versicherung</td>
                            <td style="padding:10px 12px;font-size:14px;text-align:right;font-family:monospace;font-weight:700;color:#15803d">CHF ${fmt(d.tagessatz80)}</td>
                        </tr>
                    </tbody>
                </table>

                ${breakdownHtml}
            </div>`;
    } catch(e) {
        el.innerHTML = `<div style="padding:20px;color:#dc2626">Fehler: ${e.message}</div>`;
    }
}

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

// ══════════════════════════════════════════════════════════════════
// BANKVERBINDUNG (pro MA, mit Historie)
// ══════════════════════════════════════════════════════════════════

async function loadBankAccountsTab(employeeId) {
    const el = document.getElementById('bankAccountsContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const res = await fetch(`/api/employee-bank-accounts/employee/${employeeId}`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>'; return; }
        const list = await res.json();
        renderBankAccountsList(el, list);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
    }
}

function renderBankAccountsList(el, list) {
    if (!Array.isArray(list) || list.length === 0) {
        el.innerHTML = '<div style="padding:16px;color:#94a3b8;font-style:italic;font-size:13px">Noch keine Bankverbindung erfasst. Über "Neue Bankverbindung" erfassen.</div>';
        return;
    }
    const today = new Date().toISOString().slice(0, 10);
    const rows = list.map(b => {
        const active = b.validFrom <= today && (!b.validTo || b.validTo >= today);
        const status = active
            ? '<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#dcfce7;color:#166534">Aktiv</span>'
            : (b.validFrom > today
                ? '<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#dbeafe;color:#1e40af">Geplant</span>'
                : '<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#f1f5f9;color:#64748b">Abgelaufen</span>');
        const bisTxt = b.validTo ? fmtDate(b.validTo) : '<span style="color:#94a3b8">offen</span>';
        const inhaber = b.kontoinhaber ? `<div style="font-size:11px;color:#64748b;margin-top:2px">Inhaber: ${b.kontoinhaber}</div>` : '';
        const ref     = b.zahlungsreferenz ? `<div style="font-size:11px;color:#64748b;font-family:ui-monospace,Menlo,Consolas,monospace">Ref: ${b.zahlungsreferenz}</div>` : '';
        const hauptbankBadge = b.isHauptbank
            ? '<span style="font-size:10px;font-weight:600;padding:1px 7px;border-radius:10px;background:#dbeafe;color:#1e40af;margin-left:6px">Hauptbank</span>'
            : '';
        let aufteilungInfo = '';
        if (b.aufteilungTyp && b.aufteilungTyp !== 'VOLL' && b.aufteilungWert != null) {
            const w = Number(b.aufteilungWert);
            const txt = b.aufteilungTyp === 'PROZENT'        ? `${w}% vom Brutto`
                      : b.aufteilungTyp === 'FIXBETRAG'      ? `CHF ${w.toFixed(2)} (fix)`
                      : b.aufteilungTyp === 'NETTO_ABZUEGLICH' ? `Netto − CHF ${w.toFixed(2)}`
                      : '';
            if (txt) aufteilungInfo = `<div style="font-size:11px;color:#7c3aed;margin-top:2px">Aufteilung: ${txt}</div>`;
        }
        return `<tr style="${active ? '' : 'opacity:0.65;'}border-bottom:1px solid #f1f5f9">
            <td style="padding:10px 14px">
                <div style="font-family:ui-monospace,Menlo,Consolas,monospace;font-weight:600">${formatIbanDisplay(b.iban)}${hauptbankBadge}</div>
                <div style="font-size:11px;color:#64748b">${b.bankName ?? ''}${b.bic ? ' · ' + b.bic : ''}</div>
                ${inhaber}
                ${ref}
                ${aufteilungInfo}
            </td>
            <td style="padding:10px 14px;font-size:12px">${fmtDate(b.validFrom)} – ${bisTxt}</td>
            <td style="padding:10px 14px;text-align:center">${status}</td>
            <td style="padding:10px 14px;color:#94a3b8;font-size:12px">${b.bemerkung ?? ''}</td>
            <td style="padding:10px 14px;text-align:right;white-space:nowrap">
                <button class="btn-stamp-edit" onclick='openBankAccountModal(${JSON.stringify(b).replace(/'/g,"&#39;")})'>✎</button>
                <button class="btn-stamp-del"  onclick="deleteBankAccount(${b.id})">✕</button>
            </td>
        </tr>`;
    }).join('');
    el.innerHTML = `<table style="width:100%;font-size:13px;border-collapse:collapse;margin-top:4px">
        <thead><tr style="color:#64748b;text-align:left;border-bottom:1px solid #e2e8f0">
            <th style="padding:8px 14px;font-weight:600">IBAN / Bank</th>
            <th style="padding:8px 14px;font-weight:600">Gültigkeit</th>
            <th style="padding:8px 14px;font-weight:600;text-align:center">Status</th>
            <th style="padding:8px 14px;font-weight:600">Bemerkung</th>
            <th></th>
        </tr></thead>
        <tbody>${rows}</tbody>
    </table>`;
}

function formatIbanDisplay(iban) {
    if (!iban) return '';
    const clean = iban.replace(/\s+/g, '');
    return clean.replace(/(.{4})/g, '$1 ').trim();
}

function openBankAccountModal(existing) {
    const modal = document.getElementById('bankAccountModal');
    if (!modal) return;
    modal.style.display = 'flex';
    modal.dataset.editId = existing?.id ?? '';
    document.getElementById('baModalTitle').textContent = existing ? 'Bankverbindung bearbeiten' : 'Bankverbindung erfassen';

    const today = new Date().toISOString().slice(0, 10);
    document.getElementById('baIban').value     = existing?.iban ?? '';
    document.getElementById('baBic').value      = existing?.bic ?? '';
    document.getElementById('baBankName').value = existing?.bankName ?? '';
    document.getElementById('baKontoinhaber').value = existing?.kontoinhaber ?? '';
    document.getElementById('baZahlungsreferenz').value = existing?.zahlungsreferenz ?? '';
    document.getElementById('baBemerkung').value = existing?.bemerkung ?? '';
    document.getElementById('baValidFrom').value = existing?.validFrom ?? today;
    document.getElementById('baValidTo').value   = existing?.validTo ?? '';
    document.getElementById('baIsHauptbank').checked = existing?.isHauptbank ?? true;
    document.getElementById('baAufteilungTyp').value = existing?.aufteilungTyp ?? 'VOLL';
    document.getElementById('baAufteilungWert').value = existing?.aufteilungWert ?? '';
    onAufteilungTypChange();

    // Initiales Live-Feedback falls IBAN schon gefüllt
    validateIbanFieldMa(document.getElementById('baIban'));
}

function onAufteilungTypChange() {
    const typ = document.getElementById('baAufteilungTyp').value;
    const wertEl   = document.getElementById('baAufteilungWert');
    const labelEl  = document.getElementById('baAufteilungWertLabel');
    const hintEl   = document.getElementById('baAufteilungHint');
    if (typ === 'VOLL') {
        wertEl.disabled = true;
        wertEl.value = '';
        labelEl.textContent = '—';
        if (hintEl) hintEl.textContent = 'Dieses Konto bekommt den gesamten Rest-Nettolohn.';
    } else {
        wertEl.disabled = false;
        if (typ === 'PROZENT') {
            labelEl.textContent = 'Prozent';
            if (hintEl) hintEl.textContent = 'Prozentualer Anteil vom Bruttolohn, der auf dieses Konto geht.';
        } else if (typ === 'FIXBETRAG') {
            labelEl.textContent = 'CHF';
            if (hintEl) hintEl.textContent = 'Fixer CHF-Betrag, der auf dieses Konto geht. Rest wird auf die Hauptbank überwiesen.';
        } else if (typ === 'NETTO_ABZUEGLICH') {
            labelEl.textContent = 'CHF';
            if (hintEl) hintEl.textContent = 'Nettolohn minus dieser CHF-Betrag — z.B. "Lohn minus 500 CHF fürs Sparkonto".';
        }
    }
}

function closeBankAccountModal() {
    const modal = document.getElementById('bankAccountModal');
    if (modal) { modal.style.display = 'none'; modal.dataset.editId = ''; }
}

// MA-Variante der IBAN-Validierung: nutzt validateIban() aus admin-settings.js
// und füllt BIC/Bankname im Bank-Modal.
async function validateIbanFieldMa(inputEl) {
    const hint = document.getElementById('baIbanHint');
    if (!hint) return;
    const val = inputEl.value.trim();
    if (!val) { hint.textContent = ''; hint.style.color = ''; inputEl.style.borderColor = ''; return; }
    // validateIban() ist global aus admin-settings.js
    const r = (typeof validateIban === 'function') ? validateIban(val, 'IBAN') : { valid: true };
    if (r.valid) {
        hint.textContent = `✓ Gültige IBAN${r.country ? ' (' + r.country + ')' : ''}`;
        hint.style.color = '#16a34a';
        inputEl.style.borderColor = '#86efac';
        if (r.country === 'CH' || r.country === 'LI') {
            try {
                const res = await fetch(`/api/banks/lookup?iban=${encodeURIComponent(val)}`, { headers: ah() });
                if (res.ok) {
                    const b = await res.json();
                    hint.textContent += ` — ${b.name}${b.ort ? ', ' + b.ort : ''}`;
                    const bicEl  = document.getElementById('baBic');
                    const nameEl = document.getElementById('baBankName');
                    if (bicEl  && !bicEl.value.trim()  && b.bic)  bicEl.value  = b.bic;
                    if (nameEl && !nameEl.value.trim() && b.name) nameEl.value = b.name;
                }
            } catch {}
        }
    } else {
        hint.textContent = '✗ ' + r.error;
        hint.style.color = '#dc2626';
        inputEl.style.borderColor = '#fca5a5';
    }
}

async function saveBankAccount() {
    const modal = document.getElementById('bankAccountModal');
    const editId = modal?.dataset.editId;
    const iban   = document.getElementById('baIban').value.trim();
    const from   = document.getElementById('baValidFrom').value;
    const to     = document.getElementById('baValidTo').value;
    if (!iban) { alert('IBAN ist erforderlich.'); return; }
    if (!from) { alert('"Gültig ab"-Datum ist erforderlich.'); return; }
    if (to && to < from) { alert('"Gültig bis" muss nach "Gültig ab" liegen.'); return; }

    // IBAN-Validierung vor dem Speichern (mit Confirm falls ungültig)
    if (typeof validateIban === 'function') {
        const r = validateIban(iban, 'IBAN');
        if (!r.valid && !confirm(`Die IBAN scheint ungültig:\n${r.error}\n\nTrotzdem speichern?`)) return;
    }

    const aufteilungTyp  = document.getElementById('baAufteilungTyp').value;
    const aufteilungWertRaw = document.getElementById('baAufteilungWert').value;
    const aufteilungWert = (aufteilungTyp !== 'VOLL' && aufteilungWertRaw) ? parseFloat(aufteilungWertRaw) : null;
    if (aufteilungTyp !== 'VOLL' && (aufteilungWert === null || !(aufteilungWert > 0))) {
        alert('Bei der gewählten Aufteilung muss ein Wert > 0 angegeben werden.'); return;
    }
    if (aufteilungTyp === 'PROZENT' && aufteilungWert > 100) {
        alert('Prozent-Wert darf max. 100 sein.'); return;
    }

    const body = {
        employeeId:       selectedEmployeeId,
        iban,
        bic:              document.getElementById('baBic').value.trim() || null,
        bankName:         document.getElementById('baBankName').value.trim() || null,
        kontoinhaber:     document.getElementById('baKontoinhaber').value.trim() || null,
        zahlungsreferenz: document.getElementById('baZahlungsreferenz').value.trim() || null,
        bemerkung:        document.getElementById('baBemerkung').value.trim() || null,
        isHauptbank:      document.getElementById('baIsHauptbank').checked,
        aufteilungTyp,
        aufteilungWert,
        validFrom:        from,
        validTo:          to || null
    };
    try {
        const url    = editId ? `/api/employee-bank-accounts/${editId}` : '/api/employee-bank-accounts';
        const method = editId ? 'PUT' : 'POST';
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            let msg = 'Fehler beim Speichern.';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch {}
            alert(msg);
            return;
        }
        closeBankAccountModal();
        loadBankAccountsTab(selectedEmployeeId);
    } catch(e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function deleteBankAccount(id) {
    if (!confirm('Bankverbindung wirklich löschen?')) return;
    try {
        const res = await fetch(`/api/employee-bank-accounts/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadBankAccountsTab(selectedEmployeeId);
    } catch(e) { alert('Verbindungsfehler: ' + e.message); }
}