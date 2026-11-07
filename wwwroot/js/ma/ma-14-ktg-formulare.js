// ══════════════════════════════════════════════
// TAB: KTG/UVG – Tagessatz nach Spezialistenvorgabe
// Regel A (≤ 4 Perioden seit Vertragsstart): Hochrechnung aus Vertrag
// Regel B (≥ 4 Perioden):                    Durchschnitt aus AHV-Brutto (+ Mehrstunden bei MTP)
// Walter 17.07.2026: gleiche Rechnung an drei Orten —
//   full  = Tab «KTG/UVG»
//   side  = Absenzen-Tab rechts (Arbeitsplatz Krank/Unfall)
//   compact = Legacy (Übersicht nutzt Saldi-Tabelle seit 19.07.2026)
function _ktgFmtChf(n, dec = 2) {
    return Number(n || 0).toLocaleString('de-CH', {
        minimumFractionDigits: dec, maximumFractionDigits: dec
    });
}

function _ktgBadgeHtml(regel, compact = false) {
    if (regel === 'MANUELL')
        return `<span class="ktg-badge ktg-badge-man">MANUELL</span>`;
    if (regel === 'A')
        return `<span class="ktg-badge ktg-badge-a">${compact ? 'REGEL A' : 'REGEL A · Hochrechnung'}</span>`;
    return `<span class="ktg-badge ktg-badge-b">${compact ? 'REGEL B' : 'REGEL B · Durchschnitt'}</span>`;
}

function _ktgBreakdownHtml(d) {
    const bd = d.breakdown || {};
    const fmt = _ktgFmtChf;
    const renderMonate = (titel, istBerechnungsbasis) => {
        const monate = bd.monate || [];
        if (monate.length === 0) return '';
        const rows = monate.map(m => `
            <tr style="border-top:1px solid #f1f5f9">
                <td style="padding:6px 10px;font-size:12px;color:#475569">${m.monatName} ${m.jahr}</td>
                <td style="padding:6px 10px;font-size:12px;text-align:right;font-family:monospace">CHF ${fmt(m.brutto)}</td>
            </tr>`).join('');
        const avg = monate.reduce((s, m) => s + Number(m.brutto), 0) / monate.length;
        const footer = istBerechnungsbasis
            ? `<tr style="border-top:2px solid #e2e8f0;background:#f6f3ee">
                   <td style="padding:8px 10px;font-size:12px;font-weight:700;color:#6b7280">Ø pro Monat</td>
                   <td style="padding:8px 10px;font-size:12px;text-align:right;font-family:monospace;font-weight:700;color:#6b7280">CHF ${fmt(avg)}</td>
               </tr>`
            : '';
        return `
            <div style="margin-top:12px;padding:12px 16px;background:#f8fafc;border-radius:8px;font-size:12px;color:#334155">
                <div style="font-weight:600;margin-bottom:6px">${titel}</div>
                <table style="width:100%;border-collapse:collapse">
                    <tbody>${rows}</tbody>
                    ${footer ? `<tfoot>${footer}</tfoot>` : ''}
                </table>
            </div>`;
    };

    let breakdownHtml = '';
    if (d.regel === 'A') {
        if (d.vertragsModell === 'FIX' || d.vertragsModell === 'FIX-M') {
            breakdownHtml = `
                <div style="margin-top:12px;padding:12px 16px;background:#f8fafc;border-radius:8px;font-size:12px;color:#334155">
                    <div><b>Monatslohn:</b> CHF ${fmt(bd.monatsLohn)}</div>
                    <div style="margin-top:4px;color:#64748b">Formel: Monatslohn × 12 ÷ 365 = Tagessatz 100 %</div>
                </div>`;
        } else {
            breakdownHtml = `
                <div style="margin-top:12px;padding:12px 16px;background:#f8fafc;border-radius:8px;font-size:12px;color:#334155;line-height:1.7">
                    <div><b>Stundenlohn (Basis):</b> CHF ${fmt(bd.stundenlohnBasis, 2)}</div>
                    <div>+ Ferien ${fmt(bd.ferienPct, 2)} % + Feiertag ${fmt(bd.feiertagPct, 2)} %, darauf + 13. ML ${fmt(bd.zehnterMLPct, 2)} %</div>
                    <div><b>= Brutto-Stundenlohn:</b> CHF ${fmt(bd.stundenlohnBrutto, 4)}</div>
                    <div style="margin-top:6px"><b>Wochenstunden:</b> ${fmt(bd.wochenStunden, 2)} h (${d.vertragsModell === 'MTP' ? 'garantiert' : 'FLEX aus Filiale'})</div>
                    <div style="margin-top:4px;color:#64748b">Formel: Wochenstunden × Std-Lohn brutto × 52 ÷ 365 = Tagessatz 100 %</div>
                </div>`;
        }
        breakdownHtml += renderMonate(
            `ℹ️ Bisherige Lohnperioden <span style="font-weight:400;color:#64748b">(zur Information — flie\u00dft bei Regel A nicht in die Berechnung ein)</span>`,
            false
        );
    } else if (d.regel !== 'MANUELL') {
        const monate = bd.monate || [];
        breakdownHtml = renderMonate(
            `AHV-Brutto der letzten ${monate.length} Perioden`,
            true
        );
        if (d.vertragsModell === 'MTP') {
            breakdownHtml += `
                <div style="margin-top:8px;padding:12px 16px;background:#f8fafc;border-radius:8px;font-size:12px;color:#334155;line-height:1.7">
                    <div style="font-weight:600;margin-bottom:4px">MTP-Aufteilung:</div>
                    <div>Garantie-Basis/Monat: CHF ${fmt(bd.garantieBasisMonat)} &rarr; Tagessatz CHF ${fmt(bd.garantieTagessatz)}</div>
                    <div>Ø Mehrstunden/Monat (brutto): CHF ${fmt(bd.mehrstundenAnteilMonat)} &rarr; Tagessatz CHF ${fmt(bd.mehrstundenTagessatz)}</div>
                </div>`;
        }
        breakdownHtml += `
            <div style="margin-top:4px;font-size:12px;color:#64748b;padding:0 16px">
                Formel: Ø × 12 ÷ 365 = Tagessatz 100 %${d.vertragsModell === 'MTP' ? ' (Garantie- und Mehrstunden-Anteil summiert)' : ''}
            </div>`;
    }
    breakdownHtml += _ktgKarenzWegHtml(d);
    return breakdownHtml;
}

/** Rechenweg Karenz und Taggeld nach L-GAV (Walter 07.11.2026, D. Sägesser):
 *  Karenz = Grundlohn × 88 %, Ferien/Feiertag/13. ML einzeln (wie auf der
 *  Lohnabrechnung); Taggeld = Tagessatz × 80 %, pro Tag auf 5 Rp., ohne Zuschläge. */
function _ktgKarenzWegHtml(d) {
    const bd = d.breakdown || {};
    if (bd.karenzGrundlohn == null || d.karenzAbgeschlossen) return '';
    const fmt = _ktgFmtChf;
    const zeile = (txt, betrag, stark) => `
        <tr style="border-top:1px solid var(--oc-rand-schwach)">
            <td style="padding:4px 10px;${stark ? 'font-weight:700;' : ''}">${txt}</td>
            <td style="padding:4px 10px;text-align:right;font-family:monospace;${stark ? 'font-weight:700;' : ''}">CHF ${fmt(betrag)}</td>
        </tr>`;
    return `
        <div style="margin-top:12px;padding:12px 16px;background:var(--oc-flaeche-2);border-radius:8px;font-size:12px;color:var(--oc-text)">
            <div style="font-weight:600;margin-bottom:6px">Karenz 88 % pro Tag — Rechenweg L-GAV</div>
            <table style="width:100%;border-collapse:collapse"><tbody>
                ${zeile(`Grundlohn CHF ${fmt(bd.grundTagessatz)} × 88 %`, bd.karenzGrundlohn)}
                ${zeile(`+ Ferien ${fmt(bd.ferienPct, 2)} %`, bd.karenzFerien)}
                ${zeile(`+ Feiertag ${fmt(bd.feiertagPct, 2)} %`, bd.karenzFeiertag)}
                ${zeile(`+ 13. ML ${fmt(bd.zehnterMLPct, 2)} % auf das Zwischentotal`, bd.karenz13ml)}
                ${zeile('= Karenz pro Tag (88 % des Tagessatzes)', d.tagessatz88, true)}
            </tbody></table>
            <div style="margin-top:6px;color:var(--oc-text-3)">Taggeld 80 %: Tagessatz × 80 %, pro Tag auf 5 Rappen (wie die Versicherung) — eine Zeile ohne Zuschläge, Ferien, Feiertag und 13. ML sind im Tagessatz enthalten.</div>
        </div>`;
}

function _ktgRatesTableHtml(d) {
    const fmt = _ktgFmtChf;
    return `
        <table class="ktg-rates-table">
            <thead>
                <tr>
                    <th>TAGESSATZ</th>
                    <th>CHF / TAG</th>
                </tr>
            </thead>
            <tbody>
                <tr>
                    <td>100 %</td>
                    <td>CHF ${fmt(d.tagessatz100)}</td>
                </tr>
                ${d.karenzAbgeschlossen ? `
                <tr class="ktg-row-muted">
                    <td colspan="2">88 % — Karenz übersprungen (altes System)</td>
                </tr>
                ` : `
                <tr class="ktg-row-88">
                    <td>88 % — Karenzfrist</td>
                    <td>CHF ${fmt(d.tagessatz88)}</td>
                </tr>
                `}
                <tr class="ktg-row-80">
                    <td>80 % — Meldebetrag</td>
                    <td>CHF ${fmt(d.tagessatz80)}</td>
                </tr>
            </tbody>
        </table>`;
}

function _ktgOverrideBtnHtml(d) {
    const empId = selectedEmployee?.id || 0;
    const manuell = d.regel === 'MANUELL' ? d.tagessatz100 : '';
    return `<button class="btn btn-outline ktg-ov-btn" onclick="openKtgOverrideModal(${empId}, ${d.tagessatz100 || 0}, ${d.karenzAbgeschlossen ? 'true' : 'false'}, '${manuell}')">✎ Tagessatz übersteuern…</button>`;
}

/** Wochenstunden für Tagessatz-Meta (statt «N Per.» — Walter 25.07.2026). */
function _ktgMetaWochenstunden(d) {
    const wo = d?.breakdown?.wochenStunden;
    if (wo == null || !Number.isFinite(Number(wo))) return '— h/Wo.';
    const n = Number(wo);
    const txt = n.toLocaleString('de-CH', {
        minimumFractionDigits: Number.isInteger(n) ? 0 : 2,
        maximumFractionDigits: 2,
    });
    return `${txt} h/Wo.`;
}

function renderKtgTagessatzHtml(d, mode = 'full') {
    const fmt = _ktgFmtChf;
    const vs = d.vertragsStart ? new Date(d.vertragsStart).toLocaleDateString('de-CH') : '—';
    const badge = _ktgBadgeHtml(d.regel, mode === 'compact' || mode === 'side');
    const woLabel = _ktgMetaWochenstunden(d);
    const nPer = Number(d.anzahlPerioden) || 0;
    const perLabel = `${nPer} Lohnperiode${nPer === 1 ? '' : 'n'}`;
    // Modell · Vertragsstart · Wochenstunden · Anzahl Lohnperioden (Regel A/B).
    const meta = (mode === 'compact' || mode === 'side')
        ? `<b>${d.vertragsModell || '?'}</b> · ${vs} · ${woLabel} · ${perLabel}`
        : `Vertrag <b>${d.vertragsModell || '?'}</b> seit ${vs} · ${woLabel} · ${perLabel}`;

    if (mode === 'compact') {
        return `
            <div class="ktg-compact">
                <div class="ktg-compact-top">${badge}</div>
                <div class="ktg-compact-meta">${meta}</div>
                <div class="ktg-compact-rows">
                    <div><span>100 %</span><strong>CHF ${fmt(d.tagessatz100)}</strong></div>
                    ${d.karenzAbgeschlossen
                        ? `<div class="muted"><span>88 %</span><strong>übersprungen</strong></div>`
                        : `<div class="r88"><span>88 %</span><strong>CHF ${fmt(d.tagessatz88)}</strong></div>`}
                    <div class="r80"><span>80 %</span><strong>CHF ${fmt(d.tagessatz80)}</strong></div>
                </div>
                <button type="button" class="ov-more" style="margin-top:4px;border:none;background:none;padding:0;cursor:pointer;text-align:left"
                        onclick="switchEmpTab('absenzen')">Bei Absenzen anzeigen →</button>
            </div>`;
    }

    // Absenzen-Sidebar (side): Walter 26.07.2026 — Berechnung immer sichtbar
    // (genug Platz), kein Aufklappen mehr.
    if (mode === 'side') {
        const breakdown = _ktgBreakdownHtml(d);
        return `
            <div class="ktg-panel ktg-panel-side">
                <div class="ktg-panel-h">
                    <div class="ktg-panel-title">Tagessatz</div>
                    ${badge}
                </div>
                <div class="ktg-panel-meta">${meta}</div>
                <div class="ktg-compact-rows">
                    <div><span>100 %</span><strong>CHF ${fmt(d.tagessatz100)}</strong></div>
                    ${d.karenzAbgeschlossen
                        ? `<div class="muted"><span>88 %</span><strong>übersprungen</strong></div>`
                        : `<div class="r88"><span>88 %</span><strong>CHF ${fmt(d.tagessatz88)}</strong></div>`}
                    <div class="r80"><span>80 %</span><strong>CHF ${fmt(d.tagessatz80)}</strong></div>
                </div>
                <div class="ktg-panel-actions">${_ktgOverrideBtnHtml(d)}</div>
                ${breakdown ? `<div class="ktg-side-berechnung"><div class="ktg-side-berechnung-label">Berechnung</div>${breakdown}</div>` : ''}
            </div>`;
    }

    const breakdown = _ktgBreakdownHtml(d);
    return `
        <div class="ktg-panel ktg-panel-full" style="padding:20px">
            <div class="ktg-panel-h">
                <div class="ktg-panel-title">📊 KTG/UVG-Tagessatz</div>
                ${badge}
            </div>
            <div class="ktg-panel-meta">${meta}</div>
            ${_ktgRatesTableHtml(d)}
            <div class="ktg-panel-actions">${_ktgOverrideBtnHtml(d)}</div>
            ${breakdown}
        </div>`;
}

function _ovKtgSkeletonHtml() {
    // Absenzen-Sidebar-Fallback (Übersicht nutzt Saldi-Tabelle).
    return `<div class="ktg-compact ktg-compact-skel" aria-busy="true">
        <div class="ktg-compact-top"><span class="ktg-badge ktg-badge-a" style="opacity:.35">…</span></div>
        <div class="ktg-compact-meta" style="opacity:.45">— · — · —</div>
        <div class="ktg-compact-rows">
            <div><span>100 %</span><strong>· · ·</strong></div>
            <div class="r88"><span>88 %</span><strong>· · ·</strong></div>
            <div class="r80"><span>80 %</span><strong>· · ·</strong></div>
        </div>
    </div>`;
}

// ── Übersicht: Stunden & Saldi (Walter 19.07.2026) ─────────────────────
// Spalten einmal: Soll · gearb. · Absenz · Vorm. · Saldo
// Zeilen: Stunden · Ferien · Feiertage · Nacht
// Daten = dieselbe Calculate-Engine wie der Lohnlauf (kein eigener Report).
function _ovSaldiMode() {
    const m = localStorage.getItem('hrOvSaldiMode');
    return m === 'monat' ? 'monat' : 'aktuell'; // Default: per heute
}

function ovSaldiSetMode(mode) {
    const next = mode === 'monat' ? 'monat' : 'aktuell';
    localStorage.setItem('hrOvSaldiMode', next);
    _ovSaldiSyncSwitch();
    const cache = window._ovSaldiCache;
    const el = document.getElementById('ovSaldiContent');
    if (el && cache && cache.s)
        el.innerHTML = renderOvSaldiHtml(cache.s, cache.stRow, next);
}

function _ovSaldiSyncSwitch() {
    const mode = _ovSaldiMode();
    document.querySelectorAll('.ov-saldi-sw').forEach(btn => {
        btn.classList.toggle('on', btn.getAttribute('data-mode') === mode);
    });
}

function _ovSaldiSkeletonHtml() {
    const dash = '<td class="ov-saldi-dash">·</td>';
    return `<table class="ov-saldi-tbl" aria-busy="true">
        <thead><tr><th></th><th>Soll</th><th>gearb.</th><th>Absenz</th><th>Vorm.</th><th>Saldo</th></tr></thead>
        <tbody>
            <tr><td>Stunden</td>${dash}${dash}${dash}${dash}${dash}</tr>
            <tr><td>Ferien</td>${dash}${dash}${dash}${dash}${dash}</tr>
            <tr><td>Feiertage</td>${dash}${dash}${dash}${dash}${dash}</tr>
            <tr><td>Nacht</td>${dash}${dash}${dash}${dash}${dash}</tr>
        </tbody>
    </table>`;
}

function _ovSaldiNum(v, { signed = false, dashIfNull = false } = {}) {
    if (v == null || (dashIfNull && !Number.isFinite(Number(v))))
        return '<td class="ov-saldi-dash">–</td>';
    const n = Number(v);
    if (!Number.isFinite(n)) return '<td class="ov-saldi-dash">–</td>';
    const txt = (signed && n > 0.005 ? '+' : '') + n.toLocaleString('de-CH', {
        minimumFractionDigits: 1,
        maximumFractionDigits: 2
    });
    let cls = 'ov-saldi-n';
    if (signed) {
        if (n > 0.005) cls += ' ov-saldi-pos';
        else if (n < -0.005) cls += ' ov-saldi-neg';
    }
    return `<td class="${cls}">${txt}</td>`;
}

function _ovSaldiDash() { return '<td class="ov-saldi-dash">–</td>'; }

function renderOvSaldiHtml(s, stRow, mode) {
    if (!s) return _ovSaldiSkeletonHtml();
    mode = mode || _ovSaldiMode();
    const model = (s.employmentModel || (stRow && stRow.model) || '').toUpperCase();
    const isFlex = model === 'FLEX' || model === 'UTP';
    const isFix  = model === 'FIX' || model === 'FIX-M';
    const isAktuell = mode !== 'monat';

    // Stunden: «aktuell» = Stichtag-Block (blau im Sollstunden-Report),
    // «Monat» = Monats-Block — dieselben Felder wie dort.
    let rowStunden;
    if (isFlex) {
        const worked = Number(s.workedHours ?? 0);
        rowStunden = `<tr><td>Stunden</td>${_ovSaldiDash()}${_ovSaldiNum(worked)}${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}</tr>`;
    } else if (stRow) {
        const p = isAktuell ? 'st' : 'mt';
        const sollH  = Number(stRow[p + 'Soll'] ?? 0);
        const gearbH = Number(stRow[p + 'Gearb'] ?? 0);
        const absH   = Number(stRow[p + 'Absenz'] ?? 0);
        const vorH   = Number(stRow[p + 'SaldoVor'] ?? 0);
        const saldoH = Number(stRow[p + 'Saldo'] ?? 0);
        rowStunden = `<tr><td>Stunden</td>${_ovSaldiNum(sollH)}${_ovSaldiNum(gearbH)}${_ovSaldiNum(absH)}${_ovSaldiNum(vorH, { signed: true })}${_ovSaldiNum(saldoH, { signed: true })}</tr>`;
    } else {
        rowStunden = `<tr><td>Stunden</td>${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}</tr>`;
    }

    // Ferien / Feiertage / Nacht — Monatswerte aus Calculate (kein Stichtag-Block)
    const ferSoll  = Number(s.ferienTageAccrual ?? 0);
    const ferAbs   = Number(s.ferienTageGenommen ?? 0);
    const ferVor   = Number(s.vormonatFerienTage ?? 0);
    const ferSaldo = Number(s.ferienTageSaldoNeu ?? 0);

    const ftSoll  = Number(s.feiertagTageAccrual ?? 0);
    const ftAbs   = Number(s.feiertagTageGenommen ?? 0);
    const ftVor   = Number(s.vormonatFeiertagTage ?? 0);
    const ftSaldo = Number(s.feiertagTageSaldoNeu ?? 0);

    const nachtGearb = Number(s.nightHours ?? 0);
    const nachtAbs   = Number(s.nachtKompStunden ?? 0);
    const nachtVor   = Number(s.vormonatNachtSaldo ?? 0);
    const nachtSaldo = Number(s.neuerNachtSaldo ?? 0);

    const rowFerien = `<tr><td>Ferien</td>${_ovSaldiNum(ferSoll)}${_ovSaldiDash()}${_ovSaldiNum(ferAbs)}${_ovSaldiNum(ferVor, { signed: true })}${_ovSaldiNum(ferSaldo, { signed: true })}</tr>`;

    const rowFeiertag = isFix
        ? `<tr><td>Feiertage</td>${_ovSaldiNum(ftSoll)}${_ovSaldiDash()}${_ovSaldiNum(ftAbs)}${_ovSaldiNum(ftVor, { signed: true })}${_ovSaldiNum(ftSaldo, { signed: true })}</tr>`
        : `<tr><td>Feiertage</td>${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}${_ovSaldiDash()}</tr>`;

    const rowNacht = `<tr><td>Nacht</td>${_ovSaldiDash()}${_ovSaldiNum(nachtGearb)}${_ovSaldiNum(nachtAbs)}${_ovSaldiNum(nachtVor, { signed: true })}${_ovSaldiNum(nachtSaldo, { signed: true })}</tr>`;

    return `<table class="ov-saldi-tbl">
        <thead><tr><th></th><th>Soll</th><th>gearb.</th><th>Absenz</th><th>Vorm.</th><th>Saldo</th></tr></thead>
        <tbody>${rowStunden}${rowFerien}${rowFeiertag}${rowNacht}</tbody>
    </table>`;
}

async function loadOvSaldi(employeeId) {
    const el = document.getElementById('ovSaldiContent');
    if (!el || !employeeId) return;
    if (typeof currentUser !== 'undefined' && currentUser?.role === 'lowuser') {
        el.innerHTML = '<div class="ov-saldi-msg">Lohnübersicht ist für diese Rolle nicht verfügbar.</div>';
        return;
    }

    const gen = (window._ovSaldiLoadGen = (window._ovSaldiLoadGen || 0) + 1);
    el.innerHTML = _ovSaldiSkeletonHtml();
    _ovSaldiSyncSwitch();

    const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
        ? fixedCompanyProfileId
        : (typeof selectedCompanyProfile !== 'undefined' && selectedCompanyProfile?.id)
        ? selectedCompanyProfile.id
        : null;
    if (!cid) {
        el.innerHTML = `<div class="ov-saldi-msg">Bitte Filiale wählen.</div>`;
        return;
    }

    const now = new Date();
    const year  = parseInt(document.getElementById('lohnYearSelect')?.value  || now.getFullYear(), 10);
    const month = parseInt(document.getElementById('lohnMonthSelect')?.value || (now.getMonth() + 1), 10);
    const hdr = typeof ah === 'function' ? ah() : { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` };
    const ts = Date.now();

    try {
        // Calculate → Ferien/Feiertag/Nacht; Sollstunden-Report (1 MA) → Stunden st/mt
        const [calcRes, stRes] = await Promise.all([
            fetch(`/api/payroll/calculate?employeeId=${employeeId}&year=${year}&month=${month}&companyProfileId=${cid}&_=${ts}`,
                { headers: hdr, cache: 'no-store' }),
            fetch(`/api/payroll/sollstunden-report?companyProfileId=${cid}&year=${year}&month=${month}&employeeId=${employeeId}&_=${ts}`,
                { headers: hdr, cache: 'no-store' })
        ]);
        if (gen !== window._ovSaldiLoadGen) return;
        if (calcRes.status === 404) {
            el.innerHTML = `<div class="ov-saldi-msg">Kein Vertrag in dieser Periode.</div>`;
            return;
        }
        if (!calcRes.ok) {
            el.innerHTML = `<div class="ov-saldi-msg ov-saldi-err">Fehler ${calcRes.status}</div>`;
            return;
        }
        const s = await calcRes.json();
        let stRow = null;
        if (stRes.ok) {
            const st = await stRes.json();
            stRow = (st.rows || []).find(r => r.employeeId === employeeId) || null;
        }
        if (gen !== window._ovSaldiLoadGen) return;
        window._ovSaldiCache = { empId: employeeId, s, stRow };
        _ovSaldiSyncSwitch();
        el.innerHTML = renderOvSaldiHtml(s, stRow, _ovSaldiMode());
    } catch (e) {
        if (gen !== window._ovSaldiLoadGen) return;
        el.innerHTML = `<div class="ov-saldi-msg ov-saldi-err">Fehler: ${esc(e.message || String(e))}</div>`;
    }
}

async function loadKtgTab(employeeId) {
    // Nur noch Absenzen-Sidebar (Übersicht = Saldi-Tabelle).
    const side = document.getElementById('ktgTagessatzSidebar');
    if (!side || !employeeId) return;
    if (typeof currentUser !== 'undefined' && currentUser?.role === 'lowuser') {
        side.innerHTML = '<div style="padding:16px;color:#8b8b8b;font-size:13px">Nicht verfügbar für diese Rolle.</div>';
        return;
    }

    const gen = (window._ktgLoadGen = (window._ktgLoadGen || 0) + 1);
    side.innerHTML = '<div style="padding:16px;text-align:center;color:#94a3b8;font-size:13px">Lade…</div>';

    try {
        const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
            ? fixedCompanyProfileId
            : (typeof selectedCompanyProfile !== 'undefined' && selectedCompanyProfile?.id)
            ? selectedCompanyProfile.id
            : null;

        if (!cid) {
            side.innerHTML = '<div style="padding:16px;color:#94a3b8;font-size:13px">Bitte Filiale wählen.</div>';
            return;
        }

        const res = await fetch(`/api/payroll/ktg-tagessatz?employeeId=${employeeId}&companyProfileId=${cid}`,
            { headers: { 'Authorization': `Bearer ${localStorage.getItem('hrToken')}` } });

        if (gen !== window._ktgLoadGen) return;

        if (res.status === 404) {
            side.innerHTML = `<div style="padding:20px;text-align:center;color:#94a3b8;font-size:13px">Kein aktives Anstellungsverhältnis gefunden.</div>`;
            return;
        }
        if (!res.ok) {
            side.innerHTML = `<div style="padding:16px;color:#dc2626;font-size:13px">Fehler ${res.status}</div>`;
            return;
        }

        const d = await res.json();
        if (gen !== window._ktgLoadGen) return;
        side.innerHTML = `<div class="ktg-side-card">${renderKtgTagessatzHtml(d, 'side')}</div>`;
    } catch (e) {
        if (gen !== window._ktgLoadGen) return;
        side.innerHTML = `<div style="padding:16px;color:#dc2626;font-size:13px">Fehler: ${e.message}</div>`;
    }
}

// ══════════════════════════════════════════════
// KTG/UVG-Tagessatz: Manueller Override
// Walter-Vorgabe: Legacy-MA aus altem Lohnsystem hat u.U. schon einen
// errechneten Tagessatz (Versicherer-Standard). Plus: Karenz kann
// bereits abgelaufen sein → direkt 80% Meldebetrag.
// ══════════════════════════════════════════════
function openKtgOverrideModal(employeeId, autoTagessatz100, karenzAbgeschlossen, manuellExisting) {
    if (!employeeId) { alert('Kein Mitarbeiter ausgewählt.'); return; }
    const manuellVal = manuellExisting && manuellExisting !== '0'
        ? Number(manuellExisting).toFixed(2) : '';
    const html = `
        <div id="ktgOvBackdrop" style="position:fixed;inset:0;background:rgba(0,0,0,.5);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px"
             onclick="if(event.target===this) closeKtgOverrideModal()">
            <div style="background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);border-radius:12px;width:100%;max-width:520px;box-shadow:0 20px 60px rgba(0,0,0,.3);overflow:hidden">
                <div style="padding:16px 22px;border-bottom:1px solid #e2e8f0">
                    <div style="font-size:15px;font-weight:700;color:#1e293b">KTG/UVG-Tagessatz übersteuern</div>
                    <div style="font-size:12px;color:#64748b;margin-top:2px">Auto-Berechnung: CHF ${Number(autoTagessatz100).toFixed(2)} / Tag (100 %)</div>
                </div>
                <div style="padding:18px 22px">
                    <label style="font-size:12px;font-weight:600;color:#374151;display:block">Manueller Tagessatz 100 % (CHF)</label>
                    <input type="number" step="0.01" min="0" id="ktgOvManuell" value="${manuellVal}"
                           placeholder="z.B. 36.25"
                           style="width:100%;padding:9px 12px;border:1px solid #d1d5db;border-radius:7px;font-size:14px;margin-top:4px">
                    <div style="font-size:11px;color:#64748b;margin-top:3px">Leer lassen = Auto-Berechnung verwenden. Die 88-/80-%-Stufen werden aus diesem Wert abgeleitet.</div>

                    <label style="display:flex;align-items:center;gap:8px;margin-top:18px;cursor:pointer">
                        <input type="checkbox" id="ktgOvKarenz" ${karenzAbgeschlossen ? 'checked' : ''}>
                        <span>
                            <span style="font-size:13px;font-weight:600;color:#0f172a">Karenz bereits abgeschlossen</span>
                            <span style="display:block;font-size:11.5px;color:#64748b;margin-top:2px">Im alten Lohnsystem wurde die Karenzfrist bereits durchlaufen — die Versicherung zahlt direkt 80 % (Meldebetrag), kein 88 %-Schritt mehr.</span>
                        </span>
                    </label>

                    <div id="ktgOvError" style="color:#dc2626;font-size:12px;margin-top:12px"></div>
                </div>
                <div style="padding:14px 22px;border-top:1px solid #f1f5f9;display:flex;justify-content:flex-end;gap:10px">
                    <button class="btn btn-outline" onclick="closeKtgOverrideModal()">Abbrechen</button>
                    <button class="btn btn-primary" onclick="saveKtgOverride(${employeeId})">Speichern</button>
                </div>
            </div>
        </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

function closeKtgOverrideModal() {
    document.getElementById('ktgOvBackdrop')?.remove();
}

async function saveKtgOverride(employeeId) {
    const errEl    = document.getElementById('ktgOvError');
    const manRaw   = document.getElementById('ktgOvManuell').value.trim();
    const karenz   = document.getElementById('ktgOvKarenz').checked;
    let manVal = null;
    if (manRaw !== '') {
        manVal = parseFloat(manRaw);
        if (!Number.isFinite(manVal) || manVal < 0) {
            errEl.textContent = 'Ungültiger Betrag.'; return;
        }
    }
    try {
        const res = await fetch(`/api/employees/${employeeId}`, {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                ktgTagessatzManuellSet: true,
                ktgTagessatzManuell:    manVal,
                ktgKarenzAbgeschlossen: karenz
            })
        });
        if (!res.ok) { const e = await res.json().catch(()=>({})); throw new Error(e.error || e.message || `HTTP ${res.status}`); }
        closeKtgOverrideModal();
        if (typeof showToast === 'function') {
            showToast(manVal !== null ? 'Manueller Tagessatz gespeichert' : 'Auto-Berechnung wieder aktiv', 'success');
        }
        // KTG-Tab neu laden
        if (typeof loadKtgTab === 'function') loadKtgTab(employeeId);
    } catch (e) {
        errEl.textContent = e.message;
    }
}

// TAB: Formulare / Arbeitslosigkeit / Zwischenverdienst
// ══════════════════════════════════════════════

async function loadFormulareTab(employeeId) {
    // Monat vorbelegen (aktueller Monat)
    const now = new Date();
    const selMonat = document.getElementById('zvMonat');
    const selJahr  = document.getElementById('zvJahr');
    if (selMonat && !selMonat._initialized) {
        selMonat.value = now.getMonth() + 1;
        selMonat._initialized = true;
    }

    // (QST-Anmeldung braucht keinen Filial-Picker mehr — Filiale + Kanton
    //  werden im Backend automatisch aus den MA-Daten ermittelt.)

    // Arbeitslosigkeits-Einträge laden
    try {
        const res = await fetch(`/api/zwischenverdienist/arbeitslosigkeit/${employeeId}`, { headers: ah() });
        if (!res.ok) throw new Error();
        const list = await res.json();
        renderArbeitslosigkeitList(list, employeeId);
    } catch {
        document.getElementById('arbeitslosigkeitList').innerHTML =
            '<p style="color:#ef4444;font-size:13px">Fehler beim Laden.</p>';
    }
}

function renderArbeitslosigkeitList(list, employeeId) {
    const el = document.getElementById('arbeitslosigkeitList');
    if (!list.length) {
        el.innerHTML = '<p style="color:#94a3b8;font-size:13px">Noch keine Arbeitslosigkeits-Perioden erfasst.</p>';
        return;
    }
    const rows = list.map(a => `
      <tr>
        <td>${fmtDate(a.angemeldetSeit)}</td>
        <td>${a.abgemeldetAm ? fmtDate(a.abgemeldetAm) : '<span style="color:#22c55e;font-weight:600">aktiv</span>'}</td>
        <td>${a.ravStelle || '–'}</td>
        <td>${a.ravKundennummer || '–'}</td>
        <td>${a.arbeitslosenkasse || '–'}</td>
        <td style="white-space:nowrap">
          <button class="btn-icon" onclick="editArbeitslosigkeit(${JSON.stringify(a).replace(/"/g,'&quot;')})">✏️</button>
          <button class="btn-icon" onclick="deleteArbeitslosigkeit(${a.id},${employeeId})">🗑️</button>
        </td>
      </tr>`).join('');
    el.innerHTML = `
      <table class="data-table" style="font-size:12px">
        <thead><tr>
          <th>Angemeldet seit</th><th>Abgemeldet am</th>
          <th>RAV-Stelle</th><th>Kundennr.</th><th>ALK</th><th></th>
        </tr></thead>
        <tbody>${rows}</tbody>
      </table>`;
}

function fmtDate(ds) {
    if (!ds) return '';
    const d = new Date(ds);
    return `${String(d.getUTCDate()).padStart(2,'0')}.${String(d.getUTCMonth()+1).padStart(2,'0')}.${d.getUTCFullYear()}`;
}

function openArbeitslosigkeitForm(entry) {
    document.getElementById('alId').value               = entry?.id || '';
    document.getElementById('alAngemeldetSeit').value   = entry?.angemeldetSeit?.slice(0,10) || '';
    document.getElementById('alAbgemeldetAm').value     = entry?.abgemeldetAm?.slice(0,10)   || '';
    document.getElementById('alRavStelle').value        = entry?.ravStelle        || '';
    document.getElementById('alRavKundennummer').value  = entry?.ravKundennummer  || '';
    document.getElementById('alArbeitslosenkasse').value= entry?.arbeitslosenkasse|| '';
    document.getElementById('alBemerkung').value        = entry?.bemerkung        || '';
    document.getElementById('arbeitslosigkeitInlineForm').style.display = 'block';
}

function editArbeitslosigkeit(entry) { openArbeitslosigkeitForm(entry); }

function closeArbeitslosigkeitForm() {
    document.getElementById('arbeitslosigkeitInlineForm').style.display = 'none';
}

async function saveArbeitslosigkeit() {
    const id    = document.getElementById('alId').value;
    const since = document.getElementById('alAngemeldetSeit').value;
    if (!since) { alert('Anmeldedatum ist erforderlich.'); return; }

    const body = {
        employeeId:       selectedEmployeeId,
        angemeldetSeit:   since,
        abgemeldetAm:     document.getElementById('alAbgemeldetAm').value || null,
        ravStelle:        document.getElementById('alRavStelle').value.trim()         || null,
        ravKundennummer:  document.getElementById('alRavKundennummer').value.trim()   || null,
        arbeitslosenkasse:document.getElementById('alArbeitslosenkasse').value.trim() || null,
        bemerkung:        document.getElementById('alBemerkung').value.trim()         || null,
    };

    const url    = id ? `/api/zwischenverdienist/arbeitslosigkeit/${id}` : '/api/zwischenverdienist/arbeitslosigkeit';
    const method = id ? 'PUT' : 'POST';

    try {
        const res = await fetch(url, { method, headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!res.ok) { alert('Fehler beim Speichern.'); return; }
        closeArbeitslosigkeitForm();
        loadFormulareTab(selectedEmployeeId);
    } catch { alert('Verbindungsfehler.'); }
}

async function deleteArbeitslosigkeit(id, employeeId) {
    if (!(await liquidConfirm('Eintrag löschen?'))) return;
    try {
        const res = await fetch(`/api/zwischenverdienist/arbeitslosigkeit/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadFormulareTab(employeeId);
    } catch { alert('Verbindungsfehler.'); }
}

async function generateZwischenverdienst() {
    const monat = document.getElementById('zvMonat').value;
    const jahr  = document.getElementById('zvJahr').value;

    // companyProfileId aus dem aktuellen Mitarbeiter ermitteln
    const cpId = selectedEmployee?.employments?.[0]?.companyProfileId
              || selectedEmployee?.companyProfileId
              || 1;

    const url = `/api/zwischenverdienist/pdf?employeeId=${selectedEmployeeId}&year=${jahr}&month=${monat}&companyProfileId=${cpId}`;

    // PDF in neuem Tab öffnen (Browser zeigt Download-Dialog)
    window.open(url, '_blank');
}

// ── QST-Anmeldeformular generieren ──────────────────────────────────────
// Keine Parameter mehr nötig: das Backend ermittelt die Filiale aus dem
// aktiven Arbeitsverhältnis des MA und den Kanton aus dem aktiven QST-Eintrag
// (Wohnsitz-Kanton). Walter wollte die manuelle Auswahl loswerden, weil
// beides eindeutig aus den MA-Daten herleitbar ist.
async function generateQstAnmeldung() {
    if (!selectedEmployeeId) return;
    window.open(`/api/qst-anmeldung/${selectedEmployeeId}/pdf`, '_blank');
}

