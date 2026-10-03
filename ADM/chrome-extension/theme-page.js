"use strict";

(() => {
    const apply = appearance => globalThis.AdmExtensionTheme?.apply(document.documentElement, appearance);
    globalThis.AdmExtensionTheme?.load(stored => { if (stored) apply(stored); });
    globalThis.AdmApplyPageTheme = apply;
})();
