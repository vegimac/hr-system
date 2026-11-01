// ══════════════════════════════════════════════════════════════════
// SPERRFRIST-PANEL (Kündigungsschutz nach Art. 336c OR)
// ══════════════════════════════════════════════════════════════════
// Schutz gilt nur solange tatsächlich AU besteht — höchstens 30/90/180
// Tage (je Dienstjahr). Erster AU-Tag = Sperrtag 1 (inklusiv).
/**
 * Frühester letzter Arbeitstag bei Kündigung heute.
 * L-GAV Gastgewerbe (Walter 25.07.2026): 1.–5. Dienstjahr = 1 Monat,
 * ab 6. Dienstjahr = 2 Monate — jeweils auf Monatsende.
 * (Nicht OR 335c mit 2 Monaten ab 2. Jahr.)
 */
function _sperrKuendPerEndeMonat(dienstjahr, kuendAbIso) {
    const dj = dienstjahr || 1;
    const months = dj >= 6 ? 2 : 1;
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    let kdat = today;
    if (kuendAbIso) {
        const ab = new Date(String(kuendAbIso).slice(0, 10) + 'T00:00:00');
        if (!isNaN(ab) && ab > today) kdat = ab;
    }
    // 1. des Monats + months + 1 Monat − 1 Tag = Monatsende nach Frist
    const per = new Date(kdat.getFullYear(), kdat.getMonth() + months + 1, 0);
    const dd = String(per.getDate()).padStart(2, '0');
    const mm = String(per.getMonth() + 1).padStart(2, '0');
    return { per, months, perTxt: `${dd}.${mm}.${per.getFullYear()}` };
}

function renderSperrfristPanel(info) {
    if (!info) return '';

    const wrap = (color, bg, border, inner) => `
        <div style="background:${bg};border:1px solid ${border};border-radius:10px;padding:12px 16px;margin-bottom:8px;font-size:13.5px">
            <div style="font-size:11px;font-weight:700;color:${color};text-transform:uppercase;letter-spacing:0.08em;margin-bottom:6px">Kündigungsschutz</div>
            ${inner}
        </div>`;

    const status = info.status;
    const dj = info.dienstjahrAmStichtag || 1;
    const djText = info.dienstjahrAmStichtag ? `${info.dienstjahrAmStichtag}. Dienstjahr` : '–';
    const isMts  = info.auGrund === 'MUTTERSCHAFT';
    const grundLabel = info.auGrund === 'UNFALL' ? 'Unfall'
                    : info.auGrund === 'KRANK+UNFALL' ? 'Krankheit + Unfall'
                    : info.auGrund === 'MUTTERSCHAFT' ? 'Mutterschaft'
                    : (info.auGrund ? 'Krankheit' : '');
    const maxTage = info.sperrfristTage || null;
    let auTage = info.auDauerTage || 0;
    if (!auTage && info.auBeginn && info.auEnde) {
        const a = new Date(String(info.auBeginn).slice(0, 10) + 'T00:00:00');
        const b = new Date(String(info.auEnde).slice(0, 10) + 'T00:00:00');
        if (!isNaN(a) && !isNaN(b)) auTage = Math.round((b - a) / 86400000) + 1;
    }
    const chainKurz = (info.auBeginn && info.auEnde)
        ? `${fmtDate(info.auBeginn)} – ${fmtDate(info.auEnde)}`
        : '';
    // Walter 25.07.2026: Leitsatz «X Tage ununterbrochen krank · Kündigung per Ende Monat»
    const kuendPerInfo = _sperrKuendPerEndeMonat(dj, info.kuendigungAbDatum);
    const kuendPerTxt = kuendPerInfo.perTxt;
    const heroKuendbar = auTage > 0
        ? `<b>${auTage} Tage</b> ununterbrochen krank${chainKurz ? ` (${chainKurz})` : ''}. Kündigung per <b>${kuendPerTxt}</b> (Ende Monat) möglich.`
        : `Kündigung per <b>${kuendPerTxt}</b> (Ende Monat) möglich.`;

    // Walter 25.07.2026: Banner nur bei laufender AU (Krank/Unfall) wenn die
    // Sperrfrist erreicht/ausgeschöpft ist — nach AU-Ende immer normale L-GAV-
    // Kündigung, kein Sonderbanner. Mutterschaft bleibt sichtbar.
    if (status === 'KEINE_AU' || status === 'KEIN_EINTRITT' || status === 'IN_PROBEZEIT')
        return '';
    if (!isMts
        && status !== 'SPERRFRIST_ABGELAUFEN'
        && !(status === 'GESCHUETZT' && maxTage && auTage >= maxTage))
        return '';

    const isGeschuetzt = status === 'GESCHUETZT';
    const isKuendbar = status === 'SPERRFRIST_ABGELAUFEN';
    const color  = isGeschuetzt ? '#b91c1c' : '#166534';
    const bg     = isGeschuetzt ? '#fef2f2' : '#f0fdf4';
    const border = isGeschuetzt ? '#fecaca' : '#86efac';

    // Mutterschaft: festes Ende (Geburt + 16 Wochen) — eigene Formulierung
    if (isMts) {
        return wrap(color, bg, border, `
            <div style="display:flex;gap:14px;align-items:flex-start;flex-wrap:wrap">
                <div style="flex:1;min-width:240px">
                    <div style="font-weight:700;color:#0f172a;font-size:14px;line-height:1.35">
                        Kündigungsschutz wegen Schwangerschaft/Mutterschaft
                    </div>
                    <div style="color:#475569;margin-top:6px;line-height:1.45">
                        Geschützt bis <b>${fmtDate(info.sperrfristEnde)}</b> —
                        Kündigung frühestens ab <b style="color:${color}">${fmtDate(info.kuendigungAbDatum)}</b>.
                        ${info.hinweis ? `<div style="margin-top:6px;font-size:12px">${esc(info.hinweis)}</div>` : ''}
                    </div>
                </div>
                ${isGeschuetzt ? `<div style="text-align:right;min-width:120px">
                    <div style="font-size:22px;font-weight:700;color:${color}">${info.verbleibendeTage ?? '–'}</div>
                    <div style="font-size:11px;color:#64748b">Tage bis kündbar</div>
                </div>` : `<div style="font-size:13px;font-weight:700;color:${color}">✓ Schutz abgelaufen</div>`}
            </div>`);
    }

    const sperrTag = info.auDauerTage || auTage || 0;
    const aktuellBis = info.aktuellGeschuetztBis || info.auEnde;
    const maxBis = info.sperrfristEnde;
    const kuendAb = info.kuendigungAbDatum;
    const chainLine = chainKurz
        ? `Durchgehende AU-Kette <b>${chainKurz}</b>` + (grundLabel ? ` (${grundLabel})` : '')
        : '';

    // Walter 25.07.2026: Leitsatz mit Tagen + Kündigung per Monatsende
    if (isKuendbar) {
        return wrap('#166534', '#ecfdf5', '#6ee7b7', `
            <div style="display:flex;gap:14px;align-items:flex-start;flex-wrap:wrap">
                <div style="flex:1;min-width:260px">
                    <div style="font-weight:800;color:#14532d;font-size:15px;line-height:1.4">
                        ${heroKuendbar}
                    </div>
                    <div style="color:#475569;margin-top:8px;line-height:1.5;font-size:12.5px">
                        ${djText} · Sperrfrist (${maxTage || '–'} Tage) endete am ${fmtDate(maxBis)}
                        ${kuendAb ? ` · kündbar seit ${fmtDate(kuendAb)}` : ''}
                        · Kündigungsfrist ${kuendPerInfo.months} Monat(e) auf Ende Monat (L-GAV).
                        Karenz/Lohnfortzahlung ist davon unabhängig.
                    </div>
                    ${info.hinweis ? `<div style="margin-top:6px;padding:8px 10px;background:#fffbeb;border:1px solid #fde68a;border-radius:6px;color:#78350f;font-size:11px">⚠︎ ${esc(info.hinweis)}</div>` : ''}
                </div>
                <div style="text-align:right;min-width:120px">
                    <div style="font-size:13px;font-weight:800;color:#14532d;background:#bbf7d0;padding:8px 10px;border-radius:8px">KÜNDBAR</div>
                    <div style="font-size:11px;color:#64748b;margin-top:6px">per ${kuendPerTxt}</div>
                </div>
            </div>`);
    }

    // GESCHUETZT (Krankheit/Unfall): aktuell vs. maximal klar trennen
    return wrap(color, bg, border, `
        <div style="display:flex;gap:14px;align-items:flex-start;flex-wrap:wrap">
            <div style="flex:1;min-width:260px">
                <div style="font-weight:700;color:#0f172a;font-size:14px;line-height:1.35">
                    Aktuell kündigungsgeschützt aufgrund Arbeitsunfähigkeit
                </div>
                <div style="color:#334155;margin-top:8px;line-height:1.5">
                    ${chainLine || `Ärztlich bestätigte Arbeitsunfähigkeit bis <b>${fmtDate(aktuellBis)}</b>`}.
                    ${aktuellBis && info.auEnde && aktuellBis !== info.auEnde ? '' : ''}
                </div>
                <div style="color:#475569;margin-top:6px;line-height:1.5;font-size:13px">
                    Bei durchgehender Arbeitsunfähigkeit maximale Sperrfrist bis <b>${fmtDate(maxBis)}</b>
                    — Kündigung frühestens ab <b style="color:${color}">${fmtDate(kuendAb)}</b>
                    (${djText}${maxTage ? `, max. ${maxTage} Tage` : ''}).
                </div>
                <div style="color:#94a3b8;margin-top:6px;font-size:11.5px;line-height:1.4">
                    Hinweis: Krankheits-Karenz / Lohnfortzahlung ist eine separate Berechnung und beendet den Kündigungsschutz nicht.
                </div>
                ${info.hinweis ? `<div style="margin-top:6px;padding:8px 10px;background:#fffbeb;border:1px solid #fde68a;border-radius:6px;color:#78350f;font-size:11px">⚠︎ ${esc(info.hinweis)}</div>` : ''}
            </div>
            <div style="text-align:right;min-width:130px">
                <div style="font-size:22px;font-weight:700;color:${color}">${sperrTag}<span style="font-size:14px;font-weight:600;color:#94a3b8"> / ${maxTage || '–'}</span></div>
                <div style="font-size:11px;color:#64748b">Sperrtag von maximal ${maxTage || '–'}</div>
            </div>
        </div>`);
}

// ══════════════════════════════════════════════════════════════════
// KARENZ-PANEL (Krankheit + Unfall)
// ══════════════════════════════════════════════════════════════════
// Walter 25.07.2026: Karenz IMMER zeigen (kompakt rechts unter Tagessatz).
// Krank und Unfall getrennt, nie zusammengezählt. Keine Vorjahre/Details.
function renderKarenzSidebar(krankHist, unfallHist) {
    return [
        renderKarenzCard(krankHist,  { label: 'Krankheits-Karenz', dayLabel: 'Kranktage',  defaultMax: 14 }),
        renderKarenzCard(unfallHist, { label: 'Unfall-Karenz',     dayLabel: 'Unfalltage', defaultMax: 2  }),
    ].join('');
}

// Karte «Ferienkürzung» unter der Karenz (Walter 23.09.2026): pro Dienstjahr
// (abgelaufen + laufend) Krankheitstage, Zwölftel, bereits gekürzt, noch
// möglich — mit Knopf direkt ins Erfassen-Formular für genau dieses Jahr.
async function fkSidebarLaden(employeeId) {
    const side = document.getElementById('karenzSidebar');
    if (!side || !employeeId) return;
    const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    const heute = new Date(), vorJahr = new Date(); vorJahr.setFullYear(heute.getFullYear() - 1);
    let infos = [];
    try {
        infos = await Promise.all([iso(vorJahr), iso(heute)].map(d =>
            fetch(`/api/absences/employee/${employeeId}/ferienkuerzung-info?datum=${d}`, { headers: ah() })
                .then(r => r.ok ? r.json() : null)));
    } catch { return; }
    if (window.selectedEmployeeId !== employeeId) return;   // schneller MA-Wechsel
    infos = infos.filter(i => i && i.dienstjahrVon && i.dienstjahrVon !== '0001-01-01'
                           && (Number(i.tageKrankUnfall) > 0 || Number(i.bereitsGekuerzt) > 0));
    document.getElementById('fkSideCard')?.remove();
    if (!infos.length) return;
    const n2 = v => Number(v || 0).toFixed(2);
    const kurz = t => _absDatumKurz(t);
    const zeilen = infos.map((i, k) => {
        const noch = Number(i.nochMoeglich || 0);
        const laufend = !!i.laufend;
        const monat = heute.toLocaleDateString('de-CH', { month: 'long' });
        return `<div style="padding:8px 0;${k ? 'border-top:1px solid rgba(146,64,14,0.15);' : ''}">
            <div style="font-weight:700;color:#3f3f3f">${kurz(i.dienstjahrVon)} – ${kurz(i.dienstjahrBis)} <span style="font-weight:500;color:#8b8b8b">(${laufend ? `laufend · Stand Ende ${monat}` : 'abgelaufen'})</span></div>
            <div style="font-size:12.5px;color:#646464">${Number(i.tageKrankUnfall).toFixed(1).replace(/\.0$/, '')} Krankheitstage → <b>${i.zwoelftel}/12</b> = ${n2(i.gesamtTage)} Tage</div>
            <div style="font-size:12.5px;color:#646464">${Number(i.bereitsGekuerzt) > 0 ? `bereits gekürzt ${n2(i.bereitsGekuerzt)} · ` : ''}${i.verzichtet ? 'nicht kürzen gewählt · ' : ''}noch möglich <b style="color:#92400e">${Math.floor(noch)} Tage</b>${noch % 1 ? ` <span style="color:#8b8b8b">(genau ${n2(noch)})</span>` : ''}</div>
            ${laufend ? `<div style="font-size:11.5px;color:#8b8b8b;margin-top:3px">Kürzen nach Ende des Dienstjahres oder beim Austritt.</div>` : ''}
            ${noch >= 1 ? `<button type="button" onclick="openFerienKuerzungModal(${employeeId}, null, '${i.dienstjahrVon}')"
                style="margin-top:6px;${laufend
                    ? 'background:rgba(255,255,255,0.6);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35)'
                    : 'background:#1a1a1a;color:#fff;border:none'};border-radius:10px;padding:5px 12px;font-size:12px;font-weight:700;cursor:pointer">Kürzung erfassen</button>` : ''}
        </div>`;
    }).join('');
    side.insertAdjacentHTML('beforeend', `
        <div id="fkSideCard" class="karenz-side-card" style="background:linear-gradient(165deg,#fffbeb,#fef3c7);border-color:#fcd34d">
            <div class="karenz-side-label" style="color:#92400e">Ferienkürzung</div>
            ${zeilen}
        </div>`);
}

function _todayIsoLocal() {
    const t = new Date();
    return `${t.getFullYear()}-${String(t.getMonth() + 1).padStart(2, '0')}-${String(t.getDate()).padStart(2, '0')}`;
}

function _pickCurrentKarenzJahr(history) {
    if (!Array.isArray(history) || history.length === 0) return null;
    const iso = _todayIsoLocal();
    return history.find(h => h.info && h.info.von <= iso && h.info.bis >= iso) || history[0];
}

function renderKarenzCard(history, cfg) {
    const current = _pickCurrentKarenzJahr(history);
    if (!current?.info) return '';

    const curInfo = current.info;
    const max  = Number(curInfo.tageMax) || cfg.defaultMax;
    const used = Number(curInfo.tageVerbraucht) || 0;
    const erreicht = !!curInfo.grenzErreichtAm;
    // Walter 25.07.2026: Tage im Arbeitsjahr (nicht Anzahl Fälle).
    const usedTxt = used.toLocaleString('de-CH', {
        minimumFractionDigits: Number.isInteger(used) ? 0 : 2,
        maximumFractionDigits: 2,
    });
    const statusTxt = erreicht
        ? `Grenze ${max} Tage erreicht (${fmtDate(curInfo.grenzErreichtAm)})`
        : `Grenze offen (${usedTxt} / ${max} Tage)`;

    return `
    <div class="karenz-side-card ${erreicht ? 'karenz-reached' : 'karenz-ok'}">
        <div class="karenz-side-label">${cfg.label}</div>
        <div class="karenz-side-year">${fmtDate(curInfo.von)} – ${fmtDate(curInfo.bis)}</div>
        <div class="karenz-side-count">
            <strong>${usedTxt}</strong>
            <span>${cfg.dayLabel} in diesem Arbeitsjahr</span>
        </div>
        <div class="karenz-side-status">${statusTxt}</div>
    </div>`;
}

function fmtDate(iso) {
    if (!iso) return '–';
    const d = new Date(iso + 'T00:00:00');
    return d.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

// ── Absenz-Typen-Cache ─────────────────────────────────────────
// Laden einmal pro Modal-Öffnung; Cache wird bei jedem openAbsenceModal
// zurückgesetzt, damit Admin-Änderungen sichtbar werden.
let _absenzTypenCache = null;
async function getAbsenzTypen() {
    if (_absenzTypenCache) return _absenzTypenCache;
    try {
        const res = await fetch('/api/absenz-typen', { headers: ah() });
        if (!res.ok) return [];
        _absenzTypenCache = await res.json();
        return _absenzTypenCache;
    } catch {
        return [];
    }
}

// ── Absenz-Modal ───────────────────────────────────────────────
async function openAbsenceModal(existing, opts) {
    _absenzTypenCache = null;  // Cache invalidieren → frische Konfig holen
    const modal = document.getElementById('absenceModal');
    if (!modal) return;
    modal.style.display = 'flex';

    // Nur-Ansicht-Modus (Walter 13.08.2026): gesperrte Absenzen («In Lohn
    // verwendet») dürfen im Detail angeschaut, aber nicht geändert werden.
    const absReadOnly = !!(opts && opts.readOnly);
    modal.classList.toggle('abs-ro', absReadOnly);
    const absRoBanner = document.getElementById('absReadOnlyBanner');
    if (absRoBanner) absRoBanner.style.display = absReadOnly ? '' : 'none';
    const absSaveBtn = document.getElementById('absSaveBtn');
    if (absSaveBtn) absSaveBtn.style.display = absReadOnly ? 'none' : '';

    // Typen aus DB laden und Dropdown befüllen
    const typen = await getAbsenzTypen();
    const sel = document.getElementById('absTypeSelect');
    const currentVal = existing?.absenceType ?? 'KRANK';
    // Feiertag nur bei FIX/FIX-M erfassbar (Walter 05.10.2026) — eine bestehende
    // (importierte) Feiertags-Absenz bleibt bearbeitbar.
    const absModell = (selectedEmployee?.employmentModel ?? '').toUpperCase();
    const feiertagGesperrt = absModell === 'MTP' || absModell === 'FLEX' || absModell === 'UTP';
    sel.innerHTML = typen
        .filter(t => !(feiertagGesperrt && t.code === 'FEIERTAG' && currentVal !== 'FEIERTAG'))
        .map(t =>
        `<option value="${t.code}" ${t.code === currentVal ? 'selected' : ''}>${t.bezeichnung}</option>`
    ).join('')
        // Walter 23.09.2026: öffnet das eigene Formular (keine echte Absenz-Zeile).
        + (existing ? '' : '<option value="FERIEN_KUERZUNG">Absenzbedingte Ferienkürzung</option>');

    // Aktive Lohnperiode der MA-Filiale ermitteln, um die Datums-Auswahl auf
    // diese Periode zu begrenzen (Walter-Wunsch: nicht versehentlich in eine
    // andere Periode erfassen). Beim Edit lassen wir's offen, weil die
    // bestehende Absenz auch ausserhalb der heutigen Periode liegen kann.
    const isNewAbs = !existing;
    let absPeriodFrom = '', absPeriodTo = '';
    if (isNewAbs) {
        const cid = (typeof selectedEmployee !== 'undefined' && selectedEmployee?.companyProfileId)
                  ?? (typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null);
        if (cid) {
            try {
                const todayIso = new Date().toISOString().slice(0, 10);
                const y = parseInt(todayIso.slice(0, 4), 10);
                const m = parseInt(todayIso.slice(5, 7), 10);
                const r = await fetch(`/api/payroll-perioden/current?companyProfileId=${cid}&year=${y}&month=${m}`,
                    { headers: { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` } });
                if (r.ok) {
                    const txt = await r.text();
                    if (txt && txt.trim() && txt.trim() !== 'null') {
                        const p = JSON.parse(txt);
                        absPeriodFrom = (p.periodFrom || '').slice(0, 10);
                        absPeriodTo   = (p.periodTo   || '').slice(0, 10);
                    }
                }
            } catch {}
        }
    }
    const fromEl = document.getElementById('absDateFrom');
    const toEl   = document.getElementById('absDateTo');
    // Walter-Vorgabe 27.06.2026: KEINE Begrenzung mehr auf die aktuelle Periode.
    // Absenzen dürfen in jede noch NICHT definitiv abgeschlossene Periode (auch
    // rückwirkend). Die Sperre prüft der Server per-Periode beim Speichern und
    // meldet eine abgeschlossene Periode mit klarer Fehlermeldung (LOHN_EDIT_LOCKED).
    fromEl.removeAttribute('min'); fromEl.removeAttribute('max');
    toEl.removeAttribute('min');   toEl.removeAttribute('max');

    // Reset form — bei neuer Absenz: heutiges Datum (oder periodFrom falls
    // heute ausserhalb der Periode liegt) in Von/Bis vorbelegen.
    const today = new Date().toISOString().slice(0, 10);
    let defaultDate = today;
    if (absPeriodFrom && absPeriodTo && (today < absPeriodFrom || today > absPeriodTo)) {
        defaultDate = absPeriodFrom;
    }
    document.getElementById('absTypeSelect').value   = currentVal;
    document.getElementById('absDateFrom').value      = existing?.dateFrom ?? defaultDate;
    document.getElementById('absDateTo').value        = existing?.dateTo   ?? defaultDate;
    document.getElementById('absProzent').value       = existing?.prozent ?? 100;
    document.getElementById('absNotes').value         = existing?.notes ?? '';
    const ffCb = document.getElementById('absFerienfaehig');
    if (ffCb) ffCb.checked = !!existing?.ferienfaehig;
    absFerienfaehigSync();
    document.getElementById('absModalTitle').textContent = existing ? 'Absenz bearbeiten' : 'Absenz erfassen';
    document.getElementById('absenceModal').dataset.editId = existing?.id ?? '';

    // Pre-select worked days if editing
    window._absEditWorkedDays = existing?.workedDays ? JSON.parse(existing.workedDays) : [];
    window._absIsNew = isNewAbs;
    window._absUserTouchedDays = false;
    window._absContinuationHint = '';
    window._absDienstplanBis = existing?.dienstplanBis ? String(existing.dienstplanBis).slice(0, 10) : '';
    // «Dienstplan bekannt bis» folgt dem Bis-Datum, bis man es von Hand ändert (Walter 06.10.2026) —
    // seit 13.10.2026 auch beim Bearbeiten einer Absenz ohne eingetragenes Datum.
    window._absPlanAuto = isNewAbs || !window._absDienstplanBis;

    // Neue Krank/Unfall direkt im Anschluss an Vormonat → Mo–Fr erzwingen
    // (Sa/So frei), analog Import-Fix Walter 26.07.2026. Bei FIX/MTP zählt
    // ohne Dienstplan 1/7 — dort keine Vorauswahl.
    if (isNewAbs && (currentVal === 'KRANK' || currentVal === 'UNFALL') && !_absZgModell()) {
        await _absApplyContinuationMoFr(currentVal, document.getElementById('absDateFrom').value);
    }

    // Raster der letzten Absenz wegräumen — sonst liest _absZgTageAusDom alte Kreuze.
    const dayBox = document.getElementById('absDayCheckboxes');
    if (dayBox) dayBox.innerHTML = '';
    renderAbsDayCheckboxes();
    calcAbsHoursPreview();
    window._absCalAnker = null;
    window._absCalMonat = null;
    absCalRender();
}

// ── Zeitraum im Kalender markieren (Walter 13.10.2026) ──────────────────
// Zwei Monate nebeneinander. 1. Klick = Von (und Bis), 2. Klick = Bis;
// liegt der 2. Klick vor dem ersten, werden die beiden getauscht.
window._absCalAnker = null;   // ISO des ersten Klicks, solange der zweite fehlt
window._absCalMonat = null;   // erster gezeigter Monat als Date (1. des Monats)
function _absCalIso(d) {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
function absCalRender() {
    const box = document.getElementById('absRangeCal');
    if (!box) return;
    const from = document.getElementById('absDateFrom').value;
    const to = document.getElementById('absDateTo').value;
    const ref = from ? new Date(from + 'T00:00:00') : new Date();
    let m = window._absCalMonat;
    const imBild = mo => m && (mo.getFullYear() * 12 + mo.getMonth()) - (m.getFullYear() * 12 + m.getMonth());
    if (!m || (from && !window._absCalAnker && ![0, 1].includes(imBild(ref)))) {
        m = new Date(ref.getFullYear(), ref.getMonth(), 1);
        window._absCalMonat = m;
    }
    const heute = _absCalIso(new Date());
    const monat = (start) => {
        const kopf = start.toLocaleDateString('de-CH', { month: 'long', year: 'numeric' });
        let html = `<div class="abs-cal-monat"><div class="abs-cal-titel">${kopf}</div><div class="abs-cal-grid">`
            + ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'].map(t => `<div class="abs-cal-wt">${t}</div>`).join('');
        const leer = (start.getDay() + 6) % 7;
        for (let i = 0; i < leer; i++) html += '<div></div>';
        const tage = new Date(start.getFullYear(), start.getMonth() + 1, 0).getDate();
        for (let t = 1; t <= tage; t++) {
            const d = new Date(start.getFullYear(), start.getMonth(), t);
            const iso = _absCalIso(d);
            const cls = ['abs-cal-tag'];
            if (d.getDay() === 0 || d.getDay() === 6) cls.push('we');
            if (iso === heute) cls.push('heute');
            if (from && to && iso >= from && iso <= to) cls.push('in');
            if (iso === from) cls.push('start');
            if (iso === to) cls.push('ende');
            html += `<button type="button" class="${cls.join(' ')}" data-iso="${iso}" onclick="absCalKlick('${iso}')" onmouseenter="absCalHover('${iso}')">${t}</button>`;
        }
        return html + '</div></div>';
    };
    const n = from && to && from <= to
        ? Math.round((new Date(to + 'T00:00:00') - new Date(from + 'T00:00:00')) / 86400000) + 1 : 0;
    const fmt = iso => `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}`;
    const status = window._absCalAnker
        ? `Von ${fmt(window._absCalAnker)} — jetzt den letzten Tag anklicken`
        : (n ? `${fmt(from)} – ${fmt(to)} · ${n} Kalendertag${n === 1 ? '' : 'e'}` : 'Ersten Tag anklicken, dann den letzten');
    box.innerHTML = `<div class="abs-cal-kopf">
            <button type="button" class="abs-cal-nav" onclick="absCalBlaettern(-1)" title="Früher">‹</button>
            <span class="abs-cal-status">${status}</span>
            <button type="button" class="abs-cal-nav" onclick="absCalBlaettern(1)" title="Später">›</button>
        </div>
        <div class="abs-cal-monate" onmouseleave="absCalHover(null)">${monat(m)}${monat(new Date(m.getFullYear(), m.getMonth() + 1, 1))}</div>`;
}
function absCalBlaettern(delta) {
    const m = window._absCalMonat || new Date();
    window._absCalMonat = new Date(m.getFullYear(), m.getMonth() + delta, 1);
    absCalRender();
}
function absCalHover(iso) {
    const a = window._absCalAnker;
    document.querySelectorAll('#absRangeCal .abs-cal-tag').forEach(b => {
        const x = b.dataset.iso;
        b.classList.toggle('vorschau', !!(a && iso && x >= (a < iso ? a : iso) && x <= (a < iso ? iso : a)));
    });
}
function absCalKlick(iso) {
    const fromEl = document.getElementById('absDateFrom');
    const toEl = document.getElementById('absDateTo');
    const a = window._absCalAnker;
    if (!a) {
        fromEl.value = iso;
        toEl.value = iso;
        window._absCalAnker = iso;
    } else {
        fromEl.value = a < iso ? a : iso;
        toEl.value = a < iso ? iso : a;
        window._absCalAnker = null;
    }
    renderAbsDayCheckboxes();
    calcAbsHoursPreview();
    absCalRender();
}

/** Fortlaufende AU: Absenz endet am Vortag von dateFrom → Mo–Fr vorwählen. */
async function _absApplyContinuationMoFr(type, dateFrom) {
    window._absContinuationHint = '';
    const empId = typeof selectedEmployeeId !== 'undefined' ? selectedEmployeeId : null;
    if (!empId || !dateFrom || !type) return;
    try {
        const res = await fetch(`/api/absences/employee/${empId}`, { headers: ah() });
        if (!res.ok) return;
        const list = await res.json();
        const from = new Date(dateFrom + 'T00:00:00');
        const prevDay = new Date(from);
        prevDay.setDate(prevDay.getDate() - 1);
        const prevIso = localIso(prevDay);
        const prev = (list || []).find(a =>
            a.absenceType === type
            && a.dateTo
            && String(a.dateTo).slice(0, 10) === prevIso);
        if (!prev) return;
        // Mo–Fr für den neuen Zeitraum vorwählen (Sa/So frei) — Muster Vormonat.
        const toEl = document.getElementById('absDateTo');
        const to = (toEl && toEl.value) || dateFrom;
        const days = [];
        let cur = new Date(dateFrom + 'T00:00:00');
        const end = new Date(to + 'T00:00:00');
        while (cur <= end) {
            const dow = cur.getDay();
            if (dow >= 1 && dow <= 5) days.push(localIso(cur));
            cur.setDate(cur.getDate() + 1);
        }
        window._absEditWorkedDays = days;
        window._absContinuationHint =
            'Fortsetzung Vormonat erkannt — Mo–Fr markiert, Sa/So frei (wie lange Krankheit).';
    } catch { /* ignore */ }
}

// Häkchen «arbeitsunfähig, aber ferienfähig» nur bei Ferien (Walter 23.09.2026).
function absFerienfaehigSync() {
    const row = document.getElementById('absFerienfaehigRow');
    const typ = document.getElementById('absTypeSelect')?.value;
    if (row) row.style.display = typ === 'FERIEN' ? 'flex' : 'none';
}

// Markierung direkt aus der Liste setzen/entfernen — auch bei abgerechneten
// Ferien (ändert keinen Lohn, nur die Sperrfrist-Kette).
async function absToggleFerienfaehig(employeeId, absenceId, wert) {
    const r = await fetch(`/api/employees/${employeeId}/absences/${absenceId}/ferienfaehig`, {
        method: 'PATCH', headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ wert }) });
    if (!r.ok) { const j = await r.json().catch(() => ({})); alert(j.message || ('Fehler ' + r.status)); return; }
    loadAbsenzenTab(employeeId);
}

function closeAbsenceModal() {
    window._absAfterSave = null;   // Abbruch → kein Dokument verknüpfen
    const modal = document.getElementById('absenceModal');
    if (modal) { modal.style.display = 'none'; modal.dataset.editId = ''; }
    window._absEditWorkedDays = [];
    window._absIsNew = false;
    window._absUserTouchedDays = false;
    window._absContinuationHint = '';
    window._absDienstplanBis = '';
    window._absPlanAuto = false;
    window._absCalAnker = null;
}

// Wenn Von-Datum geändert wird: Bis-Datum automatisch auf dasselbe Datum
// setzen (typischer 1-Tages-Fall). Für Mehrtages-Absenzen passt der User
// Bis danach manuell an.
function syncAbsDateTo() {
    const fromEl = document.getElementById('absDateFrom');
    const toEl   = document.getElementById('absDateTo');
    if (!fromEl || !toEl) return;
    if (!fromEl.value) return;
    toEl.value = fromEl.value;
}

async function renderAbsDayCheckboxes() {
    const from  = document.getElementById('absDateFrom').value;
    const to    = document.getElementById('absDateTo').value;
    const type  = document.getElementById('absTypeSelect').value;
    const box   = document.getElementById('absDayCheckboxes');
    if (!box) return;

    if (!from || !to || from > to) { box.innerHTML = ''; return; }

    // Neue Krank/Unfall: bei Datums-/Typ-Wechsel Fortsetzung Vormonat neu prüfen
    // (solange User kein eigenes Muster gewählt hat).
    if (window._absIsNew && (type === 'KRANK' || type === 'UNFALL') && !window._absUserTouchedDays && !_absZgModell()) {
        await _absApplyContinuationMoFr(type, from);
        // Zeitraum Bis kann länger sein als beim ersten Apply — Mo–Fr neu aufbauen.
        if (window._absContinuationHint) {
            const days = [];
            let cur = new Date(from + 'T00:00:00');
            const end = new Date(to + 'T00:00:00');
            while (cur <= end) {
                const dow = cur.getDay();
                if (dow >= 1 && dow <= 5) days.push(localIso(cur));
                cur.setDate(cur.getDate() + 1);
            }
            window._absEditWorkedDays = days;
        }
    }

    const dayNames = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];
    const preselect = window._absEditWorkedDays ?? [];

    // Alle Tage im Zeitraum aufzählen
    const days = [];
    let cur = new Date(from + 'T00:00:00');
    const end = new Date(to + 'T00:00:00');
    while (cur <= end) {
        days.push(new Date(cur));
        cur.setDate(cur.getDate() + 1);
    }

    if (type !== 'KRANK' && type !== 'UNFALL') {
        // Walter-Vorgabe 27.06.2026: Das Tage-Raster gibt es NUR bei Krankheit
        // und Unfall (dort wird im 1/5 gerechnet, also zählt welche Tage der MA
        // gearbeitet hätte). Alle anderen Typen (Ferien, Feiertag, unbezahlter
        // Urlaub, Schulung …) überspringen das Raster komplett — es zählen
        // automatisch alle Tage im Zeitraum (das Backend rechnet je nach Typ-
        // Konfiguration). Nur ein Info-Text.
        box.innerHTML = `<div class="abs-day-info">Alle ${days.length} Tag(e) im Zeitraum werden automatisch verbucht — ein Tage-Raster gibt es nur bei Krankheit/Unfall (1/5-Berechnung).</div>`;
        calcAbsHoursPreview();
        return;
    }

    if (_absZgModell() && await _absZgRaster(box, days, from, to)) return;

    // KRANK / UNFALL: Tage auswählen (1/5).
    // Walter-Vorgabe 28.05.2026: 7-Spalten-Wochenraster Mo-So. Default-
    // Vorauswahl: Mo–Fr markiert, Sa+So NICHT markiert (typische 5-Tage-Woche).
    // Walter 26.07.2026: bei fortlaufender Krankheit (Anschluss Vormonat)
    // immer Mo–Fr / Sa+So frei — nicht «erste 5 Tage ab Periodenstart».
    // Walter-Vorgabe 30.05.2026: Sa+So klickbar + Schnellauswahl:
    //   • "Mo–Fr Muster"        = Default / lange Krankheit (Sa+So frei)
    //   • "5 gearbeitet / 2 frei" = Schicht-Rhythmus ab Tag 1 (nur manuell)
    //   • "Alle Tage" / "Keine"
    const contHint = window._absContinuationHint
        ? `<div class="abs-day-info" style="margin-bottom:8px;color:#166534;background:#f0fdf4;border:1px solid #bbf7d0;padding:8px 10px;border-radius:8px;font-size:12px">${window._absContinuationHint}</div>`
        : '';
    let html = contHint
             + '<div class="abs-day-label">Welche Tage hätte der/die Mitarbeitende gearbeitet?</div>'
             + '<div class="abs-day-quick">'
             + '<button type="button" onclick="absDayPreset(\'mofr\')">Mo–Fr Muster</button>'
             + '<button type="button" onclick="absDayPreset(\'5and2\')">5 gearbeitet / 2 frei</button>'
             + '<button type="button" onclick="absDayPreset(\'all\')">Alle Tage</button>'
             + '<button type="button" onclick="absDayPreset(\'none\')">Keine</button>'
             + '</div>'
             + '<div class="abs-day-grid-head">'
             + '<div>Mo</div><div>Di</div><div>Mi</div><div>Do</div><div>Fr</div><div>Sa</div><div>So</div>'
             + '</div>'
             + '<div class="abs-day-grid">';

    // Versatz vorne: leere Zellen für die Tage vor dem ersten Datum,
    // damit der erste Eintrag in der korrekten Wochentag-Spalte landet.
    // getDay: 0=So, 1=Mo, ..., 6=Sa  →  Mo-Index: (dow+6) % 7
    const firstDow = days[0].getDay();
    const firstMondayOffset = (firstDow + 6) % 7;
    for (let i = 0; i < firstMondayOffset; i++) {
        html += '<div class="abs-day-item abs-day-empty"></div>';
    }

    days.forEach(d => {
        const iso     = localIso(d);
        const dow     = d.getDay();
        const weekday = dayNames[dow];
        const dateStr = d.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit' });
        const isSaSo  = dow === 0 || dow === 6;
        const chk = preselect.length > 0
            ? (preselect.includes(iso) ? 'checked' : '')
            : (isSaSo ? '' : 'checked');
        html += `<label class="abs-day-item${isSaSo ? ' abs-day-weekend' : ''}">
            <input type="checkbox" value="${iso}" ${chk} onchange="window._absUserTouchedDays=true;calcAbsHoursPreview()">
            <span class="abs-day-name">${weekday}</span>
            <span class="abs-day-date">${dateStr}</span>
        </label>`;
    });
    html += '</div>';
    // Legende (Walter 13.08.2026): klarmachen, welche Tage zählen.
    html += `<div style="margin-top:8px;font-size:11.5px;color:#64748b;display:flex;gap:16px;flex-wrap:wrap">
        <span><span style="display:inline-block;width:12px;height:12px;background:#6b6152;border-radius:4px;vertical-align:-2px"></span> dunkel = hätte gearbeitet → zählt (× Wochenstunden ÷ 5)</span>
        <span><span style="display:inline-block;width:12px;height:12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:4px;vertical-align:-2px"></span> hell = nicht eingeplant → zählt 0 h</span>
    </div>`;
    box.innerHTML = html;
    calcAbsHoursPreview();
}

// ── Zeitgutschrift Krank/Unfall FIX/FIX-M/MTP (Walter 05.10.2026) ──────────
// Bis «Dienstplan bekannt bis» zählt ein eingeplanter Tag Wochenstunden ÷
// Arbeitstage, ein freier Tag 0 h; danach Wochenstunden ÷ 7 pro Kalendertag.
// Die Stunden rechnet der Server — dieselbe Regel wie im Lohn.
function _absZgModell() {
    const m = (selectedEmployee?.employmentModel || '').toUpperCase();
    return m === 'FIX' || m === 'FIX-M' || m === 'MTP';
}
function _absZgTageAusDom() {
    const boxes = document.querySelectorAll('#absDayCheckboxes input[type=checkbox]');
    if (boxes.length) window._absEditWorkedDays = [...boxes].filter(cb => cb.checked).map(cb => cb.value);
}
async function _absZgVorschau() {
    const type = document.getElementById('absTypeSelect').value;
    const from = document.getElementById('absDateFrom').value;
    const to   = document.getElementById('absDateTo').value;
    if (!from || !to || from > to) return null;
    _absZgTageAusDom();
    let prozent = Number(document.getElementById('absProzent')?.value ?? 100);
    if (!Number.isFinite(prozent) || prozent <= 0) prozent = 100;
    try {
        const res = await fetch('/api/absences/zeitgutschrift-vorschau', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                employeeId: selectedEmployeeId, absenceType: type, dateFrom: from, dateTo: to,
                dienstplanBis: window._absDienstplanBis || null,
                workedDays: JSON.stringify(window._absEditWorkedDays || []),
                prozent: Math.min(prozent, 100),
            }),
        });
        return res.ok ? await res.json() : null;
    } catch { return null; }
}
function _absZgPlanSetzen(wert, folgtBis = false) {
    _absZgTageAusDom();
    window._absPlanAuto = folgtBis;
    window._absDienstplanBis = wert || '';
    window._absUserTouchedDays = true;
    renderAbsDayCheckboxes();
}
/** Raster «Dienstplan bekannt bis». false = Vertrag am Datum ist nicht FIX/MTP → altes Raster. */
async function _absZgRaster(box, days, from, to) {
    _absZgTageAusDom();
    let plan = window._absPlanAuto ? to : (window._absDienstplanBis || '');
    if (plan && plan < from) plan = '';
    if (plan && plan > to) plan = to;
    window._absDienstplanBis = plan;

    const v = await _absZgVorschau();
    if (!v || !v.betrifft) return false;
    const n2 = x => Number(x).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const w = Number(v.wochenstunden) || 0;

    if (v.methode !== 'DIENSTPLAN_1_7') {
        const txt = v.methode === 'MO_FR_1_5'
            ? `Mo–Fr je ${n2(w / 5)} h (Wochenstunden ÷ 5), Sa/So 0 h`
            : `jeder Kalendertag ${n2(w / 7)} h (Wochenstunden ÷ 7)`;
        box.innerHTML = `<div class="abs-day-info">Die Filiale rechnet Krankheit und Unfall ohne Dienstplan: ${txt}.</div>`;
        calcAbsHoursPreview();
        return true;
    }

    const at = Number(v.arbeitstage) || 5;
    const atText = at.toLocaleString('de-CH', { maximumFractionDigits: 1 });
    let html = `<div class="abs-day-label">Dienstplan bekannt bis</div>
        <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-bottom:8px">
            <input type="date" id="absDienstplanBis" class="ma-input" style="max-width:170px" min="${from}" value="${plan}" onchange="_absZgPlanSetzen(this.value)">
            <div class="abs-day-quick" style="margin:0">
                <button type="button" onclick="_absZgPlanSetzen('${to}', true)">Plan deckt ganze Absenz</button>
                <button type="button" onclick="_absZgPlanSetzen('')">Kein Dienstplan</button>
            </div>
        </div>`;
    if (!plan) {
        html += `<div class="abs-day-info">Ohne Dienstplan zählt jeder Kalendertag ${n2(w / 7)} h (Wochenstunden ÷ 7). Ist der Dienstplan bekannt: Datum eintragen und die eingeplanten Tage ankreuzen.</div>`;
        box.innerHTML = html;
        calcAbsHoursPreview();
        return true;
    }

    const dayNames = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];
    const preselect = window._absEditWorkedDays ?? [];
    html += '<div class="abs-day-label">An welchen Tagen war der/die Mitarbeitende eingeplant?</div>'
          + '<div class="abs-day-quick">'
          + '<button type="button" onclick="absDayPreset(\'mofr\')">Mo–Fr Muster</button>'
          + '<button type="button" onclick="absDayPreset(\'5and2\')">5 gearbeitet / 2 frei</button>'
          + '<button type="button" onclick="absDayPreset(\'all\')">Alle Tage</button>'
          + '<button type="button" onclick="absDayPreset(\'none\')">Keine</button>'
          + '</div>'
          + '<div class="abs-day-grid-head"><div>Mo</div><div>Di</div><div>Mi</div><div>Do</div><div>Fr</div><div>Sa</div><div>So</div></div>'
          + '<div class="abs-day-grid">';
    const offset = (days[0].getDay() + 6) % 7;
    for (let i = 0; i < offset; i++) html += '<div class="abs-day-item abs-day-empty"></div>';
    days.forEach(d => {
        const iso = localIso(d);
        const dow = d.getDay();
        const dateStr = d.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit' });
        const isSaSo = dow === 0 || dow === 6;
        if (iso <= plan) {
            html += `<label class="abs-day-item${isSaSo ? ' abs-day-weekend' : ''}">
                <input type="checkbox" value="${iso}" ${preselect.includes(iso) ? 'checked' : ''} onchange="window._absUserTouchedDays=true;calcAbsHoursPreview()">
                <span class="abs-day-name">${dayNames[dow]}</span>
                <span class="abs-day-date">${dateStr}</span>
            </label>`;
        } else {
            html += `<div class="abs-day-item" title="Nach dem Dienstplan: Wochenstunden ÷ 7" style="opacity:.55;background:#eceae4;cursor:default">
                <span class="abs-day-name">${dayNames[dow]}</span>
                <span class="abs-day-date">${dateStr}</span>
                <span style="font-size:10px;color:#646464">1/7</span>
            </div>`;
        }
    });
    html += '</div>';
    html += `<div style="margin-top:8px;font-size:11.5px;color:#64748b;display:flex;gap:16px;flex-wrap:wrap">
        <span><span style="display:inline-block;width:12px;height:12px;background:#6b6152;border-radius:4px;vertical-align:-2px"></span> eingeplant → ${n2(w / at)} h (Wochenstunden ÷ ${atText} Arbeitstage)</span>
        <span><span style="display:inline-block;width:12px;height:12px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:4px;vertical-align:-2px"></span> frei → 0 h</span>
        <span><span style="display:inline-block;width:12px;height:12px;background:#eceae4;border-radius:4px;vertical-align:-2px"></span> 1/7 = nach dem Dienstplan → ${n2(w / 7)} h</span>
    </div>`;
    box.innerHTML = html;
    calcAbsHoursPreview();
    return true;
}

// Walter-Vorgabe 30.05.2026: Schnellauswahl-Muster für die Krank-/Unfall-
// Tage-Auswahl. Sa+So sind in der Gastro normale Arbeitstage, daher braucht
// es flexible Vorlagen.
//   • mofr   = Mo–Fr markiert, Sa+So frei (Default für klassische 5-Tage-Woche)
//   • 5and2  = ab erstem Tag: 5 gearbeitet / 2 frei wiederholend (unabhängig
//              vom Wochentag — passend für Schichtbetriebe mit festem Rhythmus)
//   • all    = alle Tage markiert (7-Tage-Woche)
//   • none   = alle Tage abgewählt (User klickt manuell)
function absDayPreset(mode) {
    window._absUserTouchedDays = true;
    window._absContinuationHint = '';
    const boxes = document.querySelectorAll('#absDayCheckboxes input[type=checkbox]');
    let idx = 0;  // läuft nur über echte Tage (nicht über die abs-day-empty-Spacer)
    boxes.forEach(cb => {
        const iso = cb.value;
        if (!iso) return;
        const d = new Date(iso + 'T00:00:00');
        const dow = d.getDay();  // 0=So, 1=Mo, ..., 6=Sa
        if (mode === 'mofr') {
            cb.checked = (dow >= 1 && dow <= 5);
        } else if (mode === '5and2') {
            // Position im 7-Tage-Zyklus: Tage 1-5 markiert, Tage 6-7 frei
            // Achtung: startet am Periodenanfang — bei Monatswechsel/Fortsetzung
            // lieber «Mo–Fr Muster» nutzen (Walter 26.07.2026).
            cb.checked = (idx % 7) < 5;
        } else if (mode === 'all') {
            cb.checked = true;
        } else if (mode === 'none') {
            cb.checked = false;
        }
        idx++;
    });
    if (typeof calcAbsHoursPreview === 'function') calcAbsHoursPreview();
}

function getAbsWorkedDays() {
    const type = document.getElementById('absTypeSelect').value;
    const from = document.getElementById('absDateFrom').value;
    const to   = document.getElementById('absDateTo').value;

    // Walter-Vorgabe 27.06.2026: NUR Krankheit/Unfall nutzen das Tage-Raster
    // (angekreuzte Tage, 1/5). ALLE anderen Typen (Ferien, Feiertag, unbezahlter
    // Urlaub, Schulung …) zählen automatisch alle Tage im Zeitraum.
    if (type !== 'KRANK' && type !== 'UNFALL') {
        const days = [];
        if (from && to && from <= to) {
            let cur = new Date(from + 'T00:00:00');
            const end = new Date(to + 'T00:00:00');
            while (cur <= end) {
                days.push(localIso(cur));
                cur.setDate(cur.getDate() + 1);
            }
        }
        return days;
    }

    // KRANK / UNFALL: angekreuzte Tage
    return [...document.querySelectorAll('#absDayCheckboxes input[type=checkbox]:checked')]
        .map(cb => cb.value);
}

async function calcAbsHoursPreview() {
    const type     = document.getElementById('absTypeSelect').value;
    const empModel = selectedEmployee?.employmentModel ?? '';
    const previewEl = document.getElementById('absHoursPreview');
    if (!previewEl) return;

    if ((type === 'KRANK' || type === 'UNFALL') && _absZgModell()) {
        const seq = (window._absZgSeq = (window._absZgSeq || 0) + 1);
        const v = await _absZgVorschau();
        if (seq !== window._absZgSeq) return;
        if (v && v.betrifft) {
            const n2 = x => Number(x).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            const at = Number(v.arbeitstage).toLocaleString('de-CH', { maximumFractionDigits: 1 });
            const pz = Number(document.getElementById('absProzent')?.value ?? 100);
            const pzText = Number.isFinite(pz) && pz > 0 && pz < 100 ? ` · bei ${pz} % Ausfall` : '';
            const tagessoll = Number(v.wochenstunden) / (Number(v.arbeitstage) > 0 ? Number(v.arbeitstage) : 5);
            const basisText = v.hatArbeitstage
                ? `Tagessoll ${n2(tagessoll)} h = ${n2(v.wochenstunden)} h ÷ ${at} Arbeitstage pro Woche `
                  + (v.arbeitstageVonHand ? '(am Vertrag eingetragen)' : '(Vorschlag aus dem Pensum — stimmt das? Sonst im Vertrag bearbeiten unter «Arbeitstage / Woche» ändern)')
                : `Tagessoll ${n2(tagessoll)} h = Garantie ${n2(v.wochenstunden)} h ÷ 5`;
            previewEl.innerHTML = `<span class="abs-hours-pos">+${n2(v.stunden)} h</span> <span class="abs-hours-label">${esc(v.erklaerung || 'keine Tage')}${pzText}</span>`
                + `<br><span class="abs-hours-label">${esc(basisText)}</span>`;
            previewEl.dataset.hours = Number(v.stunden).toFixed(2);
            previewEl.dataset.nachtKompOver9 = '';
            return;
        }
    }

    const workedDays = getAbsWorkedDays();
    const count      = workedDays.length;

    // Ausfall-Prozent (Default 100). Wird multiplikativ auf die Stunden
    // angewendet — 50 = halb krank/abwesend, etc.
    let prozent = Number(document.getElementById('absProzent')?.value ?? 100);
    if (!Number.isFinite(prozent) || prozent <= 0) prozent = 100;
    if (prozent > 100) prozent = 100;
    const pFactor   = prozent / 100;
    const pSuffix   = prozent < 100 ? ` × ${prozent}%` : '';

    // Konfiguration für diesen Absenz-Typ aus Cache laden
    const typen = await getAbsenzTypen();
    const typCfg = typen.find(t => t.code === type);
    let modus            = typCfg?.gutschriftModus ?? '1/5';
    let hatGutschrift    = typCfg?.zeitgutschrift  ?? true;
    const utpAuszahlung  = typCfg?.utpAuszahlung   ?? false;
    const basisStunden   = typCfg?.basisStunden    ?? 'BETRIEB';
    const reduziertSaldo = typCfg?.reduziertSaldo  ?? null;

    // FIX/FIX-M bei FEIERTAG: AbsenzTyp-Konfig (gutschriftModus aus
    // Datenbank) wird jetzt respektiert — vorher hartkodiert auf 1/7,
    // war Legacy-Regel die obsolete ist seit der Saldo-Mechanik. Zusätzlich
    // erscheint im Hinweis der Saldo-Bezug (Feiertag-Saldo wird im
    // PayrollController-Block automatisch um die bezogenen Tage reduziert).

    // MTP/UTP bei FEIERTAG: keine Zeitgutschrift, kein Saldo. Beide sind
    // stundenbasierte Modelle (UTP rein nach gestempelten Stunden, MTP mit
    // garantiertem Mindestpensum). Feiertagentschädigung wird als % auf den
    // Stundenlohn berechnet und jeden Monat mit dem Lohn ausbezahlt — daher
    // kein Eintrag in den Arbeitsstunden-Saldo und kein Feiertag-Tage-Saldo.
    if ((empModel === 'MTP' || empModel === 'FLEX' || empModel === 'UTP') && type === 'FEIERTAG') {
        previewEl.innerHTML = `<span class="abs-hours-label">${empModel}: nur Info — keine Wirkung auf Lohn oder Stunden. Der Feiertag ist über die Feiertagsentschädigung (% pro Stunde) bezahlt.</span>`;
        previewEl.dataset.hours = '0';
        return;
    }

    // MTP und UTP bei FERIEN: KEINE Zeitgutschrift. Stattdessen:
    //   - MTP: Garantie-Festlohn (10.5) wird um die Ferientage gekürzt;
    //          Ferien-Auszahlung anteilig aus Ferien-Geld-Saldo (CHF).
    //   - FLEX: Auszahlung anteilig aus Ferien-Geld-Saldo (CHF) —
    //          sofern der Firmenparameter "Feriengeld auf Konto"
    //          aktiv ist (sonst wird Ferien mit Stundenlohn ausbezahlt).
    //   Beide: Ferien-Tage-Saldo wird um die bezogenen Ferientage reduziert.
    if ((empModel === 'MTP' || empModel === 'FLEX') && type === 'FERIEN') {
        const modellInfo = empModel === 'MTP'
            ? 'Festlohn wird um diese Tage gekürzt, Auszahlung aus Ferien-Geld-Saldo (CHF)'
            : 'Auszahlung anteilig aus Ferien-Geld-Saldo (CHF)';
        previewEl.innerHTML = `<span class="abs-hours-label">${empModel}: ${count} Ferientag${count > 1 ? 'e' : ''} — ${modellInfo}. Ferien-Tage-Saldo -${count}. (Keine Stunden-Gutschrift)</span>`;
        previewEl.dataset.hours = '0';
        return;
    }

    // Wochenstunden-Basis pro AbsenzTyp (CH-Payroll-Regel):
    //   BETRIEB = Filial-NormalWeeklyHours (42 h Default)
    //   VERTRAG = Modell + Typ-abhängig:
    //     MTP       → GuaranteedHoursPerWeek (z.B. 33 h/Woche)
    //                  → alle Typen: Krank/Unfall/Ferien-Gutschrift basieren
    //                    auf der Garantie (nicht auf Betriebs-Wochen).
    //     FIX/FIX-M → Spezialregel:
    //                  FERIEN und FEIERTAG: pensum-adjustiertes Wochensoll
    //                    (1/7 × WeeklyHours bzw. betriebWeekly × Pensum/100)
    //                  KRANK und UNFALL: volle Betriebs-Wochen (1/5 × 42h)
    //                    — entspricht Walter's Vorgabe:
    //                    "bei FIX und FIX-M immer 1/5 der Betriebszeit"
    //     UTP       → Betrieb (Fallback; Gutschrift bei UTP ist selten)
    const betriebWeekly = Number(selectedCompanyProfile?.normalWeeklyHours ?? 42);
    let weeklyH = betriebWeekly;
    // Walter-Vorgabe 30.05.2026 (override): bei MTP IMMER die garantierten
    // Wochenstunden als Basis — unabhängig vom AbsenzTyp-Setting basisStunden.
    // Stundenlöhner-Modell: nur die Garantie ist der Maßstab, nicht der Betrieb.
    if (empModel === 'MTP') {
        weeklyH = Number(selectedEmployee?.guaranteedHoursPerWeek
                      ?? selectedEmployee?.weeklyHours
                      ?? betriebWeekly);
    } else if (basisStunden === 'VERTRAG') {
        if (empModel === 'FIX' || empModel === 'FIX-M') {
            // NUR bei FERIEN/FEIERTAG pensum-adjustiert, sonst Betrieb.
            if (type === 'FERIEN' || type === 'FEIERTAG') {
                const pct = Number(selectedEmployee?.employmentPercentage ?? 100);
                weeklyH = Number(selectedEmployee?.weeklyHours
                              ?? (betriebWeekly * pct / 100));
            }
            // KRANK, UNFALL, SCHULUNG, MILITAER: weeklyH bleibt auf betriebWeekly
        }
        // FLEX: bleibt auf betriebWeekly
    }

    let hours = 0;
    let hint  = '';

    // Nacht-Kompensation = bezahlter Freitag (1/5 Wochenstunden), für ALLE
    // Modelle inkl. FLEX. Nachtzuschlag selbst wird nie ausbezahlt (nur Saldo);
    // Ausnahme Austritt. Walter 02.08.2026.
    if (reduziertSaldo === 'NACHT_STUNDEN') {
        hours = count * (weeklyH / 5) * pFactor;
        const saldoHint = empModel === 'FLEX'
            ? 'bezahlter Freitag als Stundenlohn, Nacht-Saldo sinkt'
            : 'bezahlter Freitag (Ist-Stunden), Nacht-Saldo sinkt';
        hint  = `<span class="abs-hours-pos">+${hours.toFixed(2)} h</span> <span class="abs-hours-label">${typCfg?.bezeichnung ?? type}: ${count} Tag${count>1?'e':''} × ${weeklyH.toFixed(2)} h ÷ 5${pSuffix} → ${saldoHint}</span>`;
        // Warnung ab > 9 h (mehr als ein typischer Komp-Tag bei 42h-Woche).
        if (hours > 9) {
            hint += `<br><span class="abs-hours-label" style="color:#b45309;font-weight:600">⚠ Mehr als 9 Stunden Kompensation — üblich ist 1 bezahlter Freitag (1/5 Wochenarbeitszeit). Bitte prüfen.</span>`;
        }
        previewEl.innerHTML = hint;
        previewEl.dataset.hours = hours.toFixed(2);
        previewEl.dataset.nachtKompOver9 = hours > 9 ? '1' : '';
        return;
    }

    // FLEX: nur Typen mit UtpAuszahlung-Flag bekommen etwas
    if (empModel === 'FLEX' && !utpAuszahlung) {
        previewEl.innerHTML = '<span class="abs-hours-label">FLEX: keine automatische Stundengutschrift für diesen Typ — kann pro Absenz-Typ aktiviert werden (Systemeinstellungen → Absenz-Typen → „UTP als Stundenlohn auszahlen")</span>';
        previewEl.dataset.hours = '0';
        previewEl.dataset.nachtKompOver9 = '';
        return;
    }

    if (type === 'UNBEZ_URLAUB') {
        // Walter-Vorgabe 27.06.2026: Unbezahlter Urlaub wird NICHT ausbezahlt.
        // Im Lohnlauf wird stattdessen der Festlohn (FIX/FIX-M, Tagessatz 12/365)
        // bzw. die garantierten Soll-Stunden (MTP, 1/7) um diese Tage gekürzt;
        // bei UTP wirkt es automatisch (nur gestempelte Stunden werden bezahlt).
        hours = 0;
        hint  = `<span class="abs-hours-label">Unbezahlter Urlaub: ${count} Tag${count>1?'e':''} — keine Auszahlung. Im Lohnlauf wird der Festlohn (FIX/FIX-M) bzw. die Soll-Stunden (MTP) um diese Tage gekürzt.</span>`;
    } else if (!hatGutschrift) {
        // Kein Zeitgutschrift → Ausbezahlung
        hours = count * (weeklyH / 5) * pFactor;
        hint  = `<span class="abs-hours-label">${typCfg?.bezeichnung ?? type}: keine Zeitgutschrift, wird ausbezahlt (${count} Tag${count>1?'e':''} × ${weeklyH.toFixed(2)} h ÷ 5${pSuffix})</span>`;
    } else if (modus === '1/7') {
        hours = count * (weeklyH / 7) * pFactor;
        // Immer Gutschrift — die Stunden werden dem Arbeitszeit-Saldo
        // hinzugerechnet, damit Ferien/ähnliche Tage nicht als Minusstunden
        // erscheinen. Bei MTP basiert die Berechnung auf den garantierten
        // Vertragsstunden (33h), bei FIX/FIX-M/UTP auf der betrieblichen
        // Wochenarbeitszeit (42h) — geregelt über absenz_typ.basis_stunden.
        hint = `<span class="abs-hours-pos">+${hours.toFixed(2)} h</span> <span class="abs-hours-label">Gutschrift (${count} Tage × ${weeklyH.toFixed(2)} h ÷ 7${pSuffix})</span>`;
    } else {
        // 1/5 (Standard)
        hours = count * (weeklyH / 5) * pFactor;
        hint  = `<span class="abs-hours-pos">+${hours.toFixed(2)} h</span> <span class="abs-hours-label">Gutschrift (${count} Tag${count>1?'e':''} × ${weeklyH.toFixed(2)} h ÷ 5${pSuffix})</span>`;
    }

    // FEIERTAG bei FIX/FIX-M: zusätzlicher Hinweis dass der Feiertag-Saldo
    // um die bezogenen Tage reduziert wird (zusätzlich zur Stunden-Gutschrift).
    if (type === 'FEIERTAG' && (empModel === 'FIX' || empModel === 'FIX-M')) {
        hint += `<br><span class="abs-hours-label" style="color:#b45309">→ Feiertag-Saldo wird um ${count} Tag${count>1?'e':''} reduziert</span>`;
    }
    // FERIEN für FIX/FIX-M: zusätzlich Ferien-Tage-Saldo-Reduktion erwähnen
    // (UTP/MTP wurde oben schon per early-return abgefangen).
    if (type === 'FERIEN' && (empModel === 'FIX' || empModel === 'FIX-M')) {
        hint += `<br><span class="abs-hours-label" style="color:#15803d">→ Ferien-Tage-Saldo wird um ${count} Tag${count>1?'e':''} reduziert</span>`;
    }

    previewEl.innerHTML = hint;
    previewEl.dataset.hours = hours.toFixed(2);
    previewEl.dataset.nachtKompOver9 = '';
}

async function saveAbsence() {
    const editId   = document.getElementById('absenceModal').dataset.editId;
    const type     = document.getElementById('absTypeSelect').value;
    const dateFrom = document.getElementById('absDateFrom').value;
    const dateTo   = document.getElementById('absDateTo').value;
    const notes    = document.getElementById('absNotes').value.trim();

    if (!dateFrom || !dateTo || dateFrom > dateTo) {
        alert('Bitte gültigen Zeitraum eingeben.');
        return;
    }

    // Soft-Validierung: bei Neu-Erfassung innerhalb der aktiven Lohnperiode bleiben.
    // Beim Edit nicht prüfen — bestehende Absenzen können älter als die heutige
    // Periode sein.
    if (!editId) {
        const fromInput = document.getElementById('absDateFrom');
        const toInput   = document.getElementById('absDateTo');
        const min = fromInput?.min || '';
        const max = fromInput?.max || toInput?.max || '';
        if (min && (dateFrom < min || dateTo < min)) {
            alert(`Datum liegt vor dem Lohnperiode-Beginn (${min}). Bitte ein Datum innerhalb der aktiven Periode wählen.`);
            return;
        }
        if (max && (dateFrom > max || dateTo > max)) {
            alert(`Datum liegt nach dem Lohnperiode-Ende (${max}). Bitte ein Datum innerhalb der aktiven Periode wählen.`);
            return;
        }
    }

    // Überlappung: Client-Warnung vor dem Speichern (Server blockt zusätzlich hart).
    const overlap = findAbsenceOverlap(selectedEmployeeId, dateFrom, dateTo, editId || null);
    if (overlap) {
        alert(formatAbsenceOverlapMsg(overlap));
        return;
    }

    const workedDays   = getAbsWorkedDays();
    const hoursPreview = document.getElementById('absHoursPreview');
    const hours        = parseFloat(hoursPreview?.dataset.hours ?? '0');

    // Walter 02.08.2026: Nacht-Komp. > 9 h → Warnung (üblich = 1 bezahlter Freitag).
    if ((type === 'NACHT_KOMP' || hoursPreview?.dataset.nachtKompOver9 === '1') && hours > 9) {
        const ok = confirm(
            `Nacht-Kompensation umfasst ${hours.toFixed(2)} h (mehr als 9 h).\n\n` +
            `Üblich ist ein bezahlter Freitag = 1/5 der Wochenarbeitszeit. ` +
            `Der Nachtzuschlag selbst wird nicht ausbezahlt (nur über Komp. / bei Austritt).\n\n` +
            `Trotzdem speichern?`
        );
        if (!ok) return;
    }

    let prozent = Number(document.getElementById('absProzent')?.value ?? 100);
    if (!Number.isFinite(prozent) || prozent <= 0) prozent = 100;
    if (prozent > 100) prozent = 100;

    const payload = {
        employeeId:    selectedEmployeeId,
        absenceType:   type,
        dateFrom,
        dateTo,
        workedDays:    JSON.stringify(workedDays),
        hoursCredited: hours,
        prozent,
        notes,
        ferienfaehig:  type === 'FERIEN' && !!document.getElementById('absFerienfaehig')?.checked,
        dienstplanBis: (type === 'KRANK' || type === 'UNFALL') && _absZgModell() ? (window._absDienstplanBis || null) : null,
    };

    try {
        const url    = editId ? `/api/absences/${editId}` : '/api/absences';
        const method = editId ? 'PUT' : 'POST';
        const res    = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
        });
        // Lohnlauf-Sperre? Zeigt Toast und bricht ab.
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            let msg = 'Fehler beim Speichern.';
            try {
                const j = await res.json();
                if (j.error === 'ABSENCE_OVERLAP' && j.message) msg = j.message;
                else if (j.message) msg = j.message;
            } catch {}
            alert(msg);
            return;
        }
        // Dokument aus dem Verknüpfen-Dialog (Walter 23.09.2026): nach dem
        // Anlegen einer NEUEN Absenz das hochgeladene Arztzeugnis daran hängen.
        const afterSave = !editId ? window._absAfterSave : null;
        window._absAfterSave = null;
        if (afterSave) {
            let neu = null;
            try { neu = await res.json(); } catch {}
            if (neu?.id) { try { await afterSave(neu.id); } catch (e) { console.error('absAfterSave', e); } }
        }
        closeAbsenceModal();
        loadAbsenzenTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// Walter-Vorgabe 09.06.2026: ⋮-Menü-Toggle für alle MA-Listen (Absenz, Permit,
// QST, …). Generischer Helper mit ID-Prefix, damit Menüs aus verschiedenen
// Tabellen sich nicht in die Quere kommen.
// McBook-Fix 27.07.2026: Menü wie dokToggleMenu an body + position:fixed —
// sonst clipped overflow:hidden in .emp-detail / .abs-list die Popups.
function rowMenuCloseAll() {
    document.querySelectorAll('.dok-menu.show').forEach(m => {
        m.classList.remove('show', 'up');
        m.style.position = '';
        m.style.top = '';
        m.style.left = '';
        m.style.right = '';
        m.style.bottom = '';
        m.style.removeProperty('z-index');
        const orig = m._rowOrigParent;
        if (orig && orig !== m.parentElement && orig.isConnected) {
            orig.appendChild(m);
        }
        m._rowOrigParent = null;
    });
}
function rowMenuToggle(event, prefix, id) {
    event.stopPropagation();
    const menu = document.getElementById(`${prefix}Menu-${id}`);
    const btn = event.currentTarget || event.target?.closest('button');
    const wasOpen = menu?.classList.contains('show');
    rowMenuCloseAll();
    if (typeof dokCloseAllMenus === 'function') {
        try { dokCloseAllMenus(); } catch { /* optional */ }
    }
    if (wasOpen || !menu || !btn) return;

    if (menu.parentElement !== document.body) {
        menu._rowOrigParent = menu.parentElement;
        document.body.appendChild(menu);
    }
    menu.style.position = 'fixed';
    // Über jedem Fenster (Familien-Fenster z-index 2000; CSS setzt 400 !important).
    menu.style.setProperty('z-index', '9700', 'important');
    menu.style.right = 'auto';
    menu.style.bottom = 'auto';
    menu.style.left = '-9999px';
    menu.style.top = '0';
    menu.classList.add('show');

    const btnRect = btn.getBoundingClientRect();
    const menuW = menu.offsetWidth;
    const menuH = menu.offsetHeight;
    const margin = 6;
    let top = btnRect.bottom + 4;
    if (top + menuH > window.innerHeight - margin) {
        top = Math.max(margin, btnRect.top - menuH - 4);
    }
    let left = btnRect.right - menuW;
    if (left < margin) left = margin;
    if (left + menuW > window.innerWidth - margin) {
        left = window.innerWidth - menuW - margin;
    }
    menu.style.top = top + 'px';
    menu.style.left = left + 'px';

    setTimeout(() => {
        document.addEventListener('click', rowMenuCloseAll, { once: true });
        window.addEventListener('scroll', rowMenuCloseAll, { once: true, capture: true });
        window.addEventListener('resize', rowMenuCloseAll, { once: true });
    }, 10);
}
function absToggleMenu(event, id)   { rowMenuToggle(event, 'abs',    id); }

// ══════════════════════════════════════════════════════════════════════
// Absenzbedingte Ferienkürzung (Walter-Vorgabe 23.09.2026)
// Bewusster HR-Eintrag statt Häkchen im Lohnlauf. Eigene Tabelle
// ferien_kuerzung — in der Absenzen-Liste als Zeile. Rechnung serverseitig
// (L-GAV: ⌊Tage/30⌋−1 ab 60 Tagen, Krank/Unfall/Militär/Zivilschutz, ohne
// Ferien). Vorschlag = ganze Tage abgerundet, genauer Betrag erlaubt, nie mehr.
// Dienstjahr (dessen Krankheitstage) und Buchungsdatum (Lohnlauf-Monat) sind
// getrennt — die Kürzung fürs abgelaufene Dienstjahr wird heute gebucht.
// ══════════════════════════════════════════════════════════════════════
function fkToggleMenu(event, id) { rowMenuToggle(event, 'fk', id); }

function _fkRowHtml(employeeId, k) {
    const tageTxt = k.verzicht ? 'nicht gekürzt' : `${Number(k.tage).toFixed(2).replace(/\.00$/, '')} Tage`;
    const stdTxt  = k.verzicht
        ? '<span class="abs-hours-label">bewusst verzichtet</span>'
        : `<span class="abs-hours-neg">−${Number(k.tage).toFixed(2)} Tage</span> <span class="abs-hours-label">Ferien</span>`;
    const notiz = `Dienstjahr ab ${_absDatumKurz(k.dienstjahrVon)}${k.bemerkung ? ' · ' + esc(k.bemerkung) : ''}`;
    const kJson = JSON.stringify(k).replace(/'/g, '&#39;');
    return `<tr>
        <td><span class="abs-type-badge" style="background:#fef3c7;color:#92400e;border:1px solid #fcd34d">Ferienkürzung</span></td>
        <td style="white-space:nowrap">${_absDatumKurz(k.datum)}</td>
        <td style="white-space:nowrap;color:#475569">${tageTxt}</td>
        <td>${stdTxt}</td>
        <td class="abs-notes" title="${esc(notiz)}">${notiz}</td>
        <td class="abs-actions">
            <div class="dok-menu-wrap">
                <button type="button" class="dok-menu-btn dok-menu-btn-soft" onclick="fkToggleMenu(event, ${k.id})" title="Aktionen" aria-label="Aktionen"><span class="dok-menu-dots" aria-hidden="true"></span></button>
                <div class="dok-menu" id="fkMenu-${k.id}">
                    <button class="dok-menu-item" onclick='openFerienKuerzungModal(${employeeId}, ${kJson})'>Bearbeiten</button>
                    <button class="dok-menu-item danger" onclick="fkLoeschen(${employeeId}, ${k.id})">Löschen</button>
                </div>
            </div>
        </td>
    </tr>`;
}

async function fkLoeschen(employeeId, id) {
    if (!(await liquidConfirm('Diese Ferienkürzung löschen? Der Ferien-Saldo wird im nächsten Lohnlauf wieder erhöht.',
            { title: 'Ferienkürzung löschen', yesLabel: 'Löschen', noLabel: 'Abbrechen' }))) return;
    const r = await fetch(`/api/absences/ferienkuerzung/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) { const j = await r.json().catch(() => ({})); alert(j.message || ('Fehler ' + r.status)); return; }
    loadAbsenzenTab(employeeId);
}

async function openFerienKuerzungModal(empId, eintrag, dienstjahrVon) {
    if (!empId) return;
    const iso = d => d.toISOString().slice(0, 10);
    const heute = new Date();
    const vorJahr = new Date(heute); vorJahr.setFullYear(heute.getFullYear() - 1);
    const q = eintrag ? `&ausserId=${eintrag.id}` : '';
    let infos = [];
    try {
        infos = await Promise.all([iso(heute), iso(vorJahr)].map(d =>
            fetch(`/api/absences/employee/${empId}/ferienkuerzung-info?datum=${d}${q}`, { headers: ah() }).then(r => r.ok ? r.json() : null)));
    } catch {}
    infos = infos.filter(i => i && i.dienstjahrVon && i.dienstjahrVon !== '0001-01-01');
    if (!infos.length) { alert('Ohne Eintrittsdatum lässt sich das Dienstjahr nicht bestimmen.'); return; }
    // Vorwahl: beim Bearbeiten das Dienstjahr des Eintrags, sonst das erste mit «noch möglich».
    const wunschDj = eintrag?.dienstjahrVon || dienstjahrVon;
    let idx = wunschDj ? Math.max(0, infos.findIndex(i => i.dienstjahrVon === wunschDj))
                       : Math.max(0, infos.findIndex(i => Number(i.nochMoeglich) > 0));
    const d = t => _absDatumKurz(t);
    const num = n => Number(n || 0).toFixed(2);

    document.getElementById('fkOverlay')?.remove();
    const wrap = document.createElement('div');
    wrap.id = 'fkOverlay';
    wrap.style.cssText = 'position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9800;display:flex;align-items:center;justify-content:center';
    wrap.innerHTML = `
    <div class="modal" style="max-width:520px;width:94%;padding:22px 24px;border-radius:16px">
        <div style="font-size:15px;font-weight:800;color:#3f3f3f;margin-bottom:10px">Absenzbedingte Ferienkürzung</div>
        <label style="display:block;font-size:12px;font-weight:700;color:#8b8b8b;margin-bottom:4px">Dienstjahr</label>
        <select id="fkDj" class="ma-select" style="width:100%">
            ${infos.map((i, n) => `<option value="${n}" ${n === idx ? 'selected' : ''}>${d(i.dienstjahrVon)} – ${d(i.dienstjahrBis)}${n === 0 ? ' (laufend)' : ''}</option>`).join('')}
        </select>
        <div id="fkInfo" style="margin-top:10px;padding:10px 12px;border-radius:12px;background:rgba(254,243,199,0.55);border:1px solid rgba(217,119,6,0.35);font-size:12.5px;color:#3f3f3f;line-height:1.55"></div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:12px">
            <div><label style="display:block;font-size:12px;font-weight:700;color:#8b8b8b;margin-bottom:4px">Gebucht per (Lohnlauf-Monat)</label>
                 <input type="date" id="fkDatum" class="ma-input" value="${eintrag?.datum || iso(heute)}"></div>
            <div><label style="display:block;font-size:12px;font-weight:700;color:#8b8b8b;margin-bottom:4px">Tage kürzen</label>
                 <input type="number" id="fkTage" class="ma-input" step="0.01" min="0"></div>
        </div>
        <div style="margin-top:10px"><label style="display:block;font-size:12px;font-weight:700;color:#8b8b8b;margin-bottom:4px">Bemerkung (optional)</label>
             <input type="text" id="fkBem" class="ma-input" value="${esc(eintrag?.bemerkung || '')}" placeholder="z.B. mitgeteilt am …"></div>
        <div id="fkStatus" style="font-size:12px;color:#b91c1c;margin-top:8px"></div>
        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:14px;flex-wrap:wrap">
            <button id="fkAbbr" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 16px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
            <button id="fkVerzicht" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 16px;cursor:pointer;font-size:13.5px;font-weight:700">Nicht kürzen</button>
            <button id="fkOk" style="background:#1a1a1a;color:#fff;border:none;border-radius:12px;padding:9px 16px;cursor:pointer;font-size:13.5px;font-weight:700">Kürzen</button>
        </div>
    </div>`;
    document.body.appendChild(wrap);
    const close = () => wrap.remove();
    wrap.addEventListener('click', e => { if (e.target === wrap) close(); });
    wrap.querySelector('#fkAbbr').onclick = close;

    const zeigeInfo = (setzeTage) => {
        const i = infos[idx];
        const noch = Number(i.nochMoeglich || 0);
        wrap.querySelector('#fkInfo').innerHTML = `
            Krankheit/Unfall/Militär/Zivilschutz: <b>${Number(i.tageKrankUnfall).toFixed(1)} Tage</b> <span style="color:#8b8b8b">(gewichtet, ohne Ferien)</span><br>
            Kürzung möglich: <b>${i.zwoelftel}/12</b> von ${i.jahresFerienTage} Tagen = <b>${num(i.gesamtTage)} Tage</b>
            ${Number(i.bereitsGekuerzt) > 0 ? ` · bereits gekürzt ${num(i.bereitsGekuerzt)}` : ''}<br>
            <b>Noch möglich: ${num(noch)} Tage</b> · Vorschlag abgerundet: <b>${Number(i.vorschlagGanzeTage)} Tage</b>
            ${i.verzichtet ? '<br><span style="color:#8b8b8b">Für dieses Dienstjahr wurde bereits «nicht kürzen» gewählt.</span>' : ''}
            ${i.laufend ? '<br><span style="color:#92400e">Dienstjahr läuft noch — Stand bis Ende des aktuellen Monats. Normalerweise wird erst nach Ende des Dienstjahres oder beim Austritt gekürzt.</span>' : ''}`;
        const t = wrap.querySelector('#fkTage');
        t.max = String(noch);
        if (setzeTage) t.value = eintrag && !eintrag.verzicht && infos[idx].dienstjahrVon === eintrag.dienstjahrVon
            ? eintrag.tage : Number(i.vorschlagGanzeTage);
        wrap.querySelector('#fkOk').disabled = noch <= 0;
        wrap.querySelector('#fkOk').style.opacity = noch <= 0 ? '0.45' : '1';
    };
    zeigeInfo(true);
    wrap.querySelector('#fkDj').onchange = e => { idx = parseInt(e.target.value, 10) || 0; zeigeInfo(true); };

    const speichern = async (verzicht) => {
        const st = wrap.querySelector('#fkStatus');
        const tage = parseFloat(wrap.querySelector('#fkTage').value || '0');
        const noch = Number(infos[idx].nochMoeglich || 0);
        if (!verzicht && !(tage > 0)) { st.textContent = 'Bitte die Anzahl Tage angeben.'; return; }
        if (!verzicht && tage > noch + 0.0001) { st.textContent = `Höchstens ${num(noch)} Tage möglich.`; return; }
        const body = {
            employeeId: empId,
            datum: wrap.querySelector('#fkDatum').value,
            dienstjahr: infos[idx].dienstjahrVon,
            tage: verzicht ? 0 : tage,
            verzicht,
            bemerkung: wrap.querySelector('#fkBem').value.trim() || (verzicht ? 'Bewusst nicht gekürzt' : null),
        };
        const url = eintrag ? `/api/absences/ferienkuerzung/${eintrag.id}` : '/api/absences/ferienkuerzung';
        const r = await fetch(url, { method: eintrag ? 'PUT' : 'POST', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!r.ok) { const j = await r.json().catch(() => ({})); st.textContent = j.message || ('Fehler ' + r.status); return; }
        close();
        loadAbsenzenTab(empId);
        if (typeof showToast === 'function') showToast(verzicht ? '✓ «Nicht kürzen» gespeichert' : `✓ Ferienkürzung ${num(tage)} Tage erfasst`, 'success');
    };
    wrap.querySelector('#fkOk').onclick = () => speichern(false);
    wrap.querySelector('#fkVerzicht').onclick = () => speichern(true);
}

// Absenzen-Liste: kurzes Datum dd.mm.yy (Walter 23.09.2026, nur diese Tabelle).
function _absDatumKurz(iso) {
    const t = String(iso || '').slice(0, 10);
    return t.length === 10 ? `${t.slice(8, 10)}.${t.slice(5, 7)}.${t.slice(2, 4)}` : '–';
}
function permitToggleMenu(event, id){ rowMenuToggle(event, 'permit', id); }
function qstToggleMenu(event, id)   { rowMenuToggle(event, 'qst',    id); }
function bankToggleMenu(event, id)  { rowMenuToggle(event, 'bank',   id); }
function famToggleMenu(event, id)   { rowMenuToggle(event, 'fam',    id); }
function allowToggleMenu(event, id) { rowMenuToggle(event, 'allow',  id); }

async function deleteAbsence(id) {
    // ⋮-Menü schließen bevor der Confirm-Dialog kommt
    rowMenuCloseAll();
    if (!(await liquidConfirm('Absenz wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/absences/${id}`, { method: 'DELETE', headers: ah() });
        // Lohnlauf-Sperre? Zeigt Toast und bricht ab.
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            let msg = 'Fehler beim Löschen.';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch {}
            alert(msg);
            return;
        }
        loadAbsenzenTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

