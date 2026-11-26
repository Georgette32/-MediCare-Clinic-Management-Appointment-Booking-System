// MediCare site behaviour. Inline script and inline event handlers are not allowed by the Content-Security-Policy,
// so pages mark behaviour with data attributes and this file wires it up.
(function () {
    "use strict";

    // data-confirm="question" on a button or form: ask before continuing; cancel stops the click or submit.
    document.addEventListener("click", function (event) {
        if (!(event.target instanceof Element)) return;
        var button = event.target.closest("button[data-confirm], input[type=submit][data-confirm]");
        if (button && !window.confirm(button.getAttribute("data-confirm"))) {
            event.preventDefault();
        }
    });

    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (form instanceof HTMLFormElement && form.hasAttribute("data-confirm") && !window.confirm(form.getAttribute("data-confirm"))) {
            event.preventDefault();
        }
    });

    // data-print on a button: print the page.
    document.addEventListener("click", function (event) {
        if (event.target instanceof Element && event.target.closest("[data-print]")) {
            window.print();
        }
    });

    // data-close-window on a button: close this window (works when the window was opened by a script).
    document.addEventListener("click", function (event) {
        if (event.target instanceof Element && event.target.closest("[data-close-window]")) {
            window.close();
        }
    });

    // Popovers built on <details> (the notification bell, "Register walk-in"): the summary opens and closes them;
    // a click outside or Escape closes them too, and only one is open at a time.
    var popovers = "details.mc-bell, details.mc-pop, details.mc-menu";
    document.addEventListener("click", function (event) {
        var target = event.target instanceof Node ? event.target : null;
        document.querySelectorAll(popovers + "[open]").forEach(function (open) {
            if (!target || !open.contains(target)) open.removeAttribute("open");
        });
    });
    document.addEventListener("keydown", function (event) {
        if (event.key !== "Escape") return;
        document.querySelectorAll(popovers + "[open]").forEach(function (open) {
            open.removeAttribute("open");
            var summary = open.querySelector("summary");
            if (summary) summary.focus();
        });
    });
    document.addEventListener("toggle", function (event) {
        var details = event.target;
        if (!(details instanceof HTMLDetailsElement) || !details.open || !details.matches(popovers)) return;
        var field = details.querySelector("input:not([type=hidden]), select, textarea");
        if (field && details.matches(".mc-pop")) field.focus();
    }, true);

    // Filter a list in place. Inside [data-filter-scope]: buttons [data-filter-chip="value"] pick a status
    // ("" = all), an input [data-filter-text] matches words, and items [data-filter-item] carry data-status and
    // data-text. [data-filter-empty] shows when nothing matches. Works without a reload; the server list is unchanged.
    document.querySelectorAll("[data-filter-scope]").forEach(function (scope) {
        var chips = scope.querySelectorAll("[data-filter-chip]");
        var text = scope.querySelector("[data-filter-text]");
        var items = scope.querySelectorAll("[data-filter-item]");
        var empty = scope.querySelector("[data-filter-empty]");
        var status = "";
        // A folded group opens while a filter matches something inside it, then returns to how it was.
        var groups = scope.querySelectorAll("[data-filter-group]");
        var openBefore = null;
        function applyGroups(filtering) {
            if (filtering && !openBefore) openBefore = Array.prototype.map.call(groups, function (group) { return group.open; });
            groups.forEach(function (group, index) {
                var has = !!group.querySelector("[data-filter-item]:not([hidden])");
                group.hidden = !has;
                if (filtering) group.open = has;
                else if (openBefore) group.open = openBefore[index];
            });
            if (!filtering) openBefore = null;
        }
        function apply() {
            var words = (text && text.value || "").trim().toLowerCase().split(/\s+/).filter(Boolean);
            var shown = 0;
            items.forEach(function (item) {
                var haystack = (item.getAttribute("data-text") || "").toLowerCase();
                var match = (!status || item.getAttribute("data-status") === status)
                    && words.every(function (word) { return haystack.indexOf(word) !== -1; });
                item.hidden = !match;
                if (match) shown++;
            });
            applyGroups(!!status || words.length > 0);
            if (empty) empty.hidden = shown !== 0;
        }
        chips.forEach(function (chip) {
            chip.addEventListener("click", function () {
                status = chip.getAttribute("data-filter-chip") || "";
                chips.forEach(function (other) { other.setAttribute("aria-pressed", other === chip ? "true" : "false"); });
                apply();
            });
        });
        if (text) text.addEventListener("input", apply);
    });

    // Validation without the browser's bubbles. Forms that use jQuery validation already opt out (it sets novalidate);
    // every other form keeps its HTML rules (required, type, pattern, min/max) but shows the clinic's own inline message
    // under the field. data-msg on a field overrides the wording.
    function messageFor(field) {
        var v = field.validity;
        if (field.getAttribute("data-msg")) return field.getAttribute("data-msg");
        if (v.valueMissing) {
            if (field.type === "checkbox") return "Tick this box to continue.";
            if (field.tagName === "SELECT" || field.type === "radio") return "Choose an option.";
            return "This field is required.";
        }
        if (v.typeMismatch) return field.type === "email" ? "Enter a valid email address." : field.type === "url" ? "Enter a valid web address." : "Enter a valid value.";
        if (v.patternMismatch) return field.title || "Use the format shown.";
        if (v.tooShort) return "Use at least " + field.minLength + " characters.";
        if (v.tooLong) return "Use at most " + field.maxLength + " characters.";
        if (v.rangeUnderflow) return "Enter " + field.min + " or more.";
        if (v.rangeOverflow) return "Enter " + field.max + " or less.";
        if (v.badInput) return "Enter a number.";
        if (v.stepMismatch) return "Enter a valid value.";
        return field.validationMessage;
    }
    function messageAnchor(field) {
        return field.closest(".input-group, .form-check, .mc-search-field, .mc-daynav") || field;
    }
    function clearFieldError(field) {
        field.classList.remove("mc-invalid");
        field.removeAttribute("aria-invalid");
        var anchor = messageAnchor(field);
        var next = anchor.nextElementSibling;
        if (next && next.classList.contains("mc-field-msg")) {
            next.remove();
            var describedBy = (field.getAttribute("aria-describedby") || "").split(" ").filter(function (id) { return id && id !== next.id; });
            if (describedBy.length) field.setAttribute("aria-describedby", describedBy.join(" ")); else field.removeAttribute("aria-describedby");
        }
    }
    var fieldMsgCount = 0;
    document.addEventListener("invalid", function (event) {
        var field = event.target;
        if (!(field instanceof HTMLElement) || !field.form || field.form.hasAttribute("data-native-validation")) return;
        event.preventDefault();
        clearFieldError(field);
        var msg = document.createElement("span");
        msg.className = "mc-field-msg";
        msg.id = "mc-field-msg-" + (++fieldMsgCount);
        msg.setAttribute("role", "alert");
        msg.textContent = messageFor(field);
        messageAnchor(field).insertAdjacentElement("afterend", msg);
        field.classList.add("mc-invalid");
        field.setAttribute("aria-invalid", "true");
        field.setAttribute("aria-describedby", ((field.getAttribute("aria-describedby") || "") + " " + msg.id).trim());
        // Focus only the first invalid field of this submit attempt.
        var form = field.form;
        if (!form.__mcFocusPending) {
            form.__mcFocusPending = true;
            field.focus();
            window.setTimeout(function () { form.__mcFocusPending = false; }, 0);
        }
    }, true);
    ["input", "change"].forEach(function (name) {
        document.addEventListener(name, function (event) {
            var field = event.target;
            if (field instanceof HTMLElement && field.classList.contains("mc-invalid") && field.validity && field.validity.valid) clearFieldError(field);
        });
    });

    // data-check="…" on a field: plain-words checks that say exactly what is wrong. The server runs the same rules
    // (Models/InputChecks.cs); keep the two in step. Checked when the field loses focus and before the form is sent.
    function looksLikePhone(text) { return /\d/.test(text) && /^[\d+\s\-()]+$/.test(text); }
    function plural(n, word) { return n + " " + word + (n === 1 ? "" : "s"); }
    function fullMobileProblem(text) {
        var trimmed = text.trim();
        if (trimmed.indexOf("+") > 0) return "Put + only at the start of the number, as in +201012345678.";
        var digits = trimmed.replace(/\D/g, ""), national;
        if (trimmed.charAt(0) === "+") {
            if (digits.indexOf("20") !== 0) return "Only Egyptian numbers are accepted: start with +20 or 01.";
            national = digits.slice(2);
        } else if (digits.indexOf("0020") === 0) national = digits.slice(4);
        else if (digits.charAt(0) === "0") {
            if (digits.indexOf("01") !== 0) return "Egyptian mobile numbers start with 01.";
            national = digits.slice(1);
        } else if (digits.charAt(0) === "1") return "Add the leading 0: Egyptian mobile numbers start with 01.";
        else return "Egyptian mobile numbers start with 01.";
        if (national.length < 10) return "This number is " + plural(10 - national.length, "digit") + " short: a mobile number has 11 digits, like 01012345678.";
        if (national.length > 10) return "This number has " + plural(national.length - 10, "digit") + " too many: a mobile number has 11 digits, like 01012345678.";
        if (!/^1[0125]/.test(national)) return "Egyptian mobile numbers start with 010, 011, 012 or 015.";
        return null;
    }
    var checks = {
        "patient-search": function (value) {
            var text = value.trim();
            if (!text) return null;
            if (looksLikePhone(text)) {
                var count = text.replace(/\D/g, "").length;
                if (count < 3) return "Type at least 3 digits to search by phone number.";
                var meantComplete = text.charAt(0) === "+" || text.indexOf("00") === 0 || count >= 10;
                return meantComplete ? fullMobileProblem(text) : null;
            }
            if (/\d/.test(text)) return "Search by a phone number or by a name, not both: remove the letters or the digits.";
            if ((text.match(/\p{L}/gu) || []).length < 2) return "Type at least 2 letters of the patient’s name.";
            if (!/^[\p{L}\p{M}\s.'’\-]+$/u.test(text)) return "Names can only contain letters, spaces, dots, apostrophes and dashes.";
            return null;
        },
        "login-username": function (value) {
            var text = value.trim();
            if (!text) return "Enter your mobile number (patients) or your staff username.";
            if (looksLikePhone(text)) return fullMobileProblem(text);
            if (/\s/.test(text)) return "Usernames don’t contain spaces. Check for a space before or inside it.";
            var at = text.indexOf("@");
            if (at !== -1) {
                if (at === 0) return "Add the part before @, as in name@clinic.com.";
                if (text.split("@").length > 2) return "An email address has only one @.";
                var domain = text.slice(at + 1);
                if (!domain) return "Add the part after @, as in name@clinic.com.";
                if (domain.indexOf(".") === -1 || /^\.|\.$/.test(domain)) return "The part after @ looks incomplete, as in name@clinic.com.";
            } else if ((text.match(/\d/g) || []).length >= 8 && /\p{L}/u.test(text)) {
                return "This mixes letters into a phone number. Patients sign in with their mobile number only, like 01012345678.";
            }
            return null;
        },
        "login-password": function (value) {
            if (!value) return "Enter your password.";
            if (!value.trim()) return "Your password can’t be only spaces.";
            if (value.length > 128) return "Passwords are at most 128 characters. Check that you pasted the right text.";
            return null;
        }
    };
    function showCheck(field, problem) {
        // Fields under jQuery validation have their own message span; use it so there is one message per field.
        var slot = field.form && field.name ? field.form.querySelector('[data-valmsg-for="' + field.name + '"]') : null;
        if (slot) {
            slot.textContent = problem || "";
            slot.classList.toggle("field-validation-error", !!problem);
            slot.classList.toggle("field-validation-valid", !problem);
            field.classList.toggle("input-validation-error", !!problem);
            if (problem) field.setAttribute("aria-invalid", "true"); else field.removeAttribute("aria-invalid");
            return;
        }
        clearFieldError(field);
        if (!problem) return;
        var msg = document.createElement("span");
        msg.className = "mc-field-msg";
        msg.id = "mc-field-msg-" + (++fieldMsgCount);
        msg.setAttribute("role", "alert");
        msg.textContent = problem;
        messageAnchor(field).insertAdjacentElement("afterend", msg);
        field.classList.add("mc-invalid");
        field.setAttribute("aria-invalid", "true");
        field.setAttribute("aria-describedby", ((field.getAttribute("aria-describedby") || "") + " " + msg.id).trim());
    }
    function runCheck(field) {
        var check = checks[field.getAttribute("data-check")];
        if (!check) return null;
        var problem = check(field.value || "");
        showCheck(field, problem);
        return problem;
    }
    document.addEventListener("focusout", function (event) {
        var field = event.target;
        // An empty field is only reported on submit, so tabbing through the form does not shout at people.
        if (field instanceof HTMLInputElement && field.hasAttribute("data-check") && field.value) runCheck(field);
    });
    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement)) return;
        var first = null;
        form.querySelectorAll("[data-check]").forEach(function (field) {
            if (runCheck(field) && !first) first = field;
        });
        if (first) {
            event.preventDefault();
            event.stopImmediatePropagation();
            first.focus();
        }
    }, true);

    // data-caps-warning on a password field: say when Caps Lock is on (the next .mc-caps sibling shows).
    function capsWarning(event) {
        var field = event.target;
        if (!(field instanceof HTMLInputElement) || !field.hasAttribute("data-caps-warning") || typeof event.getModifierState !== "function") return;
        var note = field.parentElement && field.parentElement.querySelector(".mc-caps");
        if (note) note.hidden = !event.getModifierState("CapsLock");
    }
    document.addEventListener("keydown", capsWarning);
    document.addEventListener("keyup", capsWarning);
    document.addEventListener("focusout", function (event) {
        var field = event.target;
        if (field instanceof HTMLInputElement && field.hasAttribute("data-caps-warning")) {
            var note = field.parentElement && field.parentElement.querySelector(".mc-caps");
            if (note) note.hidden = true;
        }
    });

    // data-autosubmit on a select: submit its form as soon as the choice changes.
    document.addEventListener("change", function (event) {
        var field = event.target;
        if ((field instanceof HTMLSelectElement || field instanceof HTMLInputElement) && field.hasAttribute("data-autosubmit") && field.form) {
            field.form.requestSubmit ? field.form.requestSubmit() : field.form.submit();
        }
    });

    // data-refresh-seconds on a container: reload a live board on a timer, but never while the person is busy with it
    // (typing, a popover open, a confirm dialog, or the tab in the background).
    var live = document.querySelector("[data-refresh-seconds]");
    if (live) {
        var seconds = Math.max(15, parseInt(live.getAttribute("data-refresh-seconds"), 10) || 30);
        var lastInput = Date.now();
        ["keydown", "pointerdown", "input"].forEach(function (name) {
            document.addEventListener(name, function () { lastInput = Date.now(); }, true);
        });
        window.setInterval(function () {
            var busy = document.visibilityState !== "visible"
                || Date.now() - lastInput < 10000
                || document.querySelector(popovers + "[open]")
                || (document.activeElement && document.activeElement.matches("input, select, textarea"));
            if (!busy) window.location.reload();
        }, seconds * 1000);
    }
})();
