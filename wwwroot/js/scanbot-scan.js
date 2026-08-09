/**
 * Scanbot Web SDK — gemeinsamer Kern (Walter-Vorgabe 09.08.2026).
 * Genutzt vom Posteingang (scanbot-posteingang.js) UND vom MA-Postfach
 * (postfach.html). Lädt das SDK lazy, öffnet den Dokument-Scanner und
 * liefert das Ergebnis als PDF-File zurück.
 *
 * Lizenz (Evaluation):
 *  - Ohne Key: Trial ~60 Sekunden pro Browser-Session (Scanbot-Default).
 *  - 7-Tage-Trial: https://scanbot.io/trial/ → «Web SDK» → Domain als App-ID,
 *    Key dann: localStorage.setItem('scanbotLicenseKey', '<KEY>')
 *  - Produktiv: Web Document Scanner SDK (jährliche Flat) — Key später
 *    zentral via appsettings/Env statt localStorage.
 */
(function () {
    'use strict';

    let _sdk = null;
    let _initPromise = null;
    let _scriptPromise = null;

    // Nicht «bin/» nennen — Root-.gitignore ignoriert **/bin/
    const ENGINE_PATH = '/lib/scanbot-web-sdk/engine/document-scanner/';
    const UI2_SRC = '/lib/scanbot-web-sdk/ScanbotSDK.ui2.min.js';

    function licenseKey() {
        try {
            return String(window.SCANBOT_LICENSE_KEY || localStorage.getItem('scanbotLicenseKey') || '').trim();
        } catch {
            return String(window.SCANBOT_LICENSE_KEY || '').trim();
        }
    }

    function loadUi2Script() {
        if (typeof ScanbotSDK !== 'undefined') return Promise.resolve();
        if (_scriptPromise) return _scriptPromise;
        _scriptPromise = new Promise((resolve, reject) => {
            const s = document.createElement('script');
            s.src = UI2_SRC;
            s.async = true;
            s.onload = () => resolve();
            s.onerror = () => {
                _scriptPromise = null;
                reject(new Error('Scanbot-Skript konnte nicht geladen werden.'));
            };
            document.head.appendChild(s);
        });
        return _scriptPromise;
    }

    async function ensureSdk() {
        if (_sdk) return _sdk;
        if (_initPromise) return _initPromise;
        _initPromise = (async () => {
            await loadUi2Script();
            if (typeof ScanbotSDK === 'undefined') {
                throw new Error('Scanbot SDK nicht verfügbar.');
            }
            _sdk = await ScanbotSDK.initialize({
                licenseKey: licenseKey(),
                enginePath: ENGINE_PATH,
            });
            return _sdk;
        })();
        try {
            return await _initPromise;
        } catch (e) {
            _initPromise = null;
            _sdk = null;
            throw e;
        }
    }

    function pdfFileName() {
        const d = new Date();
        const p = n => String(n).padStart(2, '0');
        return `Scan_${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}_${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}.pdf`;
    }

    /**
     * Kamera-Scan → PDF-File. opts.busy('load'|'ready'|'pdf'|'') meldet den
     * Fortschritt an den Aufrufer (der rendert selbst). Liefert null, wenn
     * der Benutzer abbricht. Wirft bei Fehlern (Lizenz, CSP, Kamera …).
     */
    async function scanbotScanToPdf(opts) {
        const busy = (opts && typeof opts.busy === 'function') ? opts.busy : function () {};
        if (!window.isSecureContext) {
            throw new Error('Kamera-Scan braucht eine sichere Verbindung (HTTPS oder localhost).');
        }
        busy('load');
        const sdk = await ensureSdk();

        const config = new ScanbotSDK.UI.Config.DocumentScanningFlow();
        try {
            // OneCrew-nah: ruhiges Kohle statt Scanbot-Rot
            config.palette = new ScanbotSDK.UI.Config.Palette({
                sbColorPrimary: '#3f3f3f',
                sbColorPrimaryDisabled: '#e5e5e5',
                sbColorOnPrimary: '#ffffff',
                sbColorSecondary: '#f1f5f9',
                sbColorOnSecondary: '#3f3f3f',
                sbColorSurface: '#ffffff',
                sbColorOnSurface: '#1a1a1a',
                sbColorOnSurfaceVariant: '#646464',
                sbColorOutline: '#e2e8f0',
                sbColorPositive: '#16a34a',
                sbColorNegative: '#dc2626',
                sbColorWarning: '#d97706',
                sbColorModalOverlay: '#000000A3',
            });
        } catch {
            /* Palette optional — SDK-Default bleibt ok */
        }

        busy('ready');
        const result = await ScanbotSDK.UI.createDocumentScanner(config);
        if (!result || !result.document) {
            busy('');
            return null;
        }

        busy('pdf');
        const pdfConfig = new ScanbotSDK.Config.PdfConfiguration();
        const engine = await sdk.beginPdf(pdfConfig);
        await engine.addPages(result.document, false);
        const pdfBuf = await engine.complete();
        busy('');
        return new File([pdfBuf], pdfFileName(), { type: 'application/pdf' });
    }

    /** Hilfetext zu bekannten Fehlerbildern (Lizenz abgelaufen, CSP). */
    function scanbotErrorHint(msg) {
        if (/license|trial|expired/i.test(msg)) {
            return 'Ohne Key gilt nur ~60s Trial. 7-Tage-Key unter scanbot.io/trial (Web SDK, Domain) → localStorage.setItem(\'scanbotLicenseKey\', \'…\')';
        }
        if (/enginePath|worker|WASM|Content Security|CSP/i.test(msg)) {
            return 'Server muss WASM + Worker erlauben (CSP: wasm-unsafe-eval, worker-src blob). Nach Deploy einmal Hard-Reload.';
        }
        return '';
    }

    window.scanbotScanToPdf = scanbotScanToPdf;
    window.scanbotErrorHint = scanbotErrorHint;
    window.scanbotSetLicense = function (key) {
        localStorage.setItem('scanbotLicenseKey', String(key || '').trim());
        _sdk = null;
        _initPromise = null;
        return 'OK — Key gespeichert. Beim nächsten Scan neu initialisiert.';
    };
})();
