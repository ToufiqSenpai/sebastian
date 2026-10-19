import { Effect, FileSystem, Path, PlatformError } from "effect"
import { InitError } from "./errors"

export interface CopyReport {
  readonly copied: string[]
  readonly skipped: string[]
}

const copyFileIfMissing = (
  source: string,
  destination: string,
  overwrite = false
): Effect.Effect<
  "copied" | "skipped",
  PlatformError.PlatformError,
  FileSystem.FileSystem | Path.Path
> =>
  Effect.gen(function* () {
    const fs = yield* FileSystem.FileSystem
    const path = yield* Path.Path
    if (!overwrite && (yield* fs.exists(destination))) return "skipped" as const
    const data = yield* fs.readFile(source)
    yield* fs.makeDirectory(path.dirname(destination), { recursive: true })
    yield* fs.writeFile(destination, data)
    return "copied" as const
  })

const copyTree = (
  sourceDir: string,
  destinationDir: string,
  relativeBase: string
): Effect.Effect<
  CopyReport,
  PlatformError.PlatformError,
  FileSystem.FileSystem | Path.Path
> =>
  Effect.gen(function* () {
    const fs = yield* FileSystem.FileSystem
    const path = yield* Path.Path
    const report: CopyReport = { copied: [], skipped: [] }

    const entries = yield* fs.readDirectory(sourceDir)
    for (const name of entries.sort()) {
      const source = path.join(sourceDir, name)
      const destination = path.join(destinationDir, name)
      const relative = `${relativeBase}/${name}`
      const info = yield* fs.stat(source)
      if (info.type === "Directory") {
        const child = yield* copyTree(source, destination, relative)
        report.copied.push(...child.copied)
        report.skipped.push(...child.skipped)
      } else {
        const result = yield* copyFileIfMissing(source, destination)
        if (result === "copied") report.copied.push(relative)
        else report.skipped.push(relative)
      }
    }

    return report
  })

export const resolveAssetsRoot: Effect.Effect<
  string,
  InitError,
  FileSystem.FileSystem | Path.Path
> = Effect.gen(function* () {
  const fs = yield* FileSystem.FileSystem
  const path = yield* Path.Path
  const candidates = Bun.isStandaloneExecutable
    ? [import.meta.dir, path.resolve(import.meta.dir, "..")]
    : [path.resolve(import.meta.dir, "..")]

  for (const root of candidates) {
    if (yield* fs.exists(path.join(root, "templates", "AGENTS.md"))) return root
  }

  return yield* new InitError({
    message: "Project templates were not found.",
    hint: "The binary is missing its embedded assets. Rebuild it with `bun run build`."
  })
}).pipe(
  Effect.mapError((error) =>
    error instanceof InitError
      ? error
      : new InitError({
          message: "Could not read the embedded project templates.",
          hint: String(error)
        })
  )
)

export const copyAssets = (
  targetDir: string,
  overwriteAgents = false
): Effect.Effect<CopyReport, InitError, FileSystem.FileSystem | Path.Path> =>
  Effect.gen(function* () {
    const fs = yield* FileSystem.FileSystem
    const path = yield* Path.Path
    const root = yield* resolveAssetsRoot
    const report: CopyReport = { copied: [], skipped: [] }

    const agents = yield* copyFileIfMissing(
      path.join(root, "templates", "AGENTS.md"),
      path.join(targetDir, "AGENTS.md"),
      overwriteAgents
    )
    if (agents === "copied") report.copied.push("AGENTS.md")
    else report.skipped.push("AGENTS.md")

    const docsSource = path.join(root, "templates", "docs", "agents")
    if (yield* fs.exists(docsSource)) {
      const docs = yield* copyTree(
        docsSource,
        path.join(targetDir, "docs", "agents"),
        "docs/agents"
      )
      report.copied.push(...docs.copied)
      report.skipped.push(...docs.skipped)
    }

    const skillsSource = path.join(root, "templates", "skills")
    if (yield* fs.exists(skillsSource)) {
      const entries = yield* fs.readDirectory(skillsSource)
      for (const name of entries.sort()) {
        const source = path.join(skillsSource, name)
        const info = yield* fs.stat(source)
        if (info.type !== "Directory") continue

        const relative = `.agents/skills/${name}`
        const destination = path.join(targetDir, ".agents", "skills", name)
        if (yield* fs.exists(destination)) {
          report.skipped.push(relative)
          continue
        }

        const skill = yield* copyTree(source, destination, relative)
        report.copied.push(...skill.copied)
      }
    }

    return report
  }).pipe(
    Effect.mapError((error) =>
      error instanceof InitError
        ? error
        : new InitError({
            message: "Could not copy the project templates.",
            hint: String(error)
          })
    )
  )
