// ══════════════════════════════════════════════════════════════════════
// contracts-page.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════
// CONTRACT PAGE
// ══════════════════════════════════════════════
// ══════════════════════════════════════════════
// VERTRÄGE – Liste + Detail
// ══════════════════════════════════════════════
let allEmpData = [];
let selectedEmp = null;
let allVtEmployees = [];
let selectedVtEmployee = null;

// ── MITARBEITER LIST ─────────────────────────────────
// Mitarbeiter-Liste-Logik liegt jetzt in /wwwroot/employees.js
// (loadMitarbeiterList, applyEmpFilter, setEmpFilter, renderEmployeeList, filterEmployeeList)

function renderEmpList(employees) {
    const listEl = document.getElementById('empList');
    if (!listEl) return;
    if (!employees.length) {
        listEl.innerHTML = '<div style="padding:20px;text-align:center;color:#94a3b8">Keine Mitarbeiter gefunden</div>';
        return;
    }
    listEl.innerHTML = employees.map(e => {
        const initials = ((e.firstName||'')[0]||'') + ((e.lastName||'')[0]||'');
        const isFemale = e.gender === 'female';
        const isSelected = selectedEmp?.id === e.id;
        const model = e.employments?.find(v => v.isActive)?.employmentModel || '';
        const modelColor = { MTP:'#d1fae5', UTP:'#fef3c7', FIX:'#dbeafe', 'FIX-M':'#ede9fe' };
        return `<div class="emp-list-item ${isSelected ? 'active' : ''}" onclick="selectEmployee(${e.id})">
            <div class="emp-avatar ${isFemale ? 'female' : ''}">${initials}</div>
            <div>
                <div class="emp-list-name">${e.firstName} ${e.lastName}</div>
                <div class="emp-list-nr">${e.employeeNumber || ''}</div>
            </div>
            ${model ? `<span style="margin-left:auto;font-size:10px;font-weight:600;padding:2px 6px;border-radius:8px;background:${modelColor[model]||'#f1f5f9'};flex-shrink:0">${model}</span>` : ''}
        </div>`;
    }).join('');
}

function filterEmployeeList() {
    const q = document.getElementById('empSearch')?.value.toLowerCase().trim() || '';
    if (!q) { renderEmpList(allEmpData); return; }
    renderEmpList(allEmpData.filter(e =>
        `${e.firstName} ${e.lastName}`.toLowerCase().includes(q) ||
        (e.employeeNumber||'').includes(q)
    ));
}

async function selectEmployee(id) {
    selectedEmp = allEmpData.find(e => e.id === id) || null;
    renderEmpList(allEmpData.filter(e => {
        const q = document.getElementById('empSearch')?.value.toLowerCase().trim() || '';
        return !q || `${e.firstName} ${e.lastName}`.toLowerCase().includes(q) || (e.employeeNumber||'').includes(q);
    }));
    if (!selectedEmp) return;
    const panel = document.getElementById('empDetailPanel');
    if (!panel) return;
    // Load contracts
    let contracts = [];
    try {
        const r = await fetch(`/api/employments/employee/${id}`, { headers: ah() });
        if (r.ok) contracts = await r.json();
    } catch {}
    renderEmpDetail(selectedEmp, contracts, panel);
}

function renderEmpDetail(emp, contracts, panel) {
    const name = `${emp.firstName} ${emp.lastName}`.trim();
    const active = contracts.filter(c => c.isActive).sort((a,b) =>
        new Date(b.contractStartDate) - new Date(a.contractStartDate))[0];
    panel.innerHTML = `
    <div class="emp-detail-header">
        <div style="display:flex;align-items:center;justify-content:space-between">
            <div class="emp-detail-name">${name}</div>
        </div>
        <div class="emp-detail-meta">Personal-Nr. ${emp.employeeNumber || '–'} &nbsp;·&nbsp; ${contracts.length} Vertrag${contracts.length !== 1 ? 'e' : ''}</div>
        <div class="emp-detail-tabs">
            <div class="emp-tab active" onclick="empSwitchTab(this,'empTabPersonal')">Personal</div>
            <div class="emp-tab" onclick="empSwitchTab(this,'empTabVertraege')">Verträge</div>
        </div>
    </div>
    <div class="emp-detail-body">
        <div class="emp-tab-content active" id="empTabPersonal">
            <div class="emp-section-title">Persönliche Daten</div>
            <div class="emp-field-grid">
                ${empField('Vorname', emp.firstName)}
                ${empField('Nachname', emp.lastName)}
                ${empField('Geburtsdatum', emp.dateOfBirth ? new Date(emp.dateOfBirth).toLocaleDateString('de-CH') : '–')}
                ${empField('Geschlecht', emp.gender === 'female' ? 'Weiblich' : emp.gender === 'male' ? 'Männlich' : '–')}
                ${empField('E-Mail', emp.email)}
                ${empField('Telefon', emp.phone)}
                ${empField('Adresse', emp.address)}
                ${empField('PLZ / Ort', [emp.postalCode, emp.city].filter(Boolean).join(' '))}
                ${empField('Region', emp.region)}
                ${empField('Nationalität', emp.nationality?.name || '–')}
                ${empField('AHV-Nummer', emp.ahvNumber)}
                ${empField('Im Betrieb seit', emp.hiredDate ? new Date(emp.hiredDate).toLocaleDateString('de-CH') : '–')}
            </div>
        </div>
        <div class="emp-tab-content" id="empTabVertraege">
            <div class="emp-section-title">Verträge</div>
            ${contracts.length ? contracts.sort((a,b) => new Date(b.contractStartDate)-new Date(a.contractStartDate)).map(c => `
            <div style="background:${c.isActive?'#f0fdf4':'#f8fafc'};border:1px solid ${c.isActive?'#bbf7d0':'#e2e8f0'};border-radius:10px;padding:14px 16px;margin-bottom:10px">
                <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:8px">
                <div style="display:flex;align-items:center;gap:8px">
                    <span style="font-weight:600;font-size:13px">${c.employmentModel} – ${c.jobTitle||'–'}</span>
                    ${c.isActive ? '<span style="font-size:11px;background:#dcfce7;color:#15803d;padding:2px 8px;border-radius:10px;font-weight:600">Aktiv</span>' : '<span style="font-size:11px;background:#f1f5f9;color:#64748b;padding:2px 8px;border-radius:10px">Inaktiv</span>'}
                </div>
                <div style="display:grid;grid-template-columns:1fr 1fr;gap:6px;font-size:12px;color:#64748b">
                    <div>Von: <b style="color:#374151">${new Date(c.contractStartDate).toLocaleDateString('de-CH')}</b></div>
                    <div>Bis: <b style="color:#374151">${c.contractEndDate ? new Date(c.contractEndDate).toLocaleDateString('de-CH') : 'Unbefristet'}</b></div>
                    ${c.hourlyRate ? `<div>Stundenlohn: <b style="color:#374151">CHF ${Number(c.hourlyRate).toFixed(2)}</b></div>` : ''}
                    ${c.monthlySalary ? `<div>Monatslohn: <b style="color:#374151">CHF ${Number(c.monthlySalary).toFixed(2)}</b></div>` : ''}
                    ${c.employmentPercentage ? `<div>Pensum: <b style="color:#374151">${c.employmentPercentage}%</b></div>` : ''}
                    ${c.guaranteedHoursPerWeek ? `<div>Garantierte Std.: <b style="color:#374151">${c.guaranteedHoursPerWeek}h/Wo</b></div>` : ''}
                </div>
            </div>`).join('') : '<div style="color:#94a3b8;font-size:13px">Keine Verträge vorhanden</div>'}
        </div>
    </div>`;
}

