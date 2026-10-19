import { describe, expect, test } from "bun:test"
import { BunServices } from "@effect/platform-bun"
import { Effect, FileSystem, Path } from "effect"
import { ensureAgentsIgnored } from "./init"

const withTempDir = <A, E>(
  use: (
    directory: string
  ) => Effect.Effect<A, E, FileSystem.FileSystem | Path.Path>
) =>
  Effect.gen(function* () {
    const fs = yield* FileSystem.FileSystem
    const directory = yield* fs.makeTempDirectoryScoped({
      prefix: "sebastian-init-"
    })
    return yield* use(directory)
  }).pipe(Effect.scoped, Effect.provide(BunServices.layer))

describe("ensureAgentsIgnored", () => {
  test("creates .gitignore once", async () => {
    const result = await Effect.runPromise(
      withTempDir((directory) =>
        Effect.gen(function* () {
          const fs = yield* FileSystem.FileSystem
          yield* ensureAgentsIgnored(directory)
          yield* ensureAgentsIgnored(directory)
          return yield* fs.readFileString(`${directory}/.gitignore`)
        })
      )
    )

    expect(result).toBe("# Agents\n.agents/*\n")
  })

  test("appends below existing content", async () => {
    const result = await Effect.runPromise(
      withTempDir((directory) =>
        Effect.gen(function* () {
          const fs = yield* FileSystem.FileSystem
          yield* fs.writeFileString(
            `${directory}/.gitignore`,
            "node_modules/\ndist/"
          )
          yield* ensureAgentsIgnored(directory)
          return yield* fs.readFileString(`${directory}/.gitignore`)
        })
      )
    )

    expect(result).toBe("node_modules/\ndist/\n\n# Agents\n.agents/*\n")
  })
})
