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
// Grösse über eigene Griffe an allen Kanten/Ecken (Walter 12.10.2026) — die
// native resize-Ecke lag unter dem PDF-Viewer und war nicht greifbar.
// Doppelklick auf den Kopf = maximieren/zurück; die letzte Grösse merkt
// sich der Browser pro Fenster-ID.
const FP_MIN_W = 380, FP_MIN_H = 280;

function _fpFixGeometry(box) {
    const r = box.getBoundingClientRect();
    box.style.position = 'fixed';
    box.style.left = r.left + 'px';
    box.style.top = r.top + 'px';
    box.style.width = r.width + 'px';
    box.style.height = r.height + 'px';
    box.style.right = 'auto';
    box.style.bottom = 'auto';
    return r;
}

function _fpSaveGeometry(box) {
    if (!box.id) return;
    const r = box.getBoundingClientRect();
    try {
        localStorage.setItem('fpWin:' + box.id, JSON.stringify({ l: r.left, t: r.top, w: r.width, h: r.height }));
    } catch { /* Speicher voll/gesperrt — dann eben ohne Merken */ }
}

function _fpRestoreGeometry(box) {
    if (!box.id) return;
    let g = null;
    try { g = JSON.parse(localStorage.getItem('fpWin:' + box.id) || 'null'); } catch { g = null; }
    if (!g || !(g.w > 0) || !(g.h > 0)) return;
    const w = Math.max(FP_MIN_W, Math.min(window.innerWidth, g.w));
    const h = Math.max(FP_MIN_H, Math.min(window.innerHeight, g.h));
    const l = Math.max(0, Math.min(window.innerWidth - w, g.l));
    const t = Math.max(0, Math.min(window.innerHeight - h, g.t));
    Object.assign(box.style, {
        position: 'fixed', left: l + 'px', top: t + 'px', width: w + 'px', height: h + 'px',
        right: 'auto', bottom: 'auto',
    });
}

function _fpDrag(e, box, onMove) {
    e.preventDefault();
    e.stopPropagation();
    const start = _fpFixGeometry(box);
    box._fpMax = null;
    const frames = box.querySelectorAll('iframe');
    frames.forEach(f => { f.style.pointerEvents = 'none'; });
    const move = (ev) => onMove(ev, start);
    const up = () => {
        document.removeEventListener('pointermove', move);
        document.removeEventListener('pointerup', up);
        frames.forEach(f => { f.style.pointerEvents = ''; });
        document.body.style.cursor = '';
        _fpSaveGeometry(box);
        // Loslassen ausserhalb des Fensters erzeugt einen Klick auf den
        // Hintergrund — der würde die Vorschau schliessen.
        const schlucken = (ce) => { ce.stopPropagation(); ce.preventDefault(); };
        window.addEventListener('click', schlucken, { capture: true, once: true });
        setTimeout(() => window.removeEventListener('click', schlucken, { capture: true }), 0);
    };
    document.addEventListener('pointermove', move);
    document.addEventListener('pointerup', up);
}

function fpMakeWindow(box, handle) {
    if (!box || !handle || box._fpWin) return;
    box._fpWin = true;
    box.style.resize = 'none';
    box.style.overflow = 'hidden';
    box.style.minWidth = FP_MIN_W + 'px';
    box.style.minHeight = FP_MIN_H + 'px';
    handle.style.cursor = 'move';
    handle.style.userSelect = 'none';
    _fpRestoreGeometry(box);

    handle.addEventListener('pointerdown', (e) => {
        // Buttons/Inputs im Header bleiben normal bedienbar.
        if (e.target.closest('button,select,input,a,textarea')) return;
        if (e.button !== 0) return;
        const dx = e.clientX - box.getBoundingClientRect().left;
        const dy = e.clientY - box.getBoundingClientRect().top;
        _fpDrag(e, box, (ev, r) => {
            box.style.left = Math.min(window.innerWidth - 80, Math.max(80 - r.width, ev.clientX - dx)) + 'px';
            box.style.top  = Math.min(window.innerHeight - 44, Math.max(0, ev.clientY - dy)) + 'px';
        });
    });

    handle.addEventListener('dblclick', (e) => {
        if (e.target.closest('button,select,input,a,textarea')) return;
        if (box._fpMax) {
            Object.assign(box.style, box._fpMax);
            box._fpMax = null;
        } else {
            _fpFixGeometry(box);
            box._fpMax = { left: box.style.left, top: box.style.top, width: box.style.width, height: box.style.height };
            Object.assign(box.style, { left: '8px', top: '8px', width: (window.innerWidth - 16) + 'px', height: (window.innerHeight - 16) + 'px' });
        }
        _fpSaveGeometry(box);
    });

    // Griffe: Kanten 8 px, Ecken 16 px — liegen über dem iframe (z-index).
    const griffe = {
        n:  'top:0;left:16px;right:16px;height:8px;cursor:ns-resize',
        s:  'bottom:0;left:16px;right:16px;height:8px;cursor:ns-resize',
        e:  'right:0;top:16px;bottom:16px;width:8px;cursor:ew-resize',
        w:  'left:0;top:16px;bottom:16px;width:8px;cursor:ew-resize',
        ne: 'top:0;right:0;width:16px;height:16px;cursor:nesw-resize',
        nw: 'top:0;left:0;width:16px;height:16px;cursor:nwse-resize',
        se: 'bottom:0;right:0;width:16px;height:16px;cursor:nwse-resize;'
            + 'background:linear-gradient(135deg,transparent 55%,rgba(60,55,48,0.35) 55%,rgba(60,55,48,0.35) 62%,transparent 62%,transparent 72%,rgba(60,55,48,0.35) 72%,rgba(60,55,48,0.35) 79%,transparent 79%)',
        sw: 'bottom:0;left:0;width:16px;height:16px;cursor:nesw-resize',
    };
    for (const [dir, css] of Object.entries(griffe)) {
        const g = document.createElement('div');
        g.className = 'fp-resize-griff';
        g.style.cssText = 'position:absolute;z-index:20;touch-action:none;' + css;
        g.addEventListener('pointerdown', (e) => {
            if (e.button !== 0) return;
            const sx = e.clientX, sy = e.clientY;
            document.body.style.cursor = getComputedStyle(g).cursor;
            _fpDrag(e, box, (ev, r) => {
                const ddx = ev.clientX - sx, ddy = ev.clientY - sy;
                let { left, top, width, height } = r;
                if (dir.includes('e')) width = Math.max(FP_MIN_W, Math.min(window.innerWidth - left, r.width + ddx));
                if (dir.includes('s')) height = Math.max(FP_MIN_H, Math.min(window.innerHeight - top, r.height + ddy));
                if (dir.includes('w')) {
                    width = Math.max(FP_MIN_W, Math.min(r.right, r.width - ddx));
                    left = r.right - width;
                }
                if (dir.includes('n')) {
                    height = Math.max(FP_MIN_H, Math.min(r.bottom, r.height - ddy));
                    top = r.bottom - height;
                }
                Object.assign(box.style, { left: left + 'px', top: top + 'px', width: width + 'px', height: height + 'px' });
            });
        });
        box.appendChild(g);
    }
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
        + '<button id="filePreviewDownloadBtn" onclick="filePreviewDownload()" style="padding:7px 16px;border:1px solid #cbd5e1;background:white;border-radius:7px;font-size:13px;cursor:pointer;color:#0f172a">⬇ Herunterladen</button>'
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
    // Fenster-Geometrie bei jedem Öffnen zurücksetzen (Walter 23.08.2026):
    // nach Verschieben/Vergrössern blieb die Pixel-Geometrie für ALLE weiteren
    // Vorschauen kleben — hing das Fenster teils ausserhalb des Bildschirms,
    // wirkte das PDF «nicht bis unten scrollbar». Jedes Öffnen startet wieder
    // vollflächig sichtbar; verschieben/ziehen bleibt pro Ansicht möglich.
    const _fpBox = document.getElementById('filePreviewBox');
    if (_fpBox) {
        _fpBox.style.position = 'absolute';
        _fpBox.style.left = '5vw';  _fpBox.style.right  = '5vw';
        _fpBox.style.top  = '3vh';  _fpBox.style.bottom = '3vh';
        _fpBox.style.width = 'auto'; _fpBox.style.height = 'auto';
    }
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
    // opts.ohneDownload: z.B. Word-Dokument, das nur für die Anzeige als PDF
    // gerendert wurde — ein Download würde dieses Zwischen-PDF speichern.
    const dlBtn = document.getElementById('filePreviewDownloadBtn');
    if (dlBtn) dlBtn.style.display = opts && opts.ohneDownload ? 'none' : '';
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
        const name = cdFilename(cd, filenameFallback || 'datei');
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
