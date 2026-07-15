// ══════════════════════════════════════════════════════════════════════
// austritt.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ═══════════════ AUSTRITT ERFASSEN ═══════════════
let terminateCtx = { employeeId: null, employmentId: null, startDate: null };

async function openTerminateModal(employeeId, employmentId, startDate) {
    terminateCtx = { employeeId, employmentId, startDate };
    const modal  = document.getElementById('terminateModal');
    const dateEl = document.getElementById('terminateDate');
    const subEl  = document.getElementById('terminateSub');
    const alert  = document.getElementById('terminateAlert');
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
    if (alert) alert.innerHTML = '';
    // Default: Ende aktueller Monat (timezone-sicher als YYYY-MM-DD bauen,
    // toISOString() würde wegen UTC-Konvertierung einen Tag abziehen).
    const now = new Date();
    const end = new Date(now.getFullYear(), now.getMonth() + 1, 0);
    if (dateEl) dateEl.value = isoLocalDate(end);
    // Subtitle = MA-Name aus selectedVtEmployee, falls verfügbar
    const emp = (typeof selectedVtEmployee !== 'undefined' && selectedVtEmployee?.id === employeeId)
        ? selectedVtEmployee : null;
    if (subEl) subEl.textContent = emp ? `${emp.firstName} ${emp.lastName} · ${_t('vt.label.personalNr')} ${emp.employeeNumber || '–'}` : '';
    if (modal) modal.style.display = 'flex';
    if (window.i18n && window.i18n.applyAll) window.i18n.applyAll(modal);

    // Walter-Vorgabe 17.05.2026: Austrittsdatum darf nicht in einer laufenden
    // Lohnperiode liegen. min-date setzen damit User gar nicht erst falsch
    // klicken kann; default ggf. nach vorne ziehen.
    if (dateEl && window.lohnEditLock) {
        const activeEmp = emp?.employments?.find(x => x.isActive) || emp?.employments?.[0];
        const cpId = activeEmp?.companyProfileId
                  || (typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null);
        if (cpId) {
            const state = await window.lohnEditLock.loadState(Number(cpId));
            window.lohnEditLock.applyToDateInput(dateEl, state);
            if (state.firstAllowedDate && dateEl.value && dateEl.value < state.firstAllowedDate) {
                // Default war Ende aktueller Monat — wenn der in der gesperrten
                // Periode liegt, springen wir auf Ende des nächsten freien Monats.
                const fa = new Date(state.firstAllowedDate + 'T12:00:00');
                const nextEnd = new Date(fa.getFullYear(), fa.getMonth() + 1, 0);
                dateEl.value = isoLocalDate(nextEnd);
            }
        }
    }

    checkTerminateDate();
}

function closeTerminateModal() {
    const modal = document.getElementById('terminateModal');
    if (modal) modal.style.display = 'none';
    const sumWrap = document.getElementById('terminateSummary');
    if (sumWrap) sumWrap.style.display = 'none';
}

function setTerminateDateToMonthEnd(offsetMonths) {
    const dateEl = document.getElementById('terminateDate');
    if (!dateEl) return;
    const now = new Date();
    const end = new Date(now.getFullYear(), now.getMonth() + 1 + (offsetMonths || 0), 0);
    dateEl.value = isoLocalDate(end);
    checkTerminateDate();
}

// Helper: YYYY-MM-DD aus lokalen Date-Komponenten (timezone-sicher)
function isoLocalDate(d) {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
}

function checkTerminateDate() {
    const dateEl = document.getElementById('terminateDate');
    const hint   = document.getElementById('terminateDateHint');
    if (!dateEl || !hint) return;
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    const val = dateEl.value;
    if (!val) { hint.textContent = ''; return; }
    const d = new Date(val + 'T12:00:00');
    const lastDay = new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
    if (d.getDate() === lastDay) {
        hint.style.color = '#16a34a';
        hint.textContent = _t('austritt.hint.monthEnd', { date: d.toLocaleDateString('de-CH') });
    } else {
        hint.style.color = '#d97706';
        hint.textContent = _t('austritt.hint.notMonthEnd');
    }
    // Punktlandungs-Vorschau live nachladen
    loadTerminateSummary(val);
}

// Lädt die Austritts-Vorschau vom Backend und rendert sie ins Modal.
let terminateSummaryDebounce = null;
function loadTerminateSummary(exitDate) {
    if (terminateSummaryDebounce) clearTimeout(terminateSummaryDebounce);
    terminateSummaryDebounce = setTimeout(() => doLoadTerminateSummary(exitDate), 250);
}
async function doLoadTerminateSummary(exitDate) {
    const wrap   = document.getElementById('terminateSummary');
    const body   = document.getElementById('terminateSummaryBody');
    const empId  = terminateCtx.employmentId;
    if (!wrap || !body || !empId || !exitDate) return;
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    body.innerHTML = `<div style="color:#94a3b8;font-style:italic">${_t('austritt.loading')}</div>`;
    wrap.style.display = '';
    try {
        const res = await fetch(`/api/employments/${empId}/exit-summary?exitDate=${exitDate}`, {
            headers: { 'Authorization': `Bearer ${authToken}` }
        });
        if (!res.ok) {
            body.innerHTML = `<div style="color:#dc2626">${_t('austritt.err.loadPreview')}</div>`;
            return;
        }
        const s = await res.json();
        body.innerHTML = renderTerminateSummary(s);
    } catch (e) {
        body.innerHTML = `<div style="color:#dc2626">${_t('austritt.err.network', { msg: e.message })}</div>`;
    }
}

function renderTerminateSummary(s) {
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    const fmtH = v => (v == null ? '–' : Number(v).toFixed(2) + ' h');
    const fmtT = v => (v == null ? '–' : _t('austritt.unit.days', { n: Number(v).toFixed(2) }));
    const fmtChf = v => (v == null ? '–' : 'CHF ' + Number(v).toFixed(2));
    const stichtag = s.saldoStand
        ? new Date(s.saldoStand).toLocaleDateString('de-CH')
        : '–';

    // Stunden-Sektion (nur FIX/FIX-M/MTP)
    let stundenBlock = '';
    if (s.isFixOrFixM || s.isMtp) {
        const colorStunden = s.stundenNochZuLeisten > 0.5 ? '#d97706'
                          : s.stundenNochZuLeisten < -0.5 ? '#dc2626'
                          : '#16a34a';
        const labelStunden = s.stundenNochZuLeisten > 0.5
            ? _t('austritt.status.hoursOwed')
            : s.stundenNochZuLeisten < -0.5
                ? _t('austritt.status.hoursOver')
                : _t('austritt.status.cleanLanding');
        stundenBlock = `
        <div style="margin-bottom:14px">
            <div style="font-size:11px;font-weight:600;color:#64748b;margin-bottom:6px">${_t('austritt.section.hours')}</div>
            <div style="display:grid;grid-template-columns:1fr auto;row-gap:4px;font-size:12px">
                <div style="color:#64748b">${_t('austritt.label.balanceAt', { date: stichtag })}</div>
                <div style="font-weight:600;text-align:right">${fmtH(s.hourSaldo)}</div>
                <div style="color:#64748b">${_t('austritt.label.targetHours', { days: s.remainingDays })}</div>
                <div style="font-weight:600;text-align:right">${fmtH(s.sollStundenRest)}</div>
                <div style="color:${colorStunden};font-weight:600;border-top:1px solid #e2e8f0;padding-top:4px">${labelStunden}</div>
                <div style="color:${colorStunden};font-weight:700;text-align:right;border-top:1px solid #e2e8f0;padding-top:4px">${fmtH(Math.abs(s.stundenNochZuLeisten))}</div>
            </div>
        </div>`;
    }

    // Ferien-Sektion
    const endSaldo = Number(s.ferienErwarteterSaldoBeiAustritt) || 0;
    const colorFerien = endSaldo > 0.5 ? '#d97706'
                      : endSaldo < -0.5 ? '#dc2626'
                      : '#16a34a';
    const labelFerien = endSaldo > 0.5
        ? _t('austritt.status.vacUseUp')
        : endSaldo < -0.5
            ? _t('austritt.status.vacOver')
            : _t('austritt.status.cleanLanding');
    const ferienBlock = `
    <div style="margin-bottom:14px">
        <div style="font-size:11px;font-weight:600;color:#64748b;margin-bottom:6px">${_t('austritt.section.vacation')}</div>
        <div style="display:grid;grid-template-columns:1fr auto;row-gap:4px;font-size:12px">
            <div style="color:#64748b">${_t('austritt.label.balanceAt', { date: stichtag })}</div>
            <div style="font-weight:600;text-align:right">${fmtT(s.ferienTageSaldo)}</div>
            <div style="color:#64748b">${_t('austritt.label.vacEntitlement', { days: s.remainingDays })}</div>
            <div style="font-weight:600;text-align:right">${fmtT(s.ferienAnspruchRest)}</div>
            <div style="color:${colorFerien};font-weight:600;border-top:1px solid #e2e8f0;padding-top:4px">${labelFerien}</div>
            <div style="color:${colorFerien};font-weight:700;text-align:right;border-top:1px solid #e2e8f0;padding-top:4px">${fmtT(Math.abs(endSaldo))}</div>
        </div>
    </div>`;

    // Feiertag-Saldo + 13.ML + Ferien-Geld als kompakte Liste
    const restBlock = `
    <div style="margin-bottom:4px">
        <div style="font-size:11px;font-weight:600;color:#64748b;margin-bottom:6px">${_t('austritt.section.payout')}</div>
        <div style="display:grid;grid-template-columns:1fr auto;row-gap:4px;font-size:12px">
            ${s.feiertagTageSaldo ? `<div style="color:#64748b">${_t('austritt.payout.holiday')}</div><div style="font-weight:600;text-align:right">${fmtT(s.feiertagTageSaldo)}</div>` : ''}
            ${s.ferienGeldSaldo ? `<div style="color:#64748b">${_t('austritt.payout.vacMoney')}</div><div style="font-weight:600;text-align:right">${fmtChf(s.ferienGeldSaldo)}</div>` : ''}
            <div style="color:#64748b">${_t('austritt.payout.thirteenth', { date: stichtag })}</div>
            <div style="font-weight:600;text-align:right">${fmtChf(s.thirteenthAccumulated)}</div>
        </div>
    </div>`;

    const noSaldo = !s.saldoVorhanden
        ? `<div style="background:#fffbeb;border:1px solid #fde68a;color:#92400e;padding:8px 10px;border-radius:6px;font-size:11px;margin-bottom:10px">${_t('austritt.warn.noPeriod')}</div>`
        : '';

    // Saldo-Quelle transparent anzeigen (Periode + Status)
    const saldoInfo = s.saldoVorhanden
        ? `<div style="background:#f6f3ee;border:1px solid #e5e0d6;color:#6b6152;padding:6px 10px;border-radius:6px;font-size:10px;margin-bottom:10px">${_t('austritt.info.balanceFrom', { month: String(s.saldoQuelleMonth).padStart(2,'0'), year: s.saldoQuelleYear, status: s.saldoQuelleStatus ? _t('austritt.info.statusSuffix', { status: s.saldoQuelleStatus }) : '' })}</div>`
        : '';

    return noSaldo + saldoInfo + stundenBlock + ferienBlock + restBlock;
}

async function saveTerminate() {
    const dateEl = document.getElementById('terminateDate');
    const alert  = document.getElementById('terminateAlert');
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    if (!dateEl || !dateEl.value) {
        alert.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px">${_t('austritt.err.dateRequired')}</div>`;
        return;
    }
    const exitDate = dateEl.value;
    const { employmentId, employeeId } = terminateCtx;
    try {
        const res = await fetch(`/api/employments/${employmentId}/terminate`, {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}`, 'Content-Type': 'application/json' },
            body: JSON.stringify({ exitDate })
        });
        // Lohnlauf-Sperre: zeigt die Backend-Meldung direkt im Alert-Block.
        if (res.status === 409) {
            const body = await res.clone().json().catch(() => ({}));
            if (body && body.error === 'LOHN_EDIT_LOCKED') {
                alert.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px">${body.message}</div>`;
                if (window.lohnEditLock) window.lohnEditLock.invalidateCache();
                return;
            }
        }
        if (!res.ok) {
            const err = await res.json().catch(() => ({ error: _t('austritt.err.unknown') }));
            alert.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px">${_t('austritt.err.failed', { msg: err.error })}</div>`;
            return;
        }
        // Walter 14.06.2026: Austritt aendert isActive + contractEndDate
        // → MA-Picker-Cache invalidieren.
        if (typeof invalidateEmployeeLookupCache === 'function') invalidateEmployeeLookupCache();
        closeTerminateModal();
        // Vertragsliste neu laden
        if (typeof selectVtEmployee === 'function' && employeeId) {
            // selectedVtEmployee neu laden
            const refreshed = await fetch(`/api/employees/${employeeId}`, {
                headers: { 'Authorization': `Bearer ${authToken}` }
            });
            if (refreshed.ok) {
                const emp = await refreshed.json();
                // In allVtEmployees ersetzen, damit selectVtEmployee die neuen Daten findet
                if (typeof allVtEmployees !== 'undefined') {
                    const idx = allVtEmployees.findIndex(e => e.id === employeeId);
                    if (idx >= 0) allVtEmployees[idx] = emp;
                }
                selectVtEmployee(employeeId);
            }
        }
    } catch (e) {
        alert.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px">${_t('austritt.err.network', { msg: e.message })}</div>`;
    }
}

async function downloadContractPdfById(employeeId, contractId) {
    const btn = event?.target;
    if (btn) { btn.textContent = '⏳…'; btn.disabled = true; }
    try {
        const res = await fetch(`/api/contracts/employment/${contractId}/pdf`, {
            headers: { 'Authorization': `Bearer ${authToken}` }
        });
        if (!res.ok) { alert('Fehler beim PDF: ' + await res.text()); return; }
        const blob = await res.blob();
        const cd   = res.headers.get('Content-Disposition') || '';
        const match = cd.match(/filename="?([^"]+)"?/);
        await previewFileModal(blob, match ? match[1] : 'Vertrag.pdf');
    } catch (err) { alert('Fehler: ' + err.message); }
    finally { if (btn) { btn.textContent = '📄 PDF'; btn.disabled = false; } }
}


// ═══════════════ ARBEITSZEUGNIS (Walter-Vorgabe 14.07.2026) ═══════════════
// Drei Qualitätsstufen (Durchschnitt/Gut/Sehr gut) + Mehrfachauswahl der
// verrichteten Arbeit (Küche/Kasse/Drive). PDF im Vorschaufenster.
let _azEmployeeId = null;

function openZeugnisModal(employeeId) {
    _azEmployeeId = employeeId;
    let ov = document.getElementById('azModal');
    if (!ov) {
        ov = document.createElement('div');
        ov.id = 'azModal';
        ov.style.cssText = 'position:fixed;inset:0;z-index:4000;background:rgba(60,55,48,0.4);display:flex;align-items:center;justify-content:center;padding:20px';
        ov.onclick = e => { if (e.target === ov) ov.style.display = 'none'; };
        const pill = 'display:flex;align-items:center;gap:8px;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:10px 14px;cursor:pointer;font-size:13.5px;font-weight:600;color:#3f3f3f';
        ov.innerHTML = `
            <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:18px;max-width:460px;width:100%;padding:20px 22px;box-shadow:0 24px 60px rgba(60,55,48,0.22)">
                <div style="font-size:16px;font-weight:700;color:#3f3f3f;margin-bottom:2px">Arbeitszeugnis erstellen</div>
                <div id="azSub" style="font-size:12.5px;color:#8b8b8b;margin-bottom:14px"></div>

                <div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;letter-spacing:0.4px;margin-bottom:6px">Qualität</div>
                <div style="display:flex;flex-direction:column;gap:8px;margin-bottom:16px">
                    <label style="${pill}"><input type="radio" name="azQuali" value="sehr_gut"> Sehr gut <span style="color:#8b8b8b;font-weight:400">— stets zu unserer vollsten Zufriedenheit</span></label>
                    <label style="${pill}"><input type="radio" name="azQuali" value="gut" checked> Gut <span style="color:#8b8b8b;font-weight:400">— stets zu unserer vollen Zufriedenheit</span></label>
                    <label style="${pill}"><input type="radio" name="azQuali" value="durchschnitt"> Durchschnitt <span style="color:#8b8b8b;font-weight:400">— zu unserer Zufriedenheit</span></label>
                </div>

                <div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;letter-spacing:0.4px;margin-bottom:6px">Verrichtete Arbeit</div>
                <div style="display:flex;gap:8px;margin-bottom:16px">
                    <label style="${pill};flex:1;justify-content:center"><input type="checkbox" id="azKueche"> Küche</label>
                    <label style="${pill};flex:1;justify-content:center"><input type="checkbox" id="azKasse" checked> Kasse</label>
                    <label style="${pill};flex:1;justify-content:center"><input type="checkbox" id="azDrive" checked> Drive</label>
                </div>

                <label style="${pill};margin-bottom:16px"><input type="checkbox" id="azWunsch" checked> Austritt auf eigenen Wunsch <span style="color:#8b8b8b;font-weight:400">— «verlässt unser Unternehmen auf eigenen Wunsch»</span></label>

                <div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;letter-spacing:0.4px;margin-bottom:6px">Zeugnis-Datum</div>
                <input type="date" id="azDatum" style="width:100%;box-sizing:border-box;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:10px 14px;font-size:13.5px;color:#3f3f3f;margin-bottom:16px">

                <div id="azAlert"></div>
                <div style="display:flex;gap:10px;justify-content:flex-end">
                    <button onclick="document.getElementById('azModal').style.display='none'"
                            style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
                    <button id="azGoBtn" onclick="azGenerate()"
                            style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700">📄 PDF erstellen</button>
                </div>
            </div>`;
        document.body.appendChild(ov);
    }
    // Datum default heute; Untertitel = MA-Name falls greifbar
    const d = new Date();
    document.getElementById('azDatum').value = isoLocalDate(d);
    // MA-Name: aus Verträge-Seite ODER Mitarbeiter-Maske (beide Kontexte).
    let emp = null;
    if (typeof selectedVtEmployee !== 'undefined' && selectedVtEmployee?.id === employeeId) emp = selectedVtEmployee;
    else if (typeof selectedEmployee !== 'undefined' && selectedEmployee?.id === employeeId) emp = selectedEmployee;
    else if (window._empDetailCache?.id === employeeId) emp = window._empDetailCache;
    document.getElementById('azSub').textContent = emp
        ? `${emp.firstName} ${emp.lastName} · Personalnr. ${emp.employeeNumber || '–'}` : '';
    document.getElementById('azAlert').innerHTML = '';
    ov.style.display = 'flex';
}

async function azGenerate() {
    const alertEl = document.getElementById('azAlert');
    const btn = document.getElementById('azGoBtn');
    const bereiche = [];
    if (document.getElementById('azKueche').checked) bereiche.push('kueche');
    if (document.getElementById('azKasse').checked)  bereiche.push('kasse');
    if (document.getElementById('azDrive').checked)  bereiche.push('drive');
    if (bereiche.length === 0) {
        alertEl.innerHTML = '<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">Bitte mindestens einen Bereich wählen (Küche, Kasse, Drive).</div>';
        return;
    }
    const quali = document.querySelector('input[name="azQuali"]:checked')?.value || 'gut';
    const datum = document.getElementById('azDatum').value || null;
    btn.disabled = true; btn.textContent = '⏳ erstelle…';
    try {
        const res = await fetch(`/api/arbeitszeugnis/${_azEmployeeId}/pdf`, {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}`, 'Content-Type': 'application/json' },
            body: JSON.stringify({ qualitaet: quali, bereiche, datum,
                aufEigenenWunsch: document.getElementById('azWunsch')?.checked ?? true })
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            alertEl.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">${err.message || err.error || ('HTTP ' + res.status)}</div>`;
            return;
        }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename="?([^"]+)"?/);
        document.getElementById('azModal').style.display = 'none';
        await previewFileModal(blob, m ? m[1] : 'Arbeitszeugnis.pdf');
    } catch (e) {
        alertEl.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">Netzwerkfehler: ${e.message}</div>`;
    } finally {
        btn.disabled = false; btn.textContent = '📄 PDF erstellen';
    }
}
