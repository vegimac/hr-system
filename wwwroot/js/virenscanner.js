// Virenscanner (Walter-Vorgabe 08.10.2026): System › Kontrolle › Virenscanner.
// ClamAV prüft jeden Upload serverseitig (VirenScanFilter, WebDAV, easy@work-HR-Dateien).
// Hier: Status (erreichbar, Signaturstand), Funde mit «Erledigt», einmaliger Durchlauf
// über die bestehende Dokumentablage. Während der Durchlauf läuft, alle 3 s neu laden.

let _vsTimer = null;

function _vsEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}

function _vsDatum(iso, mitZeit = true) {
    if (!iso) return '–';
    const d = new Date(iso);
    if (isNaN(d)) return '–';
    const p = n => String(n).padStart(2, '0');
    const t = `${p(d.getDate())}.${p(d.getMonth() + 1)}.${d.getFullYear()}`;
    return mitZeit ? `${t} ${p(d.getHours())}:${p(d.getMinutes())}` : t;
}

function _vsQuelle(q) {
    return ({ UPLOAD: 'Upload', WEBDAV: 'WebDAV', EASYATWORK: 'easy@work', BESTAND: 'Bestand' })[q] || (q || '–');
}

function vsInit() {
    vsLoad();
}

async function vsLoad() {
    await Promise.all([vsLoadStatus(), vsLoadFunde()]);
}

async function vsLoadStatus() {
    const el = document.getElementById('vsStatus');
    const bel = document.getElementById('vsBestand');
    const btn = document.getElementById('vsBestandBtn');
    if (!el) return;
    try {
        const r = await fetch('/api/viren-scanner/status', { headers: ah() });
        if (!r.ok) { el.innerHTML = `<span style="color:#991b1b">Fehler ${r.status} beim Laden.</span>`; return; }
        const s = await r.json();

        let zeile;
        if (!s.pflicht) {
            zeile = '<span style="color:#8b8b8b">○ Auf diesem Rechner läuft kein Virenscanner — geprüft wird nur auf dem Server.</span>';
        } else if (s.erreichbar) {
            const sig = s.signaturenStand
                ? `Signaturen vom ${_vsDatum(s.signaturenStand)}${s.signaturenVersion ? ' (Version ' + s.signaturenVersion + ')' : ''}`
                : 'Signaturstand unbekannt';
            const alt = s.signaturenStand && (Date.now() - new Date(s.signaturenStand).getTime()) > 3 * 86400000;
            zeile = `<span style="color:#166534;font-weight:700">● bereit</span> · <span style="${alt ? 'color:#b45309;font-weight:600' : ''}">${_vsEsc(sig)}${alt ? ' — älter als 3 Tage, Aktualisierung prüfen' : ''}</span>`;
        } else {
            zeile = '<span style="color:#b91c1c;font-weight:700">● nicht erreichbar</span> — Uploads werden abgelehnt, bis der Scanner wieder läuft.';
        }
        const offen = s.offeneFunde
            ? ` · <span style="color:#b91c1c;font-weight:700">${s.offeneFunde} offene${s.offeneFunde === 1 ? 'r' : ''} Fund${s.offeneFunde === 1 ? '' : 'e'}</span>`
            : ' · keine offenen Funde';
        el.innerHTML = zeile + offen;

        const b = s.bestand || {};
        if (btn) btn.disabled = !s.pflicht || !s.erreichbar || !!b.laeuft;
        if (bel) {
            if (b.laeuft) {
                const pct = b.gesamt ? Math.floor(b.geprueft * 100 / b.gesamt) : 0;
                bel.innerHTML = `Prüfung bestehender Dokumente läuft: <b>${b.geprueft} / ${b.gesamt}</b> (${pct} %) · ${b.funde} Fund${b.funde === 1 ? '' : 'e'}`
                    + (b.unlesbar ? ` · ${b.unlesbar} nicht lesbar` : '');
            } else if (b.gestartetAm) {
                const ok = !b.fehler;
                bel.innerHTML = `Letzte Prüfung bestehender Dokumente: ${_vsDatum(b.gestartetAm)}${b.gestartetVon ? ' von ' + _vsEsc(b.gestartetVon) : ''}`
                    + ` · ${b.geprueft} / ${b.gesamt} geprüft · ${b.funde} Fund${b.funde === 1 ? '' : 'e'}`
                    + (b.unlesbar ? ` · ${b.unlesbar} nicht lesbar` : '')
                    + (ok ? '' : `<div style="color:#b91c1c;margin-top:4px">${_vsEsc(b.fehler)}</div>`);
            } else {
                bel.textContent = 'Bestehende Dokumente wurden seit dem letzten Serverstart nicht geprüft.';
            }
        }

        if (b.laeuft) vsTimerStarten(); else vsTimerStoppen(true);
    } catch (e) {
        el.innerHTML = `<span style="color:#991b1b">${_vsEsc(e.message || String(e))}</span>`;
    }
}

function vsTimerStarten() {
    if (_vsTimer) return;
    _vsTimer = setInterval(() => {
        const pg = document.getElementById('page-virenscanner');
        if (!pg || !pg.classList.contains('active')) { vsTimerStoppen(false); return; }
        vsLoadStatus();
    }, 3000);
}

function vsTimerStoppen(funde) {
    if (!_vsTimer) return;
    clearInterval(_vsTimer);
    _vsTimer = null;
    if (funde) vsLoadFunde();
}

async function vsLoadFunde() {
    const body = document.getElementById('vsBody');
    if (!body) return;
    const alle = document.getElementById('vsAlle')?.checked ? 'true' : 'false';
    try {
        const r = await fetch(`/api/viren-scanner/funde?alle=${alle}`, { headers: ah() });
        if (!r.ok) { body.innerHTML = `<tr><td colspan="8" style="color:#991b1b;padding:14px">Fehler ${r.status} beim Laden.</td></tr>`; return; }
        const liste = await r.json();
        if (!liste.length) {
            body.innerHTML = '<tr><td colspan="8" style="color:#6b7280;padding:18px 14px">Keine Funde.</td></tr>';
            return;
        }
        body.innerHTML = liste.map(f => {
            const wer = [f.benutzer, f.maName ? 'MA: ' + f.maName : null].filter(Boolean).map(_vsEsc).join('<br>');
            const status = f.erledigt
                ? `<span style="color:#166534">erledigt ${_vsDatum(f.erledigtAm, false)}</span>`
                : '<span style="color:#b91c1c;font-weight:700">offen</span>';
            const menu = f.erledigt ? '' : `
                <div style="position:relative;display:inline-block">
                    <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'vs-${f.id}')">⋮</button>
                    <div class="dok-menu" id="dokMenu-vs-${f.id}">
                        <button class="dok-menu-item" onclick="dokCloseAllMenus(); vsErledigt(${f.id})">Erledigt</button>
                    </div>
                </div>`;
            return `<tr>
                <td>${_vsDatum(f.gefundenAm)}</td>
                <td>${_vsEsc(_vsQuelle(f.quelle))}</td>
                <td style="word-break:break-all">${_vsEsc(f.dateiname)}</td>
                <td style="color:#b91c1c;font-weight:600">${_vsEsc(f.virus)}</td>
                <td style="font-size:12px;color:#646464;word-break:break-all">${_vsEsc(f.ort)}</td>
                <td>${wer || '–'}</td>
                <td>${status}</td>
                <td style="text-align:right">${menu}</td>
            </tr>`;
        }).join('');
    } catch (e) {
        body.innerHTML = `<tr><td colspan="8" style="color:#991b1b;padding:14px">${_vsEsc(e.message || String(e))}</td></tr>`;
    }
}

async function vsErledigt(id) {
    const ok = await liquidConfirm('Fund als erledigt markieren? Bei Funden aus dem Bestand vorher die Datei prüfen bzw. löschen.',
        { title: 'Virenscanner', yesLabel: 'Erledigt', noLabel: 'Abbrechen' });
    if (!ok) return;
    try {
        const r = await fetch(`/api/viren-scanner/funde/${id}/erledigt`, { method: 'POST', headers: ah() });
        if (!r.ok) { showToast(`Fehler ${r.status}`, 'error'); return; }
        showToast('Fund erledigt.', 'success');
        vsLoad();
    } catch (e) {
        showToast(e.message || String(e), 'error');
    }
}

async function vsBestandStarten() {
    const ok = await liquidConfirm('Alle bestehenden Dokumente der Ablage jetzt auf Schadsoftware prüfen? Das läuft im Hintergrund und kann je nach Menge einige Minuten dauern. Funde erscheinen unten in der Liste und auf der To-do-Liste.',
        { title: 'Bestehende Dokumente prüfen', yesLabel: 'Prüfung starten', noLabel: 'Abbrechen' });
    if (!ok) return;
    try {
        const r = await fetch('/api/viren-scanner/bestand-scan', { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { showToast(j.message || `Fehler ${r.status}`, 'error'); return; }
        showToast('Prüfung gestartet.', 'success');
        vsLoadStatus();
    } catch (e) {
        showToast(e.message || String(e), 'error');
    }
}
