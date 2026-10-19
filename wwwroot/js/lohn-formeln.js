// Entwicklung → Lohn-Formeln (read-only Registry, Walter 19.10.2026)
async function loadLohnFormeln() {
  const box = document.getElementById('lohnFormelnBody');
  const inv = document.getElementById('lohnFormelnInventar');
  if (!box) return;
  box.innerHTML = '<p style="color:#8b8b8b">Laden…</p>';
  try {
    const r = await fetch('/api/lohn-formeln', { headers: { Authorization: 'Bearer ' + localStorage.hrToken } });
    if (!r.ok) throw new Error('HTTP ' + r.status);
    const d = await r.json();
    const hinweis = document.getElementById('lohnFormelnHinweis');
    if (hinweis) hinweis.textContent = d.hinweis || '';
    box.innerHTML = `<table class="data-table" style="width:100%;font-size:13px;border-collapse:collapse">
      <thead><tr>
        <th style="text-align:left;padding:6px 8px">Id</th>
        <th style="text-align:left;padding:6px 8px">Titel</th>
        <th style="text-align:left;padding:6px 8px">Formel</th>
        <th style="text-align:left;padding:6px 8px">Parameter</th>
        <th style="text-align:left;padding:6px 8px">Swissdec</th>
        <th style="text-align:left;padding:6px 8px">Mirus</th>
        <th style="text-align:left;padding:6px 8px">Schema</th>
      </tr></thead>
      <tbody>${(d.formeln || []).map(f => `<tr style="border-top:1px solid rgba(60,55,48,0.12)">
        <td style="padding:6px 8px;font-family:ui-monospace,monospace;font-size:12px">${esc(f.id)}</td>
        <td style="padding:6px 8px">${esc(f.titel)}</td>
        <td style="padding:6px 8px;color:#3f3f3f">${esc(f.formelText)}</td>
        <td style="padding:6px 8px;color:#646464;font-size:12px">${esc(f.parameterQuellen)}</td>
        <td style="padding:6px 8px;font-family:ui-monospace,monospace">${esc(f.swissdecCode || '—')}</td>
        <td style="padding:6px 8px;font-family:ui-monospace,monospace;color:#8b8b8b">${esc(f.mirusCode || '—')}</td>
        <td style="padding:6px 8px">${esc(f.schemaArt || '—')}</td>
      </tr>`).join('')}</tbody></table>`;
    if (inv) {
      inv.innerHTML = `<table class="data-table" style="width:100%;font-size:12.5px;border-collapse:collapse;margin-top:8px">
        <thead><tr>
          <th style="text-align:left;padding:5px 8px">Alt (Mirus)</th>
          <th style="text-align:left;padding:5px 8px">Swissdec</th>
          <th style="text-align:left;padding:5px 8px">Formel</th>
          <th style="text-align:left;padding:5px 8px">Art</th>
          <th style="text-align:left;padding:5px 8px">Bemerkung</th>
        </tr></thead>
        <tbody>${(d.inventar || []).map(z => `<tr style="border-top:1px solid rgba(60,55,48,0.10)">
          <td style="padding:5px 8px;font-family:ui-monospace,monospace">${esc(z.alterCode)}</td>
          <td style="padding:5px 8px;font-family:ui-monospace,monospace">${esc(z.swissdecCode)}</td>
          <td style="padding:5px 8px">${esc(z.formelId)}</td>
          <td style="padding:5px 8px">${esc(z.schemaArt)}</td>
          <td style="padding:5px 8px;color:#646464">${esc(z.bemerkung)}</td>
        </tr>`).join('')}</tbody></table>`;
    }
  } catch (e) {
    box.innerHTML = `<p style="color:#b91c1c">Fehler: ${esc(e.message || String(e))}</p>`;
  }
}

function esc(s) {
  return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

document.addEventListener('DOMContentLoaded', () => {
  const orig = window.showPage;
  if (typeof orig !== 'function') return;
  window.showPage = function (name) {
    orig.apply(this, arguments);
    if (name === 'lohn-formeln') loadLohnFormeln();
  };
});
