// ══════════════════════════════════════════════════════════════════════
// gs-fz-abgleich.js — HR-Hub → Kontrolle → «FZ-Abgleich GastroSocial»
// ──────────────────────────────────────────────────────────────────────
// Walter 12.10.2026: «Familienzulagenbescheinigung» aus dem GastroSocial-
// Portal (Excel) gegen die Zulagen bei den Kindern der MA der Sidebar-
// Filiale, Monat für Monat. Nur Auswertung.
// Backend: /api/imports/gastrosocial-fz-abgleich
// ══════════════════════════════════════════════════════════════════════
let _gsfData = null;
let _gsfFile = null;
let _gsfFiliale = '';

function gsfInit() {
    ['gsfAlert', 'gsfSummary', 'gsfResult'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.innerHTML = '';
    });
    const inp = document.getElementById('gsfFileInput');
    if (inp) inp.value = '';
    _gsfData = null;
    _gsfFile = null;
    _gsfFiliale = '';
    gsfUpdateBranchBanner();
}

function gsfUpdateBranchBanner() {
    const el = document.getElementById('gsfBranchBanner');
    if (!el) return;
    const cpId = typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null;
    const b = (typeof allBranches !== 'undefined' ? allBranches : []).find(x => x.id === cpId);
    if (!b) {
        el.innerHTML = '⚠ Bitte links eine Filiale wählen — jede Filiale hat bei GastroSocial ein eigenes Konto, also auch eine eigene Bescheinigung.';
        el.style.background = '#fef3c7';
        el.style.borderColor = '#fde68a';
        el.style.color = '#92400e';
        return;
    }
    el.innerHTML = `Filiale: <b>${esc(b.restaurantCode || '')} ${esc(b.city || b.name || '')}</b> — in AKISnet: <b>FZ AK → FZ-Bescheinigung anzeigen</b>, Excel dieser Filiale hochladen (nicht die Mitarbeitenden-Liste).`;
    el.style.background = '#f6f3ee';
    el.style.borderColor = '#e5e0d6';
    el.style.color = '#6b6152';
}

async function gsfAnalyze() {
    const inp = document.getElementById('gsfFileInput');
    ['gsfAlert', 'gsfSummary', 'gsfResult'].forEach(id => { document.getElementById(id).innerHTML = ''; });
    if (!inp.files || inp.files.length === 0) {
        gsfFehler('Bitte die Excel aus AKISnet «FZ-Bescheinigung anzeigen» wählen (nicht die Mitarbeitenden-Liste).');
        return;
    }
    const cpId = typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0;
    if (!cpId) {
        gsfFehler('Bitte zuerst links eine Filiale wählen.');
        return;
    }
    _gsfFile = inp.files[0];
    const fd = new FormData();
    fd.append('file', _gsfFile);
    fd.append('companyProfileId', String(cpId));

    const btn = document.getElementById('gsfAnalyzeBtn');
    if (btn) { btn.disabled = true; btn.textContent = '⏳ vergleiche…'; }
    try {
        const r = await fetch('/api/imports/gastrosocial-fz-abgleich/analyze', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + (typeof authToken !== 'undefined' ? authToken : localStorage.hrToken) },
            body: fd
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            gsfFehler('Fehler vom Server (' + r.status + '): ' + (j.error || j.message || r.statusText));
            return;
        }
        const j = await r.json();
        _gsfData = j.ergebnis;
        _gsfFiliale = j.filiale || '';
    } catch (e) {
        gsfFehler('Verbindung fehlgeschlagen: ' + e.message);
        return;
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'Vergleichen'; }
    }
    try {
        gsfRender(_gsfData);
    } catch (e) {
        gsfFehler('Anzeige fehlgeschlagen: ' + e.message + ' — bitte Seite neu laden (Cmd+Shift+R).');
    }
}

/** PDF der offenen Pendenzen (ohne «Passt») — Checkbox zum Abhaken. */
async function gsfPdf() {
    if (!_gsfFile || !_gsfData) {
        gsfFehler('Bitte zuerst die Bescheinigung vergleichen.');
        return;
    }
    const cpId = typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0;
    if (!cpId) {
        gsfFehler('Bitte zuerst links eine Filiale wählen.');
        return;
    }
    const fd = new FormData();
    fd.append('file', _gsfFile);
    fd.append('companyProfileId', String(cpId));
    const btn = document.getElementById('gsfPdfBtn');
    if (btn) { btn.disabled = true; btn.textContent = '⏳ PDF…'; }
    try {
        const r = await fetch('/api/imports/gastrosocial-fz-abgleich/pdf', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + (typeof authToken !== 'undefined' ? authToken : localStorage.hrToken) },
            body: fd
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            gsfFehler('PDF fehlgeschlagen: ' + (j.error || j.message || r.status));
            return;
        }
        const blob = await r.blob();
        const cd = r.headers.get('Content-Disposition') || '';
        const fn = (typeof cdFilename === 'function' ? cdFilename(cd, null) : null)
            || `FZ-Abgleich-Pendenzen-${(_gsfFiliale || 'Filiale').replace(/\s+/g, '-')}.pdf`;
        if (typeof previewFileModal === 'function') await previewFileModal(blob, fn);
        else if (typeof saveBlobAsk === 'function') await saveBlobAsk(blob, fn);
    } catch (e) {
        gsfFehler('Verbindung fehlgeschlagen: ' + e.message);
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = '📄 Pendenzen-PDF'; }
    }
}

/** Fehler bleibt stehen (kein Ausblenden nach 4 s wie showPageAlert). */
function gsfFehler(msg) {
    const el = document.getElementById('gsfAlert');
    if (!el) return;
    el.innerHTML = `<div style="padding:10px 14px;background:#fee2e2;border:1px solid #fca5a5;border-radius:10px;color:#991b1b;font-size:13px">${esc(msg)}</div>`;
}

function gsfChf(v) {
    if (v == null) return '—';
    return Number(v).toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, "'");
}

function gsfDate(iso) {
    if (!iso) return '—';
    const s = String(iso);
    return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
}

/** liste gesetzt ⇒ Kachel ist anklickbar und zeigt die Liste darunter. */
function gsfTile(n, label, bg, fg, liste) {
    const klick = liste
        ? ` class="gsf-tile" data-liste="${liste}" role="button" tabindex="0" title="Liste anzeigen"
             onclick="gsfZeigeListe('${liste}')" onkeydown="if(event.key==='Enter'||event.key===' '){event.preventDefault();gsfZeigeListe('${liste}')}"`
        : '';
    return `<div${klick} style="background:${bg};border-radius:10px;padding:12px 14px;color:${fg};border:2px solid transparent;${liste ? 'cursor:pointer;transition:transform .15s,box-shadow .15s,border-color .15s' : ''}">
        <div style="font-size:24px;font-weight:700">${n ?? 0}</div>
        <div style="font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.03em">${label}</div>
    </div>`;
}

function gsfZeigeListe(liste) {
    const sel = document.getElementById('gsfFilter');
    if (sel && sel.value !== liste) {
        sel.value = liste;
        sel.dispatchEvent(new Event('change'));
    } else {
        gsfRenderRows();
    }
}

function gsfMarkiereKachel(liste) {
    document.querySelectorAll('#gsfSummary .gsf-tile').forEach(k => {
        const an = k.dataset.liste === liste;
        k.style.borderColor = an ? '#3f3f3f' : 'transparent';
        k.style.boxShadow = an ? '0 4px 12px rgba(60,55,48,.22)' : 'none';
        k.style.transform = an ? 'translateY(-2px)' : 'none';
    });
}

/** ansicht: nach einer Korrektur dieselbe Liste wieder zeigen (solange sie nicht leer ist). */
function gsfRender(d, ansicht) {
    const fe = (d.fehlt || []).length, ab = (d.abweichung || []).length;
    const no = (d.nurOneCrew || []).length, nz = (d.nichtZuordenbar || []).length, ok = (d.passt || []).length;
    const ap = (d.angabenPruefen || []).length;
    const zeitraum = d.von && d.bis ? `${gsfDate(d.von)} – ${gsfDate(d.bis)}` : '';
    document.getElementById('gsfSummary').innerHTML = `
        ${zeitraum ? `<div style="font-size:12.5px;color:#64748b;margin-bottom:10px">Bescheinigung ${zeitraum} · ${d.zeilen} Zeilen · Total GastroSocial CHF ${gsfChf(d.totalGs)}</div>` : ''}
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(130px,1fr));gap:10px">
            ${gsfTile(d.kinder, 'Kinder bei GastroSocial', '#f1f5f9', '#475569')}
            ${gsfTile(fe, 'Fehlt in OneCrew', fe ? '#fee2e2' : '#f0fdf4', fe ? '#991b1b' : '#15803d', 'fehlt')}
            ${gsfTile(ab, 'Abweichung', ab ? '#fef3c7' : '#f0fdf4', ab ? '#92400e' : '#15803d', 'abweichung')}
            ${gsfTile(no, 'Nur in OneCrew', no ? '#fce7f3' : '#f0fdf4', no ? '#9d174d' : '#15803d', 'nurOneCrew')}
            ${gsfTile(nz, 'Nicht zuordenbar', nz ? '#e0e7ff' : '#f0fdf4', nz ? '#3730a3' : '#15803d', 'nichtZuordenbar')}
            ${gsfTile(ap, 'Angaben prüfen', ap ? '#ffedd5' : '#f0fdf4', ap ? '#9a3412' : '#15803d', 'angabenPruefen')}
            ${gsfTile(ok, 'Passt', '#dcfce7', '#166534', 'passt')}
        </div>
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center;margin:14px 0 10px">
            <span style="font-size:12px;color:#64748b;font-weight:600">Ansicht:</span>
            <select id="gsfFilter" onchange="gsfRenderRows()">
                <option value="fehlt">Fehlt in OneCrew — GastroSocial zahlt, OneCrew nicht (${fe})</option>
                <option value="abweichung">Abweichung — Beginn/Ende oder Ansatz verschieden (${ab})</option>
                <option value="nurOneCrew">Nur in OneCrew — GastroSocial bescheinigt nichts (${no})</option>
                <option value="nichtZuordenbar">Nicht zuordenbar — Bezüger in OneCrew nicht gefunden (${nz})</option>
                <option value="angabenPruefen">Angaben prüfen — AHV, Name, Geburtsdatum … weichen ab (${ap})</option>
                <option value="passt">Passt (${ok})</option>
            </select>
            <button type="button" id="gsfPdfBtn" onclick="gsfPdf()"
                    style="margin-left:auto;padding:8px 14px;border:none;border-radius:12px;background:#3f3f3f;color:#fff;font-size:13px;font-weight:600;cursor:pointer"
                    title="Offene Pendenzen als PDF (Checkbox zum Abhaken)">📄 Pendenzen-PDF</button>
        </div>`;
    document.getElementById('gsfResult').innerHTML = `<div id="gsfRows"></div>`;
    const sel = document.getElementById('gsfFilter');
    if (sel) sel.value = ansicht && (d[ansicht] || []).length ? ansicht
        : fe ? 'fehlt' : ab ? 'abweichung' : no ? 'nurOneCrew' : nz ? 'nichtZuordenbar' : ap ? 'angabenPruefen' : 'passt';
    gsfRenderRows();
}

function gsfRenderRows() {
    if (!_gsfData) return;
    const filter = document.getElementById('gsfFilter')?.value || 'fehlt';
    gsfMarkiereKachel(filter);
    const el = document.getElementById('gsfRows');
    if (!el) return;
    const rows = _gsfData[filter] || [];
    if (!rows.length) {
        const leer = {
            fehlt: '✓ Für jedes Kind, für das GastroSocial zahlt, ist in OneCrew eine Zulage erfasst.',
            abweichung: '✓ Beginn, Ende und Ansatz stimmen überall überein.',
            nurOneCrew: '✓ Keine Zulage in OneCrew, die GastroSocial nicht bescheinigt.',
            nichtZuordenbar: '✓ Alle Bezüger sind einem MA zugeordnet.',
            angabenPruefen: '✓ AHV-Nummern, Namen und Geburtsdaten stimmen mit GastroSocial überein.',
            passt: 'Keine Einträge.'
        }[filter];
        el.innerHTML = `<div style="padding:28px;text-align:center;color:#15803d;font-size:13.5px">${leer}</div>`;
        return;
    }
    const aufgabe = {
        fehlt: 'GastroSocial zahlt für dieses Kind, in OneCrew fehlt das Kind oder die Zulage in diesen Monaten — im Lohn wird sie nicht ausbezahlt. Kind/Zulage im MA → Familie erfassen.',
        abweichung: 'Beginn, Ende oder Ansatz weichen ab. Zulage beim Kind prüfen (Gültig ab/bis, Art, Satz). Anteilige Teilmonate und Direktauszahlung (Ansatz leer) sind kein Alarm.',
        nurOneCrew: 'OneCrew zahlt eine Zulage, GastroSocial bescheinigt für dieses Kind nichts — Antrag fehlt, anderer Elternteil bezieht sie, oder die Zulage in OneCrew beenden.',
        nichtZuordenbar: 'Bezüger in OneCrew nicht gefunden — AHV-Nummer beim MA nachtragen oder prüfen, ob die Person bei uns arbeitet.',
        angabenPruefen: 'Die Angaben zum Kind oder MA stimmen nicht mit GastroSocial überein oder fehlen in OneCrew. Mit Ausweis/Geburtsurkunde klären: falsch in OneCrew → im MA → Familie korrigieren, falsch bei GastroSocial → dort melden. (Die Bescheinigung enthält vom Kind nur AHV-Nr., Vorname und Geburtsdatum — Nachname und Geschlecht werden nur geprüft, wenn der Export sie mitliefert.)',
        passt: ''
    }[filter];
    const th = (t, st = '') => `<th style="padding:8px 6px;text-align:left;${st}">${t}</th>`;
    const td = (v, st = '', attrs = '') => `<td ${attrs} style="padding:8px 6px;vertical-align:top;${st}">${v}</td>`;
    const maZelle = z => z.employeeId
        ? `<button type="button" class="dok-menu-btn" style="min-width:auto;padding:5px 10px;font-size:13px;font-weight:600;cursor:pointer"
                onclick="gsfOpenEmployee(${z.employeeId})" title="Mitarbeiter öffnen">${esc(z.oneCrewName || 'MA')}</button>
            ${z.personalnummer ? `<div style="font-size:11.5px;color:#94a3b8;font-family:monospace;margin-top:3px">${esc(z.personalnummer)}</div>` : ''}`
        : '<span style="color:#94a3b8">—</span>';
    const gruppen = gsfGruppenNachMa(rows);
    el.innerHTML = `
        ${aufgabe ? `<div style="font-size:13.5px;color:#475569;margin:0 0 10px;line-height:1.45">${esc(aufgabe)}</div>` : ''}
        <table style="width:100%;table-layout:fixed;border-collapse:collapse;font-size:14px;background:rgba(255,255,255,.55);border-radius:12px;overflow:hidden">
            <colgroup>
                <col style="width:11%"><col style="width:11%"><col style="width:10%"><col style="width:7%">
                <col style="width:18%"><col style="width:6%"><col style="width:13%"><col style="width:6%"><col style="width:18%">
            </colgroup>
            <thead><tr style="background:#f8fafc;color:#64748b;font-size:11.5px;text-transform:uppercase;letter-spacing:.02em">
                ${th('MA')}${th('Bezüger')}${th('Kind')}${th('Geb.')}${th('GastroSocial')}${th('Total GS', 'text-align:right')}${th('Zulagen OneCrew')}${th('Total OC', 'text-align:right')}${th('Hinweis')}
            </tr></thead>
            <tbody>${gruppen.map(g => g.kinder.map((z, i) => {
                const diff = z.gsTotal != null && z.ocTotal != null && Number(z.gsTotal) !== Number(z.ocTotal);
                const span = g.kinder.length > 1 ? `rowspan="${g.kinder.length}"` : '';
                const maBez = i === 0
                    ? `${td(maZelle(z), 'border-right:1px solid rgba(60,55,48,.06)', span)}
                       ${td(`<div style="font-weight:600">${esc(z.bezueger || z.oneCrewName || '—')}</div><div style="font-size:11.5px;color:#94a3b8;font-family:monospace;margin-top:2px">${esc(z.bezuegerAhv || '')}</div>`,
                           'border-right:1px solid rgba(60,55,48,.06)', span)}`
                    : '';
                return `<tr style="border-top:1px solid #eee">
                    ${maBez}
                    ${td(`<div style="font-weight:600">${esc(z.kind || '—')}</div><div style="font-size:11.5px;color:#94a3b8;font-family:monospace;margin-top:2px">${esc(z.kindAhv || '')}</div>${z.kindAhvUebernehmen && z.familyMemberId
                        ? `<button class="dok-menu-btn" style="min-width:auto;padding:3px 8px;font-size:12px;margin-top:5px" onclick="gsfKindAhvUebernehmen('${filter}',${z.familyMemberId})" title="AHV-Nummer von GastroSocial beim Kind in OneCrew eintragen">＋ AHV übernehmen</button>`
                        : ''}`)}
                    ${td(gsfDate(z.kindGeburt), 'white-space:nowrap')}
                    ${td(gsfZeitraeumeHtml(z.gastroSocial))}
                    ${td(z.gsTotal != null && filter !== 'nurOneCrew' ? gsfChf(z.gsTotal) : '—', 'text-align:right;white-space:nowrap;font-variant-numeric:tabular-nums')}
                    ${td(z.oneCrew ? gsfZeitraeumeHtml(z.oneCrew) : (z.employeeId ? '<span style="color:#94a3b8">keine</span>' : '—'))}
                    ${td(z.ocTotal != null ? gsfChf(z.ocTotal) : '—', `text-align:right;white-space:nowrap;font-variant-numeric:tabular-nums;${diff ? 'color:#991b1b;font-weight:600' : ''}`)}
                    ${td(filter === 'angabenPruefen'
                        ? (z.angaben || []).map(a => `<div style="color:#9a3412;margin-bottom:3px">• ${esc(a)}</div>`).join('')
                        : esc(z.hinweis || ''), 'color:#64748b;font-size:13px;line-height:1.4')}
                </tr>`;
            }).join('')).join('')}</tbody>
        </table>`;
}

/** Geschwister unter demselben MA/Bezüger zusammenfassen (Reihenfolge beibehalten). */
function gsfGruppenNachMa(rows) {
    const map = new Map();
    const order = [];
    rows.forEach(z => {
        const key = z.employeeId != null ? 'e:' + z.employeeId
            : (z.bezuegerAhv ? 'a:' + z.bezuegerAhv : 'r:' + order.length);
        if (!map.has(key)) {
            map.set(key, { kinder: [] });
            order.push(key);
        }
        map.get(key).kinder.push(z);
    });
    return order.map(k => map.get(k));
}

/** Mehrere Zeiträume (getrennt mit «;») untereinander, neuestes Datum oben. */
function gsfZeitraeumeHtml(text) {
    if (!text) return '<span style="color:#94a3b8">keine</span>';
    const teile = String(text).split(/\s*;\s*/).map(s => s.trim()).filter(Boolean);
    if (teile.length === 0) return '<span style="color:#94a3b8">keine</span>';
    if (teile.length === 1) return `<div style="line-height:1.45">${esc(teile[0])}</div>`;
    const sorted = teile.slice().sort((a, b) => gsfZeitStartMs(b) - gsfZeitStartMs(a));
    return `<div style="display:flex;flex-direction:column;gap:4px;line-height:1.4">${sorted.map((t, i) =>
        `<div style="padding:2px 0${i < sorted.length - 1 ? ';border-bottom:1px solid rgba(60,55,48,.08)' : ''}">${esc(t)}</div>`).join('')}</div>`;
}

function gsfZeitStartMs(s) {
    const m = String(s).match(/(\d{2})\.(\d{2})\.(\d{4})/);
    if (!m) return 0;
    return Date.UTC(+m[3], +m[2] - 1, +m[1]);
}

async function gsfKindAhvUebernehmen(liste, memberId) {
    const z = (_gsfData?.[liste] || []).find(x => x.familyMemberId === memberId);
    if (!z?.kindAhvUebernehmen) return;
    if (!(await liquidConfirm(`AHV-Nummer ${z.kindAhvUebernehmen} bei ${z.kind || 'diesem Kind'} in OneCrew eintragen?`,
            { title: 'AHV-Nummer übernehmen', yesLabel: 'Eintragen', noLabel: 'Abbrechen' }))) return;
    try {
        const r = await fetch('/api/imports/gastrosocial-fz-abgleich/kind-ahv', {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ familyMemberId: memberId, ahv: z.kindAhvUebernehmen })
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { gsfFehler(j.message || j.error || ('Fehler ' + r.status)); return; }
        const weg = h => !h.startsWith('AHV-Nummer des Kindes fehlt');
        Object.keys(_gsfData).forEach(k => {
            if (!Array.isArray(_gsfData[k])) return;
            _gsfData[k].filter(x => x.familyMemberId === memberId).forEach(x => {
                x.kindAhvUebernehmen = null;
                x.hinweis = (x.hinweis || '').split(' · ').filter(weg).join(' · ') || null;
                x.angaben = (x.angaben || []).filter(weg);
            });
        });
        _gsfData.angabenPruefen = (_gsfData.angabenPruefen || []).filter(x => (x.angaben || []).length > 0);
        if (typeof showToast === 'function') showToast(`AHV-Nummer bei ${z.kind || 'Kind'} eingetragen.`, 'success');
        gsfRender(_gsfData, liste);
    } catch (e) {
        gsfFehler('Verbindung fehlgeschlagen: ' + e.message);
    }
}

function gsfOpenEmployee(id) {
    if (typeof dashOpenEmployee === 'function') { dashOpenEmployee(id, 'familie'); return; }
    window.activeEmpId = id;
    showPage('mitarbeiter');
}
