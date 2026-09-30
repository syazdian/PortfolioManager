window.ibrokerManualLaunch = {
    open() {
        const dialog = document.getElementById('ibroker-manual-launch-modal');
        if (dialog) {
            dialog.showModal();
        }
    },
    close() {
        const dialog = document.getElementById('ibroker-manual-launch-modal');
        if (dialog) {
            dialog.close();
        }
    },
    copy(command) {
        if (command) {
            navigator.clipboard.writeText(command);
        }
    },
    openBrowser(url) {
        if (url) {
            window.open(url, '_blank', 'noopener');
        }
    }
};
