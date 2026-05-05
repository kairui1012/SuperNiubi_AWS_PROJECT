// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

document.addEventListener('wheel', (event) => {
    const target = event.target;

    if (!(target instanceof HTMLInputElement)) {
        return;
    }

    if (target.type !== 'number' || document.activeElement !== target) {
        return;
    }

    if (!target.closest('.landlord-cloud-skin')) {
        return;
    }

    event.preventDefault();
}, { passive: false });
