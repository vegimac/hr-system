// ════════════════════════════════════════════════════════════════════
// Lohnlauf-Edit-Sperre — Frontend-Helper
// Walter-Vorgabe 17.05.2026 (Variante 2 — periodenbezogen)
// ────────────────────────────────────────────────────────────────────
// Globale Mini-API:
//
//   await lohnEditLock.loadState(branchId)
//       → {firstAllowedDate, reason} oder {firstAllowedDate:null, reason:null}
//
//   lohnEditLock.renderBanner(containerEl, state)
//       → setzt einen gelben Banner in containerEl (oder versteckt ihn)
//
//   lohnEditLock.applyToDateInput(inputEl, state)
//       → setzt input[type=date].min auf firstAllowedDate
//
//   lohnEditLock.isLockedResponse(response)
//       → erkennt 409 LOHN_EDIT_LOCKED, zeigt Toast, gibt true zurück
//
// Cache pro Branch (5 Sekunden) damit die GETs nicht jeden Klick auslösen.
// ════════════════════════════════════════════════════════════════════
(function () {
    const _cache = new Map(); // branchId → {state, ts}
    const TTL_MS = 5000;

    async function loadState(branchId) {
        if (!branchId) return { firstAllowedDate: null, reason: null };
        const hit = _cache.get(branchId);
        if (hit && (Date.now() - hit.ts) < TTL_MS) return hit.state;

        try {
            const r = await fetch(`/api/lohn-edit-lock/first-allowed-date?branchId=${branchId}`, {
                headers: { Authorization: 'Bearer ' + (localStorage.hrToken || '') },
                cache: 'no-store'
            });
            if (!r.ok) return { firstAllowedDate: null, reason: null };
            const data = await r.json();
            const state = {
                firstAllowedDate: data.firstAllowedDate || null,
                reason:           data.reason || null
            };
            _cache.set(branchId, { state, ts: Date.now() });
            return state;
        } catch (e) {
            console.warn('lohnEditLock.loadState failed:', e);
            return { firstAllowedDate: null, reason: null };
        }
    }

    function invalidateCache(branchId) {
        if (branchId) _cache.delete(branchId);
        else _cache.clear();
    }

    function _fmtDate(iso) {
        if (!iso || iso.length < 10) return '';
        return iso.slice(8, 10) + '.' + iso.slice(5, 7) + '.' + iso.slice(0, 4);
    }

    /**
     * Rendert oder versteckt einen Banner im gegebenen Container.
     * Container sollte ein leeres <div> sein, das oben in der Tab/Seite sitzt.
     */
    function renderBanner(container, state) {
        if (!container) return;
        if (!state || !state.firstAllowedDate) {
            container.innerHTML = '';
            container.style.display = 'none';
            return;
        }
        const firstFmt = _fmtDate(state.firstAllowedDate);
        const reason   = state.reason || `Lohnperioden vor ${firstFmt} sind in Verarbeitung oder abgeschlossen — Edits nur ab ${firstFmt}.`;
        container.style.display = '';
        container.innerHTML = `
          <div style="background:#fef3c7;border:1px solid #f59e0b;border-radius:8px;padding:10px 14px;margin:8px 0;font-size:13px;color:#78350f;display:flex;align-items:center;gap:10px;">
            <span style="font-size:18px;">🔒</span>
            <span><strong>Lohnlauf-Sperre:</strong> ${reason}</span>
          </div>`;
    }

    /**
     * Setzt input[type=date].min auf firstAllowedDate, damit der User
     * gar nicht erst ein gesperrtes Datum auswählen kann.
     */
    function applyToDateInput(inputEl, state) {
        if (!inputEl) return;
        if (state && state.firstAllowedDate) {
            inputEl.min = state.firstAllowedDate;
        } else {
            inputEl.removeAttribute('min');
        }
    }

    /**
     * Erkennt 409 LOHN_EDIT_LOCKED in einer Response. Bei Match: Toast zeigen
     * + true zurückgeben. Sonst false zurückgeben (Fehler weiterreichen).
     * Verwendung:
     *
     *   const r = await fetch(...);
     *   if (await lohnEditLock.handleResponse(r)) return; // war locked
     *   if (!r.ok) { ... normaler Fehler-Pfad ... }
     */
    async function handleResponse(response) {
        if (!response || response.status !== 409) return false;
        let body = {};
        try { body = await response.clone().json(); } catch (e) { return false; }
        if (body && body.error === 'LOHN_EDIT_LOCKED') {
            const msg = body.message || 'Lohnlauf-Sperre — Datum liegt in einer verarbeiteten Periode.';
            if (typeof showToast === 'function') {
                showToast(msg, 'error');
            } else {
                alert(msg);
            }
            return true;
        }
        return false;
    }

    window.lohnEditLock = {
        loadState,
        invalidateCache,
        renderBanner,
        applyToDateInput,
        handleResponse,
        fmtDate: _fmtDate
    };
})();
