// Browser glue for Blazor (window.mb). Everything stays on the device except mb.ping.
(() => {
    const safe = (fn, fallback) => { try { return fn(); } catch { return fallback; } };
    let wakeLock = null, wantAwake = false, installEvt = null, updateReg = null, updateTarget = null, updateReady = false;

    window.addEventListener('beforeinstallprompt', e => { e.preventDefault(); installEvt = e; });

    async function requestWake() {
        if (!wantAwake || !('wakeLock' in navigator) || document.visibilityState !== 'visible') return;
        try { wakeLock = await navigator.wakeLock.request('screen'); } catch { /* not allowed now */ }
    }
    document.addEventListener('visibilitychange', requestWake);

    // QR scanner: camera → canvas → jsQR (loaded lazily, bundled locally for offline use).
    const scanner = {
        stream: null,
        timer: null,
        async start(video, target) {
            scanner.stop();
            // Browsers (iOS especially) only allow the camera on HTTPS or localhost.
            if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
                target.invokeMethodAsync('OnScanError', window.isSecureContext ? 'NotFoundError' : 'InsecureContext');
                return;
            }
            try {
                if (!window.jsQR) await new Promise((ok, fail) => {
                    const s = document.createElement('script');
                    s.src = 'lib/jsqr/jsQR.js'; s.onload = ok; s.onerror = fail;
                    document.head.appendChild(s);
                });
                scanner.stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' }, audio: false });
            } catch (e) {
                target.invokeMethodAsync('OnScanError', (e && e.name) || 'Error');
                return;
            }
            video.srcObject = scanner.stream;
            video.setAttribute('playsinline', '');
            await video.play().catch(() => { });
            const canvas = document.createElement('canvas');
            const ctx = canvas.getContext('2d', { willReadFrequently: true });
            const tick = () => {
                if (!scanner.stream) return;
                if (video.readyState >= 2 && video.videoWidth) {
                    const scale = Math.min(1, 640 / video.videoWidth);
                    canvas.width = video.videoWidth * scale;
                    canvas.height = video.videoHeight * scale;
                    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
                    const img = ctx.getImageData(0, 0, canvas.width, canvas.height);
                    const code = window.jsQR(img.data, img.width, img.height, { inversionAttempts: 'dontInvert' });
                    if (code && code.data) {
                        target.invokeMethodAsync('OnScanned', code.data);
                        scanner.timer = setTimeout(tick, 1500); // pause, then keep scanning until the component stops us
                        return;
                    }
                }
                scanner.timer = setTimeout(tick, 150);
            };
            tick();
        },
        stop() {
            clearTimeout(scanner.timer);
            if (scanner.stream) scanner.stream.getTracks().forEach(t => t.stop());
            scanner.stream = null;
        },
    };

    function setUpdate(reg) {
        updateReg = reg; updateReady = true;
        if (updateTarget) updateTarget.invokeMethodAsync('OnUpdateReady');
    }

    window.mb = {
        storage: {
            get: k => safe(() => localStorage.getItem(k), null),
            set: (k, v) => safe(() => { localStorage.setItem(k, v); return true; }, false),
            remove: k => safe(() => localStorage.removeItem(k)),
        },
        languages: () => [...(navigator.languages || [navigator.language || 'en'])],
        setLang: (code, oops, reload) => {
            document.documentElement.lang = code;
            // Crash banner lives outside Blazor (index.html); keep it in the current language.
            const ui = document.getElementById('blazor-error-ui');
            if (ui && oops) { ui.querySelector('span').textContent = oops; ui.querySelector('.reload').textContent = reload; }
        },

        // Fire-and-forget usage ping. No body, no cookies, never blocks the app.
        ping: url => {
            if (!url || !navigator.onLine) return;
            safe(() => fetch(url, { method: 'POST', mode: 'no-cors', cache: 'no-store', credentials: 'omit', keepalive: true }).catch(() => { }));
        },

        vibrate: ms => safe(() => navigator.vibrate && navigator.vibrate(ms)),
        copy: async text => { try { await navigator.clipboard.writeText(text); return true; } catch { return false; } },
        keepAwake: async on => {
            wantAwake = on;
            if (on) await requestWake();
            else if (wakeLock) { safe(() => wakeLock.release()); wakeLock = null; }
        },

        pwa: {
            installState: () => {
                if (window.matchMedia('(display-mode: standalone)').matches || navigator.standalone) return 'installed';
                if (installEvt) return 'available';
                return /iphone|ipad|ipod/i.test(navigator.userAgent) ? 'ios' : 'unavailable';
            },
            install: async () => {
                if (!installEvt) return false;
                installEvt.prompt();
                const choice = await installEvt.userChoice;
                installEvt = null;
                return choice.outcome === 'accepted';
            },
            onUpdateReady: target => { updateTarget = target; if (updateReady) target.invokeMethodAsync('OnUpdateReady'); },
            applyUpdate: () => {
                if (updateReg && updateReg.waiting) updateReg.waiting.postMessage('skipWaiting');
                else location.reload();
            },
        },

        // Code fields: uppercase, drop junk, insert "-" every 4 chars while typing; keeps the caret in place.
        // Pasted links (join URLs) are left alone so they still work.
        codeInput: (el, maxChars) => {
            if (!el || el.dataset.codeInput) return;
            el.dataset.codeInput = '1';
            const norm = s => s.toUpperCase().replace(/[^0-9A-Z]/g, '');
            el.addEventListener('input', () => {
                const raw = el.value;
                if (raw.includes('://') || raw.includes(':')) return;
                const caret = el.selectionStart ?? raw.length;
                const before = norm(raw.slice(0, caret)).length;
                const clean = norm(raw).slice(0, maxChars);
                const out = clean.replace(/(.{4})(?=.)/g, '$1-');
                if (out === raw) return;
                el.value = out;
                const pos = Math.min(out.length, before + Math.floor(Math.max(before - 1, 0) / 4));
                try { el.setSelectionRange(pos, pos); } catch { }
            });
        },
        scanner: {
            start: (video, target) => scanner.start(video, target),
            stop: () => scanner.stop(),
        },
    };

    if ('serviceWorker' in navigator) {
        let reloading = false;
        navigator.serviceWorker.addEventListener('controllerchange', () => { if (!reloading) { reloading = true; location.reload(); } });
        navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' }).then(reg => {
            if (reg.waiting && navigator.serviceWorker.controller) setUpdate(reg);
            reg.addEventListener('updatefound', () => {
                const w = reg.installing;
                w && w.addEventListener('statechange', () => {
                    if (w.state === 'installed' && navigator.serviceWorker.controller) setUpdate(reg);
                });
            });
        }).catch(() => { });
    }
})();
