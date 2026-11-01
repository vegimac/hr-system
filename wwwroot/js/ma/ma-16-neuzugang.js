// ═══════════ „＋ Neuer MA aus easy@work" (Walter-Vorgabe 08.07.2026) ═══════════
// Einzelimport für die Mitarbeiter-Verwaltung: neuer MA wird IMMER zuerst in
// easy@work erfasst (CSV ist Vergangenheit), dann hier reingeholt. Zeigt NUR
// neue MA (NEW) + Änderungen bei AKTIVEN MA (UPDATE) — inaktive werden vom
// Backend nie angefasst (OnlyActive fest verdrahtet). Für admin/superuser/
// user(GF)/buchhaltung; GF nur für seine Filialen (Server prüft).
// Einstieg mit Wegwahl (Walter-Vorgabe 12.07.2026): normalerweise via API —
// ABER wenn der MA-Datensatz in easy@work in einem FREMDEN Restaurant gesperrt
// ist (Franchise-Wechsler: gehört einem anderen Betreiber), sieht unsere API
// ihn nicht. Dann bleibt der alte CSV-Import (easy@work-Export-Liste) der Weg.
//
// Personalnummern-Folge NEW (Walter 03.08.2026): ausgewählte NEU-Nummern müssen
// exakt max+1…max+N sein (anschliessend an höchste Nr. der Filiale + untereinander).
// Sonst gesamter Import gesperrt. Nur dieser Neuzugang-Pfad — nicht Admin-Sync.
let _empEasyNumberSeq = null; // { maxExisting, prefix } aus Preview

// ── Ladekarte im OneCrew-Stil (Walter 08.09.2026) ─────────────────────
// Sanduhr + warme Karte + laufender Balken. Einmal als HTML (für Modals)
// und einmal als Vollbild-Overlay (für den Einzel-MA-Sync ohne Modal).
function ocLoadingCardHtml(titel, text) {
    const esc = (v) => String(v ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
    return `
        <style>
            @keyframes ocAtSpin { 0% { transform: rotate(0deg) scale(1); } 50% { transform: rotate(180deg) scale(1.08); } 100% { transform: rotate(360deg) scale(1); } }
            @keyframes ocDots { 0% { content: ''; } 25% { content: '.'; } 50% { content: '..'; } 75%,100% { content: '...'; } }
            @keyframes ocBar { 0% { left: -40%; } 100% { left: 100%; } }
            .oc-load-dots::after { content: ''; animation: ocDots 1.6s steps(1) infinite; }
        </style>
        <div style="display:flex;align-items:center;gap:18px;background:linear-gradient(135deg,#f3f8ea 0%,#faf8f5 60%);border:1px solid #d7e4bd;border-radius:16px;padding:18px 20px;margin:8px 0 4px">
            <!-- Der Gag (Walter 08.09.2026): statt Sanduhr dreht sich das grüne @ von easy@work -->
            <div style="width:56px;height:56px;border-radius:50%;background:#eaf2da;display:flex;align-items:center;justify-content:center;flex-shrink:0;box-shadow:inset 0 0 0 2px #b7d17a">
                <img src="img/easyatwork-icon.webp?v=20260628a" alt="easy@work" style="width:34px;height:34px;object-fit:contain;animation:ocAtSpin 1.8s ease-in-out infinite">
            </div>
            <div style="min-width:0;flex:1">
                <div style="font-size:15px;font-weight:700;color:#3f3f3f">${esc(titel)}<span class="oc-load-dots"></span></div>
                <div style="font-size:12.5px;color:#6b6152;margin-top:3px;line-height:1.5">${esc(text)}</div>
                <div style="position:relative;height:6px;border-radius:3px;background:#ece9e2;overflow:hidden;margin-top:12px">
                    <div style="position:absolute;top:0;bottom:0;width:40%;border-radius:3px;background:linear-gradient(90deg,#b7d17a,#8db33a);animation:ocBar 1.4s ease-in-out infinite"></div>
                </div>
            </div>
        </div>`;
}

function ocLoadingOverlay(an, titel, text) {
    let ov = document.getElementById('ocLoadingOverlay');
    if (!an) { if (ov) ov.style.display = 'none'; return; }
    if (!ov) {
        ov = document.createElement('div');
        ov.id = 'ocLoadingOverlay';
        ov.style.cssText = 'position:fixed;inset:0;z-index:450;background:rgba(60,55,48,0.35);display:flex;align-items:flex-start;justify-content:center;padding:120px 20px';
        document.body.appendChild(ov);
    }
    ov.style.display = 'flex';
    ov.innerHTML = `<div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:18px;max-width:560px;width:100%;padding:12px 18px 14px;box-shadow:0 24px 60px rgba(60,55,48,0.22)">${ocLoadingCardHtml(titel, text)}</div>`;
}

// Walter 08.09.2026: keine Auswahlfrage API/CSV mehr — immer die API, und der
// Sync startet sofort. Der alte CSV-Importer bleibt über openImportTool('csv')
// erreichbar (Entwicklung → Import), nur nicht mehr von hier.
function empImportFromEasy() {
    empImportFromEasyApi();
}

async function empImportFromEasyApi() {
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
    if (!cpId) { alert('Bitte zuerst oben in der Sidebar eine Filiale wählen.'); return; }

    let ov = document.getElementById('empEasyImportModal');
    if (!ov) {
        ov = document.createElement('div');
        ov.id = 'empEasyImportModal';
        ov.style.cssText = 'position:fixed;inset:0;z-index:400;background:rgba(60,55,48,0.4);display:flex;align-items:flex-start;justify-content:center;padding:40px 20px';
        ov.onclick = e => { if (e.target === ov) ov.style.display = 'none'; };
        document.body.appendChild(ov);
    }
    ov.style.display = 'flex';
    ov.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:18px;max-width:720px;width:100%;max-height:calc(100vh - 80px);display:flex;flex-direction:column;box-shadow:0 24px 60px rgba(60,55,48,0.22)">
            <div style="display:flex;justify-content:space-between;align-items:center;padding:18px 22px 10px">
                <div>
                    <div style="font-size:17px;font-weight:700;color:#3f3f3f">＋ Neuer MA aus easy@work</div>
                    <div style="font-size:12.5px;color:#8b8b8b;margin-top:2px">Neue MA zuerst in easy@work erfassen — hier erscheinen sie als NEU. Aktive MA mit Änderungen als UPDATE. Inaktive werden nicht angefasst.</div>
                </div>
                <button onclick="document.getElementById('empEasyImportModal').style.display='none'" aria-label="Schliessen"
                        style="background:rgba(255,255,255,0.6);border:1px solid rgba(0,0,0,0.06);border-radius:10px;width:34px;height:34px;font-size:19px;cursor:pointer;color:#646464;flex-shrink:0">&times;</button>
            </div>
            <div id="empEasyImportBody" style="padding:6px 22px 12px;overflow-y:auto;flex:1">
                ${ocLoadingCardHtml('Hole Daten aus easy@work', 'Aktive Mitarbeitende und künftige Eintritte der Filiale werden mit OneCrew abgeglichen — Verträge, Lohn und Zivilstand inklusive. Das dauert je nach Filiale 10–40 Sekunden.')}
            </div>
            <div id="empEasyImportFoot" style="display:flex;gap:10px;justify-content:flex-end;align-items:center;padding:12px 22px 18px;border-top:1px solid rgba(139,139,139,0.2)"></div>
        </div>`;

    const body = document.getElementById('empEasyImportBody');
    const foot = document.getElementById('empEasyImportFoot');
    try {
        const r = await fetch('/api/easywork/neuzugang/preview', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyProfileId: cpId }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) {
            body.innerHTML = `<div style="background:#f3e7e7;border:1px solid #d8b8b8;color:#7a3f3f;border-radius:10px;padding:12px;font-size:13px">✗ ${esc(j.error || j.message || ('Fehler HTTP ' + r.status))}</div>`;
            return;
        }
        const rows = j.rows || [];
        _empEasyNumberSeq = j.numberSequence || null;
        if (!rows.length) {
            body.innerHTML = ((j.bestaetigen || []).length
                    ? ''
                    : `<div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:12px;font-size:13px">✓ Alles aktuell — keine neuen MA und keine Änderungen bei aktiven MA.</div>`)
                + _empEasyNotes(j, cpId);
            return;
        }
        body.innerHTML = `
            ${_empEasySeqBanner(j.numberSequence)}
            <div style="font-size:12.5px;color:#646464;margin-bottom:8px">${j.countNew} neu · ${j.countUpdate} mit Änderungen${j.countVertragFehlt ? ` · ${j.countVertragFehlt} ohne Vertrag in dieser Filiale` : ''} — abwählen, was (noch) nicht übernommen werden soll:</div>
            ${rows.map((x, i) => {
                const isNew = x.status === 'NEW';
                // Übertritt: MA schon erfasst, Vertrag dieser Filiale fehlt noch
                // (Walter-Bug 24.09.2026) — eigene Marke, damit man ihn nicht mit
                // einer Stammdaten-Änderung verwechselt.
                const badge = x.vertragFehlt
                    ? '<span style="font-size:10.5px;font-weight:700;background:#fdf1dc;color:#7c5a10;border:1px solid #f3d9a4;border-radius:10px;padding:2px 8px">VERTRAG FEHLT</span>'
                    : isNew
                    ? '<span style="font-size:10.5px;font-weight:700;background:#e7f0e7;color:#3f5540;border:1px solid #b8ccb8;border-radius:10px;padding:2px 8px">NEU</span>'
                    : '<span style="font-size:10.5px;font-weight:700;background:#ece9e2;color:#6b6152;border:1px solid #d0c8b8;border-radius:10px;padding:2px 8px">UPDATE</span>';
                const detail = (isNew || x.vertragFehlt)
                    ? esc(x.reason || x.employmentInfo || 'wird neu angelegt')
                    : esc((x.changedFields || []).join(', ') || x.reason || 'Änderungen');
                const reentry = x.possibleReentry
                    ? `<div style="font-size:11.5px;color:#92400e;margin-top:2px">⚠ Möglicher Wiedereintritt (bestehende Nr. ${esc(x.reentryEmployeeNumber || '?')})</div>` : '';
                return `
                <label style="display:flex;gap:10px;align-items:flex-start;padding:9px 10px;border:1px solid rgba(139,139,139,0.22);border-radius:10px;margin-bottom:6px;background:rgba(255,255,255,0.55);cursor:pointer">
                    <input type="checkbox" class="empEasyRow" data-number="${esc(x.number || '')}" data-status="${esc(x.status || '')}" checked style="margin-top:3px;width:15px;height:15px" onchange="_empEasyCount()">
                    <div style="min-width:0">
                        <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap">
                            <span style="font-weight:600;color:#3f3f3f;font-size:13.5px">${esc(((x.firstName||'') + ' ' + (x.lastName||'')).trim())}</span>
                            <span style="color:#8b8b8b;font-size:12px;font-family:monospace">${esc(x.number || '')}</span>
                            ${badge}
                        </div>
                        <div style="font-size:12px;color:#646464;margin-top:2px;word-break:break-word">${detail}</div>
                        ${reentry}
                    </div>
                </label>`;
            }).join('')}
            <div id="empEasySeqWarn" style="display:none;background:#f3e7e7;border:1px solid #d8b8b8;color:#7a3f3f;border-radius:10px;padding:10px 12px;font-size:12.5px;margin-top:8px;white-space:pre-wrap"></div>
            <label id="empEasySeqForceWrap" style="display:none;align-items:center;gap:8px;margin-top:8px;cursor:pointer;font-size:12.5px;color:#3f3f3f">
                <input type="checkbox" id="empEasySeqForce" style="width:15px;height:15px" onchange="_empEasyCount()">
                <span style="display:inline-flex;align-items:center;gap:6px;border:1px solid #d8b8b8;background:#fff;border-radius:999px;padding:3px 10px;font-weight:600">Trotzdem importieren</span>
                <span style="color:#646464">Nummer in easy@work geblockt oder Lücke wird gefüllt — freie Nummer trotz Reihenfolge übernehmen</span>
            </label>
            ${_empEasyNotes(j, cpId)}`;
        foot.innerHTML = `
            <button onclick="document.getElementById('empEasyImportModal').style.display='none'"
                    style="padding:9px 16px;border:1px solid rgba(139,139,139,0.35);border-radius:12px;background:rgba(255,255,255,0.5);cursor:pointer;font-size:13px;color:#646464">Abbrechen</button>
            <button id="empEasyCommitBtn" onclick="empEasyImportCommit(${cpId})"
                    style="padding:9px 18px;border:none;border-radius:12px;background:#3f3f3f;color:#fff;cursor:pointer;font-size:13.5px;font-weight:600">Ausgewählte importieren (${rows.length})</button>`;
        _empEasyCount();
    } catch (e) {
        body.innerHTML = `<div style="background:#f3e7e7;border:1px solid #d8b8b8;color:#7a3f3f;border-radius:10px;padding:12px;font-size:13px">Netzwerkfehler: ${esc(e.message)}</div>`;
    }
}

function _empEasySeqBanner(seq) {
    if (!seq) return '';
    const max = seq.maxExisting != null ? String(seq.maxExisting) : '—';
    const next = seq.maxExisting != null ? String(Number(seq.maxExisting) + 1) : 'erste Nummer der Filiale';
    return `<div style="background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.28);border-radius:10px;padding:10px 12px;font-size:12.5px;color:#3f3f3f;margin-bottom:10px">
        Letzte Nr. in OneCrew: <b style="font-family:monospace">${esc(max)}</b>
        · Nächste freie Nummer: <b style="font-family:monospace">${esc(next)}</b>${seq.maxExisting != null ? ', dann +1 …' : ''}.
        Weicht eine NEU-Nummer davon ab, erscheint eine Warnung — der Import bleibt möglich.
    </div>`;
}

/** Client-Spiegel der Server-Regel EmployeeNumberSequenceGuard (nur UX; Server entscheidet hart). */
function _empEasyValidateNewSequence(newNumbers) {
    if (!newNumbers || !newNumbers.length) return { ok: true, message: '' };
    const parsed = [];
    for (const raw of newNumbers) {
        const n = String(raw || '').trim();
        if (!/^\d+$/.test(n)) return { ok: false, message: `Personalnummer «${n}» ist nicht rein numerisch — Import gesperrt.` };
        parsed.push(Number(n));
    }
    if (new Set(parsed).size !== parsed.length)
        return { ok: false, message: 'Doppelte Personalnummern in der Auswahl — Import gesperrt.' };
    parsed.sort((a, b) => a - b);
    const max = _empEasyNumberSeq && _empEasyNumberSeq.maxExisting != null
        ? Number(_empEasyNumberSeq.maxExisting) : null;
    const expected = [];
    if (max != null) {
        for (let i = 1; i <= parsed.length; i++) expected.push(max + i);
    } else {
        for (let i = 0; i < parsed.length; i++) expected.push(parsed[0] + i);
    }
    const same = parsed.length === expected.length && parsed.every((v, i) => v === expected[i]);
    if (same) return { ok: true, message: '' };
    // Pille «trotzdem importieren» (Walter 08.09.2026): easy@work blockiert manchmal
    // Nummern → Lücke nach OBEN ist ok. Nach unten bleibt gesperrt (Vertipper).
    // Präzisiert 08.09.2026: auch Lücken nach UNTEN dürfen gefüllt werden
    // (580108 nach 580109) — mit der Pille ist jede freie Nummer erlaubt.
    const forceEl = document.getElementById('empEasySeqForce');
    if (forceEl && forceEl.checked) return { ok: true, message: '', forced: true };
    const lueckeMoeglich = true;
    const letzte = max != null ? String(max) : '(keine)';
    const erwartetTxt = expected.join(', ');
    const erhaltenTxt = parsed.join(', ');
    const message = parsed.length === 1
        ? `Personalnummer muss direkt anschliessen. Letzte Nr. in OneCrew: ${letzte}. Erwartet: ${erwartetTxt}. Erhalten: ${erhaltenTxt}.`
        : `Neue Personalnummern müssen fortlaufend an die letzte Nr. und untereinander anschliessen. Letzte Nr. in OneCrew: ${letzte}. Erwartet: ${erwartetTxt}. Erhalten: ${erhaltenTxt}.`;
    return { ok: false, message, lueckeMoeglich };
}

function _empEasyNotes(j, cpId) {
    let html = typeof pdNeuzugangHtml === 'function' ? pdNeuzugangHtml(j, cpId) : '';
    // «MA ohne Lohn» (Supervisor) werden nie importiert — das aber SAGEN,
    // nicht stillschweigend weglassen (Walter 01.09.2026).
    if (j.phantomSkipped > 0)
        html += `<div style="background:#f1f5f9;border:1px solid #cbd5e1;color:#475569;border-radius:10px;padding:8px 12px;font-size:12px;margin-top:8px">`
              + `${j.phantomSkipped} «MA ohne Lohn» (Supervisor) nicht aufgeführt — diese werden nie importiert.</div>`;
    if (j.conflicts && j.conflicts.length)
        html += `<div style="background:#fdf6dd;border:1px solid #e4d28a;color:#6b5a1f;border-radius:10px;padding:10px 12px;font-size:12px;margin-top:8px;white-space:pre-wrap">⚠ Konflikte (werden NICHT importiert — bitte Admin prüfen lassen):\n${j.conflicts.map(escapeHtml).join('\n')}</div>`;
    return html;
}

function _empEasyCount() {
    const checked = Array.from(document.querySelectorAll('#empEasyImportModal .empEasyRow:checked'));
    const n = checked.length;
    const newNums = checked.filter(el => el.dataset.status === 'NEW').map(el => el.dataset.number).filter(Boolean);
    const seq = _empEasyValidateNewSequence(newNums);
    const warn = document.getElementById('empEasySeqWarn');
    if (warn) {
        // Walter 09.09.2026: Abweichung von der Folge ist nur noch eine Warnung.
        if (!seq.ok) { warn.style.display = 'block'; warn.style.background = '#fdf6dd'; warn.style.borderColor = '#e4d28a'; warn.style.color = '#6b5a1f'; warn.textContent = '⚠ ' + seq.message + '\nDer Import ist trotzdem möglich — bitte nur prüfen, ob die Nummer so gewollt ist.'; }
        else { warn.style.display = 'none'; warn.textContent = ''; }
    }
    // Pille nur zeigen, wenn eine Lücke nach oben vorliegt (oder sie schon gesetzt ist).
    const forceWrap = document.getElementById('empEasySeqForceWrap');
    const forceEl = document.getElementById('empEasySeqForce');
    if (forceWrap) {
        forceWrap.style.display = 'none';   // Pille «Trotzdem importieren» überflüssig — Folge ist nur noch Warnung
        if (forceEl) forceEl.checked = false;
    }
    const btn = document.getElementById('empEasyCommitBtn');
    if (btn) {
        btn.textContent = `Ausgewählte importieren (${n})`;
        const block = n === 0;   // Folge-Abweichung sperrt nicht mehr (Walter 09.09.2026)
        btn.disabled = block;
        btn.style.opacity = block ? '0.5' : '1';
    }
}

async function empEasyImportCommit(cpId) {
    const numbers = Array.from(document.querySelectorAll('#empEasyImportModal .empEasyRow:checked'))
        .map(el => el.dataset.number).filter(Boolean);
    if (!numbers.length) return;
    const btn = document.getElementById('empEasyCommitBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Importiere …'; }
    const body = document.getElementById('empEasyImportBody');
    // Ladekarte während des Imports (Walter 08.09.2026) — vorherigen Inhalt
    // merken, damit bei einem Fehler die Liste wieder da ist.
    const vorher = body ? body.innerHTML : '';
    if (body) body.innerHTML = ocLoadingCardHtml(
        numbers.length === 1 ? 'Importiere 1 Mitarbeiter aus easy@work' : `Importiere ${numbers.length} Mitarbeitende aus easy@work`,
        'Stammdaten, Verträge, Lohn und Zivilstand werden angelegt und die Personalnummern geprüft. Bitte das Fenster offen lassen.');
    try {
        const r = await fetch('/api/easywork/neuzugang/commit', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                companyProfileId: cpId, selectedNumbers: numbers,
                trotzdemImportieren: !!(document.getElementById('empEasySeqForce') || {}).checked,
            }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok || j.blocked) {
            let msg;
            if (j.error === 'NUMBER_SEQUENCE_INVALID' || j.error === 'NUMBER_INVALID') {
                msg = 'Import gesperrt — Personalnummer:\n' + (j.message || '');
            } else if (j.blocked) {
                msg = 'Import blockiert — Personalnummern-Kollision:\n' + (j.numberConflicts || []).join('\n');
            } else {
                msg = j.message || j.error || ('Fehler HTTP ' + r.status);
            }
            body.innerHTML = vorher + `<div style="background:#f3e7e7;border:1px solid #d8b8b8;color:#7a3f3f;border-radius:10px;padding:12px;font-size:13px;white-space:pre-wrap;margin-top:8px">✗ ${esc(msg)}</div>`;
            _empEasyCount();
            const btnN = document.getElementById('empEasyCommitBtn');
            if (btnN) { btnN.disabled = false; btnN.textContent = 'Erneut versuchen'; }
            return;
        }
        const skipped = (j.skippedContracts && j.skippedContracts.length)
            ? `<div style="background:#fdf6dd;border:1px solid #e4d28a;color:#6b5a1f;border-radius:10px;padding:10px 12px;font-size:12px;margin-top:8px;white-space:pre-wrap">⚠ Nicht importierte Verträge (Periode abgeschlossen):\n${j.skippedContracts.map(escapeHtml).join('\n')}</div>` : '';
        const seqWarn = j.numberSequenceWarning
            ? `<div style="background:#fdf6dd;border:1px solid #e4d28a;color:#6b5a1f;border-radius:10px;padding:10px 12px;font-size:12px;margin-top:8px;white-space:pre-wrap">⚠ Personalnummern-Folge: ${escapeHtml(j.numberSequenceWarning)}</div>` : '';
        body.innerHTML = `
            <div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:12px;font-size:13px">
                ✓ Import abgeschlossen — ${j.inserted || 0} neu angelegt, ${j.updated || 0} aktualisiert.
            </div>${seqWarn}${skipped}`;
        const foot = document.getElementById('empEasyImportFoot');
        if (foot) foot.innerHTML = `
            <button onclick="document.getElementById('empEasyImportModal').style.display='none'"
                    style="padding:9px 18px;border:none;border-radius:12px;background:#3f3f3f;color:#fff;cursor:pointer;font-size:13.5px;font-weight:600">Schliessen</button>`;
        // MA-Liste auffrischen — der neue MA soll sofort links erscheinen.
        if (typeof invalidateEmployeeLookupCache === 'function') invalidateEmployeeLookupCache();
        if (typeof loadMitarbeiterList === 'function') await loadMitarbeiterList();
    } catch (e) {
        body.innerHTML = `<div style="background:#f3e7e7;border:1px solid #d8b8b8;color:#7a3f3f;border-radius:10px;padding:12px;font-size:13px">Netzwerkfehler: ${esc(e.message)}</div>`;
        if (btn) { btn.disabled = false; btn.textContent = 'Erneut versuchen'; }
    }
}

// HR: alle aktiven Vertrags-Links dieses Vertrags widerrufen (Walter 07.07.2026).
async function contractShareRevoke(employmentId) {
    if (!employmentId) return;
    if (!(await liquidConfirm('Alle aktiven Vertrags-Links dieses Vertrags sofort ungültig machen?\n\nBereits verschickte Links zeigen danach «Link nicht mehr gültig».'))) return;
    // Feedback-Box im AKTIVEN Tab suchen (Uebersicht ODER Personal-Strip —
    // Klasse statt ID, damit beide Instanzen erlaubt sind, Walter 17.07.2026).
    const box = document.querySelector('.emp-tab-content.active .contractShareBox')
        || document.querySelector('.contractShareBox');
    try {
        const res = await fetch('/api/contract-share/revoke', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ employmentId }),
        });
        const j = await res.json().catch(() => ({}));
        if (!res.ok || !j.ok) {
            if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">✗ ${esc(j.error || ('Fehler HTTP ' + res.status))}</div>`;
            return;
        }
        if (box) box.innerHTML = `<div style="background:#e7f0e7;border:1px solid #b8ccb8;color:#3f5540;border-radius:10px;padding:12px;font-size:13px">✓ ${j.revoked === 0 ? 'Kein aktiver Link vorhanden.' : `${j.revoked} Link(s) widerrufen — verschickte Links sind ab sofort ungültig.`}</div>`;
    } catch (e) {
        if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

// HR: öffentlichen Vertrags-Link + SMS-Text erzeugen (Copy-Variante).
// Wird vom SMS-Button nicht mehr aufgerufen (Direktversand oben), bleibt
// als Code erhalten für manuelles Kopieren/Verlinken.
async function contractShareCreate(employeeId, employmentId) {
    if (!employeeId && !employmentId) return;
    // Feedback-Box im AKTIVEN Tab suchen (Uebersicht ODER Personal-Strip —
    // Klasse statt ID, damit beide Instanzen erlaubt sind, Walter 17.07.2026).
    const box = document.querySelector('.emp-tab-content.active .contractShareBox')
        || document.querySelector('.contractShareBox');
    if (box) box.innerHTML = '<div style="color:#8b8b8b;font-size:13px;padding:8px 0">⏳ Link wird erzeugt …</div>';
    try {
        const res = await fetch('/api/contract-share', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            // employmentId = genau dieser Vertrag; sonst (Fallback) der aktive.
            body: JSON.stringify(employmentId ? { employmentId } : { employeeId }),
        });
        const j = await res.json().catch(() => ({}));
        if (!res.ok) {
            if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">${esc(j.error || j.message || ('Fehler HTTP ' + res.status))}</div>`;
            return;
        }
        const exp = j.expiresAt ? new Date(j.expiresAt) : null;
        const expStr = exp && !isNaN(exp.getTime())
            ? exp.toLocaleDateString('de-CH')
            : '';
        if (box) box.innerHTML = `
            <div style="background:rgba(255,255,255,0.55);border:1px solid rgba(255,255,255,0.62);box-shadow:0 6px 20px rgba(60,55,48,0.14);border-radius:14px;padding:14px 16px">
                <div style="font-weight:700;color:#3f3f3f;font-size:14px;margin-bottom:8px">📄 Arbeitsvertrag-Link erstellt</div>

                <div style="font-size:12px;color:#8b8b8b;margin-bottom:4px">Öffentlicher Link (ohne Login):</div>
                <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-bottom:12px">
                    <input id="contractShareLinkInput" readonly value="${esc(j.url)}"
                        style="flex:1 1 260px;min-width:0;font-size:12px;padding:8px 10px;border:1px solid rgba(60,55,48,0.18);border-radius:9px;background:#faf8f5;color:#3f3f3f">
                    <button onclick="contractShareCopy('contractShareLinkInput')"
                        style="white-space:nowrap;background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 14px;font-size:13px;font-weight:600;cursor:pointer">Kopieren</button>
                    <a href="${esc(j.url)}" target="_blank" rel="noopener"
                        style="white-space:nowrap;text-decoration:none;background:rgba(255,255,255,0.58);color:#3f3f3f;border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:8px 14px;font-size:13px;font-weight:600">Öffnen</a>
                </div>

                <div style="font-size:12px;color:#8b8b8b;margin-bottom:4px">SMS-Text (zum Kopieren):</div>
                <div style="display:flex;gap:8px;align-items:flex-start;flex-wrap:wrap;margin-bottom:10px">
                    <textarea id="contractShareSmsInput" readonly rows="2"
                        style="flex:1 1 260px;min-width:0;font-size:12px;padding:8px 10px;border:1px solid rgba(60,55,48,0.18);border-radius:9px;background:#faf8f5;color:#3f3f3f;resize:vertical;white-space:pre-wrap">${esc(j.smsText || '')}</textarea>
                    <button onclick="contractShareCopy('contractShareSmsInput')"
                        style="white-space:nowrap;background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 14px;font-size:13px;font-weight:600;cursor:pointer">SMS kopieren</button>
                </div>

                <div style="font-size:12px;color:#8b8b8b;line-height:1.5">
                    ${expStr ? `Gültig bis ${esc(expStr)}. ` : ''}SMS-Direktversand folgt später — Text + Link vorerst manuell senden.
                </div>
            </div>`;
        box?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    } catch (e) {
        if (box) box.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;border-radius:10px;padding:12px;font-size:13px">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

function contractShareCopy(inputId) {
    const el = document.getElementById(inputId);
    if (!el) return;
    el.select();
    navigator.clipboard?.writeText(el.value).catch(() => { try { document.execCommand('copy'); } catch (_) {} });
}

// HR: Onboarding-/Reset-QR erzeugen (MA scannt → setzt Passwort → direkt eingeloggt).
async function postfachSetupQr(employeeId) {
    if (!employeeId) return;
    try {
        const res = await fetch(`/api/postfach-setup/create/${employeeId}`, { method: 'POST', headers: ah() });
        const j = await res.json().catch(() => ({}));
        if (!res.ok) { alert(j.error || j.message || 'Fehler beim Erzeugen des QR-Codes.'); return; }
        showSetupQrModal(j, employeeId);
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

function showSetupQrModal(j, employeeId) {
    let ov = document.getElementById('setupQrModal');
    if (!ov) {
        ov = document.createElement('div');
        ov.id = 'setupQrModal';
        ov.style.cssText = 'position:fixed;inset:0;z-index:400;background:rgba(15,23,42,0.5);display:flex;align-items:center;justify-content:center;padding:20px';
        ov.onclick = e => { if (e.target === ov) ov.style.display = 'none'; };
        document.body.appendChild(ov);
    }
    const bis = j.expiresAt ? new Date(j.expiresAt).toLocaleString('de-CH', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '';
    ov.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:20px;max-width:420px;width:100%;padding:24px;box-shadow:0 24px 60px rgba(60,55,48,0.22);text-align:center">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px">
                <h2 style="margin:0;font-size:18px;font-weight:700;color:#3f3f3f">Postfach einrichten</h2>
                <button onclick="document.getElementById('setupQrModal').style.display='none'" aria-label="Schliessen" style="background:rgba(255,255,255,0.6);border:1px solid rgba(0,0,0,0.06);border-radius:10px;width:34px;height:34px;font-size:19px;cursor:pointer;color:#646464">&times;</button>
            </div>
            <p style="font-size:13px;color:#646464;margin:0 0 16px;text-align:left;line-height:1.5">${esc(j.firstName || 'Der Mitarbeitende')} scannt diesen QR-Code mit der Handykamera und setzt direkt sein Passwort — danach ist er automatisch in seinem Postfach. Gültig bis ${esc(bis)}.</p>
            <img src="${esc(j.qrPng)}" alt="QR-Code" style="width:240px;height:240px;image-rendering:pixelated;background:#fff;border:1px solid rgba(255,255,255,0.62);border-radius:14px;padding:8px;box-shadow:0 8px 24px rgba(60,55,48,0.12)">
            <div style="display:flex;gap:8px;align-items:center;margin-top:16px">
                <input id="setupQrLink" readonly value="${esc(j.url)}" style="flex:1;min-width:0;font-size:12px;padding:10px 12px;border:1px solid rgba(0,0,0,0.10);border-radius:12px;background:rgba(255,255,255,0.55);color:#3f3f3f">
                <button style="background:#3f3f3f;color:#faf8f5;border:0;white-space:nowrap;font-weight:600;font-size:13px;padding:10px 16px;border-radius:12px;cursor:pointer;box-shadow:0 6px 16px rgba(60,55,48,0.18)" onclick="(function(){var e=document.getElementById('setupQrLink');e.select();navigator.clipboard&&navigator.clipboard.writeText(e.value);})()">Link kopieren</button>
            </div>
            <!-- App-Link per E-Mail (Walter 18.08.2026) — für bestehende MA aus der Ferne -->
            <div style="margin-top:14px;border-top:1px solid rgba(60,55,48,0.12);padding-top:12px;text-align:left">
                <input id="setupQrDokWunsch" placeholder="Benötigtes Dokument (optional) — z.B. Ausweis Vorder- und Rückseite"
                       style="width:100%;box-sizing:border-box;font-size:12.5px;padding:9px 12px;border:1px solid rgba(0,0,0,0.10);border-radius:12px;background:#fff;color:#3f3f3f;margin-bottom:8px">
                <button id="setupQrMailBtn" onclick="pfSendAppLinkMail(${employeeId})"
                        style="background:transparent;border:1px solid rgba(60,55,48,0.25);border-radius:12px;padding:9px 16px;font-size:13px;font-weight:600;cursor:pointer;color:#3f3f3f;width:100%">📧 Link per E-Mail an den MA senden</button>
                <div id="setupQrMailStatus" style="font-size:12px;color:#6b6152;margin-top:6px"></div>
                <div id="setupQrTokenStatus" style="font-size:12px;color:#6b6152;margin-top:8px;border-top:1px dashed rgba(60,55,48,0.15);padding-top:8px">Lade Status…</div>
            </div>
        </div>`;
    ov.style.display = 'flex';
    pfLoadSetupStatus(employeeId);
}

// Status «gesendet / geöffnet / eingerichtet» des letzten Links (Walter 18.08.2026)
async function pfLoadSetupStatus(employeeId) {
    const el = document.getElementById('setupQrTokenStatus');
    if (!el || !employeeId) return;
    const fmt = d => d ? new Date(d).toLocaleString('de-CH', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : null;
    try {
        const r = await fetch(`/api/postfach-setup/status/${employeeId}`, { headers: ah() });
        const j = r.ok ? await r.json() : null;
        if (!j || !j.hasToken) { el.innerHTML = 'Noch kein Link erzeugt.'; return; }
        const teile = [`Link erzeugt: <b>${fmt(j.createdAt)}</b>`];
        teile.push(j.openedAt ? `✅ geöffnet ${fmt(j.openedAt)}` : '⏳ noch nicht geöffnet');
        teile.push(j.usedAt ? `✅ eingerichtet ${fmt(j.usedAt)}` : '⏳ Passwort noch nicht gesetzt');
        if (j.lastLoginAt) teile.push(`letzter App-Login ${fmt(j.lastLoginAt)}`);
        el.innerHTML = teile.join(' · ')
            + ` &nbsp;<a href="#" onclick="pfLoadSetupStatus(${employeeId});return false" style="color:#6b7280">aktualisieren</a>`;
    } catch { el.textContent = 'Status nicht verfügbar.'; }
}

// App-Link per E-Mail an bestehenden MA (Walter 18.08.2026). Solange der
// TESTMODUS im Backend aktiv ist, wird die Mail an Walter umgeleitet —
// der Status-Text weist das aus.
async function pfSendAppLinkMail(employeeId) {
    const btn = document.getElementById('setupQrMailBtn');
    const st = document.getElementById('setupQrMailStatus');
    if (btn) { btn.disabled = true; btn.textContent = 'Sende E-Mail…'; }
    try {
        const r = await fetch(`/api/postfach-setup/send-app-link/${employeeId}`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ dokumentWunsch: document.getElementById('setupQrDokWunsch')?.value || null }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(j.error || j.message || ('HTTP ' + r.status));
        if (st) st.innerHTML = j.redirected
            ? `✅ Gesendet an <b>${esc(j.sentTo)}</b> <span style="color:#92400e">(TESTMODUS — eigentlicher Empfänger: ${esc(j.empEmail || '–')})</span>`
            : `✅ Gesendet an <b>${esc(j.sentTo)}</b>`;
        if (btn) btn.textContent = '📧 Erneut senden';
    } catch (e) {
        if (st) st.innerHTML = `<span style="color:#b91c1c">Fehler: ${esc(e.message)}</span>`;
        if (btn) btn.textContent = '📧 Link per E-Mail an den MA senden';
    } finally {
        if (btn) btn.disabled = false;
    }
}

// Kleiner App-Status-Chip im MA-Header (Walter 18.08.2026)
async function pfLoadAppChip(employeeId) {
    const el = document.getElementById('empAppChip');
    if (!el) return;
    try {
        const r = await fetch(`/api/postfach-setup/status/${employeeId}`, { headers: ah() });
        if (!r.ok) return;
        const j = await r.json();
        const fmt = d => d ? new Date(d).toLocaleDateString('de-CH') : '';
        if (j.usedAt || j.lastLoginAt) {
            el.style.background = '#dcfce7'; el.style.color = '#166534';
            el.innerHTML = `📱 App eingerichtet${j.lastLoginAt ? ' · Login ' + fmt(j.lastLoginAt) : ''}`;
        } else if (j.hasToken) {
            el.style.background = '#fef3c7'; el.style.color = '#92400e';
            el.innerHTML = j.openedAt
                ? `📱 Link geöffnet ${fmt(j.openedAt)} — noch nicht eingerichtet`
                : `📱 Link gesendet ${fmt(j.createdAt)} — noch nicht geöffnet`;
        } else {
            el.style.background = 'rgba(60,55,48,0.08)'; el.style.color = '#8b8578';
            el.innerHTML = '📱 App nicht eingerichtet';
        }
        el.style.display = '';
    } catch { /* Chip bleibt versteckt */ }
}

// HR: alle Face-ID-/Passkey-Geräte eines MA löschen (z.B. bei Geräteverlust).
async function faceIdAdminReset(employeeId) {
    if (!employeeId) return;
    let count = null;
    try {
        const r = await fetch(`/api/webauthn/admin/by-employee/${employeeId}`, { headers: ah() });
        if (r.ok) count = (await r.json())?.count;
    } catch (e) { /* egal, Löschung trotzdem anbieten */ }
    const msg = (count != null)
        ? `Face ID zurücksetzen?\n\nDer MA hat ${count} aktivierte(s) Gerät(e). Alle werden entfernt — der MA meldet sich dann wieder mit Passwort an und kann Face ID neu aktivieren.`
        : 'Face ID zurücksetzen?\n\nAlle aktivierten Geräte dieses MA werden entfernt.';
    if (!(await liquidConfirm(msg))) return;
    try {
        const res = await fetch(`/api/webauthn/admin/by-employee/${employeeId}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Zurücksetzen.'); return; }
        const d = await res.json().catch(() => ({}));
        alert(`Face ID zurückgesetzt (${d.removed ?? 0} Gerät(e) entfernt).`);
        await loadPostfachAccountBlock(employeeId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function postfachUnlock(employeeId) {
    if (!employeeId) return;
    try {
        const res = await fetch(`/api/employees/${employeeId}/postfach-account/unlock`, {
            method: 'POST',
            headers: ah(),
        });
        if (!res.ok) { alert('Fehler beim Aufheben der Sperre.'); return; }
        await loadPostfachAccountBlock(employeeId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Bulk: Postfach für alle aktiven MA erstellen (einmaliger Backfill nach
// Phase-1-Deploy). Idempotent — bestehende Accounts werden übersprungen.
async function postfachBackfillRun() {
    if (!(await liquidConfirm(
        'Postfach-Backfill ausführen?\n\n' +
        'Für alle aktiven Mitarbeiter ohne Postfach-Account wird einer angelegt.\n' +
        '  • Username = Personalnummer\n' +
        '  • Initial-Passwort = Personalnummer (= Username)\n' +
        '  • Beim ersten Login muss der MA das Passwort zwingend wechseln.\n\n' +
        'Bestehende Accounts bleiben unverändert. Vorgang ist idempotent und ' +
        'kann beliebig oft ausgeführt werden.'
    ))) return;

    try {
        const res = await fetch('/api/admin/postfach-backfill', {
            method: 'POST',
            headers: ah(),
        });
        if (!res.ok) {
            if (res.status === 401 || res.status === 403) {
                alert('Backfill ist nur für Admins möglich.');
                return;
            }
            const j = await res.json().catch(() => null);
            alert(j?.message || 'Fehler beim Backfill.');
            return;
        }
        const data = await res.json();
        const errLines = (data.errors || []).map(e => `• ${esc(e)}`).join('<br>');
        alert(
            `Postfach-Backfill abgeschlossen:\n\n` +
            `MA geprüft:        ${data.scanned}\n` +
            `Accounts erstellt: ${data.created}\n` +
            `Übersprungen:      ${data.skipped}\n` +
            (errLines ? `Fehler: ${data.errors.length}\n` : 'Keine Fehler.')
        );
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Modal das nach Reset einmalig das Initial-Passwort zeigt — mit Copy-Button.
// Nach Schliessen ist das Passwort weg (nicht persistent gespeichert ausser
// als bcrypt-Hash). Walter notiert oder schickt es dem MA.
function showInitialPasswordModal(username, password) {
    let overlay = document.getElementById('initialPwOverlay');
    if (!overlay) {
        overlay = document.createElement('div');
        overlay.id = 'initialPwOverlay';
        overlay.style = 'display:none;position:fixed;inset:0;background:rgba(15,23,42,0.55);z-index:5000;align-items:center;justify-content:center;padding:20px';
        document.body.appendChild(overlay);
    }
    overlay.innerHTML = `
        <div style="background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);border-radius:14px;width:100%;max-width:480px;box-shadow:0 24px 64px rgba(0,0,0,0.28);overflow:hidden">
            <div style="padding:18px 22px;background:linear-gradient(180deg,#ece9e2,#fff);border-bottom:1px solid #e2e8f0;display:flex;align-items:center;gap:10px">
                <span style="font-size:20px">📬</span>
                <div>
                    <div style="font-weight:700;font-size:15px;color:#0f172a">Initial-Passwort gesetzt</div>
                    <div style="font-size:12px;color:#64748b">Einmalig sichtbar — bitte jetzt notieren oder dem MA übergeben</div>
                </div>
            </div>
            <div style="padding:20px 22px;display:flex;flex-direction:column;gap:14px">
                <div>
                    <div style="font-size:11.5px;font-weight:600;color:#475569;margin-bottom:5px">Benutzername</div>
                    <div style="font-family:ui-monospace,Menlo,Consolas,monospace;font-size:14px;color:#0f172a;background:#f1f5f9;padding:8px 12px;border-radius:8px">${esc(username)}</div>
                </div>
                <div>
                    <div style="font-size:11.5px;font-weight:600;color:#475569;margin-bottom:5px">Initial-Passwort</div>
                    <div style="display:flex;gap:6px">
                        <div style="flex:1;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:15px;font-weight:600;color:#0f172a;background:#f1f5f9;padding:8px 12px;border-radius:8px">${esc(password)}</div>
                        <button onclick="navigator.clipboard.writeText('${esc(password)}').then(()=>this.textContent='✓ Kopiert')"
                                style="background:#3f3f3f;color:#fff;border:none;padding:0 14px;border-radius:8px;cursor:pointer;font-size:12.5px;font-weight:600;white-space:nowrap">Kopieren</button>
                    </div>
                </div>
                <div style="background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:10px 12px;font-size:12px;color:#92400e;line-height:1.5">
                    <strong>Wichtig:</strong> Beim ersten Login muss der MA dieses Passwort wechseln. Sobald dieses Fenster geschlossen ist, kann das Passwort nicht mehr eingesehen werden — nur ein erneuter Reset ist möglich.
                </div>
            </div>
            <div style="padding:14px 22px;border-top:1px solid #f1f5f9;display:flex;justify-content:flex-end">
                <button onclick="document.getElementById('initialPwOverlay').style.display='none'"
                        style="background:#0f172a;color:#fff;border:none;padding:9px 22px;border-radius:8px;cursor:pointer;font-size:13px;font-weight:600">Schliessen</button>
            </div>
        </div>`;
    overlay.style.display = 'flex';
}

async function deleteEmployeeAddress(addrId) {
    if (!selectedEmployeeId || !addrId) return;
    if (!(await liquidConfirm('Adresse wirklich löschen?'))) return;

    // Sofort aus der Liste nehmen — kein «Geister»-Eintrag bis der Reload fertig ist.
    document.querySelectorAll(`.emp-addr-row[data-addr-id="${addrId}"]`)
        .forEach(row => row.remove());
    _otherAddressesContainers().forEach(el => {
        if (!el.querySelector('.emp-addr-row')) el.innerHTML = '';
    });
    closeEmployeeAddressModal();

    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/addresses/${addrId}`, {
            method: 'DELETE', headers: ah()
        });
        if (!res.ok) {
            let msg = 'Fehler beim Löschen.';
            try { const j = await res.json(); msg = j.error || j.message || msg; } catch {}
            alert(msg);
            await loadEmployeeAddressesTab(selectedEmployeeId);
            return;
        }
        await loadEmployeeAddressesTab(selectedEmployeeId);
        // Familie-Modal: Dropdown/Felder aktualisieren, falls offen.
        if (document.getElementById('fmAddrAlt') && typeof fmRefreshAddressUi === 'function') {
            // Walter 25.08.2026: «Nein, nicht im Haushalt»-Auswahl beibehalten.
            const ext = document.getElementById('fmAddrExtern')?.checked;
            try { await fmRefreshAddressUi(null, ext ? false : undefined); } catch {}
        }
    } catch(e) {
        alert('Verbindungsfehler: ' + e.message);
        await loadEmployeeAddressesTab(selectedEmployeeId);
    }
}

