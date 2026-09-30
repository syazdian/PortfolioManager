window.ibrokerBrowserSync = {
    async fetchPositions(loginUrl, brokerAccountId) {
        if (!loginUrl) {
            throw new Error('IBroker login URL is not configured.');
        }

        if (!brokerAccountId) {
            throw new Error('IBroker account ID is not configured.');
        }

        const baseUrl = new URL(loginUrl, window.location.href);
        const requestUrl = new URL(`/v1/api/portfolio/${encodeURIComponent(brokerAccountId)}/positions/0`, baseUrl);

        const response = await fetch(requestUrl, {
            method: 'GET',
            credentials: 'include',
            cache: 'no-store',
            mode: 'cors'
        });

        const body = await response.text();
        if (!response.ok) {
            throw new Error(`IBroker fetch failed (${response.status} ${response.statusText}): ${body}`);
        }

        return body;
    },
    async copyText(text) {
        if (text) {
            await navigator.clipboard.writeText(text);
        }
    }
};
