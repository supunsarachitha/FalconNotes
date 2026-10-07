// Composer interop (lib/caret.ts, lib/focus.ts, and the DOM side of lib/markdownEdit.ts's toolbar edits stay
// JavaScript, as the port map says): the caret's on-screen position, for the tag-suggestions popup; focusing a field
// at its end; growing a text box with its content; and applying a toolbar edit through execCommand so the browser's
// own Undo sees it as typing.

const COPIED_STYLES = [
  "boxSizing", "width", "borderTopWidth", "borderRightWidth", "borderBottomWidth", "borderLeftWidth", "paddingTop",
  "paddingRight", "paddingBottom", "paddingLeft", "fontFamily", "fontSize", "fontWeight", "fontStyle", "letterSpacing",
  "lineHeight", "textTransform", "wordSpacing", "tabSize", "textIndent",
];

// A hidden copy of the text box, with the same font, padding and wrapping, holding the text up to the caret followed
// by a marker; the marker's position is the caret's (text boxes have no API for this).
function caretPosition(textarea, offset) {
  const style = window.getComputedStyle(textarea);
  const mirror = document.createElement("div");
  for (const property of COPIED_STYLES) mirror.style[property] = style[property];
  mirror.style.position = "absolute";
  mirror.style.visibility = "hidden";
  mirror.style.top = "0";
  mirror.style.left = "-9999px";
  mirror.style.whiteSpace = "pre-wrap";
  mirror.style.overflowWrap = "break-word";
  mirror.textContent = textarea.value.slice(0, offset);
  const marker = document.createElement("span");
  marker.textContent = "​";
  mirror.appendChild(marker);
  document.body.appendChild(mirror);
  const lineHeight = Number.parseFloat(style.lineHeight) || Number.parseFloat(style.fontSize) * 1.5 || 24;
  const position = { top: marker.offsetTop - textarea.scrollTop, left: marker.offsetLeft - textarea.scrollLeft, height: lineHeight };
  mirror.remove();
  return position;
}

/** Places `popup` just under the caret at `offset` in `textarea`, clamped so it never runs off the right edge. */
export function positionPopup(textarea, popup, offset, width) {
  const at = caretPosition(textarea, offset);
  popup.style.top = `${textarea.offsetTop + at.top + at.height}px`;
  popup.style.left = `${Math.max(0, Math.min(at.left, textarea.clientWidth - width))}px`;
  popup.style.width = `${width}px`;
  popup.style.visibility = "visible";
}

/** Plain focus, caret where the browser puts it (the start, for a field with text): moving on from the title. */
export function focus(element) {
  element?.focus();
}

/** Focuses a field with the caret after its last character (browsers put it at the start otherwise). */
export function focusAtEnd(element) {
  if (!element) return;
  element.focus({ preventScroll: true });
  const end = element.value.length;
  element.setSelectionRange(end, end);
  if (element.tagName === "TEXTAREA") element.scrollTop = element.scrollHeight;
  element.scrollIntoView?.({ block: "nearest" });
}

const grown = new WeakSet();

/** Grows immediately, up to `maxPixels`: call after the text changes from .NET (typing grows itself, below). */
export function growNow(element, maxPixels) {
  element.style.height = "auto";
  element.style.height = `${Math.min(element.scrollHeight, maxPixels)}px`;
}

/** Wires a text box to grow with its content as it is typed in, up to `maxPixels`. */
export function bindAutoGrow(element, maxPixels) {
  if (!grown.has(element)) {
    element.addEventListener("input", () => growNow(element, maxPixels));
    grown.add(element);
  }

  growNow(element, maxPixels);
}

/** The text box's current value and selection. */
export function getSelection(element) {
  return { value: element.value, start: element.selectionStart, end: element.selectionEnd };
}

/** Selects a range without changing the text. */
export function setSelection(element, start, end) {
  element.setSelectionRange(start, end);
}

/**
 * Replaces `value[edit.start..edit.end]` with `edit.insert` and selects `edit.selectStart..edit.selectEnd`, through
 * `execCommand("insertText")` so the browser's own Undo sees it as typing; where that is unavailable, the value is
 * set directly and an `input` event is dispatched, which is what keeps a bound value in sync either way.
 */
export function applyEdit(element, edit) {
  element.focus();
  element.setSelectionRange(edit.start, edit.end);
  const typed = typeof document.execCommand === "function" && document.execCommand("insertText", false, edit.insert);
  if (!typed) {
    const value = element.value;
    element.value = value.slice(0, edit.start) + edit.insert + value.slice(edit.end);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  }

  element.setSelectionRange(edit.selectStart, edit.selectEnd);
}
