define(['loading', 'toast', 'mainTabsManager'], function (loading, toast, mainTabsManager) {
    'use strict';

    var _lastData = null;
    var _expandedSeries = {};
    var _expandedSeasons = {};
    var _detectionEnabled = false;
    var _skipExisting = true;
    var _detectionPollInterval = null;

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

    function pctBar(count, total, fillClass) {
        var pct = total > 0 ? Math.min(100, Math.round(count / total * 100)) : 0;
        return '<span class="tme-pct-bar-wrap">' +
            '<span class="tme-pct-bar"><span class="' + fillClass + '" style="width:' + pct + '%;"></span></span>' +
            '<span>' + count + ' <span style="opacity:0.5;">(' + pct + '%)</span></span>' +
            '</span>';
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function detectButton(scope, attrs, label) {
        if (!_detectionEnabled) return '';
        var dataAttrs = 'data-scope="' + scope + '"';
        Object.keys(attrs).forEach(function (key) {
            dataAttrs += ' data-' + key + '="' + escapeHtml(String(attrs[key])) + '"';
        });
        return '<button is="emby-button" type="button" class="raised tme-detect-btn tme-detect-action" ' + dataAttrs + '>' +
            '<i class="md-icon">movie_filter</i>' + label +
            '</button>';
    }

    function renderTable() {
        var body = document.querySelector('#tmeSummaryBody');
        body.innerHTML = '';

        if (!_lastData || !_lastData.Series || _lastData.Series.length === 0) {
            document.querySelector('#tmeSummaryStatus').textContent = 'No TV series found.';
            return;
        }

        _lastData.Series.forEach(function (series) {
            var expanded = !!_expandedSeries[series.Id];

            var seriesTr = document.createElement('tr');
            seriesTr.className = 'tme-summary-series-row' + (expanded ? ' expanded' : '');
            seriesTr.setAttribute('data-series-id', series.Id);
            seriesTr.innerHTML =
                '<td>' +
                    '<div class="tme-row-flex">' +
                        '<span class="tme-row-name">' +
                            '<i class="md-icon tme-summary-toggle-icon">chevron_right</i>' + escapeHtml(series.Name) +
                        '</span>' +
                        detectButton('Series', { 'series-id': series.Id }, 'Detect Series') +
                    '</div>' +
                '</td>' +
                '<td style="text-align:center;">' + series.EpisodeCount + '</td>' +
                '<td>' + pctBar(series.IntroCount, series.EpisodeCount, 'tme-pct-fill-intro') + '</td>' +
                '<td style="text-align:center;"><span class="tme-avg">' + escapeHtml(series.AvgIntro) + '</span></td>' +
                '<td>' + pctBar(series.CreditsCount, series.EpisodeCount, 'tme-pct-fill-credits') + '</td>';
            body.appendChild(seriesTr);

            if (!expanded) return;

            (series.Seasons || []).forEach(function (season) {
                var seasonExpanded = !!_expandedSeasons[season.Id];

                var seasonTr = document.createElement('tr');
                seasonTr.className = 'tme-summary-season-row' + (seasonExpanded ? ' expanded' : '');
                seasonTr.setAttribute('data-series-id', series.Id);
                seasonTr.setAttribute('data-season-id', season.Id);
                seasonTr.innerHTML =
                    '<td style="padding-left:1.8em;opacity:0.85;">' +
                        '<div class="tme-row-flex">' +
                            '<span class="tme-row-name">' +
                                '<i class="md-icon tme-summary-toggle-icon">chevron_right</i>' + escapeHtml(season.Name) +
                            '</span>' +
                            detectButton('Season', { 'series-id': series.Id, 'season-number': season.IndexNumber }, 'Detect Season') +
                        '</div>' +
                    '</td>' +
                    '<td style="text-align:center;">' + season.EpisodeCount + '</td>' +
                    '<td>' + pctBar(season.IntroCount, season.EpisodeCount, 'tme-pct-fill-intro') + '</td>' +
                    '<td style="text-align:center;"><span class="tme-avg">' + escapeHtml(season.AvgIntro) + '</span></td>' +
                    '<td>' + pctBar(season.CreditsCount, season.EpisodeCount, 'tme-pct-fill-credits') + '</td>';
                body.appendChild(seasonTr);

                if (!seasonExpanded) return;

                (season.Episodes || []).forEach(function (episode) {
                    var episodeTr = document.createElement('tr');
                    episodeTr.className = 'tme-summary-episode-row';
                    episodeTr.setAttribute('data-series-id', series.Id);
                    episodeTr.setAttribute('data-season-id', season.Id);
                    episodeTr.innerHTML =
                        '<td style="padding-left:3.2em;">' +
                            '<div class="tme-row-flex">' +
                                '<span class="tme-row-name">Episode ' + (episode.IndexNumber != null ? episode.IndexNumber : '?') +
                                    ' &mdash; ' + escapeHtml(episode.Name) + '</span>' +
                                detectButton('Episode', { 'episode-id': episode.Id }, 'Detect Episode') +
                            '</div>' +
                        '</td>' +
                        '<td></td>' +
                        '<td class="tme-status-yesno">' + (episode.HasIntro ? 'Yes' : '&mdash;') + '</td>' +
                        '<td></td>' +
                        '<td class="tme-status-yesno">' + (episode.HasCredits ? 'Yes' : '&mdash;') + '</td>';
                    body.appendChild(episodeTr);
                });
            });
        });

        document.querySelector('#tmeSummaryStatus').style.display = 'none';
        document.querySelector('#tmeSummaryTable').style.display = '';
    }

    function toggleSeries(seriesId) {
        _expandedSeries[seriesId] = !_expandedSeries[seriesId];
        renderTable();
    }

    function toggleSeason(seasonId) {
        _expandedSeasons[seasonId] = !_expandedSeasons[seasonId];
        renderTable();
    }

    function loadDetectionConfig() {
        return ApiClient.getJSON(ApiClient.getUrl('TimeMarkEdit/GetCreditsDetectionConfig'))
            .then(function (result) {
                _detectionEnabled = !(result && result.Enabled === false);
                _skipExisting = !!(result && result.SkipExistingMarkers);
            })
            .catch(function () {
                _detectionEnabled = false;
            });
    }

    function runDetection(scope, dataset) {
        var url, payload, confirmText, scopeLabel;

        if (scope === 'Episode') {
            url = 'CreditsDetector/ProcessEpisode';
            payload = { ItemId: dataset.episodeId, SkipExistingMarkers: _skipExisting };
            confirmText = 'Start EmbyCredits detection for this episode?';
            scopeLabel = 'this episode';
        } else if (scope === 'Season') {
            url = 'CreditsDetector/ProcessSeason';
            payload = { SeriesId: dataset.seriesId, SeasonNumber: parseInt(dataset.seasonNumber, 10), SkipExistingMarkers: _skipExisting };
            confirmText = 'Start EmbyCredits detection for the entire season?';
            scopeLabel = 'the season';
        } else {
            url = 'CreditsDetector/ProcessSeries';
            payload = { SeriesId: dataset.seriesId, SkipExistingMarkers: _skipExisting };
            confirmText = 'Start EmbyCredits detection for the entire series?';
            scopeLabel = 'the series';
        }

        if (!confirm(confirmText)) return;

        loading.show();

        fetch(ApiClient.getUrl(url), {
            method: 'POST',
            headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        })
        .then(function (r) {
            if (r.status === 404) throw new Error('not-installed');
            return r.json();
        })
        .then(function (result) {
            loading.hide();
            if (result.Success) {
                startDetectionPolling(scopeLabel);
            } else {
                toast({ type: 'error', text: result.Message || 'Failed to start detection' });
            }
        })
        .catch(function (err) {
            loading.hide();
            if (err && err.message === 'not-installed') {
                toast({ type: 'error', text: 'EmbyCredits plugin was not found on this server — see the Detections tab' });
            } else {
                console.error('Error starting credits detection:', err);
                toast({ type: 'error', text: 'Failed to start detection' });
            }
        });
    }

    function startDetectionPolling(scopeLabel) {
        var bar = document.querySelector('#tmeDetectionProgressBar');
        var statusText = document.querySelector('#tmeDetectionStatusText');
        var percentText = document.querySelector('#tmeDetectionPercentText');
        var fill = document.querySelector('#tmeDetectionProgressFill');
        var countsText = document.querySelector('#tmeDetectionCountsText');
        var cancelBtn = document.querySelector('#btnCancelTmeDetection');

        if (_detectionPollInterval) clearInterval(_detectionPollInterval);

        if (bar) bar.style.display = 'block';
        if (statusText) statusText.textContent = 'Starting EmbyCredits detection for ' + scopeLabel + '\u2026';
        if (percentText) percentText.textContent = '0%';
        if (fill) fill.style.width = '0%';
        if (countsText) countsText.textContent = '';
        if (cancelBtn) cancelBtn.style.display = '';

        _detectionPollInterval = setInterval(function () {
            ApiClient.getJSON(ApiClient.getUrl('CreditsDetector/GetProgress'))
                .then(function (progress) {
                    var percent = Math.round(progress.PercentComplete || 0);
                    if (fill) fill.style.width = percent + '%';
                    if (percentText) percentText.textContent = percent + '%';
                    if (statusText) {
                        statusText.textContent = progress.IsRunning
                            ? ('Detecting: ' + (progress.CurrentItem || '\u2026'))
                            : 'Detection complete';
                    }
                    if (countsText) {
                        countsText.textContent = (progress.ProcessedItems || 0) + '/' + (progress.TotalItems || 0) +
                            ' processed \u00b7 ' + (progress.SuccessfulItems || 0) + ' succeeded \u00b7 ' +
                            (progress.FailedItems || 0) + ' failed \u00b7 ' + (progress.SkippedItems || 0) + ' skipped';
                    }

                    if (!progress.IsRunning) {
                        clearInterval(_detectionPollInterval);
                        _detectionPollInterval = null;
                        if (cancelBtn) cancelBtn.style.display = 'none';

                        toast({
                            type: (progress.FailedItems > 0 && !progress.SuccessfulItems) ? 'error' : 'success',
                            text: 'EmbyCredits detection finished: ' + (progress.SuccessfulItems || 0) + ' succeeded, ' +
                                (progress.FailedItems || 0) + ' failed, ' + (progress.SkippedItems || 0) + ' skipped'
                        });

                        setTimeout(function () {
                            if (bar) bar.style.display = 'none';
                        }, 6000);

                        load();
                    }
                })
                .catch(function (err) {
                    console.error('Error polling credits detection progress:', err);
                    clearInterval(_detectionPollInterval);
                    _detectionPollInterval = null;
                    if (cancelBtn) cancelBtn.style.display = 'none';
                });
        }, 1000);
    }

    function cancelDetection() {
        fetch(ApiClient.getUrl('CreditsDetector/CancelDetection'), {
            method: 'POST',
            headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' }
        }).catch(function () {});
    }

    function load() {
        var statusEl = document.querySelector('#tmeSummaryStatus');
        var tableEl = document.querySelector('#tmeSummaryTable');
        if (statusEl) { statusEl.textContent = 'Loading\u2026'; statusEl.style.display = ''; }
        if (tableEl) tableEl.style.display = 'none';

        loading.show();

        loadDetectionConfig().then(function () {
            return ApiClient.getJSON(ApiClient.getUrl('TimeMarkEdit/GetSummary'));
        })
            .then(function (result) {
                loading.hide();
                if (result && result.Success) {
                    _lastData = result;
                    renderTable();
                } else {
                    if (statusEl) statusEl.textContent = 'Failed to load summary.';
                    toast({ type: 'error', text: (result && result.Message) || 'Failed to load summary' });
                }
            })
            .catch(function (err) {
                loading.hide();
                console.error('TimeMarkEdit Summary error:', err);
                if (statusEl) statusEl.textContent = 'Failed to load summary.';
                toast({ type: 'error', text: 'Failed to load summary' });
            });
    }

    return function (view, params) {
        var body = view.querySelector('#tmeSummaryBody');

        body.addEventListener('click', function (e) {
            var detectBtn = e.target.closest('.tme-detect-action');
            if (detectBtn) {
                e.stopPropagation();
                runDetection(detectBtn.getAttribute('data-scope'), detectBtn.dataset);
                return;
            }

            var seasonRow = e.target.closest('.tme-summary-season-row');
            if (seasonRow) {
                toggleSeason(seasonRow.getAttribute('data-season-id'));
                return;
            }

            var seriesRow = e.target.closest('.tme-summary-series-row');
            if (seriesRow) {
                toggleSeries(seriesRow.getAttribute('data-series-id'));
            }
        });

        var cancelBtn = view.querySelector('#btnCancelTmeDetection');
        if (cancelBtn) cancelBtn.addEventListener('click', cancelDetection);

        view.addEventListener('viewshow', function () {
            mainTabsManager.setTabs(this, 1, getTabList);
            load();
        });
    };
});
