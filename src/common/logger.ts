import { Config, Effect, Layer, Logger, References } from "effect"
import type { LogLevel } from "effect/LogLevel"

const consoleLogger =
  process.stderr.isTTY === true
    ? Logger.consolePretty()
    : Logger.withConsoleError(Logger.formatJson)

const logLevelLayer = Layer.unwrap(
  Effect.gen(function* () {
    const level = yield* Config.LogLevel("LOG_LEVEL").pipe(
      Config.withDefault("Info"),
      Effect.orElseSucceed((): LogLevel => "Info")
    )

    return Layer.succeed(References.MinimumLogLevel, level)
  })
)

export const LoggerLayer = Layer.mergeAll(
  Logger.layer([consoleLogger, Logger.tracerLogger]),
  Layer.succeed(Logger.LogToStderr, true),
  logLevelLayer
)
