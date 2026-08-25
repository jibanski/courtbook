// Bumped to v2 to purge stale page entries cached by the old (buggy) fetch handler,
// which used to cache dynamic pages like MyCashLog/Bookings and could silently
// serve stale statuses (e.g. a cancelled booking still showing "Confirmed").
const CACHE_NAME = 'courtbook-v2';

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
// - Static assets (css/js/icons) → cache-first (safe: filenames are content-versioned)
// - Everything else (pages/API) → network-only, no caching, no stale fallback.
//   Booking/cash-log/status pages must NEVER silently show stale data (e.g. a
//   cancelled booking still reading "Confirmed") just because a fetch hiccuped —
//   that's worse than a normal offline error, so no offline fallback is used here.
self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);

    // Only handle same-origin requests
    if (url.origin !== self.location.origin) return;

    const isStatic = /\.(css|js|svg|ico|png|jpg|webp|woff2?)$/.test(url.pathname);

    if (isStatic) {
        event.respondWith(
            caches.match(event.request).then(cached => cached || fetch(event.request))
        );
    }
    // Non-static requests: let the browser handle them normally (no respondWith override).
});
