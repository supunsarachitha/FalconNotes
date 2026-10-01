// Port of lib/appearance.ts (docs/06, Light and dark). The app resolves System from the device in C#, because WebViews
// do not report it reliably, so exactly one of .dark and .light is always set.

const KEY = "falcon-notes:appearance";

export function apply(dark, accent) {
  const root = document.documentElement;
  root.classList.toggle("dark", dark);
  root.classList.toggle("light", !dark);
  root.style.colorScheme = dark ? "dark" : "light";
  if (accent === "falcon") delete root.dataset.accent;
  else root.dataset.accent = accent;
  try {
    localStorage.setItem(KEY, JSON.stringify({ dark, accent }));
  } catch {
    // The choice still applies now; it is just not remembered for the first paint.
  }
}

export function setMenuSize(size) {
  document.documentElement.dataset.menuSize = size;
}

/** The system's safe-area insets, measured natively (docs/12): the page uses var(--safe-*, env(…)). */
export function setInsets(top, right, bottom, left) {
  const style = document.documentElement.style;
  style.setProperty("--safe-top", `${top}px`);
  style.setProperty("--safe-right", `${right}px`);
  style.setProperty("--safe-bottom", `${bottom}px`);
  style.setProperty("--safe-left", `${left}px`);
}

export function scrollToTop() {
  window.scrollTo(0, 0);
}

/** Erase all data: the remembered appearance goes too (docs/04, Starting over). */
export function forget() {
  try {
    localStorage.clear();
  } catch {
    // Nothing to clear.
  }
}
