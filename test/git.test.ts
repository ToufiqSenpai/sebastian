import { describe, expect, test } from "bun:test"
import { BunServices } from "@effect/platform-bun"
import { Effect, FileSystem, Path } from "effect"
import type { ChildProcessSpawner } from "effect/process"
import {
  commitAll,
  ensureGitIdentity,
  initRepo,
  inspectGit
} from "../src/libs/git"
import { runProcess } from "../src/process"

const withTempRepo = <A, E>(
  use: (
    directory: string
  ) => Effect.Effect<
    A,
    E,
    FileSystem.FileSystem | Path.Path | ChildProcessSpawner.ChildProcessSpawner
  >
) =>
  Effect.gen(function* () {
    const fs = yield* FileSystem.FileSystem
    const directory = yield* fs.makeTempDirectoryScoped({
      prefix: "sebastian-git-"
    })
    return yield* use(directory)
  }).pipe(Effect.scoped, Effect.provide(BunServices.layer))

describe("git", () => {
  test("initializes and inspects a repository", async () => {
    const result = await Effect.runPromise(
      withTempRepo((directory) =>
        Effect.gen(function* () {
          const path = yield* Path.Path
          const before = yield* inspectGit(directory)
          yield* initRepo(directory)
          const after = yield* inspectGit(directory)
          return { before, after, expectedRoot: path.resolve(directory) }
        })
      )
    )

    expect(result.before.isRepo).toBe(false)
    expect(result.after.isRepo).toBe(true)
    expect(result.after.hasCommits).toBe(false)
    expect(result.after.hasOrigin).toBe(false)
    expect(result.after.root.toLowerCase()).toBe(
      result.expectedRoot.toLowerCase()
    )
  })

  test("creates a commit and sees it", async () => {
    const result = await Effect.runPromise(
      withTempRepo((directory) =>
        Effect.gen(function* () {
          const fs = yield* FileSystem.FileSystem
          yield* initRepo(directory)
          yield* fs.writeFileString(`${directory}/hello.txt`, "hello")
          const name = yield* runProcess(
            "git",
            ["config", "user.name", "Sebastian Test"],
            directory
          )
          const email = yield* runProcess(
            "git",
            ["config", "user.email", "sebastian@example.com"],
            directory
          )
          yield* ensureGitIdentity(directory)
          const committed = yield* commitAll(directory, "test commit")
          const state = yield* inspectGit(directory)
          return { identity: [name, email], committed, state }
        })
      )
    )

    expect(result.identity.every((result) => result.exitCode === 0)).toBe(true)
    expect(result.committed).toBe(true)
    expect(result.state.hasCommits).toBe(true)
  })

  test("detects an origin and a nested repository", async () => {
    const result = await Effect.runPromise(
      withTempRepo((directory) =>
        Effect.gen(function* () {
          const fs = yield* FileSystem.FileSystem
          const path = yield* Path.Path
          yield* initRepo(directory)
          const add = yield* runProcess(
            "git",
            ["remote", "add", "origin", "https://example.com/example/repo.git"],
            directory
          )
          const nested = path.join(directory, "nested")
          yield* fs.makeDirectory(nested, { recursive: true })
          const state = yield* inspectGit(nested)
          return { add, state, root: path.resolve(directory) }
        })
      )
    )

    expect(result.add.exitCode).toBe(0)
    expect(result.state.isRepo).toBe(true)
    expect(result.state.hasOrigin).toBe(true)
    expect(result.state.root.toLowerCase()).toBe(result.root.toLowerCase())
  })
})
