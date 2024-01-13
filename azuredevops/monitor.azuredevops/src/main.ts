import { Protocol,
          timeAndSend, tmpDir, StepFunction
       } from '@metrist/protocol'
import path = require('path')
import { spawnSync, SpawnSyncOptions } from 'child_process';
import { rm } from 'fs/promises';
import { randomBytes } from 'crypto';
import { writeFileSync } from 'fs';
import moment = require('moment')

const proto = new Protocol();
const steps: Record<string, StepFunction> = {}

const config = {
  runID: randomBytes(16).toString('hex'),
  personalAccessToken: '',
  organization: '',
  repository: '',
  tmpDir: ''
}
//
// Prefix for all branches created by this monitor
const branchPrefix = "monitor-repo-"

steps.CloneRepo = async function() {
  return timeAndSend(proto, async () => {
    if (runGitWithArgs(["clone", gitURL(), config.tmpDir]) != 0) {
      throw new Error("Clone failed with non 0 exit code.")
    }
  })
}

steps.PushCode = async function () {
  const options: SpawnSyncOptions = { cwd: config.tmpDir }
  const tempFilePath = path.join(config.tmpDir, "test.txt")
  runGitWithArgs(["checkout", "-b", getBranchName()], options)
  writeFileSync(tempFilePath, config.runID)
  runGitWithArgs(["add", tempFilePath], options)
  runGitWithArgs(["commit", "-am", `Add test.txt`], options)
  return timeAndSend(proto, async () => {
    if (runGitWithArgs(["push", gitURL(), "--all"], options) != 0) {
      throw new Error("Push failed with non 0 exit code")
    }
  })
}

steps.RemoveRemoteBranch = async function () {
  const options: SpawnSyncOptions = { cwd: config.tmpDir }
  return timeAndSend(proto, async () => {
    if (runGitWithArgs(["push", "origin", "--delete", getBranchName()], options) != 0) {
      throw new Error("Push failed with non 0 exit code")
    }
  })
}

const configCallback = async function(_config: any) {
  config.personalAccessToken = proto.getConfigValue('azuredevops', 'personalAccessToken') ?? ''
  config.organization = proto.getConfigValue('azuredevops', 'organization') ?? ''
  config.repository = proto.getConfigValue('azuredevops', 'repository') ?? ''
  config.tmpDir = tmpDir('azuredevops', _config.organization, _config.repository)
}

  // Look for branches older than an hour and delete them.
  // No namespace here as we use a separate AzureDevOps organization per environment (dev/prod) and we also restrict to a configurable repository
const cleanupHandler = async function() {
    try {
      proto.logInfo(`Running cleanup. Will remove any branches with a date before ${moment().subtract(1, 'hours').toISOString()}`)

      // Reclone as tearDownHandler would have removed the tmpDir. Since cleanup is not guaranteed to be enabled, tearDownHandler still has to do the tmpDir cleanup
      runGitWithArgs(["clone", gitURL(), config.tmpDir])

      const options: SpawnSyncOptions = { cwd: config.tmpDir }
      const { stderr, stdout } = spawnSync("git", ["for-each-ref", "--sort=-committerdate", "refs/remotes/", "--format=%(refname) %(authordate:iso8601)"], options)

      if (stderr != null) proto.logInfo(`stderr: ${stderr.toString()}`)

      if (stdout != null) {
        proto.logInfo(`stdout: ${stdout.toString()}`)

        await Promise.all(stdout.toString().split(/\r?\n/).map(async line =>
        {
          if (line == null || line.trim() == "")
            return

          const spacePosition = line.indexOf(" ")
          const date = line.substring(spacePosition + 1)
          const momentDate = moment(date, "YYYY-MM-DD HH:mm:ss Z")
          const branch = line.substring(0, spacePosition).replace("refs/remotes/origin/", "")

          if (!branch.startsWith(branchPrefix)) {
            proto.logInfo(`Not deleting ${branch} as it does not start with ${branchPrefix}`)
            return;
          }

          if (moreThanOneHourAgo(momentDate)) {
            proto.logInfo(`Deleting stale ${branch} with date ${momentDate.toISOString()} as it is older than ${moment().subtract(1, 'hours').toISOString()}`)
            runGitWithArgs(["push", "origin", "--delete", branch], options)
          }
        }
        ));
      }
    } finally {
      // Cleanup the checked out repo. teardownHandler runs before cleanup and there's no guarantee cleanup is enabled.
      // For that reason teardownHandler should always cleanup its tmpDir forcing us to also clean it up here or we risk
      // leaving tmp files around if cleanup is not enabled.
      await rm(config.tmpDir, { recursive: true, force: true })
    }
  }

const teardownHandler = async function() {
  proto.logInfo("In teardown")
  return await rm(config.tmpDir, { recursive: true, force: true })
}

const gitURL = function (): string {
  return `https://${config.personalAccessToken}@dev.azure.com/${config.organization}/${config.repository}/_git/${config.repository}`
}

const runGitWithArgs = function (args: ReadonlyArray<string>, options?: SpawnSyncOptions) {
  const { status, stderr, stdout } = spawnSync("git", args, options)
  if (stdout.toString()) proto.logInfo(`stdout: ${stdout.toString()}`)
  const stderrStr = stderr.toString();
  if (status !== 0 &&
    stderrStr &&
    !stderrStr.includes("Author identity unknown") // This monitor doesn't set up its gitconfig so we can ignore this message
  ) {
    proto.logError(`stderr: ${stderrStr}`)
  }
  return status;
}

const getBranchName = function (): string {
  return `${branchPrefix}${config.runID}`
}

const moreThanOneHourAgo = function (momentDate: moment.Moment) {
  return momentDate.isBefore(moment().subtract(1, 'hours'));
}

const main = async function() {
  await proto.handshake(configCallback)

  let step: string | null = null
  while ((step = await proto.getStep(cleanupHandler, teardownHandler)) != null) {
    proto.logDebug(`Starting step ${step}`)
    await steps[step]()
      .catch(e => proto.sendError(e))
  }
  proto.logInfo('Orchestrator asked me to exit, all done')
  process.exit(0)
}


main();
