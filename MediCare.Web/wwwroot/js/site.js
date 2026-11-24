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
})();
