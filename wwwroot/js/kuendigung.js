// Kündigungsschreiben (Walter-Vorgabe 22.06.2026). HR wählt einen MA, das
// Backend rechnet Kündigungsfrist + letzten Arbeitstag aus dem Vertrag und
// prüft die Sperrfrist (Art. 336c OR). PDF kommt aus /api/kuendigung/{id}/pdf.
let _kuAllEmployees = [];
let _kuInfo = null;

async function kuendigungInit() {
    const d = document.getElementById('kuDatum');
    if (d && !d.value) d.value = new Date().toISOString().slice(0, 10);
    try { _kuAllEmployees = await loadEmployeeLookup(); }
    catch { _kuAllEmployees = []; }
    kuRenderEmpList();
}

function kuRenderEmpList() {
    const sel = document.getElementById('kuEmpSelect');
    if (!sel) return;
    const filter = document.getElementById('kuEmpFilter')?.value || 'active';
    const search = (document.getElementById('kuEmpSearch')?.value || '').toLowerCase().trim();
    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;

    // Filial-Zuordnung (Walter 15.07.2026, gleiche Regel wie ToDo/Kontrolle):
    // AKTIVE Vertraege bestimmen die Filiale; ohne aktiven Vertrag zaehlt der
    // juengste (offenes Ende = juengster). Alte Fremd-Filial-Vertraege und
    // MA ohne Vertrag erscheinen NICHT mehr. Number() gegen Typ-Mismatch.
    const cidN = cid != null && cid !== '' ? Number(cid) : null;
    const inBranch = (e) => {
        if (!cidN) return true;
        const emps = e.employments || [];
        if (emps.length === 0) return false;
        const aktive = emps.filter(v => v.isActive);
        if (aktive.length > 0) return aktive.some(v => Number(v.companyProfileId) === cidN);
        const juengster = emps.slice().sort((a, b) =>
            String(b.contractEndDate || '9999-12-31').localeCompare(String(a.contractEndDate || '9999-12-31')))[0];
        return Number(juengster?.companyProfileId) === cidN;
    };

    let list = _kuAllEmployees.filter(inBranch);
    if (filter === 'active')   list = list.filter(e => e.isActive);
    if (filter === 'inactive') list = list.filter(e => !e.isActive);
    if (search) {
        list = list.filter(e =>
            (`${e.firstName || ''} ${e.lastName || ''}`.toLowerCase().includes(search)) ||
            (e.employeeNumber || '').toLowerCase().includes(search));
    }
    // Konvention: nach Vorname sortieren.
    list.sort((a, b) => {
        const f = (a.firstName || '').localeCompare(b.firstName || '');
        return f !== 0 ? f : (a.lastName || '').localeCompare(b.lastName || '');
    });

    const cur = sel.value;
    sel.innerHTML = `<option value="">— Mitarbeiter wählen —</option>` + list.map(e => {
        const nr  = e.employeeNumber ? ` · ${e.employeeNumber}` : '';
        const tag = e.isActive ? '' : ' · (inaktiv)';
        const name = `${e.firstName || ''} ${e.lastName || ''}`.trim();
        return `<option value="${e.id}">${escapeHtml(name)}${escapeHtml(nr)}${tag}</option>`;
    }).join('');
    if (cur) sel.value = cur;
    // Wenn die aktuelle Auswahl rausgefiltert wurde, Details ausblenden.
    if (sel.value !== cur) { const det = document.getElementById('kuDetails'); if (det) det.style.display = 'none'; }
}

function kuOnEmpChange() {
    const id = +(document.getElementById('kuEmpSelect')?.value || 0);
    const det = document.getElementById('kuDetails');
    if (!id) { if (det) det.style.display = 'none'; return; }
    // Dokument-zuerst-Ablauf (Walter 15.07.2026): ist bereits ein anderes
    // Dokument als Kuendigung gewaehlt, direkt dessen Modal oeffnen.
    const art = document.getElementById('kuDocArt')?.value || 'kuendigung';
    const block = document.getElementById('kuKuendigungBlock');
    if (block) block.style.display = art === 'kuendigung' ? '' : 'none';
    if (art !== 'kuendigung') {
        window.activeEmpId = id;
        if (det) det.style.display = 'block';
        kuOpenDoc(art);
        return;
    }
    window.activeEmpId = id;
    if (det) det.style.display = 'block';
    kuLoadInfo();
}

async function kuLoadInfo() {
    const id = +(document.getElementById('kuEmpSelect')?.value || 0);
    if (!id) return;
    const datum = document.getElementById('kuDatum')?.value || '';
    const url = `/api/kuendigung/${id}/info` + (datum ? `?datum=${datum}` : '');
    try {
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) { document.getElementById('kuSperr').innerHTML = ''; return; }
        const info = await r.json();
        _kuInfo = info;

        const lt = document.getElementById('kuLetzter');
        if (lt) lt.value = info.letzterArbeitstag || '';
        const ort = document.getElementById('kuOrt');
        if (ort && !ort.value) ort.value = info.company?.ort || '';
        // Grund-Typ automatisch auf „Probezeit" stellen, wenn in Probezeit.
        const gt = document.getElementById('kuGrundType');
        if (gt && info.inProbation && gt.value === 'ordentlich') gt.value = 'probezeit';

        const hint = document.getElementById('kuFristHint');
        if (hint) {
            const kopf = info.inProbation
                ? `Probezeit · Frist ${info.noticeText}`
                : `${info.dienstjahr}. Dienstjahr · Frist ${info.noticeText}`;
            // Regel-Herkunft (Walter 15.07.2026): WARUM gilt diese Frist?
            hint.innerHTML = `${escapeHtml(kopf)}${info.noticeRule
                ? `<br><span style="color:#8b8b8b">${escapeHtml(info.noticeRule)}</span>` : ''}`;
        }

        kuRenderSperr(info.sperrfrist);
        kuLoadVerwarnungen(id);
    } catch (_) { /* still */ }
}

// Verwarnungs-Verlauf des MA (Walter 14.07.2026): zeigt beim Kündigungs-
// Entscheid die Eskalations-Historie direkt an (aktive, nicht stornierte).
async function kuLoadVerwarnungen(empId) {
    const host = document.getElementById('kuSperr');
    if (!host) return;
    let box = document.getElementById('kuVerwarnungen');
    if (!box) {
        box = document.createElement('div');
        box.id = 'kuVerwarnungen';
        host.parentNode.insertBefore(box, host.nextSibling);
    }
    box.innerHTML = '';
    try {
        const r = await fetch(`/api/verwarnungen/by-employee/${empId}`, { headers: ah() });
        if (!r.ok) return;
        const list = (await r.json()).filter(v => !v.storniert);
        if (!list.length) {
            box.innerHTML = `<div style="margin-top:8px;background:#f6f3ee;border:1px solid #e5e0d6;color:#6b6152;padding:8px 14px;border-radius:8px;font-size:12px">Keine Verwarnungen im Verlauf.</div>`;
            return;
        }
        const stufeLbl = { VERWARNUNG_1: '1. Verwarnung', VERWARNUNG_2: '2. Verwarnung', LETZTE: 'Letzte Verwarnung' };
        const rows = list.map(v => {
            const g = (v.gruende || '').split('\n').filter(Boolean).join(', ');
            return `<div style="display:flex;gap:10px;font-size:12px;padding:3px 0">
                <span style="font-weight:700;white-space:nowrap">${_kuFmt(v.datum)}</span>
                <span style="font-weight:600;color:${v.stufe === 'LETZTE' ? '#991b1b' : '#92400e'};white-space:nowrap">${stufeLbl[v.stufe] || v.stufe}</span>
                <span style="color:#6b6152">${escapeHtml(g || v.beschreibung || '')}</span>
            </div>`;
        }).join('');
        const hatLetzte = list.some(v => v.stufe === 'LETZTE');
        box.innerHTML = `<div style="margin-top:8px;background:${hatLetzte ? '#fef2f2' : '#fffbeb'};border:1px solid ${hatLetzte ? '#fecaca' : '#fde68a'};padding:10px 14px;border-radius:8px">
            <div style="font-size:12px;font-weight:700;color:${hatLetzte ? '#991b1b' : '#92400e'};margin-bottom:4px">⚠ ${list.length} Verwarnung(en) im Verlauf</div>
            ${rows}
        </div>`;
    } catch (_) { /* still */ }
}

function _kuFmt(iso) {
    if (!iso) return '';
    const s = String(iso).slice(0, 10);
    return s.length === 10 ? `${s.slice(8, 10)}.${s.slice(5, 7)}.${s.slice(0, 4)}` : '';
}

function kuRenderSperr(s) {
    const el = document.getElementById('kuSperr');
    if (!el) return;
    if (!s) { el.innerHTML = ''; return; }
    if (s.blocked) {
        const ab = s.kuendigungAbDatum ? ` (frühestens ab ${_kuFmt(s.kuendigungAbDatum)})` : '';
        el.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 14px;border-radius:8px;font-size:13px;font-weight:600">⛔ Kündigung aktuell nicht zulässig — ${escapeHtml(s.statusText || 'Sperrfrist aktiv')}${ab}.</div>`;
    } else {
        el.innerHTML = `<div style="background:#f0fdf4;border:1px solid #bbf7d0;color:#166534;padding:8px 14px;border-radius:8px;font-size:12.5px">✓ Keine Sperrfrist — ${escapeHtml(s.statusText || 'Kündigung möglich')}</div>`;
    }
}

// Dokument-Auswahl = SCHRITT 1 (Walter 15.07.2026): zuerst das Dokument,
// dann den MA. Kuendigung → Details unten abarbeiten; alle anderen → das
// jeweilige Modal oeffnet sich, sobald ein MA gewaehlt ist.
function kuDocArtChanged() {
    const art = document.getElementById('kuDocArt')?.value || 'kuendigung';
    const block = document.getElementById('kuKuendigungBlock');
    if (block) block.style.display = art === 'kuendigung' ? '' : 'none';
    const id = +(document.getElementById('kuEmpSelect')?.value || 0);
    if (art !== 'kuendigung' && id) kuOpenDoc(art);
}

function kuOpenDoc(art) {
    const id = +(document.getElementById('kuEmpSelect')?.value || 0);
    if (!id) { alert('Bitte zuerst einen Mitarbeiter wählen.'); return; }
    window.activeEmpId = id;
    // Globale MA-Auswahl der MA-Maske mitziehen (Verwarnungs-Modal speichert
    // gegen selectedEmployeeId; stale selectedEmployee vermeiden).
    try { selectedEmployeeId = id; selectedEmployee = null; } catch (_) {}
    if (art === 'verwarnung') { openVerwarnungModal(null); return; }
    openZeugnisModal(id, art === 'zwischen', art === 'best');
}

// Abbrechen (Walter 15.07.2026): Formular zuruecksetzen + zurueck zum HR-Hub.
function kuAbbrechen() {
    const sel = document.getElementById('kuEmpSelect');
    if (sel) sel.value = '';
    const det = document.getElementById('kuDetails');
    if (det) det.style.display = 'none';
    const grund = document.getElementById('kuGrundText');
    if (grund) grund.value = '';
    const suche = document.getElementById('kuEmpSearch');
    if (suche) suche.value = '';
    _kuInfo = null;
    if (typeof showPage === 'function') showPage('hr-hub');
}

async function kuGenerate() {
    const id = +(document.getElementById('kuEmpSelect')?.value || 0);
    if (!id) { alert('Bitte zuerst einen Mitarbeiter wählen.'); return; }
    // Sperrfrist: warnen, aber die Erstellung bleibt HR-Entscheid (nicht hart sperren).
    if (_kuInfo?.sperrfrist?.blocked &&
        !confirm('Achtung: Für diesen MA läuft eine Sperrfrist (Kündigung wäre evtl. nichtig). Trotzdem ein Schreiben erstellen?')) return;

    const grundType = document.getElementById('kuGrundType')?.value || 'ordentlich';
    const grundText = (document.getElementById('kuGrundText')?.value || '').trim();
    const grundMap = { ordentlich: '', probezeit: 'Kündigung während der Probezeit', fristlos: 'Fristlose Kündigung' };
    const grund = [grundMap[grundType], grundText].filter(Boolean).join(' — ') || null;

    const body = {
        kuendigungsDatum:  document.getElementById('kuDatum')?.value || null,
        letzterArbeitstag: document.getElementById('kuLetzter')?.value || null,
        ort:               (document.getElementById('kuOrt')?.value || '').trim() || null,
        grund,
        eingeschrieben:    document.getElementById('kuEingeschrieben')?.checked ?? false
    };
    try {
        const r = await fetch(`/api/kuendigung/${id}/pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!r.ok) {
            let m = `Fehler (${r.status})`;
            try { const j = await r.json(); if (j?.message) m = j.message; } catch (_) {}
            alert('PDF konnte nicht erstellt werden.\n' + m);
            return;
        }
        const blob = await r.blob();
        const cd = r.headers.get('Content-Disposition') || '';
        const m = cd.match(/filename="?([^"]+)"?/);
        const fn = m ? m[1] : `Kuendigung_${id}.pdf`;
        if (typeof previewFileModal === 'function') previewFileModal(blob, fn);
        else if (typeof saveBlobAsk === 'function') saveBlobAsk(blob, fn);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}
