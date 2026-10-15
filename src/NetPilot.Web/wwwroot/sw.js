/* Root scope; deliberately no fetch handler or offline cache. */
self.addEventListener('push', event => {
    let payload = {};
    try { payload = event.data?.json() ?? {}; } catch { /* A malformed push still gets a visible fallback. */ }
    if (!payload || typeof payload !== 'object') payload = {};
    const tokens = {};
    const actions = [];
    for (const entry of Array.isArray(payload.actions) ? payload.actions : []) {
        if (actions.length >= 2) break;
        if (!entry || !['extend-1gb', 'restore-now'].includes(entry.action) || typeof entry.token !== 'string') continue;
        tokens[entry.action] = entry.token;
        actions.push({ action: entry.action, title: typeof entry.title === 'string' ? entry.title : entry.action });
    }
    event.waitUntil(self.registration.showNotification(typeof payload.title === 'string' ? payload.title : 'NetPilot', {
        body: typeof payload.body === 'string' ? payload.body : 'Open NetPilot for details.',
        tag: typeof payload.tag === 'string' ? payload.tag : 'netpilot',
        icon: '/icons/icon-192.png',
        data: { url: localUrl(payload.url), tokens }, actions
    }));
});

function localUrl(candidate) {
    try {
        const url = new URL(typeof candidate === 'string' ? candidate : '/', self.location.origin);
        return url.origin === self.location.origin ? url.href : self.location.origin + '/';
    } catch { return self.location.origin + '/'; }
}

self.addEventListener('notificationclick', event => {
    if (!event.action) event.notification.close();
    event.waitUntil(event.action ? applyAction(event) : openDashboard(event.notification.data?.url));
});

async function applyAction(event) {
    const token = event.notification.data?.tokens?.[event.action];
    let message = 'This notification action is unavailable. Open NetPilot for details.';
    if (typeof token === 'string') {
        try {
            const response = await fetch('/api/notifications/action', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                credentials: 'omit', cache: 'no-store', body: JSON.stringify({ token, action: event.action })
            });
            const result = await response.json();
            message = typeof result.message === 'string' ? result.message : 'The action could not be completed.';
            if (['Ok', 'AlreadyUsed', 'Invalid', 'Expired', 'NotFound'].includes(result.status)) event.notification.close();
        } catch { message = 'Cannot reach NetPilot. Check your connection and try again from the original notification.'; }
    }
    await self.registration.showNotification('NetPilot', {
        body: message, tag: 'netpilot:action-result', icon: '/icons/icon-192.png', data: { url: '/' }
    });
}

async function openDashboard(candidate) {
    const url = localUrl(candidate);
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const existing = windows.find(client => client.url === url);
    if (existing) return existing.focus();
    const sameOrigin = windows.find(client => new URL(client.url).origin === self.location.origin);
    if (sameOrigin) {
        await sameOrigin.navigate(url);
        return sameOrigin.focus();
    }
    return self.clients.openWindow(url);
}

self.addEventListener('pushsubscriptionchange', event => {
    // Subscription rotation requires the dashboard to save fresh keys in the shared store.
    event.waitUntil(Promise.resolve());
});
