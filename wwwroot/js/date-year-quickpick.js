// ══════════════════════════════════════════════════════════════════════
// date-year-quickpick.js — Kompaktes Jahres-/Monats-Datumsmenü
// ──────────────────────────────────────────────────────────────────────
// Walter 26.07.2026: wie natives Chrome-Jahresmenü, aber sofort sichtbar
// und mit Vorjahr (z.B. 2025) oberhalb des aktuellen Jahres (2026) —
// ohne Hochscrollen / ohne Extra-Menü öffnen.
//   • Akkordeon: Jahr-Zeilen, aktives Jahr mit Monatsraster
//   • Scroll-Start = Vorjahr oben
//   • Monat-Klick → Tage, Tag-Klick → übernehmen
// Leeres Feld startet bei HEUTE. Opt-out: data-yp="off"
// ══════════════════════════════════════════════════════════════════════
(function () {
    const MONATE = ['Jan.', 'Feb.', 'März', 'Apr.', 'Mai', 'Juni',
                    'Juli', 'Aug.', 'Sept.', 'Okt.', 'Nov.', 'Dez.'];
    const WOCHENTAGE = ['M', 'D', 'M', 'D', 'F', 'S', 'S'];

    let _panel = null;
    let _activeInput = null;
    let _view = 'yearmonth'; // yearmonth | days
    let _y = 0, _m = 0, _d = 0; // 0-based month

    function pad2(n) { return String(n).padStart(2, '0'); }

    function todayParts() {
        const t = new Date();
        return { y: t.getFullYear(), m: t.getMonth(), d: t.getDate() };
    }

    function parseIso(v) {
        if (v && /^\d{4}-\d{2}-\d{2}$/.test(v)) {
            return {
                y: +v.slice(0, 4),
                m: +v.slice(5, 7) - 1,
                d: +v.slice(8, 10),
            };
        }
        return todayParts();
    }

    function toIso(y, m, d) {
        return `${y}-${pad2(m + 1)}-${pad2(d)}`;
    }

    function yearRange() {
        const cur = new Date().getFullYear();
        const from = cur - 10;
        const to = cur + 4;
        const years = [];
        for (let y = from; y <= to; y++) years.push(y);
        return years;
    }

    function ensurePanel() {
        if (_panel) return _panel;
        _panel = document.createElement('div');
        _panel.id = 'ypDateMenu';
        _panel.className = 'yp-menu';
        _panel.setAttribute('role', 'dialog');
        document.body.appendChild(_panel);

        document.addEventListener('mousedown', (ev) => {
            if (!_panel || _panel.style.display === 'none') return;
            if (_panel.contains(ev.target)) return;
            if (_activeInput && (ev.target === _activeInput || _activeInput.contains(ev.target))) return;
            closeMenu();
        });
        document.addEventListener('keydown', (ev) => {
            if (ev.key === 'Escape') closeMenu();
        });
        window.addEventListener('resize', closeMenu);
        return _panel;
    }

    function closeMenu() {
        if (_panel) _panel.style.display = 'none';
        _activeInput = null;
    }

    function commit(y, m, d) {
        if (!_activeInput) return;
        const inp = _activeInput;
        inp.value = toIso(y, m, d);
        inp.dispatchEvent(new Event('input', { bubbles: true }));
        inp.dispatchEvent(new Event('change', { bubbles: true }));
        closeMenu();
    }

    function clearValue() {
        if (_activeInput) {
            _activeInput.value = '';
            _activeInput.dispatchEvent(new Event('input', { bubbles: true }));
            _activeInput.dispatchEvent(new Event('change', { bubbles: true }));
        }
        closeMenu();
    }

    /** Jahr oberhalb des geöffneten Jahres an den oberen Rand scrollen
        (bei 2026 → 2025 sichtbar darüber, ohne Hochscrollen). */
    function scrollPrevYearVisible(listEl) {
        if (!listEl) return;
        const open = listEl.querySelector('.yp-year-row.is-open');
        if (!open) return;
        let anchor = open.previousElementSibling;
        while (anchor && !anchor.classList.contains('yp-year-row')) {
            anchor = anchor.previousElementSibling;
        }
        const target = anchor || open;
        try {
            target.scrollIntoView({ block: 'start', inline: 'nearest' });
        } catch {
            listEl.scrollTop = Math.max(0, target.offsetTop - 2);
        }
    }

    function render() {
        const panel = ensurePanel();
        if (_view === 'days') {
            renderDays(panel);
        } else {
            renderYearMonth(panel);
        }
    }

    function renderYearMonth(panel) {
        const years = yearRange();
        const curY = new Date().getFullYear();
        const monthsHtml = MONATE.map((name, i) => {
            const cls = 'yp-month' + (i === _m ? ' is-active' : '');
            return `<button type="button" class="${cls}" data-m="${i}">${name}</button>`;
        }).join('');

        const rows = years.map(y => {
            const isOpen = y === _y;
            const isPrev = y === curY - 1;
            const cls = 'yp-year-row'
                + (isOpen ? ' is-open' : '')
                + (isPrev ? ' is-prev' : '')
                + (y === curY ? ' is-current' : '');
            let html = `<button type="button" class="${cls}" data-y="${y}">${y}</button>`;
            if (isOpen) {
                html += `<div class="yp-months" data-for="${y}">${monthsHtml}</div>`;
            }
            return html;
        }).join('');

        panel.innerHTML = `
            <div class="yp-menu-head">
                <span class="yp-menu-title">${MONATE[_m]} ${_y}</span>
                <button type="button" class="yp-menu-close" title="Schliessen" aria-label="Schliessen">✕</button>
            </div>
            <div class="yp-year-list">${rows}</div>
            <div class="yp-menu-foot">
                <button type="button" class="yp-link" data-act="clear">Löschen</button>
                <button type="button" class="yp-link" data-act="today">Heute</button>
            </div>`;

        panel.querySelector('.yp-menu-close').onclick = closeMenu;
        panel.querySelectorAll('.yp-year-row').forEach(b => {
            b.onclick = () => {
                _y = +b.dataset.y;
                render();
            };
        });
        panel.querySelectorAll('.yp-month').forEach(b => {
            b.onclick = () => {
                _m = +b.dataset.m;
                _view = 'days';
                render();
            };
        });
        panel.querySelector('[data-act="clear"]').onclick = clearValue;
        panel.querySelector('[data-act="today"]').onclick = () => {
            const t = todayParts();
            commit(t.y, t.m, t.d);
        };

        // Nach Layout: Vorjahr oben sichtbar (Walter)
        requestAnimationFrame(() => {
            scrollPrevYearVisible(panel.querySelector('.yp-year-list'));
        });
    }

    function renderDays(panel) {
        const first = new Date(_y, _m, 1);
        const start = (first.getDay() + 6) % 7;
        const daysInMonth = new Date(_y, _m + 1, 0).getDate();
        const today = todayParts();

        let cells = '';
        for (let i = 0; i < start; i++) cells += `<span class="yp-day is-empty"></span>`;
        for (let d = 1; d <= daysInMonth; d++) {
            const isSel = d === _d && true;
            const isToday = d === today.d && _m === today.m && _y === today.y;
            cells += `<button type="button" class="yp-day${isSel ? ' is-active' : ''}${isToday ? ' is-today' : ''}" data-d="${d}">${d}</button>`;
        }

        panel.innerHTML = `
            <div class="yp-menu-head">
                <button type="button" class="yp-back" data-act="back">← ${MONATE[_m]} ${_y}</button>
                <button type="button" class="yp-menu-close" title="Schliessen" aria-label="Schliessen">✕</button>
            </div>
            <div class="yp-dow">${WOCHENTAGE.map(w => `<span>${w}</span>`).join('')}</div>
            <div class="yp-days">${cells}</div>
            <div class="yp-menu-foot">
                <button type="button" class="yp-link" data-act="clear">Löschen</button>
                <button type="button" class="yp-link" data-act="today">Heute</button>
            </div>`;

        panel.querySelector('.yp-menu-close').onclick = closeMenu;
        panel.querySelector('[data-act="back"]').onclick = () => {
            _view = 'yearmonth';
            render();
        };
        panel.querySelectorAll('.yp-day[data-d]').forEach(b => {
            b.onclick = () => commit(_y, _m, +b.dataset.d);
        });
        panel.querySelector('[data-act="clear"]').onclick = clearValue;
        panel.querySelector('[data-act="today"]').onclick = () => {
            const t = todayParts();
            commit(t.y, t.m, t.d);
        };
    }

    function positionPanel(input) {
        const panel = ensurePanel();
        panel.style.display = 'block';
        panel.style.visibility = 'hidden';
        const r = input.getBoundingClientRect();
        const pw = panel.offsetWidth || 248;
        const ph = panel.offsetHeight || 300;
        let left = r.left;
        let top = r.bottom + 4;
        if (left + pw > window.innerWidth - 8) left = window.innerWidth - pw - 8;
        if (left < 8) left = 8;
        if (top + ph > window.innerHeight - 8) top = Math.max(8, r.top - ph - 4);
        panel.style.left = left + 'px';
        panel.style.top = top + 'px';
        panel.style.visibility = 'visible';
    }

    function openMenu(input) {
        const parts = parseIso(input.value);
        _y = parts.y;
        _m = parts.m;
        _d = parts.d;
        _view = 'yearmonth';
        _activeInput = input;
        if (!input.value) {
            const t = todayParts();
            _y = t.y; _m = t.m; _d = t.d;
        }
        render();
        positionPanel(input);
        // Nochmal nach Positionierung scrollen (Layout fertig)
        requestAnimationFrame(() => {
            scrollPrevYearVisible(_panel && _panel.querySelector('.yp-year-list'));
        });
    }

    function attach(input) {
        if (!input || input._ypAttached) return;
        if (input.getAttribute('data-yp') === 'off') return;
        if ((input.type || '').toLowerCase() !== 'date') return;
        input._ypAttached = true;

        const next = input.nextElementSibling;
        if (next && next.getAttribute && next.getAttribute('data-yp-row') === '1') next.remove();

        const open = (ev) => {
            ev.preventDefault();
            ev.stopPropagation();
            openMenu(input);
        };
        input.addEventListener('mousedown', open);
        input.addEventListener('click', open);
        input.addEventListener('keydown', (ev) => {
            if (ev.key === 'Enter' || ev.key === ' ' || ev.key === 'ArrowDown') open(ev);
        });
        try {
            Object.defineProperty(input, 'showPicker', {
                configurable: true,
                value: function () { openMenu(input); },
            });
        } catch { /* ignore */ }
    }

    function attachById(id) {
        const el = document.getElementById(id);
        if (el) attach(el);
    }

    function scan(root) {
        const scope = root && root.querySelectorAll ? root : document;
        if (scope.matches && scope.matches('input[type="date"]')) attach(scope);
        scope.querySelectorAll('input[type="date"]').forEach(el => attach(el));
    }

    function startAuto() {
        scan(document);
        if (window._ypObserver) return;
        const mo = new MutationObserver(muts => {
            for (const m of muts) {
                for (const n of m.addedNodes) {
                    if (n.nodeType !== 1) continue;
                    if (n.getAttribute && n.getAttribute('data-yp-row') === '1') {
                        n.remove();
                        continue;
                    }
                    if (n.matches && n.matches('input[type="date"]')) attach(n);
                    else if (n.querySelectorAll) scan(n);
                }
            }
        });
        mo.observe(document.documentElement, { childList: true, subtree: true });
        window._ypObserver = mo;
    }

    window.YearPick = { attach, attachById, scan, startAuto, openMenu, closeMenu };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', startAuto);
    } else {
        startAuto();
    }
})();
