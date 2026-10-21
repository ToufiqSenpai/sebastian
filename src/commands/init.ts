import { Effect, FileSystem, Path, Terminal } from "effect"
import { Prompt } from "effect/cli"
import { InitError } from "../errors"
import { copyAssets, type CopyReport } from "../files"
import {
  GitHubCli,
  type Repository,
  type RepositoryVisibility
} from "../libs/github-cli"
import { Git, type GitStatus } from "../libs/git"

const ask = <A>(prompt: Prompt.Prompt<A>) =>
  Prompt.run(prompt).pipe(
    Effect.catchTag("QuitError", () =>
      Effect.fail(new InitError({ message: "\nCancelled." }))
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

export const ensureAgentsIgnored = (
  targetDir: string
): Effect.Effect<void, InitError, FileSystem.FileSystem | Path.Path> =>
  Effect.gen(function* () {
    const fs = yield* FileSystem.FileSystem
    const path = yield* Path.Path
    const file = path.join(targetDir, ".gitignore")
    const existing = (yield* fs.exists(file))
      ? yield* fs.readFileString(file)
      : ""
    if (existing.split("\n").some((line) => line.trim() === ".agents/*")) {
      return
    }

    let separator = ""
    if (existing.length > 0) {
      separator = existing.endsWith("\n") ? "\n" : "\n\n"
    }
    yield* fs.writeFileString(
      file,
      `${existing}${separator}# Agents\n.agents/*\n`
    )
  }).pipe(
    Effect.mapError(
      (error) =>
        new InitError({
          message: "Could not update .gitignore.",
          hint: String(error)
        })
    )
  )

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
  readonly git: GitStatus
  readonly gitInitialized: boolean
  readonly report: CopyReport
  readonly repository: Repository | undefined
}

const gitLabel = (git: GitStatus, initialized: boolean): string => {
  if (initialized) return "initialized"
  return git.isRepository ? "already initialized" : "skipped"
}

const printSummary = (summary: Summary) =>
  Effect.gen(function* () {
    const { cwd, git, gitInitialized, report, repository } = summary
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
      `  Git: ${gitLabel(git, gitInitialized)}`
    )
    if (git.hasOrigin) {
      lines.push("  GitHub: origin already configured (skipped)")
    } else if (repository !== undefined) {
      lines.push(`  GitHub: ${repository.url ?? "repository created"}`)
    } else {
      lines.push("  GitHub: skipped")
    }

    if (git.hasCommits || git.hasOrigin) {
      lines.push(
        "",
        "  Next:",
        '    git add -A && git commit -m "chore: initialize project"',
        "    git push -u origin HEAD"
      )
    }

    yield* Effect.logInfo(lines.join("\n"))
  })

export const runInit: Effect.Effect<
  void,
  InitError,
  FileSystem.FileSystem | Path.Path | Terminal.Terminal | Git | GitHubCli
> = Effect.gen(function* () {
  yield* ensureInteractive

  const path = yield* Path.Path
  const fs = yield* FileSystem.FileSystem
  const gitService = yield* Git
  const github = yield* GitHubCli
  const cwd = process.cwd()
  yield* Effect.logInfo(`sebastian init\nDirectory: ${cwd}`)

  const git = yield* gitService.status(cwd)
  if (git.isRepository && path.resolve(git.root) !== path.resolve(cwd)) {
    return yield* new InitError({
      message: `This directory is inside the git repository at ${git.root}.`,
      hint: "Run `sebastian init` from the repository root or use a new directory."
    })
  }

  if (!git.hasOrigin) {
    yield* github.status(cwd)
  }

  let repoName: string | undefined
  let visibility: RepositoryVisibility = "private"
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

  const agentsPath = path.join(cwd, "AGENTS.md")
  const hasAgents = yield* fs.exists(agentsPath).pipe(
    Effect.mapError(
      (error) =>
        new InitError({
          message: "Could not check for an existing AGENTS.md.",
          hint: String(error)
        })
    )
  )
  let overwriteAgents = false
  if (hasAgents) {
    overwriteAgents = yield* ask(
      Prompt.Confirm({
        message: "AGENTS.md already exists. Overwrite it?",
        initial: false
      })
    )
  }

  const report = yield* copyAssets(cwd, overwriteAgents)
  yield* ensureAgentsIgnored(cwd)

  let gitInitialized = false
  if (!git.isRepository) {
    yield* gitService.init(cwd)
    gitInitialized = true
  }

  if (!git.hasCommits) {
    yield* gitService.commit(cwd, "chore: initial commit")
  }

  let repository: Repository | undefined
  if (!git.hasOrigin && repoName !== undefined) {
    repository = yield* github.createRepository(repoName, visibility, cwd)
  }

  yield* printSummary({
    cwd,
    git,
    gitInitialized,
    report,
    repository
  })
})
