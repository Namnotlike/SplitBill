// Service Worker cho SplitBill PWA (CLAUDE.md mục 23, bổ sung 2026-09-07).
//
// Nguyên tắc bắt buộc, đúng tinh thần "Σ net = 0 phải luôn đúng" của cả dự án: KHÔNG BAO GIỜ phục vụ
// dữ liệu tài chính (số dư, khoản chi, kế hoạch thanh toán...) từ cache. Service worker này CHỈ cache
// các tài nguyên tĩnh không đổi theo dữ liệu (css/js/lib/icon/font) — mọi trang HTML và mọi request
// khác luôn đi thẳng ra mạng (network-only). Nếu không có mạng, trang chỉ fallback về "offline.html"
// (thông báo ngoại tuyến trung lập, không có số liệu), KHÔNG BAO GIỜ fallback về bản HTML cũ đã cache
// của chính trang đó — vì bản cũ có thể chứa số dư/khoản chi đã lỗi thời, hiển thị nhầm cho người dùng
// tưởng là dữ liệu hiện tại.

const CACHE_NAME = 'splitbill-static-v1';
const STATIC_PATH_PREFIXES = ['/css/', '/js/', '/lib/', '/icons/'];
const PRECACHE_URLS = ['/offline.html', '/css/site.css', '/js/site.js'];

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => cache.addAll(PRECACHE_URLS))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k))))
            .then(() => self.clients.claim())
    );
});

function isStaticAsset(url) {
    return STATIC_PATH_PREFIXES.some(prefix => url.pathname.startsWith(prefix));
}

self.addEventListener('fetch', event => {
    const request = event.request;

    // Chỉ can thiệp GET — mọi POST (form submit tạo/sửa/xóa khoản chi, settlement...) luôn phải đi
    // thẳng ra mạng, không có khái niệm "cache" cho hành động ghi dữ liệu.
    if (request.method !== 'GET') {
        return;
    }

    const url = new URL(request.url);
    if (url.origin !== self.location.origin) {
        return; // không can thiệp tài nguyên bên ngoài (CDN font/bootstrap...) — để trình duyệt tự lo
    }

    if (isStaticAsset(url)) {
        // Cache-first cho asset tĩnh: nhanh, và asset đổi nội dung thì đổi URL (asp-append-version)
        // nên không lo phục vụ nhầm bản cũ.
        event.respondWith(
            caches.match(request).then(cached => {
                if (cached) return cached;
                return fetch(request).then(response => {
                    if (response.ok) {
                        const clone = response.clone();
                        caches.open(CACHE_NAME).then(cache => cache.put(request, clone));
                    }
                    return response;
                });
            })
        );
        return;
    }

    if (request.mode === 'navigate') {
        // Trang HTML (bao gồm mọi trang có số liệu tài chính): network-only, KHÔNG cache. Chỉ khi mất
        // mạng hoàn toàn mới rơi về trang ngoại tuyến trung lập, không có số liệu cũ nào bị lộ ra.
        event.respondWith(
            fetch(request).catch(() => caches.match('/offline.html'))
        );
        return;
    }

    // Mọi request GET khác (API gọi từ trình duyệt nếu có, ảnh hóa đơn...) — network-only, không cache.
});

// ===== Web Push (CLAUDE.md mục 25.7, bổ sung 2026-09-09) =====
// Payload luôn là JSON { title, body, url } do NotificationService dựng ở server (mục 25.7) — không
// bao giờ tự tin dữ liệu payload (có thể thiếu/lỗi nếu push service giao sai định dạng), nên bọc
// try/catch quanh JSON.parse và luôn có fallback hợp lý.
self.addEventListener('push', event => {
    var data = { title: 'SplitBill', body: 'Bạn có thông báo mới.', url: '/' };
    if (event.data) {
        try {
            var parsed = event.data.json();
            data.title = parsed.title || data.title;
            data.body = parsed.body || data.body;
            data.url = parsed.url || data.url;
        } catch (e) { /* payload không phải JSON hợp lệ — giữ nguyên fallback trung lập */ }
    }

    event.waitUntil(
        self.registration.showNotification(data.title, {
            body: data.body,
            icon: '/icons/icon-192.png',
            data: { url: data.url },
        })
    );
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    var url = (event.notification.data && event.notification.data.url) || '/';
    event.waitUntil(
        self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(clientList => {
            // Nếu đã có tab SplitBill đang mở, focus tab đó thay vì mở tab mới trùng lặp.
            for (var i = 0; i < clientList.length; i++) {
                var client = clientList[i];
                if (client.url.indexOf(self.location.origin) === 0 && 'focus' in client) {
                    client.navigate(url);
                    return client.focus();
                }
            }
            if (self.clients.openWindow) {
                return self.clients.openWindow(url);
            }
        })
    );
});
