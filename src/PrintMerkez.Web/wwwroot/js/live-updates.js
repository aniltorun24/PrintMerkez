(() => {
    'use strict';
    const region = document.querySelector('[data-live-region]');
    const toolbar = document.querySelector('[data-live-toolbar]');
    if (!region || !toolbar) return;
    const label = toolbar.querySelector('[data-live-status]');
    const toggle = toolbar.querySelector('[data-live-toggle]');
    const chartScroll = region.querySelector('.usage-chart-scroll');
    if (chartScroll) chartScroll.scrollLeft = chartScroll.scrollWidth - chartScroll.clientWidth;
    let stream, controller, timer, applyTimer, paused = false, busy = false, queued = false;
    let etag = '', version = '', pendingHtml = null, failures = 0, dataHealthy = false, lastSignal = Date.now();
    const active = () => !paused && !document.hidden && navigator.onLine;
    function status(text, state) { label.textContent = text; toolbar.dataset.state = state; }
    function liveStatus() { status('Canlı • Son kontrol: ' + new Date().toLocaleTimeString('tr-TR'), 'live'); }
    function apply() {
        clearTimeout(applyTimer);
        if (pendingHtml === null || !active()) return;
        const selection = window.getSelection();
        if (region.contains(document.activeElement) || (selection?.toString() && region.contains(selection.anchorNode))) {
            applyTimer = setTimeout(apply, 1000); return;
        }
        const scrolls = [...region.querySelectorAll('.table-scroll')].map(e => [e.scrollLeft, e.scrollTop]);
        const expanded = [...region.querySelectorAll('details')].map(e => e.open);
        const x = window.scrollX, y = window.scrollY;
        const doc = new DOMParser().parseFromString(pendingHtml, 'text/html');
        region.replaceChildren(...doc.body.childNodes);
        pendingHtml = null;
        region.querySelectorAll('details').forEach((e, i) => { if (expanded[i] !== undefined) e.open = expanded[i]; });
        region.querySelectorAll('.table-scroll').forEach((e, i) => {
            if (scrolls[i]) { e.scrollLeft = scrolls[i][0]; e.scrollTop = scrolls[i][1]; }
        });
        window.scrollTo(x, y);
    }
    function schedule(delay = 100) {
        if (!active()) return;
        if (busy) { queued = true; return; }
        clearTimeout(timer); timer = setTimeout(refresh, delay);
    }
    async function refresh() {
        if (!active() || busy) return;
        busy = true; controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 10000);
        try {
            const url = new URL(window.location.href);
            url.searchParams.set('handler', 'Live'); url.searchParams.set('v', version);
            const response = await fetch(url, { signal: controller.signal, credentials: 'same-origin',
                headers: etag ? { 'If-None-Match': etag } : {} });
            if (response.status === 401 || response.status === 403) {
                paused = true; disconnect();
                window.location.replace('/Login?ReturnUrl=' + encodeURIComponent(window.location.pathname + window.location.search));
                return;
            }
            if (response.status !== 304) {
                if (!response.ok || !response.headers.get('content-type')?.includes('text/html')) throw new Error('snapshot');
                const html = await response.text();
                if (active()) { pendingHtml = html; apply(); }
            }
            etag = response.headers.get('etag') || etag;
            failures = 0; dataHealthy = true;
            if (active()) liveStatus();
        } catch {
            dataHealthy = false;
            if (active()) {
                status('Güncel veri alınamadı; son görüntü korunuyor. Yeniden denenecek.', 'error');
                failures++; queued = false;
                timer = setTimeout(refresh, Math.min(30000, 1000 * 2 ** Math.min(failures, 5)) + Math.random() * 500);
            }
        } finally {
            clearTimeout(timeout); busy = false;
            if (queued && active()) { queued = false; schedule(); }
        }
    }
    function disconnect() { stream?.close(); stream = null; controller?.abort(); clearTimeout(timer); clearTimeout(applyTimer); }
    function connect() {
        if (!active() || stream) return;
        if (!window.EventSource) { status('Tarayıcı canlı bağlantıyı desteklemiyor.', 'error'); return; }
        status('Canlı bağlantı kuruluyor…', 'waiting');
        stream = new EventSource('/live/events');
        lastSignal = Date.now();
        stream.addEventListener('connected', event => { lastSignal = Date.now(); version = event.data; etag = ''; schedule(Math.random() * 300); });
        stream.addEventListener('changed', event => { lastSignal = Date.now(); version = event.data; schedule(); });
        stream.addEventListener('ping', () => { lastSignal = Date.now(); if (dataHealthy && active()) liveStatus(); });
        stream.onerror = () => { dataHealthy = false; if (active()) { status('Canlı bağlantı kesildi; otomatik yeniden bağlanılıyor.', 'error'); schedule(); } };
    }
    toggle.addEventListener('click', () => {
        paused = !paused; toggle.setAttribute('aria-pressed', String(paused));
        toggle.textContent = paused ? 'Canlı güncellemeyi başlat' : 'Güncellemeyi duraklat';
        if (paused) { disconnect(); status('Canlı güncelleme duraklatıldı.', 'paused'); }
        else { apply(); connect(); }
    });
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) { disconnect(); if (!paused) status('Sekme gizli; güncelleme bekliyor.', 'paused'); }
        else connect();
    });
    window.addEventListener('offline', () => { disconnect(); if (!paused) status('Ağ bağlantısı kesildi; son görüntü korunuyor.', 'error'); });
    window.addEventListener('online', connect);
    let heartbeat;
    function watchHeartbeat() {
        clearInterval(heartbeat);
        heartbeat = setInterval(() => {
        if (active() && stream && Date.now() - lastSignal > 35000) {
            disconnect(); status('Canlı bağlantı yanıt vermiyor; yeniden bağlanılıyor.', 'error');
            timer = setTimeout(connect, 1000);
        }
        }, 5000);
    }
    window.addEventListener('pagehide', () => { disconnect(); clearInterval(heartbeat); });
    window.addEventListener('pageshow', () => { watchHeartbeat(); connect(); });
    watchHeartbeat();
    connect();
})();
