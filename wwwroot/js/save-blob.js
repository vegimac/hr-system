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
async function saveBlobAsk(blob, filename) {
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
            return;
        } catch (e) {
            if (e && e.name === 'AbortError') return;   // User hat abgebrochen
            // sonst: klassischer Download als Fallback
        }
    }
    const objUrl = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = objUrl; a.download = filename;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(objUrl), 5000);
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
