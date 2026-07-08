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
});
