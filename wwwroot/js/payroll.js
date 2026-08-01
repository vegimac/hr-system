// ══════════════════════════════════════════════════════════════════════
// payroll.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════
// LOHNVERWALTUNG
// ══════════════════════════════════════════════
let lohnCurrentSlip = null;

// ── Zulagen & Abzüge pro Mitarbeiter/Periode ──────────────────────────────
let _lzCurrentEmpId   = null;
let _lzCurrentCompId  = null;
let _lzCurrentYear    = null;
let _lzCurrentMonth   = null;
let _lzLohnpositionen = []; // Dropdown-Cache (ZULAGE + ABZUG Lohnpositionen)

async function lzInit(empId, compId, year, month) {
    _lzCurrentEmpId  = empId;
    _lzCurrentCompId = compId;
    _lzCurrentYear   = year;
    _lzCurrentMonth  = month;

    // Walter 19.05.2026: Card existiert in beiden Tabs (Definitiv + Akonto).
    const lohnP = document.getElementById('lohnZulagenPanel');
    const akWfP = document.getElementById('akWfZulagenPanel');
    if (lohnP) lohnP.style.display = 'block';
    if (akWfP) akWfP.style.display = 'block';
    lzCloseForm();

    // Lohnpositionen für Dropdown laden (immer frisch — neue Codes wie 65.2
    // nach Deploy/Seed sonst unsichtbar bis Hard-Reload).
    try {
        const res = await fetch('/api/lohn-zulag-typen', { headers: ah(), cache: 'no-store' });
        _lzLohnpositionen = res.ok ? await res.json() : [];
    } catch { _lzLohnpositionen = []; }

    await lzLoad();
    await lzLoadDepotRefund();
}

/** Depot-Refund-Box im Zulagen-Panel (Korrekturlohn / Austritt). */
async function lzLoadDepotRefund() {
    const box = document.getElementById('lohnDepotRefundBox');
    if (!box) return;
    box.style.display = 'none';
    box.innerHTML = '';
    const empId = _lzCurrentEmpId;
    if (!empId) return;
    try {
        const res = await fetch(`/api/employees/${empId}/uniform-depot`, { headers: ah(), cache: 'no-store' });
        if (!res.ok) return;
        const d = await res.json();
        if (!d || !d.status) {
            // Kein Depot — bei Korrektur-MA Hinweis + Sofort-Anlegen möglich
            if (_lohnIsCorrection(empId)) {
                box.style.display = 'block';
                box.innerHTML = `<div style="background:#f8fafc;border:1px dashed #cbd5e1;border-radius:8px;padding:10px 12px;font-size:12px;color:#64748b">
                    Kein Uniformen-Depot vorhanden.
                    <button type="button" onclick="lzEnsureDepotAndRefund()" style="margin-left:8px;background:#3f3f3f;color:#fff;border:none;padding:5px 10px;border-radius:8px;font-size:11px;font-weight:600;cursor:pointer">Depot CHF 50 anlegen + zurückzahlen</button>
                </div>`;
            }
            return;
        }
        const bal = Number(d.balance || 0);
        if (d.status === 'ZURUECKBEZAHLT') {
            box.style.display = 'block';
            box.innerHTML = `<div style="background:#dcfce7;border:1px solid #86efac;border-radius:8px;padding:10px 12px;font-size:12px;color:#166534;font-weight:600">
                Uniformen-Depot bereits zurückbezahlt${d.refundPeriode ? ' (' + d.refundPeriode + ')' : ''}
            </div>`;
            return;
        }
        if (d.status === 'VERFALLEN') {
            box.style.display = 'block';
            box.innerHTML = `<div style="background:#fee2e2;border:1px solid #fecaca;border-radius:8px;padding:10px 12px;font-size:12px;color:#991b1b;font-weight:600">
                Uniformen-Depot verfallen — kein Refund
            </div>`;
            return;
        }
        if (d.status === 'EINBEHALTEN' && bal > 0) {
            box.style.display = 'block';
            if (d.returnConfirmed === true) {
                box.innerHTML = `<div style="background:#dcfce7;border:1px solid #86efac;border-radius:8px;padding:10px 12px;font-size:12px;color:#166534">
                    <strong>Depot-Refund bereit:</strong> CHF ${bal.toFixed(2)} erscheint automatisch auf dem Slip (Uniform zurück).
                </div>`;
            } else {
                box.innerHTML = `<div style="background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:10px 12px;font-size:12px;color:#92400e">
                    <div style="font-weight:700;margin-bottom:6px">Uniformen-Depot CHF ${bal.toFixed(2)} einbehalten</div>
                    <div style="display:flex;gap:8px;flex-wrap:wrap">
                        <button type="button" onclick="lzSetDepotReturn(true)"
                            style="background:#3f3f3f;color:#fff;border:none;padding:6px 11px;border-radius:9px;font-size:11.5px;font-weight:600;cursor:pointer">Uniform zurück → +${bal.toFixed(2)} Refund</button>
                        <button type="button" onclick="lzSetDepotReturn(false)"
                            style="background:rgba(255,255,255,0.7);color:#3f3f3f;border:1px solid #cbd5e1;padding:6px 11px;border-radius:9px;font-size:11.5px;font-weight:600;cursor:pointer">Nicht zurück → verfällt</button>
                    </div>
                </div>`;
            }
        }
    } catch { /* best-effort */ }
}

async function lzSetDepotReturn(returned) {
    const empId = _lzCurrentEmpId;
    if (!empId) return;
    if (!confirm(returned
        ? 'Uniform zurückgegeben — CHF 50 als Refund auf den Slip setzen?'
        : 'Uniform NICHT zurück — Depot verfällt?')) return;
    try {
        const res = await fetch(`/api/employees/${empId}/uniform-depot/return`, {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ returned: !!returned }),
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            alert(err.message || err.error || 'Speichern fehlgeschlagen');
            return;
        }
        await lzLoadDepotRefund();
        if (_lzCurrentEmpId && _lzCurrentCompId && _lzCurrentYear && _lzCurrentMonth) {
            loadLohnSlip(_lzCurrentEmpId, _lzCurrentCompId, _lzCurrentYear, _lzCurrentMonth);
        }
        if (typeof showToast === 'function') {
            showToast(returned ? 'Depot-Refund auf Slip' : 'Depot verfällt', 'success');
        }
    } catch (e) {
        alert('Netzwerkfehler: ' + (e?.message || e));
    }
}

/** Kein Depot vorhanden → anlegen + sofort als zurück markieren (API). */
async function lzEnsureDepotAndRefund() {
    await lzSetDepotReturn(true);
}

async function lzLoad() {
    // Walter 19.05.2026: gleiches HTML in beide Listen schreiben (Definitiv + Akonto).
    const listEls = [
        document.getElementById('lohnZulagenList'),
        document.getElementById('akWfZulagenList'),
    ].filter(Boolean);
    const setHtml = (html) => listEls.forEach(el => { el.innerHTML = html; });
    if (listEls.length === 0) return;
    setHtml('<div style="padding:12px 0;color:#94a3b8;font-size:13px">Lade…</div>');
    const periode = `${_lzCurrentYear}-${String(_lzCurrentMonth).padStart(2,'0')}`;
    try {
        const res = await fetch(`/api/lohn-zulagen/${_lzCurrentEmpId}/${periode}`, { headers: ah() });
        const list = res.ok ? await res.json() : [];
        // Liste zwischenspeichern, damit lzEditById() die Bemerkung sauber
        // aufgreifen kann — vermeidet Quoting-Probleme mit Sonderzeichen
        // (Anführungszeichen etc.) die im onclick-Attribut brechen würden.
        window._lzItems = list;
        if (!list.length) {
            setHtml('<div style="padding:14px 0;color:#94a3b8;font-size:13px;font-style:italic">Keine Zulagen/Abzüge für diese Periode</div>');
            return;
        }
        const rowsHtml = list.map(z => {
            const isAbzug = z.typ === 'ABZUG';
            const bemEsc  = (z.bemerkung ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
            const lpBezEsc = (z.lohnpositionBezeichnung ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
            return `<div style="display:flex;align-items:center;gap:10px;padding:10px 0;border-bottom:1px solid #f1f5f9">
                <span style="font-size:11px;font-weight:600;padding:2px 7px;border-radius:10px;${isAbzug ? 'background:#fee2e2;color:#991b1b' : 'background:#dcfce7;color:#166534'}">${isAbzug ? '− Abzug' : '+ Zulage'}</span>
                <div style="flex:1;min-width:0">
                    <div style="font-weight:500;font-size:13px">${lpBezEsc}</div>
                    ${bemEsc ? `<div style="font-size:11px;color:#64748b">${bemEsc}</div>` : ''}
                </div>
                <div style="font-weight:600;font-size:13px;font-family:monospace;color:${isAbzug ? '#dc2626' : '#059669'}">${isAbzug ? '−' : '+'} CHF ${Number(z.betrag).toLocaleString('de-CH',{minimumFractionDigits:2,maximumFractionDigits:2})}</div>
                <button onclick="lzEditById(${z.id})" style="border:none;background:#f1f5f9;color:#374151;padding:3px 9px;border-radius:6px;font-size:12px;cursor:pointer">✏️</button>
                <button onclick="lzDelete(${z.id})" style="border:none;background:#fee2e2;color:#dc2626;padding:3px 9px;border-radius:6px;font-size:12px;cursor:pointer">🗑</button>
            </div>`;
        }).join('');
        setHtml(rowsHtml);
    } catch(e) {
        setHtml(`<div style="padding:12px 0;color:#dc2626;font-size:13px">Fehler: ${e.message}</div>`);
    }
    // Edit-Sperre für die Buttons anwenden (Walter 19.05.2026): GF darf
    // während HR-Phase nichts mehr ändern, alle nach HR_FREIGEGEBEN nicht
    // mehr. Die Logik liegt in akonto-workflow.js.
    if (typeof _akWfApplyZulagenLock === 'function') _akWfApplyZulagenLock();
}

function lzEditById(id) {
    const z = (window._lzItems || []).find(x => x.id === id);
    if (!z) return;
    lzEdit(z.id, z.lohnpositionId, z.betrag, z.bemerkung ?? '');
}

function lzOpenForm() {
    document.getElementById('lzEditId').value   = '';
    document.getElementById('lzBetrag').value   = '';
    document.getElementById('lzBemerkung').value = '';

    const sel = document.getElementById('lzLpSel');
    sel.innerHTML = '<option value="">— Lohnposition wählen —</option>' +
        _lzLohnpositionen.map(l =>
            `<option value="${l.id}">[${l.code}] ${l.bezeichnung} ${l.typ === 'ABZUG' ? '(−)' : '(+)'}</option>`
        ).join('');
    sel.value = '';

    // Walter 19.05.2026: globaler Modal-Overlay statt eingebettetes Form,
    // damit der Akonto-Tab dieselbe Maske nutzen kann.
    const overlay = document.getElementById('lohnZulagenFormOverlay');
    if (overlay) overlay.style.display = 'flex';
    document.getElementById('lohnZulagenForm').style.display = 'block';
    document.getElementById('lzLpSel').focus();
}

function lzEdit(id, lpId, betrag, bem) {
    document.getElementById('lzEditId').value    = id;
    document.getElementById('lzBetrag').value    = betrag;
    document.getElementById('lzBemerkung').value = bem || '';

    const sel = document.getElementById('lzLpSel');
    sel.innerHTML = '<option value="">— Lohnposition wählen —</option>' +
        _lzLohnpositionen.map(l =>
            `<option value="${l.id}">[${l.code}] ${l.bezeichnung} ${l.typ === 'ABZUG' ? '(−)' : '(+)'}</option>`
        ).join('');
    sel.value = lpId;

    // Panel und Formular sichtbar machen (globaler Modal-Overlay, Walter 19.05.2026)
    const overlay = document.getElementById('lohnZulagenFormOverlay');
    if (overlay) overlay.style.display = 'flex';
    document.getElementById('lohnZulagenForm').style.display  = 'block';
}

function lzCloseForm() {
    document.getElementById('lohnZulagenForm').style.display = 'none';
    const overlay = document.getElementById('lohnZulagenFormOverlay');
    if (overlay) overlay.style.display = 'none';
}

async function lzSave() {
    const id      = document.getElementById('lzEditId').value;
    const lpId    = parseInt(document.getElementById('lzLpSel').value);
    const betrag  = parseFloat(document.getElementById('lzBetrag').value);
    const bem     = document.getElementById('lzBemerkung').value.trim() || null;

    if (!lpId)         { alert('Bitte eine Lohnposition wählen.'); return; }
    if (!betrag || betrag <= 0) { alert('Bitte einen gültigen Betrag eingeben.'); return; }

    const periode = `${_lzCurrentYear}-${String(_lzCurrentMonth).padStart(2,'0')}`;
    try {
        let res;
        if (id) {
            res = await fetch(`/api/lohn-zulagen/${id}`, {
                method: 'PUT',
                headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify({ betrag, bemerkung: bem })
            });
        } else {
            res = await fetch('/api/lohn-zulagen', {
                method: 'POST',
                headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify({ employeeId: _lzCurrentEmpId, periode, lohnpositionId: lpId, betrag, bemerkung: bem })
            });
        }
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) { alert('Fehler beim Speichern.'); return; }
        lzCloseForm();
        await lzLoad();
        // Lohnabrechnung neu berechnen — sowohl Definitiv-Slip als auch (falls
        // Akonto-Tab aktiv) den Akonto-Wert. Walter-Vorgabe 19.05.2026: nach
        // Speichern einer Zulage/Abzug soll der User NICHT extra „↻ Neu berechnen"
        // klicken müssen.
        loadLohnSlip(_lzCurrentEmpId, _lzCurrentCompId, _lzCurrentYear, _lzCurrentMonth);
        if (typeof _akWfMode !== 'undefined' && _akWfMode === 'akonto'
            && typeof akWfStart === 'function') {
            try { await akWfStart(); } catch (e) { console.error('akWfStart nach lzSave', e); }
        }
    } catch(e) { alert('Verbindungsfehler: ' + e.message); }
}

async function lzDelete(id) {
    if (!confirm('Eintrag löschen?')) return;
    try {
        const res = await fetch(`/api/lohn-zulagen/${id}`, { method: 'DELETE', headers: ah() });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            const err = await res.text();
            alert('Fehler beim Löschen: ' + err);
            return;
        }
        await lzLoad();
        loadLohnSlip(_lzCurrentEmpId, _lzCurrentCompId, _lzCurrentYear, _lzCurrentMonth);
        if (typeof _akWfMode !== 'undefined' && _akWfMode === 'akonto'
            && typeof akWfStart === 'function') {
            try { await akWfStart(); } catch (e) { console.error('akWfStart nach lzDelete', e); }
        }
    } catch(e) { alert('Verbindungsfehler: ' + e.message); }
}

async function initLohnPage() {
    // Filiale aus Hauptmenü übernehmen
    const branchInput = document.getElementById('lohnBranchSelect');
    const branchLabel = document.getElementById('lohnBranchLabel');
    if (!fixedCompanyProfileId) {
        if (branchLabel) branchLabel.textContent = '– Bitte zuerst Filiale im Hauptmenü wählen –';
        branchLabel.style.color = '#dc2626';
        return;
    }
    const branch = allBranches.find(b => b.id === fixedCompanyProfileId);
    if (branchInput) branchInput.value = fixedCompanyProfileId;
    if (branchLabel) {
        branchLabel.textContent = branch ? `${branch.restaurantCode ? branch.restaurantCode + ' – ' : ''}${branch.branchName || branch.companyName}` : fixedCompanyProfileId;
        branchLabel.style.color = '#0f172a';
    }

    // Default-Periode setzen (älteste offene) und Liste laden
    await setDefaultLohnPeriode(fixedCompanyProfileId);
    if (fixedCompanyProfileId) loadLohnList();
}

// Default-Periode bestimmen: älteste noch offene (status != "abgeschlossen").
// Wenn alles abgeschlossen ist oder keine Perioden existieren, fällt das
// System auf den aktuellen Monat zurück. Damit landet Walter direkt im
// Lohnlauf der dran ist statt in einem leeren Folge-Monat.
//
// Wird sowohl beim Page-Init als auch beim Filialwechsel aufgerufen, damit
// nach Wechsel die Periode der NEUEN Filiale gewählt wird (nicht die der
// vorherigen).
async function setDefaultLohnPeriode(companyProfileId) {
    const monthSel = document.getElementById('lohnMonthSelect');
    const yearSel  = document.getElementById('lohnYearSelect');
    if (!monthSel || !yearSel) return;

    const now = new Date();
    let defMonth = now.getMonth() + 1;
    let defYear  = now.getFullYear();
    if (companyProfileId) {
        try {
            const r = await fetch(`/api/payroll-perioden?companyProfileId=${companyProfileId}`, { headers: ah() });
            if (r.ok) {
                const arr = await r.json();
                const open = (arr || []).filter(p => p.status !== 'abgeschlossen');
                if (open.length > 0) {
                    open.sort((a, b) => (a.year - b.year) || (a.month - b.month));
                    defMonth = open[0].month;
                    defYear  = open[0].year;
                }
            }
        } catch { /* fallback bleibt aktueller Monat */ }
    }

    const monthNames = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
    monthSel.innerHTML = monthNames.map((m,i) =>
        `<option value="${i+1}" ${i+1 === defMonth ? 'selected' : ''}>${m}</option>`).join('');
    const yearSet = new Set([now.getFullYear()-1, now.getFullYear(), now.getFullYear()+1, defYear]);
    const years   = [...yearSet].sort((a,b) => a - b);
    yearSel.innerHTML = years.map(y =>
        `<option value="${y}" ${y === defYear ? 'selected' : ''}>${y}</option>`).join('');
}

async function lohnBranchChanged() {
    // Filiale aus Hauptmenü synchronisieren
    const branchInput = document.getElementById('lohnBranchSelect');
    const branchLabel = document.getElementById('lohnBranchLabel');
    if (branchInput && fixedCompanyProfileId) branchInput.value = fixedCompanyProfileId;
    if (branchLabel) {
        const branch = allBranches.find(b => b.id === fixedCompanyProfileId);
        branchLabel.textContent = branch ? `${branch.restaurantCode ? branch.restaurantCode + ' – ' : ''}${branch.branchName || branch.companyName}` : '–';
    }
    lzReset();
    // Periode auf älteste offene der neuen Filiale setzen — sonst bleibt
    // die Auswahl der vorherigen Filiale stehen (z.B. Februar 2025 obwohl
    // dieser für die neue Filiale gar nicht der Lohnlauf ist).
    await setDefaultLohnPeriode(fixedCompanyProfileId);
    loadLohnList();
}
function lohnYearChanged()   { loadLohnSlipFromPanel(); }
function lzReset() {
    _lzCurrentEmpId = null;
    document.getElementById('lohnZulagenPanel').style.display  = 'none';
    document.getElementById('lohnSlipCard').style.display      = 'none';
    document.getElementById('lohnSlipEmpty').style.display     = 'flex';
    document.getElementById('lohnVertragPanel').style.display  = 'none';
    document.getElementById('lohnVertragEmpty').style.display  = 'block';
    document.getElementById('lohnPeriodToolbar').style.display = 'none';
    // Top-Aktions-Buttons (PDF/Reopen/Bestätigen) parallel ausblenden
    const ta = document.getElementById('lohnTopActions');
    if (ta) ta.style.display = 'none';
}

// ID des aktuell ausgewählten Mitarbeiters — bleibt bei Perioden-Wechsel
// erhalten. Die Liste ist nach Vorname sortiert; beim Re-Render wird zum
// ausgewählten MA gescrollt (nicht umgeordnet).
let _lohnSelectedEmpId = null;

// Korrekturlohn für Ausgetretene (Walter Aug 2026): manuell hinzugefügte
// MA-IDs pro Filiale+Periode (sessionStorage). Zusätzlich werden Kandidaten
// mit offenen Zulagen / Depot-Refund automatisch eingeblendet.
let _lohnCorrectionIds = new Set();
function _lohnCorrKey(cid, y, m) { return `lohnCorr_${cid}_${y}_${m}`; }
function _lohnCorrLoad(cid, y, m) {
    try {
        const raw = sessionStorage.getItem(_lohnCorrKey(cid, y, m));
        _lohnCorrectionIds = new Set(raw ? JSON.parse(raw).map(Number) : []);
    } catch { _lohnCorrectionIds = new Set(); }
}
function _lohnCorrSave(cid, y, m) {
    sessionStorage.setItem(_lohnCorrKey(cid, y, m), JSON.stringify([..._lohnCorrectionIds]));
}
function _lohnIsCorrection(empId) {
    return _lohnCorrectionIds.has(Number(empId));
}

// ══════════════════════════════════════════════════════════════════════
// Definitiv-Workflow Single-Source-of-Truth (Walter-Vorgabe 20.05.2026)
// ══════════════════════════════════════════════════════════════════════
// Der Definitivlauf wird – wie vom Akonto-Lauf vorgegeben – über EINEN
// State-Cache + EINE Render-Funktion gesteuert. Damit verschwindet die
// früher verstreute Button-Logik (loadLohnSlip + loadLohnPeriodBanner +
// loadLohnList), die ständig auseinanderlief.
//
// _lohnWfData spiegelt _akWfData aus akonto-workflow.js:
//   { status, periode, periodeId, snapByEmp:{empId:{id,status}},
//     gfConfirmed, hrConfirmed, activeTotal }
// status = Periode-Status ('offen' | 'provisorisch_abgeschlossen' | 'abgeschlossen').
// snapByEmp = Snapshot-Status je MA (BERECHNET/FREIGEGEBEN_GF/HR_BESTAETIGT/ABGESCHLOSSEN).
let _lohnWfData = null;
// empId → { minimum, actual, unit, difference, message } für MA unter L-GAV-
// Mindestlohn in der aktuellen Periode (Walter 20.05.2026). Speist Listen-⚠,
// Status-Counter, Lohnzettel-Banner und die Bestätigen-Sperre.
let _lohnMwUnderpaid = {};

// Periode-Status-Optik (analog _AK_STATUS im Akonto-Tab).
const _LOHN_STATUS = {
    offen:                      { label: 'Offen',                       color: '#6b6152', bg: '#efece5' },
    provisorisch_abgeschlossen: { label: 'Provisorisch abgeschlossen',  color: '#92400e', bg: '#fef3c7' },
    abgeschlossen:              { label: 'Abgeschlossen',               color: '#166534', bg: '#dcfce7' },
};

// Zeitstempel-Formatter für den Status-Trail (analog _akFmtTs).
function _lohnFmtTs(ts) {
    if (!ts) return '–';
    const d = new Date(ts);
    if (isNaN(d)) return ts;
    return d.toLocaleString('de-CH', { day:'2-digit', month:'2-digit', year:'numeric', hour:'2-digit', minute:'2-digit' });
}

// Einziger Refresh-Pfad des Definitivlaufs (analog akWfRefresh). loadLohnList
// macht den eigentlichen Fetch (Periode + Snapshots + Mitarbeiter) und rendert
// am Ende _lohnWfRenderStatusBar — daher ist dies ein dünner Wrapper, den die
// Aktions-Handler nach jeder Aktion aufrufen.
async function lohnWfRefresh() {
    await loadLohnList();
}

// ── Status-Bar: zeigt Stufe + die nächsten Aktions-Buttons (EINZIGE Stelle) ──
// Mirror von _akWfRenderStatusBar aus akonto-workflow.js. Rendert die
// Status-Pille, den Fortschritts-Counter und ALLE Aktionsbuttons abhängig von:
//   • Periode-Status (offen / provisorisch_abgeschlossen / abgeschlossen)
//   • Snapshot-Status des aktuell selektierten MA (_lohnSelectedEmpId)
//   • Rolle (GF = user, HR = admin/superuser)
// KEINE Button-Sichtbarkeit darf irgendwo sonst gesetzt werden.
function _lohnWfRenderStatusBar() {
    const bar = document.getElementById('lohnDefinitivStatusBar');
    if (!bar) return;
    const d = _lohnWfData;
    if (!d) { bar.innerHTML = ''; return; }

    const meta  = _LOHN_STATUS[d.status] || _LOHN_STATUS.offen;
    const isHr  = (typeof _akIsHr === 'function')
        ? _akIsHr()
        : ((typeof currentUser !== 'undefined' && currentUser?.role)
            && (currentUser.role === 'admin' || currentUser.role === 'superuser'));
    const total = d.activeTotal || 0;
    const gf    = d.gfConfirmed || 0;
    const hr    = d.hrConfirmed || 0;
    const isOffen = d.status === 'offen';
    const isProv  = d.status === 'provisorisch_abgeschlossen';
    const isAbg   = d.status === 'abgeschlossen';

    // Counter zeigt den jeweils relevanten Schritt (GF-Phase: GF-Fortschritt,
    // HR-Phase: HR-Fortschritt) — analog Akonto-Tab.
    const counts = (isProv || isAbg)
        ? `${hr}/${total} HR-bestätigt`
        : `${gf}/${total} bestätigt`;
    const allGf = total > 0 && gf >= total;
    const allHr = total > 0 && hr >= total;

    // Snapshot-Status des aktuell selektierten MA → bestimmt die per-MA-Buttons.
    const selStatus = (d.snapByEmp && _lohnSelectedEmpId != null
        && d.snapByEmp[_lohnSelectedEmpId]?.status) || 'BERECHNET';
    const isCorrSel = _lohnSelectedEmpId != null && _lohnIsCorrection(_lohnSelectedEmpId);

    // ─ GF Per-MA-Aktionen (offen) + Korrekturlohn auch in HR-Phase (Walter Aug 2026) ─
    // Nachträgliche Korrektur (UVG/Depot) kommt oft erst wenn die Periode
    // schon bei HR ist — sonst wäre Bestätigen unmöglich ohne «Zurück an GF».
    const canConfirmCorrInHr = isCorrSel && isProv && isHr && selStatus === 'BERECHNET';
    const perMaConfirm = ((isOffen && selStatus === 'BERECHNET') || canConfirmCorrInHr)
        ? `<button class="btn btn-primary btn-sm" onclick="confirmLohn()">${isCorrSel ? '✓ Korrekturlohn bestätigen' : '✓ Lohn bestätigen'}</button>` : '';
    const perMaReopen = (isOffen && selStatus === 'FREIGEGEBEN_GF')
        ? `<button class="btn btn-outline btn-sm" onclick="reopenLohn()" style="color:#b91c1c;border-color:#fecaca">↶ Wieder eröffnen</button>` : '';

    // ─ HR Per-MA-Aktionen (nur in provisorisch_abgeschlossen, nur HR) ─
    const hrMaBestaetigen = (isHr && isProv && selStatus === 'FREIGEGEBEN_GF')
        ? `<button class="btn btn-primary btn-sm" onclick="lohnHrBestaetigen()">✓ HR-bestätigen</button>` : '';
    const hrMaZurueck = (isHr && isProv && selStatus === 'HR_BESTAETIGT')
        ? `<button class="btn btn-outline btn-sm" onclick="lohnHrZurueckziehen()" style="color:#b91c1c;border-color:#fecaca">↶ HR-Bestätigung zurückziehen</button>` : '';

    // Gemeinsame Buttons
    const pdfBtn = `<button class="btn btn-outline btn-sm" onclick="exportLohnPdf()">PDF</button>`;
    const skBtn  = `<button class="btn btn-outline btn-sm" onclick="exportStundenkontrollePdf()" title="Monatsblatt: Stunden kontrollieren und unterschreiben">Std.-Kontrolle</button>`;
    const footerHasText = !!(d.periode?.pdfFooterText && d.periode.pdfFooterText.trim());
    const bemBtn = (isOffen || isProv)
        ? `<button class="btn btn-outline btn-sm" onclick="openPeriodeBemerkungModal()" style="color:${footerHasText ? '#15803d' : '#64748b'};font-size:11px">${footerHasText ? '✏️ Bemerkung' : '＋ Bemerkung'}</button>`
        : '';

    const lockPill = (txt, bg, color) =>
        `<span style="color:${color};font-size:11.5px;font-weight:600;background:${bg};padding:3px 9px;border-radius:8px">${txt}</span>`;

    // Walter-Vorgabe 31.05.2026: Sekundär-Aktionen (Reports, Admin-Wartung,
    // Downloads) wandern ins ⋯-Dropdown. Die Status-Bar bleibt damit auf
    // max 3 sichtbare Buttons: Per-MA-Aktion + Workflow-Schritt + ⋯-Menü.
    const isAdmin = (typeof currentUser !== 'undefined' && currentUser?.role === 'admin');
    // Baut ein ⋯-Menü mit den übergebenen Item-HTML-Strings (gefiltert auf
    // nicht-leere). Wenn keine Items → leer.
    function buildMoreMenu(items) {
        const filled = items.filter(x => x && x.trim());
        if (filled.length === 0) return '';
        return `<div class="action-menu">
            <button class="action-menu-trigger" onclick="actionMenu.toggle(this)" title="Weitere Aktionen">⋯ Mehr</button>
            <div class="action-menu-list">${filled.join('')}</div>
        </div>`;
    }
    const menuItem = (label, onclick, opts = {}) =>
        `<button class="action-menu-item${opts.danger ? ' danger' : ''}" onclick="${onclick}"${opts.title ? ` title="${opts.title}"` : ''}>${label}</button>`;
    const menuDivider = '<div class="action-menu-divider"></div>';

    let actions = '';
    switch (d.status) {
        case 'offen':
            // GF-Phase: jeden MA bestätigen, dann an HR senden.
            actions = `${perMaConfirm}${perMaReopen}${pdfBtn}${skBtn}
                <button class="btn btn-success btn-sm" onclick="lohnAnHrSendenAktuell()" ${allGf ? '' : 'disabled'}>An HR senden →</button>`;
            break;
        case 'provisorisch_abgeschlossen':
            if (isHr) {
                // Sekundär-Aktionen ins ⋯-Menü
                const moreItems = [
                    menuItem('📋 Alle Lohnbelege (PDF)', 'lohnDownloadVorabPdf()', { title: 'Alle Lohnbelege der Periode in einem PDF' }),
                    menuItem('📋 GF-Übersicht (Saldi)', "lohnSaldoListe('gf')",     { title: 'Saldi-Übersicht für den Geschäftsführer' }),
                    isAdmin ? menuDivider : '',
                    isAdmin ? menuItem('🔄 Fibu-Codes nachtragen', 'lohnRefreshCodes()',      { title: 'Fibu-Codes in bestehende Lohnzettel nachtragen (Wartung)' }) : '',
                    isAdmin ? menuItem('♻️ Snapshots neu berechnen', 'lohnRecomputeSnapshots()', { title: 'Lohnzettel der Periode neu berechnen — Reparatur bei inkonsistenten Snapshots' }) : '',
                ];
                // perMaConfirm: Korrekturlohn nachträglich in HR-Phase bestätigen
                actions = `${perMaConfirm}${hrMaBestaetigen}${hrMaZurueck}${pdfBtn}${skBtn}
                    ${buildMoreMenu(moreItems)}
                    <button class="btn btn-outline btn-sm" onclick="lohnZurueckAnGf()" style="color:#b45309;border-color:#fcd34d">↩ Zurück an GF</button>
                    <button class="btn btn-success btn-sm" onclick="lohnOpenLohnbelegeModal()" ${allHr ? '' : 'disabled'} title="Alle Lohnbelege ansehen, drucken und an MA versenden">📑 Lohnbelege + DTA</button>`;
            } else {
                const moreItemsGf = [
                    menuItem('📅 Stundenkontrolle', 'exportStundenkontrollePdf()', { title: 'Monatsblatt: Stunden kontrollieren und unterschreiben' }),
                    menuItem('📋 GF-Übersicht (Saldi)', "lohnSaldoListe('gf')", { title: 'Saldi-Übersicht für den Geschäftsführer' }),
                ];
                actions = lockPill('🔒 Bei HR — keine Änderungen möglich', '#fef3c7', '#b45309') + buildMoreMenu(moreItemsGf);
            }
            break;
        case 'abgeschlossen':
            const moreItemsFinal = [
                menuItem('📥 DTA-File', 'lohnDownloadDtaMa()', { title: 'pain.001-XML für die Bank' }),
                isHr  ? menuItem('📑 Lohnbelege ansehen', 'lohnOpenLohnbelegeModal()', { title: 'Alle Lohnbelege ansehen / drucken' }) : '',
                menuItem('📋 GF-Übersicht (Saldi)', "lohnSaldoListe('gf')", { title: 'Saldi-Übersicht für den Geschäftsführer' }),
                isAdmin ? menuDivider : '',
                isAdmin ? menuItem('🔄 Fibu-Codes nachtragen', 'lohnRefreshCodes()', { title: 'Wartung' }) : '',
                isAdmin ? menuItem('♻️ Snapshots neu berechnen', 'lohnRecomputeSnapshots()', { title: 'Reparatur' }) : '',
            ];
            actions = `${pdfBtn}${skBtn}
                ${buildMoreMenu(moreItemsFinal)}
                ${lockPill('🔒 Abgeschlossen — Admin-Reopen via Lohnperioden-Modul', '#dcfce7', '#15803d')}`;
            break;
    }

    const trail = [
        d.periode?.provisorischAbgeschlossenAt ? `Provisorisch: ${_lohnFmtTs(d.periode.provisorischAbgeschlossenAt)}` : null,
        d.periode?.abgeschlossenAt             ? `Abgeschlossen: ${_lohnFmtTs(d.periode.abgeschlossenAt)}`           : null,
    ].filter(x => x).join('\n');

    bar.innerHTML = `
        <div title="${trail}" style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;padding:6px 4px;font-size:12px">
            <span style="background:${meta.bg};color:${meta.color};padding:2px 9px;border-radius:8px;font-weight:700;font-size:11px;white-space:nowrap">${meta.label}</span>
            ${total > 0 ? `<span style="color:#64748b;white-space:nowrap">${counts}</span>` : ''}
            ${d.mwUnderpaidCount > 0 ? `<span title="Diese MA können erst nach Lohnkorrektur bestätigt werden (unter Mindestlohn oder ohne Lohnsumme)" style="color:#b91c1c;background:#fee2e2;padding:2px 9px;border-radius:8px;font-weight:700;font-size:11px;white-space:nowrap">⚠ ${d.mwUnderpaidCount} mit Lohnproblem</span>` : ''}
            <span style="display:inline-flex;gap:6px;flex-wrap:wrap;margin-left:auto">${bemBtn}${actions}</span>
        </div>`;
}

async function loadLohnList() {
    const companyId = document.getElementById('lohnBranchSelect').value;
    if (!companyId) return;

    const listEl = document.getElementById('lohnEmpList');
    // Walter-Vorgabe 20.05.2026 („genau wie Akonto", kein Sprung nach oben):
    //   1) Aktuelle Scroll-Position merken und nach dem Neuaufbau wieder setzen,
    //      damit die Liste beim Refresh NICHT an den Anfang reisst.
    //   2) Den „Lade…"-Platzhalter NUR zeigen, wenn die Liste leer ist (erster
    //      Aufruf). Bei einem Refresh bleibt die alte Liste sichtbar bis die
    //      neue fertig gerendert ist — sonst blitzt sie leer auf (= Flackern).
    const _prevScroll = listEl.scrollTop || 0;
    if (!listEl.querySelector('.lohn-emp-row')) {
        listEl.innerHTML = '<div style="padding:20px;text-align:center;color:#94a3b8">Lade…</div>';
    }

    const cid = parseInt(companyId);
    const y   = parseInt(document.getElementById('lohnYearSelect')?.value  || new Date().getFullYear());
    const m   = parseInt(document.getElementById('lohnMonthSelect')?.value || (new Date().getMonth()+1));

    try {
        // Snapshots für diese Periode laden — der Snapshot-Status entscheidet
        // über die Häkchen-Anzeige (Walter 19.05.2026):
        //   BERECHNET       → kein Häkchen (GF muss noch bestätigen)
        //   FREIGEGEBEN_GF  → ✓ (GF-Bestätigung, wartet auf HR)
        //   HR_BESTAETIGT   → ✓✓ (HR-Bestätigung, definitiv ready)
        //   ABGESCHLOSSEN   → ✓✓ (Periode versendet)
        // Reine Existenz des Snapshots reicht NICHT mehr aus — sonst wären
        // alle MA mit Akonto-Vorberechnung sofort als bestätigt markiert.
        let gfEmpIds = new Set();      // FREIGEGEBEN_GF + HR_BESTAETIGT + ABGESCHLOSSEN
        let hrEmpIds = new Set();      // HR_BESTAETIGT + ABGESCHLOSSEN
        // Single-Source-of-Truth (Walter-Vorgabe 20.05.2026): loadLohnList ist
        // der EINZIGE Fetch-Pfad. Periode + Snapshots werden hier geladen und
        // in _lohnWfData abgelegt — _lohnWfRenderStatusBar liest ausschliesslich
        // daraus. So gibt es keine zweite Stelle mehr, die Status/Buttons ableitet.
        let _pData = null;             // volles Periode-Objekt (status, pdfFooterText, …)
        let _snapByEmp = {};           // empId → { id, status }
        try {
            const pRes = await fetch(`/api/payroll-perioden/current?companyProfileId=${cid}&year=${y}&month=${m}`, { headers: ah() });
            if (pRes.ok) {
                const txt = await pRes.text();
                if (txt && txt.trim() && txt.trim() !== 'null') { try { _pData = JSON.parse(txt); } catch {} }
            }
            if (_pData?.id) {
                const snRes = await fetch(`/api/payroll-perioden/${_pData.id}/snapshots`, { headers: ah() });
                if (snRes.ok) {
                    const snaps = await snRes.json();
                    snaps.forEach(s => {
                        const st = s.status || 'BERECHNET';
                        _snapByEmp[s.employeeId] = { id: s.id, status: st };
                        if (st === 'FREIGEGEBEN_GF' || st === 'HR_BESTAETIGT' || st === 'ABGESCHLOSSEN') {
                            gfEmpIds.add(s.employeeId);
                        }
                        if (st === 'HR_BESTAETIGT' || st === 'ABGESCHLOSSEN') {
                            hrEmpIds.add(s.employeeId);
                        }
                    });
                }
            }
        } catch {}
        // Kompatibilität: bisheriger Code verwendet confirmedEmpIds für „GF-bestätigt"
        const confirmedEmpIds = gfEmpIds;

        // QST-aktive MA-IDs für die Lohnperiode laden (Walter-Vorgabe
        // 18.05.2026): der QST-Shortcut neben dem Modell-Badge erscheint
        // NUR bei MA mit aktivem QST-Eintrag der die Lohnperiode überlappt.
        // B-Permit kann auch QST-befreit sein → kein nationality-/permit-Filter,
        // sondern hartes „QST-Datensatz vorhanden in der Periode".
        const periodFromIso = `${y}-${String(m).padStart(2,'0')}-01`;
        const lastDayQ      = new Date(y, m, 0).getDate();
        const periodToIso   = `${y}-${String(m).padStart(2,'0')}-${String(lastDayQ).padStart(2,'0')}`;
        let qstEmpIds = new Set();
        try {
            const qRes = await fetch(`/api/employee-quellensteuer/active-employee-ids?from=${periodFromIso}&to=${periodToIso}`, { headers: ah() });
            if (qRes.ok) (await qRes.json() || []).forEach(id => qstEmpIds.add(id));
        } catch {}

        // Mindestlohn-Unterschreitungen der Periode (Walter 20.05.2026): markiert
        // betroffene MA in der Liste + speist Banner/Counter/Bestätigen-Sperre.
        _lohnMwUnderpaid = {};
        try {
            const mwRes = await fetch(`/api/minimum-wage-rules/check-period?companyProfileId=${cid}&year=${y}&month=${m}`, { headers: ah() });
            if (mwRes.ok) (await mwRes.json() || []).forEach(u => { _lohnMwUnderpaid[u.employeeId] = u; });
        } catch {}

        const res  = await fetch(`/api/employees`, { headers: ah() });
        const emps = await res.json();

        // Flache Mitarbeiterliste: nur aktive MAs mit aktivem Vertrag in dieser Filiale.
        // "Kein Lohn"-Flag (isPayrollExcluded) blendet MA aus dem Lohn-Tab aus —
        // sie bleiben aktiv im Stempel/Posteingang, werden aber nicht abgerechnet.
        // Vertrag muss auch IN DER PERIODE gültig sein — sonst wären MA mit
        // späterem Eintritt fälschlicherweise als "muss bestätigt werden"
        // gezählt und der Provisorische Abschluss würde nie greifen.
        const periodEnd   = new Date(y, m, 0);                         // letzter Tag des Monats
        const periodStart = new Date(y, m - 1, 1);                     // erster Tag des Monats
        const active = emps
            .filter(e => e.isActive && !e.isPayrollExcluded)
            .map(e => {
                // Vertrag für diese Filiale, der in der Periode gültig ist.
                // Walter-Vorgabe 31.05.2026: KEIN v.isActive-Check mehr — bei Austritt
                // wird das isActive-Flag oft automatisch auf false gesetzt, der Vertrag
                // gilt aber für den letzten Lohnmonat noch. Einzig massgeblich ist:
                //   contractStartDate <= periodEnd
                //   AND (!contractEndDate || contractEndDate >= periodStart)
                // Bug 31.05.2026: Valmira Alili (Austritt 31.1.2026, Krank 29.–31.1.)
                // wurde wegen v.isActive=false aus Januar 26 ausgefiltert.
                const emp = (e.employments || [])
                    .filter(v => v.companyProfileId === cid)
                    .filter(v => {
                        if (v.contractStartDate) {
                            const cs = new Date(v.contractStartDate);
                            if (cs > periodEnd) return false;
                        }
                        if (v.contractEndDate) {
                            const ce = new Date(v.contractEndDate);
                            if (ce < periodStart) return false;
                        }
                        return true;
                    })
                    .sort((a, b) => (b.contractStartDate || '') > (a.contractStartDate || '') ? 1 : -1)[0];
                if (!emp) return null;
                return { ...e, employmentModel: emp.employmentModel, empObj: emp, isCorrection: false };
            })
            .filter(Boolean);

        // Korrekturlohn: manuell hinzugefügte + Kandidaten mit Zulagen/Depot-Refund
        _lohnCorrLoad(cid, y, m);
        try {
            const cRes = await fetch(
                `/api/payroll/correction-candidates?companyProfileId=${cid}&year=${y}&month=${m}`,
                { headers: ah() });
            if (cRes.ok) {
                const cands = await cRes.json();
                const activeIds = new Set(active.map(a => a.id));
                for (const c of cands) {
                    const auto = c.hasZulagen || c.hasPendingDepotRefund;
                    if (!_lohnCorrectionIds.has(c.id) && !auto) continue;
                    if (activeIds.has(c.id)) continue;
                    if (auto) _lohnCorrectionIds.add(c.id);
                    active.push({
                        id: c.id,
                        firstName: c.firstName,
                        lastName: c.lastName,
                        employeeNumber: c.employeeNumber,
                        isActive: c.isActive,
                        exitDate: c.exitDate,
                        employmentModel: c.employmentModel,
                        empObj: null,
                        isCorrection: true,
                        isPayrollExcluded: false,
                        employments: [],
                    });
                    activeIds.add(c.id);
                }
                _lohnCorrSave(cid, y, m);
            }
        } catch { /* best-effort */ }

        // ── _lohnWfData füllen: EINZIGE Quelle für die Statusbar ──────────────
        // Counts werden auf die aktiven MA dieser Filiale bezogen (Denominator
        // = activeTotal) — ein Snapshot eines inzwischen inaktiven MA bläht den
        // Counter nicht auf. Korrektur-MA zählen mit (sonst fehlt Confirm-Button).
        _lohnWfData = {
            status:      _pData?.status || 'offen',
            periode:     _pData,
            periodeId:   _pData?.id || null,
            snapByEmp:   _snapByEmp,
            gfConfirmed: active.filter(e => gfEmpIds.has(e.id)).length,
            hrConfirmed: active.filter(e => hrEmpIds.has(e.id)).length,
            activeTotal: active.length,
            mwUnderpaidCount: active.filter(e => _lohnMwUnderpaid[e.id]).length,
        };
        window._currentLohnPeriode = _pData;
        // Legacy-Chrome (alte Status-Pille im Toolbar-Banner + alte statische
        // Button-Zeile) bleibt im Definitiv-Modus dauerhaft aus — alles läuft
        // jetzt über #lohnDefinitivStatusBar.
        const _legacyBanner = document.getElementById('lohnPeriodBanner'); if (_legacyBanner) _legacyBanner.style.display = 'none';
        const _legacyTop    = document.getElementById('lohnTopActions');   if (_legacyTop)    _legacyTop.style.display    = 'none';

        listEl.innerHTML = '';
        if (active.length === 0) {
            listEl.innerHTML = '<div style="padding:20px;text-align:center;color:#94a3b8">Keine Mitarbeiter</div>';
            _lohnWfRenderStatusBar();
            return;
        }

        // Stabile Sortierung nach Vorname (dann Nachname als Tie-Break).
        // Der ausgewählte MA wird nicht umgeordnet, sondern am Ende hochgescrollt.
        active.sort((a, b) => {
            const na = ((a.firstName ?? '') + ' ' + (a.lastName ?? '')).trim().toLowerCase();
            const nb = ((b.firstName ?? '') + ' ' + (b.lastName ?? '')).trim().toLowerCase();
            return na.localeCompare(nb, 'de');
        });

        // Status-Zähler: bezieht sich auf die aktiven MAs in dieser Filiale.
        // "Bestätigt" zählt jeden MA der in der Liste mit ✓ markiert ist
        // (= Snapshot für diese Periode existiert) — konsistent mit dem
        // was die MA-Auswahl zeigt.
        window._lohnStats = {
            confirmedCount: active.filter(e => confirmedEmpIds.has(e.id)).length,
            activeTotal:    active.length
        };

        // Auto-Auswahl beim Öffnen der Seite: zuletzt bearbeiteter MA der
        // Filiale (aus localStorage), sonst der erste in der Liste. Wird
        // nur angewendet wenn noch nichts explizit ausgewählt ist ODER
        // die bisherige Auswahl nicht mehr in der Liste vorkommt (z.B.
        // nach Filial-Wechsel).
        const activeIds = new Set(active.map(e => e.id));
        if (!_lohnSelectedEmpId || !activeIds.has(_lohnSelectedEmpId)) {
            const lastKey = `lohnLastEmp_${cid}`;
            const lastId  = parseInt(localStorage.getItem(lastKey) || '0');
            _lohnSelectedEmpId = (lastId && activeIds.has(lastId))
                ? lastId
                : active[0].id;
        }

        active.forEach(e => {
            const initials   = ((e.firstName||'')[0]||'') + ((e.lastName||'')[0]||'');
            const modelClass = (m) => ({ MTP:'model-badge-mtp', FLEX:'model-badge-utp', FIX:'model-badge-fix', 'FIX-M':'model-badge-fix-m' })[m] || '';
            // Drei-Stufen-Markierung analog Akonto-Tab:
            //   HR-bestätigt (✓✓ blau) — wenn HR oder Periode-Abschluss durch
            //   GF-bestätigt (✓ grün) — wenn GF freigegeben hat
            //   Offen (Initialen grau) — wenn Snapshot noch BERECHNET ist oder gar nicht existiert
            const isHrConfirmed = hrEmpIds.has(e.id);
            const isGfConfirmed = gfEmpIds.has(e.id);
            const isConfirmed   = isGfConfirmed;   // Legacy-Variable für Sortier-/Count-Logik
            // Mindestlohn-Warnung (Walter 20.05.2026): ⚠ wenn unter L-GAV.
            const mwWarn = _lohnMwUnderpaid[e.id];
            const mwIcon = mwWarn
                ? `<span title="${String(mwWarn.message || 'Lohn unter L-GAV-Mindestlohn').replace(/"/g,'&quot;')}" style="color:#dc2626;margin-left:5px;font-size:12px">⚠</span>`
                : '';
            const statusIcon = isHrConfirmed ? '✓✓'
                              : isGfConfirmed ? '✓'
                              : initials.toUpperCase();
            const statusBg   = isHrConfirmed ? '#ece9e2'
                              : isGfConfirmed ? '#dcfce7'
                              : '#e2e8f0';
            const statusFg   = isHrConfirmed ? '#6b6152'
                              : isGfConfirmed ? '#166534'
                              : '#475569';
            const statusText = isHrConfirmed ? 'HR-bestätigt'
                              : isGfConfirmed ? 'GF bestätigt'
                              : (e.employeeNumber || '');
            const statusTextColor = isHrConfirmed ? '#6b6152'
                                  : isGfConfirmed ? '#16a34a'
                                  : '#94a3b8';
            const row = document.createElement('div');
            row.className = 'lohn-emp-row';
            if (e.id === _lohnSelectedEmpId) row.classList.add('lohn-emp-active');
            row.dataset.empId = e.id;
            // MA-Auswahl mirror akWfSelectMa: KEIN voller Listen-Rebuild pro Klick
            // (das verursachte Flackern). Nur Highlight + Status-Bar neu rendern
            // (per-MA-Buttons für den neuen MA) + Lohnzettel direkt laden.
            row.onclick = () => {
                _lohnSelectedEmpId = e.id;
                // Cross-Modul-Sprung (Walter 21.05.2026): zuletzt fokussierter MA
                // → Mitarbeiter/Verträge springen beim Wechsel direkt dorthin.
                window.activeEmpId = e.id;
                localStorage.setItem(`lohnLastEmp_${cid}`, String(e.id));
                highlightLohnEmp(row);
                showLohnVertragInfo(e);
                _lohnWfRenderStatusBar();
                const year  = parseInt(document.getElementById('lohnYearSelect')?.value  || new Date().getFullYear());
                const month = parseInt(document.getElementById('lohnMonthSelect')?.value || (new Date().getMonth()+1));
                lzInit(e.id, cid, year, month);
                loadLohnSlip(e.id, cid, year, month);
            };
            // Walter 18.05.2026: Vertrags-Badge IMMER links, QST-Button IMMER
            // rechts in einem Slot mit fester Breite — so sind die Spalten
            // über alle Zeilen aligniert auch wenn der QST-Button fehlt.
            const hasQst = qstEmpIds.has(e.id);
            const qstBtnHtml = hasQst
                ? `<button title="Quellensteuer bearbeiten" onclick="event.stopPropagation();openQstModal(${e.id},${JSON.stringify({firstName:e.firstName,lastName:e.lastName,zipCode:e.zipCode,city:e.city,nationalityCode:e.nationalityRef?.code??e.nationality,permitTypeName:e.permitType?.name,zivilstand:e.zivilstand})})"
                       style="background:none;border:1px solid #cbd5e1;border-radius:6px;padding:2px 7px;font-size:11px;cursor:pointer;color:#475569;flex-shrink:0">QST</button>`
                : '';
            const corrBadge = e.isCorrection
                ? `<span title="Korrekturlohn (ausgetreten)" style="font-size:9px;font-weight:700;padding:1px 6px;border-radius:8px;background:#fef3c7;color:#92400e;margin-left:4px">Korr.</span>`
                : '';
            row.innerHTML = `
                <div style="width:34px;height:34px;border-radius:50%;background:${statusBg};display:flex;align-items:center;justify-content:center;font-weight:700;font-size:12px;color:${statusFg};flex-shrink:0">
                    ${statusIcon}
                </div>
                <div style="flex:1;min-width:0">
                    <!-- Walter-Vorgabe 07.06.2026: Namen umbrechen statt mit „…" abkürzen. -->
                    <div class="lohn-emp-name" style="font-weight:600;font-size:13px;line-height:1.25;word-break:break-word">${e.firstName} ${e.lastName}${corrBadge}${mwIcon}</div>
                    <div class="lohn-emp-nr" style="font-size:11px;color:${statusTextColor};word-break:break-word">${statusText}${e.isCorrection && e.exitDate ? ' · ausgetreten ' + (e.exitDate.slice(8,10)+'.'+e.exitDate.slice(5,7)+'.'+e.exitDate.slice(0,4)) : ''}</div>
                </div>
                <div style="display:flex;align-items:center;justify-content:flex-end;gap:6px;width:100px;flex-shrink:0">
                    <span class="${modelClass(e.employmentModel)}" style="font-size:10px;font-weight:600;padding:2px 7px;border-radius:10px;min-width:40px;text-align:center">${modelDisplay(e.employmentModel)}</span>
                    <span style="width:38px;display:flex;justify-content:flex-end">${e.isCorrection ? '' : qstBtnHtml}</span>
                </div>`;
            listEl.appendChild(row);
        });

        // Scroll-Position des Refresh wiederherstellen (Walter 20.05.2026):
        // der Rebuild via innerHTML='' setzt scrollTop auf 0 — ohne diese Zeile
        // springt die Liste bei jedem Bestätigen kurz nach oben und scrollt dann
        // wieder runter. Mit der Wiederherstellung bleibt sie ruhig stehen und
        // nur der Auto-Sprung (scrollIntoView) bewegt den Cursor sanft nach unten.
        listEl.scrollTop = _prevScroll;

        // Auto-Select beim Öffnen der Seite. WICHTIG (Walter-Vorgabe 20.05.2026,
        // „genau wie Akonto"): KEIN force-scroll-to-top mehr! Die frühere Zeile
        // `listEl.scrollTop += sel.getBoundingClientRect()...` riss die Liste bei
        // JEDEM Refresh nach oben und zog den gerade bestätigten MA an den Anfang
        // — beim Akonto-Tab gibt es das nicht. Dort scrollt AUSSCHLIESSLICH der
        // Auto-Sprung (scrollIntoView block:'nearest'), sodass der Cursor nach
        // unten wandert und die Liste erst mitscrollt, wenn er unten ankommt.
        // Hier identisch: beim Refresh nicht scrollen; nur beim allerersten
        // Seitenaufruf (Detail-Panel noch leer) den selektierten MA laden +
        // sanft in den sichtbaren Bereich holen.
        if (_lohnSelectedEmpId) {
            const sel = listEl.querySelector(`.lohn-emp-row[data-emp-id="${_lohnSelectedEmpId}"]`);
            if (sel) {
                const vertragPanel = document.getElementById('lohnVertragPanel');
                if (vertragPanel && vertragPanel.style.display === 'none') {
                    sel.click();
                    setTimeout(() => sel.scrollIntoView({ block: 'nearest' }), 50);
                }
            }
        }

        // Status-Bar rendern (EINZIGE Stelle für Pille + Counter + Buttons).
        // Kein loadLohnPeriodBanner mehr — das ist jetzt nur noch ein Shim auf
        // lohnWfRefresh und würde hier eine Endlosrekursion auslösen.
        _lohnWfRenderStatusBar();
        // Zulagen-Lock erneut anwenden — bei Status-Wechsel der Periode muss
        // sich die Card-Sichtbarkeit (+ Erfassen / ✎ / 🗑) aktualisieren.
        if (typeof _akWfApplyZulagenLock === 'function') _akWfApplyZulagenLock();
    } catch(e) {
        listEl.innerHTML = `<div style="padding:20px;color:#dc2626;font-size:13px">Fehler: ${e.message}</div>`;
    }
}

function filterLohnEmpList() {
    const q = (document.getElementById('lohnEmpSearch')?.value || '').toLowerCase();
    document.querySelectorAll('.lohn-emp-row').forEach(r => {
        const name = r.querySelector('.lohn-emp-name')?.textContent?.toLowerCase() || '';
        const nr   = r.querySelector('.lohn-emp-nr')?.textContent?.toLowerCase() || '';
        r.style.display = (!q || name.includes(q) || nr.includes(q)) ? '' : 'none';
    });
}

function showLohnVertragInfo(emp) {
    // Walter 19.05.2026: Card wird gleichzeitig in Definitiv-Tab (lohn*) und
    // Akonto-Tab (akWf*) gerendert, damit beide Bildschirme identisch wirken.
    const targets = [
        {
            panel:  document.getElementById('lohnVertragPanel'),
            empty:  document.getElementById('lohnVertragEmpty'),
            nameEl: document.getElementById('lohnVertragName'),
            infoEl: document.getElementById('lohnVertragInfo'),
        },
        {
            panel:  document.getElementById('akWfVertragPanel'),
            empty:  document.getElementById('akWfVertragEmpty'),
            nameEl: document.getElementById('akWfVertragName'),
            infoEl: document.getElementById('akWfVertragInfo'),
        },
    ].filter(t => t.panel);
    const perPanel = document.getElementById('lohnPeriodToolbar');
    if (targets.length === 0) return;

    if (!emp) {
        targets.forEach(t => {
            if (t.empty) t.empty.style.display = 'block';
            t.panel.style.display = 'none';
        });
        return;
    }

    // Korrekturlohn: kein laufender Vertrag — kompakte Info statt leerem Panel
    if (emp.isCorrection) {
        const exit = emp.exitDate
            ? emp.exitDate.slice(8,10)+'.'+emp.exitDate.slice(5,7)+'.'+emp.exitDate.slice(0,4)
            : '–';
        const modelLabel = { FLEX:'Stundenlohn (FLEX)', MTP:'Mindestpensum (MTP)', FIX:'Festlohn (FIX)', 'FIX-M':'Management (FIX-M)' };
        targets.forEach(t => {
            if (t.nameEl) t.nameEl.innerHTML = `${escHtml(emp.firstName||'')} ${escHtml(emp.lastName||'')}
                <span style="margin-left:8px;font-size:11px;font-weight:600;padding:2px 8px;border-radius:8px;background:#fef3c7;color:#92400e">Korrekturlohn</span>`;
            if (t.infoEl) t.infoEl.innerHTML = `
                <div style="display:grid;grid-template-columns:1fr 1fr;gap:4px 12px">
                    <div>Personal-Nr.: <b style="color:#374151">${escHtml(emp.employeeNumber||'–')}</b></div>
                    <div>Modell: <b style="color:#374151">${escHtml(modelLabel[emp.employmentModel]||emp.employmentModel||'–')}</b></div>
                    <div>Austritt: <b style="color:#374151">${exit}</b></div>
                    <div style="grid-column:1/-1;color:#92400e;font-size:12px">Nur manuelle Zulagen/Abzüge (+ Depot-Refund)</div>
                </div>`;
            if (t.empty) t.empty.style.display = 'none';
            t.panel.style.display = 'block';
        });
        ['lohnStundenCard', 'akWfStundenCard'].forEach(id => {
            const c = document.getElementById(id);
            if (c) c.style.display = 'none';
        });
        if (perPanel) perPanel.style.display = 'block';
        return;
    }

    const contract = (emp.employments || [])
        .filter(c => c.isActive)
        .sort((a,b) => (b.contractStartDate||'') > (a.contractStartDate||'') ? 1 : -1)[0];

    if (!contract) {
        targets.forEach(t => {
            if (t.empty) t.empty.style.display = 'block';
            t.panel.style.display = 'none';
        });
        // Stunden-Card auch ausblenden wenn kein MA ausgewählt
        ['lohnStundenCard', 'akWfStundenCard'].forEach(id => {
            const c = document.getElementById(id);
            if (c) c.style.display = 'none';
        });
        if (perPanel) perPanel.style.display = 'none';
        return;
    }

    const modelLabel = { FLEX:'Stundenlohn (FLEX)', MTP:'Mindestpensum (MTP)', FIX:'Festlohn (FIX)', 'FIX-M':'Management (FIX-M)' };
    const modelClass = (m) => ({ MTP:'model-badge-mtp', FLEX:'model-badge-utp', FIX:'model-badge-fix', 'FIX-M':'model-badge-fix-m' })[m] || '';
    const fmt = d => d ? new Date(d).toLocaleDateString('de-CH') : '–';
    const lohn = contract.salaryType === 'monthly' && contract.monthlySalary
        ? `CHF ${Number(contract.monthlySalary).toFixed(2)} / Monat`
        : contract.hourlyRate ? `CHF ${Number(contract.hourlyRate).toFixed(2)} / Stunde` : '–';

    // Walter-Vorgabe 19.05.2026: Alter zum Periodenbeginn anzeigen — relevant
    // für die Ferienanspruchs-Stufe (5 vs. 6 Wochen) und Mindestlohn-Alters-
    // schwellen (z.B. unter 18 = Lehrlings-Satz). Wir nehmen die aktuell im
    // Lohn-Tab gewählte Periode; wenn keine vorhanden, fallback heute.
    const periodYear  = parseInt(document.getElementById('lohnYearSelect')?.value) || new Date().getFullYear();
    const periodMonth = parseInt(document.getElementById('lohnMonthSelect')?.value) || (new Date().getMonth() + 1);
    const periodStart = new Date(periodYear, periodMonth - 1, 1);
    let alterStr = '–';
    if (emp.dateOfBirth) {
        const dob = new Date(emp.dateOfBirth);
        let age = periodStart.getFullYear() - dob.getFullYear();
        if (periodStart.getMonth() < dob.getMonth()
            || (periodStart.getMonth() === dob.getMonth() && periodStart.getDate() < dob.getDate())) {
            age--;
        }
        alterStr = `${age} J. (am ${periodStart.toLocaleDateString('de-CH')})`;
    }

    const nameHtml = `${emp.firstName} ${emp.lastName}
        <span class="${modelClass(contract.employmentModel)}" style="margin-left:8px;font-size:11px;font-weight:600;padding:2px 8px;border-radius:8px">${modelLabel[contract.employmentModel]||modelDisplay(contract.employmentModel)}</span>`;
    const infoHtml = `
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:4px 12px">
            <div>Personal-Nr.: <b style="color:#374151">${emp.employeeNumber||'–'}</b></div>
            <div>Funktion: <b style="color:#374151">${contract.jobTitle||'–'}</b></div>
            <div>Lohn: <b style="color:#374151">${lohn}</b></div>
            ${contract.guaranteedHoursPerWeek ? `<div>Garantiert: <b style="color:#374151">${contract.guaranteedHoursPerWeek} h/Wo</b></div>` : ''}
            ${contract.employmentPercentage ? `<div>Pensum: <b style="color:#374151">${contract.employmentPercentage}%</b></div>` : ''}
            <div>Vertrag seit: <b style="color:#374151">${fmt(contract.contractStartDate)}</b></div>
            <div title="Alter zum Periodenbeginn — relevant für Ferienanspruch (5/6 Wochen) und Alters-Mindestlöhne">Alter: <b style="color:#374151">${alterStr}</b></div>
            ${contract.probationEndDate ? `<div style="color:#92400e">Probezeit bis: <b>${fmt(contract.probationEndDate)}</b></div>` : ''}
        </div>`;
    targets.forEach(t => {
        if (t.nameEl) t.nameEl.innerHTML = nameHtml;
        if (t.infoEl) t.infoEl.innerHTML = infoHtml;
        if (t.empty)  t.empty.style.display = 'none';
        t.panel.style.display = 'block';
    });
    if (perPanel) {
        perPanel.style.display = 'flex';
        // Monat/Jahr Dropdowns füllen falls noch leer
        const monthSel = document.getElementById('lohnMonthSelect');
        const yearSel  = document.getElementById('lohnYearSelect');
        if (monthSel && monthSel.options.length === 0) {
            const monthNames = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
            monthNames.forEach((m, i) => {
                const o = document.createElement('option');
                o.value = i + 1; o.textContent = m;
                monthSel.appendChild(o);
            });
            monthSel.value = new Date().getMonth() + 1;
        }
        if (yearSel && yearSel.options.length === 0) {
            const curY = new Date().getFullYear();
            for (let y = curY + 1; y >= curY - 2; y--) {
                const o = document.createElement('option');
                o.value = y; o.textContent = y;
                yearSel.appendChild(o);
            }
            yearSel.value = curY;
        }
    }
}

async function loadLohnSlipFromPanel() {
    // lohnBranchSelect ist ein hidden input – fixedCompanyProfileId als Fallback
    const cidRaw = document.getElementById('lohnBranchSelect')?.value;
    const cid    = parseInt(cidRaw) || fixedCompanyProfileId;
    const year   = parseInt(document.getElementById('lohnYearSelect')?.value);
    const month  = parseInt(document.getElementById('lohnMonthSelect')?.value);
    if (!cid || !year || !month) return;
    // Die aktuell ausgewählte Mitarbeiter-ID ist in _lohnSelectedEmpId gespeichert
    // und überlebt das loadLohnList(): die Liste wird neu gerendert (nach Vorname
    // sortiert), und der selektierte MA bekommt die Active-Klasse + wird in den
    // sichtbaren Bereich gescrollt.
    const empId = _lohnSelectedEmpId;
    // Single-Refresh: loadLohnList lädt Periode + Snapshots + MA, füllt
    // _lohnWfData und rendert die Statusbar. KEIN separater
    // loadLohnPeriodBanner-Aufruf mehr (das ist nur noch ein Shim hierauf).
    await loadLohnList();
    // Lohnzettel für die neue Periode neu berechnen
    if (empId) {
        lzInit(empId, cid, year, month);
        loadLohnSlip(empId, cid, year, month);
    }
}

function highlightLohnEmp(row) {
    document.querySelectorAll('.lohn-emp-row').forEach(r => r.classList.remove('lohn-emp-active'));
    row.classList.add('lohn-emp-active');
}

// Request-Token gegen Race-Conditions: schneller MA-Wechsel kann mehrere
// loadLohnSlip()-Aufrufe parallel auslösen, deren async Saldo-Fetches in
// beliebiger Reihenfolge zurückkommen. Ohne Token überschreibt eine ältere
// Antwort die neuere → Button-State zeigt den falschen MA.
let _lohnSlipReqToken = 0;

async function loadLohnSlip(employeeId, companyId, year, month) {
    document.getElementById('lohnSlipCard').style.display  = 'none';
    document.getElementById('lohnSlipEmpty').style.display = 'flex';
    document.getElementById('lohnSlip').innerHTML = '<div style="padding:40px;text-align:center;color:#94a3b8">Berechne…</div>';
    document.getElementById('lohnSlipCard').style.display  = 'block';
    document.getElementById('lohnSlipEmpty').style.display = 'none';
    // Aktionsbuttons werden NICHT mehr hier gesetzt — sie leben ausschliesslich
    // in #lohnDefinitivStatusBar (_lohnWfRenderStatusBar). loadLohnSlip rendert
    // nur noch den Lohnzettel selbst (Walter-Vorgabe 20.05.2026).
    const myToken = ++_lohnSlipReqToken;

    try {
        // Cache-Buster + cache:no-store damit Browser nach Absenz-/Stempelzeit-
        // Änderungen NICHT den alten gecachten Lohnzettel zurückgibt.
        const ts = Date.now();
        const isCorr = _lohnIsCorrection(employeeId);
        const corrQ = isCorr ? '&isCorrection=true' : '';
        const res  = await fetch(`/api/payroll/calculate?employeeId=${employeeId}&year=${year}&month=${month}&companyProfileId=${companyId}${corrQ}&_=${ts}`,
                                  { headers: ah(), cache: 'no-store' });
        if (!res.ok) {
            const text = await res.text();
            let msg = `HTTP ${res.status}`;
            try { const j = JSON.parse(text); msg = j.error || j.message || j.title || text; } catch { msg = text.substring(0, 400); }
            throw new Error(msg);
        }
        const slip = await res.json();
        if (slip && slip.pausiert) {
            const bisTxt = slip.pauseBis
                ? ` bis ${new Date(slip.pauseBis + 'T00:00:00').toLocaleDateString('de-CH')}`
                : ' (läuft noch)';
            document.getElementById('lohnSlip').innerHTML = `
                <div style="padding:40px;text-align:center">
                    <div style="font-size:44px;margin-bottom:12px">🏥</div>
                    <div style="font-size:18px;font-weight:700;color:#1e293b;margin-bottom:8px">Versicherungs-Übergabe aktiv</div>
                    <div style="color:#475569;line-height:1.6">
                        Lohn läuft über die KTG-Versicherung<br>
                        <span style="color:#94a3b8">seit ${new Date(slip.pauseVon + 'T00:00:00').toLocaleDateString('de-CH')}${bisTxt}</span>
                    </div>
                    <div style="margin-top:16px;font-size:13px;color:#64748b">
                        Für diese Periode erfolgt keine Lohnabrechnung durch den Arbeitgeber.<br>
                        Zum Deaktivieren: beim Mitarbeiter im Absenzen-Tab die Pause bearbeiten.
                    </div>
                </div>`;
            lohnCurrentSlip = null;
            // Während KTG-Pause keine Abrechnung. confirmLohn() bricht bei
            // lohnCurrentSlip==null sauber ab; die Statusbar bleibt sichtbar.
            return;
        }
        lohnCurrentSlip = { ...slip, employeeId, companyId, year, month, isCorrection: !!(slip.isCorrection || isCorr) };
        // Stale-Antwort verwerfen, falls inzwischen ein neuer Aufruf läuft.
        if (myToken !== _lohnSlipReqToken) return;
        renderLohnSlip(lohnCurrentSlip);
        // Button-Sichtbarkeit kommt AUSSCHLIESSLICH aus _lohnWfRenderStatusBar
        // (gespeist aus _lohnWfData). loadLohnSlip toggelt keine Buttons mehr —
        // das war die Quelle der wiederkehrenden „Button fehlt / verdeckt"-Bugs
        // (Walter-Vorgabe 20.05.2026: Definitiv = Akonto-Architektur).
    } catch(e) {
        document.getElementById('lohnSlip').innerHTML = `<div style="padding:40px;color:#dc2626">Fehler: ${e.message}</div>`;
    }
}

// Toggle: Ferien-Kürzung anwenden (Stufe 2)
// Wenn aktiviert: vom aktuellen Ferien-Tage-Saldo wird die vorgeschlagene
// Kürzung abgezogen. Beim nächsten „Lohn bestätigen" wird der reduzierte
// ferienTageSaldoNeu (= s.ferienTageSaldoNeu, siehe confirmLohn) in
// PayrollSaldo persistiert.
//
// Walter-Bug 18.05.2026: hier waren zwei Fehler drin —
//   1) `window.currentLohnSlip` (undefined) statt `lohnCurrentSlip`
//   2) Re-Render rief `renderLohnAbrechnung` (existiert nicht) statt `renderLohnSlip`
// Resultat: Klick auf die Checkbox hatte sichtbar gar keinen Effekt.
function toggleFerienKuerzung(checkbox, vorschlagTage) {
    const slip = lohnCurrentSlip;
    if (!slip) return;
    if (checkbox.checked) {
        slip.ferienKuerzungAngewendet = true;
        slip.ferienKuerzungAngewendetTage = vorschlagTage;
        slip.ferienTageSaldoNeu = (Number(slip.ferienTageSaldoNeu) || 0) - Number(vorschlagTage);
    } else {
        slip.ferienKuerzungAngewendet = false;
        slip.ferienKuerzungAngewendetTage = 0;
        slip.ferienTageSaldoNeu = (Number(slip.ferienTageSaldoNeu) || 0) + Number(vorschlagTage);
    }
    // Re-render damit der neue Saldo angezeigt wird
    if (typeof renderLohnSlip === 'function') renderLohnSlip(slip);
}

async function reopenLohn() {
    if (!lohnCurrentSlip) return;
    const s = lohnCurrentSlip;
    if (!confirm('Bestätigten Lohnzettel wieder eröffnen?\n\nDer Saldo wird zurück auf "draft" gesetzt, Lohnabtretungs-Einträge werden rückgebucht. Du kannst danach Absenzen/Zulagen bearbeiten und neu bestätigen.')) return;

    const periode = await ensurePeriode(s.companyId, s.year, s.month);
    if (!periode) return;

    try {
        const res = await fetch('/api/payroll/reopen', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                employeeId:       s.employeeId,
                companyProfileId: s.companyId,
                payrollPeriodeId: periode.id,
                year:             s.year,
                month:            s.month
            })
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({ error: res.statusText }));
            throw new Error(err.error || err.message || 'Fehler beim Wieder-Eröffnen');
        }
        showToast('Lohnzettel wieder eröffnet ↻', 'success');
        // Single-Refresh + Slip neu laden (Statusbar zeigt wieder „✓ bestätigen").
        await lohnWfRefresh();
        await loadLohnSlip(s.employeeId, s.companyId, s.year, s.month);
    } catch(e) {
        alert(e.message);
    }
}

function fmt(n) {
    if (n == null) return '';
    return Number(n).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
function fmtNum(n, decimals=2) {
    if (n == null) return '';
    return Number(n).toFixed(decimals);
}

/// Vom Lohnzettel direkt zur MA-Maske springen + Bankverbindungs-Modal öffnen.
/// Wird von der "Auszahlung an"-Sektion aufgerufen, wenn der MA keine
/// Bankverbindung hat (warning=true). selectEmployee + showPage sind aus
/// employees.js / index.html-Navigation.
async function jumpToMaForBankEntry(employeeId) {
    if (!employeeId) return;
    showPage('mitarbeiter');
    try {
        await selectEmployee(employeeId);
    } catch (e) { /* Ignorieren — Modal trotzdem öffnen */ }
    // Kurz warten, damit selectedEmployeeId im Edit-Kontext gesetzt ist
    setTimeout(() => {
        if (typeof openBankAccountModal === 'function') openBankAccountModal(null);
    }, 50);
}

// Renders den Lohnzettel in das angegebene Target-Element. Wird ohne 2. Parameter
// in den Standard-Container '#lohnSlip' (Definitiv-Modul) gerendert; mit explizitem
// targetEl in beliebigen anderen Mount-Point — z.B. Akonto-Workflow-Detail-Panel.
// Walter-Vorgabe 30.05.2026: zwischen MA-Info-Card und Zulagen/Abzüge-Card
// kommt eine kompakte „Stunden Lohnperiode"-Card, die zeigt:
//   • Pro-Rata-Soll Periode
//   • Abzug Ferien (1/7-Kalender)
//   • Abzug Krank/Unfall (1/5-Werktag)
//   • Effektives Soll
//   • Gestempelt (Ist) + Absenz-Gutschrift
//   • Vormonat-Saldo
//   • Mehrstunden bzw. Saldo Lohnperiode
// Nur bei MTP / FIX / FIX-M sinnvoll (UTP hat kein Soll).
function renderStundenCard(s) {
    const cards = [
        document.getElementById('lohnStundenCard'),
        document.getElementById('akWfStundenCard'),
    ].filter(c => c);
    if (cards.length === 0) return;
    const model = (s && s.employmentModel) || '';
    const isMtp = model === 'MTP';
    const isFix = model === 'FIX' || model === 'FIX-M';
    if (!isMtp && !isFix) {
        cards.forEach(c => c.style.display = 'none');
        return;
    }
    const soll       = Number(s.sollStunden ?? 0);
    const sollVoll   = Number(s.sollStundenVoll ?? soll);
    const ferienRed  = Number(s.sollFerienReduktion ?? 0);
    // Krank/Unfall-Reduktion ist sollVoll - ferien - soll (Restdifferenz)
    const krankUnfallRed = Math.max(0, sollVoll - ferienRed - soll);
    const worked     = Number(s.workedHours ?? 0);
    const absenz     = Number(s.absenzGutschrift ?? 0);
    const ist        = worked + absenz;
    const diff       = ist - soll;
    const vor        = Number(s.vormonatHourSaldo ?? 0);
    const saldo      = Number(s.neuerHourSaldo ?? 0);
    // Walter-Vorgabe 30.05.2026: bei MTP werden Mehrstunden ausbezahlt → die
    // Anzeige unten zeigt den Auszahlungs-Wert (mehrstunden). Der neuerHourSaldo
    // ist in diesem Fall 0 (oder negativ, wenn der MA unter dem Soll lag).
    const mehrstd    = Number(s.mehrstunden ?? 0);
    const period     = s.periodLabel || '';

    const fNum = (n, decimals = 2) =>
        Number(n).toLocaleString('de-CH', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
    const signed = (n) => {
        if (n > 0) return `<span style="color:#16a34a">+${fNum(n)} h</span>`;
        if (n < 0) return `<span style="color:#dc2626">${fNum(n)} h</span>`;
        return `<span style="color:#94a3b8">0.00 h</span>`;
    };
    const row = (label, value, opts = {}) => `
        <div style="display:flex;justify-content:space-between;align-items:baseline;padding:4px 0;${opts.bold ? 'border-top:1px solid #e2e8f0;margin-top:4px;padding-top:8px;font-weight:600' : ''}">
            <span style="color:${opts.muted ? '#94a3b8' : '#475569'};font-size:12px">${label}</span>
            <span style="color:${opts.color || '#334155'};font-size:12.5px;font-weight:${opts.bold ? 700 : 500};white-space:nowrap">${value}</span>
        </div>`;

    const html = `
        <div style="padding:13px 18px;border-bottom:1px solid #f1f5f9;display:flex;align-items:center;justify-content:space-between">
            <div style="font-weight:600;font-size:13px;color:#475569">Stunden — ${period}</div>
        </div>
        <div style="padding:8px 18px 14px">
            ${row('Soll voll (Pro-Rata)', fNum(sollVoll) + ' h')}
            ${ferienRed > 0 ? row('− Ferien (1/7-Kalender)', '−' + fNum(ferienRed) + ' h', { color:'#dc2626' }) : ''}
            ${krankUnfallRed > 0 ? row(isMtp ? '− Krank/Unfall (1/5-Werktag)' : '− Krank/Unfall', '−' + fNum(krankUnfallRed) + ' h', { color:'#dc2626' }) : ''}
            ${row('Effektives Soll', fNum(soll) + ' h', { bold:true })}
            ${row('Gestempelt (Ist)', fNum(worked) + ' h')}
            ${absenz > 0 ? row('+ Absenz-Gutschrift', '+' + fNum(absenz) + ' h', { muted:true }) : ''}
            <!-- Walter-Vorgabe 30.05.2026: drei Saldo-Zeilen statt einer.
                 Saldo aktueller Monat = Ist − Soll
                 Saldo Vormonat        = vormonatHourSaldo
                 Saldo Lohnperiode     = neuer Saldo (nach Auszahlung bei MTP)
                 Bei MTP mit Auszahlung wird dazwischen "Mehrstunden ausbezahlt"
                 eingeblendet (grün), damit klar ist wie das Mehr verteilt wurde. -->
            ${row('Saldo aktueller Monat', signed(diff), { bold:true })}
            ${row('Saldo Vormonat',        signed(vor))}
            ${isMtp && mehrstd > 0
                ? row('Mehrstunden ausbezahlt', signed(mehrstd), { color:'#16a34a' })
                : ''
            }
            ${row('Saldo Lohnperiode', signed(saldo), { bold:true })}
        </div>`;
    cards.forEach(c => {
        c.innerHTML = html;
        c.style.display = 'block';
    });
}

function renderLohnSlip(s, targetEl) {
    const mount = targetEl || document.getElementById('lohnSlip');
    if (!mount) return;
    // Walter-Vorgabe 30.05.2026: Stunden-Card neben dem Lohnzettel mit aktualisieren
    try { renderStundenCard(s); } catch(e) { /* best-effort, nicht den Slip brechen */ }
    // Mindestlohn-Banner (Walter 20.05.2026): roter Hinweis wenn der aktuell
    // gewählte MA unter dem L-GAV-Mindestlohn liegt. Quelle: _lohnMwUnderpaid
    // (check-period). Bestätigen ist server- UND clientseitig gesperrt.
    const _mwWarn = (typeof _lohnMwUnderpaid !== 'undefined') ? _lohnMwUnderpaid[_lohnSelectedEmpId] : null;
    let _mwHead = '⚠ Mindestlohn unterschritten — Bestätigen gesperrt';
    if (_mwWarn) {
        if (_mwWarn.problem === 'NO_SALARY')   _mwHead = '⚠ Lohnsumme fehlt — Bestätigen gesperrt';
        else if (_mwWarn.problem === 'QST_OFFEN') _mwHead = '⚠ QST-Pflicht offen — Bestätigen gesperrt';
    }
    // Walter-Vorgabe 26.05.2026: bei QST_OFFEN zusätzlich Sprung-Button zum
    // MA-QST-Tab (öffnet Mitarbeiter-Modul + Tab + Schnell-Buttons).
    const _qstSprung = _mwWarn && _mwWarn.problem === 'QST_OFFEN'
        ? `<div style="margin-top:6px"><button onclick="window.activeEmpId=${_lohnSelectedEmpId};showPage('mitarbeiter');setTimeout(()=>switchEmpTab('quellensteuer'),250)" style="background:#dc2626;color:#fff;border:none;padding:6px 12px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer">→ QST im MA-Tab erfassen</button></div>`
        : '';
    const _mwBanner = (_mwWarn && !s.isCorrection)
        ? `<div style="background:#fee2e2;border:1px solid #fca5a5;color:#991b1b;border-radius:8px;padding:8px 12px;margin-bottom:8px;font-size:12.5px;font-weight:600">${_mwHead}<div style="font-weight:400;margin-top:2px">${String(_mwWarn.message || '').replace(/</g,'&lt;')}</div>${_qstSprung}</div>`
        : '';
    const _corrBanner = s.isCorrection
        ? `<div style="background:#fffbeb;border:1px solid #fde68a;color:#92400e;border-radius:8px;padding:8px 12px;margin-bottom:8px;font-size:12.5px;font-weight:600">Korrekturlohn / Sonderlohn<span style="font-weight:400;display:block;margin-top:2px">Nur manuelle Zulagen/Abzüge (+ Depot-Refund). Keine Stempelzeiten, Absenzen oder Saldo-Fortschreibung.</span></div>`
        : '';
    // Helfer: "Gerechnet" — Wert wenn vorhanden und ungleich Betrag, sonst leer
    const renderAccrued = (l) => {
        const acc = l.accrued != null ? Number(l.accrued) : Number(l.betrag);
        const pay = Number(l.betrag);
        // Gerechnet zeigen wenn ≠ Ausbezahlt (sonst redundant)
        if (Math.abs(acc - pay) < 0.005) return '';
        return fmt(acc);
    };
    const lohnRows = s.lohnLines.map(l => `
        <tr>
            <td class="ls-desc">${l.bezeichnung}</td>
            <td class="ls-num">${l.anzahl != null ? fmtNum(l.anzahl) : ''}</td>
            <td class="ls-num">${l.prozent != null ? fmtNum(l.prozent, 3) : ''}</td>
            <td class="ls-num">${l.basis   != null ? fmt(l.basis)   : ''}</td>
            <td class="ls-amt" style="color:#64748b">${renderAccrued(l)}</td>
            <td class="ls-amt">${Number(l.betrag) === 0 ? '<span style="color:#94a3b8">0.00</span>' : fmt(l.betrag)}</td>
        </tr>`).join('');

    const abzugRows = s.abzugLines.map(l => `
        <tr>
            <td class="ls-desc">${l.bezeichnung}</td>
            <td class="ls-num"></td>
            <td class="ls-num">${l.prozent != null ? fmtNum(l.prozent, 3) : ''}</td>
            <td class="ls-num">${l.basis   != null ? fmt(l.basis) : ''}</td>
            <td class="ls-amt"></td>
            <td class="ls-amt" style="color:#dc2626">${fmt(l.betrag)}</td>
        </tr>`).join('');

    const salutation = s.salutation === 'Frau' ? 'Frau' : s.salutation === 'Herr' ? 'Herr' : '';

    mount.innerHTML = `
    <div class="ls-wrap" style="padding-top:2px;padding-bottom:3px">
        ${_corrBanner}
        ${_mwBanner}
        <!-- Header-Div und Sektion-Titel (Lohn, Abzüge) weggelassen
             (Walter-Vorgabe 01.06.2026): Periode/Filiale stehen bereits
             im Page-Header, „Lohn"/„Abzüge" sind durch Total-Zeilen klar
             erkennbar. Platzersparnis. -->

        <!-- Tabelle -->
        <table class="ls-table">
            <thead>
                <tr>
                    <th class="ls-desc">Bezeichnung</th>
                    <th class="ls-num">Anzahl</th>
                    <th class="ls-num">%</th>
                    <th class="ls-num">Basis</th>
                    <th class="ls-amt" style="color:#64748b">Gerechnet</th>
                    <th class="ls-amt">Ausbezahlt</th>
                </tr>
            </thead>
            <tbody>
                ${lohnRows}
                <tr class="ls-total-row">
                    <td colspan="4" class="ls-desc">Total Lohn</td>
                    <td class="ls-amt"></td>
                    <td class="ls-amt">${fmt(s.totalLohn)}</td>
                </tr>

                ${s.abzugLines.length > 0 ? `
                <tr><td colspan="6" style="height:3px"></td></tr>
                ${s.usingDefaultDeductions ? `<tr><td colspan="6" style="text-align:right;padding:2px 4px"><span style="font-size:10px;font-weight:500;color:#b45309;background:#fef3c7;border:1px solid #fcd34d;border-radius:4px;padding:1px 6px">CH-Standard 2026</span></td></tr>` : ''}
                ${abzugRows}
                <tr class="ls-total-row">
                    <td colspan="4" class="ls-desc">Total Abzüge</td>
                    <td class="ls-amt"></td>
                    <td class="ls-amt" style="color:#dc2626">${fmt(s.totalAbzuege)}</td>
                </tr>
                ${s.usingDefaultDeductions ? `<tr><td colspan="6" style="font-size:10px;color:#92400e;padding:2px 8px 6px">⚠ Standardsätze AHV 5.3 % / ALV 1.1 % – bitte unter Filialen &gt; Abzüge konfigurieren</td></tr>` : ''}
                ` : ''}

                <tr><td colspan="6" style="height:3px"></td></tr>
                <tr class="ls-netto-row">
                    <td colspan="4" class="ls-desc">Nettolohn</td>
                    <td class="ls-amt"></td>
                    <td class="ls-amt">${fmt(s.nettolohn)}</td>
                </tr>

                ${(s.zulagenExtraLines?.length > 0 || s.abzuegeExtraLines?.length > 0) ? `
                <tr><td colspan="6" style="height:2px"></td></tr>
                ${s.zulagenExtraLines?.length > 0 ? `
                <tr class="ls-section-hd"><td colspan="6">Weitere Zahlungen</td></tr>
                ${s.zulagenExtraLines.map(l => `
                <tr>
                    <td class="ls-desc">${l.bezeichnung}</td>
                    <td colspan="4"></td>
                    <td class="ls-amt" style="color:#16a34a">+${fmt(l.betrag)}</td>
                </tr>`).join('')}` : ''}
                ${s.abzuegeExtraLines?.length > 0 ? `
                <tr class="ls-section-hd"><td colspan="6">Weitere Abzüge</td></tr>
                ${s.abzuegeExtraLines.map(l => `
                <tr class="ls-extra-abzug">
                    <td class="ls-desc" style="color:#dc2626">${l.bezeichnung}</td>
                    <td colspan="4"></td>
                    <td class="ls-amt" style="color:#dc2626">${fmt(l.betrag)}</td>
                </tr>`).join('')}` : ''}
                <tr class="ls-netto-row" style="border-top:2px solid #6b6152">
                    <td colspan="4" class="ls-desc">Auszahlungsbetrag</td>
                    <td class="ls-amt"></td>
                    <td class="ls-amt">${fmt(s.auszahlungsbetrag)}</td>
                </tr>` : ''}

            </tbody>
        </table>

        <!-- Stunden-Übersicht (MTP, FIX, FIX-M): Soll | Ist | Diff | Übertrag | Saldo -->
        ${(s.employmentModel === 'MTP' || s.employmentModel === 'FIX' || s.employmentModel === 'FIX-M') ? (() => {
            const soll    = Number(s.sollStunden ?? 0);
            const worked  = Number(s.workedHours ?? 0);       // gestempelte Stunden
            const absenz  = Number(s.absenzGutschrift ?? 0);  // Krankheit/Feiertag/Schulung etc.
            const ist     = worked + absenz;                  // Ist = Gestempelt + Absenz-Gutschrift
            const diff    = ist - soll;
            const vor     = Number(s.vormonatHourSaldo ?? 0);
            const saldo   = Number(s.neuerHourSaldo ?? 0);

            // Einheit steht im Titel ("Arbeitsstunden") — daher keine "h" hinter den Zahlen.
            const plainH = (v) =>
                `<span style="color:#334155;white-space:nowrap">${fmtNum(v)}</span>`;
            const signedH = (v) => {
                if (v < 0) return `<span style="color:#dc2626;white-space:nowrap">${fmtNum(v)}</span>`;
                if (v > 0) return `<span style="color:#16a34a;white-space:nowrap">+${fmtNum(v)}</span>`;
                return `<span style="color:#cbd5e1;white-space:nowrap">—</span>`;
            };
            const signedBold = (v) => {
                if (v < 0) return `<span style="color:#dc2626;font-weight:700;white-space:nowrap">${fmtNum(v)}</span>`;
                if (v > 0) return `<span style="color:#16a34a;font-weight:700;white-space:nowrap">+${fmtNum(v)}</span>`;
                return `<span style="color:#334155;font-weight:700;white-space:nowrap">0.00</span>`;
            };

            // Untertitel "davon …" nur wenn Absenz-Gutschrift > 0.
            // Aufschlüsselung pro AbsenceType (KRANK, FEIERTAG, FERIEN,
            // SCHULUNG, etc.) damit Walter den exakten Beitrag jeder Absenz-
            // Art zur IST-Stundenzahl sehen kann.
            const breakdown = s.absenzBreakdown || {};
            const ABSENZ_LABEL_FOR_BREAKDOWN = {
                KRANK:      'Krank',
                UNFALL:     'Unfall',
                SCHULUNG:   'Schulung',
                MILITAER:   'Militär',
                FEIERTAG:   'Feiertag',
                FERIEN:     'Ferien',
                NACHT_KOMP: 'Nacht-Komp.',
                MUTTERSCHAFT: 'Mutterschaft',
                VATERSCHAFT:  'Vaterschaft',
                UNBEZ_URLAUB: 'unbez. Urlaub',
            };
            const parts = Object.entries(breakdown)
                .filter(([_, v]) => Number(v) > 0)
                .sort(([, a], [, b]) => Number(b) - Number(a))
                .map(([k, v]) => `${ABSENZ_LABEL_FOR_BREAKDOWN[k] || k} ${fmtNum(v)}`);
            const istDetail = absenz > 0
                ? `<div style="font-size:11px;color:#94a3b8;margin-top:2px;text-align:left;white-space:nowrap">
                       gestempelt ${fmtNum(worked)}${parts.length ? ' + ' + parts.join(' + ') : ' + Absenz ' + fmtNum(absenz)}
                   </div>`
                : '';

            // Untertitel "Soll-Berechnung" — MTP mit Ferien-Reduktion
            const sollVoll  = Number(s.sollStundenVoll  ?? 0);
            const sollRed   = Number(s.sollFerienReduktion ?? 0);
            const guarH     = Number(s.guaranteedHoursPerWeek ?? 0);
            const ferTage   = Number(s.ferienTageInPeriode ?? 0);
            const sollDetail = (s.employmentModel === 'MTP' && sollVoll > 0 && sollRed > 0)
                ? `<span style="font-size:11px;color:#94a3b8;margin-left:10px;white-space:nowrap"
                          title="Garantie ${guarH} h/Woche · 52/12 = ${fmtNum(sollVoll)} Sollstunden\n${ferTage} Ferientage × ${guarH}/7 = ${fmtNum(sollRed)} Stunden">
                       ${fmtNum(sollVoll)} Soll <span style="color:#dc2626">−${fmtNum(sollRed)}</span>
                       <span style="color:#94a3b8">(${guarH}/7 × ${ferTage} Ferientage)</span>
                   </span>`
                : '';

            return `
            <table class="ls-table" style="margin-top:3px">
                <thead>
                    <tr class="ls-col-hd">
                        <th style="text-align:left">Stunden-Übersicht</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Soll</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Ist</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Differenz</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Vormonat</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Saldo</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td class="ls-desc">Arbeitsstunden${sollDetail}</td>
                        <td class="ls-num">${plainH(soll)}</td>
                        <td class="ls-num">${plainH(ist)}</td>
                        <td class="ls-num">${signedH(diff)}</td>
                        <td class="ls-num">${signedH(vor)}</td>
                        <td class="ls-amt">${signedBold(saldo)}</td>
                    </tr>
                    ${istDetail ? `<tr><td colspan="6" style="padding-top:0;padding-bottom:0">${istDetail}</td></tr>` : ''}
                </tbody>
            </table>`;
        })() : ''}

        <!-- Saldi: Vorperiode / Aktuell / Saldo aktuelle Periode -->
        ${(() => {
            // Vertragstyp-basierte Saldi-Anzeige (synchron zur Saldo-Vortrag-
            // Tabelle). Walter will, dass relevante Saldi IMMER sichtbar sind,
            // auch wenn der Wert in der aktuellen Periode 0 ist — sonst sieht
            // man nicht dass z.B. der 13. ML-Saldo gerade ausbezahlt wurde
            // und auf 0 steht.
            const model      = s.employmentModel || '';
            const isUtp      = model === 'FLEX';
            const isMtp      = model === 'MTP';
            const isFixModel = model === 'FIX' || model === 'FIX-M';
            const isUtpOrMtp = isUtp || isMtp;

            // Welche Saldi sind für diesen Vertragstyp relevant?
            const showNacht      = true;                   // alle (Walter)
            const showFerienTage = true;                   // alle
            const showFeiertag   = isFixModel;             // FIX/FIX-M
            const showFerienGeld = isUtpOrMtp;             // UTP/MTP
            const show13Saldo    = !isUtp;                 // MTP/FIX/FIX-M

            const hasSaldi = showNacht || showFerienTage || showFeiertag || showFerienGeld || show13Saldo;
            if (!hasSaldi) return '';

            // Einheiten stehen jetzt im Titel — Zahlen ohne Suffix.
            const pos = (v) => v > 0 ? `<span style="color:#16a34a;white-space:nowrap">+${fmtNum(v)}</span>` : `<span style="color:#cbd5e1">—</span>`;
            const neg = (v) => v > 0 ? `<span style="color:#dc2626;white-space:nowrap">−${fmtNum(v)}</span>` : `<span style="color:#cbd5e1">—</span>`;
            const rows = [];

            // ── Nacht-Saldo (MTP, FIX, FIX-M) ───────────────────────────
            // Immer anzeigen für relevante Vertragstypen — auch wenn Saldo 0.
            if (showNacht) {
                rows.push(`<tr>
                    <td class="ls-desc" style="color:#5b21b6">Nacht-Saldo (Stunden)</td>
                    <td class="ls-num" style="color:#64748b;white-space:nowrap">${fmtNum(s.vormonatNachtSaldo ?? 0)}</td>
                    <td class="ls-num">${pos(s.nightBonus ?? 0)}</td>
                    <td class="ls-num">${neg(s.nachtKompStunden ?? 0)}</td>
                    <td class="ls-amt" style="color:#5b21b6;font-weight:600;white-space:nowrap">${fmtNum(s.neuerNachtSaldo ?? 0)}</td>
                </tr>`);
            }

            // ── Ferien-Tage ──────────────────────────────────────────────
            // Für alle Vertragstypen relevant. Immer anzeigen, auch wenn 0.
            if (showFerienTage) {
                const ftSaldo = s.ferienTageSaldoNeu ?? 0;
                rows.push(`<tr>
                    <td class="ls-desc" style="color:#15803d">Ferien-Saldo Tage (${s.vacationWeeks || 5} Wo.)</td>
                    <td class="ls-num" style="color:#64748b;white-space:nowrap">${fmtNum(s.vormonatFerienTage ?? 0)}</td>
                    <td class="ls-num">${pos(s.ferienTageAccrual ?? 0)}</td>
                    <td class="ls-num">${neg(s.ferienTageGenommen ?? 0)}</td>
                    <td class="ls-amt" style="color:${ftSaldo >= 0 ? '#15803d' : '#dc2626'};font-weight:600;white-space:nowrap">${fmtNum(ftSaldo)}</td>
                </tr>`);
            }

            // ── Ferienanspruch-Kürzung (Art. 329b OR) ────────────────────
            // Stufe 2: Vorschlag anzeigen + Toggle "anwenden"
            if (s.ferienKuerzung) {
                const k = s.ferienKuerzung;
                const fmtD = (d) => {
                    if (!d) return '';
                    const dt = new Date(d);
                    return dt.toLocaleDateString('de-CH');
                };
                const reasonParts = [];
                if (k.kuerzungUnverschuldet12tel > 0)
                    reasonParts.push(`${fmtNum(k.tageKrankUnfall)} Tage Krank/Unfall → ${k.kuerzungUnverschuldet12tel}/12`);
                if (k.kuerzungSelbst12tel > 0)
                    reasonParts.push(`${fmtNum(k.tageUnbezUrlaub)} Tage unbez. Urlaub → ${k.kuerzungSelbst12tel}/12`);
                if (k.kuerzungSchwanger12tel > 0)
                    reasonParts.push(`${fmtNum(k.tageMutterschaft)} Tage Mutterschaft → ${k.kuerzungSchwanger12tel}/12`);

                const isApplied = !!s.ferienKuerzungAngewendet;
                const toggleId = `ferienkuerzungToggle_${(s.employeeId || '')}_${(s.year || '')}_${(s.month || '')}`;

                rows.push(`<tr>
                    <td colspan="5" style="background:#fef3c7;border-left:3px solid #d97706;padding:10px 14px;border-radius:6px">
                        <div style="display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap">
                            <div style="font-size:12px;color:#92400e">
                                <strong>⚠️ Ferienanspruch-Kürzung möglich</strong> (Art. 329b OR)
                                <div style="font-size:11px;color:#78350f;margin-top:3px">
                                    Dienstjahr ${fmtD(k.dienstjahrVon)} – ${fmtD(k.dienstjahrBis)}:
                                    ${reasonParts.join(' · ')}
                                </div>
                                <div style="font-size:11px;color:#78350f;margin-top:3px">
                                    <strong>Vorschlag</strong>: ${k.totalKuerzung12tel}/12 = <strong>${fmtNum(k.vorschlagTage)} Tage</strong> Kürzung
                                </div>
                            </div>
                            <label style="display:flex;align-items:center;gap:6px;font-size:12px;color:#92400e;cursor:pointer;white-space:nowrap">
                                <input type="checkbox" id="${toggleId}" ${isApplied ? 'checked' : ''} onchange="toggleFerienKuerzung(this, ${k.vorschlagTage})">
                                Kürzung anwenden
                            </label>
                        </div>
                    </td>
                </tr>`);
            }

            // ── Feiertag-Tage (nur FIX/FIX-M) ──────────────────────────
            // Immer anzeigen für FIX/FIX-M, auch wenn Saldo 0.
            if (showFeiertag) {
                rows.push(`<tr>
                    <td class="ls-desc" style="color:#b45309">Feiertag-Saldo Tage</td>
                    <td class="ls-num" style="color:#64748b;white-space:nowrap">${fmtNum(s.vormonatFeiertagTage ?? 0)}</td>
                    <td class="ls-num">${pos(s.feiertagTageAccrual ?? 0)}</td>
                    <td class="ls-num">${neg(s.feiertagTageGenommen ?? 0)}</td>
                    <td class="ls-amt" style="color:${(s.feiertagTageSaldoNeu ?? 0) >= 0 ? '#b45309' : '#dc2626'};font-weight:600;white-space:nowrap">${fmtNum(s.feiertagTageSaldoNeu ?? 0)}</td>
                </tr>`);
            }

            // ── Ferien-Geld (UTP/MTP) ────────────────────────────────────
            // Für MTP/UTP IMMER anzeigen — auch wenn in einem Monat keine
            // Gutschrift dazu kommt, ist der Saldo aus Vormonaten relevant.
            // FIX/FIX-M haben kein Ferien-Geld (Ferien im Monatslohn enthalten).
            if (showFerienGeld) {
                const accrual = Math.round((s.ferienGeldAccrual ?? (s.ferienGeldSaldoNeu + (s.ferienGeldAuszahlung ?? 0) - s.vormonatFerienGeld)) * 100) / 100;
                rows.push(`<tr>
                    <td class="ls-desc" style="color:#15803d">Ferien-Geld (CHF)</td>
                    <td class="ls-num" style="color:#64748b;white-space:nowrap">${fmtNum(s.vormonatFerienGeld ?? 0)}</td>
                    <td class="ls-num">${pos(accrual)}</td>
                    <td class="ls-num">${neg(s.ferienGeldAuszahlung ?? 0)}</td>
                    <td class="ls-amt" style="color:#15803d;font-weight:600;white-space:nowrap">${fmtNum(s.ferienGeldSaldoNeu ?? 0)}</td>
                </tr>`);
            }

            // ── 13. Monatslohn-Saldo (MTP, FIX, FIX-M) ───────────────────
            // Im Nicht-Auszahlungsmonat: Vormonat (prev) | Akt. Zuwachs |
            // — | Saldo neu (akkumuliert).
            // Im Auszahlungsmonat: Vormonat (vor Auszahlung) | Akt. Zuwachs |
            // Bezogen (Auszahlung) | Saldo neu = 0. Backend liefert die
            // Display-Werte explizit, damit nach dem Saldo-Reset alle vier
            // Spalten weiterhin nachvollziehbar sind.
            if (show13Saldo) {
                const payout = s.thirteenthPayout ?? 0;
                if (payout > 0) {
                    // Auszahlungsmonat: Werte aus *ForDisplay nehmen
                    const prevDisp    = s.thirteenthPrevForDisplay ?? 0;
                    const accrualDisp = s.thirteenthAccrualForDisplay ?? 0;
                    rows.push(`<tr>
                        <td class="ls-desc" style="color:#64748b">Rückst. 13. Monatslohn (CHF)</td>
                        <td class="ls-num" style="color:#64748b;white-space:nowrap">${fmt(prevDisp)}</td>
                        <td class="ls-num">${pos(accrualDisp)}</td>
                        <td class="ls-num">${neg(payout)}</td>
                        <td class="ls-amt" style="color:#64748b;font-weight:600;white-space:nowrap">${fmt(0)}</td>
                    </tr>`);
                } else {
                    // Reguläre Akkumulation: Vormonat + Zuwachs = Saldo neu
                    const monthly     = s.thirteenthMonthly ?? 0;
                    const accumulated = s.thirteenthAccumulated ?? 0;
                    const prev        = Math.round((accumulated - monthly) * 100) / 100;
                    rows.push(`<tr>
                        <td class="ls-desc" style="color:#64748b">Rückst. 13. Monatslohn (CHF)</td>
                        <td class="ls-num" style="color:#64748b;white-space:nowrap">${fmt(prev < 0 ? 0 : prev)}</td>
                        <td class="ls-num">${pos(monthly)}</td>
                        <td class="ls-num"><span style="color:#cbd5e1">—</span></td>
                        <td class="ls-amt" style="color:#64748b;font-weight:600;white-space:nowrap">${fmt(accumulated)}</td>
                    </tr>`);
                }
            }

            return `
            <table class="ls-table" style="margin-top:3px">
                <thead>
                    <tr class="ls-col-hd">
                        <th style="text-align:left">Saldi</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Vormonat</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Aktuell</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Bezogen</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Saldo</th>
                    </tr>
                </thead>
                <tbody>${rows.join('')}</tbody>
            </table>`;
        })()}

        <!-- Auszahlungs-Sektion: Empfänger des Nettolohns (Bankverbindung des MA
             + ggf. Lohnabtretungs-Empfänger). Zeigt für die Kontrolle direkt im
             Lohnzettel, wohin der Auszahlungsbetrag fliesst — gleiche Daten wie
             im PDF, aber kompakt. -->
        ${(() => {
            const emp = Array.isArray(s.auszahlungEmpfaenger) ? s.auszahlungEmpfaenger : [];
            if (emp.length === 0) return '';
            const fmtIban = (iban) => {
                if (!iban) return '';
                const clean = String(iban).replace(/\s+/g, '');
                return clean.replace(/(.{4})/g, '$1 ').trim();
            };
            const empId = s.employeeId;   // für Direkt-Sprung zum MA
            const rows = emp.map(e => {
                const isBeh    = e.typ === 'BEHOERDE';
                const warning  = !!e.warning;
                const labelCol = warning ? '#b45309' : (isBeh ? '#6d28d9' : '#0f172a');
                const ibanTxt  = e.iban ? fmtIban(e.iban) : (warning ? 'Keine IBAN hinterlegt' : '');
                // Backend liefert label = "Empfänger (BankName)" — wir splitten
                // wieder auseinander, damit wir die Felder einzeln in der Reihen-
                // folge Empfänger → IBAN → BankName (truncated) darstellen können.
                const labelStr = e.label || '';
                const bankParen = e.bankName ? ` (${e.bankName})` : '';
                const namePart = (bankParen && labelStr.endsWith(bankParen))
                    ? labelStr.slice(0, -bankParen.length).trim()
                    : labelStr;
                const subTxt = e.referenz ? ('Ref. ' + e.referenz) : '';
                const typeBadge = isBeh
                    ? `<span style="display:inline-block;background:#ede9fe;color:#6d28d9;padding:1px 7px;border-radius:8px;font-size:10px;font-weight:600;flex-shrink:0">Lohnabtretung</span>`
                    : '';
                const ibanInline = ibanTxt
                    ? `<span style="font-family:ui-monospace,Menlo,Consolas,monospace;font-size:12px;color:#475569;flex-shrink:0">${ibanTxt}</span>`
                    : '';
                // Bankname truncated wenn lang — nimmt restliche Spaltenbreite ein,
                // läuft NICHT in die Betragsspalte hinein.
                const bankSpan = e.bankName
                    ? `<span title="${(e.bankName||'').replace(/"/g,'&quot;')}"
                              style="color:#94a3b8;font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;min-width:0;flex:1 1 auto">${e.bankName}</span>`
                    : '';
                // Bei Warnung "keine Bankverbindung" Action-Button: direkt zum
                // MA springen und Bank-Modal öffnen — spart Walter den Umweg
                // über Sidebar → MA suchen → Bank-Tab.
                const fixBankBtn = (warning && empId)
                    ? `<button onclick="jumpToMaForBankEntry(${empId})"
                                style="background:#fef3c7;color:#92400e;border:1px solid #fcd34d;border-radius:6px;padding:2px 10px;font-size:11px;font-weight:600;cursor:pointer;flex-shrink:0">→ Bankverbindung erfassen</button>`
                    : '';
                return `
                <tr${warning ? ' style="background:#fffbeb"' : ''}>
                    <td class="ls-desc" style="color:${labelCol}">
                        <div style="display:flex;align-items:center;gap:10px;min-width:0">
                            ${typeBadge}
                            <span style="flex-shrink:0">${namePart}</span>
                            ${ibanInline}
                            ${bankSpan}
                            ${fixBankBtn}
                        </div>
                        ${subTxt ? `<div style="font-size:11px;color:#64748b;margin-top:1px">${subTxt}</div>` : ''}
                    </td>
                    <td class="ls-amt" style="font-weight:600">${fmt(e.betrag)}</td>
                </tr>`;
            }).join('');

            return `
            <table class="ls-table" style="margin-top:3px">
                <thead>
                    <tr class="ls-col-hd">
                        <th style="text-align:left">Auszahlung an</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>`;
        })()}
    </div>`;
}

async function saveLohnSaldo() {
    // Legacy-Funktion: ruft jetzt confirmLohn auf
    confirmLohn();
}

async function confirmLohn() {
    if (!lohnCurrentSlip) return;
    const s = lohnCurrentSlip;
    const isCorr = !!(s.isCorrection || _lohnIsCorrection(s.employeeId));

    // Lohnproblem-Sperre (Walter 20./21./26.05.2026): unter L-GAV ODER ohne
    // Lohnsumme ODER QST-Pflicht offen → Bestätigen blockiert. Server blockt
    // zusätzlich mit 409; dies ist nur die freundliche UX davor.
    // Korrekturlohn: keine Mindestlohn-/QST-Sperre.
    const _lohnProb = !isCorr ? _lohnMwUnderpaid[s.employeeId] : null;
    if (_lohnProb) {
        let head = 'Bestätigen gesperrt — Mindestlohn unterschritten.';
        let hint = 'Bitte zuerst den Lohn im Vertrag erfassen/korrigieren.';
        if (_lohnProb.problem === 'NO_SALARY') {
            head = 'Bestätigen gesperrt — Lohnsumme fehlt.';
        } else if (_lohnProb.problem === 'QST_OFFEN') {
            head = 'Bestätigen gesperrt — QST-Pflicht offen.';
            hint = 'Bitte im MA-Tab → Quellensteuer den höchsten Tarif erfassen oder die Behörden-Befreiung hinterlegen.';
        }
        alert(head + '\n\n' + (_lohnProb.message || 'Lohnproblem.') + '\n\n' + hint);
        return;
    }

    // Periode holen oder erstellen
    const cid = s.companyId, year = s.year, month = s.month;
    let periode = await ensurePeriode(cid, year, month);
    if (!periode) return;

    if (periode.status === 'abgeschlossen') {
        alert('Diese Periode ist bereits abgeschlossen. Korrekturen gehen in die nächste Periode.');
        return;
    }

    const btn = document.getElementById('btnLohnBestaetigen');
    if (btn) { btn.disabled = true; btn.textContent = 'Speichere…'; }

    try {
        const res = await fetch('/api/payroll/confirm', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                employeeId:                  s.employeeId,
                companyProfileId:            cid,
                payrollPeriodeId:            periode.id,
                year,
                month,
                hourSaldo:                   s.neuerHourSaldo     ?? 0,
                nachtSaldo:                  s.neuerNachtSaldo    ?? 0,
                nightHoursWorked:            s.nightHours         ?? 0,
                ferienGeldSaldo:             s.ferienGeldSaldoNeu ?? 0,
                ferienTageSaldo:             s.ferienTageSaldoNeu ?? 0,
                feiertagTageSaldo:           s.feiertagTageSaldoNeu ?? 0,
                thirteenthMonthMonthly:      s.thirteenthMonthly  ?? 0,
                thirteenthMonthAccumulated:  s.thirteenthAccumulated ?? 0,
                grossAmount:                 s.totalLohn,
                netAmount:                   s.nettolohn,
                svBasisAhv:                  s.svBasisAhv         ?? 0,
                svBasisBvg:                  s.svBasisBvg         ?? 0,
                qstBetrag:                   s.qstBetrag          ?? 0,
                slipJson:                    JSON.stringify(s),
                // Server rechnet jetzt selbst nach (Sicherheit) — die Beträge
                // oben dienen nur noch als Referenz. Die EINZIGE Entscheidung,
                // die der Server braucht: ob die Ferien-Kürzung angewendet wurde.
                applyFerienKuerzung:         !!s.ferienKuerzungAngewendet,
                isCorrection:                isCorr,
                lohnAbtretungen:             (s.lohnAbtretungen ?? []).map(l => ({ assignmentId: l.assignmentId, betrag: l.betrag }))
            })
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({ message: res.statusText }));
            throw new Error(err.message || err.error || 'Fehler beim Bestätigen');
        }
        const result = await res.json();
        // Walter-Vorgabe 20.05.2026: flüssig wie Akonto — KEIN voller
        // loadLohnList()-Reload (der lädt /api/employees neu + scrollt doppelt).
        // Stattdessen nur die eine Zeile im DOM auf „GF bestätigt" setzen,
        // Banner-Counter aktualisieren, dann zum nächsten MA springen.
        // Single-Refresh (analog Akonto): _lohnWfData + Liste + Statusbar neu.
        await lohnWfRefresh();
        showToast(isCorr ? 'Korrekturlohn bestätigt ✓' : 'Lohn bestätigt ✓', 'success');
        // Zum nächsten unbestätigten MA springen. Wenn keiner mehr offen ist,
        // bleibt der aktuelle MA selektiert — die Statusbar zeigt jetzt „↶ Wieder
        // eröffnen" (alles aus _lohnWfData, kein stale Button mehr möglich).
        if (!_lohnJumpToNextUnconfirmed(s.employeeId, 'gf')) {
            await loadLohnSlip(s.employeeId, cid, year, month);
        }
    } catch(e) {
        alert(e.message);
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = '✓ Lohn bestätigen'; }
    }
}

/// Setzt eine MA-Zeile in der Liste ohne vollen Reload auf einen Bestätigt-
/// Status (Walter-Vorgabe 20.05.2026 — flüssige Akonto-Mechanik). Aktualisiert
/// nur Avatar-Icon/-Farbe + Untertext. Der Banner-Counter zählt unabhängig
/// davon die echten Snapshot-Status (siehe loadLohnPeriodBanner).
///   mode='gf' → grünes ✓  „GF bestätigt"
///   mode='hr' → blaues ✓✓ „HR-bestätigt"
function _lohnMarkRowConfirmed(empId, mode) {
    const row = document.querySelector(`#lohnEmpList .lohn-emp-row[data-emp-id="${empId}"]`);
    if (!row) return;
    const avatar = row.firstElementChild;
    const sub    = row.querySelector('.lohn-emp-nr');
    if (mode === 'hr') {
        if (avatar) { avatar.style.background = '#ece9e2'; avatar.style.color = '#6b6152'; avatar.textContent = '✓✓'; }
        if (sub)    { sub.textContent = 'HR-bestätigt'; sub.style.color = '#6b6152'; }
    } else {
        if (avatar) { avatar.style.background = '#dcfce7'; avatar.style.color = '#166534'; avatar.textContent = '✓'; }
        if (sub)    { sub.textContent = 'GF bestätigt'; sub.style.color = '#16a34a'; }
    }
}

/// Auto-Sprung zum nächsten zu bearbeitenden MA nach einer Bestätigung
/// (analog _akWfJumpToNextOpen). Sucht im aktuellen Render der MA-Liste den
/// nächsten Eintrag der noch eine Aktion braucht und lädt direkt dessen Slip.
/// Walter-Vorgabe 19.05.2026: KEIN .click() auf die Zeile (das würde
/// loadLohnSlipFromPanel → loadLohnList re-triggern → Flackern), stattdessen
/// nur Highlight + direkter loadLohnSlip-Aufruf.
///
/// mode='gf' (Default): nächster MA der NOCH GAR NICHT bestätigt ist.
/// mode='hr':           nächster MA der GF-bestätigt, aber noch nicht HR-bestätigt ist.
/// Beide Workflows (GF-Bestätigen + HR-Bestätigen) nutzen damit dieselbe
/// flacker-freie Sprung-Mechanik — analog zum Akonto-Tab.
/// Gibt true zurück wenn zu einem nächsten MA gesprungen wurde, sonst false
/// (= alle erledigt, kein offener MA mehr). Der Aufrufer lädt dann den Slip
/// des aktuellen MA neu, damit dessen Buttons nicht stale bleiben.
function _lohnJumpToNextUnconfirmed(currentEmpId, mode = 'gf') {
    const rows = Array.from(document.querySelectorAll('#lohnEmpList .lohn-emp-row'));
    if (!rows.length) return false;
    const statusText = row => (row.querySelector('.lohn-emp-nr')?.textContent || '').trim();
    const needsAction = mode === 'hr'
        // HR sucht MA die GF-bestätigt sind (= grünes ✓), aber noch nicht HR-bestätigt (✓✓).
        ? (row => statusText(row).startsWith('GF bestätigt'))
        // GF sucht MA ohne jegliches Häkchen.
        : (row => {
            const t = statusText(row);
            return !(t.startsWith('GF bestätigt') || t.startsWith('HR-bestätigt') || t.startsWith('Lohn bestätigt'));
          });
    const idx = rows.findIndex(r => Number(r.dataset.empId) === Number(currentEmpId));
    const order = [];
    for (let i = idx + 1; i < rows.length; i++) order.push(rows[i]);
    for (let i = 0; i <= idx; i++)              order.push(rows[i]);
    const nextRow = order.find(needsAction);
    if (!nextRow) return false;

    const nextEmpId = Number(nextRow.dataset.empId);
    if (!nextEmpId) return false;

    // Walter-Bug 20.05.2026: Den KANONISCHEN Auswahl-Pfad benutzen — der
    // Row-onclick (in loadLohnList) setzt in EINEM Schritt Selection +
    // Highlight + Vertrag-Card (showLohnVertragInfo mit dem echten MA-Objekt
    // aus der Closure) + Statusbar (_lohnWfRenderStatusBar) + Zulagen +
    // Lohnzettel — alles konsistent, kein voller Listen-Rebuild, kein Flackern.
    // Früher wurde das hier manuell nachgebaut, ABER ohne Statusbar-Re-Render
    // (und mit dem nicht existierenden `_empAllRaw`): die per-MA-Buttons blieben
    // auf dem vorherigen MA stehen (nach „HR-bestätigen" war nur noch
    // „HR-Bestätigung zurückziehen" sichtbar) und die Vertrag-Card hing fest.
    nextRow.click();

    setTimeout(() => {
        nextRow.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }, 50);
    return true;
}

async function ensurePeriode(companyId, year, month) {
    // Prüft ob Periode existiert, sonst neu anlegen
    const res = await fetch(`/api/payroll-perioden/current?companyProfileId=${companyId}&year=${year}&month=${month}`, { headers: ah() });
    if (!res.ok) { alert('Fehler beim Laden der Periode'); return null; }
    // Defensiv: leerer Body oder "null" → Periode existiert nicht
    const raw = await res.text();
    let periode = null;
    if (raw && raw.trim() && raw.trim() !== 'null') {
        try { periode = JSON.parse(raw); } catch { periode = null; }
    }
    if (periode) return periode;

    // Periode noch nicht vorhanden → anlegen
    if (!confirm(`Noch keine Lohnperiode für ${month}/${year} vorhanden. Jetzt erstellen?`)) return null;
    const cr = await fetch('/api/payroll-perioden', {
        method: 'POST',
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ companyProfileId: companyId, year, month, label: null })
    });
    if (!cr.ok) { const e = await cr.json().catch(()=>({})); alert(e.message || 'Periode konnte nicht erstellt werden'); return null; }
    periode = await cr.json();
    await loadLohnPeriodBanner(companyId, year, month);
    return periode;
}

// Shim (Walter-Vorgabe 20.05.2026): loadLohnPeriodBanner ist nur noch ein Alias
// auf den Single-Refresh des Definitivlaufs (lohnWfRefresh → loadLohnList →
// _lohnWfRenderStatusBar). Die frühere Banner-/Button-Logik ist komplett in
// _lohnWfRenderStatusBar gewandert — EINZIGE Stelle für Status + Buttons.
// Alle Alt-Aufrufer (Aktions-Handler) funktionieren unverändert weiter.
async function loadLohnPeriodBanner(companyId, year, month) {
    return lohnWfRefresh();
/* ===== TOTER ALT-CODE (bleibt zur Historie, wird nie erreicht) ============
    const banner = document.getElementById('lohnPeriodBanner');
    if (!banner) return;
    if (!companyId) { banner.style.display = 'none'; return; }

    try {
        const res = await fetch(`/api/payroll-perioden/current?companyProfileId=${companyId}&year=${year}&month=${month}`, { headers: ah() });
        const p   = res.ok ? await res.json() : null;

        if (!p) {
            banner.style.display = 'block';
            banner.innerHTML = `
                <div style="background:#fff8e1;border:1px solid #fbbf24;border-radius:8px;padding:10px 16px;display:flex;align-items:center;gap:12px;font-size:13px">
                    <svg width="16" height="16" fill="none" viewBox="0 0 24 24" stroke="#d97706" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                    <span style="color:#92400e">Noch keine Periode für ${month}/${year} angelegt — wird beim ersten Bestätigen automatisch erstellt.</span>
                </div>`;
            return;
        }

        // Status-Pill: 3 Stufen — offen (blau), provisorisch (orange), abgeschlossen (grün)
        const isAbgeschlossen   = p.status === 'abgeschlossen';
        const isProvisorisch    = p.status === 'provisorisch_abgeschlossen';
        const isOffen           = !isAbgeschlossen && !isProvisorisch;
        const statusLabel = isAbgeschlossen ? 'Abgeschlossen' : isProvisorisch ? 'Provisorisch abgeschlossen' : 'Offen';
        const pillBg    = isAbgeschlossen ? '#dcfce7' : isProvisorisch ? '#fef3c7' : '#efece5';
        const pillColor = isAbgeschlossen ? '#166534' : isProvisorisch ? '#92400e' : '#6b6152';

        // Bestätigt-Zähler (Walter-Bugfix 20.05.2026): GF-bestätigte MA werden
        // aus den ECHTEN Snapshot-Status gezählt (zuverlässig), NICHT aus dem
        // client-seitigen _lohnStats.confirmedCount — der geriet nach dem
        // „Zurück an GF"-Hin-und-Her aus dem Tritt, sodass „An HR senden" nicht
        // mehr erschien. activeTotal (Anzahl aktiver MA der Filiale) kommt
        // weiterhin aus loadLohnList, da es sich beim Bestätigen nicht ändert.
        let gfConfirmed = 0, hrConfirmed = 0;
        try {
            const snRes = await fetch(`/api/payroll-perioden/${p.id}/snapshots`, { headers: ah() });
            if (snRes.ok) {
                const snaps = await snRes.json();
                snaps.forEach(sn => {
                    const st = sn.status || 'BERECHNET';
                    if (st === 'FREIGEGEBEN_GF' || st === 'HR_BESTAETIGT' || st === 'ABGESCHLOSSEN') gfConfirmed++;
                    if (st === 'HR_BESTAETIGT' || st === 'ABGESCHLOSSEN') hrConfirmed++;
                });
            }
        } catch {}
        const activeTotal = (window._lohnStats && window._lohnStats.activeTotal) || 0;
        const stats       = { confirmedCount: gfConfirmed, activeTotal };
        // GF-Phase (offen): Counter zeigt GF-Fortschritt. HR-Phase
        // (provisorisch_abgeschlossen): Counter zeigt HR-Fortschritt — analog
        // Akonto-Tab. So sieht HR sofort wie viele MA noch HR-bestätigt werden müssen.
        const confirmText = isProvisorisch
            ? `${hrConfirmed}/${activeTotal} HR-bestätigt`
            : `${gfConfirmed}/${activeTotal} bestätigt`;
        const allBestaetigt   = activeTotal > 0 && gfConfirmed >= activeTotal;   // GF: alle freigegeben → „An HR senden"
        const allHrBestaetigt = activeTotal > 0 && hrConfirmed >= activeTotal;   // HR: alle bestätigt → „Lohnbelege + DTA"

        // Abschluss-Button (Walter-Vorgabe 19.05.2026: Sprache + Workflow
        // analog Akonto-Tab):
        //   • offen + alle bestätigt        → „An HR senden →"  (GF-Aktion)
        //   • offen + nicht alle bestätigt  → Hinweis „erst alle bestätigen"
        //   • provisorisch_abgeschlossen    → 🔒 Bei HR (lokale Statusanzeige).
        //     HR-Aktionen (Zurück an GF / Definitiv abschliessen) sind in der
        //     oberen Toolbar — siehe lohnTopActions.
        // Walter-Vorgabe 20.05.2026: Der „An HR senden"-Button ist KEIN Banner-
        // Element mehr — er sitzt rechts bei den GF-Aktionsbuttons (lohnTopActions,
        // #btnLohnAnHrSenden, weiter unten getoggelt). Im Banner bleibt nur die
        // Info-Anzeige (Hinweis „erst alle bestätigen" bzw. „🔒 Bei HR").
        const abschlussBtn = isOffen
            ? (allBestaetigt
                ? ''
                : `<span style="font-size:11px;color:#94a3b8">Erst alle Lohnzettel bestätigen (${stats.activeTotal - stats.confirmedCount} ausstehend)</span>`)
            : isProvisorisch
                ? (isHr
                    // HR-Phase: Hinweis wie viele MA noch HR-bestätigt werden müssen.
                    // Erst wenn alle durch sind, erscheint „Lohnbelege + DTA" (rechts).
                    ? (allHrBestaetigt
                        ? `<span style="font-size:11.5px;font-weight:600;color:#166534;background:#dcfce7;padding:3px 9px;border-radius:8px">✓ Alle HR-bestätigt — bereit für DTA</span>`
                        : `<span style="font-size:11px;color:#94a3b8">Erst alle Lohnzettel HR-bestätigen (${activeTotal - hrConfirmed} ausstehend)</span>`)
                    : `<span style="font-size:11.5px;font-weight:600;color:#92400e;background:#fef3c7;padding:3px 9px;border-radius:8px">🔒 Bei HR — keine Änderungen möglich</span>`)
                : '';

        // Aktuellen Periode-Kontext im window-Objekt cachen, damit der
        // Bemerkungs-Button keine Argumente benötigt (vermeidet Escaping-
        // Probleme bei mehrzeiligen Texten mit Anführungszeichen).
        window._currentLohnPeriode = p;

        // Sammel-Aktions-Buttons im Top-Bar einblenden (Walter-Vorgabe
        // 18.05.2026 — gehören in den Lohnlauf, nicht ins admin-only
        // Lohnperioden-Modul). Sichtbar je nach Status:
        //   • Vorab-PDF (alle Lohnbelege) → ab provisorisch_abgeschlossen
        //   • DTA-File (pain.001)        → erst nach definitiv abgeschlossen
        const btnVorabPdf = document.getElementById('btnLohnVorabPdf');
        const btnDtaMa    = document.getElementById('btnLohnDtaMa');
        if (btnVorabPdf) btnVorabPdf.style.display = (isProvisorisch || isAbgeschlossen) ? '' : 'none';
        if (btnDtaMa)    btnDtaMa.style.display    = isAbgeschlossen ? '' : 'none';

        // HR-Aktionen (Walter 19.05.2026, analog Akonto-Tab) — sichtbar wenn
        // Periode provisorisch_abgeschlossen UND User ist admin/superuser.
        const isHr = (typeof currentUser !== 'undefined' && currentUser?.role)
                        && (currentUser.role === 'admin' || currentUser.role === 'superuser');
        // GF-Aktion „An HR senden" (rechts bei den Aktionsbuttons): sichtbar
        // nur wenn Periode offen UND alle MA bestätigt sind.
        const btnAnHr = document.getElementById('btnLohnAnHrSenden');
        if (btnAnHr) btnAnHr.style.display = (isOffen && allBestaetigt) ? '' : 'none';

        const btnZurueck    = document.getElementById('btnLohnZurueckAnGf');
        const btnDefinitiv  = document.getElementById('btnLohnDefinitivAbschliessen');
        const btnLbView     = document.getElementById('btnLohnLohnbelegeView');
        if (btnZurueck)   btnZurueck.style.display   = (isProvisorisch && isHr) ? '' : 'none';
        // „Lohnbelege + DTA" (= Versand) erst wenn HR ALLE MA bestätigt hat —
        // analog Akonto, wo DTA erst im Status HR_FREIGEGEBEN erscheint.
        if (btnDefinitiv) btnDefinitiv.style.display = (isProvisorisch && isHr && allHrBestaetigt) ? '' : 'none';
        // Nach DTA-Klick: Periode ist 'abgeschlossen'. Belege bleiben für HR
        // einsehbar (Druck/Re-Download), Versand-Button im Modal ist dann
        // deaktiviert — der Re-Versand-Pfad geht über Re-Open.
        if (btnLbView)    btnLbView.style.display    = (isAbgeschlossen && isHr) ? '' : 'none';

        // GF-Lock auf per-MA-Aktionen: in provisorisch_abgeschlossen sieht GF
        // weder „Lohn bestätigen" noch „Wieder eröffnen". Admin/superuser
        // sehen das aber auch nicht — die nutzen die HR-Aktionen oben.
        if (isProvisorisch || isAbgeschlossen) {
            const bt = document.getElementById('btnLohnBestaetigen');
            const rp = document.getElementById('btnLohnReopen');
            if (bt) bt.style.display = 'none';
            if (rp) rp.style.display = 'none';
        }

        // Zulagen-Lock erneut anwenden — bei Status-Wechsel der Periode muss
        // sich die Card-Sichtbarkeit aktualisieren (Walter-Bug 19.05.2026:
        // nach „An HR senden" konnte GF noch Zulagen erfassen).
        if (typeof _akWfApplyZulagenLock === 'function') _akWfApplyZulagenLock();
        const footerHasText = !!(p.pdfFooterText && p.pdfFooterText.trim());
        const footerLabel   = footerHasText ? '✏️ Bemerkung bearbeiten' : '＋ Bemerkung erfassen';
        const footerColor   = footerHasText ? '#15803d' : '#64748b';

        banner.style.display = 'block';
        banner.innerHTML = `
            <div title="Lohnperiode ${p.periodFrom} – ${p.periodTo}"
                 style="display:flex;align-items:center;gap:10px;font-size:12px;color:#64748b;padding:2px 0">
                <span style="background:${pillBg};color:${pillColor};padding:2px 8px;border-radius:10px;font-size:11px;font-weight:600">
                    ${statusLabel}
                </span>
                <span>${confirmText}</span>
                ${abschlussBtn}
                <button class="btn btn-sm btn-outline"
                        style="color:${footerColor};border-color:#e2e8f0;font-size:11px;padding:3px 10px"
                        onclick="openPeriodeBemerkungModal()">
                    ${footerLabel}
                </button>
            </div>`;
    } catch(e) {
        banner.style.display = 'none';
    }
===== ENDE TOTER ALT-CODE ===== */
}

// ── Sammel-Downloads (Vorab-PDF / Definitiv-DTA) ──────────────────────────
// Walter-Vorgabe 18.05.2026: Lohnbeleg-Sammel-PDF + Definitiv-DTA werden
// direkt aus dem GF/HR-Lohnlauf (page-lohn) abgerufen — kein Detour über
// das admin-only Lohnperioden-Modul. Endpoints existieren schon
// (/api/lohnlauf/{periodeId}/vorab-pdf bzw. /dta-ma).

// saveBlobAsk() + saveUrlAsk() leben jetzt zentral in js/save-blob.js
// (Walter-Vorgabe 21.05.2026 — EINE kanonische Download-Stelle für das ganze
// Programm). Hier nur noch genutzt, nicht mehr definiert.

// preview=true → anzeigbare Dateien (PDF) zuerst im Vorschaufenster zeigen;
// preview=false (Default) → direkt „Speichern unter…" (echte Downloads wie DTA).
async function _lohnDownloadBlob(url, filenameHint, preview = false) {
    try {
        const res = await fetch(url, { headers: ah() });
        if (!res.ok) {
            const txt = await res.text().catch(()=>'');
            alert(`Download fehlgeschlagen (HTTP ${res.status}): ${txt || 'Unbekannter Fehler'}`);
            return;
        }
        const blob = await res.blob();
        const disp = res.headers.get('content-disposition') || '';
        const m    = disp.match(/filename\*?=["']?(?:UTF-8''|)([^;"']+)/i);
        const filename = (m && decodeURIComponent(m[1])) || filenameHint;
        if (preview) await previewFileModal(blob, filename);
        else         await saveBlobAsk(blob, filename);
    } catch (e) {
        alert(`Download-Fehler: ${e.message}`);
    }
}

async function lohnDownloadVorabPdf() {
    const p = window._currentLohnPeriode;
    if (!p?.id) { alert('Keine Periode aktiv.'); return; }
    await _lohnDownloadBlob(
        `/api/lohnlauf/${p.id}/vorab-pdf`,
        `Lohnbelege_${p.label || (p.year + '-' + String(p.month).padStart(2,'0'))}.pdf`,
        true);  // PDF → Vorschaufenster
}

async function lohnDownloadDtaMa() {
    const p = window._currentLohnPeriode;
    if (!p?.id) { alert('Keine Periode aktiv.'); return; }
    await _lohnDownloadBlob(
        `/api/lohnlauf/${p.id}/dta-ma`,
        `Lohn_DTA_${p.label || (p.year + '-' + String(p.month).padStart(2,'0'))}.xml`);
}

// Saldo-Listen zum Definitiv-Abschluss (Walter 21.05.2026): zwei PDFs der
// aktuellen Filiale+Periode. variant 'buchhaltung' = alle Saldi + Brutto/Netto
// + IBAN; 'gf' = kompakte Übersicht (UTP ohne 13.). Download via Speichern-unter.
async function lohnSaldoListe(variant) {
    const p = window._currentLohnPeriode || {};
    const cid = p.companyProfileId || document.getElementById('lohnBranchSelect')?.value;
    const y   = p.year  || parseInt(document.getElementById('lohnYearSelect')?.value  || new Date().getFullYear());
    const m   = p.month || parseInt(document.getElementById('lohnMonthSelect')?.value || (new Date().getMonth() + 1));
    if (!cid) { alert('Keine Filiale/Periode aktiv.'); return; }
    const ep    = variant === 'gf' ? 'saldo-liste-gf' : 'saldo-liste-buchhaltung';
    const label = variant === 'gf' ? 'GF-Saldi-Uebersicht' : 'Lohn-Saldi-Buchhaltung';
    const mm    = String(m).padStart(2, '0');
    try {
        const r = await fetch(
            `/api/payroll/${ep}?companyProfileId=${cid}&year=${y}&month=${m}`,
            { headers: ah() });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            alert('PDF konnte nicht erstellt werden: ' + (j.error || `HTTP ${r.status}`));
            return;
        }
        const blob = await r.blob();
        await previewFileModal(blob, `${label}_${cid}_${y}-${mm}.pdf`);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Fibu-Journal (Buchungssätze) der aktuellen Filiale+Periode — Walter 22.05.2026.
async function lohnFibuJournal() {
    const p = window._currentLohnPeriode || {};
    const cid = p.companyProfileId || document.getElementById('lohnBranchSelect')?.value;
    const y   = p.year  || parseInt(document.getElementById('lohnYearSelect')?.value  || new Date().getFullYear());
    const m   = p.month || parseInt(document.getElementById('lohnMonthSelect')?.value || (new Date().getMonth() + 1));
    if (!cid) { alert('Keine Filiale/Periode aktiv.'); return; }
    const mm = String(m).padStart(2, '0');
    try {
        const r = await fetch(`/api/payroll/fibu-journal?companyProfileId=${cid}&year=${y}&month=${m}`, { headers: ah() });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            alert('Fibu-Journal konnte nicht erstellt werden: ' + (j.error || `HTTP ${r.status}`));
            return;
        }
        const blob = await r.blob();
        await previewFileModal(blob, `Fibu-Journal_${cid}_${y}-${mm}.pdf`);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Admin-Wartung (Walter 22.05.2026): trägt die Fibu-Codes (categoryCode/code)
// in bestehende Snapshot-SlipJsons der aktuellen Filiale+Periode nach — für
// Alt-Perioden, die VOR dem Engine-Code-Tagging bestätigt wurden. Ändert NUR
// abzugLines/lohnLines; Status/Beträge/Workflow bleiben unangetastet, kein
// Neu-Durchschleusen der MA nötig. Danach kann das Fibu-Journal alle
// Abzugszeilen verbuchen (Konto 1920 → 0).
async function lohnRefreshCodes() {
    const p = window._currentLohnPeriode || {};
    const cid = p.companyProfileId || document.getElementById('lohnBranchSelect')?.value;
    const y   = p.year  || parseInt(document.getElementById('lohnYearSelect')?.value  || new Date().getFullYear());
    const m   = p.month || parseInt(document.getElementById('lohnMonthSelect')?.value || (new Date().getMonth() + 1));
    if (!cid) { alert('Keine Filiale/Periode aktiv.'); return; }
    const mm = String(m).padStart(2, '0');
    if (!confirm(
        `Fibu-Codes für Periode ${mm}.${y} nachtragen?\n\n` +
        'Die Lohnzettel werden neu berechnet und nur die Buchungs-Codes\n' +
        '(für das Fibu-Journal) im Hintergrund nachgetragen.\n\n' +
        'Beträge, Status und Workflow bleiben unverändert.')) return;
    try {
        const r = await fetch(
            `/api/payroll/refresh-snapshot-codes?companyProfileId=${cid}&year=${y}&month=${m}`,
            { method: 'POST', headers: ah() });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            alert('Codes konnten nicht nachgetragen werden: ' + (j.error || `HTTP ${r.status}`));
            return;
        }
        const j = await r.json().catch(() => ({}));
        alert(`✓ Codes nachgetragen: ${j.updated ?? '?'} von ${j.total ?? '?'} Lohnzetteln aktualisiert.\n\n` +
              'Du kannst das Fibu-Journal jetzt neu erstellen — Konto 1920 sollte aufgehen.');
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Admin-Reparatur (Walter 22.05.2026): rechnet die Lohnzettel der Periode NEU und
// überschreibt Brutto + Netto + SlipJson GEMEINSAM aus einer frischen Rechnung —
// behebt inkonsistente Snapshots (z.B. wenn Slip-Abzüge und eingefrorenes Netto
// auseinanderlaufen → Konto 1920 geht nicht auf). Workflow-Status bleibt; KEINE
// 46-MA-Neubestätigung nötig. ACHTUNG: überschreibt die eingefrorenen Beträge.
async function lohnRecomputeSnapshots() {
    const p = window._currentLohnPeriode || {};
    const cid = p.companyProfileId || document.getElementById('lohnBranchSelect')?.value;
    const y   = p.year  || parseInt(document.getElementById('lohnYearSelect')?.value  || new Date().getFullYear());
    const m   = p.month || parseInt(document.getElementById('lohnMonthSelect')?.value || (new Date().getMonth() + 1));
    if (!cid) { alert('Keine Filiale/Periode aktiv.'); return; }
    const mm = String(m).padStart(2, '0');
    if (!confirm(
        `Lohnzettel der Periode ${mm}.${y} NEU berechnen?\n\n` +
        'Brutto, Netto und Lohnzettel werden aus der aktuellen Berechnung\n' +
        'überschrieben (Status/Workflow bleiben). Reparatur bei inkonsistenten\n' +
        'Snapshots.\n\n' +
        '⚠ NUR ausführen, wenn die Periode noch NICHT bei der Bank ausbezahlt ist —\n' +
        'die eingefrorenen Beträge werden überschrieben.')) return;
    try {
        const r = await fetch(
            `/api/payroll/recompute-snapshots?companyProfileId=${cid}&year=${y}&month=${m}`,
            { method: 'POST', headers: ah() });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            alert('Neuberechnung fehlgeschlagen: ' + (j.error || `HTTP ${r.status}`));
            return;
        }
        const j = await r.json().catch(() => ({}));
        alert(`✓ ${j.updated ?? '?'} von ${j.total ?? '?'} Lohnzetteln neu berechnet.\n\n` +
              'Erstelle das Fibu-Journal neu — Konto 1920 sollte jetzt aufgehen.');
        if (typeof lohnWfRefresh === 'function') lohnWfRefresh();
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// ── HR-Aktionen Definitivlauf (Walter 19.05.2026, analog Akonto) ──
// Beide Endpoints sind admin/superuser only (Backend-Guard auf
// PayrollPeriodeController). Frontend zeigt die Buttons nur diese Rolle.

async function lohnZurueckAnGf() {
    const p = window._currentLohnPeriode;
    if (!p?.id) { alert('Keine Periode aktiv.'); return; }
    const grund = prompt('Begründung für GF (warum zurück?):');
    if (grund === null || grund.trim() === '') return;

    // Walter-Vorgabe 19.05.2026: Status entscheidet welcher Endpoint:
    //   provisorisch_abgeschlossen → /zurueck-an-gf (HR holt zurück)
    //   abgeschlossen              → /wieder-oeffnen (Admin-Reset)
    // Frühere Frontend-Logik rief immer /wieder-oeffnen — das ergab 409.
    const isAdminReset = (p.status === 'abgeschlossen');
    const endpoint     = isAdminReset ? 'wieder-oeffnen' : 'zurueck-an-gf';

    if (isAdminReset) {
        // Pflicht-Bestätigung: Admin muss bestätigen dass der DTA bei der
        // Bank gelöscht wurde — sonst kann die Auszahlung doppelt laufen.
        const dtaGeloescht = confirm(
            'ACHTUNG: Bereits abgeschlossene Periode wieder eröffnen.\n\n' +
            'Hast du den DTA bei der Bank gelöscht oder storniert?\n\n' +
            '✓ JA → Periode wird auf "provisorisch_abgeschlossen" zurückgerollt,\n' +
            '       Lohnzettel aus MA-Postfächern entfernt.\n' +
            '✗ NEIN → Vorgang abbrechen.\n\n' +
            'Diese Operation ist NACH dem Zahldatum DTA gesperrt — die Bank hätte\n' +
            'die Zahlungen bereits ausgeführt. Notfall-Eingriff nur über Entwickler.'
        );
        if (!dtaGeloescht) return;
    }

    try {
        const userId = (typeof currentUser !== 'undefined' && currentUser?.id) ? currentUser.id : 0;
        const res = await fetch(`/api/payroll-perioden/${p.id}/${endpoint}`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId, bemerkung: grund })
        });
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            // Backend-Sperre nach Zahldatum erkennbar machen
            if (e.error === 'PAYOUT_DATE_REACHED') {
                alert('⛔ ' + e.message);
                return;
            }
            throw new Error(e.message || e.error || 'Fehler beim Zurückgeben');
        }
        showToast(isAdminReset ? 'Periode wieder eröffnet ↺' : 'Periode an GF zurückgegeben ↩', 'success');
        await lohnWfRefresh();
        const s = lohnCurrentSlip;
        if (s) await loadLohnSlip(s.employeeId, s.companyId, s.year, s.month);
    } catch (e) { alert(e.message); }
}

// Legacy: direkter Klick auf "Definitiv abschliessen" — der neue Flow geht
// über lohnOpenLohnbelegeModal() → lohnLohnbelegeDispatch() im Modal.
// Funktion bleibt für Rückwärtskompatibilität (falls noch irgendwo verlinkt).
async function lohnDefinitivAbschliessen() {
    return lohnOpenLohnbelegeModal();
}

// HR per-MA-Aktionen im Definitivlauf (Walter 19.05.2026, analog Akonto-Tab).
// Backend-Endpoints existieren bereits unter /api/payroll/hr-bestaetigen/{snapshotId}
// und /api/payroll/hr-zurueckziehen/{snapshotId}.
async function lohnHrBestaetigen() {
    const s = lohnCurrentSlip;
    if (!s?.employeeId) return;
    const cid = s.companyId, year = s.year, month = s.month;
    // Snapshot-ID via aktueller Periode + employeeId nachschlagen
    const snapId = await _lohnFindCurrentSnapshotId(s);
    if (!snapId) { alert('Snapshot nicht gefunden.'); return; }
    try {
        const res = await fetch(`/api/payroll/hr-bestaetigen/${snapId}`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: '{}'
        });
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            throw new Error(e.message || e.error || 'Fehler beim HR-Bestätigen');
        }
        // Walter-Vorgabe 20.05.2026: flüssig wie Akonto — KEIN voller
        // loadLohnList()-Reload. Nur die Zeile auf „HR-bestätigt" (✓✓) setzen,
        // Banner/Top-Bar aktualisieren, dann zum nächsten GF-bestätigten MA
        // springen.
        // Single-Refresh (analog Akonto): _lohnWfData + Liste + Statusbar neu.
        await lohnWfRefresh();
        showToast('HR-bestätigt ✓✓', 'success');
        // Zum nächsten GF-bestätigten MA springen. Wenn keiner mehr offen ist
        // (alle HR-bestätigt), bleibt der MA selektiert — die Statusbar zeigt
        // dann „📑 Lohnbelege + DTA" (aus _lohnWfData, allHr=true).
        if (!_lohnJumpToNextUnconfirmed(s.employeeId, 'hr')) {
            await loadLohnSlip(s.employeeId, cid, year, month);
        }
    } catch (e) { alert(e.message); }
}

async function lohnHrZurueckziehen() {
    const s = lohnCurrentSlip;
    if (!s?.employeeId) return;
    if (!confirm('HR-Bestätigung für dieses Lohnblatt zurückziehen?')) return;
    const cid = s.companyId, year = s.year, month = s.month;
    const snapId = await _lohnFindCurrentSnapshotId(s);
    if (!snapId) { alert('Snapshot nicht gefunden.'); return; }
    try {
        const res = await fetch(`/api/payroll/hr-zurueckziehen/${snapId}`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: '{}'
        });
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            throw new Error(e.message || e.error || 'Fehler beim Zurückziehen');
        }
        showToast('HR-Bestätigung zurückgezogen', 'success');
        // Single-Refresh — MA bleibt selektiert, Statusbar zeigt wieder
        // „✓ HR-bestätigen" (selStatus == FREIGEGEBEN_GF aus _lohnWfData).
        await lohnWfRefresh();
        await loadLohnSlip(s.employeeId, cid, year, month);
    } catch (e) { alert(e.message); }
}

// Helper: Snapshot-ID des aktuellen MA in der aktuellen Periode finden.
async function _lohnFindCurrentSnapshotId(s) {
    try {
        const pRes = await fetch(
            `/api/payroll-perioden/current?companyProfileId=${s.companyId}&year=${s.year}&month=${s.month}`,
            { headers: ah() });
        const pData = pRes.ok ? await pRes.json() : null;
        if (!pData?.id) return null;
        const snRes = await fetch(`/api/payroll-perioden/${pData.id}/snapshots`, { headers: ah() });
        if (!snRes.ok) return null;
        const arr = await snRes.json();
        const mine = (arr || []).find(x => x.employeeId === s.employeeId);
        return mine?.id || null;
    } catch { return null; }
}

// ─── Lohnbelege-Vorschau-Modal (Walter-Vorgabe 19.05.2026) ──────────────
// Nach HR-Bestätigung im Definitivlauf zeigt das Modal alle Lohnbelege als
// zusammengeführtes PDF (Endpoint /api/lohnlauf/{id}/vorab-pdf). Im Footer
// kann HR die Belege drucken, das Vorab-PDF oder das DTA-File speichern und
// — der Hauptschritt — den finalen Versand auslösen: „💰 DTA erstellen + an
// MA versenden". Das ruft /api/payroll-perioden/{id}/definitiv-abschliessen,
// der die Lohnzettel pro MA ins Postfach ablegt + E-Mails versendet (im
// Test-Modus geht alles an die TestRedirectTo-Adresse aus den SMTP-Settings).
let _lohnLohnbelegePdfBlobUrl = null;
let _lohnLohnbelegePdfFilename = 'Lohnlauf_Vorab.pdf';
let _lohnLohnbelegePeriodeId = null;

async function lohnOpenLohnbelegeModal() {
    const p = window._currentLohnPeriode;
    if (!p?.id) { alert('Keine Periode aktiv.'); return; }
    _lohnLohnbelegePeriodeId = p.id;

    // Modal-Header bestücken
    const months = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
    const branchSel = document.getElementById('branchSelect');
    const branchName = branchSel?.options?.[branchSel.selectedIndex]?.text || `Filiale ${p.companyProfileId || ''}`;
    const monatLabel = p.month ? `${months[p.month-1]} ${p.year}` : (p.label || '');
    const title = document.getElementById('lohnLohnbelegePdfTitle');
    if (title) title.textContent = `${branchName} · ${monatLabel}`;

    // Modal sofort öffnen (User sieht Lade-Spinner im iframe), PDF im Hintergrund holen
    const modal = document.getElementById('lohnLohnbelegePdfModal');
    if (modal) modal.style.display = 'block';

    // Hinweis-Banner bei provisorisch_abgeschlossen (Versand noch ausstehend)
    const info = document.getElementById('lohnLohnbelegeDispatchInfo');
    if (info) info.style.display = (p.status === 'provisorisch_abgeschlossen') ? 'block' : 'none';

    // Versand-Button nur in provisorisch_abgeschlossen aktiv
    const btnDispatch = document.getElementById('btnLohnLohnbelegeDispatch');
    if (btnDispatch) {
        if (p.status === 'provisorisch_abgeschlossen') {
            btnDispatch.style.display = '';
            btnDispatch.disabled = false;
            btnDispatch.textContent = '💰 DTA erstellen + an MA versenden';
        } else {
            // Status 'abgeschlossen' → Versand bereits durch, Re-Versand via Re-Open
            btnDispatch.style.display = 'none';
        }
    }

    // PDF laden
    try {
        const r = await fetch(`/api/lohnlauf/${p.id}/vorab-pdf`, {
            headers: { Authorization: 'Bearer ' + (localStorage.hrToken || '') }
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            alert('Lohnbelege-PDF konnte nicht generiert werden: ' + (j.message || j.error || `HTTP ${r.status}`));
            return;
        }
        const blob = await r.blob();
        if (_lohnLohnbelegePdfBlobUrl) URL.revokeObjectURL(_lohnLohnbelegePdfBlobUrl);
        _lohnLohnbelegePdfBlobUrl  = URL.createObjectURL(blob);
        _lohnLohnbelegePdfFilename = `Lohnlauf_Vorab_${p.companyProfileId || ''}_${p.year}-${String(p.month).padStart(2,'0')}.pdf`;
        const frame = document.getElementById('lohnLohnbelegePdfFrame');
        if (frame) frame.src = _lohnLohnbelegePdfBlobUrl;
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

function lohnLohnbelegePdfClose() {
    const modal = document.getElementById('lohnLohnbelegePdfModal');
    if (modal) modal.style.display = 'none';
    const frame = document.getElementById('lohnLohnbelegePdfFrame');
    if (frame) frame.src = 'about:blank';
    if (_lohnLohnbelegePdfBlobUrl) {
        URL.revokeObjectURL(_lohnLohnbelegePdfBlobUrl);
        _lohnLohnbelegePdfBlobUrl = null;
    }
    _lohnLohnbelegePeriodeId = null;
}

async function lohnLohnbelegePdfDownload() {
    await saveUrlAsk(_lohnLohnbelegePdfBlobUrl, _lohnLohnbelegePdfFilename);
}

function lohnLohnbelegePdfPrint() {
    const f = document.getElementById('lohnLohnbelegePdfFrame');
    if (!f || !f.contentWindow) return;
    try {
        f.contentWindow.focus();
        f.contentWindow.print();
    } catch (e) {
        alert('Drucken fehlgeschlagen: ' + (e?.message || e));
    }
}

// DTA-XML herunterladen (pain.001 für die MA-Auszahlungen). Geht solange
// die Periode mind. provisorisch_abgeschlossen ist (Snapshots eingefroren).
async function lohnDtaMaDownload() {
    const id = _lohnLohnbelegePeriodeId;
    if (!id) { alert('Keine Periode aktiv.'); return; }
    try {
        const r = await fetch(`/api/lohnlauf/${id}/dta-ma`, {
            headers: { Authorization: 'Bearer ' + (localStorage.hrToken || '') }
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            alert('DTA-Generierung fehlgeschlagen: ' + (j.message || j.error || `HTTP ${r.status}`));
            return;
        }
        const blob = await r.blob();
        const p = window._currentLohnPeriode || {};
        const filename = `DTA_MA_${p.companyProfileId || ''}_${p.year || ''}-${String(p.month || 0).padStart(2,'0')}.xml`;
        await saveBlobAsk(blob, filename);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

// Finaler Versand-Schritt: definitiv-abschliessen ruft im Backend
// • Status → "abgeschlossen"
// • Auszahlungsdatum setzen (heute, kann später per Re-Open angepasst werden)
// • Lohnzettel pro MA ins Postfach
// • E-Mail an MA (Fire-and-Forget, Test-Redirect via SMTP-Settings)
async function lohnLohnbelegeDispatch() {
    const id = _lohnLohnbelegePeriodeId;
    if (!id) { alert('Keine Periode aktiv.'); return; }

    // Walter-Vorgabe 19.05.2026: Bank-Ausführungsdatum (ReqdExctnDt) wird
    // vor dem DTA-Download erfasst und in PayrollPeriode.Auszahlungsdatum
    // persistiert. Default: morgen. Kalender-Picker, nur Zukunft wählbar
    // (Walter-Vorgabe 20.05.2026 — kein ISO-prompt mehr).
    const isoToday    = _isoLocalDate(new Date());
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    const isoTomorrow = _isoLocalDate(tomorrow);
    const auszahlungsdatum = await askPayoutDate({
        title: 'Auszahlungsdatum erfassen',
        message: 'Wann soll die Bank die Löhne ausführen?<br>(= Bank-Ausführungsdatum / ReqdExctnDt im DTA)<br>Heute ist möglich (Bankannahme bis 15:00).',
        defaultIso: isoTomorrow,
        minIso:     isoToday,
    });
    if (!auszahlungsdatum) return;
    const dateDe = `${auszahlungsdatum.slice(8,10)}.${auszahlungsdatum.slice(5,7)}.${auszahlungsdatum.slice(0,4)}`;

    // Walter-Vorgabe 19.05.2026: atomic-Versand. Backend wirft sonst weil DTA
    // nur aus Status 'abgeschlossen' generiert werden kann — Reihenfolge ist
    // also: erst Periode auf abgeschlossen setzen (inkl. Datum + MA-Versand),
    // DANACH DTA herunterladen.
    if (!confirm(
        'DTA erstellen und an MA versenden?\n\n' +
        `Bank-Ausführungsdatum: ${dateDe}\n\n` +
        'Mit JA passiert folgendes (alles atomic):\n' +
        '• Periode → "abgeschlossen", alle Lohnzettel eingefroren\n' +
        '• Lohnzettel + Stundenkontrolle (Monatsblatt zur Unterschrift) landen im MA-Postfach\n' +
        '• E-Mail-Benachrichtigung an MA (Test-Modus: Mails an Test-Redirect-Adresse)\n' +
        `• Bank-Ausführungsdatum ${dateDe} wird in der Periode hinterlegt\n` +
        '• Anschliessend wird das DTA-XML automatisch heruntergeladen\n\n' +
        `Reset durch Admin nur bis ${dateDe} möglich.\n\n` +
        'Wirklich an MA versenden?'
    )) return;

    const btn = document.getElementById('btnLohnLohnbelegeDispatch');
    if (btn) { btn.disabled = true; btn.textContent = '⏳ Versende…'; }

    try {
        const userId = (typeof currentUser !== 'undefined' && currentUser?.id) ? currentUser.id : 0;
        const res = await fetch(`/api/payroll-perioden/${id}/definitiv-abschliessen`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId, auszahlungsdatum })
        });
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            throw new Error(e.message || e.error || 'Fehler beim Versand');
        }
        const data = await res.json().catch(() => ({}));
        showToast('Lohnbelege versendet ✓ — DTA wird heruntergeladen.', 'success');
        if (data?.message) console.log('[lohnDispatch]', data.message);

        // Modal-State aktualisieren: Versand-Banner weg, Versand-Button weg
        const info = document.getElementById('lohnLohnbelegeDispatchInfo');
        if (info) info.style.display = 'none';
        if (btn) btn.style.display = 'none';

        // Jetzt erst DTA-Download (Backend braucht Status 'abgeschlossen').
        // Kurze Pause damit der Toast sichtbar bleibt.
        await new Promise(r => setTimeout(r, 400));
        await lohnDtaMaDownload();

        // Status wird jetzt 'abgeschlossen' → Single-Refresh (Liste + Statusbar
        // springen auf „Abgeschlossen", Buttons: DTA-File + Lohnbelege ansehen).
        await lohnWfRefresh();
    } catch (e) {
        alert(e.message);
        if (btn) { btn.disabled = false; btn.textContent = '💰 DTA erstellen + an MA versenden'; }
    }
}

// Default-Text für die Bemerkung (L-GAV Art. 12 Ziff. 2 — 13. Monatslohn
// Probezeit-Auflösung). Wird als Vorschlag eingefügt wenn kein Text gesetzt ist.
const DEFAULT_PERIODE_BEMERKUNG =
    'Gemäss Art. 12 Ziffer 2 L-GAV entfällt der anteilsmässige Anspruch auf den ' +
    '13. Monatslohn, wenn ein Arbeitsverhältnis im Rahmen der Probezeit aufgelöst wird.';

// Öffnet das Modal zum Bearbeiten der Periode-Bemerkung. Nutzt den im
// Banner gecachten Period-Kontext (window._currentLohnPeriode).
function openPeriodeBemerkungModal() {
    const p = window._currentLohnPeriode;
    if (!p) { alert('Keine Periode aktiv.'); return; }
    const cur = p.pdfFooterText && p.pdfFooterText.trim()
        ? p.pdfFooterText
        : DEFAULT_PERIODE_BEMERKUNG;

    const modal = document.getElementById('periodeBemerkungModal');
    const ta    = document.getElementById('periodeBemerkungText');
    if (!modal || !ta) {
        // Fallback: Browser-Prompt (sollte aber nicht eintreten — Modal ist im DOM)
        const txt = prompt('Bemerkung für diese Lohnperiode (Fussnote auf den Lohnbelegen):', cur);
        if (txt === null) return;
        savePeriodeBemerkung(p.id, txt);
        return;
    }
    ta.value = cur;
    modal.dataset.periodeId = p.id;
    modal.style.display = 'flex';
    setTimeout(() => ta.focus(), 50);
}

function closePeriodeBemerkungModal() {
    const modal = document.getElementById('periodeBemerkungModal');
    if (modal) modal.style.display = 'none';
}

async function savePeriodeBemerkungFromModal() {
    const modal = document.getElementById('periodeBemerkungModal');
    const ta    = document.getElementById('periodeBemerkungText');
    if (!modal || !ta) return;
    const periodeId = parseInt(modal.dataset.periodeId);
    if (!periodeId) return;
    await savePeriodeBemerkung(periodeId, ta.value);
    closePeriodeBemerkungModal();
}

async function savePeriodeBemerkung(periodeId, text) {
    try {
        const res = await fetch(`/api/payroll-perioden/${periodeId}/bemerkung`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ text })
        });
        if (!res.ok) { const e = await res.json().catch(()=>({})); throw new Error(e.message || 'Fehler'); }
        showToast('Bemerkung gespeichert ✓', 'success');
        await lohnWfRefresh();
    } catch(e) { alert(e.message); }
}

// GF-Button „An HR senden" aus der Aktionsleiste — nutzt die aktuell im
// Banner gecachte Periode (window._currentLohnPeriode).
function lohnAnHrSendenAktuell() {
    const p = window._currentLohnPeriode;
    if (!p?.id) { alert('Keine Periode aktiv.'); return; }
    abschliessePeriode(p.id, p.label);
}

async function abschliessePeriode(periodeId, label) {
    if (!confirm(`Periode «${label}» provisorisch abschliessen?\n\nDanach sind die Lohnzettel eingefroren — Korrekturen nur noch durch HR/Admin möglich. HR übernimmt für Vorab-Kontrolle und definitiven Lohnabschluss.`)) return;
    try {
        const res = await fetch(`/api/payroll-perioden/${periodeId}/abschliessen`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId: currentUser?.id ?? 0 })
        });
        if (!res.ok) { const e = await res.json().catch(()=>({})); throw new Error(e.message || 'Fehler'); }
        const r = await res.json();
        showToast(r.message, 'success');
        // Single-Refresh: Statusbar springt von „Offen" auf „Provisorisch
        // abgeschlossen", GF-Buttons verschwinden, HR-Buttons erscheinen.
        await lohnWfRefresh();
    } catch(e) { alert(e.message); }
}

function showToast(msg, type='info') {
    let t = document.getElementById('toastMsg');
    if (!t) {
        t = document.createElement('div');
        t.id = 'toastMsg';
        // Oben rechts platziert (nahe den Aktions-Buttons wie Abschliessen /
        // Aktualisieren in der Lohn-Tab). Kompakt: kleinere Padding, kleinere
        // Schrift, schnelleres Ausblenden — weniger aufdringlich.
        t.style.cssText = 'position:fixed;top:16px;right:24px;padding:6px 14px;border-radius:7px;font-size:12px;font-weight:600;z-index:9999;box-shadow:0 4px 14px rgba(0,0,0,.18);transition:opacity .25s';
        document.body.appendChild(t);
    }
    t.textContent = msg;
    t.style.background = type === 'success' ? '#16a34a' : type === 'error' ? '#dc2626' : '#334155';
    t.style.color = '#fff';
    t.style.opacity = '1';
    clearTimeout(t._timer);
    t._timer = setTimeout(() => { t.style.opacity = '0'; }, 1800);
}

// ── Lohnabrechnungs-PDF: Vorschau-Modal mit Drucken/Speichern/Ablegen ─
// Gleicher Mechanismus wie zviPdfModal und qstaPdfModal: PDF wird einmal
// als Blob geladen, in ein iframe eingebunden, drei Aktions-Buttons
// (Speichern als Datei / Drucken / In MA-Personalakte ablegen).
let _lohnPdfBlob = null, _lohnPdfBlobUrl = null, _lohnPdfEmpId = null, _lohnPdfFilename = 'lohnabrechnung.pdf';

async function exportLohnPdf() {
    if (!lohnCurrentSlip) return;
    const s = lohnCurrentSlip;
    try {
        const res = await fetch(
            `/api/payroll/pdf?employeeId=${s.employeeId}&year=${s.year}&month=${s.month}&companyProfileId=${s.companyId}`,
            { headers: ah() });
        if (!res.ok) {
            alert('Fehler beim Erstellen: ' + (await res.text()));
            return;
        }
        const blob = await res.blob();
        if (_lohnPdfBlobUrl) URL.revokeObjectURL(_lohnPdfBlobUrl);
        _lohnPdfBlob    = blob;
        _lohnPdfBlobUrl = URL.createObjectURL(blob);
        _lohnPdfEmpId   = s.employeeId;
        const cd = res.headers.get('Content-Disposition') || '';
        const m  = /filename="?([^";]+)"?/.exec(cd);
        _lohnPdfFilename = m ? m[1] : `Lohnabrechnung_${s.year}-${String(s.month).padStart(2,'0')}.pdf`;

        const monatNames = ['','Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
        const empName = s.employeeName || '';
        document.getElementById('lohnPdfTitle').textContent =
            empName + ' · ' + monatNames[s.month] + ' ' + s.year;
        document.getElementById('lohnPdfFrame').src = _lohnPdfBlobUrl;
        document.getElementById('lohnSaveForm').style.display = 'none';
        document.getElementById('lohnSaveStatus').textContent = '';
        document.getElementById('lohnSaveBemerkung').value = '';
        document.getElementById('lohnPdfModal').style.display = 'block';
    } catch(e) {
        alert('Netzwerkfehler: ' + e.message);
    }
}

// Monatsblatt Stundenkontrolle (Walter 01.08.2026): MA kontrolliert Stunden
// und unterschreibt. Beim Definitiv-Versand landet das Blatt mit dem Lohnzettel
// im MA-Postfach. Vorschau hier im Lohnlauf.
async function exportStundenkontrollePdf() {
    if (!lohnCurrentSlip) {
        alert('Bitte zuerst einen Mitarbeiter wählen.');
        return;
    }
    const s = lohnCurrentSlip;
    const name = (s.employeeName || 'MA').replace(/\s+/g, '_');
    const filename = `Stundenkontrolle_${name}_${s.year}-${String(s.month).padStart(2, '0')}.pdf`;
    try {
        await previewUrlFetch(
            `/api/payroll/stundenkontrolle-pdf?employeeId=${s.employeeId}&year=${s.year}&month=${s.month}&companyProfileId=${s.companyId}`,
            filename,
            typeof ah === 'function' ? ah() : {});
    } catch (e) {
        alert('Stundenkontrolle konnte nicht erstellt werden: ' + (e.message || e));
    }
}

function lohnPdfClose() {
    document.getElementById('lohnPdfModal').style.display = 'none';
    document.getElementById('lohnPdfFrame').src = 'about:blank';
    if (_lohnPdfBlobUrl) { URL.revokeObjectURL(_lohnPdfBlobUrl); _lohnPdfBlobUrl = null; }
    _lohnPdfBlob = null;
    _lohnPdfEmpId = null;
}

async function lohnPdfDownload() {
    if (_lohnPdfBlob) { await saveBlobAsk(_lohnPdfBlob, _lohnPdfFilename); return; }
    await saveUrlAsk(_lohnPdfBlobUrl, _lohnPdfFilename);
}

function lohnPdfPrint() {
    const f = document.getElementById('lohnPdfFrame');
    if (!f || !f.contentWindow) return;
    try { f.contentWindow.focus(); f.contentWindow.print(); }
    catch (e) { alert('Drucken nicht möglich: ' + (e?.message || e)); }
}

async function lohnPdfSaveToDocsToggle() {
    const form = document.getElementById('lohnSaveForm');
    const sel  = document.getElementById('lohnSaveTyp');
    if (!form || !sel) return;
    if (!sel.options.length) {
        try {
            const r = await fetch('/api/documents/taxonomie', { headers: ah() });
            const tx = r.ok ? await r.json() : [];
            const opts = [];
            tx.forEach(k => {
                (k.typen || []).forEach(t => {
                    opts.push(`<option value="${t.id}">${k.name} → ${t.name}</option>`);
                });
            });
            sel.innerHTML = '<option value="">– Dokument-Typ wählen –</option>' + opts.join('');
            // Bevorzugt: ein Dokument-Typ der "Lohnabrechnung" / "Lohn" enthält
            const preferred = Array.from(sel.options).find(o =>
                /lohnabrechnung|lohnzettel|lohnausweis|\blohn\b/i.test(o.textContent));
            if (preferred) sel.value = preferred.value;
        } catch {
            sel.innerHTML = '<option value="">Fehler beim Laden der Typen</option>';
        }
    }
    form.style.display = (form.style.display === 'none') ? 'block' : 'none';
}

async function lohnPdfSaveToDocsSubmit() {
    const status = document.getElementById('lohnSaveStatus');
    const submit = document.getElementById('lohnSaveSubmit');
    if (!_lohnPdfBlob || !_lohnPdfEmpId) {
        status.textContent = 'Kein PDF zum Ablegen vorhanden.'; status.style.color = '#b91c1c'; return;
    }
    const typId = parseInt(document.getElementById('lohnSaveTyp').value, 10);
    if (!Number.isFinite(typId) || typId <= 0) {
        status.textContent = 'Bitte Dokument-Typ wählen.'; status.style.color = '#b91c1c'; return;
    }
    const branch = (typeof allBranches !== 'undefined' && Array.isArray(allBranches) && fixedCompanyProfileId)
        ? allBranches.find(b => b.id === fixedCompanyProfileId)
        : null;
    const branchCode = branch?.restaurantCode || '';
    if (!branchCode) {
        status.textContent = 'Keine Filiale aktiv — bitte zuerst Filiale wählen.'; status.style.color = '#b91c1c'; return;
    }

    submit.disabled = true; status.textContent = 'Lade hoch…'; status.style.color = '#64748b';

    try {
        const fd = new FormData();
        fd.append('file', _lohnPdfBlob, _lohnPdfFilename);
        fd.append('employeeId', String(_lohnPdfEmpId));
        fd.append('dokumentTypId', String(typId));
        fd.append('branchCode', branchCode);
        const bem = document.getElementById('lohnSaveBemerkung').value.trim();
        if (bem) fd.append('bemerkung', bem);

        const r = await fetch('/api/documents/upload', {
            method: 'POST',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd
        });
        if (!r.ok) {
            const txt = await r.text();
            status.textContent = (r.status === 409)
                ? 'Bereits vorhanden: dieses Dokument existiert für diesen MA schon.'
                : 'Fehler beim Speichern: ' + txt;
            status.style.color = '#b91c1c';
        } else {
            status.textContent = '✓ Lohnabrechnung in MA-Personalakte abgelegt.';
            status.style.color = '#15803d';
        }
    } catch (e) {
        status.textContent = 'Netzwerkfehler: ' + (e?.message || e); status.style.color = '#b91c1c';
    } finally {
        submit.disabled = false;
    }
}

// ═══ Korrekturlohn-Picker (Walter Aug 2026) ═══════════════════════════════
async function openLohnCorrectionPicker() {
    const cid = parseInt(document.getElementById('lohnBranchSelect')?.value || fixedCompanyProfileId || 0);
    const y   = parseInt(document.getElementById('lohnYearSelect')?.value  || new Date().getFullYear());
    const m   = parseInt(document.getElementById('lohnMonthSelect')?.value || (new Date().getMonth()+1));
    if (!cid) { alert('Bitte zuerst eine Filiale wählen.'); return; }

    let modal = document.getElementById('lohnCorrectionModal');
    if (!modal) {
        modal = document.createElement('div');
        modal.id = 'lohnCorrectionModal';
        modal.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(0,0,0,0.45);z-index:3000;align-items:flex-start;justify-content:center;overflow-y:auto;padding:32px 16px';
        modal.innerHTML = `
        <div class="ma-modal-box narrow" style="margin:auto;max-width:480px">
            <div class="ma-modal-head">
                <div>
                    <div class="ma-modal-title">Korrekturlohn hinzufügen</div>
                    <div class="ma-modal-sub">Ausgetretene MA dieser Filiale — Sonder-/Nachzahlung</div>
                </div>
                <button class="ma-modal-close" onclick="closeLohnCorrectionPicker()">✕</button>
            </div>
            <div class="ma-modal-body">
                <input type="text" id="lohnCorrSearch" class="ma-input" placeholder="Suchen…" oninput="filterLohnCorrList()" style="margin-bottom:10px">
                <div id="lohnCorrList" style="max-height:360px;overflow-y:auto"></div>
            </div>
            <div class="ma-modal-foot">
                <button class="btn btn-outline" onclick="closeLohnCorrectionPicker()">Schliessen</button>
            </div>
        </div>`;
        document.body.appendChild(modal);
        modal.addEventListener('click', (e) => { if (e.target === modal) closeLohnCorrectionPicker(); });
    }

    const listEl = document.getElementById('lohnCorrList');
    listEl.innerHTML = '<div style="padding:20px;text-align:center;color:#94a3b8;font-size:13px">Lade…</div>';
    modal.style.display = 'flex';
    document.getElementById('lohnCorrSearch').value = '';

    try {
        const res = await fetch(
            `/api/payroll/correction-candidates?companyProfileId=${cid}&year=${y}&month=${m}`,
            { headers: ah() });
        if (!res.ok) throw new Error(await res.text());
        const cands = await res.json();
        window._lohnCorrCandidates = cands;
        renderLohnCorrList(cands);
    } catch (e) {
        listEl.innerHTML = `<div style="padding:16px;color:#dc2626;font-size:13px">Fehler: ${e.message || e}</div>`;
    }
}

function closeLohnCorrectionPicker() {
    const modal = document.getElementById('lohnCorrectionModal');
    if (modal) modal.style.display = 'none';
}

function filterLohnCorrList() {
    const q = (document.getElementById('lohnCorrSearch')?.value || '').trim().toLowerCase();
    const all = window._lohnCorrCandidates || [];
    const filtered = !q ? all : all.filter(c =>
        `${c.firstName||''} ${c.lastName||''} ${c.employeeNumber||''}`.toLowerCase().includes(q));
    renderLohnCorrList(filtered);
}

function renderLohnCorrList(list) {
    const el = document.getElementById('lohnCorrList');
    if (!el) return;
    if (!list.length) {
        el.innerHTML = '<div style="padding:20px;text-align:center;color:#94a3b8;font-size:13px">Keine ausgetretenen MA gefunden</div>';
        return;
    }
    el.innerHTML = list.map(c => {
        const exit = c.exitDate
            ? c.exitDate.slice(8,10)+'.'+c.exitDate.slice(5,7)+'.'+c.exitDate.slice(0,4)
            : '–';
        const flags = [
            c.hasPendingDepotRefund ? '<span style="font-size:10px;background:#dcfce7;color:#166534;padding:1px 6px;border-radius:8px">Depot-Refund</span>' : '',
            c.hasZulagen ? '<span style="font-size:10px;background:#e0e7ff;color:#3730a3;padding:1px 6px;border-radius:8px">Zulagen</span>' : '',
            _lohnIsCorrection(c.id) ? '<span style="font-size:10px;background:#fef3c7;color:#92400e;padding:1px 6px;border-radius:8px">bereits in Liste</span>' : '',
        ].filter(Boolean).join(' ');
        return `<button type="button" onclick="addLohnCorrectionMa(${c.id})"
            style="display:flex;align-items:center;justify-content:space-between;gap:10px;width:100%;text-align:left;padding:10px 12px;border:none;border-bottom:1px solid #f1f5f9;background:transparent;cursor:pointer">
            <div>
                <div style="font-weight:600;font-size:13px;color:#1a1a1a">${escHtml(c.firstName||'')} ${escHtml(c.lastName||'')}</div>
                <div style="font-size:11px;color:#94a3b8">${escHtml(c.employeeNumber||'')} · ausgetreten ${exit} · ${escHtml(c.employmentModel||'')}</div>
            </div>
            <div style="display:flex;gap:4px;flex-shrink:0">${flags}</div>
        </button>`;
    }).join('');
}

function escHtml(s) {
    return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

async function addLohnCorrectionMa(empId) {
    const cid = parseInt(document.getElementById('lohnBranchSelect')?.value || fixedCompanyProfileId || 0);
    const y   = parseInt(document.getElementById('lohnYearSelect')?.value  || new Date().getFullYear());
    const m   = parseInt(document.getElementById('lohnMonthSelect')?.value || (new Date().getMonth()+1));
    _lohnCorrectionIds.add(Number(empId));
    _lohnCorrSave(cid, y, m);
    closeLohnCorrectionPicker();
    _lohnSelectedEmpId = Number(empId);
    window.activeEmpId = Number(empId);
    await loadLohnList();
    // Nach Rebuild auswählen + Slip laden
    const row = document.querySelector(`#lohnEmpList .lohn-emp-row[data-emp-id="${empId}"]`);
    if (row) row.click();
    else {
        lzInit(empId, cid, y, m);
        loadLohnSlip(empId, cid, y, m);
    }
}

