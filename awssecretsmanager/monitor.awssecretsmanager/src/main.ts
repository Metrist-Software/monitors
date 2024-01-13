import { Protocol, StepFunction, timed } from '@metrist/protocol'
import { Command } from '@aws-sdk/types'
import { CreateSecretCommand, DeleteSecretCommand, GetSecretValueCommand, ListSecretsCommand, SecretsManagerClient, SecretsManagerClientResolvedConfig } from '@aws-sdk/client-secrets-manager'
import moment = require('moment')

const randString = () => Math.floor(Math.random() * 1000000000).toString(32)
let randomId = ''
const randomPass = randString() + randString() + randString()

const config = {
  region: '',
  idPrefix: ''
}

const proto = new Protocol()
const steps: Record<string, StepFunction> = {}

const teardownHandler = async function() {
}

const cleanupHandler = async function () {
  const client = new SecretsManagerClient({region: config.region})

  const response = await client.send(new ListSecretsCommand({
    Filters: [{
      Key: "name",
      Values: [config.idPrefix]
    }]
  }))
  if (!response?.SecretList) return
  for (const secret of response.SecretList) {
    if (secret.Name?.startsWith(config.idPrefix)) {
      if (secret.CreatedDate! < moment().subtract(1, 'hour').toDate()) {
        proto.logInfo(`Deleting stale secret ${secret.Name} as it was created at ${secret.CreatedDate}`)
        await client.send(new DeleteSecretCommand({
          SecretId: secret.Name,
          ForceDeleteWithoutRecovery: true
        }))
      }
    }
  }
}

async function timeCommand(command: Command<any, any, any, any, SecretsManagerClientResolvedConfig>) {
  const client = new SecretsManagerClient({region: config.region})
  proto.sendTime(await timed(async () => {
    await client.send(command)
  }))
}

steps.CreateSecret = async function() {
  timeCommand(new CreateSecretCommand({
    Name: randomId,
    SecretString: randomPass
  }))
}

steps.GetSecretValue = async function() {
  timeCommand(new GetSecretValueCommand({
    SecretId: randomId
  }))
}

steps.DeleteSecret = async function() {
  timeCommand(new DeleteSecretCommand({
    SecretId: randomId,
    ForceDeleteWithoutRecovery: true
  }))
}


const configCallback = async function(_config: any) {
  config.region = proto.getConfigValue('awssecretsmanager', 'Region') ?? ''
  process.env.AWS_ACCESS_KEY_ID = proto.getConfigValue('awssecretsmanager', 'AwsAccessKeyId') ?? ''
  process.env.AWS_SECRET_ACCESS_KEY = proto.getConfigValue('awssecretsmanager', 'AwsSecretAccessKey') ?? ''
  const environmentTag = proto.getConfigValue('awssecretsmanager', 'EnvironmentTag') ?? ''
  config.idPrefix = '/monitor.awssecretsmanager/' + environmentTag + '/' + config.region + '/'
  randomId = config.idPrefix + randString()
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

main()
