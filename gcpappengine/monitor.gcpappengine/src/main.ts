import { Protocol, StepFunction, run, timeAndSend, timed } from '@metrist/protocol'
import { ServicesClient, VersionsClient, InstancesClient } from '@google-cloud/appengine-admin'
import axios from 'axios'
import { URL } from 'url'

const newVersionId = 'v' + Math.round(Math.random() * 1048560).toString(16)

const proto = new Protocol()
const steps: Record<string, StepFunction> = {}

let isConfigured = false

let env: string
let servicesClient: ServicesClient
let instancesClient: InstancesClient
let versionsClient: VersionsClient

const config = {
  gcpPrivateKey: '',
  projectId: '',
  appUrl: '',
  appZipUrl: '',
  persistentInstanceId: 'v1'
}

function ensureNotNullOrUndefined<T>(arg: T | null | undefined): T {
  if (arg == null) {
    throw new Error('Must not be null')
  }

  return arg
}

function configureIfNeeded() {
  if (isConfigured) return

  config.projectId = ensureNotNullOrUndefined(proto.getConfigValue('gcpappengine', 'ProjectId'))
  config.appZipUrl = ensureNotNullOrUndefined(proto.getConfigValue('gcpappengine', 'AppZipUrl'))

  const hostname = ensureNotNullOrUndefined(proto.getConfigValue('gcpappengine', 'AppHostname'))
  if (hostname) {
    config.appUrl = `https://${hostname}`
  }

  const privateKey = ensureNotNullOrUndefined(proto.getConfigValue('gcpappengine', 'PrivateKey'))
  if (privateKey) {
    config.gcpPrivateKey = Buffer.from(privateKey, 'base64').toString()
  }

  env = ensureNotNullOrUndefined(proto.getConfigValue('gcpappengine', 'EnvironmentTag') ?? process.env['ENVIRONMENT_TAG'])
  const credentials = JSON.parse(config.gcpPrivateKey)
  servicesClient = new ServicesClient({ credentials })
  instancesClient = new InstancesClient({ credentials })
  versionsClient = new VersionsClient({ credentials })

  isConfigured = true
}

const listInstances = async (version: string) => {
  const [instances] = await instancesClient.listInstances({
    parent: `apps/${config.projectId}/services/default/versions/${version}`,
  })

  return instances
}

const migrateTraffic = async (versionId: string) => {
  proto.logInfo(`Migrating traffic to ${versionId}`)
  const [longOp] = await servicesClient.updateService({
    name: `apps/${config.projectId}/services/default`,
    updateMask: {
      paths: [
        'split',
      ],
    },
    service: {
      split: {
        allocations: {
          [versionId]: 1,
        }
      }
    }
  })
  return longOp.promise()
}

steps.AutoScaleUp = async () => {
  configureIfNeeded()

  // Wait for any existing instances to scale down
  let instances = await listInstances(config.persistentInstanceId)
  while(instances.length > 0) {
    await sleep(1000)
    instances = await listInstances(config.persistentInstanceId)
  }

  await timeAndSend(proto, async () => {
    await axios.get(config.appUrl)
  })
}

steps.PingApp = async () => {
  configureIfNeeded()

  // Do an initial ping to make sure the app is up
  await axios.get(config.appUrl)

  // Second ping for telemetry reading
  return timeAndSend(proto, async () => {
    await axios.get(config.appUrl)
  })
}

steps.CreateVersion = async () => {
  configureIfNeeded()

  await timeAndSend(proto, async () => {
    const [longOp] = await versionsClient.createVersion({
      parent: `apps/${config.projectId}/services/default/versions`,
      version: {

        id: newVersionId,
        runtime: 'nodejs18',

        servingStatus: 'SERVING',
        instanceClass: 'B1',
        basicScaling: {
          idleTimeout: {
            seconds: 10
          },
          maxInstances: 1
        },
        entrypoint: {
          shell: 'node ./app.js',
        },
        deployment: {
          zip: {
            sourceUrl: config.appZipUrl,
          }
        },
      }
    })
    await longOp.promise()

    let url = new URL(config.appUrl)
    url.host = `${newVersionId}-dot-${url.host}`

    while(true) {
      const res = await axios.get(url.toString())
      if (res.data === newVersionId) return
      await sleep(1000)
    }
  })
}

steps.MigrateTraffic = async () => {
  proto.logDebug('Updating Service')

  const time = await timed(async () => {
    await migrateTraffic(newVersionId)

    while (true) {
      const res = await axios.get(config.appUrl)
      if (res.data === newVersionId) return
      await sleep(500)
    }
  })

  // Return traffic to the original version to allow deleting the newly created one
  await migrateTraffic(config.persistentInstanceId)

  proto.sendTime(time)
}

steps.AutoScaleDown = async () => {
  configureIfNeeded()

  // Make sure an instance is running before waiting to scale down
  const activeVersion = (await axios.get(config.appUrl)).data

  proto.logInfo(`Waiting for ${activeVersion} to scale down`)

  await timeAndSend(proto, async () => {
    while(true) {
      const instances = await listInstances(activeVersion)

      proto.logInfo('Scaling got ' + instances.length + ' instances')

      if (instances.length === 0) return
      await sleep(1000)
    }
  })
}

steps.DestroyVersion = async () => {
  configureIfNeeded()

  await timeAndSend(proto, async () => {
    const [longOp, op] = await versionsClient.deleteVersion({
      name: `apps/${config.projectId}/services/default/versions/${newVersionId}`
    })

    await longOp.promise()
  })
}

async function cleanupHandler() {
}

async function teardownHandler() {
  // Migrate traffic back to the persistent instance
  await migrateTraffic(config.persistentInstanceId)

  // Delete all deployed versions other than the persistent one
  const [versions] = await versionsClient.listVersions({
    parent: `apps/${config.projectId}/services/default`
  })

  const ops = await Promise.all(versions.map(version => {
    if (version.id && version.id !== config.persistentInstanceId) {
      proto.logInfo(`Deleting version ${version.id}`)
      return versionsClient.deleteVersion({
        name: `apps/${config.projectId}/services/default/versions/${version.id}`
      })
    } else {
      return null
    }
  })
  .filter(Boolean))

  await Promise.all(ops.map(op => {
    if (!op) return null
    return op[0].promise()
  }))
}

async function sleep(ms: number) {
  return new Promise(resolve => setTimeout(resolve, ms))
}

async function main() {
  await run(proto, steps, cleanupHandler, teardownHandler)
}

main()
