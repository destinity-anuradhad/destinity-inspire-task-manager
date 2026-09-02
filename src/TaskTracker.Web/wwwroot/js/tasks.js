function splitDateTime(s) {
    if (!s) return { date: '', time: '' };
    const parts = s.trim().split(/[T ]/);
    return { date: parts[0] || '', time: parts.length > 1 ? parts[1].substring(0, 5) : '' };
}

// Convert any stored date string to yyyy-MM-dd for <input type="date">
function toDateInput(s) {
    if (!s) return '';
    if (/^\d{4}-\d{2}-\d{2}$/.test(s)) return s;               // already ISO
    var m = s.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/);
    if (m) return `${m[3]}-${m[2].padStart(2,'0')}-${m[1].padStart(2,'0')}`; // dd/MM/yyyy
    var d = new Date(s);
    return isNaN(d.getTime()) ? '' : d.toISOString().split('T')[0];
}

const backdrop = document.getElementById('canvas-backdrop');
const panel    = document.getElementById('task-canvas');

let _cvTaskId   = null;
let _cvTask     = null;    // current task object open in canvas
let _cvEditable = false;   // current canvas editable state
let _modulesCache = null;  // cache modules for session

// Load modules into the Module dropdown, then load clients. isEditable re-applies disabled state after load.
async function _loadModules(moduleId, moduleName, clientId, clientName, isEditable) {
    const sel = document.getElementById('cf-project');
    sel.innerHTML = '<option value="">— Loading… —</option>';
    try {
        if (!_modulesCache || _modulesCache.length === 0) {
            const r = await fetch('/Tasks/GetModules');
            if (!r.ok) throw new Error('HTTP ' + r.status);
            _modulesCache = await r.json();
        }
        sel.innerHTML = '<option value="">— None —</option>';
        _modulesCache.forEach(m => {
            const opt = document.createElement('option');
            opt.value       = m.name;
            opt.dataset.mid = m.id;
            opt.textContent = m.name;
            if (moduleId ? m.id === moduleId : (moduleName && m.name === moduleName)) {
                opt.selected = true;
                document.getElementById('cf-module-id').value = m.id;
            }
            sel.appendChild(opt);
        });
    } catch(e) {
        sel.innerHTML = '<option value="">— Failed to load —</option>';
        console.error('GetModules error:', e);
    }
    // Load clients — filtered by selected module, or all clients if none selected
    const selMid = parseInt(document.getElementById('cf-module-id').value) || 0;
    await _loadClients(selMid, clientId, clientName);

    // Re-apply disabled state after async population (user may have clicked Edit while loading)
    if (!_cvEditable) {
        document.querySelectorAll('#canvas-form select').forEach(el => {
            el.disabled = true;
            el.classList.add('bg-gray-50', 'cursor-default');
        });
    }
}

// Load clients for the selected module ID
async function _loadClients(moduleId, restoreClientId, restoreClientName) {
    const sel = document.getElementById('cf-property');
    sel.innerHTML = '<option value="">— Loading… —</option>';
    try {
        const r = await fetch('/Tasks/GetClients?moduleId=' + moduleId);
        const clients = await r.json();
        sel.innerHTML = '<option value="">— None —</option>';
        clients.forEach(c => {
            const opt = document.createElement('option');
            opt.value       = c.name;
            opt.dataset.cid = c.id;
            opt.textContent = c.name + (c.fromCompany ? ' ★' : '');
            opt.title       = [c.contactPerson, c.contactNo, c.email].filter(Boolean).join(' · ');
            if (restoreClientId ? c.id === restoreClientId : c.name === restoreClientName) {
                opt.selected = true;
                document.getElementById('cf-client-id').value = c.id;
            }
            sel.appendChild(opt);
        });
        if (!clients.length)
            sel.innerHTML = '<option value="">— No clients found —</option>';
    } catch(e) {
        sel.innerHTML = '<option value="">— Failed to load —</option>';
        console.error('GetClients error:', e);
    }
}

// Called when the Module dropdown changes
async function onModuleChange(selectEl) {
    const opt = selectEl.options[selectEl.selectedIndex];
    document.getElementById('cf-module-id').value = opt ? (opt.dataset.mid ?? '') : '';
    document.getElementById('cf-client-id').value = '';
    const mid = parseInt(document.getElementById('cf-module-id').value) || 0;
    await _loadClients(mid, null, '');
}

// ── Owner picker (admin only) ─────────────────────────────
let _ownerSelected = new Set();
let _ownerPickerOpen = false;

function _ownerAvatar(name) {
    const clr = (CV_COLORS && CV_COLORS[name]) ? CV_COLORS[name] : '#64748b';
    return `<span style="display:inline-flex;align-items:center;justify-content:center;width:20px;height:20px;border-radius:50%;background:${clr};color:#fff;font-size:10px;font-weight:700;flex-shrink:0">${(name||'?')[0].toUpperCase()}</span>`;
}

function _renderOwnerChips() {
    const trigger = document.getElementById('cf-owner-trigger');
    if (!trigger) return;
    const ph = document.getElementById('cf-owner-placeholder');
    // Remove old chips (keep placeholder + chevron)
    trigger.querySelectorAll('.owner-chip').forEach(c => c.remove());
    if (_ownerSelected.size === 0) {
        ph.classList.remove('hidden');
    } else {
        ph.classList.add('hidden');
        const chevron = trigger.querySelector('svg');
        [..._ownerSelected].forEach(name => {
            const chip = document.createElement('span');
            chip.className = 'owner-chip inline-flex items-center gap-1 pl-1 pr-1.5 py-0.5 rounded-full text-xs font-medium text-white';
            chip.style.background = (CV_COLORS && CV_COLORS[name]) ? CV_COLORS[name] : '#64748b';
            chip.innerHTML = `${_ownerAvatar(name)}<span>${name}</span>`
                + `<button type="button" onclick="event.stopPropagation();_toggleOwner('${name.replace(/'/g,"\\'")}')"
                           style="margin-left:2px;opacity:.7;line-height:1" class="hover:opacity-100">✕</button>`;
            trigger.insertBefore(chip, chevron);
        });
    }
    document.getElementById('cf-owner').value = [..._ownerSelected].join(', ');
}

function _renderOwnerList(filter) {
    const ul = document.getElementById('cf-owner-items');
    if (!ul) return;
    const allNames = [...document.getElementById('cf-owner-sel').options].map(o => o.value);
    const q = (filter || '').toLowerCase();
    ul.innerHTML = allNames
        .filter(n => !q || n.toLowerCase().includes(q))
        .map(n => {
            const checked = _ownerSelected.has(n);
            return `<li onclick="_toggleOwner('${n.replace(/'/g,"\\'")}')"
                        class="flex items-center gap-2.5 px-3 py-2 cursor-pointer hover:bg-gray-50 select-none text-sm ${checked ? 'bg-blue-50/50' : ''}">
                <span style="display:inline-flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:50%;background:${(CV_COLORS&&CV_COLORS[n])||'#64748b'};color:#fff;font-size:12px;font-weight:700;flex-shrink:0">${n[0].toUpperCase()}</span>
                <span class="flex-1 ${checked ? 'font-medium text-gray-900' : 'text-gray-700'}">${n}</span>
                ${checked ? '<svg class="w-4 h-4 text-blue-600 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.5" d="M5 13l4 4L19 7"/></svg>' : ''}
            </li>`;
        }).join('');
}

function _toggleOwner(name) {
    if (_ownerSelected.has(name)) _ownerSelected.delete(name);
    else _ownerSelected.add(name);
    _renderOwnerChips();
    _renderOwnerList(document.getElementById('cf-owner-search')?.value);
}

function _filterOwners(q) { _renderOwnerList(q); }

function toggleOwnerPicker() {
    _ownerPickerOpen = !_ownerPickerOpen;
    const panel = document.getElementById('cf-owner-panel');
    panel.classList.toggle('hidden', !_ownerPickerOpen);
    if (_ownerPickerOpen) {
        document.getElementById('cf-owner-search').value = '';
        _renderOwnerList('');
        document.getElementById('cf-owner-search').focus();
    }
}

document.addEventListener('click', e => {
    if (_ownerPickerOpen && !e.target.closest('#cf-owner-picker')) {
        _ownerPickerOpen = false;
        document.getElementById('cf-owner-panel')?.classList.add('hidden');
    }
});

// Restore owner field — admin: custom picker; non-admin: locked to self
function _restoreOwner(ownerValue) {
    if (CV_ADMIN) {
        _ownerSelected = new Set(
            ownerValue.split(',').map(s => s.trim()).filter(Boolean)
        );
        _renderOwnerChips();
        if (_ownerPickerOpen) _renderOwnerList(document.getElementById('cf-owner-search')?.value);
    } else {
        const displayName = ownerValue || CV_ME;
        document.getElementById('cf-owner-label').textContent = displayName;
        document.getElementById('cf-owner').value = CV_ME;
    }
}

// ── Sprint loader ─────────────────────────────────────────────────────────────

let _sprintsCache = null;

async function _loadSprints(currentSprintId) {
    const sel = document.getElementById('cf-sprint-sel');
    try {
        if (!_sprintsCache || _sprintsCache.length === 0) {
            const r = await fetch('/Tasks/GetSprints');
            _sprintsCache = await r.json();
        }
        sel.innerHTML = '<option value="">— Backlog —</option>' +
            _sprintsCache.map(s =>
                `<option value="${s.id}"${s.id == currentSprintId ? ' selected' : ''}>${s.isActive ? '⚡ ' : ''}${s.name}</option>`
            ).join('');
        if (currentSprintId) sel.value = currentSprintId;
    } catch {
        sel.innerHTML = '<option value="">— Backlog —</option>';
    }
}

// ── Item Type / Status / Parent helpers ──────────────────────────────────────

function _updateStatusOptions(itemType, currentVal) {
    const sel = document.getElementById('cf-status');
    const opts = ITEM_STATUSES[itemType] || ITEM_STATUSES[''];
    sel.innerHTML = '<option value="">— None —</option>' +
        opts.map(s => `<option value="${s}"${s === currentVal ? ' selected' : ''}>${s}</option>`).join('');
}

async function _loadParentPicker(itemType, currentParentId) {
    const sel = document.getElementById('cf-parent-sel');
    const parentType = PARENT_TYPE[itemType];
    if (!parentType) {
        sel.innerHTML = '<option value="">— None —</option>';
        sel.disabled = true;
        sel.classList.add('bg-gray-50', 'cursor-default');
        document.getElementById('cf-parent-id').value = '';
        return;
    }
    sel.disabled = false;
    sel.classList.remove('bg-gray-50', 'cursor-default');
    sel.innerHTML = '<option value="">— Loading… —</option>';
    try {
        const r = await fetch('/Tasks/GetParentItems?itemType=' + encodeURIComponent(itemType));
        const items = await r.json();
        sel.innerHTML = '<option value="">— None —</option>' +
            items.map(i => `<option value="${i.id}" data-module-id="${i.moduleId??''}" data-client-id="${i.clientId??''}" data-project="${_esc(i.project??'')}" data-property="${_esc(i.property??'')}" data-priority="${i.priority??''}"${i.id==currentParentId?' selected':''}>${_esc(i.goal??'—')}</option>`).join('');
        if (currentParentId) sel.value = currentParentId;
    } catch {
        sel.innerHTML = '<option value="">— Error loading —</option>';
    }
}

function _onParentChange(sel) {
    document.getElementById('cf-parent-id').value = sel.value;
    if (!sel.value) return;
    const opt      = sel.options[sel.selectedIndex];
    const moduleId = parseInt(opt.dataset.moduleId) || null;
    const clientId = parseInt(opt.dataset.clientId) || null;
    const project  = opt.dataset.project  || '';
    const property = opt.dataset.property || '';
    // Req 1: auto-fill module/client/project/property from parent
    _loadModules(moduleId, project, clientId, property, _cvEditable);
    // Req 5: auto-inherit priority from parent if not already set
    const priFld = document.getElementById('cf-priority');
    if (priFld && !priFld.value && opt.dataset.priority)
        priFld.value = opt.dataset.priority;
    // Visual hint that fields were inherited
    if (moduleId || clientId || project || property || opt.dataset.priority) {
        const hint = document.getElementById('cf-parent-inherit-hint');
        if (hint) { hint.classList.remove('hidden'); setTimeout(() => hint.classList.add('hidden'), 3000); }
    }
}

function onItemTypeChange(val) {
    document.getElementById('cf-item-type').value = val;
    const currentStatus = document.getElementById('cf-status').value;
    _updateStatusOptions(val, currentStatus);
    _loadParentPicker(val, document.getElementById('cf-parent-id').value);
    document.getElementById('cf-ac-section').classList.toggle('hidden', val !== 'Story');
    // Epics and Features span multiple sprints — hide the sprint picker for them
    const sprintRow = document.getElementById('cf-sprint-row');
    const noSprint  = val === 'Epic' || val === 'Feature';
    sprintRow.classList.toggle('hidden', noSprint);
    if (noSprint) {
        document.getElementById('cf-sprint-sel').value = '';
        document.getElementById('cf-sprint-id').value  = '';
    }
}

// ─────────────────────────────────────────────────────────────────────────────

function openCanvas(task) {
    _destroyMde(); // must destroy before clearing field values
    const isNew   = !task;
    const _owners = (task?.owner || '').split(',').map(s => s.trim().toLowerCase()).filter(Boolean);
    const _meLc   = (CV_ME || '').toLowerCase();
    // Mirrors server-side CanModify: admin, unowned, owner, reporter, or creator
    const isEditable = isNew || CV_ADMIN || _owners.length === 0 || _owners.includes(_meLc)
        || (task?.reporter  || '').toLowerCase() === _meLc
        || (task?.createdBy || '').toLowerCase() === _meLc;
    _cvTaskId = task ? task.id : null;
    _cvTask   = task || null;

    // Clear form fields
    ['cf-id','cf-goal','cf-owner',
     'cf-status','cf-priority','cf-weight',
     'cf-endDate-vis','cf-labels','cf-notes','cf-ac',
     'cf-item-type','cf-parent-id','cf-sprint-id','cf-swimlane']
    .forEach(id => { const el = document.getElementById(id); if (el) el.value = ''; });
    document.getElementById('cf-comment-text').value = '';
    document.getElementById('cf-module-id').value = '';
    document.getElementById('cf-client-id').value = '';
    document.getElementById('cf-item-type-sel').value = '';
    document.getElementById('cf-sprint-sel').value = '';
    _updateStatusOptions('', '');
    _loadParentPicker('', null);
    // Reset owner field
    _restoreOwner('');

    const itemType = task ? (task.itemType ?? '') : '';
    if (task) {
        document.getElementById('canvas-title').textContent    = isEditable ? 'Edit Task' : 'Task Details';
        document.getElementById('canvas-subtitle').textContent = 'Task #' + task.id;
        document.getElementById('canvas-subtitle').classList.remove('hidden');
        document.getElementById('canvas-save-label').textContent = 'Save Changes';
        document.getElementById('cf-id').value               = task.id;
        document.getElementById('cf-goal').value             = task.goal ?? '';
        _restoreOwner(task.owner ?? '');
        document.getElementById('cf-status').value           = task.status   ?? '';
        document.getElementById('cf-priority').value         = task.priority ?? '';
        document.getElementById('cf-swimlane').value         = task.swimlane ?? '';
        const reporterSel = document.getElementById('cf-reporter-sel');
        if (reporterSel) reporterSel.value = task.reporter || task.createdBy || '';
        document.getElementById('cf-endDate-vis').value      = task.endDate ? task.endDate.split('T')[0] : '';
        document.getElementById('cf-weight').value           = task.weight  ?? '';
        document.getElementById('cf-labels').value           = task.labels  ?? '';
        document.getElementById('cf-notes').value            = task.notes   ?? '';
        document.getElementById('cf-ac').value               = task.acceptanceCriteria ?? '';
        // Item type, parent, sprint
        document.getElementById('cf-item-type').value     = itemType;
        document.getElementById('cf-item-type-sel').value = itemType;
        document.getElementById('cf-parent-id').value     = task.parentId ?? '';
        document.getElementById('cf-sprint-id').value     = task.sprintId ?? '';
        document.getElementById('cf-ac-section').classList.toggle('hidden', itemType !== 'Story');
        const noSprint = itemType === 'Epic' || itemType === 'Feature';
        document.getElementById('cf-sprint-row').classList.toggle('hidden', noSprint);
        _updateStatusOptions(itemType, task.status ?? '');
        _loadParentPicker(itemType, task.parentId ?? null);
        _loadSprints(noSprint ? null : (task.sprintId ?? null));
    } else {
        document.getElementById('canvas-title').textContent = 'New Task';
        document.getElementById('canvas-subtitle').classList.add('hidden');
        document.getElementById('canvas-save-label').textContent = 'Save Task';
        const reporterSelNew = document.getElementById('cf-reporter-sel');
        if (reporterSelNew) reporterSelNew.value = '@myDisplayName';
        document.getElementById('cf-ac-section').classList.add('hidden');
        // Non-admin: default owner to themselves; admin: clear selection
        _restoreOwner(CV_ADMIN ? '' : CV_ME);
        _updateStatusOptions('', '');
        _loadSprints(null);
    }

    // Toggle edit / view-only mode
    _applyCanvasMode(isEditable, task);

    // Show / hide copy URL button
    const copyBtn = document.getElementById('canvas-copy-url');
    if (task) { copyBtn.classList.remove('hidden'); } else { copyBtn.classList.add('hidden'); }

    // Watch button — existing tasks only
    const watchBtn = document.getElementById('canvas-watch-btn');
    if (task) {
        watchBtn.classList.remove('hidden');
        watchBtn.classList.add('flex');
        _loadWatchers(task.id);
    } else {
        watchBtn.classList.add('hidden');
        watchBtn.classList.remove('flex');
    }

    // Show comments, history, attachments, links for existing tasks
    const commSection  = document.getElementById('cf-comments-section');
    const histSection  = document.getElementById('cf-history-section');
    const attSection   = document.getElementById('cf-attachments-section');
    const linksSection = document.getElementById('cf-links-section');
    if (task) {
        commSection.classList.remove('hidden');
        _loadComments(task.id);
        histSection.classList.remove('hidden');
        _loadHistory(task.id);
        attSection.classList.remove('hidden');
        _loadAttachments(task.id);
        linksSection.classList.remove('hidden');
        _clearLinkSearch();
        _loadLinks(task.id);
        _loadChildStats(task.id, itemType);
    } else {
        commSection.classList.add('hidden');
        histSection.classList.add('hidden');
        attSection.classList.add('hidden');
        linksSection.classList.add('hidden');
        document.getElementById('canvas-progress-bar').classList.add('hidden');
    }

    // Join SignalR group for real-time comments/links
    if (task) srJoinTask(task.id);

    // Open canvas immediately — dropdowns load in background
    backdrop.classList.remove('opacity-0','pointer-events-none');
    backdrop.classList.add('opacity-100');
    panel.classList.remove('translate-x-full');
    document.body.style.overflow = 'hidden';

    // Load module + client dropdowns after panel is visible
    _loadModules(task ? (task.moduleId ?? null) : null,
                 task ? (task.project   ?? '')  : '',
                 task ? (task.clientId  ?? null) : null,
                 task ? (task.property  ?? '')  : '',
                 isEditable);
}

function _applyCanvasMode(isEditable, task) {
    _cvEditable = !!isEditable;
    const viewCard = document.getElementById('canvas-view-card');
    const form     = document.getElementById('canvas-form');

    if (!isEditable && task) {
        // View-only: show the two-button card, hide the form
        viewCard.classList.remove('hidden');
        form.classList.add('hidden');
        document.getElementById('canvas-view-goal').textContent = task.goal || '';
        const owners = (task.owner || '').split(',').map(s => s.trim().toLowerCase()).filter(Boolean);
        const canEdit = CV_ADMIN || owners.includes((CV_ME || '').toLowerCase());
        document.getElementById('canvas-edit-btn').classList.toggle('hidden', !canEdit);
    } else {
        // Edit / new: hide the card, show the form with fields enabled
        viewCard.classList.add('hidden');
        form.classList.remove('hidden');
        document.querySelectorAll('#canvas-form input:not([type=hidden]):not(#cf-comment-text), #canvas-form select, #canvas-form textarea:not(#cf-comment-text)')
            .forEach(el => {
                el.disabled = !isEditable;
                el.classList.toggle('bg-gray-50', !isEditable);
                el.classList.toggle('cursor-default', !isEditable);
            });
        const ownerPicker = document.getElementById('cf-owner-picker');
        if (ownerPicker) ownerPicker.style.pointerEvents = isEditable ? '' : 'none';
        // ItemType select is a plain select — handled by querySelectorAll above
        // but parent select may be disabled from _loadParentPicker; re-enable if editing
        if (isEditable) {
            const ps = document.getElementById('cf-parent-sel');
            if (ps && PARENT_TYPE[document.getElementById('cf-item-type-sel').value]) {
                ps.disabled = false;
                ps.classList.remove('bg-gray-50', 'cursor-default');
            }
        }
    }

    document.getElementById('canvas-save-btn').classList.toggle('hidden', !isEditable);
    document.getElementById('canvas-view-badge').classList.add('hidden');
    _mdeInit(isEditable);
}

function _switchToEditMode() {
    document.getElementById('canvas-title').textContent = 'Edit Task';
    _applyCanvasMode(true, _cvTask);
}

function _switchToCommentMode() {
    // Show form in read-only mode (fields stay disabled; comment box is always enabled)
    document.getElementById('canvas-view-card').classList.add('hidden');
    const form = document.getElementById('canvas-form');
    form.classList.remove('hidden');
    setTimeout(() => {
        const section = document.getElementById('cf-comments-section');
        if (section) section.scrollIntoView({ behavior: 'smooth', block: 'start' });
        const box = document.getElementById('cf-comment-text');
        if (box) box.focus();
    }, 100);
}

function _esc(s) { return (s||'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }

function _relTime(iso) {
    const d = (Date.now() - new Date(iso).getTime()) / 1000;
    if (d < 60)  return 'just now';
    if (d < 3600) return Math.floor(d/60) + 'm ago';
    if (d < 86400) return Math.floor(d/3600) + 'h ago';
    if (d < 604800) return Math.floor(d/86400) + 'd ago';
    return new Date(iso).toLocaleDateString(undefined, {day:'numeric',month:'short',year:'numeric'});
}

function _avatarHtml(name, size) {
    const clr = (CV_COLORS && CV_COLORS[name]) ? CV_COLORS[name] : '#64748b';
    const init = name ? name[0].toUpperCase() : '?';
    const sz = size || 28;
    return `<div style="width:${sz}px;height:${sz}px;border-radius:50%;background:${clr};color:#fff;font-size:${Math.round(sz*0.43)}px;font-weight:700;display:flex;align-items:center;justify-content:center;flex-shrink:0">${_esc(init)}</div>`;
}

async function _loadChildStats(taskId, itemType) {
    const bar = document.getElementById('canvas-progress-bar');
    if (!taskId || !['Epic', 'Feature', 'Story'].includes(itemType)) {
        bar.classList.add('hidden');
        return;
    }
    try {
        const r = await fetch(`/Tasks/GetChildStats?id=${taskId}`);
        const d = await r.json();
        if (!d || d.total === 0) { bar.classList.add('hidden'); return; }
        document.getElementById('canvas-progress-label').textContent =
            `${d.done} of ${d.total} ${d.leafType} complete`;
        document.getElementById('canvas-progress-pct').textContent = d.pct + '%';
        const fill = document.getElementById('canvas-progress-fill');
        fill.style.width = d.pct + '%';
        fill.classList.remove('bg-blue-500', 'bg-green-500');
        fill.classList.add(d.pct === 100 ? 'bg-green-500' : 'bg-blue-500');
        document.getElementById('canvas-progress-pct').className =
            'font-semibold ' + (d.pct === 100 ? 'text-green-600' : 'text-blue-600');
        bar.classList.remove('hidden');
    } catch {
        bar.classList.add('hidden');
    }
}

async function _loadComments(taskId) {
    const list  = document.getElementById('cf-comments-list');
    const count = document.getElementById('cf-comment-count');
    list.innerHTML = '<div class="flex items-center gap-2 py-3 justify-center"><svg class="w-4 h-4 animate-spin text-gray-300" fill="none" viewBox="0 0 24 24"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8z"></path></svg><span class="text-xs text-gray-400">Loading…</span></div>';
    try {
        const r = await fetch('/Tasks/GetComments?taskId=' + taskId);
        const comments = await r.json();
        count.textContent = comments.length ? comments.length + ' comment' + (comments.length !== 1 ? 's' : '') : '';
        if (!comments.length) {
            list.innerHTML = `<div class="flex flex-col items-center py-6 text-center">
                <svg class="w-8 h-8 text-gray-200 mb-2" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z"/></svg>
                <p class="text-xs text-gray-400">No comments yet</p>
                <p class="text-[10px] text-gray-300 mt-0.5">Be the first to add one</p>
            </div>`;
        } else {
            list.innerHTML = comments.map(c => {
                const isMe = c.changedBy === CV_ME;
                const bg   = isMe ? 'background:#eff6ff;border:1px solid #bfdbfe' : 'background:#f8fafc;border:1px solid #e2e8f0';
                return `<div style="display:flex;gap:8px;align-items:flex-start${isMe ? ';flex-direction:row-reverse' : ''}">
                    ${_avatarHtml(c.changedBy, 28)}
                    <div style="max-width:calc(100% - 40px)">
                        <div style="display:flex;align-items:baseline;gap:6px;margin-bottom:3px;${isMe ? 'flex-direction:row-reverse' : ''}">
                            <span style="font-size:11.5px;font-weight:600;color:#1e293b">${_esc(c.changedBy)||'Unknown'}</span>
                            <span style="font-size:10px;color:#94a3b8" title="${_esc(c.changedAt)}">${_relTime(c.changedAtIso)}</span>
                        </div>
                        <div style="padding:8px 11px;border-radius:${isMe ? '12px 4px 12px 12px' : '4px 12px 12px 12px'};${bg}">
                            <p style="font-size:13px;color:#334155;white-space:pre-wrap;line-height:1.5;margin:0">${_renderCommentText(c.details)}</p>
                        </div>
                    </div>
                </div>`;
            }).join('');
            list.scrollTop = list.scrollHeight;
        }
    } catch(e) { list.innerHTML = `<p style="font-size:12px;color:#f87171;padding:8px 0">Could not load comments — ${e.message || 'network error'}. Try closing and reopening the task.</p>`; }
}

async function submitComment() {
    if (!_cvTaskId) return;
    const ta  = document.getElementById('cf-comment-text');
    const btn = document.getElementById('cf-comment-btn');
    const text = ta.value.trim();
    if (!text) { ta.focus(); return; }
    const token = document.querySelector('#canvas-form input[name=__RequestVerificationToken]').value;
    btn.disabled = true;
    const fd = new FormData();
    fd.append('taskId', _cvTaskId);
    fd.append('comment', text);
    fd.append('__RequestVerificationToken', token);
    try {
        const r = await fetch('/Tasks/AddComment', { method:'POST', body: fd });
        const j = await r.json();
        if (j.success) {
            ta.value = '';
            ta.style.height = 'auto';
            await _loadComments(_cvTaskId);
        } else showToast(j.error || 'Failed to post comment.', 'error');
    } catch { showToast('Failed to post comment.', 'error'); }
    finally { btn.disabled = false; ta.focus(); }
}

// ─── @@mention / #task autocomplete ─────────────────────────────────────────
let _acMode   = null;   // '@@' | '#' | null
let _acStart  = -1;     // textarea index where trigger char sits
let _acItems  = [];     // dropdown item list
let _acIdx    = -1;     // selected row
let _acTimer  = null;   // debounce for # search

function _acOnInput(ta) {
    const pos  = ta.selectionStart;
    const text = ta.value.substring(0, pos);
    // Match last @@ or # that starts a "word" (beginning of string or preceded by whitespace)
    const m = text.match(/(?:^|[\s,;])([@@#]\S*)$/);
    if (!m) { _acClose(); return; }
    const full    = m[1];
    const trigger = full[0];
    const query   = full.slice(1).toLowerCase();
    _acMode  = trigger;
    _acStart = pos - full.length;
    if (trigger === '@@') {
        const hits = (CV_USERS || [])
            .filter(n => n.toLowerCase().startsWith(query))
            .slice(0, 8)
            .map(n => ({ label: n, value: n }));
        _acShowItems(hits);
    } else {
        if (!query) { _acClose(); return; }
        clearTimeout(_acTimer);
        _acTimer = setTimeout(async () => {
            try {
                const r = await fetch('/Tasks/SearchTasks?q=' + encodeURIComponent(query));
                const tasks = await r.json();
                _acShowItems(tasks.map(t => ({ label: '#' + t.id + ' ' + (t.goal || ''), value: String(t.id) })));
            } catch {}
        }, 180);
    }
}

function _acShowItems(items) {
    _acItems = items;
    _acIdx   = items.length > 0 ? 0 : -1;
    if (!items.length) { _acClose(); return; }
    _acRender();
    _acPosition();
}

function _acRender() {
    const dd = document.getElementById('cf-ac-dropdown');
    if (!dd) return;
    dd.innerHTML = _acItems.map((item, i) =>
        `<button type="button" onclick="_acSelect(${i})"
                 style="display:block;width:100%;text-align:left;padding:8px 12px;font-size:13px;border:none;background:${i===_acIdx?'#eff6ff':'transparent'};color:${i===_acIdx?'#1d4ed8':'#1e293b'};cursor:pointer;white-space:nowrap;overflow:hidden;text-overflow:ellipsis"
                 onmouseover="this.style.background='#f8fafc'" onmouseout="this.style.background='${i===_acIdx?'#eff6ff':'transparent'}'">
            ${_esc(item.label)}
        </button>`
    ).join('');
    dd.classList.remove('hidden');
}

function _acPosition() {
    const ta   = document.getElementById('cf-comment-text');
    const dd   = document.getElementById('cf-ac-dropdown');
    if (!ta || !dd) return;
    const rect  = ta.getBoundingClientRect();
    const spaceBelow = window.innerHeight - rect.bottom;
    const maxH  = 192;
    if (spaceBelow >= maxH || spaceBelow >= 100) {
        dd.style.top  = (rect.bottom + 4) + 'px';
    } else {
        dd.style.top  = (rect.top - maxH - 4) + 'px';
    }
    dd.style.left  = rect.left + 'px';
    dd.style.width = Math.min(300, window.innerWidth - rect.left - 16) + 'px';
}

function _acKeyDown(e, ta) {
    const dd = document.getElementById('cf-ac-dropdown');
    if (!dd || dd.classList.contains('hidden')) return;
    if (e.key === 'ArrowDown') {
        e.preventDefault();
        _acIdx = (_acIdx + 1) % _acItems.length;
        _acRender();
    } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        _acIdx = (_acIdx - 1 + _acItems.length) % _acItems.length;
        _acRender();
    } else if (e.key === 'Enter' && !e.ctrlKey && _acIdx >= 0) {
        e.preventDefault();
        _acSelect(_acIdx);
    } else if (e.key === 'Tab' && _acIdx >= 0) {
        e.preventDefault();
        _acSelect(_acIdx);
    } else if (e.key === 'Escape') {
        e.preventDefault();
        _acClose();
    }
}

function _acSelect(idx) {
    const item = _acItems[idx];
    if (!item) return;
    const ta     = document.getElementById('cf-comment-text');
    const pos    = ta.selectionStart;
    const insert = _acMode === '@@' ? ('@@' + item.value + ' ') : ('#' + item.value + ' ');
    ta.value = ta.value.substring(0, _acStart) + insert + ta.value.substring(pos);
    const newPos = _acStart + insert.length;
    ta.setSelectionRange(newPos, newPos);
    ta.style.height = 'auto';
    ta.style.height = Math.min(ta.scrollHeight, 120) + 'px';
    _acClose();
    ta.focus();
}

function _acClose() {
    clearTimeout(_acTimer);
    _acMode = null; _acStart = -1; _acItems = []; _acIdx = -1;
    const dd = document.getElementById('cf-ac-dropdown');
    if (dd) dd.classList.add('hidden');
}

// Dismiss when clicking outside the textarea or dropdown
document.addEventListener('click', e => {
    if (!e.target.closest('#cf-comment-text') && !e.target.closest('#cf-ac-dropdown')) _acClose();
});

// Render comment text: HTML-escape then highlight @@mentions and #task-ids
function _renderCommentText(text) {
    let s = _esc(text);
    // @@mention → blue highlight
    s = s.replace(/@@([^\s<&]+)/g, '<span style="color:#2563eb;font-weight:600">@@$1</span>');
    // #task-id → violet, clickable
    s = s.replace(/#(\d+)/g, '<span style="color:#7c3aed;font-weight:600;cursor:pointer;text-decoration:underline;text-underline-offset:2px" onclick="_openTaskById($1)">#$1</span>');
    return s;
}

async function _openTaskById(id) {
    try {
        const r = await fetch('/Tasks/GetTask?id=' + id);
        if (!r.ok) { showToast('Task #' + id + ' not found.', 'error'); return; }
        const task = await r.json();
        openCanvas(task);
    } catch { showToast('Could not open task #' + id + '.', 'error'); }
}

// ─── Task Links (Phase 5) ────────────────────────────────────────────────────
const LINK_INVERSE = { 'Blocks':'Blocked By','Blocked By':'Blocks','Predecessor':'Successor','Successor':'Predecessor','Related':'Related','Duplicate':'Duplicate' };
const LINK_COLORS  = { 'Related':'bg-gray-100 text-gray-600','Blocks':'bg-red-100 text-red-700','Blocked By':'bg-orange-100 text-orange-700','Duplicate':'bg-amber-100 text-amber-700','Predecessor':'bg-violet-100 text-violet-700','Successor':'bg-blue-100 text-blue-700' };

let _linkSearchTimer   = null;
let _linkSelectedTask  = null;

async function _loadLinks(taskId) {
    const list  = document.getElementById('cf-links-list');
    const count = document.getElementById('cf-link-count');
    list.innerHTML = '<div class="flex items-center gap-2 py-2 justify-center"><svg class="w-4 h-4 animate-spin text-gray-300" fill="none" viewBox="0 0 24 24"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8z"></path></svg><span class="text-xs text-gray-400">Loading…</span></div>';
    try {
        const r = await fetch('/Tasks/GetLinks?taskId=' + taskId);
        const links = await r.json();
        count.textContent = links.length ? links.length + ' link' + (links.length !== 1 ? 's' : '') : '';
        if (!links.length) {
            list.innerHTML = '<p class="text-xs text-gray-400 py-1">No links yet.</p>';
        } else {
            list.innerHTML = links.map(l => _renderLinkRow(l)).join('');
        }
    } catch(e) { list.innerHTML = `<p class="text-xs text-red-400 py-1">Could not load links.</p>`; }
}

function _renderLinkRow(l) {
    const displayType = l.isFrom ? l.linkType : (LINK_INVERSE[l.linkType] || l.linkType);
    const colorClass  = LINK_COLORS[displayType] || 'bg-gray-100 text-gray-600';
    const typeLabel   = l.linkedTaskItemType ? `<span class="text-[10px] text-gray-400 font-medium">${_esc(l.linkedTaskItemType)}</span>` : '';
    return `<div class="flex items-center gap-2 group py-1 px-2 rounded-lg hover:bg-gray-50">
        <span class="text-[10px] font-semibold px-2 py-0.5 rounded-full flex-shrink-0 ${colorClass}">${_esc(displayType)}</span>
        <div class="flex-1 min-w-0 flex items-center gap-1.5 overflow-hidden">
            ${typeLabel}
            <span class="text-sm text-gray-700 truncate">#${l.linkedTaskId} ${_esc(l.linkedTaskGoal)}</span>
        </div>
        <span class="text-[10px] text-gray-400 flex-shrink-0">${_esc(l.linkedTaskStatus)}</span>
        <button type="button" onclick="_removeLink(${l.id})"
                class="opacity-0 group-hover:opacity-100 flex-shrink-0 w-5 h-5 text-gray-400 hover:text-red-500 transition-opacity"
                title="Remove link">
            <svg viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clip-rule="evenodd"/></svg>
        </button>
    </div>`;
}

function _linkSearch(q) {
    clearTimeout(_linkSearchTimer);
    const res = document.getElementById('cf-link-results');
    if (!q || q.trim().length < 1) { res.classList.add('hidden'); return; }
    _linkSearchTimer = setTimeout(async () => {
        try {
            const r = await fetch('/Tasks/SearchTasks?q=' + encodeURIComponent(q.trim()));
            const tasks = await r.json();
            if (!tasks.length) { res.innerHTML = '<div class="px-3 py-2 text-xs text-gray-400">No results</div>'; }
            else { res.innerHTML = tasks.map(t => `<div class="px-3 py-2 text-sm cursor-pointer hover:bg-blue-50 flex items-center gap-2" onclick="_selectLinkedTask(${JSON.stringify(t).replace(/</g,'\\u003c')})">
                <span class="text-[10px] text-gray-400 w-8 text-right flex-shrink-0">#${t.id}</span>
                <span class="flex-1 truncate">${_esc(t.goal || '—')}</span>
                ${t.itemType ? `<span class="text-[10px] text-gray-400 flex-shrink-0">${_esc(t.itemType)}</span>` : ''}
            </div>`).join(''); }
            res.classList.remove('hidden');
        } catch { res.classList.add('hidden'); }
    }, 280);
}

function _selectLinkedTask(task) {
    _linkSelectedTask = task;
    document.getElementById('cf-link-search').value = '';
    document.getElementById('cf-link-results').classList.add('hidden');
    const sel = document.getElementById('cf-link-selected');
    sel.classList.remove('hidden');
    sel.classList.add('flex');
    document.getElementById('cf-link-selected-label').textContent = '#' + task.id + ' ' + (task.goal || '—');
}

function _clearLinkSearch() {
    _linkSelectedTask = null;
    document.getElementById('cf-link-search').value = '';
    document.getElementById('cf-link-results').classList.add('hidden');
    const sel = document.getElementById('cf-link-selected');
    sel.classList.add('hidden');
    sel.classList.remove('flex');
}

async function _addLink() {
    if (!_linkSelectedTask) { showToast('Search and select a task first.', 'error'); return; }
    if (!_cvTaskId)          { showToast('No task open.', 'error'); return; }
    const linkType = document.getElementById('cf-link-type').value;
    const token    = document.querySelector('#canvas-form input[name=__RequestVerificationToken]').value;
    const fd = new FormData();
    fd.append('fromTaskId', _cvTaskId);
    fd.append('toTaskId',   _linkSelectedTask.id);
    fd.append('linkType',   linkType);
    fd.append('__RequestVerificationToken', token);
    try {
        const r = await fetch('/Tasks/AddLink', { method:'POST', body: fd });
        if (r.ok) { _clearLinkSearch(); await _loadLinks(_cvTaskId); }
        else { const e = await r.json().catch(()=>({})); showToast(e.message || 'Failed to add link.', 'error'); }
    } catch { showToast('Failed to add link.', 'error'); }
}

async function _removeLink(id) {
    const token = document.querySelector('#canvas-form input[name=__RequestVerificationToken]').value;
    const fd = new FormData();
    fd.append('id', id);
    fd.append('__RequestVerificationToken', token);
    try {
        const r = await fetch('/Tasks/RemoveLink?id=' + id, { method:'POST', body: fd });
        if (r.ok) await _loadLinks(_cvTaskId);
        else showToast('Failed to remove link.', 'error');
    } catch { showToast('Failed to remove link.', 'error'); }
}
// ── 7.3 History tab ──────────────────────────────────────────────────────────
async function _loadHistory(taskId) {
    const list = document.getElementById('cf-history-list');
    list.innerHTML = '<div class="text-gray-400 text-xs py-2">Loading…</div>';
    try {
        const r = await fetch('/Tasks/GetHistory?taskId=' + taskId);
        const entries = await r.json();
        if (!entries.length) {
            list.innerHTML = '<div class="text-gray-400 text-xs py-2">No history yet.</div>';
            return;
        }
        const actionColor = a => ({
            'Created': 'text-green-600', 'Deleted': 'text-red-500',
            'Status Changed': 'text-blue-600', 'Auto-Updated': 'text-violet-600'
        }[a] || 'text-gray-500');
        list.innerHTML = entries.map(e => `
            <div class="flex items-start gap-2 py-1.5 border-b border-gray-50 last:border-0">
                <span class="text-[10px] font-semibold uppercase tracking-wide mt-0.5 w-20 flex-shrink-0 ${actionColor(e.action)}">${_esc(e.action)}</span>
                <div class="flex-1 min-w-0">
                    <p class="text-xs text-gray-700 truncate">${_esc(e.details || '—')}</p>
                    <p class="text-[10px] text-gray-400 mt-0.5">${_esc(e.changedBy)} · ${_esc(e.changedAt)}</p>
                </div>
            </div>`).join('');
    } catch { list.innerHTML = '<div class="text-red-400 text-xs py-2">Failed to load history.</div>'; }
}

// ── 7.2 Copy item URL ─────────────────────────────────────────────────────────
function copyTaskUrl() {
    if (!_cvTaskId) return;
    const url = window.location.origin + '/Tasks?highlight=' + _cvTaskId;
    navigator.clipboard.writeText(url).then(
        () => showToast('Link copied to clipboard!', 'success'),
        () => showToast('Failed to copy link.', 'error')
    );
}

// ── 7.5 Kanban column collapse ────────────────────────────────────────────────
function toggleKanbanCol(btn) {
    const col  = btn.closest('[data-kanban-col]');
    const body = col.querySelector('.kanban-col-body');
    const collapsedEl = col.querySelector('.kanban-col-collapsed');
    const icon = btn.querySelector('.kanban-collapse-icon');
    const isCollapsed = col.dataset.collapsed === 'true';
    const colName = col.dataset.colName || '';

    if (isCollapsed) {
        col.dataset.collapsed = 'false';
        col.classList.remove('w-10'); col.classList.add('w-72');
        body.classList.remove('hidden');
        collapsedEl.classList.add('hidden');
        icon.innerHTML = '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 19l-7-7 7-7m8 14l-7-7 7-7"/>';
        btn.title = 'Collapse column';
    } else {
        col.dataset.collapsed = 'true';
        col.classList.remove('w-72'); col.classList.add('w-10');
        body.classList.add('hidden');
        collapsedEl.classList.remove('hidden');
        icon.innerHTML = '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 5l7 7-7 7M5 5l7 7-7 7"/>';
        btn.title = 'Expand column';
    }

    try {
        const stored = JSON.parse(localStorage.getItem('kanban-collapsed') || '{}');
        stored[colName] = !isCollapsed;
        localStorage.setItem('kanban-collapsed', JSON.stringify(stored));
    } catch {}
}

// Restore kanban collapsed state from localStorage on page load
(function restoreKanbanCollapsed() {
    try {
        const stored = JSON.parse(localStorage.getItem('kanban-collapsed') || '{}');
        document.querySelectorAll('[data-kanban-col]').forEach(col => {
            const name = col.dataset.colName || '';
            if (stored[name]) {
                const btn = col.querySelector('button[onclick*="toggleKanbanCol"]');
                if (btn) toggleKanbanCol(btn);
            }
        });
    } catch {}
})();

// ── 7.4 Board person filter ───────────────────────────────────────────────────
let _bdPersonFilter = '';
function _bdFilterPerson(person) {
    _bdPersonFilter = person;
    // Re-apply by re-rendering current board data
    if (typeof _bdRenderBoard === 'function') _bdRenderBoard();
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 8.1: EasyMDE markdown editor lifecycle
// ─────────────────────────────────────────────────────────────────────────────
let _mdeNotes = null, _mdeAc = null;

function _destroyMde() {
    if (_mdeNotes) { try { _mdeNotes.toTextArea(); } catch {} _mdeNotes = null; }
    if (_mdeAc)    { try { _mdeAc.toTextArea();    } catch {} _mdeAc    = null; }
}

function _mdeSync() {
    if (_mdeNotes) document.getElementById('cf-notes').value = _mdeNotes.value();
    if (_mdeAc)    document.getElementById('cf-ac').value    = _mdeAc.value();
}

function _mdeInit(isEditable) {
    const notesEl   = document.getElementById('cf-notes');
    const acEl      = document.getElementById('cf-ac');
    const notesPrev = document.getElementById('cf-notes-preview');
    const acPrev    = document.getElementById('cf-ac-preview');
    const acSection = document.getElementById('cf-ac-section');

    if (isEditable) {
        // Show textareas, hide previews
        notesPrev.classList.add('hidden');
        acPrev.classList.add('hidden');
        notesEl.classList.remove('hidden');
        acEl.classList.remove('hidden');

        const mdeOpts = (el, ph) => ({
            element: el,
            placeholder: ph,
            spellChecker: false,
            autosave: { enabled: false },
            toolbar: ['bold','italic','strikethrough','|','heading-2','|','unordered-list','ordered-list','|','link','code','|','preview','side-by-side','fullscreen'],
            minHeight: '80px',
            status: false,
        });
        try { _mdeNotes = new EasyMDE(mdeOpts(notesEl, 'Additional context… (Markdown supported)')); } catch {}
        if (!acSection.classList.contains('hidden')) {
            try { _mdeAc = new EasyMDE(mdeOpts(acEl, 'Given… When… Then…')); } catch {}
        }
    } else {
        // Hide textareas (EasyMDE wrappers already gone after _destroyMde in openCanvas)
        notesEl.classList.add('hidden');
        acEl.classList.add('hidden');
        // Render markdown to preview divs
        const mdNotes = notesEl.value.trim();
        const mdAc    = acEl.value.trim();
        if (mdNotes && typeof marked !== 'undefined') {
            notesPrev.innerHTML = marked.parse(mdNotes);
            notesPrev.classList.remove('hidden');
        } else {
            notesPrev.innerHTML = mdNotes ? `<p class="text-gray-600">${_esc(mdNotes)}</p>` : '<p class="text-gray-400 italic text-xs">No description.</p>';
            notesPrev.classList.remove('hidden');
        }
        if (!acSection.classList.contains('hidden')) {
            if (mdAc && typeof marked !== 'undefined') {
                acPrev.innerHTML = marked.parse(mdAc);
                acPrev.classList.remove('hidden');
            } else if (mdAc) {
                acPrev.innerHTML = `<p class="text-gray-600">${_esc(mdAc)}</p>`;
                acPrev.classList.remove('hidden');
            } else {
                acPrev.classList.add('hidden');
            }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 8.2: Attachments
// ─────────────────────────────────────────────────────────────────────────────
function _fmtSize(bytes) {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / 1024 / 1024).toFixed(1) + ' MB';
}

async function _loadAttachments(taskId) {
    const list    = document.getElementById('cf-att-list');
    const cntBadge = document.getElementById('cf-att-count');
    list.innerHTML = '<div class="text-gray-400 text-xs py-1">Loading…</div>';
    try {
        const r = await fetch('/Tasks/GetAttachments?taskId=' + taskId);
        const items = await r.json();
        cntBadge.textContent = items.length || '';
        cntBadge.classList.toggle('hidden', !items.length);
        if (!items.length) {
            list.innerHTML = '<div class="text-gray-400 text-xs py-1 italic">No attachments.</div>';
            return;
        }
        list.innerHTML = items.map(a => `
            <div class="flex items-center gap-2 p-2 rounded-lg bg-gray-50 hover:bg-gray-100 transition-colors group" data-att-id="${a.id}">
                <svg class="w-4 h-4 text-gray-400 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z"/></svg>
                <div class="flex-1 min-w-0">
                    <a href="/Tasks/DownloadAttachment/${a.id}" target="_blank"
                       class="font-medium text-[#094f70] hover:underline truncate block">${_esc(a.fileName)}</a>
                    <span class="text-[10px] text-gray-400">${_fmtSize(a.fileSize)} · ${_esc(a.uploadedBy || '')} · ${_esc(a.uploadedAt)}</span>
                </div>
                <button onclick="deleteAttachment(${a.id}, '${_esc(a.fileName).replace(/'/g, "\\'")}')"
                        class="opacity-0 group-hover:opacity-100 transition-opacity p-1 rounded hover:bg-red-100 hover:text-red-500 text-gray-400"
                        title="Delete">
                    <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/></svg>
                </button>
            </div>`).join('');
    } catch {
        list.innerHTML = '<div class="text-red-400 text-xs py-1">Failed to load attachments.</div>';
    }
}

async function uploadAttachment(input) {
    if (!input.files || !input.files.length || !_cvTaskId) return;
    const attList = document.getElementById('cf-att-list');
    const token   = document.querySelector('[name=__RequestVerificationToken]')?.value ?? '';
    for (const file of input.files) {
        const fd = new FormData();
        fd.append('taskId', _cvTaskId);
        fd.append('file', file);
        const row = document.createElement('div');
        row.className = 'text-gray-400 text-xs py-1';
        row.textContent = 'Uploading ' + file.name + '…';
        attList.prepend(row);
        try {
            const r = await fetch('/Tasks/UploadAttachment', {
                method: 'POST',
                headers: { 'RequestVerificationToken': token },
                body: fd
            });
            const j = await r.json();
            if (!j.success) { showToast(j.error || 'Upload failed.', 'error'); row.remove(); }
            else { row.remove(); }
        } catch { showToast('Upload failed.', 'error'); row.remove(); }
    }
    input.value = '';
    await _loadAttachments(_cvTaskId);
}

async function deleteAttachment(id, name) {
    const ok = await Swal.fire({
        title: 'Delete attachment?',
        text: name,
        icon: 'warning',
        showCancelButton: true,
        confirmButtonColor: '#ef4444',
        cancelButtonColor: '#94a3b8',
        confirmButtonText: 'Delete',
        reverseButtons: true,
        focusCancel: true,
    });
    if (!ok.isConfirmed) return;
    const token = document.querySelector('[name=__RequestVerificationToken]')?.value ?? '';
    try {
        const r = await fetch('/Tasks/DeleteAttachment/' + id, {
            method: 'POST',
            headers: { 'RequestVerificationToken': token }
        });
        const j = await r.json();
        if (j.success) { showToast('Attachment deleted.', 'success'); await _loadAttachments(_cvTaskId); }
        else showToast(j.error || 'Delete failed.', 'error');
    } catch { showToast('Delete failed.', 'error'); }
}

// ─────────────────────────────────────────────────────────────────────────────

function closeCanvas() {
    _destroyMde();
    if (_cvTaskId) srLeaveTask(_cvTaskId);
    backdrop.classList.add('opacity-0','pointer-events-none');
    backdrop.classList.remove('opacity-100');
    panel.classList.add('translate-x-full');
    document.body.style.overflow = '';
}

async function submitCanvas() {
    // Sync owner → hidden field before FormData reads it
    if (CV_ADMIN) {
        document.getElementById('cf-owner').value = [..._ownerSelected].join(', ');
    }
    // Non-admin: cf-owner already locked to CV_ME by _restoreOwner
    _mdeSync(); // sync EasyMDE editor values to underlying textareas before submit

    const form = document.getElementById('canvas-form');
    if (!form.checkValidity()) { form.reportValidity(); return; }
    // Req 4: cannot assign to sprint without an owner
    const _sprintVal  = document.getElementById('cf-sprint-id').value;
    const _ownerVal   = document.getElementById('cf-owner').value?.trim();
    if (_sprintVal && !_ownerVal) {
        showToast('Please assign an owner before adding this task to a sprint.', 'error');
        document.getElementById('cf-owner-trigger')?.scrollIntoView({ behavior: 'smooth', block: 'center' });
        return;
    }
    // Standard rule: only New / To Do may be unowned — starting or finishing work needs an owner
    const _statusVal = document.getElementById('cf-status').value;
    if (['Active','In Progress','Resolved','Done','Closed'].includes(_statusVal) && !_ownerVal) {
        showToast(`Please assign an owner before moving this to ${_statusVal}.`, 'error');
        document.getElementById('cf-owner-trigger')?.scrollIntoView({ behavior: 'smooth', block: 'center' });
        return;
    }
    const btn = document.getElementById('canvas-save-btn');
    const sp  = document.getElementById('canvas-spinner');
    btn.disabled = true; sp.classList.remove('hidden');
    try {
        const r = await fetch('/Tasks/Save', { method:'POST', body: new FormData(form) });
        if (r.status === 409) {
            await Swal.fire({
                icon: 'warning',
                title: 'Conflict',
                text: 'This task was modified by someone else. Reload to see the latest version.',
                showCancelButton: true,
                confirmButtonText: 'Reload',
                cancelButtonText: 'Discard',
            }).then(res => { if (res.isConfirmed) location.reload(); });
            return;
        }
        const j = await r.json();
        if (j.success) {
            const isEdit = !!document.getElementById('cf-id').value;
            if (j.capacityWarning)
                sessionStorage.setItem('__toast', JSON.stringify({ msg: j.capacityWarning, type: 'warning' }));
            else
                sessionStorage.setItem('__toast', JSON.stringify({ msg: isEdit ? 'Task updated successfully.' : 'New task created.', type: 'success' }));
            sessionStorage.setItem('scrollY', window.scrollY);
            window.location.reload();
        } else if (j.openChildren) {
            // Req 7: soft-block — let user force-close if they acknowledge
            const res = await Swal.fire({
                icon: 'warning',
                title: 'Sub-tasks still open',
                text: `${j.openChildren} child item(s) are not yet complete. Close this item anyway?`,
                showCancelButton: true, confirmButtonText: 'Close anyway', cancelButtonText: 'Cancel',
                confirmButtonColor: '#d33'
            });
            if (res.isConfirmed) {
                const fd2 = new FormData(form);
                fd2.append('ForceClose', 'true');
                const r2 = await fetch('/Tasks/Save', { method:'POST', body: fd2 });
                const j2 = await r2.json();
                if (j2.success) {
                    const isEdit = !!document.getElementById('cf-id').value;
                    sessionStorage.setItem('scrollY', window.scrollY);
                    sessionStorage.setItem('__toast', JSON.stringify({ msg: isEdit ? 'Task updated.' : 'Task created.', type: 'success' }));
                    window.location.reload();
                } else {
                    showToast(j2.error ?? 'Could not save task.', 'error');
                }
            }
        } else {
            showToast(j.error ?? 'Could not save task.', 'error');
        }
    } catch (e) { showToast('Save failed: ' + e.message, 'error'); }
    finally { btn.disabled = false; sp.classList.add('hidden'); }
}

document.addEventListener('keydown', e => { if (e.key === 'Escape') closeCanvas(); });

// ─── Watchers ────────────────────────────────────────────────────────────────
let _watchState = { watching: false, count: 0 };

function _renderWatchBtn(names) {
    const btn   = document.getElementById('canvas-watch-btn');
    const label = document.getElementById('canvas-watch-label');
    const cnt   = document.getElementById('canvas-watch-count');
    if (!btn) return;
    label.textContent = _watchState.watching ? 'Watching' : 'Watch';
    btn.classList.toggle('border-blue-300',  _watchState.watching);
    btn.classList.toggle('text-blue-600',    _watchState.watching);
    btn.classList.toggle('bg-blue-50',       _watchState.watching);
    btn.classList.toggle('border-gray-200', !_watchState.watching);
    btn.classList.toggle('text-gray-500',   !_watchState.watching);
    if (_watchState.count > 0) {
        cnt.textContent = _watchState.count;
        cnt.classList.remove('hidden');
    } else cnt.classList.add('hidden');
    btn.title = names && names.length
        ? 'Watching: ' + names.join(', ')
        : 'Watch this task — get notified on status changes and comments';
}

async function _loadWatchers(taskId) {
    try {
        const r = await fetch('/Tasks/GetWatchers?taskId=' + taskId);
        const d = await r.json();
        _watchState = { watching: d.watching, count: d.count };
        _renderWatchBtn((d.watchers || []).map(w => w.name));
    } catch {}
}

async function toggleWatch() {
    if (!_cvTaskId) return;
    const fd = new FormData();
    fd.append('taskId', _cvTaskId);
    fd.append('__RequestVerificationToken', document.querySelector('input[name="__RequestVerificationToken"]').value);
    try {
        const r = await fetch('/Tasks/ToggleWatch', { method: 'POST', body: fd });
        const d = await r.json();
        if (d.success) {
            _watchState = { watching: d.watching, count: d.count };
            _renderWatchBtn(null);
            showToast(d.watching ? 'You are now watching this task.' : 'Stopped watching.', 'success');
            _loadWatchers(_cvTaskId); // refresh tooltip names
        }
    } catch {}
}

// Req 9: label autocomplete — populate datalist on first focus, then filter as user types
(function () {
    let _labelsLoaded = false;
    const labelsInput = document.getElementById('cf-labels');
    if (!labelsInput) return;
    labelsInput.addEventListener('focus', async () => {
        if (_labelsLoaded) return;
        try {
            const r = await fetch('/Tasks/GetLabels');
            const tags = await r.json();
            const dl = document.getElementById('cf-labels-list');
            if (!dl) return;
            dl.innerHTML = tags.map(t => `<option value="${t}">`).join('');
            _labelsLoaded = true;
        } catch { /* ignore */ }
    });
    // Smart: when user finishes typing a token (after comma), suggest from existing labels
    labelsInput.addEventListener('input', () => {
        const val = labelsInput.value;
        const lastComma = val.lastIndexOf(',');
        const current = lastComma >= 0 ? val.slice(lastComma + 1).trimStart() : val;
        const dl = document.getElementById('cf-labels-list');
        if (!dl || !_labelsLoaded) return;
        const prefix = val.slice(0, lastComma >= 0 ? lastComma + 1 : 0);
        const allOpts = [...dl.querySelectorAll('option')].map(o => o.value);
        dl.innerHTML = allOpts
            .filter(o => o.startsWith(current.toLowerCase()))
            .map(o => `<option value="${prefix ? prefix + ' ' : ''}${o}">`)
            .join('');
    });
})();

function toggleDetail(detailId, row) {
    const d = document.getElementById(detailId);
    if (!d) return;
    const hidden = d.classList.contains('hidden');
    d.classList.toggle('hidden', !hidden);
    if (row.dataset.overdue !== 'true') row.classList.toggle('bg-blue-50/60', hidden);
}

// ── Inline status (fixed-position dropdown, not clipped by overflow:hidden) ──
// _wsData is injected inline by the server-rendered script block
const _statusDd = document.getElementById('status-dropdown');
let   _statusDdBtn = null;

function closeStatusDropdown() {
    _statusDd.classList.add('hidden');
    _statusDdBtn = null;
}

function toggleStatusMenu(btn) {
    if (_statusDdBtn === btn) { closeStatusDropdown(); return; }
    closeStatusDropdown();

    const taskId    = btn.dataset.taskId;
    const curStatus = btn.dataset.curStatus;
    const itemType  = btn.dataset.itemType || '';
    const allowed   = ITEM_STATUSES[itemType] || ITEM_STATUSES[''];

    _statusDd.innerHTML = '';
    _wsData.filter(ws => allowed.includes(ws.name)).forEach(ws => {
        const isCur = ws.name === curStatus;
        const item  = document.createElement('button');
        item.type   = 'button';
        item.className = 'w-full text-left px-3 py-1.5 text-xs hover:bg-gray-50 flex items-center gap-2 '
                       + (isCur ? 'font-semibold text-blue-600' : 'text-gray-700');
        item.innerHTML  = `<span class="w-2 h-2 rounded-full flex-shrink-0" style="background:${ws.color}"></span>${ws.name}`
                        + (isCur ? '<span class="ml-auto text-blue-600">✓</span>' : '');
        item.addEventListener('click', () => setTaskStatus(taskId, ws.name));
        _statusDd.appendChild(item);
    });

    const rect = btn.getBoundingClientRect();
    _statusDd.style.top  = (rect.bottom + 4) + 'px';
    _statusDd.style.left = rect.left + 'px';
    _statusDd.classList.remove('hidden');

    // Flip left if overflowing right edge
    const ddRect = _statusDd.getBoundingClientRect();
    if (ddRect.right > window.innerWidth - 8)
        _statusDd.style.left = (rect.right - _statusDd.offsetWidth) + 'px';

    _statusDdBtn = btn;
}

// Standard rule: work needs an owner. When the server answers needOwner,
// this opens an owner picker, saves the choice, and returns the name (or null on cancel).
async function _promptAssignOwner(taskId, goal, statusLabel) {
    const userOpts = (typeof CV_USERS !== 'undefined' ? CV_USERS : []).map(u => `<option value="${u}">${u}</option>`).join('');
    const { value: chosenOwner, isConfirmed } = await Swal.fire({
        title: 'Who will work on this?',
        html: `<p class="text-sm text-gray-600 mb-3"><strong>${goal || 'This task'}</strong> has no owner.<br>Assign an owner to move it to <b>${statusLabel}</b>.</p>
               <select id="swal-owner-sel" class="swal2-input" style="width:90%;margin:0 auto;">
                 <option value="">— Select owner —</option>${userOpts}
               </select>`,
        showCancelButton: true,
        confirmButtonText: 'Assign & Move',
        cancelButtonText: 'Cancel',
        confirmButtonColor: '#2563eb',
        cancelButtonColor: '#94a3b8',
        preConfirm: () => {
            const v = document.getElementById('swal-owner-sel').value;
            if (!v) { Swal.showValidationMessage('Please select an owner.'); return false; }
            return v;
        }
    });
    if (!isConfirmed || !chosenOwner) return null;
    const fd = new FormData();
    fd.append('id', taskId);
    fd.append('owner', chosenOwner);
    fd.append('__RequestVerificationToken', document.querySelector('[name=__RequestVerificationToken]').value);
    const r = await fetch('/Tasks/QuickSetOwner', { method: 'POST', body: fd });
    const d = await r.json();
    if (!d.success) { showToast(d.error || 'Could not set owner.', 'error'); return null; }
    return chosenOwner;
}

async function setTaskStatus(taskId, status) {
    closeStatusDropdown();
    const btn = document.querySelector(`[data-task-id="${taskId}"]`);
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = '<svg class="w-3 h-3 animate-spin" fill="none" viewBox="0 0 24 24"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8z"></path></svg> Saving…';
    }
    const fd = new FormData();
    fd.append('id', taskId);
    fd.append('status', status);
    fd.append('__RequestVerificationToken',
        document.querySelector('[name=__RequestVerificationToken]').value);
    try {
        const r = await fetch('/Tasks/UpdateStatus', { method:'POST', body: fd });
        const j = await r.json();
        if (j.success) { sessionStorage.setItem('scrollY', window.scrollY); window.location.reload(); }
        else if (j.needOwner) {
            const goal = btn?.closest('tr')?.querySelector('.font-medium')?.textContent?.trim() ?? '';
            if (btn) { btn.disabled = false; btn.textContent = status; }
            const owner = await _promptAssignOwner(taskId, goal, status);
            if (owner) {
                const r2 = await fetch('/Tasks/UpdateStatus', { method:'POST', body: fd });
                const j2 = await r2.json();
                if (j2.success) { sessionStorage.setItem('scrollY', window.scrollY); window.location.reload(); return; }
                showToast(j2.error || 'Could not update status.', 'error');
            }
            sessionStorage.setItem('scrollY', window.scrollY); window.location.reload();
        }
        else if (j.openChildren) {
            if (btn) { btn.disabled = false; btn.textContent = status; }
            if (await _confirmForceClose(j.openChildren)) {
                fd.append('ForceClose', 'true');
                const r2 = await fetch('/Tasks/UpdateStatus', { method:'POST', body: fd });
                const j2 = await r2.json();
                if (j2.success) { sessionStorage.setItem('scrollY', window.scrollY); window.location.reload(); return; }
                showToast(j2.error || 'Could not update status.', 'error');
            }
        }
        else {
            showToast(j.error || 'Could not update status.', 'error');
            if (btn) { btn.disabled = false; btn.textContent = status; }
        }
    } catch (e) {
        showToast('Failed: ' + e.message, 'error');
        if (btn) { btn.disabled = false; btn.textContent = status; }
    }
}

// Req 7: shared "children still open" confirmation for the inline/board status paths
async function _confirmForceClose(count) {
    const res = await Swal.fire({
        icon: 'warning',
        title: 'Sub-tasks still open',
        text: `${count} child item(s) are not yet complete. Close this item anyway?`,
        showCancelButton: true, confirmButtonText: 'Close anyway', cancelButtonText: 'Cancel',
        confirmButtonColor: '#d33'
    });
    return res.isConfirmed;
}

document.addEventListener('click', e => {
    if (!e.target.closest('#status-dropdown') && !e.target.closest('[data-task-id]'))
        closeStatusDropdown();
});

document.addEventListener('click', e => {
    if (!e.target.closest('#cf-link-results') && !e.target.closest('#cf-link-search'))
        document.getElementById('cf-link-results')?.classList.add('hidden');
});
document.addEventListener('scroll', closeStatusDropdown, true);

// Restore scroll after reload
window.addEventListener('DOMContentLoaded', () => {
    const y = sessionStorage.getItem('scrollY');
    if (y) { window.scrollTo(0, parseInt(y)); sessionStorage.removeItem('scrollY'); }
});

// ── Kanban drag-and-drop ──────────────────────────────────
let _kDragCard = null, _kDragId = null, _kDropLocked = false;

function handleKanbanDragStart(e) {
    if (_kDropLocked) { e.preventDefault(); return; } // block new drags while confirm is open
    _kDragCard = e.currentTarget;
    _kDragId   = e.currentTarget.dataset.taskId;
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', _kDragId);
    requestAnimationFrame(() => {
        if (_kDragCard) _kDragCard.classList.add('opacity-40', 'scale-95');
    });
}

function handleKanbanDragEnd(e) {
    if (_kDragCard) _kDragCard.classList.remove('opacity-40', 'scale-95');
    document.querySelectorAll('.swimlane-drop').forEach(c =>
        c.classList.remove('ring-2','ring-inset','ring-blue-400','bg-blue-50/40','ring-orange-300','bg-orange-50/30'));
    if (!_kDropLocked) _kDragCard = null;
}

async function handleKanbanDrop(e, dropZone, newStatus, newSwimlane) {
    e.preventDefault();
    dropZone.classList.remove('ring-2','ring-inset','ring-blue-400','bg-blue-50/40','ring-orange-300','bg-orange-50/30');
    if (!_kDragCard || _kDropLocked) return;

    const card       = _kDragCard;
    const taskId     = _kDragId;
    const srcZone    = card.closest('.swimlane-drop');
    const srcColEl   = card.closest('[data-kanban-col]');
    const destColEl  = dropZone.closest('[data-kanban-col]');
    _kDragCard = null; _kDragId = null;
    card.classList.remove('opacity-40', 'scale-95');
    _kDropLocked = true;

    if (srcZone === dropZone) { _kDropLocked = false; return; } // same zone — no-op

    const oldStatus  = srcZone?.dataset.colStatus ?? '';
    const oldSwimlane = card.dataset.swimlane ?? 'Default';

    // WIP limit check on destination column
    const wipLimit   = parseInt(destColEl?.dataset.wipLimit || '0') || 0;
    const destCount  = destColEl?.querySelectorAll('.kanban-card').length ?? 0;
    const wipWarning = (wipLimit > 0 && destCount >= wipLimit)
        ? `<br><span style="color:#ef4444;font-size:.8rem;font-weight:600">⚠ WIP limit: ${destCount}/${wipLimit} — column is full</span>`
        : '';

    // Build confirm message
    const taskGoal = card.querySelector('p')?.textContent?.trim() ?? 'this task';
    const short    = taskGoal.length > 50 ? taskGoal.slice(0, 47) + '…' : taskGoal;
    const statusChange  = oldStatus !== newStatus
        ? `<span style="color:#94a3b8;font-size:.8rem">${oldStatus || '—'}</span> <span style="color:#094f70;font-weight:700">→</span> <span style="font-weight:600">${newStatus}</span>`
        : `<span style="color:#64748b">Status: ${newStatus}</span>`;
    const laneChange    = oldSwimlane !== newSwimlane
        ? `<br><span style="color:#94a3b8;font-size:.8rem">Lane: ${oldSwimlane} → <strong>${newSwimlane}</strong></span>`
        : '';

    const confirmed = await Swal.fire({
        title: 'Move task?',
        html: `<span style="font-size:.85rem;color:#475569">"${short}"</span><br><br>${statusChange}${laneChange}${wipWarning}`,
        icon: wipWarning ? 'warning' : 'question',
        showCancelButton: true,
        confirmButtonColor: '#094f70',
        cancelButtonColor: '#94a3b8',
        confirmButtonText: 'Move',
        cancelButtonText: 'Cancel',
        reverseButtons: true,
        focusCancel: true,
    });
    _kDropLocked = false;
    if (!confirmed.isConfirmed) return;

    // Move card in DOM
    dropZone.querySelector('.kanban-empty')?.classList.add('hidden');
    card.dataset.swimlane = newSwimlane;
    dropZone.appendChild(card);

    if (srcZone?.querySelectorAll('.kanban-card').length === 0)
        srcZone.querySelector('.kanban-empty')?.classList.remove('hidden');

    _kanbanRefreshCount(srcColEl);
    _kanbanRefreshCount(destColEl);

    // Persist to server
    const fd = new FormData();
    fd.append('id', taskId);
    fd.append('status', newStatus);
    fd.append('swimlane', newSwimlane === 'Default' ? '' : newSwimlane);
    fd.append('__RequestVerificationToken',
        document.querySelector('[name=__RequestVerificationToken]').value);
    try {
        const r = await fetch('/Tasks/UpdateStatus', { method: 'POST', body: fd });
        const j = await r.json();
        if (!j.success) {
            if (j.needOwner) {
                const owner = await _promptAssignOwner(taskId, taskGoal, newStatus);
                if (owner) {
                    const r2 = await fetch('/Tasks/UpdateStatus', { method: 'POST', body: fd });
                    const j2 = await r2.json();
                    if (j2.success) { showToast(`Assigned to ${owner} and moved to ${newStatus}.`, 'success'); return; }
                    showToast('Update failed: ' + (j2.error || ''), 'error');
                }
                location.reload(); // revert the optimistic card move
            } else if (j.openChildren) {
                if (await _confirmForceClose(j.openChildren)) {
                    fd.append('ForceClose', 'true');
                    const r2 = await fetch('/Tasks/UpdateStatus', { method: 'POST', body: fd });
                    const j2 = await r2.json();
                    if (j2.success) return;
                    showToast('Update failed: ' + (j2.error || ''), 'error');
                }
                location.reload(); // revert the optimistic card move
            } else {
                showToast('Update failed: ' + j.error, 'error');
                location.reload();
            }
        }
    } catch (err) { showToast('Network error: ' + err.message, 'error'); location.reload(); }
}

function _kanbanRefreshCount(colEl) {
    if (!colEl) return;
    const allCards = colEl.querySelectorAll('.kanban-card');
    const n        = allCards.length;
    const wip      = parseInt(colEl.dataset.wipLimit || '0') || 0;

    const badge = colEl.querySelector('.kanban-count');
    if (badge) {
        badge.textContent = wip > 0 ? `${n} / ${wip}` : String(n);
        const over  = wip > 0 && n >= wip;
        const near  = wip > 0 && !over && n >= Math.ceil(wip * 0.8);
        badge.className = badge.className.replace(/\b(bg|text)-\S+/g, '');
        badge.classList.add(...(over ? ['bg-red-100','text-red-700'] : near ? ['bg-amber-100','text-amber-700'] : ['bg-gray-100','text-gray-500']));
        colEl.querySelector('.kanban-col-header')?.classList.toggle('bg-red-50/30', over);
        colEl.classList.toggle('border-red-300', over);
        colEl.classList.toggle('border-gray-200', !over);
    }

    // Update swimlane section counts
    const expN = colEl.querySelector('.swimlane-drop[data-swimlane="Expedite"]')?.querySelectorAll('.kanban-card').length ?? 0;
    const defN = colEl.querySelector('.swimlane-drop[data-swimlane="Default"]')?.querySelectorAll('.kanban-card').length ?? 0;
    const expBadge = colEl.querySelector('.swimlane-exp-count');
    const defBadge = colEl.querySelector('.swimlane-def-count');
    if (expBadge) expBadge.textContent = expN;
    if (defBadge) defBadge.textContent = defN;
}

// ─── SignalR live update handlers (override globals set in _Layout) ────────────
window.onTaskUpdated = function(data) {
    // Update table row status badge
    document.querySelectorAll(`[data-task-id="${data.id}"] [data-status-badge]`).forEach(el => {
        el.textContent = data.status;
    });
    // If canvas is open for this task, show a subtle "Updated by X" banner
    if (_cvTaskId === data.id && data.updatedBy && data.updatedBy !== CV_ME) {
        let banner = document.getElementById('cv-updated-banner');
        if (!banner) {
            banner = document.createElement('div');
            banner.id = 'cv-updated-banner';
            banner.className = 'mx-6 mt-3 px-3 py-2 bg-amber-50 border border-amber-200 rounded-lg text-xs text-amber-700 flex items-center gap-2';
            document.getElementById('canvas-form')?.prepend(banner);
        }
        banner.innerHTML = `<svg class="w-4 h-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/></svg> Updated by <b>${_esc(data.updatedBy)}</b> — refresh to see changes`;
        banner.classList.remove('hidden');
    }
};
window.onCommentAdded = function(taskId) {
    if (_cvTaskId === taskId) _loadComments(taskId);
};
window.onLinkUpdated = function(taskId) {
    if (_cvTaskId === taskId) _loadLinks(taskId);
};
// ─────────────────────────────────────────────────────────────────────────────

// ── Bulk selection (table view) ───────────────────────────────────────────────
let _bulkSelected = new Set();
let _bulkLastIdx  = -1;

function _bulkToken() {
    return document.querySelector('#tbl-aft input[name="__RequestVerificationToken"]')?.value
        ?? document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
}

function bulkSelectAll(cb) {
    document.querySelectorAll('.bl-row-check').forEach(c => {
        c.checked = cb.checked;
        const id = parseInt(c.dataset.id);
        if (cb.checked) _bulkSelected.add(id); else _bulkSelected.delete(id);
    });
    _bulkUpdateBar();
}

function bulkCheckChange(event) {
    const clicked = event.target;
    const checks  = [...document.querySelectorAll('.bl-row-check')];
    const curIdx  = checks.indexOf(clicked);
    if (event.shiftKey && _bulkLastIdx >= 0 && curIdx >= 0) {
        const lo = Math.min(curIdx, _bulkLastIdx), hi = Math.max(curIdx, _bulkLastIdx);
        for (let i = lo; i <= hi; i++) {
            checks[i].checked = clicked.checked;
            const id = parseInt(checks[i].dataset.id);
            if (clicked.checked) _bulkSelected.add(id); else _bulkSelected.delete(id);
        }
    } else {
        const id = parseInt(clicked.dataset.id);
        if (clicked.checked) _bulkSelected.add(id); else _bulkSelected.delete(id);
    }
    if (curIdx >= 0) _bulkLastIdx = curIdx;
    const allCk = checks.length > 0 && checks.every(c => c.checked);
    const selAll = document.getElementById('bl-select-all');
    if (selAll) selAll.checked = allCk;
    _bulkUpdateBar();
}

function _bulkUpdateBar() {
    const bar = document.getElementById('bulk-bar');
    const cnt = document.getElementById('bulk-count');
    if (!bar) return;
    if (_bulkSelected.size > 0) {
        bar.classList.remove('hidden');
        cnt.textContent = _bulkSelected.size + ' selected';
    } else {
        bar.classList.add('hidden');
        ['bulk-status-sel','bulk-owner-sel','bulk-sprint-sel','bulk-priority-sel'].forEach(id => {
            const el = document.getElementById(id);
            if (el) el.selectedIndex = 0;
        });
    }
}

function bulkClearSelection() {
    _bulkSelected.clear();
    document.querySelectorAll('.bl-row-check').forEach(c => c.checked = false);
    const selAll = document.getElementById('bl-select-all');
    if (selAll) selAll.checked = false;
    _bulkUpdateBar();
}

// Appended to bulk toasts when some tasks were skipped for lack of permission
function _deniedNote(j) {
    return j.denied > 0 ? ` ${j.denied} skipped — no permission.` : '';
}

async function bulkDelete() {
    if (!_bulkSelected.size) return;
    const { isConfirmed } = await Swal.fire({
        title: 'Move to Recycle Bin?',
        html: _bulkSelected.size + ' task(s) will be moved to the Recycle Bin.',
        icon: 'warning', showCancelButton: true,
        confirmButtonText: 'Move to Bin', confirmButtonColor: '#ef4444'
    });
    if (!isConfirmed) return;
    try {
        const r = await fetch('/Tasks/BulkDelete', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': _bulkToken() },
            body: JSON.stringify([..._bulkSelected])
        });
        const j = await r.json();
        if (j.success) { showToast(j.count + ' task(s) moved to Bin.' + _deniedNote(j), j.denied > 0 ? 'warning' : 'success'); setTimeout(() => location.reload(), 900); }
        else showToast(j.error || 'Failed.', 'error');
    } catch { showToast('Failed.', 'error'); }
}

async function bulkChangeStatus(status) {
    if (!_bulkSelected.size || !status) return;
    const sel = document.getElementById('bulk-status-sel');
    try {
        const r = await fetch('/Tasks/BulkChangeStatus', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': _bulkToken() },
            body: JSON.stringify({ ids: [..._bulkSelected], status })
        });
        const j = await r.json();
        if (j.success) {
            let msg = 'Status updated for ' + j.count + ' task(s).' + _deniedNote(j);
            if (j.noOwner > 0) msg += ` ${j.noOwner} skipped — no owner assigned.`;
            showToast(msg, (j.denied > 0 || j.noOwner > 0) ? 'warning' : 'success');
            setTimeout(() => location.reload(), 900);
        }
        else { showToast(j.error || 'Failed.', 'error'); if (sel) sel.selectedIndex = 0; }
    } catch { showToast('Failed.', 'error'); if (sel) sel.selectedIndex = 0; }
}

async function bulkChangeOwner(owner) {
    if (!_bulkSelected.size) return;
    const sel = document.getElementById('bulk-owner-sel');
    try {
        const r = await fetch('/Tasks/BulkChangeOwner', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': _bulkToken() },
            body: JSON.stringify({ ids: [..._bulkSelected], owner })
        });
        const j = await r.json();
        if (j.success) { showToast('Assignee updated for ' + j.count + ' task(s).' + _deniedNote(j), j.denied > 0 ? 'warning' : 'success'); setTimeout(() => location.reload(), 900); }
        else { showToast(j.error || 'Failed.', 'error'); if (sel) sel.selectedIndex = 0; }
    } catch { showToast('Failed.', 'error'); if (sel) sel.selectedIndex = 0; }
}

async function bulkAssignSprint(sprintId) {
    if (!_bulkSelected.size) return;
    const sel = document.getElementById('bulk-sprint-sel');
    try {
        const r = await fetch('/Tasks/BulkAssignSprint', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': _bulkToken() },
            body: JSON.stringify({ ids: [..._bulkSelected], sprintId })
        });
        const j = await r.json();
        if (j.success) {
            const msg = (j.skipped > 0
                ? `Sprint assigned for ${j.count} task(s). ${j.skipped} skipped — no owner assigned.`
                : `Sprint assigned for ${j.count} task(s).`) + _deniedNote(j);
            showToast(msg, (j.skipped > 0 || j.denied > 0) ? 'warning' : 'success');
            setTimeout(() => location.reload(), 1200);
        } else { showToast(j.error || 'Failed.', 'error'); if (sel) sel.selectedIndex = 0; }
    } catch { showToast('Failed.', 'error'); if (sel) sel.selectedIndex = 0; }
}

async function bulkChangePriority(priority) {
    if (!_bulkSelected.size) return;
    const sel = document.getElementById('bulk-priority-sel');
    try {
        const r = await fetch('/Tasks/BulkChangePriority', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': _bulkToken() },
            body: JSON.stringify({ ids: [..._bulkSelected], priority })
        });
        const j = await r.json();
        if (j.success) { showToast('Priority updated for ' + j.count + ' task(s).' + _deniedNote(j), j.denied > 0 ? 'warning' : 'success'); setTimeout(() => location.reload(), 900); }
        else { showToast(j.error || 'Failed.', 'error'); if (sel) sel.selectedIndex = 0; }
    } catch { showToast('Failed.', 'error'); if (sel) sel.selectedIndex = 0; }
}
// ─────────────────────────────────────────────────────────────────────────────
