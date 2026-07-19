/**
 * @param {unknown} value
 * @returns {number}
 */
export function clampSemitones(value) {
  const parsed = typeof value === "number" ? value : Number(value);
  if (!Number.isFinite(parsed)) return 0;
  return Math.max(-12, Math.min(12, Math.round(parsed)));
}

/**
 * @param {number} value
 * @returns {string}
 */
export function formatSemitones(value) {
  const normalized = clampSemitones(value);
  if (normalized === 0) return "0 st";
  return `${normalized > 0 ? "+" : ""}${normalized} st`;
}

/**
 * @param {string} state
 * @returns {{ label: string, className: string }}
 */
export function describePipelineState(state) {
  switch (state) {
    case "ready":
      return { label: "Audio processing is active", className: "ready" };
    case "setup-required":
      return { label: "Setup required", className: "warning" };
    case "error":
      return { label: "Audio helper error", className: "error" };
    default:
      return { label: "Helper disconnected", className: "offline" };
  }
}

/**
 * @param {string} state
 * @param {string | null | undefined} latestVersion
 * @returns {string | null}
 */
export function formatUpdateAction(state, latestVersion) {
  switch (state) {
    case "available":
      return `Update to v${latestVersion || "latest"}`;
    case "downloading":
      return "Downloading...";
    case "installing":
      return "Installing...";
    default:
      return null;
  }
}
