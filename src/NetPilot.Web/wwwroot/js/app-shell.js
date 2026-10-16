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
})();
