// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// ===== Dark mode toggle (CLAUDE.md mục 15) =====
// data-theme trên <html> đã được set sớm bởi script inline trong <head> của _Layout.cshtml (tránh
// FOUC) — ở đây chỉ cần đồng bộ icon nút bấm lúc tải trang và xử lý click để đảo theme + lưu lại.
(function () {
    var toggleButton = document.getElementById('sb-theme-toggle');
    var icon = document.getElementById('sb-theme-icon');
    if (!toggleButton || !icon) {
        return;
    }

    function currentTheme() {
        return document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
    }

    function syncIcon() {
        // Icon thể hiện hành động sẽ xảy ra khi bấm (giống quy ước phổ biến), không phải theme hiện tại:
        // đang sáng thì hiện 🌙 (bấm để chuyển tối), đang tối thì hiện ☀️ (bấm để chuyển sáng).
        icon.textContent = currentTheme() === 'dark' ? '☀️' : '🌙';
    }

    syncIcon();

    toggleButton.addEventListener('click', function () {
        var next = currentTheme() === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        document.documentElement.setAttribute('data-bs-theme', next);
        try {
            localStorage.setItem('sb-theme', next);
        } catch (e) { /* localStorage có thể bị chặn — theme vẫn đổi cho phiên hiện tại, chỉ không nhớ lại lần sau */ }
        syncIcon();
    });
})();
