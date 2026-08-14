// ══════════════════════════════════════════════════════════════════════
//  FERIENPLANER MANAGER (Walter-Vorgabe 14.08.2026)
//  Schwester-Ansicht des Manager-Dienstplans: gleiches Monats-Grid, gleiche
//  Manager-Zeilen — aber NUR Ferien. Der GF zieht Wunsch-Ferien mit der
//  Maus auf (ORANGE = in Planung, per Drag verschiebbar). Klick auf den
//  orangen Balken → «Definitiv setzen»: die echte Ferien-Absenz wird
//  erfasst, der Balken wird GRÜN und die Ferien erscheinen automatisch im
//  Manager-Dienstplan (Absenz-Overlay). Grüner Balken → Rücknahme mit
//  Rückfrage. Sichtbar nur für GF (user) + admin.
// ══════════════════════════════════════════════════════════════════════
let _fplYear = null, _fplMonth = null, _fplData = null;
let _fplDrag = null;   // {mode:'new'|'move', empId, startIso, curIso, plan?, moved}
let _fplPending = null; // Klick-Klick-Planung: {empId, startIso} nach dem 1. Klick

function fplInit() {
    if (_fplYear == null) {
        // Startmonat vom Manager-DP übernehmen, wenn vorhanden.
        if (typeof _dpYear !== 'undefined' && _dpYear != null) { _fplYear = _dpYear; _fplMonth = _dpMonth; }
        else {
            const now = new Date();
            _fplYear = now.getFullYear();
            _fplMonth = now.getMonth() + 1;
        }
    }
    fplLoad();
}

function fplShift(delta) {
    _fplPending = null;
    _fplMonth += delta;
    if (_fplMonth < 1)  { _fplMonth = 12; _fplYear--; }
    if (_fplMonth > 12) { _fplMonth = 1;  _fplYear++; }
    fplLoad();
}

async function fplLoad() {
    const el = document.getElementById('fplGrid');
    const title = document.getElementById('fplMonatTitel');
    if (!el) return;
    const monNames = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
    if (title) title.textContent = `${monNames[_fplMonth - 1]} ${_fplYear}`;
    el.innerHTML = '<div style="color:#8b8b8b;padding:20px;font-size:12.5px">Wird geladen…</div>';
    try {
        const res = await fetch(`/api/ferien-planung?year=${_fplYear}&month=${_fplMonth}`, { headers: ah(), cache: 'no-store' });
        if (!res.ok) { el.innerHTML = '<div style="color:#991b1b;padding:20px;font-size:12.5px">Laden fehlgeschlagen.</div>'; return; }
        _fplData = await res.json();
        fplRender();
    } catch (_) {
        el.innerHTML = '<div style="color:#991b1b;padding:20px;font-size:12.5px">Verbindungsfehler.</div>';
    }
}

function _fplIso(t) { return `${_fplYear}-${String(_fplMonth).padStart(2, '0')}-${String(t).padStart(2, '0')}`; }
function _fplFmtD(iso) { return iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : ''; }
function _fplAddDays(iso, n) {
    const d = new Date(iso + 'T00:00:00');
    d.setDate(d.getDate() + n);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
function _fplDayDiff(a, b) {
    return Math.round((new Date(b + 'T00:00:00') - new Date(a + 'T00:00:00')) / 86400000);
}

function fplRender() {
    const el = document.getElementById('fplGrid');
    if (!el || !_fplData) return;
    const d = _fplData;
    const tage = new Date(_fplYear, _fplMonth, 0).getDate();
    const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
    const wd = ['So','Mo','Di','Mi','Do','Fr','Sa'];

    // Kopfzeilen wie im Manager-DP (KW / Datum / Tag, Montags-Trennlinie).
    let kwRow = '<tr><th class="dp-side">KW</th>';
    let dayRow = '<tr><th class="dp-side">Datum</th>';
    let wdRow = '<tr><th class="dp-side">Tag</th>';
    for (let t = 1; t <= tage; t++) {
        const dt = new Date(_fplYear, _fplMonth - 1, t);
        const we = dt.getDay() === 0 || dt.getDay() === 6;
        const mo = dt.getDay() === 1;
        const cls = (we || mo) ? ` class="${we ? 'dp-we' : ''}${mo ? ' dp-mo' : ''}"` : '';
        kwRow  += `<th${cls}>${mo ? _dpKw(dt) : ''}</th>`;
        dayRow += `<th${cls}>${String(t).padStart(2, '0')}</th>`;
        wdRow  += `<th${cls}>${wd[dt.getDay()]}</th>`;
    }
    kwRow  += '<th class="dp-sum dp-sumfirst"></th><th class="dp-sum"></th></tr>';
    dayRow += '<th class="dp-sum dp-sumfirst"></th><th class="dp-sum"></th></tr>';
    wdRow  += '<th class="dp-sum dp-sumfirst" title="geplante Ferientage (orange) im Monat">Plan</th>'
            + '<th class="dp-sum" title="definitive Ferientage (grün) im Monat">Fix</th></tr>';

    const first = _fplIso(1), last = _fplIso(tage);
    let body = '';
    let lastCp = null;
    for (const z of d.zeilen) {
        if (z.companyProfileId !== lastCp) {
            lastCp = z.companyProfileId;
            const f = (d.filialen || []).find(x => x.id === z.companyProfileId);
            let brCells = '';
            for (let t = 1; t <= tage; t++) {
                const mo = new Date(_fplYear, _fplMonth - 1, t).getDay() === 1;
                brCells += `<td class="dp-brday${mo ? ' dp-mo' : ''}"></td>`;
            }
            body += `<tr class="dp-branch"><td class="dp-side">${esc(f ? (f.code ? f.code + ' ' : '') + (f.name || '') : '')}</td>${brCells}<td class="dp-sumfirst"></td><td></td></tr>`;
        }

        // Tages-Maps: grün (definitive Ferien-Absenz) schlägt orange (Planung).
        const fix = {};   // iso → {absenceId, planungId, von, bis}
        for (const a of (z.ferien || [])) {
            let cur = a.von < first ? first : a.von;
            const end = a.bis > last ? last : a.bis;
            while (cur <= end) { fix[cur] = a; cur = _fplAddDays(cur, 1); }
        }
        const plan = {};  // iso → {id, von, bis}
        for (const p of (z.planungen || [])) {
            let cur = p.von < first ? first : p.von;
            const end = p.bis > last ? last : p.bis;
            while (cur <= end) { if (!fix[cur]) plan[cur] = p; cur = _fplAddDays(cur, 1); }
        }

        const anzName = z.vorname + (z.nachname ? ` ${z.nachname.charAt(0)}.` : '');
        let row = `<td class="dp-side dp-name${z.istGf ? ' dp-gf' : ''}" title="${esc(z.vorname)} ${esc(z.nachname)}${z.istGf ? ' — Geschäftsführer/in' : ''}">${esc(anzName)}${z.istGf ? ' ★' : ''}</td>`;
        let sumPlan = 0, sumFix = 0;
        for (let t = 1; t <= tage; t++) {
            const iso = _fplIso(t);
            const dt = new Date(_fplYear, _fplMonth - 1, t);
            const moCls = dt.getDay() === 1 ? ' dp-mo' : '';
            const fx = fix[iso], pl = plan[iso];
            let cls = 'dp-cell fpl-cell' + moCls, tip = '', attrs = '';
            if (fx) {
                sumFix++;
                cls += ' fpl-fix' + (iso === (fx.von < first ? first : fx.von) ? ' fpl-s' : '') + (iso === (fx.bis > last ? last : fx.bis) ? ' fpl-e' : '');
                tip = `Ferien definitiv ${_fplFmtD(fx.von)}–${_fplFmtD(fx.bis)}${z.planbar ? ' — Klick: zurücknehmen' : ''}`;
                attrs = ` data-fix="1" data-absence="${fx.absenceId}" data-planung="${fx.planungId ?? ''}"`;
            } else if (pl) {
                sumPlan++;
                cls += ' fpl-plan' + (iso === (pl.von < first ? first : pl.von) ? ' fpl-s' : '') + (iso === (pl.bis > last ? last : pl.bis) ? ' fpl-e' : '');
                tip = `In Planung ${_fplFmtD(pl.von)}–${_fplFmtD(pl.bis)}${z.planbar ? ' — Klick: definitiv setzen / löschen, Ziehen: verschieben' : ''}`;
                attrs = ` data-plan="${pl.id}"`;
            } else if (z.planbar) {
                tip = 'Ferien planen: über die Tage ziehen — oder Starttag anklicken, dann Endtag anklicken';
            }
            row += `<td class="${cls}"${attrs} data-emp="${z.employeeId}" data-iso="${iso}" data-planbar="${z.planbar ? 1 : 0}" id="fpl-${z.employeeId}-${iso}" title="${esc(tip)}"></td>`;
        }
        row += `<td class="dp-sum dp-sumfirst">${sumPlan || ''}</td><td class="dp-sum">${sumFix || ''}</td>`;
        body += `<tr>${row}</tr>`;
    }

    const leg = `
        <span style="display:inline-flex;align-items:center;gap:5px;margin-right:14px;font-size:11.5px;color:#646464">
            <span style="display:inline-block;width:26px;height:14px;border-radius:7px;background:#fdba74;border:1px solid rgba(60,55,48,0.18)"></span>in Planung — Balken ziehen zum Verschieben, Klick = definitiv/löschen</span>
        <span style="display:inline-flex;align-items:center;gap:5px;margin-right:14px;font-size:11.5px;color:#646464">
            <span style="display:inline-block;width:26px;height:14px;border-radius:7px;background:#bbf7d0;border:1px solid rgba(60,55,48,0.18)"></span>definitiv — in den Absenzen erfasst, erscheint im Manager-Dienstplan</span>`;

    el.innerHTML = `
        <div style="margin-bottom:8px">${leg}</div>
        <div class="dp-scroll"><table class="dp-table" id="fplTable">
            <thead>${kwRow}${dayRow}${wdRow}</thead>
            <tbody>${body}</tbody>
        </table></div>`;

    // Sticky-Offsets wie im DP aus echten Zeilenhöhen messen.
    const headRows = el.querySelectorAll('thead tr');
    let off = 0;
    headRows.forEach(r => {
        r.querySelectorAll('th').forEach(th => { th.style.top = off + 'px'; });
        off += r.getBoundingClientRect().height;
    });

    _fplBindMouse();
}

// ── Maus-Interaktion: Aufziehen (neu) + Verschieben (Balken) ─────────────
function _fplBindMouse() {
    const table = document.getElementById('fplTable');
    if (!table) return;
    table.onmousedown = (ev) => {
        const td = ev.target.closest('td.fpl-cell');
        if (!td || td.dataset.planbar !== '1') return;
        ev.preventDefault();
        if (td.dataset.fix === '1') {
            _fplDrag = { mode: 'fix', empId: +td.dataset.emp, absenceId: +td.dataset.absence,
                         planungId: td.dataset.planung ? +td.dataset.planung : null, moved: false };
            return;
        }
        if (td.dataset.plan) {
            const z = _fplData.zeilen.find(x => x.employeeId === +td.dataset.emp);
            const p = (z?.planungen || []).find(x => x.id === +td.dataset.plan);
            if (!p) return;
            _fplDrag = { mode: 'move', empId: +td.dataset.emp, plan: p,
                         startIso: td.dataset.iso, curIso: td.dataset.iso, moved: false };
            return;
        }
        _fplDrag = { mode: 'new', empId: +td.dataset.emp, startIso: td.dataset.iso, curIso: td.dataset.iso, moved: false };
        _fplPaintSelection();
    };
    table.onmouseover = (ev) => {
        if (!_fplDrag) return;
        const td = ev.target.closest('td.fpl-cell');
        if (!td || +td.dataset.emp !== _fplDrag.empId) return;
        if (td.dataset.iso !== _fplDrag.curIso) _fplDrag.moved = true;
        _fplDrag.curIso = td.dataset.iso;
        if (_fplDrag.mode === 'new') _fplPaintSelection();
        if (_fplDrag.mode === 'move') _fplPaintMove();
    };
    // Einmalig global registrieren (addEventListener — document.onmouseup
    // würde fremde Handler überschreiben).
    if (!window._fplMouseUpBound) {
        window._fplMouseUpBound = true;
        document.addEventListener('mouseup', () => { if (_fplDrag) _fplMouseUp(); });
    }
}

function _fplClearMarks() {
    document.querySelectorAll('#fplTable .fpl-sel').forEach(c => c.classList.remove('fpl-sel'));
}

function _fplPaintSelection() {
    _fplClearMarks();
    if (!_fplDrag) return;
    const [a, b] = [_fplDrag.startIso, _fplDrag.curIso].sort();
    let cur = a;
    while (cur <= b) {
        document.getElementById(`fpl-${_fplDrag.empId}-${cur}`)?.classList.add('fpl-sel');
        cur = _fplAddDays(cur, 1);
    }
}

function _fplPaintMove() {
    _fplClearMarks();
    if (!_fplDrag || _fplDrag.mode !== 'move') return;
    const delta = _fplDayDiff(_fplDrag.startIso, _fplDrag.curIso);
    const von = _fplAddDays(_fplDrag.plan.von, delta);
    const bis = _fplAddDays(_fplDrag.plan.bis, delta);
    let cur = von;
    while (cur <= bis) {
        document.getElementById(`fpl-${_fplDrag.empId}-${cur}`)?.classList.add('fpl-sel');
        cur = _fplAddDays(cur, 1);
    }
}

async function _fplMouseUp() {
    const drag = _fplDrag;
    _fplDrag = null;
    _fplClearMarks();
    if (!drag) return;

    if (drag.mode === 'new') {
        // Klick ohne Ziehen: Klick-Klick-Planung (Walter 14.08.2026) —
        // 1. Klick = Starttag (markiert), 2. Klick = Endtag → Balken.
        // Gleicher Tag zweimal = 1 Ferientag. Ziehen geht weiterhin.
        if (!drag.moved) {
            if (_fplPending && _fplPending.empId === drag.empId) {
                const [von, bis] = [_fplPending.startIso, drag.startIso].sort();
                _fplPending = null;
                await _fplApi('POST', '/api/ferien-planung', { employeeId: drag.empId, dateFrom: von, dateTo: bis });
            } else {
                _fplPending = { empId: drag.empId, startIso: drag.startIso };
                document.getElementById(`fpl-${drag.empId}-${drag.startIso}`)?.classList.add('fpl-sel');
                showToast(`Starttag ${_fplFmtD(drag.startIso)} gesetzt — jetzt den Endtag anklicken (gleicher Tag nochmals = 1 Tag).`, 'info');
            }
            return;
        }
        _fplPending = null;
        const [von, bis] = [drag.startIso, drag.curIso].sort();
        await _fplApi('POST', '/api/ferien-planung', { employeeId: drag.empId, dateFrom: von, dateTo: bis });
        return;
    }
    if (drag.mode === 'move') {
        if (!drag.moved) { _fplPlanMenu(drag.plan, drag.empId); return; }
        const delta = _fplDayDiff(drag.startIso, drag.curIso);
        if (delta === 0) { _fplPlanMenu(drag.plan, drag.empId); return; }
        await _fplApi('PUT', `/api/ferien-planung/${drag.plan.id}`, {
            employeeId: drag.empId,
            dateFrom: _fplAddDays(drag.plan.von, delta),
            dateTo: _fplAddDays(drag.plan.bis, delta),
        });
        return;
    }
    if (drag.mode === 'fix' && !drag.moved) {
        const ok = await liquidConfirm(
            'Diese definitiven Ferien zurücknehmen? Die Ferien-Absenz wird gelöscht und der Balken wird wieder orange (in Planung).',
            { title: 'Ferien zurücknehmen', yesLabel: 'Ja, zurücknehmen', noLabel: 'Abbrechen' });
        if (!ok) return;
        const url = drag.planungId
            ? `/api/ferien-planung/${drag.planungId}/zuruecknehmen`
            : `/api/ferien-planung/absenz/${drag.absenceId}/zuruecknehmen`;
        await _fplApi('POST', url);
    }
}

// Klick auf orangen Balken: Definitiv setzen / Löschen.
function _fplPlanMenu(plan, empId) {
    document.getElementById('fplMenuModal')?.remove();
    const z = _fplData?.zeilen.find(x => x.employeeId === empId);
    const name = z ? `${z.vorname} ${z.nachname}`.trim() : '';
    const ov = document.createElement('div');
    ov.id = 'fplMenuModal';
    ov.style.cssText = 'position:fixed;inset:0;background:rgba(30,28,25,0.45);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px';
    ov.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 18px 50px rgba(60,55,48,0.22);max-width:440px;width:100%;padding:20px 22px">
            <div style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:4px">🏖 Ferien-Planung</div>
            <div style="font-size:13px;color:#646464;margin-bottom:14px">${name} — ${_fplFmtD(plan.von)} bis ${_fplFmtD(plan.bis)}<br>
                <span style="font-size:12px;color:#8b8b8b">Verschieben: Balken mit der Maus ziehen.</span></div>
            <div style="display:flex;gap:10px;flex-wrap:wrap;justify-content:flex-end">
                <button onclick="document.getElementById('fplMenuModal').remove()" style="background:rgba(255,255,255,0.72);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:7px 14px;font-size:13px;font-weight:600;cursor:pointer;color:#3f3f3f">Abbrechen</button>
                <button onclick="fplPlanDelete(${plan.id})" style="background:rgba(255,255,255,0.72);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:7px 14px;font-size:13px;font-weight:600;cursor:pointer;color:#991b1b">🗑 Löschen</button>
                <button onclick="fplPlanDefinitiv(${plan.id})" style="background:#166534;color:#fff;border:none;border-radius:12px;padding:7px 16px;font-size:13px;font-weight:700;cursor:pointer">✓ Definitiv setzen</button>
            </div>
        </div>`;
    ov.onclick = (e) => { if (e.target === ov) ov.remove(); };
    document.body.appendChild(ov);
}

async function fplPlanDefinitiv(id) {
    document.getElementById('fplMenuModal')?.remove();
    const ok = await liquidConfirm(
        'Ferien definitiv setzen? Die Ferien-Absenz wird beim Manager erfasst und erscheint im Manager-Dienstplan.',
        { title: 'Ferien definitiv', yesLabel: 'Ja, definitiv setzen', noLabel: 'Abbrechen' });
    if (!ok) return;
    await _fplApi('POST', `/api/ferien-planung/${id}/definitiv`);
}

async function fplPlanDelete(id) {
    document.getElementById('fplMenuModal')?.remove();
    const ok = await liquidConfirm('Diese Ferien-Planung löschen?', { title: 'Ferienplaner' });
    if (!ok) return;
    await _fplApi('DELETE', `/api/ferien-planung/${id}`);
}

async function _fplApi(method, url, body) {
    try {
        const res = await fetch(url, {
            method,
            headers: ah(),
            body: body ? JSON.stringify(body) : undefined,
        });
        if (!res.ok) {
            if (typeof lohnEditLock !== 'undefined' && await lohnEditLock.handleResponse(res)) { fplLoad(); return; }
            let msg = 'Aktion fehlgeschlagen.';
            try { const j = await res.json(); msg = j.message || j.error || msg; } catch (_) {}
            showToast(msg, 'error');
        }
    } catch (_) { showToast('Verbindungsfehler.', 'error'); }
    fplLoad();
}

// Vom Manager-DP aus öffnen (Button neben der Monatswahl, nur GF+admin).
function fplOpenFromDp() {
    _fplYear = _dpYear;
    _fplMonth = _dpMonth;
    showPage('ferien-planer');
}
