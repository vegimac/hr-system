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
    // Schweizer PLZ = 4-stellig numerisch.
    const before = el.value;
    const cleaned = before.replace(/\D/g, '').slice(0, 4);
    if (cleaned !== before) {
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
let _empIdsWithActiveQst    = null;   // MA-IDs mit aktivem QST-Tarif
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
            // ── Heimatfiliale-Prinzip (Walter-Vorgabe 05.08.2026) ────────────
            // Das Dossier wohnt dort, wo der MA ARBEITET: beim Filialwechsel
            // zieht die GANZE Akte (inkl. alter Verträge/History) in die neue
            // Filiale um; die alte Filiale zeigt den MA NICHT mehr (auch nicht
            // unter «Alle» — Fall Mirjete Velijaj 104→230). Beim Austritt
            // bleibt das Dossier in der LETZTEN Filiale. MA mit PARALLELEN
            // laufenden/zukünftigen Verträgen in mehreren Filialen erscheinen
            // in allen diesen Filialen.
            const withCp = emps.filter(v => v.companyProfileId);
            if (!withCp.length) return false;
            const laeuft = v => _empContractActiveOn(v, today)
                || (v.contractStartDate && new Date(v.contractStartDate) > today
                    && (!v.contractEndDate || new Date(v.contractEndDate) >= today));
            const activeCpids = [...new Set(withCp.filter(laeuft).map(v => Number(v.companyProfileId)))];
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
                e._branchUebertritt = zielB
                    ? `${(zielB.restaurantCode || '').replace(/^0+/, '')} ${zielB.city || zielB.branchName || ''}`.trim()
                    : 'andere Filiale';
                return true;
            }
            // Nirgends aktiv → Heimat = Filiale des zuletzt beendeten Vertrags.
            const letzte = withCp.slice().sort((a, b) =>
                String(b.contractEndDate || b.contractStartDate || '')
                    .localeCompare(String(a.contractEndDate || a.contractStartDate || '')))[0];
            if (Number(letzte.companyProfileId) !== cpid) return false;
            return true;
        });
    }

    // Status-Filter — filial-bewusst (Walter 20.08.2026): Übertritts-MA
    // zählen in der alten Filiale als inaktiv, obwohl e.isActive=true.
    const istHierAktiv = e => isActiveEffective(e) && !e._branchUebertritt;
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
            let lnPrefix = '';
            if (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) {
                const lnB = (typeof allBranches !== 'undefined' ? allBranches : []).find(x => x.id === Number(fixedCompanyProfileId));
                lnPrefix = (lnB?.restaurantCode || '').replace(/^0+/, '');
            }
            const lnTop = [...new Set(_empAllRaw
                .map(e => (e.employeeNumber || '').trim())
                .filter(n => /^\d+$/.test(n) && !n.startsWith('9999'))
                .filter(n => !lnPrefix || n.startsWith(lnPrefix))
                .map(Number))]
                .sort((a, b) => b - a)
                .slice(0, 2);
            // Eine Zeile — sonst überlappen lange Nummern das Suchfeld
            // (Walter 18.07.2026).
            lnPanel.setAttribute('data-lastnums',
                lnTop.length ? 'letzte Nr. ' + lnTop.join(' · ') : '');
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
async function setEmpSpecialFilter(value) {
    _empSpecialFilter = value || '';
    const cfg = EMP_SPECIAL_FILTERS[_empSpecialFilter];
    if (cfg && typeof cfg.prepare === 'function') {
        await cfg.prepare();
    }
    // Visuelles Feedback: aktiv = blauer Rahmen + heller Hintergrund.
    const sel = document.getElementById('empSpecialFilter');
    if (sel) {
        sel.style.borderColor = _empSpecialFilter ? '#3f3f3f' : '#e2e8f0';
        sel.style.background  = _empSpecialFilter ? '#f6f3ee' : '#f8fafc';
        sel.style.color       = _empSpecialFilter ? '#6b7280' : '#475569';
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
        const r = await fetch(`/api/documents/by-field?employeeId=${empId}&code=employee_photo`,
                              { headers: ah() });
        if (!r.ok) return;  // 404 = kein Foto, einfach Initialen lassen
        const meta = await r.json();
        if (!meta || !meta.id) return;
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
        const stillThere = document.getElementById('empDetailPhoto');
        if (!stillThere) {
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
        uniq.push({ id: a.id, number: n });
    }
    if (summary) {
        summary.textContent = uniq.map(a => a.number).join(', ') || '–';
    }
    // Klick auf den Chip entfernt die alte Nummer (Walter 05.08.2026 —
    // Fehlerkorrektur-Aliase wie 10400025 sollen weg können).
    box.innerHTML = uniq
        .map(a => `<span class="emp-old-number" style="cursor:pointer" title="Alte Nummer — klicken zum Entfernen"
            onclick="removeNumberAlias(${empId}, ${a.id}, '${esc(a.number)}')">${esc(a.number)}</span>`)
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
            alert('Entfernen fehlgeschlagen (HTTP ' + r.status + ').');
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

// ── Detail rendern ─────────────────────────────
function renderEmployeeDetail(emp) {
    const panel = document.getElementById('empDetailPanel');
    const name = ((emp.firstName ?? '') + ' ' + (emp.lastName ?? '')).trim() || '–';
    const entry = emp.entryDate ? formatDate(emp.entryDate) : '–';
    const birthHeader = emp.dateOfBirth
        ? `${formatDate(emp.dateOfBirth)} <span style="color:#94a3b8;font-weight:500">(${calcAge(emp.dateOfBirth)} J.)</span>`
        : '–';
    const exit  = emp.exitDate  ? formatDate(emp.exitDate)  : _t('ma.detail.statusActive', 'Aktiv');
    const nr    = emp.employeeNumber ?? '–';
    // Walter-Vorgabe 14.06.2026: Header-Status MUSS sich an emp.isActive
    // orientieren — vorher zeigte er nur „● Aktiv" wenn kein ExitDate gesetzt
    // war. Wenn ein MA via Re-Import ohne ExitDate deaktiviert wird (z.B.
    // weil er aus dem easy@work-CSV verschwunden ist), entstand sonst ein
    // UI-Widerspruch: Liste/Anstellung-Block „inaktiv", Header „Aktiv".
    // Reihenfolge: Austrittsdatum (mit Austritt:-Label) > „● Inaktiv" (grau)
    // > „● Aktiv" (grün).
    const headerStatusHtml = emp.exitDate
        ? `${_t('ma.detail.exitDate','Austritt')}: ${exit}`
        : (emp.isActive
            ? '<span style="color:#22c55e">● ' + _t('ma.detail.statusActive','Aktiv') + '</span>'
            : '<span style="color:#94a3b8">● ' + _t('ma.detail.statusInactive','Inaktiv') + '</span>');
    // Walter-Vorgabe 07.06.2026: Mitarbeiterfoto im Detail-Header.
    // Initialen als Sofort-Fallback; das echte Foto wird asynchron via
    // loadEmployeePhoto() nachgeladen, falls in der Doku-Struktur ein Typ
    // mit linked_field_code='employee_photo' existiert und ein Bild da ist.
    const initials = ((emp.firstName?.[0] ?? '') + (emp.lastName?.[0] ?? '')).toUpperCase() || '?';
    const isFemale = (emp.gender || '').toLowerCase() === 'female' || (emp.salutation || '').toLowerCase() === 'frau';

    // ── KOPF-CARD (Etappe 1, Walter 17.07.2026, nach Mockup) ──
    // Badges nur wenn zutreffend; Fakten-Zeile: Eintritt · Geburtstag ·
    // Vertrag · Telefon · E-Mail (Wohnort/Kanton bewusst NICHT — stehen in
    // den Karten; Restaurant NICHT — Filiale ist global gewählt).
    const _hcToday = new Date().toISOString().slice(0, 10);
    const _hcActive = (emp.employments || []).filter(c => c.isActive)
        .sort((a, b) => String(b.contractStartDate || '').localeCompare(String(a.contractStartDate || '')))[0] || null;
    const _hcBadges = [];
    if (emp.exitDate)
        _hcBadges.push(`<span class="emp-hbadge hb-exit">${_t('ma.detail.exitDate','Austritt')} ${exit}</span>`);
    else if (emp.isActive)
        _hcBadges.push(`<span class="emp-hbadge hb-ok">● ${_t('ma.detail.statusActive','Aktiv')}</span>`);
    else
        _hcBadges.push(`<span class="emp-hbadge hb-inak">● ${_t('ma.detail.statusInactive','Inaktiv')}</span>`);
    // App-/Postfach-Status (Walter 18.08.2026): sitzt NICHT in der oberen
    // Badge-Zeile (dort ist kein Platz), sondern klein unter der E-Mail.
    if (window._activePregnancy) {
        const _p = window._activePregnancy;
        const _mutTxt = _p.geburtsdatum
            ? `Mutterschaft — Geburt ${formatDate(_p.geburtsdatum)}`
            : (_p.errechneterTermin ? `Mutterschaft — ET ${formatDate(_p.errechneterTermin)}` : 'Mutterschaft');
        _hcBadges.push(`<span class="emp-hbadge hb-mut" style="cursor:pointer" title="Zur Schwangerschaft im Familie-Tab" onclick="switchEmpTab('familie')">🤰 ${_mutTxt}</span>`);
    }
    if (emp.kuendigungPer) {
        const _kd = (emp.kuendigungDurch || '').toUpperCase();
        const _kdLbl = _kd === 'AN' ? ' · durch MA' : _kd === 'AG' ? ' · durch uns' : '';
        _hcBadges.push(`<span class="emp-hbadge hb-kuend">✕ Gekündigt per ${formatDate(emp.kuendigungPer)}${_kdLbl}</span>`);
    }
    // Probezeit-Badge oben: Datum + Status + «eintragen»
    // (Walter 02.08.2026) — nicht mehr in der Anstellung-Karte.
    const _hcPzEnde = _hcActive?.probationEndDate
        ? String(_hcActive.probationEndDate).slice(0, 10) : null;
    const _hcPzAktiv = !!( _hcPzEnde && _hcPzEnde >= _hcToday);
    const _hcPz1Ok = !!(emp.probezeitGespraech1Am && emp.probezeitGespraech1DokumentId);
    if (_hcPzAktiv) {
        const pzStatus = _hcPz1Ok
            ? `<span class="emp-hpz-status ok">✓ erledigt</span>`
            : `<span class="emp-hpz-status open">offen</span>
               <button type="button" class="emp-hpz-btn" onclick="event.stopPropagation();openProbezeitModal(${emp.id})"
                 title="Gesprächsdatum setzen und unterschriebenes Protokoll verknüpfen">→ eintragen</button>`;
        _hcBadges.push(`<span class="emp-hbadge hb-prob">⏳ Probezeit bis ${formatDate(_hcPzEnde)} · ${pzStatus}</span>`);
    }
    if (emp.isPayrollExcluded)
        _hcBadges.push(`<span class="emp-hbadge hb-inak">⛔ MA ohne Lohn</span>`);
    // Zusatz-Angabe je Modell (Walter 17.07.2026): FIX/FIX-M = Pensum %,
    // MTP = garantierte Wochenstunden, FLEX = Wochenstunden (informativ).
    let _hcVertragZusatz = '';
    if (_hcActive) {
        const _m = (_hcActive.employmentModel || '').toUpperCase();
        if ((_m === 'FIX' || _m === 'FIX-M') && _hcActive.employmentPercentage != null)
            _hcVertragZusatz = ' · ' + Number(_hcActive.employmentPercentage) + ' %';
        else if (_m === 'MTP' && (_hcActive.guaranteedHoursPerWeek != null || _hcActive.weeklyHours != null))
            _hcVertragZusatz = ' · ' + Number(_hcActive.guaranteedHoursPerWeek ?? _hcActive.weeklyHours) + ' h/Wo.';
        else if (_m === 'FLEX' && _hcActive.weeklyHours != null)
            _hcVertragZusatz = ' · ' + Number(_hcActive.weeklyHours) + ' h/Wo.';
    }
    // Vertrag als EIGENE fixe Zeile unter dem Namen (Walter 17.07.2026,
    // final): in der Badge-Zeile wrappte die Pille je nach Namenslaenge —
    // jetzt hat jede Info ihren festen Platz, bei jedem MA identisch.
    const _hcVertragLine = _hcActive
        ? `<span class="emp-hbadge hb-vert"><span class="emp-contract-model ${contractModelClass(_hcActive.employmentModel || '')}" style="margin-right:2px">${esc(modelDisplay(_hcActive.employmentModel || '–'))}</span>${esc(_hcActive.jobTitle || _hcActive.jobGroupCode || '')}${_hcVertragZusatz}</span>`
        : `<span class="emp-hbadge hb-inak">kein aktiver Vertrag</span>`;
    const _hcFact = (label, value) => `<div class="emp-hfact"><div class="emp-hfact-l">${label}</div><div class="emp-hfact-v">${value || '<span style="color:#8b8b8b;font-weight:500">–</span>'}</div></div>`;
    // Direkt anrufen / E-Mail schreiben (Walter 19.07.2026) — Icon VOR dem
    // Wert (besser sichtbar), öffnet tel:/mailto:.
    const _hcActIcon = (kind) => kind === 'tel'
        ? `<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.81.36 1.6.68 2.34a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.74.32 1.53.55 2.34.68A2 2 0 0 1 22 16.92z"/></svg>`
        : `<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/></svg>`;
    const _hcPhoneHref = (emp.phoneMobile || '').replace(/[\s\-().]/g, '');
    const _hcPhoneVal = emp.phoneMobile
        ? `<span class="emp-hfact-with-act"><a class="emp-hfact-act" href="tel:${esc(_hcPhoneHref)}" title="Anrufen: ${esc(emp.phoneMobile)}" aria-label="Anrufen">${_hcActIcon('tel')}</a><span class="emp-hfact-txt" title="${esc(emp.phoneMobile)}">${esc(emp.phoneMobile)}</span></span>`
        : null;
    const _appChip = emp.isPayrollExcluded ? '' :
        `<span id="empAppChip" style="display:none;font-size:10.5px;font-weight:600;padding:1px 8px;border-radius:8px;cursor:pointer;margin-top:2px;width:fit-content" onclick="postfachSetupQr(${emp.id})" title="Klick: Link/QR senden"></span>`;
    if (!emp.isPayrollExcluded) requestAnimationFrame(() => pfLoadAppChip(emp.id));
    const _hcEmailVal = emp.email
        ? `<span class="emp-hfact-with-act" style="display:flex;flex-direction:column;align-items:flex-start"><span style="display:flex;align-items:center;gap:6px"><a class="emp-hfact-act" href="mailto:${esc(emp.email)}" title="E-Mail an ${esc(emp.email)}" aria-label="E-Mail schreiben">${_hcActIcon('mail')}</a><span class="emp-hfact-txt" title="${esc(emp.email)}">${esc(emp.email)}</span></span>${_appChip}</span>`
        : null;

    panel.innerHTML = `
    <div class="emp-detail-header">
        <div style="display:flex;align-items:flex-start;justify-content:flex-start;gap:16px">
            <div id="empDetailPhoto"
                 class="emp-avatar ${isFemale ? 'female' : ''}"
                 style="border-radius:50%;flex-shrink:0;background-size:cover;background-position:center;display:flex;align-items:center;justify-content:center;overflow:hidden">${initials}</div>
            <div style="min-width:0;flex:1 1 auto">
                <div class="emp-detail-name" style="display:flex;align-items:baseline;gap:10px;flex-wrap:nowrap;white-space:nowrap;min-width:0">
                    <span>${name}</span>
                    <span style="font-size:16px;font-weight:650;color:#8b8b8b">${nr}</span>
                    <span id="empNumberAliases" data-emp="${emp.id}"></span>
                    ${_hcBadges.join('')}
                </div>
                <div class="emp-hvertrag">${_hcVertragLine}</div>
                <!-- EINE Fakten-Zeile, fixe Spalten: Eintritt|Geburtstag|Telefon|E-Mail -->
                <div class="emp-hfacts">
                    ${_hcFact(_t('ma.detail.entryDate','Eintritt'), emp.entryDate ? entry : null)}
                    ${_hcFact('Geburtstag', emp.dateOfBirth ? `${birthHeader}${linkedDocButton('birth_cert')}` : null)}
                    ${_hcFact(_t('ma.field.phone','Telefon'), _hcPhoneVal)}
                    ${_hcFact('E-Mail', _hcEmailVal)}
                </div>
            </div>
            <!-- Rechte Aktions-Spalte ABSOLUT positioniert (Walter 17.07.2026:
                 «Zeilen und Positionen heilig») — nimmt dem Namen-/Fakten-
                 Bereich keine Breite weg. top:54px = unter der reservierten
                 langSwitcher-Zone (CLAUDE.md). startEmpEdit() ersetzt weiterhin
                 den Inhalt von #empHeaderActions. -->
            <div class="emp-head-right">
            <div id="empTabActionBar" style="display:flex;gap:8px;align-items:center;justify-content:flex-end"></div>
            <div id="empHeaderActions" style="display:flex;gap:8px;flex-wrap:wrap;justify-content:flex-end;align-content:flex-start;min-width:0">
                <!-- Walter 17.07.2026: alle Aktions-Buttons in den Tab
                     «Restaurant Admin» verschoben (_raTilesHtml); easy@work-
                     Sync sitzt global oben im langSwitcher (#lsEmpSyncBtn).
                     Hier bleibt nur das Inline-Speichern; startEmpEdit()
                     ersetzt den Inhalt durch Speichern/Abbrechen. -->
                <button id="empInlineSaveBtn" class="emp-inline-save" onclick="saveEmpEdit()" style="display:none">Speichern</button>
            </div>
            </div>
        </div>
        <div class="emp-detail-tabs">
            <div class="emp-tab active" data-tab="uebersicht" onclick="switchEmpTab('uebersicht')" style="line-height:1.2;text-align:center">${_t('ma.tab.overview','Übersicht')}</div>
            <div class="emp-tab"        data-tab="familie"    onclick="switchEmpTab('familie')" style="line-height:1.2;text-align:center">${_t('ma.tab.family','Familie<br>Schwanger')}</div>
            <div class="emp-tab"        data-tab="quellensteuer" onclick="switchEmpTab('quellensteuer')" style="line-height:1.2;text-align:center">${_t('ma.tab.permitQst','Bewilligung QST<br>Bank')}</div>
            <div class="emp-tab"        data-tab="verwarnungen" onclick="switchEmpTab('verwarnungen')" style="line-height:1.2;text-align:center">${_t('ma.tab.restAdmin','MA<br>Formulare')}</div>
            <div class="emp-tab"        data-tab="stempelzeiten" onclick="switchEmpTab('stempelzeiten')">${_t('ma.tab.timeRecords','Stempelzeiten')}</div>
            <div class="emp-tab"        data-tab="absenzen"   onclick="switchEmpTab('absenzen')" style="line-height:1.2;text-align:center">${_t('ma.tab.absencesKtg','Absenzen /<br>KTG/UVG')}</div>
            <div class="emp-tab"        data-tab="verfuegbarkeit" onclick="switchEmpTab('verfuegbarkeit')" style="line-height:1.2;text-align:center">${_t('ma.tab.availability','Verfügbarkeit')}</div>
            <div class="emp-tab"        data-tab="zulagen"    onclick="switchEmpTab('zulagen')" style="line-height:1.2;text-align:center">${_t('ma.tab.zulagenAbzuege','Zulagen Abzüge<br>Abtretung BVG')}</div>
            <div class="emp-tab"        data-tab="dokumente"  onclick="switchEmpTab('dokumente')" style="line-height:1.2;text-align:center">Dokumente /<br>Historie</div>
        </div>
    </div>
    <div class="emp-detail-body">
        <!-- TAB: Uebersicht (Etappe 2, Walter 17.07.2026) — read-only Karten,
             Bearbeiten weiterhin nur in den Fach-Tabs. -->
        <div class="emp-tab-content active" id="emp-tab-uebersicht">
            <div id="uebersichtContent"><div class="emp-placeholder"><span>Wird geladen…</span></div></div>
        </div>
        <!-- TAB «Persönliche Angaben» entfernt 17.07.2026 (Walter): Inhalt lebt
             in der Übersicht (Personalien/Anstellung/Verträge/Nachtarbeit).
             Alias personal → uebersicht in switchEmpTab. buildEmpEditPersonal
             bleibt als Code erhalten (nicht mehr verdrahtet). -->

        <!-- TAB: Familie -->
        <div class="emp-tab-content" id="emp-tab-familie">
            <div id="familieContent">
                <div class="emp-placeholder">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
                    <span>${_t('ma.loading','Wird geladen...')}</span>
                </div>
            </div>
        </div>

        <!-- TAB: Bewilligung QST Bank (Walter-Vorgabe 15.07.2026, final) —
             Reihenfolge (Walter 19.07.2026): Bank zuoberst, dann Bewilligungen
             + QST — die Bewilligungs-Liste kann lang werden und soll die
             Bank nicht nach unten verdrängen. Tab-Key bleibt 'quellensteuer'. -->
        <div class="emp-tab-content" id="emp-tab-quellensteuer">
            ${!emp.isPayrollExcluded ? `
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between;margin-top:0">
                <span style="display:inline-flex;align-items:center;gap:8px">
                    ${_t('ma.section.bank','Bankverbindung')}
                    <button title="Verknüpfte Dokumente öffnen (Bankkarte / IBAN-Beleg)"
                            onclick="openLinkedDoc('bank_card')"
                            style="background:${(window._linkedDocCodes && window._linkedDocCodes.has('bank_card')) ? '#dcfce7' : '#f8f7f4'};border:1px ${(window._linkedDocCodes && window._linkedDocCodes.has('bank_card')) ? 'solid #86efac' : 'dashed #d5d0c6'};border-radius:6px;padding:2px 7px;cursor:pointer;color:${(window._linkedDocCodes && window._linkedDocCodes.has('bank_card')) ? '#15803d' : '#b3ada1'};display:inline-flex;align-items:center;gap:3px;font-size:11px;font-weight:600;line-height:1;text-transform:none;letter-spacing:0">
                        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                            <polyline points="14 2 14 8 20 8"/>
                            <line x1="16" y1="13" x2="8" y2="13"/>
                            <line x1="16" y1="17" x2="8" y2="17"/>
                            <line x1="10" y1="9" x2="8" y2="9"/>
                        </svg>
                        <span>Doku${(window._linkedDocCodes && window._linkedDocCodes.has('bank_card')) ? ' ✓' : ''}</span>
                    </button>
                </span>
                <!-- „+ Neue Bankverbindung" sitzt im Header (empTabActionBar). -->
                <button class="btn-emp-add" onclick="openBankAccountModal(null)" style="display:none">
                    ${_t('ma.btn.newBank','Neue Bankverbindung')}
                </button>
            </div>
            <div id="bankAccountsContent">
                <div class="emp-placeholder"><span>Wird geladen…</span></div>
            </div>
            ` : `
            <div style="margin:0 0 14px;padding:12px 16px;background:#fef3c7;border:1px solid #fbbf24;border-radius:8px;color:#92400e;font-size:13px;line-height:1.55">
                <strong>⛔ ${_t('ma.phantom.title','MA ohne Lohn')}</strong> — ${_t('ma.phantom.bankDesc','Phantom-MA für easy@work-Zugang. Bankverbindung wird nicht angezeigt — dieser MA hat keinen Vertrag und keine Lohnzahlung.')}
            </div>
            `}
            <div id="quellensteuerContent" style="margin-top:28px">
                <div class="emp-placeholder">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/></svg>
                    <span>${_t('ma.loading','Wird geladen...')}</span>
                </div>
            </div>
        </div>

        <!-- TAB: Restaurant Admin (Walter-Vorgabe 15.07.2026) — enthaelt die
             Verwarnungs-Verwaltung; weitere Admin-Funktionen kommen hier dazu. -->
        <div class="emp-tab-content" id="emp-tab-verwarnungen">
            <div id="verwarnungenContent">
                <div class="emp-placeholder"><span>${_t('ma.loading','Wird geladen...')}</span></div>
            </div>
        </div>

        <!-- TAB: Stempelzeiten -->
        <div class="emp-tab-content" id="emp-tab-stempelzeiten">
            <div id="stempelzeitenContent">
                <div class="emp-placeholder">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                    <span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span>
                </div>
            </div>
        </div>

        <!-- TAB: Absenzen — Walter-Vorgabe 26.05.2026: vom alten kombinierten
             „Absenzen Zulagen Abzüge"-Tab abgetrennt; Zulagen/Abzüge sind
             jetzt ein eigener Tab.
             Walter 17.07.2026: zweispaltig — Absenzen links scrollen,
             KTG/UVG-Tagessatz schmal rechts (Arbeitsplatz Krank/Unfall).
             Walter 25.07.2026: Karenz kompakt unter dem Tagessatz (gleiche Breite). -->
        <div class="emp-tab-content" id="emp-tab-absenzen">
            <div class="abs-ktg-layout">
                <div class="abs-ktg-main">
                    <div class="emp-section-title">${_t('abs.section.absences','Absenzen')}</div>
                    <div id="absenzenContent">
                        <div class="emp-placeholder">
                            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>
                            <span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span>
                        </div>
                    </div>
                </div>
                <aside class="abs-ktg-side" aria-label="Tagessatz und Karenz">
                    <div id="ktgTagessatzSidebar">
                        <div class="emp-placeholder" style="padding:24px 12px"><span>${_t('ma.loading','Wird geladen...')}</span></div>
                    </div>
                    <div id="karenzSidebar"></div>
                </aside>
            </div>
        </div>

        <!-- TAB: Verfügbarkeit (verfügbare Arbeitszeiten, versioniert) -->
        <div class="emp-tab-content" id="emp-tab-verfuegbarkeit">
            <div id="verfuegbarkeitContent">
                <div class="emp-placeholder" style="height:200px">${_t('ma.loading','Wird geladen...')}</div>
            </div>
        </div>

        <!-- TAB: Zulagen & Abzüge -->
        <div class="emp-tab-content" id="emp-tab-zulagen">
            <!-- Lohnschema-Block (Walter 17.08.2026): Standard-Lohnblatt des
                 Vertragsmodells als read-only-Chips, gerendert von
                 loadLohnschemaBlockForModel() in js/lohnschema.js -->
            <div id="empLohnschemaBlock"></div>
            <!-- Uniformen-Depot (Walter Aug 2026) -->
            <div class="emp-section-title" style="display:flex;align-items:center;gap:8px" title="CHF 50 beim 1. Lohn · Rückerstattung bei ordentlichem Austritt">
                <span>Uniformen-Depot</span>
            </div>
            <div id="uniformDepotContent">
                <div class="emp-placeholder"><span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span></div>
            </div>

            <div style="height:1px;background:#e2e8f0;margin:24px 0"></div>

            <!-- Bereich 1: Wiederkehrende Zulagen & Abzüge -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                <span>${_t('abs.section.recurring','Wiederkehrende Zulagen &amp; Abzüge')}</span>
                <span style="font-size:11px;font-weight:400;color:#94a3b8">${_t('abs.section.recurringHint','Werden bei jedem Lohnlauf im Gültigkeitszeitraum automatisch verrechnet')}</span>
            </div>
            <div id="recurringWagesContent">
                <div class="emp-placeholder"><span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span></div>
            </div>

            <div style="height:1px;background:#e2e8f0;margin:24px 0"></div>

            <!-- Bereich 2: Lohnabtretungen (Pfändung / Sozialamt) -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                <span>${_t('abs.section.lohnabtretung','Lohnabtretungen')}</span>
                <span style="font-size:11px;font-weight:400;color:#94a3b8">${(window.i18n && i18n.getLang && i18n.getLang() === 'en') ? 'Wage garnishment or assignment to social welfare — calculated on net pay' : 'Lohnpfändung oder Abtretung an Sozialamt — nach Netto berechnet'}</span>
            </div>
            <div id="lohnAssignmentsContent">
                <div class="emp-placeholder"><span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span></div>
            </div>

            <div style="height:1px;background:#e2e8f0;margin:24px 0"></div>

            <!-- Bereich 3: BVG-Zusatz-Mitgliedschaft (Walter-Vorgabe 26.05.2026):
                 Belohnungs-Programm — Personalentscheid pro MA, versioniert.
                 Walter-Vorgabe 26.05.2026 (nachträglich): ans Ende des Tabs
                 verschoben — selten editiert, gehört untenhin. -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                <span>${_t('abs.section.bvgZusatz','BVG-Zusatz')} <span style="font-weight:400;color:#94a3b8;font-size:12px">${_t('abs.section.bvgZusatzHint','— Belohnungs-Programm, Personalentscheid pro MA')}</span></span>
                <button class="btn-emp-add" onclick="openBvgZusatzModal(null)">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                    ${_t('abs.btn.newBvgZusatz','Mitgliedschaft erfassen')}
                </button>
            </div>
            <div id="bvgZusatzContent">
                <div class="emp-placeholder"><span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span></div>
            </div>
        </div>

<!-- TAB KTG/UVG entfernt 17.07.2026 (Walter): Tagessatz nur noch bei Absenzen
     (Sidebar). Übersicht zeigt seit 19.07.2026 die Saldi-Tabelle. -->

<!-- Mutterschafts-Tab entfernt am 11.06.2026 (Walter-Vorgabe): Modul lebt
     jetzt komplett im Familie-Tab. Die mts*-Funktionen + renderPregnancyCard
     bleiben — werden vom Familie-Tab aufgerufen. -->

<!-- TAB: Dokumente -->
        <div class="emp-tab-content" id="emp-tab-dokumente">
            <div id="empTabDokumente">
                <div class="emp-placeholder" style="height:200px">${_t('ma.loading','Wird geladen...')}</div>
            </div>
        </div>
        <!-- TAB: Historie (Walter 20.08.2026) — read-only Zeitachse aus
             vorhandenen Daten (Verträge, Übertritte, Umzüge, QST, Bewilligungen). -->
        <div class="emp-tab-content" id="emp-tab-historie">
            <div id="historieContent">
                <div class="emp-placeholder" style="height:200px">${_t('ma.loading','Wird geladen...')}</div>
            </div>
        </div>
    </div>`;

    // Tab-Persistenz: vorher aktiven Tab wiederherstellen statt zurück auf "personal".
    // Wenn Walter z.B. "Familie" angezeigt hat und einen anderen MA wählt, bleibt
    // er auf "Familie". switchEmpTab triggert den passenden loadXxx()-Aufruf
    // (Bankverbindung beim personal-Tab, Familie beim familie-Tab usw.).
    // easy@work-Sync-Button oben im langSwitcher (Walter 17.07.2026):
    // sichtbar sobald ein MA gewaehlt ist und die Rolle es erlaubt.
    window._lsEmpSyncAllowed = ['admin','superuser','buchhaltung','user'].includes(currentUser?.role);
    const _lsBtn = document.getElementById('lsEmpSyncBtn');
    if (_lsBtn) _lsBtn.style.display = window._lsEmpSyncAllowed ? 'inline-flex' : 'none';

    switchEmpTab(activeEmpTab || 'uebersicht');
}

// ═══════════ TAB «UEBERSICHT» (Etappe 2, Walter 17.07.2026) ═══════════
// Read-only Karten-Grid nach Prototyp: Personalien · Kontakt & Adresse ·
// Anstellung · Nachtarbeit · Vertraege · Dokumente. Jede Karte hat einen
// Sprung-Pfeil in ihren Fach-Tab — bearbeitet wird NUR dort. Daten kommen
// aus dem bereits geladenen selectedEmployee; nur die Dokumente werden
// nachgeladen (bestehender Endpoint by-employee).
function _ovCard(title, jumpTab, jumpTitle, bodyHtml, extraHeader = '') {
    const jump = jumpTab
        ? `<button class="ov-jump" title="${jumpTitle}" onclick="switchEmpTab('${jumpTab}')">→</button>`
        : '';
    return `<div class="ov-card">
        <div class="ov-card-h"><span>${title}</span><span style="display:flex;gap:8px;align-items:center">${extraHeader}${jump}</span></div>
        ${bodyHtml}
    </div>`;
}
function _ovF(label, value) {
    return `<div class="ov-f"><div class="ov-fl">${label}</div><div class="ov-fv">${value || '<span class="ov-empty">–</span>'}</div></div>`;
}

function loadUebersichtTab() {
    const el = document.getElementById('uebersichtContent');
    const emp = selectedEmployee;
    if (!el || !emp) return;


    // ── Karte Personalien & Adresse (Walter 17.07.2026, 3 fixe Zeilen):
    //    Z1 Anrede·Briefanrede·Zivilstand·Geschlecht — Z2 Adresse (breit)
    //    ·Telefon 2 — Z3 Nationalität·ZEMIS·AHV·Speichern.
    //    Briefanrede/Telefon 2/ZEMIS sind DIREKT HIER editierbar: die
    //    ov-Inputs spiegeln ihre Werte beim Speichern in die (versteckten)
    //    ef-*-Inputs des Personal-Tabs und rufen saveEmpEdit() — derselbe
    //    geprüfte Save-Pfad, keine doppelten DOM-IDs. ──
    const adresse = [emp.street, [emp.zipCode, stripCityCantonSuffix(emp.city)].filter(Boolean).join(' ')].filter(Boolean).join(', ');
    // Edit-Felder im TEXT-Look (Walter 17.07.2026: grosse Input-Kaesten
    // zerstoerten die ruhige Karte): sehen aus wie Werte, nur mit dezenter
    // gestrichelter Unterlinie — erst bei Fokus wird der Rahmen sichtbar.
    const _ovE = (label, id, value, ph = '') => `
        <div class="ov-f"><div class="ov-fl">${label}</div>
        <input id="${id}" class="ov-editin" value="${esc(value)}" placeholder="${ph}" oninput="ovDirty()" title="Klicken zum Bearbeiten"></div>`;
    // Geschlecht kurz als «Sex» w/m/d (Walter 17.07.2026). Anrede redundant.
    // ── Personalien & Adresse: Z1 Ledigname·Briefanrede·Kurzname(ro)·Sex·
    //    Strasse·PLZ·Ort·Kanton(schmal)·Tel 2. Kurzname = easy@work Nickname,
    //    nur Anzeige (Walter 17.07.2026). ──
    const _rel = emp.religion || '';
    const _relOpt = (v, label) => `<option value="${v}" ${_rel === v ? 'selected' : ''}>${label}</option>`;
    const _pf = (label, valueHtml) => `
        <div class="ov-pf"><div class="ov-pfl">${label}</div><div class="ov-pfv">${valueHtml || '<span class="ov-empty">–</span>'}</div></div>`;
    const _pfE = (label, id, value, ph = '', type = 'text', w = 180) => `
        <div class="ov-pf"><div class="ov-pfl">${label}</div>
        <input id="${id}" class="ov-softin" style="width:${w}px" type="${type}" value="${type === 'date' ? toDateInput(value) : esc(value)}" placeholder="${ph}" ${type === 'date' ? 'onchange="ovDirty()"' : 'oninput="ovDirty()"'}></div>`;
    const _g = (emp.gender || '').toLowerCase();
    const gKurz2 = _g.startsWith('m') ? 'M' : (_g.startsWith('f') || _g === 'w' || _g === 'weiblich') ? 'W' : (emp.gender ? 'D' : null);
    const istCH = (emp.nationalityCode || '').toUpperCase() === 'CH';
    // Personalien & Adresse (Walter 19.07.2026):
    // Ein gemeinsames 5-Spalten-Raster über Z1–Z3:
    //   Strasse | PLZ | Ort+Kt. | Telefon 2 | AHV
    //   Briefanrede | Nickname | Sex | Konfession | —
    //   Zivilstand | seit | Ledigname | Nationalität | ZEMIS
    const kPers = _ovCard('Personalien & Adresse', null, '', `
        <div class="ov-pers-body">
        <div class="ov-pers-aligned">
            <div class="ov-pf ov-pf-street" title="${esc(emp.street || '')}">
                <div class="ov-pfl">${_t('ma.field.street','Strasse')}</div>
                <div class="ov-pfv">${esc(emp.street) || '<span class="ov-empty">–</span>'}</div>
            </div>
            <div class="ov-pf ov-pf-plz">
                <div class="ov-pfl">PLZ</div>
                <div class="ov-pfv">${esc(emp.zipCode) || '<span class="ov-empty">–</span>'}</div>
            </div>
            <div class="ov-pf ov-pf-ortkt">
                <div class="ov-ortkt">
                    <div class="ov-pf ov-pf-city" title="${esc(stripCityCantonSuffix(emp.city) || '')}">
                        <div class="ov-pfl">${_t('ma.field.city','Ort')}</div>
                        <div class="ov-pfv">${esc(stripCityCantonSuffix(emp.city)) || '<span class="ov-empty">–</span>'}</div>
                    </div>
                    <div class="ov-pf ov-pf-kt">
                        <div class="ov-pfl">Kt.</div>
                        <div class="ov-pfv">${esc(emp.cantonCode) || '<span class="ov-empty">–</span>'}</div>
                    </div>
                </div>
            </div>
            <div class="ov-pf ov-pf-tel2"><div class="ov-pfl">Telefon 2</div>
            <input id="ov-phone2" class="ov-softin" type="tel" value="${esc(emp.phone2)}" placeholder="+41 79 …" oninput="validatePhone(this);ovDirty()" onblur="validatePhoneBlur(this)"></div>
            <div class="ov-pf ov-pf-ahv">
                <div class="ov-pfl">AHV-Nr.</div>
                <div class="ov-pfv">${esc(emp.ahvNumber ?? emp.socialSecurityNumber)
                    || (typeof ahvQuickPdf === 'function' ? `<button type="button" onclick="ahvQuickPdf(${emp.id})"
                            title="AHV-Anmeldung 318.260 vorbefüllt öffnen (Versicherungsausweis bestellen)"
                            style="height:22px;display:inline-flex;align-items:center;vertical-align:middle;background:#3f3f3f;color:#fff;border:none;border-radius:8px;padding:0 10px;font-size:10.5px;font-weight:600;cursor:pointer;box-shadow:0 1px 4px rgba(60,55,48,0.18);white-space:nowrap">📄 Ausweis bestellen</button>`
                        : '<span class="ov-empty">–</span>')}</div>
            </div>

            <div class="ov-pf"><div class="ov-pfl">${_t('ma.field.letterSalutation','Briefanrede')}</div>
            <input id="ov-letterSalutation" class="ov-softin" type="text" value="${esc(emp.letterSalutation)}" oninput="ovDirty()"></div>
            <div class="ov-pf" title="Kommt aus easy@work (Nickname) — hier nicht editierbar">
                <div class="ov-pfl">${_t('ma.field.shortName','Nickname')}</div>
                <div class="ov-pfv">${esc(emp.shortName) || '<span class="ov-empty">–</span>'}</div>
            </div>
            <div class="ov-pf ov-pf-sex">
                <div class="ov-pfl">Sex</div>
                <div class="ov-pfv">${gKurz2 || '<span class="ov-empty">–</span>'}</div>
            </div>
            <div class="ov-pf ov-pf-konf"><div class="ov-pfl">${_t('ma.field.religion','Konfession')}</div>
            <select id="ov-religion" class="ov-softin" onchange="ovDirty()">
                <option value="">–</option>
                ${_relOpt('evangelisch_reformiert', _t('ma.value.religion.evangelisch_reformiert','Evang.-reformiert'))}
                ${_relOpt('roemisch_katholisch', _t('ma.value.religion.roemisch_katholisch','Röm.-katholisch'))}
                ${_relOpt('christ_katholisch', _t('ma.value.religion.christ_katholisch','Christ-katholisch'))}
                ${_relOpt('andere', _t('ma.value.religion.andere','Andere'))}
                ${_relOpt('keine', _t('ma.value.religion.keine','Keine'))}
            </select></div>
            ${istCH
                ? `<div class="ov-pf ov-pf-z2-empty" aria-hidden="true"></div>`
                // Walter-Vorgabe 20.08.2026: letzte Bewilligung direkt in der
                // MA-Maske (freie Zelle über der ZEMIS-Nr.) — read-only Info
                // aus der jüngsten Permit-History (emp.permitType/Expiry),
                // Klick springt in den Bewilligungs-Tab.
                : `<div class="ov-pf" title="Neueste Bewilligung — Klick öffnet den Tab «Bewilligung QST Bank»"
                        style="cursor:pointer" onclick="switchEmpTab('quellensteuer')">
                    <div class="ov-pfl">Bewilligung</div>
                    <div class="ov-pfv">${emp.permitType
                        // Walter-Vorgabe 20.08.2026 (final): B sticht ins Auge
                        // (fett, Link-Look — Klick springt zur Bewilligung),
                        // «bis dd.mm.jjjj» klein und nicht fett daneben.
                        ? `<b style="font-size:15px;text-decoration:underline;text-underline-offset:3px;text-decoration-color:rgba(60,55,48,0.35)">${esc(emp.permitType.code)}</b>${emp.permitExpiryDate ? `<span style="font-size:11.5px;font-weight:400;color:#8b8b8b"> bis ${formatDate(emp.permitExpiryDate)}</span>` : ''}`
                        : '<span class="ov-empty" style="color:#b91c1c;font-weight:600">– keine erfasst –</span>'}</div>
                </div>`}

            <div class="ov-pf">
                <div class="ov-pfl">${_t('ma.field.maritalStatus','Zivilstand')}</div>
                <div class="ov-pfv">${formatMaritalStatus(emp.zivilstand ?? emp.maritalStatus) || '–'} ${linkedDocButton('marriage_cert')}</div>
            </div>
            <div class="ov-pf"><div class="ov-pfl">${_t('ma.field.maritalSince','Zivilstand seit')}</div>
            <input id="ov-maritalStatusSince" class="ov-softin" type="date" value="${toDateInput(emp.maritalStatusSince)}" onchange="ovDirty()"></div>
            <div class="ov-pf"><div class="ov-pfl">${_t('ma.field.maidenName','Ledigname')}</div>
            <input id="ov-maidenName" class="ov-softin" type="text" value="${esc(emp.maidenName)}" oninput="ovDirty()"></div>
            <div class="ov-pf">
                <div class="ov-pfl">${_t('ma.field.nationality','Nationalität')}</div>
                <div class="ov-pfv">${emp.nationalityName ? `${esc(emp.nationalityName)} <span class="ov-code">(${esc(emp.nationalityCode || '')})</span>` : (esc(emp.nationalityCode ?? emp.nationality) || '–')} ${linkedDocButton('passport')}</div>
            </div>
            ${istCH
                ? `<div class="ov-pf ov-pf-zemis-empty" aria-hidden="true"></div>`
                : `<div class="ov-pf ov-pf-zemis"><div class="ov-pfl">ZEMIS-Nr.</div>
            <input id="ov-zemisNumber" class="ov-softin" type="text" value="${esc(emp.zemisNumber)}" placeholder="${_t('ma.placeholder.zemis','z.B. 12345678.9')}" maxlength="14" oninput="ovDirty()"></div>`}
        </div>
        </div>`,
        `<button id="ovSaveBtn" class="ov-hbtn ov-hbtn-primary ov-savebtn" style="display:none" onclick="ovSave()">Speichern</button>`);

    // Weitere Adressen = eigene Box unten (Walter 02.08.2026).
    // Titelzeile: «Weitere Adressen» [(n)] · Beschreibung (Walter 02.08.2026).
    const kAddr = emp.isPayrollExcluded ? '' : _ovCard(
        `<span id="ovAddrCardTitle">${_t('ma.section.otherAddresses', 'Weitere Adressen')}</span>` +
        `<span id="ovAddrCardCount" class="ov-addr-count"></span>` +
        ` <span class="ov-addr-hint">${_t('ma.section.otherAddrHint', '(z.B. Korrespondenz, Ferienwohnung, Sozialamt — Hauptadresse oben)')}</span>`,
        null, '',
        `<div id="otherAddressesContent"></div>`,
        `<button type="button" class="ov-hbtn" style="padding:4px 12px;font-size:12px" onclick="openEmployeeAddressModal(null)">＋ ${_t('ma.btn.addAddress','Adresse hinzufügen')}</button>`);

    // ── Karte Anstellung (Walter 17.07.2026): L-GAV/<8h-Toggles +
    //    Kuendigungs-Daten mit Auto-Fristberechnung.
    //    Layout (Walter 19.07.2026): 2×2 Datumsfelder links, Toggles
    //    untereinander ganz rechts —
    //    Eintritt | Austritt | L-GAV
    //    Gekündigt am | Kündigung per | < 8 h / Wo. ──
    // Layout (Walter 02.08.2026): Probezeit nur noch als Badge im MA-Kopf.
    //   Zeile 1: Eintritt | Austritt | L-GAV (rechts)
    //   Zeile 2: Gekündigt am | Kündigung per | Kündigung durch | Austrittsgrund
    // < 8 h / Wo. gehört zum FLEX-Vertrag (Vertragsmaske), nicht zur Anstellung.
    const kAnst = _ovCard('Anstellung', null, '', `
        <div class="ov-anst-grid">
            <div class="ov-anst-top">
                <div class="ov-pf ov-anst-datum"><div class="ov-pfl">${_t('ma.detail.entryDate','Eintritt')}</div><div class="ov-pfv">${emp.entryDate ? formatDate(emp.entryDate) : '<span class="ov-empty">–</span>'}</div></div>
                <div class="ov-pf ov-anst-datum"><div class="ov-pfl">${_t('ma.detail.exitDate','Austritt')}</div><div class="ov-pfv">${emp.exitDate ? formatDate(emp.exitDate) : '<span class="ov-empty">–</span>'}</div></div>
                <div class="ov-pf ov-anst-tog"><div class="ov-pfl">L-GAV</div><div class="ov-pfv">${yesNoToggle('ov-lgavPflichtig', !!emp.lgavPflichtig)}</div></div>
            </div>
            <div class="ov-anst-kuend-row">
                <div class="ov-pf ov-anst-date ov-anst-kuend"><div class="ov-pfl">Gekündigt am</div>
                <input id="ov-kuendAm" class="ov-softin" type="date" value="${toDateInput(emp.kuendigungAusgesprochenAm)}" onchange="ovKuendAmChanged(${emp.id})"></div>
                <div class="ov-pf ov-anst-date ov-anst-kuend"><div class="ov-pfl">Kündigung per</div>
                <input id="ov-kuendPer" class="ov-softin" type="date" value="${toDateInput(emp.kuendigungPer)}" onchange="ovDirty()"></div>
                <div class="ov-pf ov-anst-date ov-anst-kuend"><div class="ov-pfl">Kündigung durch</div>
                <select id="ov-kuendDurch" class="ov-softin" onchange="ovDirty()">
                    <option value="">—</option>
                    <option value="AG"${(emp.kuendigungDurch || '').toUpperCase() === 'AG' ? ' selected' : ''}>durch uns</option>
                    <option value="AN"${(emp.kuendigungDurch || '').toUpperCase() === 'AN' ? ' selected' : ''}>durch Mitarbeiter</option>
                </select></div>
                <div class="ov-pf ov-anst-date ov-anst-kuend"><div class="ov-pfl">Austrittsgrund</div>
                <select id="ov-austrittsgrund" class="ov-softin" onchange="ovDirty()">${_austrittsgrundOptionsHtml(emp.austrittsgrund)}</select></div>
            </div>
        </div>`,
        `<button class="ov-hbtn ov-hbtn-primary ov-savebtn" style="display:none" onclick="ovSave()">Speichern</button>`);
    // ── Karte Nachtarbeit (Walter 17.07.2026): der VOLLE Funktions-Block
    //    aus dem frueheren Personal-Tab lebt jetzt HIER (einzige Instanz,
    //    keine DOM-ID-Dubletten): Status mit Ablauf-Warnung, Drucken-Buttons,
    //    Zeugnis-/Ausnahme-Anzeige, ⋮-Menue (Datum bearbeiten, verknuepfen,
    //    loesen) + Inline-Datum-Edit. ──
    // Pflicht-Badge: grün/rot (ArGV1 Art. 30). Layout in festen Zeilen
    // (Walter 19.07.2026), damit Badge/Nächte/Datum/Warnungen/Buttons
    // nicht je nach Inhalt springen.
    const kNacht = _ovCard('Nachtarbeit', null, '', `
            <div class="nw-card-body">
                <div id="nwView_${emp.id}" class="nw-layout">
                    <div class="nw-row nw-row1">
                        ${_nwDutyBadgeOnlyHtml(emp)}
                        ${_nwDutyCountHtml(emp)}
                        <div class="nw-col-menu">
                            <div class="dok-menu-wrap">
                                <button class="dok-menu-btn" onclick="nwToggleMenu(event, ${emp.id})" title="Aktionen">⋮</button>
                                <div class="dok-menu" id="nwMenu-${emp.id}">
                                    <button class="dok-menu-item" onclick="nwStartEdit(${emp.id})">Ausstellungsdatum bearbeiten</button>
                                    <button class="dok-menu-item" onclick="openAusweisDokuModal(${emp.id},'night_work_exam')">${emp.nightWorkExamDokumentId ? 'ArztZeug. ersetzen' : 'ArztZeug. verknüpfen'}</button>
                                    ${emp.nightWorkExamDokumentId ? `<button class="dok-menu-item" onclick="nwUnlinkDoku(${emp.id},'night_work_exam','Arztzeugnis')">ArztZeug. lösen</button>` : ''}
                                    <button class="dok-menu-item" onclick="openAusweisDokuModal(${emp.id},'night_work_ausnahme')">${emp.nightWorkAusnahmeDokumentId ? 'Ausn.Reg. ersetzen' : 'Ausn.Reg. verknüpfen'}</button>
                                    ${emp.nightWorkAusnahmeDokumentId ? `<button class="dok-menu-item" onclick="nwUnlinkDoku(${emp.id},'night_work_ausnahme','Ausnahmeregelung')">Ausn.Reg. lösen</button>` : ''}
                                </div>
                            </div>
                        </div>
                    </div>
                    <div class="nw-row nw-row2">
                        <div class="nw-dates" id="nwViewText_${emp.id}">${_nwViewTextHtml(emp.nightWorkExamIssued || (emp.nightWorkExamValidUntil ? _nwAddYears(emp.nightWorkExamValidUntil, -2) : null), emp.nightWorkExamValidUntil, emp.nightWorkExamMismatch, emp.nightWorkExamSollBis, emp.nightWorkExamDokumentId)}</div>
                    </div>
                    ${(() => {
                        // Fehlende Docs = rot; vollständig + gültig = grüne «Alles in Ordnung»
                        // (Walter 26.07.2026 — Alarm-Rot nur wenn wirklich etwas fehlt).
                        const status = _nwStatusChipsHtml(emp);
                        return status
                            ? `<div class="nw-row nw-row3"><div class="nw-warns">${status}</div></div>`
                            : '';
                    })()}
                    <div class="nw-row nw-row4 nw-actions">
                        <button class="nw-act-btn" onclick="openNachtEignungPdf(${emp.id})" title="Ärztliches Untersuchungsformular (SECO) drucken">🖨 Arztformular</button>
                        <button class="nw-act-btn" onclick="openNachtAusnahmePdf(${emp.id})" title="Ausnahmeregelung Tag-/Nachtarbeit drucken">🖨 Ausn. Reg.</button>
                        ${emp.nightWorkExamDokumentId
                            ? `<button class="nw-act-btn nw-act-view" onclick="qstOpenBefreiungsDok(${emp.id}, ${emp.nightWorkExamDokumentId})" title="Hinterlegtes Arztzeugnis anzeigen">👁 Arztzeugnis</button>`
                            : ''}
                        ${emp.nightWorkAusnahmeDokumentId
                            ? `<button class="nw-act-btn nw-act-view" onclick="qstOpenBefreiungsDok(${emp.id}, ${emp.nightWorkAusnahmeDokumentId})" title="Hinterlegte Ausnahmeregelung anzeigen">👁 Ausn. Reg.</button>`
                            : ''}
                    </div>
                </div>
                <div id="nwEdit_${emp.id}" class="nw-edit" style="display:none">
                    <span class="nw-edit-label">Ausgestellt</span>
                    <input type="date" id="nwDateInput_${emp.id}" value="${emp.nightWorkExamIssued ? String(emp.nightWorkExamIssued).slice(0,10) : (emp.nightWorkExamValidUntil ? _nwAddYears(emp.nightWorkExamValidUntil, -2) : '')}"
                           oninput="nwPreview(${emp.id}, this.value)"
                           title="Ausstellungsdatum des Arztzeugnisses"
                           class="nw-edit-input">
                    <span id="nwGueltigBis_${emp.id}">${_nwGueltigBisHtml(emp.nightWorkExamValidUntil, emp.nightWorkExamDokumentId)}</span>
                    <button onclick="nwSaveEdit(${emp.id})" class="nw-edit-save">Speichern</button>
                    <button onclick="nwCancelEdit(${emp.id})" class="nw-edit-cancel">Abbrechen</button>
                </div>
            </div>
`);

    // ── Karte Vertraege (alle — Personal-Tab entfernt, kein «Alle anzeigen») ──
    const contracts = (emp.employments || []).slice()
        .sort((a, b) => String(b.contractStartDate || '').localeCompare(String(a.contractStartDate || '')));
    const vRows = contracts.map(c => {
        const von = c.contractStartDate ? formatDate(c.contractStartDate) : '–';
        const bis = c.contractEndDate ? formatDate(c.contractEndDate) : 'offen';
        const pensum = empContractPensumText(c);
        const lohn = empContractWageText(c);
        const actions = _empContractActionsHtml(emp, c, contracts);
        // Punkt: offen/laufend = grün, beendet = grau (unabhängig von den Aktions-Buttons)
        const laufend = !_empContractIsEnded(c);
        const metaExtra = [pensum, lohn].filter(Boolean).map(t => ' · ' + esc(t)).join('');
        return `<div class="ov-vrow${laufend ? '' : ' archiv'}">
            <span class="ov-vdot${laufend ? ' g' : ''}"></span>
            <span class="emp-contract-model ${contractModelClass(c.employmentModel || '')}">${esc(modelDisplay(c.employmentModel || '–'))}</span>
            <span class="ov-vrole">${esc(c.jobTitle || c.jobGroupCode || 'Vertrag')}</span>
            <span class="ov-vmeta">${von} – ${bis}${metaExtra}</span>
<!-- SMS-/Öffnungs-Status entfernt (Walter 10.08.2026): die Info lebt jetzt
     in der ONBOARDING-Auswertung im HR-Hub. loadOvVertragSms bleibt als
     toter Code erhalten, wird aber nicht mehr aufgerufen. -->
            ${actions}
        </div>`;
    }).join('') || '<div class="ov-empty" style="padding:4px 0">Keine Verträge vorhanden.</div>';
    // Feste 3-Zeilen-Hoehe (Walter 17.07.2026): 4 passten nicht ohne Masken-
    // Scroll. Mehr Verträge → nur die innere Liste scrollt, Maske bleibt fix.
    const vList = `<div class="ov-vlist">${vRows}</div>`;
    // SMS-/Link-Feedback-Container fuer die Uebersicht (Klasse statt ID —
    // der Personal-Strip hat seinen eigenen; siehe contractShareBox-Lookup).
    const vShare = '<div class="contractShareBox" style="margin:4px 0 0"></div>';
    // Verträge breit + rechts Stunden/Saldi-Tabelle (Walter 19.07.2026).
    // KTG/UVG-Tagessatz bleibt im Absenzen-Tab (Sidebar) — nicht mehr hier.
    const kVert = _ovCard(`Verträge <span class="ov-count">${contracts.length}</span>`, null, '', vList + vShare);
    const kSaldi = `<div class="ov-card ov-saldi-card">
        <div class="ov-saldi-head">
            <div class="ov-saldi-switch" role="group" aria-label="Saldi-Zeitraum">
                <button type="button" class="ov-saldi-sw" data-mode="aktuell" onclick="ovSaldiSetMode('aktuell')">aktuell</button>
                <button type="button" class="ov-saldi-sw" data-mode="monat" onclick="ovSaldiSetMode('monat')">Monat</button>
            </div>
        </div>
        <div id="ovSaldiContent" class="ov-saldi-slot">${_ovSaldiSkeletonHtml()}</div>
    </div>`;

    // Dokumente-Karte entfernt (Walter 17.07.2026) — Dokumente haben wie
    // gehabt ihren eigenen Bereich (Tab «Dokumente»).

    // Personalien · Anstellung|Nachtarbeit · Verträge|Saldi · Weitere Adressen
    el.innerHTML = `<div class="ov-wrap">${emp.isPayrollExcluded
        ? `<div class="ov-full">${kPers}</div>`
        : `<div class="ov-full">${kPers}</div>${kAnst}${kNacht}<div class="ov-vertraege-ktg">${kVert}${kSaldi}</div><div class="ov-addr-full">${kAddr}</div>`}</div>`;
    if (!emp.isPayrollExcluded && typeof loadEmployeeAddressesTab === 'function')
        loadEmployeeAddressesTab(emp.id);
    if (!emp.isPayrollExcluded) {
        loadOvSaldi(emp.id);
        // loadOvVertragSms(emp.id);  // entfernt — Status jetzt in der ONBOARDING-Auswertung (HR-Hub)
    }
}

// SMS-/Link-Status in der Vertragszeile (Walter 05.08.2026): NUR wenn
// wirklich eine Vertrags-SMS versendet wurde (sms_log) — Link-only-Erzeugung
// oder gar kein Versand zeigt nichts. Best-effort nach dem Render.
async function loadOvVertragSms(employeeId) {
    try {
        const r = await fetch(`/api/contract-share/status-by-employee?employeeId=${employeeId}`, { headers: ah() });
        if (!r.ok) return;
        const s = await r.json();
        if (!s.lastSmsSentAt || !Array.isArray(s.tokens)) return; // nie eine SMS raus → nichts anzeigen
        const f = ts => {
            const d = new Date(ts);
            return isNaN(d.getTime()) ? '' :
                d.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit', year: '2-digit' }) +
                ' ' + d.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' });
        };
        for (const t of s.tokens) {
            const el = document.getElementById(`ovVsms_${t.employmentId}`);
            if (!el) continue;
            // Kompakt (Walter 05.08.2026): nur Icons + Datum/Zeit, kein Text.
            // 📲 = gesendet · 👁 = geöffnet (PDF abgerufen ergänzt ✓) · 👁 grau = noch nicht.
            let html = `📲 ${f(t.createdAt)}`;
            if (t.openedAt) {
                html += ` · 👁 ${f(t.openedAt)}${t.usedAt ? ' ✓' : ''}`;
            } else {
                html += ' · <span style="color:#b45309">👁 –</span>';
            }
            el.innerHTML = html;
            el.title = 'Vertrags-SMS: gesendet' + (t.openedAt ? ' · Link geöffnet' + (t.usedAt ? ' · PDF abgerufen' : '') : ' · Link noch nicht geöffnet');
        }
    } catch (_) { /* Status ist nur Komfort */ }
}

// Inline-Edit in der Uebersicht (Walter 17.07.2026): Speichern liest ov-*
// direkt (Personal-Tab entfernt — kein Spiegeln mehr auf ef-*).
// Austrittsgrund — kurze Labels (Walter 26.07.2026). Codes = Backend AustrittsgrundCodes.
const _AUSTRITTSGRUND = [
    ['AUSBILDUNG', 'Ausbildung'],
    ['ANDERER_JOB', 'Anderer Job'],
    ['UMZUG', 'Umzug'],
    ['FAMILIE', 'Familie'],
    ['GESUNDHEIT', 'Gesundheit'],
    ['ARBEITSZEITEN', 'Arbeitszeiten'],
    ['LOHN', 'Lohn/Pensum'],
    ['TEAM', 'Team/Führung'],
    ['PROBEZEIT', 'Probezeit'],
    ['LEISTUNG', 'Leistung'],
    ['VERFUEGBARKEIT', 'Verfügbarkeit'],
    ['VERHALTEN', 'Verhalten'],
    ['BEFRISTUNG', 'Befristung'],
    ['PERS_GRUENDE', 'pers. Gründe'],
    ['DIVERS', 'Divers'],
];
function _austrittsgrundOptionsHtml(selected) {
    const cur = (selected || '').toUpperCase();
    let h = '<option value="">—</option>';
    for (const [code, lbl] of _AUSTRITTSGRUND)
        h += `<option value="${code}"${cur === code ? ' selected' : ''}>${lbl}</option>`;
    return h;
}
function ovDirty() {
    document.querySelectorAll('.ov-savebtn').forEach(b => b.style.display = 'inline-flex');
}
async function ovKuendAmChanged(empId) {
    if (typeof kuendAmChanged === 'function') await kuendAmChanged(empId);
    ovDirty();
}
function ovSave() {
    saveEmpEdit();
}


// ⋮-Menue der Kopf-Card — seit 17.07.2026 nicht mehr verdrahtet (Buttons
// leben im Restaurant-Admin-Tab), Funktion bleibt als Code erhalten.
function empHeadMenuToggle(ev) {
    ev.stopPropagation();
    const menu = document.getElementById('empHeadMenu');
    const wasOpen = menu?.classList.contains('show');
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    if (menu && !wasOpen) menu.classList.add('show');
}

function renderEmpContractList(emp) {
    const contracts = (emp.employments || []).slice().sort((a, b) => {
        const ad = a.contractStartDate || '';
        const bd = b.contractStartDate || '';
        return bd.localeCompare(ad);
    });
    if (!contracts.length) {
        return `<div class="emp-contract-strip empty">Keine Verträge vorhanden.</div>`;
    }
    const rows = contracts.map(c => {
        const model = c.employmentModel || '–';
        const from = c.contractStartDate ? formatDate(c.contractStartDate) : '–';
        const to = c.contractEndDate ? formatDate(c.contractEndDate) : 'offen';
        const title = c.jobTitle || c.jobGroupCode || c.position || 'Vertrag';
        const pensum = empContractPensumText(c);
        const wage = empContractWageText(c);
        const active = _empContractIsEnded(c)
            ? `<span class="emp-contract-status">archiviert</span>`
            : `<span class="emp-contract-status active">aktiv</span>`;
        const actions = _empContractActionsHtml(emp, c, contracts);
        const metaExtra = [pensum, wage].filter(Boolean).map(t => ' · ' + esc(t)).join('');
        return `<div class="emp-contract-row">
            <div class="emp-contract-main">
                <span class="emp-contract-model ${contractModelClass(model)}">${esc(modelDisplay(model))}</span>
                <span class="emp-contract-title">${esc(title)}</span>
                ${active}
            </div>
            <div class="emp-contract-meta">${from} – ${to}${metaExtra}${c.probationEndDate ? ' · Probezeit bis ' + formatDate(c.probationEndDate) : ''}</div>
            <div class="emp-contract-actions">${actions}</div>
        </div>`;
    }).join('');
    return `<div class="emp-contract-strip" aria-label="Verträge">
        <div class="emp-contract-head">
            <span>Verträge</span>
            <span>${contracts.length}</span>
        </div>
        <div class="emp-contract-scroll">${rows}</div>
    </div>
    <div class="contractShareBox" style="margin:10px 0 0"></div>`;
}

function contractModelClass(model) {
    return ({
        MTP: 'model-badge-mtp',
        FLEX: 'model-badge-utp',
        FIX: 'model-badge-fix',
        'FIX-M': 'model-badge-fix-m'
    })[model] || '';
}

function empContractWageText(c) {
    const fmt = v => Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    if (c.hourlyRate != null) return `CHF ${fmt(c.hourlyRate)}/h`;
    if (c.monthlySalaryFte != null) return `CHF ${fmt(c.monthlySalaryFte)} / 100%`;
    if (c.monthlySalary != null) return `CHF ${fmt(c.monthlySalary)} / Mt.`;
    return '';
}

// FIX/FIX-M → Pensum «80%»; MTP → Wochenstunden «25/Wo» (Walter 02.08.2026).
function empContractPensumText(c) {
    const m = (c.employmentModel || '').toUpperCase();
    if (m === 'FIX' || m === 'FIX-M') {
        if (c.employmentPercentage == null || c.employmentPercentage === '') return '';
        const n = Number(c.employmentPercentage);
        if (!Number.isFinite(n)) return '';
        return `${Number.isInteger(n) ? n : n.toLocaleString('de-CH', { maximumFractionDigits: 2 })}%`;
    }
    if (m === 'MTP') {
        const h = c.guaranteedHoursPerWeek ?? c.weeklyHours;
        if (h == null || h === '') return '';
        const n = Number(h);
        if (!Number.isFinite(n)) return '';
        return `${Number.isInteger(n) ? n : n.toLocaleString('de-CH', { maximumFractionDigits: 2 })}/Wo`;
    }
    return '';
}

function _empContractIsEnded(c) {
    const today = new Date().toISOString().slice(0, 10);
    const end = c.contractEndDate ? String(c.contractEndDate).slice(0, 10) : '';
    return !!(end && end < today);
}

// Historischer Vertrag = beendet UND es gibt bereits einen neueren Vertrag.
// Dann nur noch Anschauen — kein Bearbeiten / SMS / Link-Reset (Walter 19.07.2026).
function _empContractIsHistorisch(c, allContracts) {
    if (!_empContractIsEnded(c)) return false;
    const start = String(c.contractStartDate || '');
    return (allContracts || []).some(o => String(o.contractStartDate || '') > start);
}

function _empContractActionsHtml(emp, c, allContracts) {
    const cid = c.id ?? c.employmentId;
    if (!cid) return '';
    // ⋮-Menü wie überall (Walter 31.07.2026) — statt vier Text-Buttons.
    const historisch = _empContractIsHistorisch(c, allContracts);
    // Vertrags-SMS nur für ausgewählte Benutzer (Walter 10.08.2026):
    // admin/superuser immer; Rolle user braucht das Häkchen «Vertrags-SMS
    // senden» (Filial-Tab «Unterzeichner»). Server prüft zusätzlich pro Filiale.
    const darfSms = ['admin', 'superuser', 'buchhaltung'].includes(currentUser?.role) || !!currentUser?.canVertragSms;
    const smsItems = darfSms
        ? `<button type="button" class="dok-menu-item" onclick="contractShareSendSms(${emp.id}, ${cid}, '${esc(emp.phoneMobile || '')}')">SMS</button>
           <button type="button" class="dok-menu-item danger" onclick="contractShareRevoke(${cid})">Link löschen</button>`
        : '';
    const items = historisch
        ? `<button type="button" class="dok-menu-item" onclick="openEmpContractPdf(${cid}, false)">Drucken</button>`
        : `<button type="button" class="dok-menu-item" onclick="empContractEdit(${cid}, ${emp.id})">Bearbeiten</button>
           <button type="button" class="dok-menu-item" onclick="openEmpContractPdf(${cid}, false)">Drucken</button>${smsItems}`;
    return `<div class="dok-menu-wrap ov-vmenu" style="margin-left:auto;flex-shrink:0">
        <button type="button" class="dok-menu-btn" onclick="ctrToggleMenu(event, ${cid})" title="Aktionen" aria-label="Aktionen">⋮</button>
        <div class="dok-menu" id="ctrMenu-${cid}">${items}</div>
    </div>`;
}
function ctrToggleMenu(event, id) { rowMenuToggle(event, 'ctr', id); }

async function openEmpContractPdf(contractId, printAfterOpen) {
    const filename = `Arbeitsvertrag_${contractId}.pdf`;
    const ok = await previewUrlFetch(`/api/contracts/employment/${contractId}/pdf`, filename, ah());
    // Unterzeichner-Umschalter in der Vorschau (Walter 23.08.2026) —
    // Funktion lebt global in contracts-edit.js.
    if (ok && typeof vtInjectSignerSelector === 'function') vtInjectSignerSelector(contractId);
    if (ok && printAfterOpen) {
        setTimeout(() => {
            if (typeof filePreviewPrint === 'function') filePreviewPrint();
        }, 450);
    }
}

// Klick-Handler der langSwitcher-Pill (Walter 17.07.2026, Bug-Fix v2):
// MA-Id robust aufloesen — window-Spiegel ODER Modul-Variable — und bei
// fehlender Auswahl eine klare Meldung statt stillem Nichtstun.
function lsEmpSyncClick() {
    const id = window.selectedEmployeeId
        || (typeof selectedEmployeeId !== 'undefined' ? selectedEmployeeId : null);
    if (!id) { alert('Bitte zuerst einen Mitarbeiter auswählen.'); return; }
    easyworkSyncSelectedEmployee(id);
}

async function easyworkSyncSelectedEmployee(empId) {
    if (!empId) return;
    // Spinner auf dem Button zeigen, der tatsaechlich existiert: der alte
    // Header-Button (btnEmpEasyworkSync) ist seit 17.07.2026 entfernt —
    // die Pill oben im langSwitcher (#lsEmpSyncBtn) uebernimmt.
    const btn = document.getElementById('btnEmpEasyworkSync')
        || document.getElementById('lsEmpSyncBtn');
    const oldHtml = btn?.innerHTML;
    // Laufender Sync sichtbar machen (Walter-Vorgabe 13.07.2026): drehender
    // Spinner im Button, solange die API-Calls laufen (kann einige Sekunden
    // dauern — Verträge, Properties, Verfügbarkeit).
    if (btn) { btn.disabled = true; btn.innerHTML = '<span class="eaw-spin"></span> synchronisiere…'; }
    try {
        const res = await fetch(`/api/easywork/employees/cowork/${empId}/sync`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyProfileId: fixedCompanyProfileId || null })
        });
        const data = await res.json().catch(() => ({}));
        if (!res.ok || data.success === false) {
            const errors = data.errors && data.errors.length ? data.errors : [data.message || data.error || 'easy@work-Abgleich fehlgeschlagen.'];
            const notes = (data.notes && data.notes.length) ? '\n\nHinweise:\n' + data.notes.map(n => '• ' + n).join('\n') : '';
            alert('easy@work-Abgleich nicht möglich:\n\n' + errors.map(e => '• ' + e).join('\n') + notes);
            return;
        }

        // Nach Sync immer kanonisch neu laden (Walter-Bug 26.07.2026):
        // Früher: eigener GET + renderEmployeeDetail — konnte von einem noch
        // laufenden selectEmployee-Fetch mit ALTEN Verträgen überschrieben
        // werden (nur Gen-Bump + selectEmployee ist race-sicher). Ausserdem
        // aktualisiert selectEmployee Header/Übersicht/Verträge wie beim
        // manuellen MA-Wechsel (den Workaround «MA vor und zurück»).
        window._empSelectGen = (window._empSelectGen || 0) + 1;
        if (typeof selectEmployee === 'function')
            await selectEmployee(empId);
        if (selectedEmployee && selectedEmployee.id === empId) {
            const idx = allEmployees.findIndex(e => e.id === empId);
            if (idx >= 0) {
                allEmployees[idx] = {
                    ...allEmployees[idx],
                    ...selectedEmployee,
                    employments: [...(selectedEmployee.employments || [])]
                };
                renderEmployeeList(allEmployees);
            }
        }
        // Rückmeldung (Walter-Vorgabe 09.07.2026): Erfolg nur als kurzer,
        // nicht-blockierender Toast. Nur übersprungene Verträge (geschlossene
        // Lohnperiode) bleiben als alert — die muss man gesehen haben.
        const upd = data.updatedFields || [];
        const skipped = data.skippedContracts || [];
        if (skipped.length)
            // Neutrale Überschrift (Walter 10.07.2026): die Liste enthält nicht nur
            // Perioden-Sperren, sondern auch Strict-Fehler (fehlender Lohn-Tarif,
            // Überlappung …) — der konkrete Grund steht in jeder Zeile.
            alert('⚠ Diese Verträge wurden NICHT importiert:\n' + skipped.map(s => '• ' + s).join('\n'));
        else if (typeof showToast === 'function')
            showToast(upd.length ? 'Daten aktualisiert' : 'Keine Änderungen', 'success');

        // Adresswechsel aus easy@work (Walter 08.08.2026): sofort den
        // Umzugs-Dialog öffnen — Umzugsdatum bestätigen, QST folgt automatisch.
        if (upd.includes('Wohnort-Historie (Umzugsdatum bestätigen)')
            && typeof openUmzugModal === 'function') {
            showToast('Neue Adresse aus easy@work — bitte Umzugsdatum bestätigen.', 'success');
            openUmzugModal(empId);
        }

        // Schwangerschaft in easy@work gelöscht → in OneCrew nachfragen
        // (Walter-Vorgabe 27.07.2026). Kein Auto-Delete.
        const orphans = data.orphanedPregnancies || [];
        if (orphans.length) {
            const fmt = (iso) => {
                if (!iso) return '—';
                const s = String(iso).slice(0, 10);
                return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
            };
            const etList = orphans.map(p => fmt(p.errechneterTermin)).join(', ');
            const ja = typeof liquidConfirm === 'function'
                ? await liquidConfirm(
                    `Schwangerschaft in easy gelöscht.\n\nIn OneCrew löschen?\n(ET ${etList})`,
                    { title: 'Schwangerschaft in easy gelöscht', yesLabel: 'Ja, löschen', noLabel: 'Nein, behalten' })
                : confirm(`Schwangerschaft in easy gelöscht.\n\nIn OneCrew löschen?\n(ET ${etList})`);
            if (ja) {
                let okCount = 0;
                for (const p of orphans) {
                    const del = await fetch(`/api/pregnancies/${p.id}`, { method: 'DELETE', headers: ah() });
                    if (del.ok) okCount++;
                    else alert('Löschen fehlgeschlagen (Id ' + p.id + '): ' + (await del.text().catch(() => del.status)));
                }
                window._empSelectGen = (window._empSelectGen || 0) + 1;
                if (typeof selectEmployee === 'function') await selectEmployee(empId);
                if (typeof loadFamilieTab === 'function' && selectedEmployeeId === empId)
                    loadFamilieTab(empId);
                if (typeof showToast === 'function' && okCount)
                    showToast(okCount === 1 ? 'Schwangerschaft gelöscht' : `${okCount} Schwangerschaften gelöscht`, 'success');
            }
        }
    } catch (e) {
        alert('easy@work-Abgleich fehlgeschlagen: ' + (e?.message || e));
    } finally {
        const btn2 = document.getElementById('btnEmpEasyworkSync')
            || document.getElementById('lsEmpSyncBtn');
        if (btn2) { btn2.disabled = false; if (oldHtml) btn2.innerHTML = oldHtml; }
    }
}

// ── Tab wechseln ───────────────────────────────
function switchEmpTab(tab) {
    // Bank ist seit 15.07.2026 Teil von «Bewilligung QST Bank».
    if (tab === 'bank') tab = 'quellensteuer';
    // Zeiten-Kombi zurückgenommen (Walter 17.07.2026): falls noch jemand auf
    // dem Kurzzeit-Key «zeiten» hängt → Absenzen (war der Fokus-Tab).
    // Tab «KTG/UVG» entfernt 17.07.2026 → Absenzen (Tagessatz rechts).
    if (tab === 'zeiten' || tab === 'ktg') tab = 'absenzen';
    // Tab «Persönliche Angaben» entfernt 17.07.2026 → Übersicht.
    if (tab === 'personal') tab = 'uebersicht';
    activeEmpTab = tab;
    document.querySelectorAll('.emp-tab').forEach(t =>
        // Historie hat keinen eigenen Pill (Walter 20.08.2026) — die
        // Dokumente-Pill «Dokumente / Historie» bleibt aktiv markiert.
        t.classList.toggle('active', t.dataset.tab === tab
            || (tab === 'historie' && t.dataset.tab === 'dokumente')));
    document.querySelectorAll('.emp-tab-content').forEach(c =>
        c.classList.toggle('active', c.id === 'emp-tab-' + tab));
    // Übersicht / Dokumente / Stempelzeiten / Absenzen: Detail-Body ohne Scroll
    // (Walter 17./19.07.2026) — Maske fix; nur die jeweilige Liste scrollt.
    document.querySelectorAll('#page-mitarbeiter .emp-detail-body').forEach(b => {
        b.classList.toggle('ov-noscroll', tab === 'uebersicht');
        b.classList.toggle('dok-noscroll', tab === 'dokumente');
        b.classList.toggle('stempel-noscroll', tab === 'stempelzeiten');
        b.classList.toggle('abs-noscroll', tab === 'absenzen');
    });
    // Header-Actions (Inline-Speichern) — Übersicht speichert über ov-savebtn
    // in den Karten; andere Tabs haben eigene Edit-Buttons.
    const headerActions = document.getElementById('empHeaderActions');
    if (headerActions) {
        headerActions.style.display = 'none';
    }
    // Tab-spezifischer „+ Neu"-Button im Header (Walter 01.06.2026, Standard
    // wie Lohn-Tab: Aktionen oben sticky, nicht in der Liste unten).
    const tabBar = document.getElementById('empTabActionBar');
    if (tabBar) {
        const isExcluded = !!selectedEmployee?.isPayrollExcluded;
        const plusIcon = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>';
        if (tab === 'familie') {
            // Button lebt im Familie-Tab-Body (Leerzeile / Listen-Kopf),
            // nicht oben rechts neben den Stammdaten (Walter 27.07.2026).
            tabBar.innerHTML = '';
        } else if (tab === 'quellensteuer') {
            // Bank-Button zuerst (Sektion steht zuoberst, Walter 19.07.2026).
            tabBar.innerHTML = (!isExcluded ? `<button class="btn-emp-add" onclick="openBankAccountModal(null)">${plusIcon} ${_t('ma.btn.newBank','Bankverbindung')}</button>` : '')
                + `<button class="btn-emp-add" onclick="openQstFromTab(null)" style="margin-left:${isExcluded ? '0' : '8px'}">${plusIcon} QST-Eintrag</button>`;
        } else if (tab === 'verwarnungen' && !isExcluded) {
            // Restaurant Admin: Aktionen als Icon-Kacheln IM Tab-Body (Walter
            // 15.07.2026) — oben rechts kollidierten die Buttons mit dem
            // langSwitcher (reservierte Zone, CLAUDE.md).
            tabBar.innerHTML = '';
        } else if (tab === 'absenzen') {
            tabBar.innerHTML = `<button class="btn-emp-add" onclick="openAbsenceModal(null)">${plusIcon} Absenz erfassen</button>`;
        } else if (tab === 'verfuegbarkeit' && !isExcluded) {
            tabBar.innerHTML = `<button class="btn-emp-add" onclick="verfNewForm()">${plusIcon} Neue Verfügbarkeit</button>`;
        } else if (tab === 'dokumente') {
            // Walter-Vorgabe 09.06.2026: „Dokument hochladen" sitzt jetzt im Doku-
            // Body (rechts in der .dok-list-header-Zeile, auf Höhe des Kategorie-
            // Pfads) — nicht mehr oben rechts im Tab-Action-Bar, wo er mit dem
            // langSwitcher kollidierte.
            tabBar.innerHTML = '';
        } else {
            tabBar.innerHTML = '';
        }
    }
    if (tab === 'uebersicht'     && selectedEmployeeId) loadUebersichtTab();
    if (tab === 'familie'        && selectedEmployeeId) loadFamilieTab(selectedEmployeeId);
    if (tab === 'quellensteuer'  && selectedEmployeeId) {
        loadQuellensteuerTab(selectedEmployeeId);
        // Bankverwaltung gehoert zu «Bewilligung QST Bank» (Walter 15.07.2026).
        if (!selectedEmployee?.isPayrollExcluded) loadBankAccountsTab(selectedEmployeeId);
    }
    if (tab === 'verwarnungen'   && selectedEmployeeId) loadVerwarnungenTab(selectedEmployeeId);
    if (tab === 'stempelzeiten'  && selectedEmployeeId) loadStempelzeitenTab(selectedEmployeeId);
    if (tab === 'absenzen'       && selectedEmployeeId) {
        loadAbsenzenTab(selectedEmployeeId);
        // Tagessatz rechts neben den Absenzen (Walter 17.07.2026).
        if (typeof loadKtgTab === 'function') loadKtgTab(selectedEmployeeId);
    }
    if (tab === 'verfuegbarkeit' && selectedEmployeeId && typeof loadVerfuegbarkeitTab === 'function') loadVerfuegbarkeitTab(selectedEmployeeId);
    if (tab === 'zulagen'        && selectedEmployeeId) {
        // Walter-Vorgabe 26.05.2026: BVG-Zusatz + Recurring + Lohnabtretungen
        // teilen sich den neuen „Zulagen & Abzüge"-Tab.
        // Lohnschema-Block (Walter 17.08.2026): Standard-Lohnblatt des
        // Vertragsmodells — Modell aus dem aktiven Vertrag (gleiche Wahl wie
        // das Header-Badge: jüngster aktiver Vertrag).
        if (typeof loadLohnschemaBlockForModel === 'function') {
            const _lsC = ((selectedEmployee?.employments) || []).filter(x => x.isActive)
                .sort((a, b) => String(b.contractStartDate || '').localeCompare(String(a.contractStartDate || '')))[0];
            loadLohnschemaBlockForModel(_lsC?.employmentModel || null);
        }
        if (typeof loadUniformDepotTab === 'function') loadUniformDepotTab(selectedEmployeeId);
        if (typeof loadBvgZusatzTab === 'function') loadBvgZusatzTab(selectedEmployeeId);
        loadRecurringWagesTab(selectedEmployeeId);
        loadLohnAssignmentsTab(selectedEmployeeId);
    }
    if (tab === 'dokumente'      && selectedEmployeeId) loadEmpDokumente(selectedEmployeeId);
    if (tab === 'historie'       && selectedEmployeeId) loadHistorieTab(selectedEmployeeId);

    // MA-Liste links bleibt jetzt auf ALLEN Sub-Tabs sichtbar (auch Dokumente).
    // Walter wollte konsistentes Verhalten zu Stempelzeiten — schnelles
    // Wechseln zwischen MA ohne Tab zu verlassen. Frühere Sonderbehandlung
    // mit emp-layout-dokumente entfernt; falls die Klasse von einer alten
    // Code-Stelle noch dranhängt, hier defensiv weg.
    const empLayout = document.querySelector('.emp-layout');
    if (empLayout) empLayout.classList.remove('emp-layout-dokumente');
}

// ══════════════════════════════════════════════
// HISTORIE-TAB (Walter 20.08.2026) — read-only Zeitachse
// aus vorhandenen Daten, gruppiert nach Jahr. Kein Pflegeaufwand.
// ══════════════════════════════════════════════
async function loadHistorieTab(employeeId) {
    const el = document.getElementById('historieContent');
    if (!el) return;
    try {
        const res = await fetch(`/api/employees/${employeeId}/historie`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder">Historie konnte nicht geladen werden.</div>'; return; }
        const events = await res.json();
        if (!Array.isArray(events) || !events.length) {
            el.innerHTML = '<div class="emp-placeholder">Noch keine Ereignisse vorhanden.</div>';
            return;
        }
        const typColor = t => ({
            eintritt:     '#166534',
            vertrag:      '#1d4ed8',
            vertrag_ende: '#6b7280',
            umzug:        '#b45309',
            umzug_kanton: '#b91c1c',
            qst:          '#7c3aed',
            permit:       '#0e7490',
            nummer:       '#6b7280',
            app:          '#166534',
            austritt:     '#b91c1c'
        }[t] || '#6b7280');
        let html = `<div style="display:flex;align-items:center;justify-content:space-between;gap:10px">
            <div class="emp-section-title" style="margin-top:0;margin-bottom:0">Zeitachse</div>
            <button onclick="switchEmpTab('dokumente')"
                    style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.25);color:#3f3f3f;border-radius:12px;padding:5px 14px;font-size:12.5px;font-weight:600;cursor:pointer">
                ← Dokumente
            </button>
        </div>
        <div style="font-size:12px;color:#8b8b8b;margin-bottom:12px">
            Automatisch aus Verträgen, Wohnort-Historie, QST-Versionen, Bewilligungen und Personalnummern
            zusammengestellt — nichts zu pflegen. Neueste Ereignisse zuoberst.
        </div>`;
        let jahr = null;
        html += '<div style="border-left:2px solid #d5d0c6;margin-left:8px;padding-left:0">';
        events.forEach(ev => {
            const d  = ev.datum ? new Date(ev.datum) : null;
            const j  = d ? d.getFullYear() : 'ohne Datum';
            if (j !== jahr) {
                jahr = j;
                html += `<div style="margin:14px 0 6px -8px;display:flex;align-items:center;gap:8px">
                    <span style="background:#3f3f3f;color:#fff;font-size:11px;font-weight:700;padding:2px 10px;border-radius:10px">${j}</span>
                </div>`;
            }
            const dStr = d ? d.toLocaleDateString('de-CH') : '–';
            const kritisch = ev.typ === 'umzug_kanton';
            html += `
            <div style="display:flex;gap:10px;align-items:flex-start;padding:5px 0 5px 0;margin-left:-5px">
                <span style="width:8px;height:8px;border-radius:50%;background:${typColor(ev.typ)};margin-top:5px;flex-shrink:0"></span>
                <span style="font-size:12px;color:#8b8b8b;width:78px;flex-shrink:0;margin-top:1px">${dStr}</span>
                <span style="font-size:13px;color:${kritisch ? '#b91c1c' : '#3f3f3f'};${kritisch ? 'font-weight:600;' : ''}line-height:1.4">${ev.icon || ''} ${esc(ev.text || '')}</span>
            </div>`;
        });
        html += '</div>';
        el.innerHTML = html;
    } catch {
        el.innerHTML = '<div class="emp-placeholder">Verbindungsfehler.</div>';
    }
}

// ══════════════════════════════════════════════
// QUELLENSTEUER TAB
// ══════════════════════════════════════════════

let qstOpenedFromTab = false;

async function loadQuellensteuerTab(employeeId) {
    const el = document.getElementById('quellensteuerContent');
    if (!el) return;
    // Walter-Vorgabe 13.06.2026: Phantom-MA (IsPayrollExcluded=true) bekommen
    // gar keinen QST-Inhalt — kein Banner, kein Tarif-Editor, kein Bewilligungs-
    // Block. Diese MA werden nicht abgerechnet, also gibt es nichts zu prüfen.
    if (selectedEmployee?.isPayrollExcluded) {
        el.innerHTML = `
        <div style="padding:28px 22px;text-align:center;color:#92400e;background:#fef3c7;border:1px solid #fde68a;border-left:4px solid #f59e0b;border-radius:10px;margin:18px">
            <div style="font-size:18px;margin-bottom:6px">⛔</div>
            <div style="font-weight:600;font-size:13.5px">MA ohne Lohn — keine QST-Prüfung</div>
            <div style="font-size:12px;color:#a16207;margin-top:6px;line-height:1.5">
                Dieser Mitarbeiter ist als „MA ohne Lohn" markiert (Phantom-MA für easy@work-Zugang).<br>
                Es findet keine Lohnabrechnung statt — daher auch keine Quellensteuer-Prüfung.
            </div>
        </div>`;
        return;
    }
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen...</span></div>';
    try {
        // Walter-Vorgabe 26.05.2026: Parallel den QST-Pflicht-Check holen.
        // Walter-Vorgabe 07.06.2026: zusätzlich die Bewilligungs-Historie —
        // sie wohnt jetzt im selben Tab, oben über den QST-Einträgen.
        const [entriesRes, pflichtRes, permitsRes] = await Promise.all([
            fetch(`/api/employees/${employeeId}/quellensteuer`, { headers: ah() }),
            fetch(`/api/employees/${employeeId}/qst-pflicht`, { headers: ah() }),
            fetch(`/api/employees/${employeeId}/permit-history`, { headers: ah() })
        ]);
        if (!entriesRes.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>'; return; }
        const entries = await entriesRes.json();
        // Walter-Vorgabe 07.06.2026: QST-Einträge in einen globalen Cache,
        // damit das Permit-Modal den B→C-Hinweis prüfen kann.
        window._empQstCache = entries;
        const pflicht = pflichtRes.ok ? await pflichtRes.json() : null;
        if (permitsRes.ok) {
            _permitHistoryCache = await permitsRes.json();
        }
        renderQuellensteuerTab(el, entries, pflicht);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler</span></div>';
    }
}

// Walter-Vorgabe 26.05.2026: Banner-Renderer für den QST-Pflicht-Status.
// Drei Zustände: GRÜN befreit / ROT Pflicht offen / BLAU Pflicht + Erfassung.
function renderQstPflichtBanner(pflicht) {
    if (!pflicht) return '';
    const empId = selectedEmployeeId;

    if (pflicht.befreiungsGrund) {
        // Befreit — grüner Banner mit Begründung
        const grundText = {
            'CH-Buerger': 'Schweizer Staatsbürger',
            'C-Ausweis': 'C-Ausweis (Niederlassungsbewilligung)',
            'Behoerde': 'Befreiung durch Steuerbehörde',
            'Ehepartner-CH': 'Verheiratet mit Schweizer/in',
            'Ehepartner-C': 'Verheiratet mit C-Ausweis-Inhaber'
        }[pflicht.befreiungsGrund] || pflicht.befreiungsGrund;

        // Gültig-bis-Info nur bei Behörden-Befreiung mit Ablaufdatum
        const fmtBefDate = (iso) => {
            if (!iso) return '';
            const s = String(iso).slice(0, 10);
            if (s.length !== 10) return '';
            return s.slice(8,10) + '.' + s.slice(5,7) + '.' + s.slice(0,4);
        };
        let gueltigText = '';
        if (pflicht.befreiungsGrund === 'Behoerde') {
            const ab  = fmtBefDate(pflicht.befreiungsGueltigAb);
            const bis = fmtBefDate(pflicht.befreiungsGueltigBis);
            if (ab && bis)  gueltigText = ` · gültig ${ab}–${bis}`;
            else if (ab)    gueltigText = ` · gültig ab ${ab}`;
            else if (bis)   gueltigText = ` · gültig bis ${bis}`;
        }

        // Walter-Vorgabe 28.05.2026: bei Behörden-Befreiung das hinterlegte
        // Bestätigungsschreiben direkt aus dem Banner per Side-Panel anschauen
        // können (gleicher Mechanismus wie Dokumenten-Verwaltung).
        const dokAnschauen = (pflicht.befreiungsGrund === 'Behoerde' && pflicht.befreiungsDokumentId)
            ? `<button onclick="qstOpenBefreiungsDok(${empId}, ${pflicht.befreiungsDokumentId})" title="Bestätigungsschreiben im Vorschau-Panel rechts öffnen" style="background:#fff;border:1px solid #16a34a;color:#16a34a;padding:6px 12px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:5px">📄 Dokument anschauen</button>`
            : '';
        const aufheben = pflicht.befreiungsGrund === 'Behoerde'
            ? `<button onclick="qstBefreiungAufheben(${empId})" style="background:transparent;border:1px solid #16a34a;color:#16a34a;padding:6px 12px;border-radius:6px;font-size:12px;cursor:pointer">Befreiung aufheben</button>`
            : '';

        // Walter-Vorgabe 12.06.2026: zusätzlicher roter Warnbanner, wenn die
        // Befreiung über den Ehepartner geht (CH oder C) und der Ausweis des
        // Ehepartners noch nicht als Dokument hinterlegt ist. Befreiung gilt
        // trotzdem (kein Lohnlauf-Block), aber Walter will den fehlenden
        // Beleg im MA-QST-Tab sofort sehen — analog zur „Ausweis Ehegatte
        // fehlt"-Kontrollliste.
        const spouseWarn = pflicht.spouseDokumentFehlt && (
            pflicht.befreiungsGrund === 'Ehepartner-CH'
            || pflicht.befreiungsGrund === 'Ehepartner-C'
        ) ? `
        <div style="background:#fef2f2;border:1px solid #fca5a5;border-left:4px solid #dc2626;border-radius:8px;padding:12px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <span style="font-size:18px">⚠️</span>
            <div style="flex:1;min-width:200px">
                <div style="font-weight:700;color:#991b1b;font-size:13px">Ausweis des Ehepartners fehlt</div>
                <div style="color:#b91c1c;font-size:12px;margin-top:2px">Die Befreiung von der Quellensteuer stützt sich auf den Ehepartner — bitte den Ausweis des Ehepartners in den Dokumenten hochladen (Typ „Ausweis Ehegatte").</div>
            </div>
            <button onclick="qstOpenFamilieFromBanner(${empId})" style="background:#dc2626;color:#fff;border:none;padding:7px 14px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;margin-left:auto;white-space:nowrap">
                → Zur Familie
            </button>
        </div>` : '';

        // Walter-Vorgabe 13.06.2026: roter Warnbanner für MA selbst, wenn
        // das Beleg-Dokument NICHT direkt am MA verknüpft ist:
        //   CH-Bürger → employee.id_pass_dokument_id   (Pass ODER ID-Karte)
        //   C-Ausweis → employee.c_ausweis_dokument_id (Bewilligungs-Dokument)
        // Klick „Dokument verknüpfen" öffnet Modal mit Doku-Picker.
        // Walter 14.06.2026: bei C-Ausweis hängen wir das Doku jetzt direkt
        // an die jüngste Permit-History (FK PermitHistory.DokumentId) statt
        // ans alte Employee.CAusweisDokumentId. Pflicht.currentPermitHistoryId
        // kommt vom QstPflichtCheckService mit.
        const empKind = pflicht.befreiungsGrund === 'CH-Buerger' ? 'id_pass'
                       : pflicht.befreiungsGrund === 'C-Ausweis' ? 'permit_history' : null;
        const empDokTitle = pflicht.befreiungsGrund === 'CH-Buerger'
            ? 'Ausweis-Dokument nicht verknüpft (ID oder Pass)'
            : 'Ausweis-Dokument nicht verknüpft (C-Ausweis)';
        const empDokText = pflicht.befreiungsGrund === 'CH-Buerger'
            ? 'Der Mitarbeiter ist Schweizer Staatsbürger — bitte das hochgeladene Pass- oder ID-Dokument hier verknüpfen.'
            : 'Der Mitarbeiter hat einen C-Ausweis — bitte das hochgeladene Bewilligungs-Dokument hier verknüpfen.';
        const empWarnHandler = empKind === 'permit_history' && pflicht.currentPermitHistoryId
            ? `permitOpenDokuModal(${pflicht.currentPermitHistoryId})`
            : empKind
                ? `openAusweisDokuModal(${empId},'${empKind}')`
                : '';
        const empWarn = pflicht.employeeDokumentFehlt && empKind ? `
        <div style="background:#fef2f2;border:1px solid #fca5a5;border-left:4px solid #dc2626;border-radius:8px;padding:12px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <span style="font-size:18px">⚠️</span>
            <div style="flex:1;min-width:200px">
                <div style="font-weight:700;color:#991b1b;font-size:13px">${empDokTitle}</div>
                <div style="color:#b91c1c;font-size:12px;margin-top:2px">${empDokText}</div>
            </div>
            <button onclick="${empWarnHandler}" style="background:#dc2626;color:#fff;border:none;padding:7px 14px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;margin-left:auto;white-space:nowrap">
                📎 Dokument verknüpfen
            </button>
        </div>` : '';

        // Zusatz: wenn der Beleg verknüpft IST (also kein Warnbanner), kann
        // Walter ihn aus dem grünen Banner direkt anschauen / die Verknüpfung
        // aufheben. Dafür die jeweils passende Dokument-ID + Buttons.
        const verknuepfteDokId = pflicht.befreiungsGrund === 'CH-Buerger'
                                    ? pflicht.idPassDokumentId
                                    : pflicht.befreiungsGrund === 'C-Ausweis'
                                        ? pflicht.cAusweisDokumentId
                                        : null;
        const ausweisDokButtons = (empKind && verknuepfteDokId)
            ? `<button onclick="qstOpenBefreiungsDok(${empId}, ${verknuepfteDokId})" title="Beleg-Dokument im Vorschau-Panel rechts öffnen" style="background:#fff;border:1px solid #16a34a;color:#16a34a;padding:6px 12px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:5px">📄 Dokument anschauen</button>
               <button onclick="ausweisDokuUnlink(${empId},'${empKind}')" style="background:transparent;border:1px solid #16a34a;color:#16a34a;padding:6px 12px;border-radius:6px;font-size:12px;cursor:pointer">Verknüpfung aufheben</button>`
            : '';

        // Walter-Vorgabe 20.06.2026: gleiche zwei Buttons beim Ehepartner-Beleg,
        // wenn die Befreiung über den Ehepartner (CH/C) läuft und dessen Ausweis
        // verknüpft ist — Dokument anschauen + Verknüpfung aufheben.
        const isSpouseGrund = pflicht.befreiungsGrund === 'Ehepartner-CH'
                           || pflicht.befreiungsGrund === 'Ehepartner-C';
        const spouseDokButtons = (isSpouseGrund && !pflicht.spouseDokumentFehlt
                                  && pflicht.spouseDokumentId && pflicht.spouseFamilyMemberId)
            ? `<button onclick="qstOpenBefreiungsDok(${empId}, ${pflicht.spouseDokumentId})" title="Ausweis des Ehepartners im Vorschau-Panel rechts öffnen" style="background:#fff;border:1px solid #16a34a;color:#16a34a;padding:6px 12px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:5px">📄 Dokument anschauen</button>
               <button onclick="spouseDokuUnlink(${empId}, ${pflicht.spouseFamilyMemberId})" style="background:transparent;border:1px solid #16a34a;color:#16a34a;padding:6px 12px;border-radius:6px;font-size:12px;cursor:pointer">Verknüpfung aufheben</button>`
            : '';

        return spouseWarn + empWarn + `
        <div style="background:#f0fdf4;border:1px solid #86efac;border-left:4px solid #16a34a;border-radius:8px;padding:12px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <span style="font-size:18px">✅</span>
            <div style="flex:1;min-width:200px">
                <div style="font-weight:600;color:#166534;font-size:13px">Nicht QST-pflichtig</div>
                <div style="color:#15803d;font-size:12px;margin-top:2px">${grundText}${gueltigText}</div>
            </div>
            <div style="display:flex;gap:8px;flex-wrap:wrap;margin-left:auto">
                ${dokAnschauen}
                ${ausweisDokButtons}
                ${spouseDokButtons}
                ${aufheben}
            </div>
        </div>`;
    }

    if (pflicht.isPflichtOffen) {
        // Rot — Pflicht offen, keine Erfassung
        return `
        <div style="background:#fef2f2;border:1px solid #fca5a5;border-left:4px solid #dc2626;border-radius:8px;padding:14px;margin-bottom:14px">
            <div style="display:flex;align-items:flex-start;gap:10px;margin-bottom:10px">
                <span style="font-size:20px">⚠️</span>
                <div style="flex:1">
                    <div style="font-weight:700;color:#991b1b;font-size:13px">QST-Pflicht offen — Lohnlauf gesperrt</div>
                    <div style="color:#b91c1c;font-size:12px;margin-top:3px;line-height:1.45">
                        ${pflicht.message}<br>
                        <span style="color:#7f1d1d">Schweizer Praxis: lieber höchsten Tarif erfassen und ggf. zurückzahlen, als zu wenig abziehen.</span>
                    </div>
                </div>
            </div>
            <div style="display:flex;gap:8px;flex-wrap:wrap">
                <button onclick="openQstHoechsterTarif()" style="background:#dc2626;color:#fff;border:none;padding:8px 14px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer">
                    🔴 Höchsten Tarif erfassen
                </button>
                <button onclick="openQstBefreiungModal()" style="background:#fff;border:1px solid #dc2626;color:#dc2626;padding:8px 14px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer">
                    📄 Behörden-Befreiung erfassen
                </button>
            </div>
        </div>`;
    }

    if (pflicht.isQstPflichtig && pflicht.hasErfassung) {
        // Blau — Pflicht, Erfassung vorhanden — alles ok. Walter-Vorgabe
        // 26.05.2026: trotzdem den Befreiungs-Button anbieten, falls der MA
        // später eine Bestätigung von der Steuerbehörde erhält.
        return `
        <div style="background:#f6f3ee;border:1px solid #e5e0d6;border-left:4px solid #1a1a1a;border-radius:8px;padding:10px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px">
            <span style="font-size:16px">ℹ️</span>
            <div style="color:#6b6152;font-size:12px;flex:1">QST-pflichtig — Erfassung vorhanden.</div>
            <button onclick="openQstBefreiungModal()" style="background:#fff;border:1px solid #1a1a1a;color:#1a1a1a;padding:5px 10px;border-radius:5px;font-size:11.5px;font-weight:600;cursor:pointer;white-space:nowrap">
                📄 Behörden-Befreiung erfassen
            </button>
        </div>`;
    }

    return '';
}

// Walter-Vorgabe 12.06.2026: Sprung aus dem QST-Banner direkt in den Familie-
// Tab des MA — dort kann Walter den Ausweis des Ehepartners hochladen. Setzt
// window.activeEmpId, damit beim Tab-Wechsel der richtige MA selektiert bleibt.
function qstOpenFamilieFromBanner(empId) {
    if (!empId) return;
    window.activeEmpId = empId;
    if (typeof switchEmpTab === 'function') switchEmpTab('familie');
}

// Walter-Vorgabe 13.06.2026: Sprung aus dem QST-Banner direkt in den Dokumente-
// Tab des MA, wo Walter den fehlenden ID/Pass (CH-Bürger) bzw. die Bewilligung
// (C-Ausweis) hochladen kann.
function qstOpenDokumenteFromBanner(empId) {
    if (!empId) return;
    window.activeEmpId = empId;
    if (typeof switchEmpTab === 'function') switchEmpTab('dokumente');
}

// ══════════════════════════════════════════════════════════════════════
// Ausweis-Doku-Verknüpfung (Walter-Vorgabe 13.06.2026)
// ──────────────────────────────────────────────────────────────────────
// Direkte Verknüpfung MA → konkretes Beleg-Dokument für QST-Befreiung:
//   • kind='id_pass'   → CH-Bürger, Pass oder ID-Karte
//   • kind='c_ausweis' → C-Ausweis-Inhaber, Bewilligungs-Dokument
// Aufgerufen über den roten Banner im QST-Tab.
// ══════════════════════════════════════════════════════════════════════

async function openAusweisDokuModal(empId, kind, extra) {
    if (!empId) empId = window.activeEmpId || selectedEmployeeId || null;
    if (!empId) {
        alert('Mitarbeiter-ID fehlt. Bitte den MA links erneut anklicken.');
        return;
    }
    if (!['id_pass', 'c_ausweis', 'spouse', 'behoerden_befreiung', 'permit_history',
          'night_work_exam', 'night_work_ausnahme',
          'probezeit_gespraech1', 'probezeit_gespraech2',
          'lohn_assignment', 'qst_tarif'].includes(kind)) return;

    if (typeof loadEmpDokumente === 'function') {
        try { await loadEmpDokumente(empId); } catch {}
    }
    if (typeof _dokState === 'undefined' || !_dokState.empId) {
        alert('Doku-Modul nicht initialisiert. Bitte Seite neu laden.');
        return;
    }

    // Welcher Typ ist „relevant" für diesen Modus?
    // Walter 14.06.2026: permit_history nutzt denselben „permit"-Code / Name-Regex
    // wie c_ausweis — die Doku-Auswahl ist visuell identisch.
    const wantedCodes = kind === 'id_pass'             ? ['id_card', 'passport']
                      : kind === 'c_ausweis'           ? ['permit']
                      : kind === 'permit_history'      ? ['permit']
                      : kind === 'spouse'              ? ['spouse', 'spouse_permit']
                      : kind === 'probezeit_gespraech1' || kind === 'probezeit_gespraech2'
                          ? ['probezeitgespraech', 'probezeit_gespraech']
                      : kind === 'lohn_assignment'     ? ['lohnabtretung', 'pfaendung', 'pfändung']
                      : kind === 'qst_tarif'           ? ['qst', 'quellensteuer', 'tarif']
                          :                                  []; // behoerden_befreiung: nur Name-Match
    const wantedNamesRx = kind === 'id_pass'           ? /(ident|pass|reisepass|id[\s-]?karte|ausweis)/i
                       : kind === 'c_ausweis'          ? /(aufenthalt|bewilligung|permit|c.{0,3}ausweis)/i
                       : kind === 'permit_history'     ? /(aufenthalt|bewilligung|permit|ausweis)/i
                       : kind === 'spouse'             ? /(ehegatt|ehepartner|spouse|partner)/i
                       : kind === 'night_work_exam'    ? /(arzt|zeugnis|eignung|nacht|verzicht|untersuch)/i
                       : kind === 'night_work_ausnahme' ? /(ausnahme|nacht|tag.{0,3}nacht|anlage)/i
                       : kind === 'probezeit_gespraech1' || kind === 'probezeit_gespraech2'
                           ? /(probezeit)/i
                       : kind === 'lohn_assignment'
                           ? /(pfänd|pfaend|abtretung|lohnabtretung|betreibung|ors|vollmacht|inkasso)/i
                       : kind === 'qst_tarif'
                           ? /(tarif|quellensteuer|qst|steuer)/i
                       :                                  /(quellensteuer\s*befreiung|qst\s*befreiung|befreiung|bestätig|behörd|ämter)/i;

    const tax  = Array.isArray(_dokState.taxonomy) ? _dokState.taxonomy : [];
    const allTypes = tax.flatMap(k => (k.typen || []).map(t => ({ ...t, _katName: k.name, _katId: k.id })));

    // Default-Typ für Upload-Vorauswahl (linked_field_code zuerst, dann Name)
    let defaultTyp = allTypes.find(t => wantedCodes.includes(t.linkedFieldCode || ''))
                  || allTypes.find(t => wantedNamesRx.test(t.name || ''));

    // Dokument-Relevanz-Check
    const typById = new Map(allTypes.map(t => [t.id, t]));
    const isRelevantDoc = d => {
        const t = typById.get(d.dokumentTypId);
        if (!t) return false;
        if (wantedCodes.includes(t.linkedFieldCode || '')) return true;
        return wantedNamesRx.test((t.name || '') + ' ' + (d.bemerkung || ''));
    };

    const docs = Array.isArray(_dokState.docs) ? _dokState.docs.slice() : [];
    // Sortierung: relevante zuerst, dann neueste-zuerst
    docs.sort((a, b) => {
        const ra = isRelevantDoc(a) ? 0 : 1;
        const rb = isRelevantDoc(b) ? 0 : 1;
        if (ra !== rb) return ra - rb;
        return String(b.geaendertAm || b.hochgeladenAm || '')
                .localeCompare(String(a.geaendertAm || a.hochgeladenAm || ''));
    });

    // Kategorien für Filter-Chips zählen
    const catCounts = {};
    for (const d of docs) {
        const c = d.kategorieName || '–';
        catCounts[c] = (catCounts[c] || 0) + 1;
    }
    const catOrder = tax.map(k => k.name).filter(n => catCounts[n]);
    Object.keys(catCounts).forEach(n => { if (!catOrder.includes(n)) catOrder.push(n); });

    const titleText = kind === 'id_pass'             ? 'Pass oder Identitätskarte verknüpfen'
                   : kind === 'c_ausweis'           ? 'C-Ausweis-Dokument verknüpfen'
                   : kind === 'permit_history'      ? 'Bewilligungs-Dokument verknüpfen'
                   : kind === 'spouse'              ? 'Ausweis Ehepartner verknüpfen'
                   : kind === 'night_work_exam'     ? 'Nachtarbeit: Arztzeugnis verknüpfen'
                   : kind === 'night_work_ausnahme' ? 'Nachtarbeit: Ausnahmeregelung verknüpfen'
                   : kind === 'probezeit_gespraech1' ? 'Probezeitgespräch 1: Protokoll verknüpfen'
                   : kind === 'probezeit_gespraech2' ? 'Probezeitgespräch 2: Protokoll verknüpfen'
                   : kind === 'lohn_assignment'     ? 'Lohnabtretung: Beleg-Dokument verknüpfen'
                   : kind === 'qst_tarif'           ? 'QST-Tarifbestätigung verknüpfen'
                   :                                  'Behörden-Befreiung verknüpfen';
    const hintText  = kind === 'id_pass'
        ? 'Wähle ein bestehendes Dokument (Pass oder Identitätskarte) — passende sind oben hervorgehoben. Oder lade ein neues hoch.'
        : kind === 'c_ausweis' || kind === 'permit_history'
            ? 'Wähle das Bewilligungs-Dokument — passende sind oben hervorgehoben. Oder lade ein neues hoch.'
            : kind === 'spouse'
                ? 'Wähle das Ausweis-Dokument des Ehepartners (Pass, ID oder Bewilligung) — passende sind oben hervorgehoben. Oder lade ein neues hoch.'
                : kind === 'night_work_exam'
                    ? 'Wähle das ärztliche Eignungszeugnis (Arztzeugnis) des MA. Oder lade ein neues Dokument hoch.'
                    : kind === 'night_work_ausnahme'
                        ? 'Wähle die unterschriebene Ausnahmeregelung Tag-/Nachtarbeit des MA. Oder lade ein neues Dokument hoch.'
                        : (kind === 'probezeit_gespraech1' || kind === 'probezeit_gespraech2')
                            ? 'Wähle das ausgefüllte Probezeitgespräch-Protokoll (Typ «Probezeitgespräch» unter Mitarbeiterentwicklung). Oder lade ein neues hoch.'
                        : kind === 'lohn_assignment'
                            ? 'Wähle das Abtretungs-/Pfändungsdokument — ohne Beleg ist die Lohnabtretung im Lohnlauf unwirksam. Oder lade ein neues hoch.'
                        : kind === 'qst_tarif'
                            ? 'Wähle die Tarifbestätigung / Tarifmeldung der Steuerbehörde zu dieser QST-Version — passende sind oben hervorgehoben. Oder lade ein neues hoch.'
                        : 'Wähle das Bestätigungsschreiben der Steuerbehörde — passende sind oben hervorgehoben. Oder lade ein neues hoch.';

    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, c =>
        ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
    const fmtDate = (iso) => {
        if (!iso) return '–';
        const s = String(iso).slice(0, 10);
        if (s.length !== 10) return '–';
        return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
    };

    const docRowsHtml = docs.length ? docs.map(d => {
        const rel = isRelevantDoc(d);
        const star = rel ? '<span style="color:#16a34a;font-weight:700;margin-right:4px" title="Passt zum Befreiungsgrund">●</span>' : '';
        const fname = d.bemerkung || d.filenameOriginal || ('Dokument #' + d.id);
        // Walter 14.06.2026: Hover-Farbe per CSS-Klasse (.ausweis-doku-row /
        // .ausweis-doku-row.relevant) statt Inline-Style — sonst überschreibt
        // der Inline-Background im Dark Mode die CSS-Variablen.
        return `
        <tr class="ausweis-doku-row${rel ? ' relevant' : ''}"
            data-cat="${esc((d.kategorieName || '').toLowerCase())}"
            data-search="${esc(((d.bemerkung || '') + ' ' + (d.filenameOriginal || '') + ' ' + (d.dokumentTypName || '')).toLowerCase())}"
            onclick="ausweisDokuPick(${d.id})">
            <td style="padding:9px 12px;font-size:11.5px;color:#475569">${esc(d.kategorieName || '–')}</td>
            <td style="padding:9px 12px;font-size:12px;font-weight:600;color:#0f172a">${esc(d.dokumentTypName || '–')}</td>
            <td style="padding:9px 12px;font-size:12px;color:#0f172a">${star}${esc(fname)}</td>
            <td style="padding:9px 12px;font-size:11px;color:#94a3b8;white-space:nowrap">${fmtDate(d.geaendertAm || d.hochgeladenAm)}</td>
        </tr>`;
    }).join('') : `
        <tr><td colspan="4" style="padding:30px;text-align:center;color:#94a3b8;font-style:italic;font-size:12.5px">
            Noch kein Dokument hinterlegt — lade eines hoch.
        </td></tr>`;

    const catChipsHtml = catOrder.length ? `
        <div style="display:flex;flex-wrap:wrap;gap:6px;margin-bottom:10px">
            <button type="button" data-cat="" onclick="ausweisDokuFilterCat('')"
                    style="padding:5px 12px;background:#0f172a;color:#fff;border:none;border-radius:999px;font-size:11.5px;font-weight:600;cursor:pointer"
                    class="ausweis-cat-chip ausweis-cat-active">
                Alle <span style="opacity:.7">${docs.length}</span>
            </button>
            ${catOrder.map(c => `
                <button type="button" data-cat="${esc(c.toLowerCase())}" onclick="ausweisDokuFilterCat('${esc(c).replace(/'/g,'\\\'')}')"
                        style="padding:5px 12px;background:#fff;color:#475569;border:1px solid #e2e8f0;border-radius:999px;font-size:11.5px;font-weight:600;cursor:pointer"
                        class="ausweis-cat-chip">
                    ${esc(c)} <span style="color:#94a3b8">${catCounts[c]}</span>
                </button>`).join('')}
        </div>` : '';

    const html = `
    <style>
        /* Walter 14.06.2026: Hover-/Default-Farben via CSS-Klassen,
           damit Dark Mode mit Theme-Variablen umgehen kann. Vorher
           waren die Backgrounds inline gesetzt → überschrieben den
           Dark-Mode-Style. */
        #ausweisDokuModal .ausweis-doku-row {
            cursor: pointer;
            border-bottom: 1px solid #f1f5f9;
            background: #fff;
        }
        #ausweisDokuModal .ausweis-doku-row.relevant { background: #f0fdf4; }
        #ausweisDokuModal .ausweis-doku-row:hover    { background: #f6f3ee; }
        body.theme-dark #ausweisDokuModal .ausweis-doku-row {
            background: #1e293b;
            border-bottom-color: #334155;
        }
        body.theme-dark #ausweisDokuModal .ausweis-doku-row.relevant { background: #064e3b; }
        body.theme-dark #ausweisDokuModal .ausweis-doku-row:hover    { background: #5a5348; }
    </style>
    <div id="ausweisDokuModal" style="position:fixed;inset:0;background:rgba(0,0,0,0.45);z-index:9600;display:flex;align-items:center;justify-content:center;padding:20px"
         onclick="if(event.target===this)closeAusweisDokuModal()">
        <div style="background:#fff;border-radius:12px;width:880px;max-width:96vw;max-height:90vh;display:flex;flex-direction:column;box-shadow:0 14px 56px rgba(0,0,0,0.28)">
            <div style="padding:16px 22px;border-bottom:1px solid #e2e8f0;display:flex;align-items:center;gap:10px">
                <span style="font-size:20px">📎</span>
                <div style="flex:1">
                    <div style="font-weight:700;color:#0f172a;font-size:14.5px">${titleText}</div>
                    <div style="color:#64748b;font-size:12px;margin-top:2px">${hintText}</div>
                </div>
                <button onclick="ausweisDokuUploadNew()" style="background:#1a1a1a;color:#fff;border:none;padding:8px 14px;border-radius:6px;font-size:12.5px;font-weight:600;cursor:pointer;white-space:nowrap;display:inline-flex;align-items:center;gap:6px">
                    <span>＋</span> Neues Dokument
                </button>
                <button onclick="closeAusweisDokuModal()" style="background:none;border:none;color:#94a3b8;font-size:22px;cursor:pointer;line-height:1;margin-left:4px">×</button>
            </div>
            <div style="padding:14px 22px;overflow-y:auto;flex:1">
                ${catChipsHtml}
                <input id="ausweisDokuSearch" type="text" placeholder="Filtern (Beschreibung, Typ, Dateiname)…"
                       oninput="ausweisDokuFilterText(this.value)"
                       style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:6px;font-size:12px;margin-bottom:10px">
                <div style="border:1px solid #e2e8f0;border-radius:6px;overflow:hidden">
                    <table style="width:100%;border-collapse:collapse" id="ausweisDokuTable">
                        <thead>
                            <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0">
                                <th style="padding:8px 12px;text-align:left;font-size:10.5px;color:#64748b;font-weight:600;text-transform:uppercase">Kategorie</th>
                                <th style="padding:8px 12px;text-align:left;font-size:10.5px;color:#64748b;font-weight:600;text-transform:uppercase">Typ</th>
                                <th style="padding:8px 12px;text-align:left;font-size:10.5px;color:#64748b;font-weight:600;text-transform:uppercase">Beschreibung</th>
                                <th style="padding:8px 12px;text-align:left;font-size:10.5px;color:#64748b;font-weight:600;text-transform:uppercase">Geändert</th>
                            </tr>
                        </thead>
                        <tbody>${docRowsHtml}</tbody>
                    </table>
                </div>
            </div>
            <div style="padding:12px 22px;border-top:1px solid #e2e8f0;display:flex;justify-content:flex-end">
                <button onclick="closeAusweisDokuModal()" style="background:#fff;border:1px solid #cbd5e1;color:#475569;padding:7px 16px;border-radius:6px;font-size:13px;cursor:pointer">Abbrechen</button>
            </div>
        </div>
    </div>`;

    closeAusweisDokuModal();
    document.body.insertAdjacentHTML('beforeend', html);
    window._ausweisDokuCtx = {
        empId,
        kind,
        defaultTypId: defaultTyp?.id || null,
        defaultKatId: defaultTyp?._katId || null,
        spouseFamilyMemberId: extra?.spouseFamilyMemberId || null,
        // Walter 14.06.2026: für kind='permit_history' der konkrete History-Eintrag.
        permitHistoryId: extra?.permitHistoryId || null,
        // Walter 02.08.2026: Lohnabtretung-Beleg.
        lohnAssignmentId: extra?.lohnAssignmentId || null,
        // Walter 21.08.2026: Tarifbestätigung pro QST-Version.
        qstEntryId: extra?.qstEntryId || null
    };
}

// Filter-Funktionen
function ausweisDokuFilterCat(cat) {
    const catLow = (cat || '').toLowerCase();
    document.querySelectorAll('#ausweisDokuModal .ausweis-cat-chip').forEach(b => {
        const isActive = (b.dataset.cat || '') === catLow;
        b.style.background = isActive ? '#0f172a' : '#fff';
        b.style.color = isActive ? '#fff' : '#475569';
        b.style.border = isActive ? 'none' : '1px solid #e2e8f0';
    });
    ausweisDokuApplyFilters();
}
function ausweisDokuFilterText(_q) { ausweisDokuApplyFilters(); }
function ausweisDokuApplyFilters() {
    const activeChip = document.querySelector('#ausweisDokuModal .ausweis-cat-chip[style*="rgb(15, 23, 42)"]')
                     || document.querySelector('#ausweisDokuModal .ausweis-cat-chip[style*="#0f172a"]');
    const cat  = (activeChip?.dataset.cat || '').toLowerCase();
    const q    = (document.getElementById('ausweisDokuSearch')?.value || '').trim().toLowerCase();
    document.querySelectorAll('#ausweisDokuTable tbody tr[data-search]').forEach(tr => {
        const trCat = (tr.dataset.cat || '');
        const okCat = !cat || trCat === cat;
        const okQ   = !q   || (tr.dataset.search || '').includes(q);
        tr.style.display = (okCat && okQ) ? '' : 'none';
    });
}

// „+ Neues Dokument" → öffnet das Standard-Upload-Modal mit Vorauswahl + Callback
function ausweisDokuUploadNew() {
    const ctx = window._ausweisDokuCtx;
    if (!ctx) return;
    if (typeof _dokState === 'undefined') return;
    _dokState.selectedKategorieId = ctx.defaultKatId;
    _dokState.selectedTypId       = ctx.defaultTypId;
    // Probezeit-Reopen-Flag AUFSCHIEBEN (Walter 22.07.2026): sonst wuerde
    // closeAusweisDokuModal das pzModal sofort ueber das Upload-Modal legen.
    const pzFlag = window._pzReopenAfterDoku;
    window._pzReopenAfterDoku = null;
    _dokState.afterUpload = async (newDokId, _raw, formInfo) => {
        if (!newDokId) { if (pzFlag) { window._pzReopenAfterDoku = pzFlag; pzReopenIfFlagged(); } return; }
        await ausweisDokuVerknuepfen(ctx.empId, ctx.kind, newDokId, formInfo);
        if (pzFlag) { window._pzReopenAfterDoku = pzFlag; pzReopenIfFlagged(); }
    };
    closeAusweisDokuModal();
    if (typeof openDokUploadModal === 'function') openDokUploadModal();
}

// Klick auf eine Tabellenzeile → bestehendes Dokument verknüpfen
async function ausweisDokuPick(dokumentId) {
    const ctx = window._ausweisDokuCtx;
    if (!ctx || !dokumentId) return;
    await ausweisDokuVerknuepfen(ctx.empId, ctx.kind, dokumentId, null);
    closeAusweisDokuModal();
}

// Gemeinsamer PATCH-Aufruf (von Upload-Callback + Direct-Pick).
// Mode-spezifische Routing:
//   • id_pass / c_ausweis        → /api/employees/{id}/ausweis-doku
//   • spouse                     → /api/employees/{id}/family/{famId}/dokument
//   • behoerden_befreiung        → /api/employees/{id}/qst-befreiung
// Für Behörden-Befreiung wird zusätzlich Gültig-ab/bis übergeben — beim Direct-
// Pick nehmen wir die Felder des Dokuments selbst (gueltigVon/gueltigBis),
// beim Upload-Pfad kommen sie als 3. Callback-Argument aus dem Upload-Modal.
async function ausweisDokuVerknuepfen(empId, kind, dokumentId, formInfo) {
    const ctx = window._ausweisDokuCtx || {};
    try {
        let url, body;
        if (kind === 'permit_history') {
            // Walter 14.06.2026: Verknüpfung pro Bewilligungs-Eintrag.
            const historyId = ctx.permitHistoryId;
            if (!historyId) { alert('Bewilligungs-Eintrag-ID fehlt.'); return; }
            url  = `/api/employees/${empId}/permit-history/${historyId}/dokument`;
            body = JSON.stringify({ dokumentId });
        } else if (kind === 'lohn_assignment') {
            const laId = ctx.lohnAssignmentId;
            if (!laId) { alert('Lohnabtretungs-ID fehlt.'); return; }
            url  = `/api/employee-lohn-assignments/${laId}/dokument`;
            body = JSON.stringify({ dokumentId });
        } else if (kind === 'spouse') {
            const famId = ctx.spouseFamilyMemberId;
            if (!famId) { alert('Ehepartner-ID fehlt.'); return; }
            url  = `/api/employees/${empId}/family/${famId}/dokument`;
            body = JSON.stringify({ dokumentId });
        } else if (kind === 'qst_tarif') {
            // Walter 21.08.2026: Tarifbestätigung pro QST-Version.
            const qstId = ctx.qstEntryId;
            if (!qstId) { alert('QST-Eintrag-ID fehlt.'); return; }
            url  = `/api/employees/${empId}/quellensteuer/${qstId}/dokument`;
            body = JSON.stringify({ dokumentId });
        } else if (kind === 'behoerden_befreiung') {
            // Gültig-ab/bis ermitteln: erst formInfo (Upload-Pfad), sonst aus
            // dem Dokument selbst (Direct-Pick).
            let gueltigAb  = formInfo?.gueltigVon || null;
            let gueltigBis = formInfo?.gueltigBis || null;
            if (!gueltigAb && Array.isArray(_dokState?.docs)) {
                const d = _dokState.docs.find(x => x.id === dokumentId);
                gueltigAb  = (d?.gueltigVon || '').slice(0, 10) || null;
                gueltigBis = (d?.gueltigBis || '').slice(0, 10) || null;
            }
            if (!gueltigAb) {
                gueltigAb = prompt('Gültig-ab-Datum für die Behörden-Befreiung (TT.MM.JJJJ):');
                if (!gueltigAb) return;
                // einfache Eingabe akzeptieren — wandelt TT.MM.JJJJ in ISO um
                const m = gueltigAb.match(/^(\d{1,2})[.\/](\d{1,2})[.\/](\d{4})$/);
                if (m) gueltigAb = `${m[3]}-${m[2].padStart(2,'0')}-${m[1].padStart(2,'0')}`;
            }
            url  = `/api/employees/${empId}/qst-befreiung`;
            body = JSON.stringify({ befreit: true, dokumentId, gueltigAb, gueltigBis });
        } else {
            url  = `/api/employees/${empId}/ausweis-doku`;
            body = JSON.stringify({ kind, dokumentId });
        }
        const res = await fetch(url, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || `Verknüpfen fehlgeschlagen (${res.status})`);
            return;
        }
        // QST-Tab refresht den Banner; Familie-Tab refresht die Doku-Pille.
        if (typeof loadQuellensteuerTab === 'function') loadQuellensteuerTab(empId);
        if (kind === 'spouse' && typeof loadFamilieTab === 'function') loadFamilieTab(empId);
        // Walter 14.06.2026: Bewilligungs-Liste in der MA-Maske neu laden,
        // damit die 📎-Pille pro Eintrag den frischen Doku-Status zeigt.
        // Plus: MA-Detail neu laden, damit auch die 📎-Pille im AUFENTHALT-
        // Block in der MA-Maske aktualisiert wird (currentPermitDokumentId).
        if (kind === 'permit_history') {
            if (typeof loadPermitHistory === 'function') loadPermitHistory(empId);
            if (typeof selectEmployee === 'function') selectEmployee(empId);
        }
        if (kind === 'lohn_assignment' && typeof loadLohnAssignmentsTab === 'function') {
            loadLohnAssignmentsTab(empId);
        }
        // Nachtarbeit-Belege: MA-Detail neu laden (Anzeige-Buttons im Nachtarbeit-Block).
        if ((kind === 'night_work_exam' || kind === 'night_work_ausnahme') && typeof selectEmployee === 'function') selectEmployee(empId);
        // Probezeitgespräch: vorgeschlagene Datum übernehmen falls noch leer,
        // dann Modal + Anstellung neu zeichnen (Walter 21.07.2026).
        if (kind === 'probezeit_gespraech1' || kind === 'probezeit_gespraech2') {
            const nr = kind === 'probezeit_gespraech2' ? 2 : 1;
            const empNow = typeof selectedEmployee !== 'undefined' ? selectedEmployee : null;
            const hasAm = nr === 1 ? empNow?.probezeitGespraech1Am : empNow?.probezeitGespraech2Am;
            if (!hasAm && typeof pzSaveDate === 'function') {
                const inp = document.getElementById('pzAm' + nr);
                const iso = (inp && inp.value) || '';
                if (iso) await pzSaveDate(empId, nr, iso);
            }
            if (typeof selectEmployee === 'function') await selectEmployee(empId);
            if (typeof pzRefreshModal === 'function') pzRefreshModal();
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

function closeAusweisDokuModal() {
    document.getElementById('ausweisDokuModal')?.remove();
    // Context NICHT löschen — wird vom Upload-Callback noch gebraucht.
    // Kam der Waehler aus dem Probezeit-Modal: dieses wieder oeffnen
    // (Walter 22.07.2026) — gilt fuer Auswahl UND Abbrechen.
    pzReopenIfFlagged();
}

// Probezeit-Modal nach dem Dokument-Waehler wieder oeffnen — mit frisch
// geladenen MA-Daten, damit Verknuepfung/Status sofort stimmen.
function pzReopenIfFlagged() {
    if (!window._pzReopenAfterDoku) return;
    const empId = window._pzReopenAfterDoku;
    window._pzReopenAfterDoku = null;
    (async () => {
        try {
            const r = await fetch(`/api/employees/${empId}`, { headers: ah(), cache: 'no-store' });
            if (r.ok) {
                selectedEmployee = await r.json();
                window.selectedEmployeeId = empId;
            }
        } catch {}
        _pzEnsureModal();
        pzRefreshModal();
        const m = document.getElementById('pzModal');
        if (m) m.style.display = 'flex';
    })();
}

async function ausweisDokuUnlink(empId, kind) {
    if (!empId || !kind) return;
    if (!(await liquidConfirm('Verknüpfung wirklich aufheben? Der Banner zeigt danach wieder „Beleg fehlt".'))) return;
    try {
        const res = await fetch(`/api/employees/${empId}/ausweis-doku`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ kind, dokumentId: null })
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || `Fehler (${res.status})`);
            return;
        }
        if (typeof loadQuellensteuerTab === 'function') loadQuellensteuerTab(empId);
        if ((kind === 'probezeit_gespraech1' || kind === 'probezeit_gespraech2'
             || kind === 'night_work_exam' || kind === 'night_work_ausnahme')
            && typeof selectEmployee === 'function') {
            await selectEmployee(empId);
        }
        if ((kind === 'probezeit_gespraech1' || kind === 'probezeit_gespraech2')
            && typeof pzRefreshModal === 'function') pzRefreshModal();
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// SECO-Formular „Eignung Schicht-/Nachtarbeit" vorausgefüllt holen und im
// Vorschaufenster zeigen (Walter 20.06.2026). Betrieb + MA-Angaben kommen
// server-seitig aus Filiale (CompanyProfile) + MA-Stammdaten.
async function openNachtEignungPdf(empId) {
    if (!empId) return;
    try {
        const res = await fetch(`/api/nacht-eignung/${empId}/pdf`, { headers: ah() });
        if (!res.ok) {
            let msg = `Fehler (${res.status})`;
            try { const j = await res.json(); if (j?.message) msg = j.message; } catch (_) {}
            alert('Formular konnte nicht erstellt werden.\n' + msg);
            return;
        }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename="?([^"]+)"?/);
        const filename = m ? m[1] : `Nachtarbeit_Eignung_${empId}.pdf`;
        if (typeof previewFileModal === 'function') previewFileModal(blob, filename);
        else if (typeof saveBlobAsk === 'function') saveBlobAsk(blob, filename);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Verzichtserklärung Nachtarbeit (Beilage-Layout, gelber Kopf) holen + Vorschau.
async function openNachtVerzichtPdf(empId) {
    if (!empId) return;
    try {
        const res = await fetch(`/api/nacht-eignung/${empId}/verzicht-pdf`, { headers: ah() });
        if (!res.ok) {
            let msg = `Fehler (${res.status})`;
            try { const j = await res.json(); if (j?.message) msg = j.message; } catch (_) {}
            alert('Formular konnte nicht erstellt werden.\n' + msg);
            return;
        }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename="?([^"]+)"?/);
        const filename = m ? m[1] : `Nachtarbeit_Verzicht_${empId}.pdf`;
        if (typeof previewFileModal === 'function') previewFileModal(blob, filename);
        else if (typeof saveBlobAsk === 'function') saveBlobAsk(blob, filename);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Ausnahmeregelung Tag-/Nachtarbeit (Anlage zum Arbeitsvertrag, gelber Kopf mit
// Titel über Banner) – MA-Angaben links, Filiale rechts, server-seitig gefüllt.
async function openNachtAusnahmePdf(empId) {
    if (!empId) return;
    try {
        const res = await fetch(`/api/nacht-eignung/${empId}/ausnahme-pdf`, { headers: ah() });
        if (!res.ok) {
            let msg = `Fehler (${res.status})`;
            try { const j = await res.json(); if (j?.message) msg = j.message; } catch (_) {}
            alert('Formular konnte nicht erstellt werden.\n' + msg);
            return;
        }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename="?([^"]+)"?/);
        const filename = m ? m[1] : `Nachtarbeit_Ausnahme_${empId}.pdf`;
        if (typeof previewFileModal === 'function') previewFileModal(blob, filename);
        else if (typeof saveBlobAsk === 'function') saveBlobAsk(blob, filename);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Ehepartner-Beleg-Dokument aus dem QST-Banner lösen (Walter 20.06.2026) —
// analog ausweisDokuUnlink, nur über den Family-Member-Dokument-PATCH.
async function spouseDokuUnlink(empId, familyMemberId) {
    if (!empId || !familyMemberId) return;
    if (!(await liquidConfirm('Verknüpfung wirklich aufheben? Der Banner zeigt danach wieder „Ausweis des Ehepartners fehlt".'))) return;
    try {
        const res = await fetch(`/api/employees/${empId}/family/${familyMemberId}/dokument`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ dokumentId: null })
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || `Fehler (${res.status})`);
            return;
        }
        loadQuellensteuerTab(empId);
        // Auch den Familie-Tab auffrischen (Walter 13.07.2026): der Loesen-
        // Knopf sitzt neu auch in der Ehepartner-Zeile dort.
        if (typeof loadFamilieTab === 'function') loadFamilieTab(empId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Datum ± n Jahre (ISO yyyy-MM-dd; lokale Rechnung, Feb-29-sicher).
function _nwAddYears(iso, n) {
    if (!iso) return '';
    const p = String(iso).slice(0, 10).split('-').map(Number);
    if (p.length < 3) return '';
    const dt = new Date(p[0] + n, p[1] - 1, p[2]);
    return `${dt.getFullYear()}-${String(dt.getMonth() + 1).padStart(2, '0')}-${String(dt.getDate()).padStart(2, '0')}`;
}

// „gültig bis"-Anzeige (inneres HTML der id'd Span) — wird in-place aktualisiert.
// Kompaktes «gültig bis» (inline). Grün nur mit verknüpftem Arztzeugnis.
function _nwGueltigBisHtml(validUntil, hasArztDoc) {
    if (!validUntil) return `<span class="nw-inline-muted">gültig bis —</span>`;
    const t = new Date(); t.setHours(0, 0, 0, 0);
    const exp = new Date(validUntil) < t;
    let cls = 'nw-inline-val';
    if (exp) cls += ' nw-date-expired';
    else if (hasArztDoc) cls += ' nw-date-ok';
    const tip = hasArztDoc ? '' : ' title="Datum hinterlegt — gilt erst mit verknüpftem Arztzeugnis"';
    return `<span class="nw-inline-lbl">gültig bis</span> <span class="${cls}"${tip}>${formatDate(validUntil)}</span>${exp ? ' <span class="nw-date-tag">abgelaufen</span>' : ''}`;
}

// Kompakte Datumszeile (eine Linie, feste Reihenfolge).
function _nwViewTextHtml(issueIso, validUntil, mismatch, sollBisIso, hasArztDoc) {
    if (!validUntil && !issueIso) {
        return `<span class="nw-inline-muted nw-dates-empty">Keine Untersuchung erfasst</span>`;
    }
    let html = `<span class="nw-inline-lbl">Ausgestellt</span> <span class="nw-inline-val">${formatDate(issueIso)}</span>`
             + ` <span class="nw-dotsep">·</span> ${_nwGueltigBisHtml(validUntil, !!hasArztDoc)}`;
    if (mismatch) {
        const soll = sollBisIso ? formatDate(sollBisIso) : '—';
        html += ` <span class="nw-warn-chip nw-warn-chip-sm" title="Das Enddatum in easy@work stimmt nicht mit der Regel überein und muss dort korrigiert werden">easy@work-Ende ≠ ${soll}</span>`;
    }
    return html;
}

// true = Pflicht/Planung aktiv UND Arztzeugnis + Ausnahmeregelung + Datum ok.
function _nwNachweiseOk(emp) {
    if (!emp) return false;
    const requires = !!emp.nightWorkRequiresDocuments;
    const hasDates = !!(emp.nightWorkExamValidUntil || emp.nightWorkExamIssued);
    if (!requires && !hasDates) return false;
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const validUntil = emp.nightWorkExamValidUntil ? new Date(emp.nightWorkExamValidUntil) : null;
    if (validUntil) validUntil.setHours(0, 0, 0, 0);
    if (validUntil && validUntil < today) return false;
    const hasArztDoc = !!emp.nightWorkExamDokumentId;
    if (!hasArztDoc) return false;
    const examCurrent = (validUntil && validUntil >= today)
        || (hasArztDoc && !emp.nightWorkExamValidUntil);
    if (!examCurrent) return false;
    if (!emp.nightWorkAusnahmeDokumentId) return false;
    return true;
}

// Status-Pille allein (feste Spalte im nw-layout).
// ≤18: grün «Keine Untersuch-Pflicht».
// >18: immer rot «Untersuch-Pflicht» (gesetzliche Pflicht — Walter 26.07.2026;
// Erfüllungsstand steht separat im Chip «Alles in Ordnung» / fehlt-Chips).
function _nwDutyBadgeOnlyHtml(emp) {
    if (!emp) return '';
    const req = !!emp.nightWorkRequiresDocuments;
    const n = emp.nightWorkMaxNightsInSixWeeks != null ? emp.nightWorkMaxNightsInSixWeeks : 0;
    const tip = `Max. ${n} Nacht-Tage in einem rollierenden 6-Wochen-Fenster (42 Tage, ArGV1 Art. 30 — Pflicht ab >18)`;
    if (req) {
        const tip2 = _nwNachweiseOk(emp)
            ? tip + ' — Nachweise vollständig, alles in Ordnung'
            : tip + ' — Nachweise noch unvollständig';
        return `<span class="nw-duty-badge nw-duty-on" title="${tip2}"><span class="nw-duty-dot"></span>Untersuch-Pflicht</span>`;
    }
    return `<span class="nw-duty-badge nw-duty-off" title="${tip}"><span class="nw-duty-dot"></span>Keine Untersuch-Pflicht</span>`;
}

// Nächte-Zähler allein (feste Spalte). Max. im rollierenden 6-Wochen-Fenster.
// Rot wenn Untersuch-Pflicht besteht (wie Badge).
function _nwDutyCountHtml(emp) {
    if (!emp) return '';
    const req = !!emp.nightWorkRequiresDocuments;
    const n = emp.nightWorkMaxNightsInSixWeeks != null ? emp.nightWorkMaxNightsInSixWeeks : 0;
    const tip = `Max. ${n} Nacht-Tage in einem rollierenden 6-Wochen-Fenster (42 Tage, ArGV1 Art. 30 — Pflicht ab >18)`;
    const label = n === 1 ? '1 Nacht' : n + ' Nächte';
    return `<span class="nw-duty-count${req ? ' nw-duty-count-on' : ''}" title="${tip}">${label} / 6 Wochen</span>`;
}

// Rote «fehlt»-Hinweise ODER grünes «Alles in Ordnung» (Walter 19./26.07.2026).
// Datum erfasst = Nachtarbeit geplant → Arztzeugnis UND Ausnahmeregelung prüfen.
// Ohne verknüpfte Dokumente gilt der Nachweis NICHT (Datum allein reicht nie).
// Zusätzlich bei ArGV1 Art. 30 (>18 Nächte / 42 Tage) auch ohne Datum.
function _nwMissingDocsHtml(emp) {
    if (!emp) return '';
    const requires = !!emp.nightWorkRequiresDocuments;
    const hasDates = !!(emp.nightWorkExamValidUntil || emp.nightWorkExamIssued);
    // Ohne geplante Nachtarbeit (kein Datum) und ohne ArGV1-Pflicht → keine Hinweise.
    if (!requires && !hasDates) return '';
    // Walter-Vorgabe 21.08.2026 (Fall Gazale, ersetzt «immer melden» vom
    // 12.07.2026): rote Chips NUR bei tatsächlicher Untersuch-Pflicht
    // (>18 Nächte im 6-Wochen-Fenster). Ein abgelaufenes Alt-Zeugnis ohne
    // aktuelle Nachtarbeit → grauer Info-Chip statt rot; arbeitet der MA
    // wieder >18 Nächte, werden die Chips automatisch wieder rot.
    if (!requires) {
        const vu = emp.nightWorkExamValidUntil ? new Date(emp.nightWorkExamValidUntil) : null;
        const t0 = new Date(); t0.setHours(0, 0, 0, 0);
        if (vu && vu < t0) {
            return `<span class="nw-warn-chip" style="background:#f1efe9;border-color:#d5d0c6;color:#8b8b8b"
                title="Zeugnis war gültig bis ${vu.toLocaleDateString('de-CH')} — zurzeit keine Untersuch-Pflicht (≤18 Nächte/6 Wochen), daher ohne Folge. Vor erneuter Nacht-Planung erneuern.">Zeugnis abgelaufen · zzt. keine Nachtarbeit</span>`;
        }
        // Zeugnis noch gültig oder nur Datum erfasst → kein Lärm.
        return '';
    }

    const today = new Date(); today.setHours(0, 0, 0, 0);
    const validUntil = emp.nightWorkExamValidUntil ? new Date(emp.nightWorkExamValidUntil) : null;
    if (validUntil) validUntil.setHours(0, 0, 0, 0);
    const hasArztDoc = !!emp.nightWorkExamDokumentId;
    const examCurrent = (validUntil && validUntil >= today)
        || (hasArztDoc && !emp.nightWorkExamValidUntil);
    const hasAusnahme = !!emp.nightWorkAusnahmeDokumentId;
    const parts = [];
    const tip = hasDates
        ? 'Nachtarbeit geplant (Datum erfasst) — gültig erst mit verknüpften Dokumenten'
        : `${emp.nightWorkMaxNightsInSixWeeks || '?'} Nächte in 6 Wochen — Nachweise Pflicht (ArGV1 Art. 30)`;

    if (validUntil && validUntil < today) {
        parts.push(`<span class="nw-warn-chip" title="${tip}">Nachtbew. abgelaufen</span>`);
    } else if (!hasArztDoc) {
        parts.push(`<span class="nw-warn-chip" title="${tip}">Arztzeugnis fehlt</span>`);
    } else if (!examCurrent) {
        parts.push(`<span class="nw-warn-chip" title="${tip}">Nacht Untersuch fehlt</span>`);
    }

    if (!hasAusnahme) {
        parts.push(`<span class="nw-warn-chip" title="${tip}">Ausn. Regel fehlt</span>`);
    }
    if (!parts.length) return '';
    return parts.join('');
}

/** Warn-Chips ODER grünes «Alles in Ordnung» wenn Pflicht erfüllt. */
function _nwStatusChipsHtml(emp) {
    const missing = _nwMissingDocsHtml(emp);
    if (missing) return missing;
    if (_nwNachweiseOk(emp)) {
        return `<span class="nw-ok-chip" title="Arztzeugnis und Ausnahmeregelung verknüpft, Gültigkeit ok">Alles in Ordnung</span>`;
    }
    return '';
}

// ⋮-Menü + Edit-Toggle für die Nachtarbeit-Zeile.
function _nwClearMenuPos(menu) {
    if (!menu) return;
    menu.style.position = '';
    menu.style.top = '';
    menu.style.left = '';
    menu.style.right = '';
    menu.style.bottom = '';
    menu.style.zIndex = '';
}
// Fixed-Position: Übersicht-Karten clippen absolute Menüs (overflow:hidden).
function nwToggleMenu(event, id) {
    event.stopPropagation();
    const menu = document.getElementById('nwMenu-' + id);
    const wasOpen = menu?.classList.contains('show');
    document.querySelectorAll('.dok-menu.show').forEach(m => {
        m.classList.remove('show');
        _nwClearMenuPos(m);
    });
    if (wasOpen || !menu) return;
    const btn = event.currentTarget;
    const r = btn.getBoundingClientRect();
    menu.classList.add('show');
    const mh = menu.offsetHeight || 200;
    const spaceBelow = window.innerHeight - r.bottom;
    menu.style.position = 'fixed';
    menu.style.right = Math.max(8, window.innerWidth - r.right) + 'px';
    menu.style.left = 'auto';
    menu.style.zIndex = '6000';
    if (spaceBelow < mh + 8) {
        menu.style.top = 'auto';
        menu.style.bottom = Math.max(8, window.innerHeight - r.top + 4) + 'px';
    } else {
        menu.style.top = (r.bottom + 4) + 'px';
        menu.style.bottom = 'auto';
    }
    setTimeout(() => {
        document.addEventListener('click', () => {
            document.querySelectorAll('.dok-menu.show').forEach(m => {
                m.classList.remove('show');
                _nwClearMenuPos(m);
            });
        }, { once: true });
    }, 10);
}
function nwStartEdit(empId) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const v = document.getElementById('nwView_' + empId), e = document.getElementById('nwEdit_' + empId);
    if (v) v.style.display = 'none';
    if (e) e.style.display = 'flex';
    const inp = document.getElementById('nwDateInput_' + empId);
    if (inp) inp.focus();
}
function nwCancelEdit(empId) {
    const v = document.getElementById('nwView_' + empId), e = document.getElementById('nwEdit_' + empId);
    if (e) e.style.display = 'none';
    // Inline-Style weg → .nw-layout (CSS) übernimmt wieder die feste Zeilenstruktur.
    if (v) v.style.display = '';
}
// Verknüpftes Nachtarbeit-Dokument (Arztzeugnis / Ausnahmeregelung) vom MA lösen —
// nur die Verknüpfung wird entfernt, das Dokument selbst bleibt erhalten.
async function nwUnlinkDoku(empId, kind, label) {
    if (!(await liquidConfirm(`${label} von diesem Mitarbeiter lösen?\n\nNur die Verknüpfung wird entfernt — das Dokument selbst bleibt im Dokumente-Tab erhalten.`))) return;
    try {
        const res = await fetch(`/api/employees/${empId}/ausweis-doku`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ kind, dokumentId: null })
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || `Lösen fehlgeschlagen (${res.status})`);
            return;
        }
        if (typeof selectEmployee === 'function') selectEmployee(empId);
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}
function _nwHasArztDoc(empId) {
    const emp = (typeof selectedEmployee !== 'undefined' && selectedEmployee && selectedEmployee.id === empId)
        ? selectedEmployee : null;
    return !!(emp && emp.nightWorkExamDokumentId);
}

// Live-Vorschau „gültig bis" während des Tippens (+2 Jahre).
function nwPreview(empId, val) {
    const v = val ? _nwAddYears(val, 2) : null;
    const s = document.getElementById('nwGueltigBis_' + empId);
    if (s) s.innerHTML = _nwGueltigBisHtml(v, _nwHasArztDoc(empId));
}
// Speichern aus dem Editiermodus — danach zurück in die Read-only-Ansicht.
async function nwSaveEdit(empId) {
    const inp = document.getElementById('nwDateInput_' + empId);
    const issueVal = inp ? inp.value : '';
    if (issueVal) {
        const y = parseInt(String(issueVal).slice(0, 4), 10);
        if (!y || y < 1990 || y > new Date().getFullYear() + 1) { alert('Bitte ein gültiges Ausstellungsdatum eingeben.'); return; }
    }
    try {
        const res = await fetch(`/api/employees/${empId}/night-work-exam-date`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ issued: issueVal || null })
        });
        if (!res.ok) {
            let body = ''; try { body = await res.text(); } catch (_) {}
            alert('Speichern des Datums fehlgeschlagen (HTTP ' + res.status + ').\n' + (body || '').slice(0, 300));
            return;
        }
        const data = await res.json().catch(() => ({}));
        const validUntil = data.nightWorkExamValidUntil || null;
        const vt = document.getElementById('nwViewText_' + empId);
        // Manuell erfasst = immer regelkonform → keine Abweichungswarnung.
        // Grün erst mit verknüpftem Scan (hasArztDoc).
        if (vt) vt.innerHTML = _nwViewTextHtml(issueVal || null, validUntil, false, validUntil, _nwHasArztDoc(empId));
        nwCancelEdit(empId);
    } catch (e) { alert('Netzwerkfehler beim Speichern.'); }
}

// Nachtarbeit-Untersuchung: AUSSTELLUNGSdatum erfassen (Walter 20.06.2026) —
// gespeichert wird „gültig bis" = Ausstellung + 2 Jahre (ArG). Dedizierter PATCH,
// rührt keine anderen Anstellungs-Felder an.
async function saveNightExamDate(empId, issueVal) {
    // Native date-Inputs feuern „change" auch bei halb getipptem Jahr (z.B. 0020-…) →
    // erst speichern, wenn das Jahr plausibel ist. Sonst würde jeder Zwischenstand
    // gespeichert/abgelehnt und man könnte das Jahr nie fertig tippen.
    if (issueVal) {
        const y = parseInt(String(issueVal).slice(0, 4), 10);
        if (!y || y < 1990 || y > new Date().getFullYear() + 1) return;
    }
    try {
        const res = await fetch(`/api/employees/${empId}/night-work-exam-date`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ issued: issueVal || null })
        });
        if (!res.ok) {
            let body = ''; try { body = await res.text(); } catch (_) {}
            alert('Speichern des Datums fehlgeschlagen (HTTP ' + res.status + ').\n' + (body || '').slice(0, 300));
            return;
        }
        const data = await res.json().catch(() => ({}));
        // In-place aktualisieren statt selectEmployee → Fokus/Cursor bleibt im Feld.
        const span = document.getElementById('nwGueltigBis_' + empId);
        if (span) span.innerHTML = _nwGueltigBisHtml(data.nightWorkExamValidUntil || null, _nwHasArztDoc(empId));
    } catch (e) { alert('Netzwerkfehler beim Speichern.'); }
}

async function qstBefreiungAufheben(empId) {
    if (!(await liquidConfirm('Soll die Behörden-Befreiung wirklich aufgehoben werden? Der MA wird danach wieder QST-pflichtig.'))) return;
    const res = await fetch(`/api/employees/${empId}/qst-befreiung`, {
        method: 'PATCH', headers: { ...ah(), 'Content-Type':'application/json' },
        body: JSON.stringify({ befreit: false })
    });
    if (await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) { alert('Fehler beim Aufheben.'); return; }
    loadQuellensteuerTab(empId);
}

// Walter-Vorgabe 28.05.2026: hinterlegtes Befreiungs-Dokument aus dem QST-Tab-
// Banner heraus im Vorschau-Side-Panel öffnen. dokOpenPreviewPanel kommt aus
// documents.js und erwartet das Dokument in _dokState.docs. Wenn die Dokumenten-
// Tab dieses MA noch nie geöffnet wurde, ist die Liste leer → erst lazy laden.
async function qstOpenBefreiungsDok(empId, dokId, opts) {
    if (!dokId) return;
    try {
        const needsLoad = !_dokState || !_dokState.docs ||
                          _dokState.empId !== empId ||
                          !_dokState.docs.find(d => d.id === dokId);
        if (needsLoad) {
            const res = await fetch(`/api/documents/by-employee/${empId}`, { headers: ah() });
            if (res.ok) {
                _dokState.empId = empId;
                _dokState.docs  = await res.json();
            }
        }
    } catch { /* best-effort; dokOpenPreviewPanel rendert sonst nichts */ }
    if (typeof dokOpenPreviewPanel === 'function') {
        dokOpenPreviewPanel(dokId, opts);
    } else {
        alert('Vorschau-Modul nicht geladen.');
    }
}

// Schnell-Button: Höchsten Tarif erfassen → öffnet das normale QST-Modal mit
// vorausgefüllten Werten (A0Y für ledig, C0Y für verheiratet — beide mit
// Kirchensteuer, höchste Belastung in der jeweiligen Tarif-Gruppe).
//
// Walter-Vorgabe 14.06.2026: async + KEIN Auto-Vorschlag-Override mehr.
// Vorher lief das Setzen via setTimeout(100ms) als Race gegen
// openQstFromTab → openQstEntry → qstApplyServerVorschlagToForm, was
// unzuverlässig war (mal A0Y, mal A1N je nach Familie/Stichtag). Jetzt:
// 1) await openQstFromTab(null) → der Server-Vorschlag steht stabil im Feld.
// 2) Manuell auf den HÖCHSTEN Tarif überschreiben (Walter-Wille überstimmt
//    den Algorithmus).
// 3) qstSuggestTarif() rendert den Banner — er erkennt jetzt „bewusst Y
//    gewählt" und zeigt das transparent an.
async function openQstHoechsterTarif() {
    if (!selectedEmployee) return;
    const isVerheiratet = (selectedEmployee.maritalStatus || '').toLowerCase().includes('verheiratet')
                       || (selectedEmployee.maritalStatus || '').toLowerCase().includes('partnerschaft');
    const tarifCode = isVerheiratet ? 'C' : 'A';

    await openQstFromTab(null);

    const setVal = (id, v) => { const el = document.getElementById(id); if (el) el.value = v; };
    setVal('qstTarifCode', tarifCode);
    setVal('qstKinder',    '0');             // höchste Stufe = 0 Kinder
    const kirche = document.getElementById('qstKirchensteuer');
    if (kirche) kirche.checked = true;       // mit Kirchensteuer = höchste Belastung
    if (typeof buildQstCode === 'function') buildQstCode();

    // Banner aktualisieren — zeigt jetzt „Server-Vorschlag wäre X, du hast
    // bewusst Y gewählt" (gewünschte Transparenz, kein erneutes Auto-Apply).
    if (typeof qstSuggestTarif === 'function') qstSuggestTarif();
    if (typeof qstUpdateAutoKinderHint === 'function') qstUpdateAutoKinderHint();
}

// Behörden-Befreiung erfassen — Modal mit direktem Upload ODER bestehendem Doku
async function openQstBefreiungModal() {
    if (!selectedEmployeeId) return;
    // Walter-Vorgabe 13.06.2026: jetzt das gleiche schlanke Auswahl+Upload-
    // Modal wie für Ausweis-Doku — nur mit kind='behoerden_befreiung'. Das
    // Modal filtert/highlights "Befreiung/Behörde/Quellensteuer"-Typen,
    // Direct-Pick verknüpft sofort, Upload-Pfad nimmt Gültig-ab/bis aus dem
    // Upload-Modal. Alles unten als toter Fallback-Code für Notfall.
    return openAusweisDokuModal(selectedEmployeeId, 'behoerden_befreiung');

    // ──────────────────────────────────────────────────────────────────
    // ALTE VARIANTE (deaktiviert seit 13.06.2026, bleibt als Fallback)
    // ──────────────────────────────────────────────────────────────────
    /* eslint-disable */
    const empId = selectedEmployeeId;
    if (typeof loadEmpDokumente === 'function') {
        try { await loadEmpDokumente(empId); } catch {}
    }
    if (typeof _dokState !== 'undefined' && _dokState.empId === empId) {
        // Passenden Typ in der Dokument-Struktur vorauswählen — Name-Match
        // auf „Quellensteuer Befreiung" o.ä. (kein linked_field_code für
        // diese Befreiung). Fallback: irgendetwas mit „befreiung" im Namen.
        let foundTyp = null, foundKat = null;
        const tax = Array.isArray(_dokState.taxonomy) ? _dokState.taxonomy : [];
        const isQstName = n => /quellensteuer\s*befreiung|qst\s*befreiung/i.test(n || '');
        for (const k of tax) {
            for (const t of (k.typen || [])) {
                if (isQstName(t.name)) { foundTyp = t; foundKat = k; break; }
            }
            if (foundTyp) break;
        }
        if (!foundTyp) {
            for (const k of tax) {
                for (const t of (k.typen || [])) {
                    if (/befreiung/i.test(t.name || '')) { foundTyp = t; foundKat = k; break; }
                }
                if (foundTyp) break;
            }
        }
        _dokState.selectedKategorieId = foundKat?.id || null;
        _dokState.selectedTypId       = foundTyp?.id || null;

        _dokState.afterUpload = async (newDokId, _raw, form) => {
            if (!newDokId) return;
            try {
                const res = await fetch(`/api/employees/${empId}/qst-befreiung`, {
                    method: 'PATCH',
                    headers: { ...ah(), 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        befreit:    true,
                        dokumentId: newDokId,
                        gueltigAb:  form?.gueltigVon || null,
                        gueltigBis: form?.gueltigBis || null
                    })
                });
                if (!res.ok) {
                    const j = await res.json().catch(() => null);
                    alert(j?.message || `Befreiung speichern fehlgeschlagen (${res.status})`);
                    return;
                }
                loadQuellensteuerTab(empId);
            } catch (e) {
                alert('Verbindungsfehler: ' + e.message);
            }
        };

        if (typeof openDokUploadModal === 'function') {
            openDokUploadModal();
        } else {
            alert('Doku-Upload-Modal nicht geladen. Bitte Seite neu laden.');
            _dokState.afterUpload = null;
        }
        return;
    }
    // Fallback: wenn _dokState nicht initialisiert ist, zum alten Modal-
    // Pfad weiterlaufen (toter Code unten — Sicherheits-Backup).
    // ──────────────────────────────────────────────────────────────────
    // ALTE VARIANTE (deaktiviert seit 13.06.2026, bleibt als Fallback)
    // ──────────────────────────────────────────────────────────────────
    const [dokRes, taxRes] = await Promise.all([
        fetch(`/api/documents/by-employee/${selectedEmployeeId}`, { headers: ah() }),
        fetch('/api/documents/taxonomie', { headers: ah() })
    ]);
    const dokumente = dokRes.ok ? await dokRes.json() : [];
    const taxonomy  = taxRes.ok ? await taxRes.json() : [];

    // Walter-Vorgabe 28.05.2026 (Teil 2): Typ-Picker beim Hochladen optisch
    // wie die Dokumente-Verwaltung (Kategorie-Pill + Typ-Name, klickbare Zeilen)
    // statt plain <select>. Reihenfolge: QST/Befreiungs-Typen zuerst (Treffer auf
    // Typ-Namen wie „Quellensteuer Befreiung" — der liegt unter „Lohn / Arbeitszeit",
    // nicht unter „Ämter & Behörden"), dann die übrigen Typen darunter.
    const _allTypes = (taxonomy || []).flatMap(k =>
        (k.typen || []).map(t => ({ id: t.id, name: t.name, katName: k.name, katId: k.id }))
    );
    const _isQstTyp = t =>
        /befreiung|quellensteuer|qst|ämter|behörd|bestätigung/i.test(t.name + ' ' + t.katName);
    const _typQst   = _allTypes.filter(_isQstTyp);
    const _typRest  = _allTypes.filter(t => !_isQstTyp(t));
    _typQst.sort((a, b) => a.name.localeCompare(b.name));
    _typRest.sort((a, b) => (a.katName || '').localeCompare(b.katName || '') || a.name.localeCompare(b.name));
    // Default-Auswahl: bevorzugt der Typ „Quellensteuer Befreiung" — sonst der
    // erste QST-/Befreiungs-Typ. Wenn keiner existiert, bleibt die Auswahl leer.
    const _defaultTyp = _typQst.find(t => /quellensteuer\s*befreiung|qst\s*befreiung/i.test(t.name))
                     || _typQst.find(t => /befreiung/i.test(t.name))
                     || _typQst[0]
                     || null;

    // Walter-Vorgabe 28.05.2026: Dokument-Picker wie Dokumente-Verwaltung —
    // klickbare Tabelle mit Kategorie-Pill / Typ / Beschreibung+Datum statt
    // plain <select>. Nutzt dieselben CSS-Klassen (.dok-table, .dok-cat-pill)
    // und denselben Slug (dokCatSlug aus documents.js), damit das Modal
    // visuell mit der Dokumenten-Verwaltung übereinstimmt.
    const fmtDate = (iso) => {
        if (!iso) return '–';
        const s = String(iso).slice(0, 10);
        if (s.length !== 10) return '–';
        return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
    };
    const dokSorted = (dokumente || [])
        .slice()
        .sort((a, b) => String(b.geaendertAm || b.hochgeladenAm || '')
                         .localeCompare(String(a.geaendertAm || a.hochgeladenAm || '')));
    const isBehoerden = d =>
        /ämter|behörd|quellensteuer|qst/i.test((d.kategorieName || '') + ' ' + (d.dokumentTypName || ''));
    // Behörden/QST-Treffer zuerst, dann der Rest — innerhalb beider Gruppen
    // bleibt die neueste-zuerst-Sortierung erhalten.
    const dokOrdered = [
        ...dokSorted.filter(isBehoerden),
        ...dokSorted.filter(d => !isBehoerden(d)),
    ];
    const slug = (typeof dokCatSlug === 'function')
        ? dokCatSlug
        : (n => (n || '').toLowerCase()
            .replace(/ä/g,'ae').replace(/ö/g,'oe').replace(/ü/g,'ue').replace(/ß/g,'ss')
            .replace(/[^a-z0-9]+/g,'-').replace(/^-+|-+$/g,''));
    const renderDokRow = d => {
        const catSlug = slug(d.kategorieName);
        const erstelltIso = d.erstelltAm || d.gueltigVon || d.hochgeladenAm;
        const dateLine = `${fmtDate(erstelltIso)} · ${fmtDate(d.geaendertAm)} · ${fmtDate(d.zugriffAm)}`;
        const ext = ((d.filenameOriginal || '').toLowerCase().match(/\.[^.]+$/) || [''])[0];
        const isPdf = d.mimeType === 'application/pdf';
        const isImg = (d.mimeType || '').startsWith('image/');
        const icon  = isPdf ? '📄' : isImg ? '🖼️' : '📎';
        const fname = esc(d.filenameOriginal || ('Dokument #' + d.id));
        const bem   = d.bemerkung ? `<b>${esc(d.bemerkung)}</b>` : `<span style="color:#cbd5e1">${fname}</span>`;
        const nameLine = d.bemerkung
            ? `${icon} ${bem} <span style="color:#94a3b8;font-weight:400;font-size:11.5px">· ${fname}</span>`
            : `${icon} ${bem}`;
        return `<tr data-dok-id="${d.id}"
                    data-dok-cat="${esc((d.kategorieName || '').toLowerCase())}"
                    data-dok-search="${esc((d.filenameOriginal||'') + ' ' + (d.bemerkung||'') + ' ' + (d.kategorieName||'') + ' ' + (d.dokumentTypName||'')).toLowerCase()}"
                    onclick="qstBefSelectDok(${d.id})"
                    style="cursor:pointer">
            <td><span class="dok-cat-pill cat-${catSlug}">${esc(d.kategorieName || '')}</span></td>
            <td style="color:#475569">${esc(d.dokumentTypName || '')}</td>
            <td>${nameLine}<br><span class="dok-meta-inline">${dateLine}</span></td>
        </tr>`;
    };
    // Walter-Vorgabe 28.05.2026 (Teil 3): Oberkategorie-Chips über der Liste
    // — wie der Kategorie-Tree links in der Dokumenten-Verwaltung. Reihenfolge
    // wie in der Taxonomie selbst, Anzahl pro Kategorie in einem grauen Badge.
    // Default-Chip „Alle" — sobald der User auf eine Kategorie klickt, blendet
    // die Liste alles andere aus. Kombiniert mit dem Text-Filter darunter.
    const catCounts = {};
    dokOrdered.forEach(d => {
        const k = d.kategorieName || '';
        catCounts[k] = (catCounts[k] || 0) + 1;
    });
    const taxOrder = (taxonomy || []).map(k => k.name).filter(n => catCounts[n]);
    // Eventuell vorhandene Kategorien, die NICHT in der Taxonomie stehen (Alt-Daten),
    // hängen ans Ende, damit kein Doku im Filter verloren geht.
    Object.keys(catCounts).forEach(n => { if (!taxOrder.includes(n)) taxOrder.push(n); });
    const renderCatChip = (label, count, value, active) => {
        const catSlug = value ? slug(value) : '';
        const pillClass = value ? `dok-cat-pill cat-${catSlug}` : '';
        const baseStyle = active
            ? 'background:#0f172a;color:#fff;border:1px solid #0f172a'
            : 'background:#fff;color:#0f172a;border:1px solid #e2e8f0';
        // Wenn nicht aktiv UND eine Kategorie-Farbe existiert, zeigen wir die Pill
        // mit der Original-Farbe (wie in der Doku-Verwaltung). Aktive Chip ist
        // immer dunkel/weiss als klarer Selected-Marker.
        if (active || !value) {
            return `<button type="button" class="qst-bef-chip" data-cat="${esc((value||'').toLowerCase())}"
                            onclick="qstBefFilterCat('${esc(value||'').replace(/'/g,'\\\'')}', this)"
                            style="${baseStyle};padding:4px 10px;border-radius:999px;font-size:11.5px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:6px">
                        ${esc(label)}
                        <span style="background:${active ? 'rgba(255,255,255,.2)' : '#f1f5f9'};color:${active ? '#fff' : '#64748b'};padding:1px 6px;border-radius:999px;font-size:10.5px;font-weight:600">${count}</span>
                    </button>`;
        }
        return `<button type="button" class="qst-bef-chip ${pillClass}" data-cat="${esc((value||'').toLowerCase())}"
                        onclick="qstBefFilterCat('${esc(value||'').replace(/'/g,'\\\'')}', this)"
                        style="padding:4px 10px;border-radius:999px;font-size:11.5px;font-weight:600;cursor:pointer;border:1px solid transparent;display:inline-flex;align-items:center;gap:6px">
                    ${esc(label)}
                    <span style="background:rgba(15,23,42,.08);color:#64748b;padding:1px 6px;border-radius:999px;font-size:10.5px;font-weight:600">${count}</span>
                </button>`;
    };
    const dokChipsHtml = dokOrdered.length
        ? `<div id="qstBefDokChips" style="display:flex;flex-wrap:wrap;gap:6px;margin-bottom:8px">
             ${renderCatChip('Alle', dokOrdered.length, '', true)}
             ${taxOrder.map(n => renderCatChip(n, catCounts[n], n, false)).join('')}
           </div>`
        : '';
    const dokTableHtml = dokOrdered.length
        ? `<div style="border:1px solid #e2e8f0;border-radius:6px;max-height:280px;overflow-y:auto;background:#fff">
             <table class="dok-table" id="qstBefDokTable" style="font-size:12px">
               <thead><tr><th style="width:160px">Kategorie</th><th style="width:130px">Typ</th><th>Beschreibung<div style="font-weight:400;font-size:10px;color:#94a3b8;margin-top:2px;text-transform:none;letter-spacing:0">Erstellt · Geändert · Geöffnet</div></th></tr></thead>
               <tbody>${dokOrdered.map(renderDokRow).join('')}</tbody>
             </table>
           </div>
           <div id="qstBefDokNoMatch" style="display:none;padding:14px;text-align:center;color:#94a3b8;font-size:12px">Kein Dokument entspricht der Auswahl.</div>`
        : `<div style="padding:18px;text-align:center;color:#dc2626;font-size:12px;background:#fef2f2;border:1px dashed #fecaca;border-radius:6px">
             ⚠ Bei diesem Mitarbeiter sind noch keine Dokumente hochgeladen. Bitte unter <strong>A</strong> direkt das Bestätigungsschreiben hochladen.
           </div>`;
    const dokSearchHtml = dokOrdered.length > 4
        ? `<input type="text" id="qstBefDokSearch" placeholder="Filtern (Name, Bemerkung, Typ)…"
                  oninput="qstBefDokApplyFilters()"
                  style="width:100%;padding:6px 8px;border:1px solid #cbd5e1;border-radius:5px;font-size:12px;margin-bottom:8px">`
        : '';
    const dokSelectedLabel = dokOrdered.length
        ? `<div id="qstBefDokSelected" style="font-size:11.5px;color:#94a3b8;margin-top:6px">Noch kein Dokument ausgewählt — Zeile anklicken.</div>`
        : '';

    // --- Typ-Picker (Option A) im gleichen Look wie der Doku-Picker ---
    // Aufbau: 2-spaltige Tabelle mit Kategorie-Pill + Typ-Name. QST-/Befreiungs-
    // relevante Typen sind oben gruppiert (visuell hervorgehoben), die übrigen
    // Typen darunter zum Aufklappen, damit das Modal kompakt bleibt.
    const renderTypRow = (t, opts) => {
        const o = opts || {};
        const catSlug = slug(t.katName);
        const isDefault = _defaultTyp && _defaultTyp.id === t.id;
        const sel = isDefault ? 'background:#ece9e2;outline:2px solid #1a1a1a;outline-offset:-2px' : '';
        const hidden = o.hidden ? 'display:none' : '';
        const extraClass = o.rest ? 'qst-typ-rest' : '';
        const style = [hidden, 'cursor:pointer', sel].filter(Boolean).join(';');
        return `<tr class="${extraClass}" data-typ-id="${t.id}" data-typ-search="${esc((t.name + ' ' + t.katName).toLowerCase())}"
                    onclick="qstBefSelectTyp(${t.id})"
                    style="${style}">
            <td><span class="dok-cat-pill cat-${catSlug}">${esc(t.katName || '')}</span></td>
            <td style="color:#0f172a;font-weight:${o.highlight ? 600 : 500}">${esc(t.name || '')}</td>
        </tr>`;
    };
    const _hasRest = _typRest.length > 0;
    const typTableHtml = _allTypes.length
        ? `<div style="border:1px solid #e2e8f0;border-radius:6px;background:#fff;max-height:240px;overflow-y:auto">
             <table class="dok-table" id="qstBefTypTable" style="font-size:12px">
               <thead><tr><th style="width:180px">Kategorie</th><th>Typ</th></tr></thead>
               <tbody>
                 ${_typQst.length ? `<tr><td colspan="2" style="background:#f8fafc;color:#64748b;font-size:10.5px;text-transform:uppercase;letter-spacing:.04em;padding:6px 12px">Für QST-Befreiung empfohlen</td></tr>` : ''}
                 ${_typQst.map(t => renderTypRow(t, { highlight: true })).join('')}
                 ${_hasRest ? `<tr id="qstBefTypRestToggleRow"><td colspan="2" style="padding:6px 12px;background:#f8fafc">
                    <button type="button" id="qstBefTypRestToggle" onclick="qstBefTypToggleRest()"
                            style="background:transparent;border:none;color:#1a1a1a;font-size:11.5px;cursor:pointer;font-weight:600">
                        Weitere Typen anzeigen (${_typRest.length}) ▾
                    </button></td></tr>` : ''}
                 ${_typRest.map(t => renderTypRow(t, { hidden: true, rest: true })).join('')}
               </tbody>
             </table>
           </div>
           <input type="hidden" id="qstBefTyp" value="${_defaultTyp ? _defaultTyp.id : ''}">
           <div id="qstBefTypSelected" style="font-size:11.5px;color:${_defaultTyp ? '#0f5132' : '#dc2626'};margin-top:6px">
              ${_defaultTyp
                ? `✓ ausgewählt: <b>${esc(_defaultTyp.name)}</b> <span style="color:#94a3b8">· ${esc(_defaultTyp.katName)}</span>`
                : '⚠ Kein Doku-Typ verfügbar — bitte in der Dokument-Verwaltung anlegen.'}
           </div>`
        : `<input type="hidden" id="qstBefTyp" value="">
           <div style="font-size:11.5px;color:#dc2626;padding:8px 0">⚠ Kein Doku-Typ verfügbar — bitte in der Dokument-Verwaltung anlegen.</div>`;

    const html = `
    <div id="qstBefreiungModal" style="position:fixed;inset:0;background:rgba(15,23,42,.55);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px"
         onclick="if(event.target===this)document.getElementById('qstBefreiungModal').remove()">
      <div style="background:#fff;border-radius:14px;max-width:780px;width:100%;padding:24px;box-shadow:0 20px 50px rgba(0,0,0,.25);max-height:92vh;overflow-y:auto">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:14px">
            <div style="font-size:16px;font-weight:700;color:#0f172a">Behörden-Befreiung erfassen</div>
            <button onclick="document.getElementById('qstBefreiungModal').remove()" style="background:transparent;border:none;font-size:20px;color:#64748b;cursor:pointer">×</button>
        </div>
        <div style="background:#fef3c7;border-left:3px solid #f59e0b;padding:10px 12px;border-radius:6px;font-size:12px;color:#78350f;margin-bottom:16px">
            Befreiung gilt nur mit gültigem Bestätigungsschreiben der Steuerbehörde. Datei direkt hochladen <strong>oder</strong> ein bereits abgelegtes Dokument verlinken.
        </div>

        <!-- Option A: Datei direkt hochladen -->
        <div style="border:1px solid #cbd5e1;border-radius:8px;padding:14px;margin-bottom:12px">
            <div style="font-size:12px;font-weight:700;color:#5a5348;margin-bottom:8px;text-transform:uppercase;letter-spacing:.04em">A · Datei hochladen</div>
            <input type="file" id="qstBefFile" accept=".pdf,image/*" style="width:100%;font-size:12.5px"
                   onchange="qstBefFileChanged(this)">
            <div id="qstBefFileLabel" style="font-size:11.5px;color:#64748b;margin-top:6px"></div>
            <div style="margin-top:12px">
                <label style="display:block;font-size:12px;font-weight:600;color:#374151;margin-bottom:6px">Doku-Typ</label>
                ${typTableHtml}
            </div>
        </div>

        <div style="text-align:center;color:#94a3b8;font-size:11px;margin:8px 0;text-transform:uppercase;letter-spacing:.08em">— oder —</div>

        <!-- Option B: Bestehendes Dokument auswählen -->
        <div style="border:1px solid #cbd5e1;border-radius:8px;padding:14px;margin-bottom:16px">
            <div style="font-size:12px;font-weight:700;color:#5a5348;margin-bottom:8px;text-transform:uppercase;letter-spacing:.04em">B · Bestehendes Dokument</div>
            <input type="hidden" id="qstBefDok" value="">
            ${dokChipsHtml}
            ${dokSearchHtml}
            ${dokTableHtml}
            ${dokSelectedLabel}
        </div>

        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-bottom:18px">
            <div>
                <label style="display:block;font-size:12px;font-weight:600;color:#374151;margin-bottom:6px">Gültig ab *</label>
                <input type="date" id="qstBefAb" style="width:100%;padding:8px;border:1px solid #cbd5e1;border-radius:5px;font-size:13px">
            </div>
            <div>
                <label style="display:block;font-size:12px;font-weight:600;color:#374151;margin-bottom:6px">Gültig bis</label>
                <input type="date" id="qstBefBis" style="width:100%;padding:8px;border:1px solid #cbd5e1;border-radius:5px;font-size:13px" placeholder="leer = unbefristet">
            </div>
        </div>
        <div id="qstBefStatus" style="font-size:12px;color:#64748b;margin-bottom:10px"></div>
        <div style="display:flex;justify-content:flex-end;gap:8px">
            <button onclick="document.getElementById('qstBefreiungModal').remove()" style="background:#fff;border:1px solid #cbd5e1;color:#475569;padding:8px 16px;border-radius:6px;font-size:13px;cursor:pointer">Abbrechen</button>
            <button id="qstBefSaveBtn" onclick="qstBefreiungSpeichern()" style="background:#16a34a;color:#fff;border:none;padding:8px 16px;border-radius:6px;font-size:13px;font-weight:600;cursor:pointer">Befreiung speichern</button>
        </div>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

function qstBefFileChanged(input) {
    const lbl = document.getElementById('qstBefFileLabel');
    if (input.files && input.files[0]) {
        const f = input.files[0];
        lbl.textContent = `Ausgewählt: ${f.name} (${Math.round(f.size/1024)} KB)`;
        // Bei Datei-Auswahl: Bestehendes-Doku-Picker zurücksetzen (Option A wins).
        const hidden = document.getElementById('qstBefDok');
        if (hidden) hidden.value = '';
        document.querySelectorAll('#qstBefDokTable tr[data-dok-id]').forEach(tr => {
            tr.style.background = '';
            tr.style.outline = '';
        });
        const sel = document.getElementById('qstBefDokSelected');
        if (sel) {
            sel.innerHTML = 'Noch kein Dokument ausgewählt — Zeile anklicken.';
            sel.style.color = '#94a3b8';
        }
    } else {
        lbl.textContent = '';
    }
}

// Walter-Vorgabe 28.05.2026: Dokument-Picker im qstBefreiungModal — Klick auf
// Zeile setzt den verdeckten Wert + markiert die Zeile blau. Optionaler
// Live-Filter darüber (Filename / Bemerkung / Kategorie / Typ).
function qstBefSelectDok(dokId) {
    const hidden = document.getElementById('qstBefDok');
    if (hidden) hidden.value = String(dokId);
    // Datei-Upload-Feld räumen (Option B wins)
    const fileInp = document.getElementById('qstBefFile');
    if (fileInp) fileInp.value = '';
    const lbl = document.getElementById('qstBefFileLabel');
    if (lbl) lbl.textContent = '';
    // Zeile markieren
    let picked = null;
    document.querySelectorAll('#qstBefDokTable tr[data-dok-id]').forEach(tr => {
        if (parseInt(tr.getAttribute('data-dok-id'), 10) === dokId) {
            tr.style.background = '#ece9e2';
            tr.style.outline = '2px solid #1a1a1a';
            tr.style.outlineOffset = '-2px';
            picked = tr;
        } else {
            tr.style.background = '';
            tr.style.outline = '';
        }
    });
    const sel = document.getElementById('qstBefDokSelected');
    if (sel && picked) {
        // Beschreibung der ausgewählten Zeile (Kategorie / Typ / Beschreibung)
        const tds = picked.querySelectorAll('td');
        const kat = tds[0] ? tds[0].innerText.trim() : '';
        const typ = tds[1] ? tds[1].innerText.trim() : '';
        const beschr = tds[2] ? tds[2].innerText.trim().split('\n')[0] : '';
        sel.innerHTML = `✓ ausgewählt: <b>${esc(beschr)}</b> <span style="color:#94a3b8">· ${esc(kat)} / ${esc(typ)}</span>`;
        sel.style.color = '#0f5132';
    }
}

// Walter-Vorgabe 28.05.2026 (Teil 3): kombinierter Filter aus Kategorie-Chip
// + Text-Suche. _qstBefDokCat hält den aktiven Chip-Wert (leer = „Alle").
let _qstBefDokCat = '';
function qstBefDokApplyFilters() {
    const inp = document.getElementById('qstBefDokSearch');
    const needle = (inp ? inp.value : '').toLowerCase().trim();
    const cat = (_qstBefDokCat || '').toLowerCase();
    let shown = 0;
    document.querySelectorAll('#qstBefDokTable tr[data-dok-id]').forEach(tr => {
        const hay   = tr.getAttribute('data-dok-search') || '';
        const trCat = tr.getAttribute('data-dok-cat')    || '';
        const okText = !needle || hay.includes(needle);
        const okCat  = !cat    || trCat === cat;
        const match  = okText && okCat;
        tr.style.display = match ? '' : 'none';
        if (match) shown++;
    });
    const empty = document.getElementById('qstBefDokNoMatch');
    if (empty) empty.style.display = shown === 0 ? 'block' : 'none';
}

// Legacy-Alias — wird nicht mehr von neuen DOM-Listenern aufgerufen, bleibt
// aber stehen falls externe Caller die alte Signatur erwarten.
function qstBefFilterDok(_q) { qstBefDokApplyFilters(); }

// Kategorie-Chip klicken: aktiven Chip markieren + Liste filtern.
function qstBefFilterCat(catValue, btnEl) {
    _qstBefDokCat = catValue || '';
    // Chip-Highlights aktualisieren: aktive Chip dunkel-gefüllt, alle anderen
    // zurück auf Original-Pill-Optik bzw. neutrale „Alle"-Optik.
    const chips = document.querySelectorAll('#qstBefDokChips .qst-bef-chip');
    chips.forEach(c => {
        const isActive = c === btnEl;
        const catAttr  = c.getAttribute('data-cat') || '';
        const isAlle   = !catAttr;
        // Klassen-Reset
        c.className = 'qst-bef-chip';
        if (isActive) {
            c.style.background = '#0f172a';
            c.style.color      = '#fff';
            c.style.border     = '1px solid #0f172a';
            const badge = c.querySelector('span');
            if (badge) { badge.style.background = 'rgba(255,255,255,.2)'; badge.style.color = '#fff'; }
        } else if (isAlle) {
            c.style.background = '#fff';
            c.style.color      = '#0f172a';
            c.style.border     = '1px solid #e2e8f0';
            const badge = c.querySelector('span');
            if (badge) { badge.style.background = '#f1f5f9'; badge.style.color = '#64748b'; }
        } else {
            // Kategorie-Pill in Original-Farbe (via .dok-cat-pill CSS-Klasse)
            const slugFn = (typeof dokCatSlug === 'function')
                ? dokCatSlug
                : (n => (n || '').toLowerCase()
                    .replace(/ä/g,'ae').replace(/ö/g,'oe').replace(/ü/g,'ue').replace(/ß/g,'ss')
                    .replace(/[^a-z0-9]+/g,'-').replace(/^-+|-+$/g,''));
            c.className = `qst-bef-chip dok-cat-pill cat-${slugFn(catAttr)}`;
            c.style.background = '';
            c.style.color      = '';
            c.style.border     = '1px solid transparent';
            const badge = c.querySelector('span');
            if (badge) { badge.style.background = 'rgba(15,23,42,.08)'; badge.style.color = '#64748b'; }
        }
    });
    qstBefDokApplyFilters();
}

// Walter-Vorgabe 28.05.2026: Typ-Picker (Option A) — Klick auf eine Zeile
// markiert den gewählten Doku-Typ; identische Highlight-Optik wie der
// Doku-Picker oben.
function qstBefSelectTyp(typId) {
    const hidden = document.getElementById('qstBefTyp');
    if (hidden) hidden.value = String(typId);
    let picked = null;
    document.querySelectorAll('#qstBefTypTable tr[data-typ-id]').forEach(tr => {
        if (parseInt(tr.getAttribute('data-typ-id'), 10) === typId) {
            tr.style.background = '#ece9e2';
            tr.style.outline = '2px solid #1a1a1a';
            tr.style.outlineOffset = '-2px';
            picked = tr;
        } else {
            tr.style.background = '';
            tr.style.outline = '';
        }
    });
    const sel = document.getElementById('qstBefTypSelected');
    if (sel && picked) {
        const tds = picked.querySelectorAll('td');
        const kat = tds[0] ? tds[0].innerText.trim() : '';
        const typ = tds[1] ? tds[1].innerText.trim() : '';
        sel.innerHTML = `✓ ausgewählt: <b>${esc(typ)}</b> <span style="color:#94a3b8">· ${esc(kat)}</span>`;
        sel.style.color = '#0f5132';
    }
}

// Aufklapp-Toggle für die „weiteren Typen" unterhalb der QST-empfohlenen Liste.
function qstBefTypToggleRest() {
    const rows = document.querySelectorAll('#qstBefTypTable tr.qst-typ-rest');
    const btn  = document.getElementById('qstBefTypRestToggle');
    if (!rows.length) return;
    const isHidden = rows[0].style.display === 'none';
    rows.forEach(r => { r.style.display = isHidden ? '' : 'none'; });
    if (btn) {
        const total = rows.length;
        btn.innerHTML = isHidden
            ? `Weitere Typen ausblenden ▴`
            : `Weitere Typen anzeigen (${total}) ▾`;
    }
}

async function qstBefreiungSpeichern() {
    const empId = selectedEmployeeId;
    const fileInput = document.getElementById('qstBefFile');
    const file = fileInput?.files?.[0] || null;
    let dokId = parseInt(document.getElementById('qstBefDok').value || '0', 10);
    const ab    = document.getElementById('qstBefAb').value;
    const bis   = document.getElementById('qstBefBis').value;
    const typId = parseInt(document.getElementById('qstBefTyp').value || '0', 10);
    const statusEl = document.getElementById('qstBefStatus');
    const saveBtn  = document.getElementById('qstBefSaveBtn');

    if (!ab)             { alert('Bitte das Gültig-ab-Datum eintragen.'); return; }
    if (!file && !dokId) { alert('Bitte eine Datei hochladen ODER ein bestehendes Dokument auswählen.'); return; }
    if (file && !typId)  { alert('Bitte den Dokument-Typ wählen.'); return; }

    saveBtn.disabled = true;
    saveBtn.textContent = 'Speichern…';

    // Walter-Vorgabe 26.05.2026: Direkt-Upload — erst Datei hochladen, dann
    // die zurückgekommene Dokument-ID an den Befreiungs-PATCH übergeben.
    if (file) {
        try {
            statusEl.textContent = 'Datei wird hochgeladen…';
            // Branch-Code des aktuellen MA aus dem aktiven Vertrag
            const empBranch = selectedEmployee?.employments?.find(e => e.isActive)?.companyProfile?.restaurantCode
                           ?? (typeof allBranches !== 'undefined'
                               ? (allBranches.find(b => b.id === (selectedEmployee?.companyProfileId
                                                                || fixedCompanyProfileId))?.restaurantCode)
                               : null);
            const form = new FormData();
            form.append('file', file);
            form.append('employeeId',   String(empId));
            form.append('dokumentTypId', String(typId));
            if (empBranch) form.append('branchCode', empBranch);
            form.append('bemerkung', 'QST-Befreiung Bestätigungsschreiben');
            // WICHTIG: KEIN ah() benutzen — das setzt Content-Type:application/json
            // und der Browser kann dann den multipart/form-data-Boundary nicht mehr
            // selbst setzen → Server meldet „The file field is required".
            // Nur den Authorization-Header schicken (analog js/documents.js → dokUpload).
            const upRes = await fetch('/api/documents/upload', {
                method: 'POST',
                headers: { 'Authorization': `Bearer ${authToken}` },
                body: form
            });
            if (!upRes.ok) {
                const txt = await upRes.text().catch(() => '');
                throw new Error(txt || 'Upload fehlgeschlagen');
            }
            const upBody = await upRes.json();
            dokId = upBody.id;
            statusEl.textContent = '';
        } catch (e) {
            statusEl.innerHTML = `<span style="color:#dc2626">${esc(e.message || 'Upload-Fehler')}</span>`;
            saveBtn.disabled = false;
            saveBtn.textContent = 'Befreiung speichern';
            return;
        }
    }

    const res = await fetch(`/api/employees/${empId}/qst-befreiung`, {
        method: 'PATCH', headers: { ...ah(), 'Content-Type':'application/json' },
        body: JSON.stringify({ befreit: true, dokumentId: dokId, gueltigAb: ab, gueltigBis: bis || null })
    });
    if (await window.lohnEditLock.handleResponse(res)) { saveBtn.disabled = false; saveBtn.textContent = 'Befreiung speichern'; return; }
    if (!res.ok) {
        const body = await res.clone().json().catch(() => ({}));
        alert(body.message || 'Fehler beim Speichern.');
        saveBtn.disabled = false;
        saveBtn.textContent = 'Befreiung speichern';
        return;
    }
    document.getElementById('qstBefreiungModal').remove();
    loadQuellensteuerTab(empId);
}

// Walter-Vorgabe 20.08.2026: roter Banner «Ehepartner-Angaben unvollständig»
// — der Lohnlauf ist gesperrt, bis Nationalität/Bewilligung/Erwerbstätig-
// Frage/Arbeitgeber des Ehepartners im Familie-Tab erfasst sind.
function renderQstPartnerBanner(pflicht) {
    if (!pflicht?.partnerDatenFehlen) return '';
    const maengel = (pflicht.partnerDatenMaengel || [])
        .map(m => `<li style="margin:2px 0">${esc(m)}</li>`).join('');
    return `
    <div style="background:#fef2f2;border:1px solid #fecaca;border-radius:12px;padding:12px 16px;margin-bottom:14px">
        <div style="font-weight:700;color:#b91c1c;font-size:13.5px;display:flex;align-items:center;gap:8px">
            ⚠ Ehepartner-Angaben unvollständig — Lohnlauf gesperrt
        </div>
        <ul style="margin:6px 0 8px 18px;padding:0;font-size:12.5px;color:#7f1d1d">${maengel}</ul>
        <button onclick="switchEmpTab('familie')"
                style="background:#3f3f3f;color:#fff;border:1px solid #1a1a1a;padding:5px 14px;border-radius:12px;font-size:12px;font-weight:600;cursor:pointer">
            → Ehepartner im Familie-Tab vervollständigen
        </button>
    </div>`;
}

// Walter-Vorgabe 20.08.2026: orange Tarif-Plausibilitäts-Warnungen (KS 45)
// — reine Hinweise, kein Block (verheiratet⇒B/C, C⇒Partner arbeitet, H-Regeln,
// A-Kinderziffer nur mit Behördenbewilligung).
function renderQstTarifWarnBanner(pflicht) {
    const w = pflicht?.tarifWarnungen || [];
    if (!w.length) return '';
    return `
    <div style="background:#fffbeb;border:1px solid #fde68a;border-radius:12px;padding:12px 16px;margin-bottom:14px">
        <div style="font-weight:700;color:#92400e;font-size:13px">Tarif prüfen (KS 45)</div>
        <ul style="margin:6px 0 0 18px;padding:0;font-size:12.5px;color:#854d0e">
            ${w.map(x => `<li style="margin:2px 0">${esc(x)}</li>`).join('')}
        </ul>
    </div>`;
}

function renderQuellensteuerTab(el, entries, pflicht) {
    // Walter-Vorgabe 26.05.2026: Pflicht-Banner OBEN (vor allem anderen).
    const banner = renderQstPflichtBanner(pflicht)
                 // Walter-Vorgabe 20.08.2026: Ehepartner-Angaben unvollständig
                 // (blockt Lohnlauf) + Tarif-Plausibilitäts-Warnungen.
                 + renderQstPartnerBanner(pflicht)
                 + renderQstTarifWarnBanner(pflicht)
                 // Walter-Vorgabe 04.08.2026: Kantonswechsel-Hinweis direkt
                 // darunter — Wohnkanton ≠ Kanton der aktuellen QST-Version
                 // → «🚚 Umzug erfassen» (Monatsregel Kreisschreiben 45).
                 + renderQstUmzugBanner(entries);
    // Bewilligungen + QST unter dem Pflicht-Banner. Bank steht darüber
    // (ausserhalb dieses Containers — Walter 19.07.2026).
    const permitsHtml = renderPermitListHtml(_permitHistoryCache || []);
    // Walter-Vorgabe 07.06.2026: Doku-Button neben „Bewilligungen" (analog
    // Bank-Tab) — öffnet die Dokumenten-Verwaltung gefiltert auf den
    // Permit-Dokument-Typ (linked_field_code='permit').
    const permitHasDoc = window._linkedDocCodes && window._linkedDocCodes.has('permit');
    const permitDocBtn = `<button title="${permitHasDoc ? 'Verknüpftes Bewilligungs-Dokument öffnen' : 'Noch kein Dokument vorhanden — klicken um hochzuladen'}"
                                  onclick="openLinkedDoc('permit')"
                                  style="background:${permitHasDoc ? '#dcfce7' : '#f8f7f4'};border:1px ${permitHasDoc ? 'solid #86efac' : 'dashed #d5d0c6'};border-radius:6px;padding:2px 7px;cursor:pointer;color:${permitHasDoc ? '#15803d' : '#b3ada1'};display:inline-flex;align-items:center;gap:3px;font-size:11px;font-weight:600;line-height:1;text-transform:none;letter-spacing:0">
                              <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                                  <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                                  <polyline points="14 2 14 8 20 8"/>
                                  <line x1="16" y1="13" x2="8" y2="13"/>
                                  <line x1="16" y1="17" x2="8" y2="17"/>
                                  <line x1="10" y1="9" x2="8" y2="9"/>
                              </svg>
                              <span>Doku${permitHasDoc ? ' ✓' : ''}</span>
                          </button>`;
    const permitsSection = `
    <div style="margin-bottom:22px">
        <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between;margin-top:0">
            <span style="display:inline-flex;align-items:center;gap:8px">
                Bewilligungen
                ${permitDocBtn}
                ${selectedEmployee?.zemisNumber ? `<span style="font-size:11px;font-weight:600;color:#6b7280;background:#ece9e2;border-radius:999px;padding:2px 10px;text-transform:none;letter-spacing:0" title="ZEMIS-Nummer (Ausländerregister) — von der Ausweis-Rückseite">ZEMIS ${esc(selectedEmployee.zemisNumber)}</span>` : ''}
            </span>
            ${isOpsRole() ? `
            <button class="btn-emp-add" onclick="openPermitHistoryModal(null)">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                Neue Bewilligung
            </button>` : ''}
        </div>
        ${permitsHtml}
    </div>
    <div class="emp-section-title" style="margin-top:6px">Quellensteuer-Einträge</div>`;
    // „+ Neuer Eintrag" sitzt jetzt im Header (empTabActionBar,
    // Walter-Vorgabe 01.06.2026) — Toolbar hier versteckt.
    const toolbar = `
    <div class="emp-familie-toolbar" style="display:none">
        <button class="btn-emp-add" onclick="openQstFromTab(null)">
            Neuer Eintrag
        </button>
    </div>`;

    if (!entries.length) {
        el.innerHTML = banner + permitsSection + toolbar + `
        <div class="emp-placeholder">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/></svg>
            <span>Keine Quellensteuer-Einträge erfasst</span>
        </div>`;
        return;
    }

    // Neueste zuerst
    const sorted = [...entries].sort((a, b) => (b.validFrom ?? '').localeCompare(a.validFrom ?? ''));
    let html = banner + permitsSection + toolbar;

    sorted.forEach(e => {
        const isCurrent = !e.validTo;
        const vonStr   = e.validFrom ? formatDate(e.validFrom) : '–';
        const bisStr   = e.validTo   ? formatDate(e.validTo)   : '…';
        const kanton   = e.steuerkanton   ?? '–';
        const code     = e.qstCode        ?? (e.tarifCode ? `${e.tarifCode}${e.anzahlKinder ?? 0}${e.kirchensteuer ? 'Y' : 'N'}` : '–');
        const kinder   = e.anzahlKinder   ?? 0;
        const kirche   = e.kirchensteuer  ? 'mit Kirchensteuer' : 'ohne Kirchensteuer';
        const pct      = e.prozentsatz    ? ` · ${Number(e.prozentsatz).toFixed(2)} %` : '';
        const gemeinde = e.qstGemeinde    ? ` · ${e.qstGemeinde}` : '';

        html += `
        <div class="emp-family-card" style="border-left:3px solid ${isCurrent ? '#1a1a1a' : '#e2e8f0'};margin-bottom:12px">
            <div class="emp-family-card-head">
                <div>
                    <div class="emp-family-name" style="display:flex;align-items:center;gap:8px">
                        ${isCurrent ? '<span style="background:#f6f3ee;color:#6b7280;font-size:10px;font-weight:700;padding:2px 7px;border-radius:10px;letter-spacing:.04em">AKTUELL</span>' : ''}
                        <span>${vonStr} bis ${bisStr}</span>
                    </div>
                    <div style="font-size:12px;color:#64748b;margin-top:3px">
                        Kanton <strong>${kanton}</strong> · Code <strong>${code}</strong> · ${kinder} Kinder · ${kirche}${pct}${gemeinde}
                    </div>
                </div>
                <!-- Tarifbestätigung als Beleg (Walter 21.08.2026) — auch bei
                     gesperrten Einträgen verknüpfbar (reiner Beleg, kein Lock). -->
                ${e.dokumentId
                    ? `<span style="display:inline-flex;gap:4px;align-items:center;margin-left:auto;margin-right:8px;flex-shrink:0">
                        <button class="fam-tile-doc fam-tile-doc-ok" onclick="qstOpenBefreiungsDok(${selectedEmployeeId}, ${e.dokumentId})" title="Tarifbestätigung öffnen">📄 Tarifbestätigung</button>
                        <button class="fam-tile-doc" onclick="openAusweisDokuModal(${selectedEmployeeId},'qst_tarif',{qstEntryId:${e.id}})" title="Anderes Dokument verknüpfen">↻</button>
                        <button class="fam-tile-doc fam-tile-doc-danger" onclick="qstTarifDokUnlink(${e.id})" title="Verknüpfung lösen">✕</button></span>`
                    : `<button class="fam-tile-doc" style="margin-left:auto;margin-right:8px;flex-shrink:0" onclick="openAusweisDokuModal(${selectedEmployeeId},'qst_tarif',{qstEntryId:${e.id}})" title="Tarifbestätigung der Steuerbehörde verknüpfen">📎 Tarifbestätigung</button>`}
                ${e.inLohnVerwendet
                    ? `<span title="Dieser QST-Eintrag gehört zu einer definitiv abgeschlossenen Lohnperiode (DTA erstellt) und ist nicht mehr editierbar. Für Änderungen: '+ Neuer Eintrag' oben." style="display:inline-flex;align-items:center;gap:4px;font-size:11px;font-weight:600;color:#b91c1c;background:#fee2e2;padding:4px 10px;border-radius:12px;cursor:help;flex-shrink:0">🔒 In Lohn verwendet</span>`
                    : `<div class="dok-menu-wrap" style="flex-shrink:0">
                        <button class="dok-menu-btn" onclick="qstToggleMenu(event, ${e.id})" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="qstMenu-${e.id}">
                            <button class="dok-menu-item" onclick="openQstFromTab(${e.id})">Bearbeiten</button>
                            <button class="dok-menu-item danger" onclick="deleteQstEntry(${e.id})">Löschen</button>
                        </div>
                       </div>`}
            </div>
        </div>`;
    });

    el.innerHTML = html;
}

// ══════════════════════════════════════════════════════════════════════
// Umzug / Kantonswechsel (Walter-Vorgabe 04.08.2026)
// ──────────────────────────────────────────────────────────────────────
// Amtliche Monatsregel (ESTV Kreisschreiben 45): der GESAMTE Umzugsmonat
// wird noch mit dem bisherigen Wohnkanton abgerechnet; der neue Kanton
// gilt ab dem 1. des Folgemonats. Beim alten Kanton zählt der letzte Tag
// des Umzugsmonats als Austritt, beim neuen der 1. des Folgemonats als
// Eintritt (Meldedaten für die Quellensteuermeldung an die Kantone).
// ══════════════════════════════════════════════════════════════════════

// Aktuelle (heute gültige) QST-Version — jüngstes validFrom, Tie-Break id
// (gleiche Auswahl wie Dashboard-Warnung qst_kanton_mismatch).
// Tarifbestätigung von der QST-Version lösen (Walter 21.08.2026) —
// das Dokument selbst bleibt im Doku-Tab erhalten.
async function qstTarifDokUnlink(entryId) {
    if (!(await liquidConfirm('Verknüpfung zur Tarifbestätigung lösen? Das Dokument selbst bleibt erhalten.'))) return;
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer/${entryId}/dokument`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ dokumentId: null })
        });
        if (!res.ok) { alert('Lösen fehlgeschlagen.'); return; }
        loadQuellensteuerTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

function _qstCurrentVersion(entries) {
    const d = new Date();
    const today = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    const valid = (entries || []).filter(e =>
        (e.validFrom || '') <= today && (!e.validTo || e.validTo >= today));
    if (!valid.length) return null;
    valid.sort((a, b) => (b.validFrom || '').localeCompare(a.validFrom || '') || ((b.id || 0) - (a.id || 0)));
    return valid[0];
}

let _qstUmzugAlt = null;   // Kanton der aktuellen QST-Version
let _qstUmzugNeu = null;   // Wohnkanton des MA (Ziel-Kanton)

function renderQstUmzugBanner(entries) {
    const emp = selectedEmployee;
    if (!emp || !emp.cantonCode) return '';
    const cur = _qstCurrentVersion(entries);
    if (!cur || !cur.steuerkanton) return '';
    const wohn = String(emp.cantonCode).trim().toUpperCase();
    const qstK = String(cur.steuerkanton).trim().toUpperCase();
    if (!wohn || !qstK || wohn === qstK) return '';
    // Kantonswechsel BEREITS erfasst (Walter 08.08.2026): existiert eine
    // künftige Version mit dem Wohnkanton (z.B. via Umzugsdatum-Bestätigung
    // im Wohnort-Dialog), grünen Info-Banner zeigen statt nochmal zu fordern.
    const dNow = new Date();
    const todayIso = `${dNow.getFullYear()}-${String(dNow.getMonth() + 1).padStart(2, '0')}-${String(dNow.getDate()).padStart(2, '0')}`;
    const erfasst = (entries || []).find(e =>
        String(e.steuerkanton || '').trim().toUpperCase() === wohn
        && (e.validFrom || '') > todayIso);
    if (erfasst) {
        const f = (iso) => `${String(iso).slice(8, 10)}.${String(iso).slice(5, 7)}.${String(iso).slice(0, 4)}`;
        return `
        <div style="background:#f0fdf4;border:1px solid #86efac;border-left:4px solid #16a34a;border-radius:8px;padding:10px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px">
            <span style="font-size:16px">✅</span>
            <div style="font-size:12.5px;color:#166534">
                <b>Kantonswechsel erfasst:</b> Umzugsmonat noch ${esc(qstK)}, <b>${esc(wohn)}</b> gilt ab <b>${f(erfasst.validFrom)}</b> (Kreisschreiben 45).
            </div>
        </div>`;
    }
    return `
    <div style="background:#fff7ed;border:1px solid #fdba74;border-left:4px solid #ea580c;border-radius:8px;padding:12px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px;flex-wrap:wrap">
        <span style="font-size:18px">🚚</span>
        <div style="flex:1;min-width:200px">
            <div style="font-weight:700;color:#9a3412;font-size:13px">Wohnkanton ${esc(wohn)} ≠ QST-Kanton ${esc(qstK)}</div>
            <div style="color:#c2410c;font-size:12px;margin-top:2px">
                Die aktuelle QST-Version rechnet mit Kanton ${esc(qstK)}, der MA wohnt aber in ${esc(wohn)}.
                Bei einem Umzug: der ganze Umzugsmonat bleibt ${esc(qstK)}, ${esc(wohn)} gilt ab dem 1. des Folgemonats (Kreisschreiben 45).
            </div>
        </div>
        <button onclick="openQstUmzugModal('${esc(qstK)}','${esc(wohn)}')" style="background:#ea580c;color:#fff;border:none;padding:8px 14px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;margin-left:auto;white-space:nowrap">
            🚚 Umzug erfassen
        </button>
    </div>`;
}

function openQstUmzugModal(altKanton, neuKanton) {
    if (!selectedEmployeeId) return;
    _qstUmzugAlt = altKanton;
    _qstUmzugNeu = neuKanton;

    const ktName = (code) => (typeof kantonNameFor === 'function' && kantonNameFor(code))
        ? `${code} — ${kantonNameFor(code)}` : code;

    const d = new Date();
    const todayIso = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

    document.getElementById('qstUmzugModal')?.remove();
    const wrap = document.createElement('div');
    wrap.id = 'qstUmzugModal';
    wrap.style.cssText = 'position:fixed;inset:0;z-index:10050;display:flex;align-items:center;justify-content:center;background:rgba(60,55,48,0.34);backdrop-filter:blur(6px)';
    wrap.addEventListener('click', (ev) => { if (ev.target === wrap) closeQstUmzugModal(); });
    // Liquid-Glass-Karte (Walter-Vorgabe 01.07.2026): Off-White, Glasrand,
    // Kohle-Primärpille, heller Glas-Sekundärbutton.
    wrap.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 18px 48px rgba(60,55,48,0.22);width:min(440px,92vw);padding:22px 24px" onclick="event.stopPropagation()">
        <div id="qstUmzugBody">
            <div style="font-size:16px;font-weight:700;color:#3f3f3f;display:flex;align-items:center;gap:8px">🚚 Umzug erfassen</div>
            <div style="font-size:12px;color:#8b8b8b;margin-top:3px">${esc(selectedEmployee ? `${selectedEmployee.firstName ?? ''} ${selectedEmployee.lastName ?? ''}`.trim() : '')}</div>

            <div style="display:flex;align-items:center;gap:10px;margin:16px 0 14px;background:rgba(255,255,255,0.48);border:1px solid rgba(255,255,255,0.62);border-radius:12px;padding:10px 14px">
                <div style="flex:1">
                    <div style="font-size:10.5px;font-weight:700;color:#8b8b8b;letter-spacing:.05em;text-transform:uppercase">Bisher (QST)</div>
                    <div style="font-size:13px;font-weight:600;color:#3f3f3f;margin-top:2px">${esc(ktName(altKanton))}</div>
                </div>
                <span style="font-size:16px;color:#6b7280">→</span>
                <div style="flex:1;text-align:right">
                    <div style="font-size:10.5px;font-weight:700;color:#8b8b8b;letter-spacing:.05em;text-transform:uppercase">Neu (Wohnkanton)</div>
                    <div style="font-size:13px;font-weight:600;color:#3f3f3f;margin-top:2px">${esc(ktName(neuKanton))}</div>
                </div>
            </div>

            <label style="display:block;font-size:12px;font-weight:600;color:#646464;margin-bottom:5px">Umzugsdatum *</label>
            <input type="date" id="qstUmzugDatum" value="${todayIso}" onchange="qstUmzugUpdateInfo()" oninput="qstUmzugUpdateInfo()"
                   style="width:100%;box-sizing:border-box;background:rgba(255,255,255,0.58);border:1px solid rgba(255,255,255,0.62);border-radius:10px;padding:9px 12px;font-size:13px;color:#3f3f3f;box-shadow:0 1px 3px rgba(60,55,48,0.14)">

            <div id="qstUmzugInfo" style="margin-top:12px;background:rgba(255,255,255,0.38);border:1px solid rgba(255,255,255,0.62);border-radius:10px;padding:10px 12px;font-size:12px;color:#646464;line-height:1.5"></div>

            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:18px">
                <button onclick="closeQstUmzugModal()" style="background:rgba(255,255,255,0.55);border:1px solid rgba(255,255,255,0.62);color:#646464;padding:9px 16px;border-radius:12px;font-size:13px;font-weight:600;cursor:pointer;box-shadow:0 1px 3px rgba(60,55,48,0.14)">Abbrechen</button>
                <button id="qstUmzugSaveBtn" onclick="saveQstUmzug()" style="background:#1a1a1a;border:none;color:#fff;padding:9px 18px;border-radius:12px;font-size:13px;font-weight:600;cursor:pointer;box-shadow:0 3px 8px rgba(60,55,48,0.22)">Umzug speichern</button>
            </div>
        </div>
    </div>`;
    document.body.appendChild(wrap);
    qstUmzugUpdateInfo();
    if (window.lohnEditLock && typeof window.lohnEditLock.loadState === 'function') {
        // Gesperrte Tage im Date-Picker gar nicht erst anbieten — der Server
        // prüft den STICHTAG (Folgemonat) nochmals autoritativ.
        const branchId = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
        if (branchId) {
            window.lohnEditLock.loadState(branchId).then(state => {
                const inp = document.getElementById('qstUmzugDatum');
                if (inp && state) window.lohnEditLock.applyToDateInput(inp, state);
            }).catch(() => {});
        }
    }
}

function closeQstUmzugModal() {
    document.getElementById('qstUmzugModal')?.remove();
}

// Info-Text der Monatsregel — live beim Datumswechsel aktualisiert.
function qstUmzugUpdateInfo() {
    const inp  = document.getElementById('qstUmzugDatum');
    const info = document.getElementById('qstUmzugInfo');
    if (!inp || !info) return;
    if (!inp.value) {
        info.innerHTML = 'Bitte ein Umzugsdatum wählen.';
        return;
    }
    const [y, m, d] = inp.value.split('-').map(Number);
    let austritt, eintritt, satz;
    if (d === 1) {
        // Spezialfall Umzug am 1. (Walter 08.08.2026): kein angebrochener
        // Monat — der neue Kanton gilt ab genau diesem Tag.
        const py = m === 1 ? y - 1 : y;
        const pm = m === 1 ? 12    : m - 1;
        const plast = new Date(py, pm, 0).getDate();
        austritt = `${String(plast).padStart(2, '0')}.${String(pm).padStart(2, '0')}.${py}`;
        eintritt = `01.${String(m).padStart(2, '0')}.${y}`;
        satz = `Umzug am Monatsersten — kein angebrochener Monat: <b>${esc(_qstUmzugNeu || '?')}</b> gilt ab <b>${eintritt}</b>, <b>${esc(_qstUmzugAlt || '?')}</b> bis ${austritt}.`;
    } else {
        const lastDay = new Date(y, m, 0).getDate();                  // letzter Tag des Umzugsmonats
        const fy      = m === 12 ? y + 1 : y;
        const fm      = m === 12 ? 1     : m + 1;
        austritt = `${String(lastDay).padStart(2, '0')}.${String(m).padStart(2, '0')}.${y}`;
        eintritt = `01.${String(fm).padStart(2, '0')}.${fy}`;
        satz = `Der ganze Umzugsmonat wird noch mit <b>${esc(_qstUmzugAlt || '?')}</b> abgerechnet (bis ${austritt});
        <b>${esc(_qstUmzugNeu || '?')}</b> gilt ab <b>${eintritt}</b> (Monatsregel Kreisschreiben 45).`;
    }
    info.innerHTML = `${satz}<br>
        Meldung an die Kantone: ${esc(_qstUmzugAlt || '?')} Austritt ${austritt}, ${esc(_qstUmzugNeu || '?')} Eintritt ${eintritt}.`;
}

async function saveQstUmzug() {
    const empId = selectedEmployeeId;
    const inp   = document.getElementById('qstUmzugDatum');
    if (!empId || !inp || !inp.value) { alert('Bitte ein Umzugsdatum angeben.'); return; }
    const btn = document.getElementById('qstUmzugSaveBtn');
    const resetBtn = () => { if (btn) { btn.disabled = false; btn.textContent = 'Umzug speichern'; } };
    if (btn) { btn.disabled = true; btn.textContent = 'Speichert…'; }
    try {
        const res = await fetch(`/api/employee-quellensteuer/${empId}/umzug`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ umzugsDatum: inp.value, neuerKanton: _qstUmzugNeu || null })
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) { resetBtn(); return; }
        const j = await res.json().catch(() => ({}));
        if (!res.ok) {
            alert(j.message || j.error || 'Fehler beim Erfassen des Umzugs.');
            resetBtn();
            return;
        }
        // Erfolg: Meldedaten BLEIBEN im Modal sichtbar (nicht nur Toast) —
        // Walter braucht sie für die Quellensteuermeldung an die Kantone.
        const fmtIso = (iso) => iso ? `${String(iso).slice(8, 10)}.${String(iso).slice(5, 7)}.${String(iso).slice(0, 4)}` : '–';
        const body = document.getElementById('qstUmzugBody');
        if (body) {
            body.innerHTML = `
            <div style="font-size:16px;font-weight:700;color:#166534;display:flex;align-items:center;gap:8px">✅ Umzug erfasst</div>
            <div style="margin-top:12px;background:#f0fdf4;border:1px solid #86efac;border-radius:10px;padding:12px 14px;font-size:12.5px;color:#166534;line-height:1.6">
                QST-Versionen aktualisiert: Umzugsmonat noch <b>${esc(j.alterKanton || _qstUmzugAlt || '?')}</b>,
                <b>${esc(j.neuerKanton || _qstUmzugNeu || '?')}</b> gilt ab <b>${fmtIso(j.neuerKantonEintritt)}</b>.<br>
                <b>Meldung an die Kantone:</b><br>
                • ${esc(j.alterKanton || _qstUmzugAlt || '?')} — Austritt <b>${fmtIso(j.alterKantonAustritt)}</b><br>
                • ${esc(j.neuerKanton || _qstUmzugNeu || '?')} — Eintritt <b>${fmtIso(j.neuerKantonEintritt)}</b>
            </div>
            <div style="display:flex;justify-content:flex-end;margin-top:16px">
                <button onclick="closeQstUmzugModal()" style="background:#1a1a1a;border:none;color:#fff;padding:9px 18px;border-radius:12px;font-size:13px;font-weight:600;cursor:pointer;box-shadow:0 3px 8px rgba(60,55,48,0.22)">Schliessen</button>
            </div>`;
        }
        if (typeof showToast === 'function') {
            showToast(`Meldung: ${j.alterKanton || ''} Austritt ${fmtIso(j.alterKantonAustritt)}, ${j.neuerKanton || ''} Eintritt ${fmtIso(j.neuerKantonEintritt)}`, 'success');
        }
        // QST-Tab neu laden (Versionen-Liste + Banner verschwinden lassen).
        if (typeof loadQuellensteuerTab === 'function') loadQuellensteuerTab(empId);
        if (typeof reloadLohnAfterQstChange === 'function') reloadLohnAfterQstChange(empId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        resetBtn();
    }
}

// Walter-Vorgabe 07.06.2026: QST-Eintrag löschen — gleiche UI-Logik wie
// bei der Bewilligung. Backend prüft im Edit-Lock-Service ob der Eintrag
// schon in einem Lohnlauf verwendet wurde.
// Walter-Vorgabe 07.06.2026: Ehegatten-Dokument hochladen — nutzt den
// normalen Doku-Upload-Dialog. Walter wählt im Modal selbst die Kategorie
// (z.B. „Persönliche Angaben → Ehegatten Dokumente"). Damit ist die UI
// unabhängig von der konkreten Dokument-Struktur — keine Hardcode-Codes,
// die brechen wenn Walter die Struktur ändert.
//
// openDokUploadModal() returnt sofort, wenn _dokState.empId noch nicht
// gesetzt ist. Wir laden daher zuerst die Doku-Daten des aktuellen MA
// (befüllt _dokState.empId + _dokState.taxonomy), dann das Modal öffnen.
async function fmOpenSpouseDocUpload() {
    if (!selectedEmployeeId) {
        alert('Bitte zuerst einen Mitarbeiter wählen.');
        return;
    }
    if (typeof loadEmpDokumente === 'function') {
        try { await loadEmpDokumente(selectedEmployeeId); } catch {}
    }
    // Walter-Vorgabe 07.06.2026 (Variante C+): Vorauswahl bevorzugt über
    // linked_field_code='spouse' (in der Dokument-Struktur als „Ehegatte
    // (Familie)" wählbar). Walter pflegt das einmal pro Dokument-Typ — z.B.
    // „Ausweis Ehegatte" mit Verknüpfung 'spouse'. Beim Klick wird dann der
    // passende TYP direkt vorausgewählt (nicht nur die Kategorie). Fallback:
    // Name-Match „Ehegatte/Ehepartner/Spouse" auf Kategorie-Ebene. Bei keinem
    // Treffer bleibt der Picker offen, Walter wählt selbst.
    try {
        const tax = (typeof _dokState !== 'undefined') ? _dokState.taxonomy : null;
        if (Array.isArray(tax)) {
            let foundTyp = null;
            let foundKat = null;
            for (const k of tax) {
                for (const t of (k.typen || [])) {
                    if (t.linkedFieldCode === 'spouse') {
                        foundTyp = t;
                        foundKat = k;
                        break;
                    }
                }
                if (foundTyp) break;
            }
            if (foundTyp) {
                _dokState.selectedKategorieId = foundKat.id;
                _dokState.selectedTypId = foundTyp.id;
            } else {
                // Fallback Name-Match auf Kategorie-Ebene
                const cat = tax.find(k => {
                    const n = (k.name || '').toLowerCase();
                    return n.includes('ehegatte') || n.includes('ehepartner') || n.includes('spouse');
                });
                if (cat) {
                    _dokState.selectedKategorieId = cat.id;
                    _dokState.selectedTypId = null;
                }
            }
        }
    } catch {}
    if (typeof openDokUploadModal === 'function') {
        openDokUploadModal();
    } else {
        alert('Doku-Upload-Modal nicht geladen. Bitte Seite neu laden.');
    }
}

async function deleteQstEntry(entryId) {
    if (!selectedEmployeeId || !entryId) return;
    if (!(await liquidConfirm('Diesen Quellensteuer-Eintrag wirklich löschen?\n\nDer aktuelle QST-Status wird automatisch neu ermittelt.'))) return;
    try {
        let res = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer/${entryId}`, {
            method: 'DELETE', headers: ah()
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            const j = await res.json().catch(() => ({}));
            // Admin-Force-Delete für abgeschlossene Historie-Versionen
            // (Walter 21.08.2026): Fehlerfassungen/Testdaten bereinigen.
            // Die Lohnlauf-Sperre bleibt auch für den Admin bestehen.
            if (j.error === 'QST_ABGESCHLOSSEN' && currentUser?.role === 'admin') {
                if (!(await liquidConfirm(
                    'Diese QST-Version ist abgeschlossene HISTORIE.\n\n'
                    + 'Als Admin kannst du sie trotzdem endgültig löschen — z.B. bei einer '
                    + 'Fehlerfassung. Danach fehlt dieser Zeitraum in der QST-Zeitachse.\n\n'
                    + 'Wirklich endgültig löschen?',
                    { title: 'Historie löschen (Admin)', yesLabel: 'Endgültig löschen', noLabel: 'Abbrechen' }))) return;
                res = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer/${entryId}?force=true`, {
                    method: 'DELETE', headers: ah()
                });
                if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
                if (!res.ok) {
                    const j2 = await res.json().catch(() => ({}));
                    alert(j2.message || j2.error || 'Fehler beim Löschen.');
                    return;
                }
            } else {
                alert(j.message || j.error || 'Fehler beim Löschen.');
                return;
            }
        }
        if (typeof loadQuellensteuerTab === 'function') {
            await loadQuellensteuerTab(selectedEmployeeId);
        }
        if (typeof selectEmployee === 'function') {
            await selectEmployee(selectedEmployeeId);
        }
        if (typeof reloadLohnAfterQstChange === 'function') {
            reloadLohnAfterQstChange(selectedEmployeeId);
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function openQstFromTab(entryId) {
    if (!selectedEmployeeId || !selectedEmployee) return;
    qstOpenedFromTab    = true;
    qstCurrentEmployeeId = selectedEmployeeId;
    qstCurrentEntryId    = entryId ?? null;
    // Damit openQstEntry() Steuerkanton/Gemeinde/BFS aus den MA-Stammdaten
    // automatisch vorschlagen kann, qstEmployeeData hier setzen.
    qstEmployeeData     = selectedEmployee;

    const emp = selectedEmployee;

    // Stammdaten im Modal setzen
    const setTxt = (id, val) => { const el = document.getElementById(id); if (el) el.textContent = val; };
    setTxt('qstModalSub',         `${emp.firstName ?? ''} ${emp.lastName ?? ''}`.trim());
    setTxt('qstPermitDisplay',
        emp.permitType
            ? `${emp.permitType.code}${emp.permitType.description ? ' — ' + emp.permitType.description : ''}`
            : (emp.permitTypeId ? 'Typ ' + emp.permitTypeId : '–'));
        setTxt('qstWohnortDisplay',   [emp.zipCode, stripCityCantonSuffix(emp.city)].filter(Boolean).join(' ') || '–');
    // Nationalität immer als Volltext (Walter-Vorgabe 14.05.2026) — der
    // Backend liefert nationalityName als Klartext (AppText → ISO-Tabelle).
    setTxt('qstNatDisplay',       emp.nationalityName ?? emp.nationality ?? '–');
    const _ktName = (typeof kantonNameFor === 'function') ? kantonNameFor(emp.cantonCode) : null;
    setTxt('qstKantonDisplay',    emp.cantonCode ? (_ktName ? `${emp.cantonCode} — ${_ktName}` : emp.cantonCode) : '–');
    setTxt('qstZivilstandDisplay', emp.maritalStatus ?? '–');

    // Verlauf laden
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer`, { headers: ah() });
        qstAllEntries = res.ok ? await res.json() : [];
    } catch { qstAllEntries = []; }
    if (typeof renderQstHistoryTabs === 'function') renderQstHistoryTabs();

    // Ehepartner-Info aus Familie-Tab anzeigen
    if (typeof loadQstPartnerInfo === 'function') {
        loadQstPartnerInfo(selectedEmployeeId);
    }

    // Kinder aus dem Familie-Tab laden (Walter-Bug 12.08.2026): dieser
    // Tab-Öffnungspfad hat den Kinder-Cache nie befüllt — deshalb stand
    // «Keine Kinder erfasst», obwohl Kinder vorhanden waren.
    if (typeof loadQstFamilyKinder === 'function') {
        await loadQstFamilyKinder(selectedEmployeeId);
    }

    // Formular befüllen
    if (entryId) {
        try {
            const r = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer/${entryId}`, { headers: ah() });
            if (r.ok) {
                const entry = await r.json();
                populateQstForm(entry);
                // Walter-Vorgabe 14.06.2026: bestehende Einträge NIE auto-
                // overwriten — Server-Vorschlag NUR als Banner zeigen.
                if (typeof qstFetchServerVorschlag === 'function') {
                    await qstFetchServerVorschlag(entry?.validFrom);
                    if (typeof qstRenderVorschlagBanner === 'function') qstRenderVorschlagBanner();
                    if (typeof qstUpdateAutoKinderHint === 'function') qstUpdateAutoKinderHint();
                }
            }
        } catch {}
    } else {
        // Neuer Eintrag → openQstEntry(null) übernimmt:
        // - Felder leeren
        // - Gültig-ab = letzter Eintrag.gültigBis + 1 Tag (oder heute)
        // - Auto-Fill Steuerkanton, Gemeinde, BFS-Nr aus Wohnadresse
        if (typeof openQstEntry === 'function') {
            await openQstEntry(null);
        } else {
            populateQstForm(null);
            const vf = document.getElementById('qstValidFrom');
            if (vf) vf.value = new Date().toISOString().slice(0, 10);
        }
    }

    document.getElementById('qstSaveResult').textContent = '';
    document.getElementById('qstModal').style.display = 'flex';
}

// ── Familie Tab laden ──────────────────────────
async function loadFamilieTab(employeeId) {
    const el = document.getElementById('familieContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen...</span></div>';
    try {
        const res = await fetch(`/api/employees/${employeeId}/family`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>'; return; }
        const members = await res.json();
        // Walter 18.05.2026: Zulagen pro Kind vorab parallel laden für die
        // Inline-Darstellung (analog Bank-Liste).
        const kinder = members.filter(m => m.memberType === 'Kind');
        const allowanceMap = {};
        if (kinder.length) {
            await Promise.all(kinder.map(async k => {
                try {
                    const r = await fetch(`/api/family-members/${k.id}/allowances`, { headers: ah() });
                    allowanceMap[k.id] = r.ok ? await r.json() : [];
                } catch {
                    allowanceMap[k.id] = [];
                }
            }));
        }
        // Walter 11.06.2026: bei Frauen ALLE Schwangerschaften mitladen
        // (inkl. Fristen pro Eintrag) — das Mutterschafts-Modul lebt jetzt
        // komplett im Familie-Tab. Aktive (laufende) Schwangerschaft als
        // erste Card, ältere darunter, Erfass-Button unten.
        let pregnancyDetails = [];
        const emp = selectedEmployee;
        if (emp && IstWeiblich(emp.gender)) {
            try {
                const r = await fetch(`/api/pregnancies?employeeId=${employeeId}`, { headers: ah() });
                if (r.ok) {
                    const list = await r.json();
                    pregnancyDetails = await Promise.all(list.map(p =>
                        fetch(`/api/pregnancies/${p.id}`, { headers: ah() }).then(rr => rr.ok ? rr.json() : null)));
                    pregnancyDetails = pregnancyDetails.filter(Boolean);
                }
            } catch {}
        }
        // Walter-Vorgabe 13.06.2026: QST-Pflicht-Status mitladen, damit der
        // Familie-Tab beim Ehepartner einen roten Warnbanner zeigen kann,
        // wenn die QST-Befreiung über den Spouse läuft und das Beleg-Doku
        // fehlt (analog zum Banner im Bewilligung/QST-Tab).
        let pflicht = null;
        try {
            const pr = await fetch(`/api/employees/${employeeId}/qst-pflicht`, { headers: ah() });
            if (pr.ok) pflicht = await pr.json();
        } catch {}
        window._famPflichtCache = pflicht;
        renderFamilieTab(el, members, employeeId, allowanceMap, pregnancyDetails, pflicht);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler</span></div>';
    }
}

// Mutterschafts-Block des Familie-Tabs (Walter 11.06.2026; als Helper
// extrahiert 16.07.2026, weil er bei MA OHNE Familienmitglieder durch den
// early-return verloren ging — Walter-Bug: «hier konnte ich frueher die
// mutterschaft eintragen»). Nur bei Frauen.
function _familieMutterschaftHtml(employeeId, pregnancyDetails) {
    const empF = selectedEmployee;
    if (!empF || !IstWeiblich(empF.gender)) return '';
    const list = pregnancyDetails || [];
    // Abgeschlossene Schwangerschaften ausblenden (Walter 12.08.2026):
    // sichtbar ist nur die laufende (bis 16 Wochen nach Geburt/ET, gleiches
    // Fenster wie Badge/Kündigungsschutz). Ältere sind Historie und stehen
    // eingeklappt hinter «frühere anzeigen».
    const _mtsHeute = new Date().toISOString().slice(0, 10);
    const _mtsIsCurrent = d => {
        const p = d?.pregnancy || {};
        const basis = p.geburtsdatum || p.errechneterTermin;
        if (!basis) return true; // ohne Datum sicherheitshalber zeigen
        const ende = new Date(basis); ende.setDate(ende.getDate() + 16 * 7);
        return ende.toISOString().slice(0, 10) >= _mtsHeute;
    };
    const current = list.filter(_mtsIsCurrent);
    const older   = list.filter(d => !_mtsIsCurrent(d))
        .sort((a, b) => String(b?.pregnancy?.errechneterTermin || '').localeCompare(String(a?.pregnancy?.errechneterTermin || '')));
    // Button nur wenn keine LAUFENDE offene Schwangerschaft (ohne Geburt) —
    // ältere/abgeschlossene blockieren «+ Schwangerschaft erfassen» nicht
    // mehr (Walter 27.07.2026, präzisiert 12.08.2026).
    const hasOpen = current.some(d => d?.pregnancy && !d.pregnancy.geburtsdatum);
    const addBtn = hasOpen ? '' : `
            <button type="button" class="btn-emp-add" style="padding:6px 14px;font-size:12px;margin-left:auto" onclick="mtsOpenNew(${employeeId})">+ Schwangerschaft erfassen</button>`;
    const olderHtml = older.length ? `
            <div style="margin-top:10px">
                <a href="#" id="mtsOldToggle" style="font-size:12px;color:#8b8b8b;text-decoration:underline"
                   onclick="document.getElementById('mtsOldWrap').style.display='block';this.style.display='none';return false">
                   ${older.length} frühere Schwangerschaft${older.length > 1 ? 'en' : ''} anzeigen</a>
                <div id="mtsOldWrap" style="display:none;opacity:0.75">${older.map(d => renderPregnancyCard(d)).join('')}</div>
            </div>` : '';
    return `
        <div class="emp-section-title" style="margin-top:24px;display:flex;align-items:center;justify-content:space-between">
            <span>Mutterschaft</span>
            ${addBtn}
        </div>
        <div id="mutterschaftContent">
            ${current.length
                ? current.map(d => renderPregnancyCard(d)).join('')
                : `<div class="emp-placeholder" style="padding:24px"><span>${list.length ? 'Keine laufende Schwangerschaft.' : 'Keine Schwangerschaft erfasst.'}</span></div>`
            }
            ${olderHtml}
        </div>`;
}

function renderFamilieTab(el, members, employeeId, allowanceMap = {}, pregnancyDetails = [], pflicht = null) {
    // Cache für Detail-Popup-Lookup
    window._familyMembersCache = members;

    // Walter-Vorgabe 27.07.2026: „+ Familienmitglied" in der Leerzeile /
    // im Listen-Kopf — NICHT oben rechts im Header (empTabActionBar).
    const isExcluded = !!selectedEmployee?.isPayrollExcluded;
    const addBtn = isExcluded ? '' : `
        <button type="button" class="btn-emp-add" onclick="openFamilyModal(null)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            ${_t('famTab.add','Familienmitglied')}
        </button>`;

    if (!members.length) {
        el.innerHTML = `
        <div class="emp-familie-empty-row">
            <div class="emp-familie-empty-msg">
                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
                <span>${_t('famTab.empty','Keine Familienmitglieder erfasst')}</span>
            </div>
            ${addBtn}
        </div>` + _familieMutterschaftHtml(employeeId, pregnancyDetails);
        return;
    }

    const listHead = `
    <div class="emp-section-title" style="margin-top:0;display:flex;align-items:center;justify-content:space-between;gap:12px">
        <span>${_t('famTab.listTitle','Familienmitglieder')}</span>
        ${addBtn}
    </div>`;

    // Gruppieren nach Typ
    const groups = {};
    members.forEach(m => {
        if (!groups[m.memberType]) groups[m.memberType] = [];
        groups[m.memberType].push(m);
    });

    const typeOrder = ['Ehepartner', 'Kind', 'Mutter', 'Vater', 'Sonstige'];
    // Walter-Vorgabe 13.06.2026: roter Warnbanner ganz oben, wenn die QST-
    // Befreiung über den Ehepartner läuft und das Beleg-Doku noch nicht am
    // Family-Member verknüpft ist. Klick öffnet das gleiche Auswahl+Upload-
    // Modal wie für ID/Pass / C-Ausweis beim MA selbst.
    let spouseTopBanner = '';
    if (pflicht && pflicht.spouseDokumentFehlt && pflicht.spouseFamilyMemberId
        && (pflicht.befreiungsGrund === 'Ehepartner-CH' || pflicht.befreiungsGrund === 'Ehepartner-C')) {
        const grundText = pflicht.befreiungsGrund === 'Ehepartner-CH'
            ? 'Der Ehepartner ist Schweizer Staatsbürger — bitte Pass oder Identitätskarte des Ehepartners hier verknüpfen.'
            : 'Der Ehepartner hat einen C-Ausweis — bitte das Bewilligungs-Dokument des Ehepartners hier verknüpfen.';
        spouseTopBanner = `
        <div style="background:#fef2f2;border:1px solid #fca5a5;border-left:4px solid #dc2626;border-radius:8px;padding:12px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <span style="font-size:18px">⚠️</span>
            <div style="flex:1;min-width:200px">
                <div style="font-weight:700;color:#991b1b;font-size:13px">Ausweis Ehepartner fehlt für die QST-Befreiung</div>
                <div style="color:#b91c1c;font-size:12px;margin-top:2px">${grundText}</div>
            </div>
            <button onclick="openAusweisDokuModal(${employeeId},'spouse',{spouseFamilyMemberId:${pflicht.spouseFamilyMemberId}})" style="background:#dc2626;color:#fff;border:none;padding:7px 14px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;margin-left:auto;white-space:nowrap">
                📎 Dokument verknüpfen
            </button>
        </div>`;
    }

    let html = spouseTopBanner + listHead;

    // Display-Label für Mitglieds-Typen (mit Plural für „Kinder").
    const typeLabel = (type, count) => {
        const isEn = window.i18n && window.i18n.getLang && i18n.getLang() === 'en';
        if (type === 'Kind' && count > 1) return isEn ? 'Children' : 'Kinder';
        return _t('fam.value.type.' + type, type);
    };
    const yearsLabel = window.i18n && window.i18n.getLang && i18n.getLang() === 'en' ? 'years' : 'Jahre';

    typeOrder.forEach(type => {
        if (!groups[type]) return;
        const sectionTitle = typeLabel(type, groups[type].length);
        html += `<div class="emp-section-title" style="margin-top:14px">${sectionTitle}</div>`;
        // Walter 18.07.2026: kompakte Kachel-Raster statt vollbreiter
        // Listen + leerer Zulagen-Boxen (war unübersichtlich bei mehreren Kindern).
        html += `<div class="fam-tile-grid">`;
        groups[type].forEach(m => {
            const name = ((m.firstName ?? '') + ' ' + (m.lastName ?? '')).trim() || '–';
            const dob  = m.dateOfBirth ? formatDate(m.dateOfBirth) : '';
            const age  = m.dateOfBirth ? calcAge(m.dateOfBirth) : null;
            const phoneMeta = m.phone ? esc(m.phone) : '';
            const metaParts = [
                dob ? `${dob}${age !== null ? ' · ' + age + ' ' + yearsLabel : ''}` : '',
                phoneMeta
            ].filter(Boolean);
            const meta = metaParts.join(' · ');

            // Walter-Vorgabe 07.06.2026: Beim EHEPARTNER (nicht bei Kindern!)
            // die Bewilligung + Ablaufdatum als Badge anzeigen, plus einen
            // Doku-Button für die Ausweis-Kopie.
            let spousePermitBadge = '';
            let spouseDocBtn = '';
            if (type === 'Ehepartner') {
                const pCode = m.permitType?.code || null;
                const pDesc = m.permitType?.description || null;
                const pExp  = m.permitExpiryDate ? formatDate(m.permitExpiryDate) : null;
                const natCode = (m.nationalityCode || '').toUpperCase();
                const isCh = natCode === 'CH';
                if (pCode || pDesc || pExp) {
                    let nameLabel;
                    if (pCode && pDesc)      nameLabel = `${esc(pCode)} — ${esc(pDesc)}`;
                    else if (pCode)          nameLabel = esc(pCode);
                    else if (pDesc)          nameLabel = esc(pDesc);
                    else                     nameLabel = '';
                    const label = nameLabel
                        ? (pExp ? `${nameLabel} bis ${pExp}` : nameLabel)
                        : `bis ${pExp}`;
                    spousePermitBadge = `<span class="fam-tile-badge fam-tile-badge-permit" title="Bewilligung Ehepartner">📋 ${label}</span>`;
                } else if (isCh) {
                    spousePermitBadge = `<span class="fam-tile-badge fam-tile-badge-ch" title="CH-Bürger — keine Bewilligung nötig">🇨🇭 CH-Bürger</span>`;
                } else {
                    spousePermitBadge = `<span class="fam-tile-badge fam-tile-badge-warn" title="Keine Bewilligung erfasst">⚠ Keine Bewilligung</span>`;
                }
                // Walter-Vorgabe 20.08.2026: Erwerbstätig-Badge am Ehepartner —
                // rot wenn die Frage offen ist (blockt bei QST-pflichtigen
                // verheirateten MA den Lohnlauf).
                if (m.erwerbstaetig === true) {
                    const agTxt = [m.arbeitgeberName, m.arbeitgeberOrt].filter(Boolean).join(', ');
                    spousePermitBadge += `<span class="fam-tile-badge" style="background:#dcfce7;color:#166534" title="Erwerbstätig${agTxt ? ' bei ' + esc(agTxt) : ''}">💼 erwerbstätig${agTxt ? ' · ' + esc(agTxt) : ''}</span>`;
                    if (!m.arbeitgeberName)
                        spousePermitBadge += `<span class="fam-tile-badge fam-tile-badge-warn" title="Arbeitgeber fehlt — blockt den Lohnlauf">⚠ Arbeitgeber fehlt</span>`;
                } else if (m.erwerbstaetig === false) {
                    spousePermitBadge += `<span class="fam-tile-badge" title="Nicht erwerbstätig">nicht erwerbstätig</span>`;
                } else {
                    spousePermitBadge += `<span class="fam-tile-badge fam-tile-badge-warn" title="Erwerbstätig-Frage offen — blockt bei QST-pflichtigen verheirateten MA den Lohnlauf">⚠ Erwerbstätig?</span>`;
                }
                const hasSpouseDok = !!m.dokumentId;
                if (hasSpouseDok) {
                    spouseDocBtn = `<button class="fam-tile-doc fam-tile-doc-ok" onclick="event.stopPropagation();qstOpenBefreiungsDok(${employeeId}, ${m.dokumentId})" title="Verknüpftes Beleg-Dokument öffnen">📄 Doku</button>
                        <button class="fam-tile-doc" onclick="event.stopPropagation();openAusweisDokuModal(${employeeId},'spouse',{spouseFamilyMemberId:${m.id}})" title="Anderes Dokument verknüpfen">↻</button>
                        <button class="fam-tile-doc fam-tile-doc-danger" onclick="event.stopPropagation();spouseDokuUnlink(${employeeId}, ${m.id})" title="Verknüpfung lösen">✕</button>`;
                } else {
                    spouseDocBtn = `<button class="fam-tile-doc" onclick="event.stopPropagation();openAusweisDokuModal(${employeeId},'spouse',{spouseFamilyMemberId:${m.id}})" title="Beleg-Dokument verknüpfen">📎 Doku</button>`;
                }
            }

            // Walter-Vorgabe 20.08.2026: Kind in Erstausbildung — Kinderziffer
            // läuft über den 18. Geburtstag hinaus (KS 45; Beleg hinterlegen).
            if (type === 'Kind' && m.inErstausbildung) {
                spousePermitBadge += `<span class="fam-tile-badge" style="background:#dbeafe;color:#1d4ed8" title="In Erstausbildung — QST-Kinderziffer läuft über 18 hinaus (Lehrvertrag/Immatrikulation als Beleg)">🎓 Erstausbildung</span>`;
            }

            let addrBadge = '';
            if (m.alternativeAddress) {
                const a = m.alternativeAddress;
                const ort = [a.zipCode, stripCityCantonSuffix(a.city)].filter(Boolean).join(' ');
                const land = a.country && a.country.toLowerCase() !== 'schweiz' ? a.country : '';
                const tip  = [a.description, [a.street, a.street2].filter(Boolean).join(' / '), ort, land].filter(Boolean).join(' · ');
                const short = ort || a.description || a.country || 'andere Adresse';
                addrBadge = `<span class="fam-tile-badge fam-tile-badge-addr" title="${esc(tip)}">📍 ${esc(short)}</span>`;
            }

            const memberJson = JSON.stringify(m).replace(/"/g, '&quot;');

            // Kinder: Zulagen kompakt — KEINE leere gestrichelte Box mehr.
            // Ohne Zulagen nur „+ Zulage"; mit Zulagen eine Chip-Zeile + Detail.
            let kindAllowancesBlock = '';
            if (type === 'Kind') {
                const allowances = allowanceMap[m.id] || [];
                if (allowances.length === 0) {
                    kindAllowancesBlock = `
                    <div class="fam-tile-foot" onclick="event.stopPropagation()">
                        <button type="button" class="fam-tile-zulage-btn" onclick="openAllowanceFromCard(${m.id}, null)">+ Zulage</button>
                    </div>`;
                } else {
                    const chips = allowances.map(a => {
                        const artShort = a.allowanceType || 'Zulage';
                        const bisShort = a.validTo ? formatDate(a.validTo) : 'offen';
                        const locked = a.inLohnVerwendet === true;
                        const hasDok = !!a.dokumentId;
                        const aJson = JSON.stringify(a).replace(/"/g, '&quot;');
                        return `<button type="button" class="fam-tile-chip${locked ? ' is-locked' : ''}"
                            title="${esc(artShort)} · CHF ${Number(a.monthlyAmount).toFixed(2)} · bis ${bisShort}${hasDok ? ' · mit Entscheid-Doku' : ''}${locked ? ' · in Lohn verwendet' : ''}"
                            onclick="event.stopPropagation();openAllowanceFromCard(${m.id}, ${aJson})">
                            ${locked ? '🔒 ' : ''}${hasDok ? '📄 ' : ''}${esc(artShort)} · ${Number(a.monthlyAmount).toFixed(0)}
                        </button>`;
                    }).join('');
                    kindAllowancesBlock = `
                    <div class="fam-tile-foot" onclick="event.stopPropagation()">
                        <div class="fam-tile-chips">${chips}</div>
                        <button type="button" class="fam-tile-zulage-btn" onclick="openAllowanceFromCard(${m.id}, null)">+</button>
                    </div>`;
                }
            }

            const tileWide = type === 'Ehepartner' ? ' fam-tile-wide' : '';
            html += `
            <div class="fam-tile${tileWide}" onclick="openFamilyModal(${memberJson})">
                <div class="fam-tile-top">
                    <div class="fam-tile-name">${esc(name)}</div>
                    <div class="dok-menu-wrap" onclick="event.stopPropagation()">
                        <button class="dok-menu-btn" onclick="famToggleMenu(event, ${m.id})" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="famMenu-${m.id}">
                            <button class="dok-menu-item" onclick="openFamilyModal(${memberJson})">Bearbeiten</button>
                            <button class="dok-menu-item danger" onclick="deleteFamilyMember(${m.id})">Löschen</button>
                        </div>
                    </div>
                </div>
                <div class="fam-tile-meta">${meta || '–'}</div>
                ${(spousePermitBadge || spouseDocBtn || addrBadge)
                    ? `<div class="fam-tile-badges" onclick="event.stopPropagation()">${spousePermitBadge}${spouseDocBtn}${addrBadge}</div>`
                    : ''}
                ${kindAllowancesBlock}
            </div>`;
        });
        html += `</div>`;
    });

    // Walter 11.06.2026: Mutterschafts-Modul komplett im Familie-Tab — nur
    // bei Frauen (Helper _familieMutterschaftHtml, siehe oben).
    html += _familieMutterschaftHtml(employeeId, pregnancyDetails);

    el.innerHTML = html;
}

function showFamilyDetailPopup(memberId) {
    const m = (window._familyMembersCache ?? []).find(x => x.id === memberId);
    if (!m) return;
    const name = ((m.firstName ?? '') + ' ' + (m.lastName ?? '')).trim() || '–';
    const dob  = m.dateOfBirth ? formatDate(m.dateOfBirth) : '–';
    const age  = m.dateOfBirth ? calcAge(m.dateOfBirth) : null;
    const yearsLbl = window.i18n && i18n.getLang && i18n.getLang() === 'en' ? 'years' : 'Jahre';
    const memberTypeLbl = _t('fam.value.type.' + m.memberType, m.memberType);
    const subtitle = `${memberTypeLbl}${age !== null ? ' · ' + dob + ' (' + age + ' ' + yearsLbl + ')' : ''}`;

    const row = (label, value) => `
        <div style="display:flex;justify-content:space-between;padding:6px 0;border-bottom:1px solid #f1f5f9">
            <span style="color:#64748b;font-size:12px">${label}</span>
            <span style="color:#1e293b;font-weight:500">${value ?? '–'}</span>
        </div>`;

    const html = `
        <div style="position:fixed;inset:0;background:rgba(0,0,0,0.4);z-index:2000;display:flex;align-items:center;justify-content:center" onclick="if(event.target===this)closeFamilyDetailPopup()">
            <div style="background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);border-radius:14px;width:480px;max-width:92vw;box-shadow:0 12px 48px rgba(0,0,0,0.2);overflow:hidden">
                <div style="padding:18px 22px;border-bottom:1px solid #e2e8f0;display:flex;align-items:flex-start;justify-content:space-between;gap:8px">
                    <div>
                        <div style="font-size:16px;font-weight:700;color:#0f172a">${esc(name)}</div>
                        <div style="font-size:12px;color:#64748b;margin-top:2px">${esc(subtitle)}</div>
                    </div>
                    <button onclick="closeFamilyDetailPopup()" style="background:none;border:none;cursor:pointer;font-size:18px;color:#94a3b8;padding:4px 8px">✕</button>
                </div>
                <div style="padding:14px 22px">
                    ${row(_t('fam.field.ahv','AHV-Nummer'),     m.socialSecurityNumber || '–')}
                    ${row(_t('fam.field.phone','Telefon'),      m.phone || '–')}
                    ${row(_t('fam.field.livesInCh','In der Schweiz lebend'), m.livesInSwitzerland ? (i18n.getLang && i18n.getLang() === 'en' ? 'Yes' : 'Ja') : (i18n.getLang && i18n.getLang() === 'en' ? 'No' : 'Nein'))}
                    ${row(_t('fam.field.qstFrom','QST ab'),     m.qstDeductibleFrom  ? formatDate(m.qstDeductibleFrom)  : '–')}
                    ${row(_t('fam.field.qstUntil','QST bis'),   m.qstDeductibleUntil ? formatDate(m.qstDeductibleUntil) : '–')}
                </div>
                <div style="padding:14px 22px;border-top:1px solid #e2e8f0;display:flex;justify-content:flex-end;gap:8px">
                    <button onclick="closeFamilyDetailPopup();openFamilyModal(${JSON.stringify(m).replace(/"/g, '&quot;')})" style="background:#1a1a1a;color:white;border:none;border-radius:8px;padding:8px 16px;font-size:13px;cursor:pointer;font-weight:600">✎ ${_t('docs.btn.edit','Bearbeiten')}</button>
                    <button onclick="closeFamilyDetailPopup()" style="background:white;border:1px solid #e2e8f0;border-radius:8px;padding:8px 16px;font-size:13px;cursor:pointer">${_t('common.close','Schliessen')}</button>
                </div>
            </div>
        </div>`;

    let pop = document.getElementById('familyDetailPopup');
    if (!pop) {
        pop = document.createElement('div');
        pop.id = 'familyDetailPopup';
        document.body.appendChild(pop);
    }
    pop.innerHTML = html;
}

function closeFamilyDetailPopup() {
    const pop = document.getElementById('familyDetailPopup');
    if (pop) pop.innerHTML = '';
}

function calcAge(dateStr) {
    const dob = new Date(dateStr);
    if (isNaN(dob)) return null;
    const today = new Date();
    let age = today.getFullYear() - dob.getFullYear();
    if (today < new Date(today.getFullYear(), dob.getMonth(), dob.getDate())) age--;
    return age;
}

// Live-Anzeige neben „Geburtsdatum" im Familienangehöriger-Modal — Alter
// wird mit aktuellem Datum berechnet (Walter-Vorgabe).
function updateFmAgeDisplay() {
    const dob = document.getElementById('fmDateOfBirth')?.value;
    const dispEl = document.getElementById('fmAgeDisplay');
    if (!dispEl) return;
    const age = dob ? calcAge(dob) : null;
    dispEl.textContent = age != null ? `(${age} J.)` : '';
}

// Walter-Vorgabe 14.06.2026: Wenn der User im Familien-Modal das Geburts-
// datum eintippt, automatisch:
//   • QST abzugsberechtigt AB  = Geburtsdatum
//       IMMER überschreiben — AB ist immer = Geburtsdatum (1:1-Beziehung),
//       alte fehlerhafte Werte (z.B. „01.01.0002" von einem Tipp-Glitsch)
//       werden so automatisch korrigiert.
//   • QST abzugsberechtigt BIS = Geburtsdatum + 18 Jahre (volljährig)
//       Nur überschreiben wenn das Feld leer war ODER mit dem alten Auto-
//       Wert (irgend-dob + 18) übereinstimmt — manuelle Verlängerungen
//       wegen Ausbildung (z.B. bis 25 Jahre) bleiben erhalten.
function fmAutoQstFromDob() {
    const dob = document.getElementById('fmDateOfBirth')?.value;
    if (!dob || !/^\d{4}-\d{2}-\d{2}$/.test(dob)) return;
    const fromEl  = document.getElementById('fmQstFrom');
    const untilEl = document.getElementById('fmQstUntil');

    // AB → immer hartes Sync mit Geburtsdatum
    if (fromEl) fromEl.value = dob;

    // BIS → Geburtsdatum + 18 Jahre, nur wenn leer oder vom Default-Pattern
    // (also nicht manuell verlängert)
    if (untilEl) {
        const [y, m, d] = dob.split('-');
        const y18 = String(parseInt(y, 10) + 18);
        const newBis = `${y18}-${m}-${d}`;
        const curBis = untilEl.value || '';
        // Heuristik „war noch Auto": bis = irgendein Jahr + dieselbe MM-DD wie
        // das (möglicherweise alte) Geburtsdatum + 18. Ohne alten dob: wir
        // überschreiben nur wenn leer ODER wenn der Bis-Tag exakt mit dem
        // Geburts-Tag übereinstimmt (typisches 18.-Geburtstag-Muster).
        const dobMmDd = `${m}-${d}`;
        const bisMmDd = curBis.length >= 10 ? curBis.slice(5, 10) : '';
        const looksLikeAuto = !curBis || bisMmDd === dobMmDd;
        if (looksLikeAuto) untilEl.value = newBis;
    }
}

// Walter-Vorgabe 28.05.2026: Wenn der User im Familien-Modal den Typ auf
// Kind oder Ehepartner ändert UND das Nachname-Feld noch leer ist, mit dem
// Nachnamen des MA vorbefüllen. Bei Mutter/Vater/Sonstige NICHT vorbefüllen
// (die haben oft eigene Nachnamen). Wir überschreiben NIE einen schon vom
// User eingetippten Wert — Vorbefüllen passiert nur wenn das Feld leer ist.
function fmTypeChanged() {
    const typeEl = document.getElementById('fmMemberType');
    const lastEl = document.getElementById('fmLastName');
    if (!typeEl || !lastEl) return;
    const type = typeEl.value;
    const maLast = (selectedEmployee?.lastName || '').trim();
    if ((type === 'Kind' || type === 'Ehepartner') && maLast && !lastEl.value.trim()) {
        lastEl.value = maLast;
    }
    fmQstBlocksVisibility(type);
}

// Walter-Vorgabe 20.08.2026: typ-abhängige QST-Blöcke im Familien-Modal —
// Erwerbstätigkeit nur beim Ehepartner, Erstausbildung nur beim Kind.
function fmQstBlocksVisibility(type) {
    const erwerbSec = document.getElementById('fmErwerbSection');
    if (erwerbSec) erwerbSec.style.display = (type === 'Ehepartner') ? '' : 'none';
    // Walter-Vorgabe 20.08.2026: die GANZE QST-Sektion (Abzug ab/bis +
    // Erstausbildung) gibt es nur bei Kindern — beim Ehepartner & Co. weg.
    const qstSec = document.getElementById('fmQstSection');
    if (qstSec) qstSec.style.display = (type === 'Kind') ? '' : 'none';
}

// Segment-Pille Erwerbstätig lesen/schreiben ('' = Frage offen).
function fmSetErwerb(val) {
    const want = val === true ? 'ja' : val === false ? 'nein' : '';
    document.querySelectorAll('input[name="fmErwerb"]').forEach(r => { r.checked = (r.value === want); });
    fmErwerbChanged();
}
function fmGetErwerb() {
    const r = document.querySelector('input[name="fmErwerb"]:checked');
    return r?.value === 'ja' ? true : r?.value === 'nein' ? false : null;
}
function fmErwerbChanged() {
    // Arbeitgeber-Felder nur bei «Ja» aktiv — bei Nein/offen ausgegraut.
    const aktiv = fmGetErwerb() === true;
    ['fmArbeitgeberName', 'fmArbeitgeberStrasse', 'fmArbeitgeberPlz',
     'fmArbeitgeberOrt', 'fmArbeitgeberKanton', 'fmStellenantritt'].forEach(id => {
        const el = document.getElementById(id);
        if (!el) return;
        el.disabled = !aktiv;
        el.style.opacity = aktiv ? '' : '0.5';
    });
}

// Analog für das MA-Edit-Modal — Alter neben dem Geburtsdatum-Input.
function updateEfDobAgeDisplay() {
    const dob = document.getElementById('ef-dob')?.value;
    const dispEl = document.getElementById('ef-dob-age');
    if (!dispEl) return;
    const age = dob ? calcAge(dob) : null;
    dispEl.textContent = age != null ? `(${age} J.)` : '';
}

function formatMonthYear(dateStr) {
    if (!dateStr) return '–';
    const d = new Date(dateStr);
    if (isNaN(d)) return dateStr;
    return d.toLocaleDateString('de-CH', { month: '2-digit', year: 'numeric' });
}

// ── AHV-Nummer-Validierung ──────────────────────────────────────────────
// Schweizer Sozialversicherungsnummer (NNSS) im EAN-13-Format:
//   - 13 Ziffern, beginnt mit 756 (CH)
//   - Letzte Ziffer = Prüfziffer aus den ersten 12
//   - EAN-13: Position 1,3,5,…×1, Position 2,4,6,…×3, Summe mod 10
function validateAhvNummer(input) {
    if (!input || !input.trim()) return { valid: null };  // leer = kein Fehler
    const digits = input.replace(/\D/g, '');
    if (digits.length !== 13) {
        return { valid: false, error: `Muss 13 Ziffern haben (aktuell ${digits.length})` };
    }
    if (!digits.startsWith('756')) {
        return { valid: false, error: 'Muss mit 756 beginnen (CH-Code)' };
    }
    // EAN-13 Prüfziffer auf den ersten 12 Ziffern
    let sum = 0;
    for (let i = 0; i < 12; i++) {
        const d = parseInt(digits[i], 10);
        sum += (i % 2 === 0) ? d : d * 3;
    }
    const expected = (10 - sum % 10) % 10;
    const actual   = parseInt(digits[12], 10);
    if (expected !== actual) {
        return {
            valid: false,
            error: `Prüfziffer falsch (erwartet ${expected}, eingegeben ${actual})`
        };
    }
    // Normalisiert: "756.XXXX.XXXX.XX"
    return {
        valid: true,
        normalized: `${digits.slice(0,3)}.${digits.slice(3,7)}.${digits.slice(7,11)}.${digits.slice(11)}`
    };
}

// Hängt Validierungs-Status an ein Input-Feld + sein Status-Element.
// onBlur=true: bei gültiger AHV-Nr automatisch ins Standard-Format normalisieren
// (756.XXXX.XXXX.XX), damit die Anzeige konsistent ist.
function validateAhvField(inputEl, onBlur) {
    const statusEl = document.getElementById(inputEl.id + '-status');
    if (!statusEl) return;
    const result = validateAhvNummer(inputEl.value);
    if (result.valid === null) {
        statusEl.textContent = '';
        inputEl.style.borderColor = '';
        return;
    }
    if (result.valid) {
        statusEl.textContent = '✓ AHV-Nr. gültig';
        statusEl.style.color = '#15803d';
        inputEl.style.borderColor = '#86efac';
        if (onBlur) inputEl.value = result.normalized;
    } else {
        statusEl.textContent = '✗ ' + result.error;
        statusEl.style.color = '#dc2626';
        inputEl.style.borderColor = '#fca5a5';
    }
}

// ── Hilfsfunktionen ────────────────────────────
// Optional 3. Argument: Field-Code (permit, passport, ahv_card, ...).
// Wenn ein verknüpftes Dokument für diesen MA existiert, erscheint ein
// 📎-Button rechts vom Wert. Klick öffnet das neueste Dokument im Preview.
function linkedDocButton(linkedCode) {
    if (!linkedCode) return '';
    const hasDoc = window._linkedDocCodes && window._linkedDocCodes.has(linkedCode);
    // Walter-Vorgabe 10.07.2026: «vorhanden» muss klar erkennbar sein —
    // GRÜN mit Häkchen; «fehlt» bleibt blass mit gestricheltem Rand.
    const styleActive   = "background:#dcfce7;border:1px solid #86efac;color:#15803d";
    const styleInactive = "background:#f8f7f4;border:1px dashed #d5d0c6;color:#b3ada1";
    const tooltip = hasDoc ? 'Dokument vorhanden — klicken zum Öffnen' : 'Noch kein Dokument vorhanden — klicken um hochzuladen';
    return `<button class="emp-field-docbtn" data-linked-code="${linkedCode}" title="${tooltip}"
               onclick="openLinkedDoc('${linkedCode}')"
               style="margin-left:8px;${hasDoc ? styleActive : styleInactive};border-radius:6px;padding:2px 7px;cursor:pointer;vertical-align:middle;display:inline-flex;align-items:center;gap:3px;font-size:11px;font-weight:600;line-height:1;transition:all .15s">
               <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                 <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                 <polyline points="14 2 14 8 20 8"/>
                 <line x1="16" y1="13" x2="8" y2="13"/>
                 <line x1="16" y1="17" x2="8" y2="17"/>
                 <line x1="10" y1="9" x2="8" y2="9"/>
               </svg>
               <span>Doku${hasDoc ? ' ✓' : ''}</span>
           </button>`;
}

// Nachgeladenes linked-codes-Set → bestehende Doku-Buttons tauschen,
// OHNE die ganze Übersicht (Verträge/KTG) neu zu bauen.
function _ovPatchLinkedDocButtons() {
    document.querySelectorAll('.emp-field-docbtn[data-linked-code]').forEach(btn => {
        const code = btn.getAttribute('data-linked-code');
        if (!code) return;
        const html = linkedDocButton(code);
        if (!html) return;
        const tmp = document.createElement('span');
        tmp.innerHTML = html;
        const neu = tmp.firstElementChild;
        if (neu) btn.replaceWith(neu);
    });
}

function field(label, value, linkedCode, easyworkInfo = false) {
    const empty = !value || value === 'null' || value === 'undefined';
    const docBtn = linkedDocButton(linkedCode);
    return `<div class="emp-field liquid-field${easyworkInfo ? ' easywork-source-field' : ''}">
        <div class="emp-field-label">${label}</div>
        <div class="emp-field-value${empty ? ' empty' : ''}${easyworkInfo ? ' easywork-info' : ''}">${empty ? '–' : value}${docBtn}</div>
    </div>`;
}

function inlineEditField(label, inputHtml) {
    return `<div class="emp-field liquid-field inline-edit-field">
        <div class="emp-field-label">${label}</div>
        <div class="emp-field-value">${inputHtml}</div>
    </div>`;
}

function yesNoToggle(id, value) {
    return `<div class="emp-yesno-toggle" data-target="${id}">
        <input type="hidden" id="${id}" value="${value ? 'true' : 'false'}">
        <button type="button" class="${value ? 'active' : ''}" onclick="empSetYesNo('${id}', true)">ja</button>
        <button type="button" class="${!value ? 'active' : ''}" onclick="empSetYesNo('${id}', false)">nein</button>
    </div>`;
}

function empSetYesNo(id, value) {
    const input = document.getElementById(id);
    if (input) input.value = value ? 'true' : 'false';
    const wrap = input?.closest('.emp-yesno-toggle');
    if (wrap) {
        wrap.querySelectorAll('button').forEach(btn => {
            const isYes = btn.textContent.trim() === 'ja';
            btn.classList.toggle('active', isYes === value);
        });
    }
    // Vertrags-Modal (ce-*) speichert über «Speichern» — kein Inline-Dirty.
    if (!String(id || '').startsWith('ce-')) empInlineDirty();
    if (id.startsWith('ov-') && typeof ovDirty === 'function') ovDirty();
}

function empInlineDirty() {
    // Walter-Bug 16.07.2026: der Button steht mit style="display:none" im DOM —
    // visibility allein machte ihn nie sichtbar. display umschalten. Es gibt
    // mehrere Inline-Speichern-Buttons (oben + direkt neben den Kuendigungs-
    // Feldern) — alle einblenden.
    document.querySelectorAll('.emp-inline-save').forEach(btn => {
        btn.style.display = 'inline-flex';
        btn.style.visibility = 'visible';
    });
}

// «Gekündigt am» erfasst → «Kündigung per» automatisch aus der Kündigungs-
// frist des MA berechnen (Walter 16.07.2026). Gleiche Quelle wie die
// Kündigungs-Seite: GET /api/kuendigung/{id}/info?datum=… liefert den
// letzten Arbeitstag (Probezeit/Dienstjahre/Filial-Einstellung inklusive).
async function kuendAmChanged(empId) {
    // Übersicht (ov-*) ist die aktive Quelle; ef-* nur noch Fallback (Edit-Legacy).
    const am = document.getElementById('ov-kuendAm')?.value
            || document.getElementById('ef-kuendAm')?.value;
    const perEl = document.getElementById('ov-kuendPer')
               || document.getElementById('ef-kuendPer');
    if (!am || !perEl) return;
    try {
        const r = await fetch(`/api/kuendigung/${empId}/info?datum=${am}`, { headers: ah() });
        if (!r.ok) return;
        const info = await r.json();
        const per = info?.notice?.letzterArbeitstag || info?.letzterArbeitstag;
        if (per) {
            perEl.value = String(per).slice(0, 10);
            if (typeof ovDirty === 'function') ovDirty();
            else empInlineDirty();
        }
    } catch (_) {}
}

// Klick auf 📎: springt in den Dokumente-Tab des MA und filtert auf den
// passenden Dokument-Typ. So sieht der User ALLE Dokumente dieses Typs
// (auch wenn mehrere vorhanden oder unsauber abgelegt) und kann das
// richtige selbst auswählen.
async function openLinkedDoc(code) {
    if (!selectedEmployeeId) return;
    // Taxonomie direkt über die API holen (_dokState ist mit `let` in
    // index.html deklariert → nicht auf window verfügbar).
    let taxonomy = null;
    try {
        const r = await fetch('/api/documents/taxonomie', { headers: ah() });
        if (r.ok) taxonomy = await r.json();
    } catch {}
    if (!taxonomy || taxonomy.length === 0) {
        alert('Dokumenten-Struktur konnte nicht geladen werden.');
        return;
    }

    // Ersten Typ mit passendem linked_field_code suchen.
    let matchedTyp = null, matchedKat = null;
    for (const k of taxonomy) {
        const t = (k.typen || []).find(x => x.linkedFieldCode === code);
        if (t) { matchedTyp = t; matchedKat = k; break; }
    }
    if (!matchedTyp) {
        alert('Kein Dokument-Typ mit dieser Verknüpfung gefunden.\n'
            + 'Bitte unter Systemeinstellungen → Dokument-Struktur einen Typ '
            + 'mit dem passenden Code anlegen.');
        return;
    }

    // Walter-Vorgabe 17.07.2026 (Ruecknahme des Umwegs): existiert genau EIN
    // Dokument dieses Typs, wird es DIREKT im Vorschaufenster geoeffnet —
    // OHNE Wechsel in die Doku-Verwaltung (gruene Pille = ansehen). Erst bei
    // keinem (Upload noetig) oder mehreren Dokumenten (Auswahl noetig) geht
    // es wie bisher in den Dokumente-Tab mit gesetztem Filter.
    try {
        const rd = await fetch(`/api/documents/by-employee/${selectedEmployeeId}`, { headers: ah() });
        if (rd.ok) {
            const alle = await rd.json();
            const typDocs = (alle || []).filter(d => d.dokumentTypId === matchedTyp.id);
            if (typDocs.length === 1 && typeof qstOpenBefreiungsDok === 'function') {
                qstOpenBefreiungsDok(selectedEmployeeId, typDocs[0].id);
                return;
            }
        }
    } catch {}

    // Auf Dokumente-Tab umschalten — triggert loadEmpDokumente().
    if (typeof switchEmpTab === 'function') switchEmpTab('dokumente');

    // Warten bis loadEmpDokumente die Liste fertig geladen hat — sonst
    // überschreibt sein renderDokumenteUi unseren Filter wieder.
    let attempts = 0;
    while (!document.getElementById('empTabDokumente')
            ?.querySelector('.dok-tree-node')
           && attempts < 30) {
        await new Promise(r => setTimeout(r, 100));
        attempts++;
    }

    // Filter setzen — gleiche Funktion wie im Tree-Klick.
    if (typeof dokSelectType === 'function') {
        dokSelectType(matchedTyp.id, matchedKat.id);
    }

    // Walter-Vorgabe 07.06.2026: bei genau EINEM Dokument im gefilterten
    // Typ direkt das Vorschau-Panel öffnen — spart einen Klick. Bei mehreren
    // bleibt die Liste sichtbar (Walter wählt selbst).
    try {
        const docs = (typeof _dokState !== 'undefined' && Array.isArray(_dokState.docs))
            ? _dokState.docs.filter(d => d.dokumentTypId === matchedTyp.id)
            : [];
        if (docs.length === 1 && typeof dokOpenPreviewPanel === 'function') {
            dokOpenPreviewPanel(docs[0].id);
        }
    } catch {}
}

// Sprung aus einer Krank-/Unfall-Absenz direkt in den Dokumente-Tab, gefiltert
// auf den Dokument-Typ „Arztzeugnis". Anders als openLinkedDoc() matcht es über
// den Typ-NAMEN (nicht über linked_field_code) — der Typ „Arztzeugnis" ist im
// Standard-Dokumentenraster (Kategorie Absenzen) fix vorhanden.
async function openAbsenceArztzeugnis() {
    if (!selectedEmployeeId) return;
    // Auf Dokumente-Tab umschalten — triggert loadEmpDokumente().
    if (typeof switchEmpTab === 'function') switchEmpTab('dokumente');

    let taxonomy = null;
    try {
        const r = await fetch('/api/documents/taxonomie', { headers: ah() });
        if (r.ok) taxonomy = await r.json();
    } catch {}
    if (!taxonomy || taxonomy.length === 0) {
        alert('Dokumenten-Struktur konnte nicht geladen werden.');
        return;
    }

    // Ersten Typ suchen, dessen Name „Arztzeugnis" enthält (case-insensitive).
    let matchedTyp = null, matchedKat = null;
    for (const k of taxonomy) {
        const t = (k.typen || []).find(x => (x.name || '').toLowerCase().includes('arztzeugnis'));
        if (t) { matchedTyp = t; matchedKat = k; break; }
    }
    if (!matchedTyp) {
        alert('Kein Dokument-Typ „Arztzeugnis" gefunden.\n'
            + 'Bitte unter Systemeinstellungen → Dokument-Struktur einen Typ '
            + 'namens „Arztzeugnis" anlegen.');
        return;
    }

    // Warten bis loadEmpDokumente die Tree-Liste fertig gerendert hat — sonst
    // überschreibt sein renderDokumenteUi unseren Filter wieder.
    let attempts = 0;
    while (!document.getElementById('empTabDokumente')
            ?.querySelector('.dok-tree-node')
           && attempts < 30) {
        await new Promise(r => setTimeout(r, 100));
        attempts++;
    }

    // Filter setzen — gleiche Funktion wie im Tree-Klick.
    if (typeof dokSelectType === 'function') {
        dokSelectType(matchedTyp.id, matchedKat.id);
    }

    // Walter-Vorgabe 07.06.2026: bei genau EINEM Dokument im gefilterten
    // Typ direkt das Vorschau-Panel öffnen — spart einen Klick. Bei mehreren
    // bleibt die Liste sichtbar (Walter wählt selbst).
    try {
        const docs = (typeof _dokState !== 'undefined' && Array.isArray(_dokState.docs))
            ? _dokState.docs.filter(d => d.dokumentTypId === matchedTyp.id)
            : [];
        if (docs.length === 1 && typeof dokOpenPreviewPanel === 'function') {
            dokOpenPreviewPanel(docs[0].id);
        }
    } catch {}
}

function getInitials(first, last) {
    const f = (first ?? '').trim()[0] ?? '';
    const l = (last  ?? '').trim()[0] ?? '';
    return (f + l).toUpperCase() || '?';
}

function formatDate(dateStr) {
    if (!dateStr) return '–';
    const d = new Date(dateStr);
    if (isNaN(d)) return dateStr;
    return d.toLocaleDateString('de-CH');
}

// Geschlecht-Code (female/male/m/w) → menschen-lesbares Label.
// Übersetzung folgt der UI-Sprache (i18n); Default ist Deutsch falls i18n
// noch nicht geladen ist. Edit-Modal-Dropdown bleibt mit englischen
// value-Codes (female/male) — wir mappen nur die Anzeige.
function formatGender(g) {
    if (!g) return null;
    const v = String(g).toLowerCase();
    if (v === 'female' || v === 'f' || v.startsWith('w')) return _t('ma.value.gender.female','Weiblich');
    if (v === 'male'   || v === 'm' || v.startsWith('m')) return _t('ma.value.gender.male',  'Männlich');
    if (v === 'divers' || v === 'diverse' || v === 'andere' || v === 'other' || v === 'x' || v === 'd') return _t('ma.value.gender.divers','Divers');
    return g;
}

// Zivilstand-Code (ledig/verheiratet/...) → Anzeige-Label.
// Backend speichert lowercase ohne Umlaute; UI zeigt mit korrekter Schreibweise
// und in EN/DE.
function formatMaritalStatus(s) {
    if (!s) return null;
    const key = 'ma.value.maritalStatus.' + String(s).toLowerCase().trim();
    if (window.i18n && window.i18n.t) {
        const v = window.i18n.t(key);
        if (v && v !== key) return v;   // gefunden
    }
    // Fallback: ersten Buchstaben gross
    return String(s).charAt(0).toUpperCase() + String(s).slice(1);
}

// Sprach-Code (de/fr/it/en) → Anzeige-Label in der UI-Sprache.
function formatLanguage(code) {
    if (!code) return null;
    const key = 'ma.value.language.' + String(code).toLowerCase().trim();
    if (window.i18n && window.i18n.t) {
        const v = window.i18n.t(key);
        if (v && v !== key) return v;
    }
    return code;
}

// Anrede (Herr/Frau, ggf. lowercase aus DB) → Display.
function formatSalutation(s) {
    if (!s) return null;
    const v = String(s).toLowerCase().trim();
    if (v === 'herr' || v === 'mr' || v === 'mr.') return _t('ma.value.salutation.herr','Herr');
    if (v === 'frau' || v === 'ms' || v === 'mrs.' || v === 'ms.') return _t('ma.value.salutation.frau','Frau');
    if (v === 'divers' || v === 'diverse') return null;
    return s;
}

// ══════════════════════════════════════════════
// MITARBEITER BEARBEITEN
// ══════════════════════════════════════════════

// Permit-Type Cache – ausschliesslich aus der Datenbank (permit_type Tabelle)
let _permitTypeCache = null;
async function getPermitTypes() {
    if (_permitTypeCache) return _permitTypeCache;
    try {
        const res = await fetch('/api/permittypes', { headers: ah() });
        if (res.ok) {
            _permitTypeCache = await res.json();
        } else {
            console.error('Permit-Types konnten nicht geladen werden:', res.status);
            _permitTypeCache = [];
        }
    } catch (e) {
        console.error('Fehler beim Laden der Permit-Types:', e);
        _permitTypeCache = [];
    }
    return _permitTypeCache;
}

// Nationalitäten-Cache (analog zu PermitTypes). Wird einmalig geladen und
// für den Nationalitäten-Dropdown im Edit-Formular wiederverwendet.
// ── Nationalitäts-Combobox (Walter 12.07.2026, v2) ──────────────────────
// Die native Select-Aufklappliste fängt alle Tasten selbst ab — «BGR»
// tippen landet auf Bhutan. Darum wird das Select durch ein SUCH-FELD mit
// eigener Liste ersetzt (Liquid-Glass-Konvention: native Selects ersetzen,
// Original-Select bleibt versteckt im DOM und trägt weiterhin den Wert —
// alle Speicher-Pfade lesen unverändert sel.value). Suche matcht Name,
// alpha-2 UND Ausweis-Kürzel alpha-3, als «enthält»-Suche.
function natMakeCombo(sel) {
    if (!sel || sel._natCombo || sel.disabled) return;   // easy@work-gesperrt → nativ lassen
    sel._natCombo = true;
    // Walter-Bug 20.08.2026 («zwei Felder»): der globale liquid-select-Enhancer
    // hat das Select evtl. schon in ein .lqsel-wrap (Button + Panel) umgebaut —
    // dann stünden Combo-Input UND Liquid-Button doppelt da. Liquid-Wrap
    // rückbauen; das Select wird gleich darunter zur unsichtbaren Datenquelle
    // des Combo-Controls (.no-liquid = erlaubte technische Ausnahme, verhindert
    // dass der MutationObserver es erneut umbaut).
    const lqWrap = sel.closest('.lqsel-wrap');
    if (lqWrap && lqWrap.parentNode) {
        lqWrap.parentNode.insertBefore(sel, lqWrap);
        lqWrap.remove();
        sel._lq = false;
        sel.style.display = '';
    }
    sel.classList.add('no-liquid');
    const readOpts = () => Array.from(sel.options).map(o => ({ value: o.value, label: (o.textContent || '').trim() }));
    let opts = readOpts();
    const wrap = document.createElement('div');
    wrap.style.cssText = 'position:relative';
    sel.parentNode.insertBefore(wrap, sel);
    wrap.appendChild(sel);
    sel.style.display = 'none';
    const inp = document.createElement('input');
    inp.type = 'text';
    inp.className = sel.className || 'ef-input';
    inp.placeholder = 'Name oder Kürzel, z.B. BGR…';
    inp.autocomplete = 'off';
    const list = document.createElement('div');
    list.style.cssText = 'position:absolute;top:100%;left:0;right:0;z-index:3000;background:#fff;border:1px solid #e2ddd3;border-radius:10px;box-shadow:0 12px 28px rgba(60,55,48,0.18);max-height:260px;overflow:auto;display:none;margin-top:4px';
    wrap.appendChild(inp);
    wrap.appendChild(list);
    const curLabel = () => {
        const c = opts.find(o => o.value === sel.value);
        return c && c.value !== '' ? c.label : '';
    };
    inp.value = curLabel();
    let items = [], hi = -1;
    const norm = s => (s || '').toUpperCase();
    function render(q) {
        const Q = norm(q);
        items = opts.filter(o => o.value !== '' && (!Q || norm(o.label).includes(Q)));
        // Walter 20.08.2026: Treffer, die mit der Eingabe BEGINNEN, zuerst
        // («bul» → Bulgarien vor Ländern, die «bul» nur enthalten).
        if (Q) items.sort((a, b) =>
            (norm(b.label).startsWith(Q) ? 1 : 0) - (norm(a.label).startsWith(Q) ? 1 : 0)
            || a.label.localeCompare(b.label));
        hi = items.length ? 0 : -1;
        list.innerHTML = items.slice(0, 300).map((o, i) =>
            `<div data-i="${i}" style="padding:7px 12px;font-size:13px;cursor:pointer;color:#3f3f3f;${i === hi ? 'background:#ece9e2' : ''}">${esc(o.label)}</div>`).join('')
            || '<div style="padding:8px 12px;color:#8b8b8b;font-size:12.5px">kein Treffer</div>';
        list.style.display = 'block';
    }
    function paint() {
        Array.from(list.children).forEach((el, i) => { el.style.background = i === hi ? '#ece9e2' : ''; });
        const el = list.children[hi];
        if (el && el.scrollIntoView) el.scrollIntoView({ block: 'nearest' });
    }
    function choose(i) {
        const o = items[i];
        if (!o) return;
        sel.value = o.value;
        sel.dispatchEvent(new Event('change'));
        inp.value = o.label;
        list.style.display = 'none';
    }
    inp.addEventListener('focus', () => { inp.select(); render(''); });
    inp.addEventListener('input', () => render(inp.value));
    inp.addEventListener('keydown', (e) => {
        if (list.style.display === 'none' && (e.key === 'ArrowDown' || e.key === 'Enter')) { render(inp.value); e.preventDefault(); return; }
        if (e.key === 'ArrowDown')      { hi = Math.min(hi + 1, items.length - 1); paint(); e.preventDefault(); }
        else if (e.key === 'ArrowUp')   { hi = Math.max(hi - 1, 0); paint(); e.preventDefault(); }
        else if (e.key === 'Enter')     { choose(hi); e.preventDefault(); }
        else if (e.key === 'Escape')    { list.style.display = 'none'; }
    });
    list.addEventListener('mousedown', (e) => {   // mousedown: feuert VOR blur
        const t = e.target.closest('[data-i]');
        if (t) { choose(parseInt(t.getAttribute('data-i'), 10)); e.preventDefault(); }
    });
    inp.addEventListener('blur', () => setTimeout(() => {
        list.style.display = 'none';
        inp.value = curLabel();   // Tipp-Rest ohne Auswahl → zurück auf den gewählten Stand
    }, 150));

    // KRITISCH (Walter-Bug 13.07.2026, Enisa/Shkozjan): das Familien-Modal
    // baut die Optionen bei JEDEM Öffnen neu (innerHTML) — die Combobox
    // zeigte dann noch den ALTEN Text («Schweiz»), obwohl sel.value leer
    // war → nationality_id wurde als NULL gespeichert. Options-Änderungen
    // beobachten und Optionsliste + Anzeige neu synchronisieren.
    new MutationObserver(() => {
        opts = readOpts();
        inp.value = curLabel();
        if (list.style.display !== 'none') render(inp.value);
    }).observe(sel, { childList: true, subtree: true });
}

let _nationalityCache = null;
async function getNationalities() {
    if (_nationalityCache) return _nationalityCache;
    try {
        const res = await fetch('/api/nationalities', { headers: ah() });
        if (res.ok) {
            _nationalityCache = await res.json();
        } else {
            console.error('Nationalitäten konnten nicht geladen werden:', res.status);
            _nationalityCache = [];
        }
    } catch (e) {
        console.error('Fehler beim Laden der Nationalitäten:', e);
        _nationalityCache = [];
    }
    return _nationalityCache;
}

// Personal-Tab entfernt (Walter 17.07.2026) — Bearbeiten läuft über die
// Soft-Inputs in der Übersicht. Stub bleibt für Alt-Aufrufer.
async function startEmpEdit() {
    if (!selectedEmployee) return;
    switchEmpTab('uebersicht');
}

function buildEmpEditPersonal(emp, permitTypes = [], nationalities = []) {
    const permitOptions = permitTypes
        .filter(p => p.isActive !== false)
        .map(p => `<option value="${p.id}" ${emp.permitTypeId == p.id ? 'selected' : ""}>${p.description ?? p.code}</option>`)
        .join("");
    // Nationalitäten-Optionen — id, code, name. Die meisten User wollen nach
    // Land suchen, daher zeigen wir den Namen primär; Code sekundär falls
    // unterschiedlich (z.B. "Schweiz" vs CH).
    const nationalityOptions = (nationalities || [])
        .filter(n => n.isActive !== false)
        .map(n => {
            const id    = n.id   ?? n.Id;
            const code  = n.code ?? n.Code ?? '';
            const name  = n.name ?? n.Name ?? code;
            const sel   = (emp.nationalityId == id) ? 'selected' : '';
            // Ausweis-Kürzel (alpha-3) mit anzeigen (Walter 12.07.2026).
            const codes = code ? (n.code3 ? `${code} / ${n.code3}` : code) : '';
            return `<option value="${id}" ${sel}>${name}${codes && code !== name ? ' (' + codes + ')' : ''}</option>`;
        }).join('');
    const isMtp = emp.employmentModel === 'MTP';
    const isFix = emp.employmentModel === 'FIX' || emp.employmentModel === 'FIX-M';
    const ewTitle = 'Kommt aus easy@work und ist hier nicht editierbar.';
    const ewInput = `readonly data-easywork-locked="1" title="${ewTitle}"`;
    const ewSelect = `disabled data-easywork-locked="1" title="${ewTitle}"`;
    return `
    <div class="emp-section-title">${_t('ma.section.personalien','Personalien')}</div>
    <div class="emp-field-grid easywork-info-grid emp-flow-line emp-personal-main-line">
        ${eField(_t('ma.field.salutation','Anrede'), `<select id="ef-salutation" class="ef-input" ${ewSelect}>
            <option value="">–</option>
            <option value="Herr"   ${emp.salutation==='Herr'  ?'selected':''}>${_t('ma.value.salutation.herr','Herr')}</option>
            <option value="Frau"   ${emp.salutation==='Frau'  ?'selected':''}>${_t('ma.value.salutation.frau','Frau')}</option>
        </select>`)}
        ${eField(_t('ma.field.letterSalutation','Briefanrede'), `<input id="ef-letterSalutation" class="ef-input" value="${esc(emp.letterSalutation)}" placeholder="${_t('ma.placeholder.letterSalutation','z.B. Sehr geehrte Frau Muster')}">`)}
        ${eField(_t('ma.field.maidenName','Ledigname'), `<input id="ef-maidenName" class="ef-input" value="${esc(emp.maidenName)}">`)}
        ${eField(_t('ma.field.shortName','Kurzname'),   `<input id="ef-shortName"  class="ef-input" value="${esc(emp.shortName)}">`)}
        ${eField(_t('ma.field.gender','Geschlecht'), `<select id="ef-gender" class="ef-input" ${ewSelect}>
            <option value="">–</option>
            <option value="female" ${emp.gender==='female'?'selected':''}>${_t('ma.value.gender.female','Weiblich')}</option>
            <option value="male"   ${emp.gender==='male'  ?'selected':''}>${_t('ma.value.gender.male','Männlich')}</option>
            <option value="divers" ${emp.gender==='divers'?'selected':''}>${_t('ma.value.gender.divers','Divers')}</option>
        </select>`)}
    </div>

    <!-- Walter 26.05.2026: Adresse + Kontakt in die Personalien-Card. -->
    <div class="emp-field-grid easywork-info-grid emp-flow-line emp-address-line">
        ${eField(_t('ma.field.street','Strasse'),       `<input id="ef-street"  class="ef-input" value="${esc(emp.street)}" ${ewInput}>`)}
        ${eField(_t('ma.field.zipCode','PLZ'),          `<input id="ef-zip" class="ef-input" value="${esc(emp.zipCode)}" inputmode="numeric" maxlength="4" ${ewInput}>`)}
        ${eField(_t('ma.field.city','Ort'),             `<input id="ef-city" class="ef-input" value="${esc(stripCityCantonSuffix(emp.city))}" ${ewInput}>`)}
        ${eField(_t('ma.field.canton','Kanton'),        renderKantonSelect('ef-canton', emp.cantonCode, ewSelect))}
        ${eField(_t('ma.field.country','Land'),         `<input id="ef-country" class="ef-input" value="${esc(emp.country ?? 'CH')}" ${ewInput}>`)}
    </div>
    <div class="emp-field-grid easywork-info-grid emp-flow-line emp-personal-extra-line">
        ${eField(_t('ma.field.maritalStatus','Zivilstand'), `<select id="ef-zivilstand" class="ef-input" ${ewSelect}>
            <option value="">–</option>
            <option value="unbekannt"                  ${(emp.zivilstand ?? emp.maritalStatus)==='unbekannt'                  ?'selected':''}>${_t('ma.value.maritalStatus.unbekannt','Unbekannt')}</option>
            <option value="ledig"                      ${(emp.zivilstand ?? emp.maritalStatus)==='ledig'                      ?'selected':''}>${_t('ma.value.maritalStatus.ledig','Ledig')}</option>
            <option value="verheiratet"                ${(emp.zivilstand ?? emp.maritalStatus)==='verheiratet'                ?'selected':''}>${_t('ma.value.maritalStatus.verheiratet','Verheiratet')}</option>
            <option value="geschieden"                 ${(emp.zivilstand ?? emp.maritalStatus)==='geschieden'                 ?'selected':''}>${_t('ma.value.maritalStatus.geschieden','Geschieden')}</option>
            <option value="verwitwet"                  ${(emp.zivilstand ?? emp.maritalStatus)==='verwitwet'                  ?'selected':''}>${_t('ma.value.maritalStatus.verwitwet','Verwitwet')}</option>
            <option value="getrennt"                   ${(emp.zivilstand ?? emp.maritalStatus)==='getrennt'                   ?'selected':''}>${_t('ma.value.maritalStatus.getrennt','Getrennt')}</option>
            <option value="eingetragene_partnerschaft" ${(emp.zivilstand ?? emp.maritalStatus)==='eingetragene_partnerschaft' ?'selected':''}>${_t('ma.value.maritalStatus.eingetragene_partnerschaft','Eingetragene Partnerschaft')}</option>
        </select>`)}
        ${eField(_t('ma.field.maritalSince','Zivilstand seit'), `<input id="ef-maritalStatusSince" class="ef-input" type="date" value="${toDateInput(emp.maritalStatusSince)}">`)}
        ${eField(_t('ma.field.religion','Konfession'), `<select id="ef-religion" class="ef-input">
            <option value="">–</option>
            <option value="evangelisch_reformiert" ${emp.religion==='evangelisch_reformiert'?'selected':''}>${_t('ma.value.religion.evangelisch_reformiert','Evang.-reformiert')}</option>
            <option value="roemisch_katholisch"    ${emp.religion==='roemisch_katholisch'   ?'selected':''}>${_t('ma.value.religion.roemisch_katholisch','Röm.-katholisch')}</option>
            <option value="christ_katholisch"      ${emp.religion==='christ_katholisch'     ?'selected':''}>${_t('ma.value.religion.christ_katholisch','Christ-katholisch')}</option>
            <option value="andere"                 ${emp.religion==='andere'                ?'selected':''}>${_t('ma.value.religion.andere','Andere')}</option>
            <option value="keine"                  ${emp.religion==='keine'                 ?'selected':''}>${_t('ma.value.religion.keine','Keine')}</option>
        </select>`)}
        ${eField(_t('ma.field.nationality','Nationalität'), `<select id="ef-nationalityId" class="ef-input" ${ewSelect}>
            <option value="">–</option>
            ${nationalityOptions}
        </select>`)}
        ${eField(_t('ma.field.zemis','ZEMIS-Nr.'), `<input id="ef-zemisNumber" class="ef-input" value="${esc(emp.zemisNumber)}" placeholder="${_t('ma.placeholder.zemis','z.B. 12345678.9')}">`)}
    </div>
    <div class="emp-field-grid easywork-info-grid emp-flow-line emp-contact-line">
        ${eField(_t('ma.field.phone','Telefon'), `<input id="ef-phone" class="ef-input" type="tel" value="${esc(emp.phoneMobile)}" placeholder="${_t('ma.placeholder.phone','+41 79 409 43 33')}" ${ewInput}>`)}
        ${eField('Telefon 2', `<input id="ef-phone2" class="ef-input" type="tel" value="${esc(emp.phone2)}" placeholder="${_t('ma.placeholder.phone','+41 79 409 43 33')}" oninput="validatePhone(this)" onblur="validatePhoneBlur(this)">`)}
        ${eField(_t('ma.field.email','E-Mail'),  `<input id="ef-email" class="ef-input" type="email" value="${esc(emp.email)}" ${ewInput}>`)}
    </div>
    <div id="ef-plz-hint" style="font-size:12px;margin-top:-6px;margin-bottom:6px"></div>

    <div class="emp-section-title" style="margin-top:2px">${_t('ma.section.anstellung','Anstellung')}</div>
    <!-- Walter-Vorgabe 07.06.2026: 5 Anstellungs-Felder in EINER Zeile.
         Eintritt/Austritt/Aktiv links, die zwei Booleans (L-GAV / <8 h)
         rechts schmaler. -->
    <div class="emp-field-grid easywork-info-grid emp-flow-line emp-employment-line">
        ${eField(_t('ma.field.exitDate','Austrittsdatum'),
            `<input id="ef-exit"  class="ef-input" type="date" value="${toDateInput(emp.exitDate)}" ${ewInput}>`)}
        ${eField('Gekündigt am',
            `<input id="ef-kuendAm" class="ef-input" type="date" value="${toDateInput(emp.kuendigungAusgesprochenAm)}">`,
            'Vom Kündigungsschreiben gesetzt')}
        ${eField('Kündigung per',
            `<input id="ef-kuendPer" class="ef-input" type="date" value="${toDateInput(emp.kuendigungPer)}">`,
            'Letzter Arbeitstag gemäss Kündigung')}
        ${eField('Kündigung durch',
            `<select id="ef-kuendDurch" class="ef-input">
                <option value="">—</option>
                <option value="AG"${(emp.kuendigungDurch || '').toUpperCase() === 'AG' ? ' selected' : ''}>durch uns</option>
                <option value="AN"${(emp.kuendigungDurch || '').toUpperCase() === 'AN' ? ' selected' : ''}>durch Mitarbeiter</option>
             </select>`,
            'Wer die Kündigung ausgesprochen hat')}
        ${eField('Austrittsgrund',
            `<select id="ef-austrittsgrund" class="ef-input">${_austrittsgrundOptionsHtml(emp.austrittsgrund)}</select>`,
            'Für Statistik')}
        ${eField('L-GAV',
            `<label style="display:flex;align-items:center;gap:8px;height:19px;cursor:pointer">
                 <input id="ef-lgavPflichtig" type="checkbox" ${emp.lgavPflichtig ? 'checked' : ''}
                        style="width:16px;height:16px;cursor:pointer;margin:0">
                 <span style="font-size:12px;color:#475569">pflichtig</span>
             </label>`,
            _t('ma.field.lgavPflichtigHint','Jährlicher Abzug im Lohnlauf'))}
    </div>
    <div style="margin:6px 0 0;font-size:11px;color:#94a3b8">Nachtarbeit-Untersuchung (Ausstellungsdatum + Dokument) wird in der Ansicht direkt erfasst.</div>
    <div style="margin:4px 0 16px;font-size:11.5px;color:#64748b;line-height:1.45">
        ${_t('ma.entryDate.hint','Eintrittsdatum wird benötigt für: Sperrfrist-Berechnung (Art. 336c OR), Karenzjahr-Berechnung (Krank/Unfall), Ferien-Kürzung (Art. 329b OR), Dienstjubiläen.')}
    </div>

    <!-- Versteckte Felder für Backwards-Compat: Edit-Save sendet sie immer noch,
         aber sie sind read-only und werden vom History-Sync ohnehin überschrieben.
         Bei „MA ohne Lohn" (IsPayrollExcluded) ist die Aufenthalts-Sektion
         ausgeblendet — die Hidden-Inputs bleiben aber bestehen, damit der
         Save-Handler den DB-Wert nicht versehentlich auf 0/null setzt. -->
    <input type="hidden" id="ef-permitType" value="${emp.permitTypeId ?? 0}">
    <input type="hidden" id="ef-permitExpiry" value="${toDateInput(emp.permitExpiryDate)}">

    ${emp.isPayrollExcluded ? `
    <div style="margin:14px 0;padding:12px 16px;background:#fef3c7;border:1px solid #fbbf24;border-radius:8px;color:#92400e;font-size:13px;line-height:1.55">
        <strong>⛔ ${_t('ma.phantom.title','MA ohne Lohn')}</strong> — ${_t('ma.phantom.editDesc','Phantom-MA für easy@work-Zugang. Bewilligung und Zusatzadressen werden hier nicht angeboten, da dieser MA keinen Vertrag und keine Lohnzahlung hat. Über die Checkbox „Kein Lohn" unten kann die Markierung wieder aufgehoben werden.')}
    </div>
    ` : ''}

    <!-- Arbeitsverhältnis-Sektion (Eintritt, Modell, Pensum, Stundenlohn) wurde
         entfernt — wird im Verträge-Tab gepflegt. Eintrittsdatum + Personal-Nr.
         stehen ohnehin im MA-Detail-Header. -->

    ${currentUser?.role === 'admin' ? `
    <div class="emp-section-title">${_t('ma.section.lohn','Lohn')}</div>
    <div style="padding:10px 14px;background:${emp.isPayrollExcluded ? '#fef3c7' : '#f8fafc'};border:1px solid ${emp.isPayrollExcluded ? '#fde68a' : '#e2e8f0'};border-radius:8px">
        <label style="display:flex;align-items:flex-start;gap:10px;cursor:pointer">
            <input type="checkbox" id="ef-isPayrollExcluded"
                   ${emp.isPayrollExcluded ? 'checked' : ''}
                   style="margin-top:3px;width:16px;height:16px;flex-shrink:0">
            <div style="font-size:13px;color:#0f172a">
                <b>${_t('ma.payrollExcluded.title','Kein Lohn')}</b> <span style="color:#94a3b8;font-weight:400;font-size:11.5px">${_t('ma.payrollExcluded.role','(nur Admin)')}</span>
                <div style="font-size:11.5px;color:#64748b;margin-top:3px;line-height:1.45">
                    ${_t('ma.payrollExcluded.desc','MA wird im System geführt (Stempelsystem, Vorgesetzter-Referenz, Posteingang) — aber NICHT im Lohn-Tab abgerechnet. Beim CSV-Re-Import bleibt diese Markierung erhalten.')}
                </div>
            </div>
        </label>
    </div>` : ''}

    ${!emp.isPayrollExcluded ? `
    <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between;margin-top:2px">
        <span>${_t('ma.section.otherAddresses','Weitere Adressen')} <span style="font-weight:400;color:#94a3b8;font-size:12px">${_t('ma.section.otherAddrHint','(z.B. Korrespondenz, Ferienwohnung, Sozialamt — Hauptadresse oben)')}</span></span>
        <button type="button" class="btn-emp-add" onclick="openEmployeeAddressModal(null)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            ${_t('ma.btn.addAddress','Adresse hinzufügen')}
        </button>
    </div>
    <div id="otherAddressesContent">
        <div class="emp-placeholder"><span>${_t('ma.loading','Wird geladen…')}</span></div>
    </div>
    ` : ''}`;
    // Hinweis: Bankverbindung + Postfach-Zugang sind NICHT mehr im Edit-Formular —
    // sie leben im eigenen Tab „Bank & Postfach" (Walter-Vorgabe 14.05.2026)
    // und werden dort über eigene Modals/Buttons gepflegt, nicht über das
    // Personal-Edit-Formular.
}

// (buildEmpEditAdressen wurde entfernt — Adresse + Kontakt sind jetzt
// im Personal-Edit-Formular integriert.)

// Hilfshelfer: Edit-Feld
function eField(label, inputHtml, hint) {
    return `<div class="emp-field liquid-field">
        <div class="emp-field-label">${label}</div>
        <div class="emp-field-value">${inputHtml}</div>
        ${hint ? `<div class="emp-field-hint">${hint}</div>` : ''}
    </div>`;
}

// (schwache esc()-Dublette entfernt 22.07.2026 — die vollständige Definition weiter unten gilt)

// Schweizer Kantone — 2-Zeichen-Codes mit deutschem Namen, alphabetisch.
const SWISS_KANTONE = [
    ['AG', 'Aargau'],           ['AI', 'Appenzell Innerrhoden'],
    ['AR', 'Appenzell Ausserrhoden'], ['BE', 'Bern'],
    ['BL', 'Basel-Landschaft'], ['BS', 'Basel-Stadt'],
    ['FR', 'Freiburg'],         ['GE', 'Genf'],
    ['GL', 'Glarus'],           ['GR', 'Graubünden'],
    ['JU', 'Jura'],             ['LU', 'Luzern'],
    ['NE', 'Neuenburg'],        ['NW', 'Nidwalden'],
    ['OW', 'Obwalden'],         ['SG', 'St. Gallen'],
    ['SH', 'Schaffhausen'],     ['SO', 'Solothurn'],
    ['SZ', 'Schwyz'],           ['TG', 'Thurgau'],
    ['TI', 'Tessin'],           ['UR', 'Uri'],
    ['VD', 'Waadt'],            ['VS', 'Wallis'],
    ['ZG', 'Zug'],              ['ZH', 'Zürich']
];

function renderKantonSelect(id, current, extraAttrs = '') {
    const cur = (current ?? '').toString().toUpperCase();
    const opts = SWISS_KANTONE
        .map(([code, name]) => `<option value="${code}" ${code === cur ? 'selected' : ''}>${code} — ${name}</option>`)
        .join('');
    return `<select id="${id}" class="ef-input" ${extraAttrs}>
        <option value="" ${!cur ? 'selected' : ''}>— nicht gepflegt —</option>
        ${opts}
    </select>`;
}

function kantonNameFor(code) {
    if (!code) return null;
    const found = SWISS_KANTONE.find(([c]) => c === code.toUpperCase());
    return found ? found[1] : null;
}

// Lookup für PLZ → Gemeinde(n) + Kanton.
// Wird aus dem Edit-Form heraus aufgerufen, wenn der User die PLZ
// tippt oder das Feld verlässt. Befüllt die Ort- und Kanton-Felder
// automatisch. Bei mehreren Gemeinden pro PLZ erscheint eine Auswahl
// im Ort-Dropdown, der Kanton wird an die erste Gemeinde angepasst
// und aktualisiert sich live, wenn der User eine andere Gemeinde wählt.
let _plzLookupAbort = null;
let _plzLookupCache = new Map();

async function plzLookup(rawPlz) {
    const plz = (rawPlz ?? '').toString().trim();
    const cityInput   = document.getElementById('ef-city');
    const kantonSelect = document.getElementById('ef-canton');
    const hint = document.getElementById('ef-plz-hint');
    if (!cityInput || !kantonSelect || !hint) return;

    if (!/^\d{4}$/.test(plz)) {
        hint.innerHTML = '';
        return;
    }

    // Vorherigen Request abbrechen (z.B. wenn User schnell tippt)
    if (_plzLookupAbort) _plzLookupAbort.abort();
    _plzLookupAbort = new AbortController();

    let locs = _plzLookupCache.get(plz);
    if (!locs) {
        try {
            const res = await fetch(`/api/swiss-locations/by-plz?plz=${encodeURIComponent(plz)}`, {
                headers: ah(),
                signal: _plzLookupAbort.signal
            });
            if (!res.ok) return;
            locs = await res.json();
            _plzLookupCache.set(plz, locs);
        } catch (e) {
            if (e.name === 'AbortError') return;
            return;
        }
    }

    if (!locs.length) {
        hint.innerHTML = `<span style="color:#b45309">⚠ PLZ ${plz} nicht im Ortschaftsverzeichnis gefunden — Ort und Kanton bitte manuell eintragen.</span>`;
        return;
    }

    // Eindeutiger Treffer → automatisch setzen
    if (locs.length === 1) {
        const l = locs[0];
        const ortName = stripCityCantonSuffix(l.ortschaftsname || l.gemeindename);
        cityInput.value   = ortName;
        kantonSelect.value = l.kantonskuerzel;
        // Ortschaft ohne Kanton in Klammern (Walter 02.08.2026)
        hint.innerHTML = `<span style="color:#16a34a">✓ ${esc(ortName)}</span>`;
        return;
    }

    // Mehrere Gemeinden → Combobox im Ort-Feld via HTML5-<datalist>.
    // Walter klickt im Ort-Feld auf das Pfeil-Symbol und sieht die Liste,
    // kann aber jederzeit auch frei tippen (und "off-list"-Werte eingeben).
    // Wenn der getippte/gewählte Wert zu einer Gemeinde matcht, wird der
    // Kanton automatisch synchronisiert.
    bindDatalistToCityInput(cityInput, kantonSelect, null, locs, plz, hint);
}

// Hilfsfunktion: hängt eine <datalist> ans Ort-Input und installiert einen
// input-Handler, der Kanton (und ggf. BFS-Nr.) bei einem Treffer aktualisiert.
function bindDatalistToCityInput(cityEl, cantonEl, bfsEl, locs, plz, hint) {
    if (!cityEl) return;
    const dlId = (cityEl.id || 'plz') + '-datalist';

    // Bestehende datalist (von früherer PLZ-Eingabe) entfernen
    const old = document.getElementById(dlId);
    if (old) old.remove();

    const dl = document.createElement('datalist');
    dl.id = dlId;
    const locName = l => stripCityCantonSuffix(l.ortschaftsname || l.gemeindename);
    dl.innerHTML = locs.map(l => {
        const bfs = l.bfsNr ?? l.bfsNumber ?? l.bfs_number ?? '';
        return `<option value="${esc(locName(l))}" data-kanton="${l.kantonskuerzel}" data-bfs="${bfs}"></option>`;
    }).join('');
    cityEl.setAttribute('list', dlId);
    cityEl.parentElement?.appendChild(dl);

    // Wenn aktueller Wert schon einer der Treffer ist → Kanton/BFS sofort syncen
    const pre = locs.find(l => locName(l) === cityEl.value || l.gemeindename === cityEl.value);
    if (pre) {
        if (cantonEl) cantonEl.value = pre.kantonskuerzel;
        if (bfsEl)    bfsEl.value    = pre.bfsNr ?? pre.bfsNumber ?? pre.bfs_number ?? '';
        cityEl.value = locName(pre);
    }

    // Bei jeder Änderung im Ort-Feld den Kanton aktualisieren falls Match
    cityEl.oninput = () => {
        const match = locs.find(l => locName(l) === cityEl.value || l.gemeindename === cityEl.value);
        if (match) {
            if (cantonEl) cantonEl.value = match.kantonskuerzel;
            if (bfsEl)    bfsEl.value    = match.bfsNr ?? match.bfsNumber ?? match.bfs_number ?? '';
        }
    };

    if (hint) {
        hint.innerHTML = pre
            ? `<span style="color:#16a34a">✓ ${esc(locName(pre))}</span>
               <span style="color:#94a3b8;font-size:11.5px;margin-left:6px">— ${locs.length} Gemeinden für PLZ ${plz}; Ort-Feld öffnen für andere Auswahl.</span>`
            : `<span style="color:#475569">PLZ ${plz} → ${locs.length} Gemeinden — im Ort-Feld auswählen oder frei tippen.</span>`;
    }
}

function cancelEmpEdit() {
    if (selectedEmployee) renderEmployeeDetail(selectedEmployee);
}

// Walter-Vorgabe 18.05.2026: Aktiv-Checkbox prüft beim Entfernen des Hakens
// SOFORT (nicht erst beim Speichern) ob es offene Lohnperioden mit Daten gibt.
// Bei Sperre: Checkbox zurücksetzen + Klartext-Hinweis mit Grund (welche
// Periode, Stempelzeiten und/oder Absenz-Typen mit Datumsbereich).
async function onIsActiveChange(cb, empId) {
    // Label live nachziehen — gleicher visueller Effekt wie vorher
    const lbl = document.getElementById('ef-isactive-label');
    if (lbl) lbl.textContent = cb.checked ? 'aktiv' : 'inaktiv';

    // Aktivieren ist immer erlaubt — Lock greift nur beim Inaktivsetzen.
    if (cb.checked) return;

    try {
        const res = await fetch(`/api/employees/${empId}/deactivate-check`,
                                { headers: ah(), cache: 'no-store' });
        if (!res.ok) {
            console.warn('deactivate-check HTTP', res.status);
            return;
        }
        const data = await res.json();
        if (data.canDeactivate) return;

        // Detail-Meldung aus blockers zusammenbauen — eine Karte je Periode,
        // mit Stempelzeit-Count und Absenz-Zeilen (Typ + Datumsbereich).
        const fmt = d => d ? d.substring(8,10) + '.' + d.substring(5,7) + '.' + d.substring(0,4) : '';
        const lines = (data.blockers || []).map(b => {
            const parts = [];
            if (b.timeEntriesCount > 0)
                parts.push(`• ${b.timeEntriesCount} Stempel-Eintrag(e)`);
            (b.absences || []).forEach(a => {
                const typLabel = ({KRANK:'Krankheit',UNFALL:'Unfall',FERIEN:'Ferien',
                                   SCHULUNG:'Schulung',MUTT_VATER:'Mutter-/Vaterschaft'})[a.type] || a.type;
                parts.push(`• ${typLabel} ${fmt(a.dateFrom)} – ${fmt(a.dateTo)}`);
            });
            return `Lohnperiode '${b.periodLabel}' (${fmt(b.periodFrom)} – ${fmt(b.periodTo)}):\n${parts.join('\n')}`;
        }).join('\n\n');

        alert(`MA kann nicht inaktiv gesetzt werden.\n\n${lines}\n\nBitte zuerst den Lohnlauf abschliessen oder die blockierenden Daten löschen.`);
        // Checkbox zurück auf "aktiv" — der MA bleibt im Form-State aktiv
        cb.checked = true;
        if (lbl) lbl.textContent = 'aktiv';
    } catch (e) {
        console.error('deactivate-check', e);
        // Bei Netzwerkfehler nicht blockieren — der Save-Endpoint wirft sonst
        // sowieso noch die 409 wenn Daten im Weg sind.
    }
}

async function saveEmpEdit() {
    if (!selectedEmployeeId || !selectedEmployee) return;
    const emp = selectedEmployee;
    const easyWorkLocked = true;

    // AHV-Nr-Prüfziffer-Check: blockt Save wenn AHV-Nr eingegeben aber ungültig.
    const ahvInput = document.getElementById('ef-ahvNummer');
    if (!easyWorkLocked && ahvInput && ahvInput.value.trim()) {
        const ahvCheck = validateAhvNummer(ahvInput.value);
        if (ahvCheck.valid === false) {
            alert('AHV-Nr. ist nicht gültig:\n' + ahvCheck.error
                + '\n\nBitte korrigieren oder Feld leer lassen.');
            ahvInput.focus();
            return;
        }
        if (ahvCheck.valid === true) {
            // In normalisiertem Format speichern (756.XXXX.XXXX.XX)
            ahvInput.value = ahvCheck.normalized;
        }
    }

    // ── Employee Stammdaten ──────────────────────────────────────────────
    // Walter-Vorgabe 01.06.2026: harte Validierung Telefon/E-Mail/PLZ vor dem Save.
    const _phoneRaw = easyWorkLocked ? (emp.phoneMobile || '') : (document.getElementById('ef-phone')?.value || '');
    const _phoneFmt = _phoneRaw ? (easyWorkLocked ? _phoneRaw : window.formatPhoneIntl(_phoneRaw)) : '';
    if (!easyWorkLocked && _phoneRaw && !/^\+\d{2}\s\d{2}\s\d{3}\s\d{2}\s\d{2}$/.test(_phoneFmt)) {
        alert('Telefon-Format ungültig (erwartet +99 99 999 99 99, z.B. +41 79 409 43 33).');
        return;
    }
    if (!easyWorkLocked && _phoneFmt) document.getElementById('ef-phone').value = _phoneFmt;
    const _emailRaw = easyWorkLocked ? (emp.email || '') : (document.getElementById('ef-email')?.value || '').trim();
    if (!easyWorkLocked && _emailRaw && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(_emailRaw)) {
        alert('E-Mail-Adresse ist ungültig.');
        return;
    }
    const _zipRaw = easyWorkLocked ? (emp.zipCode || '') : (document.getElementById('ef-zip')?.value || '').trim();
    if (!easyWorkLocked && _zipRaw && !/^\d{4}$/.test(_zipRaw)) {
        alert('PLZ muss 4-stellig numerisch sein.');
        return;
    }
    // Übersicht (ov-*) ist die aktive Edit-Quelle; ef-* nur noch Legacy
    // (buildEmpEditPersonal, nicht mehr verdrahtet).
    const formVal = (efId, ovId) => {
        const ef = document.getElementById(efId);
        if (ef) return ef.value;
        const ov = ovId ? document.getElementById(ovId) : null;
        return ov ? ov.value : '';
    };
    const formEl = (efId, ovId) => document.getElementById(efId) || (ovId ? document.getElementById(ovId) : null);

    const _phone2Raw = formVal('ef-phone2', 'ov-phone2');
    const _phone2Fmt = _phone2Raw ? window.formatPhoneIntl(_phone2Raw) : '';
    if (_phone2Raw && !/^\+\d{2}\s\d{2}\s\d{3}\s\d{2}\s\d{2}$/.test(_phone2Fmt)) {
        alert('Telefon 2-Format ungültig (erwartet +99 99 999 99 99, z.B. +41 79 409 43 33).');
        return;
    }
    const phone2El = formEl('ef-phone2', 'ov-phone2');
    if (_phone2Fmt && phone2El) phone2El.value = _phone2Fmt;

    const exitVal = easyWorkLocked ? toDateInput(emp.exitDate) : document.getElementById('ef-exit')?.value;
    const isActiveInput = document.getElementById('ef-isactive');
    const boolVal = (efId, ovId, fallback) => {
        const el = formEl(efId, ovId);
        if (!el) return !!fallback;
        if (el.type === 'checkbox') return el.checked === true;
        return el.value === 'true';
    };
    const permitTypeEl = document.getElementById('ef-permitType');
    const permitExpiryEl = document.getElementById('ef-permitExpiry');
    const zemisEl = formEl('ef-zemisNumber', 'ov-zemisNumber');
    const placeOfOriginEl = document.getElementById('ef-placeOfOrigin');
    const empPayload = {
        firstName:    easyWorkLocked ? (emp.firstName || null) : (document.getElementById('ef-firstName')?.value || null),
        lastName:     easyWorkLocked ? (emp.lastName || null) : (document.getElementById('ef-lastName')?.value || null),
        salutation:   easyWorkLocked ? (emp.salutation || null) : (document.getElementById('ef-salutation')?.value || null),
        gender:       easyWorkLocked ? (emp.gender || null) : (document.getElementById('ef-gender')?.value || null),
        dateOfBirth:  easyWorkLocked ? (toDateInput(emp.dateOfBirth) || null) : (document.getElementById('ef-dob')?.value || null),
        languageCode: easyWorkLocked ? (emp.languageCode || null) : (document.getElementById('ef-lang')?.value || null),
        phoneMobile:  easyWorkLocked ? (emp.phoneMobile || null) : (_phoneFmt || null),
        phone2:       _phone2Fmt || null,
        email:        easyWorkLocked ? (emp.email || null) : (_emailRaw || null),
        street:       easyWorkLocked ? (emp.street || null) : (document.getElementById('ef-street')?.value || null),
        zipCode:      _zipRaw || null,
        city:         easyWorkLocked ? (emp.city || null) : (stripCityCantonSuffix(document.getElementById('ef-city')?.value) || null),
        country:      easyWorkLocked ? (emp.country || null) : (document.getElementById('ef-country')?.value || null),
        cantonCode:   easyWorkLocked ? (emp.cantonCode || null) : (document.getElementById('ef-canton')?.value || null),
        permitTypeId: permitTypeEl ? (parseInt(permitTypeEl.value) || 0) : (emp.permitTypeId || 0),
        permitExpiryDate: permitExpiryEl ? (permitExpiryEl.value || null) : (toDateInput(emp.permitExpiryDate) || null),
        nationalityId: easyWorkLocked ? (emp.nationalityId || null) : (parseInt(document.getElementById('ef-nationalityId')?.value) || null),
        // ZEMIS: Backend-Konvention — null = «nicht ändern», '' = «löschen».
        // Feld gerendert → Wert (oder '' zum Löschen) senden; sonst null.
        zemisNumber:  zemisEl ? (zemisEl.value || '').trim() : null,
        entryDate:    easyWorkLocked ? (toDateInput(emp.entryDate) || null) : (document.getElementById('ef-entry')?.value || null),
        exitDateSet:  true,
        exitDate:     exitVal || null,
        // Kündigungs-Daten (Walter 16.07.2026) — kuendigungSet:true, damit das
        // Backend sie schreibt (leer = löschen, z.B. nach manuellem Rückzug).
        kuendigungSet: true,
        kuendigungAusgesprochenAm: formVal('ef-kuendAm', 'ov-kuendAm') || null,
        kuendigungPer:             formVal('ef-kuendPer', 'ov-kuendPer') || null,
        kuendigungDurch:           formVal('ef-kuendDurch', 'ov-kuendDurch') || null,
        austrittsgrund:            formVal('ef-austrittsgrund', 'ov-austrittsgrund') || null,
        // Walter-Vorgabe 18.05.2026: Aktiv-Flag bewusst gesetzt vom UI,
        // KEIN Auto-Sync mehr aus ExitDate (Backend nimmt diesen Wert 1:1).
        isActive:     isActiveInput ? isActiveInput.checked === true : !!emp.isActive,
        // Walter-Vorgabe 07.06.2026: Anstellungs-Booleans (Übersicht ov-*).
        lgavPflichtig:        boolVal('ef-lgavPflichtig', 'ov-lgavPflichtig', emp.lgavPflichtig),
        // < 8 h / Wo. gehört zum FLEX-Vertrag (Walter 31.07.2026) — hier nicht mehr schreiben.
        socialSecurityNumber: easyWorkLocked ? (emp.socialSecurityNumber || null) : (document.getElementById('ef-ahvNummer')?.value || null),
        ahvNummer:    easyWorkLocked ? (emp.socialSecurityNumber || null) : (document.getElementById('ef-ahvNummer')?.value || null),
        maidenName:   formVal('ef-maidenName', 'ov-maidenName') || null,
        // Kurzname = easy@work Nickname — nur anzeigen, beim Save nicht
        // ueberschreiben wenn kein Formularfeld (Uebersicht ist read-only).
        shortName:    formEl('ef-shortName', 'ov-shortName')
                        ? (formVal('ef-shortName', 'ov-shortName') || null)
                        : (emp.shortName || null),
        zivilstand:   easyWorkLocked ? ((emp.zivilstand ?? emp.maritalStatus) || null) : (document.getElementById('ef-zivilstand')?.value || null),
        maritalStatus:easyWorkLocked ? ((emp.zivilstand ?? emp.maritalStatus) || null) : (document.getElementById('ef-zivilstand')?.value || null),

        // Erweiterte Zivilstand-Angaben (allgemein, nicht QST-spezifisch).
        // QST-spezifische Felder (Konkubinat, gemeinsame elterliche Sorge,
        // Unterhalt, höheres Einkommen, Grenzgänger, Wochenaufenthalter)
        // werden im Modul Quellensteuer zeitlich versioniert gepflegt.
        maritalStatusSinceSet: true,
        maritalStatusSince:    formVal('ef-maritalStatusSince', 'ov-maritalStatusSince') || null,
        // separatedSince-Feld wurde aus dem UI entfernt (Walter: „Getrennt"
        // ist bereits ein Zivilstand, separates Datum überflüssig). Wir
        // senden separatedSinceSet=false, damit der Backend-Handler das
        // Feld unverändert lässt.
        separatedSinceSet:     false,
        separatedSince:        null,
        religion:              formVal('ef-religion', 'ov-religion') || null,
        letterSalutation:      (formVal('ef-letterSalutation', 'ov-letterSalutation') || '').trim() || null,
        placeOfOrigin:         placeOfOriginEl ? (placeOfOriginEl.value?.trim() || null) : (emp.placeOfOrigin || null),
    };
    const religionBefore = emp.religion || '';
    const religionAfter  = empPayload.religion || '';
    const religionChanged = religionBefore !== religionAfter;

    // "Kein Lohn"-Flag — nur senden wenn der Toggle im Formular existiert
    // (= aktueller User ist admin; sonst rendert das Feld nicht).
    // Walter-Vorgabe 13.06.2026: serverseitig auf admin-only beschränkt,
    // andere Rollen würden 403 PHANTOM_TOGGLE_ADMIN_ONLY bekommen.
    const payrollExclChk = document.getElementById('ef-isPayrollExcluded');
    if (payrollExclChk) {
        empPayload.isPayrollExcluded = payrollExclChk.checked;
    }

    try {
        // Walter-Vorgabe 07.06.2026: KEIN impliziter Permit-PUT mehr im
        // MA-Save. Bewilligungen werden ausschliesslich über das Permit-
        // Modal pro Listenzeile gepflegt — das verhindert strukturell die
        // früheren Overlap-Bugs, bei denen der MA-Save unbemerkt eine
        // Bewilligung mit-veränderte.

        // Nur Stammdaten speichern – Vertragsdaten werden im Modul Vertrag bearbeitet
        const requests = [
            fetch(`/api/employees/${selectedEmployeeId}`, {
                method: 'PUT',
                headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify(empPayload)
            })
        ];

        const results = await Promise.all(requests);
        const failed = results.find(r => !r.ok);
        if (failed) {
            // Walter-Bug 18.05.2026: bei 409 (z.B. MA_HAS_OPEN_PERIOD_DATA beim
            // Inaktiv-Setzen) bekommt der User jetzt die Klartext-Meldung statt
            // einem generischen „Fehler". Beim Re-Aktivieren des Häkchens muss
            // der User selber zuerst den Lohnlauf abschliessen.
            let msg = 'Fehler beim Speichern. Bitte erneut versuchen.';
            try {
                const json = await failed.clone().json();
                msg = json.message || json.error || msg;
            } catch {
                const txt = await failed.text().catch(() => '');
                if (txt) msg = txt;
            }
            alert(msg);
            return;
        }

        // Neu laden und in Anzeigemodus zurück
        const res = await fetch(`/api/employees/${selectedEmployeeId}`, { headers: ah() });
        if (res.ok) {
            selectedEmployee = await res.json();
            // Walter 14.06.2026: MA-Picker-Cache invalidieren — beim nächsten
            // Lookup-Open holen Posteingang/HR/Importer/etc. die frische Liste.
            if (typeof invalidateEmployeeLookupCache === 'function') invalidateEmployeeLookupCache();
            // Liste aktualisieren (Name könnte sich geändert haben)
            const idx = allEmployees.findIndex(e => e.id === selectedEmployeeId);
            if (idx >= 0) {
                allEmployees[idx] = { ...allEmployees[idx], ...selectedEmployee };
                allEmployees.sort((a, b) => {
                    const na = ((a.firstName ?? '') + ' ' + (a.lastName ?? '')).trim().toLowerCase();
                    const nb = ((b.firstName ?? '') + ' ' + (b.lastName ?? '')).trim().toLowerCase();
                    return na.localeCompare(nb, 'de');
                });
                const q = document.getElementById('empSearch')?.value ?? '';
                const list = q ? allEmployees.filter(e => {
                    const n = ((e.firstName??'')+(e.lastName??'')).toLowerCase();
                    return n.includes(q.toLowerCase()) || (e.employeeNumber??'').includes(q);
                }) : allEmployees;
                renderEmployeeList(list);
            }
            renderEmployeeDetail(selectedEmployee);

            // Konfession geändert → QST Kirchensteuer wurde serverseitig
            // nachgezogen (Walter 01.08.2026). Kurz zurückmelden + Lohn neu laden.
            if (religionChanged) {
                try {
                    const qr = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer/current`, { headers: ah() });
                    if (qr.ok) {
                        const qst = await qr.json();
                        if (qst && qst.qstCode) {
                            const kirche = qst.kirchensteuer ? 'mit Kirchensteuer' : 'ohne Kirchensteuer';
                            _showQstSyncToast(`QST nachgezogen: ${qst.qstCode} · ${kirche}`);
                        }
                    }
                } catch { /* toast best-effort */ }
                if (typeof reloadLohnAfterQstChange === 'function') {
                    reloadLohnAfterQstChange(selectedEmployeeId);
                }
            }
            // QST-Prüfroutine (Walter 23.08.2026): Zivilstand/Konfession
            // können den Tarif kippen — Auffälligkeiten sofort melden.
            qstRecheckNachAenderung(selectedEmployeeId);
        }
    } catch {
        alert('Verbindungsfehler beim Speichern.');
    }
}

function parseFloatOrNull(val) {
    if (val === '' || val === null || val === undefined) return null;
    const n = parseFloat(val);
    return isNaN(n) ? null : n;
}

// ── QST-Prüfroutine nach Stammdaten-Änderungen (Walter-Vorgabe 23.08.2026) ──
// Läuft nach JEDEM Speichern der MA-Stammdaten (Zivilstand/Konfession) und
// nach Anlegen/Ändern/Löschen von Familienmitgliedern (Ehepartner/Kind).
// Holt die zentrale Prüfung (/qst-pflicht) und zeigt bei Auffälligkeiten
// einen deutlichen Dialog mit Sprung in den QST-Tab — z.B. «Tarif A, aber
// Kind im Haushalt → H1 prüfen» nach dem Löschen des Ehepartners.
async function qstRecheckNachAenderung(empId) {
    if (!empId) return;
    try {
        const r = await fetch(`/api/employees/${empId}/qst-pflicht`, { headers: ah() });
        if (!r.ok) return;
        const j = await r.json();
        const hinweise = [];
        if (j.isPflichtOffen) hinweise.push(j.message || 'QST-Pflicht offen — Erfassung fehlt.');
        if (Array.isArray(j.partnerDatenMaengel)) hinweise.push(...j.partnerDatenMaengel);
        if (Array.isArray(j.tarifWarnungen))      hinweise.push(...j.tarifWarnungen);
        if (!hinweise.length) return;
        const msg = 'Die Änderung betrifft die Quellensteuer:\n\n• '
                  + hinweise.slice(0, 3).join('\n\n• ')
                  + (hinweise.length > 3 ? `\n\n… und ${hinweise.length - 3} weitere Hinweise.` : '');
        const go = await liquidConfirm(msg, {
            title: 'QST prüfen', yesLabel: 'QST-Tab öffnen', noLabel: 'Später' });
        if (go && typeof switchEmpTab === 'function') switchEmpTab('quellensteuer');
    } catch (_) { /* Prüfung ist Komfort — nie den Speicher-Fluss stören */ }
}

// ══════════════════════════════════════════════
// FAMILIE MODAL
// ══════════════════════════════════════════════

let editingFamilyMemberId = null;

function openFamilyModal(member) {
    editingFamilyMemberId = member ? member.id : null;

    document.getElementById('familyModalTitle').textContent =
        member ? _t('fam.modalTitleEdit','Familienangehöriger bearbeiten')
               : _t('fam.modalTitleNew','Familienangehörigen hinzufügen');

    // Statische Strings die wir per data-i18n im Modal markiert haben jetzt
    // einmal anwenden — das Modal wurde via .style.display=flex „geöffnet",
    // i18n.applyAll() rendert alle data-i18n-Elemente in die aktuelle Sprache.
    if (window.i18n && window.i18n.applyAll) window.i18n.applyAll(document.getElementById('familyModal'));

    // Felder befüllen oder leeren
    document.getElementById('fmMemberType').value      = member?.memberType         ?? 'Kind';
    document.getElementById('fmGender').value          = member?.gender             ?? '';
    document.getElementById('fmFirstName').value       = member?.firstName          ?? '';
    // Walter-Vorgabe 28.05.2026: Bei NEU + Kind/Ehepartner den Nachnamen des MA
    // vorbefüllen — kann der User natürlich überschreiben. Bei bestehenden
    // Einträgen (Edit) den gespeicherten Wert übernehmen.
    const _maLast = (selectedEmployee?.lastName || '').trim();
    const _defaultType = member?.memberType ?? 'Kind';
    const _prefillLast = (!member && _maLast && (_defaultType === 'Kind' || _defaultType === 'Ehepartner'))
        ? _maLast : (member?.lastName ?? '');
    document.getElementById('fmLastName').value        = _prefillLast;
    document.getElementById('fmMaidenName').value      = member?.maidenName         ?? '';
    document.getElementById('fmDateOfBirth').value     = toDateInput(member?.dateOfBirth);
    updateFmAgeDisplay();
    document.getElementById('fmSocialSecurity').value  = member?.socialSecurityNumber ?? '';
    document.getElementById('fmPhone').value           = member?.phone ?? '';

    // Walter-Vorgabe 14.06.2026: NEUE Familienmitglieder (vor allem Kinder)
    // bekommen sinnvolle Defaults vom MA:
    //   • Lebt in Schweiz   → JA  (Schweizer Familienzulagen-Logik)
    //   • Nationalität      → wie MA (Mutter/Vater)
    //   • Bewilligung       → wie MA (Mutter/Vater)
    //   • QST ab/bis        → Geburtsdatum bis 18. Geburtstag (siehe fmAutoQstFromDob)
    // Bei bestehenden Einträgen wird der gespeicherte Wert übernommen.
    const _isNewMember = !member;
    document.getElementById('fmLivesInSwitzerland').checked =
        _isNewMember ? true : (member?.livesInSwitzerland ?? false);
    document.getElementById('fmQstFrom').value         = toDateInput(member?.qstDeductibleFrom);
    document.getElementById('fmQstUntil').value        = toDateInput(member?.qstDeductibleUntil);

    // Walter-Vorgabe 20.08.2026: QST-Relevanz-Felder — Ehepartner-Erwerb +
    // Kind-Erstausbildung (+ typ-abhängige Sichtbarkeit).
    fmSetErwerb(member?.erwerbstaetig ?? null);
    document.getElementById('fmArbeitgeberName').value    = member?.arbeitgeberName    ?? '';
    document.getElementById('fmArbeitgeberStrasse').value = member?.arbeitgeberStrasse ?? '';
    document.getElementById('fmArbeitgeberPlz').value     = member?.arbeitgeberPlz     ?? '';
    document.getElementById('fmArbeitgeberOrt').value     = member?.arbeitgeberOrt     ?? '';
    document.getElementById('fmArbeitgeberKanton').value  = member?.arbeitgeberKanton  ?? '';
    document.getElementById('fmStellenantritt').value     = toDateInput(member?.stellenantritt);
    const agHint = document.getElementById('fmAgPlzHint');
    if (agHint) agHint.innerHTML = '';
    const erstCb = document.getElementById('fmInErstausbildung');
    if (erstCb) erstCb.checked = member?.inErstausbildung ?? false;
    fmQstBlocksVisibility(member?.memberType ?? 'Kind');

    // ── Aufenthalt + Nationalität: Permit-Types + Nationalitäten füllen
    //    (gleiche Listen wie beim MA-Edit-Modal). Vorausgewählt wird der
    //    bestehende Wert; bei NEU der Wert vom MA als Default.
    const _defaultPermitTypeId = _isNewMember
        ? (selectedEmployee?.permitTypeId ?? selectedEmployee?.permitType?.id ?? null)
        : (member?.permitTypeId ?? null);
    const _defaultNationalityId = _isNewMember
        ? (selectedEmployee?.nationalityId ?? null)
        : (member?.nationalityId ?? null);
    fmFillPermitAndNationalitySelects(_defaultPermitTypeId, _defaultNationalityId);
    document.getElementById('fmPermitExpiry').value = toDateInput(member?.permitExpiryDate);
    document.getElementById('fmZemisNumber').value  = member?.zemisNumber ?? '';

    // Walter-Vorgabe 07.06.2026: „Dokument hochladen"-Button NUR beim
    // Ehepartner sichtbar — klickt auf den normalen Standard-Upload-Dialog
    // (openDokUploadModal), wo Walter Kategorie + Typ frei wählt
    // (typisch: „Persönliche Angaben / Ehegatten Dokumente"). Damit ist
    // der Mechanismus unempfindlich gegen Änderungen in der Dokument-Struktur.
    const docUploadBtn = document.getElementById('fmSpouseDocBtn');
    const isSpouse = (member?.memberType ?? document.getElementById('fmType')?.value) === 'Ehepartner';
    if (docUploadBtn) {
        docUploadBtn.style.display = isSpouse ? 'inline-flex' : 'none';
    }

    // ── Adresse: Radio-Modus + Dropdown der MA-Zusatzadressen befüllen
    fmRefreshAddressUi(member?.alternativeAddressId ?? null);

    // Zulagen-Block ist seit 18.05.2026 aus dem Modal entfernt (Walter:
    // Zulagen leben jetzt INLINE pro Kind in der Familie-Tab). Der
    // versteckte Mount-Point #fmAllowanceList bleibt für Backwards-Compat,
    // wird aber nicht mehr aktiv befüllt. Null-Checks aufs Add-Button-
    // Element, weil das DOM-Element nicht mehr existiert.
    const allowanceList = document.getElementById('fmAllowanceList');
    const allowanceAddBtn = document.getElementById('fmAllowanceAddBtn');
    if (member?.id && allowanceList) {
        // Nur laden falls jemand das Element via DevTools sichtbar gemacht hat
        // (kein normaler User-Pfad).
        loadFamilyAllowances(member.id);
    }
    if (allowanceAddBtn) allowanceAddBtn.style.display = member?.id ? 'inline-block' : 'none';

    document.getElementById('familyModal').style.display = 'flex';
    // Ausweis des Ehepartners daneben anzeigen (Walter-Vorgabe 12.07.2026):
    // beim Erfassen soll der geöffnete Scan sichtbar BLEIBEN — gleiche
    // Panel-Mechanik wie im Bewilligungs-Modal.
    famLoadSpouseDocs(member);
}

// ── Ehegatten-Ausweis-Panel neben dem Familien-Modal (Walter 12.07.2026) ──
// Zeigt die Ausweis-Dokumente aus ZWEI Quellen: das DIREKT am Familienmitglied
// verknüpfte Doku (member.dokumentId — der grüne «Doku verknüpft»-Badge) und
// alle spouse-getypten Dokumente des MA; bei mehreren mit Auswahl.
// Kein Dokument → Panel bleibt unsichtbar (kein Lärm bei Kindern etc.).
async function famLoadSpouseDocs(member) {
    const modal = document.getElementById('familyModal');
    if (!modal || !selectedEmployeeId) return;
    let panel = document.getElementById('fam-docpanel');
    if (!panel) {
        modal.style.gap = '14px';
        panel = document.createElement('div');
        panel.id = 'fam-docpanel';
        panel.style.cssText = 'display:none;background:#fff;border-radius:14px;flex:1;min-width:380px;max-width:44vw;max-height:92vh;padding:14px;flex-direction:column;gap:8px;box-shadow:0 24px 48px rgba(0,0,0,0.25)';
        panel.innerHTML = `
            <div style="display:flex;align-items:center;justify-content:space-between;gap:8px">
                <div id="fam-docname" style="font-size:12.5px;font-weight:700;color:#3f3f3f;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;min-width:0"></div>
                <button id="fam-doczoom" style="display:none;background:rgba(255,255,255,0.6);border:1px solid #e2ddd3;color:#3f3f3f;border-radius:10px;padding:6px 13px;font-size:12.5px;font-weight:600;cursor:pointer;flex-shrink:0" title="Im grossen Vorschaufenster öffnen (mit Drucken/Zoom)">⤢ Vergrössern</button>
            </div>
            <div id="fam-docview" style="flex:1;min-height:320px;overflow:auto;display:flex;align-items:flex-start;justify-content:center;background:#f6f3ee;border-radius:10px"></div>`;
        modal.appendChild(panel);
    }
    panel.style.display = 'none';
    try {
        // ── Quellen sammeln (Walter-Vorgabe 20.08.2026, «beim Erfassen des
        // Ehemanns ein Dokument daneben anzeigen»): nicht mehr NUR die
        // spouse-getypten Dokus — zusätzlich ALLE Dokumente des MA und die
        // Postfach-Eingänge (dort landet die frisch geschickte Ausweiskopie/
        // Heiratsurkunde meistens zuerst). Reihenfolge im Dropdown:
        // verknüpftes Doku → spouse-Dokus → Postfach (neueste zuerst) →
        // übrige Doku-Tab-Dokumente.
        const entries = [];
        const seenDoc = new Set();
        const pushDoc = (d, prefix) => {
            if (!d || seenDoc.has('d' + d.id)) return;
            seenDoc.add('d' + d.id);
            entries.push({
                previewUrl: `/api/documents/preview/${d.id}`,
                filename:   d.filenameOriginal || 'dokument',
                label:      `${prefix}${(d.bemerkung || '').trim() || d.filenameOriginal || 'Dokument'}`
                          + (d.geaendertAm || d.hochgeladenAm ? ' · ' + formatDate(d.geaendertAm || d.hochgeladenAm) : '')
            });
        };

        const r = await fetch(`/api/documents/by-field?employeeId=${selectedEmployeeId}&code=spouse&all=true`, { headers: ah() });
        let spouseDocs = r.ok ? await r.json() : [];
        if (!Array.isArray(spouseDocs)) spouseDocs = [];

        let alleDocs = [];
        try {
            const rd = await fetch(`/api/documents/by-employee/${selectedEmployeeId}`, { headers: ah() });
            if (rd.ok) alleDocs = await rd.json() || [];
        } catch (_) { /* best-effort */ }

        // 1) explizit verknüpftes Doku zuoberst
        if (member?.dokumentId) {
            const dm = spouseDocs.find(d => d.id === member.dokumentId)
                    || (alleDocs || []).find(d => d.id === member.dokumentId);
            pushDoc(dm, '');
        }
        // 2) spouse-getypte Dokus
        spouseDocs.forEach(d => pushDoc(d, ''));
        // 3) BEWUSST KEINE Postfach-Eingänge (Walter-Vorgabe 20.08.2026):
        // nur beim MA ABGELEGTE Dokumente anzeigen — ein Doku im Postfach
        // muss zuerst beim MA abgelegt werden, sonst könnten falsche/fremde
        // Dokus erscheinen (Postfach kann Sammel-/Momentaufnahmen enthalten).
        // 4) übrige Dokumente aus dem Doku-Tab (neueste zuerst)
        (alleDocs || [])
            .sort((a, b) => String(b.geaendertAm || b.hochgeladenAm || '').localeCompare(String(a.geaendertAm || a.hochgeladenAm || '')))
            .slice(0, 25)
            .forEach(d => pushDoc(d, '📂 '));

        if (entries.length === 0) return;
        window._famDocs = entries;
        panel.style.display = 'flex';
        // Box schmaler machen, damit Panel + Erfassung nebeneinander passen
        // (CSS-Regel #familyModal.fam-docs-open, Walter 20.08.2026).
        modal.classList.add('fam-docs-open');
        const nameEl = document.getElementById('fam-docname');
        if (entries.length > 1) {
            nameEl.innerHTML = `<select onchange="famShowSpouseDoc(parseInt(this.value))"
                style="max-width:100%;background:rgba(255,255,255,0.7);border:1px solid #e2ddd3;border-radius:10px;padding:5px 9px;font-size:12.5px;font-weight:600;color:#3f3f3f;cursor:pointer">
                ${entries.map((d, i) => `<option value="${i}">${esc(d.label)}</option>`).join('')}
            </select>`;
        }
        await famShowSpouseDoc(0);
    } catch (_) { /* Panel bleibt unsichtbar */ }
}

async function famShowSpouseDoc(idx) {
    const doc = (window._famDocs || [])[idx];
    if (!doc) return;
    if ((window._famDocs || []).length <= 1) {
        const nameEl = document.getElementById('fam-docname');
        if (nameEl) nameEl.textContent = doc.label || 'Ausweis Ehepartner';
    }
    const zoomBtn = document.getElementById('fam-doczoom');
    if (zoomBtn) {
        zoomBtn.style.display = 'inline-flex';
        zoomBtn.onclick = () => previewUrlFetch(doc.previewUrl, doc.filename || 'ausweis', ah());
    }
    try {
        const pr = await fetch(doc.previewUrl, { headers: ah() });
        if (!pr.ok) return;
        const blob = await pr.blob();
        const url = URL.createObjectURL(blob);
        const view = document.getElementById('fam-docview');
        if ((blob.type || '').startsWith('image/')) {
            view.innerHTML = `<img src="${url}" style="max-width:100%;max-height:100%;object-fit:contain">`;
        } else {
            view.innerHTML = `<iframe src="${url}" style="width:100%;height:100%;border:none;min-height:480px"></iframe>`;
        }
    } catch (_) { /* Vorschau best-effort */ }
}

// Permit-Typen + Nationalitäten ins Familienmitglied-Modal laden.
// Selbe Datenquellen wie im MA-Edit-Modal — wir cachen via getPermitTypes()
// + getNationalities() um nicht bei jedem Modal-Open neu zu fetchen.
async function fmFillPermitAndNationalitySelects(currentPermitTypeId, currentNationalityId) {
    const permitSel = document.getElementById('fmPermitTypeId');
    const natSel    = document.getElementById('fmNationalityId');
    if (permitSel) {
        try {
            const types = await getPermitTypes();
            permitSel.innerHTML = '<option value="">– keine / CH-Bürger –</option>'
                + (types || [])
                    .filter(p => p.isActive !== false)
                    .map(p => {
                        const sel = (currentPermitTypeId == p.id) ? 'selected' : '';
                        return `<option value="${p.id}" ${sel}>${p.description ?? p.code}</option>`;
                    }).join('');
        } catch { /* ignore */ }
    }
    if (natSel) {
        try {
            const nats = await getNationalities();
            natSel.innerHTML = '<option value="">–</option>'
                + (nats || [])
                    .filter(n => n.isActive !== false)
                    .map(n => {
                        const id    = n.id   ?? n.Id;
                        const code  = n.code ?? n.Code ?? '';
                        const name  = n.name ?? n.Name ?? code;
                        const sel   = (currentNationalityId == id) ? 'selected' : '';
                        // Ausweis-Kürzel (alpha-3) mit anzeigen (Walter 12.07.2026):
                        // die Ausweise drucken BGR/MKD/… — so kann man direkt abtippen.
                        const codes = code ? (n.code3 ? `${code} / ${n.code3}` : code) : '';
                        return `<option value="${id}" ${sel}>${name}${codes && code !== name ? ' (' + codes + ')' : ''}</option>`;
                    }).join('');
            natMakeCombo(natSel);   // Such-Combobox statt nativem Dropdown (Walter 12.07.2026)
        } catch { /* ignore */ }
    }
}

// ── Orts-Vorwärtssuche (Walter 20.08.2026) ─────────────────────────────
// Wer die PLZ nicht kennt, tippt den Ortsnamen an («Reid» → Reiden LU …);
// die Auswahl füllt PLZ + Ort + Kanton. Wiederverwendbar: im HTML
// oninput="ortNameSuggest(this, '<plzId>', '<ortId>', '<kantonId>')".
let _ortSugTimer = null, _ortSugBox = null;
function ortSugClose() { if (_ortSugBox) { _ortSugBox.remove(); _ortSugBox = null; } }
function ortNameSuggest(inp, plzId, ortId, kantonId) {
    clearTimeout(_ortSugTimer);
    const q = (inp.value || '').trim();
    if (q.length < 2 || /^\d/.test(q)) { ortSugClose(); return; }
    _ortSugTimer = setTimeout(async () => {
        try {
            const res = await fetch(`/api/swiss-locations/by-name?q=${encodeURIComponent(q)}`, { headers: ah() });
            if (!res.ok) return;
            const locs = await res.json();
            ortSugClose();
            if (!Array.isArray(locs) || !locs.length) return;
            const r = inp.getBoundingClientRect();
            const box = document.createElement('div');
            box.style.cssText = `position:fixed;left:${r.left}px;top:${r.bottom + 4}px;width:${Math.max(r.width, 260)}px;z-index:9000;background:#fff;border:1px solid #e2ddd3;border-radius:10px;box-shadow:0 12px 28px rgba(60,55,48,0.18);max-height:240px;overflow:auto`;
            box.innerHTML = locs.map((l, i) =>
                `<div data-i="${i}" style="padding:7px 12px;font-size:13px;cursor:pointer;color:#3f3f3f">${esc(l.plz4)} ${esc(l.ortschaftsname)} <span style="color:#8b8b8b">${esc(l.kantonskuerzel || '')}</span></div>`).join('');
            box.addEventListener('mousedown', e => {   // mousedown feuert VOR blur
                const t = e.target.closest('[data-i]');
                if (!t) return;
                const l = locs[parseInt(t.getAttribute('data-i'), 10)];
                const set = (id, v) => { const el = document.getElementById(id); if (el) el.value = v || ''; };
                set(plzId, l.plz4); set(ortId, l.ortschaftsname); set(kantonId, l.kantonskuerzel);
                ortSugClose();
                e.preventDefault();
            });
            document.body.appendChild(box);
            _ortSugBox = box;
            const closeOnBlur = () => { setTimeout(ortSugClose, 150); inp.removeEventListener('blur', closeOnBlur); };
            inp.addEventListener('blur', closeOnBlur);
        } catch (_) { /* best-effort */ }
    }, 250);
}

// ── Adresse-Auswahl im Familienmitglied-Modal ──────────────────────────
// Zeigt entweder "Lebt beim MA" (Hauptadresse) oder "Andere Adresse"
// (Dropdown der employee_address des MA). Dropdown wird live aus dem
// Backend befüllt — beim Speichern legen wir alternativeAddressId mit.
async function fmRefreshAddressUi(currentAlternativeAddressId) {
    const sameRadio = document.getElementById('fmAddrSameAsEmp');
    const altRadio  = document.getElementById('fmAddrAlt');
    const summaryEl = document.getElementById('fmAddrEmpSummary');
    const altBox    = document.getElementById('fmAddrAltBox');
    const select    = document.getElementById('fmAlternativeAddressId');
    const hintEl    = document.getElementById('fmAddrAltHint');

    // Hauptadresse-Zusammenfassung anzeigen (vom geladenen MA-Datensatz)
    const emp = selectedEmployee;
    if (summaryEl && emp) {
        const parts = [
            emp.street,
            [emp.zipCode, stripCityCantonSuffix(emp.city)].filter(Boolean).join(' '),
            emp.country && emp.country.toLowerCase() !== 'schweiz' ? emp.country : null,
        ].filter(Boolean);
        summaryEl.textContent = parts.length ? parts.join(', ') : '— keine Hauptadresse erfasst —';
    }

    // Zusatzadressen des MA laden und Dropdown füllen
    if (select && selectedEmployeeId) {
        select.innerHTML = '<option value="">— Adresse wählen —</option>';
        try {
            const res = await fetch(`/api/employees/${selectedEmployeeId}/addresses`, { headers: ah() });
            if (res.ok) {
                const list = await res.json();
                list.forEach(a => {
                    const opt = document.createElement('option');
                    opt.value = a.id;
                    const summary = [
                        a.description || null,
                        [a.street, a.houseNumber].filter(Boolean).join(' '),
                        [a.zipCode, stripCityCantonSuffix(a.city)].filter(Boolean).join(' '),
                        a.country && a.country.toLowerCase() !== 'schweiz' ? a.country : null,
                    ].filter(Boolean).join(' · ');
                    opt.textContent = summary || `Adresse #${a.id}`;
                    select.appendChild(opt);
                });
                if (hintEl) {
                    hintEl.textContent = list.length === 0
                        ? 'Noch keine Zusatzadressen beim Mitarbeiter — über «+ Neue Zusatzadresse» eine anlegen.'
                        : '';
                }
            } else if (hintEl) {
                hintEl.textContent = '';
            }
        } catch { /* still */ }
    }

    // «Lebt beim Ehepartner» (Walter 21.08.2026): nur anbieten, wenn ein
    // Ehepartner mit ANDERER Adresse existiert und nicht gerade der
    // Ehepartner selbst bearbeitet wird — übernimmt dessen Zusatzadresse
    // (wichtig für QST/Halbfamilie: Kind nicht im Haushalt des MA).
    const spouseRow  = document.getElementById('fmAddrSpouseRow');
    const spouseRad  = document.getElementById('fmAddrSpouse');
    const spouseSum  = document.getElementById('fmAddrSpouseSummary');
    const editiertTyp = document.getElementById('fmMemberType')?.value;
    const spouse = (window._familyMembersCache || []).find(m =>
        m.memberType === 'Ehepartner' && m.alternativeAddressId
        && m.id !== editingFamilyMemberId);
    window._fmSpouseAltAddrId = (editiertTyp !== 'Ehepartner' && spouse)
        ? spouse.alternativeAddressId : null;
    if (spouseRow) {
        spouseRow.style.display = window._fmSpouseAltAddrId ? '' : 'none';
        if (spouseSum && spouse) {
            const a = spouse.alternativeAddress;
            spouseSum.textContent = a
                ? [ [a.street].filter(Boolean).join(' '),
                    [a.zipCode, stripCityCantonSuffix(a.city)].filter(Boolean).join(' ') ]
                    .filter(Boolean).join(', ')
                : `${spouse.firstName ?? ''} ${spouse.lastName ?? ''}`.trim();
        }
    }

    // Initial-Modus setzen
    const useSpouse = !!(window._fmSpouseAltAddrId && currentAlternativeAddressId
        && Number(currentAlternativeAddressId) === Number(window._fmSpouseAltAddrId));
    const useAlt = !!currentAlternativeAddressId && !useSpouse;
    if (sameRadio && altRadio) {
        sameRadio.checked = !useAlt && !useSpouse;
        altRadio.checked  = useAlt;
    }
    if (spouseRad) spouseRad.checked = useSpouse;
    if (altBox) altBox.style.display = useAlt ? 'block' : 'none';
    if (select && useAlt) select.value = String(currentAlternativeAddressId);
}

function fmAddrModeChanged() {
    const useAlt = document.getElementById('fmAddrAlt')?.checked;
    const altBox = document.getElementById('fmAddrAltBox');
    if (altBox) altBox.style.display = useAlt ? 'block' : 'none';
    if (!useAlt) {
        // Auswahl beim Wechsel zurück auf "Hauptadresse" zurücksetzen,
        // damit kein verwaister AlternativeAddressId mitgespeichert wird.
        const sel = document.getElementById('fmAlternativeAddressId');
        if (sel) sel.value = '';
    }
}

// "+ Neue Zusatzadresse" im Familie-Modal: öffnet das bestehende
// Mitarbeiter-Adress-Modal. Nach Speichern dort wird die Liste hier
// neu geladen und die neue Adresse als Default ausgewählt.
function openEmployeeAddressModalFromFamily() {
    if (!selectedEmployeeId) {
        alert('Mitarbeiter zuerst laden.');
        return;
    }
    // Hook: nach erfolgreichem Speichern im Adress-Modal → unsere Liste auffrischen
    window._fmReloadAddressesAfterSave = true;
    openEmployeeAddressModal(null);
}

function closeFamilyModal() {
    const modal = document.getElementById('familyModal');
    modal.style.display = 'none';
    modal.classList.remove('fam-docs-open');
    editingFamilyMemberId = null;
}

async function saveFamilyMember() {
    if (!selectedEmployeeId) return;

    // Adress-Modus: "alt" = abweichende Adresse aus den Zusatzadressen des MA;
    // "spouse" (Walter 21.08.2026) = Zusatzadresse des Ehepartners übernehmen.
    // Wenn "same" oder leeres Dropdown → AlternativeAddressId = null
    const addrUseSpouse = document.getElementById('fmAddrSpouse')?.checked && window._fmSpouseAltAddrId;
    const addrUseAlt = document.getElementById('fmAddrAlt')?.checked;
    const addrSelVal = document.getElementById('fmAlternativeAddressId')?.value || '';
    const alternativeAddressId = addrUseSpouse
        ? Number(window._fmSpouseAltAddrId)
        : (addrUseAlt && addrSelVal) ? parseInt(addrSelVal, 10) : null;

    // Aufenthalt + Nationalität: leer = null. Permit-Type-Id und
    // Nationality-Id sind FK in der DB.
    const permitTypeId   = parseInt(document.getElementById('fmPermitTypeId').value, 10);
    const nationalityId  = parseInt(document.getElementById('fmNationalityId').value, 10);

    // Telefon wie beim MA normalisieren (+41 79 333 44 55).
    const phoneRaw = (document.getElementById('fmPhone')?.value || '').trim();
    const phoneFmt = phoneRaw ? window.formatPhoneIntl(phoneRaw) : '';
    if (phoneRaw && !/^\+\d{2}\s\d{2}\s\d{3}\s\d{2}\s\d{2}$/.test(phoneFmt)) {
        alert('Telefon-Format ungültig (erwartet +99 99 999 99 99, z.B. +41 79 409 43 33).');
        document.getElementById('fmPhone')?.focus();
        return;
    }
    if (phoneFmt) document.getElementById('fmPhone').value = phoneFmt;

    const payload = {
        memberType:             document.getElementById('fmMemberType').value         || 'Kind',
        gender:                 document.getElementById('fmGender').value             || null,
        firstName:              document.getElementById('fmFirstName').value          || null,
        lastName:               document.getElementById('fmLastName').value           || null,
        maidenName:             document.getElementById('fmMaidenName').value         || null,
        dateOfBirth:            document.getElementById('fmDateOfBirth').value        || null,
        socialSecurityNumber:   document.getElementById('fmSocialSecurity').value     || null,
        phone:                  phoneFmt || null,
        livesInSwitzerland:     document.getElementById('fmLivesInSwitzerland').checked,
        alternativeAddressId,
        qstDeductibleFrom:      document.getElementById('fmQstFrom').value            || null,
        qstDeductibleUntil:     document.getElementById('fmQstUntil').value           || null,
        permitTypeId:           Number.isFinite(permitTypeId) && permitTypeId > 0 ? permitTypeId : null,
        permitExpiryDate:       document.getElementById('fmPermitExpiry').value       || null,
        zemisNumber:            (document.getElementById('fmZemisNumber').value || '').trim() || null,
        nationalityId:          Number.isFinite(nationalityId) && nationalityId > 0 ? nationalityId : null,
        // Walter-Vorgabe 20.08.2026: QST-Relevanz-Felder.
        erwerbstaetig:          fmGetErwerb(),
        arbeitgeberName:        (document.getElementById('fmArbeitgeberName')?.value    || '').trim() || null,
        arbeitgeberStrasse:     (document.getElementById('fmArbeitgeberStrasse')?.value || '').trim() || null,
        arbeitgeberPlz:         (document.getElementById('fmArbeitgeberPlz')?.value     || '').trim() || null,
        arbeitgeberOrt:         (document.getElementById('fmArbeitgeberOrt')?.value     || '').trim() || null,
        arbeitgeberKanton:      (document.getElementById('fmArbeitgeberKanton')?.value  || '').trim().toUpperCase() || null,
        stellenantritt:         document.getElementById('fmStellenantritt')?.value      || null,
        inErstausbildung:       document.getElementById('fmInErstausbildung')?.checked ?? false,
        // Zulagen werden separat über /api/family-members/{id}/allowances verwaltet.
    };

    const isEdit = editingFamilyMemberId !== null;
    const url    = isEdit
        ? `/api/employees/${selectedEmployeeId}/family/${editingFamilyMemberId}`
        : `/api/employees/${selectedEmployeeId}/family`;
    const method = isEdit ? 'PUT' : 'POST';

    try {
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (!res.ok) {
            alert('Fehler beim Speichern.');
            return;
        }
        closeFamilyModal();
        loadFamilieTab(selectedEmployeeId);
        // QST-Prüfroutine (Walter 23.08.2026): Ehepartner/Kind-Änderungen
        // können Tarif + Kinderziffer kippen — sofort melden.
        qstRecheckNachAenderung(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

async function deleteFamilyMember(id) {
    if (!(await liquidConfirm('Diesen Eintrag wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/family/${id}`, {
            method: 'DELETE',
            headers: ah()
        });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadFamilieTab(selectedEmployeeId);
        // QST-Prüfroutine (Walter 23.08.2026): z.B. Ehemann gelöscht
        // (Scheidung) → Tarif B/C stimmt nicht mehr, oder H-Ziffer ändert.
        qstRecheckNachAenderung(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// ══════════════════════════════════════════════════════════════════
// FAMILIENZULAGEN (versioniert pro Familienmitglied)
// ══════════════════════════════════════════════════════════════════

const _ALLOWANCE_TYPE_LABEL = {
    KZ: 'Kinderzulage', AZ: 'Ausbildungszulage',
    GZ: 'Geburtszulage', AdoptZ: 'Adoptionszulage',
    HZ: 'Haushaltszulage', SONSTIGE: 'Sonstige'
};

async function loadFamilyAllowances(familyMemberId) {
    const el = document.getElementById('fmAllowanceList');
    if (!el) return;
    el.innerHTML = '<span style="color:#94a3b8">Wird geladen…</span>';
    try {
        const res = await fetch(`/api/family-members/${familyMemberId}/allowances`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<span style="color:#dc2626">Fehler beim Laden.</span>'; return; }
        const list = await res.json();
        if (!list.length) {
            el.innerHTML = '<span style="color:#94a3b8;font-style:italic">Keine Zulagen erfasst.</span>';
            return;
        }
        el.innerHTML = `
            <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                <thead>
                    <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0;color:#64748b;font-size:10.5px;letter-spacing:.04em">
                        <th style="padding:6px 8px;text-align:left;font-weight:600">VON</th>
                        <th style="padding:6px 8px;text-align:left;font-weight:600">BIS</th>
                        <th style="padding:6px 8px;text-align:right;font-weight:600">CHF/MT.</th>
                        <th style="padding:6px 8px;text-align:left;font-weight:600">ART</th>
                        <th style="padding:6px 8px;text-align:right;font-weight:600">AKT.</th>
                    </tr>
                </thead>
                <tbody>
                ${list.map(a => `
                    <tr style="border-bottom:1px solid #f1f5f9">
                        <td style="padding:6px 8px">${formatDate(a.validFrom)}</td>
                        <td style="padding:6px 8px">${a.validTo ? formatDate(a.validTo) : '<span style="color:#16a34a">offen</span>'}</td>
                        <td style="padding:6px 8px;text-align:right;font-family:ui-monospace,Menlo,Consolas,monospace">${Number(a.monthlyAmount).toFixed(2)}</td>
                        <td style="padding:6px 8px;color:#64748b">${a.allowanceType ? (a.allowanceType + ' — ' + (_ALLOWANCE_TYPE_LABEL[a.allowanceType] || '')) : '–'}</td>
                        <td style="padding:6px 8px;text-align:right">
                            <div class="dok-menu-wrap" style="display:inline-block">
                                <button class="dok-menu-btn" onclick="allowToggleMenu(event, ${a.id})" title="Aktionen">⋮</button>
                                <div class="dok-menu" id="allowMenu-${a.id}">
                                    <button class="dok-menu-item" onclick='openAllowanceModal(${JSON.stringify(a)})'>Bearbeiten</button>
                                </div>
                            </div>
                        </td>
                    </tr>
                `).join('')}
                </tbody>
            </table>`;
    } catch (e) {
        el.innerHTML = `<span style="color:#dc2626">Fehler: ${e.message}</span>`;
    }
}

// Walter 18.05.2026: direktes Öffnen der Zulagen-Erfassung aus der Familie-
// Liste — ohne über das Familienmember-Edit-Modal gehen zu müssen.
function openAllowanceFromCard(familyMemberId, existing) {
    editingFamilyMemberId = familyMemberId;
    openAllowanceModal(existing);
}

// Walter-Vorgabe 28.05.2026 (v3): User erfasst pro Kind den KONKRETEN Tarif-
// Satz (z.B. „KZ Satz 1") mit Gültig-ab/bis. Picker zeigt die im Tarif der
// Filiale verfügbaren Sätze. Engine schaut pro Lohnperiode: welcher Eintrag
// ist gültig + welcher Satz ist gewählt → holt aktuellen Wert aus Tarif.
// Cache der zuletzt geladenen Tarif-Optionen pro Stichtag.
let _alTarifOptionsByDate = { stichtag: '', list: [], tarifInfo: null };

async function openAllowanceModal(existing) {
    if (!editingFamilyMemberId) {
        alert('Bitte zuerst das Familienmitglied speichern.');
        return;
    }
    const d = (typeof existing === 'object' && existing !== null) ? existing : {};
    const isNew = !d.id;
    document.getElementById('alId').value            = d.id ?? '';
    document.getElementById('alValidFrom').value     = d.validFrom ?? '';
    document.getElementById('alValidTo').value       = d.validTo   ?? '';
    document.getElementById('alMonthlyAmount').value = (d.monthlyAmount ?? '0').toString();
    document.getElementById('alAllowanceType').value = d.allowanceType ?? '';
    document.getElementById('alTarifSatzNr').value   = (d.tarifSatzNr ?? '').toString();
    document.getElementById('alNote').value          = d.note ?? '';
    const errEl = document.getElementById('alError');
    if (errEl) { errEl.textContent = ''; errEl.style.color = '#dc2626'; }
    document.getElementById('alTarifHint').textContent = '';
    const lblInfo = document.getElementById('alTarifInfoLbl');
    if (lblInfo) lblInfo.textContent = '';
    document.getElementById('allowanceModalTitle').textContent = d.id ? 'Zulage bearbeiten' : 'Zulage hinzufügen';
    // Kind-Name sichtbar in der Maske (Walter 19.07.2026) — bei mehreren
    // Kindern im FAK-Entscheid sonst unklar, für wen erfasst wird.
    const child = (window._familyMembersCache || []).find(m => m.id === editingFamilyMemberId);
    const childName = child
        ? [child.firstName, child.lastName].filter(Boolean).join(' ').trim()
        : '';
    const childDob = child?.dateOfBirth ? formatDate(child.dateOfBirth) : '';
    const subEl = document.getElementById('allowanceModalSub');
    if (subEl) {
        if (childName) {
            subEl.textContent = childDob
                ? `${childName} · geb. ${childDob}`
                : childName;
            subEl.style.display = '';
        } else {
            subEl.textContent = '';
            subEl.style.display = 'none';
        }
    }
    document.getElementById('alDeleteBtn').style.display = d.id ? 'inline-block' : 'none';

    // Lohnlauf-Sperre (weich, wie QST): Gültig-ab nicht in definitiv
    // abgeschlossene Monate. FAK-Entscheid oft älter (z.B. 01.01.2025) —
    // für den offenen Definitiv-Monat auf FirstAllowed hochsetzen.
    const vfInp = document.getElementById('alValidFrom');
    if (window.lohnEditLock && vfInp && typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) {
        const state = await window.lohnEditLock.loadState(fixedCompanyProfileId, { mode: 'contracts' });
        window.lohnEditLock.applyToDateInput(vfInp, state);
        if (isNew && state.firstAllowedDate) {
            const orig = vfInp.value;
            if (!orig || orig < state.firstAllowedDate) {
                vfInp.value = state.firstAllowedDate;
                if (orig && orig < state.firstAllowedDate && errEl) {
                    errEl.style.color = '#92400e';
                    errEl.textContent =
                        `Gültig ab ${window.lohnEditLock.fmtDate(orig)} liegt in einer abgeschlossenen Lohnperiode — auf ${window.lohnEditLock.fmtDate(state.firstAllowedDate)} gesetzt (frühester offener Definitiv-Monat). Der FAK-Entscheid kann älter sein; für den offenen Lohn reicht das.`;
                }
            }
        }
    }

    document.getElementById('allowanceModal').style.display = 'flex';

    // ValidFrom-Wechsel → Optionen neu laden (Tarif-Version könnte abweichen).
    if (vfInp && !vfInp.dataset.alBound) {
        vfInp.addEventListener('change', () => alLoadTarifOptionsAndPreselect());
        vfInp.dataset.alBound = '1';
    }
    await Promise.all([
        alLoadTarifOptionsAndPreselect(),
        alLoadEntscheidDocs(d.dokumentId ?? null)
    ]);
}

/** FAK-/Entscheidungsdokumente aus dem MA-Dossier für den Zulage-Picker. */
async function alLoadEntscheidDocs(selectedDokId) {
    const sel = document.getElementById('alDokumentId');
    const panel = document.getElementById('al-docpanel');
    if (!sel) return;
    sel.innerHTML = '<option value="">– Dokumente laden … –</option>';
    if (panel) panel.style.display = 'none';
    window._alDocs = [];

    if (!selectedEmployeeId) {
        sel.innerHTML = '<option value="">– kein Mitarbeiter –</option>';
        return;
    }
    try {
        // Bevorzugt Typen mit linked_field_code=family_allowance, sonst alle
        // Dokus — FAK-Entscheide (Name enthält Kinderzulage/FAK/…) oben.
        let preferred = [];
        try {
            const rf = await fetch(
                `/api/documents/by-field?employeeId=${selectedEmployeeId}&code=family_allowance&all=true`,
                { headers: ah() });
            if (rf.ok) preferred = await rf.json();
            if (!Array.isArray(preferred)) preferred = [];
        } catch (_) { preferred = []; }

        const rAll = await fetch(`/api/documents/by-employee/${selectedEmployeeId}`, { headers: ah() });
        let alle = rAll.ok ? await rAll.json() : [];
        if (!Array.isArray(alle)) alle = [];

        const prefIds = new Set(preferred.map(d => d.id));
        const isFakLike = (d) => {
            const t = ((d.dokumentTypName || '') + ' ' + (d.kategorieName || '') + ' ' + (d.bemerkung || '')).toLowerCase();
            return /kinderzulage|familienzulage|ausbildungszulage|fak|entscheid|bescheid/.test(t)
                || prefIds.has(d.id);
        };
        const sorted = [...alle].sort((a, b) => {
            const af = isFakLike(a) ? 0 : 1;
            const bf = isFakLike(b) ? 0 : 1;
            if (af !== bf) return af - bf;
            return String(b.erstelltAm || b.hochgeladenAm || '')
                .localeCompare(String(a.erstelltAm || a.hochgeladenAm || ''));
        });
        window._alDocs = sorted;

        if (sorted.length === 0) {
            sel.innerHTML = '<option value="">– keine Dokumente im Dossier –</option>';
            return;
        }
        const selId = selectedDokId != null ? Number(selectedDokId) : null;
        sel.innerHTML = '<option value="">– kein Dokument –</option>' +
            sorted.map(d => {
                const typ = d.dokumentTypName ? `${d.dokumentTypName} · ` : '';
                const name = d.bemerkung || d.filenameOriginal || 'Dokument';
                const dt = d.erstelltAm || d.hochgeladenAm
                    ? ' · ' + formatDate(d.erstelltAm || d.hochgeladenAm) : '';
                const mark = isFakLike(d) ? '★ ' : '';
                return `<option value="${d.id}" ${selId === d.id ? 'selected' : ''}>${mark}${esc(typ + name)}${dt}</option>`;
            }).join('');
        if (selId && sorted.some(d => d.id === selId)) {
            await alShowEntscheidDoc(selId);
        }
    } catch (e) {
        sel.innerHTML = '<option value="">– Fehler beim Laden –</option>';
    }
}

/** Entscheidungsdokument neben dem Zulage-Modal als Info öffnen. */
async function alShowEntscheidDoc(docId) {
    const panel = document.getElementById('al-docpanel');
    const nameEl = document.getElementById('al-docname');
    const view = document.getElementById('al-docview');
    const zoomBtn = document.getElementById('al-doczoom');
    if (!panel || !view) return;
    if (!docId) {
        panel.style.display = 'none';
        view.innerHTML = '';
        if (nameEl) nameEl.textContent = '';
        if (zoomBtn) zoomBtn.style.display = 'none';
        return;
    }
    const doc = (window._alDocs || []).find(d => d.id === docId);
    if (nameEl) {
        nameEl.textContent = doc
            ? (doc.bemerkung || doc.filenameOriginal || 'Entscheidungsdokument')
            : 'Entscheidungsdokument';
    }
    panel.style.display = 'flex';
    view.innerHTML = '<div style="padding:24px;color:#8b8b8b;font-size:13px">Lade Vorschau…</div>';
    if (zoomBtn) {
        zoomBtn.style.display = 'inline-flex';
        zoomBtn.onclick = () => previewUrlFetch(
            `/api/documents/preview/${docId}`,
            (doc && (doc.bemerkung || doc.filenameOriginal)) || 'entscheid',
            ah());
    }
    try {
        const pr = await fetch(`/api/documents/preview/${docId}`, { headers: ah() });
        if (!pr.ok) {
            view.innerHTML = '<div style="padding:24px;color:#b91c1c;font-size:13px">Vorschau nicht verfügbar.</div>';
            return;
        }
        const blob = await pr.blob();
        const url = URL.createObjectURL(blob);
        if ((blob.type || '').startsWith('image/')) {
            view.innerHTML = `<img src="${url}" style="max-width:100%;max-height:100%;object-fit:contain">`;
        } else {
            view.innerHTML = `<iframe src="${url}" style="width:100%;height:100%;border:none;min-height:480px"></iframe>`;
        }
    } catch (_) {
        view.innerHTML = '<div style="padding:24px;color:#b91c1c;font-size:13px">Vorschau fehlgeschlagen.</div>';
    }
}

// Holt die verfügbaren Tarif-Sätze (KZ Satz 1/2, AZ Satz 1/2, GZ, AdoptZ —
// jeweils mit aktuellem Betrag) für die Filiale des MA am Stichtag = ValidFrom.
// Baut den Dropdown auf, wählt bei Edit den zuvor gespeicherten Satz wieder
// vor (Match nach allowanceType + tarifSatzNr).
async function alLoadTarifOptionsAndPreselect() {
    const pick = document.getElementById('alTarifPick');
    const hint = document.getElementById('alTarifHint');
    const lblInfo = document.getElementById('alTarifInfoLbl');
    if (!pick) return;

    const validFrom = document.getElementById('alValidFrom').value
                  || new Date().toISOString().slice(0,10);
    const cacheHit = _alTarifOptionsByDate.stichtag === validFrom;
    let data = cacheHit ? { options: _alTarifOptionsByDate.list, ..._alTarifOptionsByDate.tarifInfo }
                        : null;

    if (!data) {
        pick.innerHTML = '<option value="">– Tarif wird geladen … –</option>';
        try {
            const url = `/api/family-members/${editingFamilyMemberId}/allowances/tarif-options`
                      + `?effectiveDate=${encodeURIComponent(validFrom)}`;
            const res = await fetch(url, { headers: ah() });
            if (!res.ok) {
                pick.innerHTML = '<option value="">– Vorschau-Fehler –</option>';
                hint.innerHTML = '<span style="color:#dc2626">⚠ Tarif konnte nicht geladen werden.</span>';
                return;
            }
            data = await res.json();
            _alTarifOptionsByDate = {
                stichtag: validFrom,
                list: data.options || [],
                tarifInfo: {
                    kantonCode: data.kantonCode,
                    tarifValidFrom: data.tarifValidFrom,
                    tarifValidTo: data.tarifValidTo
                }
            };
        } catch {
            pick.innerHTML = '<option value="">– Verbindungsfehler –</option>';
            return;
        }
    }

    if (lblInfo) lblInfo.textContent = data.kantonCode ? `· Tarif Filiale: ${data.kantonCode}` : '';
    const opts = data.options || [];
    if (opts.length === 0) {
        pick.innerHTML = '<option value="">– Kein Tarif am Stichtag hinterlegt –</option>';
        hint.innerHTML = `<span style="color:#dc2626">⚠ Für die Filiale ist am ${formatDate(validFrom)} kein FAK-Tarif aktiv. Bitte in Systemeinstellungen → Familienzulagen-Tarife anlegen.</span>`;
        return;
    }

    // Vorauswahl bei Edit: zuerst (type + satz), sonst nur type, sonst nichts.
    const selType = document.getElementById('alAllowanceType').value;
    const selSatz = document.getElementById('alTarifSatzNr').value;
    const matchKey = (o) => `${o.allowanceType}|${o.tarifSatzNr == null ? '' : o.tarifSatzNr}`;
    const targetKey = `${selType}|${selSatz}`;
    let preselectIdx = opts.findIndex(o => matchKey(o) === targetKey);
    if (preselectIdx < 0 && selType) preselectIdx = opts.findIndex(o => o.allowanceType === selType);

    pick.innerHTML = '<option value="">– Bitte wählen –</option>' +
        opts.map((o, i) => {
            const amt = Number(o.amount).toFixed(2);
            return `<option value="${i}" ${i===preselectIdx?'selected':''}>${esc(o.label)} — ${amt} CHF/Mt</option>`;
        }).join('');

    if (preselectIdx >= 0) {
        alRefreshResolvePreview();
    } else {
        hint.innerHTML = '<span style="color:#94a3b8">Bitte einen Satz auswählen — der aktuell gültige Betrag erscheint dann hier.</span>';
    }
}

// Onchange-Handler des Pickers + ValidFrom: schreibt allowanceType + satzNr +
// aktueller Betrag in die hidden Inputs UND zeigt die Live-Vorschau.
async function alRefreshResolvePreview() {
    const pick = document.getElementById('alTarifPick');
    const hint = document.getElementById('alTarifHint');
    if (!pick || !hint) return;

    const opts = _alTarifOptionsByDate.list || [];
    const idx = parseInt(pick.value, 10);
    if (!Number.isFinite(idx) || idx < 0 || !opts[idx]) {
        // Nichts gewählt — hidden Felder leeren
        document.getElementById('alAllowanceType').value = '';
        document.getElementById('alTarifSatzNr').value   = '';
        document.getElementById('alMonthlyAmount').value = '0';
        hint.innerHTML = '<span style="color:#94a3b8">Bitte einen Satz auswählen.</span>';
        return;
    }
    const opt = opts[idx];
    const amt = Number(opt.amount).toFixed(2);
    document.getElementById('alAllowanceType').value = opt.allowanceType;
    document.getElementById('alTarifSatzNr').value   = opt.tarifSatzNr == null ? '' : String(opt.tarifSatzNr);
    document.getElementById('alMonthlyAmount').value = amt;

    const info = _alTarifOptionsByDate.tarifInfo || {};
    const gAb = info.tarifValidFrom ? formatDate(info.tarifValidFrom) : '–';
    const gBis = info.tarifValidTo ? formatDate(info.tarifValidTo) : 'offen';
    hint.innerHTML = `<span style="color:#16a34a">✓ <strong>${amt} CHF/Mt</strong></span> <span style="color:#64748b">· ${esc(opt.label)}, Tarif ${esc(info.kantonCode || '')} gültig ${gAb} – ${gBis}</span><br><span style="color:#94a3b8;font-size:10.5px">Der Lohnlauf holt den Betrag pro Periode aus dieser Systemtabelle — bei Tarif-Wechsel automatisch der neue Wert.</span>`;
}

function closeAllowanceModal() {
    document.getElementById('allowanceModal').style.display = 'none';
    const panel = document.getElementById('al-docpanel');
    if (panel) {
        panel.style.display = 'none';
        const view = document.getElementById('al-docview');
        if (view) view.innerHTML = '';
    }
}

async function saveAllowance() {
    const err = document.getElementById('alError');
    err.textContent = '';
    const id = document.getElementById('alId').value;
    const validFrom = document.getElementById('alValidFrom').value;
    const validTo   = document.getElementById('alValidTo').value;
    const monthly   = parseFloat(document.getElementById('alMonthlyAmount').value);
    const allowanceType = document.getElementById('alAllowanceType').value || null;
    const satzRaw   = document.getElementById('alTarifSatzNr').value;
    const tarifSatzNr = satzRaw === '' ? null : parseInt(satzRaw, 10);
    const note      = document.getElementById('alNote').value.trim() || null;
    const dokRaw    = document.getElementById('alDokumentId')?.value || '';
    const dokumentId = dokRaw === '' ? null : parseInt(dokRaw, 10);

    if (!validFrom)        { err.textContent = 'Gültig ab ist Pflicht.';        return; }
    if (!Number.isFinite(monthly) || monthly < 0) {
        err.textContent = 'Bitte eine Zulage aus der Liste wählen.'; return;
    }
    if (!allowanceType) {
        err.textContent = 'Bitte eine Zulage aus der Liste wählen.'; return;
    }
    if (validTo && validTo < validFrom) {
        err.textContent = 'Gültig bis darf nicht vor Gültig ab liegen.'; return;
    }

    const payload = {
        validFrom,
        validTo: validTo || null,
        monthlyAmount: monthly,
        allowanceType,
        tarifSatzNr,
        note,
        dokumentId: Number.isFinite(dokumentId) ? dokumentId : null,
    };

    try {
        const url    = id
            ? `/api/family-members/${editingFamilyMemberId}/allowances/${id}`
            : `/api/family-members/${editingFamilyMemberId}/allowances`;
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (res.status === 409) {
            const body = await res.clone().json().catch(() => ({}));
            if (body && body.error === 'LOHN_EDIT_LOCKED') {
                if (window.lohnEditLock) await window.lohnEditLock.handleResponse(res);
                err.style.color = '#dc2626';
                err.textContent = body.message || 'Lohnlauf-Sperre — Datum liegt in einer abgeschlossenen Periode.';
                if (body.firstAllowedDate) {
                    const vf = document.getElementById('alValidFrom');
                    if (vf) {
                        vf.min = body.firstAllowedDate;
                        if (!vf.value || vf.value < body.firstAllowedDate) {
                            vf.value = body.firstAllowedDate;
                            await alLoadTarifOptionsAndPreselect();
                        }
                    }
                }
                return;
            }
        }
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            err.style.color = '#dc2626';
            err.textContent = e.message || e.error || 'Fehler beim Speichern.';
            return;
        }
        closeAllowanceModal();
        loadFamilyAllowances(editingFamilyMemberId);
        // Walter 18.05.2026: Familie-Tab neu laden, damit die Inline-Zulagen
        // in der Kind-Card aktualisiert sind.
        if (typeof selectedEmployeeId !== 'undefined' && selectedEmployeeId) {
            loadFamilieTab(selectedEmployeeId);
        }
    } catch (e) {
        err.textContent = 'Verbindungsfehler: ' + e.message;
    }
}

async function deleteAllowance() {
    const id = document.getElementById('alId').value;
    if (!id || !editingFamilyMemberId) return;
    if (!(await liquidConfirm('Diese Zulagen-Position wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/family-members/${editingFamilyMemberId}/allowances/${id}`, {
            method: 'DELETE', headers: ah()
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        closeAllowanceModal();
        loadFamilyAllowances(editingFamilyMemberId);
        if (typeof selectedEmployeeId !== 'undefined' && selectedEmployeeId) {
            loadFamilieTab(selectedEmployeeId);
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// ── Timezone-sichere ISO-Datumsfunktion ─────────
// toISOString() gibt UTC zurück → in UTC+1/+2 einen Tag zu früh!
// localIso() verwendet lokale Datumskomponenten
function localIso(d) {
    return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
}

// ── Hilfsfunktionen für Datumseingaben ─────────
function toDateInput(dateStr) {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    if (isNaN(d)) return '';
    return localIso(d);
}

function toMonthInput(dateStr) {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    if (isNaN(d)) return '';
    return d.toISOString().slice(0, 7); // YYYY-MM
}

function monthInputToDate(val) {
    if (!val) return null;
    return val + '-01'; // ersten Tag des Monats
}

// ══════════════════════════════════════════════
// STEMPELZEITEN TAB
// ══════════════════════════════════════════════


// ── Nachtstunden-Berechnung ─────────────────────
// nightStart / nightEnd im Format "HH:MM", Übermitternacht wird unterstützt
function calcAutoNightHours(timeInStr, timeOutStr, nightStartStr, nightEndStr) {
    if (!timeInStr || !timeOutStr || !nightStartStr || !nightEndStr) return 0;

    const toMin = t => { const [h, m] = t.split(':').map(Number); return h * 60 + m; };
    const inMin  = toMin(timeInStr);
    const outMin = toMin(timeOutStr) + (toMin(timeOutStr) <= toMin(timeInStr) ? 1440 : 0); // Übermitternacht
    const ns = toMin(nightStartStr);
    const ne = toMin(nightEndStr) + (toMin(nightEndStr) <= ns ? 1440 : 0); // Nachtende nächster Tag

    // Überlappung [inMin, outMin) ∩ [ns, ne)
    const start = Math.max(inMin, ns);
    const end   = Math.min(outMin, ne);
    const nightMin = Math.max(0, end - start);
    return Math.round((nightMin / 60) * 100) / 100;
}

function updateAutoNightHours() {
    const timeIn  = document.getElementById('teTimeIn').value;
    const timeOut = document.getElementById('teTimeOut').value;
    if (!timeIn || !timeOut) return;
    const ns = selectedCompanyProfile?.nightStartTime ?? '00:00';
    const ne = selectedCompanyProfile?.nightEndTime   ?? '07:00';
    const night = calcAutoNightHours(timeIn, timeOut, ns, ne);
    document.getElementById('teNight').value = night;
}

// ── Modal: Eintrag hinzufügen / bearbeiten ─────

async function openTimeEntryModal(entry) {
    editingTimeEntryId = entry ? entry.id : null;
    document.getElementById('timeEntryModalTitle').textContent =
        entry ? 'Stempelzeit bearbeiten' : 'Stempelzeit hinzufügen';

    const today = localIso(new Date());
    const dateEl = document.getElementById('teDate');
    dateEl.value     = entry ? toDateInput(entry.entryDate ?? entry.entry_date) : today;
    document.getElementById('teTimeIn').value   = entry ? toTimeInput(entry.timeIn  ?? entry.time_in)  : '';
    document.getElementById('teTimeOut').value  = entry ? toTimeInput(entry.timeOut ?? entry.time_out) : '';
    document.getElementById('teNight').value    = entry?.nightHours ?? 0;
    document.getElementById('teComment').value  = entry?.comment ?? '';

    // Nachtstunden neu berechnen, wenn Zeiten vorhanden
    if (document.getElementById('teTimeIn').value && document.getElementById('teTimeOut').value) {
        updateAutoNightHours();
    }

    // Lohnlauf-Sperre: min-date setzen, damit User gesperrte Tage gar nicht
    // auswählen kann. Holt sich den State async für die Filiale des MA.
    if (window.lohnEditLock && typeof fixedCompanyProfileId !== 'undefined') {
        const state = await window.lohnEditLock.loadState(fixedCompanyProfileId);
        window.lohnEditLock.applyToDateInput(dateEl, state);
    }

    document.getElementById('timeEntryModal').style.display = 'flex';
}

function closeTimeEntryModal() {
    document.getElementById('timeEntryModal').style.display = 'none';
    editingTimeEntryId = null;
}

async function saveTimeEntry() {
    if (!selectedEmployeeId) return;

    const date    = document.getElementById('teDate').value;
    const timeIn  = document.getElementById('teTimeIn').value;
    const timeOut = document.getElementById('teTimeOut').value;

    if (!date || !timeIn) { alert('Datum und Einzeit sind pflichtfelder.'); return; }

    const payload = {
        entryDate:    date,
        timeIn:       `${date}T${timeIn}:00Z`,
        timeOut:      timeOut ? `${date}T${timeOut}:00Z` : null,
        comment:      document.getElementById('teComment').value || null,
        nightHours:   parseFloat(document.getElementById('teNight').value) || 0,
    };

    const isEdit = editingTimeEntryId !== null;
    const url    = isEdit
        ? `/api/employees/${selectedEmployeeId}/timeentries/${editingTimeEntryId}`
        : `/api/employees/${selectedEmployeeId}/timeentries`;
    const method = isEdit ? 'PUT' : 'POST';

    try {
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            let msg = `Fehler beim Speichern (HTTP ${res.status})`;
            try { const e = await res.json(); msg += '\n' + (e.error ?? '') + (e.inner ? '\n' + e.inner : ''); } catch {}
            alert(msg); return;
        }
        closeTimeEntryModal();
        loadStempelzeitenTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

async function deleteTimeEntry(id) {
    if (!(await liquidConfirm('Diesen Eintrag wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/timeentries/${id}`, {
            method: 'DELETE',
            headers: ah()
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadStempelzeitenTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

function toTimeInput(dateTimeStr) {
    if (!dateTimeStr) return '';
    const d = new Date(dateTimeStr);
    if (isNaN(d)) return '';
    return d.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: 'UTC' });
}

// ════════════════════════════════════════════════════════════════
//  ABSENZEN
// ════════════════════════════════════════════════════════════════

const ABSENCE_LABELS = {
    KRANK:      { label: 'Krankheit',                 color: 'abs-type-krank'    },
    UNFALL:     { label: 'Unfall',                    color: 'abs-type-unfall'   },
    SCHULUNG:   { label: 'Schulung',                  color: 'abs-type-schulung' },
    FERIEN:     { label: 'Ferien',                    color: 'abs-type-ferien'   },
    NACHT_KOMP: { label: 'Nacht-Kompensation',        color: 'abs-type-nacht'    },
    MILITAER:   { label: 'Militär',                   color: 'abs-type-schulung' },
    FEIERTAG:   { label: 'Feiertag',                  color: 'abs-type-feiertag' },
    MUTT_VATER: { label: 'Mutter-/Vaterschaftsurlaub', color: 'abs-type-krank'   },
    FREI_KOMP:  { label: 'Frei-Kompensation',          color: 'abs-type-nacht'   },
    BEZ_ABSENZ: { label: 'Bezahlte Absenz',            color: 'abs-type-schulung'},
};

// ══════════════════════════════════════════════════════════════════════
// Walter-Vorgabe 10.06.2026: Mutterschafts-Modul.
// ──────────────────────────────────────────────────────────────────────
// Helper: erkennt „weiblich" über alle System-Konventionen.
function IstWeiblich(gender) {
    const g = String(gender || '').trim().toLowerCase();
    return g === 'female' || g === 'weiblich' || g === 'f' || g === 'w';
}

async function loadMutterschaftTab(employeeId) {
    const el = document.getElementById('mutterschaftContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const r = await fetch(`/api/pregnancies?employeeId=${employeeId}`, { headers: ah() });
        if (!r.ok) {
            el.innerHTML = `<div style="color:#dc2626;padding:18px">Fehler beim Laden (${r.status})</div>`;
            return;
        }
        const list = await r.json();
        // Detail-Daten (mit Fristen) für jede Schwangerschaft parallel laden.
        const details = await Promise.all(list.map(p =>
            fetch(`/api/pregnancies/${p.id}`, { headers: ah() }).then(rr => rr.ok ? rr.json() : null)));
        renderMutterschaftTab(el, employeeId, details.filter(Boolean));
    } catch (e) {
        el.innerHTML = `<div style="color:#dc2626;padding:18px">Fehler: ${e.message}</div>`;
    }
}

function renderMutterschaftTab(el, employeeId, details) {
    const today = new Date().toISOString().slice(0, 10);
    // Button nur ohne offene Schwangerschaft (Walter 27.07.2026).
    const hasOpen = (details || []).some(d => d?.pregnancy && !d.pregnancy.geburtsdatum);
    const newBtn = hasOpen ? '' : `<button class="btn btn-primary" onclick="mtsOpenNew(${employeeId})" style="padding:8px 16px;font-size:13px">+ Schwangerschaft erfassen</button>`;
    if (!details.length) {
        el.innerHTML = `
        <div style="padding:14px 0 10px;display:flex;justify-content:space-between;align-items:center">
            <h3 style="margin:0;font-size:15px;font-weight:700;color:#0f172a">Mutterschaft</h3>
            ${newBtn}
        </div>
        <div class="emp-placeholder" style="padding:32px"><span>Keine Schwangerschaft erfasst.</span></div>`;
        return;
    }
    el.innerHTML = `
    <div style="padding:14px 0 10px;display:flex;justify-content:space-between;align-items:center">
        <h3 style="margin:0;font-size:15px;font-weight:700;color:#0f172a">Mutterschaft</h3>
        ${newBtn}
    </div>
    ${details.map(d => renderPregnancyCard(d)).join('')}
    `;
}

function renderPregnancyCard(d) {
    const p = d.pregnancy;
    const fmt = iso => iso ? `${iso.slice(8,10)}.${iso.slice(5,7)}.${iso.slice(0,4)}` : '–';
    const banner = d.kuendigungsschutz
        ? `<div style="margin:8px 0 12px;padding:10px 14px;background:#fce7f3;border:1px solid #f9a8d4;border-radius:8px;color:#9d174d;font-size:13px;font-weight:600">
              ⚖ Kündigungsschutz aktiv: ${fmt(d.kuendigungsschutz.von)} – ${fmt(d.kuendigungsschutz.bis)}
           </div>`
        : '';
    // Walter 10.06.2026: aufsteigend nach Datum sortieren — chronologisch
    // lesbar (was zuerst greift, steht oben). Fristen ohne Datum (Defensive)
    // landen am Ende.
    const sortedFristen = [...(d.fristen || [])].sort((a, b) => {
        const da = a.datum || '9999-12-31';
        const db = b.datum || '9999-12-31';
        return da.localeCompare(db);
    });
    const fristenRows = sortedFristen.map(f => {
        let icon, color;
        if (f.istArbeitsverbot && f.status === 'aktiv')      { icon = '🔴'; color = '#dc2626'; }
        else if (f.status === 'aktiv')                        { icon = '🟢'; color = '#16a34a'; }
        else if (f.status === 'abgeschlossen')                { icon = '✅'; color = '#94a3b8'; }
        else                                                  { icon = '⚪'; color = '#64748b'; }
        const lineThrough = f.status === 'abgeschlossen' ? 'text-decoration:line-through;opacity:0.65;' : '';
        // Variante B (Walter 10.06.2026): Lohn-Pille + Staffel-Text + Phasen-Ende
        const lohnParts = [];
        if (f.lohnersatzPct  != null) lohnParts.push(`${f.lohnersatzPct} %`);
        if (f.maxBetragProTag != null) lohnParts.push(`max. CHF ${Number(f.maxBetragProTag).toFixed(0)}/Tag`);
        const lohnPill = lohnParts.length
            ? `<span style="background:#fce7f3;color:#9d174d;font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:9px;margin-left:6px;white-space:nowrap">${lohnParts.join(' · ')}</span>`
            : '';
        const staffelLine = f.staffelText
            ? `<div style="font-size:11px;color:#9d174d;background:#fce7f3;padding:2px 6px;border-radius:4px;margin-top:3px;display:inline-block">${esc(f.staffelText)}</div>`
            : '';
        // Rechte Spalte: „ab" — und falls Phasen-Ende vorhanden auch „bis"
        const dateBlock = f.datumEnde
            ? `<div style="text-align:right">
                  <div style="font-variant-numeric:tabular-nums;color:#475569;white-space:nowrap;font-size:12.5px">${fmt(f.datum)}</div>
                  <div style="font-variant-numeric:tabular-nums;color:#94a3b8;white-space:nowrap;font-size:11px">bis ${fmt(f.datumEnde)}</div>
               </div>`
            : `<div style="font-variant-numeric:tabular-nums;color:#475569;white-space:nowrap;font-size:12.5px">${fmt(f.datum)}</div>`;
        return `<div style="display:flex;align-items:center;gap:12px;padding:8px 12px;border-bottom:1px solid #f1f5f9;font-size:13px;${lineThrough}">
            <div style="font-size:18px;line-height:1">${icon}</div>
            <div style="flex:1;min-width:0">
                <div style="font-weight:600;color:${color}">${esc(f.bezeichnung)}${lohnPill}</div>
                <div style="font-size:11.5px;color:#64748b">${esc(f.gesetz || '')}${f.beschreibung ? ' · ' + esc(f.beschreibung) : ''}</div>
                ${staffelLine}
            </div>
            ${dateBlock}
        </div>`;
    }).join('');
    // Walter 20.07.2026: Fahrplan-Menü (fixed, damit nichts abgeschnitten wird).
    // Nummerierung: 5 · Brief an Arzt, 6 · Geburt eintragen.
    const geburtsInfo = p.geburtsdatum
        ? `<span style="color:#16a34a;font-size:12px;font-weight:600">Geburt: ${fmt(p.geburtsdatum)}</span>`
        : '';
    return `
    <div style="border:1px solid #e2e8f0;border-radius:10px;margin-bottom:16px;background:white">
        <div style="padding:14px 16px;border-bottom:1px solid #e2e8f0;display:flex;justify-content:space-between;align-items:center;gap:12px">
            <div style="min-width:0;flex:1">
                <div style="font-weight:700;color:#0f172a;font-size:14px">Errechneter Termin: ${fmt(p.errechneterTermin)}</div>
                <div style="font-size:12px;color:#9d174d;margin-top:2px;font-weight:600">Beginn Schwangerschaft: ${fmt(p.schwangerschaftsBeginn)} <span style="color:#94a3b8;font-weight:400">(ET − 280 Tage)</span></div>
                <div style="font-size:12px;color:#64748b;margin-top:2px">Gemeldet: ${fmt(p.meldedatum)}${p.bemerkung ? ' · ' + esc(p.bemerkung) : ''}</div>
            </div>
            <div style="display:flex;align-items:center;gap:10px;flex-shrink:0">
                ${geburtsInfo}
                <div class="dok-menu-wrap" style="display:inline-block">
                    <button onclick="mtsToggleMenu(event, ${p.id})" title="Alle Schritte des Mutterschafts-Prozesses"
                            style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 18px;cursor:pointer;font-size:13px;font-weight:700">🧭 Fahrplan ▾</button>
                    <div class="dok-menu" id="mtsMenu-${p.id}" style="min-width:290px">
                        <button class="dok-menu-item" style="white-space:nowrap" onclick="mtsDownloadPdf(${p.id})">Fristen-Liste drucken (PDF)</button>
                        <button class="dok-menu-item" style="white-space:nowrap" onclick="mvCheckliste(${p.id})">1 · Gesprächs-Checkliste (PDF)</button>
                        <button class="dok-menu-item" style="white-space:nowrap" onclick="mvOpen(${p.id})">2 · Mutterschaftsvereinbarung…</button>
                        <button class="dok-menu-item" style="white-space:nowrap" onclick="abRisiko(${p.id})">3 · Risikobeurteilung (PDF)</button>
                        <button class="dok-menu-item" style="white-space:nowrap" onclick="abEignungMenu(${p.id})">4 · Eignungsbeurteilung (PDF)</button>
                        <button class="dok-menu-item" style="white-space:nowrap" onclick="abOpen(${p.id})">5 · Brief an behandelnden Arzt…</button>
                        ${p.geburtsdatum
                            ? `<button class="dok-menu-item" style="white-space:nowrap" onclick="mbOpen(${p.id}, '${String(p.geburtsdatum).slice(0,10)}')">6 · Mutterschaftsbestätigung…</button>`
                            : `<button class="dok-menu-item" style="white-space:nowrap" onclick="mtsOpenGeburt(${p.id})">6 · Geburt eintragen</button>`}
                    </div>
                </div>
                <div class="dok-menu-wrap">
                    <button class="dok-menu-btn" onclick="mtsActToggleMenu(event, ${p.id})" title="Aktionen">⋮</button>
                    <div class="dok-menu" id="mtsActMenu-${p.id}">
                        <button class="dok-menu-item" onclick="mtsDownloadPdf(${p.id})">Fristen-Liste drucken</button>
                        <button class="dok-menu-item" onclick="mtsOpenEdit(${p.id})">Bearbeiten</button>
                        <button class="dok-menu-item danger" onclick="mtsDelete(${p.id})">Löschen</button>
                    </div>
                </div>
            </div>
        </div>
        ${banner}
        <div style="padding:8px 12px 0;display:flex;align-items:center;justify-content:space-between;gap:8px">
            <div style="font-size:11.5px;font-weight:700;color:#64748b;text-transform:uppercase;letter-spacing:.04em">Schutzfristen</div>
            <button type="button" onclick="mtsDownloadPdf(${p.id})" title="Fristen-Liste als PDF öffnen und drucken"
                    style="background:none;border:none;color:#3f3f3f;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline;padding:0">🖨 Liste drucken</button>
        </div>
        <div style="padding:6px 0 10px">${fristenRows}</div>
    </div>`;
}

function esc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}

// Fixed-Position wie Nachtarbeit-Menü: emp-detail-panel clippt absolute
// Menüs (overflow:hidden) — sonst fehlen Fahrplan-Punkte 3–5 (Arztbrief).
function _mtsClearMenuPos(menu) {
    if (!menu) return;
    menu.style.position = '';
    menu.style.top = '';
    menu.style.left = '';
    menu.style.right = '';
    menu.style.bottom = '';
    menu.style.zIndex = '';
}
function mtsToggleMenu(event, id) {
    event.stopPropagation();
    const menu = document.getElementById('mtsMenu-' + id);
    const wasOpen = menu?.classList.contains('show');
    document.querySelectorAll('.dok-menu.show').forEach(m => {
        m.classList.remove('show');
        _mtsClearMenuPos(m);
    });
    if (wasOpen || !menu) return;
    const btn = event.currentTarget;
    const r = btn.getBoundingClientRect();
    menu.classList.add('show');
    const mh = menu.offsetHeight || 280;
    const spaceBelow = window.innerHeight - r.bottom;
    menu.style.position = 'fixed';
    menu.style.right = Math.max(8, window.innerWidth - r.right) + 'px';
    menu.style.left = 'auto';
    menu.style.zIndex = '6000';
    if (spaceBelow < mh + 8) {
        menu.style.top = 'auto';
        menu.style.bottom = Math.max(8, window.innerHeight - r.top + 4) + 'px';
    } else {
        menu.style.top = (r.bottom + 4) + 'px';
        menu.style.bottom = 'auto';
    }
    setTimeout(() => {
        document.addEventListener('click', () => {
            document.querySelectorAll('.dok-menu.show').forEach(m => {
                m.classList.remove('show');
                _mtsClearMenuPos(m);
            });
        }, { once: true });
    }, 10);
}
function mtsActToggleMenu(event, id) { rowMenuToggle(event, 'mtsAct', id); }

// Walter 11.06.2026: Sprung in den Dokumente-Tab + Filter auf
// „Absenzen → Mutter-/Vaterschaft". Analog openAbsenceArztzeugnis().
async function mtsOpenDokuTab() {
    if (!selectedEmployeeId) return;
    if (typeof switchEmpTab === 'function') switchEmpTab('dokumente');

    let taxonomy = null;
    try {
        const r = await fetch('/api/documents/taxonomie', { headers: ah() });
        if (r.ok) taxonomy = await r.json();
    } catch {}
    if (!taxonomy || taxonomy.length === 0) return;

    // Bevorzugt: Kategorie „Absenzen" + Typ enthält „Mutter" oder „Vaterschaft".
    let matchedTyp = null, matchedKat = null;
    for (const k of taxonomy) {
        const isAbsenzen = (k.name || '').toLowerCase().includes('absenzen');
        const t = (k.typen || []).find(x => {
            const n = (x.name || '').toLowerCase();
            return n.includes('mutter') || n.includes('vaterschaft');
        });
        if (t && (isAbsenzen || !matchedTyp)) {
            matchedTyp = t; matchedKat = k;
            if (isAbsenzen) break;
        }
    }
    if (!matchedTyp) {
        alert('Kein Dokument-Typ „Mutter-/Vaterschaft" gefunden.\n'
            + 'Bitte unter Systemeinstellungen → Dokument-Struktur einen Typ '
            + 'unter Kategorie „Absenzen" anlegen.');
        return;
    }

    let attempts = 0;
    while (!document.getElementById('empTabDokumente')
            ?.querySelector('.dok-tree-node')
           && attempts < 30) {
        await new Promise(r => setTimeout(r, 100));
        attempts++;
    }
    if (typeof dokSelectType === 'function') {
        dokSelectType(matchedTyp.id, matchedKat.id);
    }
    // Bei genau einem Dokument: Vorschau direkt öffnen.
    try {
        const docs = (typeof _dokState !== 'undefined' && Array.isArray(_dokState.docs))
            ? _dokState.docs.filter(d => d.dokumentTypId === matchedTyp.id)
            : [];
        if (docs.length === 1 && typeof dokOpenPreviewPanel === 'function') {
            dokOpenPreviewPanel(docs[0].id);
        }
    } catch {}
}

async function mtsLoadDokumentOptions(employeeId, selectedDokId) {
    const sel = document.getElementById('mtsFormDokumentId');
    if (!sel) return;
    sel.innerHTML = '<option value="">— Dokumente laden … —</option>';
    try {
        const r = await fetch(`/api/documents/by-employee/${employeeId}`, { headers: ah() });
        let alle = r.ok ? await r.json() : [];
        if (!Array.isArray(alle)) alle = [];
        const isEtLike = (d) => {
            const t = ((d.dokumentTypName || '') + ' ' + (d.kategorieName || '') + ' ' + (d.bemerkung || '') + ' ' + (d.filenameOriginal || '')).toLowerCase();
            return /mutter|vater|schwanger|arztbest|errechnet|termin|arztzeugnis/.test(t);
        };
        const sorted = [...alle].sort((a, b) => {
            const af = isEtLike(a) ? 0 : 1;
            const bf = isEtLike(b) ? 0 : 1;
            if (af !== bf) return af - bf;
            return String(b.erstelltAm || b.hochgeladenAm || '')
                .localeCompare(String(a.erstelltAm || a.hochgeladenAm || ''));
        });
        sel.innerHTML = '<option value="">— kein Dokument —</option>';
        for (const d of sorted) {
            const o = document.createElement('option');
            o.value = d.id;
            const typ = d.dokumentTypName || 'Dokument';
            // Bemerkung vor Filename (Walter 20.07.2026) — Dateinamen oft nicht sagend.
            const name = (d.bemerkung || '').trim() || d.filenameOriginal || ('#' + d.id);
            o.textContent = `${typ}: ${name}`;
            sel.appendChild(o);
        }
        if (selectedDokId) sel.value = String(selectedDokId);
    } catch (_) {
        sel.innerHTML = '<option value="">— Laden fehlgeschlagen —</option>';
    }
}

function mtsPreviewDokument() {
    const empId = parseInt(document.getElementById('mtsFormEmployeeId')?.value || '0', 10);
    const dokId = parseInt(document.getElementById('mtsFormDokumentId')?.value || '0', 10);
    if (!dokId) return alert('Bitte zuerst ein Dokument wählen.');
    if (typeof qstOpenBefreiungsDok === 'function') qstOpenBefreiungsDok(empId, dokId);
}

function mtsOpenNew(employeeId) {
    document.getElementById('mtsFormId').value = '';
    document.getElementById('mtsFormEmployeeId').value = employeeId;
    document.getElementById('mtsFormTitle').textContent = 'Schwangerschaft erfassen';
    const today = new Date().toISOString().slice(0,10);
    document.getElementById('mtsFormMeldedatum').value = today;
    document.getElementById('mtsFormET').value = '';
    document.getElementById('mtsFormBemerkung').value = '';
    mtsLoadDokumentOptions(employeeId, null);
    document.getElementById('mtsFormModal').style.display = 'flex';
}

async function mtsOpenEdit(id) {
    const r = await fetch(`/api/pregnancies/${id}`, { headers: ah() });
    if (!r.ok) return alert('Fehler beim Laden');
    const d = await r.json();
    const p = d.pregnancy;
    document.getElementById('mtsFormId').value = p.id;
    document.getElementById('mtsFormEmployeeId').value = p.employeeId;
    document.getElementById('mtsFormTitle').textContent = 'Schwangerschaft bearbeiten';
    document.getElementById('mtsFormMeldedatum').value = p.meldedatum;
    document.getElementById('mtsFormET').value = p.errechneterTermin;
    document.getElementById('mtsFormBemerkung').value = p.bemerkung || '';
    await mtsLoadDokumentOptions(p.employeeId, p.arztbestaetigungDokumentId || null);
    document.getElementById('mtsFormModal').style.display = 'flex';
}

function mtsCloseForm() { document.getElementById('mtsFormModal').style.display = 'none'; }

async function mtsSaveForm() {
    const id = document.getElementById('mtsFormId').value;
    const employeeId = parseInt(document.getElementById('mtsFormEmployeeId').value);
    const dokRaw = document.getElementById('mtsFormDokumentId')?.value || '';
    const dokId = dokRaw ? parseInt(dokRaw, 10) : null;
    const dto = {
        employeeId,
        meldedatum:        document.getElementById('mtsFormMeldedatum').value,
        errechneterTermin: document.getElementById('mtsFormET').value,
        bemerkung:         document.getElementById('mtsFormBemerkung').value.trim() || null,
        arztbestaetigungDokumentId: dokId,
        setArztbestaetigungDokument: true,
    };
    if (!dto.meldedatum || !dto.errechneterTermin) {
        alert('Meldedatum und errechneter Termin sind Pflicht.');
        return;
    }
    const url = id ? `/api/pregnancies/${id}` : '/api/pregnancies';
    const method = id ? 'PUT' : 'POST';
    const r = await fetch(url, {
        method,
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify(dto)
    });
    if (!r.ok) {
        let t = '';
        try { t = await r.text(); } catch (_) {}
        let msg = t;
        try {
            const j = JSON.parse(t);
            msg = j.message || j.error || t;
        } catch (_) {}
        return alert('Fehler beim Speichern: ' + (msg || ('HTTP ' + r.status)));
    }
    mtsCloseForm();
    loadFamilieTab(employeeId);
    // MA-Listen-Marker «Schwanger» aktualisieren
    if (typeof loadMitarbeiterList === 'function') loadMitarbeiterList();
}

// Geburt eintragen — Liquid-Dialog statt natives prompt() (Walter 16.07.2026).
// Gespeichert wird in employee_pregnancy.geburtsdatum (PUT /api/pregnancies/{id});
// davon haengen Kuendigungsschutz-Ende (Geburt + 16 Wochen) und Fristen ab.
// Walter 27.07.2026: zugleich Kind in der Familie anlegen (Vorname/Name/Geschlecht).
let _mtsGeburtPregId = null;

function _mtsGeburtEnsureModal() {
    let div = document.getElementById('mtsGeburtModal');
    if (!div) {
        div = document.createElement('div');
        div.id = 'mtsGeburtModal';
        div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center';
        document.body.appendChild(div);
    }
    const inp = 'width:100%;padding:8px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white;box-sizing:border-box';
    const lbl = 'font-size:11.5px;font-weight:700;color:#646464;display:block;margin-bottom:4px';
    div.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:460px;width:92%;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:4px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">Geburt eintragen</div>
            <button onclick="mtsGeburtClose()" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
        </div>
        <div style="font-size:12px;color:#646464;margin-bottom:14px">Geburtsdatum für die Mutterschafts-Fristen — und das Neugeborene wird als Kind in der Familie erfasst.</div>
        <label style="${lbl}">Geburtsdatum *</label>
        <input type="date" id="mtsGeburtDatum" data-yp="birth" style="${inp};margin-bottom:12px">
        <div style="font-size:12px;font-weight:700;color:#3f3f3f;margin:4px 0 10px">Neugeborenes (Familie)</div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 12px;margin-bottom:18px">
            <div>
                <label style="${lbl}">Vorname *</label>
                <input type="text" id="mtsGeburtVorname" autocomplete="off" style="${inp}">
            </div>
            <div>
                <label style="${lbl}">Name *</label>
                <input type="text" id="mtsGeburtNachname" autocomplete="off" style="${inp}">
            </div>
            <div style="grid-column:1 / -1">
                <label style="${lbl}">Geschlecht *</label>
                <select id="mtsGeburtGeschlecht" style="${inp}">
                    <option value="">– bitte wählen –</option>
                    <option value="Männlich">Männlich</option>
                    <option value="Weiblich">Weiblich</option>
                    <option value="Divers">Divers</option>
                </select>
            </div>
        </div>
        <div style="display:flex;justify-content:flex-end;gap:10px">
            <button onclick="mtsGeburtClose()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
            <button onclick="mtsGeburtSpeichern()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Speichern</button>
        </div>
    </div>`;
}

function mtsOpenGeburt(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    _mtsGeburtEnsureModal();
    _mtsGeburtPregId = id;
    const heute = new Date();
    document.getElementById('mtsGeburtDatum').value =
        `${heute.getFullYear()}-${String(heute.getMonth()+1).padStart(2,'0')}-${String(heute.getDate()).padStart(2,'0')}`;
    document.getElementById('mtsGeburtVorname').value = '';
    // Nachname der Mutter vorbefüllen (wie Familien-Modal bei Kind).
    document.getElementById('mtsGeburtNachname').value =
        (selectedEmployee?.lastName || '').trim();
    document.getElementById('mtsGeburtGeschlecht').value = '';
    document.getElementById('mtsGeburtModal').style.display = 'flex';
    setTimeout(() => document.getElementById('mtsGeburtVorname')?.focus(), 30);
}

function mtsGeburtClose() {
    const m = document.getElementById('mtsGeburtModal');
    if (m) m.style.display = 'none';
}

async function mtsGeburtSpeichern() {
    const iso = document.getElementById('mtsGeburtDatum').value;
    const vorname = (document.getElementById('mtsGeburtVorname')?.value || '').trim();
    const nachname = (document.getElementById('mtsGeburtNachname')?.value || '').trim();
    const geschlecht = (document.getElementById('mtsGeburtGeschlecht')?.value || '').trim();
    if (!iso) return alert('Bitte das Geburtsdatum wählen.');
    if (!vorname) return alert('Bitte den Vornamen des Kindes angeben.');
    if (!nachname) return alert('Bitte den Namen des Kindes angeben.');
    if (!geschlecht) return alert('Bitte das Geschlecht des Kindes wählen.');
    const r = await fetch(`/api/pregnancies/${_mtsGeburtPregId}`, {
        method: 'PUT',
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({
            geburtsdatum: iso,
            kindVorname: vorname,
            kindNachname: nachname,
            kindGeschlecht: geschlecht,
        })
    });
    if (!r.ok) {
        let msg = 'Fehler beim Speichern.';
        try {
            const j = await r.json();
            if (j.message) msg = j.message;
        } catch {
            try { msg = await r.text(); } catch { /* ignore */ }
        }
        return alert(msg);
    }
    mtsGeburtClose();
    loadFamilieTab(selectedEmployeeId);
    if (typeof showToast === 'function')
        showToast('Geburt eingetragen — Kind in der Familie erfasst', 'success');
}

// ── Mutterschaftsbestätigung nach der Geburt (Walter 16.07.2026, nach
// Word-Vorlage): Gratulation, Urlaubs-Zeitraum 98 Tage ab Geburt,
// Rückkehr-Varianten bzw. Beendigung, EO-Formular-Frist. Fahrplan-Punkt 5,
// sobald das definitive Geburtsdatum erfasst ist.
let _mbPregId = null;
let _mbGeburtIso = null;

function _mbEnsureModal() {
    if (document.getElementById('mbModal')) return;
    const div = document.createElement('div');
    div.id = 'mbModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center';
    div.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:560px;width:94%;max-height:92vh;overflow-y:auto;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:4px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">Mutterschaftsbestätigung</div>
            <button onclick="mbClose()" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
        </div>
        <div id="mbInfo" style="font-size:12px;color:#646464;margin-bottom:14px"></div>

        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 14px">
            <div>
                <label style="font-size:11.5px;font-weight:700;color:#646464">Vorname des Kindes (optional)</label>
                <input type="text" id="mbKind" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div>
                <label style="font-size:11.5px;font-weight:700;color:#646464">Wiederaufnahme der Arbeit am</label>
                <input type="date" id="mbWieder" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div>
                <label style="font-size:11.5px;font-weight:700;color:#646464">Bezahlte Urlaubstage im Anschluss</label>
                <input type="number" id="mbUrlaubBez" min="0" value="0" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div>
                <label style="font-size:11.5px;font-weight:700;color:#646464">Unbezahlte Urlaubstage im Anschluss</label>
                <input type="number" id="mbUrlaubUnbez" min="0" value="0" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
        </div>

        <div style="margin-top:14px">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:5px">Rückkehr</div>
            <label style="display:block;font-size:13px;margin-bottom:3px"><input type="radio" name="mbRueck" value="GLEICH" checked onchange="mbRueckChanged()"> zu denselben Bedingungen</label>
            <label style="display:block;font-size:13px;margin-bottom:3px"><input type="radio" name="mbRueck" value="ANDERS" onchange="mbRueckChanged()"> zu geänderten Bedingungen</label>
            <label style="display:block;font-size:13px"><input type="radio" name="mbRueck" value="KEINE" onchange="mbRueckChanged()"> Beendigung des Arbeitsverhältnisses</label>
            <div id="mbAndersFields" style="display:none;margin-top:8px;padding:10px;background:rgba(255,255,255,0.5);border:1px solid rgba(139,139,139,0.25);border-radius:10px">
                <div style="display:grid;grid-template-columns:120px 1fr;gap:10px">
                    <div>
                        <label style="font-size:11.5px;font-weight:700;color:#646464">Pensum %</label>
                        <input type="number" id="mbPensum" min="1" max="100" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                    </div>
                    <div>
                        <label style="font-size:11.5px;font-weight:700;color:#646464">Restaurant (bei Wechsel, sonst leer)</label>
                        <input type="text" id="mbRestaurant" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                    </div>
                </div>
            </div>
            <div id="mbKeineFields" style="display:none;margin-top:8px;padding:10px;background:rgba(255,255,255,0.5);border:1px solid rgba(139,139,139,0.25);border-radius:10px">
                <label style="font-size:13px;cursor:pointer"><input type="checkbox" id="mbPk" style="width:15px;height:15px;cursor:pointer"> Mitarbeiterin zahlt in die Pensionskasse ein (Formular «Freizügigkeitsleistung» beilegen)</label>
            </div>
        </div>

        <div style="margin-top:14px">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:5px">Zustellung</div>
            <label style="font-size:13px;margin-right:16px"><input type="radio" name="mbZustell" value="P" checked> persönliche Aushändigung</label>
            <label style="font-size:13px"><input type="radio" name="mbZustell" value="E"> per Einschreiben</label>
        </div>

        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:20px">
            <button onclick="mbClose()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
            <button onclick="mbGenerate()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Bestätigung erstellen</button>
        </div>
    </div>`;
    document.body.appendChild(div);
}

function mbOpen(pregId, geburtIso) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    _mbEnsureModal();
    _mbPregId = pregId;
    _mbGeburtIso = geburtIso;
    const geburt = new Date(geburtIso);
    const ende = new Date(geburt); ende.setDate(ende.getDate() + 97);
    const wieder = new Date(ende); wieder.setDate(wieder.getDate() + 1);
    const iso = d => `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
    const ch = d => `${String(d.getDate()).padStart(2,'0')}.${String(d.getMonth()+1).padStart(2,'0')}.${d.getFullYear()}`;
    document.getElementById('mbInfo').textContent =
        `Entbunden am ${ch(geburt)} — Mutterschaftsurlaub 14 Wochen (98 Tage) bis ${ch(ende)}. Wiederaufnahme-Vorschlag: Folgetag (bei Urlaub im Anschluss entsprechend anpassen).`;
    document.getElementById('mbWieder').value = iso(wieder);
    document.getElementById('mbKind').value = '';
    document.getElementById('mbUrlaubBez').value = 0;
    document.getElementById('mbUrlaubUnbez').value = 0;
    document.querySelector('input[name="mbRueck"][value="GLEICH"]').checked = true;
    mbRueckChanged();
    document.getElementById('mbModal').style.display = 'flex';
}

function mbClose() {
    const m = document.getElementById('mbModal');
    if (m) m.style.display = 'none';
}

function mbRueckChanged() {
    const v = document.querySelector('input[name="mbRueck"]:checked')?.value;
    document.getElementById('mbAndersFields').style.display = v === 'ANDERS' ? 'block' : 'none';
    document.getElementById('mbKeineFields').style.display  = v === 'KEINE'  ? 'block' : 'none';
    const w = document.getElementById('mbWieder');
    if (w) w.disabled = v === 'KEINE';
}

async function mbGenerate() {
    if (!_mbPregId) return;
    const rueckkehr = document.querySelector('input[name="mbRueck"]:checked')?.value || 'GLEICH';
    const dto = {
        rueckkehr,
        urlaubBezahlt:   +(document.getElementById('mbUrlaubBez').value || 0),
        urlaubUnbezahlt: +(document.getElementById('mbUrlaubUnbez').value || 0),
        wiederaufnahme:  rueckkehr === 'KEINE' ? null : (document.getElementById('mbWieder').value || null),
        pensumProzent:   document.getElementById('mbPensum').value ? +document.getElementById('mbPensum').value : null,
        rueckkehrRestaurant: document.getElementById('mbRestaurant').value || null,
        pensionskasse:   document.getElementById('mbPk').checked,
        kindName:        document.getElementById('mbKind').value || null,
        eingeschrieben:  document.querySelector('input[name="mbZustell"]:checked')?.value === 'E'
    };
    if (rueckkehr !== 'KEINE' && !dto.wiederaufnahme) return alert('Bitte das Datum der Wiederaufnahme wählen.');
    if (rueckkehr === 'ANDERS' && !dto.pensumProzent) return alert('Bei geänderten Bedingungen bitte das Pensum in % angeben.');
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${_mbPregId}/bestaetigung-pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        mbClose();
        previewFileModal(blob, 'Mutterschaftsbestaetigung.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

// ── Brief an den behandelnden Arzt (Walter 16.07.2026, nach Word-Vorlage):
// medizinische Eignungsuntersuchung Mutterschutz. Arzt aus dem Ärzte-
// Verzeichnis (Systemeinstellungen → Ärzte); PDF im Vorschaufenster oder
// direkt per E-Mail an die Praxis (mit Liquid-Bestätigung).
let _abPregId = null;

let _abAerzteListe = [];
let _abEditId = null; // null = neuer Arzt, sonst PUT auf bestehenden

function _abEnsureModal() {
    // Altes Modal ohne Dok-/Edit-Actions nach Deploy neu aufbauen.
    const existing = document.getElementById('abModal');
    if (existing && (!document.getElementById('abDokBlock') || !document.getElementById('abEditArztBtn') || !document.getElementById('abPlzHint'))) {
        existing.remove();
    }
    if (document.getElementById('abModal')) return;
    const div = document.createElement('div');
    div.id = 'abModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center';
    div.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:520px;width:94%;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:4px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">Brief an den behandelnden Arzt</div>
            <button onclick="abClose()" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
        </div>
        <div style="font-size:12px;color:#646464;margin-bottom:14px">Medizinische Eignungsuntersuchung Mutterschutz — Beilagen: Risikobeurteilung + Eignungsbeurteilung. Ärzte werden in den Systemeinstellungen → Ärzte gepflegt.</div>
        <div id="abDokBlock" style="margin-bottom:14px;padding:10px 12px;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.28);border-radius:10px">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:4px">Arztbestätigung errechneter Termin</div>
            <div id="abDokContent" style="font-size:12.5px;color:#3f3f3f">—</div>
        </div>
        <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;flex-wrap:wrap">
            <label style="font-size:11.5px;font-weight:700;color:#646464">Behandelnde Ärztin / behandelnder Arzt</label>
            <div style="display:flex;align-items:center;gap:12px">
                <button type="button" onclick="abToggleNeu()" style="background:none;border:none;color:#3f3f3f;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline">+ Neuer Arzt</button>
                <button type="button" id="abEditArztBtn" onclick="abEditArzt()" style="background:none;border:none;color:#3f3f3f;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline">Bearbeiten</button>
                <button type="button" id="abDeleteArztBtn" onclick="abDeleteArzt()" style="background:none;border:none;color:#b91c1c;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline">Löschen</button>
            </div>
        </div>
        <select id="abArzt" style="width:100%;padding:8px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white;margin-bottom:16px"></select>
        <!-- Schnell-Erfassung / Bearbeiten (Walter 16.07.2026 / 20.07.2026) -->
        <div id="abNeuBlock" style="display:none;margin:-6px 0 16px;padding:12px;background:rgba(255,255,255,0.5);border:1px solid rgba(139,139,139,0.25);border-radius:10px">
            <div id="abNeuBlockTitle" style="font-size:12px;font-weight:700;color:#3f3f3f;margin-bottom:8px">Neuer Arzt</div>
            <div style="display:grid;grid-template-columns:90px 1fr 1fr;gap:8px">
                <input type="text" id="abNeuTitel" placeholder="Titel" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                <input type="text" id="abNeuVorname" placeholder="Vorname" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                <input type="text" id="abNeuNachname" placeholder="Nachname (Pflicht)" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:8px">
                <input type="text" id="abNeuFach" placeholder="Fachgebiet" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                <input type="text" id="abNeuPraxis" placeholder="Praxis / Institution" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div style="display:grid;grid-template-columns:2fr 80px 1fr;gap:8px;margin-top:8px">
                <input type="text" id="abNeuStrasse" placeholder="Strasse Nr." style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                <input type="text" id="abNeuPlz" placeholder="PLZ" inputmode="numeric" maxlength="4" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white"
                       oninput="validateZip(this);if(this.value.length===4)plzLookupGeneric(this.value,'abNeuOrt',null,null,'abPlzHint')"
                       onblur="plzLookupGeneric(this.value,'abNeuOrt',null,null,'abPlzHint')">
                <input type="text" id="abNeuOrt" placeholder="Ort" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white"
                       oninput="validateCity(this)">
            </div>
            <div id="abPlzHint" style="font-size:11.5px;margin-top:4px;min-height:16px"></div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:8px">
                <input type="text" id="abNeuTelefon" placeholder="Telefon" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                <input type="text" id="abNeuEmail" placeholder="E-Mail" style="padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:10px">
                <button type="button" onclick="abNeuAbbrechen()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:8px 14px;cursor:pointer;font-size:13px;font-weight:700">Abbrechen</button>
                <button type="button" id="abNeuSaveBtn" onclick="abNeuSpeichern()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 16px;cursor:pointer;font-size:13px;font-weight:700">Arzt speichern</button>
            </div>
        </div>
        <div style="display:flex;justify-content:flex-end;gap:10px;flex-wrap:wrap">
            <button onclick="abClose()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
            <button onclick="abRisiko()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">📋 Risikobeurteilung</button>
            <button onclick="abEignung()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">🩺 Eignungsbeurteilung</button>
            <button onclick="abPdf()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">📄 Brief (PDF)</button>
            <button onclick="abEmail()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">✉ Per E-Mail senden</button>
        </div>
    </div>`;
    document.body.appendChild(div);
}

let _abEmpId = null;
let _abDokId = null;

function _abRenderDokBlock(p) {
    const box = document.getElementById('abDokContent');
    if (!box) return;
    _abEmpId = p?.employeeId || null;
    _abDokId = p?.arztbestaetigungDokumentId || null;
    if (_abDokId) {
        const name = p.arztbestaetigungDokumentName || ('Dokument #' + _abDokId);
        box.innerHTML = `<div style="display:flex;align-items:center;justify-content:space-between;gap:10px">
            <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap" title="${esc(name)}">📄 ${esc(name)}</span>
            <button type="button" onclick="abOpenDokument()" style="flex-shrink:0;background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:6px 12px;cursor:pointer;font-size:12px;font-weight:700">Anschauen</button>
        </div>`;
    } else {
        box.innerHTML = `<span style="color:#8b8b8b">Noch nicht verknüpft — bitte bei der Schwangerschaftserfassung die Arztbestätigung verbinden.</span>`;
    }
}

function abOpenDokument() {
    if (!_abDokId || !_abEmpId) return;
    // sticky: neben dem Arztbrief tippen ohne dass die Meldung verschwindet
    // (Walter 20.07.2026) — schliesst nur via × oder bei Speichern/Abbrechen.
    if (typeof qstOpenBefreiungsDok === 'function') {
        qstOpenBefreiungsDok(_abEmpId, _abDokId, { sticky: true });
    }
}

async function abOpen(pregId) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    _abEnsureModal();
    _abPregId = pregId;
    _abEditId = null;
    abNeuAbbrechen();
    // Arztbestätigung aus der Schwangerschaft laden (Walter 20.07.2026).
    try {
        const r = await fetch(`/api/pregnancies/${pregId}`, { headers: ah() });
        if (r.ok) {
            const d = await r.json();
            _abRenderDokBlock(d.pregnancy || null);
        } else {
            _abRenderDokBlock(null);
        }
    } catch (_) { _abRenderDokBlock(null); }
    await abLoadAerzte();
    document.getElementById('abModal').style.display = 'flex';
}

async function abLoadAerzte(selectId) {
    const sel = document.getElementById('abArzt');
    if (!sel) return;
    sel.innerHTML = '<option value="">— Arzt wählen —</option>';
    _abAerzteListe = [];
    try {
        const r = await fetch('/api/aerzte', { headers: ah() });
        if (r.ok) {
            _abAerzteListe = await r.json();
            for (const a of _abAerzteListe) {
                const name = [a.titel, a.vorname, a.nachname].filter(Boolean).join(' ');
                const extra = [a.praxisName, a.ort].filter(Boolean).join(', ');
                const o = document.createElement('option');
                o.value = a.id;
                o.textContent = extra ? `${name} — ${extra}` : name;
                o.dataset.email = a.email || '';
                sel.appendChild(o);
            }
        }
    } catch (_) {}
    if (selectId) { sel.value = String(selectId); sel.dispatchEvent(new Event('change')); }
}

function _abClearNeuForm() {
    ['abNeuTitel','abNeuVorname','abNeuNachname','abNeuFach','abNeuPraxis','abNeuStrasse','abNeuPlz','abNeuOrt','abNeuTelefon','abNeuEmail']
        .forEach(id => { const el = document.getElementById(id); if (el) el.value = ''; });
}

function _abShowNeuBlock(editMode) {
    const b = document.getElementById('abNeuBlock');
    const title = document.getElementById('abNeuBlockTitle');
    const btn = document.getElementById('abNeuSaveBtn');
    if (!b) return;
    b.style.display = 'block';
    if (title) title.textContent = editMode ? 'Arzt bearbeiten' : 'Neuer Arzt';
    if (btn) btn.textContent = editMode ? 'Änderungen speichern' : 'Arzt speichern';
}

function abNeuAbbrechen() {
    _abEditId = null;
    _abClearNeuForm();
    const b = document.getElementById('abNeuBlock');
    if (b) b.style.display = 'none';
}

function abToggleNeu() {
    const b = document.getElementById('abNeuBlock');
    if (!b) return;
    // Wenn schon offen im Neu-Modus → einklappen; sonst Neu-Formular zeigen.
    if (b.style.display !== 'none' && !_abEditId) {
        abNeuAbbrechen();
        return;
    }
    _abEditId = null;
    _abClearNeuForm();
    _abShowNeuBlock(false);
}

function abEditArzt() {
    const id = +(document.getElementById('abArzt')?.value || 0);
    if (!id) return alert('Bitte zuerst einen Arzt in der Liste wählen.');
    const a = _abAerzteListe.find(x => x.id === id);
    if (!a) return alert('Arzt nicht gefunden — Liste bitte neu laden.');
    _abEditId = id;
    const set = (elId, val) => { const el = document.getElementById(elId); if (el) el.value = val || ''; };
    set('abNeuTitel', a.titel);
    set('abNeuVorname', a.vorname);
    set('abNeuNachname', a.nachname);
    set('abNeuFach', a.fachgebiet);
    set('abNeuPraxis', a.praxisName);
    set('abNeuStrasse', a.strasse);
    set('abNeuPlz', a.plz);
    set('abNeuOrt', a.ort);
    set('abNeuTelefon', a.telefon);
    set('abNeuEmail', a.email);
    _abShowNeuBlock(true);
}

async function abDeleteArzt() {
    const id = +(document.getElementById('abArzt')?.value || 0);
    if (!id) return alert('Bitte zuerst einen Arzt in der Liste wählen.');
    const a = _abAerzteListe.find(x => x.id === id);
    const name = [a?.titel, a?.vorname, a?.nachname].filter(Boolean).join(' ') || ('#' + id);
    const ja = typeof liquidConfirm === 'function'
        ? await liquidConfirm(`Arzt «${name}» wirklich löschen?`, { title: 'Arzt löschen?', yesLabel: 'Ja, löschen', noLabel: 'Nein' })
        : confirm(`Arzt «${name}» wirklich löschen?`);
    if (!ja) return;
    try {
        const r = await fetch(`/api/aerzte/${id}`, { method: 'DELETE', headers: ah() });
        if (!r.ok) return alert('Löschen fehlgeschlagen: ' + r.status);
        if (_abEditId === id) abNeuAbbrechen();
        await abLoadAerzte();
    } catch (e) { alert('Fehler: ' + e.message); }
}

// Speichert neu (POST) oder Änderungen (PUT) ins Ärzte-Verzeichnis und
// selektiert den Arzt danach im Dropdown.
async function abNeuSpeichern() {
    const v = id => (document.getElementById(id)?.value || '').trim();
    const dto = {
        titel: v('abNeuTitel') || null,
        vorname: v('abNeuVorname'),
        nachname: v('abNeuNachname'),
        fachgebiet: v('abNeuFach') || null,
        praxisName: v('abNeuPraxis') || null,
        strasse: v('abNeuStrasse') || null,
        plz: v('abNeuPlz') || null,
        ort: v('abNeuOrt') || null,
        telefon: v('abNeuTelefon') || null,
        email: v('abNeuEmail') || null
    };
    if (!dto.nachname) return alert('Bitte mindestens den Nachnamen erfassen.');
    const editId = _abEditId;
    try {
        const url = editId ? `/api/aerzte/${editId}` : '/api/aerzte';
        const r = await fetch(url, {
            method: editId ? 'PUT' : 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('Speichern fehlgeschlagen: ' + t); }
        const saved = await r.json();
        abNeuAbbrechen();
        if (typeof dokClosePreviewPanel === 'function') dokClosePreviewPanel();
        await abLoadAerzte(saved.id);
    } catch (e) { alert('Fehler: ' + e.message); }
}

function abClose() {
    const m = document.getElementById('abModal');
    if (m) m.style.display = 'none';
    if (typeof dokClosePreviewPanel === 'function') dokClosePreviewPanel();
}

function _abArztId() {
    const id = +(document.getElementById('abArzt')?.value || 0);
    if (!id) { alert('Bitte zuerst einen Arzt wählen.'); return null; }
    return id;
}

async function abPdf() {
    const arztId = _abArztId();
    if (!arztId || !_abPregId) return;
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${_abPregId}/arztbrief-pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ arztId })
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        abClose();
        previewFileModal(blob, 'Arztbrief_Eignungsuntersuchung.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

// Personalisierte Risikobeurteilung (offizielles 7-Seiten-PDF, Seite 1 mit
// Filiale/Kontakt/Betriebsbeschrieb ausgefuellt) im Vorschaufenster.
async function abRisiko(pregId) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const id = pregId || _abPregId;
    if (!id) return;
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${id}/risikobeurteilung-pdf`, { headers: ah() });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        previewFileModal(blob, 'Risikobeurteilung_Mutterschutz.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

// Eignungsbeurteilung (Aerztliches Zeugnis MuSchV Art. 3) — Beilage zum
// Arztbrief; Arzt-Vorauswahl wird uebernommen, ist aber optional.
async function abEignung() {
    if (!_abPregId) return;
    const arztId = +(document.getElementById('abArzt')?.value || 0);
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${_abPregId}/eignung-pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ arztId })
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        previewFileModal(blob, 'Eignungsbeurteilung.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

// Fahrplan-Punkt 4: Eignung auch ohne geöffnetes Arztbrief-Modal.
async function abEignungMenu(pregId) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    if (pregId) _abPregId = pregId;
    if (!_abPregId) return;
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${_abPregId}/eignung-pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ arztId: 0 })
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        previewFileModal(blob, 'Eignungsbeurteilung.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

async function abEmail() {
    const arztId = _abArztId();
    if (!arztId || !_abPregId) return;
    const sel = document.getElementById('abArzt');
    const opt = sel.options[sel.selectedIndex];
    const email = opt?.dataset?.email || '';
    if (!email) return alert('Für diesen Arzt ist keine E-Mail-Adresse hinterlegt — bitte in Systemeinstellungen → Ärzte ergänzen.');
    const ja = await liquidConfirm(
        `Arztbrief jetzt per E-Mail an ${email} senden?\n\nAls Anhang mitgeschickt werden der Brief, die personalisierte Risikobeurteilung Mutterschutz und die Eignungsbeurteilung (Ärztliches Zeugnis).`,
        { title: 'E-Mail senden?', yesLabel: 'Ja, senden', noLabel: 'Nein' });
    if (!ja) return;
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${_abPregId}/arztbrief-email`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ arztId })
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('E-Mail-Fehler: ' + t); }
        abClose();
        alert('Arztbrief wurde per E-Mail an ' + email + ' gesendet.');
    } catch (e) { alert('Fehler: ' + e.message); }
}

// ── Mutterschafts-Gespräch: Checkliste + Vereinbarung (Walter 16.07.2026,
// nach Word-Vorlage «Mutterschaftsvereinbarung.docx»). Ablauf: Checkliste
// drucken → Gespräch mit der MA → Varianten im Modal wählen → Vereinbarung
// als PDF (Vorschaufenster, dann drucken/unterschreiben/ablegen).
let _mvPregId = null;

function _mvEnsureModal() {
    // Altes Modal ohne vollständige Rückkehr-Optionen nach Deploy neu aufbauen.
    const existing = document.getElementById('mvModal');
    if (existing && !document.querySelector('input[name="mvRueck"][value="KEINE"]')) {
        existing.remove();
    }
    if (document.getElementById('mvModal')) return;
    const div = document.createElement('div');
    div.id = 'mvModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center';
    div.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:560px;width:94%;max-height:92vh;overflow-y:auto;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:4px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">Mutterschaftsvereinbarung</div>
            <button onclick="mvClose()" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
        </div>
        <div style="font-size:12px;color:#646464;margin-bottom:14px">Zuerst die Checkliste mit der Mitarbeiterin durcharbeiten — danach hier die vereinbarten Varianten wählen (Verlängerung + Rückkehr kommen in den Brieftext).</div>

        <button onclick="mvCheckliste()" style="width:100%;background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 14px;cursor:pointer;font-size:13px;font-weight:700;margin-bottom:16px">📋 Gesprächs-Checkliste drucken</button>

        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 14px">
            <div style="grid-column:1/3">
                <label style="font-size:11.5px;font-weight:700;color:#646464">Gesprächsdatum</label>
                <input type="date" id="mvGespraech" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div>
                <label style="font-size:11.5px;font-weight:700;color:#646464">Verlängerung: bezahlte Urlaubstage</label>
                <input type="number" id="mvVerlBez" min="0" value="0" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
            <div>
                <label style="font-size:11.5px;font-weight:700;color:#646464">Verlängerung: unbezahlte Urlaubstage</label>
                <input type="number" id="mvVerlUnbez" min="0" value="0" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
            </div>
        </div>

        <div style="margin-top:14px;padding:12px;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.28);border-radius:12px">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:8px">Rückkehr nach der Mutterschaft <span style="color:#b91c1c">*</span></div>
            <label style="display:flex;align-items:flex-start;gap:8px;font-size:13px;margin-bottom:8px;cursor:pointer"><input type="radio" name="mvRueck" value="GLEICH" checked onchange="mvRueckChanged()" style="margin-top:2px"> <span>dieselben Vertragsbedingungen wie vor der Geburt</span></label>
            <label style="display:flex;align-items:flex-start;gap:8px;font-size:13px;margin-bottom:8px;cursor:pointer"><input type="radio" name="mvRueck" value="ANDERS" onchange="mvRueckChanged()" style="margin-top:2px"> <span>geänderte Bedingungen (Pensum / Restaurant / Verfügbarkeit)</span></label>
            <label style="display:flex;align-items:flex-start;gap:8px;font-size:13px;cursor:pointer"><input type="radio" name="mvRueck" value="KEINE" onchange="mvRueckChanged()" style="margin-top:2px"> <span><b>keine Rückkehr</b> nach dem Mutterschaftsurlaub (Wunsch der Mitarbeiterin)</span></label>
            <div id="mvAndersFields" style="display:none;margin-top:10px;padding:10px;background:rgba(255,255,255,0.5);border:1px solid rgba(139,139,139,0.25);border-radius:10px">
                <div style="display:grid;grid-template-columns:120px 1fr;gap:10px">
                    <div>
                        <label style="font-size:11.5px;font-weight:700;color:#646464">Pensum %</label>
                        <input type="number" id="mvPensum" min="1" max="100" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                    </div>
                    <div>
                        <label style="font-size:11.5px;font-weight:700;color:#646464">Restaurant (bei Wechsel, sonst leer)</label>
                        <input type="text" id="mvRestaurant" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
                    </div>
                </div>
                <div style="font-size:11px;color:#8b8b8b;margin-top:6px">Neue Verfügbarkeitszeiten der Vereinbarung beilegen.</div>
            </div>
            <div id="mvKeineHint" style="display:none;margin-top:10px;padding:8px 10px;background:#fce7f3;border:1px solid #f9a8d4;border-radius:8px;font-size:12px;color:#9d174d;font-weight:600">Im Brief steht dann die Kenntnisnahme: keine Wiederaufnahme der Beschäftigung nach dem Mutterschaftsurlaub.</div>
        </div>

        <div style="margin-top:14px">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:5px">Zustellung</div>
            <label style="font-size:13px;margin-right:16px"><input type="radio" name="mvZustell" value="P" checked> persönliche Aushändigung</label>
            <label style="font-size:13px"><input type="radio" name="mvZustell" value="E"> per Einschreiben</label>
        </div>

        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:20px">
            <button onclick="mvClose()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
            <button onclick="mvGenerate()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Vereinbarung erstellen</button>
        </div>
    </div>`;
    document.body.appendChild(div);
}

function mvOpen(pregId) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    _mvEnsureModal();
    _mvPregId = pregId;
    const heute = new Date();
    document.getElementById('mvGespraech').value =
        `${heute.getFullYear()}-${String(heute.getMonth()+1).padStart(2,'0')}-${String(heute.getDate()).padStart(2,'0')}`;
    document.getElementById('mvVerlBez').value = 0;
    document.getElementById('mvVerlUnbez').value = 0;
    const g = document.querySelector('input[name="mvRueck"][value="GLEICH"]');
    if (g) g.checked = true;
    mvRueckChanged();
    document.getElementById('mvModal').style.display = 'flex';
}

function mvClose() {
    const m = document.getElementById('mvModal');
    if (m) m.style.display = 'none';
}

function mvRueckChanged() {
    const v = document.querySelector('input[name="mvRueck"]:checked')?.value;
    const anders = document.getElementById('mvAndersFields');
    const hint = document.getElementById('mvKeineHint');
    if (anders) anders.style.display = v === 'ANDERS' ? 'block' : 'none';
    if (hint) hint.style.display = v === 'KEINE' ? 'block' : 'none';
}

async function mvCheckliste(pregId) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const id = pregId || _mvPregId;
    if (!id) return;
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${id}/checkliste-pdf`, { headers: ah() });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        previewFileModal(blob, 'Mutterschafts-Checkliste.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

async function mvGenerate() {
    if (!_mvPregId) return;
    const rueckkehr = document.querySelector('input[name="mvRueck"]:checked')?.value;
    if (!rueckkehr) {
        return alert('Bitte die Rückkehr nach der Mutterschaft wählen (dieselben Bedingungen / geändert / keine Rückkehr).');
    }
    const dto = {
        gespraechsDatum: document.getElementById('mvGespraech').value || null,
        verlBezahlt:     +(document.getElementById('mvVerlBez').value || 0),
        verlUnbezahlt:   +(document.getElementById('mvVerlUnbez').value || 0),
        rueckkehr,
        pensumProzent:   document.getElementById('mvPensum')?.value ? +document.getElementById('mvPensum').value : null,
        rueckkehrRestaurant: document.getElementById('mvRestaurant')?.value || null,
        eingeschrieben:  document.querySelector('input[name="mvZustell"]:checked')?.value === 'E'
    };
    if (dto.rueckkehr === 'ANDERS' && !dto.pensumProzent) {
        return alert('Bei geänderten Bedingungen bitte das Pensum in % angeben.');
    }
    try {
        const r = await fetch(`/api/mutterschaft-vereinbarung/${_mvPregId}/vereinbarung-pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('PDF-Fehler: ' + t); }
        const blob = await r.blob();
        mvClose();
        previewFileModal(blob, 'Mutterschaftsvereinbarung.pdf');
    } catch (e) { alert('Fehler: ' + e.message); }
}

async function mtsDownloadPdf(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => {
        m.classList.remove('show');
        if (typeof _mtsClearMenuPos === 'function') _mtsClearMenuPos(m);
    });
    try {
        const r = await fetch(`/api/pregnancies/${id}/pdf`, { headers: ah() });
        if (!r.ok) {
            let msg = 'PDF-Fehler: ' + r.status;
            try { const j = await r.json(); if (j.message) msg = j.message; } catch {}
            return alert(msg);
        }
        const blob = await r.blob();
        const fname = `Mutterschaft_Fristen_${id}.pdf`;
        // Vorschau mit Drucken / Herunterladen (Walter 20.07.2026).
        if (typeof previewFileModal === 'function') previewFileModal(blob, fname);
        else if (typeof saveBlobAsk === 'function') saveBlobAsk(blob, fname);
    } catch (e) {
        alert('PDF-Fehler: ' + e.message);
    }
}

async function mtsDelete(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    if (!(await liquidConfirm('Schwangerschaft wirklich löschen?'))) return;
    const r = await fetch(`/api/pregnancies/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) return alert('Fehler: ' + await r.text());
    loadFamilieTab(selectedEmployeeId);
    if (typeof loadMitarbeiterList === 'function') loadMitarbeiterList();
}

// Cache für Überlappungs-Check im Absenz-Modal (Walter 26.07.2026).
window._absencesCache = window._absencesCache || { employeeId: null, list: [] };

async function loadAbsenzenTab(employeeId) {
    const el = document.getElementById('absenzenContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    const karenzSide = document.getElementById('karenzSidebar');
    if (karenzSide) karenzSide.innerHTML = '';
    try {
        const activeEmp = selectedEmployee?.employments?.find(e => e.isActive)
                       ?? selectedEmployee?.employments?.[0];
        const cpId      = activeEmp?.companyProfileId;

        // Soft-Lock kommt serverseitig pro Absenz als inLohnVerwendet
        // (nur Status «abgeschlossen») — kein hard firstAllowedDate mehr
        // (der sperrte schon bei provisorisch/HR/Akonto).
        const [absRes, karenzKrankRes, karenzUnfallRes, sperrRes] = await Promise.all([
            fetch(`/api/absences/employee/${employeeId}`, { headers: ah() }),
            cpId
                ? fetch(`/api/absences/employee/${employeeId}/karenz-history?companyProfileId=${cpId}&absenceType=KRANK`, { headers: ah() })
                : Promise.resolve(null),
            cpId
                ? fetch(`/api/absences/employee/${employeeId}/karenz-history?companyProfileId=${cpId}&absenceType=UNFALL`, { headers: ah() })
                : Promise.resolve(null),
            fetch(`/api/absences/employee/${employeeId}/sperrfrist`, { headers: ah() }),
        ]);
        if (!absRes.ok) throw new Error();
        const absences         = await absRes.json();
        window._absencesCache = { employeeId, list: Array.isArray(absences) ? absences : [] };
        const karenzKrankHist  = karenzKrankRes  && karenzKrankRes.ok  ? await karenzKrankRes.json()  : [];
        const karenzUnfallHist = karenzUnfallRes && karenzUnfallRes.ok ? await karenzUnfallRes.json() : [];
        const sperrfrist       = sperrRes && sperrRes.ok ? await sperrRes.json() : null;
        renderAbsenzenList(el, absences, employeeId, karenzKrankHist, sperrfrist, karenzUnfallHist);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
    }
}

/** Pro Kalendertag nur eine Absenz — Client-Vorabcheck (Server blockt hart). */
function findAbsenceOverlap(employeeId, dateFrom, dateTo, excludeId) {
    const cache = window._absencesCache;
    const list = (cache && cache.employeeId === employeeId && Array.isArray(cache.list))
        ? cache.list
        : [];
    return list.find(a => {
        if (excludeId && String(a.id) === String(excludeId)) return false;
        return a.dateFrom <= dateTo && a.dateTo >= dateFrom;
    }) || null;
}

function formatAbsenceOverlapMsg(conflict) {
    const meta = ABSENCE_LABELS[conflict.absenceType] || { label: conflict.absenceType || 'Absenz' };
    const from = conflict.dateFrom
        ? conflict.dateFrom.slice(8, 10) + '.' + conflict.dateFrom.slice(5, 7) + '.' + conflict.dateFrom.slice(0, 4)
        : '?';
    const to = conflict.dateTo
        ? conflict.dateTo.slice(8, 10) + '.' + conflict.dateTo.slice(5, 7) + '.' + conflict.dateTo.slice(0, 4)
        : '?';
    return `Überlappung mit «${meta.label}» vom ${from}–${to}.\n\n`
        + `Pro Tag ist nur eine Absenz erlaubt.\n`
        + `Während Krankheit / Unfall / Mutterschaft sind keine weiteren Absenzen möglich.\n`
        + `Bei Bedarf die bestehende Absenz aufteilen (z.B. Ferien vor und nach einem Kompensationstag).`;
}

/** AU-Typen: Krankheit / Unfall / Mutter-/Vaterschaft — dürfen nicht unterbrochen werden. */
const ABSENCE_AU_TYPES = new Set(['KRANK', 'UNFALL', 'MUTT_VATER']);

function _absDayAfterIso(iso) {
    if (!iso || iso.length < 10) return null;
    const d = new Date(iso.slice(0, 10) + 'T00:00:00');
    if (Number.isNaN(d.getTime())) return null;
    d.setDate(d.getDate() + 1);
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
}

/**
 * Kritische Bestands-Absenzen (Walter 26.07.2026):
 * 1) Überlappung am selben Kalendertag (Doppel-Eintrag)
 * 2) AU → andere Absenz → direkt wieder AU (Unterbrechung, z.B. Ferien in einer Krankheit)
 * Liefert Map absenzId → unique Gründe.
 */
function analyzeAbsenceCritical(absences) {
    const reasonsById = new Map();
    const mark = (id, reason) => {
        if (id == null) return;
        if (!reasonsById.has(id)) reasonsById.set(id, []);
        const arr = reasonsById.get(id);
        if (!arr.includes(reason)) arr.push(reason);
    };

    const list = Array.isArray(absences) ? absences.slice() : [];
    list.sort((a, b) =>
        String(a.dateFrom || '').localeCompare(String(b.dateFrom || ''))
        || String(a.dateTo || '').localeCompare(String(b.dateTo || ''))
        || (a.id - b.id));

    for (let i = 0; i < list.length; i++) {
        for (let j = i + 1; j < list.length; j++) {
            const a = list[i], b = list[j];
            if (!a.dateFrom || !a.dateTo || !b.dateFrom || !b.dateTo) continue;
            if (a.dateFrom <= b.dateTo && a.dateTo >= b.dateFrom) {
                mark(a.id, 'Überlappung am selben Tag');
                mark(b.id, 'Überlappung am selben Tag');
            }
        }
    }

    for (const mid of list) {
        if (!mid.dateFrom || !mid.dateTo) continue;
        if (ABSENCE_AU_TYPES.has(mid.absenceType)) continue;

        const prev = list.filter(a =>
            ABSENCE_AU_TYPES.has(a.absenceType)
            && a.dateTo
            && _absDayAfterIso(a.dateTo) === mid.dateFrom);
        const next = list.filter(a =>
            ABSENCE_AU_TYPES.has(a.absenceType)
            && a.dateFrom
            && _absDayAfterIso(mid.dateTo) === a.dateFrom);

        if (prev.length && next.length) {
            const midLabel = (ABSENCE_LABELS[mid.absenceType] || {}).label || mid.absenceType || 'Absenz';
            mark(mid.id, `Unterbricht Krankheit/Unfall («${midLabel}» dazwischen)`);
            prev.forEach(p => mark(p.id, 'Unterbrochen durch andere Absenz'));
            next.forEach(n => mark(n.id, 'Fortsetzung direkt nach Unterbrechung'));
        }
    }

    return reasonsById;
}

function renderAbsenzenList(el, absences, employeeId, karenzKrankHist = [], sperrfrist = null, karenzUnfallHist = []) {
    const empModel = selectedEmployee?.employmentModel ?? '';
    const noHours  = empModel === 'FLEX';
    const sperrHtml  = renderSperrfristPanel(sperrfrist);
    // Karenz sitzt rechts unter dem Tagessatz — immer, Krank/Unfall getrennt.
    const karenzSide = document.getElementById('karenzSidebar');
    if (karenzSide) karenzSide.innerHTML = renderKarenzSidebar(karenzKrankHist, karenzUnfallHist);

    const criticalById = analyzeAbsenceCritical(absences);
    const criticalCount = criticalById.size;
    const criticalBanner = criticalCount > 0
        ? `<div class="abs-critical-banner" role="alert">
               <strong>⚠ Kritisch: ${criticalCount} Absenz${criticalCount === 1 ? '' : 'en'} bereinigen</strong>
               <span>Doppelte Tage oder Krankheit/Unfall mit eingeschobener Absenz dazwischen (z.B. Ferien mitten in einer Krankheit). Pro Tag nur eine Absenz — bei Bedarf aufteilen.</span>
           </div>`
        : '';

    let rows = '';
    if (absences.length === 0) {
        rows = `<tr><td colspan="6" style="text-align:center;color:#94a3b8;padding:24px">Keine Absenzen erfasst</td></tr>`;
    } else {
        absences.forEach(a => {
            const meta   = ABSENCE_LABELS[a.absenceType] ?? { label: a.absenceType, color: '' };
            const critReasons = criticalById.get(a.id) || [];
            const isCritical = critReasons.length > 0;
            const prozent = Number(a.prozent ?? 100);
            const typBadge = prozent < 100
                ? `${meta.label} <span style="font-size:10px;opacity:0.85;font-weight:600">${prozent}%</span>`
                : meta.label;
            // Anzahl Kalendertage von .. bis (inklusive)
            let tageStr = '–';
            if (a.dateFrom && a.dateTo) {
                const dFrom = new Date(a.dateFrom + 'T00:00:00');
                const dTo   = new Date(a.dateTo   + 'T00:00:00');
                const days  = Math.floor((dTo - dFrom) / 86400000) + 1;
                if (days > 0) tageStr = `${days} ${days === 1 ? 'Tag' : 'Tage'}`;
            }

            // Für FERIEN MTP = Abzug, sonst Gutschrift
            let hoursCell = '–';
            if (!noHours && a.hoursCredited != null) {
                const isMtpFerien = (empModel === 'MTP' && a.absenceType === 'FERIEN');
                const sign = isMtpFerien ? '−' : '+';
                const cls  = isMtpFerien ? 'abs-hours-neg' : 'abs-hours-pos';
                const label = isMtpFerien ? 'Abzug' : 'Gutschrift';
                hoursCell = `<span class="${cls}">${sign}${Number(a.hoursCredited).toFixed(2)} h</span>
                             <span class="abs-hours-label">${label}</span>`;
            }

            // Krank-/Unfall-Absenzen: direkter Sprung zum Arztzeugnis im
            // Dokumente-Tab. Walter-Vorgabe 09.06.2026 (final): bleibt prominent
            // mit Text „Doku" — das ist eine eigene Aktion (Sprung in den Doku-
            // Tab), nicht „Edit/Delete dieser Zeile", deshalb NICHT ins ⋮-Menü.
            const docBtn = (a.absenceType === 'KRANK' || a.absenceType === 'UNFALL')
                ? `<button type="button" class="abs-dok-btn" title="Arztzeugnis im Dokumente-Tab öffnen"
                           onclick="openAbsenceArztzeugnis()">
                       <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                         <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                         <polyline points="14 2 14 8 20 8"/>
                         <line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><line x1="10" y1="9" x2="8" y2="9"/>
                       </svg>
                       <span>Doku</span>
                   </button>`
                : '';

            // Soft-Lock (Walter Aug 2026): nur wenn Definitiv «abgeschlossen»
            // (DTA) — Flag kommt vom Server (inLohnVerwendet).
            const isLocked = !!a.inLohnVerwendet;
            // Walter-Vorgabe 09.06.2026 (final): nur ⋮-Menü, kein extra Stift —
            // Bearbeiten + Löschen leben im Menü.
            const actionsHtml  = isLocked
                ? `<span style="display:inline-flex;align-items:center;gap:6px">
                       <button type="button" onclick='openAbsenceModal(${JSON.stringify(a).replace(/'/g,"&#39;")}, {readOnly:true})'
                               title="Absenz im Detail ansehen (nur Ansicht)"
                               style="background:#fff;border:1px solid #cbd5e1;border-radius:8px;padding:3px 10px;font-size:11.5px;cursor:pointer;color:#3f3f3f;font-weight:600">👁 Ansehen</button>
                       <span title="Diese Absenz liegt in einer verarbeiteten Lohnperiode und ist nicht mehr editierbar." style="display:inline-flex;align-items:center;gap:4px;font-size:11px;font-weight:600;color:#b91c1c;background:#fee2e2;padding:4px 10px;border-radius:12px;cursor:help;">🔒 In Lohn verwendet</span>
                   </span>`
                : `<div class="dok-menu-wrap">
                       <button type="button" class="dok-menu-btn dok-menu-btn-soft" onclick="absToggleMenu(event, ${a.id})" title="Aktionen" aria-label="Aktionen"><span class="dok-menu-dots" aria-hidden="true"></span></button>
                       <div class="dok-menu" id="absMenu-${a.id}">
                           <button class="dok-menu-item" onclick='openAbsenceModal(${JSON.stringify(a).replace(/'/g,"&#39;")})'>Bearbeiten</button>
                           <button class="dok-menu-item danger" onclick="deleteAbsence(${a.id})">Löschen</button>
                       </div>
                   </div>`;

            const critBadge = isCritical
                ? `<span class="abs-critical-badge" title="${esc(critReasons.join(' · '))}">⚠ Kritisch</span>`
                : '';
            const critHint = isCritical
                ? `<div class="abs-critical-hint">${esc(critReasons.join(' · '))}</div>`
                : '';

            rows += `<tr class="${isCritical ? 'abs-row-critical' : ''}">
                <td>
                    <span class="abs-type-badge ${meta.color}">${typBadge}</span>
                    ${critBadge}
                </td>
                <td>${fmtDate(a.dateFrom)} – ${fmtDate(a.dateTo)}${critHint}</td>
                <td style="white-space:nowrap;color:#475569;font-variant-numeric:tabular-nums">${tageStr}</td>
                <td>${hoursCell}</td>
                <td class="abs-notes">${a.notes ?? ''}</td>
                <td class="abs-actions">
                    ${docBtn}
                    ${actionsHtml}
                </td>
            </tr>`;
        });
    }

    // Walter 19.07.2026: Info-Panels + Spaltenköpfe FIX ausserhalb Scroll
    // (kein sticky) — nur Datenzeilen scrollen (analog Stempelzeiten/Dokumente).
    const colgroup = `
        <colgroup>
            <col class="abs-col-type"><col class="abs-col-period"><col class="abs-col-days">
            <col class="abs-col-hours"><col class="abs-col-notes"><col class="abs-col-actions">
        </colgroup>`;
    el.innerHTML = `
    <div class="abs-list-shell">
        <div class="abs-list-fixed">
            <div class="abs-info-panels">
                ${criticalBanner}
                ${sperrHtml}
            </div>
            <!-- „Absenz erfassen" sitzt jetzt im Header (empTabActionBar) — Walter 01.06.2026 -->
            <div class="abs-toolbar" style="display:none">
                <button class="btn-emp-add" onclick="openAbsenceModal(null)">Absenz erfassen</button>
            </div>
            <div class="abs-cols">
                <table class="abs-table abs-table-head">
                    ${colgroup}
                    <thead><tr>
                        <th>Typ</th><th>Zeitraum</th><th>Tage</th><th>Stunden</th><th>Bemerkung</th><th></th>
                    </tr></thead>
                </table>
            </div>
        </div>
        <div class="abs-list-scroll" id="absListScroll">
            <table class="abs-table abs-table-body">
                ${colgroup}
                <tbody>${rows}</tbody>
            </table>
        </div>
    </div>`;
    absBindScrollIsolation();
}

/** Wheel-Isolation am Listenrand — Titel/Spaltenköpfe springen nicht (Walter 19.07.2026). */
function absBindScrollIsolation() {
    const wrap = document.querySelector('#absenzenContent .abs-list-shell');
    const sc = document.getElementById('absListScroll');
    if (!wrap || !sc || sc.dataset.scrollLock === '1') return;
    sc.dataset.scrollLock = '1';

    // Horizontal-Scroll: Spaltenköpfe mitziehen (wie Dokumente) — sonst laufen
    // Kopf und Sticky-⋮-Spalte auf McBooks auseinander.
    const cols = wrap.querySelector('.abs-cols');
    if (cols) {
        sc.addEventListener('scroll', () => { cols.scrollLeft = sc.scrollLeft; }, { passive: true });
    }

    wrap.addEventListener('wheel', (e) => {
        e.stopPropagation();
        const maxScroll = sc.scrollHeight - sc.clientHeight;
        if (maxScroll <= 0) {
            e.preventDefault();
            return;
        }
        const atTop = sc.scrollTop <= 0;
        const atBottom = sc.scrollTop >= maxScroll - 1;
        if ((atTop && e.deltaY < 0) || (atBottom && e.deltaY > 0)) {
            e.preventDefault();
            return;
        }
        if (!sc.contains(e.target)) {
            sc.scrollTop = Math.min(maxScroll, Math.max(0, sc.scrollTop + e.deltaY));
            e.preventDefault();
        }
    }, { passive: false });
}

// ══════════════════════════════════════════════════════════════════
// SPERRFRIST-PANEL (Kündigungsschutz nach Art. 336c OR)
// ══════════════════════════════════════════════════════════════════
// Schutz gilt nur solange tatsächlich AU besteht — höchstens 30/90/180
// Tage (je Dienstjahr). Erster AU-Tag = Sperrtag 1 (inklusiv).
/**
 * Frühester letzter Arbeitstag bei Kündigung heute.
 * L-GAV Gastgewerbe (Walter 25.07.2026): 1.–5. Dienstjahr = 1 Monat,
 * ab 6. Dienstjahr = 2 Monate — jeweils auf Monatsende.
 * (Nicht OR 335c mit 2 Monaten ab 2. Jahr.)
 */
function _sperrKuendPerEndeMonat(dienstjahr, kuendAbIso) {
    const dj = dienstjahr || 1;
    const months = dj >= 6 ? 2 : 1;
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    let kdat = today;
    if (kuendAbIso) {
        const ab = new Date(String(kuendAbIso).slice(0, 10) + 'T00:00:00');
        if (!isNaN(ab) && ab > today) kdat = ab;
    }
    // 1. des Monats + months + 1 Monat − 1 Tag = Monatsende nach Frist
    const per = new Date(kdat.getFullYear(), kdat.getMonth() + months + 1, 0);
    const dd = String(per.getDate()).padStart(2, '0');
    const mm = String(per.getMonth() + 1).padStart(2, '0');
    return { per, months, perTxt: `${dd}.${mm}.${per.getFullYear()}` };
}

function renderSperrfristPanel(info) {
    if (!info) return '';

    const wrap = (color, bg, border, inner) => `
        <div style="background:${bg};border:1px solid ${border};border-radius:10px;padding:12px 16px;margin-bottom:8px;font-size:13.5px">
            <div style="font-size:11px;font-weight:700;color:${color};text-transform:uppercase;letter-spacing:0.08em;margin-bottom:6px">Kündigungsschutz</div>
            ${inner}
        </div>`;

    const status = info.status;
    const dj = info.dienstjahrAmStichtag || 1;
    const djText = info.dienstjahrAmStichtag ? `${info.dienstjahrAmStichtag}. Dienstjahr` : '–';
    const isMts  = info.auGrund === 'MUTTERSCHAFT';
    const grundLabel = info.auGrund === 'UNFALL' ? 'Unfall'
                    : info.auGrund === 'KRANK+UNFALL' ? 'Krankheit + Unfall'
                    : info.auGrund === 'MUTTERSCHAFT' ? 'Mutterschaft'
                    : (info.auGrund ? 'Krankheit' : '');
    const maxTage = info.sperrfristTage || null;
    let auTage = info.auDauerTage || 0;
    if (!auTage && info.auBeginn && info.auEnde) {
        const a = new Date(String(info.auBeginn).slice(0, 10) + 'T00:00:00');
        const b = new Date(String(info.auEnde).slice(0, 10) + 'T00:00:00');
        if (!isNaN(a) && !isNaN(b)) auTage = Math.round((b - a) / 86400000) + 1;
    }
    const chainKurz = (info.auBeginn && info.auEnde)
        ? `${fmtDate(info.auBeginn)} – ${fmtDate(info.auEnde)}`
        : '';
    // Walter 25.07.2026: Leitsatz «X Tage ununterbrochen krank · Kündigung per Ende Monat»
    const kuendPerInfo = _sperrKuendPerEndeMonat(dj, info.kuendigungAbDatum);
    const kuendPerTxt = kuendPerInfo.perTxt;
    const heroKuendbar = auTage > 0
        ? `<b>${auTage} Tage</b> ununterbrochen krank${chainKurz ? ` (${chainKurz})` : ''}. Kündigung per <b>${kuendPerTxt}</b> (Ende Monat) möglich.`
        : `Kündigung per <b>${kuendPerTxt}</b> (Ende Monat) möglich.`;

    // Walter 25.07.2026: Banner nur bei laufender AU (Krank/Unfall) wenn die
    // Sperrfrist erreicht/ausgeschöpft ist — nach AU-Ende immer normale L-GAV-
    // Kündigung, kein Sonderbanner. Mutterschaft bleibt sichtbar.
    if (status === 'KEINE_AU' || status === 'KEIN_EINTRITT' || status === 'IN_PROBEZEIT')
        return '';
    if (!isMts
        && status !== 'SPERRFRIST_ABGELAUFEN'
        && !(status === 'GESCHUETZT' && maxTage && auTage >= maxTage))
        return '';

    const isGeschuetzt = status === 'GESCHUETZT';
    const isKuendbar = status === 'SPERRFRIST_ABGELAUFEN';
    const color  = isGeschuetzt ? '#b91c1c' : '#166534';
    const bg     = isGeschuetzt ? '#fef2f2' : '#f0fdf4';
    const border = isGeschuetzt ? '#fecaca' : '#86efac';

    // Mutterschaft: festes Ende (Geburt + 16 Wochen) — eigene Formulierung
    if (isMts) {
        return wrap(color, bg, border, `
            <div style="display:flex;gap:14px;align-items:flex-start;flex-wrap:wrap">
                <div style="flex:1;min-width:240px">
                    <div style="font-weight:700;color:#0f172a;font-size:14px;line-height:1.35">
                        Kündigungsschutz wegen Schwangerschaft/Mutterschaft
                    </div>
                    <div style="color:#475569;margin-top:6px;line-height:1.45">
                        Geschützt bis <b>${fmtDate(info.sperrfristEnde)}</b> —
                        Kündigung frühestens ab <b style="color:${color}">${fmtDate(info.kuendigungAbDatum)}</b>.
                        ${info.hinweis ? `<div style="margin-top:6px;font-size:12px">${esc(info.hinweis)}</div>` : ''}
                    </div>
                </div>
                ${isGeschuetzt ? `<div style="text-align:right;min-width:120px">
                    <div style="font-size:22px;font-weight:700;color:${color}">${info.verbleibendeTage ?? '–'}</div>
                    <div style="font-size:11px;color:#64748b">Tage bis kündbar</div>
                </div>` : `<div style="font-size:13px;font-weight:700;color:${color}">✓ Schutz abgelaufen</div>`}
            </div>`);
    }

    const sperrTag = info.auDauerTage || auTage || 0;
    const aktuellBis = info.aktuellGeschuetztBis || info.auEnde;
    const maxBis = info.sperrfristEnde;
    const kuendAb = info.kuendigungAbDatum;
    const chainLine = chainKurz
        ? `Durchgehende AU-Kette <b>${chainKurz}</b>` + (grundLabel ? ` (${grundLabel})` : '')
        : '';

    // Walter 25.07.2026: Leitsatz mit Tagen + Kündigung per Monatsende
    if (isKuendbar) {
        return wrap('#166534', '#ecfdf5', '#6ee7b7', `
            <div style="display:flex;gap:14px;align-items:flex-start;flex-wrap:wrap">
                <div style="flex:1;min-width:260px">
                    <div style="font-weight:800;color:#14532d;font-size:15px;line-height:1.4">
                        ${heroKuendbar}
                    </div>
                    <div style="color:#475569;margin-top:8px;line-height:1.5;font-size:12.5px">
                        ${djText} · Sperrfrist (${maxTage || '–'} Tage) endete am ${fmtDate(maxBis)}
                        ${kuendAb ? ` · kündbar seit ${fmtDate(kuendAb)}` : ''}
                        · Kündigungsfrist ${kuendPerInfo.months} Monat(e) auf Ende Monat (L-GAV).
                        Karenz/Lohnfortzahlung ist davon unabhängig.
                    </div>
                    ${info.hinweis ? `<div style="margin-top:6px;padding:8px 10px;background:#fffbeb;border:1px solid #fde68a;border-radius:6px;color:#78350f;font-size:11px">⚠︎ ${esc(info.hinweis)}</div>` : ''}
                </div>
                <div style="text-align:right;min-width:120px">
                    <div style="font-size:13px;font-weight:800;color:#14532d;background:#bbf7d0;padding:8px 10px;border-radius:8px">KÜNDBAR</div>
                    <div style="font-size:11px;color:#64748b;margin-top:6px">per ${kuendPerTxt}</div>
                </div>
            </div>`);
    }

    // GESCHUETZT (Krankheit/Unfall): aktuell vs. maximal klar trennen
    return wrap(color, bg, border, `
        <div style="display:flex;gap:14px;align-items:flex-start;flex-wrap:wrap">
            <div style="flex:1;min-width:260px">
                <div style="font-weight:700;color:#0f172a;font-size:14px;line-height:1.35">
                    Aktuell kündigungsgeschützt aufgrund Arbeitsunfähigkeit
                </div>
                <div style="color:#334155;margin-top:8px;line-height:1.5">
                    ${chainLine || `Ärztlich bestätigte Arbeitsunfähigkeit bis <b>${fmtDate(aktuellBis)}</b>`}.
                    ${aktuellBis && info.auEnde && aktuellBis !== info.auEnde ? '' : ''}
                </div>
                <div style="color:#475569;margin-top:6px;line-height:1.5;font-size:13px">
                    Bei durchgehender Arbeitsunfähigkeit maximale Sperrfrist bis <b>${fmtDate(maxBis)}</b>
                    — Kündigung frühestens ab <b style="color:${color}">${fmtDate(kuendAb)}</b>
                    (${djText}${maxTage ? `, max. ${maxTage} Tage` : ''}).
                </div>
                <div style="color:#94a3b8;margin-top:6px;font-size:11.5px;line-height:1.4">
                    Hinweis: Krankheits-Karenz / Lohnfortzahlung ist eine separate Berechnung und beendet den Kündigungsschutz nicht.
                </div>
                ${info.hinweis ? `<div style="margin-top:6px;padding:8px 10px;background:#fffbeb;border:1px solid #fde68a;border-radius:6px;color:#78350f;font-size:11px">⚠︎ ${esc(info.hinweis)}</div>` : ''}
            </div>
            <div style="text-align:right;min-width:130px">
                <div style="font-size:22px;font-weight:700;color:${color}">${sperrTag}<span style="font-size:14px;font-weight:600;color:#94a3b8"> / ${maxTage || '–'}</span></div>
                <div style="font-size:11px;color:#64748b">Sperrtag von maximal ${maxTage || '–'}</div>
            </div>
        </div>`);
}

// ══════════════════════════════════════════════════════════════════
// KARENZ-PANEL (Krankheit + Unfall)
// ══════════════════════════════════════════════════════════════════
// Walter 25.07.2026: Karenz IMMER zeigen (kompakt rechts unter Tagessatz).
// Krank und Unfall getrennt, nie zusammengezählt. Keine Vorjahre/Details.
function renderKarenzSidebar(krankHist, unfallHist) {
    return [
        renderKarenzCard(krankHist,  { label: 'Krankheits-Karenz', dayLabel: 'Kranktage',  defaultMax: 14 }),
        renderKarenzCard(unfallHist, { label: 'Unfall-Karenz',     dayLabel: 'Unfalltage', defaultMax: 2  }),
    ].join('');
}

function _todayIsoLocal() {
    const t = new Date();
    return `${t.getFullYear()}-${String(t.getMonth() + 1).padStart(2, '0')}-${String(t.getDate()).padStart(2, '0')}`;
}

function _pickCurrentKarenzJahr(history) {
    if (!Array.isArray(history) || history.length === 0) return null;
    const iso = _todayIsoLocal();
    return history.find(h => h.info && h.info.von <= iso && h.info.bis >= iso) || history[0];
}

function renderKarenzCard(history, cfg) {
    const current = _pickCurrentKarenzJahr(history);
    if (!current?.info) return '';

    const curInfo = current.info;
    const max  = Number(curInfo.tageMax) || cfg.defaultMax;
    const used = Number(curInfo.tageVerbraucht) || 0;
    const erreicht = !!curInfo.grenzErreichtAm;
    // Walter 25.07.2026: Tage im Arbeitsjahr (nicht Anzahl Fälle).
    const usedTxt = used.toLocaleString('de-CH', {
        minimumFractionDigits: Number.isInteger(used) ? 0 : 2,
        maximumFractionDigits: 2,
    });
    const statusTxt = erreicht
        ? `Grenze ${max} Tage erreicht (${fmtDate(curInfo.grenzErreichtAm)})`
        : `Grenze offen (${usedTxt} / ${max} Tage)`;

    return `
    <div class="karenz-side-card ${erreicht ? 'karenz-reached' : 'karenz-ok'}">
        <div class="karenz-side-label">${cfg.label}</div>
        <div class="karenz-side-year">${fmtDate(curInfo.von)} – ${fmtDate(curInfo.bis)}</div>
        <div class="karenz-side-count">
            <strong>${usedTxt}</strong>
            <span>${cfg.dayLabel} in diesem Arbeitsjahr</span>
        </div>
        <div class="karenz-side-status">${statusTxt}</div>
    </div>`;
}

function fmtDate(iso) {
    if (!iso) return '–';
    const d = new Date(iso + 'T00:00:00');
    return d.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

// ── Absenz-Typen-Cache ─────────────────────────────────────────
// Laden einmal pro Modal-Öffnung; Cache wird bei jedem openAbsenceModal
// zurückgesetzt, damit Admin-Änderungen sichtbar werden.
let _absenzTypenCache = null;
async function getAbsenzTypen() {
    if (_absenzTypenCache) return _absenzTypenCache;
    try {
        const res = await fetch('/api/absenz-typen', { headers: ah() });
        if (!res.ok) return [];
        _absenzTypenCache = await res.json();
        return _absenzTypenCache;
    } catch {
        return [];
    }
}

// ── Absenz-Modal ───────────────────────────────────────────────
async function openAbsenceModal(existing, opts) {
    _absenzTypenCache = null;  // Cache invalidieren → frische Konfig holen
    const modal = document.getElementById('absenceModal');
    if (!modal) return;
    modal.style.display = 'flex';

    // Nur-Ansicht-Modus (Walter 13.08.2026): gesperrte Absenzen («In Lohn
    // verwendet») dürfen im Detail angeschaut, aber nicht geändert werden.
    const absReadOnly = !!(opts && opts.readOnly);
    modal.classList.toggle('abs-ro', absReadOnly);
    const absRoBanner = document.getElementById('absReadOnlyBanner');
    if (absRoBanner) absRoBanner.style.display = absReadOnly ? '' : 'none';
    const absSaveBtn = document.getElementById('absSaveBtn');
    if (absSaveBtn) absSaveBtn.style.display = absReadOnly ? 'none' : '';

    // Typen aus DB laden und Dropdown befüllen
    const typen = await getAbsenzTypen();
    const sel = document.getElementById('absTypeSelect');
    const currentVal = existing?.absenceType ?? 'KRANK';
    sel.innerHTML = typen.map(t =>
        `<option value="${t.code}" ${t.code === currentVal ? 'selected' : ''}>${t.bezeichnung}</option>`
    ).join('');

    // Aktive Lohnperiode der MA-Filiale ermitteln, um die Datums-Auswahl auf
    // diese Periode zu begrenzen (Walter-Wunsch: nicht versehentlich in eine
    // andere Periode erfassen). Beim Edit lassen wir's offen, weil die
    // bestehende Absenz auch ausserhalb der heutigen Periode liegen kann.
    const isNewAbs = !existing;
    let absPeriodFrom = '', absPeriodTo = '';
    if (isNewAbs) {
        const cid = (typeof selectedEmployee !== 'undefined' && selectedEmployee?.companyProfileId)
                  ?? (typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null);
        if (cid) {
            try {
                const todayIso = new Date().toISOString().slice(0, 10);
                const y = parseInt(todayIso.slice(0, 4), 10);
                const m = parseInt(todayIso.slice(5, 7), 10);
                const r = await fetch(`/api/payroll-perioden/current?companyProfileId=${cid}&year=${y}&month=${m}`,
                    { headers: { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` } });
                if (r.ok) {
                    const txt = await r.text();
                    if (txt && txt.trim() && txt.trim() !== 'null') {
                        const p = JSON.parse(txt);
                        absPeriodFrom = (p.periodFrom || '').slice(0, 10);
                        absPeriodTo   = (p.periodTo   || '').slice(0, 10);
                    }
                }
            } catch {}
        }
    }
    const fromEl = document.getElementById('absDateFrom');
    const toEl   = document.getElementById('absDateTo');
    // Walter-Vorgabe 27.06.2026: KEINE Begrenzung mehr auf die aktuelle Periode.
    // Absenzen dürfen in jede noch NICHT definitiv abgeschlossene Periode (auch
    // rückwirkend). Die Sperre prüft der Server per-Periode beim Speichern und
    // meldet eine abgeschlossene Periode mit klarer Fehlermeldung (LOHN_EDIT_LOCKED).
    fromEl.removeAttribute('min'); fromEl.removeAttribute('max');
    toEl.removeAttribute('min');   toEl.removeAttribute('max');

    // Reset form — bei neuer Absenz: heutiges Datum (oder periodFrom falls
    // heute ausserhalb der Periode liegt) in Von/Bis vorbelegen.
    const today = new Date().toISOString().slice(0, 10);
    let defaultDate = today;
    if (absPeriodFrom && absPeriodTo && (today < absPeriodFrom || today > absPeriodTo)) {
        defaultDate = absPeriodFrom;
    }
    document.getElementById('absTypeSelect').value   = currentVal;
    document.getElementById('absDateFrom').value      = existing?.dateFrom ?? defaultDate;
    document.getElementById('absDateTo').value        = existing?.dateTo   ?? defaultDate;
    document.getElementById('absProzent').value       = existing?.prozent ?? 100;
    document.getElementById('absNotes').value         = existing?.notes ?? '';
    document.getElementById('absModalTitle').textContent = existing ? 'Absenz bearbeiten' : 'Absenz erfassen';
    document.getElementById('absenceModal').dataset.editId = existing?.id ?? '';

    // Pre-select worked days if editing
    window._absEditWorkedDays = existing?.workedDays ? JSON.parse(existing.workedDays) : [];
    window._absIsNew = isNewAbs;
    window._absUserTouchedDays = false;
    window._absContinuationHint = '';

    // Neue Krank/Unfall direkt im Anschluss an Vormonat → Mo–Fr erzwingen
    // (Sa/So frei), analog Import-Fix Walter 26.07.2026.
    if (isNewAbs && (currentVal === 'KRANK' || currentVal === 'UNFALL')) {
        await _absApplyContinuationMoFr(currentVal, document.getElementById('absDateFrom').value);
    }

    renderAbsDayCheckboxes();
    calcAbsHoursPreview();
}

/** Fortlaufende AU: Absenz endet am Vortag von dateFrom → Mo–Fr vorwählen. */
async function _absApplyContinuationMoFr(type, dateFrom) {
    window._absContinuationHint = '';
    const empId = typeof selectedEmployeeId !== 'undefined' ? selectedEmployeeId : null;
    if (!empId || !dateFrom || !type) return;
    try {
        const res = await fetch(`/api/absences/employee/${empId}`, { headers: ah() });
        if (!res.ok) return;
        const list = await res.json();
        const from = new Date(dateFrom + 'T00:00:00');
        const prevDay = new Date(from);
        prevDay.setDate(prevDay.getDate() - 1);
        const prevIso = localIso(prevDay);
        const prev = (list || []).find(a =>
            a.absenceType === type
            && a.dateTo
            && String(a.dateTo).slice(0, 10) === prevIso);
        if (!prev) return;
        // Mo–Fr für den neuen Zeitraum vorwählen (Sa/So frei) — Muster Vormonat.
        const toEl = document.getElementById('absDateTo');
        const to = (toEl && toEl.value) || dateFrom;
        const days = [];
        let cur = new Date(dateFrom + 'T00:00:00');
        const end = new Date(to + 'T00:00:00');
        while (cur <= end) {
            const dow = cur.getDay();
            if (dow >= 1 && dow <= 5) days.push(localIso(cur));
            cur.setDate(cur.getDate() + 1);
        }
        window._absEditWorkedDays = days;
        window._absContinuationHint =
            'Fortsetzung Vormonat erkannt — Mo–Fr markiert, Sa/So frei (wie lange Krankheit).';
    } catch { /* ignore */ }
}

function closeAbsenceModal() {
    const modal = document.getElementById('absenceModal');
    if (modal) { modal.style.display = 'none'; modal.dataset.editId = ''; }
    window._absEditWorkedDays = [];
    window._absIsNew = false;
    window._absUserTouchedDays = false;
    window._absContinuationHint = '';
}

// Wenn Von-Datum geändert wird: Bis-Datum automatisch auf dasselbe Datum
// setzen (typischer 1-Tages-Fall). Für Mehrtages-Absenzen passt der User
// Bis danach manuell an.
function syncAbsDateTo() {
    const fromEl = document.getElementById('absDateFrom');
    const toEl   = document.getElementById('absDateTo');
    if (!fromEl || !toEl) return;
    if (!fromEl.value) return;
    toEl.value = fromEl.value;
}

async function renderAbsDayCheckboxes() {
    const from  = document.getElementById('absDateFrom').value;
    const to    = document.getElementById('absDateTo').value;
    const type  = document.getElementById('absTypeSelect').value;
    const box   = document.getElementById('absDayCheckboxes');
    if (!box) return;

    if (!from || !to || from > to) { box.innerHTML = ''; return; }

    // Neue Krank/Unfall: bei Datums-/Typ-Wechsel Fortsetzung Vormonat neu prüfen
    // (solange User kein eigenes Muster gewählt hat).
    if (window._absIsNew && (type === 'KRANK' || type === 'UNFALL') && !window._absUserTouchedDays) {
        await _absApplyContinuationMoFr(type, from);
        // Zeitraum Bis kann länger sein als beim ersten Apply — Mo–Fr neu aufbauen.
        if (window._absContinuationHint) {
            const days = [];
            let cur = new Date(from + 'T00:00:00');
            const end = new Date(to + 'T00:00:00');
            while (cur <= end) {
                const dow = cur.getDay();
                if (dow >= 1 && dow <= 5) days.push(localIso(cur));
                cur.setDate(cur.getDate() + 1);
            }
            window._absEditWorkedDays = days;
        }
    }

    const dayNames = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];
    const preselect = window._absEditWorkedDays ?? [];

    // Alle Tage im Zeitraum aufzählen
    const days = [];
    let cur = new Date(from + 'T00:00:00');
    const end = new Date(to + 'T00:00:00');
    while (cur <= end) {
        days.push(new Date(cur));
        cur.setDate(cur.getDate() + 1);
    }

    if (type !== 'KRANK' && type !== 'UNFALL') {
        // Walter-Vorgabe 27.06.2026: Das Tage-Raster gibt es NUR bei Krankheit
        // und Unfall (dort wird im 1/5 gerechnet, also zählt welche Tage der MA
        // gearbeitet hätte). Alle anderen Typen (Ferien, Feiertag, unbezahlter
        // Urlaub, Schulung …) überspringen das Raster komplett — es zählen
        // automatisch alle Tage im Zeitraum (das Backend rechnet je nach Typ-
        // Konfiguration). Nur ein Info-Text.
        box.innerHTML = `<div class="abs-day-info">Alle ${days.length} Tag(e) im Zeitraum werden automatisch verbucht — ein Tage-Raster gibt es nur bei Krankheit/Unfall (1/5-Berechnung).</div>`;
        calcAbsHoursPreview();
        return;
    }

    // KRANK / UNFALL: Tage auswählen (1/5).
    // Walter-Vorgabe 28.05.2026: 7-Spalten-Wochenraster Mo-So. Default-
    // Vorauswahl: Mo–Fr markiert, Sa+So NICHT markiert (typische 5-Tage-Woche).
    // Walter 26.07.2026: bei fortlaufender Krankheit (Anschluss Vormonat)
    // immer Mo–Fr / Sa+So frei — nicht «erste 5 Tage ab Periodenstart».
    // Walter-Vorgabe 30.05.2026: Sa+So klickbar + Schnellauswahl:
    //   • "Mo–Fr Muster"        = Default / lange Krankheit (Sa+So frei)
    //   • "5 gearbeitet / 2 frei" = Schicht-Rhythmus ab Tag 1 (nur manuell)
    //   • "Alle Tage" / "Keine"
    const contHint = window._absContinuationHint
        ? `<div class="abs-day-info" style="margin-bottom:8px;color:#166534;background:#f0fdf4;border:1px solid #bbf7d0;padding:8px 10px;border-radius:8px;font-size:12px">${window._absContinuationHint}</div>`
        : '';
    let html = contHint
             + '<div class="abs-day-label">Welche Tage hätte der/die Mitarbeitende gearbeitet?</div>'
             + '<div class="abs-day-quick">'
             + '<button type="button" onclick="absDayPreset(\'mofr\')">Mo–Fr Muster</button>'
             + '<button type="button" onclick="absDayPreset(\'5and2\')">5 gearbeitet / 2 frei</button>'
             + '<button type="button" onclick="absDayPreset(\'all\')">Alle Tage</button>'
             + '<button type="button" onclick="absDayPreset(\'none\')">Keine</button>'
             + '</div>'
             + '<div class="abs-day-grid-head">'
             + '<div>Mo</div><div>Di</div><div>Mi</div><div>Do</div><div>Fr</div><div>Sa</div><div>So</div>'
             + '</div>'
             + '<div class="abs-day-grid">';

    // Versatz vorne: leere Zellen für die Tage vor dem ersten Datum,
    // damit der erste Eintrag in der korrekten Wochentag-Spalte landet.
    // getDay: 0=So, 1=Mo, ..., 6=Sa  →  Mo-Index: (dow+6) % 7
    const firstDow = days[0].getDay();
    const firstMondayOffset = (firstDow + 6) % 7;
    for (let i = 0; i < firstMondayOffset; i++) {
        html += '<div class="abs-day-item abs-day-empty"></div>';
    }

    days.forEach(d => {
        const iso     = localIso(d);
        const dow     = d.getDay();
        const weekday = dayNames[dow];
        const dateStr = d.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit' });
        const isSaSo  = dow === 0 || dow === 6;
        const chk = preselect.length > 0
            ? (preselect.includes(iso) ? 'checked' : '')
            : (isSaSo ? '' : 'checked');
        html += `<label class="abs-day-item${isSaSo ? ' abs-day-weekend' : ''}">
            <input type="checkbox" value="${iso}" ${chk} onchange="window._absUserTouchedDays=true;calcAbsHoursPreview()">
            <span class="abs-day-name">${weekday}</span>
            <span class="abs-day-date">${dateStr}</span>
        </label>`;
    });
    html += '</div>';
    // Legende (Walter 13.08.2026): klarmachen, welche Tage zählen.
    html += `<div style="margin-top:8px;font-size:11.5px;color:#64748b;display:flex;gap:16px;flex-wrap:wrap">
        <span><span style="display:inline-block;width:12px;height:12px;background:#6b6152;border-radius:4px;vertical-align:-2px"></span> dunkel = hätte gearbeitet → zählt (× Wochenstunden ÷ 5)</span>
        <span><span style="display:inline-block;width:12px;height:12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:4px;vertical-align:-2px"></span> hell = nicht eingeplant → zählt 0 h</span>
    </div>`;
    box.innerHTML = html;
    calcAbsHoursPreview();
}

// Walter-Vorgabe 30.05.2026: Schnellauswahl-Muster für die Krank-/Unfall-
// Tage-Auswahl. Sa+So sind in der Gastro normale Arbeitstage, daher braucht
// es flexible Vorlagen.
//   • mofr   = Mo–Fr markiert, Sa+So frei (Default für klassische 5-Tage-Woche)
//   • 5and2  = ab erstem Tag: 5 gearbeitet / 2 frei wiederholend (unabhängig
//              vom Wochentag — passend für Schichtbetriebe mit festem Rhythmus)
//   • all    = alle Tage markiert (7-Tage-Woche)
//   • none   = alle Tage abgewählt (User klickt manuell)
function absDayPreset(mode) {
    window._absUserTouchedDays = true;
    window._absContinuationHint = '';
    const boxes = document.querySelectorAll('#absDayCheckboxes input[type=checkbox]');
    let idx = 0;  // läuft nur über echte Tage (nicht über die abs-day-empty-Spacer)
    boxes.forEach(cb => {
        const iso = cb.value;
        if (!iso) return;
        const d = new Date(iso + 'T00:00:00');
        const dow = d.getDay();  // 0=So, 1=Mo, ..., 6=Sa
        if (mode === 'mofr') {
            cb.checked = (dow >= 1 && dow <= 5);
        } else if (mode === '5and2') {
            // Position im 7-Tage-Zyklus: Tage 1-5 markiert, Tage 6-7 frei
            // Achtung: startet am Periodenanfang — bei Monatswechsel/Fortsetzung
            // lieber «Mo–Fr Muster» nutzen (Walter 26.07.2026).
            cb.checked = (idx % 7) < 5;
        } else if (mode === 'all') {
            cb.checked = true;
        } else if (mode === 'none') {
            cb.checked = false;
        }
        idx++;
    });
    if (typeof calcAbsHoursPreview === 'function') calcAbsHoursPreview();
}

function getAbsWorkedDays() {
    const type = document.getElementById('absTypeSelect').value;
    const from = document.getElementById('absDateFrom').value;
    const to   = document.getElementById('absDateTo').value;

    // Walter-Vorgabe 27.06.2026: NUR Krankheit/Unfall nutzen das Tage-Raster
    // (angekreuzte Tage, 1/5). ALLE anderen Typen (Ferien, Feiertag, unbezahlter
    // Urlaub, Schulung …) zählen automatisch alle Tage im Zeitraum.
    if (type !== 'KRANK' && type !== 'UNFALL') {
        const days = [];
        if (from && to && from <= to) {
            let cur = new Date(from + 'T00:00:00');
            const end = new Date(to + 'T00:00:00');
            while (cur <= end) {
                days.push(localIso(cur));
                cur.setDate(cur.getDate() + 1);
            }
        }
        return days;
    }

    // KRANK / UNFALL: angekreuzte Tage
    return [...document.querySelectorAll('#absDayCheckboxes input[type=checkbox]:checked')]
        .map(cb => cb.value);
}

async function calcAbsHoursPreview() {
    const type     = document.getElementById('absTypeSelect').value;
    const empModel = selectedEmployee?.employmentModel ?? '';
    const previewEl = document.getElementById('absHoursPreview');
    if (!previewEl) return;

    const workedDays = getAbsWorkedDays();
    const count      = workedDays.length;

    // Ausfall-Prozent (Default 100). Wird multiplikativ auf die Stunden
    // angewendet — 50 = halb krank/abwesend, etc.
    let prozent = Number(document.getElementById('absProzent')?.value ?? 100);
    if (!Number.isFinite(prozent) || prozent <= 0) prozent = 100;
    if (prozent > 100) prozent = 100;
    const pFactor   = prozent / 100;
    const pSuffix   = prozent < 100 ? ` × ${prozent}%` : '';

    // Konfiguration für diesen Absenz-Typ aus Cache laden
    const typen = await getAbsenzTypen();
    const typCfg = typen.find(t => t.code === type);
    let modus            = typCfg?.gutschriftModus ?? '1/5';
    let hatGutschrift    = typCfg?.zeitgutschrift  ?? true;
    const utpAuszahlung  = typCfg?.utpAuszahlung   ?? false;
    const basisStunden   = typCfg?.basisStunden    ?? 'BETRIEB';
    const reduziertSaldo = typCfg?.reduziertSaldo  ?? null;

    // FIX/FIX-M bei FEIERTAG: AbsenzTyp-Konfig (gutschriftModus aus
    // Datenbank) wird jetzt respektiert — vorher hartkodiert auf 1/7,
    // war Legacy-Regel die obsolete ist seit der Saldo-Mechanik. Zusätzlich
    // erscheint im Hinweis der Saldo-Bezug (Feiertag-Saldo wird im
    // PayrollController-Block automatisch um die bezogenen Tage reduziert).

    // MTP/UTP bei FEIERTAG: keine Zeitgutschrift, kein Saldo. Beide sind
    // stundenbasierte Modelle (UTP rein nach gestempelten Stunden, MTP mit
    // garantiertem Mindestpensum). Feiertagentschädigung wird als % auf den
    // Stundenlohn berechnet und jeden Monat mit dem Lohn ausbezahlt — daher
    // kein Eintrag in den Arbeitsstunden-Saldo und kein Feiertag-Tage-Saldo.
    if ((empModel === 'MTP' || empModel === 'FLEX') && type === 'FEIERTAG') {
        previewEl.innerHTML = `<span class="abs-hours-label">${empModel}: Feiertagentschädigung wird als % monatlich mit dem Lohn ausbezahlt (kein Saldo-Eintrag)</span>`;
        previewEl.dataset.hours = '0';
        return;
    }

    // MTP und UTP bei FERIEN: KEINE Zeitgutschrift. Stattdessen:
    //   - MTP: Garantie-Festlohn (10.5) wird um die Ferientage gekürzt;
    //          Ferien-Auszahlung anteilig aus Ferien-Geld-Saldo (CHF).
    //   - FLEX: Auszahlung anteilig aus Ferien-Geld-Saldo (CHF) —
    //          sofern der Firmenparameter "Feriengeld auf Konto"
    //          aktiv ist (sonst wird Ferien mit Stundenlohn ausbezahlt).
    //   Beide: Ferien-Tage-Saldo wird um die bezogenen Ferientage reduziert.
    if ((empModel === 'MTP' || empModel === 'FLEX') && type === 'FERIEN') {
        const modellInfo = empModel === 'MTP'
            ? 'Festlohn wird um diese Tage gekürzt, Auszahlung aus Ferien-Geld-Saldo (CHF)'
            : 'Auszahlung anteilig aus Ferien-Geld-Saldo (CHF)';
        previewEl.innerHTML = `<span class="abs-hours-label">${empModel}: ${count} Ferientag${count > 1 ? 'e' : ''} — ${modellInfo}. Ferien-Tage-Saldo -${count}. (Keine Stunden-Gutschrift)</span>`;
        previewEl.dataset.hours = '0';
        return;
    }

    // Wochenstunden-Basis pro AbsenzTyp (CH-Payroll-Regel):
    //   BETRIEB = Filial-NormalWeeklyHours (42 h Default)
    //   VERTRAG = Modell + Typ-abhängig:
    //     MTP       → GuaranteedHoursPerWeek (z.B. 33 h/Woche)
    //                  → alle Typen: Krank/Unfall/Ferien-Gutschrift basieren
    //                    auf der Garantie (nicht auf Betriebs-Wochen).
    //     FIX/FIX-M → Spezialregel:
    //                  FERIEN und FEIERTAG: pensum-adjustiertes Wochensoll
    //                    (1/7 × WeeklyHours bzw. betriebWeekly × Pensum/100)
    //                  KRANK und UNFALL: volle Betriebs-Wochen (1/5 × 42h)
    //                    — entspricht Walter's Vorgabe:
    //                    "bei FIX und FIX-M immer 1/5 der Betriebszeit"
    //     UTP       → Betrieb (Fallback; Gutschrift bei UTP ist selten)
    const betriebWeekly = Number(selectedCompanyProfile?.normalWeeklyHours ?? 42);
    let weeklyH = betriebWeekly;
    // Walter-Vorgabe 30.05.2026 (override): bei MTP IMMER die garantierten
    // Wochenstunden als Basis — unabhängig vom AbsenzTyp-Setting basisStunden.
    // Stundenlöhner-Modell: nur die Garantie ist der Maßstab, nicht der Betrieb.
    if (empModel === 'MTP') {
        weeklyH = Number(selectedEmployee?.guaranteedHoursPerWeek
                      ?? selectedEmployee?.weeklyHours
                      ?? betriebWeekly);
    } else if (basisStunden === 'VERTRAG') {
        if (empModel === 'FIX' || empModel === 'FIX-M') {
            // NUR bei FERIEN/FEIERTAG pensum-adjustiert, sonst Betrieb.
            if (type === 'FERIEN' || type === 'FEIERTAG') {
                const pct = Number(selectedEmployee?.employmentPercentage ?? 100);
                weeklyH = Number(selectedEmployee?.weeklyHours
                              ?? (betriebWeekly * pct / 100));
            }
            // KRANK, UNFALL, SCHULUNG, MILITAER: weeklyH bleibt auf betriebWeekly
        }
        // FLEX: bleibt auf betriebWeekly
    }

    let hours = 0;
    let hint  = '';

    // Nacht-Kompensation = bezahlter Freitag (1/5 Wochenstunden), für ALLE
    // Modelle inkl. FLEX. Nachtzuschlag selbst wird nie ausbezahlt (nur Saldo);
    // Ausnahme Austritt. Walter 02.08.2026.
    if (reduziertSaldo === 'NACHT_STUNDEN') {
        hours = count * (weeklyH / 5) * pFactor;
        const saldoHint = empModel === 'FLEX'
            ? 'bezahlter Freitag als Stundenlohn, Nacht-Saldo sinkt'
            : 'bezahlter Freitag (Ist-Stunden), Nacht-Saldo sinkt';
        hint  = `<span class="abs-hours-pos">+${hours.toFixed(2)} h</span> <span class="abs-hours-label">${typCfg?.bezeichnung ?? type}: ${count} Tag${count>1?'e':''} × ${weeklyH.toFixed(2)} h ÷ 5${pSuffix} → ${saldoHint}</span>`;
        // Warnung ab > 9 h (mehr als ein typischer Komp-Tag bei 42h-Woche).
        if (hours > 9) {
            hint += `<br><span class="abs-hours-label" style="color:#b45309;font-weight:600">⚠ Mehr als 9 Stunden Kompensation — üblich ist 1 bezahlter Freitag (1/5 Wochenarbeitszeit). Bitte prüfen.</span>`;
        }
        previewEl.innerHTML = hint;
        previewEl.dataset.hours = hours.toFixed(2);
        previewEl.dataset.nachtKompOver9 = hours > 9 ? '1' : '';
        return;
    }

    // FLEX: nur Typen mit UtpAuszahlung-Flag bekommen etwas
    if (empModel === 'FLEX' && !utpAuszahlung) {
        previewEl.innerHTML = '<span class="abs-hours-label">FLEX: keine automatische Stundengutschrift für diesen Typ — kann pro Absenz-Typ aktiviert werden (Systemeinstellungen → Absenz-Typen → „UTP als Stundenlohn auszahlen")</span>';
        previewEl.dataset.hours = '0';
        previewEl.dataset.nachtKompOver9 = '';
        return;
    }

    if (type === 'UNBEZ_URLAUB') {
        // Walter-Vorgabe 27.06.2026: Unbezahlter Urlaub wird NICHT ausbezahlt.
        // Im Lohnlauf wird stattdessen der Festlohn (FIX/FIX-M, Tagessatz 12/365)
        // bzw. die garantierten Soll-Stunden (MTP, 1/7) um diese Tage gekürzt;
        // bei UTP wirkt es automatisch (nur gestempelte Stunden werden bezahlt).
        hours = 0;
        hint  = `<span class="abs-hours-label">Unbezahlter Urlaub: ${count} Tag${count>1?'e':''} — keine Auszahlung. Im Lohnlauf wird der Festlohn (FIX/FIX-M) bzw. die Soll-Stunden (MTP) um diese Tage gekürzt.</span>`;
    } else if (!hatGutschrift) {
        // Kein Zeitgutschrift → Ausbezahlung
        hours = count * (weeklyH / 5) * pFactor;
        hint  = `<span class="abs-hours-label">${typCfg?.bezeichnung ?? type}: keine Zeitgutschrift, wird ausbezahlt (${count} Tag${count>1?'e':''} × ${weeklyH.toFixed(2)} h ÷ 5${pSuffix})</span>`;
    } else if (modus === '1/7') {
        hours = count * (weeklyH / 7) * pFactor;
        // Immer Gutschrift — die Stunden werden dem Arbeitszeit-Saldo
        // hinzugerechnet, damit Ferien/ähnliche Tage nicht als Minusstunden
        // erscheinen. Bei MTP basiert die Berechnung auf den garantierten
        // Vertragsstunden (33h), bei FIX/FIX-M/UTP auf der betrieblichen
        // Wochenarbeitszeit (42h) — geregelt über absenz_typ.basis_stunden.
        hint = `<span class="abs-hours-pos">+${hours.toFixed(2)} h</span> <span class="abs-hours-label">Gutschrift (${count} Tage × ${weeklyH.toFixed(2)} h ÷ 7${pSuffix})</span>`;
    } else {
        // 1/5 (Standard)
        hours = count * (weeklyH / 5) * pFactor;
        hint  = `<span class="abs-hours-pos">+${hours.toFixed(2)} h</span> <span class="abs-hours-label">Gutschrift (${count} Tag${count>1?'e':''} × ${weeklyH.toFixed(2)} h ÷ 5${pSuffix})</span>`;
    }

    // FEIERTAG bei FIX/FIX-M: zusätzlicher Hinweis dass der Feiertag-Saldo
    // um die bezogenen Tage reduziert wird (zusätzlich zur Stunden-Gutschrift).
    if (type === 'FEIERTAG' && (empModel === 'FIX' || empModel === 'FIX-M')) {
        hint += `<br><span class="abs-hours-label" style="color:#b45309">→ Feiertag-Saldo wird um ${count} Tag${count>1?'e':''} reduziert</span>`;
    }
    // FERIEN für FIX/FIX-M: zusätzlich Ferien-Tage-Saldo-Reduktion erwähnen
    // (UTP/MTP wurde oben schon per early-return abgefangen).
    if (type === 'FERIEN' && (empModel === 'FIX' || empModel === 'FIX-M')) {
        hint += `<br><span class="abs-hours-label" style="color:#15803d">→ Ferien-Tage-Saldo wird um ${count} Tag${count>1?'e':''} reduziert</span>`;
    }

    previewEl.innerHTML = hint;
    previewEl.dataset.hours = hours.toFixed(2);
    previewEl.dataset.nachtKompOver9 = '';
}

async function saveAbsence() {
    const editId   = document.getElementById('absenceModal').dataset.editId;
    const type     = document.getElementById('absTypeSelect').value;
    const dateFrom = document.getElementById('absDateFrom').value;
    const dateTo   = document.getElementById('absDateTo').value;
    const notes    = document.getElementById('absNotes').value.trim();

    if (!dateFrom || !dateTo || dateFrom > dateTo) {
        alert('Bitte gültigen Zeitraum eingeben.');
        return;
    }

    // Soft-Validierung: bei Neu-Erfassung innerhalb der aktiven Lohnperiode bleiben.
    // Beim Edit nicht prüfen — bestehende Absenzen können älter als die heutige
    // Periode sein.
    if (!editId) {
        const fromInput = document.getElementById('absDateFrom');
        const toInput   = document.getElementById('absDateTo');
        const min = fromInput?.min || '';
        const max = fromInput?.max || toInput?.max || '';
        if (min && (dateFrom < min || dateTo < min)) {
            alert(`Datum liegt vor dem Lohnperiode-Beginn (${min}). Bitte ein Datum innerhalb der aktiven Periode wählen.`);
            return;
        }
        if (max && (dateFrom > max || dateTo > max)) {
            alert(`Datum liegt nach dem Lohnperiode-Ende (${max}). Bitte ein Datum innerhalb der aktiven Periode wählen.`);
            return;
        }
    }

    // Überlappung: Client-Warnung vor dem Speichern (Server blockt zusätzlich hart).
    const overlap = findAbsenceOverlap(selectedEmployeeId, dateFrom, dateTo, editId || null);
    if (overlap) {
        alert(formatAbsenceOverlapMsg(overlap));
        return;
    }

    const workedDays   = getAbsWorkedDays();
    const hoursPreview = document.getElementById('absHoursPreview');
    const hours        = parseFloat(hoursPreview?.dataset.hours ?? '0');

    // Walter 02.08.2026: Nacht-Komp. > 9 h → Warnung (üblich = 1 bezahlter Freitag).
    if ((type === 'NACHT_KOMP' || hoursPreview?.dataset.nachtKompOver9 === '1') && hours > 9) {
        const ok = confirm(
            `Nacht-Kompensation umfasst ${hours.toFixed(2)} h (mehr als 9 h).\n\n` +
            `Üblich ist ein bezahlter Freitag = 1/5 der Wochenarbeitszeit. ` +
            `Der Nachtzuschlag selbst wird nicht ausbezahlt (nur über Komp. / bei Austritt).\n\n` +
            `Trotzdem speichern?`
        );
        if (!ok) return;
    }

    let prozent = Number(document.getElementById('absProzent')?.value ?? 100);
    if (!Number.isFinite(prozent) || prozent <= 0) prozent = 100;
    if (prozent > 100) prozent = 100;

    const payload = {
        employeeId:    selectedEmployeeId,
        absenceType:   type,
        dateFrom,
        dateTo,
        workedDays:    JSON.stringify(workedDays),
        hoursCredited: hours,
        prozent,
        notes,
    };

    try {
        const url    = editId ? `/api/absences/${editId}` : '/api/absences';
        const method = editId ? 'PUT' : 'POST';
        const res    = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
        });
        // Lohnlauf-Sperre? Zeigt Toast und bricht ab.
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            let msg = 'Fehler beim Speichern.';
            try {
                const j = await res.json();
                if (j.error === 'ABSENCE_OVERLAP' && j.message) msg = j.message;
                else if (j.message) msg = j.message;
            } catch {}
            alert(msg);
            return;
        }
        closeAbsenceModal();
        loadAbsenzenTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// Walter-Vorgabe 09.06.2026: ⋮-Menü-Toggle für alle MA-Listen (Absenz, Permit,
// QST, …). Generischer Helper mit ID-Prefix, damit Menüs aus verschiedenen
// Tabellen sich nicht in die Quere kommen.
// McBook-Fix 27.07.2026: Menü wie dokToggleMenu an body + position:fixed —
// sonst clipped overflow:hidden in .emp-detail / .abs-list die Popups.
function rowMenuCloseAll() {
    document.querySelectorAll('.dok-menu.show').forEach(m => {
        m.classList.remove('show', 'up');
        m.style.position = '';
        m.style.top = '';
        m.style.left = '';
        m.style.right = '';
        m.style.bottom = '';
        const orig = m._rowOrigParent;
        if (orig && orig !== m.parentElement && orig.isConnected) {
            orig.appendChild(m);
        }
        m._rowOrigParent = null;
    });
}
function rowMenuToggle(event, prefix, id) {
    event.stopPropagation();
    const menu = document.getElementById(`${prefix}Menu-${id}`);
    const btn = event.currentTarget || event.target?.closest('button');
    const wasOpen = menu?.classList.contains('show');
    rowMenuCloseAll();
    if (typeof dokCloseAllMenus === 'function') {
        try { dokCloseAllMenus(); } catch { /* optional */ }
    }
    if (wasOpen || !menu || !btn) return;

    if (menu.parentElement !== document.body) {
        menu._rowOrigParent = menu.parentElement;
        document.body.appendChild(menu);
    }
    menu.style.position = 'fixed';
    menu.style.right = 'auto';
    menu.style.bottom = 'auto';
    menu.style.left = '-9999px';
    menu.style.top = '0';
    menu.classList.add('show');

    const btnRect = btn.getBoundingClientRect();
    const menuW = menu.offsetWidth;
    const menuH = menu.offsetHeight;
    const margin = 6;
    let top = btnRect.bottom + 4;
    if (top + menuH > window.innerHeight - margin) {
        top = Math.max(margin, btnRect.top - menuH - 4);
    }
    let left = btnRect.right - menuW;
    if (left < margin) left = margin;
    if (left + menuW > window.innerWidth - margin) {
        left = window.innerWidth - menuW - margin;
    }
    menu.style.top = top + 'px';
    menu.style.left = left + 'px';

    setTimeout(() => {
        document.addEventListener('click', rowMenuCloseAll, { once: true });
        window.addEventListener('scroll', rowMenuCloseAll, { once: true, capture: true });
        window.addEventListener('resize', rowMenuCloseAll, { once: true });
    }, 10);
}
function absToggleMenu(event, id)   { rowMenuToggle(event, 'abs',    id); }
function permitToggleMenu(event, id){ rowMenuToggle(event, 'permit', id); }
function qstToggleMenu(event, id)   { rowMenuToggle(event, 'qst',    id); }
function bankToggleMenu(event, id)  { rowMenuToggle(event, 'bank',   id); }
function famToggleMenu(event, id)   { rowMenuToggle(event, 'fam',    id); }
function allowToggleMenu(event, id) { rowMenuToggle(event, 'allow',  id); }

async function deleteAbsence(id) {
    // ⋮-Menü schließen bevor der Confirm-Dialog kommt
    rowMenuCloseAll();
    if (!(await liquidConfirm('Absenz wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/absences/${id}`, { method: 'DELETE', headers: ah() });
        // Lohnlauf-Sperre? Zeigt Toast und bricht ab.
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            let msg = 'Fehler beim Löschen.';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch {}
            alert(msg);
            return;
        }
        loadAbsenzenTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// ══════════════════════════════════════════════════════════════════
// WIEDERKEHRENDE ZULAGEN / ABZÜGE (pro Mitarbeiter, mit Gültig-ab/bis)
// ══════════════════════════════════════════════════════════════════

let _rwLohnpositionen = [];   // Lohnposition-Cache (ZULAGE + ABZUG)

async function loadRecurringWagesTab(employeeId) {
    const el = document.getElementById('recurringWagesContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';

    // Lohnposition-Katalog einmalig laden
    if (_rwLohnpositionen.length === 0) {
        try {
            const resLp = await fetch('/api/lohn-zulag-typen', { headers: ah() });
            _rwLohnpositionen = resLp.ok ? await resLp.json() : [];
        } catch { _rwLohnpositionen = []; }
    }

    try {
        const res = await fetch(`/api/employee-recurring-wages/${employeeId}`, { headers: ah() });
        if (!res.ok) throw new Error();
        const list = await res.json();
        renderRecurringWagesList(el, list, employeeId);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
    }
}

function renderRecurringWagesList(el, list, employeeId) {
    const fmtAmount = v => Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const today = new Date().toISOString().slice(0, 10);

    let rows = '';
    if (!list.length) {
        rows = `<tr><td colspan="6" style="text-align:center;color:#94a3b8;padding:20px">Keine wiederkehrenden Einträge</td></tr>`;
    } else {
        list.forEach(r => {
            const isAbzug = r.typ === 'ABZUG';
            const typeBadge = isAbzug
                ? `<span style="font-size:11px;font-weight:600;padding:2px 8px;border-radius:10px;background:#fee2e2;color:#991b1b">− Abzug</span>`
                : `<span style="font-size:11px;font-weight:600;padding:2px 8px;border-radius:10px;background:#dcfce7;color:#166534">+ Zulage</span>`;
            const activeNow = r.validFrom <= today && (!r.validTo || r.validTo >= today);
            const activeIcon = activeNow
                ? '<span title="Zurzeit aktiv" style="color:#16a34a">●</span>'
                : '<span title="Ausserhalb Gültigkeitszeitraum" style="color:#cbd5e1">○</span>';
            rows += `<tr>
                <td>${activeIcon} ${typeBadge} <span style="font-weight:500">${r.lohnpositionBezeichnung}</span>
                    <span style="color:#94a3b8;font-size:11px;margin-left:4px">[${r.lohnpositionCode}]</span></td>
                <td style="font-family:monospace;text-align:right;color:${isAbzug ? '#dc2626' : '#059669'};font-weight:600">
                    ${isAbzug ? '−' : '+'} CHF ${fmtAmount(r.betrag)}</td>
                <td style="white-space:nowrap">${fmtDate(r.validFrom)}</td>
                <td style="white-space:nowrap;color:${r.validTo ? '#334155' : '#94a3b8'}">${r.validTo ? fmtDate(r.validTo) : 'offen'}</td>
                <td style="color:#64748b">${r.bemerkung ?? ''}</td>
                <td style="text-align:right;white-space:nowrap">
                    <button class="btn-stamp-edit" onclick='openRecurringWageModal(${JSON.stringify(r).replace(/'/g,"&#39;")})'>✎</button>
                    <button class="btn-stamp-del"  onclick="deleteRecurringWage(${r.id})">✕</button>
                </td>
            </tr>`;
        });
    }

    el.innerHTML = `
    <div class="abs-toolbar">
        <button class="btn-emp-add" onclick="openRecurringWageModal(null)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Zulage / Abzug erfassen
        </button>
    </div>
    <table class="abs-table">
        <thead><tr>
            <th>Lohnposition</th>
            <th style="text-align:right">Betrag</th>
            <th>Gültig ab</th>
            <th>Gültig bis</th>
            <th>Bemerkung</th>
            <th></th>
        </tr></thead>
        <tbody>${rows}</tbody>
    </table>`;
}

function openRecurringWageModal(existing) {
    const modal = document.getElementById('recurringWageModal');
    if (!modal) return;
    modal.style.display = 'flex';
    modal.dataset.editId = existing?.id ?? '';
    document.getElementById('rwModalTitle').textContent = existing ? 'Wiederkehrende Vergütung bearbeiten' : 'Wiederkehrende Vergütung erfassen';

    // Dropdown befüllen
    const sel = document.getElementById('rwLpSel');
    sel.innerHTML = '<option value="">— Lohnposition wählen —</option>' +
        _rwLohnpositionen.map(l =>
            `<option value="${l.id}">[${l.code}] ${l.bezeichnung} ${l.typ === 'ABZUG' ? '(−)' : '(+)'}</option>`
        ).join('');
    sel.value = existing?.lohnpositionId ?? '';

    const today = new Date().toISOString().slice(0, 10);
    document.getElementById('rwBetrag').value    = existing?.betrag ?? '';
    document.getElementById('rwValidFrom').value = existing?.validFrom ?? today;
    document.getElementById('rwValidTo').value   = existing?.validTo   ?? '';
    document.getElementById('rwBemerkung').value = existing?.bemerkung ?? '';
}

function closeRecurringWageModal() {
    const modal = document.getElementById('recurringWageModal');
    if (modal) { modal.style.display = 'none'; modal.dataset.editId = ''; }
}

async function saveRecurringWage() {
    const modal = document.getElementById('recurringWageModal');
    const editId = modal?.dataset.editId;
    const lpId   = parseInt(document.getElementById('rwLpSel').value);
    const betrag = parseFloat(document.getElementById('rwBetrag').value);
    const from   = document.getElementById('rwValidFrom').value;
    const to     = document.getElementById('rwValidTo').value;
    const bem    = document.getElementById('rwBemerkung').value.trim() || null;

    if (!lpId)   { alert('Bitte eine Lohnposition wählen.'); return; }
    if (!betrag || betrag <= 0) { alert('Bitte einen gültigen Betrag eingeben.'); return; }
    if (!from)   { alert('Bitte "Gültig ab"-Datum angeben.'); return; }
    if (to && to < from) { alert('"Gültig bis" muss grösser oder gleich "Gültig ab" sein.'); return; }

    const body = {
        employeeId:     selectedEmployeeId,
        lohnpositionId: lpId,
        betrag,
        validFrom:      from,
        validTo:        to || null,
        bemerkung:      bem
    };

    try {
        const url = editId ? `/api/employee-recurring-wages/${editId}` : '/api/employee-recurring-wages';
        const method = editId ? 'PUT' : 'POST';
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            const err = await res.text();
            alert('Fehler beim Speichern: ' + err);
            return;
        }
        closeRecurringWageModal();
        loadRecurringWagesTab(selectedEmployeeId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function deleteRecurringWage(id) {
    if (!(await liquidConfirm('Eintrag wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/employee-recurring-wages/${id}`, { method: 'DELETE', headers: ah() });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadRecurringWagesTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// ══════════════════════════════════════════════════════════════════
// LOHNABTRETUNGEN (Pfändung / Sozialamt) pro Mitarbeiter
// ══════════════════════════════════════════════════════════════════

let _laBehoerden = [];   // Cache aktive Behörden

async function loadLohnAssignmentsTab(employeeId) {
    const el = document.getElementById('lohnAssignmentsContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';

    // Behörden-Katalog einmalig laden
    if (_laBehoerden.length === 0) {
        try {
            const rB = await fetch('/api/behoerden', { headers: ah() });
            _laBehoerden = rB.ok ? await rB.json() : [];
        } catch { _laBehoerden = []; }
    }

    try {
        const res = await fetch(`/api/employee-lohn-assignments/${employeeId}`, { headers: ah() });
        if (!res.ok) throw new Error();
        const list = await res.json();
        renderLohnAssignmentsList(el, list);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
    }
}

function renderLohnAssignmentsList(el, list) {
    // Walter 02.08.2026: Dokument-Pflicht wie Bewilligungen —
    // «🔗 Doku verknüpfen» (gestrichelt) / «👁 Doku» (grün) + Header-Badge.
    const fmt = v => Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const today = new Date().toISOString().slice(0, 10);
    const missingDok = list.filter(a => !a.dokumentId).length;
    const allOk = list.length > 0 && missingDok === 0;
    const headerBadge = list.length === 0
        ? ''
        : (allOk
            ? `<span title="Alle Einträge haben einen Beleg"
                     style="display:inline-flex;align-items:center;gap:4px;font-size:11px;font-weight:600;padding:2px 9px;border-radius:999px;background:#dcfce7;color:#166534;border:1px solid #86efac">📄 Doku ✓</span>`
            : `<span title="${missingDok} Eintrag/Einträge ohne Beleg — im Lohnlauf unwirksam"
                     style="display:inline-flex;align-items:center;gap:4px;font-size:11px;font-weight:600;padding:2px 9px;border-radius:999px;background:#fee2e2;color:#991b1b;border:1px solid #fca5a5">● Dokument-Pflicht</span>`);

    const toolbar = `
    <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between;margin-top:0;margin-bottom:10px">
        <span style="display:inline-flex;align-items:center;gap:8px">
            Lohnabtretungen
            ${headerBadge}
        </span>
        <button class="btn-emp-add" onclick="openLohnAssignmentModal(null)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Lohnabtretung erfassen
        </button>
    </div>`;

    if (!list.length) {
        el.innerHTML = toolbar + `
        <div style="padding:14px;background:#fff;border:1px dashed #cbd5e1;border-radius:6px;color:#94a3b8;font-style:italic;font-size:12.5px;text-align:center">
            Keine Lohnabtretungen erfasst.
        </div>`;
        return;
    }

    let cards = '';
    list.forEach(a => {
        const activeNow = a.validFrom <= today && (!a.validTo || a.validTo >= today);
        const fertig    = a.zielbetrag > 0 && a.bereitsAbgezogen >= a.zielbetrag;
        const hasDok    = !!a.dokumentId;
        const wirksam   = hasDok && activeNow && !fertig;

        let statusPill;
        if (fertig)         statusPill = '<span style="display:inline-flex;align-items:center;gap:4px;font-size:10.5px;font-weight:700;padding:2px 8px;border-radius:9px;background:#cffafe;color:#0e7490">✓ erledigt</span>';
        else if (wirksam)   statusPill = '<span style="display:inline-flex;align-items:center;gap:4px;font-size:10.5px;font-weight:700;padding:2px 8px;border-radius:9px;background:#dcfce7;color:#166534">● aktiv</span>';
        else if (activeNow && !hasDok)
                            statusPill = '<span style="display:inline-flex;align-items:center;gap:4px;font-size:10.5px;font-weight:700;padding:2px 8px;border-radius:9px;background:#fee2e2;color:#991b1b">● Dokument-Pflicht</span>';
        else                statusPill = '<span style="display:inline-flex;align-items:center;gap:4px;font-size:10.5px;font-weight:700;padding:2px 8px;border-radius:9px;background:#f1f5f9;color:#64748b">○ inaktiv</span>';

        const okBadge = hasDok
            ? '<span style="display:inline-flex;align-items:center;gap:5px;font-size:11px;font-weight:600;padding:2px 9px;border-radius:999px;background:#dcfce7;color:#166534;border:1px solid #86efac"><span style="width:7px;height:7px;border-radius:50%;background:#16a34a;display:inline-block"></span>Alles in Ordnung</span>'
            : '<span style="display:inline-flex;align-items:center;gap:5px;font-size:11px;font-weight:600;padding:2px 9px;border-radius:999px;background:#fee2e2;color:#991b1b;border:1px solid #fca5a5"><span style="width:7px;height:7px;border-radius:50%;background:#dc2626;display:inline-block"></span>Dokument-Pflicht</span>';

        const zielbetragVal = a.zielbetrag > 0 ? fmt(a.zielbetrag) + ' CHF' : 'offen';
        const fortschritt = a.zielbetrag > 0
            ? `Bisher ${fmt(a.bereitsAbgezogen)} von ${fmt(a.zielbetrag)} CHF`
            : `Bisher ${fmt(a.bereitsAbgezogen)} CHF · unbegrenzt`;
        const bisStr = a.validTo
            ? formatDate(a.validTo)
            : 'bis Widerruf';
        const refParts = [];
        if (a.referenzAmt)       refParts.push(esc(a.referenzAmt));
        if (a.zahlungsReferenz)  refParts.push(esc(a.zahlungsReferenz));
        if (a.bemerkung)         refParts.push(esc(a.bemerkung));
        const refHtml = refParts.length ? refParts.join(' · ') : '';
        const aJson = JSON.stringify(a).replace(/'/g, '&#39;');

        const rowBorder = !hasDok
            ? '1.5px solid #fca5a5'
            : (wirksam ? '1.5px solid #16a34a' : '1px solid #e2e8f0');
        const rowBg = !hasDok
            ? '#fef2f2'
            : (wirksam ? '#f0fdf4' : '#fafafa');

        const dokBtn = hasDok
            ? `<button type="button" onclick="qstOpenBefreiungsDok(${a.employeeId || selectedEmployeeId}, ${a.dokumentId})"
                   style="flex-shrink:0;background:#dcfce7;color:#166534;border:1px solid #86efac;padding:4px 10px;border-radius:6px;font-size:11.5px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:5px"
                   title="${esc(a.dokumentName || 'Dokument')} anschauen">
                   👁 Doku
               </button>`
            : `<button type="button" onclick="laOpenDokuModal(${a.id})"
                   style="flex-shrink:0;background:#fff;color:#475569;border:1px dashed #cbd5e1;padding:4px 10px;border-radius:6px;font-size:11.5px;cursor:pointer">
                   🔗 Doku verknüpfen
               </button>`;

        cards += `
        <div style="padding:10px 12px;border:${rowBorder};border-radius:8px;background:${rowBg};margin-bottom:6px;display:flex;align-items:flex-start;gap:12px">
            <div style="flex:1;min-width:0">
                <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin-bottom:4px">
                    <span style="font-weight:700;color:#0f172a;font-size:13.5px">${esc(a.bezeichnung || 'Lohnpfändung')}</span>
                    ${statusPill}
                    ${okBadge}
                </div>
                <div style="font-size:12px;color:#475569;margin-bottom:2px">
                    ${esc(a.behoerdeName ?? '—')}${a.sachbearbeiterName ? ` · ${esc(a.sachbearbeiterName)}` : ''}
                    ${a.lohnausweisAnBehoerde ? ' · <span style="color:#64748b">Lohnausweis an SB</span>' : ''}
                </div>
                <div style="font-size:11.5px;color:#64748b">
                    ${formatDate(a.validFrom)} – ${bisStr}
                    · Freigrenze ${fmt(a.freigrenze)} · Ziel ${zielbetragVal}
                </div>
                <div style="font-size:11.5px;color:#64748b;margin-top:2px">${fortschritt}</div>
                ${refHtml ? `<div style="font-size:11px;color:#94a3b8;margin-top:3px">${refHtml}</div>` : ''}
            </div>
            ${dokBtn}
            <div class="dok-menu-wrap" style="flex-shrink:0">
                <button class="dok-menu-btn" onclick="laToggleMenu(event, ${a.id})" title="Aktionen">⋮</button>
                <div class="dok-menu" id="laMenu-${a.id}">
                    <button class="dok-menu-item" onclick='openLohnAssignmentModal(${aJson})'>Bearbeiten</button>
                    ${hasDok
                        ? `<button class="dok-menu-item" onclick="laOpenDokuModal(${a.id})">Doku ersetzen</button>
                           <button class="dok-menu-item" onclick="laUnlinkDokument(${a.id})">Verknüpfung aufheben</button>`
                        : `<button class="dok-menu-item" onclick="laOpenDokuModal(${a.id})">Doku verknüpfen</button>`}
                    <button class="dok-menu-item danger" onclick="deleteLohnAssignment(${a.id})">Löschen</button>
                </div>
                ${a.lohnausweisAnBehoerde
                    ? `<div class="emp-field" style="grid-column:span 4">
                           <div class="emp-field-value" style="font-size:12px;color:#475569">📄 Lohnausweis-Link an Behörde beim Definitiv-Abschluss</div>
                       </div>`
                    : ''}
            </div>
        </div>`;
    });

    el.innerHTML = toolbar + cards;
}

function laToggleMenu(event, id) { rowMenuToggle(event, 'la', id); }

function laOpenDokuModal(lohnAssignmentId) {
    const empId = selectedEmployeeId || window.activeEmpId;
    if (!empId || !lohnAssignmentId) return;
    openAusweisDokuModal(empId, 'lohn_assignment', { lohnAssignmentId });
}

async function laUnlinkDokument(lohnAssignmentId) {
    rowMenuCloseAll();
    if (!lohnAssignmentId) return;
    if (!(await liquidConfirm('Dokument-Verknüpfung wirklich aufheben?\n\nOhne Beleg greift die Lohnabtretung im Lohnlauf nicht mehr.'))) return;
    try {
        const res = await fetch(`/api/employee-lohn-assignments/${lohnAssignmentId}/dokument`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ dokumentId: null })
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || 'Verknüpfung konnte nicht aufgehoben werden.');
            return;
        }
        if (typeof loadLohnAssignmentsTab === 'function') {
            loadLohnAssignmentsTab(selectedEmployeeId || window.activeEmpId);
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

let _laSbCache = {}; // behoerdeId → [{id,name,email,…}]

function openLohnAssignmentModal(existing) {
    const modal = document.getElementById('lohnAssignmentModal');
    if (!modal) return;
    modal.style.display = 'flex';
    modal.dataset.editId = existing?.id ?? '';
    modal.dataset.pendingSbId = existing?.behoerdeSachbearbeiterId ?? '';
    document.getElementById('laModalTitle').textContent = existing ? 'Lohnabtretung bearbeiten' : 'Lohnabtretung erfassen';
    _laSbCache = {}; // frischer SB-Stamm (nach Behörden-Pflege)

    // Behörden-Dropdown
    const sel = document.getElementById('laBehoerdeSel');
    sel.innerHTML = '<option value="">— Behörde wählen —</option>' +
        _laBehoerden.map(b => `<option value="${b.id}">${b.name}</option>`).join('');
    sel.value = existing?.behoerdeId ?? '';

    const today = new Date().toISOString().slice(0, 10);
    document.getElementById('laBezeichnung').value      = existing?.bezeichnung ?? 'Lohnpfändung';
    document.getElementById('laFreigrenze').value       = existing?.freigrenze ?? '';
    document.getElementById('laZielbetrag').value       = (existing?.zielbetrag && existing.zielbetrag > 0) ? existing.zielbetrag : '';
    document.getElementById('laValidFrom').value        = existing?.validFrom ?? today;
    document.getElementById('laValidTo').value          = existing?.validTo   ?? '';
    document.getElementById('laReferenzAmt').value      = existing?.referenzAmt ?? '';
    const zrEl = document.getElementById('laZahlungsReferenz');
    zrEl.value = existing?.zahlungsReferenz ?? '';
    validateZahlungsReferenz(zrEl);   // initiales Live-Feedback (falls Wert vorhanden)
    // Neu: Bemerkung default = Name, Vorname, AHV (Walter 02.08.2026).
    // Bestehende Einträge behalten ihren gespeicherten Text.
    document.getElementById('laBemerkung').value = existing
        ? (existing.bemerkung ?? '')
        : laDefaultBemerkung();
    const laCb = document.getElementById('laLohnausweisAnBehoerde');
    if (laCb) laCb.checked = !!existing?.lohnausweisAnBehoerde;
    laOnBehoerdeChange();
}

/** Default-Bemerkung für neue Lohnabtretung: «Name, Vorname, AHV». */
function laDefaultBemerkung() {
    const emp = (typeof selectedEmployee !== 'undefined' && selectedEmployee) ? selectedEmployee : null;
    if (!emp) return '';
    const last  = (emp.lastName  || '').trim();
    const first = (emp.firstName || '').trim();
    const ahv   = (emp.socialSecurityNumber || emp.ahvNumber || emp.ahvNummer || '').trim();
    const parts = [];
    if (last)  parts.push(last);
    if (first) parts.push(first);
    let text = parts.join(', ');
    if (ahv) text = text ? `${text}, ${ahv}` : ahv;
    return text;
}

async function laOnBehoerdeChange() {
    const sel = document.getElementById('laBehoerdeSel');
    const sbSel = document.getElementById('laSachbearbeiterSel');
    const modal = document.getElementById('lohnAssignmentModal');
    if (!sel || !sbSel) return;
    const behoerdeId = parseInt(sel.value, 10) || 0;
    const pending = modal?.dataset.pendingSbId || '';
    sbSel.innerHTML = '<option value="">— Sachbearbeiter wählen —</option>';
    if (!behoerdeId) return;
    try {
        if (!_laSbCache[behoerdeId]) {
            const res = await fetch(`/api/behoerden/${behoerdeId}/sachbearbeiter`, { headers: ah() });
            _laSbCache[behoerdeId] = res.ok ? await res.json() : [];
        }
        const list = _laSbCache[behoerdeId] || [];
        for (const s of list) {
            const opt = document.createElement('option');
            opt.value = s.id;
            opt.textContent = s.email ? `${s.name} (${s.email})` : s.name;
            sbSel.appendChild(opt);
        }
        if (pending) sbSel.value = pending;
    } catch { /* ignore */ }
}

function closeLohnAssignmentModal() {
    const modal = document.getElementById('lohnAssignmentModal');
    if (modal) { modal.style.display = 'none'; modal.dataset.editId = ''; }
}

async function saveLohnAssignment() {
    const modal = document.getElementById('lohnAssignmentModal');
    const editId = modal?.dataset.editId;
    const behoerdeId  = parseInt(document.getElementById('laBehoerdeSel').value);
    const sbRaw       = document.getElementById('laSachbearbeiterSel')?.value;
    const behoerdeSachbearbeiterId = sbRaw ? parseInt(sbRaw, 10) : null;
    const bezeichnung = document.getElementById('laBezeichnung').value.trim() || 'Lohnpfändung';
    const freigrenze  = parseFloat(document.getElementById('laFreigrenze').value) || 0;
    const zielbetragStr = document.getElementById('laZielbetrag').value;
    const zielbetrag  = zielbetragStr ? parseFloat(zielbetragStr) : 0;
    const from        = document.getElementById('laValidFrom').value;
    const to          = document.getElementById('laValidTo').value;
    const refAmt      = document.getElementById('laReferenzAmt').value.trim() || null;
    const refZahlung  = document.getElementById('laZahlungsReferenz').value.trim() || null;
    const bem         = document.getElementById('laBemerkung').value.trim() || null;
    const lohnausweisAnBehoerde = !!document.getElementById('laLohnausweisAnBehoerde')?.checked;

    if (!behoerdeId) { alert('Bitte eine Behörde wählen.'); return; }
    if (lohnausweisAnBehoerde) {
        const sbList = _laSbCache[behoerdeId] || [];
        const sb = behoerdeSachbearbeiterId
            ? sbList.find(x => x.id === behoerdeSachbearbeiterId)
            : null;
        if (!sb || !String(sb.email || '').trim()) {
            alert('Für «Lohnausweis an Behörde» bitte einen Sachbearbeiter mit E-Mail wählen (Stamm unter Behörden).');
            return;
        }
    }
    if (freigrenze < 0)     { alert('Freigrenze muss ≥ 0 sein.'); return; }
    if (zielbetrag < 0)     { alert('Zielbetrag muss ≥ 0 sein.'); return; }
    if (!from)              { alert('Bitte "Gültig ab"-Datum angeben.'); return; }
    if (to && to < from)    { alert('"Gültig bis" muss grösser oder gleich "Gültig ab" sein.'); return; }
    if (refZahlung) {
        const check = validateReferenz(refZahlung);
        if (!check.valid && !(await liquidConfirm(`Die Zahlungsreferenz scheint ungültig zu sein:\n${check.error}\n\nTrotzdem speichern?`))) return;
    }

    const body = {
        employeeId: selectedEmployeeId,
        behoerdeId,
        behoerdeSachbearbeiterId: behoerdeSachbearbeiterId || null,
        bezeichnung,
        freigrenze,
        zielbetrag,
        validFrom: from,
        validTo:   to || null,
        referenzAmt:      refAmt,
        zahlungsReferenz: refZahlung,
        bemerkung: bem,
        lohnausweisAnBehoerde
    };

    try {
        const url = editId ? `/api/employee-lohn-assignments/${editId}` : '/api/employee-lohn-assignments';
        const method = editId ? 'PUT' : 'POST';
        const res = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { const err = await res.text(); alert('Fehler beim Speichern: ' + err); return; }
        const saved = await res.json().catch(() => null);
        closeLohnAssignmentModal();
        await loadLohnAssignmentsTab(selectedEmployeeId);
        // Neu angelegt ohne Beleg → sofort Verknüpfungs-Dialog (Bewilligungen-Muster).
        if (!editId && saved?.id && !saved.dokumentId) {
            laOpenDokuModal(saved.id);
        }
    } catch(e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function deleteLohnAssignment(id) {
    if (!(await liquidConfirm('Lohnabtretung wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/employee-lohn-assignments/${id}`, { method: 'DELETE', headers: ah() });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadLohnAssignmentsTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// ══════════════════════════════════════════════════════════════════
// ZAHLUNGSREFERENZ-VALIDIERUNG (QR-Referenz + SCOR/RF-Referenz)
// ══════════════════════════════════════════════════════════════════
//
// Zwei gültige Formate in der Schweiz:
//   1) QR-Referenz (früher ESR/BVR): 27 Ziffern, Modulo-10 rekursiv.
//      Die 27. Ziffer ist die Prüfziffer.
//   2) SCOR / RF-Creditor Reference (ISO 11649): "RF" + 2 Prüfziffern
//      + bis 21 alphanumerische Zeichen. Prüfung via Modulo-97.
//
// Liefert { valid: bool, type: 'QR'|'SCOR'|'UNKNOWN', error?: string }.

function validateReferenz(raw) {
    if (!raw) return { valid: true, type: 'UNKNOWN' };   // leer = ok (optional)
    const clean = raw.replace(/\s+/g, '').toUpperCase();

    // SCOR beginnt mit "RF"
    if (clean.startsWith('RF')) return validateScor(clean);

    // Sonst: rein numerisch → QR-Referenz
    if (/^\d+$/.test(clean)) return validateQrReferenz(clean);

    return { valid: false, type: 'UNKNOWN',
             error: 'Weder QR-Referenz (nur Ziffern) noch SCOR/RF-Referenz (mit "RF" am Anfang).' };
}

// QR-Referenz Modulo-10 rekursiv (27 Ziffern, letzte = Prüfziffer)
function validateQrReferenz(digits) {
    if (digits.length !== 27) {
        return { valid: false, type: 'QR',
                 error: `QR-Referenz muss exakt 27 Ziffern haben (aktuell ${digits.length}).` };
    }
    const table = [0, 9, 4, 6, 8, 2, 7, 1, 3, 5];
    let carry = 0;
    for (let i = 0; i < 26; i++) {
        carry = table[(carry + parseInt(digits[i], 10)) % 10];
    }
    const expected = (10 - carry) % 10;
    const actual   = parseInt(digits[26], 10);
    if (expected !== actual) {
        return { valid: false, type: 'QR',
                 error: `Prüfziffer falsch. Erwartet ${expected}, gefunden ${actual}.` };
    }
    return { valid: true, type: 'QR' };
}

// SCOR / RF Modulo-97 (ISO 11649 / ISO 13616)
// Gesamtlänge max. 25 Zeichen (RF + 2 + bis 21).
function validateScor(ref) {
    if (ref.length < 5 || ref.length > 25) {
        return { valid: false, type: 'SCOR',
                 error: `SCOR-Referenz muss 5–25 Zeichen haben (aktuell ${ref.length}).` };
    }
    if (!/^RF\d{2}[A-Z0-9]+$/.test(ref)) {
        return { valid: false, type: 'SCOR',
                 error: 'SCOR-Format: "RF" + 2 Prüfziffern + alphanumerisch.' };
    }
    // "RF" + Prüfziffern an den Schluss rotieren, Buchstaben zu Zahlen.
    const rearranged = ref.slice(4) + ref.slice(0, 4);
    let numeric = '';
    for (const ch of rearranged) {
        if (ch >= '0' && ch <= '9') numeric += ch;
        else                         numeric += (ch.charCodeAt(0) - 55).toString(); // A=10…Z=35
    }
    // Mod-97 in Chunks (BigInt wäre Alternative, aber so bleibt der Code simpel)
    let remainder = 0;
    for (const ch of numeric) {
        remainder = (remainder * 10 + parseInt(ch, 10)) % 97;
    }
    if (remainder !== 1) {
        return { valid: false, type: 'SCOR',
                 error: 'Prüfziffer der SCOR-Referenz stimmt nicht (MOD-97 ≠ 1).' };
    }
    return { valid: true, type: 'SCOR' };
}

// Live-Feedback im Modal (direkt unter dem Eingabefeld)
function validateZahlungsReferenz(inputEl) {
    const hint = document.getElementById('laReferenzHint');
    if (!hint) return;
    const val = inputEl.value.trim();
    if (!val) { hint.textContent = ''; hint.style.color = ''; inputEl.style.borderColor = ''; return; }
    const r = validateReferenz(val);
    if (r.valid) {
        hint.textContent = r.type === 'QR' ? '✓ Gültige QR-Referenz (27-stellig)'
                          : r.type === 'SCOR' ? '✓ Gültige SCOR/RF-Referenz'
                          : '';
        hint.style.color = '#16a34a';
        inputEl.style.borderColor = '#86efac';
    } else {
        hint.textContent = '✗ ' + r.error;
        hint.style.color = '#dc2626';
        inputEl.style.borderColor = '#fca5a5';
    }
}

// ══════════════════════════════════════════════════════════════════
// ZULAGEN / ABZÜGE TAB
// ══════════════════════════════════════════════════════════════════

const monthNames = ['Januar','Februar','März','April','Mai','Juni',
                    'Juli','August','September','Oktober','November','Dezember'];

function zulagenPeriodeStr() {
    return `${zulagenPeriodYear}-${String(zulagenPeriodMonth).padStart(2,'0')}`;
}

async function loadZulagenTab(employeeId) {
    const el = document.getElementById('zulagenContent');
    if (!el) return;

    const periode = zulagenPeriodeStr();
    const periodeLabel = `${monthNames[zulagenPeriodMonth - 1]} ${zulagenPeriodYear}`;

    try {
        const [listRes, typenRes] = await Promise.all([
            fetch(`/api/lohn-zulagen/${employeeId}/${periode}`, { headers: ah() }),
            fetch('/api/lohn-zulag-typen', { headers: ah() })
        ]);
        const eintraege = listRes.ok ? await listRes.json() : [];
        const typen     = typenRes.ok ? await typenRes.json() : [];

        // Getrennt: Zulagen und Abzüge
        const zulagen = eintraege.filter(e => e.typTyp === 'ZULAGE');
        const abzuege = eintraege.filter(e => e.typTyp === 'ABZUG');
        const totalZ  = zulagen.reduce((s, e) => s + e.betrag, 0);
        const totalA  = abzuege.reduce((s, e) => s + e.betrag, 0);
        const fmtCHF  = v => 'CHF ' + v.toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, "'");

        const buildRows = (list, typ) => list.length === 0
            ? `<tr><td colspan="5" style="color:#94a3b8;font-style:italic;padding:10px 8px">Keine ${typ === 'ZULAGE' ? 'Zulagen' : 'Abzüge'} erfasst</td></tr>`
            : list.map(e => `
                <tr>
                    <td>${e.typBezeichnung}</td>
                    <td>${e.bemerkung ?? '—'}</td>
                    <td style="text-align:center">
                        ${e.svPflichtig ? '<span style="color:#16a34a;font-size:11px">SV</span>' : ''}
                        ${e.qstPflichtig ? '<span style="color:#7c3aed;font-size:11px;margin-left:4px">QST</span>' : ''}
                        ${!e.svPflichtig && !e.qstPflichtig ? '<span style="color:#94a3b8;font-size:11px">—</span>' : ''}
                    </td>
                    <td style="text-align:right;font-variant-numeric:tabular-nums">
                        ${typ === 'ABZUG' ? '<span style="color:#dc2626">−</span>' : ''}${fmtCHF(e.betrag)}
                    </td>
                    <td style="text-align:right">
                        <button class="btn btn-sm btn-secondary" onclick="deleteZulage(${e.id})">Löschen</button>
                    </td>
                </tr>`).join('');

        el.innerHTML = `
        <div class="stamp-toolbar" style="position:sticky;top:0;z-index:20;background:white;margin:0 -24px;padding:14px 24px 12px;border-bottom:1px solid #e2e8f0;display:flex;align-items:center;gap:12px;flex-wrap:wrap">
            <button class="btn btn-secondary btn-sm" onclick="zulagenPrevMonth(${employeeId})">‹</button>
            <span style="font-weight:600;min-width:130px;text-align:center">${periodeLabel}</span>
            <button class="btn btn-secondary btn-sm" onclick="zulagenNextMonth(${employeeId})">›</button>
            <div style="flex:1"></div>
            <button class="btn btn-primary btn-sm" onclick="openZulageForm(${employeeId}, '${periode}')">+ Hinzufügen</button>
        </div>

        <div id="zulagenFormWrap" style="display:none;background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;padding:18px;margin:16px 0">
            <div style="font-weight:600;margin-bottom:12px">Neue Zulage / Abzug erfassen</div>
            <div style="display:grid;grid-template-columns:1fr 160px;gap:12px;margin-bottom:10px">
                <div>
                    <label class="f-label">Typ *</label>
                    <select id="zulagenTypId" class="f-input" onchange="onZulagenTypChange()">
                        <option value="">— Bitte wählen —</option>
                        ${typen.map(t => `<option value="${t.id}" data-typ="${t.typ}" data-sv="${t.svPflichtig}" data-qst="${t.qstPflichtig}">${t.typ === 'ABZUG' ? '− ' : '+ '}${t.bezeichnung}</option>`).join('')}
                    </select>
                </div>
                <div>
                    <label class="f-label">Betrag (CHF) *</label>
                    <input type="number" id="zulagenBetrag" class="f-input" min="0.01" step="0.01" placeholder="0.00">
                </div>
            </div>
            <div style="margin-bottom:10px">
                <label class="f-label">Bemerkung (optional)</label>
                <input type="text" id="zulagenBemerkung" class="f-input" placeholder="z.B. 312 km × CHF 0.70">
            </div>
            <div id="zulagenFlags" style="display:none;background:#f6f3ee;border:1px solid #e5e0d6;border-radius:6px;padding:8px 12px;margin-bottom:10px;font-size:12.5px;color:#6b6152"></div>
            <div style="display:flex;gap:8px">
                <button class="btn btn-primary" onclick="saveZulage(${employeeId}, '${periode}')">Speichern</button>
                <button class="btn btn-secondary" onclick="closeZulageForm()">Abbrechen</button>
            </div>
        </div>

        <div style="margin-top:16px">
            <!-- Zulagen -->
            <div style="font-weight:600;color:#166534;margin-bottom:8px;display:flex;align-items:center;gap:8px">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                Zulagen
                ${zulagen.length > 0 ? `<span style="margin-left:auto;font-weight:500;color:#15803d">${fmtCHF(totalZ)}</span>` : ''}
            </div>
            <table class="data-table" style="margin-bottom:20px">
                <thead><tr><th>Bezeichnung</th><th>Bemerkung</th><th style="text-align:center">Basis</th><th style="text-align:right">Betrag</th><th></th></tr></thead>
                <tbody>${buildRows(zulagen, 'ZULAGE')}</tbody>
            </table>

            <!-- Abzüge -->
            <div style="font-weight:600;color:#991b1b;margin-bottom:8px;display:flex;align-items:center;gap:8px">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="5" y1="12" x2="19" y2="12"/></svg>
                Abzüge
                ${abzuege.length > 0 ? `<span style="margin-left:auto;font-weight:500;color:#dc2626">−${fmtCHF(totalA)}</span>` : ''}
            </div>
            <table class="data-table">
                <thead><tr><th>Bezeichnung</th><th>Bemerkung</th><th style="text-align:center">Basis</th><th style="text-align:right">Betrag</th><th></th></tr></thead>
                <tbody>${buildRows(abzuege, 'ABZUG')}</tbody>
            </table>
        </div>`;

    } catch(e) {
        el.innerHTML = `<div class="emp-placeholder"><span>Fehler beim Laden: ${e.message}</span></div>`;
    }
}

function zulagenPrevMonth(employeeId) {
    zulagenPeriodMonth--;
    if (zulagenPeriodMonth < 1) { zulagenPeriodMonth = 12; zulagenPeriodYear--; }
    loadZulagenTab(employeeId);
}

function zulagenNextMonth(employeeId) {
    zulagenPeriodMonth++;
    if (zulagenPeriodMonth > 12) { zulagenPeriodMonth = 1; zulagenPeriodYear++; }
    loadZulagenTab(employeeId);
}

function openZulageForm(employeeId, periode) {
    document.getElementById('zulagenFormWrap').style.display = 'block';
    document.getElementById('zulagenTypId').value    = '';
    document.getElementById('zulagenBetrag').value   = '';
    document.getElementById('zulagenBemerkung').value = '';
    document.getElementById('zulagenFlags').style.display = 'none';
}

function closeZulageForm() {
    const w = document.getElementById('zulagenFormWrap');
    if (w) w.style.display = 'none';
}

function onZulagenTypChange() {
    const sel = document.getElementById('zulagenTypId');
    const opt = sel.selectedOptions[0];
    const flags = document.getElementById('zulagenFlags');
    if (!opt || !opt.value) { flags.style.display = 'none'; return; }
    const sv  = opt.dataset.sv === 'true';
    const qst = opt.dataset.qst === 'true';
    const parts = [];
    if (sv)  parts.push('SV-pflichtig (fliesst in AHV/IV/EO/ALV-Basis ein)');
    if (qst) parts.push('QST-pflichtig (fliesst in Quellensteuer-Basis ein)');
    if (!sv && !qst) parts.push('Nicht SV- und nicht QST-pflichtig (wird nach Nettolohn verrechnet)');
    flags.textContent = '⚡ ' + parts.join(' · ');
    flags.style.display = 'block';
}

async function saveZulage(employeeId, periode) {
    const typId   = parseInt(document.getElementById('zulagenTypId').value);
    const betrag  = parseFloat(document.getElementById('zulagenBetrag').value);
    const bemerkg = document.getElementById('zulagenBemerkung').value.trim() || null;

    if (!typId)         { alert('Bitte einen Typ wählen.'); return; }
    if (!betrag || betrag <= 0) { alert('Bitte einen gültigen Betrag eingeben.'); return; }

    try {
        const res = await fetch('/api/lohn-zulagen', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ employeeId, periode, typId, betrag, bemerkung: bemerkg })
        });
        if (!res.ok) { const e = await res.text(); alert('Fehler: ' + e); return; }
        closeZulageForm();
        loadZulagenTab(employeeId);
    } catch { alert('Verbindungsfehler.'); }
}

async function deleteZulage(id) {
    if (!(await liquidConfirm('Eintrag wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/lohn-zulagen/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadZulagenTab(selectedEmployeeId);
    } catch { alert('Verbindungsfehler.'); }
}

// ══════════════════════════════════════════════
// TAB: Stempelzeiten – Einträge pro Mitarbeiter und Monat
// ══════════════════════════════════════════════
const MONATSNAMEN_DE = ['Januar','Februar','März','April','Mai','Juni',
                        'Juli','August','September','Oktober','November','Dezember'];

async function loadStempelzeitenTab(employeeId) {
    const el = document.getElementById('stempelzeitenContent');
    if (!el) return;
    if (!employeeId) {
        el.innerHTML = `<div class="emp-placeholder">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
            <span>Bitte wähle einen Mitarbeiter</span>
        </div>`;
        return;
    }

    // Walter-Vorgabe 21.06.2026: Stempelzeiten IMMER nach KALENDERMONAT
    // (nicht mehr nach Lohnperiode). Links Jahr (ab 2025), rechts Monat (1–12);
    // Vorgabe = aktueller Monat. Auswahl persistiert über MA-Wechsel via
    // _stempelGlobalYear/_stempelGlobalMonth.
    el._stempelPerioden  = [];
    el._stempelPeriodeId = null;

    if (!el._stempelYear || !el._stempelMonth) {
        if (typeof _stempelGlobalYear !== 'undefined' && _stempelGlobalYear) {
            el._stempelYear  = _stempelGlobalYear;
            el._stempelMonth = _stempelGlobalMonth;
        } else {
            const now = new Date();
            el._stempelYear  = now.getFullYear();
            el._stempelMonth = now.getMonth() + 1;
        }
    }

    const curY = new Date().getFullYear();
    const jahre = [];
    for (let y = 2025; y <= curY + 1; y++) jahre.push(y);
    const yearOpts = jahre.map(y => `
        <option value="${y}" ${y === el._stempelYear ? 'selected' : ''}>${y}</option>`).join('');
    const monthOpts = MONATSNAMEN_DE.map((n, i) => `
        <option value="${i+1}" ${i+1 === el._stempelMonth ? 'selected' : ''}>${n}</option>`).join('');
    const filterHtml = `
        <select id="stempelYearSel" class="f-input stempel-period-sel stempel-period-year" onchange="stempelChangePeriod()">${yearOpts}</select>
        <select id="stempelMonthSel" class="f-input stempel-period-sel stempel-period-month" onchange="stempelChangePeriod()">${monthOpts}</select>`;

    // Walter 19.07.2026 (final): Filter + Spaltenköpfe AUSSERHALB des Scrolls
    // (kein sticky mehr) — sonst hüpfen sie am Listenanfang/-ende.
    // Look: Liquid Glass / Kohle — Klassen in app.css.
    el.innerHTML = `
        <div class="stempel-tab-wrap">
            <div id="stempelFilterRow" class="stempel-filter-row">
                ${filterHtml}
                <div id="stempelCount" class="stempel-count"></div>
            </div>
            <div class="stempel-table-card">
                <div id="stempelCols" class="stempel-cols"></div>
                <div id="stempelListe" class="stempel-liste">
                    <div class="stempel-loading">Lade…</div>
                </div>
            </div>
        </div>`;
    stempelBindScrollIsolation();

    await stempelLadeEintraege(employeeId);
}

/** Wheel-Isolation am Listenrand — Filter/Titel springen nicht (Walter 19.07.2026). */
function stempelBindScrollIsolation() {
    const wrap = document.querySelector('#stempelzeitenContent .stempel-tab-wrap');
    const sc = document.getElementById('stempelListe');
    if (!wrap || !sc || sc.dataset.scrollLock === '1') return;
    sc.dataset.scrollLock = '1';

    wrap.addEventListener('wheel', (e) => {
        e.stopPropagation();
        const maxScroll = sc.scrollHeight - sc.clientHeight;
        if (maxScroll <= 0) {
            e.preventDefault();
            return;
        }
        const atTop = sc.scrollTop <= 0;
        const atBottom = sc.scrollTop >= maxScroll - 1;
        if ((atTop && e.deltaY < 0) || (atBottom && e.deltaY > 0)) {
            e.preventDefault();
            return;
        }
        if (!sc.contains(e.target)) {
            sc.scrollTop = Math.min(maxScroll, Math.max(0, sc.scrollTop + e.deltaY));
            e.preventDefault();
        }
    }, { passive: false });
}

/** @deprecated Sticky-Offset entfällt — Spaltenköpfe liegen ausserhalb des Scrolls. */
function stempelUpdateFilterStickyOffset() { /* no-op, Alt-Aufrufer */ }

function stempelFmtDateShort(iso) {
    if (!iso) return '';
    const m = /(\d{4})-(\d{2})-(\d{2})/.exec(iso);
    return m ? `${m[3]}.${m[2]}.${m[1].slice(2)}` : '';
}

function stempelChangePeriod() {
    const el = document.getElementById('stempelzeitenContent');
    if (!el || !selectedEmployeeId) return;
    const perSel = document.getElementById('stempelPeriodeSel');
    if (perSel) {
        el._stempelPeriodeId = parseInt(perSel.value, 10);
        // Auswahl global persistieren — bleibt beim MA-Wechsel erhalten
        _stempelGlobalPeriodeId = el._stempelPeriodeId;
    } else {
        // Kalendermonat-Fallback
        el._stempelYear  = parseInt(document.getElementById('stempelYearSel').value, 10);
        el._stempelMonth = parseInt(document.getElementById('stempelMonthSel').value, 10);
        _stempelGlobalYear  = el._stempelYear;
        _stempelGlobalMonth = el._stempelMonth;
    }
    stempelLadeEintraege(selectedEmployeeId);
}

// Helfer: DB speichert Stempelzeiten als `timestamp without time zone` (Lokalzeit,
// keine TZ-Konvertierung). Wir parsen die Strings direkt per Regex, nicht via
// Date-Objekt — das vermeidet jede Browser-TZ-Interpretation.
const stempelPad2    = (n) => String(n).padStart(2, '0');
const stempelFmtTime = (iso) => {
    if (iso == null || iso === '') return '';
    const s = String(iso);
    // ISO: 2026-07-12T16:05:00(.fff)(Z|+02:00)
    let m = /T(\d{2}):(\d{2})/.exec(s);
    if (m) return `${m[1]}:${m[2]}`;
    // Space-Variante: 2026-07-12 16:05:00
    m = /^\d{4}-\d{2}-\d{2}[ ](\d{2}):(\d{2})/.exec(s);
    if (m) return `${m[1]}:${m[2]}`;
    // Nur Zeit: 16:05[:00]
    m = /^(\d{1,2}):(\d{2})(?::\d{2})?$/.exec(s.trim());
    if (m) return `${stempelPad2(m[1])}:${m[2]}`;
    return '';
};
// Original-Zeiten aus easy@work-Audit-Text (wie Backend ParseEditedTimesFromTexts).
// «Aus vom 2.7.2026, 13:37 bis zum … geändert» → 13:37 (auch mit Icon-Präfix).
const stempelParseOrigFromText = (text) => {
    if (!text) return { in: '', out: '' };
    const inM  = /Ein\s+vom\s+.+?(\d{1,2}):(\d{2})\s+bis\s+zum/i.exec(text);
    const outM = /Aus\s+vom\s+.+?(\d{1,2}):(\d{2})\s+bis\s+zum/i.exec(text);
    const fmt = (m) => m ? `${stempelPad2(m[1])}:${m[2]}` : '';
    return { in: fmt(inM), out: fmt(outM) };
};
const stempelOriginalTimes = (r) => {
    let tin  = stempelFmtTime(r.originalTimeIn  ?? r.OriginalTimeIn);
    let tout = stempelFmtTime(r.originalTimeOut ?? r.OriginalTimeOut);
    if (!tin || !tout) {
        const fromText = stempelParseOrigFromText(
            [r.comment, r.originalComment, r.OriginalComment].filter(Boolean).join('\n'));
        if (!tin)  tin  = fromText.in;
        if (!tout) tout = fromText.out;
    }
    return { in: tin, out: tout };
};
const stempelFmtDate = (iso) => {
    if (!iso) return '';
    const m = /(\d{4})-(\d{2})-(\d{2})/.exec(iso);
    if (!m) return '';
    // Wochentag via UTC-Date (reines Datum, keine Zeit-Komponente)
    const d = new Date(Date.UTC(+m[1], +m[2]-1, +m[3]));
    const wd = ['So.','Mo.','Di.','Mi.','Do.','Fr.','Sa.'][d.getUTCDay()];
    return `${wd}, ${m[3]}.${m[2]}.${m[1].slice(2)}`;
};
const stempelFmtHours = (h) => Number(h || 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// ── ISO-Woche (Mo–So) ───────────────────────────────────────────────────────
// Montag der Woche als stabiler Gruppen-Schlüssel ("yyyy-mm-dd").
const stempelWeekMonday = (iso) => {
    const m = /(\d{4})-(\d{2})-(\d{2})/.exec(iso || '');
    if (!m) return null;
    const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3]));
    const dow = d.getUTCDay();                 // 0=So … 6=Sa
    d.setUTCDate(d.getUTCDate() + (dow === 0 ? -6 : 1 - dow)); // → Montag
    return d.toISOString().slice(0, 10);
};
// Sonntag der Woche (Mo–So) als "yyyy-mm-dd".
const stempelWeekSunday = (iso) => {
    const mon = stempelWeekMonday(iso);
    if (!mon) return null;
    const m = /(\d{4})-(\d{2})-(\d{2})/.exec(mon);
    const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3]));
    d.setUTCDate(d.getUTCDate() + 6);
    return d.toISOString().slice(0, 10);
};
// ISO-Kalenderwoche (1–53).
const stempelIsoWeek = (iso) => {
    const m = /(\d{4})-(\d{2})-(\d{2})/.exec(iso || '');
    if (!m) return null;
    const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3]));
    const day = (d.getUTCDay() + 6) % 7;       // Mo=0 … So=6
    d.setUTCDate(d.getUTCDate() - day + 3);     // Donnerstag dieser Woche
    const firstThu = new Date(Date.UTC(d.getUTCFullYear(), 0, 4));
    const firstDay = (firstThu.getUTCDay() + 6) % 7;
    firstThu.setUTCDate(firstThu.getUTCDate() - firstDay + 3);
    return 1 + Math.round((d - firstThu) / (7 * 24 * 3600 * 1000));
};

// Datum + HH:MM → ISO ohne Z (Backend speichert 1:1 als Lokalzeit)
function stempelBuildIso(dateStr, timeStr) {
    if (!dateStr || !timeStr) return null;
    return `${dateStr}T${timeStr}:00`;
}

let _stempelRowsCache = []; // Cache für Edit-Modus

async function stempelLadeEintraege(employeeId) {
    const el = document.getElementById('stempelzeitenContent');
    const listEl  = document.getElementById('stempelListe');
    const countEl = document.getElementById('stempelCount');
    if (!listEl || !el) return;
    listEl.innerHTML = `<div class="stempel-loading">Lade…</div>`;

    // URL bauen. Wir laden IMMER per Datumsbereich — und zwar ERWEITERT um die
    // vollen ISO-Wochen an den Periodenrändern (Mo der ersten Woche bis So der
    // letzten Woche). Grund: Wochentotale (Mo–So) müssen die ganze Woche
    // umfassen und das Total NUR beim echten letzten Eintrag der Woche zeigen —
    // auch wenn eine Woche über die Monatsgrenze läuft (Walter-Vorgabe 24.05.2026).
    // Angezeigt werden danach nur die Zeilen INNERHALB der Periode [pFrom..pTo].
    let pFrom, pTo, labelHint;
    if (el._stempelPeriodeId) {
        const periode = (el._stempelPerioden || []).find(p => p.id === el._stempelPeriodeId);
        if (!periode) {
            listEl.innerHTML = `<div style="padding:20px;color:#dc2626;font-size:13px">Periode nicht gefunden.</div>`;
            return;
        }
        pFrom = (periode.periodFrom || '').slice(0, 10);
        pTo   = (periode.periodTo   || '').slice(0, 10);
        labelHint = `Lohnperiode ${periode.label || ''} (${stempelFmtDateShort(periode.periodFrom)}–${stempelFmtDateShort(periode.periodTo)})`;
    } else {
        const y = el._stempelYear, mo = el._stempelMonth;
        pFrom = `${y}-${String(mo).padStart(2,'0')}-01`;
        pTo   = new Date(Date.UTC(y, mo, 0)).toISOString().slice(0, 10); // letzter Tag des Monats
        labelHint = `${MONATSNAMEN_DE[mo-1]} ${y}`;
    }
    const extFrom = stempelWeekMonday(pFrom) || pFrom;
    const extTo   = stempelWeekSunday(pTo)   || pTo;
    const url = `/api/employees/${employeeId}/timeentries?dateFrom=${extFrom}&dateTo=${extTo}`;

    try {
        // Lohnlauf-Sperre parallel zur Stempel-Liste laden — Buttons werden
        // pro Zeile konditional gerendert, je nachdem ob das Datum im
        // gesperrten Bereich liegt.
        const activeEmp = selectedEmployee?.employments?.find(e => e.isActive)
                       ?? selectedEmployee?.employments?.[0];
        const cpId      = activeEmp?.companyProfileId;
        const cpIdForLock = cpId || (typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null);

        const [res, lockState] = await Promise.all([
            fetch(url, { headers: { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` } }),
            cpIdForLock && window.lohnEditLock
                ? window.lohnEditLock.loadState(cpIdForLock)
                : Promise.resolve(null)
        ]);
        if (!res.ok) {
            listEl.innerHTML = `<div style="padding:20px;color:#dc2626;font-size:13px">Fehler ${res.status}</div>`;
            return;
        }
        const allRows = await res.json();
        // Angezeigt werden nur Einträge INNERHALB der Periode; die Rand-Wochen
        // (aus den Nachbarmonaten) dienen nur der korrekten Wochentotal-Bildung.
        const rows = allRows.filter(r => {
            const d = (r.entryDate || '').slice(0, 10);
            return d >= pFrom && d <= pTo;
        });
        _stempelRowsCache = rows;

        if (countEl) {
            countEl.textContent = rows.length === 0
                ? `Keine Einträge`
                : `${rows.length} Eintrag${rows.length === 1 ? '' : 'e'} · ${labelHint}`;
        }

        stempelRenderTable(rows, employeeId, lockState, allRows, pFrom, pTo);

        // Wenn leer: Shortcut-Buttons zu Monaten mit Einträgen nachladen
        if (rows.length === 0) stempelLadeQuickNav(employeeId);
    } catch (err) {
        listEl.innerHTML = `<div style="padding:20px;color:#dc2626;font-size:13px">Fehler: ${err.message}</div>`;
    }
}

// Monats-Schnellwahl als feste Matrix (Walter-Vorgabe 21.06.2026): IMMER alle
// 12 Monate, eine Zeile pro Jahr (mind. die letzten 3 Jahre + jedes Jahr mit
// Daten). Monate mit Einträgen zeigen die Anzahl, leere sind ausgegraut — aber
// alle anklickbar.
async function stempelLadeQuickNav(employeeId) {
    const navEl = document.getElementById('stempelQuickNav');
    if (!navEl) return;
    let periods = [];
    try {
        const res = await fetch(`/api/employees/${employeeId}/timeentries/periods`,
            { headers: { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` } });
        if (res.ok) periods = await res.json();
    } catch { /* silent → leeres Raster */ }
    if (!Array.isArray(periods)) periods = [];

    const byYear = {};
    periods.forEach(p => { (byYear[p.year] ??= {})[p.month] = p.count; });

    const curY = new Date().getFullYear();
    // 6 Jahre anzeigen (Aufbewahrung = 5 Jahre, Walter-Vorgabe 21.06.2026)
    // plus jedes Jahr mit Daten.
    const yearSet = new Set();
    for (let i = 0; i <= 5; i++) yearSet.add(curY - i);
    Object.keys(byYear).forEach(y => yearSet.add(Number(y)));
    const years = Array.from(yearSet).sort((a, b) => b - a);

    const el   = document.getElementById('stempelzeitenContent');
    const selY = el ? el._stempelYear  : null;
    const selM = el ? el._stempelMonth : null;
    const mon  = ['Jan','Feb','Mär','Apr','Mai','Jun','Jul','Aug','Sep','Okt','Nov','Dez'];

    let html = `<div style="font-size:12px;color:#64748b;margin-bottom:8px">Monat direkt wählen:</div>`;
    html += `<div style="display:inline-grid;grid-template-columns:auto repeat(12, minmax(38px,1fr));gap:4px;align-items:center;max-width:100%">`;
    html += `<div></div>` + mon.map(n => `<div style="font-size:10.5px;color:#94a3b8;text-align:center;font-weight:600">${n}</div>`).join('');
    years.forEach(y => {
        html += `<div style="font-size:12.5px;font-weight:700;color:#475569;padding-right:8px;text-align:right">${y}</div>`;
        for (let m = 1; m <= 12; m++) {
            const cnt   = byYear[y] && byYear[y][m];
            const has   = cnt > 0;
            const isSel = (y === selY && m === selM);
            const border = isSel ? '#1a1a1a' : (has ? '#cbd5e1' : '#eef2f6');
            const bg     = isSel ? '#1a1a1a' : (has ? '#fff'    : '#fafafa');
            const color  = isSel ? '#fff'    : (has ? '#334155' : '#cbd5e1');
            html += `<button onclick="stempelJumpTo(${y},${m})" title="${mon[m-1]} ${y}${has ? ' · ' + cnt + ' Einträge' : ' · keine Einträge'}" style="font-size:11px;padding:5px 0;border-radius:6px;cursor:pointer;border:1px solid ${border};background:${bg};color:${color};font-weight:${has ? 600 : 400}">${has ? cnt : '·'}</button>`;
        }
    });
    html += `</div>`;
    navEl.innerHTML = html;
}

function stempelJumpTo(year, month) {
    const el = document.getElementById('stempelzeitenContent');
    if (!el) return;
    // Perioden-Modus: suche Periode die Year/Month abdeckt
    const perSel = document.getElementById('stempelPeriodeSel');
    if (perSel && Array.isArray(el._stempelPerioden)) {
        const match = el._stempelPerioden.find(p => p.year === year && p.month === month);
        if (match) {
            perSel.value = match.id;
            el._stempelPeriodeId = match.id;
            stempelChangePeriod();
            return;
        }
    }
    // Kalendermonat-Fallback
    const yEl = document.getElementById('stempelYearSel');
    const mEl = document.getElementById('stempelMonthSel');
    if (!yEl || !mEl) return;
    if (!Array.from(yEl.options).some(o => parseInt(o.value, 10) === year)) {
        const opt = document.createElement('option');
        opt.value = year; opt.textContent = year;
        yEl.appendChild(opt);
    }
    yEl.value = year;
    mEl.value = month;
    stempelChangePeriod();
}

function stempelRenderTable(rows, employeeId, lockState = null, allRows = null, pFrom = null, pTo = null) {
    const listEl = document.getElementById('stempelListe');
    if (!listEl) return;

    // allRows = volle ISO-Wochen inkl. Nachbarmonats-Rändern (für Wochentotale).
    // rows    = nur die Periode (was angezeigt wird). Fallback: rows = allRows.
    const fullRows = (allRows && allRows.length >= 0) ? allRows : rows;

    // Aufsteigend nach Datum sortieren (für korrekte Mo–So-Gruppierung).
    const sortAsc = (a, b) =>
        (a.entryDate || '').localeCompare(b.entryDate || '') ||
        (a.timeIn    || '').localeCompare(b.timeIn    || '');
    const sorted   = [...rows].sort(sortAsc);       // Anzeige (nur Periode)
    const sortedAll = [...fullRows].sort(sortAsc);  // für Wochentotale (volle Wochen)

    // Monats-Summen (nur Periode). Absolute Stunden = Tag + Nacht
    // (Walter 03.08.2026 — nicht totalHours allein, das war in Alt-Daten oft nur Tag).
    const absH = (r) => {
        const d = Number(r.durationHours ?? 0);
        const n = Number(r.nightHours ?? 0);
        const t = Number(r.totalHours ?? 0);
        if (d > 0 || n > 0) {
            const parts = d + n;
            // totalHours schon = Tag+Nacht → total; total ≈ nur Tag → Tag+Nacht
            if (t >= parts - 0.05) return t;
            if (n > 0 && Math.abs(t - d) <= 0.05) return parts;
            return Math.max(t, parts);
        }
        return t;
    };
    let sumTag = 0, sumN = 0, sumTot = 0;
    sorted.forEach(r => {
        sumTag += Number(r.durationHours ?? 0);
        sumN   += Number(r.nightHours ?? 0);
        sumTot += absH(r);
    });

    // Wochentotal (Mo–So) = absolute gestempelte Stunden (Tag+Nacht)
    const weekSum = {}, lastIdOfWeek = {};
    sortedAll.forEach(r => {
        const wk = stempelWeekMonday(r.entryDate);
        if (!wk) return;
        weekSum[wk] = (weekSum[wk] || 0) + absH(r);
        lastIdOfWeek[wk] = r.id;   // sortiert aufsteigend ⇒ am Ende = letzter Eintrag der Woche
    });

    // Max. Stunden/Woche der aktuell gewählten Filiale (Warngrenze).
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
    const branch = (typeof allBranches !== 'undefined' ? allBranches : []).find(b => b.id === cpId);
    const maxWeekly = (branch && branch.maxWeeklyHours != null && branch.maxWeeklyHours !== '')
        ? Number(branch.maxWeeklyHours) : null;

    const esc = (s) => s == null ? '' : String(s).replace(/</g,'&lt;');

    const trs = sorted.map((r, i) => {
        const wasEdited = !!r.editedBy;

        // Wochentotal NUR beim echten letzten Eintrag der Woche zeigen (über alle
        // geladenen Wochen, inkl. Nachbarmonate). Läuft die Woche in den nächsten
        // Monat und hat dort Einträge, fällt das Total hier weg (es erscheint dann
        // dort); hat der nächste Monat keine Einträge dieser Woche, ist der letzte
        // Eintrag hier (z.B. Fr) und das Total wird hier gezeigt.
        const wk = stempelWeekMonday(r.entryDate);
        const isLastOfWeek = wk && lastIdOfWeek[wk] === r.id;

        // Wochentotal-Badge: KW schwach, Total fett (über Max → .over).
        // Look via .stempel-week-* in app.css (Liquid Glass).
        let weekBadge = '';
        if (isLastOfWeek) {
            const wt   = weekSum[wk] || 0;
            const over = maxWeekly != null && wt > maxWeekly + 1e-9;
            const kw   = stempelIsoWeek(r.entryDate);
            const kwLabel  = `<span class="stempel-week-kw">∑ KW${kw}</span>`;
            const hrsLabel = `<span class="stempel-week-hrs${over ? ' over' : ''}">${stempelFmtHours(wt)} h${over ? ` ⚠ &gt; ${stempelFmtHours(maxWeekly)}` : ''}</span>`;
            weekBadge = `<span class="stempel-week-badge" title="Wochentotal Mo–So (gestempelt)${maxWeekly != null ? ' · Max ' + stempelFmtHours(maxWeekly) + ' h' : ''}">${kwLabel}${hrsLabel}</span>`;
        }

        // Korrekturzeile (oben): geänderte Werte markiert, Kommentar mit Audit.
        const timeCls = wasEdited ? ' stempel-time-edited' : '';
        const korrekturKommentar = wasEdited
            ? `${esc(r.comment || '')}${r.comment ? ' · ' : ''}<span class="stempel-edit-meta">geändert ${new Date(r.editedAt).toLocaleDateString('de-CH')} von ${esc(r.editedBy)}</span>`
            : esc(r.comment);

        // Kommentar-Zelle: Kommentar + (optional) Wochentotal direkt dahinter.
        // Walter 17.06.2026: Wochentotal nicht am rechten Rand „pinnen", sondern
        // mit Lücke nach dem Kommentar — wirkt zusammenhängend.
        const kommentarCell = weekBadge
            ? `<div class="stempel-kommentar-wrap"><span>${korrekturKommentar}</span>${weekBadge}</div>`
            : korrekturKommentar;

        const nightH = Number(r.nightHours || 0);
        const rowCls = [
            'stempel-row',
            isLastOfWeek ? 'stempel-row-week-end' : '',
            wasEdited ? 'stempel-row-edited' : '',
        ].filter(Boolean).join(' ');

        // Stempelzeiten sind read-only (Walter-Vorgabe 17.05.2026): keine
        // Edit-/Löschen-Buttons mehr — easy@work ist die Quelle der Wahrheit.

        const totalRow = stempelFmtHours(absH(r));
        const mainRow = `
            <tr class="${rowCls}" data-row-id="${r.id}">
                <td class="stempel-td stempel-td-date">${stempelFmtDate(r.entryDate)}</td>
                <td class="stempel-td stempel-td-time${timeCls}">${stempelFmtTime(r.timeIn)}</td>
                <td class="stempel-td stempel-td-time${timeCls}">${stempelFmtTime(r.timeOut)}</td>
                <td class="stempel-td stempel-td-num">${stempelFmtHours(r.durationHours)}</td>
                <td class="stempel-td stempel-td-num ${nightH > 0 ? 'stempel-night' : 'stempel-night-zero'}">${stempelFmtHours(r.nightHours)}</td>
                <td class="stempel-td stempel-td-num stempel-td-total">${totalRow}</td>
                <td class="stempel-td stempel-td-comment">${kommentarCell}</td>
            </tr>`;

        if (!wasEdited) return mainRow;

        // Original-Zeile direkt darunter (Pfeil ↳ zur Korrektur).
        // Zeiten aus DB-Feldern, Fallback Audit-Text im Kommentar.
        const orig = stempelOriginalTimes(r);
        const origInShow  = orig.in  || '—';
        const origOutShow = orig.out || '—';
        const origRow = `
            <tr class="stempel-orig-row${isLastOfWeek ? ' stempel-row-week-end' : ''}">
                <td class="stempel-td stempel-orig-label">↳ Original</td>
                <td class="stempel-td stempel-td-time stempel-orig-time">${origInShow}</td>
                <td class="stempel-td stempel-td-time stempel-orig-time">${origOutShow}</td>
                <td class="stempel-td stempel-orig-spacer" colspan="3"></td>
                <td class="stempel-td stempel-orig-comment">${esc(r.originalComment || '')}</td>
            </tr>`;

        return mainRow + origRow;
    }).join('');

    const empty = sorted.length === 0
        ? `<tr><td colspan="7" class="stempel-empty">
            Keine Einträge
            <div id="stempelQuickNav" class="stempel-quick-nav"></div>
        </td></tr>`
        : '';

    // Spaltenköpfe FIX ausserhalb Scroll; nur Zeilen + Summe scrollen.
    const headHtml = `
        <table class="stempel-table stempel-table-head">
            <colgroup>
                <col class="stempel-col-date"><col class="stempel-col-time"><col class="stempel-col-time">
                <col class="stempel-col-num"><col class="stempel-col-num"><col class="stempel-col-num">
                <col class="stempel-col-comment">
            </colgroup>
            <thead>
                <tr>
                    <th class="stempel-th stempel-th-left">Datum</th>
                    <th class="stempel-th stempel-th-left">In</th>
                    <th class="stempel-th stempel-th-left">Out</th>
                    <th class="stempel-th stempel-th-right">Tag</th>
                    <th class="stempel-th stempel-th-right">Nacht</th>
                    <th class="stempel-th stempel-th-right">Total</th>
                    <th class="stempel-th stempel-th-left">Kommentar / Woche</th>
                </tr>
            </thead>
        </table>`;
    const colsEl = document.getElementById('stempelCols');
    if (colsEl) colsEl.innerHTML = headHtml;

    listEl.innerHTML = `
        <table class="stempel-table stempel-table-body">
            <colgroup>
                <col class="stempel-col-date"><col class="stempel-col-time"><col class="stempel-col-time">
                <col class="stempel-col-num"><col class="stempel-col-num"><col class="stempel-col-num">
                <col class="stempel-col-comment">
            </colgroup>
            <tbody>${trs}${empty}</tbody>
            ${sorted.length > 0 ? `<tfoot>
                <tr class="stempel-foot-row">
                    <td colspan="3" class="stempel-ft stempel-ft-label">Summe</td>
                    <td class="stempel-ft stempel-ft-num">${stempelFmtHours(sumTag)}</td>
                    <td class="stempel-ft stempel-ft-num">${stempelFmtHours(sumN)}</td>
                    <td class="stempel-ft stempel-ft-num stempel-ft-total">${stempelFmtHours(sumTot)}</td>
                    <td class="stempel-ft"></td>
                </tr>
            </tfoot>` : ''}
        </table>`;
}

// ────────────────────────────────────────────────────────────────────────────
// Stempelzeiten-Edit-/Save-/Delete-Funktionen ENTFERNT (Walter 17.06.2026):
// Cowork ist seit Mai 2026 read-only für Stempelzeiten. Backend-Endpoints
// POST/PUT/DELETE liefern 403. Mit der easy@work-API-Integration ist
// easy@work die alleinige Quelle — Erfassung + Korrekturen passieren dort,
// in Cowork läuft nur noch der Sync und die Anzeige.
//
// Entfernt: stempelRenderForm, stempelStartNew, stempelStartEdit,
//           stempelCancelForm, stempelSaveForm, stempelDelete.
// Bleibt:  stempelEditRow im DOM-Template (versteckt, harmlos);
//          OriginalTimeIn/Out-Anzeige (Audit-Hinweis aus dem easy@work-Sync).
// ────────────────────────────────────────────────────────────────────────────

// ══════════════════════════════════════════════
// TAB: KTG/UVG – Tagessatz nach Spezialistenvorgabe
// Regel A (≤ 4 Perioden seit Vertragsstart): Hochrechnung aus Vertrag
// Regel B (≥ 4 Perioden):                    Durchschnitt aus AHV-Brutto (+ Mehrstunden bei MTP)
// Walter 17.07.2026: gleiche Rechnung an drei Orten —
//   full  = Tab «KTG/UVG»
//   side  = Absenzen-Tab rechts (Arbeitsplatz Krank/Unfall)
//   compact = Legacy (Übersicht nutzt Saldi-Tabelle seit 19.07.2026)
function _ktgFmtChf(n, dec = 2) {
    return Number(n || 0).toLocaleString('de-CH', {
        minimumFractionDigits: dec, maximumFractionDigits: dec
    });
}

function _ktgBadgeHtml(regel, compact = false) {
    if (regel === 'MANUELL')
        return `<span class="ktg-badge ktg-badge-man">MANUELL</span>`;
    if (regel === 'A')
        return `<span class="ktg-badge ktg-badge-a">${compact ? 'REGEL A' : 'REGEL A · Hochrechnung'}</span>`;
    return `<span class="ktg-badge ktg-badge-b">${compact ? 'REGEL B' : 'REGEL B · Durchschnitt'}</span>`;
}

function _ktgBreakdownHtml(d) {
    const bd = d.breakdown || {};
    const fmt = _ktgFmtChf;
    const renderMonate = (titel, istBerechnungsbasis) => {
        const monate = bd.monate || [];
        if (monate.length === 0) return '';
        const rows = monate.map(m => `
            <tr style="border-top:1px solid #f1f5f9">
                <td style="padding:6px 10px;font-size:12px;color:#475569">${m.monatName} ${m.jahr}</td>
                <td style="padding:6px 10px;font-size:12px;text-align:right;font-family:monospace">CHF ${fmt(m.brutto)}</td>
            </tr>`).join('');
        const avg = monate.reduce((s, m) => s + Number(m.brutto), 0) / monate.length;
        const footer = istBerechnungsbasis
            ? `<tr style="border-top:2px solid #e2e8f0;background:#f6f3ee">
                   <td style="padding:8px 10px;font-size:12px;font-weight:700;color:#6b7280">Ø pro Monat</td>
                   <td style="padding:8px 10px;font-size:12px;text-align:right;font-family:monospace;font-weight:700;color:#6b7280">CHF ${fmt(avg)}</td>
               </tr>`
            : '';
        return `
            <div style="margin-top:12px;padding:12px 16px;background:#f8fafc;border-radius:8px;font-size:12px;color:#334155">
                <div style="font-weight:600;margin-bottom:6px">${titel}</div>
                <table style="width:100%;border-collapse:collapse">
                    <tbody>${rows}</tbody>
                    ${footer ? `<tfoot>${footer}</tfoot>` : ''}
                </table>
            </div>`;
    };

    let breakdownHtml = '';
    if (d.regel === 'A') {
        if (d.vertragsModell === 'FIX' || d.vertragsModell === 'FIX-M') {
            breakdownHtml = `
                <div style="margin-top:12px;padding:12px 16px;background:#f8fafc;border-radius:8px;font-size:12px;color:#334155">
                    <div><b>Monatslohn:</b> CHF ${fmt(bd.monatsLohn)}</div>
                    <div style="margin-top:4px;color:#64748b">Formel: Monatslohn × 12 ÷ 365 = Tagessatz 100 %</div>
                </div>`;
        } else {
            breakdownHtml = `
                <div style="margin-top:12px;padding:12px 16px;background:#f8fafc;border-radius:8px;font-size:12px;color:#334155;line-height:1.7">
                    <div><b>Stundenlohn (Basis):</b> CHF ${fmt(bd.stundenlohnBasis, 2)}</div>
                    <div>+ Ferien ${fmt(bd.ferienPct, 2)} %, + 13. ML ${fmt(bd.zehnterMLPct, 2)} %</div>
                    <div style="color:#94a3b8;font-size:11px">ohne Feiertag % — Feiertagentschädigung ist AHV-pflichtiger Lohn und läuft während Krankheit als separate Lohnzeile (04.08.2026)</div>
                    <div><b>= Brutto-Stundenlohn:</b> CHF ${fmt(bd.stundenlohnBrutto, 4)}</div>
                    <div style="margin-top:6px"><b>Wochenstunden:</b> ${fmt(bd.wochenStunden, 2)} h (${d.vertragsModell === 'MTP' ? 'garantiert' : 'FLEX aus Filiale'})</div>
                    <div style="margin-top:4px;color:#64748b">Formel: Wochenstunden × Std-Lohn brutto × 52 ÷ 365 = Tagessatz 100 %</div>
                </div>`;
        }
        breakdownHtml += renderMonate(
            `ℹ️ Bisherige Lohnperioden <span style="font-weight:400;color:#64748b">(zur Information — flie\u00dft bei Regel A nicht in die Berechnung ein)</span>`,
            false
        );
    } else if (d.regel !== 'MANUELL') {
        const monate = bd.monate || [];
        breakdownHtml = renderMonate(
            `AHV-Brutto der letzten ${monate.length} Perioden`,
            true
        );
        if (d.vertragsModell === 'MTP') {
            breakdownHtml += `
                <div style="margin-top:8px;padding:12px 16px;background:#f8fafc;border-radius:8px;font-size:12px;color:#334155;line-height:1.7">
                    <div style="font-weight:600;margin-bottom:4px">MTP-Aufteilung:</div>
                    <div>Garantie-Basis/Monat: CHF ${fmt(bd.garantieBasisMonat)} &rarr; Tagessatz CHF ${fmt(bd.garantieTagessatz)}</div>
                    <div>Ø Mehrstunden/Monat (brutto): CHF ${fmt(bd.mehrstundenAnteilMonat)} &rarr; Tagessatz CHF ${fmt(bd.mehrstundenTagessatz)}</div>
                </div>`;
        }
        breakdownHtml += `
            <div style="margin-top:4px;font-size:12px;color:#64748b;padding:0 16px">
                Formel: Ø × 12 ÷ 365 = Tagessatz 100 %${d.vertragsModell === 'MTP' ? ' (Garantie- und Mehrstunden-Anteil summiert)' : ''}
            </div>`;
    }
    return breakdownHtml;
}

function _ktgRatesTableHtml(d) {
    const fmt = _ktgFmtChf;
    return `
        <table class="ktg-rates-table">
            <thead>
                <tr>
                    <th>TAGESSATZ</th>
                    <th>CHF / TAG</th>
                </tr>
            </thead>
            <tbody>
                <tr>
                    <td>100 %</td>
                    <td>CHF ${fmt(d.tagessatz100)}</td>
                </tr>
                ${d.karenzAbgeschlossen ? `
                <tr class="ktg-row-muted">
                    <td colspan="2">88 % — Karenz übersprungen (altes System)</td>
                </tr>
                ` : `
                <tr class="ktg-row-88">
                    <td>88 % — Karenzfrist</td>
                    <td>CHF ${fmt(d.tagessatz88)}</td>
                </tr>
                `}
                <tr class="ktg-row-80">
                    <td>80 % — Meldebetrag</td>
                    <td>CHF ${fmt(d.tagessatz80)}</td>
                </tr>
            </tbody>
        </table>`;
}

function _ktgOverrideBtnHtml(d) {
    const empId = selectedEmployee?.id || 0;
    const manuell = d.regel === 'MANUELL' ? d.tagessatz100 : '';
    return `<button class="btn btn-outline ktg-ov-btn" onclick="openKtgOverrideModal(${empId}, ${d.tagessatz100 || 0}, ${d.karenzAbgeschlossen ? 'true' : 'false'}, '${manuell}')">✎ Tagessatz übersteuern…</button>`;
}

/** Wochenstunden für Tagessatz-Meta (statt «N Per.» — Walter 25.07.2026). */
function _ktgMetaWochenstunden(d) {
    const wo = d?.breakdown?.wochenStunden;
    if (wo == null || !Number.isFinite(Number(wo))) return '— h/Wo.';
    const n = Number(wo);
    const txt = n.toLocaleString('de-CH', {
        minimumFractionDigits: Number.isInteger(n) ? 0 : 2,
        maximumFractionDigits: 2,
    });
    return `${txt} h/Wo.`;
}

function renderKtgTagessatzHtml(d, mode = 'full') {
    const fmt = _ktgFmtChf;
    const vs = d.vertragsStart ? new Date(d.vertragsStart).toLocaleDateString('de-CH') : '—';
    const badge = _ktgBadgeHtml(d.regel, mode === 'compact' || mode === 'side');
    const woLabel = _ktgMetaWochenstunden(d);
    const nPer = Number(d.anzahlPerioden) || 0;
    const perLabel = `${nPer} Lohnperiode${nPer === 1 ? '' : 'n'}`;
    // Modell · Vertragsstart · Wochenstunden · Anzahl Lohnperioden (Regel A/B).
    const meta = (mode === 'compact' || mode === 'side')
        ? `<b>${d.vertragsModell || '?'}</b> · ${vs} · ${woLabel} · ${perLabel}`
        : `Vertrag <b>${d.vertragsModell || '?'}</b> seit ${vs} · ${woLabel} · ${perLabel}`;

    if (mode === 'compact') {
        return `
            <div class="ktg-compact">
                <div class="ktg-compact-top">${badge}</div>
                <div class="ktg-compact-meta">${meta}</div>
                <div class="ktg-compact-rows">
                    <div><span>100 %</span><strong>CHF ${fmt(d.tagessatz100)}</strong></div>
                    ${d.karenzAbgeschlossen
                        ? `<div class="muted"><span>88 %</span><strong>übersprungen</strong></div>`
                        : `<div class="r88"><span>88 %</span><strong>CHF ${fmt(d.tagessatz88)}</strong></div>`}
                    <div class="r80"><span>80 %</span><strong>CHF ${fmt(d.tagessatz80)}</strong></div>
                </div>
                <button type="button" class="ov-more" style="margin-top:4px;border:none;background:none;padding:0;cursor:pointer;text-align:left"
                        onclick="switchEmpTab('absenzen')">Bei Absenzen anzeigen →</button>
            </div>`;
    }

    // Absenzen-Sidebar (side): Walter 26.07.2026 — Berechnung immer sichtbar
    // (genug Platz), kein Aufklappen mehr.
    if (mode === 'side') {
        const breakdown = _ktgBreakdownHtml(d);
        return `
            <div class="ktg-panel ktg-panel-side">
                <div class="ktg-panel-h">
                    <div class="ktg-panel-title">Tagessatz</div>
                    ${badge}
                </div>
                <div class="ktg-panel-meta">${meta}</div>
                <div class="ktg-compact-rows">
                    <div><span>100 %</span><strong>CHF ${fmt(d.tagessatz100)}</strong></div>
                    ${d.karenzAbgeschlossen
                        ? `<div class="muted"><span>88 %</span><strong>übersprungen</strong></div>`
                        : `<div class="r88"><span>88 %</span><strong>CHF ${fmt(d.tagessatz88)}</strong></div>`}
                    <div class="r80"><span>80 %</span><strong>CHF ${fmt(d.tagessatz80)}</strong></div>
                </div>
                <div class="ktg-panel-actions">${_ktgOverrideBtnHtml(d)}</div>
                ${breakdown ? `<div class="ktg-side-berechnung"><div class="ktg-side-berechnung-label">Berechnung</div>${breakdown}</div>` : ''}
            </div>`;
    }

    const breakdown = _ktgBreakdownHtml(d);
    return `
        <div class="ktg-panel ktg-panel-full" style="padding:20px">
            <div class="ktg-panel-h">
                <div class="ktg-panel-title">📊 KTG/UVG-Tagessatz</div>
                ${badge}
            </div>
            <div class="ktg-panel-meta">${meta}</div>
            ${_ktgRatesTableHtml(d)}
            <div class="ktg-panel-actions">${_ktgOverrideBtnHtml(d)}</div>
            ${breakdown}
        </div>`;
}

function _ovKtgSkeletonHtml() {
    // Absenzen-Sidebar-Fallback (Übersicht nutzt Saldi-Tabelle).
    return `<div class="ktg-compact ktg-compact-skel" aria-busy="true">
        <div class="ktg-compact-top"><span class="ktg-badge ktg-badge-a" style="opacity:.35">…</span></div>
        <div class="ktg-compact-meta" style="opacity:.45">— · — · —</div>
        <div class="ktg-compact-rows">
            <div><span>100 %</span><strong>· · ·</strong></div>
            <div class="r88"><span>88 %</span><strong>· · ·</strong></div>
            <div class="r80"><span>80 %</span><strong>· · ·</strong></div>
        </div>
    </div>`;
}

// ── Übersicht: Stunden & Saldi (Walter 19.07.2026) ─────────────────────
// Spalten einmal: Soll · gearb. · Absenz · Vorm. · Saldo
// Zeilen: Stunden · Ferien · Feiertage · Nacht
// Daten = dieselbe Calculate-Engine wie der Lohnlauf (kein eigener Report).
function _ovSaldiMode() {
    const m = localStorage.getItem('hrOvSaldiMode');
    return m === 'monat' ? 'monat' : 'aktuell'; // Default: per heute
}

function ovSaldiSetMode(mode) {
    const next = mode === 'monat' ? 'monat' : 'aktuell';
    localStorage.setItem('hrOvSaldiMode', next);
    _ovSaldiSyncSwitch();
    const cache = window._ovSaldiCache;
    const el = document.getElementById('ovSaldiContent');
    if (el && cache && cache.s)
        el.innerHTML = renderOvSaldiHtml(cache.s, cache.stRow, next);
}

function _ovSaldiSyncSwitch() {
    const mode = _ovSaldiMode();
    document.querySelectorAll('.ov-saldi-sw').forEach(btn => {
        btn.classList.toggle('on', btn.getAttribute('data-mode') === mode);
    });
}

function _ovSaldiSkeletonHtml() {
    const dash = '<td class="ov-saldi-dash">·</td>';
    return `<table class="ov-saldi-tbl" aria-busy="true">
        <thead><tr><th></th><th>Soll</th><th>gearb.</th><th>Absenz</th><th>Vorm.</th><th>Saldo</th></tr></thead>
        <tbody>
            <tr><td>Stunden</td>${dash}${dash}${dash}${dash}${dash}</tr>
            <tr><td>Ferien</td>${dash}${dash}${dash}${dash}${dash}</tr>
            <tr><td>Feiertage</td>${dash}${dash}${dash}${dash}${dash}</tr>
            <tr><td>Nacht</td>${dash}${dash}${dash}${dash}${dash}</tr>
        </tbody>
    </table>`;
}

function _ovSaldiNum(v, { signed = false, dashIfNull = false } = {}) {
    if (v == null || (dashIfNull && !Number.isFinite(Number(v))))
        return '<td class="ov-saldi-dash">–</td>';
    const n = Number(v);
    if (!Number.isFinite(n)) return '<td class="ov-saldi-dash">–</td>';
    const txt = (signed && n > 0.005 ? '+' : '') + n.toLocaleString('de-CH', {
        minimumFractionDigits: 1,
        maximumFractionDigits: 2
    });
    let cls = 'ov-saldi-n';
    if (signed) {
        if (n > 0.005) cls += ' ov-saldi-pos';
        else if (n < -0.005) cls += ' ov-saldi-neg';
    }
    return `<td class="${cls}">${txt}</td>`;
}

function _ovSaldiDash() { return '<td class="ov-saldi-dash">–</td>'; }

function renderOvSaldiHtml(s, stRow, mode) {
    if (!s) return _ovSaldiSkeletonHtml();
    mode = mode || _ovSaldiMode();
    const model = (s.employmentModel || (stRow && stRow.model) || '').toUpperCase();
    const isFlex = model === 'FLEX' || model === 'UTP';
    const isFix  = model === 'FIX' || model === 'FIX-M';
    const isAktuell = mode !== 'monat';

    // Stunden: «aktuell» = Stichtag-Block (blau im Sollstunden-Report),
    // «Monat» = Monats-Block — dieselben Felder wie dort.
    let rowStunden;
    if (isFlex) {
        const worked = Number(s.workedHours ?? 0);
        rowStunden = `<tr><td>Stunden</td>${_ovSaldiDash()}${_ovSaldiNum(worked)}${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}</tr>`;
    } else if (stRow) {
        const p = isAktuell ? 'st' : 'mt';
        const sollH  = Number(stRow[p + 'Soll'] ?? 0);
        const gearbH = Number(stRow[p + 'Gearb'] ?? 0);
        const absH   = Number(stRow[p + 'Absenz'] ?? 0);
        const vorH   = Number(stRow[p + 'SaldoVor'] ?? 0);
        const saldoH = Number(stRow[p + 'Saldo'] ?? 0);
        rowStunden = `<tr><td>Stunden</td>${_ovSaldiNum(sollH)}${_ovSaldiNum(gearbH)}${_ovSaldiNum(absH)}${_ovSaldiNum(vorH, { signed: true })}${_ovSaldiNum(saldoH, { signed: true })}</tr>`;
    } else {
        rowStunden = `<tr><td>Stunden</td>${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}</tr>`;
    }

    // Ferien / Feiertage / Nacht — Monatswerte aus Calculate (kein Stichtag-Block)
    const ferSoll  = Number(s.ferienTageAccrual ?? 0);
    const ferAbs   = Number(s.ferienTageGenommen ?? 0);
    const ferVor   = Number(s.vormonatFerienTage ?? 0);
    const ferSaldo = Number(s.ferienTageSaldoNeu ?? 0);

    const ftSoll  = Number(s.feiertagTageAccrual ?? 0);
    const ftAbs   = Number(s.feiertagTageGenommen ?? 0);
    const ftVor   = Number(s.vormonatFeiertagTage ?? 0);
    const ftSaldo = Number(s.feiertagTageSaldoNeu ?? 0);

    const nachtGearb = Number(s.nightHours ?? 0);
    const nachtAbs   = Number(s.nachtKompStunden ?? 0);
    const nachtVor   = Number(s.vormonatNachtSaldo ?? 0);
    const nachtSaldo = Number(s.neuerNachtSaldo ?? 0);

    const rowFerien = `<tr><td>Ferien</td>${_ovSaldiNum(ferSoll)}${_ovSaldiDash()}${_ovSaldiNum(ferAbs)}${_ovSaldiNum(ferVor, { signed: true })}${_ovSaldiNum(ferSaldo, { signed: true })}</tr>`;

    const rowFeiertag = isFix
        ? `<tr><td>Feiertage</td>${_ovSaldiNum(ftSoll)}${_ovSaldiDash()}${_ovSaldiNum(ftAbs)}${_ovSaldiNum(ftVor, { signed: true })}${_ovSaldiNum(ftSaldo, { signed: true })}</tr>`
        : `<tr><td>Feiertage</td>${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}</tr>`;

    const rowNacht = `<tr><td>Nacht</td>${_ovSaldiDash()}${_ovSaldiNum(nachtGearb)}${_ovSaldiNum(nachtAbs)}${_ovSaldiNum(nachtVor, { signed: true })}${_ovSaldiNum(nachtSaldo, { signed: true })}</tr>`;

    return `<table class="ov-saldi-tbl">
        <thead><tr><th></th><th>Soll</th><th>gearb.</th><th>Absenz</th><th>Vorm.</th><th>Saldo</th></tr></thead>
        <tbody>${rowStunden}${rowFerien}${rowFeiertag}${rowNacht}</tbody>
    </table>`;
}

async function loadOvSaldi(employeeId) {
    const el = document.getElementById('ovSaldiContent');
    if (!el || !employeeId) return;

    const gen = (window._ovSaldiLoadGen = (window._ovSaldiLoadGen || 0) + 1);
    el.innerHTML = _ovSaldiSkeletonHtml();
    _ovSaldiSyncSwitch();

    const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
        ? fixedCompanyProfileId
        : (typeof selectedCompanyProfile !== 'undefined' && selectedCompanyProfile?.id)
        ? selectedCompanyProfile.id
        : null;
    if (!cid) {
        el.innerHTML = `<div class="ov-saldi-msg">Bitte Filiale wählen.</div>`;
        return;
    }

    const now = new Date();
    const year  = parseInt(document.getElementById('lohnYearSelect')?.value  || now.getFullYear(), 10);
    const month = parseInt(document.getElementById('lohnMonthSelect')?.value || (now.getMonth() + 1), 10);
    const hdr = typeof ah === 'function' ? ah() : { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` };
    const ts = Date.now();

    try {
        // Calculate → Ferien/Feiertag/Nacht; Sollstunden-Report (1 MA) → Stunden st/mt
        const [calcRes, stRes] = await Promise.all([
            fetch(`/api/payroll/calculate?employeeId=${employeeId}&year=${year}&month=${month}&companyProfileId=${cid}&_=${ts}`,
                { headers: hdr, cache: 'no-store' }),
            fetch(`/api/payroll/sollstunden-report?companyProfileId=${cid}&year=${year}&month=${month}&employeeId=${employeeId}&_=${ts}`,
                { headers: hdr, cache: 'no-store' })
        ]);
        if (gen !== window._ovSaldiLoadGen) return;
        if (calcRes.status === 404) {
            el.innerHTML = `<div class="ov-saldi-msg">Kein Vertrag in dieser Periode.</div>`;
            return;
        }
        if (!calcRes.ok) {
            el.innerHTML = `<div class="ov-saldi-msg ov-saldi-err">Fehler ${calcRes.status}</div>`;
            return;
        }
        const s = await calcRes.json();
        let stRow = null;
        if (stRes.ok) {
            const st = await stRes.json();
            stRow = (st.rows || []).find(r => r.employeeId === employeeId) || null;
        }
        if (gen !== window._ovSaldiLoadGen) return;
        window._ovSaldiCache = { empId: employeeId, s, stRow };
        _ovSaldiSyncSwitch();
        el.innerHTML = renderOvSaldiHtml(s, stRow, _ovSaldiMode());
    } catch (e) {
        if (gen !== window._ovSaldiLoadGen) return;
        el.innerHTML = `<div class="ov-saldi-msg ov-saldi-err">Fehler: ${esc(e.message || String(e))}</div>`;
    }
}

async function loadKtgTab(employeeId) {
    // Nur noch Absenzen-Sidebar (Übersicht = Saldi-Tabelle).
    const side = document.getElementById('ktgTagessatzSidebar');
    if (!side || !employeeId) return;

    const gen = (window._ktgLoadGen = (window._ktgLoadGen || 0) + 1);
    side.innerHTML = '<div style="padding:16px;text-align:center;color:#94a3b8;font-size:13px">Lade…</div>';

    try {
        const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
            ? fixedCompanyProfileId
            : (typeof selectedCompanyProfile !== 'undefined' && selectedCompanyProfile?.id)
            ? selectedCompanyProfile.id
            : null;

        if (!cid) {
            side.innerHTML = '<div style="padding:16px;color:#94a3b8;font-size:13px">Bitte Filiale wählen.</div>';
            return;
        }

        const res = await fetch(`/api/payroll/ktg-tagessatz?employeeId=${employeeId}&companyProfileId=${cid}`,
            { headers: { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` } });

        if (gen !== window._ktgLoadGen) return;

        if (res.status === 404) {
            side.innerHTML = `<div style="padding:20px;text-align:center;color:#94a3b8;font-size:13px">Kein aktives Anstellungsverhältnis gefunden.</div>`;
            return;
        }
        if (!res.ok) {
            side.innerHTML = `<div style="padding:16px;color:#dc2626;font-size:13px">Fehler ${res.status}</div>`;
            return;
        }

        const d = await res.json();
        if (gen !== window._ktgLoadGen) return;
        side.innerHTML = `<div class="ktg-side-card">${renderKtgTagessatzHtml(d, 'side')}</div>`;
    } catch (e) {
        if (gen !== window._ktgLoadGen) return;
        side.innerHTML = `<div style="padding:16px;color:#dc2626;font-size:13px">Fehler: ${e.message}</div>`;
    }
}

// ══════════════════════════════════════════════
// KTG/UVG-Tagessatz: Manueller Override
// Walter-Vorgabe: Legacy-MA aus altem Lohnsystem hat u.U. schon einen
// errechneten Tagessatz (Versicherer-Standard). Plus: Karenz kann
// bereits abgelaufen sein → direkt 80% Meldebetrag.
// ══════════════════════════════════════════════
function openKtgOverrideModal(employeeId, autoTagessatz100, karenzAbgeschlossen, manuellExisting) {
    if (!employeeId) { alert('Kein Mitarbeiter ausgewählt.'); return; }
    const manuellVal = manuellExisting && manuellExisting !== '0'
        ? Number(manuellExisting).toFixed(2) : '';
    const html = `
        <div id="ktgOvBackdrop" style="position:fixed;inset:0;background:rgba(0,0,0,.5);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px"
             onclick="if(event.target===this) closeKtgOverrideModal()">
            <div style="background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);border-radius:12px;width:100%;max-width:520px;box-shadow:0 20px 60px rgba(0,0,0,.3);overflow:hidden">
                <div style="padding:16px 22px;border-bottom:1px solid #e2e8f0">
                    <div style="font-size:15px;font-weight:700;color:#1e293b">KTG/UVG-Tagessatz übersteuern</div>
                    <div style="font-size:12px;color:#64748b;margin-top:2px">Auto-Berechnung: CHF ${Number(autoTagessatz100).toFixed(2)} / Tag (100 %)</div>
                </div>
                <div style="padding:18px 22px">
                    <label style="font-size:12px;font-weight:600;color:#374151;display:block">Manueller Tagessatz 100 % (CHF)</label>
                    <input type="number" step="0.01" min="0" id="ktgOvManuell" value="${manuellVal}"
                           placeholder="z.B. 36.25"
                           style="width:100%;padding:9px 12px;border:1px solid #d1d5db;border-radius:7px;font-size:14px;margin-top:4px">
                    <div style="font-size:11px;color:#64748b;margin-top:3px">Leer lassen = Auto-Berechnung verwenden. Die 88-/80-%-Stufen werden aus diesem Wert abgeleitet.</div>

                    <label style="display:flex;align-items:center;gap:8px;margin-top:18px;cursor:pointer">
                        <input type="checkbox" id="ktgOvKarenz" ${karenzAbgeschlossen ? 'checked' : ''}>
                        <span>
                            <span style="font-size:13px;font-weight:600;color:#0f172a">Karenz bereits abgeschlossen</span>
                            <span style="display:block;font-size:11.5px;color:#64748b;margin-top:2px">Im alten Lohnsystem wurde die Karenzfrist bereits durchlaufen — die Versicherung zahlt direkt 80 % (Meldebetrag), kein 88 %-Schritt mehr.</span>
                        </span>
                    </label>

                    <div id="ktgOvError" style="color:#dc2626;font-size:12px;margin-top:12px"></div>
                </div>
                <div style="padding:14px 22px;border-top:1px solid #f1f5f9;display:flex;justify-content:flex-end;gap:10px">
                    <button class="btn btn-outline" onclick="closeKtgOverrideModal()">Abbrechen</button>
                    <button class="btn btn-primary" onclick="saveKtgOverride(${employeeId})">Speichern</button>
                </div>
            </div>
        </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

function closeKtgOverrideModal() {
    document.getElementById('ktgOvBackdrop')?.remove();
}

async function saveKtgOverride(employeeId) {
    const errEl    = document.getElementById('ktgOvError');
    const manRaw   = document.getElementById('ktgOvManuell').value.trim();
    const karenz   = document.getElementById('ktgOvKarenz').checked;
    let manVal = null;
    if (manRaw !== '') {
        manVal = parseFloat(manRaw);
        if (!Number.isFinite(manVal) || manVal < 0) {
            errEl.textContent = 'Ungültiger Betrag.'; return;
        }
    }
    try {
        const res = await fetch(`/api/employees/${employeeId}`, {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                ktgTagessatzManuellSet: true,
                ktgTagessatzManuell:    manVal,
                ktgKarenzAbgeschlossen: karenz
            })
        });
        if (!res.ok) { const e = await res.json().catch(()=>({})); throw new Error(e.error || e.message || `HTTP ${res.status}`); }
        closeKtgOverrideModal();
        if (typeof showToast === 'function') {
            showToast(manVal !== null ? 'Manueller Tagessatz gespeichert' : 'Auto-Berechnung wieder aktiv', 'success');
        }
        // KTG-Tab neu laden
        if (typeof loadKtgTab === 'function') loadKtgTab(employeeId);
    } catch (e) {
        errEl.textContent = e.message;
    }
}

// TAB: Formulare / Arbeitslosigkeit / Zwischenverdienst
// ══════════════════════════════════════════════

async function loadFormulareTab(employeeId) {
    // Monat vorbelegen (aktueller Monat)
    const now = new Date();
    const selMonat = document.getElementById('zvMonat');
    const selJahr  = document.getElementById('zvJahr');
    if (selMonat && !selMonat._initialized) {
        selMonat.value = now.getMonth() + 1;
        selMonat._initialized = true;
    }

    // (QST-Anmeldung braucht keinen Filial-Picker mehr — Filiale + Kanton
    //  werden im Backend automatisch aus den MA-Daten ermittelt.)

    // Arbeitslosigkeits-Einträge laden
    try {
        const res = await fetch(`/api/zwischenverdienist/arbeitslosigkeit/${employeeId}`, { headers: ah() });
        if (!res.ok) throw new Error();
        const list = await res.json();
        renderArbeitslosigkeitList(list, employeeId);
    } catch {
        document.getElementById('arbeitslosigkeitList').innerHTML =
            '<p style="color:#ef4444;font-size:13px">Fehler beim Laden.</p>';
    }
}

function renderArbeitslosigkeitList(list, employeeId) {
    const el = document.getElementById('arbeitslosigkeitList');
    if (!list.length) {
        el.innerHTML = '<p style="color:#94a3b8;font-size:13px">Noch keine Arbeitslosigkeits-Perioden erfasst.</p>';
        return;
    }
    const rows = list.map(a => `
      <tr>
        <td>${fmtDate(a.angemeldetSeit)}</td>
        <td>${a.abgemeldetAm ? fmtDate(a.abgemeldetAm) : '<span style="color:#22c55e;font-weight:600">aktiv</span>'}</td>
        <td>${a.ravStelle || '–'}</td>
        <td>${a.ravKundennummer || '–'}</td>
        <td>${a.arbeitslosenkasse || '–'}</td>
        <td style="white-space:nowrap">
          <button class="btn-icon" onclick="editArbeitslosigkeit(${JSON.stringify(a).replace(/"/g,'&quot;')})">✏️</button>
          <button class="btn-icon" onclick="deleteArbeitslosigkeit(${a.id},${employeeId})">🗑️</button>
        </td>
      </tr>`).join('');
    el.innerHTML = `
      <table class="data-table" style="font-size:12px">
        <thead><tr>
          <th>Angemeldet seit</th><th>Abgemeldet am</th>
          <th>RAV-Stelle</th><th>Kundennr.</th><th>ALK</th><th></th>
        </tr></thead>
        <tbody>${rows}</tbody>
      </table>`;
}

function fmtDate(ds) {
    if (!ds) return '';
    const d = new Date(ds);
    return `${String(d.getUTCDate()).padStart(2,'0')}.${String(d.getUTCMonth()+1).padStart(2,'0')}.${d.getUTCFullYear()}`;
}

function openArbeitslosigkeitForm(entry) {
    document.getElementById('alId').value               = entry?.id || '';
    document.getElementById('alAngemeldetSeit').value   = entry?.angemeldetSeit?.slice(0,10) || '';
    document.getElementById('alAbgemeldetAm').value     = entry?.abgemeldetAm?.slice(0,10)   || '';
    document.getElementById('alRavStelle').value        = entry?.ravStelle        || '';
    document.getElementById('alRavKundennummer').value  = entry?.ravKundennummer  || '';
    document.getElementById('alArbeitslosenkasse').value= entry?.arbeitslosenkasse|| '';
    document.getElementById('alBemerkung').value        = entry?.bemerkung        || '';
    document.getElementById('arbeitslosigkeitInlineForm').style.display = 'block';
}

function editArbeitslosigkeit(entry) { openArbeitslosigkeitForm(entry); }

function closeArbeitslosigkeitForm() {
    document.getElementById('arbeitslosigkeitInlineForm').style.display = 'none';
}

async function saveArbeitslosigkeit() {
    const id    = document.getElementById('alId').value;
    const since = document.getElementById('alAngemeldetSeit').value;
    if (!since) { alert('Anmeldedatum ist erforderlich.'); return; }

    const body = {
        employeeId:       selectedEmployeeId,
        angemeldetSeit:   since,
        abgemeldetAm:     document.getElementById('alAbgemeldetAm').value || null,
        ravStelle:        document.getElementById('alRavStelle').value.trim()         || null,
        ravKundennummer:  document.getElementById('alRavKundennummer').value.trim()   || null,
        arbeitslosenkasse:document.getElementById('alArbeitslosenkasse').value.trim() || null,
        bemerkung:        document.getElementById('alBemerkung').value.trim()         || null,
    };

    const url    = id ? `/api/zwischenverdienist/arbeitslosigkeit/${id}` : '/api/zwischenverdienist/arbeitslosigkeit';
    const method = id ? 'PUT' : 'POST';

    try {
        const res = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!res.ok) { alert('Fehler beim Speichern.'); return; }
        closeArbeitslosigkeitForm();
        loadFormulareTab(selectedEmployeeId);
    } catch { alert('Verbindungsfehler.'); }
}

async function deleteArbeitslosigkeit(id, employeeId) {
    if (!(await liquidConfirm('Eintrag löschen?'))) return;
    try {
        const res = await fetch(`/api/zwischenverdienist/arbeitslosigkeit/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadFormulareTab(employeeId);
    } catch { alert('Verbindungsfehler.'); }
}

async function generateZwischenverdienst() {
    const monat = document.getElementById('zvMonat').value;
    const jahr  = document.getElementById('zvJahr').value;

    // companyProfileId aus dem aktuellen Mitarbeiter ermitteln
    const cpId = selectedEmployee?.employments?.[0]?.companyProfileId
              || selectedEmployee?.companyProfileId
              || 1;

    const url = `/api/zwischenverdienist/pdf?employeeId=${selectedEmployeeId}&year=${jahr}&month=${monat}&companyProfileId=${cpId}`;

    // PDF in neuem Tab öffnen (Browser zeigt Download-Dialog)
    window.open(url, '_blank');
}

// ── QST-Anmeldeformular generieren ──────────────────────────────────────
// Keine Parameter mehr nötig: das Backend ermittelt die Filiale aus dem
// aktiven Arbeitsverhältnis des MA und den Kanton aus dem aktiven QST-Eintrag
// (Wohnsitz-Kanton). Walter wollte die manuelle Auswahl loswerden, weil
// beides eindeutig aus den MA-Daten herleitbar ist.
async function generateQstAnmeldung() {
    if (!selectedEmployeeId) return;
    window.open(`/api/qst-anmeldung/${selectedEmployeeId}/pdf`, '_blank');
}

// ══════════════════════════════════════════════════════════════════
// BANKVERBINDUNG (pro MA, mit Historie)
// ══════════════════════════════════════════════════════════════════

async function loadBankAccountsTab(employeeId) {
    const el = document.getElementById('bankAccountsContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const res = await fetch(`/api/employee-bank-accounts/employee/${employeeId}`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>'; return; }
        const list = await res.json();
        renderBankAccountsList(el, list);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden.</span></div>';
    }
}

function renderBankAccountsList(el, list) {
    if (!Array.isArray(list) || list.length === 0) {
        el.innerHTML = '<div style="padding:16px;color:#94a3b8;font-style:italic;font-size:13px">Noch keine Bankverbindung erfasst. Über "Neue Bankverbindung" erfassen.</div>';
        return;
    }
    const today = new Date().toISOString().slice(0, 10);
    const rows = list.map(b => {
        const active = b.validFrom <= today && (!b.validTo || b.validTo >= today);
        const status = active
            ? '<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#dcfce7;color:#166534">Aktiv</span>'
            : (b.validFrom > today
                ? '<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#ece9e2;color:#6b6152">Geplant</span>'
                : '<span style="font-size:11px;padding:2px 8px;border-radius:10px;background:#f1f5f9;color:#64748b">Abgelaufen</span>');
        const bisTxt = b.validTo ? fmtDate(b.validTo) : '<span style="color:#94a3b8">offen</span>';
        const inhaber = b.kontoinhaber ? `<div style="font-size:11px;color:#64748b;margin-top:2px">Inhaber: ${b.kontoinhaber}</div>` : '';
        const ref     = b.zahlungsreferenz ? `<div style="font-size:11px;color:#64748b;font-family:ui-monospace,Menlo,Consolas,monospace">Ref: ${b.zahlungsreferenz}</div>` : '';
        const hauptbankBadge = b.isHauptbank
            ? '<span style="font-size:10px;font-weight:600;padding:1px 7px;border-radius:10px;background:#ece9e2;color:#6b6152;margin-left:6px">Hauptbank</span>'
            : '';
        let aufteilungInfo = '';
        if (b.aufteilungTyp && b.aufteilungTyp !== 'VOLL' && b.aufteilungWert != null) {
            const w = Number(b.aufteilungWert);
            const txt = b.aufteilungTyp === 'PROZENT'        ? `${w}% vom Brutto`
                      : b.aufteilungTyp === 'FIXBETRAG'      ? `CHF ${w.toFixed(2)} (fix)`
                      : b.aufteilungTyp === 'NETTO_ABZUEGLICH' ? `Netto − CHF ${w.toFixed(2)}`
                      : '';
            if (txt) aufteilungInfo = `<div style="font-size:11px;color:#7c3aed;margin-top:2px">Aufteilung: ${txt}</div>`;
        }
        return `<tr style="${active ? '' : 'opacity:0.65;'}border-bottom:1px solid #f1f5f9">
            <td style="padding:10px 14px">
                <div style="font-family:ui-monospace,Menlo,Consolas,monospace;font-weight:600">${formatIbanDisplay(b.iban)}${hauptbankBadge}</div>
                <div style="font-size:11px;color:#64748b">${b.bankName ?? ''}${b.bic ? ' · ' + b.bic : ''}</div>
                ${inhaber}
                ${ref}
                ${aufteilungInfo}
            </td>
            <td style="padding:10px 14px;font-size:12px">${fmtDate(b.validFrom)} – ${bisTxt}</td>
            <td style="padding:10px 14px;text-align:center">${status}</td>
            <td style="padding:10px 14px;color:#94a3b8;font-size:12px">${b.bemerkung ?? ''}</td>
            <td style="padding:10px 14px;text-align:right;white-space:nowrap">
                ${b.inLohnVerwendet
                    ? `<span title="Diese Bankverbindung wurde bereits in einem Lohnlauf verwendet und ist nicht mehr editierbar. Für Änderungen: '+ Neue Bankverbindung' oben rechts." style="display:inline-flex;align-items:center;gap:4px;font-size:11px;font-weight:600;color:#b91c1c;background:#fee2e2;padding:4px 10px;border-radius:12px;cursor:help;">🔒 In Lohn verwendet</span>`
                    : `<div class="dok-menu-wrap" style="display:inline-block">
                        <button class="dok-menu-btn" onclick="bankToggleMenu(event, ${b.id})" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="bankMenu-${b.id}">
                            <button class="dok-menu-item" onclick='openBankAccountModal(${JSON.stringify(b).replace(/'/g,"&#39;")})'>Bearbeiten</button>
                            <button class="dok-menu-item danger" onclick="deleteBankAccount(${b.id})">Löschen</button>
                        </div>
                       </div>`}
            </td>
        </tr>`;
    }).join('');
    el.innerHTML = `<table style="width:100%;font-size:13px;border-collapse:collapse;margin-top:4px">
        <thead><tr style="color:#64748b;text-align:left;border-bottom:1px solid #e2e8f0">
            <th style="padding:8px 14px;font-weight:600">IBAN / Bank</th>
            <th style="padding:8px 14px;font-weight:600">Gültigkeit</th>
            <th style="padding:8px 14px;font-weight:600;text-align:center">Status</th>
            <th style="padding:8px 14px;font-weight:600">Bemerkung</th>
            <th></th>
        </tr></thead>
        <tbody>${rows}</tbody>
    </table>`;
}

function formatIbanDisplay(iban) {
    if (!iban) return '';
    const clean = iban.replace(/\s+/g, '');
    return clean.replace(/(.{4})/g, '$1 ').trim();
}

async function openBankAccountModal(existing) {
    const modal = document.getElementById('bankAccountModal');
    if (!modal) return;
    modal.style.display = 'flex';
    modal.dataset.editId = existing?.id ?? '';
    document.getElementById('baModalTitle').textContent = existing
        ? _t('bank.modalTitleEdit','Bankverbindung bearbeiten')
        : _t('bank.modalTitleNew','Bankverbindung erfassen');
    if (window.i18n && window.i18n.applyAll) window.i18n.applyAll(modal);

    const today = new Date().toISOString().slice(0, 10);
    document.getElementById('baIban').value     = existing?.iban ?? '';
    document.getElementById('baBic').value      = existing?.bic ?? '';
    document.getElementById('baBankName').value = existing?.bankName ?? '';
    document.getElementById('baKontoinhaber').value = existing?.kontoinhaber ?? '';
    document.getElementById('baKontoinhaberStrasse').value = existing?.kontoinhaberStrasse ?? '';
    document.getElementById('baKontoinhaberPlz').value = existing?.kontoinhaberPlz ?? '';
    document.getElementById('baKontoinhaberOrt').value = existing?.kontoinhaberOrt ?? '';
    document.getElementById('baKontoinhaberLand').value = existing?.kontoinhaberLand ?? '';
    onKontoinhaberChange();   // Adressblock je nach Kontoinhaber ein-/ausblenden
    document.getElementById('baZahlungsreferenz').value = existing?.zahlungsreferenz ?? '';
    document.getElementById('baBemerkung').value = existing?.bemerkung ?? '';
    const validFromEl = document.getElementById('baValidFrom');
    validFromEl.value = existing?.validFrom ?? today;
    document.getElementById('baValidTo').value   = existing?.validTo ?? '';

    // Lohnlauf-Sperre: min-Date für ValidFrom auf erstes freies Datum setzen.
    // Beim Edit lassen wir's offen (das alte ValidFrom darf nicht künstlich
    // nach vorne springen — der existing-Datensatz ist sowieso nur editierbar
    // wenn die Bank NICHT inLohnVerwendet ist, sonst wird der Bleistift gar
    // nicht angezeigt).
    if (window.lohnEditLock && !existing && typeof fixedCompanyProfileId !== 'undefined') {
        const state = await window.lohnEditLock.loadState(fixedCompanyProfileId);
        window.lohnEditLock.applyToDateInput(validFromEl, state);
        // Default ggf. nach vorne ziehen
        if (state.firstAllowedDate && validFromEl.value < state.firstAllowedDate) {
            validFromEl.value = state.firstAllowedDate;
        }
    }
    document.getElementById('baIsHauptbank').checked = existing?.isHauptbank ?? true;
    document.getElementById('baAufteilungTyp').value = existing?.aufteilungTyp ?? 'VOLL';
    document.getElementById('baAufteilungWert').value = existing?.aufteilungWert ?? '';
    onAufteilungTypChange();

    // Initiales Live-Feedback falls IBAN schon gefüllt
    validateIbanFieldMa(document.getElementById('baIban'));
}

function onAufteilungTypChange() {
    const typ = document.getElementById('baAufteilungTyp').value;
    const wertEl   = document.getElementById('baAufteilungWert');
    const labelEl  = document.getElementById('baAufteilungWertLabel');
    const hintEl   = document.getElementById('baAufteilungHint');
    if (typ === 'VOLL') {
        wertEl.disabled = true;
        wertEl.value = '';
        labelEl.textContent = '—';
        if (hintEl) hintEl.textContent = 'Dieses Konto bekommt den gesamten Rest-Nettolohn.';
    } else {
        wertEl.disabled = false;
        if (typ === 'PROZENT') {
            labelEl.textContent = 'Prozent';
            if (hintEl) hintEl.textContent = 'Prozentualer Anteil vom Bruttolohn, der auf dieses Konto geht.';
        } else if (typ === 'FIXBETRAG') {
            labelEl.textContent = 'CHF';
            if (hintEl) hintEl.textContent = 'Fixer CHF-Betrag, der auf dieses Konto geht. Rest wird auf die Hauptbank überwiesen.';
        } else if (typ === 'NETTO_ABZUEGLICH') {
            labelEl.textContent = 'CHF';
            if (hintEl) hintEl.textContent = 'Nettolohn minus dieser CHF-Betrag — z.B. "Lohn minus 500 CHF fürs Sparkonto".';
        }
    }
}

function closeBankAccountModal() {
    const modal = document.getElementById('bankAccountModal');
    if (modal) { modal.style.display = 'none'; modal.dataset.editId = ''; }
}

/// Empfänger-Adressblock ein-/ausblenden je nachdem ob ein abweichender
/// Kontoinhaber/Empfänger gesetzt ist. Bei Revolut/Wise muss die volle
/// Adresse erfasst sein, sonst akzeptiert die Schweizer Bank die SEPA-Zahlung
/// nicht.
function onKontoinhaberChange() {
    const k = document.getElementById('baKontoinhaber')?.value.trim() || '';
    const wrap = document.getElementById('baKontoinhaberAddrWrap');
    if (wrap) wrap.style.display = k.length > 0 ? 'block' : 'none';
}

// MA-Variante der IBAN-Validierung: nutzt validateIban() aus admin-settings.js
// und füllt BIC/Bankname im Bank-Modal.
async function validateIbanFieldMa(inputEl) {
    const hint = document.getElementById('baIbanHint');
    if (!hint) return;
    const val = inputEl.value.trim();
    if (!val) { hint.textContent = ''; hint.style.color = ''; inputEl.style.borderColor = ''; return; }
    // validateIban() ist global aus admin-settings.js
    const r = (typeof validateIban === 'function') ? validateIban(val, 'IBAN') : { valid: true };
    if (r.valid) {
        hint.textContent = `✓ Gültige IBAN${r.country ? ' (' + r.country + ')' : ''}`;
        hint.style.color = '#16a34a';
        inputEl.style.borderColor = '#86efac';
        if (r.country === 'CH' || r.country === 'LI') {
            try {
                const res = await fetch(`/api/banks/lookup?iban=${encodeURIComponent(val)}`, { headers: ah() });
                if (res.ok) {
                    const b = await res.json();
                    hint.textContent += ` — ${b.name}${b.ort ? ', ' + b.ort : ''}`;
                    const bicEl  = document.getElementById('baBic');
                    const nameEl = document.getElementById('baBankName');
                    if (bicEl  && !bicEl.value.trim()  && b.bic)  bicEl.value  = b.bic;
                    if (nameEl && !nameEl.value.trim() && b.name) nameEl.value = b.name;
                }
            } catch {}
        }
    } else {
        hint.textContent = '✗ ' + r.error;
        hint.style.color = '#dc2626';
        inputEl.style.borderColor = '#fca5a5';
    }
}

async function saveBankAccount() {
    const modal = document.getElementById('bankAccountModal');
    const editId = modal?.dataset.editId;
    const iban   = document.getElementById('baIban').value.trim();
    const from   = document.getElementById('baValidFrom').value;
    const to     = document.getElementById('baValidTo').value;
    if (!iban) { alert('IBAN ist erforderlich.'); return; }
    if (!from) { alert('"Gültig ab"-Datum ist erforderlich.'); return; }
    if (to && to < from) { alert('"Gültig bis" muss nach "Gültig ab" liegen.'); return; }

    // IBAN-Validierung vor dem Speichern (mit Confirm falls ungültig)
    if (typeof validateIban === 'function') {
        const r = validateIban(iban, 'IBAN');
        if (!r.valid && !(await liquidConfirm(`Die IBAN scheint ungültig:\n${r.error}\n\nTrotzdem speichern?`))) return;
    }

    // Sicherheits-Check: Bemerkung wie „Lohnzahlung Iksarenko Maryna 756.5352.1067.16"
    // gegen den aktuell selektierten MA gegenprüfen. Schützt vor dem Fall,
    // dass man am falschen MA-Tab landet und versehentlich das Konto eines
    // anderen MA erfasst (Bug: ORS-Service-Konto landete bei Medine statt
    // bei Maryna). Prüfung in dieser Reihenfolge — die erste eindeutige
    // Diskrepanz löst eine Warnung aus, der Nutzer muss bestätigen.
    const bemerkungVal = document.getElementById('baBemerkung').value.trim();
    if (bemerkungVal && selectedEmployee) {
        const maFirst = (selectedEmployee.firstName || '').trim();
        const maLast  = (selectedEmployee.lastName  || '').trim();
        const maAhv   = (selectedEmployee.socialSecurityNumber || '').replace(/\s+/g, '');

        // 1. AHV-Match — eindeutigster Fingerabdruck.
        const ahvMatch = bemerkungVal.match(/756\.\d{4}\.\d{4}\.\d{2}/);
        if (ahvMatch && maAhv && ahvMatch[0] !== maAhv) {
            const msg = `Achtung: Die Bemerkung enthält die AHV-Nummer ${ahvMatch[0]}, der aktuell ausgewählte MA ${maFirst} ${maLast} hat aber AHV ${maAhv}.\n\nWird das Konto wirklich diesem MA zugeordnet?`;
            if (!(await liquidConfirm(msg))) return;
        }
        // 2. Name-Match — Bemerkung beginnt typisch mit "Lohnzahlung X Y".
        else {
            const nameRe = /(?:Lohnzahlung|Lohn|Salär|Salaire)\s+([A-ZÄÖÜa-zäöü][\wäöüÄÖÜß'’-]+)\s+([A-ZÄÖÜa-zäöü][\wäöüÄÖÜß'’-]+)/u;
            const nm = bemerkungVal.match(nameRe);
            if (nm && maFirst && maLast) {
                const a = (nm[1] + ' ' + nm[2]).toLowerCase();
                const b = (maFirst + ' ' + maLast).toLowerCase();
                const bSwap = (maLast + ' ' + maFirst).toLowerCase();
                if (a !== b && a !== bSwap) {
                    const msg = `Achtung: Die Bemerkung enthält den Namen „${nm[1]} ${nm[2]}", der ausgewählte MA heisst aber ${maFirst} ${maLast}.\n\nWird das Konto wirklich diesem MA zugeordnet?`;
                    if (!(await liquidConfirm(msg))) return;
                }
            }
        }
    }

    const aufteilungTyp  = document.getElementById('baAufteilungTyp').value;
    const aufteilungWertRaw = document.getElementById('baAufteilungWert').value;
    const aufteilungWert = (aufteilungTyp !== 'VOLL' && aufteilungWertRaw) ? parseFloat(aufteilungWertRaw) : null;
    if (aufteilungTyp !== 'VOLL' && (aufteilungWert === null || !(aufteilungWert > 0))) {
        alert('Bei der gewählten Aufteilung muss ein Wert > 0 angegeben werden.'); return;
    }
    if (aufteilungTyp === 'PROZENT' && aufteilungWert > 100) {
        alert('Prozent-Wert darf max. 100 sein.'); return;
    }

    const body = {
        employeeId:           selectedEmployeeId,
        iban,
        bic:                  document.getElementById('baBic').value.trim() || null,
        bankName:             document.getElementById('baBankName').value.trim() || null,
        kontoinhaber:         document.getElementById('baKontoinhaber').value.trim() || null,
        kontoinhaberStrasse:  document.getElementById('baKontoinhaberStrasse').value.trim() || null,
        kontoinhaberPlz:      document.getElementById('baKontoinhaberPlz').value.trim() || null,
        kontoinhaberOrt:      document.getElementById('baKontoinhaberOrt').value.trim() || null,
        kontoinhaberLand:    (document.getElementById('baKontoinhaberLand').value.trim().toUpperCase() || null),
        zahlungsreferenz:     document.getElementById('baZahlungsreferenz').value.trim() || null,
        bemerkung:            document.getElementById('baBemerkung').value.trim() || null,
        isHauptbank:          document.getElementById('baIsHauptbank').checked,
        aufteilungTyp,
        aufteilungWert,
        validFrom:            from,
        validTo:              to || null
    };
    try {
        const url    = editId ? `/api/employee-bank-accounts/${editId}` : '/api/employee-bank-accounts';
        const method = editId ? 'PUT' : 'POST';
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            let msg = 'Fehler beim Speichern.';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch {}
            alert(msg);
            return;
        }
        closeBankAccountModal();
        loadBankAccountsTab(selectedEmployeeId);
        // Spezialfilter "ohne Bankverbindung" hat einen MA-ID-Cache —
        // beim nächsten Aktivieren neu vom Server holen.
        _empIdsWithActiveBank = null;
    } catch(e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function deleteBankAccount(id) {
    if (!(await liquidConfirm('Bankverbindung wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/employee-bank-accounts/${id}`, { method: 'DELETE', headers: ah() });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadBankAccountsTab(selectedEmployeeId);
        _empIdsWithActiveBank = null;  // Cache invalidieren (siehe saveBankAccount)
    } catch(e) { alert('Verbindungsfehler: ' + e.message); }
}

// ════════════════════════════════════════════════════════════════════
// EMPLOYEE-ADRESSEN (Korrespondenz, Ferienwohnung, Sozialamt, ...)
// Hauptadresse bleibt direkt am Employee (für QST/Wohnkanton-Logik).
// ════════════════════════════════════════════════════════════════════

const EMP_ADDRESS_TYPES = [
    'Korrespondenzadresse',
    // Walter 21.08.2026: eigene Typen für getrennt lebende Familien —
    // wichtig bei QST-/Halbfamilien-Abklärungen (wo wohnt der Partner /
    // das Kind?). Die Typen sind Beschriftungen; die Verknüpfung läuft wie
    // immer über das Familienmitglied (Andere Adresse → diese wählen).
    'Adresse Ehepartner',
    'Anderer Elternteil (Kind)',
    'Ferienwohnung',
    'Sozialamt',
    'Arbeitgeber',
    'Notfallkontakt',
    'Postanschrift',
    'Zweitwohnsitz',
    'Auslandsadresse',
    'Sonstige'
];

// PLZ-Lookup im Bankkonto-Modal („abweichender Empfänger") — füllt Ort
// und setzt Land=CH bei einer 4-stelligen CH-PLZ. Bei ausländischer PLZ
// (Land bereits gesetzt und ≠ CH) wird nichts gemacht.
async function baKontoinhaberPlzLookup(rawPlz) {
    const plz   = (rawPlz ?? '').toString().trim();
    const ortEl = document.getElementById('baKontoinhaberOrt');
    const landEl= document.getElementById('baKontoinhaberLand');
    const list  = document.getElementById('baKontoinhaberCityList');
    if (!ortEl) return;
    if (!/^\d{4}$/.test(plz)) return;
    const land = (landEl?.value ?? '').trim().toUpperCase();
    if (land && land !== 'CH') return;   // ausländisch — Schweizer-Lookup übergehen
    try {
        const res = await fetch(`/api/swiss-locations/by-plz?plz=${encodeURIComponent(plz)}`, { headers: ah() });
        if (!res.ok) return;
        const locs = await res.json();
        if (!Array.isArray(locs) || locs.length === 0) return;
        if (locs.length === 1) {
            ortEl.value  = locs[0].gemeindename;
            if (landEl && !landEl.value.trim()) landEl.value = 'CH';
            if (list) list.innerHTML = '';
            return;
        }
        if (list) {
            list.innerHTML = locs.map(l => `<option value="${l.gemeindename}">${l.kantonskuerzel}</option>`).join('');
        }
        if (landEl && !landEl.value.trim()) landEl.value = 'CH';
    } catch { /* still */ }
}

// Generischer PLZ-Lookup für beliebige Field-IDs (Hauptadresse hat eigene Funktion).
// cantonId/bfsId/hintId optional — z.B. Ärzte-Formular hat nur PLZ+Ort
// (Walter 20.07.2026: gleiche Auswahlliste bei mehreren Orten pro PLZ).
async function plzLookupGeneric(rawPlz, cityId, cantonId, bfsId, hintId) {
    const plz = (rawPlz ?? '').toString().trim();
    const cityEl   = document.getElementById(cityId);
    const cantonEl = cantonId ? document.getElementById(cantonId) : null;
    const bfsEl    = bfsId ? document.getElementById(bfsId) : null;
    const hint     = hintId ? document.getElementById(hintId) : null;
    if (!cityEl) return;

    if (!/^\d{4}$/.test(plz)) { if (hint) hint.innerHTML = ''; return; }

    let locs = _plzLookupCache.get(plz);
    if (!locs) {
        try {
            const res = await fetch(`/api/swiss-locations/by-plz?plz=${encodeURIComponent(plz)}`, { headers: ah() });
            if (!res.ok) return;
            locs = await res.json();
            _plzLookupCache.set(plz, locs);
        } catch { return; }
    }

    if (!locs.length) {
        if (hint) hint.innerHTML = `<span style="color:#b45309">⚠ PLZ ${plz} nicht gefunden — bitte manuell eintragen.</span>`;
        return;
    }
    if (locs.length === 1) {
        const l = locs[0];
        const ortName = stripCityCantonSuffix(l.ortschaftsname || l.gemeindename);
        cityEl.value = ortName;
        if (cantonEl) cantonEl.value = l.kantonskuerzel;
        if (bfsEl) bfsEl.value = l.bfsNr ?? l.bfsNumber ?? l.bfs_number ?? '';
        if (hint) hint.innerHTML = `<span style="color:#16a34a">✓ ${esc(ortName)}</span>`;
        return;
    }
    // Mehrere Treffer → Combobox im Ort-Feld via HTML5-<datalist>.
    // Gleiche UX wie bei der Hauptadresse: User klickt im Ort-Feld auf
    // den Pfeil und sieht die Vorschläge, kann aber frei tippen.
    bindDatalistToCityInput(cityEl, cantonEl, bfsEl, locs, plz, hint);
}

function _otherAddressesContainers() {
    // Alle Treffer (Fallback falls jemals doppelte IDs im DOM sind).
    return Array.from(document.querySelectorAll('#otherAddressesContent'));
}

async function loadEmployeeAddressesTab(employeeId) {
    const els = _otherAddressesContainers();
    if (!els.length) return;
    // Kein «Wird geladen…»-Platzhalter (Walter 18.07.2026): der würde die
    // Personalien-Karte kurz aufblasen und Verträge+KTG nach unten schieben.
    const gen = (window._addrLoadGen = (window._addrLoadGen || 0) + 1);
    try {
        const res = await fetch(`/api/employees/${employeeId}/addresses`, { headers: ah() });
        if (gen !== window._addrLoadGen) return;
        if (!res.ok) {
            els.forEach(el => { el.innerHTML = ''; });
            _ovUpdateAddrCardCount(0);
            return;
        }
        const list = await res.json();
        if (gen !== window._addrLoadGen) return;
        els.forEach(el => renderEmployeeAddressesList(el, list));
    } catch {
        if (gen !== window._addrLoadGen) return;
        els.forEach(el => { el.innerHTML = ''; });
        _ovUpdateAddrCardCount(0);
    }
}

function _ovUpdateAddrCardCount(n) {
    // Anzahl nur bei >1 neben dem Titel: «Weitere Adressen (2)» (Walter 02.08.2026).
    const countEl = document.getElementById('ovAddrCardCount');
    if (countEl) countEl.textContent = n > 1 ? ` (${n})` : '';
}

function renderEmployeeAddressesList(el, list) {
    if (!Array.isArray(list) || list.length === 0) {
        // Kein Hinweis-Text (Walter 17.07.2026): keine Adressen = leer.
        el.innerHTML = '';
        if (el.closest?.('.ov-addr-full') || el.id === 'otherAddressesContent')
            _ovUpdateAddrCardCount(0);
        return;
    }
    const fmtDate = d => d ? new Date(d).toLocaleDateString('de-CH') : '';
    const rows = list.map(a => {
        const id = a.id ?? a.Id;
        const lines = [];
        if (a.description) lines.push(a.description);
        if (a.street)      lines.push(a.street + (a.street2 ? ' / ' + a.street2 : ''));
        if (a.poBox)       lines.push('Postfach ' + a.poBox);
        // Ortschaft ohne Kanton in Klammern (Walter 02.08.2026) — Kt. ist eigenes Feld.
        const ort = [a.zipCode, stripCityCantonSuffix(a.city)].filter(Boolean).join(' ');
        if (ort) lines.push(ort);
        // Land nur anzeigen wenn es NICHT die Schweiz ist (Standard = "CH").
        // Beide Schreibweisen abfangen, falls noch Altdaten "Schweiz" enthalten.
        if (a.country && a.country !== 'CH' && a.country !== 'Schweiz') lines.push(a.country);
        const contactLine = [a.phone, a.email].filter(Boolean).join(' · ');
        if (contactLine) lines.push(contactLine);

        // Kompakte EIN-Zeilen-Darstellung (Walter 17.07.2026 / 26.07.2026).
        return `<div class="emp-addr-row" data-addr-id="${id}">
            <span class="emp-addr-type">${a.addressType || a.AddressType || 'Adresse'}</span>
            <span class="emp-addr-text">
                ${lines.length ? lines.join(' · ') : '<span class="emp-addr-empty">Keine Detail-Angaben</span>'}
            </span>
            ${a.validFrom ? `<span class="emp-addr-valid">gültig ab ${fmtDate(a.validFrom)}</span>` : ''}
            <span class="emp-addr-actions">
                <button class="btn-stamp-edit" onclick='openEmployeeAddressModal(${JSON.stringify(a).replace(/'/g,"&#39;")})'>✎</button>
                <button class="btn-stamp-edit" style="color:#b91c1c" onclick="deleteEmployeeAddress(${id})">🗑</button>
            </span>
        </div>`;
    }).join('');
    el.innerHTML = rows;
    _ovUpdateAddrCardCount(list.length);
}

let _empAddrEditing = null;  // null = neu, sonst die zu editierende Adresse

function openEmployeeAddressModal(existing) {
    _empAddrEditing = existing || null;
    const isNew = !existing;
    let modal = document.getElementById('empAddressModal');
    if (!modal) {
        modal = document.createElement('div');
        modal.id = 'empAddressModal';
        modal.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,0.45);z-index:3000;display:none;align-items:flex-start;justify-content:center;overflow-y:auto;padding:24px 16px';
        document.body.appendChild(modal);
    }
    const a = existing || { addressType: 'Korrespondenzadresse', country: 'CH' };
    const optTypes = EMP_ADDRESS_TYPES.map(t =>
        `<option value="${t}" ${t === a.addressType ? 'selected' : ''}>${t}</option>`
    ).join('');

    modal.innerHTML = `
    <div style="background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);border-radius:12px;width:100%;max-width:760px;box-shadow:0 20px 60px rgba(0,0,0,0.25);margin:auto">
        <div style="padding:14px 22px;border-bottom:1px solid #e2e8f0;display:flex;align-items:center;justify-content:space-between">
            <div style="font-size:15px;font-weight:700;color:#1e293b">${isNew ? _t('addr.modalTitleNew','Adresse hinzufügen') : _t('addr.modalTitleEdit','Adresse bearbeiten')}</div>
            <button onclick="closeEmployeeAddressModal()" style="background:none;border:none;cursor:pointer;font-size:18px;color:#94a3b8">✕</button>
        </div>
        <div style="padding:18px 22px">
            <div class="emp-section-title">${_t('addr.field.description','Beschreibung')}</div>
            <div class="emp-field-grid">
                ${eField(_t('addr.field.description','Adresstyp') + ' *', `<select id="empAddr-type" class="ef-input">${optTypes}</select>`)}
                ${eField(_t('addr.field.validFrom','Gültig ab'), `<input id="empAddr-validFrom" class="ef-input" type="date" value="${a.validFrom ? String(a.validFrom).slice(0,10) : ''}">`)}
                ${eField(_t('addr.field.description','Bezeichnung'), `<input id="empAddr-description" class="ef-input" value="${esc(a.description)}" placeholder="${_t('addr.field.descriptionHint','z.B. Sozialdienst Sursee, Eltern, …')}">`)}
            </div>

            <div class="emp-section-title">${_t('ma.section.address','Adresse')}</div>
            <div class="emp-field-grid">
                ${eField(_t('addr.field.street','Strasse'),  `<input id="empAddr-street"  class="ef-input" value="${esc(a.street)}">`)}
                ${eField(_t('addr.field.street2','Strasse 2'), `<input id="empAddr-street2" class="ef-input" value="${esc(a.street2)}">`)}
                ${eField(_t('addr.field.poBox','Postfach'),  `<input id="empAddr-poBox"   class="ef-input" value="${esc(a.poBox)}">`)}
                ${eField(_t('addr.field.zipCode','PLZ'),     `<input id="empAddr-zip" class="ef-input" value="${esc(a.zipCode)}" inputmode="numeric" maxlength="4" oninput="validateZip(this)" onblur="plzLookupGeneric(this.value,'empAddr-city','empAddr-canton','empAddr-bfs','empAddr-plz-hint')" onkeyup="if(this.value.length===4)plzLookupGeneric(this.value,'empAddr-city','empAddr-canton','empAddr-bfs','empAddr-plz-hint')">`)}
                ${eField(_t('addr.field.city','Ort'),        `<input id="empAddr-city" class="ef-input" value="${esc(stripCityCantonSuffix(a.city))}" oninput="validateCity(this)">`)}
                ${eField('BFS-Nr.',                          `<input id="empAddr-bfs"  class="ef-input" value="${esc(a.bfsNumber)}">`)}
                ${eField(_t('addr.field.canton','Kanton'),   renderKantonSelect('empAddr-canton', a.canton))}
                ${eField(_t('addr.field.country','Land'),    `<input id="empAddr-country" class="ef-input" value="${esc(a.country ?? 'CH')}">`)}
            </div>
            <div id="empAddr-plz-hint" style="font-size:12px;margin-top:-6px;margin-bottom:10px"></div>

            <div class="emp-section-title">${_t('ma.section.contact','Kontakt')}</div>
            <div class="emp-field-grid">
                ${eField(_t('ma.field.phone','Telefon'),     `<input id="empAddr-phone"  class="ef-input" type="tel" value="${esc(a.phone)}" placeholder="+41 79 409 43 33" oninput="validatePhone(this)" onblur="validatePhoneBlur(this)">`)}
                ${eField(_t('ma.field.phone','Telefon') + ' 2', `<input id="empAddr-phone2" class="ef-input" type="tel" value="${esc(a.phone2)}" oninput="validatePhone(this)" onblur="validatePhoneBlur(this)">`)}
                ${eField(_t('ma.field.email','E-Mail'),      `<input id="empAddr-email"  class="ef-input" type="email" value="${esc(a.email)}" oninput="validateEmail(this)" onblur="validateEmail(this, true)">`)}
                ${eField('IncaMail', `<label style="display:flex;align-items:center;gap:8px;padding:8px 0;font-size:13px;color:#475569;cursor:pointer">
                    <input type="checkbox" id="empAddr-incamailDisabled" ${a.incamailDisabled ? 'checked' : ''}>
                    Kein IncaMail
                </label>`)}
            </div>

            <div id="empAddr-error" style="color:#dc2626;font-size:12px;margin:8px 0"></div>
            <div style="display:flex;justify-content:flex-end;gap:10px;border-top:1px solid #f1f5f9;padding-top:14px;margin-top:6px">
                <button class="btn btn-outline" onclick="closeEmployeeAddressModal()">${_t('addr.btn.cancel','Abbrechen')}</button>
                ${isNew ? '' : `<button class="btn btn-outline" style="border-color:#fca5a5;color:#b91c1c" onclick="deleteEmployeeAddress(${a.id})">🗑 ${_t('addr.btn.delete','Löschen')}</button>`}
                <button class="btn btn-primary" onclick="saveEmployeeAddress()">${_t('addr.btn.save','Speichern')}</button>
            </div>
        </div>
    </div>`;
    modal.style.display = 'flex';
}

function closeEmployeeAddressModal() {
    // Modal komplett aus dem DOM entfernen — sonst bleiben die Formular-
    // Felder der gelöschten Adresse unsichtbar im Hintergrund hängen und
    // können bei einem erneuten Öffnen/Z-Index-Glitch wieder auftauchen.
    const m = document.getElementById('empAddressModal');
    if (m) m.remove();
    _empAddrEditing = null;
}

async function saveEmployeeAddress() {
    if (!selectedEmployeeId) return;
    const errEl = document.getElementById('empAddr-error');
    errEl.textContent = '';

    const val = id => (document.getElementById(id)?.value || '').trim();

    // Walter-Vorgabe 01.06.2026: harte Validierung vor dem Speichern.
    const zip = val('empAddr-zip');
    if (zip && !/^\d{4}$/.test(zip)) {
        errEl.textContent = 'PLZ muss 4-stellig numerisch sein.';
        return;
    }
    const city = val('empAddr-city');
    if (city && !/^[A-Za-zÀ-ÿ\s\-'\.]+$/.test(city)) {
        errEl.textContent = 'Ort darf nur Buchstaben enthalten.';
        return;
    }
    // Telefon: auf "+XX XX XXX XX XX"-Format normalisieren (idempotent)
    // und prüfen dass es ungefähr in dieses Schema passt.
    const phoneOk = v => !v || /^\+\d{2}\s\d{2}\s\d{3}\s\d{2}\s\d{2}$/.test(v);
    const phone1Raw = val('empAddr-phone');
    const phone2Raw = val('empAddr-phone2');
    const phone1 = phone1Raw ? window.formatPhoneIntl(phone1Raw) : '';
    const phone2 = phone2Raw ? window.formatPhoneIntl(phone2Raw) : '';
    if (phone1Raw && !phoneOk(phone1)) {
        errEl.textContent = 'Telefon-Format ungültig (erwartet +99 99 999 99 99).';
        return;
    }
    if (phone2Raw && !phoneOk(phone2)) {
        errEl.textContent = 'Telefon 2-Format ungültig (erwartet +99 99 999 99 99).';
        return;
    }
    // formatierte Werte in die Felder zurückspielen damit sie sichtbar gespeichert werden
    if (phone1) document.getElementById('empAddr-phone').value = phone1;
    if (phone2) document.getElementById('empAddr-phone2').value = phone2;
    const email = val('empAddr-email');
    if (email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
        errEl.textContent = 'E-Mail-Adresse ist ungültig.';
        return;
    }

    const payload = {
        addressType:      val('empAddr-type') || 'Korrespondenzadresse',
        validFrom:        val('empAddr-validFrom') || null,
        description:      val('empAddr-description') || null,
        street:           val('empAddr-street') || null,
        street2:          val('empAddr-street2') || null,
        poBox:            val('empAddr-poBox') || null,
        bfsNumber:        val('empAddr-bfs') || null,
        zipCode:          val('empAddr-zip') || null,
        city:             stripCityCantonSuffix(val('empAddr-city')) || null,
        canton:           val('empAddr-canton') || null,
        country:          val('empAddr-country') || 'CH',
        phone:            val('empAddr-phone') || null,
        phone2:           val('empAddr-phone2') || null,
        email:            val('empAddr-email') || null,
        incamailDisabled: document.getElementById('empAddr-incamailDisabled')?.checked || false
    };

    try {
        const isNew = !_empAddrEditing;
        const url = isNew
            ? `/api/employees/${selectedEmployeeId}/addresses`
            : `/api/employees/${selectedEmployeeId}/addresses/${_empAddrEditing.id}`;
        const res = await fetch(url, {
            method: isNew ? 'POST' : 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (!res.ok) {
            let msg = 'Fehler beim Speichern (' + res.status + ').';
            try {
                const j = await res.json();
                msg = j.message || j.error || msg;
                if (j.detail) msg += ' — ' + j.detail;
            } catch {}
            errEl.textContent = msg;
            return;
        }
        const saved = await res.json().catch(() => null);
        closeEmployeeAddressModal();
        loadEmployeeAddressesTab(selectedEmployeeId);

        // Hook: wenn das Familie-Modal die "+ Neue Zusatzadresse" ausgelöst
        // hat, refreshen wir dort das Dropdown und wählen die neue Adresse aus.
        if (window._fmReloadAddressesAfterSave) {
            window._fmReloadAddressesAfterSave = false;
            // "Andere Adresse"-Modus aktivieren und neue ID vorbelegen
            const altRadio = document.getElementById('fmAddrAlt');
            if (altRadio) altRadio.checked = true;
            await fmRefreshAddressUi(saved?.id ?? null);
        }
    } catch(e) {
        errEl.textContent = 'Verbindungsfehler: ' + e.message;
    }
}

// ════════════════════════════════════════════════════════════════════
// POSTFACH-ZUGANG (Login-Account des Mitarbeiters)
// Ein Block im Personal-Tab zeigt den Account-Status. Backoffice kann nur
// das Passwort zurücksetzen — niemand sieht das vom MA gewechselte Passwort
// (bcrypt-Hash). Initial-Passwort wird einmalig in einem Modal gezeigt.
// ════════════════════════════════════════════════════════════════════

async function loadPostfachAccountBlock(employeeId) {
    const el = document.getElementById('postfachAccountBlock');
    if (!el || !employeeId) return;
    try {
        const res = await fetch(`/api/employees/${employeeId}/postfach-account`, { headers: ah() });
        if (!res.ok) {
            el.innerHTML = '<div style="padding:12px;color:#dc2626;font-size:12.5px">Status nicht abrufbar.</div>';
            return;
        }
        const s = await res.json();
        renderPostfachAccountBlock(el, s);
    } catch (e) {
        el.innerHTML = `<div style="padding:12px;color:#dc2626;font-size:12.5px">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

function renderPostfachAccountBlock(el, s) {
    if (!s) { el.innerHTML = ''; return; }

    const fmtDt = d => d ? new Date(d).toLocaleString('de-CH', { dateStyle: 'short', timeStyle: 'short' }) : '–';

    // Status-Badge
    let statusBadge;
    if (!s.exists) {
        statusBadge = `<span style="font-size:11px;font-weight:600;padding:3px 10px;border-radius:10px;background:#fef3c7;color:#92400e">Kein Account</span>`;
    } else if (!s.employeeIsActive) {
        statusBadge = `<span style="font-size:11px;font-weight:600;padding:3px 10px;border-radius:10px;background:#fee2e2;color:#991b1b">Gesperrt — MA inaktiv</span>`;
    } else if (s.locked) {
        statusBadge = `<span style="font-size:11px;font-weight:600;padding:3px 10px;border-radius:10px;background:#fef3c7;color:#92400e">Gesperrt bis ${fmtDt(s.lockedUntil)}</span>`;
    } else if (!s.isActive) {
        statusBadge = `<span style="font-size:11px;font-weight:600;padding:3px 10px;border-radius:10px;background:#fee2e2;color:#991b1b">Account inaktiv</span>`;
    } else {
        statusBadge = `<span style="font-size:11px;font-weight:600;padding:3px 10px;border-radius:10px;background:#dcfce7;color:#166534">● Aktiv</span>`;
    }

    const lastLogin = s.lastLoginAt
        ? `Letzter Login: <b>${fmtDt(s.lastLoginAt)}</b>`
        : `<span style="color:#94a3b8">Noch nie eingeloggt</span>`;

    // Buttons (nur sinnvoll wenn Account existiert und MA aktiv ist)
    let buttons = '';
    if (s.exists && s.employeeIsActive) {
        buttons += `<button class="btn-emp-add" style="background:#3f3f3f;color:#fff" onclick="postfachResetPassword(${s.employeeId})">
                        Passwort zurücksetzen
                    </button>`;
        if (s.locked) {
            buttons += `<button class="btn-emp-add" style="background:#fff;color:#475569;border:1px solid #cbd5e1;margin-left:6px" onclick="postfachUnlock(${s.employeeId})">
                            Sperre aufheben
                        </button>`;
        }
        buttons += `<button class="btn-emp-add" style="background:#fff;color:#475569;border:1px solid #cbd5e1;margin-left:6px" onclick="faceIdAdminReset(${s.employeeId})">
                        Face ID zurücksetzen
                    </button>`;
        buttons += `<button class="btn-emp-add" style="background:#0f766e;color:#fff;margin-left:6px" onclick="postfachSetupQr(${s.employeeId})">
                        Onboarding-QR
                    </button>`;
    } else if (!s.exists && s.employeeIsActive) {
        // Sollte normalerweise nicht passieren (Auto-Erstellung beim Anlegen)
        // — Backfill-Button als Notlösung.
        buttons += `<button class="btn-emp-add" style="background:#3f3f3f;color:#fff" onclick="postfachResetPassword(${s.employeeId})">
                        Account jetzt erstellen
                    </button>`;
    }

    el.innerHTML = `
    <div style="border:1px solid #e2e8f0;border-radius:10px;padding:14px 16px;background:#f8fafc">
        <div style="display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap">
            <div style="display:flex;flex-direction:column;gap:4px;font-size:13px;color:#0f172a">
                <div style="display:flex;align-items:center;gap:8px">
                    <span style="font-size:16px">📬</span>
                    <span style="font-weight:600">Persönliches Postfach</span>
                    ${statusBadge}
                </div>
                <div style="font-size:12px;color:#64748b">
                    Benutzername: <b style="color:#0f172a">${s.employeeNumber || '–'}</b> &nbsp;·&nbsp; ${lastLogin}
                    ${s.failedLoginCount > 0 ? ` &nbsp;·&nbsp; <span style="color:#b45309">${s.failedLoginCount} Fehlversuche</span>` : ''}
                </div>
            </div>
            <div style="display:flex;gap:6px">${buttons}</div>
        </div>
        <div style="font-size:11.5px;color:#94a3b8;margin-top:8px;line-height:1.4">
            Das aktuelle Passwort des MA ist nicht einsehbar — nur ein Reset auf das Initial-Passwort ist möglich. Beim ersten Login wird der MA aufgefordert ein neues Passwort zu setzen.
        </div>
    </div>`;
}

// ══════════════════════════════════════════════════════════════════════
// MA LÖSCHEN (Walter-Vorgabe 12.06.2026)
// ──────────────────────────────────────────────────────────────────────
// Nur für admin sichtbar (Button im Header). Zwei Pfade:
//   • Lohn-Daten vorhanden → SOFT-DELETE (IsHidden=true, MA bleibt in DB
//     für Audit, ist aber überall ausgeblendet)
//   • Keine Lohn-Daten     → HARD-DELETE (alle Daten weg: Verträge,
//     Bewilligungen, Doku, Bank, Familie, Absenzen, Stempelzeiten, etc.)
//
// Modal zeigt die Counts der zu löschenden Daten und verlangt das Tippen
// des Nachnamens (Anti-Versehen-Schutz analog GitHub „delete repo").
// ══════════════════════════════════════════════════════════════════════

async function openDeleteEmployeeModal(employeeId) {
    if (!employeeId) return;
    if (currentUser?.role !== 'admin') {
        alert('Nur Admin darf Mitarbeiter löschen.');
        return;
    }
    let preview;
    try {
        const res = await fetch(`/api/employees/${employeeId}/delete-preview`, { headers: ah() });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || 'Fehler beim Laden der Vorschau.');
            return;
        }
        preview = await res.json();
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        return;
    }

    const c       = preview.counts || {};
    const isSoft  = preview.mode === 'soft';
    const lohnRow = preview.hasLohnData
        ? `<div style="background:#fef3c7;border:1px solid #fbbf24;border-left:4px solid #f59e0b;border-radius:6px;padding:10px 14px;margin-top:10px;color:#92400e;font-size:12.5px;line-height:1.5">
             <strong>Lohn-Daten gefunden:</strong> ${c.payrollSnapshots} Lohnabrechnungen, ${c.payrollSaldi} Saldi, ${c.akontoZahlungen} Akonto-Zahlungen.<br>
             Diese bleiben für Audit + Jahresauswertungen erhalten. Der MA wird in allen Listen ausgeblendet.
           </div>`
        : '';

    const dataList = [
        { n: c.employments,      label: 'Verträge' },
        { n: c.permits,          label: 'Bewilligungen' },
        { n: c.documents,        label: 'Dokumente' },
        { n: c.bankAccounts,     label: 'Bankverbindungen' },
        { n: c.familyMembers,    label: 'Familienangaben' },
        { n: c.absences,         label: 'Absenzen' },
        { n: c.timeEntries,      label: 'Stempelzeiten' }
    ].filter(x => x.n > 0);

    const hardWarn = !isSoft && dataList.length > 0
        ? `<div style="background:#fef2f2;border:1px solid #fca5a5;border-left:4px solid #dc2626;border-radius:6px;padding:10px 14px;margin-top:10px;color:#991b1b;font-size:12.5px;line-height:1.6">
             <strong>UNWIDERRUFLICH:</strong> folgende Daten werden komplett gelöscht:
             <ul style="margin:6px 0 0;padding-left:18px">
               ${dataList.map(x => `<li>${x.n} ${x.label}</li>`).join('')}
             </ul>
           </div>`
        : (!isSoft ? '<div style="background:#fef2f2;border:1px solid #fca5a5;border-left:4px solid #dc2626;border-radius:6px;padding:10px 14px;margin-top:10px;color:#991b1b;font-size:12.5px"><strong>Keine zusätzlichen Daten</strong> — der MA hat nur Stammdaten und wird komplett gelöscht.</div>' : '');

    // Walter-Vorgabe 14.06.2026: Nachnamen-Eintipp-Bestätigung entfernt —
    // Löschen darf ohnehin nur ein Admin (Backend-Check + UI-Check vor dem
    // Modal-Open). Eine einfache Ja/Nein-Bestätigung mit deutlichem roten
    // Button reicht. Der Vorschaublock mit der Liste der zu löschenden
    // Daten (hardWarn / lohnRow) bleibt — DAS ist die echte Sicherheit.

    const modalHtml = `
    <div id="delEmpModal" style="position:fixed;inset:0;background:rgba(0,0,0,0.45);z-index:3000;display:flex;align-items:center;justify-content:center" onclick="if(event.target===this)closeDeleteEmployeeModal()">
        <div style="background:#fff;border-radius:12px;width:540px;max-width:92vw;max-height:90vh;overflow-y:auto;box-shadow:0 16px 56px rgba(0,0,0,0.25)">
            <div style="padding:16px 22px;border-bottom:1px solid #e2e8f0;display:flex;align-items:center;gap:10px">
                <span style="font-size:22px">${isSoft ? '👁' : '🗑'}</span>
                <div style="flex:1">
                    <div style="font-weight:700;color:#0f172a;font-size:15px">${isSoft ? 'Mitarbeiter ausblenden' : 'Mitarbeiter LÖSCHEN'}</div>
                    <div style="color:#64748b;font-size:12px;margin-top:2px">${preview.employeeName} · Personalnr. ${preview.employeeNumber}</div>
                </div>
                <button onclick="closeDeleteEmployeeModal()" style="background:none;border:none;color:#94a3b8;font-size:22px;cursor:pointer;line-height:1">×</button>
            </div>
            <div style="padding:18px 22px">
                <div style="color:#475569;font-size:13px;line-height:1.55">
                    ${isSoft
                        ? `Da bereits Lohnabrechnungen existieren, kann dieser Mitarbeiter nicht physisch gelöscht werden. Er wird unsichtbar gemacht.`
                        : `Dieser Mitarbeiter wird <strong>komplett gelöscht</strong>. Diese Aktion kann nicht rückgängig gemacht werden.`}
                </div>
                ${lohnRow}
                ${hardWarn}
            </div>
            <div style="padding:14px 22px;border-top:1px solid #e2e8f0;display:flex;justify-content:flex-end;gap:8px">
                <button onclick="closeDeleteEmployeeModal()" style="background:#fff;border:1px solid #cbd5e1;color:#475569;padding:8px 16px;border-radius:6px;font-size:13px;cursor:pointer">Abbrechen</button>
                <button id="delEmpConfirmBtn" onclick="confirmDeleteEmployee(${employeeId}, '${preview.mode}')" style="background:#dc2626;border:none;color:#fff;padding:8px 16px;border-radius:6px;font-size:13px;font-weight:600;cursor:pointer">
                    ${isSoft ? 'Ja, ausblenden' : 'Ja, endgültig löschen'}
                </button>
            </div>
        </div>
    </div>`;
    // Vorhandenes Modal entfernen, dann neues einfügen.
    closeDeleteEmployeeModal();
    document.body.insertAdjacentHTML('beforeend', modalHtml);
    // Fokus auf den Abbrechen-Knopf, damit ein versehentliches Enter NICHT
    // löscht (Walter-Wille: bewusster Klick auf den roten Knopf).
    setTimeout(() => document.querySelector('#delEmpModal button')?.focus(), 50);
}

function closeDeleteEmployeeModal() {
    document.getElementById('delEmpModal')?.remove();
}

async function confirmDeleteEmployee(employeeId, expectedMode) {
    const btn = document.getElementById('delEmpConfirmBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Lösche…'; }

    // Walter-Vorgabe 14.06.2026: nach dem Löschen NICHT in den leeren
    // Zustand zurückspringen (= erster MA der Liste), sondern den NÄCHSTEN
    // sichtbaren MA in der aktuellen Liste auswählen. IDs aus den DOM-
    // List-Items extrahieren (selectEmployee(...)-onclick), Position des
    // gelöschten finden, dann nächsten nehmen (Wrap: ist der gelöschte
    // der letzte, → vorheriger).
    const _nextEmpAfterDelete = (() => {
        const items = Array.from(document.querySelectorAll('#empList .emp-list-item'));
        const ids = items.map(el => {
            const m = (el.getAttribute('onclick') || '').match(/selectEmployee\((\d+)\)/);
            return m ? parseInt(m[1], 10) : null;
        }).filter(x => x != null);
        const idx = ids.indexOf(employeeId);
        if (idx < 0) return null;                // nicht in Liste → kein Sprung
        if (ids.length <= 1) return null;        // einziger MA → nichts mehr da
        return ids[idx + 1] ?? ids[idx - 1];     // nächster, sonst voriger
    })();

    try {
        const res = await fetch(`/api/employees/${employeeId}?expectedMode=${encodeURIComponent(expectedMode)}`, {
            method: 'DELETE',
            headers: ah()
        });
        const data = await res.json().catch(() => null);
        if (!res.ok) {
            alert(data?.message || `Fehler beim Löschen (${res.status})`);
            if (btn) { btn.disabled = false; btn.textContent = expectedMode === 'soft' ? 'Ja, ausblenden' : 'Ja, endgültig löschen'; }
            return;
        }
        closeDeleteEmployeeModal();
        alert(data?.message || 'Mitarbeiter gelöscht.');
        // Walter 14.06.2026: MA-Picker-Cache invalidieren (siehe employee-lookup-cache.js).
        if (typeof invalidateEmployeeLookupCache === 'function') invalidateEmployeeLookupCache();

        // Selektion auf den nächsten MA legen, BEVOR die Liste neu geladen
        // wird — applyEmpFilter (in loadMitarbeiterList) liest
        // window.activeEmpId mit höchster Priorität und selektiert den.
        selectedEmployeeId = null;
        window.selectedEmployeeId = null;
        selectedEmployee   = null;
        window.activeEmpId = _nextEmpAfterDelete; // null → fällt auf allEmployees[0]
        if (typeof loadMitarbeiterList === 'function') await loadMitarbeiterList();
        if (!_nextEmpAfterDelete) {
            const detail = document.getElementById('empDetail');
            if (detail) detail.innerHTML = '<div class="emp-placeholder"><span>Bitte einen Mitarbeiter auswählen.</span></div>';
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        if (btn) { btn.disabled = false; btn.textContent = expectedMode === 'soft' ? 'Ja, ausblenden' : 'Ja, endgültig löschen'; }
    }
}

async function postfachResetPassword(employeeId) {
    if (!employeeId) return;
    if (!(await liquidConfirm('Passwort zurücksetzen?\n\nDer MA muss beim nächsten Login ein neues Passwort setzen. Das aktuelle Passwort wird verworfen.'))) return;
    try {
        const res = await fetch(`/api/employees/${employeeId}/postfach-account/reset-password`, {
            method: 'POST',
            headers: ah(),
        });
        if (!res.ok) {
            const j = await res.json().catch(() => null);
            alert(j?.message || 'Fehler beim Zurücksetzen.');
            return;
        }
        const data = await res.json();
        showInitialPasswordModal(data.username, data.initialPassword);
        await loadPostfachAccountBlock(employeeId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// HR: Vertrags-Link direkt per SMS an den MA senden (Walter 07.07.2026, Etappe 2).
// Nur eine Rückfrage «Vertrag per SMS an Nr. X wirklich senden?» — dann sendet
// das Backend über eCall (Test-Umleitung greift dort automatisch).
async function contractShareSendSms(employeeId, employmentId, phone) {
    if (!employeeId && !employmentId) return;
    const nr = (phone || '').trim();
    if (!nr) {
        alert('Für diesen Mitarbeitenden ist keine Handynummer hinterlegt.\n\nBitte zuerst im Personal-Tab die Telefonnummer erfassen.');
        return;
    }

    // «bereits gesendet»-Hinweis (Punkt 5): letzter Versand + Link-Status in
    // die Rückfrage aufnehmen. Best-effort — ohne Status normal fragen.
    let hint = '';
    if (employmentId) {
        try {
            const sr = await fetch(`/api/contract-share/status?employmentId=${employmentId}`, { headers: ah() });
            if (sr.ok) {
                const s = await sr.json();
                if (s.lastSmsSentAt) {
                    const d = new Date(s.lastSmsSentAt);
                    hint += `\n\nBereits gesendet am ${d.toLocaleDateString('de-CH')} ${d.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' })}.`;
                }
                if (s.activeLink) {
                    hint += s.activeLink.openedAt
                        ? `\nDer aktuelle Link wurde vom MA geöffnet${s.activeLink.usedAt ? ' (PDF abgerufen)' : ''}.`
                        : '\nDer aktuelle Link wurde noch nicht geöffnet.';
                }
                if (hint) hint += '\nBeim erneuten Senden werden alte Links ungültig.';
            }
        } catch (_) { /* Status ist nur Komfort */ }
    }
    if (!(await liquidConfirm(`Vertrag per SMS an ${nr} wirklich senden?${hint}`))) return;

    // Feedback-Box im AKTIVEN Tab suchen (Uebersicht ODER Personal-Strip —
    // Klasse statt ID, damit beide Instanzen erlaubt sind, Walter 17.07.2026).
    const box = document.querySelector('.emp-tab-content.active .contractShareBox')
        || document.querySelector('.contractShareBox');
    if (box) box.innerHTML = '<div style="color:#8b8b8b;font-size:13px;padding:8px 0">📲 SMS wird gesendet …</div>';
    try {
        const res = await fetch('/api/contract-share/send', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(employmentId ? { employmentId } : { employeeId }),
        });
        const j = await res.json().catch(() => ({}));
        if (!res.ok || !j.ok) {
            if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">✗ ${esc(j.error || j.message || ('Fehler HTTP ' + res.status))}</div>`;
            return;
        }
        const exp = j.expiresAt ? new Date(j.expiresAt) : null;
        const expStr = exp && !isNaN(exp.getTime()) ? exp.toLocaleDateString('de-CH') : '';
        const redirectNote = j.redirectedTo
            ? `<div style="margin-top:6px;color:#6b5a1f">⚠ Test-Umleitung aktiv — die SMS ging an ${esc(j.redirectedTo)}.</div>`
            : '';
        if (box) box.innerHTML = `
            <div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:12px 14px;font-size:13px;line-height:1.55">
                ✓ Vertrags-SMS gesendet an ${esc(j.to || nr)}.${expStr ? ` Link gültig bis ${esc(expStr)}.` : ''}
                ${redirectNote}
            </div>`;
        box?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    } catch (e) {
        if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

// Vertrag direkt aus dem MA-Detail bearbeiten (Walter-Vorgabe 08.07.2026):
// öffnet dasselbe Vertrags-Modal wie das Verträge-Modul (contracts-edit.js,
// inkl. Mindestlohn-Live-Prüfung + easy@work-Override-Checkbox). Der volle
// Vertrag wird frisch vom Server geholt — die Strip-Daten sind nur eine
// Projektion. Nach dem Speichern lädt _ceAfterSave das MA-Detail neu.
async function empContractEdit(employmentId, employeeId) {
    if (!employmentId) return;
    try {
        const r = await fetch(`/api/employments/${employmentId}`, { headers: ah() });
        if (!r.ok) { alert('Vertrag konnte nicht geladen werden (HTTP ' + r.status + ').'); return; }
        const c = await r.json();
        window._ceAfterSave = async () => {
            if (typeof selectEmployee === 'function' && employeeId) await selectEmployee(employeeId);
        };
        await openContractEditModal(c, 'edit');
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// ═══════════ „＋ Neuer MA aus easy@work" (Walter-Vorgabe 08.07.2026) ═══════════
// Einzelimport für die Mitarbeiter-Verwaltung: neuer MA wird IMMER zuerst in
// easy@work erfasst (CSV ist Vergangenheit), dann hier reingeholt. Zeigt NUR
// neue MA (NEW) + Änderungen bei AKTIVEN MA (UPDATE) — inaktive werden vom
// Backend nie angefasst (OnlyActive fest verdrahtet). Für admin/superuser/
// user(GF)/buchhaltung; GF nur für seine Filialen (Server prüft).
// Einstieg mit Wegwahl (Walter-Vorgabe 12.07.2026): normalerweise via API —
// ABER wenn der MA-Datensatz in easy@work in einem FREMDEN Restaurant gesperrt
// ist (Franchise-Wechsler: gehört einem anderen Betreiber), sieht unsere API
// ihn nicht. Dann bleibt der alte CSV-Import (easy@work-Export-Liste) der Weg.
//
// Personalnummern-Folge NEW (Walter 03.08.2026): ausgewählte NEU-Nummern müssen
// exakt max+1…max+N sein (anschliessend an höchste Nr. der Filiale + untereinander).
// Sonst gesamter Import gesperrt. Nur dieser Neuzugang-Pfad — nicht Admin-Sync.
let _empEasyNumberSeq = null; // { maxExisting, prefix } aus Preview

function empImportFromEasy() {
    const cpId0 = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
    if (!cpId0) { alert('Bitte zuerst oben in der Sidebar eine Filiale wählen.'); return; }

    let ov = document.getElementById('empEasyImportModal');
    if (!ov) {
        ov = document.createElement('div');
        ov.id = 'empEasyImportModal';
        ov.style.cssText = 'position:fixed;inset:0;z-index:400;background:rgba(60,55,48,0.4);display:flex;align-items:flex-start;justify-content:center;padding:40px 20px';
        ov.onclick = e => { if (e.target === ov) ov.style.display = 'none'; };
        document.body.appendChild(ov);
    }
    ov.style.display = 'flex';
    ov.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:18px;max-width:520px;width:100%;box-shadow:0 24px 60px rgba(60,55,48,0.22)">
            <div style="display:flex;justify-content:space-between;align-items:center;padding:18px 22px 6px">
                <div style="font-size:17px;font-weight:700;color:#3f3f3f">＋ Neuer MA aus easy@work</div>
                <button onclick="document.getElementById('empEasyImportModal').style.display='none'" aria-label="Schliessen"
                        style="background:rgba(255,255,255,0.6);border:1px solid rgba(0,0,0,0.06);border-radius:10px;width:34px;height:34px;font-size:19px;cursor:pointer;color:#646464;flex-shrink:0">&times;</button>
            </div>
            <div style="padding:6px 22px 20px;display:flex;flex-direction:column;gap:10px">
                <button onclick="empImportFromEasyApi()"
                        style="text-align:left;background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:13px 16px;cursor:pointer">
                    <div style="font-size:14px;font-weight:700">Via easy@work-API <span style="font-weight:400;opacity:0.75">(Normalfall)</span></div>
                    <div style="font-size:12px;opacity:0.75;margin-top:2px">Holt neue MA + Änderungen aktiver MA direkt aus easy@work.</div>
                </button>
                <button onclick="document.getElementById('empEasyImportModal').style.display='none'; openImportTool('csv');"
                        style="text-align:left;background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:13px 16px;cursor:pointer">
                    <div style="font-size:14px;font-weight:700">Via CSV-Datei <span style="font-weight:400;color:#8b8b8b">(alter Importer)</span></div>
                    <div style="font-size:12px;color:#8b8b8b;margin-top:2px">Für MA, deren easy@work-Datensatz in einem fremden Restaurant gesperrt ist — die API sieht sie nicht. Export-Liste aus easy@work als CSV hochladen.</div>
                </button>
            </div>
        </div>`;
}

async function empImportFromEasyApi() {
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
    if (!cpId) { alert('Bitte zuerst oben in der Sidebar eine Filiale wählen.'); return; }

    let ov = document.getElementById('empEasyImportModal');
    if (!ov) {
        ov = document.createElement('div');
        ov.id = 'empEasyImportModal';
        ov.style.cssText = 'position:fixed;inset:0;z-index:400;background:rgba(60,55,48,0.4);display:flex;align-items:flex-start;justify-content:center;padding:40px 20px';
        ov.onclick = e => { if (e.target === ov) ov.style.display = 'none'; };
        document.body.appendChild(ov);
    }
    ov.style.display = 'flex';
    ov.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:18px;max-width:720px;width:100%;max-height:calc(100vh - 80px);display:flex;flex-direction:column;box-shadow:0 24px 60px rgba(60,55,48,0.22)">
            <div style="display:flex;justify-content:space-between;align-items:center;padding:18px 22px 10px">
                <div>
                    <div style="font-size:17px;font-weight:700;color:#3f3f3f">＋ Neuer MA aus easy@work</div>
                    <div style="font-size:12.5px;color:#8b8b8b;margin-top:2px">Neue MA zuerst in easy@work erfassen — hier erscheinen sie als NEU. Aktive MA mit Änderungen als UPDATE. Inaktive werden nicht angefasst.</div>
                </div>
                <button onclick="document.getElementById('empEasyImportModal').style.display='none'" aria-label="Schliessen"
                        style="background:rgba(255,255,255,0.6);border:1px solid rgba(0,0,0,0.06);border-radius:10px;width:34px;height:34px;font-size:19px;cursor:pointer;color:#646464;flex-shrink:0">&times;</button>
            </div>
            <div id="empEasyImportBody" style="padding:6px 22px 12px;overflow-y:auto;flex:1">
                <div style="color:#8b8b8b;font-size:13px;padding:14px 0">⏳ Hole Daten aus easy@work — das kann einen Moment dauern …</div>
            </div>
            <div id="empEasyImportFoot" style="display:flex;gap:10px;justify-content:flex-end;align-items:center;padding:12px 22px 18px;border-top:1px solid rgba(139,139,139,0.2)"></div>
        </div>`;

    const body = document.getElementById('empEasyImportBody');
    const foot = document.getElementById('empEasyImportFoot');
    try {
        const r = await fetch('/api/easywork/neuzugang/preview', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyProfileId: cpId }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) {
            body.innerHTML = `<div style="background:#f3e7e7;border:1px solid #d8b8b8;color:#7a3f3f;border-radius:10px;padding:12px;font-size:13px">✗ ${esc(j.error || j.message || ('Fehler HTTP ' + r.status))}</div>`;
            return;
        }
        const rows = j.rows || [];
        _empEasyNumberSeq = j.numberSequence || null;
        if (!rows.length) {
            body.innerHTML = `<div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:12px;font-size:13px">✓ Alles aktuell — keine neuen MA und keine Änderungen bei aktiven MA.</div>`
                + _empEasyNotes(j);
            return;
        }
        body.innerHTML = `
            ${_empEasySeqBanner(j.numberSequence)}
            <div style="font-size:12.5px;color:#646464;margin-bottom:8px">${j.countNew} neu · ${j.countUpdate} mit Änderungen — abwählen, was (noch) nicht übernommen werden soll:</div>
            ${rows.map((x, i) => {
                const isNew = x.status === 'NEW';
                const badge = isNew
                    ? '<span style="font-size:10.5px;font-weight:700;background:#e7f0e7;color:#3f5540;border:1px solid #b8ccb8;border-radius:10px;padding:2px 8px">NEU</span>'
                    : '<span style="font-size:10.5px;font-weight:700;background:#ece9e2;color:#6b6152;border:1px solid #d0c8b8;border-radius:10px;padding:2px 8px">UPDATE</span>';
                const detail = isNew
                    ? esc(x.employmentInfo || 'wird neu angelegt')
                    : esc((x.changedFields || []).join(', ') || x.reason || 'Änderungen');
                const reentry = x.possibleReentry
                    ? `<div style="font-size:11.5px;color:#92400e;margin-top:2px">⚠ Möglicher Wiedereintritt (bestehende Nr. ${esc(x.reentryEmployeeNumber || '?')})</div>` : '';
                return `
                <label style="display:flex;gap:10px;align-items:flex-start;padding:9px 10px;border:1px solid rgba(139,139,139,0.22);border-radius:10px;margin-bottom:6px;background:rgba(255,255,255,0.55);cursor:pointer">
                    <input type="checkbox" class="empEasyRow" data-number="${esc(x.number || '')}" data-status="${esc(x.status || '')}" checked style="margin-top:3px;width:15px;height:15px" onchange="_empEasyCount()">
                    <div style="min-width:0">
                        <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap">
                            <span style="font-weight:600;color:#3f3f3f;font-size:13.5px">${esc(((x.firstName||'') + ' ' + (x.lastName||'')).trim())}</span>
                            <span style="color:#8b8b8b;font-size:12px;font-family:monospace">${esc(x.number || '')}</span>
                            ${badge}
                        </div>
                        <div style="font-size:12px;color:#646464;margin-top:2px;word-break:break-word">${detail}</div>
                        ${reentry}
                    </div>
                </label>`;
            }).join('')}
            <div id="empEasySeqWarn" style="display:none;background:#f3e7e7;border:1px solid #d8b8b8;color:#7a3f3f;border-radius:10px;padding:10px 12px;font-size:12.5px;margin-top:8px;white-space:pre-wrap"></div>
            ${_empEasyNotes(j)}`;
        foot.innerHTML = `
            <button onclick="document.getElementById('empEasyImportModal').style.display='none'"
                    style="padding:9px 16px;border:1px solid rgba(139,139,139,0.35);border-radius:12px;background:rgba(255,255,255,0.5);cursor:pointer;font-size:13px;color:#646464">Abbrechen</button>
            <button id="empEasyCommitBtn" onclick="empEasyImportCommit(${cpId})"
                    style="padding:9px 18px;border:none;border-radius:12px;background:#3f3f3f;color:#fff;cursor:pointer;font-size:13.5px;font-weight:600">Ausgewählte importieren (${rows.length})</button>`;
        _empEasyCount();
    } catch (e) {
        body.innerHTML = `<div style="background:#f3e7e7;border:1px solid #d8b8b8;color:#7a3f3f;border-radius:10px;padding:12px;font-size:13px">Netzwerkfehler: ${esc(e.message)}</div>`;
    }
}

function _empEasySeqBanner(seq) {
    if (!seq) return '';
    const max = seq.maxExisting != null ? String(seq.maxExisting) : '—';
    const next = seq.maxExisting != null ? String(Number(seq.maxExisting) + 1) : 'erste Nummer der Filiale';
    return `<div style="background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.28);border-radius:10px;padding:10px 12px;font-size:12.5px;color:#3f3f3f;margin-bottom:10px">
        Letzte Nr. in OneCrew: <b style="font-family:monospace">${esc(max)}</b>
        · Neue NEU-Nummern müssen fortlaufend anschliessen (nächste: <b style="font-family:monospace">${esc(next)}</b>${seq.maxExisting != null ? ', dann +1 …' : ''}).
        Sonst ist der Import gesperrt — bitte in easy@work korrigieren.
    </div>`;
}

/** Client-Spiegel der Server-Regel EmployeeNumberSequenceGuard (nur UX; Server entscheidet hart). */
function _empEasyValidateNewSequence(newNumbers) {
    if (!newNumbers || !newNumbers.length) return { ok: true, message: '' };
    const parsed = [];
    for (const raw of newNumbers) {
        const n = String(raw || '').trim();
        if (!/^\d+$/.test(n)) return { ok: false, message: `Personalnummer «${n}» ist nicht rein numerisch — Import gesperrt.` };
        parsed.push(Number(n));
    }
    if (new Set(parsed).size !== parsed.length)
        return { ok: false, message: 'Doppelte Personalnummern in der Auswahl — Import gesperrt.' };
    parsed.sort((a, b) => a - b);
    const max = _empEasyNumberSeq && _empEasyNumberSeq.maxExisting != null
        ? Number(_empEasyNumberSeq.maxExisting) : null;
    const expected = [];
    if (max != null) {
        for (let i = 1; i <= parsed.length; i++) expected.push(max + i);
    } else {
        for (let i = 0; i < parsed.length; i++) expected.push(parsed[0] + i);
    }
    const same = parsed.length === expected.length && parsed.every((v, i) => v === expected[i]);
    if (same) return { ok: true, message: '' };
    const letzte = max != null ? String(max) : '(keine)';
    const erwartetTxt = expected.join(', ');
    const erhaltenTxt = parsed.join(', ');
    const message = parsed.length === 1
        ? `Personalnummer muss direkt anschliessen. Letzte Nr. in OneCrew: ${letzte}. Erwartet: ${erwartetTxt}. Erhalten: ${erhaltenTxt}.`
        : `Neue Personalnummern müssen fortlaufend an die letzte Nr. und untereinander anschliessen. Letzte Nr. in OneCrew: ${letzte}. Erwartet: ${erwartetTxt}. Erhalten: ${erhaltenTxt}.`;
    return { ok: false, message };
}

function _empEasyNotes(j) {
    let html = '';
    if (j.conflicts && j.conflicts.length)
        html += `<div style="background:#fdf6dd;border:1px solid #e4d28a;color:#6b5a1f;border-radius:10px;padding:10px 12px;font-size:12px;margin-top:8px;white-space:pre-wrap">⚠ Konflikte (werden NICHT importiert — bitte Admin prüfen lassen):\n${j.conflicts.map(escapeHtml).join('\n')}</div>`;
    return html;
}

function _empEasyCount() {
    const checked = Array.from(document.querySelectorAll('#empEasyImportModal .empEasyRow:checked'));
    const n = checked.length;
    const newNums = checked.filter(el => el.dataset.status === 'NEW').map(el => el.dataset.number).filter(Boolean);
    const seq = _empEasyValidateNewSequence(newNums);
    const warn = document.getElementById('empEasySeqWarn');
    if (warn) {
        if (!seq.ok) { warn.style.display = 'block'; warn.textContent = '⛔ ' + seq.message; }
        else { warn.style.display = 'none'; warn.textContent = ''; }
    }
    const btn = document.getElementById('empEasyCommitBtn');
    if (btn) {
        btn.textContent = `Ausgewählte importieren (${n})`;
        const block = n === 0 || !seq.ok;
        btn.disabled = block;
        btn.style.opacity = block ? '0.5' : '1';
    }
}

async function empEasyImportCommit(cpId) {
    const numbers = Array.from(document.querySelectorAll('#empEasyImportModal .empEasyRow:checked'))
        .map(el => el.dataset.number).filter(Boolean);
    if (!numbers.length) return;
    const btn = document.getElementById('empEasyCommitBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Importiere …'; }
    const body = document.getElementById('empEasyImportBody');
    try {
        const r = await fetch('/api/easywork/neuzugang/commit', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyProfileId: cpId, selectedNumbers: numbers }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok || j.blocked) {
            let msg;
            if (j.error === 'NUMBER_SEQUENCE_INVALID') {
                msg = 'Import gesperrt — Personalnummern-Folge:\n' + (j.message || '');
            } else if (j.blocked) {
                msg = 'Import blockiert — Personalnummern-Kollision:\n' + (j.numberConflicts || []).join('\n');
            } else {
                msg = j.message || j.error || ('Fehler HTTP ' + r.status);
            }
            body.innerHTML = `<div style="background:#f3e7e7;border:1px solid #d8b8b8;color:#7a3f3f;border-radius:10px;padding:12px;font-size:13px;white-space:pre-wrap">✗ ${esc(msg)}</div>`;
            if (btn) { btn.disabled = false; btn.textContent = 'Erneut versuchen'; }
            return;
        }
        const skipped = (j.skippedContracts && j.skippedContracts.length)
            ? `<div style="background:#fdf6dd;border:1px solid #e4d28a;color:#6b5a1f;border-radius:10px;padding:10px 12px;font-size:12px;margin-top:8px;white-space:pre-wrap">⚠ Nicht importierte Verträge (Periode abgeschlossen):\n${j.skippedContracts.map(escapeHtml).join('\n')}</div>` : '';
        body.innerHTML = `
            <div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:12px;font-size:13px">
                ✓ Import abgeschlossen — ${j.inserted || 0} neu angelegt, ${j.updated || 0} aktualisiert.
            </div>${skipped}`;
        const foot = document.getElementById('empEasyImportFoot');
        if (foot) foot.innerHTML = `
            <button onclick="document.getElementById('empEasyImportModal').style.display='none'"
                    style="padding:9px 18px;border:none;border-radius:12px;background:#3f3f3f;color:#fff;cursor:pointer;font-size:13.5px;font-weight:600">Schliessen</button>`;
        // MA-Liste auffrischen — der neue MA soll sofort links erscheinen.
        if (typeof invalidateEmployeeLookupCache === 'function') invalidateEmployeeLookupCache();
        if (typeof loadMitarbeiterList === 'function') await loadMitarbeiterList();
    } catch (e) {
        body.innerHTML = `<div style="background:#f3e7e7;border:1px solid #d8b8b8;color:#7a3f3f;border-radius:10px;padding:12px;font-size:13px">Netzwerkfehler: ${esc(e.message)}</div>`;
        if (btn) { btn.disabled = false; btn.textContent = 'Erneut versuchen'; }
    }
}

// HR: alle aktiven Vertrags-Links dieses Vertrags widerrufen (Walter 07.07.2026).
async function contractShareRevoke(employmentId) {
    if (!employmentId) return;
    if (!(await liquidConfirm('Alle aktiven Vertrags-Links dieses Vertrags sofort ungültig machen?\n\nBereits verschickte Links zeigen danach «Link nicht mehr gültig».'))) return;
    // Feedback-Box im AKTIVEN Tab suchen (Uebersicht ODER Personal-Strip —
    // Klasse statt ID, damit beide Instanzen erlaubt sind, Walter 17.07.2026).
    const box = document.querySelector('.emp-tab-content.active .contractShareBox')
        || document.querySelector('.contractShareBox');
    try {
        const res = await fetch('/api/contract-share/revoke', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ employmentId }),
        });
        const j = await res.json().catch(() => ({}));
        if (!res.ok || !j.ok) {
            if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">✗ ${esc(j.error || ('Fehler HTTP ' + res.status))}</div>`;
            return;
        }
        if (box) box.innerHTML = `<div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:12px;font-size:13px">✓ ${j.revoked === 0 ? 'Kein aktiver Link vorhanden.' : `${j.revoked} Link(s) widerrufen — verschickte Links sind ab sofort ungültig.`}</div>`;
    } catch (e) {
        if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

// HR: öffentlichen Vertrags-Link + SMS-Text erzeugen (Copy-Variante).
// Wird vom SMS-Button nicht mehr aufgerufen (Direktversand oben), bleibt
// als Code erhalten für manuelles Kopieren/Verlinken.
async function contractShareCreate(employeeId, employmentId) {
    if (!employeeId && !employmentId) return;
    // Feedback-Box im AKTIVEN Tab suchen (Uebersicht ODER Personal-Strip —
    // Klasse statt ID, damit beide Instanzen erlaubt sind, Walter 17.07.2026).
    const box = document.querySelector('.emp-tab-content.active .contractShareBox')
        || document.querySelector('.contractShareBox');
    if (box) box.innerHTML = '<div style="color:#8b8b8b;font-size:13px;padding:8px 0">⏳ Link wird erzeugt …</div>';
    try {
        const res = await fetch('/api/contract-share', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            // employmentId = genau dieser Vertrag; sonst (Fallback) der aktive.
            body: JSON.stringify(employmentId ? { employmentId } : { employeeId }),
        });
        const j = await res.json().catch(() => ({}));
        if (!res.ok) {
            if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">${esc(j.error || j.message || ('Fehler HTTP ' + res.status))}</div>`;
            return;
        }
        const exp = j.expiresAt ? new Date(j.expiresAt) : null;
        const expStr = exp && !isNaN(exp.getTime())
            ? exp.toLocaleDateString('de-CH')
            : '';
        if (box) box.innerHTML = `
            <div style="background:rgba(255,255,255,0.55);border:1px solid rgba(255,255,255,0.62);box-shadow:0 6px 20px rgba(60,55,48,0.14);border-radius:14px;padding:14px 16px">
                <div style="font-weight:700;color:#3f3f3f;font-size:14px;margin-bottom:8px">📄 Arbeitsvertrag-Link erstellt</div>

                <div style="font-size:12px;color:#8b8b8b;margin-bottom:4px">Öffentlicher Link (ohne Login):</div>
                <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-bottom:12px">
                    <input id="contractShareLinkInput" readonly value="${esc(j.url)}"
                        style="flex:1 1 260px;min-width:0;font-size:12px;padding:8px 10px;border:1px solid rgba(60,55,48,0.18);border-radius:9px;background:#faf8f5;color:#3f3f3f">
                    <button onclick="contractShareCopy('contractShareLinkInput')"
                        style="white-space:nowrap;background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 14px;font-size:13px;font-weight:600;cursor:pointer">Kopieren</button>
                    <a href="${esc(j.url)}" target="_blank" rel="noopener"
                        style="white-space:nowrap;text-decoration:none;background:rgba(255,255,255,0.58);color:#3f3f3f;border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:8px 14px;font-size:13px;font-weight:600">Öffnen</a>
                </div>

                <div style="font-size:12px;color:#8b8b8b;margin-bottom:4px">SMS-Text (zum Kopieren):</div>
                <div style="display:flex;gap:8px;align-items:flex-start;flex-wrap:wrap;margin-bottom:10px">
                    <textarea id="contractShareSmsInput" readonly rows="2"
                        style="flex:1 1 260px;min-width:0;font-size:12px;padding:8px 10px;border:1px solid rgba(60,55,48,0.18);border-radius:9px;background:#faf8f5;color:#3f3f3f;resize:vertical;white-space:pre-wrap">${esc(j.smsText || '')}</textarea>
                    <button onclick="contractShareCopy('contractShareSmsInput')"
                        style="white-space:nowrap;background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 14px;font-size:13px;font-weight:600;cursor:pointer">SMS kopieren</button>
                </div>

                <div style="font-size:12px;color:#8b8b8b;line-height:1.5">
                    ${expStr ? `Gültig bis ${esc(expStr)}. ` : ''}SMS-Direktversand folgt später — Text + Link vorerst manuell senden.
                </div>
            </div>`;
        box?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    } catch (e) {
        if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

function contractShareCopy(inputId) {
    const el = document.getElementById(inputId);
    if (!el) return;
    el.select();
    navigator.clipboard?.writeText(el.value).catch(() => { try { document.execCommand('copy'); } catch (_) {} });
}

// HR: Onboarding-/Reset-QR erzeugen (MA scannt → setzt Passwort → direkt eingeloggt).
async function postfachSetupQr(employeeId) {
    if (!employeeId) return;
    try {
        const res = await fetch(`/api/postfach-setup/create/${employeeId}`, { method: 'POST', headers: ah() });
        const j = await res.json().catch(() => ({}));
        if (!res.ok) { alert(j.error || j.message || 'Fehler beim Erzeugen des QR-Codes.'); return; }
        showSetupQrModal(j, employeeId);
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

function showSetupQrModal(j, employeeId) {
    let ov = document.getElementById('setupQrModal');
    if (!ov) {
        ov = document.createElement('div');
        ov.id = 'setupQrModal';
        ov.style.cssText = 'position:fixed;inset:0;z-index:400;background:rgba(15,23,42,0.5);display:flex;align-items:center;justify-content:center;padding:20px';
        ov.onclick = e => { if (e.target === ov) ov.style.display = 'none'; };
        document.body.appendChild(ov);
    }
    const bis = j.expiresAt ? new Date(j.expiresAt).toLocaleString('de-CH', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '';
    ov.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:20px;max-width:420px;width:100%;padding:24px;box-shadow:0 24px 60px rgba(60,55,48,0.22);text-align:center">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px">
                <h2 style="margin:0;font-size:18px;font-weight:700;color:#3f3f3f">Postfach einrichten</h2>
                <button onclick="document.getElementById('setupQrModal').style.display='none'" aria-label="Schliessen" style="background:rgba(255,255,255,0.6);border:1px solid rgba(0,0,0,0.06);border-radius:10px;width:34px;height:34px;font-size:19px;cursor:pointer;color:#646464">&times;</button>
            </div>
            <p style="font-size:13px;color:#646464;margin:0 0 16px;text-align:left;line-height:1.5">${esc(j.firstName || 'Der Mitarbeitende')} scannt diesen QR-Code mit der Handykamera und setzt direkt sein Passwort — danach ist er automatisch in seinem Postfach. Gültig bis ${esc(bis)}.</p>
            <img src="${esc(j.qrPng)}" alt="QR-Code" style="width:240px;height:240px;image-rendering:pixelated;background:#fff;border:1px solid rgba(255,255,255,0.62);border-radius:14px;padding:8px;box-shadow:0 8px 24px rgba(60,55,48,0.12)">
            <div style="display:flex;gap:8px;align-items:center;margin-top:16px">
                <input id="setupQrLink" readonly value="${esc(j.url)}" style="flex:1;min-width:0;font-size:12px;padding:10px 12px;border:1px solid rgba(0,0,0,0.10);border-radius:12px;background:rgba(255,255,255,0.55);color:#3f3f3f">
                <button style="background:#3f3f3f;color:#faf8f5;border:0;white-space:nowrap;font-weight:600;font-size:13px;padding:10px 16px;border-radius:12px;cursor:pointer;box-shadow:0 6px 16px rgba(60,55,48,0.18)" onclick="(function(){var e=document.getElementById('setupQrLink');e.select();navigator.clipboard&&navigator.clipboard.writeText(e.value);})()">Link kopieren</button>
            </div>
            <!-- App-Link per E-Mail (Walter 18.08.2026) — für bestehende MA aus der Ferne -->
            <div style="margin-top:14px;border-top:1px solid rgba(60,55,48,0.12);padding-top:12px;text-align:left">
                <input id="setupQrDokWunsch" placeholder="Benötigtes Dokument (optional) — z.B. Ausweis Vorder- und Rückseite"
                       style="width:100%;box-sizing:border-box;font-size:12.5px;padding:9px 12px;border:1px solid rgba(0,0,0,0.10);border-radius:12px;background:#fff;color:#3f3f3f;margin-bottom:8px">
                <button id="setupQrMailBtn" onclick="pfSendAppLinkMail(${employeeId})"
                        style="background:transparent;border:1px solid rgba(60,55,48,0.25);border-radius:12px;padding:9px 16px;font-size:13px;font-weight:600;cursor:pointer;color:#3f3f3f;width:100%">📧 Link per E-Mail an den MA senden</button>
                <div id="setupQrMailStatus" style="font-size:12px;color:#6b6152;margin-top:6px"></div>
                <div id="setupQrTokenStatus" style="font-size:12px;color:#6b6152;margin-top:8px;border-top:1px dashed rgba(60,55,48,0.15);padding-top:8px">Lade Status…</div>
            </div>
        </div>`;
    ov.style.display = 'flex';
    pfLoadSetupStatus(employeeId);
}

// Status «gesendet / geöffnet / eingerichtet» des letzten Links (Walter 18.08.2026)
async function pfLoadSetupStatus(employeeId) {
    const el = document.getElementById('setupQrTokenStatus');
    if (!el || !employeeId) return;
    const fmt = d => d ? new Date(d).toLocaleString('de-CH', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : null;
    try {
        const r = await fetch(`/api/postfach-setup/status/${employeeId}`, { headers: ah() });
        const j = r.ok ? await r.json() : null;
        if (!j || !j.hasToken) { el.innerHTML = 'Noch kein Link erzeugt.'; return; }
        const teile = [`Link erzeugt: <b>${fmt(j.createdAt)}</b>`];
        teile.push(j.openedAt ? `✅ geöffnet ${fmt(j.openedAt)}` : '⏳ noch nicht geöffnet');
        teile.push(j.usedAt ? `✅ eingerichtet ${fmt(j.usedAt)}` : '⏳ Passwort noch nicht gesetzt');
        if (j.lastLoginAt) teile.push(`letzter App-Login ${fmt(j.lastLoginAt)}`);
        el.innerHTML = teile.join(' · ')
            + ` &nbsp;<a href="#" onclick="pfLoadSetupStatus(${employeeId});return false" style="color:#6b7280">aktualisieren</a>`;
    } catch { el.textContent = 'Status nicht verfügbar.'; }
}

// App-Link per E-Mail an bestehenden MA (Walter 18.08.2026). Solange der
// TESTMODUS im Backend aktiv ist, wird die Mail an Walter umgeleitet —
// der Status-Text weist das aus.
async function pfSendAppLinkMail(employeeId) {
    const btn = document.getElementById('setupQrMailBtn');
    const st = document.getElementById('setupQrMailStatus');
    if (btn) { btn.disabled = true; btn.textContent = 'Sende E-Mail…'; }
    try {
        const r = await fetch(`/api/postfach-setup/send-app-link/${employeeId}`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ dokumentWunsch: document.getElementById('setupQrDokWunsch')?.value || null }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(j.error || j.message || ('HTTP ' + r.status));
        if (st) st.innerHTML = j.redirected
            ? `✅ Gesendet an <b>${esc(j.sentTo)}</b> <span style="color:#92400e">(TESTMODUS — eigentlicher Empfänger: ${esc(j.empEmail || '–')})</span>`
            : `✅ Gesendet an <b>${esc(j.sentTo)}</b>`;
        if (btn) btn.textContent = '📧 Erneut senden';
    } catch (e) {
        if (st) st.innerHTML = `<span style="color:#b91c1c">Fehler: ${esc(e.message)}</span>`;
        if (btn) btn.textContent = '📧 Link per E-Mail an den MA senden';
    } finally {
        if (btn) btn.disabled = false;
    }
}

// Kleiner App-Status-Chip im MA-Header (Walter 18.08.2026)
async function pfLoadAppChip(employeeId) {
    const el = document.getElementById('empAppChip');
    if (!el) return;
    try {
        const r = await fetch(`/api/postfach-setup/status/${employeeId}`, { headers: ah() });
        if (!r.ok) return;
        const j = await r.json();
        const fmt = d => d ? new Date(d).toLocaleDateString('de-CH') : '';
        if (j.usedAt || j.lastLoginAt) {
            el.style.background = '#dcfce7'; el.style.color = '#166534';
            el.innerHTML = `📱 App eingerichtet${j.lastLoginAt ? ' · Login ' + fmt(j.lastLoginAt) : ''}`;
        } else if (j.hasToken) {
            el.style.background = '#fef3c7'; el.style.color = '#92400e';
            el.innerHTML = j.openedAt
                ? `📱 Link geöffnet ${fmt(j.openedAt)} — noch nicht eingerichtet`
                : `📱 Link gesendet ${fmt(j.createdAt)} — noch nicht geöffnet`;
        } else {
            el.style.background = 'rgba(60,55,48,0.08)'; el.style.color = '#8b8578';
            el.innerHTML = '📱 App nicht eingerichtet';
        }
        el.style.display = '';
    } catch { /* Chip bleibt versteckt */ }
}

// HR: alle Face-ID-/Passkey-Geräte eines MA löschen (z.B. bei Geräteverlust).
async function faceIdAdminReset(employeeId) {
    if (!employeeId) return;
    let count = null;
    try {
        const r = await fetch(`/api/webauthn/admin/by-employee/${employeeId}`, { headers: ah() });
        if (r.ok) count = (await r.json())?.count;
    } catch (e) { /* egal, Löschung trotzdem anbieten */ }
    const msg = (count != null)
        ? `Face ID zurücksetzen?\n\nDer MA hat ${count} aktivierte(s) Gerät(e). Alle werden entfernt — der MA meldet sich dann wieder mit Passwort an und kann Face ID neu aktivieren.`
        : 'Face ID zurücksetzen?\n\nAlle aktivierten Geräte dieses MA werden entfernt.';
    if (!(await liquidConfirm(msg))) return;
    try {
        const res = await fetch(`/api/webauthn/admin/by-employee/${employeeId}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Zurücksetzen.'); return; }
        const d = await res.json().catch(() => ({}));
        alert(`Face ID zurückgesetzt (${d.removed ?? 0} Gerät(e) entfernt).`);
        await loadPostfachAccountBlock(employeeId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function postfachUnlock(employeeId) {
    if (!employeeId) return;
    try {
        const res = await fetch(`/api/employees/${employeeId}/postfach-account/unlock`, {
            method: 'POST',
            headers: ah(),
        });
        if (!res.ok) { alert('Fehler beim Aufheben der Sperre.'); return; }
        await loadPostfachAccountBlock(employeeId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Bulk: Postfach für alle aktiven MA erstellen (einmaliger Backfill nach
// Phase-1-Deploy). Idempotent — bestehende Accounts werden übersprungen.
async function postfachBackfillRun() {
    if (!(await liquidConfirm(
        'Postfach-Backfill ausführen?\n\n' +
        'Für alle aktiven Mitarbeiter ohne Postfach-Account wird einer angelegt.\n' +
        '  • Username = Personalnummer\n' +
        '  • Initial-Passwort = Personalnummer (= Username)\n' +
        '  • Beim ersten Login muss der MA das Passwort zwingend wechseln.\n\n' +
        'Bestehende Accounts bleiben unverändert. Vorgang ist idempotent und ' +
        'kann beliebig oft ausgeführt werden.'
    ))) return;

    try {
        const res = await fetch('/api/admin/postfach-backfill', {
            method: 'POST',
            headers: ah(),
        });
        if (!res.ok) {
            if (res.status === 401 || res.status === 403) {
                alert('Backfill ist nur für Admins möglich.');
                return;
            }
            const j = await res.json().catch(() => null);
            alert(j?.message || 'Fehler beim Backfill.');
            return;
        }
        const data = await res.json();
        const errLines = (data.errors || []).map(e => `• ${esc(e)}`).join('<br>');
        alert(
            `Postfach-Backfill abgeschlossen:\n\n` +
            `MA geprüft:        ${data.scanned}\n` +
            `Accounts erstellt: ${data.created}\n` +
            `Übersprungen:      ${data.skipped}\n` +
            (errLines ? `Fehler: ${data.errors.length}\n` : 'Keine Fehler.')
        );
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Modal das nach Reset einmalig das Initial-Passwort zeigt — mit Copy-Button.
// Nach Schliessen ist das Passwort weg (nicht persistent gespeichert ausser
// als bcrypt-Hash). Walter notiert oder schickt es dem MA.
function showInitialPasswordModal(username, password) {
    let overlay = document.getElementById('initialPwOverlay');
    if (!overlay) {
        overlay = document.createElement('div');
        overlay.id = 'initialPwOverlay';
        overlay.style = 'display:none;position:fixed;inset:0;background:rgba(15,23,42,0.55);z-index:5000;align-items:center;justify-content:center;padding:20px';
        document.body.appendChild(overlay);
    }
    overlay.innerHTML = `
        <div style="background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);border-radius:14px;width:100%;max-width:480px;box-shadow:0 24px 64px rgba(0,0,0,0.28);overflow:hidden">
            <div style="padding:18px 22px;background:linear-gradient(180deg,#ece9e2,#fff);border-bottom:1px solid #e2e8f0;display:flex;align-items:center;gap:10px">
                <span style="font-size:20px">📬</span>
                <div>
                    <div style="font-weight:700;font-size:15px;color:#0f172a">Initial-Passwort gesetzt</div>
                    <div style="font-size:12px;color:#64748b">Einmalig sichtbar — bitte jetzt notieren oder dem MA übergeben</div>
                </div>
            </div>
            <div style="padding:20px 22px;display:flex;flex-direction:column;gap:14px">
                <div>
                    <div style="font-size:11.5px;font-weight:600;color:#475569;margin-bottom:5px">Benutzername</div>
                    <div style="font-family:ui-monospace,Menlo,Consolas,monospace;font-size:14px;color:#0f172a;background:#f1f5f9;padding:8px 12px;border-radius:8px">${esc(username)}</div>
                </div>
                <div>
                    <div style="font-size:11.5px;font-weight:600;color:#475569;margin-bottom:5px">Initial-Passwort</div>
                    <div style="display:flex;gap:6px">
                        <div style="flex:1;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:15px;font-weight:600;color:#0f172a;background:#f1f5f9;padding:8px 12px;border-radius:8px">${esc(password)}</div>
                        <button onclick="navigator.clipboard.writeText('${esc(password)}').then(()=>this.textContent='✓ Kopiert')"
                                style="background:#3f3f3f;color:#fff;border:none;padding:0 14px;border-radius:8px;cursor:pointer;font-size:12.5px;font-weight:600;white-space:nowrap">Kopieren</button>
                    </div>
                </div>
                <div style="background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:10px 12px;font-size:12px;color:#92400e;line-height:1.5">
                    <strong>Wichtig:</strong> Beim ersten Login muss der MA dieses Passwort wechseln. Sobald dieses Fenster geschlossen ist, kann das Passwort nicht mehr eingesehen werden — nur ein erneuter Reset ist möglich.
                </div>
            </div>
            <div style="padding:14px 22px;border-top:1px solid #f1f5f9;display:flex;justify-content:flex-end">
                <button onclick="document.getElementById('initialPwOverlay').style.display='none'"
                        style="background:#0f172a;color:#fff;border:none;padding:9px 22px;border-radius:8px;cursor:pointer;font-size:13px;font-weight:600">Schliessen</button>
            </div>
        </div>`;
    overlay.style.display = 'flex';
}

async function deleteEmployeeAddress(addrId) {
    if (!selectedEmployeeId || !addrId) return;
    if (!(await liquidConfirm('Adresse wirklich löschen?'))) return;

    // Sofort aus der Liste nehmen — kein «Geister»-Eintrag bis der Reload fertig ist.
    document.querySelectorAll(`.emp-addr-row[data-addr-id="${addrId}"]`)
        .forEach(row => row.remove());
    _otherAddressesContainers().forEach(el => {
        if (!el.querySelector('.emp-addr-row')) el.innerHTML = '';
    });
    closeEmployeeAddressModal();

    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/addresses/${addrId}`, {
            method: 'DELETE', headers: ah()
        });
        if (!res.ok) {
            let msg = 'Fehler beim Löschen.';
            try { const j = await res.json(); msg = j.error || j.message || msg; } catch {}
            alert(msg);
            await loadEmployeeAddressesTab(selectedEmployeeId);
            return;
        }
        await loadEmployeeAddressesTab(selectedEmployeeId);
        // Familie-Modal: Dropdown/Felder aktualisieren, falls offen.
        if (document.getElementById('fmAddrAlt') && typeof fmRefreshAddressUi === 'function') {
            try { await fmRefreshAddressUi(null); } catch {}
        }
    } catch(e) {
        alert('Verbindungsfehler: ' + e.message);
        await loadEmployeeAddressesTab(selectedEmployeeId);
    }
}

// ══════════════════════════════════════════════════════════════════════
// BEWILLIGUNGS-VERLAUF (employee_permit_history)
// ──────────────────────────────────────────────────────────────────────
// Aktuelle Bewilligung wird auf employee.permit_type_id /
// permit_expiry_date vom Backend automatisch synchronisiert (jener
// History-Eintrag der heute gilt). Verlauf wird unter dem Aufenthalt-
// Block angezeigt. Modal für CREATE/UPDATE/DELETE — Backend ist
// /api/employees/{id}/permit-history.
//
// Spezialfall „Einbürgerung": Bewilligung-Dropdown auf „— keine —" lassen
// (permit_type_id = NULL), Notiz „Einbürgerung am ...". Im MA-Stamm
// zusätzlich Nationalität auf CH wechseln.
// ══════════════════════════════════════════════════════════════════════
let _permitHistoryCache = [];

async function loadPermitHistory(employeeId) {
    const container = document.getElementById('permitHistoryContent');
    if (!container) return;
    try {
        const res = await fetch(`/api/employees/${employeeId}/permit-history`, { headers: ah() });
        if (!res.ok) {
            container.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden des Verlaufs</span></div>';
            return;
        }
        _permitHistoryCache = await res.json();
        renderPermitHistory(_permitHistoryCache);
    } catch (e) {
        container.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler: ' + esc(e.message) + '</span></div>';
    }
}

// Walter-Vorgabe 07.06.2026: Bewilligungen werden NICHT mehr im Personal-Tab
// gerendert — sie leben jetzt im Bewilligung/QST-Tab. renderPermitHistory()
// bleibt als Compat-Shim: schreibt nur noch in den Container, FALLS einer
// existiert (alte Aufrufer wie loadPermitHistory). renderPermitListHtml()
// ist die kanonische Implementation und wird vom QST-Tab direkt aufgerufen.
function renderPermitListHtml(entries) {
    const list = entries || [];
    if (list.length === 0) {
        return `<div style="padding:12px;color:#94a3b8;font-style:italic;font-size:12.5px">Keine Bewilligung erfasst.</div>`;
    }

    // Walter 20.07.2026: GF (user) darf Bewilligungen voll pflegen.
    const isAdmin = typeof isOpsRole === 'function' ? isOpsRole()
        : (currentUser?.role === 'admin' || currentUser?.role === 'superuser' || currentUser?.role === 'user' || currentUser?.role === 'buchhaltung');
    // Schweizer Lokaldatum (nicht UTC) — sonst kann um Mitternacht das Datum kippen.
    const _n = new Date();
    const todayIso = `${_n.getFullYear()}-${String(_n.getMonth() + 1).padStart(2, '0')}-${String(_n.getDate()).padStart(2, '0')}`;
    const _iso = (d) => (d || '').toString().slice(0, 10);
    const _isExpired = (h) => !!(_iso(h.validTo) && _iso(h.validTo) < todayIso);
    const _isValidToday = (h) => {
        const from = _iso(h.validFrom);
        const to   = _iso(h.validTo);
        if (from && from > todayIso) return false;
        if (to && to < todayIso) return false;
        return true;
    };
    // «AKTUELL» nur wenn wirklich heute gültig — Datum schlägt Server-Flag
    // (Walter 18.07.2026: abgelaufene Bewilligung nie grün).
    const cur = list.find(h => _isValidToday(h) && (h.isCurrent || !h.validTo))
        ?? list.find(h => _isValidToday(h))
        ?? null;

    // Überlapp-Erkennung (paarweise) — für oranger Hinweis-Banner.
    const dates = list.map(h => ({
        from: h.validFrom ? h.validFrom.slice(0,10) : null,
        to:   h.validTo   ? h.validTo.slice(0,10)   : '9999-12-31'
    }));
    let hasOverlap = false;
    for (let i = 0; i < dates.length && !hasOverlap; i++) {
        for (let j = i + 1; j < dates.length && !hasOverlap; j++) {
            const a = dates[i], b = dates[j];
            if (a.from && b.from && a.from <= b.to && b.from <= a.to) hasOverlap = true;
        }
    }

    // Sortierung: nach neuer „neueste"-Definition. Höchstes valid_to, dann
    // höchstes valid_from. Offener Eintrag (valid_to NULL) gewinnt als max.
    const sorted = [...list].sort((a, b) => {
        const at = a.validTo || '9999-12-31';
        const bt = b.validTo || '9999-12-31';
        if (at !== bt) return bt.localeCompare(at);
        return (b.validFrom || '').localeCompare(a.validFrom || '');
    });

    const rowsHtml = sorted.map(h => {
        const fromTxt   = h.validFrom ? formatDate(h.validFrom) : '–';
        const toTxt     = h.validTo   ? formatDate(h.validTo)   : '<span style="color:#15803d;font-weight:600">offen</span>';
        const code      = h.permitCode || (h.permitTypeId ? 'Typ ' + h.permitTypeId : '<span style="color:#94a3b8">— keine —</span>');
        const desc      = h.permitDescription ? ' <span style="color:#94a3b8;font-size:11px">— ' + esc(h.permitDescription) + '</span>' : '';
        const noteTxt   = h.note ? `<div style="font-size:11.5px;color:#64748b;margin-top:3px">${esc(h.note)}</div>` : '';
        const isExpired = _isExpired(h);
        // Abgelaufen schlägt immer — nie grün/AKTUELL trotz isCurrent=true.
        const isCur     = !isExpired && cur && h.id === cur.id;
        const rowStyle  = isExpired
            ? 'padding:8px 12px;border:1.5px solid #fca5a5;border-radius:6px;background:#fef2f2;margin-bottom:5px;display:flex;align-items:center;gap:12px'
            : isCur
            ? 'padding:8px 12px;border:1.5px solid #16a34a;border-radius:6px;background:#f0fdf4;margin-bottom:5px;display:flex;align-items:center;gap:12px'
            : 'padding:8px 12px;border:1px solid #e2e8f0;border-radius:6px;background:#fafafa;margin-bottom:5px;display:flex;align-items:center;gap:12px';
        const aktuellPille = isExpired
            ? '<span style="display:inline-block;background:#fee2e2;color:#991b1b;padding:1px 8px;border-radius:9px;font-size:10.5px;font-weight:700;margin-left:6px;vertical-align:middle">ABGELAUFEN</span>'
            : isCur
            ? '<span style="display:inline-block;background:#dcfce7;color:#166534;padding:1px 8px;border-radius:9px;font-size:10.5px;font-weight:700;margin-left:6px;vertical-align:middle">AKTUELL</span>'
            : '';
        // Walter-Vorgabe 14.06.2026: pro Bewilligungs-Eintrag das verknüpfte
        // Doku zeigen (klein, grün wenn vorhanden, rot/„fehlt" wenn nicht).
        // Walter-Vorgabe 12.07.2026: der GRÜNE Button öffnet das verknüpfte
        // Dokument im Vorschau-Panel (anschauen!) — neu verknüpfen/ersetzen
        // läuft über das ⋮-Menü. Nur der gestrichelte «verknüpfen»-Button
        // (kein Doku) öffnet weiterhin direkt den Picker.
        const dokBtn = h.dokumentId
            ? `<button type="button" onclick='qstOpenBefreiungsDok(selectedEmployeeId, ${h.dokumentId})'
                   style="flex-shrink:0;background:#dcfce7;color:#166534;border:1px solid #86efac;padding:4px 10px;border-radius:6px;font-size:11.5px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:5px"
                   title="${esc(h.dokumentName || 'Dokument')} anschauen">
                   👁 Doku
               </button>`
            : `<button type="button" onclick='permitOpenDokuModal(${h.id})'
                   style="flex-shrink:0;background:#fff;color:#475569;border:1px dashed #cbd5e1;padding:4px 10px;border-radius:6px;font-size:11.5px;cursor:pointer">
                   🔗 Doku verknüpfen
               </button>`;
        // Walter 19.07.2026: bei abgelaufener Bewilligung SMS-Erinnerung an den MA
        // (eCall, analog Vertrags-SMS). Ohne Handynummer grau/disabled.
        const phone = (selectedEmployee?.phoneMobile || '').trim();
        const smsBtn = isExpired
            ? (phone
                ? `<button type="button" class="emp-contract-btn" style="flex-shrink:0"
                       title="Erinnerung per SMS: Bewilligung abgelaufen — bitte neue nachreichen"
                       onclick="permitExpiredSendSms(selectedEmployeeId, ${h.id})">SMS</button>`
                : `<button type="button" class="emp-contract-btn" style="flex-shrink:0;opacity:.45;cursor:not-allowed"
                       title="Keine Handynummer hinterlegt — bitte im Personal-Tab erfassen" disabled>SMS</button>`)
            : '';
        return `
        <div style="${rowStyle}">
            <div style="flex:1;min-width:0">
                <div style="font-weight:600;color:#475569;font-size:12.5px">${code}${desc}${aktuellPille}</div>
                <div style="font-size:11.5px;color:#64748b;margin-top:1px">
                    ${fromTxt} – ${toTxt}
                </div>
                ${noteTxt}
            </div>
            ${dokBtn}
            ${smsBtn}
            ${isAdmin ? `
            <div class="dok-menu-wrap" style="flex-shrink:0">
                <button class="dok-menu-btn" onclick="permitToggleMenu(event, ${h.id})" title="Aktionen">⋮</button>
                <div class="dok-menu" id="permitMenu-${h.id}">
                    <button class="dok-menu-item" onclick='openPermitHistoryModal(${h.id})'>Bearbeiten</button>
                    ${h.dokumentId ? `<button class="dok-menu-item" onclick='permitOpenDokuModal(${h.id})'>Doku ersetzen</button>` : ''}
                    <button class="dok-menu-item danger" onclick='deletePermitHistoryEntry(${h.id})'>Löschen</button>
                </div>
            </div>` : ''}
        </div>`;
    }).join('');

    // Walter-Vorgabe 07.06.2026 (final): Überlappungen sind nicht mehr
    // erlaubt — neue Einträge schliessen den Vorgänger automatisch ab. Wenn
    // trotzdem eine Überlappung sichtbar ist, sind das Altlasten aus dem
    // Import → Warnbanner und Hinweis auf manuelle Korrektur.
    const overlapHint = hasOverlap
        ? `<div style="margin-bottom:8px;padding:8px 12px;background:#fef3c7;border:1px solid #fbbf24;border-radius:6px;color:#92400e;font-size:12px;line-height:1.5">
               ⚠ <strong>Überlappung erkannt:</strong> Zwei oder mehr Einträge teilen sich einen Zeitraum (vermutlich Import-Altlasten). Bitte den älteren Eintrag bearbeiten und sein Bis-Datum auf den Tag vor dem Beginn der nächsten Bewilligung setzen.
           </div>`
        : '';
    const scrollWrap = list.length > 3
        ? 'max-height:280px;overflow-y:auto;border:1px solid #e2e8f0;border-radius:6px;padding:6px;background:#fff'
        : '';
    return `
        ${overlapHint}
        ${list.length > 3 ? `<div style="font-size:11px;color:#94a3b8;margin-bottom:4px">${list.length} Bewilligungen · ↕ scrollbar</div>` : ''}
        <div style="${scrollWrap}">
            ${rowsHtml}
        </div>
        <div class="permitSmsBox" style="margin-top:8px"></div>`;
}

// HR: Erinnerungs-SMS bei abgelaufener Bewilligung (Walter 19.07.2026).
// Text aus Moments-Vorlage BEWILLIGUNG_ABGELAUFEN; Versand über eCall.
async function permitExpiredSendSms(employeeId, historyId) {
    if (!employeeId || !historyId) return;
    const box = document.querySelector('.emp-tab-content.active .permitSmsBox')
        || document.querySelector('.permitSmsBox');

    let preview = null;
    try {
        const pr = await fetch(`/api/employees/${employeeId}/permit-history/${historyId}/sms-preview`, { headers: ah() });
        preview = await pr.json().catch(() => ({}));
        if (!pr.ok) {
            alert(preview.message || preview.error || ('Vorschau fehlgeschlagen (HTTP ' + pr.status + ')'));
            return;
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        return;
    }

    const nr = (preview.phone || '').trim();
    if (!nr) {
        alert('Für diesen Mitarbeitenden ist keine Handynummer hinterlegt.\n\nBitte zuerst im Personal-Tab die Telefonnummer erfassen.');
        return;
    }

    let hint = '';
    if (preview.lastSmsSentAt) {
        const d = new Date(preview.lastSmsSentAt);
        hint += `\n\nBereits gesendet am ${d.toLocaleDateString('de-CH')} ${d.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' })}.`;
    }
    const textPreview = (preview.smsText || '').trim();
    const confirmMsg = `Bewilligungs-Erinnerung per SMS an ${nr} wirklich senden?${hint}`
        + (textPreview ? `\n\nSMS-Kurztext (${preview.smsChars || textPreview.length}/${preview.smsMaxChars || 160}):\n${textPreview}` : '')
        + '\n\n(+ Link zur ausführlichen Mitteilung wird angehängt)';
    if (!(await liquidConfirm(confirmMsg, { yesLabel: 'Senden', noLabel: 'Abbruch' }))) return;

    if (box) box.innerHTML = '<div style="color:#8b8b8b;font-size:13px;padding:8px 0">📲 SMS wird gesendet …</div>';
    try {
        const res = await fetch(`/api/employees/${employeeId}/permit-history/${historyId}/send-sms`, {
            method: 'POST',
            headers: ah(),
        });
        const j = await res.json().catch(() => ({}));
        if (!res.ok || !j.ok) {
            if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">✗ ${esc(j.error || j.message || ('Fehler HTTP ' + res.status))}</div>`;
            return;
        }
        const redirectNote = j.redirectedTo
            ? `<div style="margin-top:6px;color:#6b5a1f">⚠ Test-Umleitung aktiv — die SMS ging an ${esc(j.redirectedTo)}.</div>`
            : '';
        if (box) box.innerHTML = `
            <div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:12px 14px;font-size:13px;line-height:1.55">
                ✓ Bewilligungs-SMS gesendet an ${esc(j.to || nr)}.
                ${redirectNote}
            </div>`;
        box?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    } catch (e) {
        if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

function renderPermitHistory(entries) {
    const container = document.getElementById('permitHistoryContent');
    if (!container) return;
    container.innerHTML = renderPermitListHtml(entries);
}

/**
 * Walter-Vorgabe 14.06.2026: Doku-Picker für genau einen Permit-History-
 * Eintrag öffnen. Setzt den Kontext (permitHistoryId) und delegiert an
 * den universellen openAusweisDokuModal mit kind='permit_history'.
 */
function permitOpenDokuModal(historyId) {
    if (!selectedEmployeeId || !historyId) return;
    window._ausweisDokuCtx = {
        empId: selectedEmployeeId,
        kind:  'permit_history',
        permitHistoryId: historyId
    };
    openAusweisDokuModal(selectedEmployeeId, 'permit_history', { permitHistoryId: historyId });
}

function togglePermitHistory() {
    const inner = document.getElementById('permitHistoryListInner');
    const icon  = document.getElementById('permitHistoryToggleIcon');
    const btn   = document.getElementById('permitHistoryToggleBtn');
    if (!inner) return;
    const open = inner.style.display !== 'none';
    inner.style.display = open ? 'none' : 'block';
    if (icon) icon.textContent = open ? '▸' : '▾';
    if (btn) {
        const span = btn.querySelector('span:last-child');
        if (span) {
            const count = inner.children.length;
            span.textContent = open ? `Verlauf anzeigen (${count})` : `Verlauf ausblenden`;
        }
    }
}

async function openPermitHistoryModal(entryId) {
    // Auto-Verknüpfung des gelesenen Ausweis-Dokus (Walter 23.08.2026):
    // wird beim OCR gesetzt, beim Öffnen des Modals zurückgesetzt.
    window._phfOcrDocId = null;
    if (!selectedEmployeeId) return;
    const permitTypes = await getPermitTypes();
    const entry = entryId ? (_permitHistoryCache.find(h => h.id === entryId) || null) : null;
    const isEdit = entry !== null;
    const today = new Date().toISOString().slice(0,10);

    // Walter-Vorgabe 07.06.2026: bei NEUer Bewilligung den Typ der aktuellsten
    // Bewilligung vorbelegen — meist ist die neue Bewilligung eine Verlängerung
    // desselben Typs (B → B mit neuem Ablauf), nicht ein Wechsel auf C.
    const aktuelle = !entry ? (_permitHistoryCache || []).find(h => h.isCurrent) : null;
    const defaultPermitTypeId = aktuelle?.permitTypeId ?? null;
    // Default-ValidFrom: Tag nach dem ValidTo der aktuellsten Bewilligung —
    // so schliesst die neue nahtlos an. Fallback = heute.
    let defaultValidFrom = today;
    if (aktuelle?.validTo) {
        const d = new Date(aktuelle.validTo);
        if (!isNaN(d.getTime())) {
            d.setDate(d.getDate() + 1);
            defaultValidFrom = d.toISOString().slice(0,10);
        }
    }

    const permitOptions = permitTypes
        .filter(p => p.isActive !== false)
        .map(p => {
            const sel = entry
                ? entry.permitTypeId === p.id
                : defaultPermitTypeId === p.id;
            return `<option value="${p.id}" ${sel ? 'selected' : ''}>${p.description ?? p.code}</option>`;
        })
        .join('');

    // Modal-Container: einmal anlegen, dann immer wiederverwenden
    let modal = document.getElementById('permitHistoryModal');
    if (!modal) {
        modal = document.createElement('div');
        modal.id = 'permitHistoryModal';
        modal.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,0.45);z-index:300;display:flex;align-items:center;justify-content:center;padding:20px';
        document.body.appendChild(modal);
    }
    // Code→TypId-Map für die OCR-Übernahme (Walter 12.07.2026)
    window._phfPermitCodeMap = {};
    permitTypes.forEach(p => { if (p.code) window._phfPermitCodeMap[p.code.toUpperCase()] = p.id; });

    modal.innerHTML = `
        <div style="display:flex;gap:14px;align-items:stretch;max-width:1100px;width:100%;max-height:90vh">
        <div style="background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);border-radius:14px;max-width:540px;width:100%;max-height:90vh;overflow:auto;padding:22px 24px;flex-shrink:0">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:14px">
                <h3 style="margin:0;font-size:18px;color:#0f172a">${isEdit ? _t('permit.modalTitleEdit','Bewilligung bearbeiten') : _t('permit.modalTitleNew','Neue Bewilligung')}</h3>
                <button onclick="closePermitHistoryModal()" style="background:none;border:none;font-size:22px;color:#94a3b8;cursor:pointer">×</button>
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px 16px">
                <div style="grid-column:1 / -1">
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">${_t('permit.field.type','Bewilligung')}</label>
                    <select id="phf-permitType" onchange="phfCheckCTypeHint()" style="width:100%;padding:9px 11px;border:1px solid #cbd5e1;border-radius:7px;font-size:13.5px">
                        <option value="">${_t('fam.field.permitDefault','— keine (Einbürgerung / CH-Bürger) —')}</option>
                        ${permitOptions}
                    </select>
                    <!-- Walter-Vorgabe 07.06.2026: bei C-Ausweis-Wechsel
                         (oder Einbürgerung) Hinweis dass QST nicht mehr nötig ist. -->
                    <div id="phf-cTypeHint" style="display:none;margin-top:8px;padding:8px 12px;background:#ecfdf5;border:1px solid #6ee7b7;border-radius:6px;color:#065f46;font-size:12px;line-height:1.5"></div>
                </div>
                <div>
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">${_t('permit.field.validFrom','Gültig ab')} *</label>
                    <input id="phf-validFrom" type="date" value="${entry?.validFrom ?? defaultValidFrom}"
                           style="width:100%;padding:9px 11px;border:1px solid #cbd5e1;border-radius:7px;font-size:13.5px">
                </div>
                <div>
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">
                        ${_t('permit.field.validTo','Gültig bis')}
                    </label>
                    <input id="phf-validTo" type="date" value="${entry?.validTo ?? entry?.permitExpiryDate ?? ''}"
                           style="width:100%;padding:9px 11px;border:1px solid #cbd5e1;border-radius:7px;font-size:13.5px">
                    <div style="font-size:11px;color:#64748b;margin-top:3px">Ablauf-Datum auf dem Ausweis. Leer = aktuell offen.</div>
                </div>
                <div style="grid-column:1 / -1">
                    <label style="font-size:12px;font-weight:600;color:#475569;display:block;margin-bottom:4px">${_t('permit.field.note','Notiz')}</label>
                    <textarea id="phf-note" rows="2"
                              style="width:100%;padding:9px 11px;border:1px solid #cbd5e1;border-radius:7px;font-size:13px;font-family:inherit">${entry?.note ? esc(entry.note).replace(/&quot;/g,'"') : ''}</textarea>
                </div>
            </div>
            <div style="display:flex;gap:8px;justify-content:flex-end;margin-top:18px;padding-top:14px;border-top:1px solid #e2e8f0">
                <button onclick="closePermitHistoryModal()"
                        style="padding:9px 16px;border:1px solid #cbd5e1;border-radius:7px;background:#fff;color:#475569;cursor:pointer;font-size:13px">${_t('permit.btn.cancel','Abbrechen')}</button>
                <button class="btn-primary" onclick="savePermitHistoryEntry(${entry?.id ?? 'null'})"
                        style="padding:9px 18px;font-size:13px">${_t('permit.btn.save','Speichern')}</button>
            </div>
            <div id="phf-error" style="margin-top:10px;color:#b91c1c;font-size:12.5px"></div>
        </div>
        <div id="phf-docpanel" style="display:none;background:#fff;border-radius:14px;flex:1;min-width:380px;max-height:90vh;padding:14px;flex-direction:column;gap:8px">
            <div style="display:flex;align-items:center;justify-content:space-between;gap:8px">
                <div id="phf-docname" style="font-size:12.5px;font-weight:700;color:#3f3f3f;overflow:hidden;text-overflow:ellipsis;white-space:nowrap"></div>
                <div style="display:flex;gap:6px;flex-shrink:0">
                    <button id="phf-zoombtn" style="display:none;background:rgba(255,255,255,0.6);border:1px solid #e2ddd3;color:#3f3f3f;border-radius:10px;padding:6px 13px;font-size:12.5px;font-weight:600;cursor:pointer" title="Im grossen Vorschaufenster öffnen (mit Drucken/Zoom)">⤢ Vergrössern</button>
                    <button id="phf-ocrbtn" style="display:none;background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:6px 13px;font-size:12.5px;font-weight:600;cursor:pointer">🔍 Ausweis lesen</button>
                </div>
            </div>
            <div id="phf-ocrresult" style="display:none;font-size:12px;padding:7px 10px;border-radius:8px"></div>
            <div id="phf-docview" style="flex:1;min-height:300px;background:#f6f3ee;border:1px solid #e7e1d8;border-radius:10px;overflow:hidden;display:flex;align-items:center;justify-content:center"></div>
        </div>
        </div>`;
    modal.style.display = 'flex';
    // Ausweis-Dokument (linked_field_code='permit') daneben anzeigen —
    // Walter-Vorgabe 12.07.2026: erleichtert das Abtippen; OCR füllt vor.
    phfLoadPermitDoc(selectedEmployeeId);
    // Walter-Vorgabe 07.06.2026: Hinweis sofort prüfen (bei Bearbeitung
    // schon C ausgewählt, oder bei Neu wenn vorherige Bewilligung B war
    // und User wechselt auf C).
    phfCheckCTypeHint();
}

// Walter-Vorgabe 07.06.2026: Hinweis im Permit-Modal — wenn der User C
// auswählt (Niederlassungsbewilligung) und es noch offene QST-Einträge
// gibt, blenden wir einen grünen Info-Banner ein: „QST nicht mehr nötig".
function phfCheckCTypeHint() {
    const sel    = document.getElementById('phf-permitType');
    const hintEl = document.getElementById('phf-cTypeHint');
    if (!sel || !hintEl) return;
    const opt = sel.options[sel.selectedIndex];
    const txt = (opt?.textContent || '').toUpperCase();
    const isC = txt.startsWith('C ') || txt.includes('AUSWEIS C') || txt.includes('NIEDERLASSUNG');
    const isNone = !sel.value; // Einbürgerung
    if (!isC && !isNone) {
        hintEl.style.display = 'none';
        hintEl.innerHTML = '';
        return;
    }
    // QST-Einträge prüfen — gibt's einen mit ValidTo NULL?
    let qstOffen = false;
    // _empQstCache existiert evtl. nicht — wir greifen auf den laufenden
    // QST-Tab zurück (gewohntes Muster) oder akzeptieren undefined.
    try {
        const cur = (window._empQstCache || []).some(e => !e.validTo);
        qstOffen = !!cur;
    } catch { /* egal */ }
    const titel = isNone
        ? 'Einbürgerung — Quellensteuer entfällt'
        : 'C-Ausweis — Quellensteuer entfällt';
    const body  = isNone
        ? 'Schweizer Bürger sind nicht mehr quellensteuerpflichtig.'
        : 'Mit dem C-Ausweis (Niederlassungsbewilligung) entfällt die QST-Pflicht.';
    const offenHinweis = qstOffen
        ? '<br><strong>Hinweis:</strong> der MA hat noch offene QST-Einträge — bitte im QST-Bereich das Ende-Datum setzen.'
        : '';
    hintEl.innerHTML = `ℹ <strong>${titel}.</strong> ${body}${offenHinweis}`;
    hintEl.style.display = 'block';
}

function closePermitHistoryModal() {
    const m = document.getElementById('permitHistoryModal');
    if (m) m.style.display = 'none';
}

async function savePermitHistoryEntry(entryId) {
    if (!selectedEmployeeId) return;
    const permitTypeRaw = document.getElementById('phf-permitType').value;
    const dto = {
        permitTypeId:     permitTypeRaw ? parseInt(permitTypeRaw) : null,
        validFrom:        document.getElementById('phf-validFrom').value,
        validTo:          document.getElementById('phf-validTo').value || null,
        note:             document.getElementById('phf-note').value.trim() || null,
        // Gelesener Ausweis-Scan automatisch mitverknüpfen (Walter 23.08.2026,
        // nur beim Neu-Anlegen — bestehende Einträge behalten ihr Doku).
        dokumentId:       !entryId && window._phfOcrDocId ? window._phfOcrDocId : null
    };
    const errEl = document.getElementById('phf-error');
    errEl.textContent = '';
    if (!dto.validFrom) { errEl.textContent = 'Gültig ab ist Pflicht.'; return; }
    // Walter-Vorgabe 01.06.2026: ValidTo (= Ablauf-Datum auf dem Ausweis) ist
    // Pflicht — ausser bei CH-Bürger/Einbürgerung (kein PermitType).
    if (dto.permitTypeId && !dto.validTo) { errEl.textContent = 'Gültig bis (Ablauf-Datum) ist Pflicht.'; return; }
    if (dto.validTo && dto.validTo < dto.validFrom) { errEl.textContent = 'Gültig bis darf nicht vor Gültig ab liegen.'; return; }

    try {
        const url = entryId
            ? `/api/employees/${selectedEmployeeId}/permit-history/${entryId}`
            : `/api/employees/${selectedEmployeeId}/permit-history`;
        const res = await fetch(url, {
            method: entryId ? 'PUT' : 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            const j = await res.json().catch(() => ({}));
            errEl.textContent = j.error || ('Fehler beim Speichern (' + res.status + ')');
            return;
        }
        closePermitHistoryModal();
        // Walter-Vorgabe 07.06.2026: Bewilligungen leben jetzt im Tab
        // Bewilligung/QST. loadQuellensteuerTab lädt Bewilligungs-History
        // UND QST-Einträge in einem Rutsch und rendert beides. Falls der
        // Container fehlt (z.B. Modal aus dem Personal-Tab geöffnet), fällt
        // er still durch — der Reload der MA-Stammdaten unten frischt die
        // Info-Zeile dort sowieso auf.
        if (typeof loadQuellensteuerTab === 'function') {
            await loadQuellensteuerTab(selectedEmployeeId);
        }
        // Walter-Vorgabe 07.06.2026: MA-Stammdaten neu laden, damit die
        // Info-Zeile im Personal-Tab + Header (aktuelle Bewilligung, Gültig bis)
        // den frisch synchronisierten Stand zeigen. selectEmployee() lädt
        // das Detail neu, behält den aktiven Sub-Tab und re-rendert.
        if (typeof selectEmployee === 'function') {
            await selectEmployee(selectedEmployeeId);
        }
    } catch (e) {
        errEl.textContent = 'Verbindungsfehler: ' + e.message;
    }
}

async function deletePermitHistoryEntry(entryId) {
    if (!selectedEmployeeId || !entryId) return;
    if (!(await liquidConfirm('Diesen Bewilligungs-Eintrag wirklich löschen?\n\nDie aktuelle Bewilligung wird automatisch neu berechnet.'))) return;
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/permit-history/${entryId}`, {
            method: 'DELETE', headers: ah()
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        // Walter-Vorgabe 07.06.2026: gleiches Reload-Pattern wie nach Save.
        if (typeof loadQuellensteuerTab === 'function') {
            await loadQuellensteuerTab(selectedEmployeeId);
        }
        if (typeof selectEmployee === 'function') {
            await selectEmployee(selectedEmployeeId);
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// ══════════════════════════════════════════════════════════════════════
// BVG-Zusatz-Mitgliedschaft (Walter-Vorgabe 26.05.2026)
// ══════════════════════════════════════════════════════════════════════
// Versionierte Mitgliedschaft im BVG-Zusatz-Vorsorge-Programm. Belohnung,
// die Walter pro MA entscheidet (egal welches Vertragsmodell). Engine
// rechnet BVG_ZUSATZ-Beiträge NUR wenn am Periodenanfang eine offene
// Mitgliedschaft existiert.
let _bvgZusatzCache = [];

async function loadUniformDepotTab(employeeId) {
    const el = document.getElementById('uniformDepotContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const res = await fetch(`/api/employees/${employeeId}/uniform-depot`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>'; return; }
        renderUniformDepotTab(el, await res.json());
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler</span></div>';
    }
}

function renderUniformDepotTab(el, d) {
    if (!d || !d.status) {
        el.innerHTML = `<div style="padding:14px;background:#f8fafc;border:1px dashed #cbd5e1;border-radius:8px;color:#64748b;font-size:12.5px;line-height:1.5">
            Noch kein Depot — beim <strong>1. Lohn</strong> werden automatisch CHF 50 abgezogen und hier als einbehaltenes Depot geführt.
        </div>`;
        return;
    }
    const bal = Number(d.balance || 0).toFixed(2);
    const statusMap = {
        EINBEHALTEN:    { label: 'Einbehalten', color: '#92400e', bg: '#fffbeb', border: '#fde68a' },
        ZURUECKBEZAHLT: { label: 'Zurückbezahlt', color: '#166534', bg: '#dcfce7', border: '#86efac' },
        VERFALLEN:      { label: 'Verfallen', color: '#991b1b', bg: '#fee2e2', border: '#fecaca' },
    };
    const st = statusMap[d.status] || { label: d.status, color: '#475569', bg: '#f1f5f9', border: '#cbd5e1' };
    const charged = d.chargedPeriode === 'BACKFILL'
        ? 'Backfill (Eintritt vor 01.07.2026)'
        : (d.chargedPeriode || '–');
    const refund = d.refundPeriode
        ? `<div style="font-size:12px;color:#64748b;margin-top:4px">Rückerstattet in Periode ${esc(d.refundPeriode)}</div>`
        : '';
    // Rückgabe-Entscheidung nur bei Austritt (letzter Lohn / Korrektur) —
    // nicht bei aktiven MA die gerade den Eintritts-Abzug haben.
    const emp = (typeof allEmployees !== 'undefined' && allEmployees)
        ? allEmployees.find(x => x.id === (selectedEmployeeId || window.activeEmpId))
        : null;
    const hasExit = !!(emp?.exitDate || emp?.ExitDate);
    const ret = d.returnConfirmed === true
        ? '<div style="font-size:12px;color:#166534;margin-top:4px">Uniform zurückgegeben → Refund erscheint automatisch auf dem (Korrektur-)Lohnzettel</div>'
        : d.returnConfirmed === false
            ? '<div style="font-size:12px;color:#991b1b;margin-top:4px">Uniform nicht zurück → Depot verfällt (kein Refund)</div>'
            : (hasExit
                ? '<div style="font-size:12px;color:#92400e;margin-top:4px">Rückgabe noch nicht entschieden</div>'
                : '');
    const bem = d.bemerkung ? `<div style="font-size:11.5px;color:#94a3b8;margin-top:6px">${esc(d.bemerkung)}</div>` : '';
    const canDecide = hasExit && d.status === 'EINBEHALTEN' && Number(d.balance || 0) > 0;
    const actions = canDecide ? `
        <div style="display:flex;gap:8px;flex-wrap:wrap;margin-top:12px">
            <button type="button" onclick="setUniformDepotReturn(true)"
                style="background:#3f3f3f;color:#fff;border:none;padding:7px 12px;border-radius:10px;font-size:12px;font-weight:600;cursor:pointer">Uniform zurück → Refund</button>
            <button type="button" onclick="setUniformDepotReturn(false)"
                style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid #cbd5e1;padding:7px 12px;border-radius:10px;font-size:12px;font-weight:600;cursor:pointer">Nicht zurück → verfällt</button>
        </div>` : (d.status === 'EINBEHALTEN' && Number(d.balance || 0) > 0 && !hasExit
        ? `<div style="font-size:11.5px;color:#94a3b8;margin-top:10px">Rückgabe/Verfall wird beim <strong>Austritt</strong> (letzter Lohn) entschieden.</div>`
        : '');
    // Schlank (Walter 18.08.2026): eine kompakte Zeile statt grosser Block.
    el.innerHTML = `
        <div class="ud-card ud-${esc(d.status || '')}" style="padding:7px 12px;background:${st.bg};border:1px solid ${st.border};border-radius:10px;font-size:12px">
            <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
                <b class="ud-amt" style="font-size:13px;color:#1a1a1a">CHF ${bal}</b>
                <span style="color:#64748b">Belastet: ${esc(charged)}</span>
                <span class="ud-pill" style="font-size:10.5px;font-weight:700;padding:2px 9px;border-radius:999px;background:#fff;color:${st.color};border:1px solid ${st.border};margin-left:auto">${st.label}</span>
            </div>
            ${refund}${ret}${bem}
            ${actions}
        </div>`;
}

async function setUniformDepotReturn(returned) {
    const empId = selectedEmployeeId || window.activeEmpId;
    if (!empId) return;
    const msg = returned
        ? 'Uniform zurückgegeben — CHF 50 erscheinen als Refund auf dem nächsten (Korrektur-)Lohnzettel. Fortfahren?'
        : 'Uniform NICHT zurück — Depot verfällt, kein Refund. Fortfahren?';
    if (!confirm(msg)) return;
    try {
        const res = await fetch(`/api/employees/${empId}/uniform-depot/return`, {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ returned: !!returned }),
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            alert(err.message || err.error || 'Speichern fehlgeschlagen');
            return;
        }
        await loadUniformDepotTab(empId);
        if (typeof showToast === 'function') {
            showToast(returned ? 'Uniform zurück → Refund bereit' : 'Depot wird verfallen', 'success');
        }
    } catch (e) {
        alert('Netzwerkfehler: ' + (e?.message || e));
    }
}

async function loadBvgZusatzTab(employeeId) {
    const el = document.getElementById('bvgZusatzContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const res = await fetch(`/api/employees/${employeeId}/bvg-zusatz-member`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>'; return; }
        _bvgZusatzCache = await res.json();
        renderBvgZusatzTab(el, _bvgZusatzCache);
    } catch (e) {
        el.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler</span></div>';
    }
}

function renderBvgZusatzTab(el, entries) {
    if (!entries || entries.length === 0) {
        el.innerHTML = `<div style="padding:14px;background:#f8fafc;border:1px dashed #cbd5e1;border-radius:6px;color:#64748b;font-size:12.5px;text-align:center">
            Keine Mitgliedschaft erfasst — der MA bekommt aktuell keine BVG-Zusatz-Beiträge berechnet.
        </div>`;
        return;
    }
    // Neueste zuerst
    const sorted = [...entries].sort((a, b) => (b.validFrom ?? '').localeCompare(a.validFrom ?? ''));
    let html = '';
    sorted.forEach(e => {
        const vonStr = e.validFrom ? formatDate(e.validFrom) : '–';
        const bisStr = e.validTo   ? formatDate(e.validTo)   : '<span style="color:#15803d;font-weight:600">offen</span>';
        const isCurrent = !!e.isCurrent;
        const bem = e.bemerkung ? `<div style="font-size:11.5px;color:#64748b;margin-top:3px">${esc(e.bemerkung)}</div>` : '';
        html += `
        <div class="emp-family-card" style="border-left:3px solid ${isCurrent ? '#16a34a' : '#cbd5e1'};margin-bottom:8px">
            <div class="emp-family-card-head">
                <div>
                    <div class="emp-family-name" style="display:flex;align-items:center;gap:8px">
                        ${isCurrent ? '<span style="background:#dcfce7;color:#166534;font-size:10px;font-weight:700;padding:2px 7px;border-radius:10px;letter-spacing:.04em">AKTIV</span>' : ''}
                        <span>${vonStr} bis ${bisStr}</span>
                    </div>
                    ${bem}
                </div>
                <div style="display:flex;gap:6px">
                    <button class="btn-emp-edit" onclick="openBvgZusatzModal(${e.id})">
                        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
                        Bearbeiten
                    </button>
                    <button class="btn-emp-del" onclick="deleteBvgZusatz(${e.id})">Löschen</button>
                </div>
            </div>
        </div>`;
    });
    el.innerHTML = html;
}

function openBvgZusatzModal(entryId) {
    if (!selectedEmployeeId) return;
    const entry = entryId ? _bvgZusatzCache.find(m => m.id === entryId) : null;
    const titel = entry ? 'Mitgliedschaft bearbeiten' : 'Neue BVG-Zusatz-Mitgliedschaft';
    const vonVal = entry?.validFrom ? entry.validFrom.slice(0,10) : '';
    const bisVal = entry?.validTo   ? entry.validTo.slice(0,10)   : '';
    const bem    = entry?.bemerkung || '';

    // Walter-Vorgabe 27.05.2026: MA-Maske-Stil (ma-modal-box / ma-grid / ma-input).
    const html = `
    <div id="bvgZusatzModal" style="position:fixed;inset:0;background:rgba(15,23,42,.55);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px"
         onclick="if(event.target===this)document.getElementById('bvgZusatzModal').remove()">
      <div class="ma-modal-box narrow">
        <div class="ma-modal-head">
            <div class="ma-modal-title">${titel}</div>
            <button class="ma-modal-close" onclick="document.getElementById('bvgZusatzModal').remove()">✕</button>
        </div>
        <div class="ma-modal-body">
            <div style="background:#f6f3ee;border:1px solid #e7e1d8;padding:10px 12px;border-radius:8px;font-size:12px;color:#3f4d5e;margin-bottom:10px;line-height:1.5">
                Der MA bekommt BVG-Zusatz-Beiträge nur dann berechnet, wenn am Anfang der Lohnperiode eine offene Mitgliedschaft existiert. Beim Austritt aus dem Programm <strong>Gültig bis</strong> setzen.
            </div>
            <input type="hidden" id="bvgZusatzId" value="${entry?.id ?? ''}">

            <div class="emp-section-title">Gültigkeit</div>
            <div class="ma-grid cols-2">
                <div class="ma-field">
                    <div class="ma-field-label">Gültig ab *</div>
                    <input type="date" id="bvgZusatzVon" class="ma-input" value="${vonVal}">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Gültig bis <span class="opt">(leer = laufend)</span></div>
                    <input type="date" id="bvgZusatzBis" class="ma-input" value="${bisVal}">
                </div>
            </div>

            <div class="emp-section-title">Bemerkung</div>
            <div class="ma-grid cols-1">
                <div class="ma-field">
                    <div class="ma-field-label">Bemerkung <span class="opt">(optional)</span></div>
                    <textarea id="bvgZusatzBem" class="ma-textarea" placeholder="z.B. „Beförderung Restaurant-Manager 1.1.2026"">${esc(bem)}</textarea>
                </div>
            </div>
        </div>
        <div class="ma-modal-foot">
            <button class="btn btn-outline" onclick="document.getElementById('bvgZusatzModal').remove()">Abbrechen</button>
            <button class="btn btn-primary" onclick="saveBvgZusatz()">Speichern</button>
        </div>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

async function saveBvgZusatz() {
    const id   = document.getElementById('bvgZusatzId').value;
    const von  = document.getElementById('bvgZusatzVon').value;
    const bis  = document.getElementById('bvgZusatzBis').value;
    const bem  = document.getElementById('bvgZusatzBem').value.trim();
    if (!von) { alert('Bitte „Gültig ab" eintragen.'); return; }
    const dto = { validFrom: von, validTo: bis || null, bemerkung: bem || null };
    const url = id
        ? `/api/employees/${selectedEmployeeId}/bvg-zusatz-member/${id}`
        : `/api/employees/${selectedEmployeeId}/bvg-zusatz-member`;
    const res = await fetch(url, {
        method: id ? 'PUT' : 'POST',
        headers: { ...ah(), 'Content-Type':'application/json' },
        body: JSON.stringify(dto)
    });
    if (await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) {
        const body = await res.clone().json().catch(() => ({}));
        alert(body.message || 'Fehler beim Speichern.');
        return;
    }
    document.getElementById('bvgZusatzModal').remove();
    loadBvgZusatzTab(selectedEmployeeId);
}

async function deleteBvgZusatz(id) {
    if (!(await liquidConfirm('Diese Mitgliedschaft wirklich löschen? Wenn der MA aus dem Programm austritt, lieber „Gültig bis" setzen statt löschen.'))) return;
    const res = await fetch(`/api/employees/${selectedEmployeeId}/bvg-zusatz-member/${id}`, {
        method: 'DELETE', headers: ah()
    });
    if (await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) {
        const body = await res.clone().json().catch(() => ({}));
        alert(body.message || 'Fehler beim Löschen.');
        return;
    }
    loadBvgZusatzTab(selectedEmployeeId);
}

// ── Bewilligungs-Modal: Ausweis-Vorschau + OCR (Walter-Vorgabe 12.07.2026) ──
async function phfLoadPermitDoc(empId) {
    const panel = document.getElementById('phf-docpanel');
    if (!panel || !empId) return;
    try {
        const r = await fetch(`/api/documents/by-field?employeeId=${empId}&code=permit&all=true`, { headers: ah() });
        const docs = r.ok ? await r.json() : [];
        if (!Array.isArray(docs) || docs.length === 0) {
            panel.style.display = 'flex';
            document.getElementById('phf-docview').innerHTML =
                '<div style="color:#8b8b8b;font-size:12.5px;padding:18px;text-align:center">Kein Ausweis-Dokument hinterlegt.<br>Im Dokumente-Tab hochladen (Typ mit Feld-Verknüpfung «Bewilligung»), dann erscheint es hier.</div>';
            return;
        }
        panel.style.display = 'flex';
        // Mehrere Ausweis-Scans → Auswahl (Walter 12.07.2026): bisher wurde
        // stillschweigend der neueste genommen — der richtige muss wählbar sein.
        window._phfDocs = docs;
        const nameEl = document.getElementById('phf-docname');
        if (docs.length > 1) {
            nameEl.innerHTML = `<select id="phf-docselect" onchange="phfShowPermitDoc(parseInt(this.value))"
                style="max-width:100%;background:rgba(255,255,255,0.7);border:1px solid #e2ddd3;border-radius:10px;padding:5px 9px;font-size:12.5px;font-weight:600;color:#3f3f3f;cursor:pointer">
                ${docs.map(d => `<option value="${d.id}">${esc((d.bemerkung || '').trim() || d.filenameOriginal || 'Dokument')}${d.hochgeladenAm ? ' · ' + formatDate(d.hochgeladenAm) : ''}</option>`).join('')}
            </select>`;
        }
        await phfShowPermitDoc(docs[0].id);
    } catch (_) { /* Panel bleibt leer */ }
}

// Zeigt EINEN gewählten Ausweis-Scan im Panel (Vorschau + Buttons umbinden).
async function phfShowPermitDoc(docId) {
    const doc = (window._phfDocs || []).find(d => d.id === docId);
    if (!doc) return;
    if ((window._phfDocs || []).length <= 1)
        document.getElementById('phf-docname').textContent = (doc.bemerkung || '').trim() || doc.filenameOriginal || 'Ausweis';
    const res = document.getElementById('phf-ocrresult');
    if (res) res.style.display = 'none';   // OCR-Ergebnis gehört zum vorherigen Scan
    const ocrBtn = document.getElementById('phf-ocrbtn');
    ocrBtn.style.display = 'inline-flex';
    ocrBtn.onclick = () => phfOcrPermit(doc.id);
    const zoomBtn = document.getElementById('phf-zoombtn');
    zoomBtn.style.display = 'inline-flex';
    // Grosses Vorschaufenster (previewFileModal) — mit Drucken/Herunterladen.
    zoomBtn.onclick = () => previewUrlFetch(`/api/documents/preview/${doc.id}`, doc.filenameOriginal || 'ausweis', ah());

    // Vorschau als Blob (iframe kann keinen Bearer-Header senden)
    try {
        const pr = await fetch(`/api/documents/preview/${doc.id}`, { headers: ah() });
        if (!pr.ok) return;
        const blob = await pr.blob();
        const url = URL.createObjectURL(blob);
        const view = document.getElementById('phf-docview');
        if ((blob.type || '').startsWith('image/')) {
            view.innerHTML = `<img src="${url}" style="max-width:100%;max-height:100%;object-fit:contain">`;
        } else {
            view.innerHTML = `<iframe src="${url}" style="width:100%;height:100%;border:none"></iframe>`;
        }
    } catch (_) { /* Vorschau best-effort */ }
}

async function phfOcrPermit(docId) {
    const btn = document.getElementById('phf-ocrbtn');
    const res = document.getElementById('phf-ocrresult');
    if (btn) { btn.disabled = true; btn.textContent = 'liest…'; }
    try {
        const r = await fetch(`/api/documents/${docId}/ocr-permit`, { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) {
            res.style.display = 'block';
            res.style.cssText += ';background:#fef2f2;border:1px solid #fecaca;color:#b91c1c;display:block';
            res.textContent = j.message || j.error || ('OCR fehlgeschlagen (HTTP ' + r.status + ')');
            return;
        }
        // Walter-Vorgabe 23.08.2026: der gelesene Ausweis-Scan wird beim
        // Speichern automatisch mit dem neuen Bewilligungs-Eintrag verknüpft
        // (dokumentId im Create-DTO) — kein manuelles «Doku verknüpfen» mehr.
        window._phfOcrDocId = docId;
        const parts = [];
        if (j.permitCode) {
            const typId = window._phfPermitCodeMap?.[j.permitCode.toUpperCase()];
            if (typId) {
                const sel = document.getElementById('phf-permitType');
                if (sel) { sel.value = String(typId); if (typeof phfCheckCTypeHint === 'function') phfCheckCTypeHint(); }
            }
            parts.push('Typ ' + j.permitCode);
        }
        const fmtIso = iso => iso.slice(8,10) + '.' + iso.slice(5,7) + '.' + iso.slice(0,4);
        if (j.issued) {
            const inp = document.getElementById('phf-validFrom');
            if (inp) inp.value = j.issued;
            parts.push('ausgestellt ' + fmtIso(j.issued));
        }
        if (j.validUntil) {
            const inp = document.getElementById('phf-validTo');
            if (inp) inp.value = j.validUntil;
            parts.push('gültig bis ' + fmtIso(j.validUntil) + (j.mrzGelesen ? ' (MRZ ✓)' : ''));
        }
        // ZEMIS-Nr (MRZ Zeile 1) direkt am MA speichern — Personen-Stammdatum
        // (Walter 12.07.2026), unabhängig von der Bewilligungs-Version.
        if (j.zemisNr && selectedEmployeeId) {
            try {
                const zr = await fetch(`/api/employees/${selectedEmployeeId}/zemis-nr`, {
                    method: 'PATCH',
                    headers: { ...ah(), 'Content-Type': 'application/json' },
                    body: JSON.stringify({ zemisNr: j.zemisNr })
                });
                if (zr.ok) {
                    const zj = await zr.json().catch(() => null);
                    if (zj?.kept) {
                        parts.push('ZEMIS-Nr belassen (manuell erfasst: ' + zj.zemisNr + ')');
                    } else {
                        parts.push('ZEMIS-Nr ' + j.zemisNr + ' am MA gespeichert');
                        if (selectedEmployee) selectedEmployee.zemisNumber = j.zemisNr;
                    }
                }
            } catch (_) {}
        }
        // Rohtext IMMER einblendbar (Diagnose, Walter 12.07.2026) — auch bei Teilerfolg.
        const rohtext = j.excerpt
            ? `<details style="margin-top:6px"><summary style="cursor:pointer;font-size:11px">Gelesener Rohtext anzeigen</summary><pre style="white-space:pre-wrap;font-size:10.5px;max-height:160px;overflow:auto;margin:4px 0 0">${esc(j.excerpt)}</pre></details>`
            : '';
        res.style.display = 'block';
        if (parts.length) {
            res.style.cssText += ';background:#dcfce7;border:1px solid #86efac;color:#15803d;display:block';
            res.innerHTML = '✓ Gelesen: ' + esc(parts.join(' · ')) + ' — bitte mit dem Ausweis abgleichen, dann Speichern.' + rohtext;
        } else {
            res.style.cssText += ';background:#fef3c7;border:1px solid #fde68a;color:#92400e;display:block';
            res.innerHTML = 'Nichts Verwertbares erkannt — bitte von Hand erfassen.' + rohtext;
        }
    } catch (e) {
        res.style.display = 'block';
        res.textContent = 'OCR-Fehler: ' + e.message;
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = '🔍 Ausweis lesen'; }
    }
}

// ══════════════════════════════════════════════
// VERWARNUNGEN (Walter-Vorgabe 14.07.2026)
// ──────────────────────────────────────────────
// Eskalations-Verlauf pro MA: Stufe (1./2./letzte Verwarnung), angekreuzte
// Gründe aus dem Papier-Formular, Beschreibung, PFLICHT-Dokument
// (unterschriebenes Schreiben — Upload direkt im Modal oder bestehendes
// Dokument wählen). Kein Löschen: nur Storno (admin/superuser) mit Grund.
// ══════════════════════════════════════════════
let _vwList = [];
let _vwGruende = null;
let _vwEditId = null;

const VW_STUFEN = {
    VERWARNUNG_1: { label: '1. Verwarnung',      bg: '#fef3c7', fg: '#92400e' },
    VERWARNUNG_2: { label: '2. Verwarnung',      bg: '#fed7aa', fg: '#9a3412' },
    LETZTE:       { label: 'Letzte Verwarnung',  bg: '#fee2e2', fg: '#991b1b' }
};

async function loadVerwarnungenTab(employeeId) {
    const el = document.getElementById('verwarnungenContent');
    if (!el) return;
    if (selectedEmployee?.isPayrollExcluded) {
        el.innerHTML = `<div style="margin:14px 0;padding:12px 16px;background:#fef3c7;border:1px solid #fbbf24;border-radius:8px;color:#92400e;font-size:13px">
            <strong>⛔ MA ohne Lohn</strong> — keine Verwarnungs-Verwaltung für Phantom-MA.</div>`;
        return;
    }
    el.innerHTML = '<div class="emp-placeholder" style="height:120px"><span>Wird geladen…</span></div>';
    try {
        const r = await fetch(`/api/verwarnungen/by-employee/${employeeId}`, { headers: ah() });
        if (!r.ok) { el.innerHTML = `<div style="color:#dc2626;padding:16px">Fehler beim Laden (${r.status})</div>`; return; }
        _vwList = await r.json();
        renderVerwarnungenTab(el);
        // Bereich «BFS / Statistik» (Walter 13.08.2026) — kleine LSE-Ergänzungs-
        // felder am Ende des Restaurant-Admin-Tabs (nicht prominent im Stamm).
        if (typeof lseEmpBlockAppend === 'function' && ['admin', 'superuser'].includes(currentUser?.role))
            lseEmpBlockAppend(el, employeeId);
    } catch (e) {
        el.innerHTML = `<div style="color:#dc2626;padding:16px">Netzwerkfehler: ${e.message}</div>`;
    }
}

function renderVerwarnungenTab(el) {
    const aktiv = _vwList.filter(v => !v.storniert);
    // Hinweis-Banner: bei «Letzte Verwarnung» aktiv → rot.
    const hatLetzte = aktiv.some(v => v.stufe === 'LETZTE');
    const banner = aktiv.length === 0
        ? `<div style="padding:24px;text-align:center;color:#16a34a;font-size:13.5px;font-weight:600">✓ Keine Verwarnungen erfasst.</div>`
        : hatLetzte
            ? `<div style="margin-bottom:14px;padding:10px 14px;background:#fee2e2;border:1px solid #fca5a5;border-radius:8px;color:#991b1b;font-size:12.5px;font-weight:600">⚠ Letzte Verwarnung mit Kündigungsandrohung ausgesprochen — ${aktiv.length} aktive Verwarnung(en) im Verlauf.</div>`
            : `<div style="margin-bottom:14px;padding:10px 14px;background:#fef3c7;border:1px solid #fde68a;border-radius:8px;color:#92400e;font-size:12.5px">${aktiv.length} aktive Verwarnung(en) im Verlauf.</div>`;

    const rows = _vwList.map(v => {
        const st = VW_STUFEN[v.stufe] || { label: v.stufe, bg: '#ece9e2', fg: '#6b6152' };
        const datum = v.datum ? new Date(v.datum).toLocaleDateString('de-CH') : '–';
        const gruende = (v.gruende || '').split('\n').filter(Boolean);
        const stil = v.storniert ? 'opacity:0.55' : '';
        const stornoBadge = v.storniert
            ? `<span title="${esc(v.stornoGrund || '')}" style="font-size:11px;font-weight:700;padding:2px 8px;border-radius:10px;background:#e2e8f0;color:#475569;text-decoration:none">STORNIERT</span>` : '';
        const menu = `
            <div style="position:relative;display:inline-block">
                <button class="dok-menu-btn" onclick="vwToggleMenu(event, ${v.id})">⋮</button>
                <div class="dok-menu" id="vwMenu${v.id}" style="display:none;position:absolute;right:0;top:32px;z-index:50;background:#fff;border:1px solid #e2e8f0;border-radius:8px;box-shadow:0 8px 24px rgba(0,0,0,0.12);min-width:180px">
                    ${!v.storniert ? `<div class="dok-menu-item" onclick="openVerwarnungModal(${v.id})">✎ Bearbeiten / Scan nachreichen</div><div class="dok-menu-item" onclick="vwPrintFormular(${v.id})">🖨 Formular drucken</div>` : ''}
                    ${isOpsRole()
                        ? `<div class="dok-menu-item danger" onclick="vwDelete(${v.id})">🗑 Löschen</div>` : ''}
                </div>
            </div>`;
        return `<div style="border:1px solid #e5e0d6;border-radius:10px;padding:12px 14px;margin-bottom:10px;background:#faf8f5;${stil}">
            <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
                <span style="font-weight:700;font-size:13px">${datum}</span>
                <span style="font-size:11px;font-weight:700;padding:3px 10px;border-radius:10px;background:${st.bg};color:${st.fg}">${st.label}</span>
                ${stornoBadge}
                <span style="flex:1"></span>
                ${v.dokumentId
                    ? `<button class="dok-menu-btn" title="Verwarnungsschreiben ansehen (${esc(v.dokumentName || '')})" style="min-width:auto;padding:4px 10px" onclick="vwViewDoc(${v.dokumentId})">👁 Doku</button>`
                    : (!v.storniert ? `<span title="Formular drucken, unterschreiben lassen und den Scan über ✎ Bearbeiten hinterlegen" style="font-size:11px;font-weight:700;padding:3px 10px;border-radius:10px;background:#fee2e2;color:#991b1b">⚠ Schreiben fehlt</span>` : '')}
                ${menu}
            </div>
            ${gruende.length ? `<div style="margin-top:8px;display:flex;flex-wrap:wrap;gap:6px">${gruende.map(g =>
                `<span style="font-size:11.5px;padding:2px 9px;border-radius:10px;background:#ece9e2;color:#6b6152">${esc(g)}</span>`).join('')}</div>` : ''}
            ${v.beschreibung ? `<div style="margin-top:8px;font-size:12.5px;color:#3f3f3f;white-space:pre-wrap">${esc(v.beschreibung)}</div>` : ''}
            <div style="margin-top:8px;font-size:11px;color:#8b8b8b">Erfasst von ${esc(v.erstelltVon || '–')}${v.erstelltAm ? ' am ' + new Date(v.erstelltAm).toLocaleDateString('de-CH') : ''}</div>
        </div>`;
    }).join('');

    el.innerHTML = `
        ${_raTilesHtml()}
        <div class="emp-section-title">Verwarnungen</div>
        ${banner}
        ${rows}`;
}

// Restaurant-Admin-Kacheln (Walter 15.07.2026): Icon-Buttons im Stil der
// Startseiten-Module (Sketch-Icons von Walter in wwwroot/img).
function _raTilesHtml() {
    // Startseiten-Stil (Walter 15.07.2026 v5): Glas-Kachel, freigestelltes
    // Sketch-Icon oben, Beschriftung darunter.
    // Bewerbungsbogen / Kandidat an HR / Absenzkalender sind nach
    // McAdmin gezogen (Walter 15.08.2026) — nicht MA-bezogen.
    const tile = (img, title, onclick) => `
        <button type="button" class="ra-tile" onclick="${onclick}">
            <img src="img/${encodeURI(img)}?v=20260815d" alt="" loading="lazy">
            <span>${title}</span>
        </button>`;
    const kontoTiles = selectedEmployee?.isPayrollExcluded ? '' : `
        ${tile('Postfach passwort.png', 'Postfach-Passwort', 'postfachResetPassword(selectedEmployeeId)')}
        ${tile('onboarding qr.png', 'Onboarding-QR', 'postfachSetupQr(selectedEmployeeId)')}
        ${tile('face id zurück.png', 'Face ID zurücksetzen', 'faceIdAdminReset(selectedEmployeeId)')}`;
    return `<div class="ra-tile-row">
        ${tile('probezeit.png', 'Probezeit', 'openProbezeitModal(selectedEmployeeId)')}
        ${tile('arbeitsbestaetigung.png', 'Arbeitsbestätigung', 'openZeugnisModal(selectedEmployeeId, false, true)')}
        ${tile('verwarnung.png', 'Verwarnung', 'openVerwarnungModal(null)')}
        ${tile('Aufforderung.png', 'Arbeits Aufforderung', 'raOpenAufforderungArbeit(selectedEmployeeId)')}
        ${tile('Schlusszeugnis.png', 'Arbeitszeugnis', 'openZeugnisModal(selectedEmployeeId)')}
        ${tile('zwischenzeugnis.png', 'Zwischenzeugnis', 'openZeugnisModal(selectedEmployeeId, true)')}
        ${tile('umzug.svg', 'Umzug erfassen', 'openUmzugModal(selectedEmployeeId)')}
        ${kontoTiles}
    </div>`;
}

// Blanko-Bewerbungsbogen der gewählten Filiale (Walter 27.07.2026).
async function raBewerbungsbogenPdf() {
    const cpId = fixedCompanyProfileId
        || selectedEmployee?.employments?.find(e => e.isActive)?.companyProfileId
        || selectedEmployee?.employments?.[0]?.companyProfileId;
    if (!cpId) return alert('Bitte zuerst eine Filiale wählen.');
    try {
        if (typeof previewUrlFetch === 'function') {
            await previewUrlFetch(
                `/api/bewerbungsbogen/pdf?companyProfileId=${cpId}`,
                'Bewerbungsbogen.pdf',
                ah());
            return;
        }
        const r = await fetch(`/api/bewerbungsbogen/pdf?companyProfileId=${cpId}`, { headers: ah() });
        if (!r.ok) {
            const err = await r.json().catch(() => ({}));
            return alert(err.message || err.error || ('PDF fehlgeschlagen: HTTP ' + r.status));
        }
        const blob = await r.blob();
        if (typeof previewFileModal === 'function') await previewFileModal(blob, 'Bewerbungsbogen.pdf');
        else if (typeof saveBlobAsk === 'function') await saveBlobAsk(blob, 'Bewerbungsbogen.pdf');
    } catch (e) {
        alert('Bewerbungsbogen fehlgeschlagen: ' + (e?.message || e));
    }
}

// ── Probezeit (Restaurant Admin, Walter 20.07.2026) ──────────────────────
// Formular blanko · ein Gespräch mit Datum + Protokoll-Verknüpfung
// (Dokumenttyp Probezeitgespräch) · Kündigung während Probezeit.
function openProbezeitModal(empId) {
    const emp = selectedEmployee;
    if (!empId || !emp || emp.id !== empId) {
        if (typeof selectEmployee === 'function' && empId) selectEmployee(empId);
    }
    _pzEnsureModal();
    pzRefreshModal();
    document.getElementById('pzModal').style.display = 'flex';
}

function _pzEnsureModal() {
    if (document.getElementById('pzModal')) return;
    const div = document.createElement('div');
    div.id = 'pzModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center';
    div.innerHTML = `
    <div class="iv-modal-box" style="border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:560px;width:94%;max-height:92vh;overflow-y:auto;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:6px">
            <div style="font-size:16px;font-weight:800;color:#3f3f3f">Probezeit</div>
            <div style="display:flex;gap:12px;align-items:center">
                <button type="button" onclick="pzClose()" class="kd-btn-glass" style="font-size:13px;padding:7px 16px;border-radius:12px">← Zurück</button>
                <button type="button" onclick="pzClose()" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
            </div>
        </div>
        <div id="pzBody" style="font-size:13px;color:#3f3f3f"></div>
        <div style="display:flex;justify-content:flex-end;margin-top:18px">
            <button type="button" onclick="pzClose()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Schliessen</button>
        </div>
    </div>`;
    document.body.appendChild(div);
}

function pzClose() {
    const m = document.getElementById('pzModal');
    if (m) m.style.display = 'none';
}

// Dokument-Waehler AUS dem Probezeit-Modal (Walter 22.07.2026): das
// pzModal (z-index 9000) lag ueber dem Waehler (2400) — Dokumente waren
// nicht anklickbar. Darum: pzModal zu, Waehler auf, danach pzModal
// wieder oeffnen (Flag wird in closeAusweisDokuModal ausgewertet).
function pzOpenDokuPicker(empId, kind) {
    pzClose();
    window._pzReopenAfterDoku = empId;
    openAusweisDokuModal(empId, kind);
}

function pzRefreshModal() {
    const body = document.getElementById('pzBody');
    const emp = selectedEmployee;
    if (!body || !emp) return;
    const ende = emp.probationEndDate ? formatDate(emp.probationEndDate) : '–';
    const inPz = emp.probationEndDate && new Date(emp.probationEndDate) >= new Date(new Date().toDateString());
    // Ein Gespräch (Walter 21.07.2026) — Backend-Feld nr=1.
    const am = emp.probezeitGespraech1Am;
    const dokId = emp.probezeitGespraech1DokumentId;
    const kind = 'probezeit_gespraech1';
    // Leer → heutiges Datum vorschlagen (Walter 21.07.2026); speichern erst bei Änderung.
    const _td = new Date();
    const todayIso = `${_td.getFullYear()}-${String(_td.getMonth() + 1).padStart(2, '0')}-${String(_td.getDate()).padStart(2, '0')}`;
    const amIso = am ? String(am).slice(0, 10) : todayIso;
    const amTxt = am ? formatDate(am) : null;
    const ok = !!(am && dokId);
    const gespraechRow = `<div style="padding:12px;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.28);border-radius:12px;margin-bottom:10px">
            <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;margin-bottom:8px">
                <div style="font-weight:750;color:#3f3f3f">Probezeitgespräch</div>
                <span style="font-size:11px;font-weight:700;padding:2px 9px;border-radius:999px;${ok
                    ? 'background:rgba(22,163,74,0.12);color:#166534;border:1px solid rgba(34,197,94,0.35)'
                    : 'background:rgba(244,63,94,0.10);color:#9f1239;border:1px solid rgba(251,113,133,0.35)'}">${ok ? '✓ erledigt' : 'offen'}</span>
            </div>
            <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin-bottom:8px">
                <label style="font-size:11.5px;font-weight:700;color:#646464">Durchgeführt am</label>
                <input type="date" id="pzAm1" value="${amIso}" style="padding:6px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white"
                       onchange="pzSaveDate(${emp.id}, 1, this.value)">
                ${amTxt ? `<span style="font-size:12px;color:#646464">${amTxt}</span>` : ''}
            </div>
            <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap">
                ${dokId
                    ? `<button type="button" onclick="qstOpenBefreiungsDok(${emp.id}, ${dokId}, {sticky:true})" style="background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:6px 12px;cursor:pointer;font-size:12px;font-weight:700">👁 Protokoll</button>
                       <button type="button" onclick="pzOpenDokuPicker(${emp.id},'${kind}')" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:10px;padding:6px 12px;cursor:pointer;font-size:12px;font-weight:700">Protokoll ersetzen</button>
                       <button type="button" onclick="ausweisDokuUnlink(${emp.id},'${kind}')" style="background:none;border:none;color:#b91c1c;font-size:12px;font-weight:700;cursor:pointer;text-decoration:underline">lösen</button>`
                    : `<button type="button" onclick="pzOpenDokuPicker(${emp.id},'${kind}')" style="background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:6px 12px;cursor:pointer;font-size:12px;font-weight:700">📄 Protokoll verknüpfen</button>
                       <span style="font-size:11.5px;color:#8b8b8b">→ Dokus · Mitarbeiterentwicklung · Probezeitgespräch</span>`}
            </div>
            ${am && !dokId ? `<div style="margin-top:8px;font-size:11.5px;color:#9f1239;font-weight:650">Datum gesetzt — bitte noch das ausgefüllte Protokoll verknüpfen.</div>` : ''}
            ${!am && dokId ? `<div style="margin-top:8px;font-size:11.5px;color:#a16207;font-weight:650">Protokoll verknüpft — bitte noch das Durchführungsdatum setzen.</div>` : ''}
        </div>`;
    body.innerHTML = `
        <div style="margin-bottom:14px;padding:10px 12px;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.28);border-radius:10px">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:2px">Probezeit bis</div>
            <div style="font-size:15px;font-weight:750;color:#3f3f3f">${ende}${inPz ? ' <span style="font-size:11.5px;font-weight:650;color:#a16207">(läuft)</span>' : ''}</div>
        </div>
        <div style="display:flex;gap:8px;flex-wrap:wrap;margin-bottom:14px">
            <button type="button" onclick="pzGenerateBericht()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 14px;cursor:pointer;font-size:12.5px;font-weight:700">📋 Probezeit Gespräch</button>
            <button type="button" onclick="pzOpenKuendigung(${emp.id})" style="background:rgba(255,255,255,0.55);color:#9f1239;border:1px solid rgba(251,113,133,0.40);border-radius:12px;padding:8px 14px;cursor:pointer;font-size:12.5px;font-weight:700">Kündigung während Probezeit</button>
        </div>
        <div style="font-size:12px;color:#646464;margin-bottom:10px">Protokoll generieren → ausdrucken/ausfüllen → unterschreiben → Scan unter Dokus → Mitarbeiterentwicklung → Probezeitgespräch verknüpfen und Datum bestätigen.</div>
        ${gespraechRow}`;
}

async function pzSaveDate(empId, nr, iso) {
    try {
        const r = await fetch(`/api/employees/${empId}/probezeit-gespraech`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ nr, am: iso || null })
        });
        if (!r.ok) {
            let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){}
            return alert('Speichern fehlgeschlagen: ' + t);
        }
        if (typeof selectEmployee === 'function') await selectEmployee(empId);
        pzRefreshModal();
    } catch (e) { alert('Fehler: ' + e.message); }
}

async function pzGenerateBericht() {
    const emp = selectedEmployee;
    if (!emp?.id) return;
    const url = `/api/employees/${emp.id}/probezeitbericht-pdf`;
    const fname = `PZ-${emp.employeeNumber || emp.id}-${emp.firstName || 'MA'}.pdf`;
    try {
        if (typeof previewUrlFetch === 'function') {
            await previewUrlFetch(url, fname, ah());
            return;
        }
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) {
            let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){}
            return alert('PDF-Fehler: ' + t);
        }
        const blob = await r.blob();
        if (typeof previewFileModal === 'function') await previewFileModal(blob, fname);
        else if (typeof saveBlobAsk === 'function') await saveBlobAsk(blob, fname);
    } catch (e) { alert('Fehler: ' + e.message); }
}

async function pzOpenKuendigung(empId) {
    pzClose();
    // Rücksprung: Restaurant Admin + Probezeit-Modal (nicht HR-Hub) —
    // GF kommt von hier (Walter 21.07.2026).
    if (typeof kuSetReturnTo === 'function') {
        kuSetReturnTo({
            page: 'mitarbeiter',
            empId,
            tab: 'verwarnungen',
            reopenProbezeit: true
        });
    }
    if (typeof showPage === 'function') showPage('kuendigung');
    // Picker nach Init setzen (kuendigungInit läuft in showPage).
    // Grund «probezeit» VOR kuLoadInfo setzen — sonst rechnet die Frist
    // noch als ordentlich (Walter 21.07.2026).
    const trySelect = () => {
        const sel = document.getElementById('kuEmpSelect');
        if (!sel || !sel.options.length) return false;
        sel.value = String(empId);
        const gt = document.getElementById('kuGrundType');
        if (gt) gt.value = 'probezeit';
        if (typeof kuOnEmpChange === 'function') kuOnEmpChange();
        return true;
    };
    if (!trySelect()) {
        let n = 0;
        const t = setInterval(() => { if (trySelect() || ++n > 20) clearInterval(t); }, 100);
    }
}

function vwToggleMenu(ev, id) {
    ev.stopPropagation();
    document.querySelectorAll('[id^="vwMenu"]').forEach(m => { if (m.id !== 'vwMenu' + id) m.style.display = 'none'; });
    const m = document.getElementById('vwMenu' + id);
    if (m) m.style.display = m.style.display === 'none' ? 'block' : 'none';
    document.addEventListener('click', () => document.querySelectorAll('[id^="vwMenu"]').forEach(x => x.style.display = 'none'), { once: true });
}

async function vwViewDoc(dokId) {
    if (typeof previewUrlFetch === 'function') {
        await previewUrlFetch(`/api/documents/preview/${dokId}`, 'Verwarnung.pdf', ah());
    }
}

async function _vwLoadGruende() {
    if (_vwGruende) return _vwGruende;
    try {
        const r = await fetch('/api/verwarnungen/gruende', { headers: ah() });
        if (r.ok) _vwGruende = await r.json();
    } catch {}
    return _vwGruende || [];
}

async function openVerwarnungModal(id) {
    if (!selectedEmployeeId) return;
    _vwEditId = id;
    const edit = id ? _vwList.find(v => v.id === id) : null;
    const gruende = await _vwLoadGruende();
    const gewaehlt = new Set(edit ? (edit.gruende || '').split('\n').filter(Boolean) : []);

    let ov = document.getElementById('vwModal');
    if (ov) ov.remove();
    ov = document.createElement('div');
    ov.id = 'vwModal';
    ov.style.cssText = 'position:fixed;inset:0;z-index:4000;background:rgba(60,55,48,0.4);display:flex;align-items:center;justify-content:center;padding:20px';
    ov.onclick = e => { if (e.target === ov) ov.remove(); };

    const pill = 'display:flex;align-items:center;gap:7px;background:transparent;border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:6px 10px;cursor:pointer;font-size:12.5px;color:#3f3f3f';
    const heute = new Date();
    const heuteIso = `${heute.getFullYear()}-${String(heute.getMonth()+1).padStart(2,'0')}-${String(heute.getDate()).padStart(2,'0')}`;

    ov.innerHTML = `
        <div class="iv-modal-box" style="border:1px solid rgba(255,255,255,0.62);border-radius:18px;max-width:760px;width:100%;max-height:92vh;overflow:auto;padding:22px 24px;box-shadow:0 24px 60px rgba(60,55,48,0.22)">
            <div style="display:flex;align-items:flex-start;justify-content:space-between;gap:12px;margin-bottom:2px">
                <div style="font-size:16px;font-weight:800;color:#3f3f3f">${edit ? 'Verwarnung bearbeiten' : 'Verwarnung erfassen'}</div>
                <button type="button" onclick="document.getElementById('vwModal').remove()" class="kd-btn-glass" style="font-size:13px;padding:7px 16px;border-radius:12px">← Zurück</button>
            </div>
            <div style="font-size:12.5px;color:#8b8b8b;margin-bottom:14px">${selectedEmployee ? esc(selectedEmployee.firstName + ' ' + selectedEmployee.lastName) : ''}</div>

            <div style="display:flex;gap:12px;margin-bottom:14px">
                <div style="flex:1">
                    <div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;margin-bottom:5px">Datum</div>
                    <input type="date" id="vwDatum" value="${edit?.datum ? String(edit.datum).slice(0,10) : heuteIso}"
                           style="width:100%;box-sizing:border-box;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.35);border-radius:10px;padding:8px 12px;font-size:13px;color:#3f3f3f">
                </div>
                <div style="flex:1.4">
                    <div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;margin-bottom:5px">Stufe</div>
                    <select id="vwStufe" style="width:100%;box-sizing:border-box;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.35);border-radius:10px;padding:8px 12px;font-size:13px;color:#3f3f3f">
                        <option value="VERWARNUNG_1" ${(!edit || edit.stufe==='VERWARNUNG_1')?'selected':''}>1. Verwarnung</option>
                        <option value="VERWARNUNG_2" ${edit?.stufe==='VERWARNUNG_2'?'selected':''}>2. Verwarnung</option>
                        <option value="LETZTE" ${edit?.stufe==='LETZTE'?'selected':''}>Letzte Verwarnung (Kündigungsandrohung)</option>
                    </select>
                </div>
            </div>

            <div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;margin-bottom:6px">Gründe (wie Papier-Formular, Mehrfachauswahl)</div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:6px;margin-bottom:14px">
                ${gruende.map((g, i) => `<label style="${pill}"><input type="checkbox" class="vwGrund" value="${esc(g)}" ${gewaehlt.has(g)?'checked':''}> ${esc(g)}</label>`).join('')}
            </div>

            <div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;margin-bottom:5px">Beschreibung / Bemerkung</div>
            <textarea id="vwBeschreibung" rows="3" placeholder="Was ist vorgefallen? Was wird erwartet?"
                      style="width:100%;box-sizing:border-box;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.35);border-radius:10px;padding:8px 12px;font-size:13px;color:#3f3f3f;resize:vertical;margin-bottom:14px">${edit?.beschreibung ? esc(edit.beschreibung) : ''}</textarea>

            <div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;margin-bottom:6px">Unterschriebenes Verwarnungsschreiben</div>
            <div style="font-size:11.5px;color:#8b8b8b;margin:-2px 0 8px">Ablauf: unten «Formular drucken» → von MA + Schichtführer unterschreiben lassen → Scan hier hochladen (auch nachträglich über ✎ Bearbeiten möglich).</div>
            <div style="background:rgba(255,255,255,0.45);border:1px solid rgba(139,139,139,0.25);border-radius:12px;padding:12px;margin-bottom:14px">
                ${edit?.dokumentId ? `<div style="font-size:12.5px;color:#15803d;font-weight:600;margin-bottom:8px">✓ Verknüpft: ${esc(edit.dokumentName || 'Dokument #' + edit.dokumentId)} <span style="color:#8b8b8b;font-weight:400">— neues Hochladen/Wählen ersetzt es</span></div>` : ''}
                <label style="${pill};margin-bottom:8px"><input type="radio" name="vwDocMode" value="upload" checked> 📤 Datei hochladen (Scan)</label>
                <input type="file" id="vwFile" accept=".pdf,.jpg,.jpeg,.png,.tif,.tiff" style="font-size:12px;margin:0 0 10px 24px;display:block">
                <label style="${pill};margin-bottom:8px"><input type="radio" name="vwDocMode" value="existing"> 📁 Bestehendes Dokument wählen</label>
                <select id="vwExistingDoc" style="display:none;width:calc(100% - 24px);margin-left:24px;box-sizing:border-box;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.35);border-radius:10px;padding:8px 12px;font-size:12.5px;color:#3f3f3f">
                    <option value="">– lädt… –</option>
                </select>
            </div>

            <div id="vwAlert"></div>
            <div style="display:flex;gap:10px;align-items:center">
                <button onclick="vwFormularPdf()"
                        style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700">🖨 Formular drucken</button>
                <span style="flex:1"></span>
                <button onclick="document.getElementById('vwModal').remove()"
                        style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
                <button id="vwSaveBtn" onclick="vwSave()"
                        style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:10px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Speichern</button>
            </div>
        </div>`;
    document.body.appendChild(ov);

    // Radio-Umschaltung Upload vs. bestehendes Dokument
    ov.querySelectorAll('input[name="vwDocMode"]').forEach(r => r.addEventListener('change', async () => {
        const mode = ov.querySelector('input[name="vwDocMode"]:checked')?.value;
        document.getElementById('vwFile').style.display = mode === 'upload' ? 'block' : 'none';
        const sel = document.getElementById('vwExistingDoc');
        sel.style.display = mode === 'existing' ? 'block' : 'none';
        if (mode === 'existing' && sel.options.length <= 1) await _vwFillExistingDocs(sel);
    }));
}

async function _vwFillExistingDocs(sel) {
    try {
        const r = await fetch(`/api/documents/by-employee/${selectedEmployeeId}?all=true`, { headers: ah() });
        if (!r.ok) return;
        const docs = await r.json();
        sel.innerHTML = '<option value="">– Dokument wählen –</option>' +
            (Array.isArray(docs) ? docs : []).map(d =>
                `<option value="${d.id}">${esc((d.bemerkung || '').trim() || d.filenameOriginal || ('Dokument #' + d.id))}${d.geaendertAm || d.hochgeladenAm ? ' (' + new Date(d.geaendertAm || d.hochgeladenAm).toLocaleDateString('de-CH') + ')' : ''}</option>`).join('');
    } catch {}
}

async function vwSave() {
    const alertEl = document.getElementById('vwAlert');
    const btn = document.getElementById('vwSaveBtn');
    const showErr = msg => alertEl.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:12px;margin-bottom:12px">${msg}</div>`;

    const gruende = [...document.querySelectorAll('.vwGrund:checked')].map(c => c.value);
    const beschreibung = document.getElementById('vwBeschreibung').value.trim();
    if (gruende.length === 0 && !beschreibung) { showErr('Mindestens einen Grund ankreuzen oder eine Beschreibung erfassen.'); return; }

    const edit = _vwEditId ? _vwList.find(v => v.id === _vwEditId) : null;
    const mode = document.querySelector('input[name="vwDocMode"]:checked')?.value;
    const file = document.getElementById('vwFile')?.files?.[0];
    const existingId = parseInt(document.getElementById('vwExistingDoc')?.value) || null;

    let dokumentId = edit?.dokumentId || null;
    btn.disabled = true; btn.textContent = '⏳ speichere…';
    try {
        // 1) Dokument beschaffen (Pflicht bei Neuerfassung)
        if (mode === 'upload' && file) {
            const branch = (typeof allBranches !== 'undefined' ? allBranches : [])?.find(b => b.id === fixedCompanyProfileId);
            const branchCode = branch?.restaurantCode || '';
            if (!branchCode) { showErr('Filiale nicht gewählt — bitte zuerst links eine Filiale wählen.'); return; }
            const typR = await fetch('/api/verwarnungen/dokument-typ', { headers: ah() });
            if (!typR.ok) { showErr('Dokument-Typ «Abmahnung» (Mitarbeiterentwicklung) konnte nicht ermittelt werden.'); return; }
            const typ = await typR.json();
            const stufe = document.getElementById('vwStufe')?.value || 'VERWARNUNG_1';
            const stufeLbl = ({
                VERWARNUNG_1: '1. Verwarnung',
                VERWARNUNG_2: '2. Verwarnung',
                LETZTE: 'Letzte Verwarnung'
            })[stufe] || 'Verwarnung';
            const datumIso = document.getElementById('vwDatum')?.value || '';
            const datumLbl = datumIso
                ? `${datumIso.slice(8, 10)}.${datumIso.slice(5, 7)}.${datumIso.slice(0, 4)}`
                : '';
            // Walter 28.07.2026: Beschreibung = Stufe (+ Datum), landet in Abmahnung.
            const bemerkung = datumLbl ? `${stufeLbl} vom ${datumLbl}` : stufeLbl;
            const safeName = bemerkung.replace(/[\\/:*?"<>|]+/g, '').trim() || 'Verwarnung';
            const ext = (file.name && file.name.includes('.'))
                ? file.name.slice(file.name.lastIndexOf('.'))
                : '.pdf';
            const fd = new FormData();
            fd.append('file', file, `${safeName}${ext}`);
            fd.append('employeeId', selectedEmployeeId);
            fd.append('dokumentTypId', typ.id);
            fd.append('branchCode', branchCode);
            fd.append('bemerkung', bemerkung);
            // WICHTIG: nur Authorization — ah() setzt Content-Type json und wuerde multipart brechen.
            const up = await fetch('/api/documents/upload', { method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd });
            if (up.status === 409) {
                const dup = await up.json().catch(() => ({}));
                dokumentId = dup.duplicateId || dokumentId;   // gleiche Datei schon da → verknüpfen
            } else if (!up.ok) {
                showErr('Upload fehlgeschlagen: ' + (await up.text()).slice(0, 200)); return;
            } else {
                const doc = await up.json();
                dokumentId = doc.id || doc.Id || dokumentId;
            }
        } else if (mode === 'existing' && existingId) {
            dokumentId = existingId;
        }
        // Dokument optional (Walter 15.07.2026): Formular-Workflow — Scan wird
        // nach der Unterschrift über ✎ Bearbeiten nachgereicht («Schreiben fehlt»).

        // 2) Verwarnung speichern
        const body = JSON.stringify({
            datum: document.getElementById('vwDatum').value || null,
            stufe: document.getElementById('vwStufe').value,
            gruende, beschreibung, dokumentId
        });
        const url = _vwEditId ? `/api/verwarnungen/${_vwEditId}` : `/api/verwarnungen/${selectedEmployeeId}`;
        const r = await fetch(url, {
            method: _vwEditId ? 'PUT' : 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body
        });
        if (!r.ok) {
            const err = await r.json().catch(() => ({}));
            showErr(err.message || err.error || ('HTTP ' + r.status)); return;
        }
        document.getElementById('vwModal')?.remove();
        loadVerwarnungenTab(selectedEmployeeId);
    } catch (e) {
        showErr('Netzwerkfehler: ' + e.message);
    } finally {
        btn.disabled = false; btn.textContent = 'Speichern';
    }
}

// Formular-PDF aus den aktuellen Modal-Feldern (speichert nichts).
async function vwFormularPdf() {
    const gruende = [...document.querySelectorAll('.vwGrund:checked')].map(c => c.value);
    const body = JSON.stringify({
        datum: document.getElementById('vwDatum')?.value || null,
        stufe: document.getElementById('vwStufe')?.value || 'VERWARNUNG_1',
        gruende,
        beschreibung: document.getElementById('vwBeschreibung')?.value.trim() || null
    });
    await _vwFetchFormular(body);
}

// Formular-PDF aus einer bestehenden Verwarnungs-Zeile (z.B. Nachdruck).
async function vwPrintFormular(id) {
    const v = _vwList.find(x => x.id === id);
    if (!v) return;
    const body = JSON.stringify({
        datum: v.datum ? String(v.datum).slice(0, 10) : null,
        stufe: v.stufe,
        gruende: (v.gruende || '').split('\n').filter(Boolean),
        beschreibung: v.beschreibung || null
    });
    await _vwFetchFormular(body);
}

async function _vwFetchFormular(body) {
    try {
        const r = await fetch(`/api/verwarnungen/${selectedEmployeeId}/formular-pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body
        });
        if (!r.ok) {
            const err = await r.json().catch(() => ({}));
            alert(err.message || err.error || ('Formular fehlgeschlagen: HTTP ' + r.status));
            return;
        }
        const blob = await r.blob();
        const cd = r.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename="?([^"]+)"?/);
        await previewFileModal(blob, m ? m[1] : 'Verwarnung.pdf');
    } catch (e) { alert('Netzwerkfehler: ' + e.message); }
}

// Echtes Löschen (Walter-Entscheid 15.07.2026) — kein Storno-Behalten mehr.
async function vwDelete(id) {
    if (!(await liquidConfirm('Verwarnung endgültig löschen? Das hinterlegte Dokument bleibt in der Personalakte.'))) return;
    try {
        const r = await fetch(`/api/verwarnungen/${id}`, { method: 'DELETE', headers: ah() });
        if (!r.ok) { alert('Löschen fehlgeschlagen (' + r.status + ')'); return; }
        loadVerwarnungenTab(selectedEmployeeId);
    } catch (e) { alert('Netzwerkfehler: ' + e.message); }
}

// ══════════════════════════════════════════════════════════════════════
//  UMZUG BESTÄTIGEN (Walter-Vorgabe 07./08.08.2026)
//  Adressen kommen AUSSCHLIESSLICH aus easy@work. Dieser Dialog bestätigt
//  nur das UMZUGSDATUM zum offenen Adresswechsel (Pending aus dem Sync).
//  Bei Kantonswechsel versioniert das Backend die QST automatisch:
//  alter Kanton bis Ende Umzugsmonat, neuer ab 1. des Folgemonats.
//  Admin kann Historie-Einträge korrigieren: Datum ändern / löschen.
// ══════════════════════════════════════════════════════════════════════
let _umzugEmpId = null;
let _umzugHist = [];

function _umzugEnsureModal() {
    if (document.getElementById('umzugModal')) return;
    const inp = 'width:100%;margin-top:3px;padding:7px 10px;border:1px solid rgba(60,55,48,0.18);border-radius:8px;font-size:13px;background:#fff;box-sizing:border-box;font-family:inherit;color:#3f3f3f';
    const lbl = 'display:block;font-size:11.5px;font-weight:600;color:#8b8b8b';
    const div = document.createElement('div');
    div.id = 'umzugModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;z-index:320;background:rgba(40,36,30,0.38);backdrop-filter:blur(2px)';
    div.innerHTML = `
    <div style="position:absolute;top:50%;left:50%;transform:translate(-50%,-50%);width:min(560px,94vw);max-height:92vh;overflow:auto;background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 25px 60px rgba(60,55,48,0.22);padding:22px 24px">
        <div style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:4px">🚚 Umzug bestätigen</div>
        <div id="umzugMaName" style="font-size:12px;color:#8b8b8b;margin-bottom:12px"></div>
        <div id="umzugPendingBox" style="margin-bottom:12px"></div>
        <div id="umzugFormBlock">
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 14px">
                <label style="${lbl}">Umzugsdatum<input id="umzugDatum" type="date" style="${inp}"></label>
                <label style="${lbl}">Bemerkung <span style="font-weight:400">(optional)</span><input id="umzugBem" style="${inp}"></label>
            </div>
            <div style="background:rgba(255,255,255,0.45);border:1px solid rgba(60,55,48,0.12);border-radius:10px;padding:8px 12px;margin-top:12px;font-size:11.5px;color:#646464">
                Bei einem <b>Kantonswechsel</b> wird die Quellensteuer automatisch versioniert:
                der angebrochene Monat zahlt noch im alten Kanton (Umzug am Monatsersten: neuer Kanton ab genau diesem Tag).
            </div>
        </div>
        <div id="umzugHistorie" style="margin-top:12px"></div>
        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:16px">
            <button onclick="document.getElementById('umzugModal').style.display='none'"
                    style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);color:#646464;border-radius:999px;padding:8px 18px;font-size:13px;font-weight:600;cursor:pointer">Schliessen</button>
            <button id="umzugSaveBtn" onclick="umzugSave()"
                    style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 20px;font-size:13px;font-weight:600;cursor:pointer;box-shadow:0 2px 8px rgba(60,55,48,0.2)">Datum bestätigen</button>
        </div>
    </div>`;
    div.addEventListener('click', (e) => { if (e.target === div) div.style.display = 'none'; });
    document.body.appendChild(div);
}

async function openUmzugModal(empId) {
    if (!empId) return;
    _umzugEnsureModal();
    _umzugEmpId = empId;
    const set = (id, v) => { const el = document.getElementById(id); if (el) el.value = v ?? ''; };
    ['umzugDatum', 'umzugBem'].forEach(id => set(id, ''));
    const nameEl = document.getElementById('umzugMaName');
    if (nameEl && typeof selectedEmployee !== 'undefined' && selectedEmployee) {
        nameEl.textContent = `${selectedEmployee.firstName || ''} ${selectedEmployee.lastName || ''} — aktuelle Adresse ${selectedEmployee.zipCode || ''} ${selectedEmployee.city || ''} (${selectedEmployee.cantonCode || '–'})`;
    }
    document.getElementById('umzugModal').style.display = 'block';
    await umzugLoadHistorie(empId);
}

async function umzugLoadHistorie(empId) {
    const el = document.getElementById('umzugHistorie');
    const box = document.getElementById('umzugPendingBox');
    const saveBtn = document.getElementById('umzugSaveBtn');
    if (!el) return;
    el.innerHTML = '';
    try {
        const res = await fetch(`/api/employees/${empId}/wohnort`, { headers: ah(), cache: 'no-store' });
        if (!res.ok) return;
        const list = await res.json();
        _umzugHist = list;
        const f = (iso) => iso ? new Date(iso).toLocaleDateString('de-CH') : null;
        const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
        const isAdmin = typeof currentUser !== 'undefined' && currentUser?.role === 'admin';

        // Offener easy@work-Wechsel: nur dann gibt es etwas zu bestätigen.
        const pending = list.find(h => h.datumOffen);
        if (box) {
            box.innerHTML = pending
                ? `<div style="background:#fffbeb;border:1px solid #fde68a;border-radius:10px;padding:10px 12px;font-size:12.5px;color:#92400e">
                    Neue Adresse aus easy@work: <b>${esc(pending.plz)} ${esc(pending.ort)} ${esc(pending.kantonCode)}</b><br>
                    <span style="font-size:11.5px">Bitte Umzugsdatum bestätigen — die Adresse selbst wird nur in easy@work gepflegt.</span></div>`
                : `<div style="background:rgba(255,255,255,0.45);border:1px solid rgba(60,55,48,0.12);border-radius:10px;padding:10px 12px;font-size:12.5px;color:#646464">
                    Kein offener Adresswechsel. Adressänderungen zuerst in <b>easy@work</b> erfassen und den MA synchronisieren.</div>`;
        }
        if (saveBtn) saveBtn.style.display = pending ? '' : 'none';
        // Ohne offenen Wechsel gibt es nichts zu erfassen — Formular +
        // Regel-Hinweis komplett ausblenden (Walter 08.08.2026).
        const formBlock = document.getElementById('umzugFormBlock');
        if (formBlock) formBlock.style.display = pending ? '' : 'none';

        if (!list.length) return;
        el.innerHTML = `<div style="font-size:11.5px;font-weight:700;color:#8b8b8b;margin-bottom:4px">WOHNORT-HISTORIE</div>`
            + list.map(h => `<div id="umzugRow-${h.id}" style="display:flex;align-items:center;gap:8px;font-size:12px;color:#3f3f3f;padding:3px 0;border-bottom:1px solid rgba(60,55,48,0.08)">
                <div style="flex:1;min-width:0">
                    ${h.strasse ? esc(h.strasse) + ', ' : ''}${esc(h.plz)} ${esc(h.ort)} <b>${esc(h.kantonCode)}</b>
                    <span style="color:#8b8b8b">· ${h.gueltigAb ? 'ab ' + f(h.gueltigAb) : 'seit jeher'}${h.gueltigBis ? ' bis ' + f(h.gueltigBis) : ''}</span>
                    ${h.datumOffen ? '<span style="margin-left:6px;font-size:10.5px;font-weight:700;color:#b45309;border:1px solid #fde68a;background:#fffbeb;border-radius:6px;padding:1px 6px">Datum offen</span>' : ''}
                </div>
                ${isAdmin ? `
                <button onclick="umzugEntryEdit(${h.id})" title="Gültig-ab-Datum korrigieren"
                        style="flex-shrink:0;background:#fff;border:1px solid #cbd5e1;color:#475569;border-radius:6px;padding:3px 8px;font-size:11px;cursor:pointer">📅 Datum</button>
                <button onclick="umzugEntryDelete(${h.id})" title="Eintrag löschen"
                        style="flex-shrink:0;background:#fff;border:1px dashed #fca5a5;color:#991b1b;border-radius:6px;padding:3px 8px;font-size:11px;cursor:pointer">🗑</button>` : ''}
            </div>`).join('');
    } catch (_) { /* Historie optional */ }
}

async function umzugSave() {
    const val = (id) => document.getElementById(id)?.value?.trim() || null;
    const datum = val('umzugDatum');
    if (!datum) { showToast('Bitte Umzugsdatum wählen.', 'error'); return; }
    const btn = document.getElementById('umzugSaveBtn');
    if (btn) btn.disabled = true;
    try {
        const res = await fetch(`/api/employees/${_umzugEmpId}/wohnort/umzug`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ umzugsdatum: datum, bemerkung: val('umzugBem') }),
        });
        if (typeof lohnEditLock !== 'undefined' && await lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            let msg = 'Bestätigen fehlgeschlagen.';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch (_) {}
            showToast(msg, 'error');
            return;
        }
        const d = await res.json();
        document.getElementById('umzugModal').style.display = 'none';
        showToast('Umzugsdatum bestätigt.' + (d.qstInfo ? ' ' + d.qstInfo : ''), 'success');
        if (typeof selectEmployee === 'function' && _umzugEmpId) selectEmployee(_umzugEmpId);
    } catch (_) {
        showToast('Verbindungsfehler beim Speichern.', 'error');
    } finally {
        if (btn) btn.disabled = false;
    }
}

// ── Admin-Korrekturen (Walter 08.08.2026): NUR Datum ändern + löschen —
//    Adresse ist easy@work-Sache und hier nie editierbar. ────────────────
function umzugEntryEdit(id) {
    const h = _umzugHist.find(x => x.id === id);
    const row = document.getElementById('umzugRow-' + id);
    if (!h || !row) return;
    const inp = 'padding:4px 7px;border:1px solid rgba(60,55,48,0.18);border-radius:7px;font-size:12px;background:#fff;color:#3f3f3f';
    row.innerHTML = `
        <span style="flex:1;min-width:0">${String(h.plz || '')} ${String(h.ort || '')} <b>${String(h.kantonCode || '')}</b></span>
        <input id="uzE-ab-${id}" type="date" value="${h.gueltigAb || ''}" title="leer = seit jeher" style="${inp}">
        <button onclick="umzugEntrySave(${id})" style="background:#3f3f3f;color:#fff;border:none;border-radius:8px;padding:4px 10px;font-size:11.5px;font-weight:600;cursor:pointer">✓</button>
        <button onclick="umzugLoadHistorie(_umzugEmpId)" style="background:transparent;border:1px solid rgba(60,55,48,0.18);color:#646464;border-radius:8px;padding:4px 9px;font-size:11.5px;cursor:pointer">✕</button>`;
}

async function umzugEntrySave(id) {
    const abVal = document.getElementById('uzE-ab-' + id)?.value?.trim() ?? '';
    try {
        const res = await fetch(`/api/employees/${_umzugEmpId}/wohnort/${id}`, {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                gueltigAb: abVal,                        // '' = seit jeher
                datumOffen: abVal ? false : undefined,   // Datum gesetzt = bestätigt
            }),
        });
        if (!res.ok) { showToast('Speichern fehlgeschlagen.', 'error'); return; }
        showToast('Datum angepasst.', 'success');
        umzugLoadHistorie(_umzugEmpId);
    } catch (_) { showToast('Verbindungsfehler.', 'error'); }
}

async function umzugEntryDelete(id) {
    const h = _umzugHist.find(x => x.id === id);
    const ok = await liquidConfirm(
        `Historie-Eintrag «${h?.plz || ''} ${h?.ort || ''}» löschen? (reine Datenkorrektur — QST-Versionen bleiben unberührt)`,
        { title: 'Eintrag löschen', yesLabel: 'Löschen', noLabel: 'Abbrechen' });
    if (!ok) return;
    try {
        const res = await fetch(`/api/employees/${_umzugEmpId}/wohnort/${id}`, {
            method: 'DELETE', headers: ah(),
        });
        if (!res.ok) { showToast('Löschen fehlgeschlagen.', 'error'); return; }
        showToast('Eintrag gelöscht.', 'success');
        umzugLoadHistorie(_umzugEmpId);
    } catch (_) { showToast('Verbindungsfehler.', 'error'); }
}
