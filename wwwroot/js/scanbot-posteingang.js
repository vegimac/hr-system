/**
 * Scanbot Web Document Scanner — Posteingang (Kamera → PDF → Upload-Modal).
 *
 * Lizenz (Evaluation):
 *  - Ohne Key: Trial ~60 Sekunden pro Browser-Session (Scanbot-Default).
 *  - 7-Tage-Trial: https://scanbot.io/trial/ → «Web SDK» → Domain als App-ID
 *    (z.B. onecrew.ch bzw. test.hr-srgmbh.ch), Key dann:
 *      localStorage.setItem('scanbotLicenseKey', '<KEY>')
 *    oder window.SCANBOT_LICENSE_KEY vor dem Scan setzen.
 *  - Produktiv: Scanbot Web Document Scanner SDK (jährliche Flat), Domains + Feature
 *    Document Scanning; Key via localStorage oder später appsettings/Env.
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

    function setBusy(html) {
        const el = document.getElementById('pbAlert');
        if (el) el.innerHTML = html || '';
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

    function attachFileToUpload(file) {
        const input = document.getElementById('pbFile');
        if (!input) throw new Error('Upload-Feld nicht gefunden.');
        const dt = new DataTransfer();
        dt.items.add(file);
        input.files = dt.files;
        if (typeof pbShowFile === 'function') pbShowFile(file);
    }

    /**
     * Kamera-Scan starten → PDF erzeugen → bestehendes Upload-Modal öffnen (Datei vorausgefüllt).
     */
    async function pbScanDocument() {
        const currentVal = document.getElementById('pbBranchSelect')?.value;
        if (!currentVal) {
            alert('Bitte erst Postfach wählen.');
            return;
        }
        if (!window.isSecureContext) {
            alert('Kamera-Scan braucht eine sichere Verbindung (HTTPS oder localhost).');
            return;
        }

        try {
            setBusy('<div style="padding:10px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;font-size:13px;color:#475569">📷 Scanbot wird geladen… (Kamera + WASM, bitte warten)</div>');
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

            setBusy('<div style="padding:10px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;font-size:13px;color:#475569">📷 Scanner bereit — bitte Dokument fotografieren</div>');
            const result = await ScanbotSDK.UI.createDocumentScanner(config);
            if (!result || !result.document) {
                setBusy('');
                return;
            }

            setBusy('<div style="padding:10px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;font-size:13px;color:#475569">📄 PDF wird erstellt…</div>');
            const pdfConfig = new ScanbotSDK.Config.PdfConfiguration();
            const engine = await sdk.beginPdf(pdfConfig);
            await engine.addPages(result.document, false);
            const pdfBuf = await engine.complete();
            const file = new File([pdfBuf], pdfFileName(), { type: 'application/pdf' });

            setBusy('');
            if (typeof pbOpenUpload !== 'function') {
                throw new Error('Upload-Dialog nicht verfügbar.');
            }
            await pbOpenUpload();
            attachFileToUpload(file);
        } catch (err) {
            console.error('pbScanDocument', err);
            const msg = (err && (err.message || err.toString())) || 'Unbekannter Fehler';
            let hint = '';
            if (/license|trial|expired/i.test(msg)) {
                hint = '<br><span style="font-size:12px;color:#64748b">Hinweis: Ohne Key gilt nur ~60s Trial. 7-Tage-Key unter scanbot.io/trial (Web SDK, Domain) → <code>localStorage.setItem(\'scanbotLicenseKey\', \'…\')</code></span>';
            } else if (/enginePath|worker|WASM|Content Security|CSP/i.test(msg)) {
                hint = '<br><span style="font-size:12px;color:#64748b">Hinweis: Server muss WASM + Worker erlauben (CSP: wasm-unsafe-eval, worker-src blob). Nach Deploy einmal Hard-Reload (Cache leeren).</span>';
            }
            setBusy(`<div style="padding:10px 12px;background:#fef2f2;color:#b91c1c;border:1px solid #fecaca;border-radius:8px;font-size:13px">Scan fehlgeschlagen: ${msg}${hint}</div>`);
        }
    }

    window.pbScanDocument = pbScanDocument;
    window.pbScanbotSetLicense = function (key) {
        localStorage.setItem('scanbotLicenseKey', String(key || '').trim());
        _sdk = null;
        _initPromise = null;
        return 'OK — Key gespeichert. Beim nächsten Scan neu initialisiert.';
    };
})();
