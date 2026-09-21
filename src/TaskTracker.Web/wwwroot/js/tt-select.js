/**
 * tt-select — lightweight searchable dropdown (no dependencies)
 * Add data-searchable to any <select> to enhance it.
 * Uses computed styles from the original <select> so it matches exactly
 * without inheriting Tailwind class conflicts on a <button> element.
 */
(function () {
    'use strict';

    const MIN_FOR_SEARCH = 7;

    function build(select) {
        if (select._ttBuilt) return;
        select._ttBuilt = true;
        const isMulti = select.multiple;
        const placeholder = select.dataset.placeholder || '';

        // Capture the select's visual geometry BEFORE hiding it
        const cs      = window.getComputedStyle(select);
        const trig    = {
            border    : cs.border         || '1px solid #e2e8f0',
            radius    : cs.borderRadius   || '8px',
            fontSize  : cs.fontSize       || '14px',
            lineHeight: cs.lineHeight     || '1.5',
            padTop    : cs.paddingTop     || '6px',
            padBot    : cs.paddingBottom  || '6px',
            padL      : cs.paddingLeft    || '12px',
            padR      : cs.paddingRight   || '12px',
            // A native <select multiple> reports a tall multi-row listbox height; ignore it and let
            // padding define a single-line trigger. Single-selects keep their measured height.
            minH      : isMulti ? 'auto' : (cs.height || 'auto'),
            color     : cs.color          || '#1e293b',
            fontFamily: cs.fontFamily     || 'inherit',
        };

        // ── Wrapper ──────────────────────────────────────────────────────────
        const wrap = document.createElement('div');
        wrap.style.cssText = 'position:relative;display:block;box-sizing:border-box;';
        // Match the select's width behaviour
        if (select.style.width || select.classList.contains('w-full')) {
            wrap.style.width = '100%';
        }

        select.parentNode.insertBefore(wrap, select);
        wrap.appendChild(select);
        select.style.display = 'none';

        // ── Trigger button ───────────────────────────────────────────────────
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.style.cssText = [
            'display:flex',
            'align-items:center',
            'justify-content:space-between',
            'gap:6px',
            'width:100%',
            'box-sizing:border-box',
            'text-align:left',
            'background:#fff',
            `border:${trig.border}`,
            `border-radius:${trig.radius}`,
            `font-size:${trig.fontSize}`,
            `line-height:${trig.lineHeight}`,
            `padding:${trig.padTop} ${trig.padR} ${trig.padBot} ${trig.padL}`,
            `min-height:${trig.minH}`,
            `color:${trig.color}`,
            `font-family:${trig.fontFamily}`,
            'cursor:pointer',
            'outline:none',
            'transition:border-color .15s,box-shadow .15s',
            '-webkit-appearance:none',
            'appearance:none',
        ].join(';');

        btn.addEventListener('focus', () => {
            btn.style.borderColor = '#93c5fd';
            btn.style.boxShadow   = '0 0 0 3px rgba(147,197,253,.35)';
        });
        btn.addEventListener('blur', () => {
            btn.style.borderColor = '';
            btn.style.boxShadow   = '';
        });

        const labelSpan = document.createElement('span');
        labelSpan.style.cssText = 'flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';

        const chevron = document.createElement('span');
        chevron.style.cssText = 'flex-shrink:0;display:flex;align-items:center;transition:transform .15s;color:#9ca3af;';
        chevron.innerHTML = `<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M6 9l6 6 6-6"/></svg>`;

        btn.appendChild(labelSpan);
        btn.appendChild(chevron);
        wrap.appendChild(btn);

        // ── Panel ────────────────────────────────────────────────────────────
        const panel = document.createElement('div');
        panel.style.cssText = [
            'display:none',
            'position:absolute',
            'left:0',
            'z-index:9999',
            'background:#fff',
            'border:1px solid #e2e8f0',
            'border-radius:8px',
            'box-shadow:0 8px 24px rgba(0,0,0,.12)',
            'min-width:100%',
            'max-width:360px',
            'width:max-content',
            'box-sizing:border-box',
            'overflow:hidden',
        ].join(';');
        wrap.appendChild(panel);

        // Search input row
        const searchWrap = document.createElement('div');
        searchWrap.style.cssText = 'padding:8px;border-bottom:1px solid #f1f5f9;';

        const searchInput = document.createElement('input');
        searchInput.type = 'text';
        searchInput.placeholder = 'Search…';
        searchInput.style.cssText = [
            'display:block',
            'width:100%',
            'box-sizing:border-box',
            'padding:5px 10px',
            'border:1px solid #e2e8f0',
            'border-radius:6px',
            'font-size:12px',
            'font-family:inherit',
            'line-height:1.4',
            'color:#1e293b',
            'background:#f8fafc',
            'outline:none',
            '-webkit-appearance:none',
        ].join(';');
        searchInput.addEventListener('focus', () => { searchInput.style.borderColor = '#93c5fd'; searchInput.style.boxShadow = '0 0 0 2px rgba(147,197,253,.3)'; });
        searchInput.addEventListener('blur',  () => { searchInput.style.borderColor = '#e2e8f0'; searchInput.style.boxShadow = ''; });
        searchInput.addEventListener('input', () => renderList(searchInput.value));
        searchInput.addEventListener('keydown', e => {
            if (e.key === 'Escape') { closePanel(); btn.focus(); }
        });
        searchWrap.appendChild(searchInput);

        // Option list
        const list = document.createElement('ul');
        list.style.cssText = 'max-height:216px;overflow-y:auto;padding:4px 0;margin:0;list-style:none;';

        panel.appendChild(searchWrap);

        // Multi-select only: "Select all / Clear" bulk actions. "Select all" respects the current
        // search filter (selects just the visible options); "Clear" deselects everything.
        if (isMulti) {
            const bulkRow = document.createElement('div');
            bulkRow.style.cssText = 'display:flex;gap:6px;align-items:center;padding:6px 10px;border-bottom:1px solid #f1f5f9;';
            const mkBtn = (label, color) => {
                const b = document.createElement('button');
                b.type = 'button'; b.textContent = label;
                b.style.cssText = `font-size:11px;font-weight:600;color:${color};background:transparent;border:none;cursor:pointer;padding:2px 4px;border-radius:4px;`;
                b.addEventListener('mouseenter', () => b.style.background = '#f1f5f9');
                b.addEventListener('mouseleave', () => b.style.background = 'transparent');
                return b;
            };
            const selAllBtn = mkBtn('Select all', '#2563eb');
            const clearBtn  = mkBtn('Clear', '#64748b');
            selAllBtn.addEventListener('mousedown', e => {
                e.preventDefault();
                const q = (searchInput.value || '').toLowerCase().trim();
                opts().forEach(o => { if (o.value !== '' && (!q || o.text.toLowerCase().includes(q))) o.selected = true; });
                select.dispatchEvent(new Event('change', { bubbles: true }));
                refreshLabel(); renderList(searchInput.value);
            });
            clearBtn.addEventListener('mousedown', e => {
                e.preventDefault();
                opts().forEach(o => o.selected = false);
                select.dispatchEvent(new Event('change', { bubbles: true }));
                refreshLabel(); renderList(searchInput.value);
            });
            const sep = document.createElement('span'); sep.textContent = '·'; sep.style.color = '#cbd5e1';
            bulkRow.appendChild(selAllBtn); bulkRow.appendChild(sep); bulkRow.appendChild(clearBtn);
            panel.appendChild(bulkRow);
        }

        panel.appendChild(list);

        // ── Helpers ──────────────────────────────────────────────────────────
        let isOpen = false;

        const opts = () => Array.from(select.options);
        const selectedOpts = () => opts().filter(o => o.selected && o.value !== '');

        function selectedLabel() {
            if (isMulti) {
                const sel = selectedOpts();
                if (sel.length === 0) return placeholder || (opts()[0]?.text || '');
                if (sel.length === 1) return sel[0].text;
                return sel.length + ' selected';
            }
            const o = opts().find(o => o.value === select.value);
            return o ? o.text : (opts()[0]?.text || '');
        }

        function refreshLabel() {
            const hasVal = isMulti ? selectedOpts().length > 0 : !!select.value;
            labelSpan.textContent = selectedLabel();
            labelSpan.style.color = hasVal ? trig.color : '#94a3b8';
            const dis = select.disabled;
            btn.style.background = dis ? '#f8fafc' : '#fff';
            btn.style.opacity    = dis ? '0.6' : '1';
            btn.style.cursor     = dis ? 'not-allowed' : 'pointer';
            btn.disabled         = dis;
        }

        function renderList(q) {
            const query   = (q || '').toLowerCase().trim();
            const all     = opts();
            // Always offer search on multi-selects; single-selects only once the list is long enough.
            searchWrap.style.display = (isMulti || all.length >= MIN_FOR_SEARCH) ? '' : 'none';

            list.innerHTML = '';
            const filtered = query ? all.filter(o => o.text.toLowerCase().includes(query)) : all;

            if (!filtered.length) {
                const li = document.createElement('li');
                li.textContent = 'No results';
                li.style.cssText = 'padding:8px 12px;font-size:13px;color:#94a3b8;cursor:default;';
                list.appendChild(li);
                return;
            }

            const curVal = select.value;
            filtered.forEach(o => {
                const active = isMulti ? o.selected : (o.value === curVal);
                const li = document.createElement('li');
                li.style.cssText = [
                    'display:flex',
                    'align-items:center',
                    'justify-content:space-between',
                    'gap:8px',
                    'padding:8px 12px',
                    'font-size:13px',
                    'font-family:inherit',
                    'cursor:pointer',
                    'user-select:none',
                    '-webkit-user-select:none',
                    'white-space:nowrap',
                    'overflow:hidden',
                    'text-overflow:ellipsis',
                    active
                        ? 'background:#eff6ff;color:#1d4ed8;font-weight:500;'
                        : 'color:#1e293b;background:transparent;',
                ].join(';');

                // Multi-select: leading checkbox to make toggling obvious
                if (isMulti) {
                    const box = document.createElement('span');
                    box.style.cssText = [
                        'flex-shrink:0', 'width:15px', 'height:15px', 'border-radius:4px',
                        'display:flex', 'align-items:center', 'justify-content:center',
                        `border:1.5px solid ${active ? '#1d4ed8' : '#cbd5e1'}`,
                        `background:${active ? '#1d4ed8' : '#fff'}`,
                    ].join(';');
                    if (active) box.innerHTML = `<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="3.5"><path d="M5 13l4 4L19 7"/></svg>`;
                    li.appendChild(box);
                }

                const textSpan = document.createElement('span');
                textSpan.textContent = o.text;
                textSpan.style.cssText = 'flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
                li.appendChild(textSpan);

                if (!isMulti && active) {
                    const tick = document.createElement('span');
                    tick.style.flexShrink = '0';
                    tick.innerHTML = `<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="#1d4ed8" stroke-width="2.5"><path d="M5 13l4 4L19 7"/></svg>`;
                    li.appendChild(tick);
                }

                li.addEventListener('mouseenter', () => { if (!active) li.style.background = '#f8fafc'; });
                li.addEventListener('mouseleave', () => { if (!active) li.style.background = 'transparent'; });
                li.addEventListener('mousedown', e => {
                    e.preventDefault();
                    if (isMulti) {
                        o.selected = !o.selected;
                        select.dispatchEvent(new Event('change', { bubbles: true }));
                        refreshLabel();
                        renderList(searchInput.value);   // keep panel open, refresh checkmarks
                    } else {
                        select.value = o.value;
                        select.dispatchEvent(new Event('change', { bubbles: true }));
                        refreshLabel();
                        closePanel();
                    }
                });
                list.appendChild(li);
            });
        }

        function openPanel() {
            if (select.disabled) return;
            isOpen = true;

            // Flip above if not enough space below
            const rect       = wrap.getBoundingClientRect();
            const spaceBelow = window.innerHeight - rect.bottom;
            const spaceAbove = rect.top;
            const estH       = Math.min(opts().length * 36 + 68, 290);

            if (spaceBelow < estH && spaceAbove > spaceBelow) {
                panel.style.bottom      = '100%';
                panel.style.top         = 'auto';
                panel.style.marginBottom = '3px';
                panel.style.marginTop    = '';
            } else {
                panel.style.top          = '100%';
                panel.style.bottom       = 'auto';
                panel.style.marginTop    = '3px';
                panel.style.marginBottom = '';
            }

            panel.style.display    = 'block';
            chevron.style.transform = 'rotate(180deg)';
            btn.style.borderColor  = '#93c5fd';
            btn.style.boxShadow    = '0 0 0 3px rgba(147,197,253,.35)';

            searchInput.value = '';
            renderList('');

            requestAnimationFrame(() => {
                if (searchWrap.style.display !== 'none') {
                    searchInput.focus();
                }
                // Scroll active item into view
                const active = list.querySelector('li[style*="#eff6ff"]');
                if (active) active.scrollIntoView({ block: 'nearest' });
            });
        }

        function closePanel() {
            isOpen = false;
            panel.style.display     = 'none';
            chevron.style.transform  = '';
            btn.style.borderColor   = '';
            btn.style.boxShadow     = '';
        }

        // ── Events ───────────────────────────────────────────────────────────
        btn.addEventListener('click', e => {
            e.stopPropagation();
            isOpen ? closePanel() : openPanel();
        });

        document.addEventListener('click', e => {
            if (isOpen && !wrap.contains(e.target)) closePanel();
        }, true);

        // Watch for option changes (dynamic loading)
        new MutationObserver(() => {
            refreshLabel();
            if (isOpen) renderList(searchInput.value);
        }).observe(select, { childList: true, subtree: true, attributes: true, attributeFilter: ['selected', 'disabled'] });

        // Intercept select.value = x so the trigger label stays in sync with
        // programmatic value assignments (e.g. canvas form populating fields).
        // Single-select only — a multi-select's value is driven by option.selected.
        if (!isMulti) {
            const _origDesc = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value');
            Object.defineProperty(select, 'value', {
                get() { return _origDesc.get.call(this); },
                set(v) { _origDesc.set.call(this, v); refreshLabel(); },
                configurable: true,
            });
        }

        select._ttSelect = { refresh: refreshLabel, close: closePanel };
        refreshLabel();
    }

    function init(root) {
        (root || document).querySelectorAll('select[data-searchable]').forEach(build);
    }

    document.addEventListener('DOMContentLoaded', () => init());

    window.ttSelectInit = sel => {
        if (sel instanceof HTMLSelectElement) build(sel);
        else if (typeof sel === 'string') { const el = document.getElementById(sel); if (el) build(el); }
        else init();
    };
})();
