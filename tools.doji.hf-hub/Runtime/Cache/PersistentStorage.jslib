mergeInto(LibraryManager.library, {
  // Copies persistentDataPath from memory to IndexedDB. With autoSyncPersistentDataPath, Unity already does that
  // when a file closes. A call during a running copy starts one more copy after it, so two never overlap.
  HfHub_PersistentStorage_Flush: function () {
    if (Module.autoSyncPersistentDataPath) {
      return;
    }

    if (Module.hfHubFlushRunning) {
      Module.hfHubFlushAgain = true;
      return;
    }

    Module.hfHubFlushRunning = true;
    var flush = function () {
      FS.syncfs(false, function (error) {
        if (error) {
          console.error("HF Hub could not save persistentDataPath to IndexedDB: " + error);
        }

        if (Module.hfHubFlushAgain) {
          Module.hfHubFlushAgain = false;
          flush();
        } else {
          Module.hfHubFlushRunning = false;
        }
      });
    };
    flush();
  }
});
