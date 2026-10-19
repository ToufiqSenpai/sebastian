import { BunRuntime, BunServices } from "@effect/platform-bun"
import { Effect } from "effect"
import { Command } from "effect/cli"
import pkg from "../package.json"
import { formatInitError } from "./errors"
import { runInit } from "./commands/init"

const init = Command.make("init", {}, () =>
  runInit.pipe(
    Effect.catchTag("InitError", (error) =>
      Effect.sync(() => {
        console.error(formatInitError(error))
        process.exitCode = 1
      })
    )
  )
).pipe(
  Command.withDescription("Initialize the current directory for agent-driven work")
)

const sebastian = Command.make("sebastian").pipe(
  Command.withDescription("Agent workflow CLI"),
  Command.withSubcommands([init])
)

BunRuntime.runMain(
  Command.run(sebastian, { version: pkg.version }).pipe(
    Effect.provide(BunServices.layer)
  )
)
