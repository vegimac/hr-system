// Walter-Vorgabe 27.05.2026: Hilfe-Side-Panel mit Markdown-Seiten.
// Klick auf den ?-Button oeffnet ein Panel von rechts mit:
//   - Inhaltsverzeichnis (HELP_PAGES — statisch unten)
//   - Markdown-Renderer (laedt /help/<slug>.md aus wwwroot/help/)
//
// Markdown-Render: minimal genug fuer Headlines, Listen, Code, Bold,
// Italic, Links und Tabellen — KEIN Volltext-Marked-Loader (extern).
// Saemtliche Inhalte liegen als statische .md-Files unter wwwroot/help/
// und koennen vom Admin direkt im Server bearbeitet werden.

// Rollenbasierte Kapitel (Walter-Vorgabe 07.07.2026): jedes Kapitel trägt
// eine `roles`-Liste — der Benutzer sieht NUR die Kapitel der Programmteile,
// zu denen seine Rolle berechtigt ist. Kein `roles`-Feld = für alle sichtbar.
// Rollen: admin, superuser, user (GF), buchhaltung, lowuser (employee nutzt
// die separate Postfach-Seite und sieht dieses Panel nie).
const HELP_PAGES = [
    { slug: 'index',         title: '🏠 Übersicht' },
    { slug: 'mitarbeiter',   title: 'Mitarbeiter' },
    { slug: 'vertraege',     title: 'Verträge' },
    { slug: 'lohnlauf',      title: 'Lohnlauf',                    roles: ['admin', 'superuser', 'user', 'buchhaltung'] },
    { slug: 'qst',           title: 'Quellensteuer',               roles: ['admin', 'superuser', 'user', 'buchhaltung'] },
    { slug: 'moments',       title: 'Moments (Mitteilungen)',      roles: ['admin', 'superuser', 'user', 'buchhaltung'] },
    { slug: 'sms',           title: 'SMS & Vertrags-Link',         roles: ['admin', 'superuser', 'user', 'buchhaltung'] },
    { slug: 'dokumente',     title: 'Dokumente & Posteingang',     roles: ['admin', 'superuser', 'user', 'buchhaltung'] },
    { slug: 'fibu',          title: 'Buchhaltung (Fibu)',          roles: ['admin', 'buchhaltung'] },
    { slug: 'audit',         title: 'Aktivitäts-Log',              roles: ['admin'] },
    { slug: 'suche',         title: 'Globale Suche (⌘K)' },
    { slug: 'rollen',        title: 'Rollen & Berechtigungen',     roles: ['admin'] },
];

// Sichtbare Kapitel für den eingeloggten Benutzer. Unbekannte Rolle
// (currentUser noch nicht geladen) → nur die rollen-freien Kapitel.
function helpVisiblePages() {
    const role = (typeof currentUser !== 'undefined' && currentUser && currentUser.role) || null;
    return HELP_PAGES.filter(p => !p.roles || (role && p.roles.includes(role)));
}

function helpCanSee(slug) {
    return helpVisiblePages().some(p => p.slug === slug);
}

// Walter-Vorgabe 28.05.2026: kontextuelle Hilfe — `helpOpen()` ohne
// Parameter findet die passende Hilfe-Seite zur aktuell sichtbaren
// Programm-Seite UND ggf. zum aktiven Tab.
//
// Detektion-Quelle ist primaer die globale `currentPageName` (wird in
// showPage gesetzt — deterministisch). DOM-Suche nur als Fallback.
// Bei Mitarbeiter-Seite zusaetzlich `activeEmpTab` lesen — der QST-Tab
// oeffnet z.B. direkt die QST-Hilfe, der Dokumente-Tab die Dokumente-Hilfe.
const HELP_PAGE_BY_APP_PAGE = {
    'dashboard':         'index',
    'mitarbeiter':       'mitarbeiter',
    'vertraege':         'vertraege',
    'lohn':              'lohnlauf',
    'lohnlauf':          'lohnlauf',
    'akonto-lauf':       'lohnlauf',
    'perioden':          'lohnlauf',
    'qst-anmeldung':     'qst',
    'posteingang':       'dokumente',
    'audit-log':         'audit',
    'benutzer':          'rollen',
    'admin-hub':         'index',
    'moments':           'moments',
    'fibu':              'fibu',
    'ecall':             'sms',
};

// Tab-spezifisches Mapping: wenn auf Mitarbeiter-Seite ein bestimmter
// Tab aktiv ist, springe direkt in die passende Hilfe (statt nur auf
// die allgemeine Mitarbeiter-Seite).
const HELP_PAGE_BY_EMP_TAB = {
    'personal':       'mitarbeiter',
    'familie':        'mitarbeiter',
    'bank':           'mitarbeiter',
    'quellensteuer':  'qst',
    'stempelzeiten':  'mitarbeiter',
    'absenzen':       'mitarbeiter',
    'zeiten':         'mitarbeiter', // Kurzzeit-Alias → Absenzen
    'zulagen':        'mitarbeiter',
    'ktg':            'mitarbeiter', // Alias → Absenzen (Tab entfernt)
    'dokumente':      'dokumente',
};

// Lesbares Label fuer den Kontext-Hinweis im Header
// (z.B. "Mitarbeiter · Quellensteuer").
function helpContextLabel(ctx) {
    if (!ctx || !ctx.page) return null;
    const pageLabels = {
        'dashboard': 'Dashboard',
        'mitarbeiter': 'Mitarbeiter',
        'vertraege': 'Verträge',
        'lohn': 'Lohnlauf',
        'lohnlauf': 'Lohnlauf',
        'akonto-lauf': 'Lohnlauf (Akonto)',
        'perioden': 'Lohnperioden',
        'qst-anmeldung': 'QST-Anmeldung',
        'posteingang': 'Posteingang',
        'audit-log': 'Aktivitäts-Log',
        'benutzer': 'Benutzer',
        'admin-hub': 'Systemeinstellungen',
        'moments': 'Moments',
        'fibu': 'Buchhaltung (Fibu)',
        'ecall': 'SMS (eCall)',
    };
    const tabLabels = {
        'personal': 'Persönliche Angaben',
        'familie': 'Familie',
        'bank': 'Bank',
        'quellensteuer': 'Quellensteuer',
        'stempelzeiten': 'Stempelzeiten',
        'absenzen': 'Absenzen / KTG/UVG',
        'zeiten': 'Absenzen / KTG/UVG', // Kurzzeit-Alias
        'zulagen': 'Zulagen & Abzüge',
        'ktg': 'Absenzen / KTG/UVG', // Tab entfernt → Alias
        'dokumente': 'Dokumente',
    };
    const pageLbl = pageLabels[ctx.page] || ctx.page;
    if (ctx.page === 'mitarbeiter'
        && typeof activeEmpTab !== 'undefined' && activeEmpTab
        && tabLabels[activeEmpTab]) {
        return pageLbl + ' · ' + tabLabels[activeEmpTab];
    }
    return pageLbl;
}

function helpDetectCurrentPage() {
    // 1) Primaer: globale currentPageName aus showPage()
    let page = (typeof currentPageName !== 'undefined' && currentPageName) ? currentPageName : null;
    // 2) Fallback: DOM-Suche nach .page.active (nur falls currentPageName fehlt)
    if (!page) {
        const active = document.querySelector('.page.active');
        if (active) {
            const m = (active.id || '').match(/^page-(.+)$/);
            if (m) page = m[1];
        }
    }
    // 3) Sub-Kontext: Tab-spezifisch auf der Mitarbeiter-Seite
    if (page === 'mitarbeiter'
        && typeof activeEmpTab !== 'undefined' && activeEmpTab
        && HELP_PAGE_BY_EMP_TAB[activeEmpTab]) {
        return { page, slug: HELP_PAGE_BY_EMP_TAB[activeEmpTab] };
    }
    if (page && HELP_PAGE_BY_APP_PAGE[page]) {
        return { page, slug: HELP_PAGE_BY_APP_PAGE[page] };
    }
    return { page, slug: null };
}

function helpOpen(slug) {
    helpClose(); // falls schon offen — sauber schliessen + neu
    // Kontextuelle Auswahl: kein Slug uebergeben? → ermittle aus
    // currentPageName + activeEmpTab.
    let initial = slug;
    let ctxInfo = null;
    if (!initial) {
        const ctx = helpDetectCurrentPage();
        if (ctx && ctx.slug) { initial = ctx.slug; ctxInfo = ctx; }
        try { console.log('[Help] Kontext-Detektion:', ctx, '→ slug:', initial); } catch (_) {}
    }
    if (!initial) initial = 'index';   // Kontext > letzter Slug > index
    // Rollen-Filter: Kapitel ausserhalb der Berechtigung → auf Übersicht.
    if (!helpCanSee(initial)) initial = 'index';

    // Kontext-Hinweis-Text fuer den Header („Du bist auf: …")
    const ctxLabel = helpContextLabel(ctxInfo);

    const html = `
    <div id="helpPanel" style="
        position:fixed; top:0; right:0; width:38vw; height:100vh;
        min-width:360px; max-width:60vw;
        background:#fff; box-shadow:-8px 0 30px rgba(0,0,0,0.18);
        z-index:9700; display:flex; flex-direction:column; overflow:hidden;
        transform:translateX(100%); transition:transform .22s ease-out;
    ">
        <div id="helpResizeLeft" title="Breite ziehen"
             style="position:absolute;left:0;top:0;bottom:0;width:6px;cursor:ew-resize;z-index:6"></div>
        <div style="display:flex;justify-content:space-between;align-items:center;gap:10px;padding:10px 14px;border-bottom:1px solid #e2e8f0;background:#f8fafc">
            <div style="display:flex;flex-direction:column;gap:1px;min-width:0">
                <div style="display:flex;align-items:center;gap:8px;font-size:14px;font-weight:700;color:#0f172a">
                    <span style="font-size:16px">❓</span><span>Hilfe</span>
                </div>
                ${ctxLabel ? `<div style="font-size:11px;color:#64748b;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">📍 Du bist auf: <span style="color:#1a1a1a;font-weight:500">${ctxLabel}</span></div>` : ''}
            </div>
            <div style="display:flex;align-items:center;gap:6px;flex-shrink:0">
                <button onclick="helpOpen('index')" title="Startseite" style="background:#fff;border:1px solid #cbd5e1;border-radius:4px;padding:3px 8px;font-size:12px;cursor:pointer">🏠</button>
                <button onclick="helpClose()" style="background:none;border:none;font-size:20px;cursor:pointer;color:#94a3b8;padding:0 6px">×</button>
            </div>
        </div>
        <div style="display:flex;flex:1;overflow:hidden">
            <nav id="helpNav" style="width:200px;flex-shrink:0;border-right:1px solid #e2e8f0;overflow-y:auto;padding:8px 0;background:#f8fafc;font-size:12.5px">
                ${helpVisiblePages().map(p => `
                <div onclick="helpOpen('${p.slug}')"
                     id="helpNav-${p.slug}"
                     style="padding:7px 12px;cursor:pointer;color:#334155;border-left:3px solid transparent;line-height:1.3"
                     onmouseover="if(this.dataset.active!=='1')this.style.background='#f1efe9'"
                     onmouseout="if(this.dataset.active!=='1')this.style.background='transparent'">
                    ${p.title}
                </div>`).join('')}
            </nav>
            <div id="helpContent" style="flex:1;overflow-y:auto;padding:18px 22px;font-size:14px;line-height:1.6;color:#1e293b">
                <div style="color:#94a3b8">Lade …</div>
            </div>
        </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
    requestAnimationFrame(() => {
        const p = document.getElementById('helpPanel');
        if (p) p.style.transform = 'translateX(0)';
    });
    helpMakeLeftResizable();
    helpLoad(initial);
    // ESC schliesst
    document.addEventListener('keydown', _helpEscHandler);
}

function helpClose() {
    const p = document.getElementById('helpPanel');
    if (p) p.remove();
    document.removeEventListener('keydown', _helpEscHandler);
}

function _helpEscHandler(e) {
    if (e.key === 'Escape') helpClose();
}

async function helpLoad(slug) {
    // Rollen-Filter auch bei internen Links (#slug) durchsetzen.
    if (!helpCanSee(slug)) slug = 'index';
    helpReadLastSlug.last = slug;
    try { localStorage.setItem('helpLastSlug', slug); } catch (_) {}
    // Nav-Highlight
    document.querySelectorAll('#helpNav > div').forEach(el => {
        const isActive = el.id === 'helpNav-' + slug;
        el.dataset.active = isActive ? '1' : '0';
        el.style.background       = isActive ? '#ece9e2' : 'transparent';
        el.style.borderLeftColor  = isActive ? '#1a1a1a' : 'transparent';
        el.style.color            = isActive ? '#6b6152' : '#334155';
        el.style.fontWeight       = isActive ? '600' : '400';
    });
    const cont = document.getElementById('helpContent');
    if (!cont) return;
    cont.innerHTML = '<div style="color:#94a3b8">Lade …</div>';
    try {
        // KEIN Bearer-Token noetig — die .md-Files liegen unter wwwroot/help/
        // und werden via UseStaticFiles ausgeliefert (public).
        const r = await fetch('/help/' + slug + '.md', { cache: 'no-store' });
        if (!r.ok) {
            cont.innerHTML = `<div style="color:#dc2626">Seite „${helpEsc(slug)}" nicht gefunden (HTTP ${r.status}).</div>`;
            return;
        }
        const md = await r.text();
        cont.innerHTML = helpRenderMarkdown(md);
        // Interne Links in der Hilfe (z.B. „[Quellensteuer](#qst)") auf helpOpen umlenken
        cont.querySelectorAll('a[href^="#"]').forEach(a => {
            a.addEventListener('click', (ev) => {
                ev.preventDefault();
                const target = a.getAttribute('href').slice(1);
                helpOpen(target);
            });
        });
        cont.scrollTop = 0;
    } catch (err) {
        cont.innerHTML = `<div style="color:#dc2626">Fehler: ${helpEsc(err.message)}</div>`;
    }
}

function helpReadLastSlug() {
    try { return localStorage.getItem('helpLastSlug'); } catch (_) { return null; }
}

// ── Minimaler Markdown-Renderer ───────────────────────────────────────
// Genug fuer Walters Hilfe: Headlines, Bold/Italic, Listen, Code, Links,
// horizontal rule, blockquote. Kein YAML-Frontmatter, kein HTML-Pass-
// through (Sicherheit: alles wird HTML-escaped, dann gezielt zurueck-
// gemustert).
function helpRenderMarkdown(md) {
    // Normalize line endings
    md = md.replace(/\r\n?/g, '\n');

    // Escape erst alles, dann gezielt zurueckmustern.
    let s = helpEsc(md);

    // Code-Blocks (```...```)
    s = s.replace(/```([\s\S]*?)```/g, (m, code) =>
        `<pre style="background:#0f172a;color:#e2e8f0;padding:12px;border-radius:4px;overflow-x:auto;font-size:12.5px;line-height:1.5">${code}</pre>`);

    // Inline code (`...`)
    s = s.replace(/`([^`\n]+)`/g, '<code style="background:#f1f5f9;border:1px solid #e2e8f0;border-radius:3px;padding:0 4px;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:13px">$1</code>');

    // Headlines
    s = s.replace(/^### (.+)$/gm, '<h3 style="font-size:14px;font-weight:700;color:#0f172a;margin:18px 0 6px">$1</h3>');
    s = s.replace(/^## (.+)$/gm,  '<h2 style="font-size:16px;font-weight:700;color:#0f172a;margin:20px 0 8px;padding-bottom:4px;border-bottom:1px solid #e2e8f0">$1</h2>');
    s = s.replace(/^# (.+)$/gm,   '<h1 style="font-size:20px;font-weight:800;color:#0f172a;margin:0 0 14px">$1</h1>');

    // Horizontal rule
    s = s.replace(/^---+$/gm, '<hr style="border:none;border-top:1px solid #e2e8f0;margin:14px 0">');

    // Bold (**...**)
    s = s.replace(/\*\*([^*\n]+)\*\*/g, '<strong>$1</strong>');
    // Italic (*...*)
    s = s.replace(/(^|[^*])\*([^*\n]+)\*/g, '$1<em>$2</em>');

    // Links [text](url) — internal #slug Links bleiben so, externe oeffnen im neuen Tab
    s = s.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (m, text, url) => {
        if (url.startsWith('#')) return `<a href="${url}" style="color:#1a1a1a;cursor:pointer;text-decoration:underline">${text}</a>`;
        return `<a href="${url}" target="_blank" rel="noopener" style="color:#1a1a1a;text-decoration:underline">${text}</a>`;
    });

    // Lists — Walter-Hilfe: einfache Unordered + Ordered, keine verschachtelten Levels.
    // Mehrere aufeinanderfolgende „- " Zeilen → <ul>
    s = s.replace(/(^|\n)((?:[ \t]*[-*] .+(?:\n|$))+)/g, (m, lead, block) => {
        const items = block.trim().split(/\n/).map(l => l.replace(/^[ \t]*[-*] /, '')).map(t => `<li>${t}</li>`).join('');
        return `${lead}<ul style="padding-left:22px;margin:6px 0">${items}</ul>`;
    });
    s = s.replace(/(^|\n)((?:[ \t]*\d+\. .+(?:\n|$))+)/g, (m, lead, block) => {
        const items = block.trim().split(/\n/).map(l => l.replace(/^[ \t]*\d+\. /, '')).map(t => `<li>${t}</li>`).join('');
        return `${lead}<ol style="padding-left:22px;margin:6px 0">${items}</ol>`;
    });

    // Blockquote
    s = s.replace(/^&gt; (.+)$/gm, '<blockquote style="border-left:3px solid #1a1a1a;background:#f6f3ee;padding:6px 10px;margin:8px 0;color:#5a5348">$1</blockquote>');

    // Paragraphs — leere Zeile = Absatz. Wir wickeln alle Zeilen die nicht
    // schon Block-Tags sind in <p>.
    const lines = s.split(/\n\n+/);
    s = lines.map(blk => {
        const t = blk.trim();
        if (!t) return '';
        if (/^<(h\d|ul|ol|pre|blockquote|hr)/.test(t)) return t;
        return `<p style="margin:6px 0">${t.replace(/\n/g, '<br>')}</p>`;
    }).join('\n');

    return s;
}

function helpEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c]));
}

// Resize-Anfasser an der linken Kante (Panel ist rechts verankert)
function helpMakeLeftResizable() {
    const panel  = document.getElementById('helpPanel');
    const handle = document.getElementById('helpResizeLeft');
    if (!panel || !handle) return;
    let resizing = false, startX = 0, startW = 0, shield = null;
    handle.addEventListener('mousedown', (e) => {
        startX = e.clientX; startW = panel.offsetWidth;
        resizing = true;
        shield = document.createElement('div');
        shield.style.cssText = 'position:fixed;inset:0;z-index:10001;cursor:ew-resize';
        document.body.appendChild(shield);
        e.preventDefault(); e.stopPropagation();
    });
    function onMove(e) {
        if (!resizing) return;
        let w = startW + (startX - e.clientX);
        w = Math.max(340, Math.min(window.innerWidth * 0.70, w));
        panel.style.width = w + 'px';
    }
    function onUp() {
        if (!resizing) return;
        resizing = false;
        if (shield) { shield.remove(); shield = null; }
        try { localStorage.setItem('helpWidth', panel.offsetWidth); } catch (_) {}
    }
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);

    // Gespeicherte Breite wiederherstellen
    try {
        const saved = parseInt(localStorage.getItem('helpWidth') || '0', 10);
        if (saved >= 340 && saved <= window.innerWidth * 0.70) panel.style.width = saved + 'px';
    } catch (_) {}
}
