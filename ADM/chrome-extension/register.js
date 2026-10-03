document.addEventListener('DOMContentLoaded', function () {
    window.setTimeout(()=>{
        document.getElementById("link").click();
    },1000);
    document.getElementById("link").href = "adm-app:chrome-extension://" + chrome.runtime.id + "/";
}, false);