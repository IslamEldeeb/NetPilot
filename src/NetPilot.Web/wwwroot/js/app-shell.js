(() => {
    'use strict';
    document.addEventListener('keydown', event => {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
            const search = document.querySelector('[data-np-search]');
            if (search && !document.querySelector('.mud-dialog, dialog[open]')) {
                event.preventDefault();
                search.click();
            }
        }
    });
    // The persistent drawer must only exist on wide viewports: MudBlazor offsets the whole
    // layout by the drawer width whenever a persistent drawer is open, which squeezed the
    // phone layout into a 142px column.
    const wide = window.matchMedia('(min-width:1200px)');
    window.netpilotShell = {
        isWide: () => wide.matches,
        onWideChange: reference => {
            const notify = () => reference.invokeMethodAsync('SetWideFromJs', wide.matches);
            wide.addEventListener('change', notify);
            return notify();
        }
    };
})();
