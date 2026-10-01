// ══════════════════════════════════════════════════════════════════════
//  MANAGER-SCHULUNGEN — seit 01.10.2026 nur noch Einstiege + Excel-Import.
//  Nothelfer / Peak / SECO sind Typen im Verzeichnis «Schulungen &
//  Ausbildungen»; erfasst wird im MA (Tab «Verfügbarkeit / Training»),
//  die Übersicht ist «Schulungen» unter Auswertungen (js/schulungen.js).
// ══════════════════════════════════════════════════════════════════════

function msOpenFiliale() { showPage('schulungen-uebersicht'); }
function msOpenAlle()    { showPage('schulungen-uebersicht'); }

// Import aus der Nothelfer-Excel (admin): Vorschau (dryRun) → Commit.
// Schreibt Einträge in die Schulungs-Historie (Art «übernommen»).
function msImportExcel() {
    const inp = document.createElement('input');
    inp.type = 'file';
    inp.accept = '.xlsx';
    inp.onchange = async () => {
        const f = inp.files?.[0];
        if (!f) return;
        const fd = new FormData();
        fd.append('file', f);
        showToast('Excel wird analysiert…', 'info');
        try {
            // Bei FormData KEIN ah() (Content-Type-Falle) — nur Bearer.
            const res = await fetch('/api/manager-schulungen/import-excel?dryRun=true', {
                method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd,
            });
            const j = await res.json();
            if (!res.ok) { showToast(j.message || j.error || 'Analyse fehlgeschlagen.', 'error'); return; }
            const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;');
            const un = (j.unmatched || []).map(u => `<li>Zeile ${u.zeile}: ${esc(u.name)} <span style="color:#8b8b8b">(${esc(u.grund)})</span></li>`).join('');
            const mtRows = (j.matched || []).slice(0, 60).map(m => `
                <tr><td style="padding:2px 6px">${esc(m.name)}</td>
                    <td style="padding:2px 6px;color:#166534">→ ${esc(m.maName)} (${esc(m.employeeNumber || '')})</td>
                    <td style="padding:2px 6px;color:#8b8b8b;font-size:11px">${esc(m.eid || '')} ${esc(m.sso || '')}</td></tr>`).join('');
            const ov = document.createElement('div');
            ov.id = 'msImportModal';
            ov.style.cssText = 'position:fixed;inset:0;background:rgba(30,28,25,0.45);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px';
            ov.innerHTML = `
                <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 18px 50px rgba(60,55,48,0.22);max-width:680px;width:100%;max-height:85vh;overflow:auto;padding:20px 22px">
                    <div style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:8px">Schulungs-Import — Vorschau</div>
                    <div style="font-size:13px;color:#3f3f3f;margin-bottom:8px"><b>${(j.matched || []).length}</b> Zeilen zugeordnet, <b>${(j.unmatched || []).length}</b> ohne Zuordnung.</div>
                    ${un ? `<div style="font-size:12.5px;color:#991b1b;margin-bottom:8px"><b>Nicht zugeordnet (übersprungen):</b><ul style="margin:4px 0 0 18px">${un}</ul></div>` : ''}
                    <table style="width:100%;border-collapse:collapse;font-size:12px;margin-bottom:10px">${mtRows}</table>
                    <div style="font-size:11.5px;color:#8b8b8b;margin-bottom:12px">Nur gefüllte Excel-Werte werden übernommen. Jedes Datum wird ein Eintrag in der Schulungs-Historie; bestehende Einträge bleiben.</div>
                    <div style="display:flex;justify-content:flex-end;gap:10px">
                        <button id="msImpCancel" class="btn btn-secondary">Abbrechen</button>
                        <button id="msImpOk" class="btn btn-primary">Importieren</button>
                    </div>
                </div>`;
            ov.onclick = (e) => { if (e.target === ov) ov.remove(); };
            document.body.appendChild(ov);
            document.getElementById('msImpCancel').onclick = () => ov.remove();
            document.getElementById('msImpOk').onclick = async () => {
                ov.remove();
                const fd2 = new FormData();
                fd2.append('file', f);
                const res2 = await fetch('/api/manager-schulungen/import-excel?dryRun=false', {
                    method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` }, body: fd2,
                });
                const j2 = await res2.json().catch(() => ({}));
                if (!res2.ok) { showToast(j2.message || 'Import fehlgeschlagen.', 'error'); return; }
                showToast(`${j2.neueEintraege} Schulungs-Einträge übernommen (${j2.updated} Mitarbeitende).`, 'success');
                if (typeof schulungUebersichtInit === 'function') schulungUebersichtInit();
            };
        } catch (_) { showToast('Verbindungsfehler.', 'error'); }
    };
    inp.click();
}
