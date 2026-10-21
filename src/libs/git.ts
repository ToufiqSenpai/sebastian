import { Context, Effect, Layer, Path, PlatformError } from "effect"
import { ChildProcessSpawner } from "effect/process"
import { InitError } from "../errors"
import { runProcess, type ProcessResult } from "../process"

export interface GitStatus {
  readonly isRepository: boolean
  readonly root: string
  readonly hasCommits: boolean
  readonly hasOrigin: boolean
}

const gitFailure = (
  error: InitError | PlatformError.PlatformError
): InitError =>
  error instanceof InitError
    ? error
    : new InitError({
        message: "git is required to initialize a project.",
        hint: "Install Git and make sure it is available on PATH, then retry."
      })

const commandFailure = (label: string, result: ProcessResult): InitError =>
  new InitError({
    message: `${label} failed (exit code ${result.exitCode}).`,
    hint: (result.stderr || result.stdout).trim() || undefined
  })

export class Git extends Context.Service<
  Git,
  {
    status(cwd: string): Effect.Effect<GitStatus, InitError>
    init(cwd: string): Effect.Effect<void, InitError>
    commit(cwd: string, message: string): Effect.Effect<void, InitError>
  }
>()("Git") {
  public static readonly layer = Layer.effect(
    Git,
    Effect.gen(function* () {
      const path = yield* Path.Path
      const spawner = yield* ChildProcessSpawner.ChildProcessSpawner

      return Git.of({
        status: (cwd) =>
          Effect.gen(function* () {
            const inside = (
              yield* runProcess(
                "git",
                ["rev-parse", "--is-inside-work-tree"],
                cwd
              )
            ).stdout.trim()
            if (inside !== "true") {
              return {
                isRepository: false,
                root: "",
                hasCommits: false,
                hasOrigin: false
              }
            }

            const root = (
              yield* runProcess("git", ["rev-parse", "--show-toplevel"], cwd)
            ).stdout.trim()
            if (root.length === 0) {
              return yield* new InitError({
                message: "Could not determine the git repository root.",
                hint: "Run `git status` in this directory to inspect the repository."
              })
            }

            const head = (
              yield* runProcess("git", ["rev-parse", "--verify", "HEAD"], cwd)
            ).stdout.trim()
            const origin = (
              yield* runProcess("git", ["remote", "get-url", "origin"], cwd)
            ).stdout.trim()

            return {
              isRepository: true,
              root: path.resolve(root),
              hasCommits: head.length > 0,
              hasOrigin: origin.length > 0
            }
          }).pipe(
            Effect.mapError(gitFailure),
            Effect.provideService(
              ChildProcessSpawner.ChildProcessSpawner,
              spawner
            )
          ),
        init: (cwd) =>
          Effect.gen(function* () {
            const result = yield* runProcess("git", ["init", "-b", "main"], cwd)
            if (result.exitCode !== 0) {
              return yield* commandFailure("git init", result)
            }
          }).pipe(
            Effect.mapError(gitFailure),
            Effect.provideService(
              ChildProcessSpawner.ChildProcessSpawner,
              spawner
            )
          ),
        commit: (cwd, message) =>
          Effect.gen(function* () {
            const added = yield* runProcess("git", ["add", "-A"], cwd)
            if (added.exitCode !== 0) {
              return yield* commandFailure("git add", added)
            }
            const committed = yield* runProcess(
              "git",
              ["commit", "-m", message],
              cwd
            )
            if (committed.exitCode !== 0) {
              return yield* commandFailure("git commit", committed)
            }
          }).pipe(
            Effect.mapError(gitFailure),
            Effect.provideService(
              ChildProcessSpawner.ChildProcessSpawner,
              spawner
            )
          )
      })
    })
  )
}
