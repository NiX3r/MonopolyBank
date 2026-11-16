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

    // Shared bank: drag a [data-coin] onto another [data-party] → target.OnCoinDrop(from, to).
    // Pointer events (touch + mouse); a tap without movement stays a normal click.
    function coinDrag(root, target) {
        if (!root || root.dataset.coinDrag) return;
        root.dataset.coinDrag = '1';
        let drag = null, suppressClick = 0;
        const buzz = ms => safe(() => navigator.vibrate && navigator.vibrate(ms));
        const dropAt = (x, y) => {
            const el = document.elementFromPoint(x, y)?.closest('[data-party]');
            return el && root.contains(el) && el.dataset.party !== drag.from ? el : null;
        };
        const setOver = el => {
            if (el === drag.over) return;
            drag.over?.classList.remove('drop-over');
            drag.over = el;
            if (el) { el.classList.add('drop-over'); buzz(8); }
        };
        // Near the screen edges the page scrolls, so every player can be reached.
        const autoScroll = () => {
            if (!drag?.ghost) return;
            const edge = 90, y = drag.y, h = window.innerHeight;
            const v = y < edge + 60 ? -(edge + 60 - y) / 5 : y > h - edge ? (y - (h - edge)) / 5 : 0;
            if (v) { window.scrollBy(0, v); setOver(dropAt(drag.x, drag.y)); }
            drag.raf = requestAnimationFrame(autoScroll);
        };
        const finish = () => {
            if (!drag) return;
            cancelAnimationFrame(drag.raf);
            drag.ghost?.remove();
            drag.over?.classList.remove('drop-over');
            drag.source?.classList.remove('drag-source');
            document.body.classList.remove('coin-dragging');
            drag = null;
        };

        root.addEventListener('pointerdown', e => {
            const coin = e.target.closest('[data-coin]');
            if (!coin || e.button > 0 || drag) return;
            if (e.pointerType === 'mouse') e.preventDefault(); // no text selection while dragging
            drag = { coin, from: coin.dataset.coin, id: e.pointerId, x0: e.clientX, y0: e.clientY, x: e.clientX, y: e.clientY };
            safe(() => coin.setPointerCapture(e.pointerId));
        });
        root.addEventListener('pointermove', e => {
            if (!drag || e.pointerId !== drag.id) return;
            drag.x = e.clientX; drag.y = e.clientY;
            if (!drag.ghost) {
                if (Math.hypot(drag.x - drag.x0, drag.y - drag.y0) < 8) return;
                drag.ghost = drag.coin.cloneNode(true);
                drag.ghost.removeAttribute('data-coin');
                drag.ghost.removeAttribute('data-testid');
                drag.ghost.className = 'coin coin-ghost';
                document.body.appendChild(drag.ghost);
                drag.source = drag.coin.closest('[data-party]');
                drag.source?.classList.add('drag-source');
                document.body.classList.add('coin-dragging');
                buzz(12);
                drag.raf = requestAnimationFrame(autoScroll);
            }
            drag.ghost.style.transform = `translate(${drag.x}px, ${drag.y}px)`;
            setOver(dropAt(drag.x, drag.y));
        });
        root.addEventListener('pointerup', e => {
            if (!drag || e.pointerId !== drag.id) return;
            if (drag.ghost) {
                suppressClick = Date.now() + 600;
                const to = dropAt(e.clientX, e.clientY);
                if (to) target.invokeMethodAsync('OnCoinDrop', drag.from, to.dataset.party);
            }
            finish();
        });
        root.addEventListener('pointercancel', finish);
        root.addEventListener('lostpointercapture', e => { if (drag && e.pointerId === drag.id && !drag.ghost) finish(); });
        // The click that follows a drag must not also open the payment from the coin.
        window.addEventListener('click', e => {
            if (Date.now() < suppressClick) { e.stopPropagation(); e.preventDefault(); suppressClick = 0; }
        }, true);
    }

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
        coinDrag,
        // Which emoji this device can draw: colored glyph, or a mono glyph that isn't the "missing" box, and a single
        // glyph wide (an unsupported ZWJ sequence falls apart into several). Flags and ZWJ sequences must be colored:
        // without support they show as letters ("CZ") or a plain black flag.
        emojiSupport: list => safe(() => {
            const size = 40, c = document.createElement('canvas');
            c.width = c.height = size;
            const ctx = c.getContext('2d', { willReadFrequently: true });
            ctx.font = '30px "Apple Color Emoji", "Segoe UI Emoji", "Noto Color Emoji", sans-serif';
            ctx.textBaseline = 'top';
            const draw = e => { ctx.clearRect(0, 0, size, size); ctx.fillStyle = '#000'; ctx.fillText(e, 2, 2); return ctx.getImageData(0, 0, size, size).data; };
            const missing = draw('\u{10FFFD}');
            const wide = (ctx.measureText('\u{1F600}').width || 30) * 1.4;
            const needsColor = /[\u{1F1E6}-\u{1F1FF}\u{E0020}-\u{E007F}\u200D]/u;
            return list.map(e => {
                if (ctx.measureText(e).width > wide) return false;
                const strict = needsColor.test(e);
                const d = draw(e);
                let same = true, ink = false;
                for (let i = 0; i < d.length; i += 4) {
                    if (d[i + 3] > 40) {
                        ink = true;
                        if (Math.abs(d[i] - d[i + 1]) > 24 || Math.abs(d[i + 1] - d[i + 2]) > 24) return true;
                    }
                    if (same && d[i + 3] !== missing[i + 3]) same = false;
                }
                return !strict && ink && !same;
            });
        }, list.map(() => true)),
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
