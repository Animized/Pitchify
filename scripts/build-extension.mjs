import { build } from "esbuild";
import { mkdir, readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const repoRoot = fileURLToPath(new URL("../", import.meta.url));
const entryPoint = fileURLToPath(
  new URL("../extension/src/index.ts", import.meta.url),
);
const outputFile = fileURLToPath(
  new URL("../dist/pitchify.template.js", import.meta.url),
);
const packageJson = JSON.parse(
  await readFile(new URL("../package.json", import.meta.url), "utf8"),
);
await mkdir(new URL("../dist/", import.meta.url), { recursive: true });

await build({
  absWorkingDir: repoRoot,
  entryPoints: [entryPoint],
  outfile: outputFile,
  bundle: true,
  minify: false,
  format: "iife",
  target: ["chrome100"],
  legalComments: "eof",
  banner: {
    js: `/* Pitchify v${packageJson.version} - MIT License */`,
  },
});

console.log("Built dist/pitchify.template.js");
