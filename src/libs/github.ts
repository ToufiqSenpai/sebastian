import { Effect } from "effect"
import { InitError } from "../errors"
import { runProcess } from "../process"

export type Visibility = "private" | "public"

export interface Repository {
  readonly url: string | undefined
}

export const ghRepoCreateArgs = (
  name: string,
  visibility: Visibility,
  cwd: string,
  push: boolean
): readonly string[] => [
  "repo",
  "create",
  name,
  visibility === "private" ? "--private" : "--public",
  "--source",
  cwd,
  "--remote",
  "origin",
  ...(push ? ["--push"] : [])
]

// Trailing punctuation is stripped by hand: `[.,)]+$` is a super-linear regex.
const trimPunctuation = (url: string): string => {
  let end = url.length
  while (end > 0 && ".,)".includes(url.charAt(end - 1))) end -= 1
  return url.slice(0, end)
}

export const parseRepoUrl = (output: string): string | undefined => {
  const url = output.match(/https?:\/\/\S+/g)?.at(-1)
  return url === undefined ? undefined : trimPunctuation(url)
}

const ghFailure = (): InitError =>
  new InitError({
    message: "GitHub CLI is required to create a repository.",
    hint:
      "Install the GitHub CLI (https://cli.github.com), run `gh auth login`, then retry."
  })

export const checkGh = (cwd: string) =>
  runProcess("gh", ["auth", "status"], cwd).pipe(
    Effect.mapError(ghFailure),
    Effect.flatMap((result) =>
      result.exitCode === 0
        ? Effect.void
        : Effect.fail(
            new InitError({
              message: "GitHub CLI is not authenticated.",
              hint: "Run `gh auth login`, then retry."
            })
          )
    )
  )

export const createRepo = (
  name: string,
  visibility: Visibility,
  cwd: string,
  push: boolean
) =>
  runProcess("gh", ghRepoCreateArgs(name, visibility, cwd, push), cwd).pipe(
    Effect.mapError(ghFailure),
    Effect.flatMap((result) =>
      result.exitCode === 0
        ? Effect.succeed({ url: parseRepoUrl(result.stdout) })
        : Effect.fail(
            new InitError({
              message: `Could not create or push the GitHub repository "${name}".`,
              hint:
                (result.stderr || result.stdout).trim() ||
                "Run `gh repo create` or `git push` manually to see the full error."
            })
          )
    )
  )
