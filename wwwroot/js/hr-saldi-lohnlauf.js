// ══════════════════════════════════════════════════════════════════════
// hr-saldi-lohnlauf.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════
//  SALDI-VORTRAG (HR-Modul, Migration vom Vorsystem)
//  Pro MA einmalig die fünf Saldi (+ 13. ML) als Lohnpositionen 901–906
//  in der Migrations-Periode erfassen. Bei erneutem Editieren werden die
//  bestehenden Einträge überschrieben — Idempotenz auf MA-Ebene.
// ══════════════════════════════════════════════════════════════════════
// HR-BEREICH → LOHNLAUF (Status-Cockpit, Vorab-PDF, Definitiver Abschluss)
// ══════════════════════════════════════════════════════════════════════

let _llCurrentPeriodeId = null;
let _llCurrentStatus    = null;
let _llVorabPdfBlobUrl  = null;
// Tab-State (Walter-Vorgabe 17.05.2026): 'akonto' oder 'definitiv'.
// Persistent in localStorage, Default 'akonto' (Akonto-Lauf kommt zeitlich
// zuerst — Mitte Monat. Definitivlauf läuft am Monatsende).
let _llTab = (() => {
    try { return localStorage.getItem('hrLohnlaufTab') || 'akonto'; }
    catch { return 'akonto'; }
})();

// Tab umschalten: setzt internen State, persistiert in localStorage,
// blendet die richtige View ein und triggert deren Loader.
function llSwitchTab(name) {
    _llTab = (name === 'definitiv') ? 'definitiv' : 'akonto';
    try { localStorage.setItem('hrLohnlaufTab', _llTab); } catch {}
    _llUpdateTabUi();
    // Beide Tabs hängen am gleichen Periode-Picker — Inhalt neu laden,
    // damit der eben sichtbare Tab aktuelle Daten zeigt.
    if (_llTab === 'akonto')    llLoadAkontoTab();
    else                         llLoadStatus();
}

// Tab-Pillen-Styles aktualisieren und Views ein-/ausblenden.
function _llUpdateTabUi() {
    const akBtn  = document.getElementById('llTabAkontoBtn');
    const defBtn = document.getElementById('llTabDefinitivBtn');
    const akView = document.getElementById('llAkontoView');
    const dfView = document.getElementById('llDefinitivView');
    if (!akBtn || !defBtn || !akView || !dfView) return;

    const isAk = (_llTab === 'akonto');
    // Aktiver Tab: blauer Border-Bottom + dunkler Text.
    akBtn.style.borderBottomColor = isAk ? '#6b6152' : 'transparent';
    akBtn.style.color             = isAk ? '#0f172a' : '#64748b';
    defBtn.style.borderBottomColor = isAk ? 'transparent' : '#6b6152';
    defBtn.style.color             = isAk ? '#64748b' : '#0f172a';
    akView.style.display = isAk ? '' : 'none';
    dfView.style.display = isAk ? 'none' : '';
}

function llInit() {
    // Hidden Filial-Select wird vom globalen Selektor (oben links) gespeist.
    // Sichtbar wird der Filial-Name in #llBranchInfo angezeigt.
    const sel = document.getElementById('llBranchSelect');
    const branches = (typeof allBranches !== 'undefined' && Array.isArray(allBranches)) ? allBranches : [];
    sel.innerHTML = branches.map(b =>
        `<option value="${b.id}">${b.restaurantCode || '?'} — ${b.branchName || b.companyName}</option>`).join('');

    // Jahres-Picker mit aktuellem Jahr ± 1 vor / 5 hinten
    const ySel = document.getElementById('llYearSelect');
    const now = new Date();
    const curY = now.getFullYear();
    ySel.innerHTML = '';
    for (let y = curY + 1; y >= curY - 5; y--) {
        const opt = document.createElement('option');
        opt.value = y; opt.textContent = y;
        if (y === curY) opt.selected = true;
        ySel.appendChild(opt);
    }
    document.getElementById('llMonthSelect').value = (now.getMonth() + 1).toString();

    document.getElementById('llAuditLog').innerHTML = '';

    // Tab-State aus localStorage anwenden (Default 'akonto').
    _llUpdateTabUi();

    // Aktuelle Filiale aus globalem Selektor übernehmen und Inhalt des
    // aktiven Tabs laden.
    llSyncFromGlobalBranch();
}

// Synchronisiert den (versteckten) Lohnlauf-Filial-Select mit dem globalen
// Selektor (oben links in der Sidebar). Wird beim Öffnen der Page UND bei
// jedem globalen Filialwechsel aufgerufen — siehe onBranchChange().
// Setzt zusätzlich Monat/Jahr auf die älteste OFFENE oder PROVISORISCH-
// abgeschlossene Periode dieser Filiale — gleiches Pattern wie im Lohn-Tab
// (siehe setDefaultLohnPeriode). User landet damit immer dort, wo Arbeit
// auf ihn wartet, nicht stumpf auf dem aktuellen Monat.
async function llSyncFromGlobalBranch() {
    const hiddenSel = document.getElementById('llBranchSelect');
    const infoEl    = document.getElementById('llBranchInfo');
    if (!hiddenSel || !infoEl) return;
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);

    const branches = (typeof allBranches !== 'undefined' && Array.isArray(allBranches)) ? allBranches : [];
    const cid = currentBranchId ? String(currentBranchId) : '';
    hiddenSel.value = cid;

    if (!cid) {
        infoEl.innerHTML = `<span style="color:#94a3b8">${_t('ll.hint.pickBranch')}</span>`;
        document.getElementById('llStatusCockpit').innerHTML =
            `<div style="padding:30px;text-align:center;color:#94a3b8">${_t('ll.hint.pickBranchLong')}</div>`;
        document.getElementById('llAuditLog').innerHTML = '';
        _llCurrentPeriodeId = null;
        return;
    }

    // Filial-Info-Box setzen
    const b = branches.find(x => String(x.id) === cid);
    if (b) {
        infoEl.innerHTML = `<span style="font-weight:600">${b.restaurantCode || '?'} — ${b.branchName || b.companyName}</span>
                            <span style="color:#94a3b8;font-size:12px;margin-left:10px">${_t('ll.hint.toSwitch')}</span>`;
    } else {
        infoEl.innerHTML = `<span style="color:#94a3b8">${_t('ll.hint.unknownBranch')}</span>`;
    }

    // Älteste nicht-abgeschlossene Periode finden und Monat/Jahr darauf setzen
    await llSetDefaultPeriode(parseInt(cid));

    // Aktiven Tab laden (Default Akonto).
    if (_llTab === 'akonto') llLoadAkontoTab();
    else                      llLoadStatus();
}

// Setzt die Monat/Jahr-Auswahl auf die älteste nicht-abgeschlossene Periode
// der Filiale. Falls keine offene Periode existiert (alles fertig oder
// noch keine Periode angelegt), bleibt der aktuelle Monat stehen.
async function llSetDefaultPeriode(companyProfileId) {
    const monthSel = document.getElementById('llMonthSelect');
    const yearSel  = document.getElementById('llYearSelect');
    if (!monthSel || !yearSel || !companyProfileId) return;
    try {
        const r = await fetch(`/api/payroll-perioden?companyProfileId=${companyProfileId}`, { headers: ah() });
        if (!r.ok) return;
        const arr = await r.json();
        const open = (arr || []).filter(p => p.status !== 'abgeschlossen');
        if (open.length === 0) return;
        open.sort((a, b) => (a.year - b.year) || (a.month - b.month));
        const target = open[0];

        // Sicherstellen, dass das Ziel-Jahr im Dropdown drin ist
        const have = Array.from(yearSel.options).map(o => parseInt(o.value));
        if (!have.includes(target.year)) {
            const opt = document.createElement('option');
            opt.value = target.year; opt.textContent = target.year;
            // sortiert einfügen (absteigend wie llInit es aufbaut)
            const newer = Array.from(yearSel.options).find(o => parseInt(o.value) < target.year);
            if (newer) yearSel.insertBefore(opt, newer); else yearSel.appendChild(opt);
        }
        yearSel.value  = String(target.year);
        monthSel.value = String(target.month);
    } catch { /* ignorieren — bleibt aktueller Monat */ }
}

function llBranchChanged() {
    if (_llTab === 'akonto') llLoadAkontoTab();
    else                      llLoadStatus();
}

// Onchange-Hook der gemeinsamen Periode-Selects (Monat/Jahr) — lädt den
// gerade aktiven Tab neu. Beide Tabs teilen sich Filiale/Monat/Jahr.
function llPeriodChanged() {
    if (_llTab === 'akonto') llLoadAkontoTab();
    else                      llLoadStatus();
}

// ══════════════════════════════════════════════════════════════════════
// Akonto-Tab (HR-Sicht) — HR sieht den Akonto-Lauf der gewählten Filiale +
// Periode, kann pro MA den Netto-Akonto direkt überschreiben (mit Grund),
// die Periode an GF zurückgeben, freigeben oder auszahlen.
//
// Endpoints:
//   GET  /api/akonto/workflow/status              → Periode + Zahlungen
//   POST /api/akonto/workflow/hr-override/{id}    → Netto-Korrektur (BEI_HR)
//   POST /api/akonto/workflow/zurueck-an-gf       → Periode zurück an GF
//   POST /api/akonto/workflow/hr-freigabe         → HR-Freigabe
//   POST /api/akonto/workflow/auszahlen           → Auszahlen (DTA)
// ══════════════════════════════════════════════════════════════════════

let _llAkontoData    = null;   // letzte Antwort von /status
let _llAkSelectedId  = null;   // aktuell selektierte Akonto-Zahlung-Id
let _llAkSlipReqToken = 0;     // Race-Schutz beim schnellen MA-Wechsel

async function llLoadAkontoTab() {
    const bar   = document.getElementById('llAkontoStatusBar');
    const list  = document.getElementById('llAkMaList');
    if (!bar || !list) return;

    const cid   = document.getElementById('llBranchSelect').value;
    const year  = document.getElementById('llYearSelect').value;
    const month = document.getElementById('llMonthSelect').value;
    if (!cid) {
        bar.innerHTML  = '';
        list.innerHTML = `<div style="padding:28px;text-align:center;color:#94a3b8;font-size:13px">Filiale oben links wählen…</div>`;
        _llAkSetEmpty();
        return;
    }
    list.innerHTML = `<div style="padding:28px;text-align:center;color:#94a3b8;font-size:13px">Lädt…</div>`;

    try {
        const r = await fetch(
            `/api/akonto/workflow/status?companyProfileId=${cid}&year=${year}&month=${month}&_=${Date.now()}`,
            { headers: ah(), cache: 'no-store' }
        );
        if (!r.ok) {
            bar.innerHTML = `<div style="padding:10px 14px;background:#fee2e2;color:#b91c1c;border-radius:7px;font-size:13px">Fehler beim Laden (HTTP ${r.status}).</div>`;
            list.innerHTML = '';
            return;
        }
        _llAkontoData = await r.json();
        _llAkRenderStatusBar(_llAkontoData, parseInt(year), parseInt(month));
        _llAkRenderMaList();

        // Auto-Select wie im GF-Workspace: alte Selektion behalten falls noch da,
        // sonst ersten MA wählen.
        const zList = _llAkontoData.zahlungen || [];
        if (_llAkSelectedId) {
            const stillThere = zList.find(z => z.id === _llAkSelectedId);
            if (stillThere)            llAkSelectMa(_llAkSelectedId);
            else if (zList.length > 0) llAkSelectMa(zList[0].id);
            else                       _llAkSetEmpty();
        } else if (zList.length > 0) {
            llAkSelectMa(zList[0].id);
        } else {
            _llAkSetEmpty();
        }
    } catch (e) {
        bar.innerHTML = `<div style="padding:10px 14px;background:#fee2e2;color:#b91c1c;border-radius:7px;font-size:13px">Verbindungsfehler: ${e.message}</div>`;
    }
}

// ── Status-Bar oben (Status + Counter + HR-Periode-Aktionen) ───────────
function _llAkRenderStatusBar(data, year, month) {
    const bar = document.getElementById('llAkontoStatusBar');
    if (!bar) return;

    const isAdmin     = currentUser?.role === 'admin';
    const isSuperUser = currentUser?.role === 'superuser';
    const isHr        = isAdmin || isSuperUser;

    // OFFEN: noch keine Akonto-Periode → kein MA-Detail
    if (!data || !data.akontoStatus || data.akontoStatus === 'OFFEN') {
        const monatStr = String(month).padStart(2, '0') + '.' + year;
        bar.innerHTML = `
        <div class="card" style="padding:16px">
            <div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap">
                <span style="background:#efece5;color:#6b6152;padding:4px 12px;border-radius:14px;font-size:12px;font-weight:700">OFFEN</span>
                <div style="font-size:13.5px;color:#475569">Akonto-Lauf ${monatStr} wurde vom GF noch nicht gestartet.</div>
            </div>
            <div style="font-size:12px;color:#94a3b8;margin-top:6px">
                Sobald der GF in der Lohnverwaltung „Akonto vorbereiten" gedrückt und alle Lohnblätter freigegeben hat, erscheinen sie hier zur HR-Kontrolle.
            </div>
        </div>`;
        return;
    }

    const statusMap = {
        IN_BEARBEITUNG_GF: { txt: 'In Bearbeitung (GF)', bg: '#fef3c7', col: '#92400e' },
        BEI_HR:            { txt: 'Bei HR',              bg: '#ece9e2', col: '#6b6152' },
        HR_FREIGEGEBEN:    { txt: 'HR-freigegeben',      bg: '#dcfce7', col: '#166534' },
        AUSBEZAHLT:        { txt: 'Ausbezahlt',          bg: '#bbf7d0', col: '#15803d' },
    };
    const st = statusMap[data.akontoStatus] || { txt: data.akontoStatus, bg: '#f1f5f9', col: '#475569' };

    const zahlungen   = Array.isArray(data.zahlungen) ? data.zahlungen : [];
    const total       = zahlungen.length;
    const hrBestaetigtCnt = zahlungen.filter(z => z.status === 'HR_BESTAETIGT' || z.status === 'AUSBEZAHLT').length;
    const ausbezahltCnt   = zahlungen.filter(z => z.status === 'AUSBEZAHLT').length;
    const fmtChf = n => 'CHF ' + (Math.round((parseFloat(n)||0) * 100) / 100).toLocaleString('de-CH', {minimumFractionDigits: 2, maximumFractionDigits: 2});
    const sumNetto = zahlungen.reduce((s, z) => s + (parseFloat(z.nettoAkonto) || 0), 0);
    const allDone  = total > 0 && hrBestaetigtCnt === total;

    let actionsHtml = '';
    if (data.akontoStatus === 'IN_BEARBEITUNG_GF') {
        actionsHtml = `<span style="color:#92400e;font-size:11.5px;font-weight:600;background:#fef3c7;padding:3px 9px;border-radius:8px">⏳ Wartet auf GF</span>`;
    } else if (data.akontoStatus === 'BEI_HR' && isHr) {
        // Walter 17.05.2026: kein Pauschal-„HR-Freigabe"-Button mehr — HR
        // bestätigt jeden MA einzeln (siehe Detail-Panel). Sobald alle MA
        // HR_BESTAETIGT sind, springt die Periode automatisch auf
        // HR_FREIGEGEBEN und der DTA-Button erscheint.
        actionsHtml = `
            <button class="btn btn-outline" onclick="llAkontoZurueckAnGf()" style="font-size:13px;padding:7px 12px;color:#dc2626;border-color:#fca5a5">↩ Alles zurück an GF</button>
            <span style="font-size:11.5px;color:#92400e;background:#fef3c7;padding:3px 9px;border-radius:8px">Pro MA „✓ HR-bestätigen" — DTA wird frei wenn ${total} / ${total}</span>`;
    } else if (data.akontoStatus === 'HR_FREIGEGEBEN' && isHr) {
        actionsHtml = `<button class="btn btn-primary" onclick="llAkontoAuszahlen()" style="font-size:13px;padding:7px 12px;background:#6b6152;border-color:#6b6152">💰 Akonto auszahlen (DTA)</button>`;
    } else if (data.akontoStatus === 'AUSBEZAHLT') {
        const dt = data.akontoAusbezahltAt ? new Date(data.akontoAusbezahltAt).toLocaleString('de-CH') : '–';
        actionsHtml = `<span style="color:#15803d;font-size:11.5px;font-weight:600;background:#bbf7d0;padding:3px 9px;border-radius:8px">✅ Ausbezahlt ${dt} (${ausbezahltCnt} MA)</span>`;
    }

    bar.innerHTML = `
    <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;padding:6px 4px">
        <div style="font-size:15px;font-weight:700;color:#0f172a">Akonto ${String(month).padStart(2,'0')}.${year}</div>
        <span style="background:${st.bg};color:${st.col};padding:4px 12px;border-radius:14px;font-size:12px;font-weight:700">${st.txt}</span>
        <span style="font-size:12.5px;color:#64748b">${hrBestaetigtCnt}/${total} HR-bestätigt · Summe ${fmtChf(sumNetto)}</span>
        <span style="display:inline-flex;gap:8px;flex-wrap:wrap;margin-left:auto">${actionsHtml}</span>
    </div>`;
}

// ── MA-Liste links (analog GF-Workspace) ───────────────────────────────
function _llAkRenderMaList() {
    const el = document.getElementById('llAkMaList');
    const cntEl = document.getElementById('llAkListCount');
    if (!el || !_llAkontoData) return;

    const zahlungen = Array.isArray(_llAkontoData.zahlungen) ? _llAkontoData.zahlungen : [];
    if (cntEl) cntEl.textContent = zahlungen.length ? `${zahlungen.length} MA` : '';
    if (!zahlungen.length) {
        el.innerHTML = `<div style="padding:28px;text-align:center;color:#94a3b8;font-size:13px">Keine Lohnzeilen</div>`;
        return;
    }
    // CLAUDE.md-Konvention: nach Vorname sortieren
    const sorted = [...zahlungen].sort((a, b) =>
        (a.firstName||'').localeCompare(b.firstName||'') || (a.lastName||'').localeCompare(b.lastName||''));

    const modelClass = (m) => ({ MTP:'model-badge-mtp', UTP:'model-badge-utp', FIX:'model-badge-fix', 'FIX-M':'model-badge-fix-m' })[m] || '';
    el.innerHTML = '';
    sorted.forEach(r => {
        const isSelected = r.id === _llAkSelectedId;
        const isFreigegeben  = r.status === 'FREIGEGEBEN_GF';
        const isHrBestaetigt = r.status === 'HR_BESTAETIGT';
        const isAusbezahlt   = r.status === 'AUSBEZAHLT';
        // Walter 17.05.2026: in der HR-Sicht zählt der grüne ✓-Avatar nur,
        // wenn der MA von HR bestätigt (oder ausbezahlt) ist. FREIGEGEBEN_GF
        // zeigt noch Initialen → klare Signal-Wirkung „muss noch von HR".
        const isDone = isHrBestaetigt || isAusbezahlt;
        const initials = ((r.firstName||'')[0]||'') + ((r.lastName||'')[0]||'');
        const model = r.modell || '';

        const row = document.createElement('div');
        row.className = 'lohn-emp-row';
        if (isSelected) row.classList.add('lohn-emp-active');
        row.dataset.akontoId = r.id;
        row.style.cursor = 'pointer';

        const sub = isAusbezahlt    ? `<span style="color:#15803d;font-weight:600">Akonto ausbezahlt</span>`
                  : isHrBestaetigt  ? `<span style="color:#15803d;font-weight:600">HR bestätigt</span>`
                  : isFreigegeben   ? `<span style="color:#6b6152;font-weight:600">GF freigegeben – wartet auf HR</span>`
                  : `<span style="color:#94a3b8">${r.employeeNumber||r.employeeId}</span>`;

        const avatar = isDone
            ? `<div style="width:34px;height:34px;border-radius:50%;background:#dcfce7;color:#166534;display:flex;align-items:center;justify-content:center;font-weight:700">✓</div>`
            : `<div style="width:34px;height:34px;border-radius:50%;background:#f1f5f9;color:#475569;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:12px">${initials}</div>`;

        const modelBadge = model
            ? `<span class="${modelClass(model)}" style="padding:2px 8px;border-radius:8px;font-size:10.5px;font-weight:600">${model}</span>`
            : '';

        const hrNote = r.kommentarHr
            ? ` <span title="HR-Notiz: ${(r.kommentarHr||'').replace(/"/g,'&quot;')}" style="color:#b91c1c">📝</span>`
            : '';

        row.innerHTML = `
            <div style="display:flex;align-items:center;gap:10px;padding:6px 8px">
                ${avatar}
                <div style="flex:1;min-width:0">
                    <div style="font-weight:600;color:#0f172a;font-size:13px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${r.firstName||''} ${r.lastName||''}${hrNote}</div>
                    <div style="font-size:11.5px;margin-top:1px">${sub}</div>
                </div>
                ${modelBadge}
            </div>`;
        row.addEventListener('click', () => llAkSelectMa(r.id));
        el.appendChild(row);
    });
    llAkFilterMaList();
}

// Live-Filter via Suchfeld oben
function llAkFilterMaList() {
    const q = (document.getElementById('llAkEmpSearch')?.value || '').trim().toLowerCase();
    const list = document.getElementById('llAkMaList');
    if (!list) return;
    Array.from(list.children).forEach(row => {
        if (!q) { row.style.display = ''; return; }
        const txt = (row.textContent || '').toLowerCase();
        row.style.display = txt.includes(q) ? '' : 'none';
    });
}

// MA selektieren + Lohnzettel laden
async function llAkSelectMa(zahlungId) {
    _llAkSelectedId = zahlungId;
    _llAkRenderMaList();   // Selektion-Highlight neu setzen

    const z = (_llAkontoData?.zahlungen || []).find(x => x.id === zahlungId);
    if (!z) { _llAkSetEmpty(); return; }

    const empty = document.getElementById('llAkDetailEmpty');
    const card  = document.getElementById('llAkDetailCard');
    const cnt   = document.getElementById('llAkDetailContent');
    if (!empty || !card || !cnt) return;
    empty.style.display = 'none';
    card.style.display  = '';

    // Skeleton während des Ladens
    cnt.innerHTML = `<div style="padding:28px;text-align:center;color:#94a3b8;font-size:13px">Lädt Lohnzettel…</div>`;

    // Race-Token: bei schnellem MA-Wechsel verwerfen wir veraltete Antworten.
    const reqToken = ++_llAkSlipReqToken;

    const cid   = document.getElementById('llBranchSelect').value;
    const year  = document.getElementById('llYearSelect').value;
    const month = document.getElementById('llMonthSelect').value;

    try {
        const r = await fetch(
            `/api/payroll/calculate?employeeId=${z.employeeId}&companyProfileId=${cid}&year=${year}&month=${month}&_=${Date.now()}`,
            { headers: ah(), cache: 'no-store' }
        );
        if (reqToken !== _llAkSlipReqToken) return; // zwischenzeitlich anderer MA gewählt
        if (!r.ok) {
            cnt.innerHTML = `<div style="padding:18px;color:#dc2626">Lohnzettel-Vorschau fehlgeschlagen (HTTP ${r.status}).</div>`;
            return;
        }
        const slip = await r.json();
        _llAkRenderDetail(z, slip);
    } catch (e) {
        if (reqToken !== _llAkSlipReqToken) return;
        cnt.innerHTML = `<div style="padding:18px;color:#dc2626">Fehler: ${e.message}</div>`;
    }
}

function _llAkSetEmpty() {
    const empty = document.getElementById('llAkDetailEmpty');
    const card  = document.getElementById('llAkDetailCard');
    if (empty) empty.style.display = '';
    if (card)  card.style.display  = 'none';
}

// Rendert: Header (Name + Nr + Stati) → Lohnzettel (renderLohnSlip) →
// Akonto-Box mit Netto-Betrag + ✎-Edit-Button (nur in BEI_HR + HR-User).
function _llAkRenderDetail(z, slip) {
    const cnt = document.getElementById('llAkDetailContent');
    if (!cnt) return;

    const isAdmin     = currentUser?.role === 'admin';
    const isSuperUser = currentUser?.role === 'superuser';
    const isHr        = isAdmin || isSuperUser;
    const periodIsBeiHr = _llAkontoData?.akontoStatus === 'BEI_HR';
    const periodIsHrFr  = _llAkontoData?.akontoStatus === 'HR_FREIGEGEBEN';
    const editable      = periodIsBeiHr && isHr;

    const isAusbezahlt   = z.status === 'AUSBEZAHLT';
    const isHrBestaetigt = z.status === 'HR_BESTAETIGT';
    const isFreigegeben  = z.status === 'FREIGEGEBEN_GF';
    const stPill = isAusbezahlt
        ? `<span style="background:#dcfce7;color:#166534;padding:3px 10px;border-radius:10px;font-size:11px;font-weight:700">✓ ausbezahlt</span>`
        : isHrBestaetigt
            ? `<span style="background:#dcfce7;color:#15803d;padding:3px 10px;border-radius:10px;font-size:11px;font-weight:700">✓ HR bestätigt</span>`
            : isFreigegeben
                ? `<span style="background:#ece9e2;color:#6b6152;padding:3px 10px;border-radius:10px;font-size:11px;font-weight:700">GF freigegeben – wartet auf HR</span>`
                : `<span style="background:#fef3c7;color:#92400e;padding:3px 10px;border-radius:10px;font-size:11px;font-weight:700">berechnet</span>`;

    const gfStamp = z.gfFreigegebenAt
        ? `<span style="color:#15803d;font-size:11.5px">✓ GF freigegeben am ${new Date(z.gfFreigegebenAt).toLocaleString('de-CH')}</span>`
        : '';

    const fmtChf = n => 'CHF ' + (Math.round((parseFloat(n)||0) * 100) / 100).toLocaleString('de-CH', {minimumFractionDigits: 2, maximumFractionDigits: 2});

    // HR-Aktions-Buttons je nach Status (Walter 17.05.2026 — pro-MA-Workflow):
    //  • FREIGEGEBEN_GF in BEI_HR  → „✓ HR-bestätigen"  + „✎ ändern"
    //  • HR_BESTAETIGT  in BEI_HR  → „↶ HR-Bestätigung zurückziehen"  + „✎ ändern"
    //  • HR_BESTAETIGT  in HR_FREIGEGEBEN (alle durch) → noch zurückziehbar, kein ändern (Periode-Lock)
    //  • AUSBEZAHLT → keine Aktionen
    let hrActions = '';
    if (isHr && periodIsBeiHr) {
        if (isFreigegeben) {
            hrActions = `
                <button class="btn btn-outline" onclick="llAkontoEditBetrag(${z.id}, ${z.nettoAkonto||0})" style="font-size:12px;padding:5px 12px">✎ ändern</button>
                <button class="btn btn-primary" onclick="llAkHrBestaetigen(${z.id})" style="font-size:12px;padding:5px 14px;background:#15803d;border-color:#15803d">✓ HR-bestätigen</button>`;
        } else if (isHrBestaetigt) {
            hrActions = `
                <button class="btn btn-outline" onclick="llAkontoEditBetrag(${z.id}, ${z.nettoAkonto||0})" style="font-size:12px;padding:5px 12px">✎ ändern</button>
                <button class="btn btn-outline" onclick="llAkHrZurueckziehen(${z.id})" style="font-size:12px;padding:5px 12px;color:#b91c1c;border-color:#fecaca">↶ Bestätigung zurückziehen</button>`;
        }
    } else if (isHr && periodIsHrFr && isHrBestaetigt) {
        hrActions = `<button class="btn btn-outline" onclick="llAkHrZurueckziehen(${z.id})" style="font-size:12px;padding:5px 12px;color:#b91c1c;border-color:#fecaca">↶ Bestätigung zurückziehen</button>`;
    }

    const lockHint = (isHr && !periodIsBeiHr && !periodIsHrFr)
        ? `<div style="font-size:11px;color:#94a3b8;margin-top:4px">🔒 HR-Aktionen nur in Phase „Bei HR" / „HR-freigegeben" möglich.</div>`
        : '';
    const hrKomBox = z.kommentarHr
        ? `<div style="margin-top:10px;padding:8px 12px;background:#fef3c7;border-left:3px solid #d97706;font-size:11.5px;color:#78350f;white-space:pre-wrap">${(z.kommentarHr||'').replace(/</g,'&lt;')}</div>`
        : '';

    cnt.innerHTML = `
    <div style="padding:14px 18px 0 18px">
        <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin-bottom:4px">
            <div style="font-size:16px;font-weight:700;color:#0f172a">${z.firstName||''} ${z.lastName||''}</div>
            ${stPill}
            <span style="font-size:12px;color:#94a3b8">Personal-Nr. ${z.employeeNumber||z.employeeId}</span>
            <span style="margin-left:auto">${gfStamp}</span>
        </div>
        ${hrKomBox}
    </div>
    <div id="llAkSlipMount" style="padding:0 4px"></div>
    <div style="padding:12px 18px 18px 18px;border-top:2px solid #0f172a;background:#f8fafc">
        <div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap">
            <div style="font-size:13px;color:#475569">Netto-Akonto an MA</div>
            <div style="font-size:18px;font-weight:700;color:#0f172a;font-variant-numeric:tabular-nums">${fmtChf(z.nettoAkonto)}</div>
            <span style="margin-left:auto;display:inline-flex;gap:8px;flex-wrap:wrap">${hrActions}</span>
        </div>
        ${lockHint}
    </div>`;

    // Lohnzettel rendern (gleicher Renderer wie GF / Definitivlauf)
    const mount = document.getElementById('llAkSlipMount');
    if (mount && typeof renderLohnSlip === 'function') {
        renderLohnSlip(slip, mount);
    }
}

// Pro-MA HR-Bestätigung (Walter 17.05.2026)
async function llAkHrBestaetigen(zahlungId) {
    try {
        const r = await fetch(`/api/akonto/workflow/hr-bestaetigen/${zahlungId}`, {
            method: 'POST', headers: ah()
        });
        if (!r.ok) { const j = await r.json().catch(()=>({})); throw new Error(j.error || `HTTP ${r.status}`); }
        if (typeof showToast === 'function') showToast('HR-bestätigt.', 'success');
        await llLoadAkontoTab();
        // Auto-Sprung zum nächsten noch-zu-bestätigenden MA
        const next = (_llAkontoData?.zahlungen || []).find(z => z.status === 'FREIGEGEBEN_GF');
        if (next) llAkSelectMa(next.id);
    } catch (e) { alert('Fehler: ' + e.message); }
}

async function llAkHrZurueckziehen(zahlungId) {
    if (!confirm('HR-Bestätigung zurückziehen?')) return;
    try {
        const r = await fetch(`/api/akonto/workflow/hr-zurueckziehen/${zahlungId}`, {
            method: 'POST', headers: ah()
        });
        if (!r.ok) { const j = await r.json().catch(()=>({})); throw new Error(j.error || `HTTP ${r.status}`); }
        if (typeof showToast === 'function') showToast('HR-Bestätigung zurückgezogen.', 'success');
        await llLoadAkontoTab();
    } catch (e) { alert('Fehler: ' + e.message); }
}

// ── Aktionen ───────────────────────────────────────────────────────────

async function llAkontoEditBetrag(zahlungId, altBetrag) {
    const neuStr = prompt(`Neuer Netto-Akonto-Betrag (alt: CHF ${altBetrag}):`, String(altBetrag));
    if (neuStr === null) return;
    const neu = parseFloat(String(neuStr).replace(/[^\d.,-]/g, '').replace(',', '.'));
    if (!Number.isFinite(neu) || neu < 0) { alert('Ungültiger Betrag.'); return; }
    const grund = prompt('Grund der Korrektur (wird im Audit gespeichert):');
    if (!grund || !grund.trim()) { alert('Grund ist Pflicht.'); return; }
    try {
        const r = await fetch(`/api/akonto/workflow/hr-override/${zahlungId}`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ neuerNettoAkonto: neu, grund: grund.trim() })
        });
        if (!r.ok) { const j = await r.json().catch(()=>({})); throw new Error(j.error || `HTTP ${r.status}`); }
        if (typeof showToast === 'function') showToast('Akonto korrigiert.', 'success');
        // Status + MA-Liste + aktueller MA-Detail neu laden (Auswahl bleibt
        // dank _llAkSelectedId erhalten).
        await llLoadAkontoTab();
    } catch (e) { alert('Korrektur fehlgeschlagen: ' + e.message); }
}

async function llAkontoZurueckAnGf() {
    const kommentar = prompt('Begründung für die Rücksendung an den GF:');
    if (!kommentar || !kommentar.trim()) return;
    const cid   = document.getElementById('llBranchSelect').value;
    const year  = parseInt(document.getElementById('llYearSelect').value);
    const month = parseInt(document.getElementById('llMonthSelect').value);
    try {
        const r = await fetch('/api/akonto/workflow/zurueck-an-gf', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyProfileId: parseInt(cid), year, month, kommentar: kommentar.trim() })
        });
        if (!r.ok) { const j = await r.json().catch(()=>({})); throw new Error(j.error || `HTTP ${r.status}`); }
        if (typeof showToast === 'function') showToast('Periode zurück an GF.', 'success');
        await llLoadAkontoTab();
    } catch (e) { alert('Fehler: ' + e.message); }
}

// LEGACY: Pauschal-HR-Freigabe (Walter-Vorgabe 17.05.2026: durch pro-MA-Flow
// ersetzt). Wird vom neuen UI nicht mehr aufgerufen. Backend-Endpoint bleibt
// trotzdem (akzeptiert noch alte Clients und markiert dann alle FREIGEGEBEN_GF
// als HR_BESTAETIGT).
async function llAkontoHrFreigabe() {
    if (!confirm('Pauschal HR-Freigabe (legacy)? Besser jeden MA einzeln bestätigen.')) return;
    const cid   = document.getElementById('llBranchSelect').value;
    const year  = parseInt(document.getElementById('llYearSelect').value);
    const month = parseInt(document.getElementById('llMonthSelect').value);
    try {
        const r = await fetch('/api/akonto/workflow/hr-freigabe', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyProfileId: parseInt(cid), year, month })
        });
        if (!r.ok) { const j = await r.json().catch(()=>({})); throw new Error(j.error || `HTTP ${r.status}`); }
        if (typeof showToast === 'function') showToast('HR-Freigabe (pauschal) erteilt.', 'success');
        await llLoadAkontoTab();
    } catch (e) { alert('Fehler: ' + e.message); }
}

async function llAkontoAuszahlen() {
    if (!confirm('Akonto auszahlen? Datensätze werden eingefroren und können nur noch durch Admin wieder geöffnet werden.')) return;
    const cid   = document.getElementById('llBranchSelect').value;
    const year  = parseInt(document.getElementById('llYearSelect').value);
    const month = parseInt(document.getElementById('llMonthSelect').value);
    try {
        const r = await fetch('/api/akonto/workflow/auszahlen', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyProfileId: parseInt(cid), year, month })
        });
        if (!r.ok) { const j = await r.json().catch(()=>({})); throw new Error(j.error || `HTTP ${r.status}`); }
        const res = await r.json();
        if (typeof showToast === 'function') showToast(`Akonto ausbezahlt — ${res.countAusbezahlt} Datensätze.`, 'success');
        await llLoadAkontoTab();
    } catch (e) { alert('Fehler: ' + e.message); }
}

async function llLoadStatus() {
    const cid   = document.getElementById('llBranchSelect').value;
    const year  = document.getElementById('llYearSelect').value;
    const month = document.getElementById('llMonthSelect').value;
    const cockpit = document.getElementById('llStatusCockpit');
    const auditEl = document.getElementById('llAuditLog');
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    if (!cid) { cockpit.innerHTML = `<div style="padding:30px;text-align:center;color:#94a3b8">${_t('ll.hint.pickBranchLong')}</div>`; return; }

    cockpit.innerHTML = `<div style="padding:30px;text-align:center;color:#94a3b8">${_t('ll.loading')}</div>`;
    auditEl.innerHTML = '';

    try {
        const r = await fetch(`/api/payroll-perioden/current?companyProfileId=${cid}&year=${year}&month=${month}`, { headers: ah() });
        const txt = await r.text();
        let p = null;
        if (txt && txt.trim() && txt.trim() !== 'null') {
            try { p = JSON.parse(txt); } catch {}
        }
        if (!p) {
            cockpit.innerHTML = `
            <div class="card" style="padding:30px;text-align:center">
                <div style="font-size:42px">📭</div>
                <div style="margin-top:12px;font-weight:600;color:#475569">${_t('ll.noPeriod', { month, year })}</div>
                <div style="font-size:12.5px;color:#94a3b8;margin-top:6px">${_t('ll.noPeriod.hint')}</div>
            </div>`;
            return;
        }
        _llCurrentPeriodeId = p.id;
        _llCurrentStatus    = p.status;
        await llRenderCockpit(p, cid, year, month);
        await llLoadAudit(p.id);
    } catch (e) {
        cockpit.innerHTML = `<div style="color:#dc2626;padding:14px">${_t('ll.error', { msg: e.message })}</div>`;
    }
}

async function llRenderCockpit(p, cid, year, month) {
    const cockpit = document.getElementById('llStatusCockpit');
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    const fmtDate  = d => d ? new Date(d).toLocaleDateString('de-CH', {day:'2-digit',month:'2-digit',year:'numeric'}) : '–';
    const fmtDateTime = d => d ? new Date(d).toLocaleString('de-CH') : '–';

    // Validate-Aufruf für Vorbedingungen-Hinweise
    let validation = null;
    try {
        const vr = await fetch(`/api/lohnlauf/${p.id}/validate`, { headers: ah() });
        if (vr.ok) validation = await vr.json();
    } catch {}

    const isOffen        = p.status === 'offen';
    const isProvisorisch = p.status === 'provisorisch_abgeschlossen';
    const isAbgeschlossen= p.status === 'abgeschlossen';

    const statusBadge = isAbgeschlossen
        ? `<span style="background:#dcfce7;color:#166534;padding:4px 12px;border-radius:14px;font-size:12px;font-weight:700">${_t('ll.status.closed')}</span>`
        : isProvisorisch
            ? `<span style="background:#fef3c7;color:#92400e;padding:4px 12px;border-radius:14px;font-size:12px;font-weight:700">${_t('ll.status.provisional')}</span>`
            : `<span style="background:#efece5;color:#6b6152;padding:4px 12px;border-radius:14px;font-size:12px;font-weight:700">${_t('ll.status.open')}</span>`;

    const isAdmin     = currentUser?.role === 'admin';
    const isSuperUser = currentUser?.role === 'superuser';
    const isHr        = isAdmin || isSuperUser;

    let actionsHtml = '';
    if (isOffen) {
        const issues = validation?.issues || [];
        if (issues.length === 0) {
            actionsHtml = `<div style="padding:14px;background:#efece5;border-left:3px solid #6b6152;border-radius:7px;color:#5a5348;font-size:13px">
                ${_t('ll.preconditionsOk')}
            </div>`;
        } else {
            actionsHtml = `<div style="padding:14px;background:#fef2f2;border-left:3px solid #b91c1c;border-radius:7px;color:#7f1d1d;font-size:13px">
                <b>${_t('ll.openIssues')}</b>
                <ul style="margin:6px 0 0 18px;padding:0">${issues.map(i => `<li>${i}</li>`).join('')}</ul>
            </div>`;
        }
    } else if (isProvisorisch) {
        actionsHtml = `
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:6px">
            <button class="btn btn-primary" onclick="llOpenVorabPdf()" style="font-size:13px;padding:10px">
                ${_t('ll.btn.showVorabPdf')}
            </button>
            ${isHr ? `
            <button class="btn btn-primary" onclick="llOpenDefinitiv()" style="font-size:13px;padding:10px;background:#15803d;border-color:#15803d">
                ${_t('ll.btn.definitivClose')}
            </button>
            <button class="btn btn-outline" onclick="llOpenZurueck()" style="font-size:13px;padding:10px;color:#dc2626;border-color:#fca5a5;grid-column:1/-1">
                ${_t('ll.btn.zurueckAnGf')}
            </button>
            ` : ''}
        </div>`;
    } else if (isAbgeschlossen) {
        actionsHtml = `
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:6px">
            <button class="btn btn-outline" onclick="llOpenVorabPdf()" style="font-size:13px;padding:10px">
                ${_t('ll.btn.showPayslips')}
            </button>
            <button class="btn btn-primary" onclick="llDownloadDta('ma')" style="font-size:13px;padding:10px;background:#15803d;border-color:#15803d">
                ${_t('ll.btn.dtaMa')}
            </button>
            <button class="btn btn-outline" onclick="llDownloadDta('behoerden')" style="font-size:13px;padding:10px;grid-column:1/-1">
                ${_t('ll.btn.dtaBehoerden')}
            </button>
            ${isAdmin ? `
            <button class="btn btn-outline" onclick="llWiederOeffnen()" style="font-size:13px;padding:10px;color:#92400e;border-color:#fde68a;grid-column:1/-1">
                ${_t('ll.btn.reopen')}
            </button>
            ` : ''}
        </div>`;
    }

    cockpit.innerHTML = `
    <div class="card" style="padding:18px">
        <div style="display:flex;align-items:center;gap:12px;margin-bottom:14px">
            <div style="font-size:15px;font-weight:700;color:#0f172a">${p.label}</div>
            <span style="font-size:12px;color:#64748b">${fmtDate(p.periodFrom)} – ${fmtDate(p.periodTo)}</span>
            <div style="margin-left:auto">${statusBadge}</div>
        </div>
        <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(170px,1fr));gap:10px;margin-bottom:14px">
            ${tileCard(_t('ll.tile.provisionalAt'), p.provisorischAbgeschlossenAm ? fmtDateTime(p.provisorischAbgeschlossenAm) : '–', '#92400e')}
            ${tileCard(_t('ll.tile.finalAt'),    p.abgeschlossenAm ? fmtDateTime(p.abgeschlossenAm) : '–', '#166534')}
            ${tileCard(_t('ll.tile.payoutDate'), p.auszahlungsdatum ? fmtDate(p.auszahlungsdatum) : '–', '#6b6152')}
            ${tileCard(_t('ll.tile.periodId'), p.id, '#64748b')}
        </div>
        ${actionsHtml}
    </div>`;
}

async function llLoadAudit(periodeId) {
    const el = document.getElementById('llAuditLog');
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
    try {
        const r = await fetch(`/api/payroll-perioden/${periodeId}/audit`, { headers: ah() });
        if (!r.ok) { el.innerHTML = ''; return; }
        const list = await r.json();
        if (list.length === 0) { el.innerHTML = ''; return; }
        const labels = {
            PROVISORISCH_ABGESCHLOSSEN: { txt:_t('ll.audit.action.provisorisch'), col:'#92400e', bg:'#fef3c7' },
            DEFINITIV_ABGESCHLOSSEN:    { txt:_t('ll.audit.action.definitiv'),    col:'#166534', bg:'#dcfce7' },
            ZURUECK_AN_GF:              { txt:_t('ll.audit.action.zurueck'),       col:'#b91c1c', bg:'#fee2e2' },
            WIEDER_GEOEFFNET:           { txt:_t('ll.audit.action.reopened'),      col:'#92400e', bg:'#fef3c7' },
            AN_GF_GESENDET:             { txt:_t('ll.audit.action.sentToGf'),      col:'#6b6152', bg:'#efece5' },
        };
        el.innerHTML = `
        <div class="card" style="padding:0;overflow:hidden">
            <div style="padding:10px 16px;border-bottom:1px solid #e2e8f0;font-size:12px;font-weight:700;color:#475569">${_t('ll.audit.title')}</div>
            <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                <thead>
                    <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0">
                        <th style="padding:8px 12px;text-align:left;font-weight:600;color:#475569">${_t('ll.audit.col.date')}</th>
                        <th style="padding:8px 12px;text-align:left;font-weight:600;color:#475569">${_t('ll.audit.col.user')}</th>
                        <th style="padding:8px 12px;text-align:left;font-weight:600;color:#475569">${_t('ll.audit.col.action')}</th>
                        <th style="padding:8px 12px;text-align:left;font-weight:600;color:#475569">${_t('ll.audit.col.note')}</th>
                    </tr>
                </thead>
                <tbody>
                    ${list.map(e => {
                        const l = labels[e.action] || { txt: e.action, col:'#475569', bg:'#f1f5f9' };
                        return `<tr style="border-top:1px solid #f1f5f9">
                            <td style="padding:6px 12px;color:#64748b">${new Date(e.createdAt).toLocaleString('de-CH')}</td>
                            <td style="padding:6px 12px">${e.userName}</td>
                            <td style="padding:6px 12px"><span style="background:${l.bg};color:${l.col};padding:2px 9px;border-radius:8px;font-size:10.5px;font-weight:600">${l.txt}</span></td>
                            <td style="padding:6px 12px;color:#475569">${e.bemerkung || ''}</td>
                        </tr>`;
                    }).join('')}
                </tbody>
            </table>
        </div>`;
    } catch { el.innerHTML = ''; }
}

// ── Vorab-PDF Modal ────────────────────────────────────────────────────
async function llOpenVorabPdf() {
    if (!_llCurrentPeriodeId) return;
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    const sub = document.getElementById('llVorabPdfSub');
    sub.textContent = _t('ll.vorab.generating');
    const frame = document.getElementById('llVorabPdfFrame');
    frame.src = '';
    const modal = document.getElementById('llVorabPdfModal');
    modal.style.display = 'flex';
    if (window.i18n && window.i18n.applyAll) window.i18n.applyAll(modal);
    try {
        const r = await fetch(`/api/lohnlauf/${_llCurrentPeriodeId}/vorab-pdf`, { headers: ah() });
        if (!r.ok) {
            let msg = _t('ll.vorab.errLoad');
            try { const j = await r.json(); if (j.message) msg = j.message; } catch {}
            sub.textContent = msg;
            return;
        }
        const blob = await r.blob();
        if (_llVorabPdfBlobUrl) URL.revokeObjectURL(_llVorabPdfBlobUrl);
        _llVorabPdfBlobUrl = URL.createObjectURL(blob);
        frame.src = _llVorabPdfBlobUrl;
        sub.textContent = _t('ll.vorab.sizeInfo', { id: _llCurrentPeriodeId, kb: (blob.size/1024).toFixed(0) });
    } catch (e) {
        sub.textContent = _t('ll.error', { msg: e.message });
    }
}

function closeLlVorabPdf() {
    document.getElementById('llVorabPdfModal').style.display = 'none';
    document.getElementById('llVorabPdfFrame').src = '';
    if (_llVorabPdfBlobUrl) { URL.revokeObjectURL(_llVorabPdfBlobUrl); _llVorabPdfBlobUrl = null; }
}

async function llVorabPdfDownload() {
    await saveUrlAsk(_llVorabPdfBlobUrl, `Lohnlauf_Vorab_${_llCurrentPeriodeId}.pdf`);
}

function llVorabPdfPrint() {
    const frame = document.getElementById('llVorabPdfFrame');
    if (frame?.contentWindow) frame.contentWindow.print();
}

// ── Posteingang-Versand (Stub bis Phase 4 — schickt Hinweis) ───────────
async function llSendToPosteingang(target) {
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
    alert(_t('ll.send.todo'));
}

// ── DTA-Download (pain.001 XML) ────────────────────────────────────────
async function llDownloadDta(typ) {
    if (!_llCurrentPeriodeId) return;
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    const url = `/api/lohnlauf/${_llCurrentPeriodeId}/dta-${typ}`;
    try {
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) {
            let msg = _t('ll.dta.errGenerate');
            try { const j = await r.json(); if (j.message) msg = j.message; } catch {}
            alert(msg);
            return;
        }
        const blob = await r.blob();
        await saveBlobAsk(blob, `DTA_${typ === 'ma' ? 'Mitarbeiter' : 'Behoerden'}_Periode_${_llCurrentPeriodeId}.xml`);
        showToast(_t('ll.dta.toast'), 'success');
    } catch (e) {
        alert(_t('ll.dta.connError', { msg: e.message }));
    }
}

// ── "Zurück an GF" ─────────────────────────────────────────────────────
function llOpenZurueck() {
    document.getElementById('llZurueckBemerkung').value = '';
    const modal = document.getElementById('llZurueckModal');
    modal.style.display = 'flex';
    if (window.i18n && window.i18n.applyAll) window.i18n.applyAll(modal);
}
function closeLlZurueck() { document.getElementById('llZurueckModal').style.display = 'none'; }
async function llSubmitZurueck() {
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    const bemerkung = document.getElementById('llZurueckBemerkung').value.trim();
    if (!bemerkung) { alert(_t('ll.zurueck.errEmptyNote')); return; }
    if (!confirm(_t('ll.zurueck.confirm', { note: bemerkung }))) return;
    try {
        const r = await fetch(`/api/payroll-perioden/${_llCurrentPeriodeId}/zurueck-an-gf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId: currentUser?.id ?? 0, bemerkung })
        });
        if (!r.ok) { const j = await r.json().catch(()=>({})); throw new Error(j.message || 'Fehler'); }
        closeLlZurueck();
        showToast(_t('ll.zurueck.toast'), 'success');
        await llLoadStatus();
    } catch (e) { alert(e.message); }
}

// ── Definitiver Abschluss ──────────────────────────────────────────────
function llOpenDefinitiv() {
    // Default-Auszahlungsdatum: morgen
    const t = new Date(); t.setDate(t.getDate() + 1);
    document.getElementById('llDefAuszahlung').value = t.toISOString().slice(0,10);
    const modal = document.getElementById('llDefinitivModal');
    modal.style.display = 'flex';
    if (window.i18n && window.i18n.applyAll) window.i18n.applyAll(modal);
}
function closeLlDefinitiv() { document.getElementById('llDefinitivModal').style.display = 'none'; }
async function llSubmitDefinitiv() {
    const _t = (k, args) => (window.i18n ? (args ? window.i18n.tFormat(k, args) : window.i18n.t(k)) : k);
    const auszahlungsdatum = document.getElementById('llDefAuszahlung').value;
    if (!auszahlungsdatum) { alert(_t('ll.definitiv.errPayoutMissing')); return; }
    if (!confirm(_t('ll.definitiv.confirm', { date: auszahlungsdatum }))) return;
    // Buttons sperren + Submit-Button-Text ändern, damit klar ist dass was läuft.
    const submitBtn = document.querySelector('#llDefinitivModal .btn-primary');
    const cancelBtn = document.querySelector('#llDefinitivModal .btn-outline, #llDefinitivModal button:not(.btn-primary)');
    const origLabel = submitBtn ? submitBtn.textContent : '';
    if (submitBtn) { submitBtn.disabled = true; submitBtn.textContent = _t('ll.definitiv.processing'); }
    if (cancelBtn) { cancelBtn.disabled = true; }
    try {
        const r = await fetch(`/api/payroll-perioden/${_llCurrentPeriodeId}/definitiv-abschliessen`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId: currentUser?.id ?? 0, auszahlungsdatum })
        });
        if (!r.ok) { const j = await r.json().catch(()=>({})); throw new Error(j.message || 'Fehler'); }
        closeLlDefinitiv();
        showToast(_t('ll.definitiv.toast'), 'success');
        await llLoadStatus();
    } catch (e) {
        alert(e.message);
    } finally {
        if (submitBtn) { submitBtn.disabled = false; submitBtn.textContent = origLabel; }
        if (cancelBtn) { cancelBtn.disabled = false; }
    }
}

// ── Wieder öffnen (Admin only) ─────────────────────────────────────────
async function llWiederOeffnen() {
    const _t = (k) => (window.i18n ? window.i18n.t(k) : k);
    const bemerkung = prompt(_t('ll.reopen.prompt'));
    if (!bemerkung) return;

    // Walter-Vorgabe 19.05.2026: Pflicht-Bestätigung dass der DTA bei der
    // Bank gelöscht wurde, sonst läuft die Zahlung doppelt. Backend prüft
    // zusätzlich dass das Zahldatum DTA noch nicht erreicht ist — nach dem
    // Datum ist auch der Admin-Reset gesperrt.
    if (!confirm(
        'ACHTUNG: Definitiv abgeschlossene Lohnperiode wieder eröffnen.\n\n' +
        'Hast du den DTA bei der Bank gelöscht oder storniert?\n\n' +
        '✓ JA → Periode wird auf "provisorisch_abgeschlossen" zurückgerollt,\n' +
        '       Lohnzettel aus MA-Postfächern entfernt.\n' +
        '✗ NEIN → Vorgang abbrechen.\n\n' +
        'Diese Operation ist NACH dem Zahldatum DTA gesperrt.'
    )) return;

    try {
        const r = await fetch(`/api/payroll-perioden/${_llCurrentPeriodeId}/wieder-oeffnen`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId: currentUser?.id ?? 0, bemerkung })
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            if (j.error === 'PAYOUT_DATE_REACHED') {
                alert('⛔ ' + j.message);
                return;
            }
            throw new Error(j.message || 'Fehler');
        }
        showToast(_t('ll.reopen.toast'), 'success');
        await llLoadStatus();
    } catch (e) { alert(e.message); }
}

// ══════════════════════════════════════════════════════════════════════
let _svAllEmployees       = [];   // alle aktiven MA inkl. hatVortrag-Flag
let _svCurrentEmployeeId  = null; // aktuell im Modal bearbeiteter MA

async function svInit() {
    // Saldi-Vortrag folgt dem globalen Filial-Selektor (Sidebar oben links).
    // Filial-Banner anzeigen damit der User weiss auf welche Filiale sich
    // die Liste bezieht.
    const banner = document.getElementById('svBranchBanner');
    if (banner) {
        if (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId
            && typeof allBranches !== 'undefined' && Array.isArray(allBranches)) {
            const b = allBranches.find(x => x.id === fixedCompanyProfileId);
            if (b) {
                const code = b.restaurantCode ? '#' + b.restaurantCode + ' · ' : '';
                const bn   = b.branchName || b.companyName || '–';
                banner.innerHTML = `<b>Filiale:</b> ${code}${bn} <span style="color:#94a3b8">— wird aus dem Hauptmenü übernommen</span>`;
            } else {
                banner.innerHTML = '<b>Alle Filialen</b>';
            }
        } else {
            banner.innerHTML = '<b>Alle Filialen</b>';
        }
    }
    await svRefreshList();
}

async function svRefreshList() {
    // Filial-Filter kommt aus dem globalen Sidebar-Selektor (fixedCompanyProfileId).
    const filiale = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                      ? String(fixedCompanyProfileId) : '';
    const url = filiale
        ? `/api/saldo-vortrag?companyProfileId=${filiale}`
        : '/api/saldo-vortrag';
    try {
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        _svAllEmployees = await r.json();
        svRenderList();
    } catch (err) {
        document.getElementById('svList').innerHTML =
            `<div style="padding:20px;color:#dc2626;font-size:13px">Fehler beim Laden: ${err.message}</div>`;
    }
}

function svRenderList() {
    const q = (document.getElementById('svSearch')?.value || '').toLowerCase();
    const filtered = q
        ? _svAllEmployees.filter(e => {
            const name = `${e.firstName} ${e.lastName}`.toLowerCase();
            return name.includes(q) || (e.employeeNumber || '').toLowerCase().includes(q);
        })
        : _svAllEmployees;

    // IMMER nach Vorname sortieren (Konvention im System)
    const sorted = [...filtered].sort((a, b) =>
        (a.firstName || '').localeCompare(b.firstName || '') ||
        (a.lastName  || '').localeCompare(b.lastName  || ''));

    const countEl = document.getElementById('svListCount');
    if (countEl) {
        const total = _svAllEmployees.length;
        const mit   = _svAllEmployees.filter(e => e.hatVortrag).length;
        countEl.textContent = `${sorted.length} von ${total} · ${mit} mit Vortrag`;
    }

    const list = document.getElementById('svList');
    if (sorted.length === 0) {
        list.innerHTML = '<div style="padding:24px;text-align:center;color:#94a3b8;font-size:13px">Keine Mitarbeiter gefunden.</div>';
        return;
    }

    const fmtDate = (iso) => {
        if (!iso) return '–';
        try { return new Date(iso).toLocaleDateString('de-CH'); } catch { return '–'; }
    };

    list.innerHTML = sorted.map(e => {
        const initials = ((e.firstName || '')[0] || '') + ((e.lastName || '')[0] || '');
        const status = e.hatVortrag
            ? `<span class="sv-status-ok">✓ erfasst ${fmtDate(e.erfasstAm)}</span>`
            : `<span class="sv-status-open">⚠ noch offen</span>`;
        // Vertragstyp-Badge (nutzt gleichen Stil wie im Modal-Header)
        const model = (e.employmentModel || '').toUpperCase();
        const badgeClass = model === 'FIX-M' ? 'fix-m'
                         : model === 'UTP'   ? 'utp'
                         : model === 'MTP'   ? 'mtp'
                         : model === 'FIX'   ? 'fix'
                         : 'none';
        const badgeLabel = model || '–';
        const vertragBadge = `<span class="sv-vertrag-badge ${badgeClass}">${badgeLabel}</span>`;
        return `<div class="sv-list-row" onclick="svOpenModal(${e.id})">
            <div class="sv-avatar">${initials.toUpperCase()}</div>
            <div class="sv-row-text">
                <div class="sv-row-name">${e.firstName || ''} ${e.lastName || ''}</div>
                <div class="sv-row-nr">${e.employeeNumber || ''}</div>
            </div>
            ${vertragBadge}
            ${status}
        </div>`;
    }).join('');
}

async function svLoadPeriodenForBranch(companyProfileId) {
    const sel = document.getElementById('svPeriode');
    const hint = document.getElementById('svPeriodeHint');
    if (!companyProfileId) {
        sel.innerHTML = '<option value="">— keine Filiale zugeordnet —</option>';
        if (hint) hint.innerHTML = '⚠ Mitarbeiter hat keinen aktiven Vertrag mit Filiale. Vortrag kann nicht erfasst werden.';
        return [];
    }
    sel.innerHTML = '<option value="">— Lade Perioden… —</option>';
    try {
        const r = await fetch(`/api/payroll-perioden?companyProfileId=${companyProfileId}`, { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const perioden = await r.json();

        if (!perioden.length) {
            sel.innerHTML = '<option value="">— keine Perioden vorhanden —</option>';
            if (hint) hint.innerHTML = `⚠ Für diese Filiale sind noch keine Lohnperioden erfasst. Bitte erst unter <b>Lohnperioden</b> anlegen.`;
            return [];
        }

        // Format: "Mai 2026 · 21.04.26–20.05.26"
        const fmtDate = (iso) => {
            try {
                const d = new Date(iso);
                return `${String(d.getDate()).padStart(2,'0')}.${String(d.getMonth()+1).padStart(2,'0')}.${String(d.getFullYear()).slice(2)}`;
            } catch { return iso; }
        };

        // value = "YYYY-MM" damit's mit der LohnZulage.Periode-Spalte konsistent ist.
        // Wir nehmen Year+Month der Periode (nicht das Datum von PeriodFrom).
        const opts = perioden.map(p => {
            const yyyymm = `${p.year}-${String(p.month).padStart(2,'0')}`;
            const label  = `${p.label || (p.year + '-' + p.month)} · ${fmtDate(p.periodFrom)}–${fmtDate(p.periodTo)}`;
            return `<option value="${yyyymm}">${label}</option>`;
        });
        sel.innerHTML = opts.join('');

        // Default-Auswahl: älteste noch offene Periode im aktuellen Kalender-
        // jahr — das ist typischerweise die "Einstiegs-Periode" für die
        // Migration. Fallback wenn keine offene Periode im aktuellen Jahr:
        // jüngste Periode (= bisheriges Default-Verhalten der DESC-Liste).
        const currentYear = new Date().getFullYear();
        const offen = perioden
            .filter(p => p.status === 'offen' && p.year === currentYear)
            .sort((a, b) => a.month - b.month);
        if (offen.length > 0) {
            const first = offen[0];
            sel.value = `${first.year}-${String(first.month).padStart(2,'0')}`;
        }

        if (hint) hint.textContent = 'In dieser Lohnperiode erscheinen die Vortrag-Buchungen.';
        return perioden;
    } catch (err) {
        sel.innerHTML = '<option value="">— Fehler beim Laden —</option>';
        if (hint) hint.innerHTML = `⚠ Fehler: ${err.message}`;
        return [];
    }
}

// Welche Saldi sind pro Vertragstyp relevant (= im Vorsystem geführt)?
// UTP zahlt Feiertag/13. monatlich aus, daher keine Saldi.
// FIX/FIX-M haben das Ferien-Geld im Festlohn drin.
// Nacht-Saldo: nur MTP/FIX/FIX-M (UTP trackt keine Stunden).
const SV_FIELD_RELEVANCE = {
    'UTP':   { zeit: false, feiertag: false, ferien: true, nacht: false, feriengeld: true,  dreizehnter: false },
    'MTP':   { zeit: true,  feiertag: false, ferien: true, nacht: true,  feriengeld: true,  dreizehnter: true  },
    'FIX':   { zeit: true,  feiertag: true,  ferien: true, nacht: true,  feriengeld: false, dreizehnter: true  },
    'FIX-M': { zeit: true,  feiertag: true,  ferien: true, nacht: true,  feriengeld: false, dreizehnter: true  }
};

function svApplyVertragHighlighting(model) {
    const rules = SV_FIELD_RELEVANCE[model] || {};
    document.querySelectorAll('#svFieldsGrid .sv-field').forEach(f => {
        const key = f.dataset.saldo;
        const relevant = rules[key];
        f.classList.remove('sv-active', 'sv-inactive');
        // Wenn Vertragstyp unbekannt: alle Felder neutral lassen
        if (relevant === undefined) return;
        f.classList.add(relevant ? 'sv-active' : 'sv-inactive');
    });
}

async function svOpenModal(employeeId) {
    _svCurrentEmployeeId = employeeId;
    const emp = _svAllEmployees.find(e => e.id === employeeId);
    if (!emp) return;

    // Header-Subtitle inkl. Vertragstyp-Badge
    const model = (emp.employmentModel || '').toUpperCase();
    const badgeClass = model === 'FIX-M' ? 'fix-m'
                     : model === 'UTP'   ? 'utp'
                     : model === 'MTP'   ? 'mtp'
                     : model === 'FIX'   ? 'fix'
                     : 'none';
    const badgeLabel = model || 'kein Vertrag';
    document.getElementById('svModalSubtitle').innerHTML =
        `${emp.firstName} ${emp.lastName} · Personal-Nr. ${emp.employeeNumber || '–'}` +
        `<span class="sv-vertrag-badge ${badgeClass}">${badgeLabel}</span>`;

    // Felder gemäss Vertragstyp hervorheben/ausgrauen
    svApplyVertragHighlighting(model);

    document.getElementById('svModalAlert').innerHTML = '';
    document.getElementById('svVortragModal').style.display = 'block';

    // Lohnperioden der MA-Filiale parallel laden
    const periodenPromise = svLoadPeriodenForBranch(emp.primaryCompanyProfileId);

    // Bestehenden Vortrag laden (wenn vorhanden)
    try {
        const [r, _] = await Promise.all([
            fetch(`/api/saldo-vortrag/${employeeId}`, { headers: ah() }),
            periodenPromise
        ]);
        const data = r.ok ? await r.json() : null;
        const sel  = document.getElementById('svPeriode');

        if (data?.exists) {
            // Periode setzen (falls die Periode in der Filial-Liste vorhanden ist)
            if (data.periode) {
                const found = Array.from(sel.options).find(o => o.value === data.periode);
                if (found) {
                    sel.value = data.periode;
                } else {
                    // Periode nicht in der Liste — als Sonder-Option oben einfügen
                    const opt = document.createElement('option');
                    opt.value = data.periode;
                    opt.textContent = `${data.periode} (frühere Erfassung)`;
                    sel.insertBefore(opt, sel.firstChild);
                    sel.value = data.periode;
                }
            }
            document.getElementById('svZeitSaldo').value         = data.zeitSaldoH ?? 0;
            document.getElementById('svFeiertagSaldo').value     = data.feiertagSaldoH ?? 0;
            document.getElementById('svFerienTageSaldo').value   = data.ferienSaldoTage ?? 0;
            document.getElementById('svNachtSaldo').value        = data.nachtSaldoH ?? 0;
            document.getElementById('svFerienGeldSaldo').value   = data.ferienGeldSaldoChf ?? 0;
            document.getElementById('svDreizehnterSaldo').value  = data.dreizehnterSaldoChf ?? 0;
            document.getElementById('svDeleteBtn').style.display = 'inline-flex';
        } else {
            // Default: erste verfügbare Periode (= aktuelle/jüngste, weil Liste DESC sortiert ist)
            // ist bereits durch svLoadPeriodenForBranch gesetzt; alle Werte leer.
            document.getElementById('svZeitSaldo').value         = '';
            document.getElementById('svFeiertagSaldo').value     = '';
            document.getElementById('svFerienTageSaldo').value   = '';
            document.getElementById('svNachtSaldo').value        = '';
            document.getElementById('svFerienGeldSaldo').value   = '';
            document.getElementById('svDreizehnterSaldo').value  = '';
            document.getElementById('svDeleteBtn').style.display = 'none';
        }
    } catch (err) {
        document.getElementById('svModalAlert').innerHTML =
            `<div style="padding:8px 12px;background:#fef2f2;border:1px solid #fecaca;color:#dc2626;border-radius:8px;font-size:12.5px;margin-bottom:12px">Fehler beim Laden: ${err.message}</div>`;
    }
}

function svModalClose() {
    document.getElementById('svVortragModal').style.display = 'none';
    _svCurrentEmployeeId = null;
}

async function svSaveVortrag() {
    if (!_svCurrentEmployeeId) return;

    const periode = document.getElementById('svPeriode').value;
    if (!/^\d{4}-\d{2}$/.test(periode)) {
        svShowAlert('Bitte eine Lohnperiode auswählen. Falls die Filiale noch keine Perioden hat, müssen diese zuerst unter "Lohnperioden" angelegt werden.', 'err');
        return;
    }

    const num = (id) => {
        const v = parseFloat(document.getElementById(id).value);
        return Number.isFinite(v) ? v : 0;
    };

    const dto = {
        periode,
        zeitSaldoH:           num('svZeitSaldo'),
        feiertagSaldoH:       num('svFeiertagSaldo'),
        ferienSaldoTage:      num('svFerienTageSaldo'),
        nachtSaldoH:          num('svNachtSaldo'),
        ferienGeldSaldoChf:   num('svFerienGeldSaldo'),
        dreizehnterSaldoChf:  num('svDreizehnterSaldo')
    };

    try {
        const r = await fetch(`/api/saldo-vortrag/${_svCurrentEmployeeId}`, {
            method:  'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body:    JSON.stringify(dto)
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(r)) return;
        if (!r.ok) {
            const txt = await r.text();
            throw new Error(txt || ('HTTP ' + r.status));
        }
        svShowAlert('Vortrag gespeichert.', 'ok');
        await svRefreshList();
        setTimeout(svModalClose, 800);
    } catch (err) {
        svShowAlert('Fehler: ' + err.message, 'err');
    }
}

async function svDeleteVortrag() {
    if (!_svCurrentEmployeeId) return;
    if (!confirm('Vortrag-Einträge dieses Mitarbeiters wirklich entfernen? Die Saldi starten danach wieder bei 0.')) return;

    try {
        const r = await fetch(`/api/saldo-vortrag/${_svCurrentEmployeeId}`, {
            method: 'DELETE', headers: ah()
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(r)) return;
        if (!r.ok) throw new Error('HTTP ' + r.status);
        svShowAlert('Vortrag entfernt.', 'ok');
        await svRefreshList();
        setTimeout(svModalClose, 600);
    } catch (err) {
        svShowAlert('Fehler beim Löschen: ' + err.message, 'err');
    }
}

function svShowAlert(msg, kind) {
    const el = document.getElementById('svModalAlert');
    if (!el) return;
    const bg = kind === 'ok' ? '#dcfce7' : '#fef2f2';
    const bd = kind === 'ok' ? '#bbf7d0' : '#fecaca';
    const fg = kind === 'ok' ? '#15803d' : '#dc2626';
    el.innerHTML = `<div style="padding:8px 12px;background:${bg};border:1px solid ${bd};color:${fg};border-radius:8px;font-size:12.5px;margin-bottom:12px">${msg}</div>`;
}

