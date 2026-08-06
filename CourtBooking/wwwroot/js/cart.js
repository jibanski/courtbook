// ── Multi-court cart (localStorage) ─────────────────────────────────────
// Lets a customer accumulate several court/date/time selections — possibly across different
// courts of the SAME facility — before paying for all of them in one checkout. The cart never
// touches the server until /Cart/Checkout is submitted; until then it's just localStorage.
window.CBCart = (function () {
    var STORAGE_KEY = 'cb-cart';

    function empty() {
        return { facilityOwnerId: null, facilitySlug: null, facilityName: null, items: [] };
    }

    function getCart() {
        try {
            var raw = localStorage.getItem(STORAGE_KEY);
            if (!raw) return empty();
            var parsed = JSON.parse(raw);
            if (!parsed || !Array.isArray(parsed.items)) return empty();
            return parsed;
        } catch (e) {
            return empty();
        }
    }

    function saveCart(cart) {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(cart));
        refreshWidget();
    }

    function clear() {
        localStorage.removeItem(STORAGE_KEY);
        refreshWidget();
    }

    // item: { courtId, courtName, ownerId, facilitySlug, facilityName, date, startHour, endHour, price }
    function addItem(item) {
        var cart = getCart();

        if (cart.items.length > 0 && cart.facilityOwnerId && cart.facilityOwnerId !== item.ownerId) {
            var proceed = window.confirm(
                'Your cart has slots from a different facility (' + (cart.facilityName || 'another facility') + ').\n' +
                'Starting a new cart here will clear those. Continue?');
            if (!proceed) return false;
            cart = empty();
        }

        cart.facilityOwnerId = item.ownerId;
        cart.facilitySlug    = item.facilitySlug;
        cart.facilityName    = item.facilityName;

        var dup = cart.items.some(function (i) {
            return i.courtId === item.courtId && i.date === item.date && i.startHour === item.startHour;
        });
        if (dup) return false;

        cart.items.push({
            courtId: item.courtId,
            courtName: item.courtName,
            date: item.date,
            startHour: item.startHour,
            endHour: item.endHour,
            price: item.price,
            addOns: []
        });
        saveCart(cart);
        return true;
    }

    function removeItem(index) {
        var cart = getCart();
        cart.items.splice(index, 1);
        if (cart.items.length === 0) cart = empty();
        saveCart(cart);
    }

    function count() {
        return getCart().items.length;
    }

    function baseTotal() {
        return getCart().items.reduce(function (sum, i) { return sum + (i.price || 0); }, 0);
    }

    // ── Floating widget (rendered by Views/Shared/_CartWidget.cshtml) ────────────────────
    function refreshWidget() {
        var badge = document.getElementById('cbCartBadge');
        var link  = document.getElementById('cbCartLink');
        var total = document.getElementById('cbCartTotal');
        if (!badge) return;

        var cart = getCart();
        var n = cart.items.length;
        badge.textContent = n;
        badge.style.display = n > 0 ? 'inline-flex' : 'none';
        if (total) total.textContent = '₱' + baseTotal().toLocaleString();

        // A page can pin the widget to a fixed checkout URL (e.g. staff's own walk-in cart
        // form, which needs no facility slug) via data-static-url — in that case leave the
        // href alone instead of deriving one from the cart's facilitySlug.
        if (link && link.dataset.staticUrl !== 'true' && cart.facilitySlug) {
            link.href = '/Cart/Checkout?slug=' + encodeURIComponent(cart.facilitySlug);
        }
    }

    function toast(message) {
        var el = document.createElement('div');
        el.textContent = message;
        el.style.cssText = 'position:fixed;bottom:88px;right:20px;z-index:1080;background:#212529;color:#fff;' +
            'padding:10px 16px;border-radius:8px;font-size:.85rem;box-shadow:0 4px 12px rgba(0,0,0,.25);opacity:0;' +
            'transition:opacity .2s;';
        document.body.appendChild(el);
        requestAnimationFrame(function () { el.style.opacity = '1'; });
        setTimeout(function () {
            el.style.opacity = '0';
            setTimeout(function () { el.remove(); }, 200);
        }, 2200);
    }

    document.addEventListener('DOMContentLoaded', refreshWidget);

    return {
        getCart: getCart,
        addItem: addItem,
        removeItem: removeItem,
        clear: clear,
        count: count,
        baseTotal: baseTotal,
        refreshWidget: refreshWidget,
        toast: toast
    };
})();

// ── "Add to cart" buttons on the facility booking grid ───────────────────────────────
// Buttons carry data-* attributes (see Views/Facility/BookCourt.cshtml); clicking one adds the
// slot to the cart instead of navigating to the single-slot booking form.
document.addEventListener('click', function (e) {
    var btn = e.target.closest('.cb-add-to-cart');
    if (!btn) return;
    e.preventDefault();

    var added = window.CBCart.addItem({
        courtId: parseInt(btn.dataset.courtId, 10),
        courtName: btn.dataset.courtName,
        ownerId: btn.dataset.ownerId,
        facilitySlug: btn.dataset.facilitySlug,
        facilityName: btn.dataset.facilityName,
        date: btn.dataset.date,
        startHour: parseInt(btn.dataset.startHour, 10),
        endHour: parseInt(btn.dataset.endHour, 10),
        price: parseFloat(btn.dataset.price)
    });

    if (added) {
        var n = window.CBCart.count();
        window.CBCart.toast('Added to cart — ' + n + ' slot' + (n === 1 ? '' : 's'));
    }
});
