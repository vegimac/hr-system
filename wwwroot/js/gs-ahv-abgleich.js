// ══════════════════════════════════════════════════════════════════════
// gs-ahv-abgleich.js — HR-Hub → Kontrolle → «AHV-Abgleich GastroSocial»
// ──────────────────────────────────────────────────────────────────────
// Walter 11.10.2026: Liste «Mitarbeitende» aus dem GastroSocial-Portal
// (Excel) gegen die MA der Sidebar-Filiale: wer muss abgemeldet werden,
// wer ist nicht gemeldet. Nur Auswertung. Backend: /api/imports/gastrosocial-abgleich
// ══════════════════════════════════════════════════════════════════════
let _gsaData = null;
// An-/Abmeldungen, die die Liste bestätigt und OneCrew noch nicht vermerkt hat (Walter 13.10.2026).
let _gsaUebernahme = [];

function gsaInit() {
    ['gsaAlert', 'gsaSummary', 'gsaResult'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.innerHTML = '';
    });
    const inp = document.getElementById('gsaFileInput');
    if (inp) inp.value = '';
    _gsaData = null;
    _gsaUebernahme = [];
    gsaUpdateBranchBanner();
}

function gsaUpdateBranchBanner() {
    const el = document.getElementById('gsaBranchBanner');
    if (!el) return;
    const cpId = typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null;
    const b = (typeof allBranches !== 'undefined' ? allBranches : []).find(x => x.id === cpId);
    if (!b) {
        el.innerHTML = '⚠ Bitte links eine Filiale wählen — jede Filiale hat bei GastroSocial ein eigenes Konto, also auch eine eigene Liste.';
        el.style.background = '#fef3c7';
        el.style.borderColor = '#fde68a';
        el.style.color = '#92400e';
        return;
    }
    el.innerHTML = `Filiale: <b>${esc(b.restaurantCode || '')} ${esc(b.city || b.name || '')}</b> — bitte die GastroSocial-Liste <b>dieser</b> Filiale hochladen.`;
    el.style.background = '#f6f3ee';
    el.style.borderColor = '#e5e0d6';
    el.style.color = '#6b6152';
}

async function gsaAnalyze() {
    const inp = document.getElementById('gsaFileInput');
    document.getElementById('gsaAlert').innerHTML = '';
    document.getElementById('gsaSummary').innerHTML = '';
    document.getElementById('gsaResult').innerHTML = '';
    if (!inp.files || inp.files.length === 0) {
        gsaFehler('Bitte die GastroSocial-Liste «Mitarbeitende» (XLSX) wählen.');
        return;
    }
    const cpId = typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0;
    if (!cpId) {
        gsaFehler('Bitte zuerst links eine Filiale wählen.');
        return;
    }
    const fd = new FormData();
    fd.append('file', inp.files[0]);
    fd.append('companyProfileId', String(cpId));

    const btn = document.getElementById('gsaAnalyzeBtn');
    if (btn) { btn.disabled = true; btn.textContent = '⏳ vergleiche…'; }
    try {
        const r = await fetch('/api/imports/gastrosocial-abgleich/analyze', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + (typeof authToken !== 'undefined' ? authToken : localStorage.hrToken) },
            body: fd
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            gsaFehler('Fehler vom Server (' + r.status + '): ' + (j.error || j.message || r.statusText));
            return;
        }
        const antwort = await r.json();
        _gsaData = antwort.ergebnis;
        _gsaUebernahme = antwort.uebernahme || [];
    } catch (e) {
        gsaFehler('Verbindung fehlgeschlagen: ' + e.message);
        return;
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'Vergleichen'; }
    }
    try {
        gsaRender(_gsaData);
    } catch (e) {
        gsaFehler('Anzeige fehlgeschlagen: ' + e.message + ' — bitte Seite neu laden (Cmd+Shift+R).');
    }
}

/** Fehler bleibt stehen (kein Ausblenden nach 4 s wie showPageAlert). */
function gsaFehler(msg) {
    const el = document.getElementById('gsaAlert');
    if (!el) return;
    el.innerHTML = `<div style="padding:10px 14px;background:#fee2e2;border:1px solid #fca5a5;border-radius:10px;color:#991b1b;font-size:13px">${esc(msg)}</div>`;
}

/** liste gesetzt ⇒ Kachel ist anklickbar und zeigt die Liste darunter. */
function gsaTile(n, label, bg, fg, liste) {
    const klick = liste
        ? ` class="gsa-tile" data-liste="${liste}" role="button" tabindex="0" title="Liste anzeigen"
             onclick="gsaZeigeListe('${liste}')" onkeydown="if(event.key==='Enter'||event.key===' '){event.preventDefault();gsaZeigeListe('${liste}')}"`
        : '';
    return `<div${klick} style="background:${bg};border-radius:10px;padding:12px 14px;color:${fg};border:2px solid transparent;${liste ? 'cursor:pointer;transition:transform .15s,box-shadow .15s,border-color .15s' : ''}">
        <div style="font-size:24px;font-weight:700">${n ?? 0}</div>
        <div style="font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.03em">${label}</div>
    </div>`;
}

function gsaZeigeListe(liste) {
    const sel = document.getElementById('gsaFilter');
    if (sel && sel.value !== liste) {
        sel.value = liste;
        sel.dispatchEvent(new Event('change'));
    } else {
        gsaRenderRows();
    }
}

function gsaMarkiereKachel(liste) {
    document.querySelectorAll('#gsaSummary .gsa-tile').forEach(k => {
        const an = k.dataset.liste === liste;
        k.style.borderColor = an ? '#3f3f3f' : 'transparent';
        k.style.boxShadow = an ? '0 4px 12px rgba(60,55,48,.22)' : 'none';
        k.style.transform = an ? 'translateY(-2px)' : 'none';
    });
}

function gsaRender(d) {
    const aa = (d.austrittObwohlAktiv || []).length;
    const ab = (d.abmelden || []).length, ng = (d.nichtGemeldet || []).length;
    const nz = (d.nichtZuordenbar || []).length, ok = (d.gemeldet || []).length;
    const fz = (d.fzPruefen || []).length;
    const ap = (d.angabenPruefen || []).length;
    const warnung = aa
        ? `<div style="margin-bottom:12px;padding:10px 14px;background:#fee2e2;border:1px solid #fca5a5;border-radius:10px;color:#991b1b;font-size:13px">
              ⚠ <b>${aa} MA</b> ${aa === 1 ? 'ist' : 'sind'} bei GastroSocial ausgetreten, in OneCrew aber aktiv — Austritt bei GastroSocial prüfen und rückgängig machen.
           </div>` : '';
    document.getElementById('gsaSummary').innerHTML = `
        ${warnung}
        ${d.mitAusgetretenen ? '' : `<div style="margin-bottom:12px;padding:10px 14px;background:#fef3c7;border:1px solid #fde68a;border-radius:10px;color:#92400e;font-size:13px">
            ⚠ Diese Liste enthält <b>nur Beschäftigte</b>. Für die Abmeldungen (und Ein-/Austritte innerhalb des Jahres) im GastroSocial-Portal die Liste <b>inklusive Ausgetretene</b> exportieren und nochmals hochladen.
        </div>`}
        <div id="gsaUebernahmeBar">${gsaUebernahmeHtml()}</div>
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(130px,1fr));gap:10px">
            ${gsaTile(d.gemeldetBeschaeftigt, 'Bei GastroSocial beschäftigt', '#f1f5f9', '#475569')}
            ${d.gemeldetAusgetreten ? gsaTile(d.gemeldetAusgetreten, 'Bei GastroSocial ausgetreten', '#f1f5f9', '#475569') : ''}
            ${gsaTile(d.oneCrewAktiv, 'OneCrew aktiv', '#ece9e2', '#6b6152')}
            ${aa ? gsaTile(aa, 'AHV-Austritt, aber aktiv', '#fecaca', '#7f1d1d', 'austrittObwohlAktiv') : ''}
            ${gsaTile(ab, 'Abmelden', ab ? '#fee2e2' : '#f0fdf4', ab ? '#991b1b' : '#15803d', 'abmelden')}
            ${gsaTile(ng, 'Nicht gemeldet', ng ? '#fef3c7' : '#f0fdf4', ng ? '#92400e' : '#15803d', 'nichtGemeldet')}
            ${gsaTile(nz, 'Nicht zuordenbar', nz ? '#e0e7ff' : '#f0fdf4', nz ? '#3730a3' : '#15803d', 'nichtZuordenbar')}
            ${gsaTile(fz, 'FZ prüfen', fz ? '#fce7f3' : '#f0fdf4', fz ? '#9d174d' : '#15803d', 'fzPruefen')}
            ${gsaTile(ap, 'Angaben prüfen', ap ? '#ffedd5' : '#f0fdf4', ap ? '#9a3412' : '#15803d', 'angabenPruefen')}
            ${gsaTile(ok, 'Passt', '#dcfce7', '#166534', 'gemeldet')}
        </div>
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center;margin:14px 0 10px">
            <span style="font-size:12px;color:#64748b;font-weight:600">Ansicht:</span>
            <select id="gsaFilter" onchange="gsaRenderRows()">
                <option value="austrittObwohlAktiv">⚠ AHV-Austritt, aber in OneCrew aktiv (${aa})</option>
                <option value="abmelden">Abmelden — bei GastroSocial gemeldet, bei uns ausgetreten (${ab})</option>
                <option value="nichtGemeldet">Nicht gemeldet — bei uns aktiv, fehlt bei GastroSocial (${ng})</option>
                <option value="nichtZuordenbar">Nicht zuordenbar — in OneCrew nicht gefunden (${nz})</option>
                <option value="fzPruefen">FZ prüfen — Familienzulagen GastroSocial ≠ Zulagen bei den Kindern (${fz})</option>
                <option value="angabenPruefen">Angaben prüfen — AHV-Nr., Name, Vorname, Geschlecht, Geb.-Datum weichen ab (${ap})</option>
                <option value="gemeldet">Passt (${ok})</option>
            </select>
            ${d.ohneLohn ? `<span style="font-size:12px;color:#64748b">${d.ohneLohn} MA ohne Lohn (Phantom) nicht verlangt</span>` : ''}
        </div>`;
    document.getElementById('gsaResult').innerHTML = `<div id="gsaRows"></div>`;
    const sel = document.getElementById('gsaFilter');
    if (sel) sel.value = aa ? 'austrittObwohlAktiv' : ab ? 'abmelden' : ng ? 'nichtGemeldet' : nz ? 'nichtZuordenbar' : 'gemeldet';
    gsaRenderRows();
}

function gsaRenderRows() {
    if (!_gsaData) return;
    const filter = document.getElementById('gsaFilter')?.value || 'abmelden';
    gsaMarkiereKachel(filter);
    const el = document.getElementById('gsaRows');
    if (!el) return;
    const rows = _gsaData[filter] || [];
    if (!rows.length) {
        const leer = {
            austrittObwohlAktiv: '✓ Niemand ist bei GastroSocial ausgetreten, der bei uns noch arbeitet.',
            abmelden: '✓ Niemand mehr abzumelden.',
            nichtGemeldet: '✓ Alle aktiven MA mit Lohn sind bei GastroSocial gemeldet.',
            nichtZuordenbar: '✓ Alle Zeilen der Liste sind einem MA zugeordnet.',
            fzPruefen: '✓ Familienzulagen stimmen: wer bei GastroSocial FZ hat, hat in OneCrew eine Zulage beim Kind — und umgekehrt.',
            angabenPruefen: '✓ AHV-Nummer, Name, Vorname, Geschlecht und Geburtsdatum stimmen mit GastroSocial überein.',
            gemeldet: 'Keine Einträge.'
        }[filter];
        el.innerHTML = `<div style="padding:28px;text-align:center;color:#15803d;font-size:13.5px">${leer}</div>`;
        return;
    }
    const aufgabe = {
        austrittObwohlAktiv: 'Bei GastroSocial wurde ein Austritt gemeldet, die Person arbeitet laut OneCrew aber noch in dieser Filiale. Austritt bei GastroSocial rückgängig machen — oder in OneCrew den Austritt nachtragen, falls sie wirklich weg ist.',
        abmelden: 'Bei GastroSocial austreten lassen (Austritt melden).',
        nichtGemeldet: 'Bei GastroSocial anmelden (Eintritt melden).',
        nichtZuordenbar: 'Prüfen: arbeitet die Person noch bei uns? Sonst abmelden; sonst AHV-Nummer in OneCrew nachtragen.',
        fzPruefen: 'Vergleich «Leistungen FZ» bei GastroSocial mit den heute gültigen Zulagen bei den Kindern in OneCrew (MA → Familie). Fehlt die Zulage in OneCrew, wird sie im Lohn nicht ausbezahlt.',
        angabenPruefen: 'Die Personalien bei GastroSocial und in OneCrew stimmen nicht überein oder fehlen in OneCrew. Mit Ausweis klären: falsch in OneCrew → beim MA korrigieren (bzw. in easy@work, wenn es von dort kommt), falsch bei GastroSocial → dort melden.',
        gemeldet: ''
    }[filter];
    const th = t => `<th style="padding:8px 10px;text-align:left">${t}</th>`;
    const td = (v, st = '') => `<td style="padding:7px 10px;${st}">${v}</td>`;
    el.innerHTML = `
        ${aufgabe ? `<div style="font-size:12.5px;color:#475569;margin:0 0 8px">${esc(aufgabe)}</div>` : ''}
        <table style="width:100%;border-collapse:collapse;font-size:12.5px;background:rgba(255,255,255,.55);border-radius:12px;overflow:hidden">
            <thead><tr style="background:#f8fafc;color:#64748b;font-size:11px;text-transform:uppercase">
                ${th('Name')}${th('AHV-Nummer')}${th('Geb.-Datum')}${th('Leistungen GS')}${th('Zulagen OneCrew')}${th('GastroSocial')}${th('Eintritt')}${th('Austritt')}${th('OneCrew')}${th('Hinweis')}
            </tr></thead>
            <tbody>${rows.map(z => {
                const fz = (z.leistungen || '').toUpperCase().includes('FZ');
                const fzHinweis = fz && filter === 'abmelden'
                    ? '<div style="color:#991b1b;font-weight:600">Bezieht Familienzulagen — auch FZ abmelden</div>'
                    : fz && filter === 'austrittObwohlAktiv'
                    ? '<div style="color:#991b1b;font-weight:600">Familienzulagen prüfen — evtl. mit dem Austritt gestoppt</div>' : '';
                const aus = /^aus/i.test(z.status || '');
                const gs = z.status
                    ? `<span style="color:${aus ? '#991b1b' : '#166534'};font-weight:600">${esc(z.status)}</span>${z.statusaenderung ? `<div style="font-size:11px;color:#64748b">${aus ? 'per ' : 'seit '}${gsaDate(z.statusaenderung)}</div>` : ''}`
                    : '—';
                return `<tr style="border-top:1px solid #eee">
                    ${td(esc(z.name || z.oneCrewName || '—'), 'font-weight:600')}
                    ${td(esc(z.ahv || z.oneCrewAhv || '—'), 'font-family:monospace;white-space:nowrap')}
                    ${td(gsaDate(z.geburtsdatum))}
                    ${td(z.leistungen ? `<span style="display:inline-block;padding:2px 8px;border-radius:999px;background:${fz ? '#fee2e2' : '#f1f5f9'};color:${fz ? '#991b1b' : '#475569'};font-weight:600">${esc(z.leistungen)}</span>` : '—')}
                    ${td(z.oneCrewFz ? esc(z.oneCrewFz) : (z.employeeId ? '<span style="color:#94a3b8">keine</span>' : '—'), 'font-size:12px')}
                    ${td(gs, 'white-space:nowrap')}
                    ${td(gsaDate(z.eintritt))}
                    ${td(gsaDate(z.austritt))}
                    ${td(z.employeeId
                        ? `<button class="dok-menu-btn" style="min-width:auto;padding:3px 9px;font-size:12px" onclick="gsaOpenEmployee(${z.employeeId})">→ ${esc(z.oneCrewName || 'MA')}</button>${z.personalnummer ? ` <span style="font-size:11px;color:#94a3b8;font-family:monospace">${esc(z.personalnummer)}</span>` : ''}`
                        : '—')}
                    ${td(filter === 'angabenPruefen'
                        ? (z.angaben || []).map(a => `<div style="color:#9a3412">• ${esc(a)}</div>`).join('')
                        : fzHinweis + esc(z.hinweis || ''), 'color:#64748b;font-size:12px')}
                </tr>`;
            }).join('')}</tbody>
        </table>`;
}

function gsaUebernahmeHtml() {
    const n = _gsaUebernahme.length;
    if (!n) return '';
    const an = _gsaUebernahme.filter(x => x.art === 'ANMELDUNG').length;
    const ab = n - an;
    const teile = [an ? `${an} Anmeldung${an === 1 ? '' : 'en'}` : '', ab ? `${ab} Abmeldung${ab === 1 ? '' : 'en'}` : ''].filter(Boolean).join(' und ');
    return `<div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:12px;padding:10px 14px;background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.14);border-radius:12px;font-size:12.5px;color:#3f3f3f">
        <span style="flex:1;min-width:240px">Laut dieser Liste sind <b>${teile}</b> bei GastroSocial schon gemacht, in OneCrew aber noch nicht vermerkt.
            <span style="color:#8b8b8b">Übernehmen beruhigt die To-dos «GastroSocial-Anmeldung/-Abmeldung fehlt».</span></span>
        <button onclick="gsaUebernahmeZeigen()" style="background:rgba(255,255,255,0.6);border:1px solid rgba(60,55,48,0.2);color:#3f3f3f;border-radius:999px;padding:6px 14px;font-size:12.5px;font-weight:600;cursor:pointer">Liste ansehen</button>
        <button id="gsaUebernahmeBtn" onclick="gsaUebernehmen()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 16px;font-size:12.5px;font-weight:600;cursor:pointer">Ergebnis übernehmen (${n})</button>
    </div>`;
}

function gsaUebernahmeZeigen() {
    const zeilen = _gsaUebernahme.map(x => `${x.name} — ${x.art === 'ANMELDUNG' ? 'Eintritt' : 'Austritt'} ${gsaDate(x.datum)}`).join('\n');
    liquidConfirm(zeilen, { title: 'Wird als gemeldet vermerkt', yesLabel: 'Übernehmen', noLabel: 'Schliessen' })
        .then(ok => { if (ok) gsaUebernehmen(true); });
}

async function gsaUebernehmen(bestaetigt) {
    const cpId = typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0;
    if (!cpId || !_gsaUebernahme.length) return;
    const ok = bestaetigt || await liquidConfirm(`${_gsaUebernahme.length} An-/Abmeldungen als «bei GastroSocial gemeldet» vermerken?`,
        { title: 'Ergebnis übernehmen', yesLabel: 'Übernehmen', noLabel: 'Abbrechen' });
    if (!ok) return;
    const btn = document.getElementById('gsaUebernahmeBtn');
    if (btn) btn.disabled = true;
    try {
        const r = await fetch('/api/gastrosocial-meldung/abgleich-uebernehmen', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + (typeof authToken !== 'undefined' ? authToken : localStorage.hrToken), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                companyProfileId: cpId,
                eintraege: _gsaUebernahme.map(x => ({ employeeId: x.employeeId, art: x.art, datum: x.datum })),
            }),
        });
        if (!r.ok) { gsaFehler('Übernehmen fehlgeschlagen (' + r.status + ').'); return; }
        const j = await r.json();
        _gsaUebernahme = [];
        const bar = document.getElementById('gsaUebernahmeBar');
        if (bar) bar.innerHTML = `<div style="margin-bottom:12px;padding:10px 14px;background:#f0fdf4;border:1px solid #bbf7d0;border-radius:12px;font-size:12.5px;color:#166534">✓ ${j.neu} An-/Abmeldungen als gemeldet vermerkt.</div>`;
    } catch (e) {
        gsaFehler('Verbindung fehlgeschlagen: ' + e.message);
    } finally {
        if (btn) btn.disabled = false;
    }
}

function gsaDate(iso) {
    if (!iso) return '—';
    const s = String(iso);
    return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
}

function gsaOpenEmployee(id) {
    const tab = document.getElementById('gsaFilter')?.value === 'fzPruefen' ? 'familie' : null;
    if (typeof dashOpenEmployee === 'function') { dashOpenEmployee(id, tab); return; }
    window.activeEmpId = id;
    showPage('mitarbeiter');
}
