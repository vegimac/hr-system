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

    document.getElementById('lohnZulagenPanel').style.display = 'block';
    lzCloseForm();

    // Lohnpositionen für Dropdown einmalig laden
    if (_lzLohnpositionen.length === 0) {
        try {
            const res = await fetch('/api/lohn-zulag-typen', { headers: ah() });
            _lzLohnpositionen = res.ok ? await res.json() : [];
        } catch { _lzLohnpositionen = []; }
    }

    await lzLoad();
}

async function lzLoad() {
    const listEl = document.getElementById('lohnZulagenList');
    listEl.innerHTML = '<div style="padding:12px 0;color:#94a3b8;font-size:13px">Lade…</div>';
    const periode = `${_lzCurrentYear}-${String(_lzCurrentMonth).padStart(2,'0')}`;
    try {
        const res = await fetch(`/api/lohn-zulagen/${_lzCurrentEmpId}/${periode}`, { headers: ah() });
        const list = res.ok ? await res.json() : [];
        // Liste zwischenspeichern, damit lzEditById() die Bemerkung sauber
        // aufgreifen kann — vermeidet Quoting-Probleme mit Sonderzeichen
        // (Anführungszeichen etc.) die im onclick-Attribut brechen würden.
        window._lzItems = list;
        if (!list.length) {
            listEl.innerHTML = '<div style="padding:14px 0;color:#94a3b8;font-size:13px;font-style:italic">Keine Zulagen/Abzüge für diese Periode</div>';
            return;
        }
        listEl.innerHTML = list.map(z => {
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
    } catch(e) {
        listEl.innerHTML = `<div style="padding:12px 0;color:#dc2626;font-size:13px">Fehler: ${e.message}</div>`;
    }
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

    // Panel und Formular sichtbar machen
    document.getElementById('lohnZulagenPanel').style.display = 'block';
    document.getElementById('lohnZulagenForm').style.display  = 'block';
    document.getElementById('lohnZulagenForm').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function lzCloseForm() {
    document.getElementById('lohnZulagenForm').style.display = 'none';
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
        if (!res.ok) { alert('Fehler beim Speichern.'); return; }
        lzCloseForm();
        await lzLoad();
        // Lohnabrechnung neu berechnen
        loadLohnSlip(_lzCurrentEmpId, _lzCurrentCompId, _lzCurrentYear, _lzCurrentMonth);
    } catch(e) { alert('Verbindungsfehler: ' + e.message); }
}

async function lzDelete(id) {
    if (!confirm('Eintrag löschen?')) return;
    try {
        const res = await fetch(`/api/lohn-zulagen/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) {
            const err = await res.text();
            alert('Fehler beim Löschen: ' + err);
            return;
        }
        await lzLoad();
        loadLohnSlip(_lzCurrentEmpId, _lzCurrentCompId, _lzCurrentYear, _lzCurrentMonth);
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

async function loadLohnList() {
    const companyId = document.getElementById('lohnBranchSelect').value;
    if (!companyId) return;

    const listEl = document.getElementById('lohnEmpList');
    listEl.innerHTML = '<div style="padding:20px;text-align:center;color:#94a3b8">Lade…</div>';

    const cid = parseInt(companyId);
    const y   = parseInt(document.getElementById('lohnYearSelect')?.value  || new Date().getFullYear());
    const m   = parseInt(document.getElementById('lohnMonthSelect')?.value || (new Date().getMonth()+1));

    try {
        // Snapshots für diese Periode laden (um ✓ anzuzeigen)
        let confirmedEmpIds = new Set();
        try {
            const pRes = await fetch(`/api/payroll-perioden/current?companyProfileId=${cid}&year=${y}&month=${m}`, { headers: ah() });
            const pData = pRes.ok ? await pRes.json() : null;
            if (pData?.id) {
                const snRes = await fetch(`/api/payroll-perioden/${pData.id}/snapshots`, { headers: ah() });
                if (snRes.ok) {
                    const snaps = await snRes.json();
                    snaps.forEach(s => confirmedEmpIds.add(s.employeeId));
                }
            }
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
                // Aktiver Vertrag für diese Filiale (isActive=true + companyProfileId passt + Vertrag bereits gestartet)
                const emp = (e.employments || [])
                    .filter(v => v.companyProfileId === cid && v.isActive)
                    .filter(v => {
                        // Vertrag muss in der Periode aktiv sein:
                        //   contractStartDate <= periodEnd
                        //   AND (!contractEndDate || contractEndDate >= periodStart)
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
                return { ...e, employmentModel: emp.employmentModel, empObj: emp };
            })
            .filter(Boolean);

        listEl.innerHTML = '';
        if (active.length === 0) {
            listEl.innerHTML = '<div style="padding:20px;text-align:center;color:#94a3b8">Keine Mitarbeiter</div>';
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
            const modelColor = { MTP:'#d1fae5', UTP:'#fef3c7', FIX:'#dbeafe', 'FIX-M':'#ede9fe' };
            const isConfirmed = confirmedEmpIds.has(e.id);
            const row = document.createElement('div');
            row.className = 'lohn-emp-row';
            if (e.id === _lohnSelectedEmpId) row.classList.add('lohn-emp-active');
            row.dataset.empId = e.id;
            row.onclick = () => {
                _lohnSelectedEmpId = e.id;
                localStorage.setItem(`lohnLastEmp_${cid}`, String(e.id));
                highlightLohnEmp(row);
                showLohnVertragInfo(e);
                const year  = parseInt(document.getElementById('lohnYearSelect')?.value  || new Date().getFullYear());
                const month = parseInt(document.getElementById('lohnMonthSelect')?.value || (new Date().getMonth()+1));
                lzInit(e.id, cid, year, month);
                loadLohnSlipFromPanel();
            };
            row.innerHTML = `
                <div style="width:34px;height:34px;border-radius:50%;background:${isConfirmed?'#dcfce7':'#e2e8f0'};display:flex;align-items:center;justify-content:center;font-weight:700;font-size:12px;color:${isConfirmed?'#166534':'#475569'};flex-shrink:0">
                    ${isConfirmed ? '✓' : initials.toUpperCase()}
                </div>
                <div style="flex:1;min-width:0">
                    <div class="lohn-emp-name" style="font-weight:600;font-size:13px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${e.firstName} ${e.lastName}</div>
                    <div class="lohn-emp-nr" style="font-size:11px;color:${isConfirmed?'#16a34a':'#94a3b8'}">${isConfirmed ? 'Lohn bestätigt' : (e.employeeNumber || '')}</div>
                </div>
                <span style="font-size:10px;font-weight:600;padding:2px 7px;border-radius:10px;background:${modelColor[e.employmentModel]||'#f1f5f9'}">${e.employmentModel}</span>
                <button title="Quellensteuer erfassen" onclick="event.stopPropagation();openQstModal(${e.id},${JSON.stringify({firstName:e.firstName,lastName:e.lastName,zipCode:e.zipCode,city:e.city,nationalityCode:e.nationalityRef?.code??e.nationality,permitTypeName:e.permitType?.name,zivilstand:e.zivilstand})})"
                    style="background:none;border:1px solid #cbd5e1;border-radius:6px;padding:2px 7px;font-size:11px;cursor:pointer;color:#475569;flex-shrink:0" >QST</button>`;
            listEl.appendChild(row);
        });

        // Ausgewählten MA innerhalb der Liste nach oben scrollen (nicht umordnen)
        // und den Detail-Panel/Lohnzettel automatisch laden, falls noch nichts
        // gerendert ist. Das verhindert den leeren "Mitarbeiter auswählen"-Zustand
        // beim ersten Seitenbesuch.
        if (_lohnSelectedEmpId) {
            const sel = listEl.querySelector(`.lohn-emp-row[data-emp-id="${_lohnSelectedEmpId}"]`);
            if (sel) {
                listEl.scrollTop += sel.getBoundingClientRect().top - listEl.getBoundingClientRect().top;
                // Trigger Detail-Load wenn das Vertrag-Panel noch nicht sichtbar ist
                // (= frischer Seitenaufruf, noch kein MA manuell angeklickt worden).
                const vertragPanel = document.getElementById('lohnVertragPanel');
                if (vertragPanel && vertragPanel.style.display === 'none') {
                    sel.click();
                }
            }
        }

        // Banner mit den aktuellen Zählern aktualisieren (Offen-Pille +
        // X/Y bestätigt). Wird hier zentral aufgerufen, damit er nach
        // jedem Lohn-Speichern / Bestätigen sofort stimmt.
        loadLohnPeriodBanner(cid, y, m);
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
    const panel     = document.getElementById('lohnVertragPanel');
    const empty     = document.getElementById('lohnVertragEmpty');
    const nameEl    = document.getElementById('lohnVertragName');
    const infoEl    = document.getElementById('lohnVertragInfo');
    const perPanel  = document.getElementById('lohnPeriodToolbar');
    if (!panel) return;

    const contract = (emp.employments || [])
        .filter(c => c.isActive)
        .sort((a,b) => (b.contractStartDate||'') > (a.contractStartDate||'') ? 1 : -1)[0];

    if (!contract) {
        empty.style.display = 'block'; panel.style.display = 'none';
        if (perPanel) perPanel.style.display = 'none';
        return;
    }

    const modelLabel = { UTP:'Stundenlohn (UTP)', MTP:'Mindestpensum (MTP)', FIX:'Festlohn (FIX)', 'FIX-M':'Management (FIX-M)' };
    const modelColor = { MTP:'#d1fae5', UTP:'#fef3c7', FIX:'#dbeafe', 'FIX-M':'#ede9fe' };
    const fmt = d => d ? new Date(d).toLocaleDateString('de-CH') : '–';
    const lohn = contract.salaryType === 'monthly' && contract.monthlySalary
        ? `CHF ${Number(contract.monthlySalary).toFixed(2)} / Monat`
        : contract.hourlyRate ? `CHF ${Number(contract.hourlyRate).toFixed(2)} / Stunde` : '–';

    nameEl.innerHTML = `${emp.firstName} ${emp.lastName}
        <span style="margin-left:8px;font-size:11px;font-weight:600;padding:2px 8px;border-radius:8px;background:${modelColor[contract.employmentModel]||'#f1f5f9'}">${modelLabel[contract.employmentModel]||contract.employmentModel}</span>`;

    infoEl.innerHTML = `
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:4px 12px">
            <div>Personal-Nr.: <b style="color:#374151">${emp.employeeNumber||'–'}</b></div>
            <div>Funktion: <b style="color:#374151">${contract.jobTitle||'–'}</b></div>
            <div>Lohn: <b style="color:#374151">${lohn}</b></div>
            ${contract.guaranteedHoursPerWeek ? `<div>Garantiert: <b style="color:#374151">${contract.guaranteedHoursPerWeek} h/Wo</b></div>` : ''}
            ${contract.employmentPercentage ? `<div>Pensum: <b style="color:#374151">${contract.employmentPercentage}%</b></div>` : ''}
            <div>Vertrag seit: <b style="color:#374151">${fmt(contract.contractStartDate)}</b></div>
            ${contract.probationEndDate ? `<div style="color:#92400e">Probezeit bis: <b>${fmt(contract.probationEndDate)}</b></div>` : ''}
        </div>`;

    empty.style.display = 'none';
    panel.style.display = 'block';
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
    // Mitarbeiterliste neu laden (Bestätigt-Badges aktualisieren).
    await loadLohnList();
    // Lohnzettel für die neue Periode neu berechnen
    if (empId) {
        loadLohnPeriodBanner(cid, year, month);
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
    // Top-Aktions-Buttons sichtbar machen (PDF / Reopen / Bestätigen)
    const ta = document.getElementById('lohnTopActions');
    if (ta) ta.style.display = 'flex';

    // Sofort Default-Button-State setzen (Confirm sichtbar, Reopen versteckt),
    // damit kein "Wieder eröffnen"-Button vom vorherigen MA hängen bleibt.
    const btnBestReset   = document.getElementById('btnLohnBestaetigen');
    const btnReopenReset = document.getElementById('btnLohnReopen');
    if (btnBestReset)   btnBestReset.style.display   = '';
    if (btnReopenReset) btnReopenReset.style.display = 'none';

    const myToken = ++_lohnSlipReqToken;

    try {
        // Cache-Buster + cache:no-store damit Browser nach Absenz-/Stempelzeit-
        // Änderungen NICHT den alten gecachten Lohnzettel zurückgibt.
        const ts = Date.now();
        const res  = await fetch(`/api/payroll/calculate?employeeId=${employeeId}&year=${year}&month=${month}&companyProfileId=${companyId}&_=${ts}`,
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
            // Bestätigen-Button deaktivieren während Pause
            const btn = document.getElementById('btnLohnBestaetigen');
            if (btn) btn.disabled = true;
            return;
        }
        lohnCurrentSlip = { ...slip, employeeId, companyId, year, month };
        const btn = document.getElementById('btnLohnBestaetigen');
        if (btn) btn.disabled = false;
        renderLohnSlip(slip);

        // Saldo-Status prüfen → Confirm/Reopen-Button umschalten.
        // Cache-Bypass + Race-Token: Browser darf KEIN gecachtes Saldo-Resultat
        // einer früheren Periode zurückgeben, und falls inzwischen ein neuer
        // loadLohnSlip-Aufruf passiert ist, dürfen wir nichts mehr setzen.
        // Zusätzlich: Saldo zählt nur als 'confirmed', wenn auch ein Snapshot
        // für diesen MA + diese Periode existiert (PayrollSaldo + Snapshot
        // können theoretisch divergieren, dann ist Reopen NICHT angezeigt
        // sondern Confirm, weil noch keine echte Bestätigung vorliegt).
        try {
            const ts = Date.now();
            const [sRes, snapRes] = await Promise.all([
                fetch(`/api/payroll/saldo?employeeId=${employeeId}&year=${year}&month=${month}&companyProfileId=${companyId}&_=${ts}`,
                      { headers: ah(), cache: 'no-store' }),
                fetch(`/api/payroll-perioden/current?companyProfileId=${companyId}&year=${year}&month=${month}&_=${ts}`,
                      { headers: ah(), cache: 'no-store' })
            ]);
            // Stale-Antwort verwerfen, falls inzwischen ein neuer Aufruf läuft
            if (myToken !== _lohnSlipReqToken) return;

            const saldo = sRes.ok ? await sRes.json() : null;
            let snapshotExists = false;
            try {
                const periode = snapRes.ok ? await snapRes.json() : null;
                if (periode?.id) {
                    const sn = await fetch(`/api/payroll-perioden/${periode.id}/snapshots?_=${ts}`,
                                            { headers: ah(), cache: 'no-store' });
                    if (sn.ok) {
                        const arr = await sn.json();
                        snapshotExists = Array.isArray(arr)
                            && arr.some(x => x.employeeId === employeeId);
                    }
                }
            } catch {}
            if (myToken !== _lohnSlipReqToken) return;

            // Als 'bestätigt' gilt nur: Saldo confirmed UND Snapshot vorhanden.
            // Das verhindert false-positives durch Inkonsistenzen aus alten
            // Datenbeständen (Saldo confirmed ohne Snapshot ist Recovery-Fall).
            const isConfirmed = !!(saldo && saldo.status === 'confirmed' && snapshotExists);
            const btnBest   = document.getElementById('btnLohnBestaetigen');
            const btnReopen = document.getElementById('btnLohnReopen');
            if (btnBest)   btnBest.style.display   = isConfirmed ? 'none' : '';
            if (btnReopen) btnReopen.style.display = isConfirmed ? '' : 'none';
        } catch {
            // Bei Fehler explizit auf Default zurück (Confirm sichtbar)
            if (myToken !== _lohnSlipReqToken) return;
            const btnBest   = document.getElementById('btnLohnBestaetigen');
            const btnReopen = document.getElementById('btnLohnReopen');
            if (btnBest)   btnBest.style.display   = '';
            if (btnReopen) btnReopen.style.display = 'none';
        }
    } catch(e) {
        document.getElementById('lohnSlip').innerHTML = `<div style="padding:40px;color:#dc2626">Fehler: ${e.message}</div>`;
    }
}

// Toggle: Ferien-Kürzung anwenden (Stufe 2)
// Wenn aktiviert: vom aktuellen Ferien-Tage-Saldo wird die vorgeschlagene
// Kürzung abgezogen. Wird beim Speichern in PayrollSaldo persistiert.
function toggleFerienKuerzung(checkbox, vorschlagTage) {
    const slip = window.currentLohnSlip;
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
    if (typeof renderLohnAbrechnung === 'function') renderLohnAbrechnung(slip);
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
        // Slip + MA-Liste neu laden
        await loadLohnSlip(s.employeeId, s.companyId, s.year, s.month);
        await loadLohnList();
        await loadLohnPeriodBanner(s.companyId, s.year, s.month);
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
function renderLohnSlip(s, targetEl) {
    const mount = targetEl || document.getElementById('lohnSlip');
    if (!mount) return;
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
    <div class="ls-wrap" style="padding-top:4px;padding-bottom:6px">
        <!-- Header weggelassen (Walter 16.05.2026): Filiale + Periode + MA stehen
             bereits oben im Akonto-/Lohn-Header der Page. "Lohnabrechnung"-Titel
             ist visuell durch die Tabelle selbst klar. Volle Adresse + Druck-
             Header bleiben im PDF/PayrollPdfService für den Versand erhalten. -->
        <div style="display:flex;justify-content:space-between;align-items:baseline;margin-bottom:4px;font-size:12px;color:#94a3b8">
            <span>${s.companyName} · ${s.periodLabel}</span>
            <span style="font-weight:700;color:#1e293b;font-size:13px;letter-spacing:.3px">Lohnabrechnung</span>
        </div>

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
                <tr class="ls-section-hd"><td colspan="6">Lohn</td></tr>
                ${lohnRows}
                <tr class="ls-total-row">
                    <td colspan="4" class="ls-desc">Total Lohn</td>
                    <td class="ls-amt"></td>
                    <td class="ls-amt">${fmt(s.totalLohn)}</td>
                </tr>

                ${s.abzugLines.length > 0 ? `
                <tr><td colspan="6" style="height:5px"></td></tr>
                <tr class="ls-section-hd">
                    <td colspan="5">Abzüge</td>
                    <td style="text-align:right">${s.usingDefaultDeductions ? '<span style="font-size:10px;font-weight:500;color:#b45309;background:#fef3c7;border:1px solid #fcd34d;border-radius:4px;padding:1px 6px">CH-Standard 2026</span>' : ''}</td>
                </tr>
                ${abzugRows}
                <tr class="ls-total-row">
                    <td colspan="4" class="ls-desc">Total Abzüge</td>
                    <td class="ls-amt"></td>
                    <td class="ls-amt" style="color:#dc2626">${fmt(s.totalAbzuege)}</td>
                </tr>
                ${s.usingDefaultDeductions ? `<tr><td colspan="6" style="font-size:10px;color:#92400e;padding:2px 8px 6px">⚠ Standardsätze AHV 5.3 % / ALV 1.1 % – bitte unter Filialen &gt; Abzüge konfigurieren</td></tr>` : ''}
                ` : ''}

                <tr><td colspan="6" style="height:5px"></td></tr>
                <tr class="ls-netto-row">
                    <td colspan="4" class="ls-desc">Nettolohn</td>
                    <td class="ls-amt"></td>
                    <td class="ls-amt">${fmt(s.nettolohn)}</td>
                </tr>

                ${(s.zulagenExtraLines?.length > 0 || s.abzuegeExtraLines?.length > 0) ? `
                <tr><td colspan="6" style="height:4px"></td></tr>
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
                <tr>
                    <td class="ls-desc">${l.bezeichnung}</td>
                    <td colspan="4"></td>
                    <td class="ls-amt" style="color:#dc2626">${fmt(l.betrag)}</td>
                </tr>`).join('')}` : ''}
                <tr class="ls-netto-row" style="border-top:2px solid #1e40af">
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
            <table class="ls-table" style="margin-top:6px">
                <thead>
                    <tr class="ls-col-hd">
                        <th style="text-align:left">Stunden-Übersicht</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Soll</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Ist</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Differenz</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Übertrag Vormonat</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Saldo Lohnperiode</th>
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
            const isUtp      = model === 'UTP';
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
            <table class="ls-table" style="margin-top:6px">
                <thead>
                    <tr class="ls-col-hd">
                        <th style="text-align:left">Saldi</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Vormonat</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Aktuell</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Bezogen</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Saldo Lohnperiode</th>
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
            <table class="ls-table" style="margin-top:6px">
                <thead>
                    <tr class="ls-col-hd">
                        <th style="text-align:left">Auszahlung an</th>
                        <th style="text-align:right;color:#94a3b8;font-weight:400">Betrag CHF</th>
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
                lohnAbtretungen:             (s.lohnAbtretungen ?? []).map(l => ({ assignmentId: l.assignmentId, betrag: l.betrag }))
            })
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({ message: res.statusText }));
            throw new Error(err.message || 'Fehler beim Bestätigen');
        }
        const result = await res.json();
        // Mitarbeiterliste neu laden (Checkmark anzeigen)
        await loadLohnList();
        // Periode-Banner aktualisieren
        await loadLohnPeriodBanner(cid, year, month);
        showToast('Lohn bestätigt ✓', 'success');
    } catch(e) {
        alert(e.message);
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = '✓ Lohn bestätigen'; }
    }
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

async function loadLohnPeriodBanner(companyId, year, month) {
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
        const pillBg    = isAbgeschlossen ? '#dcfce7' : isProvisorisch ? '#fef3c7' : '#e0f2fe';
        const pillColor = isAbgeschlossen ? '#166534' : isProvisorisch ? '#92400e' : '#0369a1';

        // Kompakter Status-Zeile: nur Status-Pill + Bestätigungs-Zähler + Action.
        // Periodenbezeichnung steht oben in der Monat-/Jahr-Auswahl, Datumsbereich
        // bei Bedarf als Tooltip. Zähler nutzt die Werte aus loadLohnList
        // (aktive MAs der Filiale) — konsistent mit der MA-Auswahl links.
        const stats       = window._lohnStats || { confirmedCount: 0, activeTotal: 0 };
        const confirmText = `${stats.confirmedCount}/${stats.activeTotal} bestätigt`;
        const allBestaetigt = stats.activeTotal > 0 && stats.confirmedCount >= stats.activeTotal;

        // Abschluss-Button: nur wenn Periode offen UND alle Lohnzettel bestätigt
        // (sonst wäre der Klick eh ein Backend-Fehler). Bei < 100% bestätigt
        // erscheint stattdessen ein hellgrauer Hinweis-Text. Bei provisorisch_-
        // abgeschlossen: keine Button — HR übernimmt im Lohnlauf-Modul.
        const abschlussBtn = isOffen
            ? (allBestaetigt
                ? `<button class="btn btn-sm btn-outline" style="margin-left:auto;color:#0284c7;border-color:#7dd3fc;font-size:11px;padding:3px 10px"
                    onclick="abschliessePeriode(${p.id},'${p.label}')">Provisorischer Lohnabschluss</button>`
                : `<span style="margin-left:auto;font-size:11px;color:#94a3b8">Erst alle Lohnzettel bestätigen (${stats.activeTotal - stats.confirmedCount} ausstehend)</span>`)
            : isProvisorisch
                ? `<span style="margin-left:auto;font-size:11px;color:#92400e">Wartet auf HR — Lohnlauf läuft</span>`
                : '';

        // Aktuellen Periode-Kontext im window-Objekt cachen, damit der
        // Bemerkungs-Button keine Argumente benötigt (vermeidet Escaping-
        // Probleme bei mehrzeiligen Texten mit Anführungszeichen).
        window._currentLohnPeriode = p;
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
                <button class="btn btn-sm btn-outline"
                        style="color:${footerColor};border-color:#e2e8f0;font-size:11px;padding:3px 10px"
                        onclick="openPeriodeBemerkungModal()">
                    ${footerLabel}
                </button>
                ${abschlussBtn}
            </div>`;
    } catch(e) {
        banner.style.display = 'none';
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
        const cid   = parseInt(document.getElementById('lohnBranchSelect').value);
        const year  = parseInt(document.getElementById('lohnYearSelect').value);
        const month = parseInt(document.getElementById('lohnMonthSelect').value);
        await loadLohnPeriodBanner(cid, year, month);
    } catch(e) { alert(e.message); }
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
        const cid   = parseInt(document.getElementById('lohnBranchSelect').value);
        const year  = parseInt(document.getElementById('lohnYearSelect').value);
        const month = parseInt(document.getElementById('lohnMonthSelect').value);
        await loadLohnPeriodBanner(cid, year, month);
        await loadLohnList();
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

function lohnPdfClose() {
    document.getElementById('lohnPdfModal').style.display = 'none';
    document.getElementById('lohnPdfFrame').src = 'about:blank';
    if (_lohnPdfBlobUrl) { URL.revokeObjectURL(_lohnPdfBlobUrl); _lohnPdfBlobUrl = null; }
    _lohnPdfBlob = null;
    _lohnPdfEmpId = null;
}

function lohnPdfDownload() {
    if (!_lohnPdfBlobUrl) return;
    const a = document.createElement('a');
    a.href = _lohnPdfBlobUrl;
    a.download = _lohnPdfFilename;
    document.body.appendChild(a); a.click(); document.body.removeChild(a);
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

