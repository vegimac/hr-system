// ══════════════════════════════════════════════
// TAB: Stempelzeiten – Einträge pro Mitarbeiter und Monat
// ══════════════════════════════════════════════
const MONATSNAMEN_DE = ['Januar','Februar','März','April','Mai','Juni',
                        'Juli','August','September','Oktober','November','Dezember'];

async function loadStempelzeitenTab(employeeId) {
    const el = document.getElementById('stempelzeitenContent');
    if (!el) return;
    if (!employeeId) {
        el.innerHTML = `<div class="emp-placeholder">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
            <span>Bitte wähle einen Mitarbeiter</span>
        </div>`;
        return;
    }

    // Walter-Vorgabe 21.06.2026: Stempelzeiten IMMER nach KALENDERMONAT
    // (nicht mehr nach Lohnperiode). Links Jahr (ab 2025), rechts Monat (1–12);
    // Vorgabe = aktueller Monat. Auswahl persistiert über MA-Wechsel via
    // _stempelGlobalYear/_stempelGlobalMonth.
    el._stempelPerioden  = [];
    el._stempelPeriodeId = null;

    if (!el._stempelYear || !el._stempelMonth) {
        if (typeof _stempelGlobalYear !== 'undefined' && _stempelGlobalYear) {
            el._stempelYear  = _stempelGlobalYear;
            el._stempelMonth = _stempelGlobalMonth;
        } else {
            const now = new Date();
            el._stempelYear  = now.getFullYear();
            el._stempelMonth = now.getMonth() + 1;
        }
    }

    const curY = new Date().getFullYear();
    const jahre = [];
    for (let y = 2025; y <= curY + 1; y++) jahre.push(y);
    const yearOpts = jahre.map(y => `
        <option value="${y}" ${y === el._stempelYear ? 'selected' : ''}>${y}</option>`).join('');
    const monthOpts = MONATSNAMEN_DE.map((n, i) => `
        <option value="${i+1}" ${i+1 === el._stempelMonth ? 'selected' : ''}>${n}</option>`).join('');
    const filterHtml = `
        <div class="stempel-period-field">
            <span class="stempel-period-label">Jahr</span>
            <select id="stempelYearSel" class="f-input stempel-period-sel stempel-period-year" onchange="stempelChangePeriod()">${yearOpts}</select>
        </div>
        <div class="stempel-period-field">
            <span class="stempel-period-label">Monat</span>
            <select id="stempelMonthSel" class="f-input stempel-period-sel stempel-period-month" onchange="stempelChangePeriod()">${monthOpts}</select>
        </div>`;

    // Walter 19.07.2026 (final): Filter + Spaltenköpfe AUSSERHALB des Scrolls
    // (kein sticky mehr) — sonst hüpfen sie am Listenanfang/-ende.
    // Look: Liquid Glass / Kohle — Klassen in app.css.
    el.innerHTML = `
        <div class="stempel-tab-wrap">
            <div id="stempelFilterRow" class="stempel-filter-row">
                ${filterHtml}
                <div id="stempelCount" class="stempel-count"></div>
            </div>
            <div class="stempel-table-card">
                <div id="stempelCols" class="stempel-cols"></div>
                <div id="stempelListe" class="stempel-liste">
                    <div class="stempel-loading">Lade…</div>
                </div>
            </div>
        </div>`;
    stempelBindScrollIsolation();

    await stempelLadeEintraege(employeeId);
}

/** Wheel-Isolation am Listenrand — Filter/Titel springen nicht (Walter 19.07.2026). */
function stempelBindScrollIsolation() {
    const wrap = document.querySelector('#stempelzeitenContent .stempel-tab-wrap');
    const sc = document.getElementById('stempelListe');
    if (!wrap || !sc || sc.dataset.scrollLock === '1') return;
    sc.dataset.scrollLock = '1';

    wrap.addEventListener('wheel', (e) => {
        e.stopPropagation();
        const maxScroll = sc.scrollHeight - sc.clientHeight;
        if (maxScroll <= 0) {
            e.preventDefault();
            return;
        }
        const atTop = sc.scrollTop <= 0;
        const atBottom = sc.scrollTop >= maxScroll - 1;
        if ((atTop && e.deltaY < 0) || (atBottom && e.deltaY > 0)) {
            e.preventDefault();
            return;
        }
        if (!sc.contains(e.target)) {
            sc.scrollTop = Math.min(maxScroll, Math.max(0, sc.scrollTop + e.deltaY));
            e.preventDefault();
        }
    }, { passive: false });
}

/** @deprecated Sticky-Offset entfällt — Spaltenköpfe liegen ausserhalb des Scrolls. */
function stempelUpdateFilterStickyOffset() { /* no-op, Alt-Aufrufer */ }

function stempelFmtDateShort(iso) {
    if (!iso) return '';
    const m = /(\d{4})-(\d{2})-(\d{2})/.exec(iso);
    return m ? `${m[3]}.${m[2]}.${m[1].slice(2)}` : '';
}

function stempelChangePeriod() {
    const el = document.getElementById('stempelzeitenContent');
    if (!el || !selectedEmployeeId) return;
    const perSel = document.getElementById('stempelPeriodeSel');
    if (perSel) {
        el._stempelPeriodeId = parseInt(perSel.value, 10);
        // Auswahl global persistieren — bleibt beim MA-Wechsel erhalten
        _stempelGlobalPeriodeId = el._stempelPeriodeId;
    } else {
        // Kalendermonat-Fallback
        el._stempelYear  = parseInt(document.getElementById('stempelYearSel').value, 10);
        el._stempelMonth = parseInt(document.getElementById('stempelMonthSel').value, 10);
        _stempelGlobalYear  = el._stempelYear;
        _stempelGlobalMonth = el._stempelMonth;
    }
    stempelLadeEintraege(selectedEmployeeId);
}

// Helfer: DB speichert Stempelzeiten als `timestamp without time zone` (Lokalzeit,
// keine TZ-Konvertierung). Wir parsen die Strings direkt per Regex, nicht via
// Date-Objekt — das vermeidet jede Browser-TZ-Interpretation.
const stempelPad2    = (n) => String(n).padStart(2, '0');
const stempelFmtTime = (iso) => {
    if (iso == null || iso === '') return '';
    const s = String(iso);
    // ISO: 2026-07-12T16:05:00(.fff)(Z|+02:00)
    let m = /T(\d{2}):(\d{2})/.exec(s);
    if (m) return `${m[1]}:${m[2]}`;
    // Space-Variante: 2026-07-12 16:05:00
    m = /^\d{4}-\d{2}-\d{2}[ ](\d{2}):(\d{2})/.exec(s);
    if (m) return `${m[1]}:${m[2]}`;
    // Nur Zeit: 16:05[:00]
    m = /^(\d{1,2}):(\d{2})(?::\d{2})?$/.exec(s.trim());
    if (m) return `${stempelPad2(m[1])}:${m[2]}`;
    return '';
};
// Original-Zeiten aus easy@work-Audit-Text (wie Backend ParseEditedTimesFromTexts).
// «Aus vom 2.7.2026, 13:37 bis zum … geändert» → 13:37 (auch mit Icon-Präfix).
const stempelParseOrigFromText = (text) => {
    if (!text) return { in: '', out: '' };
    const inM  = /Ein\s+vom\s+.+?(\d{1,2}):(\d{2})\s+bis\s+zum/i.exec(text);
    const outM = /Aus\s+vom\s+.+?(\d{1,2}):(\d{2})\s+bis\s+zum/i.exec(text);
    const fmt = (m) => m ? `${stempelPad2(m[1])}:${m[2]}` : '';
    return { in: fmt(inM), out: fmt(outM) };
};
const stempelOriginalTimes = (r) => {
    let tin  = stempelFmtTime(r.originalTimeIn  ?? r.OriginalTimeIn);
    let tout = stempelFmtTime(r.originalTimeOut ?? r.OriginalTimeOut);
    if (!tin || !tout) {
        const fromText = stempelParseOrigFromText(
            [r.comment, r.originalComment, r.OriginalComment].filter(Boolean).join('\n'));
        if (!tin)  tin  = fromText.in;
        if (!tout) tout = fromText.out;
    }
    return { in: tin, out: tout };
};

/** Kurzer Korrekturtext für die Stempel-Tabelle (Walter 08.10.2026). */
function stempelKorrekturKurz(r) {
    const jetztIn  = stempelFmtTime(r.timeIn);
    const jetztAus = stempelFmtTime(r.timeOut);
    const orig = stempelOriginalTimes(r);
    const teile = [];
    const einGleich = !orig.in || !jetztIn || orig.in === jetztIn;
    const ausGleich = (!orig.out && !jetztAus) || (orig.out && jetztAus && orig.out === jetztAus);
    if (!orig.out && jetztAus) teile.push(`offen → Aus ${jetztAus}`);
    else if (orig.out && !jetztAus) teile.push(`Aus ${orig.out} → offen`);
    else if (!einGleich && !ausGleich)
        teile.push(`${orig.in || '–'}–${orig.out || '–'} → ${jetztIn || '–'}–${jetztAus || '–'}`);
    else if (!ausGleich && orig.out && jetztAus) teile.push(`Aus ${orig.out} → ${jetztAus}`);
    else if (!einGleich && orig.in && jetztIn) teile.push(`Ein ${orig.in} → ${jetztIn}`);
    else if (jetztIn || jetztAus) teile.push(`korrigiert ${[jetztIn, jetztAus].filter(Boolean).join('–')}`);
    else teile.push('korrigiert');
    if (r.editedAt) {
        const d = new Date(r.editedAt);
        if (!isNaN(d)) teile.push(d.toLocaleDateString('de-CH'));
    }
    if (r.editedBy) teile.push(`von ${r.editedBy}`);
    const kommentar = (r.comment || '').trim();
    if (kommentar) teile.push(`«${kommentar}»`);
    return teile.join(' · ');
}
// Verlauf eines Stempels live aus easy@work (Kommentare + Änderungsprotokoll).
// Felder der Einträge sind nicht dokumentiert → generisch anzeigen, Rohdaten aufklappbar.
async function stempelEasyVerlauf(entryId) {
    const old = document.getElementById('stzVerlaufModal');
    if (old) old.remove();
    const wrap = document.createElement('div');
    wrap.id = 'stzVerlaufModal';
    wrap.style.cssText = 'position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9800;display:flex;align-items:center;justify-content:center';
    wrap.innerHTML = `<div class="modal" style="max-width:640px;width:94%;max-height:86vh;overflow:auto;padding:22px 24px;border-radius:16px">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px">
            <div style="font-size:15px;font-weight:800;color:#3f3f3f">Verlauf aus easy@work</div>
            <button type="button" id="stzVerlaufZu" style="background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.35);border-radius:10px;padding:5px 12px;cursor:pointer">✕</button>
        </div>
        <div id="stzVerlaufInhalt" style="font-size:13px;color:#646464">Wird geladen…</div>
    </div>`;
    document.body.appendChild(wrap);
    const zu = () => { wrap.remove(); document.removeEventListener('keydown', onKey); };
    const onKey = e => { if (e.key === 'Escape') zu(); };
    document.addEventListener('keydown', onKey);
    wrap.addEventListener('click', e => { if (e.target === wrap) zu(); });
    wrap.querySelector('#stzVerlaufZu').onclick = zu;
    const ziel = wrap.querySelector('#stzVerlaufInhalt');

    // easy@work liefert Stempel-/Protokoll-Zeiten als UTC ohne Suffix → +Z, Anzeige Zürich.
    const zeit = (v) => {
        if (!v || typeof v !== 'string') return '';
        const iso = /[zZ]|[+-]\d{2}:?\d{2}$/.test(v) ? v : v.replace(' ', 'T') + 'Z';
        const d = new Date(iso);
        return isNaN(d) ? v : d.toLocaleString('de-CH', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
    };
    const uhrUtc = (v) => {
        if (!v || typeof v !== 'string') return '';
        const iso = /[zZ]|[+-]\d{2}:?\d{2}$/.test(v) ? v : v.replace(' ', 'T') + 'Z';
        const d = new Date(iso);
        return isNaN(d) ? '' : d.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' });
    };
    // Datum-Felder (business_date) nicht umrechnen — nur Kalendertag.
    const wertAnzeigen = (k, v) => {
        if (typeof v === 'boolean') return v ? 'ja' : 'nein';
        if (typeof v === 'string' && /^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}/.test(v)) {
            if (k === 'business_date' || /_date$/.test(k)) {
                const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(v);
                return m ? `${m[3]}.${m[2]}.${m[1]}` : v;
            }
            return zeit(v);
        }
        return String(v);
    };
    const TEXT = ['text', 'comment', 'body', 'message', 'content', 'description'];
    const WER = ['created_by_name', 'user_name', 'author_name', 'causer_name'];
    const SCHWARZ = '#1a1a1a', ROT = '#b91c1c';
    const gleichWert = (a, b) => (a == null && b == null) || String(a ?? '') === String(b ?? '');
    const stempelLabel = (e, farbe, geaendert) => {
        const einF = geaendert?.in ? ROT : farbe;
        const ausF = geaendert?.out ? ROT : farbe;
        let hint = '';
        if (e.manually_closed === true) hint = geaendert?.manually_closed
            ? `<span style="color:${ROT}"> · von Hand geschlossen</span>`
            : ' · von Hand geschlossen';
        else if (e.manually_closed === false && !e.out) hint = ' · ohne Aus (später Tagesabschluss / offen)';
        if (e.manually_opened === true) hint += geaendert?.manually_opened
            ? `<span style="color:${ROT}"> · von Hand geöffnet</span>` : ' · von Hand geöffnet';
        return `<div style="margin-top:2px;font-weight:700;color:${farbe}">`
            + `Ein <span style="color:${einF}">${esc(uhrUtc(e.in) || '–')}</span>`
            + ` · Aus <span style="color:${ausF}">${esc(uhrUtc(e.out) || '–')}</span>${hint}</div>`;
    };
    const eintrag = (art, e, vorher) => {
        if (e == null) return '';
        if (typeof e !== 'object') return `<div class="stz-vl-row"><b>${art}</b> · ${esc(String(e))}</div>`;
        const wer = WER.map(k => e[k]).find(v => typeof v === 'string' && v.trim())
            || (e.user && typeof e.user === 'object' ? (e.user.name || [e.user.firstname, e.user.lastname].filter(Boolean).join(' ')) : (typeof e.user === 'string' ? e.user : ''))
            || (typeof e.created_by === 'string' ? e.created_by : '');
        const text = TEXT.map(k => e[k]).find(v => typeof v === 'string' && v.trim());
        const istKorrektur = !!vorher && (e.in != null || e.out != null || vorher.in != null || vorher.out != null);
        const geaendert = istKorrektur ? {
            in: !gleichWert(e.in, vorher.in),
            out: !gleichWert(e.out, vorher.out),
            manually_closed: !gleichWert(e.manually_closed, vorher.manually_closed),
            manually_opened: !gleichWert(e.manually_opened, vorher.manually_opened),
        } : null;
        const farbe = istKorrektur ? ROT : SCHWARZ;
        const titel = istKorrektur ? 'Korrektur' : (art === 'Protokoll' ? 'Stempelung' : art);
        const hatStempel = e.in != null || e.out != null || (vorher && (vorher.in != null || vorher.out != null));
        const restKeys = ['approved', 'time_edited', 'business_date', 'value', 'length'];
        const rest = Object.entries(e)
            .filter(([k, v]) => restKeys.includes(k) && v != null && v !== '' && typeof v !== 'object')
            .map(([k, v]) => {
                const neu = istKorrektur && vorher && !gleichWert(v, vorher[k]);
                return `<span style="${neu ? `color:${ROT};font-weight:700` : ''}">${esc(k)}: ${esc(wertAnzeigen(k, v))}</span>`;
            }).join(' · ');
        return `<div style="padding:8px 0;border-bottom:1px solid rgba(60,55,48,0.10)">
            <div style="font-size:12px;color:#8b8b8b"><b style="color:${farbe}">${titel}</b>${e.created_at ? ' · ' + esc(zeit(e.created_at)) : ''}${wer ? ' · ' + esc(wer) : ''}</div>
            ${hatStempel ? stempelLabel(e, farbe, geaendert) : ''}
            ${text ? `<div style="color:${farbe};margin-top:2px">${esc(text)}</div>` : ''}
            ${rest ? `<div style="font-size:12px;color:#8b8b8b;margin-top:2px">${rest}</div>` : ''}
        </div>`;
    };
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/timeentries/${entryId}/easy-verlauf`, { headers: ah() });
        const j = res.ok ? await res.json() : null;
        const preStil = 'white-space:pre-wrap;font-size:11px;background:rgba(255,255,255,0.6);border-radius:8px;padding:8px;max-height:300px;overflow:auto';
        const versucheHtml = (j?.versuche || []).map(v =>
            `<div style="font-size:12px;margin-top:4px"><b>${esc(v.abfrage)}</b> → HTTP ${v.status}${v.meldung ? `<pre style="${preStil};margin:3px 0 0">${esc(v.meldung)}</pre>` : ''}</div>`).join('');
        if (!j || !j.verfuegbar) {
            ziel.innerHTML = `<div>${esc(j?.grund || `Fehler ${res.status}`)}</div>${versucheHtml}`;
            return;
        }
        const liste = (v) => Array.isArray(v) ? v : (v && Array.isArray(v.data) ? v.data : []);
        const kommentare = liste(j.comments);
        const protokoll = liste(j.changelog);
        // Protokoll chronologisch: erste Stempelung schwarz, spätere Korrektur rot (geänderte Felder).
        const protoSortiert = [...protokoll].sort((a, b) => {
            const ta = Date.parse((a.created_at || a.updated_at || '').replace(' ', 'T') + 'Z') || 0;
            const tb = Date.parse((b.created_at || b.updated_at || '').replace(' ', 'T') + 'Z') || 0;
            if (ta !== tb) return ta - tb;
            return (a.id || 0) - (b.id || 0);
        });
        const zeilen = [
            ...kommentare.map(e => eintrag('Kommentar', e, null)),
            ...protoSortiert.map((e, i) => eintrag('Protokoll', e, i > 0 ? protoSortiert[i - 1] : null)),
        ].join('');
        const gesp = j.gespeichert || {};
        const z = j.zeiten || {};
        // OneCrew-Zeiten sind Lokalzeit (timestamp without time zone) — kein +Z.
        const uhrLokal = (v) => {
            if (!v) return '';
            const s = typeof v === 'string' ? v : String(v);
            const d = new Date(/[zZ]|[+-]\d{2}:?\d{2}$/.test(s) ? s : s.replace(' ', 'T'));
            return isNaN(d) ? '' : d.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' });
        };
        const ein = uhrLokal(z.ein), aus = uhrLokal(z.aus), oEin = uhrLokal(z.originalEin), oAus = uhrLokal(z.originalAus);
        const hatAenderung = (oEin && oEin !== ein) || (oAus && oAus !== aus) || protoSortiert.length > 1;
        const zeitVergleich = (ein || aus) ? `<div style="display:flex;flex-wrap:wrap;gap:10px;margin:8px 0 12px">
            <div style="flex:1;min-width:140px;background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.12);border-radius:12px;padding:10px 12px">
                <div style="font-size:11px;font-weight:700;color:#8b8b8b;letter-spacing:.04em;text-transform:uppercase;margin-bottom:4px">Ein</div>
                ${oEin && oEin !== ein
                    ? `<div><span style="color:${SCHWARZ};font-weight:700;text-decoration:line-through">${esc(oEin)}</span>
                         <span style="margin:0 6px;color:#8b8b8b">→</span>
                         <span style="color:${ROT};font-weight:800;font-size:16px">${esc(ein || '–')}</span></div>`
                    : `<div style="font-weight:800;font-size:16px;color:${SCHWARZ}">${esc(ein || '–')}</div>`}
            </div>
            <div style="flex:1;min-width:140px;background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.12);border-radius:12px;padding:10px 12px">
                <div style="font-size:11px;font-weight:700;color:#8b8b8b;letter-spacing:.04em;text-transform:uppercase;margin-bottom:4px">Aus</div>
                ${oAus && oAus !== aus
                    ? `<div><span style="color:${SCHWARZ};font-weight:700;text-decoration:line-through">${esc(oAus || '–')}</span>
                         <span style="margin:0 6px;color:#8b8b8b">→</span>
                         <span style="color:${ROT};font-weight:800;font-size:16px">${esc(aus || '–')}</span></div>`
                    : (protoSortiert.length > 1 && protoSortiert[0].out == null && aus
                        ? `<div><span style="color:${SCHWARZ};font-weight:700">–</span>
                             <span style="margin:0 6px;color:#8b8b8b">→</span>
                             <span style="color:${ROT};font-weight:800;font-size:16px">${esc(aus)}</span></div>`
                        : `<div style="font-weight:800;font-size:16px;color:${SCHWARZ}">${esc(aus || '–')}</div>`)}
            </div>
            ${hatAenderung ? `<div style="flex-basis:100%;font-size:11.5px;color:#8b8b8b"><span style="color:${SCHWARZ};font-weight:700">schwarz</span> = erste / falsche Stempelung · <span style="color:${ROT};font-weight:700">rot</span> = Korrektur</div>` : ''}
        </div>` : '';
        ziel.innerHTML = `
            <div style="font-size:12px;color:#8b8b8b;margin-bottom:6px">Erstellt ${esc(zeit(j.createdAt) || '–')} · zuletzt geändert ${esc(zeit(j.updatedAt) || '–')} · Zeiten Zürich</div>
            ${zeitVergleich}
            ${zeilen || '<div>easy@work liefert zu diesem Stempel keine Kommentare.</div>'}
            <div style="font-size:12px;color:#8b8b8b;margin-top:10px">In OneCrew gespeichert: Kommentar «${esc(gesp.kommentar || '–')}» · Protokoll «${esc(gesp.protokoll || '–')}»</div>
            <details style="margin-top:12px"><summary style="cursor:pointer;font-size:12px;color:#8b8b8b">Rohdaten easy@work (UTC, unverändert)</summary>
                ${versucheHtml}
                <pre style="${preStil};margin-top:6px">${esc(JSON.stringify(j.roh, null, 2))}</pre>
                ${j.rohKommentare && JSON.stringify(j.rohKommentare) !== JSON.stringify(j.roh)
                    ? `<pre style="${preStil};margin-top:6px">${esc(JSON.stringify(j.rohKommentare, null, 2))}</pre>` : ''}
            </details>`;
    } catch (e) {
        ziel.textContent = 'Fehler: ' + e.message;
    }
}

const stempelFmtDate = (iso) => {
    if (!iso) return '';
    const m = /(\d{4})-(\d{2})-(\d{2})/.exec(iso);
    if (!m) return '';
    // Wochentag via UTC-Date (reines Datum, keine Zeit-Komponente)
    const d = new Date(Date.UTC(+m[1], +m[2]-1, +m[3]));
    const wd = ['So.','Mo.','Di.','Mi.','Do.','Fr.','Sa.'][d.getUTCDay()];
    return `${wd}, ${m[3]}.${m[2]}.${m[1].slice(2)}`;
};
const stempelFmtHours = (h) => Number(h || 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// ── ISO-Woche (Mo–So) ───────────────────────────────────────────────────────
// Montag der Woche als stabiler Gruppen-Schlüssel ("yyyy-mm-dd").
const stempelWeekMonday = (iso) => {
    const m = /(\d{4})-(\d{2})-(\d{2})/.exec(iso || '');
    if (!m) return null;
    const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3]));
    const dow = d.getUTCDay();                 // 0=So … 6=Sa
    d.setUTCDate(d.getUTCDate() + (dow === 0 ? -6 : 1 - dow)); // → Montag
    return d.toISOString().slice(0, 10);
};
// Sonntag der Woche (Mo–So) als "yyyy-mm-dd".
const stempelWeekSunday = (iso) => {
    const mon = stempelWeekMonday(iso);
    if (!mon) return null;
    const m = /(\d{4})-(\d{2})-(\d{2})/.exec(mon);
    const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3]));
    d.setUTCDate(d.getUTCDate() + 6);
    return d.toISOString().slice(0, 10);
};
// ISO-Kalenderwoche (1–53).
const stempelIsoWeek = (iso) => {
    const m = /(\d{4})-(\d{2})-(\d{2})/.exec(iso || '');
    if (!m) return null;
    const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3]));
    const day = (d.getUTCDay() + 6) % 7;       // Mo=0 … So=6
    d.setUTCDate(d.getUTCDate() - day + 3);     // Donnerstag dieser Woche
    const firstThu = new Date(Date.UTC(d.getUTCFullYear(), 0, 4));
    const firstDay = (firstThu.getUTCDay() + 6) % 7;
    firstThu.setUTCDate(firstThu.getUTCDate() - firstDay + 3);
    return 1 + Math.round((d - firstThu) / (7 * 24 * 3600 * 1000));
};

// Datum + HH:MM → ISO ohne Z (Backend speichert 1:1 als Lokalzeit)
function stempelBuildIso(dateStr, timeStr) {
    if (!dateStr || !timeStr) return null;
    return `${dateStr}T${timeStr}:00`;
}

let _stempelRowsCache = []; // Cache für Edit-Modus

async function stempelLadeEintraege(employeeId) {
    const el = document.getElementById('stempelzeitenContent');
    const listEl  = document.getElementById('stempelListe');
    const countEl = document.getElementById('stempelCount');
    if (!listEl || !el) return;
    listEl.innerHTML = `<div class="stempel-loading">Lade…</div>`;

    // URL bauen. Wir laden IMMER per Datumsbereich — und zwar ERWEITERT um die
    // vollen ISO-Wochen an den Periodenrändern (Mo der ersten Woche bis So der
    // letzten Woche). Grund: Wochentotale (Mo–So) müssen die ganze Woche
    // umfassen und das Total NUR beim echten letzten Eintrag der Woche zeigen —
    // auch wenn eine Woche über die Monatsgrenze läuft (Walter-Vorgabe 24.05.2026).
    // Angezeigt werden danach nur die Zeilen INNERHALB der Periode [pFrom..pTo].
    let pFrom, pTo, labelHint;
    if (el._stempelPeriodeId) {
        const periode = (el._stempelPerioden || []).find(p => p.id === el._stempelPeriodeId);
        if (!periode) {
            listEl.innerHTML = `<div style="padding:20px;color:#dc2626;font-size:13px">Periode nicht gefunden.</div>`;
            return;
        }
        pFrom = (periode.periodFrom || '').slice(0, 10);
        pTo   = (periode.periodTo   || '').slice(0, 10);
        labelHint = `Lohnperiode ${periode.label || ''} (${stempelFmtDateShort(periode.periodFrom)}–${stempelFmtDateShort(periode.periodTo)})`;
    } else {
        const y = el._stempelYear, mo = el._stempelMonth;
        pFrom = `${y}-${String(mo).padStart(2,'0')}-01`;
        pTo   = new Date(Date.UTC(y, mo, 0)).toISOString().slice(0, 10); // letzter Tag des Monats
        labelHint = `${MONATSNAMEN_DE[mo-1]} ${y}`;
    }
    const extFrom = stempelWeekMonday(pFrom) || pFrom;
    const extTo   = stempelWeekSunday(pTo)   || pTo;
    const url = `/api/employees/${employeeId}/timeentries?dateFrom=${extFrom}&dateTo=${extTo}`;

    try {
        // Lohnlauf-Sperre parallel zur Stempel-Liste laden — Buttons werden
        // pro Zeile konditional gerendert, je nachdem ob das Datum im
        // gesperrten Bereich liegt.
        const activeEmp = selectedEmployee?.employments?.find(e => e.isActive)
                       ?? selectedEmployee?.employments?.[0];
        const cpId      = activeEmp?.companyProfileId;
        const cpIdForLock = cpId || (typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null);

        const [res, lockState] = await Promise.all([
            fetch(url, { headers: { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` } }),
            cpIdForLock && window.lohnEditLock
                ? window.lohnEditLock.loadState(cpIdForLock)
                : Promise.resolve(null)
        ]);
        if (!res.ok) {
            listEl.innerHTML = `<div style="padding:20px;color:#dc2626;font-size:13px">Fehler ${res.status}</div>`;
            return;
        }
        const allRows = await res.json();
        // Angezeigt werden nur Einträge INNERHALB der Periode; die Rand-Wochen
        // (aus den Nachbarmonaten) dienen nur der korrekten Wochentotal-Bildung.
        const rows = allRows.filter(r => {
            const d = (r.entryDate || '').slice(0, 10);
            return d >= pFrom && d <= pTo;
        });
        _stempelRowsCache = rows;

        if (countEl) {
            countEl.textContent = rows.length === 0
                ? `Keine Einträge`
                : `${rows.length} Eintrag${rows.length === 1 ? '' : 'e'} · ${labelHint}`;
        }

        stempelRenderTable(rows, employeeId, lockState, allRows, pFrom, pTo);

        // Wenn leer: Shortcut-Buttons zu Monaten mit Einträgen nachladen
        if (rows.length === 0) stempelLadeQuickNav(employeeId);
    } catch (err) {
        listEl.innerHTML = `<div style="padding:20px;color:#dc2626;font-size:13px">Fehler: ${err.message}</div>`;
    }
}

// Monats-Schnellwahl als feste Matrix (Walter-Vorgabe 21.06.2026): IMMER alle
// 12 Monate, eine Zeile pro Jahr (mind. die letzten 3 Jahre + jedes Jahr mit
// Daten). Monate mit Einträgen zeigen die Anzahl, leere sind ausgegraut — aber
// alle anklickbar.
async function stempelLadeQuickNav(employeeId) {
    const navEl = document.getElementById('stempelQuickNav');
    if (!navEl) return;
    let periods = [];
    try {
        const res = await fetch(`/api/employees/${employeeId}/timeentries/periods`,
            { headers: { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` } });
        if (res.ok) periods = await res.json();
    } catch { /* silent → leeres Raster */ }
    if (!Array.isArray(periods)) periods = [];

    const byYear = {};
    periods.forEach(p => { (byYear[p.year] ??= {})[p.month] = p.count; });

    const curY = new Date().getFullYear();
    // 6 Jahre anzeigen (Aufbewahrung = 5 Jahre, Walter-Vorgabe 21.06.2026)
    // plus jedes Jahr mit Daten.
    const yearSet = new Set();
    for (let i = 0; i <= 5; i++) yearSet.add(curY - i);
    Object.keys(byYear).forEach(y => yearSet.add(Number(y)));
    const years = Array.from(yearSet).sort((a, b) => b - a);

    const el   = document.getElementById('stempelzeitenContent');
    const selY = el ? el._stempelYear  : null;
    const selM = el ? el._stempelMonth : null;
    const mon  = ['Jan','Feb','Mär','Apr','Mai','Jun','Jul','Aug','Sep','Okt','Nov','Dez'];

    let html = `<div style="font-size:12px;color:#64748b;margin-bottom:8px">Monat direkt wählen:</div>`;
    html += `<div style="display:inline-grid;grid-template-columns:auto repeat(12, minmax(38px,1fr));gap:4px;align-items:center;max-width:100%">`;
    html += `<div></div>` + mon.map(n => `<div style="font-size:10.5px;color:#94a3b8;text-align:center;font-weight:600">${n}</div>`).join('');
    years.forEach(y => {
        html += `<div style="font-size:12.5px;font-weight:700;color:#475569;padding-right:8px;text-align:right">${y}</div>`;
        for (let m = 1; m <= 12; m++) {
            const cnt   = byYear[y] && byYear[y][m];
            const has   = cnt > 0;
            const isSel = (y === selY && m === selM);
            const border = isSel ? '#1a1a1a' : (has ? '#cbd5e1' : '#eef2f6');
            const bg     = isSel ? '#1a1a1a' : (has ? '#fff'    : '#fafafa');
            const color  = isSel ? '#fff'    : (has ? '#334155' : '#cbd5e1');
            html += `<button onclick="stempelJumpTo(${y},${m})" title="${mon[m-1]} ${y}${has ? ' · ' + cnt + ' Einträge' : ' · keine Einträge'}" style="font-size:11px;padding:5px 0;border-radius:6px;cursor:pointer;border:1px solid ${border};background:${bg};color:${color};font-weight:${has ? 600 : 400}">${has ? cnt : '·'}</button>`;
        }
    });
    html += `</div>`;
    navEl.innerHTML = html;
}

function stempelJumpTo(year, month) {
    const el = document.getElementById('stempelzeitenContent');
    if (!el) return;
    // Perioden-Modus: suche Periode die Year/Month abdeckt
    const perSel = document.getElementById('stempelPeriodeSel');
    if (perSel && Array.isArray(el._stempelPerioden)) {
        const match = el._stempelPerioden.find(p => p.year === year && p.month === month);
        if (match) {
            perSel.value = match.id;
            el._stempelPeriodeId = match.id;
            stempelChangePeriod();
            return;
        }
    }
    // Kalendermonat-Fallback
    const yEl = document.getElementById('stempelYearSel');
    const mEl = document.getElementById('stempelMonthSel');
    if (!yEl || !mEl) return;
    if (!Array.from(yEl.options).some(o => parseInt(o.value, 10) === year)) {
        const opt = document.createElement('option');
        opt.value = year; opt.textContent = year;
        yEl.appendChild(opt);
    }
    yEl.value = year;
    mEl.value = month;
    stempelChangePeriod();
}

function stempelRenderTable(rows, employeeId, lockState = null, allRows = null, pFrom = null, pTo = null) {
    const listEl = document.getElementById('stempelListe');
    if (!listEl) return;

    // allRows = volle ISO-Wochen inkl. Nachbarmonats-Rändern (für Wochentotale).
    // rows    = nur die Periode (was angezeigt wird). Fallback: rows = allRows.
    const fullRows = (allRows && allRows.length >= 0) ? allRows : rows;

    // Aufsteigend nach Datum sortieren (für korrekte Mo–So-Gruppierung).
    const sortAsc = (a, b) =>
        (a.entryDate || '').localeCompare(b.entryDate || '') ||
        (a.timeIn    || '').localeCompare(b.timeIn    || '');
    const sorted   = [...rows].sort(sortAsc);       // Anzeige (nur Periode)
    const sortedAll = [...fullRows].sort(sortAsc);  // für Wochentotale (volle Wochen)

    // Monats-Summen (nur Periode). Absolute Stunden = Tag + Nacht
    // (Walter 03.08.2026 — nicht totalHours allein, das war in Alt-Daten oft nur Tag).
    const absH = (r) => {
        const d = Number(r.durationHours ?? 0);
        const n = Number(r.nightHours ?? 0);
        const t = Number(r.totalHours ?? 0);
        if (d > 0 || n > 0) {
            const parts = d + n;
            // totalHours schon = Tag+Nacht → total; total ≈ nur Tag → Tag+Nacht
            if (t >= parts - 0.05) return t;
            if (n > 0 && Math.abs(t - d) <= 0.05) return parts;
            return Math.max(t, parts);
        }
        return t;
    };
    let sumTag = 0, sumN = 0, sumTot = 0;
    sorted.forEach(r => {
        sumTag += Number(r.durationHours ?? 0);
        sumN   += Number(r.nightHours ?? 0);
        sumTot += absH(r);
    });

    // Wochentotal (Mo–So) = absolute gestempelte Stunden (Tag+Nacht)
    const weekSum = {}, lastIdOfWeek = {};
    sortedAll.forEach(r => {
        const wk = stempelWeekMonday(r.entryDate);
        if (!wk) return;
        weekSum[wk] = (weekSum[wk] || 0) + absH(r);
        lastIdOfWeek[wk] = r.id;   // sortiert aufsteigend ⇒ am Ende = letzter Eintrag der Woche
    });

    // Max. Stunden/Woche der aktuell gewählten Filiale (Warngrenze).
    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
    const branch = (typeof allBranches !== 'undefined' ? allBranches : []).find(b => b.id === cpId);
    const maxWeekly = (branch && branch.maxWeeklyHours != null && branch.maxWeeklyHours !== '')
        ? Number(branch.maxWeeklyHours) : null;

    const esc = (s) => s == null ? '' : String(s).replace(/</g,'&lt;');

    const trs = sorted.map((r, i) => {
        const wasEdited = !!r.editedBy;

        // Wochentotal NUR beim echten letzten Eintrag der Woche zeigen (über alle
        // geladenen Wochen, inkl. Nachbarmonate). Läuft die Woche in den nächsten
        // Monat und hat dort Einträge, fällt das Total hier weg (es erscheint dann
        // dort); hat der nächste Monat keine Einträge dieser Woche, ist der letzte
        // Eintrag hier (z.B. Fr) und das Total wird hier gezeigt.
        const wk = stempelWeekMonday(r.entryDate);
        const isLastOfWeek = wk && lastIdOfWeek[wk] === r.id;

        // Wochentotal-Badge: KW schwach, Total fett (über Max → .over).
        // Look via .stempel-week-* in app.css (Liquid Glass).
        let weekBadge = '';
        if (isLastOfWeek) {
            const wt   = weekSum[wk] || 0;
            const over = maxWeekly != null && wt > maxWeekly + 1e-9;
            const kw   = stempelIsoWeek(r.entryDate);
            const kwLabel  = `<span class="stempel-week-kw">∑ KW${kw}</span>`;
            const hrsLabel = `<span class="stempel-week-hrs${over ? ' over' : ''}">${stempelFmtHours(wt)} h${over ? ` ⚠ &gt; ${stempelFmtHours(maxWeekly)}` : ''}</span>`;
            weekBadge = `<span class="stempel-week-badge" title="Wochentotal Mo–So (gestempelt)${maxWeekly != null ? ' · Max ' + stempelFmtHours(maxWeekly) + ' h' : ''}">${kwLabel}${hrsLabel}</span>`;
        }

        // Korrektur: kurze Zusammenfassung statt «geändert von…» + Original-Zeile
        // (Walter 08.10.2026 — wie im easy@work-Verlauf gelesen).
        const timeCls = wasEdited ? ' stempel-time-edited' : '';
        const korrekturKommentar = wasEdited
            ? `<span class="stempel-edit-meta" style="cursor:pointer;text-decoration:underline dotted" title="Kommentare und Verlauf aus easy@work anzeigen" onclick="stempelEasyVerlauf(${r.id})">${esc(stempelKorrekturKurz(r))}</span>`
            : esc(r.comment);

        // Kommentar-Zelle: Kommentar + (optional) Wochentotal direkt dahinter.
        // Walter 17.06.2026: Wochentotal nicht am rechten Rand „pinnen", sondern
        // mit Lücke nach dem Kommentar — wirkt zusammenhängend.
        const kommentarCell = weekBadge
            ? `<div class="stempel-kommentar-wrap"><span>${korrekturKommentar}</span>${weekBadge}</div>`
            : korrekturKommentar;

        const nightH = Number(r.nightHours || 0);
        const rowCls = [
            'stempel-row',
            isLastOfWeek ? 'stempel-row-week-end' : '',
            wasEdited ? 'stempel-row-edited' : '',
        ].filter(Boolean).join(' ');

        // Stempelzeiten sind read-only (Walter-Vorgabe 17.05.2026): keine
        // Edit-/Löschen-Buttons mehr — easy@work ist die Quelle der Wahrheit.

        const totalRow = stempelFmtHours(absH(r));
        return `
            <tr class="${rowCls}" data-row-id="${r.id}">
                <td class="stempel-td stempel-td-date">${stempelFmtDate(r.entryDate)}</td>
                <td class="stempel-td stempel-td-time${timeCls}">${stempelFmtTime(r.timeIn)}</td>
                <td class="stempel-td stempel-td-time${timeCls}">${stempelFmtTime(r.timeOut)}</td>
                <td class="stempel-td stempel-td-num">${stempelFmtHours(r.durationHours)}</td>
                <td class="stempel-td stempel-td-num ${nightH > 0 ? 'stempel-night' : 'stempel-night-zero'}">${stempelFmtHours(r.nightHours)}</td>
                <td class="stempel-td stempel-td-num stempel-td-total">${totalRow}</td>
                <td class="stempel-td stempel-td-comment">${kommentarCell}</td>
            </tr>`;
    }).join('');

    const empty = sorted.length === 0
        ? `<tr><td colspan="7" class="stempel-empty">
            Keine Einträge
            <div id="stempelQuickNav" class="stempel-quick-nav"></div>
        </td></tr>`
        : '';

    // Spaltenköpfe FIX ausserhalb Scroll; nur Zeilen + Summe scrollen.
    const headHtml = `
        <table class="stempel-table stempel-table-head">
            <colgroup>
                <col class="stempel-col-date"><col class="stempel-col-time"><col class="stempel-col-time">
                <col class="stempel-col-num"><col class="stempel-col-num"><col class="stempel-col-num">
                <col class="stempel-col-comment">
            </colgroup>
            <thead>
                <tr>
                    <th class="stempel-th stempel-th-left">Datum</th>
                    <th class="stempel-th stempel-th-left">In</th>
                    <th class="stempel-th stempel-th-left">Out</th>
                    <th class="stempel-th stempel-th-right">Tag</th>
                    <th class="stempel-th stempel-th-right">Nacht</th>
                    <th class="stempel-th stempel-th-right">Total</th>
                    <th class="stempel-th stempel-th-left">Kommentar / Woche</th>
                </tr>
            </thead>
        </table>`;
    const colsEl = document.getElementById('stempelCols');
    if (colsEl) colsEl.innerHTML = headHtml;

    listEl.innerHTML = `
        <table class="stempel-table stempel-table-body">
            <colgroup>
                <col class="stempel-col-date"><col class="stempel-col-time"><col class="stempel-col-time">
                <col class="stempel-col-num"><col class="stempel-col-num"><col class="stempel-col-num">
                <col class="stempel-col-comment">
            </colgroup>
            <tbody>${trs}${empty}</tbody>
            ${sorted.length > 0 ? `<tfoot>
                <tr class="stempel-foot-row">
                    <td colspan="3" class="stempel-ft stempel-ft-label">Summe</td>
                    <td class="stempel-ft stempel-ft-num">${stempelFmtHours(sumTag)}</td>
                    <td class="stempel-ft stempel-ft-num">${stempelFmtHours(sumN)}</td>
                    <td class="stempel-ft stempel-ft-num stempel-ft-total">${stempelFmtHours(sumTot)}</td>
                    <td class="stempel-ft"></td>
                </tr>
            </tfoot>` : ''}
        </table>`;
}

// ────────────────────────────────────────────────────────────────────────────
// Stempelzeiten-Edit-/Save-/Delete-Funktionen ENTFERNT (Walter 17.06.2026):
// Cowork ist seit Mai 2026 read-only für Stempelzeiten. Backend-Endpoints
// POST/PUT/DELETE liefern 403. Mit der easy@work-API-Integration ist
// easy@work die alleinige Quelle — Erfassung + Korrekturen passieren dort,
// in Cowork läuft nur noch der Sync und die Anzeige.
//
// Entfernt: stempelRenderForm, stempelStartNew, stempelStartEdit,
//           stempelCancelForm, stempelSaveForm, stempelDelete.
// Bleibt:  stempelEditRow im DOM-Template (versteckt, harmlos);
//          OriginalTimeIn/Out-Anzeige (Audit-Hinweis aus dem easy@work-Sync).
// ────────────────────────────────────────────────────────────────────────────

