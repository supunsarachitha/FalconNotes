// Markdown.razor interop: task-list checkboxes are plain <input type="checkbox" data-task="…">, the offset where
// their item's list marker starts. Ticking one need not reload the note's HTML, so this listens for the change on
// the rendered container (one listener, however many checkboxes it holds) and tells .NET which item to toggle.

const bound = new WeakMap();

export function bindTaskToggle(container, dotnet) {
  if (!container || bound.has(container)) return;
  const onChange = (event) => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement) || input.type !== "checkbox") return;
    const offset = Number(input.dataset.task);
    if (!Number.isInteger(offset)) return;
    dotnet.invokeMethodAsync("ToggleTask", offset);
  };
  container.addEventListener("change", onChange);
  bound.set(container, onChange);
}

export function unbindTaskToggle(container) {
  const onChange = bound.get(container);
  if (onChange) container.removeEventListener("change", onChange);
  bound.delete(container);
}
