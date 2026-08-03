// ═══════════════════════════════════════════════════════════════════
//  APP-CORE — ausgelagerter Inline-Kern aus index.html (Refactor
//  Etappe 2, 08.07.2026). Unverändert verschoben: State, Auth/Login,
//  showPage/Navigation, Filial-Selektor, Theme, Helfer. Läuft VOR
//  allen Modul-Dateien (save-blob.js etc.) — Position des <script
//  src>-Tags in index.html nicht verschieben!
// ═══════════════════════════════════════════════════════════════════
// ══════════════════════════════════════════════
// STATE
// ══════════════════════════════════════════════
let currentUser = null;

// ── Vertragsmodell-ANZEIGE (Walter-Vorgabe 08.07.2026) ──────────────────
// Nur die Anzeige: intern (DB, API, Engine, CSS-Klassen) heisst das Modell
// weiterhin «UTP» — dem Benutzer zeigen wir überall «FLEX» (der Begriff aus
// easy@work). Bei neuen Anzeige-Stellen IMMER modelDisplay(model) rendern,
// nie den rohen Code.
function modelDisplay(m) {
    return m === 'UTP' ? 'FLEX' : (m || '');
}

// Ortschaft ohne Kantons-Suffix anzeigen/speichern (Walter 02.08.2026):
// «Sursee (LU)» / «Sursee LU» → «Sursee». Kanton gehört ins eigene Feld.
function stripCityCantonSuffix(s) {
    let t = String(s || '').trim();
    if (!t) return '';
    const paren = t.match(/^(.*?)\s*\(([A-Za-z]{2})\)\s*$/);
    if (paren) t = paren[1].trim();
    const parts = t.split(/\s+/).filter(Boolean);
    if (parts.length > 1 && /^[A-Za-z]{2}$/.test(parts[parts.length - 1]))
        t = parts.slice(0, -1).join(' ');
    return t;
}
window.stripCityCantonSuffix = stripCityCantonSuffix;

// ── CHF-Beträge immer mit 2 Nachkommastellen anzeigen (Walter 08.07.2026) ──
// Gilt für alle <input type="number" step="0.01"> (= CHF-/Betragsfelder).
// WICHTIG: es wird nur AUFGEFÜLLT (20.4 → 20.40, 150 → 150.00), NIE gerundet —
// Werte mit mehr als 2 Dezimalstellen (z.B. SV-Satz 1.635) bleiben unberührt.
// Greift beim Verlassen des Feldes UND periodisch für programmatisch gefüllte
// Felder (Modal-Befüllung via JS löst kein Event aus).
function chfPadValue(el) {
    if (!el || el === document.activeElement) return;
    const v = (el.value || '').trim();
    if (!v) return;
    const n = parseFloat(String(v).replace(',', '.'));
    if (isNaN(n)) return;
    if (Math.abs(n - Math.round(n * 100) / 100) > 1e-9) return; // >2 Dezimalen → nicht anfassen
    const f = n.toFixed(2);
    if (el.value !== f) el.value = f;
}
document.addEventListener('focusout', (e) => {
    const el = e.target;
    if (el && el.matches && el.matches('input[type="number"][step="0.01"]')) {
        setTimeout(() => chfPadValue(el), 0);
    }
}, true);
// SOFORT-Formatierung bei programmatischer Befüllung (Modal-Öffnung setzt
// .value per JS — dafür den nativen value-Setter abfangen, kein sichtbares
// Nachspringen mehr). Direkt desc.set → keine Rekursion.
(function () {
    const desc = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
    if (!desc || !desc.set) return;
    Object.defineProperty(HTMLInputElement.prototype, 'value', {
        configurable: true,
        get: desc.get,
        set(v) {
            desc.set.call(this, v);
            if (this.type === 'number' && this.getAttribute('step') === '0.01'
                && this !== document.activeElement) {
                const s = (desc.get.call(this) || '').trim();
                if (s) {
                    const n = parseFloat(s.replace(',', '.'));
                    if (!isNaN(n) && Math.abs(n - Math.round(n * 100) / 100) < 1e-9) {
                        const f = n.toFixed(2);
                        if (s !== f) desc.set.call(this, f);
                    }
                }
            }
        }
    });
})();
// Inline gerendertes HTML (value="…" im Template-String) sofort beim
// Einfügen ins DOM formatieren.
new MutationObserver((muts) => {
    for (const m of muts) for (const node of m.addedNodes) {
        if (node.nodeType !== 1) continue;
        if (node.matches && node.matches('input[type="number"][step="0.01"]')) chfPadValue(node);
        if (node.querySelectorAll) node.querySelectorAll('input[type="number"][step="0.01"]').forEach(chfPadValue);
    }
}).observe(document.body, { childList: true, subtree: true });
let authToken = localStorage.getItem('hrToken');
let allBranches = [];
let editingUserId = null;
let currentBranchId = null;
let currentPageName = 'dashboard';

// Contract page state
let fixedCompanyProfileId = null;
let selectedCompanyProfile = null;
let selectedCsvFile = null;
let lastComplianceResult = null;
let employeeGenderMap = {};
let employeeDateOfBirthMap = {};
let currentSnapshot = null;
let nationalityCodeToId = {};

// ══════════════════════════════════════════════
// AUTH
// ══════════════════════════════════════════════
async function doLogin() {
    const email = document.getElementById('loginEmail').value.trim();
    const password = document.getElementById('loginPassword').value;
    const errEl = document.getElementById('loginError');
    errEl.style.display = 'none';
    try {
        const res = await fetch('/api/auth/login', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ email, password })
        });
        if (!res.ok) {
            const d = await res.json().catch(() => ({}));
            errEl.textContent = d.message || 'Anmeldung fehlgeschlagen.';
            errEl.style.display = 'block'; return;
        }
        const data = await res.json();
        authToken = data.token;
        localStorage.setItem('hrToken', authToken);
        // Email für nächsten Login merken (Convenience zusätzlich zum
        // Browser-Passwort-Manager). Passwort wird NIEMALS lokal
        // gespeichert — das übernimmt der Browser-Passwort-Manager
        // verschlüsselt im Keychain/Credential-Store.
        localStorage.setItem('hrLastEmail', email);
        currentUser = data.user;
        // Session-Policy (Walter 21.06.2026) — der Login liefert sie top-level,
        // der Wächter liest sie aus currentUser.
        currentUser.idleTimeoutMinutes = data.idleTimeoutMinutes;
        currentUser.maxSessionMinutes  = data.maxSessionMinutes;
        currentUser.sessionStartedAt   = data.sessionStartedAt;
        // ── Mitarbeiter-Login → eigene Mobile-View (Postfach) ──
        // Backoffice-User bleiben in der vollen App, MA werden auf eine
        // schlanke Mobile-Seite umgeleitet wo sie nur ihre Lohnzettel sehen.
        if (data.user?.role === 'employee') {
            window.location.href = 'postfach.html';
            return;
        }
        startApp();
    } catch {
        errEl.textContent = 'Verbindungsfehler. Bitte versuch es erneut.';
        errEl.style.display = 'block';
    }
}

// ── Anmeldung per Face ID / Touch ID (WebAuthn) ───────────────────────
async function faceIdLogin() {
    const errEl = document.getElementById('loginError');
    if (errEl) errEl.style.display = 'none';
    try {
        const data = await webauthnLoginRaw();   // { token, user, mustChangePassword }
        authToken = data.token;
        localStorage.setItem('hrToken', authToken);
        currentUser = data.user || null;
        if (data.user && data.user.role === 'employee') { window.location.href = 'postfach.html'; return; }
        await checkAuth();   // Backoffice: currentUser vollständig laden
        startApp();
    } catch (e) {
        // Abbruch durch den User (Face ID abgebrochen) → still ignorieren.
        if (e && (e.name === 'NotAllowedError' || e.name === 'AbortError')) return;
        if (errEl) { errEl.textContent = e.message || 'Face-ID-Anmeldung fehlgeschlagen.'; errEl.style.display = 'block'; }
    }
}

// ── Impersonation / „View-as" (Walter-Vorgabe 28.06.2026) ─────────────
// Superadmin wechselt mit seinem EIGENEN Passwort in einen anderen Benutzer,
// um zu testen, was dieser im Programm sieht. Das aktuelle Admin-Token wird
// gesichert (hrTokenAdmin), damit man zurückwechseln kann.
async function impersonateUser() {
    const u = document.getElementById('impUsername')?.value.trim();
    const p = document.getElementById('impPassword')?.value;
    const err = document.getElementById('impError');
    if (err) { err.style.display = 'none'; err.textContent = ''; }
    if (!u || !p) { if (err) { err.textContent = 'Benutzername und dein Passwort eingeben.'; err.style.display = 'block'; } return; }
    try {
        const res = await fetch('/api/auth/impersonate', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ targetUsername: u, password: p })
        });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) { if (err) { err.textContent = data.message || 'Wechsel fehlgeschlagen.'; err.style.display = 'block'; } return; }
        // Admin-Token sichern (nur beim ERSTEN Wechsel, nicht überschreiben).
        if (!localStorage.getItem('hrTokenAdmin'))
            localStorage.setItem('hrTokenAdmin', authToken);
        localStorage.setItem('hrImpersonating', JSON.stringify({ username: data.user.username, role: data.user.role }));
        authToken = data.token;
        localStorage.setItem('hrToken', authToken);
        if (document.getElementById('impPassword')) document.getElementById('impPassword').value = '';
        // Mitarbeiter-Rolle → schlanke Postfach-Ansicht (wie echter MA-Login).
        if (data.user?.role === 'employee') { window.location.href = 'postfach.html'; return; }
        location.reload();
    } catch {
        if (err) { err.textContent = 'Verbindungsfehler.'; err.style.display = 'block'; }
    }
}

// Zurück ins eigene (Admin-)Konto.
function stopImpersonation() {
    const adminTok = localStorage.getItem('hrTokenAdmin');
    if (!adminTok) { doLogout(); return; }
    localStorage.setItem('hrToken', adminTok);
    localStorage.removeItem('hrTokenAdmin');
    localStorage.removeItem('hrImpersonating');
    authToken = adminTok;
    location.reload();
}

// Hinweis-Balken oben, solange impersoniert wird. Wird aus startApp() gerufen.
function renderImpersonationBanner() {
    let info = null;
    try { info = JSON.parse(localStorage.getItem('hrImpersonating') || 'null'); } catch {}
    let bar = document.getElementById('impersonationBanner');
    if (!info) { if (bar) bar.remove(); return; }
    if (!bar) { bar = document.createElement('div'); bar.id = 'impersonationBanner'; document.body.appendChild(bar); }
    const uname = (info.username || '?').replace(/</g, '&lt;');
    bar.innerHTML = `🧪 Testmodus — du siehst die App als <b>${uname}</b> (${info.role || ''}). ` +
        `<button onclick="stopImpersonation()">↩ Zurück zu meinem Konto</button>`;
}

// Beim Login-Bildschirm: gespeicherte Email vorausfüllen falls vorhanden.
// Das Passwort-Feld wird vom Browser-Manager bedient.
(function prefillLoginEmail() {
    const lastEmail = localStorage.getItem('hrLastEmail');
    if (!lastEmail) return;
    const trySet = () => {
        const el = document.getElementById('loginEmail');
        if (el && !el.value) {
            el.value = lastEmail;
            // Cursor ins Passwort-Feld, damit User direkt tippen kann
            const pw = document.getElementById('loginPassword');
            if (pw) pw.focus();
        }
    };
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', trySet);
    } else {
        trySet();
    }
})();

document.getElementById('loginPassword').addEventListener('keydown', e => { if (e.key === 'Enter') doLogin(); });

async function checkAuth() {
    if (!authToken) return false;
    try {
        const res = await fetch('/api/auth/me', { headers: ah() });
        if (!res.ok) { authToken = null; localStorage.removeItem('hrToken'); return false; }
        currentUser = await res.json(); return true;
    } catch { return false; }
}

// ── Theme (light/dark) ──────────────────────────────────────────────
// Wendet Theme via CSS-Klasse auf <body> an und passt Toggle-Icon an.
function applyTheme(theme) {
    const t = (theme === 'dark') ? 'dark' : 'light';
    document.body.classList.toggle('theme-dark', t === 'dark');
    const btn = document.getElementById('themeToggleBtn');
    if (btn) {
        // Icon + Label zeigen das ZIEL des Klicks:
        //   hell aktiv  → 🌙 "Dunkel"  (Klick schaltet auf dunkel)
        //   dunkel aktiv→ ☀️ "Hell"    (Klick schaltet auf hell)
        const icon  = btn.querySelector('.tt-icon');
        const label = btn.querySelector('.tt-label');
        if (icon)  icon.textContent  = (t === 'dark') ? '☀️' : '🌙';
        if (label) {
            const key = (t === 'dark') ? 'topbar.themeLight' : 'topbar.themeDark';
            label.textContent = window.i18n ? window.i18n.t(key)
                                            : (t === 'dark' ? 'Hell' : 'Dunkel');
        }
    }
    if (currentUser) currentUser.theme = t;
}

// Hell ↔ Dunkel umschalten und persistent speichern.
// Quelle der Wahrheit = body-Klasse (nicht nur currentUser.theme) —
// sonst bleibt der Toggle stecken, wenn Profil/State auseinanderlaufen.
async function toggleTheme() {
    const current = document.body.classList.contains('theme-dark')
        || (currentUser?.theme === 'dark')
        ? 'dark' : 'light';
    const next = (current === 'dark') ? 'light' : 'dark';
    applyTheme(next);   // sofort sichtbar
    try {
        await fetch('/api/auth/theme', {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ theme: next })
        });
    } catch { /* Server-Fehler ignorieren — UI-State bleibt korrekt */ }
}
window.toggleTheme = toggleTheme;
window.applyTheme = applyTheme;

function doLogout() {
    authToken = null; currentUser = null;
    localStorage.removeItem('hrToken');
    // Impersonation-/Testmodus-Reste miträumen, sonst hängt der Balken.
    localStorage.removeItem('hrTokenAdmin');
    localStorage.removeItem('hrImpersonating');
    // Voller Reload — verhindert Stale-State (alte Dropdown-Listen, allBranches,
    // Postfächer eines anderen Users etc.) Nach dem Reload erscheint das Login,
    // beim nächsten Login lädt der Benutzer mit frischen Daten.
    location.reload();
}

function ah() { return { 'Authorization': `Bearer ${authToken}`, 'Content-Type': 'application/json' }; }

/** Ops-Rolle auf GF-sichtbaren Screens (Walter 20.07.2026): admin/superuser/
 *  user(GF)/buchhaltung — alles freigeben, was der GF sehen und bedienen soll.
 *  Nicht für Systemeinstellungen, MA löschen, HR-4-Augen-Lohnaktionen. */
function isOpsRole(role) {
    const r = role || (typeof currentUser !== 'undefined' ? currentUser?.role : '');
    return r === 'admin' || r === 'superuser' || r === 'user' || r === 'buchhaltung';
}
window.isOpsRole = isOpsRole;

function updateDashboardShellState(name) {
    const page = document.getElementById('page-' + name);
    document.body.classList.toggle('liquid-shell-active', !!page?.classList.contains('liquid-ui'));
    document.body.classList.toggle('dashboard-shell-active', name === 'dashboard');
}

function syncLiquidDashboardChrome() {
    const lqSel = document.getElementById('liquidBranchSelect');
    const srcSel = document.getElementById('branchSelect');
    if (lqSel && srcSel) {
        lqSel.innerHTML = srcSel.innerHTML;
        lqSel.value = srcSel.value || '';
    }
    syncLiquidBranchMenu();
    syncSidebarBranchMenu();
    const uname = document.getElementById('liquidUserName');
    const urole = document.getElementById('liquidUserRole');
    const uava  = document.getElementById('liquidUserAvatar');
    if (uname) uname.textContent = currentUser?.username || '–';
    if (urole) urole.textContent = currentUser ? (currentUser.isSuperAdmin ? 'Super-Admin' : roleName(currentUser.role)) : '–';
    if (uava)  uava.textContent = (currentUser?.username || 'U')[0].toUpperCase();
    syncLiquidDashboardCards();
    syncLiquidEmployeeBranchSelect();
}

function onLiquidBranchChange() {
    const lqSel = document.getElementById('liquidBranchSelect');
    const srcSel = document.getElementById('branchSelect');
    if (!lqSel || !srcSel) return;
    srcSel.value = lqSel.value;
    onBranchChange();
}

function syncLiquidEmployeeBranchSelect() {
    const wrap = document.getElementById('liquidEmployeeBranchWrap');
    const label = document.getElementById('liquidEmployeeBranchLabel');
    const list = document.getElementById('liquidEmployeeBranchOptions');
    const srcSel = document.getElementById('branchSelect');
    if (!wrap || !label || !list || !srcSel) return;

    const options = Array.from(srcSel.options)
        .filter(o => o.value !== '');
    wrap.hidden = options.length <= 1;
    if (wrap.hidden) return;

    const current = srcSel.value || options[0]?.value || '';
    label.textContent = options.find(o => o.value === current)?.textContent || 'Filiale';
    list.innerHTML = options.map(o => {
        const active = o.value === current;
        return `<button type="button" class="liquid-employee-branch-option${active ? ' active' : ''}" role="option" aria-selected="${active}" onclick="selectLiquidEmployeeBranch('${o.value}')">
            <span>${active ? '✓' : ''}</span>
            <span>${liquidEscapeHtml(o.textContent || '')}</span>
        </button>`;
    }).join('');
}

function toggleLiquidEmployeeBranchMenu(event) {
    event?.stopPropagation();
    const wrap = document.getElementById('liquidEmployeeBranchWrap');
    const btn = wrap?.querySelector('.liquid-employee-branch-btn');
    const opts = document.getElementById('liquidEmployeeBranchOptions');
    if (!wrap || !opts || !btn) return;
    const open = !opts.classList.contains('open');
    if (!open) { closeLiquidEmployeeBranchMenu(); return; }
    // Das Dropdown an <body> hängen, damit es nicht vom backdrop-filter +
    // overflow der Glas-Nav als Containing-Block eingefangen und abgeschnitten
    // wird. So stimmen die fixed-Koordinaten aus getBoundingClientRect wieder.
    if (opts.parentElement !== document.body) document.body.appendChild(opts);
    const r = btn.getBoundingClientRect();
    opts.style.left = `${Math.max(8, r.left)}px`;
    opts.style.top = `${r.bottom + 6}px`;
    opts.classList.add('open');
    wrap.classList.add('open');
    btn.setAttribute('aria-expanded', 'true');
}

function closeLiquidEmployeeBranchMenu() {
    const wrap = document.getElementById('liquidEmployeeBranchWrap');
    const btn = wrap?.querySelector('.liquid-employee-branch-btn');
    const opts = document.getElementById('liquidEmployeeBranchOptions');
    opts?.classList.remove('open');
    wrap?.classList.remove('open');
    if (btn) btn.setAttribute('aria-expanded', 'false');
}

function selectLiquidEmployeeBranch(value) {
    const srcSel = document.getElementById('branchSelect');
    if (!srcSel) return;
    srcSel.value = value;
    closeLiquidEmployeeBranchMenu();
    onBranchChange();
}

// Ersetzt ein natives <select> durch ein helles Custom-Dropdown (das native
// OS-Popup rendert im macOS-Dunkelmodus dunkel, auch mit color-scheme:light).
// Das native <select> bleibt als Datenquelle erhalten (versteckt); Auswahl
// setzt dessen value + feuert ein 'change'-Event → bestehende Handler greifen.
// Optionsänderungen (dynamisches Befüllen) werden via MutationObserver
// nachgezogen. Idempotent (mehrfach aufrufbar). (Walter 05.07.2026)
function lightSelect(sel) {
    if (!sel || sel.dataset.ls2) return;
    // Schon von liquid-select.js umgebaut → nicht nochmals wrappen
    // (sonst doppelte Dropdowns, Walter-Bug 18.07.2026).
    if (sel._lq || sel.closest('.lqsel-wrap')) return;
    sel.dataset.ls2 = '1';
    sel.style.display = 'none';
    const wrap = document.createElement('span');
    wrap.className = 'ls2';
    sel.parentNode.insertBefore(wrap, sel);
    wrap.appendChild(sel);
    const btn = document.createElement('button');
    btn.type = 'button'; btn.className = 'ls2-btn';
    btn.innerHTML = '<span></span><svg viewBox="0 0 24 24" aria-hidden="true"><path d="m7 10 5 5 5-5"/></svg>';
    const opts = document.createElement('div');
    opts.className = 'ls2-opts'; opts.setAttribute('role','listbox');
    wrap.appendChild(btn); wrap.appendChild(opts);
    const esc = (t) => String(t).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    function render() {
        const cur = sel.options[sel.selectedIndex];
        btn.firstChild.textContent = cur ? cur.textContent : '';
        opts.innerHTML = Array.from(sel.options).map((o,i) =>
            `<button type="button" class="ls2-opt${i===sel.selectedIndex?' active':''}" role="option" data-i="${i}"><span class="ls2-chk">${i===sel.selectedIndex?'✓':''}</span><span>${esc(o.textContent||'')}</span></button>`
        ).join('');
    }
    btn.addEventListener('click', (e) => {
        e.stopPropagation();
        const willOpen = !wrap.classList.contains('open');
        document.querySelectorAll('.ls2.open').forEach(w => w.classList.remove('open'));
        wrap.classList.toggle('open', willOpen);
        if (willOpen) {
            render();
            const r = btn.getBoundingClientRect();
            opts.style.top = (r.bottom + 4) + 'px';
            opts.style.left = r.left + 'px';
            opts.style.minWidth = r.width + 'px';
        }
    });
    opts.addEventListener('click', (e) => {
        const b = e.target.closest('.ls2-opt'); if (!b) return;
        sel.selectedIndex = parseInt(b.dataset.i, 10);
        sel.dispatchEvent(new Event('change', { bubbles: true }));
        render();
        wrap.classList.remove('open');
    });
    new MutationObserver(render).observe(sel, { childList: true });
    render();
}
document.addEventListener('click', () => document.querySelectorAll('.ls2.open').forEach(w => w.classList.remove('open')));

// ── Sidebar-Filialauswahl als helles Glas-Dropdown (Walter 04.07.2026) ──
// Spiegelt die Optionen des versteckten #branchSelect; Auswahl setzt dessen
// value + ruft onBranchChange (wie das native Select vorher).
function syncSidebarBranchMenu() {
    const src   = document.getElementById('branchSelect');
    const label = document.getElementById('sbBranchLabel');
    const list  = document.getElementById('sbBranchOptions');
    if (!src || !label || !list) return;
    label.textContent = src.options[src.selectedIndex]?.textContent || 'Filiale';
    list.innerHTML = Array.from(src.options).map(o => {
        const active = o.value === src.value;
        return `<button type="button" class="sb-branch-option${active ? ' active' : ''}" role="option" aria-selected="${active}" onclick="selectSidebarBranch('${o.value}')">
            <span class="sb-branch-check">${active ? '✓' : ''}</span>
            <span>${liquidEscapeHtml(o.textContent || '')}</span>
        </button>`;
    }).join('');
}
function toggleSidebarBranchMenu(event) {
    event?.stopPropagation();
    const menu = document.getElementById('sbBranchMenu');
    if (!menu) return;
    const open = !menu.classList.contains('open');
    menu.classList.toggle('open', open);
    const btn = menu.querySelector('.sb-branch-btn');
    btn?.setAttribute('aria-expanded', open ? 'true' : 'false');
    // Fixed-positioniertes Dropdown unter dem Button platzieren (die Sidebar
    // clippt sonst horizontal wegen overflow-y:auto).
    if (open && btn) {
        const opts = document.getElementById('sbBranchOptions');
        const r = btn.getBoundingClientRect();
        opts.style.top  = (r.bottom + 6) + 'px';
        opts.style.left = r.left + 'px';
    }
}
function closeSidebarBranchMenu() {
    const menu = document.getElementById('sbBranchMenu');
    menu?.classList.remove('open');
    menu?.querySelector('.sb-branch-btn')?.setAttribute('aria-expanded', 'false');
}
function selectSidebarBranch(value) {
    const src = document.getElementById('branchSelect');
    if (!src) return;
    src.value = value;
    closeSidebarBranchMenu();
    onBranchChange();
    syncSidebarBranchMenu();
}

function syncLiquidBranchMenu() {
    const lqSel = document.getElementById('liquidBranchSelect');
    const label = document.getElementById('liquidBranchLabel');
    const list = document.getElementById('liquidBranchOptions');
    if (!lqSel || !label || !list) return;
    const selectedText = lqSel.options[lqSel.selectedIndex]?.textContent || 'Filiale';
    label.textContent = selectedText;
    list.innerHTML = Array.from(lqSel.options).map(o => {
        const active = o.value === lqSel.value;
        return `<button type="button" class="liquid-branch-option${active ? ' active' : ''}" role="option" aria-selected="${active}" onclick="selectLiquidBranch('${o.value}')">
            <span class="liquid-branch-check">${active ? '✓' : ''}</span>
            <span>${liquidEscapeHtml(o.textContent || '')}</span>
        </button>`;
    }).join('');
}

function liquidEscapeHtml(text) {
    return String(text).replace(/[&<>"']/g, ch => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[ch]));
}

function toggleLiquidBranchMenu(event) {
    event?.stopPropagation();
    const menu = document.getElementById('liquidBranchMenu');
    const btn = menu?.querySelector('.liquid-branch-btn');
    if (!menu) return;
    const open = !menu.classList.contains('open');
    menu.classList.toggle('open', open);
    if (btn) btn.setAttribute('aria-expanded', open ? 'true' : 'false');
}

function closeLiquidBranchMenu() {
    const menu = document.getElementById('liquidBranchMenu');
    const btn = menu?.querySelector('.liquid-branch-btn');
    menu?.classList.remove('open');
    if (btn) btn.setAttribute('aria-expanded', 'false');
}

function selectLiquidBranch(value) {
    const lqSel = document.getElementById('liquidBranchSelect');
    if (!lqSel) return;
    lqSel.value = value;
    closeLiquidBranchMenu();
    onLiquidBranchChange();
}

document.addEventListener('click', (event) => {
    if (!event.target.closest?.('#liquidBranchMenu')) closeLiquidBranchMenu();
    if (!event.target.closest?.('#liquidEmployeeBranchWrap')) closeLiquidEmployeeBranchMenu();
    if (!event.target.closest?.('#sbBranchMenu')) closeSidebarBranchMenu();
});

function syncLiquidDashboardCards() {
    const role = currentUser?.role || '';
    document.querySelectorAll('.liquid-module-card[data-liquid-roles], .liquid-employee-nav-btn[data-liquid-roles], .liquid-role-el[data-liquid-roles]').forEach(card => {
        const roles = (card.dataset.liquidRoles || '').split(',').map(r => r.trim()).filter(Boolean);
        card.hidden = roles.length > 0 && !roles.includes(role);
    });
}

function openLiquidDocumentsCard() {
    showPage('mitarbeiter');
    setTimeout(() => {
        if (typeof switchEmpTab === 'function') switchEmpTab('dokumente');
    }, 0);
}

// ── Datenaufbewahrung Stempelzeiten (Walter-Vorgabe 21.06.2026) ──────────
async function loadRetentionYears() {
    const inp = document.getElementById('retentionYearsInput');
    if (!inp) return;
    try {
        const res = await fetch('/api/app-settings/time-entry-retention', { headers: ah() });
        if (res.ok) { const d = await res.json(); inp.value = d.years; inp.min = d.min || 5; }
    } catch { /* still */ }
}
async function saveRetentionYears() {
    const inp = document.getElementById('retentionYearsInput');
    const msg = document.getElementById('retentionYearsMsg');
    if (!inp) return;
    const years = parseInt(inp.value, 10);
    if (isNaN(years) || years < 5) {
        if (msg) { msg.textContent = 'Minimum 5 Jahre.'; msg.style.color = '#dc2626'; }
        return;
    }
    try {
        const res = await fetch('/api/app-settings/time-entry-retention', {
            method: 'PUT', headers: ah(), body: JSON.stringify({ years })
        });
        if (res.ok) {
            if (msg) { msg.textContent = '✓ Gespeichert'; msg.style.color = '#16a34a'; }
        } else {
            let m = 'Fehler beim Speichern.';
            try { const j = await res.json(); if (j?.message) m = j.message; } catch {}
            if (msg) { msg.textContent = m; msg.style.color = '#dc2626'; }
        }
    } catch (e) {
        if (msg) { msg.textContent = 'Verbindungsfehler.'; msg.style.color = '#dc2626'; }
    }
}

// Walter-Vorgabe 13.06.2026: globaler 401-Interceptor. Wenn ein API-Call
// 401 zurückgibt (Token abgelaufen), Auto-Logout + Hinweis. Schützt vor
// dem Bug, dass nach Token-Ablauf jeder API-Call mit einem hässlichen
// JSON-Parse-Error fehlschlägt. Login-Endpoint ist ausgenommen — dort
// ist 401 ein legitimer „falsches Passwort"-Status.
(function installAuth401Interceptor() {
    if (window._auth401Installed) return;
    window._auth401Installed = true;
    const origFetch = window.fetch.bind(window);
    let alerting = false;   // Re-Entrance-Schutz für parallele 401-Responses

    window.fetch = async function(input, init) {
        const res = await origFetch(input, init);
        try {
            if (res.status !== 401) return res;
            // Login-Endpoint darf 401 melden (falsche Anmeldedaten) ohne Reload.
            const url = (typeof input === 'string') ? input
                       : (input && input.url) ? input.url : '';
            if (url.includes('/api/auth/login')) return res;
            // Nur wenn der Browser überhaupt eingeloggt war.
            if (!authToken) return res;
            if (alerting) return res;
            alerting = true;
            authToken = null;
            try { localStorage.removeItem('hrToken'); } catch {}
            alert('Deine Sitzung ist abgelaufen. Bitte melde dich erneut an.');
            location.reload();
        } catch { /* swallow */ }
        return res;
    };
})();

// Öffnet den MA-Importer (import.html) in einem eingebetteten iframe innerhalb
// der App, statt einen neuen Tab zu öffnen. Walter-Wunsch: gleiches Verhalten
// wie Stempelzeiten — alles bleibt in derselben Page, kein Kontextverlust.
// Token + branchId werden via URL-Param weitergegeben damit import.html den
// eigenen Login-Flow überspringen kann.
// mode='csv' (Walter 12.07.2026): CSV-Fallback-Modus — für neue MA, deren
// easy@work-Datensatz in einem FREMDEN Restaurant gesperrt ist (die API
// sieht sie nicht). Der Importer zeigt dann die Upload-Zone statt der
// easy@work-API-Liste.
function openImportTool(mode) {
    if (!authToken) { alert('Bitte zuerst anmelden.'); return; }
    if (!fixedCompanyProfileId) {
        alert('Bitte zuerst eine Filiale auswählen, bevor du den Import startest.');
        return;
    }
    const branch = allBranches.find(b => b.id === fixedCompanyProfileId);
    const branchName = branch ? `${branch.restaurantCode||''} – ${branch.branchName||branch.companyName||''}` : '';
    const restaurantCode = branch?.restaurantCode || '';
    const themeParam = (currentUser?.theme === 'dark') ? '&theme=dark' : '';
    // Cache-Buster (&v=Timestamp): import.html wird im iframe gerne vom Browser
    // gecached — deployte Frontend-Änderungen am Importer würden sonst nicht
    // greifen. Mit frischem Timestamp lädt der Browser bei jedem Öffnen neu.
    const url = 'import.html?token=' + encodeURIComponent(authToken)
              + '&branchId=' + fixedCompanyProfileId
              + '&branchName=' + encodeURIComponent(branchName)
              + themeParam
              + '&storeNumber=' + encodeURIComponent(restaurantCode)
              + '&v=' + Date.now()
              + (mode === 'csv' ? '&csv=1' : '')
              + '&embedded=1';
    // iframe füllen + Page sichtbar schalten. iframe-src wird bei jedem
    // Öffnen neu gesetzt, damit der Importer auf die aktuelle Filiale +
    // Token resettet ist (auch nach Filialwechsel mitten im Workflow).
    const frame = document.getElementById('importMaFrame');
    if (frame) frame.src = url;
    showPage('import-mitarbeiter');
}

// ══════════════════════════════════════════════
// STARTUP
// ══════════════════════════════════════════════
async function init() {
    const ok = await checkAuth();
    if (ok) {
        // MA-Postfach-Accounts haben eine eigene Mobile-Seite — niemals die
        // Backoffice-App zeigen, auch nicht beim Auto-Re-Login via Token.
        if (currentUser?.role === 'employee') {
            window.location.href = 'postfach.html';
            return;
        }
        startApp();
    }
}

async function startApp() {
    document.getElementById('loginScreen').style.display = 'none';
    document.getElementById('app').style.display = 'block';
    updateDashboardShellState('dashboard');

    document.getElementById('userName').textContent = currentUser.username;
    document.getElementById('userRoleBadge').textContent = currentUser.isSuperAdmin ? 'Super-Admin' : roleName(currentUser.role);
    document.getElementById('userAvatar').textContent = (currentUser.username || 'U')[0].toUpperCase();
    syncLiquidDashboardChrome();

    // Testmodus-Hinweisbalken zeigen, falls gerade impersoniert wird.
    if (typeof renderImpersonationBanner === 'function') renderImpersonationBanner();

    // Session-/Logout-Wächter starten (Walter-Vorgabe 21.06.2026).
    if (window.SessionGuard) window.SessionGuard.start();

    // Akonto-Workflow Sidebar-Badge: 60-Sekunden-Polling, sobald der User
    // eingeloggt ist (Sidebar-„Lohn"-Eintrag bekommt den Counter).
    if (typeof akWfStartBadgePolling === 'function') akWfStartBadgePolling();

    // Theme aus User-Profil anwenden (light = Default, dark = invertiert)
    applyTheme(currentUser.theme || 'light');

    // i18n initialisieren mit der bevorzugten Sprache aus dem User-Profil.
    // Sidebar + Top-Bar-Toggle werden sofort übersetzt; Module die JS-
    // generierte Strings rendern (Dashboard, Posteingang etc.) registrieren
    // sich via i18n.onChange() für ein Re-Render bei Sprachwechsel.
    if (window.i18n) {
        window.i18n.init(currentUser.preferredLanguage || 'de');
        // Theme-Toggle-Label mit der jetzt geladenen Sprache synchronisieren
        // (applyTheme lief oben evtl. noch mit Default-Sprache 'de').
        applyTheme(currentUser.theme || 'light');
        window.i18n.onChange(() => {
            // Theme-Toggle-Label bei Sprachwechsel neu setzen.
            applyTheme(currentUser?.theme || 'light');
            // Dashboard neu zeichnen wenn aktiv (Kategorien + Severity-Karten
            // sind JS-generiert, kein data-i18n-Attribut).
            if (typeof loadDashboard === 'function'
                && document.getElementById('page-dashboard')?.classList.contains('active')) {
                loadDashboard();
            }
            // MA-Detail neu zeichnen wenn aktiv — Sub-Tabs/Section-Titel/Field-
            // Labels sind alle JS-generiert via _t().
            if (typeof renderEmployeeDetail === 'function'
                && typeof selectedEmployee !== 'undefined' && selectedEmployee
                && document.getElementById('page-mitarbeiter')?.classList.contains('active')) {
                renderEmployeeDetail(selectedEmployee);
            }
            // Verträge-Page neu zeichnen wenn aktiv — Liste + Detail-Card sind
            // JS-generiert via _t().
            if (document.getElementById('page-vertraege')?.classList.contains('active')) {
                if (typeof renderVtList === 'function' && typeof allVtEmployees !== 'undefined' && allVtEmployees) {
                    renderVtList(allVtEmployees);
                }
                if (typeof renderVtDetail === 'function' && typeof selectedVtEmployee !== 'undefined' && selectedVtEmployee) {
                    renderVtDetail(selectedVtEmployee);
                }
            }
            // Lohnlauf-Page neu laden wenn aktiv — Status-Cockpit, Audit-Log
            // und alle Buttons werden bei jedem Switch via JS gerendert.
            if (typeof llLoadStatus === 'function'
                && document.getElementById('page-lohnlauf')?.classList.contains('active')) {
                llLoadStatus();
            }
            // QST-Anmeldung-Page: MA-Liste + Filial-Info werden bei Init
            // gerendert; bei Sprachwechsel reicht ein erneutes Init.
            if (typeof qstaInit === 'function'
                && document.getElementById('page-qst-anmeldung')?.classList.contains('active')) {
                qstaInit();
            }
            // RAV-Zwischenverdienst
            if (typeof zviInit === 'function'
                && document.getElementById('page-zwischenverdienst')?.classList.contains('active')) {
                zviInit();
            }
            // BFS-LSE-Export: Branch-Info + Empty-Row neu zeichnen
            if (typeof lseUpdateBranchInfo === 'function'
                && document.getElementById('page-lse-export')?.classList.contains('active')) {
                lseUpdateBranchInfo();
                // Toggle-Label neu setzen
                const lbl = document.getElementById('lseAllBranchesLabel');
                if (lbl) {
                    lbl.textContent = (typeof _lseAllBranches !== 'undefined' && _lseAllBranches)
                        ? window.i18n.t('lse.dyn.toggleSingle')
                        : window.i18n.t('lse.btn.allBranches');
                }
            }
        });
    }

    // Bereichs-Sichtbarkeit nach Rolle steuern (Walter-Vorgabe 17.05.2026).
    const role        = currentUser.role;
    const isAdmin     = role === 'admin';
    // Buchhaltung = wie Superuser (HR-Team) + zusätzlich der Fibu-Bereich.
    const isHrTeam    = role === 'admin' || role === 'superuser' || role === 'buchhaltung';
    // GF (Rolle 'user') braucht das Lohn-Modul für den Akonto-Workflow:
    // er bestätigt pro MA das Akonto-Lohnblatt und schickt an HR. Innerhalb
    // des Moduls werden HR-Aktionen für ihn ausgeblendet — das ist
    // separat in akonto-workflow.js geregelt.
    const isMaPostfach = role === 'employee';
    const isBuchhaltung = role === 'buchhaltung';
    // Walter-Vorgabe 14.06.2026: Rolle 'lowuser' — eingeschränkter Benutzer.
    // Sieht nur Dashboard + Mitarbeiter + Verträge. Kein Lohnlauf, kein HR-
    // Bereich, keine Systemeinstellungen, kein Datenimport.
    const isLowUser    = role === 'lowuser';
    // Lohn-Modul: admin + superuser + GF (user) + Buchhaltung. MA + lowuser NICHT.
    const canLohn      = (isAdmin || role === 'superuser' || role === 'user' || isBuchhaltung) && !isLowUser;

    // Übersicht + Personal: für alle ausser MA-Postfach.
    document.querySelectorAll('.overview-section, .people-section').forEach(el => {
        el.style.display = isMaPostfach ? 'none' : 'block';
    });
    // Systemeinstellungen: nur admin
    document.querySelectorAll('.admin-only-section').forEach(el => {
        el.style.display = isAdmin ? 'block' : 'none';
    });
    // Einzelne admin-only Karten in Systemeinstellungen (Walter 27.05.2026)
    document.querySelectorAll('.admin-only-card').forEach(el => {
        el.style.display = isAdmin ? '' : 'none';
    });
    // Datenimport-Sektion: nur admin (Filial-Onboarding-Tools)
    document.querySelectorAll('.import-section').forEach(el => {
        el.style.display = isAdmin ? 'block' : 'none';
    });
    // Finanzen/Lohn: admin + superuser + GF + Buchhaltung
    document.querySelectorAll('.finance-section').forEach(el => {
        el.style.display = canLohn ? 'block' : 'none';
    });
    // HR-Bereich: admin + superuser + Buchhaltung (= isHrTeam) — GF nicht
    document.querySelectorAll('.hr-section').forEach(el => {
        el.style.display = isHrTeam ? 'block' : 'none';
    });
    // Buchhaltung-Bereich (Fibu): zusätzlich nur für Rolle buchhaltung + admin
    document.querySelectorAll('.buchhaltung-section').forEach(el => {
        el.style.display = (isAdmin || isBuchhaltung) ? 'block' : 'none';
    });

    await loadAllBranches();
    populateBranchSelector();
    loadDashboard();
    buildContractPage();

    // Sichtbare Bereiche pro Benutzer ANWENDEN — als Letztes, damit es die
    // Rollen-Sichtbarkeit (Sektionen + Dashboard-Kacheln) überschreibt.
    applyAreaVisibility();
}

// Sichtbare-Bereiche-Filter (Walter 28.06.2026): Wenn der User eine eigene
// Bereichs-Auswahl hat (currentUser.allowedAreas = Array), zeigt das Menü +
// die Dashboard-Kacheln GENAU diese 8-Bereiche-Auswahl. „dashboard" ist immer
// sichtbar. NULL/kein Array = nicht eingreifen (alte Rollen-Sichtbarkeit gilt).
function applyAreaVisibility() {
    const aa = currentUser?.allowedAreas;
    if (!Array.isArray(aa)) return;
    const allowed = new Set(['dashboard', ...aa]);
    const ok = (area) => area === 'dashboard' || area === 'todos' || allowed.has(area);
    // Globale Sidebar: jede nav-section enthält genau einen nav-item (data-page).
    document.querySelectorAll('.sidebar .nav-section').forEach(sec => {
        const item = sec.querySelector('.nav-item[data-page]');
        if (!item) return;
        sec.style.display = ok(item.getAttribute('data-page')) ? '' : 'none';
    });
    // Dashboard-Kacheln + Mitarbeiter-Nav-Buttons (onclick="showPage('X')").
    document.querySelectorAll('.liquid-module-card, .liquid-employee-nav-btn').forEach(el => {
        const m = (el.getAttribute('onclick') || '').match(/showPage\('([^']+)'\)/);
        if (m) el.hidden = !ok(m[1]);
    });
}

function roleName(r) {
    if (r === 'admin')       return 'Administrator';
    if (r === 'superuser')   return 'Superuser';
    if (r === 'buchhaltung') return 'Buchhaltung';
    if (r === 'lowuser')     return 'Eingeschränkter Benutzer';
    return 'Benutzer';
}

// ══════════════════════════════════════════════
// NAVIGATION
// ══════════════════════════════════════════════
// Mapping: Unterseiten → in Sidebar als "admin-hub" markieren, damit der
// Systemeinstellungen-Eintrag aktiv bleibt wenn man in einem Admin-Bereich ist.
const _adminSubPages = ['benutzer','filialen','sv-saetze','lohnpositionen','mindestloehne','kontoplan','warnungen',
                         'qst-tarife','fz-tarife','absenz-typen','behoerden','globale-daten','banken','nationen','swiss-locations','audit-log',
                         'perioden','dokumentstruktur','archiv-import','dvelop-import',
                         'permit-import','hr-review-import','qst-import','family-children-import','stammdaten-import','saldo-vortrag-import','saldo-vortrag-import-stunden','mirus-address-compare','smtp-settings','ecall','moment-texte','filial-onboarding','postfach-backfill',
                         'saldo-vortrag','dok-audit','pregnancy-rules','datenaufbewahrung','daten-fix','aerzte'];

// Walter-Vorgabe 28.05.2026: Zurueck-Button rechts oben im langSwitcher-
// Widget. Wird auf allen Admin-Sub-Pages eingeblendet, sonst versteckt.
// Auch alte Breadcrumb-Inserts (falls noch von einer aelteren Session
// vorhanden) werden hier wegsanitiert.
function applyAdminBreadcrumb(name) {
    // Alte Breadcrumb-Inserts aufraeumen (Defense in Depth — falls noch
    // welche im DOM stehen z.B. aus einer alten gecachten JS-Version).
    document.querySelectorAll('.admin-breadcrumb').forEach(el => el.remove());
    const btn = document.getElementById('backToAdminBtn');
    if (!btn) return;
    // Walter-Vorgabe 14.06.2026: Zurück-zu-Systemeinstellungen-Button NUR
    // für Admin. Nicht-Admins können d.velop-Import etc. via Direkt-Link
    // (z.B. vom Dokumente-Tab des MA aus) erreichen — der Zurück-Button
    // würde sie sonst in den Admin-Bereich ziehen, in den sie nicht gehören.
    const showAdminBack = _adminSubPages.includes(name) && currentUser?.role === 'admin';
    btn.style.display = showAdminBack ? 'inline-flex' : 'none';
}

function showPage(name) {
    currentPageName = name;
    updateDashboardShellState(name);
    // easy@work-Sync-Pill im langSwitcher: nur auf der Mitarbeiter-Seite
    // mit gewaehltem MA (Walter 17.07.2026; Flag aus employees.js).
    const lsSync = document.getElementById('lsEmpSyncBtn');
    if (lsSync) lsSync.style.display =
        (name === 'mitarbeiter' && window.selectedEmployeeId && window._lsEmpSyncAllowed) ? 'inline-flex' : 'none';
    document.querySelectorAll('.page').forEach(p => p.classList.remove('active'));
    document.querySelectorAll('.nav-item').forEach(n => n.classList.remove('active'));
    document.getElementById('page-' + name)?.classList.add('active');
    const navPage = _adminSubPages.includes(name) ? 'admin-hub' : name;
    document.querySelector(`[data-page="${navPage}"]`)?.classList.add('active');
    // Walter 28.05.2026: Breadcrumb „← Systemeinstellungen / <Page>" einfuegen
    applyAdminBreadcrumb(name);
    // Dashboard frisch laden bei jedem Aufruf — sonst hängt eine veraltete
    // Liste fest, z.B. nach manuellem Setzen von Aktiv/Inaktiv im MA-Detail
    // (Walter-Bug 18.05.2026).
    if (name === 'dashboard') loadDashboard();
    if (name === 'todos' && typeof renderTodosPage === 'function') renderTodosPage();
    if (name === 'fibu') fibuInit();
    if (name === 'dok-protokoll' && typeof dpInit === 'function') dpInit();
    if (name === 'benutzer') loadUsers();
    if (name === 'datenaufbewahrung' && typeof loadRetentionYears === 'function') loadRetentionYears();
    if (name === 'daten-fix' && typeof dfInit === 'function') dfInit();
    if (name === 'filialen') loadFilialen();
    if (name === 'vertraege') loadVtList();
    if (name === 'qst-tarife') loadQstTarifeStatus();
    if (name === 'fz-tarife') fzLoad();
    if (name === 'mitarbeiter') loadMitarbeiterList();
    if (name === 'aerzte' && typeof aerzteInit === 'function') aerzteInit();
    if (name === 'lohn') {
        // initLohnPage ist async (befüllt Periode-Selects). Modus wird DANACH
        // automatisch je nach Status der Periode gewählt (Walter 16.05.2026 /
        // präzisiert 03.08.2026): Definitiv schon provisorisch/abgeschlossen
        // oder Akonto AUSBEZAHLT/UEBERSPRUNGEN → Definitiv; sonst Akonto.
        // _autoSelectLohnMode ruft setLohnMode mit dem richtigen Modus auf;
        // beides triggert weiter den Auto-Select des ersten MA (in akWfRefresh
        // bzw. loadLohnList).
        const _setMode = () => {
            if (typeof _autoSelectLohnMode === 'function') _autoSelectLohnMode();
            else if (typeof setLohnMode === 'function') setLohnMode(_akWfMode || 'definitiv');
        };
        const p = initLohnPage();
        if (p && typeof p.then === 'function') p.then(_setMode).catch(_setMode); else _setMode();
    }
    if (name === 'absenz-typen') loadAbsenzTypen();
    if (name === 'behoerden')    loadBehoerden();
    if (name === 'lohn-abtretungen' && typeof laListInit === 'function') laListInit();
    if (name === 'dokumentstruktur') loadDokumentStruktur();
    if (name === 'dvelop-import') { if (typeof dvelopResetUi === 'function') dvelopResetUi(); dvelopLoadEmployees(); if (typeof dvApiLoadSettings === 'function') dvApiLoadSettings(); }
    if (name === 'permit-import') permitImportInit();
    if (name === 'hr-review-import') hrrImportInit();
    if (name === 'qst-import')   qstImportInit();
    if (name === 'stammdaten-import') stImportInit();
    if (name === 'saldo-vortrag-import') svImpInit();
    if (name === 'saldo-vortrag-import-stunden') svhImpInit();
    if (name === 'mirus-address-compare') macInit();
    if (name === 'roster-absence-import') rosterImportInit();
    if (name === 'absence-report') arInit();
    if (name === 'akonto-lauf')    akInit();
    if (name === 'banken')       loadBanks();
    if (name === 'nationen')     natInit();
    if (name === 'swiss-locations') locInit();
    if (name === 'pregnancy-rules')  prInit();
    if (name === 'sv-saetze') loadSvSaetze();
    if (name === 'lohnpositionen') loadLohnpositionen();
    if (name === 'mindestloehne') mwInit();
    if (name === 'kontoplan') kpInit();
    if (name === 'warnungen') wcInit();
    if (name === 'easyatwork') eawInit();
    if (name === 'audit-log') alInit();
    if (name === 'posteingang') pbInit();
    else pbStopAutoRefresh();
    if (name === 'moments') momInit();
    if (name === 'moment-texte' && typeof momMgmtLoad === 'function') momMgmtLoad();
    if (name === 'qst-anmeldung') qstaInit();
    if (name === 'lohnausweis') laInit();
    if (name === 'kuendigung') kuendigungInit();
    if (name === 'aufforderung-arbeit') aufforderungArbeitInit();
    if (name === 'zwischenverdienst') zviInit();
    if (name === 'kontrolle') kontrolleInit();
    if (name === 'saldo-vortrag') svInit();
    if (name === 'lohnlauf')      llInit();
    if (name === 'perioden') initPeriodenPage();
    if (name === 'sollstunden') sollInit();
    if (name === 'ferien') ferienInit();
    if (name === 'alter-report') alterInit();
    if (name === 'fluktuation-report' && typeof flukInit === 'function') flukInit();
    if (name === 'exit-survey-report' && typeof esInit === 'function') esInit();
    if (name === 'absenz-kalender') akalInit();
    if (name === 'smtp-settings') smtpLoad();
    if (name === 'ecall') ecallLoad();
    if (name === 'lse-export')   lseInit();

    // fixhead-Seiten: Versatz für sticky Spaltentitel messen (Walter 22.07.2026).
    fixheadSyncStickyOffset();
    setTimeout(fixheadSyncStickyOffset, 400);   // nach async Renderern nochmal
}

// Misst auf aktiven fixhead-Seiten die Höhe einer mitklebenden Filter-Toolbar
// (.page-body > .sticky-section-head) und setzt sie als CSS-Var --ssh — die
// Tabellen-Kopfzeilen (thead th, sticky) starten dann exakt darunter statt
// hinter der Toolbar zu verschwinden. Ohne Toolbar bleibt --ssh 0.
function fixheadSyncStickyOffset() {
    document.querySelectorAll('.page.fixhead.active .page-body').forEach(b => {
        const t = b.querySelector(':scope > .sticky-section-head');
        b.style.setProperty('--ssh', t ? (t.offsetHeight + 'px') : '0px');
    });
}
window.addEventListener('resize', () => {
    clearTimeout(window._fixheadRsz);
    window._fixheadRsz = setTimeout(fixheadSyncStickyOffset, 150);
});

// PDF-Stempelzeiten-Import entfernt (Walter-Vorgabe 19.06.2026):
// Stempelzeiten kommen ausschliesslich über die easy@work-API. Die früheren
// Helfer (populateStzBranchSelect / onStzBranchChange / buildStzBranchMismatchWarning)
// + die Import-Seite sind ersatzlos entfernt.

// ══════════════════════════════════════════════
// BRANCHES
// ══════════════════════════════════════════════
async function loadAllBranches() {
    try {
        const res = await fetch('/api/companyprofiles', { headers: ah() });
        allBranches = res.ok ? await res.json() : [];
    } catch { allBranches = []; }
}

function populateBranchSelector() {
    const sel = document.getElementById('branchSelect');
    // Walter-Vorgabe 22.07.2026 (ersetzt die lowuser-Korrektur vom
    // 14.06.2026): «Alle Filialen» gibt es NUR fuer unbeschraenkte Rollen
    // (branches === 'all', d.h. admin/superuser). GF/lowuser/buchhaltung
    // sehen ausschliesslich ihre zugeteilten Filialen — der Server filtert
    // seit 22.07.2026 ohnehin hart (EmployeesController), das UI bietet die
    // Auswahl gar nicht mehr an.
    const unrestricted = currentUser.branches === 'all';
    sel.innerHTML = unrestricted ? '<option value="">Alle Filialen</option>' : '';
    const visible = (currentUser.branches === 'all'
        ? allBranches
        : allBranches.filter(b => currentUser.branches?.some(ub => ub.id === b.id))
    ).slice().sort((a, b) => parseInt(a.restaurantCode||'9999',10) - parseInt(b.restaurantCode||'9999',10));
    visible.forEach(b => {
        const o = document.createElement('option');
        o.value = b.id;
        o.textContent = `${b.restaurantCode ? b.restaurantCode + ' – ' : ''}${b.branchName || b.companyName}`;
        sel.appendChild(o);
    });
    // Auto-Selektion: bei einer oder mehreren sichtbaren Filialen wird die
    // erste (nach restaurantCode sortiert) vorgewählt, damit der User nicht
    // manuell klicken muss. Wenn gewünscht kann er oben immer noch auf
    // "Alle Filialen" zurückstellen (nicht bei lowuser, siehe oben).
    if (visible.length >= 1) { sel.value = visible[0].id; onBranchChange(); }
    syncLiquidDashboardChrome();
}

function onBranchChange() {
    currentBranchId = document.getElementById('branchSelect').value || null;
    fixedCompanyProfileId = currentBranchId ? parseInt(currentBranchId) : null;
    syncLiquidDashboardChrome();

    if (currentPageName === 'mitarbeiter') {
        loadMitarbeiterList();
    } else if (currentPageName === 'vertraege') {
        loadVtList();
    } else if (currentPageName === 'lohn') {
        lohnBranchChanged();
        // Akonto-Workflow-View (falls aktiv) bei Filial-Wechsel neu laden.
        if (typeof akWfOnPageOrBranchChange === 'function') akWfOnPageOrBranchChange();
    } else if (currentPageName === 'fibu') {
        // Fibu-Bereich folgt der globalen Filial-Auswahl.
        if (typeof fibuInit === 'function') fibuInit();
    } else if (currentPageName === 'dok-protokoll') {
        if (typeof dpInit === 'function') dpInit();
    } else if (currentPageName === 'kontrolle') {
        // Kontroll-Listen folgen der Sidebar-Filiale (Walter 22.07.2026).
        if (typeof kontrolleRefreshAll === 'function') kontrolleRefreshAll();
        else if (typeof kontrolleInit === 'function') kontrolleInit();
    } else if (currentPageName === 'lohnlauf') {
        // Lohnlauf folgt der globalen Filial-Auswahl — kein eigener Picker mehr.
        llSyncFromGlobalBranch();
    } else if (currentPageName === 'dvelop-import') {
        // Auswahl-Liste der MA neu filtern (auf neue Filiale)
        dvelopLoadEmployees();
    } else if (currentPageName === 'perioden') {
        // Lohnperioden-Page folgt dem globalen Selektor.
        const sel = document.getElementById('perBranchSelect');
        if (sel) {
            const target = currentBranchId ? String(currentBranchId) : '';
            if (sel.value !== target) {
                sel.value = target;
                perBranchChanged();
            }
        }
    } else if (currentPageName === 'lse-export') {
        lseUpdateBranchInfo();
    } else if (currentPageName === 'absenz-kalender') {
        // Kalender folgt der globalen Filial-Auswahl.
        if (typeof akalLoad === 'function') akalLoad();
    } else if (currentPageName === 'dashboard') {
        loadDashboard();
    } else if (currentPageName === 'todos') {
        // To-do-Seite folgt dem globalen Filial-Selektor — Alarme neu laden
        // (loadDashboard rendert die 3 Spalten neu, da page-todos aktiv ist).
        loadDashboard();
    } else if (currentPageName === 'qst-anmeldung') {
        // Filiale-Banner + MA-Liste neu rendern auf neue Filiale.
        qstaInit();
    } else if (currentPageName === 'lohnausweis') {
        // Lohnausweis-Page folgt dem globalen Filial-Selektor.
        laInit();
    } else if (currentPageName === 'moments') {
        // Moments folgt dem globalen Filial-Selektor (MA-Liste neu filtern).
        if (typeof momRenderMaSelect === 'function') momRenderMaSelect();
    } else if (currentPageName === 'kuendigung') {
        // Kündigung-Page folgt dem globalen Filial-Selektor (MA-Liste neu filtern).
        kuRenderEmpList();
    } else if (currentPageName === 'aufforderung-arbeit') {
        if (typeof aaRenderEmpList === 'function') aaRenderEmpList();
    } else if (currentPageName === 'zeugnis-doc') {
        // Dokument-Seite (Zeugnisse/Verwarnung) folgt dem Filial-Selektor.
        if (typeof zdRenderEmpList === 'function') zdRenderEmpList();
    } else if (currentPageName === 'zwischenverdienst') {
        // RAV-Zwischenverdienst folgt dem globalen Filial-Selektor.
        zviInit();
    } else if (currentPageName === 'lohn-abtretungen') {
        if (typeof laListInit === 'function') laListInit();
    } else if (currentPageName === 'saldo-vortrag') {
        // Saldi-Vortrag MA-Liste neu rendern (Filial-Filter).
        svInit();
    } else if (currentPageName === 'posteingang') {
        // Posteingang neu laden — Postfach pro Filiale.
        pbInit();
    } else if (currentPageName === 'stammdaten-import') {
        // Banner aktualisieren — File-Auswahl bleibt erhalten.
        if (typeof stImportRefreshBanner === 'function') stImportRefreshBanner();
    } else if (currentPageName === 'saldo-vortrag-import') {
        // Banner aktualisieren — Filial-Wechsel macht Analyse-Vorzustand obsolet.
        if (typeof svImpInit === 'function') svImpInit();
    } else if (currentPageName === 'saldo-vortrag-import-stunden') {
        if (typeof svhImpInit === 'function') svhImpInit();
    } else if (currentPageName === 'mirus-address-compare') {
        if (typeof macInit === 'function') macInit();
    } else if (currentPageName === 'roster-absence-import') {
        // Banner aktualisieren — File-Auswahl bleibt erhalten.
        if (typeof rosterImportRefreshBanner === 'function') rosterImportRefreshBanner();
    } else if (currentPageName === 'absence-report') {
        // In Branch-Mode: zurücksetzen (Filiale ist zentrale Dimension).
        // In Cross-Mode: nur Banner refreshen, Auswertung bleibt bestehen.
        if (typeof arOnBranchChange === 'function') arOnBranchChange();
        else if (typeof arInit === 'function') arInit();
    } else if (currentPageName === 'akonto-lauf') {
        // Bei Filial-Wechsel: Vorschau-Ergebnisse zurücksetzen (Filiale ist
        // die zentrale Dimension des Akonto-Laufs).
        if (typeof akOnBranchChange === 'function') akOnBranchChange();
    } else if (currentPageName === 'fluktuation-report') {
        // Fluktuation: bei Scope «Sidebar-Filiale» neu laden (Walter 26.07.2026).
        if (typeof flukLoad === 'function') flukLoad();
    } else if (currentPageName === 'exit-survey-report') {
        // Austritts-Feedback folgt der Sidebar-Filiale (Walter 26.07.2026).
        if (typeof esLoad === 'function') esLoad();
    }

    // Mirus-Digest-Vorschau folgt der Sidebar-Filiale (Walter 23.07.2026).
    if (typeof mirusDigestIsOpen === 'function' && mirusDigestIsOpen()
        && typeof loadMirusDigestPreview === 'function') {
        loadMirusDigestPreview();
    }
}

// ── liquidConfirm (Walter-Vorgabe 16.07.2026, gilt fuer ALLE kuenftigen
// Ja/Nein-Fragen): eigener Dialog im Liquid-Glass-Design statt des nativen
// browser-confirm (schwarz/blau). Promise<boolean>; ESC/Klick daneben = Nein.
// Verwendung: if (await liquidConfirm('Frage?')) { … }
function liquidConfirm(message, opts = {}) {
    return new Promise(resolve => {
        const old = document.getElementById('liquidConfirmModal');
        if (old) old.remove();
        const wrap = document.createElement('div');
        wrap.id = 'liquidConfirmModal';
        wrap.style.cssText = 'position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9800;display:flex;align-items:center;justify-content:center';
        const esc = t => String(t).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
        wrap.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:460px;width:92%;padding:22px 24px">
            <div style="font-size:15px;font-weight:800;color:#3f3f3f;margin-bottom:8px">${esc(opts.title || 'Frage')}</div>
            <div style="font-size:13.5px;color:#646464;line-height:1.5;white-space:pre-line">${esc(message)}</div>
            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:20px">
                <button id="lcNo" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">${esc(opts.noLabel || 'Nein')}</button>
                <button id="lcYes" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">${esc(opts.yesLabel || 'Ja')}</button>
            </div>
        </div>`;
        document.body.appendChild(wrap);
        const done = v => { wrap.remove(); document.removeEventListener('keydown', onKey); resolve(v); };
        const onKey = e => { if (e.key === 'Escape') done(false); };
        document.addEventListener('keydown', onKey);
        wrap.addEventListener('click', e => { if (e.target === wrap) done(false); });
        wrap.querySelector('#lcNo').onclick  = () => done(false);
        wrap.querySelector('#lcYes').onclick = () => done(true);
        wrap.querySelector('#lcYes').focus();
    });
}
window.liquidConfirm = liquidConfirm;

// Liquid-Ersatz für natives prompt(): Text-Eingabe im Liquid-Glass-Look.
// Promise<string|null> — null bei Abbrechen/ESC/Klick daneben, sonst der Text (getrimmt).
function liquidPrompt(message, opts = {}) {
    return new Promise(resolve => {
        const old = document.getElementById('liquidPromptModal');
        if (old) old.remove();
        const wrap = document.createElement('div');
        wrap.id = 'liquidPromptModal';
        wrap.style.cssText = 'position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9800;display:flex;align-items:center;justify-content:center';
        const esc = t => String(t).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
        wrap.innerHTML = `
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:460px;width:92%;padding:22px 24px">
            <div style="font-size:15px;font-weight:800;color:#3f3f3f;margin-bottom:8px">${esc(opts.title || 'Eingabe')}</div>
            <div style="font-size:13.5px;color:#646464;line-height:1.5;white-space:pre-line">${esc(message)}</div>
            <input id="lpInput" type="text" value="${esc(opts.value ?? '')}"
                   style="width:100%;box-sizing:border-box;margin-top:14px;background:rgba(255,255,255,0.58);border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 12px;font-size:13.5px;color:#3f3f3f;outline:none">
            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:20px">
                <button id="lpNo" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">${esc(opts.noLabel || 'Abbrechen')}</button>
                <button id="lpYes" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">${esc(opts.yesLabel || 'OK')}</button>
            </div>
        </div>`;
        document.body.appendChild(wrap);
        const input = wrap.querySelector('#lpInput');
        const done = v => { wrap.remove(); document.removeEventListener('keydown', onKey); resolve(v); };
        const ok = () => done(input.value.trim());
        const onKey = e => { if (e.key === 'Escape') done(null); };
        document.addEventListener('keydown', onKey);
        input.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); ok(); } });
        wrap.addEventListener('click', e => { if (e.target === wrap) done(null); });
        wrap.querySelector('#lpNo').onclick  = () => done(null);
        wrap.querySelector('#lpYes').onclick = ok;
        input.focus();
        input.select();
    });
}
window.liquidPrompt = liquidPrompt;
