// ══════════════════════════════════════════════════════════════════════
// branches-detail.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════
// FILIALEN
// ══════════════════════════════════════════════
// ══════════════════════════════════════════════
// FILIALEN – Liste + Detail-Panel
// ══════════════════════════════════════════════
let selectedBranch = null;

function loadFilialen() {
    // „+ Neue Filiale" nur für admin (POST /api/companyprofiles ist admin-only).
    const btn = document.getElementById('btnNewBranch');
    if (btn) {
        const isAdmin = (typeof currentUser !== 'undefined' && currentUser
            && (currentUser.role === 'admin' || currentUser.isSuperAdmin));
        btn.style.display = isAdmin ? 'block' : 'none';
    }
    renderFilialenList(allBranches);
}

// ── Neue Filiale anlegen (Walter-Vorgabe 22.06.2026) ──────────────────────
function openNewBranchModal() {
    if (document.getElementById('newBranchModal')) return;
    const html = `
    <div id="newBranchModal" style="position:fixed;inset:0;background:rgba(15,23,42,.55);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px"
         onclick="if(event.target===this)closeNewBranchModal()">
      <div class="ma-modal-box narrow">
        <div class="ma-modal-head">
            <div class="ma-modal-title">Neue Filiale</div>
            <button class="ma-modal-close" onclick="closeNewBranchModal()">✕</button>
        </div>
        <div class="ma-modal-body">
            <div class="emp-section-title">Stammdaten</div>
            <div class="ma-grid cols-2">
                <div class="ma-field">
                    <div class="ma-field-label">Firmenname *</div>
                    <input type="text" id="nbCompanyName" class="ma-input" value="Schaub Restaurant GmbH">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Filialname *</div>
                    <input type="text" id="nbBranchName" class="ma-input" placeholder="z.B. Filiale Aarau">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Restaurant-Code *</div>
                    <input type="text" id="nbRestaurantCode" class="ma-input" maxlength="3" placeholder="z.B. 058">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Land</div>
                    <input type="text" id="nbCountry" class="ma-input" value="CH" maxlength="2">
                </div>
            </div>

            <div class="emp-section-title">Adresse</div>
            <div class="ma-grid cols-2">
                <div class="ma-field">
                    <div class="ma-field-label">Strasse</div>
                    <input type="text" id="nbStreet" class="ma-input" placeholder="z.B. Bahnhofstrasse">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Hausnummer</div>
                    <input type="text" id="nbHouseNumber" class="ma-input" placeholder="z.B. 20">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">PLZ</div>
                    <input type="text" id="nbZip" class="ma-input" maxlength="5" placeholder="z.B. 5000">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Ort</div>
                    <input type="text" id="nbCity" class="ma-input" placeholder="z.B. Aarau">
                </div>
            </div>

            <div class="emp-section-title">Kontakt &amp; Arbeitszeit</div>
            <div class="ma-grid cols-2">
                <div class="ma-field">
                    <div class="ma-field-label">Telefon</div>
                    <input type="text" id="nbPhone" class="ma-input" placeholder="z.B. 062 000 00 00">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">E-Mail</div>
                    <input type="email" id="nbEmail" class="ma-input" placeholder="z.B. aarau@schaub.ch">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Wöchentl. Normalarbeitszeit (h)</div>
                    <input type="number" step="0.1" id="nbWeekly" class="ma-input" value="43.5">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Ferienwochen</div>
                    <input type="number" step="1" id="nbVacWeeks" class="ma-input" value="5">
                </div>
            </div>
        </div>
        <div class="ma-modal-foot">
            <button class="btn btn-outline" onclick="closeNewBranchModal()">Abbrechen</button>
            <button class="btn btn-primary" id="nbSaveBtn" onclick="submitNewBranch()">Filiale anlegen</button>
        </div>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

function closeNewBranchModal() {
    document.getElementById('newBranchModal')?.remove();
}

async function submitNewBranch() {
    const val = (id) => (document.getElementById(id)?.value || '').trim();
    const companyName    = val('nbCompanyName');
    const branchName     = val('nbBranchName');
    const restaurantCode = val('nbRestaurantCode');
    if (!companyName || !branchName || !restaurantCode) {
        alert('Bitte Firmenname, Filialname und Restaurant-Code ausfüllen.');
        return;
    }
    const weekly   = parseFloat(val('nbWeekly'));
    const vacWeeks = parseInt(val('nbVacWeeks'), 10);
    const body = {
        companyName,
        branchName,
        restaurantCode,
        street:      val('nbStreet')      || null,
        houseNumber: val('nbHouseNumber') || null,
        zipCode:     val('nbZip')         || null,
        city:        val('nbCity')        || null,
        country:     val('nbCountry')     || 'CH',
        phone:       val('nbPhone')       || null,
        email:       val('nbEmail')       || null,
        normalWeeklyHours:   isNaN(weekly)   ? 43.5 : weekly,
        defaultVacationWeeks: isNaN(vacWeeks) ? 5    : vacWeeks
    };
    const btn = document.getElementById('nbSaveBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Wird angelegt…'; }
    try {
        const res = await fetch('/api/companyprofiles', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            let msg = `Fehler (${res.status})`;
            try { const j = await res.json(); if (j?.message) msg = j.message; } catch (_) {}
            alert('Filiale konnte nicht angelegt werden.\n' + msg);
            if (btn) { btn.disabled = false; btn.textContent = 'Filiale anlegen'; }
            return;
        }
        const created = await res.json();
        closeNewBranchModal();
        // Liste global neu laden, neue Filiale auswählen.
        if (typeof loadAllBranches === 'function') await loadAllBranches();
        loadFilialen();
        if (created?.id) selectFiliale(created.id);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        if (btn) { btn.disabled = false; btn.textContent = 'Filiale anlegen'; }
    }
}

function renderFilialenList(branches) {
    const listEl = document.getElementById('filialenList');
    if (!listEl) return;
    if (!branches || !branches.length) {
        listEl.innerHTML = '<div style="padding:20px;text-align:center;color:#94a3b8">Keine Filialen</div>';
        return;
    }
    listEl.innerHTML = branches.map(b => {
        const name = b.branchName || b.companyName || '–';
        const code = b.restaurantCode || '';
        const city = b.city || '';
        const isActive = selectedBranch && selectedBranch.id === b.id;
        return `<div class="emp-list-item ${isActive ? 'active' : ''}" onclick="selectFiliale(${b.id})">
            <div style="display:flex;align-items:center;gap:10px;padding:10px 14px">
                <div style="width:34px;height:34px;border-radius:8px;background:#e2e8f0;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:11px;color:#475569;flex-shrink:0">${code}</div>
                <div style="flex:1;min-width:0">
                    <div class="emp-list-name" style="white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${name}</div>
                    <div class="emp-list-nr">${city}</div>
                </div>
            </div>
        </div>`;
    }).join('');
}

function filterFilialenList() {
    const q = (document.getElementById('filialenSearch')?.value || '').toLowerCase();
    const filtered = q ? allBranches.filter(b => {
        const name = (b.branchName || b.companyName || '').toLowerCase();
        const code = (b.restaurantCode || '').toLowerCase();
        return name.includes(q) || code.includes(q);
    }) : allBranches;
    renderFilialenList(filtered);
}

async function selectFiliale(id) {
    selectedBranch = allBranches.find(b => b.id === id) || null;
    renderFilialenList(allBranches);
    if (!selectedBranch) return;
    renderFilialenDetail(selectedBranch);
    await loadSignatories(id);
}

function fField(label, value) {
    const v = (value !== null && value !== undefined && value !== '') ? value : '–';
    return `<div class="emp-field">
        <div class="emp-field-label">${label}</div>
        <div class="emp-field-value" style="font-size:14px;color:#0f172a">${v}</div>
    </div>`;
}

// Probezeit-Anzeige: 14 = 14 Tage, 1/2/3 = Monate (so in der DB gespeichert).
function probationLabel(v) {
    if (v == null || v === '') return '–';
    const n = Number(v);
    if (n === 14) return '14 Tage';
    if (n === 1)  return '1 Monat';
    if (n >= 2)   return `${n} Monate`;
    return '–';
}

function renderFilialenDetail(b) {
    const panel = document.getElementById('filialenDetailPanel');
    if (!panel) return;
    const name = b.branchName || b.companyName || '–';
    const nightStart = b.nightStartTime || '00:00';
    const nightEnd   = b.nightEndTime   || '07:00';
    panel.innerHTML = `
    <div class="emp-detail-header">
        <div style="display:flex;align-items:flex-start;justify-content:space-between;gap:12px">
            <div>
                <div class="emp-detail-name">${name}</div>
                <div class="emp-detail-meta">Code: ${b.restaurantCode || '–'} &nbsp;·&nbsp; ${[b.zipCode, (typeof stripCityCantonSuffix === 'function' ? stripCityCantonSuffix(b.city) : b.city)].filter(Boolean).join(' ') || '–'}</div>
            </div>
        </div>
        <div class="emp-detail-tabs" style="align-items:center">
            <div class="emp-tab active" data-ftab="f-stamm"         onclick="switchFilialenTab('f-stamm')">Stammdaten</div>
            <div class="emp-tab"        data-ftab="f-unterzeichner"  onclick="switchFilialenTab('f-unterzeichner')">Unterzeichner</div>
            <div class="emp-tab"        data-ftab="f-abzuege"        onclick="switchFilialenTab('f-abzuege')">Abzüge</div>
            <div class="emp-tab"        data-ftab="f-einstellungen"  onclick="switchFilialenTab('f-einstellungen')">Einstellungen</div>
            <div class="emp-tab"        data-ftab="f-empf"           onclick="switchFilialenTab('f-empf')">Empfänger</div>
            ${cdokCanSee() ? `<div class="emp-tab" data-ftab="f-doks" onclick="switchFilialenTab('f-doks')">Dokumente</div>` : ''}
            <!-- Aktions-Buttons des Einstellungen-Tabs sitzen in der Tab-Leiste
                 (nicht-scrollender Kopfbereich) — bleiben so immer sichtbar.
                 Nur eingeblendet wenn der Einstellungen-Tab aktiv ist
                 (switchFilialenTab steuert die Sichtbarkeit). -->
            <div id="filEinstellungenActions" style="margin-left:auto;display:none;gap:8px">
                <button class="btn btn-outline" style="font-size:12px;padding:4px 14px" onclick="copyEinstellungenToAll(${b.id})">→ Auf alle Filialen übertragen</button>
                <button class="btn btn-primary" id="einSaveBtn-${b.id}" style="font-size:12px;padding:4px 14px;background:#16a34a" onclick="saveEinstellungen(${b.id})">💾 Speichern</button>
            </div>
        </div>
    </div>
    <div class="emp-detail-body">
        <!-- TAB: Stammdaten -->
        <div class="emp-tab-content active" id="fil-tab-f-stamm">
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                Stammdaten
                <button class="btn btn-primary" style="font-size:12px;padding:4px 14px" onclick="openAlvDatenModal(${b.id})">✎ Bearbeiten</button>
            </div>
            <div class="emp-field-grid">
                ${fField('Firma',           b.companyName)}
                ${fField('Filiale',         b.branchName)}
                ${fField('Code',            b.restaurantCode)}
                ${fField('Strasse',         [b.street, b.houseNumber].filter(Boolean).join(' '))}
                ${fField('PLZ / Ort',       [b.zipCode, (typeof stripCityCantonSuffix === 'function' ? stripCityCantonSuffix(b.city) : b.city)].filter(Boolean).join(' '))}
                ${fField('Standort-Kanton', b.kantonCode ? b.kantonCode : '<span style="color:#dc2626">⚠ nicht gesetzt</span>')}
                ${fField('Telefon',         b.phone)}
                ${fField('E-Mail',          b.email)}
                ${fField('Arbeitsort (Vertrag)', b.workLocation || `<span style="color:#8b8b8b">– (Fallback: ${b.city || 'Ort'})</span>`)}
                ${fField('BUR-Nummer',      b.burNummer)}
                ${fField('UID-Nummer',      b.uidNummer)}
                ${fField('Branchen-Code',   b.branchenCode)}
                ${fField('AHV-Kasse',       b.ahvKasse)}
                ${fField('BVG-Versicherer', b.bvgVersicherer)}
                ${fField('GAV',             b.istGav ? (b.gavName || 'Ja') : 'Nein')}
                ${fField('Lohnausweis Box F', b.lohnausweisBoxFFreierTransport ? '✓ Werks-Transport gratis' : '✗ nicht angekreuzt')}
                ${fField('Lohnausweis Box G', b.lohnausweisBoxGKantineGratis    ? '✓ Kantine gratis'       : '✗ nicht angekreuzt')}
                ${fField('Ziffer 2.1 Verpflegung/Monat',
                    (b.lohnausweisPos21VerpflegungMonat == null || b.lohnausweisPos21VerpflegungMonat === 0)
                        ? 'CHF 0 (kein Pauschalbetrag)'
                        : `CHF ${Number(b.lohnausweisPos21VerpflegungMonat).toFixed(2)}`)}
                ${fField('Probezeit', probationLabel(b.probationMonths))}
            </div>

            <!-- ── Bankverbindungen der Filiale (Auftraggeber-Konto fürs DTA) ── -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between;margin-top:18px">
                <div>
                    Bankverbindungen
                    <span style="font-size:11px;color:#94a3b8;font-weight:400;margin-left:6px">(Auftraggeber-Konto für DTA / Lohnlauf — bei Bankenwechsel: alten Eintrag mit „Gültig bis" abschliessen, neuen anlegen)</span>
                </div>
                <button class="btn btn-primary" style="font-size:12px;padding:4px 14px" onclick="openCompanyBankModal(${b.id}, null)">+ Hinzufügen</button>
            </div>
            <div id="companyBankList-${b.id}"><div style="color:#94a3b8;padding:14px;text-align:center;font-size:12px">Wird geladen…</div></div>

            <!-- ── SSL-Nummern Quellensteuer (eine pro Kanton) ───────────── -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between;margin-top:18px">
                <div>
                    SSL-Nummern Quellensteuer
                    <span style="font-size:11px;color:#94a3b8;font-weight:400;margin-left:6px">(eine pro Kanton, in dem diese Filiale QST-pflichtige MA beschäftigt)</span>
                </div>
                <button class="btn btn-primary" style="font-size:12px;padding:4px 14px" onclick="openSslModal(${b.id}, null)">+ Hinzufügen</button>
            </div>
            <div id="sslList-${b.id}"><div style="color:#94a3b8;padding:14px;text-align:center;font-size:12px">Wird geladen…</div></div>
        </div>

        <!-- TAB: Unterzeichner -->
        <div class="emp-tab-content" id="fil-tab-f-unterzeichner">
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                Unterzeichner
                <button class="btn btn-primary" style="font-size:12px;padding:4px 14px" onclick="openSignatoryForm(null)">+ Hinzufügen</button>
            </div>
            <div id="signatoryList"><div style="color:#94a3b8;padding:20px;text-align:center">Wird geladen...</div></div>
            <div id="signatoryForm" style="display:none;margin-top:20px;padding:16px;background:#f8fafc;border-radius:10px;border:1px solid #e2e8f0">
                <div style="font-weight:600;margin-bottom:14px" id="signatoryFormTitle">Unterzeichner hinzufügen</div>
                <div class="emp-field-grid">
                    <div class="emp-field" style="grid-column:1/-1">
                        <div class="emp-field-label">Benutzer auswählen</div>
                        <select id="sig-userId" class="ef-input">
                            <option value="">– Benutzer wählen –</option>
                        </select>
                    </div>
                    <div class="emp-field"><div class="emp-field-label">Funktion</div><input id="sig-function" class="ef-input" placeholder="z.B. Geschäftsführer/in"></div>
                    <div class="emp-field"><div class="emp-field-label">Rolle</div>
                        <select id="sig-role" class="ef-input">
                            <option value="">– keine –</option>
                            <option value="GESCHAEFTSFUEHRER">Geschäftsführer/in</option>
                            <option value="HR_VERANTWORTLICH">HR-Verantwortlich</option>
                            <option value="REGIONALLEITER">Regionalleiter/in</option>
                            <option value="BUCHHALTUNG">Buchhaltung</option>
                            <option value="SONSTIGES">Sonstiges</option>
                        </select>
                    </div>
                    <div class="emp-field"><div class="emp-field-label">Standard</div>
                        <label style="display:flex;align-items:center;gap:8px;cursor:pointer">
                            <input type="checkbox" id="sig-isDefault" style="width:16px;height:16px">
                            <span style="font-size:13px;color:#64748b">Allgemeiner Unterzeichner</span>
                        </label>
                    </div>
                </div>
                <div style="display:flex;gap:8px;margin-top:14px">
                    <button class="btn btn-primary" style="font-size:13px;padding:6px 18px" onclick="saveSignatory()">Speichern</button>
                    <button class="btn btn-secondary" style="font-size:13px;padding:6px 14px" onclick="closeSignatoryForm()">Abbrechen</button>
                </div>
            </div>
        </div>

        <!-- TAB: Abzüge -->
        <div class="emp-tab-content" id="fil-tab-f-abzuege">
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                Abzüge
                <button class="btn btn-primary" style="font-size:12px;padding:4px 14px" onclick="openDeductionDrawer(${b.id}, encodeURI('${name.replace(/'/g,"\'")}'))">✎ Bearbeiten</button>
            </div>
            <div style="color:#64748b;font-size:13px;padding:20px 0">Klicke auf "Bearbeiten", um die Abzüge zu verwalten.</div>
        </div>

        <!-- TAB: Einstellungen — Felder direkt editierbar in der Maske
             (Walter-Vorgabe 14.05.2026: keine Popup-Modals mehr). Ein
             „Speichern"-Button sichert alle Gruppen in einem Rutsch.
             Periodenregel wurde komplett entfernt (Walter-Vorgabe 16.05.2026,
             Akonto-Lohn-Modell): die Lohnperiode ist jetzt immer der
             Kalendermonat — kein UI, kein Modal, kein Field-Loader. -->
        <div class="emp-tab-content" id="fil-tab-f-einstellungen">
            <!-- Kein „Einstellungen"-Titel mehr (steht schon im Tab) und keine
                 Buttons hier — die Aktions-Buttons sitzen oben in der
                 Tab-Leiste (Walter-Vorgabe 15.05.2026). -->
            <div class="ein-group-title" style="margin-top:0">Arbeitszeit</div>
            <div class="emp-field-grid">
                <div class="emp-field"><div class="emp-field-label">Nacht Beginn</div>
                    <div class="emp-field-value"><input type="time" id="einNightStart" class="ef-input" value="${nightStart}"></div></div>
                <div class="emp-field"><div class="emp-field-label">Nacht Ende</div>
                    <div class="emp-field-value"><input type="time" id="einNightEnd" class="ef-input" value="${nightEnd}"></div></div>
                ${fField('Normale Wochenstunden', b.normalWeeklyHours)}
                <div class="emp-field"><div class="emp-field-label">Max. Stunden / Woche <span style="font-weight:400;text-transform:none;color:#94a3b8;letter-spacing:0">(Warnung im Stempel-Tab)</span></div>
                    <div class="emp-field-value"><input type="number" id="einMaxWeeklyHours" class="ef-input" min="0" max="168" step="0.5" placeholder="keine Grenze" value="${b.maxWeeklyHours != null ? Number(b.maxWeeklyHours) : ''}"></div></div>
            </div>

            <div class="ein-group-title">Ferien- &amp; Feiertags-Vorgaben <span style="font-weight:400;text-transform:none;color:#94a3b8;letter-spacing:0">(% nur Anzeige · Alter editierbar)</span></div>
            <div class="emp-field-grid">
                ${fField('Ferien % (5 Wochen)', b.defaultVacationPercent5Weeks)}
                ${fField('Ferien % (6 Wochen)', b.defaultVacationPercent6Weeks)}
                <div class="emp-field"><div class="emp-field-label">6 Wochen ab Alter</div>
                    <div class="emp-field-value"><input type="number" id="einVacationSixWeeksFromAge" class="ef-input" min="0" max="100" step="1" value="${b.vacationSixWeeksFromAge ?? 50}"></div></div>
                ${fField('Feiertag %',          b.defaultHolidayPercent)}
            </div>

            <div class="ein-group-title">13. Monatslohn</div>
            <div style="display:flex;gap:32px;align-items:flex-start;margin-bottom:5px;flex-wrap:wrap">
                <div class="emp-field" style="flex:0 0 130px;margin-bottom:0">
                    <!-- unsichtbarer Label-Spacer, damit der Input auf gleicher Höhe wie
                         die Monatskreuze rechts liegt (Walter-Vorgabe 06.06.2026) -->
                    <div class="emp-field-label" style="visibility:hidden">.</div>
                    <div class="emp-field-value" style="gap:6px">
                        <input type="number" id="einDefaultThirteenthPct" class="ef-input" min="0" max="100" step="0.01" placeholder="8.33" value="${b.defaultThirteenthSalaryPercent != null ? Number(b.defaultThirteenthSalaryPercent) : ''}" style="flex:1;min-width:0">
                        <span style="font-size:13px;color:#475569;font-weight:600">%</span>
                    </div>
                </div>
                <div class="emp-field" style="flex:1 1 auto;margin-bottom:0;min-width:300px">
                    <div class="emp-field-label">Auszahlungsmonate</div>
                    <div id="einTpGrid" style="display:flex;flex-wrap:wrap;gap:4px;margin-top:3px"></div>
                    <div id="einTpHint" style="font-size:11.5px;margin-top:3px"></div>
                </div>
            </div>
            <div class="emp-field-grid">
                <div class="emp-field"><div class="emp-field-label">Ferien-Geld Dezember (FLEX/MTP)</div>
                    <div class="emp-field-value"><select id="einAutoFerienGeld" class="ef-input">
                        <option value="true"  ${b.autoFerienGeldAuszahlungDezember !== false ? 'selected' : ''}>Auto-Auszahlung Dezember</option>
                        <option value="false" ${b.autoFerienGeldAuszahlungDezember === false ? 'selected' : ''}>Manuell</option>
                    </select></div></div>
            </div>

            <div class="ein-group-title">Karenz (Krank / Unfall)</div>
            <div class="emp-field-grid-3">
                <div class="emp-field"><div class="emp-field-label">Karenzjahr-Basis</div>
                    <div class="emp-field-value"><select id="einKarenzBasis" class="ef-input">
                        <option value="ARBEITSJAHR"  ${(b.karenzjahrBasis || 'ARBEITSJAHR') === 'ARBEITSJAHR' ? 'selected' : ''}>Arbeitsjahr (ab MA-Eintritt)</option>
                        <option value="KALENDERJAHR" ${b.karenzjahrBasis === 'KALENDERJAHR' ? 'selected' : ''}>Kalenderjahr (01.01.–31.12.)</option>
                    </select></div></div>
                <div class="emp-field"><div class="emp-field-label">Karenz-Tage Krank</div>
                    <div class="emp-field-value"><input type="number" id="einKarenzKrank" class="ef-input" min="0" max="365" value="${b.karenzTageMax ?? 14}"></div></div>
                <div class="emp-field"><div class="emp-field-label">Karenz-Tage Unfall</div>
                    <div class="emp-field-value"><input type="number" id="einKarenzUnfall" class="ef-input" min="0" max="365" value="${b.karenzTageMaxUnfall ?? 2}"></div></div>
                <div class="emp-field"><div class="emp-field-label">BVG-Wartefrist (Monate)</div>
                    <div class="emp-field-value"><input type="number" id="einBvgWartefrist" class="ef-input" min="0" max="24" value="${b.bvgWartefristMonate ?? 3}"></div></div>
            </div>

            <div class="ein-group-title">L-GAV-Vollzugsbeitrag</div>
            <div class="emp-field-grid-3">
                <div class="emp-field"><div class="emp-field-label">Status</div>
                    <div class="emp-field-value"><select id="einLgavAktiv" class="ef-input">
                        <option value="true"  ${b.lgavAktiv !== false ? 'selected' : ''}>Aktiv</option>
                        <option value="false" ${b.lgavAktiv === false ? 'selected' : ''}>Deaktiviert</option>
                    </select></div></div>
                <div class="emp-field"><div class="emp-field-label">Abzugs-Monat</div>
                    <div class="emp-field-value"><select id="einLgavMonat" class="ef-input">
                        ${LGAV_MONTH_LABELS.map((m, i) => `<option value="${i + 1}" ${(b.lgavTriggerMonat ?? 1) === (i + 1) ? 'selected' : ''}>${m}</option>`).join('')}
                    </select></div></div>
                <div class="emp-field"><div class="emp-field-label">Voller Beitrag (CHF)</div>
                    <div class="emp-field-value"><input type="number" id="einLgavVoll" class="ef-input" min="0" step="0.05" value="${Number(b.lgavBeitragVoll ?? 99).toFixed(2)}"></div></div>
                <div class="emp-field"><div class="emp-field-label">Reduzierter Beitrag (CHF)</div>
                    <div class="emp-field-value"><input type="number" id="einLgavRed" class="ef-input" min="0" step="0.05" value="${Number(b.lgavBeitragReduziert ?? 49.5).toFixed(2)}"></div></div>
            </div>

            <div class="ein-group-title">Akonto-Lohn</div>
            <div class="emp-field-grid">
                <div class="emp-field"><div class="emp-field-label">Akonto-% FIX</div>
                    <div class="emp-field-value"><input type="number" id="einAkontoProzent" class="ef-input" min="0" max="100" step="1" value="${Number(b.akontoProzentFix ?? 80).toFixed(0)}"></div></div>
                <div class="emp-field"><div class="emp-field-label">Akonto-% FIX-M</div>
                    <div class="emp-field-value"><input type="number" id="einAkontoProzentFixM" class="ef-input" min="0" max="100" step="1" value="${Number(b.akontoProzentFixM ?? 90).toFixed(0)}"></div></div>
                <div class="emp-field"><div class="emp-field-label">Akonto-% FLEX / MTP</div>
                    <div class="emp-field-value"><input type="number" id="einAkontoProzentHourly" class="ef-input" min="0" max="100" step="1" value="${Number(b.akontoProzentHourly ?? 100).toFixed(0)}"></div></div>
            </div>
            <div style="margin-top:6px;padding:8px 12px;background:#fffbeb;border:1px solid #fde68a;border-radius:9px;font-size:11.5px;color:#78350f;line-height:1.45">
                <b>Akonto-Regeln (Walter 16.05.2026, fix):</b><br>
                ① Kein Akonto wenn Vertragsende ≤ Periodenende.<br>
                ② Kein Akonto bei Krankheit / Unfall / Mutterschaft am Stichtag.<br>
                ③ FIX: <b>Akonto-% (FIX) × Definitiv-Auszahlung</b>, abgerundet auf CHF 10.<br>
                ④ FIX-M: <b>Akonto-% (FIX-M) × Definitiv-Auszahlung</b>, abgerundet auf CHF 10.<br>
                ⑤ FLEX / MTP: <b>Akonto-% (FLEX/MTP) × (gestempelte Stunden × Stundenlohn + Ferien-Pott − SV-Abzüge)</b>, abgerundet auf CHF 10.<br>
                <span style="color:#92400e">Ferien-Pott: nur bis Stichtag vollständig abgeschlossene Bezüge — anteilsmässig aus (Vormonats-Saldo + Akkumulation diesen Monat).</span>
            </div>
            <div style="margin-top:6px;padding:8px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:9px">
                <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin-bottom:7px">
                    <span style="font-size:13px;font-weight:600;color:#0f172a">Akonto-Termine</span>
                    <span style="font-size:11.5px;color:#94a3b8">Auszahlungsdatum pro Monat — bei Wochenende/Feiertag von Hand anpassen</span>
                    <span style="flex:1"></span>
                    <label style="font-size:11.5px;color:#64748b">Jahr
                        <select id="einAkontoYear" class="ef-input" style="width:auto;display:inline-block;margin-left:4px" onchange="onAkontoYearChange(${b.id})">
                            ${akontoYearOptions()}
                        </select>
                    </label>
                    <label style="font-size:11.5px;color:#64748b">Standard-Tag
                        <input type="number" id="einAkontoStdTag" class="ef-input" style="width:54px;display:inline-block;margin-left:4px" min="1" max="28" value="23">
                    </label>
                    <button class="btn btn-outline" style="font-size:11.5px;padding:4px 10px" onclick="generateAkontoTermine(${b.id})">↻ Jahr generieren</button>
                    <button class="btn btn-primary" style="font-size:11.5px;padding:4px 12px;background:#16a34a" onclick="saveAkontoTermine(${b.id})">💾 Termine speichern</button>
                </div>
                <div id="einAkontoTermineGrid" style="display:grid;grid-template-columns:repeat(6,1fr);gap:5px 10px"></div>
            </div>

            <div class="ein-group-title">Gemeinde/Kanton Minimum Lohn
                <span style="font-weight:400;text-transform:none;color:#94a3b8;letter-spacing:0">— nur falls Gemeinde/Kanton einen eigenen Mindestlohn vorschreibt; übersteuert den L-GAV nach oben</span></div>
            <div id="bmwBlock"><div style="font-size:12px;color:#94a3b8">Wird geladen…</div></div>

            <div class="ein-group-title">easy@work Auto-Sync
                <span style="font-weight:400;text-transform:none;color:#94a3b8;letter-spacing:0">— automatischer Stempelzeiten-Import dieser Filiale, täglich um 05:00</span></div>
            <div id="eawAutoBlock"><div style="font-size:12px;color:#94a3b8">Wird geladen…</div></div>

            <!-- Periodenregel-Anzeige entfernt (Walter-Vorgabe 15.05.2026):
                 die Lohnperiode ist jetzt immer der Kalendermonat.
                 Der „Auf alle Filialen übertragen"-Button sitzt in der
                 sticky Kopfzeile oben — kein zweiter Button hier unten nötig. -->
        </div>

        <!-- TAB: Lohndatenempfänger (Walter-Vorgabe 06.08.2026, Mirus-Vorbild) —
             zentrale Empfänger-Stammdaten + filial-spezifische Mitglied-/
             Subnummer. Empfänger werden beim Zuordnen direkt miterfasst
             (kein eigener Systemsteuerungs-Punkt). -->
        <div class="emp-tab-content" id="fil-tab-f-empf">
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                Lohndatenempfänger
                <button onclick="cpEmpfOpenModal(null)" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:6px 16px;font-size:12.5px;font-weight:600;cursor:pointer;box-shadow:0 2px 8px rgba(60,55,48,0.18)">+ Hinzufügen</button>
            </div>
            <div style="font-size:12px;color:#8b8b8b;margin:4px 0 12px">Kassen, Versicherungen und Steuerverwaltungen, an die Lohndaten dieser Filiale gemeldet werden. Adresse und Kassennummer sind zentral erfasst (gelten für alle Filialen); Mitglied- und Subnummer gehören zu dieser Filiale.</div>
            <div id="cpEmpfList"><div style="color:#8b8b8b;padding:16px;text-align:center;font-size:12px">Wird geladen…</div></div>
        </div>

        <!-- TAB: Dokumente (Walter-Vorgabe 06.08.2026) — Filial-Dokumente
             (Versicherungspolicen, AHV-Korrespondenz, QST-Unterlagen …).
             Nur sichtbar für admin oder User mit canCompanyDokumente
             (AuthController.Me); das Backend prüft den Zugriff zusätzlich. -->
        ${cdokCanSee() ? `
        <div class="emp-tab-content" id="fil-tab-f-doks">
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                Dokumente
                <button onclick="filDoksOpenUpload()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:6px 16px;font-size:12.5px;font-weight:600;cursor:pointer;box-shadow:0 2px 8px rgba(60,55,48,0.18)">+ Hochladen</button>
            </div>
            <div id="filDoksFilterBar" style="display:flex;gap:6px;flex-wrap:wrap;margin:10px 0 14px"></div>
            <div id="filDoksList"><div style="color:#8b8b8b;padding:16px;text-align:center;font-size:12px">Wird geladen…</div></div>
        </div>` : ''}
    </div>`;
    // SSL-Nummern asynchron nachladen (separater Endpoint)
    loadSslListForBranch(b.id);
    // Filial-Bankverbindungen asynchron nachladen (separate Tabelle mit Historie)
    loadCompanyBankList(b.id);
    // 13.-ML-Monatsraster im Einstellungen-Tab initialisieren.
    einTpInit(b.thirteenthMonthPayoutMonths, b.thirteenthMonthPayoutsPerYear);
    // Akonto-Termine des aktuellen Jahres laden (Akonto-Lohn-Modell).
    loadAkontoTermine(b.id);
    // Kommunalen Mindestlohn der Filiale laden (versioniert).
    bmwInit(b.id);
    // easy@work Auto-Sync-Schalter dieser Filiale laden.
    eawAutoInit(b.id);
    // Aktiven Tab beibehalten (Walter-Vorgabe 15.05.2026): wer in
    // „Einstellungen" steht und links die Filiale wechselt, bleibt in
    // „Einstellungen" — einfach von der neu gewählten Filiale.
    switchFilialenTab(activeFilialenTab);
}

// ── 13.-ML-Auszahlungsmonate als Inline-Widget (Einstellungen-Tab) ──────
// Eigener State + eigene IDs (einTp…) — kollidiert NICHT mit dem alten
// Modal-Widget (tpMonthsGrid), das nur noch als toter Code existiert.
// TP_MONTH_NAMES / tpParseMonths werden weiter unten definiert und sind
// global verfügbar.
let _einTpMonths = [];
function einTpRenderGrid() {
    const grid = document.getElementById('einTpGrid');
    if (!grid) return;
    const sel = new Set(_einTpMonths);
    grid.innerHTML = TP_MONTH_NAMES.map((name, i) => {
        const m  = i + 1;
        const on = sel.has(m);
        return `<label style="display:flex;align-items:center;gap:5px;padding:3px 7px;border:1.5px solid ${on ? 'rgba(63,63,63,.55)' : '#e2e8f0'};border-radius:9px;background:${on ? 'rgba(255,255,255,.85)' : '#f8fafc'};color:${on ? '#3f3f3f' : '#475569'};cursor:pointer;font-size:12px;font-weight:600;user-select:none;box-shadow:${on ? '0 3px 10px rgba(60,55,48,.12)' : 'none'}">
            <input type="checkbox" ${on ? 'checked' : ''} value="${m}" onchange="einTpToggle(${m})" style="margin:0">${name}
        </label>`;
    }).join('');
    const hint = document.getElementById('einTpHint');
    if (hint) {
        if (_einTpMonths.length === 0)       { hint.textContent = '⚠ Keine Auszahlungsmonate gewählt — 13. ML wird nicht ausbezahlt.'; hint.style.color = '#b45309'; }
        else if (_einTpMonths.length === 12) { hint.textContent = '✓ Monatlich — bei jeder Lohnperiode wird der 13. anteilig ausbezahlt.'; hint.style.color = '#15803d'; }
        else                                  { hint.textContent = `✓ ${_einTpMonths.length}× pro Jahr: ${_einTpMonths.map(m => TP_MONTH_NAMES[m - 1]).join(', ')}`; hint.style.color = '#6b7280'; }
    }
}
function einTpToggle(m) {
    _einTpMonths = _einTpMonths.includes(m)
        ? _einTpMonths.filter(x => x !== m)
        : [..._einTpMonths, m].sort((a, b) => a - b);
    einTpRenderGrid();
}
function einTpInit(currentMonths, legacyPerYear) {
    let init = tpParseMonths(currentMonths);
    if (init.length === 0) {
        const v = Number(legacyPerYear ?? 12);
        init = v === 1 ? [12]
             : v === 2 ? [6, 12]
             : v === 4 ? [3, 6, 9, 12]
             : [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
    }
    _einTpMonths = init;
    einTpRenderGrid();
}

// ── Akonto-Termine (Akonto-Lohn) ───────────────────────────────────────
// Akonto-Auszahlungsdatum pro Filiale/Jahr/Monat. Backend:
// /api/akonto-termine (GET) + /api/akonto-termine/save (POST).
const AKONTO_MONTH_NAMES = ['Jan', 'Feb', 'März', 'April', 'Mai', 'Juni',
                            'Juli', 'Aug', 'Sept', 'Okt', 'Nov', 'Dez'];

// <option>-Liste für den Jahr-Selektor: Vorjahr bis +2 Jahre, aktuelles Jahr aktiv.
function akontoYearOptions() {
    const cy = new Date().getFullYear();
    let html = '';
    for (let y = cy - 1; y <= cy + 2; y++) {
        html += `<option value="${y}" ${y === cy ? 'selected' : ''}>${y}</option>`;
    }
    return html;
}

// Default-Termin für einen Monat: Standard-Tag, bei Wochenende auf den
// Freitag davor verschoben. Gibt ISO 'yyyy-MM-dd' im RICHTIGEN Monat zurück.
function akontoDefaultDate(year, month, stdTag) {
    const d = new Date(year, month - 1, stdTag);
    const wd = d.getDay();                          // 0 = So, 6 = Sa
    if (wd === 6) d.setDate(d.getDate() - 1);        // Sa → Fr
    else if (wd === 0) d.setDate(d.getDate() - 2);   // So → Fr
    return d.getFullYear() + '-'
         + String(d.getMonth() + 1).padStart(2, '0') + '-'
         + String(d.getDate()).padStart(2, '0');
}

// Rendert das 12-Monats-Raster mit Datums-Inputs. `dates` = { month: 'yyyy-MM-dd' }.
// Leere Monate werden mit dem Default-Termin DIESES Monats vorbefüllt — so
// öffnet der Datums-Picker immer im richtigen Monat (nicht im aktuellen).
// min/max begrenzen die Auswahl zusätzlich auf den jeweiligen Monat.
function renderAkontoTermineGrid(dates) {
    const grid = document.getElementById('einAkontoTermineGrid');
    if (!grid) return;
    const yearSel = document.getElementById('einAkontoYear');
    const year = yearSel ? parseInt(yearSel.value, 10) : new Date().getFullYear();
    let stdTag = parseInt(document.getElementById('einAkontoStdTag')?.value, 10);
    if (!Number.isFinite(stdTag) || stdTag < 1 || stdTag > 28) stdTag = 23;
    grid.innerHTML = AKONTO_MONTH_NAMES.map((name, i) => {
        const m = i + 1;
        const val = dates[m] || akontoDefaultDate(year, m, stdTag);
        const mm = String(m).padStart(2, '0');
        const lastDay = new Date(year, m, 0).getDate();
        const min = `${year}-${mm}-01`;
        const max = `${year}-${mm}-${String(lastDay).padStart(2, '0')}`;
        return `<div class="emp-field" style="padding:6px 8px">
            <div class="emp-field-label">${name}</div>
            <div class="emp-field-value"><input type="date" id="einAkontoTermin-${m}" class="ef-input"
                   value="${val}" min="${min}" max="${max}"></div>
        </div>`;
    }).join('');
}

async function loadAkontoTermine(branchId) {
    const yearSel = document.getElementById('einAkontoYear');
    const year = yearSel ? parseInt(yearSel.value, 10) : new Date().getFullYear();
    try {
        const r = await fetch(`/api/akonto-termine?companyProfileId=${branchId}&year=${year}`, { headers: ah() });
        const list = r.ok ? await r.json() : [];
        const dates = {};
        for (const t of list) dates[t.month] = t.payoutDate;
        renderAkontoTermineGrid(dates);
    } catch {
        renderAkontoTermineGrid({});
    }
}

// Jahr-Wechsel im Selektor → Termine des neuen Jahres laden.
function onAkontoYearChange(branchId) {
    loadAkontoTermine(branchId);
}

// Setzt alle 12 Datumsfelder auf den Default zurück (Standard-Tag, bei
// Wochenende auf den Freitag davor). Speichert noch nichts — Walter kann
// die Ausreisser (Feiertage etc.) danach von Hand korrigieren.
function generateAkontoTermine(branchId) {
    const yearSel = document.getElementById('einAkontoYear');
    const year = yearSel ? parseInt(yearSel.value, 10) : new Date().getFullYear();
    let stdTag = parseInt(document.getElementById('einAkontoStdTag')?.value, 10);
    if (!Number.isFinite(stdTag) || stdTag < 1 || stdTag > 28) stdTag = 23;
    for (let m = 1; m <= 12; m++) {
        const inp = document.getElementById('einAkontoTermin-' + m);
        if (inp) inp.value = akontoDefaultDate(year, m, stdTag);
    }
}

async function saveAkontoTermine(branchId) {
    const yearSel = document.getElementById('einAkontoYear');
    const year = yearSel ? parseInt(yearSel.value, 10) : new Date().getFullYear();
    const termine = [];
    for (let m = 1; m <= 12; m++) {
        const v = document.getElementById('einAkontoTermin-' + m)?.value;
        if (v) termine.push({ month: m, payoutDate: v });
    }
    if (!termine.length) {
        alert('Keine Akonto-Termine erfasst — bitte zuerst „Jahr generieren" oder Daten von Hand eintragen.');
        return;
    }
    try {
        const r = await fetch('/api/akonto-termine/save', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyProfileId: branchId, year, termine })
        });
        if (!r.ok) {
            const j = await r.json().catch(() => null);
            alert('Fehler beim Speichern: ' + (j?.error || j?.message || ('HTTP ' + r.status)));
            return;
        }
        if (typeof showToast === 'function') showToast(`Akonto-Termine ${year} gespeichert.`, 'success');
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Überträgt den kompletten Einstellungen-Block dieser Filiale (inkl.
// Akonto-Termine des gewählten Jahres) auf ALLE anderen Filialen.
// Übertragen wird der zuletzt GESPEICHERTE Stand — darum der Hinweis im
// Bestätigungsdialog, vorher zu speichern.
async function copyEinstellungenToAll(branchId) {
    const yearSel = document.getElementById('einAkontoYear');
    const year = yearSel ? parseInt(yearSel.value, 10) : new Date().getFullYear();
    if (!confirm(
        `Den kompletten Einstellungen-Block dieser Filiale — inkl. Akonto-Termine ${year} — `
        + `auf ALLE anderen Filialen übertragen?\n\n`
        + `Die bestehenden Einstellungen der anderen Filialen werden dabei überschrieben.\n\n`
        + `Hinweis: übertragen wird der zuletzt GESPEICHERTE Stand dieser Filiale — `
        + `falls du gerade etwas geändert hast, zuerst „Speichern" (und ggf. „Termine speichern") klicken.`
    )) return;
    try {
        const r = await fetch(`/api/companyprofiles/${branchId}/copy-einstellungen-to-all`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ year })
        });
        if (!r.ok) {
            const j = await r.json().catch(() => null);
            alert('Fehler beim Übertragen: ' + (j?.error || j?.message || ('HTTP ' + r.status)));
            return;
        }
        const data = await r.json();
        const termineNote = data.termineCopied > 0
            ? ` (inkl. Akonto-Termine ${year})`
            : ` (keine Akonto-Termine ${year} hinterlegt — nur die übrigen Einstellungen)`;
        if (typeof showToast === 'function')
            showToast(`Einstellungen auf ${data.branchesUpdated} Filialen übertragen${termineNote}.`, 'success');
        else
            alert(`Einstellungen auf ${data.branchesUpdated} Filialen übertragen${termineNote}.`);
        // Cache frisch vom Server holen, sonst zeigt die Detailansicht der Ziel-
        // Filialen weiter die ALTEN Werte (Walter-Bug 22.06.2026).
        if (typeof loadAllBranches === 'function') await loadAllBranches();
        if (typeof loadFilialen === 'function') loadFilialen();
        if (selectedBranch) {
            const fresh = allBranches.find(b => b.id === selectedBranch.id);
            if (fresh) { selectedBranch = fresh; renderFilialenDetail(fresh); }
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// ── Einstellungen-Tab speichern ────────────────────────────────────────
// Liest alle Inline-Felder, validiert (Logik gespiegelt aus den früheren
// Modal-Save-Funktionen) und schickt die 5 PATCH-Calls parallel.
async function saveEinstellungen(branchId) {
    const g = id => document.getElementById(id);
    const nightStart   = g('einNightStart')?.value || '';
    const nightEnd     = g('einNightEnd')?.value || '';
    const maxWeeklyRaw = g('einMaxWeeklyHours')?.value;
    const maxWeekly    = (maxWeeklyRaw == null || maxWeeklyRaw === '') ? null : Number(maxWeeklyRaw);
    const autoFG       = g('einAutoFerienGeld')?.value === 'true';
    const karenzBasis  = g('einKarenzBasis')?.value || 'ARBEITSJAHR';
    const karenzKrank  = Number(g('einKarenzKrank')?.value);
    const karenzUnfall = Number(g('einKarenzUnfall')?.value);
    const bvgWartefrist= parseInt(g('einBvgWartefrist')?.value, 10);
    const lgavAktiv    = g('einLgavAktiv')?.value === 'true';
    const lgavMonat    = parseInt(g('einLgavMonat')?.value, 10);
    const lgavVoll     = Number(g('einLgavVoll')?.value);
    const lgavRed      = Number(g('einLgavRed')?.value);
    const akontoProzent       = Number(g('einAkontoProzent')?.value);
    const akontoProzentFixM   = Number(g('einAkontoProzentFixM')?.value);
    const akontoProzentHourly = Number(g('einAkontoProzentHourly')?.value);
    const vacSixWeeksAge      = parseInt(g('einVacationSixWeeksFromAge')?.value, 10);
    const defaultThirteenthRaw= g('einDefaultThirteenthPct')?.value;
    const defaultThirteenth   = (defaultThirteenthRaw == null || defaultThirteenthRaw === '') ? null : Number(defaultThirteenthRaw);
    const tpMonths     = [..._einTpMonths].sort((a, b) => a - b);

    // Validierung
    if (!nightStart || !nightEnd) { alert('Bitte beide Nachtzeiten angeben.'); return; }
    if (maxWeekly != null && (!Number.isFinite(maxWeekly) || maxWeekly < 0 || maxWeekly > 168)) { alert('Max. Stunden / Woche muss zwischen 0 und 168 liegen (oder leer für keine Grenze).'); return; }
    if (!['ARBEITSJAHR', 'KALENDERJAHR'].includes(karenzBasis)) { alert('Karenzjahr-Basis ungültig.'); return; }
    if (!Number.isFinite(karenzKrank)  || karenzKrank  < 0 || karenzKrank  > 365) { alert('Karenz-Tage Krank muss zwischen 0 und 365 liegen.'); return; }
    if (!Number.isFinite(karenzUnfall) || karenzUnfall < 0 || karenzUnfall > 365) { alert('Karenz-Tage Unfall muss zwischen 0 und 365 liegen.'); return; }
    if (!Number.isFinite(bvgWartefrist)|| bvgWartefrist< 0 || bvgWartefrist> 24)  { alert('BVG-Wartefrist muss zwischen 0 und 24 Monaten liegen.'); return; }
    if (!(lgavMonat >= 1 && lgavMonat <= 12)) { alert('L-GAV Abzugs-Monat ungültig.'); return; }
    if (!Number.isFinite(lgavVoll) || lgavVoll < 0) { alert('L-GAV voller Beitrag ungültig.'); return; }
    if (!Number.isFinite(lgavRed)  || lgavRed  < 0) { alert('L-GAV reduzierter Beitrag ungültig.'); return; }
    if (!Number.isFinite(akontoProzent) || akontoProzent < 0 || akontoProzent > 100) { alert('Akonto-% (FIX) muss zwischen 0 und 100 liegen.'); return; }
    if (!Number.isFinite(akontoProzentFixM) || akontoProzentFixM < 0 || akontoProzentFixM > 100) { alert('Akonto-% (FIX-M) muss zwischen 0 und 100 liegen.'); return; }
    if (!Number.isFinite(akontoProzentHourly) || akontoProzentHourly < 0 || akontoProzentHourly > 100) { alert('Akonto-% (UTP/MTP) muss zwischen 0 und 100 liegen.'); return; }
    if (!Number.isFinite(vacSixWeeksAge) || vacSixWeeksAge < 0 || vacSixWeeksAge > 100) { alert('„6 Wochen ab Alter" muss zwischen 0 und 100 liegen (Standard L-GAV = 50).'); return; }
    if (defaultThirteenth != null && (!Number.isFinite(defaultThirteenth) || defaultThirteenth < 0 || defaultThirteenth > 100)) { alert('„13. ML %" muss zwischen 0 und 100 liegen (Standard L-GAV = 8.33; leer = nicht gesetzt).'); return; }
    if (tpMonths.length === 0 && !confirm('Keine 13.-ML-Auszahlungsmonate gewählt — der 13. ML wird gar nicht ausbezahlt. Trotzdem speichern?')) return;

    const btn = g('einSaveBtn-' + branchId);
    if (btn) { btn.disabled = true; btn.textContent = '⏳ Speichern…'; }
    const H = { ...ah(), 'Content-Type': 'application/json' };
    try {
        const results = await Promise.all([
            fetch(`/api/companyprofiles/${branchId}/nighthours`,                 { method: 'PATCH', headers: H, body: JSON.stringify({ nightStartTime: nightStart, nightEndTime: nightEnd }) }),
            fetch(`/api/companyprofiles/${branchId}/auto-ferien-geld-dezember`,   { method: 'PATCH', headers: H, body: JSON.stringify({ aktiv: autoFG }) }),
            fetch(`/api/companyprofiles/${branchId}/karenz`,                      { method: 'PATCH', headers: H, body: JSON.stringify({ karenzjahrBasis: karenzBasis, karenzTageMax: karenzKrank, karenzTageMaxUnfall: karenzUnfall, bvgWartefristMonate: bvgWartefrist }) }),
            fetch(`/api/companyprofiles/${branchId}/lgav`,                        { method: 'PATCH', headers: H, body: JSON.stringify({ lgavAktiv, lgavTriggerMonat: lgavMonat, lgavBeitragVoll: lgavVoll, lgavBeitragReduziert: lgavRed }) }),
            fetch(`/api/companyprofiles/${branchId}/thirteenth-payouts`,          { method: 'PATCH', headers: H, body: JSON.stringify({ months: tpMonths, payoutsPerYear: tpMonths.length || 12 }) }),
            fetch(`/api/companyprofiles/${branchId}/akonto-prozent`,              { method: 'PATCH', headers: H, body: JSON.stringify({ akontoProzentFix: akontoProzent, akontoProzentFixM: akontoProzentFixM, akontoProzentHourly: akontoProzentHourly }) }),
            fetch(`/api/companyprofiles/${branchId}/max-weekly-hours`,            { method: 'PATCH', headers: H, body: JSON.stringify({ maxWeeklyHours: maxWeekly }) }),
            fetch(`/api/companyprofiles/${branchId}/vacation-six-weeks-from-age`, { method: 'PATCH', headers: H, body: JSON.stringify({ vacationSixWeeksFromAge: vacSixWeeksAge }) }),
            fetch(`/api/companyprofiles/${branchId}/default-thirteenth-percent`,  { method: 'PATCH', headers: H, body: JSON.stringify({ defaultThirteenthSalaryPercent: defaultThirteenth }) }),
        ]);
        const failed = results.filter(r => !r.ok).length;

        // Lokale Kopien aktualisieren (analog zu den früheren Modal-Saves)
        const patch = {
            nightStartTime: nightStart, nightEndTime: nightEnd,
            maxWeeklyHours: maxWeekly,
            autoFerienGeldAuszahlungDezember: autoFG,
            karenzjahrBasis: karenzBasis, karenzTageMax: karenzKrank,
            karenzTageMaxUnfall: karenzUnfall, bvgWartefristMonate: bvgWartefrist,
            lgavAktiv, lgavTriggerMonat: lgavMonat, lgavBeitragVoll: lgavVoll, lgavBeitragReduziert: lgavRed,
            thirteenthMonthPayoutMonths: tpMonths.join(','), thirteenthMonthPayoutsPerYear: tpMonths.length || 12,
            akontoProzentFix: akontoProzent,
            akontoProzentFixM: akontoProzentFixM,
            akontoProzentHourly: akontoProzentHourly,
            vacationSixWeeksFromAge: vacSixWeeksAge,
            defaultThirteenthSalaryPercent: defaultThirteenth,
        };
        const b = (typeof allBranches !== 'undefined' ? allBranches : []).find(x => x.id === branchId);
        if (b) Object.assign(b, patch);
        if (typeof selectedCompanyProfile !== 'undefined' && selectedCompanyProfile?.id === branchId) {
            Object.assign(selectedCompanyProfile, patch);
        }
        if (typeof selectedBranch !== 'undefined' && selectedBranch?.id === branchId) {
            Object.assign(selectedBranch, patch);
            renderFilialenDetail(selectedBranch);
        }
        if (typeof loadFilialen === 'function') loadFilialen();

        if (failed > 0) {
            alert(`${failed} von 9 Einstellungs-Gruppen konnten nicht gespeichert werden. Bitte erneut versuchen.`);
        } else if (typeof showToast === 'function') {
            showToast('Einstellungen gespeichert.', 'success');
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    } finally {
        const btn2 = document.getElementById('einSaveBtn-' + branchId);
        if (btn2) { btn2.disabled = false; btn2.textContent = '💾 Speichern'; }
    }
}

// Gemerkter aktiver Filial-Detail-Tab — bleibt über Filial-Wechsel erhalten.
let activeFilialenTab = 'f-stamm';

function switchFilialenTab(tab) {
    // Guard: existiert der Tab nicht (z.B. «Dokumente» ohne Berechtigung
    // nach User-Wechsel gemerkt), auf Stammdaten zurückfallen.
    if (!document.getElementById('fil-tab-' + tab)) tab = 'f-stamm';
    activeFilialenTab = tab;
    document.querySelectorAll('#filialenDetailPanel .emp-tab').forEach(t => {
        t.classList.toggle('active', t.dataset.ftab === tab);
    });
    document.querySelectorAll('#filialenDetailPanel .emp-tab-content').forEach(c => {
        c.classList.toggle('active', c.id === 'fil-tab-' + tab);
    });
    // Einstellungen-Aktionsbuttons (in der Tab-Leiste) nur im
    // Einstellungen-Tab einblenden.
    const einActions = document.getElementById('filEinstellungenActions');
    if (einActions) einActions.style.display = (tab === 'f-einstellungen') ? 'flex' : 'none';
    // Dokumente-Tab: Liste beim Betreten (neu) laden.
    if (tab === 'f-doks' && selectedBranch && typeof filDoksLoad === 'function') {
        filDoksLoad(selectedBranch.id);
    }
    // Lohndatenempfänger-Tab: Zuordnungen beim Betreten laden.
    if (tab === 'f-empf' && selectedBranch && typeof cpEmpfLoad === 'function') {
        cpEmpfLoad(selectedBranch.id);
    }
}

// ── Unterzeichner ──────────────────────────────────
const roleLabels = { GESCHAEFTSFUEHRER:'Geschäftsführer/in', HR_VERANTWORTLICH:'HR-Verantwortlich', BUCHHALTUNG:'Buchhaltung', SONSTIGES:'Sonstiges' };

// ── Unterzeichner via user_branch_access ──────────────────────────────
let editingSignatoryId = null;

async function loadSignatories(companyId) {
    const el = document.getElementById('signatoryList');
    if (!el) return;
    try {
        const res = await fetch(`/api/userbranch/company/${companyId}`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div style="color:#dc2626;padding:12px">Fehler beim Laden.</div>'; return; }
        renderSignatoryList(await res.json());
    } catch { el.innerHTML = '<div style="color:#dc2626;padding:12px">Verbindungsfehler.</div>'; }
}

function renderSignatoryList(list) {
    const el = document.getElementById('signatoryList');
    if (!el) return;
    if (!list.length) { el.innerHTML = '<div style="color:#94a3b8;padding:20px;text-align:center">Noch keine Benutzer zugewiesen.</div>'; return; }
    el.innerHTML = list.map(s => {
        const name = s.user?.firstName ? `${s.user.firstName} ${s.user.lastName||''}`.trim() : s.user?.username || '–';
        const initials = ((s.user?.firstName||'')[0]||'') + ((s.user?.lastName||'')[0]||'');
        return `
        <div style="display:flex;align-items:center;gap:12px;padding:10px 0;border-bottom:1px solid #f1f5f9">
            <div style="width:36px;height:36px;border-radius:50%;background:#e2e8f0;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:12px;color:#475569;flex-shrink:0">${initials.toUpperCase()}</div>
            <div style="flex:1;min-width:0">
                <div style="font-weight:600;font-size:13px">${name}</div>
                <div style="font-size:12px;color:#64748b">${s.functionTitle || s.user?.email || '–'}</div>
                ${s.user?.phone ? `<div style="font-size:11px;color:#94a3b8">${s.user.phone}</div>` : ''}
            </div>
            <div style="flex-shrink:0;display:flex;gap:4px;flex-wrap:wrap;justify-content:flex-end">
                ${s.role ? `<span style="font-size:11px;font-weight:600;padding:2px 8px;border-radius:10px;background:#f0efeb;color:#6b7280">${roleLabels[s.role]||s.role}</span>` : ''}
                ${s.isDefault ? `<span style="font-size:11px;font-weight:600;padding:2px 8px;border-radius:10px;background:#f0fdf4;color:#15803d">Allgemein</span>` : ''}
            </div>
            <div style="display:flex;gap:6px;flex-shrink:0">
                <button class="btn btn-outline" style="font-size:12px;padding:3px 10px" onclick='openSignatoryForm(${JSON.stringify(s)})'>✎</button>
                <button class="btn btn-outline" style="font-size:12px;padding:3px 10px;color:#dc2626" onclick="deleteSignatory(${s.id})">✕</button>
            </div>
        </div>`;
    }).join('');
}

async function openSignatoryForm(s) {
    editingSignatoryId = s ? s.id : null;
    document.getElementById('signatoryFormTitle').textContent = s ? 'Zuweisung bearbeiten' : 'Benutzer zuweisen';

    // Benutzer-Dropdown laden
    const sel = document.getElementById('sig-userId');
    sel.innerHTML = '<option value="">– Benutzer wählen –</option>';
    try {
        const res = await fetch('/api/users', { headers: ah() });
        if (res.ok) {
            const users = await res.json();
            users.filter(u => u.isActive).forEach(u => {
                const name = u.firstName ? `${u.firstName} ${u.lastName||''}`.trim() : u.username;
                const opt = document.createElement('option');
                opt.value = u.id;
                opt.textContent = name + (u.email ? ` (${u.email})` : '');
                sel.appendChild(opt);
            });
        }
    } catch {}

    sel.value = s?.userId || s?.user?.id || '';
    document.getElementById('sig-function').value    = s?.functionTitle || '';
    document.getElementById('sig-role').value        = s?.role          || '';
    document.getElementById('sig-isDefault').checked = s?.isDefault     || false;
    document.getElementById('signatoryForm').style.display = 'block';
    document.getElementById('signatoryForm').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function closeSignatoryForm() {
    document.getElementById('signatoryForm').style.display = 'none';
    editingSignatoryId = null;
}

async function saveSignatory() {
    if (!selectedBranch) { alert('Keine Filiale ausgewählt — bitte links eine Filiale anklicken.'); return; }
    const userId = parseInt(document.getElementById('sig-userId')?.value || '');
    if (!userId) { alert('Bitte einen Benutzer auswählen.'); return; }
    try {
        const payload = {
            userId,
            companyProfileId: selectedBranch.id,
            functionTitle: (document.getElementById('sig-function')?.value || '').trim() || null,
            role:          document.getElementById('sig-role')?.value               || null,
            isDefault:     !!document.getElementById('sig-isDefault')?.checked,
        };
        const url    = editingSignatoryId ? `/api/userbranch/${editingSignatoryId}` : '/api/userbranch';
        const method = editingSignatoryId ? 'PUT' : 'POST';
        const res = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
        if (!res.ok) {
            // Echten Grund zeigen statt Pauschal-Meldung (Walter-Bug 22.07.2026).
            let msg = '';
            try { const j = await res.json(); msg = j.message || j.error || ''; } catch {}
            if (!msg && res.status === 403) msg = 'Keine Berechtigung — Filial-Zuweisungen kann nur ein Admin ändern (nicht im Testmodus/als GF).';
            if (!msg && res.status === 401) msg = 'Sitzung abgelaufen — bitte neu anmelden.';
            alert(`Fehler beim Speichern (${res.status})${msg ? ':\n' + msg : '.'}`);
            return;
        }
        closeSignatoryForm();
        await loadSignatories(selectedBranch.id);
    } catch (e) {
        alert('Speichern fehlgeschlagen: ' + (e?.message || 'Verbindungsfehler'));
    }
}

async function deleteSignatory(id) {
    if (!(await liquidConfirm('Zuweisung wirklich entfernen?', { title: 'Zuweisung entfernen', yesLabel: 'Entfernen' }))) return;
    try {
        const res = await fetch(`/api/userbranch/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        await loadSignatories(selectedBranch.id);
    } catch { alert('Verbindungsfehler.'); }
}

// ── Filial-Stammdaten Modal (ersetzt das frühere ALV-Sub-Modal) ─────────────
// Alle Felder sind in einem Rutsch bearbeitbar. PLZ-Lookup setzt Ort + Kanton
// automatisch. Backend: PATCH /api/companyprofiles/{id}/stammdaten.
let stmModalBranchId = null;

async function openAlvDatenModal(id) {
    // Alias-Funktion aus historischen Gründen — der Detail-Bearbeiten-Button
    // ruft openAlvDatenModal auf; wir leiten auf das neue Stammdaten-Modal um.
    return openStmModal(id);
}

async function openStmModal(id) {
    stmModalBranchId = id;
    try {
        const res = await fetch(`/api/companyprofiles/${id}`, { headers: ah() });
        const b = res.ok ? await res.json() : {};
        const subtitle = document.getElementById('stmModalSubtitle');
        if (subtitle) {
            const display = b.branchName || b.companyName || `Filiale #${id}`;
            subtitle.textContent = `${display}${b.restaurantCode ? ' · Code ' + b.restaurantCode : ''}`;
        }
        document.getElementById('stmCompanyName').value    = b.companyName    || '';
        document.getElementById('stmBranchName').value     = b.branchName     || '';
        document.getElementById('stmRestaurantCode').value = b.restaurantCode || '';
        document.getElementById('stmStreet').value         = b.street         || '';
        document.getElementById('stmHouseNumber').value    = b.houseNumber    || '';
        document.getElementById('stmZipCode').value        = b.zipCode        || '';
        document.getElementById('stmCity').value           = b.city           || '';
        document.getElementById('stmKantonCode').value     = (b.kantonCode || '').toUpperCase();
        document.getElementById('stmPhone').value          = b.phone          || '';
        document.getElementById('stmEmail').value          = b.email          || '';
        const stmWl = document.getElementById('stmWorkLocation');
        if (stmWl) stmWl.value = b.workLocation || '';
        document.getElementById('stmBurNummer').value      = b.burNummer      || '';
        document.getElementById('stmUidNummer').value      = b.uidNummer      || '';
        document.getElementById('stmBranchenCode').value   = b.branchenCode   || '';
        document.getElementById('stmAhvKasse').value       = b.ahvKasse       || '';
        document.getElementById('stmBvgVersicherer').value = b.bvgVersicherer || '';
        document.getElementById('stmIstGav').checked       = !!b.istGav;
        document.getElementById('stmGavName').value        = b.gavName        || '';
        // Lohnausweis-Standardwerte (Walter 13.05.2026: pro Filiale konfigurierbar)
        document.getElementById('stmLohnausweisBoxF').checked = !!b.lohnausweisBoxFFreierTransport;
        document.getElementById('stmLohnausweisBoxG').checked = !!b.lohnausweisBoxGKantineGratis;
        document.getElementById('stmLohnausweisPos21').value  =
            (b.lohnausweisPos21VerpflegungMonat == null) ? '' : b.lohnausweisPos21VerpflegungMonat;
        const stmProb = document.getElementById('stmProbationMonths');
        if (stmProb) stmProb.value = (b.probationMonths == null) ? '' : String(b.probationMonths);
        document.getElementById('stmPlzHint').innerHTML    = '';
        stmToggleGavName();
    } catch { /* leere Felder anzeigen */ }
    document.getElementById('stmDatenModal').style.display = 'flex';
}

function closeStmModal() {
    document.getElementById('stmDatenModal').style.display = 'none';
    stmModalBranchId = null;
}

function stmToggleGavName() {
    const checked = document.getElementById('stmIstGav').checked;
    document.getElementById('stmGavNameRow').style.display = checked ? 'block' : 'none';
}

// PLZ-Lookup im Stammdaten-Modal — füllt Ort + Standort-Kanton automatisch.
// Eigene Implementierung, weil employees.js' plzLookup hardcoded auf
// 'ef-zip/ef-city/ef-canton' arbeitet.
async function stmPlzLookup(rawPlz) {
    const plz = (rawPlz ?? '').toString().trim();
    const cityEl   = document.getElementById('stmCity');
    const kantonEl = document.getElementById('stmKantonCode');
    const hint     = document.getElementById('stmPlzHint');
    if (!cityEl || !kantonEl || !hint) return;

    if (!/^\d{4}$/.test(plz)) { hint.innerHTML = ''; return; }
    try {
        const res = await fetch(`/api/swiss-locations/by-plz?plz=${encodeURIComponent(plz)}`, { headers: ah() });
        if (!res.ok) return;
        const locs = await res.json();
        if (!locs.length) {
            hint.innerHTML = `<span style="color:#b45309">⚠ PLZ ${plz} nicht im Ortschaftsverzeichnis gefunden — Ort und Kanton bitte manuell eintragen.</span>`;
            return;
        }
        if (locs.length === 1) {
            const l = locs[0];
            const ortName = (typeof stripCityCantonSuffix === 'function'
                ? stripCityCantonSuffix(l.ortschaftsname || l.gemeindename)
                : (l.ortschaftsname || l.gemeindename));
            cityEl.value   = ortName;
            kantonEl.value = l.kantonskuerzel;
            hint.innerHTML = `<span style="color:#16a34a">✓ ${ortName}</span>`;
            return;
        }
        // Mehrere Treffer: nur Hinweis — Ort und Kanton nicht überschreiben falls schon gefüllt
        hint.innerHTML = `<span style="color:#8b8b8b">${locs.length} Orte für PLZ ${plz} gefunden — bitte Ort manuell wählen.</span>`;
    } catch {
        hint.innerHTML = `<span style="color:#b45309">PLZ-Lookup nicht verfügbar.</span>`;
    }
}

async function saveStm() {
    if (!stmModalBranchId) return;
    const trimOrNull = id => {
        const v = (document.getElementById(id)?.value || '').trim();
        return v || null;
    };
    const kanton = (document.getElementById('stmKantonCode').value || '').trim().toUpperCase() || null;

    const payload = {
        companyName:    trimOrNull('stmCompanyName'),
        branchName:     trimOrNull('stmBranchName'),
        restaurantCode: trimOrNull('stmRestaurantCode'),
        street:         trimOrNull('stmStreet'),
        houseNumber:    trimOrNull('stmHouseNumber'),
        zipCode:        trimOrNull('stmZipCode'),
        city:           trimOrNull('stmCity'),
        kantonCode:     kanton,
        phone:          trimOrNull('stmPhone'),
        email:          trimOrNull('stmEmail'),
        workLocation:   trimOrNull('stmWorkLocation'),
        burNummer:      trimOrNull('stmBurNummer'),
        uidNummer:      trimOrNull('stmUidNummer'),
        branchenCode:   trimOrNull('stmBranchenCode'),
        ahvKasse:       trimOrNull('stmAhvKasse'),
        bvgVersicherer: trimOrNull('stmBvgVersicherer'),
        istGav:         document.getElementById('stmIstGav').checked,
        gavName:        trimOrNull('stmGavName'),
        // Lohnausweis-Standardwerte
        lohnausweisBoxFFreierTransport: document.getElementById('stmLohnausweisBoxF').checked,
        lohnausweisBoxGKantineGratis:   document.getElementById('stmLohnausweisBoxG').checked,
        lohnausweisPos21VerpflegungMonat: (() => {
            const v = (document.getElementById('stmLohnausweisPos21').value || '').trim();
            if (!v) return null;
            const n = parseFloat(v);
            return Number.isFinite(n) ? n : null;
        })(),
        probationMonths: (() => {
            const v = (document.getElementById('stmProbationMonths')?.value || '').trim();
            return v ? parseInt(v, 10) : null;
        })(),
    };

    try {
        const res = await fetch(`/api/companyprofiles/${stmModalBranchId}/stammdaten`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || j?.error || 'Fehler beim Speichern.');
            return;
        }
        const b = allBranches.find(x => x.id === stmModalBranchId);
        if (b) Object.assign(b, payload);
        if (selectedBranch?.id === stmModalBranchId) {
            Object.assign(selectedBranch, payload);
            renderFilialenDetail(selectedBranch);
        }
        closeStmModal();
        loadFilialen();
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// ── Filial-Bankverbindungen (Auftraggeber-Konto fürs DTA, mit Historie) ────

async function loadCompanyBankList(branchId) {
    const el = document.getElementById('companyBankList-' + branchId);
    if (!el) return;
    try {
        const r = await fetch(`/api/company-profile-bank-accounts/company/${branchId}`, { headers: ah() });
        if (!r.ok) { el.innerHTML = '<div style="color:#dc2626;padding:14px;font-size:12px">Fehler beim Laden</div>'; return; }
        const list = await r.json();
        if (!list.length) {
            el.innerHTML = '<div style="color:#94a3b8;padding:14px;text-align:center;font-size:12px">Noch keine Bankverbindung erfasst — auf „+ Hinzufügen" klicken</div>';
            return;
        }
        const today = new Date().toISOString().slice(0, 10);
        const fmtDate  = d => d ? new Date(d + 'T00:00:00').toLocaleDateString('de-CH') : '–';
        const fmtIban  = i => (i || '').replace(/(.{4})/g, '$1 ').trim();
        const isCurrentlyValid = e => e.validFrom <= today && (!e.validTo || e.validTo >= today);
        el.innerHTML = `
        <div class="card" style="padding:0;overflow:auto">
            <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                <thead>
                    <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0">
                        <th style="padding:8px 12px;text-align:left;font-weight:600;color:#475569">Status</th>
                        <th style="padding:8px 12px;text-align:left;font-weight:600;color:#475569">IBAN</th>
                        <th style="padding:8px 12px;text-align:left;font-weight:600;color:#475569">Bank</th>
                        <th style="padding:8px 12px;text-align:left;font-weight:600;color:#475569">Gültig</th>
                        <th style="padding:8px 12px;text-align:right;font-weight:600;color:#475569"></th>
                    </tr>
                </thead>
                <tbody>
                    ${list.map(e => `
                    <tr style="border-top:1px solid #f1f5f9">
                        <td style="padding:6px 12px">
                            ${e.isMain ? '<span style="display:inline-block;background:#ecebe7;color:#6b7280;padding:1px 8px;border-radius:8px;font-size:10.5px;font-weight:600">Hauptbank</span>' : ''}
                            ${isCurrentlyValid(e) ? '<span style="display:inline-block;background:#dcfce7;color:#15803d;padding:1px 8px;border-radius:8px;font-size:10.5px;font-weight:600;margin-left:4px">aktiv</span>' : '<span style="display:inline-block;background:#fef3c7;color:#92400e;padding:1px 8px;border-radius:8px;font-size:10.5px;font-weight:600;margin-left:4px">inaktiv</span>'}
                        </td>
                        <td style="padding:6px 12px;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:11.5px">${fmtIban(e.iban)}</td>
                        <td style="padding:6px 12px">${e.bankName || '–'}${e.bic ? `<div style="font-size:10.5px;color:#94a3b8">${e.bic}</div>` : ''}</td>
                        <td style="padding:6px 12px;font-size:11.5px;color:#475569">${fmtDate(e.validFrom)} → ${e.validTo ? fmtDate(e.validTo) : 'offen'}${e.bemerkung ? `<div style="font-size:11px;color:#94a3b8;margin-top:2px">${e.bemerkung}</div>` : ''}</td>
                        <td style="padding:6px 12px;text-align:right">
                            <button class="btn btn-outline" style="padding:3px 9px;font-size:11px" onclick='openCompanyBankModal(${branchId}, ${e.id})'>✎</button>
                        </td>
                    </tr>`).join('')}
                </tbody>
            </table>
        </div>`;
    } catch (err) {
        el.innerHTML = '<div style="color:#dc2626;padding:14px;font-size:12px">Fehler: ' + err.message + '</div>';
    }
}

let _companyBankBranchId = null;
let _companyBankEditId   = null;

async function openCompanyBankModal(branchId, editId) {
    _companyBankBranchId = branchId;
    _companyBankEditId   = editId || null;
    document.getElementById('companyBankBranchId').value = branchId;
    document.getElementById('companyBankEditId').value   = editId || '';
    document.getElementById('companyBankModalTitle').textContent =
        editId ? 'Bankverbindung bearbeiten' : 'Bankverbindung hinzufügen';
    document.getElementById('companyBankDeleteBtn').style.display = editId ? '' : 'none';

    // Defaults: Gültig ab = heute, Hauptbank = ja (wenn neu)
    const today = new Date().toISOString().slice(0, 10);
    let entry = null;
    if (editId) {
        try {
            const r = await fetch(`/api/company-profile-bank-accounts/company/${branchId}`, { headers: ah() });
            if (r.ok) {
                const list = await r.json();
                entry = list.find(x => x.id === editId);
            }
        } catch {}
    }

    document.getElementById('companyBankIban').value      = entry?.iban       ?? '';
    document.getElementById('companyBankBic').value       = entry?.bic        ?? '';
    document.getElementById('companyBankName').value      = entry?.bankName   ?? '';
    document.getElementById('companyBankValidFrom').value = entry?.validFrom  ?? today;
    document.getElementById('companyBankValidTo').value   = entry?.validTo    ?? '';
    document.getElementById('companyBankIsMain').checked  = entry ? !!entry.isMain : true;
    document.getElementById('companyBankBemerkung').value = entry?.bemerkung  ?? '';
    document.getElementById('companyBankIbanHint').textContent = '';
    document.getElementById('companyBankIbanHint').style.color = '';
    document.getElementById('companyBankModal').style.display = 'flex';
    if (entry?.iban) validateCompanyBankIban(document.getElementById('companyBankIban'));
}

function closeCompanyBankModal() {
    document.getElementById('companyBankModal').style.display = 'none';
    _companyBankBranchId = null;
    _companyBankEditId   = null;
}

// IBAN-Validation + Auto-Fill von BIC/Bankname (gleiche Logik wie MA-Bank-Modal)
async function validateCompanyBankIban(inputEl) {
    const hint = document.getElementById('companyBankIbanHint');
    if (!hint) return;
    const val = inputEl.value.trim();
    if (!val) { hint.textContent = ''; hint.style.color = ''; inputEl.style.borderColor = ''; return; }
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
                    const bicEl  = document.getElementById('companyBankBic');
                    const nameEl = document.getElementById('companyBankName');
                    if (bicEl  && !bicEl.value.trim()  && b.bic)  bicEl.value  = b.bic;
                    if (nameEl && !nameEl.value.trim() && b.name) nameEl.value = b.name;
                }
            } catch {}
        }
    } else {
        hint.textContent = '✗ ' + (r.message || 'Ungültige IBAN');
        hint.style.color = '#dc2626';
        inputEl.style.borderColor = '#fca5a5';
    }
}

async function saveCompanyBank() {
    if (!_companyBankBranchId) return;
    const iban      = document.getElementById('companyBankIban').value.trim();
    const validFrom = document.getElementById('companyBankValidFrom').value;
    if (!iban)      { alert('IBAN ist erforderlich.'); return; }
    if (!validFrom) { alert('„Gültig ab" ist erforderlich.'); return; }
    const payload = {
        companyProfileId: _companyBankBranchId,
        iban,
        bic:       document.getElementById('companyBankBic').value.trim()  || null,
        bankName:  document.getElementById('companyBankName').value.trim() || null,
        isMain:    document.getElementById('companyBankIsMain').checked,
        bemerkung: document.getElementById('companyBankBemerkung').value.trim() || null,
        validFrom,
        validTo:   document.getElementById('companyBankValidTo').value || null,
    };
    try {
        const url    = _companyBankEditId
                          ? `/api/company-profile-bank-accounts/${_companyBankEditId}`
                          : `/api/company-profile-bank-accounts`;
        const method = _companyBankEditId ? 'PUT' : 'POST';
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (!res.ok) {
            let msg = 'Fehler beim Speichern.';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch {}
            alert(msg); return;
        }
        const branchId = _companyBankBranchId;
        closeCompanyBankModal();
        loadCompanyBankList(branchId);
        showToast('Bankverbindung gespeichert ✓', 'success');
    } catch { alert('Verbindungsfehler.'); }
}

async function deleteCompanyBank() {
    if (!_companyBankEditId) return;
    if (!confirm('Bankverbindung wirklich löschen?')) return;
    try {
        const res = await fetch(`/api/company-profile-bank-accounts/${_companyBankEditId}`, {
            method: 'DELETE', headers: ah()
        });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        const branchId = _companyBankBranchId;
        closeCompanyBankModal();
        loadCompanyBankList(branchId);
        showToast('Bankverbindung gelöscht', 'success');
    } catch { alert('Verbindungsfehler.'); }
}

// ── SSL-Nummern Quellensteuer (eine pro Filiale × Kanton) ─────────────
const _SSL_KANTON_LABEL = {
    AG:'Aargau',AI:'Appenzell IR',AR:'Appenzell AR',BE:'Bern',
    BL:'Basel-Land',BS:'Basel-Stadt',FR:'Freiburg',GE:'Genf',GL:'Glarus',
    GR:'Graubünden',JU:'Jura',LU:'Luzern',NE:'Neuenburg',NW:'Nidwalden',
    OW:'Obwalden',SG:'St. Gallen',SH:'Schaffhausen',SO:'Solothurn',SZ:'Schwyz',
    TG:'Thurgau',TI:'Tessin',UR:'Uri',VD:'Waadt',VS:'Wallis',ZG:'Zug',ZH:'Zürich'
};

async function loadSslListForBranch(companyId) {
    const el = document.getElementById('sslList-' + companyId);
    if (!el) return;
    try {
        const res = await fetch(`/api/companyprofiles/${companyId}/ssl`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div style="color:#dc2626;padding:10px;font-size:12px">Fehler beim Laden.</div>'; return; }
        const list = await res.json();
        if (!list.length) {
            el.innerHTML = '<div style="color:#94a3b8;padding:14px;text-align:center;font-size:12px;font-style:italic">Noch keine SSL-Nummern erfasst — füge pro Kanton eine Nummer hinzu, in dem diese Filiale QST-pflichtige MA beschäftigt.</div>';
            return;
        }
        el.innerHTML = `
            <table style="width:100%;border-collapse:collapse;font-size:13px">
                <thead>
                    <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0">
                        <th style="padding:8px 12px;text-align:left;font-weight:600;color:#475569;font-size:11px;letter-spacing:.04em">KANTON</th>
                        <th style="padding:8px 12px;text-align:left;font-weight:600;color:#475569;font-size:11px;letter-spacing:.04em">SSL-NUMMER</th>
                        <th style="padding:8px 12px;text-align:left;font-weight:600;color:#475569;font-size:11px;letter-spacing:.04em">BEMERKUNG</th>
                        <th style="padding:8px 12px;text-align:right;font-weight:600;color:#475569;font-size:11px;letter-spacing:.04em">AKTIONEN</th>
                    </tr>
                </thead>
                <tbody>
                ${list.map(s => `
                    <tr style="border-bottom:1px solid #f1f5f9">
                        <td style="padding:8px 12px"><strong>${s.kantonCode}</strong> <span style="color:#64748b">— ${_SSL_KANTON_LABEL[s.kantonCode] ?? ''}</span></td>
                        <td style="padding:8px 12px;font-family:ui-monospace,Menlo,Consolas,monospace">${s.sslNummer}</td>
                        <td style="padding:8px 12px;color:#64748b">${s.bemerkung ? esc(s.bemerkung) : '<span style="color:#cbd5e1">—</span>'}</td>
                        <td style="padding:8px 12px;text-align:right;white-space:nowrap">
                            <button onclick='openSslModal(${companyId}, ${JSON.stringify(s)})' style="border:none;background:#f1f5f9;color:#374151;padding:4px 10px;border-radius:6px;font-size:12px;cursor:pointer;margin-right:4px">✏️</button>
                            <button onclick="deleteSsl(${companyId}, ${s.id}, '${s.kantonCode}')" style="border:none;background:#fee2e2;color:#dc2626;padding:4px 10px;border-radius:6px;font-size:12px;cursor:pointer">🗑</button>
                        </td>
                    </tr>`).join('')}
                </tbody>
            </table>`;
    } catch (e) {
        el.innerHTML = `<div style="color:#dc2626;padding:10px;font-size:12px">Fehler: ${e.message}</div>`;
    }
}

function openSslModal(companyId, existing) {
    const d = (typeof existing === 'object' && existing !== null) ? existing : {};
    document.getElementById('sslModalCompanyId').value = companyId;
    document.getElementById('sslModalId').value        = d.id ?? '';
    document.getElementById('sslModalKanton').value    = d.kantonCode ?? '';
    document.getElementById('sslModalNummer').value    = d.sslNummer ?? '';
    document.getElementById('sslModalBemerkung').value = d.bemerkung ?? '';
    document.getElementById('sslModalTitle').textContent = d.id ? 'SSL-Nummer bearbeiten' : 'SSL-Nummer hinzufügen';
    document.getElementById('sslModalError').textContent = '';
    document.getElementById('sslModal').style.display = 'flex';
}

function closeSslModal() {
    document.getElementById('sslModal').style.display = 'none';
}

async function saveSslEntry() {
    const companyId = parseInt(document.getElementById('sslModalCompanyId').value);
    const id        = document.getElementById('sslModalId').value;
    const errEl     = document.getElementById('sslModalError');
    const payload = {
        kantonCode: document.getElementById('sslModalKanton').value.trim().toUpperCase(),
        sslNummer:  document.getElementById('sslModalNummer').value.trim(),
        bemerkung:  document.getElementById('sslModalBemerkung').value.trim() || null,
    };
    if (!payload.kantonCode || payload.kantonCode.length !== 2) {
        errEl.textContent = 'Bitte einen Kanton wählen.'; return;
    }
    if (!payload.sslNummer) {
        errEl.textContent = 'Bitte SSL-Nummer eingeben.'; return;
    }
    try {
        const url    = id
            ? `/api/companyprofiles/${companyId}/ssl/${id}`
            : `/api/companyprofiles/${companyId}/ssl`;
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            errEl.textContent = e.error || 'Fehler beim Speichern.';
            return;
        }
        closeSslModal();
        loadSslListForBranch(companyId);
    } catch (e) {
        errEl.textContent = 'Verbindungsfehler: ' + e.message;
    }
}

async function deleteSsl(companyId, id, kantonCode) {
    if (!confirm(`SSL-Nummer für Kanton ${kantonCode} wirklich löschen?`)) return;
    try {
        const res = await fetch(`/api/companyprofiles/${companyId}/ssl/${id}`, {
            method: 'DELETE', headers: ah()
        });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadSslListForBranch(companyId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

let nightHoursModalBranchId = null;
function openNightHoursModal(id, start, end) {
    nightHoursModalBranchId = id;
    document.getElementById('nhStart').value = start;
    document.getElementById('nhEnd').value   = end;
    document.getElementById('nightHoursModal').style.display = 'flex';
}
function closeNightHoursModal() {
    document.getElementById('nightHoursModal').style.display = 'none';
    nightHoursModalBranchId = null;
}
async function saveNightHours() {
    if (!nightHoursModalBranchId) return;
    const start = document.getElementById('nhStart').value;
    const end   = document.getElementById('nhEnd').value;
    if (!start || !end) { alert('Bitte beide Zeiten angeben.'); return; }
    try {
        const res = await fetch(`/api/companyprofiles/${nightHoursModalBranchId}/nighthours`, {
            method: 'PATCH',
            headers: ah(),
            body: JSON.stringify({ nightStartTime: start, nightEndTime: end })
        });
        if (!res.ok) { alert('Fehler beim Speichern.'); return; }
        // Lokale Kopie aktualisieren
        const b = allBranches.find(x => x.id === nightHoursModalBranchId);
        if (b) { b.nightStartTime = start; b.nightEndTime = end; }
        if (selectedCompanyProfile?.id === nightHoursModalBranchId) {
            selectedCompanyProfile.nightStartTime = start;
            selectedCompanyProfile.nightEndTime   = end;
        }
        if (selectedBranch?.id === nightHoursModalBranchId) {
            selectedBranch.nightStartTime = start;
            selectedBranch.nightEndTime   = end;
            renderFilialenDetail(selectedBranch);
        }
        closeNightHoursModal();
        loadFilialen();
    } catch { alert('Verbindungsfehler.'); }
}

// ── 13. ML Auszahlungsrhythmus ──────────────────────
const TP_MONTH_NAMES = ['Jan','Feb','Mär','Apr','Mai','Jun','Jul','Aug','Sep','Okt','Nov','Dez'];

function thirteenthPayoutsLabel(months, legacyPayoutsPerYear) {
    // Bevorzugt aus CSV-Liste, Fallback auf Legacy-Anzahl
    const list = tpParseMonths(months);
    if (list.length === 0) {
        const v = Number(legacyPayoutsPerYear ?? 12);
        if (v === 1) return '1× / Jahr (nur Dezember)';
        if (v === 2) return '2× / Jahr (Juni, Dezember)';
        if (v === 4) return '4× / Jahr (März, Juni, September, Dezember)';
        return 'Monatlich (12× / Jahr)';
    }
    if (list.length === 12) return 'Monatlich (12× / Jahr)';
    if (list.length === 1)  return `1× / Jahr (${TP_MONTH_NAMES[list[0]-1]})`;
    return `${list.length}× / Jahr (${list.map(m => TP_MONTH_NAMES[m-1]).join(', ')})`;
}

function tpParseMonths(csv) {
    if (!csv) return [];
    return String(csv)
        .split(',')
        .map(s => parseInt(s.trim(), 10))
        .filter(n => Number.isFinite(n) && n >= 1 && n <= 12)
        .sort((a, b) => a - b);
}

function tpRenderGrid(selectedMonths) {
    const sel = new Set(selectedMonths);
    const grid = document.getElementById('tpMonthsGrid');
    grid.innerHTML = TP_MONTH_NAMES.map((name, i) => {
        const m = i + 1;
        const checked = sel.has(m);
        const bg = checked ? 'rgba(255,255,255,.85)' : '#f8fafc';
        const fg = checked ? '#3f3f3f' : '#475569';
        const bd = checked ? 'rgba(63,63,63,.55)' : '#e2e8f0';
        return `<label style="display:flex;align-items:center;gap:6px;padding:6px 10px;border:1.5px solid ${bd};border-radius:8px;background:${bg};color:${fg};cursor:pointer;font-size:12.5px;font-weight:600;user-select:none">
            <input type="checkbox" ${checked ? 'checked' : ''} value="${m}" onchange="tpToggleMonth(${m})" style="margin:0">
            ${name}
        </label>`;
    }).join('');
    tpUpdateHint(selectedMonths);
}

function tpUpdateHint(months) {
    const hint = document.getElementById('tpHint');
    if (!hint) return;
    if (months.length === 0)       { hint.textContent = '⚠ Keine Auszahlungsmonate gewählt — 13. ML wird nicht ausbezahlt.'; hint.style.color = '#b45309'; }
    else if (months.length === 12) { hint.textContent = '✓ Monatlich — bei jeder Lohnperiode wird der 13. anteilig ausbezahlt.'; hint.style.color = '#15803d'; }
    else                            { hint.textContent = `✓ ${months.length}× pro Jahr: ${months.map(m => TP_MONTH_NAMES[m-1]).join(', ')}`; hint.style.color = '#6b7280'; }
}

let _tpSelectedMonths = [];
function tpSetMonths(months) {
    _tpSelectedMonths = [...months].sort((a, b) => a - b);
    tpRenderGrid(_tpSelectedMonths);
}
function tpToggleMonth(m) {
    if (_tpSelectedMonths.includes(m)) _tpSelectedMonths = _tpSelectedMonths.filter(x => x !== m);
    else                                _tpSelectedMonths = [..._tpSelectedMonths, m].sort((a, b) => a - b);
    tpRenderGrid(_tpSelectedMonths);
}

let thirteenthPayoutsModalBranchId = null;
function openThirteenthPayoutsModal(id, currentMonths, legacyPayoutsPerYear) {
    thirteenthPayoutsModalBranchId = id;
    let initial = tpParseMonths(currentMonths);
    if (initial.length === 0) {
        // Fallback aus Legacy
        const v = Number(legacyPayoutsPerYear ?? 12);
        initial = v === 1 ? [12]
                : v === 2 ? [6, 12]
                : v === 4 ? [3, 6, 9, 12]
                : [1,2,3,4,5,6,7,8,9,10,11,12];
    }
    tpSetMonths(initial);
    document.getElementById('thirteenthPayoutsModal').style.display = 'flex';
}
function closeThirteenthPayoutsModal() {
    document.getElementById('thirteenthPayoutsModal').style.display = 'none';
    thirteenthPayoutsModalBranchId = null;
}
async function saveThirteenthPayouts() {
    if (!thirteenthPayoutsModalBranchId) return;
    if (_tpSelectedMonths.length === 0) {
        if (!confirm('Keine Auszahlungsmonate gewählt — 13. ML wird gar nicht ausbezahlt. Trotzdem speichern?')) return;
    }
    try {
        const res = await fetch(`/api/companyprofiles/${thirteenthPayoutsModalBranchId}/thirteenth-payouts`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ months: _tpSelectedMonths, payoutsPerYear: _tpSelectedMonths.length || 12 })
        });
        if (!res.ok) { alert('Fehler beim Speichern.'); return; }
        const csv = _tpSelectedMonths.join(',');
        const n   = _tpSelectedMonths.length || 12;
        // Lokale Kopien aktualisieren
        const b = allBranches.find(x => x.id === thirteenthPayoutsModalBranchId);
        if (b) { b.thirteenthMonthPayoutMonths = csv; b.thirteenthMonthPayoutsPerYear = n; }
        if (selectedCompanyProfile?.id === thirteenthPayoutsModalBranchId) {
            selectedCompanyProfile.thirteenthMonthPayoutMonths   = csv;
            selectedCompanyProfile.thirteenthMonthPayoutsPerYear = n;
        }
        if (selectedBranch?.id === thirteenthPayoutsModalBranchId) {
            selectedBranch.thirteenthMonthPayoutMonths   = csv;
            selectedBranch.thirteenthMonthPayoutsPerYear = n;
            renderFilialenDetail(selectedBranch);
        }
        closeThirteenthPayoutsModal();
        loadFilialen();
    } catch { alert('Verbindungsfehler.'); }
}

// ── Kommunaler Mindestlohn pro Filiale (Walter-Vorgabe 23.05.2026) ──────────
// Jahreslohn erfassen; Monat (÷13) und Stunde (÷52÷Wochenstunden) werden
// gerechnet. Versioniert (Generationen). Übersteuert L-GAV nach oben.
let _bmwBranch = null;
let _bmwWeekly = 42;

function bmwFmtDate(iso) {
    if (!iso) return '–';
    const s = String(iso).slice(0, 10);
    return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
}
function bmwFmtAmt(v) {
    return Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

// ── easy@work Auto-Sync An/Aus pro Filiale (Walter-Vorgabe 19.06.2026) ──────
let _eawAutoBranch = null;

async function eawAutoInit(branchId) {
    const el = document.getElementById('eawAutoBlock');
    if (!el) return;
    _eawAutoBranch = branchId;
    try {
        const r = await fetch(`/api/easywork/mappings/by-branch/${branchId}`, { headers: ah(), cache: 'no-store' });
        if (r.status === 404) {
            el.innerHTML = `<div style="font-size:12.5px;color:#64748b;padding:9px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:9px">
                Diese Filiale ist nicht mit easy@work verknüpft — der Auto-Sync ist hier nicht verfügbar.
                <span style="color:#94a3b8">(Verknüpfung anlegen im Bereich „easy@work API → Filial-Mappings".)</span></div>`;
            return;
        }
        if (!r.ok) { el.innerHTML = `<div style="color:#b91c1c;font-size:12px">Fehler beim Laden des Auto-Sync-Status.</div>`; return; }
        const m = await r.json();
        eawAutoRender(!!m.autoSyncEnabled, m.easyAtWorkCustomerId, m.syncState);
    } catch (e) {
        el.innerHTML = `<div style="color:#b91c1c;font-size:12px">Netzwerkfehler beim Laden.</div>`;
    }
}

function eawAutoRender(enabled, customerId, syncState) {
    const el = document.getElementById('eawAutoBlock');
    if (!el) return;
    const fmtDt = (iso) => { try { return new Date(iso).toLocaleString('de-CH', { day:'2-digit', month:'2-digit', year:'numeric', hour:'2-digit', minute:'2-digit' }); } catch(e){ return iso; } };
    let statusHtml;
    if (syncState && (syncState.lastSyncAt || syncState.lastError)) {
        const last = syncState.lastSyncAt ? fmtDt(syncState.lastSyncAt) : '–';
        const rows = (syncState.lastRowCount != null) ? syncState.lastRowCount : '–';
        statusHtml = `
            <div style="margin-top:8px;font-size:12.5px;color:#475569;display:flex;flex-direction:column;gap:3px">
                <div>Letzter Lauf: <strong>${last}</strong> · Änderungen: <strong>${rows}</strong></div>
                ${syncState.lastError ? `<div style="color:#b91c1c"><strong>⚠ Fehler:</strong> ${escapeHtml(syncState.lastError)}</div>` : ''}
                <div style="color:#94a3b8">Nächster automatischer Lauf: täglich um 05:00</div>
            </div>`;
    } else {
        statusHtml = `<div style="margin-top:8px;font-size:12.5px;color:#94a3b8">Noch kein Lauf protokolliert. Nächster automatischer Lauf: täglich um 05:00.</div>`;
    }
    el.innerHTML = `
        <div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap;padding:10px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:9px">
            <label style="display:flex;align-items:center;gap:8px;cursor:pointer;font-size:13px;color:#0f172a;flex:1;min-width:280px">
                <input type="checkbox" id="eawAutoToggle" ${enabled ? 'checked' : ''} onchange="eawAutoSave(this.checked)" style="width:16px;height:16px;cursor:pointer">
                <span><strong>Automatischer Sync aktiv</strong> — holt täglich um 05:00 die Stempelzeiten dieser Filiale aus easy@work (Customer ${customerId}).</span>
            </label>
            <span id="eawAutoState" style="font-size:12px;font-weight:700;color:${enabled ? '#16a34a' : '#94a3b8'}">${enabled ? 'EIN' : 'AUS'}</span>
        </div>
        ${statusHtml}`;
}

async function eawAutoSave(enabled) {
    const stateEl = document.getElementById('eawAutoState');
    try {
        const r = await fetch(`/api/easywork/mappings/${_eawAutoBranch}/auto-sync`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ enabled })
        });
        if (!r.ok) { alert('Speichern fehlgeschlagen.'); eawAutoInit(_eawAutoBranch); return; }
        if (stateEl) { stateEl.textContent = enabled ? 'EIN' : 'AUS'; stateEl.style.color = enabled ? '#16a34a' : '#94a3b8'; }
    } catch (e) {
        alert('Netzwerkfehler beim Speichern.');
        eawAutoInit(_eawAutoBranch);
    }
}

async function bmwInit(branchId) {
    _bmwBranch = branchId;
    const el = document.getElementById('bmwBlock');
    if (!el) return;
    el.innerHTML = '<div style="font-size:12px;color:#94a3b8">Wird geladen…</div>';
    try {
        const r = await fetch(`/api/branch-min-wage?companyProfileId=${branchId}`, { headers: ah() });
        if (!r.ok) {
            el.innerHTML = r.status === 403
                ? '<div style="font-size:12px;color:#92400e">Nur Admin/HR kann den kommunalen Mindestlohn verwalten.</div>'
                : `<div style="font-size:12px;color:#dc2626">Fehler beim Laden (HTTP ${r.status}).</div>`;
            return;
        }
        const d = await r.json();
        _bmwWeekly = d.weeklyHours || 42;
        bmwRender(d.items || []);
    } catch (e) {
        el.innerHTML = `<div style="font-size:12px;color:#dc2626">Fehler: ${e.message}</div>`;
    }
}

function bmwRender(items) {
    const el = document.getElementById('bmwBlock');
    if (!el) return;
    const weekly = _bmwWeekly;
    const rows = items.map(it => {
        const youth = it.appliesToYouth ? 'Ja' : 'Nein';
        return `<tr style="border-bottom:1px solid #f1f5f9">
            <td style="padding:6px 10px;text-align:right;font-weight:600">${bmwFmtAmt(it.annualSalary)}</td>
            <td style="padding:6px 10px;text-align:right;color:#475569">${bmwFmtAmt(it.monthly)}</td>
            <td style="padding:6px 10px;text-align:right;color:#475569">${bmwFmtAmt(it.hourly)}</td>
            <td style="padding:6px 10px;text-align:center">${youth}</td>
            <td style="padding:6px 10px">${bmwFmtDate(it.validFrom)}</td>
            <td style="padding:6px 10px">${it.validTo ? bmwFmtDate(it.validTo) : 'offen'}</td>
            <td style="padding:6px 10px;text-align:right"><button class="btn btn-outline" style="font-size:11px;padding:3px 8px;color:#dc2626" onclick="bmwDelete(${it.id})">Löschen</button></td>
        </tr>`;
    }).join('');

    const table = items.length ? `
        <table style="width:100%;border-collapse:collapse;font-size:12.5px;margin-bottom:10px">
            <thead><tr style="color:#94a3b8;font-size:10.5px;text-transform:uppercase;letter-spacing:.03em;border-bottom:1px solid #e2e8f0">
                <th style="padding:4px 10px;text-align:right">Jahreslohn</th>
                <th style="padding:4px 10px;text-align:right">Monat (÷13)</th>
                <th style="padding:4px 10px;text-align:right">Std (÷52÷${weekly})</th>
                <th style="padding:4px 10px;text-align:center">Jugend</th>
                <th style="padding:4px 10px;text-align:left">Gültig ab</th>
                <th style="padding:4px 10px;text-align:left">Gültig bis</th>
                <th></th>
            </tr></thead><tbody>${rows}</tbody>
        </table>`
        : '<div style="font-size:12px;color:#64748b;margin-bottom:10px">Kein kommunaler Mindestlohn erfasst — diese Filiale verwendet den L-GAV-Mindestlohn.</div>';

    const form = `
        <div style="display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap;background:#f8fafc;border:1px solid #e2e8f0;border-radius:9px;padding:10px 12px">
            <div><div style="font-size:11px;color:#64748b;margin-bottom:2px">Jahreslohn (CHF)</div>
                <input type="number" id="bmwAnnual" class="ef-input" style="width:120px" min="0" step="100" placeholder="48000" oninput="bmwUpdateHint()"></div>
            <div><div style="font-size:11px;color:#64748b;margin-bottom:2px">Gültig ab</div>
                <input type="date" id="bmwValidFrom" class="ef-input" style="width:150px"></div>
            <label style="font-size:12px;color:#475569;display:flex;align-items:center;gap:6px;margin-bottom:7px">
                <input type="checkbox" id="bmwYouth"> auch für Jugendliche</label>
            <button class="btn btn-primary" style="font-size:12px;padding:6px 14px" onclick="bmwAdd()">+ Version hinzufügen</button>
            <div id="bmwCalcHint" style="font-size:11.5px;color:#6b6152;flex-basis:100%"></div>
        </div>`;

    el.innerHTML = table + form;
}

function bmwUpdateHint() {
    const a = parseFloat(document.getElementById('bmwAnnual')?.value);
    const h = document.getElementById('bmwCalcHint');
    if (!h) return;
    if (isNaN(a) || a <= 0) { h.textContent = ''; return; }
    const m = a / 13, hr = a / 52 / _bmwWeekly;
    h.textContent = `→ ergibt Monatslohn ${bmwFmtAmt(m)} (÷13) · Stundenlohn ${bmwFmtAmt(hr)} (÷52÷${_bmwWeekly})`;
}

async function bmwAdd() {
    const annual = parseFloat(document.getElementById('bmwAnnual')?.value);
    const vf     = document.getElementById('bmwValidFrom')?.value;
    const youth  = document.getElementById('bmwYouth')?.checked || false;
    if (isNaN(annual) || annual <= 0) { showToast('Jahreslohn fehlt oder ungültig.', 'error'); return; }
    if (!vf) { showToast('Gültig-ab-Datum fehlt.', 'error'); return; }
    try {
        const r = await fetch('/api/branch-min-wage', {
            method: 'POST', headers: ah(),
            body: JSON.stringify({ companyProfileId: _bmwBranch, annualSalary: annual, appliesToYouth: youth, validFrom: vf })
        });
        const d = await r.json().catch(() => ({}));
        if (!r.ok) { showToast(d.error || ('Fehler (HTTP ' + r.status + ')'), 'error'); return; }
        showToast('Kommunaler Mindestlohn erfasst.', 'success');
        bmwInit(_bmwBranch);
    } catch (e) { showToast('Fehler: ' + e.message, 'error'); }
}

async function bmwDelete(id) {
    if (!confirm('Diese Version des kommunalen Mindestlohns löschen?')) return;
    try {
        const r = await fetch('/api/branch-min-wage/' + id, { method: 'DELETE', headers: ah() });
        if (!r.ok && r.status !== 204) { showToast('Löschen fehlgeschlagen (HTTP ' + r.status + ')', 'error'); return; }
        showToast('Gelöscht.', 'success');
        bmwInit(_bmwBranch);
    } catch (e) { showToast('Fehler: ' + e.message, 'error'); }
}

// ══════════════════════════════════════════════════════════════════════
// FILIAL-DOKUMENTE (Walter-Vorgabe 06.08.2026) — Tab «Dokumente» im
// Filial-Detail. Backend: CompanyDokumenteController (api/company-dokumente).
// Zugriff: admin ODER AppUser.CanCompanyDokumente (AuthController.Me liefert
// `canCompanyDokumente`); das Backend prüft zusätzlich user_branch_access.
// ⋮-Menü-Standard: dok-menu-btn + dokToggleMenu/dokCloseAllMenus (documents.js).
// ══════════════════════════════════════════════════════════════════════

// Kategorie-Codes → Labels. Backend-Pendant:
// CompanyDokumenteController.Kategorien — bei Änderungen BEIDE Stellen pflegen.
const CDOK_KATEGORIEN = [
    ['VERSICHERUNG', 'Versicherungen'],
    ['AHV_SV',       'AHV / Sozialversicherungen'],
    ['QST',          'Quellensteuer'],
    ['VERTRAEGE',    'Verträge & Behörden'],
    ['SONSTIGES',    'Sonstiges'],
];

let _filDoksBranchId = null;   // Filiale, deren Dokumente gerade geladen sind
let _filDoksCache    = [];     // letzte Server-Liste (GET /api/company-dokumente)
let _filDoksFilter   = '';     // '' = Alle, sonst Kategorie-Code

function cdokCanSee() {
    return typeof currentUser !== 'undefined' && !!currentUser
        && (currentUser.role === 'admin' || !!currentUser.canCompanyDokumente);
}

function cdokEsc(s) {
    return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;')
        .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function cdokLabel(code) {
    const hit = CDOK_KATEGORIEN.find(k => k[0] === code);
    return hit ? hit[1] : (code || 'Sonstiges');
}

async function filDoksLoad(branchId) {
    _filDoksBranchId = branchId;
    const el = document.getElementById('filDoksList');
    if (!el) return;
    el.innerHTML = '<div style="color:#8b8b8b;padding:16px;text-align:center;font-size:12px">Wird geladen…</div>';
    try {
        const res = await fetch('/api/company-dokumente?companyProfileId=' + branchId, { headers: ah() });
        if (!res.ok) {
            let msg = 'Fehler beim Laden (HTTP ' + res.status + ').';
            try { const d = await res.json(); msg = d.message || d.error || msg; } catch (_) { /* Text bleibt */ }
            el.innerHTML = `<div style="color:#b91c1c;padding:16px;font-size:13px">${cdokEsc(msg)}</div>`;
            return;
        }
        _filDoksCache = await res.json();
        filDoksRender();
    } catch (_) {
        el.innerHTML = '<div style="color:#b91c1c;padding:16px;font-size:13px">Verbindungsfehler.</div>';
    }
}

function filDoksSetFilter(code) {
    _filDoksFilter = code;
    filDoksRender();
}

function filDoksRender() {
    // Kategorie-Filter als ruhige Liquid-Pills (aktiv = Kohle, sonst Glas).
    const bar = document.getElementById('filDoksFilterBar');
    if (bar) {
        const pill = (code, label) => {
            const active = _filDoksFilter === code;
            return `<button onclick="filDoksSetFilter('${code}')" style="border:1px solid ${active ? '#3f3f3f' : 'rgba(60,55,48,0.18)'};background:${active ? '#3f3f3f' : 'rgba(255,255,255,0.55)'};color:${active ? '#fff' : '#646464'};border-radius:999px;padding:4px 12px;font-size:12px;font-weight:600;cursor:pointer">${label}</button>`;
        };
        bar.innerHTML = pill('', 'Alle') + CDOK_KATEGORIEN.map(k => pill(k[0], k[1])).join('');
    }

    const el = document.getElementById('filDoksList');
    if (!el) return;
    const docs = _filDoksFilter
        ? _filDoksCache.filter(d => d.kategorie === _filDoksFilter)
        : _filDoksCache;
    if (!docs.length) {
        el.innerHTML = '<div style="color:#8b8b8b;padding:26px;text-align:center;font-size:13px">Noch keine Dokumente — mit ‹+ Hochladen› beginnen.</div>';
        return;
    }

    const isAdmin = typeof currentUser !== 'undefined' && currentUser?.role === 'admin';
    // Gruppiert nach Kategorie in CDOK-Reihenfolge; unbekannte Codes zuletzt.
    const known = CDOK_KATEGORIEN.map(k => k[0]);
    const extra = [...new Set(docs.map(d => d.kategorie).filter(c => !known.includes(c)))];
    let html = '';
    for (const cat of [...known, ...extra]) {
        const items = docs.filter(d => d.kategorie === cat);
        if (!items.length) continue;
        html += `<div style="font-size:12px;font-weight:700;color:#646464;text-transform:uppercase;letter-spacing:.4px;margin:14px 0 4px">${cdokEsc(cdokLabel(cat))}</div>`;
        html += items.map(d => filDoksRowHtml(d, isAdmin)).join('');
    }
    el.innerHTML = html;
}

function filDoksRowHtml(d, isAdmin) {
    const name  = d.originalFilename || 'dokument';
    const isPdf = /\.pdf$/i.test(name);
    const dt    = d.createdAt ? new Date(d.createdAt).toLocaleDateString('de-CH') : '–';
    return `
    <div style="display:flex;align-items:center;gap:12px;padding:9px 4px;border-bottom:1px solid rgba(60,55,48,0.08)">
        <div style="flex:1;min-width:0">
            <!-- Bemerkung INLINE hinter dem Namen (Walter 06.08.2026) -->
            <div style="font-size:13px;color:#3f3f3f;cursor:pointer;overflow:hidden;text-overflow:ellipsis;white-space:nowrap" onclick="filDoksPreview(${d.id})" title="Ansehen">
                <span style="font-weight:600">${cdokEsc(name)}</span>${d.bemerkung ? ` <span style="font-weight:400;color:#8b8b8b;font-size:12px">— ${cdokEsc(d.bemerkung)}</span>` : ''}
            </div>
        </div>
        <div style="flex-shrink:0;text-align:right">
            <div style="font-size:12px;color:#646464">${dt}</div>
            ${d.uploadedByName ? `<div style="font-size:11px;color:#8b8b8b">${cdokEsc(d.uploadedByName)}</div>` : ''}
        </div>
        <div style="position:relative;display:inline-block;flex-shrink:0">
            <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'cdok-${d.id}')" title="Aktionen">⋮</button>
            <div class="dok-menu" id="dokMenu-cdok-${d.id}">
                <button class="dok-menu-item" onclick="dokCloseAllMenus();filDoksPreview(${d.id})">Ansehen</button>
                <button class="dok-menu-item" onclick="dokCloseAllMenus();filDoksDownload(${d.id})">Herunterladen</button>
                ${isPdf ? `<button class="dok-menu-item" onclick="dokCloseAllMenus();filDoksRotate(${d.id})">PDF drehen ↻</button>` : ''}
                ${isAdmin ? `<button class="dok-menu-item danger" onclick="dokCloseAllMenus();filDoksDelete(${d.id})">Löschen</button>` : ''}
            </div>
        </div>
    </div>`;
}

// Vorschau: PDF/Bilder direkt via /preview; alles andere (Word/Excel/…)
// serverseitig nach PDF gewandelt via /preview-pdf (LibreOffice).
function filDoksPreview(id) {
    const d = _filDoksCache.find(x => x.id === id);
    if (!d) return;
    const name = d.originalFilename || 'dokument';
    const direct = /\.(pdf|png|jpg|jpeg|webp|heic)$/i.test(name);
    const url = '/api/company-dokumente/' + id + (direct ? '/preview' : '/preview-pdf');
    previewUrlFetch(url, name, ah());
}

async function filDoksDownload(id) {
    const d = _filDoksCache.find(x => x.id === id);
    if (!d) return;
    try {
        const res = await fetch('/api/company-dokumente/' + id + '/download', { headers: ah() });
        if (!res.ok) {
            showToast('Download fehlgeschlagen (HTTP ' + res.status + ').', 'error');
            return;
        }
        const blob = await res.blob();
        await saveBlobAsk(blob, d.originalFilename || 'dokument');
    } catch (_) { showToast('Verbindungsfehler beim Download.', 'error'); }
}

async function filDoksRotate(id) {
    try {
        const res = await fetch('/api/company-dokumente/' + id + '/rotate?deg=90', {
            method: 'POST', headers: ah()
        });
        if (!res.ok) {
            let msg = 'Drehen fehlgeschlagen (HTTP ' + res.status + ').';
            try { const d = await res.json(); msg = d.message || d.error || msg; } catch (_) { /* Text bleibt */ }
            showToast(msg, 'error');
            return;
        }
        showToast('PDF um 90° gedreht.', 'success');
        await filDoksLoad(_filDoksBranchId);
        // Ist das Vorschaufenster gerade offen, gedrehte Version neu laden.
        const pm = document.getElementById('filePreviewModal');
        if (pm && pm.style.display && pm.style.display !== 'none') filDoksPreview(id);
    } catch (_) { showToast('Verbindungsfehler beim Drehen.', 'error'); }
}

async function filDoksDelete(id) {
    const d = _filDoksCache.find(x => x.id === id);
    if (!d) return;
    const ok = await liquidConfirm(
        `Dokument «${d.originalFilename || 'dokument'}» wirklich löschen?`,
        { title: 'Dokument löschen', yesLabel: 'Löschen', noLabel: 'Abbrechen' });
    if (!ok) return;
    try {
        const res = await fetch('/api/company-dokumente/' + id, { method: 'DELETE', headers: ah() });
        if (!res.ok && res.status !== 204) {
            showToast('Löschen fehlgeschlagen (HTTP ' + res.status + ').', 'error');
            return;
        }
        showToast('Dokument gelöscht.', 'success');
        await filDoksLoad(_filDoksBranchId);
    } catch (_) { showToast('Verbindungsfehler beim Löschen.', 'error'); }
}

// ── Upload-Modal (Liquid-Glass: Off-White-Karte, Kohle-Primärbutton) ────
function filDoksOpenUpload() {
    if (document.getElementById('cdokUploadModal')) return;
    const opts = CDOK_KATEGORIEN.map(k => `<option value="${k[0]}">${k[1]}</option>`).join('');
    const label = 'font-size:11.5px;font-weight:600;color:#8b8b8b;text-transform:uppercase;letter-spacing:.4px;margin-bottom:4px';
    const html = `
    <div id="cdokUploadModal" style="position:fixed;inset:0;background:rgba(60,55,48,0.35);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px"
         onclick="if(event.target===this)filDoksCloseUpload()">
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 18px 48px rgba(60,55,48,0.22);width:100%;max-width:440px;padding:22px 24px">
            <div style="font-size:16px;font-weight:700;color:#3f3f3f;margin-bottom:16px">Dokument hochladen</div>
            <div style="margin-bottom:12px">
                <div style="${label}">Datei</div>
                <input type="file" id="cdokFile" style="font-size:13px;color:#3f3f3f;width:100%">
            </div>
            <div style="margin-bottom:12px">
                <div style="${label}">Kategorie</div>
                <select id="cdokKategorie" class="ef-input">${opts}</select>
            </div>
            <div style="margin-bottom:12px">
                <div style="${label}">Bemerkung</div>
                <input type="text" id="cdokBemerkung" class="ef-input" placeholder="optional, z.B. Police 2026">
            </div>
            <div id="cdokUploadErr" style="display:none;color:#b91c1c;font-size:12.5px;margin-bottom:10px"></div>
            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:16px">
                <button onclick="filDoksCloseUpload()" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);color:#646464;border-radius:12px;padding:7px 16px;font-size:12.5px;font-weight:600;cursor:pointer">Abbrechen</button>
                <button id="cdokUploadBtn" onclick="filDoksSubmitUpload()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 18px;font-size:12.5px;font-weight:600;cursor:pointer;box-shadow:0 2px 8px rgba(60,55,48,0.18)">Hochladen</button>
            </div>
        </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

function filDoksCloseUpload() {
    document.getElementById('cdokUploadModal')?.remove();
}

async function filDoksSubmitUpload() {
    const errEl = document.getElementById('cdokUploadErr');
    const showErr = (m) => { if (errEl) { errEl.textContent = m; errEl.style.display = 'block'; } };
    if (errEl) errEl.style.display = 'none';

    const file = document.getElementById('cdokFile')?.files?.[0];
    if (!file) { showErr('Bitte eine Datei wählen.'); return; }
    if (file.size > 20 * 1024 * 1024) { showErr('Datei zu gross (max. 20 MB).'); return; }
    if (!_filDoksBranchId) { showErr('Keine Filiale gewählt.'); return; }

    const fd = new FormData();
    fd.append('file', file, file.name || 'dokument');
    fd.append('companyProfileId', String(_filDoksBranchId));
    fd.append('kategorie', document.getElementById('cdokKategorie')?.value || 'SONSTIGES');
    const bem = (document.getElementById('cdokBemerkung')?.value || '').trim();
    if (bem) fd.append('bemerkung', bem);

    const btn = document.getElementById('cdokUploadBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Lädt hoch…'; }
    try {
        // Kein ah() — das setzt Content-Type: application/json und bricht
        // FormData-Multipart. Nur der Bearer-Header (Muster users.js).
        const res = await fetch('/api/company-dokumente/upload', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd
        });
        if (!res.ok) {
            let msg = 'Hochladen fehlgeschlagen (HTTP ' + res.status + ').';
            try { const d = await res.json(); msg = d.message || d.error || msg; } catch (_) { /* Text bleibt */ }
            showErr(msg);
            return;
        }
        filDoksCloseUpload();
        showToast('Dokument hochgeladen.', 'success');
        await filDoksLoad(_filDoksBranchId);
    } catch (_) {
        showErr('Verbindungsfehler beim Hochladen.');
    } finally {
        const btn2 = document.getElementById('cdokUploadBtn');
        if (btn2) { btn2.disabled = false; btn2.textContent = 'Hochladen'; }
    }
}


// ══════════════════════════════════════════════════════════════════════
//  LOHNDATENEMPFÄNGER (Walter-Vorgabe 06.08.2026, Mirus-Vorbild)
//  Zentraler Katalog (Adresse/Kassennummer EINMAL) + Zuordnung pro
//  Filiale (Mitglied-/Subnummer). Gepflegt komplett aus diesem Tab.
// ══════════════════════════════════════════════════════════════════════
let _cpEmpfBranchId = null;
let _cpEmpfCache    = [];   // Zuordnungen dieser Filiale
let _cpEmpfKatalog  = [];   // globaler Empfänger-Katalog

const CP_EMPF_ARTEN = [
    ['AUSGLEICHSKASSE', 'Ausgleichskasse (AHV)'],
    ['FAK',             'Familienausgleichskasse (FAK)'],
    ['KTG',             'KTG-Versicherung'],
    ['UVG',             'UVG-Versicherung'],
    ['BVG',             'BVG / Pensionskasse'],
    ['QST',             'Quellensteuer'],
    ['LOHNAUSWEIS',     'Lohnausweis'],
    ['ANDERE',          'Andere'],
];
function cpEmpfArtLabel(code) {
    const f = CP_EMPF_ARTEN.find(a => a[0] === code);
    return f ? f[1] : (code || 'Andere');
}

async function cpEmpfLoad(branchId) {
    _cpEmpfBranchId = branchId;
    const el = document.getElementById('cpEmpfList');
    if (!el) return;
    try {
        const [zRes, kRes] = await Promise.all([
            fetch(`/api/companyprofiles/${branchId}/empfaenger`, { headers: ah(), cache: 'no-store' }),
            fetch('/api/lohndaten-empfaenger', { headers: ah(), cache: 'no-store' }),
        ]);
        if (!zRes.ok || !kRes.ok) {
            el.innerHTML = '<div style="color:#991b1b;padding:16px;font-size:12.5px">Laden fehlgeschlagen.</div>';
            return;
        }
        _cpEmpfCache   = await zRes.json();
        _cpEmpfKatalog = await kRes.json();
        cpEmpfRender();
    } catch (_) {
        el.innerHTML = '<div style="color:#991b1b;padding:16px;font-size:12.5px">Verbindungsfehler.</div>';
    }
}

function cpEmpfRender() {
    const el = document.getElementById('cpEmpfList');
    if (!el) return;
    if (!_cpEmpfCache.length) {
        el.innerHTML = '<div style="color:#8b8b8b;padding:26px;text-align:center;font-size:13px">Noch keine Empfänger zugeordnet — mit ‹+ Hinzufügen› beginnen.</div>';
        return;
    }
    let html = '';
    for (const [code] of CP_EMPF_ARTEN) {
        const items = _cpEmpfCache.filter(z => z.art === code);
        if (!items.length) continue;
        html += `<div style="font-size:12px;font-weight:700;color:#646464;text-transform:uppercase;letter-spacing:.4px;margin:14px 0 4px">${cdokEsc(cpEmpfArtLabel(code))}</div>`;
        html += items.map(cpEmpfRowHtml).join('');
    }
    // unbekannte Arten zuletzt
    const known = CP_EMPF_ARTEN.map(a => a[0]);
    const rest = _cpEmpfCache.filter(z => !known.includes(z.art));
    if (rest.length) {
        html += `<div style="font-size:12px;font-weight:700;color:#646464;margin:14px 0 4px">WEITERE</div>`;
        html += rest.map(cpEmpfRowHtml).join('');
    }
    el.innerHTML = html;
}

function cpEmpfRowHtml(z) {
    const adr = [z.strasse, z.postfach, [z.plz, z.ort].filter(Boolean).join(' ')]
        .filter(s => s && s.trim()).join(' · ');
    const nums = [
        z.kassennummer  ? `Kasse ${cdokEsc(z.kassennummer)}` : null,
        z.mitgliednummer ? `Mitglied ${cdokEsc(z.mitgliednummer)}` : null,
        z.subnummer     ? `Sub ${cdokEsc(z.subnummer)}` : null,
    ].filter(Boolean).join(' · ');
    return `
    <div style="display:flex;align-items:center;gap:12px;padding:9px 4px;border-bottom:1px solid rgba(60,55,48,0.08)">
        <div style="flex:1;min-width:0">
            <div style="font-size:13px;color:#3f3f3f;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">
                <span style="font-weight:600">${cdokEsc(z.bezeichnung)}</span>${z.zusatz ? ` <span style="font-weight:400;color:#8b8b8b;font-size:12px">— ${cdokEsc(z.zusatz)}</span>` : ''}${z.kantonCode ? ` <span style="font-weight:600;color:#6b7280;font-size:11px;border:1px solid rgba(60,55,48,0.18);border-radius:6px;padding:1px 6px;margin-left:4px">${cdokEsc(z.kantonCode)}</span>` : ''}
            </div>
            <div style="font-size:11.5px;color:#8b8b8b;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${cdokEsc(adr) || '–'}</div>
        </div>
        <div style="flex-shrink:0;text-align:right;font-size:12px;color:#646464">${nums || '<span style="color:#b45309">Mitgliednummer fehlt</span>'}</div>
        <div style="position:relative;display:inline-block;flex-shrink:0">
            <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'cpempf-${z.id}')" title="Aktionen">⋮</button>
            <div class="dok-menu" id="dokMenu-cpempf-${z.id}">
                <button class="dok-menu-item" onclick="dokCloseAllMenus();cpEmpfOpenModal(${z.id})">Bearbeiten</button>
                <button class="dok-menu-item danger" onclick="dokCloseAllMenus();cpEmpfDelete(${z.id})">Aus Filiale entfernen</button>
            </div>
        </div>
    </div>`;
}

// ── Modal (dynamisch, Liquid-Glass) ────────────────────────────────────
function cpEmpfEnsureModal() {
    if (document.getElementById('cpEmpfModal')) return;
    const inp = 'width:100%;margin-top:3px;padding:7px 10px;border:1px solid rgba(60,55,48,0.18);border-radius:8px;font-size:13px;background:#fff;box-sizing:border-box;font-family:inherit;color:#3f3f3f';
    const lbl = 'display:block;font-size:11.5px;font-weight:600;color:#8b8b8b';
    const div = document.createElement('div');
    div.id = 'cpEmpfModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;z-index:320;background:rgba(40,36,30,0.38);backdrop-filter:blur(2px)';
    div.innerHTML = `
    <div style="position:absolute;top:50%;left:50%;transform:translate(-50%,-50%);width:min(680px,94vw);max-height:92vh;overflow:auto;background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 25px 60px rgba(60,55,48,0.22);padding:22px 24px">
        <div id="cpEmpfModalTitle" style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:14px">Empfänger hinzufügen</div>

        <div id="cpEmpfPickBlock" style="margin-bottom:14px">
            <label style="${lbl}">Empfänger
                <select id="cpEmpfPick" onchange="cpEmpfPickChanged()" style="${inp}"></select>
            </label>
        </div>

        <div id="cpEmpfKatalogBlock" style="border:1px solid rgba(60,55,48,0.12);border-radius:12px;padding:14px;background:rgba(255,255,255,0.45);margin-bottom:14px">
            <div style="font-size:12px;font-weight:700;color:#646464;margin-bottom:8px">Empfänger-Stammdaten <span style="font-weight:400;color:#8b8b8b">(zentral — gelten für alle Filialen)</span></div>
            <div style="display:grid;grid-template-columns:1fr 2fr;gap:10px 12px">
                <label style="${lbl}">Art
                    <select id="cpEmpfArt" style="${inp}">${CP_EMPF_ARTEN.map(a => `<option value="${a[0]}">${a[1]}</option>`).join('')}</select>
                </label>
                <label style="${lbl}">Bezeichnung<input id="cpEmpfBez" style="${inp}"></label>
                <label style="${lbl}">Zusatz<input id="cpEmpfZusatz" style="${inp}"></label>
                <label style="${lbl}">Nummer der Kasse<input id="cpEmpfKassenNr" style="${inp}"></label>
                <label style="${lbl};grid-column:span 2">Strasse<input id="cpEmpfStrasse" style="${inp}"></label>
                <label style="${lbl}">Postfach<input id="cpEmpfPostfach" style="${inp}"></label>
                <label style="${lbl}">PLZ / Ort
                    <div style="display:flex;gap:8px">
                        <input id="cpEmpfPlz" style="${inp};width:80px;flex:none">
                        <input id="cpEmpfOrt" style="${inp}">
                    </div>
                </label>
                <label style="${lbl}">Kanton (bei QST)<input id="cpEmpfKanton" placeholder="AG, BE, …" maxlength="2" style="${inp}"></label>
                <label style="${lbl}">Support-E-Mail<input id="cpEmpfMail" style="${inp}"></label>
            </div>
        </div>

        <div style="border:1px solid rgba(60,55,48,0.12);border-radius:12px;padding:14px;background:rgba(255,255,255,0.45)">
            <div style="font-size:12px;font-weight:700;color:#646464;margin-bottom:8px">Angaben dieser Filiale</div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 12px">
                <label style="${lbl}">Mitgliednummer<input id="cpEmpfMitglied" placeholder="z.B. 629.0714.00" style="${inp}"></label>
                <label style="${lbl}">Subnummer<input id="cpEmpfSub" style="${inp}"></label>
                <label style="${lbl};grid-column:span 2">Bemerkung<input id="cpEmpfBem" style="${inp}"></label>
            </div>
        </div>

        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:18px">
            <button onclick="cpEmpfCloseModal()" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);color:#646464;border-radius:999px;padding:8px 18px;font-size:13px;font-weight:600;cursor:pointer">Abbrechen</button>
            <button id="cpEmpfSaveBtn" onclick="cpEmpfSave()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 20px;font-size:13px;font-weight:600;cursor:pointer;box-shadow:0 2px 8px rgba(60,55,48,0.2)">Speichern</button>
        </div>
    </div>`;
    div.addEventListener('click', (e) => { if (e.target === div) cpEmpfCloseModal(); });
    document.body.appendChild(div);
}

let _cpEmpfEditId = null;   // Zuordnungs-Id beim Bearbeiten, null = neu

function cpEmpfOpenModal(zuordnungId) {
    cpEmpfEnsureModal();
    _cpEmpfEditId = zuordnungId;
    const modal = document.getElementById('cpEmpfModal');
    const pick  = document.getElementById('cpEmpfPick');
    const title = document.getElementById('cpEmpfModalTitle');
    const set = (id, v) => { const el = document.getElementById(id); if (el) el.value = v ?? ''; };

    if (zuordnungId == null) {
        // NEU: Auswahl bestehender Empfänger (ohne bereits zugeordnete) + «Neu erfassen»
        title.textContent = 'Empfänger hinzufügen';
        document.getElementById('cpEmpfPickBlock').style.display = '';
        const zugeordnet = new Set(_cpEmpfCache.map(z => z.empfaengerId));
        const frei = _cpEmpfKatalog.filter(k => !zugeordnet.has(k.id));
        pick.innerHTML = '<option value="NEW">+ Neuen Empfänger erfassen…</option>'
            + frei.map(k => `<option value="${k.id}">${cdokEsc(cpEmpfArtLabel(k.art))} — ${cdokEsc(k.bezeichnung)}${k.kantonCode ? ' (' + cdokEsc(k.kantonCode) + ')' : ''}</option>`).join('');
        pick.value = frei.length ? String(frei[0].id) : 'NEW';
        ['cpEmpfMitglied','cpEmpfSub','cpEmpfBem'].forEach(id => set(id, ''));
        cpEmpfPickChanged();
    } else {
        // BEARBEITEN: Zuordnung + zentrale Stammdaten in einem Formular
        const z = _cpEmpfCache.find(x => x.id === zuordnungId);
        if (!z) return;
        title.textContent = 'Empfänger bearbeiten';
        document.getElementById('cpEmpfPickBlock').style.display = 'none';
        cpEmpfFillKatalog({
            art: z.art, bezeichnung: z.bezeichnung, zusatz: z.zusatz,
            kassennummer: z.kassennummer, strasse: z.strasse, postfach: z.postfach,
            plz: z.plz, ort: z.ort, kantonCode: z.kantonCode, supportEmail: z.supportEmail,
        }, false);
        set('cpEmpfMitglied', z.mitgliednummer);
        set('cpEmpfSub', z.subnummer);
        set('cpEmpfBem', z.bemerkung);
    }
    modal.style.display = 'block';
}

function cpEmpfPickChanged() {
    const v = document.getElementById('cpEmpfPick')?.value;
    if (v === 'NEW') {
        cpEmpfFillKatalog({ art: 'AUSGLEICHSKASSE' }, true);
    } else {
        const k = _cpEmpfKatalog.find(x => String(x.id) === v);
        if (k) cpEmpfFillKatalog(k, false);
    }
}

// readonly=false → Felder editierbar (zentrale Änderung wird mitgespeichert)
function cpEmpfFillKatalog(k, isNew) {
    const set = (id, v) => { const el = document.getElementById(id); if (el) el.value = v ?? ''; };
    set('cpEmpfArt', k.art || 'ANDERE');
    set('cpEmpfBez', k.bezeichnung);
    set('cpEmpfZusatz', k.zusatz);
    set('cpEmpfKassenNr', k.kassennummer);
    set('cpEmpfStrasse', k.strasse);
    set('cpEmpfPostfach', k.postfach);
    set('cpEmpfPlz', k.plz);
    set('cpEmpfOrt', k.ort);
    set('cpEmpfKanton', k.kantonCode);
    set('cpEmpfMail', k.supportEmail);
}

function cpEmpfCloseModal() {
    const m = document.getElementById('cpEmpfModal');
    if (m) m.style.display = 'none';
}

async function cpEmpfSave() {
    const val = (id) => document.getElementById(id)?.value?.trim() || null;
    const btn = document.getElementById('cpEmpfSaveBtn');
    const katalogBody = {
        art: val('cpEmpfArt'), bezeichnung: val('cpEmpfBez'), zusatz: val('cpEmpfZusatz'),
        kassennummer: val('cpEmpfKassenNr'), strasse: val('cpEmpfStrasse'),
        postfach: val('cpEmpfPostfach'), plz: val('cpEmpfPlz'), ort: val('cpEmpfOrt'),
        kantonCode: val('cpEmpfKanton'), supportEmail: val('cpEmpfMail'),
    };
    if (!katalogBody.bezeichnung) { showToast('Bezeichnung fehlt.', 'error'); return; }
    const zuordnungFields = {
        mitgliednummer: val('cpEmpfMitglied'),
        subnummer: val('cpEmpfSub'),
        bemerkung: val('cpEmpfBem'),
    };
    if (btn) btn.disabled = true;
    try {
        if (_cpEmpfEditId == null) {
            // Neu: ggf. Katalog-Empfänger anlegen, dann Zuordnung
            const pickVal = document.getElementById('cpEmpfPick')?.value;
            let empfId;
            if (pickVal === 'NEW') {
                const r = await fetch('/api/lohndaten-empfaenger', {
                    method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
                    body: JSON.stringify(katalogBody),
                });
                if (!r.ok) { showToast('Empfänger anlegen fehlgeschlagen.', 'error'); return; }
                empfId = (await r.json()).id;
            } else {
                empfId = parseInt(pickVal, 10);
                // Zentrale Stammdaten wurden evtl. angepasst → mitspeichern
                await fetch('/api/lohndaten-empfaenger/' + empfId, {
                    method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' },
                    body: JSON.stringify(katalogBody),
                });
            }
            const r2 = await fetch(`/api/companyprofiles/${_cpEmpfBranchId}/empfaenger`, {
                method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify({ empfaengerId: empfId, ...zuordnungFields }),
            });
            if (!r2.ok) {
                let msg = 'Zuordnung fehlgeschlagen.';
                try { const j = await r2.json(); if (j.error === 'EMPFAENGER_BEREITS_ZUGEORDNET') msg = 'Dieser Empfänger ist der Filiale bereits zugeordnet.'; } catch (_) {}
                showToast(msg, 'error'); return;
            }
        } else {
            // Bearbeiten: zentrale Stammdaten + Zuordnung parallel speichern
            const z = _cpEmpfCache.find(x => x.id === _cpEmpfEditId);
            if (!z) return;
            const [r1, r2] = await Promise.all([
                fetch('/api/lohndaten-empfaenger/' + z.empfaengerId, {
                    method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' },
                    body: JSON.stringify(katalogBody),
                }),
                fetch(`/api/companyprofiles/${_cpEmpfBranchId}/empfaenger/${z.id}`, {
                    method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' },
                    body: JSON.stringify({ empfaengerId: z.empfaengerId, ...zuordnungFields }),
                }),
            ]);
            if (!r1.ok || !r2.ok) { showToast('Speichern fehlgeschlagen.', 'error'); return; }
        }
        cpEmpfCloseModal();
        showToast('Gespeichert.', 'success');
        await cpEmpfLoad(_cpEmpfBranchId);
    } catch (_) {
        showToast('Verbindungsfehler.', 'error');
    } finally {
        if (btn) btn.disabled = false;
    }
}

async function cpEmpfDelete(zuordnungId) {
    const z = _cpEmpfCache.find(x => x.id === zuordnungId);
    if (!z) return;
    const ok = await liquidConfirm(
        `Empfänger «${z.bezeichnung}» aus dieser Filiale entfernen? Der zentrale Empfänger bleibt bestehen.`,
        { title: 'Empfänger entfernen', yesLabel: 'Entfernen', noLabel: 'Abbrechen' });
    if (!ok) return;
    try {
        const r = await fetch(`/api/companyprofiles/${_cpEmpfBranchId}/empfaenger/${zuordnungId}`, {
            method: 'DELETE', headers: ah(),
        });
        if (!r.ok) { showToast('Entfernen fehlgeschlagen.', 'error'); return; }
        showToast('Entfernt.', 'success');
        await cpEmpfLoad(_cpEmpfBranchId);
    } catch (_) { showToast('Verbindungsfehler.', 'error'); }
}
