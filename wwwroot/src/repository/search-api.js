/**
 * Every call the dashboards make to the package's backoffice API.
 *
 * The token is fetched per request rather than held, because the backoffice refreshes it in the
 * background and a cached copy would go stale on a long-lived dashboard.
 */

/** Matches VersionedApiBackOfficeRoute + ApiVersion("1.0") on ImobisoftSearchControllerBase. */
const API_ROOT = '/umbraco/management/api/v1/imobisoft-search';

export class SearchApi {
	#getToken;

	/**
	 * @param {() => Promise<string | undefined>} getToken Resolves the current backoffice access token.
	 */
	constructor(getToken) {
		this.#getToken = getToken;
	}

	// --- profiles ---

	getProfiles() {
		return this.#request('GET', '/profile');
	}

	getProfile(key) {
		return this.#request('GET', `/profile/${key}`);
	}

	createProfile(profile) {
		return this.#request('POST', '/profile', profile);
	}

	updateProfile(key, profile) {
		return this.#request('PUT', `/profile/${key}`, profile);
	}

	setDefaultProfile(key) {
		return this.#request('POST', `/profile/${key}/default`);
	}

	deleteProfile(key) {
		return this.#request('DELETE', `/profile/${key}`);
	}

	// --- discovery ---

	getCatalog() {
		return this.#request('GET', '/catalog');
	}

	getIndexes(includeFields = true) {
		return this.#request('GET', `/catalog/index?includeFields=${includeFields}`);
	}

	getSearchableFields(indexName) {
		return this.#request('GET', `/catalog/index/${encodeURIComponent(indexName)}/searchable-field`);
	}

	// --- previewing ---

	preview(request) {
		return this.#request('POST', '/preview', request);
	}

	autocomplete(term, profileAlias, take = 0) {
		const params = new URLSearchParams({ term });
		if (profileAlias) params.set('profileAlias', profileAlias);
		if (take) params.set('take', String(take));

		return this.#request('GET', `/autocomplete?${params}`);
	}

	// --- insights and settings ---

	getInsights(days = 30, take = 25) {
		return this.#request('GET', `/insights?days=${days}&take=${take}`);
	}

	purgeInsights() {
		return this.#request('POST', '/insights/purge');
	}

	getSettings() {
		return this.#request('GET', '/settings');
	}

	saveSettings(settings) {
		return this.#request('PUT', '/settings', settings);
	}

	/**
	 * @returns {Promise<{ data?: any, error?: string }>} Never throws, so a failed call renders as a
	 * message in the dashboard instead of an unhandled rejection in the console.
	 */
	async #request(method, path, body) {
		let response;

		try {
			const token = await this.#getToken();

			response = await fetch(`${API_ROOT}${path}`, {
				method,
				headers: {
					'Content-Type': 'application/json',
					Accept: 'application/json',
					...(token ? { Authorization: `Bearer ${token}` } : {}),
				},
				body: body === undefined ? undefined : JSON.stringify(body),
			});
		} catch (networkError) {
			return { error: `Could not reach the server: ${networkError.message}` };
		}

		if (response.status === 204) {
			return { data: null };
		}

		const raw = await response.text();
		let payload;

		try {
			payload = raw ? JSON.parse(raw) : null;
		} catch {
			payload = null;
		}

		if (response.ok) {
			return { data: payload };
		}

		if (response.status === 401 || response.status === 403) {
			return {
				error:
					'You do not have access to the Search section. Ask an administrator to grant it under Users > User groups.',
			};
		}

		// The API returns ProblemDetails for expected failures; prefer its wording over a status code.
		return { error: payload?.detail || payload?.title || `Request failed with status ${response.status}.` };
	}
}
