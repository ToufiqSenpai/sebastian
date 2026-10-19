import { Effect, Path } from "effect"
import { ChildProcessSpawner } from "effect/process"
import { InitError } from "../errors"
import { runProcess, runText, type ProcessResult } from "../process"

export interface GitState {
  readonly isRepo: boolean
  readonly root: string
  readonly hasCommits: boolean
  readonly hasOrigin: boolean
}

const gitFailure = (error: unknown): InitError =>
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

export const inspectGit = (
  cwd: string
): Effect.Effect<
  GitState,
  InitError,
  Path.Path | ChildProcessSpawner.ChildProcessSpawner
> =>
  Effect.gen(function* () {
    const path = yield* Path.Path
    const inside = yield* runText(
      "git",
      ["rev-parse", "--is-inside-work-tree"],
      cwd
    )
    if (inside !== "true") {
      return { isRepo: false, root: "", hasCommits: false, hasOrigin: false }
    }

    const root = yield* runText("git", ["rev-parse", "--show-toplevel"], cwd)
    if (root.length === 0) {
      return yield* new InitError({
        message: "Could not determine the git repository root.",
        hint: "Run `git status` in this directory to inspect the repository."
      })
    }

    const head = yield* runText("git", ["rev-parse", "--verify", "HEAD"], cwd)
    const origin = yield* runText("git", ["remote", "get-url", "origin"], cwd)
    return {
      isRepo: true,
      root: path.resolve(root),
      hasCommits: head.length > 0,
      hasOrigin: origin.length > 0
    }
  }).pipe(Effect.mapError(gitFailure))

export const initRepo = (cwd: string) =>
  Effect.gen(function* () {
    const result = yield* runProcess("git", ["init", "-b", "main"], cwd)
    if (result.exitCode !== 0) {
      return yield* commandFailure("git init", result)
    }
  }).pipe(Effect.mapError(gitFailure))

export const ensureGitIdentity = (cwd: string) =>
  Effect.gen(function* () {
    const name = yield* runText("git", ["config", "user.name"], cwd)
    const email = yield* runText("git", ["config", "user.email"], cwd)
    if (name.length === 0 || email.length === 0) {
      return yield* new InitError({
        message: "Git user.name and user.email are not configured.",
        hint:
          'Run:\n  git config --global user.name "Your Name"\n  git config --global user.email "you@example.com"'
      })
    }
  }).pipe(Effect.mapError(gitFailure))

export const commitAll = (
  cwd: string,
  message: string
): Effect.Effect<
  boolean,
  InitError,
  ChildProcessSpawner.ChildProcessSpawner
> =>
  Effect.gen(function* () {
    const status = yield* runText("git", ["status", "--porcelain"], cwd)
    if (status.length === 0) return false

    const add = yield* runProcess("git", ["add", "-A"], cwd)
    if (add.exitCode !== 0) {
      return yield* commandFailure("git add", add)
    }

    const commit = yield* runProcess("git", ["commit", "-m", message], cwd)
    if (commit.exitCode !== 0) {
      return yield* commandFailure("git commit", commit)
    }
    return true
  }).pipe(Effect.mapError(gitFailure))
