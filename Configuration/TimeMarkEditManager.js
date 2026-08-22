define(['loading', 'toast', 'mainTabsManager'], function (loading, toast, mainTabsManager) {
    'use strict';

    const CHAPTER_TYPES = ['Chapter', 'IntroStart', 'IntroEnd', 'CreditsStart', 'CreditsEnd'];

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

    function isAllLibraries() { return _view.querySelector('#chkAllLibraries').checked; }

    let _view = null;
    let _navStack = [];
    let _currentEpisodeId = null;
    let _currentEpisodeDisplayName = '';
    let _currentEpisodeRuntimeTicks = 0;
    let _isDirty = false;
    let _isSearchMode = false;
    let _searchTimeout = null;
    let _allEpisodeItems = null;
    let _fetchGeneration = 0;
    let _dragSrcRow = null;
    let _currentItemIsEpisode = false;
    let _mkvMode = false;
    let _introDbConfigured = false;
    let _currentSeriesId = null;
    let _currentSeasonNumber = null;
    let _creditsDetectionSkipExisting = true;
    let _creditsDetectionEnabled = true;
    let _creditsDetectionPollInterval = null;

    function q(id) { return _view.querySelector('#' + id); }

    function pad(n, len) { return String(n).padStart(len, '0'); }

    function ticksToTime(ticks) {
        var totalMs = Math.floor(ticks / 10000);
        var ms = totalMs % 1000;
        var totalSecs = Math.floor(totalMs / 1000);
        var ss = totalSecs % 60;
        var mm = Math.floor(totalSecs / 60) % 60;
        var hh = Math.floor(totalSecs / 3600);
        return pad(hh, 2) + ':' + pad(mm, 2) + ':' + pad(ss, 2) + '.' + pad(ms, 3);
    }

    function hmsmsToTicks(hh, mm, ss, ms) {
        var totalMs = (hh * 3600 + mm * 60 + ss) * 1000 + ms;
        return totalMs * 10000;
    }

    function formatRuntime(ticks) {
        var totalSecs = Math.floor(ticks / 10000000);
        var h = Math.floor(totalSecs / 3600);
        var m = Math.floor((totalSecs % 3600) / 60);
        var s = totalSecs % 60;
        return h > 0
            ? h + ':' + pad(m, 2) + ':' + pad(s, 2)
            : pad(m, 2) + ':' + pad(s, 2);
    }

    function fetchAllItems(params, callback) {
        var allItems = [];
        var pageSize = 1000;
        function fetchPage(startIndex) {
            var pageParams = Object.assign({}, params, { StartIndex: startIndex, Limit: pageSize });
            ApiClient.getJSON(ApiClient.getUrl('Items', pageParams))
                .then(function (response) {
                    var items = response.Items || [];
                    allItems = allItems.concat(items);
                    if (items.length === pageSize && allItems.length < (response.TotalRecordCount || 0)) {
                        fetchPage(startIndex + pageSize);
                    } else {
                        callback(null, allItems);
                    }
                })
                .catch(function (err) { callback(err, null); });
        }
        fetchPage(0);
    }

    function escapeAttr(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/"/g, '&quot;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }

    function renderPath() {
        var pathEl = q('chapterBrowserPath');
        var html = '<span class="chapter-path-crumb" data-nav-idx="-1">[root]</span>';
        _navStack.forEach(function (item, idx) {
            html += ' <span style="opacity:0.35;margin:0 2px;">/</span>';
            html += '<span class="chapter-path-crumb" data-nav-idx="' + idx + '">[' + escapeAttr(item.name) + ']</span>';
        });
        pathEl.innerHTML = 'Path &nbsp; ' + html;

        pathEl.querySelectorAll('.chapter-path-crumb').forEach(function (el) {
            el.addEventListener('click', function () {
                var idx = parseInt(el.getAttribute('data-nav-idx'));
                navigateToIndex(idx);
            });
        });
    }

    function navigateToIndex(idx) {
        if (idx < 0) {
            _navStack = [];
        } else {
            _navStack = _navStack.slice(0, idx + 1);
        }
        _isSearchMode = false;
        var searchEl = q('chapterBrowserSearch');
        if (searchEl) searchEl.value = '';
        loadCurrentLevel();
    }

    function renderBrowserList(items) {
        var listEl = q('chapterBrowserList');
        listEl.innerHTML = '';

        if (!items || items.length === 0) {
            listEl.innerHTML = '<div style="text-align:center;padding:2em 0.5em;opacity:0.38;font-size:0.85em;">No items found</div>';
            return;
        }

        var hasMovies = items.some(function (i) { return i.Type === 'Movie'; });
        var hasEpisodes = items.some(function (i) { return i.Type === 'Episode'; });
        var needsGroupHeaders = hasMovies && hasEpisodes;
        var lastGroupType = null;

        items.forEach(function (item) {
            var isLeaf = item.Type === 'Episode' || item.Type === 'Movie';

            if (needsGroupHeaders && isLeaf && item.Type !== lastGroupType) {
                var header = document.createElement('div');
                header.className = 'chapter-browser-group-header';
                header.textContent = item.Type === 'Movie' ? 'Movies' : 'Episodes';
                listEl.appendChild(header);
                lastGroupType = item.Type;
            }
            var div = document.createElement('div');
            div.className = 'chapter-browser-item ' + (isLeaf ? 'is-episode' : 'is-folder');

            if (item.Id === _currentEpisodeId) {
                div.classList.add('selected');
            }

            var label = item.Name || '';
            if (item.Type === 'Episode') {
                var ep = item.IndexNumber != null ? pad(item.IndexNumber, 2) : null;
                var sn = item.ParentIndexNumber != null ? pad(item.ParentIndexNumber, 2) : null;
                if (sn && ep) label = 'S' + sn + 'E' + ep + ' - ' + label;
                else if (ep) label = ep + ' - ' + label;
            }

            var iconSpan = document.createElement('span');
            iconSpan.className = 'chapter-item-icon';
            var labelSpan = document.createElement('span');
            labelSpan.className = 'chapter-item-label';
            labelSpan.textContent = label;
            labelSpan.title = label;

            div.appendChild(iconSpan);
            div.appendChild(labelSpan);

            div.addEventListener('click', function () {
                if (isLeaf) {
                    _view.querySelectorAll('.chapter-browser-item').forEach(function (el) {
                        el.classList.remove('selected');
                    });
                    div.classList.add('selected');
                    loadEpisodeChapters(item.Id, label);
                } else {
                    _navStack.push({ id: item.Id, name: item.Name, type: item.Type });
                    loadCurrentLevel();
                }
            });

            listEl.appendChild(div);
        });
    }

    function readFilterState() {
        var noChaptersOnly = q('chapterFilterNoChapters').checked;
        var maxCountRaw = q('chapterFilterMaxCount').value.trim();
        var minGapRaw = q('chapterFilterMinGap').value.trim();
        var minRuntimeRaw = q('chapterFilterMinRuntime').value.trim();
        var introFilter = q('chapterFilterIntro').value;
        var creditsFilter = q('chapterFilterCredits').value;
        var hasMaxCount = maxCountRaw !== '' && !isNaN(parseInt(maxCountRaw, 10));
        var hasMinGap = minGapRaw !== '' && !isNaN(parseInt(minGapRaw, 10));
        var hasMinRuntime = minRuntimeRaw !== '' && !isNaN(parseInt(minRuntimeRaw, 10));
        return {
            noChaptersOnly: noChaptersOnly,
            hasMaxCount: hasMaxCount,
            maxCount: hasMaxCount ? parseInt(maxCountRaw, 10) : -1,
            hasMinGap: hasMinGap,
            minGapSeconds: hasMinGap ? parseInt(minGapRaw, 10) : -1,
            hasMinRuntime: hasMinRuntime,
            minRuntimeSeconds: hasMinRuntime ? parseInt(minRuntimeRaw, 10) * 60 : -1,
            introFilter: introFilter,
            creditsFilter: creditsFilter,
            isActive: noChaptersOnly || hasMaxCount || hasMinGap || hasMinRuntime || introFilter !== '' || creditsFilter !== ''
        };
    }

    function loadCurrentLevel() {
        renderPath();
        var listEl = q('chapterBrowserList');
        listEl.innerHTML = '<div style="text-align:center;padding:2em 0.5em;opacity:0.38;font-size:0.85em;">Loading...</div>';

        var f = readFilterState();
        var anyFilterActive = f.isActive;

        if (_navStack.length === 0) {
            if (anyFilterActive) {
                fetchAllEpisodesAndFilter(null);
                return;
            }
            _allEpisodeItems = null;
            ApiClient.getJSON(ApiClient.getUrl('Library/MediaFolders'))
                .then(function (response) {
                    var libs = (response.Items || []).filter(function (l) {
                        if (isAllLibraries()) return true;
                        return l.CollectionType === 'tvshows' || l.CollectionType === 'mixed' || !l.CollectionType;
                    }).sort(function (a, b) { return a.Name.localeCompare(b.Name); });
                    renderBrowserList(libs);
                })
                .catch(function () { renderBrowserList([]); });
            return;
        }

        var current = _navStack[_navStack.length - 1];
        var includeTypes;
        var sortBy = 'SortName';

        if (current.type === 'CollectionFolder' || current.type === 'UserView') {
            includeTypes = isAllLibraries() ? 'Series,Movie' : 'Series';
        } else if (current.type === 'Series') {
            includeTypes = 'Season';
            sortBy = 'IndexNumber';
        } else if (current.type === 'Season') {
            includeTypes = 'Episode';
            sortBy = 'IndexNumber';
        } else {
            _allEpisodeItems = null;
            renderBrowserList([]);
            return;
        }

        var isEpisodeLevel = includeTypes === 'Episode';
        var isMixedLeafLevel = includeTypes === 'Series,Movie';

        if (!isEpisodeLevel && !isMixedLeafLevel && anyFilterActive) {
            fetchAllEpisodesAndFilter(current.id);
            return;
        }

        if (!isEpisodeLevel && !isMixedLeafLevel) _allEpisodeItems = null;

        var params = {
            ParentId: current.id,
            IncludeItemTypes: includeTypes,
            Recursive: isMixedLeafLevel,
            SortBy: sortBy,
            SortOrder: 'Ascending',
            Fields: (isEpisodeLevel || isMixedLeafLevel) ? 'ParentIndexNumber,IndexNumber,Chapters,RunTimeTicks' : 'ParentIndexNumber,IndexNumber'
        };

        fetchAllItems(params, function (err, items) {
            if (err) { renderBrowserList([]); return; }
            if (isEpisodeLevel) {
                    _allEpisodeItems = items.slice().sort(sortEpisodes);
                    applyBrowserFilter();
                } else if (isMixedLeafLevel) {
                    var movies = items.filter(function (i) { return i.Type === 'Movie'; });
                    var nonMovies = items.filter(function (i) { return i.Type !== 'Movie'; });
                    if (anyFilterActive) {
                        _allEpisodeItems = movies;
                        applyBrowserFilter(nonMovies);
                    } else {
                        renderBrowserList(items);
                    }
                } else {
                    renderBrowserList(items);
                }
        });
    }

    function fetchAllEpisodesAndFilter(parentId) {
        _allEpisodeItems = null;
        var listEl = q('chapterBrowserList');
        listEl.innerHTML = '<div style="text-align:center;padding:2em 0.5em;opacity:0.38;font-size:0.85em;">Filtering…</div>';

        _fetchGeneration++;
        var myGeneration = _fetchGeneration;

        var f = readFilterState();
        var params = {
            AllLibraries: isAllLibraries(),
            NoChaptersOnly: f.noChaptersOnly,
            MaxChapterCount: f.maxCount,
            MinGapSeconds: f.minGapSeconds,
            MinRuntimeSeconds: f.minRuntimeSeconds,
            IntroFilter: f.introFilter,
            CreditsFilter: f.creditsFilter
        };
        if (parentId) params.ParentId = parentId;

        ApiClient.getJSON(ApiClient.getUrl('TimeMarkEdit/FilterEpisodes', params))
            .then(function (response) {
                if (_fetchGeneration !== myGeneration) return;
                renderBrowserList(response && response.Success ? (response.Items || []) : []);
            })
            .catch(function () {
                if (_fetchGeneration !== myGeneration) return;
                renderBrowserList([]);
            });
    }

    function applyBrowserFilter(prependItems) {
        if (!_allEpisodeItems) return;

        var f = readFilterState();
        var maxCount = f.hasMaxCount ? f.maxCount : null;
        var minGapTicks = f.hasMinGap ? f.minGapSeconds * 10000000 : null;
        var minRuntimeTicks = f.hasMinRuntime ? f.minRuntimeSeconds * 10000000 : null;

        if (!f.isActive) {
            renderBrowserList((prependItems || []).concat(_allEpisodeItems.slice().sort(sortEpisodes)));
            return;
        }

        var filtered = _allEpisodeItems.filter(function (item) {
            var chapters = item.Chapters || [];
            var count = chapters.length;

            if (f.noChaptersOnly && count !== 0) return false;
            if (f.hasMaxCount && count >= maxCount) return false;
            if (f.hasMinRuntime && (item.RunTimeTicks || 0) < minRuntimeTicks) return false;
            if (f.introFilter === 'has' && !chapters.some(function (c) { return (c.MarkerType || 'Chapter') === 'IntroStart'; })) return false;
            if (f.introFilter === 'missing' && chapters.some(function (c) { return (c.MarkerType || 'Chapter') === 'IntroStart'; })) return false;
            if (f.creditsFilter === 'has' && !chapters.some(function (c) { return (c.MarkerType || 'Chapter') === 'CreditsStart'; })) return false;
            if (f.creditsFilter === 'missing' && chapters.some(function (c) { return (c.MarkerType || 'Chapter') === 'CreditsStart'; })) return false;
            if (f.hasMinGap) {
                var hasLargeGap = false;
                for (var i = 1; i < chapters.length; i++) {
                    if ((chapters[i].StartPositionTicks - chapters[i - 1].StartPositionTicks) > minGapTicks) {
                        hasLargeGap = true;
                        break;
                    }
                }
                if (!hasLargeGap) return false;
            }

            return true;
        });

        renderBrowserList((prependItems || []).concat(filtered.sort(sortEpisodes)));
    }

    function sortEpisodes(a, b) {
        var sn1 = a.ParentIndexNumber != null ? a.ParentIndexNumber : 9999;
        var sn2 = b.ParentIndexNumber != null ? b.ParentIndexNumber : 9999;
        if (sn1 !== sn2) return sn1 - sn2;
        var ep1 = a.IndexNumber != null ? a.IndexNumber : 9999;
        var ep2 = b.IndexNumber != null ? b.IndexNumber : 9999;
        return ep1 - ep2;
    }

    function renderTimeline(chapters) {
        var timelineEl = q('chapterTimeline');
        if (!timelineEl) return;

        if (!_currentEpisodeRuntimeTicks || _currentEpisodeRuntimeTicks <= 0 || !chapters || chapters.length === 0) {
            timelineEl.style.display = 'none';
            return;
        }

        var duration = _currentEpisodeRuntimeTicks;
        var barHtml = '';

        chapters.forEach(function (c) {
            var pct = Math.min(99.5, Math.max(0.5, c.StartPositionTicks / duration * 100));
            var typeLower = (c.MarkerType || 'chapter').toLowerCase();
            var labelTitle = escapeAttr((c.Name || c.MarkerType) + ' \u2014 ' + ticksToTime(c.StartPositionTicks));
            barHtml += '<div class="chapter-timeline-pin chapter-timeline-pin-' + typeLower +
                        '" style="left:' + pct.toFixed(2) + '%;" title="' + labelTitle + '"></div>';
        });

        timelineEl.innerHTML =
            '<div class="chapter-timeline-bar">' + barHtml + '</div>' +
            '<div class="chapter-timeline-labels">' +
            '<span>0:00</span>' +
            '<span>' + formatRuntime(duration) + '</span>' +
            '</div>';
        timelineEl.style.display = 'block';
    }

    function refreshTimeline() {
        renderTimeline(collectChapters());
    }

    function closeSearchDropdown() {
        var dd = q('chapterSearchDropdown');
        if (dd) {
            dd.classList.remove('open');
            dd.innerHTML = '';
        }
    }

    function renderSearchDropdown(items) {
        var dd = q('chapterSearchDropdown');
        if (!dd) return;
        dd.innerHTML = '';

        if (!items || items.length === 0) {
            var empty = document.createElement('div');
            empty.className = 'chapter-search-option';
            empty.style.opacity = '0.45';
            empty.style.cursor = 'default';
            empty.textContent = 'No results found';
            dd.appendChild(empty);
            dd.classList.add('open');
            return;
        }

        items.forEach(function (item) {
            var opt = document.createElement('div');
            opt.className = 'chapter-search-option';
            opt.textContent = item.Name || '';
            opt.title = item.Name || '';
            opt.addEventListener('mousedown', function (e) {
                e.preventDefault();
                clearTimeout(_searchTimeout);
                closeSearchDropdown();
                _isSearchMode = false;
                q('chapterBrowserSearch').value = '';
                if (item.Type === 'Movie' || item.Type === 'Episode') {
                    _view.querySelectorAll('.chapter-browser-item').forEach(function (el) {
                        el.classList.remove('selected');
                    });
                    loadEpisodeChapters(item.Id, item.Name);
                } else {
                    _navStack = [{ id: item.Id, name: item.Name, type: item.Type }];
                    loadCurrentLevel();
                }
            });
            dd.appendChild(opt);
        });

        dd.classList.add('open');
    }

    function handleSearch(query) {
        if (!query || query.trim().length < 2) {
            closeSearchDropdown();
            if (_isSearchMode) {
                _isSearchMode = false;
                loadCurrentLevel();
            }
            return;
        }

        _isSearchMode = true;

        var params = {
            SearchTerm: query.trim(),
            IncludeItemTypes: isAllLibraries() ? 'Series,Movie' : 'Series',
            Recursive: true,
            SortBy: 'SortName',
            SortOrder: 'Ascending',
            Fields: 'SortName',
            Limit: 100
        };

        ApiClient.getJSON(ApiClient.getUrl('Items', params))
            .then(function (response) {
                if (!_isSearchMode) return;
                var items = (response.Items || []).sort(function (a, b) {
                    return (a.Name || '').localeCompare(b.Name || '');
                });
                renderSearchDropdown(items);
            })
            .catch(function () {
                if (_isSearchMode) closeSearchDropdown();
            });
    }

    function loadEpisodeChapters(episodeId, displayName) {
        _currentEpisodeId = episodeId;
        _currentEpisodeDisplayName = displayName || '';
        _currentItemIsEpisode = false;
        _isDirty = false;

        q('chapterEditorEmpty').style.display = 'none';
        q('chapterEditorContent').style.display = 'block';
        q('chapterEpisodeTitle').textContent = displayName;
        q('chapterEpisodeSubtitle').textContent = 'Episode';
        q('chapterEpisodeRuntime').textContent = '';
        q('chapterTableBody').innerHTML =
            '<div style="text-align:center;padding:1.5em;opacity:0.4;font-size:0.85em;">Loading chapters...</div>';
        q('chapterUnsavedNote').style.opacity = '0';
        var applyBtn = q('btnApplyToSeason');
        if (applyBtn) applyBtn.style.display = 'none';
        _currentEpisodeRuntimeTicks = 0;
        _currentSeriesId = null;
        _currentSeasonNumber = null;
        var detectEpisodeBtn = q('btnDetectEpisode');
        var detectSeasonBtn = q('btnDetectSeason');
        var detectSeriesBtn = q('btnDetectSeries');
        if (detectEpisodeBtn) detectEpisodeBtn.style.display = 'none';
        if (detectSeasonBtn) detectSeasonBtn.style.display = 'none';
        if (detectSeriesBtn) detectSeriesBtn.style.display = 'none';

        var introDbBar = q('introDbBar');
        if (introDbBar) introDbBar.style.display = 'flex';
        var introDbStatusEl = q('introDbDownloadStatus');
        if (introDbStatusEl) introDbStatusEl.textContent = '';

        var userId = ApiClient.getCurrentUserId();
        var capturedId = episodeId;
        ApiClient.getJSON(ApiClient.getUrl('Users/' + userId + '/Items/' + episodeId, {
            Fields: 'Chapters'
        }))
        .then(function (response) {
            if (_currentEpisodeId !== capturedId) return;
            if (response.SeriesName) {
                var sub = response.SeriesName;
                if (response.ParentIndexNumber != null) sub += ' · Season ' + response.ParentIndexNumber;
                if (response.IndexNumber != null) sub += ' · Episode ' + response.IndexNumber;
                q('chapterEpisodeSubtitle').textContent = sub;
                _currentItemIsEpisode = true;
                if (applyBtn) applyBtn.style.display = '';
                var introDbSeasonBtn = q('btnDownloadIntroDbSeason');
                var introDbSeriesBtn = q('btnDownloadIntroDbSeries');
                if (introDbSeasonBtn) introDbSeasonBtn.style.display = '';
                if (introDbSeriesBtn) introDbSeriesBtn.style.display = '';
                _currentSeasonNumber = response.ParentIndexNumber != null ? response.ParentIndexNumber : null;
                _currentSeriesId = response.SeriesId || null;
                if (_creditsDetectionEnabled) {
                    if (detectEpisodeBtn) detectEpisodeBtn.style.display = '';
                    if (detectSeasonBtn) detectSeasonBtn.style.display = '';
                    if (detectSeriesBtn) detectSeriesBtn.style.display = '';
                }
                if (!_currentSeriesId) {
                    ApiClient.getJSON(ApiClient.getUrl('Items/' + capturedId + '/Ancestors', { UserId: userId }))
                        .then(function (ancestors) {
                            if (_currentEpisodeId !== capturedId) return;
                            var series = (ancestors || []).find(function (a) { return a.Type === 'Series'; });
                            if (series) _currentSeriesId = series.Id;
                        })
                        .catch(function () {});
                }
            } else if (response.Type === 'Movie') {
                q('chapterEpisodeSubtitle').textContent = 'Movie';
                ApiClient.getJSON(ApiClient.getUrl('Items/' + capturedId + '/Ancestors', { UserId: userId }))
                    .then(function (ancestors) {
                        if (_currentEpisodeId !== capturedId) return;
                        var library = (ancestors || []).find(function (a) {
                            return a.Type === 'CollectionFolder' || a.Type === 'UserView';
                        });
                        if (library) {
                            q('chapterEpisodeSubtitle').textContent = 'Movie · ' + library.Name;
                        }
                    })
                    .catch(function () {});
            }
            if (response.RunTimeTicks) {
                _currentEpisodeRuntimeTicks = response.RunTimeTicks;
                q('chapterEpisodeRuntime').textContent = 'Runtime: ' + formatRuntime(response.RunTimeTicks);
            } else {
                _currentEpisodeRuntimeTicks = 0;
                q('chapterEpisodeRuntime').textContent = '';
            }

            if (_mkvMode) {
                setMkvMode(true);
                loadMkvChapters();
            } else {
                var chapters = (response.Chapters || []).map(function (c) {
                    return {
                        Name: c.Name || '',
                        MarkerType: c.MarkerType || 'Chapter',
                        StartPositionTicks: c.StartPositionTicks || 0
                    };
                });
                renderChapters(chapters);
            }
        })
        .catch(function (err) {
            console.error('Error loading chapters:', err);
            toast({ type: 'error', text: 'Failed to load chapters' });
            q('chapterTableBody').innerHTML =
                '<div style="text-align:center;padding:1.5em;color:#ef9a9a;opacity:0.8;font-size:0.85em;">Failed to load chapters</div>';
        });
    }

    function renderChapters(chapters) {
        var body = q('chapterTableBody');
        body.innerHTML = '';

        if (!chapters || chapters.length === 0) {
            body.innerHTML = '<div style="text-align:center;padding:1.5em;opacity:0.38;font-size:0.85em;">No chapters — use the Add form above to create one.</div>';
            renderTimeline([]);
            return;
        }

        chapters.forEach(function (c) {
            body.appendChild(buildChapterRow(c.Name || '', c.MarkerType || 'Chapter', c.StartPositionTicks));
        });

        renderTimeline(chapters);
    }

    function buildChapterRow(name, markerType, ticks) {
        var timeStr = ticksToTime(ticks);
        var hh = parseInt(timeStr.substring(0, 2), 10);
        var mm = parseInt(timeStr.substring(3, 5), 10);
        var ss = parseInt(timeStr.substring(6, 8), 10);
        var ms = parseInt(timeStr.substring(9, 12), 10);

        var row = document.createElement('div');
        row.className = 'chapter-row';
        row.setAttribute('data-chapter-type', markerType);

        var typeOpts = CHAPTER_TYPES.map(function (t) {
            return '<option value="' + t + '"' + (t === markerType ? ' selected' : '') + '>' + t + '</option>';
        }).join('');

        row.innerHTML =
            '<div class="chapter-drag-handle" title="Drag to reorder">&#x283F;</div>' +
            '<label style="display:flex;align-items:center;justify-content:center;cursor:pointer;">' +
                '<input type="checkbox" class="chapter-row-check" style="cursor:pointer;" />' +
            '</label>' +
            '<input type="text" class="chapter-inp chapter-row-name" value="' + escapeAttr(name) + '" placeholder="Name" />' +
            '<select class="chapter-type-sel chapter-row-type">' + typeOpts + '</select>' +
            '<div class="chapter-time-group">' +
                '<div class="chapter-time-field">' +
                    '<input type="number" class="chapter-inp chapter-time-num chapter-hh" min="0" max="99" value="' + hh + '" />' +
                    '<span class="chapter-time-unit">H</span>' +
                '</div>' +
                '<span class="chapter-time-sep">:</span>' +
                '<div class="chapter-time-field">' +
                    '<input type="number" class="chapter-inp chapter-time-num chapter-mm" min="0" max="59" value="' + mm + '" />' +
                    '<span class="chapter-time-unit">M</span>' +
                '</div>' +
                '<span class="chapter-time-sep">:</span>' +
                '<div class="chapter-time-field">' +
                    '<input type="number" class="chapter-inp chapter-time-num chapter-ss" min="0" max="59" value="' + ss + '" />' +
                    '<span class="chapter-time-unit">S</span>' +
                '</div>' +
                '<span class="chapter-time-sep">.</span>' +
                '<div class="chapter-time-field">' +
                    '<input type="number" class="chapter-inp chapter-time-ms chapter-ms" min="0" max="999" value="' + ms + '" />' +
                    '<span class="chapter-time-unit">ms</span>' +
                '</div>' +
            '</div>' +
            '<button type="button" class="chapter-preview-btn" title="Preview at this timestamp">&#x25B6;</button>' +
            '<button type="button" class="chapter-del-btn" title="Delete this chapter">⊗</button>';

        row.querySelector('.chapter-row-type').addEventListener('change', function () {
            row.setAttribute('data-chapter-type', this.value);
        });

        row.querySelectorAll('input, select').forEach(function (el) {
            el.addEventListener('change', markDirty);
            el.addEventListener('input', markDirty);
        });

        row.querySelector('.chapter-preview-btn').addEventListener('click', function () {
            if (!_currentEpisodeId) return;
            var hhVal = Math.max(0, parseInt(row.querySelector('.chapter-hh').value) || 0);
            var mmVal = Math.max(0, Math.min(59, parseInt(row.querySelector('.chapter-mm').value) || 0));
            var ssVal = Math.max(0, Math.min(59, parseInt(row.querySelector('.chapter-ss').value) || 0));
            var msVal = Math.max(0, Math.min(999, parseInt(row.querySelector('.chapter-ms').value) || 0));
            var startSeconds = hmsmsToTicks(hhVal, mmVal, ssVal, msVal) / 10000000;
            require(['configurationpage?name=TimeMarkEditVideoPlayer'], function (videoPlayer) {
                videoPlayer.openVideoDialog(_currentEpisodeId, startSeconds, {
                    title: 'Preview' + (_currentEpisodeDisplayName ? ' — ' + _currentEpisodeDisplayName : ''),
                    onTimestampSelected: function (chosenSeconds) {
                        var totalMs = Math.round(chosenSeconds * 1000);
                        var newMs = totalMs % 1000;
                        var totalSecs = Math.floor(totalMs / 1000);
                        var newSs = totalSecs % 60;
                        var newMm = Math.floor(totalSecs / 60) % 60;
                        var newHh = Math.floor(totalSecs / 3600);
                        row.querySelector('.chapter-hh').value = newHh;
                        row.querySelector('.chapter-mm').value = newMm;
                        row.querySelector('.chapter-ss').value = newSs;
                        row.querySelector('.chapter-ms').value = newMs;
                        markDirty();
                        refreshTimeline();
                    }
                });
            });
        });

        row.querySelector('.chapter-del-btn').addEventListener('click', function () {
            row.remove();
            refreshEmptyState();
            markDirty();
            refreshTimeline();
        });

        var handle = row.querySelector('.chapter-drag-handle');
        handle.addEventListener('mousedown', function () { row.setAttribute('draggable', 'true'); });
        handle.addEventListener('mouseup', function () { row.removeAttribute('draggable'); });

        row.addEventListener('dragstart', function (e) {
            if (!row.hasAttribute('draggable')) { e.preventDefault(); return; }
            _dragSrcRow = row;
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', '');
            setTimeout(function () { row.classList.add('dragging'); }, 0);
        });

        row.addEventListener('dragend', function () {
            row.removeAttribute('draggable');
            row.classList.remove('dragging');
            var body = q('chapterTableBody');
            if (body) body.querySelectorAll('.chapter-row').forEach(function (r) {
                r.classList.remove('drag-over-top', 'drag-over-bottom');
            });
            _dragSrcRow = null;
        });

        row.addEventListener('dragover', function (e) {
            if (!_dragSrcRow || _dragSrcRow === row) return;
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
            var bounding = row.getBoundingClientRect();
            var body = q('chapterTableBody');
            if (body) body.querySelectorAll('.chapter-row').forEach(function (r) {
                r.classList.remove('drag-over-top', 'drag-over-bottom');
            });
            if (e.clientY > bounding.top + bounding.height / 2) {
                row.classList.add('drag-over-bottom');
            } else {
                row.classList.add('drag-over-top');
            }
        });

        row.addEventListener('drop', function (e) {
            e.preventDefault();
            row.classList.remove('drag-over-top', 'drag-over-bottom');
            if (!_dragSrcRow || _dragSrcRow === row) return;
            var bounding = row.getBoundingClientRect();
            if (e.clientY > bounding.top + row.offsetHeight / 2) {
                row.parentNode.insertBefore(_dragSrcRow, row.nextSibling);
            } else {
                row.parentNode.insertBefore(_dragSrcRow, row);
            }
            markDirty();
        });

        return row;
    }

    function refreshEmptyState() {
        var body = q('chapterTableBody');
        if (body.querySelectorAll('.chapter-row').length === 0) {
            body.innerHTML = '<div style="text-align:center;padding:1.5em;opacity:0.38;font-size:0.85em;">No chapters — use the Add form above to create one.</div>';
        }
    }

    function markDirty() {
        _isDirty = true;
        q('chapterUnsavedNote').style.opacity = '1';
    }

    function addChaptersEveryInterval() {
        if (!_currentEpisodeId) return;

        if (!_currentEpisodeRuntimeTicks) {
            toast({ type: 'warning', text: 'Episode runtime unknown — open an episode first' });
            return;
        }

        var intervalMins = parseInt(q('chapterIntervalMin').value) || 5;
        if (intervalMins < 1) intervalMins = 1;
        var prefix = q('chapterIntervalPrefix').value.trim() || 'Chapter';

        var intervalTicks = intervalMins * 60 * 10000000;
        var body = q('chapterTableBody');

        var placeholder = body.querySelector('div:not(.chapter-row)');
        if (placeholder) placeholder.remove();

        var count = 1;
        var t = 0;
        var added = 0;
        while (t < _currentEpisodeRuntimeTicks) {
            body.appendChild(buildChapterRow(prefix + ' ' + count, 'Chapter', t));
            count++;
            t += intervalTicks;
            added++;
        }

        markDirty();
        toast({ type: 'success', text: added + ' chapter' + (added === 1 ? '' : 's') + ' generated' });
        refreshTimeline();
    }

    function addChapter() {
        if (!_currentEpisodeId) return;

        var name = q('chapterNewName').value;
        var type = q('chapterNewType').value;
        var hh  = Math.max(0, parseInt(q('chapterNewHH').value) || 0);
        var mm  = Math.max(0, Math.min(59, parseInt(q('chapterNewMM').value) || 0));
        var ss  = Math.max(0, Math.min(59, parseInt(q('chapterNewSS').value) || 0));
        var ms  = Math.max(0, Math.min(999, parseInt(q('chapterNewMS').value) || 0));
        var ticks = hmsmsToTicks(hh, mm, ss, ms);

        var body = q('chapterTableBody');
        var placeholder = body.querySelector('div:not(.chapter-row)');
        if (placeholder) placeholder.remove();

        body.appendChild(buildChapterRow(name, type, ticks));

        q('chapterNewName').value = '';
        q('chapterNewType').value = 'Chapter';
        q('chapterNewHH').value = '0';
        q('chapterNewMM').value = '0';
        q('chapterNewSS').value = '0';
        q('chapterNewMS').value = '0';
        q('chapterNewName').focus();

        markDirty();
        refreshTimeline();
    }

    function deleteSelected() {
        var body = q('chapterTableBody');
        var checked = body.querySelectorAll('.chapter-row-check:checked');
        if (checked.length === 0) {
            toast({ type: 'warning', text: 'No chapters selected' });
            return;
        }
        checked.forEach(function (cb) { cb.closest('.chapter-row').remove(); });
        refreshEmptyState();
        markDirty();
        refreshTimeline();
    }

    function collectChapters() {
        var rows = q('chapterTableBody').querySelectorAll('.chapter-row');
        var chapters = [];
        rows.forEach(function (row) {
            var name       = row.querySelector('.chapter-row-name').value;
            var markerType = row.querySelector('.chapter-row-type').value;
            var hh  = Math.max(0, parseInt(row.querySelector('.chapter-hh').value) || 0);
            var mm  = Math.max(0, Math.min(59, parseInt(row.querySelector('.chapter-mm').value) || 0));
            var ss  = Math.max(0, Math.min(59, parseInt(row.querySelector('.chapter-ss').value) || 0));
            var ms  = Math.max(0, Math.min(999, parseInt(row.querySelector('.chapter-ms').value) || 0));
            chapters.push({
                Name: name,
                MarkerType: markerType,
                StartPositionTicks: hmsmsToTicks(hh, mm, ss, ms)
            });
        });
        chapters.sort(function (a, b) { return a.StartPositionTicks - b.StartPositionTicks; });
        return chapters;
    }

    function saveChapters() {
        if (!_currentEpisodeId) return;

        var chapters = collectChapters();
        loading.show();

        fetch(ApiClient.getUrl('TimeMarkEdit/SaveEpisodeChapters', { EpisodeId: _currentEpisodeId }), {
            method: 'POST',
            headers: {
                'X-Emby-Token': ApiClient.accessToken(),
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                EpisodeId: _currentEpisodeId,
                Chapters: chapters
            })
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            loading.hide();
            if (result.Success) {
                _isDirty = false;
                q('chapterUnsavedNote').style.opacity = '0';
                toast({ type: 'success', text: 'Saved ' + chapters.length + ' chapter(s)' });
                renderChapters(chapters);
            } else {
                toast({ type: 'error', text: 'Save failed: ' + (result.Message || 'Unknown error') });
            }
        })
        .catch(function (err) {
            loading.hide();
            console.error('Error saving chapters:', err);
            toast({ type: 'error', text: 'Failed to save chapters' });
        });
    }

    function applyToSeason() {
        if (!_currentEpisodeId || !_currentItemIsEpisode) return;

        var chapters = collectChapters();
        var markers = chapters.filter(function (c) { return c.MarkerType !== 'Chapter'; });

        if (markers.length === 0) {
            toast({ type: 'warning', text: 'No special markers (IntroStart, IntroEnd, CreditsStart) found — nothing to apply' });
            return;
        }

        var markerDesc = markers.map(function (m) {
            return m.MarkerType + ' @ ' + ticksToTime(m.StartPositionTicks);
        }).join('\n');

        if (!confirm('Apply to entire season?\n\n' + markerDesc + '\n\nThis will save the current episode and overwrite matching marker types in all other episodes of the same season.')) {
            return;
        }

        loading.show();

        fetch(ApiClient.getUrl('TimeMarkEdit/SaveEpisodeChapters', { EpisodeId: _currentEpisodeId }), {
            method: 'POST',
            headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ EpisodeId: _currentEpisodeId, Chapters: chapters })
        })
        .then(function (r) { return r.json(); })
        .then(function (saveResult) {
            if (!saveResult.Success) {
                loading.hide();
                toast({ type: 'error', text: 'Save failed: ' + (saveResult.Message || 'Unknown error') });
                return;
            }
            _isDirty = false;
            q('chapterUnsavedNote').style.opacity = '0';

            return fetch(ApiClient.getUrl('TimeMarkEdit/ApplySeasonMarks', {}), {
                method: 'POST',
                headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' },
                body: JSON.stringify({ EpisodeId: _currentEpisodeId })
            })
            .then(function (r) { return r.json(); })
            .then(function (applyResult) {
                loading.hide();
                if (applyResult.Success) {
                    toast({ type: 'success', text: applyResult.Message || 'Applied to season' });
                    renderChapters(chapters);
                } else {
                    toast({ type: 'error', text: applyResult.Message || 'Failed to apply to season' });
                }
            });
        })
        .catch(function (err) {
            loading.hide();
            console.error('Error applying to season:', err);
            toast({ type: 'error', text: 'Failed to apply to season' });
        });
    }

    function loadCreditsDetectionConfig() {
        ApiClient.getJSON(ApiClient.getUrl('TimeMarkEdit/GetCreditsDetectionConfig'))
            .then(function (result) {
                _creditsDetectionSkipExisting = !!(result && result.SkipExistingMarkers);
                _creditsDetectionEnabled = !(result && result.Enabled === false);
                if (!_creditsDetectionEnabled) {
                    var detectEpisodeBtn = q('btnDetectEpisode');
                    var detectSeasonBtn = q('btnDetectSeason');
                    var detectSeriesBtn = q('btnDetectSeries');
                    if (detectEpisodeBtn) detectEpisodeBtn.style.display = 'none';
                    if (detectSeasonBtn) detectSeasonBtn.style.display = 'none';
                    if (detectSeriesBtn) detectSeriesBtn.style.display = 'none';
                }
            })
            .catch(function () {});
    }

    function runCreditsDetection(scope) {
        if (!_currentEpisodeId || !_currentItemIsEpisode || !_creditsDetectionEnabled) return;

        var url, payload, confirmText, scopeLabel;

        if (scope === 'Episode') {
            url = 'CreditsDetector/ProcessEpisode';
            payload = { ItemId: _currentEpisodeId, SkipExistingMarkers: _creditsDetectionSkipExisting };
            confirmText = 'Start EmbyCredits detection for this episode?';
            scopeLabel = 'this episode';
        } else if (scope === 'Season') {
            if (!_currentSeriesId || _currentSeasonNumber == null) {
                toast({ type: 'error', text: 'Could not determine the series/season for this episode' });
                return;
            }
            url = 'CreditsDetector/ProcessSeason';
            payload = { SeriesId: _currentSeriesId, SeasonNumber: _currentSeasonNumber, SkipExistingMarkers: _creditsDetectionSkipExisting };
            confirmText = 'Start EmbyCredits detection for the entire season?';
            scopeLabel = 'the season';
        } else {
            if (!_currentSeriesId) {
                toast({ type: 'error', text: 'Could not determine the series for this episode' });
                return;
            }
            url = 'CreditsDetector/ProcessSeries';
            payload = { SeriesId: _currentSeriesId, SkipExistingMarkers: _creditsDetectionSkipExisting };
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
                startCreditsDetectionPolling(scopeLabel);
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

    function startCreditsDetectionPolling(scopeLabel) {
        var bar = q('creditsDetectionProgressBar');
        var statusText = q('creditsDetectionStatusText');
        var percentText = q('creditsDetectionPercentText');
        var fill = q('creditsDetectionProgressFill');
        var countsText = q('creditsDetectionCountsText');
        var cancelBtn = q('btnCancelCreditsDetection');

        if (_creditsDetectionPollInterval) clearInterval(_creditsDetectionPollInterval);

        if (bar) bar.style.display = 'block';
        if (statusText) statusText.textContent = 'Starting EmbyCredits detection for ' + scopeLabel + '\u2026';
        if (percentText) percentText.textContent = '0%';
        if (fill) fill.style.width = '0%';
        if (countsText) countsText.textContent = '';
        if (cancelBtn) cancelBtn.style.display = '';

        var refreshEpisodeId = _currentEpisodeId;
        var refreshDisplayName = _currentEpisodeDisplayName;

        _creditsDetectionPollInterval = setInterval(function () {
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
                        clearInterval(_creditsDetectionPollInterval);
                        _creditsDetectionPollInterval = null;
                        if (cancelBtn) cancelBtn.style.display = 'none';

                        toast({
                            type: (progress.FailedItems > 0 && !progress.SuccessfulItems) ? 'error' : 'success',
                            text: 'EmbyCredits detection finished: ' + (progress.SuccessfulItems || 0) + ' succeeded, ' +
                                (progress.FailedItems || 0) + ' failed, ' + (progress.SkippedItems || 0) + ' skipped'
                        });

                        setTimeout(function () {
                            if (bar) bar.style.display = 'none';
                        }, 6000);

                        if (_currentEpisodeId === refreshEpisodeId) {
                            if (_isDirty) {
                                toast({ type: 'warning', text: 'New timestamps may be available \u2014 this episode has unsaved changes, reload it to see them' });
                            } else {
                                loadEpisodeChapters(refreshEpisodeId, refreshDisplayName);
                            }
                        }
                    }
                })
                .catch(function (err) {
                    console.error('Error polling credits detection progress:', err);
                    clearInterval(_creditsDetectionPollInterval);
                    _creditsDetectionPollInterval = null;
                    if (cancelBtn) cancelBtn.style.display = 'none';
                });
        }, 1000);
    }

    function cancelCreditsDetection() {
        fetch(ApiClient.getUrl('CreditsDetector/CancelDetection'), {
            method: 'POST',
            headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' }
        }).catch(function () {});
    }

    function detectEpisode() { runCreditsDetection('Episode'); }
    function detectSeason() { runCreditsDetection('Season'); }
    function detectSeries() { runCreditsDetection('Series'); }

    function setMkvMode(enabled) {
        _mkvMode = enabled;

        var addRows = _view.querySelectorAll('.chapter-add-row');
        var tableHeader = _view.querySelector('.chapter-table-header');
        var embyActionBar = q('embyActionBar');
        var mkvImportBar = q('mkvImportBar');
        var mkvModeBar = q('mkvModeBar');
        var toggleBtn = q('btnToggleMkvMode');
        var toggleLabel = q('btnToggleMkvModeLabel');

        addRows.forEach(function (el) { el.style.display = enabled ? 'none' : ''; });
        if (tableHeader) tableHeader.style.display = enabled ? 'none' : '';
        if (embyActionBar) embyActionBar.style.display = enabled ? 'none' : '';
        if (mkvImportBar) mkvImportBar.style.display = enabled ? 'flex' : 'none';
        if (mkvModeBar) mkvModeBar.style.display = enabled ? 'flex' : 'none';

        var introDbBar = q('introDbBar');
        if (introDbBar) introDbBar.style.display = (!enabled && _currentEpisodeId) ? 'flex' : 'none';

        if (toggleBtn) {
            if (enabled) toggleBtn.classList.add('active');
            else toggleBtn.classList.remove('active');
        }
        if (toggleLabel) toggleLabel.textContent = enabled ? 'View Emby Chapters' : 'View MKV Chapters';

        var importSeasonBtn = q('btnImportMkvSeason');
        var importSeriesBtn = q('btnImportMkvSeries');
        if (importSeasonBtn) importSeasonBtn.style.display = (enabled && _currentItemIsEpisode) ? '' : 'none';
        if (importSeriesBtn) importSeriesBtn.style.display = (enabled && _currentItemIsEpisode) ? '' : 'none';
    }

    function toggleMkvMode() {
        if (!_currentEpisodeId) return;
        var nextMode = !_mkvMode;
        if (nextMode) {
            loadMkvChapters();
        } else {
            setMkvMode(false);
            reloadEmbyChapters();
        }
    }

    function reloadEmbyChapters() {
        var userId = ApiClient.getCurrentUserId();
        var capturedId = _currentEpisodeId;
        q('chapterTableBody').innerHTML =
            '<div style="text-align:center;padding:1.5em;opacity:0.4;font-size:0.85em;">Loading chapters...</div>';

        ApiClient.getJSON(ApiClient.getUrl('Users/' + userId + '/Items/' + capturedId, { Fields: 'Chapters' }))
            .then(function (response) {
                if (_currentEpisodeId !== capturedId) return;
                var chapters = (response.Chapters || []).map(function (c) {
                    return {
                        Name: c.Name || '',
                        MarkerType: c.MarkerType || 'Chapter',
                        StartPositionTicks: c.StartPositionTicks || 0
                    };
                });
                renderChapters(chapters);
            })
            .catch(function () {
                q('chapterTableBody').innerHTML =
                    '<div style="text-align:center;padding:1.5em;color:#ef9a9a;opacity:0.8;font-size:0.85em;">Failed to reload chapters</div>';
            });
    }

    function loadMkvChapters() {
        if (!_currentEpisodeId) return;
        var capturedId = _currentEpisodeId;

        q('chapterTableBody').innerHTML =
            '<div style="text-align:center;padding:1.5em;opacity:0.4;font-size:0.85em;">Reading MKV chapters...</div>';

        var mkvBarNote = q('mkvModeBarNote');
        if (mkvBarNote) mkvBarNote.textContent = 'Reading...';

        setMkvMode(true);

        ApiClient.getJSON(ApiClient.getUrl('TimeMarkEdit/GetMkvChapters', { ItemId: capturedId }))
            .then(function (result) {
                if (_currentEpisodeId !== capturedId) return;

                if (!result.Success) {
                    q('chapterTableBody').innerHTML =
                        '<div style="text-align:center;padding:1.5em;color:#ef9a9a;opacity:0.8;font-size:0.85em;">' +
                        (result.Message || 'Failed to read MKV chapters') + '</div>';
                    if (mkvBarNote) mkvBarNote.textContent = result.Message || 'Error';
                    return;
                }

                if (!result.IsMkv) {
                    q('chapterTableBody').innerHTML =
                        '<div style="text-align:center;padding:1.5em;opacity:0.5;font-size:0.85em;">This file is not an MKV (' + (result.FileExtension || 'unknown type') + '). Embedded chapter reading is only supported for MKV files.</div>';
                    if (mkvBarNote) mkvBarNote.textContent = 'Not an MKV file';
                    return;
                }

                var chapters = result.Chapters || [];
                if (mkvBarNote) mkvBarNote.textContent = chapters.length + ' chapter' + (chapters.length === 1 ? '' : 's') + ' found';

                renderMkvChapters(chapters, capturedId);
            })
            .catch(function () {
                if (_currentEpisodeId !== capturedId) return;
                q('chapterTableBody').innerHTML =
                    '<div style="text-align:center;padding:1.5em;color:#ef9a9a;opacity:0.8;font-size:0.85em;">Failed to read MKV chapters</div>';
                if (mkvBarNote) mkvBarNote.textContent = 'Error';
            });
    }

    function renderMkvChapters(chapters, itemId) {
        var body = q('chapterTableBody');
        body.innerHTML = '';

        if (!chapters || chapters.length === 0) {
            body.innerHTML = '<div style="text-align:center;padding:1.5em;opacity:0.38;font-size:0.85em;">No embedded chapters found in this MKV file.</div>';
            return;
        }

        var headerDiv = document.createElement('div');
        headerDiv.style.cssText = 'display:grid;grid-template-columns:1fr 120px 160px 34px 34px;gap:0.4em;padding:0.3em 0.5em;font-size:0.78em;font-weight:600;opacity:0.55;border-bottom:1px solid rgba(255,255,255,0.14);margin-bottom:3px;';
        headerDiv.innerHTML = '<span>Name</span><span>Type</span><span style="text-align:right;">Timestamp</span><span></span><span></span>';
        body.appendChild(headerDiv);

        chapters.forEach(function (c) {
            var row = document.createElement('div');
            row.className = 'mkv-chapter-row';
            row.style.gridTemplateColumns = '1fr 120px 160px 34px 34px';

            var name = c.Name || '';
            var markerType = c.MarkerType || 'Chapter';

            var nameSpan = document.createElement('span');
            nameSpan.className = 'mkv-chapter-name';
            nameSpan.textContent = name || '(unnamed)';
            nameSpan.title = name;

            var typeColors = {
                'IntroStart':   'rgba(41,128,185,0.22)',
                'IntroEnd':     'rgba(142,68,173,0.22)',
                'CreditsStart': 'rgba(230,126,34,0.22)',
                'Chapter':      'transparent'
            };
            var typeSpan = document.createElement('span');
            typeSpan.style.cssText = 'font-size:0.8em;padding:0.15em 0.4em;border-radius:3px;white-space:nowrap;background:' +
                (typeColors[markerType] || 'transparent') + ';';
            typeSpan.textContent = markerType;

            var timeSpan = document.createElement('span');
            timeSpan.className = 'mkv-chapter-time';
            timeSpan.textContent = c.StartTime || ticksToTime(c.StartPositionTicks || 0);

            var previewBtn = document.createElement('button');
            previewBtn.type = 'button';
            previewBtn.className = 'chapter-preview-btn';
            previewBtn.title = 'Preview at this timestamp';
            previewBtn.innerHTML = '&#x25B6;';
            previewBtn.addEventListener('click', function () {
                if (!itemId) return;
                var startSeconds = (c.StartPositionTicks || 0) / 10000000;
                require(['configurationpage?name=TimeMarkEditVideoPlayer'], function (videoPlayer) {
                    videoPlayer.openVideoDialog(itemId, startSeconds, {
                        title: 'Preview' + (_currentEpisodeDisplayName ? ' \u2014 ' + _currentEpisodeDisplayName : '')
                    });
                });
            });

            var importBtn = document.createElement('button');
            importBtn.type = 'button';
            importBtn.style.cssText = 'background:none;border:none;color:#81c784;cursor:pointer;font-size:1.15em;padding:0.15em 0.25em;border-radius:4px;line-height:1;transition:background 0.12s,color 0.12s;';
            importBtn.title = 'Add this chapter to Emby';
            importBtn.innerHTML = '&#x2B;';
            importBtn.addEventListener('mouseover', function () {
                importBtn.style.background = 'rgba(129,199,132,0.18)';
                importBtn.style.color = '#66bb6a';
            });
            importBtn.addEventListener('mouseout', function () {
                importBtn.style.background = 'none';
                importBtn.style.color = '#81c784';
            });
            importBtn.addEventListener('click', function () {
                importSingleMkvChapter(itemId, c.Name || '', c.StartPositionTicks || 0, markerType, importBtn);
            });

            row.appendChild(nameSpan);
            row.appendChild(typeSpan);
            row.appendChild(timeSpan);
            row.appendChild(previewBtn);
            row.appendChild(importBtn);
            body.appendChild(row);
        });
    }

    function importSingleMkvChapter(itemId, name, ticks, markerType, btn) {
        if (!itemId) return;
        var origHtml = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = '&#x23F3;';

        var userId = ApiClient.getCurrentUserId();
        ApiClient.getJSON(ApiClient.getUrl('Users/' + userId + '/Items/' + itemId, { Fields: 'Chapters' }))
            .then(function (response) {
                var existing = (response.Chapters || []).map(function (c) {
                    return {
                        Name: c.Name || '',
                        MarkerType: c.MarkerType || 'Chapter',
                        StartPositionTicks: c.StartPositionTicks || 0
                    };
                });

                existing.push({ Name: name, MarkerType: markerType, StartPositionTicks: ticks });
                existing.sort(function (a, b) { return a.StartPositionTicks - b.StartPositionTicks; });

                return fetch(ApiClient.getUrl('TimeMarkEdit/SaveEpisodeChapters', { EpisodeId: itemId }), {
                    method: 'POST',
                    headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' },
                    body: JSON.stringify({ EpisodeId: itemId, Chapters: existing })
                })
                .then(function (r) { return r.json(); })
                .then(function (result) {
                    btn.disabled = false;
                    if (result.Success) {
                        btn.innerHTML = '&#x2713;';
                        btn.style.color = '#a5d6a7';
                        toast({ type: 'success', text: '"' + (name || 'Chapter') + '" added to Emby' });
                    } else {
                        btn.innerHTML = origHtml;
                        toast({ type: 'error', text: result.Message || 'Import failed' });
                    }
                });
            })
            .catch(function () {
                btn.disabled = false;
                btn.innerHTML = origHtml;
                toast({ type: 'error', text: 'Failed to import chapter' });
            });
    }

    function importMkvChaptersForItem() {
        if (!_currentEpisodeId) return;
        var capturedId = _currentEpisodeId;
        loading.show();

        fetch(ApiClient.getUrl('TimeMarkEdit/ImportMkvChapters', {}), {
            method: 'POST',
            headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ ItemId: capturedId })
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            loading.hide();
            if (result.Success) {
                toast({ type: 'success', text: result.Message || 'Imported MKV chapters' });
            } else {
                toast({ type: 'error', text: result.Message || 'Import failed' });
            }
        })
        .catch(function () {
            loading.hide();
            toast({ type: 'error', text: 'Failed to import MKV chapters' });
        });
    }

    function importMkvChaptersBulk(scope) {
        if (!_currentEpisodeId || !_currentItemIsEpisode) return;
        var label = scope === 'Series' ? 'series' : 'season';
        if (!confirm('Import MKV embedded chapters for all episodes in this ' + label + '?\n\nThis will replace Emby chapters with the embedded MKV chapters for every episode that has them.')) return;

        loading.show();

        fetch(ApiClient.getUrl('TimeMarkEdit/ImportMkvChaptersBulk', {}), {
            method: 'POST',
            headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ EpisodeId: _currentEpisodeId, Scope: scope })
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            loading.hide();
            if (result.Success) {
                toast({ type: 'success', text: result.Message || ('Imported MKV chapters for ' + label) });
            } else {
                toast({ type: 'error', text: result.Message || 'Bulk import failed' });
            }
        })
        .catch(function () {
            loading.hide();
            toast({ type: 'error', text: 'Failed to bulk import MKV chapters' });
        });
    }

    function importMkvChaptersForSeason() { importMkvChaptersBulk('Season'); }
    function importMkvChaptersForSeries() { importMkvChaptersBulk('Series'); }

    function loadIntroDbConfig() {
        ApiClient.getJSON(ApiClient.getUrl('TimeMarkEdit/GetIntroDbConfig', {}))
            .then(function (result) {
                _introDbConfigured = result.ApiKeyConfigured || false;
                var badge = q('introDbStatusBadge');
                if (badge) {
                    badge.textContent = _introDbConfigured ? 'Upload key set' : 'No upload key';
                    badge.style.background = _introDbConfigured ? 'rgba(82,181,75,0.2)' : 'rgba(255,255,255,0.1)';
                    badge.style.color = _introDbConfigured ? '#7cce76' : '';
                }
                var segIntro = q('introDbSegIntro');
                var segRecap = q('introDbSegRecap');
                var segCredits = q('introDbSegCredits');
                var segPreview = q('introDbSegPreview');
                var overwriteChk = q('introDbOverwrite');
                var segs = result.EnabledSegments || ['intro', 'credits'];
                if (segIntro) segIntro.checked = segs.indexOf('intro') !== -1;
                if (segRecap) segRecap.checked = segs.indexOf('recap') !== -1;
                if (segCredits) segCredits.checked = segs.indexOf('credits') !== -1;
                if (segPreview) segPreview.checked = segs.indexOf('preview') !== -1;
                if (overwriteChk) overwriteChk.checked = result.OverwriteExisting !== false;
            })
            .catch(function () {});
    }

    function saveIntroDbConfig() {
        var apiKey = (q('introDbApiKey').value || '').trim();
        var overwriteExisting = q('introDbOverwrite') ? q('introDbOverwrite').checked : true;
        var enabledSegments = [];
        if (q('introDbSegIntro') && q('introDbSegIntro').checked) enabledSegments.push('intro');
        if (q('introDbSegRecap') && q('introDbSegRecap').checked) enabledSegments.push('recap');
        if (q('introDbSegCredits') && q('introDbSegCredits').checked) enabledSegments.push('credits');
        if (q('introDbSegPreview') && q('introDbSegPreview').checked) enabledSegments.push('preview');

        fetch(ApiClient.getUrl('TimeMarkEdit/SetIntroDbConfig', {}), {
            method: 'POST',
            headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ ApiKey: apiKey, OverwriteExisting: overwriteExisting, EnabledSegments: enabledSegments })
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            if (result.Success) {
                q('introDbApiKey').value = '';
                toast({ type: 'success', text: 'TheIntroDB configuration saved' });
                loadIntroDbConfig();
            } else {
                toast({ type: 'error', text: result.Message || 'Failed to save configuration' });
            }
        })
        .catch(function () { toast({ type: 'error', text: 'Failed to save configuration' }); });
    }

    function testIntroDbConnection() {
        var statusEl = q('introDbTestStatus');
        if (statusEl) statusEl.textContent = 'Testing...';

        fetch(ApiClient.getUrl('TimeMarkEdit/TestIntroDbConnection', {}), {
            method: 'POST',
            headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' },
            body: '{}'
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            if (statusEl) {
                statusEl.textContent = result.Success ? '✓ ' + (result.Message || 'OK') : '✗ ' + (result.Message || 'Failed');
                statusEl.style.color = result.Success ? '#7cce76' : '#ef9a9a';
            }
            if (result.Success) loadIntroDbConfig();
        })
        .catch(function () {
            if (statusEl) { statusEl.textContent = '✗ Connection error'; statusEl.style.color = '#ef9a9a'; }
        });
    }

    function downloadIntroDb() {
        if (!_currentEpisodeId) return;
        var statusEl = q('introDbDownloadStatus');
        if (statusEl) statusEl.textContent = 'Downloading...';
        loading.show();

        fetch(ApiClient.getUrl('TimeMarkEdit/DownloadIntroDbTimestamps', {}), {
            method: 'POST',
            headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ ItemId: _currentEpisodeId })
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            loading.hide();
            if (statusEl) statusEl.textContent = '';
            if (result.Success) {
                toast({ type: 'success', text: result.Message || 'Timestamps downloaded' });
                reloadEmbyChapters();
            } else {
                toast({ type: 'error', text: result.Message || 'Download failed' });
                if (!result.ApiKeyConfigured) {
                    if (statusEl) { statusEl.textContent = 'API key not configured'; statusEl.style.color = '#ef9a9a'; }
                }
            }
        })
        .catch(function () {
            loading.hide();
            if (statusEl) statusEl.textContent = '';
            toast({ type: 'error', text: 'Failed to download timestamps' });
        });
    }

    function downloadIntroDbBulk(scope) {
        if (!_currentEpisodeId || !_currentItemIsEpisode) return;
        var label = scope === 'Series' ? 'series' : 'season';
        if (!confirm('Download timestamps from TheIntroDB for all episodes in this ' + label + '?\n\nThis will overwrite existing Emby chapter markers for each episode.')) return;

        var statusEl = q('introDbDownloadStatus');
        if (statusEl) statusEl.textContent = 'Downloading...';
        loading.show();

        fetch(ApiClient.getUrl('TimeMarkEdit/DownloadIntroDbTimestampsBulk', {}), {
            method: 'POST',
            headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ EpisodeId: _currentEpisodeId, Scope: scope })
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            loading.hide();
            if (statusEl) statusEl.textContent = '';
            if (result.Success) {
                toast({ type: 'success', text: result.Message || ('Downloaded timestamps for ' + label) });
                reloadEmbyChapters();
            } else {
                toast({ type: 'error', text: result.Message || 'Bulk download failed' });
            }
        })
        .catch(function () {
            loading.hide();
            if (statusEl) statusEl.textContent = '';
            toast({ type: 'error', text: 'Failed to bulk download timestamps' });
        });
    }

    function downloadIntroDbSeason() { downloadIntroDbBulk('Season'); }
    function downloadIntroDbSeries() { downloadIntroDbBulk('Series'); }

    function uploadIntroDb() {
        if (!_currentEpisodeId) return;
        var statusEl = q('introDbDownloadStatus');
        if (statusEl) statusEl.textContent = 'Uploading...';
        loading.show();

        fetch(ApiClient.getUrl('TimeMarkEdit/UploadIntroDbTimestamps', {}), {
            method: 'POST',
            headers: { 'X-Emby-Token': ApiClient.accessToken(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ ItemId: _currentEpisodeId })
        })
        .then(function (r) { return r.json(); })
        .then(function (result) {
            loading.hide();
            if (statusEl) statusEl.textContent = '';
            if (result.Success) {
                toast({ type: 'success', text: result.Message || 'Uploaded to TheIntroDB' });
            } else {
                toast({ type: 'error', text: result.Message || 'Upload failed' });
            }
        })
        .catch(function () {
            loading.hide();
            if (statusEl) statusEl.textContent = '';
            toast({ type: 'error', text: 'Failed to upload timestamps' });
        });
    }

    function init(view) {
        _view = view;
        _navStack = [];
        _currentEpisodeId = null;
        _isDirty = false;
        _isSearchMode = false;
        _mkvMode = false;

        loadCurrentLevel();
        loadIntroDbConfig();
        loadCreditsDetectionConfig();

        var btnMkvToggle = q('btnToggleMkvMode');
        if (btnMkvToggle) btnMkvToggle.addEventListener('click', toggleMkvMode);

        var btnImportItem = q('btnImportMkvItem');
        if (btnImportItem) btnImportItem.addEventListener('click', importMkvChaptersForItem);

        var btnImportSeason = q('btnImportMkvSeason');
        if (btnImportSeason) btnImportSeason.addEventListener('click', importMkvChaptersForSeason);

        var btnImportSeries = q('btnImportMkvSeries');
        if (btnImportSeries) btnImportSeries.addEventListener('click', importMkvChaptersForSeries);

        var btnDownloadIntroDb = q('btnDownloadIntroDb');
        if (btnDownloadIntroDb) btnDownloadIntroDb.addEventListener('click', downloadIntroDb);

        var btnDownloadIntroDbSeason = q('btnDownloadIntroDbSeason');
        if (btnDownloadIntroDbSeason) btnDownloadIntroDbSeason.addEventListener('click', downloadIntroDbSeason);

        var btnDownloadIntroDbSeries = q('btnDownloadIntroDbSeries');
        if (btnDownloadIntroDbSeries) btnDownloadIntroDbSeries.addEventListener('click', downloadIntroDbSeries);

        var btnSaveIntroDbConfig = q('btnSaveIntroDbConfig');
        if (btnSaveIntroDbConfig) btnSaveIntroDbConfig.addEventListener('click', saveIntroDbConfig);

        var btnTestIntroDb = q('btnTestIntroDbConnection');
        if (btnTestIntroDb) btnTestIntroDb.addEventListener('click', testIntroDbConnection);

        var btnUploadIntroDb = q('btnUploadIntroDb');
        if (btnUploadIntroDb) btnUploadIntroDb.addEventListener('click', uploadIntroDb);

        var btnDetectEpisode = q('btnDetectEpisode');
        if (btnDetectEpisode) btnDetectEpisode.addEventListener('click', detectEpisode);

        var btnDetectSeason = q('btnDetectSeason');
        if (btnDetectSeason) btnDetectSeason.addEventListener('click', detectSeason);

        var btnDetectSeries = q('btnDetectSeries');
        if (btnDetectSeries) btnDetectSeries.addEventListener('click', detectSeries);

        var btnCancelCreditsDetection = q('btnCancelCreditsDetection');
        if (btnCancelCreditsDetection) btnCancelCreditsDetection.addEventListener('click', cancelCreditsDetection);

        var searchEl = q('chapterBrowserSearch');
        searchEl.addEventListener('input', function () {
            clearTimeout(_searchTimeout);
            var val = searchEl.value;
            if (!val || val.trim().length < 2) {
                if (_isSearchMode) {
                    _isSearchMode = false;
                    loadCurrentLevel();
                }
                return;
            }
            _searchTimeout = setTimeout(function () { handleSearch(val); }, 400);
        });

        searchEl.addEventListener('blur', function () {
            setTimeout(closeSearchDropdown, 150);
        });

        searchEl.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                closeSearchDropdown();
                searchEl.value = '';
                if (_isSearchMode) {
                    _isSearchMode = false;
                    loadCurrentLevel();
                }
            }
        });

        var allLibsChk = q('chkAllLibraries');
        if (allLibsChk) allLibsChk.addEventListener('change', function () {
            _navStack = [];
            _isSearchMode = false;
            searchEl.value = '';
            loadCurrentLevel();
        });

        var filterNoChaps = q('chapterFilterNoChapters');
        if (filterNoChaps) filterNoChaps.addEventListener('change', loadCurrentLevel);

        function setupNumberFilter(inputId, clearBtnId) {
            var inp = q(inputId);
            var btn = q(clearBtnId);
            if (!inp || !btn) return;
            function onInput() {
                btn.style.display = inp.value !== '' ? 'inline' : 'none';
                clearTimeout(_searchTimeout);
                _searchTimeout = setTimeout(loadCurrentLevel, 400);
            }
            inp.addEventListener('input', onInput);
            btn.addEventListener('click', function () {
                inp.value = '';
                btn.style.display = 'none';
                loadCurrentLevel();
            });
        }
        setupNumberFilter('chapterFilterMaxCount', 'chapterFilterMaxCountClear');
        setupNumberFilter('chapterFilterMinGap', 'chapterFilterMinGapClear');
        setupNumberFilter('chapterFilterMinRuntime', 'chapterFilterMinRuntimeClear');

        var filterIntro = q('chapterFilterIntro');
        if (filterIntro) filterIntro.addEventListener('change', loadCurrentLevel);
        var filterCredits = q('chapterFilterCredits');
        if (filterCredits) filterCredits.addEventListener('change', loadCurrentLevel);

        var btnAdd = q('btnAddChapter');
        if (btnAdd) btnAdd.addEventListener('click', addChapter);

        var nameField = q('chapterNewName');
        if (nameField) {
            nameField.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') { e.preventDefault(); addChapter(); }
            });
        }

        var btnDel = q('btnDeleteSelectedChapters');
        if (btnDel) btnDel.addEventListener('click', deleteSelected);

        var btnInterval = q('btnAddChaptersInterval');
        if (btnInterval) btnInterval.addEventListener('click', addChaptersEveryInterval);

        var btnSave = q('btnSaveChapters');
        if (btnSave) btnSave.addEventListener('click', saveChapters);

        var btnApply = q('btnApplyToSeason');
        if (btnApply) btnApply.addEventListener('click', applyToSeason);

        var btnPreviewNew = q('btnPreviewNewChapter');
        if (btnPreviewNew) btnPreviewNew.addEventListener('click', function () {
            if (!_currentEpisodeId) return;
            var hh = Math.max(0, parseInt(q('chapterNewHH').value) || 0);
            var mm = Math.max(0, Math.min(59, parseInt(q('chapterNewMM').value) || 0));
            var ss = Math.max(0, Math.min(59, parseInt(q('chapterNewSS').value) || 0));
            var ms = Math.max(0, Math.min(999, parseInt(q('chapterNewMS').value) || 0));
            var startSeconds = hmsmsToTicks(hh, mm, ss, ms) / 10000000;
            require(['configurationpage?name=TimeMarkEditVideoPlayer'], function (videoPlayer) {
                videoPlayer.openVideoDialog(_currentEpisodeId, startSeconds, {
                    title: 'Preview' + (_currentEpisodeDisplayName ? ' — ' + _currentEpisodeDisplayName : ''),
                    onTimestampSelected: function (chosenSeconds) {
                        var totalMs = Math.round(chosenSeconds * 1000);
                        var newMs = totalMs % 1000;
                        var totalSecs = Math.floor(totalMs / 1000);
                        var newSs = totalSecs % 60;
                        var newMm = Math.floor(totalSecs / 60) % 60;
                        var newHh = Math.floor(totalSecs / 3600);
                        q('chapterNewHH').value = newHh;
                        q('chapterNewMM').value = newMm;
                        q('chapterNewSS').value = newSs;
                        q('chapterNewMS').value = newMs;
                    }
                });
            });
        });
    }

    return function (view, params) {
        init(view);
        view.addEventListener('viewshow', function () {
            mainTabsManager.setTabs(this, 0, getTabList);
        });
    };
});
