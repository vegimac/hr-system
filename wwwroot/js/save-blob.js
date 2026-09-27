// ════════════════════════════════════════════════════════════════════════
//  save-blob.js — kanonischer Download-Helfer (Walter-Vorgabe 21.05.2026)
//
//  REGEL: NICHTS wird mehr still / automatisch heruntergeladen. JEDER Download
//  im ganzen Programm läuft über saveBlobAsk() bzw. saveUrlAsk() und öffnet
//  damit den nativen „Speichern unter…"-Dialog (File System Access API,
//  Chrome/Edge). Fehlt die API (Firefox/Safari) oder bricht der User ab, gibt
//  es einen sauberen Fallback bzw. passiert nichts.
//
//  Diese Datei wird als ERSTES Modul-Script in index.html UND in import.html
//  geladen, damit beide Helfer global überall verfügbar sind.
// ════════════════════════════════════════════════════════════════════════

// Speichert ein Blob über den nativen „Speichern unter…"-Dialog. Zielordner
// wählt der User. Bricht er ab, passiert nichts. Fällt auf den klassischen
// Anker-Download zurück, wenn showSaveFilePicker fehlt.
// Dateiname aus dem Content-Disposition-Header (Walter-Bug 31.08.2026:
// «Unbefristet 70%.pdf» → «Download fehlgeschlagen: URI malformed»).
//
// Der Server schickt nach RFC 5987 ZWEI Angaben: filename="…" als ASCII-
// Fallback und filename*=UTF-8''… prozentkodiert. Nur die zweite darf
// dekodiert werden. Wer decodeURIComponent auf die ERSTE anwendet, fliegt bei
// jedem echten Prozentzeichen im Namen auf die Nase — «70%.pdf» sieht für den
// Dekoder aus wie eine kaputte Escape-Sequenz.
//
// Reihenfolge: filename* (dekodiert, mit Netz) → filename (roh, NICHT
// dekodiert) → Fallback.
function cdFilename(cd, fallback) {
    cd = cd || '';
    const star = /filename\*=(?:UTF-8'')?([^;]+)/i.exec(cd);
    if (star) {
        const roh = star[1].trim().replace(/^["']|["']$/g, '');
        try { return decodeURIComponent(roh); } catch (_) { return roh; }
    }
    const plain = /filename="?([^";]+)"?/i.exec(cd);
    if (plain) return plain[1].trim();
    return fallback;
}
window.cdFilename = cdFilename;

// Rueckgabe (Walter 27.09.2026): 'gespeichert' | 'abgebrochen' | 'fallback'.
// Vorher lief jeder Ausgang stumm — wer abbrach und wer an einem blockierten
// Dialog haengenblieb, sah beides nichts. Alte Aufrufer ignorieren den Wert
// einfach weiter; wer will, kann es dem Benutzer sagen.
async function saveBlobAsk(blob, filename) {
    let grund = '';
    if (window.showSaveFilePicker) {
        try {
            const ext  = (String(filename).match(/\.[^.]+$/) || [''])[0].toLowerCase();
            const mime = blob.type || (ext === '.pdf' ? 'application/pdf'
                                     : ext === '.xml' ? 'application/xml'
                                     : ext === '.csv' ? 'text/csv'
                                     : 'application/octet-stream');
            const opts = { suggestedName: filename };
            if (ext) opts.types = [{ description: ext.slice(1).toUpperCase() + '-Datei', accept: { [mime]: [ext] } }];
            const handle = await window.showSaveFilePicker(opts);
            const w = await handle.createWritable();
            await w.write(blob);
            await w.close();
            return 'gespeichert';
        } catch (e) {
            if (e && e.name === 'AbortError') return 'abgebrochen';   // User hat abgebrochen
            // sonst: klassischer Download als Fallback
            grund = (e && (e.name || e.message)) ? `${e.name || ''} ${e.message || ''}`.trim() : '';
        }
    }
    // Ohne Picker: klassischer Anker-Download. Walter-Bug 27.09.2026 — im ELM-Bereich
    // kam «Failed to execute 'insertAdjacentHTML' on 'Element': … invalid XML». Der
    // Aufrufer war keiner von uns: eine Browser-Erweiterung haengt sich an
    // document.body und stolpert ueber den eingehaengten Anker. Also haengen wir
    // ihn gar nicht mehr ein — ein Klick funktioniert auch ohne. Jede Stufe hat ihr
    // eigenes Netz, damit ein einziger fremder Fehler nicht den Download verhindert.
    const objUrl = URL.createObjectURL(blob);
    const aufraeumen = () => setTimeout(() => URL.revokeObjectURL(objUrl), 15000);

    const anker = (einhaengen) => {
        const a = document.createElement('a');
        a.href = objUrl;
        a.download = filename;
        a.rel = 'noopener';
        if (einhaengen) { a.style.display = 'none'; document.body.appendChild(a); }
        try { a.click(); } finally { if (einhaengen) a.remove(); }
    };

    try { anker(false); aufraeumen(); window._saveBlobGrund = grund; return 'fallback'; }
    catch (e1) { grund = grund || `${e1.name || ''} ${e1.message || ''}`.trim(); }

    try { anker(true); aufraeumen(); window._saveBlobGrund = grund; return 'fallback'; }
    catch (e2) { grund = grund || `${e2.name || ''} ${e2.message || ''}`.trim(); }

    // Letzte Stufe: Datei in einem neuen Tab oeffnen — von dort mit Cmd+S sichern.
    const fenster = window.open(objUrl, '_blank');
    aufraeumen();
    window._saveBlobGrund = grund;
    if (!fenster) throw new Error(
        'Der Browser hat den Download blockiert' + (grund ? ` (${grund})` : '')
        + '. Bitte Pop-ups fuer diese Seite erlauben oder eine Erweiterung abschalten.');
    return 'neuer-tab';
}

// Wie saveBlobAsk, aber für eine bereits erzeugte (Blob-)URL — typisch für
// Modals, die das PDF im <iframe> vorschauen (URL liegt vor) und einen
// separaten „Herunterladen"-Button haben. Holt das Blob aus der Object-URL
// zurück und reicht es an saveBlobAsk durch.
async function saveUrlAsk(blobUrl, filename) {
    if (!blobUrl) return;
    try {
        const blob = await fetch(blobUrl).then(r => r.blob());
        await saveBlobAsk(blob, filename);
    } catch (e) {
        // Fallback: klassischer Anker-Download direkt auf die URL
        const a = document.createElement('a');
        a.href = blobUrl; a.download = filename;
        document.body.appendChild(a); a.click(); a.remove();
    }
}
