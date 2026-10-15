// Personaldaten-Plausibilität (Walter 15.10.2026, Swissdec F06_01).
// Regeln stehen im Server (Services/Personaldaten/PersonaldatenPruefung.cs);
// hier nur Anzeige und «Bist du sicher?». Geladen in index.html UND import.html.

function _pdEsc(t) {
    return String(t ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

// import.html hat kein app-core.js — dort derselbe Dialog im Liquid-Look.
function _pdConfirm(message, opts = {}) {
    if (typeof window.liquidConfirm === 'function') return window.liquidConfirm(message, opts);
    return new Promise(resolve => {
        const old = document.getElementById('pdConfirmModal');
        if (old) old.remove();
        const wrap = document.createElement('div');
        wrap.id = 'pdConfirmModal';
        wrap.style.cssText = 'position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9800;display:flex;align-items:center;justify-content:center';
        wrap.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:460px;width:92%;padding:22px 24px">
            <div style="font-size:15px;font-weight:800;color:#3f3f3f;margin-bottom:8px">${_pdEsc(opts.title || 'Frage')}</div>
            <div style="font-size:13.5px;color:#646464;line-height:1.5;white-space:pre-line">${_pdEsc(message)}</div>
            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:20px">
                <button id="pdcNo" style="${opts.hideNo ? 'display:none;' : ''}background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">${_pdEsc(opts.noLabel || 'Nein')}</button>
                <button id="pdcYes" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">${_pdEsc(opts.yesLabel || 'Ja')}</button>
            </div>
        </div>`;
        document.body.appendChild(wrap);
        const done = v => { wrap.remove(); document.removeEventListener('keydown', onKey); resolve(v); };
        const onKey = e => { if (e.key === 'Escape') done(false); };
        document.addEventListener('keydown', onKey);
        wrap.addEventListener('click', e => { if (e.target === wrap) done(false); });
        wrap.querySelector('#pdcNo').onclick = () => done(false);
        wrap.querySelector('#pdcYes').onclick = () => done(true);
        wrap.querySelector('#pdcYes').focus();
    });
}

/** «Bist du sicher?» zu einer Liste von Befunden ({code, text, wert}). */
function pdFrage(befunde, name) {
    const liste = (befunde || []).map(b => '• ' + (b.text || b.Text || '')).join('\n');
    return _pdConfirm((name ? name + '\n\n' : '') + liste + '\n\nStimmt das so?', {
        title: 'Personaldaten bestätigen', yesLabel: 'Ja, stimmt so', noLabel: 'Nein, korrigieren',
    });
}

/**
 * fetch für Speichern von MA-Stammdaten: antwortet der Server mit 409
 * PERSONALDATEN_BESTAETIGEN, wird gefragt und bei «Ja» mit Bestätigung erneut gesendet.
 * Liefert immer die letzte Response (bei «Nein» die 409).
 */
async function pdFetch(url, opts = {}) {
    const res = await fetch(url, opts);
    if (res.status !== 409) return res;
    const j = await res.clone().json().catch(() => null);
    const befunde = j && j.error === 'PERSONALDATEN_BESTAETIGEN' ? (j.befunde || []) : [];
    if (!befunde.length || !await pdFrage(befunde)) return res;
    const kopf = befunde.map(b => encodeURIComponent((b.code || '') + '|' + (b.wert || ''))).join(',');
    return fetch(url, { ...opts, headers: { ...(opts.headers || {}), 'X-Personaldaten-Bestaetigt': kopf } });
}

/** Fehlermeldung aus einer Server-Antwort (JSON mit message oder Text). */
async function pdFehlertext(res) {
    const t = await res.text().catch(() => '');
    try { const j = JSON.parse(t); return j.message || j.error || t; } catch { return t; }
}

// ── Karte «Personaldaten prüfen» in der MA-Übersicht ─────────────────────────
async function pdKarteLaden(empId) {
    const el = document.getElementById('pdKarte');
    if (!el) return;
    let befunde = [];
    try {
        const r = await fetch(`/api/employees/${empId}/personaldaten-pruefung`, { headers: ah(), cache: 'no-store' });
        if (r.ok) befunde = (await r.json()).befunde || [];
    } catch (_) { return; }
    if (typeof selectedEmployeeId !== 'undefined' && selectedEmployeeId !== empId) return;
    if (!befunde.length) { el.style.display = 'none'; el.innerHTML = ''; return; }

    const fehler = befunde.filter(b => b.stufe === 'fehler');
    const zeile = (b, i) => {
        const korrigieren = b.code.startsWith('FAM_') || b.code === 'KIND_ZU_ALT'
            ? 'im Tab «Familie» korrigieren'
            : 'in easy@work korrigieren';
        const aktion = b.stufe === 'bestaetigen'
            ? `<button onclick="pdBestaetigen(${empId}, ${i})" style="flex-shrink:0;padding:4px 12px;border:1px solid rgba(139,139,139,0.35);border-radius:10px;background:rgba(255,255,255,0.6);cursor:pointer;font-size:12px;font-weight:600;color:#3f3f3f">Stimmt so</button>`
            : `<span style="flex-shrink:0;font-size:11.5px;color:#8b8b8b">${korrigieren}</span>`;
        return `<div style="display:flex;gap:10px;align-items:center;justify-content:space-between;padding:5px 0;border-top:1px solid rgba(139,139,139,0.15)">
            <span style="font-size:12.5px;color:#3f3f3f">${b.stufe === 'fehler' ? '✗' : '?'} ${_pdEsc(b.text)}</span>${aktion}</div>`;
    };
    window._pdBefunde = befunde;
    el.innerHTML = `
        <div style="background:${fehler.length ? 'rgba(243,231,231,0.75)' : 'rgba(253,246,221,0.75)'};border:1px solid ${fehler.length ? '#d8b8b8' : '#e4d28a'};border-radius:12px;padding:10px 14px;margin-bottom:10px">
            <div style="font-size:13px;font-weight:700;color:#3f3f3f;margin-bottom:4px">🪪 Personaldaten prüfen</div>
            ${befunde.map(zeile).join('')}
        </div>`;
    el.style.display = '';
}

async function pdBestaetigen(empId, i) {
    const b = (window._pdBefunde || [])[i];
    if (!b || !await pdFrage([b])) return;
    const r = await fetch(`/api/employees/${empId}/personaldaten-bestaetigen`, {
        method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ code: b.code, wert: b.wert || '' }),
    });
    if (!r.ok) { await _pdConfirm(await pdFehlertext(r), { title: 'Nicht gespeichert', yesLabel: 'OK', hideNo: true }); return; }
    pdKarteLaden(empId);
}

// ── «Neuer MA aus easy@work»: Rückfragen pro MA ──────────────────────────────
function pdNeuzugangHtml(j, cpId) {
    const liste = j.bestaetigen || [];
    if (!liste.length) return '';
    window._pdNeuzugang = liste;
    return `<div style="background:rgba(253,246,221,0.75);border:1px solid #e4d28a;border-radius:10px;padding:10px 12px;font-size:12.5px;margin-top:8px;color:#3f3f3f">
        <div style="font-weight:700;margin-bottom:4px">? Bitte bestätigen — erst danach wird übernommen:</div>
        ${liste.map((x, i) => `
            <div style="display:flex;gap:10px;align-items:center;justify-content:space-between;padding:6px 0;border-top:1px solid rgba(139,139,139,0.18)">
                <div style="min-width:0">
                    <div style="font-weight:600">${_pdEsc(((x.firstName || '') + ' ' + (x.lastName || '')).trim())} <span style="color:#8b8b8b;font-family:monospace;font-weight:400">${_pdEsc(x.number || '')}</span></div>
                    <div style="color:#646464">${(x.befunde || []).map(b => _pdEsc(b.text)).join('<br>')}</div>
                </div>
                <button onclick="pdNeuzugangBestaetigen(${cpId}, ${i})" style="flex-shrink:0;padding:5px 12px;border:1px solid rgba(139,139,139,0.35);border-radius:10px;background:rgba(255,255,255,0.6);cursor:pointer;font-size:12px;font-weight:600;color:#3f3f3f">Bestätigen</button>
            </div>`).join('')}
    </div>`;
}

async function pdNeuzugangBestaetigen(cpId, i) {
    const x = (window._pdNeuzugang || [])[i];
    if (!x) return;
    const name = ((x.firstName || '') + ' ' + (x.lastName || '')).trim();
    if (!await pdFrage(x.befunde, name)) return;
    const r = await fetch('/api/easywork/neuzugang/bestaetigen', {
        method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({
            companyProfileId: cpId, eawEmployeeId: x.eawEmployeeId, employeeId: x.employeeId ?? null,
            befunde: (x.befunde || []).map(b => ({ code: b.code, wert: b.wert || '' })),
        }),
    });
    if (!r.ok) { await _pdConfirm(await pdFehlertext(r), { title: 'Nicht gespeichert', yesLabel: 'OK', hideNo: true }); return; }
    if (typeof empImportFromEasyApi === 'function') empImportFromEasyApi();
}
