import {
  clampSemitones,
  describePipelineState,
  formatSemitones,
  formatUpdateAction,
} from "./logic.mjs";

declare const Spicetify: any;

const API_URL = "__PITCHIFY_BASE_URL__";
const API_TOKEN = "__PITCHIFY_TOKEN__";
const POLL_INTERVAL_MS = 15_000;

type OutputDevice = {
  id: string;
  name: string;
  isDefault: boolean;
};

type UpdateStatus = {
  currentVersion: string;
  latestVersion: string | null;
  state:
    | "disabled"
    | "checking"
    | "up-to-date"
    | "available"
    | "downloading"
    | "installing"
    | "error";
  message: string | null;
};

type PitchifyStatus = {
  version: string;
  semitones: number;
  state: "ready" | "setup-required" | "error";
  inputDevice: OutputDevice | null;
  outputDevice: OutputDevice | null;
  availableOutputs: OutputDevice[];
  followsDefaultOutput: boolean;
  latencyMs: number | null;
  message: string | null;
  update: UpdateStatus | null;
};

type ViewState = {
  status: PitchifyStatus | null;
  connected: boolean;
  busy: boolean;
  error: string | null;
};

const listeners = new Set<() => void>();
let viewState: ViewState = {
  status: null,
  connected: false,
  busy: false,
  error: null,
};
let pollTimer: number | null = null;
let sliderTimer: number | null = null;
let mountFrame: number | null = null;
let requestSequence = 0;
let pitchRequestSequence = 0;
let optimisticSemitones: number | null = null;
let inlineControl: HTMLDivElement | null = null;
let inlineSlider: HTMLInputElement | null = null;
let inlineValue: HTMLOutputElement | null = null;
let inlineUpdate: HTMLButtonElement | null = null;
let inlineHost: HTMLElement | null = null;
let playbarObserver: MutationObserver | null = null;

function emit(): void {
  for (const listener of listeners) listener();
  updateInlineControl();
}

function setViewState(next: Partial<ViewState>): void {
  viewState = { ...viewState, ...next };
  emit();
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function displayedSemitones(): number {
  return optimisticSemitones ?? viewState.status?.semitones ?? 0;
}

function setOptimisticSemitones(value: number | null): void {
  optimisticSemitones =
    value === null ? null : clampSemitones(value);
  emit();
}

async function apiRequest<T>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers: {
      Authorization: `Bearer ${API_TOKEN}`,
      "Content-Type": "application/json",
      ...(init.headers ?? {}),
    },
  });

  if (!response.ok) {
    let detail = `${response.status} ${response.statusText}`;
    try {
      const body = await response.json();
      detail = body?.error ?? body?.message ?? detail;
    } catch {
      // Keep the HTTP status as the error message.
    }
    throw new Error(detail);
  }

  return response.json() as Promise<T>;
}

async function refreshStatus(silent = false): Promise<void> {
  const sequence = ++requestSequence;
  if (!silent) setViewState({ busy: true, error: null });

  try {
    const status = await apiRequest<PitchifyStatus>("/v1/status");
    if (sequence !== requestSequence) return;
    setViewState({
      status,
      connected: true,
      busy: false,
      error: null,
    });
  } catch (error) {
    if (sequence !== requestSequence) return;
    setViewState({
      connected: false,
      busy: false,
      error: error instanceof Error ? error.message : String(error),
    });
  }
}

async function setPitch(
  semitones: number,
  sequence = ++pitchRequestSequence,
): Promise<void> {
  const value = clampSemitones(semitones);
  setOptimisticSemitones(value);
  setViewState({ busy: true, error: null });
  try {
    const status = await apiRequest<PitchifyStatus>("/v1/pitch", {
      method: "PUT",
      body: JSON.stringify({ semitones: value }),
    });
    if (sequence !== pitchRequestSequence) return;
    optimisticSemitones = null;
    setViewState({
      status,
      connected: true,
      busy: false,
      error: null,
    });
  } catch (error) {
    if (sequence !== pitchRequestSequence) return;
    optimisticSemitones = null;
    setViewState({
      busy: false,
      error: error instanceof Error ? error.message : String(error),
    });
    Spicetify.showNotification("Pitchify could not change pitch", true);
  }
}

function schedulePitch(semitones: number): void {
  setOptimisticSemitones(semitones);
  const sequence = ++pitchRequestSequence;
  if (sliderTimer !== null) window.clearTimeout(sliderTimer);
  sliderTimer = window.setTimeout(() => {
    sliderTimer = null;
    void setPitch(semitones, sequence);
  }, 90);
}

async function setOutput(deviceId: string | null): Promise<void> {
  setViewState({ busy: true, error: null });
  try {
    const status = await apiRequest<PitchifyStatus>("/v1/output", {
      method: "PUT",
      body: JSON.stringify({ deviceId }),
    });
    setViewState({
      status,
      connected: true,
      busy: false,
      error: null,
    });
  } catch (error) {
    setViewState({
      busy: false,
      error: error instanceof Error ? error.message : String(error),
    });
  }
}

async function restartPipeline(): Promise<void> {
  setViewState({ busy: true, error: null });
  try {
    const status = await apiRequest<PitchifyStatus>("/v1/restart", {
      method: "POST",
    });
    setViewState({
      status,
      connected: true,
      busy: false,
      error: null,
    });
  } catch (error) {
    setViewState({
      busy: false,
      error: error instanceof Error ? error.message : String(error),
    });
  }
}

async function installUpdate(): Promise<void> {
  const status = viewState.status;
  if (!status?.update || status.update.state !== "available") return;

  setViewState({
    status: {
      ...status,
      update: {
        ...status.update,
        state: "downloading",
        message: null,
      },
    },
    error: null,
  });

  try {
    const update = await apiRequest<UpdateStatus>("/v1/update", {
      method: "POST",
    });
    setViewState({
      status: viewState.status
        ? { ...viewState.status, update }
        : viewState.status,
      error: null,
    });
    Spicetify.showNotification(
      `Pitchify ${update.latestVersion ?? "update"} is installing`,
    );

    for (const delay of [3_000, 7_000, 12_000, 20_000]) {
      window.setTimeout(() => void refreshStatus(true), delay);
    }
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (viewState.status?.update) {
      setViewState({
        status: {
          ...viewState.status,
          update: {
            ...viewState.status.update,
            state: "error",
            message,
          },
        },
        error: message,
      });
    }
    Spicetify.showNotification("Pitchify update failed", true);
  }
}

function usePitchifyState(): ViewState {
  const React = Spicetify.React;
  const [state, setState] = React.useState(viewState);

  React.useEffect(() => subscribe(() => setState({ ...viewState })), []);
  return state;
}

function PitchifyPanel(): any {
  const React = Spicetify.React;
  const h = React.createElement;
  const state = usePitchifyState();
  const status = state.status;
  const semitones = displayedSemitones();
  const descriptor = describePipelineState(
    state.connected ? (status?.state ?? "offline") : "offline",
  );

  const controlsDisabled = !state.connected || state.busy;
  const selectedOutput = status?.followsDefaultOutput
    ? ""
    : (status?.outputDevice?.id ?? "");

  return h(
    "div",
    { className: "pitchify-panel" },
    h(
      "div",
      { className: "pitchify-value", "aria-live": "polite" },
      formatSemitones(semitones),
    ),
    h(
      "div",
      { className: `pitchify-status pitchify-status--${descriptor.className}` },
      h("span", { className: "pitchify-status-dot" }),
      descriptor.label,
    ),
    h(
      "div",
      { className: "pitchify-controls" },
      h(
        "button",
        {
          className: "pitchify-step",
          disabled: controlsDisabled || semitones <= -12,
          onClick: () => void setPitch(semitones - 1),
          "aria-label": "Lower pitch one semitone",
        },
        "−",
      ),
      h("input", {
        className: "pitchify-slider",
        type: "range",
        min: -12,
        max: 12,
        step: 1,
        value: semitones,
        disabled: controlsDisabled,
        onChange: (event: Event) => {
          const value = Number((event.target as HTMLInputElement).value);
          schedulePitch(value);
        },
        "aria-label": "Pitch in semitones",
      }),
      h(
        "button",
        {
          className: "pitchify-step",
          disabled: controlsDisabled || semitones >= 12,
          onClick: () => void setPitch(semitones + 1),
          "aria-label": "Raise pitch one semitone",
        },
        "+",
      ),
    ),
    h(
      "div",
      { className: "pitchify-scale" },
      h("span", null, "−12"),
      h("span", null, "0"),
      h("span", null, "+12"),
    ),
    h(
      "button",
      {
        className: "pitchify-reset",
        disabled: controlsDisabled || semitones === 0,
        onClick: () => void setPitch(0),
      },
      "Reset to original pitch",
    ),
    h(
      "label",
      { className: "pitchify-label" },
      "Audio output",
      h(
        "select",
        {
          className: "pitchify-select",
          value: selectedOutput,
          disabled: !state.connected || state.busy,
          onChange: (event: Event) => {
            const value = (event.target as HTMLSelectElement).value;
            void setOutput(value || null);
          },
        },
        h("option", { value: "" }, "Follow Windows default"),
        ...(status?.availableOutputs ?? []).map((device) =>
          h(
            "option",
            { key: device.id, value: device.id },
            `${device.name}${device.isDefault ? " (default)" : ""}`,
          ),
        ),
      ),
    ),
    h(
      "div",
      { className: "pitchify-details" },
      h(
        "div",
        null,
        h("span", null, "Input"),
        h("strong", null, status?.inputDevice?.name ?? "VB-CABLE not detected"),
      ),
      h(
        "div",
        null,
        h("span", null, "Output"),
        h("strong", null, status?.outputDevice?.name ?? "Not available"),
      ),
      status?.latencyMs != null
        ? h(
            "div",
            null,
            h("span", null, "Estimated latency"),
            h("strong", null, `${status.latencyMs} ms`),
          )
        : null,
    ),
    status?.message
      ? h("div", { className: "pitchify-message" }, status.message)
      : null,
    state.error
      ? h(
          "div",
          { className: "pitchify-message pitchify-message--error" },
          state.connected
            ? state.error
            : "Pitchify Helper is not reachable. Run Pitchify.Helper.exe or reinstall Pitchify.",
        )
      : null,
    h(
      "button",
      {
        className: "pitchify-restart",
        disabled: state.busy,
        onClick: () =>
          state.connected ? void restartPipeline() : void refreshStatus(),
      },
      state.connected ? "Restart audio pipeline" : "Try again",
    ),
    h(
      "p",
      { className: "pitchify-footnote" },
      "Pitchify affects local Spotify playback only. Spotify must be routed to CABLE Input in Windows Volume Mixer.",
    ),
  );
}

function openPanel(): void {
  Spicetify.PopupModal.display({
    title: "Pitchify",
    content: Spicetify.React.createElement(PitchifyPanel),
    isLarge: false,
  });
  void refreshStatus();
}

function findPlaybarLeftSection(): HTMLElement | null {
  const playbar = document.querySelector<HTMLElement>(
    '[data-testid="now-playing-bar"], .main-nowPlayingBar-nowPlayingBar',
  );
  if (!playbar) return null;

  return (
    playbar.querySelector<HTMLElement>(".main-nowPlayingBar-left") ??
    (playbar.firstElementChild as HTMLElement | null)
  );
}

function createInlineControl(): HTMLDivElement {
  const control = document.createElement("div");
  control.className = "pitchify-inline";
  control.setAttribute("role", "group");
  control.setAttribute("aria-label", "Pitchify pitch control");

  const label = document.createElement("button");
  label.type = "button";
  label.className = "pitchify-inline-label";
  label.textContent = "Pitch";
  label.title = "Open Pitchify audio settings";
  label.addEventListener("click", openPanel);

  const slider = document.createElement("input");
  slider.className = "pitchify-inline-slider";
  slider.type = "range";
  slider.min = "-12";
  slider.max = "12";
  slider.step = "1";
  slider.setAttribute("aria-label", "Pitch in semitones");
  slider.addEventListener("input", () => {
    schedulePitch(Number(slider.value));
  });
  slider.addEventListener("dblclick", () => {
    schedulePitch(0);
  });

  const value = document.createElement("output");
  value.className = "pitchify-inline-value";
  value.setAttribute("aria-live", "polite");

  const update = document.createElement("button");
  update.type = "button";
  update.className = "pitchify-inline-update";
  update.hidden = true;
  update.addEventListener("click", () => void installUpdate());

  control.append(label, slider, value, update);
  inlineSlider = slider;
  inlineValue = value;
  inlineUpdate = update;
  return control;
}

function ensureInlineControl(): void {
  const target = findPlaybarLeftSection();
  if (!target) return;

  if (!inlineControl) {
    inlineControl = createInlineControl();
  }
  if (inlineControl.parentElement !== target) {
    inlineHost?.classList.remove("pitchify-inline-host");
    inlineHost = target;
    inlineHost.classList.add("pitchify-inline-host");
    target.appendChild(inlineControl);
  }
  updateInlineControl();
}

function scheduleInlineMount(): void {
  if (mountFrame !== null) return;
  mountFrame = window.requestAnimationFrame(() => {
    mountFrame = null;
    ensureInlineControl();
  });
}

function updateInlineControl(): void {
  if (!inlineControl?.isConnected) {
    scheduleInlineMount();
    return;
  }

  const value = displayedSemitones();
  const connected = viewState.connected;
  const label = connected
    ? `Pitch: ${formatSemitones(value)}. Double-click to reset.`
    : "Pitchify Helper is disconnected. Click Pitch for setup.";

  if (inlineSlider) {
    inlineSlider.value = String(value);
    inlineSlider.disabled = !connected;
    inlineSlider.title = label;
    inlineSlider.style.setProperty(
      "--pitchify-fill",
      `${((value + 12) / 24) * 100}%`,
    );
  }
  if (inlineValue) {
    const text = connected
      ? formatSemitones(value).replace(" st", "")
      : "--";
    inlineValue.value = text;
    if (inlineValue.textContent !== text) {
      inlineValue.textContent = text;
    }
  }
  const update = viewState.status?.update;
  const updateLabel = update
    ? formatUpdateAction(update.state, update.latestVersion)
    : null;
  const showUpdate = connected && updateLabel !== null;
  if (inlineUpdate) {
    inlineUpdate.hidden = !showUpdate;
    inlineUpdate.disabled = update?.state !== "available";
    if (updateLabel) {
      inlineUpdate.textContent = updateLabel;
    }
    if (update?.state === "available") {
      inlineUpdate.title =
        "Download, verify, and install the latest Pitchify release";
    }
  }
  inlineControl.title = label;
  inlineControl.classList.toggle(
    "pitchify-inline--offline",
    !connected,
  );
  inlineControl.classList.toggle(
    "pitchify-inline--has-update",
    showUpdate,
  );
}

function injectStyles(): void {
  if (document.getElementById("pitchify-styles")) return;
  const style = document.createElement("style");
  style.id = "pitchify-styles";
  style.textContent = `
    .pitchify-panel {
      color: var(--spice-text);
      display: flex;
      flex-direction: column;
      gap: 16px;
      min-width: min(420px, calc(100vw - 64px));
      padding: 4px;
    }
    .pitchify-value {
      font-size: 48px;
      font-weight: 700;
      letter-spacing: -1.5px;
      line-height: 1;
      text-align: center;
    }
    .pitchify-status {
      align-items: center;
      color: var(--spice-subtext);
      display: flex;
      font-size: 13px;
      gap: 8px;
      justify-content: center;
    }
    .pitchify-status-dot {
      background: #8b8b8b;
      border-radius: 50%;
      height: 8px;
      width: 8px;
    }
    .pitchify-status--ready .pitchify-status-dot { background: #1ed760; }
    .pitchify-status--warning .pitchify-status-dot { background: #f0a000; }
    .pitchify-status--error .pitchify-status-dot { background: #e91429; }
    .pitchify-controls {
      align-items: center;
      display: grid;
      gap: 12px;
      grid-template-columns: 42px 1fr 42px;
    }
    .pitchify-step, .pitchify-reset, .pitchify-restart {
      background: var(--spice-button);
      border: 0;
      border-radius: 999px;
      color: var(--spice-button-text);
      cursor: pointer;
      font-weight: 700;
      min-height: 40px;
      padding: 0 16px;
    }
    .pitchify-step { font-size: 24px; padding: 0; }
    .pitchify-reset, .pitchify-restart { align-self: center; }
    .pitchify-restart {
      background: transparent;
      border: 1px solid color-mix(in srgb, var(--spice-text) 35%, transparent);
      color: var(--spice-text);
    }
    .pitchify-step:hover:not(:disabled),
    .pitchify-reset:hover:not(:disabled),
    .pitchify-restart:hover:not(:disabled) { transform: scale(1.03); }
    .pitchify-step:disabled,
    .pitchify-reset:disabled,
    .pitchify-restart:disabled { cursor: default; opacity: .45; }
    .pitchify-slider { accent-color: var(--spice-button); width: 100%; }
    .pitchify-scale {
      color: var(--spice-subtext);
      display: flex;
      font-size: 11px;
      justify-content: space-between;
      margin: -10px 55px 0;
    }
    .pitchify-label {
      color: var(--spice-subtext);
      display: flex;
      flex-direction: column;
      font-size: 12px;
      font-weight: 700;
      gap: 7px;
    }
    .pitchify-select {
      background: var(--spice-card);
      border: 1px solid color-mix(in srgb, var(--spice-text) 25%, transparent);
      border-radius: 6px;
      color: var(--spice-text);
      min-height: 40px;
      padding: 0 10px;
    }
    .pitchify-details {
      background: color-mix(in srgb, var(--spice-card) 75%, transparent);
      border-radius: 8px;
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding: 12px;
    }
    .pitchify-details div {
      display: flex;
      gap: 12px;
      justify-content: space-between;
    }
    .pitchify-details span { color: var(--spice-subtext); }
    .pitchify-details strong {
      max-width: 65%;
      overflow: hidden;
      text-align: right;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .pitchify-message {
      background: color-mix(in srgb, #f0a000 15%, transparent);
      border-left: 3px solid #f0a000;
      border-radius: 4px;
      font-size: 13px;
      padding: 10px 12px;
    }
    .pitchify-message--error {
      background: color-mix(in srgb, #e91429 15%, transparent);
      border-left-color: #e91429;
    }
    .pitchify-footnote {
      color: var(--spice-subtext);
      font-size: 11px;
      line-height: 1.4;
      margin: 0;
      text-align: center;
    }
    .pitchify-inline {
      align-items: center;
      display: flex;
      gap: 9px;
      min-width: 125px;
      max-width: 210px;
      overflow: hidden;
      position: absolute;
      right: 14px;
      top: 50%;
      transform: translateY(-50%);
      width: min(210px, 48%);
      z-index: 2;
    }
    .pitchify-inline-host {
      position: relative;
    }
    .pitchify-inline--has-update {
      max-width: 340px;
      width: min(340px, 74%);
    }
    .pitchify-inline-label {
      appearance: none;
      background: transparent;
      border: 0;
      color: var(--spice-subtext);
      cursor: pointer;
      flex: 0 0 auto;
      font-size: 11px;
      font-weight: 700;
      letter-spacing: .04em;
      padding: 4px 0;
      text-transform: uppercase;
    }
    .pitchify-inline-label:hover {
      color: var(--spice-text);
    }
    .pitchify-inline-slider {
      --pitchify-fill: 50%;
      appearance: none;
      background:
        linear-gradient(
          to right,
          var(--spice-button) 0 var(--pitchify-fill),
          color-mix(in srgb, var(--spice-text) 28%, transparent)
            var(--pitchify-fill) 100%
        );
      border-radius: 999px;
      cursor: pointer;
      flex: 1 1 auto;
      height: 4px;
      min-width: 55px;
      outline: none;
    }
    .pitchify-inline-slider::-webkit-slider-thumb {
      appearance: none;
      background: var(--spice-text);
      border: 0;
      border-radius: 50%;
      box-shadow: 0 1px 4px rgba(0, 0, 0, .45);
      height: 13px;
      width: 13px;
    }
    .pitchify-inline-slider:hover::-webkit-slider-thumb {
      transform: scale(1.08);
    }
    .pitchify-inline-slider:focus-visible::-webkit-slider-thumb {
      box-shadow:
        0 0 0 3px color-mix(in srgb, var(--spice-button) 45%, transparent),
        0 1px 4px rgba(0, 0, 0, .45);
    }
    .pitchify-inline-value {
      color: var(--spice-text);
      flex: 0 0 25px;
      font-size: 11px;
      font-variant-numeric: tabular-nums;
      font-weight: 700;
      text-align: right;
    }
    .pitchify-inline-update {
      appearance: none;
      background: transparent;
      border: 0;
      color: var(--spice-button);
      cursor: pointer;
      flex: 0 0 auto;
      font-size: 10px;
      font-weight: 700;
      padding: 4px 0;
      text-decoration: underline;
      text-underline-offset: 2px;
      white-space: nowrap;
    }
    .pitchify-inline-update:hover:not(:disabled) {
      color: var(--spice-text);
    }
    .pitchify-inline-update:disabled {
      cursor: default;
      opacity: .75;
      text-decoration: none;
    }
    .pitchify-inline--offline {
      opacity: .5;
    }
    .pitchify-inline--offline .pitchify-inline-slider {
      cursor: not-allowed;
    }
    @media (max-width: 1000px) {
      .pitchify-inline {
        right: 8px;
        width: min(145px, 45%);
      }
      .pitchify-inline--has-update {
        width: min(270px, 72%);
      }
      .pitchify-inline-label {
        display: none;
      }
    }
  `;
  document.head.appendChild(style);
}

function start(): void {
  injectStyles();
  ensureInlineControl();
  playbarObserver = new MutationObserver(scheduleInlineMount);
  playbarObserver.observe(document.body, {
    childList: true,
    subtree: true,
  });

  void refreshStatus();
  pollTimer = window.setInterval(() => void refreshStatus(true), POLL_INTERVAL_MS);
  window.addEventListener("beforeunload", () => {
    if (pollTimer !== null) window.clearInterval(pollTimer);
    if (sliderTimer !== null) window.clearTimeout(sliderTimer);
    if (mountFrame !== null) window.cancelAnimationFrame(mountFrame);
    playbarObserver?.disconnect();
    inlineControl?.remove();
    inlineHost?.classList.remove("pitchify-inline-host");
  });
}

function init(): void {
  const spicetify = (globalThis as any).Spicetify;
  if (
    !spicetify?.Player ||
    !spicetify?.PopupModal ||
    !spicetify?.React
  ) {
    window.setTimeout(init, 250);
    return;
  }
  start();
}

init();
