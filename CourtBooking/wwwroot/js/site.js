// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// ── Light/Dark theme toggle ─────────────────────────────────────────────
(function () {
    var btn = document.getElementById('themeToggleBtn');
    var icon = document.getElementById('themeToggleIcon');
    if (!btn || !icon) return;

    function applyIcon() {
        var isLight = document.documentElement.getAttribute('data-theme') === 'light';
        icon.className = isLight ? 'bi bi-moon-stars-fill' : 'bi bi-sun-fill';
    }
    applyIcon();

    btn.addEventListener('click', function () {
        var isLight = document.documentElement.getAttribute('data-theme') === 'light';
        if (isLight) {
            document.documentElement.removeAttribute('data-theme');
            localStorage.setItem('cb-theme', 'dark');
        } else {
            document.documentElement.setAttribute('data-theme', 'light');
            localStorage.setItem('cb-theme', 'light');
        }
        applyIcon();
    });
})();
