// Small browser helpers called from C#: storage, game keys and focus.
window.ms2 = {
    storage: {
        available() {
            try {
                const k = "__ms2_test";
                localStorage.setItem(k, "1");
                localStorage.removeItem(k);
                return true;
            } catch {
                return false;
            }
        },
        keys: () => Object.keys(localStorage),
        get: key => localStorage.getItem(key),
        // Returns "" on success, or the reason it failed (usually storage full).
        set(key, value) {
            try {
                localStorage.setItem(key, value);
                return "";
            } catch (e) {
                return (e && e.message) || "Browser storage is full or blocked.";
            }
        },
        remove: key => localStorage.removeItem(key),
    },

    // Game keys go to the current screen, except while typing in a field or when a dialog is open.
    keys: {
        handler: null,
        id: 0,
        gameKeys: new Set(["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", " ", "Enter", "Escape",
            "w", "a", "s", "d", "f", "p", "F1", "F2", "F3", "F4"]),
        listen(dotnet) {
            this.stop();
            const id = ++this.id;
            this.handler = e => {
                if (e.ctrlKey || e.altKey || e.metaKey) return;
                const key = e.key.length === 1 ? e.key.toLowerCase() : e.key;
                if (!this.gameKeys.has(key)) return;
                const t = e.target;
                if (t && ["INPUT", "TEXTAREA", "SELECT"].includes(t.tagName)) return;
                // Menu screens keep Space/Enter for pressing buttons; on a game screen they always play.
                if (t && t.tagName === "BUTTON" && !t.closest(".game-screen") && (key === " " || key === "Enter")) return;
                if (document.querySelector(".dialog-backdrop")) return;
                e.preventDefault();
                dotnet.invokeMethodAsync("OnKey", key);
            };
            document.addEventListener("keydown", this.handler);
            return id;
        },
        // Screens stop with the id they got, so an old screen closing late cannot stop the new one.
        stop(id) {
            if (id !== undefined && id !== this.id) return;
            if (this.handler) document.removeEventListener("keydown", this.handler);
            this.handler = null;
        },
    },

    // Only one tab plays at a time, like the desktop's single instance: each tab keeps its own copy of the
    // save, so two playing tabs would wipe out each other's progress. The playing tab holds a Web Lock.
    tab: {
        lockName: "ms2.playing-tab",
        dotnet: null,
        channel: null,
        release: null, // lets go of the held lock
        // Becomes the playing tab unless another tab already is. Browsers without Web Locks are not guarded.
        start(dotnet) {
            this.dotnet = dotnet;
            if (!navigator.locks) return Promise.resolve(true);
            if (window.BroadcastChannel) {
                this.channel = new BroadcastChannel("ms2.tabs");
                this.channel.onmessage = e => {
                    if (e.data !== "yield" || !this.release) return;
                    // Synchronous, so the save is written before the other tab can load it.
                    this.dotnet.invokeMethod("OnYield");
                    this.release();
                    this.release = null;
                };
            }
            return this.acquire({ ifAvailable: true });
        },
        // Asks the playing tab to save and let go; takes over anyway if it does not answer (frozen tab).
        async takeOver() {
            if (!navigator.locks) return true;
            this.channel?.postMessage("yield");
            const wait = new AbortController();
            const timer = setTimeout(() => wait.abort(), 3000);
            const got = await this.acquire({ signal: wait.signal });
            clearTimeout(timer);
            return got || this.acquire({ steal: true });
        },
        // Resolves true once the lock is held, or false if it was not available.
        acquire(options) {
            return new Promise(resolve => {
                let held = false;
                navigator.locks.request(this.lockName, options, lock => {
                    if (!lock) return resolve(false);
                    held = true;
                    resolve(true);
                    return new Promise(r => this.release = r);
                }).catch(() => {
                    if (!held) return resolve(false); // the wait timed out
                    // Another tab took over without our save (we did not answer in time): stop saving.
                    this.release = null;
                    this.dotnet.invokeMethodAsync("OnTakenOver");
                });
            });
        },
    },

    // Tells Endless Mode when the page loses focus or is hidden, so the run can pause.
    focus: {
        handler: null,
        hide: null,
        id: 0,
        listen(dotnet) {
            this.stop();
            const id = ++this.id;
            this.handler = () => {
                if (document.hidden || !document.hasFocus()) dotnet.invokeMethodAsync("OnFocusLost");
            };
            // Closing the tab: a synchronous call, because the page may be gone before an async one runs.
            this.hide = () => dotnet.invokeMethod("OnPageHide");
            window.addEventListener("blur", this.handler);
            document.addEventListener("visibilitychange", this.handler);
            window.addEventListener("pagehide", this.hide);
            return id;
        },
        // Screens stop with the id they got, so an old screen closing late cannot stop the new one.
        stop(id) {
            if (id !== undefined && id !== this.id) return;
            if (!this.handler) return;
            window.removeEventListener("blur", this.handler);
            document.removeEventListener("visibilitychange", this.handler);
            window.removeEventListener("pagehide", this.hide);
            this.handler = this.hide = null;
        },
    },
};
