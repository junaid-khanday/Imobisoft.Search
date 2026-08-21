import { LitElement, html, css, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { fetchWithAuth } from "./auth-fetch.js";
import {
    getProfiles,
    getProfile,
    createProfile,
    updateProfile,
    setDefaultProfile,
    deleteProfile,
    invalidateProfiles,
    getCatalog,
    getIndexes,
    getSearchableFields,
    getInsights,
    purgeInsights,
    getSettings,
    updateSettings,
    previewSearch,
    getAutocomplete
} from "./data-cache.js";

const VERSION = "1.0.0";

export class ImobisoftSearchWorkspace extends UmbElementMixin(LitElement) {
    static properties = {
        _loading: { state: true },
        _activeTab: { state: true },
        _currentView: { state: true },
        _profiles: { state: true },
        _currentProfile: { state: true },
        _showProfilesDropdown: { state: true },
        _catalog: { state: true },
        _insights: { state: true },
        _insightsDays: { state: true },
        _insightsActiveReport: { state: true },
        _settings: { state: true },
        _settingsSaving: { state: true },
        _profileSaving: { state: true },
        _profileActiveTab: { state: true },
        _filterProfilesQuery: { state: true },
        _sidePanelOpen: { state: true },
        _sidePanelType: { state: true },
        _sidePanelData: { state: true },
        _sidePanelErrors: { state: true },
        _testQuery: { state: true },
        _testProfileAlias: { state: true },
        _testCulture: { state: true },
        _testActiveFilters: { state: true },
        _testResults: { state: true },
        _testSearching: { state: true },
        _testAutocompleteSuggestions: { state: true },
        _testShowAutocomplete: { state: true },
        _testDiagnosticsOpen: { state: true },
        _showMessageBox: { state: true },
        _messageBoxType: { state: true },
        _messageBoxTitle: { state: true },
        _messageBoxMessage: { state: true },
        _messageBoxConfirmText: { state: true },
        _messageBoxCancelText: { state: true },
        _toasts: { state: true },
        _showImportModal: { state: true },
        _importJsonText: { state: true },
        _showFilterTypePicker: { state: true }
    };

    static _filterTypes = [
        {
            alias: 'contentType',
            name: 'Document Type',
            desc: 'Filter search results by Umbraco document types (Policies, News, Articles)',
            defaultField: '__NodeTypeAlias',
            defaultKind: 'field'
        },
        {
            alias: 'contentNode',
            name: 'Content Page / Subtree',
            desc: 'Filter search results to specific pages or policy subtrees using document picker',
            defaultField: '__Path',
            defaultKind: 'field'
        },
        {
            alias: 'dateRange',
            name: 'Date Range & Year',
            desc: 'Filter search results by calendar years (2026, 2025, 2024) or relative intervals',
            defaultField: 'updateDate',
            defaultKind: 'dateRange'
        },
        {
            alias: 'numeric',
            name: 'Numeric',
            desc: 'Filter search results by price tiers, rating scores, or numeric intervals',
            defaultField: 'price',
            defaultKind: 'numeric'
        },
        {
            alias: 'field',
            name: 'Field / Taxonomy / Tag',
            desc: 'Filter search results dynamically by distinct values in Examine fields',
            defaultField: '',
            defaultKind: 'field'
        }
    ];

    constructor() {
        super();
        this._notificationContext = null;
        this.consumeContext(UMB_NOTIFICATION_CONTEXT, (instance) => { this._notificationContext = instance; });

        this.consumeContext(UMB_AUTH_CONTEXT, (authContext) => {
            this._authContext = authContext;
            if (authContext) {
                this._initialLoad();
            }
        });

        this._loading = false;
        this._tabs = [
            { alias: 'manageProfiles', label: 'Profiles', icon: 'icon-sliders' },
            { alias: 'testSearch', label: 'Test Search', icon: 'icon-search' },
            { alias: 'insights', label: 'Insights', icon: 'icon-chart-curve' },
            { alias: 'catalog', label: 'Indexes', icon: 'icon-server' },
            { alias: 'settings', label: 'Settings', icon: 'icon-settings' }
        ];
        this._activeTab = this._tabs[0];
        this._currentView = 'list';
        this._profiles = [];
        this._currentProfile = this._createEmptyProfile();
        this._originalProfileJson = '';
        this._showProfilesDropdown = true;
        this._catalog = { indexes: [], contentTypes: [], mediaTypes: [], languages: [] };
        this._insights = null;
        this._insightsDays = 30;
        this._insightsActiveReport = 'zero';
        this._settings = this._createEmptySettings();
        this._settingsSaving = false;
        this._profileSaving = false;
        this._profileActiveTab = 'sources';
        this._filterProfilesQuery = '';
        this._sidePanelOpen = false;
        this._sidePanelType = null;
        this._sidePanelData = null;
        this._sidePanelErrors = {};
        this._testQuery = '';
        this._testProfileAlias = 'default';
        this._testCulture = '';
        this._testActiveFilters = {};
        this._testResults = null;
        this._testSearching = false;
        this._testAutocompleteSuggestions = [];
        this._testShowAutocomplete = false;
        this._testDiagnosticsOpen = true;
        this._showMessageBox = false;
        this._messageBoxType = 'confirm';
        this._messageBoxTitle = '';
        this._messageBoxMessage = '';
        this._messageBoxCallback = null;
        this._messageBoxConfirmText = 'OK';
        this._messageBoxCancelText = 'Cancel';
        this._toasts = [];
        this._showImportModal = false;
        this._importJsonText = '';
        this._showFilterTypePicker = false;
    }

    connectedCallback() {
        super.connectedCallback();
        this._onBeforeUnload = (e) => {
            if (this._hasUnsavedChanges()) {
                e.preventDefault();
                e.returnValue = '';
            }
        };
        window.addEventListener('beforeunload', this._onBeforeUnload);
    }

    disconnectedCallback() {
        super.disconnectedCallback();
        if (this._onBeforeUnload) {
            window.removeEventListener('beforeunload', this._onBeforeUnload);
        }
    }

    _fetch(url, options = {}) {
        return fetchWithAuth(this, url, options);
    }

    async _initialLoad() {
        this._loading = true;
        this.requestUpdate();
        try {
            await Promise.all([
                this._loadProfiles(),
                this._loadCatalog(),
                this._loadSettings()
            ]);
        } catch (err) {
            console.error("Initial load error:", err);
            this._showToast("Failed to connect to search service.", "error");
        } finally {
            this._loading = false;
            this.requestUpdate();
        }
    }

    // ----------------- DATA LOADING -----------------

    async _loadProfiles(force = false) {
        try {
            const res = await getProfiles(this._fetch.bind(this), { force });
            if (res.ok && res.data) {
                this._profiles = Array.isArray(res.data) ? res.data : [];
                if (!this._testProfileAlias && this._profiles.length > 0) {
                    const def = this._profiles.find(p => p.isDefault) || this._profiles[0];
                    this._testProfileAlias = def.alias || 'default';
                }
            }
        } catch (e) {
            console.error("Failed to load profiles:", e);
        }
    }

    async _loadCatalog(force = false) {
        try {
            const res = await getCatalog(this._fetch.bind(this), { force });
            if (res.ok && res.data) {
                this._catalog = {
                    indexes: res.data.indexes || [],
                    contentTypes: res.data.contentTypes || [],
                    mediaTypes: res.data.mediaTypes || [],
                    languages: res.data.languages || []
                };
            }
        } catch (e) {
            console.error("Failed to load catalog:", e);
        }
    }

    async _loadSettings(force = false) {
        try {
            const res = await getSettings(this._fetch.bind(this), { force });
            if (res.ok && res.data) {
                this._settings = {
                    analytics: {
                        enabled: res.data.analytics?.enabled ?? true,
                        retentionDays: res.data.analytics?.retentionDays ?? 90,
                        recordZeroResultsOnly: res.data.analytics?.recordZeroResultsOnly ?? false,
                        minimumTermLength: res.data.analytics?.minimumTermLength ?? 2,
                        trackClicks: res.data.analytics?.trackClicks ?? true
                    },
                    suggestions: {
                        enabled: res.data.suggestions?.enabled ?? true,
                        suggestBelowResultCount: res.data.suggestions?.suggestBelowResultCount ?? 3,
                        fuzziness: res.data.suggestions?.fuzziness ?? 0.65,
                        maximumEditDistance: res.data.suggestions?.maximumEditDistance ?? 3,
                        autocompleteSize: res.data.suggestions?.autocompleteSize ?? 10
                    }
                };
            }
        } catch (e) {
            console.error("Failed to load settings:", e);
        }
    }

    async _loadInsights(days = this._insightsDays, force = false) {
        this._loading = true;
        this.requestUpdate();
        try {
            const res = await getInsights(this._fetch.bind(this), days, 50, { force });
            if (res.ok && res.data) {
                this._insights = res.data;
            } else {
                this._insights = null;
            }
        } catch (e) {
            console.error("Failed to load insights:", e);
            this._showToast("Could not load insights report.", "error");
        } finally {
            this._loading = false;
            this.requestUpdate();
        }
    }

    // ----------------- PROFILE FACTORY & SERIALIZATION -----------------

    _createEmptyProfile() {
        return {
            key: this._generateGuid(),
            alias: 'new-profile',
            name: 'New Search Profile',
            isDefault: false,
            enabled: true,
            rules: {
                sources: {
                    indexes: [],
                    indexTypes: [],
                    includeContentTypes: [],
                    excludeContentTypes: [],
                    includeMediaTypes: [],
                    excludeMediaTypes: [],
                    rootNodeKeys: [],
                    excludedNodeKeys: [],
                    excludeDescendantsOfExcludedNodes: true,
                    cultures: [],
                    respectNaviHide: true,
                    excludeProtected: true,
                    publishedOnly: true
                },
                matching: {
                    fields: [],
                    defaultOperator: 'or',
                    fuzziness: 0.8,
                    minimumQueryLength: 2,
                    minimumScore: 0,
                    stopWords: [],
                    synonyms: {},
                    allTermsMustMatch: false
                },
                ranking: {
                    sortBy: [],
                    contentTypeBoosts: {},
                    bestBets: [],
                    blockedTerms: [],
                    recency: {
                        enabled: false,
                        field: 'updateDate',
                        halfLifeDays: 90,
                        weight: 0.5
                    }
                },
                results: {
                    pageSize: 10,
                    maxResults: 500,
                    returnFields: [],
                    groupByContentType: false,
                    deduplicateByField: '',
                    highlight: {
                        enabled: false,
                        highlightMatches: true,
                        mode: 'sentence',
                        field: '',
                        snippetLength: 200,
                        sentenceContext: 0,
                        startTag: '<mark>',
                        endTag: '</mark>'
                    },
                    facets: []
                }
            }
        };
    }

    _createEmptySettings() {
        return {
            analytics: {
                enabled: true,
                retentionDays: 90,
                recordZeroResultsOnly: false,
                minimumTermLength: 2,
                trackClicks: true
            },
            suggestions: {
                enabled: true,
                suggestBelowResultCount: 3,
                fuzziness: 0.65,
                maximumEditDistance: 3,
                autocompleteSize: 10
            }
        };
    }

    _generateGuid() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            const r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    _normalizeProfileJson(p) {
        if (!p) return '';
        return JSON.stringify(p, (k, v) => {
            if (typeof k === 'string' && k.startsWith('_')) return undefined;
            return v;
        });
    }

    _hasUnsavedChanges() {
        if (this._activeTab.alias === 'manageProfiles' && this._currentView === 'editor' && this._currentProfile) {
            return this._normalizeProfileJson(this._currentProfile) !== this._originalProfileJson;
        }
        return false;
    }

    // ----------------- NAVIGATION -----------------

    _changeTab(tab) {
        if (this._hasUnsavedChanges()) {
            this._showConfirm("Unsaved Changes", "You have unsaved changes in this profile. Are you sure you want to leave?", (ok) => {
                if (ok) {
                    this._activeTab = tab;
                    if (tab.alias === 'manageProfiles') this._currentView = 'list';
                    if (tab.alias === 'insights' && !this._insights) this._loadInsights();
                    this.requestUpdate();
                }
            });
            return;
        }
        this._activeTab = tab;
        if (tab.alias === 'manageProfiles') {
            this._currentView = 'list';
        } else if (tab.alias === 'insights') {
            this._loadInsights();
        } else if (tab.alias === 'catalog') {
            this._loadCatalog(true);
        } else if (tab.alias === 'settings') {
            this._loadSettings(true);
        }
        this.requestUpdate();
    }

    _goToList() {
        if (this._hasUnsavedChanges()) {
            this._showConfirm("Unsaved Changes", "You have unsaved changes in this profile. Are you sure you want to exit without saving?", (ok) => {
                if (ok) {
                    this._currentView = 'list';
                    this.requestUpdate();
                }
            });
            return;
        }
        this._currentView = 'list';
        this.requestUpdate();
    }

    _goToCreateProfile() {
        if (this._hasUnsavedChanges()) {
            this._showConfirm("Unsaved Changes", "Discard unsaved changes to start a new profile?", (ok) => {
                if (ok) this._startCreateProfile();
            });
            return;
        }
        this._startCreateProfile();
    }

    _startCreateProfile() {
        const p = this._createEmptyProfile();
        if (this._profiles.length === 0) {
            p.alias = 'default';
            p.name = 'Default';
            p.isDefault = true;
        } else {
            p.alias = `profile-${this._profiles.length + 1}`;
            p.name = `Search Profile ${this._profiles.length + 1}`;
        }
        this._currentProfile = JSON.parse(JSON.stringify(p));
        this._originalProfileJson = this._normalizeProfileJson(this._currentProfile);
        this._currentView = 'editor';
        this._profileActiveTab = 'sources';
        this._activeTab = this._tabs[0];
        this.requestUpdate();
    }

    _editProfile(profile) {
        if (this._hasUnsavedChanges() && this._currentProfile?.key !== profile.key) {
            this._showConfirm("Unsaved Changes", "Discard unsaved changes and open this profile?", (ok) => {
                if (ok) this._loadProfileForEdit(profile.key);
            });
            return;
        }
        this._loadProfileForEdit(profile.key);
    }

    async _loadProfileForEdit(key) {
        this._loading = true;
        this.requestUpdate();
        try {
            const res = await getProfile(this._fetch.bind(this), key, { force: true });
            if (res.ok && res.data) {
                this._currentProfile = JSON.parse(JSON.stringify(res.data));
                this._ensureProfileStructure(this._currentProfile);
                this._originalProfileJson = this._normalizeProfileJson(this._currentProfile);
                this._currentView = 'editor';
                this._profileActiveTab = 'sources';
                this._activeTab = this._tabs[0];
            } else {
                this._showToast("Could not load search profile.", "error");
            }
        } catch (e) {
            console.error("Error loading profile:", e);
            this._showToast("Error loading search profile.", "error");
        } finally {
            this._loading = false;
            this.requestUpdate();
        }
    }

    _ensureProfileStructure(p) {
        if (!p.rules) p.rules = {};
        if (!p.rules.sources) p.rules.sources = {};
        if (!p.rules.sources.indexes) p.rules.sources.indexes = [];
        if (!p.rules.sources.indexTypes) p.rules.sources.indexTypes = [];
        if (!p.rules.sources.includeContentTypes) p.rules.sources.includeContentTypes = [];
        if (!p.rules.sources.excludeContentTypes) p.rules.sources.excludeContentTypes = [];
        if (!p.rules.sources.includeMediaTypes) p.rules.sources.includeMediaTypes = [];
        if (!p.rules.sources.excludeMediaTypes) p.rules.sources.excludeMediaTypes = [];
        if (!p.rules.sources.rootNodeKeys) p.rules.sources.rootNodeKeys = [];
        if (!p.rules.sources.startNodeKeys) p.rules.sources.startNodeKeys = p.rules.sources.rootNodeKeys || [];
        if (!p.rules.sources.excludedNodeKeys) p.rules.sources.excludedNodeKeys = [];
        if (p.rules.sources.excludeDescendantsOfExcludedNodes === undefined) p.rules.sources.excludeDescendantsOfExcludedNodes = true;
        if (!p.rules.sources.cultures) p.rules.sources.cultures = [];
        if (p.rules.sources.respectNaviHide === undefined) p.rules.sources.respectNaviHide = true;
        if (p.rules.sources.excludeProtected === undefined) p.rules.sources.excludeProtected = true;
        if (p.rules.sources.publishedOnly === undefined) p.rules.sources.publishedOnly = true;

        if (!p.rules.matching) p.rules.matching = {};
        if (!p.rules.matching.fields) p.rules.matching.fields = [];
        if (!p.rules.matching.defaultOperator) p.rules.matching.defaultOperator = 'or';
        if (p.rules.matching.fuzziness === undefined) p.rules.matching.fuzziness = 0.8;
        if (p.rules.matching.minimumQueryLength === undefined) p.rules.matching.minimumQueryLength = 2;
        if (p.rules.matching.minimumScore === undefined) p.rules.matching.minimumScore = 0;
        if (!p.rules.matching.stopWords) p.rules.matching.stopWords = [];
        if (!p.rules.matching.synonyms) p.rules.matching.synonyms = {};
        if (p.rules.matching.allTermsMustMatch === undefined) p.rules.matching.allTermsMustMatch = false;

        if (!p.rules.ranking) p.rules.ranking = {};
        if (!p.rules.ranking.sortBy) p.rules.ranking.sortBy = [];
        if (!p.rules.ranking.contentTypeBoosts) p.rules.ranking.contentTypeBoosts = {};
        if (!p.rules.ranking.bestBets) p.rules.ranking.bestBets = [];
        if (!p.rules.ranking.blockedTerms) p.rules.ranking.blockedTerms = [];
        if (!p.rules.ranking.recency) p.rules.ranking.recency = { enabled: false, field: 'updateDate', halfLifeDays: 90, weight: 0.5 };

        if (!p.rules.results) p.rules.results = {};
        if (p.rules.results.pageSize === undefined) p.rules.results.pageSize = 10;
        if (p.rules.results.maxResults === undefined) p.rules.results.maxResults = 500;
        if (!p.rules.results.returnFields) p.rules.results.returnFields = [];
        if (p.rules.results.groupByContentType === undefined) p.rules.results.groupByContentType = false;
        if (!p.rules.results.deduplicateByField) p.rules.results.deduplicateByField = '';
        if (!p.rules.results.highlight) p.rules.results.highlight = { enabled: false, highlightMatches: true, mode: 'sentence', field: '', snippetLength: 200, sentenceContext: 0, startTag: '<mark>', endTag: '</mark>' };
        if (!p.rules.results.facets) p.rules.results.facets = [];
    }

    // ----------------- PROFILE CRUD OPERATIONS -----------------

    async _saveCurrentProfile() {
        if (!this._currentProfile) return;
        if (!this._currentProfile.name || !this._currentProfile.name.trim()) {
            this._showToast("Profile name is required.", "warning");
            return;
        }
        if (!this._currentProfile.alias || !this._currentProfile.alias.trim()) {
            this._currentProfile.alias = this._currentProfile.name
                .toLowerCase()
                .replace(/[^a-z0-9_-]/g, '-')
                .replace(/-+/g, '-')
                .replace(/^-|-$/g, '') || 'profile';
        }

        const aliasPattern = /^[a-zA-Z0-9_-]+$/;
        if (!aliasPattern.test(this._currentProfile.alias.trim())) {
            this._currentProfile.alias = this._currentProfile.alias.toLowerCase().replace(/[^a-z0-9_-]/g, '-') || 'profile';
        }

        this._profileSaving = true;
        this.requestUpdate();

        try {
            const exists = this._profiles.some(p => String(p.key).toLowerCase() === String(this._currentProfile.key).toLowerCase());
            let res;
            if (exists) {
                res = await updateProfile(this._fetch.bind(this), this._currentProfile.key, this._currentProfile);
            } else {
                res = await createProfile(this._fetch.bind(this), this._currentProfile);
            }

            if (res.ok && res.data) {
                this._currentProfile = JSON.parse(JSON.stringify(res.data));
                this._ensureProfileStructure(this._currentProfile);
                this._originalProfileJson = this._normalizeProfileJson(this._currentProfile);
                await this._loadProfiles(true);
                this._showToast("Profile saved successfully!", "success");
            } else {
                const errorMsg = res.data?.detail || res.data?.title || "Failed to save search profile.";
                this._showToast(errorMsg, "error");
            }
        } catch (e) {
            console.error("Save profile error:", e);
            this._showToast("Unexpected error saving profile.", "error");
        } finally {
            this._profileSaving = false;
            this.requestUpdate();
        }
    }

    async _handleSetDefaultProfile(profile, e) {
        if (e) e.stopPropagation();
        try {
            const res = await setDefaultProfile(this._fetch.bind(this), profile.key);
            if (res.ok) {
                await this._loadProfiles(true);
                if (this._currentProfile && String(this._currentProfile.key).toLowerCase() === String(profile.key).toLowerCase()) {
                    this._currentProfile.isDefault = true;
                    this._originalProfileJson = this._normalizeProfileJson(this._currentProfile);
                }
                this._showToast(`"${profile.name}" is now the default search profile.`, "success");
            } else {
                this._showToast("Failed to set default profile.", "error");
            }
        } catch (err) {
            console.error("Error setting default profile:", err);
            this._showToast("Error setting default profile.", "error");
        }
    }

    _handleDeleteProfile(profile, e) {
        if (e) e.stopPropagation();
        if (this._profiles.length <= 1) {
            this._showAlert("Cannot Delete", "This is the only search profile. Create another profile before deleting this one.");
            return;
        }

        this._showConfirm("Delete Profile", `Are you sure you want to delete profile "${profile.name}"? This action cannot be undone.`, async (confirmed) => {
            if (!confirmed) return;
            try {
                const res = await deleteProfile(this._fetch.bind(this), profile.key);
                if (res.ok) {
                    await this._loadProfiles(true);
                    if (this._currentProfile?.key === profile.key) {
                        this._currentView = 'list';
                    }
                    this._showToast(`Profile "${profile.name}" deleted.`, "success");
                } else {
                    const msg = res.data?.detail || res.data?.title || "Failed to delete profile.";
                    this._showToast(msg, "error");
                }
            } catch (err) {
                console.error("Error deleting profile:", err);
                this._showToast("Error deleting profile.", "error");
            }
        }, "Delete", "Cancel");
    }

    _duplicateProfile(profile, e) {
        if (e) e.stopPropagation();
        const clone = JSON.parse(JSON.stringify(profile));
        clone.key = this._generateGuid();
        clone.name = `${profile.name} (Copy)`;
        clone.alias = `${profile.alias}-copy`;
        clone.isDefault = false;
        this._currentProfile = clone;
        this._originalProfileJson = '';
        this._currentView = 'editor';
        this._profileActiveTab = 'sources';
        this.requestUpdate();
        this._showToast("Profile duplicated. Review and save your changes.", "info");
    }

    _exportAllProfiles() {
        const json = JSON.stringify(this._profiles, null, 2);
        const blob = new Blob([json], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `imobisoft-search-profiles-${new Date().toISOString().slice(0, 10)}.json`;
        a.click();
        URL.revokeObjectURL(url);
        this._showToast("Profiles exported successfully.", "success");
    }

    _exportCurrentProfile() {
        if (!this._currentProfile) return;
        const json = JSON.stringify(this._currentProfile, null, 2);
        const blob = new Blob([json], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `search-profile-${this._currentProfile.alias}.json`;
        a.click();
        URL.revokeObjectURL(url);
    }

    _openImportModal() {
        this._importJsonText = '';
        this._showImportModal = true;
        this.requestUpdate();
    }

    async _handleImportSubmit() {
        if (!this._importJsonText || !this._importJsonText.trim()) {
            this._showToast("Please paste JSON content.", "warning");
            return;
        }

        try {
            const parsed = JSON.parse(this._importJsonText);
            const profilesToImport = Array.isArray(parsed) ? parsed : [parsed];

            let count = 0;
            for (const p of profilesToImport) {
                if (!p.name || !p.alias) continue;
                const newP = {
                    ...p,
                    key: this._generateGuid(),
                    isDefault: false
                };
                // Ensure unique alias
                let alias = newP.alias;
                let counter = 1;
                while (this._profiles.some(existing => existing.alias.toLowerCase() === alias.toLowerCase())) {
                    alias = `${newP.alias}-${counter++}`;
                }
                newP.alias = alias;

                const res = await createProfile(this._fetch.bind(this), newP);
                if (res.ok) count++;
            }

            this._showImportModal = false;
            await this._loadProfiles(true);
            this._showToast(`Successfully imported ${count} profile(s).`, "success");
        } catch (e) {
            console.error("Import parsing error:", e);
            this._showToast("Invalid JSON format. Please check your data.", "error");
        }
    }

    // ----------------- SIDE PANEL HANDLERS -----------------

    _openSidePanel(type, data = null) {
        this._sidePanelType = type;
        this._sidePanelErrors = {};

        if (type === 'editField') {
            this._sidePanelData = data ? JSON.parse(JSON.stringify(data)) : {
                name: '',
                boost: 1.0,
                matchMode: 'prefix',
                enabled: true,
                _isNew: !data
            };
        } else if (type === 'editFacet') {
            this._sidePanelData = data ? JSON.parse(JSON.stringify(data)) : {
                alias: '',
                label: '',
                field: '',
                kind: 'field',
                filterType: '',
                maxValues: 20,
                hideEmpty: true,
                enabled: true,
                ranges: [],
                _isNew: !data
            };
            if (data && data.enabled === undefined) {
                this._sidePanelData.enabled = true;
            }
            if (!Array.isArray(this._sidePanelData.ranges)) {
                this._sidePanelData.ranges = [];
            }
            if (data) {
                this._showFilterTypePicker = false;
                this._sidePanelData._origAlias = data.alias;
                const k = String(data.kind || '').toLowerCase();
                const f = String(data.field || '').toLowerCase();
                if (f === '__nodetypealias' || f === 'contenttypealias' || f === 'contenttype') {
                    this._sidePanelData.filterType = 'contentType';
                } else if (f === '__path' || f === 'path' || f === '__key' || f === 'key') {
                    this._sidePanelData.filterType = 'contentNode';
                } else if (k === 'daterange') {
                    this._sidePanelData.filterType = 'dateRange';
                } else if (k === 'numeric') {
                    this._sidePanelData.filterType = 'numeric';
                } else {
                    this._sidePanelData.filterType = 'field';
                }
            } else {
                this._showFilterTypePicker = true;
            }
        } else if (type === 'editBestBet') {
            this._sidePanelData = data ? JSON.parse(JSON.stringify(data)) : {
                terms: [],
                nodeKeys: [],
                _termsInput: '',
                _nodeKeysInput: '',
                _isNew: !data
            };
            if (data) {
                this._sidePanelData._termsInput = (data.terms || []).join(', ');
                this._sidePanelData._nodeKeysInput = (data.nodeKeys || []).join('\n');
            }
        } else if (type === 'editSort') {
            this._sidePanelData = data ? JSON.parse(JSON.stringify(data)) : {
                field: 'score',
                direction: 'descending',
                _isNew: !data
            };
        } else if (type === 'editContentTypeBoost') {
            this._sidePanelData = data ? JSON.parse(JSON.stringify(data)) : {
                contentType: '',
                boost: 1.5,
                _isNew: !data
            };
        } else if (type === 'editSynonym') {
            this._sidePanelData = data ? JSON.parse(JSON.stringify(data)) : {
                term: '',
                synonyms: [],
                _synonymsInput: '',
                _isNew: !data
            };
            if (data) {
                this._sidePanelData._synonymsInput = (data.synonyms || []).join(', ');
            }
        } else if (type === 'editSourceIndexes') {
            this._sidePanelData = {
                indexes: [...(this._currentProfile.rules?.sources?.indexes || [])]
            };
        } else if (type === 'editSourceEntityTypes') {
            this._sidePanelData = {
                indexTypes: [...(this._currentProfile.rules?.sources?.indexTypes || [])]
            };
        } else if (type === 'editSourceContentTypes') {
            this._sidePanelData = {
                includeContentTypes: [...(this._currentProfile.rules?.sources?.includeContentTypes || [])],
                _searchFilter: ''
            };
        } else if (type === 'editSourceExcludeContentTypes') {
            this._sidePanelData = {
                excludeContentTypes: [...(this._currentProfile.rules?.sources?.excludeContentTypes || [])],
                _searchFilter: ''
            };
        } else if (type === 'editSourceRoots') {
            const rootKeys = this._currentProfile.rules?.sources?.rootNodeKeys || this._currentProfile.rules?.sources?.startNodeKeys || [];
            this._sidePanelData = {
                rootNodeKeys: [...rootKeys],
                _rootsInput: rootKeys.join('\n'),
                excludeDescendantsOfExcludedNodes: this._currentProfile.rules?.sources?.excludeDescendantsOfExcludedNodes !== false
            };
        } else if (type === 'editSourceProtection') {
            this._sidePanelData = {
                publishedOnly: this._currentProfile.rules?.sources?.publishedOnly !== false,
                respectNaviHide: this._currentProfile.rules?.sources?.respectNaviHide !== false,
                excludeProtected: this._currentProfile.rules?.sources?.excludeProtected !== false
            };
        } else if (type === 'manageFields') {
            this._sidePanelData = {
                fields: JSON.parse(JSON.stringify(this._currentProfile.rules?.matching?.fields || []))
            };
        } else if (type === 'editMatchParameters') {
            this._sidePanelData = {
                defaultOperator: this._currentProfile.rules?.matching?.defaultOperator || 'or',
                fuzziness: this._currentProfile.rules?.matching?.fuzziness ?? 0.8,
                minimumQueryLength: this._currentProfile.rules?.matching?.minimumQueryLength ?? 2,
                allTermsMustMatch: this._currentProfile.rules?.matching?.allTermsMustMatch || false
            };
        } else if (type === 'editStopWords') {
            const words = this._currentProfile.rules?.matching?.stopWords || [];
            this._sidePanelData = {
                stopWords: [...words],
                _wordsInput: words.join('\n')
            };
        } else if (type === 'manageSynonyms') {
            this._sidePanelData = {
                synonyms: JSON.parse(JSON.stringify(this._currentProfile.rules?.matching?.synonyms || {}))
            };
        } else if (type === 'manageSort') {
            this._sidePanelData = {
                sortBy: JSON.parse(JSON.stringify(this._currentProfile.rules?.ranking?.sortBy || []))
            };
        } else if (type === 'manageContentTypeBoosts') {
            this._sidePanelData = {
                contentTypeBoosts: JSON.parse(JSON.stringify(this._currentProfile.rules?.ranking?.contentTypeBoosts || {}))
            };
        } else if (type === 'manageBestBets') {
            this._sidePanelData = {
                bestBets: JSON.parse(JSON.stringify(this._currentProfile.rules?.ranking?.bestBets || []))
            };
        } else if (type === 'editRecency') {
            this._sidePanelData = {
                enabled: this._currentProfile.rules?.ranking?.recency?.enabled || false,
                halfLifeDays: this._currentProfile.rules?.ranking?.recency?.halfLifeDays ?? 90,
                weight: this._currentProfile.rules?.ranking?.recency?.weight ?? 0.5
            };
        } else if (type === 'editBlockedTerms') {
            const terms = this._currentProfile.rules?.ranking?.blockedTerms || [];
            this._sidePanelData = {
                blockedTerms: [...terms],
                _termsInput: terms.join('\n')
            };
        } else if (type === 'editPaging') {
            this._sidePanelData = {
                pageSize: this._currentProfile.rules?.results?.pageSize ?? 10,
                maxResults: this._currentProfile.rules?.results?.maxResults ?? 500
            };
        } else if (type === 'editHighlighting') {
            this._sidePanelData = {
                enabled: this._currentProfile.rules?.results?.highlight?.enabled || false,
                mode: this._currentProfile.rules?.results?.highlight?.mode || 'sentence',
                snippetLength: this._currentProfile.rules?.results?.highlight?.snippetLength ?? 200,
                sentenceContext: this._currentProfile.rules?.results?.highlight?.sentenceContext ?? 0
            };
        } else if (type === 'editResultShaping') {
            this._sidePanelData = {
                deduplicateByField: this._currentProfile.rules?.results?.deduplicateByField || '',
                groupByContentType: this._currentProfile.rules?.results?.groupByContentType || false
            };
        } else if (type === 'manageFacets') {
            this._sidePanelData = {
                facets: JSON.parse(JSON.stringify(this._currentProfile.rules?.results?.facets || []))
            };
        } else if (type === 'profileMetadata') {
            this._sidePanelData = {
                name: this._currentProfile.name,
                alias: this._currentProfile.alias,
                enabled: this._currentProfile.enabled,
                isDefault: this._currentProfile.isDefault
            };
        }

        this._sidePanelOpen = true;
        this.requestUpdate();
    }

    _closeSidePanel() {
        this._sidePanelOpen = false;
        this._showFilterTypePicker = false;
        this._sidePanelType = null;
        this._sidePanelData = null;
        this._sidePanelErrors = {};
        this.requestUpdate();
    }

    _getFilterTypeName(alias) {
        const ft = ImobisoftSearchWorkspace._filterTypes.find(t => t.alias === alias);
        return ft ? ft.name : 'Custom Field Filter';
    }

    _getFilterTypeDesc(alias) {
        const ft = ImobisoftSearchWorkspace._filterTypes.find(t => t.alias === alias);
        return ft ? ft.desc : 'Dynamic aggregation on Examine index values.';
    }

    _getDiscoveredFields() {
        const fieldMap = new Map();
        const standard = [
            { name: 'category', type: 'text' },
            { name: 'tags', type: 'text' },
            { name: 'nodeName', type: 'text' },
            { name: 'author', type: 'text' },
            { name: 'department', type: 'text' },
            { name: 'status', type: 'text' },
            { name: 'updateDate', type: 'datetime' },
            { name: 'createDate', type: 'datetime' },
            { name: 'price', type: 'float' }
        ];

        standard.forEach(f => fieldMap.set(f.name, f));

        // If the profile has configured source indexes, prioritize discovering fields from those indexes
        const sourceIndexes = this._currentProfile?.rules?.sources?.indexes || [];
        const indexesToScan = (sourceIndexes.length > 0)
            ? (this._catalog?.indexes || []).filter(idx => sourceIndexes.includes(idx.name))
            : (this._catalog?.indexes || []);

        indexesToScan.forEach(idx => {
            (idx.fields || []).forEach(f => {
                if (f.name && !f.name.startsWith('__')) {
                    fieldMap.set(f.name, { name: f.name, type: f.type || 'text' });
                }
            });
        });

        return Array.from(fieldMap.values()).sort((a, b) => a.name.localeCompare(b.name));
    }

    _isFieldInDiscoveredList(field, filterType) {
        if (!field) return false;
        if (filterType === 'dateRange' && (field === 'updateDate' || field === 'createDate')) return true;
        if (filterType === 'numeric' && field === 'price') return true;
        const all = this._getDiscoveredFields();
        return all.some(f => f.name === field);
    }

    _getFilterTypeSvg(alias) {
        switch (alias) {
            case 'contentType':
                return html`<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line><polyline points="10 9 9 9 8 9"></polyline></svg>`;
            case 'contentNode':
                return html`<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="5" r="3"></circle><line x1="12" y1="8" x2="12" y2="14"></line><path d="M5 14h14"></path><line x1="5" y1="14" x2="5" y2="19"></line><line x1="19" y1="14" x2="19" y2="19"></line><circle cx="5" cy="19" r="2"></circle><circle cx="19" cy="19" r="2"></circle></svg>`;
            case 'dateRange':
                return html`<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect><line x1="16" y1="2" x2="16" y2="6"></line><line x1="8" y1="2" x2="8" y2="6"></line><line x1="3" y1="10" x2="21" y2="10"></line></svg>`;
            case 'numeric':
                return html`<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="4" y1="9" x2="20" y2="9"></line><line x1="4" y1="15" x2="20" y2="15"></line><line x1="10" y1="3" x2="8" y2="21"></line><line x1="16" y1="3" x2="14" y2="21"></line></svg>`;
            case 'field':
            default:
                return html`<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z"></path><line x1="7" y1="7" x2="7.01" y2="7"></line></svg>`;
        }
    }

    _openFilterTypePicker() {
        this._showFilterTypePicker = true;
        this.requestUpdate();
    }

    _closeFilterTypePicker() {
        if (this._sidePanelData?._isNew && !this._sidePanelData?.filterType) {
            this._closeSidePanel();
            return;
        }
        this._showFilterTypePicker = false;
        this.requestUpdate();
    }

    _selectFilterType(typeAlias) {
        if (!this._sidePanelData) return;
        const ft = ImobisoftSearchWorkspace._filterTypes.find(t => t.alias === typeAlias);
        if (ft) {
            this._sidePanelData.filterType = ft.alias;
            this._sidePanelData.kind = ft.defaultKind;
            if (ft.defaultField) {
                this._sidePanelData.field = ft.defaultField;
            }
            if (ft.alias === 'contentType') {
                this._sidePanelData.field = '__NodeTypeAlias';
                this._sidePanelData.kind = 'field';
                if (!this._sidePanelData.label) this._sidePanelData.label = 'Document Type';
                if (!this._sidePanelData.ranges || this._sidePanelData.ranges.length === 0) {
                    const allCts = this._catalog?.contentTypes || [];
                    if (allCts.length > 0) {
                        this._sidePanelData.ranges = allCts.slice(0, 4).map(ct => ({
                            alias: ct.alias,
                            label: ct.name,
                            from: ct.alias,
                            to: ''
                        }));
                    } else {
                        this._sidePanelData.ranges = [{ alias: '', label: '', from: '', to: '' }];
                    }
                }
            } else if (ft.alias === 'contentNode') {
                this._sidePanelData.field = '__Path';
                this._sidePanelData.kind = 'field';
                if (!this._sidePanelData.label) this._sidePanelData.label = 'Policies / Sections';
            } else if (ft.alias === 'dateRange') {
                this._sidePanelData.kind = 'dateRange';
                if (!this._sidePanelData.field || this._sidePanelData.field.startsWith('__')) {
                    this._sidePanelData.field = 'updateDate';
                }
                if (!this._sidePanelData.label) this._sidePanelData.label = 'Date / Year';
                if (!this._sidePanelData.ranges || this._sidePanelData.ranges.length === 0) {
                    this._sidePanelData.ranges = [
                        { alias: '2026', label: '2026', from: '2026-01-01', to: '2027-01-01' },
                        { alias: '2025', label: '2025', from: '2025-01-01', to: '2026-01-01' },
                        { alias: '2024', label: '2024', from: '2024-01-01', to: '2025-01-01' },
                        { alias: 'past-week', label: 'Past 7 Days', from: 'now-7d', to: 'now' },
                        { alias: 'past-month', label: 'Past 30 Days', from: 'now-30d', to: 'now' }
                    ];
                }
            } else if (ft.alias === 'numeric') {
                this._sidePanelData.kind = 'numeric';
                if (!this._sidePanelData.label) this._sidePanelData.label = 'Price';
                if (!this._sidePanelData._aliasUnlocked) {
                    this._generateAliasFromLabel(this._sidePanelData);
                }
                this._sidePanelData.field = this._sidePanelData.alias || 'price';
                if (!this._sidePanelData.ranges || this._sidePanelData.ranges.length === 0) {
                    this._sidePanelData.ranges = [
                        { alias: 'under-25', label: 'Under $25', from: '', to: '25' },
                        { alias: '25-to-50', label: '$25 to $50', from: '25', to: '50' },
                        { alias: '50-to-100', label: '$50 to $100', from: '50', to: '100' },
                        { alias: 'over-100', label: '$100 & Above', from: '100', to: '' }
                    ];
                }
            } else if (ft.alias === 'field') {
                this._sidePanelData.kind = 'field';
                this._sidePanelData.ranges = [];
                if (!this._sidePanelData.field || this._sidePanelData.field.startsWith('__')) {
                    this._sidePanelData.field = 'category';
                }
                if (!this._sidePanelData.label) this._sidePanelData.label = 'Tag / Category';
            }
            if (!this._sidePanelData._aliasUnlocked) {
                this._generateAliasFromLabel(this._sidePanelData);
            }
        }
        this._showFilterTypePicker = false;
        this.requestUpdate();
    }

    _toggleAliasLock(data) {
        data._aliasUnlocked = !data._aliasUnlocked;
        this.requestUpdate();
    }

    _generateAliasFromLabel(data) {
        if (data._aliasUnlocked) return;
        const label = data.label || '';
        data.alias = label
            .toLowerCase()
            .replace(/[^a-z0-9_-]/g, '-')
            .replace(/-+/g, '-')
            .replace(/^-|-$/g, '');
        this.requestUpdate();
    }

    _renderFilterTypePicker() {
        if (!this._sidePanelData) return nothing;
        const currentType = this._sidePanelData.filterType || this._sidePanelData.kind || 'field';
        
        return html`
            <div class="field-type-picker-overlay" @click=${() => this._closeFilterTypePicker()}>
                <div class="field-type-picker-wrapper" @click=${e => e.stopPropagation()}>
                    <div class="sp-body field-type-picker-body">
                        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px;">
                            <div>
                                <h3 style="margin: 0; font-size: 16px; font-weight: 700; color: #111827;">Select Filter Type</h3>
                                <p style="margin: 4px 0 0 0; font-size: 12px; color: #6b7280;">Choose how this filter dimension segments and aggregates your search results.</p>
                            </div>
                            <button class="btn-icon" @click=${() => this._closeFilterTypePicker()} title="Close">✕</button>
                        </div>
                        <div class="field-type-grid">
                            ${ImobisoftSearchWorkspace._filterTypes.map(t => {
                                const isSelected = currentType === t.alias;
                                return html`
                                    <div class="field-type-card ${isSelected ? 'selected' : ''}" 
                                         @click=${() => this._selectFilterType(t.alias)}>
                                        <div class="field-type-card-icon">
                                            ${this._getFilterTypeSvg(t.alias)}
                                        </div>
                                        <div class="field-type-card-info">
                                            <span class="field-type-card-name">${t.name}</span>
                                            <span class="field-type-card-desc">${t.desc}</span>
                                        </div>
                                        ${isSelected ? html`
                                            <div class="field-type-card-check">
                                                <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                                                    <polyline points="20 6 9 17 4 12"></polyline>
                                                </svg>
                                            </div>
                                        ` : nothing}
                                    </div>
                                `;
                            })}
                        </div>
                    </div>
                    <div class="builder-footer" style="position: static; height: 60px; flex-shrink: 0;">
                        <div class="footer-left">
                            <span class="footer-form-label">TYPE</span>
                            <span class="footer-divider">/</span>
                            <span class="footer-form-name">FILTER PRESET</span>
                        </div>
                        <div class="footer-right">
                            <button class="footer-btn" @click=${() => this._closeFilterTypePicker()} title="Cancel">
                                <span class="footer-discard-btn">Close</span>
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    async _saveSidePanel() {
        const d = this._sidePanelData;
        const errs = {};

        if (this._sidePanelType === 'editField') {
            if (!d.name || !d.name.trim()) errs.name = "Field name is required.";
            if (d.boost === undefined || isNaN(d.boost) || d.boost < 0) errs.boost = "Boost must be a non-negative number.";

            if (Object.keys(errs).length > 0) {
                this._sidePanelErrors = errs;
                this.requestUpdate();
                return;
            }

            const cleanField = {
                name: d.name.trim(),
                boost: parseFloat(d.boost) || 1.0,
                matchMode: d.matchMode || 'prefix',
                enabled: d.enabled !== false
            };

            const fields = this._currentProfile.rules.matching.fields;
            if (d._isNew) {
                const existingIdx = fields.findIndex(f => f.name.toLowerCase() === cleanField.name.toLowerCase());
                if (existingIdx >= 0) {
                    fields[existingIdx] = cleanField;
                } else {
                    fields.push(cleanField);
                }
            } else {
                const idx = fields.findIndex(f => f.name === d.name);
                if (idx >= 0) fields[idx] = cleanField;
                else fields.push(cleanField);
            }
        } else if (this._sidePanelType === 'editFacet') {
            if (!d.alias || !d.alias.trim()) errs.alias = "Facet alias is required.";
            
            // Auto-assign Examine field based on filter preset
            if (d.filterType === 'contentType') {
                d.field = '__NodeTypeAlias';
                d.kind = 'field';
            } else if (d.filterType === 'contentNode') {
                d.field = '__Path';
                d.kind = 'field';
            } else if (d.filterType === 'dateRange') {
                d.kind = 'dateRange';
                if (!d.field || !d.field.trim()) d.field = 'updateDate';
            } else if (d.filterType === 'numeric') {
                d.kind = 'numeric';
                d.field = (d.alias || d.field || 'price').trim();
            } else {
                d.kind = 'field';
                if (!d.field || !d.field.trim()) d.field = (d.alias || 'category').trim();
            }

            if (!d.field || !d.field.trim()) errs.field = "Index field is required.";

            let cleanRanges = [];
            if (Array.isArray(d.ranges) && d.ranges.length > 0) {
                cleanRanges = d.ranges.map((r, i) => {
                    let from = (r.from !== undefined && r.from !== null) ? String(r.from).trim() : '';
                    let to = (r.to !== undefined && r.to !== null) ? String(r.to).trim() : '';
                    const alias = (r.alias || r.label || `option-${i + 1}`).trim().toLowerCase().replace(/[^a-z0-9_-]/g, '-').replace(/-+/g, '-');
                    const label = (r.label || r.alias || `Option ${i + 1}`).trim();
                    
                    // If numeric and from/to not explicitly set, try extracting from alias or label
                    if (d.kind === 'numeric' && !from && !to) {
                        const str = (r.alias || '') + ' ' + (r.label || '');
                        const matchRange = str.match(/(\d+)\s*(?:to|-)\s*(\d+)/i);
                        const matchUnder = str.match(/(?:under|<|less than)\s*(\d+)/i);
                        const matchOver = str.match(/(?:over|>|above|\+)\s*(\d+)|(\d+)\s*(?:\+|and above|& above)/i);
                        if (matchRange) {
                            from = matchRange[1];
                            to = matchRange[2];
                        } else if (matchUnder) {
                            to = matchUnder[1];
                        } else if (matchOver) {
                            from = matchOver[1] || matchOver[2];
                        }
                    }

                    return {
                        alias,
                        label,
                        from,
                        to
                    };
                }).filter(r => r.alias.length > 0 || r.label.length > 0 || r.from.length > 0 || r.to.length > 0);
            }

            if (Object.keys(errs).length > 0) {
                this._sidePanelErrors = errs;
                this.requestUpdate();
                return;
            }

            const cleanFacet = {
                alias: d.alias.trim(),
                label: d.label?.trim() || d.alias.trim(),
                field: (d.field || d.alias || '').trim(),
                kind: d.kind || 'field',
                maxValues: parseInt(d.maxValues) || 20,
                hideEmpty: d.hideEmpty !== false,
                enabled: d.enabled !== false,
                ranges: cleanRanges
            };

            if (!this._currentProfile.rules) this._currentProfile.rules = {};
            if (!this._currentProfile.rules.results) this._currentProfile.rules.results = {};
            if (!Array.isArray(this._currentProfile.rules.results.facets)) {
                this._currentProfile.rules.results.facets = [];
            }
            const facets = this._currentProfile.rules.results.facets;
            if (d._isNew) {
                const existingIdx = facets.findIndex(f => f.alias.toLowerCase() === cleanFacet.alias.toLowerCase());
                if (existingIdx >= 0) {
                    facets[existingIdx] = cleanFacet;
                } else {
                    facets.push(cleanFacet);
                }
            } else {
                const idx = facets.findIndex(f => f.alias.toLowerCase() === d.alias.toLowerCase() || (d._origAlias && f.alias.toLowerCase() === d._origAlias.toLowerCase()));
                if (idx >= 0) {
                    facets[idx] = cleanFacet;
                } else {
                    facets.push(cleanFacet);
                }
            }
        } else if (this._sidePanelType === 'editBestBet') {
            const terms = (d._termsInput || '').split(',').map(t => t.trim()).filter(t => t.length > 0);
            const nodeKeys = (d._nodeKeysInput || '').split('\n').map(k => k.trim()).filter(k => k.length > 0);

            if (terms.length === 0) errs.terms = "At least one search query term is required.";
            if (nodeKeys.length === 0) errs.nodeKeys = "At least one pinned node GUID key is required.";

            if (Object.keys(errs).length > 0) {
                this._sidePanelErrors = errs;
                this.requestUpdate();
                return;
            }

            const cleanBet = {
                terms: terms,
                nodeKeys: nodeKeys
            };

            const bets = this._currentProfile.rules.ranking.bestBets;
            if (d._isNew) {
                bets.push(cleanBet);
            } else {
                const idx = bets.findIndex(b => b === d._origItem);
                if (idx >= 0) bets[idx] = cleanBet;
                else bets.push(cleanBet);
            }
        } else if (this._sidePanelType === 'editSort') {
            if (!d.field || !d.field.trim()) errs.field = "Sort field is required.";

            if (Object.keys(errs).length > 0) {
                this._sidePanelErrors = errs;
                this.requestUpdate();
                return;
            }

            const cleanSort = {
                field: d.field.trim(),
                direction: d.direction || 'descending'
            };

            const sortBy = this._currentProfile.rules.ranking.sortBy;
            if (d._isNew) {
                sortBy.push(cleanSort);
            } else {
                const idx = sortBy.findIndex(s => s.field === d.field);
                if (idx >= 0) sortBy[idx] = cleanSort;
                else sortBy.push(cleanSort);
            }
        } else if (this._sidePanelType === 'editContentTypeBoost') {
            if (!d.contentType || !d.contentType.trim()) errs.contentType = "Content type is required.";
            if (d.boost === undefined || isNaN(d.boost) || d.boost <= 0) errs.boost = "Boost multiplier must be > 0.";

            if (Object.keys(errs).length > 0) {
                this._sidePanelErrors = errs;
                this.requestUpdate();
                return;
            }

            this._currentProfile.rules.ranking.contentTypeBoosts[d.contentType.trim()] = parseFloat(d.boost) || 1.0;
        } else if (this._sidePanelType === 'editSynonym') {
            if (!d.term || !d.term.trim()) errs.term = "Source search term is required.";
            const syns = (d._synonymsInput || '').split(',').map(s => s.trim()).filter(s => s.length > 0);
            if (syns.length === 0) errs.synonyms = "At least one synonym is required.";

            if (Object.keys(errs).length > 0) {
                this._sidePanelErrors = errs;
                this.requestUpdate();
                return;
            }

            this._currentProfile.rules.matching.synonyms[d.term.trim().toLowerCase()] = syns;
        } else if (this._sidePanelType === 'editSourceIndexes') {
            this._currentProfile.rules.sources.indexes = [...(d.indexes || [])];
        } else if (this._sidePanelType === 'editSourceEntityTypes') {
            this._currentProfile.rules.sources.indexTypes = [...(d.indexTypes || [])];
        } else if (this._sidePanelType === 'editSourceContentTypes') {
            this._currentProfile.rules.sources.includeContentTypes = [...(d.includeContentTypes || [])];
        } else if (this._sidePanelType === 'editSourceExcludeContentTypes') {
            this._currentProfile.rules.sources.excludeContentTypes = [...(d.excludeContentTypes || [])];
        } else if (this._sidePanelType === 'editSourceRoots') {
            let keys = d.rootNodeKeys ? [...d.rootNodeKeys] : [];
            if ((!keys || keys.length === 0) && d._rootsInput) {
                keys = d._rootsInput.split('\n').map(s => s.trim()).filter(Boolean);
            }
            this._currentProfile.rules.sources.rootNodeKeys = keys;
            this._currentProfile.rules.sources.startNodeKeys = keys;
            this._currentProfile.rules.sources.excludeDescendantsOfExcludedNodes = d.excludeDescendantsOfExcludedNodes !== false;
        } else if (this._sidePanelType === 'editSourceProtection') {
            this._currentProfile.rules.sources.publishedOnly = d.publishedOnly !== false;
            this._currentProfile.rules.sources.respectNaviHide = d.respectNaviHide !== false;
            this._currentProfile.rules.sources.excludeProtected = d.excludeProtected !== false;
        } else if (this._sidePanelType === 'manageFields') {
            this._currentProfile.rules.matching.fields = d.fields || [];
        } else if (this._sidePanelType === 'editMatchParameters') {
            this._currentProfile.rules.matching.defaultOperator = d.defaultOperator || 'or';
            this._currentProfile.rules.matching.fuzziness = parseFloat(d.fuzziness) || 0.8;
            this._currentProfile.rules.matching.minimumQueryLength = parseInt(d.minimumQueryLength) || 2;
            this._currentProfile.rules.matching.allTermsMustMatch = !!d.allTermsMustMatch;
        } else if (this._sidePanelType === 'editStopWords') {
            let words = d._wordsInput ? d._wordsInput.split('\n').map(s => s.trim().toLowerCase()).filter(Boolean) : (d.stopWords || []);
            this._currentProfile.rules.matching.stopWords = Array.from(new Set(words));
        } else if (this._sidePanelType === 'manageSynonyms') {
            this._currentProfile.rules.matching.synonyms = d.synonyms || {};
        } else if (this._sidePanelType === 'manageSort') {
            this._currentProfile.rules.ranking.sortBy = d.sortBy || [];
        } else if (this._sidePanelType === 'manageContentTypeBoosts') {
            this._currentProfile.rules.ranking.contentTypeBoosts = d.contentTypeBoosts || {};
        } else if (this._sidePanelType === 'manageBestBets') {
            this._currentProfile.rules.ranking.bestBets = d.bestBets || [];
        } else if (this._sidePanelType === 'editRecency') {
            if (!this._currentProfile.rules.ranking.recency) this._currentProfile.rules.ranking.recency = {};
            this._currentProfile.rules.ranking.recency.enabled = !!d.enabled;
            this._currentProfile.rules.ranking.recency.halfLifeDays = parseInt(d.halfLifeDays) || 90;
            this._currentProfile.rules.ranking.recency.weight = parseFloat(d.weight) || 0.5;
        } else if (this._sidePanelType === 'editBlockedTerms') {
            let terms = d._termsInput ? d._termsInput.split('\n').map(s => s.trim().toLowerCase()).filter(Boolean) : (d.blockedTerms || []);
            this._currentProfile.rules.ranking.blockedTerms = Array.from(new Set(terms));
        } else if (this._sidePanelType === 'editPaging') {
            if (!this._currentProfile.rules.results) this._currentProfile.rules.results = {};
            this._currentProfile.rules.results.pageSize = parseInt(d.pageSize) || 10;
            this._currentProfile.rules.results.maxResults = parseInt(d.maxResults) || 500;
        } else if (this._sidePanelType === 'editHighlighting') {
            if (!this._currentProfile.rules.results) this._currentProfile.rules.results = {};
            if (!this._currentProfile.rules.results.highlight) this._currentProfile.rules.results.highlight = {};
            this._currentProfile.rules.results.highlight.enabled = !!d.enabled;
            this._currentProfile.rules.results.highlight.mode = d.mode || 'sentence';
            this._currentProfile.rules.results.highlight.snippetLength = parseInt(d.snippetLength) || 200;
            this._currentProfile.rules.results.highlight.sentenceContext = parseInt(d.sentenceContext) || 0;
        } else if (this._sidePanelType === 'editResultShaping') {
            if (!this._currentProfile.rules.results) this._currentProfile.rules.results = {};
            this._currentProfile.rules.results.deduplicateByField = (d.deduplicateByField || '').trim();
            this._currentProfile.rules.results.groupByContentType = !!d.groupByContentType;
        } else if (this._sidePanelType === 'manageFacets') {
            if (!this._currentProfile.rules.results) this._currentProfile.rules.results = {};
            this._currentProfile.rules.results.facets = d.facets || [];
        } else if (this._sidePanelType === 'profileMetadata') {
            if (!d.name || !d.name.trim()) errs.name = "Profile name is required.";
            if (!d.alias || !d.alias.trim()) errs.alias = "Profile alias is required.";

            if (Object.keys(errs).length > 0) {
                this._sidePanelErrors = errs;
                this.requestUpdate();
                return;
            }

            this._currentProfile.name = d.name.trim();
            this._currentProfile.alias = d.alias.trim();
            this._currentProfile.enabled = d.enabled;
        }

        this._closeSidePanel();
        await this._saveCurrentProfile();
    }

    // ----------------- TEST SEARCH & AUTOCOMPLETE -----------------

    async _runTestSearch() {
        if (!this._testQuery || !this._testQuery.trim()) {
            this._showToast("Enter a search term to test.", "warning");
            return;
        }

        this._testSearching = true;
        this._testShowAutocomplete = false;
        this.requestUpdate();

        try {
            const req = {
                term: this._testQuery.trim(),
                profileAlias: this._testProfileAlias || 'default',
                page: 1,
                pageSize: 20,
                cultures: this._testCulture ? [this._testCulture] : [],
                filters: this._testActiveFilters
            };

            // If we are currently editing a profile and testing with it, pass the in-memory rules for ad-hoc preview
            if (this._currentView === 'editor' && this._currentProfile && this._currentProfile.alias === this._testProfileAlias) {
                req.rules = this._currentProfile.rules;
            }

            const res = await previewSearch(this._fetch.bind(this), req);
            if (res.ok && res.data) {
                this._testResults = res.data;
            } else {
                this._testResults = null;
                const err = res.data?.detail || res.data?.title || "Search preview failed.";
                this._showToast(err, "error");
            }
        } catch (e) {
            console.error("Test search error:", e);
            this._showToast("Unexpected error running search test.", "error");
        } finally {
            this._testSearching = false;
            this.requestUpdate();
        }
    }

    async _handleTestInput(e) {
        this._testQuery = e.target.value;
        if (!this._testQuery || this._testQuery.length < 2) {
            this._testAutocompleteSuggestions = [];
            this._testShowAutocomplete = false;
            this.requestUpdate();
            return;
        }

        try {
            const res = await getAutocomplete(this._fetch.bind(this), this._testQuery, this._testProfileAlias, 6);
            if (res.ok && Array.isArray(res.data)) {
                this._testAutocompleteSuggestions = res.data;
                this._testShowAutocomplete = res.data.length > 0;
            }
        } catch (err) {
            console.warn("Autocomplete fetch error:", err);
        }
        this.requestUpdate();
    }

    _selectAutocompleteSuggestion(suggestion) {
        this._testQuery = suggestion.text;
        this._testShowAutocomplete = false;
        this._runTestSearch();
    }

    _toggleTestFacet(alias, value) {
        if (!this._testActiveFilters[alias]) {
            this._testActiveFilters[alias] = [];
        }
        const list = this._testActiveFilters[alias];
        const idx = list.indexOf(value);
        if (idx >= 0) {
            list.splice(idx, 1);
            if (list.length === 0) delete this._testActiveFilters[alias];
        } else {
            list.push(value);
        }
        this._runTestSearch();
    }

    _clearTestFilters() {
        this._testActiveFilters = {};
        this._runTestSearch();
    }

    // ----------------- SETTINGS ACTIONS -----------------

    async _saveSettings() {
        this._settingsSaving = true;
        this.requestUpdate();

        try {
            const res = await updateSettings(this._fetch.bind(this), this._settings);
            if (res.ok) {
                this._showToast("Search settings updated successfully!", "success");
            } else {
                this._showToast("Failed to save search settings.", "error");
            }
        } catch (e) {
            console.error("Save settings error:", e);
            this._showToast("Error saving settings.", "error");
        } finally {
            this._settingsSaving = false;
            this.requestUpdate();
        }
    }

    async _handlePurgeInsights() {
        this._showConfirm("Purge Analytics", "Delete all recorded queries and click records older than the configured retention period?", async (ok) => {
            if (!ok) return;
            try {
                const res = await purgeInsights(this._fetch.bind(this));
                if (res.ok) {
                    this._showToast(`Purged ${res.data || 0} old search log records.`, "success");
                    await this._loadInsights(this._insightsDays, true);
                } else {
                    this._showToast("Purge failed.", "error");
                }
            } catch (e) {
                console.error("Purge error:", e);
                this._showToast("Error purging analytics logs.", "error");
            }
        });
    }

    // ----------------- NOTIFICATIONS & MODALS -----------------

    _showToast(message, type = 'success', duration = 3500) {
        const id = Date.now() + Math.random();
        this._toasts = [...this._toasts, { id, message, type }];
        this.requestUpdate();
        setTimeout(() => {
            this._dismissToast(id);
        }, duration);
    }

    _dismissToast(id) {
        this._toasts = this._toasts.filter(t => t.id !== id);
        this.requestUpdate();
    }

    _showConfirm(title, message, callback, confirmText = 'Yes', cancelText = 'Cancel') {
        this._setMessageBox('confirm', title, message, callback, confirmText, cancelText);
    }

    _showAlert(title, message, callback = null, confirmText = 'OK') {
        this._setMessageBox('alert', title, message, callback, confirmText);
    }

    _setMessageBox(type, title, message, callback, confirmText, cancelText = 'Cancel') {
        this._messageBoxType = type;
        this._messageBoxTitle = title;
        this._messageBoxMessage = message;
        this._messageBoxCallback = callback;
        this._messageBoxConfirmText = confirmText;
        this._messageBoxCancelText = cancelText;
        this._showMessageBox = true;
        this.requestUpdate();
    }

    _handleMessageBoxConfirm() {
        if (this._messageBoxCallback) this._messageBoxCallback(true);
        this._showMessageBox = false;
        this.requestUpdate();
    }

    _handleMessageBoxCancel() {
        if (this._messageBoxCallback && this._messageBoxType === 'confirm') {
            this._messageBoxCallback(false);
        }
        this._showMessageBox = false;
        this.requestUpdate();
    }

    // ----------------- RENDER ROOT -----------------

    render() {
        const isEditingProfile = this._activeTab.alias === 'manageProfiles' && this._currentView === 'editor' && this._currentProfile;

        return html`
            <div class="umb-custom-dashboard">
                ${this._renderSidebar()}
                <div class="content">
                    ${this._renderContentHeader()}
                    <div class="view-container">
                        ${this._activeTab.alias === 'manageProfiles' ? (
                            this._currentView === 'list' ? this._renderProfilesList() : this._renderProfileEditor()
                        ) : nothing}

                        ${this._activeTab.alias === 'testSearch' ? this._renderTestSearchView() : nothing}
                        ${this._activeTab.alias === 'insights' ? this._renderInsightsView() : nothing}
                        ${this._activeTab.alias === 'catalog' ? this._renderCatalogView() : nothing}
                        ${this._activeTab.alias === 'settings' ? this._renderSettingsView() : nothing}
                    </div>
                    ${isEditingProfile ? this._renderFooter() : nothing}
                </div>

                ${this._sidePanelOpen ? this._renderSidePanel() : nothing}
                ${this._showMessageBox ? this._renderMessageBox() : nothing}
                ${this._showImportModal ? this._renderImportModal() : nothing}
                ${this._renderToasts()}
            </div>
        `;
    }

    _renderFooter() {
        const p = this._currentProfile;
        if (!p) return nothing;

        return html`
            <div class="builder-footer">
                <div class="footer-left">
                    <span class="footer-form-label">${this._profileActiveTab === 'facets' ? 'FILTERS' : 'PROFILE'}</span>
                    <span class="footer-divider">/</span>
                    <span class="footer-form-name">${this._profileActiveTab === 'facets' ? `${(p.rules?.results?.facets || []).length} Filters Configured` : (p.name || 'Untitled Profile')}</span>
                </div>
                <div class="footer-right">
                    ${this._profileActiveTab === 'facets' ? html`
                        <button class="footer-btn" title="Add Filter Dimension" @click=${() => this._openSidePanel('editFacet')}>
                            <span class="footer-add-btn">+ Add Filter</span>
                        </button>
                        <div class="header-divider"></div>
                    ` : nothing}
                    <button class="footer-btn" ?disabled=${this._profileSaving} title="Save Profile" @click=${this._saveCurrentProfile}>
                        <span class="footer-save-btn">${this._profileSaving ? 'Saving...' : 'Save Profile'}</span>
                    </button>
                </div>
            </div>
        `;
    }

    // ----------------- SIDEBAR -----------------

    _renderSidebar() {
        return html`
            <div class="sidebar">
                <div class="sidebar-header">
                    <span>Imobisoft Search</span>
                </div>

                <!-- 1. Search Profiles -->
                <div class="nav-item ${this._activeTab.alias === 'manageProfiles' && this._currentView === 'list' ? 'active' : ''}"
                     @click=${() => this._changeTab(this._tabs[0])}>
                    <i class="icon-sliders"></i>
                    <span>Profiles</span>
                    <i class="icon-chevron-right chevron-icon ${this._showProfilesDropdown ? 'chevron-rotated' : ''}"
                       @click=${(e) => { e.stopPropagation(); this._showProfilesDropdown = !this._showProfilesDropdown; this.requestUpdate(); }}></i>
                </div>

                ${this._showProfilesDropdown ? html`
                    <div class="sub-nav">
                        ${this._profiles.length ? this._profiles.map(p => {
                            const isSelected = this._activeTab.alias === 'manageProfiles' && this._currentView === 'editor' && this._currentProfile?.key === p.key;
                            return html`
                                <div class="sub-nav-item ${isSelected ? 'selected' : ''}"
                                     @click=${() => this._editProfile(p)}>
                                    <i class="icon-document"></i>
                                    <span class="text-truncate" style="flex:1;">${p.name}</span>
                                    ${p.isDefault ? html`<span class="badge-mini" title="Default Profile">★</span>` : nothing}
                                </div>
                            `;
                        }) : html`
                            <div class="sub-nav-item empty">No profiles found</div>
                        `}
                    </div>
                ` : nothing}

                <!-- 2. Test Search -->
                <div class="nav-item ${this._activeTab.alias === 'testSearch' ? 'active' : ''}"
                     @click=${() => this._changeTab(this._tabs[1])}>
                    <i class="icon-search"></i>
                    <span>Test Search</span>
                </div>

                <!-- 3. Insights -->
                <div class="nav-item ${this._activeTab.alias === 'insights' ? 'active' : ''}"
                     @click=${() => this._changeTab(this._tabs[2])}>
                    <i class="icon-chart-curve"></i>
                    <span>Insights</span>
                </div>

                <!-- 4. Examine Indexes -->
                <div class="nav-item ${this._activeTab.alias === 'catalog' ? 'active' : ''}"
                     @click=${() => this._changeTab(this._tabs[3])}>
                    <i class="icon-server"></i>
                    <span>Indexes</span>
                </div>

                <!-- 5. Settings -->
                <div class="nav-item ${this._activeTab.alias === 'settings' ? 'active' : ''}"
                     @click=${() => this._changeTab(this._tabs[4])}>
                    <i class="icon-settings"></i>
                    <span>Settings</span>
                </div>
            </div>
        `;
    }

    // ----------------- TOP CONTENT HEADER -----------------

    _renderContentHeader() {
        if (this._activeTab.alias === 'manageProfiles') {
            if (this._currentView === 'list') {
                return html`
                    <div class="content-header">
                        <div class="topbar-left">
                            <h3 style="margin:0;">Search Profiles</h3>
                        </div>
                        <div class="header-actions">
                            <button class="btn btn-primary" @click=${this._goToCreateProfile}>
                                <i class="icon-add"></i> Create Profile
                            </button>
                        </div>
                    </div>
                `;
            } else {
                return html`
                    <div class="content-header">
                        <div class="topbar-left">
                            <button class="btn-back" @click=${this._goToList} title="Back to profiles">
                                <i class="icon-arrow-left"></i>
                            </button>
                            <div class="divider-v"></div>
                            <div class="form-title-wrap">
                                <input type="text"
                                       class="input-form-title"
                                       .value=${this._currentProfile?.name || ''}
                                       @input=${e => {
                                           this._currentProfile.name = e.target.value;
                                           if (this._currentProfile._errorName) this._currentProfile._errorName = false;
                                           if (!this._currentProfile.alias || this._currentProfile.alias.startsWith('new-profile') || this._currentProfile.alias.startsWith('profile-')) {
                                               this._currentProfile.alias = e.target.value.toLowerCase().replace(/[^a-z0-9_-]/g, '-').replace(/-+/g, '-').replace(/^-|-$/g, '') || 'profile';
                                           }
                                           this.requestUpdate();
                                       }}
                                       placeholder="Enter profile title..." />
                                ${this._currentProfile?._errorName ? html`<div class="form-title-error-msg">Profile name is required.</div>` : nothing}
                            </div>
                        </div>
                        <div class="header-actions">
                            <button class="btn ${this._profileActiveTab === 'sources' ? 'active' : ''}"
                                    @click=${() => { this._profileActiveTab = 'sources'; this.requestUpdate(); }}
                                    title="Source">
                                <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                    <ellipse cx="12" cy="5" rx="9" ry="3"></ellipse>
                                    <path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"></path>
                                    <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"></path>
                                </svg>
                                <span>Source</span>
                            </button>
                            <button class="btn ${this._profileActiveTab === 'matching' ? 'active' : ''}"
                                    @click=${() => { this._profileActiveTab = 'matching'; this.requestUpdate(); }}
                                    title="Matching">
                                <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                    <circle cx="11" cy="11" r="8"></circle>
                                    <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
                                </svg>
                                <span>Matching</span>
                            </button>
                            <button class="btn ${this._profileActiveTab === 'ranking' ? 'active' : ''}"
                                    @click=${() => { this._profileActiveTab = 'ranking'; this.requestUpdate(); }}
                                    title="Ranking">
                                <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                    <line x1="4" y1="21" x2="4" y2="14"></line>
                                    <line x1="4" y1="10" x2="4" y2="3"></line>
                                    <line x1="12" y1="21" x2="12" y2="12"></line>
                                    <line x1="12" y1="8" x2="12" y2="3"></line>
                                    <line x1="20" y1="21" x2="20" y2="16"></line>
                                    <line x1="20" y1="12" x2="20" y2="3"></line>
                                    <line x1="1" y1="14" x2="7" y2="14"></line>
                                    <line x1="9" y1="8" x2="15" y2="8"></line>
                                    <line x1="17" y1="16" x2="23" y2="16"></line>
                                </svg>
                                <span>Ranking</span>
                            </button>
                            <button class="btn ${this._profileActiveTab === 'results' ? 'active' : ''}"
                                    @click=${() => { this._profileActiveTab = 'results'; this.requestUpdate(); }}
                                    title="Results">
                                <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                                    <polyline points="14 2 14 8 20 8"></polyline>
                                    <line x1="16" y1="13" x2="8" y2="13"></line>
                                    <line x1="16" y1="17" x2="8" y2="17"></line>
                                    <polyline points="10 9 9 9 8 9"></polyline>
                                </svg>
                                <span>Results</span>
                            </button>
                            <button class="btn ${this._profileActiveTab === 'facets' ? 'active' : ''}"
                                    @click=${() => { this._profileActiveTab = 'facets'; this.requestUpdate(); }}
                                    title="Filters">
                                <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                    <polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"></polygon>
                                </svg>
                                <span>Filters</span>
                            </button>
                        </div>
                    </div>
                `;
            }
        } else if (this._activeTab.alias === 'testSearch') {
            return html`
                <div class="content-header">
                    <div class="topbar-left">
                        <h3 style="margin:0;">Test Search & Debugger</h3>
                    </div>
                    <div class="header-actions">
                        <button class="btn btn-secondary" @click=${() => { this._testQuery = ''; this._testResults = null; this.requestUpdate(); }}>
                            <i class="icon-delete"></i> Clear
                        </button>
                        <button class="btn btn-secondary" @click=${() => { this._testDiagnosticsOpen = !this._testDiagnosticsOpen; this.requestUpdate(); }}>
                            <i class="icon-info"></i> ${this._testDiagnosticsOpen ? 'Hide Diagnostics' : 'Show Diagnostics'}
                        </button>
                    </div>
                </div>
            `;
        } else if (this._activeTab.alias === 'insights') {
            return html`
                <div class="content-header">
                    <div class="topbar-left">
                        <h3 style="margin:0;">Search Insights & Analytics</h3>
                    </div>
                    <div class="header-actions">
                        <select class="header-select"
                                .value=${String(this._insightsDays)}
                                @change=${e => { this._insightsDays = parseInt(e.target.value); this._loadInsights(this._insightsDays, true); }}>
                            <option value="7">Last 7 Days</option>
                            <option value="30">Last 30 Days</option>
                            <option value="60">Last 60 Days</option>
                            <option value="90">Last 90 Days</option>
                            <option value="365">Last 1 Year</option>
                        </select>
                        <button class="btn btn-secondary" @click=${() => this._loadInsights(this._insightsDays, true)}>
                            <i class="icon-refresh"></i> Refresh
                        </button>
                        <button class="btn btn-secondary btn-danger" @click=${this._handlePurgeInsights} title="Purge expired query analytics">
                            <i class="icon-trash"></i> Purge Logs
                        </button>
                    </div>
                </div>
            `;
        } else if (this._activeTab.alias === 'catalog') {
            return html`
                <div class="content-header">
                    <div class="topbar-left">
                        <h3 style="margin:0;">Examine Index Catalog</h3>
                    </div>
                    <div class="header-actions">
                        <button class="btn btn-secondary" @click=${() => this._loadCatalog(true)}>
                            <i class="icon-refresh"></i> Refresh Catalog
                        </button>
                    </div>
                </div>
            `;
        } else if (this._activeTab.alias === 'settings') {
            return html`
                <div class="content-header">
                    <div class="topbar-left">
                        <h3 style="margin:0;">Search Settings</h3>
                    </div>
                    <div class="header-actions">
                        <button class="btn btn-primary" @click=${this._saveSettings} ?disabled=${this._settingsSaving}>
                            <i class="icon-check"></i> ${this._settingsSaving ? 'Saving...' : 'Save Settings'}
                        </button>
                    </div>
                </div>
            `;
        }
        return nothing;
    }

    // ----------------- VIEW 1: PROFILES LIST -----------------

    _renderProfilesList() {
        const filtered = this._profiles.filter(p => {
            if (!this._filterProfilesQuery) return true;
            const q = this._filterProfilesQuery.toLowerCase();
            return (p.name && p.name.toLowerCase().includes(q)) || (p.alias && p.alias.toLowerCase().includes(q));
        });

        return html`
            <div class="profiles-list-view">
                <div class="list-controls-bar">
                    <div class="search-box-wrap">
                        <i class="icon-search search-box-icon"></i>
                        <input type="text"
                               class="search-box-input"
                               placeholder="Search profiles by name or alias..."
                               .value=${this._filterProfilesQuery}
                               @input=${e => { this._filterProfilesQuery = e.target.value; this.requestUpdate(); }}>
                    </div>
                    <div class="list-stats">
                        Showing <strong>${filtered.length}</strong> of <strong>${this._profiles.length}</strong> profiles
                    </div>
                </div>

                ${filtered.length === 0 ? html`
                    <div class="empty-state">
                        <i class="icon-sliders empty-icon"></i>
                        <h4>No Search Profiles Found</h4>
                        <p>${this._filterProfilesQuery ? "No search profiles matched your filter." : "Get started by creating your first search profile."}</p>
                        <button class="btn btn-primary" @click=${this._goToCreateProfile}>
                            <i class="icon-add"></i> Create Profile
                        </button>
                    </div>
                ` : html`
                    <div class="table-card">
                        <table class="data-table">
                            <thead>
                                <tr>
                                    <th style="width: 35%;">Profile</th>
                                    <th style="width: 15%;">Status</th>
                                    <th style="width: 25%;">Configuration Summary</th>
                                    <th style="width: 25%; text-align: right;">Actions</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${filtered.map(p => {
                                    const sourceCount = (p.rules?.sources?.indexes?.length || 0) + (p.rules?.sources?.includeContentTypes?.length || 0);
                                    const fieldCount = p.rules?.matching?.fields?.length || 0;
                                    const facetCount = p.rules?.results?.facets?.length || 0;

                                    return html`
                                        <tr class="table-row-clickable" @click=${() => this._editProfile(p)}>
                                            <td>
                                                <div class="profile-name-cell">
                                                    <span class="profile-title">${p.name}</span>
                                                    ${p.isDefault ? html`<span class="badge badge-default">Default</span>` : nothing}
                                                </div>
                                            </td>
                                            <td>
                                                <div class="status-cell">
                                                    <span class="status-dot ${p.enabled ? 'status-active' : 'status-disabled'}"></span>
                                                    <span>${p.enabled ? 'Enabled' : 'Disabled'}</span>
                                                </div>
                                            </td>
                                            <td>
                                                <div class="summary-pills">
                                                    <span class="summary-pill" title="Configured Search Fields">${fieldCount} field${fieldCount === 1 ? '' : 's'}</span>
                                                    <span class="summary-pill" title="Configured Facets">${facetCount} facet${facetCount === 1 ? '' : 's'}</span>
                                                    <span class="summary-pill" title="Page size: ${p.rules?.results?.pageSize || 10}">${p.rules?.results?.pageSize || 10} / page</span>
                                                </div>
                                            </td>
                                            <td style="text-align: right;" @click=${e => e.stopPropagation()}>
                                                <div class="row-actions">
                                                    ${!p.isDefault ? html`
                                                        <button class="btn-icon" title="Set as Default Profile" @click=${(e) => this._handleSetDefaultProfile(p, e)}>
                                                            <i class="icon-star"></i>
                                                        </button>
                                                    ` : nothing}
                                                    <button class="btn-icon" title="Duplicate Profile" @click=${(e) => this._duplicateProfile(p, e)}>
                                                        <i class="icon-copy"></i>
                                                    </button>
                                                    <button class="btn-icon" title="Edit Profile" @click=${() => this._editProfile(p)}>
                                                        <i class="icon-edit"></i>
                                                    </button>
                                                    <button class="btn-icon btn-icon-danger" title="Delete Profile" @click=${(e) => this._handleDeleteProfile(p, e)}>
                                                        <i class="icon-trash"></i>
                                                    </button>
                                                </div>
                                            </td>
                                        </tr>
                                    `;
                                })}
                            </tbody>
                        </table>
                    </div>
                `}
            </div>
        `;
    }

    // ----------------- VIEW 2: PROFILE EDITOR & RULE BUILDER -----------------

    _renderProfileEditor() {
        const p = this._currentProfile;
        if (!p) return nothing;

        return html`
            <div class="profile-editor-view">
                <div class="editor-tab-body">
                    ${this._profileActiveTab === 'sources' ? this._renderSourcesTab(p) : nothing}
                    ${this._profileActiveTab === 'matching' ? this._renderMatchingTab(p) : nothing}
                    ${this._profileActiveTab === 'ranking' ? this._renderRankingTab(p) : nothing}
                    ${this._profileActiveTab === 'results' ? this._renderResultsTab(p) : nothing}
                    ${this._profileActiveTab === 'facets' ? this._renderFacetsTab(p) : nothing}
                </div>
            </div>
        `;
    }

    // --- TAB: SOURCES & SCOPE ---
    _renderSourcesTab(p) {
        const sources = p.rules?.sources || {};
        const indexes = sources.indexes || [];
        const indexTypes = sources.indexTypes || [];
        const includeContentTypes = sources.includeContentTypes || [];
        const excludeContentTypes = sources.excludeContentTypes || [];
        const rootKeys = sources.rootNodeKeys || sources.startNodeKeys || [];

        return html`
            <div class="source-settings-container">
                <!-- 1. Target Examine Indexes -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Target Examine Indexes</div>
                        <div class="setting-desc">Select which Examine indexes to search. Leave empty to query all available indexes automatically.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editSourceIndexes')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Examine Indexes</span>
                            <span class="field-count-pill">${indexes.length ? `${indexes.length} selected` : 'All Indexes'}</span>
                        </div>
                        <div class="field-box-content">
                            ${indexes.length ? html`
                                <div class="selected-chips-wrap">
                                    ${indexes.map(idx => html`
                                        <span class="selected-chip">
                                            <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                                <ellipse cx="12" cy="5" rx="9" ry="3"></ellipse>
                                                <path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"></path>
                                                <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"></path>
                                            </svg>
                                            <strong>${idx}</strong>
                                        </span>
                                    `)}
                                </div>
                            ` : html`
                                <div class="selected-placeholder">
                                    <span class="placeholder-tag">All Available Indexes (Automatic)</span>
                                    <span class="placeholder-meta">Searches across all discovered Examine indexes on this site.</span>
                                </div>
                            `}
                        </div>
                    </div>
                </div>

                <!-- 2. Index Entity Types -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Index Entity Types</div>
                        <div class="setting-desc">Filter by Examine entity type (content, media, member). Leave empty for all.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editSourceEntityTypes')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Entity Categories</span>
                            <span class="field-count-pill">${indexTypes.length ? `${indexTypes.length} selected` : 'All Categories'}</span>
                        </div>
                        <div class="field-box-content">
                            ${indexTypes.length ? html`
                                <div class="selected-chips-wrap">
                                    ${indexTypes.map(t => html`
                                        <span class="selected-chip">
                                            <strong>${t.toUpperCase()}</strong>
                                        </span>
                                    `)}
                                </div>
                            ` : html`
                                <div class="selected-placeholder">
                                    <span class="placeholder-tag">All Entity Categories</span>
                                    <span class="placeholder-meta">Content, Media, and Member entities eligible.</span>
                                </div>
                            `}
                        </div>
                    </div>
                </div>

                <!-- 3. Include Document Types -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Include Document Types</div>
                        <div class="setting-desc">Choose specific document types to include in search results. Leave empty for all.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editSourceContentTypes')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Included Content Types</span>
                            <span class="field-count-pill">${includeContentTypes.length ? `${includeContentTypes.length} selected` : 'All Document Types'}</span>
                        </div>
                        <div class="field-box-content">
                            ${includeContentTypes.length ? html`
                                <div class="selected-chips-wrap">
                                    ${includeContentTypes.map(ct => html`
                                        <span class="selected-chip">
                                            <code>${ct}</code>
                                        </span>
                                    `)}
                                </div>
                            ` : html`
                                <div class="selected-placeholder">
                                    <span class="placeholder-tag">All Document Types</span>
                                    <span class="placeholder-meta">No document type restrictions applied.</span>
                                </div>
                            `}
                        </div>
                    </div>
                </div>

                <!-- 4. Exclude Document Types -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Exclude Document Types</div>
                        <div class="setting-desc">Document types that must be excluded from search results.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editSourceExcludeContentTypes')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Excluded Content Types</span>
                            <span class="field-count-pill">${excludeContentTypes.length ? `${excludeContentTypes.length} excluded` : 'None Excluded'}</span>
                        </div>
                        <div class="field-box-content">
                            ${excludeContentTypes.length ? html`
                                <div class="selected-chips-wrap">
                                    ${excludeContentTypes.map(ct => html`
                                        <span class="selected-chip chip-danger">
                                            <code>${ct}</code>
                                        </span>
                                    `)}
                                </div>
                            ` : html`
                                <div class="selected-placeholder">
                                    <span class="placeholder-tag">None Excluded</span>
                                    <span class="placeholder-meta">No content types blocked from results.</span>
                                </div>
                            `}
                        </div>
                    </div>
                </div>

                <!-- 5. Search Subtree Roots -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Search Subtree Roots</div>
                        <div class="setting-desc">Scope search queries to specific root nodes or content branches. Leave empty for entire site.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editSourceRoots')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Root Scope</span>
                            <span class="field-count-pill">${rootKeys.length ? `${rootKeys.length} roots` : 'Entire Site'}</span>
                        </div>
                        <div class="field-box-content">
                            ${rootKeys.length ? html`
                                <div class="selected-chips-wrap">
                                    ${rootKeys.map(k => html`
                                        <span class="selected-chip">
                                            <code>${k}</code>
                                        </span>
                                    `)}
                                </div>
                            ` : html`
                                <div class="selected-placeholder">
                                    <span class="placeholder-tag">Entire Site (Root Level)</span>
                                    <span class="placeholder-meta">Searches across the full tree without subtree restrictions.</span>
                                </div>
                            `}
                        </div>
                    </div>
                </div>

                <!-- 6. Visibility & Protection Rules -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Visibility & Protection Rules</div>
                        <div class="setting-desc">Control subtree scoping, member-protected pages, and publication state.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editSourceProtection')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Protection & Visibility</span>
                            <span class="field-count-pill">Configured</span>
                        </div>
                        <div class="field-box-content">
                            <div class="selected-chips-wrap">
                                <span class="selected-chip ${sources.publishedOnly !== false ? 'chip-success' : 'chip-muted'}">
                                    ${sources.publishedOnly !== false ? '✓ Published Only' : '✕ Include Unpublished'}
                                </span>
                                <span class="selected-chip ${sources.respectNaviHide !== false ? 'chip-success' : 'chip-muted'}">
                                    ${sources.respectNaviHide !== false ? '✓ Respect NaviHide' : '✕ Ignore NaviHide'}
                                </span>
                                <span class="selected-chip ${sources.excludeProtected !== false ? 'chip-success' : 'chip-muted'}">
                                    ${sources.excludeProtected !== false ? '✓ Exclude Protected' : '✕ Allow Protected'}
                                </span>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    // --- TAB: MATCHING & FIELDS ---
    _renderMatchingTab(p) {
        const matching = p.rules.matching;
        const fields = matching.fields || [];
        const stopWords = matching.stopWords || [];
        const synonyms = matching.synonyms || {};
        const synEntries = Object.entries(synonyms);

        return html`
            <div class="source-settings-container">
                <!-- 1. Searchable Fields Table -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Searchable Fields & Relevance Weighting</div>
                        <div class="setting-desc">Define which index fields are matched, their boost multiplier, and match modes.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('manageFields')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Searchable Fields</span>
                            <span class="field-count-pill">${fields.length ? `${fields.length} fields configured` : 'All Fields (Automatic)'}</span>
                        </div>
                        <div class="field-box-content">
                            ${fields.length ? html`
                                <div class="selected-chips-wrap">
                                    ${fields.map(f => html`
                                        <span class="selected-chip">
                                            <strong>${f.name}</strong>
                                            <span class="chip-meta">${f.boost}x (${f.matchMode || 'prefix'})</span>
                                        </span>
                                    `)}
                                </div>
                            ` : html`
                                <div class="selected-placeholder">
                                    <span class="placeholder-tag">All Text Fields (Automatic)</span>
                                    <span class="placeholder-meta">Searching across all standard Examine text properties. Click to define specific field weights.</span>
                                </div>
                            `}
                        </div>
                    </div>
                </div>

                <!-- 2. Matching Engine Parameters -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Query Match Parameters</div>
                        <div class="setting-desc">Configure logic operators, fuzziness tolerance, and minimum query length thresholds.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editMatchParameters')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Query Parser Logic</span>
                            <span class="field-count-pill">Operator: ${(matching.defaultOperator || 'or').toUpperCase()}</span>
                        </div>
                        <div class="field-box-content">
                            <div class="selected-chips-wrap">
                                <span class="selected-chip">
                                    <strong>Join:</strong> ${(matching.defaultOperator || 'or').toUpperCase()}
                                </span>
                                <span class="selected-chip">
                                    <strong>Fuzziness:</strong> ${matching.fuzziness ?? 0.8}
                                </span>
                                <span class="selected-chip">
                                    <strong>Min Length:</strong> ${matching.minimumQueryLength ?? 2} chars
                                </span>
                                <span class="selected-chip ${matching.allTermsMustMatch ? 'chip-success' : 'chip-muted'}">
                                    ${matching.allTermsMustMatch ? '✓ All Terms Required' : '✕ Any Term Matches'}
                                </span>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- 3. Stop Words -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Stop Words (Ignored Words)</div>
                        <div class="setting-desc">Strip non-informational words automatically from query strings before search.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editStopWords')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Stop Words</span>
                            <span class="field-count-pill">${stopWords.length ? `${stopWords.length} words` : 'None Configured'}</span>
                        </div>
                        <div class="field-box-content">
                            ${stopWords.length ? html`
                                <div class="selected-chips-wrap">
                                    ${stopWords.slice(0, 15).map(w => html`
                                        <span class="selected-chip">
                                            <code>${w}</code>
                                        </span>
                                    `)}
                                    ${stopWords.length > 15 ? html`
                                        <span class="selected-chip chip-muted">
                                            +${stopWords.length - 15} more
                                        </span>
                                    ` : nothing}
                                </div>
                            ` : html`
                                <div class="selected-placeholder">
                                    <span class="placeholder-tag">No Stop Words</span>
                                    <span class="placeholder-meta">All query terms are passed directly to the search index.</span>
                                </div>
                            `}
                        </div>
                    </div>
                </div>

                <!-- 4. Synonyms -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Query Synonym Groups</div>
                        <div class="setting-desc">Expand search terms into equivalent synonyms for higher result discovery.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('manageSynonyms')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Synonym Expansion</span>
                            <span class="field-count-pill">${synEntries.length ? `${synEntries.length} groups` : 'None Configured'}</span>
                        </div>
                        <div class="field-box-content">
                            ${synEntries.length ? html`
                                <div class="selected-chips-wrap">
                                    ${synEntries.map(([term, syns]) => html`
                                        <span class="selected-chip">
                                            <strong>${term}</strong> ➔ ${(syns || []).join(', ')}
                                        </span>
                                    `)}
                                </div>
                            ` : html`
                                <div class="selected-placeholder">
                                    <span class="placeholder-tag">No Synonym Groups</span>
                                    <span class="placeholder-meta">Click to add synonym groups for query expansion.</span>
                                </div>
                            `}
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    // --- TAB: RANKING & BOOSTS ---
    _renderRankingTab(p) {
        const ranking = p.rules.ranking;
        const sortBy = ranking.sortBy || [];
        const boosts = ranking.contentTypeBoosts || {};
        const boostEntries = Object.entries(boosts);
        const bestBets = ranking.bestBets || [];
        const blockedTerms = ranking.blockedTerms || [];
        const recency = ranking.recency || {};

        return html`
            <div class="source-settings-container">
                <!-- 1. Sort Levels -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Sort Priority Levels</div>
                        <div class="setting-desc">Order of priority when sorting results. Default is Score (Relevance Descending).</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('manageSort')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Sort Order</span>
                            <span class="field-count-pill">${sortBy.length ? `${sortBy.length} custom levels` : 'Default Relevance'}</span>
                        </div>
                        <div class="field-box-content">
                            ${sortBy.length ? html`
                                <div class="selected-chips-wrap">
                                    ${sortBy.map((s, idx) => html`
                                        <span class="selected-chip">
                                            <strong>#${idx + 1} ${s.field}</strong> (${(s.direction || 'asc').toUpperCase()})
                                        </span>
                                    `)}
                                </div>
                            ` : html`
                                <div class="selected-placeholder">
                                    <span class="placeholder-tag">Relevance Score (Descending)</span>
                                    <span class="placeholder-meta">Default Lucene BM25 relevance score ordering.</span>
                                </div>
                            `}
                        </div>
                    </div>
                </div>

                <!-- 2. Content Type Boosts -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Document Type Relevance Multipliers</div>
                        <div class="setting-desc">Boost specific document types (e.g. News articles at 2.0x, Products at 1.5x).</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('manageContentTypeBoosts')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Document Type Boosts</span>
                            <span class="field-count-pill">${boostEntries.length ? `${boostEntries.length} boosted types` : 'No Type Boosts'}</span>
                        </div>
                        <div class="field-box-content">
                            ${boostEntries.length ? html`
                                <div class="selected-chips-wrap">
                                    ${boostEntries.map(([alias, boost]) => html`
                                        <span class="selected-chip">
                                            <code>${alias}</code>: <strong>${boost}x</strong>
                                        </span>
                                    `)}
                                </div>
                            ` : html`
                                <div class="selected-placeholder">
                                    <span class="placeholder-tag">Equal Document Weight (1.0x)</span>
                                    <span class="placeholder-meta">All document types participate with standard weight.</span>
                                </div>
                            `}
                        </div>
                    </div>
                </div>

                <!-- 3. Best Bets -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Best Bets (Pinned Results)</div>
                        <div class="setting-desc">Pin specific Umbraco node GUIDs to the very top when visitors search specific terms.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('manageBestBets')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Pinned Best Bets</span>
                            <span class="field-count-pill">${bestBets.length ? `${bestBets.length} best bets` : 'None Configured'}</span>
                        </div>
                        <div class="field-box-content">
                            ${bestBets.length ? html`
                                <div class="selected-chips-wrap">
                                    ${bestBets.map(bet => html`
                                        <span class="selected-chip">
                                            <strong>${(bet.terms || []).join(', ')}</strong> ➔ ${(bet.nodeKeys || []).length} pinned nodes
                                        </span>
                                    `)}
                                </div>
                            ` : html`
                                <div class="selected-placeholder">
                                    <span class="placeholder-tag">No Best Bets</span>
                                    <span class="placeholder-meta">Results are sorted purely by calculated relevance algorithm.</span>
                                </div>
                            `}
                        </div>
                    </div>
                </div>

                <!-- 4. Recency Boost -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Recency Decay & Time Boost</div>
                        <div class="setting-desc">Give recently published or modified content a boost over older historical pages.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editRecency')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Recency Decay</span>
                            <span class="field-count-pill">${recency.enabled ? 'Active' : 'Disabled'}</span>
                        </div>
                        <div class="field-box-content">
                            <div class="selected-chips-wrap">
                                <span class="selected-chip ${recency.enabled ? 'chip-success' : 'chip-muted'}">
                                    ${recency.enabled ? `✓ Active (Half-life: ${recency.halfLifeDays || 90}d, Weight: ${recency.weight || 0.5}x)` : '✕ Recency Boost Disabled'}
                                </span>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- 5. Blocked Terms -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Blocked Search Terms</div>
                        <div class="setting-desc">Queries containing these terms will return 0 results or be suppressed.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editBlockedTerms')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Blocked Terms</span>
                            <span class="field-count-pill">${blockedTerms.length ? `${blockedTerms.length} blocked` : 'None Blocked'}</span>
                        </div>
                        <div class="field-box-content">
                            ${blockedTerms.length ? html`
                                <div class="selected-chips-wrap">
                                    ${blockedTerms.map(t => html`
                                        <span class="selected-chip chip-danger">
                                            <code>${t}</code>
                                        </span>
                                    `)}
                                </div>
                            ` : html`
                                <div class="selected-placeholder">
                                    <span class="placeholder-tag">No Blocked Queries</span>
                                    <span class="placeholder-meta">No search terms are suppressed.</span>
                                </div>
                            `}
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    // --- TAB: RESULTS & SNIPPETS ---
    _renderResultsTab(p) {
        const res = p.rules.results || {};
        const highlight = res.highlight || {};

        return html`
            <div class="source-settings-container">
                <!-- 1. Paging & Capacity -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Paging & Capacity Ceilings</div>
                        <div class="setting-desc">Set default page size and maximum total results window considered per search request.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editPaging')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Paging Limits</span>
                            <span class="field-count-pill">Page Size: ${res.pageSize || 10}</span>
                        </div>
                        <div class="field-box-content">
                            <div class="selected-chips-wrap">
                                <span class="selected-chip">
                                    <strong>Default Page Size:</strong> ${res.pageSize || 10} items
                                </span>
                                <span class="selected-chip">
                                    <strong>Max Results Window:</strong> ${res.maxResults || 500} items
                                </span>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- 2. Highlighting & Snippets -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Highlighting & Matching Snippets</div>
                        <div class="setting-desc">Extract sentence snippets from documents with highlight markup around matched terms.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editHighlighting')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Snippets & Highlights</span>
                            <span class="field-count-pill">${highlight.enabled ? 'Enabled' : 'Disabled'}</span>
                        </div>
                        <div class="field-box-content">
                            <div class="selected-chips-wrap">
                                <span class="selected-chip ${highlight.enabled ? 'chip-success' : 'chip-muted'}">
                                    ${highlight.enabled ? `✓ Snippets Enabled (${highlight.mode || 'sentence'}, max ${highlight.snippetLength || 200} chars)` : '✕ Snippets Disabled'}
                                </span>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- 3. De-duplication & Grouping -->
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Result Shaping & De-Duplication</div>
                        <div class="setting-desc">Collapse duplicates by field and enable document type grouping.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editResultShaping')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Result Shaping</span>
                            <span class="field-count-pill">Configured</span>
                        </div>
                        <div class="field-box-content">
                            <div class="selected-chips-wrap">
                                <span class="selected-chip ${res.deduplicateByField ? 'chip-success' : 'chip-muted'}">
                                    ${res.deduplicateByField ? `✓ De-duplicate by: ${res.deduplicateByField}` : '✕ No De-duplication'}
                                </span>
                                <span class="selected-chip ${res.groupByContentType ? 'chip-success' : 'chip-muted'}">
                                    ${res.groupByContentType ? '✓ Group by DocType' : '✕ No DocType Grouping'}
                                </span>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    // --- TAB: FACETS & FILTERS ---
    _renderFacetsTab(p) {
        if (!p.rules.results) p.rules.results = {};
        if (!Array.isArray(p.rules.results.facets)) p.rules.results.facets = [];
        const facets = p.rules.results.facets;

        return html`
            <div class="source-settings-container">
                <div class="mf-field">
                    <div class="field-left-info">
                        <div class="setting-title">Configured Facet Dimensions</div>
                        <div class="setting-desc">Facets return dynamic filter dimensions with live counts for your search UI sidebar.</div>
                    </div>
                    <div class="field-right-box clickable-box" @click=${() => this._openSidePanel('editFacet')}>
                        <div class="field-box-header">
                            <span class="field-type-tag">Facet Dimensions</span>
                            <span class="field-count-pill">${facets.length ? `${facets.length} filters configured` : 'No Filters'}</span>
                        </div>
                        <div class="field-box-content">
                            ${facets.length ? html`
                                <div class="selected-chips-wrap">
                                    ${facets.map(f => html`
                                        <span class="selected-chip" style="cursor: pointer;" @click=${(e) => { e.stopPropagation(); this._openSidePanel('editFacet', f); }}>
                                            <strong>${f.label || f.alias}</strong>
                                            <span class="chip-meta">(kind: <code>${f.kind || 'field'}</code>, field: <code>${f.field}</code>${f.ranges?.length ? `, ${f.ranges.length} options` : ''})</span>
                                        </span>
                                    `)}
                                </div>
                            ` : html`
                                <div class="selected-placeholder">
                                    <span class="placeholder-tag">No Filters Added</span>
                                    <span class="placeholder-meta">Click to add your first search filter (e.g. Document Types, Policies, Date Range, Price).</span>
                                </div>
                            `}
                        </div>
                    </div>
                </div>

                ${facets.length > 0 ? html`
                    <div style="display: flex; flex-direction: column; gap: 8px; margin-top: 10px;">
                        ${facets.map((f, idx) => {
                            const isEnabled = f.enabled !== false;
                            return html`
                                <div class="sp-choice-card"
                                     style="cursor: pointer; padding: 12px 18px; opacity: ${isEnabled ? '1' : '0.65'}; transition: opacity 0.2s ease;"
                                     @click=${() => this._openSidePanel('editFacet', f)}>
                                    <div class="sp-choice-info">
                                        <div style="display: flex; align-items: center; gap: 8px;">
                                            <strong class="sp-choice-title" style="font-size: 14px;">${f.label || f.alias}</strong>
                                            <span class="badge badge-info">${f.kind || 'field'}</span>
                                            ${!isEnabled ? html`<span class="badge badge-muted" style="background: #f1f5f9; color: #64748b; font-size: 11px;">Disabled</span>` : nothing}
                                        </div>
                                        <span class="sp-choice-meta" style="margin-top: 2px;">
                                            ${f.kind === 'field' && (!f.ranges || f.ranges.length === 0) ? 'Dynamic Tag / Taxonomy' : (f.ranges?.length ? `${f.ranges.length} option(s) configured` : 'Facet Filter')}
                                            ${f.hideEmpty !== false ? ' • Hide Empty' : ''}
                                        </span>
                                    </div>
                                    <div style="display: flex; align-items: center; gap: 10px;" @click=${e => e.stopPropagation()}>
                                        <label class="switch switch-sm" title="${isEnabled ? 'Filter is Enabled (click to disable)' : 'Filter is Disabled (click to enable)'}">
                                            <input type="checkbox"
                                                   .checked=${isEnabled}
                                                   @change=${async e => {
                                                       f.enabled = e.target.checked;
                                                       this.requestUpdate();
                                                       await this._saveCurrentProfile();
                                                   }}>
                                            <span class="slider round"></span>
                                        </label>
                                        <button class="btn-icon" title="Edit Filter" @click=${() => this._openSidePanel('editFacet', f)}>
                                            <i class="icon-edit"></i>
                                        </button>
                                        <button class="btn-icon btn-icon-danger" title="Remove Filter" @click=${async (e) => {
                                            e.stopPropagation();
                                            facets.splice(idx, 1);
                                            this.requestUpdate();
                                            await this._saveCurrentProfile();
                                        }}>
                                            <i class="icon-trash"></i>
                                        </button>
                                    </div>
                                </div>
                            `;
                        })}
                    </div>
                ` : nothing}
            </div>
        `;
    }

    // ----------------- VIEW 3: TEST SEARCH & DEBUGGER -----------------

    _renderTestSearchView() {
        return html`
            <div class="test-search-container">
                <!-- Search Input Header -->
                <div class="test-search-bar-wrap">
                    <div class="test-search-input-box">
                        <i class="icon-search search-icon"></i>
                        <input type="text"
                               class="test-search-input"
                               placeholder="Type term to search and test rules (e.g. 'contact', 'news', 'services')..."
                               .value=${this._testQuery}
                               @input=${this._handleTestInput}
                               @keydown=${e => { if (e.key === 'Enter') this._runTestSearch(); }}>
                        <button class="btn btn-primary btn-search" @click=${this._runTestSearch} ?disabled=${this._testSearching}>
                            ${this._testSearching ? 'Searching...' : 'Search'}
                        </button>
                    </div>

                    <div class="test-search-options">
                        <div class="test-opt-group">
                            <label>Profile:</label>
                            <select class="sp-select-sm"
                                    .value=${this._testProfileAlias}
                                    @change=${e => { this._testProfileAlias = e.target.value; this._runTestSearch(); }}>
                                ${this._profiles.map(p => html`
                                    <option value="${p.alias}">${p.name} (${p.alias})</option>
                                `)}
                            </select>
                        </div>

                        <div class="test-opt-group">
                            <label>Culture:</label>
                            <select class="sp-select-sm"
                                    .value=${this._testCulture}
                                    @change=${e => { this._testCulture = e.target.value; this._runTestSearch(); }}>
                                <option value="">All Cultures</option>
                                ${(this._catalog.languages || []).map(l => html`
                                    <option value="${l.isoCode}">${l.name} (${l.isoCode})</option>
                                `)}
                            </select>
                        </div>
                    </div>

                    <!-- Autocomplete dropdown suggestions -->
                    ${this._testShowAutocomplete && this._testAutocompleteSuggestions.length > 0 ? html`
                        <div class="autocomplete-dropdown">
                            <div class="autocomplete-header">Type-Ahead Suggestions</div>
                            ${this._testAutocompleteSuggestions.map(s => html`
                                <div class="autocomplete-item" @click=${() => this._selectAutocompleteSuggestion(s)}>
                                    <i class="icon-search"></i>
                                    <span>${s.text}</span>
                                    ${s.contentTypeAlias ? html`<span class="badge-mini">${s.contentTypeAlias}</span>` : nothing}
                                </div>
                            `)}
                        </div>
                    ` : nothing}
                </div>

                <!-- Did you mean / Spelling suggestion -->
                ${this._testResults?.suggestion ? html`
                    <div class="suggestion-banner">
                        <i class="icon-info"></i>
                        <span>Did you mean: <strong class="suggestion-link" @click=${() => { this._testQuery = this._testResults.suggestion; this._runTestSearch(); }}>${this._testResults.suggestion}</strong>?</span>
                    </div>
                ` : nothing}

                <!-- Active Filters Bar -->
                ${Object.keys(this._testActiveFilters).length > 0 ? html`
                    <div class="active-filters-bar">
                        <span>Active Filters:</span>
                        ${Object.entries(this._testActiveFilters).map(([k, vals]) => (vals || []).map(v => html`
                            <span class="tag-badge tag-include">
                                <span>${k}: <strong>${v}</strong></span>
                                <button @click=${() => this._toggleTestFacet(k, v)}>×</button>
                            </span>
                        `))}
                        <button class="btn-clear-filters" @click=${this._clearTestFilters}>Clear All</button>
                    </div>
                ` : nothing}

                <!-- Live Facet Filter Dimension Groups -->
                ${this._testResults?.facets?.length > 0 ? html`
                    <div style="background: #ffffff; border: 1px solid #e2e8f0; border-radius: 8px; padding: 12px 16px; margin-bottom: 16px; box-shadow: 0 1px 2px rgba(0,0,0,0.03);">
                        <div style="display: flex; flex-direction: column; gap: 10px;">
                            ${this._testResults.facets.map(f => html`
                                <div style="display: flex; align-items: center; gap: 8px; flex-wrap: wrap;">
                                    <span style="font-size: 12px; font-weight: 700; color: #334155; min-width: 110px;">
                                        ${f.label || f.alias}:
                                    </span>
                                    <div style="display: flex; gap: 6px; flex-wrap: wrap;">
                                        ${(f.values || []).map(v => {
                                            const isSelected = (this._testActiveFilters[f.alias] || []).includes(v.value);
                                            return html`
                                                <button type="button"
                                                        class="btn ${isSelected ? 'btn-primary' : 'btn-secondary'} btn-sm"
                                                        style="font-size: 11px; padding: 3px 9px; border-radius: 14px; display: inline-flex; align-items: center; gap: 4px;"
                                                        @click=${() => this._toggleTestFacet(f.alias, v.value)}>
                                                    <span>${v.label || v.value}</span>
                                                    <span class="badge ${isSelected ? 'badge-default' : 'badge-info'}" style="font-size: 10px; padding: 1px 5px;">
                                                        ${v.count}
                                                    </span>
                                                </button>
                                            `;
                                        })}
                                    </div>
                                </div>
                            `)}
                        </div>
                    </div>
                ` : nothing}

                <!-- Main 2-Column Results & Diagnostics Area -->
                <div class="test-body-grid ${this._testDiagnosticsOpen ? 'with-diagnostics' : 'full-width'}">
                    <!-- Results List Column -->
                    <div class="test-results-col">
                        ${!this._testResults ? html`
                            <div class="empty-state">
                                <i class="icon-search empty-icon"></i>
                                <h4>Search Rule Tester</h4>
                                <p>Enter a query above to see live results and inspect query planning diagnostics.</p>
                            </div>
                        ` : html`
                            <div class="results-stats-header">
                                <div>Found <strong>${this._testResults.totalResults || 0}</strong> results in <strong>${this._testResults.diagnostics?.elapsedMilliseconds || 0}ms</strong></div>
                                <div>Page ${this._testResults.page} of ${this._testResults.totalPages || 1}</div>
                            </div>

                            ${this._testResults.results.length === 0 ? html`
                                <div class="empty-state">
                                    <h4>No results found for "${this._testResults.term}"</h4>
                                    <p>Try checking index health, broadening filters, or adding synonyms in the profile editor.</p>
                                </div>
                            ` : html`
                                <div class="results-list">
                                    ${this._testResults.results.map((r, idx) => html`
                                        <div class="result-card ${r.isBestBet ? 'result-bestbet' : ''}">
                                            <div class="result-top">
                                                <span class="result-rank">#${idx + 1}</span>
                                                <a href="${r.url || '#'}" target="_blank" class="result-title">${r.name || 'Untitled'}</a>
                                                ${r.isBestBet ? html`<span class="badge badge-default">★ Pinned Best Bet</span>` : nothing}
                                                <span class="badge badge-info">${r.contentTypeAlias || r.indexType}</span>
                                                <span class="score-pill" title="Score: ${r.score} (Raw: ${r.rawScore})">${(r.score || 0).toFixed(2)} pts</span>
                                            </div>

                                            ${r.highlight ? html`
                                                <div class="result-snippet" .innerHTML=${r.highlight}></div>
                                            ` : nothing}

                                            <div class="result-meta">
                                                <span>Index: <code>${r.indexName}</code></span>
                                                ${r.url ? html`<span>URL: <a href="${r.url}" target="_blank">${r.url}</a></span>` : nothing}
                                                ${r.culture ? html`<span>Culture: ${r.culture}</span>` : nothing}
                                            </div>
                                        </div>
                                    `)}
                                </div>
                            `}
                        `}
                    </div>

                    <!-- Diagnostics Inspector Column -->
                    ${this._testDiagnosticsOpen && this._testResults?.diagnostics ? html`
                        <div class="test-diag-col">
                            <div class="diag-card">
                                <div class="diag-header">
                                    <h4>Query Plan Diagnostics</h4>
                                    <span class="badge-mini">${this._testResults.diagnostics.elapsedMilliseconds}ms</span>
                                </div>
                                <div class="diag-body">
                                    <!-- Indexes Queried -->
                                    <div class="diag-section">
                                        <div class="diag-label">Indexes Searched</div>
                                        <div class="diag-tags">
                                            ${(this._testResults.diagnostics.indexesSearched || []).map(idx => html`
                                                <span class="diag-tag">${idx}</span>
                                            `)}
                                        </div>
                                    </div>

                                    <!-- Fields Searched -->
                                    <div class="diag-section">
                                        <div class="diag-label">Fields Searched & Weights</div>
                                        <div class="diag-tags">
                                            ${(this._testResults.diagnostics.fieldsSearched || []).map(f => html`
                                                <span class="diag-tag">${f}</span>
                                            `)}
                                        </div>
                                    </div>

                                    <!-- Resolved Terms -->
                                    <div class="diag-section">
                                        <div class="diag-label">Terms After Expansion</div>
                                        <div class="diag-tags">
                                            ${(this._testResults.diagnostics.resolvedTerms || []).map(t => html`
                                                <span class="diag-tag diag-tag-term">${t}</span>
                                            `)}
                                        </div>
                                    </div>

                                    <!-- Generated Lucene Queries -->
                                    <div class="diag-section">
                                        <div class="diag-label">Examine Lucene Queries</div>
                                        ${Object.entries(this._testResults.diagnostics.queries || {}).map(([idx, q]) => html`
                                            <div class="diag-query-box">
                                                <div class="diag-query-idx">${idx}:</div>
                                                <pre class="diag-query-code">${q}</pre>
                                            </div>
                                        `)}
                                    </div>

                                    <!-- Rule Notes -->
                                    ${(this._testResults.diagnostics.notes || []).length > 0 ? html`
                                        <div class="diag-section">
                                            <div class="diag-label">Rule Execution Notes</div>
                                            <ul class="diag-notes-list">
                                                ${this._testResults.diagnostics.notes.map(n => html`<li>${n}</li>`)}
                                            </ul>
                                        </div>
                                    ` : nothing}
                                </div>
                            </div>
                        </div>
                    ` : nothing}
                </div>
            </div>
        `;
    }

    // ----------------- VIEW 4: INSIGHTS & ANALYTICS -----------------

    _renderInsightsView() {
        if (!this._insights) {
            return html`
                <div class="empty-state">
                    <i class="icon-chart-curve empty-icon"></i>
                    <h4>Loading Analytics...</h4>
                    <p>Fetching query insights report.</p>
                </div>
            `;
        }

        const s = this._insights.summary || {};

        return html`
            <div class="insights-view">
                <!-- Stat Cards Row -->
                <div class="stat-cards-grid">
                    <div class="stat-card">
                        <div class="stat-value">${s.totalSearches || 0}</div>
                        <div class="stat-label">Total Searches</div>
                    </div>
                    <div class="stat-card">
                        <div class="stat-value">${s.uniqueTerms || 0}</div>
                        <div class="stat-label">Unique Terms</div>
                    </div>
                    <div class="stat-card">
                        <div class="stat-value ${(s.zeroResultSearches || 0) > 0 ? 'text-danger' : ''}">${s.zeroResultSearches || 0}</div>
                        <div class="stat-label">Zero-Result Searches (${((s.zeroResultRate || 0) * 100).toFixed(1)}%)</div>
                    </div>
                    <div class="stat-card">
                        <div class="stat-value">${s.totalClicks || 0}</div>
                        <div class="stat-label">Total Clicks (${((s.clickThroughRate || 0) * 100).toFixed(1)}% CTR)</div>
                    </div>
                    <div class="stat-card">
                        <div class="stat-value">${(s.averageDurationMilliseconds || 0).toFixed(1)} ms</div>
                        <div class="stat-label">Avg Query Duration</div>
                    </div>
                </div>

                <!-- Daily Volume Bars -->
                ${(this._insights.volume || []).length > 0 ? html`
                    <div class="card" style="margin-top: 20px;">
                        <div class="card-header">
                            <h4>Daily Search Volume</h4>
                        </div>
                        <div class="card-body">
                            <div class="volume-chart">
                                ${this._insights.volume.slice(-30).map(v => {
                                    const max = Math.max(...this._insights.volume.map(x => x.searchCount || 1), 1);
                                    const heightPct = Math.max(((v.searchCount || 0) / max) * 100, 4);
                                    return html`
                                        <div class="chart-bar-wrap" title="${v.date}: ${v.searchCount} searches (${v.zeroResultCount} zero-result)">
                                            <div class="chart-bar" style="height: ${heightPct}%;"></div>
                                            <div class="chart-date">${String(v.date).slice(5)}</div>
                                        </div>
                                    `;
                                })}
                            </div>
                        </div>
                    </div>
                ` : nothing}

                <!-- Sub-Report Tabs -->
                <div class="insights-tabs-bar" style="margin-top: 24px;">
                    <button class="insights-tab ${this._insightsActiveReport === 'zero' ? 'active' : ''}"
                            @click=${() => { this._insightsActiveReport = 'zero'; this.requestUpdate(); }}>
                        Searches with Zero Results (${(this._insights.zeroResultTerms || []).length})
                    </button>
                    <button class="insights-tab ${this._insightsActiveReport === 'top' ? 'active' : ''}"
                            @click=${() => { this._insightsActiveReport = 'top'; this.requestUpdate(); }}>
                        Top Searched Queries (${(this._insights.topTerms || []).length})
                    </button>
                    <button class="insights-tab ${this._insightsActiveReport === 'unclicked' ? 'active' : ''}"
                            @click=${() => { this._insightsActiveReport = 'unclicked'; this.requestUpdate(); }}>
                        Unclicked Queries (${(this._insights.unclickedTerms || []).length})
                    </button>
                </div>

                <!-- Report Table -->
                <div class="table-card" style="margin-top: 12px;">
                    ${this._renderInsightsReportTable()}
                </div>
            </div>
        `;
    }

    _renderInsightsReportTable() {
        let rows = [];
        if (this._insightsActiveReport === 'zero') rows = this._insights?.zeroResultTerms || [];
        else if (this._insightsActiveReport === 'top') rows = this._insights?.topTerms || [];
        else if (this._insightsActiveReport === 'unclicked') rows = this._insights?.unclickedTerms || [];

        if (rows.length === 0) {
            return html`
                <div class="empty-state">
                    <h4>No data for this time period</h4>
                    <p>Searches and click interactions will appear here once visitors start searching.</p>
                </div>
            `;
        }

        return html`
            <table class="data-table">
                <thead>
                    <tr>
                        <th>Query Term</th>
                        <th>Search Count</th>
                        <th>Avg Results</th>
                        ${this._insightsActiveReport !== 'zero' ? html`
                            <th>Clicks</th>
                            <th>CTR %</th>
                            <th>Avg Click Position</th>
                        ` : nothing}
                        <th>Last Searched</th>
                        <th style="text-align:right;">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    ${rows.map(r => html`
                        <tr>
                            <td><strong>${r.term}</strong></td>
                            <td><span class="badge badge-info">${r.searchCount}</span></td>
                            <td>${(r.averageResultCount || 0).toFixed(1)}</td>
                            ${this._insightsActiveReport !== 'zero' ? html`
                                <td>${r.clickCount || 0}</td>
                                <td>${((r.clickThroughRate || 0) * 100).toFixed(1)}%</td>
                                <td>#${(r.averageClickPosition || 0).toFixed(1)}</td>
                            ` : nothing}
                            <td>${r.lastSearched ? new Date(r.lastSearched).toLocaleDateString() : 'N/A'}</td>
                            <td style="text-align:right;">
                                <button class="btn btn-secondary btn-sm" @click=${() => {
                                    this._testQuery = r.term;
                                    this._changeTab(this._tabs[1]);
                                    this._runTestSearch();
                                }} title="Test this query in search debugger">
                                    <i class="icon-play"></i> Test
                                </button>
                            </td>
                        </tr>
                    `)}
                </tbody>
            </table>
        `;
    }

    // ----------------- VIEW 5: CATALOG & INDEXES -----------------

    _renderCatalogView() {
        const indexes = this._catalog?.indexes || [];

        return html`
            <div class="catalog-view">
                <div class="rule-section-grid">
                    ${indexes.map(idx => html`
                        <div class="card">
                            <div class="card-header flex-between">
                                <div class="card-title-wrap">
                                    <h4>${idx.name}</h4>
                                    <span class="badge ${idx.isHealthy ? 'badge-success' : 'badge-danger'}">
                                        ${idx.isHealthy ? 'Healthy' : 'Error'}
                                    </span>
                                </div>
                                <span class="summary-pill">${idx.isUmbracoIndex ? 'Umbraco Index' : 'Custom Index'}</span>
                            </div>
                            <div class="card-body">
                                <div class="stat-cards-grid" style="grid-template-columns: repeat(2, 1fr); margin-bottom:16px;">
                                    <div class="stat-card" style="padding:12px;">
                                        <div class="stat-value" style="font-size:20px;">${idx.documentCount || 0}</div>
                                        <div class="stat-label">Total Documents</div>
                                    </div>
                                    <div class="stat-card" style="padding:12px;">
                                        <div class="stat-value" style="font-size:20px;">${(idx.fields || []).length}</div>
                                        <div class="stat-label">Discovered Fields</div>
                                    </div>
                                </div>

                                <details class="fields-details">
                                    <summary>View Index Fields (${(idx.fields || []).length})</summary>
                                    <div class="fields-grid" style="margin-top:12px;">
                                        ${(idx.fields || []).map(f => html`
                                            <div class="field-item">
                                                <code>${f.name}</code>
                                                <span class="field-type-pill">${f.type || 'text'}</span>
                                                ${f.isSearchable ? html`<span class="badge-mini" title="Searchable">Search</span>` : nothing}
                                                ${f.isSortable ? html`<span class="badge-mini" title="Sortable">Sort</span>` : nothing}
                                            </div>
                                        `)}
                                    </div>
                                </details>
                            </div>
                        </div>
                    `)}
                </div>
            </div>
        `;
    }

    // ----------------- VIEW 6: SETTINGS -----------------

    _renderSettingsView() {
        const a = this._settings.analytics;
        const s = this._settings.suggestions;

        return html`
            <div class="settings-view">
                <div class="rule-section-grid">
                    <!-- Analytics Settings -->
                    <div class="card">
                        <div class="card-header">
                            <h4>Analytics & Logging Configuration</h4>
                        </div>
                        <div class="card-body">
                            <div class="toggle-list">
                                <label class="toggle-item">
                                    <div class="toggle-info">
                                        <strong>Enable Query Analytics</strong>
                                        <span>Record search queries and interactions in the background.</span>
                                    </div>
                                    <input type="checkbox"
                                           class="switch-input"
                                           .checked=${a.enabled}
                                           @change=${e => { a.enabled = e.target.checked; this.requestUpdate(); }}>
                                </label>

                                <label class="toggle-item">
                                    <div class="toggle-info">
                                        <strong>Record Zero-Results Only</strong>
                                        <span>Only save queries that returned no matches to reduce log volume on high-traffic sites.</span>
                                    </div>
                                    <input type="checkbox"
                                           class="switch-input"
                                           .checked=${a.recordZeroResultsOnly}
                                           @change=${e => { a.recordZeroResultsOnly = e.target.checked; this.requestUpdate(); }}>
                                </label>

                                <label class="toggle-item">
                                    <div class="toggle-info">
                                        <strong>Track Result Click-Throughs</strong>
                                        <span>Track which result visitors clicked from a search page.</span>
                                    </div>
                                    <input type="checkbox"
                                           class="switch-input"
                                           .checked=${a.trackClicks}
                                           @change=${e => { a.trackClicks = e.target.checked; this.requestUpdate(); }}>
                                </label>
                            </div>

                            <div class="form-row-2col" style="margin-top: 16px;">
                                <div class="form-group">
                                    <label class="sp-label">Log Retention (Days, 0 = Forever)</label>
                                    <input type="number"
                                           class="sp-input"
                                           .value=${String(a.retentionDays)}
                                           @input=${e => { a.retentionDays = parseInt(e.target.value) || 90; this.requestUpdate(); }}>
                                </div>

                                <div class="form-group">
                                    <label class="sp-label">Minimum Query Length to Record</label>
                                    <input type="number"
                                           class="sp-input"
                                           .value=${String(a.minimumTermLength)}
                                           @input=${e => { a.minimumTermLength = parseInt(e.target.value) || 2; this.requestUpdate(); }}>
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Suggestions & Autocomplete -->
                    <div class="card">
                        <div class="card-header">
                            <h4>Suggestions & Autocomplete Engine</h4>
                        </div>
                        <div class="card-body">
                            <label class="toggle-item" style="margin-bottom: 16px;">
                                <div class="toggle-info">
                                    <strong>Enable Spelling Suggestions & Type-Ahead</strong>
                                    <span>Provide 'Did you mean' corrections and autocomplete endpoints.</span>
                                </div>
                                <input type="checkbox"
                                       class="switch-input"
                                       .checked=${s.enabled}
                                       @change=${e => { s.enabled = e.target.checked; this.requestUpdate(); }}>
                            </label>

                            <div class="form-row-3col">
                                <div class="form-group">
                                    <label class="sp-label">Suggest Below Result Count</label>
                                    <input type="number"
                                           class="sp-input"
                                           .value=${String(s.suggestBelowResultCount)}
                                           @input=${e => { s.suggestBelowResultCount = parseInt(e.target.value) || 3; this.requestUpdate(); }}>
                                </div>

                                <div class="form-group">
                                    <label class="sp-label">Fuzziness (0.0 - 1.0)</label>
                                    <input type="number"
                                           class="sp-input"
                                           step="0.05"
                                           .value=${String(s.fuzziness)}
                                           @input=${e => { s.fuzziness = parseFloat(e.target.value) || 0.65; this.requestUpdate(); }}>
                                </div>

                                <div class="form-group">
                                    <label class="sp-label">Max Autocomplete Results</label>
                                    <input type="number"
                                           class="sp-input"
                                           .value=${String(s.autocompleteSize)}
                                           @input=${e => { s.autocompleteSize = parseInt(e.target.value) || 10; this.requestUpdate(); }}>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    // ----------------- SIDE PANEL DRAWER -----------------

    _renderSidePanel() {
        const t = this._sidePanelType;
        const d = this._sidePanelData;
        if (!d) return nothing;

        let title = "Configuration";
        let labelTag = "SETTING";
        if (t === 'manageFields') { title = "Searchable Fields & Weightings"; labelTag = "FIELDS"; }
        else if (t === 'editField') { title = d._isNew ? "Add Searchable Field" : `Edit Field: ${d.name}`; labelTag = "FIELD"; }
        else if (t === 'editMatchParameters') { title = "Query Match Parameters"; labelTag = "MATCHING"; }
        else if (t === 'editStopWords') { title = "Stop Words (Ignored Words)"; labelTag = "STOPWORDS"; }
        else if (t === 'manageSynonyms') { title = "Query Synonym Groups"; labelTag = "SYNONYMS"; }
        else if (t === 'editSynonym') { title = "Add Synonym Group"; labelTag = "SYNONYM"; }
        else if (t === 'manageSort') { title = "Sort Priority Levels"; labelTag = "SORT"; }
        else if (t === 'editSort') { title = d._isNew ? "Add Sort Level" : "Edit Sort Level"; labelTag = "SORT"; }
        else if (t === 'manageContentTypeBoosts') { title = "Document Type Relevance Boosts"; labelTag = "BOOSTS"; }
        else if (t === 'editContentTypeBoost') { title = "Add Content Type Boost"; labelTag = "BOOST"; }
        else if (t === 'manageBestBets') { title = "Best Bets (Pinned Results)"; labelTag = "BESTBETS"; }
        else if (t === 'editBestBet') { title = d._isNew ? "Add Best Bet (Pinned Result)" : "Edit Best Bet"; labelTag = "BESTBET"; }
        else if (t === 'editRecency') { title = "Recency Decay & Time Boost"; labelTag = "RECENCY"; }
        else if (t === 'editBlockedTerms') { title = "Blocked Search Terms"; labelTag = "BLOCKED"; }
        else if (t === 'editPaging') { title = "Paging & Result Capacity"; labelTag = "PAGING"; }
        else if (t === 'editHighlighting') { title = "Highlighting & Snippets"; labelTag = "HIGHLIGHT"; }
        else if (t === 'editResultShaping') { title = "Result Shaping & De-Duplication"; labelTag = "SHAPING"; }
        else if (t === 'manageFacets') { title = "Facet Dimensions & Filters"; labelTag = "FILTERS"; }
        else if (t === 'editFacet') { title = d._isNew ? "Add Facet Dimension" : `Edit Facet: ${d.label || d.alias}`; labelTag = "FILTER"; }
        else if (t === 'editSourceIndexes') { title = "Target Examine Indexes"; labelTag = "INDEXES"; }
        else if (t === 'editSourceEntityTypes') { title = "Index Entity Types"; labelTag = "ENTITIES"; }
        else if (t === 'editSourceContentTypes') { title = "Include Document Types"; labelTag = "DOC TYPES"; }
        else if (t === 'editSourceExcludeContentTypes') { title = "Exclude Document Types"; labelTag = "DOC TYPES"; }
        else if (t === 'editSourceRoots') { title = "Search Subtree Roots"; labelTag = "ROOTS"; }
        else if (t === 'editSourceProtection') { title = "Visibility & Protection Rules"; labelTag = "VISIBILITY"; }
        else if (t === 'profileMetadata') { title = "Profile Metadata & Settings"; labelTag = "PROFILE"; }

        const itemName = d.label || d.name || d.alias || d.title || title;

        return html`
            <div class="side-panel-overlay" @click=${this._closeSidePanel}>
                <div class="side-panel-wrapper" @click=${e => e.stopPropagation()}>
                    <div class="sp-body">
                        ${t === 'manageFields' ? this._renderManageFieldsSidePanelBody(d) : nothing}
                        ${t === 'editField' ? this._renderFieldSidePanelBody(d) : nothing}
                        ${t === 'editMatchParameters' ? this._renderMatchParametersSidePanelBody(d) : nothing}
                        ${t === 'editStopWords' ? this._renderStopWordsSidePanelBody(d) : nothing}
                        ${t === 'manageSynonyms' ? this._renderManageSynonymsSidePanelBody(d) : nothing}
                        ${t === 'editSynonym' ? this._renderSynonymSidePanelBody(d) : nothing}
                        ${t === 'manageSort' ? this._renderManageSortSidePanelBody(d) : nothing}
                        ${t === 'editSort' ? this._renderSortSidePanelBody(d) : nothing}
                        ${t === 'manageContentTypeBoosts' ? this._renderManageContentTypeBoostsSidePanelBody(d) : nothing}
                        ${t === 'editContentTypeBoost' ? this._renderContentTypeBoostSidePanelBody(d) : nothing}
                        ${t === 'manageBestBets' ? this._renderManageBestBetsSidePanelBody(d) : nothing}
                        ${t === 'editBestBet' ? this._renderBestBetSidePanelBody(d) : nothing}
                        ${t === 'editRecency' ? this._renderRecencySidePanelBody(d) : nothing}
                        ${t === 'editBlockedTerms' ? this._renderBlockedTermsSidePanelBody(d) : nothing}
                        ${t === 'editPaging' ? this._renderPagingSidePanelBody(d) : nothing}
                        ${t === 'editHighlighting' ? this._renderHighlightingSidePanelBody(d) : nothing}
                        ${t === 'editResultShaping' ? this._renderResultShapingSidePanelBody(d) : nothing}
                        ${t === 'manageFacets' ? this._renderManageFacetsSidePanelBody(d) : nothing}
                        ${t === 'editFacet' ? this._renderFacetSidePanelBody(d) : nothing}
                        ${t === 'editSourceIndexes' ? this._renderSourceIndexesSidePanelBody(d) : nothing}
                        ${t === 'editSourceEntityTypes' ? this._renderSourceEntityTypesSidePanelBody(d) : nothing}
                        ${t === 'editSourceContentTypes' ? this._renderSourceContentTypesSidePanelBody(d, false) : nothing}
                        ${t === 'editSourceExcludeContentTypes' ? this._renderSourceContentTypesSidePanelBody(d, true) : nothing}
                        ${t === 'editSourceRoots' ? this._renderSourceRootsSidePanelBody(d) : nothing}
                        ${t === 'editSourceProtection' ? this._renderSourceProtectionSidePanelBody(d) : nothing}
                        ${t === 'profileMetadata' ? this._renderProfileMetadataSidePanelBody(d) : nothing}
                    </div>

                    <div class="builder-footer" style="position: static; height: 60px; flex-shrink: 0;">
                        <div class="footer-left">
                            <span class="footer-form-label">${labelTag}</span>
                            <span class="footer-divider">/</span>
                            <span class="footer-form-name">${itemName}</span>
                        </div>
                        <div class="footer-right">
                            <button class="footer-btn" @click=${this._closeSidePanel} title="Cancel">
                                <span class="footer-discard-btn">Cancel</span>
                            </button>
                            <div class="header-divider"></div>
                            <button class="footer-btn" @click=${() => this._saveSidePanel()} title="Submit">
                                <span class="footer-save-btn">Submit</span>
                            </button>
                        </div>
                    </div>
                </div>
            </div>
            ${this._showFilterTypePicker ? this._renderFilterTypePicker() : nothing}
        `;
    }

    _renderSourceIndexesSidePanelBody(d) {
        const indexes = this._catalog?.indexes || [];
        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-choice-header-info">
                    <p class="sp-choice-desc">
                        Select which Examine indexes to search. If none are selected, search will automatically query all available indexes on the site.
                    </p>
                    <div class="sp-choice-actions">
                        <button class="btn btn-secondary btn-sm" @click=${() => { d.indexes = []; this.requestUpdate(); }}>
                            Reset to All (Automatic)
                        </button>
                        <button class="btn btn-secondary btn-sm" @click=${() => { d.indexes = indexes.map(i => i.name); this.requestUpdate(); }}>
                            Select All (${indexes.length})
                        </button>
                    </div>
                </div>

                <div class="sp-choices-list">
                    ${indexes.map(idx => {
                        const isChecked = (d.indexes || []).includes(idx.name);
                        return html`
                            <label class="sp-choice-card ${isChecked ? 'sp-choice-active' : ''}">
                                <div class="sp-choice-info">
                                    <strong class="sp-choice-title">${idx.name}</strong>
                                    <span class="sp-choice-meta">${idx.documentCount || 0} indexed documents</span>
                                </div>
                                <input type="checkbox"
                                       class="switch-input"
                                       .checked=${isChecked}
                                       @change=${e => {
                                           if (!d.indexes) d.indexes = [];
                                           if (e.target.checked) d.indexes.push(idx.name);
                                           else d.indexes = d.indexes.filter(n => n !== idx.name);
                                           this.requestUpdate();
                                       }}>
                            </label>
                        `;
                    })}
                </div>
            </div>
        `;
    }

    _renderSourceEntityTypesSidePanelBody(d) {
        const types = [
            { id: 'content', label: 'Content (Documents & Pages)', desc: 'Standard Umbraco published content pages' },
            { id: 'media', label: 'Media (Files & Images)', desc: 'Media library assets, PDF documents, and images' },
            { id: 'member', label: 'Members', desc: 'Registered frontend member accounts' }
        ];
        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-choice-header-info">
                    <p class="sp-choice-desc">
                        Filter search results by Examine entity category. If none are selected, all categories are eligible.
                    </p>
                    <div class="sp-choice-actions">
                        <button class="btn btn-secondary btn-sm" @click=${() => { d.indexTypes = []; this.requestUpdate(); }}>
                            Reset to All Categories
                        </button>
                        <button class="btn btn-secondary btn-sm" @click=${() => { d.indexTypes = types.map(t => t.id); this.requestUpdate(); }}>
                            Select All (${types.length})
                        </button>
                    </div>
                </div>

                <div class="sp-choices-list">
                    ${types.map(t => {
                        const isChecked = (d.indexTypes || []).includes(t.id);
                        return html`
                            <label class="sp-choice-card ${isChecked ? 'sp-choice-active' : ''}">
                                <div class="sp-choice-info">
                                    <strong class="sp-choice-title">${t.label}</strong>
                                    <span class="sp-choice-meta">${t.desc}</span>
                                </div>
                                <input type="checkbox"
                                       class="switch-input"
                                       .checked=${isChecked}
                                       @change=${e => {
                                           if (!d.indexTypes) d.indexTypes = [];
                                           if (e.target.checked) d.indexTypes.push(t.id);
                                           else d.indexTypes = d.indexTypes.filter(x => x !== t.id);
                                           this.requestUpdate();
                                       }}>
                            </label>
                        `;
                    })}
                </div>
            </div>
        `;
    }

    _renderSourceContentTypesSidePanelBody(d, isExclude = false) {
        const contentTypes = [...(this._catalog?.contentTypes || []), ...(this._catalog?.mediaTypes || [])];
        const q = (d._searchFilter || '').toLowerCase();
        const filtered = contentTypes.filter(ct => !q || (ct.name && ct.name.toLowerCase().includes(q)) || (ct.alias && ct.alias.toLowerCase().includes(q)));
        const targetList = isExclude ? (d.excludeContentTypes || []) : (d.includeContentTypes || []);

        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-choice-header-info">
                    <div class="search-box-wrap" style="width: 100%; max-width: 100%;">
                        <i class="icon-search search-box-icon"></i>
                        <input type="text"
                               class="search-box-input"
                               placeholder="Search content types by name or alias..."
                               .value=${d._searchFilter || ''}
                               @input=${e => { d._searchFilter = e.target.value; this.requestUpdate(); }}>
                    </div>
                    <div class="sp-choice-actions">
                        <span class="text-muted" style="font-size: 12px; font-weight: 500;">
                            ${targetList.length} of ${contentTypes.length} ${isExclude ? 'excluded' : 'included'}
                        </span>
                        <div style="display: flex; gap: 8px;">
                            <button class="btn btn-secondary btn-sm" @click=${() => {
                                if (isExclude) d.excludeContentTypes = [];
                                else d.includeContentTypes = [];
                                this.requestUpdate();
                            }}>
                                Clear All
                            </button>
                            <button class="btn btn-secondary btn-sm" @click=${() => {
                                if (isExclude) d.excludeContentTypes = contentTypes.map(c => c.alias);
                                else d.includeContentTypes = contentTypes.map(c => c.alias);
                                this.requestUpdate();
                            }}>
                                Select All Filtered
                            </button>
                        </div>
                    </div>
                </div>

                <div class="sp-choices-list">
                    ${filtered.map(ct => {
                        const isChecked = targetList.includes(ct.alias);
                        return html`
                            <label class="sp-choice-card ${isChecked ? 'sp-choice-active' : ''}">
                                <div class="sp-choice-info">
                                    <strong class="sp-choice-title">${ct.name}</strong>
                                    <span class="sp-choice-meta"><code>${ct.alias}</code></span>
                                </div>
                                <input type="checkbox"
                                       class="switch-input"
                                       .checked=${isChecked}
                                       @change=${e => {
                                           if (isExclude) {
                                               if (!d.excludeContentTypes) d.excludeContentTypes = [];
                                               if (e.target.checked) d.excludeContentTypes.push(ct.alias);
                                               else d.excludeContentTypes = d.excludeContentTypes.filter(a => a !== ct.alias);
                                           } else {
                                               if (!d.includeContentTypes) d.includeContentTypes = [];
                                               if (e.target.checked) d.includeContentTypes.push(ct.alias);
                                               else d.includeContentTypes = d.includeContentTypes.filter(a => a !== ct.alias);
                                           }
                                           this.requestUpdate();
                                       }}>
                            </label>
                        `;
                    })}
                    ${filtered.length === 0 ? html`
                        <div class="card-empty-pad" style="text-align: center;">
                            <span class="text-muted" style="font-size: 13px;">No content types match '${d._searchFilter}'</span>
                        </div>
                    ` : nothing}
                </div>
            </div>
        `;
    }

    _renderSourceRootsSidePanelBody(d) {
        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-choice-header-info">
                    <p class="sp-choice-desc">
                        Select root content page(s) or branches from the Umbraco tree. Search queries will be scoped strictly to these subtrees. Leave empty to search the entire site.
                    </p>
                </div>

                <div class="sp-group" style="margin-bottom: 20px; width: 100%;">
                    <label class="sp-label" style="font-weight: 700; margin-bottom: 8px;">Content Tree / URL Picker</label>
                    <umb-input-document
                        .selection=${d.rootNodeKeys || []}
                        @change=${e => {
                            d.rootNodeKeys = e.target.selection || [];
                            d._rootsInput = (d.rootNodeKeys || []).join('\n');
                            this.requestUpdate();
                        }}>
                    </umb-input-document>
                </div>

                <div class="sp-group" style="margin-top: 16px;">
                    <label class="toggle-item">
                        <div class="toggle-info">
                            <strong>Exclude Descendants of Excluded Nodes</strong>
                            <span>If an excluded document has children, automatically exclude all descendants in the tree.</span>
                        </div>
                        <input type="checkbox"
                               class="switch-input"
                               .checked=${d.excludeDescendantsOfExcludedNodes !== false}
                               @change=${e => { d.excludeDescendantsOfExcludedNodes = e.target.checked; this.requestUpdate(); }}>
                    </label>
                </div>
            </div>
        `;
    }

    _renderSourceProtectionSidePanelBody(d) {
        return html`
            <div class="sp-group">
                <label class="toggle-item" style="margin-bottom: 16px;">
                    <div class="toggle-info">
                        <strong>Published Content Only</strong>
                        <span>Only return content that is currently in a published state.</span>
                    </div>
                    <input type="checkbox"
                           class="switch-input"
                           .checked=${d.publishedOnly !== false}
                           @change=${e => { d.publishedOnly = e.target.checked; this.requestUpdate(); }}>
                </label>

                <label class="toggle-item" style="margin-bottom: 16px;">
                    <div class="toggle-info">
                        <strong>Respect Navigation Hide (umbracoNaviHide)</strong>
                        <span>Exclude content where the umbracoNaviHide property is set to True.</span>
                    </div>
                    <input type="checkbox"
                           class="switch-input"
                           .checked=${d.respectNaviHide !== false}
                           @change=${e => { d.respectNaviHide = e.target.checked; this.requestUpdate(); }}>
                </label>

                <label class="toggle-item">
                    <div class="toggle-info">
                        <strong>Exclude Protected Content</strong>
                        <span>Exclude content restricted by Umbraco Public Access (Members Only).</span>
                    </div>
                    <input type="checkbox"
                           class="switch-input"
                           .checked=${d.excludeProtected !== false}
                           @change=${e => { d.excludeProtected = e.target.checked; this.requestUpdate(); }}>
                </label>
            </div>
        `;
    }

    _renderManageFieldsSidePanelBody(d) {
        const fields = d.fields || [];
        const q = (d._searchFilter || '').toLowerCase();
        const filtered = fields.filter(f => !q || (f.name && f.name.toLowerCase().includes(q)));

        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-choice-header-info">
                    <div style="display: flex; gap: 8px; align-items: center; width: 100%;">
                        <div class="search-box-wrap" style="flex: 1; margin: 0;">
                            <i class="icon-search search-box-icon"></i>
                            <input type="text"
                                   class="search-box-input"
                                   placeholder="Filter searchable fields..."
                                   .value=${d._searchFilter || ''}
                                   @input=${e => { d._searchFilter = e.target.value; this.requestUpdate(); }}>
                        </div>
                        <button class="btn btn-primary btn-sm" @click=${() => this._openSidePanel('editField')}>
                            + Add Field
                        </button>
                    </div>
                    <div class="sp-choice-actions">
                        <span class="text-muted" style="font-size: 12px; font-weight: 500;">
                            ${fields.length} search fields defined
                        </span>
                    </div>
                </div>

                <div class="sp-choices-list">
                    ${filtered.map(f => html`
                        <div class="sp-choice-card" style="cursor: default;">
                            <div class="sp-choice-info">
                                <div style="display: flex; align-items: center; gap: 8px;">
                                    <strong class="sp-choice-title">${f.name}</strong>
                                    <span class="badge badge-info">${f.boost}x boost</span>
                                    <span class="badge">${f.matchMode || 'prefix'}</span>
                                </div>
                                <span class="sp-choice-meta">Target examine document property</span>
                            </div>
                            <div style="display: flex; align-items: center; gap: 8px;">
                                <input type="checkbox"
                                       class="switch-input"
                                       title="Enable/Disable Field"
                                       .checked=${f.enabled !== false}
                                       @change=${e => { f.enabled = e.target.checked; this.requestUpdate(); }}>
                                <button class="btn-icon" title="Edit Field" @click=${() => this._openSidePanel('editField', f)}>
                                    <i class="icon-edit"></i>
                                </button>
                                <button class="btn-icon btn-icon-danger" title="Remove Field" @click=${() => {
                                    d.fields = d.fields.filter(x => x.name !== f.name);
                                    this.requestUpdate();
                                }}>
                                    <i class="icon-trash"></i>
                                </button>
                            </div>
                        </div>
                    `)}
                    ${filtered.length === 0 ? html`
                        <div class="card-empty-pad" style="text-align: center;">
                            <span class="text-muted" style="font-size: 13px;">No fields match '${d._searchFilter}'</span>
                        </div>
                    ` : nothing}
                </div>
            </div>
        `;
    }

    _renderMatchParametersSidePanelBody(d) {
        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-group" style="margin-bottom: 16px;">
                    <label class="sp-label">Default Combine Operator</label>
                    <select class="sp-select"
                            .value=${d.defaultOperator}
                            @change=${e => { d.defaultOperator = e.target.value; this.requestUpdate(); }}>
                        <option value="or">OR (Any field/term matches - Broadest results)</option>
                        <option value="and">AND (All terms must match - Strict results)</option>
                    </select>
                    <span class="sp-hint">Defines how multiple search words are joined in the underlying query parser.</span>
                </div>

                <div class="sp-group" style="margin-bottom: 16px;">
                    <label class="sp-label">Fuzziness Tolerance (0.0 - 1.0)</label>
                    <div class="sp-slider-row">
                        <input type="range"
                               min="0.1"
                               max="1.0"
                               step="0.05"
                               .value=${String(d.fuzziness)}
                               @input=${e => { d.fuzziness = parseFloat(e.target.value) || 0.8; this.requestUpdate(); }}>
                        <input type="number"
                               class="sp-input"
                               style="width: 80px;"
                               min="0.1"
                               max="1.0"
                               step="0.05"
                               .value=${String(d.fuzziness)}
                               @input=${e => { d.fuzziness = parseFloat(e.target.value) || 0.8; this.requestUpdate(); }}>
                    </div>
                    <span class="sp-hint">Controls typo tolerance when fuzzy matching is engaged. 0.8 is standard.</span>
                </div>

                <div class="sp-group" style="margin-bottom: 16px;">
                    <label class="sp-label">Minimum Query Length (Characters)</label>
                    <input type="number"
                           class="sp-input"
                           min="1"
                           max="10"
                           .value=${String(d.minimumQueryLength)}
                           @input=${e => { d.minimumQueryLength = parseInt(e.target.value) || 2; this.requestUpdate(); }}>
                    <span class="sp-hint">Queries shorter than this threshold will return empty results immediately.</span>
                </div>

                <div class="sp-group" style="margin-top: 10px;">
                    <label class="toggle-item">
                        <div class="toggle-info">
                            <strong>All Terms Must Match</strong>
                            <span>Require every single word in multi-word queries to appear in matching documents.</span>
                        </div>
                        <input type="checkbox"
                               class="switch-input"
                               .checked=${!!d.allTermsMustMatch}
                               @change=${e => { d.allTermsMustMatch = e.target.checked; this.requestUpdate(); }}>
                    </label>
                </div>
            </div>
        `;
    }

    _renderStopWordsSidePanelBody(d) {
        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-choice-header-info">
                    <p class="sp-choice-desc">
                        Words that will be automatically stripped from user queries before searching Examine indexes.
                    </p>
                    <div style="display: flex; gap: 8px;">
                        <button class="btn btn-secondary btn-sm" @click=${() => {
                            const defaults = ["a","about","an","and","are","as","at","be","by","for","from","how","in","is","it","of","on","or","that","the","this","to","was","what","when","where","who","will","with"];
                            d._wordsInput = defaults.join('\n');
                            this.requestUpdate();
                        }}>
                            Load Standard English Defaults
                        </button>
                        <button class="btn btn-secondary btn-sm" @click=${() => { d._wordsInput = ''; this.requestUpdate(); }}>
                            Clear All
                        </button>
                    </div>
                </div>

                <div class="sp-group" style="flex: 1; height: 100%; display: flex; flex-direction: column;">
                    <label class="sp-label">Stop Words (One word per line)</label>
                    <textarea class="sp-textarea"
                              style="flex: 1; min-height: 250px; font-family: monospace; font-size: 13px;"
                              placeholder="e.g.&#10;the&#10;and&#10;is&#10;for"
                              .value=${d._wordsInput || ''}
                              @input=${e => { d._wordsInput = e.target.value; this.requestUpdate(); }}></textarea>
                </div>
            </div>
        `;
    }

    _renderManageSynonymsSidePanelBody(d) {
        const synonyms = d.synonyms || {};
        const entries = Object.entries(synonyms);

        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-choice-header-info">
                    <p class="sp-choice-desc">
                        When users search for a term, automatically expand their search to also match equivalent synonyms.
                    </p>
                    <button class="btn btn-primary btn-sm" @click=${() => this._openSidePanel('editSynonym')}>
                        + Add Synonym Group
                    </button>
                </div>

                <div class="sp-choices-list">
                    ${entries.map(([term, syns]) => html`
                        <div class="sp-choice-card" style="cursor: default;">
                            <div class="sp-choice-info">
                                <strong class="sp-choice-title">${term}</strong>
                                <span class="sp-choice-meta">Expands to: <code>${(syns || []).join(', ')}</code></span>
                            </div>
                            <button class="btn-icon btn-icon-danger" title="Delete Synonym" @click=${() => {
                                delete d.synonyms[term];
                                this.requestUpdate();
                            }}>
                                <i class="icon-trash"></i>
                            </button>
                        </div>
                    `)}
                    ${entries.length === 0 ? html`
                        <div class="card-empty-pad" style="text-align: center;">
                            <span class="text-muted" style="font-size: 13px;">No synonym groups defined yet.</span>
                        </div>
                    ` : nothing}
                </div>
            </div>
        `;
    }

    _renderManageSortSidePanelBody(d) {
        const sortBy = d.sortBy || [];

        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-choice-header-info">
                    <p class="sp-choice-desc">
                        Define ordered sort rules for search results. If none are specified, results are ordered purely by calculated relevance score.
                    </p>
                    <button class="btn btn-primary btn-sm" @click=${() => this._openSidePanel('editSort')}>
                        + Add Sort Level
                    </button>
                </div>

                <div class="sp-choices-list">
                    ${sortBy.map((s, idx) => html`
                        <div class="sp-choice-card" style="cursor: default;">
                            <div class="sp-choice-info">
                                <div style="display: flex; align-items: center; gap: 8px;">
                                    <span class="badge badge-info">Priority #${idx + 1}</span>
                                    <strong class="sp-choice-title">${s.field}</strong>
                                </div>
                                <span class="sp-choice-meta">Sorted in <strong>${s.direction?.toUpperCase() || 'ASCENDING'}</strong> order</span>
                            </div>
                            <button class="btn-icon btn-icon-danger" title="Remove Sort Level" @click=${() => {
                                d.sortBy.splice(idx, 1);
                                this.requestUpdate();
                            }}>
                                <i class="icon-trash"></i>
                            </button>
                        </div>
                    `)}
                    ${sortBy.length === 0 ? html`
                        <div class="card-empty-pad" style="text-align: center;">
                            <span class="text-muted" style="font-size: 13px;">No custom sort levels. Defaulting to Relevance Score (Descending).</span>
                        </div>
                    ` : nothing}
                </div>
            </div>
        `;
    }

    _renderManageContentTypeBoostsSidePanelBody(d) {
        const boosts = d.contentTypeBoosts || {};
        const entries = Object.entries(boosts);

        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-choice-header-info">
                    <p class="sp-choice-desc">
                        Apply multiplying weights to specific Umbraco document types (e.g. boost News Articles by 2.0x, Products by 1.5x).
                    </p>
                    <button class="btn btn-primary btn-sm" @click=${() => this._openSidePanel('editContentTypeBoost')}>
                        + Add Document Type Boost
                    </button>
                </div>

                <div class="sp-choices-list">
                    ${entries.map(([alias, boost]) => html`
                        <div class="sp-choice-card" style="cursor: default;">
                            <div class="sp-choice-info">
                                <strong class="sp-choice-title">${alias}</strong>
                                <span class="sp-choice-meta">Score multiplier: <strong>${boost}x</strong></span>
                            </div>
                            <div style="display: flex; align-items: center; gap: 8px;">
                                <input type="number"
                                       class="sp-input"
                                       style="width: 75px;"
                                       step="0.1"
                                       min="0.1"
                                       max="50"
                                       .value=${String(boost)}
                                       @input=${e => {
                                           d.contentTypeBoosts[alias] = parseFloat(e.target.value) || 1.0;
                                           this.requestUpdate();
                                       }}>
                                <button class="btn-icon btn-icon-danger" title="Remove Boost" @click=${() => {
                                    delete d.contentTypeBoosts[alias];
                                    this.requestUpdate();
                                }}>
                                    <i class="icon-trash"></i>
                                </button>
                            </div>
                        </div>
                    `)}
                    ${entries.length === 0 ? html`
                        <div class="card-empty-pad" style="text-align: center;">
                            <span class="text-muted" style="font-size: 13px;">No document type boosts configured.</span>
                        </div>
                    ` : nothing}
                </div>
            </div>
        `;
    }

    _renderManageBestBetsSidePanelBody(d) {
        const bets = d.bestBets || [];

        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-choice-header-info">
                    <p class="sp-choice-desc">
                        Pin curated Umbraco content pages to the top of search results when visitors type specific trigger terms.
                    </p>
                    <button class="btn btn-primary btn-sm" @click=${() => this._openSidePanel('editBestBet')}>
                        + Add Best Bet (Pinned Result)
                    </button>
                </div>

                <div class="sp-choices-list">
                    ${bets.map((b, idx) => html`
                        <div class="sp-choice-card" style="cursor: default;">
                            <div class="sp-choice-info">
                                <div style="display: flex; flex-wrap: wrap; gap: 4px; margin-bottom: 4px;">
                                    ${(b.terms || []).map(t => html`<span class="badge badge-info">${t}</span>`)}
                                </div>
                                <span class="sp-choice-meta">Pinned nodes: <code>${(b.nodeKeys || []).join(', ')}</code></span>
                            </div>
                            <button class="btn-icon btn-icon-danger" title="Remove Best Bet" @click=${() => {
                                d.bestBets.splice(idx, 1);
                                this.requestUpdate();
                            }}>
                                <i class="icon-trash"></i>
                            </button>
                        </div>
                    `)}
                    ${bets.length === 0 ? html`
                        <div class="card-empty-pad" style="text-align: center;">
                            <span class="text-muted" style="font-size: 13px;">No best bets defined. Results are ordered purely by algorithm.</span>
                        </div>
                    ` : nothing}
                </div>
            </div>
        `;
    }

    _renderRecencySidePanelBody(d) {
        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-group" style="margin-bottom: 16px;">
                    <label class="toggle-item">
                        <div class="toggle-info">
                            <strong>Enable Recency Boost</strong>
                            <span>Lift freshly published or updated documents above older pages.</span>
                        </div>
                        <input type="checkbox"
                               class="switch-input"
                               .checked=${!!d.enabled}
                               @change=${e => { d.enabled = e.target.checked; this.requestUpdate(); }}>
                    </label>
                </div>

                <div class="sp-group" style="margin-bottom: 16px;">
                    <label class="sp-label">Half-Life (Days)</label>
                    <input type="number"
                           class="sp-input"
                           min="1"
                           max="1000"
                           .value=${String(d.halfLifeDays)}
                           @input=${e => { d.halfLifeDays = parseInt(e.target.value) || 90; this.requestUpdate(); }}>
                    <span class="sp-hint">Number of days after which a document's recency score boost drops by 50%.</span>
                </div>

                <div class="sp-group">
                    <label class="sp-label">Recency Weight Multiplier</label>
                    <div class="sp-slider-row">
                        <input type="range"
                               min="0.1"
                               max="5.0"
                               step="0.1"
                               .value=${String(d.weight)}
                               @input=${e => { d.weight = parseFloat(e.target.value) || 0.5; this.requestUpdate(); }}>
                        <input type="number"
                               class="sp-input"
                               style="width: 80px;"
                               min="0.1"
                               max="10.0"
                               step="0.1"
                               .value=${String(d.weight)}
                               @input=${e => { d.weight = parseFloat(e.target.value) || 0.5; this.requestUpdate(); }}>
                    </div>
                </div>
            </div>
        `;
    }

    _renderBlockedTermsSidePanelBody(d) {
        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-choice-header-info">
                    <p class="sp-choice-desc">
                        Search queries containing these terms will return 0 results or be blocked automatically.
                    </p>
                </div>

                <div class="sp-group" style="flex: 1; height: 100%; display: flex; flex-direction: column;">
                    <label class="sp-label">Blocked Terms (One term per line)</label>
                    <textarea class="sp-textarea"
                              style="flex: 1; min-height: 250px; font-family: monospace; font-size: 13px;"
                              placeholder="e.g.&#10;confidential&#10;internal&#10;draft"
                              .value=${d._termsInput || ''}
                              @input=${e => { d._termsInput = e.target.value; this.requestUpdate(); }}></textarea>
                </div>
            </div>
        `;
    }

    _renderPagingSidePanelBody(d) {
        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-group" style="margin-bottom: 20px;">
                    <label class="sp-label">Default Page Size</label>
                    <input type="number"
                           class="sp-input"
                           min="1"
                           max="100"
                           .value=${String(d.pageSize)}
                           @input=${e => { d.pageSize = parseInt(e.target.value) || 10; this.requestUpdate(); }}>
                    <span class="sp-hint">Number of results displayed per page by default.</span>
                </div>

                <div class="sp-group">
                    <label class="sp-label">Maximum Results Considered</label>
                    <input type="number"
                           class="sp-input"
                           min="10"
                           max="5000"
                           .value=${String(d.maxResults)}
                           @input=${e => { d.maxResults = parseInt(e.target.value) || 500; this.requestUpdate(); }}>
                    <span class="sp-hint">Upper limit on the total result set evaluated for relevance and pagination.</span>
                </div>
            </div>
        `;
    }

    _renderHighlightingSidePanelBody(d) {
        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-group" style="margin-bottom: 16px;">
                    <label class="toggle-item">
                        <div class="toggle-info">
                            <strong>Enable Snippet Highlights</strong>
                            <span>Extract snippet text with matched query terms highlighted in bold/markup.</span>
                        </div>
                        <input type="checkbox"
                               class="switch-input"
                               .checked=${!!d.enabled}
                               @change=${e => { d.enabled = e.target.checked; this.requestUpdate(); }}>
                    </label>
                </div>

                <div class="sp-group" style="margin-bottom: 16px;">
                    <label class="sp-label">Snippet Mode</label>
                    <select class="sp-select"
                            .value=${d.mode || 'sentence'}
                            @change=${e => { d.mode = e.target.value; this.requestUpdate(); }}>
                        <option value="sentence">Full Sentence (Natural cut)</option>
                        <option value="characters">Fixed Characters (Truncated)</option>
                    </select>
                </div>

                <div class="sp-group" style="margin-bottom: 16px;">
                    <label class="sp-label">Max Snippet Length (Characters)</label>
                    <input type="number"
                           class="sp-input"
                           min="50"
                           max="1000"
                           .value=${String(d.snippetLength)}
                           @input=${e => { d.snippetLength = parseInt(e.target.value) || 200; this.requestUpdate(); }}>
                </div>

                <div class="sp-group">
                    <label class="sp-label">Sentence Context (Sentences)</label>
                    <input type="number"
                           class="sp-input"
                           min="0"
                           max="5"
                           .value=${String(d.sentenceContext)}
                           @input=${e => { d.sentenceContext = parseInt(e.target.value) || 0; this.requestUpdate(); }}>
                </div>
            </div>
        `;
    }

    _renderResultShapingSidePanelBody(d) {
        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-group" style="margin-bottom: 20px;">
                    <label class="sp-label">De-Duplicate Results by Field</label>
                    <input type="text"
                           class="sp-input"
                           placeholder="e.g. urlName, parentId (leave empty to disable)"
                           .value=${d.deduplicateByField || ''}
                           @input=${e => { d.deduplicateByField = e.target.value; this.requestUpdate(); }}>
                    <span class="sp-hint">If multiple matches have the same value for this field, only the highest ranking one is returned.</span>
                </div>

                <div class="sp-group">
                    <label class="toggle-item">
                        <div class="toggle-info">
                            <strong>Group Results by Document Type</strong>
                            <span>Aggregate result counts grouped by content type alias in search response.</span>
                        </div>
                        <input type="checkbox"
                               class="switch-input"
                               .checked=${!!d.groupByContentType}
                               @change=${e => { d.groupByContentType = e.target.checked; this.requestUpdate(); }}>
                    </label>
                </div>
            </div>
        `;
    }

    _renderManageFacetsSidePanelBody(d) {
        const facets = d.facets || [];

        return html`
            <div class="sp-multi-choice-layout">
                <div class="sp-choice-header-info">
                    <p class="sp-choice-desc">
                        Define dynamic facet dimensions to return aggregated bucket counts for frontend search filter sidebars.
                    </p>
                    <button class="btn btn-primary btn-sm" @click=${() => this._openSidePanel('editFacet')}>
                        + Add Facet Dimension
                    </button>
                </div>

                <div class="sp-choices-list">
                    ${facets.map((f, idx) => html`
                        <div class="sp-choice-card" style="cursor: default;">
                            <div class="sp-choice-info">
                                <div style="display: flex; align-items: center; gap: 8px;">
                                    <strong class="sp-choice-title">${f.label || f.alias}</strong>
                                    <span class="badge badge-info">${f.kind || 'field'}</span>
                                </div>
                                <span class="sp-choice-meta">Field: <code>${f.field}</code> | Max buckets: ${f.maxValues || 20}${f.ranges?.length ? ` | ${f.ranges.length} range options` : ''}</span>
                            </div>
                            <div style="display: flex; align-items: center; gap: 8px;">
                                <button class="btn-icon" title="Edit Facet" @click=${() => this._openSidePanel('editFacet', f)}>
                                    <i class="icon-edit"></i>
                                </button>
                                <button class="btn-icon btn-icon-danger" title="Remove Facet" @click=${() => {
                                    d.facets.splice(idx, 1);
                                    this.requestUpdate();
                                }}>
                                    <i class="icon-trash"></i>
                                </button>
                            </div>
                        </div>
                    `)}
                    ${facets.length === 0 ? html`
                        <div class="card-empty-pad" style="text-align: center;">
                            <span class="text-muted" style="font-size: 13px;">No facet dimensions defined yet.</span>
                        </div>
                    ` : nothing}
                </div>
            </div>
        `;
    }

    _renderFieldSidePanelBody(d) {
        return html`
            <div class="sp-group">
                <label class="sp-label">Field Name *</label>
                <input type="text"
                       class="sp-input"
                       placeholder="e.g. nodeName, bodyText, metaDescription"
                       .value=${d.name}
                       @input=${e => { d.name = e.target.value; this.requestUpdate(); }}>
                ${this._sidePanelErrors.name ? html`<span class="sp-error">${this._sidePanelErrors.name}</span>` : nothing}
            </div>

            <div class="sp-group">
                <label class="sp-label">Match Mode</label>
                <select class="sp-select"
                        .value=${d.matchMode}
                        @change=${e => { d.matchMode = e.target.value; this.requestUpdate(); }}>
                    <option value="prefix">Prefix (term* - Default)</option>
                    <option value="exact">Exact Match</option>
                    <option value="fuzzy">Fuzzy (term~ typo tolerant)</option>
                    <option value="wildcard">Wildcard (*term*)</option>
                </select>
            </div>

            <div class="sp-group">
                <label class="sp-label">Boost Multiplier (Weight)</label>
                <div class="sp-slider-row">
                    <input type="range"
                           min="0.1"
                           max="20.0"
                           step="0.1"
                           .value=${String(d.boost)}
                           @input=${e => { d.boost = parseFloat(e.target.value); this.requestUpdate(); }}>
                    <input type="number"
                           class="sp-input"
                           style="width: 90px;"
                           min="0.1"
                           max="100.0"
                           step="0.1"
                           .value=${String(d.boost)}
                           @input=${e => { d.boost = parseFloat(e.target.value); this.requestUpdate(); }}>
                </div>
                <small class="text-muted">1.0 is neutral. A title field at 10.0 will strongly outrank body text matches.</small>
            </div>

            <div class="sp-group">
                <label class="toggle-item">
                    <div class="toggle-info">
                        <strong>Field Enabled</strong>
                        <span>Temporarily include or exclude this field from searches.</span>
                    </div>
                    <input type="checkbox"
                           class="switch-input"
                           .checked=${d.enabled}
                           @change=${e => { d.enabled = e.target.checked; this.requestUpdate(); }}>
                </label>
            </div>
        `;
    }

    _renderFacetSidePanelBody(d) {
        if (!Array.isArray(d.ranges)) {
            d.ranges = [];
        }

        const fField = String(d.field || '').toLowerCase();
        const fKind = String(d.kind || '').toLowerCase();
        const filterType = d.filterType || (
            (fField === '__nodetypealias' || fField === 'contenttypealias' || fField === 'contenttype') ? 'contentType' :
            (fField === '__path' || fField === 'path' || fField === '__key' || fField === 'key') ? 'contentNode' :
            (fKind === 'daterange') ? 'dateRange' :
            (fKind === 'numeric') ? 'numeric' : 'field'
        );

        return html`
            <!-- Label & System Alias Row (Forms 80%/20% locked pattern) -->
            <div class="sp-input-wrapper">
                <div class="sp-label-alias-row ${this._sidePanelErrors.alias ? 'has-error' : ''}">
                    <input type="text"
                           .value=${d.label || ''}
                           @input=${e => {
                               d.label = e.target.value;
                               if (!d._aliasUnlocked) {
                                   this._generateAliasFromLabel(d);
                               }
                               this.requestUpdate();
                           }}
                           class="sp-input"
                           placeholder="Enter a filter name (e.g. Policies, Published Year, Price)" />
                    <div class="sp-alias-cell">
                        <input type="text"
                               .value=${d.alias || ''}
                               ?readonly=${!d._aliasUnlocked}
                               @input=${e => { d.alias = e.target.value; }}
                               class="sp-alias-input ${d._aliasUnlocked ? '' : 'is-locked'}"
                               placeholder="alias"
                               title="${d._aliasUnlocked ? 'System Alias' : 'System Alias (locked — click padlock to edit)'}" />
                        <button type="button"
                                class="sp-alias-lock-btn ${d._aliasUnlocked ? 'is-unlocked' : ''}"
                                @click=${() => this._toggleAliasLock(d)}
                                title="${d._aliasUnlocked ? 'Lock alias' : 'Unlock to edit alias'}">
                            ${d._aliasUnlocked ? html`
                                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                    <rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect>
                                    <path d="M7 11V7a5 5 0 0 1 9.9-1"></path>
                                </svg>
                            ` : html`
                                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                    <rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect>
                                    <path d="M7 11V7a5 5 0 0 1 10 0v4"></path>
                                </svg>
                            `}
                        </button>
                    </div>
                </div>
                ${this._sidePanelErrors.alias ? html`<div class="sp-error-msg">${this._sidePanelErrors.alias}</div>` : nothing}
            </div>





            <!-- Unified Options Filter Builder (Forms Dropdown Options UI - Not needed for dynamic Tag/Field filters) -->
            ${filterType !== 'field' ? html`
                <div class="sp-options-container">
                    <div class="sp-options-left">
                        <span class="sp-clean-toggle-title">Options</span>
                        <span class="sp-clean-toggle-sub">Provides a list of options.</span>
                        ${filterType === 'contentType' && (this._catalog?.contentTypes || []).length > 0 ? html`
                            <button type="button"
                                    class="btn btn-secondary btn-sm"
                                    style="margin-top: 10px; font-size: 11px; padding: 4px 8px; width: 100%; border-radius: 4px;"
                                    @click=${() => {
                                        d.ranges = (this._catalog?.contentTypes || []).map(ct => ({
                                            alias: ct.alias,
                                            label: ct.name,
                                            from: ct.alias,
                                            to: ''
                                        }));
                                        this.requestUpdate();
                                    }}>
                                + Add All Types (${(this._catalog?.contentTypes || []).length})
                            </button>
                        ` : nothing}
                    </div>
                    <div class="sp-options-right">
                        <div class="sp-options-col-headers">
                            <span style="flex: 1; font-size: 11px; font-weight: 600; color: #6b7280; text-transform: uppercase; letter-spacing: 0.5px;">Label</span>
                            <span style="flex: 1; font-size: 11px; font-weight: 600; color: #6b7280; text-transform: uppercase; letter-spacing: 0.5px;">${filterType === 'contentType' ? 'Document Type' : 'Key / Alias'}</span>
                        </div>
                        ${(!d.ranges || d.ranges.length === 0) ? html`
                            <div class="sp-option-row">
                                <span class="sp-opt-reorder-handle" title="Option" style="opacity: 0;">
                                    <svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor">
                                        <circle cx="9" cy="6" r="1.5"></circle>
                                        <circle cx="15" cy="6" r="1.5"></circle>
                                        <circle cx="9" cy="12" r="1.5"></circle>
                                        <circle cx="15" cy="12" r="1.5"></circle>
                                        <circle cx="9" cy="18" r="1.5"></circle>
                                        <circle cx="15" cy="18" r="1.5"></circle>
                                    </svg>
                                </span>
                                <input type="text" placeholder="New Label" class="sp-val-input flex-1"
                                       @input=${e => {
                                           d.ranges = [{ label: e.target.value, alias: e.target.value.toLowerCase().replace(/[^a-z0-9_-]/g, '-'), from: '', to: '' }];
                                           this.requestUpdate();
                                       }} />
                                ${filterType === 'contentType' ? html`
                                    <select class="sp-select flex-1"
                                            style="height: 36px; padding: 4px 8px; font-size: 13px;"
                                            @change=${e => {
                                                const chosen = e.target.value;
                                                const matched = (this._catalog?.contentTypes || []).find(c => c.alias === chosen);
                                                d.ranges = [{
                                                    label: matched?.name || chosen,
                                                    alias: chosen,
                                                    from: chosen,
                                                    to: ''
                                                }];
                                                this.requestUpdate();
                                            }}>
                                        <option value="">-- Choose Document Type --</option>
                                        ${(this._catalog?.contentTypes || []).map(ct => html`
                                            <option value="${ct.alias}">${ct.name} (${ct.alias})</option>
                                        `)}
                                    </select>
                                ` : html`
                                    <input type="text" placeholder="New Key / Alias" class="sp-val-input flex-1" />
                                `}
                                <button class="sp-btn-icon-add" title="Add Option"
                                        @click=${() => {
                                            d.ranges = [{ label: '', alias: '', from: '', to: '' }];
                                            this.requestUpdate();
                                        }}>
                                    <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                        <line x1="12" y1="5" x2="12" y2="19"></line>
                                        <line x1="5" y1="12" x2="19" y2="12"></line>
                                    </svg>
                                </button>
                            </div>
                        ` : (d.ranges || []).map((opt, oIdx) => {
                            const isLast = oIdx === (d.ranges.length - 1);
                            return html`
                                <div style="display: flex; flex-direction: column; gap: 4px; margin-bottom: 8px;">
                                    <div class="sp-option-row">
                                        <span class="sp-opt-reorder-handle" title="Option">
                                            <svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor">
                                                <circle cx="9" cy="6" r="1.5"></circle>
                                                <circle cx="15" cy="6" r="1.5"></circle>
                                                <circle cx="9" cy="12" r="1.5"></circle>
                                                <circle cx="15" cy="12" r="1.5"></circle>
                                                <circle cx="9" cy="18" r="1.5"></circle>
                                                <circle cx="15" cy="18" r="1.5"></circle>
                                            </svg>
                                        </span>
                                        <input type="text"
                                               .value=${opt.label || ''}
                                               @input=${e => {
                                                   opt.label = e.target.value;
                                                   if (!opt.alias || opt.alias.startsWith('opt-') || opt.alias.startsWith('under-') || opt.alias.startsWith('over-') || opt.alias.startsWith('range-')) {
                                                       opt.alias = e.target.value.toLowerCase().replace(/[^a-z0-9_-]/g, '-').replace(/-+/g, '-');
                                                   }
                                                   this.requestUpdate();
                                               }}
                                               placeholder="New Label"
                                               class="sp-val-input flex-1" />
                                        ${filterType === 'contentType' ? html`
                                            <select class="sp-select flex-1"
                                                    style="height: 36px; padding: 4px 8px; font-size: 13px;"
                                                    .value=${opt.alias || opt.from || ''}
                                                    @change=${e => {
                                                        const chosen = e.target.value;
                                                        opt.alias = chosen;
                                                        opt.from = chosen;
                                                        const matched = (this._catalog?.contentTypes || []).find(c => c.alias === chosen);
                                                        if (matched && (!opt.label || opt.label === 'New Label' || opt.label === '')) {
                                                            opt.label = matched.name;
                                                        }
                                                        this.requestUpdate();
                                                    }}>
                                                <option value="">-- Choose Document Type --</option>
                                                ${(this._catalog?.contentTypes || []).map(ct => html`
                                                    <option value="${ct.alias}" ?selected=${opt.alias === ct.alias || opt.from === ct.alias}>${ct.name} (${ct.alias})</option>
                                                `)}
                                                ${opt.alias && !(this._catalog?.contentTypes || []).some(ct => ct.alias === opt.alias) ? html`
                                                    <option value="${opt.alias}" selected>${opt.alias}</option>
                                                ` : nothing}
                                            </select>
                                        ` : html`
                                            <input type="text"
                                                   .value=${opt.alias || opt.value || ''}
                                                   @input=${e => {
                                                       opt.alias = e.target.value;
                                                       opt.value = e.target.value;
                                                       this.requestUpdate();
                                                   }}
                                                   placeholder="New Key / Alias"
                                                   class="sp-val-input flex-1" />
                                        `}
                                        ${isLast ? html`
                                            <button class="sp-btn-icon-add" title="Add Option"
                                                    @click=${() => {
                                                        if (!Array.isArray(d.ranges)) d.ranges = [];
                                                        d.ranges.push({ label: '', alias: '', from: '', to: '' });
                                                        this.requestUpdate();
                                                    }}>
                                                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                                    <line x1="12" y1="5" x2="12" y2="19"></line>
                                                    <line x1="5" y1="12" x2="19" y2="12"></line>
                                                </svg>
                                            </button>
                                        ` : nothing}
                                        ${d.ranges.length > 1 ? html`
                                            <button class="btn-del-rule" title="Remove Option"
                                                    @click=${() => {
                                                        d.ranges.splice(oIdx, 1);
                                                        this.requestUpdate();
                                                    }}>
                                                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                                    <path d="M3 6h18"></path>
                                                    <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"></path>
                                                    <path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
                                                </svg>
                                            </button>
                                        ` : nothing}
                                    </div>

                                    ${filterType === 'contentNode' ? html`
                                        <div style="margin: 2px 0 6px 24px; padding: 6px 10px; background: #f8fafc; border: 1px dashed #cbd5e1; border-radius: 6px;">
                                            <label style="font-size: 11px; font-weight: 600; color: #64748b; margin-bottom: 4px; display: block;">Pick Content Page / Tree Node:</label>
                                            <umb-input-document
                                                .selection=${[opt.from].filter(Boolean)}
                                                @change=${e => {
                                                    const sel = e.target.selection || [];
                                                    opt.from = sel.length ? sel[0] : '';
                                                    if (!opt.alias) {
                                                        opt.alias = opt.from;
                                                    }
                                                    this.requestUpdate();
                                                }}>
                                            </umb-input-document>
                                        </div>
                                    ` : nothing}
                                </div>
                            `;
                        })}
                    </div>
                </div>
            ` : nothing}

            <div class="sp-toggle-row">
                <div class="sp-toggle-info">
                    <span class="sp-toggle-title">Filter Enabled</span>
                    <span class="sp-toggle-desc">Enable or disable this filter on frontend search results and debug previews.</span>
                </div>
                <label class="switch switch-sm">
                    <input type="checkbox"
                           .checked=${d.enabled !== false}
                           @change=${e => { d.enabled = e.target.checked; this.requestUpdate(); }}>
                    <span class="slider round"></span>
                </label>
            </div>

            <div class="sp-toggle-row">
                <div class="sp-toggle-info">
                    <span class="sp-toggle-title">Hide Empty Filter Options</span>
                    <span class="sp-toggle-desc">Hide options that currently match 0 results for the active query.</span>
                </div>
                <label class="switch switch-sm">
                    <input type="checkbox"
                           .checked=${d.hideEmpty !== false}
                           @change=${e => { d.hideEmpty = e.target.checked; this.requestUpdate(); }}>
                    <span class="slider round"></span>
                </label>
            </div>
        `;
    }

    _renderBestBetSidePanelBody(d) {
        return html`
            <div class="sp-group">
                <label class="sp-label">Trigger Search Queries (comma-separated) *</label>
                <input type="text"
                       class="sp-input"
                       placeholder="e.g. contact, get in touch, support"
                       .value=${d._termsInput}
                       @input=${e => { d._termsInput = e.target.value; this.requestUpdate(); }}>
                ${this._sidePanelErrors.terms ? html`<span class="sp-error">${this._sidePanelErrors.terms}</span>` : nothing}
            </div>

            <div class="sp-group">
                <label class="sp-label">Pinned Umbraco Node GUIDs (one per line) *</label>
                <textarea class="sp-textarea"
                          rows="4"
                          placeholder="e.g. 1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d"
                          .value=${d._nodeKeysInput}
                          @input=${e => { d._nodeKeysInput = e.target.value; this.requestUpdate(); }}></textarea>
                ${this._sidePanelErrors.nodeKeys ? html`<span class="sp-error">${this._sidePanelErrors.nodeKeys}</span>` : nothing}
                <small class="text-muted">These content nodes will always rank #1 for the trigger terms.</small>
            </div>
        `;
    }

    _renderSortSidePanelBody(d) {
        return html`
            <div class="sp-group">
                <label class="sp-label">Sort Field *</label>
                <input type="text"
                       class="sp-input"
                       placeholder="e.g. score, updateDate, nodeName"
                       .value=${d.field}
                       @input=${e => { d.field = e.target.value; this.requestUpdate(); }}>
                ${this._sidePanelErrors.field ? html`<span class="sp-error">${this._sidePanelErrors.field}</span>` : nothing}
            </div>

            <div class="sp-group">
                <label class="sp-label">Sort Direction</label>
                <select class="sp-select"
                        .value=${d.direction}
                        @change=${e => { d.direction = e.target.value; this.requestUpdate(); }}>
                    <option value="descending">Descending (Highest / Newest first)</option>
                    <option value="ascending">Ascending (Lowest / Oldest first)</option>
                </select>
            </div>
        `;
    }

    _renderContentTypeBoostSidePanelBody(d) {
        return html`
            <div class="sp-group">
                <label class="sp-label">Document Type Alias *</label>
                <select class="sp-select"
                        .value=${d.contentType}
                        @change=${e => { d.contentType = e.target.value; this.requestUpdate(); }}>
                    <option value="">Select Document Type...</option>
                    ${(this._catalog.contentTypes || []).map(ct => html`
                        <option value="${ct.alias}">${ct.name} (${ct.alias})</option>
                    `)}
                </select>
                ${this._sidePanelErrors.contentType ? html`<span class="sp-error">${this._sidePanelErrors.contentType}</span>` : nothing}
            </div>

            <div class="sp-group">
                <label class="sp-label">Boost Multiplier</label>
                <input type="number"
                       class="sp-input"
                       step="0.1"
                       min="0.1"
                       max="20.0"
                       .value=${String(d.boost)}
                       @input=${e => { d.boost = parseFloat(e.target.value); this.requestUpdate(); }}>
                ${this._sidePanelErrors.boost ? html`<span class="sp-error">${this._sidePanelErrors.boost}</span>` : nothing}
            </div>
        `;
    }

    _renderSynonymSidePanelBody(d) {
        return html`
            <div class="sp-group">
                <label class="sp-label">Source Search Word *</label>
                <input type="text"
                       class="sp-input"
                       placeholder="e.g. sofa"
                       .value=${d.term}
                       @input=${e => { d.term = e.target.value; this.requestUpdate(); }}>
                ${this._sidePanelErrors.term ? html`<span class="sp-error">${this._sidePanelErrors.term}</span>` : nothing}
            </div>

            <div class="sp-group">
                <label class="sp-label">Synonym Expansions (comma-separated) *</label>
                <input type="text"
                       class="sp-input"
                       placeholder="e.g. couch, settee, lounge"
                       .value=${d._synonymsInput}
                       @input=${e => { d._synonymsInput = e.target.value; this.requestUpdate(); }}>
                ${this._sidePanelErrors.synonyms ? html`<span class="sp-error">${this._sidePanelErrors.synonyms}</span>` : nothing}
            </div>
        `;
    }

    _renderProfileMetadataSidePanelBody(d) {
        return html`
            <div class="sp-group">
                <label class="sp-label">Profile Name *</label>
                <input type="text"
                       class="sp-input"
                       .value=${d.name}
                       @input=${e => { d.name = e.target.value; this.requestUpdate(); }}>
                ${this._sidePanelErrors.name ? html`<span class="sp-error">${this._sidePanelErrors.name}</span>` : nothing}
            </div>

            <div class="sp-group">
                <label class="sp-label">Profile Alias (Code Identifier) *</label>
                <input type="text"
                       class="sp-input"
                       .value=${d.alias}
                       @input=${e => { d.alias = e.target.value; this.requestUpdate(); }}>
                ${this._sidePanelErrors.alias ? html`<span class="sp-error">${this._sidePanelErrors.alias}</span>` : nothing}
            </div>

            <div class="sp-group">
                <label class="toggle-item">
                    <div class="toggle-info">
                        <strong>Profile Enabled</strong>
                        <span>When disabled, searching against this profile returns empty results.</span>
                    </div>
                    <input type="checkbox"
                           class="switch-input"
                           .checked=${d.enabled}
                           @change=${e => { d.enabled = e.target.checked; this.requestUpdate(); }}>
                </label>
            </div>
        `;
    }

    // ----------------- MODALS & TOASTS -----------------

    _renderMessageBox() {
        return html`
            <div class="modal-overlay" @click=${this._handleMessageBoxCancel}>
                <div class="modal-card" @click=${e => e.stopPropagation()}>
                    <div class="modal-header">
                        <h4>${this._messageBoxTitle}</h4>
                        <button class="sp-close" @click=${this._handleMessageBoxCancel}>×</button>
                    </div>
                    <div class="modal-body">
                        <p>${this._messageBoxMessage}</p>
                    </div>
                    <div class="modal-footer">
                        ${this._messageBoxType === 'confirm' ? html`
                            <button class="btn btn-secondary" @click=${this._handleMessageBoxCancel}>${this._messageBoxCancelText}</button>
                        ` : nothing}
                        <button class="btn btn-primary" @click=${this._handleMessageBoxConfirm}>${this._messageBoxConfirmText}</button>
                    </div>
                </div>
            </div>
        `;
    }

    _renderImportModal() {
        return html`
            <div class="modal-overlay" @click=${() => { this._showImportModal = false; this.requestUpdate(); }}>
                <div class="modal-card" style="max-width: 650px;" @click=${e => e.stopPropagation()}>
                    <div class="modal-header">
                        <h4>Import Search Profiles (JSON)</h4>
                        <button class="sp-close" @click=${() => { this._showImportModal = false; this.requestUpdate(); }}>×</button>
                    </div>
                    <div class="modal-body">
                        <p>Paste the exported profile JSON payload below:</p>
                        <textarea class="sp-textarea"
                                  rows="8"
                                  placeholder='[ { "name": "...", "alias": "...", "rules": { ... } } ]'
                                  .value=${this._importJsonText}
                                  @input=${e => { this._importJsonText = e.target.value; this.requestUpdate(); }}></textarea>
                    </div>
                    <div class="modal-footer">
                        <button class="btn btn-secondary" @click=${() => { this._showImportModal = false; this.requestUpdate(); }}>Cancel</button>
                        <button class="btn btn-primary" @click=${this._handleImportSubmit}>Import Profiles</button>
                    </div>
                </div>
            </div>
        `;
    }

    _renderToasts() {
        if (!this._toasts.length) return nothing;
        return html`
            <div class="toast-stack">
                ${this._toasts.map(t => html`
                    <div class="toast toast-${t.type}">
                        <div class="toast-icon">
                            ${t.type === 'success' ? '✓' : (t.type === 'error' ? '✕' : 'ℹ')}
                        </div>
                        <span class="toast-message">${t.message}</span>
                        <button class="toast-close" @click=${() => this._dismissToast(t.id)}>×</button>
                    </div>
                `)}
            </div>
        `;
    }

    // ----------------- STYLES -----------------

    static styles = css`
        :host {
            display: block;
            height: 100%;
            width: 100%;
            --primary: #000000;
            --primary-bg: #f1f1f1;
            --surface: #ffffff;
            --surface-hover: #f3f4f6;
            --surface-elevated: #ececec;
            --border: #d8d8d8;
            --border-light: #e9e9e9;
            --border-dark: #b5b5b5;
            --text-primary: #374151;
            --text-secondary: #4b5563;
            --text-tertiary: #6b7280;
            --success: #10b981;
            --error: #ef4444;
            --warning: #f59e0b;
            --info: #000000;
            --radius-sm: 4px;
            --radius-md: 6px;
            --radius-lg: 8px;
            --radius-xl: 12px;
            --shadow-sm: 0 1px 2px 0 rgba(0, 0, 0, 0.05);
            --shadow-md: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
            --shadow-lg: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
            --transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
        }

        /* SVG Mask Icons */
        [class^="icon-"], [class*=" icon-"],
        i[class^="icon-"], i[class*=" icon-"] {
            display: inline-block;
            width: 16px;
            height: 16px;
            background-color: currentColor;
            -webkit-mask-size: contain;
            mask-size: contain;
            -webkit-mask-repeat: no-repeat;
            mask-repeat: no-repeat;
            -webkit-mask-position: center;
            mask-position: center;
            vertical-align: middle;
            font-style: normal;
        }

        .icon-trash { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10 11v6"/><path d="M14 11v6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/><path d="M3 6h18"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>'); }
        .icon-edit { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.375 2.625a1 1 0 0 1 3 3l-9.013 9.014a2 2 0 0 1-.853.505l-2.873.84a.5.5 0 0 1-.62-.62l.84-2.873a2 2 0 0 1 .506-.852z"/></svg>'); }
        .icon-search { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>'); }
        .icon-settings, .icon-sliders { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="4" x2="20" y1="21" y2="21"/><line x1="4" x2="20" y1="14" y2="14"/><line x1="4" x2="20" y1="7" y2="7"/><circle cx="14" cy="21" r="2"/><circle cx="8" cy="14" r="2"/><circle cx="16" cy="7" r="2"/></svg>'); }
        .icon-server { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="20" height="8" x="2" y="2" rx="2" ry="2"/><rect width="20" height="8" x="2" y="14" rx="2" ry="2"/><line x1="6" x2="6.01" y1="6" y2="6"/><line x1="6" x2="6.01" y1="18" y2="18"/></svg>'); }
        .icon-chart-curve { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 3v18h18"/><path d="m19 9-5 5-4-4-3 3"/></svg>'); }
        .icon-document { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/></svg>'); }
        .icon-star { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>'); }
        .icon-copy { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg>'); }
        .icon-add { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14"/><path d="M12 5v14"/></svg>'); }
        .icon-arrow-left { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m12 19-7-7 7-7"/><path d="M19 12H5"/></svg>'); }
        .icon-chevron-right { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"/></svg>'); }
        .icon-download { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" x2="12" y1="15" y2="3"/></svg>'); }
        .icon-cloud-upload { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 14.899A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 2.5 8.242"/><path d="M12 12v9"/><path d="m16 16-4-4-4 4"/></svg>'); }
        .icon-check { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>'); }
        .icon-refresh { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16"/><path d="M8 16H3v5"/></svg>'); }
        .icon-filter { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"/></svg>'); }
        .icon-play { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="6 3 20 12 6 21 6 3"/></svg>'); }
        .icon-info { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>'); }
        .icon-delete { -webkit-mask-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>'); }

        /* Dashboard Outer Frame */
        .umb-custom-dashboard {
            display: flex;
            background: #ffffff;
            height: 100%;
            width: 100%;
            font-family: 'Segoe UI', system-ui, -apple-system, BlinkMacSystemFont, Roboto, sans-serif;
            overflow: hidden;
            box-sizing: border-box;
        }

        /* Sidebar */
        .sidebar {
            width: 180px;
            background: #ffffff;
            flex-shrink: 0;
            display: flex;
            flex-direction: column;
            border-right: 1px solid var(--border-light);
            height: 100%;
            overflow-y: auto;
            box-sizing: border-box;
        }

        .sidebar-header {
            padding: 18px 20px;
            font-size: 15px;
            font-weight: 800;
            color: var(--primary);
            border-bottom: 1px solid var(--border-light);
            letter-spacing: -0.01em;
        }

        .nav-item {
            padding: 12px 20px;
            cursor: pointer;
            display: flex;
            gap: 10px;
            align-items: center;
            color: var(--text-primary);
            font-size: 13.5px;
            font-weight: 500;
            transition: var(--transition);
        }

        .nav-item:hover {
            background: var(--surface-hover);
            color: var(--primary);
        }

        .nav-item.active {
            border-left: 4px solid var(--primary);
            font-weight: 700;
            color: var(--primary);
            background: var(--primary-bg);
        }

        .chevron-icon {
            margin-left: auto;
            font-size: 12px;
            transition: transform 0.2s ease;
            color: var(--text-tertiary);
        }

        .chevron-rotated { transform: rotate(90deg); color: var(--primary); }

        .sub-nav {
            background: #fafafa;
            border-bottom: 1px solid var(--border-light);
            max-height: 320px;
            overflow-y: auto;
        }

        .sub-nav-item {
            padding: 9px 16px 9px 36px;
            font-size: 12.5px;
            color: var(--text-secondary);
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 8px;
            transition: var(--transition);
        }

        .sub-nav-item:hover {
            background: var(--primary-bg);
            color: var(--primary);
        }

        .sub-nav-item.selected {
            color: var(--primary);
            font-weight: 700;
            background: var(--surface-elevated);
        }

        .sub-nav-item.empty {
            opacity: 0.6;
            cursor: default;
            font-style: italic;
        }

        .badge-mini {
            font-size: 11px;
            color: var(--primary);
            background: var(--border-light);
            padding: 2px 6px;
            border-radius: 4px;
            font-weight: 600;
        }

        /* Content Viewport */
        .content {
            flex: 1;
            display: flex;
            flex-direction: column;
            overflow: hidden;
            min-width: 0;
            width: 100%;
        }

        .content-header { 
            display: flex; 
            align-items: stretch; 
            justify-content: space-between; 
            height: 64px;
            padding: 0 0 0 24px; 
            border-bottom: 1px solid var(--border-light); 
            background: #ffffff; 
            box-sizing: border-box;
            flex-shrink: 0;
        }

        .topbar-left { 
            display: flex; 
            align-items: center; 
            gap: 16px; 
            flex: 1; 
            min-width: 0;
            padding: 12px 16px 12px 0;
        }

        .btn-back { 
            background: #ffffff !important; 
            border: 1px solid #cbd5e1 !important; 
            border-radius: 50% !important; 
            width: 36px !important; 
            height: 36px !important; 
            display: flex !important; 
            align-items: center !important; 
            justify-content: center !important; 
            cursor: pointer !important; 
            color: #0f172a !important; 
            transition: var(--transition) !important; 
            box-shadow: 0 1px 2px rgba(0,0,0,0.05) !important;
            flex-shrink: 0 !important;
        }
        .btn-back:hover { 
            background: #f8fafc !important; 
            border-color: #94a3b8 !important; 
        }

        .divider-v { 
            width: 1px; 
            height: 24px; 
            background: var(--border-light); 
        }

        /* Large Header Title Input matching forms package */
        .input-form-title { 
            border: 1px solid #e2e8f0; 
            background: #ffffff; 
            font-size: 22px; 
            font-weight: 800; 
            outline: none; 
            color: var(--text-primary); 
            padding: 1px 12px; 
            border-radius: 4px; 
            min-width: 200px; 
            flex: 1; 
            margin: 0 12px 0 0 !important; 
            letter-spacing: -0.02em;
            white-space: nowrap;  
            overflow: hidden;
            text-overflow: ellipsis; 
            box-sizing: border-box;
            transition: border-color 0.2s ease, box-shadow 0.2s ease;
        }
        
        .input-form-title:hover, .input-form-title:focus {
            border-color: #94a3b8;
            box-shadow: 0 0 0 1px #94a3b8;
        }

        .input-form-title::placeholder { color: var(--text-tertiary); opacity: 0.5; }

        .form-title-wrap {
            display: flex;
            flex-direction: column;
            flex: 1;
            gap: 4px;
        }

        .form-title-wrap .input-form-title {
            margin: 0 !important;
            width: 99%;
        }

        .form-title-error-msg {
            color: var(--error);
            font-size: 12px;
            font-weight: 600;
            padding: 4px 0;
            align-self: flex-start;
        }

        .header-actions { 
            display: flex; 
            align-items: stretch; 
            height: 100%;
            gap: 0; 
            flex-shrink: 0;
        }

        .header-actions .btn {
            display: flex !important;
            flex-direction: column !important;
            align-items: center !important;
            justify-content: center !important;
            gap: 5px !important;
            height: 100% !important;
            width: 80px !important;
            min-width: 80px !important;
            padding: 0 6px !important;
            border: none !important;
            border-left: 1px solid var(--border-light) !important;
            border-radius: 0 !important;
            background: transparent !important;
            color: var(--text-secondary) !important;
            font-size: 11px !important;
            font-weight: 600 !important;
            cursor: pointer !important;
            transition: background 0.15s, color 0.15s !important;
            user-select: none !important;
            box-sizing: border-box !important;
        }

        .header-actions .btn:hover {
            background: #f5f5f5 !important;
            color: #000000 !important;
        }

        .header-actions .btn.active {
            background: #e5e7eb !important;
            color: #000000 !important;
            font-weight: 700 !important;
        }

        .header-actions .btn svg {
            width: 18px !important;
            height: 18px !important;
            flex-shrink: 0 !important;
            color: currentColor !important;
        }

        .header-actions .btn span {
            font-size: 11.5px !important;
            line-height: 1 !important;
            letter-spacing: -0.01em !important;
            color: inherit !important;
        }

        .header-select {
            padding: 6px 12px;
            border: 1px solid var(--border);
            border-radius: var(--radius-sm);
            font-size: 13px;
            background: #ffffff;
            outline: none;
        }

        .view-container {
            flex: 1;
            overflow-y: auto;
            padding: 24px 28px;
            background: #fbfbfb;
            box-sizing: border-box;
        }

        /* Builder Footer */
        .builder-footer {
            position: sticky;
            bottom: 0;
            z-index: 900;
            display: flex;
            justify-content: space-between;
            align-items: stretch;
            height: 60px;
            flex-shrink: 0;
            background: #ffffff;
            border: none;
            border-top: 1px solid var(--border-light);
            border-radius: 0;
            padding: 0 24px;
            margin: 0;
            box-shadow: 0 -2px 8px rgba(0, 0, 0, 0.04);
            box-sizing: border-box;
        }

        .builder-footer .footer-left {
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .builder-footer .footer-form-label {
            font-size: 11px;
            font-weight: 800;
            color: #94a3b8;
            letter-spacing: 0.05em;
            text-transform: uppercase;
        }

        .builder-footer .footer-divider {
            color: #cbd5e1;
            font-weight: 300;
        }

        .builder-footer .footer-form-name {
            font-size: 13px;
            font-weight: 600;
            color: #374151;
        }

        .builder-footer .footer-right {
            display: flex;
            align-items: stretch;
            gap: 0;
            height: 100%;
        }

        .builder-footer .header-divider {
            width: 1px;
            background: #e0e0e0;
            align-self: stretch;
        }

        .builder-footer .footer-btn {
            background: transparent !important;
            border: none !important;
            padding: 0 20px !important;
            height: 100% !important;
            display: flex !important;
            align-items: center !important;
            justify-content: center !important;
            font-size: 13px !important;
            font-weight: 600 !important;
            color: #000000 !important;
            cursor: pointer !important;
            transition: background 0.15s !important;
            user-select: none !important;
            border-radius: 0 !important;
        }

        .builder-footer .footer-btn:hover {
            background: #f5f5f5 !important;
        }

        .builder-footer .footer-btn.active {
            background: #e5e7eb !important;
        }

        .builder-footer .footer-btn:disabled {
            cursor: not-allowed !important;
            opacity: 0.45 !important;
        }

        .builder-footer .footer-btn:disabled:hover {
            background: transparent !important;
        }

        .builder-footer .footer-save-btn {
            color: #2ea44f !important;
            font-size: 13px !important;
            font-weight: 600 !important;
        }

        .builder-footer .footer-discard-btn {
            color: #dc3545 !important;
            font-size: 13px !important;
            font-weight: 600 !important;
        }

        .builder-footer .footer-add-btn {
            color: #1b264f !important;
            font-size: 13px !important;
            font-weight: 600 !important;
        }

        /* Buttons matching UmbracoFormsPackage */
        .btn { 
            display: inline-flex; 
            align-items: center; 
            justify-content: center; 
            gap: 8px; 
            height: 36px;
            padding: 0 14px; 
            box-sizing: border-box;
            border-radius: var(--radius-md); 
            font-weight: 600; 
            font-size: 13px; 
            cursor: pointer; 
            transition: var(--transition); 
            border: 1px solid transparent; 
            outline: none; 
            white-space: nowrap; 
        }

        .btn-primary { 
            background: var(--primary); 
            color: white; 
            border-color: transparent;
        }
        .btn-primary:hover { 
            background: #000000; 
        }

        .btn-secondary { 
            background: var(--surface); 
            color: var(--text-primary); 
            border-color: var(--border); 
        }
        .btn-secondary:hover { 
            background: var(--surface-hover); 
            border-color: var(--primary); 
            color: var(--primary);
        }

        .btn-back { 
            background: #ffffff !important; 
            border: 1px solid #cbd5e1 !important; 
            border-radius: 50% !important; 
            width: 36px !important; 
            height: 36px !important; 
            display: flex !important; 
            align-items: center !important; 
            justify-content: center !important; 
            cursor: pointer !important; 
            color: #0f172a !important; 
            transition: var(--transition) !important; 
            box-shadow: 0 1px 2px rgba(0,0,0,0.05) !important;
            flex-shrink: 0 !important;
        }
        .btn-back:hover {
            background: #f8fafc !important;
            border-color: #94a3b8 !important;
        }

        .btn-danger {
            color: var(--error);
        }

        .btn-danger:hover {
            background: #fef2f2;
            border-color: var(--error);
        }

        .btn-sm {
            padding: 5px 10px;
            font-size: 12px;
        }

        .btn-icon {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 30px;
            height: 30px;
            padding: 0;
            border-radius: var(--radius-sm);
            background: transparent;
            border: 1px solid transparent;
            cursor: pointer;
            color: var(--text-secondary);
            transition: var(--transition);
        }

        .btn-icon:hover {
            background: var(--surface-hover);
            color: var(--primary);
            border-color: var(--border-light);
        }

        .btn-icon-danger:hover {
            background: #fef2f2;
            color: var(--error);
            border-color: #fca5a5;
        }

        .btn-back {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 32px;
            height: 32px;
            background: #ffffff;
            border: 1px solid var(--border);
            border-radius: var(--radius-sm);
            cursor: pointer;
            color: var(--text-primary);
            transition: var(--transition);
        }

        .btn-back:hover {
            background: var(--primary-bg);
            color: var(--primary);
        }

        /* Profiles List */
        .list-controls-bar {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 16px;
            gap: 16px;
        }

        .search-box-wrap {
            position: relative;
            flex: 1;
            max-width: 450px;
        }

        .search-box-icon {
            position: absolute;
            left: 12px;
            top: 50%;
            transform: translateY(-50%);
            color: var(--text-tertiary);
        }

        .search-box-input {
            width: 100%;
            padding: 8px 12px 8px 36px;
            border: 1px solid var(--border);
            border-radius: var(--radius-sm);
            font-size: 13.5px;
            background: #ffffff;
            outline: none;
            box-sizing: border-box;
        }

        .search-box-input:focus {
            border-color: var(--primary);
        }

        .list-stats {
            font-size: 13px;
            color: var(--text-secondary);
        }

        /* Cards & Tables */
        .table-card {
            background: #ffffff;
            border: 1px solid var(--border);
            border-radius: var(--radius-md);
            overflow: hidden;
            box-shadow: var(--shadow-sm);
        }

        .data-table {
            width: 100%;
            border-collapse: collapse;
            font-size: 13px;
            text-align: left;
        }

        .data-table th {
            padding: 11px 16px;
            background: #fafafa;
            color: var(--text-secondary);
            font-weight: 600;
            border-bottom: 1px solid var(--border);
        }

        .data-table td {
            padding: 12px 16px;
            border-bottom: 1px solid var(--border-light);
            color: var(--text-primary);
            vertical-align: middle;
        }

        .table-row-clickable {
            cursor: pointer;
            transition: background 0.15s ease;
        }

        .table-row-clickable:hover {
            background: #f9fafb;
        }

        .profile-name-cell {
            display: flex;
            align-items: center;
            gap: 8px;
            flex-wrap: wrap;
        }

        .profile-title {
            font-weight: 700;
            color: var(--text-primary);
        }

        .profile-alias-code {
            font-size: 12px;
            color: var(--text-tertiary);
            font-family: monospace;
            background: var(--surface-hover);
            padding: 2px 5px;
            border-radius: 3px;
        }

        .status-cell {
            display: flex;
            align-items: center;
            gap: 6px;
            font-size: 12.5px;
        }

        .status-dot {
            width: 8px;
            height: 8px;
            border-radius: 50%;
        }

        .status-active { background: var(--success); }
        .status-disabled { background: var(--text-tertiary); }

        .summary-pills {
            display: flex;
            gap: 6px;
            flex-wrap: wrap;
        }

        .summary-pill {
            font-size: 11.5px;
            background: var(--surface-hover);
            border: 1px solid var(--border-light);
            padding: 2px 7px;
            border-radius: 12px;
            color: var(--text-secondary);
        }

        .row-actions {
            display: inline-flex;
            align-items: center;
            gap: 4px;
        }

        /* Badges */
        .badge {
            display: inline-block;
            padding: 2px 8px;
            font-size: 11px;
            font-weight: 600;
            border-radius: 10px;
            text-transform: uppercase;
            letter-spacing: 0.02em;
        }

        .badge-default { background: #000000; color: #ffffff; }
        .badge-success { background: #dcfce7; color: #15803d; }
        .badge-danger { background: #fee2e2; color: #b91c1c; }
        .badge-info { background: #e0f2fe; color: #0369a1; }

        /* Profile Editor Sub-Tabs */
        .editor-tabs-bar {
            display: flex;
            background: #ffffff;
            border: 1px solid var(--border);
            border-radius: var(--radius-md);
            margin-bottom: 20px;
            padding: 4px;
            gap: 4px;
            overflow-x: auto;
        }

        .editor-tab {
            flex: 1;
            padding: 9px 16px;
            background: transparent;
            border: none;
            font-size: 13px;
            font-weight: 600;
            color: var(--text-secondary);
            border-radius: var(--radius-sm);
            cursor: pointer;
            transition: var(--transition);
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 8px;
            white-space: nowrap;
        }

        .editor-tab:hover {
            background: var(--surface-hover);
            color: var(--primary);
        }

        .editor-tab.active {
            background: var(--primary);
            color: #ffffff;
        }

        .editor-tab.active i {
            color: #ffffff;
        }

        /* Rule Section Grid & Cards */
        .source-settings-container {
            display: flex;
            flex-direction: column;
            gap: 20px;
            width: 100%;
            max-width: 100%;
            box-sizing: border-box;
        }

        .mf-field { 
            display: flex;
            gap: 24px;
            align-items: stretch;
            background: #ffffff !important; 
            border: none !important; 
            border-radius: 0 !important; 
            margin: 0; 
            padding: 0 !important; 
            cursor: default !important; 
            position: relative;
            width: 100%;
            box-sizing: border-box;
        }

        .field-left-info {
            width: 25%;
            min-width: 25%;
            max-width: 25%;
            display: flex;
            flex-direction: column;
            justify-content: flex-start;
            padding: 10px 0;
            box-sizing: border-box;
        }

        .setting-title {
            font-size: 14px;
            font-weight: 700;
            color: #1f2937;
            letter-spacing: -0.01em;
        }

        .setting-desc {
            font-size: 12px;
            color: #6b7280;
            font-weight: 500;
            margin-top: 5px;
            line-height: 1.45;
        }

        .field-right-box {
            width: 75%;
            min-width: 75%;
            max-width: 75%;
            flex: 1;
            background: #f8fafc !important;
            border: 1px solid #e2e8f0 !important;
            border-radius: 6px !important;
            padding: 14px 18px;
            display: flex;
            flex-direction: column;
            justify-content: space-between;
            min-height: 74px;
            box-sizing: border-box;
            transition: border-color 0.15s ease, background 0.15s ease, box-shadow 0.15s ease;
        }

        .clickable-box {
            cursor: pointer;
        }

        .clickable-box:hover {
            border-color: #000000 !important;
            background: #f1f5f9 !important;
            box-shadow: 0 1px 4px rgba(0, 0, 0, 0.06);
        }

        .field-box-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 8px;
        }

        .field-type-tag {
            font-size: 11px;
            font-weight: 800;
            color: #4b5563;
            letter-spacing: 0.05em;
            text-transform: uppercase;
        }

        .field-count-pill {
            font-size: 11.5px;
            font-weight: 600;
            color: #374151;
            background: #e5e7eb;
            padding: 2px 8px;
            border-radius: 4px;
        }

        .field-box-content {
            display: flex;
            align-items: center;
            flex: 1;
        }

        .selected-chips-wrap {
            display: flex;
            flex-wrap: wrap;
            gap: 6px;
            align-items: center;
        }

        .selected-chip {
            display: inline-flex;
            align-items: center;
            gap: 6px;
            background: #ffffff;
            border: 1px solid #d1d5db;
            padding: 4px 10px;
            border-radius: 4px;
            font-size: 12.5px;
            color: #111827;
            font-weight: 500;
        }

        .selected-chip svg {
            color: #4b5563;
        }

        .selected-chip code {
            font-size: 12px;
            font-weight: 600;
            color: #111827;
        }

        .selected-chip.chip-danger {
            border-color: #fca5a5;
            background: #fef2f2;
            color: #b91c1c;
        }

        .selected-chip.chip-success {
            border-color: #86efac;
            background: #f0fdf4;
            color: #166534;
        }

        .selected-chip.chip-muted {
            border-color: #e5e7eb;
            background: #f9fafb;
            color: #6b7280;
        }

        .selected-placeholder {
            display: flex;
            flex-direction: column;
            gap: 2px;
        }

        .placeholder-tag {
            font-size: 13px;
            font-weight: 600;
            color: #1f2937;
        }

        .placeholder-meta {
            font-size: 11.5px;
            color: #6b7280;
        }

        .rule-section-grid {
            display: flex;
            flex-direction: column;
            gap: 20px;
        }

        .card {
            background: #ffffff;
            border: 1px solid var(--border);
            border-radius: var(--radius-md);
            box-shadow: var(--shadow-sm);
            overflow: hidden;
        }

        .card-header {
            padding: 16px 20px;
            border-bottom: 1px solid var(--border-light);
            background: #ffffff;
        }

        .card-header h4 {
            margin: 0;
            font-size: 15px;
            font-weight: 700;
            color: var(--text-primary);
        }

        .card-subtitle {
            margin: 4px 0 0 0;
            font-size: 12.5px;
            color: var(--text-tertiary);
        }

        .card-body {
            padding: 20px;
        }

        .card-body.no-padding {
            padding: 0;
        }

        .card-empty-pad {
            padding: 20px;
        }

        .flex-between {
            display: flex;
            justify-content: space-between;
            align-items: center;
        }

        /* Chips & Checkbox grids */
        .checkbox-chips-grid {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
            gap: 10px;
        }

        .chip-checkbox {
            display: flex;
            align-items: center;
            gap: 10px;
            padding: 10px 14px;
            border: 1px solid var(--border);
            border-radius: var(--radius-sm);
            cursor: pointer;
            background: #ffffff;
            transition: var(--transition);
        }

        .chip-checkbox:hover {
            border-color: var(--primary);
            background: var(--surface-hover);
        }

        .chip-checked {
            border-color: var(--primary);
            background: var(--primary-bg);
        }

        .chip-content {
            display: flex;
            flex-direction: column;
        }

        .chip-meta {
            font-size: 11.5px;
            color: var(--text-tertiary);
        }

        /* Form Inputs & Toggles */
        .form-row-2col {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 16px;
        }

        .form-row-3col {
            display: grid;
            grid-template-columns: 1fr 1fr 1fr;
            gap: 16px;
        }

        .form-group {
            display: flex;
            flex-direction: column;
            gap: 6px;
        }

        .sp-label {
            font-size: 12.5px;
            font-weight: 600;
            color: var(--text-secondary);
        }

        .sp-input, .sp-select, .sp-textarea {
            width: 100%;
            padding: 8px 12px;
            border: 1px solid var(--border);
            border-radius: var(--radius-sm);
            font-size: 13px;
            background: #ffffff;
            box-sizing: border-box;
            outline: none;
            transition: border-color 0.15s ease;
        }

        .sp-input:focus, .sp-select:focus, .sp-textarea:focus {
            border-color: var(--primary);
        }

        .sp-select-sm {
            padding: 5px 8px;
            font-size: 12px;
            border: 1px solid var(--border);
            border-radius: var(--radius-sm);
            background: #ffffff;
        }

        .sp-error {
            font-size: 12px;
            color: var(--error);
            font-weight: 500;
        }

        .toggle-list {
            display: flex;
            flex-direction: column;
            gap: 12px;
        }

        .toggle-item {
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 12px 16px;
            background: #fafafa;
            border: 1px solid var(--border-light);
            border-radius: var(--radius-sm);
            cursor: pointer;
        }

        .toggle-info {
            display: flex;
            flex-direction: column;
            gap: 2px;
        }

        .toggle-info strong {
            font-size: 13px;
            color: var(--text-primary);
        }

        .toggle-info span {
            font-size: 12px;
            color: var(--text-tertiary);
        }

        .switch-input {
            width: 18px;
            height: 18px;
            accent-color: var(--primary);
            cursor: pointer;
        }

        /* Tags & Badges */
        .tags-container {
            display: flex;
            flex-wrap: wrap;
            gap: 6px;
            margin-top: 6px;
        }

        .tag-badge {
            display: inline-flex;
            align-items: center;
            gap: 6px;
            padding: 4px 8px;
            background: var(--surface-hover);
            border: 1px solid var(--border);
            border-radius: 4px;
            font-size: 12px;
            font-weight: 500;
        }

        .tag-badge button {
            background: transparent;
            border: none;
            color: var(--text-tertiary);
            cursor: pointer;
            font-size: 14px;
            padding: 0;
            line-height: 1;
        }

        .tag-badge button:hover {
            color: var(--error);
        }

        .tag-include { background: #f0fdf4; border-color: #bbf7d0; color: #166534; }
        .tag-exclude { background: #fef2f2; border-color: #fecaca; color: #991b1b; }
        .tag-boost { background: #f0f9ff; border-color: #bae6fd; color: #075985; }

        .boost-tag {
            font-weight: 700;
            color: #0369a1;
            background: #e0f2fe;
            padding: 2px 6px;
            border-radius: 4px;
            font-size: 12px;
        }

        .info-callout {
            display: flex;
            align-items: center;
            gap: 8px;
            padding: 10px 14px;
            background: var(--primary-bg);
            border: 1px solid var(--border);
            border-radius: var(--radius-sm);
            font-size: 13px;
            color: var(--text-secondary);
        }

        .synonyms-list {
            display: flex;
            flex-direction: column;
            gap: 6px;
            margin-top: 6px;
        }

        .synonym-item {
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 6px 10px;
            background: var(--surface-hover);
            border-radius: var(--radius-sm);
            font-size: 12.5px;
        }

        /* Side Panel Slide-Over */
        /* Side Panel Slide-Over & Drawer */
        .side-panel-overlay {
            position: fixed;
            top: 58px;
            left: 0;
            width: 100%;
            height: calc(100% - 58px);
            background: rgba(15, 23, 42, 0.4);
            backdrop-filter: blur(4px);
            z-index: 200000;
            display: flex;
            justify-content: flex-end;
            opacity: 1;
            animation: fadeInOverlay 0.3s forwards;
        }

        .side-panel-wrapper {
            width: 680px;
            max-width: 92vw;
            background: var(--surface);
            height: 100%;
            box-shadow: -10px 0 50px rgba(0, 0, 0, 0.15);
            display: flex;
            flex-direction: column;
            transform: translateX(0);
            animation: slideInPanel 0.35s cubic-bezier(0.16, 1, 0.3, 1) forwards;
            border-left: 1px solid var(--border-light);
            box-sizing: border-box;
            position: relative;
        }

        @keyframes fadeInOverlay {
            from { opacity: 0; }
            to { opacity: 1; }
        }

        @keyframes slideInPanel {
            from { transform: translateX(100%); }
            to { transform: translateX(0); }
        }

        .sp-header {
            padding: 18px 24px;
            background: var(--primary);
            color: #ffffff;
        }

        .sp-header-content {
            display: flex;
            justify-content: space-between;
            align-items: center;
        }

        .sp-title {
            font-size: 16px;
            font-weight: 700;
        }

        .sp-close {
            background: transparent;
            border: none;
            color: #ffffff;
            font-size: 24px;
            cursor: pointer;
            line-height: 1;
            padding: 0;
        }

        .sp-body {
            flex: 1;
            padding: 24px;
            overflow-y: auto;
            display: flex;
            flex-direction: column;
            gap: 18px;
            height: 100%;
            box-sizing: border-box;
        }

        .sp-group {
            display: flex;
            flex-direction: column;
            gap: 6px;
        }

        .sp-input-wrapper {
            display: flex;
            flex-direction: column;
            gap: 4px;
            width: 100%;
        }

        .sp-error-msg {
            color: var(--error);
            font-size: 12px;
            font-weight: 600;
            margin-top: 4px;
        }

        /* 80% Label + 20% System Alias Row */
        .sp-label-alias-row {
            display: flex;
            width: 100%;
            height: 48px;
            border: 1px solid #d1d5db;
            border-radius: var(--radius-md);
            overflow: hidden;
            align-items: stretch;
            box-sizing: border-box;
            background: #ffffff;
            transition: var(--transition);
        }

        .sp-label-alias-row.has-error {
            border-color: var(--error);
        }

        .sp-label-alias-row .sp-input { 
            flex: 0 0 80%;
            width: 80%;
            background: #ffffff;
            border: none;
            border-right: 1px solid #d1d5db;
            border-radius: 0;
            padding: 7px 14px;
            font-size: 13.5px;
            font-weight: 600;
            color: #1f2937;
            box-sizing: border-box;
        }

        .sp-label-alias-row .sp-input:focus {
            background: #ffffff;
            border-color: transparent;
            box-shadow: none;
            outline: none;
        }

        .sp-alias-cell {
            flex: 0 0 20%;
            width: 20%;
            display: flex;
            align-items: stretch;
            background: #f3f4f6;
            box-sizing: border-box;
            min-width: 0;
        }

        .sp-alias-input {
            flex: 1 1 auto;
            min-width: 0;
            padding: 7px 4px 7px 10px;
            border: none;
            border-radius: 0;
            font-size: 12px;
            color: var(--text-primary);
            background: transparent;
            box-sizing: border-box;
        }

        .sp-alias-input::placeholder { color: var(--text-tertiary); font-style: italic; }

        .sp-alias-input:focus {
            background: #e5e7eb;
            border-color: transparent;
            box-shadow: none;
            outline: none;
        }

        .sp-alias-input.is-locked {
            color: var(--text-tertiary);
            cursor: not-allowed;
        }

        .sp-alias-lock-btn {
            flex: 0 0 auto;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 28px;
            padding: 0;
            border: none;
            background: transparent;
            color: var(--text-tertiary);
            cursor: pointer;
            transition: var(--transition);
        }

        .sp-alias-lock-btn:hover { color: #000000; }
        .sp-alias-lock-btn.is-unlocked { color: #000000; }
        .sp-alias-lock-btn i { font-size: 13px; }

        .sp-floating-label { 
            font-size: 12px; 
            font-weight: 700; 
            color: var(--text-secondary); 
            margin-bottom: 6px; 
            display: block; 
        }

        /* 80%/20% Type Trigger Box */
        .sp-field-type-trigger-box {
            display: flex;
            width: 100%;
            min-height: 52px;
            border: 1px solid #d1d5db;
            border-radius: var(--radius-md);
            overflow: hidden;
            align-items: stretch;
            box-sizing: border-box;
            background: #ffffff;
            cursor: pointer;
            transition: all 0.15s ease;
        }

        .sp-field-type-trigger-box:hover {
            border-color: #9ca3af;
        }

        .sp-field-type-left-80 {
            flex: 0 0 80%;
            width: 80%;
            background: #ffffff;
            border-right: 1px solid #d1d5db;
            padding: 8px 14px;
            display: flex;
            flex-direction: column;
            justify-content: center;
            gap: 2px;
            box-sizing: border-box;
        }

        .sp-field-type-title {
            font-size: 13px;
            font-weight: 700;
            color: #1f2937;
        }

        .sp-field-type-desc {
            font-size: 11px;
            color: #6b7280;
            line-height: 1.25;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }

        .sp-field-type-right-20 {
            flex: 0 0 20%;
            width: 20%;
            background: #f3f4f6;
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 4px;
            padding: 8px 6px;
            box-sizing: border-box;
            transition: background 0.2s ease;
        }

        .sp-field-type-trigger-box:hover .sp-field-type-right-20 {
            background: #e5e7eb;
        }

        .sp-field-type-btn-text {
            font-size: 12px;
            font-weight: 600;
            color: #374151;
        }

        .sp-field-type-chevron {
            font-size: 11px;
            color: #4b5563;
        }

        /* Forms Dropdown Options Row Component */
        .sp-options-container {
            display: flex !important;
            flex-direction: row !important;
            align-items: flex-start !important;
            justify-content: space-between !important;
            gap: 20px !important;
            margin-top: 14px !important;
            width: 100% !important;
            box-sizing: border-box !important;
        }

        .sp-options-left {
            flex: 0 0 160px !important;
            width: 160px !important;
            min-width: 140px !important;
            max-width: 180px !important;
            display: flex !important;
            flex-direction: column !important;
            gap: 4px !important;
            box-sizing: border-box !important;
        }

        .sp-clean-toggle-title {
            display: block !important;
            font-weight: 700 !important;
            font-size: 13px !important;
            color: #1f2937 !important;
        }

        .sp-clean-toggle-sub {
            display: block !important;
            font-size: 11.5px !important;
            color: #6b7280 !important;
            line-height: 1.35 !important;
        }

        .sp-options-right {
            flex: 1 1 auto !important;
            min-width: 0 !important;
            display: flex !important;
            flex-direction: column !important;
            gap: 8px !important;
            box-sizing: border-box !important;
        }

        .sp-options-col-headers {
            display: flex !important;
            flex-direction: row !important;
            align-items: center !important;
            gap: 8px !important;
            padding-left: 22px !important;
            padding-right: 32px !important;
            margin-bottom: 2px !important;
            box-sizing: border-box !important;
        }

        .sp-option-row {
            display: flex;
            align-items: center;
            gap: 6px;
            width: 100%;
        }

        .sp-opt-reorder-handle {
            cursor: grab;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            color: #9ca3af;
            width: 16px;
            min-width: 16px;
            height: 16px;
        }

        .sp-val-input {
            width: 100%;
            height: 28px !important;
            font-size: 12px !important;
            padding: 0 8px !important;
            border: 1px solid #d1d5db !important;
            border-radius: var(--radius-sm) !important;
            background: #ffffff !important;
            color: #1f2937 !important;
            box-sizing: border-box !important;
            outline: none;
            transition: var(--transition);
        }

        .sp-val-input:focus {
            border-color: #000000 !important;
            box-shadow: 0 0 0 2px rgba(0, 0, 0, 0.08) !important;
        }

        .sp-btn-icon-add {
            width: 26px !important;
            height: 26px !important;
            min-width: 26px !important;
            display: inline-flex !important;
            align-items: center !important;
            justify-content: center !important;
            background: #f3f4f6 !important;
            border: 1px solid #d1d5db !important;
            border-radius: 4px !important;
            color: #374151 !important;
            cursor: pointer !important;
            padding: 0 !important;
            transition: all 0.15s ease;
        }

        .sp-btn-icon-add:hover {
            background: #e5e7eb !important;
            border-color: #9ca3af !important;
            color: #000000 !important;
        }

        .btn-del-rule {
            width: 26px !important;
            height: 26px !important;
            min-width: 26px !important;
            display: inline-flex !important;
            align-items: center !important;
            justify-content: center !important;
            background: transparent !important;
            border: none !important;
            color: #ef4444 !important;
            cursor: pointer !important;
            padding: 0 !important;
            transition: opacity 0.15s ease;
        }

        .btn-del-rule:hover {
            opacity: 0.7;
        }

        /* Type Picker Overlay & Drawer */
        .field-type-picker-overlay {
            position: fixed;
            top: 58px;
            left: 0;
            width: 100%;
            height: calc(100% - 58px);
            background: rgba(15, 23, 42, 0.4);
            backdrop-filter: blur(4px);
            z-index: 300000;
            display: flex;
            justify-content: flex-end;
            opacity: 1;
            animation: fadeInOverlay 0.3s forwards;
        }

        .field-type-picker-wrapper {
            width: 680px;
            max-width: 92vw;
            background: var(--surface);
            margin-top: 0;
            height: 100%;
            border-top-left-radius: 0;
            box-shadow: -10px 0 50px rgba(0, 0, 0, 0.15);
            display: flex;
            flex-direction: column;
            transform: translateX(0);
            animation: slideInPanel 0.35s cubic-bezier(0.16, 1, 0.3, 1) forwards;
            overflow: hidden;
            border-left: 1px solid var(--border-light);
        }

        .field-type-picker-body {
            padding: 20px 24px;
            flex: 1;
            overflow-y: auto;
            min-height: 0;
        }

        .field-type-grid {
            display: flex;
            flex-direction: column;
            gap: 8px;
        }

        .field-type-card {
            display: flex;
            align-items: center;
            padding: 10px 14px;
            border: 1px solid #e5e7eb;
            border-radius: var(--radius-md);
            background: #ffffff;
            cursor: pointer;
            transition: all 0.2s ease;
            gap: 12px;
        }

        .field-type-card:hover {
            border-color: #9ca3af;
            background: #f9fafb;
        }

        .field-type-card.selected {
            border-color: #4b5563;
            background: #f3f4f6;
            box-shadow: 0 0 0 2px rgba(75, 85, 99, 0.15);
        }

        .field-type-card-icon {
            width: 36px;
            height: 36px;
            border-radius: 6px;
            background: #f3f4f6;
            display: flex;
            align-items: center;
            justify-content: center;
            color: #4b5563;
            flex-shrink: 0;
            transition: all 0.2s ease;
        }

        .field-type-card.selected .field-type-card-icon {
            background: #374151;
            color: #ffffff;
        }

        .field-type-card-info {
            display: flex;
            flex-direction: column;
            gap: 2px;
            flex: 1;
        }

        .field-type-card-name {
            font-size: 13px;
            font-weight: 700;
            color: #1f2937;
        }

        .field-type-card-desc {
            font-size: 11px;
            color: #6b7280;
            line-height: 1.3;
        }

        .field-type-card-check {
            display: flex;
            align-items: center;
            justify-content: center;
            color: #374151;
        }

        /* Sub-settings & Toggles */
        .sp-sub-setting { 
            margin-top: 8px; 
            padding: 16px;
            background: #f8fafc;
            border: 1px solid #e2e8f0;
            border-radius: 8px;
        }

        .sp-toggle-row { 
            display: flex; 
            justify-content: space-between; 
            align-items: center; 
            padding: 12px 16px; 
            background: #f9fafb; 
            border-radius: var(--radius-md); 
            border: 1px solid #e5e7eb; 
            transition: var(--transition);
        }

        .sp-toggle-info { display: flex; flex-direction: column; gap: 2px; }

        .sp-toggle-title { 
            font-size: 13.5px; 
            font-weight: 700; 
            color: var(--text-primary); 
        }

        .sp-toggle-desc { 
            font-size: 11.5px; 
            color: var(--text-tertiary); 
            font-weight: 500;
        }

        /* Switch Component */
        .switch { 
            position: relative; 
            display: inline-block; 
            width: 42px; 
            height: 22px; 
            flex-shrink: 0;
        }

        .switch input { opacity: 0; width: 0; height: 0; }

        .slider { 
            position: absolute; 
            cursor: pointer; 
            top: 0; 
            left: 0; 
            right: 0; 
            bottom: 0; 
            background-color: #cbd5e1; 
            transition: var(--transition); 
            border-radius: 34px; 
        }

        .slider:before { 
            position: absolute; 
            content: ""; 
            height: 16px; 
            width: 16px; 
            left: 3px; 
            bottom: 3px; 
            background-color: white; 
            transition: var(--transition); 
            border-radius: 50%; 
            box-shadow: 0 1px 3px rgba(0,0,0,0.2);
        }

        input:checked + .slider { 
            background-color: #10b981; 
        }

        input:checked + .slider:before { 
            transform: translateX(20px); 
        }

        .switch-sm { width: 34px; height: 18px; margin: 0; }
        
        .switch-sm .slider:before { 
            height: 12px; 
            width: 12px; 
            left: 3px; 
            bottom: 3px; 
        }
        
        .switch-sm input:checked + .slider:before { 
            transform: translateX(16px); 
        }

        /* Docked Builder Footer (Exact Forms UI) */
        .builder-footer {
            display: flex;
            justify-content: space-between;
            align-items: stretch;
            height: 60px;
            flex-shrink: 0;
            background: #ffffff;
            border: none;
            border-top: 1px solid #e5e7eb;
            border-radius: 0;
            padding: 0 24px;
            margin: 0;
            box-shadow: 0 -2px 8px rgba(0, 0, 0, 0.04);
            box-sizing: border-box;
        }

        .builder-footer .footer-left {
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .builder-footer .footer-form-label {
            font-size: 11px;
            font-weight: 800;
            color: #94a3b8;
            letter-spacing: 0.05em;
            text-transform: uppercase;
        }

        .builder-footer .footer-divider {
            color: #cbd5e1;
            font-weight: 300;
        }

        .builder-footer .footer-form-name {
            font-size: 13px;
            font-weight: 600;
            color: #374151;
        }

        .builder-footer .footer-right {
            display: flex;
            align-items: stretch;
            gap: 0;
            height: 100%;
        }

        .builder-footer .header-divider {
            width: 1px;
            background: #e0e0e0;
            align-self: stretch;
        }

        .builder-footer .footer-btn {
            background: transparent !important;
            border: none !important;
            padding: 0 16px !important;
            height: 100% !important;
            display: flex !important;
            align-items: center !important;
            justify-content: center !important;
            font-size: 13px !important;
            font-weight: 600 !important;
            color: #000000 !important;
            cursor: pointer !important;
            transition: background 0.15s !important;
            user-select: none !important;
            border-radius: 0 !important;
        }

        .builder-footer .footer-btn:hover {
            background: #f5f5f5 !important;
        }

        .builder-footer .footer-save-btn {
            color: #2ea44f !important;
            font-size: 13px !important;
            font-weight: 600 !important;
        }

        .builder-footer .footer-discard-btn {
            color: #dc3545 !important;
            font-size: 13px !important;
            font-weight: 600 !important;
        }

        /* Full Height & Width Multi-Choice Layout */
        .sp-multi-choice-layout {
            display: flex;
            flex-direction: column;
            flex: 1;
            height: 100%;
            width: 100%;
            min-height: 0;
            box-sizing: border-box;
        }

        .sp-choice-header-info {
            display: flex;
            flex-direction: column;
            gap: 10px;
            margin-bottom: 14px;
            flex-shrink: 0;
        }

        .sp-choice-desc {
            font-size: 13px;
            color: #6b7280;
            line-height: 1.45;
            margin: 0;
        }

        .sp-choice-actions {
            display: flex;
            justify-content: space-between;
            align-items: center;
            gap: 8px;
            flex-wrap: wrap;
        }

        .sp-choices-list {
            display: flex;
            flex-direction: column;
            gap: 8px;
            flex: 1;
            height: 100%;
            min-height: 0;
            overflow-y: auto;
            padding-right: 4px;
            box-sizing: border-box;
        }

        .sp-choice-card {
            display: flex;
            align-items: center;
            justify-content: space-between;
            width: 100%;
            min-width: 100%;
            max-width: 100%;
            padding: 12px 16px;
            background: #ffffff;
            border: 1px solid #e5e7eb;
            border-radius: 6px;
            cursor: pointer;
            box-sizing: border-box;
            transition: all 0.15s ease;
        }

        .sp-choice-card:hover {
            border-color: #000000;
            background: #f9fafb;
        }

        .sp-choice-card.sp-choice-active {
            border-color: #000000;
            background: #f3f4f6;
        }

        .sp-choice-info {
            display: flex;
            flex-direction: column;
            gap: 2px;
            flex: 1;
            padding-right: 12px;
        }

        .sp-choice-title {
            font-size: 13.5px;
            font-weight: 700;
            color: #111827;
        }

        .sp-choice-meta {
            font-size: 12px;
            color: #6b7280;
        }

        .sp-choice-meta code {
            font-size: 11.5px;
            font-weight: 600;
            color: #374151;
            background: #e5e7eb;
            padding: 1px 5px;
            border-radius: 3px;
        }

        .sp-slider-row {
            display: flex;
            align-items: center;
            gap: 12px;
        }

        .sp-slider-row input[type="range"] {
            flex: 1;
            accent-color: var(--primary);
        }

        .sp-footer {
            padding: 16px 24px;
            background: #fafafa;
            border-top: 1px solid var(--border-light);
            display: flex;
            justify-content: flex-end;
            gap: 10px;
        }

        /* Test Search Debugger */
        .test-search-container {
            display: flex;
            flex-direction: column;
            gap: 16px;
        }

        .test-search-bar-wrap {
            position: relative;
            background: #ffffff;
            padding: 16px 20px;
            border: 1px solid var(--border);
            border-radius: var(--radius-md);
            box-shadow: var(--shadow-sm);
            display: flex;
            flex-direction: column;
            gap: 12px;
        }

        .test-search-input-box {
            position: relative;
            display: flex;
            gap: 10px;
        }

        .test-search-input-box .search-icon {
            position: absolute;
            left: 14px;
            top: 50%;
            transform: translateY(-50%);
            color: var(--text-tertiary);
        }

        .test-search-input {
            flex: 1;
            padding: 10px 14px 10px 40px;
            border: 1px solid var(--border);
            border-radius: var(--radius-sm);
            font-size: 14px;
            outline: none;
        }

        .test-search-input:focus {
            border-color: var(--primary);
        }

        .test-search-options {
            display: flex;
            gap: 20px;
            align-items: center;
        }

        .test-opt-group {
            display: flex;
            align-items: center;
            gap: 8px;
            font-size: 12.5px;
            color: var(--text-secondary);
        }

        .autocomplete-dropdown {
            position: absolute;
            top: calc(100% + 4px);
            left: 20px;
            right: 20px;
            background: #ffffff;
            border: 1px solid var(--border);
            border-radius: var(--radius-md);
            box-shadow: var(--shadow-lg);
            z-index: 100;
            max-height: 250px;
            overflow-y: auto;
        }

        .autocomplete-header {
            padding: 8px 12px;
            font-size: 11px;
            font-weight: 700;
            color: var(--text-tertiary);
            background: #fafafa;
            border-bottom: 1px solid var(--border-light);
            text-transform: uppercase;
        }

        .autocomplete-item {
            padding: 9px 12px;
            font-size: 13px;
            display: flex;
            align-items: center;
            gap: 10px;
            cursor: pointer;
            border-bottom: 1px solid var(--border-light);
        }

        .autocomplete-item:hover {
            background: var(--primary-bg);
        }

        .suggestion-banner {
            display: flex;
            align-items: center;
            gap: 8px;
            padding: 10px 16px;
            background: #eff6ff;
            border: 1px solid #bfdbfe;
            border-radius: var(--radius-sm);
            font-size: 13.5px;
            color: #1e40af;
        }

        .suggestion-link {
            text-decoration: underline;
            cursor: pointer;
        }

        .active-filters-bar {
            display: flex;
            align-items: center;
            gap: 8px;
            font-size: 12.5px;
            flex-wrap: wrap;
        }

        .btn-clear-filters {
            background: transparent;
            border: none;
            color: var(--error);
            font-size: 12px;
            font-weight: 600;
            cursor: pointer;
            text-decoration: underline;
        }

        .test-body-grid {
            display: grid;
            gap: 20px;
        }

        .test-body-grid.with-diagnostics {
            grid-template-columns: 1fr 380px;
        }

        .test-body-grid.full-width {
            grid-template-columns: 1fr;
        }

        .results-stats-header {
            display: flex;
            justify-content: space-between;
            font-size: 13px;
            color: var(--text-secondary);
            margin-bottom: 12px;
        }

        .results-list {
            display: flex;
            flex-direction: column;
            gap: 12px;
        }

        .result-card {
            background: #ffffff;
            border: 1px solid var(--border);
            border-radius: var(--radius-md);
            padding: 16px;
            box-shadow: var(--shadow-sm);
            display: flex;
            flex-direction: column;
            gap: 8px;
        }

        .result-bestbet {
            border-color: #f59e0b;
            background: #fffbeb;
        }

        .result-top {
            display: flex;
            align-items: center;
            gap: 10px;
            flex-wrap: wrap;
        }

        .result-rank {
            font-weight: 800;
            color: var(--text-tertiary);
            font-size: 13px;
        }

        .result-title {
            font-size: 15px;
            font-weight: 700;
            color: #1d4ed8;
            text-decoration: none;
        }

        .result-title:hover {
            text-decoration: underline;
        }

        .score-pill {
            margin-left: auto;
            font-size: 11.5px;
            font-weight: 600;
            color: var(--text-secondary);
            background: var(--surface-hover);
            padding: 2px 7px;
            border-radius: 4px;
        }

        .result-snippet {
            font-size: 13px;
            color: var(--text-secondary);
            line-height: 1.5;
        }

        .result-snippet mark {
            background: #fef08a;
            font-weight: 600;
            padding: 1px 3px;
            border-radius: 2px;
        }

        .result-meta {
            display: flex;
            gap: 16px;
            font-size: 11.5px;
            color: var(--text-tertiary);
            margin-top: 4px;
        }

        /* Diagnostics Sidebar */
        .diag-card {
            background: #ffffff;
            border: 1px solid var(--border);
            border-radius: var(--radius-md);
            overflow: hidden;
            box-shadow: var(--shadow-sm);
            position: sticky;
            top: 20px;
        }

        .diag-header {
            padding: 14px 16px;
            background: #18181b;
            color: #ffffff;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }

        .diag-header h4 {
            margin: 0;
            font-size: 14px;
        }

        .diag-body {
            padding: 16px;
            display: flex;
            flex-direction: column;
            gap: 14px;
            font-size: 12.5px;
            max-height: calc(100vh - 220px);
            overflow-y: auto;
        }

        .diag-section {
            display: flex;
            flex-direction: column;
            gap: 6px;
        }

        .diag-label {
            font-size: 11.5px;
            font-weight: 700;
            color: var(--text-tertiary);
            text-transform: uppercase;
        }

        .diag-tags {
            display: flex;
            flex-wrap: wrap;
            gap: 4px;
        }

        .diag-tag {
            background: var(--surface-hover);
            border: 1px solid var(--border-light);
            padding: 2px 6px;
            border-radius: 3px;
            font-size: 11px;
            font-family: monospace;
        }

        .diag-tag-term {
            background: #ecfdf5;
            border-color: #a7f3d0;
            color: #065f46;
        }

        .diag-query-box {
            background: #fafafa;
            border: 1px solid var(--border-light);
            border-radius: 4px;
            padding: 8px;
            margin-top: 4px;
        }

        .diag-query-idx {
            font-weight: 700;
            font-size: 11px;
            color: var(--text-secondary);
            margin-bottom: 2px;
        }

        .diag-query-code {
            margin: 0;
            font-family: monospace;
            font-size: 11px;
            white-space: pre-wrap;
            word-break: break-all;
            color: var(--text-primary);
        }

        .diag-notes-list {
            margin: 0;
            padding-left: 18px;
            color: var(--text-secondary);
            font-size: 12px;
        }

        /* Insights View */
        .stat-cards-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
            gap: 16px;
        }

        .stat-card {
            background: #ffffff;
            border: 1px solid var(--border);
            border-radius: var(--radius-md);
            padding: 16px 20px;
            box-shadow: var(--shadow-sm);
        }

        .stat-value {
            font-size: 26px;
            font-weight: 800;
            color: var(--text-primary);
            line-height: 1.1;
        }

        .stat-label {
            margin-top: 6px;
            font-size: 12px;
            color: var(--text-tertiary);
            font-weight: 600;
        }

        .text-danger { color: var(--error); }

        .volume-chart {
            display: flex;
            align-items: flex-end;
            gap: 6px;
            height: 120px;
            padding-top: 10px;
            border-bottom: 1px solid var(--border-light);
        }

        .chart-bar-wrap {
            flex: 1;
            height: 100%;
            display: flex;
            flex-direction: column;
            justify-content: flex-end;
            align-items: center;
            cursor: pointer;
        }

        .chart-bar {
            width: 100%;
            background: var(--primary);
            border-radius: 3px 3px 0 0;
            transition: height 0.2s ease;
        }

        .chart-bar:hover {
            background: #3b82f6;
        }

        .chart-date {
            font-size: 9px;
            color: var(--text-tertiary);
            margin-top: 4px;
            transform: rotate(-45deg);
        }

        .insights-tabs-bar {
            display: flex;
            border-bottom: 1px solid var(--border);
            gap: 8px;
        }

        .insights-tab {
            padding: 10px 16px;
            background: transparent;
            border: none;
            font-size: 13px;
            font-weight: 600;
            color: var(--text-secondary);
            cursor: pointer;
            border-bottom: 2px solid transparent;
            margin-bottom: -1px;
        }

        .insights-tab:hover {
            color: var(--primary);
        }

        .insights-tab.active {
            color: var(--primary);
            border-bottom-color: var(--primary);
        }

        /* Catalog / Fields View */
        .fields-details summary {
            cursor: pointer;
            font-size: 13px;
            font-weight: 600;
            color: var(--text-secondary);
        }

        .fields-grid {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
            gap: 8px;
        }

        .field-item {
            display: flex;
            align-items: center;
            gap: 6px;
            padding: 6px 10px;
            background: var(--surface-hover);
            border: 1px solid var(--border-light);
            border-radius: 4px;
            font-size: 12px;
        }

        .field-type-pill {
            font-size: 10px;
            color: var(--text-tertiary);
            margin-left: auto;
            text-transform: uppercase;
        }

        /* Modals & Dialogs */
        .modal-overlay {
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: rgba(0, 0, 0, 0.5);
            display: flex;
            align-items: center;
            justify-content: center;
            z-index: 10000;
            backdrop-filter: blur(2px);
        }

        .modal-card {
            background: #ffffff;
            border-radius: var(--radius-lg);
            width: 480px;
            max-width: 90vw;
            box-shadow: var(--shadow-lg);
            overflow: hidden;
            animation: fadeInModal 0.15s ease-out;
        }

        @keyframes fadeInModal {
            from { opacity: 0; transform: scale(0.95); }
            to { opacity: 1; transform: scale(1); }
        }

        .modal-header {
            padding: 16px 20px;
            border-bottom: 1px solid var(--border-light);
            display: flex;
            justify-content: space-between;
            align-items: center;
        }

        .modal-header h4 {
            margin: 0;
            font-size: 15px;
            font-weight: 700;
        }

        .modal-body {
            padding: 20px;
            font-size: 13.5px;
            color: var(--text-primary);
        }

        .modal-footer {
            padding: 14px 20px;
            background: #fafafa;
            border-top: 1px solid var(--border-light);
            display: flex;
            justify-content: flex-end;
            gap: 8px;
        }

        /* Toasts Stack */
        .toast-stack {
            position: fixed;
            bottom: 24px;
            right: 24px;
            display: flex;
            flex-direction: column;
            gap: 8px;
            z-index: 10001;
        }

        .toast {
            display: flex;
            align-items: center;
            gap: 10px;
            padding: 12px 18px;
            background: #18181b;
            color: #ffffff;
            border-radius: var(--radius-md);
            box-shadow: var(--shadow-lg);
            font-size: 13px;
            font-weight: 500;
            animation: slideUpToast 0.2s ease-out;
        }

        @keyframes slideUpToast {
            from { transform: translateY(20px); opacity: 0; }
            to { transform: translateY(0); opacity: 1; }
        }

        .toast-success { border-left: 4px solid var(--success); }
        .toast-error { border-left: 4px solid var(--error); }
        .toast-warning { border-left: 4px solid var(--warning); }
        .toast-info { border-left: 4px solid #38bdf8; }

        .toast-icon {
            font-weight: 800;
            font-size: 14px;
        }

        .toast-close {
            background: transparent;
            border: none;
            color: #a1a1aa;
            font-size: 16px;
            cursor: pointer;
            padding: 0;
            margin-left: 8px;
        }

        .toast-close:hover {
            color: #ffffff;
        }

        /* Empty States */
        .empty-state {
            text-align: center;
            padding: 48px 24px;
            color: var(--text-secondary);
        }

        .empty-icon {
            font-size: 36px;
            width: 48px;
            height: 48px;
            color: var(--text-tertiary);
            margin-bottom: 12px;
        }

        .empty-state h4 {
            margin: 0 0 6px 0;
            font-size: 16px;
            font-weight: 700;
            color: var(--text-primary);
        }

        .empty-state p {
            margin: 0 0 16px 0;
            font-size: 13px;
            color: var(--text-tertiary);
        }

        .text-truncate {
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }
    `;
}

customElements.define("imobisoft-search-workspace", ImobisoftSearchWorkspace);
