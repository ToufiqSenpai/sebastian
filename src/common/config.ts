import { Config, ConfigProvider, Effect, FileSystem } from "effect"
import { StartError } from "../errors"

export interface AppConfig {
  readonly githubAppId: string
  readonly githubAppPrivateKeyPath: string
}

/** Read the app-level configuration from the environment or `.env`. */
export const appConfig: Effect.Effect<
  AppConfig,
  StartError,
  FileSystem.FileSystem
> = Effect.gen(function* () {
  const dotEnv = yield* ConfigProvider.fromDotEnv().pipe(
    Effect.orElseSucceed(() => ConfigProvider.fromDotEnvContents(""))
  )
  const provider = ConfigProvider.orElse(ConfigProvider.fromEnv(), dotEnv)

  return yield* Config.all({
    githubAppId: Config.String("GITHUB_APP_ID"),
    githubAppPrivateKeyPath: Config.String("GITHUB_APP_PRIVATE_KEY_PATH")
  })
    .parse(provider)
    .pipe(
      Effect.mapError(
        (error) =>
          new StartError({
            message: "The GitHub App credentials are not set.",
            hint: `Set GITHUB_APP_ID and GITHUB_APP_PRIVATE_KEY_PATH, or put them in .env.\n${error.message}`
          })
      )
    )
})
