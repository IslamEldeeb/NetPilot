(() => {
    'use strict';
    const dialogs = new WeakMap();
    window.netpilotShell = {
        dialog(element, open, callback) {
            let state = dialogs.get(element);
            if (!state) {
                state = { opener: null, cancel: event => { event.preventDefault(); callback.invokeMethodAsync('RequestClose'); } };
                element.addEventListener('cancel', state.cancel);
                dialogs.set(element, state);
            }
            if (open && !element.open) {
                state.opener = document.activeElement;
                element.showModal();
                const first = element.querySelector('[data-cancel]') || element.querySelector('input, select, textarea') || element.querySelector('button, a[href]');
                first?.focus();
            } else if (!open && element.open) {
                element.close();
                if (state.opener?.isConnected) state.opener.focus();
            }
        },
        disposeDialog(element) {
            const state = dialogs.get(element);
            if (!state) return;
            element.removeEventListener('cancel', state.cancel);
            if (element.open) element.close();
            if (state.opener?.isConnected) state.opener.focus();
            dialogs.delete(element);
        }
    };
    document.addEventListener('keydown', event => {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
            const search = document.querySelector('[data-np-search]');
            if (search && !document.querySelector('dialog[open]')) { event.preventDefault(); search.click(); }
        }
    });
})();
