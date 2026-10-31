// ══════════════════════════════════════════════════════════════════════
// gs-bvg-abgleich.js — HR-Bereich → GastroSocial → «BVG-Abgleich»
// ──────────────────────────────────────────────────────────────────────
// Walter 31.10.2026: Versichertenliste der beruflichen Vorsorge gegen die
// BVG-Pflicht der Sidebar-Filiale. Schreiben erst mit «Ergebnis übernehmen».
// Backend: /api/imports/bvg-versichertenliste
// ══════════════════════════════════════════════════════════════════════
let _gsbData = null;
let _gsbUebernahme = [];
let _gsbErfolg = '';

function gsbInit() {
    _gsbErfolg = '';
    ['gsbAlert', 'gsbSummary', 'gsbResult'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.innerHTML = '';
    });
    const inp = document.getElementById('gsbFileInput');
    if (inp) inp.value = '';
    _gsbData = null;
    _gsbUebernahme = [];
    gsbUpdateBranchBanner();
}

function gsbUpdateBranchBanner() {
    const el = document.getElementById('gsbBranchBanner');
    if (!el) return;
    const cpId = typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null;
    const b = (typeof allBranches !== 'undefined' ? allBranches : []).find(x => x.id === cpId);
    if (!b) {
        el.innerHTML = '⚠ Bitte links eine Filiale wählen. Verglichen werden die Mitarbeitenden dieser Filiale.';
        el.style.background = '#fef3c7';
        el.style.borderColor = '#fde68a';
        el.style.color = '#92400e';
        return;
    }
    el.innerHTML = `Filiale: <b>${esc(b.restaurantCode || '')} ${esc(b.city || b.name || '')}</b> — Personen anderer Filialen werden angezeigt, aber nicht gespeichert.`;
    el.style.background = '#f6f3ee';
    el.style.borderColor = '#e5e0d6';
    el.style.color = '#6b6152';
}

async function gsbAnalyze(opts) {
    if (!(opts && opts.behalteErfolg)) _gsbErfolg = '';
    const inp = document.getElementById('gsbFileInput');
    document.getElementById('gsbAlert').innerHTML = '';
    document.getElementById('gsbSummary').innerHTML = '';
    document.getElementById('gsbResult').innerHTML = '';
    if (!inp.files || inp.files.length === 0) {
        gsbFehler('Bitte die Versichertenliste (XLSX) wählen.');
        return;
    }
    const cpId = typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0;
    if (!cpId) {
        gsbFehler('Bitte zuerst links eine Filiale wählen.');
        return;
    }
    const fd = new FormData();
    fd.append('file', inp.files[0]);
    fd.append('companyProfileId', String(cpId));
    const btn = document.getElementById('gsbAnalyzeBtn');
    if (btn) { btn.disabled = true; btn.textContent = '⏳ vergleiche…'; }
    try {
        const r = await fetch('/api/imports/bvg-versichertenliste/analyze', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + (typeof authToken !== 'undefined' ? authToken : localStorage.hrToken) },
            body: fd
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            gsbFehler('Fehler vom Server (' + r.status + '): ' + (j.error || j.message || r.statusText));
            return;
        }
        const antwort = await r.json();
        _gsbData = antwort.ergebnis;
        _gsbUebernahme = antwort.uebernahme || [];
    } catch (e) {
        gsbFehler('Verbindung fehlgeschlagen: ' + e.message);
        return;
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'Vergleichen'; }
    }
    try { gsbRender(_gsbData); }
    catch (e) { gsbFehler('Anzeige fehlgeschlagen: ' + e.message); }
}

function gsbFehler(msg) {
    const el = document.getElementById('gsbAlert');
    if (!el) return;
    el.innerHTML = `<div style="padding:10px 14px;background:#fee2e2;border:1px solid #fca5a5;border-radius:10px;color:#991b1b;font-size:13px">${esc(msg)}</div>`;
}

function gsbTile(n, label, bg, fg, liste) {
    const klick = liste
        ? ` class="gsb-tile" data-liste="${liste}" role="button" tabindex="0"
             onclick="gsbZeigeListe('${liste}')" onkeydown="if(event.key==='Enter'||event.key===' '){event.preventDefault();gsbZeigeListe('${liste}')}"`
        : '';
    return `<div${klick} style="background:${bg};border-radius:10px;padding:12px 14px;color:${fg};border:2px solid transparent;${liste ? 'cursor:pointer' : ''}">
        <div style="font-size:24px;font-weight:700">${n ?? 0}</div>
        <div style="font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.03em">${label}</div>
    </div>`;
}

function gsbZeigeListe(liste) {
    const sel = document.getElementById('gsbFilter');
    if (sel && sel.value !== liste) {
        sel.value = liste;
        sel.dispatchEvent(new Event('change'));
    } else gsbRenderRows();
}

function gsbMarkiereKachel(liste) {
    document.querySelectorAll('#gsbSummary .gsb-tile').forEach(k => {
        const an = k.dataset.liste === liste;
        k.style.borderColor = an ? '#3f3f3f' : 'transparent';
        k.style.boxShadow = an ? '0 4px 12px rgba(60,55,48,.22)' : 'none';
    });
}

function gsbRender(d) {
    const n = k => (d[k] || []).length;
    const fest = n('festlegen'), end = n('beenden'), abw = n('abweichend');
    const nz = n('nichtZuordenbar'), af = n('andereFiliale'), nur = n('nurInOneCrew'), ok = n('passt');
    document.getElementById('gsbSummary').innerHTML = `
        <div id="gsbUebernahmeBar">${gsbUebernahmeHtml()}</div>
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px">
            ${gsbTile(fest, 'Festlegen', fest ? '#fef3c7' : '#f0fdf4', fest ? '#92400e' : '#15803d', 'festlegen')}
            ${gsbTile(end, 'Beenden', end ? '#fee2e2' : '#f0fdf4', end ? '#991b1b' : '#15803d', 'beenden')}
            ${gsbTile(abw, 'Datum weicht ab', abw ? '#ffedd5' : '#f0fdf4', abw ? '#9a3412' : '#15803d', 'abweichend')}
            ${gsbTile(nur, 'Nur in OneCrew', nur ? '#e0e7ff' : '#f0fdf4', nur ? '#3730a3' : '#15803d', 'nurInOneCrew')}
            ${gsbTile(af, 'Andere Filiale', af ? '#f1f5f9' : '#f0fdf4', '#475569', 'andereFiliale')}
            ${gsbTile(nz, 'Nicht zuordenbar', nz ? '#e0e7ff' : '#f0fdf4', nz ? '#3730a3' : '#15803d', 'nichtZuordenbar')}
            ${gsbTile(ok, 'Passt', '#dcfce7', '#166534', 'passt')}
        </div>
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center;margin:14px 0 10px">
            <span style="font-size:12px;color:#64748b;font-weight:600">Ansicht:</span>
            <select id="gsbFilter" onchange="gsbRenderRows()">
                <option value="festlegen">Festlegen — auf der Liste versichert, in OneCrew noch offen (${fest})</option>
                <option value="beenden">Beenden — Liste hat ein Ende, OneCrew noch offen (${end})</option>
                <option value="abweichend">Datum weicht ab — nicht automatisch überschrieben (${abw})</option>
                <option value="nurInOneCrew">Nur in OneCrew versichert — fehlt auf der Liste, bleibt (${nur})</option>
                <option value="andereFiliale">Andere Filiale (${af})</option>
                <option value="nichtZuordenbar">Nicht zuordenbar (${nz})</option>
                <option value="passt">Passt (${ok})</option>
            </select>
        </div>`;
    document.getElementById('gsbResult').innerHTML = `<div id="gsbRows"></div>`;
    const sel = document.getElementById('gsbFilter');
    if (sel) sel.value = fest ? 'festlegen' : end ? 'beenden' : abw ? 'abweichend' : nur ? 'nurInOneCrew' : 'passt';
    gsbRenderRows();
}

function gsbRenderRows() {
    if (!_gsbData) return;
    const filter = document.getElementById('gsbFilter')?.value || 'festlegen';
    gsbMarkiereKachel(filter);
    const el = document.getElementById('gsbRows');
    if (!el) return;
    const rows = _gsbData[filter] || [];
    if (!rows.length) {
        el.innerHTML = `<div style="padding:28px;text-align:center;color:#15803d;font-size:13.5px">Keine Zeile in dieser Ansicht.</div>`;
        return;
    }
    const th = t => `<th style="padding:8px 10px;text-align:left">${t}</th>`;
    const td = (v, st = '') => `<td style="padding:7px 10px;${st}">${v}</td>`;
    el.innerHTML = `
        <table style="width:100%;border-collapse:collapse;font-size:12.5px;background:rgba(255,255,255,.55);border-radius:12px;overflow:hidden">
            <thead><tr style="background:#f8fafc;color:#64748b;font-size:11px;text-transform:uppercase">
                ${th('Name')}${th('AHV-Nummer')}${th('Plan')}${th('Liste ab')}${th('Liste bis')}${th('OneCrew ab')}${th('Hinweis')}
            </tr></thead>
            <tbody>${rows.map(z => `<tr style="border-top:1px solid #eee">
                ${td(z.employeeId
                    ? `<button class="dok-menu-btn" style="min-width:auto;padding:3px 9px;font-size:12px" onclick="gsbOpenEmployee(${z.employeeId})">${esc(z.oneCrewName || z.name || '—')}</button>`
                    : esc(z.name || '—'), 'font-weight:600')}
                ${td(esc(z.ahv || '—'), 'font-family:monospace;white-space:nowrap')}
                ${td(esc(z.bezeichnung || '—'))}
                ${td(gsbDate(z.gueltigAb))}
                ${td(gsbDate(z.gueltigBis))}
                ${td(gsbDate(z.oneCrewAb))}
                ${td(esc(z.hinweis || ''), 'color:#64748b;font-size:12px')}
            </tr>`).join('')}</tbody>
        </table>`;
}

function gsbUebernahmeHtml() {
    const erfolg = _gsbErfolg
        ? `<div style="margin-bottom:12px;padding:10px 14px;background:#f0fdf4;border:1px solid #bbf7d0;border-radius:12px;font-size:12.5px;color:#166534">✓ ${_gsbErfolg}</div>`
        : '';
    const n = _gsbUebernahme.length;
    if (!n) return erfolg;
    const an = _gsbUebernahme.filter(x => x.aktion === 'anlegen').length;
    const ab = _gsbUebernahme.filter(x => x.aktion === 'beenden').length;
    const teile = [an ? `${an} als versichert festlegen` : '', ab ? `${ab} beenden` : ''].filter(Boolean).join(', ');
    return erfolg + `<div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:12px;padding:10px 14px;background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.14);border-radius:12px;font-size:12.5px;color:#3f3f3f">
        <span style="flex:1;min-width:240px">Übernehmen speichert <b>${teile}</b>.
            Ab dem Datum der Liste rechnet der Lohn jeden Monat BVG, auch bei wenig Stunden.
            <span style="color:#8b8b8b">Wer nicht auf der Liste steht, bleibt unverändert. Ein abweichendes Datum wird nicht überschrieben.</span></span>
        <button id="gsbUebernahmeBtn" onclick="gsbUebernehmen()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 16px;font-size:12.5px;font-weight:600;cursor:pointer">Ergebnis übernehmen (${n})</button>
    </div>`;
}

async function gsbUebernehmen() {
    const cpId = typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId ? fixedCompanyProfileId : 0;
    if (!cpId || !_gsbUebernahme.length) return;
    const ok = await liquidConfirm(
        `${_gsbUebernahme.length} Einträge aus der Versichertenliste speichern? Der Lohn rechnet ab dem Datum der Liste jeden Monat BVG.`,
        { title: 'Ergebnis übernehmen', yesLabel: 'Übernehmen', noLabel: 'Abbrechen' });
    if (!ok) return;
    const btn = document.getElementById('gsbUebernahmeBtn');
    if (btn) btn.disabled = true;
    try {
        const r = await fetch('/api/imports/bvg-versichertenliste/uebernehmen', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + (typeof authToken !== 'undefined' ? authToken : localStorage.hrToken), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                companyProfileId: cpId,
                eintraege: _gsbUebernahme.map(x => ({
                    employeeId: x.employeeId, aktion: x.aktion,
                    gueltigAb: x.gueltigAb, gueltigBis: x.gueltigBis, bezeichnung: x.bezeichnung,
                })),
            }),
        });
        if (!r.ok) { gsbFehler('Übernehmen fehlgeschlagen (' + r.status + ').'); return; }
        const j = await r.json();
        _gsbErfolg = `${j.neu} festgelegt, ${j.beendet} beendet.`;
        await gsbAnalyze({ behalteErfolg: true });
    } catch (e) {
        gsbFehler('Verbindung fehlgeschlagen: ' + e.message);
    } finally {
        if (btn) btn.disabled = false;
    }
}

function gsbDate(iso) {
    if (!iso) return '—';
    const s = String(iso);
    return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
}

function gsbOpenEmployee(id) {
    if (typeof dashOpenEmployee === 'function') { dashOpenEmployee(id, 'zulagen'); return; }
    window.activeEmpId = id;
    showPage('mitarbeiter');
}
