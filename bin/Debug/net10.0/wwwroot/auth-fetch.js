// Authentication helper for Imobisoft Search backoffice API requests.
// Resolves the Backoffice bearer token via Umbraco's UMB_AUTH_CONTEXT and handles automatic 401 retry.

const CONTEXT_WAIT_MS = 10000;
const CONTEXT_POLL_MS = 25;

/**
 * Resolves with the host's auth context once it exists.
 */
async function _awaitAuthContext(host) {
    if (host._authContext) return host._authContext;

    const deadline = performance.now() + CONTEXT_WAIT_MS;
    while (!host._authContext && performance.now() < deadline) {
        await new Promise(resolve => setTimeout(resolve, CONTEXT_POLL_MS));
    }
    return host._authContext;
}

/**
 * Gets the current access token.
 */
async function _token(authContext) {
    if (!authContext) return "";
    try {
        return (await authContext.getLatestToken()) || "";
    } catch (e) {
        console.error("Error getting auth token:", e);
        return "";
    }
}

/**
 * fetch() with the backoffice bearer token attached, waiting for the token rather than racing it.
 */
export async function fetchWithAuth(host, url, options = {}) {
    const authContext = await _awaitAuthContext(host);
    const token = await _token(authContext);

    const headers = { ...(options.headers || {}) };
    if (token) {
        headers["Authorization"] = `Bearer ${token}`;
    }

    const response = await fetch(url, { ...options, headers });
    if (response.status !== 401 || !authContext) return response;

    // Retry once with a freshly fetched token
    const freshToken = await _token(authContext);
    if (!freshToken || freshToken === token) return response;

    return fetch(url, { ...options, headers: { ...headers, "Authorization": `Bearer ${freshToken}` } });
}
