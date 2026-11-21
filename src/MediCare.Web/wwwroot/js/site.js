/* ==========================================================================
   MediCare — client-side behaviour
   Vanilla JavaScript; Bootstrap handles dropdowns, modals and collapse.
   ========================================================================== */
(function () {
    'use strict';

    var csrfToken = (document.querySelector('meta[name="csrf-token"]') || {}).content || '';

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function formatDate(value) {
        var date = new Date(value);
        if (isNaN(date.getTime())) return '';
        return date.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
    }

    function formatPercent(value) {
        var number = Number(value);
        if (!isFinite(number)) number = 0;
        return number.toFixed(1) + '%';
    }

    function toast(message, isError) {
        if (!message) return;
        var stack = document.querySelector('.mc-toast-stack');
        if (!stack) {
            stack = document.createElement('div');
            stack.className = 'mc-toast-stack';
            document.body.appendChild(stack);
        }
        var item = document.createElement('div');
        item.className = 'mc-toast' + (isError ? ' mc-toast--error' : '');
        item.innerHTML = '<span aria-hidden="true">' + (isError ? '⚠' : '✓') + '</span><span>' + escapeHtml(message) + '</span>';
        stack.appendChild(item);
        window.setTimeout(function () {
            item.classList.add('fade-out');
            window.setTimeout(function () { item.remove(); }, 250);
        }, 4000);
    }

    /* ----------------------------------------------------------------------
       Mobile sidebar
       ---------------------------------------------------------------------- */
    function initSidebar() {
        var toggle = document.querySelector('[data-mc-sidebar-toggle]');
        var backdrop = document.querySelector('[data-mc-sidebar-backdrop]');
        if (!toggle) return;

        function setOpen(open) {
            document.body.classList.toggle('mc-sidebar-open', open);
            toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        }

        toggle.addEventListener('click', function () {
            setOpen(!document.body.classList.contains('mc-sidebar-open'));
        });

        if (backdrop) {
            backdrop.addEventListener('click', function () { setOpen(false); });
        }

        document.addEventListener('keydown', function (event) {
            if (event.key === 'Escape') setOpen(false);
        });

        document.querySelectorAll('.mc-sidebar-nav a').forEach(function (link) {
            link.addEventListener('click', function () {
                if (window.innerWidth < 992) setOpen(false);
            });
        });
    }

    /* ----------------------------------------------------------------------
       Password visibility + strength meter
       ---------------------------------------------------------------------- */
    function scorePassword(value) {
        if (!value) return 0;
        var score = 0;
        if (value.length >= 8) score++;
        if (/[a-z]/.test(value) && /[A-Z]/.test(value)) score++;
        if (/\d/.test(value)) score++;
        if (/[^A-Za-z0-9]/.test(value)) score++;
        return Math.max(1, Math.min(4, score));
    }

    function initPasswords() {
        document.querySelectorAll('[data-mc-password]').forEach(function (input) {
            var meter = input.parentElement ? input.parentElement.querySelector('[data-mc-password-meter]') : null;
            if (!meter) {
                var sibling = input.nextElementSibling;
                if (sibling && sibling.hasAttribute('data-mc-password-meter')) meter = sibling;
            }
            if (meter) {
                var update = function () { meter.setAttribute('data-score', String(scorePassword(input.value))); };
                input.addEventListener('input', update);
                update();
            }
        });

        document.querySelectorAll('[data-mc-password-toggle]').forEach(function (button) {
            button.addEventListener('click', function () {
                var group = button.closest('.input-group');
                var input = group ? group.querySelector('[data-mc-password]') : null;
                if (!input) return;
                var reveal = input.type === 'password';
                input.type = reveal ? 'text' : 'password';
                button.setAttribute('aria-label', reveal ? 'Hide password' : 'Show password');
            });
        });
    }

    /* ----------------------------------------------------------------------
       Confirmation dialogs for destructive actions
       ---------------------------------------------------------------------- */
    function initConfirmations() {
        document.querySelectorAll('form[data-confirm]').forEach(function (form) {
            form.addEventListener('submit', function (event) {
                if (form.dataset.confirmed === 'true') return;
                if (!window.confirm(form.getAttribute('data-confirm'))) {
                    event.preventDefault();
                } else {
                    form.dataset.confirmed = 'true';
                }
            });
        });
    }

    /* ----------------------------------------------------------------------
       Notification bell
       ---------------------------------------------------------------------- */
    function initNotifications() {
        var root = document.querySelector('[data-mc-notifications]');
        if (!root) return;

        var dot = root.querySelector('[data-mc-unread]');
        var label = root.querySelector('[data-mc-unread-label]');
        var list = root.querySelector('[data-mc-notification-list]');
        if (!list) return;

        function setCount(count) {
            var value = Number(count) || 0;
            if (dot) {
                dot.textContent = value > 99 ? '99+' : String(value);
                dot.classList.toggle('is-visible', value > 0);
            }
            if (label) label.textContent = value + ' new';
        }

        function render(items) {
            if (!Array.isArray(items) || items.length === 0) {
                list.innerHTML = '<li class="mc-notification-item text-secondary small">You have no notifications yet.</li>';
                return;
            }

            list.innerHTML = items.slice(0, 20).map(function (item) {
                var unread = !item.isRead;
                return '<li class="mc-notification-item' + (unread ? ' is-unread' : '') + '"' +
                    (unread ? ' data-mc-notification-id="' + escapeHtml(item.id) + '"' : '') + '>' +
                    '<strong>' + escapeHtml(item.title) + '</strong>' +
                    '<p>' + escapeHtml(item.message) + '</p>' +
                    '<time>' + escapeHtml(formatDate(item.createdAtUtc)) + '</time>' +
                    '</li>';
            }).join('');

            list.querySelectorAll('[data-mc-notification-id]').forEach(function (node) {
                node.addEventListener('click', function () { markRead(node, list); });
            });
        }

        function markRead(node, listEl) {
            var id = node.getAttribute('data-mc-notification-id');
            if (!id) return;
            fetch('/Notification/MarkAsRead?id=' + encodeURIComponent(id), {
                method: 'POST',
                headers: csrfToken ? { 'RequestVerificationToken': csrfToken } : {}
            }).then(function (response) {
                if (!response.ok) throw new Error('mark read failed');
                node.classList.remove('is-unread');
                node.removeAttribute('data-mc-notification-id');
                var remaining = listEl.querySelectorAll('[data-mc-notification-id]').length;
                setCount(remaining);
            }).catch(function () { /* non-critical: leave the item as unread */ });
        }

        fetch('/Notification/UnreadCount', { credentials: 'same-origin' })
            .then(function (response) { return response.ok ? response.json() : Promise.reject(); })
            .then(function (data) { setCount(data.unreadCount); })
            .catch(function () { setCount(0); });

        fetch('/Notification', { credentials: 'same-origin' })
            .then(function (response) { return response.ok ? response.json() : Promise.reject(); })
            .then(render)
            .catch(function () {
                list.innerHTML = '<li class="mc-notification-item text-secondary small">Notifications are unavailable right now.</li>';
            });

        root.addEventListener('shown.bs.dropdown', function () {
            fetch('/Notification', { credentials: 'same-origin' })
                .then(function (response) { return response.ok ? response.json() : Promise.reject(); })
                .then(render)
                .catch(function () { /* keep the previous list */ });
        });
    }

    /* ----------------------------------------------------------------------
       Admin analytics panels
       ---------------------------------------------------------------------- */
    function initAnalytics() {
        var container = document.querySelector('[data-mc-analytics]');
        if (!container) return;

        var form = document.querySelector('[data-mc-analytics-form]');
        var fromInput = document.querySelector('[data-mc-analytics-from]');
        var toInput = document.querySelector('[data-mc-analytics-to]');

        function isoDate(date) {
            var offset = date.getTimezoneOffset();
            return new Date(date.getTime() - offset * 60000).toISOString().slice(0, 10);
        }

        if (fromInput && toInput) {
            var end = new Date();
            var start = new Date();
            start.setDate(start.getDate() - 30);
            fromInput.value = isoDate(start);
            toInput.value = isoDate(end);
        }

        function panel(name) { return container.querySelector('[data-mc-panel="' + name + '"]'); }
        function loading(name) {
            var target = panel(name);
            if (target) target.innerHTML = '<p class="mc-hint mb-0">Loading…</p>';
        }
        function failed(name) {
            var target = panel(name);
            if (target) target.innerHTML = '<p class="mc-hint mb-0">No data available for this range.</p>';
        }

        function rangeParams() {
            var from = fromInput && fromInput.value ? fromInput.value + 'T00:00:00Z' : '';
            var to = toInput && toInput.value ? toInput.value + 'T23:59:59Z' : '';
            return '?startDateUtc=' + encodeURIComponent(from) + '&endDateUtc=' + encodeURIComponent(to);
        }

        function getJson(url) {
            return fetch(url + rangeParams(), { credentials: 'same-origin' })
                .then(function (response) { return response.ok ? response.json() : Promise.reject(); });
        }

        function renderAppointments(data) {
            var target = panel('appointments');
            if (!target) return;
            if (!data) return failed('appointments');
            var total = Number(data.totalAppointments) || 0;
            target.innerHTML =
                '<div class="d-flex flex-wrap gap-2 mb-3">' +
                    '<span class="mc-badge mc-badge--primary">' + total + ' total</span>' +
                    '<span class="mc-badge mc-badge--success">' + formatPercent(data.completionRate) + ' completed</span>' +
                    '<span class="mc-badge mc-badge--danger">' + formatPercent(data.cancellationRate) + ' cancelled</span>' +
                    '<span class="mc-badge mc-badge--warning">' + formatPercent(data.noShowRate) + ' no-show</span>' +
                '</div>' +
                '<div class="mc-progress"><span style="width:' + Math.max(0, Math.min(100, Number(data.completionRate) || 0)) + '%"></span></div>' +
                '<p class="mc-hint small mt-2 mb-0">Completion rate across the selected period.</p>';
        }

        function renderDoctors(items) {
            var target = panel('doctors');
            if (!target) return;
            if (!Array.isArray(items) || items.length === 0) return failed('doctors');
            target.innerHTML = items.map(function (item) {
                var rate = Math.max(0, Math.min(100, Number(item.completionRate) || 0));
                return '<div class="mb-3">' +
                    '<div class="d-flex justify-content-between gap-2 mb-1">' +
                        '<span class="small fw-semibold">' + escapeHtml(item.doctorName) + '</span>' +
                        '<span class="mc-hint small">' + (Number(item.totalAppointments) || 0) + ' · ' + formatPercent(item.completionRate) + '</span>' +
                    '</div>' +
                    '<div class="mc-progress"><span style="width:' + rate + '%"></span></div>' +
                '</div>';
            }).join('');
        }

        function renderPeak(items) {
            var target = panel('peak');
            if (!target) return;
            if (!Array.isArray(items) || items.length === 0) return failed('peak');
            var max = Math.max.apply(null, items.map(function (item) { return Number(item.appointmentCount) || 0; }).concat([1]));
            target.innerHTML = '<div class="mc-bars" style="height:110px;">' + items.map(function (item) {
                var count = Number(item.appointmentCount) || 0;
                var height = (100 * count / max).toFixed(1);
                var hour = Number(item.hour) || 0;
                return '<div class="mc-bar-item" title="' + count + ' appointments">' +
                    '<div class="mc-bar-track"><div class="mc-bar-fill" style="height:' + height + '%"></div></div>' +
                    '<span class="mc-bar-label">' + (hour < 10 ? '0' + hour : hour) + ':00</span>' +
                '</div>';
            }).join('') + '</div>';
        }

        function renderSpecializations(items) {
            var target = panel('specializations');
            if (!target) return;
            if (!Array.isArray(items) || items.length === 0) return failed('specializations');
            var max = Math.max.apply(null, items.map(function (item) { return Number(item.appointmentCount) || 0; }).concat([1]));
            target.innerHTML = items.map(function (item) {
                var count = Number(item.appointmentCount) || 0;
                return '<div class="mb-3">' +
                    '<div class="d-flex justify-content-between gap-2 mb-1">' +
                        '<span class="small fw-semibold">' + escapeHtml(item.specializationName) + '</span>' +
                        '<span class="mc-hint small">' + count + '</span>' +
                    '</div>' +
                    '<div class="mc-progress"><span style="width:' + (100 * count / max).toFixed(1) + '%"></span></div>' +
                '</div>';
            }).join('');
        }

        function load() {
            ['appointments', 'doctors', 'peak', 'specializations'].forEach(loading);

            getJson(container.getAttribute('data-appointments-url'))
                .then(renderAppointments).catch(function () { failed('appointments'); });
            getJson(container.getAttribute('data-doctors-url'))
                .then(renderDoctors).catch(function () { failed('doctors'); });
            getJson(container.getAttribute('data-peak-url'))
                .then(renderPeak).catch(function () { failed('peak'); });
            getJson(container.getAttribute('data-specializations-url'))
                .then(renderSpecializations).catch(function () { failed('specializations'); });
        }

        if (form) {
            form.addEventListener('submit', function (event) {
                event.preventDefault();
                load();
            });
        }
        load();
    }

    function ready(fn) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', fn);
        } else {
            fn();
        }
    }

    ready(function () {
        initSidebar();
        initPasswords();
        initConfirmations();
        initNotifications();
        initAnalytics();
    });
})();
