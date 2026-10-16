// UID-BFS + BUR-Prüfziffer (Browser, spiegelt ElmStammdatenPruefung).
// Fehler sofort beim Verlassen des Feldes (blur), nicht erst beim Speichern.
(function (w) {
    'use strict';

    function uidBfsGueltig(uid) {
        const u = String(uid || '').trim().toUpperCase();
        if (!/^CHE-\d{3}\.\d{3}\.\d{3}$/.test(u)) return false;
        const z = u.replace(/\D/g, '');
        const gewicht = [5, 4, 3, 2, 7, 6, 5, 4];
        let summe = 0;
        for (let i = 0; i < 8; i++) summe += (z.charCodeAt(i) - 48) * gewicht[i];
        const rest = summe % 11;
        if (rest === 1) return false;
        const pruef = rest === 0 ? 0 : 11 - rest;
        return pruef === (z.charCodeAt(8) - 48);
    }

    function uidBfsFehler(uid) {
        const roh = String(uid || '').trim();
        if (!roh) return null;
        const u = roh.toUpperCase();
        if (!/^CHE-\d{3}\.\d{3}\.\d{3}$/.test(u))
            return 'UID bitte im Format CHE-XXX.XXX.XXX erfassen.';
        if (!uidBfsGueltig(u))
            return `UID «${u}» ist ungültig (Prüfziffer stimmt nicht).`;
        return null;
    }

    function burNummerGueltig(bur) {
        const b = String(bur || '').trim().toUpperCase();
        if (!/^[A-Z][0-9]{8}$/.test(b)) return false;
        const gewicht = [5, 4, 3, 2, 7, 6, 5];
        let summe = 0;
        for (let i = 0; i < 7; i++) summe += (b.charCodeAt(i + 1) - 48) * gewicht[i];
        const rest = summe % 11;
        if (rest === 1) return false;
        const pruef = rest === 0 ? 0 : 11 - rest;
        return pruef === (b.charCodeAt(8) - 48);
    }

    function burNummerFehler(bur) {
        const roh = String(bur || '').trim();
        if (!roh) return null;
        const b = roh.toUpperCase();
        if (burNummerGueltig(b)) return null;
        if (/^[A-Z][0-9]{8}$/.test(b))
            return `BUR-Nummer «${b}» ist ungültig (Prüfziffer stimmt nicht).`;
        return `BUR-Nummer «${b}» ist ungültig (Format: ein Buchstabe, acht Ziffern, z.B. A89058593).`;
    }

    function pflichtFehler(wert, label) {
        return String(wert || '').trim() ? null : `${label} ist Pflicht.`;
    }

    function _fehlerEl(input) {
        const id = input.id ? `feldFehler-${input.id}` : null;
        let el = id ? document.getElementById(id) : null;
        if (!el) {
            el = document.createElement('div');
            el.className = 'feld-fehler-text';
            if (id) el.id = id;
            // Nach dem Input, vor einem bestehenden f-hint (der bleibt als Hilfe)
            if (input.nextSibling && input.nextSibling.classList
                && input.nextSibling.classList.contains('f-hint'))
                input.parentNode.insertBefore(el, input.nextSibling);
            else if (input.parentNode)
                input.parentNode.insertBefore(el, input.nextSibling);
        }
        return el;
    }

    /** Zeigt oder löscht den Fehler am Feld. msg = null → ok. */
    function feldFehlerSetzen(input, msg) {
        if (!input) return;
        const el = _fehlerEl(input);
        if (msg) {
            input.classList.add('feld-ungueltig');
            input.setAttribute('aria-invalid', 'true');
            el.textContent = msg;
            el.style.display = 'block';
        } else {
            input.classList.remove('feld-ungueltig');
            input.removeAttribute('aria-invalid');
            el.textContent = '';
            el.style.display = 'none';
        }
    }

    function feldFehlerLoeschen(input) {
        feldFehlerSetzen(input, null);
    }

    /**
     * Bindet blur-Prüfung. opts:
     *   art: 'uid' | 'bur' | 'pflicht'
     *   label: Text für Pflichtfelder
     *   leerOk: true = leeres UID/BUR erlaubt (Default true)
     */
    function feldPruefungBinden(inputOrId, opts) {
        const input = typeof inputOrId === 'string' ? document.getElementById(inputOrId) : inputOrId;
        if (!input || input.dataset.swissUidBound === '1') return;
        input.dataset.swissUidBound = '1';
        const art = (opts && opts.art) || 'uid';
        const label = (opts && opts.label) || 'Feld';
        const pruefen = () => {
            const v = input.value;
            let msg = null;
            if (art === 'uid') msg = uidBfsFehler(v);
            else if (art === 'bur') msg = burNummerFehler(v);
            else if (art === 'pflicht') msg = pflichtFehler(v, label);
            feldFehlerSetzen(input, msg);
            return !msg;
        };
        input.addEventListener('blur', pruefen);
        input.addEventListener('input', () => {
            if (input.classList.contains('feld-ungueltig')) pruefen();
        });
        input._swissUidPruefen = pruefen;
    }

    /** Alle gebundenen Felder in root prüfen; false wenn eines fehlschlägt. */
    function felderPruefen(root) {
        const scope = root || document;
        let ok = true;
        scope.querySelectorAll('[data-swiss-uid-bound="1"]').forEach(el => {
            if (typeof el._swissUidPruefen === 'function' && !el._swissUidPruefen()) ok = false;
        });
        return ok;
    }

    w.SwissUid = {
        uidBfsGueltig, uidBfsFehler,
        burNummerGueltig, burNummerFehler,
        pflichtFehler,
        feldFehlerSetzen, feldFehlerLoeschen,
        feldPruefungBinden, felderPruefen,
    };
})(window);
