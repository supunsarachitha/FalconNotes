// Double-tap to edit (lib/doubleTap.ts port): calls back when an element is double-tapped on a touch screen or
// double-clicked, except on the controls inside it (links, buttons, checkboxes, players, menus). The second tap's
// touch is cancelled, so the browser does not go on to click whatever has taken the element's place.

const DELAY = 350; // the most time between the two taps of a double-tap, in milliseconds
const SLOP = 24; // how far a tap may move, and the second tap may land from the first, in pixels

function ignored(event) {
  const target = event.target;
  if (!(target instanceof Element) || !event.currentTarget.contains(target)) return true;
  return target.closest("a, button, input, label, select, textarea, summary, audio, video, [role='button']") !== null;
}

const bound = new WeakMap();

/** Wires double-tap/double-click handling onto `element`, calling `dotnet.OnDoubleTap()` when it fires. */
export function bindDoubleTap(element, dotnet) {
  if (!element || bound.has(element)) return;
  let start = null;
  let last = null;

  const onDoubleClick = (event) => {
    if (ignored(event)) return;
    window.getSelection()?.removeAllRanges(); // the double-click selected a word
    dotnet.invokeMethodAsync("OnDoubleTap");
  };
  const onTouchStart = (event) => {
    const touch = event.touches.length === 1 ? event.touches[0] : undefined;
    start = touch ? { x: touch.clientX, y: touch.clientY } : null;
    if (!touch) last = null; // two fingers: a pinch, not a tap
  };
  const onTouchCancel = () => {
    start = null;
    last = null;
  };
  const onTouchEnd = (event) => {
    const from = start;
    const touch = event.changedTouches[0];
    start = null;
    if (!from || !touch || ignored(event) || Math.hypot(touch.clientX - from.x, touch.clientY - from.y) > SLOP) {
      last = null; // a scroll, a swipe or a tap on a control
      return;
    }
    const tap = { at: performance.now(), x: touch.clientX, y: touch.clientY };
    const previous = last;
    if (previous && tap.at - previous.at <= DELAY && Math.hypot(tap.x - previous.x, tap.y - previous.y) <= SLOP) {
      last = null;
      event.preventDefault();
      dotnet.invokeMethodAsync("OnDoubleTap");
    } else {
      last = tap;
    }
  };

  element.addEventListener("dblclick", onDoubleClick);
  element.addEventListener("touchstart", onTouchStart);
  element.addEventListener("touchcancel", onTouchCancel);
  element.addEventListener("touchend", onTouchEnd);
  bound.set(element, () => {
    element.removeEventListener("dblclick", onDoubleClick);
    element.removeEventListener("touchstart", onTouchStart);
    element.removeEventListener("touchcancel", onTouchCancel);
    element.removeEventListener("touchend", onTouchEnd);
  });
}

/** Undoes {@link bindDoubleTap}. */
export function unbindDoubleTap(element) {
  bound.get(element)?.();
  bound.delete(element);
}
