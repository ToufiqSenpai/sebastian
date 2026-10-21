import { createAppAuth } from "@octokit/auth-app"
import { Context, Effect, FileSystem, Layer } from "effect"
import { Octokit, RequestError } from "octokit"
import { appConfig } from "../common/config"
import { StartError } from "../errors"

interface AppCredentials {
  readonly appId: string
  readonly privateKey: string
}

/** Read the GitHub App credentials sebastian runs as. */
const appCredentials: Effect.Effect<
  AppCredentials,
  StartError,
  FileSystem.FileSystem
> = Effect.gen(function* () {
  const { githubAppId, githubAppPrivateKeyPath } = yield* appConfig
  const fs = yield* FileSystem.FileSystem
  const privateKey = yield* fs
    .readFileString(githubAppPrivateKeyPath)
    .pipe(
      Effect.mapError(
        (error) =>
          new StartError({
            message: `Could not read the GitHub App private key at ${githubAppPrivateKeyPath}.`,
            hint: String(error)
          })
      )
    )

  return { appId: githubAppId, privateKey }
})

/** One issue exactly as Octokit types it. */
export type GitHubIssue = Awaited<
  ReturnType<Octokit["rest"]["issues"]["get"]>
>["data"]

export interface RepoContext {
  readonly octokit: Octokit
  readonly owner: string
  readonly repo: string
}

const isNotFound = (cause: unknown): boolean =>
  cause instanceof RequestError && cause.status === 404

const requestData = <T>(
  request: () => PromiseLike<T>,
  message: string
): Effect.Effect<T, StartError> =>
  Effect.tryPromise({
    try: request,
    catch: (cause) =>
      new StartError({
        message,
        hint: cause instanceof Error ? cause.message : String(cause)
      })
  })

/** Authenticate as the GitHub App and mint an installation access token. */
const connectGitHubApp = (
  credentials: AppCredentials,
  owner: string,
  repo: string
): Effect.Effect<string, StartError> =>
  Effect.gen(function* () {
    const auth = createAppAuth({
      appId: credentials.appId,
      privateKey: credentials.privateKey
    })
    const appAuthentication = yield* Effect.tryPromise({
      try: () => auth({ type: "app" }),
      catch: (cause) =>
        new StartError({
          message: "Could not authenticate as the GitHub App.",
          hint: cause instanceof Error ? cause.message : String(cause)
        })
    })
    const appOctokit = new Octokit({ auth: appAuthentication.token })
    const installation = yield* requestData(
      () =>
        appOctokit.rest.apps
          .getRepoInstallation({ owner, repo })
          .then((response) => response.data),
      `The GitHub App is not installed on ${owner}/${repo}.`
    )
    const installationAuthentication = yield* Effect.tryPromise({
      try: () => auth({ type: "installation", installationId: installation.id }),
      catch: (cause) =>
        new StartError({
          message: "Could not mint an installation access token.",
          hint: cause instanceof Error ? cause.message : String(cause)
        })
    })

    return installationAuthentication.token
  })

export class GitHubApp extends Context.Service<
  GitHubApp,
  {
    /** The repository the app is installed on. */
    readonly context: RepoContext
    getIssue(number: number): Effect.Effect<GitHubIssue, StartError>
    getParentIssueNumber(
      number: number
    ): Effect.Effect<number | undefined, StartError>
    getSubIssues(
      number: number
    ): Effect.Effect<readonly GitHubIssue[], StartError>
    listIssuesByLabel(
      label: string
    ): Effect.Effect<readonly GitHubIssue[], StartError>
    readonly getDefaultBranch: Effect.Effect<string, StartError>
  }
>()("GitHubApp") {
  public static readonly layer = (owner: string, repo: string) =>
    Layer.effect(
      GitHubApp,
      Effect.gen(function* () {
        const credentials = yield* appCredentials
        const token = yield* connectGitHubApp(credentials, owner, repo)
        const octokit = new Octokit({ auth: token })

        return GitHubApp.of({
          context: { octokit, owner, repo },
          getIssue: (number) =>
            requestData(
              () =>
                octokit.rest.issues
                  .get({ owner, repo, issue_number: number })
                  .then((response) => response.data),
              `Could not read issue #${number}.`
            ),
          getParentIssueNumber: (number) =>
            requestData(
              () =>
                octokit.rest.issues
                  .getParent({ owner, repo, issue_number: number })
                  .then((response) => response.data)
                  .catch((cause) => {
                    if (isNotFound(cause)) return undefined
                    throw cause
                  }),
              `Could not read the parent of issue #${number}.`
            ).pipe(Effect.map((parent) => parent?.number)),
          getSubIssues: (number) =>
            requestData(
              () =>
                octokit.rest.issues
                  .listSubIssues({
                    owner,
                    repo,
                    issue_number: number,
                    per_page: 100
                  })
                  .then((response) => response.data),
              `Could not read the sub-issues of issue #${number}.`
            ),
          listIssuesByLabel: (label) =>
            requestData(
              () =>
                octokit.rest.issues
                  .listForRepo({
                    owner,
                    repo,
                    labels: label,
                    state: "open",
                    per_page: 100
                  })
                  .then((response) => response.data),
              `Could not list issues labelled "${label}".`
            ).pipe(
              Effect.map((issues) =>
                issues.filter((issue) => issue.pull_request === undefined)
              )
            ),
          getDefaultBranch: requestData(
            () =>
              octokit.rest.repos
                .get({ owner, repo })
                .then((response) => response.data),
            `Could not read ${owner}/${repo}.`
          ).pipe(Effect.map((repository) => repository.default_branch))
        })
      })
    )
}
