import { describe, expect, test } from "bun:test"
import { ghRepoCreateArgs, parseRepoUrl } from "./github"

describe("GitHub repository creation", () => {
  test("builds private push arguments", () => {
    const args = ghRepoCreateArgs("demo", "private", "C:\\work\\demo", true)
    expect(args).toContain("demo")
    expect(args).toContain("--private")
    expect(args).toContain("--push")
    expect(args).toContain("--source")
    expect(args).toContain("--remote")
    expect(args).toContain("origin")
  })

  test("builds public no-push arguments", () => {
    const args = ghRepoCreateArgs("demo", "public", "/work/demo", false)
    expect(args).toContain("--public")
    expect(args).not.toContain("--push")
  })

  test("extracts the repository URL from gh output", () => {
    expect(
      parseRepoUrl(
        "✓ Created repository owner/demo on GitHub\nhttps://github.com/owner/demo\n"
      )
    ).toBe("https://github.com/owner/demo")
    expect(parseRepoUrl("no link here")).toBeUndefined()
  })
})
