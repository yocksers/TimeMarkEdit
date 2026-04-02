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
            }
        ];
    }

    function pad(n, len) { return String(n).padStart(len, '0'); }

    function pctBar(count, total, fillClass) {
        var pct = total > 0 ? Math.min(100, Math.round(count / total * 100)) : 0;
        return '<span class="tme-pct-bar-wrap">' +
            '<span class="tme-pct-bar"><span class="' + fillClass + '" style="width:' + pct + '%;"></span></span>' +
            '<span>' + count + ' <span style="opacity:0.5;">(' + pct + '%)</span></span>' +
            '</span>';
    }

    function render(data) {
        var body = document.querySelector('#tmeSummaryBody');
        body.innerHTML = '';

        if (!data || !data.Series || data.Series.length === 0) {
            document.querySelector('#tmeSummaryStatus').textContent = 'No TV series found.';
            return;
        }

        data.Series.forEach(function (series) {
            var seriesPct = series.EpisodeCount > 0
                ? Math.floor(series.IntroCount / series.EpisodeCount * 100)
                : 0;

            var seriesTr = document.createElement('tr');
            seriesTr.className = 'tme-summary-series-row';
            seriesTr.innerHTML =
                '<td colspan="5">' +
                    escapeHtml(series.Name) +
                    '<span class="tme-series-badge">' +
                        series.EpisodeCount + ' episode' + (series.EpisodeCount !== 1 ? 's' : '') +
                        ' &nbsp;&middot;&nbsp; intro ' + seriesPct + '%' +
                    '</span>' +
                '</td>';
            body.appendChild(seriesTr);

            (series.Seasons || []).forEach(function (season) {
                var tr = document.createElement('tr');
                tr.className = 'tme-summary-season-row';
                tr.innerHTML =
                    '<td style="padding-left:1.8em;opacity:0.85;">' + escapeHtml(season.Name) + '</td>' +
                    '<td style="text-align:center;">' + season.EpisodeCount + '</td>' +
                    '<td>' + pctBar(season.IntroCount, season.EpisodeCount, 'tme-pct-fill-intro') + '</td>' +
                    '<td style="text-align:center;"><span class="tme-avg">' + escapeHtml(season.AvgIntro) + '</span></td>' +
                    '<td>' + pctBar(season.CreditsCount, season.EpisodeCount, 'tme-pct-fill-credits') + '</td>';
                body.appendChild(tr);
            });
        });

        document.querySelector('#tmeSummaryStatus').style.display = 'none';
        document.querySelector('#tmeSummaryTable').style.display = '';
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function load() {
        var statusEl = document.querySelector('#tmeSummaryStatus');
        var tableEl = document.querySelector('#tmeSummaryTable');
        if (statusEl) { statusEl.textContent = 'Loading\u2026'; statusEl.style.display = ''; }
        if (tableEl) tableEl.style.display = 'none';

        loading.show();

        ApiClient.getJSON(ApiClient.getUrl('TimeMarkEdit/GetSummary'))
            .then(function (result) {
                loading.hide();
                if (result && result.Success) {
                    render(result);
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
        view.addEventListener('viewshow', function () {
            mainTabsManager.setTabs(this, 1, getTabList);
            load();
        });
    };
});
