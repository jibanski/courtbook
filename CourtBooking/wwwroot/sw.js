const CACHE_NAME = 'courtbook-v1';

const STATIC_ASSETS = [
    '/css/site.css',
    '/js/site.js',
    '/icons/icon-192.svg',
    '/icons/icon-512.svg',
    '/manifest.json',
];

// Install: cache static assets
self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME).then(cache => cache.addAll(STATIC_ASSETS))
    );
    self.skipWaiting();
});

// Activate: remove old caches
self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(keys =>
            Promise.all(keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k)))
        )
    );
    self.clients.claim();
});

// Fetch strategy:
// - Static assets (css/js/icons) → cache-first
// - Everything else (pages/API) → network-first with offline fallback
self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);

    // Only handle same-origin requests
    if (url.origin !== self.location.origin) return;

    const isStatic = /\.(css|js|svg|ico|png|jpg|webp|woff2?)$/.test(url.pathname);

    if (isStatic) {
        event.respondWith(
            caches.match(event.request).then(cached => cached || fetch(event.request))
        );
    } else {
        event.respondWith(
            fetch(event.request)
                .then(response => {
                    // Cache successful GET page responses
                    if (event.request.method === 'GET' && response.ok) {
                        const clone = response.clone();
                        caches.open(CACHE_NAME).then(cache => cache.put(event.request, clone));
                    }
                    return response;
                })
                .catch(() => caches.match(event.request))
        );
    }
});
