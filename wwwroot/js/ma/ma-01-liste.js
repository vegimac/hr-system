// ══════════════════════════════════════════════
// MITARBEITER VERWALTUNG
// ══════════════════════════════════════════════

// ── Adress-Feld-Validierung (Walter-Vorgabe 01.06.2026, global) ──────
// Live-Filter beim Tippen + harte Save-Validierung. Global verfügbar
// via window.validateZip / validateCity / validatePhone / validateEmail
// / formatPhoneIntl für alle Adressmasken im ganzen Programm.
//
// Walter-Vorgabe 01.06.2026 (Erweiterung):
//   • PLZ: nur Zahlen, bei Nicht-Zahl Eingabe → kurze Meldung (Toast).
//   • Telefon: Format „+99 99 999 99 99" (z.B. +41 79 409 43 33).
//     Wird beim Verlassen des Felds (onblur) automatisch formatiert.
//   • E-Mail: Format-Check (rotes Border-Highlight bei ungültigem Format).

// Kleine Toast-Funktion (lazy DOM, eine Instanz wieder verwendet).
window._showValidationToast = function(msg) {
    let el = document.getElementById('_validation_toast');
    if (!el) {
        el = document.createElement('div');
        el.id = '_validation_toast';
        el.style.cssText = 'position:fixed;bottom:24px;left:50%;transform:translateX(-50%);'
            + 'background:#dc2626;color:#fff;padding:10px 18px;border-radius:8px;'
            + 'font-size:13px;font-weight:600;z-index:9999;box-shadow:0 8px 20px rgba(0,0,0,0.25);'
            + 'opacity:0;transition:opacity .15s';
        document.body.appendChild(el);
    }
    el.textContent = msg;
    el.style.opacity = '1';
    clearTimeout(window._validation_toast_timer);
    window._validation_toast_timer = setTimeout(() => { el.style.opacity = '0'; }, 2200);
};

/** Neutraler Info-Toast (Kohle) — z.B. QST nach Konfessions-Änderung. */
function _showQstSyncToast(msg) {
    let el = document.getElementById('_qst_sync_toast');
    if (!el) {
        el = document.createElement('div');
        el.id = '_qst_sync_toast';
        el.style.cssText = 'position:fixed;bottom:24px;left:50%;transform:translateX(-50%);'
            + 'background:#3f3f3f;color:#fff;padding:10px 18px;border-radius:12px;'
            + 'font-size:13px;font-weight:650;z-index:9999;box-shadow:0 10px 24px rgba(60,55,48,0.22);'
            + 'opacity:0;transition:opacity .15s';
        document.body.appendChild(el);
    }
    el.textContent = msg;
    el.style.opacity = '1';
    clearTimeout(window._qst_sync_toast_timer);
    window._qst_sync_toast_timer = setTimeout(() => { el.style.opacity = '0'; }, 3200);
}

window.validateZip = function(el) {
    // CH = 4 Ziffern, Grenzregion (z.B. Italien) bis 10. Nur Ziffern.
    const before = el.value;
    const cleaned = before.replace(/\D/g, '').slice(0, 10);
    if (/\D/.test(before)) {
        el._lastZipWarn = el._lastZipWarn || 0;
        const now = Date.now();
        if (now - el._lastZipWarn > 1500) {
            window._showValidationToast('PLZ: nur Zahlen erlaubt.');
            el._lastZipWarn = now;
        }
    }
    el.value = cleaned;
};
window.validateCity = function(el) {
    // Buchstaben + diakritische Zeichen + Leerzeichen + Bindestrich +
    // Apostroph + Punkt (für Schweizer Ortsnamen wie La Chaux-de-Fonds,
    // St. Gallen, Murten / Morat).
    el.value = el.value.replace(/[^A-Za-zÀ-ÿ\s\-'\.]/g, '');
};
window.validatePhone = function(el) {
    // Live-Maske (Walter 17.07.2026): begleitet die Eingabe direkt zum
    // Zielformat «+41 78 333 22 22» — fuehrende 0 (078…) und nackte
    // Mobile-Nummern (78…) werden automatisch zu +41, Gruppierung
    // 2-2-3-2-2 waechst beim Tippen mit. Auslaendische Nummern: mit «+»
    // beginnen, dann wird keine 41 vorangestellt.
    const hadPlus = (el.value || '').trimStart().startsWith('+');
    let digits = (el.value || '').replace(/\D/g, '');
    if (digits.startsWith('00')) digits = digits.slice(2);
    if (!hadPlus) {
        if (digits.startsWith('0')) digits = '41' + digits.slice(1);
        else if (digits && !digits.startsWith('41') && /^[2-9]/.test(digits)) digits = '41' + digits;
    }
    digits = digits.slice(0, 13);
    if (!digits) { el.value = hadPlus ? '+' : ''; return; }
    let out = '+' + digits.slice(0, 2);
    if (digits.length > 2)  out += ' ' + digits.slice(2, 4);
    if (digits.length > 4)  out += ' ' + digits.slice(4, 7);
    if (digits.length > 7)  out += ' ' + digits.slice(7, 9);
    if (digits.length > 9)  out += ' ' + digits.slice(9, 11);
    if (digits.length > 11) out += digits.slice(11);
    el.value = out;
};
window.formatPhoneIntl = function(raw) {
    // Format „+99 99 999 99 99" (12 Ziffern: 2 Country + 9 lokal).
    // Strippt alle Nicht-Zahlen, erkennt 00-Präfix und führende 0 (CH-lokal).
    if (!raw) return '';
    let digits = String(raw).replace(/\D/g, '');
    if (!digits) return '';
    // 00XXXXXXXXXXX → +XXXXXXXXXXX (00 = internationale Vorwahl)
    if (digits.startsWith('00')) digits = digits.slice(2);
    // Wenn 10-stellig und mit 0 beginnt → lokales CH-Format, +41 prepend
    if (digits.length === 10 && digits.startsWith('0')) {
        digits = '41' + digits.slice(1);
    }
    // Wenn nur 9-stellig (z.B. „793090400") → CH-lokal ohne 0, +41 prepend
    if (digits.length === 9 && /^[2-9]/.test(digits)) {
        digits = '41' + digits;
    }
    // Erwartet jetzt 11 Ziffern (2 Country + 9 lokal). Aufteilung 2-2-3-2-2.
    if (digits.length === 11) {
        return '+' + digits.slice(0,2)
             + ' ' + digits.slice(2,4)
             + ' ' + digits.slice(4,7)
             + ' ' + digits.slice(7,9)
             + ' ' + digits.slice(9,11);
    }
    // Sonst: Rohwert mit + vor den Ziffern (besser als nichts).
    return '+' + digits;
};
window.validatePhoneBlur = function(el) {
    // onblur: harte Formatierung. Leer = OK.
    if (!el.value || !el.value.trim()) return;
    el.value = window.formatPhoneIntl(el.value);
};
window.validateEmail = function(el, showError) {
    // E-Mail-Format-Check: nur bei blur (showError=true) eine Warnung
    // anzeigen, beim Tippen nichts. Save-Validierung blockt zusätzlich.
    if (!showError) { el.style.borderColor = ''; return; }
    const v = (el.value || '').trim();
    const ok = !v || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v);
    el.style.borderColor = ok ? '' : '#dc2626';
    if (!ok) window._showValidationToast('E-Mail-Format ungültig.');
};

let allEmployees = [];
let selectedEmployeeId = null;
let selectedEmployee   = null;   // Ganzes Mitarbeiter-Objekt (für Sollstunden etc.)
let activeEmpTab = 'uebersicht';   // Etappe 2 (Walter 17.07.2026): Uebersicht = Lande-Tab

// Austrittsdatum-Filter (greift NUR im Inaktive-Modus). Wenn gesetzt, zeigt
// die Liste nur MA mit Austritt am oder nach diesem Datum. Walter-Vorgabe
// 28.05.2026: schnell die jüngst Ausgetretenen finden.
let _empExitDateAfter = '';

// Aktiv-Filter & Cache aller MA (für "Aktive" / "Inaktive" / "Alle"-Toggle)
let _empAllRaw = [];
let _empFilter = 'aktiv';

// Spezial-Filter (Datenqualität-Checks) — quer zum Aktiv/Inaktiv-Status.
// Aktuell aktiv: '' (kein Spezialfilter) | 'no-bank' (ohne Bankverbindung).
// Erweiterbar: einfach neuen Eintrag in EMP_SPECIAL_FILTERS registrieren —
// jede Funktion erhält den MA und liefert true wenn er die Bedingung erfüllt
// und damit IN der gefilterten Liste bleiben soll.
let _empSpecialFilter = '';
// Caches für Spezialfilter — lazy geladen beim ersten Aktivieren.
let _empIdsWithActiveBank   = null;   // MA-IDs mit aktiver Bankverbindung
let _empIdsWithVerwarnung   = null;   // MA-IDs mit nicht-stornierter Verwarnung (Filter 23.08.2026)
let _empIdsWithActiveQst    = null;
let _empIdsWithKinderzulage = null;   // MA-IDs mit laufender Kinderzulage (Filter 04.09.2026)
let _empIdsWithPermitHistory = null;  // MA-IDs mit MINDESTENS einem Permit-History-Eintrag
let _empIdsWithExpiredPermit = null;  // MA-IDs mit abgelaufener massgebender Bewilligung
let _empIdsCurrentlyAbsent  = null;   // MA-IDs mit aktueller KRANK/UNFALL/MUTT_VATER-Absenz

// Hilfsfunktion: hat der MA aktuell einen gültigen Vertrag?
// = mindestens ein Employment mit ContractStartDate <= heute und
// (ContractEndDate ist null ODER ContractEndDate >= heute).
function _empHasActiveContract(e) {
    const today = new Date(); today.setHours(0, 0, 0, 0);
    return (e.employments || []).some(emp => {
        if (!emp.contractStartDate) return false;
        const from = new Date(emp.contractStartDate);
        if (from > today) return false;
        if (!emp.contractEndDate) return true;
        const to = new Date(emp.contractEndDate);
        return to >= today;
    });
}

// Nationalitäts-Code für Listen-Filter (Walter 18.07.2026).
// Reihenfolge: Detail-Projektion → FK-Navigation → Legacy-Freitext.
// «Schweiz»/«Switzerland»/… → CH (sonst fielen CH-MA in «Keine Bewilligung»).
function _empNationalityCode(e) {
    const raw = (e.nationalityCode || e.nationalityRef?.code || e.nationality || '')
        .toString().trim().toUpperCase();
    if (!raw) return '';
    if (raw === 'CH' || raw === 'CHE'
        || raw === 'SCHWEIZ' || raw === 'SWITZERLAND'
        || raw === 'SUISSE' || raw === 'SVIZZERA' || raw === 'SWISS')
        return 'CH';
    return raw;
}

// Filter-Registry — ein Eintrag pro Spezialfilter-Option im Dropdown.
// `predicate` liefert true für MA, die im Resultat bleiben sollen.
// `prepare`  optional: async Initialisierung (z.B. Cache befüllen).
// Vertragsmodell des MA bestimmen (Walter 23.08.2026, Modell-Filter):
// gleiche Prioritäten wie das Modell-Badge in der Liste — aktiver Vertrag
// zuerst, sonst neuester. «UTP» ist der Legacy-Alias für FLEX (Rename 08.07.).
function _empModelOf(e) {
    const all = e.employments || [];
    const m = all.find(v => v.isActive)?.employmentModel || all[0]?.employmentModel || '';
    const up = String(m).toUpperCase();
    return up === 'UTP' ? 'FLEX' : up;
}

const EMP_SPECIAL_FILTERS = {
    // Vertragsmodell-Filter (Walter 23.08.2026): ganz oben im Dropdown.
    'model-flex':  { predicate: (e) => _empModelOf(e) === 'FLEX' },
    'model-mtp':   { predicate: (e) => _empModelOf(e) === 'MTP' },
    'model-fix':   { predicate: (e) => _empModelOf(e) === 'FIX' },
    'model-fixm':  { predicate: (e) => _empModelOf(e) === 'FIX-M' },
    // MA mit mind. einer nicht-stornierten Verwarnung (Walter 23.08.2026).
    // Bei jedem Wählen frisch geladen (Verwarnungen können storniert werden).
    'has-verwarnung': {
        prepare: async () => {
            try {
                const r = await fetch('/api/verwarnungen/employee-ids',
                                       { headers: ah(), cache: 'no-store' });
                _empIdsWithVerwarnung = r.ok
                    ? new Set((await r.json()).map(Number))
                    : new Set();
            } catch { _empIdsWithVerwarnung = new Set(); }
        },
        predicate: (e) => _empIdsWithVerwarnung
            && _empIdsWithVerwarnung.has(Number(e.id))
    },
    // MA mit laufender Probezeit (Walter 21.07.2026).
    // Listen-API (/api/employees) liefert probationEndDate am Employment,
    // nicht flach am MA — gleiche Quelle wie Header-Badge «Probezeit bis».
    'in-probezeit': {
        predicate: (e) => {
            const today = new Date().toISOString().slice(0, 10);
            const active = (e.employments || []).filter(c => c.isActive)
                .sort((a, b) => String(b.contractStartDate || '')
                    .localeCompare(String(a.contractStartDate || '')))[0] || null;
            const ende = active?.probationEndDate || e.probationEndDate;
            return !!(ende && String(ende).slice(0, 10) >= today);
        }
    },
    // Aktuelle Krank-/Unfall-/Mutterschafts-Absenz (heute im Zeitraum).
    // Cache bei jedem Öffnen neu laden («nur aktuelle»).
    'in-absence': {
        prepare: async () => {
            try {
                const r = await fetch('/api/absences/employee-ids-current',
                                       { headers: ah(), cache: 'no-store' });
                _empIdsCurrentlyAbsent = r.ok
                    ? new Set((await r.json()).map(Number))
                    : new Set();
            } catch { _empIdsCurrentlyAbsent = new Set(); }
        },
        predicate: (e) => _empIdsCurrentlyAbsent
            && _empIdsCurrentlyAbsent.has(Number(e.id))
    },
    'no-bank': {
        prepare: async () => {
            if (_empIdsWithActiveBank !== null) return;
            try {
                const r = await fetch('/api/employee-bank-accounts/active-employee-ids',
                                       { headers: ah(), cache: 'no-store' });
                _empIdsWithActiveBank = r.ok
                    ? new Set((await r.json()).map(Number))
                    : new Set();
            } catch { _empIdsWithActiveBank = new Set(); }
        },
        predicate: (e) => !(_empIdsWithActiveBank && _empIdsWithActiveBank.has(Number(e.id)))
    },
    // Keine Bewilligung erfasst (Walter 18.05.2026).
    // Ausländer (Nationalität ≠ CH), die noch NIE einen Permit-History-Eintrag
    // hatten. Abgelaufene Bewilligungen sind ein anderer Fall (siehe
    // permit-expired und Dashboard) und werden hier nicht angezeigt.
    // Walter-Bug 18.07.2026: Schweizerinnen mit Klartext «Schweiz» (oder nur
    // nationalityId/FK) landeten fälschlich hier — Liste hatte oft kein
    // nationalityCode, nur Legacy-Freitext.
    'no-permit': {
        prepare: async () => {
            if (_empIdsWithPermitHistory !== null) return;
            try {
                const r = await fetch('/api/employee-permit-history/employee-ids-with-history',
                                       { headers: ah(), cache: 'no-store' });
                _empIdsWithPermitHistory = r.ok
                    ? new Set((await r.json()).map(Number))
                    : new Set();
            } catch { _empIdsWithPermitHistory = new Set(); }
        },
        predicate: (e) => {
            const nat = _empNationalityCode(e);
            if (!nat || nat === 'CH') return false;
            return !_empIdsWithPermitHistory || !_empIdsWithPermitHistory.has(Number(e.id));
        }
    },
    // Bewilligung abgelaufen (Walter 18.05.2026).
    // Quelle = Permit-History (employee.permit_expiry_date entfernt 01.06.2026).
    // Endpoint spiegelt die Dashboard-Auswahl «massgebende Bewilligung».
    'permit-expired': {
        prepare: async () => {
            if (_empIdsWithExpiredPermit !== null) return;
            try {
                const r = await fetch('/api/employee-permit-history/employee-ids-with-expired',
                                       { headers: ah(), cache: 'no-store' });
                _empIdsWithExpiredPermit = r.ok
                    ? new Set((await r.json()).map(Number))
                    : new Set();
            } catch { _empIdsWithExpiredPermit = new Set(); }
        },
        predicate: (e) => _empIdsWithExpiredPermit
            && _empIdsWithExpiredPermit.has(Number(e.id))
    },
    // Quellensteuerpflichtig — hat per heute einen aktiven QST-Eintrag (Walter 18.05.2026).
    'qst-pflichtig': {
        prepare: async () => {
            if (_empIdsWithActiveQst !== null) return;
            try {
                const r = await fetch('/api/employee-quellensteuer/active-employee-ids',
                                       { headers: ah(), cache: 'no-store' });
                _empIdsWithActiveQst = r.ok
                    ? new Set((await r.json()).map(Number))
                    : new Set();
            } catch { _empIdsWithActiveQst = new Set(); }
        },
        predicate: (e) => _empIdsWithActiveQst && _empIdsWithActiveQst.has(Number(e.id))
    },
    // Mit Kinderzulagen — per heute mindestens eine laufende Zulage an einem
    // Kind (Walter 04.09.2026). Cache bei jedem Öffnen neu laden.
    'has-kinderzulage': {
        prepare: async () => {
            try {
                const r = await fetch('/api/family-allowances/employee-ids-current',
                                       { headers: ah(), cache: 'no-store' });
                _empIdsWithKinderzulage = r.ok
                    ? new Set((await r.json()).map(Number))
                    : new Set();
            } catch { _empIdsWithKinderzulage = new Set(); }
        },
        predicate: (e) => _empIdsWithKinderzulage && _empIdsWithKinderzulage.has(Number(e.id))
    },
    // Ohne gültigen Vertrag heute (Walter 18.05.2026).
    // MA als Personalakte (Phantom-MA) oder zwischen zwei Verträgen.
    'no-contract': {
        predicate: (e) => !_empHasActiveContract(e)
    },
};

// Tabs in genau der Reihenfolge wie im Markup (für ←/→-Navigation)
// Adressen wurden in den "Persönliche Angaben"-Tab integriert (ein MA hat in
// der Praxis nur eine Adresse — separate Tab unnötig).
// Walter-Vorgabe 11.06.2026: Mutterschafts-Tab entfernt — wandert komplett in
// den Familie-Tab. Aktive Schwangerschaft wird zusätzlich als roter Badge
// neben dem MA-Namen im Header angezeigt.
// Tab-Struktur (Walter-Vorgabe 15.07.2026, final): «Bewilligung QST Bank»
// (Key 'quellensteuer') enthaelt Bewilligung + QST + Bankverwaltung;
// «Restaurant Admin» (Key 'verwarnungen') enthaelt die Verwarnungen —
// weitere Restaurant-Admin-Funktionen kommen kuenftig dort hinein.
// Menü-Etappe 1 (Zeiten-Kombi) am 17.07.2026 wieder zurückgenommen —
// Stempelzeiten + Absenzen bleiben getrennte Tabs (Walter-Feedback).
// Tab «KTG/UVG» entfernt 17.07.2026 — Tagessatz lebt bei Absenzen + Übersicht.
// «historie» ist KEIN eigener Tab-Pill mehr (Walter 20.08.2026, Platz) —
// erreichbar über die 🕘-Pille im Dokumente-Kopf; switchEmpTab('historie')
// funktioniert weiterhin (Dokumente-Pill bleibt dabei aktiv markiert).
const _empTabsOrder = ['uebersicht', 'familie', 'quellensteuer', 'verwarnungen',
                       'stempelzeiten', 'absenzen', 'verfuegbarkeit', 'zulagen', 'dokumente'];

// Stempelzeiten: persistente Periode-Auswahl über MA-Wechsel hinweg
let _stempelGlobalPeriodeId = null;
let _stempelGlobalYear      = null;
let _stempelGlobalMonth     = null;

// Keyboard-Navigation (einmalig gebunden):
// ↑/↓ = vorheriger/nächster MA, ←/→ = vorheriger/nächster Tab
document.addEventListener('keydown', e => {
    // Nur reagieren wenn wir auf der Mitarbeiter-Seite sind
    const onEmpPage = document.getElementById('page-mitarbeiter')?.classList.contains('active');
    if (!onEmpPage) return;
    // Nicht reagieren wenn der Fokus in einem Eingabefeld ist
    const t = e.target;
    const tag = (t?.tagName || '').toLowerCase();
    if (tag === 'input' || tag === 'textarea' || tag === 'select' || t?.isContentEditable) return;
    // Nicht reagieren wenn ein Modal/Drawer offen ist
    const drawerOpen = document.querySelector('.drawer-open, [id$="Drawer"][style*="display:block"], [id$="Modal"][style*="display:flex"]');
    if (drawerOpen) return;
    // Nicht reagieren wenn Cmd/Ctrl/Alt gedrückt ist
    if (e.metaKey || e.ctrlKey || e.altKey) return;

    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        if (!allEmployees.length) return;
        const idx = allEmployees.findIndex(x => x.id === selectedEmployeeId);
        let next = idx;
        if (e.key === 'ArrowDown') next = idx < 0 ? 0 : Math.min(idx + 1, allEmployees.length - 1);
        if (e.key === 'ArrowUp')   next = idx < 0 ? 0 : Math.max(idx - 1, 0);
        if (next !== idx && allEmployees[next]) {
            e.preventDefault();
            selectEmployee(allEmployees[next].id);
            // gewählten MA in den sichtbaren Bereich scrollen
            setTimeout(() => {
                document.querySelector('.emp-list-item.active')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
            }, 50);
        }
    } else if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
        if (!selectedEmployeeId) return;
        // Alias-Keys auf den aktuellen Key normalisieren (z.B. bank→quellensteuer,
        // zeiten→absenzen nach Rücknahme der Zeiten-Kombi).
        let curTab = activeEmpTab;
        if (curTab === 'bank') curTab = 'quellensteuer';
        if (curTab === 'zeiten' || curTab === 'ktg') curTab = 'absenzen';
        if (curTab === 'personal') curTab = 'uebersicht';
        const idx = _empTabsOrder.indexOf(curTab);
        if (idx < 0) return;
        let next = idx;
        if (e.key === 'ArrowRight') next = Math.min(idx + 1, _empTabsOrder.length - 1);
        if (e.key === 'ArrowLeft')  next = Math.max(idx - 1, 0);
        if (next !== idx) {
            e.preventDefault();
            switchEmpTab(_empTabsOrder[next]);
        }
    }
});

// ── Liste laden ────────────────────────────────
async function loadMitarbeiterList() {
    // (früherer emp-layout-dokumente-Reset entfernt — die MA-Liste bleibt
    // jetzt auf allen Sub-Tabs sichtbar, das spezielle Doku-Layout wurde
    // abgeschafft.)
    try {
        const res = await fetch('/api/employees', { headers: ah(), cache: 'no-store' });
        if (!res.ok) return;
        _empAllRaw = await res.json();
        applyEmpFilter();
    } catch (e) {
        document.getElementById('empList').innerHTML =
            '<div class="emp-no-selection" style="height:200px"><span>Fehler beim Laden</span></div>';
    }
}

/// Wendet den Aktiv-Filter + Filial-Filter an und rendert die Liste neu.
// Ist dieser Vertrag (employment) am Stichtag aktiv? Walter-Vorgabe 23.06.2026.
// Beginn ≤ Stichtag UND (kein Ende ODER Ende ≥ Stichtag).
function _empContractActiveOn(v, refDate) {
    if (!v.contractStartDate) return false;
    const from = new Date(v.contractStartDate);
    if (from > refDate) return false;
    if (!v.contractEndDate) return true;
    const to = new Date(v.contractEndDate);
    return to >= refDate;
}

function applyEmpFilter() {
    // "alt"-Suffix in der Personalnummer = archiviert (unabhängig vom is_active-Flag).
    // Fängt Daten-Inkonsistenzen aus Voll-Migrationen ab (Bis-Datum in Zukunft
    // → is_active=true, aber Nummer "...alt" markiert MA als ehemalig).
    const isArchivedAlt = e => (e.employeeNumber || '').toLowerCase().endsWith('alt');
    const isActiveEffective = e => e.isActive && !isArchivedAlt(e);

    let filtered = _empAllRaw;
    // Walter-Vorgabe 20.08.2026 (Fall Gazale, ersetzt das harte Ausblenden
    // des Heimatfilial-Prinzips vom 05.08.2026): der Status-Filter läuft NEU
    // erst NACH dem Filial-Filter und ist FILIAL-BEWUSST — ein Übertritts-MA
    // ist in der neuen Filiale aktiv, in der alten aber «inaktiv (Übertritt)»
    // und bleibt dort unter Inaktiv/Alle sichtbar (QST-/Lohn-Historie!).
    filtered.forEach(e => { e._branchUebertritt = null; });

    // Phantom-MA ohne Lohn (isPayrollExcluded, z.B. Supervisor Nihat) in den
    // normalen Ansichten AUSBLENDEN (Walter 12.08.2026) — sie sind nur
    // easy@work-Zugänge, kein echtes Personal. Auffindbar bleiben sie unter
    // «Alle» (dort greift auch die Suche).
    if (_empFilter !== 'alle') filtered = filtered.filter(e => !e.isPayrollExcluded);

    // Austrittsdatum-Filter — nur im Inaktive-Modus aktiv. Vergleicht ISO mit ISO
    // (lexikalischer String-Compare reicht für YYYY-MM-DD). MA ohne Austrittsdatum
    // fallen raus, sobald der Filter gesetzt ist.
    if (_empFilter === 'inaktiv' && _empExitDateAfter) {
        const cutoff = _empExitDateAfter; // bereits YYYY-MM-DD aus <input type="date">
        filtered = filtered.filter(e => {
            const x = (e.exitDate || '').slice(0, 10);
            return x && x >= cutoff;
        });
    }

    // Spezial-Filter (Datenqualitäts-Checks) — wirkt zusätzlich zum Aktiv-Status.
    if (_empSpecialFilter && EMP_SPECIAL_FILTERS[_empSpecialFilter]) {
        filtered = filtered.filter(EMP_SPECIAL_FILTERS[_empSpecialFilter].predicate);
    }

    if (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) {
        const cpid = Number(fixedCompanyProfileId);
        // Restaurant-Code → Personalnummer-Präfix (058 → "58", 075 → "75", 104 → "104")
        const branch = (typeof allBranches !== 'undefined' ? allBranches : []).find(b => b.id === cpid);
        const restCode = (branch?.restaurantCode || '').replace(/^0+/, '');  // führende Nullen weg
        const today = new Date();
        today.setHours(0, 0, 0, 0);
        filtered = filtered.filter(e => {
            const emps = e.employments || [];
            // Legacy-Fallback: alle Verträge ohne Filial-Zuordnung → in jeder Filiale anzeigen
            if (emps.length && emps.every(v => !v.companyProfileId)) return true;
            // MA OHNE Verträge: anzeigen, wenn die Personalnummer zum Filial-Präfix passt
            // (z.B. 750xxx → Sursee). So tauchen frisch importierte MA ohne Vertrag auf.
            if (!emps.length && restCode && (e.employeeNumber || '').replace(/alt$/i, '').startsWith(restCode)) {
                return true;
            }
            // Testsystem: eine hier erfasste Person hat noch keinen Vertrag.
            if (!emps.length && window._istTestinstanz && !(e.easyAtWorkEmployeeId || e.easyatworkEmployeeId)) {
                return true;
            }
            // ── Heimatfiliale-Prinzip (Walter-Vorgabe 05.08.2026) ────────────
            // Das Dossier wohnt dort, wo der MA ARBEITET: beim Filialwechsel
            // zieht die GANZE Akte in die neue Filiale; die alte Filiale zeigt
            // ihn unter Inaktiv/Alle als «Übertritt» (20.08.2026 Gazale).
            // Beim Austritt bleibt die Heimat = LETZTE Filiale — frühere
            // Filialen bleiben trotzdem als «Übertritt → letzte» sichtbar
            // (Walter 16.10.2026, TF12 Casanova LU→BE: sonst verschwindet er
            // in LU komplett, obwohl Verträge Feb–Sep dort liegen).
            // Parallel laufende/zukünftige Verträge: in allen diesen Filialen.
            const withCp = emps.filter(v => v.companyProfileId);
            if (!withCp.length) return false;
            const laeuft = v => _empContractActiveOn(v, today)
                || (v.contractStartDate && new Date(v.contractStartDate) > today
                    && (!v.contractEndDate || new Date(v.contractEndDate) >= today));
            const activeCpids = [...new Set(withCp.filter(laeuft).map(v => Number(v.companyProfileId)))];
            const filialeLabel = b => b
                ? `${(b.restaurantCode || '').replace(/^0+/, '')} ${b.city || b.branchName || ''}`.trim()
                : 'andere Filiale';
            if (activeCpids.length) {
                // Hier aktiv → sichtbar (Status-Filter entscheidet unten).
                if (activeCpids.includes(cpid)) return true;
                // Walter-Vorgabe 20.08.2026: hatte der MA hier FRÜHER einen
                // Vertrag, bleibt er in dieser Filiale als «Übertritt»
                // sichtbar (zählt als inaktiv) — sonst verliert die alte
                // Filiale jede Spur (Fall Gazale Sursee→Oftringen).
                if (!withCp.some(v => Number(v.companyProfileId) === cpid)) return false;
                const zielB = (typeof allBranches !== 'undefined' ? allBranches : [])
                    .find(b => activeCpids.includes(Number(b.id)));
                e._branchUebertritt = filialeLabel(zielB);
                return true;
            }
            // Nirgends aktiv → Heimat = Filiale des zuletzt beendeten Vertrags;
            // frühere Filialen mit Vertrag bleiben als Übertritt sichtbar.
            const letzte = withCp.slice().sort((a, b) =>
                String(b.contractEndDate || b.contractStartDate || '')
                    .localeCompare(String(a.contractEndDate || a.contractStartDate || '')))[0];
            if (Number(letzte.companyProfileId) === cpid) return true;
            if (!withCp.some(v => Number(v.companyProfileId) === cpid)) return false;
            const zielB = (typeof allBranches !== 'undefined' ? allBranches : [])
                .find(b => Number(b.id) === Number(letzte.companyProfileId));
            e._branchUebertritt = filialeLabel(zielB);
            return true;
        });
    }

    // Status-Filter — filial-bewusst (Walter 20.08.2026): Übertritts-MA
    // zählen in der alten Filiale als inaktiv, obwohl e.isActive=true.
    const istHierAktiv = e => isActiveEffective(e) && !e._branchUebertritt;
    // Anzahl aktive MA der Filiale (Walter 25.08.2026) — VOR dem Status-
    // Filter zählen, damit die Zahl auch bei «Inaktive»/«Alle» stimmt.
    const _anzAktiv = filtered.filter(istHierAktiv).length;
    if (_empFilter === 'aktiv')   filtered = filtered.filter(istHierAktiv);
    if (_empFilter === 'inaktiv') filtered = filtered.filter(e => !istHierAktiv(e));

    // Letzte vergebene Personalnummern der Filiale (Walter 12.07.2026, Versuch):
    // die zwei HÖCHSTEN rein numerischen Nummern mit Filial-Präfix — als
    // Erfassungs-Hilfe für die fortlaufende Nummernvergabe oben rechts neben
    // dem «Mitarbeiter»-Titel (CSS ::after liest data-lastnums). Basis ist
    // _empAllRaw (ALLE MA, unabhängig vom Aktiv-Filter — vergeben ist vergeben);
    // «alt»-Archive und 9999er-Platzhalter zählen nicht.
    try {
        const lnPanel = document.querySelector('#page-mitarbeiter .emp-list-panel');
        if (lnPanel) {
            // Nummernkreis der Filiale (Walter-Vorgabe 02.09.2026). Vorher
            // zählte JEDE Nummer mit Filial-Präfix — auch «122023», also eine
            // mit einer Stelle zu wenig. Die stand dann als «letzte Nr.» da,
            // obwohl sie gar keine gültige Nummer der Filiale ist. Mit dem
            // Kreis zählen nur Nummern mit der richtigen Länge.
            let lnB = null;
            if (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) {
                lnB = (typeof allBranches !== 'undefined' ? allBranches : []).find(x => x.id === Number(fixedCompanyProfileId)) || null;
            }
            const lnKreis = (typeof pnKreisFuer === 'function')
                ? pnKreisFuer(lnB)
                : { praefix: (lnB?.restaurantCode || '').replace(/^0+/, ''), hatLaenge: false,
                    passt(n) { return /^\d+$/.test(n) && !n.startsWith('99')
                                   && (!this.praefix || n.startsWith(this.praefix)); },
                    naechste() { return null; } };
            // Basis: ALLE MA der Filiale, auch Ausgetretene — vergeben ist
            // vergeben (Walter 02.09.2026), eine Nummer wird nie wiederverwendet.
            const lnGueltig = [...new Set(_empAllRaw
                .map(e => (e.employeeNumber || '').trim())
                .filter(n => lnKreis.passt(n))
                .map(Number))]
                .sort((a, b) => b - a);
            const lnTop = lnGueltig.slice(0, 2);
            // Eine Zeile — sonst überlappen lange Nummern das Suchfeld
            // (Walter 18.07.2026).
            // Anzahl aktive MA davor (Walter 25.08.2026) — gleiche Zeile,
            // gleicher Stil wie «letzte Nr.».
            const lnParts = [`${_anzAktiv} aktiv`];
            if (lnTop.length) lnParts.push('letzte Nr. ' + lnTop.join(' · '));
            // Nächste freie Nummer nur zeigen, wenn der Kreis wirklich
            // definiert ist — ohne feste Länge wäre «+1» geraten.
            const lnNext = lnKreis.hatLaenge
                ? (lnGueltig.length ? lnKreis.naechste(lnGueltig[0]) : (lnKreis.praefix + '1'.padStart(lnKreis.stellen, '0')))
                : null;
            if (lnNext) lnParts.push('nächste ' + lnNext);
            lnPanel.setAttribute('data-lastnums', lnParts.join(' · '));
        }
    } catch (_) { /* reine Anzeige-Hilfe */ }

    allEmployees = filtered.sort((a, b) => {
        const na = ((a.firstName ?? '') + ' ' + (a.lastName ?? '')).trim().toLowerCase();
        const nb = ((b.firstName ?? '') + ' ' + (b.lastName ?? '')).trim().toLowerCase();
        return na.localeCompare(nb, 'de');
    });
    // Aktuelle Suche erneut anwenden
    filterEmployeeList();

    // Bei Filial-Wechsel: wenn der bisher gewählte MA nicht mehr in der
    // gefilterten Liste ist, Selektion zurücksetzen (Detail-Panel würde sonst
    // den MA der vorherigen Filiale weiter zeigen — Walter-Feedback 13.05.2026).
    if (selectedEmployeeId && !allEmployees.find(e => e.id === selectedEmployeeId)) {
        selectedEmployeeId = null;
        window.selectedEmployeeId = null;
        selectedEmployee   = null;
        updateLsEmpSyncBtn();
        const panel = document.getElementById('empDetailPanel');
        if (panel) {
            panel.innerHTML = '<div class="emp-no-selection" style="height:100%"><span>Mitarbeiter auswählen</span></div>';
        }
    }

    // Cross-Modul-Sprung (Walter 21.05.2026): der zuletzt fokussierte MA
    // (window.activeEmpId — gesetzt im Lohnlauf, Mitarbeiter UND Verträge) wird
    // beim Betreten der Seite vorselektiert, sofern in der gefilterten Liste.
    // Höchste Priorität: überschreibt eine veraltete Auswahl, damit der Wechsel
    // z.B. aus dem Lohnlauf direkt auf diesen MA springt.
    // Walter-Bug 16.07.2026: auch wenn die ID schon als selektiert gilt, aber
    // das Detail NICHT gerendert ist (selectedEmployee === null — passiert wenn
    // Kuendigungs-/Zeugnis-Seiten die Auswahl setzen, z.B. nach Abbruch im
    // Rueckzugs-Modal), muss selectEmployee laufen — sonst steht der MA zwar
    // markiert in der Liste, rechts aber «Mitarbeiter auswaehlen».
    if (window.activeEmpId
        && (window.activeEmpId !== selectedEmployeeId || !selectedEmployee)
        && allEmployees.find(e => e.id === window.activeEmpId)) {
        selectEmployee(window.activeEmpId);
    }

    // EINMALIGER Reveal-Sprung aus der ⌘K-Suche (Walter 10.07.2026, korrigiert:
    // der frühere Dauer-Umschalter torpedierte die Aktiv/Inaktiv-Buttons, weil
    // er bei JEDEM Filterwechsel zurückschaltete, solange ein verdeckter MA
    // selektiert war). _empRevealEmpId wird in der globalen Suche gesetzt und
    // hier GENAU EINMAL konsumiert: Filter passend umschalten + Detail öffnen;
    // bleibt der MA wegen des Filial-Filters draussen, öffnet nur das Detail.
    if (window._empRevealEmpId) {
        const revealId = window._empRevealEmpId;
        window._empRevealEmpId = null;   // one-shot!
        const t = (_empAllRaw || []).find(e => e.id === revealId);
        if (t) {
            const archived = !t.isActive || (t.employeeNumber || '').toLowerCase().endsWith('alt');
            if (archived && _empFilter === 'aktiv' && typeof setEmpFilter === 'function') {
                setEmpFilter('inaktiv');   // Flag ist schon geleert — keine Endlos-Schleife
            }
            selectEmployee(revealId);
        }
    }

    // Selektion vom Verträge-Tab übernehmen wenn dort einer markiert ist und
    // hier noch keiner — so bleibt der gewählte MA beim Tab-Wechsel selektiert.
    if (!selectedEmployeeId && typeof selectedVtEmployee !== 'undefined' && selectedVtEmployee?.id) {
        if (allEmployees.find(e => e.id === selectedVtEmployee.id)) {
            selectEmployee(selectedVtEmployee.id);
        }
    }
    // Default-Auswahl: wenn nach allen Filtern noch kein MA selektiert ist und
    // die Liste nicht leer ist, ersten MA aktivieren — dann sieht Walter beim
    // Reinklicken sofort Daten statt „Mitarbeiter auswählen".
    if (!selectedEmployeeId && allEmployees.length > 0) {
        selectEmployee(allEmployees[0].id);
    }
}

/// Schaltet zwischen Aktive / Inaktive / Alle um (von den Buttons aufgerufen).
function setEmpFilter(mode) {
    _empFilter = mode;
    const styleActive   = 'flex:1;padding:6px 8px;font-size:11.5px;font-weight:600;background:#3f3f3f;color:white;border:none;cursor:pointer';
    const styleInactive = 'flex:1;padding:6px 8px;font-size:11.5px;font-weight:600;background:#f1f5f9;color:#475569;border:none;cursor:pointer';
    const a = document.getElementById('empFilterAktiv');
    const i = document.getElementById('empFilterInaktiv');
    const all = document.getElementById('empFilterAlle');
    if (a)   a.style.cssText  = (mode === 'aktiv'   ? styleActive : styleInactive) + ';border-radius:6px 0 0 6px';
    if (i)   i.style.cssText  =  mode === 'inaktiv' ? styleActive : styleInactive;
    if (all) all.style.cssText = (mode === 'alle'    ? styleActive : styleInactive) + ';border-radius:0 6px 6px 0';
    // Austrittsdatum-Filter nur im Inaktive-Modus zeigen; beim Wechsel auf
    // Aktive/Alle das Filter-Feld einklappen UND den Filter zurücksetzen
    // (sonst bliebe ein verdeckter Filter aktiv, falls Walter wieder zurückwechselt).
    const exitRow = document.getElementById('empExitDateFilterRow');
    if (exitRow) exitRow.style.display = (mode === 'inaktiv') ? '' : 'none';
    if (mode !== 'inaktiv' && _empExitDateAfter) {
        _empExitDateAfter = '';
        const inp = document.getElementById('empExitDateAfter');
        if (inp) inp.value = '';
    }
    applyEmpFilter();
}

/// Austrittsdatum-Filter setzen (Date-Picker oder „×"-Reset). Wert kommt
/// als YYYY-MM-DD aus dem nativen Date-Input — bei Reset leerer String.
function setEmpExitDateAfter(val) {
    _empExitDateAfter = (val || '').slice(0, 10);
    const inp = document.getElementById('empExitDateAfter');
    if (inp && inp.value !== _empExitDateAfter) inp.value = _empExitDateAfter;
    // Visuelles Feedback: aktiv = blauer Rahmen + heller Hintergrund (analog
    // empSpecialFilter), damit Walter sofort sieht, dass ein Filter greift.
    if (inp) {
        inp.style.borderColor = _empExitDateAfter ? '#3f3f3f' : '#e2e8f0';
        inp.style.background  = _empExitDateAfter ? '#f6f3ee' : '#f8fafc';
        inp.style.color       = _empExitDateAfter ? '#6b7280' : '#475569';
        inp.style.fontWeight  = _empExitDateAfter ? '600' : '400';
    }
    applyEmpFilter();
}

/// Spezial-Filter setzen (Dropdown). Lädt bei Bedarf den passenden Cache
/// (z.B. Bankverbindungs-IDs) bevor neu gefiltert wird.
// Einträge des Filter-Menüs (Walter 04.09.2026: eigenes Aufklappmenü, damit
// alle Filter ohne Scrollen Platz haben). Reihenfolge = Anzeige.
const EMP_SPECIAL_FILTER_OPTIONS = [
    { value: '',               label: 'Alle Mitarbeiter' },
    { value: 'model-flex',     label: 'Vertrag: FLEX' },
    { value: 'model-mtp',      label: 'Vertrag: MTP' },
    { value: 'model-fix',      label: 'Vertrag: FIX' },
    { value: 'model-fixm',     label: 'Vertrag: FIX-M' },
    { value: 'in-probezeit',   label: 'In Probezeit' },
    { value: 'has-verwarnung', label: 'Mit Verwarnung' },
    { value: 'in-absence',     label: 'Krank / Unfall / Mutterschaft (aktuell)' },
    { value: 'no-bank',        label: 'Ohne Bankverbindung' },
    { value: 'no-permit',      label: 'Keine Bewilligung' },
    { value: 'permit-expired', label: 'Bewilligung abgelaufen' },
    { value: 'qst-pflichtig',  label: 'Quellensteuerpflichtig' },
    { value: 'has-kinderzulage', label: 'Mit Kinderzulagen' },
    { value: 'no-contract',    label: 'Ohne Vertrag' },
];

function empSpecialFilterToggle(event) {
    event?.stopPropagation();
    const menu = document.getElementById('empSpecialFilterMenu');
    const btn  = document.getElementById('empSpecialFilter');
    if (!menu || !btn) return;
    const wasOpen = menu.classList.contains('show');
    if (typeof dokCloseAllMenus === 'function') dokCloseAllMenus();
    if (wasOpen) return;
    menu.innerHTML = EMP_SPECIAL_FILTER_OPTIONS.map(o => `
        <button type="button" class="dok-menu-item${o.value === _empSpecialFilter ? ' is-active' : ''}"
                onclick="event.stopPropagation();dokCloseAllMenus();setEmpSpecialFilter('${o.value}')">
            <span style="display:inline-block;width:14px">${o.value === _empSpecialFilter ? '✓' : ''}</span>${esc(o.label)}
        </button>`).join('');
    // Unter dem Knopf, fix positioniert (entkommt dem overflow der Liste);
    // so hoch wie nötig — Scrollen nur, wenn der Bildschirm wirklich zu klein ist.
    const r = btn.getBoundingClientRect();
    menu._dokOrigParent = btn.parentElement;   // dokCloseAllMenus hängt es zurück
    document.body.appendChild(menu);
    menu.style.position = 'fixed';
    menu.style.top = (r.bottom + 4) + 'px';
    menu.style.left = r.left + 'px';
    menu.style.right = 'auto';
    menu.style.minWidth = r.width + 'px';
    menu.style.maxHeight = Math.max(200, window.innerHeight - r.bottom - 12) + 'px';
    menu.style.overflowY = 'auto';
    menu.classList.add('show');
    setTimeout(() => document.addEventListener('click', dokCloseAllMenus, { once: true }), 0);
}

async function setEmpSpecialFilter(value) {
    _empSpecialFilter = value || '';
    const cfg = EMP_SPECIAL_FILTERS[_empSpecialFilter];
    if (cfg && typeof cfg.prepare === 'function') {
        await cfg.prepare();
    }
    // Visuelles Feedback: aktiv = dunkler Rahmen + heller Hintergrund + Label.
    const sel = document.getElementById('empSpecialFilter');
    const lbl = document.getElementById('empSpecialFilterLabel');
    const opt = EMP_SPECIAL_FILTER_OPTIONS.find(o => o.value === _empSpecialFilter);
    if (lbl) lbl.textContent = opt ? opt.label : 'Alle Mitarbeiter';
    if (sel) {
        sel.style.borderColor = _empSpecialFilter ? '#3f3f3f' : '#e2e8f0';
        sel.style.background  = _empSpecialFilter ? '#f6f3ee' : '#f8fafc';
        sel.style.color       = _empSpecialFilter ? '#1a1a1a' : '#475569';
        sel.style.fontWeight  = _empSpecialFilter ? '600' : '400';
    }
    applyEmpFilter();
}

// ── Liste rendern ──────────────────────────────
function renderEmployeeList(employees) {
    const el = document.getElementById('empList');
    if (!employees.length) {
        el.innerHTML = '<div class="emp-no-selection" style="height:200px"><span>Keine Mitarbeiter gefunden</span></div>';
        return;
    }
    el.innerHTML = employees.map(e => {
        const name = ((e.firstName ?? '') + ' ' + (e.lastName ?? '')).trim() || '–';
        const initials = getInitials(e.firstName, e.lastName);
        const isFemale = (e.gender ?? '').toLowerCase().startsWith('w') || (e.gender ?? '').toLowerCase() === 'female';
        const active = e.id === selectedEmployeeId ? 'active liquid-employee-row-active' : '';
        // "alt"-Suffix → archiviert; konsistent mit applyEmpFilter
        const isArchivedAlt = (e.employeeNumber || '').toLowerCase().endsWith('alt');
        // Übertritts-MA (Walter 20.08.2026): in der alten Filiale wie inaktiv
        // darstellen, mit eigenem Label «Übertritt → Ziel-Filiale».
        const isUebertritt = !!e._branchUebertritt;
        const isInactive = !e.isActive || isArchivedAlt || isUebertritt;
        // Modell-Pille (FIX, FIX-M, MTP, UTP) — Walter-Bug 16.05.2026:
        // die frühere Strict-Filterung (matchEmps NUR aus dem aktiven Filial-
        // companyProfileId) hat Legacy-Verträge ohne CompanyProfileId und MA
        // mit Verträgen in anderen Filialen alle als "kein Vertrag" markiert,
        // obwohl sie eigentlich einen aktiven Vertrag haben. Neu: Fallback-
        // Kette zu allen Verträgen des MA, sodass "kein Vertrag" nur dann
        // erscheint, wenn der MA wirklich KEIN Employment hat.
        const cpidActive = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                           ? Number(fixedCompanyProfileId) : null;
        const allEmps    = e.employments || [];
        const filialEmps = cpidActive
            ? allEmps.filter(v => Number(v.companyProfileId) === cpidActive)
            : allEmps;
        const model = filialEmps.find(v => v.isActive)?.employmentModel  // aktiver Vertrag in dieser Filiale
                   || allEmps.find(v => v.isActive)?.employmentModel     // aktiver Vertrag irgendwo
                   || filialEmps[0]?.employmentModel                     // erster Vertrag in dieser Filiale
                   || allEmps[0]?.employmentModel                        // erster Vertrag überhaupt
                   || '';
        let modelBadge = '';
        if (model) {
            const modelClass = {
                MTP: 'emp-model-mtp',
                FLEX: 'emp-model-utp',
                FIX: 'emp-model-fix',
                'FIX-M': 'emp-model-fix-m'
            }[model] || 'emp-model-other';
            modelBadge = `<span class="emp-model-badge liquid-contract-pill ${modelClass}" style="margin-left:auto;font-size:10px;font-weight:600;padding:2px 6px;border-radius:8px;flex-shrink:0;align-self:center">${modelDisplay(model)}</span>`;
        } else if (!isInactive) {
            // aktiv aber ohne Vertrag (kein einziges Employment in der DB) → roter Hinweis
            modelBadge = `<span style="margin-left:auto;font-size:10px;font-weight:600;padding:2px 8px;border-radius:8px;background:#fee2e2;color:#b91c1c;flex-shrink:0;align-self:center">kein Vertrag</span>`;
        }
        // "Kein Lohn"-Pille direkt nach dem Modell-Badge (oder als alleiniges
        // Badge wenn Modell fehlt) — gelb-orange, sticht hervor damit klar ist:
        // dieser MA wird hier nicht abgerechnet.
        let kepLohnBadge = '';
        if (e.isPayrollExcluded) {
            kepLohnBadge = `<span style="font-size:10px;font-weight:600;padding:2px 7px;border-radius:8px;background:#fef3c7;color:#92400e;flex-shrink:0;align-self:center;${modelBadge ? 'margin-left:4px' : 'margin-left:auto'}">kein Lohn</span>`;
        }
        // Schwangerschaft / Mutterschutz (Walter 20.07.2026, präzisiert
        // 12.08.2026) — rosa Pille vor dem Modell-Badge. Geburt erfasst =
        // «Mutterschutz» (16-Wochen-Fenster), sonst «Schwanger».
        let schwangerBadge = '';
        if (e.isPregnant || e.isMaternity) {
            schwangerBadge = e.isMaternity
                ? `<span class="emp-schwanger-badge" title="Mutterschutz — 16 Wochen nach Geburt" style="margin-left:auto">🍼 Mutterschutz</span>`
                : `<span class="emp-schwanger-badge" title="Schwangerschaft aktiv" style="margin-left:auto">🤰 Schwanger</span>`;
            if (modelBadge) modelBadge = modelBadge.replace('margin-left:auto;', 'margin-left:4px;');
            else if (kepLohnBadge) kepLohnBadge = kepLohnBadge.replace('margin-left:auto', 'margin-left:4px');
        }
        return `
        <div class="emp-list-item liquid-employee-row ${active}${(e.isPregnant || e.isMaternity) ? ' emp-list-pregnant' : ''}" onclick="selectEmployee(${e.id})"${isInactive ? ' style="opacity:0.65"' : ''}>
            <div class="emp-avatar ${isFemale ? 'female' : ''}">${initials}</div>
            <div style="flex:1;min-width:0">
                <div class="emp-list-name">${name}${isUebertritt
                    ? ` <span style="color:#b45309;font-weight:600;font-size:11px" title="MA arbeitet jetzt in einer anderen Filiale — Dossier/Historie hier weiterhin einsehbar">(Übertritt → ${esc(e._branchUebertritt)})</span>`
                    : isInactive ? ' <span style="color:#94a3b8;font-weight:400;font-size:11px">(inaktiv)</span>' : ''}</div>
                <div class="emp-list-nr">${e.employeeNumber ?? ''}</div>
            </div>
            ${schwangerBadge}${modelBadge}${kepLohnBadge}
        </div>`;
    }).join('');
}

// ── Suche/Filter ───────────────────────────────
function filterEmployeeList() {
    const q = (document.getElementById('empSearch')?.value ?? '').toLowerCase().trim();
    if (!q) { renderEmployeeList(allEmployees); return; }
    const filtered = allEmployees.filter(e => {
        const name = ((e.firstName ?? '') + ' ' + (e.lastName ?? '')).toLowerCase();
        return name.includes(q) || (e.employeeNumber ?? '').toLowerCase().includes(q);
    });
    renderEmployeeList(filtered);
}

// ── Mitarbeiter auswählen ──────────────────────
async function selectEmployee(id) {
    if (document.getElementById('testNeuMaModal')?.classList.contains('offen')) testNeuMaZu();
    selectedEmployeeId = id;
    // Bug-Fix 17.07.2026: top-level `let` liegt NICHT auf window — der
    // easy@work-Sync-Button im langSwitcher (index.html) und showPage
    // (app-core.js) pruefen aber window.selectedEmployeeId. Spiegeln.
    window.selectedEmployeeId = id;
    // Cross-Modul-Sprung (Walter 21.05.2026): aktiver MA merken, damit
    // Verträge/Lohnlauf-Wechsel auf denselben MA springen.
    window.activeEmpId = id;

    // Aktiven Eintrag nur per Klasse markieren — KEIN kompletter Listen-
    // Rebuild (Walter 18.07.2026: volles Re-render flackerte bei jedem Klick).
    document.querySelectorAll('#empList .emp-list-item').forEach(el => {
        const m = (el.getAttribute('onclick') || '').match(/selectEmployee\((\d+)\)/);
        el.classList.toggle('active', m && parseInt(m[1], 10) === id);
        el.classList.toggle('liquid-employee-row-active', m && parseInt(m[1], 10) === id);
    });

    // Aktiven Eintrag in Sicht scrollen (Walter 21.05.2026)
    const _activeEl = document.querySelector('#empList .emp-list-item.active');
    if (_activeEl && typeof _activeEl.scrollIntoView === 'function') {
        _activeEl.scrollIntoView({ block: 'nearest' });
    }

    // Detail laden — zuerst Stammdaten zeichnen, Neben-Fetches danach
    // (sonst wartet der Screen auf Schwangerschaft/Linked-Docs → Flackern).
    const _selGen = (window._empSelectGen = (window._empSelectGen || 0) + 1);
    try {
        // cache:no-store + ts: nach easy@work-Sync nie eine veraltete
        // Vertrags-Liste aus dem Browser-Cache zeigen (Walter 26.07.2026).
        const res = await fetch(`/api/employees/${id}?_=${Date.now()}`, {
            headers: ah(), cache: 'no-store'
        });
        if (!res.ok || _selGen !== window._empSelectGen) return;
        const emp = await res.json();
        if (_selGen !== window._empSelectGen) return;
        selectedEmployee = emp;
        // Linked-Docs leeren — sonst zeigt der erste Paint noch den VORHERIGEN MA.
        // Nachladen patcht die Doku-Buttons IN PLACE (kein zweites Full-Render
        // der Übersicht → Verträge/KTG-Zeile springt nicht mehr).
        window._linkedDocCodes = new Set();
        window._activePregnancy = null;
        renderEmployeeDetail(emp);
        loadEmployeePhoto(id);
        loadNumberAliases(id);
        if (activeEmpTab && activeEmpTab !== 'uebersicht'
            && typeof switchEmpTab === 'function') {
            switchEmpTab(activeEmpTab);
        }

        // Nebeninfos nachziehen — Badge/Doc-Buttons aktualisieren ohne Full-Flash
        Promise.all([
            fetch(`/api/documents/linked-codes-for-employee?employeeId=${id}`, { headers: ah() })
                .then(r => r.ok ? r.json() : []).catch(() => []),
            IstWeiblich(emp.gender)
                ? fetch(`/api/pregnancies?employeeId=${id}`, { headers: ah() })
                    .then(r => r.ok ? r.json() : []).catch(() => [])
                : Promise.resolve([])
        ]).then(([codes, pregnancies]) => {
            if (_selGen !== window._empSelectGen || selectedEmployeeId !== id) return;
            window._linkedDocCodes = new Set(codes || []);
            window._activePregnancy = null;
            if (IstWeiblich(emp.gender) && pregnancies && pregnancies.length) {
                const heuteIso = new Date().toISOString().slice(0, 10);
                const aktiv = pregnancies.filter(p => {
                    const basis = p.geburtsdatum || p.errechneterTermin;
                    if (!basis) return false;
                    const ende = new Date(basis); ende.setDate(ende.getDate() + 16 * 7);
                    return ende.toISOString().slice(0, 10) >= heuteIso && p.isActive !== false;
                });
                aktiv.sort((a, b) => (b.errechneterTermin || '').localeCompare(a.errechneterTermin || ''));
                window._activePregnancy = aktiv[0] || null;
            }
            // Schwangerschaft-Badge braucht Header-Refresh.
            // Linked-Docs: nur Buttons patchen — KEIN zweites loadUebersichtTab
            // (sonst hüpft Verträge+KTG bei jedem MA-Wechsel).
            if (window._activePregnancy) {
                const keepTab = activeEmpTab;
                renderEmployeeDetail(selectedEmployee);
                if (keepTab && keepTab !== 'uebersicht' && typeof switchEmpTab === 'function')
                    switchEmpTab(keepTab);
                loadEmployeePhoto(id);
                loadNumberAliases(id);
            } else if (typeof _ovPatchLinkedDocButtons === 'function') {
                _ovPatchLinkedDocButtons();
            }
        });
    } catch {}
}

// i18n-Helper: kurz und bequem für die JS-generierten Labels.
// Greift auf window.i18n falls geladen, sonst Fallback auf das deutsche
// Default (zweites Argument). So bleibt die Maske auch bei i18n-Lade-
// Race noch sinnvoll bedient.
function _t(key, fallbackDe) {
    if (!window.i18n || !window.i18n.t) return fallbackDe;
    const v = window.i18n.t(key);
    // i18n.t() gibt bei fehlendem Key den Key selbst zurück — in diesem Fall
    // den deutschen Inline-Default verwenden, damit nichts wie "ma.field.foo"
    // in der UI auftaucht.
    return (v === key) ? fallbackDe : v;
}

// ══════════════════════════════════════════════════════════════════════
// Walter-Vorgabe 07.06.2026: Mitarbeiterfoto im Detail-Header.
// ──────────────────────────────────────────────────────────────────────
// Sobald in der Doku-Struktur ein Typ mit linked_field_code='employee_photo'
// existiert (typisch: „Persönliche Angaben/Mitarbeiterfoto") und für den
// MA ein Bild hochgeladen ist, wird es als runder Avatar links neben dem
// Namen gezeigt. Sonst bleiben die Initialen stehen.
// ══════════════════════════════════════════════════════════════════════
let _empPhotoUrl = null;  // Object-URL — wird beim nächsten Laden revoked
async function loadEmployeePhoto(empId) {
    if (!empId) return;
    const target = document.getElementById('empDetailPhoto');
    if (!target) return;
    // Vorherige Object-URL wieder freigeben (Memory-Leak-Schutz beim
    // schnellen MA-Wechsel über die linke Liste).
    if (_empPhotoUrl) {
        try { URL.revokeObjectURL(_empPhotoUrl); } catch {}
        _empPhotoUrl = null;
    }
    try {
        // Direkt verknüpftes Foto (Ausschnitt, Walter 23.09.2026) hat Vorrang;
        // sonst wie bisher das neueste Dokument vom Typ «Mitarbeiterfoto».
        const direkt = (typeof selectedEmployee !== 'undefined' && String(selectedEmployee?.id) === String(empId))
            ? selectedEmployee?.fotoDokumentId : null;
        let meta;
        if (direkt) {
            meta = { id: direkt, mimeType: 'image/jpeg', filenameOriginal: 'Mitarbeiterfoto' };
        } else {
            const r = await fetch(`/api/documents/by-field?employeeId=${empId}&code=employee_photo`,
                                  { headers: ah() });
            if (!r.ok) return;  // 404 = kein Foto, einfach Initialen lassen
            meta = await r.json();
        }
        if (!meta || !meta.id) return;
        if (String(window.selectedEmployeeId ?? selectedEmployeeId) !== String(empId)) return;
        const mime = (meta.mimeType || '').toLowerCase();
        if (!mime.startsWith('image/')) return;  // nur Bilder einbetten
        // WICHTIG: Backend-Route ist /api/documents/preview/{id} —
        // NICHT /api/documents/{id}/preview (häufiger Tippfehler, 404).
        const img = await fetch(`/api/documents/preview/${meta.id}`,
                                { headers: ah(), cache: 'no-store' });
        if (!img.ok) return;
        const blob = await img.blob();
        _empPhotoUrl = URL.createObjectURL(blob);
        // Falls inzwischen ein anderer MA aktiv ist (User hat schnell
        // weitergeklickt), nicht in den falschen Container schreiben.
        // Walter 06.09.2026: der Container hat bei JEDEM MA dieselbe ID —
        // «existiert noch» reicht nicht, es muss noch DERSELBE MA sein.
        // Sonst landet beim schnellen Weiterklicken das Foto des vorherigen
        // MA im Kopf des nächsten (falsches Foto).
        const stillThere = document.getElementById('empDetailPhoto');
        const nochDerselbe = String(window.selectedEmployeeId ?? selectedEmployeeId) === String(empId);
        if (!stillThere || !nochDerselbe) {
            try { URL.revokeObjectURL(_empPhotoUrl); } catch {}
            _empPhotoUrl = null;
            return;
        }
        stillThere.textContent = '';
        stillThere.style.backgroundImage = `url("${_empPhotoUrl}")`;
        stillThere.style.backgroundColor = 'transparent';
        stillThere.title = meta.filenameOriginal || 'Mitarbeiterfoto';
    } catch {
        // Stillschweigend ignorieren — Initialen-Fallback ist schon im HTML.
    }
}

// ─── Alte Personalnummern (Aliase) im MA-Header (Walter-Vorgabe 21.06.2026) ───
// Ersetzt die früheren starren Felder alt1/alt2 durch eine dynamische Liste.
async function loadNumberAliases(empId) {
    const box = document.getElementById('empNumberAliases');
    if (!box || String(box.dataset.emp) !== String(empId)) return;
    try {
        const r = await fetch(`/api/employees/${empId}/number-aliases`, { headers: ah() });
        if (!r.ok) { box.innerHTML = ''; return; }
        const rows = await r.json();
        renderNumberAliases(empId, rows);
    } catch { box.innerHTML = ''; }
}

function renderNumberAliases(empId, rows) {
    const box = document.getElementById('empNumberAliases');
    if (!box || String(box.dataset.emp) !== String(empId)) return;
    const summary = document.getElementById('empAliasSummaryField');
    const activeNumber = (selectedEmployee?.employeeNumber || '').trim();
    // Dedupe nach Nummer, erste (neueste) Zeile gewinnt — ID fürs Entfernen behalten.
    const seen = new Set();
    const uniq = [];
    for (const a of (rows || [])) {
        const n = (a.number || '').trim();
        if (!n || n === activeNumber || seen.has(n)) continue;
        seen.add(n);
        uniq.push({ id: a.id, number: n, source: a.source || '' });
    }
    if (summary) {
        summary.textContent = uniq.map(a => a.number).join(', ') || '–';
    }
    // Klick auf den Chip entfernt die alte Nummer (Walter 05.08.2026 —
    // Fehlerkorrektur-Aliase wie 10400025 sollen weg können).
    // Sync-/Merge-Aliase sind geschützt (Walter 06.09.2026): sie verbinden den
    // beendeten Filial-Datensatz in easy@work mit dieser Person. Nur manuell
    // erfasste Aliase (oder der Admin) dürfen entfernen.
    const istAdmin = currentUser?.role === 'admin';
    box.innerHTML = uniq
        .map(a => {
            const manuell = (a.source || 'manual').toLowerCase() === 'manual';
            if (manuell || istAdmin)
                return `<span class="emp-old-number" style="cursor:pointer" title="Alte Nummer${manuell ? '' : ' (aus easy@work-Sync)'} — klicken zum Entfernen"
                    onclick="removeNumberAlias(${empId}, ${a.id}, '${esc(a.number)}')">${esc(a.number)}</span>`;
            return `<span class="emp-old-number" style="cursor:help" title="Alte Nummer aus dem easy@work-Sync — wird für die Zuordnung der Filial-Datensätze gebraucht und kann nicht entfernt werden">${esc(a.number)}</span>`;
        })
        .join('');
}

async function removeNumberAlias(empId, aliasId, number) {
    const ok = await (typeof liquidConfirm === 'function'
        ? liquidConfirm(`Alte Personalnummer ${number} aus der Historie entfernen?`,
            { title: 'Alias entfernen', yesLabel: 'Entfernen', noLabel: 'Abbrechen' })
        : Promise.resolve(confirm(`Alte Nummer ${number} entfernen?`)));
    if (!ok) return;
    try {
        const r = await fetch(`/api/employees/${empId}/number-aliases/${aliasId}`, {
            method: 'DELETE', headers: ah()
        });
        if (!r.ok && r.status !== 204) {
            let m = 'Entfernen fehlgeschlagen (HTTP ' + r.status + ').';
            try { const j = await r.json(); if (j.message) m = j.message; } catch {}
            alert(m);
            return;
        }
        loadNumberAliases(empId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function addNumberAlias(empId) {
    const num = prompt('Alte Personalnummer hinzufügen:');
    if (num === null) return;
    const v = num.trim();
    if (!v) return;
    try {
        const r = await fetch(`/api/employees/${empId}/number-aliases`, {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ number: v })
        });
        if (!r.ok) {
            let m = 'Fehler beim Hinzufügen.';
            try { const j = await r.json(); if (j.message) m = j.message; } catch {}
            alert(m); return;
        }
        loadNumberAliases(empId);
    } catch { alert('Verbindungsfehler.'); }
}

async function deleteNumberAlias(empId, aliasId) {
    if (!(await liquidConfirm('Diese alte Nummer wirklich entfernen?'))) return;
    try {
        const r = await fetch(`/api/employees/${empId}/number-aliases/${aliasId}`, { method: 'DELETE', headers: ah() });
        if (!r.ok && r.status !== 204) { alert('Fehler beim Löschen.'); return; }
        loadNumberAliases(empId);
    } catch { alert('Verbindungsfehler.'); }
}

// Rechten Freiraum der Namenszeile an der schwebenden Leiste (#langSwitcher:
// easy@work sync / Suche / Sprache) ausrichten, damit die Status-Pillen
// davor umbrechen statt darunter zu verschwinden (Walter 06.09.2026).
function empHeaderReserveRight() {
    const line = document.getElementById('empDetailNameLine');
    const ls = document.getElementById('langSwitcher');
    if (!line || !ls) return;
    line.style.paddingRight = '0px';
    if (getComputedStyle(ls).display === 'none') return;
    const lr = line.getBoundingClientRect();
    const sr = ls.getBoundingClientRect();
    // Nur relevant, wenn die Leiste in der Höhe der Namenszeile liegt.
    if (sr.bottom < lr.top - 4 || sr.top > lr.bottom + 4) return;
    const overlap = lr.right - sr.left + 14;
    if (overlap > 0) line.style.paddingRight = Math.round(overlap) + 'px';
}
let _empHdrResizeT = null;
window.addEventListener('resize', () => {
    clearTimeout(_empHdrResizeT);
    _empHdrResizeT = setTimeout(empHeaderReserveRight, 120);
});

// ── Arbeitszeugnis in der Anstellung (Walter 06.09.2026) ──────────────
// Bei ausgetretenen MA: verknüpftes Zeugnis-Dokument anzeigen bzw. Pendenz
// «noch kein Arbeitszeugnis» mit «Zeugnis erstellen» + «Zeugnis verknüpfen».
// Die Dashboard-Todo «Arbeitszeugnis ausstellen» verschwindet erst, wenn
// hier ein Dokument verknüpft ist.
function _ovArbeitszeugnisRowHtml(emp) {
    if (!emp || !emp.exitDate) return '';
    const dokId = emp.arbeitszeugnisDokumentId || null;
    const btn = 'background:#3f3f3f;color:#fff;border:none;border-radius:9px;padding:3px 9px;cursor:pointer;font-size:11px;font-weight:700;line-height:1.3';
    const btn2 = 'background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:9px;padding:3px 9px;cursor:pointer;font-size:11px;font-weight:700;line-height:1.3';
    const exitVorbei = new Date(emp.exitDate) < new Date(new Date().toDateString());
    // Doku-Symbol wie überall (Walter 26.09.2026); «erstellen» bleibt eigener Knopf.
    const dokIcon = docIconBtn({ docId: dokId, was: 'Arbeitszeugnis',
        verknuepfen: `openAusweisDokuModal(${emp.id},'arbeitszeugnis')`,
        loesen: `nwUnlinkDoku(${emp.id},'arbeitszeugnis','Arbeitszeugnis')` });
    const inhalt = dokId
        ? `<span style="color:#166534;font-weight:700">✓ ausgestellt</span> ${dokIcon}`
        : `<span style="color:${exitVorbei ? '#9f1239' : '#a16207'};font-weight:700">${exitVorbei ? '⚠ fehlt' : 'offen'}</span>
           <button type="button" style="${btn}" onclick="openZeugnisModal(${emp.id})">📄 erstellen</button> ${dokIcon}`;
    // Sitzt im freien Slot zwischen Austritt und L-GAV (die Anstellungs-Karte
    // hat eine feste Höhe mit overflow:hidden — eine zusätzliche Zeile wäre
    // abgeschnitten).
    return `<div class="ov-pf ov-anst-pz-slot" style="flex-direction:column;align-items:flex-start;justify-content:flex-end;margin-bottom:0">
        <div class="ov-pfl" style="margin-bottom:1px">Arbeitszeugnis</div>
        <div style="display:flex;align-items:center;gap:6px;white-space:nowrap;font-size:12px;line-height:28px;min-height:28px;overflow:hidden">${inhalt}</div>
    </div>`;
}

// Kündigungsschreiben in der Anstellungs-Karte (Walter 25.09.2026): erscheint,
// sobald eine Kündigung erfasst ist oder ein Schreiben hängt. Gleicher freier
// Slot wie das Arbeitszeugnis (die Karte hat eine feste Höhe).
function _ovKuendigungRowHtml(emp) {
    const dokId = emp?.kuendigungDokumentId || null;
    if (!emp || (!dokId && !emp.kuendigungAusgesprochenAm && !emp.kuendigungPer)) return '';
    const btn = 'background:#3f3f3f;color:#fff;border:none;border-radius:9px;padding:3px 9px;cursor:pointer;font-size:11px;font-weight:700;line-height:1.3';
    const btn2 = 'background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:9px;padding:3px 9px;cursor:pointer;font-size:11px;font-weight:700;line-height:1.3';
    // Doku-Symbol wie überall (Walter 26.09.2026)
    const inhalt = docIconBtn({ docId: dokId, was: 'Kündigungsschreiben',
        verknuepfen: `openAusweisDokuModal(${emp.id},'kuendigung')`,
        loesen: `nwUnlinkDoku(${emp.id},'kuendigung','Kündigungsschreiben')` });
    return `<div class="ov-pf ov-anst-pz-slot" style="flex-direction:column;align-items:flex-start;justify-content:flex-end;margin-bottom:0">
        <div class="ov-pfl" style="margin-bottom:1px">Kündigung</div>
        <div style="display:flex;align-items:center;gap:6px;white-space:nowrap;font-size:12px;line-height:28px;min-height:28px;overflow:hidden">${inhalt}</div>
    </div>`;
}

