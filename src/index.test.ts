import { describe, expect, test } from "bun:test"
import { mkdtempSync, rmSync } from "node:fs"
import { tmpdir } from "node:os"
import path from "node:path"
import pkg from "../package.json"

const repoRoot = path.resolve(import.meta.dir, "..")

const run = (args: readonly string[], cwd: string = repoRoot) =>
  Bun.spawnSync({
    cmd: [process.execPath, path.join(repoRoot, "src/index.ts"), ...args],
    cwd,
    stdin: "ignore",
    stdout: "pipe",
    stderr: "pipe"
  })

describe("CLI", () => {
  test("prints help with the init command", () => {
    const result = run(["--help"])
    const output = result.stdout.toString()
    expect(result.exitCode).toBe(0)
    expect(output).toContain("init")
    expect(output).toContain("agent-driven work")
  })

  test("prints the version", () => {
    const result = run(["--version"])
    expect(result.stdout.toString().trim()).toBe(`sebastian v${pkg.version}`)
  })

  test("refuses to prompt without a terminal", () => {
    const directory = mkdtempSync(path.join(tmpdir(), "sebastian-cli-"))
    try {
      const result = run(["init"], directory)
      const output = `${result.stdout.toString()}${result.stderr.toString()}`
      expect(result.exitCode).toBe(1)
      expect(output).toContain("requires an interactive terminal")
    } finally {
      rmSync(directory, { recursive: true, force: true })
    }
  })
})
