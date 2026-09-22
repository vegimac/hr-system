// ── Vertragshistorie prüfen (Walter-Vorgabe 22.09.2026) ───────────────────
// Die Historie ist durch Sync-Fehler durcheinandergeraten (Funktion überall
// gleich, Vertragsenden am falschen Tag, Überlappungen bei Filialwechseln,
// Abschnitte ohne Lohn). easy@work ist die QUELLE — korrigiert wird dort,
// OneCrew holt es beim nächsten Sync. Diese Liste zeigt nur, WO etwas nicht
// stimmt; sie ändert nichts.
//
// Zuordnung: jeder MA gehört zur Filiale seines JÜNGSTEN Vertrags — so
// erscheint jede Person genau einmal, die Historie zeigt aber alle Filialen.

let _vhDaten = null;

function _vhEsc(t) {
    return String(t ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function _vhDatum(iso) {
    return iso ? iso.slice(8, 10) + '.' + iso.slice(5, 7) + '.' + iso.slice(0, 4) : 'offen';
}

function _vhGeld(z) {
    if (z.lohn == null) return '<span style="color:#dc2626">kein Lohn</span>';
    return Number(z.lohn).toLocaleString('de-CH', { minimumFractionDigits: 2 })
         + (z.lohnart === 'hourly' ? ' /h' : ' /Mt.');
}

function vhInit() {
    const sel = document.getElementById('vhBranch');
    if (!sel) return;
    // Filial-Selektor folgt dem globalen Sidebar-Selektor (Konvention 13.05.2026).
    const list = (typeof allBranches !== 'undefined' && Array.isArray(allBranches)) ? allBranches : [];
    sel.innerHTML = list.map(b => `<option value="${b.id}">${_vhEsc(b.branchName || b.fullDisplayName || b.companyName)}</option>`).join('');
    const fixed = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? String(fixedCompanyProfileId) : '';
    if (fixed) sel.value = fixed;
}

async function vhRun() {
    const cid = document.getElementById('vhBranch')?.value;
    if (!cid) return;
    const nurProbleme = document.getElementById('vhNurProbleme')?.checked ?? true;
    const st = document.getElementById('vhStatus');
    st.textContent = 'Lade …';
    document.getElementById('vhResults').innerHTML = '';
    try {
        const r = await fetch(`/api/employments/historie-check?companyProfileId=${cid}&nurProbleme=${nurProbleme}`,
            { headers: ah(), cache: 'no-store' });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        _vhDaten = await r.json();
        st.textContent = `${_vhDaten.mitarbeiter} Mitarbeitende · ${_vhDaten.ueberlappungen} Überlappungen · `
                       + `${_vhDaten.luecken} Lücken · ${_vhDaten.ohneLohn} ohne Lohn`;
        vhRender();
    } catch (e) {
        st.textContent = '';
        document.getElementById('vhResults').innerHTML =
            `<div class="card" style="padding:14px;color:#991b1b;background:#fef2f2;border:1px solid #fecaca">Fehler: ${_vhEsc(e.message)}</div>`;
    }
}

function vhRender() {
    const box = document.getElementById('vhResults');
    if (!_vhDaten?.liste?.length) {
        box.innerHTML = `<div class="card" style="padding:18px;color:#166534;background:#f0fdf4;border:1px solid #bbf7d0">
            ✓ Keine Probleme gefunden.</div>`;
        return;
    }
    box.innerHTML = _vhDaten.liste.map(m => {
        const rows = m.zeilen.map(z => {
            const farbe = z.ueberlappung ? 'background:#fef2f2'
                        : z.luecke       ? 'background:#fffbeb'
                        : '';
            const marke = z.ueberlappung
                ? '<span style="font-size:11px;font-weight:700;color:#991b1b;background:#fee2e2;padding:2px 8px;border-radius:9px">Überlappung</span>'
                : z.luecke
                    ? '<span style="font-size:11px;font-weight:700;color:#92400e;background:#fef3c7;padding:2px 8px;border-radius:9px">Lücke davor</span>'
                    : '';
            return `<tr style="${farbe};border-bottom:1px solid #f1f5f9">
                <td style="padding:6px 10px;white-space:nowrap">${_vhDatum(z.von)} – ${_vhDatum(z.bis)}</td>
                <td style="padding:6px 10px">${_vhEsc(z.filiale || '–')}</td>
                <td style="padding:6px 10px">${_vhEsc(z.modell || '')}</td>
                <td style="padding:6px 10px;text-align:right">${z.pensum != null ? Number(z.pensum) + ' %' : '–'}</td>
                <td style="padding:6px 10px;text-align:right;white-space:nowrap">${_vhGeld(z)}</td>
                <td style="padding:6px 10px">${_vhEsc(z.funktion || '–')}</td>
                <td style="padding:6px 10px">${marke}</td>
            </tr>`;
        }).join('');
        const badge = m.probleme > 0
            ? `<span style="font-size:11px;font-weight:700;color:#991b1b;background:#fee2e2;padding:2px 8px;border-radius:9px">${m.probleme}</span>`
            : `<span style="font-size:11px;font-weight:700;color:#166534;background:#dcfce7;padding:2px 8px;border-radius:9px">ok</span>`;
        return `<div class="card" style="padding:0;margin-bottom:12px;overflow:hidden">
            <div style="display:flex;align-items:center;gap:10px;padding:10px 14px;background:#f8fafc;border-bottom:1px solid #e2e8f0">
                <b style="font-size:14px">${_vhEsc(m.name)}</b>
                <span style="font-size:12px;color:#94a3b8">${_vhEsc(m.employeeNumber || '')}</span>
                ${m.aktiv ? '' : '<span style="font-size:11px;color:#64748b">ausgetreten</span>'}
                ${badge}
                <button class="btn btn-outline" style="margin-left:auto;font-size:12px;padding:4px 12px"
                        onclick="vhEasyVergleich(${m.employeeId}, '${_vhEsc(m.employeeNumber || '')}')">easy vergleichen</button>
            </div>
            <table style="width:100%;border-collapse:collapse;font-size:12.5px">
                <thead><tr style="text-align:left;color:#64748b">
                    <th style="padding:6px 10px;font-weight:600">Zeitraum</th>
                    <th style="padding:6px 10px;font-weight:600">Filiale</th>
                    <th style="padding:6px 10px;font-weight:600">Modell</th>
                    <th style="padding:6px 10px;font-weight:600;text-align:right">Pensum</th>
                    <th style="padding:6px 10px;font-weight:600;text-align:right">Lohn</th>
                    <th style="padding:6px 10px;font-weight:600">Funktion</th>
                    <th style="padding:6px 10px;font-weight:600"></th>
                </tr></thead>
                <tbody>${rows}</tbody>
            </table>
            <div id="vhEasy-${m.employeeId}"></div>
        </div>`;
    }).join('');
}

// Ganze Filiale neu aus easy@work holen (Walter 22.09.2026): ruft für jeden MA
// denselben Ablauf wie der Einzel-Knopf am Mitarbeiter. Ausgetretene sind dabei —
// gerade deren Historie brauchen wir für Zeugnisse. Es wird nichts gelöscht.
async function vhNeuHolen() {
    const cid = document.getElementById('vhBranch')?.value;
    const name = document.getElementById('vhBranch')?.selectedOptions?.[0]?.textContent || '';
    if (!cid) return;
    const ok = typeof liquidConfirm === 'function'
        ? await liquidConfirm(`Für alle Mitarbeitenden der Filiale ${name} werden Verträge und Löhne frisch aus easy@work geholt und die Abschnitte nachgeführt. Ausgetretene sind dabei. Es wird nichts gelöscht. Das dauert je nach Filiale ein bis zwei Minuten.`,
            { title: 'Vertragshistorie neu holen?', yesLabel: 'Starten', noLabel: 'Abbrechen' })
        : confirm('Vertragshistorie für die ganze Filiale neu holen?');
    if (!ok) return;

    const st = document.getElementById('vhStatus');
    const btn = document.getElementById('vhNeuBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'läuft …'; }
    st.textContent = 'Hole Verträge aus easy@work — bitte Fenster offen lassen …';
    try {
        const r = await fetch('/api/easyatwork/vertraege-neu-holen', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyProfileId: parseInt(cid, 10) })
        });
        if (!r.ok) {
            let msg = 'HTTP ' + r.status;
            try { const j = await r.json(); msg = j.message || j.error || msg; } catch (_) {}
            throw new Error(msg);
        }
        const d = await r.json();
        const zeilen = (d.meldungen || []).map(m => {
            const teile = [];
            if (m.geaendert) teile.push('<span style="color:#15803d;font-weight:600">Verträge aktualisiert</span>');
            (m.uebersprungen || []).forEach(x => teile.push(`<span style="color:#92400e">${_vhEsc(x)}</span>`));
            (m.fehler || []).forEach(x => teile.push(`<span style="color:#991b1b">${_vhEsc(x)}</span>`));
            (m.hinweise || []).forEach(x => teile.push(`<span style="color:#64748b">${_vhEsc(x)}</span>`));
            return `<div style="padding:5px 0;border-bottom:1px solid #f1f5f9;font-size:12.5px">
                <b>${_vhEsc(m.name)}</b> <span style="color:#94a3b8">${_vhEsc(m.nummer || '')}</span><br>${teile.join('<br>')}</div>`;
        }).join('');
        document.getElementById('vhResults').innerHTML = `
            <div class="card" style="padding:16px">
                <div style="font-size:14px;font-weight:700;margin-bottom:8px">
                    ${d.geprueft} Mitarbeitende · ${d.geaendert} mit aktualisierten Verträgen · ${d.unveraendert} unverändert${d.fehler ? ` · ${d.fehler} Fehler` : ''}
                </div>
                ${zeilen || '<div style="color:#64748b;font-size:13px">Keine Meldungen.</div>'}
                <button class="btn btn-primary" style="margin-top:12px" onclick="vhRun()">Liste neu prüfen</button>
            </div>`;
        st.textContent = 'Fertig.';
    } catch (e) {
        st.textContent = '';
        document.getElementById('vhResults').innerHTML =
            `<div class="card" style="padding:14px;color:#991b1b;background:#fef2f2;border:1px solid #fecaca">Fehler: ${_vhEsc(e.message)}</div>`;
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = '⟳ Historie aus easy@work neu holen'; }
    }
}

// Vergleich mit easy@work: zeigt, ob der Fehler in easy steckt (dort korrigieren)
// oder nur bei uns (dann genügt ein Sync).
async function vhEasyVergleich(employeeId, nummer) {
    const cid = document.getElementById('vhBranch')?.value;
    const ziel = document.getElementById(`vhEasy-${employeeId}`);
    if (!ziel || !nummer) return;
    ziel.innerHTML = '<div style="padding:10px 14px;color:#64748b;font-size:12.5px">Lade easy@work …</div>';
    try {
        const r = await fetch(`/api/easyatwork/debug/employee-dump?companyProfileId=${cid}&number=${encodeURIComponent(nummer)}`,
            { headers: ah(), cache: 'no-store' });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const d = await r.json();
        const find = (p) => d.results?.find(x => (x.path || '').endsWith(p))?.body?.data || [];
        const cs = find('/contracts').filter(c => !c.deleted_at);
        const rs = find('/pay_rates').filter(c => !c.deleted_at);
        const tag = (s) => s ? String(s).slice(8, 10) + '.' + String(s).slice(5, 7) + '.' + String(s).slice(0, 4) : 'offen';
        ziel.innerHTML = `<div style="padding:10px 14px;background:#f8fafc;border-top:1px solid #e2e8f0;font-size:12.5px">
            <b>easy@work — Verträge</b>
            <ul style="margin:4px 0 10px 18px">${cs.map(c =>
                `<li>${tag(c.from)} – ${tag(c.to)} · ${_vhEsc(c.amount_type)} ${c.amount ?? ''} · ${c.percentage != null ? Math.round(c.percentage) + ' %' : ''} · type_id ${c.type_id}</li>`).join('') || '<li>keine</li>'}</ul>
            <b>easy@work — Löhne</b>
            <ul style="margin:4px 0 0 18px">${rs.map(x =>
                `<li>${tag(x.from)} – ${tag(x.to)} · ${Number(x.rate).toLocaleString('de-CH', { minimumFractionDigits: 2 })} ${x.type === 'hour' ? '/h' : '/Mt.'}</li>`).join('') || '<li>keine</li>'}</ul>
        </div>`;
    } catch (e) {
        ziel.innerHTML = `<div style="padding:10px 14px;color:#991b1b;font-size:12.5px">easy@work: ${_vhEsc(e.message)}</div>`;
    }
}
