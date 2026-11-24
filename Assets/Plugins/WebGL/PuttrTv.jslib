// The web build as a Samsung TV app: TvPlatform.Exit() (Back on the first screen) closes it.
// In a plain browser there is nothing to close.
mergeInto(LibraryManager.library, {
  PuttrTv_Exit: function () {
    try {
      if (typeof tizen !== 'undefined') tizen.application.getCurrentApplication().exit();
    } catch (e) { }
  },
});
