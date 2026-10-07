// NoteList's infinite scroll (the narrow slice of lib/viewport.ts this port needs — the gallery's own lazy
// loading is unnecessary here, since the media handler serves files directly, docs/02): tells .NET when the
// load-more sentinel comes within reach of the viewport, so the next page can be fetched.

const observers = new WeakMap();

/** Observes `element`, calling `dotnet.OnNearEnd()` each time it comes within `marginPx` of the viewport. */
export function bindSentinel(element, dotnet, marginPx) {
  if (!element || observers.has(element)) return;
  const observer = new IntersectionObserver(
    (entries) => {
      if (entries.some((entry) => entry.isIntersecting)) dotnet.invokeMethodAsync("OnNearEnd");
    },
    { rootMargin: `${marginPx}px 0px` },
  );
  observer.observe(element);
  observers.set(element, observer);
}

/** Undoes {@link bindSentinel}. */
export function unbindSentinel(element) {
  observers.get(element)?.disconnect();
  observers.delete(element);
}
