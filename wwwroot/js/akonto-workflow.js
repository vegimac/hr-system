// ══════════════════════════════════════════════════════════════════════
// akonto-workflow.js — Akonto-Lohn 4-Augen-Workflow (Etappe 3)
// ══════════════════════════════════════════════════════════════════════
// Modus-Schalter im Lohn-Modul: Definitiv (heutige Logik) | Akonto.
// Im Akonto-Modus läuft der vereinbarte 4-Augen-Workflow:
//   GF: Akonto vorbereiten → pro Lohnblatt freigeben → an HR senden
//   HR: kontrollieren → ggf. mit Notiz zurück an GF → Final-Freigabe → Auszahlen
// API-Endpoints: /api/akonto/workflow/* (siehe AkontoWorkflowController).

// Mode wird in localStorage persistiert (Walter 16.05.2026), damit ein
// Page-Reload nicht zurück auf Definitivlauf springt. Default = 'akonto':
// Akonto ist chronologisch zuerst dran (Mitte Monat) und Walter macht ihn
// häufiger als den Definitivlauf.
const _LOHN_MODE_KEY = 'hrLohnMode';
function _loadPersistedLohnMode() {
    try {
        const m = localStorage.getItem(_LOHN_MODE_KEY);
        return (m === 'akonto' || m === 'definitiv') ? m : 'akonto';
    } catch { return 'akonto'; }
}
let _akWfMode = _loadPersistedLohnMode();    // 'definitiv' | 'akonto'
let _akWfData = null;           // /status-Response-Cache
let _akWfSelectedId = null;     // aktuell ausgewähltes akonto_zahlung.id im Detail
let _akWfEmpMap = null;         // empId → vollständiges Employee-Objekt (für Modell-Badge + QST-Button im MA-Listen-Render — analog loadLohnList in payroll.js)

// Bedeutung der Status-Werte (Doppelmoppel zur Anzeige im UI)
const _AK_STATUS = {
    OFFEN:             { label: 'Offen',                color: '#94a3b8', bg: '#f1f5f9' },
    IN_BEARBEITUNG_GF: { label: 'In Bearbeitung (GF)',  color: '#92400e', bg: '#fef3c7' },
    BEI_HR:            { label: 'Bei HR',               color: '#1e40af', bg: '#dbeafe' },
    HR_FREIGEGEBEN:    { label: 'HR freigegeben',       color: '#15803d', bg: '#dcfce7' },
    AUSBEZAHLT:        { label: 'Ausbezahlt',           color: '#7c2d12', bg: '#fed7aa' },
};
const _AK_BLATT_STATUS = {
    BERECHNET:      { label: 'berechnet',         color: '#92400e', bg: '#fef3c7' },
    FREIGEGEBEN_GF: { label: '✓ GF freigegeben',  color: '#15803d', bg: '#dcfce7' },
    AUSBEZAHLT:     { label: 'ausbezahlt',        color: '#7c2d12', bg: '#fed7aa' },
    STORNIERT:      { label: 'storniert',         color: '#b91c1c', bg: '#fee2e2' },
};

function _akFmtChf(n) {
    if (n == null || isNaN(n)) return '–';
    return `CHF ${Number(n).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}
function _akFmtDate(iso) {
    if (!iso) return '–';
    const p = String(iso).slice(0, 10).split('-');
    return p.length === 3 ? `${p[2]}.${p[1]}.${p[0]}` : iso;
}
function _akFmtTs(ts) {
    if (!ts) return '–';
    const d = new Date(ts);
    if (isNaN(d)) return ts;
    return d.toLocaleString('de-CH', { day:'2-digit', month:'2-digit', year:'numeric', hour:'2-digit', minute:'2-digit' });
}

// ── Rolle ermitteln (für Sichtbarkeit der HR-Buttons) ─────────────────────
function _akIsHr() {
    const r = (typeof currentUser !== 'undefined' && currentUser?.role) ? currentUser.role : '';
    return r === 'admin' || r === 'superuser';
}

// ── Modus-Schalter ────────────────────────────────────────────────────────
function setLohnMode(mode) {
    if (mode !== 'definitiv' && mode !== 'akonto') return;
    _akWfMode = mode;
    try { localStorage.setItem(_LOHN_MODE_KEY, mode); } catch {}
    _akWfUpdateModeButtons();
    const defView  = document.getElementById('lohnDefinitivView');
    const akView   = document.getElementById('lohnAkontoView');
    const hint     = document.getElementById('lohnModeHint');
    const topDef   = document.getElementById('lohnTopActions');
    const topAk    = document.getElementById('lohnTopActionsAkonto');
    const perBanner = document.getElementById('lohnPeriodBanner');   // Definitiv-spezifisch
    // Body-Klasse `lohn-mode-akonto` aktiviert die CSS-Regel die Definitivlauf-
    // Elemente hart versteckt — das überlebt jeden späteren `style.display='block'`
    // aus payroll.js (loadLohnSlip / loadLohnPeriodBanner laufen async parallel).
    document.body.classList.toggle('lohn-mode-akonto', mode === 'akonto');
    if (mode === 'akonto') {
        if (defView) defView.style.display = 'none';
        if (akView)  akView.style.display  = '';
        if (hint)    hint.textContent      = '';
        if (perBanner) perBanner.style.display = 'none';   // im Akonto-Modus keine Definitiv-"1/44 bestätigt"-Pille
        if (topDef) topDef.style.display = 'none';
        // Walter 16.05.2026: Top-Bar #lohnTopActionsAkonto bewusst still — alle
        // Akonto-Aktionen (per-MA + Periode) sind im akontoStatusBar konsolidiert.
        if (topAk)  topAk.style.display  = 'none';
        akWfRefresh();
    } else {
        if (defView) defView.style.display = '';
        if (akView)  akView.style.display  = 'none';
        if (hint)    hint.textContent      = '';
        if (topAk)  topAk.style.display  = 'none';
        // topDef + perBanner werden vom loadLohnSlip/loadLohnPeriodBanner-Flow
        // im Definitivlauf wieder aktiviert.
    }
    _akWfUpdateTopActions();
    _checkDefinitivLock();
}

// Top-Bar-Buttons (#lohnTopActionsAkonto) wurden konsolidiert in die
// akontoStatusBar (_akWfRenderStatusBar). Diese Funktion bleibt als No-Op,
// damit alte Aufrufer keine Fehler werfen — Walter 16.05.2026.
function _akWfUpdateTopActions() {
    const btnFrei  = document.getElementById('btnAkontoFreigeben');
    const btnZurueck = document.getElementById('btnAkontoZurueckziehen');
    if (btnFrei)   btnFrei.style.display   = 'none';
    if (btnZurueck) btnZurueck.style.display = 'none';
}

// Aktion für den prominenten Top-Bar-Button — nutzt das aktuell ausgewählte
// Akonto-Lohnblatt. doFreigeben=true → freigeben, false → Freigabe zurückziehen.
function akWfFreigabeAktuell(doFreigeben) {
    const id = _akWfSelectedId;
    if (!id) { alert('Bitte links einen Mitarbeiter wählen.'); return; }
    if (doFreigeben) akWfFreigeben(id);
    else             akWfZurueckziehen(id);
}

// Aktualisieren-Button im Top-Bar — ruft je nach Modus den richtigen Pfad.
function lohnTopRefresh() {
    if (_akWfMode === 'akonto') {
        akWfRefresh();
    } else if (typeof loadLohnSlipFromPanel === 'function') {
        loadLohnSlipFromPanel();
    }
    _checkDefinitivLock();
}

// Walter 16.05.2026: beim Aufruf Lohnverwaltung soll automatisch der richtige
// Modus aktiv sein. Logik:
//   • Akonto-Lauf der aktuellen Periode noch nicht AUSBEZAHLT → Akonto-Modus
//   • Akonto-Lauf AUSBEZAHLT (oder kein Akonto-Termin = OFFEN) → Definitiv-Modus
// Nutzt die /status-Antwort für die aktuell in den Selects gewählte Periode.
// Fallback bei Fehler / fehlenden Daten: persistierte Wahl (Default 'akonto').
async function _autoSelectLohnMode() {
    const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) || null;
    const year     = parseInt(document.getElementById('lohnYearSelect')?.value, 10);
    const month    = parseInt(document.getElementById('lohnMonthSelect')?.value, 10);
    let mode = _akWfMode || 'akonto';
    if (branchId && year && month) {
        try {
            const r = await fetch(`/api/akonto/workflow/status?companyProfileId=${branchId}&year=${year}&month=${month}&_=${Date.now()}`,
                                  { headers: ah(), cache: 'no-store' });
            if (r.ok) {
                const d = await r.json();
                mode = (d.akontoStatus === 'AUSBEZAHLT') ? 'definitiv' : 'akonto';
            }
        } catch { /* Fallback bleibt _akWfMode */ }
    }
    setLohnMode(mode);
}

// ── Definitiv-Lock (Walter 16.05.2026) ─────────────────────────────────────
// Walter: "den definitiven lohnlauf erst bearbeitbar machen, wenn der akonto
// lohnlauf durch ist". Sobald der User auf Definitivlauf wechselt oder die
// Periode ändert, fragen wir den Akonto-Status für genau diese Periode +
// Filiale ab. Solange AkontoStatus != AUSBEZAHLT:
//   • prominent gelber Banner mit Erklärung + "Zu Akonto wechseln"-Button
//   • Definitiv-Top-Action-Buttons (PDF / Bestätigen / Reopen) ausgeblendet
//   • Hint-Text im linken Slip-Vertragspanel bleibt sichtbar (nur Anzeige)
//   • "Lohn bestätigen" wäre Backend-seitig durch zukünftigen Guard ebenfalls
//     geschützt — Frontend-Lock ist die erste Verteidigungslinie.
async function _checkDefinitivLock() {
    const banner = document.getElementById('lohnDefinitivLockBanner');
    const topDef = document.getElementById('lohnTopActions');
    if (!banner) return;

    // Im Akonto-Modus immer ausblenden — Lock greift nur im Definitivlauf.
    if (_akWfMode !== 'definitiv') {
        banner.style.display = 'none';
        return;
    }

    const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) || null;
    const year     = parseInt(document.getElementById('lohnYearSelect')?.value, 10);
    const month    = parseInt(document.getElementById('lohnMonthSelect')?.value, 10);
    if (!branchId || !year || !month) {
        banner.style.display = 'none';
        return;
    }
    try {
        const ts = Date.now();
        const r = await fetch(`/api/akonto/workflow/status?companyProfileId=${branchId}&year=${year}&month=${month}&_=${ts}`,
                              { headers: ah(), cache: 'no-store' });
        if (!r.ok) { banner.style.display = 'none'; return; }
        const d = await r.json();
        // OFFEN = Akonto wurde NIE gestartet (Walter überspringt den Workflow
        // bewusst). AUSBEZAHLT = Akonto durch. Beide erlauben Definitivlauf.
        // Nur die Zwischenstati IN_BEARBEITUNG_GF / BEI_HR / HR_FREIGEGEBEN
        // blockieren — der Akonto-Lauf läuft gerade und sein Betrag könnte
        // sich noch ändern.
        const akontoFertig = d.akontoStatus === 'AUSBEZAHLT' || d.akontoStatus === 'OFFEN';
        if (akontoFertig) {
            banner.style.display = 'none';
            // topDef-Sichtbarkeit überlassen wir loadLohnSlip (zeigt sich beim Slip-Render)
            return;
        }
        // Locked → Banner zeigen, Top-Action-Buttons hart verstecken
        const months = ['', 'Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
        const statusLabel = (_AK_STATUS[d.akontoStatus] || _AK_STATUS.OFFEN).label;
        banner.style.display = '';
        banner.innerHTML = `
            <div style="padding:14px 18px;background:#fef3c7;border:1px solid #fbbf24;border-radius:8px;display:flex;align-items:center;gap:14px;font-size:13.5px;color:#78350f">
                <span style="font-size:22px">🔒</span>
                <div style="flex:1;line-height:1.45">
                    <b>Definitivlauf für ${months[month]} ${year} ist gesperrt.</b><br>
                    Akonto-Lauf hat den Status <b>${statusLabel}</b> — er muss zuerst <b>AUSBEZAHLT</b> sein,
                    bevor der Definitivlohn bestätigt werden kann (sonst stimmt die Restzahlungs-Berechnung nicht).
                </div>
                <button class="btn btn-primary" onclick="setLohnMode('akonto')">→ Zu Akonto wechseln</button>
            </div>`;
        if (topDef) topDef.style.display = 'none';
    } catch {
        banner.style.display = 'none';
    }
}

function _akWfUpdateModeButtons() {
    const def = document.getElementById('lohnModeDefinitivBtn');
    const ak  = document.getElementById('lohnModeAkontoBtn');
    if (!def || !ak) return;
    const active   = 'background:#1d4ed8;color:white;border-color:#1d4ed8';
    const inactive = 'background:white;color:#475569;border-color:#cbd5e1';
    const base     = 'padding:7px 14px;border:1px solid;font-size:13px;font-weight:600;cursor:pointer';
    // Akonto links, Definitiv rechts (Walter-Vorgabe 16.05.2026 — Akonto ist der häufigere Lauf).
    ak.setAttribute('style',  `${base};border-radius:7px 0 0 7px;margin-right:-1px;${_akWfMode === 'akonto' ? active : inactive}`);
    def.setAttribute('style', `${base};border-radius:0 7px 7px 0;${_akWfMode === 'definitiv' ? active : inactive}`);
}

// Wird vom showPage('lohn') aufgerufen + von onBranchChange / Period-Change.
function akWfOnPageOrBranchChange() {
    _akWfUpdateModeButtons();
    // Bei Page-Open / Filial-Wechsel: auf älteste offene Periode springen
    // (Walter-Vorgabe 16.05.2026 — keine Lücken). Asynchron, blockiert nichts.
    setTimeout(() => lohnSyncToOldestOpen(/*autoJump*/ true), 50);
    if (_akWfMode === 'akonto') akWfRefresh();
    _checkDefinitivLock();
}

// Periode-Selects feuern auch im Akonto-Modus + Banner-Update bei jeder
// Period-Änderung (mode-agnostisch — die Sequenz-Pflicht gilt für beide Stränge).
function _akWfInstallPeriodListeners() {
    const m = document.getElementById('lohnMonthSelect');
    const y = document.getElementById('lohnYearSelect');
    if (m && !m.dataset.akWfHooked) {
        m.dataset.akWfHooked = '1';
        m.addEventListener('change', () => {
            lohnSyncToOldestOpen(/*autoJump*/ false);
            if (_akWfMode === 'akonto') akWfRefresh();
            _checkDefinitivLock();
        });
    }
    if (y && !y.dataset.akWfHooked) {
        y.dataset.akWfHooked = '1';
        y.addEventListener('change', () => {
            lohnSyncToOldestOpen(/*autoJump*/ false);
            if (_akWfMode === 'akonto') akWfRefresh();
            _checkDefinitivLock();
        });
    }
}

// ── Sequenz-Banner + Auto-Sprung auf älteste offene Periode ────────────────
// Holt von /api/akonto/workflow/oldest-open-period die älteste noch nicht
// komplett abgeschlossene Periode der Filiale. Bei autoJump=true (Page-Open
// oder Filial-Wechsel) wird die Auswahl automatisch dorthin gesetzt. Sonst
// (User hat manuell gewechselt) nur Banner aktualisieren — Walter kann
// bewusst eine spätere Periode anschauen, Aktionen werden vom Backend aber
// blockiert.
let _lohnSyncInFlight = false;
async function lohnSyncToOldestOpen(autoJump) {
    if (_lohnSyncInFlight) return;
    _lohnSyncInFlight = true;
    try {
        const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                           ? fixedCompanyProfileId : null;
        const banner = document.getElementById('lohnSequenceBanner');
        if (!banner) return;
        if (!branchId) { banner.style.display = 'none'; return; }

        let oldest = null;
        try {
            const r = await fetch(`/api/akonto/workflow/oldest-open-period?companyProfileId=${branchId}`,
                                  { headers: ah() });
            if (r.ok) {
                const txt = await r.text();
                if (txt && txt.trim() && txt.trim() !== 'null') oldest = JSON.parse(txt);
            }
        } catch {}

        if (!oldest) {
            // Alles abgeschlossen oder keine Periode → kein Banner.
            banner.style.display = 'none';
            return;
        }

        const yInp = document.getElementById('lohnYearSelect');
        const mInp = document.getElementById('lohnMonthSelect');
        if (!yInp || !mInp) return;

        const months = ['', 'Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
        const offen = [];
        if (oldest.akontoStatus    !== 'AUSBEZAHLT')    offen.push('Akonto');
        if (oldest.definitivStatus !== 'abgeschlossen') offen.push('Definitivlauf');

        const curY = parseInt(yInp.value, 10) || 0;
        const curM = parseInt(mInp.value, 10) || 0;
        const curRef    = curY * 12 + curM;
        const oldestRef = oldest.year * 12 + oldest.month;

        if (autoJump && curRef !== oldestRef) {
            // Hart auf älteste offene springen — verhindert dass Walter eine
            // spätere Periode aus Versehen wählt.
            yInp.value = String(oldest.year);
            mInp.value = String(oldest.month);
            // Andere Listener (existing loadLohnSlipFromPanel + meine im
            // mode=akonto) feuern bei dispatchEvent — Banner-Update via
            // erneutem lohnSyncToOldestOpen kommt von dort.
            _lohnSyncInFlight = false;   // freigeben damit Re-Entry möglich
            mInp.dispatchEvent(new Event('change'));
            return;
        }

        if (curRef > oldestRef) {
            // User hat eine spätere Periode gewählt — Banner-Warnung.
            banner.style.display = '';
            banner.innerHTML = `
                <div style="padding:12px 16px;background:#fef3c7;border:1px solid #fde68a;border-radius:8px;display:flex;align-items:center;gap:12px;font-size:13px;color:#78350f">
                    <span style="font-size:18px">⚠</span>
                    <div style="flex:1">
                        <b>Periode ${months[oldest.month]} ${oldest.year}</b> ist noch nicht abgeschlossen
                        (${offen.join(' + ')} steht aus). Aktionen in späteren Perioden werden vom System
                        blockiert — bitte zuerst diese Periode fertigstellen.
                    </div>
                    <button class="btn btn-outline" onclick="lohnJumpToOldestOpen(${oldest.year},${oldest.month})">→ Zu ${months[oldest.month]} ${oldest.year}</button>
                </div>`;
        } else {
            // Aktuelle = älteste offene → kein Banner nötig.
            banner.style.display = 'none';
        }
    } finally {
        _lohnSyncInFlight = false;
    }
}

function lohnJumpToOldestOpen(year, month) {
    const yInp = document.getElementById('lohnYearSelect');
    const mInp = document.getElementById('lohnMonthSelect');
    if (!yInp || !mInp) return;
    yInp.value = String(year);
    mInp.value = String(month);
    mInp.dispatchEvent(new Event('change'));
}

// ── Hauptlader: holt /status, rendert Statusbar + MA-Liste ────────────────
let _akWfRefreshRetries = 0;
async function akWfRefresh() {
    _akWfInstallPeriodListeners();
    const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                       ? fixedCompanyProfileId : null;
    const year  = parseInt(document.getElementById('lohnYearSelect')?.value, 10);
    const month = parseInt(document.getElementById('lohnMonthSelect')?.value, 10);
    const bar   = document.getElementById('akontoStatusBar');
    const list  = document.getElementById('akontoMaList');
    if (!branchId || !year || !month) {
        // Walter-Bug-Fix 16.05.2026: Selects sind beim allerersten akWfRefresh
        // nach showPage('lohn') manchmal noch nicht befüllt (initLohnPage läuft
        // async). Statt aufzugeben max. 5x mit 300ms Abstand erneut versuchen,
        // damit die MA-Liste nicht ewig auf "Filiale + Periode wählen" stehen
        // bleibt obwohl in der Toolbar Periode + Filiale gewählt sind.
        if (_akWfMode === 'akonto' && _akWfRefreshRetries < 5) {
            _akWfRefreshRetries++;
            setTimeout(() => akWfRefresh(), 300);
            return;
        }
        if (bar)  bar.innerHTML  = '';
        if (list) list.innerHTML = `<div style="padding:28px;text-align:center;color:#94a3b8;font-size:13px">Filiale + Periode wählen</div>`;
        _akAusEmpty();
        return;
    }
    _akWfRefreshRetries = 0;   // Erfolg → Retry-Counter zurücksetzen
    try {
        // Status + vollständige MA-Liste parallel laden. /api/employees brauchen
        // wir, damit die Akonto-MA-Liste den gleichen Look wie der Definitivlauf
        // bekommt (Modell-Badge MTP/UTP/FIX/FIX-M, QST-Button mit allen Modal-
        // Argumenten). Die Akonto-Status-Response selbst liefert nur Name/Nr/Adresse.
        // Cache-Buster + cache:no-store: nach einer Freigabe MUSS der Browser
        // die frische Status-Antwort holen, sonst bleibt der MA visuell auf
        // "berechnet" hängen obwohl der DB-Status schon FREIGEGEBEN_GF ist
        // (Walter-Bug 16.05.2026).
        const ts = Date.now();
        const [r, rEmp] = await Promise.all([
            fetch(`/api/akonto/workflow/status?companyProfileId=${branchId}&year=${year}&month=${month}&_=${ts}`,
                  { headers: ah(), cache: 'no-store' }),
            fetch(`/api/employees`, { headers: ah() }),
        ]);
        if (!r.ok) {
            if (bar) bar.innerHTML = _akWfAlert('Fehler beim Laden des Akonto-Status (HTTP ' + r.status + ').', 'err');
            return;
        }
        _akWfData = await r.json();
        _akWfEmpMap = {};
        if (rEmp.ok) {
            try {
                const emps = await rEmp.json();
                (emps || []).forEach(e => { _akWfEmpMap[e.id] = e; });
            } catch {}
        }
        _akWfRenderStatusBar();
        _akWfRenderMaList();
        _akWfUpdateTopActions();
        // Auto-Select (Walter 16.05.2026): kein leeres Detail-Panel — wenn
        // noch nichts ausgewählt ODER die alte Selektion nach Periode-Wechsel
        // nicht mehr in der Liste ist, automatisch den ersten MA wählen und
        // sein Lohnblatt laden.
        const list = _akWfData.zahlungen || [];
        if (_akWfSelectedId) {
            const stillThere = list.find(z => z.id === _akWfSelectedId);
            if (stillThere)              akWfLoadDetail(_akWfSelectedId);
            else if (list.length > 0)    akWfSelectMa(list[0].id);
            else                          _akAusEmpty();
        } else if (list.length > 0) {
            akWfSelectMa(list[0].id);
        } else {
            _akAusEmpty();
        }
    } catch (e) {
        if (bar) bar.innerHTML = _akWfAlert('Verbindungsfehler: ' + e.message, 'err');
    }
}

function _akAusEmpty() {
    const card = document.getElementById('akontoDetailCard');
    const empty = document.getElementById('akontoDetailEmpty');
    if (card)  card.style.display  = 'none';
    if (empty) empty.style.display = '';
}

function _akWfAlert(msg, kind) {
    const colors = kind === 'err' ? ['#fee2e2', '#b91c1c']
                 : kind === 'ok'  ? ['#dcfce7', '#15803d']
                                  : ['#fef3c7', '#78350f'];
    return `<div style="padding:10px 14px;background:${colors[0]};color:${colors[1]};border-radius:7px;font-size:13px">${msg}</div>`;
}

// ── Status-Bar: zeigt Stufe + die nächsten Aktions-Buttons ────────────────
function _akWfRenderStatusBar() {
    const bar = document.getElementById('akontoStatusBar');
    if (!bar || !_akWfData) return;
    const d = _akWfData;
    const meta = _AK_STATUS[d.akontoStatus] || _AK_STATUS.OFFEN;
    const isHr = _akIsHr();
    const counts = `${d.countFreigegebenGf || 0}/${d.countTotal || 0} freigegeben`;

    // Aktionen je Status + Rolle — kompakte Inline-Buttons.
    // Walter 16.05.2026: ALLE Akonto-Buttons in dieser Zeile zusammen
    // (Periode-Aktionen + per-MA-Aktionen für den aktuell selektierten MA).
    const sel = (d.zahlungen || []).find(z => z.id === _akWfSelectedId);
    const perMaFreigeben = (d.akontoStatus === 'IN_BEARBEITUNG_GF' && sel?.status === 'BERECHNET')
        ? `<button class="btn btn-primary btn-sm" onclick="akWfFreigeben(${sel.id})">✓ Lohnblatt freigeben</button>` : '';
    const perMaZurueckziehen = (d.akontoStatus === 'IN_BEARBEITUNG_GF' && sel?.status === 'FREIGEGEBEN_GF')
        ? `<button class="btn btn-outline btn-sm" onclick="akWfZurueckziehen(${sel.id})" style="color:#b91c1c;border-color:#fecaca">↶ Freigabe zurückziehen</button>` : '';

    let actions = '';
    switch (d.akontoStatus) {
        case 'OFFEN':
            actions = `<button class="btn btn-primary btn-sm" onclick="akWfStart()">📅 Akonto vorbereiten</button>`;
            break;
        case 'IN_BEARBEITUNG_GF':
            // per-MA-Aktionen ZUERST (Walter klickt häufiger), dann Periode-Aktionen
            actions = `${perMaFreigeben}${perMaZurueckziehen}
                       <button class="btn btn-outline btn-sm" onclick="akWfStart()" title="Werte neu berechnen — freigegebene Blätter bleiben">↻ Neu berechnen</button>
                       <button class="btn btn-success btn-sm" onclick="akWfAnHrSenden()" ${(d.countFreigegebenGf || 0) < (d.countTotal || 0) ? 'disabled' : ''}>An HR senden →</button>`;
            break;
        case 'BEI_HR':
            if (isHr) {
                actions = `<button class="btn btn-outline btn-sm" onclick="akWfZurueckAnGf()" style="color:#b91c1c;border-color:#fecaca">↩ Zurück an GF</button>
                           <button class="btn btn-success btn-sm" onclick="akWfHrFreigabe()">✓ HR-Freigabe</button>`;
            } else {
                actions = `<span style="color:#94a3b8;font-size:11px;font-style:italic">wartet auf HR</span>`;
            }
            break;
        case 'HR_FREIGEGEBEN':
            if (isHr) {
                actions = `<button class="btn btn-primary btn-sm" onclick="akWfAuszahlen()" style="background:#7c2d12;border-color:#7c2d12">💰 Akonto auszahlen</button>`;
            } else {
                actions = `<span style="color:#94a3b8;font-size:11px;font-style:italic">wartet auf Auszahlung</span>`;
            }
            break;
        case 'AUSBEZAHLT':
            actions = `<span style="color:#7c2d12;font-size:11px;font-weight:600">ausbezahlt ${_akFmtTs(d.akontoAusbezahltAt)}</span>`;
            break;
    }

    // Walter 16.05.2026: alles in EINE Zeile, keine Card mehr, minimaler
    // vertikaler Footprint. Audit-Trail nur als title-Tooltip.
    const trail = [
        d.akontoGfStartedAt    ? `GF Start: ${_akFmtTs(d.akontoGfStartedAt)}` : null,
        d.akontoGfSentAt       ? `An HR: ${_akFmtTs(d.akontoGfSentAt)}`       : null,
        d.akontoHrFreigegebenAt? `HR-Freigabe: ${_akFmtTs(d.akontoHrFreigegebenAt)}` : null,
        d.akontoAusbezahltAt   ? `Ausbezahlt: ${_akFmtTs(d.akontoAusbezahltAt)}`    : null,
    ].filter(x => x).join('\n');

    bar.innerHTML = `
        <div title="${trail}" style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;padding:6px 4px;font-size:12px">
            <span style="background:${meta.bg};color:${meta.color};padding:2px 9px;border-radius:8px;font-weight:700;font-size:11px;white-space:nowrap">${meta.label}</span>
            ${d.countTotal > 0 ? `<span style="color:#64748b;white-space:nowrap">${counts}</span>` : ''}
            ${actions ? `<span style="display:inline-flex;gap:6px;flex-wrap:wrap;margin-left:auto">${actions}</span>` : ''}
        </div>`;
}

// ── MA-Liste links ───────────────────────────────────────────────────────
// Walter-Vorgabe 16.05.2026: Optik identisch zur Definitivlauf-MA-Liste
// (loadLohnList in payroll.js) — runder Avatar mit Initialen oder ✓-Marker,
// Name + Status-Zeile, farbiges Modell-Badge (MTP/UTP/FIX/FIX-M), QST-Button.
// Unterschied nur: statt "Lohn bestätigt" zeigen wir den Akonto-Status
// (BERECHNET / GF freigegeben / ausbezahlt) als Subline.
function _akWfRenderMaList() {
    const el = document.getElementById('akontoMaList');
    const countEl = document.getElementById('akontoListCount');
    if (!el || !_akWfData) return;
    const z = _akWfData.zahlungen || [];
    if (countEl) countEl.textContent = z.length ? `${z.length} MA` : '';

    if (!z.length) {
        if (_akWfData.akontoStatus === 'OFFEN') {
            el.innerHTML = `<div style="padding:28px;text-align:center;color:#94a3b8;font-size:13px">Akonto noch nicht vorbereitet</div>`;
        } else {
            el.innerHTML = `<div style="padding:28px;text-align:center;color:#94a3b8;font-size:13px">Keine berechtigten MA in dieser Periode</div>`;
        }
        return;
    }
    const modelColor = { MTP:'#d1fae5', UTP:'#fef3c7', FIX:'#dbeafe', 'FIX-M':'#ede9fe' };

    // Lokale MA-Liste leeren und Zeile-für-Zeile aufbauen — damit der
    // QST-Button mit einem komplexen JS-Objekt sicher per addEventListener
    // verdrahtet werden kann (analog loadLohnList).
    el.innerHTML = '';
    z.forEach(r => {
        const meta = _AK_BLATT_STATUS[r.status] || _AK_BLATT_STATUS.BERECHNET;
        const isSelected = r.id === _akWfSelectedId;
        const isFreigegeben = r.status === 'FREIGEGEBEN_GF';
        const isAusbezahlt  = r.status === 'AUSBEZAHLT';
        const isDone = isFreigegeben || isAusbezahlt;

        // Vollständiges Employee-Objekt für Modell-Badge + QST-Button
        const full = _akWfEmpMap?.[r.employeeId] || {};
        // Modell: bevorzugt aus dem aktuell aktiven Vertrag dieser Filiale,
        // sonst aus dem ersten aktiven Vertrag des MA.
        const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) || null;
        const employments = full.employments || [];
        const empCurrent = employments.find(v => v.companyProfileId === branchId && v.isActive)
                        || employments.find(v => v.isActive)
                        || employments[0]
                        || null;
        const model = empCurrent?.employmentModel || '';

        const initials = ((r.firstName||'')[0]||'') + ((r.lastName||'')[0]||'');
        const warn = !r.bankAccountCount ? ` <span title="Keine aktive Bankverbindung" style="color:#b91c1c">⚠</span>` : '';
        const hrNote = r.kommentarHr ? ` <span title="HR-Notiz: ${(r.kommentarHr||'').replace(/"/g,'&quot;')}" style="color:#b91c1c">📝</span>` : '';

        const row = document.createElement('div');
        row.className = 'lohn-emp-row';
        if (isSelected) row.classList.add('lohn-emp-active');
        row.dataset.empId = r.employeeId;
        row.dataset.akontoId = r.id;
        row.onclick = () => akWfSelectMa(r.id);

        // Subline-Layout 1:1 wie Definitivlauf (Walter 16.05.2026):
        //   fertig → grüner Text "Lohn freigegeben"
        //   sonst → graue Personalnummer
        // Kein "·"-Separator, kein doppelter Inhalt.
        const sublineColor = isDone ? '#16a34a' : '#94a3b8';
        const sublineText  = isDone ? (isAusbezahlt ? 'Akonto ausbezahlt' : 'Lohn freigegeben') : (r.employeeNumber || '');

        row.innerHTML = `
            <div style="width:34px;height:34px;border-radius:50%;background:${isDone?'#dcfce7':'#e2e8f0'};display:flex;align-items:center;justify-content:center;font-weight:700;font-size:12px;color:${isDone?'#166534':'#475569'};flex-shrink:0">
                ${isDone ? '✓' : initials.toUpperCase()}
            </div>
            <div style="flex:1;min-width:0">
                <div class="lohn-emp-name" style="font-weight:600;font-size:13px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${r.firstName} ${r.lastName}${warn}${hrNote}</div>
                <div class="lohn-emp-nr" style="font-size:11px;color:${sublineColor}">${sublineText}</div>
            </div>
            ${model ? `<span style="font-size:10px;font-weight:600;padding:2px 7px;border-radius:10px;background:${modelColor[model]||'#f1f5f9'}">${model}</span>` : ''}
            <button class="ak-qst-btn" title="Quellensteuer erfassen"
                    style="background:none;border:1px solid #cbd5e1;border-radius:6px;padding:2px 7px;font-size:11px;cursor:pointer;color:#475569;flex-shrink:0">QST</button>`;

        // QST-Button: gleicher Modal-Aufruf wie im Definitivlauf (openQstModal).
        // Per addEventListener verdrahtet, damit das komplexe Argument-Objekt
        // kein Escaping-Problem im inline-onclick erzeugt.
        const qstBtn = row.querySelector('.ak-qst-btn');
        if (qstBtn) {
            qstBtn.addEventListener('click', ev => {
                ev.stopPropagation();
                if (typeof openQstModal !== 'function') return;
                openQstModal(r.employeeId, {
                    firstName:       r.firstName,
                    lastName:        r.lastName,
                    zipCode:         r.zipCode  ?? full.zipCode  ?? '',
                    city:            r.city     ?? full.city     ?? '',
                    nationalityCode: full.nationalityRef?.code ?? full.nationality ?? '',
                    permitTypeName:  full.permitType?.name ?? '',
                    zivilstand:      full.zivilstand ?? '',
                });
            });
        }
        el.appendChild(row);
    });
}

function akWfSelectMa(id) {
    _akWfSelectedId = id;
    _akWfRenderMaList();    // re-render für active-Highlight
    filterAkontoMaList();   // Sucher-Filter ggf. erneut anwenden
    _akWfRenderStatusBar(); // per-MA-Aktionen (Freigeben/Zurückziehen) neu rendern
    _akWfUpdateTopActions();
    akWfLoadDetail(id);     // Rich-Detail asynchron nachladen
}

// Live-Filter über die Akonto-MA-Liste (analog filterLohnEmpList in payroll.js).
// Sucht in Vorname, Nachname, Personal-Nr — case-insensitive, alle Tokens müssen treffen.
function filterAkontoMaList() {
    const qRaw = (document.getElementById('akontoEmpSearch')?.value || '').toLowerCase().trim();
    const tokens = qRaw.split(/\s+/).filter(Boolean);
    document.querySelectorAll('#akontoMaList .lohn-emp-row').forEach(row => {
        if (tokens.length === 0) { row.style.display = ''; return; }
        const name = (row.querySelector('.lohn-emp-name')?.textContent || '').toLowerCase();
        const nr   = (row.querySelector('.lohn-emp-nr')?.textContent   || '').toLowerCase();
        const hay  = name + ' ' + nr;
        row.style.display = tokens.every(t => hay.includes(t)) ? '' : 'none';
    });
}

async function akWfLoadDetail(id) {
    const card = document.getElementById('akontoDetailCard');
    const empty = document.getElementById('akontoDetailEmpty');
    const content = document.getElementById('akontoDetailContent');
    if (!card || !content) return;
    card.style.display = '';
    if (empty) empty.style.display = 'none';
    content.innerHTML = `<div style="padding:32px;text-align:center;color:#94a3b8">Lade Lohnblatt…</div>`;
    try {
        const r = await fetch(`/api/akonto/workflow/lohnblatt/${id}`, { headers: ah() });
        if (!r.ok) {
            content.innerHTML = `<div style="padding:20px;color:#b91c1c">Fehler beim Laden (${r.status}).</div>`;
            return;
        }
        const d = await r.json();
        _akWfRenderRichDetail(d);
    } catch (e) {
        content.innerHTML = `<div style="padding:20px;color:#b91c1c">Verbindungsfehler: ${e.message}</div>`;
    }
}

// ── Detail-Panel rechts ──────────────────────────────────────────────────
// Walter-Vorgabe 16.05.2026: Im Akonto-Lauf soll der VOLLE Lohnzettel sichtbar
// sein — identisch zur Lohnabrechnung im Definitivlauf (Festlohn, Korrektur
// Krankheit, Ferienentschädigung, Feiertagentschädigung, 13. ML, alle Abzüge
// im Detail, Stunden-Übersicht, Saldi-Block, Auszahlungs-Empfänger).
//
// Aufbau des Akonto-Detail-Panels:
//   1. Akonto-Header: Name, Personal-Nr, Periode, Stichtag, Status-Badge
//   2. HR-Notiz (wenn vorhanden)
//   3. VOLLER Lohnzettel (renderLohnSlip aus payroll.js) — Daten aus
//      /api/payroll/calculate, projiziert auf den vollen Monat. Das ist die
//      Vorschau auf den Definitivlauf.
//   4. Akonto-spezifische Berechnungs-Box: geschätzter Brutto/Abzüge +
//      Netto-Akonto-Betrag (= das was JETZT ausbezahlt wird).
//   5. GF-Freigabe / Zurückziehen-Buttons.
function _akWfRenderRichDetail(d) {
    const card = document.getElementById('akontoDetailCard');
    const empty = document.getElementById('akontoDetailEmpty');
    const content = document.getElementById('akontoDetailContent');
    if (!card || !content) return;
    card.style.display  = '';
    if (empty) empty.style.display = 'none';

    const status = _AK_BLATT_STATUS[d.status] || _AK_BLATT_STATUS.BERECHNET;
    const isGf   = _akWfData?.akontoStatus === 'IN_BEARBEITUNG_GF';
    const canFreigeben     = isGf && d.status === 'BERECHNET';
    const canZurueckziehen = isGf && d.status === 'FREIGEGEBEN_GF';

    const e = d.employee || {};
    const b  = d.berechnung || {};

    const hrNotice = d.kommentarHr
        ? `<div style="margin:0 20px 12px;padding:10px 12px;background:#fef2f2;border:1px solid #fecaca;border-radius:7px;font-size:12.5px;color:#b91c1c">
               <b>HR-Notiz:</b> ${d.kommentarHr}
           </div>` : '';
    const gfFreigabe = d.gfFreigegebenAt
        ? `<div style="margin-top:8px;font-size:11.5px;color:#15803d">✓ Freigegeben am ${_akFmtTs(d.gfFreigegebenAt)}</div>` : '';

    // Walter Etappe 5 (16.05.2026): nach dem Backend-Refactor rechnet
    // AkontoLaufService für UTP/MTP lokal exakt (Stunden + Ferien-Pott − SV
    // × AkontoProzentHourly). FIX/FIX-M wird weiterhin via Slip-Sync
    // korrigiert (Backend kann den echten Definitivlauf-Wert nicht ohne
    // Loopback rechnen).
    const model = (d.vertrag?.employmentModel || '').toUpperCase();
    const isFix = (model === 'FIX' || model === 'FIX-M');
    const akontoProzentFix = Number(d.akontoProzentFix ?? 80);

    // Eindeutige IDs pro Lohnblatt — verhindert Konflikte falls der User
    // schnell zwischen MA wechselt und alte Mount-Points noch im DOM hängen.
    const lohnSlipMountId    = `akontoLohnSlipMount_${d.id}`;
    const akontoFixBoxMountId = `akontoFixBox_${d.id}`;
    const akontoNettoMountId  = `akontoNettoZeile_${d.id}`;

    content.innerHTML = `
        <!-- Akonto-Header (oben fixiert, Status-Badge + Audit) -->
        <div style="padding:18px 20px;border-bottom:1px solid #f1f5f9;background:#fafafa">
            <div style="display:flex;align-items:flex-start;justify-content:space-between;gap:12px">
                <div>
                    <div style="font-weight:700;font-size:17px;color:#0f172a">${e.firstName || ''} ${e.lastName || ''}</div>
                    <div style="font-size:11.5px;color:#94a3b8;margin-top:2px">Personal-Nr. ${e.employeeNumber || '–'} · Periode ${_akFmtDate(d.periodFrom)} – ${_akFmtDate(d.periodTo)} · Akonto-Stichtag ${_akFmtDate(d.payoutDate)}</div>
                </div>
                <span style="background:${status.bg};color:${status.color};padding:4px 10px;border-radius:8px;font-weight:700;font-size:12px;white-space:nowrap">${status.label}</span>
            </div>
            ${gfFreigabe}
        </div>

        ${hrNotice}

        <!-- Voller Lohnzettel (= Vorschau Definitivlauf) wird hier
             asynchron via renderLohnSlip(slip, mountEl) eingefügt.
             Padding bewusst klein (Walter 16.05.2026 — Lohnzettel ist das
             Wesentliche, kein Frame drumherum nötig). -->
        <div id="${lohnSlipMountId}" style="padding:4px 8px 8px 8px;border-bottom:1px solid #f1f5f9">
            <div style="padding:30px;text-align:center;color:#94a3b8;font-size:13px">Lade Lohnzettel-Vorschau…</div>
        </div>

        <!-- Akonto-Berechnungs-Box: was JETZT bei der Akonto-Zahlung fliesst.
             Walter-Vorgabe 16.05.2026 (universell für alle Modelle): eine
             einzige Zeile "X% von voraussichtlichem Auszahlungsbetrag = CHF YYY".
             Wert wird nach dem Slip-Load durch _akWfSyncFixFromSlip() final
             ausgefüllt + serverseitig synchronisiert. Bei bereits freigegebenen
             Lohnblättern ist der Wert eingefroren — UX-Hinweis macht das klar. -->
        <div id="${akontoFixBoxMountId}" style="padding:14px 20px;background:#f8fafc;border-top:2px solid #0f172a">
            <div style="display:flex;align-items:baseline;justify-content:space-between;gap:14px;flex-wrap:wrap">
                <div style="font-size:14px;color:#0f172a">
                    <b>${akontoProzentFix}%</b> von voraussichtlichem Auszahlungsbetrag
                </div>
                <div id="${akontoNettoMountId}" style="font-size:20px;font-weight:700;color:#0f172a;font-variant-numeric:tabular-nums">${_akFmtChf(b.nettoAkonto)}</div>
            </div>
            <div style="margin-top:4px;font-size:11px;color:#94a3b8">
                ${d.status === 'BERECHNET'
                    ? 'wird nach Lohnzettel-Vorschau aktualisiert…'
                    : (d.status === 'FREIGEGEBEN_GF'
                        ? '🔒 Wert eingefroren beim GF-Freigeben. „↶ Freigabe zurückziehen" oben rechts klicken um neu zu berechnen.'
                        : '🔒 Akonto ausbezahlt — Wert eingefroren und unveränderlich.')}
            </div>
            <!-- Freigeben/Zurückziehen liegen oben im prominenten Top-Bar
                 (#lohnTopActionsAkonto) — Walter-Vorgabe 16.05.2026, gleicher
                 Ort wie "Lohn bestätigen" im Definitivlauf. -->
        </div>`;

    // Vollen Lohnzettel asynchron nachladen — gleicher Endpoint wie
    // Definitivlauf, gleiche Render-Funktion. autoSync nur für FIX/FIX-M
    // (Walter Etappe 5e): UTP/MTP haben jetzt die korrekte lokale Berechnung.
    _akWfLoadFullLohnSlip(d, lohnSlipMountId, {
        autoSync: isFix,
        akontoProzentFix,
        akontoZahlungId: d.id,
        akontoNettoMountId,
        status: d.status,
    });
}

// Lädt den vollen Lohnzettel über /api/payroll/calculate und rendert ihn
// in den Mount-Point innerhalb des Akonto-Detail-Panels. Verwendet ein
// Race-Token (analog loadLohnSlip in payroll.js), damit schneller MA-Wechsel
// nicht zur Überschreibung mit veralteten Daten führt.
let _akWfSlipReqToken = 0;
async function _akWfLoadFullLohnSlip(d, mountId, fixCtx) {
    const myToken = ++_akWfSlipReqToken;
    // EmployeeId steht oben im Lohnblatt-Response (`z.EmployeeId`). Filiale +
    // Periode werden aus dem Page-Kontext gelesen — gleiche Quelle wie alle
    // anderen Aktionen im Akonto-Workflow (fixedCompanyProfileId + Lohn-Selects).
    const employeeId = d.employeeId;
    const companyId  = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) || null;
    const year       = parseInt(document.getElementById('lohnYearSelect')?.value, 10);
    const month      = parseInt(document.getElementById('lohnMonthSelect')?.value, 10);
    if (!employeeId || !companyId || !year || !month) return;
    try {
        const ts = Date.now();
        const r = await fetch(`/api/payroll/calculate?employeeId=${employeeId}&year=${year}&month=${month}&companyProfileId=${companyId}&_=${ts}`,
                              { headers: ah(), cache: 'no-store' });
        if (myToken !== _akWfSlipReqToken) return;   // stale
        const mount = document.getElementById(mountId);
        if (!mount) return;
        if (!r.ok) {
            const txt = await r.text();
            let msg = `HTTP ${r.status}`;
            try { const j = JSON.parse(txt); msg = j.error || j.message || j.title || txt; } catch { msg = txt.substring(0, 300); }
            mount.innerHTML = `<div style="padding:20px;color:#b91c1c;font-size:12.5px">⚠ Lohnzettel-Vorschau nicht verfügbar: ${msg}</div>`;
            return;
        }
        const slip = await r.json();
        if (myToken !== _akWfSlipReqToken) return;   // stale
        if (slip && slip.pausiert) {
            mount.innerHTML = `
                <div style="padding:24px;text-align:center;font-size:13px;color:#475569">
                    🏥 <b>Versicherungs-Übergabe aktiv</b><br>
                    <span style="color:#94a3b8">Lohn läuft über die KTG-Versicherung — keine Lohnabrechnung durch AG.</span>
                </div>`;
            return;
        }
        // Slip braucht employeeId-Feld damit jumpToMaForBankEntry etc. funktioniert
        slip.employeeId = employeeId;
        slip.companyId  = companyId;
        slip.year       = year;
        slip.month      = month;
        if (typeof renderLohnSlip === 'function') {
            renderLohnSlip(slip, mount);
        } else {
            mount.innerHTML = `<div style="padding:20px;color:#b91c1c;font-size:12.5px">Render-Funktion nicht geladen.</div>`;
        }

        // Backend-Sync für ALLE Modelle (Walter 16.05.2026): NettoAkonto =
        // AkontoProzent × Definitiv-Auszahlung, auf CHF 10 abgerundet. Damit
        // ist der Akonto NIE höher als die voraussichtliche Definitiv-Auszahlung
        // (kein Rückzahlungs-Risiko beim Definitivlauf-Restbetrag). Nur sinnvoll
        // solange Status BERECHNET ist — nach GF-Freigabe friert der Wert ein.
        if (fixCtx?.autoSync && fixCtx.status === 'BERECHNET') {
            const auszahlung = Number(slip.auszahlungsbetrag ?? slip.nettolohn ?? 0);
            if (auszahlung > 0) {
                _akWfSyncFixFromSlip(fixCtx.akontoZahlungId, auszahlung, fixCtx.akontoNettoMountId);
            }
        }
    } catch (e) {
        if (myToken !== _akWfSlipReqToken) return;
        const mount = document.getElementById(mountId);
        if (mount) mount.innerHTML = `<div style="padding:20px;color:#b91c1c;font-size:12.5px">Verbindungsfehler: ${e.message}</div>`;
    }
}

// Persistiert den frisch berechneten Akonto-Betrag für FIX/FIX-M und
// aktualisiert die Anzeige sowie die Zeile in der MA-Liste links.
async function _akWfSyncFixFromSlip(akontoZahlungId, auszahlungsbetrag, displayMountId) {
    try {
        const r = await fetch(`/api/akonto/workflow/sync-fix-from-slip/${akontoZahlungId}`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ auszahlungsbetrag })
        });
        if (!r.ok) return;
        const d = await r.json();
        // Wert im rechten Panel updaten
        const mount = document.getElementById(displayMountId);
        if (mount) mount.textContent = _akFmtChf(d.nettoAkonto);
        // Hint-Zeile "wird nach Lohnzettel-Vorschau aktualisiert" entfernen
        const box = mount?.closest('div')?.parentElement;
        const hint = box?.querySelector('div[style*="11px"][style*="94a3b8"]');
        if (hint) hint.remove();
        // Wert in der lokalen Cache-Liste mit-aktualisieren, sodass MA-Liste
        // links den korrekten Betrag in der Subline zeigt (falls dort gezeigt).
        const z = (_akWfData?.zahlungen || []).find(x => x.id === akontoZahlungId);
        if (z) {
            z.nettoAkonto        = d.nettoAkonto;
            z.geschaetzterBrutto = d.geschaetzterBrutto;
            z.geschaetzteAbzuege = d.geschaetzteAbzuege;
        }
    } catch {
        // Sync-Fehler schweigend ignorieren — Backend-Logs protokollieren.
        // Anzeige bleibt auf altem Wert; Walter sieht keine Fehlermeldung.
    }
}

// ── Keyboard-Navigation: Pfeiltasten ↑/↓ + Enter freigeben ───────────────
// Walter-Vorgabe 16.05.2026 — schnelles Durchklicken der 44 MA pro Filiale
// ohne Maus:
//   ↓ / ↑     → MA-Wechsel (analog Definitivlauf-Mitarbeiterliste)
//   Enter / F → "Lohnblatt freigeben" für den aktuellen MA + Auto-Sprung
//               zum nächsten BERECHNET-MA
document.addEventListener('keydown', e => {
    const onLohn = document.getElementById('page-lohn')?.classList.contains('active');
    if (!onLohn || _akWfMode !== 'akonto') return;
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp' && e.key !== 'Enter' && e.key !== 'f' && e.key !== 'F') return;
    // Nicht reagieren wenn Fokus in Eingabefeld / Modal offen
    const t = e.target;
    const tag = (t?.tagName || '').toLowerCase();
    if (tag === 'input' || tag === 'textarea' || tag === 'select' || t?.isContentEditable) return;
    if (e.metaKey || e.ctrlKey || e.altKey) return;
    const drawerOpen = document.querySelector('.drawer-open, [id$="Drawer"][style*="display:block"], [id$="Modal"][style*="display:flex"]');
    if (drawerOpen) return;

    const z = _akWfData?.zahlungen || [];
    if (!z.length) return;

    // Enter / F → Lohnblatt freigeben (nur wenn aktueller Status BERECHNET +
    // Periode IN_BEARBEITUNG_GF).
    if (e.key === 'Enter' || e.key === 'f' || e.key === 'F') {
        const sel = z.find(x => x.id === _akWfSelectedId);
        const isGf = _akWfData?.akontoStatus === 'IN_BEARBEITUNG_GF';
        if (sel && isGf && sel.status === 'BERECHNET') {
            e.preventDefault();
            akWfFreigeben(_akWfSelectedId);
        }
        return;
    }

    // Pfeiltasten → Navigation
    const idx = z.findIndex(x => x.id === _akWfSelectedId);
    let next = idx;
    if (e.key === 'ArrowDown') next = idx < 0 ? 0 : Math.min(idx + 1, z.length - 1);
    if (e.key === 'ArrowUp')   next = idx < 0 ? 0 : Math.max(idx - 1, 0);
    if (next !== idx && z[next]) {
        e.preventDefault();
        akWfSelectMa(z[next].id);
        // Aktiven Eintrag in den sichtbaren Bereich scrollen — gleiche
        // .lohn-emp-active-Klasse wie im Definitivlauf
        setTimeout(() => {
            document.querySelector('#akontoMaList .lohn-emp-active')
                    ?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
        }, 50);
    }
});

// ── Action-Handler ────────────────────────────────────────────────────────

async function akWfStart() {
    const branchId = fixedCompanyProfileId;
    const year  = parseInt(document.getElementById('lohnYearSelect').value, 10);
    const month = parseInt(document.getElementById('lohnMonthSelect').value, 10);
    if (!branchId || !year || !month) return;
    // Stichtag aus konfiguriertem Akonto-Termin oder Tag 23
    let stichtag = null;
    try {
        const r = await fetch(`/api/akonto-termine?companyProfileId=${branchId}&year=${year}`, { headers: ah() });
        if (r.ok) {
            const termine = await r.json();
            const t = (termine || []).find(x => x.month === month);
            if (t) stichtag = t.payoutDate;
        }
    } catch {}
    if (!stichtag) {
        const lastDay = new Date(year, month, 0).getDate();
        stichtag = `${year}-${String(month).padStart(2,'0')}-${String(Math.min(23, lastDay)).padStart(2,'0')}`;
    }
    await _akWfPost('/start', { companyProfileId: branchId, year, month, stichtag });
}

async function akWfFreigeben(id) {
    await _akWfPost(`/freigeben/${id}`, {});
    // Nach erfolgreicher Freigabe automatisch zum nächsten unbearbeiteten
    // MA springen (Walter 16.05.2026 — 44 MA pro Filiale, sonst zu viele
    // Klicks). _akWfPost hat akWfRefresh schon gemacht, also ist _akWfData
    // frisch und enthält den neuen Status.
    _akWfJumpToNextOpen(id);
}

// Sucht in _akWfData.zahlungen das nächste Lohnblatt mit Status BERECHNET
// nach dem aktuell freigegebenen — wenn keines mehr offen ist, bleibt der
// User beim freigegebenen MA stehen.
function _akWfJumpToNextOpen(currentId) {
    const z = _akWfData?.zahlungen || [];
    if (!z.length) return;
    const idx = z.findIndex(x => x.id === currentId);
    // Erst ab idx+1 weitersuchen, sonst von vorne (Wrap-around) bis zum Start.
    const order = [];
    for (let i = idx + 1; i < z.length; i++) order.push(z[i]);
    for (let i = 0; i <= idx; i++)            order.push(z[i]);
    const next = order.find(x => x.status === 'BERECHNET');
    if (next) {
        akWfSelectMa(next.id);
        setTimeout(() => {
            document.querySelector('#akontoMaList .lohn-emp-active')
                    ?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
        }, 50);
    }
}
async function akWfZurueckziehen(id) {
    if (!confirm('Freigabe für dieses Lohnblatt zurückziehen?')) return;
    await _akWfPost(`/zurueckziehen/${id}`, null);
}
async function akWfAnHrSenden() {
    if (!confirm('Alle freigegebenen Lohnblätter an HR senden?\nNach dem Senden kannst du nichts mehr ändern, bis HR ggf. zurückgibt.')) return;
    const branchId = fixedCompanyProfileId;
    const year  = parseInt(document.getElementById('lohnYearSelect').value, 10);
    const month = parseInt(document.getElementById('lohnMonthSelect').value, 10);
    await _akWfPost('/an-hr-senden', { companyProfileId: branchId, year, month });
}
async function akWfZurueckAnGf() {
    const kommentar = prompt('Begründung für den GF (warum zurück?):');
    if (kommentar === null || kommentar.trim() === '') return;
    const branchId = fixedCompanyProfileId;
    const year  = parseInt(document.getElementById('lohnYearSelect').value, 10);
    const month = parseInt(document.getElementById('lohnMonthSelect').value, 10);
    await _akWfPost('/zurueck-an-gf', { companyProfileId: branchId, year, month, kommentar });
}
async function akWfHrFreigabe() {
    if (!confirm('HR-Final-Freigabe für diesen Akonto-Lauf?')) return;
    const branchId = fixedCompanyProfileId;
    const year  = parseInt(document.getElementById('lohnYearSelect').value, 10);
    const month = parseInt(document.getElementById('lohnMonthSelect').value, 10);
    await _akWfPost('/hr-freigabe', { companyProfileId: branchId, year, month });
}
async function akWfAuszahlen() {
    if (!confirm('Akonto auszahlen?\n\nHINWEIS: DTA-Generierung (pain.001) folgt in Phase 3d — bis dahin musst du die Bank-Zahlung manuell anstossen.\n\nDie Lohnblätter werden auf AUSBEZAHLT gesetzt und sind danach unveränderlich.')) return;
    const branchId = fixedCompanyProfileId;
    const year  = parseInt(document.getElementById('lohnYearSelect').value, 10);
    const month = parseInt(document.getElementById('lohnMonthSelect').value, 10);
    await _akWfPost('/auszahlen', { companyProfileId: branchId, year, month });
}

async function _akWfPost(path, body) {
    try {
        const r = await fetch('/api/akonto/workflow' + path, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: body == null ? '' : JSON.stringify(body),
        });
        if (!r.ok) {
            let msg = 'HTTP ' + r.status;
            try { const j = await r.json(); msg = j.error || j.message || msg; } catch {}
            alert('Fehler: ' + msg);
            return;
        }
        await akWfRefresh();
        akWfBadgeRefresh();
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// ══════════════════════════════════════════════════════════════════════
// SIDEBAR-BADGE für Pending-Counts
// ══════════════════════════════════════════════════════════════════════
async function akWfBadgeRefresh() {
    try {
        const r = await fetch('/api/akonto/workflow/pending-counts', { headers: ah() });
        if (!r.ok) return;
        const d = await r.json();
        const navItem = document.querySelector('.nav-item[data-page="lohn"]');
        if (!navItem) return;
        let badge = navItem.querySelector('.akonto-pending-badge');
        if (!badge) {
            badge = document.createElement('span');
            badge.className = 'akonto-pending-badge';
            badge.style.cssText = 'margin-left:auto;background:#dc2626;color:white;font-size:10px;font-weight:700;padding:1px 7px;border-radius:9px;min-width:18px;text-align:center';
            navItem.appendChild(badge);
        }
        const n = d.inbox || 0;
        if (n > 0) {
            badge.textContent = n;
            badge.style.display = '';
            badge.title = d.role === 'hr'
                ? `${n} Akonto-Läufe warten auf HR-Freigabe`
                : `${n} Akonto-Läufe in Bearbeitung`;
        } else {
            badge.style.display = 'none';
        }
    } catch {}
}

// Periodischer Refresh des Badges (alle 60 Sek), solange der User eingeloggt ist.
let _akBadgeTimer = null;
function akWfStartBadgePolling() {
    if (_akBadgeTimer) return;
    akWfBadgeRefresh();
    _akBadgeTimer = setInterval(akWfBadgeRefresh, 60000);
}
function akWfStopBadgePolling() {
    if (_akBadgeTimer) { clearInterval(_akBadgeTimer); _akBadgeTimer = null; }
}
