// ══════════════════════════════════════════════════════════════════════
// austritt.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ═══════════════ AUSTRITT ERFASSEN ═══════════════
let terminateCtx = { employeeId: null, employmentId: null, startDate: null };

function openTerminateModal(employeeId, employmentId, startDate) {
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
        ? `<div style="background:#eff6ff;border:1px solid #bfdbfe;color:#1e40af;padding:6px 10px;border-radius:6px;font-size:10px;margin-bottom:10px">${_t('austritt.info.balanceFrom', { month: String(s.saldoQuelleMonth).padStart(2,'0'), year: s.saldoQuelleYear, status: s.saldoQuelleStatus ? _t('austritt.info.statusSuffix', { status: s.saldoQuelleStatus }) : '' })}</div>`
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
        if (!res.ok) {
            const err = await res.json().catch(() => ({ error: _t('austritt.err.unknown') }));
            alert.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px">${_t('austritt.err.failed', { msg: err.error })}</div>`;
            return;
        }
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
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        const cd   = res.headers.get('Content-Disposition') || '';
        const match = cd.match(/filename="?([^"]+)"?/);
        a.download = match ? match[1] : 'Vertrag.pdf';
        a.href = url; a.click(); URL.revokeObjectURL(url);
    } catch (err) { alert('Fehler: ' + err.message); }
    finally { if (btn) { btn.textContent = '📄 PDF'; btn.disabled = false; } }
}

