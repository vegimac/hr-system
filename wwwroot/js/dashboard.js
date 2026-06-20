// ══════════════════════════════════════════════════════════════════════
// dashboard.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════
// DASHBOARD-COCKPIT
// ──────────────────────────────────────────────────────────────────────
// Zeigt "Was wartet auf mich?"-Alarme aus /api/dashboard. Kategorien:
// Bewilligung läuft ab, Probezeit endet, befristete Verträge, Lohnperioden
// die auf Aktion warten, Geburtstage, Dienstjubiläen.
// Filtert auf die aktuell global gewählte Filiale (oben links).
// ══════════════════════════════════════════════
let _dashAlerts = [];
let _dashActiveCategoryFilter = null;  // null = alle Kategorien

// Reihenfolge entscheidet Sortierung der Filter-Buttons UND der Sektionen in
// der Alarm-Liste (Frontend nutzt Object.keys, Backend sortiert die Alerts
// passend dazu in DashboardService). Mindestlohn ist Walter-Priorität #1.
// i18n: `i18nKey` ersetzt zur Render-Zeit den Label-Text via i18n.t().
// Falls i18n.js noch nicht geladen ist (Race beim ersten Render), greift
// der `label`-Fallback (Deutsch) damit nichts leer bleibt.
const DASH_CATEGORY_META = {
    minimum_wage_violation: { i18nKey: 'dash.cat.minWageViolation', label: 'Mindestlohn-Verletzung', icon: '⚠️', color: '#b91c1c' },
    minimum_wage_ok:        { i18nKey: 'dash.cat.minWageOk',        label: 'Mindestlohn ok',         icon: '✅', color: '#15803d' },
    permit_expiring:        { i18nKey: 'dash.cat.permitExpiring',   label: 'Bewilligungen',          icon: '🪪', color: '#b91c1c' },
    probation_end:          { i18nKey: 'dash.cat.probationEnding',  label: 'Probezeit',              icon: '📋', color: '#92400e' },
    contract_end:           { i18nKey: 'dash.cat.contractEnding',   label: 'Vertragsende',           icon: '📅', color: '#92400e' },
    exit_pending_active:    { i18nKey: 'dash.cat.exitPendingActive',label: 'Austritt offen',         icon: '🚪', color: '#b91c1c' },
    qst_pflicht_offen:      { i18nKey: 'dash.cat.qstPflichtOffen',  label: 'QST-Pflicht offen',      icon: '📋', color: '#b91c1c' },
    spouse_doku_fehlt:      { i18nKey: 'dash.cat.spouseDokuFehlt',  label: 'Ausweis Ehepartner',     icon: '🪪', color: '#b91c1c' },
    employee_doku_fehlt:    { i18nKey: 'dash.cat.employeeDokuFehlt',label: 'Ausweis Mitarbeiter',    icon: '🪪', color: '#b91c1c' },
    schwangerschaft:        { i18nKey: 'dash.cat.pregnancy',        label: 'Mutterschaft',           icon: '🤰', color: '#be185d' },
    night_work_exam_fehlt:  { i18nKey: 'dash.cat.nightWorkExam',    label: 'Nachtarbeit-Untersuchung', icon: '🌙', color: '#92400e' },
    lohn_provisorisch:      { i18nKey: 'dash.cat.payrollOpen',      label: 'Lohnlauf',               icon: '💰', color: '#0369a1' },
    birthday:               { i18nKey: 'dash.cat.birthday',         label: 'Geburtstage',            icon: '🎂', color: '#9333ea' },
    anniversary:            { i18nKey: 'dash.cat.anniversary',      label: 'Dienstjubiläen',         icon: '🎉', color: '#15803d' }
};

const DASH_SEVERITY_META = {
    critical: { i18nKey: 'dash.severity.critical', label: 'Kritisch', bg: '#fee2e2', border: '#fca5a5', text: '#991b1b' },
    warning:  { i18nKey: 'dash.severity.warning',  label: 'Achtung',  bg: '#fef3c7', border: '#fbbf24', text: '#92400e' },
    info:     { i18nKey: 'dash.severity.info',     label: 'Info',     bg: '#dbeafe', border: '#93c5fd', text: '#1e40af' }
};

// Helfer: holt das übersetzte Label für eine Meta-Zeile. Fallback auf
// label (DE) wenn i18n.js noch nicht initialisiert ist.
function dashMetaLabel(meta) {
    if (!meta) return '';
    if (meta.i18nKey && window.i18n) return window.i18n.t(meta.i18nKey);
    return meta.label || '';
}

async function loadDashboard() {
    const container = document.getElementById('dashAlertsContainer');
    const sevRow    = document.getElementById('dashSeverityRow');
    const subHeader = document.getElementById('dashSubHeader');
    if (!container) return;
    const isEn = window.i18n && i18n.getLang() === 'en';
    container.innerHTML = `<div style="padding:30px;text-align:center;color:#94a3b8">${isEn ? 'Loading…' : 'Lade…'}</div>`;

    const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                  ? `?companyProfileId=${fixedCompanyProfileId}` : '';

    try {
        const r = await fetch('/api/dashboard' + cid, { headers: ah() });
        if (!r.ok) {
            const errLbl = isEn ? 'Error loading' : 'Fehler beim Laden';
            container.innerHTML = `<div style="padding:30px;text-align:center;color:#dc2626">${errLbl}: ${r.status}</div>`;
            return;
        }
        const data = await r.json();
        _dashAlerts = data.alerts || [];

        // Sub-Header mit Filial-Hinweis (Sprache folgt aktueller UI-Wahl)
        if (subHeader) {
            const branch = (allBranches || []).find(b => b.id === Number(fixedCompanyProfileId));
            const remPlural = isEn
                ? (_dashAlerts.length === 1 ? 'reminder' : 'reminders')
                : (_dashAlerts.length === 1 ? 'Erinnerung' : 'Erinnerungen');
            const allBranchesLbl = isEn ? 'All branches' : 'Alle Filialen';
            const branchLbl = isEn ? 'Branch' : 'Filiale';
            subHeader.textContent = branch
                ? `${branchLbl}: ${branch.restaurantCode || '?'} — ${branch.branchName || branch.companyName} · ${_dashAlerts.length} ${remPlural}`
                : `${allBranchesLbl} · ${_dashAlerts.length} ${remPlural}`;
        }

        // Severity-Karten oben
        renderDashSeverityRow(data.countsBySeverity || {});

        // Kategorie-Filter
        renderDashFilterRow(data.countsByCategory || {});

        // Alarm-Liste
        renderDashAlerts();

        // Mindestlohn-Vertragsanpassung Warn-Banner (wage-adjustment.js)
        if (typeof waLoadBanner === 'function') waLoadBanner('dashWageAdjustBanner');
    } catch(e) {
        container.innerHTML = `<div style="padding:30px;text-align:center;color:#dc2626">Netzwerkfehler: ${e.message}</div>`;
    }
}

function renderDashSeverityRow(counts) {
    const row = document.getElementById('dashSeverityRow');
    if (!row) return;
    const order = ['critical', 'warning', 'info'];
    row.innerHTML = order.map(sev => {
        const c = counts[sev] || 0;
        const meta = DASH_SEVERITY_META[sev];
        return `<div style="background:${meta.bg};border:1px solid ${meta.border};color:${meta.text};border-radius:10px;padding:12px 16px;display:flex;align-items:center;gap:14px">
            <div style="font-size:28px;font-weight:700;line-height:1">${c}</div>
            <div style="font-size:13px;font-weight:600;text-transform:uppercase;letter-spacing:0.5px">${dashMetaLabel(meta)}</div>
        </div>`;
    }).join('');
}

function renderDashFilterRow(countsByCategory) {
    const row = document.getElementById('dashFilterRow');
    if (!row) return;
    const cats = Object.keys(DASH_CATEGORY_META);
    const total = _dashAlerts.length;
    const allLabel = (window.i18n && i18n.getLang() === 'en') ? 'All' : 'Alle';
    const allBtn = `<button onclick="dashSetCategoryFilter(null)"
        style="padding:6px 12px;border:1px solid ${_dashActiveCategoryFilter === null ? '#3b82f6' : '#e2e8f0'};border-radius:7px;background:${_dashActiveCategoryFilter === null ? '#dbeafe' : '#fff'};color:${_dashActiveCategoryFilter === null ? '#1e40af' : '#475569'};cursor:pointer;font-weight:600;font-size:12px">
        ${allLabel} (${total})
    </button>`;
    const others = cats.map(cat => {
        const c = countsByCategory[cat] || 0;
        if (c === 0) return '';
        const meta = DASH_CATEGORY_META[cat];
        const active = _dashActiveCategoryFilter === cat;
        return `<button onclick="dashSetCategoryFilter('${cat}')"
            style="padding:6px 12px;border:1px solid ${active ? '#3b82f6' : '#e2e8f0'};border-radius:7px;background:${active ? '#dbeafe' : '#fff'};color:${active ? '#1e40af' : '#475569'};cursor:pointer;font-weight:600;font-size:12px;display:inline-flex;align-items:center;gap:5px">
            ${meta.icon} ${dashMetaLabel(meta)} (${c})
        </button>`;
    }).join('');
    row.innerHTML = allBtn + others;
}

function dashSetCategoryFilter(cat) {
    _dashActiveCategoryFilter = cat;
    renderDashFilterRow(_dashAlerts.reduce((acc, a) => {
        acc[a.category] = (acc[a.category] || 0) + 1;
        return acc;
    }, {}));
    renderDashAlerts();
}

function renderDashAlerts() {
    const container = document.getElementById('dashAlertsContainer');
    if (!container) return;
    let alerts = _dashAlerts;
    if (_dashActiveCategoryFilter) {
        alerts = alerts.filter(a => a.category === _dashActiveCategoryFilter);
    }
    if (alerts.length === 0) {
        const isEn = window.i18n && i18n.getLang() === 'en';
        const titleTxt = isEn ? 'No reminders' : 'Keine Erinnerungen';
        const subTxt   = isEn
            ? 'All clear — no action needed in the next 90 days.'
            : 'Alles erledigt — kein Handlungsbedarf in den nächsten 90 Tagen.';
        container.innerHTML = `<div class="card" style="padding:48px;text-align:center;color:#15803d">
            <div style="font-size:42px">✓</div>
            <div style="font-weight:600;font-size:16px;margin-top:10px">${titleTxt}</div>
            <div style="font-size:13px;color:#94a3b8;margin-top:6px">${subTxt}</div>
        </div>`;
        return;
    }

    // Nach Kategorie gruppieren, in Sektionen rendern
    const byCat = {};
    alerts.forEach(a => {
        if (!byCat[a.category]) byCat[a.category] = [];
        byCat[a.category].push(a);
    });

    container.innerHTML = Object.keys(byCat).map(cat => {
        const meta = DASH_CATEGORY_META[cat] || { label: cat, icon: '•', color: '#475569' };
        const items = byCat[cat];
        // Sonderfall „Mindestlohn ok": Header allein reicht — keine Alert-Box
        // darunter (Walter: keine doppelte Aussage).
        const isOkOnly = cat === 'minimum_wage_ok';
        const countLabel = isOkOnly ? '' : `<span style="font-size:12px;color:#94a3b8">${items.length}</span>`;
        const itemsHtml  = isOkOnly ? '' : items.map(a => renderDashAlertRow(a)).join('');
        return `<div style="margin-bottom:20px">
            <div style="display:flex;align-items:center;gap:8px;margin-bottom:8px">
                <span style="font-size:18px">${meta.icon}</span>
                <h2 style="font-size:14px;font-weight:700;color:${meta.color};margin:0;text-transform:uppercase;letter-spacing:0.5px">${dashMetaLabel(meta)}</h2>
                ${countLabel}
            </div>
            ${itemsHtml ? `<div style="display:flex;flex-direction:column;gap:6px">${itemsHtml}</div>` : ''}
        </div>`;
    }).join('');
}

function renderDashAlertRow(a) {
    const sev = DASH_SEVERITY_META[a.severity] || DASH_SEVERITY_META.info;
    // Datums-Locale folgt der UI-Sprache: de-CH bei DE, en-CH bei EN
    const isEn = window.i18n && i18n.getLang() === 'en';
    const dateLocale = isEn ? 'en-CH' : 'de-CH';
    const dueTxt = a.dueDate
        ? new Date(a.dueDate).toLocaleDateString(dateLocale, {day:'2-digit',month:'2-digit',year:'numeric'})
        : '';

    // Title + Subtitle: bevorzugt via i18n.tFormat(key, args), Fallback auf
    // server-rendered DE-Strings wenn Key fehlt oder i18n noch nicht geladen.
    const title = (a.titleKey && window.i18n)
        ? i18n.tFormat(a.titleKey, a.titleArgs || {})
        : (a.title || '');
    const subtitle = (a.subtitleKey && window.i18n)
        ? i18n.tFormat(a.subtitleKey, a.subtitleArgs || {})
        : (a.subtitle || '');

    // Relative Datums-Phrase: heute / in X Tagen / X Tage überfällig
    let relTxt = '';
    if (a.daysUntil != null) {
        if (a.daysUntil < 0) {
            relTxt = window.i18n
                ? i18n.tFormat('relative.daysOverdue', { days: Math.abs(a.daysUntil) })
                : `${Math.abs(a.daysUntil)} Tage überfällig`;
        } else if (a.daysUntil === 0) {
            relTxt = window.i18n ? i18n.t('relative.today') : 'heute';
        } else {
            relTxt = window.i18n
                ? i18n.tFormat('relative.inDays', { days: a.daysUntil })
                : `in ${a.daysUntil} Tagen`;
        }
    }

    // QST-Pflicht-Karten springen direkt in den Quellensteuer-Tab des MA
    // (Walter 26.05.2026 — dort sind die Schnell-Buttons).
    // Ausweis-Ehepartner-Karten springen in den Familie-Tab (Walter 12.06.2026),
    // wo Variante-C-Upload den Ehegatten-Ausweis aufnimmt.
    const onClick = a.employeeId
        ? (a.category === 'qst_pflicht_offen'
            ? `onclick="dashOpenEmployeeQst(${a.employeeId})"`
            : a.category === 'spouse_doku_fehlt'
                ? `onclick="dashOpenEmployeeFamilie(${a.employeeId})"`
                : a.category === 'employee_doku_fehlt'
                    ? `onclick="dashOpenEmployeeQst(${a.employeeId})"`
                    : a.category === 'schwangerschaft'
                        ? `onclick="dashOpenEmployeePregnancy(${a.employeeId})"`
                        : `onclick="dashOpenEmployee(${a.employeeId})"`)
        : (a.periodeId ? `onclick="dashOpenLohnlauf()"` : '');
    const cursor = onClick ? 'cursor:pointer' : '';
    return `<div ${onClick} style="background:${sev.bg};border:1px solid ${sev.border};border-radius:8px;padding:10px 14px;display:flex;align-items:center;gap:14px;${cursor};transition:transform .08s">
        <div style="flex:1;min-width:0">
            <div style="font-weight:600;color:${sev.text};font-size:13.5px">${_e(title)}</div>
            <div style="font-size:12px;color:#475569;margin-top:2px">${_e(subtitle)}</div>
        </div>
        ${dueTxt ? `<div style="font-size:11.5px;color:#64748b;text-align:right;flex-shrink:0">
            <div style="font-weight:600">${dueTxt}</div>
            ${relTxt ? `<div style="color:#94a3b8">${relTxt}</div>` : ''}
        </div>` : ''}
    </div>`;
}

function dashOpenEmployee(employeeId, subTab) {
    if (!employeeId) return;
    window.activeEmpId = employeeId;
    showPage('mitarbeiter');
    // Liste lädt async; sobald geladen, MA selektieren + ggf. Sub-Tab wechseln
    setTimeout(() => {
        if (typeof selectEmployee === 'function') selectEmployee(employeeId);
        if (subTab && typeof switchEmpTab === 'function') {
            setTimeout(() => switchEmpTab(subTab), 250);
        }
    }, 350);
}

// Spezial-Sprung für QST-Pflicht-Lücken (Walter 26.05.2026): direkt in den
// Quellensteuer-Tab, wo die Schnell-Buttons sind.
function dashOpenEmployeeQst(employeeId) { dashOpenEmployee(employeeId, 'quellensteuer'); }
function dashOpenEmployeePregnancy(employeeId) { dashOpenEmployee(employeeId, 'mutterschaft'); }
// Walter-Vorgabe 12.06.2026: Sprung in den Familie-Tab, wo der Ehegatten-
// Ausweis via Variante-C-Upload hochgeladen werden kann.
function dashOpenEmployeeFamilie(employeeId) { dashOpenEmployee(employeeId, 'familie'); }
// Walter-Vorgabe 13.06.2026: Sprung in den Dokumente-Tab, wo ID/Pass (CH-
// Bürger) oder Bewilligung (C-Ausweis) für den MA hochgeladen werden kann.
function dashOpenEmployeeDokumente(employeeId) { dashOpenEmployee(employeeId, 'dokumente'); }

function dashOpenLohnlauf() { showPage('lohnlauf'); }

