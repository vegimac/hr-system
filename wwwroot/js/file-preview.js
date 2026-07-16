// ════════════════════════════════════════════════════════════════════════
//  file-preview.js — wiederverwendbares Vorschaufenster (Walter-Vorgabe 24.05.2026)
//
//  Ergänzung zu save-blob.js: ANZEIGBARE Dateien (PDF, Bilder) werden ZUERST
//  in einem Vorschaufenster gezeigt — mit den drei Aktionen
//  Drucken / Herunterladen / Schliessen. Erst „Herunterladen" öffnet den
//  „Speichern unter…"-Dialog (saveBlobAsk).
//
//  NICHT anzeigbare Dateien (Word, Excel, …) sowie echte Download-Dateien
//  (DTA-XML, LSE-CSV) gehen weiterhin DIREKT über saveBlobAsk — KEIN
//  Vorschaufenster. Für diese Fälle wird im Code weiter saveBlobAsk genutzt;
//  bei gemischten Typen (Posteingang/Dokumente) entscheidet previewFileModal
//  automatisch nach Dateiendung/MIME.
//
//  Wird NACH save-blob.js als Modul-Script in index.html UND import.html
//  geladen. Das Modal wird beim ersten Aufruf dynamisch in <body> erzeugt,
//  daher kein HTML-Edit pro Seite nötig. Look angelehnt an lohnPdfModal.
// ════════════════════════════════════════════════════════════════════════

let _fpBlob = null, _fpUrl = null, _fpName = '';

// Liefert 'pdf' | 'image' | null  (null = nicht im Browser anzeigbar)
function _fpKind(filename, mime) {
    const ext = (String(filename || '').match(/\.[^.]+$/) || [''])[0].toLowerCase();
    const m   = String(mime || '').toLowerCase();
    if (m.includes('pdf') || ext === '.pdf') return 'pdf';
    if (m.startsWith('image/') ||
        ['.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp', '.svg'].includes(ext)) return 'image';
    return null;
}

function _fpEnsureModal() {
    let modal = document.getElementById('filePreviewModal');
    if (modal) return modal;
    modal = document.createElement('div');
    modal.id = 'filePreviewModal';
    modal.style.cssText = 'display:none;position:fixed;inset:0;z-index:10050;background:rgba(0,0,0,0.55)';
    modal.innerHTML =
        '<div style="position:absolute;top:3vh;left:5vw;right:5vw;bottom:3vh;background:white;border-radius:12px;box-shadow:0 25px 60px rgba(0,0,0,0.35);display:flex;flex-direction:column;overflow:hidden">'
        + '<div style="display:flex;align-items:center;justify-content:space-between;padding:14px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc">'
        + '<div>'
        + '<div style="font-size:15px;font-weight:700;color:#0f172a">Vorschau</div>'
        + '<div id="filePreviewTitle" style="font-size:12px;color:#64748b;margin-top:2px">–</div>'
        + '</div>'
        + '<button onclick="filePreviewClose()" title="Schliessen" style="background:transparent;border:none;cursor:pointer;font-size:22px;color:#64748b;padding:4px 10px">×</button>'
        + '</div>'
        + '<div style="flex:1;background:#1e293b;display:flex;align-items:center;justify-content:center;overflow:hidden">'
        + '<iframe id="filePreviewFrame" style="width:100%;height:100%;border:none;background:white" title="Datei-Vorschau"></iframe>'
        + '</div>'
        + '<div style="display:flex;align-items:center;justify-content:flex-end;gap:8px;padding:12px 20px;border-top:1px solid #e2e8f0;background:white">'
        + '<button onclick="filePreviewPrint()" style="padding:7px 16px;border:1px solid #cbd5e1;background:white;border-radius:7px;font-size:13px;cursor:pointer;color:#0f172a">🖨 Drucken</button>'
        + '<button onclick="filePreviewDownload()" style="padding:7px 16px;border:1px solid #cbd5e1;background:white;border-radius:7px;font-size:13px;cursor:pointer;color:#0f172a">⬇ Herunterladen</button>'
        + '<button onclick="filePreviewClose()" style="padding:7px 16px;border:none;background:#0f172a;color:white;border-radius:7px;font-size:13px;cursor:pointer">✕ Schliessen</button>'
        + '</div>'
        + '</div>';
    document.body.appendChild(modal);
    // Klick auf den dunklen Hintergrund schliesst das Fenster.
    modal.addEventListener('click', e => { if (e.target === modal) filePreviewClose(); });
    // ESC schliesst.
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && modal.style.display === 'block') filePreviewClose();
    });
    return modal;
}

// Haupt-Einstieg: zeigt das Blob im Vorschaufenster (PDF/Bild) ODER speichert
// es direkt (nicht anzeigbare Typen). Drop-in-Ersatz für saveBlobAsk(blob,name)
// überall wo der User die Datei zuerst ansehen können soll.
async function previewFileModal(blob, filename) {
    const kind = _fpKind(filename, blob && blob.type);
    if (!kind) { await saveBlobAsk(blob, filename); return; }   // nicht anzeigbar → direkt speichern
    const modal = _fpEnsureModal();
    if (_fpUrl) URL.revokeObjectURL(_fpUrl);
    _fpBlob = blob;
    _fpName = filename || 'datei';
    _fpUrl  = URL.createObjectURL(blob);
    const titleEl = document.getElementById('filePreviewTitle');
    if (titleEl) titleEl.textContent = _fpName;
    document.getElementById('filePreviewFrame').src = _fpUrl;
    modal.style.display = 'block';
}

// Convenience: holt die Datei per fetch und zeigt sie dann an. Vereinheitlicht
// das verbreitete „fetch → blob → anzeigen"-Muster. Liefert true bei Erfolg.
async function previewUrlFetch(url, filenameFallback, headers) {
    try {
        const r = await fetch(url, { headers: headers || {} });
        if (!r.ok) {
            let msg = 'HTTP ' + r.status;
            try { const j = await r.clone().json(); if (j && j.error) msg = j.error; }
            catch (_) { try { const t = await r.text(); if (t) msg = t; } catch (__) {} }
            alert('Konnte Datei nicht laden: ' + msg);
            return false;
        }
        const blob = await r.blob();
        const cd = r.headers.get('Content-Disposition') || '';
        const m  = /filename\*?=(?:UTF-8'')?["']?([^;"']+)["']?/i.exec(cd);
        const name = (m && decodeURIComponent(m[1])) || filenameFallback || 'datei';
        await previewFileModal(blob, name);
        return true;
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        return false;
    }
}

// One-Shot-Callback nach dem Schliessen des Vorschaufensters (Walter
// 16.07.2026): damit Folge-Fragen («Kuendigung aufheben?») ERST nach dem
// Schreiben kommen, nicht ueber der Vorschau.
let _fpOnClose = null;
function filePreviewOnClose(cb) { _fpOnClose = cb; }

function filePreviewClose() {
    const modal = document.getElementById('filePreviewModal');
    if (modal) modal.style.display = 'none';
    const frame = document.getElementById('filePreviewFrame');
    if (frame) frame.src = 'about:blank';
    if (_fpUrl) { URL.revokeObjectURL(_fpUrl); _fpUrl = null; }
    _fpBlob = null;
    const cb = _fpOnClose; _fpOnClose = null;
    if (cb) { try { cb(); } catch (_) {} }
}

async function filePreviewDownload() {
    if (_fpBlob)      await saveBlobAsk(_fpBlob, _fpName);
    else if (_fpUrl)  await saveUrlAsk(_fpUrl, _fpName);
}

function filePreviewPrint() {
    const frame = document.getElementById('filePreviewFrame');
    if (!frame || !frame.contentWindow) { alert('Drucken nicht möglich.'); return; }
    try { frame.contentWindow.focus(); frame.contentWindow.print(); }
    catch (e) { alert('Drucken nicht möglich: ' + (e?.message || e)); }
}
