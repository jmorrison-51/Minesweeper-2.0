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
        // Asks the browser not to clear this site's data when it is short of space. Some browsers ask the
        // player first, others decide quietly; either way the answer only matters to the browser.
        async persist() {
            try {
                if (!navigator.storage || !navigator.storage.persist) return false;
                return (await navigator.storage.persisted()) || (await navigator.storage.persist());
            } catch {
                return false;
            }
        },
    },

    file: {
        // Offers text to the player as a downloaded file.
        download(name, text) {
            const url = URL.createObjectURL(new Blob([text], { type: "text/plain" }));
            const link = document.createElement("a");
            link.href = url;
            link.download = name;
            document.body.appendChild(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 10000);
        },
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
        release: null, // lets go of the lock while this tab holds it
        waiting: null, // AbortController of this tab's request that is queued for the lock
        // Becomes the playing tab unless another tab already is (then it queues for the lock and the game is
        // told when it arrives). Resolves true to play. Browsers without Web Locks, or where they do not work,
        // are not guarded: better to play unguarded than not to play.
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
                    this.wait(); // and queue behind the tab that asked
                };
            }
            return new Promise(resolve => this.grab({ ifAvailable: true }, resolve)).then(free => {
                if (!free) this.wait();
                return free;
            });
        },
        // Queues for the lock. It comes when the playing tab is closed or hands over, and the game is told.
        wait() {
            this.waiting = new AbortController();
            this.grab({ signal: this.waiting.signal }, free => {
                if (!free) return;
                this.waiting = null;
                this.dotnet.invokeMethodAsync("OnLockFreed");
            });
        },
        // "Play here": asks the playing tab to save and let go, and resolves once this tab holds the lock. If the
        // other tab does not answer within 3 seconds (a frozen tab) the lock is taken from it.
        async takeOver() {
            if (!navigator.locks) return;
            this.channel?.postMessage("yield");
            const deadline = Date.now() + 3000;
            while (this.waiting && Date.now() < deadline) await new Promise(r => setTimeout(r, 50));
            if (this.waiting) {
                this.waiting.abort();
                this.waiting = null;
                await new Promise(resolve => this.grab({ steal: true }, resolve));
            }
        },
        // Requests the lock and holds it until release() is called. done(true) once it is held (or if locks turn
        // out to be unusable), done(false) if ifAvailable found it taken.
        grab(options, done) {
            let held = false;
            navigator.locks.request(this.lockName, options, lock => {
                if (!lock) {
                    done(false);
                    return;
                }
                held = true;
                done(true);
                return new Promise(release => this.release = release);
            }).catch(e => {
                if (held) {
                    // Another tab took the lock without waiting for our save: stop saving, and queue again.
                    this.release = null;
                    this.dotnet.invokeMethodAsync("OnTakenOver");
                    this.wait();
                } else if (!e || e.name !== "AbortError") {
                    done(true); // the request itself failed, so there is no guard to obey
                }
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
