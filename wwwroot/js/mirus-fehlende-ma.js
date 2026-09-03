// ══════════════════════════════════════════════════════════════════════
// mirus-fehlende-ma.js — HR-Hub → Kontrolle → «Mirus-Abgleich: fehlende MA»
// ──────────────────────────────────────────────────────────────────────
// Walter 03.09.2026: Mirus-Personalexport (XLS) hochladen → welche AKTIVEN
// OneCrew-MA der Sidebar-Filiale fehlen in Mirus? Pro fehlendem MA alle
// Angaben zum Erfassen (Personalien, Adresse, Vertrag, Bank, QST, Familie).
// Nur Auswertung, kein Import. Backend: /api/imports/mirus-fehlende-ma
// ══════════════════════════════════════════════════════════════════════
let _mfmFile = null;
let _mfmData = null;

function mfmInit() {
    ['mfmAlert', 'mfmSummary', 'mfmResult'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.innerHTML = '';
    });
    const inp = document.getElementById('mfmFileInput');
    if (inp) inp.value = '';
    _mfmFile = null;
    _mfmData = null;
    mfmUpdateBranchBanner();
}

function mfmUpdateBranchBanner() {
    const el = document.getElementById('mfmBranchBanner');
    if (!el) return;
    const cpId = typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null;
    const b = (typeof allBranches !== 'undefined' ? allBranches : []).find(x => x.id === cpId);
    if (!b) {
        el.innerHTML = '⚠ Bitte links eine Filiale wählen — der Abgleich läuft gegen die aktiven MA dieser Filiale.';
        el.style.background = '#fef3c7';
        el.style.borderColor = '#fde68a';
        el.style.color = '#92400e';
        return;
    }
    el.innerHTML = `Filiale: <b>${esc(b.restaurantCode || '')} ${esc(b.city || b.name || '')}</b> — nur <b>aktive</b> OneCrew-MA mit Lohn; Match per Personalnummer, dann AHV-Nummer, dann Name + Geburtsdatum.`;
    el.style.background = '#f6f3ee';
    el.style.borderColor = '#e5e0d6';
    el.style.color = '#6b6152';
}

function _mfmCpId() {
    return typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0;
}

function _mfmAuth() {
    return { 'Authorization': 'Bearer ' + (typeof authToken !== 'undefined' ? authToken : localStorage.hrToken) };
}

async function mfmAnalyze() {
    const inp = document.getElementById('mfmFileInput');
    document.getElementById('mfmAlert').innerHTML = '';
    document.getElementById('mfmSummary').innerHTML = '';
    document.getElementById('mfmResult').innerHTML = '';
    if (!inp.files || inp.files.length === 0) {
        showPageAlert('mfmAlert', 'Bitte den Mirus-Personalexport (XLS/XLSX) wählen.', 'error');
        return;
    }
    const cpId = _mfmCpId();
    if (!cpId) {
        showPageAlert('mfmAlert', 'Bitte zuerst links eine Filiale wählen.', 'error');
        return;
    }
    _mfmFile = inp.files[0];
    const fd = new FormData();
    fd.append('file', _mfmFile);
    fd.append('companyProfileId', String(cpId));

    const btn = document.getElementById('mfmAnalyzeBtn');
    if (btn) { btn.disabled = true; btn.textContent = '⏳ vergleiche…'; }
    try {
        const r = await fetch('/api/imports/mirus-fehlende-ma/analyze', { method: 'POST', headers: _mfmAuth(), body: fd });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            showPageAlert('mfmAlert', 'Fehler: ' + (j.error || j.message || r.status), 'error');
            return;
        }
        _mfmData = await r.json();
        mfmRender(_mfmData);
    } catch (e) {
        showPageAlert('mfmAlert', 'Netzwerkfehler: ' + e.message, 'error');
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'Vergleichen'; }
    }
}

async function mfmPdf() {
    if (!_mfmFile) {
        showPageAlert('mfmAlert', 'Bitte zuerst vergleichen (Datei wählen).', 'error');
        return;
    }
    const cpId = _mfmCpId();
    if (!cpId) {
        showPageAlert('mfmAlert', 'Bitte zuerst links eine Filiale wählen.', 'error');
        return;
    }
    const fd = new FormData();
    fd.append('file', _mfmFile);
    fd.append('companyProfileId', String(cpId));
    const btn = document.getElementById('mfmPdfBtn');
    if (btn) { btn.disabled = true; btn.textContent = '⏳ PDF…'; }
    try {
        const r = await fetch('/api/imports/mirus-fehlende-ma/pdf', { method: 'POST', headers: _mfmAuth(), body: fd });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            showPageAlert('mfmAlert', 'PDF fehlgeschlagen: ' + (j.error || j.message || r.status), 'error');
            return;
        }
        const blob = await r.blob();
        const fn = cdFilename(r.headers.get('Content-Disposition') || '', 'Mirus-fehlende-MA.pdf');
        if (typeof previewFileModal === 'function') await previewFileModal(blob, fn);
        else if (typeof saveBlobAsk === 'function') await saveBlobAsk(blob, fn);
    } catch (e) {
        showPageAlert('mfmAlert', 'Netzwerkfehler: ' + e.message, 'error');
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = '📄 PDF Erfassungsliste'; }
    }
}

function mfmTile(n, label, bg, fg) {
    return `<div style="background:${bg};border-radius:10px;padding:12px 14px;color:${fg}">
        <div style="font-size:24px;font-weight:700">${n ?? 0}</div>
        <div style="font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.03em">${label}</div>
    </div>`;
}

function mfmRender(data) {
    const summary = document.getElementById('mfmSummary');
    summary.innerHTML = `
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(130px,1fr));gap:10px">
            ${mfmTile(data.oneCrewAktiv, 'OneCrew aktiv', '#ece9e2', '#6b6152')}
            ${mfmTile(data.gematcht, 'In Mirus gefunden', '#dcfce7', '#166534')}
            ${mfmTile(data.fehlend, 'Fehlen in Mirus', data.fehlend ? '#fee2e2' : '#f0fdf4', data.fehlend ? '#991b1b' : '#15803d')}
            ${mfmTile(data.mirusAusgetreten, 'In Mirus ausgetreten', '#fef3c7', '#92400e')}
            ${mfmTile(data.nurMirus, 'Nur in Mirus', '#e0e7ff', '#3730a3')}
            ${mfmTile(data.mirusZeilen, 'Mirus-Zeilen', '#f1f5f9', '#475569')}
        </div>
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center;margin:14px 0 10px;justify-content:space-between">
            <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center">
                <span style="font-size:12px;color:#64748b;font-weight:600">Ansicht:</span>
                <select id="mfmFilter" onchange="mfmRenderRows()"
                        style="padding:6px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:#fff">
                    <option value="FEHLEND">Fehlen in Mirus (${data.fehlend})</option>
                    <option value="AUSGETRETEN">In Mirus ausgetreten, in OneCrew aktiv (${data.mirusAusgetreten})</option>
                    <option value="NUR_MIRUS">Nur in Mirus (${data.nurMirus})</option>
                    <option value="GEMATCHT">Gematchte Mirus-Zeilen (${(data.gematchtZeilen || []).length})</option>
                </select>
                ${data.ohneLohn ? `<span style="font-size:12px;color:#64748b">${data.ohneLohn} MA ohne Lohn (Phantom) ausgeklammert</span>` : ''}
            </div>
            <button type="button" id="mfmPdfBtn" onclick="mfmPdf()" ${data.fehlend ? '' : 'disabled'}
                    style="padding:8px 14px;border:none;border-radius:12px;background:#3f3f3f;color:#fff;font-size:13px;font-weight:600;cursor:pointer;opacity:${data.fehlend ? 1 : .5}"
                    title="PDF mit allen Angaben der fehlenden MA — ein MA pro Seite, zum Erfassen in Mirus">📄 PDF Erfassungsliste</button>
        </div>`;
    document.getElementById('mfmResult').innerHTML = `<div id="mfmRows"></div>`;
    mfmRenderRows();
}

function mfmRenderRows() {
    if (!_mfmData) return;
    const filter = document.getElementById('mfmFilter')?.value || 'FEHLEND';
    const el = document.getElementById('mfmRows');
    if (!el) return;

    if (filter === 'FEHLEND') {
        const rows = _mfmData.fehlendeMa || [];
        if (!rows.length) {
            el.innerHTML = `<div style="padding:28px;text-align:center;color:#15803d;font-size:13.5px">✓ Alle aktiven OneCrew-MA dieser Filiale sind in Mirus vorhanden.</div>`;
            return;
        }
        el.innerHTML = rows.map(m => mfmMaCard(m)).join('');
        return;
    }

    let rows = [];
    if (filter === 'AUSGETRETEN') rows = _mfmData.ausgetretenZeilen || [];
    else if (filter === 'NUR_MIRUS') rows = _mfmData.nurMirusZeilen || [];
    else rows = _mfmData.gematchtZeilen || [];
    if (!rows.length) {
        el.innerHTML = `<div style="padding:28px;text-align:center;color:#64748b;font-size:13.5px">Keine Einträge.</div>`;
        return;
    }
    el.innerHTML = `
        <table style="width:100%;border-collapse:collapse;font-size:12.5px;background:rgba(255,255,255,.55);border-radius:12px;overflow:hidden">
            <thead><tr style="background:#f8fafc;color:#64748b;font-size:11px;text-transform:uppercase">
                <th style="padding:8px 10px;text-align:left">Pers. Nr.</th>
                <th style="padding:8px 10px;text-align:left">Name (Mirus)</th>
                <th style="padding:8px 10px;text-align:left">Geb.-Datum</th>
                <th style="padding:8px 10px;text-align:left">Eintritt</th>
                <th style="padding:8px 10px;text-align:left">Austritt</th>
                <th style="padding:8px 10px;text-align:left">Kostenstelle</th>
                <th style="padding:8px 10px;text-align:left">OneCrew</th>
                <th style="padding:8px 10px;text-align:left">Hinweis</th>
            </tr></thead>
            <tbody>${rows.map(z => `
                <tr style="border-top:1px solid #eee">
                    <td style="padding:7px 10px;font-family:monospace">${esc(z.personalnummer || '—')}</td>
                    <td style="padding:7px 10px;font-weight:600">${esc(((z.vorname || '') + ' ' + (z.nachname || '')).trim() || '—')}</td>
                    <td style="padding:7px 10px">${mfmDate(z.geburtsdatum)}</td>
                    <td style="padding:7px 10px">${mfmDate(z.eintritt)}</td>
                    <td style="padding:7px 10px">${mfmDate(z.austritt)}</td>
                    <td style="padding:7px 10px">${esc(z.kostenstelle || '—')}</td>
                    <td style="padding:7px 10px">${z.employeeId
                        ? `<button class="dok-menu-btn" style="min-width:auto;padding:3px 9px;font-size:12px" onclick="mfmOpenEmployee(${z.employeeId})">→ ${esc(z.oneCrewName || 'MA')}</button>
                           ${z.oneCrewAktiv === false ? '<span style="font-size:11px;color:#94a3b8"> [inaktiv]</span>' : ''}
                           ${z.matchArt && z.matchArt !== 'NUMMER' ? `<span style="font-size:11px;color:#92400e"> via ${esc(z.matchArt)}</span>` : ''}`
                        : '—'}</td>
                    <td style="padding:7px 10px;color:#64748b;font-size:12px">${esc(z.hinweis || '')}</td>
                </tr>`).join('')}
            </tbody>
        </table>`;
}

function mfmDate(iso) {
    if (!iso) return '—';
    const s = String(iso);
    return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
}

function mfmChf(v) {
    if (v === null || v === undefined) return '—';
    return Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' CHF';
}

function mfmKv(pairs) {
    return `<div style="display:grid;grid-template-columns:150px 1fr 150px 1fr;gap:3px 10px;font-size:12.5px">
        ${pairs.map(([k, v]) => k
            ? `<div style="color:#64748b">${esc(k)}</div><div style="color:#0f172a">${v ?? '—'}</div>`
            : `<div></div><div></div>`).join('')}
    </div>`;
}

function mfmSection(title, inner) {
    return `<div style="margin-top:10px">
        <div style="font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.04em;color:#0e7490;margin-bottom:4px">${esc(title)}</div>
        ${inner}
    </div>`;
}

function mfmMaCard(m) {
    // Reihenfolge + Beschriftung wie die Mirus-Masken (Walter 03.09.2026):
    // Persönliche Angaben → Adressen → Familie → Arbeitsverhältnis → Lohndaten.
    const name = `${esc(m.nachname || '')} ${esc(m.vorname || '')}`.trim() || '—';
    const e = s => esc(s == null || s === '' ? '—' : String(s));
    const aktiv = (m.vertraege || []).find(v => v.aktiv) || (m.vertraege || [])[0];

    const luecken = (m.luecken || []).length
        ? `<div style="margin-top:6px;padding:6px 10px;background:#fef2f2;border:1px solid #fecaca;border-radius:8px;font-size:12px;color:#991b1b">In OneCrew noch unvollständig: ${esc(m.luecken.join(', '))}</div>`
        : '';

    const persoenlich = mfmKv([
        ['Personal-Nr.', e(m.personalnummer)], ['Name', e(m.nachname)],
        ['Vorname', e(m.vorname)], ['Kurzname', e(m.kurzname)],
        ['Ledigname', e(m.ledigname)], ['Geburtsdatum', mfmDate(m.geburtsdatum)],
        ['Geschlecht', e(m.geschlecht)], ['Sozialversnr.', e(m.ahv)],
        ['Zivilstand', e(m.zivilstand) + (m.zivilstandSeit ? ` (seit ${mfmDate(m.zivilstandSeit)})` : '')], ['Sprachcode', e(m.sprache)],
        ['Anrede', e(m.anrede)], ['Briefanrede', e(m.briefanrede)],
        ['Konfession', e(m.konfession)], ['Nationalität', `${e(m.nationalitaetCode)} &nbsp; ${e(m.nationalitaet)}`],
        ['Geburtsland', '<span style="color:#94a3b8">— (nicht in OneCrew)</span>'], ['Heimatort / Geburtsort', e(m.heimatort)],
        ['Aufenthaltskategorie', e(m.aufenthaltskategorie)], ['Gültig bis', mfmDate(m.bewilligungBis)],
        ['ZEMIS-Nr.', e(m.zemis)], ['Krankenkasse', '<span style="color:#94a3b8">— (nicht in OneCrew)</span>'],
        ['Beruf', e(m.beruf)], ['Kaderstufe', m.kader ? 'Kader' : '—'],
        ['Kostenstelle', e(m.kostenstelleVorschlag)], ['', ''],
    ]);

    const adressen = mfmKv([
        ['Zweck', 'Hauptadresse'], ['Gültig ab', mfmDate(m.eintritt)],
        ['Strasse', e(m.strasse)], ['Strasse 2 / Postfach', '—'],
        ['PLZ / Ort / BFS', `${e(m.plz)} &nbsp; ${e(m.ort)} &nbsp; <span style="color:#64748b">${e(m.bfs)}</span>`], ['Kanton', `${e(m.kanton)} ${esc(m.kantonName || '')}`],
        ['Land', `${e(m.land)} ${(m.land || '').toUpperCase() === 'CH' ? 'Schweiz' : ''}`], ['Telefon', e(m.telefon)],
        ['Telefon 2', e(m.telefon2)], ['Email', e(m.email)],
    ]);

    const familie = (m.familie || []).length
        ? `<table style="width:100%;border-collapse:collapse;font-size:12.5px">
            <thead><tr style="color:#64748b;font-size:11px;text-transform:uppercase">
                <th style="padding:4px 8px;text-align:left">Typ</th><th style="padding:4px 8px;text-align:left">Name Vorname</th>
                <th style="padding:4px 8px;text-align:left">Geburtsdatum</th><th style="padding:4px 8px;text-align:left">Sozialversnr.</th>
                <th style="padding:4px 8px;text-align:left">Im Haushalt</th><th style="padding:4px 8px;text-align:left">In der CH</th>
            </tr></thead>
            <tbody>${m.familie.map(f => `<tr style="border-top:1px solid #eee">
                <td style="padding:4px 8px">${e(f.typ)}</td>
                <td style="padding:4px 8px">${esc(((f.nachname || '') + ' ' + (f.vorname || '')).trim() || '—')}</td>
                <td style="padding:4px 8px">${mfmDate(f.geburtsdatum)}</td>
                <td style="padding:4px 8px">${e(f.ahv)}</td>
                <td style="padding:4px 8px">${f.imHaushalt ? 'ja' : 'nein'}</td>
                <td style="padding:4px 8px">${f.inSchweiz ? 'ja' : 'nein'}</td>
            </tr>`).join('')}</tbody>
          </table>`
        : `<div style="font-size:12.5px;color:#64748b">Keine Familienmitglieder in OneCrew erfasst.</div>`;

    const arbeitsverhaeltnis = mfmKv([
        ['Eintritt', mfmDate(m.eintritt)], ['Austritt', '—'],
        ['Angestellt zu', m.angestelltZu != null ? `${Math.round(m.angestelltZu)} %` : '—'], ['Lohnbasis', m.lohnbasis === 'ML' ? 'ML (Monatslohn)' : m.lohnbasis === 'SL' ? 'SL (Stundenlohn)' : '—'],
        ['Vertragskategorie', e(aktiv?.modell)], ['L-GAV-pflichtig', m.lgavPflichtig ? 'ja' : 'nein'],
        ['NBU', m.teilzeitUnter8h ? 'nein (< 8 h/Woche)' : 'ja'], ['', ''],
    ]);

    const vertraege = (m.vertraege || []).length
        ? m.vertraege.map(v => {
            const modell = (v.modell || '').toUpperCase();
            const fix = modell === 'FIX' || modell === 'FIX-M';
            const pairs = [
                ['Vertragsbeginn', mfmDate(v.von)], ['Vertragsende', v.bis ? mfmDate(v.bis) : 'unbefristet'],
            ];
            if (fix) {
                pairs.push(['Pensum', v.pensumProzent != null ? `${v.pensumProzent} %` : '—']);
                pairs.push(['Monatslohn', mfmChf(v.monatslohn)]);
                if (v.monatslohnFte != null) { pairs.push(['Monatslohn 100 %', mfmChf(v.monatslohnFte)]); pairs.push(['', '']); }
            } else {
                pairs.push(['Stundenlohn', mfmChf(v.stundenlohn)]);
                pairs.push(['Wochenstunden', v.wochenstunden != null ? `${v.wochenstunden} h` : '—']);
                if (v.garantierteStunden != null) { pairs.push(['Garantierte Std./Wo.', `${v.garantierteStunden} h`]); pairs.push(['', '']); }
            }
            pairs.push(['Ferienzahlung', e(v.ferienzahlung)]);
            pairs.push(['Probezeit', v.probezeitMonate != null ? `${v.probezeitMonate} Mt.` + (v.probezeitBis ? ` (bis ${mfmDate(v.probezeitBis)})` : '') : '—']);
            return `<div style="margin-top:4px;padding:8px 10px;border:1px solid rgba(60,55,48,.15);border-radius:8px;${v.aktiv ? '' : 'opacity:.6'}">
                <div style="font-weight:700;font-size:12.5px;margin-bottom:4px">${e(v.modell)} · ${e(v.lohnart)} · ${e(v.funktion)}${v.funktionText ? ` (${esc(v.funktionText)})` : ''}${v.aktiv ? '' : ' <span style="font-weight:400;color:#94a3b8">[abgelaufen]</span>'}</div>
                ${mfmKv(pairs)}
            </div>`;
        }).join('')
        : `<div style="font-size:12.5px;color:#991b1b">Kein Vertrag in dieser Filiale erfasst.</div>`;

    const q = m.qst;
    const qst = q && q.pflichtig
        ? mfmKv([
            ['Tarifcode', e(q.tarif) + (q.tarifText ? ` &nbsp; ${esc(q.tarifText)}` : '')], ['Kanton', e(q.kanton)],
            ['Gemeinde / BFS', e(q.gemeinde) + (q.gemeindeBfs ? ` &nbsp; ${q.gemeindeBfs}` : '')], ['Kirchensteuer', q.kirchensteuer ? 'ja' : 'nein'],
            ['Anzahl Kinder', q.kinder ?? '—'], ['Gültig ab', mfmDate(q.gueltigAb)],
            ['Satz', q.prozent != null ? `${q.prozent} %` : '—'], ['', ''],
        ])
        : `<div style="font-size:12.5px;color:${(q?.hinweis || '').includes('prüfen') ? '#991b1b' : '#475569'}">${e(q?.hinweis)}</div>`;

    const banken = (m.banken || []).length
        ? m.banken.map(b => mfmKv([
            ['IBAN', `<span style="font-family:monospace">${e(b.iban)}</span>${b.hauptbank ? '' : ' <span style="color:#94a3b8">(Nebenkonto)</span>'}`], ['Bank', e(b.bank)],
            ['Kontoinhaber', e(b.kontoinhaber)], ['Aufteilung', b.aufteilung ? esc(b.aufteilung) : 'voll'],
        ])).join('<div style="height:4px"></div>')
        : `<div style="font-size:12.5px;color:#991b1b">Keine Bankverbindung in OneCrew erfasst.</div>`;

    return `
    <div style="background:rgba(255,255,255,.55);border:1px solid rgba(255,255,255,.7);border-radius:12px;padding:14px 16px;margin-bottom:12px;box-shadow:0 2px 10px rgba(60,55,48,.08)">
        <div style="display:flex;justify-content:space-between;gap:12px;align-items:flex-start;flex-wrap:wrap">
            <div>
                <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
                    <span style="display:inline-block;padding:3px 10px;border-radius:999px;font-size:11.5px;font-weight:700;background:#fee2e2;color:#991b1b">Fehlt in Mirus</span>
                    <span style="font-weight:700;color:#3f3f3f;font-size:15px">${m.anrede ? esc(m.anrede) + ' ' : ''}${name}</span>
                </div>
                <div style="margin-top:4px;font-size:12px;color:#64748b">
                    Eintritt: ${mfmDate(m.eintritt)} &nbsp;·&nbsp; Personal Nr.: <span style="font-family:monospace">${e(m.personalnummer)}</span> &nbsp;·&nbsp; Kostenstelle: ${e(m.kostenstelleVorschlag)} &nbsp;·&nbsp; Angestellt zu: ${m.angestelltZu != null ? Math.round(m.angestelltZu) + '%' : '—'} &nbsp;·&nbsp; Lohnbasis: ${e(m.lohnbasis)}
                </div>
            </div>
            <button class="dok-menu-btn" style="min-width:auto;padding:4px 10px;font-size:12px" onclick="mfmOpenEmployee(${m.employeeId})">→ MA öffnen</button>
        </div>
        ${luecken}
        ${mfmSection('Mitarbeiterdaten › Persönliche Angaben', persoenlich)}
        ${mfmSection('Mitarbeiterdaten › Adressen', adressen)}
        ${mfmSection('Mitarbeiterdaten › Familie', familie)}
        ${mfmSection('Arbeitszeitdaten › Arbeitsverhältnis', arbeitsverhaeltnis)}
        ${mfmSection('Lohndaten › Lohnbestandteile (Vertrag)', vertraege)}
        ${mfmSection('Lohndaten › Quellensteuer', qst)}
        ${mfmSection('Lohndaten › Bankverbindung', banken)}
    </div>`;
}

function mfmOpenEmployee(id) {
    if (typeof window.activeEmpId !== 'undefined') window.activeEmpId = id;
    if (typeof selectedEmployeeId !== 'undefined') selectedEmployeeId = id;
    showPage('mitarbeiter');
}
