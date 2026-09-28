mergeInto(LibraryManager.library, {
  GFSave_Init__deps: ['$GFSave_Open'],
  GFSave_Read__deps: ['$GFSave_Open'],
  GFSave_Write__deps: ['$GFSave_Open'],
  GFSave_Init: function (id, databasePointer, callback) {
    GFSave_Open(id, UTF8ToString(databasePointer), callback, null);
  },

  GFSave_Read: function (id, databasePointer, keyPointer, callback) {
    var key = UTF8ToString(keyPointer);
    GFSave_Open(id, UTF8ToString(databasePointer), callback, function (db, done) {
      var transaction;
      try { transaction = db.transaction('records', 'readonly'); }
      catch (error) { done(4, String(error)); return; }
      var found = false;
      var value = '';
      var invalid = false;
      var oversized = false;
      transaction.oncomplete = function () { done(invalid ? 7 : oversized ? 2 : found ? 0 : 1, invalid ? 'Stored value is not a string.' : oversized ? 'Stored save exceeds limit.' : value); };
      transaction.onabort = function () { done(4, String(transaction.error || 'Read transaction aborted.')); };
      var request;
      try { request = transaction.objectStore('records').get(key); }
      catch (error) { try { transaction.abort(); } catch (_) { done(6, String(error)); } return; }
      request.onsuccess = function () {
        found = request.result !== undefined;
        if (found && typeof request.result !== 'string') invalid = true;
        else if (found && lengthBytesUTF8(request.result) > 262144) oversized = true;
        else if (found) value = request.result;
      };
    });
  },

  GFSave_Write: function (id, databasePointer, keyPointer, recordPointer, callback) {
    var key = UTF8ToString(keyPointer);
    var record = UTF8ToString(recordPointer);
    GFSave_Open(id, UTF8ToString(databasePointer), callback, function (db, done) {
      var transaction;
      try { transaction = db.transaction('records', 'readwrite'); }
      catch (error) { done(4, String(error)); return; }
      var writeError = null;
      transaction.oncomplete = function () { done(0, ''); };
      transaction.onabort = function () {
        var error = writeError || transaction.error;
        done(error && error.name === 'QuotaExceededError' ? 3 : 4, String(error || 'Write transaction aborted.'));
      };
      try {
        var writeRequest = transaction.objectStore('records').put(record, key);
        writeRequest.onerror = function () { writeError = writeRequest.error; };
      }
      catch (error) { writeError = error; try { transaction.abort(); } catch (_) { done(6, String(error)); } }
    });
  },

  $GFSave_Open: function (id, database, callback, action) {
    var completed = false;
    var done = function (status, message) {
      if (completed) return;
      completed = true;
      var text = String(message || '');
      var bytes = lengthBytesUTF8(text) + 1;
      var pointer = _malloc(bytes);
      stringToUTF8(text, pointer, bytes);
      try { {{{ makeDynCall('viii', 'callback') }}}(id, status, pointer); }
      finally { _free(pointer); }
    };
    if (typeof indexedDB === 'undefined') { done(5, 'IndexedDB is unavailable.'); return; }
    var request;
    try { request = indexedDB.open(database, 1); }
    catch (error) { done(5, String(error)); return; }
    request.onupgradeneeded = function () {
      var db = request.result;
      if (!db.objectStoreNames.contains('records')) db.createObjectStore('records');
    };
    request.onerror = function () { done(4, String(request.error || 'IndexedDB open failed.')); };
    request.onsuccess = function () {
      var db = request.result;
      if (completed) { db.close(); return; }
      db.onversionchange = function () { db.close(); };
      if (!action) { db.close(); done(0, ''); return; }
      action(db, function (status, message) { db.close(); done(status, message); });
    };
  }
});
