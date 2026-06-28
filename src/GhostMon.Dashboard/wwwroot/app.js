window.ghostMon = window.ghostMon || {};

window.ghostMon.copyText = async (text) => {
    if (!navigator.clipboard || !navigator.clipboard.writeText) {
        return;
    }

    await navigator.clipboard.writeText(text ?? "");
};
