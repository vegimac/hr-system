// ════════════════════════════════════════════════════════════════════════
//  pdf-progress.js — «PDF wird generiert …» (Walter-Vorgabe 01.09.2026)
//
//  Problem: Manche PDFs brauchen mehrere Sekunden (Lohnabrechnung, Verträge,
//  QST-Formular). In dieser Zeit passiert sichtbar NICHTS — der Anwender
//  weiss nicht, ob sein Klick angekommen ist, und klickt nochmals.
//
//  Lösung an EINER Stelle statt in vierzig Aufrufern: window.fetch wird
//  einmal umhüllt. Geht eine Anfrage an einen PDF-Endpunkt, erscheint die
//  Meldung; sobald die Antwort da ist, verschwindet sie wieder. Das gilt
//  damit automatisch auch für jedes PDF, das später dazukommt.
//
//  Zwei bewusste Feinheiten:
//   • VERZOEGERUNG: Die Meldung erscheint erst nach 350 ms. Ein schnelles
//     PDF soll nicht kurz aufblitzen — das wirkt unruhiger als gar nichts.
//   • ZAEHLER: Mehrere gleichzeitige PDFs (z.B. Sammeldruck) teilen sich
//     eine Meldung; sie verschwindet erst, wenn das letzte fertig ist.
//
//  Wird direkt nach save-blob.js geladen, also vor allem anderen.
// ════════════════════════════════════════════════════════════════════════
(function () {
    if (window._pdfProgressInstalled) return;
    window._pdfProgressInstalled = true;

    const VERZOEGERUNG_MS = 350;
    let offen = 0, timer = null;

    function box() {
        let el = document.getElementById('pdfProgressBox');
        if (!el) {
            el = document.createElement('div');
            el.id = 'pdfProgressBox';
            el.setAttribute('role', 'status');
            el.setAttribute('aria-live', 'polite');
            el.style.cssText = 'position:fixed;top:16px;left:50%;transform:translateX(-50%);'
                + 'display:none;align-items:center;gap:10px;padding:9px 18px;border-radius:10px;'
                + 'background:#334155;color:#fff;font-size:13px;font-weight:600;z-index:10000;'
                + 'box-shadow:0 6px 20px rgba(0,0,0,.22)';
            el.innerHTML = '<span id="pdfProgressSpin" style="display:inline-block;width:13px;height:13px;'
                + 'border:2px solid rgba(255,255,255,.35);border-top-color:#fff;border-radius:50%;'
                + 'animation:pdfProgressSpin .8s linear infinite"></span>'
                + '<span>PDF wird generiert …</span>';
            document.body.appendChild(el);
            if (!document.getElementById('pdfProgressStyle')) {
                const st = document.createElement('style');
                st.id = 'pdfProgressStyle';
                // Wer Bewegung reduziert haben will, bekommt die Meldung ohne
                // rotierenden Kreis — der Text allein sagt dasselbe.
                st.textContent = '@keyframes pdfProgressSpin{to{transform:rotate(360deg)}}'
                    + '@media (prefers-reduced-motion:reduce){#pdfProgressSpin{animation:none}}';
                document.head.appendChild(st);
            }
        }
        return el;
    }

    function anzeigen() { box().style.display = 'flex'; }
    function verbergen() { const el = document.getElementById('pdfProgressBox'); if (el) el.style.display = 'none'; }

    function start() {
        offen++;
        if (offen === 1 && !timer) timer = setTimeout(() => { timer = null; anzeigen(); }, VERZOEGERUNG_MS);
    }
    function ende() {
        offen = Math.max(0, offen - 1);
        if (offen === 0) {
            if (timer) { clearTimeout(timer); timer = null; }
            verbergen();
        }
    }

    // Erkennung am Pfad: alle PDF-Endpunkte im Programm enden auf «/pdf»,
    // enthalten «pdf» im Segment (z.B. «arztbrief-pdf», «/pdf/vorschau») oder
    // liefern eine .pdf-Datei aus. Bewusst KEINE Erkennung am Content-Type:
    // der ist erst da, wenn die Wartezeit schon vorbei ist.
    function istPdfAnfrage(url) {
        try {
            const pfad = String(url).split('?')[0].toLowerCase();
            return /(^|[\/\-_])pdf($|[\/\-_.])/.test(pfad) || pfad.endsWith('.pdf');
        } catch (_) { return false; }
    }

    const originalFetch = window.fetch;
    window.fetch = function (eingabe, optionen) {
        const url = (typeof eingabe === 'string') ? eingabe : (eingabe && eingabe.url) || '';
        if (!istPdfAnfrage(url)) return originalFetch.apply(this, arguments);
        start();
        return originalFetch.apply(this, arguments).finally(ende);
    };
})();
