import { AddUserToGroupCommand, AttachUserPolicyCommand, CreateGroupCommand, CreateUserCommand, DeleteGroupCommand, DeleteUserCommand, DetachUserPolicyCommand, IAMClient, IAMClientResolvedConfig, RemoveUserFromGroupCommand, ListGroupsCommand, Group, ListUsersCommand, User, ListGroupsForUserCommand } from '@aws-sdk/client-iam'
import { Command } from '@aws-sdk/types'
import { run, timed, Protocol, StepFunction } from '@metrist/protocol'
import moment = require('moment')


const proto = new Protocol()
const steps: Record<string, StepFunction> = {}

let configured = false
const config = {
  namespace: 'NO-NAMESPACE',
  region: 'NO-REGION'
}

function withSuffix(name: string) {
  configIfNeeded()
  return `${name}-${config.namespace}-${(+new Date()).toString(36)}`
}

let userName = ''
function getUserName() {
  if (userName == '') {
    userName = withSuffix('testuser')
  }
  return userName
}

let groupName = ''
function getGroupName() {
  if (groupName == '') {
    groupName = withSuffix('testgroup')
  }
  return groupName
}

const path = '/AwsIamMonitorTestUsers/'
const groupPath = '/AwsIamMonitorTestGroups/'

const teardownHandler = async function () {}

const cleanupHandler = async function () {
  configIfNeeded()
  await cleanupUsers().catch(e => { proto.logError(`Error running cleanupUsers for IAM monitor. ${e}`) })
  await cleanupGroups().catch(e => { proto.logError(`Error running cleanupGroups for IAM monitor. ${e}`) })
  proto.logInfo("Cleanup done")
}


async function cleanupUsers() {
  const iamClient = new IAMClient({region: config.region})

  const responseUsers = await iamClient.send(new ListUsersCommand({ PathPrefix: `${path}` }))
  if (!responseUsers?.Users) return
  for (const user of responseUsers.Users) {
    if (user != null && user.UserName != null) {
      if (shouldDeleteTestUser(user)) {
        proto.logInfo(`Deleting stale user ${user.UserName} as it was created at ${user.CreateDate}`)

        await maybeRemoveUserFromPolicy(iamClient, user.UserName)
          .catch(e => proto.logWarning(`Cleanup could not remove policy for ${user.UserName}. Error: ${e}`))

        await maybeRemoveUserFromGroups(iamClient, user.UserName)
          .catch(e => proto.logWarning(`Cleanup could not remove groups for ${user.UserName}. Error: ${e}`))

        await iamClient.send(new DeleteUserCommand({
          UserName: user.UserName
        }))
          .catch(e => proto.logWarning(`Cleanup could not delete user ${user.UserName}. Error: ${e}`))
      } else {
        proto.logInfo(`Not deleting user with name ${user.UserName}`)
      }
    }
  }
}

async function maybeRemoveUserFromGroups(iamClient: IAMClient, username: string) {
  // Must be removed from policy and group before being deleted.
  var groupResponse = await iamClient.send(new ListGroupsForUserCommand({
    UserName: username
  }))
  if (!groupResponse?.Groups) return
  for (const group of groupResponse.Groups) {
    await iamClient.send(new RemoveUserFromGroupCommand({
      GroupName: group.GroupName,
      UserName: username
    }))
  }
}

async function maybeRemoveUserFromPolicy(iamClient: IAMClient, username: string) {
  // If they aren't in it, this will throw but that's ok.
  // Must be removed from policy and group before being deleted.
  await iamClient.send(new DetachUserPolicyCommand({
    UserName: username,
    PolicyArn: getPolicyArn()
  })).catch(e => { })
}

async function cleanupGroups() {
  const iamClient = new IAMClient({region: config.region})

  const responseGroups = await iamClient.send(new ListGroupsCommand({ PathPrefix: `${groupPath}` }))
  if (!responseGroups.Groups) return
  for (const group of responseGroups.Groups) {
    if (group != null && group.GroupName != null) {
      if (shouldDeleteTestGroup(group)) {
        proto.logInfo(`Deleting stale group ${group.GroupName} as it was created at ${group.CreateDate}`)
        await iamClient.send(new DeleteGroupCommand({
          GroupName: group.GroupName
        })).catch(e => proto.logWarning(`Cleanup could not delete group ${group.GroupName}. Error: ${e}`))
      } else {
        proto.logInfo(`Not deleting group with name ${group.GroupName}`)
      }
    }
  }
}

function shouldDeleteTestUser(user: User): boolean {
  return (user.CreateDate!) < moment().subtract(1, 'hour').toDate()
    && user.UserName!.startsWith(`testuser-${config.namespace}`)
}

function shouldDeleteTestGroup(group: Group): boolean {
  return (group.CreateDate!) < moment().subtract(1, 'hour').toDate()
    && group.GroupName!.startsWith(`testgroup-${config.namespace}`)
}

function getPolicyArn() : string {
  configIfNeeded()
  return `arn:aws:iam::907343345003:policy/AwsIamMonitorTestPolicies/${config.namespace}-${config.region}-awsiam-testpolicy`
}

async function timeCommand(command: Command<any, any, any, any, IAMClientResolvedConfig>) {
  configIfNeeded()
  const iamClient = new IAMClient({region: config.region})
  proto.sendTime(await timed(async () => {
    await iamClient.send(command)
  }))
}

steps.CreateUser = async function () {
  timeCommand(new CreateUserCommand({
    Path: path,
    UserName: getUserName()
  }))
}

steps.CreateGroup = async function () {
  timeCommand(new CreateGroupCommand({
    Path: groupPath,
    GroupName: getGroupName()
  }))
}

steps.AddUserToGroup = async function () {
  timeCommand(new AddUserToGroupCommand({
    GroupName: getGroupName(),
    UserName: getUserName()
  }))
}

steps.RemoveUserFromGroup = async function () {
  timeCommand(new RemoveUserFromGroupCommand({
    GroupName: getGroupName(),
    UserName: getUserName()
  }))
}

steps.DeleteGroup = async function () {
  timeCommand(new DeleteGroupCommand({
    GroupName: getGroupName()
  }))
}

steps.AttachPolicy = async function () {
  timeCommand(new AttachUserPolicyCommand({
    UserName: getUserName(),
    PolicyArn: getPolicyArn()
  }))
}

steps.DetachPolicy = async function () {
  timeCommand(new DetachUserPolicyCommand({
    UserName: getUserName(),
    PolicyArn: getPolicyArn()
  }))
}


steps.DeleteUser = async function () {
  timeCommand(new DeleteUserCommand({
    UserName: getUserName()
  }))
}

const configIfNeeded = function () {
  if (!configured) {
    process.env.AWS_ACCESS_KEY_ID = proto.getConfigValue('awsiam', 'AWSAccessKeyId') ?? ''
    process.env.AWS_SECRET_ACCESS_KEY = proto.getConfigValue('awsiam', 'AWSSecretAccessKey') ?? ''
    config.namespace = proto.getConfigValue('awsiam', 'Namespace') ?? process.env.ENVIRONMENT_TAG ?? 'local'
    config.region = proto.getConfigValue('awsiam', 'AWSRegion') ?? process.env.ORCHESTRATOR_REGION ?? 'local-dev'
    configured = true
  }
}

async function main() {
  await run(proto, steps, cleanupHandler, teardownHandler)
}

main()
