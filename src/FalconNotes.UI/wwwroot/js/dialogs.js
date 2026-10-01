// <dialog> and dropdown menus, in place of Radix Dialog and DropdownMenu (docs/02, JavaScript interop): modal dialogs
// trap focus natively; Esc and a click on the backdrop cancel; focus returns to what opened them.

const openers = new WeakMap();

export function showModal(dialog, dotnet) {
  if (!dialog || dialog.open) return;
  openers.set(dialog, document.activeElement);
  dialog.showModal();
  dialog.oncancel = (event) => {
    event.preventDefault();
    dotnet.invokeMethodAsync("Cancel");
  };
  dialog.onclick = (event) => {
    if (event.target === dialog) dotnet.invokeMethodAsync("Cancel");
  };
}

export function close(dialog) {
  if (!dialog || !dialog.open) return;
  dialog.close();
  const opener = openers.get(dialog);
  if (opener && typeof opener.focus === "function") opener.focus();
}

const menus = new Map();

/** Places a menu under its trigger (end-aligned, 4 px below; above when there is no room) and wires its keys. */
export function openMenu(trigger, menu, dotnet) {
  if (!trigger || !menu) return;
  place(trigger, menu);
  const items = () => [...menu.querySelectorAll('[role="menuitem"]:not([disabled])')];
  items()[0]?.focus();
  const onKey = (event) => {
    const list = items();
    const index = list.indexOf(document.activeElement);
    if (event.key === "ArrowDown") {
      event.preventDefault();
      list[(index + 1) % list.length]?.focus();
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      list[(index - 1 + list.length) % list.length]?.focus();
    } else if (event.key === "Home") {
      event.preventDefault();
      list[0]?.focus();
    } else if (event.key === "End") {
      event.preventDefault();
      list[list.length - 1]?.focus();
    } else if (event.key === "Escape" || event.key === "Tab") {
      event.preventDefault();
      dotnet.invokeMethodAsync("Close");
      trigger.focus();
    }
  };
  const onPointer = (event) => {
    if (!menu.contains(event.target) && !trigger.contains(event.target)) dotnet.invokeMethodAsync("Close");
  };
  const onResize = () => place(trigger, menu);
  menu.addEventListener("keydown", onKey);
  document.addEventListener("pointerdown", onPointer, true);
  window.addEventListener("resize", onResize);
  window.addEventListener("scroll", onResize, true);
  menus.set(menu, () => {
    menu.removeEventListener("keydown", onKey);
    document.removeEventListener("pointerdown", onPointer, true);
    window.removeEventListener("resize", onResize);
    window.removeEventListener("scroll", onResize, true);
  });
}

export function closeMenu(menu) {
  menus.get(menu)?.();
  menus.delete(menu);
}

function place(trigger, menu) {
  const anchor = trigger.getBoundingClientRect();
  const box = menu.getBoundingClientRect();
  const gap = 4;
  const below = anchor.bottom + gap + box.height <= window.innerHeight - 8 || anchor.top - gap - box.height < 8;
  const top = below ? anchor.bottom + gap : anchor.top - gap - box.height;
  const left = Math.min(Math.max(8, anchor.right - box.width), window.innerWidth - box.width - 8);
  menu.style.top = `${Math.round(top)}px`;
  menu.style.left = `${Math.round(left)}px`;
  menu.style.visibility = "visible";
}
