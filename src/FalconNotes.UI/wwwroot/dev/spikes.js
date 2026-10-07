// Phase 0 spikes S3 and S5 (Debug builds only): checks that need the real WebView. Results go back to .NET as plain
// objects and are logged as FALCONSPIKE lines.

const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

export async function webViewChecks() {
  const r = { userAgent: navigator.userAgent };

  // <dialog> with showModal, and Esc closing it.
  const dialog = document.createElement("dialog");
  document.body.append(dialog);
  r.dialogShowModal = typeof dialog.showModal === "function";
  if (r.dialogShowModal) {
    dialog.showModal();
    r.dialogOpens = dialog.open && dialog.matches(":modal");
    dialog.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    dialog.close();
    r.dialogCloses = !dialog.open;
  }
  dialog.remove();

  // execCommand('insertText') keeps the browser's Undo stack.
  const box = document.createElement("textarea");
  document.body.append(box);
  box.value = "hello";
  box.focus();
  box.setSelectionRange(5, 5);
  r.insertText = document.execCommand("insertText", false, " world") && box.value === "hello world";
  r.undo = document.execCommand("undo") && box.value === "hello";
  box.remove();

  r.contentVisibility = CSS.supports("content-visibility", "auto");
  r.containIntrinsicSize = CSS.supports("contain-intrinsic-size", "auto 10rem");
  r.pointerCapture = typeof Element.prototype.setPointerCapture === "function";
  r.dialogElement = typeof HTMLDialogElement === "function";

  // The content security policy: remote images, inline scripts and eval are blocked.
  const violations = [];
  const onViolation = (e) => violations.push(e.violatedDirective);
  document.addEventListener("securitypolicyviolation", onViolation);
  const img = new Image();
  const imgResult = new Promise((resolve) => {
    img.onload = () => resolve("loaded");
    img.onerror = () => resolve("blocked");
    setTimeout(() => resolve("timeout"), 3000);
  });
  img.src = "https://example.com/falcon-spike.png";
  r.remoteImage = await imgResult;
  window.__falconInline = false;
  const inline = document.createElement("script");
  inline.textContent = "window.__falconInline = true";
  document.body.append(inline);
  r.inlineScriptRan = window.__falconInline === true;
  inline.remove();
  try {
    // eslint-disable-next-line no-eval
    r.evalRan = eval("1 + 1") === 2;
  } catch {
    r.evalRan = false;
  }
  try {
    const res = await fetch("https://example.com/");
    r.remoteFetch = `status ${res.status}`;
  } catch {
    r.remoteFetch = "blocked";
  }
  await wait(100);
  document.removeEventListener("securitypolicyviolation", onViolation);
  r.cspViolations = [...new Set(violations)].join(" ");

  // Safe areas: what env(safe-area-inset-top) gives on this WebView.
  const probe = document.createElement("div");
  probe.style.paddingTop = "env(safe-area-inset-top)";
  document.body.append(probe);
  r.safeAreaTop = getComputedStyle(probe).paddingTop;
  probe.remove();
  return r;
}

export async function mediaChecks(imageId, videoId) {
  const r = {};
  const url = (id) => `/_media/${id}`;

  try {
    const plain = await fetch(url(imageId));
    r.plainStatus = plain.status;
    r.plainType = plain.headers.get("Content-Type");
    r.plainLength = plain.headers.get("Content-Length");
    r.plainBytes = (await plain.arrayBuffer()).byteLength;
  } catch (e) {
    r.plainError = String(e);
  }
  const head = await fetch(url(videoId), { headers: { Range: "bytes=0-99" } });
  r.rangeStatus = head.status;
  r.contentRange = head.headers.get("Content-Range");
  r.rangeBytes = (await head.arrayBuffer()).byteLength;
  try {
    r.past = (await fetch(url(videoId), { headers: { Range: "bytes=999999999999-" } })).status;
  } catch (e) {
    r.past = String(e);
  }
  try {
    r.unknown = (await fetch(url("00000000-0000-0000-0000-000000000000"))).status;
  } catch (e) {
    r.unknown = String(e);
  }
  r.nosniff = head.headers.get("X-Content-Type-Options");

  let t = performance.now();
  const blob = await (await fetch(url(imageId))).blob();
  r.imageFetchMs = Math.round(performance.now() - t);
  r.imageFetchBytes = blob.size;
  t = performance.now();
  const img = document.getElementById("spike-image");
  img.src = url(imageId);
  await img.decode().catch(() => {});
  r.imageMs = Math.round(performance.now() - t);
  r.imageSize = `${img.naturalWidth}x${img.naturalHeight}`;

  try {
    const size = Number((await fetch(url(videoId), { headers: { Range: "bytes=0-0" } })).headers.get("Content-Range").split("/")[1]);
    const from = size - 354949;
    const tail = await fetch(url(videoId), { headers: { Range: `bytes=${from}-` } });
    r.tailStatus = tail.status;
    r.tailRange = tail.headers.get("Content-Range");
    r.tailLength = tail.headers.get("Content-Length");
    const bytes = await tail.arrayBuffer();
    r.tailBytes = bytes.byteLength;
    r.tailSha256 = [...new Uint8Array(await crypto.subtle.digest("SHA-256", bytes))].map((b) => b.toString(16).padStart(2, "0")).join("");
  } catch (e) {
    r.tailError = String(e);
  }
  const video = document.getElementById("spike-video");
  const events = [];
  for (const name of ["loadstart", "progress", "suspend", "abort", "error", "emptied", "stalled", "loadedmetadata", "waiting"]) {
    video.addEventListener(name, () => events.push(name));
  }
  r.videoEvents = events;
  video.muted = true;
  t = performance.now();
  video.src = url(videoId);
  await new Promise((resolve) => {
    video.onloadedmetadata = resolve;
    video.onerror = resolve;
    setTimeout(resolve, 15000);
  });
  r.videoMetadataMs = Math.round(performance.now() - t);
  r.videoDuration = Math.round(video.duration);
  r.videoError = video.error ? `${video.error.code} ${video.error.message}` : null;
  r.networkState = video.networkState;
  r.readyState = video.readyState;
  if (!Number.isFinite(video.duration)) return r;
  await video.play().catch((e) => (r.playError = String(e)));
  await wait(1500);
  r.playingAt = Math.round(video.currentTime * 10) / 10;

  const seekTo = Math.round(video.duration * 0.7);
  t = performance.now();
  video.currentTime = seekTo;
  await new Promise((resolve) => {
    video.onseeked = resolve;
    setTimeout(resolve, 15000);
  });
  r.seekMs = Math.round(performance.now() - t);
  await wait(1500);
  r.afterSeekAt = Math.round(video.currentTime * 10) / 10;
  r.seekTarget = seekTo;
  r.stillPlaying = !video.paused;
  return r;
}
