import { readFileSync } from 'node:fs';
import { JSDOM } from 'jsdom';

const source = readFileSync(new URL('../../Web/markBroken.js', import.meta.url), 'utf8');
const SERVER = 'http://jellyfin.test/';
const USER_ID = 'user1';
const DEVICE_ID = 'device1';
const PERMISSION = 'GET SonarrFlag/Permission';

/** A route handler that makes the API call fail like ApiClient does: rejecting with a fetch Response. */
export function fail(message) {
    return function () {
        const error = new Error('Request failed');
        error.json = () => message === undefined ? Promise.reject(new Error('no body')) : Promise.resolve({ Message: message });
        throw error;
    };
}

export function episode(id, extra = {}) {
    return { Id: id, Type: 'Episode', Name: 'Pilot', SeriesName: 'The Show', ParentIndexNumber: 1, IndexNumber: 2, ...extra };
}

/**
 * Boots the plugin script in a fresh jsdom window with a fake Jellyfin ApiClient.
 * `routes` maps "METHOD path" to a response value or a function receiving the request.
 * `admin` is what the permission lookup answers, unless `routes` overrides "GET SonarrFlag/Permission".
 * `items` maps item ids to what ApiClient.getItem returns; `nowPlaying` is this device's session item.
 */
export function createClient({ hash = '#/home', admin = true, routes = {}, items = {}, nowPlaying = null } = {}) {
    const dom = new JSDOM('<!doctype html><html><body></body></html>', {
        url: SERVER + 'web/' + hash,
        runScripts: 'outside-only',
        pretendToBeVisual: true
    });
    const { window } = dom;
    const { document } = window;
    const requests = [];
    const calls = [];

    function respond(handler, call) {
        try {
            if (handler === undefined) {
                throw new Error('Unexpected request ' + call.method + ' ' + call.path);
            }
            return Promise.resolve(typeof handler === 'function' ? handler(call) : handler);
        } catch (error) {
            return Promise.reject(error);
        }
    }

    window.ApiClient = {
        getUrl: path => SERVER + path,
        getCurrentUserId: () => USER_ID,
        deviceId: () => DEVICE_ID,
        getItem(userId, itemId) {
            calls.push('getItem ' + userId + ' ' + itemId);
            return itemId in client.items ? Promise.resolve(client.items[itemId]) : Promise.reject(new Error('Not found'));
        },
        getSessions(query) {
            calls.push('getSessions ' + query.deviceId);
            return Promise.resolve([{ DeviceId: DEVICE_ID, NowPlayingItem: client.nowPlaying || undefined }]);
        },
        ajax(request) {
            const call = {
                method: request.type,
                path: request.url.slice(SERVER.length),
                body: request.data === undefined ? undefined : JSON.parse(request.data),
                request
            };
            const key = call.method + ' ' + call.path;
            // The permission lookup is logged in `calls`, not `requests`, so tests can assert on the plugin's actions alone.
            if (key === PERMISSION) {
                calls.push('getPermission');
                return respond(key in routes ? routes[key] : { CanMarkBroken: client.admin }, call);
            }
            requests.push(call);
            return respond(routes[key], call);
        }
    };

    const client = {
        window,
        document,
        requests,
        calls,
        routes,
        admin,
        items,
        nowPlaying,

        load() {
            window.eval(source);
        },

        async flush() {
            for (let i = 0; i < 5; i++) {
                await new Promise(resolve => setTimeout(resolve, 0));
            }
        },

        addCard(id, parent = document.body) {
            const card = document.createElement('div');
            card.className = 'card';
            card.setAttribute('data-id', id);
            card.innerHTML = '<div class="cardBox"><img class="cardImage">'
                + '<button class="btnCardOptions" data-action="menu"><span class="material-icons more_vert"></span></button></div>';
            parent.appendChild(card);
            return card;
        },

        addDetailsMoreButton() {
            const button = document.createElement('button');
            button.className = 'btnMoreCommands';
            button.innerHTML = '<span class="material-icons more_vert"></span>';
            document.body.appendChild(button);
            return button;
        },

        /** Mimics the markup of Jellyfin's actionSheet for an item context menu. */
        async openActionSheet(ids = ['addtoplaylist', 'refresh']) {
            const sheet = document.createElement('div');
            sheet.className = 'actionSheet';
            sheet.innerHTML = '<div class="actionSheetContent"><div class="actionSheetScroller">'
                + ids.map(id => '<button is="emby-button" class="listItem actionSheetMenuItem' + (id === 'addtoplaylist' ? ' selected' : '') + '" data-id="' + id + '">'
                    + '<span class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons check"></span>'
                    + '<div class="listItemBody actionsheetListItemBody">'
                    + '<div class="listItemBodyText actionSheetItemText">' + id + '</div>'
                    + '<div class="listItemBodyText secondary">Secondary text</div>'
                    + '</div></button>').join('')
                + '</div></div>';
            document.body.appendChild(sheet);
            await client.flush();
            return sheet;
        },

        /** Mimics the video OSD's bottom button row, plus the video element it controls. */
        async openVideoOsd(src = 'http://jellyfin.test/Videos/1/stream') {
            window.location.hash = '#/video';
            const video = document.createElement('video');
            video.src = src;
            document.body.appendChild(video);

            const osd = document.createElement('div');
            osd.className = 'videoOsdBottom videoOsdBottom-maincontrols';
            osd.innerHTML = '<div class="osdControls"><div class="buttons focuscontainer-x">'
                + ['btnPause', 'btnMute'].map(c => '<button is="paper-icon-button-light" class="' + c + ' autoSize"></button>').join('')
                + '<div class="sliderContainer osdVolumeSliderContainer"></div>'
                + ['btnSubtitles', 'btnAudio', 'btnVideoOsdSettings', 'btnPip', 'btnFullscreen']
                    .map(c => '<button is="paper-icon-button-light" class="' + c + ' autoSize"></button>').join('')
                + '</div></div>';
            document.body.appendChild(osd);
            await client.flush();
            return osd;
        },

        async play(src) {
            const video = document.querySelector('video');
            if (src) {
                video.src = src;
            }
            video.dispatchEvent(new window.Event('playing'));
            await client.flush();
        },

        osdButton() {
            return document.querySelector('.btnSonarrFlag');
        },

        click(element, type = 'click') {
            element.dispatchEvent(new window.MouseEvent(type, { bubbles: true, cancelable: true }));
        },

        menuItem(sheet) {
            return sheet.querySelector('[data-id="sonarrflag-broken"]');
        },

        toasts() {
            return Array.from(document.body.children)
                .filter(el => el.style.position === 'fixed' && el.style.zIndex === '999999')
                .map(el => el.textContent);
        },

        dialog() {
            return document.querySelector('.sonarrFlagDialogContainer');
        },

        close() {
            window.close();
        }
    };

    client.load();
    return client;
}
