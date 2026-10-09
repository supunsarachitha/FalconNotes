// Runs before Blazor starts (docs/06, Light and dark: step 3): the last appearance, so the first paint is already right.
(function () {
  try {
    var saved = JSON.parse(localStorage.getItem("falcon-notes:appearance") || "{}");
    var root = document.documentElement;
    root.classList.add(saved.dark ? "dark" : "light");
    root.style.colorScheme = saved.dark ? "dark" : "light";
    if (saved.accent && saved.accent !== "falcon") root.dataset.accent = saved.accent;
    if (saved.background && saved.background !== "none") root.dataset.background = saved.background;
  } catch (e) {
    // No storage: the defaults (light, Falcon, a plain page) until the app applies the user's choice.
  }
})();
