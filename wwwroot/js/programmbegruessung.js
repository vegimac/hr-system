// ══════════════════════════════════════════════════════════════════════
// Programmbegrüssung (Walter 04.09.2026): Begrüssungs-Pool der Anmeldeseite
// pro Tageszeit pflegen. Entwicklung › Programmbegrüssung.
// ══════════════════════════════════════════════════════════════════════
let _pbg = { slots: [], standard: [] };

async function pbgInit() {
    const alertEl = document.getElementById('pbgAlert');
    if (alertEl) alertEl.style.display = 'none';
    try {
        const r = await fetch('/api/login-greeting/admin', { headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const j = await r.json();
        _pbg.slots = j.slots || [];
        _pbg.standard = j.standard || [];
        pbgRender();
        const st = document.getElementById('pbgStatus');
        if (st) st.textContent = j.individuell
            ? `Eigene Texte gespeichert${j.updatedAt ? ' am ' + new Date(j.updatedAt).toLocaleString('de-CH') : ''}.`
            : 'Zurzeit gelten die Standardtexte.';
    } catch (e) {
        if (alertEl) { alertEl.style.display = 'block'; alertEl.innerHTML = `<div style="padding:10px;background:#fee2e2;color:#b91c1c;border-radius:8px;font-size:13px">Fehler: ${esc(e.message)}</div>`; }
    }
}

function pbgRender() {
    const box = document.getElementById('pbgSlots');
    if (!box) return;
    const h = new Date().getHours();
    box.innerHTML = _pbg.slots.map(s => {
        const jetzt = h >= s.von && h < s.bis;
        const zeit = `${String(s.von).padStart(2, '0')}:00 – ${String(s.bis).padStart(2, '0')}:00`;
        return `
        <div style="background:#fff;border-radius:12px;padding:12px 14px;box-shadow:0 2px 6px rgba(60,55,48,0.10);${jetzt ? 'outline:2px solid #3f3f3f' : ''}">
            <div style="display:flex;align-items:baseline;gap:8px;margin-bottom:6px">
                <div style="font-weight:700;font-size:13.5px">${esc(s.label)}</div>
                <div style="font-size:11.5px;color:#8b8b8b">${zeit}</div>
                ${jetzt ? '<span style="margin-left:auto;font-size:10.5px;font-weight:600;background:#e7f0e7;color:#3f5540;border-radius:999px;padding:1px 8px">jetzt</span>' : ''}
            </div>
            <textarea data-key="${esc(s.key)}" rows="${Math.max(4, s.texte.length + 1)}" spellcheck="false"
                      style="width:100%;box-sizing:border-box;border:1px solid #e2ddd3;border-radius:9px;padding:8px 10px;font-size:13px;line-height:1.5;font-family:inherit;resize:vertical"
                      oninput="pbgSync()">${esc(s.texte.join('\n'))}</textarea>
        </div>`;
    }).join('');
    pbgBeispiel();
}

function pbgSync() {
    document.querySelectorAll('#pbgSlots textarea').forEach(t => {
        const s = _pbg.slots.find(x => x.key === t.dataset.key);
        if (s) s.texte = t.value.split('\n').map(x => x.trim()).filter(Boolean);
    });
}

function pbgBeispiel() {
    pbgSync();
    const h = new Date().getHours();
    const s = _pbg.slots.find(x => h >= x.von && h < x.bis);
    const slotEl = document.getElementById('pbgNowSlot');
    const bsp = document.getElementById('pbgBeispiel');
    if (slotEl) slotEl.textContent = s ? s.label : '–';
    if (bsp) bsp.textContent = s && s.texte.length ? s.texte[Math.floor(Math.random() * s.texte.length)] : '–';
}

async function pbgSave() {
    pbgSync();
    const st = document.getElementById('pbgStatus');
    if (st) st.textContent = 'Speichern …';
    try {
        const r = await fetch('/api/login-greeting/admin', { method: 'PUT', headers: ah(), body: JSON.stringify({ slots: _pbg.slots }) });
        const j = await r.json().catch(() => ({}));
        if (!r.ok || !j.ok) throw new Error(j.error || ('HTTP ' + r.status));
        _pbg.slots = j.slots || _pbg.slots;
        pbgRender();
        if (st) st.textContent = 'Gespeichert — gilt ab der nächsten Anmeldung.';
    } catch (e) {
        if (st) st.textContent = 'Fehler beim Speichern: ' + e.message;
    }
}

async function pbgReset() {
    const ok = typeof liquidConfirm === 'function'
        ? await liquidConfirm('Alle Begrüssungen auf den Standard zurücksetzen?', { yesLabel: 'Zurücksetzen' })
        : confirm('Alle Begrüssungen auf den Standard zurücksetzen?');
    if (!ok) return;
    try {
        const r = await fetch('/api/login-greeting/admin', { method: 'DELETE', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (!r.ok || !j.ok) throw new Error(j.error || ('HTTP ' + r.status));
        _pbg.slots = j.slots || _pbg.standard;
        pbgRender();
        const st = document.getElementById('pbgStatus');
        if (st) st.textContent = 'Zurückgesetzt — es gelten die Standardtexte.';
    } catch (e) { alert('Fehler: ' + e.message); }
}
