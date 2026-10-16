(() => {
    'use strict';
    const key = 'netpilot-theme';
    const root = document.documentElement;
    const system = matchMedia('(prefers-color-scheme: dark)');
    const valid = theme => ['system', 'light', 'dark'].includes(theme) ? theme : 'system';
    let preference = 'system';
    try { preference = valid(localStorage.getItem(key)); } catch { /* Storage is optional. */ }
    function apply() {
        const dark = preference === 'dark' || (preference === 'system' && system.matches);
        root.dataset.theme = dark ? 'dark' : 'light';
        root.style.setProperty('--np-start-background', dark ? '#0e1512' : '#f6faf7');
        if (root.dataset.netpilotUi === 'mud') {
            root.style.backgroundColor = dark ? '#0e1512' : '#f6faf7';
            root.style.colorScheme = dark ? 'dark' : 'light';
            document.querySelector('meta[name="theme-color"]')?.setAttribute('content', dark ? '#0e1512' : '#f6faf7');
        }
    }
    window.netpilotTheme = {
        get: () => preference,
        set(theme) {
            preference = valid(theme);
            try {
                if (preference === 'system') localStorage.removeItem(key);
                else localStorage.setItem(key, preference);
            } catch { preference = 'system'; }
            apply();
            return preference;
        }
    };
    apply();
    system.addEventListener('change', apply);
})();
