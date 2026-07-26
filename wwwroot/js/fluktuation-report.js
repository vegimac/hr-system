// ═══════════════════════════════════════════════════════════════════════════
//  FLUKTUATION / Ein- & Austritte (Walter 26.07.2026)
//  Zeitraum frei · KPIs · Donut Austrittsgründe · namentliche Listen
//  Endpoint: GET /api/reports/fluktuation?from=&to=
// ═══════════════════════════════════════════════════════════════════════════

function flukInit() {
    const fromEl = document.getElementById('flukFrom');
    const toEl = document.getElementById('flukTo');
    if (fromEl && !fromEl.value) {
        const y = new Date().getFullYear();
        fromEl.value = `${y}-01-01`;
    }
    if (toEl && !toEl.value) {
        const t = new Date();
        toEl.value = t.toISOString().slice(0, 10);
    }
    flukLoad();
}

async function flukLoad() {
    const box = document.getElementById('flukResult');
    if (!box) return;
    const from = document.getElementById('flukFrom')?.value || '';
    const to = document.getElementById('flukTo')?.value || '';
    box.innerHTML = '<div style="padding:20px;color:#8b8b8b">Lade Auswertung…</div>';
    try {
        const q = new URLSearchParams();
        if (from) q.set('from', from);
        if (to) q.set('to', to);
        const res = await fetch('/api/reports/fluktuation?' + q.toString(), { headers: ah() });
        if (!res.ok) {
            box.innerHTML = `<div style="padding:20px;color:#b91c1c">Fehler beim Laden (${res.status}).</div>`;
            return;
        }
        flukRender(await res.json());
    } catch {
        box.innerHTML = '<div style="padding:20px;color:#b91c1c">Netzwerkfehler beim Laden.</div>';
    }
}

function flukFmtDate(iso) {
    if (!iso) return '—';
    return iso.slice(8, 10) + '.' + iso.slice(5, 7) + '.' + iso.slice(0, 4);
}

function flukEsc(s) {
    return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function flukRender(data) {
    const box = document.getElementById('flukResult');
    if (!box) return;

    const kpi = (label, value, hint) => `
        <div style="background:#fff;border:1px solid rgba(255,255,255,0.72);border-radius:14px;
                    box-shadow:0 8px 24px rgba(60,55,48,0.06);padding:14px 16px;min-width:0">
            <div style="font-size:10.5px;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;color:#8b8b8b">${label}</div>
            <div style="font-size:26px;font-weight:760;color:#1a1a1a;margin-top:4px;letter-spacing:-0.02em">${value}</div>
            ${hint ? `<div style="font-size:11.5px;color:#8b8b8b;margin-top:4px">${hint}</div>` : ''}
        </div>`;

    let html = `<div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px;margin-bottom:16px">
        ${kpi('Eintritte', data.eintritteCount ?? 0, flukFmtDate(data.from) + ' – ' + flukFmtDate(data.to))}
        ${kpi('Austritte', data.austritteCount ?? 0, '')}
        ${kpi('Bestand Anfang', data.bestandAnfang ?? 0, 'am ' + flukFmtDate(data.from))}
        ${kpi('Bestand Ende', data.bestandEnde ?? 0, 'am ' + flukFmtDate(data.to))}
        ${kpi('Fluktuation', (data.fluktuationsratePct ?? 0) + ' %', data.fluktuationsFormel || '')}
        ${kpi('Ø Verbleib', data.avgVerbleibMonate != null ? (data.avgVerbleibMonate + ' Mt.') : '—', 'bei Austritten im Zeitraum')}
    </div>`;

    html += `<div style="display:grid;grid-template-columns:minmax(240px,340px) minmax(0,1fr);gap:12px;margin-bottom:18px">
        <div style="background:#fff;border:1px solid #e7e1d8;border-radius:14px;padding:14px 16px;box-shadow:0 8px 24px rgba(60,55,48,0.06)">
            <div style="font-size:13.5px;font-weight:760;color:#1a1a1a;margin-bottom:10px">Austrittsgründe</div>
            ${flukPieHtml(data.gruende || [])}
        </div>
        <div style="background:#fff;border:1px solid #e7e1d8;border-radius:14px;padding:14px 16px;box-shadow:0 8px 24px rgba(60,55,48,0.06)">
            <div style="font-size:13.5px;font-weight:760;color:#1a1a1a;margin-bottom:10px">Verteilung</div>
            ${flukGruendeLegend(data.gruende || [], data.austritteCount || 0)}
        </div>
    </div>`;

    html += flukTableBlock('Austritte', data.austritte || [], true);
    html += flukTableBlock('Eintritte', data.eintritte || [], false);

    html += `<div style="font-size:11.5px;color:#8b8b8b;margin-top:10px">
        Alle Filialen · Phantom-MA (ohne Lohn) ausgenommen ·
        Eintritt = Firmen-Eintrittsdatum · Austritt = Austrittsdatum am MA ·
        Filiale = ältester Vertrag (Hauptfiliale).
    </div>`;

    box.innerHTML = html;
}

const _FLUK_PIE_COLORS = [
    '#3f3f3f', '#6b7280', '#8b7355', '#5c6b5a', '#7a6a58',
    '#4a5568', '#9a8470', '#5a6d7a', '#7d6b7d', '#6a7a5c',
    '#8a6a5a', '#5a5a6a', '#7a7a5a', '#6a5a5a', '#b8b0a4',
];

function flukPieHtml(gruende) {
    const total = gruende.reduce((s, g) => s + (g.count || 0), 0);
    if (!total) {
        return `<div style="height:200px;display:flex;align-items:center;justify-content:center;color:#8b8b8b;font-size:13px">
            Keine Austritte im Zeitraum</div>`;
    }
    const R = 78, CX = 100, CY = 100, W = 200, H = 200;
    let angle = -Math.PI / 2;
    let paths = '';
    gruende.forEach((g, i) => {
        const frac = (g.count || 0) / total;
        const a0 = angle;
        const a1 = angle + frac * Math.PI * 2;
        angle = a1;
        if (frac <= 0) return;
        const x0 = CX + R * Math.cos(a0), y0 = CY + R * Math.sin(a0);
        const x1 = CX + R * Math.cos(a1), y1 = CY + R * Math.sin(a1);
        const large = frac > 0.5 ? 1 : 0;
        const col = _FLUK_PIE_COLORS[i % _FLUK_PIE_COLORS.length];
        if (frac >= 0.999) {
            paths += `<circle cx="${CX}" cy="${CY}" r="${R}" fill="${col}"/>`;
        } else {
            paths += `<path d="M ${CX} ${CY} L ${x0} ${y0} A ${R} ${R} 0 ${large} 1 ${x1} ${y1} Z" fill="${col}"/>`;
        }
    });
    // Donut-Loch
    paths += `<circle cx="${CX}" cy="${CY}" r="42" fill="#fff"/>`;
    paths += `<text x="${CX}" y="${CY - 4}" text-anchor="middle" font-size="20" font-weight="760" fill="#1a1a1a">${total}</text>`;
    paths += `<text x="${CX}" y="${CY + 14}" text-anchor="middle" font-size="11" fill="#8b8b8b">Austritte</text>`;
    return `<svg viewBox="0 0 ${W} ${H}" width="100%" style="max-width:220px;display:block;margin:0 auto">${paths}</svg>`;
}

function flukGruendeLegend(gruende, austritteTotal) {
    if (!gruende.length) {
        return '<div style="color:#8b8b8b;font-size:13px;padding:12px 0">Keine Daten</div>';
    }
    const total = gruende.reduce((s, g) => s + (g.count || 0), 0) || 1;
    return `<div style="display:flex;flex-direction:column;gap:6px">` +
        gruende.map((g, i) => {
            const pct = Math.round((g.count || 0) / total * 100);
            const col = _FLUK_PIE_COLORS[i % _FLUK_PIE_COLORS.length];
            return `<div style="display:flex;align-items:center;gap:10px;font-size:13px">
                <span style="width:10px;height:10px;border-radius:3px;background:${col};flex-shrink:0"></span>
                <span style="flex:1;color:#3f3f3f;font-weight:600">${flukEsc(g.label)}</span>
                <span style="color:#8b8b8b;font-variant-numeric:tabular-nums">${g.count}</span>
                <span style="width:40px;text-align:right;color:#8b8b8b;font-variant-numeric:tabular-nums">${pct}%</span>
            </div>`;
        }).join('') +
        (austritteTotal > total
            ? `<div style="font-size:11.5px;color:#8b8b8b;margin-top:6px">Summe Gründe = ${total} (alle Austritte = ${austritteTotal})</div>`
            : '') +
        `</div>`;
}

function flukTableBlock(title, rows, isExit) {
    const head = isExit
        ? `<th>Name</th><th>Filiale</th><th>Eintritt</th><th>Austritt</th><th>Künd. per</th><th>Durch</th><th>Grund</th><th>Verbleib</th>`
        : `<th>Name</th><th>Filiale</th><th>Eintritt</th>`;
    let body;
    if (!rows.length) {
        body = `<tr><td colspan="${isExit ? 8 : 3}" style="padding:16px;color:#8b8b8b;text-align:center">Keine ${title.toLowerCase()} im Zeitraum</td></tr>`;
    } else {
        body = rows.map(r => {
            if (isExit) {
                const vb = r.verbleibMonate != null ? (r.verbleibMonate + ' Mt.') : '—';
                return `<tr>
                    <td>${flukEsc(r.name)}</td>
                    <td>${flukEsc(r.branchName)}</td>
                    <td>${flukFmtDate(r.entryDate)}</td>
                    <td>${flukFmtDate(r.exitDate)}</td>
                    <td>${flukFmtDate(r.kuendigungPer)}</td>
                    <td>${flukEsc(r.kuendigungDurch)}</td>
                    <td>${flukEsc(r.austrittsgrund)}</td>
                    <td>${vb}</td>
                </tr>`;
            }
            return `<tr>
                <td>${flukEsc(r.name)}</td>
                <td>${flukEsc(r.branchName)}</td>
                <td>${flukFmtDate(r.entryDate)}</td>
            </tr>`;
        }).join('');
    }
    return `
    <div style="background:#fff;border:1px solid #e7e1d8;border-radius:14px;padding:12px 14px;margin-bottom:12px;box-shadow:0 8px 24px rgba(60,55,48,0.06)">
        <div style="font-size:13.5px;font-weight:760;color:#1a1a1a;margin-bottom:8px">${title} <span style="font-weight:600;color:#8b8b8b">(${rows.length})</span></div>
        <div style="overflow-x:auto">
            <table class="fluk-table" style="width:100%;border-collapse:collapse;font-size:13px">
                <thead><tr style="background:#efeae2;color:#3f3f3f;text-align:left">${head.replace(/<th>/g, '<th style="padding:7px 8px;border-bottom:1px solid #d8d1c4;font-size:12px;white-space:nowrap">')}</tr></thead>
                <tbody>${body.replace(/<td>/g, '<td style="padding:7px 8px;border-bottom:1px solid #f0ebe3;color:#3f3f3f">')}</tbody>
            </table>
        </div>
    </div>`;
}
