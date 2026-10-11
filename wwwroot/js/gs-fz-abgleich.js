// ══════════════════════════════════════════════════════════════════════
// gs-fz-abgleich.js — HR-Hub → Kontrolle → «FZ-Abgleich GastroSocial»
// ──────────────────────────────────────────────────────────────────────
// Walter 12.10.2026: «Familienzulagenbescheinigung» aus dem GastroSocial-
// Portal (Excel) gegen die Zulagen bei den Kindern der MA der Sidebar-
// Filiale, Monat für Monat. Nur Auswertung.
// Backend: /api/imports/gastrosocial-fz-abgleich
// ══════════════════════════════════════════════════════════════════════
let _gsfData = null;

function gsfInit() {
    ['gsfAlert', 'gsfSummary', 'gsfResult'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.innerHTML = '';
    });
    const inp = document.getElementById('gsfFileInput');
    if (inp) inp.value = '';
    _gsfData = null;
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
    el.innerHTML = `Filiale: <b>${esc(b.restaurantCode || '')} ${esc(b.city || b.name || '')}</b> — bitte die Familienzulagenbescheinigung <b>dieser</b> Filiale hochladen.`;
    el.style.background = '#f6f3ee';
    el.style.borderColor = '#e5e0d6';
    el.style.color = '#6b6152';
}

async function gsfAnalyze() {
    const inp = document.getElementById('gsfFileInput');
    ['gsfAlert', 'gsfSummary', 'gsfResult'].forEach(id => { document.getElementById(id).innerHTML = ''; });
    if (!inp.files || inp.files.length === 0) {
        gsfFehler('Bitte die GastroSocial «Familienzulagenbescheinigung» (XLSX) wählen.');
        return;
    }
    const cpId = typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0;
    if (!cpId) {
        gsfFehler('Bitte zuerst links eine Filiale wählen.');
        return;
    }
    const fd = new FormData();
    fd.append('file', inp.files[0]);
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
        _gsfData = (await r.json()).ergebnis;
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

function gsfRender(d) {
    const fe = (d.fehlt || []).length, ab = (d.abweichung || []).length;
    const no = (d.nurOneCrew || []).length, nz = (d.nichtZuordenbar || []).length, ok = (d.passt || []).length;
    const zeitraum = d.von && d.bis ? `${gsfDate(d.von)} – ${gsfDate(d.bis)}` : '';
    document.getElementById('gsfSummary').innerHTML = `
        ${zeitraum ? `<div style="font-size:12.5px;color:#64748b;margin-bottom:10px">Bescheinigung ${zeitraum} · ${d.zeilen} Zeilen · Total GastroSocial CHF ${gsfChf(d.totalGs)}</div>` : ''}
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(130px,1fr));gap:10px">
            ${gsfTile(d.kinder, 'Kinder bei GastroSocial', '#f1f5f9', '#475569')}
            ${gsfTile(fe, 'Fehlt in OneCrew', fe ? '#fee2e2' : '#f0fdf4', fe ? '#991b1b' : '#15803d', 'fehlt')}
            ${gsfTile(ab, 'Abweichung', ab ? '#fef3c7' : '#f0fdf4', ab ? '#92400e' : '#15803d', 'abweichung')}
            ${gsfTile(no, 'Nur in OneCrew', no ? '#fce7f3' : '#f0fdf4', no ? '#9d174d' : '#15803d', 'nurOneCrew')}
            ${gsfTile(nz, 'Nicht zuordenbar', nz ? '#e0e7ff' : '#f0fdf4', nz ? '#3730a3' : '#15803d', 'nichtZuordenbar')}
            ${gsfTile(ok, 'Passt', '#dcfce7', '#166534', 'passt')}
        </div>
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center;margin:14px 0 10px">
            <span style="font-size:12px;color:#64748b;font-weight:600">Ansicht:</span>
            <select id="gsfFilter" onchange="gsfRenderRows()">
                <option value="fehlt">Fehlt in OneCrew — GastroSocial zahlt, OneCrew nicht (${fe})</option>
                <option value="abweichung">Abweichung — Monate oder Ansatz verschieden (${ab})</option>
                <option value="nurOneCrew">Nur in OneCrew — GastroSocial bescheinigt nichts (${no})</option>
                <option value="nichtZuordenbar">Nicht zuordenbar — Bezüger in OneCrew nicht gefunden (${nz})</option>
                <option value="passt">Passt (${ok})</option>
            </select>
        </div>`;
    document.getElementById('gsfResult').innerHTML = `<div id="gsfRows"></div>`;
    const sel = document.getElementById('gsfFilter');
    if (sel) sel.value = fe ? 'fehlt' : ab ? 'abweichung' : no ? 'nurOneCrew' : nz ? 'nichtZuordenbar' : 'passt';
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
            abweichung: '✓ Monate und Ansätze stimmen überall überein.',
            nurOneCrew: '✓ Keine Zulage in OneCrew, die GastroSocial nicht bescheinigt.',
            nichtZuordenbar: '✓ Alle Bezüger sind einem MA zugeordnet.',
            passt: 'Keine Einträge.'
        }[filter];
        el.innerHTML = `<div style="padding:28px;text-align:center;color:#15803d;font-size:13.5px">${leer}</div>`;
        return;
    }
    const aufgabe = {
        fehlt: 'GastroSocial zahlt für dieses Kind, in OneCrew fehlt das Kind oder die Zulage in diesen Monaten — im Lohn wird sie nicht ausbezahlt. Kind/Zulage im MA → Familie erfassen.',
        abweichung: 'GastroSocial und OneCrew zahlen nicht dieselben Monate oder nicht denselben Ansatz. Zulage beim Kind prüfen (Gültig ab/bis, Art, Satz).',
        nurOneCrew: 'OneCrew zahlt eine Zulage, GastroSocial bescheinigt für dieses Kind nichts — Antrag fehlt, anderer Elternteil bezieht sie, oder die Zulage in OneCrew beenden.',
        nichtZuordenbar: 'Bezüger in OneCrew nicht gefunden — AHV-Nummer beim MA nachtragen oder prüfen, ob die Person bei uns arbeitet.',
        passt: ''
    }[filter];
    const th = t => `<th style="padding:8px 10px;text-align:left">${t}</th>`;
    const td = (v, st = '') => `<td style="padding:7px 10px;${st}">${v}</td>`;
    el.innerHTML = `
        ${aufgabe ? `<div style="font-size:12.5px;color:#475569;margin:0 0 8px">${esc(aufgabe)}</div>` : ''}
        <table style="width:100%;border-collapse:collapse;font-size:12.5px;background:rgba(255,255,255,.55);border-radius:12px;overflow:hidden">
            <thead><tr style="background:#f8fafc;color:#64748b;font-size:11px;text-transform:uppercase">
                ${th('Bezüger')}${th('Kind')}${th('Geb.-Datum')}${th('GastroSocial')}${th('Total GS')}${th('Zulagen OneCrew')}${th('Total OneCrew')}${th('OneCrew')}${th('Hinweis')}
            </tr></thead>
            <tbody>${rows.map(z => {
                const diff = z.gsTotal != null && z.ocTotal != null && Number(z.gsTotal) !== Number(z.ocTotal);
                return `<tr style="border-top:1px solid #eee">
                    ${td(`<div style="font-weight:600">${esc(z.bezueger || z.oneCrewName || '—')}</div><div style="font-size:11px;color:#94a3b8;font-family:monospace">${esc(z.bezuegerAhv || '')}</div>`)}
                    ${td(`<div style="font-weight:600">${esc(z.kind || '—')}</div><div style="font-size:11px;color:#94a3b8;font-family:monospace">${esc(z.kindAhv || '')}</div>${z.kindAhvUebernehmen && z.familyMemberId
                        ? `<button class="dok-menu-btn" style="min-width:auto;padding:2px 8px;font-size:11px;margin-top:4px" onclick="gsfKindAhvUebernehmen('${filter}',${z.familyMemberId})" title="AHV-Nummer von GastroSocial beim Kind in OneCrew eintragen">＋ AHV übernehmen</button>`
                        : ''}`)}
                    ${td(gsfDate(z.kindGeburt), 'white-space:nowrap')}
                    ${td(z.gastroSocial ? esc(z.gastroSocial) : '<span style="color:#94a3b8">keine</span>', 'font-size:12px')}
                    ${td(z.gsTotal != null && filter !== 'nurOneCrew' ? gsfChf(z.gsTotal) : '—', 'text-align:right;white-space:nowrap')}
                    ${td(z.oneCrew ? esc(z.oneCrew) : (z.employeeId ? '<span style="color:#94a3b8">keine</span>' : '—'), 'font-size:12px')}
                    ${td(z.ocTotal != null ? gsfChf(z.ocTotal) : '—', `text-align:right;white-space:nowrap;${diff ? 'color:#991b1b;font-weight:600' : ''}`)}
                    ${td(z.employeeId
                        ? `<button class="dok-menu-btn" style="min-width:auto;padding:3px 9px;font-size:12px" onclick="gsfOpenEmployee(${z.employeeId})">→ ${esc(z.oneCrewName || 'MA')}</button>${z.personalnummer ? ` <span style="font-size:11px;color:#94a3b8;font-family:monospace">${esc(z.personalnummer)}</span>` : ''}`
                        : '—')}
                    ${td(esc(z.hinweis || ''), 'color:#64748b;font-size:12px')}
                </tr>`;
            }).join('')}</tbody>
        </table>`;
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
        z.kindAhvUebernehmen = null;
        z.hinweis = (z.hinweis || '').split(' · ').filter(h => !h.startsWith('AHV-Nummer des Kindes fehlt')).join(' · ') || null;
        if (typeof showToast === 'function') showToast(`AHV-Nummer bei ${z.kind || 'Kind'} eingetragen.`, 'success');
        gsfRenderRows();
    } catch (e) {
        gsfFehler('Verbindung fehlgeschlagen: ' + e.message);
    }
}

function gsfOpenEmployee(id) {
    if (typeof dashOpenEmployee === 'function') { dashOpenEmployee(id, 'familie'); return; }
    window.activeEmpId = id;
    showPage('mitarbeiter');
}
