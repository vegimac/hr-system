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

    /** Alle gebundenen Felder in root prüfen; false wenn eines fehlschlägt. Async wegen PLZ-Lookup. */
    async function felderPruefen(root) {
        const scope = root || document;
        let ok = true;
        const els = scope.querySelectorAll('[data-swiss-uid-bound="1"]');
        for (const el of els) {
            if (typeof el._swissUidPruefen !== 'function') continue;
            const r = el._swissUidPruefen();
            const gut = (r && typeof r.then === 'function') ? await r : r;
            if (!gut) ok = false;
        }
        return ok;
    }

    function _istCHLand(landEl) {
        if (!landEl) return true;
        const l = String(landEl.value || '').trim().toUpperCase();
        return !l || l === 'CH' || l === 'SCHWEIZ';
    }

    // ── PLZ / Ort / Kanton (Ortschaftsverzeichnis) ──────────────────────────
    const _plzCache = new Map();

    function _el(idOrEl) {
        if (!idOrEl) return null;
        return typeof idOrEl === 'string' ? document.getElementById(idOrEl) : idOrEl;
    }

    function _ortNorm(s) {
        return String(s || '').trim().toLowerCase()
            .normalize('NFD').replace(/[\u0300-\u036f]/g, '');
    }

    function _ortName(loc) {
        const n = loc.ortschaftsname || loc.gemeindename || '';
        return (typeof w.stripCityCantonSuffix === 'function') ? w.stripCityCantonSuffix(n) : n;
    }

    /** Exakter Ortschaftsname (nach Normierung) — «Luzer» ≠ «Luzern». */
    function _ortPasst(ort, loc) {
        const o = _ortNorm(ort);
        if (!o) return false;
        const namen = [_ortName(loc), loc.gemeindename, loc.ortschaftsname]
            .filter(Boolean)
            .map(n => _ortNorm((typeof w.stripCityCantonSuffix === 'function') ? w.stripCityCantonSuffix(n) : n));
        return namen.some(n => n === o);
    }

    async function _locsFuer(plz) {
        if (_plzCache.has(plz)) return _plzCache.get(plz);
        // Cache von employees.js mitnutzen, falls vorhanden
        if (w._plzLookupCache && w._plzLookupCache.has(plz)) {
            const v = w._plzLookupCache.get(plz);
            _plzCache.set(plz, v);
            return v;
        }
        try {
            const headers = (typeof w.ah === 'function') ? w.ah() : {};
            const res = await fetch(`/api/swiss-locations/by-plz?plz=${encodeURIComponent(plz)}`, { headers });
            if (!res.ok) return [];
            const locs = await res.json();
            _plzCache.set(plz, locs);
            if (w._plzLookupCache) w._plzLookupCache.set(plz, locs);
            return locs;
        } catch { return []; }
    }

    /**
     * Bindet PLZ-Lookup + Abgleich Ort↔PLZ↔Kanton beim Verlassen.
     * opts: { plz, ort, kanton?, bfs?, hint?, land?, pflicht? }
     * Strasse bleibt bewusst optional (Swissdec: alternativ Postfach).
     * land = Land-Feld-Id — bei Ausland keine CH-PLZ-Prüfung.
     */
    function plzAdresseBinden(opts) {
        const plzEl = _el(opts.plz);
        const ortEl = _el(opts.ort);
        if (!plzEl || !ortEl || plzEl.dataset.swissPlzBound === '1') return;
        plzEl.dataset.swissPlzBound = '1';
        const kantonEl = _el(opts.kanton);
        const bfsEl = _el(opts.bfs);
        const hintEl = _el(opts.hint);
        const landEl = _el(opts.land);
        const pflicht = !!opts.pflicht;

        if (!plzEl.getAttribute('maxlength')) plzEl.setAttribute('maxlength', '4');
        if (!plzEl.getAttribute('inputmode')) plzEl.setAttribute('inputmode', 'numeric');

        function _adrRandReset() {
            feldFehlerLoeschen(plzEl);
            feldFehlerLoeschen(ortEl);
            if (kantonEl) feldFehlerLoeschen(kantonEl);
            plzEl.classList.remove('feld-ungueltig');
            ortEl.classList.remove('feld-ungueltig');
            if (kantonEl) kantonEl.classList.remove('feld-ungueltig');
        }

        /** Lange Adressfehler unter die ganze Zeile (hint), nicht in die schmale Kanton-Spalte. */
        function _adrFehler(ziel, msg) {
            _adrRandReset();
            if (!msg) return;
            if (ziel) {
                ziel.classList.add('feld-ungueltig');
                ziel.setAttribute('aria-invalid', 'true');
            }
            if (hintEl) {
                hintEl.dataset.swissAdrErr = '1';
                hintEl.style.color = '#b91c1c';
                hintEl.textContent = msg;
            } else if (ziel) {
                feldFehlerSetzen(ziel, msg);
            }
        }

        function _adrHintOk() {
            if (!hintEl || hintEl.dataset.swissAdrErr !== '1') return;
            delete hintEl.dataset.swissAdrErr;
            hintEl.style.color = '';
            hintEl.textContent = '';
        }

        async function lookup() {
            if (!_istCHLand(landEl)) {
                _adrRandReset();
                return true;
            }
            if (typeof w.validateZip === 'function') w.validateZip(plzEl);
            if (typeof w.plzLookupGeneric === 'function' && ortEl.id) {
                await w.plzLookupGeneric(
                    plzEl.value,
                    ortEl.id,
                    kantonEl && kantonEl.id ? kantonEl.id : null,
                    bfsEl && bfsEl.id ? bfsEl.id : null,
                    hintEl && hintEl.id ? hintEl.id : null);
            }
            return pruefen();
        }

        async function pruefen() {
            if (!_istCHLand(landEl)) {
                _adrRandReset();
                return true;
            }
            const plz = plzEl.value.trim();
            const ort = ortEl.value.trim();
            const kt = kantonEl ? (kantonEl.value || '').trim().toUpperCase() : '';

            if (!plz && !ort) {
                _adrRandReset();
                if (pflicht) {
                    _adrFehler(plzEl, 'PLZ und Ort sind Pflicht.');
                    if (ortEl) ortEl.classList.add('feld-ungueltig');
                    return false;
                }
                return true;
            }
            if (plz && !/^\d{4}$/.test(plz)) {
                _adrFehler(plzEl, 'PLZ muss 4 Ziffern haben.');
                return false;
            }
            if (!plz && ort) {
                _adrFehler(plzEl, 'PLZ fehlt zur Ortschaft.');
                return false;
            }

            const locs = await _locsFuer(plz);
            if (!locs.length) {
                _adrFehler(plzEl, `PLZ ${plz} nicht im Ortschaftsverzeichnis — Ort bitte prüfen.`);
                return false;
            }

            if (!ort) {
                if (pflicht) {
                    _adrFehler(ortEl, 'Ort ist Pflicht.');
                    return false;
                }
                _adrRandReset();
                return true;
            }
            if (!locs.some(l => _ortPasst(ort, l))) {
                const bsp = locs.slice(0, 3).map(l => _ortName(l)).filter(Boolean).join(', ');
                _adrFehler(ortEl, `Ort «${ort}» passt nicht zu PLZ ${plz}` + (bsp ? ` (z.B. ${bsp})` : '') + '.');
                return false;
            }

            if (kantonEl && kt) {
                const passend = locs.filter(l => _ortPasst(ort, l));
                if (passend.length && !passend.some(l => (l.kantonskuerzel || '').toUpperCase() === kt)) {
                    const soll = passend[0].kantonskuerzel || '?';
                    const ziel = kantonEl.tagName === 'SELECT' ? ortEl : kantonEl;
                    _adrFehler(ziel, `Kanton ${kt} passt nicht zu ${ort} / PLZ ${plz} (erwartet ${soll}).`);
                    return false;
                }
            }
            _adrRandReset();
            _adrHintOk();
            return true;
        }

        plzEl.addEventListener('input', () => {
            if (!_istCHLand(landEl)) return;
            if (typeof w.validateZip === 'function') w.validateZip(plzEl);
            if (plzEl.value.length === 4) lookup();
        });
        plzEl.addEventListener('blur', () => { lookup(); });
        ortEl.addEventListener('input', () => {
            if (typeof w.validateCity === 'function') w.validateCity(ortEl);
            // Grünes «✓ Ort» vom PLZ-Lookup gilt nicht mehr, sobald der Ort geändert wird
            if (hintEl && hintEl.dataset.swissAdrErr !== '1') {
                hintEl.innerHTML = '';
                hintEl.textContent = '';
                hintEl.style.color = '';
            }
            if (ortEl.classList.contains('feld-ungueltig')) pruefen();
        });
        ortEl.addEventListener('blur', () => { pruefen(); });
        if (kantonEl) {
            kantonEl.addEventListener('change', () => { pruefen(); });
            kantonEl.addEventListener('blur', () => { pruefen(); });
        }
        if (landEl) {
            landEl.addEventListener('change', () => { pruefen(); });
            landEl.addEventListener('blur', () => { pruefen(); });
        }

        plzEl._swissUidPruefen = () => pruefen();
        ortEl._swissUidPruefen = () => pruefen();
        plzEl.dataset.swissUidBound = '1';
        ortEl.dataset.swissUidBound = '1';
    }

    /** Bekannte PLZ/Ort-Paare + data-swiss-plz im DOM. Idempotent. */
    function plzAdresseUeberall(root) {
        const scope = root || document;
        const paare = [
            { plz: 'hsPlz', ort: 'hsOrt', kanton: 'hsKanton', hint: 'hsPlzHint' },
            { plz: 'stmZipCode', ort: 'stmCity', kanton: 'stmKantonCode', bfs: 'stmBfsGemeindeNr', hint: 'stmPlzHint' },
            { plz: 'cpEmpfPlz', ort: 'cpEmpfOrt', kanton: 'cpEmpfKanton', hint: 'cpEmpfPlzHint' },
            { plz: 'bePlz', ort: 'beOrt', hint: 'bePlzHint' },
            { plz: 'nbZip', ort: 'nbCity' },
            { plz: 'azPlz', ort: 'azOrt', hint: 'azPlzHint' },
            { plz: 'empAddr-zip', ort: 'empAddr-city', kanton: 'empAddr-canton', bfs: 'empAddr-bfs', hint: 'empAddr-plz-hint', land: 'empAddr-country' },
            { plz: 'abNeuPlz', ort: 'abNeuOrt', hint: 'abPlzHint' },
            { plz: 'waPlz', ort: 'waOrt', kanton: 'waKanton', hint: 'waPlzHint', land: 'waLand' },
            { plz: 'fmArbeitgeberPlz', ort: 'fmArbeitgeberOrt', kanton: 'fmArbeitgeberKanton', hint: 'fmAgPlzHint' },
            { plz: 'qstWagPlz', ort: 'qstWagOrt', kanton: 'qstWagKanton' },
            { plz: 'baKontoinhaberPlz', ort: 'baKontoinhaberOrt', land: 'baKontoinhaberLand' },
            { plz: 'suaPlz', ort: 'suaOrt' },
            { plz: 'ahvPlz', ort: 'ahvOrt' },
            { plz: 'ahvFirmaPlz', ort: 'ahvFirmaOrt' },
            { plz: 'uebSubPlz', ort: 'uebSubOrt' },
            { plz: 'ef-zip', ort: 'ef-city', kanton: 'ef-canton', bfs: 'ef-bfs', land: 'ef-country', hint: 'ef-plz-hint' },
        ];
        paare.forEach(p => {
            if (document.getElementById(p.plz) && document.getElementById(p.ort))
                plzAdresseBinden(p);
        });
        (scope.querySelectorAll ? scope : document).querySelectorAll('[data-swiss-plz]').forEach(plzEl => {
            plzAdresseBinden({
                plz: plzEl,
                ort: plzEl.getAttribute('data-swiss-ort'),
                kanton: plzEl.getAttribute('data-swiss-kanton'),
                bfs: plzEl.getAttribute('data-swiss-bfs'),
                hint: plzEl.getAttribute('data-swiss-hint'),
                land: plzEl.getAttribute('data-swiss-land'),
                pflicht: plzEl.getAttribute('data-swiss-pflicht') === '1',
            });
        });
    }

    w.SwissUid = {
        uidBfsGueltig, uidBfsFehler,
        burNummerGueltig, burNummerFehler,
        pflichtFehler,
        feldFehlerSetzen, feldFehlerLoeschen,
        feldPruefungBinden, felderPruefen,
        plzAdresseBinden, plzAdresseUeberall,
    };

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', () => plzAdresseUeberall());
    else
        setTimeout(() => plzAdresseUeberall(), 0);
})(window);
