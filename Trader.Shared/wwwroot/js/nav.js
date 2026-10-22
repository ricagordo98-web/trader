export function scrollToSelector(selector) {
    const element = document.querySelector(selector);

    if (element) {
        element.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
}
