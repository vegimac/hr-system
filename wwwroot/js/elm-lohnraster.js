// ══════════════════════════════════════════════════════════════════════
//  ELM-LOHNRASTER-PICKLIST (Walter-Vorgabe 17.08.2026)
//  Dauerhaftes Archiv aller 309 Raster-Positionen (Lohnarten, SV-Abzuege,
//  Absenzarten). Pro Lohnart kann per Klick eine OneCrew-Lohnposition
//  erzeugt («Uebernehmen») oder eine bestehende zugeordnet werden
//  («Verknuepfen»). Nichts wird automatisch aktiv.
// ══════════════════════════════════════════════════════════════════════
let _elrAll = [];
let _elrLohnpos = [];

async function elrInit() {
    const el = document.getElementById('elrList');
    if (el) el.innerHTML = '<div style="color:#8b8b8b;font-size:12.5px;padding:20px">Wird geladen…</div>';
    try {
        const [r1, r2] = await Promise.all([
            fetch('/api/elm-lohnraster', { headers: ah() }),
            fetch('/api/lohnpositionen', { headers: ah() }),
        ]);
        _elrAll = r1.ok ? await r1.json() : [];
        _elrLohnpos = r2.ok ? await r2.json() : [];
    } catch (e) { if (el) el.textContent = 'Fehler: ' + e.message; return; }
    elrRender();
}

function _elrEsc(s) { return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;'); }
function _elrFlag(v) {
    if (v === true)  return '<span style="color:#16a34a">✓</span>';
    if (v === false) return '<span style="color:#dc2626;opacity:.6">–</span>';
    return '<span style="color:#cbd5e1">·</span>';
}

function elrRender() {
    const el = document.getElementById('elrList');
    if (!el) return;
    const typ = document.getElementById('elrTyp')?.value || 'LOHNART';
    const q = (document.getElementById('elrSearch')?.value || '').toLowerCase().trim();
    const nurOffen = document.getElementById('elrNurOffen')?.checked;

    let rows = _elrAll.filter(e => (typ === '' || e.typ === typ));
    if (q) rows = rows.filter(e =>
        (e.code || '').toLowerCase().includes(q) ||
        (e.bezeichnung || '').toLowerCase().includes(q) ||
        (e.gruppe || '').toLowerCase().includes(q));
    if (nurOffen) rows = rows.filter(e => !e.verwendetLohnpositionId);

    const istLohnart = typ === 'LOHNART' || typ === '';
    const flagKopf = istLohnart
        ? '<th class="elr-c">AHV</th><th class="elr-c">UVG</th><th class="elr-c">KTG</th><th class="elr-c">BVG</th><th class="elr-c">QST</th><th class="elr-c">13.ML</th>'
        : '';
    el.innerHTML = `
    <div style="font-size:12px;color:#8b8b8b;margin:2px 0 6px">${rows.length} Positionen</div>
    <div class="card" style="padding:0;overflow:visible;max-width:1250px">
    <table style="width:100%;border-collapse:collapse;font-size:12.5px">
        <thead><tr style="background:rgba(255,255,255,0.55);border-bottom:1px solid rgba(60,55,48,0.14)">
            <th style="text-align:left;padding:7px 12px;width:70px">Code</th>
            <th style="text-align:left;padding:7px 12px">Bezeichnung</th>
            <th style="text-align:left;padding:7px 12px;width:130px">Gruppe</th>
            ${flagKopf}
            <th style="text-align:left;padding:7px 12px;width:90px">LA-Feld</th>
            <th style="text-align:left;padding:7px 12px;width:190px">In OneCrew</th>
            <th style="width:120px"></th>
        </tr></thead>
        <tbody>
        ${rows.map(e => {
            const flags = istLohnart
                ? `<td class="elr-c">${_elrFlag(e.ahv)}</td><td class="elr-c">${_elrFlag(e.uvg)}</td>
                   <td class="elr-c">${_elrFlag(e.ktg)}</td><td class="elr-c">${_elrFlag(e.bvg)}</td>
                   <td class="elr-c">${_elrFlag(e.qst ?? e.qstPeriodisch)}</td><td class="elr-c">${_elrFlag(e.ml13)}</td>`
                : '';
            const status = e.verwendetLohnpositionId
                ? `<span style="color:#166534;font-weight:600">✓ ${_elrEsc(e.verwendetCode)}</span> <span style="color:#8b8b8b;font-size:11.5px">${_elrEsc(e.verwendetBezeichnung || '')}</span>`
                : '<span style="color:#b0aca3">—</span>';
            const aktionen = e.typ === 'LOHNART'
                ? (e.verwendetLohnpositionId
                    ? `<button class="dok-menu-btn" style="width:auto;padding:3px 10px;font-size:11.5px" onclick="elrLoesen(${e.id})">Lösen</button>`
                    : `<button onclick="elrUebernehmen(${e.id})" style="background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:4px 11px;font-size:11.5px;font-weight:600;cursor:pointer">Übernehmen</button>
                       <button class="dok-menu-btn" style="width:auto;padding:3px 9px;font-size:11.5px" title="Bestehende OneCrew-Lohnposition zuordnen" onclick="elrVerknuepfenDialog(${e.id})">Verkn.</button>`)
                : '';
            return `<tr style="border-bottom:1px solid rgba(60,55,48,0.08);${e.inaktiv ? 'opacity:.5' : ''}">
                <td style="padding:4px 12px;font-family:monospace;font-weight:600;color:#3f3f3f;cursor:pointer" onclick="elrDetail(${e.id})" title="Details anzeigen">${_elrEsc(e.code)}</td>
                <td style="padding:4px 12px;color:#3f3f3f;cursor:pointer" onclick="elrDetail(${e.id})">${_elrEsc(e.bezeichnung)}</td>
                <td style="padding:4px 12px;color:#8b8b8b">${_elrEsc(e.gruppe || '')}</td>
                ${flags}
                <td style="padding:4px 12px;color:#6b6152;font-size:11.5px">${_elrEsc((e.lohnausweisfeld || '').split('.')[0] ? e.lohnausweisfeld : '')}</td>
                <td style="padding:4px 12px">${status}</td>
                <td style="padding:4px 12px;text-align:right;white-space:nowrap">${aktionen}</td>
            </tr>`;
        }).join('')}
        </tbody>
    </table></div>`;
}

async function elrUebernehmen(id) {
    const e = _elrAll.find(x => x.id === id);
    const ok = await liquidConfirm(
        `Position «${e.code} ${e.bezeichnung}» als OneCrew-Lohnposition anlegen? `
        + `Flags und Lohnausweisfeld werden aus dem Raster übernommen.`,
        { title: 'Position übernehmen', yesLabel: 'Übernehmen', noLabel: 'Abbrechen' });
    if (!ok) return;
    const r = await fetch(`/api/elm-lohnraster/${id}/uebernehmen`, { method: 'POST', headers: ah() });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Fehler', 'error'); return; }
    showToast(`Lohnposition ${j.code} angelegt.`, 'success');
    elrInit();
}

async function elrVerknuepfenDialog(id) {
    const e = _elrAll.find(x => x.id === id);
    const belegte = new Set(_elrAll.filter(x => x.verwendetLohnpositionId).map(x => x.verwendetLohnpositionId));
    const kandidaten = _elrLohnpos.filter(l => !belegte.has(l.id));
    const auswahl = prompt(
        `Bestehende Lohnposition dem Raster-Eintrag «${e.code} ${e.bezeichnung}» zuordnen.\n\n`
        + kandidaten.slice(0, 40).map(l => `${l.code} = ${l.bezeichnung}`).join('\n')
        + `\n\nCode der Lohnposition eingeben:`);
    if (!auswahl) return;
    const lp = _elrLohnpos.find(l => String(l.code).trim() === auswahl.trim());
    if (!lp) { showToast(`Keine Lohnposition mit Code «${auswahl}» gefunden.`, 'error'); return; }
    const r = await fetch(`/api/elm-lohnraster/${id}/verknuepfen`, {
        method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ lohnpositionId: lp.id }),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Fehler', 'error'); return; }
    showToast(`Verknüpft mit ${lp.code}.`, 'success');
    elrInit();
}

async function elrLoesen(id) {
    const ok = await liquidConfirm(
        'Verknüpfung lösen? Die OneCrew-Lohnposition bleibt bestehen — nur die Zuordnung wird entfernt.',
        { title: 'Verknüpfung lösen', yesLabel: 'Lösen', noLabel: 'Abbrechen' });
    if (!ok) return;
    const r = await fetch(`/api/elm-lohnraster/${id}/loesen`, { method: 'POST', headers: ah() });
    if (!r.ok) { showToast('Fehler beim Lösen', 'error'); return; }
    elrInit();
}

function elrDetail(id) {
    const e = _elrAll.find(x => x.id === id);
    if (!e) return;
    let attrs = [];
    try { attrs = JSON.parse(e.attrs || '[]'); } catch {}
    let m = document.getElementById('elrDetailModal');
    if (!m) {
        m = document.createElement('div');
        m.id = 'elrDetailModal';
        m.style.cssText = 'display:none;position:fixed;inset:0;z-index:400;background:rgba(0,0,0,0.5)';
        m.innerHTML = `<div style="position:absolute;top:80px;left:50%;transform:translateX(-50%);width:640px;max-height:75vh;background:#faf8f5;border-radius:16px;box-shadow:0 25px 60px rgba(0,0,0,0.35);display:flex;flex-direction:column;overflow:hidden">
            <div style="display:flex;justify-content:space-between;align-items:center;padding:14px 20px;border-bottom:1px solid rgba(60,55,48,0.12)">
                <b id="elrDetailTitle" style="font-size:14.5px;color:#3f3f3f"></b>
                <button onclick="document.getElementById('elrDetailModal').style.display='none'" style="background:none;border:none;font-size:20px;cursor:pointer;color:#8b8b8b">×</button>
            </div>
            <div id="elrDetailBody" style="flex:1;overflow:auto;padding:14px 20px;font-size:12.5px"></div></div>`;
        m.onclick = (ev) => { if (ev.target === m) m.style.display = 'none'; };
        document.body.appendChild(m);
    }
    document.getElementById('elrDetailTitle').textContent = `${e.code} — ${e.bezeichnung}`;
    document.getElementById('elrDetailBody').innerHTML =
        '<table style="width:100%;border-collapse:collapse">' + attrs.map(([k, v]) =>
            `<tr style="border-bottom:1px solid rgba(60,55,48,0.07)">
                <td style="padding:3px 8px 3px 0;color:#8b8b8b;width:55%">${_elrEsc(k)}</td>
                <td style="padding:3px 0;color:#3f3f3f">${_elrEsc(v)}</td>
            </tr>`).join('') + '</table>';
    m.style.display = 'block';
}
