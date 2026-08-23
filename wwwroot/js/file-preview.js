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

// ── Fenster-Verhalten für Vorschau-Panels (Walter-Vorgabe 12.08.2026) ────
// Macht eine Vorschau-Box wie ein Fenster bedienbar: am Kopf VERSCHIEBEN,
// unten rechts in der GRÖSSE ziehen (nativer CSS-resize). Wiederverwendbar
// für filePreviewModal (global) UND pbPreviewPanel (Posteingang) — das
// Dokumente-Panel (dokPreviewPanel) hat bereits eigenes Drag/Resize.
// Wichtig: während des Ziehens pointer-events der iframes ausschalten,
// sonst schluckt der PDF-Viewer die Maus-Events und das Ziehen bleibt hängen.
function fpMakeWindow(box, handle) {
    if (!box || !handle || box._fpWin) return;
    box._fpWin = true;
    box.style.resize = 'both';
    box.style.overflow = 'hidden';
    box.style.minWidth = '380px';
    box.style.minHeight = '280px';
    handle.style.cursor = 'move';
    handle.style.userSelect = 'none';
    handle.addEventListener('pointerdown', (e) => {
        // Buttons/Inputs im Header bleiben normal bedienbar.
        if (e.target.closest('button,select,input,a,textarea')) return;
        e.preventDefault();
        const r = box.getBoundingClientRect();
        // Auf feste Pixel-Geometrie umstellen (statt vw/vh/right/bottom) —
        // erst damit sind left/top frei verschiebbar.
        box.style.position = 'fixed';
        box.style.left = r.left + 'px';
        box.style.top = r.top + 'px';
        box.style.width = r.width + 'px';
        box.style.height = r.height + 'px';
        box.style.right = 'auto';
        box.style.bottom = 'auto';
        const dx = e.clientX - r.left, dy = e.clientY - r.top;
        const frames = box.querySelectorAll('iframe');
        frames.forEach(f => { f.style.pointerEvents = 'none'; });
        const move = (ev) => {
            box.style.left = Math.min(window.innerWidth - 80, Math.max(80 - r.width, ev.clientX - dx)) + 'px';
            box.style.top  = Math.min(window.innerHeight - 44, Math.max(0, ev.clientY - dy)) + 'px';
        };
        const up = () => {
            document.removeEventListener('pointermove', move);
            document.removeEventListener('pointerup', up);
            frames.forEach(f => { f.style.pointerEvents = ''; });
        };
        document.addEventListener('pointermove', move);
        document.addEventListener('pointerup', up);
    });
}

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
        '<div id="filePreviewBox" style="position:absolute;top:3vh;left:5vw;right:5vw;bottom:3vh;background:white;border-radius:12px;box-shadow:0 25px 60px rgba(0,0,0,0.35);display:flex;flex-direction:column;overflow:hidden">'
        + '<div id="filePreviewHeader" style="display:flex;align-items:center;justify-content:space-between;padding:14px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc">'
        + '<div>'
        + '<div style="font-size:15px;font-weight:700;color:#0f172a">Vorschau</div>'
        + '<div id="filePreviewTitle" style="font-size:12px;color:#64748b;margin-top:2px">–</div>'
        + '</div>'
        + '<button onclick="filePreviewClose()" title="Schliessen" style="background:transparent;border:none;cursor:pointer;font-size:22px;color:#64748b;padding:4px 10px">×</button>'
        + '</div>'
        + '<div style="flex:1;background:#1e293b;display:flex;align-items:center;justify-content:center;overflow:hidden">'
        + '<iframe id="filePreviewFrame" style="width:100%;height:100%;border:none;background:white" title="Datei-Vorschau"></iframe>'
        + '</div>'
        + '<div id="filePreviewDossierForm" style="display:none;align-items:center;gap:8px;padding:10px 20px;border-top:1px solid #e2e8f0;background:#f8fafc;flex-wrap:wrap">'
        + '<select id="filePreviewDossierTyp" class="no-liquid" style="padding:6px 10px;border:1px solid #cbd5e1;border-radius:7px;font-size:12.5px;background:white;max-width:320px"></select>'
        + '<input id="filePreviewDossierBem" type="text" placeholder="Bemerkung (optional)" style="flex:1;min-width:140px;padding:6px 10px;border:1px solid #cbd5e1;border-radius:7px;font-size:12.5px">'
        + '<button onclick="filePreviewDossierSubmit()" id="filePreviewDossierSubmit" style="padding:6px 14px;border:none;background:#3f3f3f;color:white;border-radius:9px;font-size:12.5px;font-weight:600;cursor:pointer">Ablegen</button>'
        + '<span id="filePreviewDossierStatus" style="font-size:12px"></span>'
        + '</div>'
        + '<div style="display:flex;align-items:center;justify-content:flex-end;gap:8px;padding:12px 20px;border-top:1px solid #e2e8f0;background:white">'
        + '<span id="filePreviewExtra" style="margin-right:auto;display:flex;align-items:center;gap:8px;font-size:13px;color:#475569"></span>'
        + '<button id="filePreviewDossierBtn" onclick="filePreviewDossierToggle()" style="display:none;padding:7px 16px;border:1px solid #cbd5e1;background:white;border-radius:7px;font-size:13px;cursor:pointer;color:#0f172a">📁 Ins Dossier ablegen</button>'
        + '<button onclick="filePreviewPrint()" style="padding:7px 16px;border:1px solid #cbd5e1;background:white;border-radius:7px;font-size:13px;cursor:pointer;color:#0f172a">🖨 Drucken</button>'
        + '<button onclick="filePreviewDownload()" style="padding:7px 16px;border:1px solid #cbd5e1;background:white;border-radius:7px;font-size:13px;cursor:pointer;color:#0f172a">⬇ Herunterladen</button>'
        + '<button onclick="filePreviewClose()" style="padding:7px 16px;border:none;background:#0f172a;color:white;border-radius:7px;font-size:13px;cursor:pointer">✕ Schliessen</button>'
        + '</div>'
        + '</div>';
    document.body.appendChild(modal);
    // Fenster-Verhalten: am Kopf verschieben, unten rechts Grösse ziehen.
    fpMakeWindow(document.getElementById('filePreviewBox'), document.getElementById('filePreviewHeader'));
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
async function previewFileModal(blob, filename, opts) {
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
    // Optionaler MA-Kontext (Walter 06.08.2026): «Ins Dossier ablegen»
    // erscheint nur, wenn der Aufrufer employeeId mitgibt.
    _fpEmpId = opts && opts.employeeId ? opts.employeeId : null;
    const dosBtn  = document.getElementById('filePreviewDossierBtn');
    const dosForm = document.getElementById('filePreviewDossierForm');
    const dosStat = document.getElementById('filePreviewDossierStatus');
    if (dosBtn)  dosBtn.style.display = _fpEmpId ? '' : 'none';
    if (dosForm) dosForm.style.display = 'none';
    if (dosStat) dosStat.textContent = '';
    // Extra-Zone links in der Knopfleiste (Walter 23.08.2026, z.B.
    // Unterzeichner-Umschalter beim Vertrags-PDF) — bei jedem Öffnen leeren;
    // Aufrufer setzt sie danach via filePreviewSetExtra().
    filePreviewSetExtra('');
    modal.style.display = 'block';
}

// ── Extra-Steuerelemente in der Vorschau-Knopfleiste (Walter 23.08.2026) ──
function filePreviewSetExtra(html) {
    const el = document.getElementById('filePreviewExtra');
    if (el) el.innerHTML = html || '';
}

// Tauscht das angezeigte Dokument aus, ohne das Fenster zu schliessen
// (z.B. nach Unterzeichner-Wechsel neu erzeugtes Vertrags-PDF).
function filePreviewReplaceBlob(blob, filename) {
    if (_fpUrl) URL.revokeObjectURL(_fpUrl);
    _fpBlob = blob;
    if (filename) _fpName = filename;
    _fpUrl = URL.createObjectURL(blob);
    const frame = document.getElementById('filePreviewFrame');
    if (frame) frame.src = _fpUrl;
}

// ── «Ins Dossier ablegen» (Walter 06.08.2026) ──────────────────────────
// Legt das angezeigte PDF als Dokument beim MA ab — gleiche API wie das
// RAV-Muster (/api/documents/upload, Typ aus der Dokument-Taxonomie).
let _fpEmpId = null;

async function filePreviewDossierToggle() {
    const form = document.getElementById('filePreviewDossierForm');
    const sel  = document.getElementById('filePreviewDossierTyp');
    if (!form || !sel) return;
    if (!sel.options.length) {
        try {
            const r = await fetch('/api/documents/taxonomie', { headers: ah() });
            const tx = r.ok ? await r.json() : [];
            const opts = [];
            tx.forEach(k => (k.typen || []).forEach(t =>
                opts.push(`<option value="${t.id}">${k.name} → ${t.name}</option>`)));
            sel.innerHTML = '<option value="">— Dokument-Typ wählen —</option>' + opts.join('');
        } catch {
            sel.innerHTML = '<option value="">Typen nicht ladbar</option>';
        }
    }
    form.style.display = (form.style.display === 'none') ? 'flex' : 'none';
}

async function filePreviewDossierSubmit() {
    const status = document.getElementById('filePreviewDossierStatus');
    const submit = document.getElementById('filePreviewDossierSubmit');
    if (!_fpBlob || !_fpEmpId) return;
    const typId = parseInt(document.getElementById('filePreviewDossierTyp')?.value || '0', 10);
    if (!Number.isFinite(typId) || typId <= 0) {
        if (status) { status.textContent = 'Bitte Dokument-Typ wählen.'; status.style.color = '#b91c1c'; }
        return;
    }
    const branch = (typeof allBranches !== 'undefined' && Array.isArray(allBranches) && typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
        ? allBranches.find(b => b.id === fixedCompanyProfileId) : null;
    const branchCode = branch?.restaurantCode || '';
    if (submit) submit.disabled = true;
    if (status) { status.textContent = 'Wird abgelegt…'; status.style.color = '#64748b'; }
    try {
        const fd = new FormData();
        fd.append('file', _fpBlob, _fpName);
        fd.append('employeeId', String(_fpEmpId));
        fd.append('dokumentTypId', String(typId));
        if (branchCode) fd.append('branchCode', branchCode);
        const bem = document.getElementById('filePreviewDossierBem')?.value?.trim();
        if (bem) fd.append('bemerkung', bem);
        const r = await fetch('/api/documents/upload', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd,
        });
        if (!r.ok) {
            if (status) {
                status.textContent = (r.status === 409)
                    ? 'Dokument existiert bereits beim MA.'
                    : 'Ablegen fehlgeschlagen (HTTP ' + r.status + ').';
                status.style.color = '#b91c1c';
            }
            return;
        }
        if (status) { status.textContent = '✓ Im Dossier abgelegt.'; status.style.color = '#15803d'; }
    } catch (e) {
        if (status) { status.textContent = 'Verbindungsfehler.'; status.style.color = '#b91c1c'; }
    } finally {
        if (submit) submit.disabled = false;
    }
}

// Convenience: holt die Datei per fetch und zeigt sie dann an. Vereinheitlicht
// das verbreitete „fetch → blob → anzeigen"-Muster. Liefert true bei Erfolg.
async function previewUrlFetch(url, filenameFallback, headers) {
    try {
        const r = await fetch(url, { headers: headers || {} });
        if (!r.ok) {
            let msg = 'HTTP ' + r.status;
            // message + detail mit anzeigen (Walter 13.08.2026) — sonst ist die
            // Ursache («BEWERBUNGSBOGEN_PDF_FEHLER») nicht diagnostizierbar.
            try {
                const j = await r.clone().json();
                if (j && j.error) msg = [j.error, j.message, j.detail].filter(Boolean).join(' — ');
            }
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
