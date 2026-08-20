import { LitElement } from '@umbraco-cms/backoffice/external/lit';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { SearchApi } from '../repository/search-api.js';

/**
 * Base for the package's dashboards. Handles the two things every one of them needs: an
 * authenticated API client, and a way to tell the editor what happened.
 *
 * Contexts resolve asynchronously, so `firstLoad()` runs once the auth context has arrived rather
 * than in `connectedCallback` - loading earlier would fire requests with no bearer token.
 */
export class ImobisoftSearchElement extends UmbElementMixin(LitElement) {
	static properties = {
		loading: { type: Boolean, state: true },
		error: { type: String, state: true },
	};

	#authContext;
	#notificationContext;
	#loaded = false;

	/** @type {SearchApi} */
	api = new SearchApi(() => this.#latestToken());

	constructor() {
		super();
		this.loading = true;
		this.error = '';

		this.consumeContext(UMB_AUTH_CONTEXT, (context) => {
			this.#authContext = context;

			if (context && !this.#loaded) {
				this.#loaded = true;
				this.firstLoad();
			}
		});

		this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
			this.#notificationContext = context;
		});
	}

	/** Override to load data once authentication is available. */
	async firstLoad() {}

	async #latestToken() {
		return this.#authContext?.getLatestToken();
	}

	/**
	 * Unwraps an API call, routing failures to the error banner so no dashboard renders a blank box
	 * when something goes wrong.
	 * @returns the payload, or undefined when the call failed.
	 */
	async call(promise, { silent = false } = {}) {
		const { data, error } = await promise;

		if (error) {
			this.error = error;

			if (!silent) {
				this.notify('warning', error);
			}

			return undefined;
		}

		this.error = '';
		return data;
	}

	notify(color, message, headline) {
		this.#notificationContext?.peek(color, { data: { headline, message } });
	}
}
