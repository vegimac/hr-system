// ═══════════════════════════════════════════════════════════════════════════
//  ABSENZ- & FERIENKALENDER pro Filiale (Walter-Vorgabe 22.07.2026)
//  Monatsraster: Zeilen = aktive MA der Filiale (nach Vorname), Spalten =
//  Kalendertage. Absenzen als farbige Balken, unten Summenzeile «Abwesend»
//  pro Tag + Engpass-Hinweis. Filiale = globaler Sidebar-Selektor.
//  Endpoint: GET /api/absences/kalender?companyProfileId&year&month
//  Einstieg: Kachel im Restaurant-Admin-Tab (img/absenzkalender.svg) +
//  showPage('absenz-kalender').
// ═══════════════════════════════════════════════════════════════════════════

let _akalYear = null, _akalMonth = null;   // aktuell angezeigter Monat
let _akalFilter = 'alle';                  // alle | ferien | krankunfall | mitabsenz
let _akalData = null;                      // letzter Server-Response

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
    if (_akalYear === null) {
        const now = new Date();
        _akalYear = now.getFullYear();
        _akalMonth = now.getMonth() + 1;
    }
    akalLoad();
}

function akalShiftMonth(delta) {
    let m = _akalMonth + delta, y = _akalYear;
    if (m < 1)  { m = 12; y--; }
    if (m > 12) { m = 1;  y++; }
    _akalYear = y; _akalMonth = m;
    akalLoad();
}

function akalToday() {
    const now = new Date();
    _akalYear = now.getFullYear();
    _akalMonth = now.getMonth() + 1;
    akalLoad();
}

function akalSetFilter(f) {
    _akalFilter = f;
    if (_akalData) akalRender(_akalData);
}

async function akalLoad() {
    const box = document.getElementById('akalResult');
    if (!box) return;
    if (!fixedCompanyProfileId) {
        box.innerHTML = '<div style="padding:24px;color:#8b8b8b;font-size:13px">Bitte links in der Sidebar eine Filiale wählen — der Kalender zeigt immer eine Filiale.</div>';
        return;
    }
    box.innerHTML = '<div style="padding:24px;color:#8b8b8b;font-size:13px">Lade Kalender…</div>';
    try {
        const res = await fetch(`/api/absences/kalender?companyProfileId=${fixedCompanyProfileId}&year=${_akalYear}&month=${_akalMonth}`, { headers: ah() });
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

// ── Helfer ──────────────────────────────────────────────────────────────────
function _akalDays()   { return new Date(_akalYear, _akalMonth, 0).getDate(); }
function _akalWd(d)    { return ['So','Mo','Di','Mi','Do','Fr','Sa'][new Date(_akalYear, _akalMonth - 1, d).getDay()]; }
function _akalIsWe(d)  { const g = new Date(_akalYear, _akalMonth - 1, d).getDay(); return g === 0 || g === 6; }
function _akalIsToday(d) {
    const now = new Date();
    return now.getFullYear() === _akalYear && now.getMonth() + 1 === _akalMonth && now.getDate() === d;
}
function _akalDayOf(iso) {
    // Tag im aktuellen Monat; ausserhalb → geclampt (Balken läuft an den Rand).
    const y = +iso.slice(0, 4), m = +iso.slice(5, 7), d = +iso.slice(8, 10);
    if (y < _akalYear || (y === _akalYear && m < _akalMonth)) return 0;          // vor Monat
    if (y > _akalYear || (y === _akalYear && m > _akalMonth)) return 99;         // nach Monat
    return d;
}
function _akalFmt(iso) { return iso.slice(8, 10) + '.' + iso.slice(5, 7) + '.' + iso.slice(0, 4); }

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
        return `Feriengeld: CHF ${Number(m.ferienGeldSaldo).toFixed(2)}`;
    }
    if (m.ferienTageSaldo == null) return '';
    return `Ferien: ${Number(m.ferienTageSaldo).toLocaleString('de-CH')} Tage`;
}

// ── Rendern ─────────────────────────────────────────────────────────────────
function akalRender(data) {
    const box = document.getElementById('akalResult');
    if (!box) return;
    const days = _akalDays();
    const monat = new Date(_akalYear, _akalMonth - 1, 1).toLocaleDateString('de-CH', { month: 'long', year: 'numeric' });

    let list = data.mitarbeiter || [];
    if (_akalFilter === 'mitabsenz') list = list.filter(m => (m.absenzen || []).length > 0);

    // Kopf: Monat + Filter + Legende
    const fpill = (key, label) =>
        `<span class="akal-fpill ${_akalFilter === key ? 'on' : ''}" onclick="akalSetFilter('${key}')">${label}</span>`;
    const legende = ['akal-ferien|Ferien', 'akal-krank|Krankheit', 'akal-unfall|Unfall',
                     'akal-mutter|Mutterschaft', 'akal-militaer|Militär/Schulung', 'akal-frei|Kompensation/Frei']
        .map(x => { const [c, l] = x.split('|'); return `<span><i class="akal-dot ${c}"></i>${l}</span>`; }).join('');

    let h = `
    <div class="akal-toolbar">
        <div class="akal-monthnav">
            <button onclick="akalShiftMonth(-1)" title="Vormonat">‹</button>
            <span class="cur">${monat}</span>
            <button onclick="akalShiftMonth(1)" title="Folgemonat">›</button>
        </div>
        <button class="akal-btn-heute" onclick="akalToday()">Heute</button>
        <span style="font-size:12px;color:#8b8b8b">${list.length} MA</span>
    </div>
    <div class="akal-filterrow">
        ${fpill('alle', 'Alle')}${fpill('ferien', 'Nur Ferien')}${fpill('krankunfall', 'Nur Krank/Unfall')}${fpill('mitabsenz', 'Nur mit Absenz')}
        <div class="akal-legend">${legende}</div>
    </div>`;

    // Tabelle
    h += '<div class="akal-card"><table class="akal-table"><thead><tr><th class="akal-namecol"></th>';
    for (let d = 1; d <= days; d++)
        h += `<th class="${_akalIsWe(d) ? 'we' : ''} ${_akalIsToday(d) ? 'today' : ''}"><div class="dnum">${d}</div><div class="dwd">${_akalWd(d)}</div></th>`;
    h += '</tr></thead><tbody>';

    const perDay = Array(days + 1).fill(0);
    for (const m of list) {
        const absList = (m.absenzen || []).filter(_akalMatchesFilter);
        const saldo = _akalSaldoLabel(m);
        h += `<tr><td class="akal-namecol" onclick="akalOpenMa(${m.id})" title="Zum Absenzen-Tab von ${esc(m.name)}">` +
             `${esc(m.name)}<span class="mod">${esc(_akalModellLabel(m))}</span>` +
             `${saldo ? `<span class="saldo">${esc(saldo)}</span>` : ''}</td>`;
        for (let d = 1; d <= days; d++) {
            const a = absList.find(x => d >= _akalDayOf(x.dateFrom) && d <= _akalDayOf(x.dateTo));
            let seg = '';
            if (a) {
                const t = AKAL_TYPES[a.type] || { cls: 'akal-frei', label: a.type, icon: '·', count: true };
                if (t.count) perDay[d]++;
                const f = Math.max(_akalDayOf(a.dateFrom), 1), tt = Math.min(_akalDayOf(a.dateTo), days);
                const cls = ['akal-seg', t.cls];
                if (d === f) cls.push('start');
                if (d === tt) cls.push('end');
                if (a.prozent && a.prozent < 100) cls.push('halb');
                const mid = Math.floor((f + tt) / 2);
                const tip = `${t.label} ${_akalFmt(a.dateFrom)}–${_akalFmt(a.dateTo)}` +
                            (a.prozent && a.prozent < 100 ? ` (${a.prozent}%)` : '') +
                            (a.notes ? ` · ${a.notes}` : '');
                seg = `<div class="${cls.join(' ')}" onclick="akalOpenMa(${m.id})">${d === mid ? t.icon : ''}<span class="tip">${esc(tip)}</span></div>`;
            }
            h += `<td class="akal-day ${_akalIsWe(d) ? 'we' : ''} ${_akalIsToday(d) ? 'today' : ''}">${seg}</td>`;
        }
        h += '</tr>';
    }
    if (!list.length)
        h += `<tr><td colspan="${days + 1}" style="padding:20px;color:#8b8b8b;font-size:13px">Keine Mitarbeitenden für diesen Filter.</td></tr>`;

    // Summenzeile «Abwesend»
    h += '</tbody><tfoot><tr><td class="akal-namecol">Abwesend</td>';
    for (let d = 1; d <= days; d++) {
        const c = perDay[d];
        const cls = c >= AKAL_WARN_HIGH ? 'warn' : (c >= AKAL_WARN_MID ? 'mid' : '');
        h += `<td class="akal-day ${_akalIsWe(d) ? 'we' : ''}"><div class="akal-cnt ${cls}">${c || ''}</div></td>`;
    }
    h += '</tr></tfoot></table></div>';

    // Engpass-Hinweis
    const total = (data.mitarbeiter || []).length;
    const engpass = [];
    for (let d = 1; d <= days; d++)
        if (perDay[d] >= AKAL_WARN_HIGH) engpass.push(`${_akalWd(d)} ${String(d).padStart(2, '0')}.${String(_akalMonth).padStart(2, '0')}.`);
    if (engpass.length)
        h += `<div class="akal-footnote">⚠ <span><b>Engpass:</b> an ${engpass.length} Tag(en) sind ${AKAL_WARN_HIGH} oder mehr von ${total} MA abwesend — ${engpass.join(', ')}</span></div>`;

    box.innerHTML = h;
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
