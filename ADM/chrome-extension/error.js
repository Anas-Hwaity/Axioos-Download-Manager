"use strict";

window.addEventListener("load", () => {
    const button = document.getElementById("OpenLink");
    if (!button) return;
    button.addEventListener("click", () => {
        button.disabled = true;
        chrome.runtime.sendMessage({ type: "ensure-desktop" }, response => {
            const failed = Boolean(chrome.runtime.lastError) || response?.accepted !== true;
            void chrome.runtime.lastError;
            if (!failed) {
                window.close();
                return;
            }
            button.disabled = false;
        });
    });
});
