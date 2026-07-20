mergeInto(LibraryManager.library, {
  PW_CopyToClipboard: function (ptr) {
    var text = UTF8ToString(ptr);
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text);
    }
  },
  PW_OpenUrl: function (ptr) {
    window.open(UTF8ToString(ptr), '_blank');
  },
  PW_GetLang: function () {
    var saved = null;
    try { saved = localStorage.getItem('pw_lang'); } catch (e) {}
    var lang = saved || (navigator.languages && navigator.languages[0]) || navigator.language || 'en';
    lang = ('' + lang).toLowerCase().split('-')[0];
    var bytes = lengthBytesUTF8(lang) + 1;
    var buf = _malloc(bytes);
    stringToUTF8(lang, buf, bytes);
    return buf;
  },
  PW_SetLang: function (ptr) {
    try { localStorage.setItem('pw_lang', UTF8ToString(ptr)); } catch (e) {}
  },
  PW_Share: function (textPtr, urlPtr) {
    var text = UTF8ToString(textPtr);
    var url = UTF8ToString(urlPtr);
    try {
      if (navigator.share) {
        // Native share sheet (mobile) — lets the player pick Instagram, X, etc.
        navigator.share({ title: 'Prompt World', text: text, url: url }).catch(function () {});
        return;
      }
    } catch (e) {}
    // Desktop fallback: open an X/Twitter compose window with the caption + URL.
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(url);
      window.open('https://twitter.com/intent/tweet?text=' + encodeURIComponent(text + ' ' + url), '_blank');
    } catch (e) {}
  },
  PW_SetUrlStage: function (idPtr, keyPtr) {
    // Reflect the current stage in the browser address bar so the URL is
    // course-specific (shareable, reload-safe) without a page navigation.
    // Preserve the creator's editKey (?...&key=) when present — dropping it
    // would break test-clear recording after a reload/bookmark.
    try {
      var id = UTF8ToString(idPtr);
      var key = keyPtr ? UTF8ToString(keyPtr) : '';
      var url = id
        ? (location.pathname + '?stage=' + id + (key ? '&key=' + key : ''))
        : location.pathname;
      history.replaceState(null, '', url);
    } catch (e) {}
  },
  PW_IsMobile: function () {
    try {
      var ua = navigator.userAgent || '';
      if (/iPhone|iPad|iPod|Android|Mobile|Silk|Kindle|BlackBerry|Opera Mini/i.test(ua)) return 1;
      // iPadOS 13+ reports as Mac but is touch — detect by touch points.
      if (navigator.maxTouchPoints && navigator.maxTouchPoints > 1 &&
          /Macintosh/i.test(ua)) return 1;
    } catch (e) {}
    return 0;
  },
  PW_AdsReady: function () {
    return (typeof window !== 'undefined' && typeof window.pwShowAd === 'function') ? 1 : 0;
  },
  PW_ShowInterstitial: function () {
    // Delegates to a page-provided hook so the ad SDK (AdSense / web-game SDK)
    // lives in HTML/JS, not the wasm. index.html defines window.pwShowAd; if it
    // is absent this is a harmless no-op.
    try {
      if (typeof window !== 'undefined' && typeof window.pwShowAd === 'function') {
        window.pwShowAd();
      }
    } catch (e) {}
  },
});
