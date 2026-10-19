import { Console, Effect, FileSystem, Path, Terminal } from "effect"
import { Prompt } from "effect/cli"
import { ChildProcessSpawner } from "effect/process"
import { InitError } from "../errors"
import { copyAssets, type CopyReport } from "../files"
import {
  checkGh,
  createRepo,
  type Repository,
  type Visibility
} from "../libs/github"
import {
  commitAll,
  ensureGitIdentity,
  initRepo,
  inspectGit,
  type GitState
} from "../libs/git"

const ask = <A>(prompt: Prompt.Prompt<A>) =>
  Prompt.run(prompt).pipe(
    Effect.catchTag("QuitError", () =>
      Effect.fail(new InitError({ message: "Cancelled." }))
    )
  )

const ensureInteractive = Effect.gen(function* () {
  if (!process.stdin.isTTY || !process.stdout.isTTY) {
    return yield* new InitError({
      message: "sebastian init requires an interactive terminal.",
      hint: "Run it directly in a terminal, not through a pipe or CI."
    })
  }
})

const trimDashes = (value: string): string => {
  let start = 0
  let end = value.length
  while (value.charAt(start) === "-") start += 1
  while (end > start && value.charAt(end - 1) === "-") end -= 1
  return value.slice(start, end)
}

// `^-+|-+$` backtracks super-linearly, so the dash trim is done by hand.
const suggestRepoName = (cwd: string, path: Path.Path): string =>
  trimDashes(
    path.basename(cwd).toLowerCase().replace(/[^a-z0-9._-]+/g, "-")
  )

const validateRepoName = (value: string): Effect.Effect<string, string> => {
  if (value.length === 0 || value === "." || value === "..") {
    return Effect.fail("Enter a repository name.")
  }
  if (value.length > 100 || !/^[A-Za-z0-9._-]+$/.test(value)) {
    return Effect.fail(
      "Use 1-100 letters, numbers, dots, hyphens, or underscores."
    )
  }
  return Effect.succeed(value)
}

interface Summary {
  readonly cwd: string
  readonly git: GitState
  readonly gitInitialized: boolean
  readonly report: CopyReport
  readonly commitRequested: boolean
  readonly commitCreated: boolean
  readonly repository: Repository | undefined
}

const gitLabel = (git: GitState, initialized: boolean): string => {
  if (initialized) return "initialized"
  return git.isRepo ? "already initialized" : "skipped"
}

const commitLabel = (requested: boolean, created: boolean): string => {
  if (created) return "created"
  return requested ? "no changes" : "skipped"
}

const printSummary = (summary: Summary) =>
  Effect.gen(function* () {
    const {
      cwd,
      git,
      gitInitialized,
      report,
      commitRequested,
      commitCreated,
      repository
    } = summary
    const lines = ["", "Summary", `  Directory: ${cwd}`]

    if (report.copied.length > 0) {
      lines.push("  Copied:")
      for (const path of report.copied) lines.push(`    + ${path}`)
    }
    if (report.skipped.length > 0) {
      lines.push("  Skipped (already exists):")
      for (const path of report.skipped) lines.push(`    = ${path}`)
    }

    lines.push(
      `  Git: ${gitLabel(git, gitInitialized)}`,
      `  Commit: ${commitLabel(commitRequested, commitCreated)}`
    )
    if (git.hasOrigin) {
      lines.push("  GitHub: origin already configured (skipped)")
    } else if (repository !== undefined) {
      lines.push(`  GitHub: ${repository.url ?? "repository created"}`)
    } else {
      lines.push("  GitHub: skipped")
    }

    if (!commitCreated && !git.hasCommits) {
      lines.push(
        "",
        "  No commit was created. When you are ready:",
        '    git add -A && git commit -m "chore: initialize project" && git push -u origin HEAD'
      )
    } else if (git.hasOrigin && commitCreated) {
      lines.push("", "  Next: git push -u origin HEAD")
    }

    yield* Console.log(lines.join("\n"))
  })

export const runInit: Effect.Effect<
  void,
  InitError,
  | FileSystem.FileSystem
  | Path.Path
  | Terminal.Terminal
  | ChildProcessSpawner.ChildProcessSpawner
> = Effect.gen(function* () {
  yield* ensureInteractive

  const path = yield* Path.Path
  const cwd = process.cwd()
  yield* Console.log(`sebastian init\nDirectory: ${cwd}`)

  const git = yield* inspectGit(cwd)
  if (git.isRepo && path.resolve(git.root) !== path.resolve(cwd)) {
    return yield* new InitError({
      message: `This directory is inside the git repository at ${git.root}.`,
      hint: "Run `sebastian init` from the repository root or use a new directory."
    })
  }

  if (!git.hasOrigin) {
    yield* checkGh(cwd)
  }

  let repoName: string | undefined
  let visibility: Visibility = "private"
  if (!git.hasOrigin) {
    const suggested = suggestRepoName(cwd, path)
    const namePrompt =
      suggested.length > 0
        ? { message: "GitHub repository name", default: suggested }
        : { message: "GitHub repository name" }
    repoName = yield* ask(
      Prompt.String({
        ...namePrompt,
        validate: validateRepoName
      })
    )
    visibility = yield* ask(
      Prompt.Select({
        message: "Repository visibility",
        choices: [
          { title: "Private", value: "private" as const },
          { title: "Public", value: "public" as const }
        ]
      })
    )
  }

  const shouldCommit = yield* ask(
    Prompt.Confirm({
      message: git.hasCommits
        ? "Commit all files?"
        : "Create an initial commit with all files?",
      initial: true
    })
  )

  if (shouldCommit) {
    yield* ensureGitIdentity(cwd)
  }

  const report = yield* copyAssets(cwd)

  let gitInitialized = false
  if (!git.isRepo) {
    yield* initRepo(cwd)
    gitInitialized = true
  }

  let commitCreated = false
  if (shouldCommit) {
    commitCreated = yield* commitAll(cwd, "chore: initialize project")
  }

  let repository: Repository | undefined
  if (!git.hasOrigin && repoName !== undefined) {
    repository = yield* createRepo(
      repoName,
      visibility,
      cwd,
      git.hasCommits || commitCreated
    )
  }

  yield* printSummary({
    cwd,
    git,
    gitInitialized,
    report,
    commitRequested: shouldCommit,
    commitCreated,
    repository
  })
})
