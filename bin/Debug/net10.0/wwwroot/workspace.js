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
        _importJsonText: { state: true }
    };

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
            const exists = this._profiles.some(p => p.key === this._currentProfile.key);
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
                if (this._currentProfile?.key === profile.key) {
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
                maxValues: 20,
                hideEmpty: true,
                ranges: [],
                _isNew: !data
            };
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
        this._sidePanelType = null;
        this._sidePanelData = null;
        this._sidePanelErrors = {};
        this.requestUpdate();
    }

    _saveSidePanel() {
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
            if (!d.field || !d.field.trim()) errs.field = "Index field is required.";

            if (Object.keys(errs).length > 0) {
                this._sidePanelErrors = errs;
                this.requestUpdate();
                return;
            }

            const cleanFacet = {
                alias: d.alias.trim(),
                label: d.label?.trim() || d.alias.trim(),
                field: d.field.trim(),
                kind: d.kind || 'field',
                maxValues: parseInt(d.maxValues) || 20,
                hideEmpty: d.hideEmpty !== false,
                ranges: Array.isArray(d.ranges) ? d.ranges : []
            };

            const facets = this._currentProfile.rules.results.facets;
            if (d._isNew) {
                facets.push(cleanFacet);
            } else {
                const idx = facets.findIndex(f => f.alias === d.alias);
                if (idx >= 0) facets[idx] = cleanFacet;
                else facets.push(cleanFacet);
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

            const bestBets = this._currentProfile.rules.ranking.bestBets;
            if (d._isNew) {
                bestBets.push(cleanBet);
            } else {
                const idx = d._index !== undefined ? d._index : -1;
                if (idx >= 0 && idx < bestBets.length) bestBets[idx] = cleanBet;
                else bestBets.push(cleanBet);
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
                const idx = d._index !== undefined ? d._index : -1;
                if (idx >= 0 && idx < sortBy.length) sortBy[idx] = cleanSort;
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
                    <span class="footer-form-label">PROFILE</span>
                    <span class="footer-divider">/</span>
                    <span class="footer-form-name">${p.name || 'Untitled Profile'}</span>
                </div>
                <div class="footer-right">
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
        const sources = p.rules.sources;

        return html`
            <div class="rule-section-grid">
                <!-- 1. Indexes Selection -->
                <div class="card">
                    <div class="card-header">
                        <div class="card-title-wrap">
                            <h4>Target Examine Indexes</h4>
                            <p class="card-subtitle">Select which Examine indexes to search. Leave empty to query all available indexes automatically.</p>
                        </div>
                    </div>
                    <div class="card-body">
                        <div class="checkbox-chips-grid">
                            ${(this._catalog.indexes || []).map(idx => {
                                const isChecked = sources.indexes.includes(idx.name);
                                return html`
                                    <label class="chip-checkbox ${isChecked ? 'chip-checked' : ''}">
                                        <input type="checkbox"
                                               .checked=${isChecked}
                                               @change=${e => {
                                                   if (e.target.checked) sources.indexes.push(idx.name);
                                                   else sources.indexes = sources.indexes.filter(n => n !== idx.name);
                                                   this.requestUpdate();
                                               }}>
                                        <div class="chip-content">
                                            <strong>${idx.name}</strong>
                                            <span class="chip-meta">${idx.documentCount || 0} docs</span>
                                        </div>
                                    </label>
                                `;
                            })}
                        </div>
                        ${sources.indexes.length === 0 ? html`
                            <div class="info-callout">
                                <i class="icon-info"></i>
                                <span>Currently searching <strong>all indexes</strong> discovered on this site.</span>
                            </div>
                        ` : nothing}
                    </div>
                </div>

                <!-- 2. Index Types -->
                <div class="card">
                    <div class="card-header">
                        <div class="card-title-wrap">
                            <h4>Index Entity Types</h4>
                            <p class="card-subtitle">Filter by Examine entity type (content, media, member).</p>
                        </div>
                    </div>
                    <div class="card-body">
                        <div class="checkbox-chips-grid">
                            ${['content', 'media', 'member'].map(type => {
                                const isChecked = sources.indexTypes.includes(type);
                                return html`
                                    <label class="chip-checkbox ${isChecked ? 'chip-checked' : ''}">
                                        <input type="checkbox"
                                               .checked=${isChecked}
                                               @change=${e => {
                                                   if (e.target.checked) sources.indexTypes.push(type);
                                                   else sources.indexTypes = sources.indexTypes.filter(t => t !== type);
                                                   this.requestUpdate();
                                               }}>
                                        <div class="chip-content">
                                            <strong>${type.toUpperCase()}</strong>
                                        </div>
                                    </label>
                                `;
                            })}
                        </div>
                    </div>
                </div>

                <!-- 3. Document Types Filter -->
                <div class="card">
                    <div class="card-header">
                        <div class="card-title-wrap">
                            <h4>Document Types Filter</h4>
                            <p class="card-subtitle">Choose document types to explicitly include or exclude from search results.</p>
                        </div>
                    </div>
                    <div class="card-body">
                        <div class="form-row-2col">
                            <div class="form-group">
                                <label class="sp-label">Include Document Types (Empty = All)</label>
                                <select class="sp-select" @change=${e => {
                                    const val = e.target.value;
                                    if (val && !sources.includeContentTypes.includes(val)) {
                                        sources.includeContentTypes.push(val);
                                        e.target.value = '';
                                        this.requestUpdate();
                                    }
                                }}>
                                    <option value="">+ Add Document Type to Include...</option>
                                    ${(this._catalog.contentTypes || []).map(ct => html`
                                        <option value="${ct.alias}">${ct.name} (${ct.alias})</option>
                                    `)}
                                </select>
                                <div class="tags-container">
                                    ${sources.includeContentTypes.map(alias => html`
                                        <span class="tag-badge tag-include">
                                            <span>${alias}</span>
                                            <button @click=${() => {
                                                sources.includeContentTypes = sources.includeContentTypes.filter(a => a !== alias);
                                                this.requestUpdate();
                                            }}>×</button>
                                        </span>
                                    `)}
                                </div>
                            </div>

                            <div class="form-group">
                                <label class="sp-label">Exclude Document Types</label>
                                <select class="sp-select" @change=${e => {
                                    const val = e.target.value;
                                    if (val && !sources.excludeContentTypes.includes(val)) {
                                        sources.excludeContentTypes.push(val);
                                        e.target.value = '';
                                        this.requestUpdate();
                                    }
                                }}>
                                    <option value="">+ Add Document Type to Exclude...</option>
                                    ${(this._catalog.contentTypes || []).map(ct => html`
                                        <option value="${ct.alias}">${ct.name} (${ct.alias})</option>
                                    `)}
                                </select>
                                <div class="tags-container">
                                    ${sources.excludeContentTypes.map(alias => html`
                                        <span class="tag-badge tag-exclude">
                                            <span>${alias}</span>
                                            <button @click=${() => {
                                                sources.excludeContentTypes = sources.excludeContentTypes.filter(a => a !== alias);
                                                this.requestUpdate();
                                            }}>×</button>
                                        </span>
                                    `)}
                                </div>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- 4. Tree Scoping & Safety Rules -->
                <div class="card">
                    <div class="card-header">
                        <div class="card-title-wrap">
                            <h4>Tree Scoping & Content Restrictions</h4>
                            <p class="card-subtitle">Control subtree scoping, member-protected pages, and publication state.</p>
                        </div>
                    </div>
                    <div class="card-body">
                        <div class="toggle-list">
                            <label class="toggle-item">
                                <div class="toggle-info">
                                    <strong>Respect umbracoNaviHide</strong>
                                    <span>Hides content when the Umbraco navigation hide checkbox is enabled.</span>
                                </div>
                                <input type="checkbox"
                                       class="switch-input"
                                       .checked=${sources.respectNaviHide}
                                       @change=${e => { sources.respectNaviHide = e.target.checked; this.requestUpdate(); }}>
                            </label>

                            <label class="toggle-item">
                                <div class="toggle-info">
                                    <strong>Exclude Member-Protected Pages</strong>
                                    <span>Do not return pages protected by public access / member roles in anonymous searches.</span>
                                </div>
                                <input type="checkbox"
                                       class="switch-input"
                                       .checked=${sources.excludeProtected}
                                       @change=${e => { sources.excludeProtected = e.target.checked; this.requestUpdate(); }}>
                            </label>

                            <label class="toggle-item">
                                <div class="toggle-info">
                                    <strong>Published Content Only</strong>
                                    <span>Strictly filter out unpublished or trashed draft content.</span>
                                </div>
                                <input type="checkbox"
                                       class="switch-input"
                                       .checked=${sources.publishedOnly}
                                       @change=${e => { sources.publishedOnly = e.target.checked; this.requestUpdate(); }}>
                            </label>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    // --- TAB: MATCHING & FIELDS ---
    _renderMatchingTab(p) {
        const matching = p.rules.matching;

        return html`
            <div class="rule-section-grid">
                <!-- 1. Searchable Fields Table -->
                <div class="card">
                    <div class="card-header flex-between">
                        <div class="card-title-wrap">
                            <h4>Searchable Fields & Relevance Weighting</h4>
                            <p class="card-subtitle">Define which index fields are matched, their boost multiplier, and match modes.</p>
                        </div>
                        <button class="btn btn-primary btn-sm" @click=${() => this._openSidePanel('editField')}>
                            <i class="icon-add"></i> Add Field
                        </button>
                    </div>
                    <div class="card-body no-padding">
                        ${matching.fields.length === 0 ? html`
                            <div class="card-empty-pad">
                                <div class="info-callout">
                                    <i class="icon-info"></i>
                                    <span>No specific fields configured. Imobisoft Search is searching all text fields automatically. Add fields to customize weighting.</span>
                                </div>
                            </div>
                        ` : html`
                            <table class="data-table">
                                <thead>
                                    <tr>
                                        <th>Field Name</th>
                                        <th>Match Mode</th>
                                        <th>Boost Weight</th>
                                        <th>Enabled</th>
                                        <th style="text-align:right;">Actions</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    ${matching.fields.map(f => html`
                                        <tr>
                                            <td><strong>${f.name}</strong></td>
                                            <td><span class="badge badge-info">${f.matchMode || 'prefix'}</span></td>
                                            <td><span class="boost-tag">${f.boost}x</span></td>
                                            <td>
                                                <input type="checkbox"
                                                       .checked=${f.enabled !== false}
                                                       @change=${e => { f.enabled = e.target.checked; this.requestUpdate(); }}>
                                            </td>
                                            <td style="text-align:right;">
                                                <button class="btn-icon" title="Edit Field" @click=${() => this._openSidePanel('editField', f)}>
                                                    <i class="icon-edit"></i>
                                                </button>
                                                <button class="btn-icon btn-icon-danger" title="Remove Field" @click=${() => {
                                                    matching.fields = matching.fields.filter(x => x.name !== f.name);
                                                    this.requestUpdate();
                                                }}>
                                                    <i class="icon-trash"></i>
                                                </button>
                                            </td>
                                        </tr>
                                    `)}
                                </tbody>
                            </table>
                        `}
                    </div>
                </div>

                <!-- 2. Matching Engine Parameters -->
                <div class="card">
                    <div class="card-header">
                        <div class="card-title-wrap">
                            <h4>Query Match Parameters</h4>
                            <p class="card-subtitle">Configure logic operators, fuzziness, and length thresholds.</p>
                        </div>
                    </div>
                    <div class="card-body">
                        <div class="form-row-3col">
                            <div class="form-group">
                                <label class="sp-label">Default Combine Operator</label>
                                <select class="sp-select"
                                        .value=${matching.defaultOperator}
                                        @change=${e => { matching.defaultOperator = e.target.value; this.requestUpdate(); }}>
                                    <option value="or">OR (Any field/term matches - Broadest)</option>
                                    <option value="and">AND (All terms must match - Strict)</option>
                                </select>
                            </div>

                            <div class="form-group">
                                <label class="sp-label">Fuzziness Tolerance (0.0 - 1.0)</label>
                                <input type="number"
                                       class="sp-input"
                                       step="0.05"
                                       min="0.1"
                                       max="1.0"
                                       .value=${String(matching.fuzziness)}
                                       @input=${e => { matching.fuzziness = parseFloat(e.target.value) || 0.8; this.requestUpdate(); }}>
                            </div>

                            <div class="form-group">
                                <label class="sp-label">Minimum Query Length</label>
                                <input type="number"
                                       class="sp-input"
                                       min="1"
                                       max="10"
                                       .value=${String(matching.minimumQueryLength)}
                                       @input=${e => { matching.minimumQueryLength = parseInt(e.target.value) || 2; this.requestUpdate(); }}>
                            </div>
                        </div>

                        <div class="toggle-list" style="margin-top: 16px;">
                            <label class="toggle-item">
                                <div class="toggle-info">
                                    <strong>All Terms Must Match</strong>
                                    <span>Require every word in a multi-word search query to match somewhere in the document.</span>
                                </div>
                                <input type="checkbox"
                                       class="switch-input"
                                       .checked=${matching.allTermsMustMatch}
                                       @change=${e => { matching.allTermsMustMatch = e.target.checked; this.requestUpdate(); }}>
                            </label>
                        </div>
                    </div>
                </div>

                <!-- 3. Stop Words & Synonyms -->
                <div class="card">
                    <div class="card-header">
                        <div class="card-title-wrap">
                            <h4>Stop Words & Query Synonyms</h4>
                            <p class="card-subtitle">Strip non-informational words and expand query terms with synonyms.</p>
                        </div>
                    </div>
                    <div class="card-body">
                        <div class="form-row-2col">
                            <!-- Stop Words -->
                            <div class="form-group">
                                <label class="sp-label">Stop Words (Ignored words)</label>
                                <div class="tag-input-row">
                                    <input type="text"
                                           class="sp-input"
                                           id="newStopWordInput"
                                           placeholder="Type word and press Enter"
                                           @keydown=${e => {
                                               if (e.key === 'Enter') {
                                                   e.preventDefault();
                                                   const val = e.target.value.trim().toLowerCase();
                                                   if (val && !matching.stopWords.includes(val)) {
                                                       matching.stopWords.push(val);
                                                       e.target.value = '';
                                                       this.requestUpdate();
                                                   }
                                               }
                                           }}>
                                </div>
                                <div class="tags-container">
                                    ${matching.stopWords.map(w => html`
                                        <span class="tag-badge">
                                            <span>${w}</span>
                                            <button @click=${() => {
                                                matching.stopWords = matching.stopWords.filter(x => x !== w);
                                                this.requestUpdate();
                                            }}>×</button>
                                        </span>
                                    `)}
                                </div>
                            </div>

                            <!-- Synonyms -->
                            <div class="form-group">
                                <div class="flex-between">
                                    <label class="sp-label">Synonym Groups</label>
                                    <button class="btn btn-secondary btn-sm" @click=${() => this._openSidePanel('editSynonym')}>+ Add Synonym</button>
                                </div>
                                <div class="synonyms-list">
                                    ${Object.keys(matching.synonyms || {}).length === 0 ? html`
                                        <div class="text-muted" style="font-size:13px; padding-top:8px;">No synonyms configured.</div>
                                    ` : Object.entries(matching.synonyms).map(([term, syns]) => html`
                                        <div class="synonym-item">
                                            <strong>${term}</strong> ➔ <span>${(syns || []).join(', ')}</span>
                                            <button class="btn-icon btn-icon-danger" @click=${() => {
                                                delete matching.synonyms[term];
                                                this.requestUpdate();
                                            }}>×</button>
                                        </div>
                                    `)}
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    // --- TAB: RANKING & BOOSTS ---
    _renderRankingTab(p) {
        const ranking = p.rules.ranking;

        return html`
            <div class="rule-section-grid">
                <!-- 1. Multi-Level Sort Rules -->
                <div class="card">
                    <div class="card-header flex-between">
                        <div class="card-title-wrap">
                            <h4>Sort Levels</h4>
                            <p class="card-subtitle">Order of priority when sorting results. Default is Score (Relevance Descending).</p>
                        </div>
                        <button class="btn btn-primary btn-sm" @click=${() => this._openSidePanel('editSort')}>
                            <i class="icon-add"></i> Add Sort Level
                        </button>
                    </div>
                    <div class="card-body no-padding">
                        ${ranking.sortBy.length === 0 ? html`
                            <div class="card-empty-pad">
                                <div class="info-callout">
                                    <i class="icon-info"></i>
                                    <span>Sorted by <strong>Relevance Score (Descending)</strong> by default.</span>
                                </div>
                            </div>
                        ` : html`
                            <table class="data-table">
                                <thead>
                                    <tr>
                                        <th>Level</th>
                                        <th>Field</th>
                                        <th>Direction</th>
                                        <th style="text-align:right;">Actions</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    ${ranking.sortBy.map((s, idx) => html`
                                        <tr>
                                            <td><strong>#${idx + 1}</strong></td>
                                            <td>${s.field}</td>
                                            <td><span class="badge badge-info">${s.direction}</span></td>
                                            <td style="text-align:right;">
                                                <button class="btn-icon btn-icon-danger" @click=${() => {
                                                    ranking.sortBy.splice(idx, 1);
                                                    this.requestUpdate();
                                                }}>
                                                    <i class="icon-trash"></i>
                                                </button>
                                            </td>
                                        </tr>
                                    `)}
                                </tbody>
                            </table>
                        `}
                    </div>
                </div>

                <!-- 2. Content Type Boosts & Recency -->
                <div class="card">
                    <div class="card-header flex-between">
                        <div class="card-title-wrap">
                            <h4>Document Type Relevance Multipliers</h4>
                            <p class="card-subtitle">Boost specific document types (e.g. News articles at 2.0x, Products at 1.5x).</p>
                        </div>
                        <button class="btn btn-primary btn-sm" @click=${() => this._openSidePanel('editContentTypeBoost')}>
                            <i class="icon-add"></i> Add Boost
                        </button>
                    </div>
                    <div class="card-body">
                        <div class="tags-container">
                            ${Object.entries(ranking.contentTypeBoosts || {}).map(([alias, boost]) => html`
                                <span class="tag-badge tag-boost">
                                    <span><strong>${alias}</strong>: ${boost}x</span>
                                    <button @click=${() => {
                                        delete ranking.contentTypeBoosts[alias];
                                        this.requestUpdate();
                                    }}>×</button>
                                </span>
                            `)}
                        </div>
                    </div>
                </div>

                <!-- 3. Best Bets (Pinned Queries) -->
                <div class="card">
                    <div class="card-header flex-between">
                        <div class="card-title-wrap">
                            <h4>Best Bets (Pinned Curated Results)</h4>
                            <p class="card-subtitle">Pin specific Umbraco node GUIDs to the very top when visitors search specific terms.</p>
                        </div>
                        <button class="btn btn-primary btn-sm" @click=${() => this._openSidePanel('editBestBet')}>
                            <i class="icon-add"></i> Add Best Bet
                        </button>
                    </div>
                    <div class="card-body no-padding">
                        ${ranking.bestBets.length === 0 ? html`
                            <div class="card-empty-pad">
                                <div class="info-callout">
                                    <i class="icon-info"></i>
                                    <span>No best bets configured. Results are ordered purely by calculated relevance.</span>
                                </div>
                            </div>
                        ` : html`
                            <table class="data-table">
                                <thead>
                                    <tr>
                                        <th>Trigger Search Terms</th>
                                        <th>Pinned Node Keys</th>
                                        <th style="text-align:right;">Actions</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    ${ranking.bestBets.map((bet, idx) => html`
                                        <tr>
                                            <td>
                                                <div class="tags-container">
                                                    ${(bet.terms || []).map(t => html`<span class="tag-badge"><span>${t}</span></span>`)}
                                                </div>
                                            </td>
                                            <td>
                                                <small>${(bet.nodeKeys || []).join(', ')}</small>
                                            </td>
                                            <td style="text-align:right;">
                                                <button class="btn-icon btn-icon-danger" @click=${() => {
                                                    ranking.bestBets.splice(idx, 1);
                                                    this.requestUpdate();
                                                }}>
                                                    <i class="icon-trash"></i>
                                                </button>
                                            </td>
                                        </tr>
                                    `)}
                                </tbody>
                            </table>
                        `}
                    </div>
                </div>

                <!-- 4. Recency Boost & Blocked Terms -->
                <div class="card">
                    <div class="card-header">
                        <div class="card-title-wrap">
                            <h4>Recency Decay & Blocked Search Terms</h4>
                            <p class="card-subtitle">Give recently published or modified content a boost and block unwanted queries.</p>
                        </div>
                    </div>
                    <div class="card-body">
                        <div class="form-row-2col">
                            <div>
                                <label class="toggle-item" style="margin-bottom: 12px;">
                                    <div class="toggle-info">
                                        <strong>Enable Recency Boost</strong>
                                        <span>Lift freshly updated content above older pages.</span>
                                    </div>
                                    <input type="checkbox"
                                           class="switch-input"
                                           .checked=${ranking.recency.enabled}
                                           @change=${e => { ranking.recency.enabled = e.target.checked; this.requestUpdate(); }}>
                                </label>

                                ${ranking.recency.enabled ? html`
                                    <div class="form-row-2col" style="margin-top: 10px;">
                                        <div class="form-group">
                                            <label class="sp-label">Half-Life (Days)</label>
                                            <input type="number"
                                                   class="sp-input"
                                                   .value=${String(ranking.recency.halfLifeDays)}
                                                   @input=${e => { ranking.recency.halfLifeDays = parseInt(e.target.value) || 90; this.requestUpdate(); }}>
                                        </div>
                                        <div class="form-group">
                                            <label class="sp-label">Boost Weight</label>
                                            <input type="number"
                                                   class="sp-input"
                                                   step="0.1"
                                                   .value=${String(ranking.recency.weight)}
                                                   @input=${e => { ranking.recency.weight = parseFloat(e.target.value) || 0.5; this.requestUpdate(); }}>
                                        </div>
                                    </div>
                                ` : nothing}
                            </div>

                            <div class="form-group">
                                <label class="sp-label">Blocked Query Terms (Return 0 results)</label>
                                <input type="text"
                                       class="sp-input"
                                       placeholder="Type blocked term and press Enter"
                                       @keydown=${e => {
                                           if (e.key === 'Enter') {
                                               e.preventDefault();
                                               const val = e.target.value.trim().toLowerCase();
                                               if (val && !ranking.blockedTerms.includes(val)) {
                                                   ranking.blockedTerms.push(val);
                                                   e.target.value = '';
                                                   this.requestUpdate();
                                               }
                                           }
                                       }}>
                                <div class="tags-container" style="margin-top: 8px;">
                                    ${ranking.blockedTerms.map(t => html`
                                        <span class="tag-badge tag-exclude">
                                            <span>${t}</span>
                                            <button @click=${() => {
                                                ranking.blockedTerms = ranking.blockedTerms.filter(x => x !== t);
                                                this.requestUpdate();
                                            }}>×</button>
                                        </span>
                                    `)}
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    // --- TAB: RESULTS & SNIPPETS ---
    _renderResultsTab(p) {
        const res = p.rules.results;

        return html`
            <div class="rule-section-grid">
                <!-- 1. Paging & Capacity -->
                <div class="card">
                    <div class="card-header">
                        <div class="card-title-wrap">
                            <h4>Paging & Capacity Ceilings</h4>
                            <p class="card-subtitle">Set page size and total results window considered per search request.</p>
                        </div>
                    </div>
                    <div class="card-body">
                        <div class="form-row-2col">
                            <div class="form-group">
                                <label class="sp-label">Default Page Size</label>
                                <input type="number"
                                       class="sp-input"
                                       min="1"
                                       max="100"
                                       .value=${String(res.pageSize)}
                                       @input=${e => { res.pageSize = parseInt(e.target.value) || 10; this.requestUpdate(); }}>
                            </div>

                            <div class="form-group">
                                <label class="sp-label">Maximum Results Considered</label>
                                <input type="number"
                                       class="sp-input"
                                       min="10"
                                       max="5000"
                                       .value=${String(res.maxResults)}
                                       @input=${e => { res.maxResults = parseInt(e.target.value) || 500; this.requestUpdate(); }}>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- 2. Highlighting & Snippets -->
                <div class="card">
                    <div class="card-header">
                        <div class="card-title-wrap">
                            <h4>Highlighting & Matching Snippets</h4>
                            <p class="card-subtitle">Extract sentence snippets from documents with highlight markup around matched terms.</p>
                        </div>
                    </div>
                    <div class="card-body">
                        <label class="toggle-item" style="margin-bottom: 16px;">
                            <div class="toggle-info">
                                <strong>Enable Snippet Highlights</strong>
                                <span>Return snippet text with matches highlighted.</span>
                            </div>
                            <input type="checkbox"
                                   class="switch-input"
                                   .checked=${res.highlight.enabled}
                                   @change=${e => { res.highlight.enabled = e.target.checked; this.requestUpdate(); }}>
                        </label>

                        ${res.highlight.enabled ? html`
                            <div class="form-row-3col" style="margin-top: 12px;">
                                <div class="form-group">
                                    <label class="sp-label">Snippet Mode</label>
                                    <select class="sp-select"
                                            .value=${res.highlight.mode}
                                            @change=${e => { res.highlight.mode = e.target.value; this.requestUpdate(); }}>
                                        <option value="sentence">Full Sentence (Natural cut)</option>
                                        <option value="characters">Fixed Characters (Truncated)</option>
                                    </select>
                                </div>

                                <div class="form-group">
                                    <label class="sp-label">Max Snippet Length</label>
                                    <input type="number"
                                           class="sp-input"
                                           .value=${String(res.highlight.snippetLength)}
                                           @input=${e => { res.highlight.snippetLength = parseInt(e.target.value) || 200; this.requestUpdate(); }}>
                                </div>

                                <div class="form-group">
                                    <label class="sp-label">Sentence Context (sentences)</label>
                                    <input type="number"
                                           class="sp-input"
                                           min="0"
                                           max="5"
                                           .value=${String(res.highlight.sentenceContext)}
                                           @input=${e => { res.highlight.sentenceContext = parseInt(e.target.value) || 0; this.requestUpdate(); }}>
                                </div>
                            </div>
                        ` : nothing}
                    </div>
                </div>

                <!-- 3. De-duplication & Grouping -->
                <div class="card">
                    <div class="card-header">
                        <div class="card-title-wrap">
                            <h4>Result Shaping & De-Duplication</h4>
                            <p class="card-subtitle">Collapse duplicates by field and enable content type grouping.</p>
                        </div>
                    </div>
                    <div class="card-body">
                        <div class="form-row-2col">
                            <div class="form-group">
                                <label class="sp-label">De-duplicate Results by Field</label>
                                <input type="text"
                                       class="sp-input"
                                       placeholder="e.g. urlName or empty to disable"
                                       .value=${res.deduplicateByField || ''}
                                       @input=${e => { res.deduplicateByField = e.target.value; this.requestUpdate(); }}>
                            </div>

                            <div class="form-group" style="display:flex; align-items:center; padding-top:20px;">
                                <label class="toggle-item" style="width:100%;">
                                    <div class="toggle-info">
                                        <strong>Group by Document Type</strong>
                                        <span>Compute result counts grouped by content type.</span>
                                    </div>
                                    <input type="checkbox"
                                           class="switch-input"
                                           .checked=${res.groupByContentType}
                                           @change=${e => { res.groupByContentType = e.target.checked; this.requestUpdate(); }}>
                                </label>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    // --- TAB: FACETS & FILTERS ---
    _renderFacetsTab(p) {
        const facets = p.rules.results.facets || [];

        return html`
            <div class="rule-section-grid">
                <div class="card">
                    <div class="card-header flex-between">
                        <div class="card-title-wrap">
                            <h4>Configured Facet Dimensions</h4>
                            <p class="card-subtitle">Facets return dynamic filter dimensions with live counts for your search UI sidebar.</p>
                        </div>
                        <button class="btn btn-primary btn-sm" @click=${() => this._openSidePanel('editFacet')}>
                            <i class="icon-add"></i> Add Facet
                        </button>
                    </div>
                    <div class="card-body no-padding">
                        ${facets.length === 0 ? html`
                            <div class="card-empty-pad">
                                <div class="empty-state">
                                    <i class="icon-filter empty-icon"></i>
                                    <h4>No Facets Configured</h4>
                                    <p>Add facets on Examine fields (such as Category, Document Type, or Date ranges) to offer filter dimensions.</p>
                                    <button class="btn btn-primary btn-sm" @click=${() => this._openSidePanel('editFacet')}>
                                        <i class="icon-add"></i> Add Your First Facet
                                    </button>
                                </div>
                            </div>
                        ` : html`
                            <table class="data-table">
                                <thead>
                                    <tr>
                                        <th>Label & Alias</th>
                                        <th>Target Field</th>
                                        <th>Facet Kind</th>
                                        <th>Max Buckets</th>
                                        <th>Hide Empty</th>
                                        <th style="text-align:right;">Actions</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    ${facets.map((f, idx) => html`
                                        <tr>
                                            <td>
                                                <strong>${f.label || f.alias}</strong>
                                                <div class="text-muted" style="font-size:12px;">alias: ${f.alias}</div>
                                            </td>
                                            <td><code>${f.field}</code></td>
                                            <td><span class="badge badge-info">${f.kind || 'field'}</span></td>
                                            <td>${f.maxValues || 20}</td>
                                            <td>${f.hideEmpty ? 'Yes' : 'No'}</td>
                                            <td style="text-align:right;">
                                                <button class="btn-icon" title="Edit Facet" @click=${() => this._openSidePanel('editFacet', f)}>
                                                    <i class="icon-edit"></i>
                                                </button>
                                                <button class="btn-icon btn-icon-danger" title="Delete Facet" @click=${() => {
                                                    facets.splice(idx, 1);
                                                    this.requestUpdate();
                                                }}>
                                                    <i class="icon-trash"></i>
                                                </button>
                                            </td>
                                        </tr>
                                    `)}
                                </tbody>
                            </table>
                        `}
                    </div>
                </div>
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
        if (t === 'editField') title = d._isNew ? "Add Searchable Field" : `Edit Field: ${d.name}`;
        else if (t === 'editFacet') title = d._isNew ? "Add Facet Dimension" : `Edit Facet: ${d.label || d.alias}`;
        else if (t === 'editBestBet') title = d._isNew ? "Add Best Bet (Pinned Result)" : "Edit Best Bet";
        else if (t === 'editSort') title = d._isNew ? "Add Sort Level" : "Edit Sort Level";
        else if (t === 'editContentTypeBoost') title = "Add Content Type Boost";
        else if (t === 'editSynonym') title = "Add Synonym Group";
        else if (t === 'profileMetadata') title = "Profile Metadata & Settings";

        return html`
            <div class="side-panel-overlay" @click=${this._closeSidePanel}>
                <div class="side-panel-wrapper" @click=${e => e.stopPropagation()}>
                    <div class="sp-header">
                        <div class="sp-header-content">
                            <span class="sp-title">${title}</span>
                            <button class="sp-close" @click=${this._closeSidePanel} title="Close">×</button>
                        </div>
                    </div>

                    <div class="sp-body">
                        ${t === 'editField' ? this._renderFieldSidePanelBody(d) : nothing}
                        ${t === 'editFacet' ? this._renderFacetSidePanelBody(d) : nothing}
                        ${t === 'editBestBet' ? this._renderBestBetSidePanelBody(d) : nothing}
                        ${t === 'editSort' ? this._renderSortSidePanelBody(d) : nothing}
                        ${t === 'editContentTypeBoost' ? this._renderContentTypeBoostSidePanelBody(d) : nothing}
                        ${t === 'editSynonym' ? this._renderSynonymSidePanelBody(d) : nothing}
                        ${t === 'profileMetadata' ? this._renderProfileMetadataSidePanelBody(d) : nothing}
                    </div>

                    <div class="sp-footer">
                        <button class="btn btn-secondary" @click=${this._closeSidePanel}>Cancel</button>
                        <button class="btn btn-primary" @click=${this._saveSidePanel}>Apply Changes</button>
                    </div>
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
        return html`
            <div class="sp-group">
                <label class="sp-label">Facet Alias (Key) *</label>
                <input type="text"
                       class="sp-input"
                       placeholder="e.g. category, docType, priceRange"
                       .value=${d.alias}
                       @input=${e => { d.alias = e.target.value; this.requestUpdate(); }}>
                ${this._sidePanelErrors.alias ? html`<span class="sp-error">${this._sidePanelErrors.alias}</span>` : nothing}
            </div>

            <div class="sp-group">
                <label class="sp-label">Display Label</label>
                <input type="text"
                       class="sp-input"
                       placeholder="e.g. Categories, Content Type"
                       .value=${d.label}
                       @input=${e => { d.label = e.target.value; this.requestUpdate(); }}>
            </div>

            <div class="sp-group">
                <label class="sp-label">Examine Index Field *</label>
                <input type="text"
                       class="sp-input"
                       placeholder="e.g. category, __NodeTypeAlias, createDate"
                       .value=${d.field}
                       @input=${e => { d.field = e.target.value; this.requestUpdate(); }}>
                ${this._sidePanelErrors.field ? html`<span class="sp-error">${this._sidePanelErrors.field}</span>` : nothing}
            </div>

            <div class="sp-group">
                <label class="sp-label">Facet Kind</label>
                <select class="sp-select"
                        .value=${d.kind}
                        @change=${e => { d.kind = e.target.value; this.requestUpdate(); }}>
                    <option value="field">Field Value Buckets (Distinct values)</option>
                    <option value="dateRange">Date Range Buckets</option>
                    <option value="numeric">Numeric Range Buckets</option>
                </select>
            </div>

            <div class="sp-group">
                <label class="sp-label">Max Returned Buckets</label>
                <input type="number"
                       class="sp-input"
                       min="1"
                       max="100"
                       .value=${String(d.maxValues)}
                       @input=${e => { d.maxValues = parseInt(e.target.value) || 20; this.requestUpdate(); }}>
            </div>

            <div class="sp-group">
                <label class="toggle-item">
                    <div class="toggle-info">
                        <strong>Hide Empty Buckets</strong>
                        <span>Hide buckets that currently have 0 matched results.</span>
                    </div>
                    <input type="checkbox"
                           class="switch-input"
                           .checked=${d.hideEmpty}
                           @change=${e => { d.hideEmpty = e.target.checked; this.requestUpdate(); }}>
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
        .side-panel-overlay {
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: rgba(0, 0, 0, 0.45);
            display: flex;
            justify-content: flex-end;
            z-index: 9999;
            backdrop-filter: blur(2px);
        }

        .side-panel-wrapper {
            width: 500px;
            max-width: 90vw;
            height: 100%;
            background: #ffffff;
            box-shadow: var(--shadow-lg);
            display: flex;
            flex-direction: column;
            box-sizing: border-box;
            animation: slideInRight 0.22s ease-out;
        }

        @keyframes slideInRight {
            from { transform: translateX(100%); }
            to { transform: translateX(0); }
        }

        .sp-header {
            padding: 16px 24px;
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
        }

        .sp-group {
            display: flex;
            flex-direction: column;
            gap: 6px;
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
