// ═══════════════════════════════════════════════════════════════════════════
//  ABSENZ- & FERIENKALENDER pro Filiale (Walter-Vorgabe 22.07.2026, v2)
//  Frei verschiebbares 31-Tage-Fenster statt fixem Kalendermonat:
//  ‹/› schieben um 7 Tage, «/» um einen Monat, Trackpad-Wisch (wheel deltaX)
//  schiebt tageweise. Volle Breite (table-layout fixed, kein H-Scroll),
//  kompakte Zeilen. Zeilen = aktive MA der Filiale (nach Vorname).
//  Endpoint: GET /api/absences/kalender?companyProfileId&from&to
//  Einstieg: Kachel im Restaurant-Admin-Tab + showPage('absenz-kalender').
// ═══════════════════════════════════════════════════════════════════════════

let _akalStart = null;                     // Date = erster Tag des Fensters
const AKAL_WIN = 31;                       // Fensterbreite in Tagen
let _akalFilter = 'alle';                  // alle | ferien | krankunfall | mitabsenz
let _akalData = null;                      // letzter Server-Response
let _akalWheelAcc = 0;                     // Trackpad-Wisch-Akkumulator
let _akalLoadTimer = null;                 // Debounce fürs Nachladen beim Wischen

// Typ-Konfiguration: Farbe (CSS-Klasse), Legende, Balken-Kürzel.
// count=false → zählt NICHT in der «Abwesend»-Summenzeile (Wunschfrei etc.).
const AKAL_TYPES = {
    FERIEN:     { cls: 'akal-ferien',   label: 'Ferien',                 icon: 'F', count: true  },
    KRANK:      { cls: 'akal-krank',    label: 'Krankheit',              icon: 'K', count: true  },
    UNFALL:     { cls: 'akal-unfall',   label: 'Unfall',                 icon: 'U', count: true  },
    MUTT_VATER: { cls: 'akal-mutter',   label: 'Mutter-/Vaterschaft',    icon: 'M', count: true  },
    MILITAER:   { cls: 'akal-militaer', label: 'Militär/Schulung',       icon: 'W', count: true  },
    SCHULUNG:   { cls: 'akal-militaer', label: 'Militär/Schulung',       icon: 'S', count: true  },
    BEZ_ABSENZ: { cls: 'akal-militaer', label: 'Bezahlte Absenz',        icon: 'B', count: true  },
    NACHT_KOMP: { cls: 'akal-frei',     label: 'Kompensation/Feiertag',  icon: '·', count: false },
    FREI_KOMP:  { cls: 'akal-frei',     label: 'Kompensation/Feiertag',  icon: '·', count: false },
    FEIERTAG:   { cls: 'akal-frei',     label: 'Kompensation/Feiertag',  icon: '·', count: false },
};
// Engpass-Schwellen (v1 fix; später pro Filiale konfigurierbar).
const AKAL_WARN_MID = 3, AKAL_WARN_HIGH = 5;

function akalInit() {
    if (_akalStart === null) {
        const now = new Date();
        _akalStart = new Date(now.getFullYear(), now.getMonth(), 1);
    }
    akalLoad();
}

// ── Fenster schieben ────────────────────────────────────────────────────────
function akalShiftDays(n, debounce) {
    _akalStart = new Date(_akalStart.getFullYear(), _akalStart.getMonth(), _akalStart.getDate() + n);
    if (debounce) {
        akalRenderHeaderOnly();                       // Datum sofort mitlaufen lassen
        clearTimeout(_akalLoadTimer);
        _akalLoadTimer = setTimeout(akalLoad, 180);
    } else {
        akalLoad();
    }
}
function akalShiftMonth(n) {
    _akalStart = new Date(_akalStart.getFullYear(), _akalStart.getMonth() + n, _akalStart.getDate());
    akalLoad();
}
function akalToday() {
    const now = new Date();
    _akalStart = new Date(now.getFullYear(), now.getMonth(), 1);
    akalLoad();
}
function akalSetFilter(f) {
    _akalFilter = f;
    if (_akalData) akalRender(_akalData);
}

// ── Laden ───────────────────────────────────────────────────────────────────
async function akalLoad() {
    const box = document.getElementById('akalResult');
    if (!box) return;
    if (!fixedCompanyProfileId) {
        box.innerHTML = '<div style="padding:24px;color:#8b8b8b;font-size:13px">Bitte links in der Sidebar eine Filiale wählen — der Kalender zeigt immer eine Filiale.</div>';
        return;
    }
    if (!box.innerHTML) box.innerHTML = '<div style="padding:24px;color:#8b8b8b;font-size:13px">Lade Kalender…</div>';
    try {
        const from = _akalIso(_akalDate(0)), to = _akalIso(_akalDate(AKAL_WIN - 1));
        const res = await fetch(`/api/absences/kalender?companyProfileId=${fixedCompanyProfileId}&from=${from}&to=${to}`, { headers: ah() });
        if (!res.ok) {
            box.innerHTML = `<div style="padding:24px;color:#b91c1c;font-size:13px">Fehler beim Laden (${res.status}).</div>`;
            return;
        }
        _akalData = await res.json();
        akalRender(_akalData);
    } catch (e) {
        box.innerHTML = '<div style="padding:24px;color:#b91c1c;font-size:13px">Netzwerkfehler beim Laden.</div>';
    }
}

// ── Datums-Helfer (Fenster-Index i = 0..AKAL_WIN-1) ────────────────────────
function _akalDate(i)  { return new Date(_akalStart.getFullYear(), _akalStart.getMonth(), _akalStart.getDate() + i); }
function _akalIso(dt)  { return dt.getFullYear() + '-' + String(dt.getMonth() + 1).padStart(2, '0') + '-' + String(dt.getDate()).padStart(2, '0'); }
function _akalIdxOf(iso) {
    // Index des ISO-Datums im Fenster; davor → negativ geclampt, danach → gross.
    const d = new Date(+iso.slice(0, 4), +iso.slice(5, 7) - 1, +iso.slice(8, 10));
    return Math.round((d - _akalDate(0)) / 86400000);
}
function _akalFmt(iso)  { return iso.slice(8, 10) + '.' + iso.slice(5, 7) + '.' + iso.slice(0, 4); }
function _akalFmtDt(dt) { return String(dt.getDate()).padStart(2, '0') + '.' + String(dt.getMonth() + 1).padStart(2, '0') + '.' + dt.getFullYear(); }
function _akalIsToday(dt) {
    const now = new Date();
    return dt.getFullYear() === now.getFullYear() && dt.getMonth() === now.getMonth() && dt.getDate() === now.getDate();
}

function _akalMatchesFilter(a) {
    if (_akalFilter === 'ferien')      return a.type === 'FERIEN';
    if (_akalFilter === 'krankunfall') return a.type === 'KRANK' || a.type === 'UNFALL';
    return true;
}

function _akalModellLabel(m) {
    const model = modelDisplay(m.modell);
    if (model === 'MTP' && m.garantierteStunden) return `MTP ${m.garantierteStunden}h`;
    if ((model === 'FIX' || model === 'FIX-M') && m.pensum) return `${model} ${m.pensum}%`;
    return model;
}

function _akalSaldoLabel(m) {
    const model = modelDisplay(m.modell);
    if (model === 'FLEX') {
        if (m.ferienGeldSaldo == null) return '';
        return `FG CHF ${Number(m.ferienGeldSaldo).toFixed(0)}`;
    }
    if (m.ferienTageSaldo == null) return '';
    return `Ferien ${Number(m.ferienTageSaldo).toLocaleString('de-CH')} T`;
}

function _akalRangeLabel() {
    return `${_akalFmtDt(_akalDate(0))} – ${_akalFmtDt(_akalDate(AKAL_WIN - 1))}`;
}

function akalRenderHeaderOnly() {
    const el = document.getElementById('akalRange');
    if (el) el.textContent = _akalRangeLabel();
}

// ── Rendern ─────────────────────────────────────────────────────────────────
function akalRender(data) {
    const box = document.getElementById('akalResult');
    if (!box) return;

    let list = (data.mitarbeiter || []).slice();
    if (_akalFilter === 'mitabsenz') list = list.filter(m => (m.absenzen || []).length > 0);

    // Sortierung (Walter 22.07.2026): Gruppen FIX-M → FIX → MTP → FLEX,
    // innerhalb der Gruppe nach Vorname; kleine Lücke zwischen den Gruppen.
    const rank = m => ({ 'FIX-M': 0, 'FIX': 1, 'MTP': 2, 'FLEX': 3 })[modelDisplay(m.modell)] ?? 4;
    list.sort((a, b) => rank(a) - rank(b) || (a.name || '').localeCompare(b.name || ''));

    const fpill = (key, label) =>
        `<span class="akal-fpill ${_akalFilter === key ? 'on' : ''}" onclick="akalSetFilter('${key}')">${label}</span>`;
    const legende = ['akal-ferien|Ferien', 'akal-krank|Krankheit', 'akal-unfall|Unfall',
                     'akal-mutter|Mutterschaft', 'akal-militaer|Militär/Schulung', 'akal-frei|Kompensation/Frei']
        .map(x => { const [c, l] = x.split('|'); return `<span><i class="akal-dot ${c}"></i>${l}</span>`; }).join('');

    let h = `
    <div class="akal-toolbar">
        <div class="akal-monthnav">
            <button onclick="akalShiftMonth(-1)" title="1 Monat zurück">«</button>
            <button onclick="akalShiftDays(-7)" title="1 Woche zurück">‹</button>
            <span class="cur" id="akalRange">${_akalRangeLabel()}</span>
            <button onclick="akalShiftDays(7)" title="1 Woche vor">›</button>
            <button onclick="akalShiftMonth(1)" title="1 Monat vor">»</button>
        </div>
        <button class="akal-btn-heute" onclick="akalToday()">Heute</button>
        <span style="font-size:11.5px;color:#a8a29a">Tipp: mit dem Trackpad seitlich wischen</span>
        <span style="font-size:12px;color:#8b8b8b;margin-left:auto">${list.length} MA</span>
    </div>
    <div class="akal-filterrow">
        ${fpill('alle', 'Alle')}${fpill('ferien', 'Nur Ferien')}${fpill('krankunfall', 'Nur Krank/Unfall')}${fpill('mitabsenz', 'Nur mit Absenz')}
        <div class="akal-legend">${legende}</div>
    </div>`;

    // Kopfzeile: Tage des Fensters; Monatsanfang bekommt Trennkante + Monatskürzel.
    h += '<div class="akal-card" id="akalCard"><table class="akal-table"><thead><tr><th class="akal-namecol"></th>';
    for (let i = 0; i < AKAL_WIN; i++) {
        const dt = _akalDate(i);
        const g = dt.getDay(), we = (g === 0 || g === 6);
        const mstart = dt.getDate() === 1 && i > 0;
        const mon = (i === 0 || dt.getDate() === 1)
            ? `<div class="dmon">${dt.toLocaleDateString('de-CH', { month: 'short' })}</div>` : '<div class="dmon">&nbsp;</div>';
        h += `<th class="${we ? 'we' : ''} ${_akalIsToday(dt) ? 'today' : ''} ${mstart ? 'mstart' : ''}">` +
             `${mon}<div class="dnum">${dt.getDate()}</div><div class="dwd">${['So','Mo','Di','Mi','Do','Fr','Sa'][g]}</div></th>`;
    }
    h += '</tr></thead><tbody>';

    const perDay = Array(AKAL_WIN).fill(0);
    let prevRank = null;
    for (const m of list) {
        const r = rank(m);
        if (prevRank !== null && r !== prevRank)
            h += `<tr class="akal-gap"><td colspan="${AKAL_WIN + 1}"></td></tr>`;
        prevRank = r;
        const absList = (m.absenzen || []).filter(_akalMatchesFilter);
        const saldo = _akalSaldoLabel(m);
        h += `<tr><td class="akal-namecol" onclick="akalOpenMa(${m.id})" title="Zum Absenzen-Tab von ${esc(m.name)}">` +
             `${esc(m.name)}<span class="mod">${esc(_akalModellLabel(m))}</span>` +
             `${saldo ? `<span class="mod saldo">${esc(saldo)}</span>` : ''}</td>`;
        for (let i = 0; i < AKAL_WIN; i++) {
            const dt = _akalDate(i);
            const g = dt.getDay(), we = (g === 0 || g === 6);
            const mstart = dt.getDate() === 1 && i > 0;
            const a = absList.find(x => i >= _akalIdxOf(x.dateFrom) && i <= _akalIdxOf(x.dateTo));
            let seg = '';
            if (a) {
                const t = AKAL_TYPES[a.type] || { cls: 'akal-frei', label: a.type, icon: '·', count: true };
                if (t.count) perDay[i]++;
                const f = Math.max(_akalIdxOf(a.dateFrom), 0), tt = Math.min(_akalIdxOf(a.dateTo), AKAL_WIN - 1);
                const cls = ['akal-seg', t.cls];
                if (i === f && _akalIdxOf(a.dateFrom) >= 0) cls.push('start');
                if (i === tt && _akalIdxOf(a.dateTo) <= AKAL_WIN - 1) cls.push('end');
                if (a.prozent && a.prozent < 100) cls.push('halb');
                const mid = Math.floor((f + tt) / 2);
                const tip = `${t.label} ${_akalFmt(a.dateFrom)}–${_akalFmt(a.dateTo)}` +
                            (a.prozent && a.prozent < 100 ? ` (${a.prozent}%)` : '') +
                            (a.notes ? ` · ${a.notes}` : '');
                seg = `<div class="${cls.join(' ')}" onclick="akalOpenMa(${m.id})">${i === mid ? t.icon : ''}<span class="tip">${esc(tip)}</span></div>`;
            }
            h += `<td class="akal-day ${we ? 'we' : ''} ${_akalIsToday(dt) ? 'today' : ''} ${mstart ? 'mstart' : ''}">${seg}</td>`;
        }
        h += '</tr>';
    }
    if (!list.length)
        h += `<tr><td colspan="${AKAL_WIN + 1}" style="padding:20px;color:#8b8b8b;font-size:13px">Keine Mitarbeitenden für diesen Filter.</td></tr>`;

    // Summenzeile «Abwesend»
    h += '</tbody><tfoot><tr><td class="akal-namecol">Abwesend</td>';
    for (let i = 0; i < AKAL_WIN; i++) {
        const dt = _akalDate(i);
        const g = dt.getDay(), we = (g === 0 || g === 6);
        const c = perDay[i];
        const cls = c >= AKAL_WARN_HIGH ? 'warn' : (c >= AKAL_WARN_MID ? 'mid' : '');
        h += `<td class="akal-day ${we ? 'we' : ''}"><div class="akal-cnt ${cls}">${c || ''}</div></td>`;
    }
    h += '</tr></tfoot></table></div>';

    // Engpass-Hinweis
    const total = (data.mitarbeiter || []).length;
    const engpass = [];
    for (let i = 0; i < AKAL_WIN; i++)
        if (perDay[i] >= AKAL_WARN_HIGH) {
            const dt = _akalDate(i);
            engpass.push(`${['So','Mo','Di','Mi','Do','Fr','Sa'][dt.getDay()]} ${String(dt.getDate()).padStart(2, '0')}.${String(dt.getMonth() + 1).padStart(2, '0')}.`);
        }
    if (engpass.length)
        h += `<div class="akal-footnote">⚠ <span><b>Engpass:</b> an ${engpass.length} Tag(en) sind ${AKAL_WARN_HIGH} oder mehr von ${total} MA abwesend — ${engpass.join(', ')}</span></div>`;

    box.innerHTML = h;

    // Trackpad-Wisch: horizontales Scrollen schiebt das Fenster tageweise.
    const card = document.getElementById('akalCard');
    if (card) card.addEventListener('wheel', ev => {
        if (Math.abs(ev.deltaX) <= Math.abs(ev.deltaY)) return;   // vertikal normal scrollen
        ev.preventDefault();
        _akalWheelAcc += ev.deltaX;
        const step = 40;                                          // px pro Tag
        const days = Math.trunc(_akalWheelAcc / step);
        if (days !== 0) {
            _akalWheelAcc -= days * step;
            akalShiftDays(days, true);
        }
    }, { passive: false });
}

// Sprung in den Absenzen-Tab des MA (Muster dashOpenEmployee, dashboard.js).
function akalOpenMa(employeeId) {
    if (!employeeId) return;
    window.activeEmpId = employeeId;
    try { activeEmpTab = 'absenzen'; } catch (_) {}
    showPage('mitarbeiter');
    setTimeout(() => {
        const alreadySel = (typeof selectedEmployeeId !== 'undefined' && selectedEmployeeId === employeeId);
        if (!alreadySel && typeof selectEmployee === 'function') selectEmployee(employeeId);
        else if (typeof switchEmpTab === 'function') switchEmpTab('absenzen');
    }, 60);
}
