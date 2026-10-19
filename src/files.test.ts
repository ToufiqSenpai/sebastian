import { describe, expect, test } from "bun:test"
import { BunServices } from "@effect/platform-bun"
import { Effect, FileSystem, Path } from "effect"
import { copyAssets } from "./files"

const withTempDir = <A, E>(
  use: (
    directory: string
  ) => Effect.Effect<A, E, FileSystem.FileSystem | Path.Path>
) =>
  Effect.gen(function* () {
    const fs = yield* FileSystem.FileSystem
    const directory = yield* fs.makeTempDirectoryScoped({
      prefix: "sebastian-test-"
    })
    return yield* use(directory)
  }).pipe(Effect.scoped, Effect.provide(BunServices.layer))

describe("copyAssets", () => {
  test("copies templates and skills into a fresh directory", async () => {
    const result = await Effect.runPromise(
      withTempDir((directory) =>
        Effect.gen(function* () {
          const fs = yield* FileSystem.FileSystem
          const report = yield* copyAssets(directory)
          const agents = yield* fs.readFileString(
            `${directory}/AGENTS.md`
          )
          const adr = yield* fs.readFileString(
            `${directory}/docs/agents/adr-format.md`
          )
          const skill = yield* fs.readFileString(
            `${directory}/.agents/skills/code-review/SKILL.md`
          )
          return { report, agents, adr, skill }
        })
      )
    )

    expect(result.report.copied).toContain("AGENTS.md")
    expect(result.report.copied).toContain("docs/agents/adr-format.md")
    expect(result.report.copied).toContain(
      ".agents/skills/code-review/SKILL.md"
    )
    expect(result.agents.length).toBeGreaterThan(0)
    expect(result.adr.length).toBeGreaterThan(0)
    expect(result.skill).toContain("code-review")
  })

  test("does not overwrite existing files", async () => {
    const result = await Effect.runPromise(
      withTempDir((directory) =>
        Effect.gen(function* () {
          const fs = yield* FileSystem.FileSystem
          yield* copyAssets(directory)
          yield* fs.writeFileString(`${directory}/AGENTS.md`, "custom")
          const report = yield* copyAssets(directory)
          const agents = yield* fs.readFileString(`${directory}/AGENTS.md`)
          return { report, agents }
        })
      )
    )

    expect(result.agents).toBe("custom")
    expect(result.report.skipped).toContain("AGENTS.md")
    expect(result.report.copied).not.toContain("AGENTS.md")
  })

  test("skips an existing skill directory as a whole", async () => {
    const result = await Effect.runPromise(
      withTempDir((directory) =>
        Effect.gen(function* () {
          const fs = yield* FileSystem.FileSystem
          const skillDir = `${directory}/.agents/skills/code-review`
          yield* fs.makeDirectory(skillDir, { recursive: true })
          yield* fs.writeFileString(`${skillDir}/sentinel.txt`, "keep me")
          const report = yield* copyAssets(directory)
          const skillExists = yield* fs.exists(`${skillDir}/SKILL.md`)
          const sentinel = yield* fs.readFileString(`${skillDir}/sentinel.txt`)
          return { report, skillExists, sentinel }
        })
      )
    )

    expect(result.report.skipped).toContain(".agents/skills/code-review")
    expect(result.skillExists).toBe(false)
    expect(result.sentinel).toBe("keep me")
  })
})
