// ══════════════════════════════════════════════════════════════════════
// contracts-edit.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════
// VERTRAG LÖSCHEN (admin / superuser)
// ══════════════════════════════════════════════
async function deleteContract(employeeId, contractId, startDateIso) {
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    if (currentUser?.role !== 'admin' && currentUser?.role !== 'superuser') {
        alert(_t('vt.err.notAuthorized'));
        return;
    }
    const startStr = startDateIso ? new Date(startDateIso).toLocaleDateString('de-CH') : '–';
    if (!confirm(_t('vt.err.confirmDelete', { date: startStr }))) return;

    async function callDelete(force) {
        const url = `/api/employments/${contractId}` + (force ? '?force=true' : '');
        return fetch(url, { method: 'DELETE', headers: ah() });
    }

    try {
        let res = await callDelete(false);
        if (res.status === 409) {
            const body = await res.json().catch(() => ({}));
            const msg = body.error || _t('vt.err.payrollExists');
            if (!confirm(_t('vt.err.confirmForceDelete', { msg }))) return;
            res = await callDelete(true);
        }
        if (!res.ok) {
            const txt = await res.text();
            let msg = 'Fehler ' + res.status;
            try { msg = JSON.parse(txt).error || msg; } catch { if (txt) msg = txt; }
            alert(_t('vt.err.deleteFailed', { msg }));
            return;
        }
        // MA neu laden
        const empRes = await fetch('/api/employees', { headers: ah() });
        if (empRes.ok) {
            const emps = await empRes.json();
            allVtEmployees = emps.filter(e => e.isActive && e.employments?.length > 0);
            selectedVtEmployee = allVtEmployees.find(e => e.id === employeeId) || null;
            if (selectedVtEmployee) renderVtDetail(selectedVtEmployee);
            renderVtList(allVtEmployees);
        }
    } catch (err) {
        alert(_t('vt.err.connectionError', { msg: err.message }));
    }
}

// ══════════════════════════════════════════════
// VERTRAG BEARBEITEN (Edit-Modal)
// ══════════════════════════════════════════════
let _ceJobGroupsLoaded = false;
let _ceEducationLevelsLoaded = false;

async function ensureCeStammdatenLoaded() {
    // Funktionsgruppen + Ausbildungsstufen einmal laden
    if (!_ceJobGroupsLoaded) {
        try {
            const res = await fetch('/api/jobgroups', { headers: ah() });
            const list = res.ok ? await res.json() : [];
            const sel = document.getElementById('ceJobGroup');
            sel.innerHTML = '<option value="">– wählen –</option>'
                + list.filter(j => j.isActive !== false)
                      .map(j => `<option value="${j.code}">${j.code}${j.label ? ' — ' + j.label : ''}</option>`).join('');
            _ceJobGroupsLoaded = true;
        } catch {}
    }
    if (!_ceEducationLevelsLoaded) {
        try {
            const res = await fetch('/api/educationlevels', { headers: ah() });
            const list = res.ok ? await res.json() : [];
            // L-GAV-Reihenfolge: Ia, Ib, II, IIIa, IIIb, IV (von unqualifiziert zu qualifiziert)
            const order = ['Ia','Ib','II','IIIa','IIIb','IV'];
            // Beschreibungen (DB hat nur den Code; Beschreibungen sind L-GAV-Standard)
            const labels = {
                'Ia':   'ohne Gastronomische Berufslehre',
                'Ib':   'ohne Gastronomische Berufslehre mit PROGRESSO',
                'II':   'mit 2-jähriger gastronomischer Berufslehre EBA',
                'IIIa': 'mit 3-jähriger gastronomischer Berufslehre EFZ',
                'IIIb': 'mit 3-jähriger gastronomischer Berufslehre GA 6 Tage',
                'IV':   'gastronomische Berufsprüfung'
            };
            const orderIdx = c => {
                const i = order.indexOf(c);
                return i === -1 ? 999 : i;
            };
            const sorted = list.filter(e => e.isActive !== false)
                               .slice()
                               .sort((a,b) => {
                                   const ia = orderIdx(a.code), ib = orderIdx(b.code);
                                   if (ia !== ib) return ia - ib;
                                   return (a.code || '').localeCompare(b.code || '');
                               });
            const sel = document.getElementById('ceEducationLevel');
            sel.innerHTML = '<option value="">– wählen –</option>'
                + sorted.map(e => {
                    const desc = labels[e.code] || e.label || e.name || '';
                    return `<option value="${e.code}">${e.code}${desc ? ' – ' + desc : ''}</option>`;
                }).join('');
            _ceEducationLevelsLoaded = true;
        } catch {}
    }
}

// Modus: 'edit' (PUT bestehender Vertrag), 'import' (POST neu aus CSV-Snapshot),
//        'new' (POST leerer neuer Vertrag)
let _ceMode = 'edit';

async function openContractEditModal(c, mode = 'edit') {
    await ensureCeStammdatenLoaded();
    _ceMode = mode;
    const modal = document.getElementById('contractEditModal');
    const isFix = c.employmentModel === 'FIX' || c.employmentModel === 'FIX-M';
    const isMtp = c.employmentModel === 'MTP';
    const isBefristet = !!c.contractEndDate;

    // Header je nach Modus anpassen — i18n-aware: data-i18n setzen, dann
    // applyAll macht die Übersetzung. Damit folgt der Title auch dem
    // Language-Switch live.
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
    const titleEl = document.getElementById('ceModalTitle')
        || modal.querySelector('div[style*="font-size:15px"]');
    const saveBtn = document.getElementById('ceSaveBtn');
    let titleKey, saveKey;
    if (mode === 'import')      { titleKey = 'vt.modal.importTitle'; saveKey = 'vt.modal.btn.import'; }
    else if (mode === 'new')    { titleKey = 'vt.modal.newTitle';    saveKey = 'vt.modal.btn.create'; }
    else                        { titleKey = 'vt.modal.editTitle';   saveKey = 'vt.modal.btn.save'; }
    if (titleEl) { titleEl.setAttribute('data-i18n', titleKey); titleEl.textContent = _t(titleKey); }
    if (saveBtn) { saveBtn.setAttribute('data-i18n', saveKey);  saveBtn.textContent = _t(saveKey); }
    // Modal-Inhalte übersetzen (data-i18n von Labels, Buttons, Placeholders)
    if (window.i18n && window.i18n.applyAll) window.i18n.applyAll(modal);

    // Sub-Header (MA-Name)
    const empName = selectedVtEmployee
        ? `${selectedVtEmployee.firstName ?? ''} ${selectedVtEmployee.lastName ?? ''}`.trim()
        : '';
    document.getElementById('ceModalSub').textContent =
        (empName ? empName + ' · ' : '') + (c.jobTitle ?? '') + ' · ' + (c.employmentModel ?? '');

    // Felder befüllen — bei 'import'/'new' ID leer lassen
    document.getElementById('ceContractId').value      = mode === 'edit' ? (c.id ?? '') : '';
    document.getElementById('ceEmployeeId').value      = c.employeeId ?? selectedVtEmployee?.id ?? '';
    document.getElementById('ceStartDate').value       = c.contractStartDate ? c.contractStartDate.slice(0,10) : '';
    document.getElementById('ceEmploymentModel').value = c.employmentModel ?? 'UTP';
    document.getElementById('ceContractType').value    = isBefristet ? 'befristet' : 'unbefristet';
    document.getElementById('ceEndDate').value         = c.contractEndDate ? c.contractEndDate.slice(0,10) : '';
    document.getElementById('ceJobTitle').value        = c.jobTitle ?? '';
    document.getElementById('ceProbationMonths').value = c.probationPeriodMonths ?? '';
    document.getElementById('ceIsActive').value        = c.isActive === false ? 'false' : 'true';

    document.getElementById('ceHourlyRate').value         = c.hourlyRate ?? '';
    document.getElementById('ceMonthlySalaryFte').value   = c.monthlySalaryFte ?? '';
    document.getElementById('ceMonthlySalary').value      = c.monthlySalary ?? '';
    document.getElementById('cePensum').value             = c.employmentPercentage ?? '';
    document.getElementById('ceWeeklyHours').value        = c.weeklyHours ?? '';
    document.getElementById('ceGuaranteedHours').value    = c.guaranteedHoursPerWeek ?? '';
    document.getElementById('ceVacationPercent').value    = c.vacationPercent ?? '';
    document.getElementById('ceHolidayPercent').value     = c.holidayPercent ?? '';
    document.getElementById('ceThirteenthPercent').value  = c.thirteenthSalaryPercent ?? '';

    // Defaults: CREW + Ia (häufigste Kombi). Greifen in ALLEN Modal-Modi
    // (edit/import/new) bei leerem Wert, damit auch Alt-Verträge mit
    // unvollständigen Daten direkt einen sinnvollen Default haben — User
    // kann nachträglich ändern. Die Vertragsliste (Anzeige) zeigt weiterhin
    // den echten DB-Stand, da diese Funktion dort gar nicht durchläuft.
    document.getElementById('ceJobGroup').value        = (c.jobGroupCode || 'CREW');
    document.getElementById('ceEducationLevel').value  = (c.educationLevelCode || selectedVtEmployee?.educationLevelCode || 'Ia');
    document.getElementById('ceErrorMsg').textContent  = '';
    document.getElementById('ceComplianceResult').innerHTML = '';

    onCeModelChange();
    onCeContractTypeChange();
    checkCeMinimumWage();

    modal.style.display = 'flex';
}

function closeContractEditModal() {
    document.getElementById('contractEditModal').style.display = 'none';
}

function onCeModelChange() {
    const m = document.getElementById('ceEmploymentModel').value;
    const isFix = m === 'FIX' || m === 'FIX-M';
    const isMtp = m === 'MTP';
    const show = (id, on) => { const el = document.getElementById(id); if (el) el.style.display = on ? '' : 'none'; };
    show('ceHourlyWrap',     !isFix);
    show('ceFteWrap',        isFix);
    show('ceMonthlyWrap',    isFix);
    show('cePensumWrap',     isFix);
    show('ceWeeklyWrap',     !isFix);
    show('ceGuaranteedWrap', isMtp);
    checkCeMinimumWage();
}

function onCeContractTypeChange() {
    const t = document.getElementById('ceContractType').value;
    const wrap = document.getElementById('ceEndDateWrap');
    if (wrap) wrap.style.opacity = t === 'befristet' ? '1' : '0.5';
}

function onCeFteChange() {
    // Bei FIX: Monatslohn = FTE × Pensum/100
    const fte = parseFloat(document.getElementById('ceMonthlySalaryFte').value);
    const pct = parseFloat(document.getElementById('cePensum').value);
    if (Number.isFinite(fte) && Number.isFinite(pct) && pct > 0) {
        document.getElementById('ceMonthlySalary').value = (fte * pct / 100).toFixed(2);
    }
    checkCeMinimumWage();
}

function onCePensumChange() {
    onCeFteChange();
}

// Letztes Compliance-Ergebnis (für "Mindestlohn übernehmen"-Button)
let _ceLastComplianceResult = null;

async function checkCeMinimumWage() {
    const infoEl   = document.getElementById('ceMinimumWageInfo');
    const resultEl = document.getElementById('ceComplianceResult');
    if (!resultEl || !infoEl) return;
    _ceLastComplianceResult = null;
    const jobGroupCode = document.getElementById('ceJobGroup').value;
    const educationLevelCode = document.getElementById('ceEducationLevel').value;
    const employmentModel = document.getElementById('ceEmploymentModel').value;
    const isFix = employmentModel === 'FIX' || employmentModel === 'FIX-M';
    const isMtp = employmentModel === 'MTP';
    const startDate = document.getElementById('ceStartDate').value;
    const pensum = parseFloat(document.getElementById('cePensum').value) || (isFix ? 100 : null);
    const hourly = parseFloat(document.getElementById('ceHourlyRate').value);
    const monthly = parseFloat(document.getElementById('ceMonthlySalary').value)
                 || parseFloat(document.getElementById('ceMonthlySalaryFte').value);
    const guaranteed = parseFloat(document.getElementById('ceGuaranteedHours').value);

    // Reset
    infoEl.innerHTML   = '';
    resultEl.innerHTML = '';

    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);

    // MTP-Hinweise
    let preHints = '';
    if (isMtp) {
        if (!Number.isFinite(guaranteed) || guaranteed <= 0) {
            preHints += `<div style="color:#92400e;font-size:11.5px">${_t('vt.compl.mtpHoursMissing')}</div>`;
        } else {
            if (guaranteed < 17) preHints += `<div style="color:#92400e;font-size:11.5px">${_t('vt.compl.mtpMin17')}</div>`;
            if (guaranteed >= 33) preHints += `<div style="color:#0369a1;font-size:11.5px">${_t('vt.compl.mtpMax33')}</div>`;
        }
    }

    // Wenn Ausbildung/Funktion/Beginn fehlen, dezenten Hinweis im Mindestlohn-Bereich
    if (!jobGroupCode || !educationLevelCode || !startDate) {
        infoEl.innerHTML = `<div style="color:#94a3b8;font-size:11.5px">${_t('vt.compl.qualMissing')}</div>`;
        if (preHints) resultEl.innerHTML = preHints;
        return;
    }

    try {
        // Effective Date: Vertrag in Zukunft → Vertragsbeginn (neuer Mindestlohn).
        // Vertrag in Vergangenheit → heute, weil L-GAV jährlich neu verhandelt
        // wird und die DB ggf. keine alten Regeln (2024/2025) hat. Identische
        // Logik wie im Vertrags-Import (runComplianceCheck in import.html).
        const todayIso = new Date().toISOString().split('T')[0];
        const effectiveDate = (startDate > todayIso) ? startDate : todayIso;
        // Geburtsdatum für altersabhängige Regel (z.B. unter 18 Jahre).
        // Backend-Property heisst dateOfBirth (JSON camelCase von DateOfBirth).
        const birthDate = selectedVtEmployee?.dateOfBirth
            ? selectedVtEmployee.dateOfBirth.slice(0, 10)
            : null;
        const body = {
            jobGroupCode,
            educationLevelCode,
            effectiveDate,
            employmentModel,
            employmentPercentage: pensum ?? 100,
            hourlyRate: !isFix && Number.isFinite(hourly) ? hourly : null,
            monthlySalary: isFix && Number.isFinite(monthly) ? monthly : null,
            birthDate
        };
        const res = await fetch('/api/compliance/check-live', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            infoEl.innerHTML = `<div style="color:#92400e;font-size:11.5px">${_t('vt.compl.serviceUnavail')}</div>`;
            if (preHints) resultEl.innerHTML = preHints;
            return;
        }
        const cr = await res.json();
        _ceLastComplianceResult = cr;

        // ──────── Mindestlohn-Info (oben, immer angezeigt sobald Quali da) ────────
        const noRule = cr.status === 'NO_RULE';
        if (noRule) {
            infoEl.innerHTML = `<div style="padding:8px 12px;border-radius:6px;background:#fffbeb;border:1px solid #fde68a;color:#92400e;font-size:11.5px">${_t('vt.compl.noRule')}</div>`;
        } else {
            let infoHtml = `<div style="padding:8px 12px;border-radius:6px;background:#eff6ff;border:1px solid #bfdbfe;color:#1e3a8a;font-size:12px;display:flex;align-items:center;gap:14px;flex-wrap:wrap">`;
            infoHtml += `<div style="font-weight:700;color:#1d4ed8">${_t('vt.compl.headline')}</div>`;
            if (!isFix && cr.minimumHourlyRate != null) {
                infoHtml += `<div>${_t('vt.compl.hourlyFrom')} <strong>CHF ${Number(cr.minimumHourlyRate).toFixed(2)}</strong>/h</div>`;
            }
            if (isFix && cr.minimumMonthlySalaryFte != null) {
                const p = cr.employmentPercentage ?? pensum ?? 100;
                infoHtml += `<div>${_t('vt.compl.monthlyFteFrom')} <strong>CHF ${Number(cr.minimumMonthlySalaryFte).toFixed(2)}</strong>`;
                if (p < 100 && cr.minimumMonthlySalary != null) {
                    infoHtml += ` <span style="color:#475569">(${p}% = CHF ${Number(cr.minimumMonthlySalary).toFixed(2)})</span>`;
                }
                infoHtml += `</div>`;
            }
            infoHtml += `<div style="color:#64748b;font-size:11px">${_t('vt.compl.validFrom')} ${new Date(startDate).toLocaleDateString('de-CH')}</div>`;
            infoHtml += `</div>`;
            infoEl.innerHTML = infoHtml;
        }

        // ──────── Lohn-Vergleich (unten, nur wenn Lohn eingegeben) ────────
        const hasSalaryInput = (!isFix && Number.isFinite(hourly) && hourly > 0)
                            || ( isFix && Number.isFinite(monthly) && monthly > 0);

        // Auto-Fill: wenn Lohn-Feld leer ist und ein Mindestlohn gefunden wurde,
        // den Mindestlohn als Default eintragen. Re-triggert dann den Check
        // damit der OK-Status angezeigt wird. Ändert nichts, wenn der User
        // bereits einen Wert eingegeben hat.
        if (!hasSalaryInput && !noRule) {
            if (!isFix && cr.minimumHourlyRate != null) {
                const hourlyEl = document.getElementById('ceHourlyRate');
                if (hourlyEl && !hourlyEl.value) {
                    hourlyEl.value = Number(cr.minimumHourlyRate).toFixed(2);
                    setTimeout(() => checkCeMinimumWage(), 0);
                    return;
                }
            }
            if (isFix && cr.minimumMonthlySalaryFte != null) {
                const fteEl = document.getElementById('ceMonthlySalaryFte');
                if (fteEl && !fteEl.value) {
                    fteEl.value = Number(cr.minimumMonthlySalaryFte).toFixed(2);
                    if (typeof onCeFteChange === 'function') onCeFteChange();
                    setTimeout(() => checkCeMinimumWage(), 0);
                    return;
                }
            }
        }

        if (!hasSalaryInput || noRule) {
            resultEl.innerHTML = preHints;
            return;
        }

        const ok = cr.status === 'OK' || cr.status === 'ok';
        const underpaid = cr.status === 'UNDERPAID' || cr.status === 'underpaid';
        const overpaid = ok && Number(cr.difference ?? 0) > 0;
        const color = underpaid ? '#dc2626' : '#15803d';
        const bg    = underpaid ? '#fef2f2' : '#f0fdf4';
        const border= underpaid ? '#fecaca' : '#bbf7d0';

        let html = preHints;
        html += `<div style="padding:10px 14px;border-radius:6px;background:${bg};border:1px solid ${border};font-size:12px">`;
        html += `<div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:6px">`;
        html += `<div style="font-weight:700;color:${color}">${
            underpaid ? _t('vt.compl.tooLow')
                      : overpaid ? _t('vt.compl.aboveMin')
                                 : _t('vt.compl.ok')}</div>`;
        if (underpaid) {
            html += `<button onclick="applyCeMinimumWage()" style="background:#dc2626;color:#fff;border:none;border-radius:5px;padding:4px 10px;font-size:11.5px;cursor:pointer;font-weight:600">${_t('vt.compl.applyMin')}</button>`;
        }
        html += `</div>`;

        // Vergleichs-Grid: Mindestwert vs. aktueller Wert
        const colMin = _t('vt.compl.colMin');
        const colCur = _t('vt.compl.colCurrent');
        html += `<div style="display:grid;grid-template-columns:auto 1fr 1fr;gap:4px 16px;font-size:11.5px;color:#475569">`;
        if (!isFix) {
            html += `<div>${_t('vt.compl.lblHourly')}</div>`;
            html += `<div>${colMin} <strong>CHF ${cr.minimumHourlyRate != null ? Number(cr.minimumHourlyRate).toFixed(2) : '–'}</strong></div>`;
            html += `<div>${colCur} <strong>CHF ${cr.currentHourlyRate != null ? Number(cr.currentHourlyRate).toFixed(2) : '–'}</strong></div>`;
        }
        if (isFix && cr.minimumMonthlySalary != null) {
            const p = cr.employmentPercentage ?? pensum ?? 100;
            html += `<div>${_t('vt.compl.lblMonthly')}</div>`;
            html += `<div>${colMin} <strong>CHF ${Number(cr.minimumMonthlySalary).toFixed(2)}</strong>${p < 100 && cr.minimumMonthlySalaryFte != null ? `<span style="color:#94a3b8"> (${p}% von ${Number(cr.minimumMonthlySalaryFte).toFixed(2)})</span>` : ''}</div>`;
            html += `<div>${colCur} <strong>CHF ${cr.currentMonthlySalary != null ? Number(cr.currentMonthlySalary).toFixed(2) : '–'}</strong></div>`;
        }
        if (cr.difference != null && cr.difference !== 0) {
            html += `<div>${_t('vt.compl.lblDiff')}</div>`;
            html += `<div style="grid-column:span 2;color:${underpaid ? '#dc2626' : '#0369a1'};font-weight:600">CHF ${Number(cr.difference).toFixed(2)} ${underpaid ? _t('vt.compl.diffLow') : _t('vt.compl.diffHigh')}</div>`;
        }
        html += `</div>`;

        if (cr.warningMessage) {
            html += `<div style="margin-top:6px;font-size:11.5px;color:#475569;font-style:italic">${cr.warningMessage}</div>`;
        }
        html += `</div>`;
        resultEl.innerHTML = html;
    } catch (e) {
        infoEl.innerHTML = `<div style="color:#92400e;font-size:11.5px">${_t('vt.compl.serviceErr', { msg: e.message })}</div>`;
        if (preHints) resultEl.innerHTML = preHints;
    }
}

/// Übernimmt den errechneten Mindestlohn als Vorschlag in das Lohn-Feld.
function applyCeMinimumWage() {
    if (!_ceLastComplianceResult) return;
    const employmentModel = document.getElementById('ceEmploymentModel').value;
    const isFix = employmentModel === 'FIX' || employmentModel === 'FIX-M';
    if (isFix) {
        const fte = _ceLastComplianceResult.minimumMonthlySalaryFte
                 ?? _ceLastComplianceResult.minimumMonthlySalary;
        if (fte != null) {
            document.getElementById('ceMonthlySalaryFte').value = Number(fte).toFixed(2);
            onCeFteChange();
        }
    } else if (_ceLastComplianceResult.minimumHourlyRate != null) {
        document.getElementById('ceHourlyRate').value = Number(_ceLastComplianceResult.minimumHourlyRate).toFixed(2);
        checkCeMinimumWage();
    }
}

async function saveContractEdit() {
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    const errEl = document.getElementById('ceErrorMsg');
    errEl.textContent = '';
    const id = document.getElementById('ceContractId').value;
    const empId = parseInt(document.getElementById('ceEmployeeId').value);

    if (_ceMode === 'edit' && !id) { errEl.textContent = _t('vt.err.noContractId'); return; }
    if ((_ceMode === 'import' || _ceMode === 'new') && !empId) { errEl.textContent = _t('vt.err.noEmployeeId'); return; }

    const employmentModel = document.getElementById('ceEmploymentModel').value;
    const isFix = employmentModel === 'FIX' || employmentModel === 'FIX-M';
    const startDate = document.getElementById('ceStartDate').value;
    if (!startDate) { errEl.textContent = _t('vt.err.startDateRequired'); return; }
    const isBefristet = document.getElementById('ceContractType').value === 'befristet';
    const endDate = document.getElementById('ceEndDate').value;

    const pensumStr = document.getElementById('cePensum').value;
    const pensum = pensumStr === '' ? null : parseFloat(pensumStr);
    const hourly = parseFloat(document.getElementById('ceHourlyRate').value) || null;
    const fte = parseFloat(document.getElementById('ceMonthlySalaryFte').value) || null;
    const monthly = parseFloat(document.getElementById('ceMonthlySalary').value) || null;

    // Pflichtfeld-Validierung für Lohn (insb. wichtig bei Import)
    if (isFix && !fte && !monthly) {
        errEl.textContent = _t('vt.err.salaryRequired');
        return;
    }
    if (!isFix && !hourly) {
        errEl.textContent = _t('vt.err.hourlyRequired');
        return;
    }

    const payload = {
        contractStartDate:       startDate,
        contractEndDate:         isBefristet && endDate ? endDate : null,
        contractType:            isBefristet ? 'befristet' : 'unbefristet',
        employmentModel,
        salaryType:              isFix ? 'monthly' : 'hourly',
        jobTitle:                document.getElementById('ceJobTitle').value || null,
        educationLevelCode:      document.getElementById('ceEducationLevel').value || null,
        employmentPercentage:    isFix ? pensum : null,
        weeklyHours:             !isFix ? (parseFloat(document.getElementById('ceWeeklyHours').value) || null) : null,
        guaranteedHoursPerWeek:  parseFloat(document.getElementById('ceGuaranteedHours').value) || null,
        hourlyRate:              !isFix ? hourly : null,
        monthlySalaryFte:        isFix ? fte : null,
        monthlySalary:           isFix ? monthly : null,
        vacationPercent:         parseFloat(document.getElementById('ceVacationPercent').value) || null,
        holidayPercent:          parseFloat(document.getElementById('ceHolidayPercent').value) || null,
        thirteenthSalaryPercent: parseFloat(document.getElementById('ceThirteenthPercent').value) || null,
        probationPeriodMonths:   parseInt(document.getElementById('ceProbationMonths').value) || null,
        isActive:                document.getElementById('ceIsActive').value === 'true',
    };

    // Bei Import / Neu zusätzliche Pflichtfelder
    if (_ceMode === 'import' || _ceMode === 'new') {
        payload.employeeId = empId;
        payload.companyProfileId = currentBranchId || 1;
        payload.contractEndDateSet = true;
    }

    try {
        const url = _ceMode === 'edit' ? `/api/employments/${id}` : '/api/employments';
        const method = _ceMode === 'edit' ? 'PUT' : 'POST';
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (!res.ok) {
            const txt = await res.text().catch(() => '');
            let msg = 'Fehler ' + res.status;
            try { const j = JSON.parse(txt); msg = j.error || j.title || msg; } catch { if (txt) msg = txt; }
            errEl.textContent = msg;
            return;
        }
        closeContractEditModal();
        // MA neu laden + Detail aktualisieren
        const empRes = await fetch('/api/employees', { headers: ah() });
        if (empRes.ok) {
            const emps = await empRes.json();
            allVtEmployees = emps.filter(e => e.isActive && e.employments?.length > 0);
            selectedVtEmployee = allVtEmployees.find(e => e.id === empId);
            if (selectedVtEmployee) renderVtDetail(selectedVtEmployee);
            renderVtList(allVtEmployees);
        }
    } catch (e) {
        errEl.textContent = _t('vt.err.connectionError', { msg: e.message });
    }
}

function buildContractPage() {
    const contractWrap = document.getElementById('contractWrap');
    if (!contractWrap) return;
    contractWrap.innerHTML = `
        <div class="c-top">
            <div class="c-section">
                <div class="c-section-title">Mitarbeiter</div>
                <div class="c-grid">
                    <label>Mitarbeiter auswählen</label>
                    <select id="employeeId"><option value="">Lade...</option></select>
                    <label>Funktion</label>
                    <select id="jobGroupCode"><option value="">Lade...</option></select>
                    <label>Ausbildung</label>
                    <select id="educationLevelCode"><option value="">Lade...</option></select>
                    <label>Nationalität</label>
                    <select id="nationalityId" onchange="onNationalityChange()"><option value="">Lade...</option></select>
                    <label>Geschlecht</label>
                    <div id="genderDisplay" class="c-readonly">–</div>
                    <label>Zivilstand</label>
                    <select id="zivilstandSelect" style="width:100%;padding:6px 10px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;background:#f8fafc">
                        <option value="">– bitte wählen –</option>
                        <option value="ledig">Ledig</option>
                        <option value="verheiratet">Verheiratet</option>
                        <option value="geschieden">Geschieden</option>
                        <option value="verwitwet">Verwitwet</option>
                        <option value="eingetragene_partnerschaft">Eingetragene Partnerschaft</option>
                        <option value="aufgeloeste_partnerschaft">Aufgelöste Partnerschaft</option>
                    </select>
                    <div id="qstBefreitLabel" style="display:none"><label>QST befreit ab</label></div>
                    <div id="qstBefreitField" style="display:none">
                        <input type="date" id="quellensteuerBefreitAb" style="width:100%">
                        <div style="font-size:11px;color:#92400e;margin-top:3px">Datum C-Ausweis / CH-Einbürgerung</div>
                    </div>
                    <div id="qstStatusLabel" style="display:none"><label>QST-Status</label></div>
                    <div id="qstStatusField" style="display:none">
                        <div id="qstStatusDisplay" class="c-readonly" style="font-size:12px"></div>
                    </div>
                </div>
                <div class="c-buttons" style="margin-top:8px" id="empSaveButtonRow" style="display:none">
                    <button class="c-btn c-btn-blue" onclick="saveEmployeeData()" id="btnSaveEmployeeData" style="display:none">
                        💾 Mitarbeiterdaten speichern
                    </button>
                </div>
            </div>
            <div class="c-section">
                <div class="c-section-title">Mitarbeiter-Import</div>
                <div id="dropzone" class="dropzone">
                    CSV-Datei hierhin ziehen
                    <div style="font-size:13px;color:#94a3b8;margin-top:6px">oder klicken zum Auswählen</div>
                </div>
                <input type="file" id="csvFileInput" accept=".csv" class="hidden">
                <div id="importFileName" style="margin-top:10px;font-size:13px;color:#64748b">Keine Datei ausgewählt.</div>
                <div class="c-buttons">
                    <button class="c-btn c-btn-blue" onclick="uploadEmployeeCsv()">CSV importieren</button>
                </div>
                <div id="importResult" style="margin-top:12px;font-size:13px"></div>
            </div>
        </div>

        <div id="snapshotBanner" class="c-section" style="display:none;background:#fffbeb;border-color:#fcd34d">
            <div class="c-section-title">Import-Vorschlag aus CSV</div>
            <div class="c-grid" id="snapshotContent"></div>
            <div class="c-buttons" style="margin-top:12px">
                <button class="c-btn c-btn-blue" onclick="applySnapshot()">Vorschlag übernehmen</button>
                <button class="c-btn c-btn-gray" onclick="dismissSnapshot()">Schliessen</button>
            </div>
        </div>

        <div style="display:none">
            <div id="companyNameDisplay"></div><div id="normalWeeklyHoursDisplay"></div>
            <div id="maxPartTimeHoursDisplay"></div><div id="holdBackVacationPayoutDisplay"></div>
            <div id="payrollPeriodStartDayDisplay"></div>
        </div>

        <div class="c-section">
            <div class="c-section-title">Vertragsgrundlagen</div>
            <div class="c-grid">
                <label>Besteht schon ein Vertrag?</label>
                <select id="existingContract"><option value="no" selected>Nein</option><option value="yes">Ja</option></select>
                <label>Vertrag erstellt am</label>
                <input type="date" id="createdDate">
                <label>Vertrag beginnt am</label>
                <input type="date" id="startDate">
                <label>Vertragstyp</label>
                <select id="employmentModel">
                    <option value="UTP" selected>UTP – Stundenlohn Teilzeit</option>
                    <option value="MTP">MTP – Garantiertes Mindest-Teilzeitpensum</option>
                    <option value="FIX">FIX – Festpensum (50–100%)</option>
                    <option value="FIX-M">FIX-M – Management (50–100%)</option>
                </select>
                <label>Befristeter Vertrag</label>
                <select id="fixedTermYesNo"><option value="no" selected>Nein</option><option value="yes">Ja</option></select>
                <div id="fixedTermMonthsLabel"><label>Befristungsdauer (Monate)</label></div>
                <div id="fixedTermMonthsField"><select id="fixedTermMonths"><option value="4">4</option><option value="5">5</option><option value="6" selected>6</option></select></div>
                <div id="calculatedEndDateLabel"><label>Vertragsende (berechnet)</label></div>
                <div id="calculatedEndDateField"><div id="calculatedEndDate" class="c-readonly"></div></div>
                <label>Probezeit</label>
                <select id="probationYesNo"><option value="yes" selected>Ja</option><option value="no">Nein</option></select>
                <div id="probationMonthsLabel"><label>Probezeit Monate</label></div>
                <div id="probationMonthsField"><select id="probationMonths"><option value="0">0</option><option value="1">1</option><option value="2">2</option><option value="3" selected>3</option></select></div>
                <div id="probationEndDateLabel"><label>Ende der Probezeit</label></div>
                <div id="probationEndDateField"><div id="probationEndDate" class="c-readonly"></div></div>
            </div>
        </div>

        <div class="c-section">
            <div class="c-section-title">Lohn / Modell</div>
            <div class="c-grid">
                <label>Lohnart</label>
                <input type="text" id="salaryType" readonly value="hourly">
                <div id="hourlyRateLabel"><label>Stundenlohn</label></div>
                <div id="hourlyRateField"><input type="number" id="hourlyRate" step="0.01"></div>
                <div id="monthlySalaryFteLabel" class="hidden"><label>Monatslohn 100%</label></div>
                <div id="monthlySalaryFteField" class="hidden">
                    <input type="number" id="monthlySalaryFte" step="0.01" placeholder="z.B. 3800.00" oninput="onFteSalaryChange()">
                </div>
                <div id="monthlySalaryLabel" class="hidden"><label>Monatslohn (nach Pensum)</label></div>
                <div id="monthlySalaryField" class="hidden">
                    <input type="number" id="monthlySalary" step="0.01" readonly style="background:#f8fafc;color:#64748b">
                </div>
                <div id="percentageLabel" class="hidden"><label>Pensum %</label></div>
                <div id="percentageField" class="hidden">
                    <select id="percentage" onchange="onPensumChange()"><option value="10">10%</option><option value="20">20%</option><option value="30">30%</option><option value="40">40%</option><option value="50">50%</option><option value="60">60%</option><option value="70">70%</option><option value="80" selected>80%</option><option value="90">90%</option><option value="100">100%</option></select>
                </div>
                <div id="guaranteedHoursLabel" class="hidden"><label>Garantierte Stunden / Woche</label></div>
                <div id="guaranteedHoursField" class="hidden"><input type="number" id="guaranteedHours" step="1" min="0" value="18"></div>
            </div>
            <div class="c-buttons">
                <button class="c-btn c-btn-gray" onclick="applyMinimumWageSuggestion()">Mindestlohn übernehmen</button>
            </div>
        </div>

        <div class="c-section">
            <div class="c-section-title">Live-Prüfung</div>
            <div id="liveResult" style="font-size:13.5px;color:#64748b">Noch keine Prüfung.</div>
            <div id="minimumWageStatusBox" class="c-status hidden" style="margin-top:12px">
                <div id="minimumWageSummary"></div>
            </div>
        </div>

        <div class="c-section">
            <div class="c-section-title">Speichern &amp; PDF</div>
            <div class="c-buttons">
                <button class="c-btn c-btn-blue" onclick="saveEmployment()">Anstellung speichern</button>
                <button class="c-btn c-btn-orange" onclick="downloadContractPdf()" id="btnPdf" disabled>📄 Vertrag als PDF</button>
            </div>
            <div id="saveResult" style="margin-top:12px;font-size:13px;color:#64748b">Noch nicht gespeichert.</div>
        </div>
    `;

    // Init contract page
    const today = new Date().toISOString().split('T')[0];
    document.getElementById('createdDate').value = today;
    document.getElementById('startDate').value = today;
    setupDropzone();
    loadEmployees();
    loadJobGroups();
    loadEducationLevels();
    loadNationalities();
    loadCompanyProfile();
    updateForm();
    checkLive();
    wireEvents();
}

function wireEvents() {
    document.querySelectorAll('#contractWrap input, #contractWrap select').forEach(el => {
        el.addEventListener('change', async () => {
            if (el.id === 'employeeId') { await loadSnapshot(el.value); updateGenderDisplay(el.value); }
            updateForm(); await checkLive();
        });
        el.addEventListener('input', async () => { updateForm(); await checkLive(); });
    });
}

function updateGenderDisplay(employeeId) {
    const g = employeeGenderMap[employeeId] ?? '';
    document.getElementById('genderDisplay').innerText = g === 'female' ? 'Weiblich' : g === 'male' ? 'Männlich' : '–';
}

async function loadEmployees() {
    const sel = document.getElementById('employeeId');
    if (!sel) return;
    sel.innerHTML = '<option value="">Lade...</option>';
    try {
        const url = fixedCompanyProfileId ? `/api/employees/lookup/company/${fixedCompanyProfileId}` : '/api/employees';
        const res = await fetch(url, { headers: ah() });
        const data = await res.json();
        sel.innerHTML = '';
        employeeGenderMap = {}; employeeDateOfBirthMap = {};
        data.forEach(item => {
            const o = document.createElement('option');
            o.value = item.id ?? item.Id;
            o.textContent = item.displayName ?? item.DisplayName;
            sel.appendChild(o);
            employeeGenderMap[o.value] = item.gender ?? item.Gender ?? '';
            employeeDateOfBirthMap[o.value] = item.dateOfBirth ?? item.DateOfBirth ?? null;
        });
        if (!sel.options.length) sel.innerHTML = '<option value="">Keine Mitarbeitenden</option>';
    } catch { sel.innerHTML = '<option value="">Fehler</option>'; }
}

async function loadSnapshot(employeeId) {
    const banner = document.getElementById('snapshotBanner');
    if (!employeeId) {
        banner.style.display = 'none';
        currentSnapshot = null;
        const hist = document.getElementById('contractHistory');
        if (hist) hist.style.display = 'none';
        // QST-Felder zurücksetzen
        const show = (id, v) => { const el = document.getElementById(id); if (el) el.style.display = v ? '' : 'none'; };
        show('qstBefreitLabel', false); show('qstBefreitField', false);
        show('qstStatusLabel', false);  show('qstStatusField', false);
        show('btnSaveEmployeeData', false);
        return;
    }
    // Mitarbeiterdaten laden (für QST-Felder)
    try {
        const empRes = await fetch(`/api/employees/${employeeId}`, { headers: ah() });
        if (empRes.ok) {
            const emp = await empRes.json();
            // Zivilstand setzen
            const zivilSel = document.getElementById('zivilstandSelect');
            if (zivilSel) zivilSel.value = emp.maritalStatus ?? emp.zivilstand ?? '';
            // Nationalität setzen
            if (emp.nationalityId) { const natSel = document.getElementById('nationalityId'); if (natSel) natSel.value = emp.nationalityId; }
            // QST-Befreit-Datum setzen
            const befreitEl = document.getElementById('quellensteuerBefreitAb');
            if (befreitEl) befreitEl.value = emp.quellensteuerBefreitAb ? emp.quellensteuerBefreitAb.slice(0,10) : '';
            onNationalityChange();
        }
    } catch {}

    try {
        const res = await fetch(`/api/employeeimportsnapshot/latest/${employeeId}`, { headers: ah() });
        if (!res.ok) { banner.style.display = 'none'; currentSnapshot = null; }
        else {
            currentSnapshot = await res.json();
            showSnapshotBanner(currentSnapshot);
            if (currentSnapshot.employmentModel) { const em = document.getElementById('employmentModel'); if (em) em.value = currentSnapshot.employmentModel; }
            if (currentSnapshot.jobGroupCode) { const jg = document.getElementById('jobGroupCode'); if (jg) jg.value = currentSnapshot.jobGroupCode; }
            const isFix = currentSnapshot.employmentModel === 'FIX' || currentSnapshot.employmentModel === 'FIX-M';
            const val = currentSnapshot.guaranteedHoursPerWeek ?? currentSnapshot.weeklyHours;
            if (val) { if (isFix) { const pct = document.getElementById('percentage'); if (pct) pct.value = Math.round(val); } else { const gh = document.getElementById('guaranteedHours'); if (gh) gh.value = Math.round(val); } }
            updateForm(); await checkLive();
        }
    } catch { banner.style.display = 'none'; currentSnapshot = null; }

    // Vertragshistorie laden
    await loadContractHistory(employeeId);
}

async function loadContractHistory(employeeId) {
    const hist = document.getElementById('contractHistory');
    if (!hist) return;
    try {
        const res = await fetch(`/api/employments/employee/${employeeId}`, { headers: ah() });
        if (!res.ok) { hist.style.display = 'none'; return; }
        const contracts = await res.json();
        if (!contracts.length) { hist.style.display = 'none'; return; }

        const fmt = d => d ? new Date(d).toLocaleDateString('de-CH', {day:'2-digit', month:'2-digit', year:'numeric'}) : '–';
        const modelLabel = { UTP:'Stundenlohn', MTP:'Mindestpensum', FIX:'Festpensum', 'FIX-M':'Management' };

        const rows = contracts.map(c => {
            const isActive = !c.contractEndDate;
            const lohn = c.salaryType === 'monthly' && c.monthlySalary
                ? `CHF ${Number(c.monthlySalary).toFixed(2)}/Mt.`
                : c.hourlyRate ? `CHF ${Number(c.hourlyRate).toFixed(2)}/h` : '–';
            const pensum = c.employmentPercentage != null ? `${c.employmentPercentage}%`
                         : c.weeklyHours != null ? `${c.weeklyHours}h/W` : '–';
            return `<tr class="${isActive ? 'ch-active' : 'ch-past'}">
                <td>${isActive ? '<span class="ch-badge-active">Aktiv</span>' : '<span class="ch-badge-past">Abgesch.</span>'}</td>
                <td>${modelLabel[c.employmentModel] ?? c.employmentModel ?? '–'}</td>
                <td>${fmt(c.contractStartDate)}</td>
                <td>${isActive ? '<em style="color:#94a3b8">offen</em>' : fmt(c.contractEndDate)}</td>
                <td>${pensum}</td>
                <td>${lohn}</td>
            </tr>`;
        }).join('');

        hist.innerHTML = `
            <div class="c-section-title" style="margin-top:24px">Vertragshistorie</div>
            <table class="ch-table">
                <thead><tr>
                    <th>Status</th><th>Modell</th><th>Von</th><th>Bis</th><th>Pensum</th><th>Lohn</th>
                </tr></thead>
                <tbody>${rows}</tbody>
            </table>`;
        hist.style.display = 'block';
    } catch { hist.style.display = 'none'; }
}

function showSnapshotBanner(snap) {
    const banner = document.getElementById('snapshotBanner');
    const content = document.getElementById('snapshotContent');
    const rows = [];
    if (snap.jobTitle) rows.push(`<label>Funktion</label><div class="c-readonly">${snap.jobTitle}</div>`);
    if (snap.employmentModel) rows.push(`<label>Vertragstyp</label><div class="c-readonly">${snap.employmentModel}</div>`);
    if (snap.hourlyRate) rows.push(`<label>Stundenlohn</label><div class="c-readonly">CHF ${Number(snap.hourlyRate).toFixed(2)}</div>`);
    if (snap.monthlySalaryFte) rows.push(`<label>Monatslohn 100%</label><div class="c-readonly">CHF ${Number(snap.monthlySalaryFte).toFixed(2)}</div>`);
    if (snap.monthlySalary && snap.monthlySalaryFte && Math.abs(snap.monthlySalary - snap.monthlySalaryFte) > 0.01)
        rows.push(`<label>Monatslohn (nach Pensum)</label><div class="c-readonly">CHF ${Number(snap.monthlySalary).toFixed(2)}</div>`);
    else if (snap.monthlySalary && !snap.monthlySalaryFte)
        rows.push(`<label>Monatslohn</label><div class="c-readonly">CHF ${Number(snap.monthlySalary).toFixed(2)}</div>`);
    const isFix = snap.employmentModel === 'FIX' || snap.employmentModel === 'FIX-M';
    const val = snap.guaranteedHoursPerWeek ?? snap.weeklyHours;
    if (val) rows.push(`<label>${isFix ? 'Pensum' : 'Wochenstunden'}</label><div class="c-readonly">${Math.round(val)}${isFix ? '%' : ' Std.'}</div>`);
    if (snap.jobGroupCode) rows.push(`<label>Funktionsgruppe</label><div class="c-readonly">${snap.jobGroupCode}</div>`);
    if (!rows.length) { banner.style.display = 'none'; return; }
    content.innerHTML = rows.join('');
    banner.style.display = 'block';
}

function applySnapshot() {
    if (!currentSnapshot) return;
    const snap = currentSnapshot;
    if (snap.employmentModel) { const em = document.getElementById('employmentModel'); if (em) em.value = snap.employmentModel; }
    if (snap.jobGroupCode) { const jg = document.getElementById('jobGroupCode'); if (jg) jg.value = snap.jobGroupCode; }
    if (snap.hourlyRate) { const hr = document.getElementById('hourlyRate'); if (hr) hr.value = Number(snap.hourlyRate).toFixed(2); }
    if (snap.monthlySalaryFte) { const fte = document.getElementById('monthlySalaryFte'); if (fte) fte.value = Number(snap.monthlySalaryFte).toFixed(2); }
    const isFix = snap.employmentModel === 'FIX' || snap.employmentModel === 'FIX-M';
    const v = snap.guaranteedHoursPerWeek ?? snap.weeklyHours;
    if (v) { if (isFix) { const p = document.getElementById('percentage'); if (p) p.value = Math.round(v); } else { const g = document.getElementById('guaranteedHours'); if (g) g.value = Math.round(v); } }
    // Tatsächlichen Lohn aus FTE × Pensum berechnen (nach dem Pensum-Feld gesetzt wurde)
    if (isFix && snap.monthlySalaryFte) { onFteSalaryChange(); }
    else if (snap.monthlySalary) { const ms = document.getElementById('monthlySalary'); if (ms) ms.value = Number(snap.monthlySalary).toFixed(2); }
    const nc = (snap.nationalityCode ?? snap.NationalityCode ?? '').toUpperCase();
    if (nc) { const natId = nationalityCodeToId[nc]; if (natId !== undefined) { const nat = document.getElementById('nationalityId'); if (nat) nat.value = natId; } }
    updateForm(); checkLive(); dismissSnapshot();
}

function dismissSnapshot() { document.getElementById('snapshotBanner').style.display = 'none'; }

async function loadJobGroups() {
    const sel = document.getElementById('jobGroupCode'); if (!sel) return;
    sel.innerHTML = '<option value="">Lade...</option>';
    try {
        const res = await fetch('/api/jobgroups', { headers: ah() });
        const data = await res.json(); sel.innerHTML = '';
        data.forEach(item => { const o = document.createElement('option'); o.value = item.code ?? item.Code; o.textContent = item.displayName ?? item.DisplayName ?? o.value; sel.appendChild(o); });
        if (!sel.options.length) sel.innerHTML = '<option value="">Keine Funktionen</option>';
    } catch { sel.innerHTML = '<option value="">Fehler</option>'; }
}

async function loadEducationLevels() {
    const sel = document.getElementById('educationLevelCode'); if (!sel) return;
    sel.innerHTML = '<option value="">Lade...</option>';
    try {
        const res = await fetch('/api/educationlevels', { headers: ah() });
        const data = await res.json();
        const order = ['Ia', 'Ib', 'II', 'IIIa', 'IIIb', 'IV'];
        data.sort((a, b) => { const cA = a.code ?? a.Code ?? ''; const cB = b.code ?? b.Code ?? ''; const iA = order.indexOf(cA); const iB = order.indexOf(cB); if (iA === -1 && iB === -1) return cA.localeCompare(cB); if (iA === -1) return 1; if (iB === -1) return -1; return iA - iB; });
        sel.innerHTML = '';
        data.forEach(item => { const o = document.createElement('option'); o.value = item.code ?? item.Code; o.textContent = item.name ?? item.Name ?? o.value; sel.appendChild(o); });
        const ia = Array.from(sel.options).find(o => o.value === 'Ia'); if (ia) sel.value = 'Ia';
    } catch { sel.innerHTML = '<option value="">Fehler</option>'; }
}

async function loadNationalities() {
    const sel = document.getElementById('nationalityId'); if (!sel) return;
    sel.innerHTML = '<option value="">Lade...</option>';
    try {
        const res = await fetch('/api/nationalities', { headers: ah() });
        const data = await res.json(); sel.innerHTML = ''; nationalityCodeToId = {};
        data.forEach(item => { const id = item.id ?? item.Id; const code = item.code ?? item.Code; const o = document.createElement('option'); o.value = id; o.textContent = item.name ?? item.Name ?? code; sel.appendChild(o); if (code) nationalityCodeToId[code.toUpperCase()] = id; });
    } catch { sel.innerHTML = '<option value="">Fehler</option>'; }
}

// Nationalität-Wechsel → QST-Befreit-Feld ein-/ausblenden
function onNationalityChange() {
    const sel     = document.getElementById('nationalityId');
    const selOpt  = sel?.options[sel.selectedIndex];
    const code    = selOpt ? Object.entries(nationalityCodeToId).find(([c,id]) => id == sel.value)?.[0] : null;
    const isCH    = code === 'CH';
    const show    = (id, visible) => { const el = document.getElementById(id); if (el) el.style.display = visible ? '' : 'none'; };
    show('qstBefreitLabel', !isCH);
    show('qstBefreitField', !isCH);
    show('qstStatusLabel',  !isCH);
    show('qstStatusField',  !isCH);
    show('btnSaveEmployeeData', true); // immer sichtbar sobald Nationalität gewählt
    if (!isCH) updateQstStatusDisplay();
}

function updateQstStatusDisplay() {
    const befreitVal = document.getElementById('quellensteuerBefreitAb')?.value;
    const el = document.getElementById('qstStatusDisplay');
    if (!el) return;
    if (!befreitVal) {
        el.innerHTML = '<span style="color:#dc2626;font-weight:600">● QST-pflichtig</span>';
    } else {
        const d = new Date(befreitVal);
        const today = new Date();
        if (d <= today) {
            el.innerHTML = `<span style="color:#16a34a;font-weight:600">● Befreit seit ${d.toLocaleDateString('de-CH')}</span>`;
        } else {
            el.innerHTML = `<span style="color:#f59e0b;font-weight:600">● QST-pflichtig, befreit ab ${d.toLocaleDateString('de-CH')}</span>`;
        }
    }
}

// Mitarbeiterstammdaten (Nationalität + QST-Befreit) speichern
async function saveEmployeeData() {
    const empId = parseInt(document.getElementById('employeeId')?.value, 10);
    if (!empId) { alert('Bitte zuerst einen Mitarbeiter auswählen.'); return; }

    const natId   = parseInt(document.getElementById('nationalityId')?.value) || null;
    const befreit = document.getElementById('quellensteuerBefreitAb')?.value || null;

    const zivilstand = document.getElementById('zivilstandSelect')?.value || null;
    const payload = {
        nationalityId:              natId,
        quellensteuerBefreitAbSet:  true,
        quellensteuerBefreitAb:     befreit,
        maritalStatus:              zivilstand,
    };

    const res = await fetch(`/api/employees/${empId}`, {
        method: 'PUT',
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
    });

    if (res.ok) {
        const btn = document.getElementById('btnSaveEmployeeData');
        if (btn) { btn.textContent = '✓ Gespeichert'; setTimeout(() => btn.textContent = '💾 Mitarbeiterdaten speichern', 2000); }
        updateQstStatusDisplay();
    } else {
        alert('Fehler beim Speichern: ' + await res.text());
    }
}

async function loadCompanyProfile() {
    try {
        const res = await fetch('/api/companyprofiles', { headers: ah() });
        const companies = await res.json();
        selectedCompanyProfile = companies.find(c => c.id === fixedCompanyProfileId) || null;
        const set = (id, val) => { const el = document.getElementById(id); if (el) el.innerText = val; };
        set('companyNameDisplay', selectedCompanyProfile?.companyName ?? '–');
        set('normalWeeklyHoursDisplay', selectedCompanyProfile?.normalWeeklyHours ?? '–');
        set('maxPartTimeHoursDisplay', selectedCompanyProfile?.maxPartTimeHoursPerWeek ?? '–');
        set('holdBackVacationPayoutDisplay', selectedCompanyProfile?.holdBackVacationPayout ? 'Ja' : 'Nein');
        set('payrollPeriodStartDayDisplay', selectedCompanyProfile?.payrollPeriodStartDay ?? '–');
    } catch {}
}

function showElement(id, visible) { const el = document.getElementById(id); if (!el) return; if (visible) el.classList.remove('hidden'); else el.classList.add('hidden'); }

function updateForm() {
    const model = document.getElementById('employmentModel')?.value;
    const salaryTypeInput = document.getElementById('salaryType');
    if (!model || !salaryTypeInput) return;
    ['hourlyRateLabel','hourlyRateField','monthlySalaryFteLabel','monthlySalaryFteField','monthlySalaryLabel','monthlySalaryField','percentageLabel','percentageField','guaranteedHoursLabel','guaranteedHoursField'].forEach(id => showElement(id, false));
    if (model === 'UTP') { salaryTypeInput.value = 'hourly'; showElement('hourlyRateLabel', true); showElement('hourlyRateField', true); }
    if (model === 'MTP') { salaryTypeInput.value = 'hourly'; showElement('hourlyRateLabel', true); showElement('hourlyRateField', true); showElement('guaranteedHoursLabel', true); showElement('guaranteedHoursField', true); }
    if (model === 'FIX' || model === 'FIX-M') { salaryTypeInput.value = 'monthly'; showElement('monthlySalaryFteLabel', true); showElement('monthlySalaryFteField', true); showElement('monthlySalaryLabel', true); showElement('monthlySalaryField', true); showElement('percentageLabel', true); showElement('percentageField', true); }
    updateContractRules(); calculateContractEndDate(); calculateProbationEndDate();
}

function updateContractRules() {
    const ec = document.getElementById('existingContract')?.value;
    const py = document.getElementById('probationYesNo');
    const fy = document.getElementById('fixedTermYesNo');
    if (!py || !fy) return;
    if (ec === 'yes') { py.value = 'no'; py.disabled = true; fy.value = 'no'; fy.disabled = true; } else { py.disabled = false; fy.disabled = false; }
    const pv = py.value === 'yes';
    showElement('probationMonthsLabel', pv); showElement('probationMonthsField', pv); showElement('probationEndDateLabel', pv); showElement('probationEndDateField', pv);
    const fv = fy.value === 'yes';
    showElement('fixedTermMonthsLabel', fv); showElement('fixedTermMonthsField', fv); showElement('calculatedEndDateLabel', fv); showElement('calculatedEndDateField', fv);
    if (!pv) { const el = document.getElementById('probationEndDate'); if (el) el.innerText = ''; }
    if (!fv) { const el = document.getElementById('calculatedEndDate'); if (el) el.innerText = ''; }
}

function addMonths(date, months) { const d = new Date(date); const day = d.getDate(); d.setMonth(d.getMonth() + months); if (d.getDate() < day) d.setDate(0); return d; }
function formatDate(date) { return `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`; }
function lastDayOfMonth(date) { return new Date(date.getFullYear(), date.getMonth() + 1, 0); }

function calculateContractEndDate() {
    const fy = document.getElementById('fixedTermYesNo')?.value;
    const sd = document.getElementById('startDate')?.value;
    const mv = parseInt(document.getElementById('fixedTermMonths')?.value || '0', 10);
    const model = document.getElementById('employmentModel')?.value;
    const target = document.getElementById('calculatedEndDate');
    if (!target) return null;
    if (fy !== 'yes' || !sd || !mv) { target.innerText = ''; return null; }
    const startDate = new Date(sd);
    const rawEnd = addMonths(startDate, mv); rawEnd.setDate(rawEnd.getDate() - 1);
    let calculatedEnd = new Date(rawEnd);
    if (model === 'FIX') { const eom = lastDayOfMonth(rawEnd); if (eom <= rawEnd) calculatedEnd = eom; else calculatedEnd = new Date(rawEnd.getFullYear(), rawEnd.getMonth(), 0); } else { calculatedEnd = rawEnd; }
    target.innerText = formatDate(calculatedEnd); return calculatedEnd;
}

function calculateProbationEndDate() {
    const py = document.getElementById('probationYesNo')?.value;
    const sd = document.getElementById('startDate')?.value;
    const mv = parseInt(document.getElementById('probationMonths')?.value || '0', 10);
    const target = document.getElementById('probationEndDate');
    if (!target) return null;
    if (py !== 'yes' || !sd) { target.innerText = ''; return null; }
    const end = addMonths(new Date(sd), mv); end.setDate(end.getDate() - 1);
    target.innerText = formatDate(end); return end;
}

function formatMoney(v) { if (v === null || v === undefined || v === '') return '–'; return `CHF ${Number(v).toFixed(2)}`; }

function renderMinimumWageStatus(result, salaryType) {
    const box = document.getElementById('minimumWageStatusBox');
    const summary = document.getElementById('minimumWageSummary');
    if (!result) { box?.classList.add('hidden'); if (summary) summary.innerHTML = ''; return; }
    box?.classList.remove('hidden');
    let html = '';
    if (salaryType === 'hourly') { html += `<div><strong>Mindest-Stundenlohn:</strong> ${formatMoney(result.minimumHourlyRate)}</div><div><strong>Aktuell:</strong> ${formatMoney(result.currentHourlyRate)}</div>`; }
    if (salaryType === 'monthly') { html += `<div><strong>Mindest-Monatslohn:</strong> ${formatMoney(result.minimumMonthlySalary)}</div><div><strong>Aktuell:</strong> ${formatMoney(result.currentMonthlySalary)}</div>`; }
    if (result.difference !== undefined) html += `<div><strong>Differenz:</strong> ${formatMoney(result.difference)}</div>`;
    if (result.status === 'UNDERPAID') { box?.classList.add('c-warn'); html += `<div class="txt-warn">Lohn zu tief</div>`; if (result.warningMessage) html += `<div>${result.warningMessage}</div>`; }
    else if (result.status === 'OK') { const diff = Number(result.difference ?? 0); if (diff > 0) { box?.classList.add('c-info'); html += `<div class="txt-info">Lohn zu hoch</div>`; } else { box?.classList.add('c-ok'); html += `<div class="txt-ok">Lohn ist in Ordnung ✓</div>`; } }
    else { box?.classList.add('c-info'); html += `<div class="txt-info">${result.warningMessage ?? 'Prüfung unvollständig.'}</div>`; }
    if (summary) summary.innerHTML = html;
}

async function checkLive() {
    const startDate = document.getElementById('startDate')?.value;
    const jobGroupCode = document.getElementById('jobGroupCode')?.value;
    const educationLevelCode = document.getElementById('educationLevelCode')?.value;
    const employmentModel = document.getElementById('employmentModel')?.value;
    const salaryType = document.getElementById('salaryType')?.value;
    const hourlyRateValue = document.getElementById('hourlyRate')?.value;
    const monthlySalaryValue = document.getElementById('monthlySalary')?.value;
    const guaranteedHoursValue = document.getElementById('guaranteedHours')?.value;
    const resultDiv = document.getElementById('liveResult');
    if (!resultDiv) return;
    if (!startDate || !jobGroupCode || !educationLevelCode) { resultDiv.innerHTML = '<span style="color:#94a3b8">Bitte Vertragsbeginn, Funktion und Ausbildung erfassen.</span>'; renderMinimumWageStatus(null, salaryType); return; }
    let html = '';
    if (selectedCompanyProfile) { html += `<p><strong>Betrieb:</strong> ${selectedCompanyProfile.companyName}</p>`; }
    if (employmentModel === 'MTP') {
        const hours = parseInt(guaranteedHoursValue || '0', 10);
        if (!hours || hours <= 0) html += `<p class="txt-warn">Bitte garantierte Stunden erfassen.</p>`;
        else { if (hours < 17) html += `<p class="txt-warn">MTP: Mindestens 17 Std./Woche empfohlen.</p>`; if (hours >= 33) html += `<p class="txt-info">Hinweis: Ab 33 Std. wäre FIX oft sinnvoller.</p>`; }
    }
    const pctEl = document.getElementById('percentage');
    const pct = pctEl && !pctEl.closest('.hidden') ? parseFloat(pctEl.value || '100') : 100;
    // Effective Date: Vertrag in Zukunft → Vertragsbeginn; sonst heute.
    // Schützt vor NO_RULE bei alten Verträgen (DB hat ggf. keine 2024/2025-Regeln).
    const _todayIso = new Date().toISOString().split('T')[0];
    const _effectiveDate = (startDate > _todayIso) ? startDate : _todayIso;
    // Geburtsdatum für altersabhängige Regel (z.B. unter 18 Jahre).
    // Backend-Property heisst dateOfBirth.
    const _birthDate = (typeof selectedVtEmployee !== 'undefined' && selectedVtEmployee?.dateOfBirth)
        ? selectedVtEmployee.dateOfBirth.slice(0, 10)
        : null;
    const body = { jobGroupCode, educationLevelCode, effectiveDate: _effectiveDate, employmentModel, employmentPercentage: pct, hourlyRate: salaryType === 'hourly' && hourlyRateValue ? parseFloat(hourlyRateValue) : null, monthlySalary: salaryType === 'monthly' && monthlySalaryValue ? parseFloat(monthlySalaryValue) : null, birthDate: _birthDate };
    try {
        const res = await fetch('/api/compliance/check-live', { method: 'POST', headers: ah(), body: JSON.stringify(body) });
        const result = await res.json(); lastComplianceResult = result;
        html += `<p><strong>Status:</strong> ${result.status}</p>`;
        if (result.minimumHourlyRate != null) html += `<p><strong>Mindest-Stundenlohn:</strong> ${formatMoney(result.minimumHourlyRate)}</p>`;
        if (result.currentHourlyRate != null) html += `<p><strong>Akt. Stundenlohn:</strong> ${formatMoney(result.currentHourlyRate)}</p>`;
        if (result.minimumMonthlySalary != null) { const p = result.employmentPercentage ?? 100; html += `<p><strong>Mindest-Monatslohn:</strong> ${formatMoney(result.minimumMonthlySalary)}${p < 100 && result.minimumMonthlySalaryFte != null ? ` <span class="txt-muted">(${p}% von ${formatMoney(result.minimumMonthlySalaryFte)})</span>` : ''}</p>`; }
        if (result.currentMonthlySalary != null) { const p = result.employmentPercentage ?? 100; html += `<p><strong>Akt. Monatslohn:</strong> ${formatMoney(result.currentMonthlySalary)}${p < 100 && result.currentMonthlySalaryFte != null ? ` <span class="txt-muted">(${p}% von ${formatMoney(result.currentMonthlySalaryFte)})</span>` : ''}</p>`; }
        if (result.difference != null) html += `<p><strong>Differenz:</strong> ${formatMoney(result.difference)}</p>`;
        resultDiv.innerHTML = html; renderMinimumWageStatus(result, salaryType);
    } catch { resultDiv.innerHTML = '<span class="txt-warn">Fehler bei der Live-Prüfung.</span>'; renderMinimumWageStatus(null, salaryType); }
}

function applyMinimumWageSuggestion() {
    if (!lastComplianceResult) return;
    const salaryType = document.getElementById('salaryType')?.value;
    if (salaryType === 'hourly' && lastComplianceResult.minimumHourlyRate != null) { const hr = document.getElementById('hourlyRate'); if (hr) hr.value = Number(lastComplianceResult.minimumHourlyRate).toFixed(2); }
    if (salaryType === 'monthly') {
        const fte = lastComplianceResult.minimumMonthlySalaryFte ?? lastComplianceResult.minimumMonthlySalary;
        if (fte != null) {
            const fteEl = document.getElementById('monthlySalaryFte');
            if (fteEl) { fteEl.value = Number(fte).toFixed(2); onFteSalaryChange(); }
        }
    }
    checkLive();
}

// Auto-berechnung: FTE-Lohn geändert → tatsächlicher Lohn neu berechnen
function onFteSalaryChange() {
    const fteVal = parseFloat(document.getElementById('monthlySalaryFte')?.value || '0');
    const pct    = parseFloat(document.getElementById('percentage')?.value || '100');
    const actual = Math.round(fteVal * pct / 100 * 100) / 100;
    const ms     = document.getElementById('monthlySalary');
    if (ms) ms.value = isNaN(actual) ? '' : actual.toFixed(2);
}

// Auto-berechnung: Pensum geändert → tatsächlicher Lohn neu berechnen
function onPensumChange() {
    onFteSalaryChange();
    checkLive();
}

function getVacationPercent(employeeId, startDate) {
    const dob = employeeDateOfBirthMap[employeeId];
    if (!dob || !startDate) return selectedCompanyProfile?.defaultVacationPercent5Weeks ?? 10.64;
    const birth = new Date(dob); const start = new Date(startDate);
    let age = start.getFullYear() - birth.getFullYear();
    if (start < new Date(start.getFullYear(), birth.getMonth(), birth.getDate())) age--;
    return age >= 50 ? (selectedCompanyProfile?.defaultVacationPercent6Weeks ?? 13.04) : (selectedCompanyProfile?.defaultVacationPercent5Weeks ?? 10.64);
}

async function saveEmployment() {
    const employeeId = parseInt(document.getElementById('employeeId')?.value, 10);
    const saveResult = document.getElementById('saveResult');
    if (!employeeId) { if (saveResult) saveResult.innerHTML = '<span class="txt-warn">Bitte zuerst einen Mitarbeiter auswählen.</span>'; return; }
    const employmentModel = document.getElementById('employmentModel')?.value;
    const salaryType = document.getElementById('salaryType')?.value;
    const startDate = document.getElementById('startDate')?.value;
    const jobGroupCode = document.getElementById('jobGroupCode')?.value;
    const fixedTermYesNo = document.getElementById('fixedTermYesNo')?.value;
    const probationYesNo = document.getElementById('probationYesNo')?.value;
    const payload = {
        employeeId, companyProfileId: fixedCompanyProfileId, employmentModel, salaryType,
        contractStartDate: startDate,
        contractEndDate: fixedTermYesNo === 'yes' ? document.getElementById('calculatedEndDate')?.innerText || null : null,
        jobTitle: jobGroupCode,
        contractType: fixedTermYesNo === 'yes' ? 'befristet' : 'unbefristet',
        employmentPercentage: (employmentModel === 'FIX' || employmentModel === 'FIX-M') ? parseFloat(document.getElementById('percentage')?.value) : null,
        weeklyHours: employmentModel === 'MTP' ? parseFloat(document.getElementById('guaranteedHours')?.value || '0') : null,
        guaranteedHoursPerWeek: employmentModel === 'MTP' ? parseFloat(document.getElementById('guaranteedHours')?.value || '0') : null,
        monthlySalaryFte: salaryType === 'monthly' ? parseFloat(document.getElementById('monthlySalaryFte')?.value || '0') : null,
        monthlySalary: salaryType === 'monthly' ? parseFloat(document.getElementById('monthlySalary')?.value || '0') : null,
        hourlyRate: salaryType === 'hourly' ? parseFloat(document.getElementById('hourlyRate')?.value || '0') : null,
        vacationPercent: salaryType === 'hourly' ? getVacationPercent(employeeId, startDate) : null,
        holidayPercent: salaryType === 'hourly' ? (selectedCompanyProfile?.defaultHolidayPercent ?? 2.27) : null,
        thirteenthSalaryPercent: 8.33,   // L-GAV Art. 12 — fix für alle Modelle
        vacationPaymentMode: selectedCompanyProfile?.holdBackVacationPayout ? 'vacation_account' : 'paid_with_salary',
        probationPeriodMonths: probationYesNo === 'yes' ? parseInt(document.getElementById('probationMonths')?.value || '0', 10) : null,
        probationEndDate: probationYesNo === 'yes' ? document.getElementById('probationEndDate')?.innerText || null : null,
        isActive: true
    };
    try {
        const res = await fetch('/api/employments', { method: 'POST', headers: ah(), body: JSON.stringify(payload) });
        if (!res.ok) { const text = await res.text(); if (saveResult) saveResult.innerHTML = `<span class="txt-warn">Fehler: ${text}</span>`; return; }
        const result = await res.json();
        const newId = result.employment?.id ?? result.id;
        let msg = `<span class="txt-ok">✓ Anstellung gespeichert.</span>`;
        if (result.previousContractClosed)
            msg += `<br><span style="font-size:12px;color:#f59e0b">⚠ ${result.previousContractClosed}</span>`;
        if (saveResult) saveResult.innerHTML = msg;
        const btnPdf = document.getElementById('btnPdf'); if (btnPdf) { btnPdf.disabled = false; btnPdf.dataset.employmentId = newId; }
    } catch { if (saveResult) saveResult.innerHTML = '<span class="txt-warn">Technischer Fehler.</span>'; }
}

async function downloadContractPdf() {
    const btnPdf = document.getElementById('btnPdf');
    const employmentId = btnPdf?.dataset.employmentId;
    if (!employmentId) return;
    btnPdf.textContent = '⏳ PDF wird erstellt…'; btnPdf.disabled = true;
    try {
        const res = await fetch(`/api/contracts/employment/${employmentId}/pdf`, { headers: { 'Authorization': `Bearer ${authToken}` } });
        if (!res.ok) { alert('Fehler beim PDF: ' + await res.text()); return; }
        const blob = await res.blob(); const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        const cd = res.headers.get('Content-Disposition') || '';
        const match = cd.match(/filename="?([^"]+)"?/);
        a.download = match ? match[1] : 'Vertrag.pdf'; a.href = url; a.click(); URL.revokeObjectURL(url);
    } catch (err) { alert('Fehler: ' + err.message); }
    finally { if (btnPdf) { btnPdf.textContent = '📄 Vertrag als PDF'; btnPdf.disabled = false; } }
}

function setupDropzone() {
    const dropzone = document.getElementById('dropzone');
    const fileInput = document.getElementById('csvFileInput');
    if (!dropzone || !fileInput) return;
    dropzone.addEventListener('click', () => fileInput.click());
    fileInput.addEventListener('change', e => setSelectedCsvFile(e.target.files?.[0] ?? null));
    dropzone.addEventListener('dragover', e => { e.preventDefault(); dropzone.classList.add('dragover'); });
    dropzone.addEventListener('dragleave', () => dropzone.classList.remove('dragover'));
    dropzone.addEventListener('drop', e => { e.preventDefault(); dropzone.classList.remove('dragover'); setSelectedCsvFile(e.dataTransfer.files?.[0] ?? null); });
}

function setSelectedCsvFile(file) {
    selectedCsvFile = file ?? null;
    const el = document.getElementById('importFileName');
    if (el) el.innerText = selectedCsvFile ? `Ausgewählt: ${selectedCsvFile.name}` : 'Keine Datei ausgewählt.';
}

async function uploadEmployeeCsv() {
    const resultBox = document.getElementById('importResult');
    if (!selectedCsvFile) { if (resultBox) resultBox.innerHTML = '<span class="txt-warn">Bitte zuerst eine CSV-Datei auswählen.</span>'; return; }
    const formData = new FormData(); formData.append('file', selectedCsvFile);
    if (resultBox) resultBox.innerHTML = '<span style="color:#64748b">Import läuft...</span>';
    try {
        const res = await fetch(`/api/employeeimport/upload/${fixedCompanyProfileId}`, { method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: formData });
        const text = await res.text();
        if (!res.ok) { if (resultBox) resultBox.innerHTML = `<span class="txt-warn">Fehler: ${text}</span>`; return; }
        const result = JSON.parse(text);
        if (resultBox) resultBox.innerHTML = `<div class="txt-ok">Import erfolgreich.</div><div>Importiert: ${result.importedRows ?? 0} | Neu: ${result.inserted ?? 0} | Aktualisiert: ${result.updated ?? 0} | Reaktiviert: ${result.reactivated ?? 0} | Inaktiv: ${result.deactivated ?? 0}</div>${result.message ? `<div class="txt-info">${result.message}</div>` : ''}`;
        await loadEmployees();
    } catch { if (resultBox) resultBox.innerHTML = '<span class="txt-warn">Technischer Fehler.</span>'; }
}


// ══════════════════════════════════════════════
// VERTRAGS-IMPORT (pro Mitarbeiter)
// ══════════════════════════════════════════════
let vtImportEmployeeId   = null;
let vtImportEmployeeNr   = null;
let vtImportFile         = null;
let vtImportSnapshotData = null;

function openVtImport(empId, empNr) {
    vtImportEmployeeId   = empId;
    vtImportEmployeeNr   = empNr;
    vtImportFile         = null;
    vtImportSnapshotData = null;
    document.getElementById('vtImportEmpName').textContent = 
        selectedVtEmployee ? `${selectedVtEmployee.firstName} ${selectedVtEmployee.lastName}` : '';
    document.getElementById('vtImportAlert').innerHTML   = '';
    document.getElementById('vtImportPreview').style.display = 'none';
    document.getElementById('vtImportPreview').innerHTML = '';
    document.getElementById('vtImportConfirmBtn').style.display = 'none';
    document.getElementById('vtImportDropZone').style.display = 'block';
    document.getElementById('vtImportFile').value = '';
    document.getElementById('vtImportModalBg').style.display = 'flex';
}

function closeVtImport() {
    document.getElementById('vtImportModalBg').style.display = 'none';
}

function vtImportHandleDrop(event) {
    event.preventDefault();
    document.getElementById('vtImportDropZone').style.borderColor = '#cbd5e1';
    const file = event.dataTransfer.files[0];
    if (file && file.name.endsWith('.csv')) vtImportFileChosen(file);
    else document.getElementById('vtImportAlert').innerHTML = '<div class="alert alert-err">Bitte eine CSV-Datei verwenden.</div>';
}

async function vtImportFileChosen(file) {
    if (!file) return;
    vtImportFile = file;
    const alertEl   = document.getElementById('vtImportAlert');
    const previewEl = document.getElementById('vtImportPreview');
    alertEl.innerHTML = '<div style="color:#64748b">Datei wird analysiert...</div>';
    previewEl.style.display = 'none';
    document.getElementById('vtImportConfirmBtn').style.display = 'none';

    try {
        // CSV hochladen und Snapshot für diesen MA laden
        const formData = new FormData();
        formData.append('file', file);
        const res = await fetch(`/api/employeeimport/upload/${currentBranchId || 1}`, {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: formData
        });
        if (!res.ok) { alertEl.innerHTML = `<div class="alert alert-err">Fehler beim Import: ${await res.text()}</div>`; return; }

        // Snapshot für diesen Mitarbeiter laden
        const snapRes = await fetch(`/api/employeeimportsnapshot/latest/${vtImportEmployeeId}`, { headers: ah() });
        if (!snapRes.ok) {
            alertEl.innerHTML = '<div class="alert alert-err">Kein Eintrag für diesen Mitarbeiter in der CSV gefunden.</div>';
            return;
        }
        vtImportSnapshotData = await snapRes.json();
        alertEl.innerHTML = '';

        // Statt Read-Only-Vorschau: Edit-Modal im Import-Modus öffnen, vorbefüllt aus Snapshot
        const s = vtImportSnapshotData;
        const isFixSnap = s.employmentModel === 'FIX' || s.employmentModel === 'FIX-M';
        const pctSnap = s.employmentPercentage ?? (isFixSnap && s.weeklyHours ? Math.round(s.weeklyHours) : null);
        const monthlySalarySnap = isFixSnap && s.monthlySalaryFte && pctSnap
            ? Math.round(s.monthlySalaryFte * pctSnap / 100 * 100) / 100
            : s.monthlySalary;
        const today = new Date().toISOString().split('T')[0];
        const importContract = {
            id: null,
            employeeId: vtImportEmployeeId,
            employmentModel: s.employmentModel || 'UTP',
            jobTitle: s.jobTitle ?? '',
            jobGroupCode: s.jobGroupCode ?? selectedVtEmployee?.jobGroupCode ?? '',
            educationLevelCode: s.educationLevelCode ?? selectedVtEmployee?.educationLevelCode ?? '',
            contractStartDate: s.contractStartDate || today,
            contractEndDate: s.contractEndDate || null,
            employmentPercentage: isFixSnap ? pctSnap : null,
            weeklyHours: !isFixSnap ? s.weeklyHours : null,
            guaranteedHoursPerWeek: s.guaranteedHoursPerWeek ?? null,
            hourlyRate: !isFixSnap ? s.hourlyRate : null,
            monthlySalaryFte: isFixSnap ? s.monthlySalaryFte : null,
            monthlySalary: isFixSnap ? monthlySalarySnap : null,
            vacationPercent: s.vacationPercent ?? null,
            holidayPercent: s.holidayPercent ?? null,
            thirteenthSalaryPercent: s.thirteenthSalaryPercent ?? null,
            probationPeriodMonths: s.probationPeriodMonths ?? null,
            isActive: true
        };
        closeVtImport();
        await openContractEditModal(importContract, 'import');
    } catch(e) {
        alertEl.innerHTML = `<div class="alert alert-err">Fehler: ${e.message}</div>`;
    }
}

async function confirmVtImport() {
    if (!vtImportSnapshotData || !vtImportEmployeeId) return;
    const btn = document.getElementById('vtImportConfirmBtn');
    btn.textContent = '⏳ Wird importiert...'; btn.disabled = true;
    try {
        // Vertrag aus Snapshot erstellen
        const s = vtImportSnapshotData;
        const isFix = s.employmentModel === 'FIX' || s.employmentModel === 'FIX-M';
        // Pensum: aus employmentPercentage oder weeklyHours (wenn FIX)
        const pct = s.employmentPercentage ?? (isFix && s.weeklyHours ? Math.round(s.weeklyHours) : null);
        // Lohn: FTE × Pensum neu berechnen
        const monthlySalary = isFix && s.monthlySalaryFte && pct
            ? Math.round(s.monthlySalaryFte * pct / 100 * 100) / 100
            : s.monthlySalary;
        const isBefristet = !!s.contractEndDate;
        const payload = {
            employeeId:          vtImportEmployeeId,
            companyProfileId:    currentBranchId || 1,
            employmentModel:     s.employmentModel,
            salaryType:          isFix ? 'monthly' : 'hourly',
            contractStartDate:   new Date().toISOString().split('T')[0],
            jobTitle:            s.jobTitle,
            weeklyHours:         isFix ? null : s.weeklyHours,
            guaranteedHoursPerWeek: s.guaranteedHoursPerWeek,
            hourlyRate:          s.hourlyRate,
            monthlySalary:       monthlySalary,
            monthlySalaryFte:    s.monthlySalaryFte,
            employmentPercentage: isFix ? pct : null,
            contractType:        isBefristet ? 'befristet' : 'unbefristet',
            contractEndDate:     isBefristet ? new Date(s.contractEndDate).toISOString().split('T')[0] : null,
            contractEndDateSet:  true,
            isActive:            true,
        };
        const res = await fetch('/api/employments', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (!res.ok) { 
            document.getElementById('vtImportAlert').innerHTML = `<div class="alert alert-err">Fehler: ${await res.text()}</div>`;
            return; 
        }
        closeVtImport();
        // MA neu laden und Detail aktualisieren
        const empRes = await fetch('/api/employees', { headers: ah() });
        const emps = await empRes.json();
        allVtEmployees = emps.filter(e => e.isActive && e.employments?.length > 0);
        selectedVtEmployee = allVtEmployees.find(e => e.id === vtImportEmployeeId);
        if (selectedVtEmployee) renderVtDetail(selectedVtEmployee);
        renderVtList(allVtEmployees);
    } catch(e) {
        document.getElementById('vtImportAlert').innerHTML = `<div class="alert alert-err">Fehler: ${e.message}</div>`;
    } finally {
        btn.textContent = 'Vertrag importieren'; btn.disabled = false;
    }
}

