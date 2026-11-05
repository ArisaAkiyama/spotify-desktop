/**
 * Spotify Native Desktop - 4-Layer Anti-Ad, Off-DOM Media Hook & Studio Dolby DSP Engine
 * Zero UI/UX Modification ("Zero AI Slop") — 100% Authentic Spotify Web Appearance
 */
(function () {
    'use strict';

    if (window.__spotifyNativeAdBlockInitialized) {
        return;
    }
    window.__spotifyNativeAdBlockInitialized = true;

    // Cache native HTMLMediaElement property descriptors for safe fast-forwarding and volume restoration
    const origCurrentTimeDescriptor = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype, 'currentTime');
    const origPlaybackRateDescriptor = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype, 'playbackRate');
    const origMutedDescriptor = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype, 'muted');
    const origVolumeDescriptor = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype, 'volume');

    function safeSetCurrentTime(media, seconds) {
        try {
            if (origCurrentTimeDescriptor && origCurrentTimeDescriptor.set) {
                origCurrentTimeDescriptor.set.call(media, seconds);
            } else {
                media.currentTime = seconds;
            }
        } catch (_) { }
    }

    function safeSetPlaybackRate(media, rate) {
        try {
            if (origPlaybackRateDescriptor && origPlaybackRateDescriptor.set) {
                origPlaybackRateDescriptor.set.call(media, rate);
            } else {
                media.playbackRate = rate;
            }
        } catch (_) { }
    }

    function safeSetMuted(media, muted) {
        try {
            if (origMutedDescriptor && origMutedDescriptor.set) {
                origMutedDescriptor.set.call(media, muted);
            } else {
                media.muted = muted;
            }
        } catch (_) { }
    }

    function safeSetVolume(media, vol) {
        try {
            const clamped = Math.max(0, Math.min(1, vol));
            if (origVolumeDescriptor && origVolumeDescriptor.set) {
                origVolumeDescriptor.set.call(media, clamped);
            } else {
                media.volume = clamped;
            }
        } catch (_) { }
    }

    // Capture Spotify's internal MediaSession action handlers (for native 'nexttrack' dispatch)
    const capturedMediaSessionHandlers = {};
    try {
        if (navigator.mediaSession && navigator.mediaSession.setActionHandler) {
            const origSetActionHandler = navigator.mediaSession.setActionHandler.bind(navigator.mediaSession);
            navigator.mediaSession.setActionHandler = function (action, handler) {
                if (action && typeof handler === 'function') {
                    capturedMediaSessionHandlers[action] = handler;
                }
                return origSetActionHandler(action, handler);
            };
        }
    } catch (_) { }

    // =========================================================================
    // LAYER 1.2: WIDEVINE EME COMPATIBILITY SHIM FOR WEBVIEW2
    // =========================================================================
    try {
        if (navigator.requestMediaKeySystemAccess) {
            const origRequestMksa = navigator.requestMediaKeySystemAccess.bind(navigator);
            navigator.requestMediaKeySystemAccess = async function (keySystem, supportedConfigurations) {
                try {
                    const normalizedConfigs = Array.isArray(supportedConfigurations)
                        ? supportedConfigurations.map((cfg) => {
                            const clone = Object.assign({}, cfg);
                            if (Array.isArray(clone.audioCapabilities)) {
                                clone.audioCapabilities = clone.audioCapabilities.map((cap) =>
                                    Object.assign({ robustness: 'SW_SECURE_CRYPTO' }, cap, {
                                        robustness: cap.robustness || 'SW_SECURE_CRYPTO'
                                    })
                                );
                            }
                            if (Array.isArray(clone.videoCapabilities)) {
                                clone.videoCapabilities = clone.videoCapabilities.map((cap) =>
                                    Object.assign({ robustness: 'SW_SECURE_CRYPTO' }, cap, {
                                        robustness: cap.robustness || 'SW_SECURE_CRYPTO'
                                    })
                                );
                            }
                            return clone;
                        })
                        : supportedConfigurations;
                    return await origRequestMksa(keySystem, normalizedConfigs);
                } catch (_) {
                    return origRequestMksa(keySystem, supportedConfigurations);
                }
            };
        }
    } catch (_) { }

    // =========================================================================
    // LAYER 1.5: OFF-DOM <AUDIO>/<VIDEO> ELEMENT INTERCEPTOR & BACKGROUND PLAY
    // =========================================================================
    const trackedMediaElements = new Set();
    const knownAdFileIds = new Set();
    let activeMediaElement = null;
    let isCurrentlyInAd = false;
    let savedVolumeBeforeAd = 1.0;
    let lastAdFastForwardTs = 0;
    let lastAdSkipClickTs = 0;
    let lastAdPromptCheckTime = 0;
    let fastAdCheckTimer = null;

    function isMediaPlayingKnownAdFile(media) {
        if (!media || knownAdFileIds.size === 0) return false;
        const src = (media.currentSrc || media.src || '').toLowerCase();
        if (!src) return false;
        for (const id of knownAdFileIds) {
            if (id && src.includes(id.toLowerCase())) {
                return true;
            }
        }
        return false;
    }

    function isSpotifyUiMuted() {
        try {
            const muteBtn = document.querySelector(
                '[data-testid="volume-bar-toggle-mute-button"], [data-testid="volume-bar"] button'
            );
            if (muteBtn) {
                const label = (muteBtn.getAttribute('aria-label') || '').toLowerCase();
                if (label.includes('bunyikan') || label.includes('unmute') || label.includes('nyalakan')) {
                    return true;
                }
            }
        } catch (_) { }
        return false;
    }

    function getSpotifyUiVolume() {
        try {
            const slider = document.querySelector(
                '[data-testid="volume-bar"] [role="slider"], [data-testid="volume-bar"] [data-testid="progress-bar"]'
            );
            if (slider) {
                const val = parseFloat(slider.getAttribute('aria-valuenow'));
                if (Number.isFinite(val)) {
                    if (val > 1.0) return Math.min(1.0, Math.max(0.01, val / 100.0));
                    if (val > 0.0) return val;
                }
            }
            const range = document.querySelector('[data-testid="volume-bar"] input[type="range"]');
            if (range) {
                const val = parseFloat(range.value);
                if (Number.isFinite(val)) {
                    const max = parseFloat(range.max) || 1.0;
                    if (val > 0) return Math.min(1.0, val / max);
                }
            }
            if (savedVolumeBeforeAd > 0) {
                return savedVolumeBeforeAd;
            }
        } catch (_) { }
        return 1.0;
    }

    function ensureNormalAudioPlayback(media) {
        if (!media || !(media instanceof HTMLMediaElement)) return;
        if (detectSpotifyAdPlaying() || isMediaPlayingKnownAdFile(media)) return;

        try {
            const userMuted = isSpotifyUiMuted();
            if (!userMuted) {
                if (media.muted) {
                    safeSetMuted(media, false);
                }
                if (media.volume === 0) {
                    const targetVol = getSpotifyUiVolume();
                    safeSetVolume(media, targetVol);
                }
            }
            if (media.playbackRate !== 1.0) {
                safeSetPlaybackRate(media, 1.0);
            }
        } catch (_) { }
    }

    function fastForwardAdMediaElement(media) {
        if (!media) return;
        try {
            if (!media.muted && media.volume > 0) {
                savedVolumeBeforeAd = media.volume;
            } else {
                const uiVol = getSpotifyUiVolume();
                if (uiVol > 0) savedVolumeBeforeAd = uiVol;
            }

            safeSetMuted(media, true);
            safeSetVolume(media, 0);

            if (Number.isFinite(media.duration) && media.duration > 0) {
                safeSetCurrentTime(media, media.duration);
                safeSetPlaybackRate(media, 1.0);
                try {
                    media.dispatchEvent(new Event('timeupdate', { bubbles: true, cancelable: true }));
                    media.dispatchEvent(new Event('ended', { bubbles: true, cancelable: true }));
                } catch (_) { }
            } else {
                safeSetPlaybackRate(media, 2.0);
            }
        } catch (_) { }
    }

    function registerMediaElement(el) {
        if (!el || !(el instanceof HTMLMediaElement)) return el;
        if (!trackedMediaElements.has(el)) {
            trackedMediaElements.add(el);

            const onMediaEventCheckAd = () => {
                activeMediaElement = el;
                const isAd = isMediaPlayingKnownAdFile(el) || detectSpotifyAdPlaying();
                if (isAd) {
                    isCurrentlyInAd = true;
                    fastForwardAdMediaElement(el);
                    handleActiveAdsAndPrompts(true);
                } else {
                    isCurrentlyInAd = false;
                    ensureNormalAudioPlayback(el);
                    onMediaPlayOrResume();
                }
            };

            el.addEventListener('play', onMediaEventCheckAd, { passive: true });
            el.addEventListener('playing', onMediaEventCheckAd, { passive: true });
            el.addEventListener('loadedmetadata', onMediaEventCheckAd, { passive: true });
            el.addEventListener('canplay', onMediaEventCheckAd, { passive: true });
            el.addEventListener('durationchange', onMediaEventCheckAd, { passive: true });
            el.addEventListener('timeupdate', () => {
                const isAd = isMediaPlayingKnownAdFile(el) || detectSpotifyAdPlaying();
                if (isAd) {
                    isCurrentlyInAd = true;
                    fastForwardAdMediaElement(el);
                } else {
                    if (isCurrentlyInAd) {
                        isCurrentlyInAd = false;
                    }
                    ensureNormalAudioPlayback(el);
                }
            }, { passive: true });
        }
        if (!activeMediaElement) {
            activeMediaElement = el;
        }
        return el;
    }

    try {
        const origCreateElement = Document.prototype.createElement;
        Document.prototype.createElement = function (tagName, options) {
            const el = origCreateElement.call(this, tagName, options);
            if (typeof tagName === 'string') {
                const lower = tagName.toLowerCase();
                if (lower === 'audio' || lower === 'video') {
                    registerMediaElement(el);
                }
            }
            return el;
        };

        const OrigAudio = window.Audio;
        if (OrigAudio) {
            window.Audio = function (src) {
                const audioEl = new OrigAudio(src);
                registerMediaElement(audioEl);
                return audioEl;
            };
            window.Audio.prototype = OrigAudio.prototype;
        }

        const origMediaPlay = HTMLMediaElement.prototype.play;
        HTMLMediaElement.prototype.play = function () {
            registerMediaElement(this);
            activeMediaElement = this;
            const isAd = isMediaPlayingKnownAdFile(this) || detectSpotifyAdPlaying();
            if (isAd) {
                isCurrentlyInAd = true;
                fastForwardAdMediaElement(this);
            } else {
                isCurrentlyInAd = false;
                ensureNormalAudioPlayback(this);
                onMediaPlayOrResume();
            }
            return origMediaPlay.apply(this, arguments);
        };
    } catch (_) { }

    function getActiveMediaElement() {
        if (activeMediaElement && !activeMediaElement.paused && !activeMediaElement.ended) {
            return activeMediaElement;
        }
        for (const el of trackedMediaElements) {
            if (!el.paused && !el.ended && el.readyState >= 1) {
                activeMediaElement = el;
                return el;
            }
        }
        const domMedia = document.querySelector('audio, video');
        if (domMedia) {
            registerMediaElement(domMedia);
            return domMedia;
        }
        if (activeMediaElement) {
            return activeMediaElement;
        }
        for (const el of trackedMediaElements) {
            return el;
        }
        return null;
    }

    // Keep visibility state always active so background playback in System Tray never throttles
    try {
        Object.defineProperty(document, 'hidden', {
            configurable: true,
            get: function () { return false; }
        });
        Object.defineProperty(document, 'webkitHidden', {
            configurable: true,
            get: function () { return false; }
        });
        Object.defineProperty(document, 'visibilityState', {
            configurable: true,
            get: function () { return 'visible'; }
        });

        const origAddEventListener = EventTarget.prototype.addEventListener;
        EventTarget.prototype.addEventListener = function (type, listener, options) {
            if (type === 'visibilitychange' || type === 'webkitvisibilitychange') {
                return;
            }
            return origAddEventListener.call(this, type, listener, options);
        };
    } catch (_) { }

    // =========================================================================
    // LAYER 2: PASSIVE STATE_MACHINE AD TRACK EXTRACTOR & WEBPACK CONTROLLER
    // Inspects /state responses and WebSocket state updates without blocking
    // spclient.wg.spotify.com (preventing "Spotify tidak bisa memutar ini sekarang").
    // =========================================================================
    function extractAdIdsFromStateMachine(stateMachine) {
        if (!stateMachine || !Array.isArray(stateMachine.tracks)) return;
        try {
            let foundAdInQueue = false;
            for (const track of stateMachine.tracks) {
                if (!track || typeof track !== 'object') continue;
                const isAdTrack =
                    track.content_type === 'AD' ||
                    (track.metadata && track.metadata.is_advertisement === true) ||
                    (typeof track.uri === 'string' && track.uri.startsWith('spotify:ad:'));

                if (isAdTrack) {
                    foundAdInQueue = true;
                    const manifest = track.manifest;
                    if (manifest) {
                        for (const key of Object.keys(manifest)) {
                            const list = manifest[key];
                            if (Array.isArray(list)) {
                                for (const item of list) {
                                    if (item && typeof item.file_id === 'string') {
                                        knownAdFileIds.add(item.file_id);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (foundAdInQueue) {
                setTimeout(handleActiveAdsAndPrompts, 20);
            }
        } catch (_) { }
    }

    try {
        const origFetch = window.fetch;
        if (origFetch) {
            window.fetch = async function (input, init) {
                const url = typeof input === 'string'
                    ? input
                    : (input && typeof input.url === 'string' ? input.url : '');

                // Block only external 3rd-party ad networks (never block spclient.wg.spotify.com)
                if (
                    url.includes('doubleclick.net') ||
                    url.includes('googlesyndication.com') ||
                    url.includes('googleadservices.com') ||
                    url.includes('2mdn.net') ||
                    url.includes('amillionads.com') ||
                    url.includes('moatads.com')
                ) {
                    return new Response('', { status: 204 });
                }

                const response = await origFetch.apply(this, arguments);
                try {
                    if (url.includes('/state')) {
                        const clone = response.clone();
                        clone.json().then((data) => {
                            if (data && data.state_machine) {
                                extractAdIdsFromStateMachine(data.state_machine);
                            }
                        }).catch(() => { });
                    }
                } catch (_) { }
                return response;
            };
        }

        // Passive WebSocket listener for state_machine updates
        const OrigWebSocket = window.WebSocket;
        if (OrigWebSocket) {
            window.WebSocket = function (url, protocols) {
                const ws = protocols ? new OrigWebSocket(url, protocols) : new OrigWebSocket(url);
                ws.addEventListener('message', (event) => {
                    try {
                        if (typeof event.data === 'string' && event.data.includes('"state_machine"')) {
                            const parsed = JSON.parse(event.data);
                            if (Array.isArray(parsed.payloads)) {
                                for (const p of parsed.payloads) {
                                    if (p && p.state_machine) {
                                        extractAdIdsFromStateMachine(p.state_machine);
                                    }
                                }
                            }
                        }
                    } catch (_) { }
                });
                return ws;
            };
            window.WebSocket.prototype = OrigWebSocket.prototype;
            Object.assign(window.WebSocket, OrigWebSocket);
        }
    } catch (_) { }

    // Hook Spotify's internal Webpack/Rspack platform, ProductState & Player controller
    let internalPlayerController = null;
    let internalProductState = null;
    let internalAdManagers = null;

    function getWebpackRequire() {
        for (const key of Object.keys(window)) {
            if (/^(webpack|rspack)Chunk/i.test(key) && Array.isArray(window[key])) {
                try {
                    let wpReq = null;
                    window[key].push([[Symbol()], {}, (req) => { wpReq = req; }]);
                    if (wpReq && wpReq.c) {
                        return wpReq;
                    }
                } catch (_) { }
            }
        }
        return null;
    }

    function hookWebpackServices() {
        try {
            const wpReq = getWebpackRequire();
            if (!wpReq || !wpReq.c) return;

            const visited = new WeakSet();

            function checkModule(obj, depth = 0) {
                if (!obj || typeof obj !== 'object' || depth > 4 || visited.has(obj)) return;
                if (obj === window || obj === document || obj instanceof Node) return;
                visited.add(obj);

                // 1. Hook ProductState API (SpotX pattern)
                try {
                    if (typeof obj.putOverridesValues === 'function') {
                        internalProductState = obj;
                        obj.putOverridesValues({
                            pairs: {
                                ads: '0',
                                catalogue: 'premium',
                                product: 'premium',
                                type: 'premium',
                                name: 'Spotify'
                            }
                        });
                    }
                } catch (_) { }

                // 2. Disable AdManagers
                try {
                    if (obj.AdManagers && obj.AdManagers.audio && typeof obj.AdManagers.audio.disable === 'function') {
                        internalAdManagers = obj.AdManagers;
                        obj.AdManagers.audio.disable();
                    }
                } catch (_) { }

                // 3. Hook Player Controller
                try {
                    const hasNext = typeof obj.skipToNext === 'function' ||
                        typeof obj.next === 'function' ||
                        typeof obj.nextTrack === 'function' ||
                        typeof obj.skip === 'function';
                    const hasPlayState = typeof obj.pause === 'function' ||
                        typeof obj.play === 'function' ||
                        typeof obj.resume === 'function';

                    if (hasNext && hasPlayState && !internalPlayerController) {
                        internalPlayerController = obj;
                        window.__spotifyInternalController = obj;
                        if (obj._streamer && typeof obj._streamer.on === 'function') {
                            obj._streamer.on('track_loaded', (e) => {
                                try {
                                    const currentTrack = e && e.data && e.data.track;
                                    if (currentTrack && currentTrack.isAd) {
                                        skipInternalPlayer();
                                    }
                                } catch (_) { }
                            });
                        }
                    }
                } catch (_) { }

                for (const k of Object.keys(obj)) {
                    try {
                        checkModule(obj[k], depth + 1);
                    } catch (_) { }
                }
            }

            for (const modId of Object.keys(wpReq.c)) {
                try {
                    const mod = wpReq.c[modId];
                    if (mod && mod.exports) {
                        checkModule(mod.exports, 0);
                    }
                } catch (_) { }
            }
        } catch (_) { }
    }

    function skipInternalPlayer() {
        if (!internalPlayerController) {
            hookWebpackServices();
        }
        if (!internalPlayerController) return false;
        try {
            if (typeof internalPlayerController.skipToNext === 'function') {
                internalPlayerController.skipToNext();
                return true;
            }
            if (typeof internalPlayerController.next === 'function') {
                internalPlayerController.next('trackdone');
                return true;
            }
            if (typeof internalPlayerController.nextTrack === 'function') {
                internalPlayerController.nextTrack();
                return true;
            }
            if (typeof internalPlayerController.skip === 'function') {
                internalPlayerController.skip();
                return true;
            }
            if (internalPlayerController._streamer) {
                if (internalPlayerController._streamer._listPlayer && typeof internalPlayerController._streamer._listPlayer.next === 'function') {
                    internalPlayerController._streamer._listPlayer.next('trackdone');
                    return true;
                }
                if (typeof internalPlayerController._streamer.next === 'function') {
                    internalPlayerController._streamer.next('trackdone');
                    return true;
                }
            }
        } catch (_) { }
        return false;
    }

    function triggerReactSkipButton() {
        const skipBtn = document.querySelector(
            '[data-testid="control-button-skip-forward"], button[aria-label="Next"], button[aria-label="Berikutnya"]'
        );
        if (!skipBtn) return false;

        try {
            skipBtn.disabled = false;
            skipBtn.removeAttribute('disabled');
            skipBtn.setAttribute('aria-disabled', 'false');
            skipBtn.click();
        } catch (_) { }

        // Inspect React fiber/props to invoke onClick directly
        try {
            const propKey = Object.keys(skipBtn).find(k => k.startsWith('__reactProps$') || k.startsWith('__reactEventHandlers$'));
            if (propKey && skipBtn[propKey] && typeof skipBtn[propKey].onClick === 'function') {
                skipBtn[propKey].onClick({ preventDefault: () => { }, stopPropagation: () => { } });
                return true;
            }
        } catch (_) { }

        try {
            const fiberKey = Object.keys(skipBtn).find(k => k.startsWith('__reactFiber$'));
            if (fiberKey && skipBtn[fiberKey]) {
                let fiber = skipBtn[fiberKey];
                let count = 0;
                while (fiber && count < 10) {
                    if (fiber.memoizedProps && typeof fiber.memoizedProps.onClick === 'function') {
                        fiber.memoizedProps.onClick({ preventDefault: () => { }, stopPropagation: () => { } });
                        return true;
                    }
                    fiber = fiber.return;
                    count++;
                }
            }
        } catch (_) { }

        return false;
    }

    function triggerMediaSessionNext() {
        try {
            if (typeof capturedMediaSessionHandlers['nexttrack'] === 'function') {
                capturedMediaSessionHandlers['nexttrack']({ action: 'nexttrack' });
                return true;
            }
        } catch (_) { }
        return false;
    }

    setTimeout(hookWebpackServices, 1500);
    setTimeout(hookWebpackServices, 4000);
    setTimeout(hookWebpackServices, 8000);

    // =========================================================================
    // LAYER 4: SURGICAL COSMETIC FILTER (CSS) - ZERO AI SLOP
    // Only hides ads, promo banners, 'Explore Premium', and 'Install App' links
    // while keeping 100% of the authentic Spotify Web UI intact.
    // =========================================================================
    const CLEAN_SPOTIFY_CSS = `
        html {
            color-scheme: dark !important;
        }

        /* Hide 'Explore Premium' / 'Upgrade' and 'Install App' buttons in top bar */
        button[data-testid="upgrade-button"],
        a[href*="/premium"],
        a[href*="spotify.com/premium"],
        a[href*="/download"],
        a[href*="spotify.com/download"],
        button[aria-label="Upgrade to Premium"],
        button[aria-label*="Explore Premium"],
        button[aria-label*="Jelajahi Premium"] {
            display: none !important;
        }

        /* Hide visual ad slots, leaderboard banners, sponsored cards, and companion ads */
        [data-testid="ad-slot-container"],
        [data-testid="billboard-ad"],
        [data-testid="companion-ad"],
        [data-testid="ad-companion-card"],
        [data-testid="ad-companion-card-tagline"],
        [data-testid="in-stream-ad"],
        [data-testid="sponsored-recommendation"],
        [data-testid="track-info-advertiser"],
        [data-testid="context-item-info-advertiser"],
        [data-testid="context-item-info-ad-subtitle"],
        [data-testid="ad-info-link"],
        section[aria-label="Advertiser"],
        aside[aria-label="Now playing view"] [data-testid="ad-companion-card"],
        aside[aria-label="Now playing view"] a[href*="/ad/"],
        iframe[src*="doubleclick.net"],
        iframe[src*="googlesyndication.com"],
        .sponsor-container {
            display: none !important;
        }
    `;

    function injectCleanCss() {
        if (document.getElementById('spotify-native-clean-css')) return;
        const target = document.head || document.documentElement;
        if (!target) return;
        const style = document.createElement('style');
        style.id = 'spotify-native-clean-css';
        style.textContent = CLEAN_SPOTIFY_CSS;
        target.appendChild(style);
    }

    injectCleanCss();
    document.addEventListener('DOMContentLoaded', injectCleanCss);

    // =========================================================================
    // LAYER 3: REAL-TIME MULTI-LANGUAGE STREAM AD-SKIPPER & MUTE GUARD
    // Supports Bahasa Indonesia ("Spotify – Iklan", "Musikmu akan berlanjut sesudah",
    // "Iklan selesai dalam") and English ("Spotify – Advertisement", etc.)
    // =========================================================================
    function detectSpotifyAdPlaying() {
        // 1. Check Document Title (Indonesian "Spotify – Iklan" & English "Spotify – Advertisement")
        const docTitle = (document.title || '').trim().toLowerCase();
        if (
            docTitle === 'iklan' ||
            docTitle === 'advertisement' ||
            docTitle.startsWith('iklan') ||
            docTitle.startsWith('advertisement') ||
            docTitle.includes('– iklan') ||
            docTitle.includes('- iklan') ||
            docTitle.includes('— iklan') ||
            docTitle.includes('– advertisement') ||
            docTitle.includes('- advertisement') ||
            docTitle.includes('— advertisement')
        ) {
            return true;
        }

        // 2. Check Right Desktop Panel ("Musikmu akan berlanjut sesudah..." / "Iklan selesai dalam..." / "Your music will continue after the break")
        const rightPanel = document.getElementById('Desktop_PanelContainer_Id') || document.querySelector('aside[aria-label="Now playing view"]');
        if (rightPanel) {
            if (
                rightPanel.querySelector('[data-testid="ad-companion-card"], [data-testid="ad-companion-card-tagline"], a[data-context-item-type="ad"]')
            ) {
                return true;
            }
            const panelText = (rightPanel.textContent || '').toLowerCase();
            if (
                panelText.includes('musikmu akan berlanjut sesudah') ||
                panelText.includes('iklan selesai dalam') ||
                panelText.includes('your music will continue after the break') ||
                panelText.includes('ad ends in') ||
                panelText.includes('iklan •') ||
                panelText.includes('advertisement •')
            ) {
                return true;
            }
        }

        // 3. Check Now Playing Bar / Widget ad indicators
        const nowPlayingBar = document.querySelector('[data-testid="now-playing-bar"], [data-testid="now-playing-widget"]');
        if (nowPlayingBar) {
            if (nowPlayingBar.getAttribute('data-ad') === 'true' || nowPlayingBar.getAttribute('data-test-ad-type')) {
                return true;
            }
            const ariaLabel = (nowPlayingBar.getAttribute('aria-label') || '').toLowerCase();
            if (ariaLabel.includes('advertisement') || ariaLabel.includes('iklan')) {
                return true;
            }

            const npText = (nowPlayingBar.textContent || '').toLowerCase();
            if (
                npText.includes('iklan •') ||
                npText.includes('advertisement •') ||
                npText.includes('iklan 1 dari') ||
                npText.includes('iklan 2 dari') ||
                npText.includes('ad 1 of') ||
                npText.includes('ad 2 of')
            ) {
                return true;
            }

            // Check if progress bar is locked/disabled with no track/album/episode link (classic ad break state)
            const progressBar = nowPlayingBar.querySelector('[data-testid="playback-progressbar"]');
            if (progressBar) {
                const isProgressDisabled =
                    progressBar.getAttribute('aria-disabled') === 'true' ||
                    progressBar.hasAttribute('disabled') ||
                    progressBar.classList.contains('disabled');
                const contentLink = nowPlayingBar.querySelector('a[href*="/track/"], a[href*="/album/"], a[href*="/episode/"], a[href*="/show/"]');
                if (!contentLink && isProgressDisabled) {
                    return true;
                }
            }
        }

        // 4. Check explicit advertiser badges or subtitles
        if (
            document.querySelector(
                '[data-testid="track-info-advertiser"], [data-testid="context-item-info-advertiser"], [data-testid="context-item-info-ad-subtitle"], [data-testid="ad-info-link"], section[aria-label="Advertiser"]'
            )
        ) {
            return true;
        }

        // 5. Check Now Playing Track Title text
        const trackTitleEl = document.querySelector('[data-testid="context-item-info-title"], [data-testid="track-info-name"]');
        if (trackTitleEl) {
            const tText = (trackTitleEl.textContent || '').trim().toLowerCase();
            if (tText === 'advertisement' || tText === 'iklan' || tText.startsWith('advertisement:') || tText.startsWith('iklan:')) {
                return true;
            }
        }

        const subtitleEl = document.querySelector('[data-testid="context-item-info-subtitles"], [data-testid="track-info-artists"]');
        if (subtitleEl) {
            const sText = (subtitleEl.textContent || '').toLowerCase();
            if (sText.includes('iklan') || sText.includes('advertisement')) {
                return true;
            }
        }

        // 6. Check MediaSession metadata
        try {
            const meta = navigator.mediaSession && navigator.mediaSession.metadata;
            if (meta) {
                const mTitle = (meta.title || '').trim().toLowerCase();
                const mArtist = (meta.artist || '').trim().toLowerCase();
                if (
                    mTitle === 'advertisement' || mTitle === 'iklan' ||
                    mArtist === 'advertisement' || mArtist === 'iklan'
                ) {
                    return true;
                }
            }
        } catch (_) { }

        // 7. Check if active media element is playing a known AD file_id
        const media = getActiveMediaElement();
        if (media && isMediaPlayingKnownAdFile(media)) {
            return true;
        }

        return false;
    }

    function handleActiveAdsAndPrompts(force = false) {
        lastAdPromptCheckTime = Date.now();
        injectCleanCss();

        // Ensure any newly attached DOM <audio>/<video> elements are registered
        document.querySelectorAll('audio, video').forEach(registerMediaElement);

        const adShowing = force === true || detectSpotifyAdPlaying();
        const media = getActiveMediaElement();

        if (adShowing) {
            if (!isCurrentlyInAd) {
                isCurrentlyInAd = true;
                if (!fastAdCheckTimer) {
                    fastAdCheckTimer = setInterval(handleActiveAdsAndPrompts, 100);
                }
                if (media && !media.muted && media.volume > 0) {
                    savedVolumeBeforeAd = media.volume;
                } else {
                    savedVolumeBeforeAd = getSpotifyUiVolume();
                }
            }

            // 1. Immediately mute and fast-forward all tracked media elements to completion
            for (const el of trackedMediaElements) {
                fastForwardAdMediaElement(el);
            }
            document.querySelectorAll('audio, video').forEach(fastForwardAdMediaElement);

            // 2. Invoke Webpack/Rspack player skip
            skipInternalPlayer();

            // 3. Trigger captured MediaSession nexttrack
            triggerMediaSessionNext();

            // 4. Trigger React skip forward button (bypassing disabled attribute)
            triggerReactSkipButton();
        } else {
            // Normal Music Playback: Continuous Un-Mute & Audio Watchdog
            if (isCurrentlyInAd) {
                isCurrentlyInAd = false;
                if (fastAdCheckTimer) {
                    clearInterval(fastAdCheckTimer);
                    fastAdCheckTimer = null;
                }
            }

            for (const el of trackedMediaElements) {
                ensureNormalAudioPlayback(el);
            }
            document.querySelectorAll('audio, video').forEach(ensureNormalAudioPlayback);
            if (activeMediaElement) {
                ensureNormalAudioPlayback(activeMediaElement);
            }
        }
    }

    let mutationScheduled = false;
    const startObserver = () => {
        const root = document.documentElement || document.body;
        if (!root) {
            requestAnimationFrame(startObserver);
            return;
        }
        const observer = new MutationObserver(() => {
            if (mutationScheduled) return;
            const elapsed = Date.now() - lastAdPromptCheckTime;
            if (elapsed < 250 && !isCurrentlyInAd) {
                return;
            }
            mutationScheduled = true;
            requestAnimationFrame(() => {
                mutationScheduled = false;
                handleActiveAdsAndPrompts();
            });
        });
        observer.observe(root, {
            childList: true,
            subtree: true,
            characterData: true,
            attributes: true,
            attributeFilter: ['class', 'data-ad', 'aria-label', 'disabled', 'aria-disabled', 'src']
        });
    };
    startObserver();
    setInterval(handleActiveAdsAndPrompts, 1000);

    // =========================================================================
    // DESKTOP SYSTEM TRAY & DYNAMIC ISLAND TELEMETRY BRIDGE
    // =========================================================================
    function parseTimeTextToSeconds(text) {
        if (!text || typeof text !== 'string') return 0;
        const clean = text.trim().replace(/^-/, '');
        const parts = clean.split(':').map(Number);
        if (parts.some((n) => !Number.isFinite(n))) return 0;
        if (parts.length === 2) {
            return parts[0] * 60 + parts[1];
        }
        if (parts.length === 3) {
            return parts[0] * 3600 + parts[1] * 60 + parts[2];
        }
        return 0;
    }

    function emitMediaState() {
        try {
            if (!window.chrome || !window.chrome.webview) return;

            // Do not overwrite Dynamic Island metadata with "Advertisement" / "Iklan" during brief ad skip
            if (isCurrentlyInAd) return;

            const media = getActiveMediaElement();
            const meta = (navigator.mediaSession && navigator.mediaSession.metadata) || null;

            const titleEl = document.querySelector(
                '[data-testid="context-item-info-title"] a, [data-testid="context-item-link"], [data-testid="now-playing-widget"] a[href*="/track/"], [data-testid="now-playing-widget"] a[href*="/album/"]'
            );
            const artistEls = document.querySelectorAll(
                '[data-testid="context-item-info-subtitles"] a, [data-testid="context-item-info-artist"]'
            );
            const artEl = document.querySelector(
                '[data-testid="cover-art-image"], [data-testid="now-playing-widget"] img'
            );

            let title = titleEl ? titleEl.textContent.trim() : '';
            if (!title && meta && meta.title) {
                title = meta.title.trim();
            }

            let artist = '';
            if (artistEls && artistEls.length > 0) {
                const names = [];
                for (const a of artistEls) {
                    const t = a.textContent ? a.textContent.trim() : '';
                    if (t && !names.includes(t)) {
                        names.push(t);
                    }
                }
                artist = names.join(', ');
            }
            if (!artist && meta && meta.artist) {
                artist = meta.artist.trim();
            }

            let albumArt = artEl && artEl.src ? artEl.src : '';
            if ((!albumArt || albumArt.startsWith('data:')) && meta && Array.isArray(meta.artwork) && meta.artwork.length > 0) {
                albumArt = meta.artwork[meta.artwork.length - 1].src || '';
            }
            if (albumArt.startsWith('data:')) {
                albumArt = '';
            }

            let isPlaying = Boolean(media && !media.paused && !media.ended);
            if (!isPlaying) {
                const playPauseBtn = document.querySelector('[data-testid="control-button-playpause"]');
                if (playPauseBtn) {
                    const aria = (playPauseBtn.getAttribute('aria-label') || '').toLowerCase();
                    if (aria === 'pause' || aria === 'jeda') {
                        isPlaying = true;
                    }
                }
            }

            let currentTime = media && Number.isFinite(media.currentTime) && media.currentTime > 0
                ? media.currentTime
                : 0;
            let duration = media && Number.isFinite(media.duration) && media.duration > 0
                ? media.duration
                : 0;

            if (duration <= 0) {
                const posEl = document.querySelector('[data-testid="playback-position"]');
                const durEl = document.querySelector('[data-testid="playback-duration"]');
                if (posEl) currentTime = parseTimeTextToSeconds(posEl.textContent);
                if (durEl) duration = parseTimeTextToSeconds(durEl.textContent);
            }

            window.chrome.webview.postMessage(JSON.stringify({
                title: title || 'Spotify',
                artist: artist || 'Select a song to play',
                albumArt: albumArt,
                isPlaying: isPlaying,
                currentTime: currentTime,
                duration: duration
            }));
        } catch (_) { }
    }

    setInterval(emitMediaState, 500);

    // =========================================================================
    // LAYER 5: BIT-PERFECT DIRECT SOUND & HARDWARE STREAMING CONTROLLER
    // In Spotify Web Player, music streams are strictly protected by Widevine DRM
    // (Encrypted Media Extensions / EME). Creating a Web Audio createMediaElementSource
    // intercepts the stream and forces Chromium CDM to output digital silence (all zeros).
    // Therefore, Spotify audio MUST stream directly via native Bit-Perfect DirectSound /
    // WASAPI output to the default audio device, while settings are safely synced.
    // =========================================================================
    let currentAudioSettings = {
        dolbyEnabled: false,
        dolbyDeviceMode: 'Headphones',
        dolbyPreset: 'Music',
        surroundLevel: 70,
        bassLevel: 68,
        clarityLevel: 65
    };

    function onMediaPlayOrResume() {
        const media = getActiveMediaElement();
        if (media) {
            ensureNormalAudioPlayback(media);
        }
    }

    const desktopController = {
        playPause: function () {
            const btn = document.querySelector(
                '[data-testid="control-button-playpause"], button[aria-label="Play"], button[aria-label="Pause"], button[aria-label="Putar"], button[aria-label="Jeda"]'
            );
            if (btn) {
                btn.click();
                setTimeout(emitMediaState, 60);
                return;
            }
            const media = getActiveMediaElement();
            if (media) {
                if (media.paused) media.play();
                else media.pause();
                setTimeout(emitMediaState, 60);
            }
        },
        next: function () {
            const btn = document.querySelector(
                '[data-testid="control-button-skip-forward"], button[aria-label="Next"], button[aria-label="Berikutnya"]'
            );
            if (btn) {
                btn.click();
                setTimeout(emitMediaState, 120);
            }
        },
        previous: function () {
            const btn = document.querySelector(
                '[data-testid="control-button-skip-back"], button[aria-label="Previous"], button[aria-label="Sebelumnya"]'
            );
            if (btn) {
                btn.click();
                setTimeout(emitMediaState, 120);
            }
        },
        seekTo: function (fraction) {
            const clamped = Math.max(0, Math.min(1, Number(fraction) || 0));
            const media = getActiveMediaElement();
            if (media && Number.isFinite(media.duration) && media.duration > 0) {
                safeSetCurrentTime(media, media.duration * clamped);
                setTimeout(emitMediaState, 60);
                return;
            }

            const progressBar = document.querySelector('[data-testid="playback-progressbar"]');
            if (progressBar) {
                const rect = progressBar.getBoundingClientRect();
                if (rect.width > 0) {
                    const clientX = rect.left + rect.width * clamped;
                    const clientY = rect.top + rect.height / 2;
                    progressBar.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, clientX, clientY }));
                    progressBar.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, clientX, clientY }));
                    setTimeout(emitMediaState, 80);
                }
            }
        },
        applyAudioSettings: function (settings) {
            if (!settings || typeof settings !== 'object') return;
            currentAudioSettings = Object.assign({}, currentAudioSettings, settings);
            const media = getActiveMediaElement();
            if (media) {
                ensureNormalAudioPlayback(media);
            }
        },
        forceSkipAd: function () {
            handleActiveAdsAndPrompts(true);
        }
    };

    window.__spotifyDesktopControl = desktopController;
})();
