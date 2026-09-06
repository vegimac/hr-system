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
    const istSchicht  = fn.startsWith('Schichtkoordinator') || fn.startsWith('Geschäftsführer') || fn.startsWith('Assistant');
    const istTrainer  = fn.startsWith('Crew-Trainer') || istSchicht;
    // Walter 06.09.2026: Funktions-Aufgaben (Training / Schichtführung) werden
    // beim Wählen der Funktion gleich ANGEKREUZT — bisher nur eingeblendet,
    // und beim Schichtkoordinator blieben alle Führungs-Aufgaben leer.
    // Einzelne Kreuze können danach immer noch von Hand entfernt werden.
    document.querySelectorAll('.azTaskRow').forEach(row => {
        const g = row.dataset.group;
        const zeigen = g === 'basis' || (g === 'trainer' && istTrainer) || (g === 'schicht' && istSchicht);
        const war = row.style.display !== 'none';
        row.style.display = zeigen ? '' : 'none';
        const c = row.querySelector('input');
        if (!c) return;
        if (!zeigen) c.checked = false;
        else if (g !== 'basis' && (!war || !c.checked)) c.checked = true;
        // Schichtkoordinator / Management (Walter 06.09.2026): ALLE Aufgaben
        // ankreuzen — auch die Crew-Grundaufgaben unabhängig von der Bereichs-Schnellwahl.
        else if (istSchicht) c.checked = true;
    });
    azRefreshButtons();
}

// ── Druck-Berechtigung (Walter 06.09.2026) ──────────────────────────────
// Leiter Crew(1) → Crew-Trainer(2) → Schichtkoordinator(3) → Geschäftsführer(4).
// Stufe des Benutzers kommt aus /me (zeugnisDruckBis) bzw. /api/arbeitszeugnis/berechtigung.
let _azBerechtigung = null;    // { code, stufe, label }
let _azEntwurf = null;         // Entwurf-Modus (HR öffnet einen Entwurf)
function _azStufe(code) { return ({ crew: 1, ct: 2, schicht: 3, alle: 4 })[String(code || '').toLowerCase()] || 0; }
function _azFunktionStufe(fn) {
    const f = String(fn || '');
    if (f.startsWith('Geschäftsführer') || f.startsWith('Assistant')) return 4;
    if (f.startsWith('Schichtkoordinator')) return 3;
    if (f.startsWith('Crew-Trainer')) return 2;
    return 1;
}
function _azStufeLabel(n) { return ['keine', 'Crew', 'Crew-Trainer/in', 'Schichtkoordinator/in', 'alle'][n] || 'keine'; }
async function _azLadeBerechtigung() {
    if (_azBerechtigung) return _azBerechtigung;
    try {
        const r = await fetch('/api/arbeitszeugnis/berechtigung', { headers: { 'Authorization': `Bearer ${authToken}` } });
        if (r.ok) { _azBerechtigung = await r.json(); return _azBerechtigung; }
    } catch (_) {}
    const code = (typeof currentUser !== 'undefined' && currentUser?.zeugnisDruckBis) || (currentUser?.role === 'admin' ? 'alle' : 'keine');
    _azBerechtigung = { code, stufe: _azStufe(code), label: _azStufeLabel(_azStufe(code)) };
    return _azBerechtigung;
}
function azDarfDrucken() {
    const fn = document.getElementById('azFunktion')?.value || '';
    return (_azBerechtigung?.stufe ?? 0) >= _azFunktionStufe(fn);
}

// Knöpfe/Hinweis je nach Berechtigung und gewählter Funktion umschalten.
function azRefreshButtons() {
    const go = document.getElementById('azGoBtn');
    const hr = document.getElementById('azHrBtn');
    const zw = document.getElementById('azZurueckBtn');
    const hint = document.getElementById('azDruckHinweis');
    const bemBox = document.getElementById('azBemerkungBox');
    if (!go || !hr) return;
    const fn = document.getElementById('azFunktion')?.value || '';
    const darf = azDarfDrucken();
    go.style.display = darf ? '' : 'none';
    hr.style.display = darf ? 'none' : '';
    if (bemBox) bemBox.style.display = darf ? 'none' : '';
    if (zw) zw.style.display = (_azEntwurf && darf) ? '' : 'none';
    if (hint) {
        hint.style.display = darf ? 'none' : '';
        hint.textContent = `Zeugnisse für «${fn}» erstellt HR — du kannst die Maske ausfüllen und als Entwurf an HR senden (deine Stufe: ${_azBerechtigung?.label || 'keine'}).`;
    }
}

// Maske aus Entwurf-Daten befüllen (HR öffnet Entwurf / Ersteller lädt seinen Entwurf).
function _azFuelleAusDaten(d) {
    if (!d) return;
    const q = document.querySelector(`input[name="azQuali"][value="${d.qualitaet || 'gut'}"]`); if (q) q.checked = true;
    const sel = document.getElementById('azFunktion');
    if (sel && d.funktion) {
        if (![...sel.options].some(o => o.value === d.funktion)) sel.add(new Option(d.funktion, d.funktion));
        sel.value = d.funktion;
    }
    if (d.datum) document.getElementById('azDatum').value = String(d.datum).slice(0, 10);
    const azA = document.getElementById('azAustritt'); if (azA && d.austritt) azA.value = String(d.austritt).slice(0, 10);
    const w = document.getElementById('azWunsch'); if (w) w.checked = d.aufEigenenWunsch !== false;
    const zu = document.querySelector(`input[name="azZustell"][value="${d.abgabe ? 'A' : 'V'}"]`); if (zu) zu.checked = true;
    const b = new Set((d.bereiche || []).map(x => String(x).toLowerCase()));
    const ku = document.getElementById('azKueche'); if (ku) ku.checked = b.has('kueche');
    const ka = document.getElementById('azKasse');  if (ka) ka.checked = b.has('kasse');
    const dr = document.getElementById('azDrive');  if (dr) dr.checked = b.has('drive');
    // Sichtbarkeit nach Funktion, dann die gespeicherten Aufgaben 1:1 setzen.
    azUpdateTaskVisibility();
    if (Array.isArray(d.aufgaben)) {
        const want = new Set(d.aufgaben);
        document.querySelectorAll('.azAufgabe').forEach(c => { c.checked = want.has(c.value); });
    }
    azRefreshButtons();
}

// Entwurf an HR senden.
async function azSendeEntwurf() {
    const alertEl = document.getElementById('azAlert');
    const btn = document.getElementById('azHrBtn');
    const body = _azSammleDto();
    if (!body) return;
    body.bemerkung = document.getElementById('azBemerkung')?.value?.trim() || null;
    btn.disabled = true; btn.textContent = '⏳ sende…';
    try {
        const res = await fetch(`/api/arbeitszeugnis/${_azEmployeeId}/entwurf`, {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}`, 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            alertEl.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">${err.message || err.error || ('HTTP ' + res.status)}</div>`;
            return;
        }
        document.getElementById('azModal').remove();
        if (typeof showToast === 'function') showToast('Zeugnis-Entwurf an HR gesendet — HR erstellt und unterschreibt das Zeugnis.');
        else alert('Zeugnis-Entwurf an HR gesendet.');
    } catch (e) {
        alertEl.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">Netzwerkfehler: ${e.message}</div>`;
    } finally {
        btn.disabled = false; btn.textContent = '✉ An HR senden';
    }
}

// HR: Entwurf mit Begründung zurückweisen.
async function azZurueckweisen() {
    if (!_azEntwurf) return;
    const grund = prompt('Entwurf zurückweisen — Grund für den Ersteller (optional):', '');
    if (grund === null) return;
    try {
        const res = await fetch(`/api/arbeitszeugnis/entwurf/${_azEntwurf.id}/zurueckweisen`, {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}`, 'Content-Type': 'application/json' },
            body: JSON.stringify({ grund })
        });
        if (!res.ok) { alert('Zurückweisen fehlgeschlagen (HTTP ' + res.status + ').'); return; }
        document.getElementById('azModal').remove();
        if (typeof showToast === 'function') showToast('Entwurf zurückgewiesen — der Ersteller wurde informiert.');
        if (typeof pbLoadList === 'function') pbLoadList();
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

// Ersteller: eigenen offenen Entwurf zurückziehen.
async function azEntwurfZurueckziehen(id) {
    if (!(await liquidConfirm('Entwurf bei HR zurückziehen?'))) return;
    try {
        const res = await fetch(`/api/arbeitszeugnis/entwurf/${id}`, { method: 'DELETE', headers: { 'Authorization': `Bearer ${authToken}` } });
        if (!res.ok && res.status !== 204) { alert('Zurückziehen fehlgeschlagen.'); return; }
        const b = document.getElementById('azEntwurfBanner'); if (b) b.remove();
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

// Maskenwerte als DTO (gemeinsam für PDF und Entwurf).
function _azSammleDto() {
    const alertEl = document.getElementById('azAlert');
    const bereiche = [];
    if (document.getElementById('azKueche').checked) bereiche.push('kueche');
    if (document.getElementById('azKasse').checked)  bereiche.push('kasse');
    if (document.getElementById('azDrive').checked)  bereiche.push('drive');
    const aufgaben = [...document.querySelectorAll('.azAufgabe:checked')].map(c => c.value);
    if (bereiche.length === 0 && !_azBest) {
        alertEl.innerHTML = '<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">Bitte mindestens einen Bereich wählen (Küche, Kasse, Drive).</div>';
        return null;
    }
    if (aufgaben.length === 0 && !_azBest) {
        alertEl.innerHTML = '<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">Bitte mindestens eine Aufgabe ankreuzen.</div>';
        return null;
    }
    const quali = document.querySelector('input[name="azQuali"]:checked')?.value || 'gut';
    const datum = document.getElementById('azDatum').value || null;
    return {
        qualitaet: quali, bereiche, datum, aufgaben,
        funktion: document.getElementById('azFunktion')?.value || null,
        aufEigenenWunsch: document.getElementById('azWunsch')?.checked ?? true,
        zwischen: _azZwischen,
        bestaetigung: _azBest,
        austritt: document.getElementById('azAustritt')?.value || null,
        // Abgabe durch Restaurant = Allgemein-Unterzeichner (Walter 12.08.2026).
        abgabe: document.querySelector('input[name="azZustell"]:checked')?.value === 'A',
        entwurfId: _azEntwurf?.id || null
    };
}

// Funktions-Vorschlag aus JobGroup + Pensum des juengsten Vertrags.
// Walter 06.09.2026: der LETZTE Vertrag bestimmt die Funktion — aktiver
// Vertrag zuerst, sonst der jüngste; Funktion aus JobGroup-Code, sonst aus
// dem Funktionstext (Mirus-Aliase wie «Shift Coordinator», «Crew Trainer»).
function _azFunktionVorschlag(emp, female) {
    const es = (emp?.employments || []).slice()
        .sort((a, b) => (b.isActive ? 1 : 0) - (a.isActive ? 1 : 0)
                     || (b.contractStartDate || '').localeCompare(a.contractStartDate || ''));
    const c = es[0] || {};
    const jg = String(c.jobGroupCode || '').toUpperCase();
    const jt = String(c.jobTitle || '').toUpperCase();
    const hat = (...k) => k.some(x => jg.includes(x) || jt.includes(x));
    if (hat('REST_MANAGER', 'RESTAURANT MANAGER', 'GESCHÄFTSFÜHRER', 'GESCHAEFTSFUEHRER'))
        return female ? 'Geschäftsführerin' : 'Geschäftsführer';
    if (hat('ASST_', 'ASSISTANT')) return 'Assistant Manager';
    if (hat('SHIFT', 'SCHICHT')) return female ? 'Schichtkoordinatorin' : 'Schichtkoordinator';
    if (hat('HOST_CT', 'TRAINER') || jg === 'CT')
        return female ? 'Crew-Trainerin' : 'Crew-Trainer';
    const vollzeit = (c.employmentModel === 'FIX' || c.employmentModel === 'FIX-M')
        && Number(c.employmentPercentage ?? 100) >= 100;
    return (vollzeit ? 'Vollzeit-' : 'Teilzeit-') + (female ? 'Crewmitarbeiterin' : 'Crewmitarbeiter');
}

// Funktionstext des massgebenden Vertrags (für den Hinweis unter dem Dropdown).
function _azVertragFunktionText(emp) {
    const es = (emp?.employments || []).slice()
        .sort((a, b) => (b.isActive ? 1 : 0) - (a.isActive ? 1 : 0)
                     || (b.contractStartDate || '').localeCompare(a.contractStartDate || ''));
    const c = es[0] || {};
    return String(c.jobTitle || c.jobGroupCode || '').trim();
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

async function openZeugnisModal(employeeId, zwischen = false, best = false, entwurf = null) {
    _azEmployeeId = employeeId;
    _azZwischen = !!zwischen;
    _azBest = !!best;
    _azEntwurf = entwurf || null;
    await _azLadeBerechtigung();
    // MA-Objekt: aus der MA-Maske, sonst (Zeugnis-Seite mit Picker, HR-Postfach)
    // vom Server holen — sonst fehlen Vertrag/Funktion, Geschlecht und Austritt
    // und der Vorschlag fällt auf Teilzeit-Crew zurück (Walter 06.09.2026).
    let emp = _azEmpObj(employeeId);
    if (!emp) {
        try {
            const r = await fetch(`/api/employees/${employeeId}?_=${Date.now()}`, { headers: { 'Authorization': `Bearer ${authToken}` }, cache: 'no-store' });
            if (r.ok) emp = await r.json();
        } catch (_) {}
    }
    // Ohne MA-Objekt (Entwurf aus dem HR-Postfach): Geschlecht aus der Funktion des Entwurfs.
    const female = emp
        ? (String(emp?.gender || '').toLowerCase().startsWith('f')
            || String(emp?.gender || '').toLowerCase() === 'w'
            || emp?.salutation === 'Frau')
        : /in$/.test(String(entwurf?.daten?.funktion || ''));
    const funktionen = female
        ? ['Teilzeit-Crewmitarbeiterin', 'Vollzeit-Crewmitarbeiterin', 'Crew-Trainerin', 'Schichtkoordinatorin', 'Assistant Manager', 'Geschäftsführerin']
        : ['Teilzeit-Crewmitarbeiter', 'Vollzeit-Crewmitarbeiter', 'Crew-Trainer', 'Schichtkoordinator', 'Assistant Manager', 'Geschäftsführer'];
    const vorschlag = emp ? _azFunktionVorschlag(emp, female) : funktionen[0];
    const subName = emp ? `${emp.firstName} ${emp.lastName} · Personalnr. ${emp.employeeNumber || '–'}`
                  : entwurf ? `${entwurf.employeeName} · Personalnr. ${entwurf.employeeNumber || '–'}` : '';
    const entwurfKopf = entwurf ? `
            <div style="margin:0 0 14px;padding:10px 12px;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.28);border-radius:10px;font-size:12.5px;color:#3f3f3f">
                <b>Entwurf von ${entwurf.erstelltVonName || '–'}</b> · ${entwurf.erstelltAm ? formatDate(entwurf.erstelltAm) : ''}
                ${entwurf.bemerkung ? `<div style="margin-top:4px;white-space:pre-wrap">Bemerkung: ${entwurf.bemerkung}</div>` : ''}
                <div style="margin-top:4px;color:#8b8b8b">Prüfen, bei Bedarf anpassen, Unterschrift wählen und «PDF erstellen» — der Ersteller erhält das fertige Zeugnis als Mitteilung.</div>
            </div>` : '';

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
            <div id="azSub" style="font-size:12.5px;color:#8b8b8b;margin-bottom:14px">${subName}</div>
            ${entwurfKopf}
            <div id="azEntwurfBanner"></div>

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
                    ${emp ? `<div style="font-size:11px;color:#8b8b8b;margin-top:4px">Vorschlag aus dem letzten Vertrag${_azVertragFunktionText(emp) ? ` (${_azVertragFunktionText(emp)})` : ''} — bei Bedarf ändern.</div>` : ''}
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
            <div id="azDruckHinweis" style="display:none;font-size:12px;color:#a16207;background:#fdf1dc;border:1px solid #f3d9a4;border-radius:10px;padding:8px 12px;margin-bottom:10px"></div>
            <div id="azBemerkungBox" style="display:none;margin-bottom:10px">
                <div style="${label}">Bemerkung an HR (optional)</div>
                <textarea id="azBemerkung" rows="2" style="${inp};resize:vertical" placeholder="z.B. MA holt das Zeugnis am Freitag ab">${entwurf?.bemerkung ? String(entwurf.bemerkung).replace(/</g, '&lt;') : ''}</textarea>
            </div>
            <div style="display:flex;gap:10px;justify-content:flex-end;margin-top:4px">
                <button onclick="document.getElementById('azModal').remove()"
                        style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
                <button id="azZurueckBtn" onclick="azZurueckweisen()" style="display:none;background:rgba(255,255,255,0.55);color:#9f1239;border:1px solid rgba(159,18,57,0.35);border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Zurückweisen</button>
                <button id="azHrBtn" onclick="azSendeEntwurf()" style="display:none;background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700;box-shadow:0 4px 14px rgba(60,55,48,0.22)">✉ An HR senden</button>
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
    if (entwurf) {
        _azFuelleAusDaten(entwurf.daten);
    } else {
        _azZeigeOffenenEntwurf(employeeId);
    }
}

// Offener Entwurf dieses MA (eigener oder — für HR — von jemandem): Banner mit «laden».
async function _azZeigeOffenenEntwurf(employeeId) {
    try {
        const art = _azBest ? 'bestaetigung' : _azZwischen ? 'zwischen' : 'arbeitszeugnis';
        const r = await fetch(`/api/arbeitszeugnis/entwuerfe?employeeId=${employeeId}&status=offen`, { headers: { 'Authorization': `Bearer ${authToken}` } });
        if (!r.ok) return;
        const list = (await r.json()).filter(x => x.art === art);
        const box = document.getElementById('azEntwurfBanner');
        if (!list.length || !box) return;
        const e = list[0];
        window._azOffenerEntwurf = e;
        const eigener = typeof currentUser !== 'undefined' && currentUser?.id === e.erstelltVon;
        box.innerHTML = `<div style="margin:0 0 14px;padding:10px 12px;background:#fdf1dc;border:1px solid #f3d9a4;border-radius:10px;font-size:12.5px;color:#7c5a10;display:flex;gap:10px;align-items:center;flex-wrap:wrap">
            <span>⏳ Entwurf vom ${formatDate(e.erstelltAm)}${eigener ? '' : ' von ' + (e.erstelltVonName || '–')} liegt bei HR.</span>
            <button type="button" onclick="_azEntwurf=window._azOffenerEntwurf;_azFuelleAusDaten(window._azOffenerEntwurf.daten);document.getElementById('azEntwurfBanner').innerHTML=''" style="background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:5px 12px;cursor:pointer;font-size:12px;font-weight:700">Entwurf laden</button>
            ${eigener ? `<button type="button" onclick="azEntwurfZurueckziehen(${e.id})" style="background:none;border:none;color:#9f1239;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline">zurückziehen</button>` : ''}
        </div>`;
    } catch (_) {}
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
    const body = _azSammleDto();
    if (!body) return;
    btn.disabled = true; btn.textContent = '⏳ erstelle…';
    try {
        const res = await fetch(`/api/arbeitszeugnis/${_azEmployeeId}/pdf`, {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}`, 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
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
        if (_azEntwurf && typeof pbLoadList === 'function') { try { pbLoadList(); } catch (_) {} }
        _azEntwurf = null;
        await previewFileModal(blob, m ? m[1] : (_azBest ? 'Arbeitsbestaetigung.pdf' : _azZwischen ? 'Zwischenzeugnis.pdf' : 'Arbeitszeugnis.pdf'));
    } catch (e) {
        alertEl.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">Netzwerkfehler: ${e.message}</div>`;
    } finally {
        btn.disabled = false; btn.textContent = '📄 PDF erstellen';
    }
}

