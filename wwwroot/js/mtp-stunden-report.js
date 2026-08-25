// ═══════════════════════════════════════════════════════════════════════════
//  MTP-STUNDEN-KONTROLLE (Walter 25.08.2026)
//  Wer erfüllt seine garantierten Wochenstunden — wer liegt drüber/drunter?
//  Eine Spalte pro voller ISO-Woche (gestempelt + angerechnete Absenz),
//  letzte Spalte Ø. KEINE Toleranz: Ø ≠ Garantie wird farblich markiert
//  (rot = zuviel, orange = zuwenig). Filiale = IMMER Sidebar-Selektor.
//  Endpoint: GET /api/reports/mtp-stunden?from=&to=&companyProfileId=
// ═══════════════════════════════════════════════════════════════════════════

let _mtpwData = null;

function mtpwInit() {
    const fromEl = document.getElementById('mtpwFrom');
    const toEl = document.getElementById('mtpwTo');
    if (toEl && !toEl.value) toEl.value = new Date().toISOString().slice(0, 10);
    if (fromEl && !fromEl.value) {
        const d = new Date();
        d.setMonth(d.getMonth() - 2);
        fromEl.value = d.toISOString().slice(0, 10);
    }
    mtpwLoad();
}

async function mtpwLoad() {
    const box = document.getElementById('mtpwResult');
    if (!box) return;
    const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
        ? fixedCompanyProfileId : null;
    if (!branchId) {
        box.innerHTML = '<div style="padding:20px;color:#8b8b8b">Bitte zuerst oben links in der Sidebar eine Filiale wählen.</div>';
        return;
    }
    const from = document.getElementById('mtpwFrom')?.value || '';
    const to = document.getElementById('mtpwTo')?.value || '';
    box.innerHTML = '<div style="padding:20px;color:#8b8b8b">Lädt…</div>';
    try {
        const qs = new URLSearchParams({ companyProfileId: branchId });
        if (from) qs.set('from', from);
        if (to) qs.set('to', to);
        const res = await fetch('/api/reports/mtp-stunden?' + qs.toString(), { headers: ah() });
        if (!res.ok) {
            box.innerHTML = `<div style="padding:20px;color:#b91c1c">Fehler beim Laden (HTTP ${res.status}).</div>`;
            return;
        }
        _mtpwData = await res.json();
        mtpwRender();
    } catch (e) {
        box.innerHTML = `<div style="padding:20px;color:#b91c1c">Verbindungsfehler: ${e.message}</div>`;
    }
}

// PDF-Ausdruck (A4 quer) im Vorschaufenster — gleiche Parameter wie die Ansicht.
async function mtpwPdf() {
    const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
        ? fixedCompanyProfileId : null;
    if (!branchId) return alert('Bitte zuerst oben links in der Sidebar eine Filiale wählen.');
    const from = document.getElementById('mtpwFrom')?.value || '';
    const to = document.getElementById('mtpwTo')?.value || '';
    const qs = new URLSearchParams({ companyProfileId: branchId });
    if (from) qs.set('from', from);
    if (to) qs.set('to', to);
    try {
        await previewUrlFetch(`/api/reports/mtp-stunden/pdf?${qs.toString()}`, 'MTP-Stunden-Kontrolle.pdf', ah());
    } catch (e) {
        alert('PDF fehlgeschlagen: ' + (e?.message || e));
    }
}

function mtpwRender() {
    const box = document.getElementById('mtpwResult');
    if (!box || !_mtpwData) return;
    const d = _mtpwData;
    const fmt = n => Number(n ?? 0).toFixed(2);
    const fmtD = iso => iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.` : '';

    if (!d.weeks || !d.weeks.length) {
        box.innerHTML = '<div style="padding:20px;color:#8b8b8b">Keine volle Woche im gewählten Zeitraum (nur komplette Wochen Mo–So bis heute zählen).</div>';
        return;
    }

    const wkTh = d.weeks.map(w =>
        `<th style="padding:7px 8px;text-align:right;white-space:nowrap" title="Woche ab Montag ${fmtD(w.monday)}${w.monday.slice(0,4)}">KW${w.kw}<br><span style="font-weight:500;color:#94a3b8">${fmtD(w.monday)}</span></th>`).join('');

    const tr = (d.rows || []).map(r => {
        const cells = (r.weeks || []).map(w => {
            if (!w) return '<td style="padding:6px 8px;text-align:right;color:#cbd5e1">–</td>';
            const tip = `gestempelt ${fmt(w.gearbeitet)} h + Absenz ${fmt(w.absenz)} h`;
            const absMark = Number(w.absenz) > 0 ? '<span style="color:#b45309">*</span>' : '';
            return `<td style="padding:6px 8px;text-align:right" title="${tip}">${fmt(w.total)}${absMark}</td>`;
        }).join('');
        // Keine Toleranz (Walter 25.08.2026, Farben final): ÜBER Garantie =
        // GRÜN (Garantie erfüllt), UNTER Garantie = ROT.
        let avgHtml = '<span style="color:#cbd5e1">–</span>';
        if (r.avg != null) {
            const avg = Number(r.avg), gar = Number(r.garantiertH || 0);
            const color = avg >= gar ? '#15803d' : '#b91c1c';
            const delta = avg - gar;
            const deltaTxt = (delta >= 0 ? '+' : '') + delta.toFixed(2);
            avgHtml = `<span style="color:${color};font-weight:700" title="Abweichung zur Garantie: ${deltaTxt} h/Wo">${fmt(avg)}</span>`;
        }
        // Schwangerschaft/Mutterschutz wie in der MA-Liste (Walter 25.08.2026).
        const preg = r.mutterschutz
            ? ' <span title="Mutterschutz — 16 Wochen nach Geburt">🍼</span>'
            : (r.schwanger ? ' <span title="Schwangerschaft aktiv">🤰</span>' : '');
        // Stunden-Saldo «aktuell» wie in der MA-Maske (Walter 25.08.2026):
        // + grün, − rot, «–» wenn kein laufender Vertrag im aktuellen Monat.
        let saldoHtml = '<span style="color:#cbd5e1">–</span>';
        if (r.saldoAktuell != null) {
            const sv = Number(r.saldoAktuell);
            const sc = sv >= 0 ? '#15803d' : '#b91c1c';
            saldoHtml = `<span style="color:${sc};font-weight:700" title="Stunden-Saldo per heute (wie MA-Maske «aktuell»)">${(sv > 0 ? '+' : '') + sv.toFixed(2)}</span>`;
        }
        return `<tr>
            <td style="padding:6px 10px;white-space:nowrap">${esc(r.vorname ?? '')}</td>
            <td style="padding:6px 10px;white-space:nowrap">${esc(r.name ?? '')}${preg}</td>
            <td style="padding:6px 10px;text-align:right;font-weight:800;color:#0a0a0a">${fmt(r.garantiertH)}</td>
            ${cells}
            <td style="padding:6px 10px;text-align:right">${avgHtml}</td>
            <td style="padding:6px 10px;text-align:right">${saldoHtml}</td>
        </tr>`;
    }).join('');

    box.innerHTML = `
        <div style="margin-bottom:10px;color:#64748b;font-size:13px">
            ${d.rows.length} MTP-MA · nur VOLLE Wochen (Mo–So) bis heute ·
            Zelle = gestempelte Stunden + angerechnete Absenz (<span style="color:#b45309">*</span> = enthält Absenz, Details im Tooltip) ·
            <span style="color:#15803d;font-weight:600">grün = Garantie erreicht/übertroffen</span> ·
            <span style="color:#b91c1c;font-weight:600">rot = unter Garantie</span> ·
            🤰 Schwanger / 🍼 Mutterschutz ·
            sortiert: grösstes Minus zuoberst
        </div>
        <div class="card" style="padding:0">
        <table style="width:100%;border-collapse:collapse;font-size:13px">
            <thead><tr style="background:#f1efe9">
                <th style="padding:7px 10px;text-align:left">Vorname</th>
                <th style="padding:7px 10px;text-align:left">Name</th>
                <th style="padding:7px 10px;text-align:right" title="Garantierte Wochenstunden aus dem MTP-Vertrag">Garantie</th>
                ${wkTh}
                <th style="padding:7px 10px;text-align:right">Ø h/Wo</th>
                <th style="padding:7px 10px;text-align:right" title="Stunden-Saldo per heute — gleiche Zahl wie die Saldi-Karte «aktuell» in der MA-Maske">Saldo<br><span style="font-weight:500;color:#94a3b8">aktuell</span></th>
            </tr></thead>
            <tbody>${tr || '<tr><td colspan="99" style="padding:16px;color:#8b8b8b">Keine MTP-Verträge in dieser Filiale im Zeitraum.</td></tr>'}</tbody>
        </table>
        </div>`;
}
