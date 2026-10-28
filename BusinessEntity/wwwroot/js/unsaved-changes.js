// Один browser listener предупреждает о локальных правках; подтверждённый уход снимает его синхронно.
(function () {
    let owner = null;
    let dirty = false;
    const warn = function (event) {
        if (!dirty) return;
        event.preventDefault();
        event.returnValue = '';
    };
    window.beUnsavedChanges = {
        attach: function (id) {
            if (owner !== id) dirty = false;
            owner = id;
            window.addEventListener('beforeunload', warn);
        },
        setDirty: function (id, value) { if (owner === id) dirty = value === true; },
        detach: function (id) {
            if (owner !== id) return;
            window.removeEventListener('beforeunload', warn);
            owner = null;
            dirty = false;
        }
    };
})();
