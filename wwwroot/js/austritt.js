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
    // Uniform-Depot-Radios zurücksetzen
    document.querySelectorAll('input[name="terminateUniformReturn"]').forEach(r => { r.checked = false; });
    const uniBlock = document.getElementById('terminateUniformBlock');
    if (uniBlock) uniBlock.style.display = 'none';
    terminateCtx.hasUniformDepot = false;
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
        // Uniformen-Depot: Block zeigen wenn EINBEHALTEN mit Saldo
        const uni = s.uniformDepot;
        const uniBlock = document.getElementById('terminateUniformBlock');
        const hasDepot = !!(uni && uni.status === 'EINBEHALTEN' && Number(uni.balance) > 0);
        terminateCtx.hasUniformDepot = hasDepot;
        if (uniBlock) uniBlock.style.display = hasDepot ? '' : 'none';
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

    // Uniformen-Depot Hinweis in der Vorschau
    let depotBlock = '';
    if (s.uniformDepot && s.uniformDepot.status === 'EINBEHALTEN' && Number(s.uniformDepot.balance) > 0) {
        depotBlock = `
        <div style="margin-top:10px;padding:8px 10px;background:#fff;border:1px solid #e7e1d8;border-radius:8px;font-size:12px;color:#3f3f3f">
            <strong>Uniformen-Depot:</strong> CHF ${Number(s.uniformDepot.balance).toFixed(2)} einbehalten
            — Entscheidung unten treffen (Rückerstattung oder Verfall).
        </div>`;
    }

    return noSaldo + saldoInfo + stundenBlock + ferienBlock + restBlock + depotBlock;
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

    let uniformZurueckgegeben = undefined;
    if (terminateCtx.hasUniformDepot) {
        const sel = document.querySelector('input[name="terminateUniformReturn"]:checked');
        if (!sel) {
            alert.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px">Bitte angeben, ob die Uniform zurückgegeben wurde (Depot CHF 50).</div>`;
            return;
        }
        uniformZurueckgegeben = sel.value === '1';
    }

    try {
        const body = { exitDate };
        if (uniformZurueckgegeben !== undefined) body.uniformZurueckgegeben = uniformZurueckgegeben;
        const res = await fetch(`/api/employments/${employmentId}/terminate`, {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}`, 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
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
        // Unterzeichner-Umschalter (Walter 23.08.2026, global aus contracts-edit.js).
        if (typeof vtInjectSignerSelector === 'function') vtInjectSignerSelector(contractId);
    } catch (err) { alert('Fehler: ' + err.message); }
    finally { if (btn) { btn.textContent = '📄 PDF'; btn.disabled = false; } }
}


// ═══════════════ ARBEITSZEUGNIS (Walter-Vorgabe 14.07.2026) ═══════════════
// Drei Qualitätsstufen (Durchschnitt/Gut/Sehr gut) + Mehrfachauswahl der
// verrichteten Arbeit (Küche/Kasse/Drive). PDF im Vorschaufenster.
let _azEmployeeId = null;

// 13er-Aufgaben-Katalog aus der Word-Vorlage «216 Oftringen» (Walter 15.07.2026).
const AZ_AUFGABEN = [
    'Produzieren und Garnieren unserer Qualitätsprodukte',
    'Bedienen unserer Gäste an der Kasse',
    'Bedienen unserer Gäste an der Kasse und am Drive',
    'Gästebetreuung',
    'Bearbeitung der Lieferung',
    'Diverse Reinigungsarbeiten im ganzen Restaurant',
    'Verfolgen des Training-Systems der Mitarbeiter am Arbeitsplatz',
    'Verantwortungen während einer Schichtführung: fachliche und personelle Führung des Teams',
    'Verwaltung und Unterhalt des Restaurant Equipments',
    'Verwaltung des Bestellwesens',
    'Verantwortung für Brandverhütung; Einhaltung der Arbeitssicherheit und des Gesundheitsschutzes, insbesondere für die Einhaltung der jeweiligen Reglemente (kant. Gewerbegesetz und Alkoholgesetz)',
    'Erstellung der Tagesabrechnungen resp. Schlussabrechnungen sowie Kassenabrechnungen',
    'Qualitätsprüfungen bei Lebensmitteln (Fleischqualität, Ölkontrolle und Einhaltung der lebensmittelrechtlichen Temperaturvorschriften) und Verantwortung der Lebensmittelkontrollen'
];

// Aufgaben-Gruppen (Walter-Vorgabe 15.07.2026, kompaktes Modal):
// basis   = immer sichtbar (Crew-Aufgaben, Index 0-5)
// trainer = ab Crew-Trainer/in (Training-System, Index 6)
// schicht = NUR Schichtkoordinator/in (Fuehrung/Equipment/Bestellwesen/
//           Brandverhuetung/Abrechnungen/Qualitaetspruefungen, Index 7-12)
function _azGroupOf(i) { return i <= 5 ? 'basis' : i === 6 ? 'trainer' : 'schicht'; }

// Sichtbarkeit der Aufgaben-Gruppen nach gewaehlter Funktion.
function azUpdateTaskVisibility() {
    const fn = document.getElementById('azFunktion')?.value || '';
    const istSchicht  = fn.startsWith('Schichtkoordinator');
    const istTrainer  = fn.startsWith('Crew-Trainer') || istSchicht;
    document.querySelectorAll('.azTaskRow').forEach(row => {
        const g = row.dataset.group;
        const zeigen = g === 'basis' || (g === 'trainer' && istTrainer) || (g === 'schicht' && istSchicht);
        row.style.display = zeigen ? '' : 'none';
        if (!zeigen) { const c = row.querySelector('input'); if (c) c.checked = false; }
    });
}

// Funktions-Vorschlag aus JobGroup + Pensum des juengsten Vertrags.
function _azFunktionVorschlag(emp, female) {
    const es = (emp?.employments || []).slice()
        .sort((a, b) => (b.contractStartDate || '').localeCompare(a.contractStartDate || ''));
    const c = es[0] || {};
    const jg = String(c.jobGroupCode || c.jobTitle || '').toUpperCase();
    if (jg.includes('SHIFT')) return female ? 'Schichtkoordinatorin' : 'Schichtkoordinator';
    if (jg.includes('HOST_CT') || jg.includes('TRAINER') || jg === 'CT')
        return female ? 'Crew-Trainerin' : 'Crew-Trainer';
    const vollzeit = (c.employmentModel === 'FIX' || c.employmentModel === 'FIX-M')
        && Number(c.employmentPercentage ?? 100) >= 100;
    return (vollzeit ? 'Vollzeit-' : 'Teilzeit-') + (female ? 'Crewmitarbeiterin' : 'Crewmitarbeiter');
}

// Braucht das ARBEITSzeugnis ein fiktives Austrittsdatum? (Walter 15.07.2026:
// letzter Vertrag offen + kein Austritt erfasst). Zwischenzeugnis/Bestaetigung
// brauchen kein Bis-Datum. Bei unbekanntem MA-Objekt: Feld sicherheitshalber zeigen.
// Walter-Vorgabe 12.08.2026: das Austrittsdatum-Feld wird beim
// ARBEITSzeugnis IMMER gezeigt — vorbefüllt mit dem erfassten MA-Austritt
// (sonst Vertragsende, sonst Monatsende). Im Zeugnis gilt IMMER das hier
// eingetragene Datum (Server-Priorität: dto.Austritt zuerst).
function _azNeedsAustritt(emp) {
    return !(_azZwischen || _azBest);
}

// Vorschlag fürs Austrittsdatum: MA-Austritt → Ende des letzten Vertrags →
// Ende des laufenden Monats.
function _azAustrittVorschlag(emp) {
    const exit = (emp?.exitDate || '').slice(0, 10);
    if (exit) return exit;
    const es = (emp?.employments || []).slice()
        .sort((a, b) => (b.contractStartDate || '').localeCompare(a.contractStartDate || ''));
    const ende = (es[0]?.contractEndDate || '').slice(0, 10);
    if (ende) return ende;
    const now = new Date();
    return isoLocalDate(new Date(now.getFullYear(), now.getMonth() + 1, 0));
}

function _azEmpObj(employeeId) {
    if (typeof selectedVtEmployee !== 'undefined' && selectedVtEmployee?.id === employeeId) return selectedVtEmployee;
    if (typeof selectedEmployee !== 'undefined' && selectedEmployee?.id === employeeId) return selectedEmployee;
    return null;
}

let _azZwischen = false;
let _azBest = false;   // Arbeitsbestätigung (Vorlage «244 Sursee»)

function openZeugnisModal(employeeId, zwischen = false, best = false) {
    _azEmployeeId = employeeId;
    _azZwischen = !!zwischen;
    _azBest = !!best;
    const emp = _azEmpObj(employeeId);
    const female = String(emp?.gender || '').toLowerCase().startsWith('f')
        || String(emp?.gender || '').toLowerCase() === 'w'
        || emp?.salutation === 'Frau';
    const funktionen = female
        ? ['Teilzeit-Crewmitarbeiterin', 'Vollzeit-Crewmitarbeiterin', 'Crew-Trainerin', 'Schichtkoordinatorin']
        : ['Teilzeit-Crewmitarbeiter', 'Vollzeit-Crewmitarbeiter', 'Crew-Trainer', 'Schichtkoordinator'];
    const vorschlag = emp ? _azFunktionVorschlag(emp, female) : funktionen[0];

    const pill = 'display:flex;align-items:center;gap:8px;background:transparent;border:1px solid rgba(60,55,48,0.22);border-radius:12px;padding:7px 11px;cursor:pointer;font-size:12.5px;font-weight:600;color:#3f3f3f';
    const pillS = 'display:flex;align-items:flex-start;gap:8px;background:transparent;border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:7px 10px;cursor:pointer;font-size:12px;color:#3f3f3f';
    const label = 'font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;letter-spacing:0.4px;margin-bottom:6px';
    const inp = 'width:100%;box-sizing:border-box;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 12px;font-size:13px;color:#3f3f3f';

    let ov = document.getElementById('azModal');
    if (ov) ov.remove();
    ov = document.createElement('div');
    ov.id = 'azModal';
    ov.style.cssText = 'position:fixed;inset:0;z-index:4000;background:rgba(60,55,48,0.4);display:flex;align-items:center;justify-content:center;padding:20px';
    ov.onclick = e => { if (e.target === ov) ov.remove(); };
    // Breites Zwei-Spalten-Layout im OneCrew-Look (Walter 12.08.2026):
    // links Beurteilung/Daten/Zustellung, rechts Bereich + Aufgaben.
    // Bei der Arbeitsbestätigung (nur 1 Satz) bleibt es einspaltig.
    ov.innerHTML = `
        <div class="iv-modal-box" style="border:1px solid rgba(255,255,255,0.62);border-radius:18px;max-width:${_azBest ? 640 : 1080}px;width:100%;max-height:92vh;overflow:auto;padding:22px 26px;box-shadow:0 24px 60px rgba(60,55,48,0.22)">
            <div style="display:flex;align-items:flex-start;justify-content:space-between;gap:12px;margin-bottom:2px">
                <div style="font-size:16px;font-weight:800;color:#3f3f3f">${_azBest ? 'Arbeitsbestätigung' : _azZwischen ? 'Zwischenzeugnis' : 'Arbeitszeugnis'} erstellen</div>
                <button onclick="document.getElementById('azModal').remove()"
                        class="kd-btn-glass" style="font-size:13px;padding:7px 16px;border-radius:12px">← Zurück</button>
            </div>
            <div id="azSub" style="font-size:12.5px;color:#8b8b8b;margin-bottom:14px">${emp ? `${emp.firstName} ${emp.lastName} · Personalnr. ${emp.employeeNumber || '–'}` : ''}</div>

            <div style="display:grid;grid-template-columns:${_azBest ? '1fr' : '1fr 1.1fr'};gap:0 26px;align-items:start">
            <div>
            <div style="${label};${_azBest ? 'display:none' : ''}">Qualität</div>
            <div style="display:${_azBest ? 'none' : 'grid'};grid-template-columns:1fr 1fr;gap:8px;margin-bottom:16px">
                <label style="${pill}"><input type="radio" name="azQuali" value="sehr_gut"> Sehr gut</label>
                <label style="${pill}"><input type="radio" name="azQuali" value="gut" checked> Gut</label>
                <label style="${pill}"><input type="radio" name="azQuali" value="durchschnitt"> Durchschnitt</label>
                <label style="${pill}"><input type="radio" name="azQuali" value="genuegend"> Genügend</label>
            </div>

            <div style="display:flex;gap:12px;margin-bottom:16px">
                <div style="flex:1.4">
                    <div style="${label}">Funktion</div>
                    <select id="azFunktion" style="${inp}" onchange="azUpdateTaskVisibility()">
                        ${funktionen.map(fn => `<option value="${fn}" ${fn === vorschlag ? 'selected' : ''}>${fn}</option>`).join('')}
                    </select>
                </div>
                <div style="flex:1">
                    <div style="${label}">Zeugnis-Datum</div>
                    <input type="date" id="azDatum" style="${inp}">
                </div>
            </div>

            ${_azNeedsAustritt(emp) ? `
            <div style="margin-bottom:16px">
                <div style="${label}">Austrittsdatum (für «war vom … bis …»)</div>
                <input type="date" id="azAustritt" style="${inp}">
                <div style="font-size:11.5px;color:#8b8b8b;margin-top:4px">Vorschlag = erfasstes Austrittsdatum des MA (sonst Vertragsende / Monatsende). Im Zeugnis gilt das HIER eingetragene Datum.</div>
            </div>` : ''}

            <div style="margin-bottom:16px">
                <div style="${label}">Zustellung &amp; Unterzeichner</div>
                <div class="zst-wrap">
                    <label class="zst-pill"><input type="radio" name="azZustell" value="V" checked>📮 Versand an Mitarbeiter</label>
                    <label class="zst-pill"><input type="radio" name="azZustell" value="A">🏪 Abgabe durch Restaurant</label>
                </div>
                <div style="font-size:11px;color:#8b8b8b;margin-top:4px">Versand: unterzeichnet der angemeldete Benutzer · Abgabe: unterzeichnet der Allgemein-Unterzeichner der Filiale.</div>
            </div>

            <label style="${pill};margin-bottom:16px;${(_azZwischen || _azBest) ? 'display:none' : ''}"><input type="checkbox" id="azWunsch" checked> Austritt auf eigenen Wunsch <span style="color:#8b8b8b;font-weight:400">— «verlässt unser Unternehmen auf eigenen Wunsch»</span></label>
            </div>

            <div style="${_azBest ? 'display:none' : ''}">
            <div style="${label}">Bereich (Schnellwahl — kreuzt die passenden Aufgaben an)</div>
            <div style="display:${_azBest ? 'none' : 'flex'};gap:8px;margin-bottom:12px">
                <label style="${pill};flex:1;justify-content:center"><input type="checkbox" id="azKueche" onchange="azQuickTasks()"> Küche</label>
                <label style="${pill};flex:1;justify-content:center"><input type="checkbox" id="azKasse" checked onchange="azQuickTasks()"> Kasse</label>
                <label style="${pill};flex:1;justify-content:center"><input type="checkbox" id="azDrive" checked onchange="azQuickTasks()"> Drive</label>
            </div>

            <div style="${label}">Aufgaben (Mehrfachauswahl — Umfang folgt der Funktion)</div>
            <div style="display:${_azBest ? 'none' : 'flex'};flex-direction:column;gap:4px;margin-bottom:14px">
                ${AZ_AUFGABEN.map((a, i) => `<label class="azTaskRow" data-group="${_azGroupOf(i)}" style="${pillS};padding:5px 9px;font-size:11.5px;line-height:1.3"><input type="checkbox" class="azAufgabe" data-i="${i}" value="${a.replace(/"/g, '&quot;')}"> <span>${a}</span></label>`).join('')}
            </div>
            </div>
            </div>

            <div id="azAlert"></div>
            <div style="display:flex;gap:10px;justify-content:flex-end;margin-top:4px">
                <button onclick="document.getElementById('azModal').remove()"
                        style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
                <button id="azGoBtn" onclick="azGenerate()"
                        style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700;box-shadow:0 4px 14px rgba(60,55,48,0.22)">📄 PDF erstellen</button>
            </div>
        </div>`;
    document.body.appendChild(ov);
    document.getElementById('azDatum').value = isoLocalDate(new Date());
    // Austrittsdatum: Vorschlag = MA-Austritt → Vertragsende → Monatsende
    // (Walter 12.08.2026); im Zeugnis gilt das eingetragene Datum.
    const azA = document.getElementById('azAustritt');
    if (azA) azA.value = _azAustrittVorschlag(emp);
    azQuickTasks();
    azUpdateTaskVisibility();
}

// Bereichs-Schnellwahl → passende Katalog-Aufgaben ankreuzen (Grundset).
function azQuickTasks() {
    const k = document.getElementById('azKueche')?.checked;
    const ka = document.getElementById('azKasse')?.checked;
    const d = document.getElementById('azDrive')?.checked;
    const want = new Set();
    if (ka && d) want.add('Bedienen unserer Gäste an der Kasse und am Drive');
    else if (ka) want.add('Bedienen unserer Gäste an der Kasse');
    if (k) want.add('Produzieren und Garnieren unserer Qualitätsprodukte');
    want.add('Gästebetreuung');
    want.add('Diverse Reinigungsarbeiten im ganzen Restaurant');
    document.querySelectorAll('.azAufgabe').forEach((c, i) => {
        // Nur die Grundset-Aufgaben umschalten — manuell gewählte Zusatz-
        // Aufgaben (Trainer/Schichtleiter, Index 4+6..12) nicht anfassen.
        const isBasis = i <= 5 && AZ_AUFGABEN[i] !== 'Bearbeitung der Lieferung';
        if (isBasis) c.checked = want.has(c.value);
    });
}

async function azGenerate() {
    const alertEl = document.getElementById('azAlert');
    const btn = document.getElementById('azGoBtn');
    const bereiche = [];
    if (document.getElementById('azKueche').checked) bereiche.push('kueche');
    if (document.getElementById('azKasse').checked)  bereiche.push('kasse');
    if (document.getElementById('azDrive').checked)  bereiche.push('drive');
    const aufgaben = [...document.querySelectorAll('.azAufgabe:checked')].map(c => c.value);
    if (bereiche.length === 0 && !_azBest) {
        alertEl.innerHTML = '<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">Bitte mindestens einen Bereich wählen (Küche, Kasse, Drive).</div>';
        return;
    }
    if (aufgaben.length === 0 && !_azBest) {
        alertEl.innerHTML = '<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">Bitte mindestens eine Aufgabe ankreuzen.</div>';
        return;
    }
    const quali = document.querySelector('input[name="azQuali"]:checked')?.value || 'gut';
    const datum = document.getElementById('azDatum').value || null;
    btn.disabled = true; btn.textContent = '⏳ erstelle…';
    try {
        const res = await fetch(`/api/arbeitszeugnis/${_azEmployeeId}/pdf`, {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}`, 'Content-Type': 'application/json' },
            body: JSON.stringify({
                qualitaet: quali, bereiche, datum, aufgaben,
                funktion: document.getElementById('azFunktion')?.value || null,
                aufEigenenWunsch: document.getElementById('azWunsch')?.checked ?? true,
                zwischen: _azZwischen,
                bestaetigung: _azBest,
                austritt: document.getElementById('azAustritt')?.value || null,
                // Abgabe durch Restaurant = Allgemein-Unterzeichner (Walter 12.08.2026).
                abgabe: document.querySelector('input[name="azZustell"]:checked')?.value === 'A'
            })
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            alertEl.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">${err.message || err.error || ('HTTP ' + res.status)}</div>`;
            return;
        }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename="?([^"]+)"?/);
        document.getElementById('azModal').remove();
        await previewFileModal(blob, m ? m[1] : (_azBest ? 'Arbeitsbestaetigung.pdf' : _azZwischen ? 'Zwischenzeugnis.pdf' : 'Arbeitszeugnis.pdf'));
    } catch (e) {
        alertEl.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">Netzwerkfehler: ${e.message}</div>`;
    } finally {
        btn.disabled = false; btn.textContent = '📄 PDF erstellen';
    }
}

