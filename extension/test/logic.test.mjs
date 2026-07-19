import assert from "node:assert/strict";
import test from "node:test";
import {
  clampSemitones,
  describePipelineState,
  formatSemitones,
  formatUpdateAction,
} from "../src/logic.mjs";

test("clampSemitones rounds and clamps to the supported range", () => {
  assert.equal(clampSemitones(3.6), 4);
  assert.equal(clampSemitones(99), 12);
  assert.equal(clampSemitones(-99), -12);
  assert.equal(clampSemitones("5"), 5);
  assert.equal(clampSemitones("nope"), 0);
});

test("formatSemitones uses a sign only for positive values", () => {
  assert.equal(formatSemitones(0), "0 st");
  assert.equal(formatSemitones(4), "+4 st");
  assert.equal(formatSemitones(-4), "-4 st");
});

test("describePipelineState provides stable UI states", () => {
  assert.equal(describePipelineState("ready").className, "ready");
  assert.equal(describePipelineState("setup-required").className, "warning");
  assert.equal(describePipelineState("error").className, "error");
  assert.equal(describePipelineState("unknown").className, "offline");
});

test("formatUpdateAction only shows actionable update states", () => {
  assert.equal(formatUpdateAction("available", "1.3.0"), "Update to v1.3.0");
  assert.equal(formatUpdateAction("downloading", "1.3.0"), "Downloading...");
  assert.equal(formatUpdateAction("installing", "1.3.0"), "Installing...");
  assert.equal(formatUpdateAction("up-to-date", "1.2.0"), null);
});
