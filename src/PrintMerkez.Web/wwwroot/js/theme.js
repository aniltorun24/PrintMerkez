(() => {
    "use strict";

    const storageKey = "printmerkez.theme";
    const themes = { light: "Açık", dark: "Koyu", purple: "Mor" };
    const root = document.documentElement;
    const isTheme = value => Object.prototype.hasOwnProperty.call(themes, value);
    const normalize = value => isTheme(value) ? value : "light";

    function readTheme() {
        try {
            return normalize(window.localStorage.getItem(storageKey));
        } catch {
            // Storage may be disabled. The page still works with the default theme.
            return "light";
        }
    }

    function applyTheme(value, announce = false) {
        const theme = normalize(value);
        root.dataset.theme = theme;
        const select = document.getElementById("theme-select");
        if (select) select.value = theme;
        const status = document.getElementById("theme-status");
        if (announce && status) status.textContent = themes[theme] + " tema seçildi.";
    }

    // Loaded in <head> before CSS so the saved theme applies before the first paint.
    applyTheme(readTheme());

    document.addEventListener("DOMContentLoaded", () => {
        const select = document.getElementById("theme-select");
        const picker = document.getElementById("theme-picker");
        if (!select || !picker) return;
        applyTheme(root.dataset.theme);
        picker.hidden = false;

        select.addEventListener("change", () => {
            const theme = normalize(select.value);
            applyTheme(theme, true);
            try {
                window.localStorage.setItem(storageKey, theme);
            } catch {
                // Keep the selected theme for this page even if it cannot be saved.
            }
        });
    });

    // Keep other open tabs on the same origin in sync.
    window.addEventListener("storage", event => {
        try {
            if (event.storageArea !== window.localStorage) return;
        } catch {
            return;
        }
        if (event.key === storageKey || event.key === null) applyTheme(readTheme());
    });
})();
