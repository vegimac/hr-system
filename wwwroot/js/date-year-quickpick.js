// ══════════════════════════════════════════════════════════════════════
// date-year-quickpick.js — Scrollbares Jahres-/Monats-Datumsmenü
// ──────────────────────────────────────────────────────────────────────
// Walter 26.07.2026: Zurück zum scrollbaren Jahresmenü (nicht Tag-Kalender
// zuerst, keine Pill-Knöpfe). Aufbau wie früher:
//   • Jahresliste scrollbar (Vorjahr … +2 Jahre sichtbar, weiter scrollbar)
//   • Monatsraster
//   • danach Tage
// Leeres Feld startet bei HEUTE.
// Opt-out: data-yp="off"
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

    function yearRange() {
        const cur = new Date().getFullYear();
        // Scrollbar: etwas Vergangenheit + Vorjahr … +2 Jahre (Walter)
        const from = cur - 8;
        const to = cur + 2;
        const years = [];
        for (let y = from; y <= to; y++) years.push(y);
        return years;
    }

    function render() {
        const panel = ensurePanel();
        if (_view === 'days') {
            renderDays(panel);
        } else {
            renderYearMonth(panel);
        }
        // Aktives Jahr in der Liste sichtbar halten
        const active = panel.querySelector('.yp-year.is-active');
        if (active) {
            try { active.scrollIntoView({ block: 'center' }); } catch { /* ignore */ }
        }
    }

    function renderYearMonth(panel) {
        const years = yearRange();
        const curY = new Date().getFullYear();
        let yearsHtml = years.map(y => {
            const cls = 'yp-year'
                + (y === _y ? ' is-active' : '')
                + (y === curY - 1 ? ' is-prev' : '')
                + (y >= curY && y <= curY + 2 ? ' is-near' : '');
            return `<button type="button" class="${cls}" data-y="${y}">${y}</button>`;
        }).join('');

        let monthsHtml = MONATE.map((name, i) => {
            const cls = 'yp-month' + (i === _m && true ? ' is-active' : '');
            return `<button type="button" class="${cls}" data-m="${i}">${name}</button>`;
        }).join('');

        panel.innerHTML = `
            <div class="yp-menu-head">
                <span class="yp-menu-title">${MONATE[_m]} ${_y}</span>
                <button type="button" class="yp-menu-close" title="Schliessen">✕</button>
            </div>
            <div class="yp-menu-body">
                <div class="yp-years">${yearsHtml}</div>
                <div class="yp-months">${monthsHtml}</div>
            </div>
            <div class="yp-menu-foot">
                <button type="button" class="yp-link" data-act="clear">Löschen</button>
                <button type="button" class="yp-link" data-act="today">Heute</button>
                <button type="button" class="yp-primary" data-act="days">Tag wählen →</button>
            </div>`;

        panel.querySelector('.yp-menu-close').onclick = closeMenu;
        panel.querySelectorAll('.yp-year').forEach(b => {
            b.onclick = () => { _y = +b.dataset.y; render(); };
        });
        panel.querySelectorAll('.yp-month').forEach(b => {
            b.onclick = () => { _m = +b.dataset.m; _view = 'days'; render(); };
        });
        panel.querySelector('[data-act="clear"]').onclick = () => {
            if (_activeInput) {
                _activeInput.value = '';
                _activeInput.dispatchEvent(new Event('input', { bubbles: true }));
                _activeInput.dispatchEvent(new Event('change', { bubbles: true }));
            }
            closeMenu();
        };
        panel.querySelector('[data-act="today"]').onclick = () => {
            const t = todayParts();
            commit(t.y, t.m, t.d);
        };
        panel.querySelector('[data-act="days"]').onclick = () => {
            _view = 'days';
            render();
        };
    }

    function renderDays(panel) {
        const first = new Date(_y, _m, 1);
        // Montag = 0
        let start = (first.getDay() + 6) % 7;
        const daysInMonth = new Date(_y, _m + 1, 0).getDate();
        const today = todayParts();

        let cells = '';
        for (let i = 0; i < start; i++) cells += `<span class="yp-day is-empty"></span>`;
        for (let d = 1; d <= daysInMonth; d++) {
            const isSel = d === _d;
            const isToday = d === today.d && _m === today.m && _y === today.y;
            cells += `<button type="button" class="yp-day${isSel ? ' is-active' : ''}${isToday ? ' is-today' : ''}" data-d="${d}">${d}</button>`;
        }

        panel.innerHTML = `
            <div class="yp-menu-head">
                <button type="button" class="yp-back" data-act="back">← ${MONATE[_m]} ${_y}</button>
                <button type="button" class="yp-menu-close" title="Schliessen">✕</button>
            </div>
            <div class="yp-dow">${WOCHENTAGE.map(w => `<span>${w}</span>`).join('')}</div>
            <div class="yp-days">${cells}</div>
            <div class="yp-menu-foot">
                <button type="button" class="yp-link" data-act="clear">Löschen</button>
                <button type="button" class="yp-link" data-act="today">Heute</button>
            </div>`;

        panel.querySelector('.yp-menu-close').onclick = closeMenu;
        panel.querySelector('[data-act="back"]').onclick = () => { _view = 'yearmonth'; render(); };
        panel.querySelectorAll('.yp-day[data-d]').forEach(b => {
            b.onclick = () => commit(_y, _m, +b.dataset.d);
        });
        panel.querySelector('[data-act="clear"]').onclick = () => {
            if (_activeInput) {
                _activeInput.value = '';
                _activeInput.dispatchEvent(new Event('input', { bubbles: true }));
                _activeInput.dispatchEvent(new Event('change', { bubbles: true }));
            }
            closeMenu();
        };
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
        const pw = panel.offsetWidth || 280;
        const ph = panel.offsetHeight || 320;
        let left = r.left;
        let top = r.bottom + 6;
        if (left + pw > window.innerWidth - 8) left = window.innerWidth - pw - 8;
        if (left < 8) left = 8;
        if (top + ph > window.innerHeight - 8) top = Math.max(8, r.top - ph - 6);
        panel.style.left = left + 'px';
        panel.style.top = top + 'px';
        panel.style.visibility = 'visible';
    }

    function openMenu(input) {
        const parts = parseIso(input.value);
        _y = parts.y;
        _m = parts.m;
        _d = parts.d;
        _view = 'yearmonth'; // IMMER zuerst Jahres-/Monatsmenü (Walter)
        _activeInput = input;
        if (!input.value) {
            // Leer → heute vorschlagen (Wert noch nicht committen, nur Ansicht)
            const t = todayParts();
            _y = t.y; _m = t.m; _d = t.d;
        }
        render();
        positionPanel(input);
    }

    function attach(input) {
        if (!input || input._ypAttached) return;
        if (input.getAttribute('data-yp') === 'off') return;
        if ((input.type || '').toLowerCase() !== 'date') return;
        input._ypAttached = true;

        // Alte Pill-Reihe entfernen falls noch im DOM
        const next = input.nextElementSibling;
        if (next && next.getAttribute && next.getAttribute('data-yp-row') === '1') next.remove();

        // Native Picker unterdrücken → unser Jahresmenü
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
        // showPicker (Chrome) umbiegen
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
