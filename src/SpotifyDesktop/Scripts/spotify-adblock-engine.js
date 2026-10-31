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

    // =========================================================================
    // LAYER 1.2: WIDEVINE EME COMPATIBILITY SHIM FOR WEBVIEW2
    // Ensures navigator.requestMediaKeySystemAccess always includes valid
    // software robustness levels so encrypted Spotify streams never fail.
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
    // Spotify Web Player creates <audio>/<video> elements off-DOM (detached).
    // Hooking createElement, Audio, and HTMLMediaElement.prototype.play at
    // document-start captures every media instance reliably.
    // =========================================================================
    const trackedMediaElements = new Set();
    let activeMediaElement = null;

    function registerMediaElement(el) {
        if (!el || !(el instanceof HTMLMediaElement)) return el;
        if (!trackedMediaElements.has(el)) {
            trackedMediaElements.add(el);
            el.addEventListener('play', () => {
                activeMediaElement = el;
                onMediaPlayOrResume();
            }, { passive: true });
            el.addEventListener('playing', () => {
                activeMediaElement = el;
                onMediaPlayOrResume();
            }, { passive: true });
            el.addEventListener('loadeddata', () => {
                if (!activeMediaElement) {
                    activeMediaElement = el;
                }
                onMediaPlayOrResume();
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
            onMediaPlayOrResume();
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
    // LAYER 2: PRE-DOCUMENT FETCH / XHR & JSON AD PAYLOAD SANITIZER
    // Intercepts Spotify ad-logic, ads/v1-v3, and telemetry endpoints while
    // keeping spclient music streaming and Widevine DRM endpoints untouched.
    // =========================================================================
    const BLOCKED_URL_PATTERNS = [
        '/ad-logic/',
        '/ads/v1/',
        '/ads/v2/',
        '/ads/v3/',
        '/gabo-receiver-service/',
        'adeventtracker.spotify.com',
        'ads-fa.spotify.com',
        'doubleclick.net',
        'googlesyndication.com',
        'googleadservices.com',
        'adservice.google.',
        '2mdn.net',
        'omaze.com',
        'b.scorecardresearch.com'
    ];

    function isBlockedSpotifyAdUrl(url) {
        if (!url || typeof url !== 'string') return false;
        const lower = url.toLowerCase();
        for (const pattern of BLOCKED_URL_PATTERNS) {
            if (lower.includes(pattern)) {
                return true;
            }
        }
        return false;
    }

    try {
        const origFetch = window.fetch;
        if (origFetch) {
            window.fetch = async function (input, init) {
                try {
                    const url = typeof input === 'string'
                        ? input
                        : (input && typeof input.url === 'string' ? input.url : '');
                    if (isBlockedSpotifyAdUrl(url)) {
                        return new Response('{}', {
                            status: 200,
                            statusText: 'OK',
                            headers: { 'Content-Type': 'application/json' }
                        });
                    }
                } catch (_) { }
                return origFetch.apply(this, arguments);
            };
        }

        const origXhrOpen = XMLHttpRequest.prototype.open;
        const origXhrSend = XMLHttpRequest.prototype.send;

        XMLHttpRequest.prototype.open = function (method, url) {
            this.__spotifyBlockedAdUrl = isBlockedSpotifyAdUrl(typeof url === 'string' ? url : String(url));
            return origXhrOpen.apply(this, arguments);
        };

        XMLHttpRequest.prototype.send = function () {
            if (this.__spotifyBlockedAdUrl) {
                try {
                    Object.defineProperty(this, 'status', { get: () => 200 });
                    Object.defineProperty(this, 'readyState', { get: () => 4 });
                    Object.defineProperty(this, 'responseText', { get: () => '{}' });
                    Object.defineProperty(this, 'response', { get: () => '{}' });
                    setTimeout(() => {
                        this.dispatchEvent(new Event('readystatechange'));
                        this.dispatchEvent(new Event('load'));
                        this.dispatchEvent(new Event('loadend'));
                    }, 1);
                    return;
                } catch (_) { }
            }
            return origXhrSend.apply(this, arguments);
        };
    } catch (_) { }

    const AD_JSON_KEYS = new Set([
        'ad_slots',
        'adSlots',
        'adBreak',
        'adPlacements',
        'sponsoredPlaylist'
    ]);

    function sanitizeSpotifyJson(obj, visited = new WeakSet(), depth = 0) {
        if (!obj || typeof obj !== 'object' || depth > 12) return obj;
        if (visited.has(obj)) return obj;
        visited.add(obj);

        if (Array.isArray(obj)) {
            for (let i = 0; i < obj.length; i++) {
                if (obj[i] && typeof obj[i] === 'object') {
                    sanitizeSpotifyJson(obj[i], visited, depth + 1);
                }
            }
            return obj;
        }

        for (const key of Object.keys(obj)) {
            if (AD_JSON_KEYS.has(key)) {
                if (Array.isArray(obj[key])) {
                    obj[key] = [];
                } else {
                    delete obj[key];
                }
                continue;
            }
            const val = obj[key];
            if (val && typeof val === 'object') {
                sanitizeSpotifyJson(val, visited, depth + 1);
            }
        }
        return obj;
    }

    const origJsonParse = JSON.parse;
    JSON.parse = function (text, reviver) {
        const parsed = origJsonParse.call(this, text, reviver);
        try {
            if (
                typeof text === 'string' &&
                text.length > 80 &&
                parsed &&
                typeof parsed === 'object' &&
                (parsed.ad_slots || parsed.adSlots || parsed.adBreak)
            ) {
                sanitizeSpotifyJson(parsed);
            }
        } catch (_) { }
        return parsed;
    };

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
        [data-testid="in-stream-ad"],
        [data-testid="sponsored-recommendation"],
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
    // LAYER 3: REAL-TIME STREAM AD-SKIPPER & MUTE GUARD
    // =========================================================================
    let wasMutedBeforeAd = false;
    let isCurrentlyInAd = false;
    let lastAdSkipAttemptMs = 0;

    function detectSpotifyAdPlaying() {
        // 1. Check Now Playing Bar / Widget ad indicators
        const nowPlayingBar = document.querySelector('[data-testid="now-playing-bar"], [data-testid="now-playing-widget"]');
        if (nowPlayingBar) {
            if (nowPlayingBar.getAttribute('data-ad') === 'true' || nowPlayingBar.getAttribute('data-test-ad-type')) {
                return true;
            }
            const ariaLabel = (nowPlayingBar.getAttribute('aria-label') || '').toLowerCase();
            if (ariaLabel.includes('advertisement') || ariaLabel.includes('iklan')) {
                return true;
            }
        }

        // 2. Check specific ad subtitle or ad link elements in player bar
        if (document.querySelector('[data-testid="context-item-info-ad-subtitle"], [data-testid="ad-info-link"]')) {
            return true;
        }

        // 3. Check document title & MediaSession metadata for Advertisement state
        const docTitle = (document.title || '').trim();
        if (/^Advertisement\b/i.test(docTitle) || /Spotify\s*[–-]\s*Advertisement/i.test(docTitle)) {
            return true;
        }

        try {
            const meta = navigator.mediaSession && navigator.mediaSession.metadata;
            if (meta) {
                const mTitle = (meta.title || '').trim();
                if (/^(Advertisement|Iklan|Spotify)$/i.test(mTitle) && (!meta.artist || /^Spotify$/i.test(meta.artist.trim()))) {
                    return true;
                }
            }
        } catch (_) { }

        return false;
    }

    function handleActiveAdsAndPrompts() {
        injectCleanCss();

        const adShowing = detectSpotifyAdPlaying();
        const media = getActiveMediaElement();

        if (adShowing) {
            if (!isCurrentlyInAd) {
                isCurrentlyInAd = true;
                wasMutedBeforeAd = media ? media.muted : false;
            }

            // Mute all tracked media elements immediately during ad break
            for (const el of trackedMediaElements) {
                try {
                    el.muted = true;
                } catch (_) { }
            }

            // Fast-forward the active ad stream to the end so Spotify immediately transitions to the next track
            if (media) {
                try {
                    if (Number.isFinite(media.duration) && media.duration > 0.5) {
                        media.currentTime = Math.max(0, media.duration - 0.05);
                    }
                    media.playbackRate = 16.0;
                } catch (_) { }
            }

            // Trigger skip-forward button with cooldown
            const nowMs = Date.now();
            if (nowMs - lastAdSkipAttemptMs > 600) {
                lastAdSkipAttemptMs = nowMs;
                const skipBtn = document.querySelector('[data-testid="control-button-skip-forward"]');
                if (skipBtn && !skipBtn.disabled) {
                    try {
                        skipBtn.click();
                    } catch (_) { }
                }
            }
        } else if (isCurrentlyInAd) {
            isCurrentlyInAd = false;
            for (const el of trackedMediaElements) {
                try {
                    el.muted = wasMutedBeforeAd;
                    if (el.playbackRate > 2) {
                        el.playbackRate = 1.0;
                    }
                } catch (_) { }
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
            mutationScheduled = true;
            requestAnimationFrame(() => {
                mutationScheduled = false;
                handleActiveAdsAndPrompts();
            });
        });
        observer.observe(root, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['class', 'data-ad', 'aria-label', 'src']
        });
    };
    startObserver();
    setInterval(handleActiveAdsAndPrompts, 350);

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

            // Determine playing state from active HTMLMediaElement or Play/Pause button state
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
    // LAYER 5: STUDIO DOLBY SPATIAL AUDIO & DSP ENGINE (Web Audio API)
    // 7-Stage Pipeline: Speaker Protection -> 6-Band Parametric EQ & De-Mud ->
    // Psychoacoustic Sub-Bass Exciter (4x WaveShaper) -> True Mid-Side (M/S) Matrix
    // with Bass-Safe Crossover (160Hz) -> Binaural 3D Convolver (38ms IR) ->
    // Dolby Multi-Knee Leveler -> True-Peak Soft Clipper Limiter.
    // =========================================================================
    let audioCtx = null;
    let connectedMedia = null;
    let dspNodes = null;
    let currentAudioSettings = {
        dolbyEnabled: false,
        dolbyDeviceMode: 'Headphones',
        dolbyPreset: 'Music',
        surroundLevel: 70,
        bassLevel: 68,
        clarityLevel: 65
    };

    function onMediaPlayOrResume() {
        if (audioCtx && audioCtx.state === 'suspended') {
            audioCtx.resume().catch(() => { });
        }
        if (currentAudioSettings.dolbyEnabled) {
            updateDolbyDspParameters();
        }
    }

    function generateSoftSaturationCurve(samples = 1024, drive = 1.0) {
        const curve = new Float32Array(samples);
        for (let i = 0; i < samples; ++i) {
            const x = (i * 2) / samples - 1;
            curve[i] = Math.tanh(x * drive);
        }
        return curve;
    }

    function generateBinauralEarlyReflectionsBuffer(ctx, duration = 0.038) {
        const sampleRate = ctx.sampleRate;
        const length = Math.floor(sampleRate * duration);
        const buffer = ctx.createBuffer(2, length, sampleRate);
        const leftData = buffer.getChannelData(0);
        const rightData = buffer.getChannelData(1);

        const taps = [
            { t: 0.0042, gL: 0.45, gR: -0.22 },
            { t: 0.0089, gL: -0.32, gR: 0.40 },
            { t: 0.0145, gL: 0.38, gR: 0.35 },
            { t: 0.0210, gL: -0.26, gR: -0.30 },
            { t: 0.0275, gL: 0.22, gR: -0.24 },
            { t: 0.0340, gL: -0.18, gR: 0.20 }
        ];

        for (const tap of taps) {
            const index = Math.floor(tap.t * sampleRate);
            if (index < length) {
                leftData[index] += tap.gL;
                rightData[index] += tap.gR;
            }
        }

        const decayTimeConstant = duration * 0.42;
        for (let i = 0; i < length; i++) {
            const t = i / sampleRate;
            const envelope = Math.exp(-t / decayTimeConstant);
            leftData[i] += (Math.random() * 2 - 1) * 0.06 * envelope;
            rightData[i] += (Math.random() * 2 - 1) * 0.06 * envelope;
        }

        return buffer;
    }

    function ensureDolbyAudioGraph() {
        try {
            const media = getActiveMediaElement();
            if (!media) return false;

            if (!audioCtx) {
                const AudioContextClass = window.AudioContext || window.webkitAudioContext;
                if (!AudioContextClass) return false;
                audioCtx = new AudioContextClass({ latencyHint: 'interactive' });
            }

            if (audioCtx.state === 'suspended') {
                audioCtx.resume().catch(() => { });
            }

            if (connectedMedia !== media || !dspNodes) {
                if (dspNodes) {
                    try {
                        dspNodes.dryGain.disconnect();
                        dspNodes.wetGain.disconnect();
                    } catch (_) { }
                    dspNodes = null;
                }

                if (!media.__spotifyAudioSourceNode) {
                    media.__spotifyAudioSourceNode = audioCtx.createMediaElementSource(media);
                }
                const source = media.__spotifyAudioSourceNode;

                // --- DRY PATH (Pure Bit-Perfect Bypass) ---
                const dryGain = audioCtx.createGain();
                dryGain.gain.value = 1.0;
                source.connect(dryGain);
                dryGain.connect(audioCtx.destination);

                // --- WET PATH (Studio Dolby Spatial DSP Pipeline) ---
                const wetGain = audioCtx.createGain();
                wetGain.gain.value = 0.0;

                // STAGE 1: Subsonic & Speaker Excursion Protection
                const hpFilter = audioCtx.createBiquadFilter();
                hpFilter.type = 'highpass';
                hpFilter.frequency.value = 22;
                hpFilter.Q.value = 0.707;
                source.connect(hpFilter);

                // STAGE 2: 6-Band Parametric Acoustic EQ
                const subBassFilter = audioCtx.createBiquadFilter();
                subBassFilter.type = 'lowshelf';
                subBassFilter.frequency.value = 60;
                subBassFilter.gain.value = 0;

                const upperBassFilter = audioCtx.createBiquadFilter();
                upperBassFilter.type = 'peaking';
                upperBassFilter.frequency.value = 135;
                upperBassFilter.Q.value = 1.1;
                upperBassFilter.gain.value = 0;

                const deMudFilter = audioCtx.createBiquadFilter();
                deMudFilter.type = 'peaking';
                deMudFilter.frequency.value = 290;
                deMudFilter.Q.value = 1.35;
                deMudFilter.gain.value = 0;

                const vocalFilter = audioCtx.createBiquadFilter();
                vocalFilter.type = 'peaking';
                vocalFilter.frequency.value = 2500;
                vocalFilter.Q.value = 0.9;
                vocalFilter.gain.value = 0;

                const presenceFilter = audioCtx.createBiquadFilter();
                presenceFilter.type = 'peaking';
                presenceFilter.frequency.value = 5600;
                presenceFilter.Q.value = 1.0;
                presenceFilter.gain.value = 0;

                const airFilter = audioCtx.createBiquadFilter();
                airFilter.type = 'highshelf';
                airFilter.frequency.value = 11500;
                airFilter.gain.value = 0;

                hpFilter.connect(subBassFilter);
                subBassFilter.connect(upperBassFilter);
                upperBassFilter.connect(deMudFilter);
                deMudFilter.connect(vocalFilter);
                vocalFilter.connect(presenceFilter);
                presenceFilter.connect(airFilter);

                // STAGE 3: Psychoacoustic Sub-Bass Harmonic Exciter (WaveShaper 4x)
                const exciterLpf = audioCtx.createBiquadFilter();
                exciterLpf.type = 'lowpass';
                exciterLpf.frequency.value = 115;
                exciterLpf.Q.value = 0.707;

                const exciterShaper = audioCtx.createWaveShaper();
                exciterShaper.curve = generateSoftSaturationCurve(512, 1.8);
                exciterShaper.oversample = '4x';

                const exciterBpf = audioCtx.createBiquadFilter();
                exciterBpf.type = 'bandpass';
                exciterBpf.frequency.value = 160;
                exciterBpf.Q.value = 1.15;

                const exciterGain = audioCtx.createGain();
                exciterGain.gain.value = 0.0;

                hpFilter.connect(exciterLpf);
                exciterLpf.connect(exciterShaper);
                exciterShaper.connect(exciterBpf);
                exciterBpf.connect(exciterGain);

                const preSpatialBus = audioCtx.createGain();
                airFilter.connect(preSpatialBus);
                exciterGain.connect(preSpatialBus);

                // STAGE 4: True Mid-Side (M/S) Matrix with Bass-Safe Crossover
                const splitter = audioCtx.createChannelSplitter(2);
                preSpatialBus.connect(splitter);

                const midGainL = audioCtx.createGain();
                const midGainR = audioCtx.createGain();
                midGainL.gain.value = 0.5;
                midGainR.gain.value = 0.5;
                splitter.connect(midGainL, 0);
                splitter.connect(midGainR, 1);

                const midBus = audioCtx.createGain();
                midGainL.connect(midBus);
                midGainR.connect(midBus);

                const sideGainL = audioCtx.createGain();
                const sideGainR = audioCtx.createGain();
                sideGainL.gain.value = 0.5;
                sideGainR.gain.value = -0.5;
                splitter.connect(sideGainL, 0);
                splitter.connect(sideGainR, 1);

                const sideBusRaw = audioCtx.createGain();
                sideGainL.connect(sideBusRaw);
                sideGainR.connect(sideBusRaw);

                const sideHpf = audioCtx.createBiquadFilter();
                sideHpf.type = 'highpass';
                sideHpf.frequency.value = 160;
                sideHpf.Q.value = 0.707;
                sideBusRaw.connect(sideHpf);

                const sideWidthGain = audioCtx.createGain();
                sideWidthGain.gain.value = 1.0;
                sideHpf.connect(sideWidthGain);

                const merger = audioCtx.createChannelMerger(2);

                const midToL = audioCtx.createGain();
                const midToR = audioCtx.createGain();
                midToL.gain.value = 1.0;
                midToR.gain.value = 1.0;
                midBus.connect(midToL);
                midBus.connect(midToR);

                const sideToL = audioCtx.createGain();
                const sideToR = audioCtx.createGain();
                sideToL.gain.value = 1.0;
                sideToR.gain.value = -1.0;
                sideWidthGain.connect(sideToL);
                sideWidthGain.connect(sideToR);

                midToL.connect(merger, 0, 0);
                sideToL.connect(merger, 0, 0);
                midToR.connect(merger, 0, 1);
                sideToR.connect(merger, 0, 1);

                // STAGE 5: Binaural 3D Early Reflections (ConvolverNode)
                const convolver = audioCtx.createConvolver();
                convolver.buffer = generateBinauralEarlyReflectionsBuffer(audioCtx, 0.038);

                const convBpf = audioCtx.createBiquadFilter();
                convBpf.type = 'bandpass';
                convBpf.frequency.value = 2400;
                convBpf.Q.value = 0.55;

                const convWetGain = audioCtx.createGain();
                convWetGain.gain.value = 0.12;

                preSpatialBus.connect(convBpf);
                convBpf.connect(convolver);
                convolver.connect(convWetGain);

                const spatialBus = audioCtx.createGain();
                merger.connect(spatialBus);
                convWetGain.connect(spatialBus);

                // STAGE 6: Dolby Multi-Knee Dynamics Leveler & Compressor
                const compressor = audioCtx.createDynamicsCompressor();
                compressor.threshold.value = -16;
                compressor.knee.value = 18;
                compressor.ratio.value = 3.4;
                compressor.attack.value = 0.003;
                compressor.release.value = 0.18;

                const makeupGain = audioCtx.createGain();
                makeupGain.gain.value = 1.15;

                // STAGE 7: True-Peak Soft Clipper Limiter (0 dBFS Guard)
                const limiterShaper = audioCtx.createWaveShaper();
                limiterShaper.curve = generateSoftSaturationCurve(1024, 1.05);
                limiterShaper.oversample = '4x';

                spatialBus.connect(compressor);
                compressor.connect(makeupGain);
                makeupGain.connect(limiterShaper);
                limiterShaper.connect(wetGain);
                wetGain.connect(audioCtx.destination);

                connectedMedia = media;
                dspNodes = {
                    dryGain,
                    wetGain,
                    hpFilter,
                    subBassFilter,
                    upperBassFilter,
                    deMudFilter,
                    vocalFilter,
                    presenceFilter,
                    airFilter,
                    exciterGain,
                    sideWidthGain,
                    convWetGain,
                    compressor,
                    makeupGain
                };
            }

            return true;
        } catch (_) {
            return false;
        }
    }

    function updateDolbyDspParameters() {
        try {
            const cfg = currentAudioSettings;
            if (!cfg.dolbyEnabled && !dspNodes) {
                return;
            }

            if (!ensureDolbyAudioGraph() || !dspNodes || !audioCtx) {
                return;
            }

            const now = audioCtx.currentTime;
            const tc = 0.035;

            if (!cfg.dolbyEnabled) {
                dspNodes.dryGain.gain.setTargetAtTime(1.0, now, tc);
                dspNodes.wetGain.gain.setTargetAtTime(0.0, now, tc);
                return;
            }

            const isLaptop = cfg.dolbyDeviceMode === 'LaptopSpeakers';
            const preset = cfg.dolbyPreset || 'Music';
            const sNorm = Math.max(0, Math.min(100, Number(cfg.surroundLevel) ?? 70)) / 100.0;
            const bNorm = Math.max(0, Math.min(100, Number(cfg.bassLevel) ?? 68)) / 100.0;
            const cNorm = Math.max(0, Math.min(100, Number(cfg.clarityLevel) ?? 65)) / 100.0;

            let presetSub = 1.0;
            let presetPunch = 1.0;
            let presetDeMud = 1.0;
            let presetVocal = 1.0;
            let presetPresence = 1.0;
            let presetAir = 1.0;
            let presetWidth = 1.0;
            let presetBinaural = 1.0;

            switch (preset) {
                case 'Cinema':
                    presetWidth = 1.35;
                    presetBinaural = 1.45;
                    presetSub = 1.25;
                    presetPunch = 1.15;
                    presetVocal = 1.25;
                    presetDeMud = 1.2;
                    presetPresence = 1.2;
                    presetAir = 1.15;
                    break;
                case 'BassBoost':
                    presetWidth = 1.05;
                    presetBinaural = 0.85;
                    presetSub = 1.55;
                    presetPunch = 1.40;
                    presetVocal = 0.95;
                    presetDeMud = 0.8;
                    presetPresence = 0.95;
                    presetAir = 0.95;
                    break;
                case 'Vocal':
                    presetWidth = 0.95;
                    presetBinaural = 0.9;
                    presetSub = 0.6;
                    presetPunch = 0.75;
                    presetVocal = 1.6;
                    presetDeMud = 1.4;
                    presetPresence = 1.35;
                    presetAir = 1.2;
                    break;
                case 'Dynamic':
                    presetWidth = 1.2;
                    presetBinaural = 1.2;
                    presetSub = 1.25;
                    presetPunch = 1.25;
                    presetVocal = 1.2;
                    presetDeMud = 1.15;
                    presetPresence = 1.25;
                    presetAir = 1.25;
                    break;
                default: // 'Music' (Balanced Dolby Audiophile Curve)
                    presetWidth = 1.12;
                    presetBinaural = 1.05;
                    presetSub = 1.15;
                    presetPunch = 1.12;
                    presetVocal = 1.1;
                    presetDeMud = 1.05;
                    presetPresence = 1.1;
                    presetAir = 1.1;
                    break;
            }

            const hpFreq = isLaptop ? 82 : 22;
            dspNodes.hpFilter.frequency.setTargetAtTime(hpFreq, now, tc);

            const subGainDb = isLaptop ? 0.5 : (bNorm * 7.5 * presetSub);
            const punchGainDb = isLaptop
                ? (bNorm * 5.8 * presetPunch)
                : (bNorm * 4.6 * presetPunch);

            dspNodes.subBassFilter.gain.setTargetAtTime(subGainDb, now, tc);
            dspNodes.upperBassFilter.gain.setTargetAtTime(punchGainDb, now, tc);

            const deMudCutDb = -(1.2 + (cNorm * 1.4 * presetDeMud));
            dspNodes.deMudFilter.gain.setTargetAtTime(deMudCutDb, now, tc);

            const vocalGainDb = (cNorm * 4.6 * presetVocal) + (isLaptop ? 1.5 : 0.0);
            const presenceGainDb = cNorm * 3.6 * presetPresence;
            const airGainDb = cNorm * 4.2 * presetAir;

            dspNodes.vocalFilter.gain.setTargetAtTime(vocalGainDb, now, tc);
            dspNodes.presenceFilter.gain.setTargetAtTime(presenceGainDb, now, tc);
            dspNodes.airFilter.gain.setTargetAtTime(airGainDb, now, tc);

            const exciterLevel = isLaptop
                ? (0.15 + (bNorm * 0.45))
                : (bNorm * 0.28);
            dspNodes.exciterGain.gain.setTargetAtTime(exciterLevel, now, tc);

            const sideWidth = 1.0 + (sNorm * 1.35 * presetWidth);
            dspNodes.sideWidthGain.gain.setTargetAtTime(sideWidth, now, tc);

            const binauralLevel = isLaptop
                ? (sNorm * 0.12 * presetBinaural)
                : (0.05 + (sNorm * 0.22 * presetBinaural));
            dspNodes.convWetGain.gain.setTargetAtTime(binauralLevel, now, tc);

            dspNodes.compressor.threshold.setTargetAtTime(isLaptop ? -18 : -15, now, tc);
            dspNodes.compressor.ratio.setTargetAtTime(isLaptop ? 4.2 : 3.4, now, tc);
            dspNodes.makeupGain.gain.setTargetAtTime(isLaptop ? 1.20 : 1.14, now, tc);

            dspNodes.dryGain.gain.setTargetAtTime(0.0, now, tc);
            dspNodes.wetGain.gain.setTargetAtTime(1.0, now, tc);
        } catch (_) { }
    }

    window.addEventListener('click', () => {
        if (audioCtx && audioCtx.state === 'suspended') {
            audioCtx.resume().catch(() => { });
        }
    }, { passive: true });

    setInterval(() => {
        if (currentAudioSettings.dolbyEnabled) {
            const media = getActiveMediaElement();
            if (media && media !== connectedMedia) {
                updateDolbyDspParameters();
            } else if (audioCtx && audioCtx.state === 'suspended' && media && !media.paused) {
                audioCtx.resume().catch(() => { });
            }
        }
    }, 1500);

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
                media.currentTime = media.duration * clamped;
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
            updateDolbyDspParameters();
        }
    };

    window.__spotifyDesktopControl = desktopController;
})();
