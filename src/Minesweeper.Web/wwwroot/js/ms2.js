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
