/**
 * Service Worker for the character roulette application.
 *
 * - Pre-caches core assets so the app works offline.
 * - Cleans up old caches on activation.
 * - Uses a network-first strategy to ensure the latest assets while keeping an
 *   offline fallback available.
 */
const CACHE_NAME = 'roulette-v6'; // Update this to refresh old caches

const OFFLINE_URL = './index.html';

// Assets to cache during the installation phase
const PRECACHE_ASSETS = [
  './',
  OFFLINE_URL,
  './js/main.js',
  './js/ui.js',
  './js/state.js',
  './js/wheel.js',
  './js/animation.js',
  './manifest.webmanifest',
  './icon-192.png',
  './icon-512.png'
];

// Install event: cache application shell
self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME).then(cache => cache.addAll(PRECACHE_ASSETS))
  );
  self.skipWaiting();
});

// Activate event: remove old caches
self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(keys =>
      Promise.all(
        keys.filter(key => key !== CACHE_NAME).map(key => caches.delete(key))
      )
    )
  );
  self.clients.claim();
});

// Fetch event: network-first with cache fallback per asset type
self.addEventListener('fetch', event => {
  const { request } = event;

  if (request.method !== 'GET') {
    event.respondWith(fetch(request));
    return;
  }

  const url = new URL(request.url);

  if (url.origin !== location.origin) {
    event.respondWith(fetch(request));
    return;
  }

  if (request.mode === 'navigate' || request.destination === 'document' || url.pathname.endsWith('.html')) {
    event.respondWith(networkFirst(request, OFFLINE_URL));
    return;
  }

  event.respondWith(networkFirst(request));
});

/**
 * Responds with a cached response if available; otherwise, fetches from the
 * network and caches the result when appropriate.
 * @param {Request} request
 * @returns {Promise<Response>}
 */
async function networkFirst(request, fallbackUrl) {
  const cache = await caches.open(CACHE_NAME);

  try {
    const response = await fetch(request);
    if (response && response.ok) {
      cache.put(request, response.clone());
    }
    return response;
  } catch (error) {
    const cached = await cache.match(request);
    if (cached) {
      return cached;
    }
    if (fallbackUrl) {
      const fallback = await cache.match(fallbackUrl);
      if (fallback) {
        return fallback;
      }
    }
    throw error;
  }
}
