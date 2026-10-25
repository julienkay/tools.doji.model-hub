mergeInto(LibraryManager.library, {
  // Copies persistentDataPath from memory to IndexedDB. With autoSyncPersistentDataPath, Unity already does that
  // when a file closes. A call during a running copy starts one more copy after it, so two never overlap.
  ModelHub_PersistentStorage_Flush: function () {
    if (Module.autoSyncPersistentDataPath) {
      return;
    }

    if (Module.modelHubFlushRunning) {
      Module.modelHubFlushAgain = true;
      return;
    }

    Module.modelHubFlushRunning = true;
    var flush = function () {
      FS.syncfs(false, function (error) {
        if (error) {
          console.error("AI Model Hub could not save persistentDataPath to IndexedDB: " + error);
        }

        if (Module.modelHubFlushAgain) {
          Module.modelHubFlushAgain = false;
          flush();
        } else {
          Module.modelHubFlushRunning = false;
        }
      });
    };
    flush();
  }
});
