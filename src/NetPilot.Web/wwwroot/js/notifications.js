export function getStatus() {
    const secure = globalThis.isSecureContext === true;
    const supported = 'serviceWorker' in navigator && 'PushManager' in globalThis && 'Notification' in globalThis;
    const ua = navigator.userAgent;
    const label = /Android/i.test(ua) ? 'Android Chrome' : /Chrome|Chromium/i.test(ua) ? 'Desktop Chrome' : 'This browser';
    return { secure, supported, permission: getPermission(), label };
}

export function getPermission() {
    return 'Notification' in globalThis ? Notification.permission : 'unsupported';
}

function availabilityError() {
    if (!globalThis.isSecureContext) return { error: 'insecure-context' };
    if (!getStatus().supported) return { error: 'unsupported' };
    return null;
}

function subscriptionInfo(subscription) {
    if (!subscription) return { subscribed: false };
    const json = subscription.toJSON();
    if (!json.endpoint || !json.keys?.p256dh || !json.keys?.auth) return { error: 'invalid-subscription' };
    return { subscribed: true, endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth };
}

function applicationServerKey(base64url) {
    const encoded = base64url.replace(/-/g, '+').replace(/_/g, '/');
    const raw = atob(encoded + '='.repeat((4 - encoded.length % 4) % 4));
    return Uint8Array.from(raw, character => character.charCodeAt(0));
}

export async function registerAndSubscribe(vapidPublicKeyBase64Url) {
    const error = availabilityError();
    if (error) return error;
    try {
        // This is the only permission prompt, invoked by the Enable button.
        const permission = await Notification.requestPermission();
        if (permission !== 'granted') return { error: permission === 'denied' ? 'denied' : 'dismissed' };
        await navigator.serviceWorker.register('/sw.js', { scope: '/', updateViaCache: 'none' });
        const registration = await navigator.serviceWorker.ready;
        const existing = await registration.pushManager.getSubscription();
        return subscriptionInfo(existing ?? await registration.pushManager.subscribe({
            userVisibleOnly: true, applicationServerKey: applicationServerKey(vapidPublicKeyBase64Url)
        }));
    } catch (error) {
        return { error: error?.name === 'NotAllowedError' ? 'denied' : 'subscribe-failed' };
    }
}

export async function getCurrentSubscription() {
    const error = availabilityError();
    if (error) return error;
    try {
        const registration = await navigator.serviceWorker.getRegistration('/');
        return subscriptionInfo(registration ? await registration.pushManager.getSubscription() : null);
    } catch { return { error: 'subscription-read-failed' }; }
}

export async function unsubscribe() {
    const error = availabilityError();
    if (error) return error;
    try {
        const registration = await navigator.serviceWorker.getRegistration('/');
        const subscription = registration ? await registration.pushManager.getSubscription() : null;
        if (!subscription) return { subscribed: false };
        const endpoint = subscription.endpoint;
        const removed = await subscription.unsubscribe();
        return removed ? { subscribed: false, endpoint } : { error: 'unsubscribe-failed' };
    } catch { return { error: 'unsubscribe-failed' }; }
}
