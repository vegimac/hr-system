// ══════════════════════════════════════════════════════════════════════
// payroll-perioden.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════════════════════════════════════
//  LOHNPERIODEN  (Seite page-perioden)
// ══════════════════════════════════════════════════════════════════════════════

// Datum-Helper: ISO "yyyy-MM-dd" → "dd.mm.yyyy" (Schweizer Konvention,
// gilt programmweit). Akzeptiert auch ISO mit Zeit-Anhängsel oder null.
function fmtDateDe(iso) {
    if (!iso) return '–';
    const s = String(iso).slice(0, 10);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(s)) return iso;
    return `${s.slice(8,10)}.${s.slice(5,7)}.${s.slice(0,4)}`;
}

function initPeriodenPage() {
    const branchSel = document.getElementById('perBranchSelect');
    const yearSel   = document.getElementById('perYearSelect');

    // Filialen füllen (immer neu aufbauen). Vorauswahl folgt dem globalen
    // Filial-Selektor (oben links), nicht selectedCompanyProfile — sonst
    // landet User auf einer anderen Filiale als oben angezeigt.
    branchSel.innerHTML = '<option value="">Filiale wählen…</option>';
    const preselect = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                      ? Number(fixedCompanyProfileId) : null;
    allBranches.forEach(b => {
        const o = document.createElement('option');
        o.value = b.id;
        o.textContent = b.branchName || b.companyName || b.name || b.id;
        if (preselect && b.id === preselect) o.selected = true;
        branchSel.appendChild(o);
    });

    // Jahre füllen
    if (yearSel.options.length === 0) {
        const curY = new Date().getFullYear();
        for (let y = curY + 1; y >= curY - 2; y--) {
            const o = document.createElement('option');
            o.value = y; o.textContent = y;
            yearSel.appendChild(o);
        }
        yearSel.value = curY;
    }

    perBranchChanged();
}

function perBranchChanged() {
    // perLoadConfig() entfernt (Walter-Vorgabe 16.05.2026): Lohnperiode ist
    // immer Kalendermonat — keine Periodenregel-Konfiguration mehr.
    perLoadPerioden();
}

async function perLoadPerioden() {
    const cid  = document.getElementById('perBranchSelect').value;
    const year = document.getElementById('perYearSelect').value;
    const tbody = document.getElementById('perTbody');
    if (!cid) { tbody.innerHTML = '<tr><td colspan="7" style="text-align:center;color:#94a3b8;padding:28px">Filiale wählen</td></tr>'; return; }

    tbody.innerHTML = '<tr><td colspan="7" style="text-align:center;color:#94a3b8;padding:20px">Lade…</td></tr>';
    try {
        const res  = await fetch(`/api/payroll-perioden?companyProfileId=${cid}&year=${year}`, { headers: ah() });
        const list = res.ok ? await res.json() : [];

        if (list.length === 0) {
            tbody.innerHTML = '<tr><td colspan="7" style="text-align:center;color:#94a3b8;padding:28px">Keine Perioden für dieses Jahr</td></tr>';
            return;
        }

        tbody.innerHTML = list.map(p => {
            const isAbgeschlossen = p.status === 'abgeschlossen';
            const isProvisorisch  = p.status === 'provisorisch_abgeschlossen';

            // Definitivlauf-Pille
            let defBadge;
            if (isAbgeschlossen)
                defBadge = `<span style="background:#dcfce7;color:#166534;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:600" title="Definitivlauf vollständig abgeschlossen">Def. abgeschlossen</span>`;
            else if (isProvisorisch)
                defBadge = `<span style="background:#fef3c7;color:#92400e;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:600" title="Definitivlauf wartet auf HR-Abschluss">Def. bei HR</span>`;
            else
                defBadge = `<span style="background:#efece5;color:#6b6152;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:600">Def. offen</span>`;

            // Akonto-Pille (neue Anzeige): zeigt den parallelen Akonto-Workflow-Status
            const akS = (p.akontoStatus || 'OFFEN').toUpperCase();
            const akontoMap = {
                'OFFEN':              { lbl: 'Akonto offen',   bg: '#f1f5f9', fg: '#64748b' },
                'IN_BEARBEITUNG_GF':  { lbl: 'Akonto bei GF',  bg: '#efece5', fg: '#6b6152' },
                'UEBERSPRUNGEN':      { lbl: 'Akonto überspr.', bg: '#e2e8f0', fg: '#64748b' },
                'BEI_HR':             { lbl: 'Akonto bei HR',  bg: '#fef3c7', fg: '#92400e' },
                'HR_FREIGEGEBEN':     { lbl: 'Akonto HR-frei', bg: '#ece9e2', fg: '#6b6152' },
                'AUSBEZAHLT':         { lbl: 'Akonto bezahlt', bg: '#dcfce7', fg: '#166534' }
            };
            const akI = akontoMap[akS] || akontoMap['OFFEN'];
            const akBadge = `<span style="background:${akI.bg};color:${akI.fg};padding:2px 8px;border-radius:10px;font-size:11px;font-weight:600" title="Akonto-Workflow-Status">${akI.lbl}</span>`;

            // Kombi-Zelle: zwei Pillen untereinander
            const statusCell = `<div style="display:flex;flex-direction:column;gap:3px;align-items:flex-start">${akBadge}${defBadge}</div>`;

            // Zahldatum DTA (Bank-Ausführungsdatum) je Strang anzeigen
            // (Walter-Vorgabe 20.05.2026): Akonto aus akontoAuszahlungsdatum,
            // Definitiv aus auszahlungsdatum. Fallback auf das jeweilige
            // „ausbezahlt/abgeschlossen am" für Alt-Perioden ohne Datum.
            const akontoZahl = (akS === 'AUSBEZAHLT')
                ? (p.akontoAuszahlungsdatum
                    ? fmtDateDe(p.akontoAuszahlungsdatum)
                    : (p.akontoAusbezahltAt ? new Date(p.akontoAusbezahltAt).toLocaleDateString('de-CH') : null))
                : null;
            const defZahl = isAbgeschlossen
                ? (p.auszahlungsdatum
                    ? fmtDateDe(p.auszahlungsdatum)
                    : (p.abgeschlossenAm ? new Date(p.abgeschlossenAm).toLocaleDateString('de-CH') : null))
                : null;
            const abschlussCell = (akontoZahl || defZahl)
                ? `<div style="display:flex;flex-direction:column;gap:2px;font-size:11.5px;color:#475569">
                       ${akontoZahl ? `<span title="Akonto: Bank-Ausführungsdatum DTA">💸 Akonto: <b>${akontoZahl}</b></span>` : ''}
                       ${defZahl    ? `<span title="Definitivlohn: Bank-Ausführungsdatum DTA">🧾 Lohn: <b>${defZahl}</b></span>` : ''}
                   </div>`
                : '<span style="color:#94a3b8">–</span>';

            // Löschen-Button: nur wenn ALLES offen ist.
            // Walter-Vorgabe 17.05.2026: Akonto-Status muss OFFEN sein,
            // sonst hängen Akonto-Datensätze + Workflow-Audit dran.
            const akontoOffen = akS === 'OFFEN';
            const canDelete   = !isAbgeschlossen
                              && !isProvisorisch
                              && p.status === 'offen'
                              && akontoOffen
                              && (p.snapshotCount || 0) === 0
                              && (p.akontoCount  || 0) === 0;
            const deleteBtn = canDelete
                ? ` <button class="btn btn-sm btn-outline" style="color:#b91c1c;border-color:#fca5a5"
                            onclick="perDelete(${p.id},'${(p.label || '').replace(/'/g, "\\'")}')">🗑 Löschen</button>`
                : '';

            // Abschliessen-Button: nur bei vollem Definitiv-Offen sinnvoll
            const abschliessenBtn = (!isAbgeschlossen && !isProvisorisch)
                ? `<button class="btn btn-sm btn-outline" style="color:#6b6152;border-color:#d0c8b8" onclick="perAbschliessen(${p.id},'${p.label}')">Abschliessen</button>`
                : '';

            // Akonto-Reset-Button (admin-only, Walter-Vorgabe 17.05.2026):
            // erscheint wenn AkontoStatus != OFFEN. Setzt die Periode aktiv
            // zurück damit lohnrelevante Edits wieder möglich werden. Audit-
            // Trail wird im Backend geschrieben.
            const isAdmin = (typeof currentUser !== 'undefined' && currentUser?.role === 'admin');
            const akontoResetBtn = (isAdmin && !akontoOffen)
                ? `<button class="btn btn-sm btn-outline" style="color:#92400e;border-color:#fcd34d;background:#fffbeb"
                            onclick="perAkontoReset(${p.companyProfileId},${p.year},${p.month},'${(p.label || '').replace(/'/g, "\\'")}')"
                            title="Akonto-Workflow dieser Periode komplett zurücksetzen — danach sind Lohn-Edits wieder möglich">↺ Akonto zurücksetzen</button>`
                : '';

            // Definitiv-Rücknahme (admin-only, Walter-Vorgabe 20.05.2026):
            // erscheint bei abgeschlossenen Perioden. Holt die Periode zurück
            // auf 'provisorisch_abgeschlossen' UND entfernt die Lohnzettel aus
            // den MA-Postfächern (genau wie in der ersten Version). Backend ist
            // gated: nur bis zum DTA-Zahldatum, danach 409 PAYOUT_DATE_REACHED.
            const defWiederOeffnenBtn = (isAdmin && isAbgeschlossen)
                ? `<button class="btn btn-sm btn-outline" style="color:#b91c1c;border-color:#fca5a5;background:#fef2f2"
                            onclick="perWiederOeffnen(${p.id},'${(p.label || '').replace(/'/g, "\\'")}')"
                            title="Definitiv-Abschluss zurücknehmen — zurück auf 'provisorisch', Lohnzettel aus MA-Postfächern entfernen. Nur bis zum DTA-Zahldatum möglich.">↩ Def. zurücknehmen</button>`
                : '';

            // Akonto-DTA-Download (Walter 17.05.2026, Phase 3d): bei AUSBEZAHLT
            // kann das pain.001-XML jederzeit re-downloaded werden.
            const akontoDtaBtn = (akS === 'AUSBEZAHLT')
                ? `<button class="btn btn-sm btn-outline" style="color:#6b6152;border-color:#d0c8b8"
                            onclick="perAkontoDtaDownload(${p.companyProfileId},${p.year},${p.month})"
                            title="pain.001-DTA-File für diesen Akonto-Lauf herunterladen">📥 Akonto-DTA</button>`
                : '';
            // Akonto-Liste als PDF (Walter 18.05.2026): Begleitliste zum DTA,
            // Buchhaltungs-Beleg. Verfügbar sobald der Akonto-Workflow gestartet
            // wurde (auch in der HR-Kontrolle, nicht nur nach AUSBEZAHLT).
            const akontoListeBtn = (akS !== 'OFFEN')
                ? `<button class="btn btn-sm btn-outline" style="color:#6b6152;border-color:#d0c8b8"
                            onclick="perAkontoListePdf(${p.companyProfileId},${p.year},${p.month})"
                            title="Akonto-Zahlungsliste als PDF herunterladen">📄 Akonto-Liste</button>`
                : '';

            const actions = isAbgeschlossen
                ? `${akontoListeBtn}${akontoDtaBtn}${defWiederOeffnenBtn}<button class="btn btn-sm btn-outline" onclick="perShowSnapshots(${p.id},'${p.label}')">Details</button>`
                : `${abschliessenBtn}
                   ${akontoListeBtn}
                   ${akontoDtaBtn}
                   ${akontoResetBtn}
                   <button class="btn btn-sm btn-outline" onclick="perShowSnapshots(${p.id},'${p.label}')">Details</button>${deleteBtn}`;

            // Zeitraum kompakt „1.1.–31.1." (ohne Jahr — steht ja in der Periode-Spalte).
            const dm = (iso) => { const s = String(iso || ''); return parseInt(s.slice(8,10),10) + '.' + parseInt(s.slice(5,7),10) + '.'; };
            const zeitraum = `${dm(p.periodFrom)}–${dm(p.periodTo)}`;
            return `<tr>
                <td style="font-weight:600">${p.label}</td>
                <td style="white-space:nowrap">${zeitraum}</td>
                <td style="text-align:center">${p.snapshotCount}</td>
                <td style="text-align:center;color:${p.finalCount === p.snapshotCount && p.snapshotCount > 0 ? '#16a34a' : '#94a3b8'};font-weight:600">${p.finalCount}</td>
                <td>${statusCell}</td>
                <td style="font-size:12px;color:#64748b">${abschlussCell}</td>
                <td style="white-space:nowrap"><div style="display:flex;gap:6px;flex-wrap:nowrap;align-items:center">${actions}</div></td>
            </tr>`;
        }).join('');
    } catch(e) {
        tbody.innerHTML = `<tr><td colspan="7" style="color:#dc2626;padding:20px">Fehler: ${e.message}</td></tr>`;
    }
}

async function perOpenNewModal() {
    const cid  = document.getElementById('perBranchSelect').value;
    const year = parseInt(document.getElementById('perYearSelect').value);
    if (!cid) { alert('Bitte Filiale wählen.'); return; }

    const monthNames = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];

    // Bestehende Perioden laden um sie auszugrauen
    let existingMonths = new Set();
    try {
        const r = await fetch(`/api/payroll-perioden?companyProfileId=${cid}&year=${year}`, { headers: ah() });
        if (r.ok) { const list = await r.json(); list.forEach(p => existingMonths.add(p.month)); }
    } catch {}

    // Modal aufbauen
    let modal = document.getElementById('perNewModal');
    if (!modal) {
        modal = document.createElement('div');
        modal.id = 'perNewModal';
        modal.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.45);z-index:8000;display:flex;align-items:center;justify-content:center';
        document.body.appendChild(modal);
    }

    const monthGrid = monthNames.map((name, i) => {
        const m = i + 1;
        const exists = existingMonths.has(m);
        return `<button data-month="${m}" onclick="perCreateMonth(${parseInt(cid)}, ${year}, ${m})"
            style="padding:10px 8px;border-radius:8px;font-size:13px;font-weight:600;cursor:${exists ? 'default' : 'pointer'};
                border:2px solid ${exists ? '#e2e8f0' : 'rgba(63,63,63,.45)'};
                background:${exists ? '#f8fafc' : 'rgba(255,255,255,.9)'};
                color:${exists ? '#94a3b8' : '#3f3f3f'};
                ${exists ? 'opacity:0.7;pointer-events:none;' : ''}">
            ${name}${exists ? '<br><span style="font-size:10px;font-weight:400">✓ offen</span>' : ''}
        </button>`;
    }).join('');

    const allMissing = [...Array(12).keys()].filter(i => !existingMonths.has(i+1));

    modal.innerHTML = `
        <div style="background:#fff;border-radius:14px;padding:28px;max-width:500px;width:100%;box-shadow:0 20px 60px rgba(0,0,0,.3)">
            <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:20px">
                <div>
                    <div style="font-size:17px;font-weight:700">Perioden öffnen — ${year}</div>
                    <div style="font-size:12px;color:#64748b;margin-top:3px">Blau = noch nicht angelegt, Grau = bereits vorhanden</div>
                </div>
                <button onclick="document.getElementById('perNewModal').remove()" style="border:none;background:none;font-size:22px;color:#94a3b8;cursor:pointer;line-height:1">&times;</button>
            </div>

            <div style="display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin-bottom:20px">
                ${monthGrid}
            </div>

            ${allMissing.length > 1 ? `
            <div style="border-top:1px solid #f1f5f9;padding-top:16px;display:flex;justify-content:flex-end;gap:10px">
                <button class="btn btn-outline" onclick="document.getElementById('perNewModal').remove()">Abbrechen</button>
                <button class="btn btn-primary" onclick="perCreateAllMonths(${parseInt(cid)}, ${year}, [${allMissing.map(i=>i+1).join(',')}])">
                    Alle ${allMissing.length} fehlenden Perioden anlegen
                </button>
            </div>` : `
            <div style="border-top:1px solid #f1f5f9;padding-top:16px;text-align:right">
                <button class="btn btn-outline" onclick="document.getElementById('perNewModal').remove()">Schliessen</button>
            </div>`}
        </div>`;
}

async function perCreateMonth(cid, year, month) {
    const monthNames = ['','Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
    try {
        const res = await fetch('/api/payroll-perioden', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyProfileId: cid, year, month, label: null })
        });
        if (!res.ok) { const e = await res.json().catch(()=>({})); throw new Error(e.error || e.message || 'Fehler'); }
        await res.json().catch(() => ({}));
        // Walter-Vorgabe 20.05.2026: keine Übergangsperioden mehr — Periode
        // ist immer der Kalendermonat.
        showToast(`${monthNames[month]} ${year} angelegt`, 'success');
        document.getElementById('perNewModal')?.remove();
        perLoadPerioden();
    } catch(e) { alert(e.message); }
}

async function perCreateAllMonths(cid, year, months) {
    const monthNames = ['','Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
    let created = 0, errors = [];
    for (const month of months) {
        try {
            const res = await fetch('/api/payroll-perioden', {
                method: 'POST',
                headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify({ companyProfileId: cid, year, month, label: null })
            });
            if (res.ok) created++;
            else { const e = await res.json().catch(()=>({})); errors.push(`${monthNames[month]}: ${e.message||'Fehler'}`); }
        } catch(e) { errors.push(`${monthNames[month]}: ${e.message}`); }
    }
    document.getElementById('perNewModal')?.remove();
    if (errors.length) alert('Teilweise Fehler:\n' + errors.join('\n'));
    else showToast(`${created} Perioden für ${year} angelegt`, 'success');
    perLoadPerioden();
}

async function perOpenNew() { perOpenNewModal(); }

async function perAbschliessen(periodeId, label) {
    if (!confirm(`Periode «${label}» abschliessen?\n\nAlle Lohnzettel werden unveränderlich finalisiert.`)) return;
    try {
        const res = await fetch(`/api/payroll-perioden/${periodeId}/abschliessen`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId: currentUser?.id ?? 0 })
        });
        if (!res.ok) { const e = await res.json().catch(()=>({})); throw new Error(e.message || 'Fehler'); }
        const r = await res.json();
        showToast(r.message, 'success');
        perLoadPerioden();
    } catch(e) { alert(e.message); }
}

async function perDelete(periodeId, label) {
    if (!confirm(`Periode «${label}» wirklich löschen?\n\nNur möglich solange Status = "offen" und keine Lohnzettel bestätigt wurden.`)) return;
    try {
        let res = await fetch(`/api/payroll-perioden/${periodeId}`, {
            method: 'DELETE', headers: ah()
        });
        if (res.status === 409) {
            const body = await res.json().catch(() => ({}));
            const msg = body.error || 'Konflikt';
            // Backend bietet ?force=true für draft-Saldi an — Walter fragen
            if (msg.includes('Saldi') && confirm(`${msg}\n\nTrotzdem löschen (inkl. Saldi)?`)) {
                res = await fetch(`/api/payroll-perioden/${periodeId}?force=true`, {
                    method: 'DELETE', headers: ah()
                });
            } else {
                alert(msg);
                return;
            }
        }
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            throw new Error(e.error || e.message || `HTTP ${res.status}`);
        }
        showToast(`Periode «${label}» gelöscht`, 'success');
        perLoadPerioden();
    } catch(e) { alert('Löschen fehlgeschlagen: ' + e.message); }
}

// ── Akonto-Zahlungsliste-PDF (Walter 18.05.2026) ─────────────────────────
// Begleitliste zum DTA + Buchhaltungs-Beleg. On-demand generiert.
async function perAkontoListePdf(cpId, year, month) {
    try {
        const r = await fetch(`/api/akonto/workflow/liste-pdf?companyProfileId=${cpId}&year=${year}&month=${month}`, {
            headers: { Authorization: 'Bearer ' + (localStorage.hrToken || '') }
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            alert('PDF-Download fehlgeschlagen: ' + (j.error || `HTTP ${r.status}`));
            return;
        }
        const blob = await r.blob();
        await previewFileModal(blob, `Akonto_Liste_${cpId}_${year}-${String(month).padStart(2,'0')}.pdf`);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// ── Akonto-DTA-Download (Walter 17.05.2026, Phase 3d) ────────────────────
// On-demand-Generierung des pain.001-XML aus der DB. Re-Download jederzeit
// möglich solange Periode AUSBEZAHLT ist.
async function perAkontoDtaDownload(cpId, year, month) {
    try {
        const r = await fetch(`/api/akonto/workflow/dta?companyProfileId=${cpId}&year=${year}&month=${month}`, {
            headers: { Authorization: 'Bearer ' + (localStorage.hrToken || '') }
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            alert('DTA-Download fehlgeschlagen: ' + (j.error || `HTTP ${r.status}`));
            return;
        }
        const blob = await r.blob();
        await saveBlobAsk(blob, `Akonto_DTA_${cpId}_${year}-${String(month).padStart(2,'0')}.xml`);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// ── Akonto-Reset (Admin-Notfall, Walter-Vorgabe 17.05.2026) ─────────────
// Setzt den Akonto-Workflow einer Periode komplett zurück auf OFFEN.
// Voraussetzung damit der GF (und alle anderen) wieder lohnrelevante Daten
// dieser Periode editieren dürfen — sonst greift der LohnEditLockService.
async function perAkontoReset(cpId, year, month, label) {
    const warn =
        `Akonto-Workflow für «${label}» wirklich KOMPLETT zurücksetzen?\n\n` +
        `Konsequenzen:\n` +
        `  • Alle berechneten / freigegebenen Akonto-Lohnzettel werden gelöscht\n` +
        `  • Bereits AUSBEZAHLTE Zahlungen werden auf STORNIERT gesetzt (Beleg bleibt)\n` +
        `  • Der Status der Periode geht zurück auf OFFEN\n` +
        `  • Audit-Eintrag wird geschrieben\n\n` +
        `Danach kann der GF die Akonto-Vorbereitung neu starten und lohnrelevante Edits werden wieder zugelassen.`;
    if (!confirm(warn)) return;

    // Walter-Vorgabe 19.05.2026: Pflicht-Bestätigung dass der DTA bei der
    // Bank gelöscht wurde — sonst läuft die Akonto-Zahlung doppelt. Backend
    // sperrt zusätzlich nach dem Klick-Datum (PAYOUT_DATE_REACHED).
    if (!confirm(
        'Hast du den DTA bei der Bank gelöscht oder storniert?\n\n' +
        '✓ JA → Reset wird durchgeführt.\n' +
        '✗ NEIN → Vorgang abbrechen.\n\n' +
        'Der Reset ist NUR am Tag der Auszahlung möglich — danach hat die Bank\n' +
        'die Zahlungen ausgeführt und die Periode ist betoniert.'
    )) return;

    const grund = prompt('Bitte den Grund für das Zurücksetzen erfassen (wird im Audit gespeichert):', '');
    if (grund === null) return;          // User hat abgebrochen
    if (!grund.trim()) { alert('Grund ist Pflichtfeld.'); return; }

    try {
        const res = await fetch('/api/akonto/workflow/reset-periode', {
            method:  'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body:    JSON.stringify({
                companyProfileId: cpId,
                year, month,
                grund: grund.trim()
            })
        });
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            // Zahldatum-Lock (PAYOUT_DATE_REACHED) klar erkennbar machen
            if (e.error === 'PAYOUT_DATE_REACHED') {
                alert('⛔ ' + e.message);
                return;
            }
            throw new Error(e.error || e.message || `HTTP ${res.status}`);
        }
        const r = await res.json();
        if (window.lohnEditLock) window.lohnEditLock.invalidateCache();
        showToast(`Akonto «${label}» zurückgesetzt (${r.gelöscht ?? 0} gelöscht, ${r.storniert ?? 0} storniert)`, 'success');
        perLoadPerioden();
    } catch(e) {
        alert('Zurücksetzen fehlgeschlagen: ' + e.message);
    }
}

// ── Definitiv-Rücknahme (Admin, Walter-Vorgabe 20.05.2026) ──────────────
// Holt eine abgeschlossene Periode zurück auf 'provisorisch_abgeschlossen'
// und entfernt die Lohnzettel aus den MA-Postfächern (genau wie in der ersten
// Version). Backend (/wieder-oeffnen, admin-only) ist gated: nur bis zum
// DTA-Zahldatum (auszahlungsdatum) — danach 409 PAYOUT_DATE_REACHED.
async function perWiederOeffnen(periodeId, label) {
    if (!confirm(
        `Definitiv-Abschluss für «${label}» zurücknehmen?\n\n` +
        `Konsequenzen:\n` +
        `  • Periode geht zurück auf "provisorisch abgeschlossen"\n` +
        `  • Die Lohnzettel werden aus den MA-Postfächern entfernt\n` +
        `  • HR-Bestätigungen bleiben erhalten — nur der DTA-Versand muss erneut\n` +
        `  • Audit-Eintrag wird geschrieben`
    )) return;

    // Pflicht-Bestätigung: DTA bei der Bank gelöscht — sonst Doppelzahlung.
    if (!confirm(
        'WICHTIG: Hast du den DTA bei der Bank gelöscht oder storniert?\n\n' +
        '✓ JA → Rücknahme wird durchgeführt.\n' +
        '✗ NEIN → Vorgang abbrechen.\n\n' +
        'Die Rücknahme ist NUR bis zum DTA-Zahldatum möglich — danach hat die\n' +
        'Bank die Löhne ausgeführt und die Periode ist betoniert.'
    )) return;

    try {
        const userId = (typeof currentUser !== 'undefined' && currentUser?.id) ? currentUser.id : 0;
        const res = await fetch(`/api/payroll-perioden/${periodeId}/wieder-oeffnen`, {
            method:  'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body:    JSON.stringify({ userId, bemerkung: 'Admin-Rücknahme via Lohnperioden-Modul' })
        });
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            if (e.error === 'PAYOUT_DATE_REACHED') { alert('⛔ ' + e.message); return; }
            throw new Error(e.message || e.error || `HTTP ${res.status}`);
        }
        if (window.lohnEditLock) window.lohnEditLock.invalidateCache();
        showToast(`Definitiv-Abschluss «${label}» zurückgenommen — Lohnzettel aus MA-Postfächern entfernt.`, 'success');
        perLoadPerioden();
    } catch(e) {
        alert('Rücknahme fehlgeschlagen: ' + e.message);
    }
}

async function perShowSnapshots(periodeId, label) {
    try {
        const res   = await fetch(`/api/payroll-perioden/${periodeId}/snapshots`, { headers: ah() });
        const snaps = res.ok ? await res.json() : [];

        if (snaps.length === 0) { alert(`Keine Lohnzettel in Periode «${label}».`); return; }

        let html = `<b>Lohnzettel — ${label}</b><br><br>`;
        html += `<table style="width:100%;border-collapse:collapse;font-size:13px">
            <thead><tr style="background:#f1f5f9">
                <th style="padding:6px 10px;text-align:left">Mitarbeiter</th>
                <th style="padding:6px 10px;text-align:right">Brutto</th>
                <th style="padding:6px 10px;text-align:right">Netto</th>
                <th style="padding:6px 10px;text-align:center">Finalisiert</th>
            </tr></thead><tbody>`;
        snaps.forEach(s => {
            html += `<tr style="border-top:1px solid #f1f5f9">
                <td style="padding:6px 10px">${s.name}</td>
                <td style="padding:6px 10px;text-align:right">CHF ${Number(s.brutto).toLocaleString('de-CH',{minimumFractionDigits:2,maximumFractionDigits:2})}</td>
                <td style="padding:6px 10px;text-align:right">CHF ${Number(s.netto).toLocaleString('de-CH',{minimumFractionDigits:2,maximumFractionDigits:2})}</td>
                <td style="padding:6px 10px;text-align:center">${s.isFinal ? '✓' : '–'}</td>
            </tr>`;
        });
        html += '</tbody></table>';

        // Einfaches Modal
        let modal = document.getElementById('perSnapshotModal');
        if (!modal) {
            modal = document.createElement('div');
            modal.id = 'perSnapshotModal';
            modal.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.4);z-index:8000;display:flex;align-items:center;justify-content:center';
            modal.onclick = e => { if (e.target === modal) modal.remove(); };
            document.body.appendChild(modal);
        }
        modal.innerHTML = `
            <div style="background:#fff;border-radius:12px;padding:28px;max-width:600px;width:100%;max-height:80vh;overflow-y:auto;box-shadow:0 20px 60px rgba(0,0,0,.3)">
                ${html}
                <div style="margin-top:16px;text-align:right">
                    <button class="btn btn-outline" onclick="document.getElementById('perSnapshotModal').remove()">Schliessen</button>
                </div>
            </div>`;
        modal.style.display = 'flex';
    } catch(e) { alert(e.message); }
}

