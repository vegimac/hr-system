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
    const anzVerwendet = rows.filter(e => e.verwendetLohnpositionId).length;
    el.innerHTML = `
    <div style="font-size:12px;color:#8b8b8b;margin:2px 0 6px">${rows.length} Positionen · <span style="color:#166534;font-weight:600">${anzVerwendet} in OneCrew verwendet</span></div>
    <div class="card" style="padding:0;overflow:visible;max-width:1250px">
    <table style="width:100%;border-collapse:collapse;font-size:12.5px">
        <thead><tr style="background:rgba(255,255,255,0.55);border-bottom:1px solid rgba(60,55,48,0.14)">
            <th style="width:46px;padding:7px 0;text-align:center" title="Häkchen = in OneCrew verwendet">✓</th>
            <th style="text-align:left;padding:7px 12px;width:70px">Code</th>
            <th style="text-align:left;padding:7px 12px">Bezeichnung</th>
            <th style="text-align:left;padding:7px 12px;width:130px">Gruppe</th>
            ${flagKopf}
            <th style="text-align:left;padding:7px 12px;width:90px">LA-Feld</th>
            <th style="text-align:left;padding:7px 12px;width:210px">In OneCrew</th>
        </tr></thead>
        <tbody>
        ${rows.map(e => {
            const flags = istLohnart
                ? `<td class="elr-c">${_elrFlag(e.ahv)}</td><td class="elr-c">${_elrFlag(e.uvg)}</td>
                   <td class="elr-c">${_elrFlag(e.ktg)}</td><td class="elr-c">${_elrFlag(e.bvg)}</td>
                   <td class="elr-c">${_elrFlag(e.qst ?? e.qstPeriodisch)}</td><td class="elr-c">${_elrFlag(e.ml13)}</td>`
                : '';
            // Haekchen-Spalte: nur Lohnarten sind an-/abwaehlbar. SV-Abzuege/Absenzen
            // sind reine Referenz (eigene Module) → kein Kaestchen.
            const checkbox = e.typ === 'LOHNART'
                ? `<input type="checkbox" class="no-liquid" style="width:16px;height:16px;cursor:pointer;accent-color:#166534"
                     ${e.verwendetLohnpositionId ? 'checked' : ''}
                     onclick="event.stopPropagation(); elrToggle(${e.id}, this)">`
                : '<span style="color:#d6d2ca">·</span>';
            const status = e.verwendetLohnpositionId
                ? `<span style="color:#166534;font-weight:600">${_elrEsc(e.verwendetCode)}</span> <span style="color:#8b8b8b;font-size:11.5px">${_elrEsc(e.verwendetBezeichnung || '')}</span>`
                : (e.typ === 'LOHNART'
                    ? `<a href="javascript:void(0)" onclick="event.stopPropagation(); elrVerknuepfenDialog(${e.id})"
                          style="color:#8b8b8b;font-size:11.5px;text-decoration:underline dotted" title="Bestehende OneCrew-Lohnposition mit anderem Code zuordnen">mit bestehender verknüpfen…</a>`
                    : '<span style="color:#b0aca3">—</span>');
            return `<tr style="border-bottom:1px solid rgba(60,55,48,0.08);${e.inaktiv ? 'opacity:.5' : ''}">
                <td style="padding:4px 0;text-align:center">${checkbox}</td>
                <td style="padding:4px 12px;font-family:monospace;font-weight:600;color:#3f3f3f;cursor:pointer" onclick="elrDetail(${e.id})" title="Details anzeigen">${_elrEsc(e.code)}</td>
                <td style="padding:4px 12px;color:#3f3f3f;cursor:pointer" onclick="elrDetail(${e.id})">${_elrEsc(e.bezeichnung)}</td>
                <td style="padding:4px 12px;color:#8b8b8b">${_elrEsc(e.gruppe || '')}</td>
                ${flags}
                <td style="padding:4px 12px;color:#6b6152;font-size:11.5px">${_elrEsc((e.lohnausweisfeld || '').split('.')[0] ? e.lohnausweisfeld : '')}</td>
                <td style="padding:4px 12px">${status}</td>
            </tr>`;
        }).join('')}
        </tbody>
    </table></div>`;
}

// Haekchen-Klick: an = uebernehmen (Lohnposition anlegen oder per Code verknuepfen),
// ab = Verknuepfung loesen (Lohnposition bleibt bestehen).
async function elrToggle(id, cb) {
    const e = _elrAll.find(x => x.id === id);
    if (!e) return;
    if (cb.checked) {
        cb.checked = false;           // erst nach Server-OK wirklich setzen
        await elrUebernehmen(id);
    } else {
        cb.checked = true;
        await elrLoesen(id);
    }
}

async function elrUebernehmen(id) {
    const e = _elrAll.find(x => x.id === id);
    const ok = await liquidConfirm(
        `Lohnart «${e.code} ${e.bezeichnung}» in OneCrew übernehmen? `
        + `Sie wird als Lohnposition angelegt (Flags + Lohnausweisfeld aus dem Raster) — `
        + `existiert der Code schon, wird die bestehende Position verknüpft.`,
        { title: 'Lohnart übernehmen', yesLabel: 'Übernehmen', noLabel: 'Abbrechen' });
    if (!ok) return;
    const r = await fetch(`/api/elm-lohnraster/${id}/uebernehmen`, { method: 'POST', headers: ah() });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) { showToast(j.message || j.error || 'Fehler', 'error'); return; }
    showToast(j.verknuepft
        ? `Mit bestehender Lohnposition ${j.code} verknüpft.`
        : `Lohnposition ${j.code} angelegt.`, 'success');
    elrInit();
}

function elrVerknuepfenDialog(id) {
    const e = _elrAll.find(x => x.id === id);
    if (!e) return;
    const belegte = new Set(_elrAll.filter(x => x.verwendetLohnpositionId).map(x => x.verwendetLohnpositionId));
    const kandidaten = _elrLohnpos.filter(l => !belegte.has(l.id))
        .sort((a, b) => String(a.code).localeCompare(String(b.code), undefined, { numeric: true }));
    if (!kandidaten.length) { showToast('Keine freie OneCrew-Lohnposition zum Verknüpfen vorhanden.', 'info'); return; }

    let m = document.getElementById('elrVerknModal');
    if (!m) {
        m = document.createElement('div');
        m.id = 'elrVerknModal';
        m.style.cssText = 'display:none;position:fixed;inset:0;z-index:400;background:rgba(0,0,0,0.5)';
        m.innerHTML = `<div style="position:absolute;top:120px;left:50%;transform:translateX(-50%);width:480px;background:#faf8f5;border-radius:16px;box-shadow:0 25px 60px rgba(0,0,0,0.35);padding:18px 22px">
            <b id="elrVerknTitle" style="font-size:14px;color:#3f3f3f;display:block;margin-bottom:4px"></b>
            <p style="font-size:12px;color:#8b8b8b;margin:0 0 12px">Bestehende OneCrew-Lohnposition (anderer Code) diesem Raster-Eintrag zuordnen — es wird nichts neu angelegt.</p>
            <select id="elrVerknSelect" style="width:100%;padding:8px 10px;border-radius:8px;font-size:13px"></select>
            <div style="display:flex;justify-content:flex-end;gap:8px;margin-top:16px">
                <button onclick="document.getElementById('elrVerknModal').style.display='none'"
                        style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:6px 14px;font-size:12.5px;cursor:pointer;color:#3f3f3f">Abbrechen</button>
                <button id="elrVerknOk"
                        style="background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:6px 16px;font-size:12.5px;font-weight:600;cursor:pointer">Verknüpfen</button>
            </div></div>`;
        m.onclick = (ev) => { if (ev.target === m) m.style.display = 'none'; };
        document.body.appendChild(m);
    }
    document.getElementById('elrVerknTitle').textContent = `${e.code} — ${e.bezeichnung}`;
    const sel = document.getElementById('elrVerknSelect');
    sel.innerHTML = kandidaten.map(l =>
        `<option value="${l.id}">${_elrEsc(l.code)} — ${_elrEsc(l.bezeichnung)}</option>`).join('');
    document.getElementById('elrVerknOk').onclick = async () => {
        const lpId = parseInt(sel.value, 10);
        if (!lpId) return;
        const r = await fetch(`/api/elm-lohnraster/${id}/verknuepfen`, {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ lohnpositionId: lpId }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { showToast(j.message || j.error || 'Fehler', 'error'); return; }
        m.style.display = 'none';
        const lp = kandidaten.find(l => l.id === lpId);
        showToast(`Verknüpft mit ${lp ? lp.code : lpId}.`, 'success');
        elrInit();
    };
    m.style.display = 'block';
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
