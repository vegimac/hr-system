// ══════════════════════════════════════════════════════════════════════
// employee-lookup-cache.js — Walter-Vorgabe 14.06.2026
// ──────────────────────────────────────────────────────────────────────
// Globaler clientseitiger Cache für die MA-Picker-Liste.
//
// Hintergrund: viele Frontend-Module (Posteingang, HR-Lohnausweis/QST/
// RAV, d.velop-Importer, Dokumente-Tab, Verträge-Liste …) brauchen nur
// eine kurze MA-Liste mit { id, name, employeeNumber, isActive,
// isPayrollExcluded, employments } für Dropdowns. Vorher ruft jedes
// Modul beim Öffnen `/api/employees` auf — das ist der schwere Endpoint
// mit komplettem Include-Graph (Vertrag×JobGroup×…) und liefert ein
// Vielfaches der nötigen Daten. Pro Wechsel auf Posteingang/HR/etc.
// also unnötiger Roundtrip mit grossem Payload.
//
// Neuer Endpoint: `GET /api/employees/lookup-full` (im EmployeesController).
// Projektion + AsNoTracking, nur die genannten Felder.
//
// Cache-Strategie:
//   • Modul-Variable `_employeeLookupCache` hält die letzte Liste.
//   • `loadEmployeeLookup()` liefert den Cache; wenn leer → 1× fetch.
//   • TTL 60 s — länger ist gefährlich (MA neu importiert / inaktiv
//     gesetzt sieht man sonst nicht), kürzer macht den Cache sinnlos.
//   • `invalidateEmployeeLookupCache()` leert ihn sofort — wird von
//     den MA-/Vertrags-Mutationspfaden gerufen (saveEmployee, vtSave,
//     confirmDeleteEmployee, Stammdaten-Importer-Commit, etc.).
//   • In-flight-Dedupe: rufen mehrere Module gleichzeitig — nur EIN
//     Server-Roundtrip, alle warten auf dieselbe Promise.
// ══════════════════════════════════════════════════════════════════════

let _employeeLookupCache    = null;
let _employeeLookupLoadedAt = 0;
let _employeeLookupInFlight = null;
const _EMP_LOOKUP_TTL_MS    = 60 * 1000;

/**
 * Liefert die MA-Liste für Picker (siehe Endpoint-Doku oben). Bei
 * Cache-Treffer ohne Roundtrip, sonst 1× fetch und cachen. Wirft KEINEN
 * Fehler — bei Netzproblemen Promise mit `[]` resolved (Aufrufer-UI
 * zeigt einfach leere Liste statt zu crashen).
 */
async function loadEmployeeLookup() {
    const now = Date.now();
    if (_employeeLookupCache && (now - _employeeLookupLoadedAt) < _EMP_LOOKUP_TTL_MS) {
        return _employeeLookupCache;
    }
    if (_employeeLookupInFlight) return _employeeLookupInFlight;

    _employeeLookupInFlight = (async () => {
        try {
            const r = await fetch('/api/employees/lookup-full', { headers: ah() });
            if (!r.ok) {
                console.warn('[employee-lookup-cache] HTTP', r.status);
                return _employeeLookupCache || [];
            }
            _employeeLookupCache    = await r.json();
            _employeeLookupLoadedAt = Date.now();
            return _employeeLookupCache;
        } catch (err) {
            console.warn('[employee-lookup-cache] fetch failed:', err);
            return _employeeLookupCache || [];
        } finally {
            _employeeLookupInFlight = null;
        }
    })();
    return _employeeLookupInFlight;
}

/**
 * Cache leeren — nach jeder MA-/Vertragsänderung aufrufen, damit der
 * nächste Picker-Open frische Daten holt. Mehrfach-Calls schaden nicht
 * (zweite Leerung ist no-op).
 */
function invalidateEmployeeLookupCache() {
    _employeeLookupCache    = null;
    _employeeLookupLoadedAt = 0;
}

// Globale Exposition (window.* — keine Module/Imports im Projekt).
window.loadEmployeeLookup            = loadEmployeeLookup;
window.invalidateEmployeeLookupCache = invalidateEmployeeLookupCache;
