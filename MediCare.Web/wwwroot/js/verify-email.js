// Email verification page: six digit boxes, live expiry ring, resend and change-email, and the success / error motion.
// The server is the source of truth for the code, its expiry, the resend cooldown and the tries left; this script only
// draws that state and plays the transitions. Without JavaScript the form still posts and the server redirects.
(() => {
  "use strict";
  const form = document.getElementById("mc-otp-form");
  if (!form) return;

  const $ = id => document.getElementById(id);
  const otp = $("mc-otp"), boxes = [...otp.querySelectorAll("input")], msg = $("mc-otp-msg");
  const arc = $("mc-otp-arc"), timer = $("mc-otp-timer"), resend = $("mc-otp-resend");
  const token = form.querySelector("input[name=__RequestVerificationToken]")?.value ?? "";
  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const lifetime = Number(form.dataset.lifetime) || 600;
  const C = 2 * Math.PI * 16;

  // Server clock offset, so a wrong device clock cannot shorten or stretch the countdown.
  let offset = Number(form.dataset.now) - Date.now();
  let expiresAt = form.dataset.expires ? Number(form.dataset.expires) : null;
  let cooldownUntil = Date.now() + (Number(form.dataset.cooldown) || 0) * 1000;
  let tries = Number(form.dataset.tries) || 0;
  let busy = null, tick = null;

  boxes.forEach((box, i) => { box.style.setProperty("--i", i); box.removeAttribute("name"); });
  arc.style.strokeDasharray = C;

  const sleep = ms => new Promise(resolve => setTimeout(resolve, reduce ? Math.min(ms, 60) : ms));
  const restart = (el, cls) => { el.classList.remove(cls); void el.offsetWidth; el.classList.add(cls); };
  const code = () => boxes.map(b => b.value).join("");
  const fmt = s => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
  const secondsLeft = () => expiresAt ? Math.max(0, Math.ceil((expiresAt - (Date.now() + offset)) / 1000)) : 0;
  const cooldownLeft = () => Math.max(0, Math.ceil((cooldownUntil - Date.now()) / 1000));
  const live = () => secondsLeft() > 0 && tries > 0;

  function say(text, ok) {
    msg.className = "mc-otp-msg" + (ok ? " is-ok" : "");
    msg.textContent = "";
    void msg.offsetWidth;
    msg.textContent = text || "";
  }

  function render() {
    const left = secondsLeft();
    $("mc-otp-clock").textContent = fmt(left).padStart(5, "0");
    arc.style.strokeDashoffset = C * (1 - Math.min(left, lifetime) / lifetime);
    timer.classList.toggle("is-late", left > 0 && left <= 60);
    timer.classList.toggle("is-out", left <= 0);
    $("mc-otp-timer-label").textContent = left > 0 ? "Code expires in" : "Code expired";
    const wait = cooldownLeft();
    resend.disabled = wait > 0;
    resend.innerHTML = wait > 0 ? `Resend in <span>${fmt(wait)}</span>` : (live() ? "Resend code" : "Send a new code");
    if (busy !== "done" && busy !== "checking")
      boxes.forEach(b => { b.disabled = !live(); });
  }

  function startClock() {
    clearInterval(tick);
    let wasLive = live();
    tick = setInterval(() => {
      if (busy === "done") return;
      render();
      if (wasLive && !live() && secondsLeft() === 0) {
        boxes.forEach(b => { b.value = ""; b.classList.remove("is-filled"); });
        say("This code has expired. Send a new one to continue.");
      }
      wasLive = live();
    }, 1000);
  }

  function apply(state) {
    if (state.serverNow) offset = state.serverNow - Date.now();
    expiresAt = state.expiresAt ?? null;
    cooldownUntil = Date.now() + (state.cooldown || 0) * 1000;
    tries = state.triesLeft ?? tries;
    if (state.maskedEmail) $("mc-otp-email").textContent = state.maskedEmail;
  }

  function refill() {
    timer.classList.add("is-refill");
    render();
    setTimeout(() => timer.classList.remove("is-refill"), 750);
  }

  async function post(url, data) {
    try {
      const response = await fetch(url, {
        method: "POST",
        headers: { "X-Requested-With": "fetch", "RequestVerificationToken": token },
        body: new URLSearchParams(data),
        credentials: "same-origin"
      });
      if (response.status === 429) return { status: "throttled", message: "Too many attempts. Please wait a minute and try again." };
      if (!(response.headers.get("content-type") || "").includes("application/json")) {
        // Signed out elsewhere, or the session ended: let the server decide where this page belongs now.
        location.reload();
        return { status: "reload" };
      }
      return await response.json();
    } catch {
      return { status: "offline", message: "No connection. Check your internet and try again." };
    }
  }

  function fill(value) {
    const digits = value.replace(/\D/g, "").slice(0, 6).split("");
    boxes.forEach((b, i) => { b.value = digits[i] ?? ""; b.classList.toggle("is-filled", !!digits[i]); });
    (boxes[digits.length] ?? boxes[5]).focus();
    if (digits.length === 6) verify();
  }

  boxes.forEach((box, i) => {
    box.addEventListener("input", () => {
      if (busy) return;
      const v = box.value.replace(/\D/g, "");
      if (v.length > 1) { fill(code().slice(0, i) + v); return; }   // paste, or autofill from the email app
      box.value = v;
      if (msg.textContent && !msg.classList.contains("is-ok")) say("");
      if (v) { restart(box, "is-filled"); if (i < 5) boxes[i + 1].focus(); else if (code().length === 6) verify(); }
      else box.classList.remove("is-filled");
    });
    box.addEventListener("keydown", e => {
      if (e.key === "Backspace" && !box.value && i > 0) {
        boxes[i - 1].value = ""; boxes[i - 1].classList.remove("is-filled"); boxes[i - 1].focus(); e.preventDefault();
      }
      if (e.key === "ArrowLeft" && i > 0) { boxes[i - 1].focus(); e.preventDefault(); }
      if (e.key === "ArrowRight" && i < 5) { boxes[i + 1].focus(); e.preventDefault(); }
    });
    box.addEventListener("focus", () => box.select());
  });

  form.addEventListener("submit", e => {
    // Sign out (and Resend without this script) post normally; the main button verifies here.
    if (e.submitter && e.submitter.id !== "mc-otp-go") return;
    e.preventDefault();
    if (busy) return;
    if (!live()) { say(secondsLeft() === 0 ? "This code has expired. Send a new one to continue." : "Send a new code to continue."); return; }
    if (code().length === 6) verify();
    else { say("Enter all 6 digits."); boxes[code().length].focus(); }
  });

  async function verify() {
    if (busy || !live()) return;
    busy = "checking";
    boxes.forEach(b => b.disabled = true);
    otp.classList.add("is-checking");
    const [result] = await Promise.all([post(form.action, { code: code() }), sleep(450)]);
    otp.classList.remove("is-checking");
    if (result.status === "ok") return success(result.redirect || "/");
    if (result.status === "reload") return;
    if (result.status === "wrong") { tries = result.triesLeft; return fail(result.message); }
    if (result.status === "expired" || result.status === "locked") { apply(result); return fail(result.message); }
    busy = null; render(); say(result.message || "Something went wrong. Try again."); boxes[0].focus();
  }

  async function fail(text) {
    restart(otp, "is-error");
    say(text);
    await sleep(820);
    boxes.forEach(b => b.classList.add("is-clearing"));
    await sleep(440);
    boxes.forEach(b => { b.value = ""; b.className = ""; });
    otp.classList.remove("is-error");
    busy = null;
    render();
    if (live()) boxes[0].focus(); else resend.focus();
  }

  async function success(redirect) {
    busy = "done";
    clearInterval(tick);
    const row = otp.getBoundingClientRect(), cx = row.left + row.width / 2;
    boxes.forEach((b, i) => {
      const r = b.getBoundingClientRect();
      b.style.setProperty("--dx", `${cx - (r.left + r.width / 2)}px`);
      b.style.setProperty("--j", Math.abs(2.5 - i) < 1 ? 2 : Math.abs(2.5 - i) < 2 ? 1 : 0);
    });
    otp.classList.add("is-success");
    $("mc-otp-meta").classList.add("is-faded");
    $("mc-otp-change-box").hidden = true;
    say("");
    await sleep(600);
    otp.classList.add("is-merging");
    await sleep(380);
    otp.classList.add("is-done");
    burst();
    $("mc-otp-addr").classList.add("is-gone");
    swapHead("Email confirmed", `Welcome to MediCare, ${form.dataset.name || "there"}. Your account is ready.`);
    await sleep(500);
    $("mc-otp-go").classList.add("is-success");
    $("mc-otp-go-text").textContent = "Opening MediCare…";
    await sleep(1450);
    location.assign(redirect);
  }

  async function swapHead(title, sub) {
    const head = $("mc-otp-head");
    restart(head, "mc-swap-out");
    await sleep(170);
    $("mc-otp-title").textContent = title;
    $("mc-otp-sub").textContent = sub;
    head.classList.remove("mc-swap-out");
    restart(head, "mc-swap-in");
  }

  function burst() {
    if (reduce) return;
    const colors = ["var(--mc-neon)", "var(--mc-neon)", "var(--teal-strong)", "var(--mc-spark)"];
    const host = $("mc-otp-burst"), n = 18;
    host.replaceChildren();
    for (let k = 0; k < n; k++) {
      const angle = (k / n) * Math.PI * 2 + (Math.random() - 0.5) * 0.3;
      const plus = k % 3 !== 2;
      const distance = (plus ? 70 : 44) + Math.random() * 42;
      const bit = document.createElement("i");
      bit.className = plus ? "plus" : "dot";
      bit.style.setProperty("--x", `${Math.cos(angle) * distance}px`);
      bit.style.setProperty("--y", `${Math.sin(angle) * distance * 0.72}px`);
      bit.style.setProperty("--r", `${(Math.random() - 0.5) * 240}deg`);
      bit.style.setProperty("--s", `${plus ? 8 + Math.random() * 6 : 4 + Math.random() * 3}px`);
      bit.style.setProperty("--c", colors[k % colors.length]);
      bit.style.animationDelay = `${0.08 + Math.random() * 0.07}s`;
      host.appendChild(bit);
    }
  }

  resend.addEventListener("click", async e => {
    e.preventDefault();
    if (busy || resend.disabled) return;
    resend.disabled = true;
    const result = await post(form.dataset.resendUrl, {});
    if (result.status === "reload") return;
    if (result.status === "sent") {
      apply(result);
      boxes.forEach(b => { b.value = ""; b.className = ""; });
      refill();
      say(result.message, true);
      boxes[0].focus();
    } else {
      if (result.cooldown !== undefined) apply(result);
      render();
      say(result.message);
    }
  });

  const changeBox = $("mc-otp-change-box"), newEmail = $("mc-otp-new-email");
  $("mc-otp-change").addEventListener("click", () => {
    changeBox.hidden = false;
    newEmail.focus();
  });
  $("mc-otp-change-cancel").addEventListener("click", () => { changeBox.hidden = true; boxes[0].focus(); });
  newEmail.addEventListener("keydown", e => { if (e.key === "Enter") { e.preventDefault(); $("mc-otp-change-send").click(); } });
  $("mc-otp-change-send").addEventListener("click", async () => {
    if (busy) return;
    const button = $("mc-otp-change-send");
    button.disabled = true;
    const result = await post(form.dataset.changeUrl, { email: newEmail.value.trim() });
    button.disabled = false;
    if (result.status === "reload") return;
    if (result.status === "changed") {
      apply(result);
      changeBox.hidden = true;
      newEmail.value = "";
      boxes.forEach(b => { b.value = ""; b.className = ""; });
      refill();
      say(result.message, true);
      boxes[0].focus();
    } else {
      if (result.cooldown !== undefined) apply(result);
      render();
      say(result.message);
      newEmail.focus();
    }
  });

  render();
  startClock();
  if (live()) boxes[0].focus();
})();
