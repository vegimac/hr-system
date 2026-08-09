/**
 * Scanbot — Posteingang-Anbindung (Kamera → PDF → Upload-Modal).
 * Der eigentliche Scanner-Kern liegt seit 09.08.2026 in js/scanbot-scan.js
 * (gemeinsam mit dem MA-Postfach); hier nur noch die Posteingang-Verdrahtung.
 * scanbot-scan.js muss VOR dieser Datei geladen sein.
 */
(function () {
    'use strict';

    function setBusy(html) {
        const el = document.getElementById('pbAlert');
        if (el) el.innerHTML = html || '';
    }

    function busyHtml(text) {
        return `<div style="padding:10px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;font-size:13px;color:#475569">${text}</div>`;
    }

    function attachFileToUpload(file) {
        const input = document.getElementById('pbFile');
        if (!input) throw new Error('Upload-Feld nicht gefunden.');
        const dt = new DataTransfer();
        dt.items.add(file);
        input.files = dt.files;
        if (typeof pbShowFile === 'function') pbShowFile(file);
    }

    /** Kamera-Scan starten → PDF erzeugen → bestehendes Upload-Modal öffnen. */
    async function pbScanDocument() {
        const currentVal = document.getElementById('pbBranchSelect')?.value;
        if (!currentVal) {
            alert('Bitte erst Postfach wählen.');
            return;
        }
        try {
            const file = await window.scanbotScanToPdf({
                busy: (stage) => {
                    if (stage === 'load')  setBusy(busyHtml('📷 Scanbot wird geladen… (Kamera + WASM, bitte warten)'));
                    else if (stage === 'ready') setBusy(busyHtml('📷 Scanner bereit — bitte Dokument fotografieren'));
                    else if (stage === 'pdf')   setBusy(busyHtml('📄 PDF wird erstellt…'));
                    else setBusy('');
                },
            });
            if (!file) { setBusy(''); return; }
            if (typeof pbOpenUpload !== 'function') {
                throw new Error('Upload-Dialog nicht verfügbar.');
            }
            await pbOpenUpload();
            attachFileToUpload(file);
        } catch (err) {
            console.error('pbScanDocument', err);
            const msg = (err && (err.message || err.toString())) || 'Unbekannter Fehler';
            const hintTxt = (typeof scanbotErrorHint === 'function') ? scanbotErrorHint(msg) : '';
            const hint = hintTxt ? `<br><span style="font-size:12px;color:#64748b">Hinweis: ${hintTxt}</span>` : '';
            setBusy(`<div style="padding:10px 12px;background:#fef2f2;color:#b91c1c;border:1px solid #fecaca;border-radius:8px;font-size:13px">Scan fehlgeschlagen: ${msg}${hint}</div>`);
        }
    }

    window.pbScanDocument = pbScanDocument;
    // Rückwärtskompatibler Alias (alte Konsolen-Anleitung).
    window.pbScanbotSetLicense = function (key) { return window.scanbotSetLicense(key); };
})();
