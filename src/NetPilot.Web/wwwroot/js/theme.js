(() => {
    'use strict';
    const key = 'netpilot-theme';
    const system = matchMedia('(prefers-color-scheme: dark)');
    let preference = 'system';
    try { preference = localStorage.getItem(key) || 'system'; } catch { /* Preference storage is optional. */ }
    const valid = theme => ['system', 'light', 'dark'].includes(theme) ? theme : 'system';
    function apply(theme) {
        preference = valid(theme);
        if (preference === 'system') document.documentElement.removeAttribute('data-theme');
        else document.documentElement.dataset.theme = preference;
        const dark = preference === 'dark' || (preference === 'system' && system.matches);
        document.querySelector('meta[name="theme-color"]')?.setAttribute('content', dark ? '#0e1512' : '#f6faf7');
        document.querySelectorAll('[data-np-theme]').forEach(control => { control.value = preference; });
    }
    apply(preference);
    document.addEventListener('change', event => {
        if (!event.target.matches('[data-np-theme]')) return;
        const theme = valid(event.target.value);
        try {
            if (theme === 'system') localStorage.removeItem(key);
            else localStorage.setItem(key, theme);
            apply(theme);
        } catch { apply('system'); }
    });
    system.addEventListener('change', () => apply(preference));
    addEventListener('storage', event => { if (event.key === key || event.key === null) apply(event.newValue || 'system'); });
    // Blazor can insert controls after startup or an enhanced navigation.
    new MutationObserver(() => {
        document.querySelectorAll('[data-np-theme]').forEach(control => {
            if (control.value !== preference) control.value = preference;
        });
    }).observe(document.documentElement, { childList: true, subtree: true });
})();
