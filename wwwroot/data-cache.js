// Data cache and API client for Imobisoft Search Management endpoints.

export function getBaseApiUrl() {
    try {
        const component = document.querySelector('imobisoft-search-workspace');
        if (component?.hasAttribute('data-api-url')) {
            return component.getAttribute('data-api-url').replace(/\/$/, '') + '/';
        }
        if (window.IMOBISOFT_SEARCH_CONFIG?.apiUrl) {
            return window.IMOBISOFT_SEARCH_CONFIG.apiUrl.replace(/\/$/, '') + '/';
        }
    } catch (e) {
        console.warn('Error determining API URL:', e);
    }
    return '/umbraco/management/api/v1/imobisoft-search/';
}

const PROFILES_TTL_MS = 30000;
const CATALOG_TTL_MS = 60000;
const SETTINGS_TTL_MS = 30000;

/** @type {Map<string, {at: number, ttl: number, promise: Promise<any>}>} */
const _entries = new Map();

function _read(key, ttl, producer) {
    const hit = _entries.get(key);
    if (hit && (performance.now() - hit.at) < hit.ttl) return hit.promise;

    const forget = () => {
        const current = _entries.get(key);
        if (current && current.promise === promise) _entries.delete(key);
    };

    const promise = producer().then(
        result => {
            if (!result || result.ok === false) forget();
            return result;
        },
        err => {
            forget();
            throw err;
        }
    );

    _entries.set(key, { at: performance.now(), ttl, promise });
    return promise;
}

async function _json(fetchFn, url, options = {}) {
    // options is spread first, then headers: spreading options last would overwrite the merged
    // header object with the caller's own and drop the Accept header on every POST and PUT.
    const response = await fetchFn(url, {
        ...options,
        headers: {
            'Accept': 'application/json',
            ...(options.headers || {})
        }
    });

    let data = null;
    try {
        const text = await response.text();
        if (text && text.trim().length > 0) {
            data = JSON.parse(text);
        }
    } catch {
        data = null;
    }

    return { ok: response.ok, status: response.status, data };
}

// ----------------- PROFILES API -----------------

export function getProfiles(fetchFn, { force = false } = {}) {
    const url = `${getBaseApiUrl()}profile`;
    if (force) _entries.delete("profiles");
    return _read("profiles", PROFILES_TTL_MS, () => _json(fetchFn, url));
}

export function getProfile(fetchFn, key, { force = false } = {}) {
    const cacheKey = `profile:${String(key).toLowerCase()}`;
    const url = `${getBaseApiUrl()}profile/${encodeURIComponent(key)}`;
    if (force) _entries.delete(cacheKey);
    return _read(cacheKey, PROFILES_TTL_MS, () => _json(fetchFn, url));
}

export async function createProfile(fetchFn, profile) {
    const url = `${getBaseApiUrl()}profile`;
    const res = await _json(fetchFn, url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(profile)
    });
    invalidateProfiles();
    return res;
}

export async function updateProfile(fetchFn, key, profile) {
    const url = `${getBaseApiUrl()}profile/${encodeURIComponent(key)}`;
    const res = await _json(fetchFn, url, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(profile)
    });
    invalidateProfiles();
    return res;
}

export async function setDefaultProfile(fetchFn, key) {
    const url = `${getBaseApiUrl()}profile/${encodeURIComponent(key)}/default`;
    const res = await _json(fetchFn, url, { method: 'POST' });
    invalidateProfiles();
    return res;
}

export async function deleteProfile(fetchFn, key) {
    const url = `${getBaseApiUrl()}profile/${encodeURIComponent(key)}`;
    const res = await _json(fetchFn, url, { method: 'DELETE' });
    invalidateProfiles();
    return res;
}

export function invalidateProfiles() {
    for (const key of Array.from(_entries.keys())) {
        if (key === "profiles" || key.startsWith("profile:")) _entries.delete(key);
    }
}

// ----------------- CATALOG API -----------------

export function getCatalog(fetchFn, { force = false } = {}) {
    const url = `${getBaseApiUrl()}catalog`;
    if (force) _entries.delete("catalog");
    return _read("catalog", CATALOG_TTL_MS, () => _json(fetchFn, url));
}

export function getIndexes(fetchFn, includeFields = true, { force = false } = {}) {
    const cacheKey = `catalog:indexes:${includeFields}`;
    const url = `${getBaseApiUrl()}catalog/index?includeFields=${includeFields}`;
    if (force) _entries.delete(cacheKey);
    return _read(cacheKey, CATALOG_TTL_MS, () => _json(fetchFn, url));
}

export function getSearchableFields(fetchFn, indexName, { force = false } = {}) {
    const cacheKey = `catalog:fields:${indexName}`;
    const url = `${getBaseApiUrl()}catalog/index/${encodeURIComponent(indexName)}/searchable-field`;
    if (force) _entries.delete(cacheKey);
    return _read(cacheKey, CATALOG_TTL_MS, () => _json(fetchFn, url));
}

export function getContentTypes(fetchFn, { force = false } = {}) {
    const url = `${getBaseApiUrl()}catalog/content-type`;
    if (force) _entries.delete("catalog:content-type");
    return _read("catalog:content-type", CATALOG_TTL_MS, () => _json(fetchFn, url));
}

export function getMediaTypes(fetchFn, { force = false } = {}) {
    const url = `${getBaseApiUrl()}catalog/media-type`;
    if (force) _entries.delete("catalog:media-type");
    return _read("catalog:media-type", CATALOG_TTL_MS, () => _json(fetchFn, url));
}

export function getLanguages(fetchFn, { force = false } = {}) {
    const url = `${getBaseApiUrl()}catalog/language`;
    if (force) _entries.delete("catalog:language");
    return _read("catalog:language", CATALOG_TTL_MS, () => _json(fetchFn, url));
}

// Themes shipped in the package plus any the site added under its own Views/Partials/Search/Themes.
// Cached like the rest of the catalog: the set only changes when the site is redeployed.
export function getThemes(fetchFn, { force = false } = {}) {
    const url = `${getBaseApiUrl()}catalog/theme`;
    if (force) _entries.delete("catalog:theme");
    return _read("catalog:theme", CATALOG_TTL_MS, () => _json(fetchFn, url));
}

// Resolves one node key to its display name for chips and summaries. Results are cached per key
// for the session - names rarely change and the endpoint is cheap, but pickers can render many.
const _nodeNameCache = new Map();

export async function getNodeName(fetchFn, key) {
    if (_nodeNameCache.has(key)) {
        return { ok: true, status: 200, data: _nodeNameCache.get(key) };
    }

    const url = `${getBaseApiUrl()}catalog/node?key=${encodeURIComponent(key)}`;
    const res = await _json(fetchFn, url);

    if (res.ok && res.data?.name) {
        _nodeNameCache.set(key, res.data);
    }

    return res;
}

// ----------------- INSIGHTS API -----------------

export function getInsights(fetchFn, days = 30, take = 25, { force = false } = {}) {
    const cacheKey = `insights:${days}:${take}`;
    const url = `${getBaseApiUrl()}insights?days=${days}&take=${take}`;
    if (force) _entries.delete(cacheKey);
    return _read(cacheKey, 10000, () => _json(fetchFn, url));
}

export async function purgeInsights(fetchFn) {
    const url = `${getBaseApiUrl()}insights/purge`;
    const res = await _json(fetchFn, url, { method: 'POST' });
    for (const key of Array.from(_entries.keys())) {
        if (key.startsWith("insights:")) _entries.delete(key);
    }
    return res;
}

// ----------------- SETTINGS API -----------------

export function getSettings(fetchFn, { force = false } = {}) {
    const url = `${getBaseApiUrl()}settings`;
    if (force) _entries.delete("settings");
    return _read("settings", SETTINGS_TTL_MS, () => _json(fetchFn, url));
}

export async function updateSettings(fetchFn, settings) {
    const url = `${getBaseApiUrl()}settings`;
    const res = await _json(fetchFn, url, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(settings)
    });
    _entries.delete("settings");
    return res;
}

// Verifies an AI credential against the provider. Never cached - the whole point is to find out
// what is true right now, and a cached "Connected" for a key that has since been revoked is worse
// than no test at all.
export async function testAiConnection(fetchFn, { apiKey = '', model = '' } = {}) {
    const url = `${getBaseApiUrl()}settings/ai/test`;
    return _json(fetchFn, url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ apiKey, model })
    });
}

// ----------------- PREVIEW & AUTOCOMPLETE API -----------------

export async function previewSearch(fetchFn, request) {
    const url = `${getBaseApiUrl()}preview`;
    return _json(fetchFn, url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(request)
    });
}

// Runs the search and returns it rendered through the profile's theme, as the markup the site
// would serve. Never cached - it is the live preview of whatever rules are being edited.
export async function renderPreview(fetchFn, request) {
    const url = `${getBaseApiUrl()}preview/render`;
    return _json(fetchFn, url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(request)
    });
}

export async function getAutocomplete(fetchFn, term, profileAlias = "", take = 10) {
    let url = `${getBaseApiUrl()}autocomplete?term=${encodeURIComponent(term || '')}&take=${take}`;
    if (profileAlias) {
        url += `&profileAlias=${encodeURIComponent(profileAlias)}`;
    }
    return _json(fetchFn, url);
}
