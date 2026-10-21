import { Data } from "effect"

export class InitError extends Data.TaggedError("InitError")<{
  readonly message: string
  readonly hint?: string
}> {}

export const formatError = (error: {
  readonly message: string
  readonly hint?: string
}): string =>
  error.hint === undefined ? error.message : `${error.message}\n\n${error.hint}`
