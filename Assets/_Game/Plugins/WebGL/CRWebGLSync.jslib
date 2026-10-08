// WebGL 에서는 파일을 쓴 뒤 **브라우저 저장소로 내보내야** 남는다 — 안 하면 새로고침에
// 진행이 사라진다. 좀비퀸에서 같은 자리를 겪었다 (`Plugins/WebGL/ZQWebGLSync.jslib`).
// 오너가 웹 빌드로 확인하므로 이 게임에서는 **처음부터** 필요하다.
var CRWebGLSync = {
  CRSyncFs: function () {
    try { FS.syncfs(false, function (err) { if (err) console.warn('[CR] syncfs', err); }); }
    catch (e) { console.warn('[CR] syncfs 못 함', e); }
  }
};
mergeInto(LibraryManager.library, CRWebGLSync);
