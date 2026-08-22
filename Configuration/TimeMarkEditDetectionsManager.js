define(['loading', 'toast', 'mainTabsManager'], function (loading, toast, mainTabsManager) {
    'use strict';

    function getTabList() {
        return [
            {
                href: Dashboard.getConfigurationPageUrl('TimeMarkEditPage'),
                name: 'Edit Chapters'
            },
            {
                href: Dashboard.getConfigurationPageUrl('TimeMarkEditSummaryPage'),
                name: 'Summary'
            },
            {
                href: Dashboard.getConfigurationPageUrl('TimeMarkEditDetectionsPage'),
                name: 'Detections'
            }
        ];
    }

    var _view = null;

    function q(id) { return _view.querySelector('#' + id); }

    function loadConfig() {
        ApiClient.getJSON(ApiClient.getUrl('TimeMarkEdit/GetCreditsDetectionConfig'))
            .then(function (result) {
                q('chkCreditsDetectionEnabled').checked = !(result && result.Enabled === false);
                q('chkSkipExistingMarkers').checked = !!(result && result.SkipExistingMarkers);
                updateOptionsVisibility();
            })
            .catch(function () {
                toast({ type: 'error', text: 'Failed to load detection settings' });
            });
    }

    function updateOptionsVisibility() {
        var optionsEl = q('creditsDetectionOptions');
        if (optionsEl) optionsEl.style.display = q('chkCreditsDetectionEnabled').checked ? '' : 'none';
    }

    function saveConfig() {
        var statusEl = q('creditsDetectionSaveStatus');
        if (statusEl) statusEl.textContent = '';
        loading.show();

        fetch(ApiClient.getUrl('TimeMarkEdit/SetCreditsDetectionConfig'), {
            method: 'POST',
            headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                Enabled: q('chkCreditsDetectionEnabled').checked,
                SkipExistingMarkers: q('chkSkipExistingMarkers').checked
            })
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            loading.hide();
            if (result.Success) {
                toast({ type: 'success', text: 'Detection settings saved' });
            } else {
                toast({ type: 'error', text: result.Message || 'Save failed' });
            }
        })
        .catch(function () {
            loading.hide();
            toast({ type: 'error', text: 'Failed to save detection settings' });
        });
    }

    function testConnection() {
        var statusEl = q('creditsDetectorStatus');
        if (statusEl) { statusEl.textContent = 'Checking\u2026'; statusEl.style.color = ''; }

        fetch(ApiClient.getUrl('CreditsDetector/GetProgress'), {
            headers: { 'X-Emby-Token': ApiClient.accessToken() }
        })
        .then(function (r) {
            if (r.status === 404) throw new Error('not-installed');
            if (!r.ok) throw new Error('error');
            return r.json();
        })
        .then(function () {
            if (statusEl) { statusEl.textContent = '\u2713 EmbyCredits plugin found and responding'; statusEl.style.color = '#7cce76'; }
        })
        .catch(function (err) {
            if (statusEl) {
                statusEl.textContent = (err && err.message === 'not-installed')
                    ? '\u2717 EmbyCredits plugin not found on this server'
                    : '\u2717 Could not reach the EmbyCredits plugin';
                statusEl.style.color = '#ef9a9a';
            }
        });
    }

    function init(view) {
        _view = view;

        loadConfig();

        var chkEnabled = q('chkCreditsDetectionEnabled');
        if (chkEnabled) chkEnabled.addEventListener('change', updateOptionsVisibility);

        var btnSave = q('btnSaveCreditsDetectionConfig');
        if (btnSave) btnSave.addEventListener('click', saveConfig);

        var btnTest = q('btnTestCreditsDetectorConnection');
        if (btnTest) btnTest.addEventListener('click', testConnection);
    }

    return function (view, params) {
        view.addEventListener('viewshow', function () {
            mainTabsManager.setTabs(this, 2, getTabList);
            init(this);
        });
    };
});
