"use strict";

class VideoPopup {
    run() {
        document.addEventListener("DOMContentLoaded", this.onLoad.bind(this), false);
    }

    onLoad() {
        chrome.tabs.query({ active: true, currentWindow: true }, tabs => {
            void chrome.runtime.lastError;
            chrome.runtime.sendMessage({ type: "stat", tabId: tabs?.[0]?.id }, this.onMsg.bind(this));
        });
        document.getElementById("chk").addEventListener("change", () => {
            chrome.runtime.sendMessage({ type: "cmd", enabled: document.getElementById("chk").checked });
            window.close();
        });
        document.getElementById("clear").addEventListener("click", () => {
            chrome.runtime.sendMessage({ type: "clear" });
            window.close();
        });
    }

    onMsg(response) {
        void chrome.runtime.lastError;
        if (response?.appearance) globalThis.AdmApplyPageTheme?.(response.appearance);
        const list = Array.isArray(response?.list) ? response.list : [];
        document.getElementById("chk").checked = response?.enabled === true;
        document.getElementById("actions").hidden = list.length === 0;
        document.getElementById("count").textContent = list.length > 0
            ? `${list.length} item${list.length === 1 ? "" : "s"} detected` : "";
        this.renderList(list);
    }

    renderList(items) {
        const container = document.getElementById("list");
        const empty = document.getElementById("empty");
        if (items.length === 0) return;
        empty.remove();
        const groups = [
            ["Found by yt-dlp", items.filter(item => String(item?.source || "") === "ytdlp")],
            ["Detected in the browser", items.filter(item => String(item?.source || "") !== "ytdlp")]
        ];
        for (const [heading, groupItems] of groups) {
            if (groupItems.length === 0) continue;
            const label = document.createElement("div");
            label.className = "adm-group";
            label.setAttribute("role", "presentation");
            label.textContent = `${heading} (${groupItems.length})`;
            container.appendChild(label);
            this.renderGroup(container, groupItems);
        }
    }

    renderGroup(container, items) {
        for (const item of items) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "adm-item";
            button.setAttribute("role", "menuitem");
            const title = document.createElement("span");
            title.className = "adm-item-title";
            title.dir = "auto";
            title.textContent = String(item?.text || "Detected media");
            title.title = title.textContent;
            const detail = document.createElement("span");
            detail.className = "adm-item-detail";
            detail.textContent = String(item?.info || "");
            button.append(title, detail);
            button.addEventListener("click", () => {
                button.disabled = true;
                chrome.runtime.sendMessage({ type: "vid", itemId: String(item?.id ?? "") }, () => {
                    void chrome.runtime.lastError;
                    window.close();
                });
            });
            container.appendChild(button);
        }
    }
}

new VideoPopup().run();
