import { Effect, Stream } from "effect"
import { ChildProcess, ChildProcessSpawner } from "effect/process"

export interface ProcessResult {
  readonly exitCode: number
  readonly stdout: string
  readonly stderr: string
}

export const runProcess = (
  command: string,
  args: readonly string[],
  cwd: string
) =>
  Effect.scoped(
    Effect.gen(function* () {
      const spawner = yield* ChildProcessSpawner.ChildProcessSpawner
      const handle = yield* spawner.spawn(
        ChildProcess.make(command, args, {
          cwd,
          stdin: "ignore",
          stdout: "pipe",
          stderr: "pipe"
        })
      )
      const [exitCode, stdout, stderr] = yield* Effect.all(
        [
          handle.exitCode,
          Stream.mkString(Stream.decodeText(handle.stdout)),
          Stream.mkString(Stream.decodeText(handle.stderr))
        ] as const,
        { concurrency: "unbounded" }
      )
      return { exitCode: Number(exitCode), stdout, stderr }
    })
  )
