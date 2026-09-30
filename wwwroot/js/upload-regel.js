// ── Nur PDF + Bilder hochladen (Walter-Vorgabe 01.10.2026) ─────────────
// Gegenstück zu Services/DokumentUploadRegel.cs. Word/Excel/PowerPoint wird
// abgelehnt mit dem Hinweis «bitte als PDF umwandeln» — OneCrew bietet die
// Umwandlung gleich an (POST /api/documents/in-pdf-umwandeln). ZIP & Co.
// lassen sich nicht umwandeln und werden nur abgelehnt.

const UPLOAD_ERLAUBT = ['.pdf', '.jpg', '.jpeg', '.png', '.gif', '.tif', '.tiff', '.heic', '.heif', '.webp'];
const UPLOAD_UMWANDELBAR = ['.doc', '.docx', '.odt', '.rtf', '.xls', '.xlsx', '.ods', '.ppt', '.pptx', '.odp'];
// Word/Excel bleiben im Dateiauswahl-Dialog wählbar, damit der Hinweis samt
// Umwandlungs-Angebot erscheint (sonst wären sie still ausgegraut).
const UPLOAD_ACCEPT = UPLOAD_ERLAUBT.concat(UPLOAD_UMWANDELBAR).join(',');
const UPLOAD_HINWEIS = 'Word-, Excel- und ZIP-Dateien werden nicht mehr gespeichert. Bitte als PDF umwandeln und speichern.';

function uploadEndung(name) {
    const m = String(name || '').toLowerCase().match(/\.[^.]+$/);
    return m ? m[0] : '';
}

function uploadIstErlaubt(name) {
    return UPLOAD_ERLAUBT.includes(uploadEndung(name));
}

async function uploadInPdfUmwandeln(file) {
    const fd = new FormData();
    fd.append('file', file, file.name);
    const r = await fetch('/api/documents/in-pdf-umwandeln', {
        method: 'POST',
        headers: { 'Authorization': `Bearer ${authToken}` },
        body: fd,
    });
    if (!r.ok) {
        let msg = 'HTTP ' + r.status;
        try { const j = await r.json(); msg = j.message || j.error || msg; } catch {}
        throw new Error(msg);
    }
    const blob = await r.blob();
    const name = file.name.replace(/\.[^.]+$/, '') + '.pdf';
    return new File([blob], name, { type: 'application/pdf' });
}

// Prüft mehrere Dateien auf einmal (eine einzige Rückfrage). Liefert die
// Liste, die hochgeladen werden darf: erlaubte Dateien unverändert, Word/
// Excel als umgewandeltes PDF (wenn gewünscht), der Rest fällt weg.
async function uploadDateienPruefen(fileList) {
    const alle = Array.from(fileList || []);
    const umwandelbar = alle.filter(f => UPLOAD_UMWANDELBAR.includes(uploadEndung(f.name)));
    const gesperrt = alle.filter(f => !uploadIstErlaubt(f.name) && !UPLOAD_UMWANDELBAR.includes(uploadEndung(f.name)));
    if (!umwandelbar.length && !gesperrt.length) return alle;

    const namen = arr => arr.map(f => '• ' + f.name).join('\n');
    let text = UPLOAD_HINWEIS + '\n';
    if (gesperrt.length) text += '\nNicht möglich (bitte selbst als PDF speichern):\n' + namen(gesperrt) + '\n';
    let umwandeln = false;
    if (umwandelbar.length) {
        text += '\nOneCrew kann diese Datei' + (umwandelbar.length > 1 ? 'en' : '') + ' jetzt in ein PDF umwandeln:\n' + namen(umwandelbar);
        umwandeln = await liquidConfirm(text, {
            title: 'Bitte als PDF speichern',
            yesLabel: 'In PDF umwandeln',
            noLabel: 'Abbrechen',
            zIndex: 10050,
        });
    } else {
        await liquidConfirm(text, { title: 'Bitte als PDF speichern', yesLabel: 'OK', hideNo: true, zIndex: 10050 });
    }

    const ergebnis = [];
    for (const f of alle) {
        if (uploadIstErlaubt(f.name)) { ergebnis.push(f); continue; }
        if (!umwandeln || !UPLOAD_UMWANDELBAR.includes(uploadEndung(f.name))) continue;
        try {
            ergebnis.push(await uploadInPdfUmwandeln(f));
        } catch (e) {
            await liquidConfirm(`«${f.name}» konnte nicht umgewandelt werden: ${e.message}\n\nBitte selbst als PDF speichern und dann hochladen.`,
                { title: 'Umwandlung fehlgeschlagen', yesLabel: 'OK', hideNo: true, zIndex: 10050 });
        }
    }
    return ergebnis;
}

async function uploadDateiPruefen(file) {
    if (!file) return null;
    return (await uploadDateienPruefen([file]))[0] || null;
}

// Für <input type="file">: ersetzt die gewählten Dateien durch die geprüften
// (Word → PDF) bzw. leert das Feld. Bestehender Code, der input.files liest,
// bekommt so ohne Änderung das PDF.
async function uploadInputPruefen(input) {
    if (!input?.files?.length) return [];
    const geprueft = await uploadDateienPruefen(input.files);
    const dt = new DataTransfer();
    geprueft.forEach(f => dt.items.add(f));
    input.files = dt.files;
    return geprueft;
}
