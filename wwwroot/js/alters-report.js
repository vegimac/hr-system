// ═══════════════════════════════════════════════════════════════════════════
//  ALTERS-REPORT über alle Filialen (Walter-Vorgabe 08.07.2026)
//  Matrix: Zeilen = Alterskategorien, Spalten = Filialen, Zellen = namentlich.
//  Kategorien (exklusiv, flächendeckend): <16 · 16–17 · 18–29 · 30–44 ·
//  45–49 · 50+ · Pension ≤ 1 Jahr ·
//  ohne Geburtsdatum. Nur aktive MA mit laufendem Vertrag.
//  Endpoint: GET /api/reports/alter (+ /pdf für A4-quer).
// ═══════════════════════════════════════════════════════════════════════════

function alterInit() { alterLoad(); }

async function alterLoad() {
    const box = document.getElementById('alterResult');
    if (!box) return;
    box.innerHTML = '<div style="padding:20px;color:#8b8b8b">Lade Auswertung…</div>';
    try {
        const res = await fetch('/api/reports/alter', { headers: ah() });
        if (!res.ok) {
            box.innerHTML = `<div style="padding:20px;color:#b91c1c">Fehler beim Laden (${res.status}).</div>`;
            return;
        }
        alterRender(await res.json());
    } catch (e) {
        box.innerHTML = '<div style="padding:20px;color:#b91c1c">Netzwerkfehler beim Laden.</div>';
    }
}

function alterFmtDate(iso) {
    if (!iso) return '';
    return iso.slice(8, 10) + '.' + iso.slice(5, 7) + '.' + iso.slice(0, 4);
}

function alterRender(data) {
    const box = document.getElementById('alterResult');
    const branches = data.branches || [];
    const kats = data.kategorien || [];

    const th = (txt) => `<th style="text-align:left;padding:8px 10px;background:#efeae2;border-bottom:1px solid #d8d1c4;font-size:12.5px;color:#3f3f3f;white-space:nowrap">${txt}</th>`;

    let html = `<div style="overflow-x:auto;background:#f6f3ee;border:1px solid #e7e1d8;border-radius:14px;padding:4px">
        <table style="border-collapse:collapse;width:100%;min-width:${170 + branches.length * 120}px">
        <thead><tr>${th('Alterskategorie')}${branches.map(b => th(b.name || '')).join('')}</tr></thead><tbody>`;

    for (const k of kats) {
        if (k.key === 'ogeb' && k.total === 0) continue; // Hinweis-Zeile nur bei Bedarf
        html += `<tr>
            <td style="vertical-align:top;padding:8px 10px;border-bottom:1px solid #e7e1d8;min-width:150px">
                <div style="font-weight:700;font-size:13px;color:#3f3f3f">${k.label} <span style="font-weight:600;color:#8b8b8b">(${k.total})</span></div>
                ${k.hint ? `<div style="font-size:11px;color:#8b8b8b;margin-top:2px">${k.hint}</div>` : ''}
            </td>`;
        for (const b of branches) {
            const list = (k.perBranch && k.perBranch[String(b.id)]) || [];
            html += `<td style="vertical-align:top;padding:8px 10px;border-bottom:1px solid #e7e1d8">`;
            const anzahl = (k.counts && k.counts[String(b.id)]) || list.length;
            if (!anzahl) {
                html += '<span style="color:#c2bbae">—</span>';
            } else if (k.namentlich === false) {
                // nur Anzahl (18–29 / 30–44 — sonst wird die Liste zu lang)
                html += `<span style="font-size:13px;font-weight:700;color:#3f3f3f">${anzahl}</span>`;
            } else {
                html += list.map(p =>
                    `<div style="margin-bottom:1px;font-size:12.5px;color:#3f3f3f;white-space:nowrap">${p.name}${p.alter != null ? ` <span style="color:#8b8b8b">${p.alter}</span>` : ''}</div>`
                ).join('');
            }
            html += '</td>';
        }
        html += '</tr>';
    }

    // Total-Zeile: Summe aller aktiven MA pro Filiale + gesamt
    const tdT = 'padding:8px 10px;border-top:1px solid #d8d1c4;background:#efeae2;font-weight:700;font-size:13px;color:#3f3f3f';
    html += `<tr><td style="${tdT}">Total aktive MA <span style="color:#8b8b8b">(${data.totalAll ?? 0})</span></td>`;
    for (const b of branches)
        html += `<td style="${tdT}">${(data.totalPerBranch && data.totalPerBranch[String(b.id)]) || 0}</td>`;
    html += '</tr>';

    html += `</tbody></table></div>`;
    html += alterCurveHtml(data.alterVerteilung || []);
    html += `<div style="font-size:11.5px;color:#8b8b8b;margin-top:8px">
            Stichtag ${alterFmtDate(data.stichtag)} · nur aktive Mitarbeiter mit laufendem Vertrag ·
            Kategorien sind exklusiv (jeder MA erscheint genau einmal) ·
            Pension = AHV-Referenzalter erreicht oder in weniger als 12 Monaten.
        </div>`;
    box.innerHTML = html;
}

// Altersverteilungs-KURVE über alle Filialen (Walter-Vorgabe 08.07.2026):
// X-Achse fix 15–65, alle 5 Jahre ein Punkt = Anzahl Personen im 5-Jahres-Band
// ([15–20) … [60–65), letzter Punkt 65+; unter 15 zählt zum ersten Band).
// Jede Person genau einmal (Hauptfiliale-Dedup kommt vom Server).
function alterCurveHtml(ages) {
    if (!ages.length) return '';
    // Bänder: die KRITISCHEN Gruppen <16 und 16–17 haben eigene Punkte (rot),
    // dann 18–19 und ab 20 5-Jahres-Bänder bis 65+. Punkt = Band-Mitte.
    const bands = [
        { from: 15, to: 16, label: '&lt;16', kritisch: true }, // «<» in SVG/HTML escapen!
        { from: 16, to: 18, label: '16–17', kritisch: true },
        { from: 18, to: 20, label: '', kritisch: false },
    ];
    for (let t = 20; t < 65; t += 5) bands.push({ from: t, to: t + 5, label: '', kritisch: false });
    bands.push({ from: 65, to: 70, label: '65+', kritisch: false });

    const counts = Array(bands.length).fill(0);
    for (const a of ages) {
        const v = Math.min(69.9, Math.max(15, a));
        for (let i = 0; i < bands.length; i++)
            if (v >= bands[i].from && v < bands[i].to) { counts[i]++; break; }
    }
    const maxCount = Math.max(1, ...counts);

    const W = 860, H = 210, ml = 22, mr = 22, top = 34, bottom = 26;
    const XA = age => ml + (W - ml - mr) * (age - 15) / (70 - 15);
    const Y = c => top + (H - top - bottom) * (1 - c / maxCount);
    const mid = i => (bands[i].from + bands[i].to) / 2;

    const gridTicks = [16, 18];
    for (let t = 20; t <= 65; t += 5) gridTicks.push(t);
    let grid = '', labels = '';
    for (const t of gridTicks) {
        grid += `<line x1="${XA(t)}" y1="${top}" x2="${XA(t)}" y2="${H - bottom}" stroke="#e7e1d8" stroke-width="1"/>`;
        labels += `<text x="${XA(t)}" y="${H - 8}" font-size="10.5" fill="#8b8b8b" text-anchor="middle">${t}${t === 65 ? '+' : ''}</text>`;
    }

    let pts = '', dots = '';
    for (let i = 0; i < bands.length; i++) {
        const x = XA(mid(i)), y = Y(counts[i]);
        const farbe = bands[i].kritisch ? '#b91c1c' : '#8a7d63';
        pts += `${x},${y} `;
        dots += `<circle cx="${x}" cy="${y}" r="3.2" fill="${farbe}"/>`;
        if (counts[i] || bands[i].kritisch)
            dots += `<text x="${x}" y="${y - 7}" font-size="11" font-weight="700" fill="${bands[i].kritisch ? '#b91c1c' : '#404040'}" text-anchor="middle">${counts[i]}</text>`;
        if (bands[i].kritisch)
            dots += `<text x="${x}" y="${top - 20}" font-size="10" fill="#b91c1c" text-anchor="middle">${bands[i].label}</text>`;
    }
    let area = `M${XA(mid(0))} ${H - bottom} `;
    for (let i = 0; i < bands.length; i++) area += `L${XA(mid(i))} ${Y(counts[i])} `;
    area += `L${XA(mid(bands.length - 1))} ${H - bottom} Z`;

    return `<div style="background:#f6f3ee;border:1px solid #e7e1d8;border-radius:14px;padding:14px 16px 8px;margin-top:12px">
        <div style="font-weight:700;font-size:13px;color:#3f3f3f;margin-bottom:4px">Altersverteilung über alle Filialen
            <span style="font-weight:400;color:#8b8b8b;font-size:11.5px">(${ages.length} Personen, jede genau einmal · &lt;16 und 16–17 einzeln, ab 20 5-Jahres-Bänder)</span></div>
        <svg viewBox="0 0 ${W} ${H}" style="width:100%;max-width:1000px;display:block" font-family="inherit">
            ${grid}
            <line x1="${ml}" y1="${H - bottom}" x2="${W - mr}" y2="${H - bottom}" stroke="#c8c0b2" stroke-width="1.2"/>
            <path d="${area}" fill="#b8ab93" fill-opacity="0.3"/>
            <polyline points="${pts}" fill="none" stroke="#8a7d63" stroke-width="2.4" stroke-linejoin="round" stroke-linecap="round"/>
            ${dots}
            ${labels}
        </svg>
    </div>`;
}

async function alterPdf() {
    await previewUrlFetch('/api/reports/alter/pdf', 'altersstruktur.pdf', ah());
}
