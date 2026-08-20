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
// Ausgeklappte To-do-Hauptgruppen im Liquid-Dashboard (bleibt über Re-Renders erhalten).
let _dashExpandedCats = new Set();
function dashToggleCat(cat) {
    const g = document.querySelector('.liquid-todo-group[data-cat="' + cat + '"]');
    if (g) {
        g.classList.toggle('open');
        if (g.classList.contains('open')) _dashExpandedCats.add(cat); else _dashExpandedCats.delete(cat);
    }
}
// Ausgeklappte Wichtigkeitsstufen (Kritisch standardmässig offen, Achtung/Info zu).
let _dashExpandedSevs = new Set(['critical']);
function dashToggleSev(sev) {
    const g = document.querySelector('.liquid-todo-sev[data-sev="' + sev + '"]');
    if (g) {
        g.classList.toggle('open');
        if (g.classList.contains('open')) _dashExpandedSevs.add(sev); else _dashExpandedSevs.delete(sev);
    }
}

// Rendert die Hauptgruppen (Kategorie-Akkordeon) für eine Alarm-Teilmenge.
function renderLiquidCatGroups(list) {
    const byCat = {};
    const order = [];
    list.forEach(a => {
        if (!byCat[a.category]) { byCat[a.category] = []; order.push(a.category); }
        byCat[a.category].push(a);
    });
    return order.map(cat => {
        const meta = DASH_CATEGORY_META[cat] || { label: cat, icon: '•' };
        const items = byCat[cat];
        const label = dashMetaLabel(meta);
        // „Alle Mindestlöhne ok" o.ä. — reine Kopfzeile, kein Ausklappen.
        if (cat === 'minimum_wage_ok') {
            return `<div class="liquid-todo-row" style="cursor:default">
                <span>${meta.icon || '✓'}</span>
                <span><span class="liquid-todo-title">${_e(label)}</span></span>
                <span></span>
            </div>`;
        }
        const open = _dashExpandedCats.has(cat);
        // Innerhalb: Nacht Untersuch fehlt → abgelaufen → Rest, dann Vorname
        // (Walter 13.07.2026 / 19.07.2026).
        const rows = dashTodoSort(items).map(a => renderDashTodoRow(a)).join('');
        return `<div class="liquid-todo-group ${open ? 'open' : ''}" data-cat="${_e(cat)}">
            <div class="liquid-todo-group-head" onclick="dashToggleCat('${_e(cat)}')">
                <span class="ltg-icon">${meta.icon || '•'}</span>
                <span class="liquid-todo-title" style="flex:1">${_e(label)}</span>
                <span class="ltg-count">${items.length}</span>
                <span class="ltg-chevron">›</span>
            </div>
            <div class="liquid-todo-group-body">${rows}</div>
        </div>`;
    }).join('');
}

let _dashActiveCategoryFilter = null;  // null = alle Kategorien
let _dashActiveSeverityFilter = null;  // null = alle Stufen

// Reihenfolge entscheidet Sortierung der Filter-Buttons UND der Sektionen in
// der Alarm-Liste (Frontend nutzt Object.keys, Backend sortiert die Alerts
// passend dazu in DashboardService). Mindestlohn ist Walter-Priorität #1.
// i18n: `i18nKey` ersetzt zur Render-Zeit den Label-Text via i18n.t().
// Falls i18n.js noch nicht geladen ist (Race beim ersten Render), greift
// der `label`-Fallback (Deutsch) damit nichts leer bleibt.
const DASH_CATEGORY_META = {
    minimum_wage_violation: { i18nKey: 'dash.cat.minWageViolation', label: 'Mindestlohn-Verletzung', icon: '⚠️', color: '#b91c1c' },
    minimum_wage_ok:        { i18nKey: 'dash.cat.minWageOk',        label: 'Mindestlohn ok',         icon: '✅', color: '#15803d' },
    permit_expiring:        { i18nKey: 'dash.cat.permitExpiring',   label: 'Aufenthaltsbewilligung läuft ab', icon: '🪪', color: '#b91c1c' },
    permit_missing:         { i18nKey: 'dash.cat.permitMissing',    label: 'Bewilligung fehlt',      icon: '🪪', color: '#b91c1c' },
    probation_end:          { i18nKey: 'dash.cat.probationEnding',  label: 'Probezeit',              icon: '📋', color: '#92400e' },
    probezeit_gespraech_offen: { i18nKey: 'dash.cat.probationTalkOpen', label: 'Probezeitgespräch offen', icon: '📋', color: '#92400e' },
    contract_end:           { i18nKey: 'dash.cat.contractEnding',   label: 'Vertragsende',           icon: '📅', color: '#92400e' },
    kuendigung_ablauf:      { i18nKey: 'dash.cat.terminationEnding', label: 'Vertragsende Kündigung', icon: '🚪', color: '#b91c1c' },
    kuendigung_sperrfrist_ende: { i18nKey: 'dash.cat.terminationSperrfrist', label: 'Kündigung möglich (Sperrfrist)', icon: '⚖️', color: '#166534' },
    exit_pending_active:    { i18nKey: 'dash.cat.exitPendingActive',label: 'Austritt steht bevor',   icon: '🚪', color: '#b91c1c' },
    qst_pflicht_offen:      { i18nKey: 'dash.cat.qstPflichtOffen',  label: 'QST-Pflicht offen',      icon: '📋', color: '#b91c1c' },
    qst_kanton_mismatch:    { label: 'QST-Kanton ≠ Wohnkanton', icon: '🧾', color: '#991b1b' },
    ahv_nummer_fehlt:       { label: 'AHV-Nummer fehlt',        icon: '🆔', color: '#b91c1c' },
    umzug_datum_offen:      { label: 'Umzugsdatum bestätigen',  icon: '🚚', color: '#b45309' },
    spouse_doku_fehlt:      { i18nKey: 'dash.cat.spouseDokuFehlt',  label: 'Ausweis Ehepartner',     icon: '🪪', color: '#b91c1c' },
    qst_partner_daten:      { label: 'Ehepartner-Angaben unvollständig (QST)', icon: '💍', color: '#b91c1c' },
    kind_geschlecht_fehlt:  { label: 'Kind ohne Geschlecht (Familie)', icon: '🧒', color: '#b45309' },
    employee_doku_fehlt:    { i18nKey: 'dash.cat.employeeDokuFehlt',label: 'Ausweis Mitarbeiter',    icon: '🪪', color: '#b91c1c' },
    schwangerschaft:        { i18nKey: 'dash.cat.pregnancy',        label: 'Mutterschaft',           icon: '🤰', color: '#be185d' },
    night_work_untersuch_fehlt: { label: 'Nacht Untersuch fehlt', icon: '🌙', color: '#991b1b' },
    night_work_exam_fehlt:  { label: 'Nachtarbeit-Arztzeugnis fehlt', icon: '🌙', color: '#991b1b' },
    night_work_ausnahme_fehlt: { label: 'Nachtarbeit-Ausnahmeregelung fehlt', icon: '🌙', color: '#991b1b' },
    night_work_exam_expiring: { i18nKey: 'dash.cat.nightWorkExpiring', label: 'Nachtarbeit-Bewilligung läuft ab', icon: '🌙', color: '#92400e' },
    night_work_exam_mismatch: { label: 'Nachtarbeit-Enddatum in easy@work falsch', icon: '🌙', color: '#991b1b' },
    lohn_provisorisch:      { i18nKey: 'dash.cat.payrollOpen',      label: 'Lohnlauf',               icon: '💰', color: '#6b6152' },
    birthday:               { i18nKey: 'dash.cat.birthday',         label: 'Geburtstage',            icon: '🎂', color: '#9333ea' },
    anniversary:            { i18nKey: 'dash.cat.anniversary',      label: 'Dienstjubiläen',         icon: '🎉', color: '#15803d' },
    availability_missing:   { i18nKey: 'dash.cat.availabilityMissing', label: 'Verfügbarkeit fehlt', icon: '🕒', color: '#92400e' },
    audit_log_stumm:        { i18nKey: 'dash.cat.auditSilent', label: 'Aktivitäts-Log stumm', icon: '🧾', color: '#b91c1c' }
};

const DASH_SEVERITY_META = {
    critical: { i18nKey: 'dash.severity.critical', label: 'Kritisch', bg: '#fee2e2', border: '#fca5a5', text: '#991b1b' },
    warning:  { i18nKey: 'dash.severity.warning',  label: 'Achtung',  bg: '#fef3c7', border: '#fbbf24', text: '#92400e' },
    info:     { i18nKey: 'dash.severity.info',     label: 'Info',     bg: '#ece9e2', border: '#d0c8b8', text: '#6b6152' }
};

// Helfer: holt das übersetzte Label für eine Meta-Zeile. Fallback auf
// label (DE) wenn i18n.js noch nicht initialisiert ist.
function dashMetaLabel(meta) {
    if (!meta) return '';
    if (meta.i18nKey && window.i18n) {
        const t = window.i18n.t(meta.i18nKey);
        // Fehlt der Schlüssel im Dictionary, liefert t() den Key zurück →
        // dann auf das DE-Label zurückfallen (sonst steht „DASH.CAT.X" da).
        if (t && t !== meta.i18nKey) return t;
    }
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
        renderDashFilterRow(dashCategoryCounts(dashSeverityFilteredAlerts()));

        // Alarm-Liste
        renderDashAlerts();

        // To-dos-Kachel-Badge + (falls Seite offen) 3-Spalten-Seite aktualisieren
        dashUpdateTodoBadge();
        if (document.getElementById('page-todos')?.classList.contains('active')) renderTodosPage();

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
        const active = _dashActiveSeverityFilter === sev;
        return `<button type="button" onclick="dashSetSeverityFilter('${sev}')"
            title="${dashMetaLabel(meta)} filtern"
            style="background:${meta.bg};border:${active ? '3px solid #1a1a1a' : `1px solid ${meta.border}`};color:${meta.text};border-radius:10px;padding:${active ? '10px 14px' : '12px 16px'};display:flex;align-items:center;gap:14px;cursor:pointer;text-align:left;box-shadow:${active ? '0 0 0 3px rgba(60,55,48,.16)' : 'none'}">
            <div style="font-size:28px;font-weight:700;line-height:1">${c}</div>
            <div style="font-size:13px;font-weight:600;text-transform:uppercase;letter-spacing:0.5px">${dashMetaLabel(meta)}</div>
        </button>`;
    }).join('');
}

function renderDashFilterRow(countsByCategory) {
    const row = document.getElementById('dashFilterRow');
    if (!row) return;
    const cats = Object.keys(DASH_CATEGORY_META);
    const baseAlerts = dashSeverityFilteredAlerts();
    const total = baseAlerts.length;
    const allLabel = (window.i18n && i18n.getLang() === 'en') ? 'All' : 'Alle';
    const allBtn = `<button onclick="dashSetCategoryFilter(null)"
        style="padding:6px 12px;border:1px solid ${_dashActiveCategoryFilter === null ? '#3f3f3f' : '#e2e8f0'};border-radius:7px;background:${_dashActiveCategoryFilter === null ? '#ece9e2' : '#fff'};color:${_dashActiveCategoryFilter === null ? '#6b6152' : '#475569'};cursor:pointer;font-weight:600;font-size:12px">
        ${allLabel} (${total})
    </button>`;
    const others = cats.map(cat => {
        const c = countsByCategory[cat] || 0;
        if (c === 0) return '';
        const meta = DASH_CATEGORY_META[cat];
        const active = _dashActiveCategoryFilter === cat;
        return `<button onclick="dashSetCategoryFilter('${cat}')"
            style="padding:6px 12px;border:1px solid ${active ? '#3f3f3f' : '#e2e8f0'};border-radius:7px;background:${active ? '#ece9e2' : '#fff'};color:${active ? '#6b6152' : '#475569'};cursor:pointer;font-weight:600;font-size:12px;display:inline-flex;align-items:center;gap:5px">
            ${meta.icon} ${dashMetaLabel(meta)} (${c})
        </button>`;
    }).join('');
    row.innerHTML = allBtn + others;
}

function dashSeverityFilteredAlerts() {
    return _dashActiveSeverityFilter
        ? _dashAlerts.filter(a => a.severity === _dashActiveSeverityFilter)
        : _dashAlerts;
}

function dashCategoryCounts(alerts) {
    return alerts.reduce((acc, a) => {
        acc[a.category] = (acc[a.category] || 0) + 1;
        return acc;
    }, {});
}

function dashSetSeverityFilter(sev) {
    _dashActiveSeverityFilter = _dashActiveSeverityFilter === sev ? null : sev;
    _dashActiveCategoryFilter = null;
    renderDashSeverityRow(_dashAlerts.reduce((acc, a) => {
        acc[a.severity] = (acc[a.severity] || 0) + 1;
        return acc;
    }, {}));
    renderDashFilterRow(dashCategoryCounts(dashSeverityFilteredAlerts()));
    renderDashAlerts();
}

function dashSetCategoryFilter(cat) {
    _dashActiveCategoryFilter = cat;
    renderDashFilterRow(dashCategoryCounts(dashSeverityFilteredAlerts()));
    renderDashAlerts();
}

function renderDashAlerts() {
    const container = document.getElementById('dashAlertsContainer');
    if (!container) return;
    let alerts = dashSeverityFilteredAlerts();
    if (_dashActiveCategoryFilter) {
        alerts = alerts.filter(a => a.category === _dashActiveCategoryFilter);
    }
    const isLiquid = document.getElementById('page-dashboard')?.classList.contains('liquid-dashboard');
    if (alerts.length === 0) {
        const isEn = window.i18n && i18n.getLang() === 'en';
        const titleTxt = isEn ? 'No reminders' : 'Keine Erinnerungen';
        const subTxt   = isEn
            ? 'All clear — no action needed in the next 90 days.'
            : 'Alles erledigt — kein Handlungsbedarf in den nächsten 90 Tagen.';
        if (isLiquid) {
            container.innerHTML = `<div class="liquid-todo-row" style="cursor:default">
                <span>✓</span>
                <span><span class="liquid-todo-title">${titleTxt}</span><span class="liquid-todo-sub">${subTxt}</span></span>
                <span></span>
            </div>`;
            return;
        }
        container.innerHTML = `<div class="card" style="padding:48px;text-align:center;color:#15803d">
            <div style="font-size:42px">✓</div>
            <div style="font-weight:600;font-size:16px;margin-top:10px">${titleTxt}</div>
            <div style="font-size:13px;color:#94a3b8;margin-top:6px">${subTxt}</div>
        </div>`;
        return;
    }

    if (isLiquid) {
        // Nach Wichtigkeit (Severity) gruppieren: Kritisch offen zuoberst, Achtung +
        // Info als eingeklappte Menüpunkte. Innen jeweils die Hauptgruppen (Walter 04.07.2026).
        const bySev = {};
        alerts.forEach(a => { (bySev[a.severity] || (bySev[a.severity] = [])).push(a); });
        const sevOrder = ['critical', 'warning', 'info'];
        let html = '';
        sevOrder.forEach(sev => {
            const list = bySev[sev];
            if (!list || list.length === 0) return;
            const meta = DASH_SEVERITY_META[sev] || { label: sev, text: '#3f3f3f' };
            const open = _dashExpandedSevs.has(sev);
            html += `<div class="liquid-todo-sev ${open ? 'open' : ''}" data-sev="${sev}">
                <div class="liquid-todo-sev-head" onclick="dashToggleSev('${sev}')">
                    <span class="lts-dot" style="background:${meta.text || '#888'}"></span>
                    <span class="liquid-todo-sev-label" style="flex:1;color:${meta.text || '#3f3f3f'}">${_e(dashMetaLabel(meta))}</span>
                    <span class="ltg-count">${list.length}</span>
                    <span class="ltg-chevron">›</span>
                </div>
                <div class="liquid-todo-sev-body">${renderLiquidCatGroups(list)}</div>
            </div>`;
        });
        container.innerHTML = html;
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

// Rot aus Warnungs-Konfig (Systemeinstellungen → Warnungen):
// warnColor = none | red | red_overdue (nur wenn daysUntil < 0).
// Fallback ohne Server-Feld: frühere Hardcoded-Regeln.
function dashIsRedAlert(a) {
    const wc = String(a.warnColor || '').toLowerCase();
    if (wc === 'red') return true;
    if (wc === 'red_overdue') return a.daysUntil != null && a.daysUntil < 0;
    if (wc === 'none') return false;
    // Legacy-Fallback falls Backend noch kein warnColor liefert
    if (a.category === 'minimum_wage_violation') return true;
    if (a.category === 'permit_missing') return true;
    if (a.category === 'night_work_untersuch_fehlt') return true;
    return a.daysUntil != null && a.daysUntil < 0
        && (a.category === 'permit_expiring'
            || a.category === 'night_work_exam_expiring');
}

function renderDashTodoRow(a) {
    const meta = DASH_CATEGORY_META[a.category] || { icon: '•' };
    const { title, subtitle } = dashResolveAlertTexts(a);
    const onClick = a.employeeId
        ? ((a.category === 'qst_pflicht_offen' || a.category === 'qst_kanton_mismatch')
            ? `onclick="dashOpenEmployeeQst(${a.employeeId})"`
            : (a.category === 'spouse_doku_fehlt' || a.category === 'qst_partner_daten' || a.category === 'kind_geschlecht_fehlt')
                ? `onclick="dashOpenEmployeeFamilie(${a.employeeId})"`
                : a.category === 'employee_doku_fehlt'
                    ? `onclick="dashOpenEmployeeQst(${a.employeeId})"`
                    : a.category === 'schwangerschaft'
                        ? `onclick="dashOpenEmployeePregnancy(${a.employeeId})"`
                        : (a.category === 'permit_expiring' || a.category === 'permit_missing')
                            ? `onclick="dashOpenEmployeeQst(${a.employeeId})"`
                            : a.category === 'contract_end'
                                ? `onclick="dashOpenEmployeeVertrag(${a.employeeId})"`
                                : a.category === 'probezeit_gespraech_offen'
                                ? `onclick="dashOpenEmployeeProbezeit(${a.employeeId})"`
                                : a.category === 'availability_missing'
                                ? `onclick="dashOpenEmployeeVerfuegbarkeit(${a.employeeId})"`
                                : a.category === 'kuendigung_sperrfrist_ende'
                                ? `onclick="dashOpenEmployee(${a.employeeId}, 'absenzen')"`
                                : (a.category === 'exit_pending_active'
                                   || a.category === 'kuendigung_ablauf'
                                   || a.category === 'birthday'
                                   || a.category === 'anniversary'
                                   || a.category === 'night_work_untersuch_fehlt'
                                   || a.category === 'night_work_exam_fehlt'
                                   || a.category === 'night_work_ausnahme_fehlt'
                                   || a.category === 'night_work_exam_expiring'
                                   || a.category === 'night_work_exam_mismatch'
                                   || a.category === 'probation_end')
                                    ? `onclick="dashOpenEmployee(${a.employeeId}, 'uebersicht')"`
                                    : `onclick="dashOpenEmployee(${a.employeeId})"`)
        : (a.category === 'audit_log_stumm'
            ? `onclick="showPage('audit-log')"`
            : (a.periodeId ? `onclick="dashOpenLohnlauf()"` : ''));
    const critCls = dashIsRedAlert(a) ? ' liquid-todo-crit' : '';
    return `<div class="liquid-todo-row" ${onClick}>
        <span>${meta.icon || '•'}</span>
        <span>
            <span class="liquid-todo-title${critCls}">${_e(title)}</span>
            ${subtitle ? `<span class="liquid-todo-sub">${_e(subtitle)}</span>` : ''}
        </span>
        <span>›</span>
    </div>`;
}

// ── To-dos als eigene Seite (3 Spalten: Kritisch | Wichtig | Rest) ──────
// Öffnet die Seite und rendert sie aus dem bereits geladenen _dashAlerts.
function dashOpenTodos() {
    showPage('todos');
    renderTodosPage();   // sofort mit vorhandenen Daten zeichnen (kein Flackern)
    // Frisch für die AKTUELL gewählte Filiale nachladen — sonst zeigt die ToDo-Seite
    // die Alarme der Filiale, mit der man ins Programm eingestiegen ist, statt der
    // aktuell gewählten (Walter-Bug 06.07.2026). loadDashboard() rendert die 3 Spalten
    // neu, weil page-todos aktiv ist.
    if (typeof loadDashboard === 'function') loadDashboard();
}

// Baut eine saubere, klassische To-do-Liste (Dokument-Stil, NICHT der Sketch-
// Bildschirm) in #todosPrintArea und öffnet den Druck-/„Als PDF sichern"-Dialog.
// Anonymisierung (Walter-Vorgabe 13.07.2026): fuer PDFs an GF/Treuhaender
// duerfen KEINE Namen erscheinen — nur die Personalnummer. Entfernt den
// employeeName aus dem Text und raeumt Trennzeichen auf.
function _tpAnon(text, a) {
    let t = String(text || '');
    if (a && a.employeeName) {
        t = t.split(a.employeeName).join('');
    }
    return t.replace(/\s*·\s*·\s*/g, ' · ').replace(/^\s*[·—-]\s*/, '').trim();
}

function buildTodosPrintHtml(anonym = false) {
    const bySev = { critical: [], warning: [], info: [] };
    (_dashAlerts || []).forEach(a => { (bySev[a.severity] || bySev.info).push(a); });
    // Gleiche Sortierung wie am Bildschirm (Walter 13.07.2026).
    Object.keys(bySev).forEach(k => { bySev[k] = dashTodoSort(bySev[k]); });
    let branchLbl = 'Alle Filialen';
    try {
        const b = (typeof allBranches !== 'undefined' ? allBranches : []).find(x => x.id === Number(fixedCompanyProfileId));
        if (b) branchLbl = `${b.restaurantCode ? b.restaurantCode + ' – ' : ''}${b.branchName || b.companyName || ''}`;
    } catch (e) {}
    const today = new Date().toLocaleDateString('de-CH');
    const secTitle = { critical: 'Kritisch', warning: 'Wichtig', info: 'Information' };
    const section = (sev) => {
        const items = bySev[sev];
        const rows = items.length
            ? items.map(a => {
                let { title, subtitle: sub } = dashResolveAlertTexts(a);
                if (anonym) { title = _tpAnon(title, a); sub = _tpAnon(sub, a); }
                // ROT nur für Walters definierte Fälle (12.07.2026), auch im Druck.
                const critCls = dashIsRedAlert(a) ? ' tp-crit' : '';
                return `<tr><td class="tp-t${critCls}">${_e(title)}</td><td class="tp-s">${_e(sub)}</td></tr>`;
              }).join('')
            : `<tr><td colspan="2" class="tp-empty">— nichts offen —</td></tr>`;
        return `<h2 class="tp-h tp-${sev}">${secTitle[sev]} <span>(${items.length})</span></h2>
            <table class="tp-tbl"><tbody>${rows}</tbody></table>`;
    };
    return `<div class="tp-head"><img class="tp-logo" src="img/onecrew-logo.png" alt="OneCrew"><h1>To-do-Liste</h1><div class="tp-meta">${_e(branchLbl)} · ${today}${anonym ? ' · anonymisiert (nur Personalnummern)' : ''}</div></div>
        ${section('critical')}${section('warning')}${section('info')}`;
}

// Vor dem Druck waehlen (Walter-Vorgabe 13.07.2026): mit Namen (intern)
// oder anonymisiert (nur Personalnummern — fuer GF/Treuhaender).
function todosPrintPdf() {
    let ov = document.getElementById('todosPrintChoice');
    if (!ov) {
        ov = document.createElement('div');
        ov.id = 'todosPrintChoice';
        ov.style.cssText = 'position:fixed;inset:0;z-index:4000;background:rgba(60,55,48,0.4);display:flex;align-items:center;justify-content:center;padding:20px';
        ov.onclick = e => { if (e.target === ov) ov.style.display = 'none'; };
        ov.innerHTML = `
            <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:18px;max-width:440px;width:100%;padding:20px 22px;box-shadow:0 24px 60px rgba(60,55,48,0.22)">
                <div style="font-size:16px;font-weight:700;color:#3f3f3f;margin-bottom:4px">To-do-Liste drucken</div>
                <div style="font-size:12.5px;color:#8b8b8b;margin-bottom:14px">Für den Versand an GF/Treuhänder die anonymisierte Variante wählen — sie enthält keine Namen, nur Personalnummern.</div>
                <div style="display:flex;flex-direction:column;gap:10px">
                    <button onclick="_todosPrintRun(true)"
                            style="text-align:left;background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:12px 16px;cursor:pointer;font-size:14px;font-weight:700">
                        🔒 Anonymisiert — nur Personalnummern
                    </button>
                    <button onclick="_todosPrintRun(false)"
                            style="text-align:left;background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:12px 16px;cursor:pointer;font-size:14px;font-weight:700">
                        Mit Namen (interner Gebrauch)
                    </button>
                </div>
            </div>`;
        document.body.appendChild(ov);
    }
    ov.style.display = 'flex';
}
function _todosPrintRun(anonym) {
    const ov = document.getElementById('todosPrintChoice');
    if (ov) ov.style.display = 'none';
    const area = document.getElementById('todosPrintArea');
    if (area) area.innerHTML = buildTodosPrintHtml(anonym);
    setTimeout(() => window.print(), 80);
}

// Badge-Marker auf der To-dos-Kachel: Anzahl offener Punkte (ohne die
// reine „alles ok"-Bestätigung). Rot, versteckt wenn nichts ansteht.
function dashUpdateTodoBadge() {
    const badge = document.getElementById('dashTodoBadge');
    if (!badge) return;
    const n = (_dashAlerts || []).filter(a => a.category !== 'minimum_wage_ok').length;
    if (n > 0) { badge.textContent = n > 99 ? '99+' : n; badge.style.display = ''; }
    else { badge.style.display = 'none'; }
}

// Liefert das onclick-Attribut (Sprung zum passenden Modul) für einen Alarm.
function dashTodoOnClick(a) {
    if (a.employeeId) {
        switch (a.category) {
            case 'qst_pflicht_offen':   return `onclick="dashOpenEmployeeQst(${a.employeeId})"`;
            // QST-Kanton ≠ Wohnkanton (Walter 04.08.2026): direkt in den
            // QST-Tab des MA — dort wird der Tarif korrigiert.
            case 'qst_kanton_mismatch': return `onclick="dashOpenEmployeeQst(${a.employeeId})"`;
            case 'spouse_doku_fehlt':   return `onclick="dashOpenEmployeeFamilie(${a.employeeId})"`;
            case 'qst_partner_daten':   return `onclick="dashOpenEmployeeFamilie(${a.employeeId})"`;
            case 'kind_geschlecht_fehlt': return `onclick="dashOpenEmployeeFamilie(${a.employeeId})"`;
            case 'employee_doku_fehlt': return `onclick="dashOpenEmployeeQst(${a.employeeId})"`;
            case 'schwangerschaft':     return `onclick="dashOpenEmployeePregnancy(${a.employeeId})"`;
            case 'permit_expiring':
            case 'permit_missing':      return `onclick="dashOpenEmployeeQst(${a.employeeId})"`;
            case 'contract_end':        return `onclick="dashOpenEmployeeVertrag(${a.employeeId})"`;
            case 'kuendigung_sperrfrist_ende': return `onclick="dashOpenEmployee(${a.employeeId}, 'absenzen')"`;
            case 'kuendigung_ablauf':
            case 'exit_pending_active':
            case 'birthday':
            case 'anniversary':
            case 'night_work_untersuch_fehlt':
            case 'night_work_exam_fehlt':
            case 'night_work_ausnahme_fehlt':
            case 'night_work_exam_expiring':
            case 'night_work_exam_mismatch': return `onclick="dashOpenEmployee(${a.employeeId}, 'uebersicht')"`;
            default:                    return `onclick="dashOpenEmployee(${a.employeeId})"`;
        }
    }
    if (a.category === 'audit_log_stumm') return `onclick="showPage('audit-log')"`;
    return a.periodeId ? `onclick="dashOpenLohnlauf()"` : '';
}

/** Wochentag kurz + Datum (z.B. «Fr, 31.07.2026») — Probezeit-Todos. */
function dashFormatWeekdayDate(isoOrDate) {
    if (!isoOrDate) return '';
    const iso = String(isoOrDate).slice(0, 10);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(iso)) return '';
    const d = new Date(iso + 'T12:00:00');
    if (Number.isNaN(d.getTime())) return '';
    const isEn = window.i18n && i18n.getLang() === 'en';
    const loc = isEn ? 'en-CH' : 'de-CH';
    const wd = d.toLocaleDateString(loc, { weekday: 'short' }).replace(/\.$/, '');
    const rest = d.toLocaleDateString(loc, { day: '2-digit', month: '2-digit', year: 'numeric' });
    return wd + ', ' + rest;
}

/** Title/Subtitle inkl. Probezeit-Ende mit Wochentag (Walter 26.07.2026). */
function dashResolveAlertTexts(a) {
    const meta = DASH_CATEGORY_META[a.category] || {};
    const titleArgs = { ...(a.titleArgs || {}) };
    const subtitleArgs = { ...(a.subtitleArgs || {}) };
    if ((a.category === 'probation_end' || a.category === 'probezeit_gespraech_offen') && a.dueDate) {
        const ende = dashFormatWeekdayDate(a.dueDate);
        if (ende) {
            titleArgs.ende = ende;
            subtitleArgs.ende = ende;
        }
    }

    let titleKey = a.titleKey;
    if (a.category === 'probation_end') {
        if (a.daysUntil === 0) titleKey = 'alert.probation.ends_today';
        else if (a.daysUntil === 1) titleKey = 'alert.probation.ends_tomorrow';
        else titleKey = 'alert.probation.ends_in_days';
    }

    const title = (titleKey && window.i18n)
        ? i18n.tFormat(titleKey, titleArgs)
        : (a.title || dashMetaLabel(meta) || '');
    const subtitle = (a.subtitleKey && window.i18n)
        ? i18n.tFormat(a.subtitleKey, subtitleArgs)
        : (a.subtitle || '');
    return { title, subtitle };
}

// Pendenz-Zeile im Liquid-Glass-Look (sauberer Kreis + Text + Chevron).
function renderTodoSketchRow(a) {
    const { title, subtitle } = dashResolveAlertTexts(a);
    const tip = subtitle ? `${title} — ${subtitle}` : title;
    const critCls = dashIsRedAlert(a) ? ' td-crit' : '';
    return `<div class="td-row" ${dashTodoOnClick(a)} title="${_e(tip)}">
        <span class="td-check"><svg viewBox="0 0 40 40" aria-hidden="true"><circle cx="20" cy="20" r="12" stroke-width="1.8"/></svg></span>
        <span class="td-text">
            <span class="td-title${critCls}">${_e(title)}</span>
            ${subtitle ? `<span class="td-sub">${_e(subtitle)}</span>` : ''}
        </span>
        <span class="td-arrow"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 5 L16 12 L9 19" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg></span>
    </div>`;
}

// Rendert eine Spalte als flache Liste von Pendenz-Zeilen.
function renderTodosColumn(list) {
    if (!list.length) return '<div class="todo-empty">— nichts offen —</div>';
    return list.map(a => renderTodoSketchRow(a)).join('');
}

// Sortierung aus Warnungs-Konfig (todoPriority), dann DaysUntil (stärker
// überfällig zuerst), dann Vorname. Konfigurierbar unter Systemeinstellungen → Warnungen.
function dashTodoSort(list) {
    return [...list].sort((a, b) =>
        (a.todoPriority ?? 100) - (b.todoPriority ?? 100)
        || (a.daysUntil ?? 999999) - (b.daysUntil ?? 999999)
        || String(a.employeeName || a.title || '').localeCompare(String(b.employeeName || b.title || ''), 'de', { sensitivity: 'base' }));
}

function renderTodosPage() {
    const bySev = { critical: [], warning: [], info: [] };
    (_dashAlerts || []).forEach(a => { (bySev[a.severity] || bySev.info).push(a); });
    const map = { critical: 'Critical', warning: 'Warning', info: 'Info' };
    Object.keys(map).forEach(sev => {
        const col = document.getElementById('todosCol' + map[sev]);
        const cnt = document.getElementById('todosCnt' + map[sev]);
        if (col) col.innerHTML = renderTodosColumn(dashTodoSort(bySev[sev]));
        if (cnt) cnt.textContent = bySev[sev].length;
    });
}

function renderDashAlertRow(a) {
    const sev = DASH_SEVERITY_META[a.severity] || DASH_SEVERITY_META.info;
    // Datums-Locale folgt der UI-Sprache: de-CH bei DE, en-CH bei EN
    const isEn = window.i18n && i18n.getLang() === 'en';
    const dateLocale = isEn ? 'en-CH' : 'de-CH';
    const dueTxt = a.dueDate
        ? ((a.category === 'probation_end' || a.category === 'probezeit_gespraech_offen')
            ? dashFormatWeekdayDate(a.dueDate)
            : new Date(a.dueDate).toLocaleDateString(dateLocale, {day:'2-digit',month:'2-digit',year:'numeric'}))
        : '';

    // Title + Subtitle: bevorzugt via i18n.tFormat(key, args), Fallback auf
    // server-rendered DE-Strings wenn Key fehlt oder i18n noch nicht geladen.
    const { title, subtitle } = dashResolveAlertTexts(a);

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
    // QST-Kanton-Mismatch ebenfalls → QST-Tab (Walter 04.08.2026).
    // Ausweis-Ehepartner-Karten springen in den Familie-Tab (Walter 12.06.2026),
    // wo Variante-C-Upload den Ehegatten-Ausweis aufnimmt.
    const onClick = a.employeeId
        ? ((a.category === 'qst_pflicht_offen' || a.category === 'qst_kanton_mismatch')
            ? `onclick="dashOpenEmployeeQst(${a.employeeId})"`
            : (a.category === 'spouse_doku_fehlt' || a.category === 'qst_partner_daten' || a.category === 'kind_geschlecht_fehlt')
                ? `onclick="dashOpenEmployeeFamilie(${a.employeeId})"`
                : a.category === 'employee_doku_fehlt'
                    ? `onclick="dashOpenEmployeeQst(${a.employeeId})"`
                    : a.category === 'schwangerschaft'
                        ? `onclick="dashOpenEmployeePregnancy(${a.employeeId})"`
                        : (a.category === 'permit_expiring' || a.category === 'permit_missing')
                            ? `onclick="dashOpenEmployeeQst(${a.employeeId})"`
                            : a.category === 'contract_end'
                                ? `onclick="dashOpenEmployeeVertrag(${a.employeeId})"`
                                : a.category === 'probezeit_gespraech_offen'
                                ? `onclick="dashOpenEmployeeProbezeit(${a.employeeId})"`
                                : a.category === 'availability_missing'
                                ? `onclick="dashOpenEmployeeVerfuegbarkeit(${a.employeeId})"`
                                : a.category === 'kuendigung_sperrfrist_ende'
                                ? `onclick="dashOpenEmployee(${a.employeeId}, 'absenzen')"`
                                : (a.category === 'exit_pending_active'
                                   || a.category === 'kuendigung_ablauf'
                                   || a.category === 'birthday'
                                   || a.category === 'anniversary'
                                   || a.category === 'night_work_untersuch_fehlt'
                                   || a.category === 'night_work_exam_fehlt'
                                   || a.category === 'night_work_ausnahme_fehlt'
                                   || a.category === 'night_work_exam_expiring'
                                   || a.category === 'night_work_exam_mismatch'
                                   || a.category === 'probation_end')
                                    ? `onclick="dashOpenEmployee(${a.employeeId}, 'uebersicht')"`
                                    : `onclick="dashOpenEmployee(${a.employeeId})"`)
        : (a.category === 'audit_log_stumm'
            ? `onclick="showPage('audit-log')"`
            : (a.periodeId ? `onclick="dashOpenLohnlauf()"` : ''));
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
    // Ziel-Tab VOR dem Rendern vorgeben, damit das Detail direkt auf dem
    // richtigen Tab öffnet (kein nachträglicher Wechsel → kein Flackern).
    if (subTab) { try { activeEmpTab = subTab; } catch (_) {} }
    showPage('mitarbeiter');
    setTimeout(() => {
        const alreadySel = (typeof selectedEmployeeId !== 'undefined' && selectedEmployeeId === employeeId);
        if (!alreadySel && typeof selectEmployee === 'function') {
            // Rendert das Detail einmalig — und zwar auf activeEmpTab (= subTab).
            selectEmployee(employeeId);
        } else if (subTab && typeof switchEmpTab === 'function') {
            // MA schon selektiert → nur wechseln, wenn der sichtbare Tab abweicht.
            const curEl = document.querySelector('.emp-tab.active');
            const cur = curEl ? curEl.getAttribute('data-tab') : null;
            if (cur !== subTab) switchEmpTab(subTab);
        }
    }, 350);
}

// Spezial-Sprung für QST-Pflicht-Lücken (Walter 26.05.2026): direkt in den
// Quellensteuer-Tab, wo die Schnell-Buttons sind.
function dashOpenEmployeeQst(employeeId) { dashOpenEmployee(employeeId, 'quellensteuer'); }
function dashOpenEmployeePregnancy(employeeId) { dashOpenEmployee(employeeId, 'familie'); }
// Walter-Vorgabe 12.06.2026: Sprung in den Familie-Tab, wo der Ehegatten-
// Ausweis via Variante-C-Upload hochgeladen werden kann.
function dashOpenEmployeeFamilie(employeeId) { dashOpenEmployee(employeeId, 'familie'); }
// Walter-Vorgabe 13.06.2026: Sprung in den Dokumente-Tab, wo ID/Pass (CH-
// Bürger) oder Bewilligung (C-Ausweis) für den MA hochgeladen werden kann.
function dashOpenEmployeeDokumente(employeeId) { dashOpenEmployee(employeeId, 'dokumente'); }
// Walter-Vorgabe 07.07.2026: Sprung in den Verfügbarkeit-Tab, wo die
// verfügbaren Arbeitszeiten (L-GAV-Anlage) erfasst werden.
function dashOpenEmployeeVerfuegbarkeit(employeeId) { dashOpenEmployee(employeeId, 'verfuegbarkeit'); }
// Walter-Vorgabe 20.06.2026: „Vertrag läuft aus" springt in die Verträge-Seite
// des MA (eigene Seite, kein MA-Tab) und selektiert dort den Mitarbeiter.
// Walter-Vorgabe 12.07.2026 / 17.07.2026: Verträge stehen in der Übersicht
// (Block «Verträge»). Sprung in die MA-Maske statt auf page-vertraege.
function dashOpenEmployeeVertrag(employeeId) { dashOpenEmployee(employeeId, 'uebersicht'); }

// Probezeitgespräch offen → Restaurant Admin + Probezeit-Modal
// (Datum + Protokoll verknüpfen; kein Direkt-Upload, Walter 21.07.2026).
function dashOpenEmployeeProbezeit(employeeId) {
    if (!employeeId) return;
    window.activeEmpId = employeeId;
    try { activeEmpTab = 'verwarnungen'; } catch (_) {}
    showPage('mitarbeiter');
    setTimeout(async () => {
        try {
            if (typeof selectEmployee === 'function') await selectEmployee(employeeId);
            if (typeof switchEmpTab === 'function') switchEmpTab('verwarnungen');
            if (typeof openProbezeitModal === 'function') openProbezeitModal(employeeId);
        } catch (_) {}
    }, 400);
}

function dashOpenLohnlauf() { showPage('lohnlauf'); }

