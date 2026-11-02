(function () {
    'use strict';

    if (window.__sonarrFlagLoaded) {
        return;
    }
    window.__sonarrFlagLoaded = true;

    const MENU_ITEM_ID = 'sonarrflag-broken';
    const OSD_BUTTON_CLASS = 'btnSonarrFlag';
    const TRIGGER_WINDOW_MS = 3000;
    const SESSION_RETRY_MS = 2000;
    const SESSION_RETRIES = 3;

    let lastTrigger = null;
    let adminCheck = null;
    let lastVideoSrc = null;
    let osdRetryTimer = null;

    function hashParam(name) {
        const hash = window.location.hash || '';
        const query = hash.indexOf('?');
        return query === -1 ? null : new URLSearchParams(hash.substring(query + 1)).get(name);
    }

    function recordTrigger(event) {
        const target = event.target;
        if (!(target instanceof Element) || target.closest('.actionSheet')) {
            return;
        }

        if (target.closest('.btnMoreCommands')) {
            const id = hashParam('id');
            if (id) {
                lastTrigger = { id: id, time: Date.now() };
            }
            return;
        }

        if (event.type === 'click' && !target.closest('[data-action="menu"]')) {
            return;
        }

        const card = target.closest('[data-id]');
        if (card) {
            lastTrigger = { id: card.getAttribute('data-id'), time: Date.now() };
        }
    }

    document.addEventListener('click', recordTrigger, true);
    document.addEventListener('contextmenu', recordTrigger, true);

    /** Resolves to whether the signed-in user is an administrator, cached per user. */
    function isAdmin() {
        const client = window.ApiClient;
        const userId = client.getCurrentUserId();
        if (!adminCheck || adminCheck.userId !== userId) {
            adminCheck = {
                userId: userId,
                promise: client.getCurrentUser().then(function (user) {
                    return !!(user && user.Policy && user.Policy.IsAdministrator);
                }).catch(function () {
                    adminCheck = null;
                    return false;
                })
            };
        }
        return adminCheck.promise;
    }

    function isEpisode(item) {
        return !!item && item.Type === 'Episode';
    }

    function api(method, path, body) {
        const client = window.ApiClient;
        const request = {
            type: method,
            url: client.getUrl(path),
            dataType: 'json'
        };
        if (body !== undefined) {
            request.data = JSON.stringify(body);
            request.contentType = 'application/json';
        }
        return client.ajax(request);
    }

    /** Resolves to the server's error message when ApiClient rejected with a fetch Response. */
    function errorMessage(error) {
        if (!error || typeof error.json !== 'function') {
            return Promise.resolve(null);
        }
        return error.json().then(function (body) {
            return body && body.Message;
        }).catch(function () {
            return null;
        });
    }

    function showToast(text, duration) {
        const toast = document.createElement('div');
        toast.textContent = text;
        Object.assign(toast.style, {
            position: 'fixed',
            left: '50%',
            bottom: '2em',
            transform: 'translateX(-50%)',
            maxWidth: '90%',
            background: '#303030',
            color: '#fff',
            padding: '0.8em 1.4em',
            borderRadius: '0.3em',
            zIndex: 999999,
            boxShadow: '0 2px 6px rgba(0,0,0,.5)'
        });
        document.body.appendChild(toast);
        setTimeout(function () { toast.remove(); }, duration || 3000);
    }

    function pad(number) {
        return number < 10 ? '0' + number : String(number);
    }

    function episodeLabel(item) {
        let label = item.SeriesName || '';
        if (item.ParentIndexNumber != null && item.IndexNumber != null) {
            label += ' S' + pad(item.ParentIndexNumber) + 'E' + pad(item.IndexNumber);
            if (item.IndexNumberEnd != null && item.IndexNumberEnd > item.IndexNumber) {
                label += '-E' + pad(item.IndexNumberEnd);
            }
        }
        return label ? label + ' – ' + item.Name : item.Name;
    }

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function markBroken(item) {
        api('POST', 'SonarrFlag/Broken/' + item.Id).then(function (result) {
            const text = 'Sonarr is searching for a replacement for ' + result.Episode;
            showToast(result.Warning ? text + '. ' + result.Warning : text, result.Warning ? 6000 : 3000);
        }).catch(function (error) {
            errorMessage(error).then(function (message) {
                showToast('Could not mark as broken: ' + (message || 'the request failed.'), 6000);
            });
        });
    }

    function confirmBroken(item) {
        const container = document.createElement('div');
        container.className = 'dialogContainer sonarrFlagDialogContainer';
        Object.assign(container.style, {
            position: 'fixed',
            inset: '0',
            zIndex: 10000,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            background: 'rgba(0, 0, 0, 0.5)'
        });

        container.innerHTML =
            '<style>:where(.sonarrFlagDialog) { background-color: #202020; }</style>'
            + '<div class="focuscontainer dialog formDialog sonarrFlagDialog" role="dialog" aria-modal="true"'
            + ' style="position: relative; width: 90%; max-width: 30em; border-radius: 0.3em; margin: 0;">'
            + '  <div class="formDialogHeader">'
            + '    <button is="paper-icon-button-light" class="btnCancel autoSize" title="Close">'
            + '      <span class="material-icons close" aria-hidden="true"></span>'
            + '    </button>'
            + '    <h3 class="formDialogHeaderTitle">Mark as Broken</h3>'
            + '  </div>'
            + '  <div class="formDialogContent" style="padding: 0.5em 1.5em 1.5em;">'
            + '    <p><strong>' + escapeHtml(episodeLabel(item)) + '</strong></p>'
            + '    <p class="fieldDescription">This will blocklist the current release, delete the file and search Sonarr for a replacement.</p>'
            + '    <button is="emby-button" type="button" class="raised button-submit block btnConfirmBroken">'
            + '      <span>Mark as Broken</span>'
            + '    </button>'
            + '    <button is="emby-button" type="button" class="raised button-cancel block btnCancelBroken">'
            + '      <span>Cancel</span>'
            + '    </button>'
            + '  </div>'
            + '</div>';

        function close() {
            document.removeEventListener('keydown', onKeyDown, true);
            container.remove();
        }

        // Keeps the video player's keyboard shortcuts from firing behind the dialog.
        function onKeyDown(event) {
            event.stopPropagation();
            if (event.key === 'Escape') {
                close();
            }
        }

        container.addEventListener('click', function (event) {
            if (event.target === container || event.target.closest('.btnCancel, .btnCancelBroken')) {
                close();
            } else if (event.target.closest('.btnConfirmBroken')) {
                close();
                markBroken(item);
            }
        });
        document.addEventListener('keydown', onKeyDown, true);

        document.body.appendChild(container);
        container.querySelector('.btnConfirmBroken').focus();
    }

    function buildMenuItem(template) {
        const item = template.cloneNode(true);
        item.setAttribute('data-id', MENU_ITEM_ID);
        item.classList.remove('selected');

        const text = item.querySelector('.actionSheetItemText') || item.querySelector('.listItemBodyText');
        if (text) {
            text.textContent = 'Mark as Broken';
        }

        const icon = item.querySelector('.actionsheetMenuItemIcon');
        if (icon) {
            icon.className = 'actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons heart_broken';
            icon.textContent = '';
        }

        item.querySelectorAll('.listItemBodyText.secondary').forEach(function (el) { el.remove(); });

        return item;
    }

    function enhanceActionSheet(sheet) {
        const trigger = lastTrigger;
        if (!trigger || Date.now() - trigger.time > TRIGGER_WINDOW_MS) {
            return;
        }
        lastTrigger = null;

        if (!sheet.querySelector('.actionSheetMenuItem[data-id="refresh"], .actionSheetMenuItem[data-id="addtoplaylist"], .actionSheetMenuItem[data-id="select"]')) {
            return;
        }

        isAdmin().then(function (admin) {
            const client = window.ApiClient;
            return admin ? client.getItem(client.getCurrentUserId(), trigger.id) : null;
        }).then(function (item) {
            if (!isEpisode(item) || !sheet.isConnected || sheet.querySelector('[data-id="' + MENU_ITEM_ID + '"]')) {
                return;
            }

            const anchor = sheet.querySelector('.actionSheetMenuItem[data-id="addtoplaylist"]');
            const template = anchor || sheet.querySelector('.actionSheetMenuItem');
            if (!template) {
                return;
            }

            const menuItem = buildMenuItem(template);
            menuItem.addEventListener('click', function () { confirmBroken(item); });

            if (anchor) {
                anchor.after(menuItem);
            } else {
                (sheet.querySelector('.actionSheetScroller') || template.parentElement).appendChild(menuItem);
            }
        }).catch(function () {
        });
    }

    /** Resolves to the item playing in this browser, or null. */
    function nowPlaying() {
        return isAdmin().then(function (admin) {
            if (!admin) {
                return null;
            }
            const client = window.ApiClient;
            return client.getSessions({ deviceId: client.deviceId() }).then(function (sessions) {
                const session = (sessions || []).find(function (s) { return s.NowPlayingItem; });
                return session ? session.NowPlayingItem : null;
            });
        }).catch(function () {
            return null;
        });
    }

    /** Shows the OSD button only while an episode is playing. The session can lag behind playback, so retry a few times. */
    function refreshOsdButton(attempt) {
        clearTimeout(osdRetryTimer);
        nowPlaying().then(function (item) {
            const button = document.querySelector('.' + OSD_BUTTON_CLASS);
            if (!button) {
                return;
            }
            button.classList.toggle('hide', !isEpisode(item));
            if (!item && attempt < SESSION_RETRIES) {
                osdRetryTimer = setTimeout(function () { refreshOsdButton(attempt + 1); }, SESSION_RETRY_MS);
            }
        });
    }

    function enhanceOsd() {
        if (!(window.location.hash || '').startsWith('#/video')) {
            return;
        }

        const buttons = document.querySelector('.videoOsdBottom .buttons');
        if (!buttons || buttons.querySelector('.' + OSD_BUTTON_CLASS)) {
            return;
        }

        const template = document.createElement('div');
        template.innerHTML = '<button is="paper-icon-button-light" class="' + OSD_BUTTON_CLASS + ' autoSize hide" title="Mark as Broken">'
            + '<span class="xlargePaperIconButton material-icons heart_broken" aria-hidden="true"></span>'
            + '</button>';
        const button = template.firstElementChild;

        button.addEventListener('click', function (event) {
            event.stopPropagation();
            const video = document.querySelector('video');
            if (video && !video.paused) {
                video.pause();
            }
            nowPlaying().then(function (item) {
                if (isEpisode(item)) {
                    confirmBroken(item);
                } else {
                    showToast('Only episodes can be marked as broken');
                }
            });
        });

        const anchor = buttons.querySelector('.btnVideoOsdSettings') || buttons.querySelector('.btnFullscreen');
        if (anchor) {
            anchor.before(button);
        } else {
            buttons.appendChild(button);
        }

        refreshOsdButton(0);
    }

    // Media events don't bubble, but they can be captured. A new source means a new item started playing.
    document.addEventListener('playing', function (event) {
        const video = event.target;
        if (!(video instanceof HTMLVideoElement)) {
            return;
        }
        const src = video.currentSrc || video.src;
        if (src === lastVideoSrc) {
            return;
        }
        lastVideoSrc = src;
        refreshOsdButton(0);
    }, true);

    new MutationObserver(function (mutations) {
        enhanceOsd();
        mutations.forEach(function (mutation) {
            mutation.addedNodes.forEach(function (node) {
                if (!(node instanceof Element)) {
                    return;
                }
                const sheet = node.classList.contains('actionSheet') ? node : node.querySelector('.actionSheet');
                if (sheet) {
                    enhanceActionSheet(sheet);
                }
            });
        });
    }).observe(document.body, { childList: true, subtree: true });
})();
