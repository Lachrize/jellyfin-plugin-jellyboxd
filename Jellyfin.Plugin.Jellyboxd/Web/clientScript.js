/*
 * Jellyboxd widget for the Jellyfin web client.
 *
 *  1. A compact "Ma note" rating card injected into the item detail page, right
 *     below the cast ("Distribution") section — in flow, centered, so it doesn't
 *     float or move. Rating stored as Jellyfin UserData.Rating (1..10); saving
 *     fires UserDataSaved, which the Jellyboxd plugin pushes to Jellyboxd. The
 *     rating also refreshes live (no reload).
 *  2. A rating badge pinned to the top-left corner of every poster card.
 *
 * Talks only to Jellyfin's own API (same origin) via the global ApiClient.
 */
(function () {
  'use strict';

  var WIDGET_ID = 'jellyboxd-rating-card';
  var PURPLE = '#9b6fd6';
  var PURPLE_SOFT = '#b794e4';
  var RATABLE = ['Movie', 'Series']; // note widget on movies & whole series only
  var ratingCache = {}; // itemId -> rating (1..10) or 0
  var widgetEl = null;

  function getApiClient() {
    if (window.ApiClient) return window.ApiClient;
    if (window.connectionManager && window.connectionManager.currentApiClient) {
      try { return window.connectionManager.currentApiClient(); } catch (e) { return null; }
    }
    return null;
  }

  function getDetailItemId() {
    var h = window.location.hash || '';
    var m = h.match(/[?&]id=([a-f0-9]{32})/i);
    return m ? m[1] : null;
  }

  function ratingFromClientX(starsWrap, clientX) {
    var rect = starsWrap.getBoundingClientRect();
    var ratio = (clientX - rect.left) / rect.width;
    ratio = Math.max(0, Math.min(1, ratio));
    return Math.max(1, Math.min(10, Math.ceil(ratio * 10)));
  }

  function formatStars(value) {
    return (value / 2).toString().replace('.', ',');
  }

  function injectStyles() {
    if (document.getElementById('jbx-styles')) return;
    var s = document.createElement('style');
    s.id = 'jbx-styles';
    s.textContent =
      '#' + WIDGET_ID + '{text-align:center;margin:1.6em 0;animation:jbxFade .4s ease both}' +
      '@keyframes jbxFade{from{opacity:0;transform:translateY(6px)}to{opacity:1;transform:none}}' +
      '.jbx-card{display:inline-flex;flex-direction:column;align-items:center;gap:9px;' +
        'padding:13px 28px;border-radius:14px;font-family:inherit;color:#efeaf6;text-align:center;' +
        'background:linear-gradient(160deg,rgba(124,79,191,0.16),rgba(155,111,214,0.035));' +
        'border:1px solid rgba(155,111,214,0.28);box-shadow:0 6px 22px rgba(0,0,0,0.22)}' +
      '.jbx-label{font-size:11px;font-weight:700;letter-spacing:2.5px;text-transform:uppercase;color:' + PURPLE_SOFT + '}' +
      '.jbx-row{display:flex;align-items:center;gap:13px}' +
      '.jbx-stars{position:relative;cursor:pointer;line-height:1;font-size:25px;letter-spacing:3px;user-select:none;transition:filter .15s ease}' +
      '.jbx-stars:hover{filter:brightness(1.1)}' +
      '.jbx-stars .b{color:rgba(255,255,255,0.16)}' +
      '.jbx-stars .f{color:' + PURPLE + ';position:absolute;left:0;top:0;width:0;overflow:hidden;white-space:nowrap;' +
        'transition:width .12s ease;text-shadow:0 0 12px rgba(155,111,214,0.5)}' +
      '.jbx-value{font-size:15px;color:rgba(255,255,255,0.55);font-variant-numeric:tabular-nums;white-space:nowrap}' +
      '.jbx-value b{color:#fff;font-size:17px;font-weight:800}' +
      '.jbx-clear{display:flex;align-items:center;justify-content:center;width:24px;height:24px;border-radius:999px;' +
        'background:transparent;border:1px solid rgba(255,255,255,0.18);color:rgba(255,255,255,0.6);cursor:pointer;' +
        'font-size:11px;line-height:1;font-family:inherit;transition:.15s}' +
      '.jbx-clear:hover{border-color:rgba(155,111,214,0.6);color:' + PURPLE_SOFT + '}' +
      '.jbx-foot{font-size:9px;letter-spacing:2px;text-transform:uppercase;color:rgba(183,148,228,0.4);font-weight:700}' +
      '.jbx-status{color:' + PURPLE_SOFT + ';font-size:11px;height:12px;opacity:0;transition:opacity .2s}';
    document.head.appendChild(s);
  }

  // -------------------------------------------------------------- rating card

  function buildWidget() {
    injectStyles();
    var section = document.createElement('div');
    section.id = WIDGET_ID;
    section.className = 'verticalSection';

    var card = document.createElement('div');
    card.className = 'jbx-card';

    var label = document.createElement('div');
    label.className = 'jbx-label';
    label.textContent = 'Ma note';

    var row = document.createElement('div');
    row.className = 'jbx-row';

    var stars = document.createElement('span');
    stars.className = 'jbx-stars';
    var back = document.createElement('span');
    back.className = 'b';
    back.textContent = '★★★★★';
    var front = document.createElement('span');
    front.className = 'f';
    front.textContent = '★★★★★';
    stars.appendChild(back);
    stars.appendChild(front);

    var value = document.createElement('span');
    value.className = 'jbx-value';

    var clear = document.createElement('button');
    clear.type = 'button';
    clear.className = 'jbx-clear';
    clear.title = 'Retirer ma note';
    clear.textContent = '✕';

    row.appendChild(stars);
    row.appendChild(value);
    row.appendChild(clear);

    var status = document.createElement('div');
    status.className = 'jbx-status';

    var foot = document.createElement('div');
    foot.className = 'jbx-foot';
    foot.textContent = 'jellyboxd';

    card.appendChild(label);
    card.appendChild(row);
    card.appendChild(status);
    card.appendChild(foot);
    section.appendChild(card);

    section._actual = 0;
    section._hovering = false;
    section._render = function (v) {
      front.style.width = (v / 10 * 100) + '%';
      value.innerHTML = v > 0 ? ('<b>' + formatStars(v) + '</b> / 5') : '<b>–</b> / 5';
    };
    section._setActual = function (v) {
      section._actual = v;
      if (!section._hovering) section._render(v);
    };
    section._flash = function (text, color) {
      status.textContent = text;
      status.style.color = color || PURPLE_SOFT;
      status.style.opacity = '1';
      setTimeout(function () { status.style.opacity = '0'; }, 1500);
    };

    stars.addEventListener('mouseenter', function () { section._hovering = true; });
    stars.addEventListener('mousemove', function (e) { section._hovering = true; section._render(ratingFromClientX(stars, e.clientX)); });
    stars.addEventListener('mouseleave', function () { section._hovering = false; section._render(section._actual); });
    stars.addEventListener('click', function (e) { saveRating(ratingFromClientX(stars, e.clientX)); });
    clear.addEventListener('click', clearRating);

    section._render(0);
    return section;
  }

  function findCastSection() {
    var page = document.querySelector('.itemDetailPage:not(.hide)') ||
               document.querySelector('.libraryPage:not(.hide)') || document;
    var kws = ['distribution', 'acteur', 'cast', 'reparto', 'besetzung', 'elenco', 'interpret', 'équipe', 'equipe'];
    var titles = page.querySelectorAll('h2.sectionTitle, .sectionTitle, h2');
    for (var i = 0; i < titles.length; i++) {
      var t = (titles[i].textContent || '').trim().toLowerCase();
      for (var k = 0; k < kws.length; k++) {
        if (t.indexOf(kws[k]) >= 0 && titles[i].closest) {
          var sec = titles[i].closest('.verticalSection');
          if (sec) return sec;
        }
      }
    }
    var person = page.querySelector('[data-type="Person"]');
    if (person && person.closest) {
      var s = person.closest('.verticalSection');
      if (s) return s;
    }
    return null;
  }

  function detailContent() {
    return document.querySelector('.itemDetailPage:not(.hide) .detailPageContent') ||
           document.querySelector('.itemDetailPage:not(.hide)') ||
           document.querySelector('.detailPageContent');
  }

  function placeWidget(widget) {
    var cast = findCastSection();
    if (cast && cast.parentNode) {
      if (cast.nextSibling !== widget) cast.parentNode.insertBefore(widget, cast.nextSibling);
      return true;
    }
    var content = detailContent();
    if (content) {
      if (content.lastElementChild !== widget) content.appendChild(widget);
      return true;
    }
    return false;
  }

  function detachWidget() {
    if (widgetEl && widgetEl.parentNode) widgetEl.parentNode.removeChild(widgetEl);
  }

  function setItemRating(itemId, rating) {
    ratingCache[itemId] = rating;
    applyBadgesForItem(itemId, rating);
  }

  function loadItem(itemId) {
    var api = getApiClient();
    if (!api || !widgetEl) return;
    api.getItem(api.getCurrentUserId(), itemId).then(function (item) {
      if (!item || RATABLE.indexOf(item.Type) < 0) {
        widgetEl.dataset.ratable = 'no';
        detachWidget();
        return;
      }
      widgetEl.dataset.ratable = 'yes';
      var r = Math.round((item.UserData && item.UserData.Rating) || 0);
      widgetEl._setActual(r);
      setItemRating(itemId, r);
    }, function () {});
  }

  function reloadRating(itemId) {
    var api = getApiClient();
    if (!api || !widgetEl) return;
    api.getItem(api.getCurrentUserId(), itemId).then(function (item) {
      if (!item) return;
      var r = Math.round((item.UserData && item.UserData.Rating) || 0);
      widgetEl._setActual(r);
      setItemRating(itemId, r);
    }, function () {});
  }

  function saveRating(value) {
    var api = getApiClient();
    if (!api || !widgetEl) return;
    var itemId = widgetEl.dataset.itemId;
    api.ajax({
      type: 'POST',
      url: api.getUrl('Users/' + api.getCurrentUserId() + '/Items/' + itemId + '/UserData'),
      data: JSON.stringify({ Rating: value }),
      contentType: 'application/json'
    }).then(function () {
      widgetEl._hovering = false;
      widgetEl._setActual(value);
      setItemRating(itemId, value);
      widgetEl._flash('Enregistré ✓');
    }, function () { widgetEl._flash('Erreur', '#ff8a8a'); });
  }

  function clearRating() {
    var api = getApiClient();
    if (!api || !widgetEl) return;
    var itemId = widgetEl.dataset.itemId;
    api.ajax({
      type: 'DELETE',
      url: api.getUrl('Users/' + api.getCurrentUserId() + '/Items/' + itemId + '/Rating')
    }).then(function () {
      widgetEl._hovering = false;
      widgetEl._setActual(0);
      setItemRating(itemId, 0);
      widgetEl._flash('Note retirée');
    }, function () { widgetEl._flash('Erreur', '#ff8a8a'); });
  }

  // ------------------------------------------------------------- poster badges

  // Inline styles so the badge is correctly positioned even on pages where the
  // widget stylesheet hasn't been injected (grids, home, search).
  var BADGE_CSS = 'position:absolute;top:6px;left:6px;z-index:2;display:flex;align-items:center;gap:3px;' +
    'padding:2px 6px;border-radius:7px;background:rgba(20,15,33,0.88);' +
    '-webkit-backdrop-filter:blur(4px);backdrop-filter:blur(4px);' +
    'border:1px solid rgba(155,111,214,0.55);color:#fff;font-size:12px;font-weight:700;' +
    'line-height:1;pointer-events:none;box-shadow:0 1px 6px rgba(0,0,0,0.45)';

  function badgeHolder(card) {
    return card.querySelector('.cardImageContainer') ||
           card.querySelector('.cardScalable') ||
           card.querySelector('.cardBox') || card;
  }

  function applyBadge(card, rating) {
    var holder = badgeHolder(card);
    var badge = holder.querySelector(':scope > .jbx-badge');
    if (rating > 0) {
      if (!badge) {
        badge = document.createElement('div');
        badge.className = 'jbx-badge';
        badge.style.cssText = BADGE_CSS;
        var pos = window.getComputedStyle(holder).position;
        if (pos === 'static' || !pos) holder.style.position = 'relative';
        holder.appendChild(badge);
      }
      badge.innerHTML = '<span style="color:' + PURPLE + '">★</span> ' + formatStars(rating);
    } else if (badge) {
      badge.remove();
    }
  }

  function applyBadgesForItem(itemId, rating) {
    var cards = document.querySelectorAll('.card[data-id="' + itemId + '"]');
    for (var i = 0; i < cards.length; i++) applyBadge(cards[i], rating);
  }

  function fetchRatings(ids, done) {
    var api = getApiClient();
    if (!api || !ids.length) { done && done(); return; }
    var userId = api.getCurrentUserId();
    var chunkSize = 100;
    var chunks = [];
    for (var i = 0; i < ids.length; i += chunkSize) chunks.push(ids.slice(i, i + chunkSize));
    var remaining = chunks.length;
    chunks.forEach(function (chunk) {
      api.ajax({
        type: 'GET', dataType: 'json',
        url: api.getUrl('Users/' + userId + '/Items', { Ids: chunk.join(','), Fields: 'UserData', EnableImages: false })
      }).then(function (res) {
        var seen = {};
        (res.Items || []).forEach(function (it) { ratingCache[it.Id] = (it.UserData && it.UserData.Rating) || 0; seen[it.Id] = 1; });
        chunk.forEach(function (id) { if (!seen[id]) ratingCache[id] = 0; }); // people/folders: mark as no-rating
        if (--remaining === 0) done && done();
      }, function () { if (--remaining === 0) done && done(); });
    });
  }

  function decorateCards(force) {
    var cards = document.querySelectorAll('.card[data-id]');
    if (!cards.length) return;
    var toFetch = [];
    for (var i = 0; i < cards.length; i++) {
      var id = cards[i].getAttribute('data-id');
      if ((force || !(id in ratingCache)) && toFetch.indexOf(id) < 0) toFetch.push(id);
    }
    // Apply whatever we already know *immediately* (cached items = instant),
    // then fill in the unknowns from the server and apply again.
    for (var j = 0; j < cards.length; j++) {
      applyBadge(cards[j], ratingCache[cards[j].getAttribute('data-id')] || 0);
    }
    if (toFetch.length) {
      fetchRatings(toFetch, function () {
        var again = document.querySelectorAll('.card[data-id]');
        for (var k = 0; k < again.length; k++) applyBadge(again[k], ratingCache[again[k].getAttribute('data-id')] || 0);
      });
    }
  }

  // Preload every movie/series rating once so badges are instant from cache,
  // with no per-card fetch delay on first appearance.
  var prefetched = false;
  function prefetchRatings() {
    var api = getApiClient();
    if (!api || prefetched) return;
    prefetched = true;
    api.ajax({
      type: 'GET', dataType: 'json',
      url: api.getUrl('Users/' + api.getCurrentUserId() + '/Items', {
        IncludeItemTypes: 'Movie,Series', Recursive: true, Fields: 'UserData',
        EnableImages: false, EnableTotalRecordCount: false, Limit: 100000
      })
    }).then(function (res) {
      (res.Items || []).forEach(function (it) { ratingCache[it.Id] = (it.UserData && it.UserData.Rating) || 0; });
      decorateCards(false);
    }, function () { prefetched = false; });
  }

  // Decorate the instant a new card is inserted into the DOM.
  var decoratePending = false;
  function scheduleDecorate() {
    if (decoratePending) return;
    decoratePending = true;
    requestAnimationFrame(function () { decoratePending = false; decorateCards(false); });
  }

  function mutationHasCards(muts) {
    for (var i = 0; i < muts.length; i++) {
      var nodes = muts[i].addedNodes;
      for (var j = 0; j < nodes.length; j++) {
        var n = nodes[j];
        if (n.nodeType !== 1) continue;
        if ((n.matches && n.matches('.card[data-id]')) || (n.querySelector && n.querySelector('.card[data-id]'))) return true;
      }
    }
    return false;
  }

  // -------------------------------------------------------------------- loops

  function tick() {
    if (!getApiClient()) return;
    var itemId = getDetailItemId();

    if (itemId) {
      if (!widgetEl) widgetEl = buildWidget();
      if (widgetEl.dataset.itemId !== itemId) {
        widgetEl.dataset.itemId = itemId;
        widgetEl.dataset.ratable = '';
        widgetEl._setActual(0);
        loadItem(itemId);
      }
      if (widgetEl.dataset.ratable !== 'no') placeWidget(widgetEl);
    } else {
      detachWidget();
      if (widgetEl) widgetEl.dataset.itemId = '';
    }
  }

  function liveRefresh() {
    prefetchRatings(); // no-op once done; retries until ApiClient is ready
    if (widgetEl && widgetEl.parentNode && !widgetEl._hovering && widgetEl.dataset.itemId && widgetEl.dataset.ratable === 'yes') {
      reloadRating(widgetEl.dataset.itemId);
    }
    decorateCards(false);
  }

  injectStyles();
  window.addEventListener('hashchange', tick);

  // Instant badge decoration the moment cards are inserted into the DOM.
  var observer = new MutationObserver(function (muts) {
    if (mutationHasCards(muts)) scheduleDecorate();
  });
  if (document.body) observer.observe(document.body, { childList: true, subtree: true });

  setInterval(tick, 800);
  setInterval(liveRefresh, 1500);
  setInterval(function () { decorateCards(true); }, 6000); // catch external rating changes
  prefetchRatings();
  console.log('[Jellyboxd] widget loaded');
})();
