import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createClient, episode, fail } from './harness.js';

const SUCCESS = { Episode: 'The Show S01E02', Blocklisted: true, FileDeleted: true, Warning: null };

async function openMenuFor(client, id) {
    client.click(client.addCard(id).querySelector('[data-action="menu"]'));
    return client.openActionSheet();
}

async function confirm(client) {
    client.click(client.dialog().querySelector('.btnConfirmBroken'));
    await client.flush();
}

test('adds Mark as Broken to an episode menu for users who are allowed', async t => {
    const client = createClient({ items: { ep1: episode('ep1') } });
    t.after(() => client.close());

    const sheet = await openMenuFor(client, 'ep1');

    const item = client.menuItem(sheet);
    assert.ok(item);
    assert.equal(item.querySelector('.actionSheetItemText').textContent, 'Mark as Broken');
    assert.ok(item.querySelector('.actionsheetMenuItemIcon').classList.contains('heart_broken'));
    assert.ok(!item.classList.contains('selected'));
    assert.equal(item.querySelector('.listItemBodyText.secondary'), null);
    assert.equal(item.previousElementSibling.getAttribute('data-id'), 'addtoplaylist');
});

test('works from the details page more button', async t => {
    const client = createClient({ hash: '#/details?id=ep1', items: { ep1: episode('ep1') } });
    t.after(() => client.close());

    client.click(client.addDetailsMoreButton());
    const sheet = await client.openActionSheet();

    assert.ok(client.menuItem(sheet));
});

test('works from a right-click', async t => {
    const client = createClient({ items: { ep1: episode('ep1') } });
    t.after(() => client.close());

    client.click(client.addCard('ep1').querySelector('.cardImage'), 'contextmenu');
    const sheet = await client.openActionSheet();

    assert.ok(client.menuItem(sheet));
});

test('does not add the entry for other item types', async t => {
    const client = createClient({ items: { m1: { Id: 'm1', Type: 'Movie', Name: 'A Movie' } } });
    t.after(() => client.close());

    const sheet = await openMenuFor(client, 'm1');

    assert.equal(client.menuItem(sheet), null);
});

test('does not add the entry for users who are not allowed, and never looks the item up', async t => {
    const client = createClient({ admin: false, items: { ep1: episode('ep1') } });
    t.after(() => client.close());

    const sheet = await openMenuFor(client, 'ep1');

    assert.equal(client.menuItem(sheet), null);
    assert.ok(!client.calls.some(c => c.startsWith('getItem')));
});

test('ignores menus that are not item menus', async t => {
    const client = createClient({ items: { ep1: episode('ep1') } });
    t.after(() => client.close());

    client.click(client.addCard('ep1').querySelector('[data-action="menu"]'));
    const sheet = await client.openActionSheet(['sortname', 'datecreated']);

    assert.equal(client.menuItem(sheet), null);
});

test('confirming marks the episode as broken and reports the result', async t => {
    const client = createClient({
        items: { ep1: episode('ep1') },
        routes: { 'POST SonarrFlag/Broken/ep1': SUCCESS }
    });
    t.after(() => client.close());

    const sheet = await openMenuFor(client, 'ep1');
    client.click(client.menuItem(sheet));

    const dialog = client.dialog();
    assert.ok(dialog);
    assert.match(dialog.textContent, /The Show S01E02 – Pilot/);
    assert.deepEqual(client.requests, []);

    await confirm(client);

    assert.equal(client.dialog(), null);
    assert.deepEqual(client.requests.map(r => r.method + ' ' + r.path), ['POST SonarrFlag/Broken/ep1']);
    assert.deepEqual(client.toasts(), ['Sonarr is searching for a replacement for The Show S01E02']);
});

test('cancelling, Escape and the backdrop close the dialog without a request', async t => {
    const client = createClient({ items: { ep1: episode('ep1') } });
    t.after(() => client.close());
    const sheet = await openMenuFor(client, 'ep1');
    const { window, document } = client;

    for (const close of [
        () => client.click(client.dialog().querySelector('.btnCancelBroken')),
        () => client.click(client.dialog().querySelector('.btnCancel')),
        () => client.click(client.dialog()),
        () => document.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    ]) {
        client.click(client.menuItem(sheet));
        assert.ok(client.dialog());
        close();
        assert.equal(client.dialog(), null);
    }

    assert.deepEqual(client.requests, []);
});

test('keys pressed while the dialog is open do not reach the player', async t => {
    const client = createClient({ items: { ep1: episode('ep1') } });
    t.after(() => client.close());
    const { window, document } = client;
    const pressed = [];
    document.addEventListener('keydown', event => pressed.push(event.key));

    const sheet = await openMenuFor(client, 'ep1');
    client.click(client.menuItem(sheet));
    document.body.dispatchEvent(new window.KeyboardEvent('keydown', { key: ' ', bubbles: true }));
    document.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    document.body.dispatchEvent(new window.KeyboardEvent('keydown', { key: ' ', bubbles: true }));

    assert.deepEqual(pressed, [' ']);
});

test('shows the warning when nothing could be blocklisted', async t => {
    const client = createClient({
        items: { ep1: episode('ep1') },
        routes: { 'POST SonarrFlag/Broken/ep1': { ...SUCCESS, Blocklisted: false, Warning: 'No download was found.' } }
    });
    t.after(() => client.close());

    const sheet = await openMenuFor(client, 'ep1');
    client.click(client.menuItem(sheet));
    await confirm(client);

    assert.deepEqual(client.toasts(), ['Sonarr is searching for a replacement for The Show S01E02. No download was found.']);
});

test('shows the server error message on failure', async t => {
    const client = createClient({
        items: { ep1: episode('ep1') },
        routes: { 'POST SonarrFlag/Broken/ep1': fail('The Show was not found in Sonarr.') }
    });
    t.after(() => client.close());

    const sheet = await openMenuFor(client, 'ep1');
    client.click(client.menuItem(sheet));
    await confirm(client);

    assert.deepEqual(client.toasts(), ['Could not mark as broken: The Show was not found in Sonarr.']);
});

test('falls back to a generic error without a message', async t => {
    const client = createClient({
        items: { ep1: episode('ep1') },
        routes: { 'POST SonarrFlag/Broken/ep1': fail() }
    });
    t.after(() => client.close());

    const sheet = await openMenuFor(client, 'ep1');
    client.click(client.menuItem(sheet));
    await confirm(client);

    assert.deepEqual(client.toasts(), ['Could not mark as broken: the request failed.']);
});

test('escapes episode names in the dialog', async t => {
    const client = createClient({ items: { ep1: episode('ep1', { Name: '<img src=x onerror=alert(1)>' }) } });
    t.after(() => client.close());

    const sheet = await openMenuFor(client, 'ep1');
    client.click(client.menuItem(sheet));

    assert.equal(client.dialog().querySelector('img'), null);
    assert.match(client.dialog().textContent, /<img src=x/);
});

test('adds a heart_broken button to the video OSD before the settings button', async t => {
    const client = createClient({ nowPlaying: episode('ep1') });
    t.after(() => client.close());

    const osd = await client.openVideoOsd();

    const button = client.osdButton();
    assert.ok(button);
    assert.ok(button.querySelector('.material-icons.heart_broken'));
    assert.equal(button.getAttribute('title'), 'Mark as Broken');
    assert.ok(button.nextElementSibling.classList.contains('btnVideoOsdSettings'));
    assert.ok(!button.classList.contains('hide'));
    assert.equal(osd.querySelectorAll('.btnSonarrFlag').length, 1);
});

test('hides the OSD button while a movie is playing', async t => {
    const client = createClient({ nowPlaying: { Id: 'm1', Type: 'Movie', Name: 'A Movie' } });
    t.after(() => client.close());

    await client.openVideoOsd();

    assert.ok(client.osdButton().classList.contains('hide'));
});

test('rechecks the playing item when a new video starts', async t => {
    const client = createClient({ nowPlaying: { Id: 'm1', Type: 'Movie', Name: 'A Movie' } });
    t.after(() => client.close());
    await client.openVideoOsd();
    await client.play();

    client.nowPlaying = episode('ep1');
    await client.play();
    assert.ok(client.osdButton().classList.contains('hide'), 'same source does not recheck');

    await client.play('http://jellyfin.test/Videos/2/stream');
    assert.ok(!client.osdButton().classList.contains('hide'));
});

test('does not show the OSD button to users who are not allowed', async t => {
    const client = createClient({ admin: false, nowPlaying: episode('ep1') });
    t.after(() => client.close());

    await client.openVideoOsd();

    assert.ok(client.osdButton().classList.contains('hide'));
    assert.ok(!client.calls.some(c => c.startsWith('getSessions')));
});

test('does not touch the OSD outside the video page', async t => {
    const client = createClient({ nowPlaying: episode('ep1') });
    t.after(() => client.close());

    const osd = client.document.createElement('div');
    osd.className = 'videoOsdBottom';
    osd.innerHTML = '<div class="buttons"><button class="btnVideoOsdSettings"></button></div>';
    client.document.body.appendChild(osd);
    await client.flush();

    assert.equal(client.osdButton(), null);
});

test('the OSD button pauses playback and confirms the playing episode', async t => {
    const client = createClient({
        nowPlaying: episode('ep1'),
        routes: { 'POST SonarrFlag/Broken/ep1': SUCCESS }
    });
    t.after(() => client.close());
    await client.openVideoOsd();
    const video = client.document.querySelector('video');
    let paused = false;
    Object.defineProperty(video, 'paused', { get: () => paused });
    video.pause = () => { paused = true; };

    client.click(client.osdButton());
    await client.flush();

    assert.ok(paused);
    assert.match(client.dialog().textContent, /The Show S01E02/);

    await confirm(client);

    assert.deepEqual(client.requests.map(r => r.method + ' ' + r.path), ['POST SonarrFlag/Broken/ep1']);
});

test('the OSD button uses the item playing at click time', async t => {
    const client = createClient({
        nowPlaying: episode('ep1'),
        routes: { 'POST SonarrFlag/Broken/ep2': SUCCESS }
    });
    t.after(() => client.close());
    await client.openVideoOsd();

    client.nowPlaying = episode('ep2', { IndexNumber: 3 });
    client.click(client.osdButton());
    await client.flush();
    await confirm(client);

    assert.deepEqual(client.requests.map(r => r.path), ['SonarrFlag/Broken/ep2']);
});

test('caches the permission check', async t => {
    const client = createClient({ items: { ep1: episode('ep1') } });
    t.after(() => client.close());

    await openMenuFor(client, 'ep1');
    await openMenuFor(client, 'ep1');

    assert.equal(client.calls.filter(c => c === 'getPermission').length, 1);
});

test('hides the buttons when the permission check fails, and retries it next time', async t => {
    const client = createClient({ items: { ep1: episode('ep1') }, routes: { 'GET SonarrFlag/Permission': fail() } });
    t.after(() => client.close());

    const sheet = await openMenuFor(client, 'ep1');
    assert.equal(client.menuItem(sheet), null);

    client.routes['GET SonarrFlag/Permission'] = { CanMarkBroken: true };
    const retried = await openMenuFor(client, 'ep1');
    assert.ok(client.menuItem(retried));
});
