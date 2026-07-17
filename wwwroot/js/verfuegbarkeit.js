// ═══════════════════════════════════════════════════════════════════════════
//  VERFÜGBARKEIT-Tab im MA-Detail (Walter-Vorgabe 09.07.2026)
//  Zeigt die versionierten Verfügbarkeiten eines MA (EmployeeAvailability +
//  Slots). Datenquelle ist easy@work: der MA-Abgleich (Button «easy@work
//  synchronisieren») spiegelt availabilities + /days hierher. Sync-Versionen
//  tragen easyAtWorkAvailabilityId (Badge «easy@work»), manuelle sind ohne.
//  Anzeige read-only — gepflegt wird in easy@work.
//  Dark-Mode: Klassen .vf-* (Liquid Glass, Walter 17.07.2026).
// ═══════════════════════════════════════════════════════════════════════════

async function loadVerfuegbarkeitTab(empId) {
    const box = document.getElementById('verfuegbarkeitContent');
    if (!box) return;
    box.innerHTML = '<div class="emp-placeholder" style="height:120px">Wird geladen…</div>';
    try {
        const res = await fetch(`/api/employees/${empId}/availability`, { headers: ah() });
        if (!res.ok) { box.innerHTML = `<div style="padding:16px;color:#b91c1c">Fehler beim Laden (${res.status}).</div>`; return; }
        vfRender(box, await res.json());
    } catch (e) {
        box.innerHTML = '<div style="padding:16px;color:#b91c1c">Netzwerkfehler beim Laden.</div>';
    }
}

function vfFmtDate(iso) {
    if (!iso) return '';
    return iso.slice(8, 10) + '.' + iso.slice(5, 7) + '.' + iso.slice(0, 4);
}
function vfFmtTime(t) { return t ? t.slice(0, 5) : ''; }

function vfRender(box, list) {
    if (!Array.isArray(list) || !list.length) {
        box.innerHTML = `<div class="vf-empty">
            Keine Verfügbarkeit erfasst. Die Verfügbarkeit wird in <b>easy@work</b> gepflegt und beim
            «easy@work synchronisieren» (Button oben) übernommen.</div>`;
        return;
    }

    const dows = [['mon','Mo'],['tue','Di'],['wed','Mi'],['thu','Do'],['fri','Fr'],['sat','Sa'],['sun','So']];
    let html = '';

    for (const a of list) {
        const zeitraum = a.validTo
            ? `${vfFmtDate(a.validFrom)} – ${vfFmtDate(a.validTo)}`
            : `ab ${vfFmtDate(a.validFrom)}`;
        const badges =
            (a.isCurrent ? '<span style="background:#dcfce7;color:#166534;border-radius:999px;padding:2px 10px;font-size:11px;font-weight:700;margin-left:8px">Aktuell</span>' : '') +
            (a.easyAtWorkAvailabilityId
                ? '<span style="background:#e7e1d8;color:#5a5348;border-radius:999px;padding:2px 10px;font-size:11px;font-weight:600;margin-left:8px" title="Aus easy@work synchronisiert — dort pflegen">easy@work</span>'
                : '<span style="background:#fef3c7;color:#92400e;border-radius:999px;padding:2px 10px;font-size:11px;font-weight:600;margin-left:8px">manuell</span>');

        let inhalt;
        if (a.type === 'unrestricted') {
            inhalt = '<div style="font-size:13px;color:#166534;font-weight:600;padding:4px 0">Uneingeschränkt verfügbar — alle Tage, ganztags</div>';
        } else {
            const perDow = {};
            for (const [key] of dows) perDow[key] = [];
            for (const s of (a.slots || []))
                for (const [key] of dows)
                    if (s[key]) perDow[key].push(s.von || s.bis ? `${vfFmtTime(s.von)}–${vfFmtTime(s.bis)}` : 'ganztags');
            inhalt = '<table style="border-collapse:collapse;width:100%;max-width:660px"><tr>'
                + dows.map(([, lbl]) => `<th class="vf-th">${lbl}</th>`).join('')
                + '</tr><tr>'
                + dows.map(([key]) => {
                    const v = perDow[key];
                    return `<td class="vf-td${v.length ? '' : ' empty'}">${v.length ? v.join('<br>') : '—'}</td>`;
                }).join('')
                + '</tr></table>';
        }

        html += `<div class="vf-card"${a.isCurrent ? '' : ' style="opacity:0.75"'}>
            <div style="display:flex;align-items:center;flex-wrap:wrap;margin-bottom:8px">
                <span class="vf-title">${zeitraum}</span>${badges}
            </div>
            ${inhalt}
            ${a.bemerkung ? `<div class="vf-foot" style="margin-top:6px">${a.bemerkung}</div>` : ''}
        </div>`;
    }

    html += `<div class="vf-foot">
        Nicht aufgeführte Wochentage gelten als <b>nicht verfügbar</b>. Quelle: easy@work —
        Änderungen dort erfassen und den MA oben mit «easy@work synchronisieren» abgleichen.</div>`;
    box.innerHTML = html;
}
