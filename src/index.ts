import { BunRuntime, BunServices } from "@effect/platform-bun"
import { Effect, Layer } from "effect"
import { Command } from "effect/cli"
import pkg from "../package.json"
import { LoggerLayer } from "./common/logger"
import { formatError } from "./errors"
import { runInit } from "./commands/init"
import { Git } from "./libs/git"
import { GitHubCli } from "./libs/github-cli"

const reportFailure = (error: {
  readonly message: string
  readonly hint?: string
}) =>
  Effect.logError(formatError(error)).pipe(
    Effect.andThen(
      Effect.sync(() => {
        process.exitCode = 1
      })
    )
  )

const init = Command.make("init", {}, () =>
  runInit.pipe(Effect.catchTag("InitError", reportFailure))
).pipe(
  Command.withDescription("Initialize the current directory for agent-driven work")
)

const sebastian = Command.make("sebastian").pipe(
  Command.withDescription("Agent workflow CLI"),
  Command.withSubcommands([init])
)

BunRuntime.runMain(
  Command.run(sebastian, { version: pkg.version }).pipe(
    Effect.provide(
      Layer.mergeAll(Git.layer, GitHubCli.layer, LoggerLayer).pipe(
        Layer.provideMerge(BunServices.layer)
      )
    )
  )
)
